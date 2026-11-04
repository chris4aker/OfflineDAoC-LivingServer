using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // A data quest's level window decides who is offered the quest (and a Collection quest's turn-ins, which are
    // offers every time). Owner 2026-10-30: it no longer gates the hand-in steps of a quest the player already holds,
    // so a player who outlevelled a delivery quest can still finish it.
    [TestFixture]
    public sealed class UT_QuestLevelWindow
    {
        [Test]
        public void TheWindowIsInclusiveAtBothEnds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(QuestLevelWindow.Offers(5, 5, 10), Is.True);
                Assert.That(QuestLevelWindow.Offers(7, 5, 10), Is.True);
                Assert.That(QuestLevelWindow.Offers(10, 5, 10), Is.True);
            });
        }

        [Test]
        public void APlayerBelowTheMinimumIsNotOffered()
            => Assert.That(QuestLevelWindow.Offers(4, 5, 10), Is.False);

        [Test]
        public void APlayerAboveTheMaximumIsNotOffered()
            => Assert.That(QuestLevelWindow.Offers(11, 5, 10), Is.False);

        [Test]
        public void AOneLevelWindowOffersThatLevelOnly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(QuestLevelWindow.Offers(3, 3, 3), Is.True);
                Assert.That(QuestLevelWindow.Offers(2, 3, 3), Is.False);
                Assert.That(QuestLevelWindow.Offers(4, 3, 3), Is.False);
            });
        }
    }
}
