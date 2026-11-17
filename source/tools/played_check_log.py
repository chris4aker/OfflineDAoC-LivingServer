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
            "Stopped by player": "stopped by player", "Failed rally": "failed rally"}
SKILL_SHARES = (30.0, 50.0, 20.0)   # casual / average / veteran (BOTS-AS-PEOPLE-1.2, WP1 played check)
MISTAKES_MIN_IN_GROUP = 100         # chance decisions in the player's group before the quarter rate is judged
MISTAKES_MAX_RATIO = 0.5            # rate_in / rate_out above this contradicts "about a quarter" (WP2 played check)


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
        self.last_retreat = {}            # group -> reason of its last RVR_RETREAT
        self.relay_sends = {}             # group -> times it relayed (relayedTo>0) in the last 60 s
        self.recent_sends = deque()       # (time, group, (realm, region)) of relays in the last 60 s

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


simple("CUSTOM_CLASS_REFUSED", "REALM_RAID_TALE_FIRST", "REALM_RAID_TALE", "REALM_RAID_EPIC_RESPAWN_HELD",
       "REALM_EXPEDITION_MUSTER_GUARDED", "REALM_EXPEDITION_MUSTER_UNSAFE", "REALM_EXPEDITION_MUSTER_MOVED",
       "EPIC_SWEEP_ADD", "EPIC_SWEEP_RAID_START", "EPIC_SWEEP_RAID_DONE", "EPIC_SWEEP_RAID_NOT_STARTED",
       "EPIC_SWEEP_HELD", "RVR_SPOT", "RVR_ROAM_CHECK", "RVR_ROAM_HUNT", "RVR_ROAM_MOVE_ON", "RVR_LANE_FALLBACK",
       "RVR_LANE_REJECTED", "RVR_ASSIST_CALL", "RVR_ASSIST_SWITCH", "RVR_CC_ASSIGN", "RVR_INTERRUPT", "RVR_PEEL",
       "RVR_KITE", "RVR_STEALTH_OPEN", "RVR_PARTY_FORMED", "CRAFT_SESSION_START", "ECON_SUPPLY",
       "HIRE_WORD_165", "HIRED_COMPANION", "HIRED_COMPANION_SUMMONED", "HIRED_COMPANION_DISMISSED",
       "HIRED_COMPANION_REVIVED", "HIRED_COMPANION_RELEASED", "HIRED_COMPANION_ORPHAN",
       "HIRED_COMPANION_DELETE_FAILED", "BOT_CUSTOM_CLASS_RETIRED", "BOT_PEOPLE_SUMMARY", "BOT_SKILL_BUDGET",
       "BOT_SKILL_GROWTH", "RAID_MECHANIC_SEEN", "BOT_PLACE_BUDGET", "BOT_WAY_FOLLOW",
       "BOT_WAY_SUMMARY", "BOT_MOVE_STRAFE", "BOT_MOVE_STICK_LOST", "BOT_MOVE_OVERSHOOT",
       "BOT_MOVE_SIT", "BOT_MOVE_WALL", "BOT_MILESTONE_ARC", "BOT_MILESTONE_PROGRESS", "BOT_MILESTONE_REACHED",
       "BOT_MILESTONE_TALK", "ERA_BUILDS_INVALID")


@handles("REALM_RAID_STARTED")
def _raid_started(sec, t, message, level, near):
    f = kv(message)
    event = f.get("event", "?")
    run = RaidRun(event)
    sec.raids.append(run)
    sec.open_raids[event] = run
    run.started = t
    run.present, run.required, run.forced = f.get("present"), f.get("required"), f.get("forced")
    sec.ev["REALM_RAID_STARTED"].add(t, message)


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
    outcome = f.get("outcome", "")
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
    f = kv(message)
    step = f.get("step")
    if step == "stuck":
        run = sec.raid(f.get("event", "?"), t)
        run.stuck_waves += 1
        sec.ev["return.stuck"].add(t, message)
    elif step in ("formed", "moving", "joined") or "waited_s" in f:
        sec.ev["return.wave"].add(t, message)


@handles("REALM_EXPEDITION_ENCOUNTER_RESET")
def _encounter_reset(sec, t, message, level, near):
    if "no attempt is used" in message:
        sec.ev["raid.reset_no_attempt"].add(t, message)
    else:
        sec.ev["raid.reset_other"].add(t, message)


@handles("REALM_EXPEDITION_FINAL_ENGAGE")
def _final_engage(sec, t, message, level, near):
    f = kv(message)
    if num(f.get("mustered")) >= num(f.get("needed")):
        sec.ev["pull.at_needed"].add(t, message)
    else:
        sec.ev["pull.below_needed"].add(t, message)


@handles("REALM_EXPEDITION_ROUTE_GATE")
def _route_gate(sec, t, message, level, near):
    f = kv(message)
    if f.get("event") and f.get("into"):
        sec.region_events[f["event"]] = int(num(f["into"]))
    if "a living boss covers the walk" in message:
        sec.ev["route.boss_objective"].add(t, message)


