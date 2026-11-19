using System;
using System.Linq;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP1: the optional classic rules, both off by default. crafting_classic_quality rolls 94-99% with a
    /// small masterpiece chance at every skill; crafting_classic_losses makes a failed craft cost its materials and a
    /// spellcraft explosion destroy the item. Off, everything is the 1.87 rule WP0 pinned (UT_CraftRulesParity).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CraftRulesClassic
    {
        private bool _quality;
        private bool _losses;

        [SetUp]
        public void SetUp()
        {
            _quality = CraftingEconomyProperties.CRAFTING_CLASSIC_QUALITY;
            _losses = CraftingEconomyProperties.CRAFTING_CLASSIC_LOSSES;
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.CRAFTING_CLASSIC_QUALITY = _quality;
            CraftingEconomyProperties.CRAFTING_CLASSIC_LOSSES = _losses;
        }

        private static int[] Roll(int skill, int level, int rolls, int seed, bool classic)
        {
            var random = new Random(seed);
            var counts = new int[101];
            for (int i = 0; i < rolls; i++)
                counts[CraftRules.RollQuality(skill, level, random, classic)]++;
            return counts;
        }

        [Test]
        public void BothClassicRulesAreOffByDefault()
        {
            var fresh = typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.CRAFTING_CLASSIC_QUALITY));
            Assert.That(fresh.GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false)
                .Cast<DOL.GS.ServerProperties.ServerPropertyAttribute>().Single().DefaultValue, Is.EqualTo(false));
            Assert.That(CraftRules.FailureCostsMaterials(false), Is.False);
            Assert.That(CraftRules.ExplosionDestroysItem(false), Is.False);
            Assert.That(CraftRules.FailureCostsMaterials(true), Is.True);
            Assert.That(CraftRules.ExplosionDestroysItem(true), Is.True);
        }

        [Test]
        public void OffTheRollIsExactlyThe187Roll()
        {
            var a = new Random(31);
            var b = new Random(31);
            for (int i = 0; i < 5000; i++)
                Assert.That(CraftRules.RollQuality(480, 490, a, false), Is.EqualTo(CraftRules.RollQuality(480, 490, b)));
        }

        [TestCase(500, 495)]   // yellow
        [TestCase(500, 515)]   // orange
        [TestCase(500, 470)]   // blue
        [TestCase(500, 540)]   // red
        public void ClassicQualityIs94To99_WithASmallMasterpieceChanceAtAnySkill(int skill, int level)
        {
            const int Rolls = 200_000;
            int[] counts = Roll(skill, level, Rolls, 4127, classic: true);
            Assert.Multiple(() =>
            {
                Assert.That(counts.Take(94).Sum(), Is.Zero, "never under 94");
                Assert.That(counts[94], Is.GreaterThan(0), "94 happens");
                Assert.That(counts[95], Is.GreaterThan(0), "95 happens");
                Assert.That(counts[100] / (double)Rolls, Is.InRange(0.007, 0.013), "about 1% masterpieces below 1000");
            });
        }

        [TestCase(1000, 900)]  // legendary
        [TestCase(500, 400)]   // grey
        public void AtLegendaryOrGrey_2PercentMasterpieces_AndEven94To99(int skill, int level)
        {
            const int Rolls = 300_000;
            int[] counts = Roll(skill, level, Rolls, 9, classic: true);
            Assert.That(counts[100] / (double)Rolls, Is.InRange(0.016, 0.024));
            for (int quality = 94; quality <= 99; quality++)
                Assert.That(counts[quality] / (double)Rolls, Is.EqualTo(0.98 / 6).Within(0.006), $"{quality}%");
        }

        [Test]
        public void AHarderRecipeLeansTo94()
        {
            double Mean(int level)
            {
                int[] counts = Roll(500, level, 100_000, 5, classic: true);
                return Enumerable.Range(94, 6).Sum(q => q * (double)counts[q]) / Enumerable.Range(94, 6).Sum(q => (double)counts[q]);
            }
            double blue = Mean(475), yellow = Mean(500), red = Mean(540);
            Assert.That(blue, Is.GreaterThan(yellow));
            Assert.That(yellow, Is.GreaterThan(red));
            Assert.That(red, Is.LessThan(95.5));
        }

        [Test]
        public void ThePlayersQualityFollowsTheSetting()
        {
            ArmorCrafting craft = (ArmorCrafting)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ArmorCrafting));
            craft.eSkill = eCraftingSkill.ArmorCrafting;

            CraftingEconomyProperties.CRAFTING_CLASSIC_QUALITY = false;
            int[] off = Enumerable.Range(0, 20_000).Select(_ => craft.QualityAt(500, 500)).ToArray();
            CraftingEconomyProperties.CRAFTING_CLASSIC_QUALITY = true;
            int[] on = Enumerable.Range(0, 20_000).Select(_ => craft.QualityAt(500, 500)).ToArray();

            Assert.That(off.Min(), Is.GreaterThanOrEqualTo(96), "the 1.87 rule");
            Assert.That(off.Max(), Is.LessThanOrEqualTo(99), "no masterpiece on a yellow recipe under 1.87");
            Assert.That(on.Min(), Is.EqualTo(94));
            Assert.That(on.Count(q => q == 100), Is.GreaterThan(0), "classic: a masterpiece can happen at 500");
        }
    }
}
