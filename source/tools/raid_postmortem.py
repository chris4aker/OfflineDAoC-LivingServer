#!/usr/bin/env python3
"""The timeline of one raid attempt (#644 part 6), from the part-1 rows (docs/RAID-ATTEMPT-ROWS.md).

    python source/tools/raid_postmortem.py dungeon-albion-krondon.3 runtime/server/logs
    python source/tools/raid_postmortem.py dragon-albion.2 server.log detail.log --run 2 --out krondon-3.md
    python source/tools/raid_postmortem.py --list runtime/server/logs

Reads the logs as living_report.py does (files, or folders with server.log, detail.log and their rotated copies). For the
attempt it prints, as markdown:
- the pull (RAID_ATTEMPT): boss, size against the period size, Special Forces, roles, classes, the failed attempt before
  it and the wait since;
- the end (RAID_ATTEMPT_END): outcome, time, engaged, fallen, died against left, the boss's health;
- every raider death (RAID_DEATH) in order, with its second into the pull, role, killer, last hit, mechanic, distance,
  healers in range and attackers; the deaths per 15 s; when the first raider and a quarter and half of the pull's size
  had died;
- every other row that carries the same attempt= (REALM_EXPEDITION_ATTEMPT_ENDED, _WIPE, _ENCOUNTER_RESET,
  _SKIRMISH_LOST), at its second into the pull.

The attempt id's count starts again at 1 when the server restarts, so one id can be two pulls in a log set: --list shows
them, and --run picks one (default: the last). No raider names: the rows carry none. Local logs only.
Exit codes: 0 printed, 1 the attempt is not in the logs, 2 bad arguments.
"""
from __future__ import annotations

import argparse
import datetime as dt
import re
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import living_report as lr  # noqa: E402
from living_report_sections._raid import ROWS, attempts, count, killer_of  # noqa: E402
from living_report_sections._util import fields, message, stamp, table, tag  # noqa: E402

BUCKET_S = 15
YEAR = 2000  # leap year: 02/29 parses; the logs carry no year, only the offsets matter


def read(files) -> list[str]:
    """Every headed line of the files, in time order (as living_report.py orders them)."""
    keyed = []
    order = 0
    for path in files:
        try:
            handle = open(path, encoding="utf-8", errors="replace")
        except OSError as error:
            print(f"warning: {path}: {error}", file=sys.stderr)
            continue
        with handle:
            for raw in handle:
                line = raw.rstrip("\r\n")
                if lr.STAMP.match(line):
                    order += 1
                    keyed.append((line[:14], order, line))
    return [line for _, _, line in sorted(keyed)]


def when(text: str):
    try:
        return dt.datetime.strptime(f"{YEAR}/{text}", "%Y/%m/%d %H:%M:%S")
    except ValueError:
        return None


def offset(start: str, at: str):
    """Seconds from the pull's stamp to another stamp (across midnight too); None when either is missing."""
    a, b = when(start), when(at)
    if a is None or b is None:
        return None
    seconds = (b - a).total_seconds()
    if seconds < -12 * 3600:   # the log crossed New Year: a later line with an earlier month
        seconds += 366 * 86400
    return int(seconds)


def mmss(seconds):
    if seconds is None:
        return "-"
    sign = "-" if seconds < 0 else ""
    seconds = abs(int(seconds))
    return f"{sign}{seconds // 60}:{seconds % 60:02d}"


def other_rows(lines, attempt_id: str, start: str, end: str):
    """Lines that carry attempt=<id> and are not part-1 rows, between the pull and the end (inclusive)."""
    pattern = re.compile(r"(?:^|\s)attempt=" + re.escape(attempt_id) + r"(?=\s|$)")
    out = []
    for line in lines:
        if tag(line) in ROWS or not pattern.search(message(line)):
            continue
        at = stamp(line)
        before = offset(start, at) if start else None
        after = offset(end, at) if end else None
        if (before is not None and before < 0) or (after is not None and after > 0):
            continue   # the same id in another run of the server
        out.append(line)
    return out


