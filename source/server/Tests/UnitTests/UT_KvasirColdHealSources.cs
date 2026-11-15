using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.Styles;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Icelord Kvasir's cold heal (1.2, PC log 1dc66b6, 2026-11-15: the Midgard raid held him at 81-99% for 18 minutes, and
    /// on screen "aahhhh thank you spirit champion for healing me !" five times in 30 s). The cast filter stopped only bots'
    /// cold spells; these tests cover the other ways a bot raid lands cold on him: sub spells, area spells, style procs, cold
    /// weapons and poisons, reactive armor procs, and pets (the Spiritmaster's spirit fighters proc a cold DD on their hits),
    /// with the rule off for each. Also the attempt's fallen count (raiders rezzed or sent back in still fell in it).
    /// Tests only: nothing here was seen on the owner's PC.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_KvasirColdHealSources
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 700;
        private EpicTestServerScope _server;
        private Func<int, Spell> _itemSpell;
        private readonly Dictionary<int, Spell> _spells = new();

        public class Boss : GameEpicBoss
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class Kvasir : Boss { }
        public sealed class Vagn : Boss { }
        public sealed class Pet : GameNPC
        {
            public override bool IsAlive => true;
        }
        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override ICharacterClass CharacterClass => new ClassSpiritmaster();
        }

        private const int ColdDdId = 9_101, HeatDdId = 9_102, ColdProcBuffId = 9_103;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 65, Name = "Kvasir test", Description = "Kvasir test", Mobs = [] });
            EpicEncounterMechanics.ResetPerEncounter();
            EpicEncounterMechanics.AllowKvasirColdHeal = true;
            _itemSpell = T.ItemSpell;
            _spells.Clear();
            _spells[ColdDdId] = Dd(eDamageType.Cold, ColdDdId);
            _spells[HeatDdId] = Dd(eDamageType.Heat, HeatDdId);
            _spells[ColdProcBuffId] = ProcBuff(ColdDdId, ColdProcBuffId);
            T.ItemSpell = id => _spells.TryGetValue(id, out Spell s) ? s : null;
        }

        [TearDown]
        public void TearDown()
        {
            T.ItemSpell = _itemSpell;
            EpicEncounterMechanics.ResetPerEncounter();
            EpicEncounterMechanics.AllowKvasirColdHeal = true;
            _server.Dispose();
        }

        private static Spell Dd(eDamageType type, int id = 0, int sub = 0) => new(new DbSpell
        {
            SpellID = id, Type = eSpellType.DirectDamage.ToString(), Target = "Enemy", Damage = 95, DamageType = (int)type,
            Range = 1500, SubSpellID = sub
        }, 50);

        /// <summary>The Spiritmaster spirit fighters' Pet-Cold-DD-Proc (65302): a self buff whose Value names the proc's DD.</summary>
        private static Spell ProcBuff(int procId, int id) => new(new DbSpell
        {
            SpellID = id, Type = eSpellType.OffensiveProc.ToString(), Target = "Self", DamageType = (int)eDamageType.Cold,
            Value = procId, Duration = 1200
        }, 50);

        private static DbInventoryItem Item(eObjectType type, eDamageType damage, int proc = 0, int poison = 0) =>
            new() { Template = new DbItemTemplate { Object_Type = (int)type, Type_Damage = (int)damage, ProcSpellID = proc }, PoisonSpellID = poison };

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new ArrayList());
            typeof(GameNPC).GetField("m_spells", Hidden)?.SetValue(npc, new List<Spell>());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)?.SetValue(npc, new PropertyCollection());
            typeof(GameLiving).GetField("_abilitiesLock", Hidden)?.SetValue(npc, new Lock());
            typeof(GameLiving).GetField("m_abilities", Hidden)?.SetValue(npc, new Dictionary<string, Ability>());
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private static Bot NewBot() => (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));

        [Test]
        public void AColdSubSpellIsForbiddenOnKvasirLikeAColdSpell()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            GameBot bot = NewBot();
            Assert.That(T.ForbidsSpell(bot, kvasir, Dd(eDamageType.Energy, sub: ColdDdId)), Is.True, "its sub spell is cold");
            Assert.That(T.ForbidsSpell(bot, kvasir, Dd(eDamageType.Energy, sub: HeatDdId)), Is.False);
            Assert.That(T.ForbidsSpell(bot, Npc<Vagn>(0, 0, 0), Dd(eDamageType.Energy, sub: ColdDdId)), Is.False, "only on Kvasir");
            EpicEncounterMechanics.AllowKvasirColdHeal = false;
            Assert.That(T.ForbidsSpell(bot, kvasir, Dd(eDamageType.Energy, sub: ColdDdId)), Is.False, "rule off");
        }

        [Test]
        public void NoBotUsesAStyleWhoseProcIsColdOnKvasir()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            GameBot bot = NewBot();
            // The berserker's Left Axe 50 Aurora Borealis: 95 Damage Cold DD (20503) at 100%.
            var aurora = new Style(new DbStyle { ID = 205, ClassId = (int)eCharacterClass.Berserker, Name = "Aurora Borealis" }, null);
            aurora.Procs.Add(new StyleProcInfo(Dd(eDamageType.Cold, 20_503), (int)eCharacterClass.Berserker, 100));
            var plain = new Style(new DbStyle { ID = 204, ClassId = (int)eCharacterClass.Berserker, Name = "plain" }, null);
            var heat = new Style(new DbStyle { ID = 203, ClassId = (int)eCharacterClass.Berserker, Name = "heat" }, null);
            heat.Procs.Add(new StyleProcInfo(Dd(eDamageType.Heat), 0, 100));
            Assert.That(T.ForbidsStyle(bot, kvasir, aurora), Is.True);
            Assert.That(T.ForbidsStyle(bot, kvasir, plain), Is.False);
            Assert.That(T.ForbidsStyle(bot, kvasir, heat), Is.False);
            Assert.That(T.ForbidsStyle(bot, Npc<Vagn>(0, 0, 0), aurora), Is.False, "only on Kvasir");
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            Assert.That(T.ForbidsStyle(player, kvasir, aurora), Is.False, "a real player uses what he likes");
            // Another class's proc on a shared style id does not go off for this bot's class (StyleProcessor).
            var other = new Style(new DbStyle { ID = 205, ClassId = (int)eCharacterClass.Shadowblade, Name = "Aurora Borealis" }, null);
            other.Procs.Add(new StyleProcInfo(Dd(eDamageType.Cold), (int)eCharacterClass.Berserker, 100));
            Assert.That(T.ForbidsStyle(bot, kvasir, other), Is.False);
            EpicEncounterMechanics.AllowKvasirColdHeal = false;
            Assert.That(T.ForbidsStyle(bot, kvasir, aurora), Is.False, "rule off");
        }

        [Test]
        public void AColdWeaponOrPoisonMustNotMeleeKvasirAndAShieldOrBowIsNoMelee()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            DbInventoryItem coldAxe = Item(eObjectType.Axe, eDamageType.Cold), sword = Item(eObjectType.Sword, eDamageType.Slash);
            DbInventoryItem shield = Item(eObjectType.Shield, eDamageType.Cold), bow = Item(eObjectType.CompositeBow, eDamageType.Cold);
            Assert.That(T.MeleeDamageOf(coldAxe), Is.EqualTo(eDamageType.Cold));
            Assert.That(T.MeleeDamageOf(shield), Is.EqualTo(eDamageType.Crush), "a shield hits crush (AttackDamageType)");
            Assert.That(T.MeleeDamageOf(bow), Is.Null);
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Standard, coldAxe, null), Is.True);
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Standard, sword, coldAxe), Is.True, "the off-hand axe swings too");
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Standard, sword, shield), Is.False);
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.TwoHanded, sword, coldAxe), Is.False, "the off hand is not out");
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Distance, coldAxe, null), Is.False);
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Standard, Item(eObjectType.Sword, eDamageType.Slash, poison: ColdDdId), null),
                Is.True, "a cold poison");
            // A weapon proc goes off for a GamePlayer only (GameLiving.CheckWeaponMagicalEffect): never asked for a bot.
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Standard, Item(eObjectType.Sword, eDamageType.Slash, proc: ColdDdId), null),
                Is.False);
            Assert.That(T.MeleeForbidden(Npc<Vagn>(0, 0, 0), eActiveWeaponSlot.Standard, coldAxe, null), Is.False, "only on Kvasir");
            EpicEncounterMechanics.AllowKvasirColdHeal = false;
            Assert.That(T.MeleeForbidden(kvasir, eActiveWeaponSlot.Standard, coldAxe, null), Is.False, "rule off");
        }

        [Test]
        public void ArmorWithAColdReactiveProcIsFoundAndTheBotFightsHisAddsOrHealsFromAfar()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            // Tuscarian Monitor Boots, Dragon-shadow Crown, Ice-Shadow Gloves: 95 DD Cold (reactive) on Tuscaran loot.
            var boots = Item(eObjectType.Chain, eDamageType.Natural, proc: ColdDdId);
            var helm = Item(eObjectType.Chain, eDamageType.Natural, proc: HeatDdId);
            Assert.That(T.ArmorProcForbidden(kvasir, [helm, boots, null]), Is.True);
            Assert.That(T.ArmorProcForbidden(kvasir, [helm]), Is.False);
            Assert.That(T.ArmorProcForbidden(Npc<Vagn>(0, 0, 0), [boots]), Is.False);

            var add = Npc<Vagn>(20600, 52101, 10924);
            Vector3 close = new(20400, 52101, 10924);
            var healer = T.KvasirColdAdvice(close, kvasir, false, true, healer: true, add).Value;
            Assert.That(healer.Action, Is.EqualTo(T.BotAction.Reposition), "a healer heals on from out of his reach");
            Assert.That(Vector2.Distance(new(healer.StandAt.Value.X, healer.StandAt.Value.Y), new(kvasir.X, kvasir.Y)),
                Is.GreaterThan(T.KvasirColdStandOff));
            Assert.That(T.KvasirColdAdvice(new(22000, 52101, 10924), kvasir, false, true, true, add), Is.Null, "already out");
            var melee = T.KvasirColdAdvice(close, kvasir, true, false, false, add).Value;
            Assert.That((melee.Action, melee.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)add)));
            var none = T.KvasirColdAdvice(close, kvasir, false, true, false, null).Value;
            Assert.That(none.Action, Is.EqualTo(T.BotAction.StandOff));
            Assert.That(none.StandAt, Is.Not.Null);
            Assert.That(T.KvasirColdAdvice(close, kvasir, true, false, healer: true, add).Value.Action, Is.EqualTo(T.BotAction.Retarget),
                "a healer swinging a cold weapon is a cold weapon carrier");
            Assert.That(T.KvasirColdAdvice(close, kvasir, false, false, false, add), Is.Null, "nothing cold: fight on");
        }

        [Test]
        public void ABotsSpiritChampionStaysOffKvasirWhileColdHealsHim()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            var owner = Npc<Bot>(20900, 52101, 10924);
            var champion = Npc<Pet>(20850, 52101, 10924);
            champion.MeleeDamageType = eDamageType.Crush;
            typeof(GameNPC).GetField("m_spells", Hidden)!.SetValue(champion, new List<Spell> { _spells[ColdProcBuffId] });
            var brain = new ControlledMobBrain(owner) { Body = champion };
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(champion, brain);
            Assert.That(T.PetDamageTypes(champion), Does.Contain(eDamageType.Cold), "its proc's DD");
            Assert.That(T.PetMustNotHit(champion, kvasir), Is.True);
            Assert.That(T.PetMustNotHit(champion, Npc<Vagn>(0, 0, 0)), Is.False, "only Kvasir");
            Assert.That(T.PetMustNotHit(owner, kvasir), Is.False, "a bot is no pet");
            typeof(ControlledMobBrain).GetField("m_orderAttackTarget", Hidden)!.SetValue(brain, kvasir);
            Assert.That(typeof(ControlledMobBrain).GetMethod("CheckAttackOrderTarget", Hidden)!.Invoke(brain, null), Is.Null,
                "an attack order on him is dropped");

            var bear = Npc<Pet>(20850, 52201, 10924);
            bear.MeleeDamageType = eDamageType.Crush;
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(bear, new ControlledMobBrain(owner) { Body = bear });
            Assert.That(T.PetMustNotHit(bear, kvasir), Is.False, "a pet with no cold fights him");

            EpicEncounterMechanics.AllowKvasirColdHeal = false;
            Assert.That(T.PetMustNotHit(champion, kvasir), Is.False, "rule off: pets fight him");
        }

        [Test]
        public void APetsDamageTypesComeFromItsHarmfulSpellsSubSpellsAndProcs()
        {
            var types = new HashSet<eDamageType>();
            T.AddSpellDamageTypes(_spells[ColdProcBuffId], types);
            Assert.That(types, Is.EquivalentTo(new[] { eDamageType.Cold }), "an offensive proc: the proc's DD, not the buff's own type");
            types.Clear();
            T.AddSpellDamageTypes(Dd(eDamageType.Energy, sub: ColdDdId), types);
            Assert.That(types, Is.EquivalentTo(new[] { eDamageType.Energy, eDamageType.Cold }));
            types.Clear();
            T.AddSpellDamageTypes(new Spell(new DbSpell { Type = eSpellType.StrengthBuff.ToString(), Target = "Self",
                DamageType = (int)eDamageType.Cold }, 50), types);
            Assert.That(types, Is.Empty, "a buff is no damage");
        }

        [Test]
        public void AnAttemptsFallenCountsTheRaidersRezzedOrSentBackInSince()
        {
            // Kvasir, 2026-11-15: 85 of the raid died in the attempt; when he reset, most were back in after the return waves.
            int[] engaged = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            var fellDuring = new HashSet<int> { 1, 2, 3, 4, 5 };
            Assert.That(RealmRaidDungeonRoute.AttemptFallen(engaged, fellDuring, p => p == 10), Is.EqualTo(6));
            Assert.That(RealmRaidDungeonRoute.IsWipe(10, 6, 100, 82), Is.True, "half fell: a wipe, though they stand again");
            Assert.That(RealmRaidDungeonRoute.AttemptFallen(engaged, new HashSet<int>(), p => p == 10), Is.EqualTo(1),
                "as before when nobody fell during it");
            Assert.That(RealmRaidDungeonRoute.AttemptFallen<int>(null, fellDuring, _ => true), Is.EqualTo(0));
        }

        [Test]
        public void TheHealLogSaysWhatDealtTheHit()
        {
            var bot = NewBot();
            var pet = Npc<Pet>(0, 0, 0);
            Assert.That(EpicEncounterRestores.KvasirHealVia(new AttackData { Attacker = bot, AttackType = AttackData.eAttackType.MeleeOneHand }),
                Is.EqualTo("melee"));
            Assert.That(EpicEncounterRestores.KvasirHealVia(new AttackData { Attacker = pet, AttackType = AttackData.eAttackType.MeleeOneHand }),
                Is.EqualTo("pet-melee"));
            Assert.That(EpicEncounterRestores.KvasirHealVia(new AttackData { Attacker = bot, AttackType = AttackData.eAttackType.Ranged }),
                Is.EqualTo("ranged"));
            Assert.That(EpicEncounterRestores.KvasirHealVia(null), Is.EqualTo("unknown"));
        }
    }
}
