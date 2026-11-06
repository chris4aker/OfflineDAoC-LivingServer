using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;

namespace DOL.GS;

public static class RealmRaidRecruitmentPolicy
{
    public const int MaximumBots = 300;
    public const int MaximumParties = (MaximumBots + 7) / 8;
    public const int AutonomousMinimumPresent = 200;
    public const int MinimumParties = AutonomousMinimumPresent / 8;
    public const long AutonomousMinimumStagingMilliseconds = 15 * 60_000L;
    public const long AutonomousStagingLimitMilliseconds = 90 * 60_000L;
    public const long BattleMilliseconds = 4 * 60 * 60_000L;
    public const double NewEventChance = .20;
    public const double JoinExistingChance = .95;

    // Automatic raids sized to the roster (2026-10-19 audit): with 150 level-50 bots per realm, about 110 online and
    // 30 of them on group PvE, the fixed 200 meant no dragon, epic or neutral raid ever started on the owner's server.
    [ServerProperty("autonomous", "realm_raid_minimum_bots",
        "Automatic realm raids (dragons, epic dungeons, Summoner's Hall, Darkness Falls): level-50 bots that must be staged before the raid goes in. 0 (default) = half the realm's online level-50 bots, at least 40 and at most the encounter's period size (40-120, docs/RAID-SIZES.md), then sized by its wins and losses. Forced raids are unchanged.", 0)]
    public static int MinimumBotsSetting = 0;

    [ServerProperty("autonomous", "realm_raid_schedule_minutes",
        "Each realm opens an automatic realm raid on this schedule, rotating dragon, epic dungeon and neutral raid (0 = no schedule: raids only start when an eight-bot party happens to choose one).", 150)]
    public static int ScheduleMinutes = 150;

    public const int MinimumScaledBots = 40;
    /// <summary>
    /// The fewest an automatic raid is sized to or goes in short with: three full parties. With the period sizes
    /// (2026-11-04: 40-120 instead of 120-200) the old minimum of 40 left a 70-bot dragon raid at 57% for a short start
    /// and one trimmed to 49 at 82%, so "half the raid" no longer meant half.
    /// </summary>
    public const int MinimumRaidBots = 24;
    /// <summary>Planned parties an automatic raid holds at once while it recruits.</summary>
    public const int AutomaticPlannedParties = 3;
    /// <summary>The smallest whole PvE party that may join an automatic raid.</summary>
    public const int MinimumJoiningParty = 5;
    public const double ScaledShare = 0.5;

    /// <summary>Level-50 bots an automatic raid must stage before it goes in.</summary>
    public static int RequiredBots(int onlineLevelFifty, int suggested, int setting)
    {
        int ceiling = Math.Clamp(suggested, MinimumScaledBots, MaximumBots);
        if (setting > 0) return Math.Clamp(setting, 8, MaximumBots);
        return Math.Clamp((int)Math.Round(onlineLevelFifty * ScaledShare), MinimumScaledBots, ceiling);
    }

    /// <summary>A scaled raid recruits a quarter above what it needs, in whole parties, so late losses don't stall it (a full 200 raid keeps the 300 cap).</summary>
    public static int RecruitCap(int required) => required >= AutonomousMinimumPresent ? MaximumBots
        : Math.Min(MaximumBots, (required * 5 / 4 + 7) / 8 * 8);

