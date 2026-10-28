"""The epic chains in order: quest-ID links for the Guild of Shadows (7 to 48) and every other guild line, with the
Guild of Shadows' period dialogue. From lometur/HearthDAoC (deploy/bin/epic_chains.py, quest_dialogue.py).

The epic chains reuse quest names (the Guild of Shadows 25 and 30 are both "Regal Nobility"; 43, 45 and 48 are all
"Lord of Deceit"), so a name dependency can't say which step comes first: 30 could be skipped and 45 and 48 taken in
either order. The server (0.37 Living Server build or later) reads QuestDependency entries by quest ID: "#20188" (that
quest finished), "#20157/20469" (one of them), "!#20478" (not while that quest is active or finished). This writes them:
  1. links      the Guild of Shadows' 60 chain rows: each step needs the one before; the Camelot and Shrouded Isles
                versions of 7 and of 11 close each other.
  2. pins       every other classic line: a name dependency that is the step's own name, or that names quests at more
                than one lower level, is pinned to the IDs of those quests at the highest level below (87 rows on the
                clean world: Defenders, Church, Academy, Midgard and Hibernia lines).
  3. closed     Lady Aelawen's second Supply Run (4 rows) is offered to no one; a character on it can still finish it.
  4. rewards    the Necromancer 11: one reward for each version.
  5. texts      the level-30 speech's leftover source tags ("(prof name)", "(class name)").
  6. dialogue   period texts for 7 SI, 11 SI, 15, 20, 25, 30 and 43 (upstream left "[Traveler's]"-style stubs).

Only world rows (DataQuest) change, never a save: finished and active quests stay as they are, and the links apply only
to new offers. Each row changes only while it still holds what the clean world has (or, for dialogue, a known earlier
text), so your own edits stay. Every change is written to the ledger table offline_epic_chains_ledger in the same
database, and `rollback` puts each value back where it still holds what this wrote.

  python tools\\claude-version\\epic_chains.py             (show what would change)
  python tools\\claude-version\\epic_chains.py --apply     (server, launcher and game closed; backs up first)
  python tools\\claude-version\\epic_chains.py rollback --apply
"""
import argparse
import contextlib
import datetime
import hashlib
import json
import os
import re
import sqlite3
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]  # <playable>/tools/claude-version/
DB = ROOT / 'runtime' / 'data' / 'opendaoc.sqlite3.db'
DATA_FILE = Path(__file__).resolve().with_name('epic_chains_data.json')
LEDGER = 'offline_epic_chains_ledger'
CLASSIC = 'DOL.GS.Quests.ClassicQuestStep'
TEXT_COLUMNS = ('StepText', 'SourceText', 'TargetText', 'Description', 'FinishText')
DIALOGUE_COLUMNS = ('AcceptText', 'Description', 'SourceText', 'StepType', 'StepText', 'TargetText', 'AdvanceText',
                    'FinishText', 'StepItemTemplates', 'CollectItemTemplate', 'StartName', 'QuestDependency')
PER_STAGE = ('SourceText', 'StepType', 'StepText', 'TargetText', 'AdvanceText', 'StepItemTemplates', 'CollectItemTemplate')
PIPED = ('QuestDependency',)


def load_data(path=DATA_FILE):
    with open(path, encoding='utf-8') as f:
        return json.load(f)


class Changes:
    """The row values to write, in order: (quest id, column, old, new, step). A later change of the same cell replaces
    the new value and keeps the first old one, so the ledger always holds what the world had."""

    def __init__(self, db):
        self.db, self.cells, self.order, self.counts = db, {}, [], {}

    def value(self, qid, column):
        if (qid, column) in self.cells:
            return self.cells[(qid, column)][1]
        row = self.db.execute(f'SELECT "{column}" FROM DataQuest WHERE ID=?', (qid,)).fetchone()
        return None if row is None else row[0]

    def set(self, qid, column, new, step):
        old = self.value(qid, column)
        if (qid, column) in self.cells:
            self.cells[(qid, column)] = (self.cells[(qid, column)][0], new)
        else:
            self.cells[(qid, column)] = (old, new)
            self.order.append((qid, column))
        self.counts.setdefault(step, set()).add(qid)

    def rows(self):
        return [(qid, column, *self.cells[(qid, column)]) for qid, column in self.order
                if (self.cells[(qid, column)][0] or '') != (self.cells[(qid, column)][1] or '')]


def _entry(steps, link, col):
    """A link ("7/7closed/7si", "!11si") as the class's QuestDependency entry ("#21500/21324/20478", "!#20469")."""
    closes = link.startswith('!')
    ids = [steps[key]['ids'][col] for key in link.lstrip('!').split('/')]
    return ('!#' if closes else '#') + '/'.join(str(i) for i in ids if i is not None)


def plan_links(ch, data):
    steps = data['steps']
    for step in steps.values():
        if 'links' not in step:
            continue
        for col, qid in enumerate(step['ids']):
            if qid is not None and (ch.value(qid, 'QuestDependency') or '') == step['upstream_dependency']:
                ch.set(qid, 'QuestDependency', '|'.join(_entry(steps, link, col) for link in step['links']), 'links')


