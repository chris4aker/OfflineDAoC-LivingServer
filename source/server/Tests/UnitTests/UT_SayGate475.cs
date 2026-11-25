using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// Realism plan step 1 (issue #475; docs/realism-audit-1.2.3/structure.md "A. The say gate", "4. Measuring"): the say gate
    /// every bot line leaves through is a pass-through (the send gets the text byte for byte, nothing is held, nothing is
    /// recorded that nobody saw), every wire exit goes through it, and it writes the session chat log's rows (CHAT_SAY,
    /// CHAT_HEAR, CHAT_HELD, SCREEN_BURST, CHAT_FLAG). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_SayGate475 : DutyLeaseFixture
    {
        private readonly List<string> _rows = [];

        [SetUp]
        public void Capture()
        {
            BotSayGate.ResetForTests();
            _rows.Clear();
            BotSayGate.TestSink = row => { lock (_rows) _rows.Add(row); };
        }

        [TearDown]
        public void Release()
        {
            BotSayGate.TestSink = null;
            BotSayGate.ResetForTests();
        }

        private List<string> Rows(string tag)
        {
            lock (_rows)
                return _rows.Where(row => row.StartsWith(tag + " ", StringComparison.Ordinal)).ToList();
        }

        private static string Field(string row, string name)
        {
            Match match = Regex.Match(row, $@"(?:^| ){name}=(""[^""]*""|\S+)");
            return match.Success ? match.Groups[1].Value.Trim('"') : null;
        }

        /// <summary>Lines a gate must pass untouched: written lines, quotes, unicode, whitespace, very long, empty, null.</summary>
        private static IEnumerable<string> Lines()
        {
            yield return "anyone up for a run to Mularn?";
            yield return "  leading and trailing spaces  ";
            yield return "she said \"inv me when ur close\" lol";
            yield return "line one\nline two\r\nline three";
            yield return "Tír na nÓg ü ß Ð 漢字 🙂 ​ zero width";
            yield return "\t";
            yield return "";
            yield return null;
            yield return new string('x', 5_000);
            var random = new Random(475);
            for (int i = 0; i < 200; i++)
            {
                var text = new StringBuilder();
                int length = random.Next(0, 300);
                for (int j = 0; j < length; j++)
                    text.Append((char)random.Next(1, 0xD7FF));
                yield return text.ToString();
            }
        }

        [Test]
        public void The_gate_hands_every_line_to_its_send_byte_for_byte_and_returns_what_the_send_returned()
        {
            foreach (string line in Lines())
            {
                string given = "unset";
                string result = BotSayGate.Say(new BotSayGate.Utterance("faction", "Marelle", eRealm.Albion, line), text =>
                {
                    given = text;
                    return text;
                });
                Assert.That(given, Is.SameAs(line), "the send gets the very string the exit handed in");
                Assert.That(result, Is.SameAs(line), "the exit gets back what its send returned");
            }
            Assert.That(BotSayGate.Counts.Held, Is.Zero, "a pass-through holds nothing");
        }

        [Test]
        public void The_gate_returns_the_text_its_send_returned_even_when_the_send_changed_it()
        {
            // SendFaction trims and caps the line it sends; the record is of what went out, as before.
            string result = BotSayGate.Say(new BotSayGate.Utterance("faction", "Marelle", eRealm.Albion, "  hi  "), text => text.Trim());
            Assert.That(result, Is.EqualTo("hi"));
            Assert.That(Field(Rows("CHAT_SAY").Single(), "text"), Is.EqualTo("hi"));
        }

        [Test]
        public void Every_step_runs_in_its_fixed_order_and_passes()
        {
            Assert.That(BotSayGate.Order, Is.EqualTo(new[]
            {
                BotSayGate.Step.ChannelFit, BotSayGate.Step.Addressee, BotSayGate.Step.Freshness, BotSayGate.Step.Grounding,
                BotSayGate.Step.Pacing, BotSayGate.Step.Style, BotSayGate.Step.SendAndRecord,
            }));
            var line = new BotSayGate.Utterance("group", "Marelle", eRealm.Albion, "omw");
            foreach (BotSayGate.Step step in BotSayGate.Order)
                Assert.That(BotSayGate.Run(step, line), Is.EqualTo(BotSayGate.Verdict.Pass), step.ToString());
            Assert.That(BotSayGate.Check(line), Is.EqualTo(BotSayGate.Verdict.Pass));
            Assert.That(line.Text, Is.EqualTo("omw"), "no step changes the text");
        }

        [Test]
        public void A_line_nobody_saw_is_not_recorded()
        {
            string result = BotSayGate.Say(new BotSayGate.Utterance("say", "Marelle", eRealm.Albion, "anyone here?"), _ => null);
            Assert.That(result, Is.Null);
            Assert.That(Rows("CHAT_SAY"), Is.Empty);
            Assert.That(BotSayGate.Counts.Lines, Is.Zero);
        }

        [Test]
        public void A_line_said_is_one_CHAT_SAY_row_with_who_where_to_whom_and_the_gates_rule()
        {
            BotSayGate.Say(new BotSayGate.Utterance("tell", "Marelle", eRealm.Albion, "in avalon marsh atm, \"inv\" me when ur close", "Owner"),
                text => text);
            List<string> rows = Rows("CHAT_SAY");
            Assert.That(rows, Has.Count.EqualTo(1));
            string row = rows[0];
            Assert.Multiple(() =>
            {
                Assert.That(Field(row, "ch"), Is.EqualTo("tell"));
                Assert.That(Field(row, "from"), Is.EqualTo("Marelle"));
                Assert.That(Field(row, "to"), Is.EqualTo("Owner"));
                Assert.That(Field(row, "room"), Is.EqualTo("Owner"));
                Assert.That(Field(row, "realm"), Is.EqualTo("Albion"));
                Assert.That(Field(row, "rule"), Is.EqualTo("pass"));
                Assert.That(Field(row, "contract"), Is.EqualTo("none"), "lines carry no contract yet; the gate counts them");
                Assert.That(Field(row, "src"), Is.Not.Null.And.Not.Empty);
                Assert.That(Field(row, "via"), Does.Contain(nameof(UT_SayGate475)), "via= names the code that chose the line, past the gate");
                Assert.That(Field(row, "text"), Is.EqualTo("in avalon marsh atm, 'inv' me when ur close"));
                Assert.That(long.Parse(Field(row, "seq")), Is.GreaterThan(0));
            });
            Assert.That(_rows[0], Does.StartWith("CHAT_SESSION build="), "the file says which build wrote it");
            Assert.That(BotSayGate.Counts, Is.EqualTo((1L, 1L, 0L)));
        }

        [Test]
        public void Rows_are_numbered_in_order()
        {
            for (int i = 0; i < 5; i++)
                BotSayGate.Say(new BotSayGate.Utterance("faction", "Marelle", eRealm.Albion, "line " + i), text => text);
            long[] seqs = Rows("CHAT_SAY").Select(row => long.Parse(Field(row, "seq"))).ToArray();
            Assert.That(seqs, Is.Ordered.Ascending);
            Assert.That(seqs.Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void A_players_line_is_a_CHAT_HEAR_row_and_a_reply_to_them_names_it()
        {
            Player player = RealPlayer();
            typeof(GameObject).GetField("m_name", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, "Owner");
            BotChatTranscript.Player("tell", player, "I'm by camp station", "Marelle");
            string hear = Rows("CHAT_HEAR").Single();
            Assert.Multiple(() =>
            {
                Assert.That(Field(hear, "from"), Is.EqualTo("Owner"));
                Assert.That(Field(hear, "to"), Is.EqualTo("Marelle"));
                Assert.That(Field(hear, "intent"), Is.Not.Null.And.Not.EqualTo("?"), "how the line was read");
                Assert.That(Field(hear, "heard"), Does.Contain("Owner"));
                Assert.That(Field(hear, "text"), Is.EqualTo("I'm by camp station"));
            });
            BotSayGate.Say(new BotSayGate.Utterance("tell", "Marelle", eRealm.Albion, "im still in avalon marsh", "Owner"), text => text);
            Assert.That(Field(Rows("CHAT_SAY").Single(), "re"), Is.EqualTo(Field(hear, "seq")), "re= is the player line it answers");
        }

        [Test]
        public void A_silence_or_a_held_reply_is_a_CHAT_HELD_row_with_its_code()
        {
            AutonomousChatWhy.RecordSilence(eRealm.Albion, "faction", "player Owner", "none", 0, "an /advice answer needs to know the answer", "Owner",
                code: AutonomousChatWhy.Codes.NoAnswerHere);
            AutonomousChatWhy.Record(null, "faction", "player Owner", "said", "fact level", 3, "said", "im 42");
            string held = Rows("CHAT_HELD").Single();
            Assert.Multiple(() =>
            {
                Assert.That(Field(held, "outcome"), Is.EqualTo("silent"));
                Assert.That(Field(held, "code"), Is.EqualTo(AutonomousChatWhy.Codes.NoAnswerHere));
                Assert.That(Field(held, "to"), Is.EqualTo("Owner"));
                Assert.That(Field(held, "reason"), Does.Contain("needs to know"));
            });
        }

        [Test]
        public void A_burst_of_one_system_message_is_one_SCREEN_BURST_row()
        {
            var viewer = new object();
            for (int i = 0; i < 10; i++)
                ScreenBursts.Note(viewer, "Owner", eRealm.Albion, (i % 2 == 0 ? "XxEvilxX" : "Marelle") + "'s weapon returns to normal.", 0x19, 1_000 + i * 100);
            ScreenBursts.Note(viewer, "Owner", eRealm.Albion, "You feel better.", 0, 10_000);
            string burst = Rows("SCREEN_BURST").Single();
            Assert.Multiple(() =>
            {
                Assert.That(Field(burst, "n"), Is.EqualTo("10"));
                Assert.That(Field(burst, "key"), Is.EqualTo("*'s weapon returns to normal."));
                Assert.That(Field(burst, "to"), Is.EqualTo("Owner"));
                Assert.That(Field(burst, "window"), Is.EqualTo("900ms"));
            });
            // Fewer than the threshold, or spread out, is no burst.
            var calm = new object();
            for (int i = 0; i < 10; i++)
                ScreenBursts.Note(calm, "Owner", eRealm.Albion, "You feel better.", 0, 100_000 + i * 5_000);
            ScreenBursts.Note(calm, "Owner", eRealm.Albion, "x", 0, 200_000);
            Assert.That(Rows("SCREEN_BURST"), Has.Count.EqualTo(1));
        }

        [Test]
        public void A_burst_key_ignores_numbers_and_whose_it_is()
        {
            Assert.That(ScreenBursts.KeyOf("You gain 1234 experience points."), Is.EqualTo(ScreenBursts.KeyOf("You gain 56 experience points.")));
            Assert.That(ScreenBursts.KeyOf("Bob's weapon returns to normal."), Is.EqualTo(ScreenBursts.KeyOf("Marelle's weapon returns to normal.")));
            Assert.That(ScreenBursts.KeyOf("Your agility returns to normal."), Is.Not.EqualTo(ScreenBursts.KeyOf("Your vision returns to normal.")));
            Assert.That(ScreenBursts.Counted(DOL.GS.PacketHandler.eChatType.CT_SpellExpires), Is.True);
            Assert.That(ScreenBursts.Counted(DOL.GS.PacketHandler.eChatType.CT_Broadcast), Is.False, "chat is the gate's, not a screen burst");
            Assert.That(ScreenBursts.Counted(DOL.GS.PacketHandler.eChatType.CT_YouHit), Is.False, "every fight repeats its combat lines");
        }

        [Test]
        public void Odd_marks_the_last_lines_the_player_saw()
        {
            Player player = RealPlayer();
            typeof(GameObject).GetField("m_name", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, "Owner");
            for (int i = 0; i < 5; i++)
                BotSayGate.Say(new BotSayGate.Utterance("tell", "Marelle", eRealm.Albion, "line " + i, "Owner") { Listeners = [player] }, text => text);
            BotSayGate.Say(new BotSayGate.Utterance("tell", "Marelle", eRealm.Albion, "to someone else", "Other"), text => text);
            (long seq, long[] marked) = BotSayGate.Flag(player, "that was a laugh to a question", 3);
            long[] mine = Rows("CHAT_SAY").Where(row => Field(row, "to") == "Owner").Select(row => long.Parse(Field(row, "seq"))).ToArray();
            Assert.That(marked, Is.EqualTo(mine.TakeLast(3).ToArray()));
            string flag = Rows("CHAT_FLAG").Single();
            Assert.Multiple(() =>
            {
                Assert.That(long.Parse(Field(flag, "seq")), Is.EqualTo(seq));
                Assert.That(Field(flag, "at"), Is.EqualTo(mine[^1].ToString()));
                Assert.That(Field(flag, "lines"), Is.EqualTo(string.Join(",", mine.TakeLast(3))));
                Assert.That(Field(flag, "ch"), Is.EqualTo("tell"));
                Assert.That(Field(flag, "note"), Is.EqualTo("that was a laugh to a question"));
            });
        }

        [Test]
        public void Odd_is_a_player_command()
        {
            var attribute = typeof(OddCommandHandler).GetCustomAttribute<CmdAttribute>();
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute!.Cmd, Is.EqualTo("&odd"));
            Assert.That(attribute.Level, Is.EqualTo((uint)ePrivLevel.Player));
        }

        // ---- Every exit goes through the gate --------------------------------------------------------------------------

        private static string GameServerSource =>
            Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "GameServer"));

        /// <summary>The 17 wire exits (issue #475, structure.md finding 1) plus the bot emote text (#487), by file: the gate calls each file holds.</summary>
        private static readonly Dictionary<string, int> Exits = new()
        {
            ["bots/GameBot.cs"] = 3,
            ["bots/autonomous/AutonomousBotChatCoordinator.cs"] = 3,
            ["bots/autonomous/AutonomousBotGroupChatter.cs"] = 1,
            ["bots/autonomous/AutonomousLivingServer.cs"] = 2,
            ["bots/autonomous/EpicEncounterTactics.RaidCalls.cs"] = 1,
            ["bots/autonomous/NoLiferBuffBots.cs"] = 1,
            ["mode165/CompanionChat165.cs"] = 1,
            ["bots/autonomous/AutonomousGoldSpammers.cs"] = 1,
            ["bots/autonomous/crafting/BotCrafterVoice.cs"] = 1,
            ["bots/autonomous/AutonomousChatBroadcast.cs"] = 1,
            ["bots/autonomous/AutonomousBotGuildSocial.cs"] = 1,
            ["bots/autonomous/AutonomousBotGuildAlliances.cs"] = 1,
            ["bots/autonomous/AutonomousRaidLeeroy.cs"] = 1,
        };

        [Test]
        public void No_bot_line_is_recorded_outside_the_gate()
        {
            string root = GameServerSource;
            var outside = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.EndsWith("BotSayGate.cs", StringComparison.Ordinal) && !file.EndsWith("BotChatTranscript.cs", StringComparison.Ordinal))
                .Where(file => File.ReadAllText(file).Contains("BotChatTranscript.Bot(", StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(root, file)).ToList();
            Assert.That(outside, Is.Empty, "a bot line leaves through BotSayGate.Say, which records it");
            int exits = 0;
            foreach ((string file, int count) in Exits)
            {
                int found = Regex.Matches(File.ReadAllText(Path.Combine(root, file)), @"BotSayGate\.Say\(").Count;
                Assert.That(found, Is.EqualTo(count), file);
                exits += found;
            }
            Assert.That(exits, Is.EqualTo(18));
        }
    }
}
