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

Since #544's follow-up (FarmVibes' second-PC data):

* target con: the camp's target level minus the bot's level (campLevel - maxLevel on a kill row; targetLevel - level
  on an attempt), per realm, for solo and group kills apart, beside XP per member. Rows from before the field was
  logged read "?" and are left out of the target con (never counted as 0);
* zone names are merged across spellings ("Black Mtns. North" / "Black Mtns North", "Aegirs Landing" /
  "Aegir's Landing"): see zone_key();
* level bands: kills (by the party's highest level) and attempts (by the bot's level) per realm and 5-level band, so
  a realm with many new low bots is not compared with one at 20-29;
* re-rolled bots: a bot id whose attempt level drops below a level it already had is flagged and its attempts are
  left out of the band table (bot_level_snapshots.py does the same for database snapshots);
* arrival: the share of solo and group attempts that recorded arrivedUtc.

Since #671: a member at the level cap (50) is paid no XP by design (GameBot.GainExperience returns at the cap), so a
capped kill's 0s are left out of the XP median and mean and counted in a "capped" column instead. A row is capped
when its party's lowest level (minLevel) is at the cap; a row without minLevel counts as capped only when it is solo
at the cap. A realm with capped group kills shows "group <50" and "group 50" lines instead of one "group" line.

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
LEVEL_CAP = 50


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


BAND_WIDTH = 5
_ZONE_PUNCT = re.compile(r"[.'\u2019`]")
_SPACES = re.compile(r"\s+")


def zone_key(name: str) -> str:
    """One key for every spelling of a zone: no dots or apostrophes, one space, case folded.

    "Black Mtns. North" and "Black Mtns North", "Silvermine Mts" and "Silvermine Mts.", "Aegirs Landing" and
    "Aegir's Landing" each give one key."""
    return _SPACES.sub(" ", _ZONE_PUNCT.sub("", name or "")).strip().casefold()


def zone_names(names) -> dict:
    """Key -> the name shown for it: the spelling seen most, then the longest (the one with its punctuation)."""
    seen = defaultdict(Counter)
    for name in names:
        seen[zone_key(name)][(name or "").strip()] += 1
    return {key: sorted(counts.items(), key=lambda kv: (-kv[1], -len(kv[0]), kv[0]))[0][0] for key, counts in seen.items()}


def band_of(level) -> str:
    level = max(0, to_int(level))
    low = level // BAND_WIDTH * BAND_WIDTH
    return f"{low:02d}-{low + BAND_WIDTH - 1:02d}"


