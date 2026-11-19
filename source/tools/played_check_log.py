#!/usr/bin/env python3
"""Reads a server.log from the owner's PC and reports, per 1.2 feature, whether the log shows the evidence each played
check asks for (docs/PLAYED-CHECK-1.2.md, docs/INSTALL-CHECKS-1.2.md, docs/BOTS-AS-PEOPLE-1.2.md "Played checks",
docs/ERA-BUILDS-1.2.md "WP6").

    python source/tools/played_check_log.py server.log
    python source/tools/played_check_log.py server.log --since "11/17 06:18" --build 4be6fab
    python source/tools/played_check_log.py server.log --json

What it does and does not say:
* It reports evidence only. Each check is "Seen in log on build X" (with the time of the first matching line and that
  line, shortened), "Not seen", or "Contradicted" (a line that goes against the row, quoted). It never says a feature
  works: the owner's rule (AGENTS.md, 2026-11-08) is that a row counts once seen in his PC log, and some rows also need
  what only the screen shows (the owner's 20-minute look, a tale on screen). Those parts are named in the check's note.
* The log is split by server process: the line `DOL.GS.GameServer | Current directory is:` opens one, and its
  `FORK_BUILD name=... commit=...` line names its build. Every result is given per build and never mixes builds. A part
  of the log with no FORK_BUILD (the tail of a run whose start is in an earlier file, or a build older than the line)
  is listed and not judged.
* Player and account names are masked in quoted lines (`player=`, `account=`, `owner=`, `guest=`, and the character
  name of CUSTOM_CLASS_REFUSED). Bot names are kept.
* The file is read once, line by line (a 100 MB log takes seconds).

The overnight row's threshold is owner decision 9 of 2026-11-15: 8 hours, no game-loop stall of 2.0 s or more, at most
5 stalls of 1.0 s or more in any hour. Stalls are read the way source/tools/check_overnight_run.py reads them
(SERVER_STALL worstTickMs and AI_PROFILE max, one per minute, the two merged within 75 s, GC-only minutes not counted).
That tool stays the gate's own check (it also checks SERVER_WORK gaps and the run's stop).

Exit code: 0 a report was made, 2 nothing to judge (no FORK_BUILD in the log, or none for --build).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import re
import sys
from collections import Counter, defaultdict, deque
from pathlib import Path

SEEN = "Seen"
NOT_SEEN = "Not seen"
CONTRADICTED = "Contradicted"
NOT_APPLICABLE = "Not applicable"
# An attempt that ends "never entered combat" with this many fallen was a fight the route missed (sweep 6b).
NEVER_ENTERED_FALLEN = 10

GATE_HOURS = 8.0
HARD_STALL_MS = 2000.0
STALL_MS = 1000.0
MAX_STALLS_PER_HOUR = 5
PAIR_SECONDS = 75
QUOTE_CHARS = 160
# A STRANDED_REGROUP point this close to a resting boss is inside its reach: aggro (600 for Icelord Agmundr) plus the
# 300 of the fix in RAID-TUSCARAN-1117. Changed with --near.
RESTING_BOSS_NEAR = 900.0
STATUS_STALE_SECONDS = 15 * 60
# A boss is engaged (not resting) from a FINAL_ENGAGE, an EARLY_PULL Commit, a TARGET_CALLED, fight-ledger damage or a
# required-adds switch for it until its next wipe, defeat or heal, and for at most this long after the last such line.
ENGAGED_LAPSE_SECONDS = 15 * 60
# Return-wave steps that show headway after a step=stuck line (the wave recovered).
RETURN_WAVE_HEADWAY = ("moving", "ahead", "cleared", "met", "passable")
# Sweep 6c (build b1b6846). A return wave whose at= step stays the same this long did not move, whatever its lines say
# (Galladoria's wave 10 at step 2 and Krondon's at step 14 were counted as "made headway again").
WAVE_STEP_STILL_SECONDS = 15 * 60
# A boss leashed this many times in a row from (about) the same distance never went home: frozen or untouchable out of
# its tether (the Earth primal: 13 leashes in a row at fromSpawn=1044).
FROZEN_LEASH_RUN = 3
FROZEN_LEASH_UNITS = 25
# A monster's raw position taken as the raid's front (approach=raw) that this many stranded regroups went to and this
# many route retries came from: a regroup point raiders can't walk from (the Air primal's, 690 above the floor).
RAW_FRONT_REGROUPS = 10
RAW_FRONT_RETRIES = 10
# The regroup row's share bar (docs/PLAYED-CHECK-1.2.md, "Regroups no longer flap", reasoning there). Owner decision
# 2026-11-18: 11.5%, a backstop. The exact checks (a), (b) and (c) decide one hop and no echo. Raised to 15% by the owner
# (2026-11-18 18:19, "Raise to 15% is a good idea"): on b1b6846 one hop in dense warbands fanned out to 12.6-14.4% by the
# hour with (a), (b) and (c) all 0, so 11.5% failed a correct build. 15% still catches a runaway relay; the exact checks
# catch a second hop or an echo at any share.
RELAY_SHARE_BAR = 15.0
# Sweep 6d (build b1b6846): a required-adds phase with no add death this long while the raid dealt the adds more than
# their combined health (D-1, the Galladoria Earthmenders); raider deaths to an epic this soon after its raid ended, in
# its region (P-1, P-2); a raid's end outcome lines are matched to its REALM_RAID_ENDED within this many seconds.
NO_PROGRESS_MINUTES = 20
NO_PROGRESS_GAP_SECONDS = 10 * 60
AFTER_END_SECONDS = 10 * 60
# Sweep 6e (build b1b6846). A raid dungeon's region, and the dungeon its way in leads through (AutonomousRealmRaid's
# RaidDungeons and RealmRaidNeutralEvents; a REALM_EXPEDITION_ROUTE_GATE line's region= adds the one a raid used).
RAID_DUNGEON_REGIONS = {
    "dungeon-albion-krondon": "61", "dungeon-albion-avalon": "50", "dungeon-albion-crystal": "62",
    "dungeon-albion-corrupt": "277", "dungeon-midgard-trollheim": "150", "dungeon-midgard-iarnvidiur": "161",
    "dungeon-midgard-dodens": "246", "dungeon-hibernia-tursuil": "190", "dungeon-hibernia-marfach": "276",
}
WAY_IN_REGIONS = {"dungeon-albion-crystal": "50", "summoners-albion": "277", "summoners-midgard": "246",
                  "summoners-hibernia": "276"}
# An open attempt whose boss is back at RESET_HEALTH or more after RESET_LOW or less has reset (S-2); raider deaths to it
# more than RESET_GRACE_SECONDS later while the attempt stays open are fed in piecemeal; RESET_DEATHS or more is a finding.
RESET_LOW, RESET_HEALTH, RESET_GRACE_SECONDS, RESET_DEATHS = 70, 95, 60, 10
# A return wave with headway (a kill, a step, step=moving/passable/ahead) this recently is walking (T-1): the server's
# RealmRaidReturnWave.StuckMilliseconds.
WAVE_HEADWAY_SECONDS = 4 * 60
# A wave stuck this little before its raid won is not "never made headway" (the stuck-wave row; Crystal Cave's wave 2,
# stuck 51 s before Xanxicar died).
STUCK_BEFORE_WIN_SECONDS = 5 * 60
# Sweep 6f (build b1b6846, W-2): a return wave rebased onto its catching-up raiders at a step none of them stood at
# (standing=0, or no standing= field: builds before the fix put the wave at the step they walked to) that then shows no
# act of its own (REBASE_ACTS: a step, a fight, its end) for this long sat still: Tur Suil's wave 1, 27.5 minutes from
# 15:38:03; Trollheim's wave 4, 34.8 minutes from 15:10:54 (raiders joining it, step=waiting and step=stuck are not acts).
REBASE_SILENT_SECONDS = 10 * 60
REBASE_ACTS = ("moving", "fighting", "cleared", "met", "passable", "left", "released", "ended", "lost", "rebased")
# Sweep 7b (build 9a173f4): a boss whose damage over time reaches the whole raid and is curable (CurePoison removes every
# DamageOverTime) -> the spell. A fight with one of them (the boss hits the raid in an EPIC_FIGHT_LEDGER minute) should see
# the raid's cures (RAID_CURE_POISON) in that region; minutes more than CURE_FIGHT_GAP_SECONDS apart are separate fights,
# and a cure line up to CURE_AFTER_SECONDS after the fight's last minute counts (it is written once a minute at most).
CURABLE_DOT_BOSSES = {"Summoner Roesia": "Summoner Pain"}
CURE_FIGHT_GAP_SECONDS = 5 * 60
CURE_AFTER_SECONDS = 3 * 60
END_OUTCOME_SECONDS = 15
# DF knight loop (build b1b6846): the same target killed again this many times or more in a row in one raid's dungeon,
# with no other target killed between (REALM_EXPEDITION_TARGET_DEFEATED): Chthonic Knight Ukobat 14 times in a row from
# 16:42:30 to 18:14:46 (13 re-kills) while the Albion raid's trail gate turned it back to him each time he respawned.
REKILL_LOOP = 5
REQUIRED_ADD_RE = re.compile(r'required-add \d+/\d+ of "([^"]+)"')
CATALOG = Path(__file__).resolve().parents[2] / "docs" / "encounters" / "epic-encounters.json"
_catalog = None


def catalog() -> tuple[dict, set]:
    """From the epic encounter catalog (empty when it is missing): each encounter add's maximum health by name, and the
    encounters' boss names."""
    global _catalog
    if _catalog is None:
        health, bosses = {}, set()
        try:
            data = json.loads(CATALOG.read_text(encoding="utf-8"))
            for encounter in data.get("encounters", []):
                if encounter.get("boss"):
                    bosses.add(encounter["boss"])
                for add in encounter.get("adds") or []:
                    if add.get("name") and isinstance(add.get("maxHealth"), (int, float)):
                        health.setdefault(add["name"], int(add["maxHealth"]))
        except (OSError, ValueError):
            pass
        _catalog = (health, bosses)
    return _catalog


_homes = None


def boss_limits() -> dict:
    """From the epic encounter catalog: each boss's name -> (fight-at-home radius, template tether), 0 when unknown."""
    global _homes
    if _homes is None:
        _homes = {}
        try:
            data = json.loads(CATALOG.read_text(encoding="utf-8"))
            for encounter in data.get("encounters", []):
                if encounter.get("boss"):
                    home = (encounter.get("aiTactics") or {}).get("fightAtHomeRadius") or 0
                    tether = encounter.get("templateTetherRange") or 0
                    _homes.setdefault(encounter["boss"], (int(num(home)), int(num(tether))))
        except (OSError, ValueError):
            pass
    return _homes


def add_health() -> dict:
    return catalog()[0]
RETIRED_ACT_RE = re.compile(r'\bbot="?([^"\s]+)"? id=(\d+)')

LEVELS = {"TRC", "DBG", "INF", "WRN", "ERR", "FTL"}
KV_RE = re.compile(r'(\w+)=("([^"]*)"|\S+)')
TAG_RE = re.compile(r"[A-Z][A-Z0-9_]{3,}")
MASK_RE = re.compile(r'\b(player|account|owner|guest)=("[^"]+"|[^\s"]+)')
CUSTOM_NAME_RE = re.compile(r"\bname=\S+")
OTHER_NAME_RE = re.compile(r'\b(killer|target|victim|prey)=("[^"]*"|\S+)')

# Data-loading ERR lines every start writes (the world database's known gaps), not counted as errors.
KNOWN_STARTUP_ERRORS = (
    ("DOL.GS.NpcTemplateMgr", "No npctemplate with ID"),
    ("DOL.GS.NpcTemplate", "Tried to load a null spell into NPC template"),
    ("DOL.GS.SkillBase", "LineXSpell Spell Adding Error"),
    ("DOL.GS.DoorMgr", "Failed to register door in pathfinding provider"),
    ("DOL.GS.MerchantTradeItems", "Item template with ID"),
)
CUSTOM_CLASS_WORDS = re.compile(r"Sluaghbinder|Acolyte|Muirenn|class[ =]6[23]\b", re.IGNORECASE)

# The fixed raids' regions (AutonomousRealmRaid.Definitions); others are learned from REALM_EXPEDITION_ROUTE_GATE.
EVENT_REGIONS = {
    "dragon-albion": 1, "dragon-midgard": 100, "dragon-hibernia": 200, "epic-albion": 60, "epic-midgard": 160,
    "epic-hibernia": 191, "summoners-albion": 248, "summoners-midgard": 248, "summoners-hibernia": 248,
    "darkness-albion": 249, "darkness-midgard": 249, "darkness-hibernia": 249,
}
OUTCOMES = {"Boss defeated": "won", "Defeated": "lost", "Timed out": "timed out (stall)", "Superseded": "superseded",
            "Stopped by player": "stopped by player", "Failed rally": "failed rally", "Sweep cap": "sweep cap"}
SKILL_SHARES = (30.0, 50.0, 20.0)   # casual / average / veteran (BOTS-AS-PEOPLE-1.2, WP1 played check)
MISTAKES_MIN_IN_GROUP = 100         # chance decisions in the player's group before the quarter rate is judged
MISTAKES_MAX_RATIO = 0.5            # rate_in / rate_out above this contradicts "about a quarter" (WP2 played check)
MISTAKES_MIN_RATIO = 0.1            # below this, too few mistakes in the player's group to call it "about a quarter"
PROMISE_KEPT_BAR = 0.7              # "omw": kept at least this share of the promises that were kept or timed out
PROMISE_MIN_JUDGED = 5              # fewer kept-or-timed-out promises than this are too few to contradict


# ---- Lines -------------------------------------------------------------------------------------------------------

def parse_head(line: str):
    """(month, day, h, m, s, rest) for a line that starts with 'MM/DD HH:MM:SS | ', else None."""
    if len(line) < 17 or line[2] != "/" or line[5] != " " or line[8] != ":" or line[11] != ":" or line[14:17] != " | ":
        return None
    try:
        return int(line[0:2]), int(line[3:5]), int(line[6:8]), int(line[9:11]), int(line[12:14]), line[17:]
    except ValueError:
        return None


def split_rest(rest: str):
    """(level, logger, message) from 'LVL | logger | message'."""
    level = ""
    if len(rest) >= 6 and rest[3:6] == " | " and rest[:3] in LEVELS:
        level, rest = rest[:3], rest[6:]
    logger, sep, message = rest.partition(" | ")
    if not sep:
        return level, rest.strip(), ""
    return level, logger.strip(), message


def kv(message: str) -> dict:
    return {k: (q if v.startswith('"') else v) for k, v, q in KV_RE.findall(message)}


def num(value, default=0.0):
    try:
        return float(str(value).rstrip("%"))
    except (TypeError, ValueError):
        return default


def point(value):
    try:
        parts = [float(p) for p in str(value).split(",")[:3]]
        return tuple(parts) if len(parts) == 3 else None
    except ValueError:
        return None


def quote(message: str) -> str:
    text = MASK_RE.sub(lambda m: f"{m.group(1)}=" + ('""' if m.group(2) == '""' else "<player>"), message.strip())
    if text.startswith("CUSTOM_CLASS_REFUSED"):
        text = CUSTOM_NAME_RE.sub("name=<character>", text)
    if "GamePlayer" in text or "kind=Player" in text:
        text = OTHER_NAME_RE.sub(lambda m: f"{m.group(1)}=<name>", text)
    return text if len(text) <= QUOTE_CHARS else text[:QUOTE_CHARS - 3] + "..."


def stamp(t: dt.datetime | None) -> str:
    return t.strftime("%m/%d %H:%M:%S") if t else "-"


def parse_since(text: str, year: int) -> dt.datetime:
    for fmt in ("%Y/%m/%d %H:%M", "%Y/%m/%d %H:%M:%S"):
        try:
            return dt.datetime.strptime(f"{year}/{text.strip()}", fmt)
        except ValueError:
            pass
    raise argparse.ArgumentTypeError(f'--since wants "MM/DD HH:MM", not {text!r}')


class Clock:
    """'MM/DD HH:MM:SS' to datetimes; the year goes up by one when the month falls back (December to January)."""

    def __init__(self, year: int):
        self.year, self.last_month = year, None

    def at(self, month, day, h, m, s):
        if self.last_month is not None and month < self.last_month:
            self.year += 1
        self.last_month = month
        try:
            return dt.datetime(self.year, month, day, h, m, s)
        except ValueError:
            return None


# ---- One server process ------------------------------------------------------------------------------------------

class Evidence:
    __slots__ = ("count", "first_t", "first_line")

    def __init__(self):
        self.count, self.first_t, self.first_line = 0, None, None

    def add(self, t, message, n=1):
        self.count += n
        if self.first_t is None:
            self.first_t, self.first_line = t, quote(message)


class RaidRun:
    def __init__(self, event):
        self.event = event
        self.started = None
        self.present = self.required = None
        self.forced = None
        self.ended = None
        self.outcome = None
        self.minutes = None
        self.named = None
        self.wipes = 0
        self.wipe_bosses = []
        self.stuck_waves = 0
        self.stalled = 0
        self.teleports_near_resting = 0

    def as_dict(self):
        return {"event": self.event, "started": stamp(self.started) if self.started else None,
                "present": self.present, "required": self.required, "forced": self.forced,
                "ended": stamp(self.ended) if self.ended else None, "outcome": self.outcome,
                "result": OUTCOMES.get(self.outcome, self.outcome) if self.outcome else "no end in this build's log",
                "minutes": self.minutes, "named": self.named, "wipes": self.wipes, "wipe_bosses": self.wipe_bosses,
                "stuck_return_waves": self.stuck_waves, "stall_lines": self.stalled,
                "teleports_near_resting_boss": self.teleports_near_resting}


