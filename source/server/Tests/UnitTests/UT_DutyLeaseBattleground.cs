using System;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;
using KeepRally = DOL.GS.AutonomousBattlegroundKeepRally;

namespace DOL.UnitTests
{
    /// <summary>
    /// The battleground keep rally (docs/DUTY-LEASE.md, migration 4): a gamebot that goes up with its realm's rally holds
    /// a battleground-wave lease for that rally until the attempt ends (a regroup, a new keep holder) or it dies. Real
    /// players are never leased; they only add weight to the count. The rally's own count is unchanged
    /// (UT_BattlegroundKeepRally).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DutyLeaseBattleground : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;

        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        private static object NewRally(ushort region, eRealm realm)
        {
            Type rallyType = typeof(KeepRally).GetNestedType("Rally", BindingFlags.NonPublic)!;
            object rally = Activator.CreateInstance(rallyType, nonPublic: true)!;
            rallyType.GetField("LeaseOwner")!.SetValue(rally, KeepRally.RallyLeaseOwner(region, realm));
            rallyType.GetField("KeepName")!.SetValue(rally, "Thidranki Faste");
            return rally;
        }

        private static bool Commit(object rally, GameBot bot) =>
            (bool)typeof(KeepRally).GetMethod("CommitBot", Hidden)!.Invoke(null, new[] { rally, bot })!;

        private static void Reset(object rally) => typeof(KeepRally).GetMethod("Reset", Hidden)!.Invoke(null, new[] { rally, (object)0L });

        [Test]
        public void AStormerHoldsTheRallysLeaseUntilTheAttemptEnds()
        {
            object rally = NewRally(252, eRealm.Albion);
            Bot first = Gamebot(level: 22, objective: "Battleground"), second = Gamebot(level: 22, objective: "Battleground");
            Assert.That(Commit(rally, first), Is.True);
            Assert.That(Commit(rally, first), Is.False, "committed once");
            Assert.That(Commit(rally, second), Is.True);
            BotDutyLease lease = BotDutyLeases.Current(first);
            Assert.Multiple(() =>
            {
                Assert.That(lease.Kind, Is.EqualTo(Kind.BattlegroundWave));
                Assert.That(lease.Owner, Is.EqualTo("bg-rally:252:1"));
                Assert.That(lease.OwnerName, Is.EqualTo("Thidranki Faste"));
                Assert.That(lease.Reason, Is.EqualTo(DutyLeaseReason.BattlegroundRally));
            });
            Reset(rally);
            Assert.That(BotDutyLeases.Current(first), Is.Null, "a regroup ends the attempt and its leases");
            Assert.That(BotDutyLeases.Current(second), Is.Null);
        }

        [Test]
        public void AStormerWhoDiesLeavesTheAttempt()
        {
            object rally = NewRally(252, eRealm.Albion);
            Bot stormer = Gamebot(level: 22, objective: "Battleground");
            Commit(rally, stormer);
            BotDutyLeases.OnDied(stormer);
            Assert.That(BotDutyLeases.Current(stormer), Is.Null);
        }

        [Test]
        public void EachRealmsRallyHasItsOwnLease()
        {
            Assert.That(KeepRally.RallyLeaseOwner(252, eRealm.Albion), Is.Not.EqualTo(KeepRally.RallyLeaseOwner(252, eRealm.Midgard)));
            Assert.That(KeepRally.RallyLeaseOwner(252, eRealm.Albion), Is.Not.EqualTo(KeepRally.RallyLeaseOwner(253, eRealm.Albion)));
        }
    }
}
