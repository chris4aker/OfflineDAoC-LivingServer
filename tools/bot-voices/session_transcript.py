#!/usr/bin/env python3
"""Turns the say gate's session chat log (logs/session-chat.log; issue #475) into a transcript per player, as the client
showed it: every channel interleaved in screen order, with the lines the gate held, the system-window bursts and the
player's /odd flags marked where they happened. Player and account names are masked (Player1, Player2, ...); bot names are
kept. It also counts defects per hour, per channel, per build (the realism plan's number, docs/realism-audit-1.2.3).

    python tools/bot-voices/session_transcript.py session-chat.log
    python tools/bot-voices/session_transcript.py session-chat.log --out sessions/
    python tools/bot-voices/session_transcript.py session-chat.log session-chat.log.2026-11-25.0 --json summary.json

What it reads (one row per line, written by source/server/GameServer/bots/autonomous/BotSayGate.cs):
* CHAT_SESSION build=... commit=...: opens a server process; every result is per build and never mixes builds.
* CHAT_SAY: a bot line a player saw (heard= lists the real players who got it).
* CHAT_HEAR: a player's line (heard= the players who saw it, the speaker first).
* CHAT_HELD: a line not sent, or a player line no bot answered, and why (code=, rule=, reason=).
* SCREEN_BURST: one system message shown to a player n times within a short window.
* CHAT_FLAG: the player's /odd [note], marking the rows in lines=.

Written with --out (DIR is created): session-<MM-DD>-<build>.jsonl (every row, names masked, in order) and one
session-<MM-DD>-<build>-<PlayerN>.md per player. Transcripts and logs stay out of git (/sessions/ is ignored); agents read
them where the owner puts them.

Defects per hour = /odd flags per hour of the player's session, by the channel of the newest line each flag marks. Reader-
confirmed defects are added by hand for now. A report, never a CI gate: it comes only from real play.

Exit code: 0 a transcript was made, 2 the log held no rows.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

KV_RE = re.compile(r'(\w+)=("(?:[^"]*)"|\S*)')
HEAD_RE = re.compile(r"^(\d\d)/(\d\d) (\d\d):(\d\d):(\d\d) \| (.*)$")
TAGS = ("CHAT_SESSION", "CHAT_SAY", "CHAT_HEAR", "CHAT_HELD", "SCREEN_BURST", "CHAT_FLAG")

# How the original client shows each channel ({who} the speaker, {text} the line). Bot and player lines alike.
FORMATS = {
    "say": '{who} says, "{text}"',
    "yell": '{who} yells, "{text}"',
    "faction": "({who}) <Faction> {text}",
    "group": '[Party] {who}: "{text}"',
    "guild": '[Guild] {who}: "{text}"',
    "alliance": '[Alliance] {who}: "{text}"',
    "lfg": "[LFG] {who}: {text}",
    "trade": "[Trade] {who}: {text}",
    "advice": "[ADVICE] {who}: {text}",
    "broadcast": "[Broadcast] {who}: {text}",
    "tell": '{who} sends, "{text}"',
}


def parse_kv(message: str) -> dict:
    return {k: v[1:-1] if v.startswith('"') and v.endswith('"') and len(v) >= 2 else v for k, v in KV_RE.findall(message)}


def names(value: str | None) -> list[str]:
    return [n for n in (value or "").split(",") if n]


class Clock:
    """Rows carry MM/DD HH:MM:SS without a year; a day boundary moving backwards means the year turned."""

    def __init__(self, year: int):
        self.year = year
        self.last: dt.datetime | None = None

    def at(self, month: int, day: int, h: int, m: int, s: int) -> dt.datetime:
        t = dt.datetime(self.year, month, day, h, m, s)
        if self.last is not None and t < self.last - dt.timedelta(days=180):
            self.year += 1
            t = t.replace(year=self.year)
        self.last = t
        return t


def read_rows(paths: list[Path], year: int) -> list[dict]:
    """Every gate row of the files, in file order, each with its time, tag and fields."""
    rows = []
    clock = Clock(year)
    for path in paths:
        with path.open(encoding="utf-8", errors="replace") as handle:
            for line in handle:
                match = HEAD_RE.match(line.rstrip("\r\n"))
                if not match:
                    continue
                message = match.group(6)
                tag = message.split(" ", 1)[0]
                if tag not in TAGS:
                    continue
                row = parse_kv(message[len(tag):])
                row["tag"] = tag
                row["time"] = clock.at(*(int(match.group(i)) for i in range(1, 6)))
                rows.append(row)
    return rows


def sections(rows: list[dict]) -> list[dict]:
    """The rows split by server process (CHAT_SESSION); rows before the first are an unnamed section."""
    out: list[dict] = []
    current = None
    for row in rows:
        if row["tag"] == "CHAT_SESSION" or current is None:
            current = {"build": row.get("build", "?") if row["tag"] == "CHAT_SESSION" else "?",
                       "commit": row.get("commit", "?") if row["tag"] == "CHAT_SESSION" else "?", "rows": []}
            out.append(current)
            if row["tag"] == "CHAT_SESSION":
                continue
        current["rows"].append(row)
    return [s for s in out if s["rows"]]


class Masker:
    """Real players' names as Player1, Player2, ... in every field and in the text; bot names are kept."""

    def __init__(self):
        self.names: dict[str, str] = {}

    def learn(self, name: str | None):
        if name and name.lower() not in self.names:
            self.names[name.lower()] = f"Player{len(self.names) + 1}"

    def name(self, name: str) -> str:
        return self.names.get((name or "").lower(), name)

    def text(self, text: str) -> str:
        if not self.names or not text:
            return text
        pattern = re.compile(r"\b(" + "|".join(re.escape(n) for n in sorted(self.names, key=len, reverse=True)) + r")\b", re.IGNORECASE)
        return pattern.sub(lambda m: self.names[m.group(1).lower()], text)


