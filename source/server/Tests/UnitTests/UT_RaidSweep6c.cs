using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Sweep 6c (owner's PC log, build b1b6846, 2026-11-18, 10:27-12:00): the Earth primal frozen out of his tether and fed
    /// melee (Galladoria), the raid's front set in mid-air at the flying Air primal (Galladoria), a Krondon return wave stuck
    /// with no help left while the raid mustered, and the server stall when a raid's parties were all ended in one sweep.
    /// Bosses, raiders and routes stand in a test region; no world tick, combat, spell or pathing runs. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidSweep6c
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int EarthTemplateId = 60159436;
        private Region _region;
        private ushort _nextId = 6300;
        private EpicTestServerScope _server;
        private NpcTemplate _earthTemplate;

        public class Boss6c : GameEpicBoss
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
            public override bool InCombat => false;
            // No property calculators run here: its speed is its base speed.
            public override short MaxSpeed => MaxSpeedBase;
        }
        // The scripts' own names: the Fire primal's rule looks the others up by name.
        public sealed class FirePrimal : Boss6c { }
        public sealed class EarthPrimal : Boss6c { }
        public sealed class WaterPrimal : Boss6c { }
        public sealed class Mob : GameNPC { public override bool IsAlive => true; }

        private const int Hero = (int)eCharacterClass.Hero;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = EpicEncounterTactics.GalladoriaRegion, Name = "Sweep 6c test",
                Description = "Sweep 6c test", Mobs = [] });
            EncounterSignals.ClearAll();
            EpicEncounterMechanics.ResetPerEncounter();
            // The Earth primal's template (60159436) as the world database has it, with his walking speed. (A missing
            // template is logged as an error, which needs a running logger.)
            _earthTemplate = new NpcTemplate { TemplateId = EarthTemplateId, MaxSpeed = 250, Name = "Corrupt Primal of Earth" };
            NpcTemplateMgr.AddTemplate(_earthTemplate);
        }

        [TearDown]
        public void TearDown()
        {
            if (_earthTemplate != null) NpcTemplateMgr.RemoveTemplate(_earthTemplate);
            _earthTemplate = null;
            EncounterSignals.ClearAll();
            EpicEncounterMechanics.ResetPerEncounter();
            _server.Dispose();
        }

        private T Npc<T>(int x, int y, int z, string name = null) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            // The name before the object is active: setting it on an active one tells the players around it.
            if (name != null) typeof(GameObject).GetField("m_name", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(npc, name);
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new System.Collections.ArrayList());
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            Move(npc, x, y, z);
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private static void Move(GameNPC npc, int x, int y, int z)
        {
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
        }

        /// <summary>The Earth primal as on the PC at 10:51-11:12: 1,044 from his spawn, outside his 890 tether, frozen by his
        /// script (speed 0).</summary>
        private EarthPrimal FrozenEarthPrimal()
        {
            var earth = Npc<EarthPrimal>(39727, 62620, 11684, "Corrupt Primal of Earth");
            earth.TetherRange = 890;
            Move(earth, 39727 + 1044, 62620, 11684);
            earth.MaxSpeedBase = 0;
            return earth;
        }

        // ---- G-4: the Earth primal frozen outside his tether --------------------------------------------------------

        [Test]
        public void TheEarthPrimalGetsHisLegsBackOnceNobodyIsOnHisListAndGoesHome()
        {
            var earth = FrozenEarthPrimal();
            Assert.That(earth.IsOutOfTetherRange, Is.True, "1,044 from his spawn, tether 890");
            var brain = new EarthPrimalBrain { Body = earth };
            Assert.That(brain.HasAggro, Is.False, "the leash cleared his list");
            brain.ReturnHomeIfListEmpty();
            Assert.Multiple(() =>
            {
                Assert.That(earth.MaxSpeedBase, Is.EqualTo((short)250),
                    "build b1b6846 left him at speed 0: 13 leashes in a row at fromSpawn=1044 and he never moved");
                Assert.That(brain.FSM.GetCurrentState().StateType, Is.EqualTo(eFSMStateType.RETURN_TO_SPAWN));
            });
        }

        [Test]
        public void TheEarthPrimalStaysFrozenInsideHisTetherAndKeepsASpeedHeAlreadyHas()
        {
            // In the fight (or inside his tether) the script's freeze is his own: nothing here changes it.
            var earth = FrozenEarthPrimal();
            Move(earth, 39727 + 300, 62620, 11684);
            var brain = new EarthPrimalBrain { Body = earth };
            brain.ReturnHomeIfListEmpty();
            Assert.That(earth.MaxSpeedBase, Is.EqualTo((short)0), "inside his tether nothing changes");
            var walking = FrozenEarthPrimal();
            walking.MaxSpeedBase = 180;
            new EarthPrimalBrain { Body = walking }.ReturnHomeWithLegs();
            Assert.That(walking.MaxSpeedBase, Is.EqualTo((short)180), "a speed he has is his");
        }

        [Test]
        public void ALeashedBossLeftAtSpeedZeroByItsScriptWalksHomeAtItsTemplateSpeed()
        {
            var earth = FrozenEarthPrimal();
            earth.NPCTemplate = new NpcTemplate { MaxSpeed = 250 };
            Assert.That(AutonomousTetherReset.LeashSpeed(earth), Is.EqualTo((short)250),
                "build b1b6846 sent him home at boss.MaxSpeed = 0");
            Assert.That(earth.MaxSpeedBase, Is.EqualTo((short)250));
            var still = FrozenEarthPrimal();
            still.NPCTemplate = new NpcTemplate { MaxSpeed = 0 };
            Assert.That(AutonomousTetherReset.LeashSpeed(still), Is.EqualTo((short)0), "a boss that never moves keeps 0");
            var walker = FrozenEarthPrimal();
            walker.MaxSpeedBase = 191;
            walker.NPCTemplate = new NpcTemplate { MaxSpeed = 250 };
            Assert.That(AutonomousTetherReset.LeashSpeed(walker), Is.EqualTo((short)191), "its own speed first");
        }

        [Test]
        public void MeleeAreNotSentToAPrimalOutsideItsTether()
        {
            var fire = Npc<FirePrimal>(39481, 63240, 11699, "Corrupt Primal of Fire");
            var earth = FrozenEarthPrimal();
            var water = Npc<WaterPrimal>(39547, 62071, 11688, "Corrupt Primal of Water");
            water.Dead = true;
            var live = new List<GameNPC> { fire, earth, water };
            var melee = Npc<Mob>(39500, 63100, 11699);
            Assert.That(AutonomousTetherReset.IsOutOfTether(earth), Is.True);
            var advice = EpicEncounterTactics.Decide(melee, fire, live, 0, false, 1_000, Hero);
            Assert.Multiple(() =>
            {
                Assert.That(advice.Action, Is.Not.EqualTo(EpicEncounterTactics.BotAction.Retarget),
                    "build b1b6846: 1,735 retargets to the Earth primal outside his tether; he killed 177");
                Assert.That(advice.Target, Is.Not.SameAs(earth));
            });
            // Back inside his tether he is the melee's again, as before.
            Move(earth, 39727 + 200, 62620, 11684);
            var back = EpicEncounterTactics.Decide(melee, fire, live, 0, false, 1_000, Hero);
            Assert.That((back.Action, back.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)earth)));
        }

        // ---- G-3: the front set in mid-air ------------------------------------------------------------------------

        private static RealmRaidDungeonRoute GalladoriaRoute(Vector3 front)
        {
            ConstructorInfo ctor = typeof(RealmRaidDungeonRoute).GetConstructors(Hidden).Single(c => c.GetParameters().Length == 7);
            return (RealmRaidDungeonRoute)ctor.Invoke([EpicEncounterTactics.GalladoriaRegion, null, front, front,
                new[] { "Olcasgean" }, null, eRealm.Hibernia]);
        }

        private static void Set(RealmRaidDungeonRoute route, string field, object value) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.SetValue(route, value);

        [Test]
        public void ASwitchToAMonsterWithNoFloorApproachKeepsTheOldFront()
        {
            Vector3 floor = new(39348, 62962, 11707);
            RealmRaidDungeonRoute route = GalladoriaRoute(floor);
            var water = Npc<WaterPrimal>(39547, 62071, 11688, "Corrupt Primal of Water");
            Set(route, "_target", water);
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute));
            var air = Npc<Mob>(38492, 62016, 12372, "Corrupt Primal of Air");
            // No navmesh here: the approach does not resolve and there is no floor under it, as for the Air primal 690 up.
            typeof(RealmRaidDungeonRoute).GetMethod("TurnOn", Hidden)!.Invoke(route, [air, Array.Empty<GameLiving>(), 1_000L, "test"]);
            Assert.Multiple(() =>
            {
                Assert.That(route.TargetName, Is.EqualTo("Corrupt Primal of Air"));
                Assert.That(route.Front, Is.EqualTo(floor), "build b1b6846: front=38492,62016,12372 approach=raw");
                Assert.That(logs.Lines.Any(l => l.StartsWith("REALM_EXPEDITION_TARGET_SWITCH ") &&
                                                l.Contains("front=39348,62962,11707 approach=unresolved-front-kept")), Is.True,
                    string.Join(" | ", logs.Lines));
                Assert.That(route.Destination, Is.EqualTo(floor));
            });
            var water2 = Npc<Mob>(39547, 62071, 11688, "a fighting primal");
            bool defeated = (bool)typeof(RealmRaidDungeonRoute).GetMethod("SwitchToHostile", Hidden)!
                .Invoke(route, [water2, new Vector3(water2.X, water2.Y, water2.Z + 690), false, 2_000L]);
            Assert.That(defeated, Is.False);
            Assert.That(route.Front, Is.EqualTo(floor), "the hostile switch keeps it too");
            typeof(RealmRaidDungeonRoute).GetMethod("SwitchToHostile", Hidden)!
                .Invoke(route, [air, new Vector3(39000, 62500, 11690), true, 3_000L]);
            Assert.That(route.Front, Is.EqualTo(new Vector3(39000, 62500, 11690)), "a resolved approach is the front");
        }

        [Test]
        public void TheSwitchFrontIsTheApproachThenTheFloorUnderThenTheOldFront()
        {
            Vector3 old = new(1, 2, 3), approach = new(10, 20, 30), under = new(10, 20, 5);
            Assert.That(RealmRaidDungeonRoute.SwitchFront(old, approach, true, under), Is.EqualTo(approach));
            Assert.That(RealmRaidDungeonRoute.SwitchFront(old, approach, false, under), Is.EqualTo(under));
            Assert.That(RealmRaidDungeonRoute.SwitchFront(old, approach, false, null), Is.EqualTo(old));
        }

        // ---- K-1: a return wave stuck with its help used up while the raid musters ----------------------------------

        [Test]
        public void AStuckWaveWithNoHelpLeftEndsWhileTheRaidMusters()
        {
            const RealmRaidReturnWave.Help none = RealmRaidReturnWave.Help.None;
            // Krondon at 11:38:50: helps=1, nobody waiting at the way in, the main body mustering (it can't come).
            var help = RealmRaidReturnWave.ChooseHelp(0, false, 1);
            Assert.That(help, Is.EqualTo(none));
            Assert.That(RealmRaidReturnWave.ReleasesStuckWave(help, mustering: true), Is.True,
                "build b1b6846: the wave held 19 raiders at step 14 while the Krevo Ricik muster stood at 6 of 44");
            Assert.That(RealmRaidReturnWave.ReleasesStuckWave(RealmRaidReturnWave.ChooseHelp(0, false, 2), true), Is.True);
            Assert.That(RealmRaidReturnWave.ReleasesStuckWave(help, mustering: false), Is.False, "not mustering: the stall clock decides");
            Assert.That(RealmRaidReturnWave.ReleasesStuckWave(RealmRaidReturnWave.Help.SendWaiting, true), Is.False,
                "help left: it is helped first");
        }

        // ---- the stall after a raid ends ------------------------------------------------------------------------------

        [Test]
        public void TheBrokenPartySweepEndsAFewPartiesATickAndReachesEveryOne()
        {
            // 16 raid parties released at once (epic-midgard at 12:00:03) among 40 healthy sessions.
            var sessions = Enumerable.Range(0, 56).Select(i => (Id: i, Broken: i % 7 < 2 || i >= 50)).ToList();
            int broken = sessions.Count(s => s.Broken);
            var ended = new HashSet<int>();
            bool Finish((int Id, bool Broken) s)
            {
                if (!s.Broken || !ended.Add(s.Id)) return false;
                return true;
            }
            int cap = AutonomousBotGroupCoordinator.BrokenSessionFinishesPerSweep;
            int cursor = 0, sweeps = 0;
            while (true)
            {
                int before = ended.Count;
                cursor = AutonomousBotGroupCoordinator.SweepBrokenSessions(sessions, cursor, Finish, cap);
                Assert.That(ended.Count - before, Is.LessThanOrEqualTo(cap), "never more than the cap in one tick");
                sweeps++;
                if (cursor < 0) break;
                Assert.That(sweeps, Is.LessThan(100));
            }
            Assert.That(ended.Count, Is.EqualTo(broken), "every broken party is ended");
            Assert.That(sweeps, Is.InRange((broken + cap - 1) / cap, broken / cap + 2), "about one tick for each two parties");
            Assert.That(AutonomousBotGroupCoordinator.SweepBrokenSessions(sessions, 0, Finish, cap), Is.EqualTo(-1),
                "nothing left: one pass, done");
            Assert.That(AutonomousBotGroupCoordinator.SweepBrokenSessions(new List<int>(), 5, _ => true, cap), Is.EqualTo(-1));
        }

        [Test]
        public void ThePartySweepGoesOnFromWhereItStoppedSoNoPartyWaitsForEver()
        {
            // Two sessions that always report a finish (a lead offered, not ended) must not starve the rest.
            var ids = Enumerable.Range(0, 10).ToList();
            var looked = new HashSet<int>();
            int cursor = 0;
            for (int tick = 0; tick < 10 && cursor >= 0; tick++)
                cursor = AutonomousBotGroupCoordinator.SweepBrokenSessions(ids, cursor, i => { looked.Add(i); return i < 2 || i == 5; }, 2);
            Assert.That(looked, Is.EquivalentTo(ids), "every session is looked at");
        }
    }
}
