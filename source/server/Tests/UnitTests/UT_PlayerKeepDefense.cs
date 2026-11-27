using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture, NonParallelizable]
    public class UT_PlayerKeepDefense
    {
        [SetUp] public void SetUp() { RvrEventTestState.Clear(); }
        [TearDown] public void TearDown() { RvrEventTestState.Clear(); }
        private static AutonomousRvrEventLayer.LiveObjective Target(string id = "defense") =>
            new(id, "Test keep", AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100000, 100000, 100, false, 0, 0, 0, 0);

        [Test]
        public void AlarmOpensImmediateTimedBattleAndRepeatedHitsDoNotResetItsTimer()
        {
            Assert.That(AutonomousRvrEventLayer.DefenseResponseMilliseconds, Is.EqualTo(4 * 60 * 60_000L));
            long now = GameLoop.GameLoopTime + 100;
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now), Is.True);
            var first = AutonomousRvrEventLayer.Snapshot().Single();
            Assert.That(first.BattleStarted, Is.True);
            Assert.That(first.Kind, Does.Contain("defense"));
            Assert.That(first.RemainingMilliseconds, Is.EqualTo(now + AutonomousRvrEventLayer.DefenseResponseMilliseconds - GameLoop.GameLoopTime));
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now + 30_000), Is.False);
            Assert.That(AutonomousRvrEventLayer.Snapshot().Single().RemainingMilliseconds, Is.EqualTo(first.RemainingMilliseconds));
        }

        [Test]
        public void PlayerResponseDoesNotBlockIndependentAutonomousSiegeOrResetExistingSiegeClock()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now);
            Assert.That(AutonomousRvrEventLayer.ForceStart(Target("other"), eRealm.Hibernia, now, out _), Is.True);
            var before = AutonomousRvrEventLayer.Snapshot().Single(e => e.TargetId == "other");
            AutonomousRvrEventLayer.BeginDefenseResponse(Target("other"), eRealm.Albion, "offline", now + 30_000);
            var after = AutonomousRvrEventLayer.Snapshot().Single(e => e.TargetId == "other");
            Assert.That(after.RemainingMilliseconds, Is.EqualTo(before.RemainingMilliseconds));
            Assert.That(after.Attacker, Is.EqualTo(eRealm.Hibernia));
        }

        [Test]
        public void QuietDefenseSurvivesFiveMinutesButEndsAtFourHours()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now);
            AutonomousRvrEventLayer.PulseDefense(now + 5 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1));
            AutonomousRvrEventLayer.PulseDefense(now + AutonomousRvrEventLayer.DefenseResponseMilliseconds);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        // #523 step 1 (a), owner 2026-11-24: a player-attack defense ends about ten minutes after the attackers leave.
        [Test]
        public void DefenseEndsTenMinutesAfterTheAttackersLeave()
        {
            Assert.That(AutonomousRvrEventLayer.RepelMilliseconds, Is.EqualTo(10 * 60_000));
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now);
            AutonomousRvrEventLayer.PulseDefense(now + 9 * 60_000 + 55_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Not yet ten minutes quiet");
            AutonomousRvrEventLayer.PulseDefense(now + 10 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty, "Ten minutes with no attacker ends the defense");
        }

        [Test]
        public void AHitOnTheKeepPutsTheEndBackTenMinutes()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = Target();
            AutonomousRvrEventLayer.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            Pressure()[target.Id] = now + 8 * 60_000; // a guard or door hit by any enemy realm (ObserveKeepAttack)
            AutonomousRvrEventLayer.PulseDefense(now + 12 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1));
            AutonomousRvrEventLayer.PulseDefense(now + 17 * 60_000 + 55_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1));
            AutonomousRvrEventLayer.PulseDefense(now + 18 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        [Test]
        public void TheAttackingRealmsBotsAtTheKeepHoldTheDefenseOpen()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = Target();
            AutonomousRvrEventLayer.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            object active = EventOf(target.Id);
            ((Dictionary<string, int>)active.GetType().GetField("Attackers").GetValue(active)).Add("albion-helpers", 8);
            var present = (IDictionary)active.GetType().GetField("Present").GetValue(active);
            long seenAt = now + 9 * 60_000;
            present[1L] = ("albion-helpers", eRealm.Albion, seenAt, (GameBot)null, new Vector3(target.X, target.Y, target.Z), target.RegionId);
            AutonomousRvrEventLayer.PulseDefense(seenAt + 5_000);
            AutonomousRvrEventLayer.PulseDefense(now + 18 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Attackers seen at minute 9 hold it to minute 19");
            AutonomousRvrEventLayer.PulseDefense(seenAt + 5_000 + AutonomousRvrEventLayer.RepelMilliseconds);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        [TestCase(true, true, 100, 0f, 0f, 0f, true)]
        [TestCase(true, true, 100, 9_000f, 3_000f, 0f, true)]
        [TestCase(true, true, 100, 9_000f, 4_000f, 0f, false)]
        [TestCase(true, true, 100, 0f, 0f, 2_500f, false)]
        [TestCase(true, true, 101, 0f, 0f, 0f, false)]
        [TestCase(false, true, 100, 0f, 0f, 0f, false)]
        [TestCase(true, false, 100, 0f, 0f, 0f, false)]
        public void AnAttackerCountsAtTheKeepOnlyAliveInItsRegionAndInsideTheRing(bool alive, bool inPlay, int region,
            float dx, float dy, float dz, bool expected)
        {
            Assert.That(AutonomousRvrEventLayer.AttackerAtKeep(alive, inPlay, (ushort)region, 100, dx, dy, dz), Is.EqualTo(expected));
        }

        [TestCase(0L, 99_000_000L, false)]
        [TestCase(1_000L, 1_000L + 10 * 60_000 - 1, false)]
        [TestCase(1_000L, 1_000L + 10 * 60_000, true)]
        public void RepelRuleNeedsTenQuietMinutes(long seen, long now, bool expected)
        {
            Assert.That(AutonomousRvrEventLayer.PlayerAttackRepelled(now, seen), Is.EqualTo(expected));
        }

        private static ConcurrentDictionary<string, long> Pressure() =>
            (ConcurrentDictionary<string, long>)typeof(AutonomousRvrEventLayer).GetField("KeepCombatPressure", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static object EventOf(string id) =>
            ((IDictionary)typeof(AutonomousRvrEventLayer).GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))[id];

        [Test]
        public void CrossRealmAttackPreservesBothIndependentDefenseWindows()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target("albion") with { OwningRealm = eRealm.Albion }, eRealm.Midgard, "offline", now);
            var old = AutonomousRvrEventLayer.Snapshot().Single();
            AutonomousRvrEventLayer.BeginDefenseResponse(Target("hibernia") with { OwningRealm = eRealm.Hibernia }, eRealm.Midgard, "offline", now + 60_000);
            var events = AutonomousRvrEventLayer.Snapshot();
            Assert.That(events.Length, Is.EqualTo(2));
            Assert.That(events.Single(e => e.TargetId == "albion").RemainingMilliseconds, Is.EqualTo(old.RemainingMilliseconds));
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Hibernia, "offline", "offline", now, now + 60_000), Is.False);
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Albion, "offline", "offline", now, now + 60_000), Is.True);
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Albion, "offline", "offline", now + 60_000, now), Is.False);
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Albion, "other", "offline", now, now + 60_000), Is.False);
        }

        [Test]
        public void FriendlyAndProtectedKeepsDoNotRaiseHostileResponse()
        {
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Midgard, "offline", 100), Is.False);
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target() with { IsPortalKeep = true }, eRealm.Albion, "offline", 100), Is.False);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        [Test]
        public void ResponseScalesWithTheAttackingForce()
        {
            Assert.That(AutonomousRvrEventLayer.ScaleOf(1), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Small));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(8), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(40), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(80), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Massive));
            int previous = 0;
            foreach (var scale in new[] { AutonomousRvrEventLayer.DefenseScale.Small, AutonomousRvrEventLayer.DefenseScale.Standard,
                         AutonomousRvrEventLayer.DefenseScale.Large, AutonomousRvrEventLayer.DefenseScale.Massive })
            {
                int defenders = AutonomousRvrEventLayer.ResponseCap(true, scale);
                Assert.That(defenders, Is.GreaterThan(previous));
                Assert.That(defenders, Is.LessThanOrEqualTo(AutonomousRvrEventLayer.ResponseCap(true)));
                Assert.That(AutonomousRvrEventLayer.ResponseCap(false, scale), Is.LessThan(defenders));
                previous = defenders;
            }
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Small), Does.Contain("small defense force"));
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Standard), Does.StartWith("A defense force"));
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Large), Does.Contain("large defensive force"));
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Massive), Does.Contain("massive defense force"));
        }

        // #523, owner 2026-11-24: a player-attack defense is sized "by the attackers actually seen", not the roster.
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(80, 80)]
        public void SeenForceNeverCountsBelowOne(int seen, int expected)
        {
            Assert.That(AutonomousRvrEventLayer.SeenForceSize(seen), Is.EqualTo(expected));
        }

        private sealed class Attacker : GameNPC
        {
            public int TestX, TestY, TestZ;
            public ushort TestRegion = 100;
            public bool Alive = true;
            public eRealm TestRealm = eRealm.Albion;
            public override bool IsAlive => Alive;
            public override int X => TestX;
            public override int Y => TestY;
            public override int Z => TestZ;
            public override ushort CurrentRegionID { get => TestRegion; set { } }
            public override eRealm Realm { get => TestRealm; set { } }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
        }

        private static Attacker At(int x, int y = 100000, int z = 100)
        {
            var attacker = (Attacker)RuntimeHelpers.GetUninitializedObject(typeof(Attacker));
            attacker.TestX = x; attacker.TestY = y; attacker.TestZ = z;
            attacker.TestRegion = 100; attacker.Alive = true; attacker.TestRealm = eRealm.Albion;
            return attacker;
        }

        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(group);
            foreach (GameLiving member in members) { list.Add(member); member.Group = group; }
            return group;
        }

        private static int Seen(GameLiving attacker) => AutonomousRvrEventLayer.SeenForceSize(attacker, 100, 100000, 100000, 100);

        [Test]
        public void OnlyTheGroupMembersAtTheKeepSizeTheDefense()
        {
            // A group of eight (the player and seven companions) with only three at the keep: Standard, from three.
            Attacker leader = At(100000);
            var members = new List<GameLiving> { leader, At(101000), At(102000) };
            for (int i = 0; i < 5; i++) members.Add(At(150000)); // fifty thousand units away: not seen
            GroupOf(members.ToArray());
            Assert.That(leader.Group.MemberCount, Is.EqualTo(8), "the roster is eight");
            Assert.That(Seen(leader), Is.EqualTo(3));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(Seen(leader)), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));
        }

        [Test]
        public void AbsentDeadAndOtherRegionMembersAreNotCounted()
        {
            Attacker leader = At(100000), dead = At(100000), elsewhere = At(100000), high = At(100000, z: 5000),
                otherRealm = At(100000), here = At(100500);
            dead.Alive = false;
            elsewhere.TestRegion = 101;
            otherRealm.TestRealm = eRealm.Hibernia;
            GroupOf(leader, dead, elsewhere, high, otherRealm, here);
            Assert.That(Seen(leader), Is.EqualTo(2), "the leader and the one living member in the ring");
        }

        [Test]
        public void ACompanionRaidCountsOnlyWhoIsThere()
        {
            // A /raid 40 with twelve at the keep: Large (9-40), not the forty of the roster; never Massive.
            Attacker leader = At(100000);
            var members = new List<GameLiving> { leader };
            for (int i = 0; i < 11; i++) members.Add(At(100000 + i * 100));
            for (int i = 0; i < 28; i++) members.Add(At(100000, y: 130000));
            GroupOf(members.ToArray());
            Assert.That(Seen(leader), Is.EqualTo(12));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(Seen(leader)), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));
        }

        [Test]
        public void ASoloOrFarAwayAttackerStillCountsOne()
        {
            Assert.That(Seen(At(100000)), Is.EqualTo(1), "solo at the keep");
            Assert.That(Seen(At(200000)), Is.EqualTo(1), "the attack itself was seen (a pet or a siege engine hit)");
            Assert.That(Seen(null), Is.EqualTo(1));
            Attacker far = At(200000);
            GroupOf(far, At(200000), At(200000));
            Assert.That(Seen(far), Is.EqualTo(1), "a whole group away from the keep is still one seen attack");
        }

        [Test]
        public void TiersAndCapsAreUnchanged()
        {
            Assert.That(AutonomousRvrEventLayer.ScaleOf(1), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Small));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(2), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(9), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(41), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Massive));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Small), Is.EqualTo(24));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Standard), Is.EqualTo(48));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Large), Is.EqualTo(128));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Massive), Is.EqualTo(240));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Small), Is.EqualTo(8));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Massive), Is.EqualTo(96));
        }

        [Test]
        public void DefenseMarchesInNumbersNotATrickle()
        {
            // A huge force over speed (owner): 80% of the force gathers before it marches (or ten minutes pass).
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(240), Is.EqualTo(192));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(64), Is.EqualTo(51));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(24), Is.EqualTo(19));
            Assert.That(AutonomousRvrEventLayer.PlayerDefenseMusterMilliseconds, Is.EqualTo(10 * 60_000));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(4), Is.EqualTo(4));
        }

        [Test]
        public void PlayerHelpingAPlannedSiegeRaisesNoDefense()
        {
            long now = GameLoop.GameLoopTime + 100;
            Assert.That(AutonomousRvrEventLayer.ForceStart(Target(), eRealm.Albion, now, out _), Is.True);
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now + 1000,
                AutonomousRvrEventLayer.DefenseScale.Small), Is.False);
            Assert.That(AutonomousRvrEventLayer.IsPlayerDefenseResponse(Target().Id, now + 1000), Is.False);
        }

        [Test]
        public void ResponseHasLargeDefenseAndBoundedHelpersWithStableReserve()
        {
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true), Is.EqualTo(240));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false), Is.EqualTo(96));
            ulong seed = AutonomousRvrEventLayer.ReserveSeed("keep", 1000);
            Assert.That(Enumerable.Range(1, 1000).Count(id => AutonomousRvrEventLayer.ResponseReserve(id, seed)), Is.InRange(250, 350));
            Assert.That(AutonomousRvrEventLayer.PlayerInstigator(null), Is.Null);
        }

        [TestCase(true, true, false, true)]
        [TestCase(true, true, true, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, false, true)]
        public void ForcedExpeditionMayCoexistButNeverDuplicateEncounter(bool forced, bool sameRealm, bool sameEncounter, bool allowed)
        {
            Assert.That(RealmRaidRecruitmentPolicy.CanOpenEvent(forced, sameRealm, sameEncounter), Is.EqualTo(allowed));
        }
    }
}