class Section:
    """One server process (or the part of a log before the first process start in it)."""

    def __init__(self, index, first_line_no, near=RESTING_BOSS_NEAR):
        self.index = index
        self.near = near
        self.first_line_no = first_line_no
        self.last_line_no = first_line_no
        self.process_start = None
        self.first_t = self.last_t = None
        self.build_name = self.commit = None
        self.fork_build_t = None
        self.fork_fields = {}
        self.startup_completed = None
        self.stopping = None
        self.judged_from = None       # first line counted (after --since)
        self.ev = defaultdict(Evidence)
        self.n = Counter()
        self.lists = defaultdict(list)
        self.raids: list[RaidRun] = []
        self.open_raids: dict[str, RaidRun] = {}
        self.bosses = defaultdict(dict)   # region -> target -> (inCombat, health, point, t, level)
        self.region_events = dict(EVENT_REGIONS)
        self.stalls = []                  # (t, tick_ms, message, source)
        self.err_after = Counter()
        self.err_known = 0
        self.err_before = 0
        self.err_custom_class = Evidence()
        self.stop_play = Evidence()
        self.pending_critical = None
        self.engaged = defaultdict(dict)  # region -> boss -> (first, last) line showing it in the raid's fight
        self.engage_lines = defaultdict(dict)  # region -> boss -> time of its last FINAL_ENGAGE or EARLY_PULL
        self.add_owner = defaultdict(dict)     # region -> required add -> its boss (from REALM_EXPEDITION_TACTIC)
        self.no_engage = {}                    # (region, boss) -> first line of a pull that logged no engage
        self.boss_adds = defaultdict(set)      # boss -> names of the adds the raid fought or saw raised for it
        self.retry_watch = {}                  # boss -> [time, line, adds seen] of an open retry (FINAL_ENGAGE retry=True)
        self.waves = {}                        # (event, wave) -> "stuck" or "recovered"
        self.retired = {}                      # custom-class bot id -> name (BOT_CUSTOM_CLASS_RETIRED)
        self.copies = {}                       # retired buffbot copy id -> name (BOT_BUFFBOT_COPY_RETIRED)
        self.last_retreat = {}            # group -> reason of its last RVR_RETREAT
        self.relay_sends = {}             # group -> times it relayed (relayedTo>0) in the last 60 s
        self.recent_sends = deque()       # (time, group, (realm, region)) of relays in the last 60 s
        self.last_retreat_from = {}       # group -> from= of its last RVR_RETREAT (a relayed one's sender)
        self.send_origin = {}             # group -> (time, the group whose relay started the stance it sent, or None)
        self.leash_runs = {}              # (region, boss) -> [fromSpawn, run length, first t, first line, counted]
        self.frozen_runs = {}             # "boss at fromSpawn=N" -> the longest such run of leashes
        self.raw_fronts = {}              # (region, "x,y,z") -> (t, line) of a TARGET_SWITCH with approach=raw
        self.regroups_to = Counter()      # (region, "x,y,z") -> STRANDED_REGROUP lines to it
        self.retries_at = Counter()       # (region, "x,y,z") -> ROUTE_RETRY lines from it
        self.wave_steps = {}              # (event, wave) -> [step, since, line, flagged]
        self.required_adds = defaultdict(set)   # boss -> its required adds' names (EPIC_TACTIC_BOT "required-add i/n of")
        self.add_phases = {}              # (region, boss) -> [start, last, dealt, flagged] of a required-adds phase
        self.epic_names = set()           # epics a raid held (RESPAWN_HELD) and bosses of required adds; with the catalog's
        self.ends = []                    # raid ends: {"t", "event", "members", "positions", "regions"}
        self.recent_outcomes = defaultdict(deque)   # event -> (t, members) of AUTONOMOUS_GROUP_OUTCOME lines not yet matched
        self.after_end_deaths = []        # (t, killer, line, end) raider deaths in an ended raid's region soon after
        # Sweep 6e (build b1b6846)
        self.tethers = {}                 # boss -> tether (AUTONOMOUS_TETHER_RESET / _LEASH tether=)
        self.attempts = {}                # (region, boss) -> open attempt: {"t", "lowest", "reset", "deaths", "event"}
        self.reset_attempts = []          # attempts that reset while open: (attempt, reset line)
        self.tether_heals = []            # (t, line) TARGET_HEALED to 95%+ past the boss's home or tether while fighting
        self.avoided = defaultdict(set)   # region -> epic monsters parties walked past (AUTONOMOUS_DUNGEON_EPIC_AVOIDED)
        self.way_in_deaths = []           # (t, killer, region, line, event) deaths in a running raid's way-in region
        self.event_way_in = {}            # event -> its gate region (REALM_EXPEDITION_ROUTE_GATE region=)
        self.gate_bosses = set()          # monsters a raid fought as route gates (REALM_EXPEDITION_ROUTE_GATE bosses=)
        self.wave_headway = {}            # (event, wave) -> [last headway t, kills, ended]
        self.stall_waves = []             # (t, line, event, wave, seconds since its headway) MUSTER_STALLED beside a wave
        # Sweep 6f (build b1b6846)
        self.rebases = {}                 # (event, wave) -> (t, line) of a rebase to a step none of its raiders stood at
        self.silent_rebases = []          # (t, line, key, minutes silent, what ended the silence)
        # Sweep 7b (build 9a173f4)
        self.dot_fights = []              # [region, boss, first t, last t, first line] of a curable raid-wide DoT boss
        self.raid_cures = []              # (t, region, casts, line) RAID_CURE_POISON
        # DF knight loop (build b1b6846)
        self.kill_streaks = {}            # region -> [target, kills, first t, first line, last t, event]
        self.rekill_loops = []            # closed streaks with REKILL_LOOP+ re-kills: (first t, line, region, target, kills, last t, event)

    @property
    def commit7(self):
        return (self.commit or "")[:7]

    @property
    def judged(self):
        return self.commit is not None

    def run_start(self):
        return self.startup_completed or self.fork_build_t or self.first_t

    def run_end(self):
        return self.stopping or self.last_t

    def raid(self, event, t, create=True):
        run = self.open_raids.get(event)
        if run is None and create:
            run = RaidRun(event)
            self.raids.append(run)
            self.open_raids[event] = run
        return run

    def running_event_in(self, region):
        names = [e for e, r in self.region_events.items() if r == region and e in self.open_raids]
        if len(names) == 1:
            return names[0]
        open_now = list(self.open_raids)
        return open_now[0] if len(open_now) == 1 else (names[0] if names else None)


# ---- Reading -----------------------------------------------------------------------------------------------------

def read_log(path: Path, since: dt.datetime | None = None, year: int | None = None, near: float = RESTING_BOSS_NEAR):
    clock = Clock(year or dt.date.today().year)
    sections: list[Section] = []
    sec = Section(0, 1, near)
    sections.append(sec)

    def new_section(line_no):
        nonlocal sec
        if sec.first_t is None and sec.commit is None and sec.process_start is None:
            sec.first_line_no = line_no
            return
        sec = Section(len(sections), line_no, near)
        sections.append(sec)

    with open(path, encoding="utf-8", errors="replace") as handle:
        for line_no, raw in enumerate(handle, 1):
            head = parse_head(raw.lstrip("﻿") if line_no == 1 else raw)
            if head is None:
                pc = sec.pending_critical
                if pc is not None:
                    text = raw.strip()
                    if text.startswith("Calling ") or text.startswith("No other action performed"):
                        if "KickPlayerToCharScreen" in text:
                            sec.stop_play.add(pc[0], pc[1])
                        sec.pending_critical = None
                continue
            sec.pending_critical = None
            t = clock.at(*head[:5])
            if t is None:
                continue
            level, logger, message = split_rest(head[5])
            if logger == "DOL.GS.GameServer" and message.startswith("Current directory is:"):
                new_section(line_no)
                sec.process_start = t
            elif message.startswith("FORK_BUILD "):
                # A second build line, or one after the tail of an earlier process with no start line, opens a part.
                if sec.commit is not None or (sec.process_start is None and sec.first_t is not None):
                    new_section(line_no)
                fields = kv(message)
                sec.build_name, sec.commit, sec.fork_build_t, sec.fork_fields = (
                    fields.get("name"), fields.get("commit", ""), t, fields)
            if sec.first_t is None:
                sec.first_t = t
            sec.last_t = t
            sec.last_line_no = line_no
            if logger == "DOL.GS.GameServer":
                if message.startswith("GameServer startup completed"):
                    sec.startup_completed = t
                elif message.startswith("Stopping server"):
                    sec.stopping = sec.stopping or t
            if since is not None and t < since:
                continue
            if sec.judged_from is None:
                sec.judged_from = t
            if level in ("ERR", "FTL"):
                on_error(sec, t, level, logger, message)
            m = TAG_RE.match(message)
            if sec.retired and "id=" in message and not message.startswith("BOT_CUSTOM_CLASS_RETIRED"):
                retired_acts(sec, t, message)
            if sec.copies and "id=" in message and not message.startswith("BOT_BUFFBOT_COP"):
                copy_acts(sec, t, message)
            if m:
                handler = HANDLERS.get(m.group(0))
                if handler is not None:
                    handler(sec, t, message, level, near)
            elif message.startswith("Watchdog: Thread") and ("exited unexpectedly" in message or "taking too long" in message):
                sec.stop_play.add(t, message)
    return [s for s in sections if s.first_t is not None]


def on_error(sec: Section, t, level, logger, message):
    if CUSTOM_CLASS_WORDS.search(message):
        sec.err_custom_class.add(t, message)
    if level == "FTL":
        sec.stop_play.add(t, message)
        return
    if message.startswith("Critical error encountered in "):
        service = message[len("Critical error encountered in "):].split(":", 1)[0]
        if service in ("GameLoop", "GameLoopTickPacer"):
            sec.stop_play.add(t, message)
        else:
            sec.pending_critical = (t, message)
            sec.err_after[f"{logger} (critical, {service})"] += 1
        return
    if any(logger == lg and message.startswith(text) for lg, text in KNOWN_STARTUP_ERRORS):
        sec.err_known += 1
    elif sec.startup_completed is None:
        sec.err_before += 1
    else:
        sec.err_after[logger] += 1


# ---- Handlers (one per log tag) ----------------------------------------------------------------------------------

HANDLERS = {}


def handles(*tags):
    def register(fn):
        for tag in tags:
            HANDLERS[tag] = fn
        return fn
    return register


def simple(*tags):
    """Tags that only need a count and the first line."""
    def fn(sec, t, message, level, near):
        sec.ev[message.split(" ", 1)[0]].add(t, message)
    for tag in tags:
        HANDLERS[tag] = fn


simple("CUSTOM_CLASS_REFUSED", "REALM_RAID_TALE_FIRST", "REALM_RAID_TALE",
       "REALM_EXPEDITION_MUSTER_GUARDED", "REALM_EXPEDITION_MUSTER_UNSAFE", "REALM_EXPEDITION_MUSTER_MOVED",
       "EPIC_SWEEP_ADD", "EPIC_SWEEP_RAID_START", "EPIC_SWEEP_RAID_DONE", "EPIC_SWEEP_RAID_NOT_STARTED",
       "EPIC_SWEEP_HELD", "RVR_SPOT", "RVR_ROAM_CHECK", "RVR_ROAM_HUNT", "RVR_ROAM_MOVE_ON", "RVR_LANE_FALLBACK",
       "RVR_LANE_REJECTED", "RVR_ASSIST_CALL", "RVR_ASSIST_SWITCH", "RVR_CC_ASSIGN", "RVR_INTERRUPT", "RVR_PEEL",
       "RVR_KITE", "RVR_STEALTH_OPEN", "RVR_PARTY_FORMED", "CRAFT_SESSION_START", "ECON_SUPPLY",
       "HIRE_WORD_165", "HIRED_COMPANION", "HIRED_COMPANION_SUMMONED", "HIRED_COMPANION_DISMISSED",
       "HIRED_COMPANION_REVIVED", "HIRED_COMPANION_RELEASED", "HIRED_COMPANION_ORPHAN",
       "HIRED_COMPANION_DELETE_FAILED", "BOT_PEOPLE_SUMMARY", "BOT_SKILL_BUDGET",
       "BOT_SKILL_GROWTH", "RAID_MECHANIC_SEEN", "BOT_PLACE_BUDGET", "BOT_WAY_FOLLOW",
       "BOT_WAY_SUMMARY", "BOT_MOVE_STRAFE", "BOT_MOVE_STICK_LOST", "BOT_MOVE_OVERSHOOT",
       "BOT_MOVE_SIT", "BOT_MOVE_WALL", "BOT_MILESTONE_ARC", "BOT_MILESTONE_PROGRESS", "BOT_MILESTONE_REACHED",
       "BOT_MILESTONE_SUMMARY", "ERA_BUILDS_INVALID", "REALM_EXPEDITION_TRAIL_GATE_PASSED")


# Bots as people WP6: what a milestone line may claim, from the server's own tables (BotMilestones): experience from
# level 45 to 50 (and where level 49 starts, for the bubbles), and realm rank 5's realm points.
DING_FROM_XP, DING_49_XP, DING_TO_XP, RR5_POINTS = 53_999_999_950, 137_999_999_950, 169_999_999_950, 513_500
TROPHY_TALK_KILLS = {50: 2, 90: 5}


def milestone_bubbles_left(level: float, xp: float):
    """Bubbles left to 50 for a level 49 (1-10, as BotMilestones.BubblesLeft), else None (pure)."""
    if int(level) != 49:
        return None
    left = min(1.0, max(0.0, (DING_TO_XP - xp) / (DING_TO_XP - DING_49_XP)))
    return min(10, max(1, math.ceil(left * 10 - 1e-9)))


def milestone_claim_true(f: dict) -> bool:
    """Whether a BOT_MILESTONE_TALK line says only what the bot's logged numbers back (pure): its talk point against
    xp= / rp= / count=, the slot values the server filled (bubbles=, left_k=) against the same numbers, and the number in
    the line itself. The typing styles write "to" as 2 and "for" as 4, so a line is only asked to contain its number,
    never to contain no other."""
    arc, point, topic = f.get("arc"), num(f.get("point")), f.get("topic", "")
    line = f.get("line", "")
    for name in (f.get("trophy"), f.get("source")):
        if name:
            line = line.replace(name, " ")
    tokens = re.findall(r"\d+k?", line)
    if arc == "rr5":
        rp = num(f.get("rp"))
        if rp < RR5_POINTS * min(point, 100) / 100 - 0.5:
            return False
        left = 0 if rp >= RR5_POINTS else max(1, math.ceil((RR5_POINTS - rp) / 1000))
        if "left_k" in f and int(num(f["left_k"])) != left:
            return False
        return topic != "rr5_close" or all(int(t[:-1]) == left for t in tokens if t.endswith("k"))
    if arc == "ding50":
        level, xp = num(f.get("level")), num(f.get("xp"))
        if not (level >= 50 or xp - DING_FROM_XP >= (DING_TO_XP - DING_FROM_XP) * point / 100 - 1):
            return False
        bubbles = milestone_bubbles_left(level, xp)
        if bubbles is not None and "bubbles" in f and int(num(f["bubbles"])) != bubbles:
            return False
        return topic != "ding50_bubbles" or (bubbles is not None and str(bubbles) in tokens)
    if arc == "dragon_trophy":
        if point >= 100:
            return bool(f.get("trophy")) and bool(f.get("source"))
        count = int(num(f.get("count")))
        if count < TROPHY_TALK_KILLS.get(int(point), 10 ** 9):
            return False
        return topic != "trophy_long" or str(count) in tokens
    return False


@handles("BOT_MILESTONE_TALK")
def _milestone_talk(sec, t, message, level, near):
    sec.ev["BOT_MILESTONE_TALK"].add(t, message)
    if not milestone_claim_true(kv(message)):
        sec.ev["milestone.false_claim"].add(t, message)


@handles("REALM_RAID_STARTED")
def _raid_started(sec, t, message, level, near):
    f = kv(message)
    event = f.get("event", "?")
    run = RaidRun(event)
    sec.raids.append(run)
    sec.open_raids[event] = run
    run.started = t
    run.present, run.required, run.forced = f.get("present"), f.get("required"), f.get("forced")
    region = sec.region_events.get(event)
    if region is not None:   # a new raid in this region: engages of an earlier one do not count for it
        close_streak(sec, str(region))
        for table in (sec.engage_lines, sec.engaged, sec.add_owner):
            table.pop(str(region), None)
    sec.ev["REALM_RAID_STARTED"].add(t, message)
    sec.n[f"raid_starts.{event}"] += 1
    sec.n["raid_starts"] += 1


@handles("REALM_RAID_ENDED")
def _raid_ended(sec, t, message, level, near):
    f = kv(message)
    event = f.get("event", "?")
    run = sec.raid(event, t)
    run.ended, run.outcome = t, f.get("outcome")
    run.minutes, run.named = f.get("minutes"), f.get("named")
    if f.get("wipes") is not None:
        run.wipes = max(run.wipes, int(num(f.get("wipes"))))
    sec.open_raids.pop(event, None)
    for key in [k for k in sec.rebases if k[0] == event]:
        close_rebase(sec, key, t, f"the raid ended at {stamp(t)[6:]}")
    outcome = f.get("outcome", "")
    end = {"t": t, "event": event, "members": {}, "regions": set()}
    # The raid's dungeon and the one its way in leads through (sweep 6e: Summoner's Hall's Hall of the Corrupt, Crystal
    # Cave's Avalon City; sweep 6d judged the raid's Definition.Region only).
    end["regions"].update(raid_regions(sec, event))
    for key in [k for k in sec.attempts if str(k[0]) in end["regions"]]:
        close_attempt(sec, *key)
    for region in end["regions"]:
        close_streak(sec, region)
    waiting = sec.recent_outcomes.pop(event, ())
    for seen, members in waiting:
        if abs((t - seen).total_seconds()) <= END_OUTCOME_SECONDS:
            end["members"].update(members)
    sec.ends.append(end)
    sec.ev["REALM_RAID_ENDED"].add(t, message)
    if outcome == "Boss defeated":
        sec.ev["raid.won"].add(t, message)
    elif outcome == "Defeated":
        sec.ev["raid.lost"].add(t, message)
    elif outcome == "Timed out":
        sec.ev["raid.timed_out"].add(t, message)


@handles("REALM_RAID_WIPE")
def _raid_wipe(sec, t, message, level, near):
    f = kv(message)
    run = sec.raid(f.get("event", "?"), t)
    run.wipes = max(run.wipes + 1, int(num(f.get("failures"))))
    if f.get("boss"):
        run.wipe_bosses.append(f["boss"])
    sec.ev["REALM_RAID_WIPE"].add(t, message)


@handles("REALM_RAID_STALLED")
def _raid_stalled(sec, t, message, level, near):
    run = sec.raid(kv(message).get("event", "?"), t)
    run.stalled += 1
    sec.ev["REALM_RAID_STALLED"].add(t, message)


@handles("REALM_RAID_RETURN_WAVE")
def _return_wave(sec, t, message, level, near):
    """The gather-and-go-in claim (#195) and stuck waves are told apart: a stuck wave is its own row, and one that makes
    headway again afterwards (moving, ahead, cleared, met, passable) counts as recovered."""
    f = kv(message)
    step = f.get("step")
    key = (f.get("event", "?"), f.get("wave"))
    if f.get("wave"):
        rebase_silence(sec, t, f, step, key, message)
    wave_step(sec, t, message, key, step, f.get("at") or "")
    if f.get("wave"):
        wave_headway(sec, t, f, step, key)
    if step == "released":
        sec.ev["return.released"].add(t, message)
        if sec.waves.get(key, ("",))[0] == "stuck":
            sec.waves[key] = ("released", t, message)
        return
    if step == "stuck":
        run = sec.raid(f.get("event", "?"), t)
        run.stuck_waves += 1
        sec.ev["return.stuck"].add(t, message)
        sec.waves[key] = ("stuck", t, message)
        return
    if step in RETURN_WAVE_HEADWAY and sec.waves.get(key, ("",))[0] == "stuck":
        sec.waves[key] = ("recovered", t, message)
    if step in ("formed", "moving", "joined") or "waited_s" in f:
        sec.ev["return.wave"].add(t, message)
    if step in ("moving", "ahead", "cleared", "met"):
        sec.ev["return.went_in"].add(t, message)


