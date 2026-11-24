using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using B = DOL.GS.NoLiferBuffBots;

namespace DOL.GS.Tests
{
    /// <summary>The No Lifers' buffbots (Living Server 1.2): the pure rules. Tests only; nothing here is seen in game.</summary>
    [TestFixture]
    public class UT_NoLiferBuffBots
    {
        private static B.BuffCandidate Conc(int id, eSpellType type, int conc, int level, eSpellTarget target = eSpellTarget.REALM) =>
            new(id, $"{type}{level}", type, target, conc, level, false);

        private static B.BuffCandidate Timed(int id, eSpellType type, int level, eSpellTarget target = eSpellTarget.REALM) =>
            new(id, $"{type}{level}", type, target, 0, level, true);

        [Test]
        public void ThirtyPercentOfNoLifers_HaveABuffBot_AndNoOneElse()
        {
            int noLifers = 0, withBuffBot = 0;
            for (long id = 1; id <= 200_000; id++)
            {
                bool noLifer = AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.NoLifer;
                bool has = B.HasBuffBot(id, 30);
                if (!noLifer)
                {
                    Assert.That(has, Is.False, $"bot {id} is not a No Lifer");
                    continue;
                }
                noLifers++;
                if (has) withBuffBot++;
            }
            Assert.That(withBuffBot * 100.0 / noLifers, Is.EqualTo(30).Within(1.0));
        }

        [Test]
        public void Selection_IsFixedPerMain_AndGrowsWithThePercent()
        {
            long[] noLifers = Enumerable.Range(1, 20_000).Select(i => (long)i)
                .Where(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.NoLifer).ToArray();
            Assert.That(noLifers.Any(id => B.HasBuffBot(id, 0)), Is.False);
            Assert.That(noLifers.All(id => B.HasBuffBot(id, 100)), Is.True);
            foreach (long id in noLifers.Take(500))
            {
                Assert.That(B.HasBuffBot(id, 30), Is.EqualTo(B.HasBuffBot(id, 30)), "the same main every run");
                if (B.HasBuffBot(id, 30))
                    Assert.That(B.HasBuffBot(id, 50), Is.True, "a higher percent keeps every main that had one");
            }
            Assert.That(B.HasBuffBot(0, 100), Is.False);
            Assert.That(B.HasBuffBot(-5, 100), Is.False);
        }

        [Test]
        public void BuffBotIds_StayBetweenTheRosterAndTheSpecialForces()
        {
            Assert.That(B.BuffBotIdFor(1), Is.EqualTo(B.FirstBotId + 1));
            Assert.That(B.BuffBotIdFor(B.MaximumMainId), Is.LessThan(AutonomousEpicSpecialForces.FirstBotId));
            Assert.That(B.BuffBotIdFor(0), Is.EqualTo(0));
            Assert.That(B.BuffBotIdFor(B.MaximumMainId + 1), Is.EqualTo(0));
            Assert.That(B.HasBuffBot(B.MaximumMainId + 1, 100), Is.False);
        }

        [Test]
        public void ClassPerRealm_IsTheEraBuffer()
        {
            Assert.That(B.ClassFor(eRealm.Albion), Is.EqualTo(eCharacterClass.Cleric));
            Assert.That(B.ClassFor(eRealm.Midgard), Is.EqualTo(eCharacterClass.Shaman));
            Assert.That(B.ClassFor(eRealm.Hibernia), Is.EqualTo(eCharacterClass.Druid));
            Assert.That(B.ClassFor(eRealm.None), Is.EqualTo(eCharacterClass.Unknown));
            Assert.That(B.IsBuffBotClass(eCharacterClass.Healer), Is.False);
        }

        [TestCase(eCharacterClass.Cleric, Specs.Enhancement, Specs.Rejuvenation)]
        [TestCase(eCharacterClass.Shaman, Specs.Augmentation, Specs.Mending)]
        [TestCase(eCharacterClass.Druid, Specs.Nurture, Specs.Regrowth)]
        public void Spec_MaxesTheBuffLine_AndPutsTheRestInHealing(eCharacterClass characterClass, string buffLine, string healLine)
        {
            BotSpec spec = NoLiferBuffBotSpec.For(characterClass);
            Assert.That(spec, Is.InstanceOf<NoLiferBuffBotSpec>());
            BotSpecLine buff = spec.SpecLines.Single(line => line.Spec == buffLine);
            BotSpecLine heal = spec.SpecLines.Single(line => line.Spec == healLine);
            Assert.That(buff.SpecCap, Is.EqualTo(50));
            Assert.That(buff.levelRatio, Is.EqualTo(1.0f), "trained to its level at every level: 50 at 50");
            Assert.That(heal.levelRatio, Is.LessThan(buff.levelRatio));
            Assert.That(NoLiferBuffBotSpec.BuffLine(characterClass), Is.EqualTo(buffLine));
            Assert.That(NoLiferBuffBotSpec.BuffLine(eCharacterClass.Armsman), Is.Null);
        }