def optional_int(value):
    if value in (None, "", "?"):
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


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
    camp_level = optional_int(fields.get("campLevel"))
    if camp_level is not None and camp_level <= 0:
        camp_level = None
    max_level = to_int(fields.get("maxLevel"))
    min_level = optional_int(fields.get("minLevel"))
    party = fields.get("party", "solo")
    capped = (min_level >= LEVEL_CAP) if min_level is not None else (party == "solo" and max_level >= LEVEL_CAP)
    return {
        "realm": fields.get("realm", "None"),
        "zone": fields.get("zone", ""),
        "every": max(1, to_int(fields.get("every"), 1)),
        "dropped": to_int(fields.get("dropped")),
        "con": fields.get("con", "UNKNOWN"),
        "party": party,
        "size": to_int(fields.get("size"), 1),
        "levelDiff": to_int(fields.get("levelDiff")),
        "maxLevel": max_level,
        "minLevel": min_level,
        "capped": capped,
        "campLevel": camp_level,
        "targetCon": None if camp_level is None else camp_level - max_level,
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


def is_group(row: dict) -> bool:
    return row["party"] in ("group", "pgroup")


def is_capped(row: dict) -> bool:
    return bool(row.get("capped"))


def summarize(rows: list[dict]) -> dict:
    # Capped members are paid nothing by design (#671): their 0s are not XP lost, so they stay out of the XP figures.
    xp = [x for row in rows if not is_capped(row) for x in row["xp"]]
    capped = [row for row in rows if is_capped(row)]
    con = [row["targetCon"] for row in rows if row.get("targetCon") is not None]
    known = [row for row in rows if row["onTarget"] is not None]
    off = [row for row in known if row["onTarget"] == 0]
    cons = Counter(row["con"] for row in rows)
    return {
        "rows": len(rows),
        "kills": sum(row["every"] * (1 + row["dropped"]) for row in rows),
        "groupedRows": sum(1 for row in rows if row["party"] in ("group", "pgroup")),
        "xpPerMemberMedian": statistics.median(xp) if xp else (None if capped else 0),
        "xpPerMemberMean": round(statistics.fmean(xp)) if xp else (None if capped else 0),
        "cappedRows": len(capped),
        "cappedValues": sum(len(row["xp"]) for row in capped),
        "levelDiffMedian": statistics.median([row["levelDiff"] for row in rows]) if rows else 0,
        "targetConMedian": statistics.median(con) if con else None,
        "targetConMean": round(statistics.fmean(con), 2) if con else None,
        "targetConRows": len(con),
        "cons": {con: round(n / len(rows), 3) for con, n in cons.most_common()} if rows else {},
        "killerShareMedian": round(statistics.median([row["share"] for row in rows]), 3) if rows else 0,
        "splitShare": round(sum(1 for row in rows if row["otherParties"] > 0) / len(rows), 3) if rows else 0,
        "lostShareMedian": round(statistics.median([row["lost"] for row in rows]), 3) if rows else 0,
        "offTargetShare": round(len(off) / len(known), 3) if known else None,
        "offTargetSameName": sum(1 for row in off if row["sameName"] == 1),
        "campKnownRows": len(known),
    }


def goal_realm(goal: dict) -> str:
    return GOAL_REALMS.get(to_int(goal.get("realm")), "None")


def goal_is_group(goal: dict) -> bool:
    return bool(goal.get("groupId")) or to_int(goal.get("groupSize"), 1) > 1


def goal_target_con(goal: dict):
    target = optional_int(goal.get("targetLevel"))
    if target is None or target <= 0:
        return None
    return target - to_int(goal.get("level"))


def rerolled_bots(goals: list[dict]) -> dict:
    """Bot ids whose attempt level fell below a level they already had (in start order): re-rolled or replaced
    under the same id. Bot id -> (realm, highest level before the drop, level after it)."""
    flagged, best = {}, {}
    ordered = sorted((g for g in goals if g.get("botId") not in (None, "", 0)),
                     key=lambda g: str(g.get("startedUtc") or ""))
    for goal in ordered:
        bot, level = goal.get("botId"), to_int(goal.get("level"))
        if bot in best and level < best[bot] and bot not in flagged:
            flagged[bot] = (goal_realm(goal), best[bot], level)
        best[bot] = max(best.get(bot, 0), level)
    return flagged


def goal_summary(goals: list[dict]) -> dict:
    by_realm = defaultdict(list)
    for goal in goals:
        by_realm[goal_realm(goal)].append(goal)
    out = {}
    for realm, rows in sorted(by_realm.items()):
        xp = sum(max(0, to_int(g.get("xp"))) for g in rows)
        target = sum(max(0, to_int(g.get("targetXp"))) for g in rows)
        entry = {"attempts": len(rows), "xp": xp, "targetXp": target,
                 "offTargetXpShare": round(1 - target / xp, 3) if xp else None}
        for kind, part in (("solo", [g for g in rows if not goal_is_group(g)]),
                           ("group", [g for g in rows if goal_is_group(g)])):
            con = [c for c in (goal_target_con(g) for g in part) if c is not None]
            entry[kind] = {
                "attempts": len(part),
                "arrivedShare": round(sum(1 for g in part if g.get("arrivedUtc")) / len(part), 3) if part else None,
                "targetConMedian": statistics.median(con) if con else None,
                "targetConRows": len(con),
            }
        out[realm] = entry
    return out


def goal_bands(goals: list[dict], rerolled: dict) -> dict:
    """Per realm and 5-level band of the bot's level: attempts, XP per attempt-hour, target con. Re-rolled bots are
    left out (they would add new low-level gains to the realm)."""
    cells = defaultdict(list)
    for goal in goals:
        if goal.get("botId") in rerolled:
            continue
        cells[(goal_realm(goal), band_of(goal.get("level")))].append(goal)
    out = defaultdict(dict)
    for (realm, band), rows in sorted(cells.items()):
        seconds = sum(max(0.0, to_float(g.get("elapsedSeconds"))) for g in rows)
        xp = sum(max(0, to_int(g.get("xp"))) for g in rows)
        con = [c for c in (goal_target_con(g) for g in rows) if c is not None]
        out[realm][band] = {"attempts": len(rows), "xpPerHour": round(xp / seconds * 3600) if seconds > 0 else None,
                            "targetConMedian": statistics.median(con) if con else None}
    return dict(out)


def kill_bands(kills: list[dict]) -> dict:
    cells = defaultdict(list)
    for row in kills:
        cells[(row["realm"], band_of(row["maxLevel"]))].append(row)
    out = defaultdict(dict)
    for (realm, band), rows in sorted(cells.items()):
        s = summarize(rows)
        out[realm][band] = {"rows": s["rows"], "xpPerMemberMedian": s["xpPerMemberMedian"],
                            "targetConMedian": s["targetConMedian"], "groupedRows": s["groupedRows"]}
    return dict(out)


def realm_parties(rows: list[dict]) -> dict:
    groups = [r for r in rows if is_group(r)]
    out = {"solo": summarize([r for r in rows if r["party"] == "solo"]), "group": summarize(groups)}
    if any(is_capped(r) for r in groups):  # split only when there is something to split (#671)
        out["group <50"] = summarize([r for r in groups if not is_capped(r)])
        out["group 50"] = summarize([r for r in groups if is_capped(r)])
    return out


def build_report(kills: list[dict], goals: list[dict], min_rows: int = 1) -> dict:
    by_realm = defaultdict(list)
    by_zone = defaultdict(list)
    names = zone_names(row["zone"] for row in kills)
    for row in kills:
        by_realm[row["realm"]].append(row)
        by_zone[(row["realm"], zone_key(row["zone"]))].append(row)
    realm_order = [r for r in REALMS if r in by_realm] + sorted(r for r in by_realm if r not in REALMS)
    rerolled = rerolled_bots(goals)
    reroll_counts = Counter(realm for realm, _, _ in rerolled.values())
    return {
        "realms": {realm: summarize(by_realm[realm]) for realm in realm_order},
        "realmParties": {realm: realm_parties(by_realm[realm]) for realm in realm_order},
        "zones": [dict(realm=realm, zone=names[key], **summarize(rows))
                  for (realm, key), rows in sorted(by_zone.items(),
                                                    key=lambda item: (realm_order.index(item[0][0]), -len(item[1]),
                                                                      item[0][1]))
                  if len(rows) >= min_rows],
        "killBands": kill_bands(kills),
        "goalAttempts": goal_summary(goals),
        "goalBands": goal_bands(goals, rerolled),
        "rerolledBots": {realm: reroll_counts[realm] for realm in sorted(reroll_counts)},
    }


def pct(value) -> str:
    return "-" if value is None else f"{value * 100:.0f}%"


def xp_text(value, width: int) -> str:
    return f"{'-':>{width}}" if value is None else f"{value:>{width},.0f}"


def con_text(value) -> str:
    return "-" if value is None else f"{value:+g}"


def render(report: dict) -> str:
    out = []
    # The capped column appears only when some kill was capped, so a log without level-50 kills reads as before.
    show_capped = any(s.get("cappedRows") for s in report["realms"].values())
    head = (f"{'':<34}{'rows':>6}{'kills':>8}{'xp/mbr med':>12}{'tcon':>6}{'mean':>9}{'lvl+':>6}{'share':>7}"
            f"{'split':>7}{'lost':>6}{'off':>6}" + (f"{'capped':>12}" if show_capped else ""))

    def line(name, s):
        text = (f"{name[:33]:<34}{s['rows']:>6}{s['kills']:>8}{xp_text(s['xpPerMemberMedian'], 12)}"
                f"{con_text(s['targetConMedian']):>6}{xp_text(s['xpPerMemberMean'], 9)}"
                f"{s['levelDiffMedian']:>6}{pct(s['killerShareMedian']):>7}{pct(s['splitShare']):>7}"
                f"{pct(s['lostShareMedian']):>6}{pct(s['offTargetShare']):>6}")
        if show_capped:
            capped = f"{s.get('cappedRows', 0)}/{s.get('cappedValues', 0)}" if s.get("cappedRows") else "-"
            text += f"{capped:>12}"
        return text

    out.append("Per realm (AUTONOMOUS_KILL_XP)")
    out.append(head)
    for realm, s in report["realms"].items():
        out.append(line(realm, s))
        parties = report.get("realmParties", {}).get(realm, {})
        kinds = ("solo", "group <50", "group 50") if "group 50" in parties else ("solo", "group")
        for kind in kinds:
            part = parties.get(kind)
            if part and part["rows"]:
                out.append(line(f"  {kind}", part))
        if s["cons"]:
            out.append("    cons: " + ", ".join(f"{con} {pct(share)}" for con, share in s["cons"].items()))
    if report.get("killBands"):
        out.append("")
        out.append("Per realm and level band (the party's highest level)")
        out.append(f"{'':<16}{'rows':>6}{'xp/mbr med':>12}{'tcon':>6}{'grouped':>9}")
        for realm, bands in report["killBands"].items():
            for band, b in bands.items():
                out.append(f"{realm[:3] + ' ' + band:<16}{b['rows']:>6}{xp_text(b['xpPerMemberMedian'], 12)}"
                           f"{con_text(b['targetConMedian']):>6}{b['groupedRows']:>9}")
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
            for kind in ("solo", "group"):
                k = g.get(kind)
                if k and k["attempts"]:
                    out.append(f"    {kind:<6} attempts={k['attempts']} arrived={pct(k['arrivedShare'])} "
                               f"tcon={con_text(k['targetConMedian'])}")
        if report.get("goalBands"):
            out.append("  by level band (re-rolled bots left out):")
            for realm, bands in report["goalBands"].items():
                for band, b in bands.items():
                    xph = "-" if b["xpPerHour"] is None else f"{b['xpPerHour']:,}"
                    out.append(f"    {realm[:3]} {band} attempts={b['attempts']} xp/attempt-hour={xph} "
                               f"tcon={con_text(b['targetConMedian'])}")
        if report.get("rerolledBots"):
            out.append("  re-rolled bots (level dropped): " +
                       ", ".join(f"{realm} {n}" for realm, n in report["rerolledBots"].items()))
    out.append("")
    out.append("share = killer party's median share of the kill's damage; split = kills shared with another party or "
               "player; lost = damage that paid nobody; off = of the kills whose camp is known, the share that were not the camp's "
               "target (adds, dungeon paths): kills the bot made on the way, not XP lost; tcon = median target con, the "
               "camp's target level minus the bot's (party's highest) level, '-' when the log predates the field." +
               (" capped = kills (rows/member values) by a party at the level cap, paid nothing by design and left out "
                "of xp/mbr." if show_capped else ""))
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
