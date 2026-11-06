using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot group sessions (docs/DUTY-LEASE.md, migration 5): a bot-only party's members hold a bot-group lease bound to
    /// the party's group; a raid's or siege's own party leaves its members on that activity's higher lease.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DutyLeaseGroupSession : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        private static object Session(Group group, string id)
        {
            Type type = typeof(AutonomousBotGroupCoordinator).GetNestedType("Session", BindingFlags.NonPublic)!;
            object session = RuntimeHelpers.GetUninitializedObject(type);
            type.GetProperty("Group")!.SetValue(session, group);
            type.GetProperty("Id")!.SetValue(session, id);
            type.GetProperty("ObjectiveKind")!.SetValue(session, eAutonomousObjectiveKind.GroupPve);
            return session;
        }

        private static void Claim(object session, params GameBot[] members) =>
            typeof(AutonomousBotGroupCoordinator).GetMethod("ClaimSessionLeases", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new[] { session, members });

        [Test]
        public void APartysMembersHoldItsLeaseWhileTheyAreInIt()
        {
            Group group = Token();
            Bot tank = Gamebot(), healer = Gamebot(), stranger = Gamebot();
            tank.Group = healer.Group = group;
            stranger.Group = Token();
            Claim(Session(group, "g-21"), tank, healer, stranger);
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.Holds(tank, Kind.BotGroupSession, "g-21"), Is.True);
                Assert.That(BotDutyLeases.Holds(healer, Kind.BotGroupSession, "g-21"), Is.True);
                Assert.That(BotDutyLeases.Current(stranger), Is.Null, "not in the party's group");
                Assert.That(BotDutyLeases.Current(tank).OwnerName, Is.EqualTo("GroupPve party"));
            });
            healer.Group = null;
            Assert.That(BotDutyLeases.Current(healer), Is.Null, "a member who leaves is free");
            Assert.That(BotDutyLeases.Holds(tank, Kind.BotGroupSession, "g-21"), Is.True);
        }

        [Test]
        public void ARaidsOwnPartyKeepsItsMembersOnTheRaidLease()
        {
            Group party = Token();
            Bot raider = Gamebot();
            raider.Group = party;
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidParty, group: party);
            Claim(Session(party, "g-22"), raider);
            Assert.That(BotDutyLeases.KindOf(raider), Is.EqualTo(Kind.RealmRaid));
            Assert.That(BotDutyLeases.ForBot(raider.DatabaseID, null, 5), Has.Count.EqualTo(1), "nothing refused: it is the same party");
        }
    }
}
