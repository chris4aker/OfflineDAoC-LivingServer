using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>Why the open-field RvR tactics leave a bot alone (None: they may act on it).</summary>
public enum RvrScopeRefusal : byte
{
    None = 0,
    /// <summary>A real player's /spawn companion or temporary helper, or anything that is not an autonomous gamebot.</summary>
    NotAGamebot,
    /// <summary>A player leads the bot's group, or a player is in it.</summary>
    PlayerGroup,
    /// <summary>On a realm raid (the lease, or the group is one of a raid's parties).</summary>
    RealmRaid,
    /// <summary>In a battleground keep assault: gathering for the wave, holding at the rally or storming.</summary>
    BattlegroundAssault,
    /// <summary>Committed to a keep: a defender or a siege fighter (fights at a keep keep today's rules in 1.2).</summary>
    AtObjective,
    /// <summary>Working a siege engine job.</summary>
    SiegeOperator,
    /// <summary>Carrying a relic.</summary>
    RelicCarrier,
    /// <summary>A duty lease this rule does not name (one added after 1.2): out until the rule names it.</summary>
    OtherDuty,
}

/// <summary>
/// What <see cref="RvrTacticsScope"/> needs to know about one bot, as plain values (no game world), so the rule can be
/// unit-tested. <see cref="RvrTacticsScope.FactsOf"/> reads them from a gamebot.
/// </summary>
public readonly record struct RvrScopeFacts
{
    /// <summary>An autonomous gamebot (GameBot.IsAutonomousWorldBot), not a companion.</summary>
    public bool AutonomousGamebot { get; init; }
    /// <summary>A /spawn companion or temporary helper bot (GameBot.IsTemporaryGroupHelper).</summary>
    public bool Companion { get; init; }
    /// <summary>A real player leads the bot's group (GameBot.IsPlayerLedGroup).</summary>
    public bool PlayerLedGroup { get; init; }
    /// <summary>A real player is a member of the bot's group, whoever leads it.</summary>
    public bool PlayerInGroup { get; init; }
    /// <summary>What the bot's time is leased to (docs/DUTY-LEASE.md).</summary>
    public eDutyLeaseKind Duty { get; init; }
    /// <summary>The bot's group is one of a realm raid's parties (AutonomousRealmRaid.GetView), whatever its lease says.</summary>
    public bool OnRealmRaid { get; init; }
    /// <summary>The bot is part of a battleground keep assault (gathering, at the rally or storming), whatever its lease says.</summary>
    public bool BattlegroundAssault { get; init; }
    /// <summary>A committed keep defender or siege fighter (AutonomousRvrDefense): the frontier scan's "at objective".</summary>
    public bool AtObjective { get; init; }
    /// <summary>The bot holds a siege engine job (BotSiegeRuntime.Assigned).</summary>
    public bool SiegeOperator { get; init; }
    /// <summary>The bot carries a relic.</summary>
    public bool RelicCarrier { get; init; }

    /// <summary>A free autonomous gamebot with no duty: the simplest bot the tactics apply to.</summary>
    public static RvrScopeFacts FreeGamebot => new() { AutonomousGamebot = true };
}

/// <summary>
/// The one scope test of the open-field RvR overhaul (docs/RVR-FIELD-1.2.md, principle 6; work package 0). Every new
/// rule asks it first. The tactics apply only to an autonomous gamebot, solo or in a bot-only group, whose duty lease is
/// none, a bot group session, or an RvR event while it is not at an objective. They never apply to a realm raid, a
/// battleground keep assault, a group a player leads or is in, a companion, a committed keep defender or siege fighter,
/// a siege engine operator or a relic carrier: those keep today's rules in 1.2. The lease part mirrors
/// <see cref="AutonomousRvrPerception.MayAnswerCall"/>. The rule is pure; <see cref="FactsOf"/> reads the game.
/// </summary>
public static class RvrTacticsScope
{
    /// <summary>Why the tactics leave this bot alone, or <see cref="RvrScopeRefusal.None"/> when they may act on it. Pure.</summary>
    public static RvrScopeRefusal Refusal(in RvrScopeFacts facts)
    {
        if (!facts.AutonomousGamebot || facts.Companion)
            return RvrScopeRefusal.NotAGamebot;
        if (facts.PlayerLedGroup || facts.PlayerInGroup || BotDutyLeaseRules.IsPlayerKind(facts.Duty))
            return RvrScopeRefusal.PlayerGroup;
        if (facts.OnRealmRaid || facts.Duty == eDutyLeaseKind.RealmRaid)
            return RvrScopeRefusal.RealmRaid;
        if (facts.BattlegroundAssault || facts.Duty == eDutyLeaseKind.BattlegroundWave)
            return RvrScopeRefusal.BattlegroundAssault;
        if (facts.RelicCarrier)
            return RvrScopeRefusal.RelicCarrier;
        if (facts.SiegeOperator)
            return RvrScopeRefusal.SiegeOperator;
        if (facts.AtObjective)
            return RvrScopeRefusal.AtObjective;
        return AutonomousRvrPerception.MayAnswerCall(facts.Duty) ? RvrScopeRefusal.None : RvrScopeRefusal.OtherDuty;
    }

