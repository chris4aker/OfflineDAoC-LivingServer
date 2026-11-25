#!/usr/bin/env python3
"""Per-realm and per-zone XP per kill from the sampled AUTONOMOUS_KILL_XP rows (GitHub #544).

    python source/tools/kill_xp_report.py server.log
    python source/tools/kill_xp_report.py server.log --since "11/24 10:37" --hours 1
    python source/tools/kill_xp_report.py server.log detail.log --min-rows 5 --json

The server writes one row for every Nth autonomous-gamebot PvE kill (server property bot_kill_xp_sample_every,
default 25, at most 120 rows a minute; the row's every= and dropped= say how many kills it stands for). For each realm,
and each realm's zone, the report gives:

* rows, and the kills they stand for (sum of every= over the rows, plus every= x dropped=);
* XP per member per kill (median and mean of the killer party's xpPerMember; a member paid 0 counts);
* the median level gap (target level minus the party's highest level) and the share of each con colour;
* the split: the median killer share of the damage, the share of kills split with another party or player
  (otherParties>0), and the median share lost to damage that paid nobody (lost=);
* the off-target share: of the rows whose camp is known (onTarget=0 or 1), the share that were not the camp's target,
  and how many of those were the same monster away from the camp (sameName=1).

AUTONOMOUS_GOAL_ATTEMPT lines (one per camp attempt, routed to detail.log by logconfig.xml since 2026-11-13, not to
server.log) are read too when a detail.log is given: per realm, the share of attempt XP not from the camp's target
(1 - targetXp / xp), a second, unsampled, view of the off-target question.

Names are not printed: only realms, zones, monster cons and numbers. Reads each file once, line by line.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path

KILL_MARKER = "AUTONOMOUS_KILL_XP "
GOAL_MARKER = "AUTONOMOUS_GOAL_ATTEMPT "
FIELD = re.compile(r'(\w+)=("([^"]*)"|\S+)')
STAMP = re.compile(r"^(\d\d)/(\d\d) (\d\d):(\d\d):(\d\d) \|")
REALMS = ("Albion", "Midgard", "Hibernia")
GOAL_REALMS = {1: "Albion", 2: "Midgard", 3: "Hibernia"}


def parse_fields(message: str) -> dict:
    return {m.group(1): (m.group(3) if m.group(3) is not None else m.group(2)) for m in FIELD.finditer(message)}


def stamp_of(line: str, year: int) -> dt.datetime | None:
    m = STAMP.match(line)
    if not m:
        return None
    month, day, hour, minute, second = (int(x) for x in m.groups())
    try:
        return dt.datetime(year, month, day, hour, minute, second)
    except ValueError:
        return None


def parse_since(text: str | None, year: int) -> dt.datetime | None:
    if not text:
        return None
    return dt.datetime.strptime(f"{year}/{text}", "%Y/%m/%d %H:%M")


def to_int(value, default=0):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def to_float(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def kill_row(fields: dict) -> dict:
    xp = [to_int(x) for x in str(fields.get("xpPerMember", "0")).split("/") if x != ""]
    on_target = fields.get("onTarget")
    return {
        "realm": fields.get("realm", "None"),
        "zone": fields.get("zone", ""),
        "every": max(1, to_int(fields.get("every"), 1)),
        "dropped": to_int(fields.get("dropped")),
        "con": fields.get("con", "UNKNOWN"),
        "party": fields.get("party", "solo"),
        "size": to_int(fields.get("size"), 1),
        "levelDiff": to_int(fields.get("levelDiff")),
        "xp": xp or [0],
        "share": to_float(fields.get("share"), 1.0),
        "otherParties": to_int(fields.get("otherParties")),
        "lost": to_float(fields.get("lost")),
        "onTarget": None if on_target in (None, "?") else to_int(on_target),
        "sameName": None if fields.get("sameName") in (None, "?") else to_int(fields.get("sameName")),
    }


def read_logs(paths, since=None, until=None, year=None):
    year = year or dt.date.today().year
    kills, goals = [], []
    for path in paths:
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line in handle:
                kill_at = line.find(KILL_MARKER)
                goal_at = line.find(GOAL_MARKER) if kill_at < 0 else -1
                if kill_at < 0 and goal_at < 0:
                    continue
                if since or until:
                    at = stamp_of(line, year)
                    if at is None or (since and at < since) or (until and at >= until):
                        continue
                if kill_at >= 0:
                    kills.append(kill_row(parse_fields(line[kill_at + len(KILL_MARKER):].rstrip("\n"))))
                    continue
                try:
                    goals.append(json.loads(line[goal_at + len(GOAL_MARKER):]))
                except ValueError:
                    continue
    return kills, goals


def summarize(rows: list[dict]) -> dict:
    xp = [x for row in rows for x in row["xp"]]
    known = [row for row in rows if row["onTarget"] is not None]
    off = [row for row in known if row["onTarget"] == 0]
    cons = Counter(row["con"] for row in rows)
    return {
        "rows": len(rows),
        "kills": sum(row["every"] * (1 + row["dropped"]) for row in rows),
        "groupedRows": sum(1 for row in rows if row["party"] in ("group", "pgroup")),
        "xpPerMemberMedian": statistics.median(xp) if xp else 0,
        "xpPerMemberMean": round(statistics.fmean(xp)) if xp else 0,
        "levelDiffMedian": statistics.median([row["levelDiff"] for row in rows]) if rows else 0,
        "cons": {con: round(n / len(rows), 3) for con, n in cons.most_common()} if rows else {},
        "killerShareMedian": round(statistics.median([row["share"] for row in rows]), 3) if rows else 0,
        "splitShare": round(sum(1 for row in rows if row["otherParties"] > 0) / len(rows), 3) if rows else 0,
        "lostShareMedian": round(statistics.median([row["lost"] for row in rows]), 3) if rows else 0,
        "offTargetShare": round(len(off) / len(known), 3) if known else None,
        "offTargetSameName": sum(1 for row in off if row["sameName"] == 1),
        "campKnownRows": len(known),
    }


def goal_summary(goals: list[dict]) -> dict:
    by_realm = defaultdict(lambda: [0, 0, 0])
    for goal in goals:
        realm = GOAL_REALMS.get(to_int(goal.get("realm")), "None")
        entry = by_realm[realm]
        entry[0] += 1
        entry[1] += max(0, to_int(goal.get("xp")))
        entry[2] += max(0, to_int(goal.get("targetXp")))
    return {realm: {"attempts": n, "xp": xp, "targetXp": target,
                    "offTargetXpShare": round(1 - target / xp, 3) if xp else None}
            for realm, (n, xp, target) in sorted(by_realm.items())}


def build_report(kills: list[dict], goals: list[dict], min_rows: int = 1) -> dict:
    by_realm = defaultdict(list)
    by_zone = defaultdict(list)
    for row in kills:
        by_realm[row["realm"]].append(row)
        by_zone[(row["realm"], row["zone"])].append(row)
    realm_order = [r for r in REALMS if r in by_realm] + sorted(r for r in by_realm if r not in REALMS)
    return {
        "realms": {realm: summarize(by_realm[realm]) for realm in realm_order},
        "zones": [dict(realm=realm, zone=zone, **summarize(rows))
                  for (realm, zone), rows in sorted(by_zone.items(),
                                                     key=lambda item: (realm_order.index(item[0][0]), -len(item[1])))
                  if len(rows) >= min_rows],
        "goalAttempts": goal_summary(goals),
    }


def pct(value) -> str:
    return "-" if value is None else f"{value * 100:.0f}%"


def render(report: dict) -> str:
    out = []
    head = f"{'':<34}{'rows':>6}{'kills':>8}{'xp/mbr med':>12}{'mean':>9}{'lvl+':>6}{'share':>7}{'split':>7}{'lost':>6}{'off':>6}"

    def line(name, s):
        return (f"{name[:33]:<34}{s['rows']:>6}{s['kills']:>8}{s['xpPerMemberMedian']:>12,.0f}{s['xpPerMemberMean']:>9,}"
                f"{s['levelDiffMedian']:>6}{pct(s['killerShareMedian']):>7}{pct(s['splitShare']):>7}"
                f"{pct(s['lostShareMedian']):>6}{pct(s['offTargetShare']):>6}")

    out.append("Per realm (AUTONOMOUS_KILL_XP)")
    out.append(head)
    for realm, s in report["realms"].items():
        out.append(line(realm, s))
        if s["cons"]:
            out.append("    cons: " + ", ".join(f"{con} {pct(share)}" for con, share in s["cons"].items()))
    out.append("")
    out.append("Per zone")
    out.append(head)
    for z in report["zones"]:
        out.append(line(f"{z['realm'][:3]} {z['zone']}", z))
    if report["goalAttempts"]:
        out.append("")
        out.append("Camp attempts (AUTONOMOUS_GOAL_ATTEMPT, detail.log): XP not from the camp's target")
        for realm, g in report["goalAttempts"].items():
            out.append(f"  {realm:<10} attempts={g['attempts']} xp={g['xp']:,} targetXp={g['targetXp']:,} "
                       f"offTarget={pct(g['offTargetXpShare'])}")
    out.append("")
    out.append("share = killer party's median share of the kill's damage; split = kills shared with another party or "
               "player; lost = damage that paid nobody; off = kills not the camp's target (camp known).")
    return "\n".join(out)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("logs", nargs="+", type=Path, help="server.log (and optionally detail.log)")
    parser.add_argument("--since", help='"MM/DD HH:MM" (the log\'s own time)')
    parser.add_argument("--hours", type=float, help="hours after --since (one played hour: 1)")
    parser.add_argument("--year", type=int, help="year of the log's dates (default: this year)")
    parser.add_argument("--min-rows", type=int, default=5, help="zones with fewer rows are left out (default 5)")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    year = args.year or dt.date.today().year
    since = parse_since(args.since, year)
    until = since + dt.timedelta(hours=args.hours) if since and args.hours else None
    kills, goals = read_logs(args.logs, since, until, year)
    if not kills and not goals:
        print("No AUTONOMOUS_KILL_XP or AUTONOMOUS_GOAL_ATTEMPT lines in the given logs and window.", file=sys.stderr)
        return 2
    report = build_report(kills, goals, args.min_rows)
    print(json.dumps(report, indent=2) if args.json else render(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
