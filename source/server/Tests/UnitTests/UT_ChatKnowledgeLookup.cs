using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Realism 2e (#482): the grounded knowledge lookup (AutonomousChatLookup). The lines are the realism audit's own examples
    /// (docs/realism-audit-1.2.3/questions.md, findings 1, 3-6, 9 and 10, the wrong-fact table), not the bench's holdout. Each
    /// question is read once, by its type, and only that type's providers answer it; an answer of another subject is not said.
    /// Tests only.
    /// </summary>
    [TestFixture]
    public class UT_ChatKnowledgeLookup
    {
        private static AutonomousChatKnowledge.KnowledgePorts Ports(eRealm realm, string cls, int level) => new()
        {
            Realm = realm, Level = level, ClassName = cls, Random = new Random(7), Corpus = AutonomousBotVoices.Corpus, Bible = LoreBible.Current,
        };

        private static string Answer(string question, eRealm realm = eRealm.Albion, string cls = "Armsman", int level = 20) =>
            AutonomousChatKnowledge.TryAnswer(Ports(realm, cls, level), question, out string answer) ? answer : null;

        [TestCase("wher do i trian", "where do i train")]
        [TestCase("whre is jordhiem", "where is jordheim")]
        [TestCase("ne1 no where 2 xp @ 20", "anyone no where to xp at 20")]
        [TestCase("any1 kno wher the wepon merchent is", "anyone know where the weapon merchant is")]
        [TestCase("hwo do i get to tnn", "how do i get to tnn")]
        [TestCase("is 2 too low?", "is 2 too low?")]
        public void TyposAndShorthandAreFoldedBeforeReading(string typed, string read) =>
            Assert.That(AutonomousChatLookup.Fold(typed), Is.EqualTo(read));

        [TestCase("can i salvage dropped armor", "salvage")]
        [TestCase("can i spellcraft my own armor", "spellcraft")]
        [TestCase("lost some con, healer npc wants gold. worth it?", "conloss")]
        [TestCase("how do i stop getting group invites", "living")]
        [TestCase("what does blue con mean", "con")]
        [TestCase("how do i send a tell", "ui")]
        [TestCase("i died, now what", "death")]
        [TestCase("how do i respec", "respec")]
        [TestCase("how do i hire a companion", "living")]
        [TestCase("trainer wont train me past 5, whats going on", "classtree")]
        [TestCase("what can a naturalist turn into", "classtree")]
        [TestCase("what do relics do", "relic")]
        [TestCase("how do i form a guild", "guild")]
        [TestCase("whats in krondon?", null)]
        public void AQuestionIsReadByItsType(string question, string topic) =>
            Assert.That(AutonomousChatLookup.TopicOf(question), Is.EqualTo(topic));

        [TestCase("can i salvage dropped armor", @"/salvage", @"reinforced|cloth|chain|plate")]
        [TestCase("can i spellcraft my own armor", @"crafted", @"cloth|wear")]
        [TestCase("lost some con, healer npc wants gold. worth it?", @"\bcon\b|worth|hp", @"pie|stat")]
        [TestCase("how do i get my constitution back", @"healer", null)]
        [TestCase("how do i stop getting group invites", @"/invites off", @"/invite their|/invite name")]
        [TestCase("what does blue con mean", @"below|easy|xp", null)]
        [TestCase("do greys give xp", @"no xp|nothing", null)]
        [TestCase("how do i send a tell", @"/send", null)]
        [TestCase("i died, now what", @"/release", null)]
        [TestCase("how do i hire a companion", @"/hire", null)]
        [TestCase("how many companions can i have", @"seven", null)]
        [TestCase("whats the realm exchange", @"/tc", null)]
        [TestCase("what is /faction", @"realm", @"npc|keep score")]
        [TestCase("how many rps to hit rr3?", @"61,750", @"\bgz\b|nice")]
        [TestCase("what do relics give", @"10%", null)]
        [TestCase("how do i respec", @"/respec", null)]
        [TestCase("wtf is a bub", @"bubble|tenth", null)]
        [TestCase("what does otw mean", @"on the way", null)]
        public void TheAuditsMissesGetTheirFact(string question, string must, string mustNot)
        {
            string answer = Answer(question);
            Assert.That(answer, Is.Not.Null, question);
            Assert.That(Regex.IsMatch(answer, must, RegexOptions.IgnoreCase), Is.True, question + " -> " + answer);
            if (mustNot != null)
                Assert.That(Regex.IsMatch(answer, mustNot, RegexOptions.IgnoreCase), Is.False, question + " -> " + answer);
        }

        [Test]
        public void TheClassChoiceIsReadInThePlayersWords()
        {
            Assert.That(Answer("im lvl 5 and my trainer says i have to join a guild?? which guild", eRealm.Albion, "Fighter", 5), Does.Match("(?i)armsman"));
            Assert.That(Answer("trainer wont train me past 5, whats going on", eRealm.Midgard, "Viking", 5), Does.Match("(?i)thane|warrior"));
            Assert.That(Answer("what can a mystic turn into", eRealm.Midgard, "Mystic", 4), Does.Match("(?i)runemaster"));
            Assert.That(Answer("can a mage be a necromancer", eRealm.Albion, "Mage", 5), Does.Match("(?i)disciple|cant|can't"));
        }

        [Test]
        public void DeathRulesFollowTheServersLevels()
        {
            Assert.That(Answer("do i lose anything when i die at lvl 3", eRealm.Midgard, "Seer", 3), Does.Match(@"\b" + AutonomousChatLookup.XpLossLevel + @"\b"));
            Assert.That(Answer("do i lose xp dying in rvr", eRealm.Hibernia, "Blademaster", 37), Does.Match("(?i)no|nope"));
        }

        [Test]
        public void ADungeonAFewLevelsUpIsNotFine()
        {
            string answer = Answer("can i do muire tomb at 11", eRealm.Hibernia, "Druid", 11);
            Assert.That(answer, Does.Not.Match("(?i)fine"));
            Assert.That(answer, Does.Match("13"));
        }

        [Test]
        public void WhereToHuntLeavesOutTheZoneThePlayerIsDoneWith()
        {
            string answer = Answer("16 here, bored of silvermine. where now?", eRealm.Hibernia, "Hero", 16);
            Assert.That(answer, Is.Not.Null);
            Assert.That(answer, Does.Not.Match("(?i)silvermine"));
        }

        [Test]
        public void EveryGuideEntryNamesItsSourceAndItsTopicIsRead()
        {
            var topics = AutonomousChatLookup.TopicIds.ToHashSet();
            Assert.That(AutonomousChatLookup.Entries, Is.Not.Empty);
            foreach (var entry in AutonomousChatLookup.Entries)
            {
                Assert.That(entry.Source, Is.Not.Null.And.Not.Empty, entry.Id);
                Assert.That(topics, Does.Contain(entry.Topic), entry.Id);
                Assert.That(entry.Lines, Is.Not.Empty, entry.Id);
                foreach (string line in entry.Lines)
                    Assert.That(line, Does.Not.Match("(?i)sluagh"), entry.Id);
            }
        }

        [Test]
        public void EveryAnswerComesWithItsSource()
        {
            Assert.That(Answer("how do i respec"), Is.Not.Null);
            Assert.That(AutonomousChatKnowledge.LastFact, Is.Not.Null);
            Assert.That(AutonomousChatKnowledge.LastFact.Source, Does.Contain("respec"));
            Assert.That(AutonomousChatKnowledge.LastFact.Topic, Is.EqualTo("respec"));
        }
    }
}
