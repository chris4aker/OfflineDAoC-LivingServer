using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Flood guards for the boss mechanics that count raiders (1.2, epic wow). Since the bosses pick and count bots too
    /// (EncounterTargets), a mechanic that makes an add per raider, scales by the raiders in reach or heals per dead raider
    /// grows with the raid. A bot raid runs at up to 200% of its period size plus the Epic Special Forces
    /// (docs/RAID-SIZES.md), so without a guard it would face far more than a full raid of 2003 players did.
    /// <para>
    /// Each guard caps one count at the encounter's period raid size: the full, comfortable 2003 raid there
    /// (<see cref="AutonomousRealmRaid.Definitions"/>' SuggestedBots). Up to that size the mechanic is exactly the original
    /// one; above it, the bot raid faces what a full player raid of the period size faced, never less. Every guard is a
    /// per-encounter setting: <see cref="SetLimit"/> with a number (0 = no cap: the count grows with the raid) or null (the
    /// shipped value).
    /// </para>
    /// </summary>
    public static class EncounterFloodGuard
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(EncounterFloodGuard));

        /// <summary>Heals per dead raider count in this rolling window (a raid's wipe, rez and run back fits in it).</summary>
        public const int RecentWindowMilliseconds = 300_000;

        /// <summary>One guard: what it counts, the raid whose period size caps it (or a fixed size), and why.</summary>
        public sealed record Guard(string Key, string RaidId, int FixedSize, string Counts);

        /// <summary>The shipped guards (the PR lists the decision for each, and the encounters that need none).</summary>
        public static readonly Guard[] Shipped =
        [
            new("Legion.Adds", "darkness-albion", 0,
                "raiders Legion counts within 2,000 at each 5% step; adds = raiders / 4 (64 -> 16 a step)"),
            new("Legion.KillHeal", "darkness-albion", 0,
                "raiders whose death heals Legion 2.5% in 5 minutes (64 -> at most 160% of his health)"),
            new("HighLordSaeor.KillHeal", "darkness-albion", 0,
                "raiders whose death heals High Lord Saeor by their health in 5 minutes"),
            new("Beliathan.Minions", "darkness-albion", 0,
                "live minions (one rises from each raider Beliathan kills)"),
            new("Nosdoden.Spirits", "dungeon-midgard-trollheim", 0,
                "live 'Spirit of ...' ghosts (one from each raider Nosdoden kills; it dies when its raider is rezzed)"),
            new("OFGreenKnight.Trees", "dragon-albion", 0,
                "trees in one wave (one at each raider within 4,000, every 25-35 s)"),
            new("Oona.Undead", "dragon-midgard", 0,
                "live undead (one from each raider Oona kills)"),
            // Not in docs/RAID-SIZES.md (no raid goes there): the smallest period raid size (Dodens Gruva, Tur Suil).
            new("RisnirCruss.Apparitions", null, 40,
                "live apparitions (one from each raider Risnir Cruss kills)"),
        ];

        private static readonly ConcurrentDictionary<string, int> Overrides = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, long> LastLogged = new(StringComparer.Ordinal);

        /// <summary>The period raid size of a raid definition (<see cref="AutonomousRealmRaid.Definitions"/>), or 0.</summary>
        public static int PeriodRaidSize(string raidId) =>
            raidId == null ? 0 : AutonomousRealmRaid.Definitions.FirstOrDefault(d => d.Id == raidId)?.SuggestedBots ?? 0;

        /// <summary>The shipped cap of a guard (its raid's period size, or its fixed size); 0 for an unknown guard.</summary>
        public static int ShippedLimit(string key)
        {
            Guard guard = Shipped.FirstOrDefault(g => g.Key == key);
            if (guard == null) return 0;
            int size = PeriodRaidSize(guard.RaidId);
            return size > 0 ? size : guard.FixedSize;
        }

        /// <summary>The cap in force for a guard: its setting, else the shipped one. 0 = no cap.</summary>
        public static int Limit(string key) => Overrides.TryGetValue(key, out int v) ? v : ShippedLimit(key);

        /// <summary>The per-encounter setting: a cap (0 = none: the count grows with the raid), or null for the shipped one.</summary>
        public static void SetLimit(string key, int? value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (value is int v) Overrides[key] = Math.Max(0, v);
            else Overrides.TryRemove(key, out _);
        }

        /// <summary>Back to the shipped caps (tests).</summary>
        public static void Reset()
        {
            Overrides.Clear();
            LastLogged.Clear();
        }

        /// <summary>Every guard and its cap in force, for logs ("Legion.Adds=64").</summary>
        public static string[] Values() => Shipped.Select(g => $"{g.Key}={Limit(g.Key)}").ToArray();

        /// <summary>A count of raiders, capped: <paramref name="count"/> itself up to the cap.</summary>
        public static int Cap(string key, int count)
        {
            int limit = Limit(key);
            if (limit <= 0 || count <= limit) return count;
            Held(key, $"count={count} cap={limit}");
            return limit;
        }

        /// <summary>May one more add of this kind rise while <paramref name="alive"/> of them are up?</summary>
        public static bool MayAdd(string key, int alive)
        {
            int limit = Limit(key);
            if (limit <= 0 || alive < limit) return true;
            Held(key, $"alive={alive} cap={limit}");
            return false;
        }

        /// <summary>
        /// One more per-raider event (a heal by a death) for <paramref name="source"/> now: true and counted while fewer than the
        /// cap happened in the last <see cref="RecentWindowMilliseconds"/>; false (not counted) at the cap.
        /// </summary>
        public static bool TryRecent(GameLiving source, string key, long now)
        {
            int limit = Limit(key);
            if (limit <= 0 || source == null) return true;
            string property = "epic_flood_guard_" + key;
            Queue<long> recent = source.TempProperties.GetProperty<Queue<long>>(property);
            if (recent == null)
            {
                recent = new Queue<long>();
                source.TempProperties.SetProperty(property, recent);
            }
            lock (recent)
            {
                while (recent.Count > 0 && now - recent.Peek() >= RecentWindowMilliseconds)
                    recent.Dequeue();
                if (recent.Count >= limit)
                {
                    Held(key, $"recent={recent.Count} cap={limit} window={RecentWindowMilliseconds / 1000}s");
                    return false;
                }
                recent.Enqueue(now);
                return true;
            }
        }

        private static void Held(string key, string detail)
        {
            long now = Environment.TickCount64;
            if (LastLogged.TryGetValue(key, out long last) && now - last < 60_000) return;
            LastLogged[key] = now;
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_FLOOD_GUARD guard={key} {detail} (the period raid's count: docs/RAID-SIZES.md)");
        }
    }
}
