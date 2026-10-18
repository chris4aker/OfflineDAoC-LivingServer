using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Debates = DOL.GS.AutonomousBotDebates;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBotVoices
    {
        // Slots the server fills (AutonomousBotVoices.SlotsFor and SharedSlots).
        private static readonly string[] KnownSlots =
            ["me", "class", "level", "realm", "zone", "monster", "item", "count", "q", "keep", "frontier", "enemy", "capital", "dungeon", "camp", "monsters", "destination"];

        // Slots that need bot memory of the player; general callers never fill them, so those lines are skipped
        // unless the Living Server memory or rez code passes them in.
        private static readonly string[] MemorySlots = ["player", "place", "player_class"];

        // Slots only the Living Server event that says the line fills (grats, firsts, alarms, nemesis).
        private static readonly string[] EventSlots = ["target", "what", "size", "classes", "killer", "killer_class"];

        private static readonly string[] OutOfEra =
            ["world of warcraft", "discord", "twitch", "youtube", "lmao", "covid", "smh", "tbh", "ngl", "yeet"];

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

        private static Dictionary<string, string> FullSlots(string q = "") => new(StringComparer.Ordinal)
        {
            ["me"] = "Aldwin", ["class"] = "armsman", ["level"] = "20", ["realm"] = "Albion", ["zone"] = "Black Mountains South",
            ["monster"] = "bwca", ["item"] = "Bloodied Sword", ["count"] = "3", ["q"] = q, ["keep"] = "Caer Benowyc",
            ["frontier"] = "Pennine Mountains", ["enemy"] = "Midgard", ["capital"] = "Camelot", ["dungeon"] = "Tomb of Mithra",
            ["camp"] = "Tomb of Mithra",
        };

        private static VC.Speaker Speaker(eRealm realm = eRealm.Albion, int level = 20, BotPersonalityType type = BotPersonalityType.Leveler,
            string className = "Armsman", long id = 101, params string[] flags) =>
            new(id, "Aldwin", realm, className, level, type, new HashSet<string>(flags, StringComparer.Ordinal));

        private static IEnumerable<string> AllTexts() =>
            Corpus.Lines.Select(line => line.Text).Concat(Corpus.Scenes.SelectMany(scene => scene.Beats.SelectMany(beat => beat.Variants)));

        #region Corpus

        [Test]
        public void Corpus_LoadsFromTheEmbeddedResources()
        {
            Assert.That(Corpus.Lines.Length, Is.GreaterThanOrEqualTo(800));
            Assert.That(Corpus.Scenes.Length, Is.GreaterThanOrEqualTo(250));
            Assert.That(Corpus.Voices.Length, Is.GreaterThanOrEqualTo(5));
            Assert.That(Corpus.Lines.Select(line => line.Id).Distinct().Count(), Is.EqualTo(Corpus.Lines.Length), "Line ids are unique.");
            Assert.That(Corpus.Scenes.Select(scene => scene.Id).Distinct().Count(), Is.EqualTo(Corpus.Scenes.Length), "Scene ids are unique.");
        }

        [Test]
        public void Runtime_LoadsTheSameCorpus()
        {
            Assert.That(AutonomousBotVoices.Corpus, Is.Not.Null, "The server finds the embedded corpus.");
            Assert.That(AutonomousBotVoices.Corpus.Lines.Length, Is.EqualTo(Corpus.Lines.Length));
        }

        [Test]
        public void Slots_AreOnlyOnesTheServerFillsOrMemorySlots()
        {
            var allowed = KnownSlots.Concat(MemorySlots).Concat(EventSlots).ToHashSet();
            var used = AllTexts().SelectMany(text => Regex.Matches(text, @"\{(\w+)\}").Select(match => match.Groups[1].Value)).ToHashSet();
            Assert.That(used.Except(allowed), Is.Empty, "Every slot is filled by the server or skipped as a memory slot.");
            Assert.That(AllTexts().Where(text => text.Contains('{') != text.Contains('}')), Is.Empty, "No broken braces.");
        }

        [Test]
        public void Tags_UseRealClassesAndPersonalityTypes()
        {
            var classes = Enum.GetNames<eCharacterClass>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var types = Enum.GetNames<BotPersonalityType>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Multiple(() =>
            {
                foreach (VC.Line line in Corpus.Lines)
                {
                    Assert.That(line.Classes.Where(c => !classes.Contains(c)), Is.Empty, line.Id);
                    Assert.That(line.Who.Where(t => !types.Contains(t)), Is.Empty, line.Id);
                    Assert.That(line.Realm, Is.AnyOf("*", "alb", "mid", "hib"), line.Id);
                    Assert.That(line.LevelLow, Is.InRange(1, line.LevelHigh), line.Id);
                }
                foreach (VC.Scene scene in Corpus.Scenes)
                foreach (VC.RoleSpec spec in scene.Roles.Values)
                {
                    Assert.That(spec.Classes.Where(c => !classes.Contains(c)), Is.Empty, scene.Id);
                    Assert.That(spec.Types.Where(t => !types.Contains(t)), Is.Empty, scene.Id);
                }
            });
        }

        [Test]
        public void Lines_AreShortAndInEra()
        {
            Assert.Multiple(() =>
            {
                foreach (string text in AllTexts())
                {
                    Assert.That(text.Length, Is.InRange(1, VC.MaxLength), text);
                    foreach (string word in OutOfEra)
                        Assert.That(Regex.IsMatch(text.ToLowerInvariant(), $@"(?<![a-z]){Regex.Escape(word)}(?![a-z])"), Is.False, text);
                }
            });
        }

        [Test]
        public void Scenes_HaveTwoSpeakersAndSomethingToSay()
        {
            Assert.Multiple(() =>
            {
                foreach (VC.Scene scene in Corpus.Scenes)
                {
                    Assert.That(scene.SpeakingRoles.Length, Is.GreaterThanOrEqualTo(2), scene.Id);
                    Assert.That(scene.Beats.Where(beat => !beat.IsPause && beat.Variants.Length == 0), Is.Empty, scene.Id);
                    Assert.That(scene.Realm, Is.AnyOf("*", "alb", "mid", "hib"), scene.Id);
                }
            });
        }

        [Test]
        public void EveryScene_IsPlayable_WhenTheSpeakersCarryAnItemAndHaveATarget()
        {
            // Scenes use no memory slots, so with every server slot filled each required beat can be said.
            Dictionary<string, string> slots = FullSlots();
            Assert.That(Corpus.Scenes.Where(scene => !Debates.Playable(scene, _ => slots)).Select(scene => scene.Id), Is.Empty);
        }

        #endregion

        #region Voices

        [Test]
        public void VoiceFor_FollowsTheShares_AndIsFixedById()
        {
            const int bots = 100_000;
            double total = Corpus.Voices.Sum(voice => voice.Share);
            var counts = new Dictionary<string, int>();
            for (long id = 1; id <= bots; id++)
            {
                string name = Corpus.VoiceFor(id).Name;
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
            foreach (VC.Voice voice in Corpus.Voices)
                Assert.That(counts.GetValueOrDefault(voice.Name) / (double)bots, Is.EqualTo(voice.Share / total).Within(0.01), voice.Name);
            Assert.That(Corpus.VoiceFor(4242).Name, Is.EqualTo(Corpus.VoiceFor(4242).Name));
        }

        [Test]
        public void Style_Proper_ExpandsCapitalizesAndEndsWithAPeriod()
        {
            VC.Voice proper = Corpus.Voices.Single(voice => voice.Name == "proper");
            Assert.That(VC.Style("i cant go, im afk", proper, new Random(1)), Is.EqualTo("I can't go, I'm afk."));
            Assert.That(VC.Style("anyone for tomb?", proper, new Random(1)), Is.EqualTo("Anyone for tomb?"));
            Assert.That(VC.Style("nice :)", proper, new Random(1)), Is.EqualTo("Nice :)"));
        }

        [Test]
        public void Style_Terse_AbbreviatesAndDropsPunctuation()
        {
            VC.Voice terse = Corpus.Voices.Single(voice => voice.Name == "terse");
            Assert.That(VC.Style("thanks, see you tonight.", terse, new Random(1)), Is.EqualTo("ty see u tonite"));
        }

        [Test]
        public void Style_NoVoiceUsesLaterSlang_AndNothingGrowsPastTheLimit()
        {
            Assert.That(Corpus.Voices.SelectMany(voice => voice.Abbreviate.Select(pair => pair.To)), Does.Not.Contain("rn"));
            var random = new Random(7);
            string longLine = string.Join(' ', Enumerable.Repeat("something", 20));
            foreach (VC.Voice voice in Corpus.Voices)
            for (int draw = 0; draw < 50; draw++)
                Assert.That(VC.Style(longLine, voice, random).Length, Is.LessThanOrEqualTo(VC.MaxLength), voice.Name);
            Assert.That(VC.Style(null, Corpus.Voices[0], random), Is.Null);
        }

        [Test]
        public void SwapTypo_SwapsTwoNeighboringLettersInOneLongWord()
        {
            var random = new Random(3);
            for (int draw = 0; draw < 100; draw++)
            {
                string typo = VC.SwapTypo("need a healer here", random);
                string[] before = "need a healer here".Split(' ');
                string[] after = typo.Split(' ');
                Assert.That(after.Length, Is.EqualTo(before.Length));
                int[] changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();
                Assert.That(changed.Length, Is.LessThanOrEqualTo(1), typo);
                foreach (int index in changed)
                {
                    Assert.That(before[index].Length, Is.GreaterThanOrEqualTo(4));
                    Assert.That(after[index].OrderBy(c => c), Is.EqualTo(before[index].OrderBy(c => c)), "Same letters, swapped.");
                }
            }
            Assert.That(VC.SwapTypo("ok hi", random), Is.EqualTo("ok hi"), "No word long enough: unchanged.");
        }

        #endregion

        #region Slots

        [Test]
        public void TryFill_NeverSendsAHalfFilledLine()
        {
            Assert.That(VC.TryFill("anyone seen {player}?", FullSlots(), out string filled), Is.False);
            Assert.That(filled, Is.Null);
            var noItem = FullSlots();
            noItem["item"] = string.Empty;
            Assert.That(VC.TryFill("pc on {item}?", noItem, out _), Is.False, "An empty value isn't a value (except the opener).");
            Assert.That(VC.TryFill("pc on {item}?", null, out _), Is.False);
        }

        [Test]
        public void TryFill_EmptyOpenerIsTrimmed()
        {
            Assert.That(VC.TryFill("{q} anyone up for {dungeon}?", FullSlots(), out string bare), Is.True);
            Assert.That(bare, Is.EqualTo("anyone up for Tomb of Mithra?"));
            Assert.That(VC.TryFill("{q} anyone up for {dungeon}?", FullSlots("ok so"), out string opened), Is.True);
            Assert.That(opened, Is.EqualTo("ok so anyone up for Tomb of Mithra?"));
            Assert.That(VC.TryFill("{q}, where is {keep}", FullSlots(), out string comma), Is.True);
            Assert.That(comma, Is.EqualTo("where is Caer Benowyc"));
        }

        #endregion

        #region Choosing

        [Test]
        public void TryPick_RespectsRealmLevelNeedsClassAndIntent()
        {
            var random = new Random(11);
            var byId = Corpus.Lines.ToDictionary(line => line.Id);
            VC.Speaker speaker = Speaker(eRealm.Midgard, 12, BotPersonalityType.Explorer, "Healer", 55, "solo");
            Assert.Multiple(() =>
            {
                for (int draw = 0; draw < 400; draw++)
                {
                    Assert.That(Corpus.TryPick("ambient", speaker, FullSlots(), null, DateTime.UtcNow, random, out string text, out string id), Is.True);
                    VC.Line line = byId[id];
                    Assert.That(line.Realm, Is.AnyOf("*", "mid"), id);
                    Assert.That(12, Is.InRange(line.LevelLow, line.LevelHigh), id);
                    // Only "solo" may be required; "!flag" needs are met by a speaker without that flag (0.36-se.4).
                    Assert.That(line.Needs.Where(need => !need.StartsWith('!')).All(need => need == "solo"), Is.True, id);
                    Assert.That(VC.NeedsMet(line.Needs, speaker.Flags), Is.True, id);
                    Assert.That(VC.ClassFits(line.Classes, "Healer"), Is.True, id);
                    Assert.That(text, Does.Not.Contain("{").And.Not.Contain("}"), id);
                    Assert.That(text.Length, Is.InRange(1, VC.MaxLength), id);
                }
                for (int draw = 0; draw < 50; draw++)
                {
                    if (Corpus.TryPick("reply", speaker, FullSlots(), null, DateTime.UtcNow, random, out _, out string id, "grouping"))
                        Assert.That(byId[id].Intent, Is.EqualTo("grouping"), id);
                }
            });
        }

        [Test]
        public void TryPick_LinesWrittenForOtherTypesAreNeverChosen()
        {
            var random = new Random(5);
            var byId = Corpus.Lines.ToDictionary(line => line.Id);
            foreach (BotPersonalityType type in Enum.GetValues<BotPersonalityType>())
            {
                VC.Speaker speaker = Speaker(eRealm.Hibernia, 30, type, "Druid", 77, "in_group", "in_town", "at_camp");
                for (int draw = 0; draw < 200; draw++)
                {
                    if (!Corpus.TryPick("ambient", speaker, FullSlots(), null, DateTime.UtcNow, random, out _, out string id))
                        continue;
                    string[] who = byId[id].Who;
                    Assert.That(who.Length == 0 || VC.TypeFits(who, type), Is.True, $"{type} said {id} written for {string.Join(",", who)}");
                }
            }
            VC.Line tagged = Corpus.Lines.First(line => line.Who.Length > 0);
            BotPersonalityType own = Enum.Parse<BotPersonalityType>(tagged.Who[0], true);
            BotPersonalityType other = Enum.GetValues<BotPersonalityType>().First(t => !VC.TypeFits(tagged.Who, t));
            Assert.That(VC.WeightFor(tagged, Speaker(type: own)), Is.EqualTo(Math.Max(0.01, tagged.Weight) * 3));
            Assert.That(VC.WeightFor(tagged, Speaker(type: other)), Is.Zero);
        }

        [Test]
        public void TryPick_DoesNotRepeatInARealmWithinSixHours()
        {
            var random = new Random(9);
            var recent = new VC.RecentLines();
            DateTime now = new(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
            VC.Speaker speaker = Speaker(eRealm.Albion, 25, BotPersonalityType.Leveler, "Armsman", 9, "just_leveled");
            var seen = new List<string>();
            while (seen.Count < 100 && Corpus.TryPick("ding", speaker, FullSlots(), recent, now, random, out _, out string id))
                seen.Add(id);
            Assert.That(seen, Is.Not.Empty);
            Assert.That(seen.Distinct().Count(), Is.EqualTo(seen.Count), "Each ding line once, then nothing.");
            Assert.That(Corpus.TryPick("ding", speaker, FullSlots(), recent, now.AddHours(1), random, out _, out _), Is.False);
            Assert.That(Corpus.TryPick("ding", Speaker(eRealm.Midgard, 25, BotPersonalityType.Leveler, "Warrior", 9, "just_leveled"),
                FullSlots(), recent, now.AddHours(1), random, out _, out _), Is.True, "Another realm has its own window.");
            Assert.That(Corpus.TryPick("ding", speaker, FullSlots(), recent, now.Add(VC.RecentLines.Window).AddMinutes(1), random, out _, out _),
                Is.True, "Lines come back after the window.");
        }

        [Test]
        public void RecentLines_HoldsEveryLineForTheWholeWindow()
        {
            // Audit 2026-10-16: the old 400-entry list let a busy realm repeat lines after an hour or two.
            var recent = new VC.RecentLines();
            DateTime now = new(2026, 10, 16, 20, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 1500; i++)
                recent.Use(eRealm.Hibernia, "line" + i, now.AddSeconds(i));
            Assert.That(recent.WasUsed(eRealm.Hibernia, "line0", now.AddHours(5)), Is.True, "the first of 1,500 lines is still held");
            // The window is three days since 0.36-se.4 (six hours before).
            Assert.That(recent.WasUsed(eRealm.Hibernia, "line0", now.Add(VC.RecentLines.Window).AddHours(-1)), Is.True);
            Assert.That(recent.WasUsed(eRealm.Hibernia, "line0", now.Add(VC.RecentLines.Window).AddSeconds(1)), Is.False);
            Assert.That(recent.WasUsed(eRealm.Albion, "line0", now), Is.False, "realms are separate");
            for (int i = 0; i < VC.RecentLines.Capacity + 10; i++)
                recent.Use(eRealm.Midgard, "m" + i, now);
            Assert.That(recent.Count(eRealm.Midgard), Is.LessThanOrEqualTo(VC.RecentLines.Capacity));
        }

        [Test]
        public void TryPick_FiltersByTopicWhenAsked()
        {
            var random = new Random(5);
            VC.Speaker speaker = Speaker(eRealm.Albion, 50, BotPersonalityType.Leveler, "Armsman", 9);
            var slots = FullSlots();
            slots["target"] = "Brannoc";
            for (int i = 0; i < 20; i++)
            {
                Assert.That(Corpus.TryPick("grats", speaker, slots, null, DateTime.UtcNow, random, out _, out string id, topic: "fifty"), Is.True);
                Assert.That(Corpus.Lines.Single(line => line.Id == id).Topic, Is.EqualTo("fifty"));
            }
        }

        [Test]
        public void LivingServerKinds_HaveLinesForEverySpeaker()
        {
            var random = new Random(3);
            var slots = FullSlots();
            foreach (string slot in EventSlots.Concat(MemorySlots))
                slots[slot] = "x";
            foreach (BotPersonalityType type in Enum.GetValues<BotPersonalityType>())
            foreach ((string kind, string topic, string[] flags) in new[]
            {
                ("grats", null, new string[0]), ("grats", "fifty", new string[0]), ("rez_ask", null, new[] { "just_died" }),
                ("rez_ask", "player", new[] { "just_died", "player_nearby" }), ("rez_thanks", null, new string[0]),
                ("rez_offer", null, new string[0]), ("corpse", null, new[] { "just_died" }), ("sickness", null, new string[0]),
                ("first", null, new string[0]), ("alarm", null, new[] { "in_rvr" }), ("nemesis", null, new string[0]),
                ("revenge", null, new string[0]), ("session", "logon", new string[0]), ("session", "logoff", new string[0]),
                ("session", "brb", new string[0]), ("session", "back", new string[0]), ("linkdead", "gone", new string[0]),
                ("linkdead", "back", new string[0]), ("greet", null, new[] { "knows_player", "player_nearby" }),
            })
            {
                VC.Speaker speaker = Speaker(eRealm.Midgard, 30, type, "Healer", 77, flags);
                Assert.That(Corpus.TryPick(kind, speaker, slots, null, DateTime.UtcNow, random, out _, out _, topic: topic), Is.True,
                    $"{type} has a {kind}/{topic} line");
            }
        }

        [Test]
        public void TryPick_SkipsLinesThatNeedMemoryOfThePlayer()
        {
            var random = new Random(13);
            var byId = Corpus.Lines.ToDictionary(line => line.Id);
            VC.Speaker speaker = Speaker(eRealm.Albion, 30, BotPersonalityType.Socialite, "Cleric", 21, "in_group", "knows_player", "in_town");
            foreach (string kind in Corpus.Lines.Select(line => line.Kind).Distinct())
            for (int draw = 0; draw < 60; draw++)
            {
                if (Corpus.TryPick(kind, speaker, FullSlots(), null, DateTime.UtcNow, random, out _, out string id))
                    Assert.That(MemorySlots.Any(slot => byId[id].Text.Contains("{" + slot + "}")), Is.False, id);
            }
        }

        #endregion

        #region Debates

        private static VC.Scene TestScene(string id = "s", string realm = "*", int low = 10, int high = 20, bool healerRole = true)
        {
            var roles = new Dictionary<string, VC.RoleSpec>(StringComparer.Ordinal)
            {
                ["talker"] = new(["Socialite"], []),
                ["other"] = new([], []),
            };
            if (healerRole)
                roles["healer"] = new([], ["Healer"]);
            var beats = new List<VC.Beat>
            {
                new("talker", ["pc on {item}?"], false, 0),
                new("~", [], false, 4),
                new("other", ["no idea"], false, 0),
            };
            if (healerRole)
                beats.Add(new VC.Beat("healer", ["i can heal"], true, 0));
            return new VC.Scene(id, "test", realm, low, high, roles, beats.ToArray());
        }

        [Test]
        public void Cast_KeepsClassRoles_RelaxesTypeWishes_AndNeedsTwoSpeakers()
        {
            var random = new Random(1);
            VC.Scene scene = TestScene();
            var warrior = new Debates.Candidate(1, BotPersonalityType.Leveler, "Warrior", 15);
            var healer = new Debates.Candidate(2, BotPersonalityType.Leveler, "Healer", 15);
            var berserker = new Debates.Candidate(3, BotPersonalityType.Daredevil, "Berserker", 16);
            Dictionary<string, long> cast = Debates.Cast(scene, [warrior, healer, berserker], random);
            Assert.That(cast, Is.Not.Null);
            Assert.That(cast["healer"], Is.EqualTo(2), "The class-bound role gets the healer.");
            Assert.That(cast.Values.Distinct().Count(), Is.EqualTo(3), "Nobody plays two roles.");
            Assert.That(Debates.Cast(scene, [warrior, berserker], random), Is.Null, "No healer, no scene.");
            Assert.That(Debates.Cast(TestScene(healerRole: false), [warrior], random), Is.Null, "One bot can't debate itself.");
            var tooHigh = new Debates.Candidate(4, BotPersonalityType.Socialite, "Warrior", 40);
            Assert.That(Debates.Cast(TestScene(healerRole: false), [warrior, tooHigh], random), Is.Null, "Levels must fit within five.");
            var socialite = new Debates.Candidate(5, BotPersonalityType.Socialite, "Warrior", 20);
            for (int draw = 0; draw < 20; draw++)
                Assert.That(Debates.Cast(TestScene(healerRole: false), [warrior, socialite], random)["talker"], Is.EqualTo(5),
                    "The type wish is preferred when someone fits it.");
        }

        [Test]
        public void DecideBeat_WaitsForABusySpeakerAndEndsOnlyWhenSpeakersAreGone()
        {
            // In-game check 2026-10-17: camp_etiquette ended after 24 s because two of three levelling bots
            // were mid-fight when a line came due. A fight is now waited out.
            Assert.Multiple(() =>
            {
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Free, 0, false, 1), Is.EqualTo(Debates.BeatDecision.Speak));
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Busy, 0, false, 1), Is.EqualTo(Debates.BeatDecision.Wait),
                    "A busy speaker is waited for, even when it is the only other one around.");
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Busy, Debates.MaxBusyWaitMs - 1, true, 3), Is.EqualTo(Debates.BeatDecision.Wait),
                    "The original speaker keeps the line while the wait lasts.");
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Busy, Debates.MaxBusyWaitMs, true, 3), Is.EqualTo(Debates.BeatDecision.Speak),
                    "After the wait a stand-in takes the line.");
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Busy, Debates.MaxBusyWaitMs, false, 2), Is.EqualTo(Debates.BeatDecision.Skip),
                    "Still busy, no stand-in, two around: skip the line, keep the thread.");
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Gone, 0, false, 2), Is.EqualTo(Debates.BeatDecision.Skip));
                Assert.That(Debates.DecideBeat(Debates.SpeakerState.Gone, 0, false, 1), Is.EqualTo(Debates.BeatDecision.End),
                    "Only when fewer than two are left does the thread end.");
            });

            // A short fight: the speaker is busy for three retries, then free again. The line is said, not dropped.
            Debates.SpeakerState[] fight = [Debates.SpeakerState.Busy, Debates.SpeakerState.Busy, Debates.SpeakerState.Busy, Debates.SpeakerState.Free];
            Debates.BeatDecision last = Debates.BeatDecision.Wait;
            for (int retry = 0; retry < fight.Length && last == Debates.BeatDecision.Wait; retry++)
                last = Debates.DecideBeat(fight[retry], retry * (long)Debates.BusyRetryMs, false, 2);
            Assert.That(last, Is.EqualTo(Debates.BeatDecision.Speak));
        }

        [Test]
        public void Cast_PrefersBotsOnATownBreak()
        {
            VC.Scene scene = TestScene(healerRole: false);
            var grinding = Enumerable.Range(1, 8).Select(id => new Debates.Candidate(id, BotPersonalityType.Leveler, "Warrior", 15)).ToList();
            var inTown = new Debates.Candidate(100, BotPersonalityType.Leveler, "Warrior", 15, InTown: true);
            for (int seed = 0; seed < 20; seed++)
                Assert.That(Debates.Cast(scene, [.. grinding, inTown], new Random(seed)).Values, Does.Contain(100L), $"seed {seed}");
        }

        [Test]
        public void ChooseScene_NoRepeatUntilEveryFittingSceneIsHeard()
        {
            var random = new Random(2);
            VC.Scene[] scenes = [TestScene("a"), TestScene("b"), TestScene("c"), TestScene("alb-only", "alb")];
            int[] online = [14, 18, 30];
            VC.Scene next = Debates.ChooseScene(scenes, eRealm.Midgard, new HashSet<string> { "a", "b" }, online, random, out bool reset);
            Assert.That(next.Id, Is.EqualTo("c"));
            Assert.That(reset, Is.False);
            next = Debates.ChooseScene(scenes, eRealm.Midgard, new HashSet<string> { "a", "b", "c" }, online, random, out reset);
            Assert.That(next.Id, Is.AnyOf("a", "b", "c"), "Another realm's scene is never chosen.");
            Assert.That(reset, Is.True, "Everything heard: a fresh round.");
            Assert.That(Debates.ChooseScene(scenes, eRealm.Midgard, new HashSet<string>(), [14, 40], random, out _), Is.Null,
                "Needs two bots in the scene's level band.");
        }

        [Test]
        public void Playable_OnlyWhenEveryRequiredBeatCanBeSaid()
        {
            VC.Scene scene = TestScene();
            var noItem = FullSlots();
            noItem.Remove("item");
            Assert.That(Debates.Playable(scene, _ => FullSlots()), Is.True);
            Assert.That(Debates.Playable(scene, role => role == "talker" ? noItem : FullSlots()), Is.False, "The opener can't name an item.");
            Assert.That(Debates.Playable(scene, role => role == "talker" ? FullSlots() : noItem), Is.True, "Others don't need one.");
            var beat = new VC.Beat("x", ["pc on {item}?", "anyone know prices"], false, 0);
            Assert.That(Debates.FilledVariants(beat, noItem), Is.EqualTo(new[] { "anyone know prices" }));
        }

        [Test]
        public void Pacing_TypingDelaysAndGapsStayInBounds()
        {
            Assert.That(Debates.BeatDelayMs(string.Empty), Is.EqualTo(3_000));
            Assert.That(Debates.BeatDelayMs(null), Is.EqualTo(3_000));
            Assert.That(Debates.BeatDelayMs(new string('x', 1_000)), Is.EqualTo(10_000));
            Assert.That(Debates.BeatDelayMs("on my way"), Is.InRange(3_000, 10_000));
            var random = new Random(4);
            for (int draw = 0; draw < 1_000; draw++)
                Assert.That(Debates.NextGapMs(random), Is.InRange(Debates.MinimumGapMs, Debates.MaximumGapMs));
        }

        #endregion
    }
}
