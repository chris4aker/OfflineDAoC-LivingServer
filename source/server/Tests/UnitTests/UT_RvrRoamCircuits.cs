using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 6: a circuit is walked in order through its approach, each spot is checked for 30-60 s,
    /// prey is hunted, the move-on rule ends the stay within rvr_roam_move_on_minutes of a kill, and circuits are weighted
    /// by the live count of enemy levelling bots, with the reserve zones used only when nothing else has prey. Pure.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrRoamCircuits
    {
        private static RvrCheckSpotCatalog Catalog()
        {
            var assembly = typeof(RvrCheckSpots).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("rvr-checkspots.json", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            return RvrCheckSpotCatalog.Parse(reader.ReadToEnd());
        }

        private static RvrRoamCircuit Circuit(RvrCheckSpotCatalog catalog, string id) =>
            catalog.Frontiers.SelectMany(f => f.Circuits).Single(c => c.Id == id);

        private const long Minute = 60_000;

        [Test]
        public void A_walk_goes_through_its_approach_then_the_loop_in_order_and_ends_after_its_laps()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit circuit = Circuit(catalog, "hib-alb-2-crauchon");
            RvrRoamWalk walk = RvrRoamCircuits.Begin(circuit, catalog, "alb-7", false, 3 * Minute, 1_000, 1, Vector3.Zero);
            Assert.That(walk.Route, Is.EqualTo(new[] { "emain-amg", "emain-lane-hastener", "emain-crossroads", "emain-crauchon-plain",
                "emain-crauchon-road", "emain-crauchon-se-camps" }));
            Assert.That(walk.LoopStart, Is.EqualTo(2));
            Assert.That(walk.Index, Is.Zero, "from elsewhere: through the approach");

            var visited = new List<string>();
            long now = 1_000;
            for (int turn = 0; turn < 10_000 && !walk.Done; turn++)
            {
                // Two turns on the way, then at the spot until the walk moves on.
                RvrRoamStep away = RvrRoamCircuits.Step(walk, now, false, false);
                Assert.That(away.Action, Is.EqualTo(walk.ArrivedAt == 0 ? RvrRoamAction.Travel : RvrRoamAction.Check));
                string spot = walk.CurrentSpot;
                RvrRoamStep step = RvrRoamCircuits.Step(walk, now += 5_000, true, false);
                long arrived = step.Walk.ArrivedAt;
                while (step.Action == RvrRoamAction.Check)
                    step = RvrRoamCircuits.Step(step.Walk, now += 1_000, true, false);
                Assert.That(step.Action, Is.EqualTo(RvrRoamAction.Advance).Or.EqualTo(RvrRoamAction.Done));
                Assert.That(step.LeftSpot, Is.EqualTo(spot));
                Assert.That(now - arrived, Is.InRange(RvrRoamCircuits.DwellMinimumMilliseconds, RvrRoamCircuits.DwellMaximumMilliseconds + 1_000),
                    "checked for 30-60 seconds");
                visited.Add(spot);
                walk = step.Walk;
            }
            string[] loop = ["emain-crossroads", "emain-crauchon-plain", "emain-crauchon-road", "emain-crauchon-se-camps"];
            Assert.That(visited, Is.EqualTo(new[] { "emain-amg", "emain-lane-hastener" }.Concat(loop).Concat(loop)), "approach once, two laps");
            Assert.That(walk.Done, Is.True);
            Assert.That(RvrRoamCircuits.Step(walk, now, true, true).Action, Is.EqualTo(RvrRoamAction.Done));
        }

        [Test]
        public void Dwell_is_spread_and_the_same_for_the_same_walker()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit circuit = Circuit(catalog, "alb-mid-2-pennine-pass");
            var dwells = new HashSet<long>();
            for (int k = 0; k < 50; k++)
            {
                RvrRoamWalk walk = RvrRoamCircuits.Begin(circuit, catalog, $"mid-{k}", false, 3 * Minute, 0, 1, Vector3.Zero);
                Assert.That(walk.DwellMilliseconds, Is.InRange(30_000, 60_000));
                dwells.Add(walk.DwellMilliseconds);
                Assert.That(RvrRoamCircuits.Begin(circuit, catalog, $"mid-{k}", false, 3 * Minute, 0, 1, Vector3.Zero).DwellMilliseconds,
                    Is.EqualTo(walk.DwellMilliseconds));
            }
            Assert.That(dwells.Count, Is.GreaterThan(30), "not one fixed wait");
        }

        [Test]
        public void A_group_already_near_the_loop_joins_it_there()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit circuit = Circuit(catalog, "alb-mid-3-deep-pennine");
            RvrFrontierPoi third = catalog.Spot(circuit.Spots[2]);
            RvrRoamWalk walk = RvrRoamCircuits.Begin(circuit, catalog, "mid-1", false, 3 * Minute, 0, 1, third.Stand + new Vector3(500, 0, 0));
            Assert.That(walk.CurrentSpot, Is.EqualTo(third.Id));
            RvrRoamWalk elsewhere = RvrRoamCircuits.Begin(circuit, catalog, "mid-1", false, 3 * Minute, 0, 100, third.Stand);
            Assert.That(elsewhere.Index, Is.Zero, "in another region: from the start");
        }

        [Test]
        public void Reserve_zone_spots_are_walked_only_in_reserve_mode()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit circuit = Circuit(catalog, "alb-hib-3-south-fringe");
            Assert.That(circuit.Spots, Does.Contain("sauvage-green-knight"));
            Assert.That(RvrRoamCircuits.RouteFor(circuit, catalog, false, out _), Does.Not.Contain("sauvage-green-knight"));
            Assert.That(RvrRoamCircuits.RouteFor(circuit, catalog, true, out _), Does.Contain("sauvage-green-knight"));

            RvrFrontierPoi[] spots = catalog.Frontier(1).Spots.ToArray();
            RvrFrontierPoi pennine = catalog.Spot("pennine-north-hills");
            RvrFrontierPoi sauvage = catalog.Spot("sauvage-green-knight");
            long now = 100 * Minute;
            Assert.That(RvrRoamCircuits.ReserveMode(spots, _ => default, now), Is.True, "nobody anywhere: the reserve zones too");
            Assert.That(RvrRoamCircuits.ReserveMode(spots, s => s == pennine ? new RvrSpotPrey(2, now) : default, now), Is.False);
            Assert.That(RvrRoamCircuits.ReserveMode(spots, s => s == pennine ? new RvrSpotPrey(0, now - 5 * Minute) : default, now), Is.False,
                "prey seen there recently still counts");
            Assert.That(RvrRoamCircuits.ReserveMode(spots, s => s == pennine ? new RvrSpotPrey(0, now - 30 * Minute) : default, now), Is.True,
                "long ago does not");
            Assert.That(RvrRoamCircuits.ReserveMode(spots, s => s == sauvage ? new RvrSpotPrey(3, now) : default, now), Is.True,
                "prey in a reserve zone alone does not take the walk out of reserve mode");
        }

        [Test]
        public void Prey_at_a_spot_is_hunted_and_after_a_kill_the_group_moves_on_within_the_limit()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit circuit = Circuit(catalog, "hib-mid-2-crossroads");
            long moveOn = 3 * Minute;
            RvrRoamWalk walk = RvrRoamCircuits.Begin(circuit, catalog, "mid-2", false, moveOn, 0, 1, Vector3.Zero);
            long now = 10_000;
            RvrRoamStep step = RvrRoamCircuits.Step(walk, now, true, true);
            Assert.That(step.Action, Is.EqualTo(RvrRoamAction.Hunt));
            walk = RvrRoamCircuits.RecordKill(step.Walk, now += 20_000);
            long killAt = now;
            Assert.That(walk.KillAt, Is.EqualTo(killAt));
            walk = RvrRoamCircuits.RecordKill(walk, now + 1_000);
            Assert.That(walk.KillAt, Is.EqualTo(killAt), "the first kill starts the clock");
            Assert.That(walk.KillsHere, Is.EqualTo(2));
            // More prey keeps coming (a corpse running back): the group hunts until the move-on time, then leaves.
            while ((step = RvrRoamCircuits.Step(walk, now += 1_000, true, true)).Action == RvrRoamAction.Hunt)
                walk = step.Walk;
            Assert.That(step.Action, Is.EqualTo(RvrRoamAction.MoveOn));
            Assert.That(now - killAt, Is.InRange(moveOn - 1_000, moveOn + 1_000));
            Assert.That(step.LeftSpot, Is.EqualTo("emain-crossroads"));
            Assert.That(step.Walk.CurrentSpot, Is.EqualTo("emain-crauchon-se-camps"));
            Assert.That(step.Walk.KillAt, Is.Zero, "a fresh clock at the next spot");
            Assert.That(step.Walk.Kills, Is.EqualTo(2));

            // No more prey after the kill: it leaves at the end of the dwell, still a move-on.
            RvrRoamWalk quiet = RvrRoamCircuits.Begin(circuit, catalog, "mid-3", false, moveOn, 0, 1, Vector3.Zero);
            quiet = RvrRoamCircuits.RecordKill(RvrRoamCircuits.Step(quiet, 0, true, true).Walk, 1_000);
            RvrRoamStep end = RvrRoamCircuits.Step(quiet, quiet.ArrivedAt + quiet.DwellMilliseconds, true, false);
            Assert.That(end.Action, Is.EqualTo(RvrRoamAction.MoveOn));

            // A hunt that never lands a kill ends too.
            RvrRoamWalk endless = RvrRoamCircuits.Step(RvrRoamCircuits.Begin(circuit, catalog, "mid-4", false, moveOn, 0, 1, Vector3.Zero), 0, true, true).Walk;
            Assert.That(RvrRoamCircuits.Step(endless, endless.ArrivedAt + RvrRoamCircuits.MaximumStayMilliseconds - 1, true, true).Action, Is.EqualTo(RvrRoamAction.Hunt));
            Assert.That(RvrRoamCircuits.Step(endless, endless.ArrivedAt + RvrRoamCircuits.MaximumStayMilliseconds, true, true).Action, Is.EqualTo(RvrRoamAction.Advance));
        }

        [Test]
        public void The_move_on_time_never_passes_the_setting_and_veterans_leave_sooner()
        {
            for (double roll = 0; roll <= 1; roll += 0.05)
            {
                foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                    Assert.That(RvrRoamCircuits.MoveOnMilliseconds(3, tier, roll), Is.InRange(0, 3 * Minute), tier.ToString());
                Assert.That(RvrRoamCircuits.MoveOnMilliseconds(3, RvrSkillTier.Veteran, roll),
                    Is.LessThanOrEqualTo(RvrRoamCircuits.MoveOnMilliseconds(3, RvrSkillTier.Casual, roll)));
            }
            Assert.That(RvrRoamCircuits.MoveOnMilliseconds(3, RvrSkillTier.Casual, 1), Is.EqualTo(3 * Minute));
            Assert.That(RvrRoamCircuits.MoveOnMilliseconds(3, RvrSkillTier.Veteran, 0), Is.EqualTo((long)(0.4 * 3 * Minute)));
            Assert.That(RvrRoamCircuits.MoveOnMilliseconds(10, RvrSkillTier.Average, 0.5), Is.EqualTo(7.5 * Minute));
            Assert.That(RvrRoamCircuits.MoveOnMilliseconds(0, RvrSkillTier.Casual, 0.5), Is.Zero, "0: leave right after the kill");

            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamWalk walk = RvrRoamCircuits.Begin(Circuit(catalog, "hib-mid-2-crossroads"), catalog, "x", false, 0, 0, 1, Vector3.Zero);
            walk = RvrRoamCircuits.RecordKill(RvrRoamCircuits.Step(walk, 0, true, true).Walk, 5_000);
            Assert.That(RvrRoamCircuits.Step(walk, 5_000, true, true).Action, Is.EqualTo(RvrRoamAction.MoveOn));
        }

        [Test]
        public void A_soloer_walks_through_its_via_spots_and_then_camps_its_spot()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrFrontierPoi deep = catalog.Spot("cruachan-ailinne-hastener");
            RvrRoamWalk walk = RvrRoamCircuits.BeginSolo(deep, catalog, eRealm.Albion, "rvr-9", true, 3 * Minute, 0);
            Assert.That(walk.Route[^1], Is.EqualTo(deep.Id));
            Assert.That(walk.Route.Take(walk.LoopStart), Is.EqualTo(deep.ViaFor(eRealm.Albion)));
            Assert.That(walk.DwellMilliseconds, Is.Zero, "a via spot is walked through");
            RvrRoamStep step = RvrRoamCircuits.Step(walk, 1_000, true, true);
            Assert.That(step.Action, Is.Not.EqualTo(RvrRoamAction.Hunt), "no hunting on the way");
            for (int k = 0; k < walk.LoopStart; k++)
                walk = RvrRoamCircuits.Advance(walk);
            Assert.That(walk.CurrentSpot, Is.EqualTo(deep.Id));
            Assert.That(walk.DwellMilliseconds, Is.InRange(RvrRoamCircuits.StealthDwellMinimumMilliseconds, RvrRoamCircuits.StealthDwellMaximumMilliseconds));
            RvrRoamWalk solo = RvrRoamCircuits.BeginSolo(deep, catalog, eRealm.Midgard, "rvr-10", false, 3 * Minute, 0);
            Assert.That(solo.Route, Is.EqualTo(new[] { deep.Id }), "Midgard walks there direct");
            Assert.That(solo.DwellMilliseconds, Is.InRange(RvrRoamCircuits.SoloDwellMinimumMilliseconds, RvrRoamCircuits.SoloDwellMaximumMilliseconds));
            RvrRoamWalk there = RvrRoamCircuits.Step(solo, 5_000, true, false).Walk;
            Assert.That(there.ArrivedAt, Is.EqualTo(5_000));
            Assert.That(RvrRoamCircuits.Step(there, 5_000 + solo.DwellMilliseconds - 1, true, false).Action, Is.EqualTo(RvrRoamAction.Check));
            Assert.That(RvrRoamCircuits.Step(there, 5_000 + solo.DwellMilliseconds, true, false).Action,
                Is.EqualTo(RvrRoamAction.Done), "one spot, once: then choose again");
        }

        [Test]
        public void Circuits_are_weighted_toward_live_prey()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrRoamCircuit[] midgard = catalog.Frontier(1).Circuits.Where(c => c.Realm == eRealm.Midgard).ToArray();
            long now = 500 * Minute;
            // Six enemy levelling bots at the bwca camps in deep Pennine, nobody on the wall.
            RvrSpotPrey Prey(RvrFrontierPoi spot) => spot.Id == "pennine-sursbrooke-ne-camps" ? new RvrSpotPrey(6, now) : default;
            var weighted = midgard.Select(c => (c, RvrRoamCircuits.CircuitWeight(c, catalog, Prey, now, false))).ToArray();
            var picks = new Dictionary<string, int>();
            for (int k = 0; k < 1_000; k++)
            {
                RvrRoamCircuit picked = RvrRoamCircuits.Pick(weighted, k / 1_000.0);
                picks[picked.Id] = picks.GetValueOrDefault(picked.Id) + 1;
            }
            Assert.That(picks.GetValueOrDefault("alb-mid-3-deep-pennine"), Is.GreaterThan(850), "where the prey is");
            Assert.That(picks.GetValueOrDefault("alb-mid-1-east-wall"), Is.GreaterThan(0), "a quiet circuit still gets a little");
            Assert.That(picks.GetValueOrDefault("alb-mid-1-east-wall"), Is.LessThan(80));

            double nobody = RvrRoamCircuits.SpotWeight(default, now);
            Assert.That(nobody, Is.EqualTo(RvrRoamCircuits.BaseWeight));
            Assert.That(RvrRoamCircuits.SpotWeight(new(0, now - Minute), now), Is.GreaterThan(nobody), "seen recently");
            Assert.That(RvrRoamCircuits.SpotWeight(new(0, now - 60 * Minute), now), Is.EqualTo(nobody), "long ago");
            Assert.That(RvrRoamCircuits.SpotWeight(new(2, now), now), Is.GreaterThan(RvrRoamCircuits.SpotWeight(new(1, now), now)));
            Assert.That(RvrRoamCircuits.Pick(Array.Empty<(RvrRoamCircuit, double)>(), 0.5), Is.Null);
        }

        [Test]
        public void Stealthers_like_company_up_to_a_point()
        {
            double Weight(int mates) => RvrRoamCircuits.SoloSpotWeight(default, 0, true, mates);
            Assert.That(Weight(1), Is.GreaterThan(Weight(0)));
            Assert.That(Weight(3), Is.GreaterThan(Weight(2)));
            Assert.That(Weight(5), Is.EqualTo(Weight(3)), "three are enough");
            Assert.That(Weight(RvrRoamCircuits.PackFull), Is.LessThan(Weight(0)), "a full camp: go elsewhere");
            Assert.That(RvrRoamCircuits.SoloSpotWeight(new(3, 0), 0, false, 9), Is.EqualTo(RvrRoamCircuits.SpotWeight(new(3, 0), 0)),
                "a visible soloer goes by the prey");
        }

        [Test]
        public void The_live_count_tallies_enemy_levellers_inside_each_spot_and_each_realm_sees_the_others()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrFrontierPoi bwca = catalog.Spot("pennine-sursbrooke-ne-camps");
            var prey = new List<(ushort, float, float, eRealm)>
            {
                (1, bwca.Position.X, bwca.Position.Y, eRealm.Albion),
                (1, bwca.Position.X + 100, bwca.Position.Y, eRealm.Albion),
                (1, bwca.Position.X, bwca.Position.Y + bwca.Radius + 50, eRealm.Albion), // outside the radius
                (100, bwca.Position.X, bwca.Position.Y, eRealm.Albion),                  // another region
                (1, bwca.Position.X, bwca.Position.Y, eRealm.Midgard),
            };
            Dictionary<string, int[]> counts = RvrRoamCircuits.Tally(catalog, prey);
            Assert.That(counts[bwca.Id][(int)eRealm.Albion], Is.EqualTo(2));
            Assert.That(counts[bwca.Id][(int)eRealm.Midgard], Is.EqualTo(1));
            try
            {
                RvrRoamPrey.Set(1_000, counts, new() { [bwca.Id] = [0, 900, 950, 0] });
                Assert.That(RvrRoamPrey.For(bwca, eRealm.Midgard), Is.EqualTo(new RvrSpotPrey(2, 900)), "Midgard hunts the Albion levellers");
                Assert.That(RvrRoamPrey.For(bwca, eRealm.Hibernia), Is.EqualTo(new RvrSpotPrey(3, 950)));
                Assert.That(RvrRoamPrey.For(bwca, eRealm.Albion), Is.EqualTo(new RvrSpotPrey(1, 950)));
                Assert.That(RvrRoamPrey.For(catalog.Spot("emain-amg"), eRealm.Albion), Is.EqualTo(default(RvrSpotPrey)));
                RvrRoamPrey.Refresh(catalog, 1_000 + 60_000);
                Assert.That(RvrRoamPrey.SampledAt, Is.EqualTo(1_000), "never sampled again before three minutes");
            }
            finally { RvrRoamPrey.Set(long.MinValue, null, null); }
        }

        [Test]
        public void The_log_budget_caps_lines_per_minute()
        {
            string tag = "TEST_" + Guid.NewGuid();
            Assert.That(Enumerable.Range(0, 10).Count(_ => RvrLogBudget.Take(tag, 60_000, 3)), Is.EqualTo(3));
            Assert.That(RvrLogBudget.Take(tag, 120_000, 3), Is.True, "a new minute");
        }
    }
}
