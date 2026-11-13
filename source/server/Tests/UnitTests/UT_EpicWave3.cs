using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Epic wave 3 (1.2): Funus's archers (mercenaries, armsmen, infiltrators and scouts fight him with their listed weapon,
    /// no bot's pet touches him, a bot's damage shield doesn't heal him), the horsemen's room barrier for bots, Easmarach's
    /// waterfall finish and his push behind the falls, Torst and Hurika's flying patrol pull (left off), Legion's wounded and
    /// bubbles and his throw for bots, the Green Knight's raid start, the Shrouded Isles pack links and Legendary Afanc's
    /// throw. The tests stand bosses, adds and bots in a test region and play the rules, the scripts' methods and the
    /// recovery's state machine. No world tick, combat, spell or pathing runs; nothing here was played in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicWave3
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 1_900;
        private EpicTestServerScope _server;
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

        public sealed class TestFunus : Funus
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class TestEasmarach : Easmarach { public override bool IsAlive => true; }
        public sealed class TestOlcasgean : Olcasgean { public override bool IsAlive => true; }
        public sealed class TestXaga : Xaga { public override bool IsAlive => true; }
        public sealed class TestTorst : Torst { public override bool IsAlive => true; }
        public sealed class TestAfanc : LegendaryAfanc { public override bool IsAlive => true; }
        public sealed class TestQanris : QanrisDuros { public override bool IsAlive => true; }
        public sealed class TestLegion : DOL.GS.Scripts.Legion
        {
            public bool Fighting = true;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class TestKnight : OFGreenKnight { public override bool IsAlive => true; }
        public sealed class Court : GameNPC { public override bool IsAlive => true; }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(60, (0, 0));
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterDisplacementRecovery.ForgetAll();
            EncounterMechanicCalls.Reset();
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
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = _funusPuzzle;
            EpicEncounterMechanics.FunusBotShieldGuard = true;
            _server.Dispose();
        }

        private void UseRegion(ushort id, params (int X, int Y)[] zones)
        {
            _region = new Region(new RegionData { Id = id, Name = "Wave 3 test", Description = "Wave 3 test", Mobs = [] });
            ushort zoneId = (ushort)(id * 10);
            foreach (var (x, y) in zones)
                _region.Zones.Add(new Zone(_region, zoneId++, "Wave 3 test", x, y, 65536, 65536, zoneId, false, 0, false, 0, 0, 0, 0, 0));
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
            typeof(GameLiving).GetField("_abilitiesLock", Hidden)?.SetValue(npc, new Lock());
            typeof(GameLiving).GetField("m_abilities", Hidden)?.SetValue(npc, new Dictionary<string, Ability>());
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

        private static void ShipAs(string testClass, string encounter, string name = "AllowBotDisplacement") =>
            EpicEncounterMechanics.SetFor(name, testClass, EpicEncounterMechanics.ValueFor(name, encounter));

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the scripts are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray()));
        }

        /// <summary>A bot of <paramref name="c"/> with <paramref name="weapon"/> in its distance slot and the ability to use it.</summary>
        private Bot Archer(ICharacterClass c, eObjectType weapon, string ability, int x, int y, int z)
        {
            Bot bot = Npc<Bot>(x, y, z);
            bot.Class = c;
            bot.Inventory = new BotInventory();
            var template = new DbItemUnique(new DbItemTemplate
            {
                Id_nb = "w3_" + weapon, Name = weapon.ToString(), Object_Type = (int)weapon, Item_Type = Slot.RANGED,
                DPS_AF = 165, SPD_ABS = 43, Model = 226, Realm = (int)eRealm.Albion, Level = 50, LevelRequirement = 0,
                Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
            });
            Assert.That(bot.Inventory.AddItem(eInventorySlot.DistanceWeapon, GameInventoryItem.Create(template)), Is.True);
            var abilities = (Dictionary<string, Ability>)typeof(GameLiving).GetField("m_abilities", Hidden)!.GetValue(bot);
            abilities[ability] = new Ability(new DbAbility { KeyName = ability, Name = ability }, 1);
            return bot;
        }

        private static void SetSlot(GameLiving living, eActiveWeaponSlot slot) =>
            typeof(GameLiving).GetField("m_activeWeaponSlot", Hidden)!.SetValue(living, slot);

        // ---- what ships ---------------------------------------------------------------------------------------------

        [Test]
        public void WaveThreeShipsTheBarrierThePushLegionAndAfancButNotTheGriffons()
        {
            string[] shipped = EpicEncounterMechanics.PerEncounterValues();
            Assert.That(shipped, Is.SupersetOf(new[]
            {
                "AllowBotDisplacement@LegendaryAfanc=true",
                "AllowDisplacement@ApocInitializator=true", "AllowBotDisplacement@ApocInitializator=true",
                "AllowDisplacement@Easmarach=true", "AllowBotDisplacement@Easmarach=true",
                "AllowBotDisplacement@Legion=true",
            }));
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowGlacierGriffonPatrol), "Torst"), Is.False);
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowGlacierGriffonPatrol), "Hurika"), Is.False);
            Assert.That(EpicEncounterMechanics.AllowDisplacement, Is.False, "the global switch is unchanged");
            Assert.That(EpicEncounterMechanics.FunusBotShieldGuard, Is.True, "epic_funus_bot_shield_guard ships on");
            // The owner's per-boss setting still overrides every one of them.
            Assert.That(EpicEncounterMechanics.ParseOwnerSwitches("AllowDisplacement@Easmarach=False")[("AllowDisplacement", "Easmarach")], Is.False);
            Assert.That(EpicEncounterTactics.AllDungeonRules.Select(r => r.Encounter), Is.SupersetOf(new[]
                { "Funus", "ApocInitializator", "Easmarach", "Torst", "Legion" }));
        }

        // ---- Funus: the archers --------------------------------------------------------------------------------------

        [Test]
        public void MercenariesArmsmenAndInfiltratorsWithTheirListedWeaponFightFunus()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var raider = Npc<Bot>(30_500, 25235, 19490); // 1,033 from him
            foreach (var (c, listed) in new[]
                     {
                         (eCharacterClass.Mercenary, eObjectType.Fired), (eCharacterClass.Armsman, eObjectType.Crossbow),
                         (eCharacterClass.Infiltrator, eObjectType.Crossbow), (eCharacterClass.Scout, eObjectType.Longbow),
                     })
            {
                Assert.That(EpicEncounterTactics.FunusListedWeapon((int)c), Is.EqualTo(listed));
                Assert.That(EpicEncounterRestores.FunusAccepts((int)c, listed), Is.True, $"{c}: the script takes this weapon");
                Move(raider, 30_500, 25235, 19490);
                Assert.That(EpicEncounterTactics.FunusAdvice(raider, funus, (int)c, eObjectType.SlashingWeapon, true).Action,
                    Is.EqualTo(EpicEncounterTactics.BotAction.None), $"{c} owning it fights (it draws it)");
                Assert.That(EpicEncounterTactics.FunusAdvice(raider, funus, (int)c, eObjectType.SlashingWeapon, false).Action,
                    Is.EqualTo(EpicEncounterTactics.BotAction.StandOff), $"{c} without it stands clear");
                // Close to him and not his target: steps back rather than be in melee reach; his target stands and shoots.
                Move(raider, 29700, 25235, 19490);
                var close = EpicEncounterTactics.FunusAdvice(raider, funus, (int)c, listed, true);
                Assert.That(close.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition), c.ToString());
                Assert.That(Flat(close.StandAt.Value, At(funus)), Is.EqualTo(EpicEncounterTactics.FunusArcherStepBack).Within(1));
                funus.TargetObject = raider;
                Assert.That(EpicEncounterTactics.FunusAdvice(raider, funus, (int)c, listed, true).Action,
                    Is.EqualTo(EpicEncounterTactics.BotAction.None), $"{c}: he runs 300, his target can't outrun him");
                funus.TargetObject = null;
            }
            Assert.That(EpicEncounterTactics.FunusListedWeapon((int)eCharacterClass.Cleric), Is.EqualTo(eObjectType.GenericItem));
        }

        [Test]
        public void AnArcherFightsHimWithItsBowOnlyAndNeverWithItsMeleeWeapon()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var armsman = Archer(new ClassArmsman(), eObjectType.Crossbow, Abilities.Weapon_Crossbow, 30_400, 25235, 19490);
            var merc = Archer(new ClassMercenary(), eObjectType.Fired, Abilities.Weapon_Shortbows, 30_400, 25300, 19490);
            var wrong = Archer(new ClassArmsman(), eObjectType.Fired, Abilities.Weapon_Shortbows, 30_400, 25400, 19490);
            Assert.That(EpicEncounterTactics.OwnsFunusWeapon(armsman), Is.True);
            Assert.That(EpicEncounterTactics.OwnsFunusWeapon(merc), Is.True);
            Assert.That(EpicEncounterTactics.OwnsFunusWeapon(wrong), Is.False, "an armsman's short bow is not his crossbow");
            Assert.That(EpicEncounterTactics.FightsAtRange(armsman, funus), Is.True);
            Assert.That(EpicEncounterTactics.FightsAtRange(wrong, funus), Is.False);
            Assert.That(EpicEncounterTactics.FightsAtRange(armsman, Npc<Court>(29600, 25235, 19490)), Is.False, "only Funus");
            // Its melee weapon in hand, nothing of it may touch him; the crossbow drawn, it may.
            SetSlot(armsman, eActiveWeaponSlot.Standard);
            Assert.That(EpicEncounterTactics.MustNotHit(armsman, funus), Is.True);
            SetSlot(armsman, eActiveWeaponSlot.Distance);
            Assert.That(EpicEncounterTactics.MustNotHit(armsman, funus), Is.False);
            var live = new List<GameNPC> { funus };
            Assert.That(EpicEncounterTactics.Decide(armsman, funus, live, 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            Assert.That(EpicEncounterTactics.Decide(wrong, funus, live, 0, false, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = false;
            Assert.That(EpicEncounterTactics.FightsAtRange(armsman, funus), Is.False, "puzzle off: bots fight as they like");
        }

        // Play test duo13 (Caer Sidi, puzzle on): Funus stood on his scouts; the melee-pressure fallback put their swords in
        // hand (all four traced switches) and 50 of his 51 heals were scout sword hits. The fallback asks the rule first now.
        [Test]
        public void AScoutHeStandsOnKeepsItsBowAndNeverFallsBackToMelee()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var scout = Archer(new ClassScout(), eObjectType.Longbow, Abilities.Weapon_Longbows, 29_500, 25235, 19490);
            var noBow = Archer(new ClassScout(), eObjectType.Fired, Abilities.Weapon_Shortbows, 29_520, 25235, 19490);
            Assert.That(BotBrain.MayFallBackToMelee(scout, funus), Is.False, "a scout with its longbow: bow only on Funus");
            Assert.That(BotBrain.MayFallBackToMelee(noBow, funus), Is.True, "no longbow: the rule sends it clear, not to its bow");
            Assert.That(BotBrain.MayFallBackToMelee(scout, Npc<Court>(29600, 25235, 19490)), Is.True, "any other monster");
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = false;
            Assert.That(BotBrain.MayFallBackToMelee(scout, funus), Is.True, "puzzle off: bots fight as they like");
        }

        // Play test duo14: an interrupted draw stops the attack (its target forgotten) before the switch to melee is decided,
        // so the bot's own target must keep the bow too; and the reavers he turned on fought back (40 of 110 heals).
        [Test]
        public void TheBowStaysWhenTheAttackForgotItsTargetAndAClassHeHealsFromNeverStartsOnHim()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var scout = Archer(new ClassScout(), eObjectType.Longbow, Abilities.Weapon_Longbows, 29_500, 25235, 19490);
            var reaver = Archer(new ClassReaver(), eObjectType.Fired, Abilities.Weapon_Shortbows, 29_520, 25235, 19490);
            Assert.That(NpcAttackAction.KeepsDistanceWeaponFor(scout, null, funus), Is.True, "attack target forgotten");
            Assert.That(NpcAttackAction.KeepsDistanceWeaponFor(scout, funus, null), Is.True);
            Assert.That(NpcAttackAction.KeepsDistanceWeaponFor(scout, null, Npc<Court>(29600, 25235, 19490)), Is.False);
            Assert.That(GameBot.RefusesAttackOn(reaver, funus), Is.True, "a reaver's hits heal him");
            Assert.That(GameBot.RefusesAttackOn(scout, funus), Is.False, "a scout with its longbow fights him with it");
            Assert.That(GameBot.RefusesAttackOn(reaver, Npc<Court>(29600, 25235, 19490)), Is.False, "any other monster");
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = false;
            Assert.That(GameBot.RefusesAttackOn(reaver, funus), Is.False, "puzzle off");
        }

        [Test]
        public void EachClassStartsWithItsOwnKindOfDistanceWeapon()
        {
            Assert.That(BotRangedCombat.StarterOrder(eRealm.Albion, eCharacterClass.Mercenary)[0], Is.EqualTo(eObjectType.Fired));
            Assert.That(BotRangedCombat.StarterOrder(eRealm.Albion, eCharacterClass.Armsman)[0], Is.EqualTo(eObjectType.Crossbow));
            Assert.That(BotRangedCombat.StarterOrder(eRealm.Albion, eCharacterClass.Infiltrator)[0], Is.EqualTo(eObjectType.Crossbow));
            Assert.That(BotRangedCombat.StarterOrder(eRealm.Albion, eCharacterClass.Scout)[0], Is.EqualTo(eObjectType.Longbow));
            Assert.That(BotRangedCombat.StarterOrder(eRealm.Midgard, eCharacterClass.Hunter)[0], Is.EqualTo(eObjectType.CompositeBow));
            foreach (eCharacterClass c in Enum.GetValues<eCharacterClass>())
            {
                eObjectType[] order = BotRangedCombat.StarterOrder(eRealm.Albion, c);
                Assert.That(order.Distinct().Count(), Is.EqualTo(order.Length), c.ToString());
                Assert.That(order, Has.Length.EqualTo(6), "every kind is still tried: " + c);
            }
        }

        [Test]
        public void NoBotsPetTouchesFunusWhileHisPuzzleIsOn()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var owner = Npc<Bot>(30_400, 25235, 19490);
            var pet = Npc<Court>(30_350, 25235, 19490);
            var petBrain = new ControlledMobBrain(owner) { Body = pet };
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(pet, petBrain);
            Assert.That(EpicEncounterTactics.PetMustNotHit(pet, funus), Is.True);
            Assert.That(EpicEncounterTactics.PetMustNotHit(pet, Npc<Court>(29600, 25235, 19490)), Is.False, "only Funus");
            Assert.That(EpicEncounterTactics.PetMustNotHit(owner, funus), Is.False, "a bot is no pet");
            // An attack order on him is dropped (ControlledMobBrain.CheckAttackOrderTarget).
            typeof(ControlledMobBrain).GetField("m_orderAttackTarget", Hidden)!.SetValue(petBrain, funus);
            Assert.That(typeof(ControlledMobBrain).GetMethod("CheckAttackOrderTarget", Hidden)!.Invoke(petBrain, null), Is.Null);
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = false;
            Assert.That(EpicEncounterTactics.PetMustNotHit(pet, funus), Is.False, "puzzle off: pets fight him");
        }

        [Test]
        public void ABotsDamageShieldStingNeitherHealsNorHurtsHim()
        {
            var bot = Npc<Bot>(29600, 25235, 19490);
            object shield = RuntimeHelpers.GetUninitializedObject(typeof(DamageShieldSpellHandler));
            object add = RuntimeHelpers.GetUninitializedObject(typeof(DamageAddSpellHandler));
            object player = RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored(bot, shield, accepted: false), Is.True);
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored(bot, shield, accepted: true), Is.False, "a cleric's sting hurts him");
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored(bot, add, accepted: false), Is.False, "a hit's damage add is a hit");
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored((GamePlayer)player, shield, accepted: false), Is.False,
                "a real player keeps the original");
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored(bot, null, accepted: false), Is.False);
            EpicEncounterMechanics.FunusBotShieldGuard = false;
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored(bot, shield, accepted: false), Is.False, "the owner's switch");
            EpicEncounterMechanics.FunusBotShieldGuard = true;
            EpicEncounterMechanics.AllowFunusWeaponPuzzle = false;
            Assert.That(EpicEncounterMechanics.FunusShieldStingIgnored(bot, shield, accepted: false), Is.False, "no puzzle, no heal to stop");
        }

        // ---- the horsemen's room -------------------------------------------------------------------------------------

        [Test]
        public void EverySpotTheRaidIsSentToInTheHorsemensRoomIsInsideIt()
        {
            var room = EpicEncounterTactics.HorsemenRoom;
            Assert.That(Flat(room.Door, room.Middle), Is.GreaterThan(room.InsideRadius + room.DoorRadius + 150),
                "inside the room, a raider is well clear of the door's circle");
            foreach (Vector3 p in new[] { room.Door, new Vector3(29459, 27500, 19503), new Vector3(31000, 25244, 19490), room.Middle })
            {
                Vector3 inside = EpicEncounterTactics.KeepInside(room, p);
                Assert.That(Flat(inside, room.Middle), Is.LessThanOrEqualTo(room.InsideRadius + 1), p.ToString());
                Assert.That(Flat(inside, room.Door), Is.GreaterThan(room.DoorRadius + 150), p.ToString());
            }
            Assert.That(EpicEncounterTactics.KeepInside(room, new Vector3(29600, 25300, 19490)), Is.EqualTo(new Vector3(29600, 25300, 19490)));

            // Funus's stand-off (1,400 off him, toward the door for a raider north of him) is pulled inside.
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var wizard = Npc<Bot>(29467, 25900, 19490);
            var standOff = EpicEncounterTactics.FunusAdvice(wizard, funus, (int)eCharacterClass.Wizard, eObjectType.Staff, false);
            Assert.That(Flat(standOff.StandAt.Value, room.Door), Is.LessThan(400), "the raw stand-off is at the door");
            ShipAs(nameof(TestFunus), "ApocInitializator", "AllowDisplacement");
            Assert.That(EpicEncounterTactics.BarrierRoomOf(funus), Is.SameAs(room), "a subclass of Funus is fought in it");
            Vector3 kept = EpicEncounterTactics.KeepInsideFor(funus, standOff.StandAt.Value);
            Assert.That(Flat(kept, room.Middle), Is.LessThanOrEqualTo(room.InsideRadius + 1));
            Assert.That(EpicEncounterTactics.BarrierRoomOf(Npc<Court>(29500, 25300, 19490)), Is.Null, "not a room fighter");
            // The owner's switch off: no barrier for bots, nothing is changed.
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "ApocInitializator", false);
            Assert.That(EpicEncounterTactics.BarrierRoomOf(funus), Is.Null);
            Assert.That(EpicEncounterTactics.KeepInsideFor(funus, standOff.StandAt.Value), Is.EqualTo(standOff.StandAt.Value));
        }

        [Test]
        public void TheRouteDoesNotLeaveTheRoomWhileAHorsemanFights()
        {
            var funus = Npc<TestFunus>(29467, 25235, 19490);
            var outside = Npc<Court>(29500, 32000, 19490);
            var live = new List<GameNPC> { funus, outside };
            Assert.That(EpicEncounterTactics.DungeonRoute(outside, live, out _), Is.Null, "no fight on: the route goes where it likes");
            funus.Fighting = true;
            Assert.That(EpicEncounterTactics.DungeonRoute(outside, live, out string reason), Is.SameAs(funus));
            Assert.That(reason, Does.Contain("barred"));
            Assert.That(EpicEncounterTactics.DungeonRoute(funus, live, out _), Is.Null, "the horseman himself is fine");
            EpicEncounterMechanics.SetFor("AllowDisplacement", "ApocInitializator", false);
            Assert.That(EpicEncounterTactics.DungeonRoute(outside, live, out _), Is.Null, "barrier off: no hold");
        }

        [Test]
        public void ABotTheBarrierMovesStaysInsideAndRepeatedBouncesAreCounted()
        {
            var plan = EncounterDisplacementRecovery.Plan.For(DisplacementKind.Barrier, 1_000, new Vector3(29469, 25244, 19490), 0, 0, true);
            var o = new EncounterDisplacementRecovery.Observation(1_000, true, new Vector3(29469, 25244, 19490), new Vector3(29459, 26401, 19503),
                false, false, false, null);
            Assert.That(EncounterDisplacementRecovery.Step(plan, o).Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Land));
            var done = EncounterDisplacementRecovery.Step(plan, o);
            Assert.That(done.Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Done), "no walk back to the door it was stopped at");
            Assert.That(done.Reason, Is.EqualTo("kept inside"));

            var bot = Npc<Bot>(29459, 26401, 19503);
            for (int i = 0; i < 3; i++)
                EncounterDisplacementRecovery.CountBounce(new DisplacementEvent(bot, null, "ApocInitializator", DisplacementKind.Barrier,
                    DisplacementRecovery.None, 60, new Vector3(29459, 26401, 19503), new Vector3(29469, 25244, 19490),
                    new Vector3(29469, 25244, 19490), 0, true, 10_000 + i * 20_000, true));
            Assert.That(EncounterDisplacementRecovery.BouncesOf(bot, "ApocInitializator", 60_000), Is.EqualTo(3));
            Assert.That(EncounterDisplacementRecovery.BouncesOf(bot, "ApocInitializator", 200_000), Is.Zero, "two minutes on");
        }

        // ---- Easmarach -----------------------------------------------------------------------------------------------

        [Test]
        public void TheFloorSpotsBelowTheWaterfallReachHimAndCountForHisCloseRange()
        {
            foreach (Vector3 stand in EpicEncounterTactics.EasmarachFallsStands)
            {
                Assert.That(Vector3.Distance(stand, EpicEncounterTactics.EasmarachFallsTop), Is.LessThan(1_250), "spell and bow range: " + stand);
                Assert.That(Vector3.Distance(stand, EpicEncounterTactics.EasmarachSpawn), Is.LessThan(2_500), "his tether of his spawn: " + stand);
            }
            Assert.That(EpicEncounterTactics.OnTheFalls(new Vector3(37823, 50345, 10930)), Is.True, "where the route skipped him");
            Assert.That(EpicEncounterTactics.OnTheFalls(EpicEncounterTactics.EasmarachSpawn), Is.False);
            Assert.That(EpicEncounterTactics.OnTheFalls(new Vector3(38285, 50456, 9860)), Is.False, "fought on the floor");
        }

        [Test]
        public void OnTheWaterfallTopRangedShootFromBelowAndMeleeWaitAtHisSpawn()
        {
            UseRegion(191, (0, 0));
            var easmarach = Npc<TestEasmarach>(37823, 50345, 10930);
            Vector3 top = At(easmarach), far = new(38500, 50900, 9865);
            var ranged = EpicEncounterTactics.EasmarachFallsAdvice(far, top, easmarach, 7, atRange: true);
            Assert.That(ranged?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition));
            Assert.That(ranged.Value.StandAt, Is.EqualTo(EpicEncounterTactics.EasmarachStand(7)));
            var there = EpicEncounterTactics.EasmarachFallsAdvice(EpicEncounterTactics.EasmarachStand(7), top, easmarach, 7, atRange: true);
            Assert.That(there?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.None), "fight from the spot");
            Assert.That(there.Value.Call, Is.EqualTo("epic_falls"));
            var melee = EpicEncounterTactics.EasmarachFallsAdvice(far, top, easmarach, 7, atRange: false);
            Assert.That(melee?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(melee.Value.StandAt, Is.EqualTo(EpicEncounterTactics.EasmarachSpawn));
            Assert.That(EpicEncounterTactics.EasmarachFallsAdvice(far, EpicEncounterTactics.EasmarachSpawn, easmarach, 7, true), Is.Null,
                "down on the floor: the fight as usual");
            // Different parties spread over the spots.
            Assert.That(Enumerable.Range(0, 6).Select(EpicEncounterTactics.EasmarachStand).Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void WithHisPushOnTheRouteKillsEasmarachBeforeTheLastRoom()
        {
            UseRegion(191, (0, 0), (0, 65536));
            var easmarach = Npc<TestEasmarach>(37918, 50523, 9865);
            var olcasgean = Npc<TestOlcasgean>(39237, 62644, 11685);
            var xaga = Npc<TestXaga>(27482, 54821, 12935);
            var live = new List<GameNPC> { easmarach, olcasgean, xaga };
            Assert.That(EpicEncounterTactics.InLastRoom(olcasgean), Is.True);
            Assert.That(EpicEncounterTactics.DungeonRoute(olcasgean, live, out _), Is.Null, "a test class has no push of its own: off");
            ShipAs(nameof(TestEasmarach), "Easmarach", "AllowDisplacement");
            Assert.That(EpicEncounterTactics.DungeonRoute(olcasgean, live, out string reason), Is.SameAs(easmarach));
            Assert.That(reason, Does.Contain("current"));
            Assert.That(EpicEncounterTactics.DungeonRoute(xaga, live, out _), Is.Null, "not past the falls");
            live.Remove(easmarach);
            Assert.That(EpicEncounterTactics.DungeonRoute(olcasgean, live, out _), Is.Null, "dead: the falls are open");
        }

        // ---- Torst and Hurika ----------------------------------------------------------------------------------------

        [Test]
        public void TheRaidPullsAFlyingGriffonDownAndWaitsUnderItsLoop()
        {
            UseRegion(160, (0, 0));
            var torst = Npc<TestTorst>(53201, 39956, 16314);
            var patrol = EpicEncounterTactics.PatrolOf(torst);
            Assert.That(patrol?.Boss, Is.EqualTo("Torst"));
            Vector3 griffon = new(53201, 39956, 17300);
            // In reach of a shooter: it shoots (the pull).
            var pull = EpicEncounterTactics.GriffonPatrolAdvice(new Vector3(53300, 39900, 16300), griffon, torst, true, patrol, 0, true);
            Assert.That(pull?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            Assert.That(pull.Value.Call, Is.EqualTo("epic_griffon_pull"));
            // Out of reach: to its party's point under the loop, keeping the griffon as its target.
            Vector3 far = new(50000, 36000, 16100);
            var wait = EpicEncounterTactics.GriffonPatrolAdvice(far, griffon, torst, true, patrol, 2, true);
            Assert.That(wait?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition));
            Assert.That(Flat(wait.Value.StandAt.Value, patrol.Loop[2]), Is.LessThan(1));
            var melee = EpicEncounterTactics.GriffonPatrolAdvice(far, griffon, torst, true, patrol, 2, false);
            Assert.That(melee?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff), "melee wait for it to land");
            Assert.That(EpicEncounterTactics.GriffonPatrolAdvice(far, griffon, torst, false, patrol, 2, true), Is.Null, "landed: fight as usual");
            Assert.That(EpicEncounterTactics.PatrolOf(Npc<Court>(1000, 1000, 100)), Is.Null);
            // Off as shipped: the rule has no say.
            string decided = EpicEncounterTactics.Decide(Npc<Bot>(53300, 39900, 16300), torst, new List<GameNPC> { torst }, 0, true, 1_000).Reason;
            Assert.That(decided ?? "", Does.Not.StartWith("griffon-pull"));
        }

        // ---- Legion --------------------------------------------------------------------------------------------------

        [Test]
        public void ALowRaiderStepsOutOfLegionsReachButHisTargetStays()
        {
            UseRegion(249, (0, 0));
            var legion = Npc<TestLegion>(20_000, 20_000, 1_000);
            Vector3 near = new(20_500, 20_000, 1_000);
            var low = EpicEncounterTactics.LegionWoundedAdvice(near, 20, false, legion);
            Assert.That(low?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(Flat(low.Value.StandAt.Value, At(legion)), Is.EqualTo(EpicEncounterTactics.LegionWoundedStandOff).Within(1));
            Assert.That(Flat(low.Value.StandAt.Value, At(legion)), Is.GreaterThan(1_400), "past his close range: he can't be hurt from there");
            Assert.That(EpicEncounterTactics.LegionWoundedAdvice(near, 20, true, legion), Is.Null, "his target holds him");
            Assert.That(EpicEncounterTactics.LegionWoundedAdvice(near, 80, false, legion), Is.Null);
            var waiting = EpicEncounterTactics.LegionWoundedAdvice(new Vector3(21_700, 20_000, 1_000), 20, false, legion);
            Assert.That(waiting?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(waiting.Value.StandAt, Is.Null, "already out: it waits there");
        }

        [Test]
        public void NoBotCastsABladeturnNearAFightingLegion()
        {
            UseRegion(249, (0, 0));
            var legion = Npc<TestLegion>(20_000, 20_000, 1_000);
            legion.Fighting = true;
            var near = Npc<Bot>(21_000, 20_000, 1_000);
            var far = Npc<Bot>(24_000, 20_000, 1_000);
            Assert.That(EpicEncounterTactics.BladeturnFeedsLegion(near, legion), Is.True);
            Assert.That(EpicEncounterTactics.BladeturnFeedsLegion(far, legion), Is.False);
            legion.Fighting = false;
            Assert.That(EpicEncounterTactics.BladeturnFeedsLegion(near, legion), Is.False, "out of combat: bubbles as usual");
            Assert.That(EpicEncounterTactics.NoBladeturnHere(Npc<Bot>(1_000, 1_000, 100)), Is.False, "not in Darkness Falls");
        }

        // ---- the Green Knight ----------------------------------------------------------------------------------------

        [Test]
        public void TheGreenKnightStartsByAWhisperOrForABotRaidThatGatheredByHim()
        {
            Assert.That(OFGreenKnight.RaidChallengeDue(8, 1_000, 21_000), Is.True);
            Assert.That(OFGreenKnight.RaidChallengeDue(7, 1_000, 60_000), Is.False, "fewer than a raid's worth");
            Assert.That(OFGreenKnight.RaidChallengeDue(30, 1_000, 15_000), Is.False, "not long enough by him");
            Assert.That(OFGreenKnight.RaidChallengeDue(30, 0, 60_000), Is.False);
            UseRegion(1, (589824, 393216));
            var knight = Npc<TestKnight>(592990, 418687, 5012);
            knight.Flags = GameNPC.eFlags.PEACE;
            var passer = Npc<Bot>(593100, 418687, 5012);
            Assert.That(OFGreenKnight.CanChallenge(passer), Is.False, "a bot of no raid expedition never starts him");
            Assert.That(knight.TryStartFight(passer, "raid"), Is.False);
            Assert.That(knight.Flags & GameNPC.eFlags.PEACE, Is.EqualTo(GameNPC.eFlags.PEACE));
            Assert.That(knight.TryStartFight(null, "whisper"), Is.False);
            // The whisper goes through the same start (a real player, as before).
            string script = Source("scripts", "namedmobs", "OFGreenKnight.cs");
            Assert.That(script, Does.Contain("TryStartFight(t, \"whisper\")"));
            Assert.That(script, Does.Contain("ConsiderRaidChallenge();"));
        }

        // ---- Shrouded Isles packs, Legendary Afanc --------------------------------------------------------------------

        [Test]
        public void QanrisDurosAndDurvalLinkTheirPacksWithin3500()
        {
            UseRegion(51, (393216, 393216));
            var qanris = Npc<TestQanris>(425043, 397100, 3056);
            var brain = Brain<QanrisDurosBrain>(qanris);
            var raider = Npc<Bot>(425100, 397100, 3056);
            brain.AddToAggroList(raider, 100);
            Court Guard(int x, string package)
            {
                Court g = Npc<Court>(x, 397100, 3056);
                g.PackageID = package;
                Brain<StandardMobBrain>(g);
                return g;
            }
            Court near = Guard(427000, "DurosBaf"), far = Guard(429500, "DurosBaf"), other = Guard(426000, "DurvalBaf");
            Assert.That(EncounterPacks.LinkInRadius(brain, "DurosBaf", 3500), Is.EqualTo(1));
            Assert.That(((StandardMobBrain)near.Brain).GetBaseAggroAmount(raider), Is.GreaterThan(0), "the guard within 3,500 joins");
            Assert.That(((StandardMobBrain)far.Brain).AggroCount, Is.Zero, "4,457 off: not his pack");
            Assert.That(((StandardMobBrain)other.Brain).AggroCount, Is.Zero, "another package");
            // The scripts call it (they passed 3,500 to GetNPCsFromRegion as a region id, so the link never fired).
            foreach (var (file, package) in new[] { ("QanrisDuros.cs", "DurosBaf"), ("Durval.cs", "DurvalBaf") })
            {
                string script = Source("scripts", "namedmobs", "Albion Named Mobs", "Shrouded Isles", file);
                Assert.That(script, Does.Contain($"EncounterPacks.LinkInRadius(this, \"{package}\", 3500)"), file);
                Assert.That(script, Does.Not.Contain("GetNPCsFromRegion(3500)"), file);
            }
        }

        [Test]
        public void LegendaryAfancThrowsBotsAsShippedAndTheyWalkBack()
        {
            UseRegion(1, (393216, 327680), (393216, 393216));
            var afanc = Npc<TestAfanc>(455704, 389644, 2192);
            var brain = Brain<LegendaryAfancBrain>(afanc);
            // A raider far off (it left, or never came) is never in the pick; one by him is. Objects are placed once: a moved
            // test NPC keeps its old cell in the zone's grid, so radius lookups would still see it there.
            var gone = Npc<Bot>(440_000, 389644, 2192);
            var here = Npc<Bot>(455900, 389644, 2192);
            brain.AddToAggroList(gone, 100);
            brain.AddToAggroList(here, 100);
            Call(brain, "ThrowPlayer", new object[] { null });
            Assert.That((gone.Moves, here.Moves), Is.EqualTo((0, 0)), "a test class has no switch of its own: bots stay");
            ShipAs(nameof(TestAfanc), "LegendaryAfanc");
            var moved = new List<DisplacementEvent>();
            void Record(DisplacementEvent e) => moved.Add(e);
            EncounterDisplacement.Displaced += Record;
            try
            {
                try { Call(brain, "ThrowPlayer", new object[] { null }); }
                catch (TargetInvocationException) { /* the fall damage after the move needs a world; the move comes first */ }
                Assert.That((gone.Moves, here.Moves), Is.EqualTo((0, 1)));
                DisplacementEvent e = moved.Single();
                Assert.That(e.To, Is.EqualTo(new Vector3(451486, 393503, 2754)), "the lake shore");
                Assert.That(e.Kind, Is.EqualTo(DisplacementKind.Teleport));
                Assert.That(e.Expected, Is.EqualTo(DisplacementRecovery.WalkBack));
                Assert.That(EncounterDisplacementRecovery.IsRecovering(here), Is.True);
            }
            finally { EncounterDisplacement.Displaced -= Record; }
            // His pick list is rebuilt on every throw (it was never cleared, so a raider who had left stayed in it).
            string script = Source("scripts", "namedmobs", "Albion Named Mobs", "Classic Zones", "LegendaryAfanc.cs");
            int clear = script.IndexOf("Port_Enemys.Clear();", StringComparison.Ordinal);
            Assert.That(clear, Is.GreaterThan(script.IndexOf("private int ThrowPlayer", StringComparison.Ordinal)));
            Assert.That(clear, Is.LessThan(script.IndexOf("EncounterTargets.InRadius(Body, 2500", StringComparison.Ordinal)));

            // The recovery: it walks back; with no path (or off the mesh) it is recalled after 5 s, never left there.
            var plan = EncounterDisplacementRecovery.Plan.For(DisplacementKind.Teleport, 0, new Vector3(451486, 393503, 2754), 0, 0, true);
            var landed = new Vector3(451486, 393503, 2754);
            var back = new Vector3(455704, 389644, 2192);
            var o = new EncounterDisplacementRecovery.Observation(0, true, landed, back, false, false, false, (_, _) => true);
            Assert.That(EncounterDisplacementRecovery.Step(plan, o).Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Land));
            Assert.That(EncounterDisplacementRecovery.Step(plan, o).Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Walk));
            var stuck = EncounterDisplacementRecovery.Plan.For(DisplacementKind.Teleport, 0, landed, 0, 0, true);
            var noPath = o with { Corridor = (_, _) => false };
            EncounterDisplacementRecovery.Step(stuck, noPath);
            Assert.That(EncounterDisplacementRecovery.Step(stuck, noPath).Kind, Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Hold));
            Assert.That(EncounterDisplacementRecovery.Step(stuck, noPath with { Now = 5_000 }).Kind,
                Is.EqualTo(EncounterDisplacementRecovery.OrderKind.Recall));
        }

        // ---- the party's calls --------------------------------------------------------------------------------------

        [Test]
        public void EveryWaveThreeCallHasLines()
        {
            string lines = Source("bots", "autonomous", "data", "chat", "combat_calls.lines");
            foreach (string topic in new[] { "epic_falls", "epic_griffon_pull", "epic_legion_wounded" })
                Assert.That(lines, Does.Contain("@topic " + topic + "\n").Or.Contain("@topic " + topic + "\r\n"), topic);
        }
    }
}
