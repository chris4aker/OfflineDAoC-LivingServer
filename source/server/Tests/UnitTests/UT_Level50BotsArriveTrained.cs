using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// A bot the launcher makes at level 50 ("+ Lv.50") arrives trained in its lifetime build (owner 2026-11-11: "why
    /// can't the bots load like that? Sword and shield armsman, 2H paladin, ice wizard, fire wizard"). PC, same day,
    /// build e90bf21: 2,700 such bots loaded with every line at 1 and trained only once each had walked to a trainer.
    /// The rule that decides it is pure; the spending is the trainer's own (GameBot.SpendSpecPoints).
    /// </summary>
    [TestFixture]
    public class UT_Level50BotsArriveTrained
    {
        [Test]
        public void ABotMadeAtFiftyThatNeverTrainedArrivesTrained()
        {
            Assert.That(GameBot.ArrivesTrained(50, 1), Is.True, "made at 50 by the launcher, never at a trainer");
            Assert.That(GameBot.ArrivesTrained(50, 0), Is.True);
        }

        [Test]
        public void ABotThatLeveledUpKeepsVisitingItsTrainer()
        {
            Assert.That(GameBot.ArrivesTrained(1, 1), Is.False, "a new level-1 bot");
            Assert.That(GameBot.ArrivesTrained(49, 1), Is.False, "the launcher makes level 1 and level 50 only");
            Assert.That(GameBot.ArrivesTrained(50, 48), Is.False, "it trained on its way and owes two levels: its trainer's");
            Assert.That(GameBot.ArrivesTrained(50, 50), Is.False, "already trained");
        }
    }
}
