using System;
using NUnit.Framework;
using GuildLines = DOL.GS.AutonomousBotGuildChatLines;
using GuildSocial = DOL.GS.AutonomousBotGuildSocial;

namespace DOL.GS.Tests
{
    /// <summary>"lf guild" from a player below bot_guild_min_level (playtest 2026-10-18: a level 4 Scout got no answer).</summary>
    [TestFixture]
    public class UT_AutonomousBotGuildTooLow
    {
        [Test]
        public void TheTooLowAnswerSaysWhenToComeBack()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                string line = GuildLines.TooLowForGuild("Wrenfield", "House Caradoc", 5, new Random(seed));
                Assert.That(line, Does.Contain("Wrenfield"));
                Assert.That(line, Does.Contain("5"));
                Assert.That(line.Length, Is.LessThanOrEqualTo(GuildLines.MaxLength));
            }
        }

        [Test]
        public void TheTooLowAnswerIsGivenOncePerTenMinutes()
        {
            const long now = 5_000_000;
            Assert.That(GuildSocial.ShouldAnswerTooLow(false, 0, now), Is.True, "First ask: answered.");
            Assert.That(GuildSocial.ShouldAnswerTooLow(true, now - 60_000, now), Is.False, "Asked again a minute later.");
            Assert.That(GuildSocial.ShouldAnswerTooLow(true, now - GuildSocial.TooLowAnswerMs, now), Is.True);
        }
    }
}
