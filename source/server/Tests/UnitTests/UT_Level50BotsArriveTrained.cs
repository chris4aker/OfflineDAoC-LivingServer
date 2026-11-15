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
            Assert.That(GameBot.ArrivesTrained(49, 1), Is.False, "saved, or not known to be the launcher's");
            Assert.That(GameBot.ArrivesTrained(50, 48), Is.False, "it trained on its way and owes two levels: its trainer's");
            Assert.That(GameBot.ArrivesTrained(50, 50), Is.False, "already trained");
            Assert.That(GameBot.ArrivesTrained(22, 1, neverSaved: false), Is.False, "saved at 22: it leveled there by playing");
        }

        /// <summary>
        /// "+ Lv.20" (Living Server 1.2; owner's PC, build 1dc66b6, 2026-11-15: every bot level 50, every battleground empty)
        /// makes never-saved bots at levels 20-24 (any level above 1 qualifies); they arrive trained like "+ Lv.50" bots, not with every line at 1.
        /// </summary>
        [TestCase(15)]
        [TestCase(22)]
        [TestCase(35)]
        public void ABotTheLauncherMadeForABattlegroundArrivesTrained(int level)
        {
            Assert.That(GameBot.ArrivesTrained(level, 1, neverSaved: true), Is.True);
            Assert.That(GameBot.ArrivesTrained(level, level, neverSaved: true), Is.False, "already trained");
        }

        [Test]
        public void ANewLevelOneBotIsNotTrainedOnArrival()
        {
            Assert.That(GameBot.ArrivesTrained(1, 1, neverSaved: true), Is.False, "the launcher's \"+ Lv.1\": nothing to train");
        }
    }
}