def players_of(rows: list[dict]) -> list[str]:
    """The real players of a section: who spoke as a player, was flagged for, had a burst, or heard a line."""
    seen: dict[str, None] = {}
    for row in rows:
        if row["tag"] == "CHAT_HEAR":
            seen.setdefault(row.get("from", ""), None)
        elif row["tag"] in ("CHAT_FLAG", "SCREEN_BURST"):
            seen.setdefault(row.get("to", ""), None)
        for name in names(row.get("heard")):
            seen.setdefault(name, None)
    return [n for n in seen if n]


def seen_by(row: dict, player: str) -> bool:
    if row["tag"] in ("CHAT_SAY", "CHAT_HEAR"):
        return player.lower() in (n.lower() for n in names(row.get("heard")))
    if row["tag"] in ("SCREEN_BURST", "CHAT_FLAG"):
        return row.get("to", "").lower() == player.lower()
    if row["tag"] == "CHAT_HELD":
        # A held line or an unanswered player line belongs to the player it was for.
        return row.get("to", "").lower() == player.lower()
    return False


def screen_line(row: dict, player: str, mask: Masker) -> str:
    """The row as the player's client showed it (held rows and flags as notes, not screen text)."""
    tag = row["tag"]
    text = mask.text(row.get("text", ""))
    if tag == "CHAT_SAY":
        fmt = FORMATS.get(row.get("ch", ""), "[{ch}] {who}: {text}")
        return fmt.format(who=row.get("from", "?"), text=text, ch=row.get("ch", "?"))
    if tag == "CHAT_HEAR":
        ch = row.get("ch", "")
        mine = row.get("from", "").lower() == player.lower()
        if ch == "tell":
            return f'You send, "{text}" to {mask.name(row.get("to", "?"))}' if mine else f'{mask.name(row.get("from", "?"))} sends, "{text}"'
        who = "You" if mine and ch in ("say", "yell") else mask.name(row.get("from", "?"))
        if mine and ch == "say":
            return f'You say, "{text}"'
        fmt = FORMATS.get(ch, "[{ch}] {who}: {text}")
        return fmt.format(who=who, text=text, ch=ch)
    if tag == "SCREEN_BURST":
        return f'[System x{row.get("n", "?")} in {row.get("window", "?")}] {text}'
    if tag == "CHAT_HELD":
        who = row.get("from", "nobody")
        return (f'   -- held ({row.get("ch", "?")}, {who}): {row.get("code", "?")} {row.get("rule", "")}: '
                f'{mask.text(row.get("reason", ""))}' + (f' [{text}]' if text else ""))
    if tag == "CHAT_FLAG":
        return f'>>> /odd #{row.get("seq", "?")} marks {row.get("lines", "") or "nothing"}: {mask.text(row.get("note", "")) or "(no note)"}'
    return ""


