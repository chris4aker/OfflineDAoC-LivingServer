using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #569 step 8: archer bots open with Critical Shot on a target that isn't
    /// fighting, running or mezzed, and a Hunter keeps its pet at heel for that opener (at most 7 s). Bots never got the
    /// Critical Shot ability (players get it in OnSkillTrained), so the level comes from the bow spec.
    /// </summary>
    [TestFixture]
    public class UT_CriticalShotOpener569
    {
        private const int Enough = RangeAttackComponent.CRITICAL_SHOT_ENDURANCE_COST + RangeAttackComponent.DEFAULT_ENDURANCE_COST;

        private static bool Opens(bool switchOn = true, bool allowed = true, int level = 5, bool drawNotStarted = true,
            bool typeIsNormal = true, bool inCombat = false, int speed = 0, bool mezzed = false, int endurance = 100) =>
            BotRangedCombat.OpensWithCriticalShot(switchOn, allowed, level, drawNotStarted, typeIsNormal, inCombat, speed,
                mezzed, endurance);

        [Test]
        public void AnArcherOpensWithCriticalShotOnAnIdleTarget()
        {
            Assert.That(Opens(), Is.True);
            Assert.That(Opens(speed: BotRangedCombat.CriticalShotMaxTargetSpeed), Is.True, "a walking target can be Critical Shot");
            Assert.That(Opens(endurance: Enough), Is.True, "the Critical Shot and one normal shot after it");
        }

        [Test]
        public void NoOpenerWhenThePlayerRuleOrTheBotSaysNo() => Assert.Multiple(() =>
        {
            Assert.That(Opens(switchOn: false), Is.False, "bot_critical_shot_opener off");
            Assert.That(Opens(allowed: false), Is.False, "a hired companion's Critical Shot row set off");
            Assert.That(Opens(level: 0), Is.False, "bow spec under 3: no Critical Shot");
            Assert.That(Opens(drawNotStarted: false), Is.False, "only at the start of a draw");
            Assert.That(Opens(typeIsNormal: false), Is.False, "another shot type is already set");
            Assert.That(Opens(inCombat: true), Is.False, "the target is fighting");
            Assert.That(Opens(speed: BotRangedCombat.CriticalShotMaxTargetSpeed + 1), Is.False, "the target is running");
            Assert.That(Opens(mezzed: true), Is.False, "the target is mezzed");
            Assert.That(Opens(endurance: Enough - 1), Is.False, "not enough endurance");
        });

        [Test]
        public void ThePetWaitsForTheOpenerOnAnIdleMonster() =>
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, false, 0), Is.True);

        [Test]
        public void ThePetGoesOnceTheOpenerIsNotPossibleOrTooSlow() => Assert.Multiple(() =>
        {
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(false, true, true, false, 0), Is.False, "no Critical Shot");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, false, true, false, 0), Is.False, "no bow in hand");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, false, false, 0), Is.False, "already fighting");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, true, 0), Is.False, "pet already on it");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, false,
                BotRangedCombat.CriticalShotPetHoldMilliseconds - 1), Is.True, "still inside the hold");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, false,
                BotRangedCombat.CriticalShotPetHoldMilliseconds), Is.False, "held long enough");
        });

        [Test]
        public void TheSwitchIsOnByDefault() => Assert.That(Properties.BOT_CRITICAL_SHOT_OPENER, Is.True);

        // ---- the Hunter's pet in SynchronizeIndependentPet (the wiring)

        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameLiving> _actors = new();
        private GameServer _previous;
        private long _oldTime;
        private bool _oldSwitch;
        private static readonly IObjectDatabase EmptyDatabase =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();

        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet) =>
                attacker.Realm != defender.Realm;
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override IServerRules ServerRulesImpl => new Rules();
        }

        private sealed class Hunter : GameBot
        {
            private Hunter() : base((OfflineWorldBotRecord)null) { }
            public eActiveWeaponSlot Slot;
            public int BowSpec;
            public int TestEndurance;
            public override eActiveWeaponSlot ActiveWeaponSlot => Slot;
            public override int Endurance { get => TestEndurance; set { } }
            public override int GetModifiedSpecLevel(string keyName) => keyName == Specs.CompositeBow ? BowSpec : 0;
            public override bool IsAlive => true;
            public override bool IsAttacking => true;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool InCombat => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Midgard; set { } }
            public override ICharacterClass CharacterClass => new ClassHunter();
            public override IControlledBrain ControlledBrain { get; set; }
        }

        private sealed class Monster : GameNPC
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override bool InCombat => Fighting;
            public override int X => 1500;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override GameObject TargetObject { get; set; }
        }

        private sealed class Pet : ControlledMobBrain
        {
            public int Attacks;
            public Pet(GameLiving owner) : base(owner) { }
            public override void Attack(GameObject target) { Attacks++; OrderedAttackTarget = target as GameLiving; }
            public override void Disengage() { OrderedAttackTarget = null; }
            public override void Follow(GameObject target) { }
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _oldTime = GameLoop.GameLoopTime;
            _oldSwitch = Properties.BOT_CRITICAL_SHOT_OPENER;
            Properties.BOT_CRITICAL_SHOT_OPENER = true;
            SetTime(100_000);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameLiving actor in _actors) ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            SetTime(_oldTime);
            Properties.BOT_CRITICAL_SHOT_OPENER = _oldSwitch;
            GameServer.LoadTestDouble(_previous);
        }

        [Test]
        public void TheHuntersPetStaysAtHeelForTheOpenerThenGoesIn()
        {
            var (hunter, pet, monster) = Scene();
            AutonomousPetSupport.SynchronizeIndependentPet(hunter, pet, monster);
            Assert.That(pet.Attacks, Is.Zero, "the pet waits while the Hunter draws its Critical Shot");
            SetTime(100_000 + BotRangedCombat.CriticalShotPetHoldMilliseconds);
            AutonomousPetSupport.SynchronizeIndependentPet(hunter, pet, monster);
            Assert.That(pet.Attacks, Is.EqualTo(1), "never longer than the hold");
            Assert.That(pet.OrderedAttackTarget, Is.SameAs(monster));
        }

        [Test]
        public void ThePetGoesAtOnceWhenTheMonsterIsAlreadyFighting()
        {
            var (hunter, pet, monster) = Scene();
            monster.Fighting = true;
            AutonomousPetSupport.SynchronizeIndependentPet(hunter, pet, monster);
            Assert.That(pet.Attacks, Is.EqualTo(1));
        }

        [Test]
        public void ThePetGoesAtOnceWithNoBowInHandNoCriticalShotOrTheSwitchOff()
        {
            var (hunter, pet, monster) = Scene();
            hunter.Slot = eActiveWeaponSlot.Standard;
            AutonomousPetSupport.SynchronizeIndependentPet(hunter, pet, monster);
            Assert.That(pet.Attacks, Is.EqualTo(1), "melee weapon in hand");

            (hunter, pet, monster) = Scene();
            hunter.BowSpec = 2;
            AutonomousPetSupport.SynchronizeIndependentPet(hunter, pet, monster);
            Assert.That(pet.Attacks, Is.EqualTo(1), "bow spec 2: no Critical Shot");

            (hunter, pet, monster) = Scene();
            Properties.BOT_CRITICAL_SHOT_OPENER = false;
            AutonomousPetSupport.SynchronizeIndependentPet(hunter, pet, monster);
            Assert.That(pet.Attacks, Is.EqualTo(1), "switch off: today's behaviour");
        }

        [Test]
        public void ABotsCriticalShotLevelComesFromItsBowSpec()
        {
            var (hunter, _, _) = Scene();
            Assert.That(BotRangedCombat.CriticalShotLevel(hunter), Is.EqualTo(9));
            hunter.BowSpec = 7;
            Assert.That(BotRangedCombat.CriticalShotLevel(hunter), Is.EqualTo(2));
            hunter.BowSpec = 2;
            Assert.That(BotRangedCombat.CriticalShotLevel(hunter), Is.Zero);
        }

        private (Hunter, Pet, Monster) Scene()
        {
            Hunter hunter = Actor<Hunter>();
            // GetUninitializedObject skips field initializers: set the test double's bow, spec and endurance here
            hunter.Slot = eActiveWeaponSlot.Distance;
            hunter.BowSpec = 30;
            hunter.TestEndurance = 100;
            Field(typeof(GameLiving), hunter, "_abilitiesLock", new System.Threading.Lock());
            Field(typeof(GameLiving), hunter, "m_abilities", new Dictionary<string, Ability>());
            Field(typeof(GameNPC), hunter, "m_ownBrain", new BotBrain { Body = hunter });
            Monster monster = Actor<Monster>();
            Monster petBody = Actor<Monster>();
            Pet pet = new(hunter) { Body = petBody };
            Field(typeof(GameNPC), petBody, "m_ownBrain", pet);
            hunter.ControlledBrain = pet;
            hunter.TargetObject = monster;
            return (hunter, pet, monster);
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            actor.ObjectState = GameObject.eObjectState.Active;
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }

        private static void SetTime(long value) =>
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);

        private static void Field(Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden).SetValue(owner, value);
    }
}
