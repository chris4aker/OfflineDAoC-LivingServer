using System.IO;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #571 step 5 (DetShaft 09baa0375, 12b401e4f): a melee bot that casts an instant spell keeps swinging.
    [TestFixture]
    public class UT_BotHybridMeleePolicy
    {
        private static Spell MakeSpell(string type, double castTime, int instrument = 0) =>
            new(new DbSpell { Type = type, Target = "Enemy", CastTime = castTime, InstrumentRequirement = instrument }, 1);

        [Test]
        public void HybridsAreMeleeBots_PureCastersAreNot()
        {
            // The combat turn passes !PrefersSpellRange as the melee flag, grouped or solo, companion or gamebot.
            foreach (bool grouped in new[] { false, true })
            foreach (bool autonomous in new[] { false, true })
            {
                Assert.That(BotBrain.PrefersSpellRange(new ClassReaver(), grouped, autonomous), Is.False);
                Assert.That(BotBrain.PrefersSpellRange(new ClassPaladin(), grouped, autonomous), Is.False);
                Assert.That(BotBrain.PrefersSpellRange(new ClassThane(), grouped, autonomous), Is.False);
                Assert.That(BotBrain.PrefersSpellRange(new ClassChampion(), grouped, autonomous), Is.False);
                Assert.That(BotBrain.PrefersSpellRange(new ClassValewalker(), grouped, autonomous), Is.False);
                Assert.That(BotBrain.PrefersSpellRange(new ClassWizard(), grouped, autonomous), Is.True);
                Assert.That(BotBrain.PrefersSpellRange(new ClassSorcerer(), grouped, autonomous), Is.True);
            }
        }

        [TestCase("Lifedrain", 0, true)]
        [TestCase("DamageOverTime", 0, true)]
        [TestCase("DirectDamage", 0, true)]
        [TestCase("Taunt", 0, true)]
        [TestCase("DirectDamage", 3, false)]
        [TestCase("Taunt", 2, false)]
        public void OnlyInstantSpellsLetTheSwingGoOn(string type, double castTime, bool expected)
        {
            Spell spell = MakeSpell(type, castTime);
            // Queued and not yet started.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, false, null, true, spell), Is.EqualTo(expected));
            // Running.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, true, spell, false, null), Is.EqualTo(expected));
        }

        [Test]
        public void AnInstantWithACastTimeSpellQueuedBehindItStillStops()
        {
            Spell instant = MakeSpell("Lifedrain", 0);
            Spell slow = MakeSpell("DirectDamage", 3);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, true, instant, true, slow), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, true, slow, true, instant), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, true, instant, true, instant), Is.True);
        }

        [Test]
        public void SongsKeepTheirInstrumentRule()
        {
            Spell song = MakeSpell("SpeedEnhancement", 0, instrument: 1);
            Assert.That(BotHybridMeleePolicy.IsSwingSafeInstant(song), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, true, song, false, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, false, null, true, song), Is.False);
        }

        [Test]
        public void RangedCastersAndNoSpellActionAreUnchanged()
        {
            Spell instant = MakeSpell("Lifedrain", 0);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(false, false, false, true, false, null, true, instant), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(false, false, false, true, true, instant, false, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, false, false, null, false, null), Is.False);
        }

        [Test]
        public void UnknownRunningOrQueuedCastsStillStopTheSwing()
        {
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, false, null, true, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, true, null, false, null), Is.False);
            // The instant already resolved: nothing running, nothing queued.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, true, false, null, false, null), Is.True);
        }

        [Test]
        public void TheMinstrelKeepsItsOwnNarrowerRule()
        {
            // Grouped and autonomous Minstrels are melee by PrefersSpellRange; their charm upkeep and other non-damage
            // instants still stop the swing (MinstrelBotCombatPolicy), so this rule never answers for them.
            Assert.That(BotBrain.PrefersSpellRange(new ClassMinstrel(), true, false), Is.False);
            Spell charm = MakeSpell("Charm", 0);
            Spell instantDamage = MakeSpell("DirectDamage", 0);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, true, false, null, true, charm), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, true, false, null, true, instantDamage), Is.False);
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, false, null, true, charm), Is.False);
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, false, null, true, instantDamage), Is.True);
        }

        [TestCase("Mesmerize")]
        [TestCase("Stun")]
        [TestCase("SpeedDecrease")]
        public void InstantCrowdControlOnAPlayerLikeEnemyStopsTheSwing(string type)
        {
            Spell control = MakeSpell(type, 0);
            Assume.That(BotControlSpells.Kind(control), Is.Not.EqualTo(BotControlSpells.ControlKind.None));
            // PvP: queued or running, the swing stops so the bot doesn't break its own control.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, true, true, false, null, true, control), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, true, true, true, control, false, null), Is.False);
            // An instant damage spell on the same player still keeps the swing.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, true, true, false, null, true,
                MakeSpell("Lifedrain", 0)), Is.True);
            // Against a monster ControlAllowedOnTarget already decides which control is cast; this rule doesn't add a stop.
            Assert.That(BotHybridMeleePolicy.IsSwingSafe(control, false), Is.True);
        }

        [Test]
        public void BothCallSitesUseTheHybridRule()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "repository root");
            string brain = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs"))
                .Replace("\r\n", "\n");
            Assert.Multiple(() =>
            {
                // After a priority taunt: the stop only when the hybrid rule says no.
                Assert.That(brain, Does.Contain(
                    "if (!BotHybridMeleePolicy.ContinueMeleeAfterInstant(!rangedCaster, IsMinstrelBot,\n" +
                    "                            BotPvpCrowdControl.PlayerLike(Body.TargetObject as GameLiving), true, Body.IsCasting,"));
                // The combat turn: the hybrid rule beside the Savage, bow-draw and Minstrel rules.
                Assert.That(brain, Does.Contain(
                    "!rangedCaster, IsMinstrelBot, BotPvpCrowdControl.PlayerLike(Body.TargetObject as GameLiving), spellAction,"));
                Assert.That(brain, Does.Contain(
                    "if (spellAction && !continueSavageMelee && !preserveRangedDraw && !continueMinstrelMelee &&\n" +
                    "                    !continueHybridMelee)"));
            });
        }
    }
}