def masked_row(row: dict, mask: Masker, players: list[str]) -> dict:
    out = {}
    for key, value in row.items():
        if key == "time":
            out[key] = value.strftime("%Y-%m-%d %H:%M:%S")
        elif key in ("from", "to") and isinstance(value, str):
            out[key] = mask.name(value)
        elif key == "heard":
            out[key] = [mask.name(n) for n in names(value)]
        elif key in ("text", "note", "reason", "trigger", "room") and isinstance(value, str):
            out[key] = mask.text(value)
        else:
            out[key] = value
    out["seen_by"] = [mask.name(p) for p in players if seen_by(row, p)]
    return out


def defects(section: dict, players: list[str], mask: Masker) -> dict:
    """Flags per hour, per channel, for one build: hours are each player's span from first to last row they saw."""
    rows = section["rows"]
    hours = 0.0
    for player in players:
        times = [r["time"] for r in rows if seen_by(r, player)]
        if len(times) >= 2:
            hours += (max(times) - min(times)).total_seconds() / 3600
    by_channel: Counter = Counter(r.get("ch", "?") for r in rows if r["tag"] == "CHAT_FLAG")
    flags = sum(by_channel.values())
    lines: Counter = Counter(r.get("ch", "?") for r in rows if r["tag"] == "CHAT_SAY")
    return {
        "build": section["build"], "commit": section["commit"], "hours": round(hours, 2), "flags": flags,
        "per_hour": round(flags / hours, 2) if hours > 0 else None,
        "per_channel": {ch: {"flags": n, "per_hour": round(n / hours, 2) if hours > 0 else None} for ch, n in sorted(by_channel.items())},
        "bot_lines": dict(sorted(lines.items())),
        "held": sum(1 for r in rows if r["tag"] == "CHAT_HELD"),
        "bursts": sum(1 for r in rows if r["tag"] == "SCREEN_BURST"),
        "players": [mask.name(p) for p in players],
    }


def transcript(section: dict, player: str, mask: Masker, held: bool = True) -> str:
    out = [f"# Session transcript: {mask.name(player)}, build {section['build']} ({section['commit']})", "",
           "Screen order, all channels. `--` lines were held or went unanswered (not on screen); `>>>` lines are /odd flags.", "",
           "```"]
    for row in section["rows"]:
        if not seen_by(row, player) or (row["tag"] == "CHAT_HELD" and not held):
            continue
        out.append(f'{row["time"].strftime("%H:%M:%S")} #{row.get("seq", "?"):>6} {screen_line(row, player, mask)}')
    out.append("```")
    return "\n".join(out) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("logs", nargs="+", type=Path, help="session-chat.log files, oldest first")
    parser.add_argument("--out", type=Path, help="write the .jsonl and one .md per player here")
    parser.add_argument("--json", type=Path, help="write the defects-per-hour summary here")
    parser.add_argument("--year", type=int, default=dt.date.today().year, help="the year of the first row (default: this year)")
    parser.add_argument("--no-held", action="store_true", help="leave held lines out of the transcripts")
    args = parser.parse_args(argv)

    rows = read_rows(args.logs, args.year)
    parts = sections(rows)
    if not parts:
        print("No say gate rows in the log.", file=sys.stderr)
        return 2
    summary = []
    for section in parts:
        mask = Masker()
        players = players_of(section["rows"])
        for player in players:
            mask.learn(player)
        result = defects(section, players, mask)
        summary.append(result)
        print(f"build {result['build']} ({result['commit']}): {result['hours']} h, {result['flags']} flags"
              f" ({result['per_hour'] if result['per_hour'] is not None else '-'} per hour), {sum(result['bot_lines'].values())} bot lines,"
              f" {result['held']} held, {result['bursts']} bursts")
        for ch, value in result["per_channel"].items():
            print(f"  {ch}: {value['flags']} flags ({value['per_hour'] if value['per_hour'] is not None else '-'} per hour)")
        if args.out:
            args.out.mkdir(parents=True, exist_ok=True)
            day = section["rows"][0]["time"].strftime("%m-%d")
            stem = f"session-{day}-{re.sub(r'[^A-Za-z0-9._-]', '_', section['build'])}"
            with (args.out / f"{stem}.jsonl").open("w", encoding="utf-8") as handle:
                for row in section["rows"]:
                    handle.write(json.dumps(masked_row(row, mask, players), ensure_ascii=False) + "\n")
            for player in players:
                (args.out / f"{stem}-{mask.name(player)}.md").write_text(transcript(section, player, mask, not args.no_held), encoding="utf-8")
    if args.json:
        args.json.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
