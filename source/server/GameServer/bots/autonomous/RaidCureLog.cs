using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Raid cure-poison casts in the log (Living Server 1.2, sweep 6f). Bots already cure a damage over time with their cure
    /// poison spells (BotBrain's heal turn: CurePoison or CurePoisonGroup on a member of their own party whose
    /// <see cref="GameLiving.IsPoisoned"/> is true, which every DamageOverTime effect makes it; Summoner Roesia's Summoner Pain
    /// is one, and CurePoisonSpellHandler removes it), but that path wrote no log line, so the owner's PC log of b1b6846
    /// could not show whether Summoner Pain was cured. Now each region's raid cures are summed and written once a minute at
    /// most (RAID_CURE_POISON), so a fight of 70 raiders adds one line a minute, not one per cast. Logging only.
    /// </summary>
    public static class RaidCureLog
    {
        public const long WindowMilliseconds = 60_000;

        /// <summary>One region's cures since <see cref="Start"/> (pure bookkeeping).</summary>
        public sealed class Window
        {
            public long Start;
            public int Casts;
            public readonly HashSet<long> Curers = new();
            public readonly HashSet<long> Targets = new();
            public readonly Dictionary<string, int> Dots = new();

            public void Add(long curer, long target, IEnumerable<string> dots)
            {
                Casts++;
                Curers.Add(curer);
                Targets.Add(target);
                foreach (string d in dots ?? [])
                    if (!string.IsNullOrEmpty(d)) Dots[d] = Dots.GetValueOrDefault(d) + 1;
            }

            public bool Due(long now) => Casts > 0 && now - Start >= WindowMilliseconds;

            public string Line(ushort region, long now) =>
                $"RAID_CURE_POISON region={region} casts={Casts} curers={Curers.Count} targets={Targets.Count} " +
                $"seconds={(now - Start) / 1000} dots=\"{string.Join(",", Dots.OrderByDescending(d => d.Value).Select(d => $"{d.Key} x{d.Value}"))}\"";
        }

        private static readonly Dictionary<ushort, Window> Windows = new();

        /// <summary>A raid bot started a cure poison cast on <paramref name="target"/>: counted, and the region's line written
        /// when its minute is over.</summary>
        public static void Record(GameBot curer, GameLiving target, long now)
        {
            if (curer?.IsAutonomousWorldBot != true || target == null || AutonomousRealmRaid.RaidOf(curer.Group) == null) return;
            string[] dots;
            try
            {
                dots = target.effectListComponent.GetSpellEffects(eEffect.DamageOverTime)
                    .Select(e => e?.SpellHandler?.Spell?.Name).Where(n => n != null).Distinct().ToArray();
            }
            catch (InvalidOperationException) { dots = []; }
            string line = null;
            lock (Windows)
            {
                if (!Windows.TryGetValue(curer.CurrentRegionID, out Window w))
                    Windows[curer.CurrentRegionID] = w = new Window { Start = now };
                w.Add(curer.ObjectID, target.ObjectID, dots);
                if (w.Due(now))
                {
                    line = w.Line(curer.CurrentRegionID, now);
                    Windows.Remove(curer.CurrentRegionID);
                }
            }
            if (line != null) Log.Info(line);
        }

        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RaidCureLog));
    }
}
