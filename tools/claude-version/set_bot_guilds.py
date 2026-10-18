"""Turn bot guilds on or off (docs/BOT-GUILDS.md). Default is a read-only dry run.

  python tools\\claude-version\\set_bot_guilds.py                       (show the current settings)
  python tools\\claude-version\\set_bot_guilds.py --on --apply          (turn bot guilds on)
  python tools\\claude-version\\set_bot_guilds.py --share 0.6 --apply   (60% of eligible bots in guilds)
  python tools\\claude-version\\set_bot_guilds.py --off --apply         (every gamebot guildless again)
  python tools\\claude-version\\set_bot_guilds.py --claims off --apply  (one part off; the others stay on)

Parts, each on by default and only active while bot guilds are on:
  --social     guild chat, greetings, replies to your /gu, recruiting in /faction
  --recruit    bot officers invite you to their guild (yes/no dialog)
  --claims     bot guilds claim keeps their realm takes, and defend them first
  --economy    dues from real loot, merit points, guild buffs
  --alliances  each realm's RvR and raiding bot guilds form one alliance

Needs a server build with AutonomousBotGuilds.cs; older servers ignore these settings.
Turning it off keeps the saved guilds and memberships; turning it on again restores them.
--apply needs the server, launcher and game closed; it backs up the database first.

Removing bot guilds for good (docs/BOT-GUILDS.md, "Removing bot guilds"):
  python tools\\claude-version\\set_bot_guilds.py --purge            (show what would be removed)
  python tools\\claude-version\\set_bot_guilds.py --purge --apply    (remove it)

--purge turns bot guilds off, releases the keeps bot guilds claimed, ends their alliances, moves
your characters in a bot guild to their realm's starting guild, and deletes the bot guilds with
their ranks, banks and memberships. Your own guilds stay, and so do bots you invited into them.
It works on any server build, also after going back to a build without bot guilds. Running it
again changes nothing.
"""
import argparse
import contextlib
import datetime
import os
import re
import sqlite3
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]  # <playable>/tools/claude-version/
DB = ROOT / 'runtime/data/opendaoc.sqlite3.db'
DESCRIPTIONS = {
    'bot_guilds_enabled': 'Autonomous gamebots form and join realm guilds (see docs/BOT-GUILDS.md), and players can '
                          '/ginvite them. False keeps every gamebot guildless; saved memberships are kept.',
    'bot_guild_share': 'Share (0 to 1) of eligible autonomous gamebots that bot guild formation places in guilds.',
    'bot_guild_min_level': 'Lowest level at which an autonomous gamebot is placed in a bot guild.',
    'bot_guild_social': 'When bot guilds are on: guild members greet, congratulate and chat in guild chat when a real '
                        'member can hear, answer your /gu, and bot guild officers recruit in /faction.',
    'bot_guild_recruit_player': 'When bot guild social life is on: bot guild officers may invite you to their guild with '
                                'a yes/no dialog (when you ask for a guild, or now and then when you are nearby and unguilded).',
    'bot_guild_claims_keeps': 'When bot guilds are on: after a realm takes a keep or tower, a bot guild with enough members '
                              'inside claims it (claim_num, half for towers; guilds_claim_limit), never while a real player '
                              'of that realm is inside. Its warbands answer first when that keep is attacked.',
    'bot_guild_economy': 'When bot guilds are on: guilded gamebots pay their guild\'s dues from the real coin they loot, '
                         'their level-ups earn the guild merit points as a player\'s do, and bot guild leaders spend 1,000 '
                         'merit points on a 24-hour guild buff.',
    'bot_guild_alliances': 'When bot guilds are on: each realm\'s RvR and raiding bot guilds form one alliance (up to four '
                           'guilds); a player in one of them hears its alliance chat. Alliances with a player\'s guild are '
                           'never touched.',
}
PARTS = {'social': 'bot_guild_social', 'recruit': 'bot_guild_recruit_player', 'claims': 'bot_guild_claims_keeps',
         'economy': 'bot_guild_economy', 'alliances': 'bot_guild_alliances'}
NUMBER = re.compile(r'(0(\.[0-9]+)?|1(\.0+)?)')
LEVEL = re.compile(r'[0-9]{1,2}')


def current(db):
    rows = dict(db.execute(f"SELECT `Key`, Value FROM ServerProperty WHERE `Key` IN ({','.join('?' * len(DESCRIPTIONS))})",
                           tuple(DESCRIPTIONS)).fetchall())
    return {key: rows.get(key) for key in DESCRIPTIONS}


