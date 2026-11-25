"""RvR (RVR_DECIDE, RVR_HOUR, the fight and siege rows): the hour-by-hour report of rvr_hour.py."""
from __future__ import annotations

import datetime as dt
from pathlib import Path

import rvr_hour as rh

TITLE = "RvR by hour"
ROWS = ["RVR_*", "BG_*", "SIEGE_*", "FORK_BUILD", "AUTONOMOUS_PVP_STALEMATE", "AUTONOMOUS_UNREACHABLE_TARGET",
        "BOT_SOLO_BAIL"]
ORDER = 80


def to_lines(lines, year: int):
    """rvr_hour.read() over lines instead of a file: the same Line objects, in order, each with its build."""
    build, last, out = "before-FORK_BUILD", None, []
    for raw in lines:
        head = rh.parse_head(raw)
        if not head:
            continue
        month, day, h, m, s, rest = head
        _, _, message = rh.split_rest(rest)
        tag = message.split(" ", 1)[0]
        try:
            t = dt.datetime(year, month, day, h, m, s)
        except ValueError:
            continue
        if last and t < last - dt.timedelta(days=200):
            t = t.replace(year=t.year + 1)
        last = t
        f = rh.kv(message)
        if tag == "FORK_BUILD":
            build = f"{f.get('name', '?')}@{f.get('commit', '?')[:7]}"
        out.append(rh.Line(t, tag, f, message.rstrip("\n"), build))
    return out


def summarize(lines) -> str:
    parsed = to_lines(lines, dt.date.today().year)
    if not any(line.tag != "FORK_BUILD" for line in parsed):
        return "_No RvR rows._"
    return "```text\n" + rh.text(rh.report(parsed), Path("logs")) + "\n```"
