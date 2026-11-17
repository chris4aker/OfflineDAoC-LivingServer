using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Which kind of "bots as people" rule is asking <see cref="BotPeopleScope"/>. Behaviour rules change what a bot does and
/// keep today's rules wherever RvR tactics do, plus stable rides and dungeon raids. Record rules only keep a person's
/// records (skill experience, places learned, milestone progress) and change no behaviour, so they apply to every saved
/// autonomous gamebot, a raider included (a dragon trophy is won in a raid).
/// </summary>
public enum BotPeopleRule : byte
{
    /// <summary>A record only: skill profile and experience (WP1), places learned (WP3), milestone progress (WP6).</summary>
    Record,
    /// <summary>Skill mistakes and delays (WP2). Allowed in a group with a real player, where the chances are spared
    /// (a quarter, bot_skill_spare_player_group): read <see cref="BotPeopleFacts.InPlayerGroup"/>.</summary>
    Mistakes,
    /// <summary>Asking the way, tagging along, wrong roads (WP4). Never in a group with a real player.</summary>
    WayFinding,
    /// <summary>Movement habits: strafe, face, the stick line, overshoot, sitting, keep walls (WP5). Never with a player.</summary>
    Movement,
}

/// <summary>Why a "bots as people" rule leaves a bot alone (None: it may act on it).</summary>
public enum BotPeopleRefusal : byte
{
    None = 0,
    /// <summary>A real player's /spawn or /raid companion, a hired companion, a temporary helper, or anything that is not
    /// an autonomous gamebot.</summary>
    NotAGamebot,
    /// <summary>An Epic Special Force or a No Lifer's buffbot: in memory only, no person to keep records for.</summary>
    InMemoryOnly,
    /// <summary>A player leads the bot's group, or a player is in it (behaviour rules other than mistakes).</summary>
    PlayerGroup,
    /// <summary>On a realm raid (the lease, or the group is one of a raid's parties).</summary>
    RealmRaid,
    /// <summary>In a raid dungeon (a realm raid dungeon, or a region where a neutral raid is running).</summary>
    DungeonRaid,
    /// <summary>In a battleground keep assault (gathering, at the rally or storming).</summary>
    BattlegroundAssault,
    /// <summary>Carrying a relic.</summary>
    RelicCarrier,
    /// <summary>Working a siege engine job.</summary>
    SiegeOperator,
    /// <summary>At a keep it defends or besieges (RvrAtObjective).</summary>
    AtObjective,
    /// <summary>Riding a stablemaster's horse route.</summary>
    StableRide,
    /// <summary>A duty lease this rule does not name: out until the rule names it.</summary>
    OtherDuty,
    /// <summary>The feature's switch is off (from <see cref="BotPeopleScope.Check"/> only).</summary>
    SwitchedOff,
}

/// <summary>
/// What <see cref="BotPeopleScope"/> needs to know about one bot, as plain values, so the rule can be unit-tested. The
/// RvR part is <see cref="RvrTacticsScope.FactsOf"/>'s, so the two scopes never drift apart.
/// </summary>
public readonly record struct BotPeopleFacts
{
    /// <summary>The RvR tactics' facts (gamebot, companion, player group, duty, raid, battleground, objective, siege, relic).</summary>
    public RvrScopeFacts Rvr { get; init; }
    /// <summary>An Epic Special Force or a No Lifer's buffbot (GameBot.IsInMemoryOnly).</summary>
    public bool InMemoryOnly { get; init; }
    /// <summary>In a raid dungeon: a realm raid dungeon region, or one where a neutral raid is running.</summary>
    public bool InRaidDungeon { get; init; }
    /// <summary>Riding a stablemaster route (GameBot.IsOnStableMasterRoute).</summary>
    public bool StableRide { get; init; }

    /// <summary>A real player leads the bot's group or is in it, or the lease is a player-group one.</summary>
    public bool InPlayerGroup => Rvr.PlayerLedGroup || Rvr.PlayerInGroup || BotDutyLeaseRules.IsPlayerKind(Rvr.Duty);

    public static BotPeopleFacts FreeGamebot => new() { Rvr = RvrScopeFacts.FreeGamebot };
}

