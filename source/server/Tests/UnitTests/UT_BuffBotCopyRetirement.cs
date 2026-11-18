using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using R = DOL.GS.BuffBotCopyRetirement;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Owner decision 2026-11-18: the buffbot copies earlier builds saved into the roster (before #334; owner's test copy ids
    /// 5876-5895 and 5826, "Eilriorrin" twice) are retired once, kept, never deleted; nothing else is touched. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_BuffBotCopyRetirement
    {
        private static long FirstMain(Func<long, bool> also = null) =>
            Enumerable.Range(1, 50_000).Select(i => (long)i).First(id => NoLiferBuffBots.HasBuffBot(id, 30) && (also?.Invoke(id) ?? true));

        private static OfflineWorldBotRecord Main(long id, string name = "Mainguy") =>
            new() { BotId = id, Name = name, Realm = 3, ClassId = (int)eCharacterClass.Hero, ClassName = "Hero", Level = 50, Gender = 0, RaceId = 1 };

        /// <summary>A row as SaveAutonomousState inserted it from the buffbot's record: no ClassName.</summary>
        private static OfflineWorldBotRecord CopyOf(long mainId, long id, string name)
        {
            var identity = NoLiferBuffBots.DrawIdentity(mainId, eRealm.Hibernia, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return new OfflineWorldBotRecord
            {
                BotId = id, Name = name, Realm = 3, ClassId = (int)eCharacterClass.Druid, ClassName = string.Empty,
                RaceId = (int)identity.Race, RaceName = identity.Race.ToString(), Gender = (int)identity.Gender, Level = 50,
                Activity = "Questing",
            };
        }

        private static OfflineWorldBotRecord Gamebot(OfflineWorldBotRecord like, long id, string className = "Druid")
        {
            OfflineWorldBotRecord row = CopyOf(1, id, like.Name);
            row.RaceId = like.RaceId;
            row.Gender = like.Gender;
            row.ClassName = className;
            return row;
        }

        [Test]
        public void OnlyTheCopyIsRetired_AGamebotOfTheSameNameIsNot()
        {
            long mainId = FirstMain();
            string name = R.GeneratedNames(mainId, eRealm.Hibernia)[0];
            OfflineWorldBotRecord main = Main(mainId);
            OfflineWorldBotRecord copy = CopyOf(mainId, 5876, name);
            OfflineWorldBotRecord sameName = Gamebot(copy, 5877);                // a launcher gamebot: the same everything, a ClassName
            OfflineWorldBotRecord similar = Gamebot(copy, 5878);
            similar.Name = name + "n";
            OfflineWorldBotRecord[] rows = [main, copy, sameName, similar];

            R.Result result = R.Find(rows, _ => false, 30);
            Assert.That(result.Copies.Select(c => c.Record.BotId), Is.EqualTo(new long[] { 5876 }));
            Assert.That(result.Copies[0].Main, Is.SameAs(main));
            Assert.That(result.Suspected, Is.Empty, "a gamebot with a class name is never even a suspect");

            Assert.That(R.Retire(result.Copies[0]), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(copy.IsRetired, Is.True, "never logged in again");
                Assert.That(copy.IsOnline, Is.False);
                Assert.That(copy.Activity, Is.EqualTo("Retired: copy of Mainguy's buffbot (kept, never logs in)"));
                Assert.That(OfflineDaoc.Shared.BuffBotCopyMarker.IsRetiredCopy(copy.IsRetired, copy.ClassName, copy.Activity), Is.True,
                    "the launcher shows it as kept, not as a deletion");
                Assert.That(sameName.IsRetired || similar.IsRetired || main.IsRetired, Is.False);
            });
            Assert.That(R.RetiredLine(result.Copies[0]), Does.StartWith(
                $"BOT_BUFFBOT_COPY_RETIRED bot=\"{name}\" id=5876 class=Druid level=50 realm=Hibernia main=\"Mainguy\" mainId={mainId}:"));
            Assert.That(R.SummaryLine(result, 1), Does.StartWith("BOT_BUFFBOT_COPIES retired=1 found=1 suspected=0 mains="));

            // a second run changes nothing
            R.Result again = R.Find(rows, _ => false, 30);
            Assert.That(again.Copies, Is.Empty);
            Assert.That(R.Retire(result.Copies[0]), Is.False);
        }

        [Test]
        public void EveryCopyOfOneBuffbotIsFound_UnderAnyNameItCouldHaveHad()
        {
            long mainId = FirstMain();
            IReadOnlyList<string> names = R.GeneratedNames(mainId, eRealm.Hibernia);
            Assert.That(names, Has.Count.GreaterThanOrEqualTo(40));
            // the login's own draw: with the first name taken it takes the second (Kudaxellabuffs after Kudaxellabuff)
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { names[0], "Someoneelse" };
            Assert.That(NoLiferBuffBots.DrawIdentity(mainId, eRealm.Hibernia, taken).Name, Is.EqualTo(names[1]));
            // with only the second taken, the first
            taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { names[1] };
            Assert.That(NoLiferBuffBots.DrawIdentity(mainId, eRealm.Hibernia, taken).Name, Is.EqualTo(names[0]));

            OfflineWorldBotRecord[] rows = [Main(mainId), CopyOf(mainId, 5826, names[0]), CopyOf(mainId, 5876, names[0]), CopyOf(mainId, 5880, names[1]),
                CopyOf(mainId, 5881, names[^1])];
            Assert.That(R.Find(rows, _ => false, 30).Copies.Select(c => c.Record.BotId), Is.EqualTo(new long[] { 5826, 5876, 5880, 5881 }));
        }

        /// <summary>BUFFBOT_CREATED lines from the owner's PC logs (5378dbe, b1b6846): the names and races the login gave are the
        /// ones the repair looks for, so the generator has not drifted since those builds.</summary>
        [TestCase(5147, "Ashfury", eRealm.Hibernia, "Eilriorrin", eRace.Firbolg)]        // 11/17 22:01:02; copies 5826 and 5876
        [TestCase(4979, "Kudaxella", eRealm.Hibernia, "Kudaxellabuff", eRace.Firbolg)]
        [TestCase(4979, "Kudaxella", eRealm.Hibernia, "Kudaxellabuffs", eRace.Firbolg)]  // after a copy took "Kudaxellabuff"
        [TestCase(5490, "Grilgah", eRealm.Hibernia, "Blackbreaker", eRace.Firbolg)]
        [TestCase(3370, "Tredin", eRealm.Albion, "Leofermund", eRace.Avalonian)]
        [TestCase(4290, "Steelfury", eRealm.Midgard, "Okke", eRace.Troll)]                // the 40th generated name
        public void TheOwnersLoggedBuffbotsAreRecognised(long mainId, string mainName, eRealm realm, string buffbot, eRace race)
        {
            var identity = NoLiferBuffBots.DrawIdentity(mainId, realm, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Assert.That(identity.Race, Is.EqualTo(race));
            Assert.That(NoLiferBuffBots.HasBuffBot(mainId, 30), Is.True);
            Assert.That(NoLiferBuffBots.MainStyleNames(mainName, mainId).Concat(R.GeneratedNames(mainId, realm)), Does.Contain(buffbot));
        }

        [Test]
        public void AMainStyleCopyIsFound()
        {
            long mainId = FirstMain(id => NoLiferBuffBots.MainStyleNames("Kudaxella", id).Count > 0);
            Assert.That(NoLiferBuffBots.MainStyleNames("Kudaxella", mainId), Does.Contain(
                NoLiferBuffBots.MainStyleName("Kudaxella", mainId, new HashSet<string>())));
            OfflineWorldBotRecord[] rows = [Main(mainId, "Kudaxella"), CopyOf(mainId, 5878, "Kudaxellabuff"), CopyOf(mainId, 5879, "Kudaxellabuffs")];
            Assert.That(R.Find(rows, _ => false, 30).Copies.Select(c => c.Record.BotId), Is.EqualTo(new long[] { 5878, 5879 }));
            // a main whose buffbot never takes the main's name: those names are not its buffbot's
            long other = FirstMain(id => NoLiferBuffBots.MainStyleNames("Kudaxella", id).Count == 0);
            rows = [Main(other, "Kudaxella"), CopyOf(other, 5878, "Kudaxellabuff")];
            R.Result result = R.Find(rows, _ => false, 30);
            Assert.That(result.Copies, Is.Empty);
            Assert.That(result.Suspected.Select(r => r.BotId), Is.EqualTo(new long[] { 5878 }), "logged, not retired");
        }

        [Test]
        public void ANearMissIsNeverRetired()
        {
            long mainId = FirstMain();
            string name = R.GeneratedNames(mainId, eRealm.Hibernia)[0];
            OfflineWorldBotRecord Copy(long id, Action<OfflineWorldBotRecord> change)
            {
                OfflineWorldBotRecord row = CopyOf(mainId, id, name);
                change(row);
                return row;
            }
            OfflineWorldBotRecord[] rows =
            [
                Main(mainId),
                Copy(mainId - 1 > 0 ? mainId - 1 : 0, _ => { }),                               // older than the main
                Copy(9001, row => row.Gender = 1 - row.Gender),
                Copy(9002, row => row.RaceId += 1),
                Copy(9003, row => row.Level = 49),                                            // not a buffbot level: not a suspect either
                Copy(9004, row => row.Realm = 1),
                Copy(9005, row => row.IsRetired = true),                                      // the launcher's delete request: not ours
                Copy(9006, _ => { }),                                                         // a hired companion
                Copy(9007, row => row.ClassName = "Druid"),
            ];
            R.Result result = R.Find(rows, id => id == 9006, 30);
            Assert.That(result.Copies, Is.Empty);
            Assert.That(result.Suspected.Select(r => r.BotId), Is.SubsetOf(new long[] { mainId - 1, 9001, 9002, 9004 }));
            Assert.That(R.SuspectedLine(rows[2]), Does.StartWith("BOT_BUFFBOT_COPY_SUSPECTED bot="));

            // no main with a buffbot in the table: nothing is a copy
            Assert.That(R.Find(rows.Skip(1).ToArray(), _ => false, 30).Copies, Is.Empty);
            Assert.That(R.Find(Array.Empty<OfflineWorldBotRecord>(), null, 30).Copies, Is.Empty);
        }

        [Test]
        public void TheRepairRunsOncePerWorld()
        {
            Assert.That(R.AlreadyRan(""), Is.False);
            Assert.That(R.AlreadyRan(null), Is.False);
            Assert.That(R.WithRepair(""), Is.EqualTo("buffbot-copies"));
            Assert.That(R.WithRepair("other"), Is.EqualTo("other,buffbot-copies"));
            Assert.That(R.AlreadyRan("other, buffbot-copies"), Is.True);
            Assert.That(R.WithRepair("buffbot-copies"), Is.EqualTo("buffbot-copies"));
            Assert.That(R.PercentFor(0), Is.EqualTo(30), "copies were made at the default percent even if it is lower now");
            Assert.That(R.PercentFor(60), Is.EqualTo(60));
        }

        [Test]
        public void TheServerStartRunsItBeforeTheFirstRosterRead()
        {
            string root = null;
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "source", "server", "Dawn of Light.sln")))
                    root = Path.Combine(dir.FullName, "source", "server");
            if (root == null)
                Assert.Ignore("Server source not found beside the tests.");
            string source = File.ReadAllText(Path.Combine(root, "GameServer", "bots", "autonomous", "AutonomousPopulationController.cs"));
            int started = source.IndexOf("public static void OnServerStarted", StringComparison.Ordinal);
            int repair = source.IndexOf("RetireBuffBotCopies();", started, StringComparison.Ordinal);
            int read = source.IndexOf("RefreshControlPlane(force: true);", started, StringComparison.Ordinal);
            Assert.That(repair, Is.GreaterThan(started));
            Assert.That(repair, Is.LessThan(read));
        }
    }
}
