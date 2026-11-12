using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>df-bosses: damage dealers fight a boss that parries or evades this well from behind.</summary>
    [TestFixture]
    public class UT_BotFlankDefensiveBoss
    {
        [TestCase(70, 0, true)]    // Prince Abdin
        [TestCase(0, 50, true)]    // Summoner Cunovinda
        [TestCase(80, 0, true)]    // Warlord Dorinakka
        [TestCase(10, 10, false)]  // an ordinary monster: only bots with back or side styles flank, as before
        public void FightsFromBehind(int parry, int evade, bool flank) =>
            Assert.That(BotBrain.DefendsFromTheFront(parry, evade), Is.EqualTo(flank));
    }
}
