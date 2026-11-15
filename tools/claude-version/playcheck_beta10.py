"""The Beta 1.0 play check (docs/PLAYCHECK-BETA10.md): prepare a test copy, then read its server log row by row.

  python tools\\claude-version\\playcheck_beta10.py prep                 (show what the session needs; changes nothing)
  python tools\\claude-version\\playcheck_beta10.py prep --apply         (backup, then camp bonus 0 for the XP measurement)
  python tools\\claude-version\\playcheck_beta10.py restore --apply      (the camp bonus as it was before prep)
  python tools\\claude-version\\playcheck_beta10.py report               (every row's proof lines from the server log)
  python tools\\claude-version\\playcheck_beta10.py report --player Testrunner --log path\\to\\server.log
  python tools\\claude-version\\playcheck_beta10.py watch-xp --bot Botname  (a grouped bot's XP per kill, against the factor)

Run it in the game folder (the one with runtime\\), or pass --root. prep and restore need the server, launcher and game
closed. report only reads: the log, and the database for the clock row (safe while the server runs).
"""
import argparse
import contextlib
import datetime
import json
import os
import re
import sqlite3
import subprocess
import time
import sys
from collections import Counter, defaultdict
from pathlib import Path

DEFAULT_ROOT = Path(__file__).resolve().parents[2]  # <playable>/tools/claude-version/

# The settings the session needs (the launcher's Living Server tab), with the value each must have.
NEEDED = {
    'bot_guilds_enabled': 'True', 'bot_guild_claims_keeps': 'True', 'bot_living_grats': 'True',
    'bot_living_death': 'True', 'bot_living_nemesis': 'True', 'bot_schedule_mode': 'clock',
    'bot_living_breaks': 'True', 'bot_living_body_language': 'True', 'bot_voices_enabled': 'True',
    'bot_living_tales': 'True',
}
CAMP_KEYS = ('max_camp_bonus', 'max_dungeon_camp_bonus')
CAMP_DESCRIPTION = 'Max camp bonus, 0.55 = 55%'

# GameLiving.XPForLiving for levels 0-50 (the experience a living of that level is worth).
XP_FOR_LIVING = [5, 10, 20, 40, 80, 160, 320, 640, 1280, 2560, 5120, 7240, 10240, 14480, 20480, 28980, 40960, 57930,
                 81920, 115850, 163840, 206435, 231705, 327680, 412850, 520160, 655360, 825702, 1040319, 1310720,
                 1651404, 2080638, 2621440, 3302807, 4161277, 5242880, 6022488, 6918022, 7946720, 9128384, 10485760,
                 12044975, 13836043, 15893440, 18258769, 20971520, 24089951, 27672087, 31625241, 36513537, 41943040]

# AutonomousBotSchedule's clock curve: the share of the roster online by local hour.
WEEKDAY = [0.42, 0.32, 0.24, 0.17, 0.13, 0.12, 0.14, 0.19, 0.25, 0.30, 0.34, 0.38,
           0.44, 0.46, 0.48, 0.52, 0.58, 0.68, 0.80, 0.90, 0.96, 0.97, 0.90, 0.66]
WEEKEND = [0.58, 0.46, 0.35, 0.25, 0.18, 0.15, 0.16, 0.20, 0.30, 0.42, 0.55, 0.64,
           0.70, 0.74, 0.76, 0.78, 0.80, 0.84, 0.89, 0.95, 0.98, 0.98, 0.93, 0.76]

STAMP = re.compile(r'^(\d\d)/(\d\d) (\d\d):(\d\d):(\d\d)')


def clock_share(local):
    """AutonomousBotSchedule.ClockShare: the hour's share, run toward the next hour's (Friday 6 p.m. to Sunday: weekend)."""
    def curve(t):
        weekend = t.weekday() >= 5 or (t.weekday() == 4 and t.hour >= 18)
        return (WEEKEND if weekend else WEEKDAY)[t.hour]
    hour = local.replace(minute=0, second=0, microsecond=0)
    here, nxt = curve(hour), curve(hour + datetime.timedelta(hours=1))
    return here + (nxt - here) * ((local - hour).total_seconds() / 3600.0)


