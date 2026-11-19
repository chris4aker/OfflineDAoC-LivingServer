using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;
using R = DOL.GS.ChatPromiseReason;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests
{
    /// <summary>
    /// The chat audit's code follow-ups (F1, 2026-11-18): the promise gate wired into every line that says "omw", a ding told
    /// for bots under 50, the player's line passed to the memory reply, no holiday answer to a plain "hi", the siege stages said
    /// by topic, the third realm's siege timer, word-boundary channel caps, no "lol" habit in a no-laugh beat, no towers, the
    /// reaction lists together, /lstest deed drowned. Tests only: none of this has been seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_ChatFollowUp12
    {
        private static ChatPromiseTarget Keep() =>
            new(ChatPromiseTargetKind.Keep, "Caer Benowyc", 1, 100, 100, 0, 12, eRealm.Albion, Frontier: true);

        private static ChatPromise Make(ChatPromiseBook book, object bot, long now = 0) =>
            book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, now);

        private static ChatPromiseBook.Progress On(bool arrived = false) => new(true, true, arrived, null, false);

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

        private static string Bots(string file) => Source("source", "server", "GameServer", "bots", "autonomous", file);

        // ---- 1. the promise gate: a promise whose line is never said ends quietly ----

        [Test]
        public void APromiseWhoseLineIsNeverSaidEndsQuietlyAsUnsaid()
        {
            var lines = new List<string>();
            var book = new ChatPromiseBook(lines.Add);
            object bot = new();
            ChatPromise promise = Make(book, bot);
            Assert.That(promise.Unsaid, Is.True, "made when the line is chosen");
            Assert.That(book.Check(bot, ChatPromiseRules.SayWithinSeconds * 1000L - 1, On(arrived: true)), Is.Null,
                "not kept before its line is out");
            Assert.That(book.Check(bot, ChatPromiseRules.SayWithinSeconds * 1000L, On()), Is.EqualTo("broken"));
            Assert.That(lines[^1], Does.StartWith("CHAT_PROMISE broken bot=\"Bot1\" id=7 promise=1 reason=Alarm mode=RvrObjective " +
                                                  "target=\"Caer Benowyc\" why=unsaid after=150s"));
            Assert.That(book.Of(bot), Is.Null);
            Assert.That(ChatPromiseRules.SayWithinSeconds, Is.LessThan(ChatPromiseRules.MinDeadlineSeconds), "never judged a timeout first");
        }

        [Test]
        public void APromiseStandsOnceItsLineIsSaidByObjectOrByName()
        {
            var book = new ChatPromiseBook(_ => { });
            object bot = new();
            ChatPromise promise = Make(book, bot);
            Assert.That(book.NoteSaid(bot, "gz on 50"), Is.False, "another line of the bot's is not the promise's");
            Assert.That(book.NoteSaid(bot, "omw!!"), Is.True);
            Assert.That(promise.Unsaid, Is.False);
            Assert.That(book.Check(bot, 400_000 - 1, On(arrived: true)), Is.EqualTo("kept"));

            object other = new();
            ChatPromise byName = book.Make(other, "Bot2", 8, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, 0);
            Assert.That(book.NoteSaid("Bot2", eRealm.Midgard, "omw"), Is.False, "the realm is part of who said it");
            Assert.That(book.NoteSaid("Bot2", eRealm.Albion, "Comng, hold the door"), Is.True, "one typing slip is still the line");
            Assert.That(byName.Unsaid, Is.False);
            Assert.That(book.Unsaid(other, byName, 1_000), Is.False, "a said promise is not taken back");
        }

        [Test]
        public void TheTypedComingWordsAreReadWithOneSlip()
        {
            foreach (string line in new[] { "omw", "comng now", "portign to bled", "haeding there", "coming" })
                Assert.That(ChatPromiseRules.SaysComingAsTyped(line), Is.True, line);
            foreach (string line in new[] { "gz", "how many?", "going home", "pointing at it", "" })
                Assert.That(ChatPromiseRules.SaysComingAsTyped(line), Is.False, line);
        }

        [Test]
        public void ThreadLinesThatSayOmwGoThroughTheGateWithTheirOwnNoPromiseLine()
        {
            Assert.That(AutonomousChatFieldTalk.ThreadPromise(new AutonomousChatFieldTalk.Said("inc bled", "inc", ["omw"]), 1),
                Is.EqualTo(((R?)R.Alarm, "how many?")));
            Assert.That(AutonomousChatFieldTalk.ThreadPromise(new AutonomousChatFieldTalk.Said("anyone in emain?", "rvr", ["on my way"]), 1),
                Is.EqualTo(((R?)R.RealmPoints, "not me, maybe later")));
            Assert.That(AutonomousChatFieldTalk.ThreadPromise(new AutonomousChatFieldTalk.Said("any grps out?", "rvr", ["at bled"]), 0),
                Is.EqualTo(((R?)R.RealmPoints, "k ty")), "the opener's closing \"k omw\" becomes \"k ty\"");
            Assert.That(AutonomousChatFieldTalk.ThreadPromise(new AutonomousChatFieldTalk.Said("need rez pls", "death", ["omw"]), 1),
                Is.EqualTo(((R?)R.Rez, "cant, sry, too far")));
            Assert.That(AutonomousChatFieldTalk.ThreadPromise(new AutonomousChatFieldTalk.Said("all to beno", "rally", ["omw"]), 2).Fallback,
                Is.Null, "a third bot's chime is left out");
            Assert.That(AutonomousChatFieldTalk.ThreadPromise(new AutonomousChatFieldTalk.Said("lfg 30", "lfg", ["me"]), 1).Reason, Is.Null);
        }

        [Test]
        public void TheFieldTalkLinesNameTheirKeepForTheGate()
        {
            string code = Bots("AutonomousChatFieldTalk.cs");
            Assert.That(Regex.Matches(code, @"with \{ PromisePlace = full \}").Count, Is.EqualTo(2), "inc and rally name the full keep");
            Assert.That(code, Does.Not.Contain("\"ty, omw\""), "a bot told an xp spot doesn't say omw to it");
        }

        // ---- 1. where the gate is wired (tests that need the gamebot double) are in UT_ChatFollowUpGate below ----

        [Test]
        public void AWrittenReplyThatSaysOmwStandsOnlyWhenTheGateSendsTheBot()
        {
            var asked = new List<R>();
            string Kept(string text, bool written, bool goes, string other = "how many?") =>
                AutonomousBotChatCoordinator.WrittenReplyKept(text, written, "inc 8 mids at bled", r =>
                {
                    asked.Add(r);
                    return goes;
                }, () => other);
            Assert.That(Kept("omw", written: true, goes: false), Is.EqualTo("how many?"));
            Assert.That(asked, Is.EqualTo(new[] { R.Alarm }), "asked once, as an alarm");
            Assert.That(Kept("omw", written: true, goes: true), Is.EqualTo("omw"));
            Assert.That(Kept("omw", written: true, goes: false, other: "coming"), Is.Null, "the other reply may not promise either");
            Assert.That(Kept("ugh, again", written: true, goes: false), Is.EqualTo("ugh, again"), "no promise, no question");
            asked.Clear();
            Assert.That(Kept("on it", written: false, goes: false), Is.EqualTo("on it"), "a situation's own reply asked the gate itself");
            Assert.That(asked, Is.Empty);
            Assert.That(AutonomousBotChatCoordinator.Written("memory greeting", false), Is.True);
            Assert.That(AutonomousBotChatCoordinator.Written("intent alarm", false), Is.True);
            Assert.That(AutonomousBotChatCoordinator.Written("context inc", false), Is.False);
            Assert.That(AutonomousBotChatCoordinator.Written("live ask_whereyou", false), Is.False);
            Assert.That(AutonomousBotChatCoordinator.Written("context inc", true), Is.True);
        }

        [Test]
        public void AWrittenReplyThatSaysOmwIsPickedOnlyWhereTheCallerGatesIt()
        {
            VC.Line omw = Greeting("omw, how many?", []), calm = Greeting("how many?", []);
            Assert.That(AutonomousBotVoices.PromiseAllowed(omw), Is.False, "a guild or group reply never says it");
            Assert.That(AutonomousBotVoices.PromiseAllowed(calm), Is.True);
            using (AutonomousBotVoices.PromiseLines())
            {
                Assert.That(AutonomousBotVoices.PromiseAllowed(omw), Is.True, "the gated replies may pick it");
                using (AutonomousBotVoices.PromiseLines())
                    Assert.That(AutonomousBotVoices.PromiseAllowed(omw), Is.True);
                Assert.That(AutonomousBotVoices.PromiseAllowed(omw), Is.True, "still inside the outer scope");
            }
            Assert.That(AutonomousBotVoices.PromiseAllowed(omw), Is.False, "closed again");
            string voices = Bots("AutonomousBotVoices.cs");
            Assert.That(Regex.Matches(voices, @"PromiseAllowed\(line\)").Count, Is.GreaterThanOrEqualTo(2), "TryLine's replies and closings");
            Assert.That(voices, Does.Contain("Filter = kind is \"reply\" or \"closing\" ? filter == null ? (Func<VC.Line, bool>)PromiseAllowed"),
                "TryEventLine's too (the memory replies)");
        }

        [Test]
        public void APlayersLineIsReadForWhyTheBotWouldCome()
        {
            Assert.That(AutonomousBotChatCoordinator.PromiseReasonFor(eAutonomousChatIntent.Rez, "rez pls"), Is.EqualTo(R.Rez));
            Assert.That(AutonomousBotChatCoordinator.PromiseReasonFor(eAutonomousChatIntent.Alarm, "inc bled"), Is.EqualTo(R.Alarm));
            Assert.That(AutonomousBotChatCoordinator.PromiseReasonFor(eAutonomousChatIntent.RvR, "free rps at mmg"), Is.EqualTo(R.RealmPoints));
            Assert.That(AutonomousBotChatCoordinator.PromiseReasonFor(eAutonomousChatIntent.Help, "need heals"), Is.EqualTo(R.Heal));
            Assert.That(AutonomousBotChatCoordinator.PromiseReasonFor(eAutonomousChatIntent.Unknown, "relic raid at 9 tonight"), Is.EqualTo(R.RaidTime));
            Assert.That(AutonomousBotChatCoordinator.PromiseReasonFor(eAutonomousChatIntent.Help, "help this mob is killing me"), Is.EqualTo(R.Help));
        }

        [Test]
        public void ANewsReactionPromisesAnAlarmAtTheNewsKeepOrARaidsTime()
        {
            Assert.That(AutonomousChatBursts.PromiseOf("news siege_defend", new Dictionary<string, string> { ["keep"] = "bled" }),
                Is.EqualTo((R.Alarm, "bled")));
            Assert.That(AutonomousChatBursts.PromiseOf("news raid_recruit", null), Is.EqualTo((R.RaidTime, (string)null)));
        }

        [Test]
        public void TheNewsReactionsAndTheAlarmRepliesCommitWhenTheLineIsSaid()
        {
            string bursts = Bots("AutonomousChatBursts.cs");
            Assert.That(bursts, Does.Contain("ChatPromises.CanPromiseCome(candidate, place, reason, said, first)"), "chosen only when it could go");
            Assert.That(bursts, Does.Contain("ChatPromises.SayPromised(bot, line.Text, place, reason, said, null, first,"), "promised when said");
            string coordinator = Bots("AutonomousBotChatCoordinator.cs");
            Assert.That(coordinator, Does.Not.Contain("ChatPromises.TryPromiseCome(responder, AutonomousChatQuestions.PlaceNamed(opening"),
                "an alarm reply between bots no longer promises when it is only chosen");
            Assert.That(coordinator, Does.Contain("ChatPromises.SayPromised(speaker, text, alarmPlace, comeFor, opening,"));
            Assert.That(coordinator, Does.Contain("if (!SayThreadLine(bot, line.Text, promises, line.Who, lastSaid,"), "field threads");
            string transcript = Bots("BotChatTranscript.cs");
            Assert.That(transcript, Does.Contain("ChatPromises.NoteSaid("), "every bot line out confirms its promise");
            Assert.That(Source("source", "server", "GameServer", "bots", "GameBot.cs"), Does.Contain("ChatPromises.NoteSaid(this, str)"));
        }

        /// <summary>
        /// Every string literal in the bot chat code that says the speaker is coming (<see cref="ChatPromiseRules.SaysComing"/>)
        /// is marked with why it may: "// promise: gated" (it goes through the promise gate), "// promise: true" (it says what the
        /// bot really does: its own walk back, a rez BotBrain claimed), "// promise: none" (no promise: about someone else, a
        /// question, an order acknowledged), or "// chat-check: input" (words read in a player's line). An unmarked one fails.
        /// </summary>
        [Test]
        public void NoLineThatSaysOmwBypassesTheGate()
        {
            string root = Path.Combine(RepoRoot(), "source", "server", "GameServer");
            // Files with no chat lines that say "coming" (logs and reasons the raid code reads), and the detector itself.
            string[] skip =
            [
                "ChatPromises.cs", "EraBotNameWords.cs", "EncounterDisplacementRecovery.cs", "RealmRaidDungeonRoute.cs",
                "AutonomousDungeonTraversal.cs", "RvrRoamCircuit.cs",
                "RaidDiscipline.cs",         // raid calls: "stomp coming, everyone out" is the boss's move
            ];
            var files = Directory.GetFiles(Path.Combine(root, "bots"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "mode165"), "*.cs", SearchOption.AllDirectories))
                .Where(path => !skip.Contains(Path.GetFileName(path)) && !Path.GetFileName(path).StartsWith("EpicEncounterTactics", StringComparison.Ordinal));
            var literal = new Regex("\\$?@?\\$?\"((?:[^\"\\\\]|\\\\.)*)\"");
            var unmarked = new List<string>();
            foreach (string path in files)
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i], trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || line.Contains("Log.") || line.Contains("Exception(") ||
                        line.Contains("// promise:") || line.Contains("chat-check: input"))
                        continue;
                    // A literal the marker on its statement's last line covers (a Pick(...) spread over lines).
                    if (Enumerable.Range(i + 1, Math.Min(3, lines.Length - i - 1)).Any(j => lines[j].Contains("// promise:") &&
                            !lines[i].TrimEnd().EndsWith(';')))
                        continue;
                    foreach (Match m in literal.Matches(CodeOf(line)))
                    {
                        string text = m.Groups[1].Value;
                        if (text.StartsWith("^") || text.StartsWith("(") || text.StartsWith("\\b") || !ChatPromiseRules.SaysComing(text))
                            continue;
                        // Words the code matches inside a line (" coming ", " on it ") are reading, not saying.
                        if (text.StartsWith(" ") && text.EndsWith(" "))
                            continue;
                        unmarked.Add($"{Path.GetRelativePath(root, path)}:{i + 1}: \"{text}\"");
                    }
                }
            }
            Assert.That(unmarked, Is.Empty, "mark each with // promise: gated|true|none, or route it through ChatPromises");
        }

        /// <summary>A line without its trailing // comment (a // inside a string literal is kept).</summary>
        private static string CodeOf(string line)
        {
            bool inString = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];
                if (inString)
                {
                    if (c == '\\')
                        i++;
                    else if (c == '"')
                        inString = false;
                }
                else if (c == '"')
                    inString = true;
                else if (c == '/' && line[i + 1] == '/')
                    return line[..i];
            }
            return line;
        }

        /// <summary>
        /// E8's scenes promise nothing now (chat 1.2 follow-up: confirmed): no debate, tale or one-off scene beat says "omw",
        /// "on my way" or "ill be there". A storyline's beats may speak of a later meeting in its own arc ("i'll be there
        /// first. like always"), which is the arc's plot, not a walk the bot owes now.
        /// </summary>
        [Test]
        public void NoSceneBeatOutsideAStorylineSaysTheBotIsComing()
        {
            string json = Source("source", "server", "GameServer", "bots", "autonomous", "data", "chat", "corpus.json");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            string[] strong = [" omw ", " otw ", " on my way ", " ill be there ", " i will be there ", " be right there ", " count me in ",
                " heading there ", " heading over ", " running over ", " im coming ", " coming now "];
            var said = new List<string>();
            foreach (var scene in doc.RootElement.GetProperty("scenes").EnumerateArray())
            {
                string id = scene.GetProperty("id").GetString() ?? string.Empty;
                if (id.StartsWith("story_", StringComparison.Ordinal))
                    continue;
                foreach (var beat in scene.GetProperty("beats").EnumerateArray())
                    if (beat.TryGetProperty("variants", out var variants))
                        foreach (var variant in variants.EnumerateArray())
                        {
                            string text = variant.GetString() ?? string.Empty;
                            string t = " " + new string(text.ToLowerInvariant().Replace("'", string.Empty)
                                .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray()) + " ";
                            if (strong.Any(t.Contains))
                                said.Add(id + ": " + text);
                        }
            }
            Assert.That(said, Is.Empty);
        }

        // ---- 2. a ding told for bots under 50 ----

        [Test]
        public void ALevelUpUnder50IsADingAnd50IsAStory()
        {
            Assert.That(AutonomousBotVoices.LevelUpTold(31), Is.EqualTo("ding"));
            Assert.That(AutonomousBotVoices.LevelUpTold(49), Is.EqualTo("ding"));
            Assert.That(AutonomousBotVoices.LevelUpTold(50), Is.EqualTo("story"));
            Assert.That(AutonomousBotVoices.LevelUpTold(1), Is.Null);
            string voices = Bots("AutonomousBotVoices.cs");
            string levelUp = voices[voices.IndexOf("public static void OnBotLevelUp", StringComparison.Ordinal)..];
            levelUp = levelUp[..levelUp.IndexOf("public static string LevelUpTold", StringComparison.Ordinal)];
            Assert.That(levelUp, Does.Contain("AutonomousBotChatCoordinator.TellDing(bot);"));
        }

        // ---- 3. the player's line at both TryKnownReply call sites ----

        [Test]
        public void TheMemoryReplyIsGivenThePlayersLineAtBothCallSites()
        {
            string coordinator = Bots("AutonomousBotChatCoordinator.cs");
            var calls = Regex.Matches(coordinator, @"AutonomousLivingServer\.TryKnownReply\((?<args>[^;]*?)out string known(?<rest>[^)]*)\)");
            Assert.That(calls.Count, Is.EqualTo(2), "KnownChoice and VoicedChoiceCore");
            foreach (Match call in calls)
                Assert.That(call.Groups["rest"].Value.Trim(), Is.EqualTo(", incoming"), call.Value);
        }

        // ---- 4. no holiday answer to a plain "hi" ----

        private static VC.Line Greeting(string text, string[] needs, string[] answers = null) =>
            new("t", "reply", "greeting", ["*"], "*", [], 1, 50, needs, "greeting", null, 1, text) { Answers = answers ?? [] };

        [Test]
        public void APlainHiGetsNoHolidayAnswer()
        {
            Assert.That(AutonomousBotVoices.PlainGreetingFits(Greeting("happy halloween to you too", ["holiday_halloween"]), asked: false), Is.False);
            Assert.That(AutonomousBotVoices.PlainGreetingFits(Greeting("happy halloween to you too", ["holiday_halloween"]), asked: true), Is.False);
            Assert.That(AutonomousBotVoices.PlainGreetingFits(Greeting("hey", []), asked: false), Is.True);
            Assert.That(AutonomousBotVoices.PlainGreetingFits(Greeting("good, you?", [], ["ask_howareyou"]), asked: false), Is.False);
            Assert.That(AutonomousBotVoices.PlainGreetingFits(Greeting("good, you?", [], ["ask_howareyou"]), asked: true), Is.True);
            Assert.That(AutonomousBotVoices.PlainGreetingFits(Greeting("evening all", ["evening"]), asked: false), Is.True);
        }

        // ---- 5. RealmEventSpoken reads E4's siege stages by topic ----

        [Test]
        public void TheSiegeStagesAreSaidByTopicWhenTheNoticeIsNotRead()
        {
            Assert.That(RealmEventSpoken.KindOfTopic("siege_muster"), Is.EqualTo("siege_gather"));
            Assert.That(RealmEventSpoken.KindOfTopic("siege_warning"), Is.EqualTo("keep_army_coming"));
            Assert.That(RealmEventSpoken.KindOfTopic("siege_attack"), Is.EqualTo("siege_advance"));
            Assert.That(RealmEventSpoken.KindOfTopic("siege_defend"), Is.EqualTo("siege_begun"));
            Assert.That(RealmEventSpoken.KindOfTopic("keep_lost"), Is.Null);
            var slots = new Dictionary<string, string> { ["keep"] = "Caer Benowyc", ["enemy"] = "Midgard" };
            const string unread = "A muster stirs near the old keep, banners rising.";
            Assert.That(RealmEventSpoken.Say(unread, slots, new Random(1)), Is.EqualTo(RealmEventSpoken.Plain(unread)), "no topic: as before");
            for (int seed = 0; seed < 20; seed++)
            {
                string muster = RealmEventSpoken.Say(unread, slots, new Random(seed), "siege_muster");
                Assert.That(muster, Does.Contain("beno").Or.Contain("benowyc"), muster);
                Assert.That(muster, Is.Not.EqualTo(RealmEventSpoken.Plain(unread)));
                string warning = RealmEventSpoken.Say(unread, slots, new Random(seed), "siege_warning");
                Assert.That(warning, Does.Contain("beno").Or.Contain("benowyc"), warning);
            }
            Assert.That(Bots("RealmEventNotices.cs"), Does.Contain("RealmEventSpoken.Say(notice.Text, notice.Slots, Random.Shared, notice.Topic)"));
        }

        // ---- 6. the siege timer: the third realm doesn't hear "Our time to take X has run out" ----

        [Test]
        public void ASiegeThatRunsOutItsTimerIsToldToEachRealmAsItsOwn()
        {
            string third = RealmEventBanter.SiegeOutcome("Dun nGed", false, true, false, eRealm.None, eRealm.Albion, attacker: false);
            Assert.That(third, Does.Not.Contain("Our time"));
            Assert.That(third, Does.StartWith("The fighting for Dun nGed has ended."));
            Assert.That(RealmEventSpoken.Read(third)?.Kind, Is.EqualTo("siege_ended"), "a bot says it as \"nged over? who won\"");
            Assert.That(RealmEventBanter.SiegeOutcome("Dun nGed", false, true, false, eRealm.None, eRealm.Midgard, attacker: true),
                Does.StartWith("Our time to take Dun nGed has run out"));
            Assert.That(RealmEventBanter.SiegeOutcome("Dun nGed", true, true, false, eRealm.None, eRealm.Hibernia, attacker: false),
                Does.StartWith("We held Dun nGed!"));
            Assert.That(Bots("AutonomousRvrEventLayer.cs"), Does.Contain("attacker: realm == active.AttackerRealm"));
        }

        // ---- 7. the coordinator's 240-character caps cut at a word ----

        [Test]
        public void AChannelLineIsCutAtAWholeWord()
        {
            string longLine = string.Join(' ', Enumerable.Repeat("alloys", 50));
            string cut = AutonomousBotChatCoordinator.ChannelCap(longLine);
            Assert.That(cut.Length, Is.LessThanOrEqualTo(240));
            Assert.That(cut, Does.EndWith("alloys..."), "no mid-word cut");
            Assert.That(AutonomousBotChatCoordinator.ChannelCap("short line"), Is.EqualTo("short line"));
            Assert.That(Bots("AutonomousBotChatCoordinator.cs"), Does.Not.Contain("safe[..240]"));
        }

        // ---- 8. a no-laugh beat takes no "lol" from the typing habit ----

        [Test]
        public void ANoLaughBeatTakesNoLolFromTheTypingHabit()
        {
            var voice = new VC.Voice("plain", 1, false, false, false, [], false, [], new Dictionary<string, string>(), [], 0, 0);
            long[] lolBots = Enumerable.Range(1, 20_000).Select(i => (long)i)
                .Where(id => AutonomousChatIdiolect.HabitsOf(id).HasFlag(AutonomousChatIdiolect.Habit.LolTail)).Take(20).ToArray();
            Assert.That(lolBots, Is.Not.Empty);
            const string beat = "yes i do, with all my heart";
            int withLaugh = 0;
            foreach (long id in lolBots)
                for (int seed = 0; seed < 30; seed++)
                {
                    Assert.That(AutonomousChatDebateSpeech.Typed(beat, voice, id, new Random(seed), noLaugh: true).Any(p => p.EndsWith(" lol")),
                        Is.False, $"bot {id}, draw {seed}");
                    if (AutonomousChatDebateSpeech.Typed(beat, voice, id, new Random(seed)).Any(p => p.EndsWith(" lol")))
                        withLaugh++;
                }
            Assert.That(withLaugh, Is.GreaterThan(0), "the habit fires on an ordinary beat (the gap the flag closes)");
            Assert.That(Bots("AutonomousBotDebates.cs"), Does.Contain("AutonomousChatDebateSpeech.Typed(filled, voice, speaker.DatabaseID, Random.Shared, noLaugh)"));
        }

        // ---- 9. no towers in the keep-claim setting ----

        [Test]
        public void TheKeepClaimSettingSaysNothingOfTowers()
        {
            string properties = Source("source", "server", "GameServer", "serverproperty", "ServerProperties.cs");
            int at = properties.IndexOf("public static bool BOT_GUILD_CLAIMS_KEEPS", StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0));
            string block = properties[properties.LastIndexOf("/// <summary>", at, StringComparison.Ordinal)..at];
            Assert.That(block, Does.Not.Contain("tower").IgnoreCase);
            string launcher = Source("source", "tools", "OfflineDaoc.Launcher", "SocialExpansionSettings.cs");
            string row = launcher.Split('\n').Single(line => line.TrimStart().StartsWith("(\"bot_guild_claims_keeps\"", StringComparison.Ordinal));
            Assert.That(row, Does.Not.Contain("tower").IgnoreCase);
        }

        // ---- 10. "o/" out of guild chat; the reaction lists together ----

        [Test]
        public void GuildChatSaysNoSlashOAndTheAuditKnowsTheNewReactions()
        {
            string guild = Bots("AutonomousBotGuildChatLines.cs");
            var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            string[] waves = guild.Split('\n').Where(line => !line.TrimStart().StartsWith("//"))
                .SelectMany(line => literal.Matches(CodeOf(line)).Select(m => m.Groups[1].Value))
                .Where(text => Regex.IsMatch(text, @"(^|\s)o/")).ToArray();
            Assert.That(waves, Is.Empty, "no \"o/\" in a guild line");
            string audit = Source("source", "tools", "chat_audit.py");
            string reactions = audit[audit.IndexOf("REACTION = re.compile(", StringComparison.Ordinal)..audit.IndexOf("TRIGGER = re.compile(", StringComparison.Ordinal)];
            Assert.That(reactions, Does.Contain("|lol ouch|").And.Contain("|ouch gl|").And.Contain("|yikes)"));
            Assert.That(AutonomousChatExchange.IsReaction("lol ouch") && AutonomousChatExchange.IsReaction("ouch gl"), Is.True);
        }

        // ---- 11. /lstest deed drowned ----

        [Test]
        public void TheDeedTestKnowsDrowned()
        {
            Assert.That(LivingServerTestCommandHandler.DeedKind("drowned"), Is.EqualTo(AutonomousPlayerDeeds.Kind.Drowned));
            Assert.That(LivingServerTestCommandHandler.DeedKind("fell"), Is.EqualTo(AutonomousPlayerDeeds.Kind.Fell));
            Assert.That(LivingServerTestCommandHandler.DeedKind("swam"), Is.EqualTo(AutonomousPlayerDeeds.Kind.None));
            Assert.That(AutonomousPlayerDeeds.TopicOf(AutonomousPlayerDeeds.Kind.Drowned), Is.EqualTo("drowned"));
        }
    }

    /// <summary>The gate where a line is said (a gamebot double needs the server double).</summary>
    [TestFixture]
    public class UT_ChatFollowUpGate : DutyLeaseFixture
    {
        [Test]
        public void APromiseLineIsSaidOnlyByABotThatGoesAndItsFallbackOtherwise()
        {
            var previous = ChatPromises.Duty;
            try
            {
                GameBot bot = DutyLeaseTestKit.Gamebot();
                var said = new List<string>();
                bool Send(string text)
                {
                    said.Add(text);
                    return true;
                }
                ChatPromises.Duty = (_, _, _, _) => false;
                Assert.That(ChatPromises.SayPromised(bot, "omw", "bled", R.Alarm, "inc bled", "how many?", null, Send), Is.True);
                Assert.That(ChatPromises.SayPromised(bot, "omw", "bled", R.Alarm, "inc bled", null, null, Send), Is.False, "nothing to say instead");
                Assert.That(ChatPromises.SayPromised(bot, "ugh again", "bled", R.Alarm, "inc bled", "how many?", null, Send), Is.True);
                Assert.That(said, Is.EqualTo(new[] { "how many?", "ugh again" }));
                said.Clear();
                ChatPromises.Duty = (_, _, _, _) => true;
                Assert.That(ChatPromises.SayPromised(bot, "omw", "bled", R.Alarm, "inc bled", "how many?", null, Send), Is.True);
                Assert.That(said, Is.EqualTo(new[] { "omw" }));
                Assert.That(ChatPromises.SayPromised(bot, "omw", "bled", R.Alarm, "inc bled", "how many?", null, _ => false), Is.False);

                // A field thread: the answerer's "omw" to an inc, the opener's "k omw", a third bot's "omw too".
                var inc = new AutonomousChatFieldTalk.Said("inc bled", "inc", ["omw"]) { PromisePlace = "Bledmeer Faste" };
                said.Clear();
                ChatPromises.Duty = (_, _, _, _) => false;
                Assert.That(AutonomousBotChatCoordinator.SayThreadLine(bot, "omw", inc, 1, null, null, Send), Is.True);
                Assert.That(AutonomousBotChatCoordinator.SayThreadLine(bot, "omw too", inc, 2, "omw", null, Send), Is.False, "the chime is left out");
                Assert.That(AutonomousBotChatCoordinator.SayThreadLine(bot, "how many?", inc, 1, null, null, Send), Is.True);
                Assert.That(said, Is.EqualTo(new[] { "how many?", "how many?" }));
                var rvr = new AutonomousChatFieldTalk.Said("any grps out?", "rvr", ["at bled"]);
                said.Clear();
                Assert.That(AutonomousBotChatCoordinator.SayThreadLine(bot, "k omw", rvr, 0, "at bled", null, Send), Is.True);
                Assert.That(said, Is.EqualTo(new[] { "k ty" }));
                var lfg = new AutonomousChatFieldTalk.Said("lfg 30", "lfg", ["me"]);
                said.Clear();
                Assert.That(AutonomousBotChatCoordinator.SayThreadLine(bot, "omw", lfg, 1, null, null, Send), Is.False, "no gate for it: not said");
                Assert.That(said, Is.Empty);
                ChatPromises.Duty = (_, _, _, _) => true;
                Assert.That(AutonomousBotChatCoordinator.SayThreadLine(bot, "omw", inc, 1, null, null, Send), Is.True);
                Assert.That(said, Is.EqualTo(new[] { "omw" }));
            }
            finally
            {
                ChatPromises.Duty = previous;
            }
        }
    }
}
