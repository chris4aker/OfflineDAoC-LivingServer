using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #568 C6 (ported from True165 66f782d8d, UT_VeteranPlaybook; its veteran review 2026-11-11, M1): the veteran playbook - a fixed attack order per bot (built once per spell list) in place
    /// of the random final pick, and resist debuffs only for the caster's own damage. Order only: no spell, damage, cost or
    /// range number is touched.
    /// </summary>
    [TestFixture]
    public class UT_VeteranPlaybook568
    {
        private static int _nextId = 76_800;

        private static Spell S(string type, double damage = 0, eDamageType damageType = eDamageType.Heat, int level = 40,
            int power = 10, int radius = 0, double castTime = 2.5, int range = 1500, string target = "Enemy") =>
            new(new DbSpell
            {
                SpellID = _nextId++, Name = type + level, Type = type, Damage = damage, DamageType = (int)damageType,
                Power = power, Radius = radius, CastTime = castTime, Range = range, Target = target,
            }, level);

        private static int Cost(Spell spell) => spell.Power;

        private static PlaybookContext Fight(int health = 100, int power = 100, int targetHealth = 100, bool lasts = true,
            int casts = 1, bool petHit = false, bool snare = false) => new()
        {
            HealthPercent = health, PowerPercent = power, TargetHealthPercent = targetHealth, FightLasts = lasts,
            CastsOnTarget = casts, PetUnderAttack = petHit, SnareWanted = snare,
        };

        private static Spell Pick(BotPlaybook playbook, IReadOnlyList<Spell> candidates, PlaybookContext context,
            Func<Spell, bool> passOver = null) =>
            BotPlaybook.ChooseAttack(candidates, context, playbook.KindOfFunc, Cost, playbook.PrefersDrain,
                playbook.BoltOpenerOnly, passOver);

        [Test]
        public void SpellsAreClassifiedOncePerList()
        {
            Spell heatNuke = S("DirectDamage", 100, eDamageType.Heat);
            Spell heatDebuff = S("HeatResistDebuff");
            Spell bodyDebuff = S("BodyResistDebuff");
            Spell slashDebuff = S("SlashResistDebuff");
            Spell strDebuff = S("StrengthDebuff");
            Spell dot = S("DamageOverTime", 30, eDamageType.Matter);
            Spell drain = S("Lifedrain", 80, eDamageType.Body);
            Spell areaDrain = S("Lifedrain", 60, eDamageType.Body, radius: 350);
            Spell snareNuke = S("DamageSpeedDecrease", 70, eDamageType.Cold);
            Spell area = S("DirectDamage", 70, radius: 350);
            Spell bolt = S("Bolt", 150);
            Spell snare = S("SpeedDecrease");
            Spell elemental = S("SummonTheurgistPet");
            var playbook = BotPlaybook.Build(new[] { heatNuke, heatDebuff, bodyDebuff, slashDebuff, strDebuff, dot, drain,
                areaDrain, snareNuke, area, bolt, snare, elemental }, 50, eCharacterClass.Wizard);
            Assert.Multiple(() =>
            {
                Assert.That(playbook.KindOf(heatNuke), Is.EqualTo(PlaybookSpellKind.Nuke));
                Assert.That(playbook.KindOf(heatDebuff), Is.EqualTo(PlaybookSpellKind.ResistDebuff), "its own damage type");
                Assert.That(playbook.KindOf(bodyDebuff), Is.EqualTo(PlaybookSpellKind.ResistDebuff), "the drain deals body");
                Assert.That(playbook.KindOf(slashDebuff), Is.EqualTo(PlaybookSpellKind.MeleeResistDebuff));
                Assert.That(playbook.KindOf(strDebuff), Is.EqualTo(PlaybookSpellKind.Debuff));
                Assert.That(playbook.KindOf(dot), Is.EqualTo(PlaybookSpellKind.Dot));
                Assert.That(playbook.KindOf(drain), Is.EqualTo(PlaybookSpellKind.Lifedrain));
                Assert.That(playbook.KindOf(areaDrain), Is.EqualTo(PlaybookSpellKind.AreaNuke));
                Assert.That(playbook.KindOf(snareNuke), Is.EqualTo(PlaybookSpellKind.SnareNuke));
                Assert.That(playbook.KindOf(area), Is.EqualTo(PlaybookSpellKind.AreaNuke));
                Assert.That(playbook.KindOf(bolt), Is.EqualTo(PlaybookSpellKind.Bolt));
                Assert.That(playbook.KindOf(snare), Is.EqualTo(PlaybookSpellKind.Control));
                Assert.That(playbook.KindOf(elemental), Is.EqualTo(PlaybookSpellKind.PetSummon));
                Assert.That(playbook.KindOfFunc, Is.SameAs(playbook.KindOfFunc), "one delegate per playbook");
                Assert.That(playbook.Level, Is.EqualTo(50));
                Assert.That(playbook.DamageTypeMask & BotPlaybook.DamageBit(eDamageType.Spirit), Is.Zero);
            });
        }

        [Test]
        public void ResistDebuffsOnlyForTheCastersOwnDamage()
        {
            // a Void Eldritch: cold nukes; its body and spirit debuffs fit nobody's damage here
            Spell coldNuke = S("DirectDamage", 100, eDamageType.Cold);
            Spell coldDebuff = S("ColdResistDebuff", level: 30);
            Spell spiritDebuff = S("SpiritResistDebuff", level: 35);
            Spell strDebuff = S("StrengthDebuff", level: 38);
            Spell slashDebuff = S("SlashResistDebuff", level: 39);
            var playbook = BotPlaybook.Build(new[] { coldNuke, coldDebuff, spiritDebuff, strDebuff, slashDebuff }, 50,
                eCharacterClass.Eldritch);
            Assert.Multiple(() =>
            {
                Assert.That(playbook.KindOf(spiritDebuff), Is.EqualTo(PlaybookSpellKind.OffResistDebuff));
                Assert.That(BotPlaybook.DebuffFits(PlaybookSpellKind.OffResistDebuff, true, true, false), Is.False);
                Assert.That(BotPlaybook.DebuffFits(PlaybookSpellKind.OffResistDebuff, false, false, true), Is.True,
                    "the player picked it in the AI Options");
                Assert.That(BotPlaybook.DebuffFits(PlaybookSpellKind.MeleeResistDebuff, false, false, false), Is.False,
                    "solo caster: nobody deals slash");
                Assert.That(BotPlaybook.DebuffFits(PlaybookSpellKind.MeleeResistDebuff, true, false, false), Is.True, "grouped");
                Assert.That(BotPlaybook.DebuffFits(PlaybookSpellKind.MeleeResistDebuff, false, true, false), Is.True, "melees");
                Assert.That(BotPlaybook.DebuffFits(PlaybookSpellKind.Debuff, false, false, false), Is.True);

                var all = new[] { slashDebuff, strDebuff, spiritDebuff, coldDebuff };
                Assert.That(BotPlaybook.ChooseOpeningDebuff(all, playbook.KindOfFunc, false, false, _ => false),
                    Is.SameAs(coldDebuff), "its own damage type first, though lower level");
                Assert.That(BotPlaybook.ChooseOpeningDebuff(new[] { slashDebuff, strDebuff, spiritDebuff },
                    playbook.KindOfFunc, false, false, _ => false), Is.SameAs(strDebuff), "then a stat debuff");
                Assert.That(BotPlaybook.ChooseOpeningDebuff(new[] { slashDebuff, spiritDebuff }, playbook.KindOfFunc,
                    false, false, _ => false), Is.Null, "nothing that fits");
                Assert.That(BotPlaybook.ChooseOpeningDebuff(new[] { slashDebuff, spiritDebuff }, playbook.KindOfFunc,
                    true, false, _ => false), Is.SameAs(slashDebuff), "grouped: the melee resist debuff");
                Assert.That(BotPlaybook.ChooseOpeningDebuff(all, playbook.KindOfFunc, false, false,
                    spell => spell == spiritDebuff), Is.SameAs(spiritDebuff), "a picked debuff goes first");
                Assert.That(BotPlaybook.ChooseOpeningDebuff(new[] { coldNuke }, playbook.KindOfFunc, false, false, null),
                    Is.Null);
            });
        }

        [Test]
        public void AttackOrderFollowsThePlaybook()
        {
            Spell bigNuke = S("DirectDamage", 120, power: 20);
            Spell cheapNuke = S("DirectDamage", 80, power: 6, castTime: 2);
            Spell dot = S("DamageOverTime", 40);
            Spell drain = S("Lifedrain", 90, eDamageType.Body);
            Spell snareNuke = S("DamageSpeedDecrease", 70, eDamageType.Cold);
            Spell bolt = S("Bolt", 150);
            Spell area = S("DirectDamage", 70, radius: 350);
            Spell str = S("StrengthDebuff");
            Spell snare = S("SpeedDecrease");
            var all = new[] { bigNuke, cheapNuke, dot, drain, snareNuke, bolt, area, str, snare };
            var playbook = BotPlaybook.Build(all, 50, eCharacterClass.Sorcerer);
            Assert.Multiple(() =>
            {
                Assert.That(Pick(playbook, all, Fight(health: 50)), Is.SameAs(drain), "hurt: the lifetap");
                Assert.That(Pick(playbook, all, Fight(petHit: true)), Is.SameAs(drain), "the pet is being hit");
                Assert.That(Pick(playbook, all, Fight(casts: 0)), Is.SameAs(bolt), "the bolt opens");
                Assert.That(Pick(playbook, all, Fight(snare: true)), Is.SameAs(snareNuke), "a runner: the snare nuke");
                Assert.That(Pick(playbook, all, Fight()), Is.SameAs(dot), "a fight that lasts: the DoT");
                Assert.That(Pick(playbook, all, Fight(targetHealth: 40)), Is.SameAs(bolt),
                    "under half health no DoT; the most damage per cast");
                Assert.That(Pick(playbook, all, Fight(lasts: false)), Is.SameAs(bolt), "a short fight: no DoT");
                var noBolt = all.Where(spell => spell != bolt).ToArray();
                Assert.That(Pick(playbook, noBolt, Fight(lasts: false)), Is.SameAs(bigNuke), "the biggest single nuke");
                Assert.That(Pick(playbook, noBolt, Fight(lasts: false, power: 20)), Is.SameAs(cheapNuke),
                    "under 30% power: the most damage per power point");
                Assert.That(Pick(playbook, new[] { area, str, snare }, Fight()), Is.SameAs(area), "area only when nothing else");
                Assert.That(Pick(playbook, new[] { str, snare }, Fight()), Is.SameAs(str), "then debuffs before control");
                Assert.That(Pick(playbook, new[] { snare }, Fight()), Is.SameAs(snare));
                Assert.That(Pick(playbook, noBolt, Fight(lasts: false), spell => spell == bigNuke), Is.SameAs(drain),
                    "a spell that failed to start is passed over");
                Assert.That(Pick(playbook, new[] { bigNuke }, Fight(lasts: false), spell => spell == bigNuke),
                    Is.SameAs(bigNuke), "... unless nothing else is left");
                Assert.That(Pick(playbook, Array.Empty<Spell>(), Fight()), Is.Null);
                // the same input gives the same answer: no random pick any more
                for (int i = 0; i < 20; i++)
                    Assert.That(Pick(playbook, noBolt, Fight(lasts: false)), Is.SameAs(bigNuke));
            });
        }

        [Test]
        public void ClassRotationsFromPrima()
        {
            // Cabalist: debuff, DoT, then drain
            Spell cabDot = S("DamageOverTime", 40, eDamageType.Matter);
            Spell cabDrain = S("Lifedrain", 80, eDamageType.Body);
            Spell cabNuke = S("DirectDamage", 95, eDamageType.Body);
            var cabalist = BotPlaybook.Build(new[] { cabDot, cabDrain, cabNuke }, 50, eCharacterClass.Cabalist);
            // Wizard: the bolt only as the opener
            Spell bolt = S("Bolt", 200);
            Spell fireNuke = S("DirectDamage", 120);
            var wizard = BotPlaybook.Build(new[] { bolt, fireNuke }, 50, eCharacterClass.Wizard);
            var runemaster = BotPlaybook.Build(new[] { bolt, fireNuke }, 50, eCharacterClass.Runemaster);
            // Bonedancer: the lifetap at about 60% health or when the pet is hit, else the nuke
            Spell bdTap = S("Lifedrain", 60, eDamageType.Body);
            Spell bdNuke = S("DirectDamage", 90, eDamageType.Cold);
            var bonedancer = BotPlaybook.Build(new[] { bdTap, bdNuke }, 50, eCharacterClass.Bonedancer);
            Assert.Multiple(() =>
            {
                Assert.That(cabalist.PrefersDrain, Is.True);
                Assert.That(Pick(cabalist, new[] { cabDot, cabDrain, cabNuke }, Fight()), Is.SameAs(cabDot));
                Assert.That(Pick(cabalist, new[] { cabDrain, cabNuke }, Fight()), Is.SameAs(cabDrain), "the drain, not the nuke");

                Assert.That(wizard.BoltOpenerOnly, Is.True);
                Assert.That(Pick(wizard, new[] { bolt, fireNuke }, Fight(casts: 0)), Is.SameAs(bolt));
                Assert.That(Pick(wizard, new[] { bolt, fireNuke }, Fight(casts: 1)), Is.SameAs(fireNuke));
                Assert.That(Pick(wizard, new[] { bolt }, Fight(casts: 3)), Is.SameAs(bolt), "a bolt beats no damage at all");
                Assert.That(runemaster.BoltOpenerOnly, Is.False);
                Assert.That(Pick(runemaster, new[] { bolt, fireNuke }, Fight(casts: 3)), Is.SameAs(bolt), "more damage");
                Assert.That(BotBrain.BoltAllowedByPlaybook(true, 0), Is.True);
                Assert.That(BotBrain.BoltAllowedByPlaybook(true, 1), Is.False);
                Assert.That(BotBrain.BoltAllowedByPlaybook(false, 5), Is.True);
                Assert.That(BotPlaybook.BoltLands(0, false), Is.True);
                Assert.That(BotPlaybook.BoltLands(1, false), Is.False, "the tank is on it");
                Assert.That(BotPlaybook.BoltLands(0, true), Is.False, "it is meleeing the caster");

                Assert.That(Pick(bonedancer, new[] { bdTap, bdNuke }, Fight(health: 59)), Is.SameAs(bdTap));
                Assert.That(Pick(bonedancer, new[] { bdTap, bdNuke }, Fight(petHit: true)), Is.SameAs(bdTap));
                Assert.That(Pick(bonedancer, new[] { bdTap, bdNuke }, Fight(health: 60, lasts: false)), Is.SameAs(bdNuke));
            });
        }

        [Test]
        public void PetSummonsKeepTheirOldShare()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotPlaybook.PetSummonTurn(0, 5, 0), Is.False, "no summons");
                Assert.That(BotPlaybook.PetSummonTurn(2, 5, 0), Is.True);
                Assert.That(BotPlaybook.PetSummonTurn(2, 5, 1), Is.True);
                Assert.That(BotPlaybook.PetSummonTurn(2, 5, 2), Is.False, "2 of 5 rolls, as the random pick gave");
                Assert.That(BotPlaybook.PetSummonTurn(1, 0, 0), Is.False);
                Assert.That(Enumerable.Range(0, 5).Count(roll => BotPlaybook.PetSummonTurn(2, 5, roll)), Is.EqualTo(2));
            });
        }

        /// <summary>Every class of all three realms builds a playbook from the same kit without throwing, with the class
        /// flags only where Prima puts them; the order itself reads only the spells, so it serves every spec.</summary>
        [Test]
        public void EveryClassInAllThreeRealmsBuildsAPlaybook()
        {
            eCharacterClass[] classes =
            {
                eCharacterClass.Armsman, eCharacterClass.Cabalist, eCharacterClass.Cleric, eCharacterClass.Friar,
                eCharacterClass.Infiltrator, eCharacterClass.Mercenary, eCharacterClass.Minstrel, eCharacterClass.Necromancer,
                eCharacterClass.Paladin, eCharacterClass.Reaver, eCharacterClass.Scout, eCharacterClass.Sorcerer,
                eCharacterClass.Theurgist, eCharacterClass.Wizard,
                eCharacterClass.Berserker, eCharacterClass.Bonedancer, eCharacterClass.Healer, eCharacterClass.Hunter,
                eCharacterClass.Runemaster, eCharacterClass.Savage, eCharacterClass.Shadowblade, eCharacterClass.Shaman,
                eCharacterClass.Skald, eCharacterClass.Spiritmaster, eCharacterClass.Thane, eCharacterClass.Warrior,
                eCharacterClass.Animist, eCharacterClass.Bard, eCharacterClass.Blademaster, eCharacterClass.Champion,
                eCharacterClass.Druid, eCharacterClass.Eldritch, eCharacterClass.Enchanter, eCharacterClass.Hero,
                eCharacterClass.Mentalist, eCharacterClass.Nightshade, eCharacterClass.Ranger, eCharacterClass.Valewalker,
                eCharacterClass.Warden,
            };
            Spell nuke = S("DirectDamage", 100);
            Spell dot = S("DamageOverTime", 40);
            Spell debuff = S("HeatResistDebuff");
            var kit = new[] { nuke, dot, debuff };
            foreach (eCharacterClass characterClass in classes)
            {
                BotPlaybook playbook = BotPlaybook.Build(kit, 50, characterClass);
                Assert.That(playbook.PrefersDrain, Is.EqualTo(characterClass == eCharacterClass.Cabalist));
                Assert.That(playbook.BoltOpenerOnly,
                    Is.EqualTo(characterClass is eCharacterClass.Wizard or eCharacterClass.Theurgist));
                Assert.That(Pick(playbook, kit, Fight()), Is.SameAs(dot), characterClass.ToString());
                Assert.That(BotPlaybook.ChooseOpeningDebuff(kit, playbook.KindOfFunc, false, false, null), Is.SameAs(debuff));
            }
        }
    }
}
