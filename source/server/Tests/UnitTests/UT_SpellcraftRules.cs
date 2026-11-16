using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Crafting;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP1: SpellcraftRules.CheckImbue is SpellCrafting.IsAllowedToCombine's gem branch as a pure function
    /// (players and bot spellcrafters are checked alike), with Baptiste Marie's two upstream OpenDAoC fixes (b62885a86:
    /// a fifth bonus was accepted; aa8b5232b: focus gems were not limited to staves), via DividedRealms (a3f3e4b8,
    /// 60d3a4fb). The numbers themselves are WP0's parity-tested copies (UT_CraftRulesParity).
    /// </summary>
    [TestFixture]
    public class UT_SpellcraftRules
    {
        private const int Skill = 1000; // skill / 20 = 50 imbue points allowed

        private static ImbueGem Gem(eProperty bonus, int value, int quality = 99, bool focus = false) =>
            new((int)eObjectType.SpellcraftGem, (int)bonus, value, quality, focus);

        private static ImbueBonus[] Bonuses(params (eProperty Type, int Value)[] bonuses)
        {
            var all = bonuses.Select(bonus => new ImbueBonus((int)bonus.Type, bonus.Value)).ToList();
            while (all.Count < 4)
                all.Add(new ImbueBonus(0, 0));
            return all.ToArray();
        }

        private static ImbueRefusal Check(ImbueBonus[] item, bool staff, params ImbueGem[] gems) =>
            SpellcraftRules.CheckImbue(item, 51, 100, staff, gems, Skill);

        [Test]
        public void FourGemsOnABlankItemAreAllowed()
        {
            Assert.That(Check(Bonuses(), false,
                Gem(eProperty.Strength, 4), Gem(eProperty.Dexterity, 4), Gem(eProperty.Resist_Body, 3), Gem(eProperty.Resist_Heat, 3)),
                Is.EqualTo(ImbueRefusal.None));
        }

        [Test]
        public void AFifthBonusIsRefused_TheUpstreamCountFix()
        {
            // Before b62885a86 the check was "Count > 4" before adding, so an item with three bonuses took two gems (five).
            ImbueBonus[] threeBonuses = Bonuses((eProperty.Strength, 4), (eProperty.Dexterity, 4), (eProperty.Constitution, 4));
            Assert.Multiple(() =>
            {
                Assert.That(Check(threeBonuses, false, Gem(eProperty.Resist_Body, 3)), Is.EqualTo(ImbueRefusal.None), "the fourth is fine");
                Assert.That(Check(threeBonuses, false, Gem(eProperty.Resist_Body, 3), Gem(eProperty.Resist_Heat, 3)),
                    Is.EqualTo(ImbueRefusal.TooManyBonuses), "a fifth is refused");
                Assert.That(Check(Bonuses(), false, Gem(eProperty.Strength, 1), Gem(eProperty.Dexterity, 1), Gem(eProperty.Quickness, 1),
                    Gem(eProperty.Intelligence, 1), Gem(eProperty.Piety, 1)), Is.EqualTo(ImbueRefusal.TooManyBonuses), "five gems at once");
            });
        }

        [Test]
        public void AnItemWithFourBonusesIsAlreadyImbued()
        {
            ImbueBonus[] four = Bonuses((eProperty.Strength, 4), (eProperty.Dexterity, 4), (eProperty.Constitution, 4), (eProperty.Quickness, 4));
            Assert.That(Check(four, false, Gem(eProperty.Resist_Body, 3)), Is.EqualTo(ImbueRefusal.AlreadyImbued));
        }

        [Test]
        public void ABonusAfterAnEmptySlotDoesNotCount_AsTheOriginalNesting()
        {
            // Bonus1 empty: the original never looked at Bonus2-4, so the item counts as blank.
            var item = new[] { new ImbueBonus(0, 0), new ImbueBonus((int)eProperty.Strength, 4), new ImbueBonus((int)eProperty.Dexterity, 4), new ImbueBonus((int)eProperty.Constitution, 4) };
            Assert.That(Check(item, false, Gem(eProperty.Strength, 4)), Is.EqualTo(ImbueRefusal.None));
        }

        [Test]
        public void FocusGemsGoOnStavesOnly_TheUpstreamStaffFix()
        {
            ImbueGem focus = Gem(eProperty.Focus_Fire, 10, focus: true);
            Assert.Multiple(() =>
            {
                Assert.That(Check(Bonuses(), false, focus), Is.EqualTo(ImbueRefusal.FocusNotOnStaff));
                Assert.That(Check(Bonuses(), true, focus), Is.EqualTo(ImbueRefusal.None));
                Assert.That(Check(Bonuses(), false, Gem(eProperty.Strength, 4)), Is.EqualTo(ImbueRefusal.None), "other gems on anything");
            });
        }

        [Test]
        public void ChecksRunInTheOriginalOrder()
        {
            var notAGem = new ImbueGem((int)eObjectType.GenericItem, (int)eProperty.Focus_Fire, 10, 99, true);
            Assert.Multiple(() =>
            {
                Assert.That(Check(Bonuses(), false, notAGem), Is.EqualTo(ImbueRefusal.NotAGem), "not a gem before the staff rule");
                Assert.That(Check(Bonuses((eProperty.Focus_Fire, 10)), false, Gem(eProperty.Focus_Fire, 10, focus: true)),
                    Is.EqualTo(ImbueRefusal.FocusNotOnStaff), "the staff rule before the same-bonus rule");
                Assert.That(Check(Bonuses((eProperty.Strength, 4)), false, Gem(eProperty.Strength, 4)), Is.EqualTo(ImbueRefusal.SameBonus));
                Assert.That(Check(Bonuses(), false, Gem(eProperty.Strength, 4), Gem(eProperty.Strength, 4)), Is.EqualTo(ImbueRefusal.SameBonus), "two equal gems");
            });
        }

        [Test]
        public void SkillAndOverchargeLimits()
        {
            ImbueGem[] gems = { Gem(eProperty.Strength, 13), Gem(eProperty.Resist_Body, 7) }; // 9 + 12, total (21 + 12) / 2 = 16
            Assert.Multiple(() =>
            {
                Assert.That(SpellcraftRules.TotalImbuePoints(null, SpellcraftRules.GemBonuses(gems)), Is.EqualTo(16));
                Assert.That(SpellcraftRules.CheckImbue(Bonuses(), 51, 100, false, gems, 16 * 20), Is.EqualTo(ImbueRefusal.None));
                Assert.That(SpellcraftRules.CheckImbue(Bonuses(), 51, 100, false, gems, 16 * 20 - 1), Is.EqualTo(ImbueRefusal.NotEnoughSkill));
                // level 15, quality 96: capacity round(0.353 * 15) = 5; 16 points is 11 over (> 6)
                Assert.That(SpellcraftRules.CheckImbue(Bonuses(), 15, 96, false, gems, Skill), Is.EqualTo(ImbueRefusal.OverchargeTooFar));
                // level 51, quality 96: capacity 18, so 16 is under
                Assert.That(SpellcraftRules.CheckImbue(Bonuses(), 51, 96, false, gems, Skill), Is.EqualTo(ImbueRefusal.None));
            });
        }

        [Test]
        public void EveryRefusalHasItsLanguageKey()
        {
            foreach (ImbueRefusal refusal in System.Enum.GetValues<ImbueRefusal>())
            {
                string key = SpellcraftRules.RefusalKey(refusal);
                if (refusal == ImbueRefusal.None)
                    Assert.That(key, Is.Null);
                else
                    Assert.That(key, Does.StartWith("SpellCrafting.IsAllowedToCombine."), refusal.ToString());
            }
            Assert.That(SpellcraftRules.RefusalKey(ImbueRefusal.FocusNotOnStaff), Is.EqualTo("SpellCrafting.IsAllowedToCombine.NotStaff"));
        }

        [Test]
        public void NoGemsAndNoItemAreHandled()
        {
            Assert.That(SpellcraftRules.CheckImbue(null, 51, 100, false, null, Skill), Is.EqualTo(ImbueRefusal.None));
            Assert.That(SpellcraftRules.CheckImbue(new List<ImbueBonus>(), 51, 100, false, new List<ImbueGem>(), 0), Is.EqualTo(ImbueRefusal.None));
        }
    }
}
