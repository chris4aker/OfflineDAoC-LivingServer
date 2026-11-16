using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-05: the AI Options "option" verb was not routed, so every generic row answered "Unknown hiring command".
    [TestFixture]
    public class UT_CompanionOptionsRouting
    {
        [Test]
        public void EveryAiOptionsVerbReachesTheCompanionWindows()
        {
            foreach (string verb in CompanionOptions165.Verbs)
                Assert.That(CompanionWindows165.Verbs, Does.Contain(verb), verb);
            Assert.That(CompanionWindows165.Verbs, Does.Contain("option"));
        }

        // Living Server (2026-11-16): the rows its AI reads are offered; the other True 1.65 kinds are not (reserved)
        [Test]
        public void EveryGenericOptionKindIsKnown()
        {
            byte[] offered =
            [
                CompanionOptions165.KindDebuff, CompanionOptions165.KindBolts, CompanionOptions165.KindHunterPet,
                CompanionOptions165.KindHealerRole, CompanionOptions165.KindShamanRole, CompanionOptions165.KindFriarRole,
                CompanionOptions165.KindLifeTransfer, CompanionOptions165.KindAmnesia, CompanionOptions165.KindSavePower,
                CompanionOptions165.KindSavePowerCaster, CompanionOptions165.KindAttackSpells, CompanionOptions165.KindStance,
                CompanionOptions165.KindCrowdControl,
            ];
            for (byte kind = 5; kind <= 60; kind++)
                Assert.That(CompanionOptions165.Option(kind) != null, Is.EqualTo(System.Array.IndexOf(offered, kind) >= 0), $"kind {kind}");
        }

        // owner 2026-11-06: charming starts off for a hired Hunter only (it keeps its wolf); on for every other charm class
        [Test]
        public void CharmDefaultsOffForHuntersOnly() => Assert.Multiple(() =>
        {
            Assert.That(CompanionOptions165.CharmAllowed(null, true), Is.False, "a Hunter never set: off (its wolf)");
            Assert.That(CompanionOptions165.CharmAllowed("1", true), Is.True, "a Hunter switched on");
            Assert.That(CompanionOptions165.CharmAllowed("0", true), Is.False);
            Assert.That(CompanionOptions165.CharmAllowed(null, false), Is.True, "a Sorcerer never set: on");
            Assert.That(CompanionOptions165.CharmAllowed("0", false), Is.False, "switched off");
            Assert.That(CompanionOptions165.CharmAllowed("1", false), Is.True);
        });

        // owner 2026-11-06: a companion's realm ability tooltip ends with its own level and the next cost
        [Test]
        public void RealmAbilityDelveStatusLine()
        {
            Assert.That(CompanionWindows165.RealmAbilityStatus("Isolde", 1, 3, 3, 5),
                Is.EqualTo("Isolde has it at level 1 of 3. Level 2 costs 3 realm ability points; Isolde has 5."));
            Assert.That(CompanionWindows165.RealmAbilityStatus("Isolde", 0, 5, 1, 0),
                Does.Contain("level 0 of 5").And.Contain("Level 1 costs 1"));
            Assert.That(CompanionWindows165.RealmAbilityStatus("Isolde", 3, 3, 0, 9),
                Is.EqualTo("Isolde has it at level 3 of 3 (the highest)."));
        }
    }
}
