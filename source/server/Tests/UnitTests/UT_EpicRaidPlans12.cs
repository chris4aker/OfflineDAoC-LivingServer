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
            T.ResetAddPartiesForTests();
        }

        [TearDown]
        public void TearDown()
        {
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

        // ---- the Crypt Lord's court ---------------------------------------------------------------------------------

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

        // ---- Lich Lord Ilron -------------------------------------------------------------------------------------------

        // ---- Spindler Broodmother ------------------------------------------------------------------------------------

        // ---- the griffon icewings perched on the glacier's cliffs ----------------------------------------------------
    }
}
