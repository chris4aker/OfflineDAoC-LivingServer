using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What the realm knows a real player did (AutonomousPlayerDeeds): counts, the best named kill, a nickname.</summary>
[DataTable(TableName = "offline_player_reputation")]
public sealed class OfflinePlayerReputationRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflinePlayerReputationRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Unique = true, Varchar = 80)] public string PlayerKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 64)] public string PlayerName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false)] public int NamedKills { get; set; }
    [DataElement(AllowDbNull = false)] public int NamedSolo { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string BestNamed { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int BestNamedLevel { get; set; }
    [DataElement(AllowDbNull = false)] public int RvrKills { get; set; }
    [DataElement(AllowDbNull = false)] public int Streaks { get; set; }
    [DataElement(AllowDbNull = false)] public int Ganked { get; set; }
    [DataElement(AllowDbNull = false)] public int SillyDeaths { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 32)] public string Nickname { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 16)] public string NicknameKind { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastDeedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Deeds (bot_living_deeds, docs/bot-voices/TALKING-UPDATE.md section 12): when a real player solos or helps kill a named
/// monster, goes on a kill streak in the frontier, gets zerged, or dies to something silly, a gamebot of their realm that
/// was there tells /faction 2-10 minutes later and others react. Only what a bot really saw is told. The deeds add up to a
/// reputation (saved in offline_player_reputation) that bots who were not there bring up now and then, and to a
/// nickname once a kind of deed is common enough. Companion bots never witness; beggars don't gossip.
/// </summary>
public static class AutonomousPlayerDeeds
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.BOT_LIVING_DEEDS;

    public const int WitnessRadius = 3_000;
    public const int GankCount = 3;                  // enemies around a player's death that make it a gank (4 until 2026-10-27: a death to three never counted)
    public const int StreakKills = 3;                // enemy kills within StreakWindow
    public static readonly TimeSpan StreakWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan KindRest = TimeSpan.FromMinutes(15);     // one telling of a kind per player this often
    public static readonly TimeSpan SoloRest = TimeSpan.FromMinutes(5);      // and of any solo named kill
    public const int NamedSoloForNickname = 3, RvrKillsForNickname = 25, SillyDeathsForNickname = 4;

    internal static readonly LivingServerTable<string, OfflinePlayerReputationRecord> Reputations =
        new("player reputation", row => row.PlayerKey, StringComparer.OrdinalIgnoreCase);

    private static Timer _flushTimer;

    public static string KeyOf(eRealm realm, string name) => $"{(int)realm}|{name}".ToLowerInvariant();

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Reputations.Load(GameServer.Database.SelectAllObjects<OfflinePlayerReputationRecord>());
        }
        catch (Exception exception)
        {
            Log.Warn("Deeds: could not read player reputations", exception);
        }
        _flushTimer = new Timer(_ => Reputations.Flush(), null, 60_000, 60_000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        Reputations.Flush(wait: true);
    }

    #region Pure rules (unit tested)

    public enum Kind { None, NamedSolo, NamedGroup, RvrStreak, Ganked, SillyDeath, Fell }

    public static string TopicOf(Kind kind) => kind switch
    {
        Kind.NamedSolo => "named_solo",
        Kind.NamedGroup => "named_group",
        Kind.RvrStreak => "rvr_streak",
        Kind.Ganked => "ganked",
        Kind.SillyDeath => "silly_death",
        Kind.Fell => "fell",
        _ => null,
    };

    /// <summary>A named monster: a monster of no realm whose name starts with a capital (DAoC's named mobs). Pure.</summary>
    public static bool IsNamed(string name, bool noRealm, bool isBotOrGuard) =>
        noRealm && !isBotOrGuard && !string.IsNullOrWhiteSpace(name) && char.IsUpper(name.Trim()[0]);

    /// <summary>A named kill worth telling: blue or harder, alone or in a group. Grey and green named are not news. Pure.
    /// (Solo needed yellow until 2026-10-27; a level 50 rarely finds a yellow named, so it never happened in game.)</summary>
    public static Kind NamedKill(int conToPlayer, bool solo) =>
        conToPlayer < -1 ? Kind.None : solo ? Kind.NamedSolo : Kind.NamedGroup;

    /// <summary>A player's death: a fall (no killer), a silly death (green or grey monster), a gank (3+ enemies around). Pure.</summary>
    public static Kind Death(bool noKiller, bool killerIsMonster, int killerConToPlayer, bool killerIsEnemy, int enemiesAround) =>
        noKiller ? Kind.Fell
        : killerIsMonster && killerConToPlayer <= -2 ? Kind.SillyDeath
        : killerIsEnemy && enemiesAround >= GankCount ? Kind.Ganked
        : Kind.None;

    private static readonly string[] NamedNicknames = ["the Slayer", "Bossbane", "the Hunter", "the Headhunter", "Namedbane"];
    private static readonly string[] RvrNicknames = ["the Butcher", "Redhand", "the Reaper", "Gatecrasher", "the Wolf"];
    private static readonly string[] SillyNicknames = ["Facefloor", "the Unlucky", "Corpsewalker", "Rugburn", "the Clumsy"];

    /// <summary>
    /// The nickname a reputation earns: heroic kinds (named solos, enemy kills) over silly deaths, and a heroic one replaces
    /// a silly one. Stable for a name (the same player always gets the same word). Null when nothing changes. Pure.
    /// </summary>
    public static (string Nickname, string Kind)? Earned(string playerName, int namedSolo, int rvrKills, int sillyDeaths,
        string currentKind)
    {
        int seed = (int)(AutonomousBotPersonality.Mix(StableHash(playerName), 31) % 1000);
        string kind = namedSolo >= NamedSoloForNickname && (namedSolo * 8 >= rvrKills || rvrKills < RvrKillsForNickname) ? "named"
            : rvrKills >= RvrKillsForNickname ? "rvr"
            : sillyDeaths >= SillyDeathsForNickname ? "silly"
            : null;
        // A heroic nickname is for good: it never switches to the other heroic kind, nor back to a silly one.
        if (kind == null || kind == currentKind || currentKind is "named" or "rvr")
            return null;
        string[] pool = kind switch { "named" => NamedNicknames, "rvr" => RvrNicknames, _ => SillyNicknames };
        return (pool[seed % pool.Length], kind);
    }

    private static long StableHash(string text)
    {
        unchecked
        {
            long hash = 1469598103934665603;
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
                hash = (hash ^ c) * 1099511628211;
            return hash;
        }
    }

    #endregion

    #region Events

    private static readonly ConcurrentDictionary<string, List<long>> KillTimes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> LastTold = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Any living's death (AutonomousLivingServer.OnLivingDying).</summary>
    public static void OnDeath(GameLiving dead, GameObject killer)
    {
        if (!On || !AutonomousBotVoices.Enabled || dead == null)
            return;
        try
        {
            if (dead is GamePlayer player)
                OnPlayerDeath(player, killer as GameLiving);
            else if (PlayerOf(killer) is { } slayer)
            {
                if (dead.Realm != eRealm.None && dead.Realm != slayer.Realm && dead is GameBot or GamePlayer)
                    OnEnemyKilled(slayer, dead);
                else if (dead is GameNPC npc && IsNamed(npc.Name, npc.Realm == eRealm.None, npc is GameBot or GameKeepGuard || (npc.Flags & GameNPC.eFlags.PEACE) != 0))
                    OnNamedKilled(slayer, npc);
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Deeds: a death could not be read", exception);
        }
    }

    private static GamePlayer PlayerOf(GameObject killer) => killer switch
    {
        GamePlayer player => player,
        GameNPC { Brain: IControlledBrain controlled } => controlled.GetPlayerOwner(),
        _ => null,
    };

    private static void OnNamedKilled(GamePlayer player, GameNPC npc)
    {
        bool solo = player.Group == null || !player.Group.GetMembersInTheGroup()
            .Any(member => member != player && member.IsAlive && member.IsWithinRadius(player, 4_000));
        Kind kind = NamedKill(player.GetConLevel(npc), solo);
        if (kind == Kind.None)
            return;
        Record(player, row =>
        {
            row.NamedKills++;
            if (!solo)
                return;
            row.NamedSolo++;
            if (npc.Level >= row.BestNamedLevel)
            {
                row.BestNamed = npc.Name;
                row.BestNamedLevel = npc.Level;
            }
        }, kind, npc, npc.Name, null);
    }

    private static void OnEnemyKilled(GamePlayer player, GameLiving enemy)
    {
        long now = GameLoop.GameLoopTime;
        List<long> times = KillTimes.GetOrAdd(player.Name, _ => new List<long>());
        int recent;
        lock (times)
        {
            times.Add(now);
            times.RemoveAll(at => now - at > (long)StreakWindow.TotalMilliseconds);
            recent = times.Count;
        }
        Kind kind = recent >= StreakKills ? Kind.RvrStreak : Kind.None;
        Record(player, row =>
        {
            row.RvrKills++;
            if (kind == Kind.RvrStreak)
                row.Streaks++;
        }, kind, player, null, recent.ToString(), AutonomousLivingServer.RealmPlural(enemy.Realm));
    }

    private static void OnPlayerDeath(GamePlayer player, GameLiving killer)
    {
        bool noKiller = killer == null || killer == player;
        bool monster = killer is GameNPC npc && npc.Realm == eRealm.None && killer is not GameBot;
        bool enemy = killer != null && killer.Realm != eRealm.None && killer.Realm != player.Realm;
        int enemies = enemy ? player.GetPlayersInRadius(2_000).Count(other => other.Realm == killer.Realm && other.IsAlive) +
                              player.GetNPCsInRadius(2_000).OfType<GameBot>().Count(bot => bot.Realm == killer.Realm && bot.IsAlive) : 0;
        Kind kind = Death(noKiller, monster, monster ? player.GetConLevel(killer) : 0, enemy, enemies);
        if (kind == Kind.None)
        {
            // Why a death was no deed, for in-game checks (2026-10-27: a death to three enemy bots left no trace).
            Log.Debug($"PLAYER_DEED_SKIP player=\"{player.Name}\" death killer=\"{killer?.Name}\" enemies={enemies} con={(monster ? player.GetConLevel(killer) : 0)}");
            return;
        }
        Record(player, row =>
        {
            if (kind == Kind.Ganked)
                row.Ganked++;
            else
                row.SillyDeaths++;
        }, kind, player, killer?.Name, enemies.ToString(), enemy ? AutonomousLivingServer.RealmPlural(killer.Realm) : null);
    }

    #endregion

    #region Telling

    private static void Record(GamePlayer player, Action<OfflinePlayerReputationRecord> change, Kind kind, GameObject where,
        string target, string count, string enemy = null)
    {
        GameBot witness = WitnessOf(player, where);
        // Only what a bot saw counts: an unseen deed (an enemy kill too) never becomes reputation.
        if (witness == null)
        {
            Log.Debug($"PLAYER_DEED_SKIP player=\"{player.Name}\" kind={kind} reason=no_witness");
            return;
        }
        OfflinePlayerReputationRecord row = Reputations.Change(KeyOf(player.Realm, player.Name),
            () => new OfflinePlayerReputationRecord { PlayerKey = KeyOf(player.Realm, player.Name), PlayerName = player.Name, Realm = (int)player.Realm },
            row =>
            {
                change(row);
                row.LastDeedUtc = DateTime.UtcNow.ToString("O");
            });
        if (kind == Kind.None)
            return;
        long now = GameLoop.GameLoopTime;
        // Each kind at most every 15 minutes per player; a solo named kill per monster, and at most every 5 minutes.
        string restKey = kind == Kind.NamedSolo ? $"{player.Name}|{kind}|{target}" : $"{player.Name}|{kind}";
        if (LastTold.TryGetValue(restKey, out long last) && now - last < (long)KindRest.TotalMilliseconds ||
            kind == Kind.NamedSolo && LastTold.TryGetValue(player.Name + "|solo", out long lastSolo) && now - lastSolo < (long)SoloRest.TotalMilliseconds)
            return;
        LastTold[restKey] = now;
        if (kind == Kind.NamedSolo)
            LastTold[player.Name + "|solo"] = now;
        Log.Info($"PLAYER_DEED player=\"{player.Name}\" kind={TopicOf(kind)} target=\"{target}\" witness=\"{witness.Name}\"");

        var slots = SlotsOf(player, row);
        slots["place"] = AutonomousLivingServer.PlaceOf(player);
        if (target != null) slots["target"] = target;
        if (count != null) slots["count"] = count;
        if (enemy != null) slots["enemy"] = enemy;
        Tell(witness, player, TopicOf(kind), slots, 120_000 + Random.Shared.Next(480_000), TellTries);

        (string Nickname, string Kind)? earned = Earned(player.Name, row.NamedSolo, row.RvrKills, row.SillyDeaths, row.NicknameKind);
        if (earned is { } nickname)
        {
            Reputations.Change(row.PlayerKey, () => row, r =>
            {
                r.Nickname = nickname.Nickname;
                r.NicknameKind = nickname.Kind;
            });
            Log.Info($"PLAYER_NICKNAME player=\"{player.Name}\" nickname=\"{nickname.Nickname}\"");
            var named = SlotsOf(player, row);
            named["nickname"] = nickname.Nickname;
            Tell(witness, player, "nickname", named, 15 * 60_000 + Random.Shared.Next(10 * 60_000), TellTries);
        }
    }

    /// <summary>
    /// GM test (/lstest deed): a bot near the player tells /faction about a deed of that kind within about 10 seconds, as if
    /// it had seen it. The player's reputation counts are not changed. False with the reason when no bot is near to tell it.
    /// </summary>
    public static bool TellForTest(GamePlayer player, Kind kind, string target, out string why)
    {
        why = null;
        GameBot witness = WitnessOf(player, null);
        if (witness == null)
        {
            why = "no gamebot of your realm is within 3,000 units to have seen it";
            return false;
        }
        Reputations.TryGet(KeyOf(player.Realm, player.Name), out OfflinePlayerReputationRecord row);
        var slots = SlotsOf(player, row);
        slots["target"] = target ?? "a named monster";
        slots["count"] = "5";
        slots["enemy"] = AutonomousLivingServer.RealmPlural(player.Realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion);
        Log.Info($"PLAYER_DEED player=\"{player.Name}\" kind={TopicOf(kind)} target=\"{target}\" witness=\"{witness.Name}\" forced=lstest");
        Tell(witness, player, TopicOf(kind), slots, 5_000, TellTries);
        why = witness.Name;
        return true;
    }

    private static Dictionary<string, string> SlotsOf(GamePlayer player, OfflinePlayerReputationRecord row)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = player.Name,
            ["player_class"] = player.CharacterClass?.Name?.ToLowerInvariant(),
            ["place"] = AutonomousLivingServer.PlaceOf(player),
        };
        if (!string.IsNullOrEmpty(row?.Nickname))
            slots["nickname"] = row.Nickname;
        return slots;
    }

    /// <summary>A gamebot of the player's realm that saw it: a groupmate first, then any bot within 3,000 units.</summary>
    private static GameBot WitnessOf(GamePlayer player, GameObject where)
    {
        bool Fits(GameBot bot) => bot != null && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && bot.IsAlive &&
                                  bot.Realm == player.Realm && bot.DatabaseID > 0 && !AutonomousBeggars.Is(bot);
        GameObject at = where ?? player;
        GameBot mate = player.Group?.GetMembersInTheGroup().OfType<GameBot>()
            .Where(bot => Fits(bot) && bot.CurrentRegionID == at.CurrentRegionID && bot.IsWithinRadius(at, WitnessRadius))
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (mate != null)
            return mate;
        return at.GetNPCsInRadius(WitnessRadius).OfType<GameBot>().Where(Fits).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
    }

    /// <summary>How many times a deed is retried while its witness is busy or a /faction scene plays.</summary>
    public const int TellTries = 12;

    /// <summary>
    /// The wait before trying a deed again: a witness that is only fighting or casting is back in a few seconds, a /faction
    /// scene takes minutes. Pure. (In-game check 2026-10-28: the witness was mid-pull, and the old two-minute wait made the
    /// deed land long after the moment.)
    /// </summary>
    public static int RetryDelayMs(bool sceneRunning) => sceneRunning ? 60_000 : 15_000;

    /// <summary>
    /// The witness tells /faction after a delay (retried while it is busy or a debate runs), and one to three bots react.
    /// A witness that left the game is replaced by another gamebot near the player, if one is there.
    /// </summary>
    private static void Tell(GameBot witness, GamePlayer player, string topic, Dictionary<string, string> slots, int delayMs, int tries)
    {
        eRealm realm = player.Realm;
        // On the player's timer, so a witness that leaves the game doesn't take the deed with it.
        AutonomousLivingServer.Later(player, delayMs, null, () =>
        {
            if (!AutonomousBotChatCoordinator.HasFactionAudience(realm))
                return;
            if (!AutonomousLivingServer.IsSpeaker(witness))
            {
                // In-game check 2026-10-28: the witness ported away and logged out before it could tell anyone.
                witness = WitnessOf(player, null);
                if (witness == null)
                    return;
            }
            bool scene = AutonomousBotDebates.IsRunning(realm);
            if (!AutonomousLivingServer.CanTalk(witness) || scene)
            {
                if (tries > 1)
                    Tell(witness, player, topic, slots, RetryDelayMs(scene), tries - 1);
                return;
            }
            if (!AutonomousBotVoices.TryEventLine(witness, "deed", out string line, topic, slots))
                return;
            AutonomousBotChatCoordinator.BroadcastFaction(witness.Name, realm, line);
            AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), witness, line, "chat");
            React(realm, witness, player, topic, slots);
        });
    }

    private static void React(eRealm realm, GameBot teller, GamePlayer player, string topic, Dictionary<string, string> slots)
    {
        GameBot[] reactors = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != teller && bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !AutonomousBeggars.Is(bot) &&
                          !(bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE)))
            .OrderBy(_ => Random.Shared.Next()).Take(1 + Random.Shared.Next(3)).ToArray();
        int due = 0;
        foreach (GameBot bot in reactors)
        {
            due += 3_000 + Random.Shared.Next(9_000);
            GameBot speaker = bot;
            AutonomousLivingServer.Later(speaker, due, () => AutonomousLivingServer.CanTalk(speaker), () =>
            {
                if (AutonomousBotVoices.TryEventLine(speaker, "deed_reply", out string line, topic, slots))
                    AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
            });
        }
    }

    #endregion

    #region Reputation talk (bots who weren't there)

    private static readonly ConcurrentDictionary<eRealm, long> NextRepTalk = new();
    public const long RepTalkMinMs = 50 * 60_000, RepTalkSpreadMs = 50 * 60_000;

    /// <summary>The topics a reputation gives second-hand talk, with their slots. Pure.</summary>
    public static List<(string Topic, string Target, string Count)> RepTopics(OfflinePlayerReputationRecord row)
    {
        var topics = new List<(string, string, string)>();
        if (row == null)
            return topics;
        if (row.NamedSolo > 0 && !string.IsNullOrEmpty(row.BestNamed))
            topics.Add(("rep_named", row.BestNamed, null));
        if (row.RvrKills >= 10)
            topics.Add(("rep_rvr", null, row.RvrKills.ToString()));
        if (row.SillyDeaths >= 3)
            topics.Add(("rep_silly", null, row.SillyDeaths.ToString()));
        return topics;
    }

    /// <summary>Every 50-100 minutes per realm, a bot that wasn't there mentions an online player's reputation.</summary>
    public static void Pulse(long now)
    {
        if (!On || !AutonomousBotVoices.Enabled)
            return;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            List<GamePlayer> players = ClientService.Instance.GetPlayersOfRealm(realm);
            if (players.Count == 0)
                continue;
            long next = NextRepTalk.GetOrAdd(realm, _ => now + RepTalkMinMs / 2 + Random.Shared.Next((int)(RepTalkSpreadMs / 2)));
            if (now < next || AutonomousBotDebates.IsRunning(realm))
                continue;
            NextRepTalk[realm] = now + RepTalkMinMs + Random.Shared.Next((int)RepTalkSpreadMs);
            foreach (GamePlayer player in players.OrderBy(_ => Random.Shared.Next()))
            {
                if (!Reputations.TryGet(KeyOf(realm, player.Name), out OfflinePlayerReputationRecord row))
                    continue;
                var topics = RepTopics(row);
                if (topics.Count == 0)
                    continue;
                (string topic, string target, string count) = topics[Random.Shared.Next(topics.Count)];
                GameBot speaker = AutonomousBotRegistry.Snapshot()
                    .Where(bot => bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !AutonomousBeggars.Is(bot) &&
                                  !(bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE * 2)))
                    .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (speaker == null)
                    break;
                var slots = SlotsOf(player, row);
                if (target != null) slots["target"] = target;
                if (count != null) slots["count"] = count;
                if (AutonomousBotVoices.TryEventLine(speaker, "deed", out string line, topic, slots))
                {
                    AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
                    AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), speaker, line, "chat");
                    React(realm, speaker, player, topic, slots);
                }
                break;
            }
        }
    }

    /// <summary>A player's earned nickname, or null (for other features' lines).</summary>
    public static string NicknameOf(GamePlayer player) =>
        player != null && Reputations.TryGet(KeyOf(player.Realm, player.Name), out var row) && !string.IsNullOrEmpty(row.Nickname)
            ? row.Nickname : null;

    #endregion
}
