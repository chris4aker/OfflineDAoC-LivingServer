using System;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Living Server pacing (owner 2026-10-28): the signature moments come early in a play session.</summary>
    [TestFixture]
    public class UT_LivingServerPacing
    {
        [Test]
        public void ASessionStartsAtTheFirstLoginAndAfterHalfAnHourAway()
        {
            long now = 10_000_000;
            Assert.That(AutonomousLivingWorld.IsNewSession(true, false, 0, now), Is.True, "first player of the realm since the server started");
            Assert.That(AutonomousLivingWorld.IsNewSession(true, true, 0, now), Is.False, "still playing");
            Assert.That(AutonomousLivingWorld.IsNewSession(true, true, now - 5 * 60_000, now), Is.False, "a zone change or a quick relog");
            Assert.That(AutonomousLivingWorld.IsNewSession(true, true, now - AutonomousLivingWorld.SessionGapMs, now), Is.True, "back after half an hour");
            Assert.That(AutonomousLivingWorld.IsNewSession(false, false, 0, now), Is.False, "nobody on");
        }

        [Test]
        public void StorylinesComeOftenEnoughForAFewHoursAWeek()
        {
            Assert.That(AutonomousBotStorylines.RealmRest, Is.EqualTo(TimeSpan.FromMinutes(45)));
            Assert.That(AutonomousBotStorylines.EpisodeGapMin + AutonomousBotStorylines.EpisodeGapSpread, Is.EqualTo(TimeSpan.FromHours(10)));
            Assert.That(AutonomousBotStorylines.NewArcGap, Is.EqualTo(TimeSpan.FromHours(2)));
        }

        [Test]
        public void AGoldSpammerLastsLongEnoughToBeNoticed()
        {
            Assert.That(AutonomousGoldSpammers.AdsMin, Is.EqualTo(5));
            Assert.That(AutonomousGoldSpammers.AdsMin + AutonomousGoldSpammers.AdsSpread - 1, Is.EqualTo(10));
            Assert.That(AutonomousGoldSpammers.FirstWaveMinMs + AutonomousGoldSpammers.FirstWaveSpreadMs, Is.LessThanOrEqualTo(30 * 60_000),
                "the first wave comes within the session's first half hour");
        }
    }
}
