using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot chat audit 2026-11-18, companions, buffbots and system text (E7). A hired companion talked in two voices (build
    /// b1b6846, 11/18 02:26 and 07:28: Pannax said "Say the word and I will follow" in /say and "heya, thx for the invite" in
    /// /group); the owner chose one (2026-11-18 09:14: the same chat framework as any gamebot). Its own lines promised a
    /// silence they didn't keep and warned of danger in Camelot; and the transcript could not say which kind of bot spoke. The same world-less gamebots as the duty-lease tests (DutyLeaseTestKit). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_ChatAuditCompanionsSystemText : DutyLeaseFixture
    {
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [SetUp]
        public void Fresh() => Forget();

        [TearDown]
        public void Clean() => Forget();

        private static void Forget()
        {
            typeof(HiredCompanions165).GetMethod("ClearForTests", HiddenStatic)!.Invoke(null, null);
            typeof(CompanionChat165).GetMethod("ClearForTests", HiddenStatic)!.Invoke(null, null);
        }

        private static Bot Hired(Bot bot, string options = "")
        {
            typeof(HiredCompanions165).GetMethod("RegisterForTests", HiddenStatic)!.Invoke(null,
                [new OfflineHiredCompanionRecord { BotId = bot.DatabaseID, OwnerCharacterID = "owner-1", OwnerName = "Owner", Options = options }]);
            return bot;
        }

        private static Bot Formal(Bot bot) => Hired(bot, "speech=1");

        private static bool IsSpeaker(GameBot bot) =>
            (bool)typeof(AutonomousLivingServer).GetMethod("IsSpeaker", HiddenStatic)!.Invoke(null, [bot])!;

        // Owner 2026-11-18 09:14 (reversing TEXT-001/002): a hired companion has the same chat framework as any gamebot, in one
        // voice. With its Speech option on Formal it keeps to its True 1.65 companion lines instead, again one voice.
        [Test]
        public void A_hired_companion_is_a_speaker_like_any_gamebot_unless_it_talks_formally()
        {
            Bot gamebot = Gamebot();
            Bot companion = Hired(Gamebot());
            Bot formal = Formal(Gamebot());
            Bot helper = Companion();
            Assert.Multiple(() =>
            {
                Assert.That(IsSpeaker(gamebot), Is.True);
                Assert.That(IsSpeaker(companion), Is.True, "grats, memory, group talk, rez thanks and tells, as any gamebot");
                Assert.That(CompanionChat165.IsFormal(companion), Is.False, "the default speech talks like a player");
                Assert.That(CompanionChat165.IsFormal(formal), Is.True);
                Assert.That(IsSpeaker(formal), Is.False, "a formal companion says only its own lines");
                Assert.That(CompanionChat165.IsFormal(gamebot), Is.False, "only a hired companion has the option");
                Assert.That(IsSpeaker(helper), Is.False, "/spawn helpers are unchanged");
                Assert.That(CompanionOptions165.Option(CompanionOptions165.KindSpeech)?.Key, Is.EqualTo("speech"));
            });
        }

        // TEXT-051 (10): the transcript names the bot kind.
        [Test]
        public void The_transcript_names_the_kind_of_bot()
        {
            Bot buffbot = Gamebot();
            typeof(GameBot).GetProperty(nameof(GameBot.BuffBotMainId))!.SetValue(buffbot, 42L);
            Assert.Multiple(() =>
            {
                Assert.That(BotChatTranscript.KindOf(Gamebot()), Is.EqualTo("gamebot"));
                Assert.That(BotChatTranscript.KindOf(Hired(Gamebot())), Is.EqualTo("companion"));
                Assert.That(BotChatTranscript.KindOf(Companion()), Is.EqualTo("helper"));
                Assert.That(BotChatTranscript.KindOf(buffbot), Is.EqualTo("buffbot"));
                Assert.That(BotChatTranscript.KindOf(null), Is.EqualTo("?"));
            });
        }

        private static readonly Regex Formalish = new(@"\b(I am|I will|Shall|you wish|Well fought|My power)\b|[A-Z][a-z]+ [a-z]+\.$");

        private static CompanionChat165.AmbientFacts Idle(string zone, bool inTown, string last = null) =>
            new("Testrunner", 5, false, 100, 100, false, zone, inTown, null, last);

        // TEXT-007, LOG-021: "Keep your eyes open here in City of Camelot." (11/18 07:34:31 and 07:39:05).
        [Test]
        public void A_companion_warns_of_no_danger_in_a_capital_or_town_and_names_a_city_plainly()
        {
            var random = new Random(7);
            for (int i = 0; i < 300; i++)
            {
                string line = CompanionChat165.AmbientLine(Idle("City of Camelot", true), random);
                Assert.That(line, Does.Not.Contain("eyes open").And.Not.Contain("dont like it").And.Not.Contain("City of"), line);
                string formal = CompanionChat165.AmbientLine(Idle("City of Camelot", true), random, formal: true);
                Assert.That(formal, Does.Not.Contain("eyes open").And.Not.Contain("do not trust"), formal);
            }
            Assert.That(CompanionChat165.ZoneName("City of Camelot"), Is.EqualTo("Camelot"));
            Assert.That(CompanionChat165.ZoneName("Isle of Glass"), Is.EqualTo("Isle of Glass"));
            Assert.That(CompanionChat165.ZoneName("  "), Is.Null);
            var outside = Enumerable.Range(0, 300).Select(_ => CompanionChat165.AmbientLine(Idle("Isle of Glass", false), random)).ToHashSet();
            Assert.That(outside, Does.Contain("eyes open, Isle of Glass can get nasty"));
            Assert.That(outside.Where(line => Formalish.IsMatch(line)), Is.Empty, "the default voice is a 2003 player's, not the formal companion's");
        }

        // TEXT-008, LOG-021: the idle default fell through to "Say the word and I will follow" (seen as the idle line three times).
        [Test]
        public void A_companion_has_more_than_five_idle_lines_and_never_repeats_its_last()
        {
            var random = new Random(11);
            var seen = new HashSet<string>();
            string last = null;
            for (int i = 0; i < 400; i++)
            {
                string line = CompanionChat165.AmbientLine(Idle("Isle of Glass", false, last), random);
                Assert.That(line, Is.Not.EqualTo(last), "never the line it said last");
                seen.Add(line);
                last = line;
            }
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(12), string.Join(" | ", seen));
            Assert.That(seen, Has.All.Not.Contain("{"), "no slot left unfilled");
        }

        // TEXT-006: "As you wish. I will keep quiet." stored nothing, and "don't stop", "shut the gate" and "healthy" triggered it.
        [Test]
        public void Told_to_keep_quiet_a_companion_keeps_quiet_until_told_to_speak()
        {
            Bot companion = Hired(Gamebot());
            Player owner = RealPlayer();
            var random = new Random(3);
            foreach (string notQuiet in new[] { "don't stop", "shut the gate behind us", "you look healthy" })
            {
                Assert.That(CompanionChat165.Reply(companion, owner, notQuiet, random), Does.Not.Contain("quiet"), notQuiet);
                Assert.That(CompanionChat165.IsQuiet(companion.DatabaseID), Is.False, notQuiet);
            }
            Assert.That(CompanionChat165.Reply(companion, owner, "be quiet please", random), Is.EqualTo("k, quiet till u tell me to talk again"));
            Assert.That(CompanionChat165.IsQuiet(companion.DatabaseID), Is.True, "the promise is kept");
            Assert.That(CompanionChat165.TryAmbient(companion), Is.False, "no ambient line while quiet");
            Assert.That(CompanionChat165.Reply(companion, owner, "you can talk again", random), Does.StartWith("k, talking again"));
            Assert.That(CompanionChat165.IsQuiet(companion.DatabaseID), Is.False);
            Bot formal = Formal(Gamebot());
            Assert.That(CompanionChat165.Reply(formal, owner, "be quiet", random), Is.EqualTo("As you wish. I will keep quiet until you tell me to speak."));
            Assert.That(CompanionChat165.Reply(formal, owner, "speak", random), Does.StartWith("Very well"));
        }

        // TEXT-005: /personality on a companion shows its personality and voice, as for any gamebot.
        [Test]
        public void Personality_on_a_companion_shows_its_voice()
        {
            Assert.That(PersonalityCommandHandler.HiredCompanionText("Pannax", "Socialite (curiosity 3)", "terse", false, true),
                Does.StartWith("Pannax (hired companion): Socialite (curiosity 3)").And.EndWith("writes terse"));
            Assert.That(PersonalityCommandHandler.HiredCompanionText("Pannax", "Leveler", "terse", true, true), Does.EndWith("talks formally (their Speech option)"));
            Assert.That(CompanionChat165.OwnCompanionNamed(null, "Pannax"), Is.Null);
        }

        // Each reply intent has a player-voice answer, and none in the formal register.
        [Test]
        public void Every_reply_has_a_player_voice_and_a_formal_one()
        {
            var random = new Random(5);
            foreach (eAutonomousChatIntent intent in Enum.GetValues<eAutonomousChatIntent>())
            {
                string casual = CompanionChat165.ReplyTo(intent, "Testrunner", random, false);
                string formal = CompanionChat165.ReplyTo(intent, "Testrunner", random, true);
                Assert.That(casual, Is.EqualTo(casual.ToLowerInvariant()).Or.Contain("Testrunner"), intent.ToString());
                Assert.That(Formalish.IsMatch(casual), Is.False, casual);
                Assert.That(formal, Is.Not.EqualTo(casual), intent.ToString());
            }
        }

        // E1's conversation memory (#350) for companions: the companion with the floor answers a line that names nobody.
        [Test]
        public void The_companion_with_the_floor_answers_the_next_unnamed_line()
        {
            ChatConversations.Live.Clear();
            try
            {
                Bot first = Hired(Gamebot());
                Bot second = Hired(Gamebot());
                var near = new GameBot[] { first, second };
                const string room = "say:1";
                Assert.That(ChatConversations.Live.FloorHolder("Testrunner", room, 1_000), Is.Zero, "nobody has the floor yet");
                CompanionChat165.NoteSaid("Testrunner", room, second, "eyes open, Isle of Glass can get nasty", 1_000);
                long floor = ChatConversations.Live.FloorHolder("Testrunner", room, 2_000);
                Assert.That(floor, Is.EqualTo(second.DatabaseID), "its line gave it the floor");
                ChatConversations.Live.NotePlayer("Testrunner", room, "how far is it", 2_000);
                Assert.That(ChatConversations.Live.With("Testrunner", second.DatabaseID, 2_000)?.Turns.Count, Is.EqualTo(2),
                    "the player's next line is a turn of that companion's conversation");
                for (int seed = 0; seed < 20; seed++)
                    Assert.That(CompanionChat165.ChooseSpeaker(near, null, floor, new Random(seed)), Is.SameAs(second), "always the floor");
                Assert.That(CompanionChat165.ChooseSpeaker(near, first, floor, new Random(1)), Is.SameAs(first), "a name beats the floor");
                CompanionChat165.Reply(second, RealPlayer(), "be quiet", new Random(1));
                Assert.That(Enumerable.Range(0, 40).Select(seed => CompanionChat165.ChooseSpeaker(near, null, floor, new Random(seed))),
                    Has.None.SameAs(second), "a quiet companion gives up the floor");
            }
            finally
            {
                ChatConversations.Live.Clear();
            }
        }

        // TEXT-036: the personality's name as players read it.
        [Test]
        public void The_no_lifer_reads_no_lifer()
        {
            Assert.That(AutonomousBotPersonality.DisplayName(BotPersonalityType.NoLifer), Is.EqualTo("No Lifer"));
            Assert.That(AutonomousBotPersonality.DisplayName(BotPersonalityType.Daredevil), Is.EqualTo("Daredevil"));
        }

        // TEXT-041: a server-only setting is not sent to the launcher.
        [Test]
        public void Lstest_points_a_server_only_setting_away_from_the_launcher()
        {
            Assert.That(LivingServerTestCommandHandler.Off(true, "bot_living_tales"), Is.Null);
            Assert.That(LivingServerTestCommandHandler.Off(false, "bot_living_tales"),
                Is.EqualTo("That part is off (bot_living_tales); it is a server setting, not in the launcher."));
            Assert.That(LivingServerTestCommandHandler.Off(false, "bot_living_characters"), Does.Contain("launcher's Living Server tab"));
        }

        // DESIGN-022: the code's fallback is the shipped exponent.
        [Test]
        public void The_chat_budget_falls_back_to_the_shipped_population_exponent()
        {
            Assert.That(AutonomousChatBudget.Parse("{}").PopulationExponent, Is.EqualTo(0.65));
            Assert.That(AutonomousChatBudget.Current.PopulationExponent, Is.EqualTo(0.65), "chat-budget.json");
        }

        // TEXT-048: one text per setting. The launcher writes its text into the setting's row, so the server's and the
        // launcher's must be the same, and neither carries work-package notes, dates or "today's".
        [Test]
        public void Every_launcher_setting_text_is_the_servers_own()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source", "tools", "DAoCLivingServer.Launcher", "SocialExpansionSettings.cs")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Launcher source not found beside the tests.");
            string launcher = File.ReadAllText(Path.Combine(dir, "source", "tools", "DAoCLivingServer.Launcher", "SocialExpansionSettings.cs"));
            var server = new[] { typeof(DOL.GS.ServerProperties.Properties), typeof(CraftingEconomyProperties), typeof(HiredCompanionsMode) }
                .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Select(field => field.GetCustomAttribute<ServerPropertyAttribute>())
                .Where(attribute => attribute != null)
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Description);
            var rows = Regex.Matches(launcher, @"\(\s*""([a-z0-9_]+)""\s*,\s*""[a-z]+""\s*,\s*""((?:[^""\\]|\\.)*)""\s*\)");
            Assert.That(rows.Count, Is.GreaterThan(50), "the parser found the launcher's settings");
            var stale = new Regex(@"\bWP\d|owner 20\d\d|today's|since (Beta|Living Server) 1\.|Set in the launcher");
            Assert.Multiple(() =>
            {
                foreach (Match row in rows)
                {
                    string key = row.Groups[1].Value, text = Regex.Unescape(row.Groups[2].Value);
                    Assert.That(server.ContainsKey(key), Is.True, key + " is a server setting");
                    if (!server.TryGetValue(key, out string description))
                        continue;
                    Assert.That(text, Is.EqualTo(description), key);
                    Assert.That(stale.IsMatch(description), Is.False, key + ": " + description);
                }
            });
        }
    }
}