def rate_for(spec, level, flat):
    """LevelRangeXpRates.EffectiveRate: the range's rate, or the flat bot_xp_rate outside every range."""
    for part in (spec or '').split(';'):
        if ':' not in part:
            continue
        span, rate = part.split(':', 1)
        low, _, high = span.partition('-')
        if int(low) <= level <= int(high or low):
            return float(rate)
    return flat


def expected_capped(level, rate, cap_percent=125):
    """A solo kill at the experience cap, camp bonus 0: (long)((long)(XPForLiving[level] * cap% / 100.0) * rate)."""
    cap = int(XP_FOR_LIVING[level] * cap_percent / 100.0)
    return cap, int(cap * rate)


def running_reason(db):
    if os.name == 'nt':
        running = subprocess.check_output(['powershell', '-NoProfile', '-Command',
            'Get-Process CoreServer,DOLServer,OfflineDAoC,game -ErrorAction SilentlyContinue | '
            'Select-Object -ExpandProperty ProcessName; exit 0'], text=True)
        if running.strip():
            return 'Close the server, launcher and game first: ' + running.strip()
    wal = Path(str(db) + '-wal')
    if wal.exists() and wal.stat().st_size > 0:
        return f'{wal.name} is not empty: the server is running or did not close cleanly.'
    return None


def properties(db, keys):
    marks = ','.join('?' * len(keys))
    return dict(db.execute(f'SELECT [Key], Value FROM ServerProperty WHERE [Key] IN ({marks})', list(keys)).fetchall())


def set_property(db, key, value):
    now = datetime.datetime.now(datetime.timezone.utc).isoformat()
    db.execute('INSERT INTO ServerProperty (Category, [Key], Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID) '
               "VALUES ('pve', ?, ?, '', ?, ?, ?) ON CONFLICT([Key]) DO UPDATE SET Value=excluded.Value, "
               'LastTimeRowUpdated=excluded.LastTimeRowUpdated', (key, CAMP_DESCRIPTION, value, now, key))


def backup(db, root, what):
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    folder = root / 'runtime/server/deployment-backups' / f'{what}-{stamp}'
    folder.mkdir(parents=True, exist_ok=False)
    with contextlib.closing(sqlite3.connect(folder / 'opendaoc.sqlite3.db')) as copy:
        db.backup(copy)
    return folder


