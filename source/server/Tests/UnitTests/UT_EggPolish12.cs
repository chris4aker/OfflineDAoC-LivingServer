using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DOL.GS.Commands;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.2 polish of the playful features (owner, 2026-11-15): the jokes from after October 2003 are cut, Trials of Atlantis
    /// and Catacombs are rumours only, Caledonia's quests follow its 30-35 bracket, and the rough edges the inventory found
    /// (a raid named for its boss, holidays, roleplayer words, the GM's Leeroy "now", /lstest list, "tonight" at any hour).
    /// Tests only: none of this has been seen on the owner's PC.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_EggPolish12
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

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }

        // ---- the jokes from after October 2003 ----

        private static readonly string[] CutJokes =
            ["chicken", "achievement unlocked", "over 9000", "cool story", "i see what you did", "cake is a lie"];

        private static string[] PrivateLines(Type type, string field) =>
            (string[])type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

        [Test]
        public void LeeroyStaysButHisChickenPunchlineIsGone()
        {
            string[] all = AutonomousRaidLeeroy.Shouts.Concat(AutonomousRaidLeeroy.Reactions).Concat(AutonomousRaidLeeroy.FactionCalls)
                .Concat(AutonomousRaidLeeroy.AfterWin).Concat(AutonomousRaidLeeroy.AfterWipe).ToArray();
            Assert.That(AutonomousRaidLeeroy.Shouts, Has.All.StartWith("LEEEE"), "he still yells his name");
            Assert.That(all, Has.None.Contain("chicken").IgnoreCase);
            Assert.That(AutonomousRaidLeeroy.AfterWipe, Has.Length.EqualTo(3));
            Assert.That(AutonomousRaidLeeroy.Reactions, Has.Length.EqualTo(12));
        }

        [Test]
        public void TheRegularsBitsAreOfTheEraAndAgreeThatHorsesAreRented()
        {
            string[] bits = new[] { "HelperBits", "JokerBits", "ComplainerBits", "RecruiterBits", "VeteranBits" }
                .SelectMany(field => PrivateLines(typeof(AutonomousChatRegulars), field)).ToArray();
            foreach (string joke in CutJokes)
                Assert.That(bits, Has.None.Contain(joke).IgnoreCase, joke);
            // The dunce scene mocks owning a horse ("you rent the seat, you dont own the animal").
            Assert.That(bits, Has.None.Contain("my horse"));
            Assert.That(PrivateLines(typeof(AutonomousChatRegulars), "JokerBits"), Has.Length.EqualTo(6), "the bits keep their ids");
        }

        [Test]
        public void NoCorpusLineCarriesACutJoke()
        {
            foreach (VC.Line line in Corpus.Lines)
            {
                string text = line.Text.ToLowerInvariant();
                foreach (string joke in CutJokes.Concat(["ikr", "ftw"]))
                    Assert.That(Regex.IsMatch(text, @"\b" + Regex.Escape(joke) + @"\b"), Is.False, $"{line.Id}: {line.Text}");
            }
        }

        // ---- Trials of Atlantis and Catacombs: rumours only ----

        [Test]
        public void MasterLevelsAreExplainedAsARumour()
        {
            Assert.That(AutonomousChatKnowledge.TryExplainTerm("what does ml mean", out string answer), Is.True);
            Assert.That(answer, Does.Contain("rumour"));
        }

        [Test]
        public void TheHistoryTellsAtlantisAsHarbourTalkNotAsAVoyageMade()
        {
            string dir = Path.Combine(RepoRoot(), "source", "server", "GameServer", "bots", "autonomous", "data", "lore", "history");
            int seen = 0;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
                foreach (string text in Texts(JsonDocument.Parse(File.ReadAllText(file)).RootElement))
                {
                    if (!text.Contains("Atlantis", StringComparison.OrdinalIgnoreCase))
                        continue;
                    seen++;
                    Assert.That(text, Does.Contain("harbour talk").And.Contain("nobody has sailed there"), $"{Path.GetFileName(file)}: {text}");
                    Assert.That(text, Does.Not.Contain("they found the island remnants"));
                    Assert.That(text, Does.Not.Match(@"ships sail from"));
                }
            Assert.That(seen, Is.EqualTo(3), "one rumour a realm");
        }

        private static IEnumerable<string> Texts(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                        if (property.Name != "source")
                            foreach (string text in Texts(property.Value))
                                yield return text;
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        foreach (string text in Texts(item))
                            yield return text;
                    break;
                case JsonValueKind.String:
                    yield return element.GetString();
                    break;
            }
        }

        // ---- Caledonia's quests follow its bracket ----

        [TestCase("CaleKillQuestAlb", (ushort)250)] [TestCase("CaleKillQuestHib", (ushort)250)] [TestCase("CaleKillQuestMid", (ushort)250)]
        [TestCase("CaleKeepCaptureAlb", (ushort)250)] [TestCase("CaleKeepCaptureHib", (ushort)250)] [TestCase("CaleKeepCaptureMid", (ushort)250)]
        [TestCase("ThidKillQuestAlb", (ushort)252)] [TestCase("ThidKillQuestHib", (ushort)252)] [TestCase("ThidKillQuestMid", (ushort)252)]
        [TestCase("ThidKeepCaptureAlb", (ushort)252)] [TestCase("ThidKeepCaptureHib", (ushort)252)] [TestCase("ThidKeepCaptureMid", (ushort)252)]
        public void ABattlegroundDailyAsksTheLevelsOfItsBracket(string quest, ushort region)
        {
            Type type = typeof(BattlegroundBrackets).Assembly.GetTypes().Single(t => t.Name == quest);
            int Const(string name) => (int)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
            BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.ForRegion(region);
            Assert.That((Const("minimumLevel"), Const("maximumLevel")), Is.EqualTo(((int)bracket.MinLevel, (int)bracket.MaxLevel)), quest);
        }

        // ---- a raid named for its boss ----

        [TestCase("Golestandt", "Golestandt", true)]
        [TestCase("Coliath", "Coliath", true)]
        [TestCase("Wretch of Winter (jotun)", "Wretch of Winter", true)]
        [TestCase("Cuuldurach", "Cuuldurach the Glimmer King", true)]
        [TestCase("Galladoria", "Conservator", false)]
        [TestCase("Caer Sidi", "Apocalypse", false)]
        [TestCase(null, "Coliath", false)]
        public void ARaidCanBeNamedForItsBoss(string raid, string boss, bool named) =>
            Assert.That(RealmEventBanter.NamedForBoss(raid, boss), Is.EqualTo(named));

        [Test]
        public void AnOpenWorldWipeIsNotCalledADragonAndADragonWipeStillIs()
        {
            for (int i = 0; i < 40; i++)
            {
                string coliath = RealmEventBanter.RaidWipe("Coliath", "Coliath");
                Assert.That(coliath, Does.Contain("Coliath").And.Not.Contain("dragon").And.Not.Contain("ring").And.Not.Contain("in Coliath"), coliath);
                string dragon = RealmEventBanter.RaidWipe("Golestandt", "Golestandt");
                Assert.That(dragon, Does.Contain("ring").And.Not.Contain("in Golestandt"), dragon);
                Assert.That(RealmEventBanter.RaidWipe("Galladoria", "Conservator"), Does.Contain("Conservator"));
            }
        }

        [Test]
        public void ALineNamingBothIsSkippedOnlyWhenTheRaidIsNamedForItsBoss()
        {
            var same = new Dictionary<string, string> { ["raid"] = "Golestandt", ["boss"] = "Golestandt" };
            var dungeon = new Dictionary<string, string> { ["raid"] = "Galladoria", ["boss"] = "Conservator" };
            Assert.That(VC.NamesTheBossTwice("the raid killed {boss} in {raid}", same), Is.True);
            Assert.That(VC.NamesTheBossTwice("the raid killed {boss} in {raid}", dungeon), Is.False);
            Assert.That(VC.NamesTheBossTwice("heard the raid killed {boss}", same), Is.False);
            Assert.That(VC.NamesTheBossTwice("{raid} raid wipe", same), Is.False);
        }

        [TestCase("Golestandt", "Golestandt")]
        [TestCase("Coliath", "Coliath")]
        public void RaidNewsNeverSaysTheBossTwice(string raid, string boss)
        {
            var slots = new Dictionary<string, string> { ["raid"] = raid, ["boss"] = boss };
            var speaker = new VC.Speaker(11, "Teller", eRealm.Albion, "Cleric", 50, BotPersonalityType.Socialite,
                new HashSet<string> { "raid_won_recent", "raid_wiped_recent", "knows_player" });
            var random = new Random(5);
            int said = 0;
            foreach ((string kind, string topic) in new[] { ("news", "raid_won"), ("news", "raid_wiped"), ("tell", "news_tell") })
                for (int i = 0; i < 150; i++)
                {
                    var request = new VC.Request(kind, speaker, new Dictionary<string, string> { ["player"] = "Friend" }, null, DateTime.UtcNow, random)
                    {
                        Topic = topic,
                        NeedSlots = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["raid_won_recent"] = slots, ["raid_wiped_recent"] = slots },
                        Filter = line => line.Text.Contains("{boss}") || line.Text.Contains("{raid}"),
                    };
                    request = request with { Slots = kind == "news" ? slots : request.Slots };
                    if (!Corpus.TryPick(request, out VC.Picked picked))
                        continue;
                    said++;
                    Assert.That(Regex.Matches(picked.Text, Regex.Escape(boss), RegexOptions.IgnoreCase).Count, Is.LessThanOrEqualTo(1), picked.Text);
                }
            Assert.That(said, Is.GreaterThan(300), "the lines that name one of them are still said");
        }

        // ---- holidays ----

        [TestCase("happy new years!")]
        [TestCase("happy new year all")]
        [TestCase("Happy New Years Eve")]
        [TestCase("happy newyear")]
        public void HappyNewYearsIsAWish(string text) => Assert.That(AutonomousChatIntentModel.IsHolidayWish(text), Is.True, text);

        [Test]
        public void TheHolidayHintNamesAWishTheBotsAnswer()
        {
            foreach (string name in AutonomousHolidays.Names)
            {
                string hint = LivingServerTestCommandHandler.HolidayHint(name, new DateTime(2026, 12, 31));
                string wish = AutonomousHolidays.Wish(name);
                if (name == "aprilfools")
                {
                    Assert.That(wish, Is.Null);
                    Assert.That(hint, Does.Not.Contain("happy"), hint);
                    continue;
                }
                Assert.That(wish, Is.Not.Null, name);
                Assert.That(AutonomousChatIntentModel.IsHolidayWish(wish), Is.True, wish);
                Assert.That(hint, Does.Contain($"\"{wish}\""), hint);
            }
        }

        [TestCase(2024, 4, 1, "Easter Monday")]
        [TestCase(2029, 4, 1, "Easter Sunday")]
        public void EasterWinsOnEasterWeekendAndAprilFoolsIsStillAcknowledged(int year, int month, int day, string what)
        {
            var date = new DateTime(year, month, day);
            Assert.That(AutonomousHolidays.For(date)?.Name, Is.EqualTo("easter"), what);
            Assert.That(AutonomousHolidays.Flags(date), Does.Contain("holiday_easter").And.Contain("holiday_aprilfools").And.Contain("holiday_today"), what);
            Assert.That(AutonomousHolidays.Flags(date).Count(flag => flag == "holiday"), Is.EqualTo(1));
            Assert.That(AutonomousHolidays.AmbientShare(date), Is.EqualTo(AutonomousHolidays.DayShare));
        }

        [Test]
        public void AprilFoolsAloneIsUnchanged()
        {
            var date = new DateTime(2026, 4, 1); // Easter 2026 is 5 April
            Assert.That(AutonomousHolidays.For(date)?.Name, Is.EqualTo("aprilfools"));
            Assert.That(AutonomousHolidays.Flags(date), Is.EquivalentTo(new[] { "holiday", "holiday_aprilfools", "holiday_today" }));
        }

        // ---- roleplayer words ----

        [TestCase("see ya", "see thee")]
        [TestCase("see you later", "see thee later")]
        [TestCase("ur welcome", "thou art welcome")]
        [TestCase("your welcome", "thou art welcome")]
        [TestCase("ur right", "thou art right")]
        [TestCase("can u heal", "canst thou heal")]
        [TestCase("could you buff me", "couldst thou buff me")]
        [TestCase("no one is here", "none is here")]
        [TestCase("i have no gold", "i have no coin")]
        [TestCase("no, not yet", "nay, not yet")]
        [TestCase("no idea, sorry", "i know not, forgive me")]
        [TestCase("no problem", "'tis no trouble")]
        [TestCase("you go first", "thou go first")]
        [TestCase("ya, sounds good", "aye, sounds good")]
        [TestCase("nice, your armor is shiny", "nice, thine armor is shiny")]
        [TestCase("ur sword is nice", "thy sword is nice")]
        [TestCase("thank you, are you ok?", "my thanks, art thou very well?")]
        [TestCase("lol no", "*chuckles* nay")]
        [TestCase("thanks u", "my thanks thee")]
        public void RoleplayerWordsAreReadByTheirPlace(string line, string olde) =>
            Assert.That(AutonomousRoleplayers.Speak(line, new Random(1)).Split(new[] { ", forsooth", ", m'lord", " *bows*", ", huzzah!", ", by my troth", ", verily", " *tips hat*" },
                StringSplitOptions.None)[0].TrimEnd('?', '.', '!', ' '), Is.EqualTo(olde.TrimEnd('?', '.', '!', ' ')));

        // ---- the GM's Leeroy "now" lapses ----

        [Test]
        public void LeeroyNowLapsesAfterTenMinutesAndIsUsedOnce()
        {
            long clock = 1_000_000;
            Func<long> real = AutonomousRaidLeeroy.Clock;
            AutonomousRaidLeeroy.Clock = () => clock;
            try
            {
                AutonomousRaidLeeroy.ChargeNow();
                Assert.That(AutonomousRaidLeeroy.ChargeNowArmed, Is.True);
                clock += AutonomousRaidLeeroy.ChargeNowWindowMilliseconds + 1;
                Assert.That(AutonomousRaidLeeroy.ChargeNowArmed, Is.False, "lapsed");
                Assert.That(AutonomousRaidLeeroy.GoesNow(false, 30), Is.False, "a later Leeroy raid is not charged by an old \"now\"");
                Assert.That(AutonomousRaidLeeroy.GoesNow(false, 30), Is.False);

                AutonomousRaidLeeroy.ChargeNow();
                clock += AutonomousRaidLeeroy.ChargeNowWindowMilliseconds - 1;
                Assert.That(AutonomousRaidLeeroy.GoesNow(false, 1), Is.False, "one raider alone waits");
                Assert.That(AutonomousRaidLeeroy.GoesNow(false, 2), Is.True, "inside the window");
                Assert.That(AutonomousRaidLeeroy.GoesNow(false, 2), Is.False, "used once");
                Assert.That(AutonomousRaidLeeroy.ChargeNowArmed, Is.False);
                Assert.That(AutonomousRaidLeeroy.GoesNow(true, 30), Is.True, "his own rules still apply");
            }
            finally
            {
                AutonomousRaidLeeroy.Clock = real;
            }
        }

        // ---- /lstest list ----

        [Test]
        public void LsTestListNamesEveryPart()
        {
            string all = string.Join("\n", LivingServerTestCommandHandler.ListLines());
            foreach (string part in new[] { "voices", "debates", "tells", "memory", "deeds", "storylines", "spammers and roleplayers", "rumours",
                         "tales", "tale raids", "beggars", "bonds", "atmosphere lines", "cosmetics", "holidays", "leeroy" })
                Assert.That(all, Does.Contain(part + " "), part);
        }

        // ---- "tonight" at any hour ----

        [Test]
        public void TheRaidRumourAndTheKeepTellFitAnyHour()
        {
            Assert.That(AutonomousTales.RaidRumours, Has.None.Contain("tonight"));
            Assert.That(AutonomousTales.RaidRumours, Has.All.Contain("{boss}"));
            VC.Line[] keepFell = Corpus.Lines.Where(line => line.Kind == "tell" && line.Text.Contains("{keep} fell")).ToArray();
            Assert.That(keepFell, Is.Not.Empty);
            Assert.That(keepFell.Select(line => line.Text), Has.None.Contain("tonight"));
        }
    }
}