    // ---- Win-rate sizing (owner 2026-10-19: "mostly successful... high stakes but not a guarantee", about 2 in 3) ----
    // 2026-11-04: the full size is the period's comfortable raid (docs/RAID-SIZES.md), and the size moves from 70% of it
    // (about the period's "possible, but tough": Apocalypse at 80 of a comfortable 100-105) up to 200% (room for bots that
    // play worse than 2003 players; 150% until 2026-11-05, when the overnight losses had already had up to 180% with the
    // Special Forces), never past 300.
    // "If it keeps winning easily, it needs fewer bots next time, so the stakes stay up." The bosses are never touched:
    // the lever is the raid's own size. Each automatic raid (dragons too) is sized from its own encounter's record:
    // a win trims WinTrim points of the period size, a loss adds WinTrim * target / (100 - target) back (20.3 at 67%), so
    // the size stops moving exactly where the encounter is won target% of the time. It is a running total, not an
    // average of the last few: it settles at the size that wins 2 in 3, not at a size that still wins more often.
    // The other lever is the Epic Special Forces (AutonomousEpicSpecialForces): the help a raid gets after a wipe, from
    // the same record. Size sets the stakes; the Special Forces answer a loss.
    // 2026-11-05 (owner, after a night of 1 win in 7 on the test copy: bots play well below 2003 players): every raid
    // starts at 125% of its period size, mid-range, and the steps are doubled (10 per win, 20.3 per loss) so it finds
    // its size in a few raids, still aiming at 2 in 3.

    [ServerProperty("autonomous", "realm_raid_target_win_percent",
        "Automatic realm raids (dragons too) are sized so each encounter is won about this often: a win trims the next raid at that encounter, a loss adds back. 0 = off (every automatic raid recruits its starting size). Bosses are never weakened.", 67)]
    public static int TargetWinPercentSetting = 67;

    [ServerProperty("autonomous", "realm_raid_smallest_size_percent",
        "The smallest an automatic realm raid that keeps winning gets, as a percentage of its full size (its period size, or what realm_raid_minimum_bots and the roster give it).", 70)]
    public static int SmallestSizePercentSetting = 70;

    [ServerProperty("autonomous", "realm_raid_largest_size_percent",
        "The largest an automatic realm raid that keeps losing gets, as a percentage of its full size (its period size), never above 300 bots. Bosses are never weakened: a raid that keeps losing gets bigger instead.", 200)]
    public static int LargestSizePercentSetting = 200;

    [ServerProperty("autonomous", "realm_raid_start_size_percent",
        "The size an automatic realm raid starts at before it has any wins or losses, as a percentage of its full size (its period size, docs/RAID-SIZES.md); kept between the smallest and largest size.", 125)]
    public static int StartSizePercentSetting = 125;

    [ServerProperty("autonomous", "realm_raid_win_trim_percent",
        "Points of its full size an automatic realm raid loses for each win at its encounter (a loss adds more back, so the size settles at the target win rate).", 10)]
    public static int WinTrimPercentSetting = 10;

    /// <summary>The sizing rules, made safe: target 50-90% (0 = off), smallest 25-100%, largest 100-300% and never under
    /// the smallest, trim 1-25 points, start between the smallest and the largest.</summary>
    public readonly record struct SizingRules(int TargetWinPercent, int SmallestPercent, int LargestPercent, int WinTrimPercent,
        int StartPercent = 100)
    {
        public static readonly SizingRules Default = new(67, 70, 200, 10, 125);
        public bool On => TargetWinPercent > 0;
        public SizingRules Normalized()
        {
            int smallest = Math.Clamp(SmallestPercent, 25, 100);
            int largest = Math.Clamp(LargestPercent, Math.Max(100, smallest), 300);
            return new(TargetWinPercent <= 0 ? 0 : Math.Clamp(TargetWinPercent, 50, 90), smallest, largest,
                Math.Clamp(WinTrimPercent, 1, 25), Math.Clamp(StartPercent, smallest, largest));
        }
        /// <summary>Points a loss adds back: the trim times target / (100 - target), so wins and losses balance at the target.</summary>
        public double LossAddPercent => On ? WinTrimPercent * (double)TargetWinPercent / (100 - TargetWinPercent) : 0;
    }

    public static SizingRules RulesFromSettings() =>
        new SizingRules(TargetWinPercentSetting, SmallestSizePercentSetting, LargestSizePercentSetting, WinTrimPercentSetting,
            StartSizePercentSetting).Normalized();