def write(db, values):
    for key, value in values.items():
        db.execute("""INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID)
                      VALUES ('guild', ?, ?, '', ?, ?, ?)
                      ON CONFLICT(`Key`) DO UPDATE SET Value=excluded.Value, LastTimeRowUpdated=excluded.LastTimeRowUpdated""",
                   (key, DESCRIPTIONS[key], value, datetime.datetime.now(datetime.timezone.utc).isoformat(), key))


# --purge -----------------------------------------------------------------------------------------
# What the server does, mirrored here (verified in the server code):
#   * a released keep: AbstractGameKeep.Release() sets no guild (Keep.ClaimedGuildName '') and
#     ChangeLevel(starting_keep_level); the release builder blanks ClaimedGuildName the same way.
#   * an alliance whose leader guild leaves is disbanded: Alliance.RemoveGuild() blanks every member
#     guild's AllianceID ('') and deletes the GuildAlliance row.
#   * a new character joins its realm's starting guild (IsStartingGuild) at rank 8
#     (StartupGuilds.AddNewbieToStarterGuild), only while starting_guild is on; leaving a guild
#     blanks GuildID and GuildNote (Guild.RemovePlayer).
#   * GuildMgr.DeleteGuild deletes the Guild row (its bank is a column of it) and its GuildRank rows.
START_RANK = 8
DEFAULT_STARTING_KEEP_LEVEL = 4   # ServerProperties.STARTING_KEEP_LEVEL default


@dataclass
class PurgePlan:
    guild_ids: list = field(default_factory=list)        # every offline_bot_guilds id (its Guild row may already be gone)
    guilds: list = field(default_factory=list)           # (GuildID, GuildName, Realm, Charter) of existing bot Guild rows
    starting: list = field(default_factory=list)         # starting guilds marked as bot guilds: --purge refuses
    ranks: int = 0
    members: int = 0
    keeps: list = field(default_factory=list)            # (KeepID, Name, Level)
    keep_level: int = DEFAULT_STARTING_KEEP_LEVEL
    alliances: list = field(default_factory=list)        # (GuildAlliance_ID, AllianceName)
    allied_guilds: list = field(default_factory=list)    # (GuildID, GuildName) of other guilds in those alliances
    characters: list = field(default_factory=list)       # (DOLCharacters_ID, Name, Realm, new GuildID, new guild name)
    houses: list = field(default_factory=list)           # (HouseNumber, GuildName): reported only

    def empty(self):
        return not (self.guild_ids or self.ranks or self.members or self.keeps or self.alliances or self.characters)


def has_table(db, name):
    return db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (name,)).fetchone() is not None


def marks(values):
    return ','.join('?' * len(values))


def server_property(db, key, default):
    row = db.execute('SELECT Value FROM ServerProperty WHERE `Key`=?', (key,)).fetchone()
    return default if row is None or (row[0] or '').strip() == '' else row[0].strip()


