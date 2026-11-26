using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using static DOL.GS.BotBuffTargetPolicy;

namespace DOL.UnitTests
{
    /// <summary>
    /// #491: buffer bots buff only targets a buff does something for, a specialist buffer leaves the base buffs to the
    /// group's healers, and the critical buffs come first.
    /// </summary>
    [TestFixture]
    public class UT_BotBuffTargets491
    {
        /// <summary>A bot with a class and nothing else (no constructor runs).</summary>
        private class ClassBot : GameBot
        {
            private ClassBot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public override ICharacterClass CharacterClass => TestClass;
        }

        private static ClassBot BotOf(ICharacterClass characterClass)
        {
            var bot = (ClassBot)RuntimeHelpers.GetUninitializedObject(typeof(ClassBot));
            bot.TestClass = characterClass;
            return bot;
        }

        private static GameNPC Pet() => (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));

        private static Spell SpellOf(eSpellType type) =>
            new(new Database.DbSpell { Type = type.ToString(), Target = "Realm", Value = 10 }, 20);

        [Test]
        public void MeleeTargetNeverGetsAcuityOrPowerRegen()
        {
            foreach (ICharacterClass melee in new ICharacterClass[] { new ClassArmsman(), new ClassMercenary() })
            {
                GameBot target = BotOf(melee);
                Assert.That(Benefits(target, SpellOf(eSpellType.PowerRegenBuff)), Is.False, melee.Name);
                Assert.That(Benefits(target, SpellOf(eSpellType.AcuityBuff)), Is.False, melee.Name);
                Assert.That(Benefits(target, SpellOf(eSpellType.StrengthConstitutionBuff)), Is.True, melee.Name);
                Assert.That(Benefits(target, SpellOf(eSpellType.EnduranceRegenBuff)), Is.True, melee.Name);
            }
        }

        [Test]
        public void CasterGetsAcuityAndPowerRegen()
        {
            GameBot wizard = BotOf(new ClassWizard());
            Assert.That(Benefits(wizard, SpellOf(eSpellType.PowerRegenBuff)), Is.True);
            Assert.That(Benefits(wizard, SpellOf(eSpellType.AcuityBuff)), Is.True);
        }

        [Test]
        public void HybridsWithPowerGetPowerRegenButNotAcuity()
        {
            // Acuity only raises a list caster's power stat (StatCalculator); a Paladin or Friar still has a pool.
            foreach (ICharacterClass hybrid in new ICharacterClass[] { new ClassPaladin(), new ClassFriar(), new ClassThane() })
            {
                GameBot target = BotOf(hybrid);
                Assert.That(Benefits(target, SpellOf(eSpellType.PowerRegenBuff)), Is.True, hybrid.Name);
                Assert.That(Benefits(target, SpellOf(eSpellType.AcuityBuff)), Is.False, hybrid.Name);
            }
            // Paladins use power, Armsmen don't.
            Assert.That(Benefits(BotOf(new ClassArmsman()), SpellOf(eSpellType.PowerRegenBuff)), Is.False);
        }

        [Test]
        public void VampiirPlayerHasPowerButABotHasNoPool()
        {
            Assert.That(UsesPower(eCharacterClass.Vampiir, eStat.UNDEFINED, realPlayer: true), Is.True);
            Assert.That(UsesPower(eCharacterClass.Vampiir, eStat.UNDEFINED, realPlayer: false), Is.False);
            Assert.That(Benefits(eSpellType.AcuityBuff, TargetKind.Character, eCharacterClass.Vampiir, eStat.UNDEFINED,
                eClassType.ListCaster, realPlayer: true), Is.False);
        }

        [Test]
        public void PetsNeverGetEnduranceOrPowerRegenButKeepUsefulBuffs()
        {
            GameNPC pet = Pet();
            Assert.That(KindOf(pet, out _), Is.EqualTo(TargetKind.Pet));
            Assert.That(Benefits(pet, SpellOf(eSpellType.EnduranceRegenBuff)), Is.False);
            Assert.That(Benefits(pet, SpellOf(eSpellType.PowerRegenBuff)), Is.False);
            Assert.That(Benefits(pet, SpellOf(eSpellType.AcuityBuff)), Is.False);
            foreach (eSpellType useful in new[] { eSpellType.StrengthBuff, eSpellType.StrengthConstitutionBuff,
                         eSpellType.BaseArmorFactorBuff, eSpellType.SpecArmorFactorBuff, eSpellType.HeatResistBuff,
                         eSpellType.CombatSpeedBuff })
                Assert.That(Benefits(pet, SpellOf(useful)), Is.True, useful.ToString());
            // The party pet list (BotGroupPetBuffTargets.Enumerate) uses the same rule.
            Assert.That(Benefits(eSpellType.EnduranceRegenBuff, TargetKind.Pet), Is.False);
            Assert.That(Benefits(eSpellType.PowerRegenBuff, TargetKind.Pet), Is.False);
        }

