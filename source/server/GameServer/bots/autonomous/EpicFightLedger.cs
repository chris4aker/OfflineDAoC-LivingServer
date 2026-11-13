using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Where a raid's damage goes, and what hurts it (Living Server 1.2, diagnostics only: nothing here changes a fight).
    /// While a raid's dungeon route watches a region (RealmRaidDungeonRoute, every pass), every hit in that region is added
    /// up by who was hit: damage raiders dealt to each kind of monster, with hits and how many raiders dealt it, and damage
    /// each kind of monster dealt to raiders. The route writes one EPIC_FIGHT_LEDGER line a minute and the sums start again.
    /// Why: play tests duo6-duo27 (Spindler Broodmother, Govannon) counted deaths and health, and could not say whether the
    /// raid's damage was too small, or went to the wrong target.
    /// </summary>
    public static class EpicFightLedger
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicFightLedger));

        /// <summary>A region stays watched this long after its route last looked at it.</summary>
        public const long WatchMs = 120_000;

        private sealed class Row
        {
            public long Damage;
            public int Hits;
            public readonly HashSet<int> Who = new();
        }

        private static readonly ConcurrentDictionary<ushort, long> Watched = new();
        private static readonly Dictionary<(ushort Region, bool ToRaid, string Name), Row> Rows = new();

        public static void Watch(ushort region, long now) => Watched[region] = now + WatchMs;

        public static bool IsWatched(ushort region, long now) => Watched.TryGetValue(region, out long until) && now <= until;

        /// <summary>A hit (GameLiving.TakeDamage). Cheap when no raid watches the region.</summary>
        public static void Note(GameLiving victim, GameObject source, int amount)
        {
            if (amount <= 0 || Watched.IsEmpty || victim == null || source is not GameLiving attacker)
                return;
            ushort region = victim.CurrentRegionID;
            if (!IsWatched(region, GameLoop.GameLoopTime))
                return;
            bool victimRaider = IsRaider(victim), attackerRaider = IsRaider(attacker);
            if (victimRaider == attackerRaider)
                return;
            // Dealt by the raid: by the monster hit. Taken by the raid: by the monster that hit.
            Add(region, victimRaider, victimRaider ? attacker.Name : victim.Name, victimRaider ? victim.ObjectID : attacker.ObjectID, amount);
        }

        private static bool IsRaider(GameLiving living) =>
            living is GamePlayer or GameBot || living is GameNPC { Brain: IControlledBrain { Owner: GamePlayer or GameBot } };

        /// <summary>Pure core: one hit of <paramref name="amount"/> on or by <paramref name="name"/>, <paramref name="who"/> the raider in it.</summary>
        public static void Add(ushort region, bool toRaid, string name, int who, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(name))
                return;
            lock (Rows)
            {
                if (!Rows.TryGetValue((region, toRaid, name), out Row row))
                    Rows[(region, toRaid, name)] = row = new Row();
                row.Damage += amount;
                row.Hits++;
                row.Who.Add(who);
            }
        }

        /// <summary>
        /// The region's sums since the last call, largest first, as "name:damage/hits/raiders" lists (dealt: raiders who hit
        /// it; took: raiders it hit), and the sums start again. Empty strings when nothing was hit.
        /// </summary>
        public static (string Dealt, string Took) Flush(ushort region, int most = 6)
        {
            List<(bool ToRaid, string Name, Row Row)> rows;
            lock (Rows)
            {
                rows = Rows.Where(pair => pair.Key.Region == region).Select(pair => (pair.Key.ToRaid, pair.Key.Name, pair.Value)).ToList();
                foreach (var row in rows)
                    Rows.Remove((region, row.ToRaid, row.Name));
            }
            string Side(bool toRaid) => string.Join(",", rows.Where(row => row.ToRaid == toRaid).OrderByDescending(row => row.Row.Damage).Take(most)
                .Select(row => $"{row.Name}:{row.Row.Damage}/{row.Row.Hits}/{row.Row.Who.Count}"));
            return (Side(false), Side(true));
        }

        /// <summary>One EPIC_FIGHT_LEDGER line for the region when anything was hit since the last one.</summary>
        public static void Report(ushort region, string target, int seconds)
        {
            (string dealt, string took) = Flush(region);
            if ((dealt.Length > 0 || took.Length > 0) && Log.IsInfoEnabled)
                Log.Info($"EPIC_FIGHT_LEDGER region={region} target=\"{target}\" seconds={seconds} raid_dealt=\"{dealt}\" raid_took=\"{took}\"");
        }

        public static void ResetForTests()
        {
            Watched.Clear();
            lock (Rows)
                Rows.Clear();
        }
    }
}
