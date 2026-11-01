using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Companions hide with their player by the player's own rule (StealthSpecHandler's "too close" circle).</summary>
    [TestFixture]
    public sealed class UT_CompanionStealth
    {
        [Test]
        public void TheHideCircleMatchesThePlayersRule() => Assert.Multiple(() =>
        {
            Assert.That(CompanionStealth.HideRadius(50, 50, false), Is.EqualTo(128), "an even-level enemy, Stealth 50");
            Assert.That(CompanionStealth.HideRadius(50, 25, false), Is.EqualTo(576));
            Assert.That(CompanionStealth.HideRadius(50, 50, true), Is.EqualTo(256), "Detect Hidden doubles the circle");
            Assert.That(CompanionStealth.HideRadius(20, 50, false), Is.LessThanOrEqualTo(0), "hides right under a low enemy's nose");
            Assert.That(CompanionStealth.HideRadius(50, 80, false), Is.EqualTo(128), "Stealth counts up to 50");
        });

        [Test]
        public void NoCompanionNoStealthLeader()
        {
            Assert.That(CompanionStealth.StealthedLeader(null), Is.Null);
            Assert.That(CompanionStealth.HoldsStealth(null), Is.False);
            Assert.That(CompanionStealth.SuppressesCasting(null), Is.False);
        }
    }
}
