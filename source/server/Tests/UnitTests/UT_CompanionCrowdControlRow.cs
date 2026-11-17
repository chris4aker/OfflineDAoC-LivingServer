using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 / G7 (2026-11-11): the "Crowd control" AI row - Automatic, Main CC, Adds only, Off.</summary>
    [TestFixture]
    public class UT_CompanionCrowdControlRow
    {
        [Test]
        public void OffControlsNothing_AddsOnlyNeverTheKillTarget() => Assert.Multiple(() =>
        {
            Assert.That(CcAllowsAddControl(CcOff), Is.False);
            Assert.That(CcAllowsKillTargetControl(CcOff), Is.False);
            Assert.That(CcAllowsAddControl(CcAddsOnly), Is.True);
            Assert.That(CcAllowsKillTargetControl(CcAddsOnly), Is.False, "no opening stun on the kill target");
            Assert.That(CcAllowsAddControl(CcAutomatic), Is.True);
            Assert.That(CcAllowsKillTargetControl(CcAutomatic), Is.True, "Automatic is unchanged");
        });

        [Test]
        public void MainCcSpendsItsLongReuseOnAnyAddAndTheOthersLeaveItTheAdds() => Assert.Multiple(() =>
        {
            Assert.That(CcLongReuseOnAnyAdd(CcMain), Is.True);
            Assert.That(CcLongReuseOnAnyAdd(CcAutomatic), Is.False);
            Assert.That(DefersToMainCc(CcAutomatic, true), Is.True);
            Assert.That(DefersToMainCc(CcAutomatic, false), Is.False, "no main mezzer able to act");
            Assert.That(DefersToMainCc(CcAddsOnly, true), Is.False, "an explicit choice keeps mezzing");
            Assert.That(DefersToMainCc(CcMain, true), Is.False);
        });

        [Test]
        public void GamebotsReadAutomatic() => Assert.Multiple(() =>
        {
            Assert.That(CrowdControl(null), Is.EqualTo(CcAutomatic));
            Assert.That(MainCcReady(null), Is.False);
            var row = CompanionOptions165.Option(CompanionOptions165.KindCrowdControl);
            Assert.That(row?.Key, Is.EqualTo("cc"));
            // Living Server: Automatic and Off (stored as True 1.65's 3)
            Assert.That(row?.Choices.Length, Is.EqualTo(2));
            Assert.That(CompanionOptions165.StoredValue(CompanionOptions165.KindCrowdControl, 1), Is.EqualTo(CcOff));
        });
    }
}
