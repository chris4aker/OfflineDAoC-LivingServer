"""Bring the Living Server's own tables from an old Offline DAoC save into a new one (0.36 -> 0.37).

The game's own transfer (IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd) moves your account, characters, bots and
guilds, but not the tables only the Living Server uses: which guilds bots run and who is in them, what bots
remember about you, server firsts, nemeses, and which chat lines and /faction scenes you have already heard.
Without them, your bots' guilds turn into empty guilds and bots forget you.

  python tools\\claude-version\\carry_social_tables.py --from "C:\\Games\\OfflineDAoC\\playable-v0.36c"          (show)
  python tools\\claude-version\\carry_social_tables.py --from "C:\\Games\\OfflineDAoC\\playable-v0.36c" --apply  (copy)

Order: install the new game, run its IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd, install the Living Server
build, then run this from the NEW game folder, before you start the new server with bot guilds on. The server,
launcher and game must be closed. --apply backs up the new database first and replaces these tables in it with
the old ones; running it again copies the same rows again. The old save is only read.
"""
import argparse
import contextlib
import datetime
import os
import sqlite3
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]  # <playable>/tools/claude-version/
DB_IN_GAME = Path('runtime/data/opendaoc.sqlite3.db')
# The same list as OptionalProgressTables in source/tools/OfflineDaoc.ProgressImport/progress-policy.json.
TABLES = ['offline_bot_guilds', 'offline_bot_guild_members', 'offline_bot_scenes_heard', 'offline_bot_memory',
          'offline_bot_nemesis', 'offline_server_firsts', 'offline_bot_lines_heard',
          'offline_bot_ragequits', 'offline_player_reputation', 'offline_bot_storylines']


def database(path):
    """A game folder or a database file -> the database file."""
    path = Path(path)
    return path if path.suffix == '.db' or path.is_file() else path / DB_IN_GAME


def running_reason(db_path):
    """Why the server looks open, or None (as set_bot_guilds.py)."""
    if os.name == 'nt':
        running = subprocess.check_output(['powershell', '-NoProfile', '-Command',
            'Get-Process CoreServer,DOLServer,OfflineDAoC,game -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName; exit 0'], text=True)
        if running.strip():
            return 'Close the server, launcher and game first: ' + running.strip()
    wal = Path(str(db_path) + '-wal')
    if wal.exists() and wal.stat().st_size > 0:
        return (f'{wal.name} is not empty, so a server is running or did not close cleanly. Close the server, '
                'launcher and game (or start that server and close it normally once) and try again.')
    return None


def tables(db, schema='main'):
    return {row[0] for row in db.execute(f"SELECT name FROM {schema}.sqlite_master WHERE type='table'")}


def columns(db, table, schema='main'):
    return [row[1] for row in db.execute(f'PRAGMA {schema}.table_info("{table}")')]


def count(db, table, schema='main'):
    return db.execute(f'SELECT count(*) FROM {schema}."{table}"').fetchone()[0]


def plan(db):
    """What --apply would do, with the old database attached as 'old'. Returns (rows, problems, notes)."""
    old, new = tables(db, 'old'), tables(db, 'main')
    rows, problems, notes = [], [], []
    for table in TABLES:
        if table not in old:
            notes.append(f'{table}: not in the old save (that build never made it), nothing to copy')
            continue
        rows.append((table, count(db, table, 'old'), count(db, table, 'main') if table in new else None))
    if 'offline_world_bots' not in new or count(db, 'offline_world_bots') == 0:
        problems.append("The new save has no bots. Run the new game's IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd first.")
    elif 'offline_bot_guild_members' in old:
        members = count(db, 'offline_bot_guild_members', 'old')
        found = db.execute('SELECT count(*) FROM old.offline_bot_guild_members m '
                           'WHERE m.BotId IN (SELECT BotId FROM main.offline_world_bots)').fetchone()[0]
        if members and found < members * 0.9:
            problems.append(f'Only {found} of the {members} guilded bots in the old save are in the new one. Is --from the '
                            "save the game's transfer copied from? Run that transfer first.")
    if 'offline_bot_guilds' in old and 'Guild' in new:
        guilds = count(db, 'offline_bot_guilds', 'old')
        found = db.execute('SELECT count(*) FROM old.offline_bot_guilds b WHERE b.GuildId IN (SELECT GuildID FROM main.Guild)').fetchone()[0]
        if guilds and found < guilds:
            notes.append(f'{guilds - found} of the old save\'s {guilds} bot guilds are not in the new save; the server drops them at start')
    if 'offline_bot_guilds' in new and count(db, 'offline_bot_guilds') > 0:
        old_ids = {row[0] for row in db.execute('SELECT GuildId FROM old.offline_bot_guilds')} if 'offline_bot_guilds' in old else set()
        own = [row[0] for row in db.execute('SELECT GuildId FROM main.offline_bot_guilds') if row[0] not in old_ids]
        if own:
            problems.append(f'The new save already formed {len(own)} bot guild(s) of its own. Carry the old ones into a save '
                            'that has not run with bot guilds on, or remove those first: '
                            'python tools\\claude-version\\set_bot_guilds.py --purge --apply')
    return rows, problems, notes


