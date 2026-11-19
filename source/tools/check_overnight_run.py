#!/usr/bin/env python3
"""Checks one server run against the 1.2 overnight stability gate (docs/PLAYED-CHECK-1.2.md, owner decision 9).

The gate: "One overnight run without a stall or an error that stops play: 8 hours, no game-loop stall of 2.0 s or
more, at most 5 stalls of 1.0 s or more in any hour."

    python source/tools/check_overnight_run.py runtime/server/logs
    python source/tools/check_overnight_run.py server.2026-11-14.1.log server.2026-11-15.0.log server.log --commit 1dc66b6
    python source/tools/check_overnight_run.py runtime/server/logs --list-runs
    python source/tools/check_overnight_run.py runtime/server/logs --json

Give it the server logs (server.log and the rotated server.YYYY-MM-DD.N.log files, or the logs folder). They are put in
order by name (rotated files by date and number, server.log last), so the order on the command line does not matter.
server.log holds every warning and error (GameServer/config/logconfig.xml), so warn.log is read only when no server log
is given, and error.log only when neither is (warn.log has no AI_PROFILE, SERVER_WORK, startup, shutdown, ERR or FTL
lines: FTL reaches server.log only). detail.log, gm-action.log and the other logs are skipped. ai-profile.csv is not
read: it has per-system rows, not tick lengths.

What the server writes, and what this tool reads (source/server/GameServer/Managers/GameLoop):

* A run: the line `DOL.GS.GameServer | Current directory is:` opens a server process, `ForkBuildInfo | FORK_BUILD
  name=... commit=...` names its build, `GameServer | GameServer startup completed` is where play starts (the run's
  start), `GameServer | Stopping server...` and `GameServer | Stopped` are a clean stop. A run with no stop line ended
  by a crash, a kill or the window being closed (or is still running). The run checked is the last one in the logs,
  or the last one whose commit starts with --commit.
* Game-loop stalls, two once-a-minute sources:
  - `GameLoopWorkMetrics | SERVER_STALL worstTickMs=... stage=... gcPauseMs=...` (GameLoopWorkMetrics.ReportStallMinute,
    WRN): written for a minute whose slowest game-loop stage took 1000 ms or more, or whose GC pauses added up to 1000 ms
    or more (AiTurnStallMonitor.IsStallMinute). worstTickMs is the slowest single stage of the minute.
  - `AiProfile | AI_PROFILE tick_p50=... max=...` (AiProfile.EndTick, INF, while the ai_profile property is on): max is the
    longest whole tick of the minute (all stages together).
  A stall is a minute where either one shows a tick of 1000 ms or more; when both report the same minute (within 75 s)
  it counts once, with the longer of the two lengths. A SERVER_STALL minute whose only cause is GC pauses adding up to a
  second (no single tick of 1000 ms) is listed but not counted as a stall.
  Each source writes at most one line a minute (the minute's slowest tick), so two stalls in one minute count as one:
  the counts are a lower bound. The timestamp is the end of the minute the stall happened in.
  `SERVER_WORK` lines (one per stage each minute) show the measurement was running: a gap of 3 minutes or more with no
  SERVER_WORK line inside a run fails the gate (the loop may have been frozen, or lines are missing).
* "In any hour" is read as any 60 minutes (a sliding window), which is stricter than clock hours; the report also has a
  clock-hour table.
* Errors that stop play (each fails the gate):
  - any FTL line: `Critical error encountered in GameLoop` (the server stops), `Unhandled exception!`,
    `GameServer initialization failed!`, a thread-pool critical error;
  - `GameServiceUtils | Critical error encountered in <service>` whose action is `Calling KickPlayerToCharScreen`
    (a player was thrown out to the character screen); the same error that removed an NPC or bot is listed, not failed;
  - `Critical error encountered in GameLoopTickPacer` (ERR; the tick pacer stopped);
  - `Watchdog: Thread "..." has exited unexpectedly` (a game-loop worker died and was restarted) and
    `Watchdog: Thread "..." is taking too long` (a worker held for 7.5 s or more);
  - the server starting again with no stop line before the 8 hours were up (a crash, a kill or the window closed;
    a last run with no stop line may still be running, so it is only timed to its last line).
  Other ERR lines after startup are counted by logger, for information; the data-loading errors before
  `startup completed` are counted apart.

Exit code: 0 PASS, 1 FAIL, 2 nothing to check (no logs, no run, or no run with that commit).
"""
from __future__ import annotations

