using System.Reflection;
using DOL.GS;
using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.1 (#424): the log line a click on a bot leaves (PlayerTargetHandler.ClickLogLine), so a played check in the
    /// original client can tell "the client never sent the click" from "the server didn't take it". Automated only; the
    /// click itself needs the owner's played check.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PlayerClickTargetLog : DutyLeaseFixture
    {
        [Test]
        public void AClickOnALivingGamebotIsLoggedAsSet()
        {
            Bot bot = Gamebot(20);
            // Not the Name setter: an uninitialized bot reads as Active and the setter would send it to clients.
            typeof(GameObject).GetField("m_name", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bot, "Brannoc");
            string line = PlayerTargetHandler.ClickLogLine("Testrunner", 1234, bot, inRange: true, inView: true, examine: false);
            Assert.Multiple(() =>
            {
                Assert.That(line, Does.StartWith("PLAYER_CLICK_TARGET player=\"Testrunner\" id=1234 kind=gamebot name=\"Brannoc\" result=set"));
                Assert.That(line, Does.Contain("alive=True"));
                Assert.That(line, Does.Contain("inView=True"));
            });
        }

        [Test]
        public void ACompanionADeadBotAndAFarBotSayWhatTheyAre()
        {
            Bot companion = Companion();
            Bot dead = Gamebot(20);
            dead.Alive = false;
            Assert.Multiple(() =>
            {
                Assert.That(PlayerTargetHandler.ClickLogLine("P", 7, companion, true, true, false), Does.Contain("kind=companion"));
                Assert.That(PlayerTargetHandler.ClickLogLine("P", 7, dead, true, false, false), Does.Contain("alive=False"));
                Assert.That(PlayerTargetHandler.ClickLogLine("P", 7, dead, false, true, false), Does.Contain("result=out_of_range"));
            });
        }

        [Test]
        public void AnUnknownIdIsLoggedAndNothingElseIs()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PlayerTargetHandler.ClickLogLine("P", 99, null, false, true, false),
                    Does.Contain("kind=unknown_id").And.Contain("result=no_object"));
                Assert.That(PlayerTargetHandler.ClickLogLine("P", 0, null, true, false, false), Is.Null, "clearing the target");
                Assert.That(PlayerTargetHandler.ClickLogLine("P", 0, Gamebot(20), true, false, false), Is.Null);
            });
        }
    }
}