/// <summary>
/// The one scope test of "Bots as people" (docs/BOTS-AS-PEOPLE-1.2.md, principle 4; work package 0). Every new rule
/// asks it first and logs nothing when refused (the 5-minute BOT_PEOPLE_SUMMARY line counts refusals). It mirrors
/// <see cref="RvrTacticsScope.Refusal(in RvrScopeFacts)"/> and adds stable rides and dungeon raids: autonomous gamebots
/// only, never real players, companions (/spawn, /raid, hired) or in-memory bots. A <see cref="BotPeopleRule.Record"/>
/// rule only needs a person; the behaviour rules keep today's rules in raids, raid dungeons, battleground assaults, at a
/// keep, for siege operators and relic carriers and on stable rides, and (but for mistakes) in a group with a player.
/// The rule is pure; <see cref="FactsOf"/> reads the game.
/// </summary>
public static class BotPeopleScope
{
    /// <summary>Why a rule of this kind leaves a bot with these facts alone, or None. Pure.</summary>
    public static BotPeopleRefusal Refusal(in BotPeopleFacts facts, BotPeopleRule rule)
    {
        RvrScopeFacts rvr = facts.Rvr;
        if (!rvr.AutonomousGamebot || rvr.Companion)
            return BotPeopleRefusal.NotAGamebot;
        if (facts.InMemoryOnly)
            return BotPeopleRefusal.InMemoryOnly;
        if (rule == BotPeopleRule.Record)
            return BotPeopleRefusal.None;
        bool playerGroup = facts.InPlayerGroup;
        if (playerGroup && !AllowsPlayerGroup(rule))
            return BotPeopleRefusal.PlayerGroup;
        if (rvr.OnRealmRaid || rvr.Duty == eDutyLeaseKind.RealmRaid)
            return BotPeopleRefusal.RealmRaid;
        if (facts.InRaidDungeon)
            return BotPeopleRefusal.DungeonRaid;
        if (rvr.BattlegroundAssault || rvr.Duty == eDutyLeaseKind.BattlegroundWave)
            return BotPeopleRefusal.BattlegroundAssault;
        if (rvr.RelicCarrier)
            return BotPeopleRefusal.RelicCarrier;
        if (rvr.SiegeOperator)
            return BotPeopleRefusal.SiegeOperator;
        if (rvr.AtObjective)
            return BotPeopleRefusal.AtObjective;
        if (facts.StableRide)
            return BotPeopleRefusal.StableRide;
        bool dutyOk = AutonomousRvrPerception.MayAnswerCall(rvr.Duty) || playerGroup && BotDutyLeaseRules.IsPlayerKind(rvr.Duty);
        return dutyOk ? BotPeopleRefusal.None : BotPeopleRefusal.OtherDuty;
    }

    /// <summary>Whether a rule of this kind may act on a bot with these facts. Pure.</summary>
    public static bool Allows(in BotPeopleFacts facts, BotPeopleRule rule) => Refusal(facts, rule) == BotPeopleRefusal.None;

    /// <summary>Only mistakes (spared there) and records apply in a group with a real player. Pure.</summary>
    public static bool AllowsPlayerGroup(BotPeopleRule rule) => rule is BotPeopleRule.Record or BotPeopleRule.Mistakes;

    /// <summary>Why a rule of this kind leaves this gamebot alone now, or None (reads the game; counts nothing).</summary>
    public static BotPeopleRefusal Refusal(GameBot bot, BotPeopleRule rule)
    {
        if (BotPersonKey.Of(bot).IsNone)
            return bot is { IsAutonomousWorldBot: true, IsInMemoryOnly: true, IsTemporaryGroupHelper: false }
                ? BotPeopleRefusal.InMemoryOnly
                : BotPeopleRefusal.NotAGamebot;
        if (rule == BotPeopleRule.Record)
            return BotPeopleRefusal.None;
        return Refusal(FactsOf(bot), rule);
    }

    public static bool Allows(GameBot bot, BotPeopleRule rule) => Refusal(bot, rule) == BotPeopleRefusal.None;

    /// <summary>
    /// The hook's entry: the feature's switch first (<paramref name="switchOn"/>, e.g. BotPeopleSwitches.Current.SkillProfiles),
    /// then the scope. Counts the answer for BOT_PEOPLE_SUMMARY and returns the facts it read (default for a refusal
    /// found before the facts were needed), so a mistakes hook can read <see cref="BotPeopleFacts.InPlayerGroup"/>.
    /// </summary>
    public static BotPeopleRefusal Check(GameBot bot, BotPeopleRule rule, bool switchOn, out BotPeopleFacts facts)
    {
        facts = default;
        BotPeopleRefusal refusal;
        if (!switchOn)
            refusal = BotPeopleRefusal.SwitchedOff;
        else if (BotPersonKey.Of(bot).IsNone)
            refusal = Refusal(bot, rule);
        else
        {
            facts = rule == BotPeopleRule.Record ? FactsOwnOnly(bot) : FactsOf(bot);
            refusal = Refusal(facts, rule);
        }
        Count(rule, refusal);
        return refusal;
    }

