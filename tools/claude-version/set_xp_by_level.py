"""Set or clear XP rates by level range (xp_rate_by_level, bot_xp_rate_by_level). Default is a read-only dry run.

  python tools\\claude-version\\set_xp_by_level.py                                    (show the current settings)
  python tools\\claude-version\\set_xp_by_level.py --player "1-19:3;20-34:5;35-50:12"  (dry run)
  python tools\\claude-version\\set_xp_by_level.py --bots "1-19:1.5;20-34:2.5;35-50:6" --apply
  python tools\\claude-version\\set_xp_by_level.py --off --apply                       (back to the flat rates)

Needs a server build that has the two settings (LevelRangeXpRates.cs); older servers ignore them.
Inside a listed range its rate replaces xp_rate (you) or bot_xp_rate (autonomous gamebots); levels
outside every range keep the flat rate. Your XP in RvR zones keeps rvr_zones_xp_rate; bots in RvR
zones are still multiplied by it. Companion bots are not affected.
--apply needs the server, launcher and game closed; it backs up the database first.
"""
import argparse
import datetime
import math
import os
import re
import sqlite3
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]  # <playable>/tools/claude-version/
DB = ROOT / 'runtime/data/opendaoc.sqlite3.db'
KEYS = {'player': 'xp_rate_by_level', 'bots': 'bot_xp_rate_by_level'}
DESCRIPTIONS = {
    'xp_rate_by_level': 'Optional XP rate by level range for real player characters (set by set_xp_by_level.py).',
    'bot_xp_rate_by_level': 'Optional XP rate by level range for autonomous gamebots (set by set_xp_by_level.py).',
}
MAXIMUM_RATE = 1000.0
# ASCII only, as the server's int.TryParse / double.TryParse with the invariant culture: Python's own
# int() and float() would also accept "3_0" or non-ASCII digits, which the server rejects.
LEVEL = re.compile(r'[0-9]+')
RATE = re.compile(r'[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?')


def parse(spec):
    """The same rules as LevelRangeXpRates.TryParse on the server. Returns [(first, last, rate)]."""
    ranges = []
    for entry in (e.strip() for e in spec.split(';')):
        if not entry:
            continue
        if entry.count(':') != 1 or entry.index(':') == 0:
            raise ValueError(f'"{entry}" is not in the form first-last:rate')
        levels, rate_text = (part.strip() for part in entry.split(':'))
        bounds = [b.strip() for b in levels.split('-', 1)] if '-' in levels else [levels, levels]
        if not all(LEVEL.fullmatch(b) and 1 <= int(b) <= 255 for b in bounds):
            raise ValueError(f'"{levels}" is not a level range')
        first, last = int(bounds[0]), int(bounds[1])
        if last < first:
            raise ValueError(f'"{levels}" ends before it starts')
        rate = float(rate_text) if RATE.fullmatch(rate_text) else float('nan')
        if not math.isfinite(rate) or rate <= 0 or rate > MAXIMUM_RATE:
            raise ValueError(f'"{rate_text}" is not a rate above 0 and at most {MAXIMUM_RATE:g}')
        for a, b, _ in ranges:
            if first <= b and last >= a:
                raise ValueError(f'{first}-{last} overlaps {a}-{b}')
        ranges.append((first, last, rate))
    if not ranges:
        raise ValueError('no ranges were given')
    return ranges


def current(db):
    rows = dict(db.execute("SELECT `Key`, Value FROM ServerProperty WHERE `Key` IN (?, ?, 'xp_rate', 'bot_xp_rate')",
                           (*KEYS.values(),)).fetchall())
    return {k: rows.get(k) for k in ('xp_rate', 'xp_rate_by_level', 'bot_xp_rate', 'bot_xp_rate_by_level')}


def write(db, values):
    for key, value in values.items():
        db.execute("""INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID)
                      VALUES ('rates', ?, ?, '', ?, ?, ?)
                      ON CONFLICT(`Key`) DO UPDATE SET Value=excluded.Value, LastTimeRowUpdated=excluded.LastTimeRowUpdated""",
                   # ServerProperty_ID is unique: an empty ID is already taken, so use the key, as other rows do
                   (key, DESCRIPTIONS[key], value, datetime.datetime.now(datetime.timezone.utc).isoformat(), key))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--player', help='ranges for your characters, e.g. "1-19:3;20-34:5;35-50:12"')
    parser.add_argument('--bots', help='ranges for autonomous gamebots, e.g. "1-19:1.5;20-34:2.5;35-50:6"')
    parser.add_argument('--off', action='store_true', help='clear both settings (flat rates again)')
    parser.add_argument('--apply', action='store_true', help='write the change (server, launcher and game closed)')
    parser.add_argument('--db', type=Path, default=DB, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.off and (args.player or args.bots):
        parser.error('--off cannot be combined with --player or --bots')

    values = {}
    if args.off:
        values = {key: '' for key in KEYS.values()}
    for who in ('player', 'bots'):
        spec = getattr(args, who)
        if spec is not None:
            ranges = parse(spec)
            values[KEYS[who]] = ';'.join(f'{a}-{b}:{r:.15g}' for a, b, r in ranges)

    with sqlite3.connect(args.db.as_uri() + '?mode=ro', uri=True) as source:
        before = current(source)
    for key, value in before.items():
        print(f'{key:22s} now: {value if value not in (None, "") else "(not set)"}')
    if not values:
        print('Nothing to change. Use --player, --bots or --off.')
        return
    for key, value in values.items():
        print(f'{key:22s} new: {value or "(not set: flat rate)"}')
    if not args.apply:
        print('Dry run only; the database was not changed. Add --apply to write it.')
        return

    if os.name == 'nt':
        running = subprocess.check_output(['powershell', '-NoProfile', '-Command',
            'Get-Process CoreServer,DOLServer,OfflineDAoC,game -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName; exit 0'], text=True)
        if running.strip():
            raise RuntimeError('Close the server, launcher and game first: ' + running.strip())
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup = args.db.parent.parent / 'server/deployment-backups' / ('xp-by-level-' + stamp)
    backup.mkdir(parents=True, exist_ok=False)
    with sqlite3.connect(args.db) as db:
        with sqlite3.connect(backup / 'opendaoc.sqlite3.db') as copy:
            db.backup(copy)
        db.execute('BEGIN IMMEDIATE')
        write(db, values)
        if db.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
            raise RuntimeError('Integrity check failed; rolling back')
        db.commit()
        after = current(db)
    for key, value in values.items():
        if (after[key] or '') != value:
            raise RuntimeError(f'{key} reads back as {after[key]!r}, expected {value!r}')
    print('Applied. Backup:', backup)


if __name__ == '__main__':
    try:
        main()
    except ValueError as error:
        raise SystemExit(f'Not changed: {error}')