def _classes(allowed):
    return {int(part) for part in re.split(r'[|;,]', allowed or '') if part.strip().isdigit()}


def _shares(mine, theirs):
    return not mine or not theirs or bool(mine & theirs)


def plan_pins(ch, data):
    gos = {qid for step in data['steps'].values() for qid in step['ids'] if qid is not None}
    quests = [r for r in ch.db.execute('SELECT ID, Name, MinLevel, AllowedClasses, IFNULL(QuestDependency,\'\'), ClassType '
                                       'FROM DataQuest') if CLASSIC in (r[5] or '')]
    by_name = {}
    for quest in quests:
        by_name.setdefault(quest[1].lower(), []).append(quest)
    for qid, name, level, allowed, dependency, _ in quests:
        entries = [e for e in dependency.split('|') if e]
        if qid in gos or not entries or any(e.strip().startswith(('#', '!#')) for e in entries):
            continue
        mine, pinned, any_pinned = _classes(allowed), [], False
        for entry in entries:
            before = [q for q in by_name.get(entry.lower(), [])
                      if q[0] != qid and q[2] < level and _shares(mine, _classes(q[3]))]
            levels = {q[2] for q in before}
            if before and (entry.lower() == name.lower() or len(levels) > 1):
                top = max(levels)
                pinned.append('#' + '/'.join(str(q[0]) for q in sorted(before) if q[2] == top))
                any_pinned = True
            else:
                pinned.append(entry)
        if any_pinned:
            ch.set(qid, 'QuestDependency', '|'.join(pinned), 'pins')


def plan_closed(ch, data):
    closed = data['steps']['7closed']
    for qid in closed['ids']:
        if qid is not None and (ch.value(qid, 'QuestDependency') or '') == closed['upstream_dependency']:
            ch.set(qid, 'QuestDependency', f'#{qid}', 'closed')


def plan_rewards(ch, data):
    for fix in data['rewards']:
        if (ch.value(fix['id'], 'FinalRewardItemTemplates') or '') == fix['upstream']:
            ch.set(fix['id'], 'FinalRewardItemTemplates', fix['value'], 'rewards')


def plan_texts(ch, data):
    for fix in data['texts']:
        column = fix['column']
        if column not in TEXT_COLUMNS:
            raise ValueError(f'unknown text column {column}')
        for qid in fix['ids']:
            text = ch.value(qid, column)
            if text is None or not all(old in text for old, _ in fix['replace']):
                continue
            for old, new in fix['replace']:
                text = text.replace(old, new)
            ch.set(qid, column, text, 'texts')


def digest(values):
    """The guard digest of a row's dialogue columns, in sorted column order (None counts as "")."""
    return hashlib.sha256(json.dumps([value or '' for value in values], ensure_ascii=False).encode('utf-8')).hexdigest()


def _per_class(value, classes, what, piped=False):
    if isinstance(value, str):
        if '|' in value and not piped:
            raise ValueError(f"{what}: a value contains '|'")
        return {name: value for name in classes}
    if not isinstance(value, dict) or sorted(value) != sorted(classes):
        raise ValueError(f'{what}: expected a string or an object with every class')
    if any(not isinstance(text, str) or ('|' in text and not piped) for text in value.values()):
        raise ValueError(f"{what}: a per-class value is not a string, or contains '|'")
    return dict(value)


def resolve(column, value, classes, stages):
    """{class: text} for a dialogue column; None for a class whose row has another number of stages."""
    if column not in DIALOGUE_COLUMNS:
        raise ValueError(f'unknown column {column}')
    if not isinstance(value, list):
        if column == 'StepType':
            raise ValueError('StepType: a list with one entry for each stage')
        return _per_class(value, classes, column, column in PIPED)
    if column not in PER_STAGE:
        raise ValueError(f'{column}: a list is only for the per-stage columns')
    entries = [_per_class(entry, classes, column) for entry in value]
    return {name: None if stages[name] is not None and len(value) != stages[name] else
            '|'.join(entry[name] for entry in entries) for name in classes}


def plan_dialogue(ch, data, kept):
    classes = list(data['classes'])
    for entry in data['dialogue']:
        by_class = {c: q for c, q in zip(classes, data['steps'][entry['step']]['ids']) if q is not None}
        columns = sorted(entry['set'])
        stages = {c: (len((ch.value(q, 'StepType') or '').split('|')) if ch.value(q, 'StepType') is not None else None)
                  for c, q in by_class.items()}
        for c in classes:
            stages.setdefault(c, None)
        resolved = {column: resolve(column, entry['set'][column], classes, stages) for column in columns}
        for c, qid in by_class.items():
            if ch.value(qid, 'StepType') is None:
                continue
            values = [resolved[column][c] for column in columns]
            held = [ch.value(qid, column) or '' for column in columns]
            if None in values or (held != values and digest(held) not in set(entry['guard'].get(str(qid), []))):
                kept.append(qid)
                continue
            for column, value in zip(columns, values):
                ch.set(qid, column, value, 'dialogue')


