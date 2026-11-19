using System.Numerics;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, M6: a party calls RUN from the fight board while everyone is alive, a solo bot bails
    /// out of a fight it is losing badly, a companion healer survives to raise its player, and a solo caster takes one
    /// straight step back after a root or snare (owner O3).
    /// </summary>
    [TestFixture]
    public class UT_VeteranGroupRetreat
    {
        [Test]
        public void APartyCallsRunWhenTheFightIsLost() => Assert.Multiple(() =>
        {
            // (a) four loose monsters, one tank, no mez ready, the tank going down
            Assert.That(BotGroupRetreat.ShouldRun(4, 4, 1, 2, 60, 40, false), Is.True);
            Assert.That(BotGroupRetreat.ShouldRun(4, 4, 1, 2, 60, 40, true), Is.False, "a mezzer can still sleep them");
            Assert.That(BotGroupRetreat.ShouldRun(4, 4, 1, 2, 60, 90, false), Is.False, "the tank holds and the healers have power");
            Assert.That(BotGroupRetreat.ShouldRun(3, 3, 2, 2, 60, 40, false), Is.False, "two tanks can hold three");
            // (b) healers out of power and the tank low
            Assert.That(BotGroupRetreat.ShouldRun(1, 1, 1, 2, 5, 30, false), Is.True);
            Assert.That(BotGroupRetreat.ShouldRun(1, 1, 1, 2, 5, 70, false), Is.False);
            // (c) every healer dead, two monsters on the group, the tank hurt
            Assert.That(BotGroupRetreat.ShouldRun(2, 2, 1, 0, 0, 50, false), Is.True);
            Assert.That(BotGroupRetreat.ShouldRun(1, 1, 1, 0, 0, 50, false), Is.False, "one monster: finish it");
            Assert.That(BotGroupRetreat.ShouldRun(0, 0, 0, 0, 0, 0, false), Is.False, "no fight");
            Assert.That(BotGroupRetreat.CheckMilliseconds, Is.EqualTo(2_000));
            Assert.That(BotGroupRetreat.RetreatMilliseconds, Is.EqualTo(20_000));
        });

        [Test]
        public void ThePartyRunsBackTheWayItCame()
        {
            Vector3 fight = new(10_000, 10_000, 100);
            Vector3 home = new(20_000, 10_000, 300);
            Vector3 point = BotGroupRetreat.RetreatPoint(fight, home, fight);
            Assert.Multiple(() =>
            {
                Assert.That(point.X, Is.EqualTo(13_000).Within(1), "3,000 units toward the rendezvous");
                Assert.That(point.Y, Is.EqualTo(10_000).Within(1));
                Assert.That(BotGroupRetreat.RetreatPoint(fight, new Vector3(12_000, 10_000, 0), fight),
                    Is.EqualTo(new Vector3(12_000, 10_000, 0)), "a closer rendezvous: all the way");
                Vector3 away = BotGroupRetreat.RetreatPoint(fight, null, new Vector3(10_000, 9_000, 100));
                Assert.That(away.Y, Is.EqualTo(13_000).Within(1), "no home: straight away from the monsters");
                Vector3 tooClose = BotGroupRetreat.RetreatPoint(fight, new Vector3(10_500, 10_000, 0), new Vector3(9_000, 10_000, 0));
                Assert.That(tooClose.X, Is.EqualTo(13_000).Within(1), "a rendezvous beside the fight is no retreat");
            });
        }

        [Test]
        public void ASoloBotBailsOnlyWhenLosingBadlyWithNothingLeft() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRetreat.SoloShouldBail(25, 70, 1, false, false), Is.True);
            Assert.That(BotGroupRetreat.SoloShouldBail(25, 40, 1, false, false), Is.False, "the monster is nearly dead too");
            Assert.That(BotGroupRetreat.SoloShouldBail(45, 90, 2, false, false), Is.True, "two attackers under half health");
            Assert.That(BotGroupRetreat.SoloShouldBail(45, 90, 1, false, false), Is.False);
            Assert.That(BotGroupRetreat.SoloShouldBail(25, 70, 2, true, false), Is.False, "a heal is ready");
            Assert.That(BotGroupRetreat.SoloShouldBail(25, 70, 2, false, true), Is.False, "crowd control is ready");
        });

        [Test]
        public void ACompanionHealerSurvivesToRaise() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRetreat.CompanionHealerSurvives(true, true, 2, 0), Is.True, "nobody left to hold them");
            Assert.That(BotGroupRetreat.CompanionHealerSurvives(true, true, 3, 2), Is.True, "outnumbered");
            Assert.That(BotGroupRetreat.CompanionHealerSurvives(true, true, 1, 2), Is.False, "the others can win it");
            Assert.That(BotGroupRetreat.CompanionHealerSurvives(false, true, 3, 0), Is.False, "the player lives");
            Assert.That(BotGroupRetreat.CompanionHealerSurvives(true, false, 3, 0), Is.False, "cannot raise");
            Assert.That(BotGroupRetreat.CompanionHealerSurvives(true, true, 0, 0), Is.False, "the fight is over");
        });

        [Test]
        public void ASoloCasterStepsBackOnceAfterARoot() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRetreat.CasterStepsBack(true, true, true, 300, 20_000), Is.True);
            Assert.That(BotGroupRetreat.CasterStepsBack(false, true, true, 300, 60_000), Is.False, "never grouped");
            Assert.That(BotGroupRetreat.CasterStepsBack(true, false, true, 300, 60_000), Is.False, "casters only");
            Assert.That(BotGroupRetreat.CasterStepsBack(true, true, false, 300, 60_000), Is.False, "only after a root or snare");
            Assert.That(BotGroupRetreat.CasterStepsBack(true, true, true, 900, 60_000), Is.False, "already at range");
            Assert.That(BotGroupRetreat.CasterStepsBack(true, true, true, 300, 19_999), Is.False, "once per 20 s");
            Assert.That(BotGroupRetreat.StepBackStandOff(1_500), Is.EqualTo(1_200));
            Assert.That(BotGroupRetreat.StepBackStandOff(700), Is.EqualTo(600));
        });

    }
}
