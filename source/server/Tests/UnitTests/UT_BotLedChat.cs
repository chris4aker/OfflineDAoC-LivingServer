using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups (docs/BOT-LED-GROUPS.md, change 9): the leader's lines. Every call has corpus lines and a plain
    /// fallback; only the leader's own calls can say them (flag lead_call); lines that assume the player leads are not said
    /// in a party the bots lead (flag bot_leads).
    /// </summary>
    [TestFixture]
    public class UT_BotLedChat
    {
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

        private static IEnumerable<VC.Line> LeadLines => Corpus.Lines.Where(line => line.Topic?.StartsWith("lead_", StringComparison.Ordinal) == true);

        [Test]
        public void EveryLeaderCallHasLinesAndAFallback()
        {
            Assert.Multiple(() =>
            {
                foreach (BotLedGroups.Lead what in Enum.GetValues<BotLedGroups.Lead>())
                {
                    string topic = BotLedGroups.Topic(what);
                    string kind = what == BotLedGroups.Lead.AfkLetGo ? "tell" : "group";
                    Assert.That(LeadLines.Count(line => line.Topic == topic && line.Kind == kind), Is.GreaterThan(0), what.ToString());
                    Assert.That(BotLedGroups.Fallback(what), Is.Not.Empty, what.ToString());
                }
                Assert.That(LeadLines.Count(line => line.Topic == "lead_invite" && line.Kind == "tell"), Is.GreaterThan(0));
                Assert.That(LeadLines.Count(line => line.Topic == "lead_invite_asked" && line.Kind == "tell"), Is.GreaterThan(0));
            });
        }

        [Test]
        public void OnlyTheLeadersOwnCallsSayThem() =>
            Assert.That(LeadLines.Where(line => !line.Needs.Contains("lead_call")).Select(line => line.Text), Is.Empty);

        [Test]
        public void TheGroupCallsAreSaidOnlyInAPartyTheBotsLead() =>
            Assert.That(LeadLines.Where(line => line.Kind == "group" &&
                !(line.Needs.Contains("bot_leads") && line.Needs.Contains("in_player_group"))).Select(line => line.Text), Is.Empty);

        [TestCase("you pull great, makes this easy")]
        [TestCase("good group and a good leader, cant ask for more")]
        [TestCase("ty for the invite by the way, this is great")]
        [TestCase("you lead, i follow, we level. simple plan")]
        [TestCase("lead the way, i will follow")]
        [TestCase("your call, stay here or move on?")]
        public void LinesThatAssumeThePlayerLeadsAreNotSaidWhenTheBotsLead(string text)
        {
            VC.Line line = Corpus.Lines.Single(candidate => candidate.Kind == "group" && candidate.Text == text);
            Assert.That(line.Needs, Does.Contain("!bot_leads"));
        }

        [Test]
        public void TheFallbackFillsThePlaceTheCampAndThePlayer()
        {
            var slots = new Dictionary<string, string> { ["place"] = "Adribard's Retreat", ["camp"] = "skeletons", ["player"] = "Chris" };
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.Fill(BotLedGroups.Fallback(BotLedGroups.Lead.Welcome), slots), Is.EqualTo("welcome! meet up at Adribard's Retreat"));
                Assert.That(BotLedGroups.Fill(BotLedGroups.Fallback(BotLedGroups.Lead.Move), slots), Is.EqualTo("moving to skeletons"));
                Assert.That(BotLedGroups.Fill(BotLedGroups.Fallback(BotLedGroups.Lead.AfkHold), slots), Is.EqualTo("waiting on Chris, everyone med"));
                Assert.That(Enum.GetValues<BotLedGroups.Lead>().Select(BotLedGroups.Fallback).Select(text => BotLedGroups.Fill(text, slots)),
                    Has.None.Contains("{"));
            });
        }
    }
}
