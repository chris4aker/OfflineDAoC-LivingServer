using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Marfoirwort's Life-leech (Living Server 1.2.1, #412; tests only). His script (Marfoirwort.cs, unchanged) casts
    /// Marfoirwort Life-leech on a quarter of his melee hits: 450 body damage to his target and to every enemy within 350 of
    /// it, raiders and their pets alike, and his DealDamage heals him by every point of body damage he deals. Owner's PC,
    /// build ccba364, 2026-11-16 (sweep 3, 17:21-18:40): attempt 1 held him at 79% at the lowest until "no damage for five
    /// minutes" ended it with raiders still on him (RealmRaidDragonFight.AttemptOver), then two wipes in 77 minutes. The
    /// catalog's spread (350) moved the casters, archers and healers outside his target's party, but not the melee, the
    /// target's own party or the pets: bot melee reach is 200 and his target stood about 131-150 from him, so every melee
    /// spot lay within 350 of it (150 + 195 = 345 at the most) and each leech healed him from all of them.
    /// <para>Here: his target holds <see cref="BehindDistance"/> (185) from him, every other melee raider fights from
    /// behind him on the line from his target at <see cref="LeechBehindFor"/> (185-195), which is 370-380 from his target,
    /// clear of the 350; every raider at range steps clear of it, his target's party too; no bot's pet attacks him (a pet at
    /// his feet heals him by about 380 a leech and deals far less). The boss is unchanged.</para>
    /// <para>Log: EPIC_LEECH_EXPOSURE (each 15 s per boss: how many raiders and pets stood within 350 of his target) and
    /// the catalog's EPIC_SPREAD_LANDED lines (within=). Autonomous gamebots only, as every tactic.</para>
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>His Life-leech's radius around his target (Marfoirwort.MarfoirwortDD).</summary>
        public const int LeechRadius = 350;
        /// <summary>A raider this close past the radius counts as inside (positions drift a little between turns).</summary>
        public const int LeechMargin = 10;
        /// <summary>EPIC_LEECH_EXPOSURE is written at most this often per boss.</summary>
        public const int LeechExposureLogMilliseconds = 15_000;

        [DungeonRules]
        private static IEnumerable<DungeonRule> MarfoirwortRules() =>
        [
            new("MarfoirwortLeech", [nameof(Marfoirwort)], MarfoirwortLeechAdvice)
            {
                Summary = $"His Life-leech hits everyone within {LeechRadius} of his target and heals him by all of it: his target " +
                          $"holds {BehindDistance} from him, the other melee fight from behind him ({BehindDistance}-{BehindReach} on " +
                          $"the line from his target, so 370-380 from it), everyone at range stands clear of {LeechRadius} around " +
                          "his target (its own party too), and no bot's pet attacks him.",
                NoPets = _ => true,
            },
        ];

        /// <summary>How far behind him a melee raider stands for the leech (pure): far enough that it stands
        /// <see cref="LeechRadius"/> + 2 x <see cref="LeechMargin"/> from his target, within bot melee reach.</summary>
        public static float LeechBehindFor(float anchorToBoss) =>
            Math.Clamp(LeechRadius + 2 * LeechMargin - anchorToBoss, BehindDistance, BehindReach);

        /// <summary>
        /// Where a melee raider (not his target) stands so that his Life-leech on his target misses it (pure): null when it
        /// already stands further than <see cref="LeechRadius"/> + <see cref="LeechMargin"/> from his target, else the spot
        /// behind him on the line from his target at <see cref="LeechBehindFor"/>.
        /// </summary>
        public static Vector3? LeechMeleeSpot(Vector3 raider, Vector3 boss, Vector3 anchor)
        {
            if (Flat(raider, anchor) > LeechRadius + LeechMargin) return null;
            return EncounterMeleeStations.BehindSpot(boss, anchor, LeechBehindFor(Flat(anchor, boss)), raider.Z);
        }

        /// <summary>Where a raider at range stands clear of the leech (pure): null when clear, else out on its side.</summary>
        public static Vector3? LeechRangeSpot(Vector3 raider, Vector3 boss, Vector3 anchor)
        {
            if (Flat(raider, anchor) > LeechRadius + LeechMargin) return null;
            return SpreadSpot(raider, boss, anchor, LeechRadius, true);
        }

        private static Advice? MarfoirwortLeechAdvice(RuleContext c)
        {
            GameNPC boss = c.Target;
            if (!boss.InCombat || boss.TargetObject is not GameLiving anchor || !anchor.IsAlive) return null;
            Vector3 at = c.At, bossAt = At(boss), anchorAt = At(anchor);
            TraceSpreadTarget(boss, anchor, LeechRadius, c.Now);
            if (c.Raider == anchor)
            {
                // His target keeps 185 from him (he closes to about 131 himself): the melee behind him then clear the 350.
                EncounterMeleeStations.Hold(c.Raider, boss, BehindDistance, c.Now);
                EncounterMeleeStations.Apply(c.Raider, boss, BehindDistance);
                LogLeechExposure(boss, anchor, c.Live, c.Now);
                return null;
            }
            if (c.AtRange)
                return LeechRangeSpot(at, bossAt, anchorAt) is Vector3 clear
                    ? new(BotAction.Reposition, boss, clear, $"leech: clear of {LeechRadius} around \"{anchor.Name}\" (it heals him)")
                    : null;
            EncounterMeleeStations.Hold(c.Raider, boss, BehindDistance, c.Now);
            EncounterMeleeStations.Apply(c.Raider, boss, BehindDistance);
            return LeechMeleeSpot(at, bossAt, anchorAt) is Vector3 behind
                ? new(BotAction.Reposition, boss, behind,
                    $"leech: melee behind \"{boss.Name}\", clear of {LeechRadius} around \"{anchor.Name}\"")
                : null;
        }

        private static readonly Dictionary<GameNPC, long> LastLeechExposureLog = new();

        /// <summary>How many raiders (bots and players) and pets stand within <see cref="LeechRadius"/> of <paramref name="anchor"/>
        /// besides it (pure): what one Life-leech would heal him from.</summary>
        public static (int Raiders, int Pets) LeechExposure(Vector3 anchor, IEnumerable<(Vector3 At, bool Pet)> others) =>
            (others ?? []).Where(o => Flat(o.At, anchor) <= LeechRadius)
                .Aggregate((0, 0), (n, o) => o.Pet ? (n.Item1, n.Item2 + 1) : (n.Item1 + 1, n.Item2));

        private static void LogLeechExposure(GameNPC boss, GameLiving anchor, IReadOnlyList<GameNPC> live, long now)
        {
            if (!Log.IsInfoEnabled) return;
            lock (LastLeechExposureLog)
            {
                if (LastLeechExposureLog.TryGetValue(boss, out long last) && now - last < LeechExposureLogMilliseconds) return;
                if (LastLeechExposureLog.Count > 64) LastLeechExposureLog.Clear();
                LastLeechExposureLog[boss] = now;
            }
            Vector3 a = At(anchor);
            var others = new List<(Vector3, bool)>();
            foreach (GameNPC n in live ?? [])
                if (n != null && n != anchor && n != boss && n.IsAlive && n.CurrentRegionID == boss.CurrentRegionID && n.Realm != boss.Realm &&
                    n.Realm != 0)
                    others.Add((At(n), n is not GameBot));
            foreach (GamePlayer p in anchor.GetPlayersInRadius((ushort)(LeechRadius + 50)))
                if (p != null && p != anchor && p.IsAlive) others.Add((At(p), false));
            var (raiders, pets) = LeechExposure(a, others);
            Log.Info($"EPIC_LEECH_EXPOSURE boss=\"{boss.Name}\" region={boss.CurrentRegionID} health={boss.HealthPercent} " +
                     $"target=\"{anchor.Name}\" targetToBoss={(int)Flat(a, At(boss))} inside={raiders} pets={pets} radius={LeechRadius}");
        }
    }
}
