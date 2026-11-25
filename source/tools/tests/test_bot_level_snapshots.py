"""Tests of bot_level_snapshots.py on small synthetic rosters built here (no database from any played world).

    python3 -m pytest -q source/tools/tests/test_bot_level_snapshots.py
"""
import contextlib
import io
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import bot_level_snapshots as bls  # noqa: E402


class BotLevelSnapshotsTest(unittest.TestCase):
    def snapshot(self, rows, retired_column=True):
        handle = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        handle.close()
        path = Path(handle.name)
        self.addCleanup(path.unlink)
        with sqlite3.connect(path) as db:
            extra = ", IsRetired INTEGER NOT NULL DEFAULT 0" if retired_column else ""
            db.execute("CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY, Name TEXT NOT NULL, "
                       f"Realm INTEGER NOT NULL, Level INTEGER NOT NULL{extra})")
            for row in rows:
                cols = "BotId, Name, Realm, Level" + (", IsRetired" if retired_column else "")
                db.execute(f"INSERT INTO offline_world_bots ({cols}) VALUES ({', '.join('?' * len(row))})", row)
        return path

    def test_bands_leave_out_rerolled_replaced_new_and_retired_bots(self):
        before = self.snapshot([
            (1, "Alpha", 1, 12, 0), (2, "Bravo", 1, 13, 0), (3, "Celt", 3, 16, 0),
            (4, "Dropper", 3, 18, 0),    # re-rolled in place: level drops
            (5, "Swapped", 3, 17, 0),    # replaced: its name comes back under id 9
            (6, "Old", 2, 22, 0),        # retired in the later snapshot
            (7, "Mid", 2, 21, 0),
        ])
        after = self.snapshot([
            (1, "Alpha", 1, 18, 0), (2, "Bravo", 1, 17, 0), (3, "Celt", 3, 21, 0),
            (4, "Dropper", 3, 1, 0), (9, "Swapped", 3, 4, 0), (10, "Fresh", 3, 3, 0),
            (6, "Old", 2, 22, 1), (7, "Mid", 2, 24, 0),
        ])
        report = bls.build_report([before, after])
        window = report["windows"][0]
        self.assertEqual(window["bands"]["Albion"]["10-14"], {"bots": 2, "levelsGained": 10, "meanGain": 5.0})
        self.assertEqual(window["bands"]["Hibernia"], {"15-19": {"bots": 1, "levelsGained": 5, "meanGain": 5.0}},
                         "the re-rolled, replaced and new bots add no levels")
        self.assertEqual(window["bands"]["Midgard"], {"20-24": {"bots": 1, "levelsGained": 3, "meanGain": 3.0}})
        self.assertEqual(window["flags"]["Hibernia"], {"levelDropped": 1, "replaced": 1, "new": 2, "retired": 0})
        self.assertEqual(window["flags"]["Midgard"]["retired"], 1)
        text = bls.render(report)
        self.assertIn("Hibernia: 1 level dropped, 1 replaced, 2 new, 0 retired", text)
        for name in ("Alpha", "Dropper", "Swapped", "Fresh"):
            self.assertNotIn(name, text, "names are not printed")

    def test_three_snapshots_make_two_windows_and_an_old_roster_reads(self):
        a = self.snapshot([(1, "A", 1, 5)], retired_column=False)
        b = self.snapshot([(1, "A", 1, 9)], retired_column=False)
        c = self.snapshot([(1, "A", 1, 11)], retired_column=False)
        report = bls.build_report([a, b, c])
        self.assertEqual([w["bands"]["Albion"] for w in report["windows"]],
                         [{"05-09": {"bots": 1, "levelsGained": 4, "meanGain": 4.0}},
                          {"05-09": {"bots": 1, "levelsGained": 2, "meanGain": 2.0}}])

    def test_snapshot_is_read_only_and_cli(self):
        path = self.snapshot([(1, "A", 1, 5, 0)])
        later = self.snapshot([(1, "A", 1, 6, 0)])
        before = path.read_bytes()
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(bls.main([str(path), str(later), "--json"]), 0)
        self.assertEqual(json.loads(out.getvalue())["windows"][0]["bands"]["Albion"]["05-09"]["bots"], 1)
        self.assertEqual(path.read_bytes(), before, "the snapshot is not written")
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(bls.main([str(path)]), 2, "one snapshot is not a comparison")
        empty = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        empty.close()
        self.addCleanup(Path(empty.name).unlink)
        sqlite3.connect(empty.name).close()
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(bls.main([empty.name, str(path)]), 2, "no roster table")


if __name__ == "__main__":
    unittest.main()