    /// <summary>
    /// The raid's size as a percentage of its full size, from its encounter's results (oldest first; only wins and
    /// losses count): it starts at the starting size (125% of the period size by default) and each win trims, each loss
    /// adds, never past the limits.
    /// </summary>
    public static double SizePercent(IEnumerable<string> outcomesOldestFirst, SizingRules rules) =>
        SizePercentOf((outcomesOldestFirst ?? []).Select(o => (RaidResult)o), rules);

    /// <summary>
    /// As <see cref="SizePercent"/>, but a result that says what size the raid ran at (<see cref="RaidResult.SizePercent"/>,
    /// from its ledger row) carries on from that size. So the size never depends on which results are still in the
    /// record: with the 200% ceiling (2026-11-05) a size replayed only from the last 400 results moved by more than one
    /// step on a quarter of all raids once the record was full, as old results dropped out.
    /// </summary>
    public static double SizePercentOf(IEnumerable<RaidResult> resultsOldestFirst, SizingRules rules)
    {
        rules = rules.Normalized();
        double percent = rules.StartPercent;
        if (!rules.On) return percent;
        foreach (RaidResult result in resultsOldestFirst ?? [])
        {
            if (AutonomousEpicSpecialForces.IsWin(result.Outcome)) percent = (result.SizePercent ?? percent) - rules.WinTrimPercent;
            else if (AutonomousEpicSpecialForces.IsLoss(result.Outcome)) percent = (result.SizePercent ?? percent) + rules.LossAddPercent;
            else continue;
            percent = Math.Clamp(percent, rules.SmallestPercent, rules.LargestPercent);
        }
        return percent;
    }

    /// <summary>Bots a sized raid must stage: its share of the full size, never under three parties or over 300.</summary>
    public static int SizedRequired(int fullRequired, double percent) =>
        Math.Clamp((int)Math.Round(fullRequired * percent / 100), Math.Min(MinimumRaidBots, fullRequired), MaximumBots);

    /// <summary>The bots a sized raid recruits: the same share of the full raid's cap, in whole parties, never under
    /// what it must stage (a quarter above, as RecruitCap; a 200 raid set by realm_raid_minimum_bots at 95% recruits 288,
    /// not RecruitCap(190) = 240).</summary>
    public static int SizedRecruitCap(int fullRequired, double percent, int required) =>
        Math.Clamp((int)Math.Ceiling(RecruitCap(fullRequired) * percent / 100 / 8) * 8, Math.Min(required, MaximumBots), MaximumBots);

    /// <summary>An automatic raid's size and help, and why (REALM_RAID_SIZED).</summary>
    public sealed record RaidSizing(int FullRequired, double Percent, int Required, int RecruitCap, int Wins, int Losses,
        int Streak, int SpecialForcesOnWipe, bool SpecialForcesFromStart, string Why, bool ByResults = false,
        SizingRules? Rules = null)
    {
        /// <summary>The size to write on the raid's ledger row, or null when it was not sized by results (a size set by
        /// realm_raid_minimum_bots, or sizing off): the next raid carries on from it.</summary>
        public double? RecordedPercent => ByResults ? Percent : null;
    }