def prep(args):
    db_path = args.root / 'runtime/data/opendaoc.sqlite3.db'
    with contextlib.closing(sqlite3.connect(db_path)) as db:
        stored = properties(db, list(NEEDED) + list(CAMP_KEYS) + ['bot_xp_rate_by_level', 'bot_xp_rate', 'XP_Cap_Percent'])
        print('Settings the session needs (launcher, Living Server tab):')
        for key, want in NEEDED.items():
            have = stored.get(key, '(default)')
            print(f"  {'ok ' if str(have).lower() == want.lower() else 'SET'} {key} = {have} (needs {want})")
        print(f"  bot_xp_rate_by_level = {stored.get('bot_xp_rate_by_level', '(empty)')}, bot_xp_rate = "
              f"{stored.get('bot_xp_rate', '1.0')}, XP_Cap_Percent = {stored.get('XP_Cap_Percent', '125')}")
        print('Camp bonus now: ' + ', '.join(f'{k} = {stored.get(k, "(default)")}' for k in CAMP_KEYS))
        now = datetime.datetime.now(datetime.timezone.utc)
        for realm, name in ((1, 'Albion'), (2, 'Midgard'), (3, 'Hibernia')):
            row = db.execute('SELECT Value FROM offline_lore_memory WHERE MemoryKey = ?', (f'tale:last:{realm}',)).fetchone()
            if not row:
                print(f'{name}: no tale heard yet; its first tale comes 25-45 minutes into the session.')
                continue
            last = datetime.datetime.fromisoformat(row[0].replace('Z', '+00:00'))
            if last.tzinfo is None:
                last = last.replace(tzinfo=datetime.timezone.utc)
            hours = (now - last).total_seconds() / 3600
            note = 'fine' if hours >= 3 else (f'TOO RECENT: the first tale waits until {3 - hours:.1f} h from now, not 25-45 min '
                                              '(delete the tale:last rows on the test copy, or play later)')
            print(f'{name}: last tale {hours:.1f} h ago: {note}')
        roster = db.execute('SELECT Realm, COUNT(*) FROM offline_world_bots WHERE IsRetired = 0 GROUP BY Realm').fetchall()
        print('Roster (not retired): ' + ', '.join(f'realm {r}: {n}' for r, n in roster))
        if not args.apply:
            print('Dry run. With --apply: a backup, then max_camp_bonus and max_dungeon_camp_bonus 0 until restore.')
            return 0
        if reason := running_reason(db_path):
            print(reason)
            return 1
        folder = backup(db, args.root, 'playcheck-beta10')
        saved = {key: stored.get(key) for key in CAMP_KEYS}
        (folder / 'camp-bonus.json').write_text(json.dumps(saved))
        with db:
            for key in CAMP_KEYS:
                set_property(db, key, '0')
        print(f'Camp bonus 0 (was {saved}). Backup and saved values: {folder}')
    return 0


def restore(args):
    db_path = args.root / 'runtime/data/opendaoc.sqlite3.db'
    folders = sorted((args.root / 'runtime/server/deployment-backups').glob('playcheck-beta10-*/camp-bonus.json'))
    if not folders:
        print('No playcheck-beta10 backup with camp-bonus.json found.')
        return 1
    saved = json.loads(folders[-1].read_text())
    print(f'Camp bonus from {folders[-1].parent.name}: {saved} (a missing value is the default 0.55 / 0.66)')
    if not args.apply:
        print('Dry run. Add --apply.')
        return 0
    if reason := running_reason(db_path):
        print(reason)
        return 1
    defaults = {'max_camp_bonus': '0.55', 'max_dungeon_camp_bonus': '0.66'}
    with contextlib.closing(sqlite3.connect(db_path)) as db, db:
        for key in CAMP_KEYS:
            set_property(db, key, saved.get(key) or defaults[key])
    print('Restored.')
    return 0


