"""Crafting 2.0 WP9: no played economy ships. The release build empties every table of EconomyTables.All and checks it,
and a patch's world update never carries a saved-progress row (optional tables included).

    python -m unittest discover -s source/tools/tests -v
"""
import json
import sqlite3
import sys
import tempfile
import unittest
from contextlib import closing
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
import build_release_037 as release  # noqa: E402
import find_changes_since_release as changes  # noqa: E402
import make_patch  # noqa: E402

POLICY = json.loads((HERE / "OfflineDaoc.ProgressImport/progress-policy.json").read_text(encoding="utf-8"))
ECONOMY = ["offline_bot_types", "offline_bot_crafters", "offline_bot_craft_orders", "offline_exchange_index",
           "offline_economy_ledger", "offline_crafter_customers"]


def played_world(path: Path, economy_rows: bool = True):
    """The tables clean_world and verify_clean touch, with a played economy in them."""
    con = sqlite3.connect(path)
    con.executescript("""
        CREATE TABLE Account (Name TEXT); CREATE TABLE DOLCharacters (Name TEXT); CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY);
        CREATE TABLE Guild (GuildID TEXT, RealmPoints INT, BountyPoints INT, Bank INT, MeritPoints INT, Webpage TEXT, Email TEXT,
            Motd TEXT, oMotd TEXT, HaveGuildHouse INT, GuildHouseNumber INT);
        CREATE TABLE GuildRank (GuildID TEXT);
        CREATE TABLE DBHouse (OwnerID TEXT, GuildName TEXT, GuildHouse INT, HasConsignment INT, KeptMoney INT, Model INT, Name TEXT);
        CREATE TABLE Keep (KeepID INT, Realm INT, Level INT, ClaimedGuildName TEXT);
        CREATE TABLE Door (Door_ID INT, Health INT, State INT);
        CREATE TABLE Relic (Realm INT, OriginalRealm INT, LastRealm INT);
        CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT, Description TEXT, DefaultValue TEXT, Value TEXT,
            LastTimeRowUpdated TEXT, ServerProperty_ID TEXT);
        CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY, Value TEXT);
        CREATE TABLE offline_population_settings (Key TEXT, Value TEXT, Description TEXT);
        INSERT INTO ServerProperty VALUES ('server', 'allow_auto_account_creation', '', 'True', 'False', '', 'a');
        INSERT INTO offline_population_settings VALUES ('ActiveTarget', '600', ''), ('PopulationEnabled', 'false', ''),
            ('StartupRampMinutes', '5', '');
        INSERT INTO Account VALUES ('chris'); INSERT INTO offline_world_bots VALUES (1);
        CREATE TABLE offline_bot_types (BotId INT, PersonalityType INT);
        CREATE TABLE offline_bot_crafters (BotId INT, PrimaryCraft INT);
        CREATE TABLE offline_bot_craft_orders (OrderId TEXT, BuyerBotId INT, Copper INT);
        CREATE TABLE offline_exchange_index (BandKey TEXT, IndexCopper INT);
        CREATE TABLE offline_economy_ledger (HourUtc TEXT, Realm INT, BotCoin INT);
        CREATE TABLE offline_crafter_customers (CrafterBotId INT, CustomerId TEXT);
        """)
    con.executemany("INSERT INTO Guild (GuildID) VALUES (?)", [(g,) for g in release.STARTER_GUILDS + ("players-guild",)])
    if economy_rows:
        con.executescript("""
            INSERT INTO offline_bot_types VALUES (0, -1), (1, 4);
            INSERT INTO offline_bot_crafters VALUES (1, 11);
            INSERT INTO offline_bot_craft_orders VALUES ('o1', 1, 500);
            INSERT INTO offline_exchange_index VALUES ('1|36|25|9|2', 250000);
            INSERT INTO offline_economy_ledger VALUES ('2026-11-16T21:00:00Z', 1, 5000000);
            INSERT INTO offline_crafter_customers VALUES (1, 'chris-character');
            """)
    con.commit()
    con.close()


