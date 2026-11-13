using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.AutonomousBotCosmetics;

namespace DOL.UnitTests
{
    /// <summary>Bot cosmetics (owner 2026-11-12): each gamebot has its own look and pays coin for dyes and glows.</summary>
    [TestFixture]
    public class UT_BotCosmetics
    {
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
            int plain = styles.Count(s => s.Vanity == Vanity.Plain), peacock = styles.Count(s => s.Vanity == Vanity.Peacock);
            Assert.That(plain, Is.InRange(300, 500), "about a fifth never bother");
            Assert.That(peacock, Is.InRange(120, 280), "about a tenth are peacocks");
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
        public void ADyeFitsTheMaterial()
        {
            foreach (Material material in new[] { Material.Cloth, Material.Leather, Material.Metal })
            foreach (Hue hue in System.Enum.GetValues<Hue>())
            foreach (int id in ColoursOf(hue, material))
                Assert.That(Fits(id, material), $"{hue} {id} on {material}");
            Assert.That(Fits(30, Material.Cloth) && !Fits(30, Material.Metal), "a cloth dye is not an enamel");
            Assert.That(Fits(48, Material.Metal) && !Fits(48, Material.Leather), "gold metal is an enamel");
            Assert.That(Fits(9, Material.Leather) && !Fits(9, Material.Cloth), "leather red is a leather dye");
            Assert.That(Fits(64, Material.Cloth) && Fits(64, Material.Leather) && Fits(64, Material.Metal), "a general dye");
        }

        [Test]
        public void EveryHueHasADyeForEveryMaterialOrFallsBackToGray()
        {
            var style = new Style(Vanity.Flashy, Hue.Black, Hue.Orange, 17);
            foreach (Material material in new[] { Material.Cloth, Material.Leather, Material.Metal })
            {
                int? chest = WantedColour(style, eInventorySlot.TorsoArmor, material);
                int? arms = WantedColour(style, eInventorySlot.ArmsArmor, material);
                Assert.That(chest.HasValue && Fits(chest.Value, material), $"chest on {material}");
                Assert.That(arms.HasValue && Fits(arms.Value, material), $"arms on {material}");
            }
        }

        [Test]
        public void TidyWearsOneColourFlashyTwoPlainNone()
        {
            var tidy = new Style(Vanity.Tidy, Hue.Red, Hue.Blue, 5);
            Assert.That(HueOf(WantedColour(tidy, eInventorySlot.ArmsArmor, Material.Cloth)!.Value), Is.EqualTo(Hue.Red));
            var flashy = tidy with { Vanity = Vanity.Flashy };
            Assert.That(HueOf(WantedColour(flashy, eInventorySlot.TorsoArmor, Material.Cloth)!.Value), Is.EqualTo(Hue.Red));
            Assert.That(HueOf(WantedColour(flashy, eInventorySlot.ArmsArmor, Material.Cloth)!.Value), Is.EqualTo(Hue.Blue));
            Assert.That(WantedColour(tidy with { Vanity = Vanity.Plain }, eInventorySlot.TorsoArmor, Material.Cloth), Is.Null);
            Assert.That(WantedColour(flashy, eInventorySlot.TorsoArmor, Material.Metal),
                Is.EqualTo(WantedColour(flashy, eInventorySlot.LegsArmor, Material.Metal)), "matching pieces");
        }

        [Test]
        public void JewelleryAndWeaponsAreNotDyedButCloaksAre()
        {
            Assert.That(MaterialOf((int)eObjectType.Magical, (int)eInventorySlot.Cloak), Is.EqualTo(Material.Cloth));
            Assert.That(MaterialOf((int)eObjectType.Magical, (int)eInventorySlot.Neck), Is.Null);
            Assert.That(MaterialOf((int)eObjectType.Sword, (int)eInventorySlot.RightHandWeapon), Is.Null);
            Assert.That(MaterialOf((int)eObjectType.Studded, (int)eInventorySlot.TorsoArmor), Is.EqualTo(Material.Leather));
            Assert.That(MaterialOf((int)eObjectType.Scale, (int)eInventorySlot.LegsArmor), Is.EqualTo(Material.Metal));
        }

        [Test]
        public void GlowsSuitTheWeaponAndTheShowOff()
        {
            var flashy = new Style(Vanity.Flashy, Hue.Red, Hue.Blue, 99);
            int? sword = WantedGlow(flashy, eInventorySlot.RightHandWeapon, (int)eObjectType.SlashingWeapon, 50);
            Assert.That(sword, Is.Not.Null);
            Assert.That(GlowsFor((int)eObjectType.SlashingWeapon), Does.Contain(sword!.Value));
            Assert.That(WantedGlow(flashy, eInventorySlot.LeftHandWeapon, (int)eObjectType.SlashingWeapon, 50), Is.Null,
                "a flashy bot glows its main weapon only");
            Assert.That(WantedGlow(flashy with { Vanity = Vanity.Peacock }, eInventorySlot.LeftHandWeapon,
                (int)eObjectType.SlashingWeapon, 50), Is.Not.Null, "a peacock glows both");
            Assert.That(WantedGlow(flashy with { Vanity = Vanity.Tidy }, eInventorySlot.RightHandWeapon,
                (int)eObjectType.SlashingWeapon, 50), Is.Null, "tidy bots don't buy glows");
            Assert.That(WantedGlow(flashy, eInventorySlot.RightHandWeapon, (int)eObjectType.SlashingWeapon, 10), Is.Null, "too young");
            Assert.That(WantedGlow(flashy, eInventorySlot.DistanceWeapon, (int)eObjectType.Longbow, 50), Is.Null, "no bow glows");
            Assert.That(GlowsFor((int)eObjectType.Staff), Does.Contain(90));
            Assert.That(GlowsFor((int)eObjectType.Axe).All(g => g is >= 56 and <= 66));
        }

        [Test]
        public void TheyOnlySpendWhatTheyCanSpare()
        {
            long reserve = Reserve(50);
            Assert.That(Budget(Vanity.Peacock, reserve, 50), Is.Zero, "nothing above the reserve");
            Assert.That(Budget(Vanity.Plain, reserve * 10, 50), Is.Zero);
            Assert.That(Budget(Vanity.Tidy, reserve + 1_000_000, 50), Is.EqualTo(100_000));
            Assert.That(Budget(Vanity.Flashy, reserve + 1_000_000, 50), Is.EqualTo(250_000));
            Assert.That(Budget(Vanity.Peacock, reserve + 1_000_000, 50), Is.EqualTo(500_000));
        }

        [Test]
        public void PricesScaleWithLevelAndTheSetting()
        {
            Assert.That(DyePrice(Material.Cloth, 50), Is.EqualTo(5_000));
            Assert.That(DyePrice(Material.Metal, 50), Is.EqualTo(20_000));
            Assert.That(GlowPrice(50), Is.EqualTo(500_000));
            Assert.That(DyePrice(Material.Metal, 25), Is.EqualTo(10_000));
            Assert.That(DyePrice(Material.Cloth, 1), Is.EqualTo(500), "never under a tenth");
            Assert.That(GlowPrice(50, 200), Is.EqualTo(1_000_000));
        }
    }
}