    /// <summary>
    /// One policy for both levers: the raid's size (this record's running total) and the Epic Special Forces call-up
    /// (its last six results). <paramref name="fixedSize"/> = the owner set realm_raid_minimum_bots, which is honoured
    /// as it stands; <paramref name="recordLoaded"/> false = the ledger is still loading, so today's size.
    /// </summary>
    public static RaidSizing Size(int fullRequired, IReadOnlyCollection<RaidResult> resultsOldestFirst, SizingRules rules,
        bool recordLoaded = true, bool fixedSize = false)
    {
        rules = rules.Normalized();
        RaidResult[] results = RealmRaidRecord.CountedResults(resultsOldestFirst).ToArray();
        string[] counted = results.Select(r => r.Outcome).ToArray();
        int wins = counted.Count(AutonomousEpicSpecialForces.IsWin), losses = counted.Length - wins;
        int streak = 0;
        for (int i = counted.Length - 1; i >= 0; i--)
        {
            bool win = AutonomousEpicSpecialForces.IsWin(counted[i]);
            if (streak != 0 && win != streak > 0) break;
            streak += win ? 1 : -1;
        }
        bool fromStart = AutonomousEpicSpecialForces.ComesFromStart(counted);
        string why;
        double percent;
        if (fixedSize) { percent = 100; why = "realm_raid_minimum_bots is set: that size, not sized by results"; }
        else if (!rules.On) { percent = rules.StartPercent; why = "sizing is off (realm_raid_target_win_percent 0): the starting size"; }
        else if (!recordLoaded) { percent = rules.StartPercent; why = "the record is still loading: the starting size"; }
        else
        {
            percent = SizePercentOf(results, rules);
            string run = streak > 1 ? $", the last {streak} won" : streak < -1 ? $", the last {-streak} lost" : "";
            why = counted.Length == 0 ? $"no wins or losses here yet: the starting size, {rules.StartPercent}%"
                : $"{wins} won and {losses} lost here{run}: each win trims {rules.WinTrimPercent} points, each loss adds " +
                  $"{rules.LossAddPercent:0.#}, aiming at {rules.TargetWinPercent}% wins" +
                  (percent <= rules.SmallestPercent ? " (at the smallest size)" : percent >= rules.LargestPercent ? " (at the largest size)" : "");
        }
        int required = percent == 100 ? fullRequired : SizedRequired(fullRequired, percent);
        int cap = percent == 100 ? RecruitCap(fullRequired) : SizedRecruitCap(fullRequired, percent, required);
        int callUp = AutonomousEpicSpecialForces.CallUpParties(results);
        return new(fullRequired, percent, required, cap, wins, losses, streak, callUp, fromStart, why, !fixedSize && rules.On, rules);
    }

    /// <summary>The log line of an automatic raid's size (REALM_RAID_SIZED), written when its rally opens.</summary>
    public static string SizedLine(string eventId, string realm, RaidSizing sizing) =>
        $"REALM_RAID_SIZED event={eventId} realm={realm} full={sizing.FullRequired} size={sizing.Percent:0.#}% " +
        $"required={sizing.Required} recruitCap={sizing.RecruitCap} won={sizing.Wins} lost={sizing.Losses} " +
        $"specialForcesOnWipe={sizing.SpecialForcesOnWipe * AutonomousEpicSpecialForces.PartySize} " +
        $"specialForcesFromStart={sizing.SpecialForcesFromStart} why=\"{sizing.Why}\"" +
        // The rules in force (2026-11-05: a stored setting from an earlier test build kept the old 5-point step, and only
        // the steps in the why showed it).
        (sizing.Rules is { } rules ? $" rules=\"start={rules.StartPercent}% range={rules.SmallestPercent}-{rules.LargestPercent}% " +
            $"win={-rules.WinTrimPercent} loss=+{rules.LossAddPercent:0.#} target={rules.TargetWinPercent}%\"" : "");

    /// <summary>A realm's next scheduled raid opens when the slot is due, none of its raids is running and it has enough level 50s online.</summary>
    public static bool ScheduledRaidDue(long now, long due, bool realmHasRaid, int onlineLevelFifty, int scheduleMinutes) =>
        scheduleMinutes > 0 && now >= due && !realmHasRaid && onlineLevelFifty >= MinimumScaledBots * 2;

    /// <summary>
    /// The next slot: the schedule plus or minus up to 20 minutes, never sooner than 30, so the realms don't keep the
    /// order of the first round. With a fixed order Albion's raids always ended first and its released level 50s
    /// opened every siege of 2026-10-19.
    /// </summary>
    public const int ScheduleJitterMinutes = 20;
    public static long NextScheduleMilliseconds(int scheduleMinutes, double roll) =>
        Math.Max(30, scheduleMinutes + (int)Math.Round((Math.Clamp(roll, 0, 1) * 2 - 1) * ScheduleJitterMinutes)) * 60_000L;