    /// <summary>The facts of one gamebot (reads the game, changes nothing; the group and raid lookups are RvrTacticsScope's).</summary>
    public static BotPeopleFacts FactsOf(GameBot bot)
    {
        if (bot == null)
            return default;
        ushort region = bot.CurrentRegionID;
        return new BotPeopleFacts
        {
            Rvr = RvrTacticsScope.FactsOf(bot) with
            {
                // A player's own companion (hired companions too, once #287 is merged: GameBot.IsPlayerCompanion).
                Companion = bot.IsTemporaryGroupHelper || bot.IsPlayerCompanion,
            },
            InMemoryOnly = bot.IsInMemoryOnly,
            InRaidDungeon = AutonomousRealmRaid.IsRaidDungeon(region) || AutonomousRealmRaid.HasActiveRaidIn(region),
            StableRide = bot.IsOnStableMasterRoute,
        };
    }

    private static BotPeopleFacts FactsOwnOnly(GameBot bot) => new()
    {
        Rvr = new RvrScopeFacts
        {
            AutonomousGamebot = bot.IsAutonomousWorldBot,
            Companion = bot.IsTemporaryGroupHelper || bot.IsPlayerCompanion,
            PlayerLedGroup = bot.IsPlayerLedGroup,
        },
        InMemoryOnly = bot.IsInMemoryOnly,
    };

    // ---- Counts for the summary line (lock-free; read and reset every 5 minutes). ----

    private static readonly int Rules = Enum.GetValues<BotPeopleRule>().Length;
    private static readonly int Refusals = Enum.GetValues<BotPeopleRefusal>().Length;
    private static readonly long[] Counts = new long[Rules * Refusals];

    public static void Count(BotPeopleRule rule, BotPeopleRefusal refusal) =>
        Interlocked.Increment(ref Counts[(int)rule * Refusals + (int)refusal]);

    /// <summary>The counts since the last take, by rule and refusal (None = allowed), and resets them.</summary>
    public static long[,] TakeCounts()
    {
        var taken = new long[Rules, Refusals];
        for (int rule = 0; rule < Rules; rule++)
            for (int refusal = 0; refusal < Refusals; refusal++)
                taken[rule, refusal] = Interlocked.Exchange(ref Counts[rule * Refusals + refusal], 0);
        return taken;
    }

    /// <summary>"records=3/0 mistakes=0/2 ..." (allowed/refused per rule) and "refused=player_group:2,...". Pure.</summary>
    public static string Describe(long[,] counts)
    {
        var rules = new List<string>();
        var reasons = new Dictionary<BotPeopleRefusal, long>();
        for (int rule = 0; rule < counts.GetLength(0); rule++)
        {
            long refused = 0;
            for (int refusal = 1; refusal < counts.GetLength(1); refusal++)
            {
                refused += counts[rule, refusal];
                if (counts[rule, refusal] > 0)
                    reasons[(BotPeopleRefusal)refusal] = reasons.GetValueOrDefault((BotPeopleRefusal)refusal) + counts[rule, refusal];
            }
            rules.Add($"{RuleName((BotPeopleRule)rule)}={counts[rule, 0]}/{refused}");
        }
        string refusedText = reasons.Count == 0 ? "none" :
            string.Join(",", reasons.OrderBy(pair => pair.Key).Select(pair => $"{RefusalName(pair.Key)}:{pair.Value}"));
        return string.Join(" ", rules) + " refused=" + refusedText;
    }

    public static string RuleName(BotPeopleRule rule) => rule switch
    {
        BotPeopleRule.Record => "records",
        BotPeopleRule.Mistakes => "mistakes",
        BotPeopleRule.WayFinding => "way",
        BotPeopleRule.Movement => "movement",
        _ => rule.ToString().ToLowerInvariant(),
    };

