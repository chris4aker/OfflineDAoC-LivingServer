using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Port of True165 8abbc61a8 (its M12, 2026-11-11): event wake-ups for adds, low groupmates and enemy casts.</summary>
    [TestFixture]
    public class UT_BotEventWakes
    {
        [TestCase(1000, 1000, 0, 100)]
        [TestCase(1000, 1000, 300, 70)]
        [TestCase(1000, 1000, 2000, 0)]
        [TestCase(500, 1000, -50, 50)]
        [TestCase(10, 0, 5, 100)]
        public void PredictedHealthAfterTheHit(int health, int maxHealth, int damage, int expected)
        {
            Assert.That(BotBrain.PredictedHealthPercent(health, maxHealth, damage), Is.EqualTo(expected));
        }

        [TestCase(100, 74, true)]
        [TestCase(100, 75, false)]
        [TestCase(0, 10, false)]   // a miss or a resisted spell wakes nobody
        public void LowGroupmateWakesHealers(int damage, int predicted, bool wakes)
        {
            Assert.That(BotBrain.HealWakeNeeded(damage, predicted), Is.EqualTo(wakes));
        }

        [TestCase(true, false, false, false, true)]
        [TestCase(false, false, false, false, false)]   // not a monster
        [TestCase(true, true, false, false, false)]     // the victim is fighting it: the pull, not an add
        [TestCase(true, false, true, false, false)]     // this bot's own target
        [TestCase(true, false, false, true, false)]     // the group's kill target
        public void AnAddIsAMonsterNobodyChoseToFight(bool monster, bool victimFightsIt, bool ownTarget, bool killTarget,
            bool wakes)
        {
            Assert.That(BotBrain.IsAddWake(monster, victimFightsIt, ownTarget, killTarget), Is.EqualTo(wakes));
        }

        [TestCase(false, 1000, 999, true)]
        [TestCase(true, 1000, 0, false)]
        [TestCase(true, 3999, 1000, false)]
        [TestCase(true, 4000, 1000, true)]
        public void TheSameAddWakesABotOnceEveryThreeSeconds(bool sameAttacker, long now, long last, bool allowed)
        {
            Assert.That(BotBrain.AddWakeAllowed(sameAttacker, now, last), Is.EqualTo(allowed));
        }

        [TestCase(true, false, true, true)]
        [TestCase(false, false, true, false)]   // a heal or buff is not worth interrupting from here
        [TestCase(true, true, true, false)]     // an instant cast cannot be interrupted
        [TestCase(true, false, false, false)]   // only the bots fighting the caster
        public void EnemyCastWakesTheBotsFightingTheCaster(bool harmful, bool instant, bool targetsCaster, bool wakes)
        {
            Assert.That(BotBrain.EnemyCastWakes(harmful, instant, targetsCaster), Is.EqualTo(wakes));
        }

        [Test]
        public void AnEventWakeThinksNowButNeverMoreThanEveryQuarterSecond()
        {
            BotBrain brain = new();
            long now = GameLoop.GameLoopTime;
            brain.NextThinkTick = now + 5_000;
            Assert.That(brain.WakeForEvent(), Is.True);
            Assert.That(brain.NextThinkTick, Is.EqualTo(now));
            brain.NextThinkTick = now + 5_000;
            Assert.That(brain.WakeForEvent(), Is.False, "a second wake inside 250 ms");
            Assert.That(brain.NextThinkTick, Is.EqualTo(now + 5_000));
            Assert.That(BotBrain.EventWakeMinimumMilliseconds, Is.EqualTo(250));
        }

        [Test]
        public void ACastWithoutTargetsOrSpellIsIgnored()
        {
            Assert.DoesNotThrow(() => BotBrain.NotifyEnemyCastStarted(null, null, null));
        }
    }
}