    /// <summary>Whether the open-field tactics may act on a bot with these facts. Pure.</summary>
    public static bool Allows(in RvrScopeFacts facts) => Refusal(facts) == RvrScopeRefusal.None;

    /// <summary>
    /// The same rule from plain values, for callers that hold them already (and for tests). Pure.
    /// </summary>
    public static bool Allows(eDutyLeaseKind duty, bool autonomousGamebot = true, bool companion = false,
        bool playerLedGroup = false, bool playerInGroup = false, bool onRealmRaid = false, bool battlegroundAssault = false,
        bool atObjective = false, bool siegeOperator = false, bool relicCarrier = false) =>
        Allows(new RvrScopeFacts
        {
            AutonomousGamebot = autonomousGamebot, Companion = companion, PlayerLedGroup = playerLedGroup,
            PlayerInGroup = playerInGroup, Duty = duty, OnRealmRaid = onRealmRaid, BattlegroundAssault = battlegroundAssault,
            AtObjective = atObjective, SiegeOperator = siegeOperator, RelicCarrier = relicCarrier,
        });

    /// <summary>Whether the open-field tactics may act on this gamebot now (reads the game; see <see cref="FactsOf"/>).</summary>
    public static bool Allows(GameBot bot) => Refusal(bot) == RvrScopeRefusal.None;

    /// <summary>
    /// Why the tactics leave this gamebot alone now, or None. Reads the game in two steps, cheapest first: the bot's own
    /// flags, lease and properties, and only for a bot those let through the group, raid and battleground lookups.
    /// </summary>
    public static RvrScopeRefusal Refusal(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper)
            return RvrScopeRefusal.NotAGamebot;
        if (bot.IsPlayerLedGroup)
            return RvrScopeRefusal.PlayerGroup;
        RvrScopeFacts own = OwnFactsOf(bot);
        RvrScopeRefusal refusal = Refusal(own);
        return refusal != RvrScopeRefusal.None ? refusal : Refusal(WithGroupFacts(own, bot));
    }

    /// <summary>
    /// Reads one gamebot's scope facts from the game. It changes nothing (reading the lease ends one that has already
    /// expired, as every other reader of the lease does).
    /// </summary>
    public static RvrScopeFacts FactsOf(GameBot bot) => bot == null ? default : WithGroupFacts(OwnFactsOf(bot), bot);

    private static RvrScopeFacts OwnFactsOf(GameBot bot) => new()
    {
        AutonomousGamebot = bot.IsAutonomousWorldBot,
        Companion = bot.IsTemporaryGroupHelper,
        PlayerLedGroup = bot.IsPlayerLedGroup,
        Duty = BotDutyLeases.KindOf(bot),
        AtObjective = AutonomousRvrDefense.IsCommittedDefender(bot) || AutonomousRvrDefense.IsCommittedSiegeFighter(bot),
        SiegeOperator = BotSiegeRuntime.Assigned(bot),
        RelicCarrier = GameRelic.IsPlayerCarryingRelic(bot),
    };

    private static RvrScopeFacts WithGroupFacts(RvrScopeFacts own, GameBot bot)
    {
        Group group = bot.Group;
        return own with
        {
            PlayerInGroup = group != null && (group.Leader != null || group.GetPlayersInTheGroup().Count > 0),
            OnRealmRaid = group != null && AutonomousRealmRaid.GetView(group) != null,
            BattlegroundAssault = InBattlegroundAssault(bot),
        };
    }

    /// <summary>
    /// A battleground bot whose realm is gathering its wave there, or that holds at its realm's keep rally or storms the
    /// lord (the frontier scan's gather and storm filters). Wider than the scan's own test on purpose: a bot of a
    /// gathering realm is left alone whether or not it has reached the staging point yet.
    /// </summary>
    private static bool InBattlegroundAssault(GameBot bot)
    {
        ushort region = bot.CurrentRegionID;
        if (!BattlegroundBrackets.IsBattlegroundRegion(region) ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground))
            return false;
        return AutonomousBattlegroundWave.IsGathering(region, bot.Realm, out _) ||
               AutonomousBattlegroundKeepRally.IsRallying(region, bot.Realm, bot.DatabaseID, out _) ||
               AutonomousBattlegroundKeepRally.IsStorming(region, bot.Realm, bot.DatabaseID);
    }
}

