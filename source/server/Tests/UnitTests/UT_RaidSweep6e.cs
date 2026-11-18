using System;
using System.Collections;
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
    /// Sweep 6e (owner's PC log, build b1b6846, 2026-11-18, 13:31-15:15): Aidon the Archwizard drawn past his tether by
    /// raiders standing outside his home (S-1), an attempt left open across his reset to full (S-2), epic trash on Crystal
    /// Cave's way in walked past (W-1), musters set aside while the raid walked to them in a return wave (T-1) and a stale
    /// stranded clock (M-1). Bosses, raiders and routes stand in a test region; no world tick, combat, spell or pathing runs.
    /// Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidSweep6e
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 6900;
        private EpicTestServerScope _server;

        // Aidon's catalog row as a test double (fight at home 1,100, his four illusions, keep one alive).
        public sealed class Wizard6e : GameEpicBoss
        {
            public bool Fighting = true;
            public byte Hp = 100;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => Hp;
        }
        public sealed class Illusion6e : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class Sentry6e : GameEpicNPC { public override bool IsAlive => true; }
        public sealed class Trash6e : GameNPC { public override bool IsAlive => true; }
        public sealed class Raider6e : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }

        // Aidon's spawn (31353,37634) and his numbers from the script and the catalog.
        private const int SpawnX = 31353, SpawnY = 37634, SpawnZ = 14873;
        private const int AidonTether = 1500, AidonAggro = 600, AidonHome = 1100;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 277, Name = "Sweep 6e test", Description = "Sweep 6e test", Mobs = [] });
            EpicEncounterTactics.Register(new("Wizard6e", "test/wizard6e", ["Illusion6e"], AidonHome, null, KeepAlive: 1));
            EpicEncounterTactics.ResetBlastForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterTactics.Unregister("Wizard6e");
            EpicEncounterTactics.ResetBlastForTests();
            _server.Dispose();
        }

        private T Npc<T>(int x, int y, int z, string name = null) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            if (name != null) typeof(GameObject).GetField("m_name", Hidden)!.SetValue(npc, name);
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new ArrayList());
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

        private static void GiveBrain(GameNPC npc, int aggro)
        {
            var brain = new StandardMobBrain { AggroRange = aggro };
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(npc, brain);
            brain.Body = npc;
        }

        private Wizard6e Aidon()
        {
            var aidon = Npc<Wizard6e>(SpawnX, SpawnY, SpawnZ, "Aidon the Archwizard");
            aidon.TetherRange = AidonTether;
            // (No field initialisers run on an uninitialised object.)
            aidon.Hp = 100;
            aidon.Fighting = true;
            GiveBrain(aidon, AidonAggro);
            return aidon;
        }

        private static float FromSpawn(Vector3 at) => Vector3.Distance(at, new Vector3(SpawnX, SpawnY, SpawnZ));

        // ---- S-1: the raid stands in Aidon's room ------------------------------------------------------------------------

        [Test]
        public void AidonsMusterStandsWellInsideHisHome()
        {
            // Build b1b6846: MusterStandOff(1,400, 600, reach 1,200) = 1,100, his fight-at-home edge (FINAL_ENGAGE at 31376,36534).
            float before = RealmRaidTetherReach.MusterStandOff(RealmRaidDungeonRoute.FinalMusterStandOff, AidonAggro,
                RealmRaidTetherReach.DamageReach(AidonTether));
            Assert.That(before, Is.EqualTo(1100f), "the old muster: at his home's edge");
            float now = RealmRaidTetherReach.MusterStandOff(RealmRaidDungeonRoute.FinalMusterStandOff, AidonAggro, AidonTether, AidonHome);
            Assert.Multiple(() =>
            {
                Assert.That(now, Is.LessThanOrEqualTo(800f), "about 800 from his spawn, well inside his 1,100 home");
                Assert.That(now, Is.GreaterThanOrEqualTo(AidonAggro + RealmRaidTetherReach.MusterInset), "still outside his 600 aggro");
                Assert.That(RealmRaidTetherReach.MusterReach(AidonTether, AidonHome, AidonAggro), Is.EqualTo(900),
                    "a muster point past 900 from his spawn is refused like one past his tether");
                // The real catalog row, through the same function the route uses.
                var row = EpicEncounterTactics.All.Single(t => t.Boss == "AidonTheArchwizard");
                var leash = EpicEncounterTactics.Leashes.Single(l => l.Boss == "AidonTheArchwizard");
                Assert.That(RealmRaidTetherReach.MusterStandOff(RealmRaidDungeonRoute.FinalMusterStandOff, AidonAggro, leash.TetherRange,
                    row.FightAtHomeRadius), Is.EqualTo(800f));
                // No home radius: unchanged.
                Assert.That(RealmRaidTetherReach.MusterStandOff(RealmRaidDungeonRoute.FinalMusterStandOff, AidonAggro, AidonTether, 0),
                    Is.EqualTo(before));
                Assert.That(RealmRaidTetherReach.MusterReach(0, 0, 0), Is.EqualTo(0), "no tether and no home: no limit");
                // Caer Sidi's home (2,700) leaves the 1,400 muster as it was.
                Assert.That(RealmRaidTetherReach.MusterStandOff(RealmRaidDungeonRoute.FinalMusterStandOff, 600, 3600, 2700),
                    Is.EqualTo(RealmRaidDungeonRoute.FinalMusterStandOff));
            });
        }

        [Test]
        public void AClericHealingATankFromPastAidonsHomeWalksBackIntoHisRoom()
        {
            var aidon = Aidon();
            var illusion = Npc<Illusion6e>(SpawnX + 300, SpawnY, SpawnZ);
            var live = new List<GameNPC> { aidon, illusion };
            var tank = Npc<Raider6e>(SpawnX, SpawnY - 200, SpawnZ, "Tank");
            tank.ObjectState = GameObject.eObjectState.Inactive; // (a realm set on an active object tells the players around it)
            tank.Realm = eRealm.Albion;
            tank.ObjectState = GameObject.eObjectState.Active;
            tank.TargetObject = aidon;
            aidon.TargetObject = tank;
            // Halaermys died 1,743 from his spawn; the cleric here heals the tank from 1,600.
            var cleric = Npc<Raider6e>(SpawnX, SpawnY - 1600, SpawnZ, "Cleric");
            cleric.TargetObject = tank;
            var advice = EpicEncounterTactics.StandInsideHome(cleric, aidon, live);
            Assert.That(advice, Is.Not.Null, "build b1b6846 had no rule for a raider he was not chasing");
            Assert.Multiple(() =>
            {
                Assert.That(advice.Value.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
                Assert.That(FromSpawn(advice.Value.StandAt!.Value), Is.LessThanOrEqualTo(900f).And.GreaterThan(850f));
                // A raider on one of his illusions out there too.
                var onIllusion = Npc<Raider6e>(SpawnX + 1400, SpawnY, SpawnZ);
                onIllusion.TargetObject = illusion;
                Assert.That(EpicEncounterTactics.StandInsideHome(onIllusion, aidon, live)?.Action,
                    Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
                // Inside his home already, or not in his fight: nothing.
                var inside = Npc<Raider6e>(SpawnX, SpawnY - 1000, SpawnZ);
                inside.TargetObject = tank;
                Assert.That(EpicEncounterTactics.StandInsideHome(inside, aidon, live), Is.Null);
                var passer = Npc<Raider6e>(SpawnX, SpawnY - 1600, SpawnZ);
                Assert.That(EpicEncounterTactics.StandInsideHome(passer, aidon, live), Is.Null, "not in his fight");
                // The one he chases leads him home instead (ThinkLeadHome's first rule).
                aidon.TargetObject = cleric;
                Assert.That(EpicEncounterTactics.StandInsideHome(cleric, aidon, live), Is.Null);
            });
        }

        [Test]
        public void MeleeBesideAidonAtTheEdgeOfHisHomeKeepFighting()
        {
            Assert.Multiple(() =>
            {
                Assert.That(EpicEncounterTactics.StandsBackInside(AidonHome, 1150, 120, 1050), Is.False, "beside him, he is inside");
                Assert.That(EpicEncounterTactics.StandsBackInside(AidonHome, 1150, 120, 1200), Is.True, "he is out too: both go in");
                Assert.That(EpicEncounterTactics.StandsBackInside(AidonHome, 1600, 900, 700), Is.True);
                Assert.That(EpicEncounterTactics.StandsBackInside(0, 5000, 900, 700), Is.False, "no home radius: no rule");
                Vector3 spawn = new(0, 0, 0);
                Assert.That(EpicEncounterTactics.HomeStand(spawn, new Vector3(0, 1000, 0), AidonHome), Is.Null);
                Assert.That(EpicEncounterTactics.HomeStand(spawn, new Vector3(0, -2000, 0), AidonHome), Is.EqualTo(new Vector3(0, -900, 0)));
            });
        }

        [Test]
        public void TheKeepAliveRuleYieldsToFightAtHomeWhileAidonIsOutOfHisHome()
        {
            var aidon = Aidon();
            var illusion = Npc<Illusion6e>(SpawnX + 300, SpawnY, SpawnZ);
            var live = new List<GameNPC> { aidon, illusion };
            // Eva at 14:18:48: StandOff fight-at-home, then "keep-alive add of Aidon: fight the boss".
            var eva = Npc<Raider6e>(SpawnX, SpawnY - 1250, SpawnZ, "Eva");
            eva.TargetObject = illusion;
            var tank = Npc<Raider6e>(SpawnX, SpawnY - 1300, SpawnZ, "Tank");
            Move(aidon, SpawnX, SpawnY - 1200, SpawnZ); // 1,200 from his spawn: past his home, inside his 1,500 tether
            aidon.TargetObject = tank;
            var advice = EpicEncounterTactics.Decide(eva, illusion, live, 0, false, 1_000L);
            Assert.That(advice.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff),
                "build b1b6846: Retarget onto Aidon out there (\"keep-alive add: fight the boss\")");
            Assert.That(FromSpawn(advice.StandAt!.Value), Is.LessThanOrEqualTo(900f), "and from inside his home");
            aidon.TargetObject = eva;
            Assert.That(EpicEncounterTactics.Decide(eva, illusion, live, 0, false, 1_000L).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.LeadHome), "the one he chases leads him back");
            // Home again: the keep-alive rule as before.
            Move(aidon, SpawnX, SpawnY - 500, SpawnZ);
            var home = EpicEncounterTactics.Decide(eva, illusion, live, 0, false, 1_000L);
            Assert.That((home.Action, home.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)aidon)));
        }

        // ---- S-2: an attempt ends when its encounter resets to full ------------------------------------------------------

        [Test]
        public void AnAttemptEndsWhenItsEncounterIsBackAtFullOutOfItsTetherWalkingHomeOrWithoutAggro()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(true, 100, 1, true, false, false), Is.True, "14:18:56, leashed at 1,598");
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(true, 100, 1, false, true, false), Is.True, "walking home");
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(true, 96, 60, false, false, true), Is.True, "nobody on his list");
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(true, 100, 1, false, false, false), Is.False,
                    "a heal in the fight (inside the tether, with aggro) is the fight's");
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(true, 100, 71, true, false, false), Is.False, "never below 70%");
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(true, 94, 1, true, false, false), Is.False, "not back at 95%");
                Assert.That(RealmRaidDungeonRoute.AttemptEndsOnReset(false, 100, 1, true, false, false), Is.False, "no attempt open");
            });
        }

        private static RealmRaidDungeonRoute SummonersRoute()
        {
            ConstructorInfo ctor = typeof(RealmRaidDungeonRoute).GetConstructors(Hidden).Single(c => c.GetParameters().Length == 7);
            Vector3 muster = new(31376, 36834, 14873);
            return (RealmRaidDungeonRoute)ctor.Invoke([(ushort)277, null, muster, muster, new[] { "Wizard6e" }, null, eRealm.Albion]);
        }

        private static void Set(RealmRaidDungeonRoute route, string field, object value) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.SetValue(route, value);
        private static object Get(RealmRaidDungeonRoute route, string field) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.GetValue(route);

        [Test]
        public void AidonLeashedFromOnePercentEndsTheAttemptAsAWipeWhileRaidersStillFightHisIllusions()
        {
            var aidon = Aidon();
            var illusion = Npc<Illusion6e>(SpawnX + 300, SpawnY, SpawnZ);
            var live = new List<GameNPC> { aidon, illusion };
            var raiders = new List<GameLiving>();
            for (int i = 0; i < 20; i++)
            {
                var r = Npc<Raider6e>(SpawnX + i * 10, SpawnY - 600, SpawnZ);
                r.Dead = i < 16; // 16 raiders were left at 1%; here 4 of 20 stand
                raiders.Add(r);
            }
            raiders[^1].TargetObject = illusion;
            // 14:18:56: leashed from 1,598, full health (his script), still "in combat" with the illusion fight.
            Move(aidon, SpawnX, SpawnY - 1598, SpawnZ);
            Assert.That(AutonomousTetherReset.IsOutOfTether(aidon), Is.True);
            RealmRaidDungeonRoute route = SummonersRoute();
            Set(route, "_target", aidon);
            Set(route, "_musterTarget", aidon);
            Set(route, "_musterEngaged", true);
            ((HashSet<GameLiving>)Get(route, "_engaged")).UnionWith(raiders);
            Set(route, "_attemptRaidSize", 20);
            Set(route, "_attemptLowestPercent", 1);
            Set(route, "_live", (IReadOnlyList<GameNPC>)live);
            MethodInfo check = typeof(RealmRaidDungeonRoute).GetMethod("EndAttemptIfReset", Hidden);
            Assert.That(check, Is.Not.Null, "build b1b6846 closed an attempt only once its encounter left combat");
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute));
            var wiped = (bool?)check!.Invoke(route, [60_000L]);
            Assert.Multiple(() =>
            {
                Assert.That(wiped, Is.True, "half or more fell: a wipe");
                Assert.That(Get(route, "_musterEngaged"), Is.False, "the attempt is closed");
                Assert.That(Get(route, "_regrouping"), Is.True, "the raid regroups at the muster; no wave walks into his room");
                Assert.That(route.FailedAttempts("Aidon the Archwizard"), Is.EqualTo(1));
                Assert.That(logs.Lines.Any(l => l.Contains("how=\"reset to full\"")), Is.True, string.Join(" | ", logs.Lines));
            });
            // Back in his room, in a fight, below full: no reset.
            Move(aidon, SpawnX, SpawnY - 300, SpawnZ);
            aidon.Hp = 60;
            Set(route, "_musterEngaged", true);
            Set(route, "_attemptLowestPercent", 40);
            Assert.That((bool?)check.Invoke(route, [61_000L]), Is.Null);
        }

        // ---- W-1: epic trash on the way in -------------------------------------------------------------------------------

        [Test]
        public void AnEpicMonsterThatIsNotABossCoveringTheWalkIsAGateCandidate()
        {
            // Avalon City (region 50), the sentries' spot on Crystal Cave's way in: aggro 200 across the walk.
            var walk = new List<Vector3> { new(23500, 37000, 8650), new(24000, 38300, 8670), new(24000, 40500, 8150) };
            var sentry = Npc<Sentry6e>(24050, 39000, 8400, "xanxicarian sentry");
            GiveBrain(sentry, 200);
            var off = Npc<Sentry6e>(27000, 39000, 8400, "xanxicarian sentry");
            GiveBrain(off, 200);
            var trash = Npc<Trash6e>(24050, 39100, 8400, "ordinary");
            GiveBrain(trash, 200);
            var boss = Npc<Wizard6e>(24100, 38000, 8650, "boss");
            GiveBrain(boss, 600);
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRouteGate.BlocksWalk(sentry, walk), Is.True,
                    "build b1b6846 took only a GameEpicBoss: the sentries were walked past (action=pass)");
                Assert.That(RealmRaidRouteGate.BlocksWalk(boss, walk), Is.True, "a boss as before");
                Assert.That(RealmRaidRouteGate.BlocksWalk(off, walk), Is.False, "epic trash off the walk is left alone");
                Assert.That(RealmRaidRouteGate.BlocksWalk(trash, walk), Is.False, "ordinary monsters are the walk's own fights");
            });
        }

        [Test]
        public void TheDetourSearchStartsALittleBeforeTheMonsterOnTheWalk()
        {
            var walk = new List<Vector3> { new(0, 0, 0), new(0, 1000, 0), new(0, 3000, 0) };
            Vector3[] corridor = RealmRaidRouteGate.CorridorBefore(walk, new Vector3(100, 2500, 0), 800);
            Assert.That(corridor, Is.EqualTo(new[] { new Vector3(0, 1700, 0), new Vector3(0, 3000, 0) }));
            Vector3[] fromStart = RealmRaidRouteGate.CorridorBefore(walk, new Vector3(0, 300, 0), 800);
            Assert.That(fromStart, Is.EqualTo(walk.ToArray()), "never before the walk's start");
            Assert.That(RealmRaidRouteGate.CorridorBefore([], Vector3.Zero, 800), Is.Empty);
        }

        // ---- T-1: a muster waits for a wave walking to it ----------------------------------------------------------------

        [Test]
        public void AMusterIsNotSetAsideWhileAMovingReturnWaveWalksToIt()
        {
            Vector3 muster = new(28288, 35959, 13532);
            var progress = new RealmRaidDungeonRoute.MusterProgress();
            long start = 0, now = RealmRaidDungeonRoute.MusterStalledMilliseconds;
            Assert.That(progress.Observe(0, 32, start), Is.False);
            Assert.That(progress.Observe(0, 32, now), Is.True, "gained nobody for 20 minutes");
            // Wave 4 at step 12/27, moved two minutes ago, walking to this muster.
            var moving = new RealmRaidDungeonRoute.WaveWalking(muster + new Vector3(200, 0, 0), now - 2 * 60_000, 56, 4);
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDungeonRoute.MusterWaitsForWave(muster, moving, now), Is.True,
                    "build b1b6846 set it aside with most of the raid walking to it");
                var stuck = moving with { LastMove = now - RealmRaidReturnWave.StuckMilliseconds };
                Assert.That(RealmRaidDungeonRoute.MusterWaitsForWave(muster, stuck, now), Is.False, "stuck 4 minutes: the stall applies");
                var elsewhere = moving with { Goal = muster + new Vector3(15000, 0, 0) };
                Assert.That(RealmRaidDungeonRoute.MusterWaitsForWave(muster, elsewhere, now), Is.False, "walking to another muster");
                Assert.That(RealmRaidDungeonRoute.MusterWaitsForWave(muster, null, now), Is.False, "no wave");
                Assert.That(RealmRaidDungeonRoute.MusterWaitsForWave(muster, moving with { Members = 0 }, now), Is.False);
            });
        }

        // ---- M-1: a stale stranded clock -----------------------------------------------------------------------------

        [Test]
        public void ABossBackInsideItsTetherGetsTheFullGraceOnItsNextExcursion()
        {
            var aidon = Aidon();
            Move(aidon, SpawnX, SpawnY - 1600, SpawnZ);
            Assert.That(AutonomousTetherReset.LeashDue(aidon, false, 0), Is.False, "stranded clock starts");
            Assert.That(AutonomousTetherReset.LeashDue(aidon, false, 10_000), Is.False);
            // He walks back inside (a raider targets him there).
            Move(aidon, SpawnX, SpawnY - 600, SpawnZ);
            AutonomousTetherReset.BackInsideTether(aidon);
            // Out again a minute later: a new excursion with its own 30 s.
            Move(aidon, SpawnX, SpawnY - 1600, SpawnZ);
            Assert.That(AutonomousTetherReset.LeashDue(aidon, false, 70_000), Is.False,
                "build b1b6846 kept the old clock and leashed him in the first second (14:18:56)");
            Assert.That(AutonomousTetherReset.LeashDue(aidon, false, 95_000), Is.False);
            Assert.That(AutonomousTetherReset.LeashDue(aidon, false, 100_000), Is.True, "30 s stranded: leashed");
            // Without the return inside, the old clock leashes at once (what happened).
            var other = Aidon();
            Move(other, SpawnX, SpawnY - 1600, SpawnZ);
            AutonomousTetherReset.LeashDue(other, false, 0);
            Assert.That(AutonomousTetherReset.LeashDue(other, false, 70_000), Is.True);
        }
    }
}