def report(args):
    log_path = args.log or args.root / 'runtime/server/logs/server.log'
    lines = Path(log_path).read_text(encoding='utf-8', errors='replace').splitlines()
    print(f'{log_path}: {len(lines)} lines')
    fork = next((l for l in lines if 'FORK_BUILD' in l), None)
    print('FORK_BUILD: ' + (fork.split('FORK_BUILD', 1)[1].strip() if fork else 'not found'))
    spec = args.spec
    if spec is None and fork and (m := re.search(r'bot_xp_rate_by_level="([^"]+)"', fork)):
        spec = m.group(1)

    def grep(*needles):
        return [l for l in lines if all(n in l for n in needles)]

    def show(title, found, limit=3):
        print(f'\n== {title}: {len(found)}')
        for l in found[:limit]:
            print('   ' + l[:260])

    player = args.player
    show('1 keep claims (BOT_GUILD_CLAIM)', grep('BOT_GUILD_CLAIM'))
    show('   bot guilds founded', grep('Bot guilds: founded'), 0)
    show('2 /gc invite of a bot (BOT_GUILD_JOIN invitedBy=you)', grep('BOT_GUILD_JOIN', f'invitedBy={player}'))
    show('2 /gc remove of a bot (BOT_GUILD_REMOVE by=you)', grep('BOT_GUILD_REMOVE', f'by={player}'))
    logins = grep('entered the live world from the staggered queue')
    show('3 clock: logins', logins, 0)
    show('3 clock: scheduled logoffs (LIVING_SERVER_LOGOUT reason=logoff mode=Clock)', grep('LIVING_SERVER_LOGOUT', 'reason=logoff', 'mode=Clock'))
    show('3 clock: breaks (reason=brb)', grep('LIVING_SERVER_LOGOUT', 'reason=brb'), 1)
    db_path = args.root / 'runtime/data/opendaoc.sqlite3.db'
    if db_path.exists():
        with contextlib.closing(sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)) as db:
            roster, online = db.execute('SELECT COUNT(*), SUM(IsOnline) FROM offline_world_bots WHERE IsRetired = 0').fetchone()
        share = clock_share(datetime.datetime.now())
        print(f'   now: {online or 0} of {roster} bots online; the clock wants about {share:.2f} x {roster} = {share * roster:.0f} '
              '(a little more: no-lifers play longer; bots finishing a group stay up to 15 minutes)')
    show('4 nemesis lines in /faction (src=nemesis)', grep('CHAT_LINE', 'src=nemesis'))
    show('4 revenge lines (src=revenge)', grep('CHAT_LINE', 'src=revenge'))
    show('5 a passing bot rezzed you (LIVING_SERVER_REZ corpse=you)', grep('LIVING_SERVER_REZ ', f'corpse="{player}"'))
    skips = Counter(m.group(1) for l in grep('LIVING_SERVER_REZ_SKIP', f'corpse="{player}"')
                    if (m := re.search(r'reason=(\S+)', l)))
    print('   why nearby rez bots did not: ' + (', '.join(f'{k} {v}' for k, v in skips.most_common()) or 'none logged'))
    show('5 a dead bot asked you (LIVING_SERVER_REZ_ASK rezzer=you)', grep('LIVING_SERVER_REZ_ASK', f'rezzer="{player}"'))
    show('5 a bot thanked a rez (CHAT_LINE via=...OnBotResurrected~)', grep('CHAT_LINE', 'via=AutonomousLivingServer.OnBotResurrected'))
    grats = grep('CHAT_LINE', 'via=AutonomousLivingServer.Grats')
    show('6 grats to you (ch=say from nearby bots, ch=group from groupmates)', [l for l in grats if player.lower() in l.lower()])
    body = [tuple(int(x) for x in m.groups()) for l in grep('BOT_BODY_LANGUAGE')
            if (m := re.search(r'looks=(\d+) emotes=(\d+) steps=(\d+) greets=(\d+) cheers=(\d+)', l))]
    sums = [sum(b[i] for b in body) for i in range(5)]
    print(f'\n== 8 body language: {len(body)} reports; looks={sums[0]} emotes={sums[1]} steps={sums[2]} greets={sums[3]} cheers={sums[4]}')
    print(f"   town breaks started (AUTONOMOUS_TOWN_IDLE_STARTED): {len(grep('AUTONOMOUS_TOWN_IDLE_STARTED'))}")
    formed = defaultdict(Counter)
    for l in grep('AUTONOMOUS_GROUP_FORMED'):
        if (t := STAMP.match(l)) and (o := re.search(r'objective=(\S+)', l)):
            formed[f'{t.group(1)}/{t.group(2)} {t.group(3)}:00'][o.group(1)] += 1
    print('\n== 9 bot groups formed per hour (AUTONOMOUS_GROUP_FORMED, by objective)')
    for hour, kinds in sorted(formed.items()):
        print(f'   {hour}  ' + ', '.join(f'{k} {v}' for k, v in sorted(kinds.items())))
    show('10 linkdeads (LIVING_SERVER_LINKDEAD)', grep('LIVING_SERVER_LINKDEAD'))
    show('10 linkdead lines in /group', grep('CHAT_LINE', 'ch=group', 'via=AutonomousBotSchedule.'))
    show('11 session start (LIVING_SESSION_START realm=Albion)', grep('LIVING_SESSION_START', 'realm=Albion'), 2)
    show('11 tales started (TALE_START realm=Albion)', grep('TALE_START', 'realm=Albion'))
    show('11 rumour after a tale (TALE_RUMOUR: only a tale that is not a raid\'s has one)', grep('TALE_RUMOUR', 'realm=Albion'))
    xp_report(lines, spec, args.flat, args.cap_percent)
    return 0


