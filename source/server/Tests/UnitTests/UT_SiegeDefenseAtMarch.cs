using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.1.3 (owner's PC, RC nights 2026-11-08/09): 12 of 18 bot sieges captured, 8 of them in 4-11 minutes of battle, because
    /// the keep's realm was called only when the battle began and its first defender arrived 3-14 minutes later, or never.
    /// Every siege with defenders at the keep within two minutes of the start was held. Scouts now see the army march.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_SiegeDefenseAtMarch
    {
        [SetUp]
        public void SetUp()
        {
            RvrEventTestState.Clear();
            AutonomousRvrEventLayer.StagedAssault = true;
            AutonomousRvrEventLayer.SiegeScheduling = true;
        }

        [TearDown]
        public void TearDown()
        {
            RvrEventTestState.Clear();
            AutonomousRvrEventLayer.StagedAssault = true;
            AutonomousRvrEventLayer.SiegeScheduling = true;
        }

        [TestCase(true, false, false, ExpectedResult = true, TestName = "A bot siege calls its defenders when the army marches")]
        [TestCase(true, true, false, ExpectedResult = false, TestName = "A player's attack keeps its own immediate defense")]
        [TestCase(true, false, true, ExpectedResult = false, TestName = "The losing realm's retake keeps its rules")]
        [TestCase(false, false, false, ExpectedResult = false, TestName = "The legacy unscheduled sieges are unchanged")]
        public bool WhoIsCalledAtTheMarch(bool scheduling, bool defenseReaction, bool retake) =>
            RealmEventPolicy.DefendersCalledAtMarch(scheduling, defenseReaction, retake);

        [Test]
        public void DefendersGatherWhileTheArmyIsStillOnTheRoad()
        {
            string id = Guid.NewGuid().ToString();
            long opened = GameLoop.GameLoopTime;
            var keep = new AutonomousRvrEventLayer.LiveObjective(id, "Scouted keep",
                AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Hibernia, 200, 100, 100, 0, false, 0, 0, 5, 2);
            var roam = new AutonomousRvrEventLayer.LiveObjective(id + "-roam", "frontier patrol",
                AutonomousRvrEventLayer.Intent.Roam, eRealm.None, 200, 5000, 5000, 0, false, 0, 0, 0, 0);
            AutonomousRvrEventLayer.LiveObjective[] objectives = [keep, roam];
            var attacker = new AutonomousRvrEventLayer.Force(id + "-attack", eRealm.Albion, 8, 50, 1);
            var defender = new AutonomousRvrEventLayer.Force(id + "-defend", eRealm.Hibernia, 8, 50, 1);
            var third = new AutonomousRvrEventLayer.Force(id + "-third", eRealm.Midgard, 8, 50, 1);

            Assert.That(AutonomousRvrEventLayer.ForceStart(keep, eRealm.Albion, opened, out _), Is.True);
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(attacker, objectives, opened, 0)?.TargetId, Is.EqualTo(id));

            // While the attackers muster at their own border keep nobody in Hibernia knows yet.
            var before = AutonomousRvrEventLayer.ChooseOrJoin(defender, objectives, opened + 1, 0);
            Assert.That(before?.TargetId, Is.Not.EqualTo(id), "no defense before the army sets out");

            // The muster runs out (once extended for a thin army) and the army marches.
            long firstDeadline = opened + AutonomousRvrEventLayer.HomeMusterMilliseconds + 1;
            AutonomousRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, firstDeadline);
            long marched = firstDeadline + AutonomousRvrEventLayer.MusterExtensionMilliseconds + 1;
            Assert.That(AutonomousRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, marched)?.HomeMuster, Is.False,
                "the army is on its way");

            var plan = AutonomousRvrEventLayer.ChooseOrJoin(defender, objectives, marched + 1, 0);
            var defenseOrder = AutonomousRvrEventLayer.GetRallyOrder(defender.GroupId, eRealm.Hibernia, marched + 2);
            var thirdPlan = AutonomousRvrEventLayer.ChooseOrJoin(third, objectives, marched + 3, 0);
            Assert.Multiple(() =>
            {
                Assert.That(plan?.TargetId, Is.EqualTo(id), "scouts saw the army: the keep's realm answers before the battle");
                Assert.That(plan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent));
                Assert.That(defenseOrder?.HomeMuster, Is.True, "the defenders gather first and go in together");
                Assert.That(AutonomousRvrEventLayer.IsBattleForce(defender.GroupId, marched + 2), Is.False, "no battle yet");
                Assert.That(thirdPlan?.TargetId, Is.Not.EqualTo(id), "the third realm still joins only once the battle is on");
            });
        }
    }
}
