using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionSpellOptions165;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-11: AI Options that choose which attack spells a hired companion casts - a healer's fourth Role
    /// "Melee and heal, no attack spells", a caster's "Save power: Strict" and "Attack spells". Automatic (0) never blocks.
    /// </summary>
    [TestFixture]
    public class UT_CompanionSpellOptions165
    {
        private static Spell Make(string type, int damage = 0, string target = "Enemy", int range = 1500, int radius = 0,
            double value = 0) =>
            new(new DbSpell { Type = type, Target = target, Damage = damage, Range = range, Radius = radius, Value = value,
                Duration = 30 }, 1);

        private static readonly Spell Nuke = Make("DirectDamage", 50);
        private static readonly Spell Bolt = Make("Bolt", 60);
        private static readonly Spell Dot = Make("DamageOverTime", 10);
        private static readonly Spell Disease = Make("Disease");
        private static readonly Spell StrDebuff = Make("StrengthDebuff", value: 20);
        private static readonly Spell SnareNuke = Make("DamageSpeedDecrease", 40, value: 35);
        private static readonly Spell PointBlank = Make("DirectDamage", 40, range: 0, radius: 350);
        private static readonly Spell AreaNuke = Make("DirectDamage", 40, radius: 350);
        private static readonly Spell Root = Make("SpeedDecrease", value: 99);
        private static readonly Spell Mez = Make("Mesmerize");
        private static readonly Spell Heal = Make("Heal", target: "Realm", value: 100);
        private static readonly Spell StrBuff = Make("StrengthBuff", target: "Realm", value: 20);
        private static readonly Spell Bladeturn = Make("Bladeturn", target: "Self", range: 0);

        [Test]
        public void EveryRoleRowHasTheMeleeAndHealChoice() => Assert.Multiple(() =>
        {
            // Living Server offers Automatic and "Melee and heal", stored as True 1.65's 3
            foreach (byte kind in new[] { CompanionOptions165.KindHealerRole, CompanionOptions165.KindShamanRole,
                         CompanionOptions165.KindFriarRole })
            {
                var option = CompanionOptions165.Option(kind);
                Assert.That(option, Is.Not.Null, $"kind {kind}");
                Assert.That(option.Value.Key, Is.EqualTo("role"));
                Assert.That(option.Value.Choices, Has.Length.EqualTo(2), $"kind {kind}");
                Assert.That(option.Value.Choices[1], Is.EqualTo(CompanionOptions165.MeleeHealChoice));
                Assert.That(CompanionOptions165.StoredValue(kind, 1), Is.EqualTo(RoleMeleeHeal));
            }
            Assert.That(CompanionOptions165.MeleeHealChoice.Length, Is.LessThan(60), "the client cuts names at 60");
        });

        [Test]
        public void MeleeAndHealIsForTheFiveHealersOnly() => Assert.Multiple(() =>
        {
            foreach (eCharacterClass healer in new[] { eCharacterClass.Shaman, eCharacterClass.Cleric, eCharacterClass.Druid,
                         eCharacterClass.Healer, eCharacterClass.Friar })
            {
                Assert.That(MeleeHealRole(healer, RoleMeleeHeal), Is.True, healer.ToString());
                for (int choice = 0; choice < RoleMeleeHeal; choice++)
                    Assert.That(MeleeHealRole(healer, choice), Is.False, $"{healer} role {choice}: as before");
            }
            Assert.That(MeleeHealRole(eCharacterClass.Wizard, RoleMeleeHeal), Is.False);
        });

        [Test]
        public void MeleeAndHealBlocksEveryAttackSpellButKeepsControlHealsAndBuffs() => Assert.Multiple(() =>
        {
            foreach (Spell blocked in new[] { Nuke, Bolt, Dot, Disease, StrDebuff, SnareNuke, PointBlank, AreaNuke })
                Assert.That(MeleeHealBlocks(blocked), Is.True, blocked.SpellType.ToString());
            foreach (Spell kept in new[] { Root, Mez, Heal, StrBuff, Bladeturn })
                Assert.That(MeleeHealBlocks(kept), Is.False, kept.SpellType.ToString());
            Assert.That(MeleeHealBlocks(null), Is.False);
        });

        [Test]
        public void AttackSpellsAreaOnlyAndNone() => Assert.Multiple(() =>
        {
            foreach (Spell spell in new[] { Nuke, Bolt, PointBlank, AreaNuke, StrDebuff, Mez, Bladeturn })
                Assert.That(AttackChoiceBlocks(0, spell), Is.False, $"Automatic: {spell.SpellType}");

            Assert.That(AttackChoiceBlocks(AttackAreaOnly, Nuke), Is.True, "area only: no single-target nuke");
            Assert.That(AttackChoiceBlocks(AttackAreaOnly, Bolt), Is.True, "area only: no bolt");
            Assert.That(AttackChoiceBlocks(AttackAreaOnly, SnareNuke), Is.True);
            Assert.That(AttackChoiceBlocks(AttackAreaOnly, PointBlank), Is.False, "point blank damage stays");
            Assert.That(AttackChoiceBlocks(AttackAreaOnly, AreaNuke), Is.False, "targeted area damage stays");
            Assert.That(AttackChoiceBlocks(AttackAreaOnly, StrDebuff), Is.False, "debuffs stay");
            Assert.That(AttackChoiceBlocks(AttackAreaOnly, Mez), Is.False, "crowd control stays");

            foreach (Spell spell in new[] { Nuke, Bolt, Dot, PointBlank, AreaNuke, StrDebuff, Disease })
                Assert.That(AttackChoiceBlocks(AttackNone, spell), Is.True, $"none: {spell.SpellType}");
            foreach (Spell spell in new[] { Root, Mez, StrBuff, Bladeturn })
                Assert.That(AttackChoiceBlocks(AttackNone, spell), Is.False, $"none keeps {spell.SpellType}");
        });

        [Test]
        public void StrictSavesPowerWhoeverHoldsTheMonsterAndStopsAreaDamage() => Assert.Multiple(() =>
        {
            // Automatic needs a tank holding the monster; Strict does not
            Assert.That(BotCasterPriority.SavePowerInGroup(40, 80, false), Is.False, "Automatic: nobody holds it, nuke");
            Assert.That(StrictSavesPower(SavePowerStrict, 40, 80), Is.True, "Strict: under half power whoever holds it");
            Assert.That(StrictSavesPower(SavePowerStrict, BotCasterPriority.SavePowerBelowPercent, 80), Is.False,
                "half power or more: nuke");
            Assert.That(StrictSavesPower(SavePowerStrict, 40, BotCasterPriority.FinishTargetBelowPercent - 1), Is.False,
                "a nearly dead monster is finished");
            Assert.That(StrictSavesPower(0, 40, 80), Is.False, "Automatic is not Strict");
            Assert.That(StrictSavesPower(1, 40, 80), Is.False, "Off is not Strict");

            Assert.That(StrictBlocks(PointBlank, true), Is.True, "point blank damage waits too");
            Assert.That(StrictBlocks(AreaNuke, true), Is.True, "area damage waits too");
            Assert.That(StrictBlocks(Nuke, true), Is.True);
            Assert.That(StrictBlocks(Mez, true), Is.False, "crowd control is no damage spell");
            Assert.That(StrictBlocks(StrDebuff, true), Is.False);
            Assert.That(StrictBlocks(Bladeturn, true), Is.False);
            Assert.That(StrictBlocks(PointBlank, false), Is.False, "above half power: everything goes");
        });

        [Test]
        public void TheCasterRowsAreForTheCastersOnly() => Assert.Multiple(() =>
        {
            var save = CompanionOptions165.Option(CompanionOptions165.KindSavePowerCaster);
            Assert.That(save, Is.Not.Null);
            Assert.That(save.Value.Key, Is.EqualTo("savepower"), "the same stored key as everyone's Save power row");
            // Living Server: Automatic and Strict (stored as True 1.65's 2)
            Assert.That(save.Value.Choices, Has.Length.EqualTo(2));
            Assert.That(save.Value.Choices[1], Does.StartWith("Strict"));
            Assert.That(CompanionOptions165.StoredValue(CompanionOptions165.KindSavePowerCaster, 1), Is.EqualTo(SavePowerStrict));

            var attack = CompanionOptions165.Option(CompanionOptions165.KindAttackSpells);
            Assert.That(attack, Is.Not.Null);
            Assert.That(attack.Value.Key, Is.EqualTo("attackspells"));
            Assert.That(attack.Value.Choices, Has.Length.EqualTo(3));
            Assert.That(attack.Value.Choices[0], Is.EqualTo("Automatic"));
            Assert.That(CompanionOptions165.Caption(CompanionOptions165.KindAttackSpells), Is.EqualTo("Attack spells"));
            foreach (string name in save.Value.Choices.Concat(attack.Value.Choices))
                Assert.That(name.Length, Is.LessThan(60), name);

            foreach (eCharacterClass caster in new[] { eCharacterClass.Runemaster, eCharacterClass.Wizard,
                         eCharacterClass.Sorcerer, eCharacterClass.Eldritch, eCharacterClass.Spiritmaster,
                         eCharacterClass.Enchanter, eCharacterClass.Theurgist, eCharacterClass.Cabalist,
                         eCharacterClass.Animist, eCharacterClass.Bonedancer, eCharacterClass.Mentalist })
                Assert.That(IsCasterClass(caster), Is.True, caster.ToString());
            foreach (eCharacterClass other in new[] { eCharacterClass.Shaman, eCharacterClass.Thane,
                         eCharacterClass.Necromancer, eCharacterClass.Cleric })
                Assert.That(IsCasterClass(other), Is.False, other.ToString());
        });

        [Test]
        public void ACompanionWithNoHireRecordReadsAutomatic() => Assert.Multiple(() =>
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            foreach (string key in new[] { "role", "savepower", "attackspells" })
            {
                Assert.That(CompanionOptions165.Choice(bot, key), Is.EqualTo(0), key);
                Assert.That(CompanionOptions165.Choice(null, key), Is.EqualTo(0), key);
            }
            Assert.That(MeleeHealRole(bot), Is.False, "no hire record: no Role choice");
        });
    }
}