class EconomyTablesShipEmpty(unittest.TestCase):
    def test_the_server_list_is_read_from_its_source(self):
        self.assertEqual(release.economy_tables(REPO), ECONOMY)

    def test_the_policy_covers_every_economy_table(self):
        release.check_policy_covers(POLICY, release.economy_tables(REPO))
        missing = dict(POLICY, OptionalProgressTables=[t for t in POLICY["OptionalProgressTables"] if t != "offline_economy_ledger"])
        with self.assertRaisesRegex(AssertionError, "offline_economy_ledger"):
            release.check_policy_covers(missing, ECONOMY)

    def test_clean_world_empties_a_played_economy_and_the_check_sees_it(self):
        with tempfile.TemporaryDirectory() as tmp:
            db = Path(tmp) / "world.db"
            played_world(db)
            with closing(sqlite3.connect(db)) as con, self.assertRaisesRegex(AssertionError, "offline_bot_types still has 2"):
                release.verify_tables_empty(con, ECONOMY)
            report = release.clean_world(db, POLICY, {"Keep": {}, "Door": {}})
            self.assertEqual(report["population"]["ActiveTarget"], "0")
            with closing(sqlite3.connect(db)) as con:
                self.assertEqual(release.verify_tables_empty(con, ECONOMY), {table: 0 for table in ECONOMY})
                self.assertEqual(con.execute("SELECT COUNT(*) FROM offline_world_bots").fetchone()[0], 0)

    def test_a_world_without_the_tables_passes_and_says_so(self):
        with tempfile.TemporaryDirectory() as tmp:
            db = Path(tmp) / "world.db"
            with closing(sqlite3.connect(db)) as con:
                con.execute("CREATE TABLE offline_bot_types (BotId INT)")
                self.assertEqual(release.verify_tables_empty(con, ["offline_bot_types", "offline_economy_ledger"]),
                                 {"offline_bot_types": 0, "offline_economy_ledger": "absent"})


class PatchesNeverCarrySavedProgress(unittest.TestCase):
    """make_patch's world update and find_changes_since_release skip the optional progress tables too."""

    def _worlds(self, tmp: Path):
        base, target = tmp / "base.db", tmp / "target.db"
        with closing(sqlite3.connect(base)) as con:
            con.executescript("""
                CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT); INSERT INTO Mob VALUES ('m1', 'Rat');
                CREATE TABLE offline_bot_memory (BotId INT PRIMARY KEY, Note TEXT); INSERT INTO offline_bot_memory VALUES (1, 'old');
                """)
            con.commit()
        with closing(sqlite3.connect(target)) as con:
            con.executescript("""
                CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT); INSERT INTO Mob VALUES ('m1', 'Giant rat');
                CREATE TABLE offline_bot_memory (BotId INT PRIMARY KEY, Note TEXT); INSERT INTO offline_bot_memory VALUES (2, 'new');
                CREATE TABLE offline_economy_ledger (HourUtc TEXT, Realm INT, BotCoin INT);
                INSERT INTO offline_economy_ledger VALUES ('2026-11-16T21:00:00Z', 1, 5000000);
                """)
            con.commit()
        return base, target

    def test_a_world_update_creates_the_new_table_empty_and_carries_no_rows(self):
        with tempfile.TemporaryDirectory() as tmp:
            base, target = self._worlds(Path(tmp))
            update = make_patch.world_update(base, target, POLICY, "1.2")
            self.assertEqual([t["Table"] for t in update["Tables"]], ["Mob"], "only world data changes")
            self.assertEqual([t["Table"] for t in update["NewTables"]], ["offline_economy_ledger"], "the table exists after a patch")

    def test_changes_since_a_release_ignore_saved_progress(self):
        with tempfile.TemporaryDirectory() as tmp:
            base, target = self._worlds(Path(tmp))
            report = changes.compare_world(base, target, POLICY)
            self.assertEqual(sorted(report["tables"]), ["Mob"])


if __name__ == "__main__":
    unittest.main()
