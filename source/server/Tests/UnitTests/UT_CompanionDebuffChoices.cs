using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-08: "pick which debuffs are used before nuking as long as they stack on a target, or choose just one,
    /// or just two, or none ... if debuffs before nuking is on". One AI Options row per debuff kind (Automatic / Use /
    /// Skip) under "Debuff before nuking" (Automatic / Always / Never).
    /// </summary>
    [TestFixture]
    public class UT_CompanionDebuffChoices
    {
        [Test]
        public void MasterAndPerDebuffChoices()
        {
            Assert.That(CompanionOptions165.DebuffAllowed(0, 0, false), Is.True, "all automatic: the companion's own pick");
            Assert.That(CompanionOptions165.DebuffAllowed(2, 1, true), Is.False, "Debuff before nuking: Never");
            Assert.That(CompanionOptions165.DebuffAllowed(0, 2, false), Is.False, "Skip");
            Assert.That(CompanionOptions165.DebuffAllowed(1, 1, true), Is.True, "marked Use");
            Assert.That(CompanionOptions165.DebuffAllowed(0, 0, true), Is.False, "others marked Use: only those");
        }

        [Test]
        public void DebuffKindsAreKeyedByEffectDamageTypeAndShape()
        {
            Spell armour = new(new DbSpell { SpellID = 1, Type = "ArmorFactorDebuff", Target = "Enemy", Range = 1500 }, 14);
            Spell armourRank2 = new(new DbSpell { SpellID = 2, Type = "ArmorFactorDebuff", Target = "Enemy", Range = 1500 }, 22);
            Spell areaCon = new(new DbSpell { SpellID = 3, Type = "ConstitutionDebuff", Target = "Enemy", Range = 1500, Radius = 350 }, 11);
            Assert.That(CompanionOptions165.DebuffLineKey(armour), Is.EqualTo(CompanionOptions165.DebuffLineKey(armourRank2)),
                "ranks of one debuff are one row");
            Assert.That(CompanionOptions165.DebuffLineKey(armour), Is.Not.EqualTo(CompanionOptions165.DebuffLineKey(areaCon)));
        }
    }
}
