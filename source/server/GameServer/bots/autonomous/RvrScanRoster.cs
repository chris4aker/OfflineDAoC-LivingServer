using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>Why a bot did or did not run its frontier scan this time (RvrScanRoster.Decide).</summary>
public enum RvrScanRole : byte
{
    /// <summary>The rule does not apply (switch off, out of scope, at an objective): the bot scans as before.</summary>
    Everyone,
    /// <summary>A soloer (or a group of two or fewer): it looks for itself.</summary>
    Solo,
    /// <summary>One of its group's two lookers this turn.</summary>
    Looker,
    /// <summary>Not a looker, but woken: a mate called an enemy, a mate is fighting, or the plan has a target.</summary>
    Woken,
    /// <summary>Not a looker and nothing woke it: it does not scan.</summary>
    Idle,
}

/// <summary>
/// Who looks (docs/RVR-FIELD-1.2.md, layer "Eyes"; work package 1, rvr_bot_group_lookers). Two members of each bot
/// group take turns as lookers; soloers look for themselves; the others take their target from the group's plan or from
/// a mate's call, and run no scan of their own until something wakes them: a mate called an enemy (it is in their
/// perception memory), a group mate is fighting (retaliation stays immediate), or the group's plan has a called target.
/// The turn passes every <see cref="RotationMilliseconds"/>, in the order of the members' ids, from a start that depends
/// on the group, so each member works out the same two lookers on its own. Pure.
/// </summary>
public static class RvrScanRoster
{
    /// <summary>How many members of a group look at once.</summary>
    public const int LookersPerGroup = 2;

    /// <summary>How long one pair of lookers keeps the turn.</summary>
    public const int RotationMilliseconds = 10_000;

    /// <summary>
    /// The ids of this turn's lookers among <paramref name="memberIds"/> (the group's members that can look: alive
    /// autonomous gamebots in the region; any order, duplicates ignored). A group of two or fewer: everyone. Pure.
    /// </summary>
    public static long[] Lookers(IReadOnlyCollection<long> memberIds, long now, int lookers = LookersPerGroup,
        int rotationMilliseconds = RotationMilliseconds)
    {
        var all = new long[memberIds.Count];
        int n = 0;
        foreach (long id in memberIds)
            all[n++] = id;
        Array.Sort(all, 0, n);
        int distinct = 0;
        for (int i = 0; i < n; i++)
            if (distinct == 0 || all[distinct - 1] != all[i])
                all[distinct++] = all[i];
        long[] ids = distinct == all.Length ? all : all[..distinct];
        if (ids.Length <= lookers)
            return ids;
        long turn = Math.DivRem(now, Math.Max(1, rotationMilliseconds), out long rest) - (rest < 0 ? 1 : 0);
        // The start is spread by the group's lowest id, so groups do not all hand over on the same member index.
        ulong mixed = unchecked((ulong)ids[0] * 0x9E3779B97F4A7C15UL + (ulong)turn * (ulong)lookers);
        int start = (int)(mixed % (ulong)ids.Length);
        var result = new long[lookers];
        for (int i = 0; i < lookers; i++)
            result[i] = ids[(start + i) % ids.Length];
        return result;
    }

    /// <summary>Whether <paramref name="self"/> is one of this turn's lookers. Pure.</summary>
    public static bool IsLooker(long self, IReadOnlyCollection<long> memberIds, long now) =>
        Array.IndexOf(Lookers(memberIds, now), self) >= 0;

    /// <summary>
    /// Whether this bot scans now, and why. <paramref name="applies"/> is false when the rule does not apply (the
    /// setting off, the bot out of the tactics' scope or at an objective): everyone scans, as before. Pure.
    /// </summary>
    public static RvrScanRole Decide(bool applies, int lookingMembers, bool isLooker, bool alerted, bool groupFighting,
        bool planHasTarget)
    {
        if (!applies)
            return RvrScanRole.Everyone;
        if (lookingMembers <= LookersPerGroup)
            return RvrScanRole.Solo;
        if (isLooker)
            return RvrScanRole.Looker;
        return alerted || groupFighting || planHasTarget ? RvrScanRole.Woken : RvrScanRole.Idle;
    }

    /// <summary>Whether a bot with this role runs its scan.</summary>
    public static bool Scans(RvrScanRole role) => role != RvrScanRole.Idle;
}

/// <summary>
/// The frontier scan's budget per realm, counted as it runs and logged once a minute as RVR_SCAN_BUDGET (work package
/// 1): scans (area searches), the roles of the bots that scanned, the bots that did not (idle non-lookers), notice
/// rolls, rolls refused by the observer cap, line-of-sight rays, notices and stealth rolls. Thread-safe.
/// </summary>
public static class RvrScanBudget
{
    public const int LogIntervalMilliseconds = 60_000;

    public enum Counter
    {
        Scans,
        Everyone,
        Solo,
        Looker,
        Woken,
        Idle,
        Rolls,
        Capped,
        Rays,
        Notices,
        StealthRolls,
        /// <summary>#529 step 3: looks by fighting bots (RvrCombatRefresh).</summary>
        CombatRefreshes,
        /// <summary>#529 step 3: the line-of-sight rays those looks cast (not in Rays).</summary>
        CombatRays,
    }

