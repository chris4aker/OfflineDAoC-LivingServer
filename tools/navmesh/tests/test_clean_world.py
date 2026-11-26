"""Tests for tools/navmesh/clean_world.py (issue #687): the navmesh set's world is cleaned and checked by the release
builder's own code, and the check fails closed: a planted row in any saved-progress table, an account, a character, a
gamebot, a player's guild, a missing table or a damaged file fails it.

    python3 -m unittest discover -s tools/navmesh/tests -v
"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import sqlite3
import sys
import tempfile
import unittest
from contextlib import closing
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
sys.path.insert(0, str(REPO / "tools" / "navmesh"))

import clean_world as cw  # noqa: E402

release = cw.release
POLICY = json.loads(cw.POLICY.read_text(encoding="utf-8"))
DEFAULTS = json.loads(cw.DEFAULTS.read_text(encoding="utf-8"))
# The tables verify_clean checks by their content, not for being empty (the starting guilds; local options; run state).
KEPT = {"Guild", "GuildRank", "offline_local_options", "offline_runtime_status"}
EMPTIED = sorted(set(POLICY["ProgressTables"] + POLICY.get("OptionalProgressTables", []) + POLICY["ClearTables"]
                     + ["FactionAggroLevel"]) - KEPT)
SPECIAL = {"Account", "DOLCharacters", "offline_world_bots"}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fresh_world(path: Path, played: bool = True, class_off: bool = True) -> None:
    """A world with every table the release builder touches. played: rows in every saved-progress table, an account,
    a character, a gamebot, a player's guild, a captured keep and a broken door (what the cleaning must undo)."""
    con = sqlite3.connect(path)
    con.executescript("""
        CREATE TABLE Account (Account_ID TEXT, Name TEXT); CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT, Name TEXT);
        CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY, Name TEXT);
        CREATE TABLE Guild (GuildID TEXT, GuildName TEXT, RealmPoints INT, BountyPoints INT, Bank INT, MeritPoints INT,
            Webpage TEXT, Email TEXT, Motd TEXT, oMotd TEXT, HaveGuildHouse INT, GuildHouseNumber INT);
        CREATE TABLE GuildRank (GuildID TEXT, RankLevel INT);
        CREATE TABLE DBHouse (OwnerID TEXT, GuildName TEXT, GuildHouse INT, HasConsignment INT, KeptMoney INT, Model INT, Name TEXT);
        CREATE TABLE Keep (KeepID INT, Realm INT, Level INT, BaseLevel INT, ClaimedGuildName TEXT);
        CREATE TABLE Door (Door_ID TEXT, Health INT, State INT);
        CREATE TABLE Relic (Realm INT, OriginalRealm INT, LastRealm INT);
        CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT, Description TEXT, DefaultValue TEXT, Value TEXT,
            LastTimeRowUpdated TEXT, ServerProperty_ID TEXT);
        CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY, Value TEXT);
        CREATE TABLE offline_population_settings (Key TEXT, Value TEXT, Description TEXT);
        CREATE TABLE Zones (ZoneID INT, Name TEXT);
        INSERT INTO Zones VALUES (21, 'Cornwall'), (180, 'Fomor');
        INSERT INTO ServerProperty VALUES ('account', 'allow_auto_account_creation', '', 'True', 'False', '', 'a');
        INSERT INTO offline_population_settings VALUES ('ActiveTarget', '600', ''), ('PopulationEnabled', 'false', ''),
            ('StartupRampMinutes', '5', '');
        INSERT INTO Relic VALUES (2, 1, 2), (1, 1, 1);
        """)
    if class_off:
        con.execute("INSERT INTO ServerProperty VALUES ('classes', 'enable_sluaghbinder', '', 'True', 'False', '', 'enable_sluaghbinder')")
    for table in EMPTIED:
        if table not in SPECIAL:
            con.execute(f'CREATE TABLE "{table}" (v TEXT)')
    con.executemany("INSERT INTO Keep VALUES (?,?,?,?,'')",
                    [(int(k), v["Realm"], v["Level"], v.get("BaseLevel", 0)) for k, v in DEFAULTS["Keep"].items()])
    con.executemany("INSERT INTO Door VALUES (?,?,?)", [(k, v["Health"], v["State"]) for k, v in DEFAULTS["Door"].items()])
    con.executemany("INSERT INTO Guild (GuildID, GuildName) VALUES (?, 'starter')", [(g,) for g in release.STARTER_GUILDS])
    con.executemany("INSERT INTO GuildRank VALUES (?, 0)", [(g,) for g in release.STARTER_GUILDS])
    if played:
        con.execute("INSERT INTO Account VALUES ('a1', 'chris')")
        con.execute("INSERT INTO DOLCharacters VALUES ('c1', 'Chrisdin')")
        con.execute("INSERT INTO offline_world_bots (Name) VALUES ('Bot')")
        con.execute("INSERT INTO Guild (GuildID, GuildName, Bank) VALUES ('players-guild', 'Mine', 99)")
        con.execute("INSERT INTO GuildRank VALUES ('players-guild', 0)")
        first_keep, first_door = next(iter(DEFAULTS["Keep"])), next(iter(DEFAULTS["Door"]))
        con.execute("UPDATE Keep SET Realm=3, Level=10, ClaimedGuildName='Mine' WHERE KeepID=?", (int(first_keep),))
        con.execute("UPDATE Door SET Health=0, State=0 WHERE Door_ID=?", (first_door,))
        for table in EMPTIED:
            if table not in SPECIAL:
                con.execute(f'INSERT INTO "{table}" VALUES (\'played\')')
    con.commit()
    con.close()


