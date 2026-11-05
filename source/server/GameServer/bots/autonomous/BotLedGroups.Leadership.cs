using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B2 (docs/BOT-LED-GROUPS.md, "Taking the lead"): /lead takes over the gamebot party that hosts the
/// player, /lead bot and /makeleader hand it back, bot_led_group_join_mode can make the player the leader on joining,
/// /invites on|off turns the player's unasked invites off, and when fewer than two gamebots remain in a party that hosts
/// the player, a yes/no box asks whether the player wants to lead the last one. The pure rules come first (tested in
/// UT_BotLedLeadership), then the glue; the coordinator's own part is in AutonomousBotGroupCoordinator.BotLed.cs.
/// <para>
/// What a handover keeps: the group object (so the loot ledger, keyed by group, carries on), the gamebots in it, and the
/// player's hold on their time (the duty lease moves between the player's two kinds in one step,
/// <see cref="BotDutyLeases.HandOver"/>). Quest credit follows: a gamebot's kill credits its PlayerGroupLeader after /lead
/// and the hosted player after /lead bot (QuestKillCredit.GroupLeaderFor).
/// </para>
/// </summary>
public static partial class BotLedGroups
{
    #region Leadership rules (pure)

    /// <summary>What saying yes to a bot leader's invite does (bot_led_group_join_mode): follow the leader, or lead at once.</summary>
    public enum JoinMode { Follow, Lead }

    public const string JoinModeFollow = "follow";
    public const string JoinModeLead = "lead";

    /// <summary>"lead" (any case, trimmed) leads; anything else, empty or unknown follows (the default, owner decision 1).</summary>
    public static JoinMode ParseJoinMode(string value) =>
        string.Equals(value?.Trim(), JoinModeLead, StringComparison.OrdinalIgnoreCase) ? JoinMode.Lead : JoinMode.Follow;

    public static JoinMode CurrentJoinMode => ParseJoinMode(Properties.BOT_LED_GROUP_JOIN_MODE);

    /// <summary>The fewest gamebots a bot-led group runs with: the coordinator ends a party left with fewer than two.</summary>
    public const int MinimumBotsToLead = 2;

    /// <summary>How long the "fewer than two bots" box waits for an answer before the last gamebot goes its own way.</summary>
    public const long LastBotAnswerMs = 60_000L;

    /// <summary>How often the last gamebot's box is tried again while another yes/no box is open.</summary>
    public const int LastBotRetryMs = 5_000;

    /// <summary>The player's side of /lead. Pure data.</summary>
    public readonly record struct TakeLeadState(bool Grouped, bool LeadsAlready, bool RaidParty, bool HostedHere);

    /// <summary>
    /// Why /lead can't take over now, or null when it can: not in a group, already the leader, in a realm raid's party (the
    /// raid leader runs a raid), or in a group that is not a gamebot party hosting this player.
    /// </summary>
    public static string TakeLeadBlocks(in TakeLeadState state) =>
        !state.Grouped ? "not_grouped"
        : state.LeadsAlready ? "already_leading"
        : state.RaidParty ? "raid_party"
        : !state.HostedHere ? "not_bot_led"
        : null;

    /// <summary>What the player is told when /lead is refused.</summary>
    public static string TakeLeadMessage(string reason) => reason switch
    {
        "not_grouped" => "You are not in a group. A gamebot leader can invite you (/lfg asks for one).",
        "already_leading" => "You already lead this group. /lead bot hands the lead to a gamebot.",
        "raid_party" => "The raid leader runs this raid party; you can't take the lead of it.",
        "not_bot_led" => "Only a group a gamebot leads can be taken over with /lead.",
        "party_gone" => "The group changed before you could take the lead.",
        _ => "You can't take the lead right now.",
    };

    /// <summary>The player's group as /lead bot (or /makeleader on a gamebot) sees it. Pure data.</summary>
    public readonly record struct HandBackState(bool Grouped, bool Leads, bool HasCompanions, int Gamebots, int OtherPlayers,
        bool InCombat, bool PlaceReachable, bool ViableRoster);

