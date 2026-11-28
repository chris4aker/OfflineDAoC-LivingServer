#!/usr/bin/env python3
"""Reads server logs against the rc.3 PC checklists (#503 and #476, owner decision 63A) and prints, per issue and
checklist line, PASS / FAIL / NOT SEEN (IN GAME for a line no log row can settle), the row counts behind each result
and the first matching line as evidence, with a summary at the top.

    python3 source/tools/rc_checklist.py runtime/server/logs/server.log
    python3 source/tools/rc_checklist.py server.log detail.log --build 1a2b3c4
    python3 source/tools/rc_checklist.py server.log --json
    python3 source/tools/rc_checklist.py server.log --brief      # the summary and one line per check

The checklists are data: source/tools/rc3-checklist.json (its "format" field says how a line is judged). The tool
never fetches them from GitHub.

What a result means:
* PASS: every row the line names was seen, and nothing the line forbids. It never says a feature works on its own:
  the line's "also check" and "in game" parts are printed beside it for the reader, and only a played log counts.
* FAIL: a row the line says must not show did show (the first such line is quoted), a rate went over its bar, or a
  route recovery aimed at a point the line says it must avoid.
* NOT SEEN: some named rows are missing (the count for each is shown), or the run is shorter than the line needs.
* IN GAME: the line has no log row; only the screen settles it.

Logs are split by server process and FORK_BUILD as played_check_log.py splits them, reusing its line parsing and its
masking of player names. Each build (name and commit) is judged on its own and never mixed with another; by default
the builds named as the checklist's build (1.2.3), or the ones --build picks by name or commit prefix. Give several
files in time order (server.log, then detail.log for Debug rows such as AUTONOMOUS_AREA_HUNT): each file is split on
its own. Logs stay out of git (AGENTS.md).

Exit code: 0 a report was made, 2 nothing to judge (no FORK_BUILD for the build asked).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import played_check_log as pcl  # noqa: E402  (line parsing, the clock and name masking)

CHECKLIST = Path(__file__).resolve().parent / "rc3-checklist.json"
PASS, FAIL, NOT_SEEN, IN_GAME = "PASS", "FAIL", "NOT SEEN", "IN GAME"
ORDER = (FAIL, NOT_SEEN, IN_GAME, PASS)
CHAT_NAME_RE = re.compile(r'\b(from|to|speaker)=("[^"]*"|\S+)')


# ---- Matching ---------------------------------------------------------------------------------------------------

def condition(text: str):
    """A field condition from the data file: a function of the field's text (None when the field is missing)."""
    text = str(text)
    if text.startswith("re:"):
        pattern = re.compile(text[3:])
        return lambda v: v is not None and pattern.search(v) is not None
    m = re.fullmatch(r"(-?[\d.]+)\.\.(-?[\d.]+)", text)
    if m:
        low, high = float(m.group(1)), float(m.group(2))
        return lambda v: (n := pcl.num(v, None)) is not None and low <= n <= high
    m = re.fullmatch(r"(>=|<=|>|<)(-?[\d.]+)", text)
    if m:
        op, bar = m.group(1), float(m.group(2))
        test = {">": lambda n: n > bar, ">=": lambda n: n >= bar, "<": lambda n: n < bar, "<=": lambda n: n <= bar}[op]
        return lambda v: (n := pcl.num(v, None)) is not None and test(n)
    return lambda v: v == text


class Matcher:
    """One row the checklist looks for: a tag, field conditions and text the line must contain."""

    def __init__(self, spec: dict):
        self.tag = spec["tag"]
        self.where = {k: condition(v) for k, v in (spec.get("where") or {}).items()}
        contains = spec.get("contains") or []
        self.contains = [contains] if isinstance(contains, str) else list(contains)
        self.label = self.tag + "".join(f" {k}={v}" for k, v in (spec.get("where") or {}).items()) + "".join(
            f' "{c}"' for c in self.contains)
        self.count, self.first_t, self.first_line = 0, None, None

    def test(self, message: str, fields: dict) -> bool:
        return (all(c in message for c in self.contains)
                and all(cond(fields.get(k)) for k, cond in self.where.items()))

    def seen(self, t, message):
        self.count += 1
        if self.first_t is None:
            self.first_t, self.first_line = t, quote(message)

    def as_dict(self) -> dict:
        return {"row": self.label, "count": self.count, "first": pcl.stamp(self.first_t) if self.first_t else None,
                "line": self.first_line}


