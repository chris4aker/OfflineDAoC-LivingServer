"""epic_chains.py on a small temporary database built from its own data file. No real install is read or written.
With EPIC_TEST_WORLD set to a copy of a clean 0.37b world database, it also runs on a copy of that world.

    python tools/claude-version/tests/test_epic_chains.py
"""
import contextlib
import io
import os
import shutil
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import epic_chains as tool  # noqa: E402

DATA = tool.load_data()
CLASSES = list(DATA['classes'].items())
LEVELS = {'7': 7, '7closed': 7, '7si': 7, '11': 11, '11si': 11, '15': 15, '20': 20, '25': 25, '30': 30, '40': 40,
          '43': 43, '45': 45, '48': 48}
NAMES = {'7': "Traveler's Way -- Supply Run", '7closed': "Traveler's Way -- Supply Run", '7si': 'Strange Beings',
         '11': 'Entry Into Tomorrow', '11si': 'Shades and Shadows', '15': 'Rebellion Accepted',
         '20': 'Path of the Renegade', '25': 'Regal Nobility', '30': 'Regal Nobility', '40': 'Hidden Insurrection',
         '43': 'Lord of Deceit', '45': 'Lord of Deceit', '48': 'Lord of Deceit'}
SCHEMA = """CREATE TABLE DataQuest (ID INTEGER PRIMARY KEY, Name TEXT, MinLevel INT, MaxLevel INT, AllowedClasses TEXT,
  QuestDependency TEXT, ClassType TEXT, StartName TEXT, AcceptText TEXT, Description TEXT, SourceText TEXT, StepType TEXT,
  StepText TEXT, TargetText TEXT, AdvanceText TEXT, FinishText TEXT, StepItemTemplates TEXT, CollectItemTemplate TEXT,
  FinalRewardItemTemplates TEXT, LastTimeRowUpdated TEXT)"""


def build(path):
    """The Guild of Shadows rows as the clean world has their keys, plus a Defenders-like line to pin."""
    con = sqlite3.connect(path)
    con.execute(SCHEMA)
    for key, step in DATA['steps'].items():
        for (_, class_id), qid in zip(CLASSES, step['ids']):
            if qid is not None:
                con.execute('INSERT INTO DataQuest (ID, Name, MinLevel, MaxLevel, AllowedClasses, QuestDependency, ClassType, '
                            'StepType, SourceText) VALUES (?,?,?,50,?,?,?,?,?)',
                            (qid, NAMES[key], LEVELS[key], str(class_id), step['upstream_dependency'], tool.CLASSIC, '4|5', 'a|b'))
    for fix in DATA['rewards']:
        con.execute('UPDATE DataQuest SET FinalRewardItemTemplates=? WHERE ID=?', (fix['upstream'], fix['id']))
    con.execute("UPDATE DataQuest SET SourceText='Let this (reward) be yours, (class name) of (prof name).|' WHERE ID IN "
                "(21489, 21490, 21491, 21492, 21493)")
    for qid, level, dep in ((30001, 20, ''), (30002, 25, 'Legend of the Lake'), (30003, 30, 'Legend of the Lake')):
        con.execute('INSERT INTO DataQuest (ID, Name, MinLevel, MaxLevel, AllowedClasses, QuestDependency, ClassType) '
                    'VALUES (?,?,?,50,?,?,?)', (qid, 'Legend of the Lake' if qid < 30003 else 'Hands Of Fate', level, '2', dep,
                                                tool.CLASSIC))
    con.commit()
    con.close()


def dependency_met(entry, rows, finished):
    """DataQuest's rule for one entry (QuestDependencies.IsMet in C#)."""
    text = entry.strip()
    if not text.startswith(('#', '!#')):
        return text.lower() in {rows[i][0].lower() for i in finished}
    closes = text.startswith('!#')
    ids = {int(p) for p in text[2 if closes else 1:].split('/')}
    return not ids & finished if closes else bool(ids & finished)


def walk(test, db):
    """Every class can take 7 or 7 SI, then exactly the next step of its branch, to 48."""
    rows = {qid: (name, dep or '', classes) for qid, name, dep, classes in
            db.execute('SELECT ID, Name, QuestDependency, AllowedClasses FROM DataQuest')}
    steps = DATA['steps']
    for col, (name, class_id) in enumerate(CLASSES):
        mine = [q for s in steps.values() for q in [s['ids'][col]] if q and str(class_id) in rows[q][2].split('|')]

        def can(q, done):
            return q not in done and all(dependency_met(e, rows, done) for e in rows[q][1].split('|') if e)

        ids = {k: s['ids'][col] for k, s in steps.items()}
        for route in (['7', '11'], ['7si', '11si']):
            done = set()
            test.assertEqual(sorted(q for q in mine if can(q, done)), sorted([ids['7'], ids['7si']]), name)
            for key in route + ['15', '20', '25', '30', '40', '43', '45', '48']:
                if key not in route:
                    test.assertEqual([q for q in mine if can(q, done)], [ids[key]], f'{name} {route} {key}')
                done.add(ids[key])
            test.assertEqual([q for q in mine if can(q, done)], [], name)
        if ids['7closed']:
            test.assertFalse(can(ids['7closed'], set()), f'{name}: the closed Supply Run is offered')


class EpicChainsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.db = Path(self.temp.name) / 'runtime/data/opendaoc.sqlite3.db'
        self.db.parent.mkdir(parents=True)
        build(self.db)
        patcher = mock.patch.object(tool.subprocess, 'check_output', return_value='')
        patcher.start()
        self.addCleanup(patcher.stop)

    def tearDown(self):
        self.temp.cleanup()

    def run_tool(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            tool.main([*args, '--db', str(self.db)])
        return out.getvalue()

    def snapshot(self):
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            return con.execute('SELECT * FROM DataQuest ORDER BY ID').fetchall()

    def drop_times(self, rows):
        return [row[:-1] for row in rows]

    def test_the_data_file_links_known_steps_for_five_classes(self):
        self.assertEqual(len(DATA['classes']), 5)
        for key, step in DATA['steps'].items():
            self.assertEqual(len(step['ids']), 5, key)
            for link in step.get('links', []):
                for part in link.lstrip('!').split('/'):
                    self.assertIn(part, DATA['steps'], f'{key}: {link}')
        for entry in DATA['dialogue']:
            self.assertIn(entry['step'], DATA['steps'])
            self.assertTrue(entry['guard'])

    def test_a_dry_run_changes_nothing(self):
        before = self.snapshot()
        out = self.run_tool()
        self.assertIn('links 60', out)
        self.assertIn('Dry run', out)
        self.assertEqual(self.snapshot(), before)

    def test_apply_links_the_chain_and_pins_other_lines(self):
        out = self.run_tool('--apply')
        self.assertIn('links 60, pins 2, closed 4, rewards 2, texts 5', out)
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            walk(self, con)
            # A dependency on its own name, and one naming quests at two levels, point at the step before.
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=30002').fetchone()[0], '#30001')
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=30003').fetchone()[0], '#30002')
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=20188').fetchone()[0], '#20157/20469')
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=20157').fetchone()[0],
                             '#21500/21324|!#20469')
            self.assertEqual(con.execute('SELECT FinalRewardItemTemplates FROM DataQuest WHERE ID=20159').fetchone()[0],
                             'cq_alb_flayed_skin_necklace')
            self.assertEqual(con.execute('SELECT SourceText FROM DataQuest WHERE ID=21489').fetchone()[0],
                             'Let this reward be yours, <Class> of the Guild of Shadows.|')
        self.assertEqual(len(list((self.db.parent.parent / 'server/deployment-backups').iterdir())), 1)

    def test_a_second_apply_is_refused_and_rollback_restores_everything(self):
        before = self.snapshot()
        self.run_tool('--apply')
        self.assertIn('Already applied', self.run_tool('--apply'))
        self.assertIn('Put back', self.run_tool('rollback', '--apply'))
        self.assertEqual(self.drop_times(self.snapshot()), self.drop_times(before))
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            self.assertFalse(tool.has_table(con, tool.LEDGER))

    def test_rollback_leaves_a_value_changed_since(self):
        self.run_tool('--apply')
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            con.execute("UPDATE DataQuest SET QuestDependency='mine' WHERE ID=20188")
            con.commit()
        out = self.run_tool('rollback', '--apply')
        self.assertIn('20188.QuestDependency', out)
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=20188').fetchone()[0], 'mine')
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=20455').fetchone()[0], 'Rebellion Accepted')

    def test_a_row_changed_by_the_owner_is_left_alone(self):
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            con.execute("UPDATE DataQuest SET QuestDependency='Something Else' WHERE ID=20455")
            con.commit()
        self.run_tool('--apply')
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            self.assertEqual(con.execute('SELECT QuestDependency FROM DataQuest WHERE ID=20455').fetchone()[0], 'Something Else')

    def test_dialogue_is_written_only_over_a_guarded_text(self):
        entry = next(e for e in DATA['dialogue'] if e['step'] == '15')
        qid = DATA['steps']['15']['ids'][0]
        columns = sorted(entry['set'])
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            con.execute('UPDATE DataQuest SET StepType=? WHERE ID IN (%s)' % ','.join('?' * 5),
                        ('4|6|6|7', *DATA['steps']['15']['ids']))
            con.commit()
            held = [con.execute(f'SELECT "{c}" FROM DataQuest WHERE ID=?', (qid,)).fetchone()[0] or '' for c in columns]
        data = dict(DATA, dialogue=[dict(entry, guard={str(qid): [tool.digest(held)]})])
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            changes, kept = tool.plan(con, data)
        written = {q for q, *_ in changes.rows()} & set(DATA['steps']['15']['ids'])
        self.assertIn(qid, written)
        self.assertEqual(sorted(kept), sorted(q for q in DATA['steps']['15']['ids'][1:] if q))

    def test_a_running_server_stops_apply(self):
        (Path(str(self.db) + '-wal')).write_bytes(b'x')
        with self.assertRaises(SystemExit):
            self.run_tool('--apply')


@unittest.skipUnless(os.environ.get('EPIC_TEST_WORLD'), 'needs EPIC_TEST_WORLD (a clean 0.37b world database)')
class CleanWorldTests(unittest.TestCase):
    def test_the_clean_world(self):
        with tempfile.TemporaryDirectory() as temp:
            db = Path(temp) / 'runtime/data/opendaoc.sqlite3.db'
            db.parent.mkdir(parents=True)
            shutil.copy2(os.environ['EPIC_TEST_WORLD'], db)
            out = io.StringIO()
            with mock.patch.object(tool.subprocess, 'check_output', return_value=''), contextlib.redirect_stdout(out):
                tool.main(['--apply', '--db', str(db)])
            self.assertIn('links 60, pins 87, closed 4, rewards 2, texts 6, dialogue 35', out.getvalue())
            self.assertNotIn('left as it is', out.getvalue())
            with contextlib.closing(sqlite3.connect(db)) as con:
                walk(self, con)


if __name__ == '__main__':
    unittest.main()
