using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 5 (docs/RVR-FIELD-1.2.md, "Feet"): the embedded lane catalog
    /// (bots/autonomous/data/rvr-lanes.json) parses, a missing or broken catalog gives an empty one with the problem named,
    /// and the validation a lane gets at server start turns off a lane that no longer walks, walks another corridor or no
    /// longer keeps apart from a lower lane. Pure: the walker is a stand-in for the planner. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_RvrLaneCatalog
    {
        internal static bool Straight(Vector3 from, Vector3 to, List<Vector3> walk, List<Vector3> shape, out string failure)
        {
            failure = null;
            walk.Add(to);
            shape.Add(to);
            return true;
        }

        [Test]
        public void Embedded_catalog_parses_with_every_trip()
        {
            RvrLaneData data = RvrLaneCatalog.Data;
            Assert.Multiple(() =>
            {
                Assert.That(data.Problem, Is.Null);
                Assert.That(data.Version, Is.EqualTo(1));
                Assert.That(data.Tool, Is.EqualTo("UT_RvrLaneSurvey/2"));
                Assert.That(data.Lanes.Length, Is.EqualTo(332));
                Assert.That(data.TripCount, Is.EqualTo(149));
                Assert.That(data.Objectives.Length, Is.EqualTo(45));
                Assert.That(data.Lanes.Count(lane => lane.Kind == RvrLaneKind.Trunk), Is.EqualTo(6));
                Assert.That(data.Lanes.Count(lane => lane.Index > 0), Is.EqualTo(183));
                Assert.That(data.Criteria.MinSeparation, Is.EqualTo(2_500));
                Assert.That(data.Criteria.KeepClearance, Is.EqualTo(4_000));
            });
            foreach (var trip in data.Trips)
            {
                // Lane 0 first, indices in order, and every lane of a trip ends at the same goal: the fan re-merges.
                Assert.That(trip.Select(lane => lane.Index), Is.EqualTo(Enumerable.Range(0, trip.Length)), trip[0].From);
                Assert.That(trip.Select(lane => lane.Goal).Distinct().Count(), Is.EqualTo(1), trip[0].From + " > " + trip[0].To);
                Assert.That(trip.Select(lane => lane.Start).Distinct().Count(), Is.EqualTo(1));
                Assert.That(trip[0].Via, Is.Empty, "lane 0 is the planner's own route");
                Assert.That(data.Objective(trip[0].From), Is.Not.Null);
                Assert.That(data.Objective(trip[0].To), Is.Not.Null);
            }
            // Each portal keep has its trunk, ending at its own milegate.
            foreach (RvrLaneObjective portal in data.Objectives.Where(o => o.Kind == RvrLaneObjectiveKind.PortalKeep))
            {
                RvrLane trunk = data.TrunkFrom(portal.Region, portal.Ref);
                Assert.That(trunk, Is.Not.Null, portal.Ref);
                Assert.That(data.Objective(trunk.To).PortalKeepId, Is.EqualTo(portal.KeepId));
            }
        }

        [Test]
        public void Missing_or_broken_catalog_is_empty_and_named()
        {
            RvrLaneData missing = RvrLaneCatalog.Load(typeof(RvrLaneCatalog).Assembly, "no-such-lanes.json");
            RvrLaneData broken = RvrLaneCatalog.Parse("{ not json");
            RvrLaneData noLanes = RvrLaneCatalog.Parse("{\"version\":1}");
            RvrLaneData oneBad = RvrLaneCatalog.Parse(
                "{\"lanes\":[{\"region\":1,\"kind\":\"gate\",\"from\":{\"ref\":\"a\"},\"to\":{\"ref\":\"b\"},\"start\":[0,0,0],\"goal\":[1,1,1],\"lane\":0}," +
                "{\"region\":1,\"kind\":\"gate\",\"from\":{\"ref\":\"a\"},\"to\":{\"ref\":\"b\"},\"start\":[0,0],\"goal\":[1,1,1],\"lane\":1}]}");
            Assert.Multiple(() =>
            {
                Assert.That(missing.Lanes, Is.Empty);
                Assert.That(missing.Problem, Does.Contain("missing"));
                Assert.That(broken.Lanes, Is.Empty);
                Assert.That(broken.Problem, Does.Contain("not JSON"));
                Assert.That(noLanes.Problem, Does.Contain("no lanes"));
                Assert.That(oneBad.Lanes.Length, Is.EqualTo(1), "the malformed lane is skipped, the good one kept");
                Assert.That(oneBad.Problem, Does.Contain("1 malformed"));
            });
            // No catalog: the march takes today's route.
            RvrLaneOptions options = RvrLaneChoice.Options(missing, (_, _) => RvrLaneProof.Unchecked, 1, eRealm.Hibernia,
                Vector3.Zero, "keep:50", Vector2.Zero, 50, [], []);
            Assert.That(options.UsesLanes, Is.False);
            Assert.That(options.Fallback, Is.EqualTo(RvrLaneFallback.NoCatalog));
        }

        [Test]
        public void Validation_turns_off_a_lane_that_fails_its_walk()
        {
            RvrLane lane = RvrLaneCatalog.Data.Trip(1, "milegate:Hadrian's Wall west milegate", "keep:50")[1];
            float straight = RvrLaneGeometry.Length(lane.Points);
            RvrLane proved = lane with { Length = straight, BackLength = straight };

            RvrLaneProof good = RvrLaneCatalog.Prove(proved, false, Straight);
            RvrLaneProof failsLeg3 = RvrLaneCatalog.Prove(proved, false, FailAt(3));
            RvrLaneProof throws = RvrLaneCatalog.Prove(proved, false, (Vector3 _, Vector3 _, List<Vector3> _, List<Vector3> _, out string f) =>
                throw new System.InvalidOperationException());
            RvrLaneProof otherCorridor = RvrLaneCatalog.Prove(proved with { Length = straight * 0.6f }, false, Straight);
            RvrLaneProof back = RvrLaneCatalog.Prove(proved, true, Straight);
            Assert.Multiple(() =>
            {
                Assert.That(good.Status, Is.EqualTo(RvrLaneStatus.Validated));
                Assert.That(good.Usable, Is.True);
                Assert.That(good.Walk.Length, Is.EqualTo(lane.Points.Length));
                Assert.That(good.Length, Is.EqualTo(straight).Within(1));
                Assert.That(failsLeg3.Status, Is.EqualTo(RvrLaneStatus.Rejected));
                Assert.That(failsLeg3.Usable, Is.False);
                Assert.That(failsLeg3.Reason, Does.StartWith("leg 3/").And.Contain("blocked"));
                Assert.That(throws.Status, Is.EqualTo(RvrLaneStatus.Rejected));
                Assert.That(otherCorridor.Status, Is.EqualTo(RvrLaneStatus.Rejected));
                Assert.That(otherCorridor.Reason, Does.Contain("another corridor"));
                Assert.That(back.Walk[0], Is.EqualTo(lane.Goal), "a back walk starts at the goal");
                Assert.That(RvrLaneProof.Unchecked.Usable, Is.False, "an unchecked lane is never used");
            });
        }

        private static RvrLegWalker FailAt(int leg)
        {
            int count = 0;
            return (Vector3 from, Vector3 to, List<Vector3> walk, List<Vector3> shape, out string failure) =>
            {
                if (++count == leg) { failure = "blocked"; return false; }
                return Straight(from, to, walk, shape, out failure);
            };
        }

        [Test]
        public void Validation_turns_off_an_alternate_that_no_longer_keeps_apart()
        {
            RvrLaneProof Line(float x) => new(RvrLaneStatus.Validated, [new(x, 0, 0), new(x, 20_000, 0)],
                [new(x, 0, 0), new(x, 10_000, 0), new(x, 20_000, 0)], 20_000, null);
            float minimum = RvrLaneCriteria.Default.MinSeparation * RvrLaneCatalog.SeparationTolerance;
            Assert.Multiple(() =>
            {
                Assert.That(RvrLaneCatalog.SeparationFailure(Line(3_000), Line(0), minimum), Is.Null);
                Assert.That(RvrLaneCatalog.SeparationFailure(Line(1_000), Line(0), minimum), Does.Contain("only 1000"));
                Assert.That(RvrLaneCatalog.SeparationFailure(RvrLaneProof.Rejected("x"), Line(0), minimum), Is.Null,
                    "a rejected lane needs no separation check");
            });
        }

        [Test]
        public void Natural_realm_and_loaded_summary()
        {
            RvrLaneData data = RvrLaneCatalog.Data;
            RvrLane trunk = data.TrunkFrom(1, "keep:22");
            RvrLane gate = data.Trip(1, "milegate:Hadrian's Wall east milegate", "keep:50")[0];
            RvrLane entry = data.Lanes.First(lane => lane.Kind == RvrLaneKind.Entry && lane.Region == 100);
            Assert.Multiple(() =>
            {
                Assert.That(RvrLaneCatalog.NaturalRealm(data, trunk), Is.EqualTo(eRealm.Hibernia));
                Assert.That(RvrLaneCatalog.NaturalRealm(data, gate), Is.EqualTo(eRealm.Midgard));
                Assert.That(RvrLaneCatalog.NaturalRealm(data, gate, _ => eRealm.Albion), Is.EqualTo(eRealm.Albion), "live owner first");
                Assert.That(RvrLaneCatalog.NaturalRealm(data, entry), Is.EqualTo(eRealm.Midgard));
            });
            var forward = new RvrLaneProof[data.Lanes.Length];
            for (int i = 0; i < forward.Length; i++)
                forward[i] = i % 10 == 0 ? RvrLaneProof.Rejected("x") : new(RvrLaneStatus.Validated, [Vector3.Zero], [Vector3.Zero, Vector3.One], 1, null);
            string line = RvrLaneCatalog.Summary(data, forward, new RvrLaneProof[data.Lanes.Length], 42);
            Assert.That(line, Does.StartWith("RVR_LANE_LOADED lanes=332 trips=149 validated=298 rejected=34 "));
            Assert.That(line, Does.Contain("seconds=42").And.Contain("byRegion=\"1:trunk").And.Contain(";100:").And.Contain(";200:"));
        }
    }
}