import argparse
import bisect
import datetime as dt
import json
import re
import sys
from collections import Counter
from pathlib import Path

GATE_HOURS = 8.0
HARD_STALL_MS = 2000.0
STALL_MS = 1000.0
MAX_STALLS_PER_HOUR = 5
PAIR_SECONDS = 75
MEASUREMENT_GAP_MINUTES = 3.0
GATE_TEXT = ("One overnight run without a stall or an error that stops play: 8 hours, no game-loop stall of 2.0 s or "
             "more, at most 5 stalls of 1.0 s or more in any hour (owner decision 9)")

ROTATED_RE = re.compile(r"^(?P<kind>[a-z-]+)\.(?P<date>\d{4}-\d{2}-\d{2})\.(?P<n>\d+)\.log$")
CURRENT_RE = re.compile(r"^(?P<kind>[a-z-]+)\.log$")
LEVELS = {"TRC", "DBG", "INF", "WRN", "ERR", "FTL"}
KV_RE = re.compile(r'(\w+)=("([^"]*)"|\S+)')


# ---- Files -------------------------------------------------------------------------------------------------------

class LogFile:
    def __init__(self, path: Path):
        self.path = path
        name = path.name.lower()
        rotated = ROTATED_RE.match(name)
        current = CURRENT_RE.match(name)
        if rotated:
            self.kind = rotated.group("kind")
            self.date = dt.date.fromisoformat(rotated.group("date"))
            self.number = int(rotated.group("n"))
        else:
            self.kind = current.group("kind") if current else name
            self.date = None
            self.number = 0

    @property
    def sort_key(self):
        # Rotated files by date and number, the current file (no date) after them.
        return (self.date is None, self.date or dt.date.max, self.number)


def collect_files(paths: list[str]) -> tuple[list[LogFile], list[str]]:
    """The server logs to read, in order, and notes on what was skipped."""
    found: list[LogFile] = []
    for raw in paths:
        p = Path(raw)
        if p.is_dir():
            found.extend(LogFile(f) for f in sorted(p.iterdir()) if f.is_file() and f.suffix.lower() == ".log")
        elif p.is_file():
            found.append(LogFile(p))
        else:
            raise FileNotFoundError(raw)
    notes = []
    by_kind = {kind: [f for f in found if f.kind == kind] for kind in ("server", "warn", "error")}
    if by_kind["server"]:
        chosen = by_kind["server"]
        if by_kind["warn"] or by_kind["error"]:
            notes.append("warn/error logs skipped: server.log already holds every warning and error")
    elif by_kind["warn"]:
        chosen = by_kind["warn"]
        notes.append("no server log given: read warn.log only, which has no AI_PROFILE, SERVER_WORK, startup, shutdown, "
                     "ERR or FTL lines, so short runs, frozen loops and errors that stop play are not seen")
    else:
        chosen = by_kind["error"]
        if chosen:
            notes.append("no server or warn log given: read error.log only, which has no stall lines")
    for f in found:
        if f.kind not in by_kind:
            notes.append(f"skipped {f.path.name} (not a server log)")
    chosen.sort(key=lambda f: f.sort_key)
    return chosen, notes


# ---- Lines -------------------------------------------------------------------------------------------------------

