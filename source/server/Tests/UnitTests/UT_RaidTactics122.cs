using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2.2, the raid tactic audit's fixes (everything but the Green Knight): pure rules. Tests only:
    /// nothing here has been played in game.
    /// </summary>
    [TestFixture]
    public sealed class UT_RaidTactics122
    {
        // ---- finding 3: the paladin, friar and warden fight in melee and heal from there -----------------------------------

        [TestCase(eCharacterClass.Paladin, true)]
        [TestCase(eCharacterClass.Friar, true)]
        [TestCase(eCharacterClass.Warden, true)]
        [TestCase(eCharacterClass.Cleric, false)]
        [TestCase(eCharacterClass.Druid, false)]
        [TestCase(eCharacterClass.Healer, false)]
        [TestCase(eCharacterClass.Shaman, false)]
        public void MeleeHybridsHealButFightInMelee(eCharacterClass cls, bool hybrid)
        {
            Assert.That(BotPartyRoles.IsHealingClass(cls), Is.True, "every one of them heals");
            Assert.That(BotPartyRoles.IsMeleeHybridHealer(cls), Is.EqualTo(hybrid));
            Assert.That(BotPartyRoles.IsRangedHealer(cls), Is.EqualTo(!hybrid));
            Assert.That(T.FightsFromRange(false, (int)cls), Is.EqualTo(!hybrid), "where it stands in every raid rule");
            Assert.That(new T.RuleContext(null, null, null, 0, false, 0, (int)cls).AtRange, Is.EqualTo(!hybrid));
        }

        [Test]
        public void CastersAndArchersStillFightFromRangeAndMeleeDoNot()
        {
            Assert.That(T.FightsFromRange(true, (int)eCharacterClass.Eldritch), Is.True);
            Assert.That(T.FightsFromRange(true, (int)eCharacterClass.Ranger), Is.True);
            Assert.That(T.FightsFromRange(false, (int)eCharacterClass.Hero), Is.False);
            Assert.That(T.FightsFromRange(false, 0), Is.False, "no class");
            Assert.That(T.IsRangedHealerClass(0), Is.False);
        }

        [Test]
        public void EveryHealingClassIsEitherAMeleeHybridOrARangedHealer()
        {
            foreach (eCharacterClass cls in Enum.GetValues<eCharacterClass>().Where(BotPartyRoles.IsHealingClass))
                Assert.That(BotPartyRoles.IsMeleeHybridHealer(cls) ^ BotPartyRoles.IsRangedHealer(cls), Is.True, cls.ToString());
            // The keep lord formation's healer band (RvrLordFormation.RoleOf) holds the same ranged healers.
            foreach (eCharacterClass cls in new[] { eCharacterClass.Cleric, eCharacterClass.Druid, eCharacterClass.Healer, eCharacterClass.Shaman })
                Assert.That(RvrLordFormation.RoleOf(cls, false), Is.EqualTo(RvrLordFormation.Role.Healer));
            Assert.That(RvrLordFormation.RoleOf(eCharacterClass.Friar, false), Is.EqualTo(RvrLordFormation.Role.Melee));
            Assert.That(RvrLordFormation.RoleOf(eCharacterClass.Warden, false), Is.EqualTo(RvrLordFormation.Role.Melee));
        }

        [Test]
        public void TheSacristanAmbushGivesMeleeHybridsTheMeleeReach()
        {
            // The ambush rule's reach comes from AtRange: a friar waits for him at the melee station's 450, not the casters' 1,400.
            var friar = new T.RuleContext(null, null, null, 0, false, 0, (int)eCharacterClass.Friar);
            var cleric = new T.RuleContext(null, null, null, 0, false, 0, (int)eCharacterClass.Cleric);
            Assert.That((friar.AtRange, cleric.AtRange), Is.EqualTo((false, true)));
        }

        [Test]
        public void ABlastReactionLapsesAsTheMeleeForAMeleeHybrid()
        {
            Assert.That(T.IsRangedHealerClass((int)eCharacterClass.Friar), Is.False);
            Assert.That(T.IsRangedHealerClass((int)eCharacterClass.Cleric), Is.True);
        }
    }
}