    private const int Counters = (int)Counter.CombatRays + 1;
    private const int Realms = 4;
    private static readonly long[] Values = new long[Realms * Counters];
    private static long _nextLog;
    private static long _windowStart;

    private static int Index(eRealm realm) => Math.Clamp((int)realm, 0, Realms - 1);

    public static void Add(eRealm realm, Counter counter, long amount = 1) =>
        Interlocked.Add(ref Values[Index(realm) * Counters + (int)counter], amount);

    public static void Add(eRealm realm, RvrScanRole role) => Add(realm, role switch
    {
        RvrScanRole.Solo => Counter.Solo,
        RvrScanRole.Looker => Counter.Looker,
        RvrScanRole.Woken => Counter.Woken,
        RvrScanRole.Idle => Counter.Idle,
        _ => Counter.Everyone,
    });

    public static long Read(eRealm realm, Counter counter) => Interlocked.Read(ref Values[Index(realm) * Counters + (int)counter]);

    /// <summary>
    /// When a minute has passed since the last line, takes the counts of every realm that scanned (and resets them) for
    /// the caller to log; null otherwise. Only one caller wins each minute.
    /// </summary>
    public static List<string> TakeLines(long now)
    {
        long next = Interlocked.Read(ref _nextLog);
        if (next == 0)
        {
            if (Interlocked.CompareExchange(ref _nextLog, now + LogIntervalMilliseconds, 0) == 0)
                Interlocked.Exchange(ref _windowStart, now);
            return null;
        }
        if (now < next || Interlocked.CompareExchange(ref _nextLog, now + LogIntervalMilliseconds, next) != next)
            return null;
        long seconds = Math.Max(1, (now - Interlocked.Exchange(ref _windowStart, now)) / 1000);
        var lines = new List<string>(3);
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            var counts = new long[Counters];
            for (int i = 0; i < Counters; i++)
                counts[i] = Interlocked.Exchange(ref Values[Index(realm) * Counters + i], 0);
            if (counts[(int)Counter.Scans] == 0 && counts[(int)Counter.Idle] == 0 && counts[(int)Counter.CombatRefreshes] == 0)
                continue;
            lines.Add(Format(GlobalConstants.RealmToName(realm), seconds, counts));
        }
        return lines;
    }

    /// <summary>The RVR_SCAN_BUDGET line for one realm's counts (indexed by <see cref="Counter"/>). Pure.</summary>
    public static string Format(string realm, long seconds, IReadOnlyList<long> c) =>
        $"RVR_SCAN_BUDGET realm={realm} seconds={seconds} scans={c[(int)Counter.Scans]} " +
        $"scansPerSecond={c[(int)Counter.Scans] / (double)Math.Max(1, seconds):0.#} lookers={c[(int)Counter.Looker]} " +
        $"solo={c[(int)Counter.Solo]} woken={c[(int)Counter.Woken]} everyone={c[(int)Counter.Everyone]} " +
        $"idle={c[(int)Counter.Idle]} rolls={c[(int)Counter.Rolls]} capped={c[(int)Counter.Capped]} " +
        $"rays={c[(int)Counter.Rays]} raysPerSecond={c[(int)Counter.Rays] / (double)Math.Max(1, seconds):0.#} " +
        $"notices={c[(int)Counter.Notices]} stealthRolls={c[(int)Counter.StealthRolls]} " +
        $"combatRefreshes={c[(int)Counter.CombatRefreshes]} combatRays={c[(int)Counter.CombatRays]} " +
        $"combatRaysPerSecond={c[(int)Counter.CombatRays] / (double)Math.Max(1, seconds):0.#}";

    /// <summary>Zeroes every count and restarts the minute (tests).</summary>
    public static void Reset()
    {
        for (int i = 0; i < Values.Length; i++)
            Interlocked.Exchange(ref Values[i], 0);
        Interlocked.Exchange(ref _nextLog, 0);
        Interlocked.Exchange(ref _windowStart, 0);
    }
}

/// <summary>
/// Which of work package 1's rules apply to one bot's frontier scan (plain values). All need rvr_bot_perception (the
/// player rule) and a bot in the tactics' scope (RvrTacticsScope: not at an objective, no raid, battleground assault or
/// player's group). <see cref="PerceiveBots"/> (rvr_bot_perception_bots): enemy gamebots and their pets are noticed by
/// the player rule, the scan looks only to where noticing stops, and stealthed enemies may be noticed close up.
/// <see cref="Lookers"/> (rvr_bot_group_lookers): two lookers per group.
/// </summary>
public readonly record struct RvrFieldRules(bool PerceiveBots, bool Lookers, bool Stealth, bool FieldRadius)
{
    public static RvrFieldRules None => default;

    /// <summary>The rules for a bot from the settings and its scope. Pure.</summary>
    public static RvrFieldRules For(bool perception, bool inScope, RvrTacticsSwitches switches)
    {
        if (!perception || !inScope || !switches.FieldTactics)
            return None;
        return new(switches.PerceptionBots, switches.GroupLookers, switches.PerceptionBots, switches.PerceptionBots);
    }
}