    /// <summary>
    /// Why the lead can't go to a gamebot now, or null when it can: the player must lead a group of at least two
    /// autonomous gamebots and no other real player, without /spawn companions (they always follow their owner), out of a
    /// fight, where a player can walk or ride (no frontier, battleground or Darkness Falls), and the group must have a
    /// tank, a healer and a damage dealer, the player counting for their own class.
    /// </summary>
    public static string HandBackBlocks(in HandBackState state) =>
        !state.Grouped ? "not_grouped"
        : !state.Leads ? "not_leader"
        : state.HasCompanions ? "companions_out"
        : state.OtherPlayers > 0 ? "other_players"
        : state.Gamebots < MinimumBotsToLead ? "too_few_bots"
        : state.InCombat ? "in_combat"
        : !state.PlaceReachable ? "unreachable"
        : !state.ViableRoster ? "no_core_roster"
        : null;

    /// <summary>What the player is told when the lead can't go to a gamebot.</summary>
    public static string HandBackMessage(string reason, string botName) => reason switch
    {
        "not_grouped" => "You are not in a group.",
        "not_leader" => "You are not the leader of your group.",
        "companions_out" => "Your /spawn companions follow only you. Dismiss them before a gamebot leads.",
        "other_players" => "A gamebot can't lead a group with another real player in it.",
        "too_few_bots" => "A gamebot leads only a group with at least two gamebots in it.",
        "in_combat" => "Finish the fight first.",
        "unreachable" => "Gamebots lead groups only where you can walk or ride: not in the frontier, a battleground or Darkness Falls.",
        "no_core_roster" => "A gamebot leads only a group with a tank, a healer and a damage dealer (you count for your class).",
        "no_such_bot" => $"There is no gamebot called {botName} in your group.",
        "not_a_gamebot" => $"{botName} is a /spawn companion; only a gamebot can lead.",
        "dead" => $"{botName} is dead. Pick a living gamebot to lead.",
        "no_leader" => "No gamebot in your group can lead right now.",
        "party_gone" => "The group changed before the lead could be handed over.",
        "no_place" => "The gamebots found nowhere to start from here. Try again somewhere else.",
        _ => "The lead can't be handed to a gamebot right now.",
    };

    /// <summary>A group member as the pick of a new gamebot leader sees it. Pure data.</summary>
    public readonly record struct LeaderCandidate(bool AutonomousBot, bool Alive, int Level, bool Named);

    /// <summary>
    /// The gamebot that takes the lead (/lead bot [name], /makeleader name): the named or targeted member when there is one
    /// (it must be a living autonomous gamebot), else the living autonomous gamebot of the highest level, the first of
    /// equals. Returns its index, or -1 with the reason.
    /// </summary>
    public static int PickNewLeader(IReadOnlyList<LeaderCandidate> members, out string reason)
    {
        reason = null;
        for (int i = 0; i < members.Count; i++)
        {
            if (!members[i].Named)
                continue;
            reason = !members[i].AutonomousBot ? "not_a_gamebot" : !members[i].Alive ? "dead" : null;
            return reason == null ? i : -1;
        }
        int best = -1;
        for (int i = 0; i < members.Count; i++)
            if (members[i].AutonomousBot && members[i].Alive && (best < 0 || members[i].Level > members[best].Level))
                best = i;
        if (best < 0)
            reason = "no_leader";
        return best;
    }

    /// <summary>
    /// When a party that hosts the player is about to end, the box comes up instead only when exactly one gamebot remains
    /// (the design: "fewer than two bots remain") and the party is not a realm raid's (a raid ends its parties itself).
    /// With no gamebot left the group is already gone.
    /// </summary>
    public static bool OffersLeadOnEnd(bool playerHosted, bool raidParty, int botsLeft) => playerHosted && !raidParty && botsLeft == 1;

    /// <summary>A member's job when the party is put together around the player's own group: healer, tank or hitter by class.</summary>
    public static BotPveGroupRole NaturalRole(eCharacterClass characterClass) => PlayerRole(characterClass);