def plan_purge(db):
    """Everything --purge would change, read only. Works on a database of any server build."""
    plan = PurgePlan()
    if not has_table(db, 'offline_bot_guilds'):
        return plan   # a server build without bot guilds never made any
    marked = dict(db.execute('SELECT GuildId, Charter FROM offline_bot_guilds ORDER BY GuildId'))
    ids = plan.guild_ids = list(marked)
    if not ids:
        return plan
    rows = db.execute(f'SELECT GuildID, GuildName, Realm, IsStartingGuild FROM Guild WHERE GuildID IN ({marks(ids)})', ids).fetchall()
    plan.starting = [name for _, name, _, starting in rows if starting]
    plan.guilds = [(gid, name, realm, marked.get(gid, '')) for gid, name, realm, _ in rows]
    names = [name for _, name, _, _ in plan.guilds if name]

    plan.ranks = db.execute(f'SELECT COUNT(*) FROM GuildRank WHERE GuildID IN ({marks(ids)})', ids).fetchone()[0]
    if has_table(db, 'offline_bot_guild_members'):
        plan.members = db.execute(f'SELECT COUNT(*) FROM offline_bot_guild_members WHERE GuildId IN ({marks(ids)})',
                                  ids).fetchone()[0]
    try:
        plan.keep_level = int(server_property(db, 'starting_keep_level', DEFAULT_STARTING_KEEP_LEVEL))
    except ValueError:
        plan.keep_level = DEFAULT_STARTING_KEEP_LEVEL
    if names:
        # The server finds a keep's guild by exact name (GuildMgr.GetGuildByName), so compare exactly.
        plan.keeps = db.execute(f'SELECT KeepID, Name, Level FROM Keep WHERE ClaimedGuildName COLLATE BINARY IN ({marks(names)}) '
                                'ORDER BY KeepID', names).fetchall()
        if has_table(db, 'DBHouse'):
            plan.houses = db.execute(f'SELECT HouseNumber, GuildName FROM DBHouse WHERE GuildName COLLATE BINARY IN ({marks(names)})',
                                     names).fetchall()
    plan.alliances = db.execute(f'SELECT GuildAlliance_ID, AllianceName FROM GuildAlliance WHERE LeaderGuildID IN ({marks(ids)}) '
                                'ORDER BY AllianceName', ids).fetchall()
    alliance_ids = [aid for aid, _ in plan.alliances]
    if alliance_ids:
        plan.allied_guilds = db.execute(f'SELECT GuildID, GuildName FROM Guild WHERE AllianceID IN ({marks(alliance_ids)}) '
                                        f'AND GuildID NOT IN ({marks(ids)}) ORDER BY GuildName', alliance_ids + ids).fetchall()

    starting_on = server_property(db, 'starting_guild', 'True').lower() == 'true'
    starting = {}
    if starting_on:
        for gid, name, realm in db.execute('SELECT GuildID, GuildName, Realm FROM Guild WHERE IsStartingGuild=1 ORDER BY Guild_ID'):
            starting.setdefault(realm, (gid, name))
    for cid, name, realm in db.execute(f'SELECT DOLCharacters_ID, Name, Realm FROM DOLCharacters WHERE GuildID IN ({marks(ids)}) '
                                       'ORDER BY Name', ids):
        gid, gname = starting.get(realm, ('', ''))
        plan.characters.append((cid, name, realm, gid, gname))
    return plan


def apply_purge(db, plan):
    """Writes the plan. The caller holds the write transaction and commits."""
    ids = plan.guild_ids
    counts = {'keeps released': 0, 'alliances ended': 0, 'guilds left an alliance': 0, 'characters moved': 0,
              'guild ranks deleted': 0, 'guilds deleted': 0, 'memberships deleted': 0, 'bot guild marks deleted': 0}
    if plan.keeps:
        keep_ids = [keep_id for keep_id, _, _ in plan.keeps]
        counts['keeps released'] = db.execute(f"UPDATE Keep SET ClaimedGuildName='', Level=? WHERE KeepID IN ({marks(keep_ids)})",
                                              [plan.keep_level] + keep_ids).rowcount
    if plan.alliances:
        alliance_ids = [aid for aid, _ in plan.alliances]
        counts['guilds left an alliance'] = db.execute(
            f"UPDATE Guild SET AllianceID='' WHERE AllianceID IN ({marks(alliance_ids)}) AND GuildID NOT IN ({marks(ids)})",
            alliance_ids + ids).rowcount
        db.execute(f"UPDATE Guild SET AllianceID='' WHERE AllianceID IN ({marks(alliance_ids)})", alliance_ids)
        counts['alliances ended'] = db.execute(f'DELETE FROM GuildAlliance WHERE GuildAlliance_ID IN ({marks(alliance_ids)})',
                                               alliance_ids).rowcount
    for cid, _, _, gid, _ in plan.characters:
        if gid:
            changed = db.execute("UPDATE DOLCharacters SET GuildID=?, GuildRank=?, GuildNote='' WHERE DOLCharacters_ID=?",
                                 (gid, START_RANK, cid)).rowcount
        else:
            changed = db.execute("UPDATE DOLCharacters SET GuildID='', GuildNote='' WHERE DOLCharacters_ID=?", (cid,)).rowcount
        counts['characters moved'] += changed
    if ids:
        counts['guild ranks deleted'] = db.execute(f'DELETE FROM GuildRank WHERE GuildID IN ({marks(ids)})', ids).rowcount
        counts['guilds deleted'] = db.execute(f'DELETE FROM Guild WHERE GuildID IN ({marks(ids)}) AND IsStartingGuild=0',
                                              ids).rowcount
        if has_table(db, 'offline_bot_guild_members'):
            counts['memberships deleted'] = db.execute(f'DELETE FROM offline_bot_guild_members WHERE GuildId IN ({marks(ids)})',
                                                       ids).rowcount
        counts['bot guild marks deleted'] = db.execute(f'DELETE FROM offline_bot_guilds WHERE GuildId IN ({marks(ids)})',
                                                       ids).rowcount
    return counts


