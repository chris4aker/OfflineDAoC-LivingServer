using System;
using System.Linq;
using DOL.Database;
using NUnit.Framework;
using Lines = DOL.GS.AutonomousBotGuildChatLines;
using Social = DOL.GS.AutonomousBotGuildSocial;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBotGuildSocial
    {
        private static readonly string[] OutOfEra = ["world of warcraft", "discord", "twitch", "youtube", "lol wut", "lmao", "covid"];

        [Test]
        public void Lines_AreFilledShortAndInEra()
        {
            string[] lines = Lines.Samples("Chris", "Aldwin", "Avalon Wardens", 24, "Salisbury Plains").ToArray();
            Assert.That(lines, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (string line in lines)
                {
                    Assert.That(line, Is.Not.Null.And.Not.Empty);
                    Assert.That(line.Length, Is.LessThanOrEqualTo(Lines.MaxLength), line);
                    Assert.That(line, Does.Not.Contain("{").And.Not.Contain("}"), line);
                    foreach (string word in OutOfEra)
                        Assert.That(line.ToLowerInvariant(), Does.Not.Contain(word), line);
                }
            });
        }

        [Test]
        public void Lines_UseTheirInputs()
        {
            var random = new Random(3);
            Assert.That(Lines.Welcome("Chris", "Avalon Wardens", random), Does.Contain("Chris"));
            Assert.That(Enumerable.Range(0, 40).Select(seed => Lines.Recruit("Avalon Wardens", BotGuildCharter.Rvr, 20, new Random(seed))),
                Has.All.Contain("Avalon Wardens"));
            Assert.That(Enumerable.Range(0, 40).Select(seed => Lines.OwnDing(31, new Random(seed))), Has.All.Contain("31"));
        }

        [Test]
        public void Idle_LinesDifferByCharter()
        {
            string[] Sample(BotGuildCharter charter) => Enumerable.Range(0, 200)
                .Select(seed => Lines.Idle(charter, "Thane", 30, "Myrkwood Forest", new Random(seed))).Distinct().ToArray();
            string[] rvr = Sample(BotGuildCharter.Rvr);
            string[] crafting = Sample(BotGuildCharter.Crafting);
            Assert.That(rvr.Except(crafting), Is.Not.Empty);
            Assert.That(crafting.Except(rvr), Is.Not.Empty);
            Assert.That(rvr.Length, Is.GreaterThan(6), "Enough variety that a guild doesn't repeat itself quickly.");
        }

        [TestCase("ty!", eAutonomousChatIntent.Unknown, Lines.ReplyKind.Thanks)]
        [TestCase("thanks for the rez", eAutonomousChatIntent.Unknown, Lines.ReplyKind.Thanks)]
        [TestCase("gn all", eAutonomousChatIntent.Unknown, Lines.ReplyKind.Farewell)]
        [TestCase("heading to bed", eAutonomousChatIntent.Unknown, Lines.ReplyKind.Farewell)]
        [TestCase("hi guild", eAutonomousChatIntent.Greeting, Lines.ReplyKind.Greeting)]
        [TestCase("anyone want to group?", eAutonomousChatIntent.Grouping, Lines.ReplyKind.Grouping)]
        [TestCase("where do i buy arrows?", eAutonomousChatIntent.Unknown, Lines.ReplyKind.Question)]
        [TestCase("", eAutonomousChatIntent.Unknown, Lines.ReplyKind.None)]
        public void Classify_PicksTheReplyKind(string message, eAutonomousChatIntent intent, Lines.ReplyKind expected)
        {
            Assert.That(Lines.Classify(message, intent), Is.EqualTo(expected));
        }

        [TestCase("lf guild", false, true)]
        [TestCase("any guilds recruiting a lvl 20 cleric?", false, true)]
        [TestCase("Looking for a guild, anyone?", false, true)]
        [TestCase("me", false, false)]
        [TestCase("me", true, true)]
        [TestCase("interested", true, true)]
        [TestCase("guild buffs are nice", true, false)]
        [TestCase("where is the guild registrar", false, false)]
        public void AsksForGuild_NeedsAClearRequest(string message, bool recruitOpen, bool expected)
        {
            Assert.That(Lines.AsksForGuild(message, recruitOpen), Is.EqualTo(expected));
        }

        [Test]
        public void IdleInterval_SocialTalksMostAndBusyGuildsMoreOften()
        {
            Assert.That(Social.IdleIntervalMs(BotGuildCharter.Social, 4), Is.LessThan(Social.IdleIntervalMs(BotGuildCharter.Crafting, 4)));
            Assert.That(Social.IdleIntervalMs(BotGuildCharter.Rvr, 16), Is.LessThan(Social.IdleIntervalMs(BotGuildCharter.Rvr, 4)));
            Assert.That(Social.IdleIntervalMs(BotGuildCharter.Rvr, 1000), Is.EqualTo(Social.IdleIntervalMs(BotGuildCharter.Rvr, 4) / 3),
                "Never more than three times as often.");
            Assert.That(Social.IdleIntervalMs(BotGuildCharter.Leveling, 0), Is.EqualTo(Social.IdleIntervalMs(BotGuildCharter.Leveling, 1)));
        }

        [Test]
        public void ResponderCount_NeverExceedsWhoIsAvailable()
        {
            foreach (Lines.ReplyKind kind in Enum.GetValues<Lines.ReplyKind>())
            {
                foreach (double roll in new[] { 0.0, 0.3, 0.5, 0.9, 0.999 })
                {
                    Assert.That(Social.ResponderCount(kind, 0, roll), Is.Zero);
                    Assert.That(Social.ResponderCount(kind, 1, roll), Is.LessThanOrEqualTo(1));
                    Assert.That(Social.ResponderCount(kind, 10, roll), Is.InRange(0, 3));
                }
            }
            Assert.That(Social.ResponderCount(Lines.ReplyKind.None, 10, 0.0), Is.Zero);
            Assert.That(Social.ResponderCount(Lines.ReplyKind.Greeting, 10, 0.1), Is.EqualTo(3));
        }

        [Test]
        public void GuildmateRank_OnlySameGuildWhenEnabled()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotGuilds.GuildmateRank(true, "g1", "g1"), Is.Zero);
                Assert.That(AutonomousBotGuilds.GuildmateRank(true, "g1", "g2"), Is.EqualTo(1));
                Assert.That(AutonomousBotGuilds.GuildmateRank(true, null, null), Is.EqualTo(1), "Two guildless bots aren't guildmates.");
                Assert.That(AutonomousBotGuilds.GuildmateRank(true, "", ""), Is.EqualTo(1));
                Assert.That(AutonomousBotGuilds.GuildmateRank(false, "g1", "g1"), Is.EqualTo(1), "Off keeps today's ordering.");
            });
        }

        [Test]
        public void SharedGuildId_NeedsTwoMembers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Social.SharedGuildId(["a", "b", null, "c"]), Is.Null);
                Assert.That(Social.SharedGuildId(["a", "b", "b", null]), Is.EqualTo("b"));
                Assert.That(Social.SharedGuildId(["a", "a", "b", "b", "b"]), Is.EqualTo("b"));
                Assert.That(Social.SharedGuildId(["b", "b", "a", "a"]), Is.EqualTo("a"), "Ties go to the lowest id.");
                Assert.That(Social.SharedGuildId(null), Is.Null);
            });
        }

        [Test]
        public void ChooseGuild_PrefersTheOpenRecruiterThenFitThenSize()
        {
            var guilds = new[]
            {
                new Social.GuildChoice("a", BotGuildCharter.Rvr, 30, true),
                new Social.GuildChoice("b", BotGuildCharter.Leveling, 12, true),
                new Social.GuildChoice("c", BotGuildCharter.Leveling, 20, true),
                new Social.GuildChoice("d", BotGuildCharter.Social, 40, false),
            };
            Assert.Multiple(() =>
            {
                Assert.That(Social.ChooseGuildForPlayer(guilds, 15, "a"), Is.EqualTo("a"), "The guild that just recruited answers.");
                Assert.That(Social.ChooseGuildForPlayer(guilds, 15, "d"), Is.EqualTo("c"), "A recruiter with nobody online can't answer.");
                Assert.That(Social.ChooseGuildForPlayer(guilds, 15, null), Is.EqualTo("c"), "Leveling fits 15; the larger one wins.");
                Assert.That(Social.ChooseGuildForPlayer(guilds, 45, null), Is.EqualTo("a"), "RvR fits 45.");
                Assert.That(Social.ChooseGuildForPlayer([], 15, null), Is.Null);
                Assert.That(Social.ChooseGuildForPlayer([guilds[3]], 15, null), Is.Null);
            });
        }

        [Test]
        public void AStartingGuildDoesNotStopBotGuildsRecruitingYou()
        {
            // In-game check 2026-10-17: every new character is in its realm's starting guild (Clan Cotswold),
            // and bot guilds skipped anyone with a guild, so "lf guild" was never answered.
            var starting = new Guild(new DbGuild { GuildName = "Clan Cotswold", GuildID = "start", Realm = 1, IsStartingGuild = true });
            var real = new Guild(new DbGuild { GuildName = "Avalon Wardens", GuildID = "real", Realm = 1 });
            Assert.Multiple(() =>
            {
                Assert.That(Social.CountsAsGuildless(null), Is.True);
                Assert.That(Social.CountsAsGuildless(starting), Is.True);
                Assert.That(Social.CountsAsGuildless(real), Is.False);
            });
        }
    }
}
