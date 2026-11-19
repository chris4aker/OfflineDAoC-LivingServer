"""Bots as people (docs/BOTS-AS-PEOPLE-1.2.md, work package 0): no played bot ships. The release build reads
BotPeopleTables.All from the server source, stops when progress-policy.json misses one, empties every one and checks
it empty; the passport has a decision for each, and a patch's world update never carries their rows.

    python -m unittest discover -s source/tools/tests -v
"""
import json
import re
import sqlite3
import sys
import tempfile
import unittest
from contextlib import closing
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_release_037 as release  # noqa: E402
import make_patch  # noqa: E402
from test_release_economy_tables import played_world  # noqa: E402

POLICY = json.loads((HERE / "OfflineDaoc.ProgressImport/progress-policy.json").read_text(encoding="utf-8"))
BOT_PEOPLE = ["offline_bot_skill", "offline_bot_places", "offline_bot_milestones", "offline_bot_mechanics_seen"]


def add_played_bot_people(path: Path):
    """The four tables as the server makes them (the columns WP0 declares), with a played bot in each."""
    with closing(sqlite3.connect(path)) as con:
        con.executescript("""
            CREATE TABLE offline_bot_skill (PersonKey TEXT, CharacterId INT, Realm INT, Tier INT, ReactionsXp REAL);
            CREATE TABLE offline_bot_places (PersonKey TEXT, Realm INT, CharacterId INT, Known TEXT, HalfKnown TEXT);
            CREATE TABLE offline_bot_milestones (PersonKey TEXT, CharacterId INT, Name TEXT, Arc TEXT, State TEXT);
            CREATE TABLE offline_bot_mechanics_seen (PersonKey TEXT, Mechanic TEXT, CharacterId INT, Seen INT);
            INSERT INTO offline_bot_skill VALUES ('c:1', 1, 1, 2, 140.5), ('c:2', 2, 2, 0, 3);
            INSERT INTO offline_bot_places VALUES ('c:1', 1, 1, 'ff07', '10');
            INSERT INTO offline_bot_milestones VALUES ('c:1', 1, 'Aldwin', 'ding50', 'reached');
            INSERT INTO offline_bot_mechanics_seen VALUES ('c:1', 'Xanxicar/blast', 1, 4);
            """)
        con.commit()


