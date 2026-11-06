using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>The rvr_bot_awareness setting: how sharp autonomous gamebots are at spotting enemy players in RvR.</summary>
public enum RvrBotAwareness
{
    Easy,
    Normal,
    Veteran,
}

/// <summary>What a frontier scan found, for the perception rule. Only players and what they bring are perceived.</summary>
public enum RvrPerceivedKind
{
    /// <summary>Everything else (enemy gamebots and their pets, siege, guards, doors, monsters): acquired as before.</summary>
    Other,
    Player,
    /// <summary>A player's /spawn companion or a temporary helper bot.</summary>
    PlayerCompanion,
    /// <summary>A pet owned by a player or by a player's companion.</summary>
    PlayerPet,
}

/// <summary>Why a target was noticed (logged with RVR_NOTICE).</summary>
[Flags]
public enum RvrNoticeFactors
{
    None = 0,
    Close = 1,
    Combat = 2,
    Casting = 4,
    Group = 8,
    Facing = 16,
    Still = 32,
    Alerted = 64,
}

/// <summary>
/// One calibration point of the notice curve: at <paramref name="Distance"/> the median time from first line of sight
/// to noticing a plain sighting (a moving enemy, alone, not fighting or casting, outside the bot's view cone) is
/// <paramref name="MedianSeconds"/>, for one bot looking on its own.
/// </summary>
public readonly record struct RvrNoticeAnchor(int Distance, double MedianSeconds);

/// <summary>
/// The numbers one rvr_bot_awareness level uses: the sure and the outer radius, the calibrated notice curve, how fast
/// bots react, how far a call is heard, and how many bots of one realm may look for the same enemy each second.
/// </summary>
public sealed record RvrAwarenessProfile(int AlwaysNoticeRadius, int OuterRange, RvrNoticeAnchor[] Anchors,
    double ReactionScale, int SpotterRadius, int ObserverCap);

/// <summary>One enemy as one bot sees it on one scan.</summary>
public readonly record struct RvrSighting(double Distance, bool InCombat, bool Casting, int GroupSize, bool InFacingCone, bool StandingStill);

/// <summary>
/// A bot that could hear a spotter's call (pure input for SpotterRecipients and CallRecipients). MayAnswer is false when
/// its duty lease doesn't let it leave what it is doing (AutonomousRvrPerception.MayAnswerCall).
/// </summary>
public readonly record struct RvrSpotterMate(long Id, int X, int Y, int Z, bool Alive, bool SameRegion, bool MayAnswer = true);

/// <summary>A mate a call reaches: it treats the enemy as noticed after ExtraDelay plus its own reaction delay.</summary>
public readonly record struct RvrCallee(long Id, int ExtraDelayMilliseconds, bool Relayed);

/// <summary>What one scan of one enemy by one bot decided (AutonomousRvrPerception.Scan).</summary>
public enum RvrScanResult
{
    /// <summary>Noticed earlier (or called by a mate) and the reaction delay is over: engage.</summary>
    Engage,
    /// <summary>Noticed, still reacting.</summary>
    Reacting,
    /// <summary>Noticed on this scan: engage after <see cref="RvrScanOutcome.DelayMilliseconds"/>.</summary>
    Noticed,
    /// <summary>Looked and did not notice.</summary>
    Unseen,
    /// <summary>Enough bots of this realm already looked for this enemy this second: this bot did not roll.</summary>
    Capped,
}

/// <summary>The result of one scan, with what the RVR_NOTICE log line needs.</summary>
public readonly record struct RvrScanOutcome(RvrScanResult Result, RvrNoticeFactors Factors, double Chance, int DelayMilliseconds,
    long SinceFirstSightMilliseconds);

