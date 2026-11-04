"""Tests of make_test_world.py: it only ever writes a new file, never the source, and makes what the playtester needs.

    python -m unittest discover -s source/tools/tests -v
"""
import hashlib
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import make_test_world as mtw  # noqa: E402


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class MinimalWorldTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_minimal_world_has_a_start_the_accounts_and_the_marker(self):
        out = self.dir / "minimal.db"
        report = mtw.build_minimal(out)
        con = sqlite3.connect(out)
        self.assertEqual(dict(con.execute(f"SELECT Key,Value FROM {mtw.MARKER_TABLE}"))["mode"], "minimal")
        self.assertEqual(dict(con.execute("SELECT Name,PrivLevel FROM Account")),
                         {"playtester": 1, "playtester2": 1, "playtestergm": 3})
        # the start is inside one of the zones (the playtester places its position updates by the Zones table)
        x, y, region = con.execute("SELECT XPos,YPos,Region FROM StartupLocation").fetchone()
        zones = con.execute("SELECT ZoneID FROM Zones WHERE RegionID=? AND ?>=OffsetX*8192 AND ?<(OffsetX+Width)*8192 "
                            "AND ?>=OffsetY*8192 AND ?<(OffsetY+Height)*8192", (region, x, x, y, y)).fetchall()
        self.assertEqual(zones, [(0,)])
        self.assertEqual(con.execute("SELECT Value FROM ServerProperty WHERE `Key`='time_between_account_creation'").fetchone(), ("0",))
        con.close()
        self.assertEqual(report["startup_locations"], 1)

    def test_the_passwords_are_plain_so_the_server_hashes_them_at_first_login(self):
        out = self.dir / "minimal.db"
        mtw.build_minimal(out)
        con = sqlite3.connect(out)
        self.assertFalse(any(p.startswith("##") for (p,) in con.execute("SELECT Password FROM Account")))
        con.close()

    def test_it_never_writes_over_a_file(self):
        out = self.dir / "existing.db"
        out.write_bytes(b"keep me")
        with self.assertRaises(SystemExit):
            mtw.build_minimal(out)
        self.assertEqual(out.read_bytes(), b"keep me")


class FromReleaseTests(unittest.TestCase):
    """The parts of from-release that need no real world: the source guard, the read-only copy, the fixtures and bots."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.world = self.dir / "release.db"
        con = sqlite3.connect(self.world)
        for ddl in mtw.MINIMAL_SCHEMA.values():
            con.execute(ddl)
        con.execute("INSERT INTO StartupLocation (XPos,YPos,ZPos,Region,RealmID) VALUES (1,2,3,1,1),(4,5,6,100,2),(7,8,9,200,3)")
        con.execute("CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Realm INT, ClassId INT, "
                    "ClassName TEXT, RaceId INT, RaceName TEXT, Gender INT, Level INT, Experience INT, RealmPoints INT, ZoneName TEXT, "
                    "Activity TEXT, IsOnline INT, IsAlive INT, LastUpdateUtc TEXT, MoneyCopper INT, InventoryRevision INT, ZoneId INT, "
                    "X INT, Y INT, Z INT, RegionId INT, Health INT, Mana INT, Endurance INT, BindRegionId INT, BindX INT, BindY INT, "
                    "BindZ INT, CurrentGoal TEXT, ObjectiveProgress TEXT, LastMeaningfulProgressUtc TEXT, IsRetired INT)")
        con.commit()
        con.close()

    def tearDown(self):
        self.tmp.cleanup()

    def test_the_output_cannot_be_the_source(self):
        with self.assertRaises(SystemExit):
            mtw.refuse_paths(self.world, self.world)
        with self.assertRaises(SystemExit):
            mtw.refuse_paths(self.dir / "new.db", self.dir / "missing.db")

    def test_the_copy_leaves_the_source_byte_for_byte_alone(self):
        before = digest(self.world)
        out = self.dir / "copy.db"
        mtw.read_only_copy(self.world, out)
        self.assertEqual(digest(self.world), before)
        con = sqlite3.connect(out)
        self.assertEqual(con.execute("SELECT COUNT(*) FROM StartupLocation").fetchone()[0], 3)
        con.close()

    def test_fixtures_and_bots_go_into_the_copy(self):
        out = self.dir / "copy.db"
        mtw.read_only_copy(self.world, out)
        con = sqlite3.connect(out)
        mtw.add_fixtures(con, "from-release", str(self.world))
        names = mtw.add_test_bots(con, 2)
        con.commit()
        self.assertEqual(len(names), 6)
        realms = dict(con.execute("SELECT Realm, COUNT(*) FROM offline_world_bots GROUP BY Realm"))
        self.assertEqual(realms, {1: 2, 2: 2, 3: 2})
        # each bot starts at a startup location of its own realm, offline, as the launcher queues it
        for realm, region, online in con.execute("SELECT Realm, RegionId, IsOnline FROM offline_world_bots"):
            self.assertEqual(region, {1: 1, 2: 100, 3: 200}[realm])
            self.assertEqual(online, 0)
        report = mtw.verify_test_world(con)
        self.assertEqual(report["accounts"]["playtestergm"], 3)
        con.close()

    def test_a_world_with_someone_elses_account_fails_the_check(self):
        out = self.dir / "copy.db"
        mtw.read_only_copy(self.world, out)
        con = sqlite3.connect(out)
        mtw.add_fixtures(con, "from-release", "x")
        con.execute("INSERT INTO Account (Name, Password) VALUES ('offline', '##ABC')")
        with self.assertRaises(AssertionError):
            mtw.verify_test_world(con)
        con.close()


if __name__ == "__main__":
    unittest.main()
