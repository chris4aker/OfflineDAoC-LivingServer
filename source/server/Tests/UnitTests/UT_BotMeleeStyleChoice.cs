using System.Collections.Generic;
using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Night playtest 2026-11-04: melee bots spammed one style, taunt styles on damage dealers, chains never started.</summary>
    [TestFixture]
    public class UT_BotMeleeStyleChoice
    {
        private static Style Make(int id, double growth, int openingType = 0, int openingValue = 0, int attackResult = 0, int level = 10) =>
            new(new DbStyle { Name = $"s{id}", ID = id, SpecKeyName = "Sword", SpecLevelRequirement = level, WeaponTypeRequirement = 3,
                OpeningRequirementType = openingType, OpeningRequirementValue = openingValue, AttackResultRequirement = attackResult,
                GrowthRate = growth }, null);

        private static Style Taunt(int id, double growth)
        {
            Style style = Make(id, growth);
            style.Procs.Add(new StyleProcInfo(new Spell(new DbSpell { SpellID = BotMeleeStylePolicy.TauntSpellId, Name = "taunt",
                Type = "StyleTaunt", Target = "Enemy" }, 1), 0, 100));
            return style;
        }

        private static Style Pick(bool tauntWanted, Style lastUsed, double roll, params Style[] usable)
        {
            var openers = new HashSet<int>();
            foreach (Style style in usable)
                if (BotMeleeStylePolicy.IsChainFollowUp(style)) openers.Add(style.OpeningRequirementValue);
            var scored = new List<(Style, double)>();
            foreach (Style style in usable)
                scored.Add((style, BotMeleeStylePolicy.Score(style, openers.Contains(style.ID), tauntWanted)));
            return BotMeleeStylePolicy.Choose(scored, lastUsed, roll);
        }

        [Test]
        public void ADamageDealerDoesNotPickATauntStyle()
        {
            Style provoke = Taunt(1, 0.9);
            Style plague = Make(2, 0.5);
            Assert.That(BotMeleeStylePolicy.IsTauntStyle(provoke), Is.True);
            Assert.That(Pick(false, null, 0.9, provoke, plague), Is.SameAs(plague));
            // A grouped tank still may.
            Assert.That(Pick(true, null, 0.9, provoke, plague), Is.SameAs(provoke));
            // And a taunt is still better than no style at all.
            Assert.That(Pick(false, null, 0.9, provoke), Is.SameAs(provoke));
        }

        [Test]
        public void AChainOpenerIsPreferredAndItsFollowUpComesNext()
        {
            Style strong = Make(10, 0.8);
            Style opener = Make(11, 0.3);
            Style followUp = Make(12, 0.9, openingType: 0, openingValue: 11, attackResult: (int)Style.eAttackResultRequirement.Style);
            // Before the opener lands only the opener and the plain style are usable; the follow-up is known.
            var scored = new List<(Style, double)>
            {
                (strong, BotMeleeStylePolicy.Score(strong, false, false)),
                (opener, BotMeleeStylePolicy.Score(opener, true, false)),
            };
            Assert.That(BotMeleeStylePolicy.Choose(scored, null, 0.9), Is.SameAs(opener));
            Assert.That(Pick(false, opener, 0.9, strong, opener, followUp), Is.SameAs(followUp));
        }

        [Test]
        public void ABackOrSideStyleWinsWhenTheBotIsThere()
        {
            Style plain = Make(20, 0.9);
            Style back = Make(21, 0.4, openingType: (int)Style.eOpening.Positional, openingValue: (int)Style.eOpeningPosition.Back);
            Style front = Make(22, 0.3, openingType: (int)Style.eOpening.Positional, openingValue: (int)Style.eOpeningPosition.Front);
            Assert.That(BotMeleeStylePolicy.IsEligible(back), Is.True);
            Assert.That(Pick(false, null, 0.9, plain, back), Is.SameAs(back));
            Assert.That(Pick(false, null, 0.9, plain, front), Is.SameAs(plain), "a front style is an ordinary style");
        }

        [Test]
        public void AReactiveStyleBeatsAPlainOne()
        {
            Style plain = Make(30, 0.9);
            Style afterEvade = Make(31, 0.4, openingType: (int)Style.eOpening.Defensive,
                attackResult: (int)Style.eAttackResultRequirement.Evade);
            Assert.That(Pick(false, null, 0.9, plain, afterEvade), Is.SameAs(afterEvade));
        }

        [Test]
        public void TwoNearEqualPlainStylesTakeTurns()
        {
            Style a = Make(40, 0.9);
            Style b = Make(41, 0.8);
            Assert.That(Pick(false, null, 0.1, a, b), Is.SameAs(a));
            Assert.That(Pick(false, a, 0.1, a, b), Is.SameAs(b));
            Assert.That(Pick(false, a, 0.9, a, b), Is.SameAs(a));
            // A much weaker style does not take a turn.
            Style weak = Make(42, 0.2);
            Assert.That(Pick(false, a, 0.1, a, weak), Is.SameAs(a));
        }
    }
}