def copy(db, table):
    """Replaces main.table with old.table (creating it like the old one if the new save lacks it)."""
    if table not in tables(db, 'main'):
        sql = db.execute("SELECT sql FROM old.sqlite_master WHERE type='table' AND name=?", (table,)).fetchone()[0]
        db.execute(sql)
        for (index_sql,) in db.execute("SELECT sql FROM old.sqlite_master WHERE type='index' AND tbl_name=? AND sql IS NOT NULL",
                                       (table,)).fetchall():
            db.execute(index_sql)
    shared = [c for c in columns(db, table, 'old') if c in set(columns(db, table, 'main'))]
    names = ','.join(f'"{c}"' for c in shared)
    db.execute(f'DELETE FROM main."{table}"')
    db.execute(f'INSERT INTO main."{table}" ({names}) SELECT {names} FROM old."{table}"')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--from', dest='source', required=True, help='the old game folder (or its opendaoc.sqlite3.db)')
    parser.add_argument('--db', type=Path, default=ROOT / DB_IN_GAME, help='the new database (default: this game folder)')
    parser.add_argument('--apply', action='store_true', help='copy (without it, only show what would be copied)')
    args = parser.parse_args(argv)
    source, target = database(args.source), args.db
    if not source.exists():
        raise SystemExit(f'No old save at {source}')
    if not target.exists():
        raise SystemExit(f'No new save at {target}')
    if source.resolve() == target.resolve():
        raise SystemExit('--from is this game folder; give the OLD game folder.')

    with contextlib.closing(sqlite3.connect(target.as_uri() + ('' if args.apply else '?mode=ro'), uri=True)) as db:
        db.execute('ATTACH DATABASE ? AS old', (source.as_uri() + '?mode=ro',))
        rows, problems, notes = plan(db)
        print(f'From {source}\nInto {target}')
        for table, old_rows, new_rows in rows:
            print(f'  {table}: {old_rows} rows' + ('' if new_rows is None else f' (replacing {new_rows})'))
        for note in notes:
            print('  note:', note)
        for problem in problems:
            print('  STOP:', problem)
        if problems:
            return 1
        if not args.apply:
            print('Dry run: nothing was changed. Add --apply to copy.')
            return 0
        for path in (source, target):
            reason = running_reason(path)
            if reason:
                print('STOP:', reason)
                return 1

        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        backup = target.parent.parent / 'server/deployment-backups' / ('carry-social-' + stamp)
        suffix = 1
        while backup.exists():
            suffix += 1
            backup = backup.with_name(f'carry-social-{stamp}-{suffix}')
        backup.mkdir(parents=True)
        with contextlib.closing(sqlite3.connect(backup / 'opendaoc.sqlite3.db')) as copy_db:
            db.backup(copy_db, name='main')
        db.execute('BEGIN IMMEDIATE')
        try:
            rows, problems, _ = plan(db)   # again, inside the write lock
            if problems:
                raise RuntimeError(problems[0])
            for table, old_rows, _ in rows:
                copy(db, table)
                if count(db, table) != old_rows:
                    raise RuntimeError(f'{table}: copied {count(db, table)} rows, expected {old_rows}')
            db.execute('COMMIT')
        except Exception:
            db.execute('ROLLBACK')
            raise
    print('Copied. Backup of the new save:', backup)
    print('Start the server: bot guilds, bot memory and the rest are as they were in the old save.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
