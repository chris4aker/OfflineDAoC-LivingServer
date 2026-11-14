using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Tur Suil's raid rule (Living Server 1.2, every big boss in): Balor's eye. At each 10% Balor loses (90% to 10%) an Eye
    /// of Balor appears above him (BalorBrain.SpawnEyeOfBalor): untargetable, it never moves, picks one raider within 2,500
    /// and 3 s later starts Balor's Eye Light on it, 1,000 heat with a 4 s cast nobody interrupts, again and again until it
    /// vanishes 18 s after it came (BalorEye.RemoveEye). The period: "The eye laser shoots through walls, cannot be
    /// interrupted, and can kill anyone in two hits or fewer" (Allakhazam, Balor, 24 Aug 2003). Walls don't save the raider;
    /// range does (the spell reaches 1,800): the picked raider, unless it is the one Balor fights, runs out of 1,800 of the
    /// eye and waits there until it is gone, and the healers heal it first (the script raises the pick as a glare, radius 0).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>Balor's Eye Light's range (BalorEyeBrain.EyeDD) and how far from the eye its raider runs.</summary>
        public const int BalorEyeRange = 1_800, BalorEyeOutrun = 1_950;

        /// <summary>The raider the eye picked (BalorEyeBrain.RandomTarget). Settable for tests.</summary>
        public static Func<GameLiving> BalorEyeVictim = () => DOL.AI.Brain.BalorEyeBrain.RandomTarget;

        [DungeonRules]
        private static IEnumerable<DungeonRule> TurSuilRules() =>
        [
            new(nameof(Balor), [nameof(Balor)], BalorEyeRule)
            {
                Summary = "The raider his eye picks (1,000 heat a cast, through walls, no interrupt) runs out of the eye's " +
                          "1,800 and waits there until the eye is gone, unless Balor fights it; the healers heal it first.",
            },
        ];

        private static Advice? BalorEyeRule(RuleContext c)
        {
            if (BalorEyeVictim() is not GameLiving victim || victim != c.Raider) return null;
            GameNPC eye = (c.Live ?? []).FirstOrDefault(n => Present(n) && n is BalorEye && n.CurrentRegionID == c.Target.CurrentRegionID);
            return eye == null ? null : BalorEyeAdvice(c.At, PosOf(eye), c.Target, c.Target.TargetObject == c.Raider);
        }

        /// <summary>The eye's raider (pure but for the advice's target): Balor's own target holds him; anyone else runs to
        /// <see cref="BalorEyeOutrun"/> from the eye, away from it, and once out of its range waits where it stands.</summary>
        public static Advice? BalorEyeAdvice(Vector3 at, Vector3 eye, GameNPC balor, bool isHisTarget)
        {
            if (isHisTarget) return null;
            if (Flat(at, eye) > BalorEyeRange + SpreadMargin)
                return new(BotAction.StandOff, balor, null, $"balor's eye: out of its {BalorEyeRange}, wait until it is gone", "epic_glare");
            return new(BotAction.Reposition, balor, PointAway(at, eye, BalorEyeOutrun),
                $"balor's eye: its light reaches {BalorEyeRange} and goes through walls; run out of it", "epic_glare");
        }
    }
}
