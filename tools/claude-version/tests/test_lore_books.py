"""lore_books.py on a small temporary world. No real install is read or written.

    python tools/claude-version/tests/test_lore_books.py
"""
import contextlib
import io
import shutil
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lore_books as tool  # noqa: E402


class LoreBooksTest(unittest.TestCase):
    def setUp(self):
        self.folder = Path(tempfile.mkdtemp(prefix='lore-books-'))
        (self.folder / 'data').mkdir()
        self.db = self.folder / 'data' / 'opendaoc.sqlite3.db'
        con = sqlite3.connect(self.db)
        con.execute('CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT, PackageID TEXT)')
        con.execute('CREATE TABLE Inventory (InventoryItem_ID TEXT, ITemplate_Id TEXT)')
        con.executemany('INSERT INTO ItemTemplate VALUES (?,?,?)', [
            ('ls_lore_book_krondon', 'Of the Hall under the Hill', 'LivingServerLore'),
            ('ls_lore_book_trollheim', 'Under the Troll Hills', 'LivingServerLore'),
            ('ls_lore_book_other', 'Not ours', 'SomeoneElse'),
            ('stock_book', 'Tome of Enchantments', 'LivingServerLore'),
        ])
        con.execute("INSERT INTO Inventory VALUES ('i1', 'ls_lore_book_trollheim')")
        con.commit()
        con.close()

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def run_tool(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = tool.main([*args, '--db', str(self.db)])
        return code, out.getvalue()

    def ids(self):
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            return sorted(r[0] for r in con.execute('SELECT Id_nb FROM ItemTemplate'))

    def test_show_and_dry_run_change_nothing(self):
        before = self.db.read_bytes()
        code, out = self.run_tool()
        self.assertEqual(code, 0)
        self.assertIn('2 lore book templates', out)
        self.assertIn('ls_lore_book_trollheim: Under the Troll Hills  (someone holds a copy)', out)
        code, out = self.run_tool('rollback')
        self.assertIn('to remove: 1; kept (someone holds a copy): ls_lore_book_trollheim', out)
        self.assertIn('Dry run', out)
        self.assertEqual(self.db.read_bytes(), before)

    def test_rollback_removes_only_unheld_own_books_after_a_backup(self):
        code, out = self.run_tool('rollback', '--apply')
        self.assertEqual(code, 0)
        self.assertEqual(self.ids(), ['ls_lore_book_other', 'ls_lore_book_trollheim', 'stock_book'])
        backups = list((self.folder / 'server' / 'deployment-backups').glob('lore-books-rollback-*/opendaoc.sqlite3.db'))
        self.assertEqual(len(backups), 1)
        with contextlib.closing(sqlite3.connect(backups[0])) as con:
            self.assertEqual(con.execute('SELECT count(*) FROM ItemTemplate').fetchone()[0], 4)
        code, out = self.run_tool('rollback', '--apply')
        self.assertIn('Nothing to remove', out)

    def test_refuses_while_the_server_has_the_database_open(self):
        Path(str(self.db) + '-wal').write_bytes(b'x')
        with self.assertRaises(SystemExit):
            self.run_tool('rollback', '--apply')
        self.assertEqual(len(self.ids()), 4)


if __name__ == '__main__':
    unittest.main()