PLAN = (plan_links, plan_pins, plan_closed, plan_rewards, plan_texts)


def plan(db, data):
    """(changes, dialogue rows left alone). Nothing is written."""
    ch, kept = Changes(db), []
    for step in PLAN:
        step(ch, data)
    plan_dialogue(ch, data, kept)
    return ch, kept


def has_table(db, name):
    return db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (name,)).fetchone() is not None


def applied(db):
    return has_table(db, LEDGER) and db.execute(f'SELECT count(*) FROM {LEDGER}').fetchone()[0] > 0


def write(db, ch, now):
    db.execute(f'CREATE TABLE IF NOT EXISTS {LEDGER} (QuestId INTEGER NOT NULL, ColumnName TEXT NOT NULL, '
               'OldValue TEXT, NewValue TEXT, Step TEXT NOT NULL, AppliedUtc TEXT NOT NULL, PRIMARY KEY (QuestId, ColumnName))')
    rows = ch.rows()
    for qid, column, old, new in rows:
        db.execute(f'UPDATE DataQuest SET "{column}"=?, LastTimeRowUpdated=? WHERE ID=?', (new, now, qid))
        step = next(s for s, ids in ch.counts.items() if qid in ids)
        db.execute(f'INSERT INTO {LEDGER} VALUES (?, ?, ?, ?, ?, ?)', (qid, column, old, new, step, now))
    return len(rows)


def roll_back(db, now):
    """Puts back every ledger value whose cell still holds what this wrote; returns (restored, left as they are)."""
    restored, kept = 0, []
    for qid, column, old, new in db.execute(f'SELECT QuestId, ColumnName, OldValue, NewValue FROM {LEDGER}').fetchall():
        row = db.execute(f'SELECT "{column}" FROM DataQuest WHERE ID=?', (qid,)).fetchone()
        if row is not None and (row[0] or '') == (new or ''):
            db.execute(f'UPDATE DataQuest SET "{column}"=?, LastTimeRowUpdated=? WHERE ID=?', (old, now, qid))
            restored += 1
        else:
            kept.append(f'{qid}.{column}')
    db.execute(f'DROP TABLE {LEDGER}')
    return restored, kept


def running_reason(db_path):
    """Why the server looks open, or None (as set_bot_guilds.py)."""
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


def backup(db, db_path, action):
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    folder = db_path.parent.parent / 'server' / 'deployment-backups' / f'epic-chains-{action}-{stamp}'
    folder.mkdir(parents=True, exist_ok=False)
    with contextlib.closing(sqlite3.connect(folder / 'opendaoc.sqlite3.db')) as copy:
        db.backup(copy)
    return folder


def summary(ch, kept):
    names = ('links', 'pins', 'closed', 'rewards', 'texts', 'dialogue')
    line = ', '.join(f'{name} {len(ch.counts.get(name, ()))}' for name in names)
    return f'Quests to change: {line}' + (f'; dialogue left as it is (text changed since): {sorted(set(kept))}' if kept else '')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('action', nargs='?', choices=['apply', 'rollback'], default='apply')
    parser.add_argument('--apply', action='store_true', help='write (server, launcher and game closed)')
    parser.add_argument('--db', type=Path, default=DB, help='the database (default: this game folder)')
    args = parser.parse_args(argv)
    if not args.db.exists():
        raise SystemExit(f'No database at {args.db}')
    data = load_data()
    now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d %H:%M:%S')
    with contextlib.closing(sqlite3.connect(args.db.resolve().as_uri() + ('' if args.apply else '?mode=ro'), uri=True)) as db:
        if args.action == 'rollback':
            if not applied(db):
                print('Nothing to roll back: the epic chains were not applied to this database.')
                return 0
            count = db.execute(f'SELECT count(*) FROM {LEDGER}').fetchone()[0]
            print(f'{count} values to put back.')
        else:
            if applied(db):
                print('Already applied (see the table offline_epic_chains_ledger). Roll back first to apply again.')
                return 0
            ch, kept = plan(db, data)
            print(summary(ch, kept))
            if not ch.rows():
                print('Nothing to change.')
                return 0
        if not args.apply:
            print('Dry run: nothing was changed. Add --apply to write it.')
            return 0
        reason = running_reason(args.db)
        if reason:
            raise SystemExit(reason)
        folder = backup(db, args.db, args.action)
        db.execute('BEGIN IMMEDIATE')
        try:
            if args.action == 'rollback':
                restored, left = roll_back(db, now)
                message = f'Put back {restored} values' + (f'; left as they are (changed since): {", ".join(left)}' if left else '')
            else:
                ch, kept = plan(db, data)   # again, inside the write lock
                message = f'Wrote {write(db, ch, now)} values. {summary(ch, kept)}'
            if db.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
                raise RuntimeError('integrity check failed; rolling back')
            db.commit()
        except BaseException:
            db.rollback()
            raise
    print(message)
    print('Backup:', folder)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
