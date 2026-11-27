using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #523 step 1 (c)-(e), owner's answers of 2026-11-27: (c) 46B the player's attack keeps the horn and the size line,
    /// (d) 47A the retake after a player capture is a roll, (e) 48A the third realm joins a player's siege on its roll, after
    /// a delay.
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

        private static IDictionary Events() => (IDictionary)Field("Events").GetValue(null);

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
    }
}
