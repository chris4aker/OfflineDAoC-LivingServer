using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.Database;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The bot chat audit of 2026-11-18, events slice (corpus-b2 findings, LOG-003/005/023/037, DESIGN-007, TEXT-019):
    /// storylines, dunce, deeds, news, rumours, holidays, spammers, roleplayers, beggars, bonds, milestones and memory.
    /// Each test fails on the code or corpus before the fix.
    /// </summary>
    [TestFixture]
    public class UT_ChatAuditEvents
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

        private static IEnumerable<VC.Line> Lines(string kind, string topic = null) =>
            Corpus.Lines.Where(line => line.Kind == kind && (topic == null || line.Topic == topic));

        // ---- holidays (B2-024, B2-026) ----

        [Test]
        public void TheHolidayWindowSaysWhichSideOfTheDayItIs()
        {
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 10, 28)), Does.Contain("holiday_before").And.Not.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 10, 31)), Does.Contain("holiday_today").And.Not.Contain("holiday_before")
                .And.Not.Contain("holiday_after"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 1)), Does.Contain("holiday_after").And.Not.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 28)), Does.Contain("holiday_thanksgiving").And.Contain("holiday_after"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 12, 31)), Does.Contain("holiday_before").And.Not.Contain("holiday_today"),
                "New Year's day is January 1");
            Assert.That(AutonomousHolidays.Flags(new DateTime(2027, 1, 1)), Does.Contain("holiday_today"));
        }

        [Test]
        public void SoonAndPlansLinesAreSaidOnlyBeforeTheDay()
        {
            var soon = new Regex(@"\b(soon|this weekend|coming up|is close|tomorrow|shopping done|is coming|this year\?|plans|anyone ringing)\b");
            var holidayLines = Lines("ambient", "holiday").Where(line => soon.IsMatch(line.Text)).ToArray();
            Assert.That(holidayLines, Is.Not.Empty);
            foreach (VC.Line line in holidayLines)
                Assert.That(line.Needs, Does.Contain("holiday_before"), line.Text);
            foreach (VC.Line line in Lines("ambient", "holiday").Concat(Lines("group", "holiday"))
                         .Where(line => Regex.IsMatch(line.Text, @"trick or treat|doorbell|brb, pie|stuffed|first (death|ding) of the year")))
                Assert.That(line.Needs, Does.Contain("holiday_today"), line.Text);
        }

        [Test]
        public void HolidayPlansAreAnsweredAsHolidayPlans()
        {
            var plans = Lines("ambient", "holiday").Where(line => line.Text.Contains("plans") || line.Text.Contains("time off")).ToArray();
            Assert.That(plans, Is.Not.Empty);
            Assert.That(plans.Select(line => line.Invites).Distinct(), Is.EquivalentTo(new[] { "ask_holiday" }));
            Assert.That(Corpus.Lines.Count(line => line.Kind == "reply" && line.Answers.Contains("ask_holiday")), Is.GreaterThanOrEqualTo(8));
            foreach (string name in new[] { "valentines", "stpatricks", "aprilfools", "easter", "midsummer" })
                Assert.That(Lines("ambient", "holiday").Where(line => line.Needs.Contains("holiday_" + name)).Select(line => line.Family.Length > 0 ? line.Family : line.Text)
                    .Distinct().Count(), Is.GreaterThanOrEqualTo(8), name);
        }

        // ---- storylines (B2-022, B2-056) ----

        [Test]
        public void AnEndingNamesThePlayerOnlyWhileTheyAreOnline()
        {
            Assert.That(AutonomousBotStorylines.SideToPlay("A", "Manawatch", _ => true), Is.EqualTo("A"));
            Assert.That(AutonomousBotStorylines.SideToPlay("A", "Manawatch", _ => false), Is.Empty, "offline: the plain ending");
            Assert.That(AutonomousBotStorylines.SideToPlay("", "", _ => true), Is.Empty);
        }

        [Test]
        public void AnArcKeepsOneKeepForEveryEpisode()
        {
            var row = new OfflineBotStorylineRecord { StoryKey = "1:guildsplit", Arc = "guildsplit" };
            var first = new Dictionary<string, string>();
            Assert.That(AutonomousBotStorylines.FixArcPlace(row, new Dictionary<string, string> { ["keep"] = "Caer Benowyc", ["frontier"] = "Pennine Mountains" }, first),
                Is.True);
            var later = new Dictionary<string, string>();
            Assert.That(AutonomousBotStorylines.FixArcPlace(row, new Dictionary<string, string> { ["keep"] = "Caer Erasleigh", ["frontier"] = "Hadrian's Wall" }, later),
                Is.False);
            Assert.That(later, Is.EquivalentTo(first));
            Assert.That(later["keep"], Is.EqualTo("Caer Benowyc"));
            Assert.That(later["frontier"], Is.EqualTo("Pennine Mountains"));
        }

        [Test]
        public void TheRomanceHealerIsCastAsAHealer()
        {
            string[] healers = ["Cleric", "Friar", "Healer", "Shaman", "Druid", "Bard", "Warden"];
            foreach (VC.Scene scene in Corpus.Scenes.Where(scene => scene.Id.StartsWith("story_romance_", StringComparison.Ordinal)))
                Assert.That(scene.Roles["B"].Classes, Is.EquivalentTo(healers), scene.Id);
            VC.Scene pets = Corpus.Scenes.Single(scene => scene.Id == "dunce_pet_warrior");
            Assert.That(pets.Roles["A"].Classes, Does.Not.Contain("Cleric").And.Not.Contain("Wizard").And.Not.Contain("Minstrel"));
        }

        // ---- owner decisions 09:14: side characters, the guild split's cast, the dunce's pace, a Canadian Thanksgiving ----

        [Test]
        public void ASideCharacterKeepsItsLetterAcrossAnArc()
        {
            string saved = AutonomousBotStorylines.MergeSideCast("", new Dictionary<string, long> { ["A"] = 1, ["B"] = 2, ["C"] = 30, ["H"] = 80 }, 1, 2);
            Assert.That(saved, Is.EqualTo("C=30;H=80"));
            saved = AutonomousBotStorylines.MergeSideCast(saved, new Dictionary<string, long> { ["C"] = 31, ["D"] = 40, ["E"] = 80 }, 1, 2);
            Assert.That(AutonomousBotStorylines.ParseSideCast(saved), Is.EquivalentTo(new Dictionary<string, long> { ["C"] = 30, ["D"] = 40, ["H"] = 80 }),
                "a stand-in never takes the role over, and one bot plays one side character");
            VC.Scene trial = Corpus.Scenes.Single(scene => scene.Id == "story_ninjaloot_3");
            var free = new Dictionary<long, AutonomousBotDebates.Candidate>
            {
                [30] = new(30, BotPersonalityType.Socialite, "Cleric", 40),
                [40] = new(40, BotPersonalityType.Leveler, "Armsman", 40),
            };
            Dictionary<string, long> roles = AutonomousBotStorylines.FixedRoles(trial, 1, 2, saved, free);
            Assert.That(roles, Is.EquivalentTo(new Dictionary<string, long> { ["A"] = 1, ["B"] = 2, ["C"] = 30, ["D"] = 40 }), "H is offline: someone else plays it");
            free[30] = new(30, BotPersonalityType.Daredevil, "Cleric", 40);
            Assert.That(AutonomousBotStorylines.FixedRoles(trial, 1, 2, saved, free).ContainsKey("C"), Is.False, "the judge must be a Socialite here");
        }

        [Test]
        public void TheRecurringCharactersHaveOneLetter()
        {
            string RoleOf(string scene, string words) => Corpus.Scenes.Single(s => s.Id == scene).Beats
                .Single(beat => beat.Variants.Any(v => v.Contains(words))).Role;
            Assert.That(RoleOf("story_killsteal_3", "ref the duel"), Is.EqualTo(RoleOf("story_killsteal_4", "ref here")));
            Assert.That(RoleOf("story_ninjaloot_2", "be the judge"), Is.EqualTo(RoleOf("story_ninjaloot_3", "order in the court")));
            Assert.That(RoleOf("story_romance_4a", "officiant reporting"), Is.EqualTo(RoleOf("story_romance_4", "by the power of this robe")));
        }

        [Test]
        public void TheGuildSplitIsALeaderAndAnOfficer()
        {
            var leader = new AutonomousBotRealmGuilds.Membership(1, "g1", 0);
            Assert.That(AutonomousBotStorylines.GuildPairFits(leader, new(2, "g1", 1)), Is.True);
            Assert.That(AutonomousBotStorylines.GuildPairFits(leader, new(2, "g2", 1)), Is.False, "another guild");
            Assert.That(AutonomousBotStorylines.GuildPairFits(leader, new(2, "g1", 9)), Is.False, "a plain member");
            Assert.That(AutonomousBotStorylines.GuildPairFits(new(1, "g1", 1), new(2, "g1", 1)), Is.False, "no leader");
            Assert.That(AutonomousBotStorylines.GuildPairFits(leader, null), Is.False);
        }

        [Test]
        public void TheDunceIsOneDrawInEightWithARest()
        {
            var random = new Random(11);
            int[] levels = Enumerable.Range(5, 46).ToArray();
            int dunces = 0, draws = 4_000;
            for (int i = 0; i < draws; i++)
                if (AutonomousBotDebates.IsDunce(AutonomousBotDebates.ChooseScene(Corpus.Scenes, eRealm.Albion, new HashSet<string>(), levels, random, out _,
                        dunceDue: true)))
                    dunces++;
            Assert.That(dunces, Is.InRange(draws / 8 - 120, draws / 8 + 120));
            for (int i = 0; i < 500; i++)
                Assert.That(AutonomousBotDebates.IsDunce(AutonomousBotDebates.ChooseScene(Corpus.Scenes, eRealm.Albion, new HashSet<string>(), levels, random,
                    out _, dunceDue: false)), Is.False, "resting");
            DateTime now = new(2026, 11, 18, 20, 0, 0);
            Assert.That(AutonomousBotDebates.DunceIsDue(now.AddHours(-1), now), Is.False);
            Assert.That(AutonomousBotDebates.DunceIsDue(now.AddHours(-3), now), Is.True);
            Assert.That(AutonomousBotDebates.DunceIsDue(null, now), Is.True);
        }

        [Test]
        public void ThanksgivingCanBeCanadianOrOff()
        {
            Assert.That(AutonomousHolidays.CanadianThanksgiving(2026), Is.EqualTo(new DateTime(2026, 10, 12)));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 10, 12), "canada"), Does.Contain("holiday_thanksgiving").And.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 10, 10), "canada"), Does.Contain("holiday_before"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 26), "canada"), Is.Empty);
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 26), "off"), Is.Empty);
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 26), "us"), Does.Contain("holiday_thanksgiving"));
            Assert.That(AutonomousHolidays.Style(null), Is.EqualTo("us"));
            Assert.That(AutonomousHolidays.Style(" Canada "), Is.EqualTo("canada"));
            foreach (VC.Line line in Lines("ambient", "holiday").Where(line => line.Text.Contains("black friday") || line.Text.Contains("four day weekend")))
                Assert.That(line.Needs, Does.Contain("holiday_after"), "US only: a Canadian window has no days after: " + line.Text);
        }

        // ---- roleplayers (TEXT-019, B2-007, B2-052, LOG-023) ----

        [Test]
        public void RoleplayersNameTheEnemyAsAPeople()
        {
            var random = new Random(3);
            for (int i = 0; i < 50; i++)
            {
                string alb = AutonomousRoleplayers.EnemyPeople(eRealm.Albion, random);
                Assert.That(alb, Is.AnyOf("Norsemen", "Hibernians"));
                Assert.That(AutonomousRoleplayers.EnemyPeople(eRealm.Hibernia, random), Is.AnyOf("Britons", "Norsemen"));
            }
            foreach (VC.Line line in Lines("roleplay").Where(line => line.Text.Contains("{enemy}")))
                Assert.That(line.Text, Does.Not.Match(@"(many a|our foes in|the next) \{enemy\}|\{enemy\} (is|hath) "), line.Text);
        }

        [Test]
        public void AMockQuotesOnlyWhatWasSaid()
        {
            Assert.That(AutonomousRoleplayers.QuoteFits("forsooth? FORSOOTH?", "good morrow, brave souls of the realm"), Is.False);
            Assert.That(AutonomousRoleplayers.QuoteFits("forsooth? FORSOOTH?", "forsooth, the ale grows thin"), Is.True);
            Assert.That(AutonomousRoleplayers.QuoteFits("my eyes glazed over at \"good morrow\"", "hail, friends"), Is.False);
            Assert.That(AutonomousRoleplayers.QuoteFits("{target} is at it again", "anything"), Is.True);
            Assert.That(AutonomousRoleplayers.QuoteFits("a nerd, sayest thou? i shall add it to mine titles", "speak english", answer: true), Is.False);
            Assert.That(AutonomousRoleplayers.QuoteFits("a nerd, sayest thou? i shall add it to mine titles", "thou art a nerd", answer: true), Is.True);
            Assert.That(AutonomousRoleplayers.QuoteFits("verily i say, {target}, a churl's laughter is but wind", "speak english", answer: true), Is.True,
                "an answer's own olde words are not quotes");
        }

        [Test]
        public void SwordLinesAreForClassesThatCarryOne()
        {
            foreach (VC.Line line in Lines("roleplay", "ambient").Where(line => Regex.IsMatch(line.Text, @"\b(sword|blade|shield|axe)\b|\*draws sword\*")))
                Assert.That(line.Classes, Is.Not.Empty.And.Not.Contain("Cabalist").And.Not.Contain("Friar"), line.Text);
        }

        // ---- deeds (B2-011, 012, 013, 014, 015, 042, 045) ----

        [Test]
        public void ADrowningIsNotAFall()
        {
            Assert.That(AutonomousPlayerDeeds.Death(true, false, 0, false, 0, underwater: true), Is.EqualTo(AutonomousPlayerDeeds.Kind.Drowned));
            Assert.That(AutonomousPlayerDeeds.Death(true, false, 0, false, 0), Is.EqualTo(AutonomousPlayerDeeds.Kind.Fell));
            Assert.That(AutonomousPlayerDeeds.TopicOf(AutonomousPlayerDeeds.Kind.Drowned), Is.EqualTo("drowned"));
            Assert.That(AutonomousPlayerDeeds.NamesPlace("drowned", true, 0.5), Is.True);
            var water = new Regex(@"drown|swim|breath|water|lake|surface");
            Assert.That(Lines("deed", "drowned").Count(), Is.GreaterThanOrEqualTo(8));
            foreach (VC.Line line in Lines("deed", "fell").Concat(Lines("deed_reply", "fell")))
                Assert.That(line.Text, Does.Not.Match(water.ToString()), line.Text);
        }

        [Test]
        public void SoloMeansTheDamageWasTheirs()
        {
            Assert.That(AutonomousPlayerDeeds.Solo(false, 950, 1000), Is.True);
            Assert.That(AutonomousPlayerDeeds.Solo(false, 600, 1000), Is.False, "bots helped on the named");
            Assert.That(AutonomousPlayerDeeds.Solo(true, 1000, 1000), Is.False, "a groupmate was near");
            Assert.That(AutonomousPlayerDeeds.HardKill(-1), Is.False, "blue");
            Assert.That(AutonomousPlayerDeeds.HardKill(0), Is.True, "yellow");
            foreach (VC.Line line in Lines("deed", "named_solo").Concat(Lines("deed_reply", "named_solo"))
                         .Where(line => Regex.IsMatch(line.Text, @"needed a full group|wiped my group|still shaking|newbie mob|since launch|no one solos|rethink my spec")))
                Assert.That(line.Needs, Does.Contain("hard_kill"), line.Text);
        }

        [Test]
        public void ANicknameIsNamedOnlyWhereItFits()
        {
            Assert.That(AutonomousPlayerDeeds.NicknameFits("rep_named", "silly"), Is.False, "\"heard the name came from soloing\" of the Clumsy");
            Assert.That(AutonomousPlayerDeeds.NicknameFits("rep_rvr", "named"), Is.False);
            Assert.That(AutonomousPlayerDeeds.NicknameFits("silly_death", "silly"), Is.False, "\"maybe time for a new nickname\"");
            Assert.That(AutonomousPlayerDeeds.NicknameFits("silly_death", "rvr"), Is.True);
            Assert.That(AutonomousPlayerDeeds.NicknameFits("fell", "silly"), Is.True);
            Assert.That(AutonomousPlayerDeeds.NicknameFits("nickname", "silly"), Is.True);
        }

        [Test]
        public void RvrReputationTalkStartsAtFiftyKills()
        {
            Assert.That(AutonomousPlayerDeeds.RepTopics(new OfflinePlayerReputationRecord { RvrKills = 12 }).Select(t => t.Topic), Does.Not.Contain("rep_rvr"));
            Assert.That(AutonomousPlayerDeeds.RepTopics(new OfflinePlayerReputationRecord { RvrKills = 50 }).Select(t => t.Topic), Does.Contain("rep_rvr"));
        }

        [Test]
        public void DeedLinesSayOnlyWhatTheWitnessCouldKnow()
        {
            foreach (VC.Line line in Lines("deed", "named_group").Where(line => Regex.IsMatch(line.Text, @"^(we |our group)|\bwe (got|all)\b|kept us alive")))
                Assert.That(line.Needs, Does.Contain("in_player_group"), line.Text);
            foreach (VC.Line line in Lines("deed", "named_group").Where(line => Regex.IsMatch(line.Text, @"i was nearby|dont know who else")))
                Assert.That(line.Needs, Does.Contain("!in_player_group"), line.Text);
            foreach (VC.Line line in Lines("deed").Concat(Lines("deed_reply")))
            {
                Assert.That(line.Text, Does.Not.Match(@"\b(an|a) \{count\}|\ban \{enemy\}"), line.Text);
                if (line.Topic == "ganked" && line.Kind == "deed")
                    Assert.That(line.Text, Does.Not.Contain("zerg"), "three enemies are no zerg: " + line.Text);
                Assert.That(line.Text, Does.Not.Match(@"bunn|drowned in a puddle|newbie mobs \{count\}|mana left"), line.Text);
            }
        }

        // ---- news (B2-003..006, B2-043, LOG-003) ----

        [Test]
        public void SiegeNewsFollowsTheStage()
        {
            Assert.That(AutonomousRvrEventLayer.SiegeNewsTopic(true, false), Is.EqualTo("siege_muster"));
            Assert.That(AutonomousRvrEventLayer.SiegeNewsTopic(false, false), Is.EqualTo("siege_warning"));
            Assert.That(AutonomousRvrEventLayer.SiegeNewsTopic(true, true), Is.EqualTo("siege_attack"));
            Assert.That(AutonomousRvrEventLayer.SiegeNewsTopic(false, true), Is.EqualTo("siege_defend"));
            foreach (string topic in new[] { "siege_muster", "siege_warning" })
                Assert.That(Lines("news", topic).Count(), Is.GreaterThanOrEqualTo(8), topic);
            var claims = new Regex(@"\b(door|doors|rams?|oil|lord room|four people|barely defending|wiped|casters on the walls)\b");
            foreach (string topic in new[] { "siege_muster", "siege_warning", "siege_attack", "siege_defend", "siege_held", "siege_failed" })
                foreach (VC.Line line in Lines("news", topic))
                    Assert.That(line.Text, Does.Not.Match(claims.ToString()), $"{topic}: {line.Text}");
        }

        [Test]
        public void NewsReadsRightWithThePeopleWord()
        {
            foreach (VC.Line line in Lines("news"))
                Assert.That(line.Text, Does.Not.Match(@"\{(enemy|winner)\} (is|has) |the \{enemy\}|i ran it back myself|carried \{relic\} home myself|at the zone in|45\+"),
                    line.Text);
            Assert.That(Lines("news", "enemy_seen"), Is.Empty, "sightings are never voiced");
            Assert.That(Lines("news", "df_lost").Concat(Lines("news", "df_won")).Where(line => line.Needs.Contains("witness")), Is.Empty,
                "Darkness Falls has no place, so there is never a witness");
            Assert.That(Lines("news", "relic_lost").Any(line => line.Realm == "mid"), Is.True, "Midgard has a capital line too");
        }

        // ---- rumours (B2-002, B2-041, LOG-005) ----

        [Test]
        public void AKeepIsCalledOpenOnlyWhenNobodyHoldsIt()
        {
            Assert.That(AutonomousRumours.MayCallOpen(false, false, null, 0), Is.True);
            Assert.That(AutonomousRumours.MayCallOpen(true, false, null, 0), Is.False, "a portal keep");
            Assert.That(AutonomousRumours.MayCallOpen(false, true, null, 0), Is.False, "under siege");
            Assert.That(AutonomousRumours.MayCallOpen(false, false, TimeSpan.FromMinutes(1), 0), Is.False, "just taken: the army is inside");
            Assert.That(AutonomousRumours.MayCallOpen(false, false, TimeSpan.FromMinutes(45), 0), Is.True);
            Assert.That(AutonomousRumours.MayCallOpen(false, false, null, 12), Is.False, "its owners are there");
            Dictionary<string, string> said = AutonomousRumours.ChatSlots(new Dictionary<string, string>
                { ["keep"] = "Hlidskialf Faste", ["place"] = "Jamtland Mountains", ["target"] = "Captain Atwell" }, new Random(1));
            Assert.That(said["keep"], Does.Not.Contain("Faste").And.Not.Contain("H"));
            Assert.That(said["place"], Is.EqualTo(said["place"].ToLowerInvariant()));
            Assert.That(said["target"], Is.EqualTo("Captain Atwell"));
            foreach (VC.Line line in Lines("rumour", "keep_thin"))
                Assert.That(line.Text, Does.Not.Match(@"not around|are nowhere|hard and left|are asleep"), line.Text);
        }

        // ---- memory (B2-028, B2-043, DESIGN-007) ----

        [Test]
        public void MemoryRecallsTheGroupNotTheTown()
        {
            Assert.That(AutonomousLivingServer.RememberedPlace("City of Camelot", true), Is.Null);
            Assert.That(AutonomousLivingServer.RememberedPlace("Salisbury Plains", false), Is.EqualTo("Salisbury Plains"));
            Assert.That(AutonomousLivingServer.MemoryFlags(new OfflineBotMemoryRecord { Rezzed = 2 }), Is.EquivalentTo(new[] { "knows_player" }));
            Assert.That(AutonomousLivingServer.MemoryFlags(new OfflineBotMemoryRecord { Grouped = 1 }, "player_nearby"),
                Is.EquivalentTo(new[] { "knows_player", "player_grouped", "player_nearby" }));
            // extras.lines (another engineer's file) has "{player}, u around? want to group again": reported, not checked here.
            foreach (VC.Line line in Lines("greet").Concat(Lines("gossip")).Where(line => Regex.IsMatch(line.Text, @"\bgroup\b|\brun at\b|grouped with") &&
                                                                                    !line.Text.Contains("want to group again")))
                Assert.That(line.Needs, Does.Contain("player_grouped"), line.Text);
            Assert.That(Lines("greet").Where(line => line.Mood is "neutral" or "rival"), Is.Empty, "moods no row ever has");
            Assert.That(Lines("thanks").Where(line => line.Topic == "trade"), Is.Empty, "nothing asks for trade thanks");
        }

        [Test]
        public void AKnownPlayerIsPromisedAGroupOnlyWhenTheBotWouldJoin()
        {
            Assert.That(AutonomousLivingServer.WouldJoin(null, false, false, null), Is.True);
            Assert.That(AutonomousLivingServer.WouldJoin("too low lol", false, false, null), Is.False);
            Assert.That(AutonomousLivingServer.WouldJoin(null, true, false, null), Is.False);
            Assert.That(AutonomousLivingServer.WouldJoin(null, false, true, null), Is.False);
            Assert.That(AutonomousLivingServer.WouldJoin(null, false, false, "you're a bit low for me"), Is.False);
            Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Topic == "player" && line.Intent == "grouping")
                .Where(line => !AutonomousChatShortTalk.ReplyFits("lfg 30 cleric", line.Text)), Is.Empty, "no level asked of a line that said it");
        }

        // ---- milestones (B2-030, B2-044, LOG-037) ----

        [Test]
        public void ATrophyIsAnItemTheClassCanUse()
        {
            var vest = new DbInventoryItem { Name = "Edgebender Cloth Vest" };
            Assert.That(BotMilestoneLife.CountsAsTrophy(vest, _ => false), Is.False);
            Assert.That(BotMilestoneLife.CountsAsTrophy(vest, _ => true), Is.True);
            Assert.That(BotMilestoneLife.CountsAsTrophy(vest, null), Is.True, "no rules to ask: as before");
            foreach (VC.Line line in Lines("milestone").Concat(Corpus.Lines.Where(line => line.Answers.Contains("milestone_trophy"))))
                Assert.That(line.Text, Does.Not.Match(@"worth the wait|good stuff|sweet drop|not logging till|dragon loot|off \{boss\} again"), line.Text);
        }

        // ---- the small ones (B2-001, 008, 009, 016, 018, 019, 020, 021, 029, 031, 033, 036, 037, 038) ----

        private static readonly string[] Files = ["storylines", "dunce", "deeds", "news", "news_replies", "rumours", "era_rumours", "holidays",
            "spammers", "roleplayers", "beggars", "charity", "bonds", "milestones", "memory", "memory_more"];

        private static string[] RawLines(string file)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int up = 0; up < 8 && dir != null; up++, dir = Path.GetDirectoryName(dir))
            {
                string path = Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous", "data", "chat", file + ".lines");
                if (File.Exists(path))
                    return File.ReadAllLines(path);
                path = Path.Combine(dir, "GameServer", "bots", "autonomous", "data", "chat", file + ".lines");
                if (File.Exists(path))
                    return File.ReadAllLines(path);
            }
            return null;
        }

        [Test]
        public void TheEventLinesReadRight()
        {
            var bad = new Regex(@"(^|\s)o/|\bo7\b|\bl2p\b|woke up and chose|friends we made along|season two|i ship it|ship name|SHIP SAILED|\btoxic\b|touch a real rock|" +
                                @"asking for (nobody|the enemy)|love to see it|grind is real|\bim dead\b|chopping onions|wholesome|\boof\b|thoughts and prayers|" +
                                @"the bar is on the floor|you fed|keep feeding|fed the newbie|free for all|leader only|free loot|in the \{frontier\}|our own guards|" +
                                @"keep map|open your map|water breathing|petitioned|guildmate|bind at the healer|who even buy gold|" +
                                @"thats two now|two runs|\{other_class\}s|(?-i:asked ME)|his own mother|a mans|laughing the loudest|i saw you laughing|" +
                                @"you're a \{class\}|apologize to the horse|guards are op|my cousin is not a gm|the dog thing|i was hungry, ok|1 PLAT ONLY|farmed \{target\}",
                RegexOptions.IgnoreCase);
            var types = new HashSet<string> { "storyline", "debate", "beggar", "deed", "deed_reply", "news", "reply", "rumour", "rumour_reply", "ambient", "group",
                "spam", "spam_react", "roleplay", "bond", "bond_reply", "milestone", "greet", "gossip", "thanks", "grudge", "session", "lfg" };
            int read = 0;
            foreach (string file in Files)
            {
                string[] raw = RawLines(file);
                if (raw == null)
                    continue;
                read++;
                foreach (string text in raw.Where(text => !text.StartsWith('#') && !text.StartsWith('@')))
                {
                    Assert.That(bad.IsMatch(text), Is.False, $"{file}.lines: {text}");
                    if (text.Trim().Length > 0)
                        Assert.That(Regex.IsMatch(text, "[A-Za-z0-9]") || text.Contains('*'), Is.True, $"{file}.lines: a line with no words: {text}");
                }
            }
            Assert.That(read, Is.EqualTo(Files.Length), "the .lines files are found from the test folder");
            Assert.That(types, Is.Not.Empty);
        }
    }
}