def parse_head(line: str):
    """(month, day, h, m, s, rest) for a line that starts with 'MM/DD HH:MM:SS | ', else None."""
    if len(line) < 17 or line[2] != "/" or line[5] != " " or line[8] != ":" or line[11] != ":" or line[14:17] != " | ":
        return None
    try:
        return int(line[0:2]), int(line[3:5]), int(line[6:8]), int(line[9:11]), int(line[12:14]), line[17:]
    except ValueError:
        return None


def split_rest(rest: str, default_level: str):
    """(level, logger, message) from 'LVL | logger | message' (server.log) or 'logger | message' (warn/error.log)."""
    if len(rest) >= 6 and rest[3:6] == " | " and rest[:3] in LEVELS:
        level, rest = rest[:3], rest[6:]
    else:
        level = default_level
    logger, sep, message = rest.partition(" | ")
    if not sep:
        logger, message = rest.rstrip(" |"), ""
    return level, logger.strip(), message


class Clock:
    """Turns 'MM/DD HH:MM:SS' into datetimes: the date is on every line, so midnight needs nothing; the year comes from
    the rotated file's name (or --year, or the newest rotated file, or today) and goes up by one when the month falls
    back (December to January)."""

    def __init__(self, year: int):
        self.year = year
        self.last_month = None

    def start_file(self, f: LogFile):
        if f.date is not None:
            self.year = f.date.year
            self.last_month = None

    def at(self, month, day, h, m, s):
        if self.last_month is not None and month < self.last_month:
            self.year += 1
        self.last_month = month
        try:
            return dt.datetime(self.year, month, day, h, m, s)
        except ValueError:
            return None


def kv(message: str) -> dict:
    return {k: (q if q is not None and v.startswith('"') else v) for k, v, q in KV_RE.findall(message)}