/// <summary>
/// RvR perception (docs/LIVING-SERVER.md, "RvR perception"; owner Chris, 2026-11-01; North Star audit 2026-11-03,
/// challenge 2). A player on r/daoc reported that gamebots in the frontier saw him from 5,000 units, instantly and all
/// at once, whenever he was in line of sight: radar, not players. Now an autonomous gamebot notices an enemy player (or
/// that player's companion or pet) by chance on each scan; the chance is calibrated so that the median time to notice
/// at each distance is a stated number of seconds, whatever the scan cadence, and it rises when the enemy is fighting,
/// casting, in a group or in front of the bot. At most a few bots of a realm look for the same enemy each second (the
/// observer cap); the rest learn of it only when one of them calls it. A bot then takes a moment to react, and its
/// group hears the call at once while other groups of its warband sometimes hear it relayed, later. A bot whose duty is
/// a raid, a battleground assault or a player's group does not leave it to answer a call. Close up a bot always
/// notices. Enemy gamebots, siege, keep guards, doors and monsters are acquired as before, and a bot or group under
/// attack fights back at once. The rules here are pure (unit-tested); the frontier scan reads the game.
/// </summary>
public static class AutonomousRvrPerception
{
    // ---- the numbers (normal is the 2003 player; easy and veteran are their own curves)

    /// <summary>Within this distance a scan always notices (normal).</summary>
    public const int AlwaysNoticeRadius = 1_000;
    /// <summary>Beyond this a bot never notices on its own (normal); the old scan went to 5,000.</summary>
    public const int OuterRange = 3_500;
    /// <summary>No boost makes a scan beyond the always radius certain.</summary>
    public const double MaxChance = 0.95;

    // The boosts multiply the rate of noticing: a factor of 2 halves the median time to notice.
    public const double CombatFactor = 2.0;
    public const double CastingFactor = 1.5;
    public const double GroupFactor = 1.5;
    public const int GroupMinimum = 3;
    public const double FacingFactor = 1.75;
    public const double FacingConeDegrees = 120;
    public const double StillFactor = 0.5;

    /// <summary>The frontier scan's nominal interval (AutonomousFrontierThreatPolicy: 1,000-1,249 ms, mean 1,125).</summary>
    public const int DefaultScanIntervalMilliseconds = 1_125;
    /// <summary>The scan interval used for the chance is the real time since the bot's last scan, within these bounds.</summary>
    public const int MinScanIntervalMilliseconds = 1_000;
    public const int MaxScanIntervalMilliseconds = 2_500;

    public const int ReactionMinMilliseconds = 500;
    public const int ReactionMaxMilliseconds = 2_000;

    /// <summary>A spotter's group and warband mates within this hear the call (normal).</summary>
    public const int SpotterRadius = 2_500;
    /// <summary>The observer cap counts rolls per enemy and realm in windows of this length.</summary>
    public const int ObserverWindowMilliseconds = 1_000;
    /// <summary>The chance that another group of the spotter's warband hears the call (relayed, as a whole group).</summary>
    public const double WarbandRelayChance = 0.5;
    /// <summary>A relayed call reaches another group this much later, before its members' own reaction delay.</summary>
    public const int WarbandRelayDelayMilliseconds = 4_000;
    /// <summary>A noticed enemy is forgotten after this long out of the bot's sight.</summary>
    public const int ForgetAfterMilliseconds = 15_000;
    /// <summary>At most one spoken spot call in a group this often.</summary>
    public const int SpotCallGapMilliseconds = 12_000;

    /// <summary>
    /// The normal curve: the audit's targets (technical §8.2: 1.5 s at 1,500, 6 s at 2,500, 20 s at 3,400), 0.75 s just
    /// outside the sure radius, and the last slope carried on to the outer range (about 23 s at 3,500).
    /// </summary>
    public static readonly RvrNoticeAnchor[] NormalAnchors =
        [new(1_000, 0.75), new(1_500, 1.5), new(2_500, 6), new(3_400, 20)];

    /// <summary>Easy: twice the normal medians, sure only within 700 and never beyond 2,500.</summary>
    public static readonly RvrNoticeAnchor[] EasyAnchors =
        [new(700, 1.5), new(1_500, 3), new(2_500, 12)];

    /// <summary>Veteran: half the normal medians, sure within 1,300 and up to 4,500 (about 34 s there).</summary>
    public static readonly RvrNoticeAnchor[] VeteranAnchors =
        [new(1_300, 0.5), new(1_500, 0.75), new(2_500, 3), new(3_400, 10)];

    private static readonly RvrAwarenessProfile EasyProfile = new(700, 2_500, EasyAnchors, 1.3, 2_000, 2);
    private static readonly RvrAwarenessProfile NormalProfile = new(AlwaysNoticeRadius, OuterRange, NormalAnchors, 1.0, SpotterRadius, 3);
    private static readonly RvrAwarenessProfile VeteranProfile = new(1_300, 4_500, VeteranAnchors, 0.7, 3_000, 5);

