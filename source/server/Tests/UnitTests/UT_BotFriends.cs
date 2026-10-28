using System;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.Friends;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // /friend for autonomous bots (BotFriends): exact names in any case, same realm, parked bots through
    // offline_world_bots, never a retired bot, and a player character of the same name always wins.
    [TestFixture, NonParallelizable]
    public class UT_BotFriends
    {
        private string _path;
        private SqliteObjectDatabase _database;

        [SetUp]
        public void OpenDatabase()
        {
            // The test creates only this GUID-named SQLite file, never a server database.
            _path = Path.Combine(Path.GetTempPath(), "daoc-bot-friends-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            _database = new SqliteObjectDatabase($"Data Source={_path};Version=3;Pooling=False;");
            _database.RegisterDataObject(typeof(OfflineWorldBotRecord));
            _database.RegisterDataObject(typeof(DbCoreCharacterXCustomParam));
            _database.RegisterDataObject(typeof(DbCoreCharacter));

            AddBot("Haralhelm", eRealm.Midgard, level: 34, classId: 22, lastSaved: "2026-10-27T10:00:00.0000000Z", lastUpdate: "2026-10-27T12:30:00.0000000Z");
            AddBot("Brenna", eRealm.Hibernia, level: 12, classId: 43);
            AddBot("Oldsven", eRealm.Midgard, level: 50, classId: 23, retired: true);
            AddBot("Sharedname", eRealm.Midgard, level: 20, classId: 22);
            Assert.That(_database.AddObject(new DbCoreCharacter { Name = "Sharedname", AccountName = "player", Realm = (int)eRealm.Midgard }), Is.True);
        }

        [TearDown]
        public void DeleteDatabase()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [TestCase("Haralhelm")]
        [TestCase("haralhelm")]
        [TestCase("  HARALHELM ")]
        public void ParkedBotOfTheRealm_ResolvesToItsOwnName(string typed)
        {
            Assert.That(BotFriends.TryResolveName(_database, typed, eRealm.Midgard, out string botName), Is.True);
            Assert.That(botName, Is.EqualTo("Haralhelm"));
        }

        [TestCase("Haral")]
        [TestCase("Haralhelmx")]
        [TestCase("")]
        [TestCase("   ")]
        public void OnlyTheExactName_FindsABot(string typed)
        {
            Assert.That(BotFriends.TryResolveName(_database, typed, eRealm.Midgard, out string botName), Is.False);
            Assert.That(botName, Is.Null);
        }

        [Test]
        public void BotOfAnotherRealm_IsNotFound()
        {
            Assert.That(BotFriends.TryResolveName(_database, "Brenna", eRealm.Midgard, out _), Is.False);
            Assert.That(BotFriends.TryResolveName(_database, "Brenna", eRealm.Hibernia, out string botName), Is.True);
            Assert.That(botName, Is.EqualTo("Brenna"));
        }

        [Test]
        public void RetiredBot_IsNeverFound()
        {
            Assert.That(BotFriends.TryResolveName(_database, "Oldsven", eRealm.Midgard, out _), Is.False);
            Assert.That(BotFriends.FindRecord(_database, "Oldsven", eRealm.Midgard), Is.Null);
        }

        [TestCase("Sharedname")]
        [TestCase("sharedname")]
        public void PlayerCharacterOfTheSameName_Wins(string typed)
        {
            Assert.That(BotFriends.HasCharacter(_database, typed), Is.True);
            Assert.That(BotFriends.TryResolveName(_database, typed, eRealm.Midgard, out _), Is.False);
        }

        [Test]
        public void OfflineStatuses_ListOnlyTheBotsOfTheRealmThatAreNoCharacter()
        {
            string[] friends = ["Haralhelm", "Brenna", "Oldsven", "Sharedname", "Nobody"];
            FriendStatus[] statuses = BotFriends.OfflineStatuses(_database, friends, ["Sharedname"], eRealm.Midgard);

            Assert.That(statuses.Select(status => status.Name), Is.EquivalentTo(new[] { "Haralhelm" }));
            Assert.That(statuses[0].Level, Is.EqualTo(34));
            Assert.That(statuses[0].ClassID, Is.EqualTo(22));
            Assert.That(statuses[0].LastPlayed.ToUniversalTime(), Is.EqualTo(new DateTime(2026, 10, 27, 12, 30, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void OfflineStatuses_WithOnlyCharacters_AskNothing()
        {
            Assert.That(BotFriends.OfflineStatuses(_database, ["Sharedname"], ["sharedname"], eRealm.Midgard), Is.Empty);
        }

        [Test]
        public void LastPlayed_IsTheLaterStamp_OrMinValueWithoutOne()
        {
            var record = new OfflineWorldBotRecord { LastSavedUtc = "2026-10-28T09:00:00.0000000Z", LastUpdateUtc = "2026-10-27T09:00:00.0000000Z" };
            Assert.That(BotFriends.LastPlayed(record).ToUniversalTime(), Is.EqualTo(new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc)));

            Assert.That(BotFriends.LastPlayed(new OfflineWorldBotRecord { LastSavedUtc = "", LastUpdateUtc = "not a date" }), Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void CharacterNames_FindOnlyCharacters()
        {
            Assert.That(BotFriends.CharacterNames(_database, ["Sharedname", "Haralhelm"]), Is.EquivalentTo(new[] { "Sharedname" }));
            Assert.That(BotFriends.CharacterNames(_database, []), Is.Empty);
        }

        [Test]
        public void Notices_GoOnlyToPlayersOfTheRealmWithTheBotOnTheirList()
        {
            Assert.That(BotFriends.IsListener(eRealm.Midgard, ["haralhelm"], eRealm.Midgard, "Haralhelm"), Is.True);
            Assert.That(BotFriends.IsListener(eRealm.Albion, ["Haralhelm"], eRealm.Midgard, "Haralhelm"), Is.False);
            Assert.That(BotFriends.IsListener(eRealm.Midgard, ["Brenna"], eRealm.Midgard, "Haralhelm"), Is.False);
        }

        [Test]
        public void SocialLine_HasUpstreamsFormat()
        {
            Assert.That(BotFriends.SocialLine(3, "Haralhelm", 34, 22, "Vale of Mularn"), Is.EqualTo("F,3,Haralhelm,34,22,\"Vale of Mularn\""));
        }

        private void AddBot(string name, eRealm realm, int level, int classId, bool retired = false, string lastSaved = "", string lastUpdate = "")
        {
            Assert.That(_database.AddObject(new OfflineWorldBotRecord
            {
                Name = name, Realm = (int)realm, Level = level, ClassId = classId, IsRetired = retired,
                LastSavedUtc = lastSaved, LastUpdateUtc = lastUpdate,
            }), Is.True);
        }
    }
}
