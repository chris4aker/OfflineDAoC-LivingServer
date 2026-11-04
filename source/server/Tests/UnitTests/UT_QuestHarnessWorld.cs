using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.UnitTests.QuestHarness;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The quest harness's world mode (docs/QUESTING.md): every quest row of a world database walked, and a report of
    /// which quests can't be completed and why. The real rows ship inside the game, not in Git, so the world test runs
    /// only when QUEST_HARNESS_WORLD names a world database; the other tests here run the same report on a small world
    /// built in a temporary folder.
    ///
    ///   QUEST_HARNESS_WORLD=C:\Games\OfflineDAoC\runtime\data\opendaoc.sqlite3.db
    ///   QUEST_HARNESS_CLASSIC=C:\Games\OfflineDAoC\runtime\server\classic-quests.json   (optional; found beside the world by default)
    ///   QUEST_HARNESS_REPORT=C:\Temp\quest-report.md                                    (optional; default %TEMP%\quest-harness-report.md)
    ///   QUEST_HARNESS_ONLY=20188,20157                                                  (optional; only these quest IDs)
    ///   dotnet test source/server/Tests/Tests.csproj -c Release --filter "FullyQualifiedName~UT_QuestHarnessWorld"
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_QuestHarnessWorld
    {
        private bool _questXpByLevel;
        private string _xpRateByLevel;

        [SetUp]
        public void SetUp()
        {
            _questXpByLevel = Properties.QUEST_XP_BY_LEVEL;
            _xpRateByLevel = Properties.XP_RATE_BY_LEVEL;
        }

        [TearDown]
        public void TearDown()
        {
            Properties.QUEST_XP_BY_LEVEL = _questXpByLevel;
            Properties.XP_RATE_BY_LEVEL = _xpRateByLevel;
        }

        [Test]
        public void TheWorldsQuestRows()
        {
            string path = Environment.GetEnvironmentVariable("QUEST_HARNESS_WORLD");
            if (string.IsNullOrWhiteSpace(path))
                Assert.Ignore("Set QUEST_HARNESS_WORLD to a world database (runtime\\data\\opendaoc.sqlite3.db) to walk its quest rows.");

            string classicPath = Environment.GetEnvironmentVariable("QUEST_HARNESS_CLASSIC");
            if (string.IsNullOrWhiteSpace(classicPath))
            {
                string beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "..", "server", "classic-quests.json");
                classicPath = File.Exists(beside) ? Path.GetFullPath(beside) : null;
            }
            ClassicQuests.Config classic = classicPath == null ? new ClassicQuests.Config() : ClassicQuests.Read(classicPath);
            HashSet<int> only = (Environment.GetEnvironmentVariable("QUEST_HARNESS_ONLY") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse).ToHashSet();
            string reportPath = Environment.GetEnvironmentVariable("QUEST_HARNESS_REPORT");
            if (string.IsNullOrWhiteSpace(reportPath))
                reportPath = Path.Combine(Path.GetTempPath(), "quest-harness-report.md");

            string before = WorldDatabase.Sha256(path);
            List<QuestRowReport> reports;
            string markdown;
            using (var world = new WorldDatabase(path))
            {
                reports = QuestWorldReport.Run(world, classic, only.Count == 0 ? null : row => only.Contains(row.ID));
                markdown = QuestWorldReport.Markdown(reports, world, classicPath, DateTime.UtcNow);
            }
            File.WriteAllText(reportPath, markdown);
            TestContext.Out.WriteLine($"Report: {reportPath}");
            TestContext.Out.WriteLine($"{reports.Count} quest rows walked, {reports.Count(r => r.Passed)} finished, {reports.Count(r => !r.Passed)} can't be completed.");

            Assert.That(WorldDatabase.Sha256(path), Is.EqualTo(before), "the world database was not changed");
            Assert.That(reports, Is.Not.Empty, "the world has quest rows");
        }

        // The world mode on a small world of its own

        private static string SmallWorld()
        {
            string path = TemporarySqlite.NewDatabasePath("quest-harness-small-world");
            using var connection = new SQLiteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE DataQuest (ID INTEGER PRIMARY KEY, Name TEXT, StartType INT, StartName TEXT, StartRegionID INT, AcceptText TEXT,
  Description TEXT, SourceName TEXT, SourceText TEXT, StepType TEXT, StepText TEXT, StepItemTemplates TEXT, AdvanceText TEXT,
  TargetName TEXT, TargetText TEXT, CollectItemTemplate TEXT, MaxCount INT, MinLevel INT, MaxLevel INT, RewardMoney TEXT,
  RewardXP TEXT, RewardCLXP TEXT, RewardRP TEXT, RewardBP TEXT, OptionalRewardItemTemplates TEXT, FinalRewardItemTemplates TEXT,
  FinishText TEXT, QuestDependency TEXT, AllowedClasses TEXT, ClassType TEXT, LastTimeRowUpdated TEXT);
CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT, MaxCount INT, PackSize INT, Weight INT, Item_Type INT, Object_Type INT,
  IsDropable INT, IsTradable INT, IsPickable INT);
CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT, Region INT);
INSERT INTO ItemTemplate VALUES ('cq_letter', 'sealed letter', 1, 1, 1, 40, 0, 1, 1, 1);
INSERT INTO ItemTemplate VALUES ('cq_ring', 'ring of thanks', 1, 1, 1, 40, 0, 1, 1, 1);
INSERT INTO Mob VALUES ('m1', 'Master Edric', 1);
INSERT INTO Mob VALUES ('m2', 'Lady Elaine', 1);
INSERT INTO Mob VALUES ('m3', 'arawnite messenger', 1);
INSERT INTO DataQuest (ID, Name, StartType, StartName, StartRegionID, AcceptText, Description, SourceText, StepType, StepText,
  StepItemTemplates, AdvanceText, TargetName, TargetText, CollectItemTemplate, MaxCount, MinLevel, MaxLevel, RewardMoney, RewardXP,
  RewardCLXP, RewardRP, RewardBP, FinalRewardItemTemplates, FinishText)
VALUES (1, 'A Letter', 0, 'Master Edric', 1, 'letter', 'Take this [letter]?', 'Go.|Kill one.|Back.', '2|0|5', 'Deliver|Kill|Return',
  'cq_letter||', '||', 'Lady Elaine;1|arawnite messenger;1|Master Edric;1', 'Thanks.|Got one.|Done.', '||', 1, 10, 15,
  '0|0|100', '0|0|500', '0|0|0', '0|0|0', '0|0|0', 'cq_ring', 'Thank you.');
INSERT INTO DataQuest (ID, Name, StartType, StartName, StartRegionID, AcceptText, Description, SourceText, StepType, StepText,
  StepItemTemplates, AdvanceText, TargetName, TargetText, CollectItemTemplate, MaxCount, MinLevel, MaxLevel, RewardMoney, RewardXP,
  RewardCLXP, RewardRP, RewardBP, FinishText)
VALUES (2, 'The Lost Map', 0, 'Master Edric', 1, 'map', 'Find my [map]?', 'Go.|Back.', '4|3', 'Ask|Bring',
  '|cq_map', '|', 'Old Hermit;1|Master Edric;1', 'Hm.|Thanks.', '|', 1, 10, 15, '0|0', '0|0', '0|0', '0|0', '0|0', 'Thank you.');";
            command.ExecuteNonQuery();
            return path;
        }

        [Test]
        public void TheReportSaysWhichQuestsCantBeCompletedAndWhy()
        {
            string path = SmallWorld();
            try
            {
                string before = WorldDatabase.Sha256(path);
                List<QuestRowReport> reports;
                string markdown;
                using (var world = new WorldDatabase(path))
                {
                    reports = QuestWorldReport.Run(world, new ClassicQuests.Config());
                    markdown = QuestWorldReport.Markdown(reports, world, null, DateTime.UtcNow);
                }

                QuestRowReport letter = reports.Single(r => r.Row.ID == 1);
                QuestRowReport map = reports.Single(r => r.Row.ID == 2);
                Assert.Multiple(() =>
                {
                    Assert.That(letter.Passed, Is.True, string.Join(Environment.NewLine, letter.Walks));
                    Assert.That(letter.Walks.Select(w => w.Notes.FirstOrDefault()), Has.Some.EqualTo("kills by a gamebot in the group"),
                        "a quest with a kill step is walked again with the kills made by a gamebot in the group");
                    Assert.That(letter.Walks, Has.Count.EqualTo(3), "and again outlevelled before its hand-in");
                    Assert.That(letter.Walks[0].ItemsReceived, Does.Contain("cq_ring"));
                    Assert.That(letter.WorldProblems, Is.Empty);
                    Assert.That(letter.Walks.SelectMany(w => w.Log), Is.Empty, "the quest code logged nothing");

                    Assert.That(map.Passed, Is.False);
                    Assert.That(map.Walks[0].Stuck, Does.Contain("cq_map"), "step 1 hands the map that isn't an item template");
                    Assert.That(map.WorldProblems, Has.Some.Contains("\"Old Hermit\" does not stand in region 1"));
                    Assert.That(map.WorldProblems, Has.Some.Contains("cq_map is not an item template"));

                    Assert.That(markdown, Does.Contain("can't be completed: 1"));
                    Assert.That(markdown, Does.Contain("| 2 | The Lost Map |"));
                });
                Assert.That(WorldDatabase.Sha256(path), Is.EqualTo(before), "the world database is never written");
                Assert.That(File.Exists(path + "-wal"), Is.False, "nor opened for writing beside it");
            }
            finally
            {
                TemporarySqlite.DeleteDatabase(path);
            }
        }

        [Test]
        public void TheWorldIsReadFromACopyOpenedReadOnly()
        {
            string path = SmallWorld();
            try
            {
                using var world = new WorldDatabase(path);
                Assert.That(world.All<DOL.Database.DbDataQuest>(), Has.Count.EqualTo(2));
                // The original is locked for writing by nobody: a writer can still open it (the harness holds a copy).
                using (var writer = new SQLiteConnection($"Data Source={path};Pooling=False"))
                {
                    writer.Open();
                    using SQLiteCommand touch = writer.CreateCommand();
                    touch.CommandText = "SELECT count(*) FROM DataQuest";
                    Assert.That(Convert.ToInt32(touch.ExecuteScalar()), Is.EqualTo(2));
                }
                Assert.That(world.HasTable("DataQuest"), Is.True);
            }
            finally
            {
                TemporarySqlite.DeleteDatabase(path);
            }
        }

        [TestCase("interacting with Lady Elaine did not advance step 3", "interacting with … did not advance step N")]
        [TestCase("the quest never handed cq_letter, so step 2 can't be delivered", "the quest never handed <item>, so step N can't be delivered")]
        public void FailuresAreGroupedByKind(string stuck, string kind)
            => Assert.That(QuestWorldReport.Category(stuck), Is.EqualTo(kind));
    }
}
