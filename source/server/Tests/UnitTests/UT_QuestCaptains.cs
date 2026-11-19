using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.Quests;
using NUnit.Framework;
using Post = DOL.GS.Quests.QuestCaptains.Post;
using Standing = DOL.GS.Quests.QuestCaptains.Standing;

namespace DOL.UnitTests
{
    /// <summary>
    /// The battleground and frontier quests come from a captain of each realm (1.2, owner 2026-11-15, "Rename to
    /// captains"), not from the Atlas freeshard's Pazz and Rey. The quests keep their classes (a saved quest is keyed by its
    /// class name, and the daily reset finds dailies by "DailyQuest" in it), so progress carries over.
    /// </summary>
    [TestFixture]
    public sealed class UT_QuestCaptains
    {
        private static readonly string[] Scripts =
        {
            "BattlegroundQuests/Thidranki/ThidKillQuestAlb.cs", "BattlegroundQuests/Thidranki/ThidKillQuestHib.cs",
            "BattlegroundQuests/Thidranki/ThidKillQuestMid.cs", "BattlegroundQuests/Thidranki/ThidKeepCaptureAlb.cs",
            "BattlegroundQuests/Thidranki/ThidKeepCaptureHib.cs", "BattlegroundQuests/Thidranki/ThidKeepCaptureMid.cs",
            "BattlegroundQuests/Caledonia/CaleKillQuestAlb.cs", "BattlegroundQuests/Caledonia/CaleKillQuestHib.cs",
            "BattlegroundQuests/Caledonia/CaleKillQuestMid.cs", "BattlegroundQuests/Caledonia/CaleKeepCaptureAlb.cs",
            "BattlegroundQuests/Caledonia/CaleKeepCaptureHib.cs", "BattlegroundQuests/Caledonia/CaleKeepCaptureMid.cs",
            "AtlasQuests/DailyQuests/Albion/RvR/Daily/PlayerKillQuestAlb.cs",
            "AtlasQuests/DailyQuests/Albion/RvR/Daily/EveryLittleBitHelpsQuestAlb.cs",
            "AtlasQuests/DailyQuests/Albion/RvR/Weekly/PlayerKillWeeklyQuestAlb.cs",
            "AtlasQuests/DailyQuests/Albion/RvR/Weekly/ForTheRealmQuestAlb.cs",
            "AtlasQuests/DailyQuests/Midgard/RvR/Daily/PlayerKillQuestMid.cs",
            "AtlasQuests/DailyQuests/Midgard/RvR/Daily/EveryLittleBitHelpsQuestMid.cs",
            "AtlasQuests/DailyQuests/Midgard/RvR/Weekly/PlayerKillWeeklyQuestMid.cs",
            "AtlasQuests/DailyQuests/Midgard/RvR/Weekly/ForTheRealmQuestMid.cs",
            "AtlasQuests/DailyQuests/Hibernia/RvR/Daily/PlayerKillQuestHib.cs",
            "AtlasQuests/DailyQuests/Hibernia/RvR/Daily/EveryLittleBitHelpsQuestHib.cs",
            "AtlasQuests/DailyQuests/Hibernia/RvR/Weekly/PlayerKillWeeklyQuestHib.cs",
            "AtlasQuests/DailyQuests/Hibernia/RvR/Weekly/ForTheRealmQuestHib.cs",
        };

        private static readonly Dictionary<eRealm, ushort[]> RealmModels = new()
        {
            [eRealm.Albion] = new[] { eLivingModel.BritonMale, eLivingModel.BritonFemale, eLivingModel.HighlanderMale,
                eLivingModel.HighlanderFemale, eLivingModel.SaracenMale, eLivingModel.SaracenFemale, eLivingModel.AvalonianMale,
                eLivingModel.AvalonianFemale }.Select(m => (ushort)m).ToArray(),
            [eRealm.Midgard] = new[] { eLivingModel.NorseMale, eLivingModel.NorseFemale, eLivingModel.TrollMale,
                eLivingModel.TrollFemale, eLivingModel.DwarfMale, eLivingModel.DwarfFemale, eLivingModel.KoboldMale,
                eLivingModel.KoboldFemale }.Select(m => (ushort)m).ToArray(),
            [eRealm.Hibernia] = new[] { eLivingModel.CeltMale, eLivingModel.CeltFemale, eLivingModel.FirbolgMale,
                eLivingModel.FirbolgFemale, eLivingModel.ElfMale, eLivingModel.ElfFemale, eLivingModel.LurikeenMale,
                eLivingModel.LurikeenFemale }.Select(m => (ushort)m).ToArray(),
        };

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }

