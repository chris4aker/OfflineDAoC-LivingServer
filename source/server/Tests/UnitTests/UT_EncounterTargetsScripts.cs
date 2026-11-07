using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The boss scripts on EncounterTargets, in a test region: a dragon's glare and Xanxicar's glare pick a bot, Xanxicar's
    /// throw and Scurceol's orb throw pick bots only once bots can be displaced, and messages reach real players only.
    /// No world tick, combat or spell runs: the tests call the scripts' pick methods and read whom they picked.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EncounterTargetsScripts
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private Region _region;
        private Zone _zone;
        private ushort _nextId = 700;
        private EpicTestServerScope _server;

        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Dead;
            public int Moves;
            public override bool IsAlive => !Dead;
            public override eRealm Realm { get => eRealm.Midgard; set { } }
            public override ICharacterClass CharacterClass => new ClassWarrior();
            public override bool MoveTo(ushort regionID, int x, int y, int z, ushort heading)
            {
                Moves++;
                return true;
            }
        }

        public sealed class Dragon : MidGjalpinulva
        {
            public override bool IsAlive => true;
        }

        public sealed class Xanx : Xanxicar
        {
            public override bool IsAlive => true;
        }

        public sealed class Orb : GameNPC
        {
            public override bool IsAlive => true;
        }

        public sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public override bool IsAlive => true;
        }

        /// <summary>Records every SendMessage a real player's client gets.</summary>
        public class Recorder : DispatchProxy
        {
            public readonly List<string> Messages = [];
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IPacketLib.SendMessage) && args.Length > 0 && args[0] is string text)
                    Messages.Add(text);
                return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 62, Name = "Targets test", Description = "Targets test", Mobs = [] });
            _zone = new Zone(_region, 62, "Targets test", 0, 0, 65536, 65536, 62, false, 0, false, 0, 0, 0, 0, 0);
            _region.Zones.Add(_zone);
            EncounterTargets.BotsCanBeDisplaced = false;
            MidGjalpinulvaBrain.IsRestless = false;
            MidGjalpinulvaBrain.RandomTarget = null;
            XanxicarBrain.RandomTarget = null;
            XanxicarBrain.RandomTarget2 = null;
        }

        [TearDown]
        public void TearDown()
        {
            EncounterTargets.BotsCanBeDisplaced = false;
            MidGjalpinulvaBrain.RandomTarget = null;
            XanxicarBrain.RandomTarget = null;
            XanxicarBrain.RandomTarget2 = null;
            _server.Dispose();
        }

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

        /// <summary>Files an actor in its subzone (what ZoneService does on a tick), so GetNPCs/PlayersInRadius find it.</summary>
        private void File(GameObject obj)
        {
            Assert.That(_zone.AddObject(obj), Is.True);
            SubZoneObject sub = obj.SubZoneObject;
            sub.DestinationSubZone.AddObject(sub.Node);
            _zone.OnObjectAddedToZone();
            sub.OnSubZoneTransition();
        }

        private Player Human(int x, int y, int z)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            var caches = typeof(GameObject).GetField("_objectsInRadiusCaches", Hidden)!;
            caches.SetValue(player, Activator.CreateInstance(caches.FieldType));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(player, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GamePlayer).GetField("m_client", Hidden)!.SetValue(player, new BotDummyClient { Out = DispatchProxy.Create<IPacketLib, Recorder>() });
            player.CurrentRegion = _region;
            player.X = x; player.Y = y; player.Z = z;
            player.ObjectState = GameObject.eObjectState.Active;
            player.SubZoneObject = new SubZoneObject(player);
            File(player);
            return player;
        }

        private static TBrain Brain<TBrain>(GameNPC body) where TBrain : StandardMobBrain, new()
        {
            var brain = new TBrain { Body = body };
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(body, brain);
            return brain;
        }

        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Hidden | BindingFlags.Public)!.Invoke(target, args);

        [Test]
        public void TheWorldPoolFindsBotsInRadiusOnly()
        {
            var boss = Npc<Orb>(10_000, 10_000, 1_000);
            var near = Npc<Bot>(10_400, 10_000, 1_000);
            var far = Npc<Bot>(14_000, 10_000, 1_000);
            var add = Npc<Orb>(10_100, 10_000, 1_000);
            var pool = EncounterTargets.InRadius(boss, 2_000, EncounterTargets.AliveNotGm);
            Assert.That(pool, Is.EquivalentTo(new GameLiving[] { near }), "the bot in reach; not the far one, not another NPC");
            Assert.That(EncounterTargets.Nearest(boss, 5_000), Is.SameAs(near));
            Assert.That(EncounterTargets.RandomOther(boss, 5_000, near), Is.SameAs(far));
            Assert.That(EncounterTargets.InRadius(boss, 2_000, bots: false), Is.Empty);
            Assert.That(add, Is.Not.Null);
        }

        [Test]
        public void AShuffledPoolGivesTheFirstOneToAnyRaider()
        {
            // Scripts that take the first or the last raider of the loop no longer always take the human listed first.
            var human = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            var bots = Enumerable.Range(0, 4).Select(_ => (GameNPC)UT_EncounterTargets.NewBot()).ToList();
            int first = 0;
            const int trials = 10_000;
            for (int i = 0; i < trials; i++)
            {
                var pool = EncounterTargets.Pool(null, [human], bots);
                EncounterTargets.Shuffle(pool);
                if (pool[0] == human)
                    first++;
            }
            Assert.That(first / (double)trials, Is.EqualTo(0.2).Within(0.03));
        }

        [Test]
        public void ADragonGlarePicksABot()
        {
            var dragon = Npc<Dragon>(10_000, 10_000, 1_000);
            var brain = Brain<MidGjalpinulvaBrain>(dragon);
            var bot = Npc<Bot>(10_500, 10_000, 1_000);
            brain.AddToAggroList(bot, 100);
            Call(brain, "PrepareGlare", new object[] { null });
            Assert.That(MidGjalpinulvaBrain.RandomTarget, Is.SameAs(bot), "the glare's victim is the bot: no player is near");
        }

        [Test]
        public void XanxicarGlaresAtBotsButThrowsThemOnlyOnceBotsCanBeDisplaced()
        {
            var xanx = Npc<Xanx>(10_000, 10_000, 1_000);
            var brain = Brain<XanxicarBrain>(xanx);
            var tank = Npc<Bot>(10_100, 10_000, 1_000);
            var bot = Npc<Bot>(10_600, 10_000, 1_000);
            brain.AddToAggroList(tank, 100);
            xanx.TargetObject = tank;

            Call(brain, "GlarePlayer", new object[] { null });
            Assert.That(XanxicarBrain.RandomTarget2, Is.Not.Null.And.InstanceOf<GameBot>(), "the glare picks a bot");

            Call(brain, "ThrowPlayer", new object[] { null });
            Assert.That(bot.Moves + tank.Moves, Is.Zero, "today: the throw picks real players only, so no bot is hurled");

            EncounterTargets.BotsCanBeDisplaced = true;
            Call(brain, "ThrowPlayer", new object[] { null });
            Assert.That(bot.Moves, Is.EqualTo(1), "with bot recovery on: the bot that isn't his target is hurled");
            Assert.That(tank.Moves, Is.Zero, "never the one he is fighting");
        }

        [Test]
        public void ScurceolsOrbsThrowABotOnlyOnceBotsCanBeDisplaced()
        {
            var orb = Npc<Orb>(51_900, 20_000, 17_000);
            var brain = Brain<LyftMihtBrain1>(orb);
            var bot = Npc<Bot>(51_950, 20_000, 16_964);
            var picked = typeof(LyftMihtBrain1).GetProperty("RandomTarget", HiddenStatic | BindingFlags.FlattenHierarchy);
            var flag = typeof(LyftMihtBrain1).GetField("IsTargetPicked", HiddenStatic | BindingFlags.FlattenHierarchy);
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    flag.SetValue(null, false);
                    brain.OnAttackedByEnemy(new AttackData { Attacker = bot, Target = orb, Damage = 100 });
                }
                Assert.That(picked.GetValue(null), Is.Null, "today: a bot's hit never marks it for the throw");
                EncounterTargets.BotsCanBeDisplaced = true;
                for (int i = 0; i < 200 && picked.GetValue(null) == null; i++)
                {
                    flag.SetValue(null, false);
                    brain.OnAttackedByEnemy(new AttackData { Attacker = bot, Target = orb, Damage = 100 });
                }
                Assert.That(picked.GetValue(null), Is.SameAs(bot), "with bot recovery on: the bot that hit the orb is marked (25% a hit)");
            }
            finally
            {
                picked.SetValue(null, null);
                flag.SetValue(null, false);
            }
        }

        [Test]
        public void MessagesReachRealPlayersOnly()
        {
            var recorder = DispatchProxy.Create<IPacketLib, Recorder>();
            var client = new BotDummyClient { Out = recorder };
            var human = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            typeof(GamePlayer).GetField("m_client", Hidden)!.SetValue(human, client);
            var bot = Npc<Bot>(10_000, 10_000, 1_000);
            EncounterTargets.Tell(human, "May your corpse rot on Nastrand!", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
            EncounterTargets.Tell(bot, "May your corpse rot on Nastrand!", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
            Assert.That(((Recorder)(object)recorder).Messages, Is.EqualTo(new[] { "May your corpse rot on Nastrand!" }),
                "the human gets the line once; the bot gets the glare, not a packet");
            // The dragon's broadcast still loops GetPlayersInRadius: with only bots near, nobody is sent anything.
            var dragon = Npc<Dragon>(10_000, 10_000, 1_000);
            var brain = Brain<MidGjalpinulvaBrain>(dragon);
            Assert.DoesNotThrow(() => brain.BroadcastMessage("Gjalpinulva stares at a bot."));
        }
    
        [Test]
        public void InADragonGlareTheHumanIsOneOfMany()
        {
            var dragon = Npc<Dragon>(10_000, 10_000, 1_000);
            var brain = Brain<MidGjalpinulvaBrain>(dragon);
            var human = Human(10_300, 10_000, 1_000);
            var bots = Enumerable.Range(0, 3).Select(i => Npc<Bot>(10_400 + i * 50, 10_000, 1_000)).ToList();
            brain.AddToAggroList(bots[0], 100);
            int humanPicks = 0, botPicks = 0;
            const int trials = 2_000;
            for (int i = 0; i < trials; i++)
            {
                Call(brain, "ResetGlare", new object[] { null });
                Call(brain, "PrepareGlare", new object[] { null });
                if (MidGjalpinulvaBrain.RandomTarget == human) humanPicks++;
                else if (MidGjalpinulvaBrain.RandomTarget is Bot) botPicks++;
            }
            Assert.That(humanPicks + botPicks, Is.EqualTo(trials), "every glare picks someone of the raid");
            Assert.That(humanPicks / (double)trials, Is.EqualTo(0.25).Within(0.05),
                "before: the human took every glare; now one in four (three bots near)");
        }
    
    }
}