def quiet(fn, *args):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = fn(*args)
    return code, out.getvalue()


class CleanWorld(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "installed.db"
        self.out = self.root / "set" / "world" / "opendaoc.sqlite3.db"

    def test_a_played_world_fails_the_check_and_comes_out_clean(self):
        fresh_world(self.source)
        with self.assertRaisesRegex(cw.NotClean, "not a clean world"):
            cw.verify(self.source)
        before = digest(self.source)
        report = cw.clean(self.source, self.out)
        self.assertEqual(digest(self.source), before, "the installed world is only read")
        self.assertEqual(report["source_saves_before_cleaning"], {"Account": 1, "DOLCharacters": 1, "offline_world_bots": 1})
        self.assertEqual(report["quick_check"], "ok")
        self.assertEqual(report["keeps"], len(DEFAULTS["Keep"]))
        self.assertEqual(report["doors"], len(DEFAULTS["Door"]))
        self.assertTrue(all(report["policy_tables"][t] in (0, "absent") for t in EMPTIED))
        self.assertEqual(cw.verify(self.out)["quick_check"], "ok")
        with closing(sqlite3.connect(self.out)) as con:
            self.assertEqual(con.execute("SELECT COUNT(*) FROM Zones").fetchone()[0], 2, "world rows are kept")
            self.assertEqual({r[0] for r in con.execute("SELECT GuildID FROM Guild")}, set(release.STARTER_GUILDS))

    def test_require_fresh_refuses_a_world_with_saves(self):
        fresh_world(self.source, played=True)
        with self.assertRaisesRegex(cw.NotClean, "not a fresh install"):
            cw.clean(self.source, self.out, require_fresh=True)
        self.assertFalse(self.out.exists())
        fresh = self.root / "fresh.db"
        fresh_world(fresh, played=False)
        self.assertEqual(cw.clean(fresh, self.out, require_fresh=True)["source_saves_before_cleaning"],
                         {"Account": 0, "DOLCharacters": 0, "offline_world_bots": 0})

    def test_the_report_names_the_files_hash(self):
        fresh_world(self.source)
        self.assertEqual(cw.clean(self.source, self.out)["sha256"], digest(self.out))

    def test_the_installs_edition_is_kept(self):
        fresh_world(self.source, class_off=True)
        self.assertEqual(cw.clean(self.source, self.out)["enable_sluaghbinder"], "False")
        other = self.root / "on.db"
        fresh_world(other, class_off=False)
        self.assertEqual(cw.clean(other, self.root / "on-clean.db")["enable_sluaghbinder"], "True")

    def test_a_planted_row_in_any_saved_progress_table_fails_the_check(self):
        fresh_world(self.source, played=False)
        cw.clean(self.source, self.out)
        clean_bytes = self.out.read_bytes()
        planted = EMPTIED + ["player guild", "player guild rank"]
        self.assertGreater(len(planted), 70)
        for table in planted:
            with self.subTest(table=table):
                self.out.write_bytes(clean_bytes)
                with closing(sqlite3.connect(self.out)) as con:
                    if table == "Account":
                        con.execute("INSERT INTO Account VALUES ('a2', 'someone')")
                    elif table == "DOLCharacters":
                        con.execute("INSERT INTO DOLCharacters VALUES ('c2', 'Someone')")
                    elif table == "offline_world_bots":
                        con.execute("INSERT INTO offline_world_bots (Name) VALUES ('Planted')")
                    elif table == "player guild":
                        con.execute("INSERT INTO Guild (GuildID, GuildName) VALUES ('planted', 'Planted')")
                    elif table == "player guild rank":
                        con.execute("INSERT INTO GuildRank VALUES ('planted', 1)")
                    else:
                        con.execute(f'INSERT INTO "{table}" VALUES (\'planted\')')
                    con.commit()
                with self.assertRaises(cw.NotClean):
                    cw.verify(self.out)
                code, text = quiet(cw.main, ["verify", str(self.out)])
                self.assertEqual(code, 1)
                self.assertIn("::error title=Clean world::", text)
                self.assertIn("NOT CLEAN", text)

    def test_a_server_declared_table_missing_from_the_policy_stops_it(self):
        fresh_world(self.source, played=False)
        cw.clean(self.source, self.out)
        real = cw.release.economy_tables
        cw.release.economy_tables = lambda repo: real(repo) + ["offline_new_saved_table"]
        try:
            with self.assertRaisesRegex(cw.NotClean, "offline_new_saved_table"):
                cw.verify(self.out)
        finally:
            cw.release.economy_tables = real

    def test_a_captured_keep_or_a_broken_door_fails_the_check(self):
        fresh_world(self.source, played=False)
        cw.clean(self.source, self.out)
        with closing(sqlite3.connect(self.out)) as con:
            con.execute("UPDATE Keep SET ClaimedGuildName='Mine' WHERE KeepID=?", (int(next(iter(DEFAULTS["Keep"]))),))
            con.commit()
        with self.assertRaisesRegex(cw.NotClean, "keep"):
            cw.verify(self.out)

    def test_missing_damaged_or_incomplete_worlds_fail_closed(self):
        with self.assertRaisesRegex(cw.NotClean, "no world database"):
            cw.verify(self.root / "missing.db")
        damaged = self.root / "damaged.db"
        damaged.write_bytes(b"not a database at all" * 100)
        with self.assertRaisesRegex(cw.NotClean, "could not be checked"):
            cw.verify(damaged)
        fresh_world(self.source, played=False)
        cw.clean(self.source, self.out)
        with closing(sqlite3.connect(self.out)) as con:
            con.execute("DROP TABLE offline_world_bots")
            con.commit()
        with self.assertRaisesRegex(cw.NotClean, "could not be checked"):
            cw.verify(self.out)

    def test_it_never_writes_over_a_file_and_leaves_nothing_half_made(self):
        fresh_world(self.source)
        self.out.parent.mkdir(parents=True)
        self.out.write_bytes(b"keep me")
        with self.assertRaisesRegex(cw.NotClean, "exists"):
            cw.clean(self.source, self.out)
        self.assertEqual(self.out.read_bytes(), b"keep me")
        broken = self.root / "broken.db"
        fresh_world(broken, played=False)
        with closing(sqlite3.connect(broken)) as con:
            con.execute("DELETE FROM Keep WHERE KeepID=?", (int(next(iter(DEFAULTS["Keep"]))),))   # clean_world asserts
            con.commit()
        target = self.root / "never.db"
        with self.assertRaises(cw.NotClean):
            cw.clean(broken, target)
        self.assertFalse(target.exists())

    def test_the_command_line_reports_clean(self):
        fresh_world(self.source)
        report = self.root / "report.json"
        code, text = quiet(cw.main, ["clean", str(self.source), "--out", str(self.out), "--report", str(report)])
        self.assertEqual(code, 0, text)
        self.assertIn("::notice title=Clean world::CLEAN", text)
        self.assertEqual(json.loads(report.read_text())["quick_check"], "ok")
        code, text = quiet(cw.main, ["verify", str(self.out)])
        self.assertEqual(code, 0, text)


if __name__ == "__main__":
    unittest.main()