        private static string ScriptText(string relative) =>
            File.ReadAllText(Path.Combine(RepoRoot(), "source", "server", "GameServer", "scripts", "quests", relative));

        private static Type QuestType(string script)
        {
            string name = Path.GetFileNameWithoutExtension(script);
            return typeof(QuestCaptains).Assembly.GetTypes().Single(t => t.Name == name && typeof(BaseQuest).IsAssignableFrom(t));
        }

        // The posts

        [Test]
        public void EachRealmHasItsOwnCaptainAtEachPost()
        {
            var expected = new Dictionary<(eRealm, ushort), string>
            {
                [(eRealm.Albion, QuestCaptains.Thidranki)] = "Captain Circa",
                [(eRealm.Hibernia, QuestCaptains.Thidranki)] = "Captain Cody",
                [(eRealm.Midgard, QuestCaptains.Thidranki)] = "Captain Astridr",
                [(eRealm.Albion, QuestCaptains.Caledonia)] = "Captain Aldwyn",
                [(eRealm.Hibernia, QuestCaptains.Caledonia)] = "Captain Fiachra",
                [(eRealm.Midgard, QuestCaptains.Caledonia)] = "Captain Thorgrim",
                [(eRealm.Albion, QuestCaptains.CamelotHills)] = "Captain Edric",
                [(eRealm.Midgard, QuestCaptains.Vale)] = "Captain Hallvard",
                [(eRealm.Hibernia, QuestCaptains.Connacht)] = "Captain Eithne",
            };
            Assert.That(QuestCaptains.Posts.ToDictionary(p => (p.Realm, p.Region), p => p.Name), Is.EquivalentTo(expected));
            Assert.That(QuestCaptains.Posts.Select(p => p.Name).Distinct().Count(), Is.EqualTo(QuestCaptains.Posts.Count));
        }

        [Test]
        public void CaptainsAreOfTheirRealmAndNotTheOldGivers()
        {
            Assert.Multiple(() =>
            {
                foreach (Post post in QuestCaptains.Posts)
                {
                    Assert.That(RealmModels[post.Realm], Does.Contain(post.Model), $"{post.Name}: a {post.Realm} race");
                    Assert.That(post.GuildName, Does.EndWith("Garrison"), post.Name);
                    Assert.That(QuestCaptains.IsLegacyGiver(post.Name, post.GuildName), Is.False, post.Name);
                    Assert.That(post.Name + post.GuildName, Does.Not.Contain("Pazz").And.Not.Contain("Rey").And.Not.Contain("Bone"));
                }
            });
        }

        [Test]
        public void AnUnknownPostIsAnError()
        {
            Assert.That(() => QuestCaptains.For(eRealm.Midgard, QuestCaptains.CamelotHills), Throws.ArgumentException);
        }

        // The old givers in a saved world

        private static readonly Post Circa = QuestCaptains.For(eRealm.Albion, QuestCaptains.Thidranki);

        [Test]
        public void ANewWorldMakesTheCaptain()
        {
            QuestCaptains.Decision d = QuestCaptains.Decide(Circa, new List<Standing>());
            Assert.Multiple(() =>
            {
                Assert.That(d.Create, Is.True);
                Assert.That(d.Remove, Is.Empty);
            });
        }

        [Test]
        public void ASavedPazzIsRenamedNotDuplicated()
        {
            QuestCaptains.Decision d = QuestCaptains.Decide(Circa, new[] { new Standing("Pazz", "Bone Collector", true) });
            Assert.Multiple(() =>
            {
                Assert.That(d.Create, Is.False);
                Assert.That(d.Keep, Is.EqualTo(0));
                Assert.That(d.Rename, Is.True);
                Assert.That(d.Remove, Is.Empty);
            });
        }

        [Test]
        public void OfTwoPazzTheSavedOneIsKeptAndTheOtherGoes()
        {
            QuestCaptains.Decision d = QuestCaptains.Decide(Circa, new[]
            {
                new Standing("Pazz", "Bone Collector", false),
                new Standing("Pazz", "bone collector", true),
            });
            Assert.Multiple(() =>
            {
                Assert.That(d.Keep, Is.EqualTo(1));
                Assert.That(d.Rename, Is.True);
                Assert.That(d.Remove, Is.EqualTo(new[] { 0 }));
            });
        }

        [Test]
        public void ACaptainAlreadyThereIsKeptAndAnOldGiverBesideItGoes()
        {
            QuestCaptains.Decision d = QuestCaptains.Decide(Circa, new[]
            {
                new Standing("Rey", "Bone Collector", true),
                new Standing("Captain Circa", "Thidranki Garrison", false),
            });
            Assert.Multiple(() =>
            {
                Assert.That(d.Keep, Is.EqualTo(1));
                Assert.That(d.Rename, Is.False);
                Assert.That(d.Remove, Is.EqualTo(new[] { 0 }));
            });
        }

