using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>Holiday chatter and Leeroy (owner 2026-10-28: both pulled into Beta 1.1).</summary>
    [TestFixture]
    public class UT_HolidaysAndLeeroy
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

        private static Dictionary<string, string> Slots() => new(StringComparer.Ordinal)
        {
            ["me"] = "Aldwin", ["class"] = "armsman", ["level"] = "30", ["realm"] = "Albion", ["capital"] = "Camelot",
            ["monster"] = "bwca", ["q"] = "",
        };

        private static VC.Speaker Speaker(eRealm realm, params string[] flags) =>
            new(301, "Aldwin", realm, "Armsman", 30, BotPersonalityType.Socialite, new HashSet<string>(flags, StringComparer.Ordinal));

        [Test]
        public void MovableHolidaysFallOnTheRightDays()
        {
            Assert.That(AutonomousHolidays.EasterSunday(2024), Is.EqualTo(new DateTime(2024, 3, 31)));
            Assert.That(AutonomousHolidays.EasterSunday(2026), Is.EqualTo(new DateTime(2026, 4, 5)));
            Assert.That(AutonomousHolidays.EasterSunday(2027), Is.EqualTo(new DateTime(2027, 3, 28)));
            Assert.That(AutonomousHolidays.Thanksgiving(2025), Is.EqualTo(new DateTime(2025, 11, 27)));
            Assert.That(AutonomousHolidays.Thanksgiving(2026), Is.EqualTo(new DateTime(2026, 11, 26)));
        }

        [Test]
        public void TheCalendarSetsTheWindowAndTheDay()
        {
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 10, 28)), Is.EquivalentTo(new[] { "holiday", "holiday_halloween" }));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 10, 31, 21, 0, 0)),
                Is.EquivalentTo(new[] { "holiday", "holiday_halloween", "holiday_today" }));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 2)), Is.Empty);
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 12, 25)), Does.Contain("holiday_christmas").And.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 12, 31)), Does.Contain("holiday_newyear").And.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2027, 1, 2)), Does.Contain("holiday_newyear").And.Not.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2027, 1, 3)), Is.Empty);
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 11, 26)), Does.Contain("holiday_thanksgiving").And.Contain("holiday_today"));
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 4, 3)), Does.Contain("holiday_easter"), "Good Friday");
            Assert.That(AutonomousHolidays.Flags(new DateTime(2026, 7, 4)), Is.Empty);
            Assert.That(AutonomousHolidays.AmbientShare(new DateTime(2026, 7, 4)), Is.Zero);
            Assert.That(AutonomousHolidays.AmbientShare(new DateTime(2026, 10, 28)), Is.EqualTo(AutonomousHolidays.WindowShare));
            Assert.That(AutonomousHolidays.AmbientShare(new DateTime(2026, 10, 31)), Is.EqualTo(AutonomousHolidays.DayShare));
        }

        [Test]
        public void HolidayLinesAreSaidOnlyInTheirWindow()
        {
            var byId = Corpus.Lines.ToDictionary(line => line.Id);
            var random = new Random(5);
            VC.Speaker october = Speaker(eRealm.Hibernia, "solo", "outdoors", "evening", "holiday", "holiday_halloween", "holiday_today");
            var seen = new HashSet<string>();
            for (int draw = 0; draw < 200; draw++)
            {
                Assert.That(Corpus.TryPick("ambient", october, Slots(), null, DateTime.UtcNow, random, out string text, out string id, topic: "holiday"),
                    Is.True);
                Assert.That(byId[id].Needs, Does.Contain("holiday_halloween"), text);
                Assert.That(byId[id].Realm, Is.AnyOf("*", "hib"), text);
                seen.Add(text);
            }
            Assert.That(seen.Count, Is.GreaterThan(20), "the window has variety");
            VC.Speaker july = Speaker(eRealm.Hibernia, "solo", "outdoors", "evening");
            Assert.That(Corpus.TryPick("ambient", july, Slots(), null, DateTime.UtcNow, random, out _, out _, topic: "holiday"), Is.False);
            for (int draw = 0; draw < 300; draw++)
                if (Corpus.TryPick("ambient", july, Slots(), null, DateTime.UtcNow, random, out _, out string id))
                    Assert.That(byId[id].Needs.Any(need => need.StartsWith("holiday", StringComparison.Ordinal)), Is.False, id);
            // Every holiday has its openers.
            foreach (string name in new[] { "halloween", "thanksgiving", "christmas", "newyear", "valentines", "stpatricks", "aprilfools", "easter", "midsummer" })
                Assert.That(Corpus.Lines.Count(l => l.Kind == "ambient" && l.Topic == "holiday" && l.Needs.Contains("holiday_" + name)),
                    Is.GreaterThanOrEqualTo(3), name);
        }

        [Test]
        public void AHolidayWishIsAGreetingAndGetsTheHolidaysAnswer()
        {
            Assert.That(AutonomousChatIntentModel.Predict("happy halloween!"), Is.EqualTo(eAutonomousChatIntent.Greeting));
            Assert.That(AutonomousChatIntentModel.Predict("merry christmas everyone"), Is.EqualTo(eAutonomousChatIntent.Greeting));
            var random = new Random(9);
            VC.Speaker speaker = Speaker(eRealm.Albion, "solo", "outdoors", "evening", "holiday", "holiday_halloween", "holiday_today");
            int holiday = 0, total = 0;
            for (int draw = 0; draw < 200; draw++)
            {
                if (!Corpus.TryPick("reply", speaker, Slots(), null, DateTime.UtcNow, random, out string text, out _, "greeting"))
                    continue;
                total++;
                if (text.Contains("halloween", StringComparison.OrdinalIgnoreCase) || text.Contains("boo", StringComparison.OrdinalIgnoreCase))
                    holiday++;
            }
            Assert.That(total, Is.GreaterThan(150));
            Assert.That(holiday, Is.GreaterThan(total / 3), "most holiday greetings are answered in kind");
        }

        [Test]
        public void LeeroyRollsOneRaidInTwentyAndWaitsForPartOfTheRaid()
        {
            Assert.That(AutonomousRaidLeeroy.Rolls(0.049), Is.True);
            Assert.That(AutonomousRaidLeeroy.Rolls(0.05), Is.False);
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(30, 80, 90_000, 0, false), Is.True, "a quarter there, a minute in");
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(30, 80, 30_000, 0, false), Is.False, "too soon");
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(10, 80, 90_000, 0, false), Is.False, "too few to follow him");
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(80, 80, 90_000, 0, false), Is.False, "everyone is there: that's the pull");
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(30, 80, 90_000, 1, false), Is.False, "never after a wipe");
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(30, 80, 90_000, 0, true), Is.False, "never while regrouping");
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(8, 20, 90_000, 0, false), Is.True, "a small raid: eight will do");
            Assert.That(AutonomousRaidLeeroy.ShouldChargeStaging(60, 100, 4 * 60_000), Is.True);
            Assert.That(AutonomousRaidLeeroy.ShouldChargeStaging(40, 100, 4 * 60_000), Is.False);
            Assert.That(AutonomousRaidLeeroy.ShouldChargeStaging(60, 100, 60_000), Is.False);
        }

        [Test]
        public void LeeroyIsADamageDealerAndUsuallyADaredevil()
        {
            Assert.That(AutonomousRaidLeeroy.CanBeLeeroy(eCharacterClass.Armsman), Is.True);
            Assert.That(AutonomousRaidLeeroy.CanBeLeeroy(eCharacterClass.Berserker), Is.True);
            Assert.That(AutonomousRaidLeeroy.CanBeLeeroy(eCharacterClass.Wizard), Is.True);
            Assert.That(AutonomousRaidLeeroy.CanBeLeeroy(eCharacterClass.Cleric), Is.False);
            Assert.That(AutonomousRaidLeeroy.CanBeLeeroy(eCharacterClass.Minstrel), Is.False);
            Assert.That(AutonomousRaidLeeroy.CanBeLeeroy(eCharacterClass.Healer), Is.False);
            Assert.That(AutonomousRaidLeeroy.Eagerness(BotPersonalityType.Daredevil),
                Is.GreaterThan(AutonomousRaidLeeroy.Eagerness(BotPersonalityType.NoLifer)));
            Assert.That(AutonomousRaidLeeroy.Eagerness(BotPersonalityType.NoLifer),
                Is.GreaterThan(AutonomousRaidLeeroy.Eagerness(BotPersonalityType.Leveler)));
            Assert.That(AutonomousRaidLeeroy.Fill("thanks a lot {leeroy}", "Brannoc", "Legion"), Is.EqualTo("thanks a lot Brannoc"));
            Assert.That(AutonomousRaidLeeroy.Shouts.All(s => s.StartsWith("LEE", StringComparison.Ordinal)), Is.True);
            Assert.That(AutonomousRaidLeeroy.Reactions.Concat(AutonomousRaidLeeroy.AfterWin).Concat(AutonomousRaidLeeroy.AfterWipe)
                .All(line => line.Length <= 140), Is.True);
        }
    }
}
