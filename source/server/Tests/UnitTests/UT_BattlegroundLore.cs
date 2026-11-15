using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS.LoreChannels;
using NUnit.Framework;
using Tales = DOL.GS.AutonomousTales;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The battlegrounds' lore (owner, 2026-11-15: "We need full lore for the battlegrounds written"): the proving fields as
    /// one backstory told three ways by the loremasters, a lore file per field with each realm's take, and a storyteller's
    /// legend per field (data/chat/battleground_lore.lines). Tests only: none of it has been heard on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundLore
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

        private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        // Each field's legend and the realms that tell it: Abermenai every realm, the others the realm whose tradition names the keep.
        [TestCase("abermenai", eRealm.Albion, eRealm.Midgard, eRealm.Hibernia)]
        [TestCase("thidranki", eRealm.Midgard)]
        [TestCase("murdaigean", eRealm.Hibernia)]
        [TestCase("caledon", eRealm.Albion)]
        public void EachFieldHasItsLegend(string loreId, params eRealm[] tellers)
        {
            Assert.That(Tales.IsLegend(loreId), Is.True);
            VC.Scene[] tales = Corpus.Scenes.Where(s => Tales.IsTale(s) && Tales.LoreIdOf(s.Topic) == loreId).ToArray();
            Assert.That(tales, Has.Length.EqualTo(1), loreId);
            foreach (eRealm realm in Realms)
            {
                Assert.That(VC.RealmFits(tales[0].Realm, realm), Is.EqualTo(tellers.Contains(realm)), $"{loreId} told in {realm}");
                Assert.That(Tales.RaidIdFor(loreId, realm), Is.Null, "a legend never leads to a raid");
                if (tellers.Contains(realm))
                    Assert.That(LoreLegends.RumoursAfter(Corpus, loreId, realm, Tales.LinesOf(tales[0])), Is.Not.Empty, $"{loreId} {realm}");
            }
            Assert.That(Corpus.Scenes.Any(s => Tales.IsFireside(s) && Tales.LoreIdOf(s.Topic) == loreId), Is.False);
            // The legend's id is the field's own lore file, so a player standing in the field hears it first.
            Assert.That(LoreBible.Current.Locations.Any(l => l.Id == loreId), Is.True);
            Assert.That(LoreBible.Current.BossById(loreId), Is.Null);
        }

        [TestCase(eRealm.Albion, "tell the one about the battle of the trees", "caledon")]
        [TestCase(eRealm.Albion, "anyone tell the abermenai story?", "abermenai")]
        [TestCase(eRealm.Midgard, "tell the thidrand story", "thidranki")]
        [TestCase(eRealm.Hibernia, "tell us the one about the knots in murd", "murdaigean")]
        public void APlayerCanAskForAFieldsTale(eRealm realm, string message, string loreId)
        {
            Assert.That(Tales.IsRequest(message), Is.True);
            var tales = Corpus.Scenes.Where(s => Tales.IsTale(s) && VC.RealmFits(s.Realm, realm)).Select(t => Tales.LoreIdOf(t.Topic)).Distinct();
            Assert.That(Tales.RequestedLore(message, tales.Select(id => (id, Tales.RequestNames(id, LoreBible.Current)))), Is.EqualTo(loreId));
        }

        [Test]
        public void AFieldsTaleComesFirstForAPlayerStandingInIt()
        {
            var told = new HashSet<string>(StringComparer.Ordinal) { "thidranki", "golestandt", "atlantis" };
            Assert.That(Tales.LocalTale("thidranki", [], told), Is.EqualTo("thidranki"));
            Assert.That(Tales.LocalTale("dartmoor", ["golestandt"], told), Is.EqualTo("golestandt"), "a boss's place keeps its boss");
            Assert.That(Tales.LocalTale("caledon", [], told), Is.Null, "the realm has no tale of that field");
            Assert.That(Tales.LocalTale("krondon", [], new HashSet<string> { "krondon" }), Is.Null, "a place that is no legend");
            Assert.That(Tales.LocalTale("thidranki", [], null), Is.Null);
        }

        [Test]
        public void EachFieldIsItsOwnPlaceAndTheProvingFieldsHaveNone()
        {
            foreach ((ushort region, string id) in new[] { ((ushort)253, "abermenai"), ((ushort)252, "thidranki"), ((ushort)251, "murdaigean"), ((ushort)250, "caledon") })
                Assert.That(LoreEntryRules.LocationAt(LoreBible.Current, region, 0)?.Id, Is.EqualTo(id), $"region {region}");
            LoreBible.Location fields = LoreBible.Current.Locations.Single(l => l.Id == "proving-fields");
            Assert.That(fields.Regions, Is.Empty, "the backstory claims no region");
        }

        // The three truths of "why only the young": a charge after Camlann, the hazel law, Macha's geis.
        [TestCase(LoreRealm.Albion, "Camlann", "Their captains learn the second")]
        [TestCase(LoreRealm.Midgard, "hazel", "nobody has found a way around")]
        [TestCase(LoreRealm.Hibernia, "Macha", "a geis must be kept")]
        public void EachLoremasterTellsWhyOnlyTheYoungInItsOwnVoice(int realm, string root, string ending)
        {
            var dialogue = new LoremasterDialogue(EmbeddedLoreSource.Load(), realm, "Loremaster");
            string young = dialogue.Resolve("only the young")?.Text;
            Assert.That(young, Does.Contain(root).And.EndWith(ending + "."), young);
            foreach (string keyword in new[] { "proving fields", "only the young", "the fallen", "masters at the gate", "renegade chieftains",
                         "abermenai", "thidranki", "murdaigean", "caledonia", "caledon" })
            {
                LoremasterReply reply = dialogue.Resolve(keyword);
                Assert.That(reply, Is.Not.Null, keyword);
                foreach (string link in LoremasterDialogue.Links(reply.Text))
                    Assert.That(dialogue.Knows(link), Is.True, $"{keyword}: [{link}] leads nowhere");
            }
            // Every field's intro leads back to the backstory (its "related" link), and the backstory to every field.
            foreach (string field in new[] { "abermenai", "thidranki", "murdaigean", "caledonia" })
            {
                Assert.That(LoremasterDialogue.Links(dialogue.Resolve(field).Text).Select(LoreSourceExtensions.Key), Does.Contain("theprovingfields"), field);
                Assert.That(LoremasterDialogue.Links(dialogue.Resolve("proving fields").Text).Select(LoreSourceExtensions.Key), Does.Contain(field));
            }
        }

        [Test]
        public void TheThreeTruthsAreThreeTexts()
        {
            string[] answers = new[] { LoreRealm.Albion, LoreRealm.Midgard, LoreRealm.Hibernia }
                .Select(realm => new LoremasterDialogue(EmbeddedLoreSource.Load(), realm, "Loremaster"))
                .SelectMany(d => new[] { d.Resolve("only the young").Text, d.Resolve("the fallen").Text, d.Resolve("masters at the gate").Text })
                .ToArray();
            Assert.That(answers.Distinct().Count(), Is.EqualTo(9));
        }
    }
}
