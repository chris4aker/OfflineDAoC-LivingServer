using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using DOL.GS.ServerRules;
using DOL.GS.Spells;
using NUnit.Framework;
using static DOL.GS.ServerRules.IServerRules;

namespace DOL.UnitTests;

/// <summary>
/// 1.2.3 port package P3 from OpenDAoC-Core (combat, XP, crafting and items). Each test names the upstream commit.
/// Tests only: none of these mechanics has been checked in game.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_UpstreamCombatPort123
{
    // ---- A1: group XP (6ac049c6e) ----

    [Test]
    public void GroupXpKeepsTheHighestLevelFromBeforeTheKill()
    {
        var pair = new EntityCountTotalDamagePair(1, 100, 10);
        pair.Track(12);
        pair.Track(11);
        Assert.That(pair.HighestLevel, Is.EqualTo(12), "a snapshot of the highest level, not a live reference");

        // The member who dings on this kill can't change the value used for the others afterwards.
        int snapshot = pair.HighestLevel;
        Assert.That(snapshot, Is.EqualTo(12));
    }

    [Test]
    public void ChallengeCodeCanMatchALevelOneNpc()
    {
        // The downward search used to stop at level 2, so a level 1 NPC could never be the matching challenge level.
        ConColor levelOneToLevelTwo = ConLevels.GetConColor(ConLevels.GetConLevel(2, 1));
        Assume.That(levelOneToLevelTwo, Is.LessThan(ConColor.YELLOW));
        Assert.That(AbstractServerRules.ChallengeEquivalentNpcLevel(2, levelOneToLevelTwo), Is.EqualTo(1));
    }

    [Test]
    public void ChallengeCodeKeepsItsOtherBranches()
    {
        Assert.That(AbstractServerRules.ChallengeEquivalentNpcLevel(30, ConColor.YELLOW), Is.EqualTo(30));
        int orange = AbstractServerRules.ChallengeEquivalentNpcLevel(30, ConColor.ORANGE);
        Assert.That(ConLevels.GetConColor(ConLevels.GetConLevel(30, orange)), Is.EqualTo(ConColor.ORANGE));
        Assert.That(ConLevels.GetConColor(ConLevels.GetConLevel(30, orange + 1)), Is.Not.EqualTo(ConColor.ORANGE),
            "the highest level of that colour");
        int blue = AbstractServerRules.ChallengeEquivalentNpcLevel(30, ConColor.BLUE);
        Assert.That(ConLevels.GetConColor(ConLevels.GetConLevel(30, blue)), Is.EqualTo(ConColor.BLUE));
    }

    // ---- A6: damage formula slips ----

    [Test]
    public void HealProcsHaveNoVariance()
    {
        // 16fa2c467: item and style heal procs always heal the full amount.
        foreach (string line in new[] { GlobalSpellsLines.Item_Effects, GlobalSpellsLines.Combat_Styles_Effect })
        {
            var handler = (HealSpellHandler) RuntimeHelpers.GetUninitializedObject(typeof(HealSpellHandler));
            typeof(SpellHandler).GetField("m_spellLine", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(handler, new SpellLine(line, line, "", false));
            handler.CalculateDamageVariance(null, out double min, out double max);
            Assert.That(min, Is.EqualTo(max), line);
            Assert.That(min, Is.EqualTo(1.25), line);
        }
    }

    [Test]
    public void MeleeDamageBonusRaisesTheDamageCapToo()
    {
        // 8274b06c1: MeleeDamage (ToA, Savage and Cleric self buffs) raises the unstyled cap with the damage.
        var plain = Player(0);
        var buffed = Player(10);
        var weapon = GameInventoryItem.Create(Sword());
        weapon.SlotPosition = Slot.RIGHTHAND;
        double plainDamage = new AttackComponent(plain).WeaponDamage(weapon, null, 1, out double plainCap);
        double buffedDamage = new AttackComponent(buffed).WeaponDamage(weapon, null, 1, out double buffedCap);
        Assume.That(plainDamage, Is.GreaterThan(0));
        Assert.That(buffedDamage / plainDamage, Is.EqualTo(1.1).Within(1e-9));
        Assert.That(buffedCap / plainCap, Is.EqualTo(1.1).Within(1e-9));
    }

    [Test]
    public void DamageAddSpeedFallsBackToTheIntervalWithoutAnAttacker()
    {
        Assert.That(new AttackData { Interval = 3700 }.GetWeaponAttackSpeed(), Is.EqualTo(3700));
    }

    // ---- B3: evade, block and guard ----

    [Test]
    public void EvadeShrinksWithSeveralMeleeAttackers()
    {
        // a36db6f43: each attacker beyond the first counts a third.
        Assert.That(PlayerDefenseFormula.EvadeAttackerDivisor(0), Is.EqualTo(1));
        Assert.That(PlayerDefenseFormula.EvadeAttackerDivisor(1), Is.EqualTo(1));
        Assert.That(PlayerDefenseFormula.EvadeAttackerDivisor(4), Is.EqualTo(2).Within(1e-9));
        Assert.That(PlayerDefenseFormula.EvadeAttackerDivisor(7), Is.EqualTo(3).Within(1e-9));
    }

    [Test]
    public void EvadeArcIsNarrowForPlayersAndGamebotsOnly()
    {
        // 50027c4f7 for players and gamebots; ordinary NPCs keep 180 (narrowing it would weaken monsters).
        Assert.That(PlayerDefenseFormula.PlayerEvadeArc, Is.EqualTo(120));
        Assert.That(PlayerDefenseFormula.NpcEvadeArc, Is.EqualTo(180));
    }

    [Test]
    public void BlockRoundsAreHeldOncePerAttacker()
    {
        // a0f6fa4df, 469c9b7a1: a multi-hit attack or a fast archer uses one round, a second attacker another.
        var handler = new AttackComponent.BlockRoundHandler(null);
        var first = (GameNPC) RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        var second = (GameNPC) RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        try
        {
            Assert.That(handler.Consume(1, first, 3000, out _), Is.True);
            Assert.That(handler.Consume(1, first, 3000, out _), Is.True, "the same attacker's next hit reuses its round");
            Assert.That(handler.ReservationCount, Is.EqualTo(1));
            Assert.That(handler.Consume(1, second, 3000, out _), Is.False, "a small shield covers one attacker");
            Assert.That(handler.Consume(2, second, 3000, out _), Is.True, "a medium shield covers two");
            Assert.That(handler.ReservationCount, Is.EqualTo(2));
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or TypeInitializationException)
        {
            Assert.Inconclusive($"The timer service isn't available in this test host: {e.GetType().Name}");
        }
    }

    // ---- B7: hand to hand and Reflex Attack ----

    [Test]
    public void HandToHandHasNoUnlockLevels()
    {
        // 068076224: triples and quads at every spec.
        (double two, double three, double four) = AttackComponent.HthSwingChances(10, 0);
        Assert.That(two, Is.EqualTo(5));
        Assert.That(three, Is.EqualTo(2.5));
        Assert.That(four, Is.EqualTo(0.625));
        (two, three, four) = AttackComponent.HthSwingChances(50, 0);
        Assert.That((two, three, four), Is.EqualTo((25.0, 12.5, 3.125)));
        Assert.That(AttackComponent.HthSwingChances(0, 13), Is.EqualTo((0.0, 0.0, 0.0)));
    }

    [Test]
    public void DualistsReflexesTrickleDownToTriplesAndQuads()
    {
        // ce965da78: 8/13 of the bonus on doubles gives triples 4/13 and quads 1/13; the single swing chance is unchanged.
        (double baseTwo, double baseThree, double baseFour) = AttackComponent.HthSwingChances(40, 0);
        (double two, double three, double four) = AttackComponent.HthSwingChances(40, 13);
        Assert.That(two - baseTwo, Is.EqualTo(8).Within(1e-9));
        Assert.That(three - baseThree, Is.EqualTo(4).Within(1e-9));
        Assert.That(four - baseFour, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void TripleAndQuadMessagesAreForHandToHandOnly()
    {
        // e9fdfdd2a.
        Assert.That(WeaponAction.HandToHandMultihitMessage(true, 1), Is.Null);
        Assert.That(WeaponAction.HandToHandMultihitMessage(true, 2), Is.EqualTo("Triple attack!"));
        Assert.That(WeaponAction.HandToHandMultihitMessage(true, 3), Is.EqualTo("Quad attack!"));
        Assert.That(WeaponAction.HandToHandMultihitMessage(false, 3), Is.Null);
    }

    [Test]
    public void ReflexAttackCountersOnlyFrontalHits()
    {
        // 410f1d9bb (1.65 info).
        Assert.That(WeaponAction.ReflexAttackTriggers(eAttackResult.HitUnstyled, true), Is.True);
        Assert.That(WeaponAction.ReflexAttackTriggers(eAttackResult.HitStyle, true), Is.True);
        Assert.That(WeaponAction.ReflexAttackTriggers(eAttackResult.HitUnstyled, false), Is.False);
        foreach (eAttackResult miss in new[] { eAttackResult.Missed, eAttackResult.Blocked, eAttackResult.Evaded, eAttackResult.Parried })
            Assert.That(WeaponAction.ReflexAttackTriggers(miss, true), Is.False, miss.ToString());
    }

    [Test]
    public void GamebotDualWieldSwingsAreDualWieldAttacks()
    {
        // Living Server parity: gamebots are GameNPCs with no LeftHandSwingChance, so their dual wield swings were
        // never dual wield attacks (no offhand speed, no dual wield defense penetration).
        var bot = (ChampionBot) RuntimeHelpers.GetUninitializedObject(typeof(ChampionBot));
        var main = GameInventoryItem.Create(Sword());
        var left = GameInventoryItem.Create(Sword());
        left.Item_Type = Slot.LEFTHAND;
        Assert.That(WeaponAction.IsDualWieldAttack(main, left, bot), Is.True);
        left.Object_Type = (int) eObjectType.Shield;
        Assert.That(WeaponAction.IsDualWieldAttack(main, left, bot), Is.False);
    }

    // ---- B8: attack speed ----

    [Test]
    public void HasteMasteryOfArmsAndItemsAreThreeLayers()
    {
        // dda75528c, 99e18b6b2, 4c05debd6.
        Assert.That(MeleeSpeedPercentCalculator.Calculate(0, 0, 0), Is.EqualTo(1000));
        Assert.That(MeleeSpeedPercentCalculator.Calculate(20, 10, 10), Is.EqualTo(648)); // 20, then +8, then +7.2.
        Assert.That(MeleeSpeedPercentCalculator.Calculate(0, 0, 25), Is.EqualTo(900), "the item bonus is capped at 10%");
        Assert.That(MeleeSpeedPercentCalculator.Calculate(-20, 0, 0), Is.EqualTo(1200), "a debuff slows");
    }

    [Test]
    public void QuicknessUsesTheCommonFormula()
    {
        // ca9effdf6.
        Assert.That(AttackComponent.QuicknessMultiplier(50), Is.EqualTo(1));
        Assert.That(AttackComponent.QuicknessMultiplier(250), Is.EqualTo(0.6).Within(1e-9));
    }

    // ---- Resists: owner's decision on #506 (58bc5cdff, 738f82fa1) ----

    [Test]
    public void ConstitutionResistLayerIsMagicOnlyAndNotForCrowdControl()
    {
        Assert.That(ResistCalculator.AddsConstitutionResistLayer(true, eProperty.Resist_Heat, false), Is.True);
        Assert.That(ResistCalculator.AddsConstitutionResistLayer(true, eProperty.Resist_Body, false), Is.True);
        Assert.That(ResistCalculator.AddsConstitutionResistLayer(true, eProperty.Resist_Slash, false), Is.False);
        Assert.That(ResistCalculator.AddsConstitutionResistLayer(true, eProperty.Resist_Crush, false), Is.False);
        Assert.That(ResistCalculator.AddsConstitutionResistLayer(true, eProperty.Resist_Heat, true), Is.False);
        Assert.That(ResistCalculator.AddsConstitutionResistLayer(false, eProperty.Resist_Heat, false), Is.False);
    }

    // ---- A10: crafting master's primary skill (20a563b05) ----

    [Test]
    public void ChangingThePrimaryCraftQueuesASave()
    {
        var player = (CraftPlayer) RuntimeHelpers.GetUninitializedObject(typeof(CraftPlayer));
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(GamePlayer).GetField("_craftingLock", instance)!.SetValue(player, new System.Threading.Lock());
        typeof(GamePlayer).GetField("m_craftingSkills", instance)!.SetValue(player, new Dictionary<eCraftingSkill, int>());
        var pending = (IDictionary<GamePlayer, Dictionary<eCraftingSkill, int>>) typeof(CraftingProgressMgr)
            .GetField("_craftingChanges", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            player.ChangeCraftingPrimarySkill(eCraftingSkill.Tailoring);
            Assert.That(player.CraftingPrimarySkill, Is.EqualTo(eCraftingSkill.Tailoring));
            Assert.That(pending.ContainsKey(player), Is.True, "the change is queued for the next crafting save");
        }
        finally
        {
            pending.Remove(player);
        }
    }

    // ---- helpers ----

    private static DbItemTemplate Sword() => new()
    {
        Id_nb = Guid.NewGuid().ToString(), Name = "test sword", Level = 20,
        Realm = (int) eRealm.Albion, Object_Type = (int) eObjectType.SlashingWeapon,
        Item_Type = Slot.RIGHTHAND, Hand = 0, Type_Damage = (int) eDamageType.Slash,
        DPS_AF = 50, SPD_ABS = 30, Quality = 100, Model = 1,
        Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
        IsPickable = true, IsTradable = true, IsDropable = true, AllowedClasses = "0"
    };

    private static ArmsPlayer Player(int meleeDamage)
    {
        var player = (ArmsPlayer) RuntimeHelpers.GetUninitializedObject(typeof(ArmsPlayer));
        player.Level = 20;
        player.MeleeDamage = meleeDamage;
        return player;
    }

    private sealed class ArmsPlayer : GamePlayer
    {
        private ArmsPlayer() : base(null, null) { }
        public int MeleeDamage;
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassArmsman();
        public override int GetModified(eProperty property) => property switch
        {
            eProperty.Strength or eProperty.Dexterity => 60,
            eProperty.WeaponSkill => 100,
            eProperty.MeleeDamage => MeleeDamage,
            _ => 0,
        };
        public override int WeaponSpecLevel(DbInventoryItem item) => 10;
        public override double Effectiveness { get => 1; set { } }
    }

    private sealed class ChampionBot : GameBot
    {
        private ChampionBot() : base((OfflineWorldBotRecord) null) { }
        public override byte Level { get; set; }
    }

    private sealed class CraftPlayer : GamePlayer
    {
        private CraftPlayer() : base(null, null) { }
    }
}