        [Test]
        public void BuffPlan_SingleTargetOnly_BestRank_InPriorityOrder()
        {
            var known = new[]
            {
                Conc(1, eSpellType.StrengthBuff, 10, 30), Conc(2, eSpellType.StrengthBuff, 19, 50),
                Conc(3, eSpellType.BaseArmorFactorBuff, 15, 42), Conc(4, eSpellType.SpecArmorFactorBuff, 32, 44),
                Conc(5, eSpellType.StrengthConstitutionBuff, 33, 46), Conc(6, eSpellType.DexterityQuicknessBuff, 37, 50),
                Timed(7, eSpellType.BodyResistBuff, 45, eSpellTarget.GROUP),       // group only: needs a group
                Timed(8, eSpellType.DefensiveProc, 49, eSpellTarget.GROUP),
                Conc(9, eSpellType.DamageAdd, 18, 49, eSpellTarget.SELF),          // self only
                Timed(10, eSpellType.DamageAdd, 44),                               // Shaman's realm damage add (timed)
                Timed(11, eSpellType.Heal, 50),                                    // not a buff
            };
            var plan = B.PlanBuffs(known, mainUsesPower: true, mainUsesWeapons: true, concentrationPool: 500);
            Assert.That(plan.Select(p => p.SpellId), Is.EqualTo(new[] { 4, 5, 6, 3, 2, 10 }));
        }

        [Test]
        public void BuffPlan_FitsTheMain()
        {
            var known = new[]
            {
                Conc(1, eSpellType.AcuityBuff, 15, 42), Conc(2, eSpellType.CombatSpeedBuff, 19, 50),
                Timed(3, eSpellType.DamageAdd, 44), Conc(4, eSpellType.EnduranceRegenBuff, 5, 42),
                Conc(5, eSpellType.SpecArmorFactorBuff, 32, 44),
            };
            var caster = B.PlanBuffs(known, mainUsesPower: true, mainUsesWeapons: false, 500).Select(p => p.Type).ToArray();
            Assert.That(caster, Is.EquivalentTo(new[] { eSpellType.SpecArmorFactorBuff, eSpellType.AcuityBuff }));
            var tank = B.PlanBuffs(known, mainUsesPower: false, mainUsesWeapons: true, 500).Select(p => p.Type).ToArray();
            Assert.That(tank, Does.Not.Contain(eSpellType.AcuityBuff));
            Assert.That(tank, Does.Contain(eSpellType.CombatSpeedBuff).And.Contain(eSpellType.DamageAdd).And.Contain(eSpellType.EnduranceRegenBuff));
        }

        [Test]
        public void BuffPlan_ConcentrationRunsOutInPriorityOrder_TimedBuffsStillGo()
        {
            var known = new[]
            {
                Conc(1, eSpellType.SpecArmorFactorBuff, 32, 44), Conc(2, eSpellType.StrengthConstitutionBuff, 33, 46),
                Conc(3, eSpellType.DexterityQuicknessBuff, 37, 50), Conc(4, eSpellType.StrengthBuff, 19, 50),
                Timed(5, eSpellType.DamageAdd, 44),
            };
            var plan = B.PlanBuffs(known, true, true, concentrationPool: 70);
            // 32 + 33 = 65; dex/qui (37) no longer fits, strength (19) neither; the timed damage add costs none.
            Assert.That(plan.Select(p => p.SpellId), Is.EqualTo(new[] { 1, 2, 5 }));
            Assert.That(B.PlanBuffs(known, true, true, 0).Select(p => p.SpellId), Is.EqualTo(new[] { 5 }));
            Assert.That(B.PlanBuffs(null, true, true, 100), Is.Empty);
        }

        private static B.Situation Fine => new(true, false, true, true, 600, false, false, false, false, false);

