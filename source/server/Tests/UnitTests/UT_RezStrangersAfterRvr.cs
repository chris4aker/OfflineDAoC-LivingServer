using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.Tests;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Schedule = DOL.GS.AutonomousBotSchedule;

namespace DOL.UnitTests
{
    /// <summary>
    /// A passing gamebot healer rezzes a dead player outside its group (bot_living_death). 1.2 play-check prep
    /// (2026-11-15): the rule read "has an RvR force id" as "busy", but every RvR gamebot keeps "rvr-&lt;id&gt;" once it has
    /// run its RvR goal and it is never cleared, so no bot that had ever done RvR (every bot in a battleground or the
    /// frontier) would rez a stranger. Only a force in a running realm event counts now, as for the schedule (1.1.3).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RezStrangersAfterRvr : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh() => RvrEventTestState.Clear();

        [TearDown]
        public void Forget() => RvrEventTestState.Clear();

        [Test]
        public void TheRuleBlocksOnlyRealDuties()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.FreeToRezStrangers(false, false, false, false, false, false), Is.True);
                Assert.That(BotBrain.FreeToRezStrangers(true, false, false, false, false, false), Is.False, "on a stable route");
                Assert.That(BotBrain.FreeToRezStrangers(false, true, false, false, false, false), Is.False, "running back after a death");
                Assert.That(BotBrain.FreeToRezStrangers(false, false, true, false, false, false), Is.False, "at a siege");
                Assert.That(BotBrain.FreeToRezStrangers(false, false, false, true, false, false), Is.False, "holding a siege post");
                Assert.That(BotBrain.FreeToRezStrangers(false, false, false, false, true, false), Is.False, "carrying a relic");
                Assert.That(BotBrain.FreeToRezStrangers(false, false, false, false, false, true), Is.False, "in a realm event");
            });
        }

        [Test]
        public void ABotThatOnceRanItsRvrGoalIsNotInARealmEvent()
        {
            Bot healer = Gamebot(level: 22, objective: "RvR");
            healer.TempProperties.SetProperty("RvrEventForce", $"rvr-{healer.DatabaseID}");
            Assert.That(Schedule.InRealmEvent(healer), Is.False, "the force id alone commits it to nothing");
        }

        [Test]
        public void ABotWhoseForceIsInARunningEventIsBusy()
        {
            AutonomousRvrEventLayer.StagedAssault = true;
            var keep = new AutonomousRvrEventLayer.LiveObjective("keep-rez", "Dun Crauchon", AutonomousRvrEventLayer.Intent.AssaultKeep,
                eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
            AutonomousRvrEventLayer.ChooseOrJoin(new AutonomousRvrEventLayer.Force("warband-rez", eRealm.Midgard, 8, 50, 2), new[] { keep }, 1, 0);
            Bot marcher = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            marcher.TempProperties.SetProperty("RvrEventForce", "warband-rez");
            Assert.That(Schedule.InRealmEvent(marcher), Is.True);
        }
    }
}
