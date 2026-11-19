"""Living Server 1.2 lore books: show or remove the book item templates the server adds (offline, server closed).

The server adds one item template per lore location at every start (id ls_lore_book_<location>, PackageID
LivingServerLore, the stock "Book" model 500; LoreBooks.cs in the server). They change no stock row. This shows them
and, with rollback, removes them from a world database, for example after taking the Living Server build out:

  python tools\\claude-version\\lore_books.py                    (show the rows; nothing is changed)
  python tools\\claude-version\\lore_books.py rollback           (dry run: what rollback would remove)
  python tools\\claude-version\\lore_books.py rollback --apply   (server, launcher and game closed; backs up first)

A template that someone still holds a copy of (any row of the Inventory table: packs, vaults) is kept, so no item loses its
template; the output names it. Only rows with the PackageID LivingServerLore and the id prefix ls_lore_book_ are
touched. A server 1.2 or later started again adds the books back.
"""
import argparse
import contextlib
import datetime
import os
import sqlite3
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]  # <playable>/tools/claude-version/
DB = ROOT / 'runtime' / 'data' / 'opendaoc.sqlite3.db'
PACKAGE = 'LivingServerLore'
PREFIX = 'ls_lore_book_'


def has_table(db, name):
    return db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (name,)).fetchone() is not None


def books(db):
    """[(id, name)] of the lore book templates in this world."""
    if not has_table(db, 'ItemTemplate'):
        return []
    return db.execute("SELECT Id_nb, Name FROM ItemTemplate WHERE PackageID=? AND substr(Id_nb,1,?)=? ORDER BY Id_nb",
                      (PACKAGE, len(PREFIX), PREFIX)).fetchall()


def held(db, template_id):
    return has_table(db, 'Inventory') and db.execute(
        'SELECT 1 FROM Inventory WHERE ITemplate_Id=? LIMIT 1', (template_id,)).fetchone() is not None


def plan(db):
    """(to remove, kept because held) as lists of template ids. Nothing is written."""
    remove, kept = [], []
    for template_id, _ in books(db):
        (kept if held(db, template_id) else remove).append(template_id)
    return remove, kept


def roll_back(db):
    remove, kept = plan(db)
    for template_id in remove:
        db.execute('DELETE FROM ItemTemplate WHERE Id_nb=? AND PackageID=?', (template_id, PACKAGE))
    return remove, kept


def running_reason(db_path):
    """Why the server looks open, or None (as epic_chains.py)."""
    if os.name == 'nt':
        running = subprocess.check_output(['powershell', '-NoProfile', '-Command',
            'Get-Process CoreServer,DOLServer,OfflineDAoC,game -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName; exit 0'], text=True)
        if running.strip():
            return 'Close the server, launcher and game first: ' + running.strip()
    wal = Path(str(db_path) + '-wal')
    if wal.exists() and wal.stat().st_size > 0:
        return (f'{wal.name} is not empty, so the server is running or did not close cleanly. Close the server, '
                'launcher and game (or start the server and close it normally once) and try again.')
    return None


def backup(db, db_path):
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    folder = db_path.parent.parent / 'server' / 'deployment-backups' / f'lore-books-rollback-{stamp}'
    folder.mkdir(parents=True, exist_ok=False)
    with contextlib.closing(sqlite3.connect(folder / 'opendaoc.sqlite3.db')) as copy:
        db.backup(copy)
    return folder


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('action', nargs='?', choices=['show', 'rollback'], default='show')
    parser.add_argument('--apply', action='store_true', help='write (server, launcher and game closed)')
    parser.add_argument('--db', type=Path, default=DB, help='the database (default: this game folder)')
    args = parser.parse_args(argv)
    if not args.db.exists():
        raise SystemExit(f'No database at {args.db}')
    with contextlib.closing(sqlite3.connect(args.db.resolve().as_uri() + ('' if args.apply else '?mode=ro'), uri=True)) as db:
        rows = books(db)
        if args.action == 'show':
            print(f'{len(rows)} lore book templates.')
            for template_id, name in rows:
                print(f'  {template_id}: {name}' + ('  (someone holds a copy)' if held(db, template_id) else ''))
            return 0
        remove, kept = plan(db)
        print(f'Lore book templates to remove: {len(remove)}' + (f'; kept (someone holds a copy): {", ".join(kept)}' if kept else ''))
        if not remove:
            print('Nothing to remove.')
            return 0
        if not args.apply:
            print('Dry run: nothing was changed. Add --apply to remove them.')
            return 0
        reason = running_reason(args.db)
        if reason:
            raise SystemExit(reason)
        folder = backup(db, args.db)
        db.execute('BEGIN IMMEDIATE')
        try:
            removed, kept = roll_back(db)
            if db.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
                raise RuntimeError('integrity check failed; rolling back')
            db.commit()
        except BaseException:
            db.rollback()
            raise
    print(f'Removed {len(removed)} lore book templates.' + (f' Kept: {", ".join(kept)}' if kept else ''))
    print('Backup:', folder)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
