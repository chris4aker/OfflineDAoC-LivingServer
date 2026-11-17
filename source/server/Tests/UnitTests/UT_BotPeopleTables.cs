using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 0 (docs/BOTS-AS-PEOPLE-1.2.md, "Data and the release", "Settings", "Performance",
    /// "Log tags"): the four tables and their store (a real SQLite round trip), the twelve switches, the AI_PROFILE scopes,
    /// the summary line and the /lstest and /lswhy stubs. Nothing here changes behaviour. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotPeopleTables
    {
        public static readonly string[] Keys =
        [
            "bot_skill_profiles", "bot_skill_growth", "bot_skill_spare_player_group", "raid_learns_from_raiders",
            "bot_place_knowledge", "bot_ask_the_way", "bot_wrong_roads", "bot_movement_habits", "bot_move_strafe",
            "bot_move_stick_line", "bot_move_sit_and_walls", "bot_living_milestones",
        ];

        /// <summary>The switches whose package is built: their description says what they do now.</summary>
        public static readonly string[] Built =
            ["bot_skill_growth", "bot_place_knowledge", "bot_movement_habits", "bot_move_strafe", "bot_move_stick_line", "bot_move_sit_and_walls", "bot_ask_the_way", "bot_wrong_roads", "bot_skill_profiles", "bot_skill_spare_player_group"];

        // ---- tables ----------------------------------------------------------------------------------------------------

        [Test]
        public void Each_table_is_declared_once_with_its_record()
        {
            Assert.That(BotPeopleTables.All, Is.EqualTo(new[] { "offline_bot_skill", "offline_bot_places", "offline_bot_milestones", "offline_bot_mechanics_seen" }));
            Assert.That(BotPeopleTables.RecordTypes.Length, Is.EqualTo(BotPeopleTables.All.Length));
            for (int i = 0; i < BotPeopleTables.All.Length; i++)
            {
                Type type = BotPeopleTables.RecordTypes[i];
                Assert.That(type.GetCustomAttribute<DataTable>()?.TableName, Is.EqualTo(BotPeopleTables.All[i]), "record type order");
                Assert.That(typeof(ILivingServerRow).IsAssignableFrom(type), Is.True, type.Name + " is saved by LivingServerTable");
                Assert.That(type.GetProperty("PersonKey")?.GetCustomAttribute<DataElement>()?.Varchar, Is.EqualTo(BotPersonKey.TextLength), type.Name);
                Assert.That(type.GetProperty("CharacterId"), Is.Not.Null, type.Name + " keeps the character beside the person");
            }
        }

        [Test]
        public void The_policy_and_the_passport_know_every_table()
        {
            string root = Root();
            string policy = File.ReadAllText(Path.Combine(root, "source/tools/OfflineDaoc.ProgressImport/progress-policy.json"));
            string optional = Regex.Match(policy, "\"OptionalProgressTables\"\\s*:\\s*\\[([^\\]]*)\\]").Groups[1].Value;
            string passport = File.ReadAllText(Path.Combine(root, "source/tools/OfflineDaoc.Passport/PassportEngine.cs"));
            foreach (string table in BotPeopleTables.All)
            {
                Assert.That(optional, Does.Contain($"\"{table}\""), table + " is emptied by the release build and carried by the transfer");
                Assert.That(passport, Does.Contain($"[\"{table}\"] = new(PassportDecision.StaysInWorld"), table);
            }
        }

        private static string Root()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source/tools/OfflineDaoc.ProgressImport/progress-policy.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("The repository is not beside the tests.");
            return dir;
        }

        // ---- the store -------------------------------------------------------------------------------------------------

        private static OfflineBotSkillRecord SkillRow(BotPersonKey key, double reactions = 1) =>
            new() { PersonKey = key.ToString(), CharacterId = key.Id, Realm = 1, Tier = 1, ReactionsXp = reactions, UpdatedUtc = "2026-11-17T09:00:00Z" };

        [Test]
        public void The_store_saves_to_sqlite_and_loads_it_back_with_every_key_shape()
        {
            string folder = TemporarySqlite.NewFolder("botpeople");
            try
            {
                var database = ObjectDatabase.GetObjectDatabase(DOL.Database.Connection.EConnectionType.DATABASE_SQLITE,
                    $"Data Source={Path.Combine(folder, "people.db")};Pooling=False");
                foreach (Type type in BotPeopleTables.RecordTypes)
                    database.RegisterDataObject(type);
                var store = new BotPeopleStore(() => database);
                BotPersonKey a = BotPersonKey.Character(11), b = BotPersonKey.Character(12);
                store.Skill.Change(a, () => SkillRow(a), row => row.PullsXp += 2);
                store.Places.Change((a, 1), () => new OfflineBotPlacesRecord { PersonKey = a.ToString(), Realm = 1, CharacterId = 11 },
                    row => row.Known = BotPeopleBits.With(row.Known, 4));
                store.Milestones.Change((a, 11, "ding50"), () => new OfflineBotMilestoneRecord { PersonKey = a.ToString(), CharacterId = 11, Arc = "ding50", Name = "Aldwin" }, _ => { });
                store.MechanicsSeen.Change((b, "xanxicar.stomp"), () => new OfflineBotMechanicSeenRecord { PersonKey = b.ToString(), CharacterId = 12, Mechanic = "xanxicar.stomp" },
                    row => row.Seen++);
                store.Flush(wait: true);
                store.Skill.Change(a, () => SkillRow(a), row => row.PullsXp += 3);
                store.Flush(wait: true);

                var reloaded = new BotPeopleStore(() => database);
                reloaded.LoadFrom(database);
                Assert.That(reloaded.Counts(), Is.EqualTo(new[] { 1, 1, 1, 1 }));
                Assert.That(reloaded.Skill.TryGet(a, out var skill) && skill.PullsXp == 5, Is.True, "a change after the first insert is saved");
                Assert.That(reloaded.Places.TryGet((a, 1), out var places) && BotPeopleBits.Has(places.Known, 4), Is.True);
                Assert.That(reloaded.Milestones.TryGet((a, 11, "ding50"), out var arc) && arc.State == OfflineBotMilestoneRecord.Active, Is.True);
                Assert.That(reloaded.MechanicsSeen.TryGet((b, "xanxicar.stomp"), out var seen) && seen.Seen == 1, Is.True);

                Assert.That(reloaded.Forget(a), Is.EqualTo(3));
                reloaded.Flush(wait: true);
                var again = new BotPeopleStore(() => database);
                again.LoadFrom(database);
                Assert.That(again.Counts(), Is.EqualTo(new[] { 0, 0, 0, 1 }), "a forgotten person's rows are deleted");
            }
            finally
            {
                TemporarySqlite.DeleteFolder(folder);
            }
        }

        [Test]
        public void A_row_with_an_unreadable_key_is_skipped_and_counted()
        {
            var store = new BotPeopleStore(() => null);
            store.Load([SkillRow(BotPersonKey.Character(3)), new OfflineBotSkillRecord { PersonKey = "bot 3" }],
                [new OfflineBotPlacesRecord { PersonKey = "" }], [], null);
            Assert.That(store.Counts(), Is.EqualTo(new[] { 1, 0, 0, 0 }));
            Assert.That(store.Unreadable, Is.EqualTo(2));
        }

        [Test]
        public void Merging_characters_into_a_person_replaces_their_rows_with_the_merged_ones()
        {
            var store = new BotPeopleStore(() => null);
            BotPersonKey one = BotPersonKey.Character(1), two = BotPersonKey.Character(2), person = BotPersonKey.Player(9);
            store.Skill.Change(one, () => SkillRow(one, 30), _ => { });
            store.Skill.Change(two, () => SkillRow(two, 50), _ => { });
            store.Places.Change((one, 1), () => new OfflineBotPlacesRecord { PersonKey = "c:1", CharacterId = 1, Realm = 1, Known = "01" }, _ => { });
            store.Places.Change((two, 1), () => new OfflineBotPlacesRecord { PersonKey = "c:2", CharacterId = 2, Realm = 1, Known = "02" }, _ => { });
            store.Milestones.Change((two, 2, "ding50"), () => new OfflineBotMilestoneRecord { PersonKey = "c:2", CharacterId = 2, Arc = "ding50" }, _ => { });
            Assert.That(store.MergeInto(person, [one, two]), Is.EqualTo(3));
            Assert.That(store.Counts(), Is.EqualTo(new[] { 1, 1, 1, 0 }));
            Assert.That(store.Skill.TryGet(person, out var skill) && skill.ReactionsXp == 50 && skill.PersonKey == "p:9", Is.True);
            Assert.That(store.Places.TryGet((person, 1), out var places) && places.Known == "03", Is.True);
            Assert.That(store.Milestones.TryGet((person, 2, "ding50"), out _), Is.True);
            Assert.That(store.Skill.TryGet(one, out _) || store.Skill.TryGet(two, out _), Is.False);
            Assert.Throws<ArgumentException>(() => store.MergeInto(BotPersonKey.None, [one]));
        }

        // ---- settings --------------------------------------------------------------------------------------------------

        [Test]
        public void Every_switch_is_a_bots_setting_on_by_default_that_says_it_changes_nothing_yet()
        {
            var attributes = typeof(Properties).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (field, attribute: field.GetCustomAttribute<ServerPropertyAttribute>()))
                .Where(pair => pair.attribute != null && Keys.Contains(pair.attribute.Key)).ToArray();
            Assert.That(attributes.Select(pair => pair.attribute.Key), Is.EquivalentTo(Keys));
            foreach (var (field, attribute) in attributes)
            {
                Assert.That(attribute.Category, Is.EqualTo("bots"), attribute.Key);
                Assert.That(attribute.DefaultValue, Is.EqualTo(true), attribute.Key);
                Assert.That(field.GetValue(null), Is.EqualTo(true), attribute.Key + " starts at its default");
                // A package that fills its switch replaces this sentence and updates this test (as the RvR switches do).
                if (!Built.Contains(attribute.Key))
                    Assert.That(attribute.Description, Does.Contain("In this build it changes nothing yet (work package"), attribute.Key);
                else
                    Assert.That(attribute.Description, Does.Not.Contain("changes nothing yet"), attribute.Key);
                Assert.That(attribute.Description, Does.Not.Contain("\""), attribute.Key);
            }
            Assert.That(LivingServerSettingsUpgrade.Changes.Select(change => change.Key), Has.None.AnyOf(Keys),
                "new keys: a world from an earlier build has no row and takes the code default");
        }

        // ---- profiler, summary, commands -------------------------------------------------------------------------------

        [Test]
        public void Each_package_has_its_profiler_scope()
        {
            var names = new[] { AiSystem.BotSkill, AiSystem.PlaceKnowledge, AiSystem.WayFinding, AiSystem.MovementHabits, AiSystem.Milestones }
                .Select(AiSystemNames.Of).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "people.skill", "people.places", "people.way_finding", "people.movement", "people.milestones" }));
            Assert.That(Enum.GetValues<AiSystem>().Where(s => s != AiSystem.BuiltInCount).Select(AiSystemNames.Of), Is.Unique);
            Assert.That((int)AiSystem.BuiltInCount, Is.LessThan(AiProfile.MaxSystems));
        }

        [Test]
        public void The_summary_line_names_the_switches_the_rows_and_the_scope()
        {
            var counts = new long[Enum.GetValues<BotPeopleRule>().Length, Enum.GetValues<BotPeopleRefusal>().Length];
            counts[(int)BotPeopleRule.Mistakes, (int)BotPeopleRefusal.None] = 12;
            counts[(int)BotPeopleRule.Mistakes, (int)BotPeopleRefusal.RealmRaid] = 3;
            counts[(int)BotPeopleRule.Movement, (int)BotPeopleRefusal.PlayerGroup] = 2;
            string line = BotPeopleStore.SummaryLine(300, BotPeopleSwitches.AllOn, [5, 6, 0, 1], counts, 4);
            Assert.That(line, Does.StartWith("BOT_PEOPLE_SUMMARY seconds=300 skill=on growth=on"));
            Assert.That(line, Does.Contain("rows=skill:5,places:6,milestones:0,mechanics_seen:1"));
            Assert.That(line, Does.Contain("records=0/0 mistakes=12/3 way=0/0 movement=0/2 refused=player_group:2,realm_raid:3"));
            Assert.That(line, Does.EndWith("logouts=4"));
            Assert.That(BotPeopleStore.SummaryLine(1, BotPeopleSwitches.AllOff, [0, 0, 0, 0], new long[4, 13], 0), Does.Contain("refused=none"));
        }

        [Test]
        public void Packages_add_their_own_summary_lines_and_a_failing_one_is_skipped()
        {
            try
            {
                BotPeopleSummary.Register("TEST_B", seconds => [$"TEST_B seconds={seconds}"]);
                BotPeopleSummary.Register("TEST_A", _ => throw new InvalidOperationException());
                BotPeopleSummary.Register("TEST_C", _ => ["", null, "TEST_C x=1"]);
                Assert.That(BotPeopleSummary.TakeLines(300).Where(line => line.StartsWith("TEST_")), Is.EqualTo(new[] { "TEST_B seconds=300", "TEST_C x=1" }));
                Assert.Throws<ArgumentException>(() => BotPeopleSummary.Register(" ", _ => []));
            }
            finally
            {
                BotPeopleSummary.Unregister("TEST_A");
                BotPeopleSummary.Unregister("TEST_B");
                BotPeopleSummary.Unregister("TEST_C");
            }
        }

        [Test]
        public void Lstest_stubs_say_which_package_brings_them_until_it_registers()
        {
            foreach (var (name, package, usage) in BotPeopleTest.Subcommands)
            {
                Assert.That(usage, Does.StartWith("/lstest " + name));
                Assert.That(BotPeopleTest.Run(name, null, null, []), Does.Contain("not built yet").And.Contain(package), name);
            }
            Assert.That(BotPeopleTest.Run("nonsense", null, null, []), Does.Contain("not a Bots as people test"));
            Assert.Throws<ArgumentException>(() => BotPeopleTest.Register("nonsense", (_, _, _) => ""));
            try
            {
                BotPeopleTest.Register("skill", (_, _, words) => "profile " + string.Join(",", words));
                Assert.That(BotPeopleTest.Run("SKILL", null, null, ["a", "b"]), Is.EqualTo("profile a,b"));
                Assert.That(BotPeopleTest.ListLine(BotPeopleSwitches.AllOn), Does.Contain("tests built: skill"));
                BotPeopleTest.Register("skill", (_, _, _) => throw new InvalidOperationException("boom"));
                Assert.That(BotPeopleTest.Run("skill", null, null, []), Does.Contain("failed: boom"));
            }
            finally
            {
                BotPeopleTest.Unregister("skill");
            }
            Assert.That(BotPeopleTest.ListLine(BotPeopleSwitches.AllOff), Does.Contain("skill OFF").And.Contain("none yet"));
        }

        [Test]
        public void Lswhy_shows_the_scope_and_says_what_is_not_built_yet()
        {
            Assert.That(BotPeopleWhy.TryTopic("skill", out var skill) && skill == BotPeopleWhy.Topic.Skill, Is.True);
            Assert.That(BotPeopleWhy.TryTopic("WAY", out var way) && way == BotPeopleWhy.Topic.Way, Is.True);
            Assert.That(BotPeopleWhy.TryTopic("duty", out _), Is.False);
            Assert.That(BotPeopleWhy.ScopeLine(BotPersonKey.Character(4), BotPeopleRefusal.None, BotPeopleRefusal.None, BotPeopleRule.Mistakes, true),
                Is.EqualTo("Person c:4: records yes; mistakes now: yes (spared: a player is in its group)"));
            Assert.That(BotPeopleWhy.ScopeLine(BotPersonKey.Character(4), BotPeopleRefusal.None, BotPeopleRefusal.RealmRaid, BotPeopleRule.WayFinding, false),
                Is.EqualTo("Person c:4: records yes; way now: no (realm_raid)"));
            Assert.That(BotPeopleWhy.Lines(BotPeopleWhy.Topic.Skill, null).Single(), Does.Contain("never touches it"));
        }
    }
}