@handles("REALM_EXPEDITION_TARGET_STATUS")
def _target_status(sec, t, message, level, near):
    f = kv(message)
    region, target, at = f.get("region"), f.get("target"), point(f.get("at"))
    if region is None or target is None or at is None:
        return
    sec.bosses[region][target] = (f.get("inCombat") == "True", num(f.get("health"), 100.0), at, t, f.get("level"))


@handles("REALM_EXPEDITION_TARGET_DEFEATED")
def _target_defeated(sec, t, message, level, near):
    f = kv(message)
    sec.bosses[f.get("region")].pop(f.get("target"), None)


@handles("REALM_EXPEDITION_STRANDED_REGROUP")
def _stranded(sec, t, message, level, near):
    f = kv(message)
    region, to = f.get("region"), point(f.get("to"))
    sec.ev["stranded.all"].add(t, message)
    if to is None:
        return
    for target, (in_combat, health, at, seen, lvl) in sec.bosses.get(region, {}).items():
        if in_combat or (t - seen).total_seconds() > STATUS_STALE_SECONDS:
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
    relay its own retreat or its own regroup once; a relayed stance is never passed on again."""
    regroup = message.startswith("RVR_REGROUP ")
    f = kv(message)
    if regroup and f.get("phase") != "start":
        return
    group, reason = f.get("group", "?"), f.get("reason") or ""
    where = (f.get("realm"), f.get("region"))
    relayed = reason.startswith("relayed")
    sends = f.get("relay") == "sent" or num(f.get("relayedTo")) > 0
    reached = num(f.get("relayedTo")) > 0
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
    # (c) a relay back at its sender within 60 s: this group sent one, another group here sent one since, and now
    # this group takes up a relayed stance
    if relayed:
        mine = [s for s in sec.relay_sends.get(group, ()) if 0 < (t - s).total_seconds() <= 60]
        if mine:
            first = min(mine)
            if any(g != group and w == where and first < s and (t - s).total_seconds() <= 15
                   for s, g, w in sec.recent_sends):
                sec.ev["regroup.c_returned"].add(t, f"{message.rstrip()} [group {group} relayed at {stamp(first)[6:]}]")
    if reached:
        sec.relay_sends.setdefault(group, []).append(t)
        sec.relay_sends[group] = [s for s in sec.relay_sends[group] if (t - s).total_seconds() <= 60]
        sec.recent_sends.append((t, group, where))
        while sec.recent_sends and (t - sec.recent_sends[0][0]).total_seconds() > 60:
            sec.recent_sends.popleft()
    if not regroup:
        sec.last_retreat[group] = reason


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
    sec.ev["ERA_BUILD_CENSUS"].add(t, message)
    m = re.search(r"\bSluaghbinder=(\d+)", message)
    if m and int(m.group(1)) > 0:
        sec.ev["era.census_custom"].add(t, message)


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
    sec.lists["raid_learn"].append((f.get("raid"), num(f.get("seenShare"))))


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
    """One per minute: SERVER_STALL and AI_PROFILE lines within 75 s count once (the longer); GC-only minutes dropped."""
    events = sorted(sec.stalls, key=lambda s: s[0])
    out = []
    for s in events:
        if out and out[-1][3] != s[3] and abs((s[0] - out[-1][0]).total_seconds()) <= PAIR_SECONDS:
            if s[1] > out[-1][1]:
                out[-1] = s
            continue
        out.append(s)
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
    j.seen_if("raids", "return_wave", "After a wipe the raiders gather at the way in and go back in together (#195)",
              "return.wave", f"{c('return.wave')} return-wave lines, {c('return.stuck')} stuck",
              against="return.stuck", against_detail=f"{c('return.stuck')} return wave(s) stuck "
                                                      f"({c('return.wave')} other return-wave lines)")
    j.seen_if("raids", "pull_70", "A pull waits for 70% of the living raid (#195)", "pull.at_needed",
              f"{c('pull.at_needed')} final engages at the needed count, {c('pull.below_needed')} below it",
              against="pull.below_needed")
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
          f"TARGET_STATUS had inCombat=False",
          note="a boss's state is its last TARGET_STATUS (one a minute, the raid's targets only)")
    sweep = j.ev("EPIC_SWEEP_START")
    j.add("raids", "sweep", "/epictest sweep progress (EPIC_SWEEP_*)", SEEN if sweep.count else NOT_SEEN, sweep,
          f"starts: {'; '.join(sec.lists['sweep_start']) or 'none'}; {c('EPIC_SWEEP_ADD')} add, "
          f"{c('EPIC_SWEEP_RAID_START')} raid starts, {c('EPIC_SWEEP_RAID_DONE')} done, "
          f"{c('EPIC_SWEEP_RAID_NOT_STARTED')} not started, {c('EPIC_SWEEP_HELD')} held; "
          f"end: {'; '.join(sec.lists['sweep_end']) or 'none in this build'}", must=False)


def rvr(j: Judge, sec: Section, c):
    span = max((sec.run_end() - sec.run_start()).total_seconds(), 60.0) if sec.run_start() else 60.0
    starts, relay_started = c("RVR_REGROUP"), c("regroup.relay_started")
    share = 100 * relay_started / starts if starts else 0.0
    counts = {k: c(f"regroup.{k}") for k in ("a_relayed_sends", "b_echo_source", "c_returned")}
    over = starts and share > 10
    detail = (f"{starts} regroup starts in {hours(span)} ({starts / (span / 3600):.0f}/h), {relay_started} started by "
              f"a relay ({share:.1f}%, at most 10%); (a) relayed stance relayed again: {counts['a_relayed_sends']}, "
              f"(b) regroup ending a relayed retreat relayed: {counts['b_echo_source']}, (c) relay back at its sender "
              f"within 60 s: {counts['c_returned']}")
    bad = [j.ev(f"regroup.{k}") for k, n in counts.items() if n]
    if over:
        bad.append(j.ev("regroup.relay_started"))
    row = "Regroups no longer flap: one hop, no echo, relay-started regroups 10% of starts or less (RVR_REGROUP)"
    if bad:
        j.add("rvr", "regroup", row, CONTRADICTED, min(bad, key=lambda e: e.first_t), detail,
              note="relayedTo>0 on a group's own retreat or own regroup is allowed (#304: one hop)")
    elif starts:
        j.add("rvr", "regroup", row, SEEN, j.ev("RVR_REGROUP"), detail)
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
    j.seen_if("era", "trained", "ERA_BUILD_TRAINED match=yes", "era.trained_yes",
              f"{c('era.trained_yes')} match=yes, {c('era.trained_no')} not", against="era.trained_no")
    j.seen_if("era", "census", "No custom-class bots: ERA_BUILD_CENSUS Sluaghbinder=0", "ERA_BUILD_CENSUS",
              f"{c('ERA_BUILD_CENSUS')} census line(s), {c('era.census_custom')} with Sluaghbinder above 0",
              against="era.census_custom")
    j.seen_if("era", "retired", "Custom-class gamebots kept and not played (BOT_CUSTOM_CLASS_RETIRED)",
              "BOT_CUSTOM_CLASS_RETIRED", f"{c('BOT_CUSTOM_CLASS_RETIRED')} line(s)", must=False,
              note="only on a world that has Sluaghbinder gamebots")


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
    else:
        status = SEEN if c("mistake.spared") else NOT_SEEN
    j.add("people", "mistakes", "Mistakes at about a quarter of the strangers' rate in the player's group "
          "(BOT_SKILL_MISTAKES rate_in against rate_out; BOT_SKILL_MISTAKE spared=true)", status,
          j.ev("mistake.spared") if c("mistake.spared") else (j.ev("BOT_SKILL_MISTAKES") if c("BOT_SKILL_MISTAKES") else None),
          mistakes_detail, note="the deaths within 30 s of a pull mistake are compared by the log review")
    learn = sec.lists["raid_learn"]
    first, rose = {}, set()
    for raid, share in learn:   # in log order: a raid's seenShare above its first one
        if share > first.setdefault(raid, share):
            rose.add(raid)
    rose = sorted(rose)
    j.add("people", "raid_learn", "Raid learning: RAID_LEARN seenShare rising between runs", SEEN if rose else NOT_SEEN,
          j.ev("RAID_LEARN") if learn else None,
          f"{len(learn)} line(s)" + (f", seenShare {min(x[1] for x in learn):.2f}-{max(x[1] for x in learn):.2f}"
                                     if learn else "") + (f", rose in {', '.join(rose)}" if rose else ", no rise yet"),
          note="that the rise came between the two runs, and that both ended in a correct win or loss, is the log review's")
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
    moves = c("BOT_MOVE_HABITS") and (c("BOT_MOVE_WALL") or c("BOT_MOVE_SIT"))
    j.add("people", "movement", "Movement habits: BOT_MOVE_HABITS with BOT_MOVE_WALL / BOT_MOVE_SIT",
          SEEN if moves else NOT_SEEN, j.ev("BOT_MOVE_WALL") if c("BOT_MOVE_WALL") else (j.ev("BOT_MOVE_SIT") if moves else None),
          ", ".join(f"{t[9:].lower()}={c(t)}" for t in ("BOT_MOVE_HABITS", "BOT_MOVE_STRAFE", "BOT_MOVE_STICK_LOST",
                                                         "BOT_MOVE_OVERSHOOT", "BOT_MOVE_SIT", "BOT_MOVE_WALL"))
          + "; counted " + " ".join(f"{name}={sec.n['move.' + name]}" for name in MOVE_COUNTERS),
          note="the sit emote in the client is a screen check")
    ms = c("BOT_MILESTONE_TALK") and c("BOT_MILESTONE_REACHED")
    j.add("people", "milestones", "Milestones talked about: BOT_MILESTONE_TALK and BOT_MILESTONE_REACHED",
          SEEN if ms else NOT_SEEN, j.ev("BOT_MILESTONE_TALK") if ms else None,
          f"arc={c('BOT_MILESTONE_ARC')} progress={c('BOT_MILESTONE_PROGRESS')} reached={c('BOT_MILESTONE_REACHED')} "
          f"talk={c('BOT_MILESTONE_TALK')}", note="the Herald page is a screen check")


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
