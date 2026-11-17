using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 (2026-11-11): a companion may have a stance of its own ("tank aggressive, healer
    /// passive"); Automatic follows the player's /aggressive, /defensive or /passive.</summary>
    [TestFixture]
    public class UT_CompanionOwnStance
    {
        [Test]
        public void ACompanionsOwnStanceWinsOverTheGroups() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.OwnStance(0), Is.Null, "Automatic: the group's stance");
            Assert.That(CompanionControls165.EffectiveStance(0, CompanionStance.Defensive), Is.EqualTo(CompanionStance.Defensive));
            Assert.That(CompanionControls165.EffectiveStance(CompanionControls165.StancePassive, CompanionStance.Aggressive),
                Is.EqualTo(CompanionStance.Passive), "healer passive while the group fights");
            Assert.That(CompanionControls165.EffectiveStance(CompanionControls165.StanceAggressive, CompanionStance.Passive),
                Is.EqualTo(CompanionStance.Aggressive));
            Assert.That(CompanionControls165.EffectiveStance(CompanionControls165.StanceDefensive, CompanionStance.Aggressive),
                Is.EqualTo(CompanionStance.Defensive));
        });

        [Test]
        public void TheRowOffersTheThreeStancesAfterAutomatic()
        {
            // Living Server has no /passive: Automatic, Aggressive and Defensive (stored 0, 1, 2 as in True 1.65)
            var row = CompanionOptions165.Option(CompanionOptions165.KindStance);
            Assert.That(row?.Key, Is.EqualTo("stance"));
            Assert.That(row?.Choices, Is.EqualTo(new[] { "Automatic (the group's stance)", "Aggressive", "Defensive" }));
        }
    }
}
