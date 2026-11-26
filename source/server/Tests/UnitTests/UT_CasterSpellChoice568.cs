using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using Kind = DOL.GS.BotControlSpells.ControlKind;
using Holder = DOL.GS.BotHoldDamage.Holder;

namespace DOL.UnitTests
{
    /// <summary>
    /// #568: caster spell choice like a veteran (C1-C8). Pure rules only; tests, not an in-game check.
    /// </summary>
    [TestFixture]
    public class UT_CasterSpellChoice568
    {
        // ---- C1: the mezzed add (DividedRealms 9e18aa53 spec) -------------------------------------------------------

        [Test]
        public void C1_AGroupmatesFightCountsAsTheFight() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.UncontrolledFightRule(true, false), Is.True, "its own list has a free monster");
            Assert.That(BotBrain.UncontrolledFightRule(false, true), Is.True,
                "only the mezzed add on its own list, but a groupmate fights another monster");
            Assert.That(BotBrain.UncontrolledFightRule(false, false), Is.False, "everything left is controlled");
            // the companion leaves the mezzed add alone while the group fights elsewhere ...
            Assert.That(BotBrain.PassOverControlled(true, BotBrain.UncontrolledFightRule(false, true)), Is.True);
            // ... and breaks it only when nothing else is left
            Assert.That(BotBrain.PassOverControlled(true, BotBrain.UncontrolledFightRule(false, false)), Is.False);
            Assert.That(BotBrain.PassOverControlled(false, BotBrain.UncontrolledFightRule(false, true)), Is.False);
        });

        private static Spell S(string type, double value = 0, double damage = 0, int recast = 0, int subSpell = 0,
            int radius = 0, int id = 0, int castTime = 0) =>
            new(new DbSpell { SpellID = id, Name = type, Type = type, Target = "Enemy", Value = value, Damage = damage,
                RecastDelay = recast, SubSpellID = subSpell, Radius = radius, Range = 1500, CastTime = castTime / 1000.0 }, 10);

        // ---- C2: control on the bot's own target (True165 a95ef07c4) ------------------------------------------------

        [Test]
        public void C2_ControlSpellsAreClassifiedByType() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.KindOfType(S("Mesmerize")), Is.EqualTo(Kind.Mez));
            Assert.That(BotControlSpells.KindOfType(S("Stun")), Is.EqualTo(Kind.Stun));
            Assert.That(BotControlSpells.KindOfType(S("Nearsight", 25)), Is.EqualTo(Kind.Nearsight));
            Assert.That(BotControlSpells.KindOfType(S("SpeedDecrease", 40)), Is.EqualTo(Kind.Snare));
            Assert.That(BotControlSpells.KindOfType(S("SpeedDecrease", 99)), Is.EqualTo(Kind.Root));
            Assert.That(BotControlSpells.KindOfType(S("UnbreakableSpeedDecrease", 40)), Is.EqualTo(Kind.Snare));
            Assert.That(BotControlSpells.KindOfType(S("UnbreakableSpeedDecrease", 99)), Is.EqualTo(Kind.Root));
            Assert.That(BotControlSpells.KindOfType(S("DamageSpeedDecrease", 40, 148)), Is.EqualTo(Kind.None),
                "a snare-nuke is damage");
            Assert.That(BotControlSpells.KindOfType(S("DirectDamage", 0, 100)), Is.EqualTo(Kind.None));
            Assert.That(BotControlSpells.KindOfType(S("StrengthDebuff", 20)), Is.EqualTo(Kind.None));
            Assert.That(BotControlSpells.KindOfType(null), Is.EqualTo(Kind.None));
        });

        [Test]
        public void C2_BombersAreClassifiedByTheirPayload()
        {
            var payloads = new Dictionary<int, Spell>
            {
                [11367] = S("SpeedDecrease", 99, id: 11367),
                [11110] = S("DirectDamage", 0, 225, id: 11110),
            };
            Spell Resolve(int id) => payloads.TryGetValue(id, out Spell spell) ? spell : null;
            Spell vines = S("Bomber", 50, 50, subSpell: 11367);
            Spell hatred = S("Bomber", 50, 50, subSpell: 11110);
            Spell unknown = S("Bomber", 50, 50, subSpell: 999999);
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.KindWith(vines, Resolve), Is.EqualTo(Kind.Root));
                Assert.That(BotControlSpells.IsControlBomberWith(hatred, Resolve), Is.False);
                Assert.That(BotControlSpells.IsControlWith(unknown, Resolve), Is.False, "an unknown payload is not guessed");
                Assert.That(BotCasterPriority.IsDamageWith(vines, Resolve), Is.False, "a root bomber is no nuke");
                Assert.That(BotCasterPriority.IsDamageWith(hatred, Resolve), Is.True);
                Assert.That(BotCasterPriority.IsDamageWith(unknown, Resolve), Is.True, "unchanged when unresolved");
            });
        }

        [Test]
        public void C2_NoMezRootOrNearsightOnTheKillTarget() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.None, false, false, false), Is.True, "nukes are not control");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Mez, false, true, true), Is.False, "no mez on the kill target");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Root, false, true, true), Is.False, "a Druid roots its own target");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Nearsight, false, true, true), Is.False);
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Snare, false, false, true), Is.False, "a held monster");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Snare, false, true, false), Is.True, "a runner");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Stun, false, true, false), Is.False, "stun as filler");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Stun, false, false, true), Is.True);
            foreach (Kind kind in new[] { Kind.Mez, Kind.Root, Kind.Snare, Kind.Stun, Kind.Nearsight })
                Assert.That(BotControlSpells.AllowsOnOwnTarget(kind, true, false, false), Is.True, "PvP keeps " + kind);
        });

        [TestCase(true, 20, false, true)]
        [TestCase(true, 20, true, false)]    // still swinging at its victim
        [TestCase(false, 20, false, false)]  // standing
        [TestCase(true, 80, false, false)]   // healthy and walking: no runner
        [TestCase(true, 35, false, false)]
        public void C2_ASnareOnlyOnARunner(bool moving, int health, bool inMelee, bool runner) =>
            Assert.That(BotControlSpells.IsRunner(moving, health, inMelee), Is.EqualTo(runner));

        [TestCase(true, false, false, false, true)]   // interrupt a casting monster
        [TestCase(false, true, false, false, true)]   // peel a monster off the healer
        [TestCase(false, false, false, true, false)]  // never filler, even in an emergency
        [TestCase(true, false, true, false, false)]   // Paralyzing Glance waits for an emergency
        [TestCase(false, true, true, true, true)]
        public void C2_AStunOnlyInterruptsOrPeels(bool casting, bool peel, bool longReuse, bool emergency, bool worthwhile) =>
            Assert.That(BotControlSpells.StunWorthwhile(casting, peel, longReuse, emergency), Is.EqualTo(worthwhile));

        [Test]
        public void C2_LongReuseIsAMinuteOrMore() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.IsLongReuse(S("Stun", recast: 600)), Is.True, "Paralyzing Glance");
            Assert.That(BotControlSpells.IsLongReuse(S("Mesmerize", recast: 600)), Is.True, "Hypnotic Harmony");
            Assert.That(BotControlSpells.IsLongReuse(S("Stun", recast: 10)), Is.False, "Stunning Yell");
            Assert.That(BotControlSpells.IsLongReuse(S("Stun")), Is.False, "Halt");
        });

        [Test]
        public void C2_ControlIsNoDebuffAndSnareNukesAreDamage() => Assert.Multiple(() =>
        {
            foreach (Spell control in new[] { S("Stun"), S("Nearsight", 25), S("SpeedDecrease", 40), S("SpeedDecrease", 99),
                         S("UnbreakableSpeedDecrease", 40), S("Mesmerize") })
                Assert.That(BotCasterPriority.IsDebuff(control), Is.False, control.Name);
            Assert.That(BotCasterPriority.IsDamage(S("DamageSpeedDecrease", 40, 148)), Is.True);
        });

        // ---- C3: the plain nuke on a held target --------------------------------------------------------------------

        [Test]
        public void C3_AHeldTargetGetsThePlainNukeOverASnareNuke()
        {
            Spell demobilizing = S("DamageSpeedDecrease", 40, 148);
            Spell plain = S("DirectDamage", 0, 140);
            Spell weak = S("DirectDamage", 0, 60);
            Spell area = S("DirectDamage", 0, 160, radius: 350);
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, plain, true), Is.True);
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, plain, false), Is.False, "nobody holds it");
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, weak, true), Is.False, "no comparable nuke");
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, area, true), Is.False, "an area spell is another job");
                Assert.That(BotControlSpells.PrefersPlainNuke(plain, demobilizing, true), Is.False);
            });
        }

        // ---- C4: utility spells (True165 edcfb0f4d) -----------------------------------------------------------------

        [Test]
        public void C4_ShedAggroOnlyWithALivingTankToTakeItBack() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.ShedsAggro(true, false, true, true, false), Is.True);
            Assert.That(BotBrain.ShedsAggro(false, false, true, true, false), Is.False, "solo: nobody to take it");
            Assert.That(BotBrain.ShedsAggro(true, true, true, true, false), Is.False, "the tank keeps it");
            Assert.That(BotBrain.ShedsAggro(true, false, false, true, false), Is.False, "no living tank");
            Assert.That(BotBrain.ShedsAggro(true, false, true, false, false), Is.False, "nothing on me");
            Assert.That(BotBrain.ShedsAggro(true, false, true, true, true), Is.False, "RvR keeps its own rules");
        });

        [Test]
        public void C4_AmnesiaHandsTheMonsterBackToANearPet() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.AmnesiaForPet(true, true, 300), Is.True, "the monster turned from the pet to the caster");
            Assert.That(BotBrain.AmnesiaForPet(true, true, BotBrain.AmnesiaPetReach), Is.True);
            Assert.That(BotBrain.AmnesiaForPet(true, true, 900), Is.False, "the pet is too far to take it back");
            Assert.That(BotBrain.AmnesiaForPet(true, false, 100), Is.False, "no living pet");
            Assert.That(BotBrain.AmnesiaForPet(false, true, 100), Is.False, "the monster is not on the caster");
        });

        [Test]
        public void C4_LifeTransferOnlyUnderTheThresholds() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.LifeTransferWanted(34, 60), Is.True);
            Assert.That(BotBrain.LifeTransferWanted(35, 100), Is.False, "the patient is not low enough");
            Assert.That(BotBrain.LifeTransferWanted(10, 59), Is.False, "the Cabalist itself is too hurt");
        });

        [Test]
        public void C4_ConfusionOnlyOnAnEnemyPlayersPetAndNeverTheUnsafeKind() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.ConfusionOnEnemyPet(true, true, true), Is.True);
            Assert.That(BotBrain.ConfusionOnEnemyPet(false, true, true), Is.False, "it does not work on players");
            Assert.That(BotBrain.ConfusionOnEnemyPet(true, false, true), Is.False, "a monster's pet");
            Assert.That(BotBrain.ConfusionOnEnemyPet(true, true, false), Is.False, "our own realm's pet");
            Assert.That(BotBrain.ConfusionSafe(S("Confusion", 0)), Is.True);
            Assert.That(BotBrain.ConfusionSafe(S("Confusion", -50)), Is.False, "could turn it on its neighbours");
            Assert.That(BotBrain.ConfusionSafe(null), Is.False);
        });

        [Test]
        public void C4_CompanionAmnesiaAndLifeTransferOffAreHonoured() => Assert.Multiple(() =>
        {
            // the AI Options rows exist for the spells (and CompanionSpellOptions165.Blocks refuses them at the cast)
            Assert.That(CompanionOptions165.Caption(CompanionOptions165.KindAmnesia), Is.EqualTo("Amnesia"));
            Assert.That(CompanionOptions165.Caption(CompanionOptions165.KindLifeTransfer), Is.EqualTo("Life transfer"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindAmnesia).Value.Choices[1], Is.EqualTo("Off"));
        });

        // ---- C5: casters wait for the tank or pet (True165 b3498be, 5a96d4b) ----------------------------------------

        [Test]
        public void C5_PetHoldsDamageAbovePercent() => Assert.Multiple(() =>
        {
            Assert.That(BotHoldDamage.PetHoldsDamageAbovePercent(eCharacterClass.Spiritmaster), Is.EqualTo(75), "Prima");
            Assert.That(BotHoldDamage.PetHoldsDamageAbovePercent(eCharacterClass.Enchanter), Is.EqualTo(85));
            Assert.That(BotHoldDamage.PetHoldsDamageAbovePercent(eCharacterClass.Cabalist), Is.EqualTo(85));
        });

        private static bool Hold(bool ranged = true, bool monster = true, bool onCaster = false, bool tank = true,
            bool pet = false, long held = 1_000, Holder holder = Holder.GroupTank, int health = 90,
            eCharacterClass cls = eCharacterClass.Wizard) =>
            BotHoldDamage.Holds(ranged, monster, onCaster, tank, pet, held, holder, health, cls);

        [Test]
        public void C5_CastersWaitForTheTankOrPet() => Assert.Multiple(() =>
        {
            Assert.That(Hold(), Is.True, "the tank holds it at 90%");
            Assert.That(Hold(health: 75), Is.False, "the tank has it under 75%: nuke");
            Assert.That(Hold(holder: Holder.Nobody), Is.True, "the group waits for its tank's pull");
            Assert.That(Hold(holder: Holder.Other), Is.True, "a healer has it: the tank takes it back first");
            Assert.That(Hold(tank: false, pet: true, holder: Holder.OwnPet, health: 86, cls: eCharacterClass.Enchanter), Is.True);
            Assert.That(Hold(tank: false, pet: true, holder: Holder.OwnPet, health: 85, cls: eCharacterClass.Enchanter), Is.False);
            Assert.That(Hold(tank: false, pet: true, holder: Holder.OwnPet, health: 80, cls: eCharacterClass.Spiritmaster),
                Is.True, "a Spiritmaster's pet holds it to 75%");
            Assert.That(Hold(tank: false, pet: true, holder: Holder.OwnPet, health: 75, cls: eCharacterClass.Spiritmaster),
                Is.False);
            Assert.That(Hold(tank: false, pet: true, holder: Holder.Nobody), Is.False, "a pet caster opens itself");
        });

        [Test]
        public void C5_TheHoldEndsAndNeverAppliesWhereItWouldHurt() => Assert.Multiple(() =>
        {
            Assert.That(Hold(held: BotHoldDamage.HoldMilliseconds), Is.True);
            Assert.That(Hold(held: BotHoldDamage.HoldMilliseconds + 1), Is.False, "8 s at most per target");
            Assert.That(Hold(onCaster: true), Is.False, "the monster is on the caster");
            Assert.That(Hold(monster: false), Is.False, "PvP: no hold");
            Assert.That(Hold(ranged: false), Is.False, "a melee hybrid");
            Assert.That(Hold(tank: false, pet: false, holder: Holder.Nobody), Is.False, "solo without a pet");
        });

        [Test]
        public void C5_HoldingDamageKeepsDebuffsAndPurposefulStunsAndPvpControl()
        {
            Spell stun = S("Stun"), snare = S("SpeedDecrease", 40), root = S("SpeedDecrease", 99), mez = S("Mesmerize"),
                nearsight = S("Nearsight", 25), debuff = S("StrengthDebuff", 20), nuke = S("DirectDamage", 0, 100),
                snareNuke = S("DamageSpeedDecrease", 40, 148);
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.HoldDamageStrips(debuff, false), Is.False);
                Assert.That(BotControlSpells.HoldDamageStrips(stun, false), Is.False, "an approved stun interrupts or peels");
                Assert.That(BotControlSpells.HoldDamageStrips(nuke, false), Is.True);
                Assert.That(BotControlSpells.HoldDamageStrips(snareNuke, false), Is.True);
                foreach (Spell control in new[] { snare, root, mez, nearsight })
                {
                    Assert.That(BotControlSpells.HoldDamageStrips(control, false), Is.True, control.Name + " on a monster");
                    Assert.That(BotControlSpells.HoldDamageStrips(control, true), Is.False, control.Name + " on a player's pet");
                }
            });
        }

        // ---- C6: the debuff opening and the bolt rule (the playbook itself: UT_VeteranPlaybook568) ----------------

        [Test]
        public void C6_DebuffOpeningOnAFreshTargetThatLasts() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, false, 40, 100, false, 0), Is.True, "an even con");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, false, 35, 100, false, 0), Is.False, "a blue solo");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, true, 35, 100, false, 0), Is.True, "grouped");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, false, 20, 100, true, 0), Is.True, "a player");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, true, 40, 100, false, 2), Is.False, "two cast: nuke");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, true, 40, 100, false, 2, 3), Is.True,
                "three debuffs the player marked Use");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 29, true, 40, 100, false, 0), Is.False, "low on power");
            Assert.That(BotCasterPriority.DebuffOpeningWorthwhile(40, 100, true, 40, 59, false, 0), Is.False, "already hurt");
            Assert.That(BotCasterPriority.AllowInstant(S("StrengthDebuff", 20), true, false, true), Is.True);
            Assert.That(BotCasterPriority.AllowInstant(S("StrengthDebuff", 20), true, false, false), Is.False);
        });

        [Test]
        public void C6_NoBoltIntoAMeleeScrum() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.BoltWorthwhile(0), Is.True);
            Assert.That(BotCasterPriority.BoltWorthwhile(1), Is.False, "the tank is on it");
        });

        // ---- C7: PvP crowd control cleanup (DetShaft abd63c964 spec) ------------------------------------------------

        [Test]
        public void C7_NoAreaMezOnAPackAlreadyMezzedOrImmune() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.PvpAreaMezWorthwhile(2, 1_000, 0), Is.True);
            Assert.That(BotBrain.PvpAreaMezWorthwhile(1, 1_000, 0), Is.False,
                "the rest of the pack is mezzed or immune: one free enemy is no area mez");
            Assert.That(BotBrain.PvpAreaMezWorthwhile(0, 1_000, 0), Is.False);
            Assert.That(BotBrain.PvpAreaMezWorthwhile(5, 1_000, 1_000 + BotBrain.PvpAreaMezHoldMilliseconds), Is.False,
                "held 60 s after an attempt");
            Assert.That(BotBrain.PvpAreaMezWorthwhile(5, 61_000, 61_000), Is.True);
        });

        [Test]
        public void C7_NoMezOrRootOnAnEnemyAGroupmateHits() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.PvpMezOrRootAllowed(true, true), Is.False);
            Assert.That(BotBrain.PvpMezOrRootAllowed(true, false), Is.True);
            Assert.That(BotBrain.PvpMezOrRootAllowed(false, true), Is.True, "a stun still goes");
        });

        [Test]
        public void C7_ControlOrderPutsSlowAreaMezzesLast() => Assert.Multiple(() =>
        {
            int quickArea = BotBrain.PvpControlRank(true, 350, 2_500);
            int single = BotBrain.PvpControlRank(true, 0, 2_500);
            int slowArea = BotBrain.PvpControlRank(true, 350, 4_000);
            int root = BotBrain.PvpControlRank(false, 0, 2_500);
            Assert.That(quickArea, Is.LessThan(single));
            Assert.That(single, Is.LessThan(slowArea));
            Assert.That(slowArea, Is.LessThan(root));
        });

        [Test]
        public void C7_AClaimDropsASecondAfterItsCastStops() => Assert.Multiple(() =>
        {
            const long until = 10_000;
            Assert.That(BotPvpCrowdControl.ClaimHolds(1_000, until, false, true, 0), Is.True, "the cast is running");
            Assert.That(BotPvpCrowdControl.ClaimHolds(1_000, until, false, false, 0), Is.True, "not yet seen stopped");
            Assert.That(BotPvpCrowdControl.ClaimHolds(2_000, until, false, false, 1_000), Is.True, "within the 1 s grace");
            Assert.That(BotPvpCrowdControl.ClaimHolds(2_001, until, false, false, 1_000), Is.False, "the cast stopped");
            Assert.That(BotPvpCrowdControl.ClaimHolds(1_000, until, true, true, 0), Is.False, "the target is immune");
            Assert.That(BotPvpCrowdControl.ClaimHolds(until, until, false, true, 0), Is.False, "the time ran out");
        });

        // ---- C8: "Attack spells: None" keeps the self bladeturn up --------------------------------------------------

        [Test]
        public void C8_AttackSpellsNoneRecastsTheBladeturnInAFight() => Assert.Multiple(() =>
        {
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(true, CompanionSpellOptions165.AttackNone), Is.True,
                "a Runemaster set to None");
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(true, 0), Is.False, "Automatic");
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(true, CompanionSpellOptions165.AttackAreaOnly), Is.False);
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(false, CompanionSpellOptions165.AttackNone), Is.False,
                "gamebots never");
            Assert.That(BotBrain.SelfBladeturnAggroWaits(false, true), Is.True, "the ordinary upkeep waits for the fight's end");
            Assert.That(BotBrain.SelfBladeturnAggroWaits(true, true), Is.False, "None: aggro is no reason to wait");
            Assert.That(BotBrain.SelfBladeturnSafe(true, BotBrain.SelfBladeturnAggroWaits(true, true), false, false, false, 3_000),
                Is.True, "in a fight, nothing hit it for 3 s");
            Assert.That(BotBrain.SelfBladeturnSafe(true, BotBrain.SelfBladeturnAggroWaits(true, true), false, false, false, 1_000),
                Is.False, "still never while being hit");
        });
    }
}
