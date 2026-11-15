using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Wave = DOL.GS.AutonomousBattlegroundWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// The way a battleground storm goes in at Thidranki Faste (owner's PC, Thidranki 2026-11-15). Build 3d67b6f: with both
    /// doors shut, 18 of 19 storm samples marched from outside the walls on the keep door 252000302 (behind the outer gate)
    /// because it was the nearest. Build ea882e6: the outer gate 252000301 was broken and the keep door shut; a bot through
    /// the gate walked at the shielded lord instead of the keep door; and 16 Hibernians died to the keep's guards one at a
    /// time while their wave of 15 went. Only the pure rules are tested here; whether the wave reaches and batters the keep door needs
    /// a run on the owner's PC (BG_STORM_TRACE step=keep-door, BG_WAVE_SPENT diedToGuards).
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundStormRoute
    {
        // Door table, region 252 (Thidranki Faste, keep 11 at 33089,38271).
        private static readonly Vector2 Centre = new(33089, 38271);
        private static readonly Vector3 OuterGate = new(34333, 38275, 3720);   // 252000301, heading 1024
        private static readonly Vector3 KeepDoor = new(32654, 38180, 3720);    // 252000302, heading 1030
        private static readonly Vector3 Postern = new(32323, 37845, 3975);     // 252000303, IsPostern

        // A Hibernian on the staging side, south-west of the keep (BG_STORM_TRACE bot=Siovul at=31103,36309).
        private static readonly Vector2 Hibernian = new(31103, 36309);

        private static (int, float, float, bool, bool) Door(int number, Vector3 at, Vector2 bot, bool posternLike, bool closed) =>
            (number, Vector2.Distance(new(at.X, at.Y), Centre), Vector2.Distance(new(at.X, at.Y), bot), posternLike, closed);

        [Test]
        public void BothDoorsShutTheOuterGateIsTheWayInThoughTheKeepDoorIsNearer()
        {
            var doors = new[]
            {
                Door(2, KeepDoor, Hibernian, false, true),
                Door(1, OuterGate, Hibernian, false, true),
                Door(3, Postern, Hibernian, true, true),
            };
            Assert.That(doors[0].Item3, Is.LessThan(doors[1].Item3), "the keep door is the nearer one for this Hibernian");
            Assert.That(AutonomousWorldBotController.StormDoorIndex(doors), Is.EqualTo(1));
        }

        [Test]
        public void OuterGateBrokenTheShutKeepDoorIsNext()
        {
            // build ea882e6: doors=1/6, doorsUp="252000302@32654,38180,3720"
            var doors = new[]
            {
                Door(1, OuterGate, Hibernian, false, false),
                Door(2, KeepDoor, Hibernian, false, true),
                Door(3, Postern, Hibernian, true, true),
            };
            Assert.That(AutonomousWorldBotController.StormDoorIndex(doors), Is.EqualTo(1));
        }

        [Test]
        public void NoMainDoorShutMeansNoStormDoorEvenWithAShutPostern()
        {
            var doors = new[]
            {
                Door(1, OuterGate, Hibernian, false, false),
                Door(2, KeepDoor, Hibernian, false, false),
                Door(3, Postern, Hibernian, true, true),
            };
            Assert.That(AutonomousWorldBotController.StormDoorIndex(doors), Is.EqualTo(-1));
        }

        [Test]
        public void TwoGatesOfTheSameNumberTheOuterThenTheNearerGoesFirst()
        {
            var nearOuter = (1, 1_200f, 900f, false, true);
            var farOuter = (1, 1_200f, 2_000f, false, true);
            var inner = (1, 400f, 100f, false, true);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousWorldBotController.StormDoorIndex(new[] { farOuter, nearOuter }), Is.EqualTo(1));
                Assert.That(AutonomousWorldBotController.StormDoorIndex(new[] { inner, farOuter }), Is.EqualTo(1));
            });
        }

        [Test]
        public void TheOuterGatesFrontIsOutsideTheWalls()
        {
            Vector3 front = AutonomousWorldBotController.DoorFrontPoint(OuterGate, 1024, Centre, null);
            Assert.Multiple(() =>
            {
                Assert.That(front.X, Is.GreaterThan(OuterGate.X), "east of the gate, away from the keep's centre");
                Assert.That(Vector2.Distance(new(front.X, front.Y), new(OuterGate.X, OuterGate.Y)),
                    Is.EqualTo(AutonomousWorldBotController.DoorFrontDistance).Within(0.5f));
                Assert.That(Vector2.Distance(new(front.X, front.Y), Centre), Is.GreaterThan(Vector2.Distance(new(OuterGate.X, OuterGate.Y), Centre)));
            });
        }

        [Test]
        public void TheKeepDoorsFrontIsInTheCourtyardTowardTheOuterGate()
        {
            Vector3 front = AutonomousWorldBotController.DoorFrontPoint(KeepDoor, 1030, Centre, new Vector2(OuterGate.X, OuterGate.Y));
            Assert.Multiple(() =>
            {
                Assert.That(front.X, Is.GreaterThan(KeepDoor.X).And.LessThan(OuterGate.X), "between the keep door and the gate");
                Assert.That(Vector2.Distance(new(front.X, front.Y), new(KeepDoor.X, KeepDoor.Y)),
                    Is.LessThan(450), "within FindReachableEnemyDoor's reach of the door");
            });
            // Without the gate as approach the "away from the centre" face is the keep building's inside (west, by the lord).
            Vector3 wrong = AutonomousWorldBotController.DoorFrontPoint(KeepDoor, 1030, Centre, null);
            Assert.That(wrong.X, Is.LessThan(KeepDoor.X));
        }

        [Test]
        public void AGoingWaveHelpsTheBotAGuardIsHitting()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Wave.WaveAssists(ranged: false, victimToBot: 0, guardToVictim: 120), Is.True, "the guard is hitting this bot");
                Assert.That(Wave.WaveAssists(ranged: false, victimToBot: 1_400, guardToVictim: 150), Is.True,
                    "a neighbour 1,400 away: farther than the old 1,000 of FiringOnRealm");
                Assert.That(Wave.WaveAssists(ranged: false, victimToBot: 1_600, guardToVictim: 150), Is.False, "too far back in the wave");
                Assert.That(Wave.WaveAssists(ranged: false, victimToBot: 300, guardToVictim: 900), Is.False,
                    "a melee bot does not run at a wall archer it cannot walk to");
                Assert.That(Wave.WaveAssists(ranged: true, victimToBot: 300, guardToVictim: 900), Is.True, "a ranged bot shoots back");
            });
        }

        [Test]
        public void WaveGuardRuleIsUnchangedForTheGuardsNotHittingAnyone()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Wave.WaveTakesGuard(gatesShut: true, isLord: false, distance: 2_000, firingOnRealm: false), Is.False);
                Assert.That(Wave.WaveTakesGuard(gatesShut: true, isLord: false, distance: 1_000, firingOnRealm: false), Is.True);
                Assert.That(Wave.WaveTakesGuard(gatesShut: false, isLord: false, distance: 4_000, firingOnRealm: false), Is.True);
            });
        }
    }
}
