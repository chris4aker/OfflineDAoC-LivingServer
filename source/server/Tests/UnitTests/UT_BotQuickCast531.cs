using DOL.GS;
using DOL.GS.SkillHandler;
using NUnit.Framework;
using static DOL.GS.BotQuickCast;

namespace DOL.UnitTests
{
    /// <summary>
    /// #531 step 2d: an open-field gamebot uses Quickcast on one key cast (a mez or its big heal), only when an enemy's
    /// interrupt would cost that cast, with the player's rules (not in melee, double power, 30 s between uses). The
    /// wiring is shown by the #520 rehearsal row (qc=). Tests only.
    /// </summary>
    [TestFixture]
    public class UT_BotQuickCast531
    {
        private const long Ready = QuickCastAbilityHandler.DISABLE_DURATION;

        [TestCase(true, false, ExpectedResult = true, TestName = "a mez is a key cast")]
        [TestCase(false, true, ExpectedResult = true, TestName = "the big heal is a key cast")]
        [TestCase(false, false, ExpectedResult = false, TestName = "a nuke or a small heal is not")]
        public bool KeyCasts(bool mez, bool bigHeal) => IsKeyCast(mez, bigHeal);

        [Test]
        public void QuickcastCarriesOnlyAKeyCastAnInterruptWouldCost()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Carries(true, true, true, true, false, Ready, 100, 40), Is.True, "a mez under interrupt");
                Assert.That(Carries(true, true, true, false, false, Ready, 100, 40), Is.False, "no interrupt: cast it normally");
                Assert.That(Carries(true, false, true, true, false, Ready, 100, 40), Is.False, "not a key cast");
                Assert.That(Carries(true, true, false, true, false, Ready, 100, 40), Is.False, "an instant needs no Quickcast");
                Assert.That(Carries(false, true, true, true, false, Ready, 100, 40), Is.False, "a companion, or a bot outside the open field");
                Assert.That(Carries(true, true, true, true, true, Ready, 100, 40), Is.False, "not while swinging (the player's rule)");
            });
        }

        [Test]
        public void QuickcastWaitsThirtySecondsAndNeedsDoublePower()
        {
            Assert.That(QuickCastAbilityHandler.DISABLE_DURATION, Is.EqualTo(30_000), "the player's wait");
            Assert.Multiple(() =>
            {
                Assert.That(Carries(true, true, true, true, false, Ready - 1, 100, 40), Is.False, "29.999 s after the last");
                Assert.That(Carries(true, true, true, true, false, Ready, 100, 40), Is.True, "30 s after the last");
                Assert.That(Carries(true, true, true, true, false, long.MaxValue, 100, 40), Is.True, "never used");
                Assert.That(Carries(true, true, true, true, false, Ready, 39, 40), Is.False, "not enough for double power");
                Assert.That(Carries(true, true, true, true, false, Ready, 40, 40), Is.True, "just enough");
            });
        }
    }
}