def report(attempt, lines) -> str:
    p, e = attempt.pull, attempt.end or {}
    out = [f"# Raid attempt {attempt.key}", ""]
    if p:
        size, period = p.get("size", "-"), p.get("period", "-")
        out.append(f"**Pull** at {attempt.pulled_at}: {p.get('kind', '?')} `{p.get('event', '?')}`, boss **{attempt.boss}** "
                   f"(pull {p.get('n', '?')} of this boss), region {p.get('region', '?')}.")
        out.append(f"- Size {size} against a period size of {period} ({p.get('sizePct', '-')}%), Special Forces "
                   f"{p.get('sf', '-')}, real players {p.get('players', '-')}, build {p.get('build', '-')}.")
        roles = (p.get("roles") or "-").split(":")
        if len(roles) == 4:
            out.append(f"- Roles: tank {roles[0]}, healer {roles[1]}, buffer {roles[2]}, attacker {roles[3]}.")
        out.append(f"- Classes: {(p.get('classes') or '-').replace(',', ', ').replace(':', ' ')}.")
        if p.get("prev"):
            out.append(f"- After failed attempt `{p['prev']}`: {mmss(count(p.get('wipeToPullS')))} from its end to this pull.")
        if p.get("gatherS"):
            out.append(f"- Muster or regroup before the pull: {mmss(count(p.get('gatherS')))}.")
    else:
        out.append("**No pull row** for this attempt in the logs (they start after it).")
    out.append("")
    if attempt.end:
        out.append(f"**End** at {attempt.ended_at}: **{e.get('outcome', '?')}** after {mmss(count(e.get('secs')))}. "
                   f"Engaged {e.get('engaged', '-')}, fallen {e.get('fallen', '-')} (died {e.get('died', '-')}, left "
                   f"{e.get('left', '-')}), {e.get('deaths', '-')} deaths. Boss health {e.get('health', '-')}%, lowest "
                   f"{e.get('lowest', '-')}%.")
        logged = len(attempt.deaths)
        if count(e.get("deaths")) is not None and count(e.get("deaths")) != logged:
            out.append(f"_The end row counts {e.get('deaths')} deaths; {logged} RAID_DEATH rows are in these logs._")
    else:
        out.append("**No end row**: the attempt was still live when the logs stop, or the server stopped.")

    deaths = sorted(attempt.deaths, key=lambda sd: count(sd[1].get("t")) or 0)
    out += ["", f"## Deaths ({len(deaths)})", ""]
    if deaths:
        size = count(p.get("size"))
        times = [count(d.get("t")) for _, d in deaths]
        marks = [f"first at {mmss(times[0])}"]
        if size:
            for share, name in ((4, "a quarter"), (2, "half")):
                need = -(-size // share)
                if len(times) >= need:
                    marks.append(f"{name} of the pull's {size} at {mmss(times[need - 1])}")
        out.append("Raider deaths: " + ", ".join(marks) + ".")
        buckets = Counter((t or 0) // BUCKET_S for t in times)
        out += ["", f"Deaths per {BUCKET_S} s: " + ", ".join(
            f"{mmss(b * BUCKET_S)} {n}" for b, n in sorted(buckets.items())) + "."]
        roles = Counter(d.get("role", "-") for _, d in deaths)
        killers = Counter(killer_of(d) for _, d in deaths)
        out += ["", "By role: " + ", ".join(f"{r} {n}" for r, n in roles.most_common()) + ". By killer: " +
                ", ".join(f"{k} {n}" for k, n in killers.most_common()) + "."]
        out += ["", table(["t", "Raider", "Kind", "Class", "Role", "Level", "Killer", "Last hit", "Spell", "Mechanic",
                           "Distance", "Healers in range", "Attackers"],
                          [[mmss(count(d.get("t"))), d.get("idx", "-"),
                            d.get("kind", "-") + (" (you)" if d.get("player") == "1" else ""),
                            d.get("class", "-"), d.get("role", "-"), d.get("level", "-"), killer_of(d), d.get("source", "-"),
                            d.get("spellId", "0") if d.get("spellId") not in (None, "0") else "-", d.get("mechanic", "-"),
                            d.get("dist", "-"), d.get("healersIn", "-"), d.get("attackers", "-")] for _, d in deaths])]
        again = Counter(d.get("idx") for _, d in deaths if d.get("idx"))
        twice = sorted((i for i, n in again.items() if n > 1), key=lambda i: count(i) or 0)
        if twice:
            out += ["", f"Raiders who died more than once: {', '.join(f'#{i} ({again[i]}x)' for i in twice)}."]
    else:
        out.append("No raider died in this attempt.")

    rest = other_rows(lines, attempt.id, attempt.pulled_at, attempt.ended_at)
    if rest:
        out += ["", f"## Other rows of this attempt ({len(rest)})", ""]
        for line in rest:
            at = offset(attempt.pulled_at, stamp(line)) if attempt.pulled_at else None
            out.append(f"- {mmss(at)} `{message(line)}`")
    return "\n".join(out) + "\n"


def listing(found) -> str:
    if not found:
        return "No RAID_ATTEMPT rows in these logs.\n"
    rows = [[a.key, a.pulled_at or "-", a.boss, a.pull.get("size", "-"), a.outcome, mmss(a.secs), len(a.deaths)] for a in found]
    return table(["Attempt", "Pulled", "Boss", "Size", "Outcome", "Time", "Deaths"], rows) + "\n"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("paths", nargs="+", metavar="ATTEMPT LOGS",
                    help="the attempt id (e.g. dungeon-albion-krondon.3; none with --list), then log files or folders")
    ap.add_argument("--list", action="store_true", help="list the attempts in the logs")
    ap.add_argument("--run", type=int, help="which pull of an id seen more than once (1, 2, ...; default the last)")
    ap.add_argument("--out", type=Path, help="write the markdown here instead of printing it")
    args = ap.parse_args(argv)
    if args.list:
        args.attempt, logs = None, args.paths
    else:
        args.attempt, logs = args.paths[0], args.paths[1:]
        if not logs:
            ap.print_usage(sys.stderr)
            print("error: give the attempt id, then the logs (or --list and the logs)", file=sys.stderr)
            return 2
    files = lr.log_files(logs)
    if not files:
        print("error: no log files found", file=sys.stderr)
        return 2
    lines = read(files)
    found = attempts(lines)
    if args.list:
        text = listing(found)
    else:
        runs = [a for a in found if a.id == args.attempt]
        if not runs:
            print(f"error: attempt {args.attempt} is not in these logs ({len(found)} attempts; --list shows them)",
                  file=sys.stderr)
            return 1
        if args.run is not None and not 1 <= args.run <= len(runs):
            print(f"error: {args.attempt} has {len(runs)} pull(s) in these logs", file=sys.stderr)
            return 2
        chosen = runs[(args.run or len(runs)) - 1]
        text = report(chosen, lines)
        if len(runs) > 1 and args.run is None:
            text = (f"_{args.attempt} was pulled {len(runs)} times in these logs (the server restarted between them); "
                    f"showing the last. --run 1..{len(runs)} picks one._\n\n") + text
    if args.out:
        args.out.write_text(text, encoding="utf-8")
    else:
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8")
        print(text, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