def rebase_silence(sec, t, f, step, key, message):
    """W-2 (sweep 6f): a rebase to a step none of the new core stands at is held open until the wave's next act of its own
    (REBASE_ACTS); REBASE_SILENT_SECONDS or more without one is a silent rebase."""
    if step not in REBASE_ACTS:
        return
    if key in sec.rebases:
        close_rebase(sec, key, t, f"step={step} at {stamp(t)[6:]}")
    if step == "rebased":
        sec.ev["return.rebased"].add(t, message)
        standing = f.get("standing")
        if standing is None or num(standing) <= 0:
            sec.rebases[key] = (t, message)


def close_rebase(sec, key, t, why):
    since, line = sec.rebases.pop(key)
    seconds = (t - since).total_seconds()
    if seconds >= REBASE_SILENT_SECONDS:
        sec.silent_rebases.append((since, line, key, seconds / 60, why))


WAVE_STEP_RE = re.compile(r"(\d+)/\d+$")


def wave_step(sec, t, message, key, step, at):
    """A wave whose at= step stays the same for WAVE_STEP_STILL_SECONDS did not move (sweep 6c): its joins, "ahead" lines
    (raiders leaving it) and catch-ups are not the wave's own headway. A wave's end, or a new wave of that number, closes it."""
    if step == "formed" or step in ("ended", "lost", "cleared", "met", "released"):
        sec.wave_steps.pop(key, None)
        return
    m = WAVE_STEP_RE.fullmatch(at)
    if not m:
        return
    n = int(m.group(1))
    held = sec.wave_steps.get(key)
    if held is None or held[0] != n:
        sec.wave_steps[key] = [n, t, message, False]
        return
    if not held[3] and (t - held[1]).total_seconds() >= WAVE_STEP_STILL_SECONDS:
        held[3] = True
        minutes = (t - held[1]).total_seconds() / 60
        sec.ev["return.step_still"].add(held[1], f"{held[2].rstrip()} [the same step to {stamp(t)[6:]}, {minutes:.0f} min]")
        sec.lists["still_waves"].append((key, n, held[1]))


@handles("AUTONOMOUS_TETHER_LEASH")
def _tether_leash(sec, t, message, level, near):
    """A boss leashed FROZEN_LEASH_RUN times in a row from about the same distance never went home (sweep 6c, build
    b1b6846: the Earth primal, 13 leashes at fromSpawn=1044 while his script held his speed at 0)."""
    f = kv(message)
    key, distance = (f.get("region"), f.get("boss")), num(f.get("fromSpawn"), -1)
    if f.get("boss") and f.get("tether"):
        sec.tethers[f["boss"]] = f["tether"]
    sec.ev["tether.leash"].add(t, message)
    run = sec.leash_runs.get(key)
    if run is None or abs(run[0] - distance) > FROZEN_LEASH_UNITS:
        sec.leash_runs[key] = [distance, 1, t, message, False]
        return
    run[1] += 1
    if run[1] >= FROZEN_LEASH_RUN and not run[4]:
        run[4] = True
        sec.ev["tether.frozen"].add(run[2], f"{run[3].rstrip()} [leashed {FROZEN_LEASH_RUN}+ times in a row from about here]")
    if run[4]:
        label = f"{key[1]} at fromSpawn={int(run[0])}"
        sec.frozen_runs[label] = max(sec.frozen_runs.get(label, 0), run[1])


@handles("REALM_EXPEDITION_TARGET_SWITCH")
def _target_switch(sec, t, message, level, near):
    f = kv(message)
    if f.get("approach") == "raw" and f.get("front"):
        sec.raw_fronts.setdefault((f.get("region"), f["front"]), (t, message))


@handles("REALM_EXPEDITION_ROUTE_RETRY")
def _route_retry(sec, t, message, level, near):
    f = kv(message)
    sec.retries_at[(f.get("region"), f.get("position"))] += 1


@handles("REALM_EXPEDITION_ENCOUNTER_RESET")
def _encounter_reset(sec, t, message, level, near):
    f = kv(message)
    close_attempt(sec, f.get("region"), f.get("target"))
    if "no attempt is used" in message:
        sec.ev["raid.reset_no_attempt"].add(t, message)
    else:
        sec.ev["raid.reset_other"].add(t, message)


# ---- Sweep 6e (build b1b6846): Aidon's tether heals, an attempt open across a reset, epic trash on the way in,
# musters set aside while a wave walked to them -----------------------------------------------------------------------

def raid_regions(sec, event) -> set:
    """A raid's own dungeon and the one its way in leads through, as region strings."""
    regions = set()
    if sec.region_events.get(event) is not None:
        regions.add(str(sec.region_events[event]))
    for table in (RAID_DUNGEON_REGIONS, WAY_IN_REGIONS, sec.event_way_in):
        if table.get(event):
            regions.add(str(table[event]))
    return regions


def way_in_of(sec, event):
    return sec.event_way_in.get(event) or WAY_IN_REGIONS.get(event)


def event_in_region(sec, region):
    """The running raid whose own dungeon is `region`."""
    for event in sec.open_raids:
        own = sec.region_events.get(event)
        if str(own if own is not None else RAID_DUNGEON_REGIONS.get(event)) == str(region):
            return event
    return None


def open_attempt(sec, t, region, boss):
    if region is not None and boss:
        sec.attempts[(region, boss)] = {"t": t, "lowest": 100.0, "reset": None, "deaths": [], "boss": boss,
                                        "region": region}


def close_attempt(sec, region, boss):
    attempt = sec.attempts.pop((region, boss), None)
    if attempt and attempt["reset"] is not None:
        sec.reset_attempts.append(attempt)


def attempt_health(sec, t, region, boss, health, message):
    attempt = sec.attempts.get((region, boss))
    if attempt is None:
        return
    if attempt["reset"] is None and attempt["lowest"] <= RESET_LOW and health >= RESET_HEALTH:
        attempt["reset"] = (t, message)
    attempt["lowest"] = min(attempt["lowest"], health)


def tether_heal(sec, t, f, message):
    """S-1: a boss healed to 95%+ while fighting the raid, past its fight-at-home radius (or its tether)."""
    boss = f.get("target")
    home, tether = boss_limits().get(boss, (0, 0))
    tether = int(num(sec.tethers.get(boss), 0)) or tether
    limit = home or tether
    healed_from, healed_to, distance = num(f.get("from"), 100), num(f.get("to")), num(f.get("fromSpawn"), -1)
    if (limit and f.get("inCombat") == "True" and healed_to >= RESET_HEALTH and healed_to - healed_from >= 5
            and distance > limit):
        sec.tether_heals.append((t, f"REALM_EXPEDITION_TARGET_HEALED region={f.get('region')} target=\"{boss}\" "
                                    f"from={f.get('from')} to={f.get('to')} fromSpawn={f.get('fromSpawn')} "
                                    f"[past {'its home ' + str(home) if home else 'its tether'}"
                                    f"{', tether ' + str(tether) if home and tether else ''}]"))


@handles("AUTONOMOUS_TETHER_RESET")
def _tether_reset(sec, t, message, level, near):
    f = kv(message)
    if f.get("boss") and f.get("tether"):
        sec.tethers[f["boss"]] = f["tether"]


@handles("REALM_EXPEDITION_SKIRMISH_LOST")
def _skirmish_lost(sec, t, message, level, near):
    f = kv(message)
    close_attempt(sec, f.get("region"), f.get("target"))


@handles("AUTONOMOUS_DUNGEON_EPIC_AVOIDED")
def _epic_avoided(sec, t, message, level, near):
    f = kv(message)
    if f.get("region") is not None and f.get("target"):
        sec.avoided[str(f["region"])].add(f["target"])


@handles("REALM_EXPEDITION_MUSTER_STALLED")
def _muster_stalled(sec, t, message, level, near):
    """T-1: a muster set aside as "gained nobody" while a return wave of the raid with headway in the last
    WAVE_HEADWAY_SECONDS was walking in (the wave walks to the route's destination: the muster)."""
    f = kv(message)
    sec.ev["muster.stalled"].add(t, message)
    event = event_in_region(sec, f.get("region"))
    if event is None:
        return
    moving = [(key, (t - h[0]).total_seconds()) for key, h in sec.wave_headway.items()
              if key[0] == event and not h[2] and (t - h[0]).total_seconds() <= WAVE_HEADWAY_SECONDS]
    if moving:
        key, ago = min(moving, key=lambda m: m[1])
        sec.stall_waves.append((t, message, event, key[1], ago))


simple("REALM_EXPEDITION_MUSTER_WAITS_FOR_WAVE")


def wave_headway(sec, t, f, step, key):
    if step == "formed":
        sec.wave_headway[key] = [t, 0, False]
        return
    held = sec.wave_headway.get(key)
    if held is None:
        if step in ("fighting", "moving", "passable", "ahead", "joined"):
            held = sec.wave_headway[key] = [t, 0, False]
        else:
            return
    if step in ("ended", "lost", "met", "released"):
        held[2] = True
        return
    kills = int(num(f.get("kills"), held[1]))
    if step in ("moving", "passable", "ahead", "cleared") or kills > held[1]:
        held[0] = t
    held[1] = max(held[1], kills)


def engage(sec, region, boss, t):
    if region is not None and boss:
        start, _ = sec.engaged[region].get(boss, (t, t))
        sec.engaged[region][boss] = (start, t)


def disengage(sec, region, boss):
    """The fight with a boss ended (its wipe, defeat or heal). A TARGET_STATUS from before that fight began says nothing
    about it now, so it is dropped: the boss counts as resting again from its next status line."""
    if region is None or not boss:
        return
    fight = sec.engaged[region].pop(boss, None)
    status = sec.bosses.get(region, {}).get(boss)
    if fight and status and status[3] < fight[0]:
        sec.bosses[region].pop(boss, None)


def engaged_now(sec, region, boss, t) -> bool:
    fight = sec.engaged.get(region, {}).get(boss)
    return fight is not None and (t - fight[1]).total_seconds() <= ENGAGED_LAPSE_SECONDS


def pull_without_engage(sec, t, region, boss, message):
    """A pull of a mustered encounter that logged no FINAL_ENGAGE or EARLY_PULL for it (DIAG-5378-galladoria E)."""
    if region is None or not boss or boss in sec.engage_lines.get(region, {}) or (region, boss) in sec.no_engage:
        return
    sec.no_engage[(region, boss)] = t
    sec.ev["pull.no_engage"].add(t, message)


@handles("REALM_EXPEDITION_FINAL_ENGAGE")
def _final_engage(sec, t, message, level, near):
    f = kv(message)
    if num(f.get("mustered")) >= num(f.get("needed")):
        sec.ev["pull.at_needed"].add(t, message)
    else:
        sec.ev["pull.below_needed"].add(t, message)
    engage(sec, f.get("region"), f.get("target"), t)
    if f.get("region") is not None and f.get("target"):
        sec.engage_lines[f["region"]][f["target"]] = t
    open_attempt(sec, t, f.get("region"), f.get("target"))
    boss = f.get("target")
    if boss:
        close_retry(sec, boss)
        if f.get("retry") == "True" and boss in sec.boss_adds:
            sec.retry_watch[boss] = [t, message, False]


# ---- A retry of a boss with adds (sweep 6b, build b1b6846: Fornfrusenen's retry raised no shards) ------------------

ADD_OF_RE = re.compile(r'required-adds? of "([^"]+)"')


def add_seen(sec, boss, add=None):
    """The raid fought (or saw raised) an add of `boss`: it has adds, and an open retry on it has them too."""
    if not boss:
        return
    if add and add != boss:
        sec.boss_adds[boss].add(add)
    elif boss not in sec.boss_adds:
        sec.boss_adds[boss] = set()
    watch = sec.retry_watch.get(boss)
    if watch is not None:
        watch[2] = True


def close_retry(sec, boss):
    """The retry on `boss` ended (its defeat, wipe, attempt end or next engage): one with no add line is a finding."""
    watch = sec.retry_watch.pop(boss, None)
    if watch is None:
        return
    sec.ev["retry.with_adds" if watch[2] else "retry.no_adds"].add(watch[0], watch[1])


@handles("LORE_LINE")
def _lore_line(sec, t, message, level, near):
    f = kv(message)
    if f.get("trigger") == "addsSpawn":
        add_seen(sec, f.get("boss"))


@handles("EPIC_TACTIC_BOT")
def _tactic_bot(sec, t, message, level, near):
    f = kv(message)
    boss, to = f.get("target"), f.get("to")
    if boss and to and to != boss and f.get("action") in ("Retarget", "Reposition"):
        add_seen(sec, boss, to)
    owner = REQUIRED_ADD_RE.search(message)
    if owner and to:
        sec.required_adds[owner.group(1)].add(to)
        sec.epic_names.add(owner.group(1))


@handles("REALM_EXPEDITION_ATTEMPT_ENDED")
def _attempt_ended(sec, t, message, level, near):
    """An attempt that never entered combat while ten or more of the raid fell was a fight the route did not see
    (sweep 6b, build b1b6846: Aroon's three attempts ended between his Scaiths with 14, 35 and 19 fallen)."""
    f = kv(message)
    if f.get("how") == "never entered combat" and num(f.get("fallen")) >= NEVER_ENTERED_FALLEN:
        sec.ev["attempt.never_entered_fallen"].add(t, message)
    else:
        sec.ev["attempt.ended"].add(t, message)
    close_retry(sec, f.get("target"))
    close_attempt(sec, f.get("region"), f.get("target"))


@handles("REALM_EXPEDITION_EARLY_PULL")
def _early_pull(sec, t, message, level, near):
    f = kv(message)
    if f.get("region") is not None and f.get("target"):
        sec.engage_lines[f["region"]][f["target"]] = t
    if f.get("decision") == "Commit":
        engage(sec, f.get("region"), f.get("target"), t)
        if (f.get("region"), f.get("target")) not in sec.attempts:
            open_attempt(sec, t, f.get("region"), f.get("target"))


@handles("REALM_EXPEDITION_TARGET_CALLED")
def _target_called(sec, t, message, level, near):
    f = kv(message)
    engage(sec, f.get("region"), f.get("target"), t)


@handles("EPIC_FIGHT_LEDGER")
def _fight_ledger(sec, t, message, level, near):
    """Every NPC named in raid_dealt (the raid hit it) or raid_took (it hit the raid) is in the fight this minute."""
    f = kv(message)
    region = f.get("region")
    target = f.get("target")
    adds = sec.boss_adds.get(target) or ()
    for field in ("raid_dealt", "raid_took"):
        for entry in (f.get(field) or "").split(","):
            name = entry.rsplit(":", 1)[0].strip()
            if name and ":" in entry:
                engage(sec, region, name, t)
                if name in adds:
                    add_seen(sec, target, name)
    no_progress_minute(sec, t, region, f.get("raid_dealt") or "")
    dot_fight_minute(sec, t, region, f.get("raid_took") or "", message)


def dot_fight_minute(sec, t, region, took_field, message):
    """Sweep 7b: a minute in which a curable raid-wide DoT boss (CURABLE_DOT_BOSSES) hit the raid."""
    for entry in took_field.split(","):
        name, sep, _ = entry.rpartition(":")
        boss = name.strip()
        if not sep or boss not in CURABLE_DOT_BOSSES:
            continue
        last = next((f for f in reversed(sec.dot_fights) if f[0] == region and f[1] == boss), None)
        if last is not None and (t - last[3]).total_seconds() <= CURE_FIGHT_GAP_SECONDS:
            last[3] = t
        else:
            sec.dot_fights.append([region, boss, t, t, message])


@handles("RAID_CURE_POISON")
def _raid_cure_poison(sec, t, message, level, near):
    f = kv(message)
    sec.ev["RAID_CURE_POISON"].add(t, message)
    sec.raid_cures.append((t, f.get("region"), int(num(f.get("casts"))), message))


def no_progress_minute(sec, t, region, dealt_field):
    """Sweep 6d (D-1): one ledger minute of each required-adds phase the raid hit; a phase with no add death for
    NO_PROGRESS_MINUTES while the raid dealt its adds more than their combined health (from the encounter catalog) is a
    finding. A gap of NO_PROGRESS_GAP_SECONDS with no damage on them, or an add's death, starts the phase again."""
    dealt = {}
    for entry in dealt_field.split(","):
        name, sep, rest = entry.rpartition(":")
        if sep and name.strip():
            dealt[name.strip()] = num(rest.split("/", 1)[0])
    if not dealt:
        return
    health = add_health()
    for boss, adds in sec.required_adds.items():
        on_adds = sum(v for n, v in dealt.items() if n in adds)
        if on_adds <= 0:
            continue
        key = (region, boss)
        phase = sec.add_phases.get(key)
        if phase is None or (t - phase[1]).total_seconds() > NO_PROGRESS_GAP_SECONDS:
            phase = sec.add_phases[key] = [t, t, 0.0, False]
        phase[1] = t
        phase[2] += on_adds
        if phase[3] or any(a not in health for a in adds):
            continue
        combined = sum(health[a] for a in adds)
        minutes = (t - phase[0]).total_seconds() / 60
        if minutes >= NO_PROGRESS_MINUTES and phase[2] > combined:
            phase[3] = True
            sec.ev["adds.no_progress"].add(
                t, f"EPIC_FIGHT_LEDGER region={region}: {phase[2]:,.0f} dealt on {combined:,} combined health "
                   f"(encounter catalog) in {minutes:.0f} min with no death of the {len(adds)} required adds of {boss} "
                   f"({', '.join(sorted(adds))})")
            sec.lists["no_progress_phases"].append((boss, region, combined, phase))


@handles("REALM_RAID_EPIC_RESPAWN_HELD")
def _respawn_held(sec, t, message, level, near):
    sec.ev["REALM_RAID_EPIC_RESPAWN_HELD"].add(t, message)
    npc = kv(message).get("npc")
    if npc:
        sec.epic_names.add(npc)


@handles("AUTONOMOUS_GROUP_OUTCOME")
def _group_outcome(sec, t, message, level, near):
    """A raid party's outcome when its raid ends ("camp":"realm-event-<event>"): who was in the raid and where each stood."""
    if '"camp":"realm-event-' not in message:
        return
    try:
        data = json.loads(message.split(" ", 1)[1])
    except (IndexError, ValueError):
        return
    event = (data.get("camp") or "")[len("realm-event-"):]
    members = {}
    for m in data.get("members") or []:
        if m.get("name"):
            members[m["name"]] = (bool(m.get("alive")), str(m.get("region")),
                                  f"{m.get('x')},{m.get('y')},{m.get('z')}")
    for end in reversed(sec.ends):
        if end["event"] == event and 0 <= (t - end["t"]).total_seconds() <= END_OUTCOME_SECONDS:
            end["members"].update(members)
            return
    queue = sec.recent_outcomes[event]
    queue.append((t, members))
    while queue and (t - queue[0][0]).total_seconds() > END_OUTCOME_SECONDS:
        queue.popleft()


