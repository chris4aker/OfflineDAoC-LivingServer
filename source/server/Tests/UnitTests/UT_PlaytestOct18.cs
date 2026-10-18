using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Chat = DOL.GS.AutonomousBotChatCoordinator;
using GuildLines = DOL.GS.AutonomousBotGuildChatLines;
using GuildSocial = DOL.GS.AutonomousBotGuildSocial;
using Living = DOL.GS.AutonomousLivingServer;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>Scout playtest 2026-10-18 (0.36-se.2, level 1 to 5, three hours): bot chat and log fixes.</summary>
    [TestFixture]
    public class UT_PlaytestOct18
    {
        #region A bot's closing line follows a reply

        [Test]
        public void AClosingLineNeedsAReplyThatWasSaid()
        {
            // Renaward: "just took on 4 orc clanshacker at once, still standing" then Renaward: "Fair enough. Safe travels."
            Assert.That(Chat.ClosingFollowsAReply(0), Is.False);
            Assert.That(Chat.ClosingFollowsAReply(1), Is.True);
            Assert.That(Chat.ClosingFollowsAReply(3), Is.True);
        }

        #endregion

        #region "Where are you?" by /send

        [TestCase("where are you?", true)]
        [TestCase("where are you? i can come to you", true)]
        [TestCase("Where r u", true)]
        [TestCase("wru", true)]
        [TestCase("where you at", true)]
        [TestCase("whats your loc?", true)]
        [TestCase("where are you hunting tonight", true)]
        [TestCase("where is the scout trainer in camelot?", false)]
        [TestCase("where do i train?", false)]
        [TestCase("hey, want to group?", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AsksWhereYouAreOnlyAboutTheBot(string message, bool expected) =>
            Assert.That(Chat.AsksWhereYouAre(message), Is.EqualTo(expected));

        [Test]
        public void TheWhereAnswerNamesTheZone()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                string far = Chat.WhereText("Camelot Hills", false, 7, "Scout", new Random(seed));
                Assert.That(far, Does.Contain("Camelot Hills"));
                string near = Chat.WhereText("Camelot Hills", true, 7, "Scout", new Random(seed));
                Assert.That(near, Does.Contain("Camelot Hills"));
                Assert.That(near, Does.Contain("7 scout"));
            }
        }

        [Test]
        public void AJoinRequestStillGetsTheJoinAnswer() =>
            // "where are you? want to group?" is a join request first (its answer already says where the bot is).
            Assert.That(Chat.AsksToJoinGroup("where are you? want to group?"), Is.True);

        #endregion

        #region "lf guild" below the guild level

        [Test]
        public void TheTooLowAnswerSaysWhenToComeBack()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                string line = GuildLines.TooLowForGuild("Wrenfield", "House Caradoc", 5, new Random(seed));
                Assert.That(line, Does.Contain("Wrenfield"));
                Assert.That(line, Does.Contain("5"));
                Assert.That(line.Length, Is.LessThanOrEqualTo(VC.MaxLength));
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

        #endregion

        #region Grouped bots don't ask for company

        private static VC _corpus;

        private static VC Corpus => _corpus ??= Load();

        private static VC Load()
        {
            var assembly = typeof(VC).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        [TestCase("who is up for a group just for the company?")]
        [TestCase("i miss my old group, they all went to bed")]
        [TestCase("anyone want to hang out in {capital} for a bit?")]
        public void LookingForCompanyNeedsABotWithNoGroup(string text)
        {
            // Linaine, grouped with the player: "who is up for a group just for the company?"
            VC.Line line = Corpus.Lines.Single(l => l.Text == text);
            Assert.That(line.Needs, Does.Contain("solo"));
        }

        [Test]
        public void EveryLfgLineSaysWhetherTheBotHasAGroup() =>
            // "lf group" lines need a bot with no group; "lfm" lines need a bot in one.
            // (The "invite" topic, offers to a player the bot remembers, is neither.)
            Assert.That(Corpus.Lines.Where(l => l.Kind == "lfg" && l.Topic != "invite").Select(l => l.Needs.Contains("solo") || l.Needs.Contains("in_group")),
                Has.All.True);

        #endregion

        #region Logs

        [Test]
        public void TheMeetupDeathLineSaysHowLongTheBotReallyWaited()
        {
            // Both events of the playtest said waitedSeconds=900, 5 and 7 minutes into the meetup.
            var attendance = new AutonomousRendezvousAttendance();
            const long start = 1_000_000;
            attendance.ReachedDeathLimit(7, 15, start);
            attendance.Observe(7, start, false);
            long waited = attendance.WaitedMilliseconds(7, start + 90_000);
            Assert.That(attendance.ReachedDeathLimit(7, 18, start + 90_000), Is.True);
            Assert.That(waited, Is.EqualTo(90_000), "Read before the limit is applied.");
            Assert.That(attendance.WaitedMilliseconds(7, start + 90_000), Is.EqualTo(AutonomousRendezvousAttendance.TimeoutMilliseconds),
                "Read after, it is the full wait - which is why the log line reads it first.");
        }

        [TestCase(false, false, false, false, false)]
        [TestCase(true, false, false, false, true)]
        [TestCase(false, true, false, false, true)]
        [TestCase(false, false, true, false, true)]
        [TestCase(false, false, false, true, true)]
        public void ARezAskIsLoggedOnlyWhenAPlayerCouldAnswer(bool rezzer, bool rezClass, bool hears, bool playerGroup, bool expected) =>
            Assert.That(Living.RezAskWorthLogging(rezzer, rezClass, hears, playerGroup), Is.EqualTo(expected));

        #endregion
    }
}
