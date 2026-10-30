using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // /allrelease and the way back after a release (BotGroupRelease): a portal keep only for a gamebot going back to a
    // player's group and under the battleground's level cap, a gamebot joins only the living player who leads it, after
    // the wait, and what the player is told.
    [TestFixture]
    public class UT_BotGroupRelease
    {
        [TestCase(true, 22, 24, true)]
        [TestCase(true, 24, 24, true)]
        [TestCase(true, 25, 24, false)]
        [TestCase(false, 22, 24, false)]
        public void PortalKeep_OnlyInABattlegroundUnderItsCap(bool battleground, int level, int maxLevel, bool expected) =>
            Assert.That(BotGroupRelease.PortalKeepApplies(true, true, battleground, level, maxLevel), Is.EqualTo(expected));

        [TestCase(false, true, TestName = "Solo gamebot or bot-only group")]
        [TestCase(true, false, TestName = "Not going back to the party")]
        [TestCase(false, false, TestName = "Neither")]
        public void PortalKeep_OnlyOnTheWayBackToAPlayersGroup(bool playerLed, bool returnToParty) =>
            Assert.That(BotGroupRelease.PortalKeepApplies(playerLed, returnToParty, true, 22, 24), Is.False);

        [TestCase(true, false, true, true, true, true, true)]
        [TestCase(false, false, true, true, true, true, false)]
        [TestCase(true, true, true, true, true, true, false)]
        [TestCase(true, false, false, true, true, true, false)]
        [TestCase(true, false, true, false, true, true, false)]
        [TestCase(true, false, true, true, false, true, false)]
        [TestCase(true, false, true, true, true, false, false)]
        public void JoinsLeader_OnlyAGamebotOfTheLivingLeadersGroup(bool autonomous, bool temporary, bool sameGroup,
            bool sameRealm, bool leaderAlive, bool leaderActive, bool expected) =>
            Assert.That(BotGroupRelease.CanJoinLeader(autonomous, temporary, sameGroup, sameRealm, leaderAlive, leaderActive),
                Is.EqualTo(expected));

        [Test]
        public void ReturnWait_EndsAfterTheDelay()
        {
            Assert.That(BotGroupRelease.ReturnDelayOver(1_000, 1_000 + BotGroupRelease.ReturnToLeaderDelayMilliseconds - 1), Is.False);
            Assert.That(BotGroupRelease.ReturnDelayOver(1_000, 1_000 + BotGroupRelease.ReturnToLeaderDelayMilliseconds), Is.True);
        }

        [Test]
        public void Report_NoDeadBots()
        {
            string[] lines = BotGroupRelease.Report([], []).ToArray();
            Assert.That(lines, Is.EqualTo(new[] { "There are no dead bots in your group." }));
        }

        [Test]
        public void Report_NamesReleasedAndWaiting()
        {
            string[] lines = BotGroupRelease.Report(["Knutiunvald", "Dagny"], ["Helper"]).ToArray();
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo("Released: Knutiunvald, Dagny."));
            Assert.That(lines[1], Does.StartWith("Could not release yet: Helper."));
        }

        [Test]
        public void Report_OnlyReleased_SaysNothingAboutWaiting() =>
            Assert.That(BotGroupRelease.Report(["Knutiunvald"], []).Single(), Is.EqualTo("Released: Knutiunvald."));
    }
}
