using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Solo camp choice when the camps a bot may take are all crowded (from Offline DAoC True 1.65, shadowofze; its owner
    /// 2026-11-10, "camps").
    /// Logs 11/08-11/10: Hibernian level 9-13 solo bots ended 16.8% of their attempts EmptyCamp (Albion 2.8%, Midgard
    /// 3.2%). After a death a bot may only take GREEN camps for a while, and in Hibernia that left a handful of level-7
    /// camps with 15-30 bots each; a bot sent away by the 15-second crowded exit often picked a copy of the same monster
    /// beside it next.
    /// Two rules, each behind its own switch:
    /// - busy siblings: a camp a bot just left as crowded, and the same-monster camps sharing its spawns, are skipped by
    ///   that bot for 10 minutes while any other camp remains (never empties the list);
    /// - widening: a solo level 8+ bot held below its natural con after deaths may take an uncrowded camp ONE con step
    ///   harder (GREEN to BLUE, BLUE to YELLOW) when every camp it may take is crowded; never the con that killed it in the
    ///   last 30 minutes and never above its natural solo ceiling. The at-camp target filter is unchanged.
    /// Pure rules plus small per-realm counters for one summary line every 10 minutes.
    /// </summary>
    public static class AutonomousCampCrowdWidening
    {
        [ServerProperty("bots", "bot_camp_busy_sibling_memory",
            "Does a solo bot that leaves a camp because it is crowded skip that camp and the same-monster camps sharing its spawns for 10 minutes (only while another camp remains)? Off: only the exact camp id is skipped, as before.", true)]
        public static bool BusySiblingMemory = true;

        [ServerProperty("bots", "bot_camp_crowd_widening",
            "May a solo level 8+ bot whose after-death limit keeps it on easier camps take an uncrowded camp one con step harder (green to blue, blue to yellow; never the con that just killed it, never above its normal solo limit) when every camp it may take is crowded? Off: it joins the least crowded of its allowed camps, as before.", true)]
        public static bool WidenWhenCrowded = true;   // owner 2026-11-10: approved ON (round-3 review asked for off)

        /// <summary>Bots below this level are never widened (kept apart from the level-1 safety rules).</summary>
        public const int MinimumLevel = 8;
        public const long BusyMilliseconds = 10 * 60_000;
        public const int MaxBusyCamps = 6;
        public const long SummaryMilliseconds = 10 * 60_000;

        /// <summary>One con step above <paramref name="current"/>, or null: already at the natural ceiling, or the step
        /// would reach the con the bot died to (<paramref name="diedTo"/>, null for none or a road death).</summary>
        public static ConColor? WidenedCeiling(ConColor current, ConColor natural, ConColor? diedTo)
        {
            if (current >= natural)
                return null;
            ConColor next = (ConColor)((int)current + 1);
            if (diedTo is ConColor died && next >= died)
                return null;
            return next;
        }

        /// <summary>Widening is only looked at for a solo, ungrouped, outdoor bot of level 8+ held below its natural
        /// ceiling, with allowed camps that are all crowded (some exist, none quiet).</summary>
        public static bool ShouldWiden(bool soloUngrouped, bool outdoor, int level, int strictCount, int quietStrictCount,
            ConColor current, ConColor natural) =>
            soloUngrouped && outdoor && level >= MinimumLevel && strictCount > 0 && quietStrictCount == 0 && current < natural;

        /// <summary>The quiet wider camps when no strict camp is quiet and some wider one is; otherwise null (today's path).</summary>
        public static T[] PickWidened<T>(T[] strictCamps, T[] widerCamps, Func<T[], T[]> quiet)
        {
            if (widerCamps == null || widerCamps.Length == 0)
                return null;
            if (quiet(strictCamps ?? Array.Empty<T>()).Length > 0)
                return null;
            T[] quietWider = quiet(widerCamps);
            return quietWider.Length > 0 ? quietWider : null;
        }

        /// <summary>The camps that share no spawns with a busy camp; the input itself when that would leave nothing or
        /// nothing is busy.</summary>
        public static T[] AvoidBusySiblings<T>(T[] camps, IReadOnlyCollection<string> busyIds, Func<T, string> id,
            Func<string, string, bool> sameSpawns)
        {
            if (camps == null || camps.Length == 0 || busyIds == null || busyIds.Count == 0)
                return camps;
            T[] kept = camps.Where(camp => !busyIds.Any(busy => sameSpawns(id(camp), busy))).ToArray();
            return kept.Length > 0 && kept.Length < camps.Length ? kept : camps;
        }

        /// <summary>Records a camp the bot left as crowded (per bot, at most <see cref="MaxBusyCamps"/>, oldest out).</summary>
        public static void RememberBusy(List<(string CampId, long Until)> busy, string campId, long now)
        {
            if (busy == null || string.IsNullOrEmpty(campId))
                return;
            busy.RemoveAll(entry => entry.Until <= now || string.Equals(entry.CampId, campId, StringComparison.OrdinalIgnoreCase));
            while (busy.Count >= MaxBusyCamps)
                busy.RemoveAt(0);
            busy.Add((campId, now + BusyMilliseconds));
        }

        /// <summary>Drops expired entries; the ids still busy (empty when none).</summary>
        public static string[] ActiveBusy(List<(string CampId, long Until)> busy, long now)
        {
            if (busy == null || busy.Count == 0)
                return Array.Empty<string>();
            busy.RemoveAll(entry => entry.Until <= now);
            return busy.Select(entry => entry.CampId).ToArray();
        }

        // ---- counters for AUTONOMOUS_CAMP_CROWD_SUMMARY (index = realm 1-3) ----
        private static readonly int[] StrictQuietCount = new int[4], WidenedCount = new int[4], CrowdedFallbackCount = new int[4],
            BlockedDiedToCount = new int[4], BusySkippedCount = new int[4];
        private static long _nextSummaryTick;

        private static int Slot(eRealm realm) => (int)realm is >= 1 and <= 3 ? (int)realm : 0;
        public static void CountStrictQuiet(eRealm realm) => Interlocked.Increment(ref StrictQuietCount[Slot(realm)]);
        public static void CountWidened(eRealm realm) => Interlocked.Increment(ref WidenedCount[Slot(realm)]);
        public static void CountCrowdedFallback(eRealm realm) => Interlocked.Increment(ref CrowdedFallbackCount[Slot(realm)]);
        public static void CountBlockedDiedTo(eRealm realm) => Interlocked.Increment(ref BlockedDiedToCount[Slot(realm)]);
        public static void CountBusySkipped(eRealm realm) => Interlocked.Increment(ref BusySkippedCount[Slot(realm)]);

        /// <summary>True at most once per <see cref="SummaryMilliseconds"/> (the first call only arms the timer); the line
        /// holds each realm's counts since the last summary, which are then reset.</summary>
        public static bool TryTakeSummary(long now, out string line)
        {
            line = null;
            long next = Interlocked.Read(ref _nextSummaryTick);
            if (next == 0)
            {
                Interlocked.CompareExchange(ref _nextSummaryTick, now + SummaryMilliseconds, 0);
                return false;
            }
            if (now < next || Interlocked.CompareExchange(ref _nextSummaryTick, now + SummaryMilliseconds, next) != next)
                return false;
            var parts = new List<string>(3);
            foreach ((int slot, string name) in new[] { (1, "alb"), (2, "mid"), (3, "hib") })
                parts.Add($"{name}: strict_quiet={Interlocked.Exchange(ref StrictQuietCount[slot], 0)} " +
                          $"widened={Interlocked.Exchange(ref WidenedCount[slot], 0)} " +
                          $"crowded_fallback={Interlocked.Exchange(ref CrowdedFallbackCount[slot], 0)} " +
                          $"blocked_died_to={Interlocked.Exchange(ref BlockedDiedToCount[slot], 0)} " +
                          $"busy_skipped={Interlocked.Exchange(ref BusySkippedCount[slot], 0)}");
            line = $"minutes={SummaryMilliseconds / 60_000} " + string.Join(" | ", parts);
            return true;
        }

        /// <summary>Test hook: forget the summary timer and counters.</summary>
        public static void ResetForTests()
        {
            Interlocked.Exchange(ref _nextSummaryTick, 0);
            for (int i = 0; i < 4; i++)
            {
                StrictQuietCount[i] = WidenedCount[i] = CrowdedFallbackCount[i] = BlockedDiedToCount[i] = BusySkippedCount[i] = 0;
            }
        }
    }
}