@handles("REALM_RAID_END_WALKOUT")
def _end_walkout(sec, t, message, level, near):
    """Sweep 6d builds on: the regions a dungeon raid's end holds (its dungeon and its way in)."""
    f = kv(message)
    for end in reversed(sec.ends):
        if end["event"] == f.get("event") and abs((t - end["t"]).total_seconds()) <= END_OUTCOME_SECONDS:
            end["regions"].update(r for r in (f.get("regions") or "").split(",") if r)
            break


def end_regions(end) -> set:
    """The ended raid's region(s): known from its definition or its walk-out line, else where most of its raiders stood."""
    if end["regions"]:
        return end["regions"]
    where = Counter(region for alive, region, _ in end["members"].values() if alive)
    return {where.most_common(1)[0][0]} if where else set()


@handles("AUTONOMOUS_BOT_DEATH")
def _bot_death(sec, t, message, level, near):
    """Sweep 6d (P-1, P-2): a raider of an ended raid killed by a monster in that raid's region within AFTER_END_SECONDS;
    whether the killer is an epic is judged at the end (the names are learned over the whole log)."""
    if "killerKind=monster" not in message:
        return
    f = kv(message)
    region, killer = f.get("region"), f.get("killer") or ""
    # S-2: a death to the boss (or its adds) of an attempt open after its reset.
    for attempt in sec.attempts.values():
        if (attempt["reset"] is not None and str(attempt["region"]) == str(region) and
                (t - attempt["reset"][0]).total_seconds() > RESET_GRACE_SECONDS and
                (killer == attempt["boss"] or attempt["boss"] in killer or killer in sec.boss_adds.get(attempt["boss"], ()))):
            attempt["deaths"].append((t, message))
    # W-1: a death in a running raid's way-in region (judged at the end against the epics walked past there).
    for event in sec.open_raids:
        if region is not None and way_in_of(sec, event) == str(region):
            sec.way_in_deaths.append((t, killer, str(region), message, event))
            break
    if not sec.ends:
        return
    for end in reversed(sec.ends):
        since = (t - end["t"]).total_seconds()
        if since > AFTER_END_SECONDS:
            break
        if since < 0 or f.get("region") not in end_regions(end):
            continue
        if end["members"] and f.get("bot") not in end["members"]:
            continue
        sec.after_end_deaths.append((t, f.get("killer"), message, end))
        return


@handles("REALM_EXPEDITION_NO_PROGRESS")
def _no_progress(sec, t, message, level, near):
    """The server's own line for the same finding (sweep 6d builds on)."""
    f = kv(message)
    sec.ev["adds.no_progress"].add(t, message)
    sec.lists["no_progress"].append(f"{f.get('boss')} in region {f.get('region')} (REALM_EXPEDITION_NO_PROGRESS, "
                                    f"{f.get('minutes')} min, dealt {f.get('dealt')} on {f.get('health')} health)")


@handles("REALM_EXPEDITION_TACTIC")
def _tactic(sec, t, message, level, near):
    """A required-adds switch (`reason="required-adds of "<boss>" ..."`) means the boss's fight is on. Pulled with no
    FINAL_ENGAGE or EARLY_PULL for the boss, it is a pull the 70% row cannot see."""
    f = kv(message)
    region, boss, add = f.get("region"), f.get("from"), f.get("to")
    owner = ADD_OF_RE.search(message)
    if owner:
        add_seen(sec, owner.group(1), add)
    if not boss or "required-adds of" not in message:
        return
    engage(sec, region, boss, t)
    if add:
        sec.add_owner[region][add] = boss
    pull_without_engage(sec, t, region, boss, message)


@handles("REALM_EXPEDITION_WIPE")
def _expedition_wipe(sec, t, message, level, near):
    f = kv(message)
    region, target = f.get("region"), f.get("target")
    if f.get("how") == "raid-wide deaths outside an attempt":
        owner = sec.add_owner.get(region, {}).get(target, target)
        if target not in sec.engage_lines.get(region, {}):
            pull_without_engage(sec, t, region, owner, message)
    disengage(sec, region, target)
    close_retry(sec, target)
    close_attempt(sec, region, target)
    # A wipe scored on the boss of an add closes the boss's attempt too.
    for key in [k for k in sec.attempts if k[0] == region]:
        close_attempt(sec, *key)


@handles("REALM_EXPEDITION_TARGET_HEALED")
def _target_healed(sec, t, message, level, near):
    f = kv(message)
    attempt_health(sec, t, f.get("region"), f.get("target"), num(f.get("to")), message)
    tether_heal(sec, t, f, message)
    disengage(sec, f.get("region"), f.get("target"))


@handles("REALM_EXPEDITION_ROUTE_GATE")
def _route_gate(sec, t, message, level, near):
    f = kv(message)
    if f.get("event") and f.get("into"):
        sec.region_events[f["event"]] = int(num(f["into"]))
    if f.get("event") and f.get("region") is not None:
        sec.event_way_in[f["event"]] = str(f["region"])
    for boss in (f.get("bosses") or "").split(","):
        if "@" in boss:
            sec.gate_bosses.add(boss.split("@", 1)[0])
    if "a living boss covers the walk" in message:
        sec.ev["route.boss_objective"].add(t, message)


@handles("REALM_EXPEDITION_TARGET_STATUS")
def _target_status(sec, t, message, level, near):
    f = kv(message)
    region, target, at = f.get("region"), f.get("target"), point(f.get("at"))
    if region is None or target is None or at is None:
        return
    sec.bosses[region][target] = (f.get("inCombat") == "True", num(f.get("health"), 100.0), at, t, f.get("level"))
    attempt_health(sec, t, region, target, num(f.get("health"), 100.0), message)


def close_streak(sec, region):
    """Ends a region's run of kills of one target; REKILL_LOOP or more re-kills (kills after the first) is a loop."""
    streak = sec.kill_streaks.pop(region, None)
    if streak and streak[1] - 1 >= REKILL_LOOP:
        target, kills, first, line, last, event = streak
        sec.rekill_loops.append((first, line, region, target, kills, last, event))


def kill_streak(sec, t, region, target, message):
    streak = sec.kill_streaks.get(region)
    if streak and streak[0] == target:
        streak[1] += 1
        streak[4] = t
        return
    close_streak(sec, region)
    sec.kill_streaks[region] = [target, 1, t, message, t, event_in_region(sec, region)]


@handles("REALM_EXPEDITION_TARGET_DEFEATED")
def _target_defeated(sec, t, message, level, near):
    f = kv(message)
    sec.ev["REALM_EXPEDITION_TARGET_DEFEATED"].add(t, message)
    if f.get("region") is not None and f.get("target"):
        kill_streak(sec, t, str(f["region"]), f["target"], message)
    sec.bosses[f.get("region")].pop(f.get("target"), None)
    disengage(sec, f.get("region"), f.get("target"))
    sec.attempts.pop((f.get("region"), f.get("target")), None)
    close_retry(sec, f.get("target"))
    for boss, adds in sec.required_adds.items():
        if f.get("target") in adds:
            sec.add_phases.pop((f.get("region"), boss), None)


@handles("REALM_EXPEDITION_STRANDED_REGROUP")
def _stranded(sec, t, message, level, near):
    f = kv(message)
    region, to = f.get("region"), point(f.get("to"))
    sec.ev["stranded.all"].add(t, message)
    sec.regroups_to[(region, f.get("to"))] += 1
    if to is None:
        return
    for target, (in_combat, health, at, seen, lvl) in sec.bosses.get(region, {}).items():
        if in_combat or (t - seen).total_seconds() > STATUS_STALE_SECONDS or engaged_now(sec, region, target, t):
            continue
        distance = math.dist(to, at)
        if distance <= near:
            sec.ev["stranded.near_resting"].add(
                t, f"REALM_EXPEDITION_STRANDED_REGROUP bot={f.get('bot')} region={region} to={f.get('to')}: "
                   f"{distance:.0f} from resting {target} (health {health:.0f}%, status at {stamp(seen)[6:]})")
            event = sec.running_event_in(int(num(region)))
            if event and event in sec.open_raids:
                sec.open_raids[event].teleports_near_resting += 1
            sec.lists["teleport_regions"].append((region, target))
            break


@handles("REALM_RAID_SCHEDULED", "REALM_RAID_SIZED")
def _raid_schedule(sec, t, message, level, near):
    tag = message.split(" ", 1)[0]
    sec.ev[tag].add(t, message)
    if tag == "REALM_RAID_SCHEDULED":
        f = kv(message)
        if f.get("online"):
            sec.lists["online"].append((t, f.get("realm"), num(f["online"])))


@handles("REALM_RAID_CALLED")
def _raid_called(sec, t, message, level, near):
    sec.ev["REALM_RAID_CALLED"].add(t, message)
    if 'after="the tale was told"' in message:
        sec.ev["raid.called_after_tale"].add(t, message)


@handles("EPIC_SWEEP_START", "EPIC_SWEEP_END")
def _sweep(sec, t, message, level, near):
    tag = message.split(" ", 1)[0]
    sec.ev[tag].add(t, message)
    f = kv(message)
    if tag == "EPIC_SWEEP_START":
        sec.lists["sweep_start"].append(f"{stamp(t)[6:]} lanes={f.get('lanes')} cap={f.get('cap')} raids={f.get('raids')}")
    else:
        sec.lists["sweep_end"].append(f"{stamp(t)[6:]} why={f.get('why')} results={f.get('results')}")


@handles("RVR_REGROUP", "RVR_RETREAT")
def _rvr_stance(sec, t, message, level, near):
    """The regroup row's criterion (PLAYED-CHECK-1.2.md, corrected 2026-11-17): one hop, no echo. #304 lets a group
    relay its own retreat or its own regroup once; a relayed stance is never passed on again.

    (c), a relay back at its sender: a relayed start names its sender (`from=<group>`, logged since the fix of
    DIAG-5378-misc 1). It is an echo when the sender passed on a stance that this group's own relay had started
    (a relayed stance, or the regroup ending a relayed retreat). Lines without from= (earlier builds) fall back to the
    old pattern, which is not proof of an echo: in a fight of several groups every group's own retreat reaches the
    others (DIAG-5378-misc 1), so it is reported as relays among the groups of one fight and does not contradict."""
    regroup = message.startswith("RVR_REGROUP ")
    f = kv(message)
    if regroup and f.get("phase") != "start":
        # a relayed regroup that contact ended within a second: a start that did nothing (DIAG-5378-misc 1)
        if (f.get("phase") == "end" and (f.get("started") or "").startswith("relayed") and
                (f.get("reason") or "").startswith("contact while regrouping") and 0 <= num(f.get("seconds"), -1) <= 1):
            sec.ev["regroup.relay_flash"].add(t, message)
        return
    group, reason = f.get("group", "?"), f.get("reason") or ""
    where = (f.get("realm"), f.get("region"))
    relayed = reason.startswith("relayed")
    sends = f.get("relay") == "sent" or num(f.get("relayedTo")) > 0
    reached = num(f.get("relayedTo")) > 0
    sender = f.get("from") if relayed else None
    if regroup:
        sec.ev["RVR_REGROUP"].add(t, message)
        if relayed:
            sec.ev["regroup.relay_started"].add(t, message)
    # (a) a relayed stance that relays again
    if relayed and f.get("relay") == "sent":
        sec.ev["regroup.a_relayed_sends"].add(t, message)
    # (b) the regroup that ends this group's relayed retreat relays again (the echo source)
    if regroup and reason == "retreat over" and sends and sec.last_retreat.get(group) == "relayed retreat":
        sec.ev["regroup.b_echo_source"].add(t, message)
    # (c) a relay back at its sender within 60 s
    if relayed and sender is not None:
        sec.n["relay.named"] += 1
        origin = sec.send_origin.get(sender)
        if origin and origin[1] == group and 0 <= (t - origin[0]).total_seconds() <= 60:
            sec.ev["regroup.c_returned"].add(t, f"{message.rstrip()} [sender {sender} passed on a stance group {group}'s "
                                                f"relay started, at {stamp(origin[0])[6:]}]")
    elif relayed:
        mine = [s for s in sec.relay_sends.get(group, ()) if 0 < (t - s).total_seconds() <= 60]
        if mine:
            first = min(mine)
            if any(g != group and w == where and first < s and (t - s).total_seconds() <= 15
                   for s, g, w in sec.recent_sends):
                sec.ev["regroup.c_one_fight"].add(t, f"{message.rstrip()} [group {group} relayed at {stamp(first)[6:]}]")
    if reached:
        if relayed:
            started_by = sender
        elif regroup and reason == "retreat over" and sec.last_retreat.get(group) == "relayed retreat":
            started_by = sec.last_retreat_from.get(group)
        else:
            started_by = None
        sec.send_origin[group] = (t, started_by)
        sec.relay_sends.setdefault(group, []).append(t)
        sec.relay_sends[group] = [s for s in sec.relay_sends[group] if (t - s).total_seconds() <= 60]
        sec.recent_sends.append((t, group, where))
        while sec.recent_sends and (t - sec.recent_sends[0][0]).total_seconds() > 60:
            sec.recent_sends.popleft()
    if not regroup:
        sec.last_retreat[group] = reason
        sec.last_retreat_from[group] = sender


@handles("RVR_NOTICE")
def _rvr_notice(sec, t, message, level, near):
    f = kv(message)
    kind = f.get("kind", "?")
    sec.n[f"notice.{kind}"] += 1
    if kind == "EnemyBot":
        sec.ev["notice.enemybot"].add(t, message)
        if f.get("delayMs") is not None and len(sec.lists["notice_delay"]) < 200000:
            sec.lists["notice_delay"].append(num(f["delayMs"]))
    elif kind == "Player":
        sec.ev["notice.player"].add(t, message)


@handles("RVR_BOT_KILLED")
def _rvr_killed(sec, t, message, level, near):
    sec.ev["RVR_BOT_KILLED"].add(t, message)


@handles("RVR_LANE_ASSIGN")
def _lane_assign(sec, t, message, level, near):
    sec.ev["RVR_LANE_ASSIGN"].add(t, message)


@handles("RVR_SIEGE_STARTED")
def _siege(sec, t, message, level, near):
    sec.ev["RVR_SIEGE_STARTED"].add(t, message)
    if "the marching army reached the keep" in message:
        sec.ev["siege.march_arrived"].add(t, message)


@handles("RVR_KEEP_TAKEN")
def _keep_taken(sec, t, message, level, near):
    f = kv(message)
    sec.ev["keep.taken.bg" if f.get("battleground") == "True" else "keep.taken.field"].add(t, message)


@handles("RVR_CHECKSPOT_LOADED", "RVR_CHECKSPOT_VALIDATED", "RVR_LANE_LOADED")
def _catalogs(sec, t, message, level, near):
    tag = message.split(" ", 1)[0]
    f = kv(message)
    sec.ev[tag].add(t, message)
    bad = (f.get("ok") == "False" or num(f.get("failed")) > 0 or num(f.get("parseRejected")) > 0)
    if bad:
        sec.ev["catalog.bad"].add(t, message)
    if tag == "RVR_LANE_LOADED":
        sec.lists["lanes"].append(f"lanes={f.get('lanes')} validated={f.get('validated')} rejected={f.get('rejected')}")


@handles("CRAFT_SESSION_END")
def _craft_end(sec, t, message, level, near):
    f = kv(message)
    sec.ev["CRAFT_SESSION_END"].add(t, message)
    if (f.get("reason") or "").startswith("Finished at the station"):
        sec.ev["craft.finished"].add(t, message)
    sec.n["craft.made"] += int(num(f.get("made")))


@handles("ECON_ORDER")
def _order(sec, t, message, level, near):
    f = kv(message)
    outcome = f.get("outcome", "?")
    sec.n[f"order.{outcome}"] += 1
    if outcome == "made":
        sec.ev["order.made"].add(t, message)
    elif outcome == "expired":
        sec.ev["order.expired"].add(t, message)


@handles("ECON_GOVERNOR")
def _governor(sec, t, message, level, near):
    f = kv(message)
    sec.ev["ECON_GOVERNOR"].add(t, message)
    sec.lists["governor"].append((num(f.get("smoothed")), num(f.get("target"))))


@handles("ECON_FLOWS")
def _flows(sec, t, message, level, near):
    f = kv(message)
    sec.ev["ECON_FLOWS"].add(t, message)
    sec.n["exchange.crafted"] += int(num(f.get("exchange_crafted")))
    sec.n["exchange.sales"] += int(num(f.get("exchange_sales")))
    if int(num(f.get("exchange_crafted"))) > 0:
        sec.ev["exchange.crafted"].add(t, message)


@handles("ECON_UNDRESS_GUARD")
def _undress(sec, t, message, level, near):
    f = kv(message)
    sec.ev["ECON_UNDRESS_GUARD"].add(t, message)
    sec.n["undress.checked"] += int(num(f.get("checked")))
    if num(f.get("blocked")) > 0:
        sec.ev["undress.blocked"].add(t, message, int(num(f.get("blocked"))))


@handles("ECON_WEAR")
def _wear(sec, t, message, level, near):
    f = kv(message)
    sec.ev["ECON_WEAR"].add(t, message)
    sec.n["wear.repaired"] += int(num(f.get("repaired")))
    sec.n["wear.below88"] += int(num(f.get("below88")))
    if num(f.get("repaired")) > 0:
        sec.ev["wear.repaired"].add(t, message)


@handles("ECON_CONSUMABLE")
def _consumable(sec, t, message, level, near):
    f = kv(message)
    sec.ev["ECON_CONSUMABLE"].add(t, message)
    sec.n["potions.bought"] += int(num(f.get("potionsBought")))
    sec.n["potions.drunk"] += int(num(f.get("potionsDrunk")))
    if num(f.get("potionsDrunk")) > 0:
        sec.ev["potions.drunk"].add(t, message)


@handles("HIRED_COMPANION_LOGIN", "HIRED_COMPANION_LOGOUT")
def _companion_session(sec, t, message, level, near):
    tag = message.split(" ", 1)[0]
    f = kv(message)
    sec.ev[tag].add(t, message)
    if tag == "HIRED_COMPANION_LOGOUT" and num(f.get("parked")) > 0:
        sec.ev["companion.parked"].add(t, message)
    if tag == "HIRED_COMPANION_LOGIN" and num(f.get("summoning")) > 0:
        sec.ev["companion.back"].add(t, message)


