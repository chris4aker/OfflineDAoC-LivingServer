using System;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 6: a soloer, duo or stealther opens only when the odds allow (RvrSoloEngage), with the
    /// tier's mistakes; the stealth opener is allowed only from stealth, in scope, for a melee stealther, once per target
    /// (RvrStealthOpener), and the style policy puts a usable opener first. Pure.
    /// </summary>
    [TestFixture]
    public class UT_RvrSoloEngage
    {
        private static RvrSoloFacts Healthy(int targetLevel = 50, int enemies = 1, int healers = 0, int allies = 1) => new()
        {
            OwnLevel = 50,
            OwnHealthPercent = 100,
            UsesPower = true,
            OwnPowerPercent = 100,
            Allies = allies,
            TargetLevel = targetLevel,
            TargetHealthPercent = 100,
            EnemiesNearTarget = enemies,
            HealersNearTarget = healers,
        };

        [Test]
        public void A_soloer_opens_on_a_lone_equal_and_holds_against_a_group()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrSoloEngage.OddsAllow(Healthy()), Is.True, "alone, same level");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(targetLevel: 52)), Is.False, "alone but stronger");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(enemies: 2)), Is.False, "two of them");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(enemies: 3, allies: 2)), Is.False, "a group against a duo");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(enemies: 2, allies: 2) with { TargetLevel = 45 }), Is.True, "a duo on a weaker pair");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(enemies: 2, healers: 1, allies: 2)), Is.False, "a healer counts twice");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy() with { OwnHealthPercent = 60 }), Is.False, "hurt");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy() with { OwnPowerPercent = 20 }), Is.False, "out of power");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy() with { UsesPower = false, OwnPowerPercent = 0 }), Is.True, "a melee class needs no power");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(targetLevel: 53) with { Stealthed = true }), Is.True, "from stealth, a few levels up");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(targetLevel: 55) with { Stealthed = true }), Is.False);
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(targetLevel: 52) with { TargetHealthPercent = 40 }), Is.True, "a wounded target");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(targetLevel: 52) with { TargetBusy = true }), Is.True, "a target busy with someone else");
                Assert.That(RvrSoloEngage.OddsAllow(Healthy(enemies: 8) with { TargetAttackingUs = true, OwnHealthPercent = 10 }), Is.True,
                    "fighting back is always allowed");
            });
        }

        [Test]
        public void The_tier_misjudges_the_odds_sometimes_but_never_fighting_back_or_badly_hurt()
        {
            RvrSoloFacts good = Healthy(), bad = Healthy(enemies: 5);
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
            {
                double chance = RvrSoloEngage.MistakeChance(tier);
                Assert.That(RvrSoloEngage.MayOpen(good, tier, chance + 0.01), Is.True, tier.ToString());
                Assert.That(RvrSoloEngage.MayOpen(bad, tier, chance + 0.01), Is.False);
                Assert.That(RvrSoloEngage.MayOpen(good, tier, chance - 0.01), Is.False, "lets a good target pass");
                Assert.That(RvrSoloEngage.MayOpen(bad, tier, chance - 0.01), Is.True, "opens on bad odds");
                Assert.That(RvrSoloEngage.MayOpen(bad with { TargetAttackingUs = true }, tier, 0), Is.True);
                Assert.That(RvrSoloEngage.MayOpen(bad with { OwnHealthPercent = 20 }, tier, 0), Is.False);
            }
            Assert.That(RvrSoloEngage.MistakeChance(RvrSkillTier.Casual), Is.GreaterThan(RvrSoloEngage.MistakeChance(RvrSkillTier.Average)));
            Assert.That(RvrSoloEngage.MistakeChance(RvrSkillTier.Average), Is.GreaterThan(RvrSoloEngage.MistakeChance(RvrSkillTier.Veteran)));
            int opened = Enumerable.Range(0, 10_000).Count(k => RvrSoloEngage.MayOpen(bad, RvrSkillTier.Casual, k / 10_000.0));
            Assert.That(opened / 100.0, Is.EqualTo(20).Within(0.5), "a casual soloer opens on bad odds one time in five");
        }

        [Test]
        public void The_stealth_opener_needs_stealth_scope_the_switch_and_a_melee_stealther_once_per_target()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrStealthOpener.Allows(true, true, true, true, true, false), Is.True);
                Assert.That(RvrStealthOpener.Allows(false, true, true, true, true, false), Is.False, "switch off");
                Assert.That(RvrStealthOpener.Allows(true, false, true, true, true, false), Is.False, "out of scope (raid, keep fight, player group)");
                Assert.That(RvrStealthOpener.Allows(true, true, false, true, true, false), Is.False, "not a stealth style");
                Assert.That(RvrStealthOpener.Allows(true, true, true, false, true, false), Is.False, "not hidden");
                Assert.That(RvrStealthOpener.Allows(true, true, true, true, false, false), Is.False, "not a melee stealther");
                Assert.That(RvrStealthOpener.Allows(true, true, true, true, true, true), Is.False, "already opened on this target");
                Assert.That(RvrStealthOpener.IsOpenerClass(eCharacterClass.Nightshade), Is.True);
                Assert.That(RvrStealthOpener.IsOpenerClass(eCharacterClass.Hunter), Is.False, "archers camp but have no melee opener");
                Assert.That(RvrStealthOpener.IsCampingStealther(eCharacterClass.Ranger), Is.True);
                Assert.That(RvrStealthOpener.IsCampingStealther(eCharacterClass.Minstrel), Is.False);
            });
        }

        [Test]
        public void The_opener_turn_opens_waits_steps_behind_or_lets_the_fight_go_on()
        {
            const long since = 10_000;
            Assert.Multiple(() =>
            {
                Assert.That(RvrStealthOpener.Next(false, true, false, true, false, true, 0, since, since), Is.EqualTo(RvrStealthOpener.Move.None), "seen: no opener");
                Assert.That(RvrStealthOpener.Next(true, false, false, true, false, true, 0, since, since), Is.EqualTo(RvrStealthOpener.Move.None), "not in reach yet");
                Assert.That(RvrStealthOpener.Next(true, true, true, true, false, true, 0, since, since), Is.EqualTo(RvrStealthOpener.Move.None), "opened already");
                Assert.That(RvrStealthOpener.Next(true, true, false, true, false, true, 0, since, since), Is.EqualTo(RvrStealthOpener.Move.Open));
                Assert.That(RvrStealthOpener.Next(true, true, false, true, false, true, since, since, since + 1_000), Is.EqualTo(RvrStealthOpener.Move.Wait));
                Assert.That(RvrStealthOpener.Next(true, true, false, true, false, true, since, since, since + RvrStealthOpener.QueuedMilliseconds),
                    Is.EqualTo(RvrStealthOpener.Move.None), "a queued opener that never landed is given up");
                Assert.That(RvrStealthOpener.Next(true, true, false, false, true, true, 0, since, since + 1_000), Is.EqualTo(RvrStealthOpener.Move.GetBehind));
                Assert.That(RvrStealthOpener.Next(true, true, false, false, true, true, 0, since, since + RvrStealthOpener.BehindMilliseconds),
                    Is.EqualTo(RvrStealthOpener.Move.None), "not behind in time: fight without it");
                Assert.That(RvrStealthOpener.Next(true, true, false, false, true, false, 0, since, since), Is.EqualTo(RvrStealthOpener.Move.None),
                    "a casual stealther does not bother to get behind");
            });
            int veteran = Enumerable.Range(0, 1_000).Count(k => RvrStealthOpener.GoesBehind(RvrSkillTier.Veteran, k / 1_000.0));
            int casual = Enumerable.Range(0, 1_000).Count(k => RvrStealthOpener.GoesBehind(RvrSkillTier.Casual, k / 1_000.0));
            Assert.That(veteran, Is.EqualTo(900).Within(5));
            Assert.That(casual, Is.EqualTo(400).Within(5));
        }

        [Test]
        public void A_usable_stealth_opener_scores_first_and_stealth_styles_stay_out_of_ordinary_eligibility()
        {
            var opener = new Style(new DbStyle { Name = "Backstab I", ID = 335, SpecKeyName = Specs.Critical_Strike, SpecLevelRequirement = 2,
                StealthRequirement = true, GrowthRate = 0.1 }, null);
            var chain = new Style(new DbStyle { Name = "Chain", ID = 900, SpecKeyName = Specs.Critical_Strike, SpecLevelRequirement = 10,
                OpeningRequirementType = (int)Style.eOpening.Offensive, OpeningRequirementValue = 1, GrowthRate = 1.2 }, null);
            Assert.That(BotMeleeStylePolicy.IsEligible(opener), Is.False, "only the opener hook lets it in");
            Assert.That(BotMeleeStylePolicy.Score(opener, false, false), Is.GreaterThan(BotMeleeStylePolicy.Score(chain, false, false)));
        }
    }
}
