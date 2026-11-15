"""Tests of make_classoff_copy.py: the copy becomes the 0.37 edition, the source is never changed, and the guards hold.

    python -m unittest discover -s source/tools/tests -v
"""
import hashlib
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import make_classoff_copy as mcc  # noqa: E402

SLUAGH = b"MZ 0.37b client with the Sluaghbinder patches"
CLASSIC = b"MZ 0.37 client without them"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tree_digest(folder: Path) -> dict:
    return {str(p.relative_to(folder)): digest(p.read_bytes()) for p in sorted(folder.rglob("*")) if p.is_file()}


def make_game(parent: Path, name: str, client: bytes = SLUAGH) -> Path:
    game = parent / name
    (game / "runtime" / "data").mkdir(parents=True)
    (game / "runtime" / "server").mkdir(parents=True)
    (game / "runtime" / "client-opendaoc" / "app").mkdir(parents=True)
    (game / mcc.GAME_DLL).write_bytes(client)
    (game / "runtime" / "server" / "GameServer.dll").write_bytes(b"server")
    con = sqlite3.connect(game / mcc.WORLD)
    con.execute("CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT PRIMARY KEY, Description TEXT, DefaultValue TEXT, "
                "Value TEXT, LastTimeRowUpdated TEXT, ServerProperty_ID TEXT)")
    con.execute("INSERT INTO ServerProperty VALUES ('classes','enable_sluaghbinder','','True','True','','enable_sluaghbinder')")
    con.execute("CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT)")
    con.executemany("INSERT INTO Mob VALUES (?, ?)", [(m, m) for m in mcc.release.SLUAGH_MOBS] + [("guard", "Guard")])
    con.execute("CREATE TABLE ClassXSpecialization (ClassID INT, Spec TEXT)")
    con.execute("INSERT INTO ClassXSpecialization VALUES (63, 'Binding'), (53, 'Nurture')")
    con.execute("CREATE TABLE DOLCharacters (Name TEXT, AccountName TEXT, Realm INT, Class INT)")
    con.execute("INSERT INTO DOLCharacters VALUES ('Shade', 'owner', 3, 16), ('Fern', 'owner', 3, 53)")
    con.execute("CREATE TABLE offline_world_bots (Name TEXT, Realm INT, ClassId INT)")
    con.execute("INSERT INTO offline_world_bots VALUES ('Aodhan', 3, 63), ('Brannoc', 3, 47)")
    con.commit()
    con.close()
    return game


class ClassOffCopyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        patch = mock.patch.multiple(mcc, CLASSIC_GAME_DLL=digest(CLASSIC), SLUAGH_GAME_DLL=digest(SLUAGH))
        patch.start()
        self.addCleanup(patch.stop)
        self.fresh = make_game(self.dir, "playable-v0.37b-fresh")
        self.classic = self.dir / "new-install-game.dll"
        self.classic.write_bytes(CLASSIC)

    def tearDown(self):
        self.tmp.cleanup()

    def test_the_copy_is_the_037_edition_and_the_source_is_untouched(self):
        before = tree_digest(self.fresh)
        self.assertEqual(mcc.status(self.fresh), "EDITION enable_sluaghbinder=True game.dll=sluaghbinder")
        report = mcc.make(self.fresh, self.dir / "playable-v0.37-classoff", self.classic, check_port=False)
        copy = self.dir / "playable-v0.37-classoff"
        self.assertEqual(report["status"], "EDITION enable_sluaghbinder=False game.dll=classic")
        self.assertEqual(mcc.status(copy), report["status"])
        self.assertEqual((copy / mcc.GAME_DLL).read_bytes(), CLASSIC)
        self.assertEqual((copy / "runtime" / "server" / "GameServer.dll").read_bytes(), b"server")
        self.assertEqual(report["removed_mobs"], 2)
        self.assertEqual(report["removed_class_specializations"], 1)
        self.assertEqual(report["characters"], [("Shade", "owner", 16)])
        self.assertEqual(report["gamebots"], [("Aodhan", 63)])
        con = sqlite3.connect(copy / mcc.WORLD)
        self.assertEqual(con.execute("SELECT Name FROM Mob").fetchall(), [("Guard",)])
        con.close()
        self.assertEqual(tree_digest(self.fresh), before, "the source folder changed")

    def test_status_names_the_edition_and_the_custom_class_saves(self):
        import contextlib
        import io
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(mcc.main(["status", "--game", str(self.fresh)]), 0)
        lines = out.getvalue().splitlines()
        self.assertEqual(lines[0], "EDITION enable_sluaghbinder=True game.dll=sluaghbinder")
        self.assertEqual(lines[1], "CUSTOM-CLASS characters=1 gamebots=1")
        self.assertIn("Shade", lines[2])

    def test_the_main_install_is_refused(self):
        main = make_game(self.dir, "playable-v0.37b")
        before = tree_digest(main)
        with self.assertRaises(SystemExit):
            mcc.make(main, self.dir / "playable-v0.37-classoff", self.classic, check_port=False)
        with self.assertRaises(SystemExit):
            mcc.make(self.fresh, self.dir / "playable-v0.37b", self.classic, check_port=False)
        self.assertEqual(tree_digest(main), before)

    def test_targets_that_exist_or_lie_elsewhere_are_refused(self):
        (self.dir / "taken").mkdir()
        with self.assertRaises(SystemExit):
            mcc.make(self.fresh, self.dir / "taken", self.classic, check_port=False)
        (self.dir / "elsewhere").mkdir()
        with self.assertRaises(SystemExit):
            mcc.make(self.fresh, self.dir / "elsewhere" / "copy", self.classic, check_port=False)

    def test_the_client_hash_guards(self):
        wrong = self.dir / "wrong.dll"
        wrong.write_bytes(b"MZ something else")
        with self.assertRaises(SystemExit):
            mcc.make(self.fresh, self.dir / "c1", wrong, check_port=False)
        with self.assertRaises(SystemExit):
            mcc.make(self.fresh, self.dir / "c2", None, check_port=False)
        odd = make_game(self.dir, "playable-odd", client=b"MZ patched by hand")
        with self.assertRaises(SystemExit):
            mcc.make(odd, self.dir / "c3", self.classic, check_port=False)
        self.assertFalse(any((self.dir / n).exists() for n in ("c1", "c2", "c3")), "a refused copy left a folder")

    def test_a_game_with_the_classic_client_needs_no_replacement(self):
        game = make_game(self.dir, "playable-classic", client=CLASSIC)
        report = mcc.make(game, self.dir / "playable-classic-off", None, check_port=False)
        self.assertFalse(report["client_replaced"])
        self.assertEqual(report["status"], "EDITION enable_sluaghbinder=False game.dll=classic")


if __name__ == "__main__":
    unittest.main()