def quote(message: str) -> str:
    text = pcl.quote(message)
    if text.startswith("CHAT_"):
        text = CHAT_NAME_RE.sub(lambda m: f"{m.group(1)}=<name>", text)
    return text


def point(value):
    try:
        parts = [float(p) for p in str(value).split(",")[:3]]
    except ValueError:
        return None
    return parts if len(parts) >= 2 else None


class Near:
    """FAIL when a b row's point lands within `within` (x, y) of an earlier a row's point with the same join field."""

    def __init__(self, spec: dict):
        self.a, self.b = Matcher(spec["a"]), Matcher(spec["b"])
        self.a_point, self.b_point, self.join, self.within = spec["a_point"], spec["b_point"], spec.get("join"), float(
            spec["within"])
        self.points: dict = {}
        self.label = f"{self.b.label} {self.b_point}= within {spec['within']} of {self.a.label} {self.a_point}="
        self.hits = Matcher({"tag": self.b.tag})
        self.hits.label = self.label

    def feed(self, t, tag, message, fields):
        if tag == self.a.tag and self.a.test(message, fields):
            p = point(fields.get(self.a_point))
            if p is not None:
                self.points.setdefault(fields.get(self.join) if self.join else None, []).append(p)
        if tag == self.b.tag and self.b.test(message, fields):
            p = point(fields.get(self.b_point))
            if p is None:
                return
            for q in self.points.get(fields.get(self.join) if self.join else None, ()):
                if math.hypot(p[0] - q[0], p[1] - q[1]) <= self.within:
                    self.hits.seen(t, message)
                    return


class Check:
    def __init__(self, issue: dict, spec: dict):
        self.issue, self.spec = issue, spec
        self.rows = [[Matcher(m) for m in r["any"]] if "any" in r else [Matcher(r)] for r in spec.get("rows", [])]
        self.counts = [Matcher(m) for m in spec.get("count", [])]
        self.never = [Matcher(m) for m in spec.get("never", [])]
        self.rates = [(Matcher(m), float(m["max_per_hour"])) for m in spec.get("rate", [])]
        self.near = [Near(n) for n in spec.get("near", [])]

    def matchers(self):
        for group in self.rows:
            yield from group
        yield from self.counts
        yield from self.never
        for m, _ in self.rates:
            yield m

    def judge(self, hours: float) -> dict:
        status, why = PASS, []
        bad = [m for m in self.never if m.count] + [n.hits for n in self.near if n.hits.count]
        over = [(m, bar) for m, bar in self.rates if hours > 0 and m.count / hours >= bar]
        if bad or over:
            status = FAIL
            why += [f"must not show: {m.label} ({m.count})" for m in bad]
            why += [f"{m.label}: {m.count / hours:.1f}/h, bar {bar:g}/h" for m, bar in over]
        missing = [g for g in self.rows if not any(m.count >= 1 for m in g)]
        short = self.spec.get("min_hours") and hours < float(self.spec["min_hours"])
        if status != FAIL:
            if not self.rows and not self.near and not self.never and not self.rates:
                status = IN_GAME
            elif missing or short:
                status = NOT_SEEN
                if missing:
                    why.append(f"{len(self.rows) - len(missing)}/{len(self.rows)} rows seen; missing: "
                               + "; ".join(" or ".join(m.label for m in g) for g in missing))
                if short:
                    why.append(f"the run is {hours:.1f} h; the line needs {self.spec['min_hours']} h or more")
        return {
            "issue": self.issue["issue"], "list": self.issue["list"], "id": self.spec["id"], "look": self.spec["look"],
            "status": status, "why": why,
            "rows": [m.as_dict() for g in self.rows for m in g],
            "count": [m.as_dict() for m in self.counts],
            "never": [m.as_dict() for m in self.never] + [n.hits.as_dict() for n in self.near],
            "rate": [dict(m.as_dict(), per_hour=round(m.count / hours, 2) if hours else None, bar=bar)
                     for m, bar in self.rates],
            "also": self.spec.get("also"), "in_game": self.spec.get("in_game", []),
        }


