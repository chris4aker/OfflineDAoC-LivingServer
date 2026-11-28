using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.GS.Keeps;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #523 step 1 (b)-(e), owner's answers of 2026-11-27: (b) 45A a lone guard pull raises no alarm, (c) 46B the player's
    /// attack keeps the horn and the size line, (d) 47A the retake after a player capture is a roll, (e) 48A the third realm
    /// joins a player's siege on its roll, after a delay.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PlayerSiegeAlarm523
    {
        private DOL.UnitTests.EpicTestServerScope _server;
        private object _botSource, _retakeRoll;
        private static FieldInfo Field(string name) =>
            typeof(L).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp] public void SetUp()
        {
            _server = new DOL.UnitTests.EpicTestServerScope();
            _botSource = Field("AttackerRealmBotSource").GetValue(null);
            _retakeRoll = Field("RetakeRoll").GetValue(null);
            Field("AttackerRealmBotSource").SetValue(null, (Func<IEnumerable<GameLiving>>)(() => Array.Empty<GameLiving>()));
            RvrEventTestState.Clear();
        }

        [TearDown] public void TearDown()
        {
            RvrEventTestState.Clear();
            Field("AttackerRealmBotSource").SetValue(null, _botSource);
            Field("RetakeRoll").SetValue(null, _retakeRoll);
            _server.Dispose();
        }

        private sealed class KeepPlayer : GamePlayer
        {
            private KeepPlayer() : base(null, null) { }
            public override bool IsAlive => true;
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override int X => 100000;
            public override int Y => 100000;
            public override int Z => 100;
            public override ushort CurrentRegionID { get => 100; set { } }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
            public override DOL.GS.PacketHandler.IPacketLib Out => DispatchProxy.Create<DOL.GS.PacketHandler.IPacketLib, UT_PlayerKeepDefense.Silent>();
        }

        private sealed class Ally : GameNPC
        {
            public override bool IsAlive => true;
            public override int X => 100500;
            public override int Y => 100000;
            public override int Z => 100;
            public override ushort CurrentRegionID { get => 100; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
        }

        private static GameKeep Keep(ushort id = 9)
        {
            var keep = (GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(GameKeep));
            keep.DBKeep = new DOL.Database.DbKeep { KeepID = id, Name = "Test keep", Realm = (byte)eRealm.Midgard, Region = 100,
                X = 100000, Y = 100000, Z = 100, BaseLevel = 50 };
            return keep;
        }

        private static KeepPlayer Player() => (KeepPlayer)RuntimeHelpers.GetUninitializedObject(typeof(KeepPlayer));

        private static IDictionary Alarms() => (IDictionary)Field("DefenseAlarms").GetValue(null);
        private static IDictionary Events() => (IDictionary)Field("Events").GetValue(null);

        private static bool Sustained(string id)
        {
            object alarm = Alarms()[id];
            return (bool)alarm.GetType().GetProperty("Sustained").GetValue(alarm);
        }

        private static object NewAlarm(AbstractGameKeep keep, GamePlayer player, long tick, bool sustained)
        {
            var type = typeof(L).GetNestedType("Alarm", BindingFlags.NonPublic);
            return Activator.CreateInstance(type, keep, player, tick, sustained);
        }

        // ---- (b) a lone guard pull ------------------------------------------------------------------------------

        [TestCase(L.DefenseScale.Small, false, false, TestName = "A lone guard hit raises nothing")]
        [TestCase(L.DefenseScale.Small, true, true, TestName = "A lone attacker who hit a door or killed a guard raises it")]
        [TestCase(L.DefenseScale.Standard, false, true, TestName = "Two or more attackers at the keep raise it on a guard hit")]
        [TestCase(L.DefenseScale.Large, false, true, TestName = "A raid at the keep raises it on a guard hit")]
        [TestCase(L.DefenseScale.Massive, true, true, TestName = "A massive army raises it")]
        public void WhenAnAlarmRaisesADefense(L.DefenseScale scale, bool sustained, bool raises) =>
            Assert.That(L.AlarmRaisesDefense(scale, sustained), Is.EqualTo(raises));

        [Test]
        public void ALoneGuardPullRaisesNoDefenseAndTheAlarmWaits()
        {
            var keep = Keep();
            long now = GameLoop.GameLoopTime;
            L.ObserveKeepAttack(keep, Player(), L.KeepAttackKind.GuardHit);
            Assert.That(Sustained("rvr-keep-9"), Is.False);
            L.PulseDefense(now);
            Assert.That(L.Snapshot(), Is.Empty, "a guard hit by one attacker raises no defense");
            Assert.That(Alarms().Contains("rvr-keep-9"), Is.True, "the alarm waits for a door hit or a guard death");
            L.PulseDefense(now + L.DefenseQuietMilliseconds + 5_000);
            Assert.That(Alarms().Contains("rvr-keep-9"), Is.False, "after the quiet minutes it is gone");
            Assert.That(L.Snapshot(), Is.Empty);
        }

        [TestCase(L.KeepAttackKind.DoorHit)]
        [TestCase(L.KeepAttackKind.GuardKilled)]
        public void ADoorHitOrAGuardDeathRaisesItForALoneAttacker(L.KeepAttackKind kind)
        {
            var keep = Keep();
            var player = Player();
            long now = GameLoop.GameLoopTime;
            L.ObserveKeepAttack(keep, player, L.KeepAttackKind.GuardHit);
            L.PulseDefense(now);
            Assert.That(L.Snapshot(), Is.Empty);
            // In the same second as the guard hit: the one-a-second throttle must not drop it.
            L.ObserveKeepAttack(keep, player, kind);
            Assert.That(Sustained("rvr-keep-9"), Is.True);
            L.PulseDefense(now + 5_000);
            var defense = L.Snapshot().Single();
            Assert.That(defense.TargetId, Is.EqualTo("rvr-keep-9"));
            Assert.That(defense.Kind, Does.Contain("defense"));
            Assert.That(ScaleOf("rvr-keep-9"), Is.EqualTo(L.DefenseScale.Small), "the lone attacker still draws the small answer");
        }

        [Test]
        public void ADoorHitStaysWithTheAlarmThroughLaterGuardHits()
        {
            var keep = Keep();
            var player = Player();
            long now = GameLoop.GameLoopTime;
            Alarms()["rvr-keep-9"] = NewAlarm(keep, player, now - 2 * 60_000, sustained: true);
            L.ObserveKeepAttack(keep, player, L.KeepAttackKind.GuardHit);
            Assert.That(Sustained("rvr-keep-9"), Is.True, "a door hit two minutes ago still counts");

            Alarms()["rvr-keep-9"] = NewAlarm(keep, player, now - L.DefenseQuietMilliseconds - 1, sustained: true);
            L.ObserveKeepAttack(keep, player, L.KeepAttackKind.GuardHit);
            Assert.That(Sustained("rvr-keep-9"), Is.False, "a door hit from before the quiet minutes does not");
        }

        [Test]
        public void TwoAttackersAtTheKeepRaiseItOnAGuardHit()
        {
            var keep = Keep();
            var ally = (Ally)RuntimeHelpers.GetUninitializedObject(typeof(Ally));
            Field("AttackerRealmBotSource").SetValue(null, (Func<IEnumerable<GameLiving>>)(() => new GameLiving[] { ally }));
            L.ObserveKeepAttack(keep, Player(), L.KeepAttackKind.GuardHit);
            L.PulseDefense(GameLoop.GameLoopTime);
            Assert.That(L.Snapshot().Single().TargetId, Is.EqualTo("rvr-keep-9"));
            Assert.That(ScaleOf("rvr-keep-9"), Is.EqualTo(L.DefenseScale.Standard));
        }

        [Test]
        public void ALoneGuardHitAtAPlannedSiegeStillMarksItAttacked()
        {
            var keep = Keep();
            var target = new L.LiveObjective("rvr-keep-9", "Test keep", L.Intent.AssaultKeep, eRealm.Midgard, 100, 100000, 100000, 100,
                false, 0, 0, 0, 0);
            Assert.That(L.ForceStart(target, eRealm.Albion, GameLoop.GameLoopTime, out string reason), Is.True, reason);
            object planned = Events()["rvr-keep-9"];
            planned.GetType().GetField("AttackObserved").SetValue(planned, false);
            L.ObserveKeepAttack(keep, Player(), L.KeepAttackKind.GuardHit);
            L.PulseDefense(GameLoop.GameLoopTime);
            Assert.That((bool)planned.GetType().GetField("AttackObserved").GetValue(planned), Is.True);
            Assert.That(L.IsPlayerDefenseResponse("rvr-keep-9", GameLoop.GameLoopTime), Is.False);
        }

        private static L.DefenseScale ScaleOf(string id)
        {
            object active = Events()[id];
            return (L.DefenseScale)active.GetType().GetField("DefenseScale").GetValue(active);
        }

        // ---- (c) the horn and the size line stay --------------------------------------------------------------

        [Test]
        public void APlayersAttackKeepsTheHornAndTheSizeLine()
        {
            Assert.That(L.DefenseHornSound, Is.EqualTo(219), "the horn is on by default (owner, 46B)");
            Assert.That(L.RallyLine(L.DefenseScale.Small), Is.EqualTo("A small defense force has been rallied!"));
            Assert.That(L.RallyLine(L.DefenseScale.Massive), Is.EqualTo("A massive defense force has been rallied!"));
        }

        // ---- (d) the retake is a roll -------------------------------------------------------------------------

        [TestCase(0.0, true)]
        [TestCase(0.49, true)]
        [TestCase(0.5, false)]
        [TestCase(0.99, false)]
        public void TheRetakeFollowsAPlayerCaptureOnARoll(double roll, bool retakes)
        {
            Assert.That(RealmEventPolicy.PlayerCaptureRetakeChance, Is.EqualTo(0.5));
            Assert.That(RealmEventPolicy.RetakeFollowsPlayerCapture(roll), Is.EqualTo(retakes));
        }

        [TestCase(0.49, true)]
        [TestCase(0.5, false)]
        public void AKeepThePlayerTookIsRetakenOnlyWhenTheRollComesUp(double roll, bool retakes)
        {
            Field("RetakeRoll").SetValue(null, (Func<double>)(() => roll));
            long now = GameLoop.GameLoopTime + 100;
            var target = new L.LiveObjective("rvr-keep-9", "Test keep", L.Intent.AssaultKeep, eRealm.Midgard, 100, 100000, 100000, 100,
                false, 0, 0, 0, 0);
            Assert.That(L.BeginDefenseResponse(target, eRealm.Albion, "offline", now), Is.True);
            L.EndTarget(target.Id, now + 1_000, eRealm.Albion);
            var events = L.Snapshot();
            if (retakes)
            {
                var retake = events.Single();
                Assert.That(retake.Attacker, Is.EqualTo(eRealm.Midgard), "the realm that lost the keep comes back");
                Assert.That(retake.Defender, Is.EqualTo(eRealm.Albion));
            }
            else Assert.That(events, Is.Empty, "no retake: the keep is left as after a bot capture");
        }

        // ---- (e) the third realm joins on its roll, after a delay -------------------------------------------------

        [TestCase(false, 0L, false)]
        [TestCase(false, 60 * 60_000L, false)]
        [TestCase(true, 0L, false)]
        [TestCase(true, 5 * 60_000L - 1, false)]
        [TestCase(true, 5 * 60_000L, true)]
        [TestCase(true, 60 * 60_000L, true)]
        public void TheThirdRealmJoinsAPlayersSiegeOnItsRollAfterTheDelay(bool intervenes, long since, bool joins)
        {
            Assert.That(RealmEventPolicy.ThirdRealmDelayMilliseconds, Is.EqualTo(5 * 60_000L));
            Assert.That(L.ThirdRealmJoinsDefense(intervenes, since), Is.EqualTo(joins));
        }

        [Test]
        public void ThePlayerDefenseClockStartsAtTheAlarm()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = new L.LiveObjective("rvr-keep-9", "Test keep", L.Intent.AssaultKeep, eRealm.Midgard, 100, 100000, 100000, 100,
                false, 0, 0, 0, 0);
            L.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            object active = Events()["rvr-keep-9"];
            Assert.That((long)active.GetType().GetField("BattleStartedTick").GetValue(active), Is.EqualTo(now),
                "the third realm's five minutes count from the alarm");
            // A later hit does not move it.
            L.BeginDefenseResponse(target, eRealm.Albion, "offline", now + 60_000);
            Assert.That((long)active.GetType().GetField("BattleStartedTick").GetValue(active), Is.EqualTo(now));
        }

        // The three wiring sites the rules above don't reach (review of #802): the tests call ObserveKeepAttack with an
        // explicit kind and ThirdRealmJoinsDefense directly, so the call sites are pinned in the source text. Reverting any
        // of them fails here.
        private static string Source(string relative)
        {
            string root = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 10 && root != null && !File.Exists(Path.Combine(root, relative)); i++)
                root = Path.GetDirectoryName(root);
            if (root == null || !File.Exists(Path.Combine(root, relative)))
                Assert.Fail($"{relative} not found above the test folder.");
            return Regex.Replace(File.ReadAllText(Path.Combine(root, relative)), @"\s+", " ");
        }

        [Test]
        public void TheDoorTheGuardDeathAndTheThirdRealmFilterAreWired()
        {
            string door = Source("source/server/GameServer/keeps/Gameobjects/GameKeepDoor.cs");
            string guard = Source("source/server/GameServer/keeps/Gameobjects/Guards/GameKeepGuard.cs");
            string defense = Source("source/server/GameServer/bots/autonomous/AutonomousRvrEventLayer.Defense.cs");
            Assert.Multiple(() =>
            {
                // (b) A door hit raises the alarm for a lone attacker.
                Assert.That(door, Does.Contain(
                    "AutonomousRvrEventLayer.ObserveKeepAttack(Component.Keep, source, AutonomousRvrEventLayer.KeepAttackKind.DoorHit);"));
                // (b) A guard's death raises it too, in Die, before the base death.
                int die = guard.IndexOf("public override void Die(GameObject killer)", StringComparison.Ordinal);
                Assert.That(die, Is.GreaterThanOrEqualTo(0), "GameKeepGuard.Die");
                string dieBody = guard.Substring(die, guard.IndexOf("base.Die(killer);", die, StringComparison.Ordinal) - die);
                Assert.That(dieBody, Does.Contain(
                    "if (killer != this) AutonomousRvrEventLayer.ObserveKeepAttack(Component?.Keep, killer, AutonomousRvrEventLayer.KeepAttackKind.GuardKilled);"));
                // (e) The recruit pass skips the third realm of a player's siege until its roll and delay allow it.
                Assert.That(defense, Does.Contain(
                    "if (active.DefenseReaction && force.Realm == OtherRealm(active) && !ThirdRealmJoinsDefense(active.ThirdRealmIntervenes, now - active.BattleStartedTick)) continue;"));
            });
        }
    }
}
