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
            Assert.That(T.SpindlerAddPartyCount(14), Is.EqualTo(6), "PC log e3b8238's raid: 8 wanted, 45% of 14 at most");
            Assert.That(T.SpindlerAddPartyCount(20), Is.EqualTo(8));
            Assert.That(T.SpindlerAddPartyCount(4), Is.EqualTo(1));
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
    }
}
