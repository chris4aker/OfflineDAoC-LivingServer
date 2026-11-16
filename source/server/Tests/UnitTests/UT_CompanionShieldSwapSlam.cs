using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 / G10 (2026-11-11): a two-hander tank swaps to its weapon and shield for one Slam when a
    /// stun is wanted, then back.</summary>
    [TestFixture]
    public class UT_CompanionShieldSwapSlam
    {
        [Test]
        public void SwapsOnlyWhenEverythingIsReadyAndAStunIsWanted() => Assert.Multiple(() =>
        {
            Assert.That(SlamSwapWanted(0, true, true, true, true, true, false), Is.True);
            Assert.That(SlamSwapWanted(AbilityOff, true, true, true, true, true, false), Is.False, "row off");
            Assert.That(SlamSwapWanted(0, false, true, true, true, true, false), Is.False, "not a tank");
            Assert.That(SlamSwapWanted(0, true, false, true, true, true, false), Is.False, "already on the shield");
            Assert.That(SlamSwapWanted(0, true, true, false, true, true, false), Is.False, "no shield set");
            Assert.That(SlamSwapWanted(0, true, true, true, false, true, false), Is.False, "no usable Slam");
            Assert.That(SlamSwapWanted(0, true, true, true, true, false, false), Is.False, "no stun wanted (not a filler)");
            Assert.That(SlamSwapWanted(0, true, true, true, true, true, true), Is.False, "stun immune");
        });

        [Test]
        public void SwapsBackOnceTheSlamIsDone() => Assert.Multiple(() =>
        {
            Assert.That(SlamSwapEnds(true, false, 100), Is.True, "landed");
            Assert.That(SlamSwapEnds(false, true, 100), Is.True, "swung");
            Assert.That(SlamSwapEnds(false, false, SlamSwapMilliseconds), Is.True, "timed out");
            Assert.That(SlamSwapEnds(false, false, 1_000), Is.False);
        });
    }
}