    public static RvrBotAwareness ParseAwareness(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "easy" => RvrBotAwareness.Easy,
        "veteran" => RvrBotAwareness.Veteran,
        _ => RvrBotAwareness.Normal,
    };

    public static RvrAwarenessProfile Profile(RvrBotAwareness awareness) => awareness switch
    {
        RvrBotAwareness.Easy => EasyProfile,
        RvrBotAwareness.Veteran => VeteranProfile,
        _ => NormalProfile,
    };

    /// <summary>Only enemy players and what they bring are perceived; everything else is acquired as before.</summary>
    public static bool IsSubject(RvrPerceivedKind kind) => kind != RvrPerceivedKind.Other;

    /// <summary>
    /// Whether the perception rule applies to this pick: the setting is on, the target is a player-kind, and the target is
    /// not firing on the bot or its realm-mates near it, nor attacking its group (retaliation stays immediate).
    /// </summary>
    public static bool Applies(bool enabled, RvrPerceivedKind kind, bool firingOnRealm, bool attackingGroup) =>
        enabled && IsSubject(kind) && !firingOnRealm && !attackingGroup;

    /// <summary>
    /// The median time, in seconds, from first line of sight to noticing a plain sighting at this distance: 0 within the
    /// sure radius, infinite beyond the outer range, otherwise interpolated between the anchors on a log scale (the time
    /// grows by the same factor for every extra 100 units within a segment). Pure.
    /// </summary>
    public static double MedianSecondsToNotice(double distance, RvrAwarenessProfile profile)
    {
        if (distance <= profile.AlwaysNoticeRadius)
            return 0;
        if (distance > profile.OuterRange)
            return double.PositiveInfinity;
        RvrNoticeAnchor[] anchors = profile.Anchors;
        if (anchors.Length == 1 || distance <= anchors[0].Distance)
            return anchors[0].MedianSeconds;
        int i = 0;
        while (i < anchors.Length - 2 && distance > anchors[i + 1].Distance)
            i++;
        // Beyond the last anchor t exceeds 1 and the last segment's slope carries on to the outer range.
        double t = (distance - anchors[i].Distance) / Math.Max(1, anchors[i + 1].Distance - anchors[i].Distance);
        double from = Math.Log(anchors[i].MedianSeconds), to = Math.Log(anchors[i + 1].MedianSeconds);
        return Math.Exp(from + t * (to - from));
    }

    /// <summary>
    /// The chance per scan that makes the median time to notice exactly <paramref name="medianSeconds"/> for a bot that
    /// scans every <paramref name="scanIntervalMilliseconds"/> and first sees the enemy at a random moment between two
    /// scans. With m = median / interval = k + x (k whole), the chance p that nothing is noticed by the median is
    /// (1 - x·p)(1 - p)^k; this solves that for one half. A median shorter than half an interval cannot be met even by a
    /// certain scan, so it gives 1. Pure.
    /// </summary>
    public static double ChancePerScan(double medianSeconds, double scanIntervalMilliseconds)
    {
        if (double.IsPositiveInfinity(medianSeconds))
            return 0;
        double m = medianSeconds * 1000 / Math.Max(1, scanIntervalMilliseconds);
        if (m <= 0.5)
            return 1;
        int k = (int)Math.Floor(m);
        double x = m - k;
        if (k == 0)
            return 0.5 / x;
        // Newton's method inside a shrinking bracket (the remaining chance falls as p rises): a few steps.
        double lo = 0, hi = 1, p = 1 - Math.Pow(0.5, 1 / m);
        for (int step = 0; step < 50; step++)
        {
            double qk1 = Math.Pow(1 - p, k - 1), qk = qk1 * (1 - p);
            double f = (1 - x * p) * qk - 0.5;
            if (Math.Abs(f) < 1e-12)
                break;
            if (f > 0) lo = p; else hi = p;
            double slope = -x * qk - (1 - x * p) * k * qk1;
            double next = slope < 0 ? p - f / slope : double.NaN;
            p = next > lo && next < hi ? next : (lo + hi) / 2;
        }
        return p;
    }