    /// <summary>What /pull, /invite and /makeleader answer in a group a gamebot leads.</summary>
    public static string BotLeadsMessage(string leaderName) =>
        $"{(string.IsNullOrWhiteSpace(leaderName) ? "A gamebot" : leaderName)} is leading this group. /lead to take over.";

    /// <summary>The message for a player in a group a gamebot leads, or null when the player's group is not one.</summary>
    public static string BotLeadsMessageFor(GamePlayer player) =>
        player?.Group is { } group && HostedPlayerOf(group) == player ? BotLeadsMessage(group.LivingLeader?.Name) : null;

    /// <summary>/invites on, off (true, false), or anything else (null: show the setting).</summary>
    public static bool? ParseToggle(string value) =>
        string.Equals(value?.Trim(), "on", StringComparison.OrdinalIgnoreCase) ? true
        : string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase) ? false
        : null;

    #endregion

    #region /lead, /lead bot, /makeleader

    /// <summary>
    /// /lead: the player takes over the gamebot party that hosts them. The leader says "all yours", the player becomes the
    /// group leader and every gamebot follows them as invited bots do; the party's session ends without disbanding.
    /// Returns what the player is told.
    /// </summary>
    public static string TakeLead(GamePlayer player, string why = "lead_command")
    {
        if (player == null)
            return TakeLeadMessage(null);
        Group group = player.Group;
        var state = new TakeLeadState(group != null, group != null && group.LivingLeader == player,
            group != null && (AutonomousRealmRaid.GetView(group) != null || ViewOf(group)?.RaidGuest == true), HostedPlayerOf(group) == player);
        string blocked = TakeLeadBlocks(state);
        if (blocked == null && !AutonomousBotGroupCoordinator.TryHandOverToPlayer(player, why, out blocked))
            blocked ??= "party_gone";
        if (blocked != null)
        {
            LogHandover("refused", player, null, blocked, why);
            return TakeLeadMessage(blocked);
        }
        return "You lead the group now. The gamebots follow you: /pull, your camps, your pace. /lead bot hands it back.";
    }

    /// <summary>
    /// /lead bot [name], /makeleader on a gamebot: a gamebot takes the lead of the player's group and the bots run it as a
    /// party that hosts the player (the player follows). <paramref name="named"/> is the gamebot asked for (or null for
    /// the best one). Returns what the player is told.
    /// </summary>
    public static string HandBack(GamePlayer player, GameBot named = null, string name = null)
    {
        if (player == null)
            return HandBackMessage(null, null);
        Group group = player.Group;
        List<GameLiving> members = group?.GetMembersInTheGroup() ?? [];
        GameBot[] gamebots = members.OfType<GameBot>().Where(IsLeadingKind).ToArray();
        GameBot[] candidates = members.OfType<GameBot>().ToArray();
        if (name != null && named == null)
        {
            named = candidates.FirstOrDefault(bot => string.Equals(bot.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (named == null)
                return Refuse("no_such_bot", name);
        }
        int pick = PickNewLeader(candidates.Select(bot => new LeaderCandidate(IsLeadingKind(bot), bot.IsAlive, bot.Level, bot == named))
            .ToArray(), out string why);
        if (pick < 0)
            return Refuse(why, named?.Name);
        GameBot leader = candidates[pick];
        var state = new HandBackState(
            Grouped: group != null,
            Leads: group?.LivingLeader == player,
            HasCompanions: candidates.Any(bot => bot.IsTemporaryGroupHelper),
            Gamebots: gamebots.Length,
            OtherPlayers: members.OfType<GamePlayer>().Count(other => other != player),
            InCombat: player.InCombat || gamebots.Any(bot => bot.InCombat),
            PlaceReachable: !StateOf(player).InFrontier && !BattlegroundBrackets.IsBattlegroundRegion(player.CurrentRegionID) &&
                            player.CurrentRegionID != AutonomousDarknessFallsPolicy.RegionId,
            ViableRoster: true);
        string blocked = HandBackBlocks(state);
        if (blocked == null && !AutonomousBotGroupCoordinator.TryHandBackToBot(player, leader, out blocked))
            blocked ??= "party_gone";
        if (blocked != null)
            return Refuse(blocked, leader.Name);
        return $"{leader.Name} leads the group now and picks a camp near here. You follow; /lead takes it back.";

        string Refuse(string reason, string botName)
        {
            LogHandover("refused", player, null, reason, "lead_bot");
            return HandBackMessage(reason, botName);
        }
    }

    /// <summary>An autonomous gamebot (not a /spawn companion, not an Epic Special Force): only these lead a party.</summary>
    private static bool IsLeadingKind(GameBot bot) =>
        bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsEpicSpecialForce: false };

    internal static void LogHandover(string outcome, GamePlayer player, GameBot bot, string reason, string why, string group = null)
    {
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_HANDOVER outcome={outcome} player=\"{player?.Name}\" bot=\"{bot?.Name}\" reason={reason} via={why}" +
                     (group != null ? $" group={group}" : string.Empty));
    }

    #endregion

    #region The last gamebot's box

    /// <summary>A "fewer than two bots" offer waiting for the player's answer.</summary>
    private sealed class LastBotOffer
    {
        public LastBotOffer(Group group, GameBot bot, GamePlayer player, long deadline)
        {
            Group = group;
            Bot = bot;
            Player = player;
            Deadline = deadline;
        }

        public Group Group { get; }
        public GameBot Bot { get; }
        public GamePlayer Player { get; }
        public long Deadline { get; }
        public bool Resolved;
    }

    private static readonly ConcurrentDictionary<Group, LastBotOffer> LastBotOffers = new(ReferenceEqualityComparer.Instance);

    /// <summary>A last-gamebot box is waiting for this group (tests, and the coordinator's own checks).</summary>
    public static bool HasLastBotOffer(Group group) => group != null && LastBotOffers.ContainsKey(group);

    /// <summary>
    /// The party that hosted the player is down to one gamebot (the coordinator ended its session without disbanding):
    /// the player gets a yes/no box. Yes: the player leads, and the gamebot follows them. No, or no answer within a
    /// minute: the gamebot says goodbye and goes its own way, and the group breaks up.
    /// </summary>
    internal static void AskToLeadLastBot(Group group, GameBot bot, GamePlayer player)
    {
        if (group == null || bot == null || player == null)
            return;
        var offer = new LastBotOffer(group, bot, player, GameLoop.GameLoopTime + LastBotAnswerMs);
        LastBotOffers[group] = offer;
        LogHandover("offered", player, bot, "last_bot", "fewer_than_two_bots");
        ShowLastBotBox(offer);
        AutonomousLivingServer.Later(bot, (int)LastBotAnswerMs, null, () => ResolveLastBot(offer, false, "no_answer"));
    }

    private static void ShowLastBotBox(LastBotOffer offer)
    {
        if (offer.Resolved)
            return;
        ClientService.Instance.Post(static offer =>
        {
            GamePlayer player = offer.Player;
            if (offer.Resolved || player?.Out == null || player.Group != offer.Group)
            {
                ResolveLastBot(offer, false, "player_left");
                return;
            }
            // Never over another box: try again every few seconds until the minute is up.
            if (player.CustomDialogCallback != null)
            {
                if (GameLoop.GameLoopTime + LastBotRetryMs < offer.Deadline)
                    AutonomousLivingServer.Later(offer.Bot, LastBotRetryMs, null, () => ShowLastBotBox(offer));
                return;
            }
            player.Out.SendCustomDialog($"Only {offer.Bot.Name} is left in your group. Do you want to lead the group? " +
                                        $"(No: {offer.Bot.Name} goes its own way.)",
                (_, response) => ResolveLastBot(offer, response == 0x01, response == 0x01 ? "yes" : "no"));
        }, offer);
    }

    private static void ResolveLastBot(LastBotOffer offer, bool yes, string answer)
    {
        lock (offer)
        {
            if (offer.Resolved)
                return;
            offer.Resolved = true;
        }
        LastBotOffers.TryRemove(new KeyValuePair<Group, LastBotOffer>(offer.Group, offer));
        GameBot bot = offer.Bot;
        GamePlayer player = offer.Player;
        bool together = bot.Group == offer.Group && player.Group == offer.Group;
        if (yes && together && AutonomousBotGroupCoordinator.MakeLastBotFollow(offer.Group, bot, player))
        {
            LogHandover("accepted", player, bot, "last_bot", answer);
            player.Out?.SendMessage($"You lead the group now. {bot.Name} follows you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return;
        }
        LogHandover("declined", player, bot, "last_bot", answer);
        if (bot.Group != offer.Group)
            return;
        Announce(bot, Lead.Farewell, player, null, null);
        offer.Group.RemoveMember(bot);
        BotDutyLeases.Release(bot, eDutyLeaseKind.BotLedGroupWithPlayer, null, DutyLeaseReason.LeftGroup);
        AutonomousObjectiveAssignments.BeginSoloAfterGroupTask(bot, "The party it led with a player ended; choosing independent work");
    }

    #endregion

    #region /invites on|off

    /// <summary>The character option /invites keeps (DOLCharactersXCustomParam, a saved-progress table).</summary>
    public const string InvitesParam = "LivingServer.BotLedInvites";

    /// <summary>The player's own /invites setting as saved with the character (false when never set, or unreadable).</summary>
    private static bool StoredInvitesOff(GamePlayer player)
    {
        string id = player?.DBCharacter?.ObjectId;
        if (string.IsNullOrEmpty(id))
            return false;
        try
        {
            DbCoreCharacterXCustomParam row = DOLDB<DbCoreCharacterXCustomParam>.SelectObject(
                DB.Column("DOLCharactersObjectId").IsEqualTo(id).And(DB.Column("KeyName").IsEqualTo(InvitesParam)));
            return string.Equals(row?.Value, "off", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            Log.Warn($"BOT_LED_INVITE could not read /invites for {player.Name}", exception);
            return false;
        }
    }

    /// <summary>
    /// /invites on|off: unasked invites from gamebot leaders and raid leaders for this character. Asking (/lfg, a tell)
    /// still gets one. Saved with the character. Returns what the player is told.
    /// </summary>
    public static string SetPlayerInvites(GamePlayer player, bool on)
    {
        if (player == null)
            return string.Empty;
        MemoryOf(player, GameLoop.GameLoopTime).PlayerInvitesOff = !on;
        string id = player.DBCharacter?.ObjectId;
        if (!string.IsNullOrEmpty(id))
        {
            try
            {
                DbCoreCharacterXCustomParam row = DOLDB<DbCoreCharacterXCustomParam>.SelectObject(
                    DB.Column("DOLCharactersObjectId").IsEqualTo(id).And(DB.Column("KeyName").IsEqualTo(InvitesParam)));
                if (row == null)
                    GameServer.Database.AddObject(new DbCoreCharacterXCustomParam(id, InvitesParam, on ? "on" : "off"));
                else
                {
                    row.Value = on ? "on" : "off";
                    GameServer.Database.SaveObject(row);
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"BOT_LED_INVITE could not save /invites for {player.Name}", exception);
            }
        }
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_INVITE outcome=setting player=\"{player.Name}\" invites={(on ? "on" : "off")}");
        return InvitesStatus(player);
    }

    /// <summary>What /invites says about the player's invites now.</summary>
    public static string InvitesStatus(GamePlayer player)
    {
        bool mine = !MemoryOf(player, GameLoop.GameLoopTime).PlayerInvitesOff;
        string server = InvitesOn ? string.Empty : " (the server's bot_group_invite_player setting is off, so none come unasked anyway)";
        return mine
            ? $"Bot leaders' invites are on: a gamebot leader or raid leader may invite you now and then{server}. /invites off stops them; /lfg still asks for one."
            : "Bot leaders' invites are off: none come unasked. /lfg (or a tell to a leader) still gets you one. /invites on turns them back on.";
    }

    #endregion
}
