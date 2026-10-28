using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The talking update's chat simulator: scenes played through the real corpus picker (personality, level, realm, class,
    /// flags, slots, writing style, no-repeat rules) and the pure combat-call and news rules, written out as a readable
    /// transcript, and checked for the lies the update removes (a Hibernian in Ardee naming an Albion dungeon). It does not run
    /// a world: who is where, who fights and what news exists is set by each scene. Set TALK_TRANSCRIPT to a path to keep the
    /// transcript; otherwise it goes to the test output.
    /// </summary>
    [TestFixture]
    public class UT_ChatSimulation
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

        private static readonly string[] Names =
        [
            "Aerwyn", "Brannoc", "Caitir", "Dunstan", "Eluned", "Fionn", "Gareth", "Halvard", "Ingrid", "Jorund", "Keira", "Lorcan",
            "Maeve", "Njal", "Osric", "Pryce", "Ragna", "Siobhan", "Torvald", "Ulric", "Vala", "Wynn", "Ysolde", "Zeph",
        ];

        private static readonly (eRealm Realm, string Code, string[] Classes)[] Realms =
        [
            (eRealm.Albion, "alb", ["Armsman", "Cleric", "Wizard", "Friar", "Minstrel", "Paladin", "Sorcerer", "Scout", "Infiltrator"]),
            (eRealm.Midgard, "mid", ["Warrior", "Healer", "Runemaster", "Shaman", "Skald", "Thane", "Hunter", "Berserker", "Spiritmaster"]),
            (eRealm.Hibernia, "hib", ["Hero", "Druid", "Eldritch", "Bard", "Enchanter", "Champion", "Ranger", "Mentalist", "Warden"]),
        ];

        private sealed class Sim
        {
            public readonly StringBuilder Out = new();
            public readonly VC.RecentLines Recent = new();
            public readonly Random Random;
            public DateTime Now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
            public readonly List<string> Problems = new();
            public Sim(int seed) => Random = new Random(seed);
            public void Line(string channel, string who, string text) => Out.AppendLine($"  [{channel}] {who}: {text}");
            public void Note(string text) => Out.AppendLine($"  -- {text}");
        }

        private static VC.Speaker Speaker(Sim sim, eRealm realm, string cls, int level, long id, IEnumerable<string> flags) =>
            new(id, Names[(int)(id % Names.Length)], realm, cls, level, (BotPersonalityType)(id % 6),
                new HashSet<string>(flags.Append(VC.TimeFlag(20)), StringComparer.Ordinal));

        private static Dictionary<string, string> Slots(VC.Speaker speaker, string zone, Dictionary<string, string> extra = null)
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["me"] = speaker.Name, ["class"] = speaker.ClassName.ToLowerInvariant(), ["level"] = speaker.Level.ToString(),
                ["realm"] = GlobalConstants.RealmToName(speaker.Realm), ["zone"] = zone, ["bubbles"] = "4",
            };
            if (extra != null)
                foreach (var pair in extra)
                    slots[pair.Key] = pair.Value;
            return slots;
        }

        private static bool Pick(Sim sim, string kind, VC.Speaker speaker, Dictionary<string, string> slots, out VC.Picked picked,
            string topic = null, string answers = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> needSlots = null,
            Func<VC.Line, bool> filter = null)
        {
            sim.Now = sim.Now.AddSeconds(20 + sim.Random.Next(90));
            return Corpus.TryPick(new VC.Request(kind, speaker, slots, sim.Recent, sim.Now, sim.Random)
            {
                Topic = topic, Answers = answers, NeedSlots = needSlots, Filter = filter, LocalHour = 20, RelaxTo = VC.RecentLines.Relaxed,
            }, out picked);
        }

        /// <summary>The places a text names (by alias of five letters or more, or the whole key).</summary>
        private static IEnumerable<AutonomousChatPlaces.Place> Named(string text)
        {
            string lower = text.ToLowerInvariant();
            foreach (AutonomousChatPlaces.Place place in AutonomousChatPlaces.All)
                if (place.Aliases.Append(place.Key).Any(a => a.Length >= 5 &&
                        Regex.IsMatch(lower, $@"(?<![a-z']){Regex.Escape(a)}(?![a-z])")))
                    yield return place;
        }

        /// <summary>A place of another realm named by a speaker, other than a frontier, capital or battleground: the old lie.</summary>
        private static void CheckPlaces(Sim sim, VC.Speaker speaker, string code, VC.Picked picked)
        {
            foreach (AutonomousChatPlaces.Place place in Named(picked.Text))
                if (place.Realm != "*" && place.Realm != code && place.Type is not ("frontier" or "capital" or "battleground"))
                    sim.Problems.Add($"{code} {speaker.Name} named {place.Realm} place {place.Key}: \"{picked.Text}\"");
        }

        private static void Evening(Sim sim, (eRealm Realm, string Code, string[] Classes) realm)
        {
            sim.Out.AppendLine($"== An evening in {GlobalConstants.RealmToName(realm.Realm)}: /faction and /say, 30 openers");
            AutonomousChatPlaces.Place[] home = AutonomousChatPlaces.All
                .Where(p => p.Realm == realm.Code && p.Type is "zone" or "town" or "dungeon" && p.Zones.Length > 0).ToArray();
            for (int i = 0; i < 30; i++)
            {
                AutonomousChatPlaces.Place at = home[sim.Random.Next(home.Length)];
                string zone = at.Zones[0];
                int level = 5 + sim.Random.Next(46);
                long id = 1000 + (int)realm.Realm * 100 + i;
                var flags = new List<string>(AutonomousChatPlaces.Flags(zone, null, null)) { sim.Random.Next(3) == 0 ? "in_group" : "solo" };
                flags.Add(at.Type == "dungeon" ? "in_dungeon" : "outdoors");
                flags.Add(AutonomousBotVoices.XpFlag(sim.Random.Next(10), level));
                if (level <= 14) flags.Add("newbie");
                else if (level >= 40) flags.Add("veteran");
                VC.Speaker speaker = Speaker(sim, realm.Realm, realm.Classes[sim.Random.Next(realm.Classes.Length)], level, id, flags.Where(f => f != null));
                string kind = sim.Random.Next(4) == 0 && flags.Contains("solo") && level >= 5 ? "lfg" : "ambient";
                if (!Pick(sim, kind, speaker, Slots(speaker, zone), out VC.Picked picked))
                    continue;
                sim.Line(kind == "lfg" ? "lfg" : "faction", $"{speaker.Name} ({level} {speaker.ClassName}, in {zone})", picked.Text);
                CheckPlaces(sim, speaker, realm.Code, picked);
                if (!string.IsNullOrEmpty(picked.Correction))
                    sim.Line("faction", speaker.Name, picked.Correction);
                // A reply now and then, as the coordinator schedules them.
                string[] chain = VC.AnswerChain(picked.Line.Invites);
                if (chain.Length > 0 && sim.Random.Next(2) == 0)
                {
                    VC.Speaker mate = Speaker(sim, realm.Realm, realm.Classes[sim.Random.Next(realm.Classes.Length)], level, id + 50, ["solo", "outdoors"]);
                    foreach (string answers in chain)
                        if (Pick(sim, "reply", mate, Slots(mate, zone, new() { ["player"] = speaker.Name }), out VC.Picked reply, answers: answers,
                                filter: line => AutonomousChatShortTalk.ReplyFits(picked.Text, line.Text)))
                        {
                            sim.Line("faction", mate.Name, reply.Text);
                            CheckPlaces(sim, mate, realm.Code, reply);
                            break;
                        }
                }
            }
        }

        private static void KeepLost(Sim sim)
        {
            sim.Out.AppendLine("== News: Midgard takes Caer Benowyc from Albion (an hour of flags)");
            var board = new ChatNewsBoard();
            DateTime taken = sim.Now;
            foreach (ChatNewsItem item in AutonomousChatNews.KeepChange("Caer Benowyc", "Pennine Mountains", eRealm.Albion, eRealm.Midgard, taken))
                board.Post(item);
            foreach ((eRealm realm, string code, string[] classes) in Realms)
            {
                var needSlots = board.FlagSlots(realm, taken.AddMinutes(1));
                ChatNewsItem item = board.Latest(realm, ChatNewsBoard.KeepWindow, taken.AddMinutes(1), "keep_won", "keep_lost", "keep_other");
                VC.Speaker teller = Speaker(sim, realm, classes[0], 45, 5000 + (int)realm, needSlots.Keys.Concat(["in_frontier", "in_rvr"]));
                var slots = Slots(teller, "Pennine Mountains", item.Slots().ToDictionary(p => p.Key, p => p.Value));
                if (Pick(sim, "news", teller, slots, out VC.Picked news, topic: item.Topic, needSlots: needSlots))
                {
                    sim.Line("faction", $"{teller.Name} ({GlobalConstants.RealmToName(realm)}, {item.Topic})", news.Text);
                    if (news.Text.Contains("{"))
                        sim.Problems.Add("unfilled slot: " + news.Text);
                    VC.Speaker other = Speaker(sim, realm, classes[1], 30, 5100 + (int)realm, ["outdoors"]);
                    foreach (string answers in new[] { "news_" + item.Topic, "news" })
                        if (Pick(sim, "reply", other, Slots(other, "Camelot Hills"), out VC.Picked reply, answers: answers))
                        {
                            sim.Line("faction", other.Name, reply.Text);
                            break;
                        }
                }
                else
                    sim.Problems.Add($"no {item.Topic} news line for {realm}");
                // A bot far from the frontier with no news flag must not talk about a keep at all.
                VC.Speaker quiet = Speaker(sim, realm, classes[2], 20, 5200 + (int)realm, ["outdoors", "solo"]);
                for (int i = 0; i < 40; i++)
                    if (Pick(sim, "ambient", quiet, Slots(quiet, "Camelot Hills"), out VC.Picked ambient) && ambient.Text.Contains("Caer Benowyc"))
                        sim.Problems.Add("keep named without news: " + ambient.Text);
            }
        }

        private static void Fight(Sim sim)
        {
            sim.Out.AppendLine("== A Hibernian group with the player: a pull, an add, a mez, low health, a win, a rest");
            var group = new AutonomousBotCombatCalls.GroupState();
            var bots = new[]
            {
                Speaker(sim, eRealm.Hibernia, "Hero", 30, 7001, ["in_group", "in_player_group", "is_tank", "in_combat"]),
                Speaker(sim, eRealm.Hibernia, "Druid", 30, 7002, ["in_group", "in_player_group", "is_healer", "is_support", "can_rez", "in_combat"]),
                Speaker(sim, eRealm.Hibernia, "Enchanter", 30, 7003, ["in_group", "in_player_group", "is_caster", "in_combat"]),
                Speaker(sim, eRealm.Hibernia, "Bard", 30, 7004, ["in_group", "in_player_group", "is_support", "is_healer", "in_combat"]),
            };
            var always = new FixedRandom(0.0);
            void Call(VC.Speaker who, string topic, string what, int enemies = 1)
            {
                // The same line filter the server uses for pull and add calls.
                if (Pick(sim, "combat", who, Slots(who, "Cursed Forest"), out VC.Picked picked, topic: topic,
                        filter: topic is "pull" or "add" ? line => AutonomousBotCombatCalls.PullLineFits(line.Text, enemies) : null))
                    sim.Line("group", $"{who.Name} ({who.ClassName}, {what})", picked.Text);
                else
                    sim.Note($"{who.Name} has no {topic} line ({what})");
            }
            string pull = AutonomousBotCombatCalls.GroupEvent(group, new(true, 1, 5, 5, false), 1_000, always);
            Call(bots[0], pull, "fight starts, 1 enemy");
            string add = AutonomousBotCombatCalls.GroupEvent(group, new(true, 2, 5, 5, false), 6_000, always);
            Call(bots[2], add, "a second enemy joins", 2);
            Call(bots[3], AutonomousBotCombatCalls.CastTopic(eSpellType.Mesmerize, eSpellTarget.ENEMY, 0, 2), "its mesmerize landed");
            var tank = new AutonomousBotCombatCalls.BotState();
            Call(bots[0], AutonomousBotCombatCalls.SelfEvent(tank, true, 24, 0, false, 12_000, always), "health 24%");
            Call(bots[1], AutonomousBotCombatCalls.CastTopic(eSpellType.Heal, eSpellTarget.GROUP, 0, 2), "casting a group heal");
            var caster = new AutonomousBotCombatCalls.BotState();
            Call(bots[2], AutonomousBotCombatCalls.SelfEvent(caster, true, 80, 9, true, 20_000, always), "power 9%");
            string end = AutonomousBotCombatCalls.GroupEvent(group, new(false, 0, 5, 5, false), 40_000, always);
            Call(bots[1], end, "the fight ends");
            AutonomousBotCombatCalls.GroupEvent(group, new(true, 1, 5, 5, false), 60_000, new FixedRandom(0.99));
            string rest = AutonomousBotCombatCalls.GroupEvent(group, new(false, 0, 5, 5, true), 90_000, always);
            Call(bots[2], rest, "the next fight ends, the group is worn");
        }

        private static void Conversations(Sim sim)
        {
            sim.Out.AppendLine("== Conversations: a follow-up, an answered question, a duo offer by tell");
            VC.Speaker boaster = Speaker(sim, eRealm.Midgard, "Thane", 33, 8001, ["outdoors", "solo", "at:myrkwood"]);
            if (Pick(sim, "ambient", boaster, Slots(boaster, "Myrkwood Forest"), out VC.Picked opener,
                    filter: line => VC.InviteFamily(line.Invites) is "boast" or "complaint" or "chat"))
            {
                string family = VC.InviteFamily(opener.Line.Invites);
                sim.Line("faction", boaster.Name, opener.Text);
                sim.Line("faction", "You", "why?");
                if (Pick(sim, "reply", boaster, Slots(boaster, "Myrkwood Forest"), out VC.Picked why, answers: "followup_why",
                        filter: line => line.Topic == family) ||
                    Pick(sim, "reply", boaster, Slots(boaster, "Myrkwood Forest"), out why, answers: "followup_why",
                        filter: line => line.Topic is "general" or ""))
                    sim.Line("faction", boaster.Name, why.Text);
                else
                    sim.Problems.Add("no answer to why?");
            }
            VC.Speaker asker = Speaker(sim, eRealm.Albion, "Friar", 22, 8002, ["outdoors", "solo"]);
            if (Pick(sim, "ambient", asker, Slots(asker, "Black Mountains South"), out VC.Picked ask,
                    filter: line => VC.InviteFamily(line.Invites) == "ask"))
            {
                sim.Line("faction", asker.Name, ask.Text);
                sim.Line("faction", "You", "try the spiders by the road, worked for me");
                Assert.That(AutonomousChatThreads.ReadsAsAnswer("try the spiders by the road, worked for me", eAutonomousChatIntent.Grinding), Is.True);
                foreach (string answers in AutonomousChatThreads.ClosingAnswers(ask.Line.Invites))
                    if (Pick(sim, "closing", asker, Slots(asker, "Black Mountains South", new() { ["player"] = "You" }), out VC.Picked thanks, answers: answers))
                    {
                        sim.Line("faction", asker.Name, thanks.Text);
                        break;
                    }
            }
            VC.Speaker duo = Speaker(sim, eRealm.Hibernia, "Champion", 18, 8003, ["outdoors", "solo"]);
            if (Pick(sim, "tell", duo, Slots(duo, "Cursed Forest"), out VC.Picked offer, topic: "group_offer",
                    filter: line => AutonomousLivingServer.OfferFits(line.Text, true)))
            {
                sim.Line("tell", duo.Name + " -> You", offer.Text);
                sim.Line("tell", "You -> " + duo.Name, "sure");
                Assert.That(AutonomousChatThreads.SaysYes("sure"), Is.True);
                sim.Line("tell", duo.Name + " -> You", "sure, send me an invite   (JoinReply: the player invites; a bot cannot)");
            }
            else
                sim.Problems.Add("no group_offer tell");
        }

        private sealed class FixedRandom(double value) : Random
        {
            public override double NextDouble() => value;
            public override int Next(int maxValue) => 0;
            public override int Next(int minValue, int maxValue) => minValue;
        }

        [Test]
        public void ASimulatedEveningSaysOnlyWhatIsTrue()
        {
            var sim = new Sim(20261026);
            foreach (var realm in Realms)
                Evening(sim, realm);
            KeepLost(sim);
            Fight(sim);
            Conversations(sim);
            string transcript = sim.Out.ToString();
            string path = Environment.GetEnvironmentVariable("TALK_TRANSCRIPT");
            if (!string.IsNullOrEmpty(path))
                File.WriteAllText(path, transcript);
            else
                TestContext.Out.WriteLine(transcript);
            Assert.That(sim.Problems, Is.Empty, string.Join("\n", sim.Problems));
            Assert.That(transcript.Split('\n').Count(l => l.StartsWith("  [")), Is.GreaterThan(80));
        }
    }
}