    /// <summary>The chance that one scan notices this enemy, and the factors that moved it. Pure.</summary>
    public static double NoticeChance(RvrSighting sighting, RvrAwarenessProfile profile, double scanIntervalMilliseconds,
        out RvrNoticeFactors factors)
    {
        factors = RvrNoticeFactors.None;
        if (sighting.Distance <= profile.AlwaysNoticeRadius)
        {
            factors = RvrNoticeFactors.Close;
            return 1.0;
        }
        if (sighting.Distance > profile.OuterRange)
            return 0.0;
        double rate = 1;
        if (sighting.InCombat) { rate *= CombatFactor; factors |= RvrNoticeFactors.Combat; }
        if (sighting.Casting) { rate *= CastingFactor; factors |= RvrNoticeFactors.Casting; }
        if (sighting.GroupSize >= GroupMinimum) { rate *= GroupFactor; factors |= RvrNoticeFactors.Group; }
        if (sighting.InFacingCone) { rate *= FacingFactor; factors |= RvrNoticeFactors.Facing; }
        if (sighting.StandingStill && !sighting.InCombat) { rate *= StillFactor; factors |= RvrNoticeFactors.Still; }
        double median = MedianSecondsToNotice(sighting.Distance, profile) / rate;
        return Math.Min(MaxChance, ChancePerScan(median, ClampScanInterval(scanIntervalMilliseconds)));
    }

    public static double NoticeChance(RvrSighting sighting, RvrAwarenessProfile profile, out RvrNoticeFactors factors) =>
        NoticeChance(sighting, profile, DefaultScanIntervalMilliseconds, out factors);

    public static double NoticeChance(RvrSighting sighting, RvrAwarenessProfile profile) => NoticeChance(sighting, profile, out _);

    /// <summary>The scan interval the chance is computed for: the real time since the last scan, within bounds. Pure.</summary>
    public static double ClampScanInterval(double milliseconds) =>
        Math.Clamp(milliseconds, MinScanIntervalMilliseconds, MaxScanIntervalMilliseconds);

    /// <summary>Whether a scan with this roll (0 to 1) notices. Pure.</summary>
    public static bool Notices(RvrSighting sighting, RvrAwarenessProfile profile, double roll, out RvrNoticeFactors factors) =>
        roll < NoticeChance(sighting, profile, out factors);

    /// <summary>Whether a bearing (degrees clockwise from where the bot faces, 0-360) is inside the facing cone. Pure.</summary>
    public static bool InFacingCone(double bearingDegrees, double coneDegrees = FacingConeDegrees)
    {
        double bearing = (bearingDegrees % 360 + 360) % 360;
        return bearing <= coneDegrees / 2 || bearing >= 360 - coneDegrees / 2;
    }

    /// <summary>
    /// Whether a bot may answer a mate's call, by what its time is leased to (docs/DUTY-LEASE.md): a free bot, an RvR
    /// event marcher and a bot in a bot-only group (the group the call is meant for) may; a bot on a realm raid, in a
    /// battleground assault or in a player's group keeps its duty. It still notices for itself. Pure.
    /// </summary>
    public static bool MayAnswerCall(eDutyLeaseKind duty) =>
        duty is eDutyLeaseKind.None or eDutyLeaseKind.RvrEvent or eDutyLeaseKind.BotGroupSession;

    /// <summary>Whether another group of the warband hears a relayed call, for a roll from 0 to 1. Pure.</summary>
    public static bool RelayReaches(double roll) => roll < WarbandRelayChance;

    /// <summary>
    /// How bold the bot is, 0 to 1: Daredevils 1, others from their Risk trait; 0.5 with personalities off (null). Pure.
    /// </summary>
    public static double Boldness(BotPersonality? personality) => personality switch
    {
        null => 0.5,
        { Type: BotPersonalityType.Daredevil } => 1.0,
        { } p => Math.Clamp(p.Traits.Risk, 0, 9) / 9.0 * 0.8,
    };

    /// <summary>
    /// The delay between noticing and engaging, 0.5-2.0 s at normal: higher level and bolder personalities are faster, and a
    /// roll (0 to 1) adds the rest. Easy is slower and veteran faster (the profile's reaction scale). Pure.
    /// </summary>
    public static int ReactionDelayMilliseconds(int level, BotPersonality? personality, double roll, RvrAwarenessProfile profile)
    {
        double levelFactor = (Math.Clamp(level, 1, 50) - 1) / 49.0;
        double speed = 0.45 * levelFactor + 0.35 * Boldness(personality) + 0.20 * Math.Clamp(roll, 0, 1);
        double delay = ReactionMaxMilliseconds - (ReactionMaxMilliseconds - ReactionMinMilliseconds) * speed;
        return (int)Math.Round(delay * profile.ReactionScale);
    }