@handles("ERA_BUILDS_LOADED")
def _era_loaded(sec, t, message, level, near):
    f = kv(message)
    if f.get("loaded") == "yes" and f.get("customClass") == "never" and num(f.get("dropped")) == 0:
        sec.ev["era.loaded_ok"].add(t, message)
    else:
        sec.ev["era.loaded_bad"].add(t, message)


@handles("ERA_BUILD_ASSIGNED")
def _era_assigned(sec, t, message, level, near):
    f = kv(message)
    sec.ev["ERA_BUILD_ASSIGNED"].add(t, message)
    sec.n[f"era.draw.{f.get('draw', '?')}"] += 1
    if f.get("class", "").lower() == "sluaghbinder":
        sec.ev["era.custom_assigned"].add(t, message)


@handles("ERA_BUILD_TRAINED")
def _era_trained(sec, t, message, level, near):
    f = kv(message)
    sec.ev["era.trained_yes" if f.get("match") == "yes" else "era.trained_no"].add(t, message)


@handles("ERA_BUILD_CENSUS")
def _era_census(sec, t, message, level, near):
    """The census counts the whole roster, retired custom-class bots included (LogEraBuildCensus(roster)); the check
    holds the count against BOT_CUSTOM_CLASS_RETIRED (DIAG-5378-misc 2)."""
    sec.ev["ERA_BUILD_CENSUS"].add(t, message)
    m = re.search(r"\bSluaghbinder=(\d+)", message)
    if m and int(m.group(1)) > 0:
        sec.ev["era.census_custom"].add(t, message)
        if int(m.group(1)) > sec.n["era.census_custom_max"]:
            sec.n["era.census_custom_max"] = int(m.group(1))
            sec.lists["era.census_custom_line"] = [(t, message)]


@handles("BOT_CUSTOM_CLASS_RETIRED")
def _custom_retired(sec, t, message, level, near):
    sec.ev["BOT_CUSTOM_CLASS_RETIRED"].add(t, message)
    f = kv(message)
    if f.get("id"):
        sec.retired[f["id"]] = f.get("bot")


def retired_acts(sec, t, message):
    """A retired custom-class bot that logs a line of its own (bot=<name> id=<id>, the two matching): it plays."""
    for name, bot_id in RETIRED_ACT_RE.findall(message):
        if sec.retired.get(bot_id) == name:
            sec.ev["era.retired_acts"].add(t, message)
            return


@handles("BOT_BUFFBOT_COPY_RETIRED")
def _copy_retired(sec, t, message, level, near):
    sec.ev["BOT_BUFFBOT_COPY_RETIRED"].add(t, message)
    f = kv(message)
    if f.get("id"):
        sec.copies[f["id"]] = f.get("bot")


@handles("BOT_BUFFBOT_COPY_SUSPECTED")
def _copy_suspected(sec, t, message, level, near):
    sec.ev["BOT_BUFFBOT_COPY_SUSPECTED"].add(t, message)


@handles("BOT_BUFFBOT_COPIES")
def _copies_summary(sec, t, message, level, near):
    sec.ev["BOT_BUFFBOT_COPIES"].add(t, message)
    sec.lists["copies_summary"].append(message.strip().partition(" ")[2])


def copy_acts(sec, t, message):
    """A retired buffbot copy that logs a line of its own (bot=<name> id=<id>, both matching): it plays. The buffbot itself
    has its own id (1,800,000,000 plus its main's), so the same name does not count."""
    for name, bot_id in RETIRED_ACT_RE.findall(message):
        if sec.copies.get(bot_id) == name:
            sec.ev["copies.acts"].add(t, message)
            return


@handles("BOT_SKILL_PROFILE")
def _skill_profile(sec, t, message, level, near):
    f = kv(message)
    sec.ev["BOT_SKILL_PROFILE"].add(t, message)
    # The start tier is the roll the 30/50/20 shares are about; tier= has grown with play (a played world).
    sec.n[f"tier.{f.get('start', f.get('tier', '?'))}"] += 1


MISTAKE_COUNTS_RE = re.compile(r"\b(out|in)\.(pull|buff|power|position)=c(\d+)/(\d+),a(\d+)/(\d+),v(\d+)/(\d+)")


@handles("BOT_SKILL_MISTAKES")
def _skill_mistakes(sec, t, message, level, near):
    """The 5-minute counts: mistakes/decisions of the chance kinds (pull, buff, power, position) out of and in the
    player's group, summed over the session for the quarter-rate check (BOTS-AS-PEOPLE-1.2, WP2 played check)."""
    sec.ev["BOT_SKILL_MISTAKES"].add(t, message)
    for m in MISTAKE_COUNTS_RE.finditer(message):
        side = m.group(1)
        values = [int(x) for x in m.groups()[2:]]
        sec.n[f"mistakes.{side}.m"] += values[0] + values[2] + values[4]
        sec.n[f"mistakes.{side}.d"] += values[1] + values[3] + values[5]


@handles("BOT_SKILL_MISTAKE")
def _skill_mistake(sec, t, message, level, near):
    f = kv(message)
    sec.ev["BOT_SKILL_MISTAKE"].add(t, message)
    if f.get("spared") == "true":
        sec.ev["mistake.spared"].add(t, message)


MOVE_COUNTERS = ("strafes", "faces", "stickLost", "overshoots", "sits", "wallSits", "wallStands")
MOVE_REQUIRED = ("sits", "stickLost", "overshoots")   # PLAYED-CHECK-1.2 Bots as people row 7


@handles("BOT_MOVE_HABITS")
def _move_habits(sec, t, message, level, near):
    """The 5-minute counts of the movement habits, summed over the session (WP5 played check)."""
    sec.ev["BOT_MOVE_HABITS"].add(t, message)
    f = kv(message)
    for name in MOVE_COUNTERS:
        sec.n[f"move.{name}"] += int(num(f.get(name)))


@handles("RAID_LEARN")
def _raid_learn(sec, t, message, level, near):
    f = kv(message)
    sec.ev["RAID_LEARN"].add(t, message)
    raid = f.get("raid")
    # The run: the raid instance the line names (run=, since WP7), else the count of REALM_RAID_STARTED lines for that
    # raid so far (or of any raid, when the ids differ). RAID_LEARN is logged again whenever the roster changes in a run.
    run = f.get("run") if f.get("run") not in (None, "0") else \
        f"start{sec.n[f'raid_starts.{raid}'] or sec.n['raid_starts']}"
    sec.lists["raid_learn"].append((raid, num(f.get("seenShare")), run))


@handles("CHAT_PROMISE")
def _chat_promise(sec, t, message, level, near):
    """CHAT_PROMISE made / kept / broken / refused (ChatPromises: "omw" made true, 1.2 chat audit)."""
    parts = message.split(" ", 2)
    outcome = parts[1] if len(parts) > 1 else "?"
    sec.ev[f"promise.{outcome}"].add(t, message)
    if outcome == "broken":
        sec.ev[f"promise.broken.{kv(message).get('why', '?')}"].add(t, message)


@handles("BOT_PLACE_LEARNED")
def _place(sec, t, message, level, near):
    f = kv(message)
    sec.ev[f"place.{f.get('how', '?')}"].add(t, message)


@handles("BOT_WAY_ASK", "BOT_WAY_TOLD")
def _way(sec, t, message, level, near):
    sec.ev[message.split(" ", 1)[0]].add(t, message)


@handles("BOT_WAY_WRONG_ROAD")
def _wrong_road(sec, t, message, level, near):
    f = kv(message)
    sec.ev["BOT_WAY_WRONG_ROAD"].add(t, message)
    if num(f.get("cost_s")) > 180:
        sec.ev["way.wrong_road_long"].add(t, message)


@handles("SERVER_STALL")
def _server_stall(sec, t, message, level, near):
    f = kv(message)
    sec.stalls.append((t, num(f.get("worstTickMs")), message, "SERVER_STALL"))


@handles("AI_PROFILE")
def _ai_profile(sec, t, message, level, near):
    if not message.startswith("AI_PROFILE tick_p50="):
        return
    i = message.find(" max=")
    if i < 0:
        return
    tick = num(message[i + 5:].split(" ", 1)[0])
    if tick >= STALL_MS:
        sec.stalls.append((t, tick, message, "AI_PROFILE"))


# ---- Judging -----------------------------------------------------------------------------------------------------

def merged_stalls(sec: Section):
    """One per minute: SERVER_STALL and AI_PROFILE lines within 75 s count once (the longer); GC-only minutes dropped.

    An entry pairs one SERVER_STALL with one AI_PROFILE and then takes no third line. Owner's PC, build b1b6846
    (2026-11-18): each minute's SERVER_STALL (mm:03) and AI_PROFILE (mm:04) were paired, the entry took the AI line's
    time, and the next minute's SERVER_STALL 59 s later was merged into it too: 14 stalls were reported as 10, and 13 in
    an hour as 9."""
    events = sorted(sec.stalls, key=lambda s: s[0])
    out, paired = [], []
    for s in events:
        if out and not paired[-1] and out[-1][3] != s[3] and abs((s[0] - out[-1][0]).total_seconds()) <= PAIR_SECONDS:
            if s[1] > out[-1][1]:
                out[-1] = s
            paired[-1] = True
            continue
        out.append(s)
        paired.append(False)
    return [s for s in out if s[1] >= STALL_MS]


def worst_hour(times):
    best, best_from, j = 0, None, 0
    for i, t in enumerate(times):
        while (t - times[j]).total_seconds() >= 3600:
            j += 1
        if i - j + 1 > best:
            best, best_from = i - j + 1, times[j]
    return best, best_from


