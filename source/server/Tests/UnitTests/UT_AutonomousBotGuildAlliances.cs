using NUnit.Framework;
using Alliances = DOL.GS.AutonomousBotGuildAlliances;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBotGuildAlliances
    {
        [Test]
        public void Plan_FoundsWithTwoFreeRvrOrRaidingGuilds_LedByTheOldestRvrGuild()
        {
            var guilds = new[]
            {
                new Alliances.Candidate("raid", BotGuildCharter.Raiding, 0, false),
                new Alliances.Candidate("social", BotGuildCharter.Social, 1, false),
                new Alliances.Candidate("rvr-b", BotGuildCharter.Rvr, 7, false),
                new Alliances.Candidate("rvr-a", BotGuildCharter.Rvr, 3, false),
            };
            (string leader, var add) = Alliances.Plan(guilds, null, 0);
            Assert.That(leader, Is.EqualTo("rvr-a"));
            Assert.That(add, Is.EqualTo(new[] { "raid", "rvr-b" }), "Social guilds stay out; oldest first.");
        }

        [Test]
        public void Plan_NeedsTwoFreeGuildsToFound()
        {
            (string leader, var add) = Alliances.Plan([new Alliances.Candidate("rvr", BotGuildCharter.Rvr, 0, false),
                new Alliances.Candidate("lvl", BotGuildCharter.Leveling, 1, false)], null, 0);
            Assert.That(leader, Is.Null);
            Assert.That(add, Is.Empty);
        }

        [Test]
        public void Plan_RaidingGuildsCanLeadWhenNoRvrGuildIsFree()
        {
            (string leader, var add) = Alliances.Plan([new Alliances.Candidate("r2", BotGuildCharter.Raiding, 5, false),
                new Alliances.Candidate("r1", BotGuildCharter.Raiding, 2, false)], null, 0);
            Assert.That(leader, Is.EqualTo("r1"));
            Assert.That(add, Is.EqualTo(new[] { "r2" }));
        }

        [Test]
        public void Plan_GrowsAnExistingAllianceUpToFour_SkippingGuildsInAlliances()
        {
            var guilds = new[]
            {
                new Alliances.Candidate("a", BotGuildCharter.Rvr, 0, true),
                new Alliances.Candidate("b", BotGuildCharter.Rvr, 1, true),
                new Alliances.Candidate("c", BotGuildCharter.Rvr, 2, false),
                new Alliances.Candidate("d", BotGuildCharter.Raiding, 3, false),
                new Alliances.Candidate("e", BotGuildCharter.Rvr, 4, false),
            };
            (string leader, var add) = Alliances.Plan(guilds, "a", 2);
            Assert.That(leader, Is.EqualTo("a"));
            Assert.That(add, Is.EqualTo(new[] { "c", "d" }));
            Assert.That(Alliances.Plan(guilds, "a", Alliances.MaximumGuildsPerAlliance).ToAdd, Is.Empty);
        }

        [Test]
        public void Plan_HandlesNothing()
        {
            Assert.That(Alliances.Plan(null, null, 0).LeaderId, Is.Null);
        }
    }
}