    /// <summary>The log and /lswhy name of a refusal: snake case ("player_group").</summary>
    public static string RefusalName(BotPeopleRefusal refusal) => refusal switch
    {
        BotPeopleRefusal.None => "none",
        BotPeopleRefusal.NotAGamebot => "not_a_gamebot",
        BotPeopleRefusal.InMemoryOnly => "in_memory_only",
        BotPeopleRefusal.PlayerGroup => "player_group",
        BotPeopleRefusal.RealmRaid => "realm_raid",
        BotPeopleRefusal.DungeonRaid => "dungeon_raid",
        BotPeopleRefusal.BattlegroundAssault => "battleground_assault",
        BotPeopleRefusal.RelicCarrier => "relic_carrier",
        BotPeopleRefusal.SiegeOperator => "siege_operator",
        BotPeopleRefusal.AtObjective => "at_objective",
        BotPeopleRefusal.StableRide => "stable_ride",
        BotPeopleRefusal.OtherDuty => "other_duty",
        BotPeopleRefusal.SwitchedOff => "switched_off",
        _ => refusal.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// The "Bots as people" switches as one value, so rules can take them as an argument and stay pure. Each master switch
/// turns its item's sub-switches off with it. <see cref="Current"/> reads the server properties; <see cref="AllOn"/> is
/// what a default install has.
/// </summary>
public readonly record struct BotPeopleSwitches
{
    /// <summary>bot_skill_profiles: item 2's master switch (WP1, WP2).</summary>
    public bool SkillProfiles { get; init; }
    /// <summary>bot_skill_growth (WP1).</summary>
    public bool SkillGrowth { get; init; }
    /// <summary>bot_skill_spare_player_group (WP2).</summary>
    public bool SparePlayerGroup { get; init; }
    /// <summary>raid_learns_from_raiders (WP2R). Not under a master switch: it changes raids, not bots' skill.</summary>
    public bool RaidLearnsFromRaiders { get; init; }
    /// <summary>bot_place_knowledge: item 4's master switch (WP3, WP4).</summary>
    public bool PlaceKnowledge { get; init; }
    /// <summary>bot_ask_the_way (WP4).</summary>
    public bool AskTheWay { get; init; }
    /// <summary>bot_wrong_roads (WP4).</summary>
    public bool WrongRoads { get; init; }
    /// <summary>bot_movement_habits: item 5's master switch (WP5).</summary>
    public bool MovementHabits { get; init; }
    /// <summary>bot_move_strafe (WP5; also needs rvr_bot_field_tactics).</summary>
    public bool MoveStrafe { get; init; }
    /// <summary>bot_move_stick_line (WP5).</summary>
    public bool MoveStickLine { get; init; }
    /// <summary>bot_move_sit_and_walls (WP5).</summary>
    public bool MoveSitAndWalls { get; init; }
    /// <summary>bot_living_milestones: item 6 (WP6).</summary>
    public bool Milestones { get; init; }

    public static BotPeopleSwitches AllOn => From(true, true, true, true, true, true, true, true, true, true, true, true);

    public static BotPeopleSwitches AllOff => default;

    /// <summary>The server properties now, with each master switch applied to its item's sub-switches.</summary>
    public static BotPeopleSwitches Current => From(
        Properties.BOT_SKILL_PROFILES, Properties.BOT_SKILL_GROWTH, Properties.BOT_SKILL_SPARE_PLAYER_GROUP,
        Properties.RAID_LEARNS_FROM_RAIDERS, Properties.BOT_PLACE_KNOWLEDGE, Properties.BOT_ASK_THE_WAY,
        Properties.BOT_WRONG_ROADS, Properties.BOT_MOVEMENT_HABITS, Properties.BOT_MOVE_STRAFE,
        Properties.BOT_MOVE_STICK_LINE, Properties.BOT_MOVE_SIT_AND_WALLS, Properties.BOT_LIVING_MILESTONES);

    /// <summary>The switches from the twelve settings, a master switch turning its sub-switches off. Pure.</summary>
    public static BotPeopleSwitches From(bool skillProfiles, bool skillGrowth, bool sparePlayerGroup, bool raidLearns,
        bool placeKnowledge, bool askTheWay, bool wrongRoads, bool movementHabits, bool strafe, bool stickLine,
        bool sitAndWalls, bool milestones) => new()
    {
        SkillProfiles = skillProfiles,
        SkillGrowth = skillProfiles && skillGrowth,
        SparePlayerGroup = skillProfiles && sparePlayerGroup,
        RaidLearnsFromRaiders = raidLearns,
        PlaceKnowledge = placeKnowledge,
        AskTheWay = placeKnowledge && askTheWay,
        WrongRoads = placeKnowledge && wrongRoads,
        MovementHabits = movementHabits,
        MoveStrafe = movementHabits && strafe,
        MoveStickLine = movementHabits && stickLine,
        MoveSitAndWalls = movementHabits && sitAndWalls,
        Milestones = milestones,
    };

    /// <summary>"skill=on growth=on ..." for the summary line. Pure.</summary>
    public string Describe() =>
        $"skill={On(SkillProfiles)} growth={On(SkillGrowth)} spare={On(SparePlayerGroup)} raidLearn={On(RaidLearnsFromRaiders)} " +
        $"places={On(PlaceKnowledge)} ask={On(AskTheWay)} wrongRoads={On(WrongRoads)} movement={On(MovementHabits)} " +
        $"strafe={On(MoveStrafe)} stick={On(MoveStickLine)} sit={On(MoveSitAndWalls)} milestones={On(Milestones)}";

    private static string On(bool on) => on ? "on" : "off";
}