    /// <summary>
    /// Whether the line-of-sight check for this enemy can be skipped because the scan could not use it: the bot has not
    /// noticed it (nor been told of it), and it is either beyond the outer range or outside the sure radius while this
    /// second's observer slots for it are taken. Pure apart from reading the memory and the budget.
    /// </summary>
    public static bool SkipsLineOfSight(Memory memory, ObserverBudget observers, long bot, int realm, long target, long now,
        double distance, RvrAwarenessProfile profile)
    {
        if (distance <= profile.AlwaysNoticeRadius || memory.Peek(bot, target, now) != Memory.State.Unnoticed)
            return false;
        return distance > profile.OuterRange || observers.IsFull(target, realm, now, profile.ObserverCap);
    }

    /// <summary>
    /// One scan of one visible enemy by one bot. A noticed (or called) enemy is engaged once the reaction delay is over; a
    /// call is dropped when the bot's duty doesn't allow answering it, and the bot looks for itself. Otherwise the bot
    /// takes one of this second's observer slots for the enemy (not needed within the sure radius; none taken when the
    /// chance is zero) and rolls. <paramref name="reactionDelay"/> is asked only when the bot notices.
    /// </summary>
    public static RvrScanOutcome Scan(Memory memory, ObserverBudget observers, long bot, int realm, long target, long now,
        RvrSighting sighting, RvrAwarenessProfile profile, double scanIntervalMilliseconds, double roll, Func<int> reactionDelay,
        bool mayAnswerCall)
    {
        Memory.State state = memory.Seen(bot, target, now, out bool fromCall);
        if (state != Memory.State.Unnoticed && fromCall && !mayAnswerCall)
        {
            memory.DropCall(bot, target);
            state = Memory.State.Unnoticed;
        }
        if (state == Memory.State.Ready)
            return new(RvrScanResult.Engage, fromCall ? RvrNoticeFactors.Alerted : RvrNoticeFactors.None, 1, 0, 0);
        if (state == Memory.State.Reacting)
            return new(RvrScanResult.Reacting, fromCall ? RvrNoticeFactors.Alerted : RvrNoticeFactors.None, 1, 0, 0);
        double chance = NoticeChance(sighting, profile, scanIntervalMilliseconds, out RvrNoticeFactors factors);
        if (chance <= 0)
            return new(RvrScanResult.Unseen, factors, 0, 0, 0);
        if ((factors & RvrNoticeFactors.Close) == 0 && !observers.TryClaim(target, realm, now, profile.ObserverCap))
            return new(RvrScanResult.Capped, factors, chance, 0, 0);
        if (roll >= chance)
            return new(RvrScanResult.Unseen, factors, chance, 0, 0);
        int delay = reactionDelay();
        long since = memory.Notice(bot, target, now, delay);
        return new(RvrScanResult.Noticed, factors, chance, delay, since);
    }

    /// <summary>The spotter's mates that hear its call: alive, in its region, within the spotter radius, not itself. Pure.</summary>
    public static long[] SpotterRecipients(long spotterId, int x, int y, int z, IEnumerable<RvrSpotterMate> mates, int radius)
    {
        long squared = (long)radius * radius;
        return mates.Where(mate => mate.Id != spotterId && mate.Alive && mate.SameRegion &&
                (long)(mate.X - x) * (mate.X - x) + (long)(mate.Y - y) * (mate.Y - y) + (long)(mate.Z - z) * (mate.Z - z) <= squared)
            .Select(mate => mate.Id).Distinct().ToArray();
    }