        [Test]
        public void TheSecondStartChangesNothing()
        {
            // The first start renamed the saved Pazz and removed its twin; the post now holds only the captain.
            var there = new List<Standing> { new("Pazz", "Bone Collector", true), new("Pazz", "Bone Collector", false) };
            QuestCaptains.Decision first = QuestCaptains.Decide(Circa, there);
            var after = there.Select((s, i) => i == first.Keep ? new Standing(Circa.Name, Circa.GuildName, s.Saved) : s)
                .Where((s, i) => !first.Remove.Contains(i)).ToList();

            QuestCaptains.Decision second = QuestCaptains.Decide(Circa, after);
            Assert.Multiple(() =>
            {
                Assert.That(after.Select(s => s.Name), Is.EqualTo(new[] { "Captain Circa" }));
                Assert.That(second.Create, Is.False);
                Assert.That(second.Rename, Is.False);
                Assert.That(second.Remove, Is.Empty);
            });
        }

        [Test]
        public void OtherNpcsAtThePostAreNeverTouched()
        {
            // Only a Pazz or Rey titled "Bone Collector" is an old giver; a GM's own "Pazz" or the keep's merchants stay.
            QuestCaptains.Decision d = QuestCaptains.Decide(Circa, new[]
            {
                new Standing("Pazz", "Smith", true),
                new Standing("Siege Master", "Merchant", true),
                new Standing("Rey", "", true),
            });
            Assert.Multiple(() =>
            {
                Assert.That(d.Create, Is.True);
                Assert.That(d.Remove, Is.Empty);
            });
        }

        // The quest scripts

        [Test]
        public void NoScriptNamesPazzReyOrTheBoneCollector()
        {
            var post = new Regex(@"Pazz|\bRey\b|Bone Collector|\bFen\b|toeknuckle|skeleton|\[Daily\]|\[Weekly\]");
            Assert.Multiple(() =>
            {
                foreach (string script in Scripts)
                    Assert.That(post.Matches(ScriptText(script)).Select(m => m.Value).Distinct(), Is.Empty, script);
            });
        }

        [Test]
        public void EachScriptTakesItsRealmsCaptainAtItsPost()
        {
            var ensure = new Regex(@"QuestCaptains\.Ensure\(eRealm\.(\w+), (\d+)\)");
            Assert.Multiple(() =>
            {
                foreach (string script in Scripts)
                {
                    MatchCollection calls = ensure.Matches(ScriptText(script));
                    Assert.That(calls.Count, Is.EqualTo(1), script);
                    if (calls.Count != 1)
                        continue;
                    var realm = Enum.Parse<eRealm>(calls[0].Groups[1].Value);
                    ushort region = ushort.Parse(calls[0].Groups[2].Value);
                    string suffix = Path.GetFileNameWithoutExtension(script)[^3..];
                    Assert.That(realm, Is.EqualTo(suffix switch { "Alb" => eRealm.Albion, "Mid" => eRealm.Midgard, _ => eRealm.Hibernia }), script);
                    ushort expectedRegion = script.Contains("Thidranki") ? QuestCaptains.Thidranki
                        : script.Contains("Caledonia") ? QuestCaptains.Caledonia
                        : suffix switch { "Alb" => QuestCaptains.CamelotHills, "Mid" => QuestCaptains.Vale, _ => QuestCaptains.Connacht };
                    Assert.That(region, Is.EqualTo(expectedRegion), script);
                    Post captain = QuestCaptains.For(realm, region);
                    Assert.That(ScriptText(script), Does.Contain($"Return to {captain.Name} in {captain.Place}"), script);
                }
            });
        }

        [Test]
        public void TheQuestsKeepTheirClassesSoSavedProgressLoads()
        {
            // AbstractQuest saves DbQuest.Name = GetType().FullName and loads it back by that name; DailyQuestService clears
            // finished dailies by "DailyQuest" in it. The captains changed the titles, not the classes.
            Assert.Multiple(() =>
            {
                foreach (string script in Scripts)
                {
                    Type type = QuestType(script);
                    Assert.That(type.FullName, Does.Contain("DailyQuest").Or.Contain("WeeklyQuest"), script);
                    Assert.That(typeof(DailyQuest).IsAssignableFrom(type) || typeof(WeeklyQuest).IsAssignableFrom(type), Is.True, script);
                    var title = (string)type.GetField("questTitle", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
                    Assert.That(title, Does.Not.StartWith("["), script);
                }
            });
        }
    }
}
