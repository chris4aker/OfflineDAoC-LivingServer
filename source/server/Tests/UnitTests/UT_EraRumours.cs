using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.LoreChannels;
using NUnit.Framework;
using Tales = DOL.GS.AutonomousTales;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Trials of Atlantis and Catacombs as rumours only (owner, 2026-11-15, "Yes agreed on lore"): the storyteller's legend of
    /// the glass that spoke, a /faction debate, rare place-bound musings, the answers when a player asks, and the loremasters'
    /// words. Tests only: none of this has been seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_EraRumours
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

        private static readonly Regex EraWord = new(@"\b(atlantis|catacombs)\b", RegexOptions.IgnoreCase);
        private static readonly Regex NeverEvenAsRumour = new(@"artifact|master ?level|\bml\d*\b|champion level|\bcl\d*\b|\btoa\b", RegexOptions.IgnoreCase);
        private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        [Test]
        public void OnlyRumourTaggedLinesNameAtlantisOrCatacombs()
        {
            int rumours = 0;
            foreach (VC.Line line in Corpus.Lines)
            {
                if (EraWord.IsMatch(line.Text))
                    Assert.That(LoreLegends.IsEraRumour(line), Is.True, $"{line.Id} ({line.Topic}): {line.Text}");
                if (LoreLegends.IsEraRumour(line))
                {
                    rumours++;
                    Assert.That(NeverEvenAsRumour.IsMatch(line.Text), Is.False, line.Text);
                }
            }
            foreach (VC.Scene scene in Corpus.Scenes)
                foreach (string text in scene.Beats.Where(b => !b.IsPause).SelectMany(b => b.Variants))
                {
                    if (EraWord.IsMatch(text))
                        Assert.That(scene.Topic, Is.EqualTo(LoreLegends.AtlantisTopic).Or.EqualTo(LoreLegends.BelowTopic), $"{scene.Id}: {text}");
                    Assert.That(NeverEvenAsRumour.IsMatch(text) && scene.Topic?.StartsWith("rumour_", StringComparison.Ordinal) == true, Is.False, text);
                }
            Assert.That(rumours, Is.InRange(6, 20), "rare: a handful of lines, not a theme");
        }

        [Test]
        public void TheRumoursAreRareMusingsSaidOnlyWhereTheyFit()
        {
            VC.Line[] ambient = Corpus.Lines.Where(l => LoreLegends.IsEraRumour(l) && l.Kind == "ambient").ToArray();
            Assert.That(ambient, Is.Not.Empty);
            foreach (VC.Line line in ambient)
            {
                // A musing: only now and then as a /faction opener, never in /say or /broadcast (AutonomousChatFiller).
                Assert.That(AutonomousChatFiller.IsMusing(line), Is.True, line.Text);
                Assert.That(AutonomousChatFiller.AmbientAllowed(line, false, new Random(1)), Is.False, $"not in /say: {line.Text}");
                Assert.That(line.Needs.Any(n => n.StartsWith("at:", StringComparison.Ordinal)), Is.True, $"said only at its place: {line.Text}");
                Assert.That(line.Weight, Is.LessThan(1.0), line.Text);
            }
            // Of 400 /faction openers drawn, about 5% may be a musing at all.
            var random = new Random(7);
            int allowed = Enumerable.Range(0, 400).Count(_ => AutonomousChatFiller.AmbientAllowed(ambient[0], true, random));
            Assert.That(allowed, Is.InRange(5, 40));
        }

        [Test]
        public void TheLegendIsToldByTheStorytellerWithNoRaidAndNoFireside()
        {
            VC.Scene[] legend = Corpus.Scenes.Where(s => Tales.IsTale(s) && Tales.LoreIdOf(s.Topic) == "atlantis").ToArray();
            Assert.That(legend, Has.Length.EqualTo(1));
            foreach (eRealm realm in Realms)
            {
                Assert.That(VC.RealmFits(legend[0].Realm, realm), Is.True, realm.ToString());
                Assert.That(Tales.RaidIdFor("atlantis", realm), Is.Null);
            }
            Assert.That(Corpus.Scenes.Any(s => Tales.IsFireside(s) && Tales.LoreIdOf(s.Topic) == "atlantis"), Is.False);
            // Told with restraint: the legend never names the place; the listeners and the sailors do.
            Assert.That(Tales.LinesOf(legend[0]).Any(line => EraWord.IsMatch(line)), Is.False);
            Assert.That(Tales.IsLegend("atlantis"), Is.True);
            Assert.That(Tales.IsLegend("golestandt"), Is.False);
        }

        [TestCase("tell the one about atlantis")]
        [TestCase("can someone tell the glass ball story")]
        [TestCase("tell us about the city under the sea")]
        public void APlayerCanAskForTheLegend(string message)
        {
            Assert.That(Tales.IsRequest(message), Is.True);
            var tales = Corpus.Scenes.Where(Tales.IsTale).Select(t => Tales.LoreIdOf(t.Topic)).Distinct();
            Assert.That(Tales.RequestedLore(message, tales.Select(id => (id, Tales.RequestNames(id, LoreBible.Current)))), Is.EqualTo("atlantis"));
        }

        [TestCase("what is atlantis?", eRealm.Albion, "atlantis")]
        [TestCase("anyone heard of atlantis", eRealm.Midgard, "atlantis")]
        [TestCase("is there really a city under the sea?", eRealm.Hibernia, "atlantis")]
        [TestCase("is there anything under camelot?", eRealm.Albion, "below")]
        [TestCase("whats beneath jordheim", eRealm.Midgard, "below")]
        [TestCase("what is under tnn?", eRealm.Hibernia, "below")]
        [TestCase("whats under jordheim?", eRealm.Albion, null)]
        [TestCase("where is the catacombs of cardova?", eRealm.Albion, null)]
        [TestCase("lfg tomb of mithra", eRealm.Albion, null)]
        [TestCase("atlantis", eRealm.Albion, null)]
        public void AQuestionIsReadForItsSubject(string text, eRealm realm, string subject) =>
            Assert.That(LoreLegends.Subject(text, realm), Is.EqualTo(subject), text);

        [TestCase(eRealm.Albion, "gothwaite", "camelot")]
        [TestCase(eRealm.Midgard, "aegirhamn", "jordheim")]
        [TestCase(eRealm.Hibernia, "domnann", "tnn")]
        public void ABotAnswersInItsRealmsVoice(eRealm realm, string harbour, string capital)
        {
            Assert.That(LoreLegends.TryAnswer(Corpus, "what is atlantis?", realm, new Random(3), out string sea), Is.True);
            Assert.That(sea, Does.Contain(harbour));
            Assert.That(LoreLegends.TryAnswer(Corpus, "is there anything under the city?", realm, new Random(3), out string below), Is.True);
            Assert.That(below, Does.Contain(capital));
            Assert.That(LoreLegends.TryAnswer(Corpus, "where do i train?", realm, new Random(3), out _), Is.False);
        }

        [TestCase(LoreRealm.Albion, "what is atlantis?", "Caer Gothwaite")]
        [TestCase(LoreRealm.Midgard, "Atlantis", "Aegirhamn")]
        [TestCase(LoreRealm.Hibernia, "tell me about atlantis", "Domnann")]
        [TestCase(LoreRealm.Albion, "is there anything under camelot?", "Inconnu")]
        [TestCase(LoreRealm.Midgard, "what lies beneath Jordheim", "kobold")]
        [TestCase(LoreRealm.Hibernia, "Under Tir na Nog", "Shar")]
        public void EachLoremasterAnswersInItsOwnVoice(int realm, string asked, string expected)
        {
            var dialogue = new LoremasterDialogue(EmbeddedLoreSource.Load(), realm, "Loremaster");
            LoremasterReply reply = dialogue.Resolve(asked);
            Assert.That(reply, Is.Not.Null, asked);
            Assert.That(reply.Text, Does.Contain(expected));
            Assert.That(NeverEvenAsRumour.IsMatch(reply.Text), Is.False, reply.Text);
            foreach (string link in LoremasterDialogue.Links(reply.Text))
                Assert.That(dialogue.Knows(link), Is.True, $"[{link}] leads nowhere");
        }

        [Test]
        public void TheSeaIsHearsayInEveryRealm()
        {
            foreach (int realm in new[] { LoreRealm.Albion, LoreRealm.Midgard, LoreRealm.Hibernia })
            {
                string text = new LoremasterDialogue(EmbeddedLoreSource.Load(), realm, "Loremaster").Resolve("atlantis").Text;
                Assert.That(text, Does.Match(@"[Nn]obody has sailed there|[Nn]o ship of Midgard has sailed there|[Nn]one of them has rowed out"), text);
                Assert.That(text, Does.Contain("glass that spoke"), "it points the curious at the storyteller");
            }
        }

        /// <summary>
        /// The Heretic is a Catacombs class (2004): the realm histories never present one, or any other Catacombs class, as a
        /// fact. Arawn's worship is heresy to the Church, and whether any acolyte has gone over to him is cellar talk (owner's
        /// era rules, 2026-11-15). "Heretic Elam" and "the Heretical Hermit" are 2003 Albion NPCs in the location files, not the
        /// class, so only the histories are read here. Valkyrie and warlock are older words (Midgard's dead are passed by the
        /// valkyries), so only the coined class names are banned.
        /// </summary>
        [Test]
        public void TheHistoriesNameNoCatacombsClass()
        {
            var catacombsClass = new Regex(@"\b(heretics?|bainshees?|vampiirs?)\b", RegexOptions.IgnoreCase);
            LoreHistoryText[] histories = EmbeddedLoreSource.Load().Histories().ToArray();
            Assert.That(histories, Is.Not.Empty);
            foreach (LoreHistoryText history in histories)
            {
                var texts = history.Eras.SelectMany(e => new[] { e.Summary, e.Text }.Concat(e.Events.SelectMany(v => new[] { v.Name, v.Text })))
                    .Concat(history.Factions.SelectMany(f => new[] { f.Name, f.Summary, f.Text }))
                    .Append(history.Intro).Where(t => !string.IsNullOrEmpty(t));
                foreach (string text in texts)
                    Assert.That(catacombsClass.IsMatch(text), Is.False, $"{history.Id}: {text}");
            }
            string arawn = new LoremasterDialogue(EmbeddedLoreSource.Load(), LoreRealm.Albion, "Loremaster").Resolve("arawn").Text;
            Assert.That(arawn, Does.Contain("cellar talk"), arawn);
        }

        [TestCase("what is atlantis?", "atlantis")]
        [TestCase("Is there anything beneath Camelot?", "under camelot")]
        [TestCase("so, tell me about the Dream Trees", "dream trees")]
        [TestCase("Krondon", null)]
        [TestCase("places", null)]
        public void AQuestionInWordsGivesItsKeyword(string text, string keyword) =>
            Assert.That(LoremasterDialogue.AskedKeyword(text), Is.EqualTo(keyword));
    }
}