def hours(seconds):
    m = int(seconds // 60)
    return f"{m // 60} h {m % 60:02d} min"


class Judge:
    def __init__(self, sec: Section):
        self.sec = sec
        self.checks = []

    def add(self, area, cid, row, status, ev: Evidence | None = None, detail="", must=True, note=""):
        item = {"area": area, "id": cid, "row": row, "must": must, "status": status, "detail": detail, "note": note,
                "at": None, "line": None}
        if ev is not None and ev.first_t is not None:
            item["at"], item["line"] = stamp(ev.first_t), ev.first_line
        self.checks.append(item)

    def ev(self, key) -> Evidence:
        return self.sec.ev.get(key) or Evidence()

    def seen_if(self, area, cid, row, key, detail="", must=True, note="", against=None, against_detail=""):
        """Seen when `key` has a line; Contradicted when `against` has one (that line is quoted)."""
        bad = self.ev(against) if against else Evidence()
        if bad.count:
            self.add(area, cid, row, CONTRADICTED, bad, against_detail or detail, must, note)
        elif self.ev(key).count:
            self.add(area, cid, row, SEEN, self.ev(key), detail, must, note)
        else:
            self.add(area, cid, row, NOT_SEEN, None, detail, must, note)


def judge(sec: Section) -> list[dict]:
    j = Judge(sec)
    c = lambda key: j.ev(key).count  # noqa: E731
    install(j, sec, c)
    raids(j, sec, c)
    rvr(j, sec, c)
    crafting(j, sec, c)
    companions(j, sec, c)
    era(j, sec, c)
    people(j, sec, c)
    chat(j, sec, c)
    stalls_and_errors(j, sec, c)
    return j.checks


def install(j: Judge, sec: Section, c):
    flags = sec.fork_fields
    fork = Evidence()
    fork.add(sec.fork_build_t, "FORK_BUILD " + " ".join(f"{k}={flags[k]}" for k in ("name", "commit", "sluaghbinder")
                                                        if k in flags))
    j.add("install", "fork_build", "The server names its build (FORK_BUILD name=1.2-beta)",
          SEEN if sec.build_name == "1.2-beta" else NOT_SEEN, fork,
          f"name={sec.build_name}", note="row 1 and 2 proof line (INSTALL-CHECKS-1.2.md)")
    started = Evidence()
    if sec.startup_completed:
        started.add(sec.startup_completed, "GameServer startup completed")
    j.add("install", "startup", "GameServer startup completed", SEEN if started.count else NOT_SEEN, started)
    off = flags.get("sluaghbinder")
    j.add("install", "classoff_flag", "Class-off world: FORK_BUILD ... sluaghbinder=off (row 3)",
          SEEN if off == "off" else NOT_SEEN, fork if off == "off" else None,
          f"sluaghbinder={off or 'not in the line (older build)'}")
    j.seen_if("install", "custom_class_refused", "The Mauler slot refused at character creation (CUSTOM_CLASS_REFUSED)",
              "CUSTOM_CLASS_REFUSED", f"{c('CUSTOM_CLASS_REFUSED')} refusals",
              note="the class list on screen is a screen check")
    if off == "off":
        raid_ev = j.ev("REALM_RAID_STARTED")
        j.add("install", "classoff_raid", "Class-off session: a raid starts (REALM_RAID_STARTED after sluaghbinder=off)",
              SEEN if raid_ev.count else NOT_SEEN, raid_ev, f"{raid_ev.count} raid start(s)")
        bad = sec.err_custom_class
        j.add("install", "classoff_errors", "Class-off session: no ERR/FTL naming Sluaghbinder, Acolyte, class 62/63, Muirenn",
              CONTRADICTED if bad.count else (SEEN if sec.startup_completed else NOT_SEEN), bad if bad.count else None,
              f"{bad.count} such line(s)", note="an hour of play is timed from the session, not judged here")


def raids(j: Judge, sec: Section, c):
    ended = [r for r in sec.raids if r.outcome]
    results = [r for r in ended if r.outcome in ("Boss defeated", "Defeated")]
    j.add("raids", "result", "Raids end in a result, never a stall (raid bar)",
          CONTRADICTED if c("raid.timed_out") else (SEEN if results else NOT_SEEN),
          j.ev("raid.timed_out") if c("raid.timed_out") else (j.ev("raid.won") if c("raid.won") else j.ev("raid.lost")),
          f"{len(ended)} ended: {c('raid.won')} won, {c('raid.lost')} lost, {c('raid.timed_out')} timed out; "
          f"{c('REALM_RAID_STALLED')} REALM_RAID_STALLED line(s)",
          note="whether a loss was a correct raid is the log review's call")
    won = ", ".join(r.event for r in ended if r.outcome == "Boss defeated")
    j.seen_if("raids", "won", "A raid won (REALM_RAID_ENDED outcome=\"Boss defeated\")", "raid.won", won or "none")
    j.seen_if("raids", "tale_call", "A raid the game opens itself: the tale first, then the call (#192)",
              "raid.called_after_tale", f"{c('REALM_RAID_TALE_FIRST')} tale-first, {c('REALM_RAID_CALLED')} calls",
              note="the notice and horn on screen are a screen check")
    j.seen_if("raids", "schedule", "Raids every four hours per realm (REALM_RAID_SIZED / SCHEDULED, #192)",
              "REALM_RAID_SCHEDULED", f"{c('REALM_RAID_SCHEDULED')} scheduled, {c('REALM_RAID_SIZED')} sized")
    went_in = j.ev("return.went_in")
    j.add("raids", "return_wave", "After a wipe the raiders gather at the way in and go back in together (#195)",
          SEEN if went_in.count else NOT_SEEN, went_in if went_in.count else None,
          f"{c('return.wave')} gather/form/join lines, {went_in.count} of a wave going in (moving, ahead, cleared, met); "
          f"stuck waves are the next row")
    # A wave stuck shortly before its raid won is not "never made headway" (sweep 6e: Crystal Cave's wave 2, stuck 51 s
    # before Xanxicar died).
    wins = defaultdict(list)
    for r in sec.raids:
        if r.outcome == "Boss defeated" and r.ended:
            wins[r.event].append(r.ended)
    won_after = {key for key, v in sec.waves.items() if v[0] == "stuck" and any(
        0 <= (end - v[1]).total_seconds() <= STUCK_BEFORE_WIN_SECONDS for end in wins.get(key[0], ()))}
    stuck = [v for key, v in sec.waves.items() if v[0] == "stuck" and key not in won_after]
    recovered = [v for v in sec.waves.values() if v[0] == "recovered"]
    stuck_detail = (f"{c('return.stuck')} step=stuck line(s), {len(sec.waves)} wave(s) stuck at least once: "
                    f"{len(recovered)} made headway again, {len(stuck)} never did"
                    + (f", {len(won_after)} stuck less than {STUCK_BEFORE_WIN_SECONDS // 60} minutes before its raid won "
                       f"(not counted)" if won_after else "")
                    + (f", {sum(1 for v in sec.waves.values() if v[0] == 'released')} released to the raid (sweep 6c, 6d)"
                       if c("return.released") else ""))
    if stuck:
        bad = Evidence()
        first = min(stuck, key=lambda v: v[1])
        bad.add(first[1], first[2])
        j.add("raids", "return_stuck", "A stuck return wave makes headway again (REALM_RAID_RETURN_WAVE step=stuck)",
              CONTRADICTED, bad, stuck_detail, note="why a wave stuck (navmesh, a refused regroup, help calls) is the "
                                                    "log review's call")
    else:
        j.add("raids", "return_stuck", "A stuck return wave makes headway again (REALM_RAID_RETURN_WAVE step=stuck)",
              SEEN if recovered else NOT_SEEN, j.ev("return.stuck") if recovered else None, stuck_detail, must=False)
    no_engage = j.ev("pull.no_engage")
    pull_detail = (f"{c('pull.at_needed')} final engages at the needed count, {c('pull.below_needed')} below it, "
                   f"{no_engage.count} pull(s) with no FINAL_ENGAGE or EARLY_PULL (a required-adds switch or a wipe "
                   f"outside an attempt)" + (f": {', '.join(b for _, b in sec.no_engage)}" if sec.no_engage else ""))
    if c("pull.below_needed") or no_engage.count:
        bad = min((e for e in (j.ev("pull.below_needed"), no_engage) if e.count), key=lambda e: e.first_t)
        j.add("raids", "pull_70", "A pull waits for 70% of the living raid (#195)", CONTRADICTED, bad, pull_detail)
    else:
        j.seen_if("raids", "pull_70", "A pull waits for 70% of the living raid (#195)", "pull.at_needed", pull_detail)
    retry_detail = (f"{c('retry.with_adds')} retry(ies) of a boss with adds showed its adds, {c('retry.no_adds')} showed "
                    f"none (no addsSpawn lore line, add retarget or add damage)"
                    + (f"; still open: {', '.join(sec.retry_watch)}" if sec.retry_watch else ""))
    j.seen_if("raids", "retry_adds", "A retry of a boss that raises adds raises them again (FINAL_ENGAGE retry=True, sweep 6b)",
              "retry.with_adds", retry_detail, against="retry.no_adds",
              note="a boss whose adds stay dead by design is the log review's call")
    never = j.ev("attempt.never_entered_fallen")
    j.seen_if("raids", "attempt_contact",
              f"No attempt ends \"never entered combat\" with {NEVER_ENTERED_FALLEN} or more fallen (ATTEMPT_ENDED, sweep 6b)",
              "attempt.ended", f"{c('attempt.ended') + never.count} attempt end(s), {never.count} \"never entered combat\" "
              f"with {NEVER_ENTERED_FALLEN}+ fallen", against="attempt.never_entered_fallen")
    j.seen_if("raids", "reset_no_attempt", "A boss that resets while the raid still stands costs no attempt (#195)",
              "raid.reset_no_attempt", f"{c('raid.reset_no_attempt')} reset(s)")
    j.seen_if("raids", "route_boss", "A living boss on a raid's way in is fought as an objective (#213)",
              "route.boss_objective", f"{c('route.boss_objective')} line(s)")
    j.seen_if("raids", "respawn_held", "A raid dungeon's epics don't respawn while its raid is inside (#213)",
              "REALM_RAID_EPIC_RESPAWN_HELD", f"{c('REALM_RAID_EPIC_RESPAWN_HELD')} held")
    j.seen_if("raids", "muster_guarded", "No muster inside another living boss's aggro (#213)",
              "REALM_EXPEDITION_MUSTER_GUARDED",
              f"{c('REALM_EXPEDITION_MUSTER_GUARDED')} guarded, {c('REALM_EXPEDITION_MUSTER_UNSAFE')} unsafe, "
              f"{c('REALM_EXPEDITION_MUSTER_MOVED')} moved",
              note="a stranded teleport into a resting boss's reach is the next check")
    near = j.ev("stranded.near_resting")
    j.add("raids", "teleport_resting_boss",
          "No stranded raider teleported near a resting boss (REALM_EXPEDITION_STRANDED_REGROUP, RAID-TUSCARAN-1117)",
          CONTRADICTED if near.count else (SEEN if c("stranded.all") else NOT_SEEN),
          near if near.count else j.ev("stranded.all"),
          f"{c('stranded.all')} stranded regroups, {near.count} within {sec.near:.0f} of a boss whose last "
          f"TARGET_STATUS had inCombat=False and that was not engaged",
          note="a boss rests from its last TARGET_STATUS inCombat=False unless engaged: from a FINAL_ENGAGE, EARLY_PULL "
               "Commit, TARGET_CALLED, fight-ledger damage or required-adds switch until its wipe, defeat or heal")
    sweep6c_rows(j, sec, c)
    sweep6d_rows(j, sec, c)
    sweep6e_rows(j, sec, c)
    sweep6f_rows(j, sec, c)
    sweep7b_rows(j, sec, c)
    rekill_rows(j, sec, c)
    sweep = j.ev("EPIC_SWEEP_START")
    j.add("raids", "sweep", "/epictest sweep progress (EPIC_SWEEP_*)", SEEN if sweep.count else NOT_SEEN, sweep,
          f"starts: {'; '.join(sec.lists['sweep_start']) or 'none'}; {c('EPIC_SWEEP_ADD')} add, "
          f"{c('EPIC_SWEEP_RAID_START')} raid starts, {c('EPIC_SWEEP_RAID_DONE')} done, "
          f"{c('EPIC_SWEEP_RAID_NOT_STARTED')} not started, {c('EPIC_SWEEP_HELD')} held; "
          f"end: {'; '.join(sec.lists['sweep_end']) or 'none in this build'}", must=False)


def sweep6c_rows(j: Judge, sec: Section, c):
    """Sweep 6c (build b1b6846): a boss the leash never brought home, a regroup point at a monster's raw position that
    raiders could not walk from, and a return wave that did not move."""
    frozen = j.ev("tether.frozen")
    runs = ", ".join(f"{label} x{n}" for label, n in sorted(sec.frozen_runs.items(), key=lambda kv_: -kv_[1]))
    j.add("raids", "leash_home",
          f"A leashed boss goes home: none leashed {FROZEN_LEASH_RUN}+ times in a row from the same spot "
          f"(AUTONOMOUS_TETHER_LEASH, sweep 6c)",
          CONTRADICTED if frozen.count else (SEEN if c("tether.leash") else NOT_SEEN),
          frozen if frozen.count else j.ev("tether.leash"),
          f"{c('tether.leash')} leash(es); frozen or untouchable out of its tether: {runs or 'none'}",
          note="the same fromSpawn (within 25) leash after leash: the boss never walked home")
    raw = []
    for key, (t, message) in sec.raw_fronts.items():
        regroups, retries = sec.regroups_to[key], sec.retries_at[key]
        if regroups >= RAW_FRONT_REGROUPS and retries >= RAW_FRONT_RETRIES:
            raw.append((t, message, key, regroups, retries))
    if raw:
        first = min(raw, key=lambda r: r[0])
        bad = Evidence()
        bad.add(first[0], first[1])
        detail = "; ".join(f"region {k[0]} {k[1]}: {g} stranded regroups to it, {r} route retries from it"
                           for _, _, k, g, r in raw)
        j.add("raids", "raw_front", "No regroup point at a monster's raw position raiders can't walk from (sweep 6c)",
              CONTRADICTED, bad, detail, note="for a flyer that is in the air (the Air primal, 690 above the floor)")
    else:
        j.add("raids", "raw_front", "No regroup point at a monster's raw position raiders can't walk from (sweep 6c)",
              SEEN if c("stranded.all") else NOT_SEEN, j.ev("stranded.all") if c("stranded.all") else None,
              f"{len(sec.raw_fronts)} raw front(s); none with {RAW_FRONT_REGROUPS}+ stranded regroups to it and "
              f"{RAW_FRONT_RETRIES}+ route retries from it")
    still = j.ev("return.step_still")
    # "counted as headway": the stuck row counts this wave as having made headway again
    held = [f"{key[0]} wave {key[1]} at step {n} from {stamp(since)[6:]}"
            + (" (counted as headway)" if sec.waves.get(key, ("",))[0] == "recovered" else "")
            for key, n, since in sec.lists["still_waves"]]
    j.add("raids", "wave_moves",
          f"A return wave moves: none holds one step {WAVE_STEP_STILL_SECONDS // 60} minutes or more "
          f"(REALM_RAID_RETURN_WAVE at=, sweep 6c)",
          CONTRADICTED if still.count else (SEEN if c("return.went_in") else NOT_SEEN),
          still if still.count else (j.ev("return.went_in") if c("return.went_in") else None),
          f"{still.count} wave(s) held one step: {'; '.join(held) or 'none'}; "
          f"{c('return.released')} stuck wave(s) released to the raid (its muster or its fight)",
          note="joins, catch-ups and step=ahead (raiders leaving the wave) are not the wave's own headway")


def sweep6d_rows(j: Judge, sec: Section, c):
    """Sweep 6d (build b1b6846): a required-adds phase that never progressed, raiders killed by an epic after their raid
    ended, and raiders who ended a raid at a regroup point they could not walk from."""
    stalled = j.ev("adds.no_progress")
    # A phase's sums run on after its finding until an add dies or the damage stops: told through its last minute.
    told = [f"{boss} in region {region} from {stamp(p[0])[6:]} to {stamp(p[1])[6:]}: {p[2]:,.0f} dealt on {combined:,} "
            f"health in {(p[1] - p[0]).total_seconds() / 60:.0f} min with no add death"
            for boss, region, combined, p in sec.lists["no_progress_phases"]]
    sec.lists["no_progress"][:0] = told
    j.add("raids", "adds_progress",
          f"A required-adds phase makes progress: no {NO_PROGRESS_MINUTES}+ minutes without an add death while the raid "
          f"dealt the adds more than their combined health (EPIC_FIGHT_LEDGER, REALM_EXPEDITION_NO_PROGRESS, sweep 6d)",
          CONTRADICTED if stalled.count else (SEEN if sec.add_phases else NOT_SEEN), stalled if stalled.count else None,
          f"{len(sec.add_phases)} required-adds phase(s) seen; no progress: {'; '.join(sec.lists['no_progress']) or 'none'}",
          note="the adds' health comes from docs/encounters/epic-encounters.json; an add the catalog lacks is not judged")
    epics = epic_killers(sec)
    deaths = [d for d in sec.after_end_deaths if d[1] in epics]
    if deaths:
        bad = Evidence()
        bad.add(deaths[0][0], deaths[0][2], len(deaths))
        by = Counter(f"{d[3]['event']} ({d[1]})" for d in deaths)
        j.add("raids", "deaths_after_end",
              f"No raider is killed by an epic within {AFTER_END_SECONDS // 60} minutes after its raid ended, in the "
              f"raid's region or its way in (AUTONOMOUS_BOT_DEATH, sweep 6d, 6e)", CONTRADICTED, bad,
              f"{len(deaths)} death(s), {len({d[2].split('bot=', 1)[1].split(' ', 1)[0] for d in deaths})} raider(s): "
              + "; ".join(f"{k} x{n}" for k, n in by.most_common()))
    else:
        j.add("raids", "deaths_after_end",
              f"No raider is killed by an epic within {AFTER_END_SECONDS // 60} minutes after its raid ended, in the "
              f"raid's region or its way in (AUTONOMOUS_BOT_DEATH, sweep 6d, 6e)", SEEN if sec.ends else NOT_SEEN,
              j.ev("REALM_RAID_ENDED") if sec.ends else None,
              f"{len(sec.ends)} raid end(s); {len(sec.after_end_deaths)} raider death(s) to monsters there in that time, "
              f"none to an epic")
    stranded = []
    for end in sec.ends:
        at = Counter()
        for name, (alive, region, pos) in end["members"].items():
            if alive and sec.regroups_to[(region, pos)] and sec.retries_at[(region, pos)]:
                at[(region, pos)] += 1
        for (region, pos), n in at.items():
            stranded.append((end, region, pos, n))
    if stranded:
        bad = Evidence()
        first = stranded[0]
        bad.add(first[0]["t"], f"REALM_RAID_ENDED event={first[0]['event']}: {first[3]} raider(s) at {first[1]}:{first[2]}, "
                               f"a STRANDED_REGROUP point with ROUTE_RETRY from it", sum(x[3] for x in stranded))
        j.add("raids", "stranded_at_end",
              "No raider ends a raid at a stranded regroup point it could not walk from (AUTONOMOUS_GROUP_OUTCOME, "
              "STRANDED_REGROUP to=, ROUTE_RETRY, sweep 6d)", CONTRADICTED, bad,
              "; ".join(f"{e['event']} at {stamp(e['t'])[6:]}: {n} at {r}:{p} ({sec.regroups_to[(r, p)]} regroups to it, "
                        f"{sec.retries_at[(r, p)]} retries from it)" for e, r, p, n in stranded))
    else:
        j.add("raids", "stranded_at_end",
              "No raider ends a raid at a stranded regroup point it could not walk from (AUTONOMOUS_GROUP_OUTCOME, "
              "STRANDED_REGROUP to=, ROUTE_RETRY, sweep 6d)",
              SEEN if any(e["members"] for e in sec.ends) else NOT_SEEN, j.ev("REALM_RAID_ENDED") if sec.ends else None,
              f"{sum(len(e['members']) for e in sec.ends)} raider position(s) at {len(sec.ends)} raid end(s); none at such a point")


def epic_killers(sec: Section) -> set:
    """Epic monsters by name: the catalog's bosses, those a raid held or fought adds of, those adds (Aidon's illusions),
    and the epics parties walked past (AUTONOMOUS_DUNGEON_EPIC_AVOIDED: Avalon City's xanxicarian sentries)."""
    names = set(sec.epic_names) | catalog()[1]
    for adds in sec.required_adds.values():
        names |= adds
    for adds in sec.boss_adds.values():
        names |= adds
    for avoided in sec.avoided.values():
        names |= avoided
    return names


def sweep6e_rows(j: Judge, sec: Section, c):
    """Sweep 6e (build b1b6846): a raid boss healed to full out of its home or tether while it fought the raid (S-1),
    deaths after a full reset inside an attempt left open (S-2), raider deaths to the epic trash on a raid's way in
    (W-1), and a muster set aside while a return wave walked to it (T-1)."""
    engages = c("pull.at_needed") + c("pull.below_needed")
    heals = sec.tether_heals
    if heals:
        bad = Evidence()
        bad.add(heals[0][0], heals[0][1], len(heals))
        by = Counter(kv(line).get("target") for _, line in heals)
        j.add("raids", "tether_heal",
              "No raid boss heals to full past its home or tether while the raid fights it (REALM_EXPEDITION_TARGET_HEALED "
              "fromSpawn=, sweep 6e)", CONTRADICTED, bad,
              f"{len(heals)} heal(s) to {RESET_HEALTH}%+ past the boss's fight-at-home radius (catalog) or tether: "
              + "; ".join(f"{b} x{n}" for b, n in by.most_common()),
              note="the boss's script heals it out there by design (Aidon's anti-kite rule); the finding is where the raid stood")
    else:
        j.add("raids", "tether_heal",
              "No raid boss heals to full past its home or tether while the raid fights it (REALM_EXPEDITION_TARGET_HEALED "
              "fromSpawn=, sweep 6e)", SEEN if engages else NOT_SEEN, j.ev("pull.at_needed") if engages else None,
              "no heal to full past a home or tether while fighting")
    resets = list(sec.reset_attempts) + [a for a in sec.attempts.values() if a["reset"] is not None]
    fed = [a for a in resets if len(a["deaths"]) >= RESET_DEATHS]
    if fed:
        first = min(fed, key=lambda a: a["reset"][0])
        bad = Evidence()
        bad.add(first["reset"][0], first["reset"][1], sum(len(a["deaths"]) for a in fed))
        j.add("raids", "reset_closes",
              f"An attempt ends when its boss resets to full: fewer than {RESET_DEATHS} deaths to it after a reset inside "
              f"an open attempt (FINAL_ENGAGE to WIPE / ATTEMPT_ENDED, TARGET_STATUS / TARGET_HEALED health, sweep 6e)",
              CONTRADICTED, bad,
              "; ".join(f"{a['boss']} in region {a['region']}: back at {RESET_HEALTH}%+ at {stamp(a['reset'][0])[6:]} after "
                        f"{RESET_LOW}% or less, {len(a['deaths'])} death(s) to it from {RESET_GRACE_SECONDS} s later while "
                        f"the attempt stayed open" for a in fed))
    else:
        j.add("raids", "reset_closes",
              f"An attempt ends when its boss resets to full: fewer than {RESET_DEATHS} deaths to it after a reset inside "
              f"an open attempt (FINAL_ENGAGE to WIPE / ATTEMPT_ENDED, TARGET_STATUS / TARGET_HEALED health, sweep 6e)",
              SEEN if engages else NOT_SEEN, j.ev("pull.at_needed") if engages else None,
              f"{len(resets)} reset(s) inside an open attempt, none followed by {RESET_DEATHS}+ deaths to the boss")
    # Epic trash only: a boss (the catalog's, or a route gate's) and its adds are the raid's own fight there.
    fought = catalog()[1] | set(add_health()) | sec.gate_bosses
    for boss in list(fought):
        fought |= sec.required_adds.get(boss, set()) | sec.boss_adds.get(boss, set())
    way_in = [d for d in sec.way_in_deaths if d[1] in sec.avoided.get(d[2], ()) and d[1] not in fought]
    if way_in:
        bad = Evidence()
        bad.add(way_in[0][0], way_in[0][3], len(way_in))
        by = Counter(f"{d[4]} in region {d[2]} ({d[1]})" for d in way_in)
        j.add("raids", "way_in_epics",
              "No raider is killed on a raid's way in by epic monsters the parties walk past (AUTONOMOUS_BOT_DEATH, "
              "AUTONOMOUS_DUNGEON_EPIC_AVOIDED, sweep 6e)", CONTRADICTED, bad,
              f"{len(way_in)} death(s) while the raid ran: " + "; ".join(f"{k} x{n}" for k, n in by.most_common()),
              note="deaths in the way-in region while the raid runs, to a monster logged as an avoided epic there; "
                   "whether each was a raider is the log review's call")
    else:
        j.add("raids", "way_in_epics",
              "No raider is killed on a raid's way in by epic monsters the parties walk past (AUTONOMOUS_BOT_DEATH, "
              "AUTONOMOUS_DUNGEON_EPIC_AVOIDED, sweep 6e)",
              SEEN if sec.way_in_deaths or c("route.boss_objective") else NOT_SEEN,
              j.ev("route.boss_objective") if c("route.boss_objective") else None,
              f"{len(sec.way_in_deaths)} death(s) in a running raid's way-in region, none to an epic walked past there")
    stalls = sec.stall_waves
    if stalls:
        bad = Evidence()
        bad.add(stalls[0][0], f"{stalls[0][1].rstrip()} [wave {stalls[0][3]} made headway {stalls[0][4]:.0f} s before]",
                len(stalls))
        j.add("raids", "muster_wave",
              f"No muster is set aside while a return wave with headway in the last {WAVE_HEADWAY_SECONDS // 60} minutes "
              f"walks to it (REALM_EXPEDITION_MUSTER_STALLED, REALM_RAID_RETURN_WAVE, sweep 6e)", CONTRADICTED, bad,
              "; ".join(f"{e} at {stamp(t)[6:]}: \"{kv(line).get('target')}\" set aside, wave {w} headway {ago:.0f} s before"
                        for t, line, e, w, ago in stalls)
              + f"; {c('REALM_EXPEDITION_MUSTER_WAITS_FOR_WAVE')} MUSTER_WAITS_FOR_WAVE line(s)",
              note="a wave walks to the route's destination, the muster; headway is a kill, a new step or step=moving")
    else:
        waits = c("REALM_EXPEDITION_MUSTER_WAITS_FOR_WAVE")
        j.add("raids", "muster_wave",
              f"No muster is set aside while a return wave with headway in the last {WAVE_HEADWAY_SECONDS // 60} minutes "
              f"walks to it (REALM_EXPEDITION_MUSTER_STALLED, REALM_RAID_RETURN_WAVE, sweep 6e)",
              SEEN if waits or c("muster.stalled") else NOT_SEEN,
              j.ev("REALM_EXPEDITION_MUSTER_WAITS_FOR_WAVE") if waits else (j.ev("muster.stalled") if c("muster.stalled") else None),
              f"{c('muster.stalled')} muster(s) set aside, none beside a moving wave; {waits} MUSTER_WAITS_FOR_WAVE line(s)")


def sweep6f_rows(j: Judge, sec: Section, c):
    """Sweep 6f (build b1b6846): a return wave rebased to a step none of its raiders stood at that then sat silent (W-2)."""
    end = sec.run_end()
    for key in list(sec.rebases):
        if end is not None:
            close_rebase(sec, key, end, "the log ends")
    silent = sorted(sec.silent_rebases)
    text = (f"A rebased return wave moves: none is rebased to a step none of its raiders stand at and then makes no step, "
            f"fight or end of its own for {REBASE_SILENT_SECONDS // 60} minutes or more (REALM_RAID_RETURN_WAVE step=rebased, "
            f"sweep 6f)")
    if silent:
        bad = Evidence()
        bad.add(silent[0][0], silent[0][1], len(silent))
        j.add("raids", "rebase_silent", text, CONTRADICTED, bad,
              "; ".join(f"{k[0]} wave {k[1]} rebased at {stamp(t)[6:]} to {kv(line).get('at')} "
                        f"(standing={kv(line).get('standing', 'not logged')}), silent {m:.1f} min until {why}"
                        for t, line, k, m, why in silent),
              note="standing= is the new core standing at the step (builds before sweep 6f don't log it: their rebase "
                   "used the step the raiders walked to); joins, step=waiting and step=stuck are not the wave's own acts")
    else:
        j.add("raids", "rebase_silent", text, SEEN if c("return.rebased") else NOT_SEEN,
              j.ev("return.rebased") if c("return.rebased") else None,
              f"{c('return.rebased')} rebase(s), none silent {REBASE_SILENT_SECONDS // 60}+ minutes at a step nobody stood at")


def sweep7b_rows(j: Judge, sec: Section, c):
    """Sweep 7b (build 9a173f4): a fight with a curable raid-wide DoT boss (Summoner Roesia's Summoner Pain) sees the raid
    cure it (RAID_CURE_POISON in her region during the fight). 9a173f4: four attempts, 0 cure lines."""
    text = ("In a fight with a boss whose damage over time reaches the raid and is curable (Summoner Roesia's Summoner "
            "Pain), the raid's healers cure it: RAID_CURE_POISON in her region during the fight (sweep 7b)")
    if not sec.dot_fights:
        j.add("raids", "raid_cure_dot", text, NOT_SEEN, None, "no fight with " + ", ".join(sorted(CURABLE_DOT_BOSSES)))
        return
    results = []
    for region, boss, first, last, line in sec.dot_fights:
        until = last + dt.timedelta(seconds=CURE_AFTER_SECONDS)
        cures = [x for x in sec.raid_cures if x[1] == region and first <= x[0] <= until]
        results.append((first, line, boss, region, last, len(cures), sum(x[2] for x in cures)))
    uncured = [r for r in results if r[5] == 0]
    detail = "; ".join(f"{boss} in region {region} {stamp(first)[6:]}-{stamp(last)[6:]}: {lines} RAID_CURE_POISON "
                       f"line(s), {casts} cast(s)" for first, _, boss, region, last, lines, casts in results)
    note = ("RAID_CURE_POISON sums a region's raid cure casts and is written once a minute at most, when a cast comes a "
            "minute or more after the window opened; builds before sweep 7b cured only when no heal had been chosen")
    if uncured:
        bad = Evidence()
        bad.add(uncured[0][0], uncured[0][1], len(uncured))
        j.add("raids", "raid_cure_dot", text, CONTRADICTED, bad, detail, note=note)
    else:
        j.add("raids", "raid_cure_dot", text, SEEN, j.ev("RAID_CURE_POISON"), detail, note=note)


def rekill_rows(j: Judge, sec: Section, c):
    """DF knight loop (build b1b6846): one raid killing the same target again and again with no other kill between."""
    for region in list(sec.kill_streaks):
        close_streak(sec, region)
    # One monster killed again, not a pack: a named one (a proper name, as the server tells named from trash), and no
    # encounter's add (the catalog's, or one the log showed raised for or required by a boss: Aidon's illusions).
    adds = set(add_health())
    for names in list(sec.required_adds.values()) + list(sec.boss_adds.values()):
        adds |= names
    loops = sorted(loop for loop in sec.rekill_loops if loop[3] != loop[3].lower() and loop[3] not in adds)
    text = (f"A raid's route moves on: it never kills the same named target {REKILL_LOOP + 1} times in a row "
            f"({REKILL_LOOP}+ re-kills) with no other target killed between (REALM_EXPEDITION_TARGET_DEFEATED, DF knight loop)")
    passed = c("REALM_EXPEDITION_TRAIL_GATE_PASSED")
    if loops:
        bad = Evidence()
        bad.add(loops[0][0], loops[0][1], len(loops))
        j.add("raids", "rekill_loop", text, CONTRADICTED, bad,
              "; ".join(f"\"{target}\" in region {region}{f' ({event})' if event else ''}: {kills} kills in a row "
                        f"from {stamp(first)[6:]} to {stamp(last)[6:]}" for first, _, region, target, kills, last, event in loops)
              + f"; {passed} TRAIL_GATE_PASSED line(s)",
              note="kills of one region's route targets, in order; a named one (proper name), no encounter add; two raids "
                   "in one region (Darkness Falls) would break a streak, never make one")
    else:
        kills = c("REALM_EXPEDITION_TARGET_DEFEATED")
        j.add("raids", "rekill_loop", text, SEEN if kills else NOT_SEEN,
              j.ev("REALM_EXPEDITION_TARGET_DEFEATED") if kills else None,
              f"{kills} route kill(s), no named target killed {REKILL_LOOP + 1}+ times in a row; {passed} TRAIL_GATE_PASSED line(s)")


def rvr(j: Judge, sec: Section, c):
    span = max((sec.run_end() - sec.run_start()).total_seconds(), 60.0) if sec.run_start() else 60.0
    starts, relay_started = c("RVR_REGROUP"), c("regroup.relay_started")
    share = 100 * relay_started / starts if starts else 0.0
    counts = {k: c(f"regroup.{k}") for k in ("a_relayed_sends", "b_echo_source", "c_returned")}
    named, one_fight = sec.n["relay.named"], c("regroup.c_one_fight")
    over = starts and share > RELAY_SHARE_BAR
    c_text = (f"(c) relay back at its sender within 60 s: {counts['c_returned']} (exact, from= on {named} relayed "
              f"start(s))" if named else
              f"(c) relay back at its sender within 60 s: not judged (no from= on relayed starts, a build before the "
              f"sender was logged); relays among the groups of one fight: {one_fight}")
    if named and one_fight:
        c_text += f"; {one_fight} relayed start(s) without from= among the groups of one fight"
    detail = (f"{starts} regroup starts in {hours(span)} ({starts / (span / 3600):.0f}/h), {relay_started} started by "
              f"a relay ({share:.1f}%, at most {RELAY_SHARE_BAR:g}%); (a) relayed stance relayed again: "
              f"{counts['a_relayed_sends']}, (b) regroup ending a relayed retreat relayed: {counts['b_echo_source']}, "
              f"{c_text}; relayed regroups ended by contact within 1 s: {c('regroup.relay_flash')}")
    bad = [j.ev(f"regroup.{k}") for k, n in counts.items() if n]
    if over:
        bad.append(j.ev("regroup.relay_started"))
    row = (f"Regroups no longer flap: one hop, no echo, relay-started regroups {RELAY_SHARE_BAR:g}% of starts or less "
           f"(RVR_REGROUP)")
    note = "relayedTo>0 on a group's own retreat or own regroup is allowed (#304: one hop)"
    if bad:
        j.add("rvr", "regroup", row, CONTRADICTED, min(bad, key=lambda e: e.first_t), detail, note=note)
    elif starts:
        j.add("rvr", "regroup", row, SEEN, j.ev("RVR_REGROUP"), detail, note=note)
    else:
        j.add("rvr", "regroup", row, NOT_SEEN, None, detail)
    delays = sorted(sec.lists["notice_delay"])
    median = f"{delays[len(delays) // 2]:.0f} ms" if delays else "-"
    j.seen_if("rvr", "notice", "Bots notice bots by the player rules (RVR_NOTICE kind=EnemyBot, RVR_SPOT)",
              "notice.enemybot", f"{c('notice.enemybot')} EnemyBot notices (median delayMs {median}), "
              f"{sec.n['notice.Player']} of the player, {c('RVR_SPOT')} spots",
              note="held against the perception table by the log review, not here")
    online = sec.lists["online"]
    kills = c("RVR_BOT_KILLED")
    rate = f"{kills / (span / 3600):.0f} an hour"
    online_text = (f"; online from REALM_RAID_SCHEDULED: {', '.join(f'{r} {int(n)}' for _, r, n in online[-3:])}"
                   if online else "; no online count in the log (REALM_RAID_SCHEDULED online=)")
    j.seen_if("rvr", "frontier", "The frontier isn't dead (RVR_BOT_KILLED per online bot)", "RVR_BOT_KILLED",
              f"{kills} kills, {rate}{online_text}",
              note="the bar (half of ccba364's rate per online bot, at daytime population) is not judged here")
    roam_ok = Evidence() if not (c("RVR_ROAM_HUNT") and c("RVR_ROAM_MOVE_ON")) else j.ev("RVR_ROAM_MOVE_ON")
    j.add("rvr", "roam", "Roaming eights find fights and move on (RVR_ROAM_HUNT and RVR_ROAM_MOVE_ON)",
          SEEN if roam_ok.count else NOT_SEEN, roam_ok if roam_ok.count else None,
          f"{c('RVR_ROAM_CHECK')} checks, {c('RVR_ROAM_HUNT')} hunts, {c('RVR_ROAM_MOVE_ON')} move-ons")
    tactics = ("RVR_ASSIST_CALL", "RVR_ASSIST_SWITCH", "RVR_CC_ASSIGN", "RVR_INTERRUPT", "RVR_PEEL", "RVR_KITE",
               "RVR_STEALTH_OPEN", "RVR_PARTY_FORMED")
    first = min((j.ev(t) for t in tactics if c(t)), key=lambda e: e.first_t, default=None)
    j.add("rvr", "tactics", "Group tactics fire (assist, crowd control, interrupt, peel, kite, stealth, parties)",
          SEEN if first else NOT_SEEN, first, ", ".join(f"{t[4:].lower()}={c(t)}" for t in tactics),
          note="the owner's 20-minute look is a screen check")
    lanes_ok = Evidence() if not (c("RVR_LANE_ASSIGN") and c("siege.march_arrived")) else j.ev("siege.march_arrived")
    j.add("rvr", "lanes", "March lanes arrive (RVR_LANE_ASSIGN, a march reaching its keep)",
          SEEN if lanes_ok.count else NOT_SEEN, lanes_ok if lanes_ok.count else None,
          f"{c('RVR_LANE_ASSIGN')} assigned, {c('RVR_LANE_FALLBACK')} fallbacks, {c('RVR_LANE_REJECTED')} rejected, "
          f"{c('siege.march_arrived')} marches reached their keep", note="arrival by lane is not in the log")
    keep = j.ev("keep.taken.field")
    j.add("rvr", "sieges", "Sieges still happen and keeps change hands (RVR_SIEGE_STARTED, RVR_KEEP_TAKEN)",
          SEEN if c("RVR_SIEGE_STARTED") and keep.count else NOT_SEEN, keep if keep.count else None,
          f"{c('RVR_SIEGE_STARTED')} sieges, {keep.count} keeps taken in the field, "
          f"{c('keep.taken.bg')} in battlegrounds")
    loaded = c("RVR_CHECKSPOT_LOADED") and c("RVR_CHECKSPOT_VALIDATED") and c("RVR_LANE_LOADED")
    j.add("rvr", "catalogs", "Catalogs load and validate (RVR_CHECKSPOT_LOADED / _VALIDATED, RVR_LANE_LOADED)",
          CONTRADICTED if c("catalog.bad") else (SEEN if loaded else NOT_SEEN),
          j.ev("catalog.bad") if c("catalog.bad") else (j.ev("RVR_CHECKSPOT_VALIDATED") if loaded else None),
          "; ".join(sec.lists["lanes"][-1:]) or "no RVR_LANE_LOADED",
          note="whether they were built from a clean release world is not in the log")


def crafting(j: Judge, sec: Section, c):
    j.seen_if("crafting", "stations", "Bot crafters craft at stations (CRAFT_SESSION_START, \"Finished at the station\")",
              "craft.finished", f"{c('CRAFT_SESSION_START')} started, {c('craft.finished')} finished at the station, "
              f"{sec.n['craft.made']} pieces made")
    made, expired, withdrawn = sec.n["order.made"], sec.n["order.expired"], sec.n["order.withdrawn"]
    closed = made + expired
    detail = f"{made} made, {expired} expired, {withdrawn} withdrawn (ECON_ORDER)"
    if closed >= 20 and made * 2 < closed:
        j.add("crafting", "orders", "Craft orders are mostly filled", CONTRADICTED, j.ev("order.expired"),
              detail + f": {100 * made / closed:.0f}% of made+expired")
    elif closed and made * 2 >= closed:
        j.add("crafting", "orders", "Craft orders are mostly filled", SEEN, j.ev("order.made"),
              detail + f": {100 * made / closed:.0f}% of made+expired")
    else:
        j.add("crafting", "orders", "Craft orders are mostly filled", NOT_SEEN, None, detail)
    gov = sec.lists["governor"]
    gov_text = (f"governor smoothed {min(g[0] for g in gov):.2f}-{max(g[0] for g in gov):.2f} against target "
                f"{gov[-1][1]:.2f}" if gov else "no ECON_GOVERNOR")
    ledger = c("ECON_FLOWS") and c("ECON_SUPPLY")
    j.add("crafting", "economy", "The economy ledger writes (ECON_SUPPLY, ECON_FLOWS, ECON_GOVERNOR)",
          SEEN if ledger else NOT_SEEN, j.ev("ECON_FLOWS") if ledger else None,
          f"{c('ECON_SUPPLY')} supply, {c('ECON_FLOWS')} flows lines; {gov_text}",
          note="\"doesn't run away\" waits for the full-day report")
    j.seen_if("crafting", "no_strip", "No bot strips its own gear (ECON_UNDRESS_GUARD blocked=0)",
              "ECON_UNDRESS_GUARD", f"{c('ECON_UNDRESS_GUARD')} hourly checks, {sec.n['undress.checked']} bots checked, "
              f"{c('undress.blocked')} blocked", against="undress.blocked")
    j.seen_if("crafting", "exchange", "Crafted goods reach the Exchange (ECON_FLOWS exchange_crafted)",
              "exchange.crafted", f"{sec.n['exchange.crafted']} crafted of {sec.n['exchange.sales']} Exchange sales",
              must=False)
    j.seen_if("crafting", "wear", "Wear and repairs (ECON_WEAR repaired)", "wear.repaired",
              f"{c('ECON_WEAR')} lines, {sec.n['wear.below88']} below 88%, {sec.n['wear.repaired']} repaired", must=False)
    j.seen_if("crafting", "potions", "Potions bought and drunk (ECON_CONSUMABLE)", "potions.drunk",
              f"{sec.n['potions.bought']} bought, {sec.n['potions.drunk']} drunk", must=False)


def companions(j: Judge, sec: Section, c):
    j.seen_if("companions", "hire_word", "The hire words are used (HIRE_WORD_165)", "HIRE_WORD_165",
              f"{c('HIRE_WORD_165')} line(s)", must=False)
    j.seen_if("companions", "hired", "A companion is hired (HIRED_COMPANION)", "HIRED_COMPANION",
              f"{c('HIRED_COMPANION')} hired, {c('HIRED_COMPANION_SUMMONED')} summoned, "
              f"{c('HIRED_COMPANION_DISMISSED')} dismissed, {c('HIRED_COMPANION_REVIVED')} revived, "
              f"{c('HIRED_COMPANION_RELEASED')} released", must=False,
              against="HIRED_COMPANION_DELETE_FAILED")
    j.seen_if("companions", "revived", "A dead companion is revived (HIRED_COMPANION_REVIVED)", "HIRED_COMPANION_REVIVED",
              f"{c('HIRED_COMPANION_REVIVED')} line(s)", must=False)
    j.seen_if("companions", "parked", "A parked companion comes back at login (HIRED_COMPANION_LOGOUT parked>0, "
              "LOGIN summoning>0, #315)", "companion.back",
              f"{c('companion.parked')} logouts with one parked, {c('companion.back')} logins summoning, "
              f"{c('HIRED_COMPANION_ORPHAN')} orphans", must=False, against="HIRED_COMPANION_ORPHAN")


def era(j: Judge, sec: Section, c):
    j.seen_if("era", "loaded", "Era builds load: ERA_BUILDS_LOADED loaded=yes customClass=never dropped=0",
              "era.loaded_ok", f"{c('ERA_BUILDS_INVALID')} ERA_BUILDS_INVALID", against="era.loaded_bad")
    draws = {k[9:]: v for k, v in sec.n.items() if k.startswith("era.draw.")}
    total = sum(draws.values())
    offmeta = f"{100 * draws.get('offmeta', 0) / total:.0f}% offmeta" if total else "no draws"
    by_draw = ", ".join(f"{k}={v}" for k, v in sorted(draws.items()))
    j.seen_if("era", "assigned", "New bots are era-built (ERA_BUILD_ASSIGNED, about 10% draw=offmeta)",
              "ERA_BUILD_ASSIGNED", f"{total} assigned ({offmeta}" + (f"; {by_draw})" if by_draw else ")"),
              against="era.custom_assigned", against_detail="a Sluaghbinder was given an era build")
    era_draws = draws.get("era", 0)
    trained_detail = f"{c('era.trained_yes')} match=yes, {c('era.trained_no')} not; {era_draws} draw=era assignment(s)"
    if era_draws == 0 and not (c("era.trained_yes") or c("era.trained_no")):
        # TRAINED is written only for a draw=era build reaching 50 (EraLifetimeBuild.FirstTrainedAtFifty)
        j.add("era", "trained", "ERA_BUILD_TRAINED match=yes", NOT_APPLICABLE, None,
              trained_detail + ": only a draw=era bot logs it", note="judged on a world with draw=era bots (criterion 1)")
    else:
        j.seen_if("era", "trained", "ERA_BUILD_TRAINED match=yes", "era.trained_yes", trained_detail,
                  against="era.trained_no")
    counted, retired, acts = sec.n["era.census_custom_max"], len(sec.retired), j.ev("era.retired_acts")
    census_row = "No custom-class bots play: ERA_BUILD_CENSUS Sluaghbinder= no more than BOT_CUSTOM_CLASS_RETIRED"
    census_detail = (f"{c('ERA_BUILD_CENSUS')} census line(s); Sluaghbinder counted {counted}, retired {retired}; "
                     f"{acts.count} line(s) of a retired one playing")
    census_note = "the census counts retired custom-class bots too; Sluaghbinder=0 is for a world with none"
    if acts.count:
        j.add("era", "census", census_row, CONTRADICTED, acts, census_detail, note=census_note)
    elif counted > retired:
        bad = Evidence()
        bad.add(*sec.lists["era.census_custom_line"][0])
        j.add("era", "census", census_row, CONTRADICTED, bad, census_detail + f": {counted - retired} not retired",
              note=census_note)
    elif c("ERA_BUILD_CENSUS"):
        j.add("era", "census", census_row, SEEN, j.ev("ERA_BUILD_CENSUS"), census_detail, note=census_note)
    else:
        j.add("era", "census", census_row, NOT_SEEN, None, census_detail, note=census_note)
    j.seen_if("era", "retired", "Custom-class gamebots kept and not played (BOT_CUSTOM_CLASS_RETIRED)",
              "BOT_CUSTOM_CLASS_RETIRED", f"{c('BOT_CUSTOM_CLASS_RETIRED')} line(s)", must=False,
              note="only on a world that has Sluaghbinder gamebots")
    copies_row = "Buffbot copies from earlier builds retired once and never played (BOT_BUFFBOT_COPIES)"
    copies_detail = (f"{'; '.join(sec.lists['copies_summary']) or 'no summary'}; {len(sec.copies)} retired, "
                     f"{c('BOT_BUFFBOT_COPY_SUSPECTED')} suspected (left as they are); "
                     f"{c('copies.acts')} line(s) of a retired copy playing")
    copies_note = "once per world, on the first start of a build with the repair; a fresh world logs retired=0 found=0"
    if c("copies.acts"):
        j.add("era", "buffbot_copies", copies_row, CONTRADICTED, j.ev("copies.acts"), copies_detail, must=False,
              note=copies_note)
    else:
        j.seen_if("era", "buffbot_copies", copies_row, "BOT_BUFFBOT_COPIES", copies_detail, must=False, note=copies_note)


def people(j: Judge, sec: Section, c):
    j.seen_if("people", "summary", "Bots as people runs (BOT_PEOPLE_SUMMARY)", "BOT_PEOPLE_SUMMARY",
              f"{c('BOT_PEOPLE_SUMMARY')} summaries")
    tiers = [sec.n["tier.casual"], sec.n["tier.average"], sec.n["tier.veteran"]]
    n = sum(tiers)
    shares = [100 * x / n for x in tiers] if n else [0, 0, 0]
    off = max(abs(s - w) for s, w in zip(shares, SKILL_SHARES)) if n else 0
    detail = f"{n} profiles: " + "/".join(f"{s:.0f}" for s in shares) + " casual/average/veteran (30/50/20 wanted)"
    if not n:
        j.add("people", "skill_profile", "Skill tiers within 5 points of 30/50/20 (BOT_SKILL_PROFILE)", NOT_SEEN, None,
              detail)
    elif off <= 5:
        j.add("people", "skill_profile", "Skill tiers within 5 points of 30/50/20 (BOT_SKILL_PROFILE)", SEEN,
              j.ev("BOT_SKILL_PROFILE"), detail)
    else:
        j.add("people", "skill_profile", "Skill tiers within 5 points of 30/50/20 (BOT_SKILL_PROFILE)",
              CONTRADICTED if n >= 100 else NOT_SEEN, j.ev("BOT_SKILL_PROFILE"),
              detail + ("" if n >= 100 else ": too few to judge"))
    j.seen_if("people", "skill_growth", "A bot that played grows a tier (BOT_SKILL_GROWTH)", "BOT_SKILL_GROWTH",
              f"{c('BOT_SKILL_GROWTH')} line(s)", note="\"after the overnight\" is the session's length")
    out_m, out_d, in_m, in_d = (sec.n[k] for k in ("mistakes.out.m", "mistakes.out.d", "mistakes.in.m", "mistakes.in.d"))
    rate_out = out_m / out_d if out_d else 0.0
    rate_in = in_m / in_d if in_d else 0.0
    ratio = rate_in / rate_out if rate_out else None
    mistakes_detail = (f"{c('BOT_SKILL_MISTAKE')} mistakes, {c('mistake.spared')} spared, {c('BOT_SKILL_MISTAKES')} "
                       f"summaries; rate_out={rate_out:.3f} ({out_m}/{out_d}) rate_in={rate_in:.3f} ({in_m}/{in_d})"
                       + (f", in/out={ratio:.2f} (about 0.25 wanted)" if ratio is not None else ""))
    if ratio is None or in_d < MISTAKES_MIN_IN_GROUP:
        status = NOT_SEEN
        mistakes_detail += f": too few decisions in the player's group to judge (under {MISTAKES_MIN_IN_GROUP})" \
            if rate_out else ""
    elif ratio > MISTAKES_MAX_RATIO:
        status = CONTRADICTED
    elif ratio < MISTAKES_MIN_RATIO:
        status = NOT_SEEN
        mistakes_detail += f": below {MISTAKES_MIN_RATIO} (the quarter rate wants mistakes in the group too)"
    else:
        status = SEEN if c("mistake.spared") else NOT_SEEN
    j.add("people", "mistakes", "Mistakes at about a quarter of the strangers' rate in the player's group "
          "(BOT_SKILL_MISTAKES rate_in against rate_out; BOT_SKILL_MISTAKE spared=true)", status,
          j.ev("mistake.spared") if c("mistake.spared") else (j.ev("BOT_SKILL_MISTAKES") if c("BOT_SKILL_MISTAKES") else None),
          mistakes_detail, note="the deaths within 30 s of a pull mistake are compared by the log review")
    learn = sec.lists["raid_learn"]
    runs = {}                   # raid -> [(run, seenShare at the run's first line)], in log order
    for raid, share, run in learn:
        if run not in (r for r, _ in runs.setdefault(raid, [])):
            runs[raid].append((run, share))
    rose = sorted(raid for raid, firsts in runs.items() if any(share > firsts[0][1] for _, share in firsts[1:]))
    j.add("people", "raid_learn", "Raid learning: RAID_LEARN seenShare rising between runs", SEEN if rose else NOT_SEEN,
          j.ev("RAID_LEARN") if learn else None,
          f"{len(learn)} line(s) in {sum(len(r) for r in runs.values())} run(s)"
          + (f", seenShare {min(x[1] for x in learn):.2f}-{max(x[1] for x in learn):.2f}" if learn else "")
          + (f", rose between runs in {', '.join(rose)}" if rose else ", no rise between runs yet"),
          note="each run is compared by its first RAID_LEARN line; that both runs ended in a correct win or loss is the "
               "log review's")
    both = c("place.seeded") and c("place.visited")
    j.add("people", "places", "Places known: BOT_PLACE_LEARNED how=seeded, then how=visited",
          SEEN if both else NOT_SEEN, j.ev("place.visited") if both else None,
          f"seeded={c('place.seeded')} visited={c('place.visited')} told={c('place.told')}")
    pairs = c("BOT_WAY_ASK") and c("BOT_WAY_TOLD")
    long = j.ev("way.wrong_road_long")
    j.add("people", "way", "Asking the way: BOT_WAY_ASK and BOT_WAY_TOLD; wrong roads cost 180 s or less",
          CONTRADICTED if long.count else (SEEN if pairs else NOT_SEEN),
          long if long.count else (j.ev("BOT_WAY_TOLD") if pairs else None),
          f"{c('BOT_WAY_ASK')} asks, {c('BOT_WAY_TOLD')} told, {c('BOT_WAY_FOLLOW')} follows, "
          f"{c('BOT_WAY_WRONG_ROAD')} wrong roads ({long.count} over 180 s)",
          note="stuck-watchdog recoveries against the session before are compared by the log review")
    moves = c("BOT_MOVE_HABITS") and (c("BOT_MOVE_WALL") or c("BOT_MOVE_SIT")) and \
        all(sec.n[f"move.{name}"] > 0 for name in MOVE_REQUIRED)
    j.add("people", "movement", "Movement habits: BOT_MOVE_HABITS with sits, stickLost and overshoots above 0, and "
          "BOT_MOVE_WALL / BOT_MOVE_SIT",
          SEEN if moves else NOT_SEEN, j.ev("BOT_MOVE_WALL") if c("BOT_MOVE_WALL") else (j.ev("BOT_MOVE_SIT") if moves else None),
          ", ".join(f"{t[9:].lower()}={c(t)}" for t in ("BOT_MOVE_HABITS", "BOT_MOVE_STRAFE", "BOT_MOVE_STICK_LOST",
                                                         "BOT_MOVE_OVERSHOOT", "BOT_MOVE_SIT", "BOT_MOVE_WALL"))
          + "; counted " + " ".join(f"{name}={sec.n['move.' + name]}" for name in MOVE_COUNTERS),
          note="the sit emote in the client is a screen check")
    ms = c("BOT_MILESTONE_TALK") and c("BOT_MILESTONE_REACHED")
    false_claims = c("milestone.false_claim")
    j.add("people", "milestones", "Milestones talked about: BOT_MILESTONE_TALK and BOT_MILESTONE_REACHED, each talk "
          "point true to the bot's logged experience or realm points",
          CONTRADICTED if false_claims else (SEEN if ms else NOT_SEEN),
          j.ev("milestone.false_claim") if false_claims else (j.ev("BOT_MILESTONE_TALK") if ms else None),
          f"arc={c('BOT_MILESTONE_ARC')} progress={c('BOT_MILESTONE_PROGRESS')} reached={c('BOT_MILESTONE_REACHED')} "
          f"talk={c('BOT_MILESTONE_TALK')} summary={c('BOT_MILESTONE_SUMMARY')} false_claims={false_claims}",
          note="the Herald page is a screen check")


def chat(j: Judge, sec: Section, c):
    made, kept, timeout = c("promise.made"), c("promise.kept"), c("promise.broken.timeout")
    other = c("promise.broken") - timeout
    judged = kept + timeout
    share = kept / judged if judged else None
    unsaid = c("promise.broken.unsaid")
    detail = (f"made={made} kept={kept} broken={c('promise.broken')} (timeout={timeout}, unsaid={unsaid}, other={other - unsaid}) "
              f"refused={c('promise.refused')}" + (f", kept {100 * share:.0f}% of kept+timeout ({PROMISE_KEPT_BAR:.0%} wanted)"
                                                    if share is not None else ""))
    row = "A bot that says it is coming arrives (CHAT_PROMISE made, then kept; timeouts under 30%)"
    note = ("a broken promise for died, taken, logged_out, keep_fell or asker_gone is an honest end, not counted against, "
            "and one broken unsaid had its line never sent (no one heard the omw); "
            "that no bot says \"omw\" without a made line is a chat-window check")
    if judged >= PROMISE_MIN_JUDGED and share < PROMISE_KEPT_BAR:
        j.add("chat", "promises", row, CONTRADICTED, j.ev("promise.broken.timeout"), detail, note=note)
    elif kept:
        j.add("chat", "promises", row, SEEN, j.ev("promise.kept"), detail, note=note)
    else:
        j.add("chat", "promises", row, NOT_SEEN, j.ev("promise.made") if made else None, detail, note=note)


def stalls_and_errors(j: Judge, sec: Section, c):
    stalls = merged_stalls(sec)
    hard = [s for s in stalls if s[1] >= HARD_STALL_MS]
    peak, peak_from = worst_hour([s[0] for s in stalls]) if stalls else (0, None)
    start, end = sec.run_start(), sec.run_end()
    length = (end - start).total_seconds() if start and end else 0
    detail = (f"{hours(length)} from {'startup' if sec.startup_completed else 'the first line'}"
              f"{' to the stop' if sec.stopping else ' to the last line'}; {len(stalls)} stall(s) of 1.0 s or more, "
              f"{len(hard)} of 2.0 s or more, at most {peak} in an hour"
              + (f" (from {stamp(peak_from)[6:]})" if peak > MAX_STALLS_PER_HOUR else ""))
    worst = max(stalls, key=lambda s: s[1]) if stalls else None
    ev = Evidence()
    if hard:
        ev.add(hard[0][0], hard[0][2])
    elif peak > MAX_STALLS_PER_HOUR:
        ev.add(peak_from, next(s[2] for s in stalls if s[0] == peak_from))
    elif worst:
        ev.add(worst[0], worst[2])
    row = "Overnight: 8 hours, no stall of 2.0 s or more, at most 5 of 1.0 s or more in any hour (owner decision 9)"
    if hard or peak > MAX_STALLS_PER_HOUR:
        j.add("stalls", "overnight", row, CONTRADICTED, ev, detail)
    elif length >= GATE_HOURS * 3600 and sec.judged_from and sec.judged_from <= (start or sec.judged_from):
        j.add("stalls", "overnight", row, SEEN, ev if ev.count else None, detail,
              note="check_overnight_run.py is the gate's tool (SERVER_WORK gaps, the stop)")
    else:
        j.add("stalls", "overnight", row, NOT_SEEN, ev if ev.count else None, detail + ": shorter than 8 hours"
              if length < GATE_HOURS * 3600 else detail + ": --since cut the run's start")
    stop = sec.stop_play
    by_logger = ", ".join(f"{k} {v}" for k, v in sec.err_after.most_common(4))
    j.add("errors", "stop_play", "No error that stops play (FTL, game-loop critical error, a player kicked, watchdog)",
          CONTRADICTED if stop.count else (SEEN if sec.startup_completed else NOT_SEEN), stop if stop.count else None,
          f"{sum(sec.err_after.values())} ERR after startup ({by_logger or 'none'}); {sec.err_known} known start-up "
          f"NPC template/spell/door/merchant lines and {sec.err_before} other start-up ERR not counted")


# ---- Output ------------------------------------------------------------------------------------------------------

def section_summary(sec: Section) -> dict:
    start, end = sec.run_start(), sec.run_end()
    return {"index": sec.index, "build": sec.commit7 or None, "commit": sec.commit, "name": sec.build_name,
            "sluaghbinder": sec.fork_fields.get("sluaghbinder"), "fork_build": stamp(sec.fork_build_t) if sec.commit else None,
            "first_line": stamp(sec.first_t), "last_line": stamp(sec.last_t), "lines": [sec.first_line_no, sec.last_line_no],
            "startup_completed": stamp(sec.startup_completed) if sec.startup_completed else None,
            "clean_stop": sec.stopping is not None, "judged_from": stamp(sec.judged_from) if sec.judged_from else None,
            "length": hours((end - start).total_seconds()) if start and end else None}


def report(sections: list[Section], build: str | None) -> list[dict]:
    out = []
    for sec in sections:
        summary = section_summary(sec)
        if not sec.judged:
            summary["judged"] = False
            summary["why"] = "no FORK_BUILD line in this part of the log: not judged"
        elif build and not sec.commit.startswith(build):
            continue
        else:
            summary["judged"] = True
            summary["raids"] = [r.as_dict() for r in sec.raids]
            summary["checks"] = judge(sec) if sec.judged_from else []
            if not sec.judged_from:
                summary["why"] = "every line of this build is before --since"
        out.append(summary)
    return out


def status_text(check, build):
    if check["status"] == SEEN:
        return f"Seen in log on build {build} ({check['at'][6:]})" if check["at"] else f"Seen in log on build {build}"
    if check["status"] == CONTRADICTED:
        return f"Contradicted in log on build {build}" + (f" ({check['at'][6:]})" if check["at"] else "")
    if check["status"] == NOT_APPLICABLE:
        return f"Not applicable on build {build}"
    return f"Not seen in log on build {build}"


def text_report(path: Path, results: list[dict]) -> str:
    lines = [f"Played-check evidence from {path.name}: what the log shows, per build. Evidence only, not a verdict "
             f"(a row counts once seen on the owner's PC; screen checks are not in the log)."]
    for r in results:
        lines.append("")
        if not r["judged"]:
            lines.append(f"Part {r['index']}: lines {r['lines'][0]}-{r['lines'][1]}, {r['first_line']} to {r['last_line']}: "
                         f"{r['why']}.")
            continue
        build = r["build"]
        lines.append(f"Build {build} ({r['name']}, sluaghbinder={r['sluaghbinder'] or '?'}): FORK_BUILD {r['fork_build']}, "
                     f"lines {r['lines'][0]}-{r['lines'][1]}, {r['first_line']} to {r['last_line']}"
                     f" ({r['length']}), {'clean stop' if r['clean_stop'] else 'no stop line'}"
                     + (f"; judged from {r['judged_from']}" if r["judged_from"] and r["judged_from"] != r["first_line"] else ""))
        if r.get("why"):
            lines.append(f"  {r['why']}")
            continue
        if r["raids"]:
            lines.append("  Raids:")
            for x in r["raids"]:
                started = (f"started {x['started'][6:]} present={x['present']}/{x['required']}"
                           f"{' forced' if x['forced'] == 'True' else ''}" if x["started"]
                           else "no REALM_RAID_STARTED (ended in staging, or started before this part of the log)")
                ended = f"ended {x['ended'][6:]} {x['outcome']} = {x['result']}" if x["ended"] else x["result"]
                extra = []
                if x["minutes"] is not None:
                    extra.append(f"{x['minutes']} min")
                if x["named"] is not None:
                    extra.append(f"{x['named']} named")
                extra.append(f"{x['wipes']} wipe(s)" + (f" at {', '.join(dict.fromkeys(x['wipe_bosses']))}"
                                                        if x["wipe_bosses"] else ""))
                if x["stuck_return_waves"]:
                    extra.append(f"{x['stuck_return_waves']} stuck return wave(s)")
                if x["stall_lines"]:
                    extra.append(f"{x['stall_lines']} REALM_RAID_STALLED")
                if x["teleports_near_resting_boss"]:
                    extra.append(f"{x['teleports_near_resting_boss']} teleport(s) near a resting boss")
                lines.append(f"    {x['event']}: {started}; {ended}; {', '.join(extra)}")
        area = None
        for ch in r["checks"]:
            if ch["area"] != area:
                area = ch["area"]
                lines.append(f"  {area.capitalize()}:")
            must = "must " if ch["must"] else ""
            lines.append(f"    [{status_text(ch, build)}] {must}{ch['row']}")
            if ch["detail"]:
                lines.append(f"        {ch['detail']}")
            if ch["line"]:
                lines.append(f"        > {ch['line']}")
            if ch["note"]:
                lines.append(f"        note: {ch['note']}")
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("log", help="a server.log (or a staged copy of it)")
    parser.add_argument("--since", help='only judge lines at or after "MM/DD HH:MM" (the build is still read before it)')
    parser.add_argument("--build", help="only report the build whose commit starts with this (e.g. 4be6fab)")
    parser.add_argument("--json", action="store_true", help="JSON instead of text")
    parser.add_argument("--year", type=int, help="the log's year (lines carry no year; default this year)")
    parser.add_argument("--near", type=float, default=RESTING_BOSS_NEAR,
                        help=f"distance to a resting boss that flags a stranded teleport (default {RESTING_BOSS_NEAR:.0f})")
    args = parser.parse_args(argv)
    path = Path(args.log)
    if not path.is_file():
        print(f"no such file: {path}", file=sys.stderr)
        return 2
    year = args.year or dt.date.today().year
    since = parse_since(args.since, year) if args.since else None
    sections = read_log(path, since=since, year=year, near=args.near)
    results = report(sections, args.build)
    judged = [r for r in results if r["judged"]]
    if args.json:
        print(json.dumps({"log": str(path), "since": args.since, "build": args.build, "sections": results}, indent=2))
    else:
        print(text_report(path, results))
    if not judged:
        if not args.json:
            print("\nNothing judged: " + (f"no FORK_BUILD for build {args.build}." if args.build else
                                          "no FORK_BUILD line in the log."), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
