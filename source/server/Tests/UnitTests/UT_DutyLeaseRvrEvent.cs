using DOL.GS;
using DOL.GS.Tests;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Siege and RvR events (docs/DUTY-LEASE.md, migration 3): a defense response and a siege party claim their members
    /// with an RvR-event lease for their force; it ends when the force leaves (RemoveForce) or its event ends. The
    /// invite's "marching with a realm force" duty reads the lease while the force is in a running event.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DutyLeaseRvrEvent : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh()
        {
            BotDutyLeases.Clear();
            RvrEventTestState.Clear();
        }

        [TearDown]
        public void Forget()
        {
            BotDutyLeases.Clear();
            RvrEventTestState.Clear();
        }

        private static void StartEventWith(string forceId)
        {
            AutonomousRvrEventLayer.StagedAssault = true;
            var keep = new AutonomousRvrEventLayer.LiveObjective("keep-lease", "Dun Crauchon", AutonomousRvrEventLayer.Intent.AssaultKeep,
                eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
            AutonomousRvrEventLayer.ChooseOrJoin(new AutonomousRvrEventLayer.Force(forceId, eRealm.Midgard, 8, 50, 2), new[] { keep }, 1, 0);
            Assert.That(AutonomousRvrEventLayer.IsEventForce(forceId, 0), Is.True);
        }

        [Test]
        public void AMarcherIsBusyWhileItsForceIsInARunningEvent()
        {
            StartEventWith("warband-1");
            Bot marcher = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "warband-1", DutyLeaseReason.RvrForce, "Dun Crauchon");
            Assert.That(BotGroupInvite.DutyOf(marcher), Is.EqualTo(BotGroupInvite.Duty.RvrEvent));
            AutonomousRvrEventLayer.RemoveForce("warband-1");
            Assert.That(BotDutyLeases.Current(marcher), Is.Null, "the force left: its lease ended");
            Assert.That(BotGroupInvite.DutyOf(marcher), Is.EqualTo(BotGroupInvite.Duty.None));
        }

        [Test]
        public void ALeaseForAForceOutsideAnyEventIsNotADuty()
        {
            Bot bot = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(bot, Kind.RvrEvent, "nowhere", DutyLeaseReason.RvrForce);
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(BotGroupInvite.Duty.None));
        }

        [Test]
        public void TheForceTagStillCountsForASoloForceWithoutALease()
        {
            StartEventWith("rvr-solo");
            Bot solo = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            solo.TempProperties.SetProperty("RvrEventForce", "rvr-solo");
            Assert.That(BotGroupInvite.DutyOf(solo), Is.EqualTo(BotGroupInvite.Duty.RvrEvent));
        }

        [Test]
        public void ARaidTakesAMarcherButAMarchNeverTakesARaider()
        {
            Bot marcher = Gamebot(), raider = Gamebot();
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "warband-2", DutyLeaseReason.RvrForce);
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            Assert.That(BotDutyLeases.Claim(marcher, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved).Outcome,
                Is.EqualTo(BotDutyLeaseRules.Outcome.Taken));
            Assert.That(BotDutyLeases.Claim(raider, Kind.RvrEvent, "warband-2", DutyLeaseReason.RvrForce).Reason,
                Is.EqualTo(DutyLeaseReason.OnRealmRaid));
        }
    }
}
