#!/usr/bin/env python3
"""Levels gained per gamebot between database snapshots, by realm and level band, with re-rolled bots flagged
(GitHub #544, FarmVibes' second-PC data).

    python source/tools/bot_level_snapshots.py backup-11-20.db backup-11-21.db backup-11-24.db
    python source/tools/bot_level_snapshots.py a.db b.db --json

Each snapshot is a copy of runtime/data/opendaoc.sqlite3.db (a daily backup). It is opened read-only and only the
offline_world_bots roster is read. For each pair of consecutive snapshots, and each realm and 5-level band of the
bot's level in the earlier snapshot, the report gives the bots and the mean levels they gained.

A comparison by realm alone is skewed by re-rolled bots: a bot replaced by a new level 1 one gains many cheap levels.
So these are flagged and left out of the band table:

* level dropped: the same BotId has a lower level in the later snapshot (re-rolled in place);
* replaced: a BotId gone from the later snapshot whose name and realm now belong to a new BotId;
* new: a BotId only in the later snapshot (a new bot, or a replacement under any name; a replaced row's new id is
  counted here too).

Retired bots (IsRetired, where the column exists) are left out too. Names are used only to match a replaced row and
are never printed: the report has realms, bands and counts.
"""
from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path

REALMS = {1: "Albion", 2: "Midgard", 3: "Hibernia"}
BAND_WIDTH = 5


def band_of(level: int) -> str:
    low = max(0, int(level)) // BAND_WIDTH * BAND_WIDTH
    return f"{low:02d}-{low + BAND_WIDTH - 1:02d}"


def read_snapshot(path: Path) -> dict:
    """BotId -> {name, realm, level} from one snapshot, read-only. Older rosters without IsRetired still read."""
    uri = Path(path).resolve().as_uri() + "?mode=ro"
    with sqlite3.connect(uri, uri=True) as db:
        columns = {row[1].lower() for row in db.execute("PRAGMA table_info(offline_world_bots)")}
        if not {"botid", "name", "realm", "level"} <= columns:
            raise ValueError(f"{path}: no offline_world_bots roster with BotId, Name, Realm and Level")
        retired = ", IsRetired" if "isretired" in columns else ", 0"
        rows = db.execute(f"SELECT BotId, Name, Realm, Level{retired} FROM offline_world_bots").fetchall()
    return {int(bot): {"name": str(name or ""), "realm": int(realm or 0), "level": int(level or 0),
                       "retired": bool(is_retired)}
            for bot, name, realm, level, is_retired in rows}


def compare(before: dict, after: dict) -> dict:
    """One pair of snapshots: the band table without flagged bots, and the flags per realm."""
    flags = defaultdict(lambda: {"levelDropped": 0, "replaced": 0, "new": 0, "retired": 0})
    gone = {bot for bot in before if bot not in after}
    new = {bot for bot in after if bot not in before}
    # A replaced row: its name and realm come back under a new id.
    new_by_name = {(after[bot]["name"].casefold(), after[bot]["realm"]): bot for bot in new}
    for bot in gone:
        row = before[bot]
        if (row["name"].casefold(), row["realm"]) in new_by_name:
            flags[REALMS.get(row["realm"], "None")]["replaced"] += 1
    for bot in new:
        flags[REALMS.get(after[bot]["realm"], "None")]["new"] += 1

    cells = defaultdict(list)
    for bot, old in before.items():
        if bot not in after:
            continue
        cur = after[bot]
        realm = REALMS.get(old["realm"], "None")
        if old["retired"] or cur["retired"]:
            flags[realm]["retired"] += 1
            continue
        if cur["level"] < old["level"]:
            flags[realm]["levelDropped"] += 1
            continue
        cells[(realm, band_of(old["level"]))].append(cur["level"] - old["level"])

    bands = defaultdict(dict)
    order = list(REALMS.values())
    for (realm, band), gains in sorted(cells.items(),
                                       key=lambda kv: (order.index(kv[0][0]) if kv[0][0] in order else 9, kv[0][1])):
        bands[realm][band] = {"bots": len(gains), "levelsGained": sum(gains),
                              "meanGain": round(sum(gains) / len(gains), 2)}
    return {"bands": dict(bands), "flags": {realm: flags[realm] for realm in sorted(flags)}}


def build_report(paths: list[Path]) -> dict:
    snapshots = [read_snapshot(path) for path in paths]
    return {"windows": [dict(before=paths[i].name, after=paths[i + 1].name, **compare(snapshots[i], snapshots[i + 1]))
                        for i in range(len(paths) - 1)]}


def render(report: dict) -> str:
    out = []
    for window in report["windows"]:
        out.append(f"{window['before']} -> {window['after']}: levels gained per bot by starting band "
                   "(re-rolled, new and retired bots left out)")
        out.append(f"  {'band':<8}" + "".join(f"{realm:>18}" for realm in REALMS.values()))
        bands = sorted({band for realm in window["bands"].values() for band in realm})
        for band in bands:
            cells = []
            for realm in REALMS.values():
                cell = window["bands"].get(realm, {}).get(band)
                cells.append(f"{cell['meanGain']:>8.2f} ({cell['bots']:>3} bots)" if cell else f"{'-':>18}")
            out.append(f"  {band:<8}" + "".join(f"{c:>18}" for c in cells))
        flagged = [f"{realm}: {f['levelDropped']} level dropped, {f['replaced']} replaced, {f['new']} new, "
                   f"{f['retired']} retired" for realm, f in window["flags"].items() if any(f.values())]
        out.append("  flagged: " + ("; ".join(flagged) if flagged else "none"))
        out.append("")
    return "\n".join(out).rstrip()


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("snapshots", nargs="+", type=Path, help="two or more database snapshots, oldest first")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    if len(args.snapshots) < 2:
        print("Give at least two snapshots, oldest first.", file=sys.stderr)
        return 2
    try:
        report = build_report(args.snapshots)
    except (sqlite3.Error, ValueError) as error:
        print(error, file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2) if args.json else render(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