def build_checks(data: dict) -> list[Check]:
    return [Check(issue, spec) for issue in data["issues"] for spec in issue["checks"]]


# ---- Reading the logs -------------------------------------------------------------------------------------------

class Section:
    def __init__(self, path, line_no):
        self.path, self.line_no = path, line_no
        self.name = self.commit = None
        self.started = False
        self.first_t = self.last_t = None
        self.lines: list = []   # (t, tag, message), only lines whose tag the checklist names


def read_sections(paths, tags: set, year: int) -> list[Section]:
    sections = []
    for path in paths:
        clock = pcl.Clock(year)
        sec = Section(path, 1)
        sections.append(sec)
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line_no, raw in enumerate(handle, 1):
                head = pcl.parse_head(raw.lstrip("﻿") if line_no == 1 else raw)
                if head is None:
                    continue
                t = clock.at(*head[:5])
                if t is None:
                    continue
                _level, logger, message = pcl.split_rest(head[5])
                message = message.rstrip("\r\n")
                starts = logger == "DOL.GS.GameServer" and message.startswith("Current directory is:")
                fork = message.startswith("FORK_BUILD ")
                if (starts and sec.first_t is not None) or (fork and (sec.commit is not None or (
                        sec.first_t is not None and not sec.started))):
                    sec = Section(path, line_no)
                    sections.append(sec)
                if starts:
                    sec.started = True
                if fork:
                    fields = pcl.kv(message)
                    sec.name, sec.commit = fields.get("name"), fields.get("commit", "")
                sec.first_t = sec.first_t or t
                sec.last_t = t
                tag = message.split(" ", 1)[0].rstrip(":")
                if tag in tags:
                    sec.lines.append((t, tag, message))
    return [s for s in sections if s.first_t is not None]


def picks(sec: Section, build: str | None, default_name: str) -> bool:
    if sec.commit is None:
        return False
    if build:
        return sec.name == build or (len(build) >= 4 and (sec.commit or "").startswith(build))
    return sec.name == default_name


def judge(data: dict, sections: list[Section]) -> dict:
    checks = build_checks(data)
    by_tag: dict = {}
    for c in checks:
        for m in c.matchers():
            by_tag.setdefault(m.tag, []).append(m)
    nears = [n for c in checks for n in c.near]
    seconds = 0.0
    for sec in sections:
        seconds += (sec.last_t - sec.first_t).total_seconds()
        for t, tag, message in sec.lines:
            matchers = by_tag.get(tag)
            fields = None
            if matchers:
                fields = pcl.kv(message)
                for m in matchers:
                    if m.test(message, fields):
                        m.seen(t, message)
            for n in nears:
                if tag in (n.a.tag, n.b.tag):
                    n.feed(t, tag, message, fields if fields is not None else pcl.kv(message))
    hours = seconds / 3600
    results = [c.judge(hours) for c in checks]
    issues = []
    for issue in data["issues"]:
        mine = [r for r in results if r["issue"] == issue["issue"]]
        # Any FAIL fails the issue, any NOT SEEN leaves it unseen; a PASS beside IN GAME lines is a PASS whose screen
        # checks are printed with it; only an issue with nothing but IN GAME lines is IN GAME.
        status = next(s for s in (FAIL, NOT_SEEN, PASS) if any(r["status"] == s for r in mine)) if any(
            r["status"] != IN_GAME for r in mine) else IN_GAME
        issues.append({"issue": issue["issue"], "list": issue["list"], "steps": issue.get("steps"),
                       "bots": issue.get("bots"), "open": issue.get("open"), "status": status, "checks": mine})
    return {"hours": round(hours, 2), "issues": issues}


