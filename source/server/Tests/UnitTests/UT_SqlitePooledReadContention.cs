using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Handlers;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The "database is locked" race the release-gates work found (Living Server 1.1.2): about 3 runs in 50 on Windows,
    /// a pooled WAL read through SelectAllObjects failed with "database is locked" in under a second. That read path
    /// (every WhereClause select) had no retry, and the connections it and the others opened had no native busy wait.
    /// These tests cover the retry, its bound, the busy wait every SqliteObjectDatabase now gets, disposal, and a heavy
    /// mix of pooled readers and writers.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_SqlitePooledReadContention
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = TemporarySqlite.NewDatabasePath("ut-sqlite-read-contention");

        [TearDown]
        public void TearDown() => TemporarySqlite.DeleteDatabase(_path);

        // The string the existing pool test uses: no BusyTimeout, as a player's serverconfig.xml may have.
        private string PooledWal => $"Data Source={_path};Version=3;Pooling=True;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60";

        [Test]
        public void EverySqliteDatabaseGetsANativeBusyWait_AndKeepsALongerOne()
        {
            Assert.That(new SQLiteConnectionStringBuilder(SqliteSafety.WithBusyTimeout(PooledWal)).BusyTimeout, Is.EqualTo(SqliteSafety.MinimumBusyTimeoutMs));
            string longer = PooledWal + ";BusyTimeout=30000";
            Assert.That(SqliteSafety.WithBusyTimeout(longer), Is.EqualTo(longer), "a longer wait is left exactly as written");

            var database = new CountingDatabase(PooledWal);
            Assert.That(new SQLiteConnectionStringBuilder(database.EffectiveConnectionString).BusyTimeout, Is.EqualTo(SqliteSafety.MinimumBusyTimeoutMs));
            Assert.That(new SQLiteConnectionStringBuilder(database.EffectiveConnectionString).JournalMode, Is.EqualTo(SQLiteJournalModeEnum.Wal), "nothing else changes");
        }

        [Test]
        public void ABriefLockOnTheSelectAllObjectsPath_IsRetried()
        {
            var database = Seeded(rows: 5);
            database.FailNextOpens(3, SQLiteErrorCode.Busy);
            Assert.That(database.SelectAllObjects<DbSinglePermission>(), Has.Count.EqualTo(5));
            Assert.That(database.FailedOpens, Is.EqualTo(3));

            database.FailNextOpens(2, SQLiteErrorCode.Locked);
            Assert.That(database.SelectObjects<DbSinglePermission>(DB.Column("PlayerID").IsEqualTo("seed-3")), Has.Count.EqualTo(1),
                "the WhereClause selects share the path");
            Assert.That(database.FailedOpens, Is.EqualTo(2));
        }

        [Test]
        public void TheRetryIsBounded_AndOtherErrorsAreNotRetried()
        {
            var database = Seeded(rows: 1);
            database.FailNextOpens(int.MaxValue, SQLiteErrorCode.Busy);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.Throws<SQLiteException>(() => database.SelectAllObjects<DbSinglePermission>());
            Assert.That(database.FailedOpens, Is.EqualTo(41), "the first try and 40 retries");
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(10_000), "the back-off is short (about 1.7 s in all)");

            var other = Seeded(rows: 1);
            other.FailNextOpens(int.MaxValue, SQLiteErrorCode.Error);
            Assert.Throws<SQLiteException>(() => other.SelectAllObjects<DbSinglePermission>());
            Assert.That(other.FailedOpens, Is.EqualTo(1), "only lock conflicts are retried");
        }

        [Test]
        public void EveryConnectionIsDisposedAfterUse()
        {
            var database = Seeded(rows: 3);
            database.SelectAllObjects<DbSinglePermission>();
            database.SelectObjects<DbSinglePermission>(DB.Column("PlayerID").IsEqualTo("seed-1"));
            database.FailNextOpens(1, SQLiteErrorCode.Busy);
            database.SelectAllObjects<DbSinglePermission>();
            var row = database.SelectAllObjects<DbSinglePermission>().First();
            row.Command = "changed";
            database.SaveObject(row);

            Assert.That(database.Created, Is.Not.Empty);
            foreach (SQLiteConnection connection in database.Created)
                Assert.Throws<ObjectDisposedException>(() => connection.Open(), "a connection was closed but not disposed");
        }

        [Test]
        public void AWhereClauseReadByManyThreadsAtOnce_AlwaysHasItsParameters()
        {
            // A clause was marked processed before its parameters were set, so a second thread could read null
            // parameters (a NullReferenceException in FillSQLParameter, once in 50 runs of the pooled test).
            int nulls = 0;
            int threads = Math.Max(4, Environment.ProcessorCount);
            for (int round = 0; round < 2000; round++)
            {
                WhereClause clause = DB.Column("PlayerID").IsEqualTo("p" + round).And(DB.Column("Command").IsEqualTo("c"));
                using var start = new Barrier(threads);
                Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
                {
                    start.SignalAndWait();
                    if (clause.Parameters == null || clause.Parameters.Length != 2 || clause.ParameterizedText == null)
                        Interlocked.Increment(ref nulls);
                });
            }

            Assert.That(nulls, Is.Zero);
            Assert.That(WhereClause.Empty.Parameters, Is.Empty);
        }

        [Test]
        public void PooledWalReadersAndWriters_DoNotFailOverRepeatedRounds()
        {
            // The existing pool test's mix, three times over and with every read path: SelectAllObjects, WhereClause
            // selects and key lookups, against inserts and saves on the same pool.
            var database = new SqliteObjectDatabase(PooledWal);
            database.RegisterDataObject(typeof(DbSinglePermission));
            var failures = new ConcurrentQueue<string>();
            for (int round = 0; round < 3; round++)
            {
                Parallel.For(0, 240, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(8, Environment.ProcessorCount * 2) }, index =>
                {
                    try
                    {
                        var row = new DbSinglePermission { PlayerID = $"r{round}-{index}", Command = "saved" };
                        if (!database.AddObject(row))
                            failures.Enqueue($"add r{round}-{index}");
                        if (database.SelectAllObjects<DbSinglePermission>().Count == 0)
                            failures.Enqueue("empty SelectAllObjects");
                        if (database.SelectObjects<DbSinglePermission>(DB.Column("PlayerID").IsEqualTo(row.PlayerID)).Count != 1)
                            failures.Enqueue($"select r{round}-{index}");
                        if (database.FindObjectByKey<DbSinglePermission>(row.ObjectId) == null)
                            failures.Enqueue($"key r{round}-{index}");
                        row.Command = "saved again";
                        if (!database.SaveObject(row))
                            failures.Enqueue($"save r{round}-{index}");
                    }
                    catch (Exception e)
                    {
                        failures.Enqueue(e.GetType().Name + ": " + e.Message);
                    }
                });
            }

            Assert.That(failures, Is.Empty, string.Join(" | ", failures.Take(5)));
            var all = database.SelectAllObjects<DbSinglePermission>();
            Assert.That(all, Has.Count.EqualTo(720));
            Assert.That(all.All(row => row.Command == "saved again"), Is.True);
        }

        private CountingDatabase Seeded(int rows)
        {
            var database = new CountingDatabase(PooledWal);
            database.RegisterDataObject(typeof(DbSinglePermission));
            for (int i = 0; i < rows; i++)
                Assert.That(database.AddObject(new DbSinglePermission { PlayerID = $"seed-{i}", Command = "seed" }), Is.True);
            database.Created.Clear();
            return database;
        }

        /// <summary>Counts the connections it makes and can fail the next opens with a chosen SQLite error.</summary>
        private sealed class CountingDatabase : SqliteObjectDatabase
        {
            private int _failuresLeft;
            private SQLiteErrorCode _code;

            public CountingDatabase(string connectionString) : base(connectionString) { }

            public string EffectiveConnectionString => ConnectionString;
            public ConcurrentQueue<DbConnection> Created { get; } = new();
            public int FailedOpens { get; private set; }

            public void FailNextOpens(int count, SQLiteErrorCode code)
            {
                _failuresLeft = count;
                _code = code;
                FailedOpens = 0;
            }

            protected override DbConnection CreateConnection(string connectionsString)
            {
                DbConnection connection = base.CreateConnection(connectionsString);
                Created.Enqueue(connection);
                return connection;
            }

            protected override void OpenConnection(DbConnection connection)
            {
                if (_failuresLeft > 0)
                {
                    _failuresLeft--;
                    FailedOpens++;
                    throw new SQLiteException(_code, "injected by the test");
                }

                base.OpenConnection(connection);
            }
        }
    }
}