    /// <summary>
    /// Who hears a spotter's call. Its own group mates within the spotter radius hear it at once. Each other group of its
    /// warband hears it relayed, as a whole group, when <paramref name="relayRoll"/> (0 to 1, one per group) is below
    /// WarbandRelayChance, WarbandRelayDelayMilliseconds later; again only members within the radius. A mate whose duty
    /// doesn't let it answer is counted in <paramref name="held"/> instead. Pure.
    /// </summary>
    public static List<RvrCallee> CallRecipients(long spotterId, int x, int y, int z, IEnumerable<RvrSpotterMate> groupMates,
        IEnumerable<IReadOnlyCollection<RvrSpotterMate>> otherGroups, int radius, Func<double> relayRoll, out int held)
    {
        var callees = new List<RvrCallee>();
        var seen = new HashSet<long> { spotterId };
        int heldCount = 0;
        void Add(IEnumerable<RvrSpotterMate> mates, int extraDelay, bool relayed)
        {
            var byId = new Dictionary<long, RvrSpotterMate>();
            foreach (RvrSpotterMate mate in mates)
                byId.TryAdd(mate.Id, mate);
            foreach (long id in SpotterRecipients(spotterId, x, y, z, byId.Values, radius))
            {
                if (!seen.Add(id))
                    continue;
                if (!byId[id].MayAnswer)
                    heldCount++;
                else
                    callees.Add(new RvrCallee(id, extraDelay, relayed));
            }
        }
        Add(groupMates, 0, false);
        foreach (IReadOnlyCollection<RvrSpotterMate> group in otherGroups)
            if (group.Count > 0 && RelayReaches(relayRoll()))
                Add(group, WarbandRelayDelayMilliseconds, true);
        held = heldCount;
        return callees;
    }

