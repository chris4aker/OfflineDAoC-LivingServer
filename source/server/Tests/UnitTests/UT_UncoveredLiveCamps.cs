using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;
using Spawn = DOL.GS.AutonomousUncoveredLiveCamps.Spawn;

namespace DOL.UnitTests
{
    /// <summary>GitHub #544: live spawn clusters no camp covers become camps, the same way in every realm.</summary>
    [TestFixture]
    public class UT_UncoveredLiveCamps
    {
        private const int Cover = 5_000;

        private static readonly Dictionary<(ushort Region, string Name), (int X, int Y)[]> NoCamps = new();

        private static Spawn S(ushort zone, ushort region, int x, int y, string name, int level, string id = null) =>
            new(zone, region, x, y, name, level, id ?? $"{name}-{x}-{y}");

        [Test]
        public void AClusterOfThreeOfOneNameAndLevelBecomesACampAtTheSpawnNearestItsCentre()
        {
            var spawns = new[]
            {
                S(8, 1, 99_100, 200_100, "Forest Bear", 14),
                S(8, 1, 99_500, 200_400, "forest bear", 14),
                S(8, 1, 99_900, 200_800, "forest bear", 14),
            };
            var clusters = AutonomousUncoveredLiveCamps.Find(spawns, NoCamps, Cover);
            Assert.That(clusters, Has.Count.EqualTo(1));
            Assert.That(clusters[0].Members, Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(clusters[0].Representative, Is.EqualTo(1), "the middle spawn, not the arithmetic average");
            Assert.That(clusters[0].MixedLevels, Is.False);
            Assert.That(clusters[0].CampId, Is.EqualTo("live-uncovered:8:1:66:133:forest bear:14"));
        }

        [Test]
        public void TwoSpawnsAreNotACamp()
        {
            var spawns = new[] { S(8, 1, 100_100, 200_100, "forest bear", 14), S(8, 1, 100_500, 200_400, "forest bear", 14) };
            Assert.That(AutonomousUncoveredLiveCamps.Find(spawns, NoCamps, Cover), Is.Empty);
        }

        [Test]
        public void ACampOfTheSameNameWithinTheCoverRadiusCoversTheSpawns()
        {
            var spawns = new[]
            {
                S(8, 1, 100_100, 200_100, "forest bear", 14), S(8, 1, 100_500, 200_400, "forest bear", 14),
                S(8, 1, 100_900, 200_800, "forest bear", 14),
                // far from the sighting: still uncovered
                S(8, 1, 130_100, 200_100, "forest bear", 15), S(8, 1, 130_300, 200_200, "forest bear", 15),
                S(8, 1, 130_400, 200_300, "forest bear", 15),
                // another name near the sighting: a bear camp does not cover wolves
                S(8, 1, 100_200, 200_200, "grey wolf", 13), S(8, 1, 100_300, 200_300, "grey wolf", 13),
                S(8, 1, 100_400, 200_400, "grey wolf", 13),
            };
            var covered = new Dictionary<(ushort Region, string Name), (int X, int Y)[]>
            {
                [(1, "Forest Bear")] = [(103_000, 202_000)],
                // the same name in another region covers nothing here
                [(2, "grey wolf")] = [(100_300, 200_300)],
            };
            var clusters = AutonomousUncoveredLiveCamps.Find(spawns, covered, Cover);
            Assert.That(clusters.Select(cluster => cluster.CampId), Is.EquivalentTo(new[]
            {
                "live-uncovered:8:1:86:133:forest bear:15",
                "live-uncovered:8:1:66:133:grey wolf:13",
            }));
        }

        [Test]
        public void SpawnsOfMixedLevelsInOneCampCellBecomeACamp()
        {
            // True 1.65's case: level 4-5 spraggons spread over 1,500-unit cells; one 4,200-unit cell holds four of them
            var spawns = new[]
            {
                S(201, 200, 340_300, 551_000, "spraggon", 4), S(201, 200, 341_900, 552_100, "spraggon", 5),
                S(201, 200, 343_500, 553_900, "spraggon", 4), S(201, 200, 344_300, 554_000, "spraggon", 5),
                S(201, 200, 340_400, 551_200, "eirebug", 4), S(201, 200, 341_000, 551_500, "eirebug", 4),
                S(201, 200, 360_000, 551_000, "spraggon", 4),
            };
            var clusters = AutonomousUncoveredLiveCamps.Find(spawns, NoCamps, Cover);
            Assert.That(clusters, Has.Count.EqualTo(1));
            Assert.That(clusters[0].MixedLevels, Is.True);
            Assert.That(clusters[0].Members, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
            Assert.That(clusters[0].CampId, Does.EndWith(":spraggon:mixed"));
        }

        [Test]
        public void TheFirstPassCampsCoverTheirNeighboursForTheMixedPass()
        {
            var spawns = new List<Spawn>
            {
                S(8, 1, 100_100, 200_100, "forest bear", 14), S(8, 1, 100_200, 200_200, "forest bear", 14),
                S(8, 1, 100_300, 200_300, "forest bear", 14),
                // three more bears of other levels in the same 4,200 cell, within 5,000 of the first camp
                S(8, 1, 101_600, 200_100, "forest bear", 15), S(8, 1, 101_700, 201_600, "forest bear", 16),
                S(8, 1, 102_000, 201_700, "forest bear", 13),
            };
            var clusters = AutonomousUncoveredLiveCamps.Find(spawns, NoCamps, Cover);
            Assert.That(clusters, Has.Count.EqualTo(1), "no second, overlapping mixed camp");
            Assert.That(clusters[0].MixedLevels, Is.False);
        }

        [Test]
        public void TheSameLayoutGivesTheSameCampsInEveryRealm()
        {
            // one layout, placed in an Albion zone (Campacorentin), a Midgard zone (Myrkwood) and a Hibernian zone
            // (Silvermine): the rule has no realm in it
            (ushort Zone, ushort Region)[] places = [(8, 1), (105, 100), (201, 200)];
            var counts = new List<int>();
            var levels = new List<string>();
            foreach (var (zone, region) in places)
            {
                var spawns = new List<Spawn>();
                for (int camp = 0; camp < 6; camp++)
                    for (int i = 0; i < 3 + camp % 3; i++)
                        spawns.Add(S(zone, region, 50_000 + camp * 12_000 + i * 200, 60_000 + i * 200, $"beast {camp % 2}", 10 + camp));
                var clusters = AutonomousUncoveredLiveCamps.Find(spawns, NoCamps, Cover);
                counts.Add(clusters.Count);
                levels.Add(string.Join(",", clusters.Select(cluster => spawns[cluster.Representative].Level)));
            }
            Assert.That(counts, Is.All.EqualTo(6));
            Assert.That(levels.Distinct().Count(), Is.EqualTo(1));
        }

        [Test]
        public void EmptyInputGivesNoCamps()
        {
            Assert.That(AutonomousUncoveredLiveCamps.Find([], NoCamps, Cover), Is.Empty);
            Assert.That(AutonomousUncoveredLiveCamps.Find(null, null, Cover), Is.Empty);
        }

        [Test]
        public void UncoveredCampsGetTheLiveClusterRouteProof()
        {
            MethodInfo method = typeof(AutonomousWorldBotController).GetMethod("IsSourceEmptyCamp",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, ["live-uncovered:8:1:66:133:forest bear:14"]), Is.True);
            Assert.That(method.Invoke(null, ["capnbry-source-empty:181:181:1:2:sneezer:20"]), Is.True);
            Assert.That(method.Invoke(null, ["capnbry:0:5:14:adder"]), Is.False);
        }

        [Test]
        public void TheCatalogPassReportsHowManyCampsItAdded()
        {
            MethodInfo pass = typeof(AutonomousWorldBotController).GetMethod("AddUncoveredLiveCamps",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(pass, Is.Not.Null);
            Assert.That(pass.ReturnType, Is.EqualTo(typeof(int)), "the count goes to AUTONOMOUS_CAMP_CATALOG_DEDUPE");
        }

        [Test]
        public void LiveUncoveredCampsMergeLikeOtherLiveCamps()
        {
            Assert.That(AutonomousCampDedupe.SourcePriority("live-uncovered:8:1:66:133:forest bear:14"), Is.EqualTo(1));
            Assert.That(AutonomousCampDedupe.IsMergeableSource("live-uncovered:8:1:66:133:forest bear:14"), Is.True);
        }
    }
}
