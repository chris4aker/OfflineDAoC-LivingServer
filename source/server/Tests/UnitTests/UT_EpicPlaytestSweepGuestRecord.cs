using System;
using System.IO;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The sweep's guest record (2026-11-14: the server was force-closed while /epictest sweep ran with a guest, and the
    /// owner's GM account came back a plain player). What the sweep changes on its guest is saved first; a normal end
    /// clears it; a start or a login with no sweep running puts it back, raising a level only to what the account had.
    /// </summary>
    [TestFixture]
    public class UT_EpicPlaytestSweepGuestRecord
    {
        private string _folder;
        private IObjectDatabase _database;

        [OneTimeSetUp]
        public void InitializeLogging() => LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);

        [SetUp]
        public void Open()
        {
            _folder = TemporarySqlite.NewFolder("sweepguest");
            _database = ObjectDatabase.GetObjectDatabase(DOL.Database.Connection.EConnectionType.DATABASE_SQLITE,
                $"Data Source={Path.Combine(_folder, "world.db")};Pooling=False");
            foreach (Type table in new[] { typeof(DbAccount), typeof(DbAccountXCustomParam), typeof(DbBans), typeof(DbCoreCharacter), typeof(DbCoreCharacterXCustomParam) })
                _database.RegisterDataObject(table);
        }

        [TearDown]
        public void Close() => TemporarySqlite.DeleteFolder(_folder);

        private DbAccount Account(string name, uint privLevel)
        {
            var account = new DbAccount { Name = name, Password = "x", PrivLevel = privLevel, CreationDate = DateTime.Now, LastLogin = DateTime.Now };
            Assert.That(_database.AddObject(account), Is.True);
            return account;
        }

        private DbCoreCharacter Character(string account, string name, eRealm realm)
        {
            var character = new DbCoreCharacter { AccountName = account, Name = name, Realm = (int)realm };
            Assert.That(_database.AddObject(character), Is.True);
            return character;
        }

        private uint SavedPrivLevel(string account) => _database.FindObjectByKey<DbAccount>(account).PrivLevel;
        private eRealm SavedRealm(DbCoreCharacter character) => (eRealm)_database.FindObjectByKey<DbCoreCharacter>(character.ObjectId).Realm;

        /// <summary>The guest's start, and the server killed mid-sweep: the lowered account and the raid's realm saved.</summary>
        private (DbAccount Account, DbCoreCharacter Character) StrandedGuest(uint homePrivLevel)
        {
            DbAccount account = Account("owner", homePrivLevel);
            DbCoreCharacter character = Character("owner", "Manawatch", eRealm.Albion);
            eRealm home = eRealm.Albion;
            Assert.That(EpicPlaytestSweep.BeginGuest(_database, account, character.ObjectId, character.Name, ref home, out _), Is.True);
            _database.SaveObject(account);
            character.Realm = (int)eRealm.Midgard;
            _database.SaveObject(character);
            return (account, character);
        }

        [Test]
        public void TheRecordReadsBackAsWritten_AndAnUnreadableOneIsNull() =>
            Assert.Multiple(() =>
            {
                var entry = new EpicPlaytestSweepGuestRecord.Entry("owner", 3, "abc-123", "Manawatch", eRealm.Hibernia);
                Assert.That(EpicPlaytestSweepGuestRecord.Parse("owner", EpicPlaytestSweepGuestRecord.Format(entry)), Is.EqualTo(entry));
                Assert.That(EpicPlaytestSweepGuestRecord.Parse("owner", "realm=1"), Is.Null, "no level");
                Assert.That(EpicPlaytestSweepGuestRecord.Parse("owner", "privlevel=x;realm=1"), Is.Null);
                Assert.That(EpicPlaytestSweepGuestRecord.Parse("owner", "privlevel=3;realm=77"), Is.Null, "no such realm");
                Assert.That(EpicPlaytestSweepGuestRecord.Parse("owner", ""), Is.Null);
            });

        [TestCase(1u, 3u, 3u, TestName = "lowered GM: raised back")]
        [TestCase(1u, 2u, 2u, TestName = "lowered GM level 2: raised back to 2")]
        [TestCase(3u, 2u, 3u, TestName = "higher now: never lowered")]
        [TestCase(3u, 3u, 3u, TestName = "already back: unchanged")]
        [TestCase(1u, 0u, 1u, TestName = "not lowered by the sweep (a player): never raised")]
        [TestCase(1u, 1u, 1u, TestName = "a player's level recorded: never raised")]
        public void APrivLevelIsOnlyRaisedBackToAGmLevelTheAccountHad(uint current, uint home, uint expected) =>
            Assert.That(EpicPlaytestSweepGuestRecord.RaisedPrivLevel(current, home), Is.EqualTo(expected));

        [Test]
        public void AGuestStart_SavesTheRecordBeforeTheAccountIsLowered()
        {
            DbAccount account = Account("owner", 3);
            DbCoreCharacter character = Character("owner", "Manawatch", eRealm.Albion);
            eRealm home = eRealm.Albion;
            Assert.That(EpicPlaytestSweep.BeginGuest(_database, account, character.ObjectId, character.Name, ref home, out uint homePrivLevel), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(homePrivLevel, Is.EqualTo(3u));
                Assert.That(account.PrivLevel, Is.EqualTo((uint)ePrivLevel.Player), "plays as a player for the sweep");
                Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner"),
                    Is.EqualTo(new EpicPlaytestSweepGuestRecord.Entry("owner", 3, character.ObjectId, "Manawatch", eRealm.Albion)));
            });
        }

        [Test]
        public void APlayerGuestsStart_RecordsItsRealm_AndNoLevelToRaise()
        {
            DbAccount account = Account("friend", 1);
            DbCoreCharacter character = Character("friend", "Brannoc", eRealm.Midgard);
            eRealm home = eRealm.Midgard;
            Assert.That(EpicPlaytestSweep.BeginGuest(_database, account, character.ObjectId, character.Name, ref home, out uint homePrivLevel), Is.True);
            Assert.That(homePrivLevel, Is.Zero);
            Assert.That(account.PrivLevel, Is.EqualTo(1u));
            Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "friend")?.HomePrivLevel, Is.Zero);
        }

        [Test]
        public void ANormalEnd_PutsTheLevelBackSavesIt_AndClearsTheRecord()
        {
            DbAccount account = Account("owner", 3);
            DbCoreCharacter character = Character("owner", "Manawatch", eRealm.Albion);
            eRealm home = eRealm.Albion;
            EpicPlaytestSweep.BeginGuest(_database, account, character.ObjectId, character.Name, ref home, out uint homePrivLevel);
            _database.SaveObject(account);
            Assert.That(SavedPrivLevel("owner"), Is.EqualTo(1u), "saved lowered mid-sweep");
            bool saved = false;
            Assert.That(EpicPlaytestSweep.EndGuest(_database, account, homePrivLevel, () => saved = true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(saved, Is.True, "the character (its realm) is saved before the record goes");
                Assert.That(account.PrivLevel, Is.EqualTo(3u));
                Assert.That(SavedPrivLevel("owner"), Is.EqualTo(3u), "saved: a stop right after the end can't lose it");
                Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner"), Is.Null);
            });
        }

        [Test]
        public void AnEndWhoseCharacterWasNotSaved_KeepsTheRecord()
        {
            DbAccount account = Account("owner", 3);
            eRealm home = eRealm.Albion;
            EpicPlaytestSweep.BeginGuest(_database, account, "id", "Manawatch", ref home, out uint homePrivLevel);
            Assert.That(EpicPlaytestSweep.EndGuest(_database, account, homePrivLevel, () => false), Is.False);
            Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner"), Is.Not.Null, "the next start puts the realm back");
            Assert.That(account.PrivLevel, Is.EqualTo(3u));
        }

        [Test]
        public void AServerStartAfterAKill_PutsTheAccountAndRealmBack_AndClearsTheRecord()
        {
            (_, DbCoreCharacter character) = StrandedGuest(3);
            DbAccount friend = Account("friend", 1);
            Assert.That(SavedPrivLevel("owner"), Is.EqualTo(1u), "stranded as a player");

            Assert.That(EpicPlaytestSweepGuestRecord.RestoreStored(_database, false, "server_start"), Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(SavedPrivLevel("owner"), Is.EqualTo(3u));
                Assert.That(SavedRealm(character), Is.EqualTo(eRealm.Albion));
                Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner"), Is.Null);
                Assert.That(SavedPrivLevel(friend.Name), Is.EqualTo(1u), "an account with no record is never touched");
            });
            Assert.That(EpicPlaytestSweepGuestRecord.RestoreStored(_database, false, "server_start"), Is.Zero, "once");
        }

        [Test]
        public void ALoginAfterAKill_PutsBackTheAccountObjectTheLoginUses()
        {
            (_, DbCoreCharacter character) = StrandedGuest(2);
            DbAccount loggingIn = _database.FindObjectByKey<DbAccount>("owner");
            Assert.That(loggingIn.PrivLevel, Is.EqualTo(1u));

            Assert.That(EpicPlaytestSweepGuestRecord.RestoreStored(_database, false, "login", live: loggingIn), Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(loggingIn.PrivLevel, Is.EqualTo(2u), "the live object, so the login doesn't save the lowered level over it");
                Assert.That(SavedPrivLevel("owner"), Is.EqualTo(2u));
                Assert.That(SavedRealm(character), Is.EqualTo(eRealm.Albion));
                Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner"), Is.Null);
            });
        }

        [Test]
        public void ARestore_NeverRaisesAnAccountThatWasNotGm()
        {
            (_, DbCoreCharacter character) = StrandedGuest(1);
            Assert.That(EpicPlaytestSweepGuestRecord.RestoreStored(_database, false, "server_start"), Is.EqualTo(1));
            Assert.That(SavedPrivLevel("owner"), Is.EqualTo(1u), "a player before the sweep stays a player");
            Assert.That(SavedRealm(character), Is.EqualTo(eRealm.Albion), "its realm still comes back");

            // A record that names a GM level for an account the sweep never lowered (written by hand, say) can't lower one either.
            DbAccount admin = Account("admin", 3);
            EpicPlaytestSweepGuestRecord.Write(_database, new EpicPlaytestSweepGuestRecord.Entry("admin", 2, null, null, eRealm.None));
            EpicPlaytestSweepGuestRecord.RestoreStored(_database, false, "server_start");
            Assert.That(SavedPrivLevel(admin.Name), Is.EqualTo(3u), "never lowered");
        }

        [Test]
        public void NothingIsRestoredWhileASweepRuns()
        {
            (_, DbCoreCharacter character) = StrandedGuest(3);
            DbAccount loggingIn = _database.FindObjectByKey<DbAccount>("owner");
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestSweepGuestRecord.RestoreStored(_database, true, "login", live: loggingIn), Is.Zero);
                Assert.That(EpicPlaytestSweepGuestRecord.RestoreStored(_database, true, "server_start"), Is.Zero);
                Assert.That(loggingIn.PrivLevel, Is.EqualTo(1u), "the running sweep's own end restores");
                Assert.That(SavedPrivLevel("owner"), Is.EqualTo(1u));
                Assert.That(SavedRealm(character), Is.EqualTo(eRealm.Midgard));
                Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner"), Is.Not.Null, "kept for the end");
            });
        }

        [Test]
        public void ANewSweepAfterALeftRecord_KeepsTheTrueHomeValues()
        {
            (DbAccount account, DbCoreCharacter character) = StrandedGuest(3);
            // Restored at login, the guest starts a new sweep standing in the raid realm it was left in: home is still Albion.
            account.PrivLevel = 3;
            eRealm home = eRealm.Midgard;
            Assert.That(EpicPlaytestSweep.BeginGuest(_database, account, character.ObjectId, character.Name, ref home, out uint homePrivLevel), Is.True);
            Assert.That(home, Is.EqualTo(eRealm.Albion));
            Assert.That(homePrivLevel, Is.EqualTo(3u));
            Assert.That(EpicPlaytestSweepGuestRecord.Read(_database, "owner")?.HomeRealm, Is.EqualTo(eRealm.Albion));
        }

        [Test]
        public void TheStartAndStopHooksAreServerEvents() =>
            Assert.Multiple(() =>
            {
                Assert.That(typeof(EpicPlaytestSweep).GetMethod(nameof(EpicPlaytestSweep.OnServerStarted))!
                    .GetCustomAttribute<GameServerStartedEventAttribute>(), Is.Not.Null, "restores at start, before the port opens");
                Assert.That(typeof(EpicPlaytestSweep).GetMethod(nameof(EpicPlaytestSweep.OnServerStopped))!
                    .GetCustomAttribute<GameServerStoppedEventAttribute>(), Is.Not.Null, "a graceful stop ends the sweep");
            });
    }
}
