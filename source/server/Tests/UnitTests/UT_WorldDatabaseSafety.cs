using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using DOL.Config;
using DOL.Database;
using DOL.Database.Connection;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.Network;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.1.2 save safety and network defaults (technical audit 2026-11-03): the connection string the world
    /// opens with, the daily backups (naming, rotation, the once-a-day rule), the realm event ledger's move into
    /// runtime\data, the code's network defaults and the UPnP clean-up. Temporary files only; no server is started.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_WorldDatabaseSafety
    {
        // The upstream code default, what a lost serverconfig.xml was rewritten with before 1.1.2.
        private const string OldDefault = "Data Source=C:\\Games\\OfflineDAoC\\runtime\\data\\opendaoc.sqlite3.db;Version=3;Pooling=False;" +
                                          "Cache Size=1073741824;Journal Mode=Off;Synchronous=Off;Foreign Keys=True;Default Timeout=60";

        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ut-world-db-safety-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(_folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static SQLiteConnectionStringBuilder Parse(string connectionString) => new(connectionString);

        // ---- the connection string

        [Test]
        public void Normalize_MakesTheOldJournalOffDefaultSafe_AndKeepsTheRest()
        {
            SqliteSafety.Result result = SqliteSafety.Normalize(OldDefault);
            SQLiteConnectionStringBuilder safe = Parse(result.ConnectionString);

            Assert.That(safe.JournalMode, Is.EqualTo(SQLiteJournalModeEnum.Wal));
            Assert.That(safe.SyncMode, Is.EqualTo(SynchronizationModes.Normal));
            Assert.That(safe.Pooling, Is.True, "WAL's fast read path in SqliteObjectDatabase needs pooled connections");
            Assert.That(safe.BusyTimeout, Is.EqualTo(SqliteSafety.MinimumBusyTimeoutMs));
            Assert.That(safe.DefaultTimeout, Is.EqualTo(60));
            Assert.That(safe.ForeignKeys, Is.True);
            Assert.That(safe.CacheSize, Is.EqualTo(1073741824));
            Assert.That(safe.DataSource, Is.EqualTo("C:\\Games\\OfflineDAoC\\runtime\\data\\opendaoc.sqlite3.db"));
            Assert.That(result.Changes, Has.Count.EqualTo(4));
            Assert.That(string.Join("; ", result.Changes), Does.Contain("journal Off -> WAL").And.Contain("synchronous Off -> Normal"));
        }

        [Test]
        public void Normalize_LeavesAnAlreadySafeStringAsItIs()
        {
            const string safe = "Data Source=world.db;Version=3;Pooling=True;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;BusyTimeout=10000;Default Timeout=60";
            SqliteSafety.Result result = SqliteSafety.Normalize(safe);
            Assert.That(result.Changes, Is.Empty);
            Assert.That(Parse(result.ConnectionString).JournalMode, Is.EqualTo(SQLiteJournalModeEnum.Wal));
            Assert.That(result.DatabasePath, Is.EqualTo(Path.GetFullPath("world.db")));
        }

        [Test]
        public void Normalize_KeepsAStricterSyncAndALongerBusyWait_AndFillsAMissingTimeout()
        {
            SqliteSafety.Result result = SqliteSafety.Normalize("Data Source=world.db;Journal Mode=Delete;Synchronous=Full;BusyTimeout=30000");
            SQLiteConnectionStringBuilder safe = Parse(result.ConnectionString);
            Assert.That(safe.JournalMode, Is.EqualTo(SQLiteJournalModeEnum.Wal));
            Assert.That(safe.SyncMode, Is.EqualTo(SynchronizationModes.Full));
            Assert.That(safe.BusyTimeout, Is.EqualTo(30000));
            Assert.That(safe.DefaultTimeout, Is.EqualTo(SqliteSafety.DefaultTimeoutSeconds));
            Assert.That(safe.Pooling, Is.True);
        }

        [Test]
        public void ANonSqliteConnectionStringIsNotTouched()
        {
            const string mysql = "Server=localhost;Port=3306;Database=DBNAME;UserId=USERNAME;Password=PASSWORD";
            Assert.That(WorldDatabaseSafety.PrepareConnectionString(EConnectionType.DATABASE_MYSQL, mysql), Is.EqualTo(mysql));
        }

        [Test]
        public void TheOldDefaultOpensTheWorldInWal_EvenAfterTheFileWasInJournalOff()
        {
            string path = Path.Combine(_folder, "opendaoc.sqlite3.db");
            using (var setup = new SQLiteConnection($"Data Source={path};Pooling=False;Journal Mode=Off"))
            {
                setup.Open();
                using var command = setup.CreateCommand();
                command.CommandText = "PRAGMA journal_mode";
                Assert.That(command.ExecuteScalar(), Is.EqualTo("off").IgnoreCase.Or.EqualTo("delete").IgnoreCase);
            }

            string old = OldDefault.Replace("C:\\Games\\OfflineDAoC\\runtime\\data\\opendaoc.sqlite3.db", path);
            var database = new SqliteObjectDatabase(SqliteSafety.Normalize(old).ConnectionString);
            database.RegisterDataObject(typeof(DbSinglePermission));
            Assert.That(database.AddObject(new DbSinglePermission { PlayerID = "safe", Command = "saved" }), Is.True);
            Assert.That(SqliteSafety.JournalMode(path), Is.EqualTo("wal"));
            Assert.That(database.SelectAllObjects<DbSinglePermission>().Single().Command, Is.EqualTo("saved"));
        }

        // ---- integrity

        [Test]
        public void IntegrityCheck_PassesAGoodFile_AndFailsADamagedOne()
        {
            string path = NewWorld("good.db", rows: 2000);
            Assert.That(SqliteSafety.CheckIntegrity(path, full: false).Ok, Is.True);
            Assert.That(SqliteSafety.CheckIntegrity(path, full: true).Ok, Is.True);

            byte[] bytes = File.ReadAllBytes(path);
            var garbage = new Random(7);
            for (int i = 4096 * 2; i < bytes.Length - 4096; i += 4096)
            {
                for (int j = 8; j < 64; j++)
                    bytes[i + j] = (byte)garbage.Next(256);
            }

            string damaged = Path.Combine(_folder, "damaged.db");
            File.WriteAllBytes(damaged, bytes);
            SqliteSafety.IntegrityResult result = SqliteSafety.CheckIntegrity(damaged, full: false);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Problems, Is.Not.Empty);
        }

        // ---- backup names, rotation and the once-a-day rule

        [Test]
        public void BackupNames_AreUtcStamped_AndOnlyTheseAreListed()
        {
            var at = new DateTime(2026, 11, 3, 19, 15, 0, DateTimeKind.Utc);
            Assert.That(SqliteBackups.Prefix(@"C:\game\runtime\data\opendaoc.sqlite3.db".Replace('\\', Path.DirectorySeparatorChar)), Is.EqualTo("opendaoc"));
            Assert.That(SqliteBackups.FileName("opendaoc", at, "daily"), Is.EqualTo("opendaoc-20261103-191500-daily.sqlite3.db"));
            Assert.Throws<ArgumentException>(() => SqliteBackups.FileName("opendaoc", at, "Daily backup"));

            foreach (string name in new[]
                     {
                         "opendaoc-20261103-191500-daily.sqlite3.db", "opendaoc-20261101-080000-daily.sqlite3.db",
                         "opendaoc-20261102-080000-manual.sqlite3.db",
                         "opendaoc-20261104-080000-daily.sqlite3.db.part", // unfinished
                         "before-classic165-spawns-20261001-120000.sqlite3.db", // Setup's own backup
                         "other-20261103-191500-daily.sqlite3.db", "opendaoc-2026-daily.sqlite3.db", "notes.txt"
                     })
                File.WriteAllText(Path.Combine(_folder, name), name);

            var all = SqliteBackups.List(_folder, "opendaoc");
            Assert.That(all.Select(b => Path.GetFileName(b.Path)), Is.EqualTo(new[]
            {
                "opendaoc-20261101-080000-daily.sqlite3.db", "opendaoc-20261102-080000-manual.sqlite3.db", "opendaoc-20261103-191500-daily.sqlite3.db"
            }));
            Assert.That(all[2].CreatedUtc, Is.EqualTo(at));
            Assert.That(all[2].CreatedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(SqliteBackups.List(_folder, "opendaoc", "daily"), Has.Count.EqualTo(2));
            Assert.That(SqliteBackups.List(Path.Combine(_folder, "missing"), "opendaoc"), Is.Empty);

            Assert.That(SqliteBackups.RemoveUnfinished(_folder, "opendaoc"), Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_folder, "opendaoc-20261104-080000-daily.sqlite3.db.part")), Is.False);
            Assert.That(File.Exists(Path.Combine(_folder, "before-classic165-spawns-20261001-120000.sqlite3.db")), Is.True);
        }

        [Test]
        public void Rotation_KeepsTheNewestDailies_AndNeverTouchesAnythingElse()
        {
            var start = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            for (int day = 0; day < 9; day++)
                File.WriteAllText(Path.Combine(_folder, SqliteBackups.FileName("opendaoc", start.AddDays(day), "daily")), "x");
            File.WriteAllText(Path.Combine(_folder, SqliteBackups.FileName("opendaoc", start.AddDays(-30), "manual")), "keep me");
            File.WriteAllText(Path.Combine(_folder, "before-classic165-spawns-20261001-120000.sqlite3.db"), "keep me");

            Assert.That(SqliteBackups.Rotate(_folder, "opendaoc", "daily", 0), Is.Empty, "0 deletes nothing");
            var deleted = SqliteBackups.Rotate(_folder, "opendaoc", "daily", 7);
            Assert.That(deleted.Select(Path.GetFileName), Is.EqualTo(new[]
            {
                "opendaoc-20261020-120000-daily.sqlite3.db", "opendaoc-20261021-120000-daily.sqlite3.db"
            }));
            var left = SqliteBackups.List(_folder, "opendaoc", "daily");
            Assert.That(left, Has.Count.EqualTo(7));
            Assert.That(left[0].CreatedUtc, Is.EqualTo(start.AddDays(2)));
            Assert.That(SqliteBackups.List(_folder, "opendaoc", "manual"), Has.Count.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_folder, "before-classic165-spawns-20261001-120000.sqlite3.db")), Is.True);
        }

        [Test]
        public void TheDailyRule_SkipsWithin24Hours_AndIgnoresAFutureDatedBackup()
        {
            var now = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
            SqliteBackups.BackupFile At(DateTime utc) => new("x", utc, "daily");

            Assert.That(SqliteBackups.IsDue(Array.Empty<SqliteBackups.BackupFile>(), now, TimeSpan.FromHours(24), out TimeSpan? none), Is.True);
            Assert.That(none, Is.Null);
            Assert.That(SqliteBackups.IsDue(new[] { At(now.AddHours(-30)), At(now.AddHours(-3)) }, now, TimeSpan.FromHours(24), out TimeSpan? age), Is.False);
            Assert.That(age, Is.EqualTo(TimeSpan.FromHours(3)));
            Assert.That(SqliteBackups.IsDue(new[] { At(now.AddHours(-24)) }, now, TimeSpan.FromHours(24), out _), Is.True);
            Assert.That(SqliteBackups.IsDue(new[] { At(now.AddDays(40)) }, now, TimeSpan.FromHours(24), out _), Is.True,
                "a clock set back must not stop backups for weeks");
        }

        [Test]
        public void ThePlan_SaysWhyABackupDoesNotRun()
        {
            var now = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
            var none = Array.Empty<SqliteBackups.BackupFile>();
            var recent = new[] { new SqliteBackups.BackupFile("x", now.AddHours(-2), "daily") };
            const long size = 500L * 1024 * 1024;

            Assert.That(WorldDatabaseSafety.PlanDaily(0, false, none, now, null, size).Reason, Does.Contain("db_backup_keep = 0"));
            Assert.That(WorldDatabaseSafety.PlanDaily(7, true, none, now, null, size).Reason, Does.Contain("integrity check failed"));
            Assert.That(WorldDatabaseSafety.PlanDaily(7, false, recent, now, null, size).Run, Is.False);
            Assert.That(WorldDatabaseSafety.PlanDaily(7, false, none, now, size, size).Reason, Does.Contain("free disk space"));
            Assert.That(WorldDatabaseSafety.PlanDaily(7, false, none, now, 10 * size, size).Run, Is.True);
            Assert.That(WorldDatabaseSafety.PlanDaily(7, false, none, now, null, size).Run, Is.True, "unknown free space doesn't stop it");
        }

        // ---- the backup itself

        [Test]
        public void ABackupIsAConsistentStandaloneCopy_WrittenUnderATemporaryName()
        {
            string world = Path.Combine(_folder, "opendaoc.sqlite3.db");
            using var server = new SQLiteConnection($"Data Source={world};Pooling=False;Journal Mode=WAL");
            server.Open();
            Execute(server, "PRAGMA wal_autocheckpoint=0; CREATE TABLE saves(id INTEGER PRIMARY KEY, what TEXT);");
            for (int i = 0; i < 500; i++)
                Execute(server, $"INSERT INTO saves(what) VALUES('save {i}')");
            Assert.That(new FileInfo(world + "-wal").Length, Is.GreaterThan(0), "the saves are still in the WAL, as on a running server");

            string backups = Path.Combine(_folder, "backups");
            var at = new DateTime(2026, 11, 3, 19, 15, 0, DateTimeKind.Utc);
            SqliteBackups.Created created = SqliteBackups.Create(world, backups, at, SqliteBackups.DailyLabel);

            Assert.That(Path.GetFileName(created.Path), Is.EqualTo("opendaoc-20261103-191500-daily.sqlite3.db"));
            Assert.That(Directory.GetFiles(backups).Select(Path.GetFileName), Is.EqualTo(new[] { "opendaoc-20261103-191500-daily.sqlite3.db" }),
                "no .part, -wal or -shm is left");
            Assert.That(SqliteSafety.JournalMode(created.Path), Is.EqualTo("delete"));
            using (var copy = SqliteSafety.OpenSide(created.Path, readOnly: true))
            using (var count = copy.CreateCommand())
            {
                count.CommandText = "SELECT count(*) FROM saves";
                Assert.That(count.ExecuteScalar(), Is.EqualTo(500L));
            }

            Execute(server, "INSERT INTO saves(what) VALUES('after the backup')");
            Assert.Throws<IOException>(() => SqliteBackups.Create(world, backups, at, SqliteBackups.DailyLabel), "a backup is never overwritten");
        }

        [Test]
        public void TheDailyTurn_WritesOncePerDay_AndKeepsTheConfiguredNumber()
        {
            string world = NewWorld("opendaoc.sqlite3.db", rows: 50);
            string backups = Path.Combine(_folder, "backups");
            var day = new DateTime(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

            Assert.That(WorldDatabaseSafety.RunDaily(world, backups, 2, false, day), Does.StartWith("DB_BACKUP result=written"));
            Assert.That(WorldDatabaseSafety.RunDaily(world, backups, 2, false, day.AddHours(5)), Does.StartWith("DB_BACKUP result=skipped"));
            Assert.That(WorldDatabaseSafety.RunDaily(world, backups, 2, false, day.AddHours(25)), Does.StartWith("DB_BACKUP result=written"));
            Assert.That(WorldDatabaseSafety.RunDaily(world, backups, 2, false, day.AddHours(50)), Does.Contain("removed=1"));
            Assert.That(SqliteBackups.List(backups, "opendaoc", "daily").Select(b => b.CreatedUtc),
                Is.EqualTo(new[] { day.AddHours(25), day.AddHours(50) }));
            Assert.That(WorldDatabaseSafety.RunDaily(world, backups, 2, true, day.AddHours(80)), Does.Contain("integrity check failed"));
            Assert.That(WorldDatabaseSafety.RunDaily(world, backups, 0, false, day.AddHours(80)), Does.Contain("result=skipped"));
            Assert.That(WorldDatabaseSafety.RunDaily(Path.Combine(_folder, "missing.db"), backups, 2, false, day.AddHours(80)),
                Does.StartWith("DB_BACKUP result=failed"));
            Assert.That(SqliteBackups.List(backups, "opendaoc", "daily"), Has.Count.EqualTo(2), "a failed or skipped turn deletes nothing");
        }

        [Test]
        public void TheStopCheckpointEmptiesTheWal()
        {
            string world = Path.Combine(_folder, "opendaoc.sqlite3.db");
            using var server = new SQLiteConnection($"Data Source={world};Pooling=False;Journal Mode=WAL");
            server.Open();
            Execute(server, "PRAGMA wal_autocheckpoint=0; CREATE TABLE saves(id INTEGER PRIMARY KEY); INSERT INTO saves DEFAULT VALUES;");
            Assert.That(new FileInfo(world + "-wal").Length, Is.GreaterThan(0));
            Assert.That(SqliteSafety.Checkpoint(world), Does.StartWith("busy=0"));
            Assert.That(new FileInfo(world + "-wal").Length, Is.Zero);
        }

        // ---- the realm event ledger moves to runtime\data

        [Test]
        public void TheLedgerMovesFromTheServerFolder_AndMergesWhenBothExist()
        {
            string runtime = Path.Combine(_folder, "runtime");
            string legacy = Path.Combine(runtime, "server", RealmEventRecordStore.FileName);
            string data = Path.Combine(runtime, "data", RealmEventRecordStore.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(legacy));

            Assert.That(RealmEventRecordStore.Locate(runtime), Is.EqualTo(data), "nothing yet: the new place");
            Assert.That(RealmEventRecordStore.MoveLegacy(legacy, data), Is.EqualTo("none"));

            RealmEventRecordStore.Save(legacy, new[] { Record("a"), Record("b") });
            Assert.That(RealmEventRecordStore.Locate(runtime), Is.EqualTo(legacy), "the launcher reads the old file until the server moves it");
            Assert.That(RealmEventRecordStore.MoveLegacy(legacy, data), Is.EqualTo("moved"));
            Assert.That(File.Exists(legacy), Is.False);
            Assert.That(RealmEventRecordStore.Read(data, "", "", "", "", 0).Total, Is.EqualTo(2));
            Assert.That(RealmEventRecordStore.Locate(runtime), Is.EqualTo(data));

            // An older build ran again after a rollback and wrote to the old place: its events are added, none twice.
            RealmEventRecordStore.Save(legacy, new[] { Record("b"), Record("c") });
            Assert.That(RealmEventRecordStore.MoveLegacy(legacy, data), Is.EqualTo("merged 1"));
            Assert.That(File.Exists(legacy), Is.False);
            Assert.That(RealmEventRecordStore.Read(data, "", "", "", "", 0).Rows.Select(r => r.Id).OrderBy(id => id), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        // ---- network defaults

        [Test]
        public void TheCodeDefaultsReachThisPcOnly_AndAMissingConfigIsWrittenThatWay()
        {
            var config = new GameServerConfiguration();
            Assert.That(config.IP, Is.EqualTo(IPAddress.Loopback));
            Assert.That(config.UDPIP, Is.EqualTo(IPAddress.Loopback));
            Assert.That(config.RegionIP, Is.EqualTo(IPAddress.Loopback));
            Assert.That(config.EnableUPnP, Is.False);
            Assert.That(config.DetectRegionIP, Is.False);
            Assert.That(config.ExposureWarnings(), Is.Empty);
            Assert.That(Parse(config.DBConnectionString).JournalMode, Is.EqualTo(SQLiteJournalModeEnum.Wal));
            Assert.That(Parse(config.DBConnectionString).SyncMode, Is.EqualTo(SynchronizationModes.Normal));

            var file = new FileInfo(Path.Combine(_folder, "config", "serverconfig.xml"));
            file.Directory.Create();
            config.SaveToXMLFile(file);
            string written = File.ReadAllText(file.FullName);
            Assert.That(written, Does.Contain("<IP>127.0.0.1</IP>").And.Contain("<UdpIP>127.0.0.1</UdpIP>").And.Contain("<EnableUPnP>False</EnableUPnP>"));

            var loaded = new GameServerConfiguration();
            loaded.LoadFromXMLFile(file);
            Assert.That(loaded.IP, Is.EqualTo(IPAddress.Loopback));
            Assert.That(loaded.EnableUPnP, Is.False);
        }

        [Test]
        public void APlayersOpenConfigKeepsWorking_AndIsNamedInTheLog()
        {
            var file = new FileInfo(Path.Combine(_folder, "serverconfig.xml"));
            File.WriteAllText(file.FullName, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<root>\r\n  <Server>\r\n    <Port>10300</Port>\r\n" +
                                             "    <IP>0.0.0.0</IP>\r\n    <UdpIP>0.0.0.0</UdpIP>\r\n    <EnableUPnP>True</EnableUPnP>\r\n" +
                                             "    <DetectRegionIP>True</DetectRegionIP>\r\n  </Server>\r\n</root>\r\n");
            var config = new GameServerConfiguration();
            config.LoadFromXMLFile(file);

            Assert.That(config.IP, Is.EqualTo(IPAddress.Any), "the player's choice stays");
            Assert.That(config.EnableUPnP, Is.True);
            IReadOnlyList<string> warnings = config.ExposureWarnings();
            Assert.That(warnings, Has.Count.EqualTo(2));
            Assert.That(warnings[0], Does.StartWith("Your server can be reached from other computers").And.Contain("TCP 0.0.0.0").And.Contain("127.0.0.1"));
            Assert.That(warnings[1], Does.StartWith("UPnP is on").And.Contain("<EnableUPnP>False</EnableUPnP>"));

            File.WriteAllText(file.FullName, "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><Server><Port>10300</Port></Server></root>");
            var sparse = new GameServerConfiguration();
            sparse.LoadFromXMLFile(file);
            Assert.That(sparse.IP, Is.EqualTo(IPAddress.Loopback), "a setting the file leaves out takes the safe default");
            Assert.That(sparse.UDPIP, Is.EqualTo(IPAddress.Loopback));
            Assert.That(sparse.ExposureWarnings(), Is.Empty);

            File.WriteAllText(file.FullName, "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><Server><IP>192.168.1.20</IP><UdpIP>127.0.0.1</UdpIP></Server></root>");
            var lan = new GameServerConfiguration();
            lan.LoadFromXMLFile(file);
            Assert.That(lan.ExposureWarnings().Single(), Does.Contain("TCP 192.168.1.20:10300").And.Not.Contain("UDP"));
        }

        [Test]
        public void UpnpMappings_AreRemovedOnStop_OnlyTheOnesThisServerAdded()
        {
            var gateway = new FakeGateway();
            var config = new GameServerConfiguration { EnableUPnP = true, DetectRegionIP = true };
            var mappings = new UpnpPortMappings(gateway);
            mappings.Open(config);

            Assert.That(gateway.Open, Is.EquivalentTo(new[] { (10400, ProtocolType.Udp), (10300, ProtocolType.Tcp) }));
            Assert.That(config.RegionIP, Is.EqualTo(IPAddress.Parse("203.0.113.7")));
            Assert.That(mappings.Close(), Is.EqualTo(2));
            Assert.That(gateway.Open, Is.Empty);
            Assert.That(mappings.Close(), Is.Zero, "a second stop removes nothing");

            // TCP refused by the router: the UDP mapping that was added is still removed; a hand-made one stays.
            var partial = new FakeGateway { RefuseTcp = true };
            partial.Open.Add((7000, ProtocolType.Tcp));
            var some = new UpnpPortMappings(partial);
            some.Open(config);
            Assert.That(some.Added, Is.EqualTo(new[] { (10400, ProtocolType.Udp) }));
            Assert.That(some.Close(), Is.EqualTo(1));
            Assert.That(partial.Open, Is.EqualTo(new[] { (7000, ProtocolType.Tcp) }));

            var none = new UpnpPortMappings(new FakeGateway { Present = false });
            none.Open(config);
            Assert.That(none.Added, Is.Empty);
            Assert.That(none.Close(), Is.Zero);
        }

        private sealed class FakeGateway : IUpnpGateway
        {
            public bool Present = true, RefuseTcp;
            public readonly List<(int, ProtocolType)> Open = new();
            public bool Discover() => Present;
            public IEnumerable<string> DescribeMappings() => Open.Select(o => o.ToString());

            public void ForwardPort(int internalPort, int externalPort, ProtocolType protocol, string description, IPAddress internalIp)
            {
                if (RefuseTcp && protocol == ProtocolType.Tcp)
                    throw new InvalidOperationException("refused");
                Open.Add((externalPort, protocol));
            }

            public void DeleteForwardingRule(int port, ProtocolType protocol) => Open.Remove((port, protocol));
            public IPAddress GetExternalIP() => IPAddress.Parse("203.0.113.7");
        }

        // ---- helpers

        private string NewWorld(string name, int rows)
        {
            string path = Path.Combine(_folder, name);
            using var connection = new SQLiteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            Execute(connection, "CREATE TABLE saves(id INTEGER PRIMARY KEY, what TEXT); CREATE INDEX saves_what ON saves(what);");
            using var tx = connection.BeginTransaction();
            for (int i = 0; i < rows; i++)
                Execute(connection, $"INSERT INTO saves(what) VALUES('{new string('x', 200)} {i}')");
            tx.Commit();
            return path;
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static RealmEventRecord Record(string id) =>
            new(id, "event-" + id, "Encounter", "Dragon", "Albion", "2026-11-0" + (id[0] - 'a' + 1) + "T10:00:00Z", "", "Rally", "In progress", "", 0, 0);
    }
}
