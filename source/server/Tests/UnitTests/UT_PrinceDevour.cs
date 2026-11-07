using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Princes Asmoien and Ba'alorien in Darkness Falls devour the nearest pet every five seconds and heal by its health.
    /// Their test was "Brain is IControlledBrain", which a bot's own brain (BotBrain) matches, so they ate companion bots and
    /// autonomous gamebots. In a test region: they now find summoned pets only (EpicEncounterMechanics.IsDevourablePet).
    /// No world tick, combat or spell runs.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_PrinceDevour
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private Zone _zone;
        private ushort _nextId = 700;
        private EpicTestServerScope _server;

        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override eRealm Realm { get => eRealm.Midgard; set { } }
            public override ICharacterClass CharacterClass => new ClassWarrior();
        }

        public sealed class Orb : GameNPC
        {
            public override bool IsAlive => true;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 62, Name = "Prince test", Description = "Prince test", Mobs = [] });
            _zone = new Zone(_region, 62, "Prince test", 0, 0, 65536, 65536, 62, false, 0, false, 0, 0, 0, 0, 0);
            _region.Zones.Add(_zone);
        }

        [TearDown]
        public void TearDown() => _server.Dispose();

        private T Npc<T>(int x, int y, int z) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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
            File(npc);
            return npc;
        }

        /// <summary>Files an actor in its subzone (what ZoneService does on a tick), so GetNPCsInRadius finds it.</summary>
        private void File(GameObject obj)
        {
            Assert.That(_zone.AddObject(obj), Is.True);
            SubZoneObject sub = obj.SubZoneObject;
            sub.DestinationSubZone.AddObject(sub.Node);
            _zone.OnObjectAddedToZone();
            sub.OnSubZoneTransition();
        }

        [Test]
        public void ThePrincesDevourPetsNotBots()
        {
            var prince = Npc<Orb>(10_000, 10_000, 1_000);
            var companion = Npc<Bot>(10_100, 10_000, 1_000);                    // nearest, with a bot's brain
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(companion, RuntimeHelpers.GetUninitializedObject(typeof(BotBrain)));
            Assert.That(companion.Brain, Is.InstanceOf<IControlledBrain>(), "a bot's brain is a controlled brain: the old test matched it");
            var pet = Npc<Orb>(10_600, 10_000, 1_000);
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(pet, RuntimeHelpers.GetUninitializedObject(typeof(ControlledMobBrain)));
            var mob = Npc<Orb>(10_050, 10_000, 1_000);
            Assert.That(EpicEncounterMechanics.IsDevourablePet(companion), Is.False);
            Assert.That(EpicEncounterMechanics.IsDevourablePet(mob), Is.False);
            Assert.That(EpicEncounterMechanics.IsDevourablePet(pet), Is.True, "a summoned pet (of a player or of a bot)");
            Assert.That(EpicEncounterMechanics.NearestDevourablePet(prince, prince.GetNPCsInRadius(2000)), Is.SameAs(pet),
                "the nearest pet, past the nearer bot");
            Assert.That(EpicEncounterMechanics.NearestDevourablePet(prince, [companion, mob]), Is.Null, "no pet: nobody is eaten");
        }
    }
}
