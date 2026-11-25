"""AUTONOMOUS_KILL_XP (with AUTONOMOUS_GOAL_ATTEMPT from detail.log): XP per kill per realm and zone, via
kill_xp_report.py (#544). Uses whatever that tool offers: the #624 version adds target con, bands and re-rolls."""
from __future__ import annotations

import json

import kill_xp_report as kx

TITLE = "Kill XP (gamebots)"
ROWS = ["AUTONOMOUS_KILL_XP", "AUTONOMOUS_GOAL_ATTEMPT"]
ORDER = 70
KILL = "AUTONOMOUS_KILL_XP "
GOAL = "AUTONOMOUS_GOAL_ATTEMPT "


def rows(lines):
    kills, goals = [], []
    for line in lines:
        at = line.find(KILL)
        if at >= 0:
            kills.append(kx.kill_row(kx.parse_fields(line[at + len(KILL):])))
            continue
        at = line.find(GOAL)
        if at >= 0:
            try:
                goals.append(json.loads(line[at + len(GOAL):]))
            except ValueError:
                continue
    return kills, goals


def summarize(lines) -> str:
    kills, goals = rows(lines)
    if not kills and not goals:
        return "_No AUTONOMOUS_KILL_XP or AUTONOMOUS_GOAL_ATTEMPT rows._"
    build = getattr(kx, "build_report")
    try:
        report = build(kills, goals, min_rows=5)
    except TypeError:  # an older or newer signature
        report = build(kills, goals)
    render = getattr(kx, "render", None)
    if render is None:
        return "```json\n" + json.dumps(report, indent=1, default=str)[:20_000] + "\n```"
    return "```text\n" + render(report) + "\n```"
