using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// The 1.2 raid plans for the epic encounters the overnight PC playtest (build e3b8238) lost: the Crypt Lord's court
    /// timed to his patrol, the Host's linked monsters, ambush and real copy, Lich Lord Ilron's lights and eyes, Icelord
    /// Agmundr's linked hrimthursa and cold blast, Spindler Broodmother's burn, add parties and mesmerize spread, the
    /// perched griffon icewings, and the route rules that mapped a monster fighting the raid onto the raid's own target.
    /// The tests stand bosses, adds and raiders in a test region and play the rules; no world tick, combat, spell or
    /// pathing runs. Nothing here was played in game: tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicRaidPlans12
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 2000;
        private EpicTestServerScope _server;
        private Func<GameNPC, int> _lordNext, _hostNext;
        private Func<GameNPC, bool> _agmundrCasting;
        private Func<GameNPC, int?> _floorUnder;

        public sealed class TestCryptLord : CryptLord
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        public sealed class TestHost : Host
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        // GetUninitializedObject runs no field initializer: every flag of a test double starts false (or 0).
        public sealed class TestIlron : DOL.GS.Scripts.LichLordIlron
        {
            public bool Resting;
            public override bool IsAlive => true;
            public override bool InCombat => !Resting;
        }
        public sealed class TestImage : IlronImages
        {
            public override bool IsAlive => true;
        }
        public sealed class TestAgmundr : Agmundr
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class TestSpindler : SpindlerBroodmother
        {
            public bool Resting;
            public byte Hurt;
            public override bool IsAlive => true;
            public override bool InCombat => !Resting;
            public override byte HealthPercent => (byte)(100 - Hurt);
        }
        public sealed class Newborn : SBAdds
        {
            public override bool IsAlive => true;
        }
        public sealed class TestEasmarach : Easmarach
        {
            public override bool IsAlive => true;
            public override bool InCombat => false;
        }
        /// <summary>Carries the last room's class name (Galladoria's route rule goes by it).</summary>
        public sealed class EarthPrimal : GameNPC
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public class Mob : GameNPC
        {
            public bool Dead, Fighting;
            public byte Hurt;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => (byte)(100 - Hurt);
        }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        private static readonly int Warrior = (int)eCharacterClass.Warrior, Wizard = (int)eCharacterClass.Wizard,
            Druid = (int)eCharacterClass.Druid, Hero = (int)eCharacterClass.Hero;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(60);
            EpicEncounterMechanics.ResetPerEncounter();
            _lordNext = T.CryptLordNextPoint;
            _hostNext = T.HostNextPoint;
            _agmundrCasting = T.AgmundrCasting;
            _floorUnder = T.FloorUnder;
            T.ForgetPerchFloors();
            T.ResetAddPartiesForTests();
        }

        [TearDown]
        public void TearDown()
        {
            T.CryptLordNextPoint = _lordNext;
            T.HostNextPoint = _hostNext;
            T.AgmundrCasting = _agmundrCasting;
            T.FloorUnder = _floorUnder;
            T.ForgetPerchFloors();
            EpicEncounterMechanics.ResetPerEncounter();
            _server.Dispose();
        }

        private void UseRegion(ushort id)
        {
            _region = new Region(new RegionData { Id = id, Name = "Raid plans test", Description = "Raid plans test", Mobs = [] });
            _region.Zones.Add(new Zone(_region, (ushort)(id * 10), "Raid plans test", 0, 0, 65536, 65536, (ushort)(id * 10 + 1),
                false, 0, false, 0, 0, 0, 0, 0));
        }

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            var caches = typeof(GameObject).GetField("_objectsInRadiusCaches", Hidden)!;
            caches.SetValue(npc, Activator.CreateInstance(caches.FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(npc, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(npc, new PropertyCollection());
            npc.effectListComponent = EffectListComponent.Create(npc);
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            npc.SubZoneObject = new SubZoneObject(npc);
            Zone zone = _region.Zones[0];
            Assert.That(zone.AddObject(npc), Is.True);
            SubZoneObject sub = npc.SubZoneObject;
            sub.DestinationSubZone.AddObject(sub.Node);
            zone.OnObjectAddedToZone();
            sub.OnSubZoneTransition();
            return npc;
        }

        private static void Move(GameNPC npc, float x, float y, float z)
        {
            npc.X = (int)x; npc.Y = (int)y; npc.Z = (int)z;
            npc.movementComponent.ForceUpdatePosition();
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = System.IO.Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        // ---- a patrol's next seconds -------------------------------------------------------------------------------

        [Test]
        public void APatrolIsWalkedRoundItsLoopAtItsSpeed()
        {
            Vector3[] loop = [new(0, 0, 0), new(1000, 0, 0), new(1000, 1000, 0)];
            Vector3[] path = T.PredictPatrol(new(0, 0, 0), loop, 1, 100, 15);
            Assert.That(path.Length, Is.EqualTo(16), "now and one point a second");
            Assert.That(path[5].X, Is.EqualTo(500).Within(0.5));
            Assert.That(path[10], Is.EqualTo(new Vector3(1000, 0, 0)));
            Assert.That(path[15].Y, Is.EqualTo(500).Within(0.5), "then on to the next point");
            Vector3[] round = T.PredictPatrol(new(1000, 1000, 0), loop, 0, 100, 30);
            Assert.That(T.ClosestApproach(round, new(0, 0, 0)), Is.LessThan(100), "round the loop back to its first point (one step a second)");
            Assert.That(T.PredictPatrol(new(5, 5, 5), [new(5, 5, 5)], 0, 100, 3).Distinct().Single(), Is.EqualTo(new Vector3(5, 5, 5)),
                "a loop of one spot: he stays there");
            Assert.That(T.NextUnreached([true, true, false, false]), Is.EqualTo(2));
            Assert.That(T.NextUnreached([true, true]), Is.EqualTo(0), "all reached: the loop starts again");
        }

        [Test]
        public void TheScriptedLoopsAreThePatrolsTheRaidPredicts()
        {
            // OpenDAoC-Core c2a55c5b9 / cadc7b8c0 (1.2 port): the Crypt Lord and the Host walk scripted loop paths, their points
            // starting at the spawn; the predictions read them in the old WalkTo chains' order (point1 first, the spawn last).
            Assert.That(T.CryptLordPatrol, Is.EqualTo(new Vector3[]
            {
                new(28461, 40166, 15373), new(28494, 43144, 15373), new(26751, 43111, 15373), new(26741, 40147, 15373),
                new(24891, 40139, 15372),
            }));
            Assert.That(T.HostPatrol.Length, Is.EqualTo(52), "point1 to point51 and his spawn");
            Assert.That(T.HostPatrol[0], Is.EqualTo(new Vector3(26749, 29730, 17871)));
            Assert.That(T.HostPatrol[31], Is.EqualTo(T.HostAmbush), "point32 is still the ambush");
            Assert.That(T.HostPatrol[^1], Is.EqualTo(new Vector3(26995, 29733, 17871)), "his spawn last");
            Assert.That(T.CryptLordWalkSpeed, Is.EqualTo(100));
            Assert.That(T.HostWalkSpeed, Is.EqualTo(100));

            var lord = Npc<TestCryptLord>(24891, 40139, 15372);
            Assert.That(lord.CanMoveOnPath, Is.False);
            Assert.That(T.CryptLordNextPoint(lord), Is.EqualTo(0), "no path yet: point1");
            lord.SetScriptedPath(DOL.GS.Movement.MovementMgr.CreatePath(DOL.Database.EPathType.Loop, CryptLord.PATROL_SPEED, CryptLord.PatrolPoints));
            Assert.That(lord.CanMoveOnPath, Is.True, "a scripted loop is patrolled like a database path");
            Assert.That(lord.CurrentPathPoint, Is.SameAs(lord.ScriptedPath));
            Assert.That(T.CryptLordNextPoint(lord), Is.EqualTo(4), "at his spawn: the loop's last point");
            lord.CurrentPathPoint = lord.ScriptedPath.Next.Next;
            Assert.That(T.CryptLordNextPoint(lord), Is.EqualTo(1), "walking to point2");
            Assert.That(lord.CurrentPathPoint.Type, Is.EqualTo(DOL.Database.EPathType.Loop));
            Assert.That(lord.CurrentPathPoint.MaxSpeed, Is.EqualTo(CryptLord.PATROL_SPEED));

            var host = Npc<TestHost>(26995, 29733, 17871);
            host.SetScriptedPath(DOL.GS.Movement.MovementMgr.CreatePath(DOL.Database.EPathType.Loop, Host.PATROL_SPEED, Host.PatrolPoints));
            var point = host.ScriptedPath;
            for (int i = 0; i < 32; i++) point = point.Next;
            host.CurrentPathPoint = point;
            Assert.That(T.HostNextPoint(host), Is.EqualTo(31), "walking to point32");
            Assert.That(new Vector3(point.X, point.Y, point.Z), Is.EqualTo(T.HostAmbush));
            host.SetScriptedPath(null);
            Assert.That(host.CanMoveOnPath, Is.False);
        }

        // ---- the Crypt Lord's court ---------------------------------------------------------------------------------

        private (TestCryptLord Lord, Mob Court, List<GameNPC> Live) CryptLordRoom(int courtX, int courtY)
        {
            var lord = Npc<TestCryptLord>(26567, 40143, 15372);
            var court = Npc<Mob>(courtX, courtY, 15373);
            court.Name = "dark light";
            court.PackageID = T.CryptLordCourt;
            return (lord, court, new List<GameNPC> { lord, court });
        }

        [Test]
        public void TheCryptLordsCourtIsClearedFirstAndHeWaits()
        {
            var (lord, court, live) = CryptLordRoom(29714, 41493);
            var stranger = Npc<Mob>(29700, 41500, 15373);
            live.Add(stranger);
            Assert.That(T.ClearFirstOwner(court, live), Is.SameAs(lord), "a court monster dies before him");
            Assert.That(T.ClearFirstOwner(stranger, live), Is.Null, "a monster outside his court is not his");
            Assert.That(T.ClearFirstOwner(lord, live), Is.Null);
            Assert.That(T.RouteTarget(court, live, default, out _), Is.SameAs(court), "the court is fought, no more as his fight (1.2)");
            lord.Dead = true;
            Assert.That(T.ClearFirstOwner(court, live), Is.Null, "he is dead: the court is trash");
        }

        [Test]
        public void ACourtMonsterIsPulledOnlyWhileHisPatrolKeepsClearOfIt()
        {
            // A court monster by his patrol's first corner (28461,40166): he walks there from his path's spawn point.
            var (lord, court, live) = CryptLordRoom(28513, 39881);
            Move(lord, 26746, 42500, 15373);
            T.CryptLordNextPoint = _ => 3;
            Assert.That(T.PullWait(court, live, 1_000), Is.Null, "walking down the far side of his rectangle: 1,700 and more off");
            Move(lord, 24891, 40139, 15372);
            T.CryptLordNextPoint = _ => 0;
            Assert.That(T.PullWait(court, live, 1_000), Does.Contain("patrol comes within"), "he is on his way to it");
            // Past it, walking away up the far leg.
            Move(lord, 28490, 42300, 15373);
            T.CryptLordNextPoint = _ => 1;
            Assert.That(T.PullWait(court, live, 1_000), Is.Null, "walking away from it");
            Assert.That(T.CourtPullApproach(new(28490, 42300, 15373), 1, new(28513, 39881, 15373)), Is.GreaterThanOrEqualTo(T.CourtPullClearance));
            // In his fight there is nothing to time, and a monster that fights is never held.
            Move(lord, 27200, 40150, 15373);
            T.CryptLordNextPoint = _ => 0;
            lord.Fighting = true;
            Assert.That(T.PullWait(court, live, 1_000), Is.Null);
            lord.Fighting = false;
            court.Fighting = true;
            Assert.That(T.PullWait(court, live, 1_000), Is.Null);
        }

        [Test]
        public void TheHardestCourtMonsterStillHasAPullWindow()
        {
            // The middle of his rectangle (a forerunner at 27776,41241): open only while he walks down to point4 and on
            // towards his path's spawn point, about 12% of his 132 s loop.
            Vector3 middle = new(27776, 41241, 15373);
            Assert.That(T.CourtPullApproach(new(26741, 40500, 15373), 3, middle), Is.GreaterThanOrEqualTo(T.CourtPullClearance));
            Assert.That(T.CourtPullApproach(new(26741, 41300, 15373), 3, middle), Is.LessThan(T.CourtPullClearance), "beside it now");
            Assert.That(T.CourtPullApproach(new(24891, 40139, 15372), 0, middle), Is.LessThan(T.CourtPullClearance),
                "he would pass below it within 35 s");
        }

        [Test]
        public void InHisFightCastersTakeHisCourtAndMeleeHim()
        {
            var (lord, court, live) = CryptLordRoom(27000, 40500);
            lord.Fighting = true;
            court.Fighting = true;
            var wizard = Npc<Raider>(26800, 40300, 15373);
            var a = T.DungeonAdvice(new(wizard, lord, live, 0, true, 1_000, Wizard));
            Assert.That((a?.Action, a?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)court)));
            Assert.That(T.DungeonAdvice(new(wizard, lord, live, 0, false, 1_000, Warrior)), Is.Null, "melee fight him");
            court.Fighting = false;
            Assert.That(T.DungeonAdvice(new(wizard, lord, live, 0, true, 1_000, Wizard)), Is.Null, "no court in his fight: everyone on him");
        }

        /// <summary>Carries Queen Kula's class name (her frost circle's muster rule goes by it).</summary>
        public sealed class QueenKula : Mob { }

        [Test]
        public void NoMusterLiesInQueenKulasFrostCircle()
        {
            // Owner's PC, build ccba364, 2026-11-16: the muster for her stood at 34116,55807, 288 from the circle's middle; the
            // raiders gathering there drew the twins out to it (RealmRaidTetherReach).
            var kula = Npc<QueenKula>(34136, 57202, 12025);
            List<GameNPC> live = [kula];
            var dangers = RealmRaidDungeonRoute.RuleMusterDangers(live).ToList();
            Assert.That(dangers.Any(d => d.Name == kula.Name && d.At == T.KulaFrostPoint && d.Radius == T.KulaFrostMusterRadius), Is.True);
            Assert.That(RealmRaidDungeonRoute.InBossDanger(new(34116, 55807, 11907), dangers), Is.True, "the 11/16 muster is refused");
            Assert.That(RealmRaidDungeonRoute.InBossDanger(new(34800, 56150, 11900), dangers), Is.False, "east of the circle is clear");
            kula.Fighting = true;
            Assert.That(RealmRaidDungeonRoute.RuleMusterDangers(live), Is.Empty, "asked only while she is out of combat, as every rule's");
            kula.Fighting = false;
            kula.Dead = true;
            Assert.That(RealmRaidDungeonRoute.RuleMusterDangers(live), Is.Empty, "dead, no circle");
        }

        [Test]
        public void NoMusterLiesByTheCryptLordsPatrolOrHisCourt()
        {
            // Owner's PC, build 1dc66b6, 2026-11-15: his muster was 27,960,40,280, 115 from his patrol and 194 from a court
            // reckoned soul at 28,112,40,159; he and his court walked into it twice.
            var (lord, court, live) = CryptLordRoom(28112, 40159);
            Vector3 oldMuster = new(27960, 40280, 15375);
            var dangers = RealmRaidDungeonRoute.RuleMusterDangers(live).ToList();
            Assert.That(RealmRaidDungeonRoute.InBossDanger(oldMuster, dangers), Is.True, "the 03:00 muster is in his patrol's reach");
            Assert.That(dangers.Where(d => d.Name == lord.Name).Select(d => d.Radius).Distinct(), Is.EqualTo(new[] { (float)T.CryptLordMusterClearance }));
            Assert.That(T.CryptLordMusterClearance, Is.GreaterThan(T.CryptLordCourtLink), "a court fight at the muster stays out of his link");
            // Every point of his loop, the closing leg too, is covered.
            foreach (Vector3 corner in T.CryptLordPatrol)
                Assert.That(T.CryptLordPatrolSamples.Min(p => Vector3.Distance(p, corner)), Is.LessThan(1));
            Vector3 closing = Vector3.Lerp(T.CryptLordPatrol[^1], T.CryptLordPatrol[0], 0.5f);
            Assert.That(T.CryptLordPatrolSamples.Min(p => Vector3.Distance(p, closing)), Is.LessThanOrEqualTo(T.CryptLordPatrolSampleStep));
            // East of his rectangle, where the raid came from, a muster is clear of him (and of the court monster).
            Assert.That(RealmRaidDungeonRoute.InBossDanger(new(29700, 40700, 15375), dangers), Is.False);
            // The court monster's own spot.
            Assert.That(dangers.Any(d => d.Name == court.Name && d.Radius == T.CourtMusterClearance), Is.True);
            // A muster walks back along the walk to the raid until it is clear (SafeMusterAlong).
            Vector3[] walk = [new(28112, 40400, 15375), new(31000, 40700, 15375)];
            Vector3? safe = RealmRaidDungeonRoute.SafeMusterAlong(walk, new(28112, 40159, 15375), RealmRaidDungeonRoute.FinalMusterStandOff, dangers, null);
            Assert.That(safe, Is.Not.Null);
            Assert.That(RealmRaidDungeonRoute.InBossDanger(safe.Value, dangers), Is.False);
            Assert.That(T.CryptLordPatrolSamples.Min(p => Vector3.Distance(p, safe.Value)), Is.GreaterThan(T.CryptLordMusterClearance));
            // In a fight he walks no loop; dead, his court is trash.
            lord.Fighting = true;
            Assert.That(RealmRaidDungeonRoute.RuleMusterDangers(live).Any(d => d.Name == lord.Name), Is.False);
            Assert.That(RealmRaidDungeonRoute.RuleMusterDangers(live).Any(d => d.Name == court.Name), Is.False, "his fight is on");
            lord.Fighting = false;
            lord.Dead = true;
            Assert.That(RealmRaidDungeonRoute.RuleMusterDangers(live), Is.Empty);
        }

        [Test]
        public void TheCryptLordIsNotPulledAgainWhileHisCourtLives()
        {
            var (lord, court, live) = CryptLordRoom(29714, 41493);
            Assert.That(T.ClearsAgainBeforePull(lord), Is.True, "after a skirmish or a wipe his court is cleared before he is pulled");
            Assert.That(T.ClearFirstOwner(court, live), Is.SameAs(lord));
            Assert.That(T.ClearsAgainBeforePull(court), Is.False);
            var ilron = Npc<TestIlron>(0, 0, 0);
            Assert.That(T.ClearsAgainBeforePull(ilron), Is.False, "only where a rule says so (and in Darkness Falls, as before)");
        }

        // ---- build 5378dbe (owner's PC, 2026-11-17): the walk from the muster, the re-clear, CC immunity -------------------

        private static readonly Vector3 CryptLordCorner = new(28461, 40166, 15373);
        private static readonly Vector3 WesternForerunner = new(26744, 39884, 15373);
        /// <summary>The raid's walk from its muster (29617,41288) to the western forerunner, by 28081,40298 (MUSTER_GUARDED).</summary>
        private static readonly Vector3[] ForerunnerWalk = [new(29617, 41288, 15375), new(28081, 40298, 15375), WesternForerunner];

        [Test]
        public void HisPatrolMustKeepClearOfTheWalkFromTheMusterNotOnlyOfTheCourtMonster()
        {
            // At his corner P1 heading north to P2: 1,740 from the forerunner and walking away, so the old window was clear...
            Assert.That(T.CourtPullApproach(CryptLordCorner, 1, WesternForerunner), Is.GreaterThanOrEqualTo(T.CourtPullClearance));
            Assert.That(T.CourtPullWait(CryptLordCorner, 1, WesternForerunner, null), Is.Null, "the 5378dbe check: a \"clean\" pull");
            // ...but the walk from the muster crosses his leg P1-P2 (28461,40543) and passes 400 from the corner: he aggroes (400).
            Assert.That(T.DistanceToWalk(CryptLordCorner, ForerunnerWalk), Is.LessThan(T.CourtWalkClearance));
            Assert.That(T.CourtWalkApproach(CryptLordCorner, 1, ForerunnerWalk), Is.LessThan(T.CryptLordWalkAggro));
            Assert.That(T.CourtPullWait(CryptLordCorner, 1, WesternForerunner, ForerunnerWalk), Does.Contain("the raid's walk from the muster"));
            // The rule wires it: with the walk, the route's PullWait for the court monster waits.
            var (lord, court, live) = CryptLordRoom((int)WesternForerunner.X, (int)WesternForerunner.Y);
            Move(lord, CryptLordCorner.X, CryptLordCorner.Y, CryptLordCorner.Z);
            T.CryptLordNextPoint = _ => 1;
            Assert.That(T.PullWait(court, live, 1_000), Is.Null, "no muster yet: the court check alone, as before");
            Assert.That(T.PullWait(court, live, 1_000, ForerunnerWalk), Does.Contain("the raid's walk from the muster"));
            Assert.That(T.CourtPullLine(court, live, ForerunnerWalk), Does.Contain("lord=28461,40166").And.Contain("walkClosest="));
        }

        [Test]
        public void TheWalkWindowStillOpensOnHisFarSide()
        {
            // Past the walk on his east leg, walking north away from it, then along the far side: clear of both.
            Vector3 east = new(28461, 41300, 15373);
            Assert.That(T.CourtPullWait(east, 1, WesternForerunner, ForerunnerWalk), Is.Null);
            Assert.That(T.CourtWalkHorizonSeconds(ForerunnerWalk), Is.GreaterThan(T.CourtPullHorizonSeconds), "the walk's time comes on top");
            // Somewhere on his loop the window opens (else the 20-minute hold lapse would pull without any timing).
            Vector3[] loop = T.PredictPatrol(T.CryptLordPatrol[^1], T.CryptLordPatrol, 0, T.CryptLordWalkSpeed, T.CryptLordLoopSeconds());
            int open = 0;
            for (int s = 0; s < loop.Length; s++)
            {
                // The point he walks to from that spot: the first loop corner ahead of him on the prediction.
                int next = NextCorner(loop[s]);
                if (T.CourtPullWait(loop[s], next, WesternForerunner, ForerunnerWalk) == null) open++;
            }
            Assert.That(open, Is.GreaterThanOrEqualTo(5), $"open {open} of {loop.Length} s");
            TestContext.Out.WriteLine($"window open {open} of {loop.Length} s of his loop");
        }

        /// <summary>The loop corner he walks to from a spot on his loop (the leg it lies on).</summary>
        private static int NextCorner(Vector3 p)
        {
            Vector3[] c = T.CryptLordPatrol;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < c.Length; i++)
            {
                Vector3 a = c[(i - 1 + c.Length) % c.Length], b = c[i];
                float d = T.DistanceToWalk(p, [a, b]);
                if (d < bestD - 0.01f && Vector3.Distance(p, b) > 0.5f) { bestD = d; best = i; }
            }
            return best;
        }

        [Test]
        public void AgmundrsPackIsClearedAgainBeforeHeIsPulledAgain()
        {
            UseRegion(160);
            var agmundr = Npc<TestAgmundr>(15037, 50114, 12408);
            var depredator = Npc<Mob>(16418, 49908, 12059); depredator.PackageID = T.AgmundrLinkedPackage;
            var seer = Npc<Mob>(17208, 50280, 12059); seer.PackageID = T.AgmundrLinkedPackage;
            var stranger = Npc<Mob>(16000, 49000, 12059);
            var live = new List<GameNPC> { agmundr, depredator, seer, stranger };
            Assert.That(T.ClearsAgainBeforePull(agmundr), Is.True, "PC 5378dbe: attempt 2 met all six of his hrimthursa");
            Assert.That(T.ClearsAgainBeforePull(depredator), Is.False);
            // The route's re-clear before the pull (RealmRaidDungeonRoute.ReclearBeforePull): the nearest living pack member.
            Vector3 front = new(15210, 50014, 12408);
            Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(agmundr, live, _ => true, front), Is.SameAs(depredator));
            Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(agmundr, live, n => n != depredator, front), Is.SameAs(seer),
                "one the route set aside is retried later");
            depredator.Dead = true; seer.Dead = true;
            Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(agmundr, live, _ => true, front), Is.Null, "his pack is dead: he is pulled");
            depredator.Dead = false;
            agmundr.Fighting = true;
            Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(agmundr, live, _ => true, front), Is.Null, "his fight is on");
            string route = Source("bots", "autonomous", "RealmRaidDungeonRoute.cs");
            Assert.That(route, Does.Contain("EpicEncounterTactics.ClearsAgainBeforePull(_target)) &&"));
            Assert.That(route, Does.Contain("ReclearBeforePull(_target, _live, n => _routeRetry.GetValueOrDefault(n) <= now, Front, ReclearFree(now)) is { } back"));
        }

        [Test]
        public void NoMezOrStunGoesOnTheCcImmuneCryptLord()
        {
            var (lord, _, _) = CryptLordRoom(29714, 41493);
            Assert.That(lord.HasAbility(Abilities.CCImmunity), Is.True);
            Assert.That(DOL.AI.Brain.BotBrain.RefusesAddControl(true, true, false, false, true), Is.True, "mez");
            Assert.That(DOL.AI.Brain.BotBrain.RefusesAddControl(true, false, false, false, true), Is.True, "stun");
            Assert.That(DOL.AI.Brain.BotBrain.RefusesAddControl(false, true, false, false, false), Is.False, "a mez on a plain add");
            Assert.That(DOL.AI.Brain.BotBrain.RefusesAddControl(false, false, false, false, true), Is.False, "a protecting stun");
            Assert.That(DOL.AI.Brain.BotBrain.RefusesAddControl(false, false, false, false, false), Is.True, "a stun protects nobody");
            Assert.That(DOL.AI.Brain.BotBrain.RefusesAddControl(false, true, true, false, true), Is.True, "mez immune now");
            string control = Source("bots", "BotBrain.PveAddControl.cs");
            Assert.That(control, Does.Contain("bool ccImmune = add.HasAbility(Abilities.CCImmunity);"));
        }

        // ---- a route rule never maps a fighting monster onto the raid's target ---------------------------------------

        [Test]
        public void AMonsterFightingTheRaidIsAnsweredNotMappedOntoTheTarget()
        {
            UseRegion(T.GalladoriaRegion);
            var easmarach = Npc<TestEasmarach>(37918, 50523, 9865);
            var primal = Npc<EarthPrimal>(36000, 54000, 9900);
            var live = new List<GameNPC> { easmarach, primal };
            if (EpicEncounterMechanics.AllowDisplacementFor(easmarach))
                Assert.That(T.RouteTarget(primal, live, default, out _), Is.SameAs(easmarach), "at rest it gives way to him, as before");
            primal.Fighting = true;
            Assert.That(T.RouteTarget(primal, live, default, out _), Is.SameAs(primal),
                "fighting the raid it is fought (PC log e3b8238: \"Easmarach -> Easmarach\" 208 times)");
            Assert.That(T.AllDungeonRules.Single(r => r.Encounter == nameof(ApocInitializator)).RoutesInCombat, Is.True,
                "the barred room still holds a raid fighting in it");
        }

        // ---- the Host ------------------------------------------------------------------------------------------------

        private (TestHost Real, TestHost Copy, List<GameNPC> Live) Hosts(Vector3 at)
        {
            var real = Npc<TestHost>((int)at.X, (int)at.Y, (int)at.Z);
            real.PackageID = T.HostRealPackage;
            var copy = Npc<TestHost>((int)at.X + 20, (int)at.Y, (int)at.Z);
            copy.PackageID = "HostCopy3";
            return (real, copy, new List<GameNPC> { real, copy });
        }

        [Test]
        public void EveryRaiderFightsTheRealHost()
        {
            var (real, copy, live) = Hosts(T.HostAmbush);
            var raider = Npc<Raider>(25700, 30800, 15119);
            var a = T.DungeonAdvice(new(raider, copy, live, 0, false, 1_000, Warrior));
            Assert.That((a?.Action, a?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)real)));
            Assert.That(T.DungeonAdvice(new(raider, real, live, 0, false, 1_000, Warrior)), Is.Null);
            copy.Fighting = real.Fighting = true;
            Assert.That(T.RouteTarget(copy, live, default, out string why), Is.SameAs(real), "in combat too: all eight fight together");
            Assert.That(why, Does.Contain("real one"));
            Assert.That(T.MusterAnchor(copy), Is.EqualTo(T.HostAmbush));
            Assert.That(T.MusterAnchor(raider), Is.Null);
        }

        [Test]
        public void TheHostsLinkedMonstersDieFirstWhileTheHostsPatrolElsewhere()
        {
            // The Hosts on the top floor; an archlich on the bottom one (1,375 below).
            var (real, copy, live) = Hosts(new(26180, 30241, 17871));
            var archlich = Npc<Mob>(26661, 30680, 15130);
            archlich.Name = "archlich";
            archlich.PackageID = T.HostLinkedPackage;
            live.Add(archlich);
            T.HostNextPoint = _ => 2;
            Assert.That(T.ClearFirstOwner(archlich, live), Is.SameAs(real));
            Assert.That(T.PullWait(archlich, live, 1_000), Is.Null, "the Hosts are a floor away");
            Assert.That(T.PullWait(copy, live, 1_000), Does.Contain("linked monsters"), "the Hosts wait while it lives");
            // The Hosts coming down the ramp to the bottom floor.
            Move(real, 25711, 29995, 15357); Move(copy, 25711, 29995, 15357);
            T.HostNextPoint = _ => 28;
            Assert.That(T.PullWait(archlich, live, 1_000), Does.Contain("patrol comes within"));
            archlich.Fighting = true;
            Assert.That(T.PullWait(archlich, live, 1_000), Is.Null, "a fight on is never held");
        }

        [Test]
        public void TheRaidWaitsAtItsAmbushForTheRealHost()
        {
            var (real, copy, live) = Hosts(new(26749, 29730, 17871));
            T.HostNextPoint = _ => 1;
            Assert.That(T.PullWait(real, live, 1_000), Does.Contain("ambush"), "on the top floor: the raid waits");
            // Point 31 to 32 is 1,300 long: from point 31 he reaches the ambush (point 32) in 13 s.
            Move(real, 24729, 29725, 15119); Move(copy, 24729, 29725, 15119);
            T.HostNextPoint = _ => 31;
            Assert.That(T.PullWait(real, live, 1_000), Is.Null, "he comes by within the window: in");
            Assert.That(T.PullWait(copy, live, 1_000), Is.Null, "whichever copy the raid musters for");
            Move(real, T.HostAmbush.X, T.HostAmbush.Y, T.HostAmbush.Z);
            T.HostNextPoint = _ => 32;
            Assert.That(T.PullWait(real, live, 1_000), Is.Null, "at the ambush now");
        }

        // ---- Lich Lord Ilron -------------------------------------------------------------------------------------------

        [Test]
        public void IlronsLightsAndEyesAreClearedFirstAndKilledFirstInHisFight()
        {
            var ilron = Npc<TestIlron>(36385, 37593, 17006);
            var eye = Npc<Mob>(35929, 37566, 17006); eye.Name = "lich's eye";
            var light = Npc<Mob>(35662, 37750, 17006); light.Name = "dark light";
            var far = Npc<Mob>(34371, 38049, 17006); far.Name = "dark light";
            var below = Npc<Mob>(35990, 37600, 15629); below.Name = "dark light";
            var live = new List<GameNPC> { ilron, eye, light, far, below };
            Assert.That(T.ClearFirstOwner(eye, live), Is.SameAs(ilron));
            Assert.That(T.ClearFirstOwner(light, live), Is.SameAs(ilron));
            Assert.That(T.ClearFirstOwner(far, live), Is.Null, "2,064 from him: not his");
            Assert.That(T.ClearFirstOwner(below, live), Is.Null, "a floor below: not his");
            var tank = Npc<Raider>(36300, 37600, 17006);
            var dps = Npc<Raider>(36200, 37400, 17006);
            ilron.TargetObject = tank;
            light.Hurt = 60;
            var a = T.DungeonAdvice(new(dps, ilron, live, 0, false, 1_000, Hero));
            Assert.That((a?.Action, a?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)light)), "the most hurt first");
            Assert.That(T.DungeonAdvice(new(tank, ilron, live, 0, false, 1_000, Hero)), Is.Null, "the party he fights holds him");
            eye.Dead = light.Dead = far.Dead = below.Dead = true;
            var image = Npc<TestImage>(36400, 37500, 17006);
            live.Add(image);
            Assert.That(T.DungeonAdvice(new(dps, ilron, live, 0, false, 1_000, Hero))?.Target, Is.SameAs(image), "then his images");
            image.ObjectState = GameObject.eObjectState.Deleted;
            Assert.That(T.DungeonAdvice(new(dps, ilron, live, 0, false, 1_000, Hero)), Is.Null, "nothing left: everyone on him");
        }

        // ---- Icelord Agmundr -------------------------------------------------------------------------------------------

        [Test]
        public void AgmundrsLinkedMonstersDieFirstAndNothingInHisAggroIsPulled()
        {
            UseRegion(160);
            var agmundr = Npc<TestAgmundr>(15037, 50114, 12408);
            var depredator = Npc<Mob>(16418, 49908, 12059); depredator.PackageID = T.AgmundrLinkedPackage;
            var harrower = Npc<Mob>(15025, 50300, 12400); harrower.Name = "hrimthursa harrower";
            var faraway = Npc<Mob>(19801, 34348, 13679);
            var live = new List<GameNPC> { agmundr, depredator, harrower, faraway };
            Assert.That(T.ClearFirstOwner(depredator, live), Is.SameAs(agmundr));
            Assert.That(T.ClearFirstOwner(harrower, live), Is.Null);
            Assert.That(T.PullWait(harrower, live, 1_000), Does.Contain("Agmundr"), "PC log e3b8238: its muster went 76 from him");
            Assert.That(T.PullWait(faraway, live, 1_000), Is.Null);
            Assert.That(T.PullWait(depredator, live, 1_000), Is.Null, "1,395 from him: pulled alone");
            agmundr.Fighting = true;
            Assert.That(T.PullWait(harrower, live, 1_000), Is.Null, "he fights already");
        }

        [Test]
        public void AgmundrsColdBlastFindsOnlyHisTargetsParty()
        {
            var boss = Npc<TestAgmundr>(15000, 50000, 12400);
            Vector3 b = new(15000, 50000, 12400), tank = new(15300, 50000, 12400);
            // Casters and healers: 750 off his target.
            var spread = T.AgmundrAdvice(new(15200, 50100, 12400), b, tank, boss, "tank", true, false, 1);
            Assert.That(spread?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(spread.Value.StandAt.Value, tank), Is.GreaterThan(T.AgmundrBlastRadius));
            Assert.That(T.AgmundrAdvice(new(14200, 50000, 12400), b, tank, boss, "tank", true, false, 1), Is.Null, "already clear");
            // Melee: behind him on the line from his target, as far as their reach allows (his target 300 off: 195 behind).
            var behind = T.AgmundrAdvice(new(15150, 50150, 12400), b, tank, boss, "tank", false, false, 1);
            Assert.That(behind?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(behind.Value.StandAt.Value.X, Is.EqualTo(15000 - T.BehindReach).Within(1));
            Assert.That(Flat(behind.Value.StandAt.Value, tank), Is.EqualTo(495).Within(1));
            Assert.That(T.AgmundrAdvice(new(14810, 50005, 12400), b, tank, boss, "tank", false, false, 1), Is.Null, "at the spot: fight");
            // While he casts it, everyone within 500 of his target steps out, the melee too, and waits out there.
            var cast = T.AgmundrAdvice(new(14810, 50005, 12400), b, tank, boss, "tank", false, true, 7);
            Assert.That(cast?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(cast.Value.StandAt.Value, tank), Is.GreaterThan(T.AgmundrBlastRadius + T.CastMargin));
            var hold = T.AgmundrAdvice(new(14700, 50000, 12400), b, tank, boss, "tank", false, true, 7);
            Assert.That(hold?.StandAt, Is.EqualTo(new Vector3(14700, 50000, 12400)), "just out: hold until it lands");
        }

        [Test]
        public void HisTargetsPartyFightsAsUsualAndTheRuleSeesHisCast()
        {
            UseRegion(160);
            var agmundr = Npc<TestAgmundr>(15000, 50000, 12400);
            agmundr.Fighting = true;
            var tank = Npc<Raider>(15300, 50000, 12400);
            var caster = Npc<Raider>(15200, 50100, 12400);
            agmundr.TargetObject = tank;
            var live = new List<GameNPC> { agmundr, tank, caster };
            T.AgmundrCasting = _ => false;
            Assert.That(T.DungeonAdvice(new(tank, agmundr, live, 0, false, 1_000, Warrior)), Is.Null, "his target fights on");
            Assert.That(T.DungeonAdvice(new(caster, agmundr, live, 0, true, 1_000, Wizard))?.Action, Is.EqualTo(T.BotAction.Reposition));
            agmundr.Fighting = false;
            Assert.That(T.DungeonAdvice(new(caster, agmundr, live, 0, true, 1_000, Wizard)), Is.Null, "no fight: no spread");
            var blast = new Spell(new DOL.Database.DbSpell { SpellID = T.AgmundrBlastSpellId, Damage = 650, Radius = 500, Type = "DirectDamageNoVariance", Target = "Enemy" }, 70);
            Assert.That(blast.ID, Is.EqualTo(T.AgmundrBlastSpellId), "his blast is known by its spell id");
        }

        // ---- Spindler Broodmother ------------------------------------------------------------------------------------

        [Test]
        public void HerAddPartiesFollowHerSpawnRateAndKeepHerDruidsAndWardensOut()
        {
            Assert.That(T.SpindlerAddPartyCount(14), Is.EqualTo(8), "PC log e3b8238's raid: 8 wanted, 60% of 14 at most (#408)");
            Assert.That(T.SpindlerAddPartyCount(13), Is.EqualTo(7), "the sweep's 100 raiders: 60% of 13");
            Assert.That(T.SpindlerAddPartyCount(20), Is.EqualTo(8));
            Assert.That(T.SpindlerAddPartyCount(4), Is.EqualTo(2));
            Assert.That(T.SpindlerAddPartyCount(1), Is.EqualTo(0));
            Assert.That(T.SpindlerAddPartyCount(14), Is.GreaterThan(T.AddPartyCount(14, 3)), "more than the 1.2 rule's two");
            bool[] healers = [true, true, true, true, true];
            int[] druids = [1, 0, 2, 1, 1];
            Assert.That(T.AddPartyPicks(healers, druids, 2), Is.EqualTo(new[] { 1, 4 }), "none of them first, then the fewest, from the end");
            Assert.That(T.AddPartyPicks([false, true, false], [0, 0, 0], 2), Is.EqualTo(new[] { 1, 2 }), "a healer first among the clean");
            Assert.That(T.KeptOutOfSpindlerAddParties(Druid), Is.True);
            Assert.That(T.KeptOutOfSpindlerAddParties((int)eCharacterClass.Warden), Is.True);
            Assert.That(T.KeptOutOfSpindlerAddParties((int)eCharacterClass.Bard), Is.False);
        }

        [Test]
        public void BelowAQuarterTheRaidBurnsHer()
        {
            UseRegion(T.GalladoriaRegion);
            var boss = Npc<TestSpindler>(21283, 51707, 10876);
            var newborn = Npc<Newborn>(21300, 51750, 10876);
            var wizard = Npc<Raider>(21000, 51700, 10876);
            var live = new List<GameNPC> { boss, newborn, wizard };
            Assert.That(T.SpindlerBurning(true, 25), Is.True);
            Assert.That(T.SpindlerBurning(true, 26), Is.False);
            Assert.That(T.SpindlerBurning(false, 10), Is.False);
            boss.Hurt = 40;
            Assert.That(T.DungeonAdvice(new(wizard, boss, live, 0, true, 1_000, Wizard)), Is.Null, "above it the catalog's rules speak");
            boss.Hurt = 76;
            var a = T.DungeonAdvice(new(wizard, boss, live, 0, true, 1_000, Wizard));
            Assert.That((a?.Action, a?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.None, (GameNPC)boss)), "on her, not on the clump");
            Assert.That(T.Decide(wizard, boss, live, 0, true, 1_000, Wizard).Action, Is.EqualTo(T.BotAction.None));
        }

        [Test]
        public void TheMesmerizeSpreadPutsEachPartysBackLineAtItsOwnPost()
        {
            Vector3 her = new(21283, 51707, 10876);
            Vector3[] ring = T.SpreadRing(her, T.SpindlerSpreadSpacing, T.SpindlerSpreadRings);
            Assert.That(ring.Length, Is.GreaterThanOrEqualTo(14));
            Vector3[] posts = T.PickSpreadPosts(ring, T.SpindlerSpreadSpacing, her + new Vector3(660, 0, 0));
            Assert.That(posts.Length, Is.GreaterThanOrEqualTo(8));
            foreach (Vector3 p in posts)
            {
                Assert.That(Flat(p, her), Is.InRange(740f, 1410f), "within the raid's 1,500 of her");
                Assert.That(Flat(p, her + new Vector3(660, 0, 0)), Is.GreaterThanOrEqualTo(T.SpindlerSpreadSpacing), "clear of the curers' post");
                foreach (Vector3 q in posts.Where(q => q != p))
                    Assert.That(Flat(p, q), Is.GreaterThanOrEqualTo(T.SpindlerSpreadSpacing), "one 300 mesmerize reaches one post");
            }
            Assert.That(T.SpreadPostIndex(13, posts.Length), Is.EqualTo(13 % posts.Length));
            Assert.That(T.SpreadPostIndex(-1, posts.Length), Is.EqualTo(0));
            Assert.That(T.SpreadPostIndex(3, 0), Is.EqualTo(-1));
            Assert.That(T.TakesSpreadPost(true, false, false), Is.True);
            Assert.That(T.TakesSpreadPost(false, false, false), Is.False, "melee stay with her");
            Assert.That(T.TakesSpreadPost(true, true, false), Is.False, "add parties chase her newborns");
            Assert.That(T.TakesSpreadPost(true, false, true), Is.False, "the party she fights stands with its tank");
            Assert.That(T.MezSpreadOf(Npc<TestSpindler>(0, 0, 0)), Is.Not.Null);
            Assert.That(T.MezSpreadOf(Npc<Mob>(0, 0, 0)), Is.Null);
        }

        // ---- the griffon icewings perched on the glacier's cliffs ----------------------------------------------------

        [Test]
        public void APerchedFlyerIsDeferredUntilItComesDown()
        {
            UseRegion(160);
            var griffon = Npc<GameEpicNPC>(52937, 34168, 15508);
            typeof(GameNPC).GetField("m_flags", Hidden)!.SetValue(griffon, GameNPC.eFlags.FLYING);
            var live = new List<GameNPC> { griffon };
            T.FloorUnder = _ => 14379;
            Assert.That(T.PullWait(griffon, live, 1_000), Does.Contain("perched 1129"), "PC log e3b8238: 0/68 for 15.5 minutes");
            T.ForgetPerchFloors();
            T.FloorUnder = _ => 15100;
            Assert.That(T.PullWait(griffon, live, 1_000), Is.Null, "408 up: shot down as before");
            T.ForgetPerchFloors();
            T.FloorUnder = _ => null;
            Assert.That(T.PullWait(griffon, live, 1_000), Is.Null, "no navmesh: no opinion");
            Assert.That(T.PerchedOutOfReach(true, true, 16000, 14000), Is.False, "it fights: it is fought");
            Assert.That(T.PerchedOutOfReach(false, false, 16000, 14000), Is.False, "it walks");
            var boss = Npc<TestHost>(52937, 34168, 15508);
            typeof(GameNPC).GetField("m_flags", Hidden)!.SetValue(boss, GameNPC.eFlags.FLYING);
            T.FloorUnder = _ => 10000;
            Assert.That(T.PullWait(boss, [boss], 1_000), Is.Null.Or.Not.Contain("perched"), "a boss is never deferred as a perched flyer");
        }

        [Test]
        public void ATacticsPullHoldEndsAfterTwentyMinutes()
        {
            long start = 1_000_000;
            Assert.That(RealmRaidDungeonRoute.PullWaitExpired(start, start + 19 * 60_000), Is.False, "a hold under 20 minutes stands");
            Assert.That(RealmRaidDungeonRoute.PullWaitExpired(start, start + RealmRaidDungeonRoute.PullWaitLimitMilliseconds), Is.True,
                "an add the raid can never reach does not keep the boss waiting for ever");
            Assert.That(RealmRaidDungeonRoute.PullWaitExpired(long.MinValue, start), Is.True, "an expired hold stays expired");
        }

        // ---- build b1b6846 (owner's PC, 2026-11-18, Caer Sidi): a dropped wipe and a re-clear held on one court monster ------

        private static RealmRaidDungeonRoute CaerSidiRoute()
        {
            ConstructorInfo ctor = typeof(RealmRaidDungeonRoute).GetConstructors(Hidden).Single(c => c.GetParameters().Length == 7);
            Vector3 muster = new(29617, 41288, 15375);
            return (RealmRaidDungeonRoute)ctor.Invoke([(ushort)60, null, muster, muster, new[] { "Apocalypse" }, null, eRealm.Albion]);
        }

        private static void Set(RealmRaidDungeonRoute route, string field, object value) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.SetValue(route, value);
        private static object Get(RealmRaidDungeonRoute route, string field) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.GetValue(route);

        /// <summary>Fight A at 07:26-07:28: an early-pull Commit on the Crypt Lord engaged <paramref name="engaged"/> raiders, of
        /// whom <paramref name="fallen"/> fell; he left combat inside the 120 s grace while his forerunner still fought the raid.</summary>
        private (RealmRaidDungeonRoute Route, TestCryptLord Lord, Mob Forerunner) OpenAttemptOnTheCryptLord(int engaged, int fallen, int failedBefore = 0)
        {
            var (lord, forerunner, live) = CryptLordRoom(28711, 40297);
            lord.Name = "Crypt Lord";
            forerunner.Name = "forerunner";
            var raiders = new List<GameLiving>();
            for (int i = 0; i < engaged; i++)
            {
                var raider = Npc<Mob>(28400 + i * 5, 40400, 15373);
                raider.Dead = i < fallen;
                raiders.Add(raider);
            }
            RealmRaidDungeonRoute route = CaerSidiRoute();
            Set(route, "_target", lord);
            Set(route, "_musterTarget", lord);
            Set(route, "_musterEngaged", true);
            ((HashSet<GameLiving>)Get(route, "_engaged")).UnionWith(raiders);
            Set(route, "_attemptRaidSize", 114);
            Set(route, "_live", (IReadOnlyList<GameNPC>)live);
            ((RealmRaidDungeonRoute.PullContact)Get(route, "_pulls")).SawCombat();
            if (failedBefore > 0) ((Dictionary<string, int>)Get(route, "_failedAttempts"))["Crypt Lord"] = failedBefore;
            lord.Fighting = false;
            forerunner.Fighting = true;
            return (route, lord, forerunner);
        }

        private static bool SwitchToHostile(RealmRaidDungeonRoute route, GameNPC to) =>
            (bool)typeof(RealmRaidDungeonRoute).GetMethod("SwitchToHostile", Hidden)!.Invoke(route, [to, new Vector3(to.X, to.Y, to.Z), true, 60_000L]);

        [Test]
        public void AWipeIsJudgedBeforeTheRouteTurnsToTheCourtMonsterFightingTheRaid()
        {
            var (route, lord, forerunner) = OpenAttemptOnTheCryptLord(engaged: 70, fallen: 60);
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute));
            Assert.That(SwitchToHostile(route, forerunner), Is.False, "one wipe of two: the raid goes on");
            string[] lines = logs.Lines.ToArray();
            int wipe = Array.FindIndex(lines, l => l.StartsWith("REALM_EXPEDITION_WIPE ") && l.Contains("target=\"Crypt Lord\"") &&
                                                   l.Contains("failures=1 ") && l.Contains("engaged=70 fallen=60"));
            int turn = Array.FindIndex(lines, l => l.StartsWith("REALM_EXPEDITION_TARGET_SWITCH ") && l.Contains("to=\"forerunner\""));
            Assert.Multiple(() =>
            {
                Assert.That(wipe, Is.GreaterThanOrEqualTo(0), "build b1b6846 scored nothing for fight A (88 deaths): " + string.Join(" | ", lines));
                Assert.That(turn, Is.GreaterThan(wipe), "the wipe first, then the switch");
                Assert.That(route.FailedAttempts("Crypt Lord"), Is.EqualTo(1));
                Assert.That(route.WipesSoFar, Is.EqualTo(1));
                Assert.That(route.TargetName, Is.EqualTo("forerunner"), "the survivors still answer the court monster");
                Assert.That(Get(route, "_musterEngaged"), Is.False, "the attempt is closed");
            });
        }

        [Test]
        public void AnAttemptWithTheRaidStandingEndsWithoutAWipeBeforeTheSwitch()
        {
            var (route, _, forerunner) = OpenAttemptOnTheCryptLord(engaged: 70, fallen: 5);
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute));
            Assert.That(SwitchToHostile(route, forerunner), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(route.FailedAttempts("Crypt Lord"), Is.Zero, "5 of 70 fell: no wipe");
                Assert.That(logs.Lines.Any(l => l.StartsWith("REALM_EXPEDITION_ATTEMPT_ENDED ") && l.Contains("wipe=False")), Is.True);
                Assert.That(route.TargetName, Is.EqualTo("forerunner"));
            });
            Assert.That(RealmRaidDungeonRoute.JudgesAttemptBeforeSwitch(attemptOnTarget: false, switchesAway: true), Is.False,
                "no attempt open: nothing to judge");
        }

        [Test]
        public void ASecondWipeJudgedAtTheSwitchDefeatsTheRaid()
        {
            var (route, _, forerunner) = OpenAttemptOnTheCryptLord(engaged: 70, fallen: 60, failedBefore: 1);
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute));
            Assert.That(SwitchToHostile(route, forerunner), Is.True, "the route stops");
            Assert.Multiple(() =>
            {
                Assert.That(route.Defeated, Is.True);
                Assert.That(route.DefeatedAt, Is.EqualTo("Crypt Lord"));
                Assert.That(logs.Lines.Any(l => l.StartsWith("REALM_EXPEDITION_TARGET_SWITCH ")), Is.False, "no switch after the defeat");
            });
        }

        /// <summary>Lord at his corner P1 walking north to P2: the reckoned soul by that corner is held (his patrol is on it);
        /// the western forerunner is clear of him (TheWalkWindowStillOpensOnHisFarSide's court check).</summary>
        private (TestCryptLord Lord, Mob Held, Mob Free, List<GameNPC> Live, Vector3 Front) HeldAndFreeCourt()
        {
            var lord = Npc<TestCryptLord>((int)CryptLordCorner.X, (int)CryptLordCorner.Y, (int)CryptLordCorner.Z);
            T.CryptLordNextPoint = _ => 1;
            var held = Npc<Mob>(28513, 39881, 15373);
            held.Name = "reckoned soul"; held.PackageID = T.CryptLordCourt;
            var free = Npc<Mob>((int)WesternForerunner.X, (int)WesternForerunner.Y, (int)WesternForerunner.Z);
            free.Name = "forerunner"; free.PackageID = T.CryptLordCourt;
            var live = new List<GameNPC> { lord, held, free };
            // The front by the held one: nearest by walk, as the 07:38:51 pick was.
            return (lord, held, free, live, new Vector3(28650, 40000, 15373));
        }

        [Test]
        public void TheReclearTakesACourtMonsterWhoseWindowIsOpenBeforeANearerOneThatMustWait()
        {
            var (lord, held, free, live, front) = HeldAndFreeCourt();
            Func<GameNPC, bool> freeNow = n => T.PullWait(n, live, 1_000, null) == null;
            Assume.That(T.PullWait(held, live, 1_000), Does.Contain("patrol comes within"));
            Assume.That(T.PullWait(free, live, 1_000), Is.Null);
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(lord, live, _ => true, front), Is.SameAs(held), "by walk alone: the 07:38:51 pick");
                Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(lord, live, _ => true, front, freeNow), Is.SameAs(free),
                    "one whose window is open now first");
                Assert.That(RealmRaidDungeonRoute.ReclearBeforePull(lord, live, _ => true, front, _ => false), Is.SameAs(held),
                    "none free: the nearest, and its hold's 20-minute lapse stands as before");
            });
        }

        [Test]
        public void WhileHeldOnACourtMonsterTheRouteTakesAnotherWhoseWindowOpened()
        {
            var (lord, held, free, live, front) = HeldAndFreeCourt();
            Func<GameNPC, bool> freeNow = n => T.PullWait(n, live, 1_000, null) == null;
            Assert.That(RealmRaidDungeonRoute.FreeReclearInstead(held, live, _ => true, front, freeNow, false), Is.SameAs(free),
                "07:38:51-07:59:33: 20 minutes held on the reckoned soul");
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDungeonRoute.FreeReclearInstead(held, live, _ => true, front, _ => false, false), Is.Null,
                    "no other free: it keeps holding");
                Assert.That(RealmRaidDungeonRoute.FreeReclearInstead(held, live, n => n != free, front, freeNow, false), Is.Null,
                    "one the route set aside is not taken");
            });
            lord.Fighting = true;
            Assert.That(RealmRaidDungeonRoute.FreeReclearInstead(held, live, _ => true, front, freeNow, false), Is.Null, "his fight is on");
            lord.Fighting = false;
            free.Dead = true;
            Assert.That(RealmRaidDungeonRoute.FreeReclearInstead(held, live, _ => true, front, freeNow, false), Is.Null);
            string route = Source("bots", "autonomous", "RealmRaidDungeonRoute.cs");
            Assert.That(route, Does.Contain("FreeReclearInstead(_target, _live, n => _routeRetry.GetValueOrDefault(n) <= now, Front, ReclearFree(now),"));
            Assert.That(RealmRaidDungeonRoute.PullWaitLimitMilliseconds, Is.EqualTo(20 * 60_000), "the lapse is the owner's call: unchanged");
        }

        // ---- the court fought at the gather point (owner, 2026-11-18, after b1b6846's fight B) -----------------------------

        /// <summary>The muster of the 5378dbe/b1b6846 court pulls (MUSTER_GUARDED 29617,41288): far from his patrol.</summary>
        private static readonly Vector3 CourtMuster = new(29617, 41288, 15375);

        private (TestCryptLord Lord, Mob Court, Mob Raider, List<GameNPC> Live) CourtFight(Vector3 courtAt)
        {
            var lord = Npc<TestCryptLord>(26567, 40143, 15372);
            var court = Npc<Mob>((int)courtAt.X, (int)courtAt.Y, (int)courtAt.Z);
            court.Name = "dark light"; court.PackageID = T.CryptLordCourt; court.Fighting = true;
            var raider = Npc<Mob>((int)courtAt.X + 50, (int)courtAt.Y, (int)courtAt.Z);
            return (lord, court, raider, new List<GameNPC> { lord, court });
        }

        [Test]
        public void ACourtFightDriftingTowardsHisPatrolIsHeldAtTheMuster()
        {
            // By his corner P1, as fight B's pack drifted within 800 of his legs (07:36, he joined 109 s after the pull).
            Vector3 drift = T.CryptLordPatrol[1] + new Vector3(0, -300, 1);
            Assume.That(T.DistanceToPatrol(drift), Is.LessThan(T.CourtHoldPatrolReach));
            Assume.That(T.DistanceToPatrol(CourtMuster), Is.GreaterThan(T.CourtHoldPatrolReach), "the muster keeps clear of his loop");
            var (lord, court, raider, live) = CourtFight(drift);
            try
            {
                Assert.That(T.CourtLeash(court, 1_000), Is.Null, "no court fight from a muster: no leash (as before)");
                T.SetCourtHold(lord, CourtMuster);
                Assert.That(T.CourtLeash(court, 1_000), Is.EqualTo(CourtMuster));
                T.Advice advice = T.Decide(raider, court, live, 0, false, 2_000, 0);
                Assert.Multiple(() =>
                {
                    Assert.That(advice.Action, Is.EqualTo(T.BotAction.StandOff), "not followed");
                    Assert.That(advice.StandAt, Is.EqualTo(CourtMuster), "the raider goes back to the muster; it chases them there");
                    Assert.That(advice.Reason, Does.StartWith("court-leash:"));
                });
                Assert.That(T.CourtLeashHolds(drift, CourtMuster, T.DistanceToPatrol(drift), 30_000), Is.True);
                // Held a minute: it is fought where it stands (a caster that never comes does not hold the raid under its spells).
                Assert.That(T.CourtLeash(court, 1_000 + T.CourtHoldLimitMilliseconds), Is.Null);
                // The Crypt Lord, his patrol and his court are unchanged: only where the raid stands.
                Assert.That(T.CryptLordCourtLink, Is.EqualTo(800));
                string tactics = Source("bots", "autonomous", "EpicEncounterTactics.cs");
                Assert.That(tactics, Does.Contain("!HasDungeonRule(target) &&\n                !IsCryptLordCourt(target))"),
                    "court monsters reach the tactic hook (TryHandle)");
            }
            finally { T.SetCourtHold(lord, null); }
        }

        [Test]
        public void ACourtFightAtTheMusterOrAwayFromHisPatrolIsFoughtAsUsual()
        {
            var (lord, court, raider, live) = CourtFight(CourtMuster + new Vector3(-400, 150, 0));
            try
            {
                T.SetCourtHold(lord, CourtMuster);
                Assert.That(T.CourtLeash(court, 1_000), Is.Null, "within the leash: the fight goes on at the muster");
                Assert.That(T.Decide(raider, court, live, 0, false, 1_000, 0).Reason ?? "", Does.Not.StartWith("court-leash"));
                // Away from his loop (farther than his link plus the margin): no leash however far from the muster.
                Vector3 far = CourtMuster + new Vector3(0, 900, 0);
                Assume.That(T.DistanceToPatrol(far), Is.GreaterThan(T.CourtHoldPatrolReach));
                Move(court, far.X, far.Y, far.Z);
                Assert.That(T.CourtLeash(court, 1_000), Is.Null);
                // Drifted to his loop, but he fights already: nothing to keep him out of.
                Vector3 drift = T.CryptLordPatrol[1] + new Vector3(0, -300, 1);
                Move(court, drift.X, drift.Y, drift.Z);
                Assert.That(T.CourtLeash(court, 1_000), Is.Not.Null);
                lord.Fighting = true;
                Assert.That(T.CourtLeash(court, 1_000), Is.Null, "his fight is on");
                lord.Fighting = false;
                court.Dead = true;
                Assert.That(T.CourtLeash(court, 1_000), Is.Null, "killed: the court fight completed");
            }
            finally { T.SetCourtHold(lord, null); }
            Assert.That(T.CourtHoldIn(60), Is.Null);
        }

        [Test]
        public void TheRouteSetsTheCourtHoldAtItsMusterAndLogsTheLeash()
        {
            Vector3 drift = T.CryptLordPatrol[1] + new Vector3(0, -300, 1);
            var (lord, court, raider, live) = CourtFight(drift);
            RealmRaidDungeonRoute route = CaerSidiRoute();
            Set(route, "_target", court);
            Set(route, "_musterTarget", court);
            Set(route, "_musterPoint", CourtMuster);
            MethodInfo observe = typeof(RealmRaidDungeonRoute).GetMethod("ObserveCourtHold", Hidden)!;
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute));
            try
            {
                observe.Invoke(route, [live.ToArray(), new GameLiving[] { raider }, 5_000L]);
                Assert.That(T.CourtHoldIn(60)?.Hold, Is.EqualTo(CourtMuster));
                string line = logs.Lines.SingleOrDefault(l => l.StartsWith("REALM_EXPEDITION_COURT_LEASH "));
                Assert.That(line, Is.Not.Null, "the PC log shows the leash holding the raid back");
                Assert.That(line, Does.Contain("court=\"dark light\"").And.Contain("muster=29617,41288"));
                Assert.That(Source("bots", "autonomous", "RealmRaidDungeonRoute.cs"),
                    Does.Contain("EpicEncounterTactics.CourtLeash(_target, now) is Vector3 courtHold"),
                    "while it holds, the route holds the raid at the muster instead of calling it on to the court monster");
                // The route turns to the Crypt Lord himself: no court hold any more.
                Set(route, "_target", lord);
                observe.Invoke(route, [live.ToArray(), new GameLiving[] { raider }, 6_000L]);
                Assert.That(T.CourtHoldIn(60), Is.Null);
            }
            finally { T.SetCourtHold(lord, null); }
        }
    }
}
