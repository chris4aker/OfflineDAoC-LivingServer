#!/usr/bin/env python3
"""The say gate's ratchet (issue #475, docs/realism-audit-1.2.3/structure.md "A. The say gate", step 6): no new bot line may
leave outside the gate.

Two checks over the server's bot code (source/server/GameServer/bots and mode165):
* Strict: `BotChatTranscript.Bot(` (the record of a bot line) is called only from BotSayGate.cs. An exit hands its line
  to `BotSayGate.Say(...)` with its send; the gate records it.
* Ratchet: the raw sends outside a `BotSayGate.Say(...)` call (a group, guild or channel message, a tell, a /say or
  /yell, a message to a player's client) may only fall, file by file, from the counts in say_gate_ratchet.json. Many are
  not bot lines (command replies, system notices); they are counted so that a new raw send shows up in review. When a
  count falls, run with --update to lower the baseline.

    python tools/bot-voices/say_gate_ratchet.py            # check (CI)
    python tools/bot-voices/say_gate_ratchet.py --update   # write the current counts as the baseline
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SERVER = REPO / "source/server/GameServer"
SCOPE = ("bots", "mode165")
GATE = "bots/autonomous/BotSayGate.cs"
BASELINE = Path(__file__).resolve().parent / "say_gate_ratchet.json"

RECORD_RE = re.compile(r"BotChatTranscript\.Bot\(")
GATE_RE = re.compile(r"BotSayGate\.Say\(")
SEND_RE = re.compile(
    r"SendMessageToGroupMembers\(|SendMessageToGuildMembers\(|ChatUtil\.Send\w*Message\(|\.Out\.SendMessage\(|"
    r"\bbase\.Say\(|\.Yell\(|\.Say\(")


def strip_comment(line: str) -> str:
    """The line without a // comment (outside a string literal)."""
    in_string = False
    i = 0
    while i < len(line):
        c = line[i]
        if c == '"' and (i == 0 or line[i - 1] != "\\"):
            in_string = not in_string
        elif not in_string and line.startswith("//", i):
            return line[:i]
        i += 1
    return line


def gated_lines(lines: list[str]) -> set[int]:
    """The line numbers inside a `BotSayGate.Say(...)` call (its whole argument list, lambdas included)."""
    gated: set[int] = set()
    depth = 0
    inside = False
    for number, raw in enumerate(lines):
        line = strip_comment(raw)
        start = 0
        if not inside:
            match = GATE_RE.search(line)
            if not match:
                continue
            inside, depth, start = True, 0, match.end() - 1
        gated.add(number)
        in_string = False
        for i in range(start, len(line)):
            c = line[i]
            if c == '"' and line[i - 1] != "\\":
                in_string = not in_string
            elif not in_string and c == "(":
                depth += 1
            elif not in_string and c == ")":
                depth -= 1
                if depth == 0:
                    inside = False
                    break
    return gated


def scan(root: Path = SERVER) -> tuple[dict[str, int], list[str], int]:
    """(raw sends outside the gate per file, files that record a bot line outside the gate, gate calls)."""
    counts: dict[str, int] = {}
    outside: list[str] = []
    gate_calls = 0
    for folder in SCOPE:
        for path in sorted((root / folder).rglob("*.cs")):
            rel = path.relative_to(root).as_posix()
            lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
            code = [strip_comment(line) for line in lines]
            if rel != GATE and any(RECORD_RE.search(line) for line in code):
                outside.append(rel)
            gate_calls += sum(len(GATE_RE.findall(line)) for line in code)
            gated = gated_lines(lines)
            n = sum(len(SEND_RE.findall(line)) for i, line in enumerate(code) if i not in gated)
            if n:
                counts[rel] = n
    return counts, outside, gate_calls


def check(counts: dict[str, int], baseline: dict[str, int]) -> tuple[list[str], list[str]]:
    """(rises: files whose raw sends grew or are new, falls: files whose count fell, for --update)."""
    rises = [f"{f}: {n} raw sends outside the say gate (baseline {baseline.get(f, 0)})" for f, n in sorted(counts.items())
             if n > baseline.get(f, 0)]
    falls = [f"{f}: {counts.get(f, 0)} (baseline {n})" for f, n in sorted(baseline.items()) if counts.get(f, 0) < n]
    return rises, falls


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="No new bot line may leave outside the say gate.")
    parser.add_argument("--update", action="store_true", help="write the current counts as the baseline")
    args = parser.parse_args(argv)
    counts, outside, gate_calls = scan()
    if outside:
        for rel in outside:
            print(f"::error title=Say gate::{rel} records a bot line itself (BotChatTranscript.Bot); hand it to BotSayGate.Say")
        return 1
    if args.update:
        BASELINE.write_text(json.dumps({"gate_calls": gate_calls, "raw_sends": counts}, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(f"Wrote {BASELINE.name}: {sum(counts.values())} raw sends in {len(counts)} files, {gate_calls} gate calls.")
        return 0
    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
    rises, falls = check(counts, baseline["raw_sends"])
    if gate_calls < baseline.get("gate_calls", 0):
        rises.append(f"{gate_calls} BotSayGate.Say calls, fewer than the baseline's {baseline['gate_calls']}: an exit left the gate")
    for line in rises:
        print(f"::error title=Say gate ratchet::{line}")
    for line in falls:
        print(f"Fell: {line} (run with --update to lower the baseline)")
    print(f"Say gate: {gate_calls} gate calls, {sum(counts.values())} raw sends outside it in {len(counts)} files; "
          f"{'FAIL' if rises else 'ok'}.")
    return 1 if rises else 0


if __name__ == "__main__":
    sys.exit(main())