    /// <summary>The compass word from the bot towards the enemy (DAoC: x grows east, y grows south). Pure.</summary>
    public static string Direction(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return "here";
        double degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
        int sector = (int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8;
        return sector switch
        {
            0 => "north", 1 => "ne", 2 => "east", 3 => "se", 4 => "south", 5 => "sw", 6 => "west", _ => "nw",
        };
    }

    /// <summary>The spoken call, "inc 3 north". Pure.</summary>
    public static string SpotCall(int count, int dx, int dy) => $"inc {Math.Max(1, count)} {Direction(dx, dy)}";

    public static string Describe(RvrNoticeFactors factors) =>
        factors == RvrNoticeFactors.None ? "distance" : factors.ToString().Replace(", ", "+");

    /// <summary>
    /// The observer cap: how many bots of each realm rolled to notice each enemy in the current one-second window. A
    /// bot within the sure radius doesn't need a slot. Windows older than a few seconds are swept now and then.
    /// Thread-safe.
    /// </summary>
    public sealed class ObserverBudget
    {
        private sealed class Window
        {
            public long Index;
            public int Count;
        }

        private readonly ConcurrentDictionary<(long Target, int Realm), Window> _windows = new();
        private long _nextSweep;

        public int Count => _windows.Count;

        private static long IndexOf(long now) => Math.DivRem(now, ObserverWindowMilliseconds, out long rest) - (rest < 0 ? 1 : 0);

        /// <summary>Whether this second's slots for the enemy are all taken (cap 0 or less: no cap).</summary>
        public bool IsFull(long target, int realm, long now, int cap)
        {
            if (cap <= 0 || !_windows.TryGetValue((target, realm), out Window window))
                return false;
            lock (window)
                return window.Index == IndexOf(now) && window.Count >= cap;
        }

        /// <summary>Takes one of this second's slots for the enemy; false when they are all taken.</summary>
        public bool TryClaim(long target, int realm, long now, int cap)
        {
            if (cap <= 0)
                return true;
            Sweep(now);
            long index = IndexOf(now);
            Window window = _windows.GetOrAdd((target, realm), _ => new Window { Index = index });
            lock (window)
            {
                if (window.Index != index)
                {
                    window.Index = index;
                    window.Count = 0;
                }
                if (window.Count >= cap)
                    return false;
                window.Count++;
                return true;
            }
        }

        private void Sweep(long now)
        {
            if (now < Volatile.Read(ref _nextSweep))
                return;
            Volatile.Write(ref _nextSweep, now + 10 * ObserverWindowMilliseconds);
            long stale = IndexOf(now) - 5;
            foreach (var pair in _windows)
                if (pair.Value.Index < stale)
                    _windows.TryRemove(pair.Key, out _);
        }

        public void Clear() => _windows.Clear();
    }

    /// <summary>
    /// What each bot has seen: (bot id, target key) to when it first had the target in sight, and once noticed (or told
    /// by a call) when it may engage. Entries are forgotten ForgetAfterMilliseconds after the bot last saw the target, and
    /// swept now and then. Thread-safe.
    /// </summary>
    public sealed class Memory
    {
        private const long NotNoticed = long.MaxValue;

        private sealed class Entry
        {
            public long ReadyAt = NotNoticed;
            public long LastSeen;
            public long FirstSeen;
            public bool FromCall;
        }

        public enum State
        {
            Unnoticed,
            Reacting,
            Ready,
        }

        private readonly ConcurrentDictionary<(long Bot, long Target), Entry> _entries = new();
        private long _nextSweep;

        public int Count => _entries.Count;

        /// <summary>The bot sees the target on this scan: its state, refreshing the entry (a first sight is recorded).</summary>
        public State Seen(long bot, long target, long now) => Seen(bot, target, now, out _);

        /// <inheritdoc cref="Seen(long, long, long)"/>
        public State Seen(long bot, long target, long now, out bool fromCall)
        {
            Sweep(now);
            Entry entry = _entries.GetOrAdd((bot, target), _ => new Entry { LastSeen = now, FirstSeen = now });
            lock (entry)
            {
                if (now - entry.LastSeen > ForgetAfterMilliseconds)
                {
                    entry.ReadyAt = NotNoticed;
                    entry.FromCall = false;
                    entry.FirstSeen = now;
                }
                entry.LastSeen = now;
                fromCall = entry.FromCall && entry.ReadyAt != NotNoticed;
                return StateOf(entry, now);
            }
        }

        /// <summary>The bot's state for the target without counting this as a sighting.</summary>
        public State Peek(long bot, long target, long now)
        {
            if (!_entries.TryGetValue((bot, target), out Entry entry))
                return State.Unnoticed;
            lock (entry)
                return now - entry.LastSeen > ForgetAfterMilliseconds ? State.Unnoticed : StateOf(entry, now);
        }

        private static State StateOf(Entry entry, long now) =>
            entry.ReadyAt == NotNoticed ? State.Unnoticed : now >= entry.ReadyAt ? State.Ready : State.Reacting;

        /// <summary>
        /// The bot noticed the target on its own: it may engage after the delay. Returns how long it had the target in
        /// sight before noticing (0 when this scan was the first sight).
        /// </summary>
        public long Notice(long bot, long target, long now, int delayMilliseconds)
        {
            Entry entry = _entries.GetOrAdd((bot, target), _ => new Entry { LastSeen = now, FirstSeen = now });
            lock (entry)
            {
                if (now - entry.LastSeen > ForgetAfterMilliseconds)
                    entry.FirstSeen = now;
                entry.ReadyAt = now + delayMilliseconds;
                entry.LastSeen = now;
                entry.FromCall = false;
                return now - entry.FirstSeen;
            }
        }

        /// <summary>
        /// A mate called the target: the bot treats it as noticed after its own delay. Something it already noticed (or
        /// was already told) stays as it is. Returns true when the call was news to this bot.
        /// </summary>
        public bool Alert(long bot, long target, long now, int delayMilliseconds)
        {
            Sweep(now);
            Entry entry = _entries.GetOrAdd((bot, target), _ => new Entry { LastSeen = now, FirstSeen = now });
            lock (entry)
            {
                bool stale = now - entry.LastSeen > ForgetAfterMilliseconds;
                if (!stale && entry.ReadyAt != NotNoticed)
                    return false;
                if (stale)
                    entry.FirstSeen = now;
                entry.ReadyAt = now + delayMilliseconds;
                entry.LastSeen = now;
                entry.FromCall = true;
                return true;
            }
        }

        /// <summary>The bot drops a call it may not answer (its duty changed): it is back to looking for itself.</summary>
        public void DropCall(long bot, long target)
        {
            if (!_entries.TryGetValue((bot, target), out Entry entry))
                return;
            lock (entry)
            {
                if (!entry.FromCall)
                    return;
                entry.ReadyAt = NotNoticed;
                entry.FromCall = false;
            }
        }

        private void Sweep(long now)
        {
            if (now < Volatile.Read(ref _nextSweep))
                return;
            Volatile.Write(ref _nextSweep, now + ForgetAfterMilliseconds);
            foreach (var pair in _entries)
                if (now - pair.Value.LastSeen > ForgetAfterMilliseconds)
                    _entries.TryRemove(pair.Key, out _);
        }

        public void Clear() => _entries.Clear();
    }
}
