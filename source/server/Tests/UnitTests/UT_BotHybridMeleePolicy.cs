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
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, true, spell), Is.EqualTo(expected));
            // Running.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, spell, false, null), Is.EqualTo(expected));
        }

        [Test]
        public void AnInstantWithACastTimeSpellQueuedBehindItStillStops()
        {
            Spell instant = MakeSpell("Lifedrain", 0);
            Spell slow = MakeSpell("DirectDamage", 3);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, instant, true, slow), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, slow, true, instant), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, instant, true, instant), Is.True);
        }

        [Test]
        public void SongsKeepTheirInstrumentRule()
        {
            Spell song = MakeSpell("SpeedEnhancement", 0, instrument: 1);
            Assert.That(BotHybridMeleePolicy.IsSwingSafeInstant(song), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, song, false, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, true, song), Is.False);
        }

        [Test]
        public void RangedCastersAndNoSpellActionAreUnchanged()
        {
            Spell instant = MakeSpell("Lifedrain", 0);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(false, true, false, null, true, instant), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(false, true, true, instant, false, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, null, false, null), Is.False);
        }

        [Test]
        public void UnknownRunningOrQueuedCastsStillStopTheSwing()
        {
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, true, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, null, false, null), Is.False);
            // The instant already resolved: nothing running, nothing queued.
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, false, null), Is.True);
        }
    }
}