def kill_amounts(bot_level, rate, group=1, cap_percent=125):
    """What one kill of a monster of each level gives a gamebot, camp bonus 0, no guild buff, no adds, all the damage
    from its group (AbstractServerRules.AwardBotOnNpcKill and GameBot.GainExperience): {monster level: amount}. A group
    of n members of one level shares the monster's experience, ceil(value / n) each, and adds 12.5% per other member,
    unscaled."""
    _, cap = expected_capped(bot_level, 1.0, cap_percent)
    amounts = {}
    for mob in range(0, 51):
        share = XP_FOR_LIVING[mob] if group <= 1 else -(-XP_FOR_LIVING[mob] // group)
        base = min(share, cap)
        amounts[mob] = int(base * rate) + int(base * (group - 1) * 0.125)
    return amounts


def first_kills(lines, spec, flat, cap_percent):
    """A new gamebot's first experience (xp=0->N, the launcher's + Lv.1 bots): one kill, nearly always solo."""
    pattern = re.compile(r'AUTONOMOUS_XP_PROGRESS .*?amount=(\d+) xp=0->(\d+) level=(\d+)->(\d+)')
    found = Counter()
    for l in lines:
        if not (m := pattern.search(l)) or not 1 <= int(m.group(3)) < 50:
            continue
        level, amount = int(m.group(3)), int(m.group(1))
        rate = rate_for(spec, level, flat)
        with_factor = set(kill_amounts(level, rate, 1, cap_percent).values())
        without = set(kill_amounts(level, flat, 1, cap_percent).values())
        found['lines'] += 1
        found['factor' if amount in with_factor else 'flat' if amount in without else 'other'] += 1
    return found


def xp_report(lines, spec, flat, cap_percent):
    """Row 7: every AUTONOMOUS_XP_PROGRESS amount (a bot's first experience, or a kill that gave it a level) against the
    exact amount of a solo kill at the experience cap, with the factor and without it."""
    print(f'\n== 7 bot XP-by-level factor ({spec or "no bot_xp_rate_by_level"}, flat {flat}, cap {cap_percent}%)')
    first = first_kills(lines, spec, flat, cap_percent)
    print(f"   new bots' first kills (xp=0->N): {first['lines']} lines; {first['factor']} are exactly a solo kill with the factor, "
          f"{first['flat']} exactly one without it, {first['other']} neither (a shared kill, or the camp bonus still on)")
    pattern = re.compile(r'AUTONOMOUS_XP_PROGRESS .*?amount=(\d+) xp=(\d+)->(\d+) level=(\d+)->(\d+)')
    bands = defaultdict(lambda: Counter())
    examples = {}
    for l in lines:
        if not (m := pattern.search(l)):
            continue
        amount, level = int(m.group(1)), int(m.group(4))
        if not 1 <= level < 50:
            continue
        rate = rate_for(spec, level, flat)
        cap, exact = expected_capped(level, rate, cap_percent)
        band = bands[rate]
        band['lines'] += 1
        if amount == exact:
            band['exact'] += 1
            examples.setdefault(rate, f'level {level}: cap {cap} x {rate} = {exact}, logged amount={amount}')
        if rate != 1.0 and amount == cap:
            band['flat'] += 1
        if amount > exact:
            band['above'] += 1
    for rate, band in sorted(bands.items()):
        print(f"   rate {rate}: {band['lines']} lines; {band['exact']} exactly cap x {rate}; {band['flat']} exactly cap x 1 "
              f"(the factor missing); {band['above']} above cap x {rate} (a group, guild buff or add bonus)")
        if rate in examples:
            print(f'      e.g. {examples[rate]}')
    print('   Level-up kills are mostly group kills or blues, so few sit exactly at the cap; any that do, and none at cap x 1,')
    print('   back the factor up. The exact measurement is watch-xp. All of it needs the camp bonus at 0 (prep --apply).')


def watch_xp(args):
    """Row 7, exact: one gamebot in your group, its experience read from the database after each kill."""
    db_path = args.root / 'runtime/data/opendaoc.sqlite3.db'
    spec, flat = args.spec, args.flat
    with contextlib.closing(sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)) as db:
        if spec is None:
            row = db.execute("SELECT Value FROM ServerProperty WHERE [Key] = 'bot_xp_rate_by_level'").fetchone()
            spec = row[0] if row else ''
        print(f'Watching {args.bot} (bot_xp_rate_by_level "{spec}", group of {args.group}). Kill one monster at a time; Ctrl+C ends.')
        last = None
        while True:
            row = db.execute('SELECT Level, Experience FROM offline_world_bots WHERE Name = ? AND IsRetired = 0', (args.bot,)).fetchone()
            if row is None:
                print(f'No gamebot named {args.bot}.')
                return 1
            if last is not None and row[1] != last[1]:
                level, delta = last[0], row[1] - last[1]
                print(f'{datetime.datetime.now():%H:%M:%S} {args.bot} level {level}: +{delta} -> '
                      f'{judge_kill(delta, level, spec, flat, args.group, args.cap_percent)}')
            last = row
            time.sleep(1)