def verify_purged(db, plan):
    """What of the plan is still in the database (empty when the purge took)."""
    ids, names = plan.guild_ids, [name for _, name, _, _ in plan.guilds if name]
    alliance_ids = [aid for aid, _ in plan.alliances]
    checks = []
    if ids:
        checks += [('bot guild marks', 'offline_bot_guilds', 'GuildId', ids), ('guilds', 'Guild', 'GuildID', ids),
                   ('guild ranks', 'GuildRank', 'GuildID', ids), ('characters', 'DOLCharacters', 'GuildID', ids)]
        if has_table(db, 'offline_bot_guild_members'):
            checks.append(('memberships', 'offline_bot_guild_members', 'GuildId', ids))
    if names:
        checks.append(('keep claims', 'Keep', 'ClaimedGuildName COLLATE BINARY', names))
    if alliance_ids:
        checks += [('alliances', 'GuildAlliance', 'GuildAlliance_ID', alliance_ids),
                   ('guilds in an ended alliance', 'Guild', 'AllianceID', alliance_ids)]
    left = [f'{label}: {count}' for label, table, column, values in checks
            for count in [db.execute(f'SELECT COUNT(*) FROM {table} WHERE {column} IN ({marks(values)})', values).fetchone()[0]]
            if count]
    if plan.keeps:
        keep_ids = [keep_id for keep_id, _, _ in plan.keeps]
        wrong = db.execute(f"SELECT COUNT(*) FROM Keep WHERE KeepID IN ({marks(keep_ids)}) "
                           f"AND (COALESCE(ClaimedGuildName,'')<>'' OR Level<>?)", keep_ids + [plan.keep_level]).fetchone()[0]
        if wrong:
            left.append(f'keeps not released: {wrong}')
    return left


def show_plan(plan):
    realms = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}
    print(f'Bot guilds to delete: {len(plan.guilds)}' + (f' (and {len(plan.guild_ids) - len(plan.guilds)} '
          'leftover bot guild mark(s) whose guild is already gone)' if len(plan.guild_ids) > len(plan.guilds) else ''))
    for _, name, realm, charter in sorted(plan.guilds, key=lambda g: (g[2], g[1] or '')):
        print(f'  {realms.get(realm, realm)}: {name} ({charter or "no charter"})')
    print(f'Guild ranks to delete: {plan.ranks}')
    print(f'Bot memberships to delete: {plan.members}')
    print(f'Keeps to release (claim removed, level {plan.keep_level}; the realm keeps them): {len(plan.keeps)}')
    for _, name, level in plan.keeps:
        print(f'  {name} (now level {level})')
    print(f'Bot guild alliances to end: {len(plan.alliances)}')
    for _, name in plan.alliances:
        print(f'  {name}')
    for _, name in plan.allied_guilds:
        print(f'  your guild "{name}" leaves its alliance with bot guilds')
    print(f'Characters to move out of a bot guild: {len(plan.characters)}')
    for _, name, _, _, gname in plan.characters:
        print(f'  {name} -> ' + (f'{gname} (rank {START_RANK})' if gname else 'no guild (no starting guild for the realm)'))
    for number, name in plan.houses:
        print(f'Note: house {number} names bot guild "{name}"; houses are not changed.')


def running_reason(db_path):
    """Why the server looks open, or None."""
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