        // #567 A2 (port of True165 BotPetBuffBenefit): no fatigue buff on a pet (StyleProcessor charges style endurance only to
        // players and bots), and the caster's own pet follows the same rule as party pets.
        [Test]
        public void PetsGetNoFatigueBuffAndTheOwnPetFollowsTheSameRule()
        {
            GameNPC pet = Pet();
            Assert.That(Benefits(pet, SpellOf(eSpellType.FatigueConsumptionBuff)), Is.False);
            Assert.That(Benefits(BotOf(new ClassArmsman()), SpellOf(eSpellType.FatigueConsumptionBuff)), Is.True, "a character uses it");
            foreach (eSpellType useless in new[] { eSpellType.EnduranceRegenBuff, eSpellType.PowerRegenBuff, eSpellType.AcuityBuff,
                         eSpellType.FatigueConsumptionBuff })
                Assert.That(AutonomousPetSupport.ShouldMaintainRoutinePetBuff(SpellOf(useless), false), Is.False, useless.ToString());
            foreach (eSpellType useful in new[] { eSpellType.StrengthBuff, eSpellType.BaseArmorFactorBuff, eSpellType.CombatSpeedBuff })
                Assert.That(AutonomousPetSupport.ShouldMaintainRoutinePetBuff(SpellOf(useful), false), Is.True, useful.ToString());
        }

        [Test]
        public void SpecBufferLeavesBaseBuffsToAHealer()
        {
            var healer = new Buffer(HasSpecBuffs: false, CanCastBase: true);
            foreach (eSpellType baseBuff in new[] { eSpellType.StrengthBuff, eSpellType.ConstitutionBuff,
                         eSpellType.DexterityBuff, eSpellType.BaseArmorFactorBuff })
                Assert.That(LeavesBaseBuff(baseBuff, casterHasSpecBuffs: true, [healer]), Is.True, baseBuff.ToString());
            // The spec buffs stay the specialist's.
            foreach (eSpellType spec in new[] { eSpellType.StrengthConstitutionBuff, eSpellType.DexterityQuicknessBuff,
                         eSpellType.SpecArmorFactorBuff, eSpellType.AcuityBuff, eSpellType.EnduranceRegenBuff })
                Assert.That(LeavesBaseBuff(spec, casterHasSpecBuffs: true, [healer]), Is.False, spec.ToString());
        }

        [Test]
        public void SpecBufferStillCoversBaseBuffsWhenNoHealerCan()
        {
            Assert.That(LeavesBaseBuff(eSpellType.StrengthBuff, true, []), Is.False, "only buffer");
            Assert.That(LeavesBaseBuff(eSpellType.StrengthBuff, true, [new Buffer(false, false)]), Is.False,
                "the healer can't cast it (no spell, level or concentration)");
            Assert.That(LeavesBaseBuff(eSpellType.StrengthBuff, true, [new Buffer(true, true)]), Is.False,
                "two specialists: neither leaves it");
            Assert.That(LeavesBaseBuff(eSpellType.StrengthBuff, false, [new Buffer(false, true)]), Is.False,
                "a healer always casts its base buffs");
        }

        [Test]
        public void CriticalBuffsComeFirst()
        {
            Assert.That(Priority(eSpellType.EnduranceRegenBuff), Is.GreaterThan(Priority(eSpellType.StrengthConstitutionBuff)));
            Assert.That(Priority(eSpellType.StrengthConstitutionBuff), Is.GreaterThan(Priority(eSpellType.HealthRegenBuff)));
            Assert.That(Priority(eSpellType.CombatSpeedBuff), Is.GreaterThan(Priority(eSpellType.HeatResistBuff)));
            Assert.That(Priority(eSpellType.HeatResistBuff), Is.GreaterThan(Priority(eSpellType.StrengthBuff)));
            Assert.That(Priority(eSpellType.AcuityBuff), Is.GreaterThan(Priority(eSpellType.BaseArmorFactorBuff)));
        }

        [Test]
        public void SpecStatBuffsPreferTanksAndMeleeOverCasters()
        {
            Assert.That(PreferredSpecTarget(eSpellType.StrengthConstitutionBuff, TargetKind.Character, eClassType.PureTank), Is.True);
            Assert.That(PreferredSpecTarget(eSpellType.DexterityQuicknessBuff, TargetKind.Character, eClassType.Hybrid), Is.True);
            Assert.That(PreferredSpecTarget(eSpellType.StrengthConstitutionBuff, TargetKind.Character, eClassType.ListCaster), Is.False);
            Assert.That(PreferredSpecTarget(eSpellType.StrengthConstitutionBuff, TargetKind.Pet, eClassType.PureTank), Is.False);
        }
    }
}