    /// <summary>First scheduled raid after a server start: Albion after 20 minutes, Midgard 30, Hibernia 40.</summary>
    public static long FirstScheduledDelayMilliseconds(int realmIndex) => (20 + 10 * Math.Clamp(realmIndex, 0, 2)) * 60_000L;

    // An automatic rally that has waited most of its window with a nearly full
    // roster goes in rather than failing outright. Caer Sidi failed twice in one
    // run with 196 and 186 of the 200 required bots at its entrance.
    public const long AutonomousLateStartMilliseconds = 75 * 60_000L;
    public const int AutonomousLateStartMinimumPresent = 180;

    public static bool Eligible(int level, bool autonomous, bool temporary, bool playerLed) =>
        level == 50 && autonomous && !temporary && !playerLed;

    public static bool CanOpenEvent(bool forced, bool sameRealmEvent, bool sameEncounterEvent) =>
        !sameEncounterEvent && (forced || !sameRealmEvent);

    // Forced raids (started by the player) go in as soon as 200 are staged and, for a dragon,
    // it has landed. They have no staging or battle time limit: they run until the encounter
    // is defeated, the player presses Stop event, or the server stops.
    public static bool Ready(bool forced, long elapsed, int present, bool landed, int required = AutonomousMinimumPresent) =>
        elapsed >= (forced ? 0 : AutonomousMinimumStagingMilliseconds) &&
        (present >= required ||
         !forced && elapsed >= AutonomousLateStartMilliseconds && present >= LateStartMinimum(required)) &&
        landed;

    /// <summary>
    /// Who must be staged for a late start: 90% of a full 200 (180), but 70% of a raid scaled to the roster. The
    /// 2026-10-19 rallies had 82-91 recruited and still only 46-51 staged after 90 minutes (stragglers fighting
    /// trash at the entrance, still walking or released), so three of five scheduled raids never fought at all.
    /// The owner wants failures to come from the fight, not from the gathering: a short raid goes in and may lose.
    /// </summary>
    public const int ScaledLateStartPercent = 70;

    /// <summary>
    /// A raid scaled to the roster whose staging time has run out goes in short with half of what it needs (at least
    /// three parties, <see cref="MinimumRaidBots"/>; 40 before the period sizes) instead of failing; the stragglers follow it in as late members. 2026-10-19/20 at 600 bots per realm: three
    /// Albion rallies failed with 90/132, 101/149 and 103/150 staged, and in each about 90 more recruits were still on
    /// the road ("following the validated exterior corridor"), not lost. A full 200 raid goes in short the same way at
    /// its deadline (2026-10-21 at 900 per realm, where every raid is a full 200: the Hibernian Darkness Falls rally
    /// failed with 170 of 200 staged, short of the 180 late start).
    /// </summary>
    public const int DeadlineStartPercent = 50;
    public static bool GoesInShortAtDeadline(bool forced, int present, int required) =>
        !forced && present >= Math.Max(MinimumRaidBots, (required * DeadlineStartPercent + 99) / 100);
    public static int LateStartMinimum(int required) => required >= AutonomousMinimumPresent
        ? required * AutonomousLateStartMinimumPresent / AutonomousMinimumPresent
        : (required * ScaledLateStartPercent + 99) / 100;

    // 2026-10-16 (owner's session): the automatic Tuscaran rally needed 84 minutes to gather 200 at Hagall, so the one
    // 90-minute staging clock left 6 minutes for the trip to the dungeon and it failed with 93 there. A slow hub leaves
    // after HubPatienceMilliseconds with HubEarlyDepartureMinimum present (late arrivals join the expedition directly),
    // and after leaving the raid always gets PostDepartureStagingMilliseconds to stage.
    public const long HubPatienceMilliseconds = 30 * 60_000L;
    public const int HubEarlyDepartureMinimum = 120;
    public const long PostDepartureStagingMilliseconds = 45 * 60_000L;

