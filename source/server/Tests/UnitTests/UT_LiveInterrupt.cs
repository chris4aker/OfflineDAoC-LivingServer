using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// 1.2.3, the live-like interrupt (port of OpenDAoC-Core 21eda0f90; owner, 2026-11-24: no switch back). A hit starts an
/// interrupt timer; a cast is lost only if the timer still runs at its half-cast point. The same rule for a real player and
/// an autonomous gamebot, through the real SpellHandler cast path. Plus the bots' reading of it (BotInterruptPolicy) and the
/// smaller pure rules of the P2 port.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_LiveInterrupt
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameLiving> _actors = [];
    private GameServer _oldServer;
    private long _oldTime;
    private Region _region;
    private PetTestLanguageScope _language;
    private int _oldSelfInterrupt;

    private sealed class EmptyServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl =>
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
    }

    // Dexterity 60 and no casting speed bonus: a 3.0 s delve cast takes 3,000 ms, its half-cast point is 1,500 ms in.
    private static int Modified(eProperty property) => property switch
    {
        eProperty.Dexterity => 60,
        eProperty.CastingSpeed => 0,
        _ => 100
    };

    private sealed class CasterBot : GameBot
    {
        private CasterBot() : base((OfflineWorldBotRecord) null) { }
        public override byte Level { get; set; } = 50;
        public override bool IsAlive => true;
        public override bool IsCrowdControlled => false;
        public override int Health { get; set; } = 50;
        public override int Mana { get; set; } = 100;
        public override int Endurance { get; set; } = 100;
        public override int MaxHealth => 100;
        public override int MaxMana => 100;
        public override int MaxEndurance => 100;
        public override int GetModified(eProperty property) => Modified(property);
    }

    private sealed class CasterPlayer : GamePlayer
    {
        private CasterPlayer() : base(null, null) { }
        public override string Name { get; set; }
        public override GS.PacketHandler.IPacketLib Out =>
            DispatchProxy.Create<GS.PacketHandler.IPacketLib, UT_RealmExchangeEconomy.NoOpPacketLib>();
        public override byte Level { get; set; } = 50;
        public override bool IsAlive => true;
        public override bool IsCrowdControlled => false;
        public override int Health { get; set; } = 50;
        public override int Mana { get; set; } = 100;
        public override int Endurance { get; set; } = 100;
        public override int MaxHealth => 100;
        public override int MaxMana => 100;
        public override int MaxEndurance => 100;
        public override int GetModified(eProperty property) => Modified(property);
    }

    private sealed class Enemy : GameNPC
    {
        public override bool IsAlive => true;
        public override int Health { get; set; } = 100;
        public override int MaxHealth => 100;
        public override int GetModified(eProperty property) => 100;
    }

    [SetUp]
    public void SetUp()
    {
        _oldServer = GameServer.Instance;
        _oldTime = GameLoop.GameLoopTime;
        GameServer.LoadTestDouble((EmptyServer) RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
        _language = new PetTestLanguageScope();
        _region = new Region(new RegionData { Id = 201, Name = "Interrupt test", Description = "Interrupt test", Mobs = [] });
        SetTime(100_000);
        ScriptMgr.ClearSpellHandlerCache();
        // The server reads it from the database (default 2,200 ms); a test world has no database.
        _oldSelfInterrupt = GS.ServerProperties.Properties.SPELL_SELF_INTERRUPT_DURATION;
        GS.ServerProperties.Properties.SPELL_SELF_INTERRUPT_DURATION = 2_200;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameLiving actor in _actors)
        {
            ServiceObjectStore.Remove(actor.castingComponent);
            ServiceObjectStore.Remove(actor.effectListComponent);
        }

        _actors.Clear();
        _language.Dispose();
        GS.ServerProperties.Properties.SPELL_SELF_INTERRUPT_DURATION = _oldSelfInterrupt;
        GameServer.LoadTestDouble(_oldServer);
        SetTime(_oldTime);
    }

    private static Spell SelfHeal() => new(new DbSpell
    {
        SpellID = 59101, Name = "Test Mending", Type = "Heal", Target = "Self",
        Range = 0, CastTime = 3.0, Value = 20, Power = 5, Uninterruptible = false
    }, 1);

    [TestCase(false, TestName = "Player keeps the cast when the interrupt timer ran out by half-cast")]
    [TestCase(true, TestName = "Gamebot keeps the cast when the interrupt timer ran out by half-cast")]
    public void KeepsTheCastWhenTheTimerRanOutByHalfCast(bool gamebot)
    {
        (GameLiving caster, SpellHandler handler, Enemy enemy) = BeginCast(gamebot);

        // Hit 0.5 s before the cast began: the timer runs out at 101,000, before the half-cast point at 101,500.
        Hit(caster, enemy, interruptEnds: 101_000);
        SetTime(101_600);
        handler.Tick();

        Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting), "the cast goes on past its half-cast point");
        Assert.That(caster.IsBeingSelfInterrupted, Is.False);

        // A hit after the half-cast point only starts a timer: the cast still lands.
        Hit(caster, enemy, interruptEnds: 104_600);
        SetTime(101_700);
        handler.Tick();
        Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting), "a hit after half-cast doesn't cost the cast");
    }

    [TestCase(false, TestName = "Player loses the cast when the interrupt timer still runs at half-cast")]
    [TestCase(true, TestName = "Gamebot loses the cast when the interrupt timer still runs at half-cast")]
    public void LosesTheCastWhenTheTimerStillRunsAtHalfCast(bool gamebot)
    {
        (GameLiving caster, SpellHandler handler, Enemy enemy) = BeginCast(gamebot);

        // A hit just after the cast began: the 3 s timer runs to 103,100, past the half-cast point.
        SetTime(100_100);
        Hit(caster, enemy, interruptEnds: 103_100);
        handler.Tick();
        Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting), "the hit alone doesn't stop the cast at once");

        SetTime(101_600);
        handler.Tick();

        Assert.That(handler.CastState, Is.Not.EqualTo(eCastState.Casting), "the cast is lost at its half-cast point");
        Assert.That(handler.CastState, Is.Not.EqualTo(eCastState.Finished));
        Assert.That(caster.IsBeingSelfInterrupted, Is.True, "a lost cast starts the short self interrupt");
        Assert.That(caster.Health, Is.EqualTo(50), "the lost heal doesn't land");
    }

    [Test]
    public void ACastMayBeginWhileAnAttackersTimerRuns()
    {
        (GameLiving caster, Enemy enemy) = Casters(gamebot: false);
        Hit(caster, enemy, interruptEnds: 101_000);
        Assert.That(caster.InterruptRemainingDuration, Is.Zero, "only the self interrupt holds a cast back");
        Assert.That(caster.IsBeingInterruptedByOther, Is.True);
    }

    [TestCase(101_000, false)]
    [TestCase(101_500, false)]
    [TestCase(101_501, true)]
    [TestCase(99_000, false)]
    public void BotsReadTheHalfCastRule(long interruptEnds, bool lost) =>
        Assert.That(BotInterruptPolicy.LostAtHalfCast(100_000, interruptEnds, 3_000), Is.EqualTo(lost));

    [Test]
    public void ABotStartsACastTheTimerWontCost()
    {
        (GameLiving bot, Enemy enemy) = Casters(gamebot: true);
        Spell heal = SelfHeal();

        Hit(bot, enemy, interruptEnds: 101_000);
        Assert.That(BotInterruptPolicy.CastWouldBeLost(bot, heal), Is.False, "the timer runs out before the half-cast point");

        Hit(bot, enemy, interruptEnds: 103_000);
        Assert.That(BotInterruptPolicy.CastWouldBeLost(bot, heal), Is.True, "the timer still runs at the half-cast point");
        Assert.That(BotInterruptPolicy.CastWouldBeLost(bot, Instant()), Is.False, "instant spells are never lost");
    }

    [TestCase("Silence", false)]
    [TestCase("0", false)]
    [TestCase("Nearsight", true)]
    [TestCase("Mesmerize", true)]
    public void RemovalNamesWithoutAnEffectRemoveNothing(string name, bool known)
    {
        Assert.That(RemoveSpellEffectHandler.TryGetEffectType(name, out eEffect effect), Is.EqualTo(known));
        if (known)
            Assert.That(effect, Is.Not.EqualTo(eEffect.Bladeturn));
        if (name == "Mesmerize")
            Assert.That(effect, Is.EqualTo(eEffect.Mez));
    }

    [TestCase(false, 300, 1000, 0)]
    [TestCase(false, 500, 1000, 1)]
    [TestCase(true, 900, 1000, 0)]
    public void FireAndForgetTurretsBucketByDistance(bool player, double distance, int range, int bucket) =>
        Assert.That(TurretFNFBrain.DistanceBucket(player, distance, range), Is.EqualTo(bucket));

    private static Spell Instant() => new(new DbSpell
    {
        SpellID = 59102, Name = "Test Instant", Type = "Heal", Target = "Self", CastTime = 0, Value = 10, Power = 1
    }, 1);

    private (GameLiving, SpellHandler, Enemy) BeginCast(bool gamebot)
    {
        (GameLiving caster, Enemy enemy) = Casters(gamebot);
        SpellLine line = new("test", "test", "test", false);
        SpellHandler handler = (SpellHandler) ScriptMgr.CreateSpellHandler(caster, SelfHeal(), line);
        Assert.That(handler, Is.Not.Null);
        handler.Target = caster;
        handler.Tick();
        Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting), "the cast begins");
        Assert.That(handler.CalculateCastingTime(), Is.EqualTo(3_000));
        return (caster, handler, enemy);
    }

    private (GameLiving, Enemy) Casters(bool gamebot)
    {
        GameLiving caster = gamebot ? Actor<CasterBot>() : Actor<CasterPlayer>();
        caster.Name = "Castertest";
        caster.Health = 50; // Uninitialized objects skip the property initializers.
        Set(typeof(GameObject), caster, "<Realm>k__BackingField", eRealm.Albion);

        if (gamebot)
        {
            Set(typeof(GameBot), caster, "m_characterClass", new GS.PlayerClass.ClassCleric());
            Set(typeof(GameBot), caster, "<IsAutonomousWorldBot>k__BackingField", true);
        }
        else
        {
            Set(typeof(GamePlayer), caster, "m_characterClass", new GS.PlayerClass.ClassCleric());
            var client = (GameClient) RuntimeHelpers.GetUninitializedObject(typeof(GameClient));
            client.Account = new DbAccount { PrivLevel = 1, Language = "EN" };
            Set(typeof(GamePlayer), caster, "m_client", client);
            Set(typeof(GamePlayer), caster, "m_steed", new WeakReference(null));
        }

        Enemy enemy = Actor<Enemy>();
        enemy.Name = "test enemy";
        Set(typeof(GameObject), enemy, "<Realm>k__BackingField", eRealm.None);
        return (caster, enemy);
    }

    private static void Hit(GameLiving caster, GameLiving attacker, long interruptEnds)
    {
        Set(typeof(GameLiving), caster, "<InterruptTime>k__BackingField", interruptEnds);
        Set(typeof(GameLiving), caster, "<LastInterrupter>k__BackingField", attacker);
    }

    private T Actor<T>() where T : GameLiving
    {
        T actor = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        SetNew(typeof(GameObject), actor, "_objectInRadiusCachesLock");
        SetNew(typeof(GameObject), actor, "_objectsInRadiusCaches");
        foreach (string field in new[] { "_changeHealthLock", "_changeManaLock", "_changeEnduranceLock", "_abilitiesLock",
            "_interruptTimerLock", "_interruptCallbackLock" })
            Set(typeof(GameLiving), actor, field, new System.Threading.Lock());
        Set(typeof(GameLiving), actor, "_disabledSkillsLock", new System.Threading.Lock());
        Set(typeof(GameLiving), actor, "m_disabledSkills", new Dictionary<(int, Type), KeyValuePair<long, Skill>>());
        foreach (string property in new[] { "ItemBonus", "AbilityBonus", "BaseBuffBonusCategory", "SpecBuffBonusCategory", "OtherBonus", "DebuffCategory", "SpecDebuffCategory" })
            Set(typeof(GameLiving), actor, $"<{property}>k__BackingField", new PropertyIndexer());
        Set(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
        Set(typeof(GameLiving), actor, "m_effects", new GameEffectList(actor));
        Set(typeof(GameLiving), actor, "m_abilities", new Dictionary<string, Ability>());
        Set(typeof(GameLiving), actor, "<ActivePulseSpells>k__BackingField", new ConcurrentDictionary<eSpellType, Spell>());

        if (actor is GameNPC)
        {
            Set(typeof(GameNPC), actor, "m_brains", new ArrayList());
            Set(typeof(GameNPC), actor, "m_spells", new List<Spell>());
        }

        actor.CurrentRegion = _region;
        actor.ObjectState = GameObject.eObjectState.Active;
        actor.castingComponent = CastingComponent.Create(actor);
        if (actor is GameNPC npc)
            npc.castingComponent = (NpcCastingComponent) actor.castingComponent;
        actor.effectListComponent = EffectListComponent.Create(actor);
        actor.attackComponent = new AttackComponent(actor);
        actor.styleComponent = StyleComponent.Create(actor);

        if (actor is GameNPC movingNpc)
        {
            movingNpc.movementComponent = new NpcMovementComponent(movingNpc);
            actor.movementComponent = movingNpc.movementComponent;
            movingNpc.movementComponent.ForceUpdatePosition();
        }

        _actors.Add(actor);
        return actor;
    }

    private static void Set(Type type, object target, string field, object value) =>
        type.GetField(field, Hidden).SetValue(target, value);

    private static void SetNew(Type type, object target, string field)
    {
        FieldInfo info = type.GetField(field, Hidden);
        info.SetValue(target, Activator.CreateInstance(info.FieldType));
    }

    private static void SetTime(long value) =>
        typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);
}
