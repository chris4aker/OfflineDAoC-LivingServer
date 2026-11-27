using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// #570 step 2: the learned danger spots (<see cref="AutonomousPveBossDanger"/>) also steer a gamebot's town and horse
/// trips, not only its camps. Port of the trip rules of OfflineDAoC-True165 (GPL-3.0) AutonomousRoadHotspots
/// (a24f34987, last a97fd32: BlocksService, FinishWalkUnsafe, LandingNearHotspot, WalkLegUnsafe) onto our shared spot
/// memory instead of a second one. There, merchant and Realm Exchange trips walked low Hibernian bots through the
/// Silvermine and Lough Derg spots (347 bots in 30 hours), and a Howth horse left riders to walk the Silvermine road to
/// Eilwen (47% died within 5 minutes). Monsters are not touched.
/// </summary>
public static class AutonomousLearnedDangerTrips
{
    /// <summary>A finishing walk (from a landing, or the whole way on foot) straight through a spot costs this much more,
    /// so a safer landing wins unless it is more than ten minutes slower (True165 FinishWalkDetourSeconds).</summary>
    public const double UnsafeFinishPenaltySeconds = 600;

    /// <summary>A bot whose goal is this close to a landing is going there, not through it, and may ride to it.</summary>
    public const double LandingGoalRadius = 3_000;

    /// <summary>
    /// Pure rule (True165 BlocksService): a service trip is dangerous to this bot when the service stands beside a spot
    /// that warns it, or (same region) the straight way there passes one. The realm's one Realm Exchange broker
    /// (<paramref name="onlyOfItsKind"/>) is judged by its own spot alone: a straight line across a realm is not a route,
    /// and without it the bot would sell its exchange goods to a merchant.
    /// </summary>
    public static bool ServiceDangerous(bool onlyOfItsKind, ushort fromRegion, int fromX, int fromY, int botLevel,
        ushort toRegion, int toX, int toY, DateTime utc)
    {
        if (AutonomousPveBossDanger.IsNear(toRegion, toX, toY, botLevel, utc))
            return true;
        return !onlyOfItsKind && fromRegion == toRegion &&
               AutonomousPveBossDanger.WalkPasses(fromRegion, fromX, fromY, toX, toY, botLevel, utc);
    }

    /// <summary>
    /// The nearest candidate, preferring one whose trip is not dangerous: a dangerous one is taken only when every
    /// candidate is (the bot is never left with a full pack and nowhere to sell it).
    /// </summary>
    public sealed class SafestNearest<T> where T : class
    {
        private double _bestMinutes = double.MaxValue, _safeMinutes = double.MaxValue;
        public T Nearest { get; private set; }
        public T NearestSafe { get; private set; }
        public int Dangerous { get; private set; }

        public void Consider(T candidate, double minutes, bool dangerous)
        {
            if (minutes < _bestMinutes)
            {
                _bestMinutes = minutes;
                Nearest = candidate;
            }
            if (dangerous)
                Dangerous++;
            else if (minutes < _safeMinutes)
            {
                _safeMinutes = minutes;
                NearestSafe = candidate;
            }
        }

        public T Choice => NearestSafe ?? Nearest;

        /// <summary>The danger rule changed the choice (the nearest one was dangerous and a safe one was left).</summary>
        public bool Skipped => NearestSafe != null && !ReferenceEquals(NearestSafe, Nearest);
    }

    /// <summary>Pure rule: a landing this far from the goal is passed through, so its dangers count.</summary>
    public static bool PassesThrough(double landingToGoal) => landingToGoal > LandingGoalRadius;

    /// <summary>
    /// Pure rule (True165 LandingNearHotspot): a solo bot does not ride to a landing beside a spot that warns it, unless
    /// its goal is beside the landing.
    /// </summary>
    public static bool LandingDangerous(ushort region, int landingX, int landingY, double landingToGoal, int botLevel, DateTime utc) =>
        PassesThrough(landingToGoal) && AutonomousPveBossDanger.IsNear(region, landingX, landingY, botLevel, utc);

    /// <summary>
    /// Pure rule (True165 FinishWalkUnsafe): the straight walk from a landing (or the bot) to its goal passes a spot. A
    /// spot at the start (the landing rule judges it) or at the goal (the bot was sent there) does not count.
    /// </summary>
    public static bool FinishWalkDangerous(ushort region, int startX, int startY, int goalX, int goalY, int botLevel, DateTime utc) =>
        AutonomousPveBossDanger.WalkPasses(region, startX, startY, goalX, goalY, botLevel, utc, goalExempt: true);

