using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.AutonomousBotCosmetics;

namespace DOL.UnitTests
{
    /// <summary>Bot cosmetics (owner 2026-11-12/13): each gamebot has its own look and pays the game's own dye prices.</summary>
    [TestFixture]
    public class UT_BotCosmetics
    {
        // A slice of the world database's dye items (model, colour, price in copper, name), as read on 2026-11-13.
        private static readonly (int, int, long, string)[] Templates =
        [
            (229, 64, 50, "light red cloth dye"), (229, 65, 100, "red cloth dye"), (229, 66, 165000, "royal red cloth dye"),
            (229, 67, 165000, "crimson cloth dye"), (229, 52, 100, "blue cloth dye"), (229, 53, 165000, "royal blue cloth dye"),
            (229, 52, 100, "blue hull paint"), (229, 0, 100, "cloth bleach"), (229, 74, 165000, "black cloth dye"),
            (229, 73, 6875, "dark gray cloth dye"),
            (494, 64, 50, "light red leather dye"), (494, 65, 1375, "red leather dye"), (494, 66, 165000, "royal red leather dye"),
            (494, 52, 1375, "blue leather dye"), (494, 73, 6875, "dark gray leather dye"),
            (495, 65, 1375, "red enamel"), (495, 67, 165000, "crimson enamel"), (495, 52, 1375, "blue enamel"),
            (495, 73, 6875, "dark gray enamel"), (495, 0, 100, "mild acid enamel remover"),
            (538, 120, 1958110, "red weapon luster"), (538, 52, 1958110, "blue weapon luster"),
            (538, 43, 975, "Black Weapon Enamel"), (538, 0, 100, "rose weapon luster"),
        ];

        private static readonly Dye[] Catalog = BuildCatalog(Templates);

        [Test]
        public void TheCatalogIsTheGamesOwnDyes()
        {
            Assert.That(Catalog.Any(d => d.Name.Contains("paint") || d.Name.Contains("bleach") || d.Name.Contains("remover")), Is.False);
            Assert.That(Catalog.Any(d => d.Name == "Black Weapon Enamel"), Is.False, "custom weapon enamels are not lusters");
            Assert.That(Catalog.Single(d => d.Material == Material.Cloth && d.Colour == 52).Price, Is.EqualTo(100), "cheapest kept");
            Assert.That(Catalog.Count(d => d.Material == Material.Weapon), Is.EqualTo(2));
            Assert.That(Catalog.All(d => d.Colour > 0));
        }

        [Test]
        public void ABotsStyleIsTheSameEveryTime()
        {
            Assert.That(StyleOf(4711, eRealm.Albion), Is.EqualTo(StyleOf(4711, eRealm.Albion)));
            Assert.That(StyleOf(4711, eRealm.Albion, 99), Is.EqualTo(StyleOf(4711, eRealm.Albion, 99)));
        }

        [Test]
        public void BotsLookDifferentAndSomeDoNotCare()
        {
            var styles = Enumerable.Range(1, 2000).Select(id => StyleOf(id, eRealm.Hibernia)).ToArray();
            Assert.That(styles.Count(s => s.Vanity == Vanity.Plain), Is.InRange(300, 500), "about a fifth never bother");
            Assert.That(styles.Count(s => s.Vanity == Vanity.Peacock), Is.InRange(120, 280), "about a tenth are peacocks");
            Assert.That(styles.Select(s => (s.Primary, s.Secondary)).Distinct().Count(), Is.GreaterThan(30), "many colour pairs");
            Assert.That(styles.Count(s => s.Primary == s.Secondary), Is.LessThan(40), "two colours, rarely the same");
        }

        [Test]
        public void RealmsLeanOnTheirColours()
        {
            int Count(eRealm realm, Hue hue) => Enumerable.Range(1, 3000).Count(id => StyleOf(id, realm).Primary == hue);
            Assert.That(Count(eRealm.Hibernia, Hue.Green), Is.GreaterThan(Count(eRealm.Albion, Hue.Green) * 3));
            Assert.That(Count(eRealm.Albion, Hue.Red), Is.GreaterThan(Count(eRealm.Hibernia, Hue.Red) * 3));
            Assert.That(Count(eRealm.Midgard, Hue.Blue), Is.GreaterThan(Count(eRealm.Hibernia, Hue.Blue) * 2));
        }

        [Test]
        public void AGuildOftenWearsOneColour()
        {
            var members = Enumerable.Range(1, 400).Select(id => StyleOf(id, eRealm.Albion, guildId: 12345).Primary).ToArray();
            Hue uniform = members.GroupBy(h => h).OrderByDescending(g => g.Count()).First().Key;
            Assert.That(members.Count(h => h == uniform), Is.GreaterThan(200), "half wear the guild's colour, and some by chance");
        }