def purge(args):
    with contextlib.closing(sqlite3.connect(args.db.as_uri() + '?mode=ro', uri=True)) as source:
        enabled = current(source)['bot_guilds_enabled']
        plan = plan_purge(source)
    print(f'bot_guilds_enabled         now: {enabled if enabled not in (None, "") else "(not set: server default)"}')
    print('bot_guilds_enabled         new: False')
    show_plan(plan)
    if plan.starting:
        raise RuntimeError('starting guild(s) ' + ', '.join(f'"{name}"' for name in plan.starting) +
                           ' are marked as bot guilds. That is not expected; nothing was changed.')
    if plan.empty() and enabled == 'False':
        print('Nothing to remove: bot guilds are off and no bot guild is left.')
        return
    if not args.apply:
        print('Dry run only; the database was not changed. Add --apply to write it.')
        return

    reason = running_reason(args.db)
    if reason:
        raise RuntimeError(reason)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup = args.db.parent.parent / 'server/deployment-backups' / ('bot-guilds-purge-' + stamp)
    backup.mkdir(parents=True, exist_ok=False)
    with contextlib.closing(sqlite3.connect(args.db)) as db:
        with contextlib.closing(sqlite3.connect(backup / 'opendaoc.sqlite3.db')) as copy:
            db.backup(copy)
        db.execute('BEGIN IMMEDIATE')
        try:
            plan = plan_purge(db)   # again, inside the write lock
            if plan.starting:
                raise RuntimeError('a starting guild is marked as a bot guild; nothing was changed')
            write(db, {'bot_guilds_enabled': 'False'})
            counts = apply_purge(db, plan)
            if db.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
                raise RuntimeError('Integrity check failed; rolling back')
            left = verify_purged(db, plan) + (['bot guild marks: some'] if not plan_purge(db).empty() else [])
            if left or current(db)['bot_guilds_enabled'] != 'False':
                raise RuntimeError('the purge did not take (' + ', '.join(left or ['bot_guilds_enabled']) + '); rolling back')
            db.commit()
        except BaseException:
            db.rollback()
            raise
        after = current(db)['bot_guilds_enabled']
    if after != 'False':
        raise RuntimeError(f'bot_guilds_enabled reads back as {after!r}, expected \'False\'')
    for name, count in counts.items():
        print(f'{name:26s} {count}')
    print('Applied. Backup:', backup)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    switch = parser.add_mutually_exclusive_group()
    switch.add_argument('--on', action='store_true', help='turn bot guilds on')
    switch.add_argument('--off', action='store_true', help='turn bot guilds off (saved guilds are kept)')
    switch.add_argument('--purge', action='store_true',
                        help='turn bot guilds off and delete them: keep claims, alliances, ranks, memberships')
    parser.add_argument('--share', help='share of eligible bots in guilds, 0 to 1 (default 0.7)')
    parser.add_argument('--min-level', help='lowest level that joins a bot guild, 1 to 50 (default 5)')
    for part in PARTS:
        parser.add_argument(f'--{part}', choices=['on', 'off'], help=f'turn the {part} part on or off')
    parser.add_argument('--apply', action='store_true', help='write the change (server, launcher and game closed)')
    parser.add_argument('--db', type=Path, default=DB, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.purge:
        if args.share is not None or args.min_level is not None or any(getattr(args, part) for part in PARTS):
            raise ValueError('--purge takes only --apply')
        purge(args)
        return

    values = {}
    if args.on or args.off:
        values['bot_guilds_enabled'] = 'True' if args.on else 'False'
    if args.share is not None:
        if not NUMBER.fullmatch(args.share.strip()):
            raise ValueError(f'"{args.share}" is not a share from 0 to 1')
        values['bot_guild_share'] = args.share.strip()
    if args.min_level is not None:
        level = args.min_level.strip()
        if not LEVEL.fullmatch(level) or not 1 <= int(level) <= 50:
            raise ValueError(f'"{args.min_level}" is not a level from 1 to 50')
        values['bot_guild_min_level'] = str(int(level))
    for part, key in PARTS.items():
        choice = getattr(args, part)
        if choice is not None:
            values[key] = 'True' if choice == 'on' else 'False'

    with sqlite3.connect(args.db.as_uri() + '?mode=ro', uri=True) as source:
        before = current(source)
    for key, value in before.items():
        print(f'{key:26s} now: {value if value not in (None, "") else "(not set: server default)"}')
    if not values:
        print('Nothing to change. Use --on, --off, --share, --min-level or a part (--social, --claims, ...).')
        return
    for key, value in values.items():
        print(f'{key:26s} new: {value}')
    if not args.apply:
        print('Dry run only; the database was not changed. Add --apply to write it.')
        return

    if os.name == 'nt':
        running = subprocess.check_output(['powershell', '-NoProfile', '-Command',
            'Get-Process CoreServer,DOLServer,OfflineDAoC,game -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName; exit 0'], text=True)
        if running.strip():
            raise RuntimeError('Close the server, launcher and game first: ' + running.strip())
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup = args.db.parent.parent / 'server/deployment-backups' / ('bot-guilds-' + stamp)
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
    except (ValueError, RuntimeError) as error:
        raise SystemExit(f'Not changed: {error}')
