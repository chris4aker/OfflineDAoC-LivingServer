using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

// 1.2.3 (#569 step 3): ported from zomboden/OfflineDAoC-True165 14dcaa5 (GPL-3.0, shadowofze).
namespace DOL.UnitTests
{
    /// <summary>A PvE party picks camps it can reach within its task time; never an empty list (owner 2026-11-10).</summary>
    [TestFixture]
    public class UT_AutonomousGroupTravelBudget
    {
        private sealed record Cand(string Id, double Minutes, bool Dungeon = false);

        private static double Eff(Cand c) => AutonomousGroupTravelBudget.EffectiveEstimate(c.Minutes, c.Dungeon);

        private static Cand[] Run(Cand[] input, long remainingMinutes, out AutonomousGroupTravelBudget.Tier tier) =>
            AutonomousGroupTravelBudget.Filter(input, Eff, remainingMinutes * 60_000L, out tier);

        [Test]
        public void BudgetIsAThirdOfTheTimeWithASixMinuteFloor()
        {
            Assert.That(AutonomousGroupTravelBudget.BudgetMinutes(120 * 60_000L), Is.EqualTo(42).Within(1e-9));
            Assert.That(AutonomousGroupTravelBudget.BudgetMinutes(10 * 60_000L), Is.EqualTo(6));
            Assert.That(AutonomousGroupTravelBudget.BudgetMinutes(-5), Is.EqualTo(6));
        }

        [Test]
        public void AllFitReturnsTheInput()
        {
            Cand[] input = [new("a", 1), new("b", 2), new("c", 3)];
            Assert.That(Run(input, 120, out var tier), Is.SameAs(input));
            Assert.That(tier, Is.EqualTo(AutonomousGroupTravelBudget.Tier.AllFit));
        }

        [Test]
        public void MixedKeepsOnlyTheFittingOnes()
        {
            // 120 min left: budget 42, so estimates up to 16.8 fit (x2.5)
            Cand[] result = Run([new("a", 5), new("b", 20), new("c", 10)], 120, out var tier);
            Assert.That(result.Select(c => c.Id), Is.EqualTo(new[] { "a", "c" }));
            Assert.That(tier, Is.EqualTo(AutonomousGroupTravelBudget.Tier.Fit));
        }

        [Test]
        public void NothingInBudgetAllowsTwiceTheBudget()
        {
            Cand[] result = Run([new("a", 20), new("b", 40)], 120, out var tier);
            Assert.That(result.Select(c => c.Id), Is.EqualTo(new[] { "a" }));
            Assert.That(tier, Is.EqualTo(AutonomousGroupTravelBudget.Tier.Wide));
        }

        [Test]
        public void NothingWithinTwiceTheBudgetKeepsTheThreeNearest()
        {
            Cand[] result = Run([new("a", 50), new("b", 40), new("c", 60), new("d", 45), new("e", 70)], 120, out var tier);
            Assert.That(result.Select(c => c.Id), Is.EquivalentTo(new[] { "a", "b", "d" }));
            Assert.That(tier, Is.EqualTo(AutonomousGroupTravelBudget.Tier.Nearest));
        }

        [Test]
        public void ASingleCandidateIsAlwaysReturned()
        {
            Cand[] input = [new("far", 500)];
            Assert.That(Run(input, 10, out _), Is.EqualTo(input));
        }

        [Test]
        public void EmptyStaysEmpty()
        {
            Assert.That(Run(Array.Empty<Cand>(), 120, out _), Is.Empty);
            Assert.That(AutonomousGroupTravelBudget.Filter<Cand>(null, Eff, 0, out _), Is.Empty);
        }

        [Test]
        public void NeverEmptyForANonEmptyInput()
        {
            var random = new Random(7);
            for (int round = 0; round < 200; round++)
            {
                Cand[] input = Enumerable.Range(0, random.Next(1, 12))
                    .Select(i => new Cand($"c{i}", random.NextDouble() * 200, random.Next(2) == 0)).ToArray();
                Assert.That(Run(input, random.Next(0, 130), out _), Is.Not.Empty);
            }
        }

        [Test]
        public void DungeonSurchargeDropsADungeonCampAnOutdoorCampKeeps()
        {
            Cand[] result = Run([new("outdoor", 10), new("dungeon", 10, true)], 120, out _);
            Assert.That(result.Select(c => c.Id), Is.EqualTo(new[] { "outdoor" }));
        }

