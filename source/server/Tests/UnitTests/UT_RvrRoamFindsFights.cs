using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #417 "Roaming eight-man RvR groups rarely find a fight" (owner's PC, `19e7037`: 96 RVR_ROAM_CHECK, 4 RVR_ROAM_HUNT).
    /// The eights' circuits were weighed by enemy levelling bots only, so with none about every circuit weighed the same,
    /// and the circuit replaced the planner's hunt of a live enemy force. Now the circuits also count the enemy RvR bots
    /// near their spots, and an eight follows the planner's hunt of a near enemy force. Pure.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrRoamFindsFights
    {
        private static RvrCheckSpotCatalog Catalog()
        {
            var assembly = typeof(RvrCheckSpots).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("rvr-checkspots.json", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            return RvrCheckSpotCatalog.Parse(reader.ReadToEnd());
        }

        private const long Minute = 60_000;

        [Test]
        public void With_no_levellers_about_the_circuit_with_the_enemy_rvr_bots_draws_the_eight()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit[] midgard = catalog.Frontier(1).Circuits.Where(c => c.Realm == eRealm.Midgard).ToArray();
            long now = 500 * Minute;
            // An Albion warband of eight at the Benowyc camps (only on the east-wall circuit); no levellers anywhere.
            RvrSpotPrey Prey(RvrFrontierPoi spot) => spot.Id == "hw-benowyc-north-camps" ? new RvrSpotPrey(0, 0, 8) : default;
            var weighted = midgard.Select(c => (c, RvrRoamCircuits.CircuitWeight(c, catalog, Prey, now, true))).ToArray();
            var picks = new Dictionary<string, int>();
            for (int k = 0; k < 1_000; k++)
            {
                RvrRoamCircuit picked = RvrRoamCircuits.Pick(weighted, k / 1_000.0);
                picks[picked.Id] = picks.GetValueOrDefault(picked.Id) + 1;
            }
            Assert.That(picks.GetValueOrDefault("alb-mid-1-east-wall"), Is.GreaterThan(800), "where the enemy is");
            Assert.That(picks.GetValueOrDefault("alb-mid-2-pennine-pass"), Is.GreaterThan(0), "a quiet circuit still gets a little");
            Assert.That(RvrRoamCircuits.CircuitRoamers(midgard.Single(c => c.Id == "alb-mid-1-east-wall"), catalog, Prey, true), Is.EqualTo(8));
            Assert.That(RvrRoamCircuits.CircuitRoamers(midgard.Single(c => c.Id == "alb-mid-2-pennine-pass"), catalog, Prey, true), Is.Zero);
        }

        [Test]
        public void The_enemy_count_weighs_for_eights_only_and_is_capped()
        {
            long now = 500 * Minute;
            Assert.That(RvrRoamCircuits.SpotWeight(new(0, 0, 8), now), Is.EqualTo(RvrRoamCircuits.BaseWeight),
                "soloers and duos still go by the levellers");
            Assert.That(RvrRoamCircuits.EightSpotWeight(new(0, 0, 0), now), Is.EqualTo(RvrRoamCircuits.BaseWeight));
            Assert.That(RvrRoamCircuits.EightSpotWeight(new(0, 0, 4), now), Is.GreaterThan(RvrRoamCircuits.EightSpotWeight(new(0, 0, 2), now)));
            Assert.That(RvrRoamCircuits.EightSpotWeight(new(0, 0, 200), now),
                Is.EqualTo(RvrRoamCircuits.EightSpotWeight(new(0, 0, RvrRoamCircuits.RoamerCountCap), now)), "one siege army does not pull every eight");
            Assert.That(RvrRoamCircuits.EightSpotWeight(new(1, now, 0), now), Is.EqualTo(RvrRoamCircuits.SpotWeight(new(1, now), now)),
                "the levellers weigh as before");
        }

        [Test]
        public void Enemy_rvr_bots_count_over_a_wider_ring_and_each_realm_sees_the_others()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrFrontierPoi spot = catalog.Spot("hw-benowyc-north-camps");
            var rvr = new List<(ushort, float, float, eRealm)>
            {
                (1, spot.Position.X, spot.Position.Y, eRealm.Albion),
                (1, spot.Position.X + spot.Radius + 3_000, spot.Position.Y, eRealm.Albion),   // inside the wider ring
                (1, spot.Position.X + spot.Radius + RvrRoamCircuits.RoamerDrawRadius + 200, spot.Position.Y, eRealm.Albion), // outside it
                (100, spot.Position.X, spot.Position.Y, eRealm.Albion),                        // another region
                (1, spot.Position.X, spot.Position.Y, eRealm.Hibernia),
            };
            Dictionary<string, int[]> near = RvrRoamCircuits.Tally(catalog, rvr, RvrRoamCircuits.RoamerDrawRadius);
            Assert.That(near[spot.Id][(int)eRealm.Albion], Is.EqualTo(2));
            Assert.That(RvrRoamCircuits.Tally(catalog, rvr)[spot.Id][(int)eRealm.Albion], Is.EqualTo(1), "the levellers' tally keeps the spot's own radius");
            try
            {
                RvrRoamPrey.Set(1_000, null, null, near);
                Assert.That(RvrRoamPrey.For(spot, eRealm.Midgard), Is.EqualTo(new RvrSpotPrey(0, 0, 3)));
                Assert.That(RvrRoamPrey.For(spot, eRealm.Albion), Is.EqualTo(new RvrSpotPrey(0, 0, 1)), "never its own realm");
                Assert.That(RvrRoamPrey.For(spot, eRealm.Hibernia), Is.EqualTo(new RvrSpotPrey(0, 0, 2)));
            }
            finally { RvrRoamPrey.Set(long.MinValue, null, null); }
        }

        [Test]
        public void An_eight_follows_the_planners_hunt_of_a_near_enemy_force_only()
        {
            Assert.That(RvrRoamCircuits.EightFollowsHunt(true, 2_500), Is.True);
            Assert.That(RvrRoamCircuits.EightFollowsHunt(true, RvrRoamCircuits.ChaseRadius), Is.True);
            Assert.That(RvrRoamCircuits.EightFollowsHunt(true, RvrRoamCircuits.ChaseRadius + 1), Is.False, "far: the circuit, which the count now draws");
            Assert.That(RvrRoamCircuits.EightFollowsHunt(false, 100), Is.False, "another frontier: no porter ride for one enemy");
        }
    }
}