    /// <remarks>2026-10-21 at 900 per realm: the Midgard Summoner's Hall rally recruited 102 for a full 200 raid, never
    /// reached the 120 early departure and failed with nobody staged. After the hub's patience a raid leaves with half
    /// of what it needs, the share it may go in short with at its deadline.</remarks>
    public static bool DepartHub(bool alreadyDeparted, int presentBots, long elapsed = 0, int required = AutonomousMinimumPresent) =>
        alreadyDeparted || presentBots >= required ||
        elapsed >= HubPatienceMilliseconds && presentBots >= Math.Min(required * HubEarlyDepartureMinimum / AutonomousMinimumPresent,
            Math.Max(MinimumRaidBots, (required * DeadlineStartPercent + 99) / 100));

    public static bool StagingExpired(bool forced, long elapsed, int present = 0, bool landed = true) =>
        !forced && elapsed >= AutonomousStagingLimitMilliseconds &&
        !(present >= AutonomousMinimumPresent && !landed);

    /// <summary>When an automatic rally's staging ends: 90 minutes after it opened, but never sooner than 45 minutes
    /// after its parties left the hub (departedTick 0 = not yet).</summary>
    public static long StagingDeadline(long created, long departedTick) =>
        Math.Max(created + AutonomousStagingLimitMilliseconds,
            departedTick > 0 ? departedTick + PostDepartureStagingMilliseconds : 0);

    public static bool StagingExpired(bool forced, long now, long created, long departedTick, int present, bool landed,
        int required = AutonomousMinimumPresent) =>
        !forced && now >= StagingDeadline(created, departedTick) &&
        !(present >= required && !landed);

    public static bool BattleExpired(bool forced, long now, long deadline) => !forced && now >= deadline;

    /// <summary>A raid party that lost members (but still exists) takes replacements up to its size when it joined.</summary>
    public static bool NeedsReplacement(int target, int members) => members > 0 && members < target;

    /// <summary>
    /// Formation slots a raid has: the realm's 38 parties plus the Epic Special Forces' three. In-game check 2026-11-04:
    /// an automatic Summoner's Hall raid of small parties (34 at the hub, four more joining on the road) held all 38 slots
    /// with 277 bots, and after its wipe at Grand Summoner Govannon all three Special Forces parties were refused.
    /// </summary>
    public const int FormationSlots = MaximumParties + AutonomousEpicSpecialForces.MaximumParties;

    /// <summary>The slots a new party may take, in order: the realm's parties take 0-37; the Special Forces take their
    /// own 38-40 first, then any slot the realm left free.</summary>
    public static IEnumerable<int> SlotOrder(bool specialForces)
    {
        if (specialForces)
            for (int slot = MaximumParties; slot < FormationSlots; slot++) yield return slot;
        for (int slot = 0; slot < MaximumParties; slot++) yield return slot;
    }

    /// <summary>
    /// Why a raid cannot take another party, or null when it can. The realm's limits (38 parties, 300 bots) count only
    /// the realm's own parties and bots; the Epic Special Forces come on top of them, up to their own three parties.
    /// <paramref name="recruitCap"/>: an automatic raid's own cap (its sized RecruitCap), which a party that answers on
    /// its own (TryJoin) is held to as well; before 2026-11-04 those parties could fill any raid to 300, so a raid
    /// trimmed after its wins would have grown back to full size by itself.
    /// </summary>
    public static string PartyCapRefusal(bool specialForces, int realmParties, int realmBots, int specialForcesParties, int joining,
        int recruitCap = MaximumBots) =>
        specialForces
            ? specialForcesParties >= AutonomousEpicSpecialForces.MaximumParties
                ? $"the raid already has {specialForcesParties} Special Forces parties" : null
            : realmParties >= MaximumParties ? $"the raid already has its {MaximumParties} parties"
            : realmBots + joining > Math.Min(MaximumBots, recruitCap)
                ? $"the raid would have {realmBots + joining} of its {Math.Min(MaximumBots, recruitCap)} bots" : null;
}
