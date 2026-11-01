using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Owner 2026-10-29: rally arrivals extend staging; Dawn of Light's BAF ranges; bots leave controlled adds alone.</summary>
    [TestFixture]
    public sealed class UT_RaidStagingAndCrowdControl
    {
        private const long Minute = 60_000;

        [Test]
        public void AnArrivalNearTheEndGivesTenMoreMinutesUpToThirty() => Assert.Multiple(() =>
        {
            long deadline = 100 * Minute;
            Assert.That(RealmRaidRecruitmentPolicy.ExtendedStagingDeadline(deadline, 0), Is.EqualTo(deadline), "nobody arrived");
            Assert.That(RealmRaidRecruitmentPolicy.ExtendedStagingDeadline(deadline, 50 * Minute), Is.EqualTo(deadline),
                "an arrival with more than 10 minutes left changes nothing");
            Assert.That(RealmRaidRecruitmentPolicy.ExtendedStagingDeadline(deadline, 95 * Minute), Is.EqualTo(105 * Minute));
            Assert.That(RealmRaidRecruitmentPolicy.ExtendedStagingDeadline(deadline, 125 * Minute), Is.EqualTo(130 * Minute),
                "never past 30 minutes beyond the deadline");
        });

        [Test]
        public void BringAFriendUsesDawnOfLightRanges() => Assert.Multiple(() =>
        {
            Assert.That(BafFriends.CloseRadius(500), Is.EqualTo(200));
            Assert.That(BafFriends.Radius(500, false), Is.EqualTo(200));
            Assert.That(BafFriends.Radius(500, true), Is.EqualTo(250), "a solo dungeon pull reaches 250");
            Assert.That(BafFriends.Radius(500, true, 4), Is.EqualTo(700));
            Assert.That(BafFriends.Radius(500, true, 8), Is.EqualTo(BafFriends.DungeonReinforcementRadius));
            Assert.That(BafFriends.SameFight(true, 50_000, 10_000), Is.True, "still fighting: no new round of adds");
            Assert.That(BafFriends.SameFight(false, 50_000, 10_000), Is.False);
            Assert.That(BafFriends.SameFight(true, 200_000, 10_000), Is.False);
            Assert.That(BafFriends.CloseRadius(0), Is.EqualTo(0), "a monster with no aggro range brings nobody outdoors");
        });

        [Test]
        public void ACampIsCrowdedWhenItsBotsOutnumberHalfItsMonsters() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampCompetition.Crowded(2, 1), Is.False, "two bots always fit");
            Assert.That(AutonomousCampCompetition.Crowded(5, 10), Is.False);
            Assert.That(AutonomousCampCompetition.Crowded(6, 10), Is.True);
            Assert.That(AutonomousCampCompetition.Pressure(6, 10), Is.LessThan(AutonomousCampCompetition.Pressure(6, 4)));
        });

        [Test]
        public void AControlledMonsterWaitsOnlyWhileSomethingElseIsInTheFight() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.PassOverControlled(true, true), Is.True, "a mezzed add waits while another fights");
            Assert.That(BotBrain.PassOverControlled(true, false), Is.False, "all controlled: break one and go on");
            Assert.That(BotBrain.PassOverControlled(false, true), Is.False);
        });
    }
}