def number(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


# ---- Runs --------------------------------------------------------------------------------------------------------

class Run:
    def __init__(self, index: int):
        self.index = index
        self.process_start = None       # 'Current directory is:' seen (the run's first line is in the logs)
        self.first_ts = None
        self.last_ts = None
        self.name = None
        self.commit = None
        self.fork_build_ts = None
        self.startup_completed = None
        self.stopping = None
        self.stopped = None
        self.server_stalls = []         # SERVER_STALL lines
        self.profile_ticks = []         # AI_PROFILE minutes with max >= STALL_MS
        self.profile_minutes = 0
        self.work_times = []            # distinct SERVER_WORK times
        self.fatal = []
        self.service_errors = []        # Critical error encountered in <service>
        self.watchdog = []
        self.errors_after_start = Counter()
        self.errors_before_start = 0
        self.files = []
        self.next_run_follows = False   # a later process started in these logs

    @property
    def has_content(self):
        return self.first_ts is not None

    @property
    def start(self):
        return self.startup_completed or self.fork_build_ts or self.first_ts

    @property
    def end(self):
        return self.stopped or self.stopping or self.last_ts

    @property
    def clean_stop(self):
        return self.stopping is not None or self.stopped is not None


def where(f: LogFile, line_no: int):
    return f"{f.path.name}:{line_no}"


def read_runs(files: list[LogFile], year: int | None = None) -> list[Run]:
    if year is None:
        dated = [f.date.year for f in files if f.date is not None]
        year = max(dated) if dated else dt.date.today().year
    clock = Clock(year)
    runs: list[Run] = []
    run = Run(0)
    pending = None          # an ERR/FTL record still collecting its continuation lines

    def new_run(final=False):
        nonlocal run
        if run.has_content:
            run.next_run_follows = not final
            runs.append(run)
        run = Run(len(runs))

    for f in files:
        clock.start_file(f)
        default_level = {"warn": "WRN", "error": "ERR"}.get(f.kind, "INF")
        with open(f.path, encoding="utf-8", errors="replace") as handle:
            for line_no, raw in enumerate(handle, 1):
                line = raw.rstrip("\r\n")
                if line_no == 1:
                    line = line.lstrip("﻿")
                head = parse_head(line)
                if head is None:
                    if pending is not None:
                        text = line.strip()
                        if text.startswith("Calling ") or text.startswith("No other action performed"):
                            pending["action"] = text
                        elif len(pending["detail"]) < 3 and text:
                            pending["detail"].append(text)
                    continue
                pending = None
                ts = clock.at(*head[:5])
                if ts is None:
                    continue
                level, logger, message = split_rest(head[5], default_level)

                if logger == "DOL.GS.GameServer" and message.startswith("Current directory is:"):
                    new_run()
                    run.process_start = ts
                elif message.startswith("FORK_BUILD "):
                    # A second build line, or (without the DBG start line, as in warn.log) the first one after the
                    # tail of an earlier process, starts a new run; FORK_BUILD is then the nearest thing to its start.
                    if run.commit is not None or (run.process_start is None and run.has_content):
                        new_run()
                    if run.process_start is None:
                        run.process_start = ts
                    fields = kv(message)
                    run.name, run.commit, run.fork_build_ts = fields.get("name"), fields.get("commit", ""), ts
                if run.first_ts is None:
                    run.first_ts = ts
                run.last_ts = ts
                if not run.files or run.files[-1] != f.path.name:
                    run.files.append(f.path.name)

                if logger == "DOL.GS.GameServer":
                    if message.startswith("GameServer startup completed"):
                        run.startup_completed = ts
                    elif message.startswith("Stopping server"):
                        run.stopping = run.stopping or ts
                    elif message == "Stopped":
                        run.stopped = ts
                if message.startswith("SERVER_WORK "):
                    if not run.work_times or run.work_times[-1] != ts:
                        run.work_times.append(ts)
                elif message.startswith("SERVER_STALL "):
                    fields = kv(message)
                    run.server_stalls.append({
                        "time": ts, "tick_ms": number(fields.get("worstTickMs")), "gc_ms": number(fields.get("gcPauseMs")),
                        "stage": fields.get("stage"), "likely": fields.get("likely"), "slowest": fields.get("slowest"),
                        "at": where(f, line_no)})
                elif message.startswith("AI_PROFILE tick_p50="):
                    run.profile_minutes += 1
                    fields = kv(message)
                    tick = number(fields.get("max"))
                    if tick >= STALL_MS:
                        run.profile_ticks.append({"time": ts, "tick_ms": tick, "at": where(f, line_no)})
                elif message.startswith("Watchdog: Thread"):
                    run.watchdog.append({"time": ts, "message": message, "at": where(f, line_no)})

                if level in ("ERR", "FTL"):
                    record = {"time": ts, "level": level, "logger": logger, "message": message[:300], "detail": [],
                              "action": None, "at": where(f, line_no)}
                    if level == "FTL":
                        run.fatal.append(record)
                        pending = record
                    elif message.startswith("Critical error encountered in "):
                        record["service"] = message[len("Critical error encountered in "):].split(":", 1)[0]
                        run.service_errors.append(record)
                        pending = record
                    elif run.startup_completed is None:
                        run.errors_before_start += 1
                    else:
                        run.errors_after_start[logger] += 1
    new_run(final=True)
    return runs


# ---- The check ---------------------------------------------------------------------------------------------------

def merge_stalls(run: Run):
    """Stall events (tick of STALL_MS or more) from both sources, one per minute, and the GC-only SERVER_STALL minutes."""
    events = []
    for s in run.server_stalls:
        events.append({"time": s["time"], "tick_ms": s["tick_ms"], "gc_ms": s["gc_ms"], "stage": s["stage"],
                       "likely": s["likely"], "slowest": s["slowest"], "sources": ["SERVER_STALL"], "at": [s["at"]]})
    used = set()
    for p in run.profile_ticks:
        best = None
        for i, e in enumerate(events):
            if i in used or "AI_PROFILE" in e["sources"]:
                continue
            gap = abs((e["time"] - p["time"]).total_seconds())
            if gap <= PAIR_SECONDS and (best is None or gap < best[0]):
                best = (gap, i)
        if best is None:
            events.append({"time": p["time"], "tick_ms": p["tick_ms"], "gc_ms": None, "stage": None, "likely": None,
                           "slowest": None, "sources": ["AI_PROFILE"], "at": [p["at"]]})
        else:
            used.add(best[1])
            e = events[best[1]]
            e["tick_ms"] = max(e["tick_ms"], p["tick_ms"])
            e["sources"].append("AI_PROFILE")
            e["at"].append(p["at"])
    stalls = sorted((e for e in events if e["tick_ms"] >= STALL_MS), key=lambda e: e["time"])
    gc_only = sorted((e for e in events if e["tick_ms"] < STALL_MS), key=lambda e: e["time"])
    return stalls, gc_only


def worst_hour(times: list[dt.datetime]):
    """(count, window start) of the 60-minute window holding the most of these times."""
    best = (0, None)
    for i, t in enumerate(times):
        count = bisect.bisect_left(times, t + dt.timedelta(hours=1)) - i
        if count > best[0]:
            best = (count, t)
    return best


def clock_hours(run: Run, stalls):
    if run.start is None or run.end is None:
        return []
    hour = run.start.replace(minute=0, second=0)
    rows = []
    while hour <= run.end:
        nxt = hour + dt.timedelta(hours=1)
        inside = [s for s in stalls if hour <= s["time"] < nxt]
        rows.append({"hour": hour, "stalls_1s": len(inside),
                     "stalls_2s": sum(1 for s in inside if s["tick_ms"] >= HARD_STALL_MS),
                     "worst_ms": max((s["tick_ms"] for s in inside), default=0.0)})
        hour = nxt
    return rows


def measurement_gaps(run: Run):
    """Stretches inside the run with no SERVER_WORK line for MEASUREMENT_GAP_MINUTES or more."""
    if not run.work_times or run.start is None:
        return []
    # Startup takes a minute before the first report; count from the first report.
    points = [t for t in run.work_times if t >= run.start] or run.work_times
    end = run.stopping or run.end
    gaps = []
    stops = points[1:] + [end]
    for i, (a, b) in enumerate(zip(points, stops)):
        minutes = (b - a).total_seconds() / 60
        if minutes >= MEASUREMENT_GAP_MINUTES:
            gaps.append({"from": a, "to": b, "minutes": round(minutes, 1), "to_end": i == len(stops) - 1})
    return gaps


def fmt(t: dt.datetime | None):
    return t.strftime("%Y-%m-%d %H:%M:%S") if t else "?"


def hours_text(seconds: float):
    minutes = int(round(seconds / 60))
    return f"{minutes // 60} h {minutes % 60:02d} min"


def check(run: Run) -> dict:
    stalls, gc_only = merge_stalls(run)
    hard = [s for s in stalls if s["tick_ms"] >= HARD_STALL_MS]
    times = [s["time"] for s in stalls]
    peak_count, peak_from = worst_hour(times)
    duration_s = (run.end - run.start).total_seconds() if run.start and run.end else 0.0
    gaps = measurement_gaps(run)
    reasons, notes = [], []

    stopping_errors = []
    for e in run.fatal:
        stopping_errors.append({"kind": "fatal", "time": e["time"], "text": f"FTL {e['logger']}: {e['message']}",
                                "detail": e["detail"], "at": e["at"]})
    kicks = [e for e in run.service_errors if e["action"] and "KickPlayerToCharScreen" in e["action"]]
    pacer = [e for e in run.service_errors if e.get("service") == "GameLoopTickPacer"]
    for e in kicks:
        stopping_errors.append({"kind": "player_kicked", "time": e["time"],
                                "text": f"{e['message'][:160]} -> {e['action'][:120]}", "detail": e["detail"], "at": e["at"]})
    for e in pacer:
        stopping_errors.append({"kind": "tick_pacer", "time": e["time"], "text": e["message"][:200], "detail": e["detail"],
                                "at": e["at"]})
    for w in run.watchdog:
        stopping_errors.append({"kind": "watchdog", "time": w["time"], "text": w["message"][:200], "detail": [], "at": w["at"]})
    other_service = [e for e in run.service_errors if e not in kicks and e not in pacer]
    if not run.clean_stop and run.next_run_follows and duration_s < GATE_HOURS * 3600:
        stopping_errors.append({"kind": "no_clean_stop", "time": run.end,
                                "text": "the server started again with no 'Stopping server' line before the 8 hours "
                                        "were up (crash, kill or window closed)", "detail": [], "at": None})
    stopping_errors.sort(key=lambda e: e["time"] or dt.datetime.min)

    if run.process_start is None:
        reasons.append("the run's start is not in these logs (give the earlier rotated server logs too)")
    if run.startup_completed is None:
        notes.append("no 'GameServer startup completed' line: the run is timed from its first line")
    if duration_s < GATE_HOURS * 3600:
        reasons.append(f"the run lasted {hours_text(duration_s)}, less than {GATE_HOURS:g} hours")
    if hard:
        longest = max(hard, key=lambda s: s["tick_ms"])
        reasons.append(f"{len(hard)} game-loop stall(s) of {HARD_STALL_MS / 1000:.1f} s or more "
                       f"(longest {longest['tick_ms'] / 1000:.2f} s, minute to {fmt(longest['time'])})")
    if peak_count > MAX_STALLS_PER_HOUR:
        reasons.append(f"{peak_count} stalls of {STALL_MS / 1000:.1f} s or more within the hour from {fmt(peak_from)} "
                       f"(at most {MAX_STALLS_PER_HOUR})")
    kinds = Counter(e["kind"] for e in stopping_errors)
    labels = {"fatal": "fatal (FTL) error(s)", "player_kicked": "service error(s) that threw the player out",
              "tick_pacer": "tick-pacer critical error(s)", "watchdog": "game-loop watchdog line(s)",
              "no_clean_stop": "end without a clean stop"}
    for kind, count in kinds.items():
        first = next(e for e in stopping_errors if e["kind"] == kind)
        reasons.append(f"error that stops play: {count} {labels[kind]} (first {fmt(first['time'])})")
    for g in gaps:
        what = "until the run's end" if g["to_end"] else f"to {fmt(g['to'])}"
        reasons.append(f"no game-loop measurement (SERVER_WORK) for {g['minutes']:g} min from {fmt(g['from'])} {what}: "
                       "the loop may have been frozen")
    if not run.work_times:
        reasons.append("no SERVER_WORK lines in the run: the game loop's measurements are missing")

    if run.clean_stop:
        notes.append(f"clean stop at {fmt(run.stopping or run.stopped)}")
    elif not run.next_run_follows:
        notes.append("no 'Stopping server' line and no later start: the server was still running when the logs were "
                     "copied, or ended without a clean stop; the run is timed to its last line")
    elif duration_s >= GATE_HOURS * 3600:
        notes.append("no 'Stopping server' line before the next start: the run ended after the 8 hours without a clean "
                     "stop (window closed, killed or crashed)")
    if run.profile_minutes == 0:
        notes.append("no AI_PROFILE lines (ai_profile off?): stalls come from SERVER_STALL only")
    if gc_only:
        notes.append(f"{len(gc_only)} SERVER_STALL minute(s) with GC pauses adding up to a second but no single tick of "
                     f"{STALL_MS / 1000:.1f} s: listed, not counted")
    if other_service:
        notes.append(f"{len(other_service)} service error(s) that removed an NPC or bot (play went on)")
    after = sum(run.errors_after_start.values())
    if after:
        top = ", ".join(f"{k} {v}" for k, v in run.errors_after_start.most_common(5))
        notes.append(f"{after} other ERR line(s) after startup (play went on): {top}")
    if run.errors_before_start:
        notes.append(f"{run.errors_before_start} ERR line(s) while the server was loading (before startup completed)")
    notes.append("each source writes at most one line a minute, so two stalls in one minute count as one: "
                 "stall counts are a lower bound")

    return {
        "verdict": "FAIL" if reasons else "PASS",
        "reasons": reasons,
        "notes": notes,
        "gate": GATE_TEXT,
        "run": {
            "index": run.index, "build": run.name, "commit": run.commit, "files": run.files,
            "process_start": run.process_start, "start": run.start, "end": run.end,
            "duration_hours": round(duration_s / 3600, 2), "clean_stop": run.clean_stop,
            "ai_profile_minutes": run.profile_minutes, "server_work_minutes": len(run.work_times),
        },
        "stalls": stalls,
        "stalls_2s": hard,
        "gc_only_minutes": gc_only,
        "worst_hour": {"count": peak_count, "from": peak_from},
        "clock_hours": clock_hours(run, stalls),
        "measurement_gaps": gaps,
        "errors_that_stop_play": stopping_errors,
        "other_service_errors": [{"time": e["time"], "service": e.get("service"), "action": e["action"], "at": e["at"]}
                                 for e in other_service],
        "other_errors_by_logger": dict(run.errors_after_start.most_common()),
        "errors_while_loading": run.errors_before_start,
    }


def run_summary(run: Run) -> dict:
    duration = (run.end - run.start).total_seconds() / 3600 if run.start and run.end else 0.0
    return {"index": run.index, "build": run.name, "commit": run.commit, "start": run.start, "end": run.end,
            "duration_hours": round(duration, 2), "clean_stop": run.clean_stop,
            "start_in_logs": run.process_start is not None}


def pick_run(runs: list[Run], commit: str | None):
    with_build = [r for r in runs if r.commit is not None]
    if commit:
        matches = [r for r in with_build if r.commit.lower().startswith(commit.lower())]
        return (matches[-1] if matches else None), len(matches)
    return (with_build[-1] if with_build else (runs[-1] if runs else None)), 1


# ---- Output ------------------------------------------------------------------------------------------------------

def to_json(value):
    if isinstance(value, dt.datetime):
        return value.isoformat(sep=" ")
    if isinstance(value, dict):
        return {k: to_json(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [to_json(v) for v in value]
    return value


def text_report(result: dict, runs: list[dict], file_notes: list[str], matches: int) -> str:
    r = result["run"]
    out = [f"Overnight gate: {result['verdict']}", f"  {result['gate']}", ""]
    out.append(f"Build:    {r['build'] or '?'} commit={r['commit'] or '?'}")
    out.append(f"Run:      {fmt(r['start'])} -> {fmt(r['end'])}  ({r['duration_hours']:.2f} h, "
               f"{'clean stop' if r['clean_stop'] else 'no stop line'})")
    out.append(f"Logs:     {', '.join(r['files'])}")
    out.append(f"Measured: {r['server_work_minutes']} SERVER_WORK minutes, {r['ai_profile_minutes']} AI_PROFILE minutes")
    if matches > 1:
        out.append(f"          ({matches} runs match that commit; the last one is checked)")
    out.append("")
    if result["reasons"]:
        out.append("Why it fails:")
        out.extend(f"  - {x}" for x in result["reasons"])
        out.append("")
    stalls = result["stalls"]
    out.append(f"Game-loop stalls of {STALL_MS / 1000:.1f} s or more: {len(stalls)} "
               f"({len(result['stalls_2s'])} of {HARD_STALL_MS / 1000:.1f} s or more); "
               f"most in any hour: {result['worst_hour']['count']}"
               + (f" (from {fmt(result['worst_hour']['from'])})" if result['worst_hour']['from'] else ""))
    for s in stalls:
        flag = "  >=2s" if s["tick_ms"] >= HARD_STALL_MS else "      "
        extra = []
        if s["stage"]:
            extra.append(f"stage={s['stage']}")
        if s["gc_ms"] is not None:
            extra.append(f"gc={s['gc_ms']:.0f}ms")
        if s["likely"]:
            extra.append(f"likely=\"{s['likely']}\"")
        out.append(f" {flag} {fmt(s['time'])}  {s['tick_ms'] / 1000:6.2f} s  [{'+'.join(s['sources'])}]  {' '.join(extra)}")
    for s in result["gc_only_minutes"]:
        out.append(f"   gc   {fmt(s['time'])}  tick {s['tick_ms'] / 1000:.2f} s, GC pauses {s['gc_ms'] / 1000:.2f} s "
                   "in the minute (not counted)")
    out.append("")
    if result["clock_hours"]:
        out.append("Per clock hour (stalls >=1.0 s / >=2.0 s, worst):")
        for h in result["clock_hours"]:
            worst = f"{h['worst_ms'] / 1000:.2f} s" if h["worst_ms"] else "-"
            over = "  over the limit" if h["stalls_1s"] > MAX_STALLS_PER_HOUR else ""
            out.append(f"  {h['hour'].strftime('%m/%d %H:00')}  {h['stalls_1s']:3d} / {h['stalls_2s']:d}   {worst}{over}")
        out.append("")
    errs = result["errors_that_stop_play"]
    out.append(f"Errors that stop play: {len(errs)}")
    for e in errs:
        out.append(f"  {fmt(e['time'])}  [{e['kind']}] {e['text']}" + (f"  ({e['at']})" if e["at"] else ""))
        for d in e["detail"][:2]:
            out.append(f"      {d[:200]}")
    out.append("")
    out.append("Notes:")
    out.extend(f"  - {x}" for x in result["notes"] + file_notes)
    out.append("")
    out.append("Runs in these logs:")
    out.extend("  " + run_line(x, x["index"] == r["index"]) for x in runs)
    return "\n".join(out)


def run_line(x: dict, chosen: bool = False) -> str:
    mark = "*" if chosen else " "
    start = fmt(x["start"]) + ("" if x["start_in_logs"] else " (start not in logs)")
    return (f"{mark} {x['build'] or '?'} {x['commit'] or '?':12}  {start} -> {fmt(x['end'])}  "
            f"{x['duration_hours']:5.2f} h  {'clean stop' if x['clean_stop'] else 'no stop line'}")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Checks a server run against the 1.2 overnight stability gate.")
    parser.add_argument("logs", nargs="+", help="server log files or the logs folder")
    parser.add_argument("--commit", help="check the last run whose FORK_BUILD commit starts with this")
    parser.add_argument("--json", action="store_true", help="print JSON instead of text")
    parser.add_argument("--list-runs", action="store_true", help="only list the runs in the logs")
    parser.add_argument("--year", type=int, help="year of the first log line when no rotated file name gives it")
    args = parser.parse_args(argv)

    try:
        files, file_notes = collect_files(args.logs)
    except FileNotFoundError as missing:
        print(f"not found: {missing}", file=sys.stderr)
        return 2
    if not files:
        print("no server, warn or error logs given", file=sys.stderr)
        return 2
    runs = read_runs(files, args.year)
    summaries = [run_summary(r) for r in runs]
    if args.list_runs:
        if args.json:
            print(json.dumps(to_json(summaries), indent=2))
        else:
            print("\n".join(run_line(x) for x in summaries) or "no runs")
        return 0 if runs else 2
    run, matches = pick_run(runs, args.commit)
    if run is None:
        print(f"no run{' with commit ' + args.commit if args.commit else ''} in these logs", file=sys.stderr)
        return 2
    result = check(run)
    if args.json:
        payload = dict(result, runs=summaries, file_notes=file_notes, commit_matches=matches)
        print(json.dumps(to_json(payload), indent=2))
    else:
        print(text_report(result, summaries, file_notes, matches))
    return 0 if result["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