    // the AUTONOMOUS_STABLE_LANDING_SKIP line needs a second, unrestricted search: at most once per bot per few minutes
    private static readonly ConcurrentDictionary<int, long> NextSkipSample = new();
    public const long SkipSampleMilliseconds = 5 * 60_000;

    /// <summary>True (and the next sample is put off) when this bot may run the unrestricted search for the skip line.</summary>
    public static bool TryTakeSkipSample(int botId, long now)
    {
        if (NextSkipSample.TryGetValue(botId, out long next) && now < next)
            return false;
        if (NextSkipSample.Count > 8_192)
            NextSkipSample.Clear();
        NextSkipSample[botId] = now + SkipSampleMilliseconds;
        return true;
    }

    /// <summary>Tests only.</summary>
    public static void ClearSamples() => NextSkipSample.Clear();
}

/// <summary>
/// #570 step 2: landings where gamebots keep dying soon after they get off a horse. Port of OfflineDAoC-True165 (GPL-3.0)
/// AutonomousStableLandingWatch (feecfac1d, last 3aaeae2; there 1,250 landing deaths in 4.2 hours, Howth alone 260).
/// A death within <see cref="WatchMilliseconds"/> of a landing counts for that landing and the bot's 5-level band; once
/// <see cref="DeadlyLandingBots"/> different bots of one band died there within <see cref="DeadlyLandingWindowMilliseconds"/>,
/// solo bots of that band ride to it only when they are going there, until the deaths age out. In memory only.
/// </summary>
public static class AutonomousStableLandingWatch
{
    public const long WatchMilliseconds = 5 * 60_000;
    public const int DeadlyLandingBots = 6;
    public const long DeadlyLandingWindowMilliseconds = 60 * 60_000;
    private const int MaxEntries = 10_000;
    private const int DeathsKeptPerLanding = 32;

    private readonly record struct Landing(string Destination, ushort Region, long Tick);

    private static readonly ConcurrentDictionary<long, Landing> Landings = new();
    private static readonly ConcurrentDictionary<(ushort Region, string Destination, int Band), Queue<(long BotId, long Tick)>>
        LandingDeaths = new();

    public static int LevelBand(int level) => Math.Max(0, level) / 5;

    /// <summary>Pure rule: a death this soon after the landing counts.</summary>
    public static bool CountsAsLandingDeath(long landedTick, long now) =>
        landedTick > 0 && now >= landedTick && now - landedTick <= WatchMilliseconds;

    /// <summary>Pure rule: enough different bots died here within the window.</summary>
    public static bool IsDeadly(IEnumerable<(long BotId, long Tick)> deaths, long now)
    {
        var bots = new HashSet<long>();
        foreach ((long botId, long tick) in deaths)
            if (now >= tick && now - tick <= DeadlyLandingWindowMilliseconds)
                bots.Add(botId);
        return bots.Count >= DeadlyLandingBots;
    }

    /// <summary>A gamebot got off a horse at this destination.</summary>
    public static void Arrived(long botId, ushort region, string destination, long now)
    {
        if (botId <= 0 || string.IsNullOrEmpty(destination))
            return;
        if (Landings.Count >= MaxEntries)
            foreach (var entry in Landings)
                if (!CountsAsLandingDeath(entry.Value.Tick, now))
                    Landings.TryRemove(entry.Key, out _);
        Landings[botId] = new Landing(destination, region, now);
    }

    /// <summary>
    /// A gamebot died. Returns the landing's destination when this death made the landing deadly for the bot's band
    /// (for one log line), else null.
    /// </summary>
    public static string Died(long botId, int level, long now)
    {
        if (botId <= 0 || !Landings.TryRemove(botId, out Landing landing) || !CountsAsLandingDeath(landing.Tick, now))
            return null;
        Queue<(long BotId, long Tick)> deaths = LandingDeaths.GetOrAdd((landing.Region, landing.Destination, LevelBand(level)), _ => new());
        lock (deaths)
        {
            bool was = IsDeadly(deaths, now);
            deaths.Enqueue((botId, now));
            while (deaths.Count > DeathsKeptPerLanding)
                deaths.Dequeue();
            return !was && IsDeadly(deaths, now) ? landing.Destination : null;
        }
    }

    /// <summary>Solo bots of this level band should skip this landing for now (one dictionary lookup).</summary>
    public static bool IsDeadlyLanding(ushort region, string destination, int level, long now)
    {
        if (string.IsNullOrEmpty(destination) ||
            !LandingDeaths.TryGetValue((region, destination, LevelBand(level)), out Queue<(long BotId, long Tick)> deaths))
            return false;
        lock (deaths)
            return IsDeadly(deaths, now);
    }

    /// <summary>Tests only.</summary>
    public static void Clear()
    {
        Landings.Clear();
        LandingDeaths.Clear();
    }
}
