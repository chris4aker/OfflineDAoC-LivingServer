using System;
using System.Linq;
using DOL.Database;
using DOL.Database.Handlers;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Ports of OpenDAoC-Core b49257e7f, 4198e9bc4 and 19123fffd: an object is marked saved only once its write is
    /// durable, single-row saves need no transaction, and an object flagged Dirty with no real change is not rewritten.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_UpstreamSaveSafety
    {
        private static readonly DateTime Seeded = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        [Test]
        public void FailedCommit_LeavesEveryRowDirtyAndUnsaved()
        {
            string path = TemporarySqlite.NewDatabasePath("daoc-save-safety");
            try
            {
                SqliteObjectDatabase database = Open(path);
                DbSinglePermission first = new() { PlayerID = "first", Command = "before" };
                DbSinglePermission second = new() { PlayerID = "second", Command = "before" };
                Assert.That(database.AddObject([first, second]), Is.True);
                first = database.FindObjectByKey<DbSinglePermission>(first.ObjectId);
                second = database.FindObjectByKey<DbSinglePermission>(second.ObjectId);

                // A deferred foreign key is only checked at COMMIT, so the UPDATEs succeed and the commit fails.
                // DDL affects no rows, so ExecuteNonQuery reports false here; the asserts below prove the guard works.
                database.ExecuteNonQuery(
                    "CREATE TABLE `CommitGuardParent` (`Id` TEXT PRIMARY KEY);" +
                    "CREATE TABLE `CommitGuardChild` (`ParentId` TEXT REFERENCES `CommitGuardParent`(`Id`) DEFERRABLE INITIALLY DEFERRED);" +
                    "CREATE TRIGGER `CommitGuard` AFTER UPDATE ON `SinglePermission` WHEN NEW.`Command` = 'boom' " +
                    "BEGIN INSERT INTO `CommitGuardChild` (`ParentId`) VALUES ('missing'); END;");

                first.Command = "after";
                second.Command = "boom";
                Assert.That(database.SaveObject([first, second]), Is.False, "A rolled-back batch must not report success.");
                Assert.That(first.Dirty, Is.True, "A rolled-back row must stay Dirty so the next flush retries it.");
                Assert.That(second.Dirty, Is.True);

                SqliteObjectDatabase fresh = Open(path);
                Assert.That(fresh.SelectAllObjects<DbSinglePermission>().Select(row => row.Command), Is.All.EqualTo("before"));

                // The retry, once the cause is gone, writes the row that was rolled back.
                second.Command = "fixed";
                Assert.That(database.SaveObject([first, second]), Is.True);
                Assert.That(first.Dirty, Is.False);
                Assert.That(Open(path).FindObjectByKey<DbSinglePermission>(first.ObjectId).Command, Is.EqualTo("after"));
            }
            finally
            {
                TemporarySqlite.DeleteDatabase(path);
            }
        }

        [Test]
        public void SingleRowSave_WithoutTransaction_IsSavedAndClean()
        {
            string path = TemporarySqlite.NewDatabasePath("daoc-save-safety");
            try
            {
                SqliteObjectDatabase database = Open(path);
                DbSinglePermission row = new() { PlayerID = "solo", Command = "before" };
                Assert.That(database.AddObject(row), Is.True);
                row = database.FindObjectByKey<DbSinglePermission>(row.ObjectId);
                row.Command = "after";

                Assert.That(database.SaveObject(row), Is.True);
                Assert.That(row.Dirty, Is.False);
                Assert.That(Open(path).FindObjectByKey<DbSinglePermission>(row.ObjectId).Command, Is.EqualTo("after"));
            }
            finally
            {
                TemporarySqlite.DeleteDatabase(path);
            }
        }

        [Test]
        public void DirtyFlagWithoutChange_DoesNotRewriteTheRow()
        {
            string path = TemporarySqlite.NewDatabasePath("daoc-save-safety");
            try
            {
                SqliteObjectDatabase database = Open(path);
                DbSinglePermission row = new() { PlayerID = "unchanged", Command = "same" };
                Assert.That(database.AddObject(row), Is.True);
                Assert.That(row.LastTimeRowUpdated, Is.GreaterThan(Seeded), "New rows are stamped when inserted.");
                Assert.That(database.ExecuteNonQuery(
                    $"UPDATE `SinglePermission` SET `LastTimeRowUpdated` = '2001-02-03 04:05:06' WHERE `SinglePermission_ID` = '{row.ObjectId}'"), Is.True);

                DbSinglePermission loaded = database.FindObjectByKey<DbSinglePermission>(row.ObjectId);
                loaded.Dirty = true; // e.g. an inventory item copied to its live type on load, or moved and moved back
                Assert.That(loaded.GetDirtyBindings(new DataTableHandler(typeof(DbSinglePermission))), Is.Empty);
                Assert.That(database.SaveObject(loaded), Is.True);
                Assert.That(loaded.Dirty, Is.False);
                Assert.That(Open(path).FindObjectByKey<DbSinglePermission>(row.ObjectId).LastTimeRowUpdated, Is.EqualTo(Seeded).Within(TimeSpan.FromSeconds(1)),
                    "Nothing changed, so the row is not written and keeps its stamp.");

                loaded.Command = "changed";
                Assert.That(database.SaveObject(loaded), Is.True);
                DbSinglePermission saved = Open(path).FindObjectByKey<DbSinglePermission>(row.ObjectId);
                Assert.That(saved.Command, Is.EqualTo("changed"));
                Assert.That(saved.LastTimeRowUpdated, Is.GreaterThan(Seeded.AddYears(10)), "A real change stamps the row.");
            }
            finally
            {
                TemporarySqlite.DeleteDatabase(path);
            }
        }

        private static SqliteObjectDatabase Open(string path)
        {
            SqliteObjectDatabase database = new($"Data Source={path};Version=3;Pooling=False;Foreign Keys=True;");
            database.RegisterDataObject(typeof(DbSinglePermission));
            return database;
        }
    }
}