def judge_kill(delta, level, spec, flat, group=2, cap_percent=125):
    """Which single kill gives exactly this much, with the level's factor or with the flat rate. The group's share counts
    the members who fought, so every size is tried, the given one first."""
    rate = rate_for(spec, level, flat)
    for size in dict.fromkeys([group] + list(range(1, 9))):
        if mobs := [m for m, a in kill_amounts(level, rate, size, cap_percent).items() if a == delta]:
            return f'EXACT with the factor {rate}: a level {mobs} monster, {size} in the group'
    if rate != flat:
        for size in dict.fromkeys([group] + list(range(1, 9))):
            if mobs := [m for m, a in kill_amounts(level, flat, size, cap_percent).items() if a == delta]:
                return f'the flat rate {flat}, factor NOT applied: a level {mobs} monster, {size} in the group'
    return ('no single kill (two kills in one save, damage from outside the group, a guild buff, adds, or the camp bonus '
            'still on)')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['prep', 'restore', 'report', 'watch-xp'])
    parser.add_argument('--bot', help='watch-xp: the gamebot in your group')
    parser.add_argument('--group', type=int, default=2, help='watch-xp: members in your group (default 2: you and the bot)')
    parser.add_argument('--root', type=Path, default=DEFAULT_ROOT, help='the game folder (has runtime\\)')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--log', type=Path)
    parser.add_argument('--player', default='Testrunner')
    parser.add_argument('--spec', help='bot_xp_rate_by_level (default: from the FORK_BUILD line)')
    parser.add_argument('--flat', type=float, default=1.0, help='bot_xp_rate (default 1.0)')
    parser.add_argument('--cap-percent', type=int, default=125, help='XP_Cap_Percent (default 125)')
    args = parser.parse_args(argv)
    if args.command == 'watch-xp' and not args.bot:
        parser.error('watch-xp needs --bot NAME')
    return {'prep': prep, 'restore': restore, 'report': report, 'watch-xp': watch_xp}[args.command](args)


if __name__ == '__main__':
    sys.exit(main())