/// <summary>
/// The switches of the open-field RvR overhaul as one value, so rules can take them as an argument and stay pure.
/// <see cref="Current"/> reads the server properties; <see cref="AllOn"/> is what a default install has.
/// <see cref="FieldTactics"/> is the master switch: with it off no other switch does anything.
/// </summary>
public readonly record struct RvrTacticsSwitches
{
    /// <summary>rvr_bot_field_tactics: the master switch.</summary>
    public bool FieldTactics { get; init; }
    /// <summary>rvr_bot_perception_bots (work package 1).</summary>
    public bool PerceptionBots { get; init; }
    /// <summary>rvr_bot_group_lookers (work package 1).</summary>
    public bool GroupLookers { get; init; }
    /// <summary>rvr_bot_assist (work package 2).</summary>
    public bool Assist { get; init; }
    /// <summary>rvr_bot_cc_plan (work package 2).</summary>
    public bool CrowdControlPlan { get; init; }
    /// <summary>rvr_bot_interrupts (work package 2).</summary>
    public bool Interrupts { get; init; }
    /// <summary>rvr_bot_peel (work package 3).</summary>
    public bool Peel { get; init; }
    /// <summary>rvr_bot_positioning (work package 3).</summary>
    public bool Positioning { get; init; }
    /// <summary>rvr_bot_size_up (work package 4).</summary>
    public bool SizeUp { get; init; }
    /// <summary>rvr_bot_disengage (work package 4).</summary>
    public bool Disengage { get; init; }
    /// <summary>rvr_bot_regroup (work package 4).</summary>
    public bool Regroup { get; init; }
    /// <summary>rvr_march_lanes (work package 5).</summary>
    public bool MarchLanes { get; init; }
    /// <summary>rvr_roam_lanes (work package 5).</summary>
    public bool RoamLanes { get; init; }
    /// <summary>rvr_roaming_parties (work package 6).</summary>
    public bool RoamingParties { get; init; }
    /// <summary>rvr_roam_checks (work package 6).</summary>
    public bool RoamChecks { get; init; }
    /// <summary>rvr_bot_stealth_openers (work package 6).</summary>
    public bool StealthOpeners { get; init; }

    /// <summary>Every switch on: a default install.</summary>
    public static RvrTacticsSwitches AllOn => new()
    {
        FieldTactics = true, PerceptionBots = true, GroupLookers = true, Assist = true, CrowdControlPlan = true,
        Interrupts = true, Peel = true, Positioning = true, SizeUp = true, Disengage = true, Regroup = true,
        MarchLanes = true, RoamLanes = true, RoamingParties = true, RoamChecks = true, StealthOpeners = true,
    };

    /// <summary>Every switch off.</summary>
    public static RvrTacticsSwitches AllOff => default;

    /// <summary>The server's settings now. With the master switch off every switch reads off.</summary>
    public static RvrTacticsSwitches Current => !Properties.RVR_BOT_FIELD_TACTICS ? AllOff : new()
    {
        FieldTactics = true,
        PerceptionBots = Properties.RVR_BOT_PERCEPTION_BOTS,
        GroupLookers = Properties.RVR_BOT_GROUP_LOOKERS,
        Assist = Properties.RVR_BOT_ASSIST,
        CrowdControlPlan = Properties.RVR_BOT_CC_PLAN,
        Interrupts = Properties.RVR_BOT_INTERRUPTS,
        Peel = Properties.RVR_BOT_PEEL,
        Positioning = Properties.RVR_BOT_POSITIONING,
        SizeUp = Properties.RVR_BOT_SIZE_UP,
        Disengage = Properties.RVR_BOT_DISENGAGE,
        Regroup = Properties.RVR_BOT_REGROUP,
        MarchLanes = Properties.RVR_MARCH_LANES,
        RoamLanes = Properties.RVR_ROAM_LANES,
        RoamingParties = Properties.RVR_ROAMING_PARTIES,
        RoamChecks = Properties.RVR_ROAM_CHECKS,
        StealthOpeners = Properties.RVR_BOT_STEALTH_OPENERS,
    };
}