        [Test]
        public void TwoTargetsInDifferentRegionsGetDifferentCrossings()
        {
            // critic 2026-11-10: the old (realm, from) key gave the second target region the first one's crossing
            var cache = new Dictionary<(eRealm Realm, ushort From, ushort To), string>();
            int resolves = 0;
            string Resolve(ushort to) { resolves++; return $"edge-to-{to}"; }
            string toCamp = AutonomousTravelCrossingCache.GetOrResolve(cache, eRealm.Albion, 1, 2, () => Resolve(2));
            string toTown = AutonomousTravelCrossingCache.GetOrResolve(cache, eRealm.Albion, 1, 3, () => Resolve(3));
            Assert.That(toCamp, Is.EqualTo("edge-to-2"));
            Assert.That(toTown, Is.EqualTo("edge-to-3"));
            Assert.That(AutonomousTravelCrossingCache.GetOrResolve(cache, eRealm.Albion, 1, 2, () => Resolve(99)),
                Is.EqualTo("edge-to-2"), "Same pair: cached, not resolved again.");
            Assert.That(resolves, Is.EqualTo(2));
            Assert.That(AutonomousTravelCrossingCache.GetOrResolve(cache, eRealm.Midgard, 1, 2, () => Resolve(2)),
                Is.EqualTo("edge-to-2"));
            Assert.That(resolves, Is.EqualTo(3), "Another realm resolves its own crossing.");
        }

        [Test]
        public void TheDungeonSurchargeOnlyWeighsDungeonAgainstOutdoor()
        {
            // owner 2026-11-10 (round-3 review): mixed list, leader outside the dungeon region: surcharge
            Assert.That(AutonomousGroupTravelBudget.AppliesDungeonSurcharge(true, true, false), Is.True);
            // leader already inside that dungeon region: none
            Assert.That(AutonomousGroupTravelBudget.AppliesDungeonSurcharge(true, true, true), Is.False);
            // all-dungeon list (SelectCamp's environment roll): none, so no tier drop for every dungeon party
            Assert.That(AutonomousGroupTravelBudget.AppliesDungeonSurcharge(true, false, false), Is.False);
            Assert.That(AutonomousGroupTravelBudget.AppliesDungeonSurcharge(false, true, false), Is.False);
            // an all-dungeon list at 45 min left now keeps every camp that fits instead of the nearest three
            Cand[] dungeons = [new("d1", 3, true), new("d2", 4, true), new("d3", 5, true), new("d4", 6, true)];
            Cand[] kept = AutonomousGroupTravelBudget.Filter(dungeons,
                c => AutonomousGroupTravelBudget.EffectiveEstimate(c.Minutes,
                    AutonomousGroupTravelBudget.AppliesDungeonSurcharge(c.Dungeon, false, false)), 45 * 60_000L, out var tier);
            Assert.That(kept, Is.SameAs(dungeons));
            Assert.That(tier, Is.EqualTo(AutonomousGroupTravelBudget.Tier.AllFit));
        }

        [Test]
        public void MeetupPlanningUsesTheNormalRunSpeed()
        {
            // owner 2026-11-10 (round-3 review): mezzed/stunned (0) or snared members plan at base speed; buffs still count
            Assert.That(AutonomousBotGroupCoordinator.MeetupPlanningSpeed(191, 0), Is.EqualTo(191));
            Assert.That(AutonomousBotGroupCoordinator.MeetupPlanningSpeed(191, 80), Is.EqualTo(191));
            Assert.That(AutonomousBotGroupCoordinator.MeetupPlanningSpeed(191, 250), Is.EqualTo(250));
        }

        [Test]
        public void ANullCrossingIsCachedToo()
        {
            var cache = new Dictionary<(eRealm Realm, ushort From, ushort To), string>();
            int resolves = 0;
            AutonomousTravelCrossingCache.GetOrResolve(cache, eRealm.Hibernia, 200, 181, () => { resolves++; return null; });
            AutonomousTravelCrossingCache.GetOrResolve(cache, eRealm.Hibernia, 200, 181, () => { resolves++; return null; });
            Assert.That(resolves, Is.EqualTo(1));
        }
    }
}
