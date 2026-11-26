using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// #644 fix: a boss mechanic's measure-only signal is written when its spell really begins (the SpellHandler past
/// CheckBeginCast), never at the script's request. A monster's CastSpell returns true once the request is queued, so a cast
/// asked for on every think (Govannon's demon DoT, Black Lady's DD, Myrddraxis's heads) wrote a signal on every think, its
/// recast delay included. Through the real SpellHandler cast path; tests only.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_MeasureOnRealCast644
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameLiving> _actors = [];
    private readonly List<EncounterSignal> _measured = [];
    private GameServer _oldServer;
    private long _oldTime;
    private Region _region;
    private PetTestLanguageScope _language;

    private sealed class EmptyServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl =>
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
    }

    private sealed class Boss : GameNPC
    {
        public override byte Level { get; set; } = 70;
        public override bool IsAlive => true;
        public override bool IsCrowdControlled => false;
        public override int Health { get; set; } = 50;
        public override int Mana { get; set; } = 100;
        public override int MaxHealth => 100;
        public override int MaxMana => 100;
        public override int GetModified(eProperty property) => property switch
        {
            eProperty.Dexterity => 60,
            eProperty.CastingSpeed => 0,
            _ => 100
        };
    }

    [SetUp]
    public void SetUp()
    {
        _oldServer = GameServer.Instance;
        _oldTime = GameLoop.GameLoopTime;
        GameServer.LoadTestDouble((EmptyServer)RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
        _language = new PetTestLanguageScope();
        _region = new Region(new RegionData { Id = 202, Name = "Measure test", Description = "Measure test", Mobs = [] });
        SetTime(100_000);
        ScriptMgr.ClearSpellHandlerCache();
        RaidBossMechanics.ResetForTests();
        _measured.Clear();
        EncounterSignals.Measured += OnMeasured;
    }

    [TearDown]
    public void TearDown()
    {
        EncounterSignals.Measured -= OnMeasured;
        RaidBossMechanics.ResetForTests();
        foreach (GameLiving actor in _actors)
        {
            ServiceObjectStore.Remove(actor.castingComponent);
            ServiceObjectStore.Remove(actor.effectListComponent);
        }
        _actors.Clear();
        _language.Dispose();
        GameServer.LoadTestDouble(_oldServer);
        SetTime(_oldTime);
    }

    private void OnMeasured(EncounterSignal s) => _measured.Add(s);

    private static Spell SelfHeal(double castSeconds) => new(new DbSpell
    {
        SpellID = 59201, Name = "Test Boss Mending", Type = "Heal", Target = "Self",
        Range = 0, CastTime = castSeconds, Value = 20, Power = 0, RecastDelay = 30, Uninterruptible = true
    }, 1);

    private static readonly SpellLine Line = new("test", "test", "test", false);

    /// <summary>The script's own request, then the spell handler's first tick (where a real cast begins or is refused).</summary>
    private SpellHandler RequestAndTick(Boss boss, Spell spell, string mechanic)
    {
        RaidBossMechanics.Cast(true, boss, mechanic, spell);
        var handler = (SpellHandler)ScriptMgr.CreateSpellHandler(boss, spell, Line);
        Assert.That(handler, Is.Not.Null);
        handler.Target = boss;
        handler.Tick();
        return handler;
    }

    [Test]
    public void ASpellOnItsRecastDelayWritesNoSignal()
    {
        Boss boss = Actor();
        Spell dot = SelfHeal(3.0);
        boss.DisableSkill(dot, 30_000);
        SpellHandler refused = RequestAndTick(boss, dot, "dot");
        Assert.Multiple(() =>
        {
            Assert.That(refused.CastState, Is.Not.EqualTo(eCastState.Casting), "the recast delay refuses the cast");
            Assert.That(_measured, Is.Empty, "the request was queued, the spell never began: no signal");
        });
        // Asked again on every think while it waits: still nothing.
        for (int think = 0; think < 5; think++)
        {
            SetTime(100_000 + (think + 1) * 1_500);
            RequestAndTick(boss, dot, "dot");
        }
        Assert.That(_measured, Is.Empty, "five refused requests, no signal");
    }

    [Test]
    public void ARealCastWritesOneSignalWhenItBegins()
    {
        Boss boss = Actor();
        Spell dot = SelfHeal(3.0);
        SpellHandler cast = RequestAndTick(boss, dot, "dot");
        Assert.That(cast.CastState, Is.EqualTo(eCastState.Casting), "the cast begins");
        Assert.That(_measured, Has.Count.EqualTo(1));
        EncounterSignal s = _measured[0];
        Assert.Multiple(() =>
        {
            Assert.That(s.Boss, Is.SameAs(boss));
            Assert.That(s.Mechanic, Is.EqualTo("dot"));
            Assert.That(s.At, Is.EqualTo(100_000));
            Assert.That(s.Lands - s.At, Is.EqualTo(cast.CalculateCastingTime()), "it lands when the cast, as the handler timed it, ends");
            Assert.That(EncounterSignals.Active(boss, 100_000), Is.Empty, "still measure-only");
        });
        // A second handler for the same request (a retry, the next tick) writes nothing: the request was used.
        var again = (SpellHandler)ScriptMgr.CreateSpellHandler(boss, dot, Line);
        again.Target = boss;
        again.Tick();
        Assert.That(_measured, Has.Count.EqualTo(1), "one request, one signal");
    }

    [Test]
    public void AnInstantSpellSignalsAsItGoesOff()
    {
        Boss boss = Actor();
        SpellHandler instant = RequestAndTick(boss, SelfHeal(0), "heal");
        Assert.That(instant.CastState, Is.Not.EqualTo(eCastState.Precast));
        Assert.That(_measured, Has.Count.EqualTo(1));
        Assert.That(_measured[0].Lands - _measured[0].At, Is.EqualTo(0));
    }

    [Test]
    public void ASpellNoScriptAskedForWritesNoSignal()
    {
        Boss boss = Actor();
        Spell dot = SelfHeal(3.0);
        var handler = (SpellHandler)ScriptMgr.CreateSpellHandler(boss, dot, Line);
        handler.Target = boss;
        handler.Tick();
        Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting));
        Assert.That(_measured, Is.Empty, "the base brain's own casts are no mechanic");
        RaidBossMechanics.Cast(false, boss, "dot", dot);
        RaidBossMechanics.CastBegan(boss, dot, boss, 3_000);
        Assert.That(_measured, Is.Empty, "a request the boss refused at once is no mechanic");
    }

    [Test]
    public void ARequestThatNeverBeganExpires()
    {
        Boss boss = Actor();
        Spell dot = SelfHeal(3.0);
        RaidBossMechanics.Cast(true, boss, "dot", dot);
        SetTime(100_000 + RaidBossMechanics.PendingMilliseconds + 1);
        RaidBossMechanics.CastBegan(boss, dot, boss, 3_000);
        Assert.That(_measured, Is.Empty);
    }

    private Boss Actor()
    {
        Boss actor = (Boss)RuntimeHelpers.GetUninitializedObject(typeof(Boss));
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
        Set(typeof(GameNPC), actor, "m_brains", new ArrayList());
        Set(typeof(GameNPC), actor, "m_spells", new List<Spell>());
        actor.Name = "Test Summoner";
        actor.Health = 50;
        Set(typeof(GameObject), actor, "<Realm>k__BackingField", eRealm.None);
        actor.CurrentRegion = _region;
        actor.ObjectState = GameObject.eObjectState.Active;
        GameLiving living = actor;
        living.castingComponent = CastingComponent.Create(actor);
        actor.castingComponent = (NpcCastingComponent)living.castingComponent;
        living.effectListComponent = EffectListComponent.Create(actor);
        living.attackComponent = new AttackComponent(actor);
        living.styleComponent = StyleComponent.Create(actor);
        actor.movementComponent = new NpcMovementComponent(actor);
        living.movementComponent = actor.movementComponent;
        actor.movementComponent.ForceUpdatePosition();
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
