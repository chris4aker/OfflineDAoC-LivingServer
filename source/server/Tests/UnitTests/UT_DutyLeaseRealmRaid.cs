using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Raid reservations are duty leases (docs/DUTY-LEASE.md, migration 2): a planned recruit (and an Epic Special Force,
    /// for its whole raid) holds raid_reserved, a formed party member raid_party. "Reserved" keeps its old meaning, the
    /// reserved stage only, so raid planning, siege parties, matchmaking and the invite decide as before
    /// (UT_DutyLeaseCurrentRules).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DutyLeaseRealmRaid : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        [Test]
        public void AReservationIsARealmRaidLeaseForThatRaid()
        {
            Bot force = SpecialForce();
            AutonomousRealmRaid.ReserveSpecialForce(force, "dragon-test");
            BotDutyLease lease = BotDutyLeases.Current(force);
            Assert.Multiple(() =>
            {
                Assert.That(lease.Kind, Is.EqualTo(Kind.RealmRaid));
                Assert.That(lease.Owner, Is.EqualTo("dragon-test"));
                Assert.That(lease.Reason, Is.EqualTo(DutyLeaseReason.RaidReserved));
                Assert.That(AutonomousRealmRaid.IsReservedFor(force, "dragon-test"), Is.True);
                Assert.That(AutonomousRealmRaid.IsReservedFor(force, null), Is.False);
                Assert.That(BotGroupInvite.DutyOf(force), Is.EqualTo(BotGroupInvite.Duty.RealmRaid));
            });
        }

        [Test]
        public void APartyMemberIsTheRaidsButNotReserved()
        {
            Bot member = Gamebot();
            Group party = Token();
            member.Group = party;
            BotDutyLeases.Claim(member, Kind.RealmRaid, "dragon-test", DutyLeaseReason.RaidParty, group: party);
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.KindOf(member), Is.EqualTo(Kind.RealmRaid));
                Assert.That(AutonomousRealmRaid.IsReserved(member), Is.False, "the old map dropped a recruit once its party formed");
                Assert.That(AutonomousRealmRaid.IsReservedFor(member, "dragon-test"), Is.False);
                Assert.That(BotGroupInvite.DutyOf(member), Is.EqualTo(BotGroupInvite.Duty.None), "as before: the invite only refused recruits");
            });
            member.Group = null;
            Assert.That(BotDutyLeases.Current(member), Is.Null, "leaving the party's group ends the party lease");
        }

        [Test]
        public void AReservationEndsWhenTheBotLogsOut()
        {
            // The one intended difference: the old map was keyed by bot id, so a recruit that logged out stayed reserved and,
            // logged back in, was kept out of raids, siege parties and group matchmaking until that raid ended.
            Bot force = SpecialForce();
            AutonomousRealmRaid.ReserveSpecialForce(force, "dragon-test");
            AutonomousBotRegistry.Unregister(force);
            Assert.That(AutonomousRealmRaid.IsReserved(force), Is.False);
        }

        [Test]
        public void OnlyTheSpecialForcesAreReservedFromOutsideARaid()
        {
            Bot ordinary = Gamebot();
            AutonomousRealmRaid.ReserveSpecialForce(ordinary, "dragon-test");
            Assert.That(BotDutyLeases.Current(ordinary), Is.Null);
        }
    }
}
