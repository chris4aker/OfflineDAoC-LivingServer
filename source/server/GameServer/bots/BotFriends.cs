using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;
using DOL.GS.Friends;
using DOL.GS.PacketHandler;

namespace DOL.GS;

/// <summary>
/// Autonomous gamebots on the friends list (/friend). Kept out of upstream's FriendsManager and /friend command,
/// which only call in here, so they stay easy to merge.
/// - A bot is found by its exact name (any case), as /send finds it: online through AutonomousBotRegistry.TryGetOnline,
///   offline ("Parked" in the launcher) through its offline_world_bots row. Same realm only, never a retired bot.
/// - A player character of the same name always wins: that name is never treated as a bot.
/// - The list itself stays names in DbCoreCharacter.SerializedFriendsList, as for players.
/// </summary>
public static class BotFriends
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotFriends));

    /// <summary>/friend name, when no player was found: the bot's own name.</summary>
    public static bool TryResolveName(string name, eRealm realm, out string botName) =>
        TryResolveName(GameServer.Database, name, realm, out botName);

    public static bool TryResolveName(IObjectDatabase database, string name, eRealm realm, out string botName)
    {
        botName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        if (HasCharacter(database, name))
            return false;

        botName = AutonomousBotRegistry.TryGetOnline(name, realm, out GameBot bot) ? bot.Name : FindRecord(database, name, realm)?.Name;
        return botName != null;
    }

    /// <summary>The bot's offline_world_bots row: exact name in any case, same realm, not retired.</summary>
    public static OfflineWorldBotRecord FindRecord(IObjectDatabase database, string name, eRealm realm)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = name.Trim();
        return database.SelectObjects<OfflineWorldBotRecord>(DB.Column("Name").IsLike(name)
                .And(DB.Column("Realm").IsEqualTo((int)realm))
                .And(DB.Column("IsRetired").IsEqualTo(false)))
            .FirstOrDefault(record => IsFriendRecord(record, name, realm));
    }

    public static bool IsFriendRecord(OfflineWorldBotRecord record, string name, eRealm realm) =>
        record != null && !record.IsRetired && record.Realm == (int)realm &&
        string.Equals(record.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The offline status of a bot just added (FriendsManager.AddFriendToPlayerList); null if it is no bot.</summary>
    public static FriendStatus OfflineStatus(string name, eRealm realm)
    {
        OfflineWorldBotRecord record = FindRecord(GameServer.Database, name, realm);
        return record == null ? null : ToStatus(record);
    }

    /// <summary>The offline statuses of the friends that are no player character, for the /friend window
    /// (FriendsManager.AddPlayerFriendsListToCache).</summary>
    public static IEnumerable<FriendStatus> OfflineStatuses(IEnumerable<string> friends, IEnumerable<string> characterNames, eRealm realm) =>
        OfflineStatuses(GameServer.Database, friends, characterNames, realm);

    public static FriendStatus[] OfflineStatuses(IObjectDatabase database, IEnumerable<string> friends, IEnumerable<string> characterNames, eRealm realm)
    {
        string[] others = friends.Except(characterNames, StringComparer.OrdinalIgnoreCase).ToArray();
        if (others.Length == 0)
            return [];

        return database.SelectObjects<OfflineWorldBotRecord>(DB.Column("Name").IsIn(others))
            .Where(record => IsFriendRecord(record, record.Name, realm))
            .Select(ToStatus)
            .ToArray();
    }

    public static FriendStatus ToStatus(OfflineWorldBotRecord record) =>
        new(record.Name, record.Level, record.ClassId, LastPlayed(record));

    /// <summary>When the bot was last in the world: the later of its last save and its last status update.</summary>
    public static DateTime LastPlayed(OfflineWorldBotRecord record)
    {
        DateTime last = DateTime.MinValue;
        foreach (string stamp in new[] { record.LastSavedUtc, record.LastUpdateUtc })
        {
            if (DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime when) && when.ToUniversalTime() > last)
                last = when.ToUniversalTime();
        }

        return last == DateTime.MinValue ? last : last.ToLocalTime();
    }

    /// <summary>The friends list sent on entering the game (FriendsManager.SendPlayerFriendsList): the online players plus
    /// the online bots, in one AddFriend packet. A second packet would read as "just entered the game" for every bot.</summary>
    public static string[] WithOnlineBots(GamePlayer player, string[] onlinePlayers)
    {
        string[] bots = OnlineBots(player, player.GetFriends().Except(onlinePlayers, StringComparer.OrdinalIgnoreCase))
            .Select(bot => bot.Name)
            .ToArray();
        return bots.Length == 0 ? onlinePlayers : onlinePlayers.Concat(bots).ToArray();
    }

    /// <summary>/friend window (FriendsManager.SendPlayerFriendsSocial): an "F" line for each online bot among the friends
    /// not listed yet, as upstream writes one for an online player. A listed bot leaves <paramref name="unlisted"/>, so it
    /// is not shown as offline too.</summary>
    public static int SendOnlineBots(GamePlayer player, List<string> unlisted, int index)
    {
        foreach (GameBot bot in OnlineBots(player, unlisted.ToArray()))
        {
            unlisted.RemoveAll(name => string.Equals(name, bot.Name, StringComparison.OrdinalIgnoreCase));
            player.Out.SendMessage(SocialLine(index++, bot.Name, bot.Level, bot.CharacterClass?.ID ?? 0, bot.CurrentZone?.Description ?? string.Empty),
                eChatType.CT_SocialInterface, eChatLoc.CL_SystemWindow);
        }

        return index;
    }

    /// <summary>A friend's line in the social window, in upstream's format: F,place,name,level,class,"zone or last played".</summary>
    public static string SocialLine(int index, string name, int level, int classId, string where) =>
        string.Format("F,{0},{1},{2},{3},\"{4}\"", index, name, level, classId, where);

    private static GameBot[] OnlineBots(GamePlayer player, IEnumerable<string> names)
    {
        GameBot[] bots = names
            .Select(name => AutonomousBotRegistry.TryGetOnline(name, player.Realm, out GameBot bot) ? bot : null)
            .Where(bot => bot != null)
            .ToArray();
        if (bots.Length == 0)
            return bots;

        HashSet<string> characters = CharacterNames(GameServer.Database, bots.Select(bot => bot.Name));
        return bots.Where(bot => !characters.Contains(bot.Name)).ToArray();
    }

    /// <summary>A bot entered the world (AutonomousBotRegistry.Register): the players who have it on their list get the
    /// AddFriend update, as FriendsManager sends when a player enters the game. The clients show the notice.</summary>
    public static void OnBotOnline(GameBot bot) => Notify(bot, true);

    /// <summary>A bot left the world (AutonomousBotRegistry.Unregister): RemoveFriend, as for a player quitting.</summary>
    public static void OnBotOffline(GameBot bot) => Notify(bot, false);

    private static void Notify(GameBot bot, bool online)
    {
        // A friends notice must never stop a bot's login or logout.
        try
        {
            if (bot == null || bot.IsTemporaryGroupHelper || bot.IsEpicSpecialForce || ClientService.Instance == null)
                return;

            GamePlayer[] listeners = ClientService.Instance.GetPlayers()
                .Where(player => IsListener(player.Realm, player.GetFriends(), bot.Realm, bot.Name))
                .ToArray();
            if (listeners.Length == 0 || HasCharacter(GameServer.Database, bot.Name))
                return;

            string[] update = [bot.Name];
            foreach (GamePlayer player in listeners)
            {
                if (online)
                    player.Out.SendAddFriends(update);
                else
                    player.Out.SendRemoveFriends(update);
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"BotFriends: could not tell friends that {bot?.Name} went {(online ? "online" : "offline")}", exception);
        }
    }

    public static bool IsListener(eRealm playerRealm, IEnumerable<string> friends, eRealm botRealm, string botName) =>
        playerRealm == botRealm && friends.Contains(botName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a player character has this name (any case): such a name is never a bot.</summary>
    public static bool HasCharacter(IObjectDatabase database, string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        database.SelectObjects<DbCoreCharacter>(DB.Column("Name").IsLike(name.Trim()))
            .Any(character => string.Equals(character.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static HashSet<string> CharacterNames(IObjectDatabase database, IEnumerable<string> names)
    {
        string[] wanted = names.ToArray();
        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);
        if (wanted.Length == 0)
            return found;

        foreach (DbCoreCharacter character in database.SelectObjects<DbCoreCharacter>(DB.Column("Name").IsIn(wanted)))
            found.Add(character.Name);
        return found;
    }
}
