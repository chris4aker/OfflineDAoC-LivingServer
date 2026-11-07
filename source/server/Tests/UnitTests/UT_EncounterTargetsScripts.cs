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
            // EncounterDisplacement moves a bot in its region (MoveInRegion), with no world removal.
            public override bool MoveInRegion(ushort regionID, int x, int y, int z, ushort heading, bool forceMove)
            {
                Moves++;
                return true;
            }
            public override void StopMoving() { }
            public override bool IsCasting => false;
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
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterFloodGuard.Reset();
            EncounterDisplacementRecovery.ForgetAll();
            MidGjalpinulvaBrain.IsRestless = false;
            MidGjalpinulvaBrain.RandomTarget = null;
            XanxicarBrain.RandomTarget = null;
            XanxicarBrain.RandomTarget2 = null;
        }

        [TearDown]
        public void TearDown()
        {
            EncounterTargets.BotsCanBeDisplaced = false;
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterFloodGuard.Reset();
            EncounterDisplacementRecovery.ForgetAll();
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

        /// <summary>The test NPC classes carry their own class names; give them the shipped switch of the boss they stand for.</summary>
        private static void ShipAs(string testClass, string encounter, string name = "AllowBotDisplacement") =>
            EpicEncounterMechanics.SetFor(name, testClass, EpicEncounterMechanics.ValueFor(name, encounter));

        [Test]
        public void XanxicarGlaresAtBotsAndThrowsThemWhereHisBotDisplacementIsOn()
        {
            var xanx = Npc<Xanx>(10_000, 10_000, 1_000);
            var brain = Brain<XanxicarBrain>(xanx);
            var tank = Npc<Bot>(10_100, 10_000, 1_000);
            var bot = Npc<Bot>(10_600, 10_000, 1_000);
            brain.AddToAggroList(tank, 100);
            xanx.TargetObject = tank;

            Call(brain, "GlarePlayer", new object[] { null });
            Assert.That(XanxicarBrain.RandomTarget2, Is.Not.Null.And.InstanceOf<GameBot>(), "the glare picks a bot");

            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "Xanx", false);
            Call(brain, "ThrowPlayer", new object[] { null });
            Assert.That(bot.Moves + tank.Moves, Is.Zero, "his bot displacement off: the throw picks real players only, no bot is hurled");

            Assert.That(EpicEncounterMechanics.ValueFor("AllowBotDisplacement", "Xanxicar"), Is.True, "shipped on for Xanxicar");
            ShipAs("Xanx", "Xanxicar");
            Assert.That(EncounterTargets.DisplacesBots(xanx), Is.True, "the pick asks the move's switch");
            Call(brain, "ThrowPlayer", new object[] { null });
            Assert.That(bot.Moves, Is.EqualTo(1), "as shipped: the bot that isn't his target is hurled");
            Assert.That(tank.Moves, Is.Zero, "never the one he is fighting");
            Assert.That(EncounterDisplacementRecovery.IsRecovering(bot), Is.True, "and it is brought back");
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

        // ---- Epic wow: picks and moves agree per boss (EncounterTargets.DisplacesBots = AllowBotDisplacementFor) ---------

        public sealed class TestLegion : DOL.GS.Scripts.Legion
        {
            public int Heals;
            public override bool IsAlive => true;
            public override int Health { get => 1; set { if (value > 1) Heals++; } }
        }

        public sealed class TestNosdoden : Nosdoden
        {
            public override bool IsAlive => true;
        }

        private static void SetHealth(GameObject o, int health) =>
            typeof(GameObject).GetField("m_health", Hidden)!.SetValue(o, health);

        [Test]
        public void EveryMovePickAsksTheSameSwitchAsTheMove()
        {
            // Shipped: the three dragons and Xanxicar move bots, and since epic wave 2 Galladoria's movers (UT_EpicWaveTwo),
            // Vifil, Cunovinda and others (UT_EpicWave2MidHib), Albion's (UT_EpicWave2Albion) and wave 3's (Legion, Easmarach,
            // Afanc, the horsemen's room: UT_EpicWave3); the rest keep them out of its picks.
            foreach (Type boss in new[] { typeof(AlbGolestandt), typeof(MidGjalpinulva), typeof(HibCuuldurach), typeof(Xanxicar),
                         typeof(Olcasgean), typeof(WaterPrimal), typeof(SpindlerBroodmother), typeof(WaterfallAntipass),
                         typeof(Jailer), typeof(SummonerCunovinda), typeof(Orylle), typeof(KrevoRicik), typeof(MoranTheMighty),
                         typeof(DOL.GS.Scripts.Legion), typeof(Easmarach), typeof(LegendaryAfanc), typeof(ApocInitializator) })
            {
                var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(boss);
                Assert.That(EncounterTargets.DisplacesBots(npc), Is.True, boss.Name);
                Assert.That(EncounterTargets.DisplacesBots(npc), Is.EqualTo(EpicEncounterMechanics.AllowBotDisplacementFor(npc)), boss.Name);
            }
            foreach (Type boss in new[] { typeof(Conservator), typeof(GrandSummonerGovannon), typeof(Orshom) })
            {
                var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(boss);
                Assert.That(EncounterTargets.DisplacesBots(npc), Is.False, boss.Name + ": player-only as before");
            }
            // One switch per boss turns both on together, and off again.
            var vifil = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Jailer));
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "Jailer", false);
            Assert.That(EncounterTargets.DisplacesBots(vifil), Is.False);
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "Jailer", true);
            Assert.That(EncounterTargets.DisplacesBots(vifil), Is.True);
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "Xanxicar", false);
            Assert.That(EncounterTargets.DisplacesBots((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Xanxicar))), Is.False);
            // The global switch (the old name too) reaches every boss without its own value.
            EncounterTargets.BotsCanBeDisplaced = true;
            Assert.That(EpicEncounterMechanics.AllowBotDisplacement, Is.True);
            Assert.That(EncounterTargets.DisplacesBots((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Orshom))), Is.True);
        }

        [Test]
        public void TheDragonsGustThrowsBotsAsShippedAndPlayersOnlyWithItsBotDisplacementOff()
        {
            var dragon = Npc<AlbGolestandt>(10_000, 10_000, 1_000);
            SetHealth(dragon, 1_000);
            var tank = Npc<Bot>(10_100, 10_000, 1_000);
            var bots = Enumerable.Range(0, 4).Select(i => Npc<Bot>(10_400 + i * 100, 10_000, 1_000)).ToList();
            dragon.TargetObject = tank;
            GameLiving[] aggro = [tank, .. bots];

            List<GameLiving> pool = DragonEncounterThrow.Candidates(dragon, aggro);
            Assert.That(pool, Is.EquivalentTo(bots), "as shipped: the bots fighting it, never its target");
            int moved = DragonEncounterThrow.Throw(dragon, aggro, new Random(3));
            Assert.That(moved, Is.InRange(2, 4));
            Assert.That(bots.Sum(b => b.Moves), Is.EqualTo(moved), "each picked bot hurled once");
            Assert.That(tank.Moves, Is.Zero);
            Assert.That(bots.Where(b => b.Moves > 0).All(EncounterDisplacementRecovery.IsRecovering), Is.True, "and brought back");

            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "AlbGolestandt", false);
            Assert.That(DragonEncounterThrow.Candidates(dragon, aggro), Is.Empty, "its bot displacement off: real players only, as the original");
            Assert.That(DragonEncounterThrow.Throw(dragon, aggro, new Random(3)), Is.Zero);
        }

        [Test]
        public void TheDragonThrowKeepsItsOriginalHeightUnlessItsSettingSoftensIt()
        {
            var spot = DragonEncounterThrow.Spots["AlbGolestandt"][0];          // 1815: 1,424 above the lair floor (391)
            Assert.That(DragonEncounterThrow.ThrowZ(spot, 391, 0), Is.EqualTo(1815), "0 (shipped): the original spot");
            Assert.That(EncounterDisplacement.FallDamagePercent(1815 - 391, 0), Is.EqualTo(62), "about 60% of a raider's health");
            Assert.That(DragonEncounterThrow.ThrowZ(spot, 391, 712), Is.EqualTo(391 + 712), "softened: at most 712 above the raider");
            Assert.That(EncounterDisplacement.FallDamagePercent(712, 0), Is.EqualTo(34));
            Assert.That(DragonEncounterThrow.ThrowZ(spot, 391, 5_000), Is.EqualTo(1815), "never higher than the original");

            var golestandt = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(AlbGolestandt));
            var gjalpinulva = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(MidGjalpinulva));
            Assert.That(DragonEncounterThrow.MaxThrowHeightFor(golestandt), Is.Zero, "owner default: the original height");
            int before = DOL.GS.ServerProperties.Properties.EPIC_DRAGON_THROW_HEIGHT_GOLESTANDT;
            try
            {
                DOL.GS.ServerProperties.Properties.EPIC_DRAGON_THROW_HEIGHT_GOLESTANDT = 712;
                Assert.That(DragonEncounterThrow.MaxThrowHeightFor(golestandt), Is.EqualTo(712), "one setting per dragon");
                Assert.That(DragonEncounterThrow.MaxThrowHeightFor(gjalpinulva), Is.Zero, "the others keep theirs");
            }
            finally { DOL.GS.ServerProperties.Properties.EPIC_DRAGON_THROW_HEIGHT_GOLESTANDT = before; }
        }

        [Test]
        public void LegionsThrowTakesBotsOnlyWithHisBotDisplacementOn()
        {
            // Epic wave 3: his platform has no walked way down, so a thrown bot is recalled to its party after 5 s (the
            // recovery's no-path fallback). Off for him, his throw takes only real players, as before.
            // Darkness Falls (region 249): the platform 48200,49566 is in the same region as the fight.
            _region = new Region(new RegionData { Id = 249, Name = "Darkness Falls test", Description = "Darkness Falls test", Mobs = [] });
            _zone = new Zone(_region, 249, "Darkness Falls test", 0, 0, 65536, 65536, 249, false, 0, false, 0, 0, 0, 0, 0);
            _region.Zones.Add(_zone);
            var legion = Npc<TestLegion>(10_000, 10_000, 1_000);
            var brain = Brain<LegionBrain>(legion);
            var bots = Enumerable.Range(0, 10).Select(i => Npc<Bot>(10_100 + i * 50, 10_000, 1_000)).ToList();
            foreach (Bot bot in bots) brain.AddToAggroList(bot, 100);
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "TestLegion", false);
            for (int i = 0; i < 20; i++)
                Call(brain, "ThrowPlayer", new object[] { null });
            Assert.That(bots.Sum(b => b.Moves), Is.Zero, "off: no bot is moved to Legion's platform");
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "TestLegion", EpicEncounterMechanics.ValueFor("AllowBotDisplacement", "Legion"));
            Call(brain, "ThrowPlayer", new object[] { null });
            // He skips the top five of his aggro list, then takes 8-16 of the rest: here all five others.
            Assert.That(bots.Sum(b => b.Moves), Is.EqualTo(5), "as shipped: the five bots below his top five go up");
            Assert.That(EncounterDisplacement.CompanionsAlwaysStay, Does.Contain("Legion"), "companions never follow a player up there");
        }

        [Test]
        public void CompanionsStayWhenTheirPlayerIsThrownUnlessTheSettingSaysOtherwise()
        {
            var xanxicar = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Xanxicar));
            var legion = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Scripts.Legion));
            Assert.That(DOL.GS.ServerProperties.Properties.EPIC_COMPANIONS_STAY_WHEN_THROWN, Is.True, "owner default");
            Assert.That(EncounterDisplacement.CompanionsStayFor(xanxicar), Is.True);
            bool before = DOL.GS.ServerProperties.Properties.EPIC_COMPANIONS_STAY_WHEN_THROWN;
            try
            {
                DOL.GS.ServerProperties.Properties.EPIC_COMPANIONS_STAY_WHEN_THROWN = false;
                Assert.That(EncounterDisplacement.CompanionsStayFor(xanxicar), Is.False, "off: they come along, like a portal");
                Assert.That(EncounterDisplacement.CompanionsStayFor(legion), Is.True, "Legion's platform: always stay");
            }
            finally { DOL.GS.ServerProperties.Properties.EPIC_COMPANIONS_STAY_WHEN_THROWN = before; }
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

        [Test]
        public void LegionsKillHealStopsAtThePeriodRaidsCountInFiveMinutes()
        {
            var legion = Npc<TestLegion>(10_000, 10_000, 1_000);
            var bot = Npc<Bot>(10_100, 10_000, 1_000);
            Assert.That(EncounterFloodGuard.Limit("Legion.KillHeal"), Is.EqualTo(64), "Darkness Falls' period raid (docs/RAID-SIZES.md)");
            for (int i = 0; i < 150; i++)
                legion.EnemyKilled(bot);
            Assert.That(legion.Heals, Is.EqualTo(64), "150 dead bots heal him as a whole 64-player raid did, not 150 times");
            EncounterFloodGuard.SetLimit("Legion.KillHeal", 0);
            legion.EnemyKilled(bot);
            Assert.That(legion.Heals, Is.EqualTo(65), "setting 0: no cap");
        }

        [Test]
        public void NosdodenRaisesNoSpiritPastThePeriodRaidsCount()
        {
            var nosdoden = Npc<TestNosdoden>(10_000, 10_000, 1_000);
            var bot = Npc<Bot>(10_100, 10_000, 1_000);
            EncounterFloodGuard.SetLimit("Nosdoden.Spirits", 3);
            for (int i = 0; i < 3; i++)
                SetHealth(Npc<NosdodenGhostAdd>(10_200 + i * 20, 10_000, 1_000), 100);
            Assert.DoesNotThrow(() => nosdoden.EnemyKilled(bot));
            Assert.That(nosdoden.GetNPCsInRadius(5000).OfType<NosdodenGhostAdd>().Count(), Is.EqualTo(3), "at the cap: no new spirit");
            Assert.That(EncounterFloodGuard.ShippedLimit("Nosdoden.Spirits"), Is.EqualTo(64), "Trollheim's period raid");
        }
    
    }
}
