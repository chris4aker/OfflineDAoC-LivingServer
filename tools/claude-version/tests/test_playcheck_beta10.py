"""playcheck_beta10.py on a small temporary game folder and log. No real install is read or written.

    python tools/claude-version/tests/test_playcheck_beta10.py
"""
import contextlib
import datetime
import io
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import playcheck_beta10 as tool  # noqa: E402

SCHEMA = """
CREATE TABLE `ServerProperty` (`Category` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `Key` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
  `Description` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `DefaultValue` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
  `Value` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
  `ServerProperty_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, PRIMARY KEY (`Key`));
CREATE TABLE `offline_lore_memory` (`MemoryKey` VARCHAR(160) NOT NULL, `Value` VARCHAR(128) NOT NULL, `UpdatedUtc` TEXT NOT NULL);
CREATE TABLE `offline_world_bots` (`Name` TEXT NOT NULL, `Realm` INT NOT NULL, `Level` INT NOT NULL, `Experience` BIGINT NOT NULL,
  `IsOnline` TINYINT(1) NOT NULL, `IsRetired` TINYINT(1) NOT NULL);
"""


class PlaycheckBeta10Tests(unittest.TestCase):
    def test_a_solo_first_kill_at_level_one(self):
        # 1-19:1.5: a level 0, 1 or 2+ monster gives 7, 15 or 18 (cap 12); without the factor 5, 10 or 12.
        with_factor = tool.kill_amounts(1, 1.5)
        self.assertEqual((with_factor[0], with_factor[1], with_factor[2], with_factor[30]), (7, 15, 18, 18))
        self.assertEqual(sorted(set(tool.kill_amounts(1, 1.0).values())), [5, 10, 12])

    def test_a_grouped_kill_shares_the_value_and_adds_the_unscaled_group_bonus(self):
        # Level 22 with one groupmate, a level 23 monster: ceil(327680 / 2) = 163840, x 2.5 = 409600, + 12.5% = 20480.
        self.assertEqual(tool.kill_amounts(22, 2.5, group=2)[23], 430080)
        # At the cap: a level 30 monster's half (825702) is over level 22's cap, 289631 (231705 x 1.25).
        self.assertEqual(tool.kill_amounts(22, 2.5, group=2)[30], int(289631 * 2.5) + int(289631 * 0.125))

    def test_a_watched_kill_is_judged_with_or_without_the_factor(self):
        spec = '1-19:1.5;20-34:2.5;35-50:6'
        self.assertIn('EXACT with the factor 2.5: a level [23] monster, 2 in the group', tool.judge_kill(430080, 22, spec, 1.0))
        solo = tool.judge_kill(int(289631 * 2.5), 22, spec, 1.0)  # alone, any monster over level 22's cap
        self.assertTrue(solo.startswith('EXACT with the factor 2.5: a level [23, 24') and solo.endswith('1 in the group'), solo)
        self.assertIn('factor NOT applied: a level [23] monster, 2 in the group', tool.judge_kill(163840 + 20480, 22, spec, 1.0))
        self.assertIn('no single kill', tool.judge_kill(123, 22, spec, 1.0))

    def test_the_rate_of_a_level(self):
        spec = '1-19:1.5;20-34:2.5;35-50:6'
        self.assertEqual([tool.rate_for(spec, level, 1.0) for level in (1, 19, 20, 34, 35, 49)], [1.5, 1.5, 2.5, 2.5, 6.0, 6.0])
        self.assertEqual(tool.rate_for('20-34:3', 10, 1.2), 1.2, 'outside every range: the flat rate')

    def test_the_clock_share_as_the_server_has_it(self):
        self.assertAlmostEqual(tool.clock_share(datetime.datetime(2026, 11, 10, 19, 30)), 0.93)       # a Tuesday, 0.90 toward 0.96
        self.assertAlmostEqual(tool.clock_share(datetime.datetime(2026, 11, 13, 17, 30)), 0.785)      # Friday, toward the weekend
        self.assertAlmostEqual(tool.clock_share(datetime.datetime(2026, 11, 14, 4, 0)), 0.18)         # Saturday 4 a.m.

    def test_prep_sets_the_camp_bonus_to_zero_and_restore_puts_it_back(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'runtime/data').mkdir(parents=True)
            db_path = root / 'runtime/data/opendaoc.sqlite3.db'
            with contextlib.closing(sqlite3.connect(db_path)) as db, db:
                db.executescript(SCHEMA)
                db.execute("INSERT INTO ServerProperty (Category, [Key], Value, ServerProperty_ID) VALUES ('pve', 'max_camp_bonus', '0.4', 'max_camp_bonus')")
                db.execute("INSERT INTO offline_lore_memory VALUES ('tale:last:1', ?, '')",
                           ((datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(hours=1)).isoformat(),))
                db.execute("INSERT INTO offline_world_bots VALUES ('Aldwin', 1, 22, 5, 1, 0)")
            args = tool.argparse.Namespace(root=root, apply=True)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(tool.prep(args), 0)
            self.assertIn('TOO RECENT', out.getvalue(), 'a tale an hour ago holds the first tale back')
            with contextlib.closing(sqlite3.connect(db_path)) as db:
                self.assertEqual(tool.properties(db, tool.CAMP_KEYS), {'max_camp_bonus': '0', 'max_dungeon_camp_bonus': '0'})
            self.assertEqual(len(list((root / 'runtime/server/deployment-backups').glob('playcheck-beta10-*/opendaoc.sqlite3.db'))), 1)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(tool.restore(args), 0)
            with contextlib.closing(sqlite3.connect(db_path)) as db:
                self.assertEqual(tool.properties(db, tool.CAMP_KEYS), {'max_camp_bonus': '0.4', 'max_dungeon_camp_bonus': '0.66'})

    def test_the_report_finds_each_rows_line(self):
        log = '\n'.join([
            '11/15 20:00:00 | WRN | DOL.GS.ForkBuildInfo | FORK_BUILD name=x commit=y bot_xp_rate_by_level="1-19:1.5;20-34:2.5;35-50:6" bot_schedule=clock',
            '11/15 20:01:00 | INF | DOL.GS.AutonomousBotGuilds | BOT_GUILD_JOIN bot=Aldwin id=5 guild="Checkers" invitedBy=Testrunner',
            '11/15 20:02:00 | INF | DOL.GS.GameBot | AUTONOMOUS_XP_PROGRESS bot="New" id=9 class="Fighter" amount=18 xp=0->18 level=1->1',
            '11/15 20:03:00 | INF | DOL.GS.GameBot | AUTONOMOUS_XP_PROGRESS bot="Old" id=8 class="Fighter" amount=12 xp=0->12 level=1->1',
            '11/15 20:04:00 | INF | DOL.AI.Brain.BotBrain | LIVING_SERVER_REZ bot="Healy" corpse="Testrunner" distance=300',
            '11/15 20:05:00 | INF | DOL.GS.AutonomousBotSchedule | LIVING_SERVER_LINKDEAD bot="Aldwin" id=5 seconds=40',
        ])
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'server.log'
            path.write_text(log, encoding='utf-8')
            out = io.StringIO()
            args = tool.argparse.Namespace(root=Path(folder), log=path, player='Testrunner', spec=None, flat=1.0, cap_percent=125)
            with contextlib.redirect_stdout(out):
                self.assertEqual(tool.report(args), 0)
        text = out.getvalue()
        self.assertIn('BOT_GUILD_JOIN bot=Aldwin', text)
        self.assertIn('== 5 a passing bot rezzed you (LIVING_SERVER_REZ corpse=you): 1', text)
        self.assertIn('== 10 linkdeads (LIVING_SERVER_LINKDEAD): 1', text)
        self.assertIn("2 lines; 1 are exactly a solo kill with the factor, 1 exactly one without it", text)


if __name__ == '__main__':
    unittest.main()
