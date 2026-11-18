using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests
{
    /// <summary>
    /// The 1.2 chat audit, replies, groups, guild, trade and crafters (scratchpad chataudit/corpus-c, codetext, logs findings; branch
    /// fix/chat-replies-content). Tests only: none of this has been seen on the owner's PC yet.
    /// </summary>
    [TestFixture]
    public class UT_ChatAuditReplies
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

        #region Combat calls (CORPUS-C-023, C-024)

        [Test]
        public void AFightAGroupmateDiedInNeverEndsWithAKillCall()
        {
            for (int seed = 0; seed < 400; seed++)
            {
                var state = new AutonomousBotCombatCalls.GroupState();
                var random = new Random(seed);
                AutonomousBotCombatCalls.GroupEvent(state, new AutonomousBotCombatCalls.GroupView(true, 1, 4, 4, false, PlayerFighting: true), 1_000, random);
                // One of four died (no wipe: three still stand), then the fight ends.
                AutonomousBotCombatCalls.GroupEvent(state, new AutonomousBotCombatCalls.GroupView(true, 1, 4, 3, false, PlayerFighting: true), 2_000, random);
                string end = AutonomousBotCombatCalls.GroupEvent(state, new AutonomousBotCombatCalls.GroupView(false, 0, 4, 3, false), 3_000, random);
                Assert.That(end, Is.Not.EqualTo("kill"), $"seed {seed}: \"clean\", \"easy\" after a groupmate died");
            }
        }

        [Test]
        public void AFightNobodyDiedInCanStillEndWithAKillCall()
        {
            bool kill = false;
            for (int seed = 0; seed < 200 && !kill; seed++)
            {
                var state = new AutonomousBotCombatCalls.GroupState();
                var random = new Random(seed);
                AutonomousBotCombatCalls.GroupEvent(state, new AutonomousBotCombatCalls.GroupView(true, 1, 4, 4, false, PlayerFighting: true), 1_000, random);
                kill = AutonomousBotCombatCalls.GroupEvent(state, new AutonomousBotCombatCalls.GroupView(false, 0, 4, 4, false), 2_000, random) == "kill";
            }
            Assert.That(kill, Is.True);
        }

        [Test]
        public void ARestCallAboutPowerComesOnlyFromALowCaster() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotCombatCalls.RestLineFits("give me a sec to med", usesPower: false, manaPercent: 0), Is.False, "a warrior");
            Assert.That(AutonomousBotCombatCalls.RestLineFits("need power, sitting", usesPower: true, manaPercent: 100), Is.False, "a full wizard");
            Assert.That(AutonomousBotCombatCalls.RestLineFits("need power, sitting", usesPower: true, manaPercent: 20), Is.True);
            Assert.That(AutonomousBotCombatCalls.RestLineFits("med up", usesPower: false, manaPercent: 0), Is.True, "a call to the group");
            Assert.That(AutonomousBotCombatCalls.RestLineFits("short rest", usesPower: false, manaPercent: 0), Is.True);
        });

        [Test]
        public void ARootCallNamesAnotherEnemyOnlyWhenThereIsOne() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotCombatCalls.RootLineFits("rooted, kill the other", 1), Is.False);
            Assert.That(AutonomousBotCombatCalls.RootLineFits("rooted the add", 1), Is.False);
            Assert.That(AutonomousBotCombatCalls.RootLineFits("rooted, kill the other", 2), Is.True);
            Assert.That(AutonomousBotCombatCalls.RootLineFits("rooted", 1), Is.True);
        });

        [Test]
        public void CombatTopicsWithoutATriggerHaveNoLines() =>
            Assert.That(Corpus.Lines.Where(line => line.Kind == "combat" && line.Topic is "mez_broke" or "healer_oom"), Is.Empty);

        [Test]
        public void NoPullLineTheFilterDropsIsWritten() =>
            Assert.That(Corpus.Lines.Where(line => line.Kind == "combat" && line.Topic == "pull" &&
                                                   Regex.IsMatch(line.Text, @"\b(pull|bringing)", RegexOptions.IgnoreCase)).Select(line => line.Text), Is.Empty);

        [Test]
        public void TheCoreCombatTopicsHaveTwentyLines()
        {
            foreach (string topic in new[] { "pull", "add", "kill", "rest", "low_hp", "wipe", "root", "stun", "oom", "mez", "taunt", "heal_big", "rez" })
                Assert.That(Corpus.Lines.Count(line => line.Kind == "combat" && line.Topic == topic), Is.GreaterThanOrEqualTo(20), topic);
        }

        #endregion

        #region Group and guild code (CORPUS-C-012, C-030, TEXT-010, TEXT-022, TEXT-023)

        [Test]
        public void OnlyABotThatLevelledTakesCongratulations() => Assert.Multiple(() =>
        {
            int[] pool = [1, 2, 3];
            Assert.That(AutonomousBotGroupChatter.CongratulatedOnly(eAutonomousChatIntent.Congrats, pool, id => id == 2), Is.EqualTo(new[] { 2 }));
            Assert.That(AutonomousBotGroupChatter.CongratulatedOnly(eAutonomousChatIntent.Congrats, pool, _ => false), Is.Empty);
            Assert.That(AutonomousBotGroupChatter.CongratulatedOnly(eAutonomousChatIntent.Greeting, pool, _ => false), Is.EqualTo(pool));
        });

        [Test]
        public void TheGuildHelloSaysEveningOnlyInTheEveningAndNeverWaves()
        {
            var morning = new HashSet<string>();
            var evening = new HashSet<string>();
            for (int seed = 0; seed < 300; seed++)
            {
                morning.Add(AutonomousBotGuildChatLines.Greeting("Tamsin", new Random(seed), 10));
                morning.Add(AutonomousBotGuildChatLines.GreetingToBot("Aldric", new Random(seed), 10));
                morning.Add(AutonomousBotGuildChatLines.Greeting("Tamsin", new Random(seed)));
                evening.Add(AutonomousBotGuildChatLines.Greeting("Tamsin", new Random(seed), 19));
            }
            Assert.Multiple(() =>
            {
                Assert.That(morning.Where(line => line.Contains("evening")), Is.Empty);
                Assert.That(morning.Concat(evening).Where(line => line.Contains("o/")), Is.Empty);
                Assert.That(evening, Has.Some.Contains("evening"));
            });
        }

        [Test]
        public void NightSaysGoodbyeOnlyAsAGoodbye() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGuildChatLines.Classify("slow night huh", eAutonomousChatIntent.Banter),
                Is.Not.EqualTo(AutonomousBotGuildChatLines.ReplyKind.Farewell));
            Assert.That(AutonomousBotGuildChatLines.Classify("night all", eAutonomousChatIntent.Unknown), Is.EqualTo(AutonomousBotGuildChatLines.ReplyKind.Farewell));
            Assert.That(AutonomousBotGuildChatLines.Classify("good night guild", eAutonomousChatIntent.Unknown),
                Is.EqualTo(AutonomousBotGuildChatLines.ReplyKind.Farewell));
            Assert.That(AutonomousBotGuildChatLines.Classify("gn", eAutonomousChatIntent.Unknown), Is.EqualTo(AutonomousBotGuildChatLines.ReplyKind.Farewell));
        });

        [Test]
        public void TheGuildLinesHaveNoStiffFullFormsOrWarbands()
        {
            string[] samples = AutonomousBotGuildChatLines.Samples("Tamsin", "Aldric", "Avalon", 30, "Camelot Hills").ToArray();
            Assert.That(samples.Where(line => line.Contains("warband") || line.Contains("you are a riot") || line.Contains("i would ask")), Is.Empty);
        }

        [Test]
        public void ALoginHelloIsForTheOneMember() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGuildSocial.GreetsOne("hey all"), Is.False);
            Assert.That(AutonomousBotGuildSocial.GreetsOne("hey!! how is everyone doing tonight"), Is.False);
            Assert.That(AutonomousBotGuildSocial.GreetsOne("heya Tamsin"), Is.True);
        });

        [Test]
        public void WithVoicesOnAGuildAnswerNeverFallsBackToThePreVoicesLines() => Assert.Multiple(() =>
        {
            Assert.That(DOL.GS.AutonomousBotGuilds.PlayerReplyOrFallback(true, null, "anyone know where the trainer is?"), Is.Null);
            Assert.That(DOL.GS.AutonomousBotGuilds.PlayerReplyOrFallback(true, "lol", "lol"), Is.EqualTo("lol"));
            Assert.That(DOL.GS.AutonomousBotGuilds.PlayerReplyOrFallback(false, null, "anyone know where the trainer is?"), Is.Not.Null,
                "with voices off the base game's line stays");
        });

        #endregion

        #region The guild corpus filter, tuned (CORPUS-C-029; owner 2026-11-18 09:14)

        [Test]
        public void MostWrittenGuildLinesCanBeSaid()
        {
            // Before the audit about 1 in 3 guild lines passed the filter (400 of 614 source lines had no sayable way to be said);
            // now every source line has one (the builder-side replay, chataudit/e5tools/gf2.py), so most expanded lines pass.
            VC.Line[] guild = Corpus.Lines.Where(line => line.Kind == "guild").ToArray();
            int sayable = guild.Count(line => !AutonomousChatFiller.IsCraftedForGuild(line));
            Assert.That(sayable, Is.GreaterThanOrEqualTo(guild.Length * 6 / 10), $"{sayable} of {guild.Length}");
        }

        [Test]
        public void TheGuildFilterKeepsTipsAndLongLinesOutButLetsShortJokesAndLifeIn()
        {
            VC.Line Guild(string text, string invites = "chat") =>
                new("x", "guild", "social", [], "*", [], 1, 50, [], string.Empty, string.Empty, 1, text) { Invites = invites };
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousChatFiller.IsCraftedForGuild(Guild("the guild that wipes together stays together", "joke")), Is.False);
                Assert.That(AutonomousChatFiller.IsCraftedForGuild(Guild("finally home from work, glad to be here")), Is.False);
                Assert.That(AutonomousChatFiller.IsCraftedForGuild(Guild("if you need potions, i know an alchemist")), Is.True, "a tip");
                Assert.That(AutonomousChatFiller.IsCraftedForGuild(Guild("one two three four five six seven eight nine")), Is.True, "over 8 words");
                Assert.That(AutonomousChatFiller.IsCrafted(Guild("finally home from work, glad to be here")), Is.True,
                    "the unprompted channels keep the life words out");
            });
        }

        [Test]
        public void AGuildHearsALifeLineEveryNinetyMinutesAtMost()
        {
            var memory = new AutonomousChatFieldTalk.Memory();
            var life = new VC.Line("x", "guild", "social", [], "*", [], 1, 50, [], string.Empty, string.Empty, 1, "rain all day here") { Invites = "chat" };
            Assert.That(AutonomousChatFiller.GuildLifeAllowed(memory, 1_000), Is.True);
            AutonomousChatFiller.NoteGuildCorpus(life, memory, 1_000);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousChatFiller.GuildLifeAllowed(memory, 1_000 + 60 * 60_000), Is.False);
                Assert.That(AutonomousChatFiller.GuildLifeAllowed(memory, 1_000 + AutonomousChatFiller.GuildLifeGapMs), Is.True,
                    "before: one in six hours");
            });
        }

        #endregion

        #region Reply fit (CORPUS-C-016)

        [Test]
        public void ABanterRemarkIsAnsweredInKindButABanterQuestionIsNot() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousChatReplyFit.Of("intent banter", "this camp is dead lol"), Is.EqualTo(AutonomousChatReplyFit.Fits));
            Assert.That(AutonomousChatReplyFit.Of("intent banter", "why is this camp so dead?"), Is.EqualTo(AutonomousChatReplyFit.Weak));
        });

        #endregion

        #region One laugh a line (1.2 CI after the rebase onto E3's voice layer)

        [Test]
        public void ALineEndingInASmileyGetsNoLolTail()
        {
            Assert.That(AutonomousChatIdiolect.EndsWithSmiley("best night ever :P"), Is.True);
            Assert.That(AutonomousChatIdiolect.EndsWithSmiley("best night ever"), Is.False);
            // Every bot id and many draws: a smiley-ended line never comes back with " lol" after it.
            for (long botId = 1; botId <= 400; botId++)
            {
                var random = new Random((int)botId);
                for (int i = 0; i < 20; i++)
                    Assert.That(AutonomousChatIdiolect.Apply("i grouped with a minstrel, best night ever :P", botId, false, random),
                        Does.Not.EndWith("lol"), $"bot {botId}");
            }
        }

        #endregion

        #region Crafters (CORPUS-C-037, C-038, LOG-024, LOG-026, LOG-036)

        [Test]
        public void ACrafterAtItsBreakSaysWhereItIsNotAtMyBreakInNow()
        {
            var made = new CrafterTalk.OrderView("o1", "Chain Hauberk", DOL.GS.CraftingEconomy.CraftOrderState.Made, 10_000, false);
            CrafterTalk.Reply reply = CrafterTalk.Answer(UT_CrafterTalk.Crafter(atBreak: true, toBreak: 0), UT_CrafterTalk.Stranger(), "is it done",
                null, [made], new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc));
            Assert.Multiple(() =>
            {
                Assert.That(reply.Topic, Is.EqualTo("craft_ready_tell"));
                Assert.That(reply.Slots.ContainsKey("zone"), Is.True);
                Assert.That(reply.Text, Does.Not.Contain("in now"));
            });
        }

        [Test]
        public void ALongCrafterTellTakesTimeToType()
        {
            string reply = string.Join(' ', Enumerable.Repeat("word", 31));
            Assert.That(CrafterCommerce.TellDelayMs(null, "what do you make?", reply, new Random(1)), Is.GreaterThanOrEqualTo(8_000));
        }

        [Test]
        public void ThousandsOfGoldAreWrittenInPlatinum() => Assert.Multiple(() =>
        {
            Assert.That(BotCrafterVoice.PriceText(92_750_000), Is.EqualTo("9p 200g"));
            Assert.That(BotCrafterVoice.PriceText(120_000_000), Is.EqualTo("12p"));
            Assert.That(BotCrafterVoice.PriceText(150_000), Is.EqualTo("15g"));
        });

        [Test]
        public void ThePriceWarSpeaksOfNoOneCraftsTools()
        {
            string[] said = Corpus.Scenes.Where(scene => scene.Topic == "story_pricewar").SelectMany(scene => scene.Beats).SelectMany(beat => beat.Variants)
                .ToArray();
            Assert.That(said, Is.Not.Empty);
            Assert.That(said.Where(text => Regex.IsMatch(text, @"\b(seams?|forge|hammer|bars|cloaks?|helm)\b", RegexOptions.IgnoreCase)), Is.Empty);
        }

        #endregion

        #region Lines (CORPUS-C-001, C-003, C-006, C-018, C-044, C-048)

        [Test]
        public void EveryClassHasSixLinesOfClassTalkInItsRealm()
        {
            var classes = new Dictionary<string, string[]>
            {
                ["alb"] = ["armsm", "paladin", "merc", "reaver", "scout", "infil", "minstrel", "cleric", "friar", "wizard", "theurgist", "sorc", "cabalist", "necro"],
                ["mid"] = ["warrior", "thane", "skald", "berserker", "savage", "hunter", "shadowblade", "healer", "shaman", "runemaster", "spiritmaster", "bonedancer"],
                ["hib"] = ["hero", "champion", "blademaster", "warden", "druid", "bard", "ranger", "nightshade", "eldritch", "enchanter", "mentalist", "animist", "valewalker"],
            };
            foreach ((string realm, string[] words) in classes)
                foreach (string word in words)
                    Assert.That(Corpus.Lines.Count(line => line.Kind == "reply" && line.Intent == "classtalk" && line.Realm == realm &&
                                                           line.Text.Contains(word, StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(6), $"{realm} {word}");
        }

        [Test]
        public void TheNewReplySetsAreWritten()
        {
            foreach ((string kind, string set, int least) in new[] { ("reply", "ask_ready", 20), ("reply", "ask_who", 10), ("reply", "ask_price", 15),
                         ("closing", "ask_unknown", 10), ("reply", "player_dingcap", 8) })
                Assert.That(Corpus.Lines.Count(line => line.Kind == kind && line.Answers.Contains(set)), Is.GreaterThanOrEqualTo(least), set);
        }

        [Test]
        public void ABotsQuestionAboutThePlayerIsClosedAsOne() =>
            Assert.That(Corpus.Lines.Where(line => line.Kind == "tell" && line.Text is "hey {player}, dinged yet?" or "{player}! where have you been hiding?")
                .Select(line => line.Invites), Is.All.EqualTo("ask_personal"));

        [Test]
        public void AReadyCheckIsAnsweredFromTheReadySet() =>
            Assert.That(Corpus.Lines.Single(line => line.Kind == "group" && line.Text == "pull?").Invites, Is.EqualTo("ask_ready"));

        [Test]
        public void NoAlarmReplySaysItIsComing() =>
            Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Intent == "alarm" &&
                                                   Regex.IsMatch(line.Text, @"\b(omw|on my way|coming|heading there)\b")).Select(line => line.Text), Is.Empty);

        [Test]
        public void NoLineWaves() =>
            Assert.That(Corpus.Lines.Where(line => line.Kind is "reply" or "closing" or "guild" or "group" or "combat" or "tell" or "lfg" or "trade" &&
                                                   Regex.IsMatch(line.Text, @"(^|\s)\\?o/")).Select(line => line.Text), Is.Empty);

        [Test]
        public void TheFirstGroupOfReplySetsHasTwentyLinesAnyBotCanSay()
        {
            static bool Plain(VC.Line line) => line.Who.Length == 0 && line.Realm == "*" && line.Classes.Length == 0 && line.LevelLow == 1 &&
                                                line.LevelHigh == 50 && line.Needs.Length == 0;
            foreach (string intent in new[] { "farewell", "back", "away", "ding", "congrats", "rez", "abuse" })
                Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Intent == intent && Plain(line)).Select(line => line.Text).Distinct().Count(),
                    Is.GreaterThanOrEqualTo(20), intent);
            foreach (string set in new[] { "ask_on", "ask_howareyou", "player_goodnews", "player_mishap" })
                Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Answers.Contains(set) && Plain(line)).Select(line => line.Text).Distinct().Count(),
                    Is.GreaterThanOrEqualTo(20), set);
        }

        #endregion
    }
}