class BotPeopleTablesShipEmpty(unittest.TestCase):
    def test_the_server_list_is_read_from_its_source(self):
        self.assertEqual(release.bot_people_tables(REPO), BOT_PEOPLE)

    def test_each_table_is_a_declared_record(self):
        source = (REPO / "source/server/GameServer/bots/autonomous/BotPeopleTables.cs").read_text(encoding="utf-8")
        constants = dict(re.findall(r'public const string (\w+) = "(\w+)";', source))
        declared = [constants[name] for name in re.findall(r"\[DataTable\(TableName = BotPeopleTables\.(\w+)\)\]", source)]
        self.assertEqual(sorted(declared), sorted(BOT_PEOPLE))

    def test_the_policy_covers_every_table_and_a_missing_one_stops_the_build(self):
        release.check_policy_covers(POLICY, release.bot_people_tables(REPO))
        for table in BOT_PEOPLE:
            self.assertIn(table, POLICY["OptionalProgressTables"], "optional: a world the 1.2 server never ran lacks it")
            missing = dict(POLICY, OptionalProgressTables=[t for t in POLICY["OptionalProgressTables"] if t != table])
            with self.assertRaisesRegex(AssertionError, table):
                release.check_policy_covers(missing, BOT_PEOPLE)

    def test_the_passport_decides_each_table(self):
        source = (HERE / "OfflineDaoc.Passport/PassportEngine.cs").read_text(encoding="utf-8")
        for table in BOT_PEOPLE:
            self.assertRegex(source, r'\["%s"\] = new\(PassportDecision\.StaysInWorld' % table)

    def test_clean_world_empties_played_bots_and_the_check_sees_it(self):
        with tempfile.TemporaryDirectory() as tmp:
            db = Path(tmp) / "world.db"
            played_world(db)
            add_played_bot_people(db)
            with closing(sqlite3.connect(db)) as con, self.assertRaisesRegex(AssertionError, "offline_bot_skill still has 2"):
                release.verify_tables_empty(con, BOT_PEOPLE)
            release.clean_world(db, POLICY, {"Keep": {}, "Door": {}})
            with closing(sqlite3.connect(db)) as con:
                self.assertEqual(release.verify_tables_empty(con, BOT_PEOPLE), {table: 0 for table in BOT_PEOPLE})

    def test_a_world_without_the_tables_passes_and_says_so(self):
        with tempfile.TemporaryDirectory() as tmp, closing(sqlite3.connect(Path(tmp) / "w.db")) as con:
            con.execute("CREATE TABLE offline_bot_skill (PersonKey TEXT)")
            self.assertEqual(release.verify_tables_empty(con, BOT_PEOPLE),
                             {"offline_bot_skill": 0, "offline_bot_places": "absent", "offline_bot_milestones": "absent",
                              "offline_bot_mechanics_seen": "absent"})

    def test_a_world_update_creates_the_tables_empty_and_carries_no_rows(self):
        with tempfile.TemporaryDirectory() as tmp:
            base, target = Path(tmp) / "base.db", Path(tmp) / "target.db"
            with closing(sqlite3.connect(base)) as con:
                con.executescript("CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT); INSERT INTO Mob VALUES ('m1', 'Rat');")
                con.commit()
            with closing(sqlite3.connect(target)) as con:
                con.executescript("CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT); INSERT INTO Mob VALUES ('m1', 'Rat');")
                con.commit()
            add_played_bot_people(target)
            update = make_patch.world_update(base, target, POLICY, "1.2")
            self.assertEqual([t["Table"] for t in update["Tables"]], [], "no saved-progress row in a patch")
            self.assertEqual(sorted(t["Table"] for t in update["NewTables"]), sorted(BOT_PEOPLE))


class BotPeopleDescriptionsMatch(unittest.TestCase):
    """The launcher copies the four master switches' descriptions from the server (WP0); they stay equal (WP7)."""

    MASTERS = ["bot_skill_profiles", "bot_place_knowledge", "bot_movement_habits", "bot_living_milestones",
               "raid_learns_from_raiders"]   # the last has no master; the launcher writes it for ALL OFF (WP7)

    def test_the_launcher_and_the_server_describe_each_master_switch_alike(self):
        server = (REPO / "source/server/GameServer/serverproperty/ServerProperties.cs").read_text(encoding="utf-8")
        launcher = (HERE / "OfflineDaoc.Launcher/SocialExpansionSettings.cs").read_text(encoding="utf-8")
        server_text = dict(re.findall(r'\[ServerProperty\("bots", "(\w+)", "([^"]*)"', server))
        launcher_text = dict(re.findall(r'\("(\w+)", "bots", "([^"]*)"\)', launcher))
        for key in self.MASTERS:
            self.assertIn(key, server_text, key)
            self.assertIn(key, launcher_text, key)
            self.assertEqual(launcher_text[key], server_text[key], key)

    def test_no_switch_says_it_changes_nothing_yet(self):
        server = (REPO / "source/server/GameServer/serverproperty/ServerProperties.cs").read_text(encoding="utf-8")
        card = (HERE / "OfflineDaoc.Launcher/SocialExpansionSettingsControl.cs").read_text(encoding="utf-8")
        server_text = dict(re.findall(r'\[ServerProperty\("bots", "(\w+)", "([^"]*)"', server))
        waiting = sorted(key for key, text in server_text.items() if "changes nothing yet" in text)
        self.assertEqual(waiting, [], "work package 6 (milestones) is built: every package is")
        self.assertNotIn("change nothing yet", card)
        self.assertNotIn("still being built", card)


if __name__ == "__main__":
    unittest.main()
