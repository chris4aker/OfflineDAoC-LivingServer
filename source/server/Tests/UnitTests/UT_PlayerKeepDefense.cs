using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;
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

        // Owner, 2026-11-27 (#784, answer 1B): "bots should be performing their own sieges and defense without the player",
        // so attacker-realm bots still at the keep hold the defense open after the player leaves.
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

        // Review ask 2: the 4-hour cap still ends a defense whose attack is seen the whole time (a hit every five minutes,
        // so the ten-minute rule never fires). Without the cap it would stay open.
        [Test]
        public void ADefenseAttackedTheWholeTimeStillEndsAtFourHours()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = Target();
            AutonomousRvrEventLayer.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            long cap = now + AutonomousRvrEventLayer.DefenseResponseMilliseconds;
            for (long t = now + 5 * 60_000; t < cap; t += 5 * 60_000)
            {
                Pressure()[target.Id] = t;
                AutonomousRvrEventLayer.PulseDefense(t);
                Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), $"Still attacked at minute {(t - now) / 60_000}");
            }
            Pressure()[target.Id] = cap - 1_000;
            AutonomousRvrEventLayer.PulseDefense(cap - 1_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Hit a second before the cap");
            AutonomousRvrEventLayer.PulseDefense(cap + 5_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty, "The four-hour cap ends it though the attack was seen a moment ago");
        }

        // Review ask 3: the attacking player standing at the keep (no hits after the alarm) holds the defense open, through
        // the alarm path that records who raised it (active.AttackingPlayer = alarm.Player).
        [Test]
        public void TheAttackingPlayerAtTheKeepHoldsTheDefenseOpenUntilTheyLeave()
        {
            using var server = new DOL.UnitTests.EpicTestServerScope();
            long now = GameLoop.GameLoopTime + 100;
            var keep = (DOL.GS.Keeps.GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Keeps.GameKeep));
            keep.DBKeep = new DOL.Database.DbKeep { KeepID = 7, Name = "Test keep", Realm = (byte)eRealm.Midgard, Region = 100,
                X = 100000, Y = 100000, Z = 100, BaseLevel = 50 };
            var player = (KeepPlayer)RuntimeHelpers.GetUninitializedObject(typeof(KeepPlayer));
            player.TestX = 100000; player.TestY = 100000; player.TestZ = 100;
            Alarms()["rvr-keep-7"] = NewAlarm(keep, player, now);

            AutonomousRvrEventLayer.PulseDefense(now);
            Assert.That(AutonomousRvrEventLayer.Snapshot().Single().TargetId, Is.EqualTo("rvr-keep-7"), "The alarm raised the defense");
            AutonomousRvrEventLayer.PulseDefense(now + 6 * 60_000);
            AutonomousRvrEventLayer.PulseDefense(now + 12 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "The player is still at the keep at minute 12");

            player.TestX = 200000; // rides away at minute 12
            AutonomousRvrEventLayer.PulseDefense(now + 12 * 60_000 + 5_000);
            AutonomousRvrEventLayer.PulseDefense(now + 21 * 60_000 + 55_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Not yet ten minutes since they left");
            AutonomousRvrEventLayer.PulseDefense(now + 22 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty, "Ten minutes after the player left, the defense ends");
        }

        private sealed class KeepPlayer : GamePlayer
        {
            private KeepPlayer() : base(null, null) { }
            public int TestX, TestY, TestZ;
            public override bool IsAlive => true;
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override int X => TestX;
            public override int Y => TestY;
            public override int Z => TestZ;
            public override ushort CurrentRegionID { get => 100; set { } }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
            public override DOL.GS.PacketHandler.IPacketLib Out => DispatchProxy.Create<DOL.GS.PacketHandler.IPacketLib, Silent>();
        }

        public class Silent : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args) =>
                method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        }

        private static IDictionary Alarms() =>
            (IDictionary)typeof(AutonomousRvrEventLayer).GetField("DefenseAlarms", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        // The alarm record is private; fill its constructor by parameter type so the test survives a change of its fields.
        private static object NewAlarm(AbstractGameKeep keep, GamePlayer player, long tick)
        {
            var type = typeof(AutonomousRvrEventLayer).GetNestedType("Alarm", BindingFlags.NonPublic);
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            object[] args = ctor.GetParameters().Select(p =>
                p.ParameterType == typeof(long) ? tick :
                p.ParameterType == typeof(int) ? 1 :
                typeof(GamePlayer).IsAssignableFrom(p.ParameterType) ? player :
                typeof(AbstractGameKeep).IsAssignableFrom(p.ParameterType) ? (object)keep : null).ToArray();
            return ctor.Invoke(args);
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
            Assert.That(Enumerable.Range(1, 100).Count(id => AutonomousRvrEventLayer.ResponseReserve(id)), Is.EqualTo(30));
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