        [Test]
        public void TheDyeFitsTheMaterialAndExists()
        {
            var style = new Style(Vanity.Flashy, Hue.Red, Hue.Blue, 17);
            foreach (Material material in new[] { Material.Cloth, Material.Leather, Material.Metal })
            {
                Dye? chest = WantedDye(style, eInventorySlot.TorsoArmor, material, Catalog);
                Assert.That(chest, Is.Not.Null);
                Assert.That(chest!.Value.Material, Is.EqualTo(material));
                Assert.That(HueOf(chest.Value.Colour), Is.EqualTo(Hue.Red));
                Assert.That(Catalog, Does.Contain(chest.Value));
            }
        }

        [Test]
        public void AColourWithNoDyeFallsBackToGray()
        {
            var style = new Style(Vanity.Flashy, Hue.Orange, Hue.Orange, 3);
            Dye? dye = WantedDye(style, eInventorySlot.TorsoArmor, Material.Metal, Catalog);
            Assert.That(dye?.Colour, Is.EqualTo(73), "no orange enamel in this slice: dark gray");
        }

        [Test]
        public void TidyWearsCheapShadesAndPeacocksDearOnes()
        {
            var tidy = new Style(Vanity.Tidy, Hue.Red, Hue.Blue, 5);
            var peacock = tidy with { Vanity = Vanity.Peacock };
            Assert.That(WantedDye(tidy, eInventorySlot.TorsoArmor, Material.Cloth, Catalog)!.Value.Price, Is.LessThanOrEqualTo(100));
            Assert.That(WantedDye(tidy with { Primary = Hue.Gray }, eInventorySlot.TorsoArmor, Material.Leather, Catalog)!.Value.Price,
                Is.LessThanOrEqualTo(TidyMostPerPiece), "tidy never pays for the dear shades");
            Assert.That(WantedDye(peacock, eInventorySlot.TorsoArmor, Material.Cloth, Catalog)!.Value.Price, Is.EqualTo(165000));
            Assert.That(HueOf(WantedDye(tidy, eInventorySlot.ArmsArmor, Material.Cloth, Catalog)!.Value.Colour), Is.EqualTo(Hue.Red),
                "tidy: one colour");
            Assert.That(HueOf(WantedDye(peacock, eInventorySlot.ArmsArmor, Material.Cloth, Catalog)!.Value.Colour), Is.EqualTo(Hue.Blue),
                "peacock: second colour on the arms");
            Assert.That(WantedDye(tidy with { Vanity = Vanity.Plain }, eInventorySlot.TorsoArmor, Material.Cloth, Catalog), Is.Null);
            Assert.That(WantedDye(peacock, eInventorySlot.TorsoArmor, Material.Metal, Catalog),
                Is.EqualTo(WantedDye(peacock, eInventorySlot.LegsArmor, Material.Metal, Catalog)), "matching pieces");
        }

        [Test]
        public void WeaponsGetALusterFromShowOffsOnly()
        {
            var flashy = new Style(Vanity.Flashy, Hue.Red, Hue.Blue, 9);
            Dye? luster = WantedDye(flashy, eInventorySlot.RightHandWeapon, Material.Weapon, Catalog);
            Assert.That(luster?.Name, Is.EqualTo("red weapon luster"));
            Assert.That(luster!.Value.Price, Is.EqualTo(1958110), "about 196 gold, as the game prices it");
            Assert.That(WantedDye(flashy with { Vanity = Vanity.Tidy }, eInventorySlot.RightHandWeapon, Material.Weapon, Catalog), Is.Null);
        }

        [Test]
        public void WhatGetsDyed()
        {
            Assert.That(MaterialOf((int)eObjectType.Magical, (int)eInventorySlot.Cloak), Is.EqualTo(Material.Cloth));
            Assert.That(MaterialOf((int)eObjectType.Magical, (int)eInventorySlot.Neck), Is.Null);
            Assert.That(MaterialOf((int)eObjectType.Sword, (int)eInventorySlot.RightHandWeapon), Is.EqualTo(Material.Weapon));
            Assert.That(MaterialOf((int)eObjectType.Shield, (int)eInventorySlot.LeftHandWeapon), Is.Null);
            Assert.That(MaterialOf((int)eObjectType.Studded, (int)eInventorySlot.TorsoArmor), Is.EqualTo(Material.Leather));
            Assert.That(MaterialOf((int)eObjectType.Scale, (int)eInventorySlot.LegsArmor), Is.EqualTo(Material.Metal));
        }

        [Test]
        public void TheyPayTheGamesPriceAndOnlyWhatTheyCanSpare()
        {
            Dye royal = Catalog.First(d => d.Name == "royal red cloth dye");
            Assert.That(PriceOf(royal), Is.EqualTo(165000));
            Assert.That(PriceOf(royal, 200), Is.EqualTo(330000));
            long reserve = Reserve(50);
            Assert.That(Budget(Vanity.Peacock, reserve, 50), Is.Zero, "nothing above the reserve");
            Assert.That(Budget(Vanity.Plain, reserve * 10, 50), Is.Zero);
            Assert.That(Budget(Vanity.Tidy, reserve + 1_000_000, 50), Is.EqualTo(100_000));
            Assert.That(Budget(Vanity.Peacock, reserve + 1_000_000, 50), Is.EqualTo(500_000));
        }
    }
}