# ---- Report -----------------------------------------------------------------------------------------------------

def summary_counts(issues):
    return {s: [i["issue"] for i in issues if i["status"] == s] for s in ORDER}


def print_report(data, blocks, unjudged, out=None, brief=False):
    w = (out or sys.stdout).write
    w(f"{data['title']}\n")
    for build, sections, result in blocks:
        w(f"\n=== Build {build[0]} commit {build[1]}: {len(sections)} server run(s), {result['hours']:.1f} h of log ===\n")
        counts = summary_counts(result["issues"])
        w("Summary (issues): " + ", ".join(f"{s} {len(counts[s])}" for s in ORDER) + "\n")
        for s in ORDER:
            if counts[s]:
                w(f"  {s}: " + " ".join(f"#{n}" for n in counts[s]) + "\n")
        for issue in result["issues"]:
            w(f"\n#{issue['issue']} ({issue['steps']}; list #{issue['list']}) - {issue['status']}\n")
            for c in issue["checks"]:
                w(f"  [{c['status']}] {c['id']}: {c['look']}\n")
                for why in c["why"]:
                    w(f"      ! {why}\n")
                if brief:
                    continue
                for kind in ("rows", "never", "rate", "count"):
                    for r in c[kind]:
                        label = {"rows": "row", "never": "never", "rate": "rate", "count": "count"}[kind]
                        extra = f" ({r['per_hour']}/h, bar {r['bar']:g}/h)" if kind == "rate" and r.get("per_hour") is not None else ""
                        w(f"      {label}: {r['row']}: {r['count']}{extra}\n")
                        if r["line"]:
                            w(f"        first {r['first']}: {r['line']}\n")
                if c["also"]:
                    w(f"      also check: {c['also']}\n")
                for item in c["in_game"]:
                    w(f"      in game: {item}\n")
            if issue.get("open") and not brief:
                w(f"  (stays open: {issue['open']})\n")
    if unjudged:
        w("\nNot judged (another build, or no FORK_BUILD line):\n")
        for sec in unjudged:
            w(f"  {sec.path}:{sec.line_no} {pcl.stamp(sec.first_t)}-{pcl.stamp(sec.last_t)} "
              f"build={sec.name or '-'} commit={sec.commit or '-'}\n")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("logs", nargs="+", type=Path, help="server.log (and detail.log), in time order")
    ap.add_argument("--build", help="a FORK_BUILD name or commit prefix (default: the checklist's build)")
    ap.add_argument("--checklist", type=Path, default=CHECKLIST)
    ap.add_argument("--year", type=int, default=dt.date.today().year, help="the year of the log's first line")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--brief", action="store_true", help="the summary and one line per check (no rows or evidence)")
    args = ap.parse_args(argv)
    data = json.loads(args.checklist.read_text(encoding="utf-8"))
    tags = {m.tag for c in build_checks(data) for m in c.matchers()}
    tags |= {n.a.tag for c in build_checks(data) for n in c.near}
    sections = read_sections(args.logs, tags, args.year)
    chosen = [s for s in sections if picks(s, args.build, data["build"])]
    unjudged = [s for s in sections if s not in chosen]
    builds: dict = {}
    for sec in chosen:
        builds.setdefault((sec.name, sec.commit), []).append(sec)
    blocks = [(build, secs, judge(data, secs)) for build, secs in builds.items()]
    if args.json:
        json.dump({"title": data["title"], "builds": [
            {"name": b[0], "commit": b[1], "runs": len(s), **r, "summary": summary_counts(r["issues"])}
            for b, s, r in blocks],
            "not_judged": [{"file": str(s.path), "line": s.line_no, "name": s.name, "commit": s.commit}
                           for s in unjudged]}, sys.stdout, indent=1)
        sys.stdout.write("\n")
    else:
        if not blocks:
            sys.stdout.write(f"No FORK_BUILD for {args.build or data['build']} in the log(s): nothing to judge.\n")
        print_report(data, blocks, unjudged, brief=args.brief)
    return 0 if blocks else 2


if __name__ == "__main__":
    sys.exit(main())
