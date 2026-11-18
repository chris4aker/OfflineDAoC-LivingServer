using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Living Server 1.2, the bot chat audit (2026-11-18), voice layer: the writing styles on functional lines, laughs, length,
    /// typos on slot values, the enemy's three shapes, alarm head counts, story beats, seasons and Caledon. Tests on the code and
    /// the corpus only; none of this has been seen in game yet.
    /// </summary>
    [TestFixture]
    public class UT_ChatAuditVoice
    {
        private static VC _corpus;
        private static VC Corpus => _corpus ??= Load();

        private static string Resource(string suffix)
        {
            var assembly = typeof(VC).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static VC Load() => VC.Parse(Resource("chat.corpus.json"), Resource("chat.voices.json"));

        private static VC.Voice Voice(string name) => Corpus.Voices.Single(voice => voice.Name == name);

        /// <summary>A style that always adds a laugh: the chatty one with its chance at 1.</summary>
        private static VC.Voice AlwaysLaughs => Voice("chatty") with { EmoteChance = 1.0 };

        // ---- DESIGN-012: functional lines keep their shape ---------------------------------------------------------------

        [TestCase("wts 97% chain hauberk /send")]
        [TestCase("wtb fine alloy great hammer pst")]
        [TestCase("lf healer for spindelhalla, 2 spots")]
        [TestCase("pc on a runed staff?")]
        [TestCase("inc 8 mids at the mile gate, healer, warrior, skald")]
        public void AProperWriterTypesATradeOrGroupLineAsTyped(string line)
        {
            Assert.That(VC.Style(line, Voice("proper"), new Random(1)), Is.EqualTo(line));
        }

        [Test]
        public void AProperWriterExpandsAtWordBoundariesAndNoCapitalAfterAnEllipsis()
        {
            VC.Voice proper = Voice("proper");
            Assert.That(VC.Style("where r u?", proper, new Random(1)), Is.EqualTo("Where are you?"));
            Assert.That(VC.Style("hmm... not sure about that one", proper, new Random(1)), Is.EqualTo("hmm... not sure about that one"),
                "starts with a chat word; and never \"... Not\"");
            Assert.That(VC.Style("well... not sure about that one", proper, new Random(1)), Does.Not.Contain("... N"));
            Assert.That(VC.Style("gz!! you earned it", proper, new Random(1)), Is.EqualTo("gz!! you earned it"));
        }

        // ---- DESIGN-013, CORPUS-B2-010/032, LOG-022: no laugh where it does not belong --------------------------------------

        [TestCase("haha yeah that was close lol", null)]
        [TestCase("nice one :)", null)]
        [TestCase("wts ring of the bear, 5g", null)]
        [TestCase("SUPER CHEAP GOLD!!! 5 MINUTE DELIVERY!!!", null)]
        [TestCase("cheap gold at the usual place", "spam")]
        [TestCase("8 mids at the bridge", "alarm")]
        [TestCase("anyone able to rez me at the bridge", "rez_ask")]
        [TestCase("i bid you good day, traveller", "roleplay")]
        public void NoLaughIsAddedToTheseLines(string line, string kind)
        {
            for (int seed = 0; seed < 20; seed++)
                Assert.That(VC.Style(line, AlwaysLaughs, new Random(seed), out _, kind), Is.EqualTo(line), kind ?? line);
            Assert.That(VC.TakesEmote(kind, line), Is.False);
        }

        [Test]
        public void AnOrdinaryLineStillTakesTheChattyLaugh()
        {
            string styled = VC.Style("long walk back to the camp", AlwaysLaughs, new Random(3), out _, "ambient");
            Assert.That(styled, Does.StartWith("long walk back to the camp "));
            Assert.That(VC.HasLaugh(styled[26..]), Is.True);
        }

        [Test]
        public void TheTerseStyleKeepsListCommasShoutsAndSentenceBreaks()
        {
            VC.Voice terse = Voice("terse");
            Assert.That(VC.Style("inc 8 mids at Caer Benowyc, healer, warrior, skald", terse, new Random(1)),
                Is.EqualTo("inc 8 mids at Caer Benowyc, healer, warrior, skald"), "a list keeps its commas");
            Assert.That(VC.Style("WANT PLATINUM? CLICK HERE NOW!!!", terse, new Random(1)), Is.EqualTo("WANT PLATINUM? CLICK HERE NOW!!!"));
            // CORPUS-B1-028 (owner's PC 11/18 00:19:36: "i do both zerg when i want points").
            Assert.That(VC.Style("i do both. zerg when i want points", terse, new Random(1)), Is.EqualTo("i do both, zerg when i want points"));
            Assert.That(VC.Style("thanks, see you tonight.", terse, new Random(1)), Is.EqualTo("ty see u tonite"), "the old terse rule stays");
        }

        [Test]
        public void ASpammersAdIsSaidAsWritten()
        {
            var line = new VC.Line("s1", "spam", "general", [], "*", [], 1, 50, [], "", "", 1, "CHEAP GOLD!!! w w w . {site} . N3T") ;
            VC corpus = Corpus.WithLines([line]);
            var speaker = new VC.Speaker(-77, "Xqzzt", eRealm.Albion, "", 1, BotPersonalityType.Leveler, new HashSet<string>());
            for (int seed = 0; seed < 40; seed++)
            {
                var request = new VC.Request("spam", speaker, new Dictionary<string, string> { ["site"] = "M M O G 0 L D" }, null, DateTime.UtcNow,
                    new Random(seed)) { Filter = l => l.Id == "s1" };
                Assert.That(corpus.TryPick(request, out VC.Picked picked), Is.True);
                Assert.That(picked.Text, Is.EqualTo("CHEAP GOLD!!! w w w . M M O G 0 L D . N3T"));
            }
        }

        [Test]
        public void AStorylinesLoversAndARageQuitSayTheirBeatsWithNoLaugh()
        {
            var story = new VC.Scene("romance_4", "story_romance", "*", 1, 50, new Dictionary<string, VC.RoleSpec>(), []);
            Assert.That(AutonomousBotDebates.NoLaughBeat(story, "A"), Is.True);
            Assert.That(AutonomousBotDebates.NoLaughBeat(story, "B"), Is.True);
            Assert.That(AutonomousBotDebates.NoLaughBeat(story, "C"), Is.False, "the onlookers may laugh");
            var quit = new VC.Scene("dunce_1", "dunce", "*", 1, 50, new Dictionary<string, VC.RoleSpec>(), []) { Exit = "A" };
            Assert.That(AutonomousBotDebates.NoLaughBeat(quit, "D"), Is.True);
            var debate = new VC.Scene("d", "leveling_pace", "*", 1, 50, new Dictionary<string, VC.RoleSpec>(), []);
            Assert.That(AutonomousBotDebates.NoLaughBeat(debate, "A"), Is.False);
        }

        // ---- DESIGN-014: one length, never a cut in mid-word ------------------------------------------------------------------

        [Test]
        public void ALongLineIsCutAtASpaceNeverInMidWord()
        {
            string longLine = string.Join(' ', Enumerable.Repeat("something", 20));
            foreach (VC.Voice voice in Corpus.Voices)
            {
                string styled = VC.Style(longLine, voice, new Random(2));
                Assert.That(styled.Length, Is.LessThanOrEqualTo(VC.MaxLength), voice.Name);
                Assert.That(styled.Split(' ').All(word => word.TrimEnd('.').Length == 0 || word.TrimEnd('.').Equals("something", StringComparison.OrdinalIgnoreCase) || VC.HasLaugh(word)), Is.True, styled);
            }
            Assert.That(VC.CutAtWord("abc def ghi", 9), Is.EqualTo("abc..."));
        }

        [Test]
        public void ALineTooLongOnceFilledIsSkippedForTheNext()
        {
            var tooLong = new VC.Line("long1", "ambient", "t", [], "*", [], 1, 50, [], "", "", 1000, "selling my {item}, ask me about it");
            var shortOne = new VC.Line("short1", "ambient", "t", [], "*", [], 1, 50, [], "", "", 0.001, "selling stuff, ask");
            VC corpus = Corpus.WithLines([tooLong, shortOne]);
            var speaker = new VC.Speaker(5, "Bot", eRealm.Albion, "Armsman", 30, BotPersonalityType.Leveler, new HashSet<string>());
            var slots = new Dictionary<string, string> { ["item"] = new string('x', 150) };
            var request = new VC.Request("ambient", speaker, slots, null, DateTime.UtcNow, new Random(1)) { Filter = l => l.Topic == "t" };
            Assert.That(corpus.TryPick(request, out VC.Picked picked), Is.True);
            Assert.That(picked.Line.Id, Is.EqualTo("short1"));
        }

        // ---- DESIGN-015: no typo or leetspeak in a slot value --------------------------------------------------------------

        [Test]
        public void TyposAndHabitsLeaveSlotValuesAlone()
        {
            VC.Voice sloppy = Voice("typo_prone") with { TypoChance = 1.0 };
            VC corpus = Corpus.WithLines([new VC.Line("typo1", "ambient", "typo", [], "*", [], 1, 50, [], "", "", 1,
                "anyone selling {item} tonight? the {class} trainer wants one")]);
            var slots = new Dictionary<string, string> { ["item"] = "fine alloy great hammer", ["class"] = "necromancer" };
            int typos = 0;
            for (int id = 1; id < 400; id++)
            {
                var speaker = new VC.Speaker(id, "Bot", eRealm.Albion, "Armsman", 30, BotPersonalityType.Leveler, new HashSet<string>());
                var request = new VC.Request("ambient", speaker, slots, null, DateTime.UtcNow, new Random(id)) { Filter = l => l.Id == "typo1" };
                Assert.That(corpus.TryPick(request, out VC.Picked picked), Is.True);
                Assert.That(picked.Text, Does.Contain("fine alloy great hammer"));
                Assert.That(picked.Text, Does.Contain("necromancer"));
                // The masked style itself, with a typo-prone writer: the slip lands outside the values.
                string masked = VC.Style("anyone selling  tonight? the  trainer wants one", sloppy, new Random(id));
                if (masked != "anyone selling  tonight? the  trainer wants one")
                    typos++;
                Assert.That(VC.Unmask(masked, ["fine alloy great hammer", "necromancer"]), Does.Contain("fine alloy great hammer").And.Contain("necromancer"));
            }
            Assert.That(typos, Is.GreaterThan(0), "the typo-prone writer still makes typos, just not in names");
        }

        // ---- TEXT-018: {enemy} is the people, {enemy_mod} the word before a noun, {enemy_realm} the realm -------------------------

        [Test]
        public void TheEnemyHasThreeShapes()
        {
            Assert.That(AutonomousBotVoices.EnemySlots(eRealm.Hibernia), Is.EqualTo(("hibs", "hib", "Hibernia")));
            Assert.That(AutonomousBotVoices.EnemySlots(eRealm.Midgard), Is.EqualTo(("mids", "mid", "Midgard")));
            var news = AutonomousBotVoices.WithEnemyShapes(new Dictionary<string, string> { ["enemy"] = "Albion", ["keep"] = "Dun Bolg" });
            Assert.That(news["enemy"], Is.EqualTo("Albion"), "the news lines' own {enemy} stays until they move to {enemy_realm}");
            Assert.That(news["enemy_mod"], Is.EqualTo("alb"));
            Assert.That(news["enemy_realm"], Is.EqualTo("Albion"));
            var all = new Dictionary<string, string> { ["enemy"] = "mids", ["enemy_mod"] = "mid", ["enemy_realm"] = "Midgard" };
            AutonomousBotVoices.MergeSlots(all, new Dictionary<string, string> { ["enemy"] = "hibs" });
            Assert.That(all["enemy_realm"], Is.EqualTo("Hibernia"), "a caller's enemy sets the realm too");
            Assert.That(all["enemy_mod"], Is.EqualTo("hib"));
            foreach (var shared in Enumerable.Range(0, 20).Select(seed => AutonomousBotVoices.SharedSlots(eRealm.Albion, 30, new Random(seed))))
            {
                Assert.That(shared["enemy"], Is.AnyOf("mids", "hibs"));
                Assert.That(AutonomousBotVoices.RealmFromWord(shared["enemy"]), Is.EqualTo(AutonomousBotVoices.RealmFromWord(shared["enemy_realm"])));
            }
        }

        [Test]
        public void NoCorpusLineUsesTheEnemyAsARealmOrBeforeANoun()
        {
            var bad = new System.Text.RegularExpressions.Regex(@"\{enemy\} (is|has|was|zerg|group|stealthers?|archer|8man|healers)\b");
            // Kinds whose lines are E3's (alarm, camp) or mostly E3's; the news, deed and rumour lines are reported to their owner.
            string[] hits = Corpus.Lines.Where(line => bad.IsMatch(line.Text) && line.Kind is "ambient" or "rvr" or "alarm" or "camp")
                .Select(line => line.Text).Distinct().ToArray();
            Assert.That(hits, Is.Empty);
        }

        // ---- TEXT-020, LOG-017, CORPUS-A-006: an alarm says what the head count is -------------------------------------------

        [Test]
        public void ThreeEnemiesAreNotAZerg()
        {
            Assert.That(AutonomousLivingServer.AlarmFlags(3), Is.EquivalentTo(new[] { "in_rvr", "few" }));
            Assert.That(AutonomousLivingServer.AlarmFlags(8), Is.EquivalentTo(new[] { "in_rvr", "many" }));
            Assert.That(AutonomousLivingServer.AlarmFlags(20), Is.EquivalentTo(new[] { "in_rvr", "many", "zerg" }));
            VC.Line[] alarms = Corpus.Lines.Where(line => line.Kind == "alarm").ToArray();
            Assert.That(alarms.Where(line => line.Text.Contains("zerg")), Is.Not.Empty);
            Assert.That(alarms.Where(line => line.Text.Contains("zerg")).All(line => line.Needs.Contains("zerg")), Is.True);
            Assert.That(alarms.Where(line => line.Text.Contains("small group")).All(line => line.Needs.Contains("few")), Is.True);
            Assert.That(alarms.Where(line => line.Text.Contains("or more")).All(line => line.Needs.Contains("many") || line.Needs.Contains("zerg")), Is.True);
            var three = new HashSet<string>(AutonomousLivingServer.AlarmFlags(3));
            Assert.That(alarms.Where(line => VC.NeedsMet(line.Needs, three)).Select(line => line.Text),
                Has.None.Contains("zerg").And.None.Contains("or more"));
        }

        // ---- the rest -----------------------------------------------------------------------------------------------------------

        [TestCase(12, "winter")]
        [TestCase(2, "winter")]
        [TestCase(3, "spring")]
        [TestCase(7, "summer")]
        [TestCase(11, "autumn")]
        public void TheSeasonComesFromThePcsCalendar(int month, string season)
        {
            Assert.That(VC.SeasonFlag(new DateTime(2026, month, 15)), Is.EqualTo(season));
        }

        [Test]
        public void CaledonIsCaledonia()
        {
            // CORPUS-A-019: the world calls the zone "Caledon" (2,656 times in the owner's logs); the corpus says caledonia.
            Assert.That(AutonomousChatPlaces.KeysAt("Caledon"), Does.Contain("caledonia"));
        }

        [Test]
        public void FunctionalLinesAreKnown()
        {
            Assert.That(VC.IsFunctional("wts ring of the bear"), Is.True);
            Assert.That(VC.IsFunctional("lf2m for spindel"), Is.True);
            Assert.That(VC.IsFunctional("inc 6 hibs at the bridge"), Is.True);
            Assert.That(VC.IsFunctional("pc on a staff?"), Is.True);
            Assert.That(VC.IsFunctional("the pcs of old"), Is.False);
            Assert.That(VC.IsFunctional("need potions, will pay"), Is.False);
        }

        [Test]
        public void AGeneratedLineInATidyWritersStyle()
        {
            // DESIGN-043: field talk passes the style (AutonomousBotVoices.StyleText); a functional line stays as typed.
            VC.Voice proper = Voice("proper");
            Assert.That(AutonomousBotVoices.StyleText(proper, 0, "need potions, anyone selling", new Random(1)), Is.EqualTo("Need potions, anyone selling."));
            Assert.That(AutonomousBotVoices.StyleText(proper, 0, "wtb potions pst", new Random(1)), Is.EqualTo("wtb potions pst"));
            Assert.That(AutonomousBotVoices.StyleText(Voice("typo_prone") with { TypoChance = 1 }, 0, "camping the spindelhalla entrance",
                new Random(1), protect: ["spindelhalla"]), Does.Contain("spindelhalla"));
        }

        [Test]
        public void TheStyleCasesMatchThePythonPort()
        {
            // tools/bot-voices/tests/style_cases.json is run through voice_style.py by test_voice_style.py; the same cases here.
            string file = null;
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null && file == null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "tools", "bot-voices", "tests", "style_cases.json");
                if (File.Exists(candidate))
                    file = candidate;
            }
            if (file == null)
                Assert.Ignore("tools/bot-voices is not beside the tests");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            int count = 0;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                string text = item[0].GetString(), voice = item[1].GetString(), kind = item[2].ValueKind == System.Text.Json.JsonValueKind.Null ? null : item[2].GetString();
                string expected = item[3].GetString();
                Assert.That(VC.Style(text, Voice(voice), new Random(1), out _, kind), Is.EqualTo(expected), $"{voice}: {text}");
                count++;
            }
            Assert.That(count, Is.GreaterThan(10));
        }
    }
}
