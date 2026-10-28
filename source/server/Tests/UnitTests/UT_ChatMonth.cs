using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// A month of evenings through the real corpus picker and its no-repeat window, to find the line pools a nightly player
    /// would hear run dry. An approximate model, not a world: per realm, 30 evenings of 4 hours with /faction openers at the
    /// pace FactionGapMs gives 900 bots in the evening (one about every 74 s), replies to about half of them, /guild lines
    /// (about 12 an hour) and about 25 player lines an evening answered from the reply sets. Speakers are random bots of the
    /// realm (level, class, personality, place). Debates are left out: one plays every 30-45 minutes and none repeats until
    /// every fitting one has played. Set TALK_MONTH_OUT to a path for the report.
    /// </summary>
    [TestFixture, Explicit("audit tool: set TALK_MONTH_OUT")]
    public class UT_ChatMonth
    {
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

        private static readonly (eRealm Realm, string[] Classes)[] Realms =
        [
            (eRealm.Albion, ["Armsman", "Cleric", "Wizard", "Friar", "Minstrel", "Paladin", "Sorcerer", "Scout", "Infiltrator", "Theurgist", "Cabalist", "Mercenary", "Necromancer", "Reaver"]),
            (eRealm.Midgard, ["Warrior", "Healer", "Runemaster", "Shaman", "Skald", "Thane", "Hunter", "Berserker", "Spiritmaster", "Savage", "Shadowblade", "Bonedancer"]),
            (eRealm.Hibernia, ["Hero", "Druid", "Eldritch", "Bard", "Enchanter", "Champion", "Ranger", "Mentalist", "Warden", "Blademaster", "Nightshade", "Animist", "Valewalker"]),
        ];

        private static readonly HashSet<string> Healers = new(["Cleric", "Friar", "Healer", "Shaman", "Druid", "Bard", "Warden", "Mentalist"]);
        private static readonly HashSet<string> Tanks = new(["Armsman", "Paladin", "Warrior", "Thane", "Hero", "Champion", "Mercenary", "Blademaster"]);
        private static readonly HashSet<string> Casters = new(["Wizard", "Sorcerer", "Theurgist", "Cabalist", "Necromancer", "Runemaster", "Spiritmaster", "Bonedancer", "Eldritch", "Enchanter", "Mentalist", "Animist"]);

        /// <summary>What a player types in an evening, as the reply set it draws (intent or answers), and how often.</summary>
        private static readonly (string Intent, string Answers, double Weight)[] PlayerLines =
        [
            ("greeting", null, 3), ("thanks", null, 4), ("laugh", null, 3), ("farewell", null, 1), ("ding", null, 1), ("back", null, 1),
            ("away", null, 1), ("congrats", null, 1), ("banter", null, 2), (null, "ask", 2), (null, "complaint", 2), (null, "ask_opinion", 1),
            (null, "ask_choice", 1), (null, "followup_why", 1), (null, "followup_really", 1), (null, "followup_how", 0.5),
            (null, "player_goodnews", 0.7), (null, "player_offer", 0.5), (null, "player_wait", 0.7), (null, "player_headsup", 0.5),
            (null, "player_wb", 0.5), (null, "player_rl_late", 0.4), (null, "player_rl_food", 0.4), (null, "player_rl_work", 0.4),
            (null, "player_rl_tech", 0.3), (null, "player_rl_pet", 0.3), (null, "player_rl_weather", 0.2), (null, "player_rl_media", 0.2),
            (null, "player_rl_school", 0.2), (null, "player_rl_body", 0.2), (null, "ask_yes", 1), (null, "ask_no", 0.7), (null, "ask_personal", 1),
        ];

        private sealed class Pool
        {
            public int Picks, Relaxed, Missed, HeardBefore, Size;
            public int? FirstRelaxedEvening;
            public readonly HashSet<string> Used = new(StringComparer.Ordinal);
        }

        [Test]
        public void AMonthOfEvenings()
        {
            string output = Environment.GetEnvironmentVariable("TALK_MONTH_OUT");
            if (string.IsNullOrEmpty(output))
                Assert.Ignore("set TALK_MONTH_OUT");
            VC corpus = Load();
            // TALK_MONTH_RATE scales the /faction and /guild pace as bot_voices_chat_rate does (1 = default).
            double rate = double.TryParse(Environment.GetEnvironmentVariable("TALK_MONTH_RATE"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double r) && r > 0 ? r : 1.0;
            var report = new StringBuilder();
            report.AppendLine($"A month of evenings (30 x 4 h per realm), chat rate {rate}, approximate model; see UT_ChatMonth.");
            foreach ((eRealm realm, string[] classes) in Realms)
            {
                var random = new Random(11 + (int)realm);
                var recent = new VC.RecentLines();
                var pools = new SortedDictionary<string, Pool>(StringComparer.Ordinal);
                var heardEver = new Dictionary<string, int>(StringComparer.Ordinal);
                var heardText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int[] heardPerWeek = new int[5], linesPerWeek = new int[5], sameTextPerWeek = new int[5];
                DateTime start = new(2026, 11, 1, 19, 0, 0, DateTimeKind.Utc);

                VC.Speaker RandomSpeaker(long id)
                {
                    string cls = classes[random.Next(classes.Length)];
                    int level = random.NextDouble() < 0.2 ? 50 : 5 + random.Next(45);
                    var type = (BotPersonalityType)random.Next(6);
                    var flags = new HashSet<string>(StringComparer.Ordinal) { VC.TimeFlag(20) };
                    string place = random.Next(6) switch { 0 => "in_town", 1 => "in_capital", 2 => "in_dungeon", 3 => level >= 20 ? "in_rvr" : "outdoors", 4 => "traveling", _ => "outdoors" };
                    flags.Add(place);
                    if (place == "outdoors" && random.Next(2) == 0)
                        flags.Add("at_camp");
                    flags.Add(random.Next(3) == 0 ? "in_group" : "solo");
                    if (Healers.Contains(cls)) { flags.Add("is_healer"); flags.Add("can_rez"); }
                    if (Tanks.Contains(cls)) flags.Add("is_tank");
                    if (Casters.Contains(cls)) flags.Add("is_caster");
                    if (level < 15) flags.Add("newbie");
                    if (level >= 45) flags.Add("veteran");
                    if (random.Next(8) == 0) flags.Add("bag_full");
                    return new VC.Speaker(id, "Bot" + id, realm, cls, level, type, flags);
                }

                string OpenerKind(VC.Speaker speaker)
                {
                    var options = new List<(string, double)> { ("ambient", 6) };
                    if (speaker.Flags.Contains("solo") && speaker.Level >= 5)
                        options.Add(("lfg", speaker.Type is BotPersonalityType.Socialite or BotPersonalityType.Leveler ? 3 : 1.5));
                    if (speaker.Type == BotPersonalityType.Crafter || speaker.Flags.Contains("bag_full"))
                        options.Add(("trade", speaker.Type == BotPersonalityType.Crafter ? 3 : 1.5));
                    if (speaker.Flags.Contains("in_rvr") || speaker.Level >= 40 && speaker.Type is BotPersonalityType.NoLifer or BotPersonalityType.Daredevil)
                        options.Add(("rvr", speaker.Flags.Contains("in_rvr") ? 4 : 1));
                    double draw = random.NextDouble() * options.Sum(o => o.Item2);
                    foreach ((string kind, double weight) in options)
                    {
                        if (draw < weight)
                            return kind;
                        draw -= weight;
                    }
                    return "ambient";
                }

                VC.Picked Pick(string key, string kind, VC.Speaker speaker, DateTime now, int evening, string intent = null, string answers = null)
                {
                    if (!pools.TryGetValue(key, out Pool pool))
                        pools[key] = pool = new Pool
                        {
                            Size = corpus.Lines.Count(line => line.Kind == kind && (intent == null || line.Intent == intent) &&
                                                              (answers == null || line.Answers.Contains(answers)) &&
                                                              (line.Realm == "*" || line.Realm == RealmCode(realm))),
                        };
                    var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["me"] = speaker.Name, ["class"] = speaker.ClassName.ToLowerInvariant(), ["level"] = speaker.Level.ToString(),
                        ["realm"] = GlobalConstants.RealmToName(realm), ["zone"] = "the hills", ["bubbles"] = "4", ["player"] = "Wynn",
                    };
                    var request = new VC.Request(kind, speaker, slots, recent, now, random) { Intent = intent, Answers = answers, LocalHour = 20 };
                    pool.Picks++;
                    if (!corpus.TryPick(request, out VC.Picked picked))
                    {
                        if (!corpus.TryPick(request with { RelaxTo = VC.RecentLines.Relaxed }, out picked))
                        {
                            pool.Missed++;
                            return null;
                        }
                        pool.Relaxed++;
                        pool.FirstRelaxedEvening ??= evening;
                    }
                    pool.Used.Add(picked.Line.Id);
                    int week = Math.Min(4, evening / 7);
                    linesPerWeek[week]++;
                    if (heardEver.TryGetValue(picked.Line.Id, out int when) && when < evening)
                    {
                        pool.HeardBefore++;
                        heardPerWeek[week]++;
                    }
                    heardEver[picked.Line.Id] = evening;
                    // The same words, after slots and style ("{zone}", typos): what a player would notice.
                    string words = new string(picked.Text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                    if (heardText.TryGetValue(words, out int said) && said < evening)
                        sameTextPerWeek[week]++;
                    heardText[words] = evening;
                    return picked;
                }

                long nextId = 1;
                for (int evening = 0; evening < 30; evening++)
                {
                    DateTime now = start.AddDays(evening);
                    DateTime end = now.AddHours(4);
                    DateTime nextGuild = now.AddMinutes(random.Next(5)), nextPlayer = now.AddMinutes(random.Next(10));
                    while (now < end)
                    {
                        now = now.AddSeconds((49 + random.Next(51)) / rate);
                        VC.Speaker speaker = RandomSpeaker(nextId++);
                        string kind = OpenerKind(speaker);
                        VC.Picked opener = Pick(kind, kind, speaker, now, evening) ?? Pick("ambient", "ambient", speaker, now, evening);
                        if (opener != null && random.Next(2) == 0)
                        {
                            VC.Speaker mate = RandomSpeaker(nextId++);
                            foreach (string answers in VC.AnswerChain(opener.Line.Invites))
                                if (Pick("reply:" + answers, "reply", mate, now.AddSeconds(8), evening, answers: answers) != null)
                                    break;
                        }
                        if (now >= nextGuild)
                        {
                            Pick("guild", "guild", RandomSpeaker(nextId++), now, evening);
                            nextGuild = now.AddSeconds((240 + random.Next(120)) / rate);
                        }
                        if (now >= nextPlayer)
                        {
                            double draw = random.NextDouble() * PlayerLines.Sum(p => p.Weight);
                            foreach ((string intent, string answers, double weight) in PlayerLines)
                            {
                                if (draw >= weight)
                                {
                                    draw -= weight;
                                    continue;
                                }
                                string kindOf = answers is "ask_yes" or "ask_no" or "ask_personal" ? "closing" : "reply";
                                Pick("player:" + (intent ?? answers), kindOf, RandomSpeaker(nextId++), now.AddSeconds(6), evening, intent, answers);
                                break;
                            }
                            nextPlayer = now.AddSeconds(400 + random.Next(380));
                        }
                    }
                }

                report.AppendLine();
                report.AppendLine($"== {RealmCode(realm)}");
                report.AppendLine("  share of lines the player had heard on an earlier evening, by week: " +
                                  string.Join(", ", Enumerable.Range(0, 5).Where(w => linesPerWeek[w] > 0)
                                      .Select(w => $"week {w + 1} {100.0 * heardPerWeek[w] / linesPerWeek[w]:0}%")));
                report.AppendLine("  the same words heard on an earlier evening (after slots and style), by week: " +
                                  string.Join(", ", Enumerable.Range(0, 5).Where(w => linesPerWeek[w] > 0)
                                      .Select(w => $"week {w + 1} {100.0 * sameTextPerWeek[w] / linesPerWeek[w]:0}%")) +
                                  $"; lines an evening: {linesPerWeek.Sum() / 30}");
                report.AppendLine("  pool | picks | pool size | distinct used | heard before | relaxed (first evening) | none left");
                foreach ((string key, Pool pool) in pools.OrderByDescending(p => p.Value.Relaxed + p.Value.Missed).ThenByDescending(p => p.Value.HeardBefore))
                    report.AppendLine($"  {key} | {pool.Picks} | {pool.Size} | {pool.Used.Count} | {pool.HeardBefore} | {pool.Relaxed}" +
                                      (pool.FirstRelaxedEvening is int first ? $" ({first + 1})" : "") + $" | {pool.Missed}");
            }
            File.WriteAllText(output, report.ToString());
            TestContext.Out.WriteLine(report.ToString());
        }

        private static string RealmCode(eRealm realm) => realm switch { eRealm.Albion => "alb", eRealm.Midgard => "mid", _ => "hib" };
    }
}
