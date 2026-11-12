using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Avalon City's encounter rules (wave 2, 1.2): Dra'argus the Mighty's Drau'gyn Sphere. The catalog rows (the sphere as
    /// his required add, Sys'sro's pit monsters as hazards) come from the hand notes.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> AvalonCityRules() =>
        [
            new(nameof(DraargusMighty), [nameof(DraargusMighty), nameof(DraugynSphere)], DraugynSphereFirst)
            {
                Summary = "Casters and archers kill the Drau'gyn Sphere from range; melee wait 850 from it, then fight him.",
            },
        ];

        /// <summary>The sphere's blast (Sphere Explosion, 300 heat every 4 s) reaches 500; its pull takes a raider on its aggro
        /// list standing 200-300 from it into its middle.</summary>
        public const int SphereBlastRadius = 500;
        /// <summary>Raiders who can't shoot it wait this far from it; casters and archers keep at least the inner distance.</summary>
        public const int SphereWaitDistance = 850, SphereCasterDistance = 650;

        /// <summary>
        /// While the Drau'gyn Sphere burns Dra'argus takes no damage and doesn't attack, and the sphere floats above the floor
        /// burning everyone within 500 and pulling the raiders at its edge into its middle. Casters and archers kill it from
        /// range (it is his required add in the catalog; they step out to 650 first if they stand closer); melee, tanks and
        /// healers wait 850 from it, keeping him as their target, and go in once it is dead.
        /// </summary>
        private static Advice? DraugynSphereFirst(RuleContext c)
        {
            DraugynSphere sphere = c.Target as DraugynSphere ?? NearestLive<DraugynSphere>(c.Target, c.Live, 3_000);
            if (!Present(sphere)) return null;
            return SphereAdvice(At(c.Raider), c.Target, sphere, c.CanShootFlyers);
        }

        public static Advice? SphereAdvice(Vector3 raider, GameNPC target, GameNPC sphere, bool atRange)
        {
            float d = Flat(raider, At(sphere));
            if (atRange)
                return d < SphereCasterDistance
                    ? new Advice(BotAction.Reposition, target, PointAway(raider, At(sphere), SphereCasterDistance + 150),
                        "sphere-first: out of the sphere's blast, then shoot it")
                    : null;
            return new Advice(BotAction.Reposition, target, d < SphereWaitDistance - 100 ? PointAway(raider, At(sphere), SphereWaitDistance) : raider,
                "sphere-first: he can't be hurt while the sphere burns; wait out of its reach");
        }
    }
}
