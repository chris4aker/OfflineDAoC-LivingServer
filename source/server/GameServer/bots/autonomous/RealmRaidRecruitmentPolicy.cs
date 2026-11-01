using System;
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
        "Automatic realm raids (dragons, epic dungeons, Summoner's Hall, Darkness Falls): level-50 bots that must be staged before the raid goes in. 0 (default) = half the realm's online level-50 bots, at least 40 and at most the encounter's suggested 200. Forced raids are unchanged.", 0)]
    public static int MinimumBotsSetting = 0;

    [ServerProperty("autonomous", "realm_raid_schedule_minutes",
        "Each realm opens an automatic realm raid on this schedule, rotating dragon, epic dungeon and neutral raid (0 = no schedule: raids only start when an eight-bot party happens to choose one).", 150)]
    public static int ScheduleMinutes = 150;

    public const int MinimumScaledBots = 40;
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
    /// 40) instead of failing; the stragglers follow it in as late members. 2026-10-19/20 at 600 bots per realm: three
    /// Albion rallies failed with 90/132, 101/149 and 103/150 staged, and in each about 90 more recruits were still on
    /// the road ("following the validated exterior corridor"), not lost. A full 200 raid goes in short the same way at
    /// its deadline (2026-10-21 at 900 per realm, where every raid is a full 200: the Hibernian Darkness Falls rally
    /// failed with 170 of 200 staged, short of the 180 late start).
    /// </summary>
    public const int DeadlineStartPercent = 50;
    public static bool GoesInShortAtDeadline(bool forced, int present, int required) =>
        !forced && present >= Math.Max(MinimumScaledBots, (required * DeadlineStartPercent + 99) / 100);
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
            Math.Max(MinimumScaledBots, (required * DeadlineStartPercent + 99) / 100));

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

    // Owner 2026-10-29: rallies failed with bots still arriving (Albion dragon: 170 there, 84 on the way). A bot that
    // arrives with less than 10 minutes left gives the rally 10 more minutes, up to 30 minutes past its deadline.
    public const long ArrivalExtensionMilliseconds = 10 * 60_000L;
    public const long MaximumExtensionMilliseconds = 30 * 60_000L;

    /// <summary>The staging deadline after arrivals: lastArrival + 10 min when that is later, never past deadline + 30 min.</summary>
    public static long ExtendedStagingDeadline(long deadline, long lastArrivalTick) =>
        lastArrivalTick <= 0 ? deadline :
        Math.Min(deadline + MaximumExtensionMilliseconds, Math.Max(deadline, lastArrivalTick + ArrivalExtensionMilliseconds));

    public static bool StagingExpired(bool forced, long now, long created, long departedTick, int present, bool landed,
        long lastArrivalTick) =>
        !forced && now >= ExtendedStagingDeadline(StagingDeadline(created, departedTick), lastArrivalTick) &&
        !(present >= AutonomousMinimumPresent && !landed);

    // Merge of 2026-11-01: the base's arrival extension and Living Server's scaled raid size (required) together.
    public static bool StagingExpired(bool forced, long now, long created, long departedTick, int present, bool landed,
        int required, long lastArrivalTick) =>
        !forced && now >= ExtendedStagingDeadline(StagingDeadline(created, departedTick), lastArrivalTick) &&
        !(present >= required && !landed);

    public static bool BattleExpired(bool forced, long now, long deadline) => !forced && now >= deadline;

    /// <summary>A raid party that lost members (but still exists) takes replacements up to its size when it joined.</summary>
    public static bool NeedsReplacement(int target, int members) => members > 0 && members < target;
}
