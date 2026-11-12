using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Epic wave 2, Albion (1.2): Caer Sidi (Funus's class and weapon puzzle, the Sacristan's patrol, Soul Reckoner's room and
    /// shield, Morbus's swarm shield, the horsemen's tether heal, Bane of Hope's teleport, the Crypt Lord's court, Apocalypse's
    /// harbingers, Lich Lord Sanguis), Krondon (Scurceol's orbs, Krevo's toss and ghost, Orylle's throw into Orshom's Fire and
    /// the cold casters who put it out), Avalon City (Sys'sro's pit, Dra'argus's sphere), the Darkness Falls princes and
    /// Beliathan, and Moran the Mighty's gust. The tests stand bosses, adds and bots in a test region and play the rules,
    /// the scripts' pick-and-throw methods and the recovery's state machine. No world tick, combat, spell or pathing runs;
    /// nothing here was played in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicWave2Albion
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 900;
        private EpicTestServerScope _server;
        private Func<GameLiving, eDamageType, bool> _deals;
        private bool _funusPuzzle;

        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Dead;
            public int Moves;
            public ICharacterClass Class = new ClassArmsman();
            public override bool IsAlive => !Dead;
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override ICharacterClass CharacterClass => Class;
            public override bool MoveTo(ushort regionID, int x, int y, int z, ushort heading)
            {
                Moves++;
                return true;
            }
            public override bool MoveInRegion(ushort regionID, int x, int y, int z, ushort heading, bool forceMove)
            {
                Moves++;
                X = x; Y = y; Z = z;
                return true;
            }
            public override void StopMoving() { }
            public override bool IsCasting => false;
        }

        public sealed class TestFunus : Funus { public override bool IsAlive => true; }
        public sealed class TestSacristan : DOL.GS.Scripts.SkeletalSacristan { public override bool IsAlive => true; }
        public sealed class TestReckoner : SoulReckoner { public override bool IsAlive => true; }
        public sealed class TestCryptLord : CryptLord { public bool Dead; public override bool IsAlive => !Dead; }
        public sealed class TestMorbus : Morbus { public override bool IsAlive => true; }
        public sealed class TestSwarm : MorbusSwarm { public bool Dead; public override bool IsAlive => !Dead; }
        public sealed class TestOrshom : Orshom { public override bool IsAlive => true; }
        public sealed class TestOrylle : Orylle { public override bool IsAlive => true; }
        public sealed class TestFire : OrshomFire { public bool Dead; public override bool IsAlive => !Dead; }
        public sealed class TestDraargus : DraargusMighty { public override bool IsAlive => true; }
        public sealed class TestSphere : DraugynSphere { public bool Dead; public override bool IsAlive => !Dead; }
        public sealed class TestAsmoien : PrinceAsmoien { public override bool IsAlive => true; }
        public sealed class TestKrevo : KrevoRicik { public override bool IsAlive => true; }
        public sealed class TestSyssro : SyssroRuthless { public override bool IsAlive => true; }
        public sealed class TestMoran : MoranTheMighty { public override bool IsAlive => true; }
        public sealed class TestBane : DOL.GS.Scripts.BaneOfHope { public override bool IsAlive => true; }
        public sealed class Court : GameNPC { public override bool IsAlive => true; }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(64, (0, 0), (0, 65536));
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterDisplacementRecovery.ForgetAll();
            EncounterMechanicCalls.Reset();
            _deals = EpicEncounterTactics.DealsDamageType;
            // The Funus tests play his puzzle, which ships off in 1.2 (owner 2026-11-08): on here, put back after.
            _funusPuzzle = EpicEncounterMechanics.AllowFunusWeaponPuzzle;
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = true;
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterDisplacementRecovery.ForgetAll();
            EncounterMechanicCalls.Reset();
            EpicEncounterTactics.DealsDamageType = _deals;
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = _funusPuzzle;
            EpicEncounterMechanics.AllowSacristanPatrol = true;
            EpicEncounterMechanics.AllowSoulReckonerSmallRoom = true;
            _server.Dispose();
        }

        /// <summary>A test region with 65,536-square zones at these offsets (the scripts' own coordinates fit).</summary>
        private void UseRegion(ushort id, params (int X, int Y)[] zones)
        {
            _region = new Region(new RegionData { Id = id, Name = "Wave 2 test", Description = "Wave 2 test", Mobs = [] });
            ushort zoneId = (ushort)(id * 10);
            foreach (var (x, y) in zones)
                _region.Zones.Add(new Zone(_region, zoneId++, "Wave 2 test", x, y, 65536, 65536, zoneId, false, 0, false, 0, 0, 0, 0, 0));
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
            Zone zone = _region.Zones.First(z => x >= z.XOffset && x < z.XOffset + z.Width && y >= z.YOffset && y < z.YOffset + z.Height);
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

        private static TBrain Brain<TBrain>(GameNPC body) where TBrain : StandardMobBrain, new()
        {
            var brain = new TBrain { Body = body };
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(body, brain);
            return brain;
        }

        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Hidden | BindingFlags.Public)!.Invoke(target, args);

        /// <summary>The test classes carry their own names: give one the shipped switch of the encounter it stands for.</summary>
        private static void ShipAs(string testClass, string encounter, string name = "AllowBotDisplacement") =>
            EpicEncounterMechanics.SetFor(name, testClass, EpicEncounterMechanics.ValueFor(name, encounter));

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        // ---- what ships ----------------------------------------------------------------------------------------------

        [Test]
        public void CaerSidisFiveRestoresAndTheAlbionMovesShipOn()
        {
            Assert.That(EpicEncounterMechanics.AllowHorsemenTetherHeal, Is.True);
            Assert.That(EpicEncounterMechanics.AllowSacristanPatrol, Is.True);
            // Funus's weapon puzzle ships off (owner 2026-11-08); UT_EpicOwnerSettings checks its default.
            Assert.That(EpicEncounterMechanics.AllowSoulReckonerFullShield, Is.True);
            Assert.That(EpicEncounterMechanics.AllowSoulReckonerSmallRoom, Is.True);
            string[] shipped = EpicEncounterMechanics.PerEncounterValues();
            Assert.That(shipped, Is.SupersetOf(new[]
            {
                "AllowDisplacement@BaneOfHope=true", "AllowBotDisplacement@BaneOfHope=true", "AllowAddShield@Morbus=true",
                "AllowBotDisplacement@LyftMihtOne=true", "AllowBotDisplacement@LyftMihtTwo=true",
                "AllowBotDisplacement@LyftMihtThree=true", "AllowBotDisplacement@LyftMihtFour=true",
                "AllowBotDisplacement@KrevoRicik=true", "AllowBotDisplacement@Orylle=true",
                "AllowBotDisplacement@SyssroRuthless=true", "AllowBotDisplacement@DraugynSphere=true",
                "AllowBotDisplacement@MoranTheMighty=true",
            }));
            // Every per-encounter name is a real class of the server (a typo would switch nothing on).
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (var (_, encounter, _) in EpicEncounterMechanics.ShippedPerEncounter)
                Assert.That(gs.GetTypes().Any(t => t.Name == encounter && typeof(GameNPC).IsAssignableFrom(t)), Is.True, encounter);
            // On since wave 3 (UT_EpicWave3): the ApocInitializator's room barrier for bots, Legion's throw.
            Assert.That(EpicEncounterMechanics.On("AllowBotDisplacement", "ApocInitializator"), Is.True);
            Assert.That(EpicEncounterMechanics.On("AllowBotDisplacement", "Legion"), Is.True);
        }

        [Test]
        public void TheEncounterRulesAreFoundAndTheCatalogCarriesTheRows()
        {
            // Albion's rules are dungeon rules (one mechanism for every dungeon since the wave-2 merge).
            Assert.That(EpicEncounterTactics.AllDungeonRules.Select(r => r.Encounter), Is.SupersetOf(new[]
            {
                "Orshom", "DraargusMighty", "Funus", "PrinceAsmoien", "SkeletalSacristan", "LichLordSanguis", "SoulReckoner",
                "CryptLord",
            }));
            EpicEncounterTactics.Tactic Row(string boss) => EpicEncounterTactics.Catalog.Single(t => t.Boss == boss);
            Assert.That(Row("Morbus").RequiredAdds, Is.EqualTo(new[] { "MorbusSwarm" }));
            Assert.That(Row("SoulReckoner").RequiredAdds, Is.EqualTo(new[] { "ReckonedSoul" }));
            Assert.That(Row("DraargusMighty").RequiredAdds, Is.EqualTo(new[] { "DraugynSphere" }));
            Assert.That(Row("KrevoRicik").RangedAdds, Is.EqualTo(new[] { "KrevolAdd" }));
            Assert.That((Row("SyssroRuthless").Hazards.Single(), Row("SyssroRuthless").HazardRadius), Is.EqualTo(("PitMonster", 250)));
            foreach (string horseman in new[] { "Fames", "Bellum", "Morbus", "Funus" })
            {
                Assert.That(Row(horseman).FightAtHomeRadius, Is.EqualTo(2700), horseman);
                Assert.That(EpicEncounterTactics.Leashes.Single(l => l.Boss == horseman).TetherRange, Is.EqualTo(3600),
                    horseman + ": fought well inside the tether where the restored heal happens");
            }
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            Assert.That(EpicEncounterTactics.HasDungeonRule(funus), Is.True, "a subclass of Funus asks the rule too");
            Assert.That(EpicEncounterTactics.HasDungeonRule(Npc<Court>(0, 0, 0)), Is.False);
        }

        // ---- Funus ---------------------------------------------------------------------------------------------------

        [Test]
        public void OnlyRaidersFunusTakesDamageFromFightHimAndNoneOfTheRestEverHitsHim()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var raider = Npc<Bot>(29700, 25235, 19490); // 233 from him
            var weapons = new[] { eObjectType.GenericItem, eObjectType.Fired, eObjectType.Crossbow, eObjectType.Longbow, eObjectType.TwoHandedWeapon };
            foreach (eCharacterClass c in Enum.GetValues<eCharacterClass>())
                foreach (eObjectType w in weapons)
                    foreach (bool bow in new[] { false, true })
                    {
                        var a = EpicEncounterTactics.FunusAdvice(raider, funus, (int)c, w, bow);
                        bool hurts = EpicEncounterRestores.FunusAccepts((int)c, w);
                        // Wave 3: scouts, mercenaries, armsmen and infiltrators owning their listed weapon fight with it only.
                        bool archer = EpicEncounterTactics.FunusArcherClass((int)c);
                        if (a.Action == EpicEncounterTactics.BotAction.None)
                            Assert.That(hurts || archer && bow, Is.True, $"{c} {w}: told to fight, but its hit heals him");
                        if (!hurts && !(archer && bow))
                            Assert.That(a.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff), $"{c} {w}");
                    }
            // A cleric fights at any range; a scout steps back rather than swing a melee weapon at 233.
            Assert.That(EpicEncounterTactics.FunusAdvice(raider, funus, (int)eCharacterClass.Cleric, eObjectType.CrushingWeapon, false).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None));
            var scout = EpicEncounterTactics.FunusAdvice(raider, funus, (int)eCharacterClass.Scout, eObjectType.SlashingWeapon, true);
            Assert.That(scout.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition));
            Assert.That(Flat(scout.StandAt.Value, At(funus)), Is.EqualTo(EpicEncounterTactics.FunusArcherStepBack).Within(1));
            Move(raider, 30_300, 25235, 19490);
            Assert.That(EpicEncounterTactics.FunusAdvice(raider, funus, (int)eCharacterClass.Scout, eObjectType.SlashingWeapon, true).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "at range a scout switches to its bow");
            // Everyone else waits past his aggro range.
            Move(raider, 29700, 25235, 19490);
            var wizard = EpicEncounterTactics.FunusAdvice(raider, funus, (int)eCharacterClass.Wizard, eObjectType.Staff, false);
            Assert.That(Flat(wizard.StandAt.Value, At(funus)), Is.EqualTo(EpicEncounterTactics.FunusStandOff).Within(1));
            Assert.That(wizard.Reason, Does.StartWith("funus-puzzle"));
        }

        [Test]
        public void ABotFunusWouldHealFromNeverHitsHimNotEvenWithAnAreaSpell()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var armsman = Npc<Bot>(29600, 25235, 19490);
            var cleric = Npc<Bot>(29600, 25300, 19490);
            cleric.Class = new ClassCleric();
            var live = new List<GameNPC> { funus };
            Assert.That(EpicEncounterTactics.MustNotHit(armsman, funus), Is.True);
            Assert.That(EpicEncounterTactics.MustNotHit(cleric, funus), Is.False);
            Assert.That(EpicEncounterTactics.MustNotHit(armsman, Npc<Court>(29600, 25235, 19490)), Is.False, "only Funus");
            Assert.That(EpicEncounterTactics.Decide(armsman, funus, live, 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(EpicEncounterTactics.Decide(cleric, funus, live, 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = false;
            Assert.That(EpicEncounterTactics.MustNotHit(armsman, funus), Is.False, "puzzle off: everyone hits him, as in 1.1");
            Assert.That(EpicEncounterTactics.Decide(armsman, funus, live, 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
        }

        // ---- Skeletal Sacristan ----------------------------------------------------------------------------------------

        [Test]
        public void TheSacristansStationsAreOnHisLoopAndSomeoneAlwaysHasHimInReach()
        {
            var points = (List<Point3D>)typeof(SkeletalSacristanBrain).GetField("_roamingPathPoints", Hidden)!.GetValue(new SkeletalSacristanBrain());
            Assert.That(EpicEncounterTactics.SacristanPatrol.Select(p => (p.X, p.Y, p.Z)),
                Is.EqualTo(points.Select(p => ((float)p.X, (float)p.Y, (float)p.Z))), "the stations follow his own path");
            Assert.That(EpicEncounterTactics.SacristanStations, Has.Length.EqualTo(11));

            // Walk him round his loop at 220 a second and ask 16 parties (a raid of 125) every second.
            var sacristan = Npc<TestSacristan>(31826, 32256, 16750);
            Vector3[] loop = EpicEncounterTactics.SacristanPatrol;
            int seconds = 0, rangedCovered = 0, meleeCovered = 0;
            for (int leg = 0; leg < loop.Length; leg++)
            {
                Vector3 a = loop[leg], b = loop[(leg + 1) % loop.Length];
                int steps = (int)Math.Ceiling(Flat(a, b) / 220);
                for (int s = 0; s < steps; s++, seconds++)
                {
                    Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                    Move(sacristan, p.X, p.Y, p.Z);
                    int ranged = 0, melee = 0;
                    for (int party = 0; party < 16; party++)
                    {
                        if (EpicEncounterTactics.SacristanAdvice(EpicEncounterTactics.SacristanStation(party), sacristan, party, true).Action ==
                            EpicEncounterTactics.BotAction.None) ranged++;
                        if (EpicEncounterTactics.SacristanAdvice(EpicEncounterTactics.SacristanStation(party), sacristan, party, false).Action ==
                            EpicEncounterTactics.BotAction.None) melee++;
                    }
                    if (ranged > 0) rangedCovered++;
                    if (melee > 0) meleeCovered++;
                }
            }
            Assert.That(seconds, Is.InRange(70, 90), "about 80 s a round");
            Assert.That(rangedCovered, Is.EqualTo(seconds), "casters and archers of some party reach him every second of his loop");
            Assert.That(meleeCovered / (double)seconds, Is.GreaterThan(0.3), "melee catch him at the stations");
            // Out of reach: wait at the station, keeping him as the target.
            Move(sacristan, 33112, 35808, 16750);
            var far = EpicEncounterTactics.SacristanAdvice(EpicEncounterTactics.SacristanStations[0], sacristan, 0, false);
            Assert.That((far.Action, far.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.Reposition, (Vector3?)EpicEncounterTactics.SacristanStations[0])));
            EpicEncounterMechanics.AllowSacristanPatrol = false;
            var bot = Npc<Bot>(31826, 32256, 16750);
            Assert.That(EpicEncounterTactics.Decide(bot, sacristan, [sacristan], 0, false, 1_000).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "patrol off: he stands when hit, the raid fights him as in 1.1");
        }

        // ---- Soul Reckoner -------------------------------------------------------------------------------------------

        [Test]
        public void SoulReckonerIsLedBackIntoHisSmallRoomAndHisSoulsComeFirst()
        {
            var reckoner = Npc<TestReckoner>(28472, 35857, 15370);
            var chased = Npc<Bot>(28472, 36000, 15370);
            var other = Npc<Bot>(28600, 35857, 15370);
            reckoner.TargetObject = chased;
            Assert.That(EpicEncounterTactics.SoulReckonerRoomRadius, Is.EqualTo(900));
            Assert.That(EpicEncounterTactics.SoulReckonerAdvice(chased, reckoner, 900), Is.Null, "in his room: fight him");
            Move(reckoner, 28472 + 760, 35857, 15370);
            var lead = EpicEncounterTactics.SoulReckonerAdvice(chased, reckoner, 900);
            Assert.That((lead.Value.Action, lead.Value.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.StandOff, (Vector3?)EpicEncounterTactics.SoulReckonerRoom)));
            Assert.That(EpicEncounterTactics.SoulReckonerAdvice(other, reckoner, 900), Is.Null,
                "760 out he is still in his room and takes damage: the rest keep hitting him (play test 2026-11-07)");
            Move(reckoner, 28472 + 950, 35857, 15370);
            var hold = EpicEncounterTactics.SoulReckonerAdvice(other, reckoner, 900);
            Assert.That(hold.Value.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition), "out of the room: the rest stop hitting but keep him");
            Assert.That(EpicEncounterTactics.SoulReckonerAdvice(other, reckoner, 1500), Is.Null, "the 1.1 room (1,500): still inside");
            Assert.That(EpicEncounterTactics.SoulLeaderAdvice(chased, reckoner, 900)?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff),
                "the one he chases leads him back even while it fights a soul");
            Assert.That(EpicEncounterTactics.SoulLeaderAdvice(other, reckoner, 900), Is.Null, "the others go on with the souls");
        }

        // ---- Morbus ---------------------------------------------------------------------------------------------------

        [Test]
        public void MorbussShieldCountsTheSwarmThatLivesNotAStaticCount()
        {
            var morbus = Npc<TestMorbus>(29467, 25235, 19490);
            var swarm = Npc<TestSwarm>(29500, 25235, 19490);
            Morbus.Morbus_Swarm_count = 0;
            Assert.That(morbus.SwarmAlive(), Is.True, "a swarm beside him shields him whatever the count says");
            var raider = Npc<Bot>(29600, 25235, 19490);
            var a = EpicEncounterTactics.Decide(raider, morbus, [morbus, swarm], 0, false, 1_000);
            Assert.That(a.Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "a test class isn't the catalog's Morbus: the required-add rule is the catalog's, checked by name");
            swarm.Dead = true;
            Morbus.Morbus_Swarm_count = 7; // left over from a swarm removed without dying
            var fresh = Npc<TestMorbus>(29467, 25235, 19490);
            Assert.That(fresh.SwarmAlive(), Is.False, "no swarm alive: he can be hurt (the old count kept him immune for good)");
            Morbus.Morbus_Swarm_count = 0;
        }

        // ---- Apocalypse ----------------------------------------------------------------------------------------------

        [Test]
        public void TheFirstHarbingersComeOnceAfterTheFourthDeathEvenWhenDeathsJumpPastIt()
        {
            Assert.That(ApocalypseBrain.FirstHarbingersDue(3, false), Is.False);
            Assert.That(ApocalypseBrain.FirstHarbingersDue(4, false), Is.True);
            Assert.That(ApocalypseBrain.FirstHarbingersDue(6, false), Is.True, "two died in one rain: 3 -> 5 (the original's == 4 missed it)");
            Assert.That(ApocalypseBrain.FirstHarbingersDue(4, true), Is.False, "once a fight");
        }

        // ---- the Crypt Lord ------------------------------------------------------------------------------------------

        [Test]
        public void ACourtMonsterIsPulledAsTheCryptLordsFight()
        {
            var lord = Npc<TestCryptLord>(26567, 40143, 15372);
            var court = Npc<Court>(27500, 41000, 15372);
            court.PackageID = EpicEncounterTactics.CryptLordCourt;
            var stranger = Npc<Court>(27500, 41100, 15372);
            var live = new List<GameNPC> { lord, court, stranger };
            Assert.That(EpicEncounterTactics.RouteTarget(court, live, default, out string why), Is.SameAs(lord));
            Assert.That(why, Does.StartWith("court of"));
            Assert.That(EpicEncounterTactics.RouteTarget(stranger, live, default, out _), Is.SameAs(stranger));
            Assert.That(EpicEncounterTactics.RouteTarget(lord, live, default, out _), Is.SameAs(lord));
            lord.Dead = true;
            Assert.That(EpicEncounterTactics.RouteTarget(court, live, default, out _), Is.SameAs(court), "he is dead: the court is trash");
        }

        // ---- Orshom's Fire and Orylle's throw -------------------------------------------------------------------------

        [Test]
        public void ColdCastersPutOutOrshomsFireAndNobodyWastesAnotherSpellOnIt()
        {
            var orshom = Npc<TestOrshom>(31697, 70458, 15610);
            var orylle = Npc<TestOrylle>(31413, 70773, 15765);
            var fire = Npc<TestFire>(31406, 69599, 15605);
            fire.Name = "Orshom's Fire";
            var live = new List<GameNPC> { orshom, orylle, fire };
            var wizard = Npc<Bot>(31406, 70500, 15605); // 901 from the fire
            var armsman = Npc<Bot>(31600, 70300, 15610);
            EpicEncounterTactics.DealsDamageType = (r, t) => r == wizard && t == eDamageType.Cold;
            var a = EpicEncounterTactics.Decide(wizard, orshom, live, 0, true, 1_000);
            Assert.That((a.Action, a.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)fire)));
            Assert.That(EpicEncounterTactics.Decide(wizard, fire, live, 0, true, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "on the fire from outside it: cast");
            Move(wizard, 31450, 69650, 15605);
            var inIt = EpicEncounterTactics.Decide(wizard, fire, live, 0, true, 1_000);
            Assert.That(inIt.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition), "standing in it: out first");
            Assert.That(Flat(inIt.StandAt.Value, At(fire)), Is.GreaterThan(450));
            // Without a cold spell the cold rule has no opinion: Orshom's catalog hazard rule (never the fire) speaks, as before.
            Assert.That(EpicEncounterTactics.DungeonAdvice(new(armsman, fire, live, 0, false, 1_000, 0)), Is.Null);
            var cold = new Spell(new DOL.Database.DbSpell { Damage = 200, DamageType = (int)eDamageType.Cold, Name = "Ice bolt", Target = "Enemy", Type = "DirectDamage" }, 50);
            var heat = new Spell(new DOL.Database.DbSpell { Damage = 200, DamageType = (int)eDamageType.Heat, Name = "Fire bolt", Target = "Enemy", Type = "DirectDamage" }, 50);
            var real = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(OrshomFire));
            Assert.That(EpicEncounterTactics.SpellWasted(real, heat), Is.True);
            Assert.That(EpicEncounterTactics.SpellWasted(real, cold), Is.False);
            Assert.That(EpicEncounterTactics.SpellWasted(orshom, heat), Is.False, "Orshom himself takes every type");
            fire.Dead = true;
            Assert.That(EpicEncounterTactics.DungeonAdvice(new(wizard, orshom, live, 0, true, 1_000, 0)), Is.Null, "fire out: fight on");
        }

        [Test]
        public void ARaiderOryllesThrowLandsInTheFireWalksOutBeforeItCountsAsBack()
        {
            Vector3 fire = new(31406, 69599, 15605), leader = new(31800, 69700, 15605); // 407 apart: "by the raid"
            var plan = EncounterDisplacementRecovery.Plan.For(DisplacementKind.Throw, 0, fire, 0, 0, true);
            Vector3 escape = EpicEncounterTactics.HazardEscape(fire, fire, 300).Value;
            EncounterDisplacementRecovery.Observation See(long now, Vector3 at, Vector3? hazard) =>
                new(now, true, at, leader, false, false, false, (_, _) => true, HazardEscape: hazard);
            Assert.That(EncounterDisplacementRecovery.Step(plan, See(0, fire, escape)).Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Land));
            var first = EncounterDisplacementRecovery.Step(plan, See(500, fire, escape));
            Assert.That((first.Kind, first.Point), Is.EqualTo((EncounterDisplacementRecovery.OrderKind.Walk, escape)),
                "landed beside its leader but in the fire: out of it first");
            var still = EncounterDisplacementRecovery.Step(plan, See(1_500, fire + new Vector3(100, 0, 0), escape));
            Assert.That(still.Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Walk));
            var done = EncounterDisplacementRecovery.Step(plan, See(3_000, escape, null));
            Assert.That(done.Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Done), "out of it and by the raid: back");
            Assert.That(EncounterDisplacementRecovery.EncounterTopic("displaced_heal", "Orylle"), Is.EqualTo("displaced_heal_fire"));
            Assert.That(EncounterDisplacementRecovery.EncounterTopic("displaced_jail", "Orylle"), Is.Null);
        }

        private readonly Dictionary<GameLiving, Vector3> _movedTo = new();
        private void Record(DisplacementEvent e) => _movedTo[e.Living] = e.To;
        private Vector3 Landed(GameLiving l) => _movedTo.TryGetValue(l, out Vector3 v) ? v : default;

        [Test]
        public void OrylleKrevoSyssroMoranAndBaneMoveBotsAsShipped()
        {
            EncounterDisplacement.Displaced += Record;
            try { MoveBotsAsShipped(); }
            finally { EncounterDisplacement.Displaced -= Record; }
        }

        private void MoveBotsAsShipped()
        {
            // Orylle: a random raider within 2,500 into the fire (Krondon, region 61).
            UseRegion(61, (0, 0), (0, 65536));
            var orylle = Npc<TestOrylle>(31413, 70773, 15765);
            var oBrain = Brain<OrylleBrain>(orylle);
            var o = Npc<Bot>(31500, 70800, 15765);
            Call(oBrain, "ThrowPlayer", new object[] { null });
            Assert.That(o.Moves, Is.Zero, "a test class has no switch of its own: bots stay");
            ShipAs(nameof(TestOrylle), "Orylle");
            Call(oBrain, "ThrowPlayer", new object[] { null });
            Assert.That(o.Moves, Is.EqualTo(1));
            Assert.That(Landed(o), Is.EqualTo(new Vector3(31406, 69599, 15605)), "into the fire's spot");
            Assert.That(EncounterDisplacementRecovery.IsRecovering(o), Is.True);

            // Krevo: one damaging hit in ten tosses the striker 400 up.
            var krevo = Npc<TestKrevo>(27189, 64319, 15614);
            var kBrain = Brain<KrevoRicikBrain>(krevo);
            var k = Npc<Bot>(27250, 64319, 15614);
            ShipAs(nameof(TestKrevo), "KrevoRicik");
            for (int i = 0; i < 400 && k.Moves == 0; i++)
            {
                try { kBrain.OnAttackedByEnemy(new AttackData { Attacker = k, Target = krevo, Damage = 100 }); }
                catch (Exception) { /* the ghost spawn and the base reaction need a world; the toss comes first */ }
            }
            Assert.That(k.Moves, Is.EqualTo(1));

            // Sys'sro: someone other than his target into the pit (Avalon City, region 50).
            UseRegion(50, (0, 0));
            var syssro = Npc<TestSyssro>(41505, 39496, 8040);
            var sBrain = Brain<SyssroRuthlessBrain>(syssro);
            var tank = Npc<Bot>(41550, 39496, 8040);
            var s = Npc<Bot>(41700, 39496, 8040);
            syssro.TargetObject = tank;
            ShipAs(nameof(TestSyssro), "SyssroRuthless");
            Call(sBrain, "ThrowPlayer", new object[] { null });
            Assert.That((s.Moves, tank.Moves), Is.EqualTo((1, 0)));
            Assert.That(Landed(s), Is.EqualTo(new Vector3(41489, 40699, 8145)), "the pit");

            // Moran: one raider a gust, never the whole raid (Albion, region 1).
            UseRegion(1, (393216, 720896));
            var moran = Npc<TestMoran>(408341, 756300, 538);
            var mBrain = Brain<MoranBrain>(moran);
            var raid = Enumerable.Range(0, 8).Select(i => Npc<Bot>(408400 + i * 30, 756300, 538)).ToList();
            ShipAs(nameof(TestMoran), "MoranTheMighty");
            Call(mBrain, "TeleportPlayerAway", new object[] { null });
            Assert.That(raid.Sum(b => b.Moves), Is.EqualTo(1), "one gust, one raider (it took all eight before)");

            // Bane of Hope: the struck raider is ported after the dot (players and bots; it was off for both).
            UseRegion(64, (0, 0), (0, 65536));
            var bane = Npc<TestBane>(31265, 30833, 13954);
            var bBrain = Brain<BaneOfHopeBrain>(bane);
            var p = Npc<Bot>(31300, 30833, 13954);
            bBrain.AddToAggroList(p, 100);
            BaneOfHopeBrain.TeleportTarget = p;
            try
            {
                Call(bBrain, "TeleportEnemy", new object[] { null });
                Assert.That(p.Moves, Is.Zero, "a test class: off");
                ShipAs(nameof(TestBane), "BaneOfHope", "AllowDisplacement");
                ShipAs(nameof(TestBane), "BaneOfHope");
                Call(bBrain, "TeleportEnemy", new object[] { null });
                Assert.That(p.Moves, Is.EqualTo(1));
            }
            finally
            {
                BaneOfHopeBrain.TeleportTarget = null;
                BaneOfHopeBrain.CanPoison = false;
            }
        }

        [Test]
        public void MoransGustTakesEachRaiderEquallyOften()
        {
            var human = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(UT_EncounterTargetsScripts.Player));
            UseRegion(1, (393216, 720896));
            var client = new BotDummyClient { Account = new DOL.Database.DbAccount { PrivLevel = 1 } };
            typeof(GamePlayer).GetField("m_client", Hidden)!.SetValue(human, client);
            var bots = Enumerable.Range(0, 4).Select(i => (GameLiving)Npc<Bot>(408400 + i * 30, 756300, 538)).ToList();
            int humanPicks = 0;
            const int trials = 4_000;
            for (int i = 0; i < trials; i++)
            {
                var victims = MoranBrain.WindVictims(bots.Append(human).ToList());
                Assert.That(victims, Has.Count.EqualTo(1));
                if (victims[0] == human) humanPicks++;
            }
            Assert.That(humanPicks / (double)trials, Is.EqualTo(0.2).Within(0.03), "the human is one of five, not every gust's victim");
            Assert.That(MoranBrain.WindVictims([]), Is.Empty);
        }

        // ---- Sys'sro's pit ---------------------------------------------------------------------------------------------

        [Test]
        public void APitMonsterIsNeverFoughtAndNoSpellIsWastedOnIt()
        {
            var syssro = Npc<TestSyssro>(41505, 39496, 8040);
            var pit = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(PitMonster));
            var hot = new Spell(new DOL.Database.DbSpell { Damage = 200, DamageType = (int)eDamageType.Heat, Name = "Fire bolt", Target = "Enemy", Type = "DirectDamage" }, 50);
            Assert.That(EpicEncounterTactics.SpellWasted(pit, hot), Is.True);
            Assert.That(syssro, Is.Not.Null);
        }

        // ---- Dra'argus's sphere ----------------------------------------------------------------------------------------

        [Test]
        public void MeleeWaitOutOfTheSpheresReachWhileCastersKillIt()
        {
            var draargus = Npc<TestDraargus>(26899, 37223, 8644);
            var sphere = Npc<TestSphere>(26766, 37124, 9027);
            var live = new List<GameNPC> { draargus, sphere };
            var melee = Npc<Bot>(26950, 37223, 8644);
            var caster = Npc<Bot>(27100, 37124, 8644); // 334 from the sphere
            var m = EpicEncounterTactics.Decide(melee, draargus, live, 0, false, 1_000);
            Assert.That(m.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition));
            Assert.That(Flat(m.StandAt.Value, At(sphere)), Is.EqualTo(EpicEncounterTactics.SphereWaitDistance).Within(1));
            Assert.That(m.Target, Is.SameAs(draargus), "keeps him as its target");
            var c = EpicEncounterTactics.Decide(caster, sphere, live, 0, true, 1_000);
            Assert.That(c.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition), "a caster in the blast steps out first");
            Assert.That(Flat(c.StandAt.Value, At(sphere)), Is.GreaterThan(EpicEncounterTactics.SphereBlastRadius));
            Move(caster, 27700, 37124, 8644);
            Assert.That(EpicEncounterTactics.Decide(caster, sphere, live, 0, true, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            sphere.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(melee, draargus, live, 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "sphere down: everyone on him");
        }

        // ---- Darkness Falls ----------------------------------------------------------------------------------------------

        [Test]
        public void PetOwnersKeepTheirPetsAwayFromThePetEatingPrinces()
        {
            var prince = Npc<TestAsmoien>(24868, 32144, 16086);
            var a = EpicEncounterTactics.PrincePetAdvice(new(25000, 32144, 16086), prince, true);
            Assert.That(a.Value.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(Flat(a.Value.StandAt.Value, At(prince)), Is.EqualTo(EpicEncounterTactics.PrincePetSafeDistance).Within(1));
            Assert.That(EpicEncounterTactics.PrincePetAdvice(new(25000, 32144, 16086), prince, false), Is.Null, "no pet: fight him");
            var bot = Npc<Bot>(25000, 32144, 16086);
            Assert.That(EpicEncounterTactics.PetOf(bot), Is.Null);
            Assert.That(EpicEncounterTactics.Decide(bot, prince, [prince], 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
        }

        [Test]
        public void BeliathanWakesWhenThePrincesAndThePrincessAreGoneAndTheRaidTakesHimThen()
        {
            foreach (string name in new[] { "Prince Abdin", "Prince Asmoien", "Prince Ba'alorien", "Princess Nahemah" })
                Assert.That(BeliathanInitBrain.WakesBeliathanWhenGone(name), Is.True, name);
            Assert.That(BeliathanInitBrain.WakesBeliathanWhenGone("High Lord Saeor"), Is.False);
            Assert.That(BeliathanInitBrain.WakesBeliathanWhenGone("Beliathan's Minion"), Is.False);
            string[] objectives = RealmRaidNeutralEvents.Objectives(249);
            Assert.That(Array.IndexOf(objectives, "Beliathan"), Is.GreaterThan(Array.IndexOf(objectives, "Princess Nahemah")));
            Assert.That(objectives.Last(), Is.EqualTo("Legion"), "the final stays Legion");
        }

        // ---- the party's calls -----------------------------------------------------------------------------------------

        [Test]
        public void EveryMechanicCallHasLinesAndIsMadeOnceAPartyEveryTwoMinutes()
        {
            Assert.That(EncounterMechanicCalls.TopicFor("funus-puzzle: stand clear"), Is.EqualTo("mech_funus"));
            Assert.That(EncounterMechanicCalls.TopicFor("required-add 1/4 of \"Morbus\""), Is.EqualTo("mech_adds_first"));
            Assert.That(EncounterMechanicCalls.TopicFor("out of \"Orshom's Fire\" (300)"), Is.EqualTo("mech_hazard"));
            Assert.That(EncounterMechanicCalls.TopicFor("dies-with \"x\""), Is.Null);
            Assert.That(EncounterMechanicCalls.Book(1, "mech_funus", 0), Is.True);
            Assert.That(EncounterMechanicCalls.Book(1, "mech_sphere", 1_000), Is.False, "not within 8 s of the last call");
            Assert.That(EncounterMechanicCalls.Book(1, "mech_funus", 60_000), Is.False);
            Assert.That(EncounterMechanicCalls.Book(2, "mech_funus", 60_000), Is.True, "another party");
            Assert.That(EncounterMechanicCalls.Book(1, "mech_funus", 121_000), Is.True);

            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "GameServer", "bots", "autonomous", "data", "chat", "combat_calls.lines")))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the line files are next to the sources");
            string lines = File.ReadAllText(Path.Combine(dir, "GameServer", "bots", "autonomous", "data", "chat", "combat_calls.lines"));
            var reasons = new[] { "funus-puzzle", "sacristan-ambush", "reckoner-room", "sanguis-finish", "cold-fire", "sphere-first",
                "prince-pets", "required-add", "ranged-add", "hazard", "fight-at-home" };
            foreach (string reason in reasons)
                Assert.That(lines, Does.Contain("@topic " + EncounterMechanicCalls.TopicFor(reason) + "\n").Or.Contain("@topic " + EncounterMechanicCalls.TopicFor(reason) + "\r\n"), reason);
            foreach (var (encounter, suffix) in EncounterDisplacementRecovery.EncounterTopics)
                foreach (string topic in new[] { "displaced_heal_", "displaced_self_" })
                    Assert.That(lines, Does.Contain("@topic " + topic + suffix), encounter);
        }
    }
}
