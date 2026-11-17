using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Era builds 1.2, WP1 (docs/ERA-BUILDS-1.2.md): the shared catalog's draws, jitter, nearest build, budget and fallbacks.
    /// Pure: no game world.
    /// </summary>
    [TestFixture]
    public class UT_EraBuildCatalog
    {
        private static EraBuildCatalog Catalog => EraBuildCatalog.Default;
        private const int Seeds = 100_000;

        [TestCase(10, 1494)]
        [TestCase(15, 2223)]
        [TestCase(20, 2979)]
        [TestCase(22, 3253)]
        [TestCase(25, 3706)]
        public void TheLevel50BudgetIsTheTrainersFormulaWithTheHalfLevels(int multiplier, int points) =>
            Assert.That(EraBuildCatalog.PointBudget(multiplier), Is.EqualTo(points));

        [Test]
        public void ALineCostsEveryLevelAboveOne()
        {
            Assert.That(EraBuildCatalog.LineCost(1), Is.Zero);
            Assert.That(EraBuildCatalog.LineCost(2), Is.EqualTo(2));
            Assert.That(EraBuildCatalog.LineCost(50), Is.EqualTo(1274));
        }

        [Test]
        public void AnAutotrainLineIsFreeUpToAQuarterOfTheLevel()
        {
            // as a player's (GamePlayer.GetAutoTrainPoints; UT_BotSpecPointsMatchAPlayer holds every level to the player)
            Assert.That(EraBuildCatalog.AutoTrainLevel(), Is.EqualTo(12));
            Assert.That(EraBuildCatalog.LineCost(12, true), Is.Zero);
            Assert.That(EraBuildCatalog.LineCost(8, true), Is.Zero);
            Assert.That(EraBuildCatalog.LineCost(50, true), Is.EqualTo(1274 - 77));
            Assert.That(EraBuildCatalog.LineCost(50, false), Is.EqualTo(1274));
            Assert.That(EraBuildCatalog.LineCost(20, true, 40), Is.EqualTo(EraBuildCatalog.LineCost(20) - EraBuildCatalog.LineCost(10)));
            Assert.That(EraBuildCatalog.LineCost(3, true, 3), Is.EqualTo(5), "below level 8 nothing is free");
            var armsman = new[] { new KeyValuePair<string, int>("Slash", 50), new KeyValuePair<string, int>("Thrust", 5),
                new KeyValuePair<string, int>("Shields", 42) };
            Assert.That(EraBuildCatalog.BuildCost(armsman) - EraBuildCatalog.BuildCost(armsman, ["Slash", "Thrust"]),
                Is.EqualTo(77 + EraBuildCatalog.LineCost(5)));
            EraClass shadowblade = Catalog.GetClass("Shadowblade");
            Assert.That(shadowblade.AutoTrainLines, Is.EqualTo(new[] { "Stealth" }));
            Assert.That(Catalog.GetClass("Wizard").AutoTrainLines, Is.Empty);
            Assert.That(Catalog.GetClass("Hunter").AutoTrainLines, Is.Empty, "the game's Hunter has no autotrain list");
            EraBuild build = Catalog.GetBuild("shadowblade-axe-la");
            Assert.That(build.Points, Is.EqualTo(EraBuildCatalog.BuildCost(build.Specs) - 77));
            Assert.That(EraBuildCatalog.BuildCost(build.Specs), Is.GreaterThan(shadowblade.PointBudget50), "over, counting every level");
            Assert.That(build.Points, Is.LessThanOrEqualTo(shadowblade.PointBudget50), "a player can pay for it");
        }

        [TestCase("Albion")]
        [TestCase("Midgard")]
        [TestCase("Hibernia")]
        public void SeededClassDrawsMatchTheSharesWithinHalfAPoint(string realm)
        {
            var counts = new Dictionary<string, int>();
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                string cls = Catalog.DrawClass(realm, seed).ClassName;
                counts[cls] = counts.GetValueOrDefault(cls) + 1;
            }
            foreach (EraClassShare share in Catalog.Shares(realm))
                Assert.That(counts.GetValueOrDefault(share.ClassName) * 100.0 / Seeds, Is.EqualTo(share.Share * 100).Within(0.5),
                    share.ClassName);
            Assert.That(counts.Keys, Does.Not.Contain(BotCustomClassRule.CustomClassName));
        }

        [Test]
        public void RandomClassDrawsMatchTheSharesToo()
        {
            var random = new Random(1665);
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < Seeds; i++)
            {
                int id = Catalog.DrawClass(3, random).ClassId;
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            foreach (EraClassShare share in Catalog.Shares(3))
                Assert.That(counts.GetValueOrDefault(share.ClassId) * 100.0 / Seeds, Is.EqualTo(share.Share * 100).Within(0.5));
            Assert.That(counts.ContainsKey(BotCustomClassRule.CustomClassId), Is.False);
        }

        [Test]
        public void SeededBuildDrawsMatchTheWeightsWithinHalfAPoint()
        {
            foreach (EraClass eraClass in Catalog.Classes)
            {
                var counts = new Dictionary<string, int>();
                for (ulong seed = 0; seed < Seeds; seed++)
                {
                    string id = Catalog.DrawBuild(eraClass.Name, seed).Id;
                    counts[id] = counts.GetValueOrDefault(id) + 1;
                }
                foreach (EraBuild build in eraClass.Builds)
                    Assert.That(counts.GetValueOrDefault(build.Id) * 100.0 / Seeds, Is.EqualTo(build.Weight * 100).Within(0.5), build.Id);
            }
        }

        [Test]
        public void OneSeedAlwaysGivesTheSameDraws()
        {
            foreach (ulong seed in new ulong[] { 0, 1, 42, 123456789, ulong.MaxValue })
            {
                Assert.That(Catalog.DrawClass("Midgard", seed), Is.EqualTo(Catalog.DrawClass("Midgard", seed)));
                Assert.That(Catalog.DrawBuild("Sorcerer", seed), Is.SameAs(Catalog.DrawBuild("Sorcerer", seed)));
                Assert.That(Catalog.IsOffMeta(seed), Is.EqualTo(Catalog.IsOffMeta(seed)));
                EraBuild build = Catalog.DrawBuild("Infiltrator", seed);
                Assert.That(Catalog.Jitter(build, seed, out string first), Is.EqualTo(Catalog.Jitter(build, seed, out string second)));
                Assert.That(first, Is.EqualTo(second));
            }
        }

        [Test]
        public void TenPercentAreOffMeta()
        {
            int off = 0;
            for (ulong seed = 0; seed < Seeds; seed++)
                if (Catalog.IsOffMeta(seed))
                    off++;
            Assert.That(off * 100.0 / Seeds, Is.EqualTo(10).Within(0.5));
            Assert.That(Catalog.IsOffMeta(7, 0), Is.False);
            Assert.That(Catalog.IsOffMeta(7, 100), Is.True);
        }

        [Test]
        public void JitterStaysInsideTheBudgetTheCapsAndTheLowerLines()
        {
            int moved = 0;
            foreach (EraBuild build in Catalog.Classes.SelectMany(c => c.Builds))
            {
                EraClass eraClass = Catalog.GetClass(build.ClassName);
                for (ulong seed = 0; seed < 200; seed++)
                {
                    var specs = Catalog.Jitter(build, seed, out string moves);
                    var before = build.Specs.ToDictionary(s => s.Key, s => s.Value);
                    var after = specs.ToDictionary(s => s.Key, s => s.Value);
                    Assert.That(after.Keys, Is.EquivalentTo(before.Keys), build.Id);
                    Assert.That(EraBuildCatalog.BuildCost(specs, eraClass.AutoTrainLines), Is.LessThanOrEqualTo(eraClass.PointBudget50), build.Id);
                    Assert.That(after.Values, Has.All.InRange(1, 50), build.Id);
                    Assert.That(after[build.Specs[0].Key], Is.EqualTo(build.Specs[0].Value), $"{build.Id}: the top line never moves");
                    int shift = before.Sum(kv => Math.Abs(kv.Value - after[kv.Key]));
                    Assert.That(shift, Is.LessThanOrEqualTo(2 * Catalog.JitterPoints), build.Id);
                    Assert.That(moves.Length > 0, Is.EqualTo(shift > 0), build.Id);
                    if (shift > 0)
                        moved++;
                }
            }
            Assert.That(moved, Is.GreaterThan(1000), "builds with two or more lower lines do vary");
            EraBuild twoLines = Catalog.GetBuild("sorcerer-mind-body");
            Assert.That(Catalog.Jitter(twoLines, 5, out string none), Is.EqualTo(twoLines.Specs));
            Assert.That(none, Is.Empty);
        }

        [Test]
        public void JitterNeverPutsALowerLineBeforeTheTopLineOnATie()
        {
            // Review of WP0-2 (2026-11-17): a lower line raised to tie the top line sorted first by name, and DerivedRatio
            // then gave it the top line's 1.0. "Axe" ties "Zz" at 44 here; "Zz" stays the first line.
            const string json = """
                {"schema": 1, "status": "test", "offMetaPercent": 10, "jitterPoints": 2,
                 "classes": {
                   "Shadowblade": {"classId": 23, "realm": "Midgard", "specMultiplier": 22, "lines": ["Zz", "Axe", "Envenom"],
                     "builds": [{"id": "tie", "family": "LeftAxe", "weight": 1, "specs": {"Zz": 44, "Axe": 43, "Envenom": 30}}]}},
                 "realms": {"Midgard": {"classShare": {"Shadowblade": {"share": 1.0}}}}}
                """;
            EraBuildCatalog catalog = EraBuildCatalog.Parse(json);
            EraBuild build = catalog.GetBuild("tie");
            int ties = 0;
            for (ulong seed = 0; seed < 2000; seed++)
            {
                var specs = catalog.Jitter(build, seed, out string moves);
                Assert.That(specs[0].Key, Is.EqualTo("Zz"), moves);
                if (specs.Count(line => line.Value == 44) == 2)
                    ties++;
            }
            Assert.That(ties, Is.GreaterThan(0), "some seed raises Axe to 44");
        }

        [Test]
        public void TheNearestBuildMatchesTheTwoHighestTrainedLines()
        {
            var trained = new Dictionary<string, int> { ["Body Magic"] = 30, ["Mind Magic"] = 40, ["Matter Magic"] = 1 };
            Assert.That(Catalog.NearestBuild("Sorcerer", trained)?.Id, Is.EqualTo("sorcerer-mind-body"), "either order, heavier build");
            trained = new Dictionary<string, int> { ["Matter Magic"] = 20, ["Mind Magic"] = 25 };
            Assert.That(Catalog.NearestBuild("Sorcerer", trained)?.Id, Is.EqualTo("sorcerer-matter-mind"));
            Assert.That(Catalog.NearestBuild("Sorcerer", new Dictionary<string, int> { ["Body Magic"] = 20, ["Matter Magic"] = 20 }), Is.Null);
            Assert.That(Catalog.NearestBuild("Sorcerer", new Dictionary<string, int> { ["Mind Magic"] = 20 }), Is.Null);
            Assert.That(Catalog.NearestBuild("Sluaghbinder", trained), Is.Null);
        }

        [Test]
        public void TheDerivedRatiosHaveTheClassFilesShape()
        {
            EraBuild build = Catalog.GetBuild("infiltrator-thrust-env");   // Thrust 50, Stealth 46, Envenom 36, CS 34, DW 10
            Assert.That(EraBuildCatalog.DerivedRatio(build, "Thrust"), Is.EqualTo(1f));
            Assert.That(EraBuildCatalog.DerivedRatio(build, "Stealth"), Is.EqualTo(0.6f + 0.2f * 11 / 15f).Within(1e-5));
            Assert.That(EraBuildCatalog.DerivedRatio(build, "Envenom"), Is.EqualTo(0.6f + 0.2f / 15f).Within(1e-5));
            Assert.That(EraBuildCatalog.DerivedRatio(build, "Critical Strike"), Is.EqualTo(34 / 50f * 0.6f).Within(1e-5));
            Assert.That(EraBuildCatalog.DerivedRatio(build, "Dual Wield"), Is.Zero);
        }

        [Test]
        public void ABrokenFileGivesAnEmptyCatalogThatSaysWhy()
        {
            EraBuildCatalog broken = EraBuildCatalog.Parse("{ not json");
            Assert.That(broken.IsLoaded, Is.False);
            Assert.That(broken.Problems.Single(), Does.Contain("does not parse"));
            Assert.That(broken.DrawClass("Albion", 1UL), Is.Null, "the launcher then draws evenly");
            Assert.That(broken.DrawBuild("Cleric", 1UL), Is.Null, "the server then keeps today's picker");
            Assert.That(EraBuildCatalog.Parse("""{"schema": 2, "classes": {}, "realms": {}}""").IsLoaded, Is.False);
        }

        [Test]
        public void ABadBuildIsDroppedAndAClassWithNoneFallsBack()
        {
            const string json = """
                {"schema": 1, "status": "test", "offMetaPercent": 10, "jitterPoints": 2,
                 "classes": {
                   "Sorcerer": {"classId": 8, "realm": "Albion", "specMultiplier": 10, "lines": ["Matter Magic", "Body Magic", "Mind Magic"],
                     "builds": [
                       {"id": "ok", "family": "MindSorc", "weight": 0.5, "specs": {"Mind Magic": 44, "Body Magic": 31}},
                       {"id": "over", "family": "MindSorc", "weight": 0.3, "specs": {"Mind Magic": 50, "Body Magic": 50}},
                       {"id": "badline", "family": "MindSorc", "weight": 0.2, "specs": {"Slash": 44}}]},
                   "Wizard": {"classId": 7, "realm": "Albion", "specMultiplier": 10, "lines": ["Fire Magic"],
                     "builds": [{"id": "nofamily", "family": "", "weight": 1, "specs": {"Fire Magic": 50}}]},
                   "Sluaghbinder": {"classId": 63, "realm": "Hibernia", "specMultiplier": 10, "lines": ["Sluagh Covenant"],
                     "builds": [{"id": "sl", "family": "SluaghbinderCovenant", "weight": 1, "specs": {"Sluagh Covenant": 50}}]}},
                 "realms": {
                   "Albion": {"classShare": {"Sorcerer": {"share": 0.5}, "Wizard": {"share": 0.5}}},
                   "Hibernia": {"classShare": {"Sluaghbinder": {"share": 1.0}}}}}
                """;
            EraBuildCatalog catalog = EraBuildCatalog.Parse(json);
            Assert.Multiple(() =>
            {
                Assert.That(catalog.IsLoaded, Is.True);
                Assert.That(catalog.GetClass("Sorcerer").Builds.Select(b => b.Id), Is.EqualTo(new[] { "ok" }));
                Assert.That(catalog.DroppedBuilds, Is.EqualTo(3));
                Assert.That(catalog.GetClass("Wizard"), Is.Null, "no valid build: today's picker");
                Assert.That(catalog.GetClass("Sluaghbinder"), Is.Null, "bots never play the custom class");
                Assert.That(catalog.Shares("Albion").Select(s => s.ClassName), Is.EqualTo(new[] { "Sorcerer" }));
                Assert.That(catalog.Shares("Hibernia"), Is.Empty);
                Assert.That(catalog.DrawClass("Hibernia", 3UL), Is.Null);
                Assert.That(catalog.Problems, Has.Some.Contains("build=over").And.Contains("a level 50 has 1494"));
                Assert.That(catalog.Problems, Has.Some.Contains("build=badline").And.Contains("Slash is not a Sorcerer line"));
                Assert.That(catalog.Problems, Has.Some.Contains("class=Sluaghbinder").And.Contains("custom class"));
            });
        }

        [Test]
        public void TheCustomClassRuleRefusesOnlyTheSluaghbinder()
        {
            Assert.That(BotCustomClassRule.BotsMayPlay((int)eCharacterClass.Sluaghbinder), Is.False);
            Assert.That(BotCustomClassRule.CustomClassId, Is.EqualTo((int)eCharacterClass.Sluaghbinder));
            foreach (eCharacterClass cls in Enum.GetValues<eCharacterClass>().Where(c => c != eCharacterClass.Sluaghbinder))
                Assert.That(BotCustomClassRule.BotsMayPlay((int)cls), Is.True, cls.ToString());
        }
    }
}
