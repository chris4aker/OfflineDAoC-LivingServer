using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.RealmAbilities;
using NUnit.Framework;
using static DOL.GS.AutonomousBotRealmAbilities;

namespace DOL.UnitTests
{
    /// <summary>
    /// #531 step 2b: 1.65 removed Avoidance of Magic, Brilliant Aura of Deflection and Volcanic Pillar. Gamebots never buy
    /// them, and the points go to the next steps of their build.
    /// </summary>
    [TestFixture]
    public class UT_BotsDropAvoidanceOfMagic531
    {
        private static readonly string[] Shared =
            { "AugStr", "AugCon", "AugDex", "AugQui", "AugAcuity", "Toughness", "Determination", "Serenity", "Dodger", "DualistsReflexes" };

        private static Dictionary<string, int> Catalog(IEnumerable<string> keys) =>
            keys.Select(k => k.StartsWith("AtlasOF_") ? k : "AtlasOF_" + k).Distinct().ToDictionary(k => k, k => k switch
            {
                Purge or PurgeReduced or SecondWind or IgnorePain or IgnorePainTank => 1,
                FirstAid => 3,
                _ => 5,
            });

        private static int Cost(string key, int level) => key switch
        {
            Purge or SecondWind => 10,
            PurgeReduced => 4,
            IgnorePain => 14,
            IgnorePainTank => 8,
            FirstAid => AtlasRAHelpers.GetCommonUpgradeCostFor3LevelsRA(level),
            _ => AtlasRAHelpers.GetCommonUpgradeCostFor5LevelsRA(level),
        };

        private static IEnumerable<TestCaseData> Classes()
        {
            yield return new TestCaseData(Shared.Concat(new[] { "MasteryOfPain", "MasteryOfParrying", "MasteryOfBlocking", "MasteryOfArms",
                Purge, SecondWind, FirstAid, IgnorePainTank }).ToArray(), (int)eCharacterClass.Armsman, Archetype.Melee).SetName("Armsman-like");
            yield return new TestCaseData(Shared.Concat(new[] { "MasteryOfMagery", "WildPower", "EtherealBond", "MasteryOfTheArcane",
                PurgeReduced, FirstAid }).ToArray(), (int)eCharacterClass.Wizard, Archetype.Caster).SetName("Wizard-like");
            yield return new TestCaseData(Shared.Concat(new[] { "MasteryOfHealing", "WildHealing", "EtherealBond", PurgeReduced, FirstAid })
                .ToArray(), (int)eCharacterClass.Cleric, Archetype.Healer).SetName("Cleric-like");
            yield return new TestCaseData(Shared.Concat(new[] { "MasteryOfArchery", "FalconsEye", "MasteryOfPain", Purge, SecondWind })
                .ToArray(), (int)eCharacterClass.Scout, Archetype.Archer).SetName("Scout-like");
        }

        [TestCaseSource(nameof(Classes))]
        public void NoBotBuildBuysAnAbility165RemovedAndThePointsStillGetSpent(string[] keys, int classId, Archetype archetype)
        {
            // The class tables offer the removed abilities; a bot plans as if they were not there.
            var offered = Catalog(keys.Concat(new[] { "AvoidanceOfMagic", "BrilliantAura", "VolcanicPillar" }));
            var without = Catalog(keys);
            for (int points = 0; points <= 200; points++)
            {
                var plan = Plan(offered, archetype, classId, points, Cost);
                foreach (string removed in RemovedAt165)
                    Assert.That(plan.ContainsKey(removed), Is.False, $"{removed} at points={points}");
                Assert.That(plan, Is.EquivalentTo(Plan(without, archetype, classId, points, Cost)),
                    $"the points go to the same steps as for a class without them (points={points})");
            }
            int spent(Dictionary<string, int> p) => p.Sum(pair => Enumerable.Range(0, pair.Value).Sum(level => Cost(pair.Key, level)));
            var full = Plan(offered, archetype, classId, 200, Cost);
            Assert.That(spent(full), Is.GreaterThan(100), "a high rank still spends its points on the build");
        }

        [Test]
        public void TheRemovedListIsThe165One() =>
            Assert.That(RemovedAt165, Is.EquivalentTo(new[] { "AtlasOF_AvoidanceOfMagic", "AtlasOF_BrilliantAura", "AtlasOF_VolcanicPillar" }));
    }
}