        [Test]
        public void Decide_FollowsOnlyThroughItsRealmsLand()
        {
            Assert.That(B.Decide(Fine).Stance, Is.EqualTo(B.Stance.Follow));
            Assert.That(B.Decide(Fine with { MainInDungeon = true }).Stance, Is.EqualTo(B.Stance.Park));
            Assert.That(B.Decide(Fine with { MainInFrontier = true }).Stance, Is.EqualTo(B.Stance.Park));
            Assert.That(B.Decide(Fine with { MainInBattleground = true }).Stance, Is.EqualTo(B.Stance.Park));
            Assert.That(B.Decide(Fine with { MainInRaid = true }).Stance, Is.EqualTo(B.Stance.Park));
            Assert.That(B.Decide(Fine with { MainOnStableRoute = true }).Stance, Is.EqualTo(B.Stance.Wait));
            Assert.That(B.Decide(Fine with { MainAlive = false }).Stance, Is.EqualTo(B.Stance.Wait));
            Assert.That(B.Decide(Fine with { SameRegion = false, Distance = double.MaxValue }).Stance, Is.EqualTo(B.Stance.Relocate));
            Assert.That(B.Decide(Fine with { Distance = B.RelocateDistance + 1 }).Stance, Is.EqualTo(B.Stance.Relocate));
        }

        [Test]
        public void Decide_AttackedItRuns_DeadItWaitsForRelease_RaidBeatsEverythingElse()
        {
            Assert.That(B.Decide(Fine with { BuffBotAttacked = true, MainInDungeon = true }).Stance, Is.EqualTo(B.Stance.Flee));
            Assert.That(B.Decide(Fine with { BuffBotAlive = false, BuffBotAttacked = true }).Stance, Is.EqualTo(B.Stance.Dead));
            Assert.That(B.Decide(Fine with { MainInRaid = true, SameRegion = false }).Stance, Is.EqualTo(B.Stance.Park),
                "a raid elsewhere: it parks, never follows into it");
            Assert.That(B.Decide(Fine with { MainInFrontier = true, SameRegion = false }).Stance, Is.EqualTo(B.Stance.Park));
        }

        [Test]
        public void Buffing_OnlyInReach_OutOfFights_NeverOnTheMove()
        {
            Assert.That(B.MayBuff(B.Stance.Follow, true, true, 800, false), Is.True);
            Assert.That(B.MayBuff(B.Stance.Park, true, true, 800, false), Is.True, "the main came back to its parked buffbot");
            Assert.That(B.MayBuff(B.Stance.Follow, true, true, B.CastReach + 1, false), Is.False);
            Assert.That(B.MayBuff(B.Stance.Follow, false, true, 100, false), Is.False);
            Assert.That(B.MayBuff(B.Stance.Follow, true, false, 100, false), Is.False);
            Assert.That(B.MayBuff(B.Stance.Follow, true, true, 100, true), Is.False);
            Assert.That(B.MayBuff(B.Stance.Flee, true, true, 100, false), Is.False);
            Assert.That(B.MayBuff(B.Stance.Relocate, true, true, 100, false), Is.False);
            Assert.That(B.FollowDistanceFor(mainInCombat: true), Is.GreaterThan(B.FollowDistanceFor(false)));
            Assert.That(B.FollowDistanceFor(true), Is.LessThan(B.CastReach), "it can rebuff from where it hangs back");
        }

        [Test]
        public void NeverGrouped_TheInviteRuleSaysSo()
        {
            // #485: the word "buffbot" only with bot_show_as_bots; hidden (the default) it reads as a player's decline.
            Assert.That(BotGroupInvite.BusyMessage("Bobbuff", BotGroupInvite.Duty.Buffing, showBots: true), Does.Contain("never joins a group"));
            Assert.That(BotGroupInvite.BusyMessage("Bobbuff", BotGroupInvite.Duty.Buffing, showBots: false), Is.EqualTo("Bobbuff declines your invitation."));
            Assert.That(B.MayJoinGroup(null), Is.True, "only a buffbot is refused");
        }

        [Test]
        public void Names_FixedFromTheMain_AtMostTwentyLetters_NeverTaken()
        {
            int named = 0;
            for (long id = 1; id <= 3_000; id++)
            {
                string name = B.MainStyleName("Thorgrim", id, null);
                Assert.That(name, Is.EqualTo(B.MainStyleName("Thorgrim", id, null)));
                if (name == null) continue;
                named++;
                Assert.That(name, Does.StartWith("Thorgrim"));
                Assert.That(name.Length, Is.LessThanOrEqualTo(20));
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
                Assert.That(B.MainStyleName("Thorgrim", id, taken), Is.Not.EqualTo(name));
            }
            Assert.That(named / 3000.0, Is.EqualTo(0.35).Within(0.05));
            Assert.That(B.MainStyleName("Averyveryverylongnam", 1, null), Is.Null.Or.Length.LessThanOrEqualTo(20));
        }

        [Test]
        public void Lines_NameTheMain()
        {
            for (int roll = 0; roll < B.TellLines.Length; roll++)
                Assert.That(B.Line(B.TellLines, "Thorgrim", roll), Does.Not.Contain("{main}"));
            Assert.That(B.Line(B.InviteLines, null, 3), Does.Not.Contain("{main}"));
        }
    }
}
