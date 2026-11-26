#!/usr/bin/env python3
"""The timeline of one raid attempt (#644 part 6), from the part-1 rows (docs/RAID-ATTEMPT-ROWS.md).

    python source/tools/raid_postmortem.py dungeon-albion-krondon.3 runtime/server/logs
    python source/tools/raid_postmortem.py dragon-albion.2 server.log detail.log --run 2 --out krondon-3.md
    python source/tools/raid_postmortem.py --list runtime/server/logs
    python source/tools/raid_postmortem.py --check raid-sim/raid-sims.raid.log

Reads the logs as living_report.py does (files, or folders with server.log, detail.log and their rotated copies). For the
attempt it prints, as markdown:
- the pull (RAID_ATTEMPT): boss, size against the period size, Special Forces, roles, classes, the failed attempt before
  it and the wait since;
- the end (RAID_ATTEMPT_END): outcome, time, engaged, fallen, died against left, the boss's health;
- every raider death (RAID_DEATH) in order, with its second into the pull, role, killer, last hit, mechanic, distance,
  healers in range and attackers; the deaths per 15 s; when the first raider and a quarter and half of the pull's size
  had died;
- the timeline: each 15 s fight sample (RAID_FIGHT_SAMPLE: boss health, raiders alive and engaged by role, the healers'
  power, whom the boss fights, the median distance, mezzed and stunned) with the deaths, the mechanic fires
  (RAID_MECHANIC_FIRE) and the boss's target changes (RAID_THREAT, diagnostics switch) between them, by second;
- the role totals at the end (RAID_ROLE_TOTALS, diagnostics switch);
- every other row that carries the same attempt= (REALM_EXPEDITION_ATTEMPT_ENDED, _WIPE, _ENCOUNTER_RESET,
  _SKIRMISH_LOST), at its second into the pull.

The attempt id's count starts again at 1 when the server restarts, so one id can be two pulls in a log set: --list shows
them, and --run picks one (default: the last). No raider names: the rows carry none. Local logs only.

--check reads the logs as a row check (the server job runs it on the rows the raid sims write, #644 part 6): every raid row
has the server's fields in order, every attempt has its pull, a simulated one (build=sim) its end, the RAID_DEATH rows are
the end's deaths= and died + left is fallen. It lists the attempts and exits 1 on any problem (or when there is no attempt).
Exit codes: 0 printed (or checked clean), 1 the attempt is not in the logs (or --check found a problem), 2 bad arguments.
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
from living_report_sections._raid import ROWS, attempts, check, count, killer_of, split  # noqa: E402
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

    out += timeline(attempt)
    out += role_totals(attempt)

    rest = other_rows(lines, attempt.id, attempt.pulled_at, attempt.ended_at)
    if rest:
        out += ["", f"## Other rows of this attempt ({len(rest)})", ""]
        for line in rest:
            at = offset(attempt.pulled_at, stamp(line)) if attempt.pulled_at else None
            out.append(f"- {mmss(at)} `{message(line)}`")
    return "\n".join(out) + "\n"


def roles_text(value) -> str:
    s = split(value)
    return "-" if s is None else f"T{s['tank']} H{s['healer']} B{s['buffer']} A{s['attacker']}"


def sample_text(f: dict) -> str:
    """One RAID_FIGHT_SAMPLE as a timeline line (no names: the row has none)."""
    target = f.get("tgtKind", "-")
    on = {"tank": "on a tank", "healer": "on a healer", "buffer": "on a buffer", "attacker": "on an attacker",
          "raider": "on a raider", "pet": "on a raider's pet", "other": "on someone outside the attempt"}.get(target, "no target")
    parts = [f"boss {f.get('bossPct', '-')}%"]
    if f.get("phase") not in (None, "-"):
        parts.append(f"phase {f['phase']}")
    if f.get("adds") not in (None, "-"):
        parts.append(f"adds {f['adds']}")
    parts += [f"alive {roles_text(f.get('alive'))}", f"engaged {roles_text(f.get('engaged'))}",
              f"healer power {f.get('healerPow', '-')}%" + (f" ({f['healersLow']} under 10%)"
                                                          if count(f.get("healersLow")) else ""),
              on, f"median distance {f.get('medDist', '-')}"]
    if count(f.get("mezzed")) or count(f.get("stunned")):
        parts.append(f"mezzed {f.get('mezzed', '0')}, stunned {f.get('stunned', '0')}")
    return "sample: " + ", ".join(parts)


def death_text(d: dict) -> str:
    who = d.get("kind", "-") + (" (you)" if d.get("player") == "1" else "")
    mech = d.get("mechanic", "-")
    return (f"death: #{d.get('idx', '-')} {d.get('role', '-')} {d.get('class', '-')} ({who}), {killer_of(d)}, "
            f"{d.get('source', '-')}" + (f", during {mech}" if mech not in (None, "-") else ""))


def mechanic_text(f: dict) -> str:
    area = f.get("exposedAtCall") not in (None, "-")
    parts = [f"{f.get('mechanic', '?')} of {f.get('boss', '?')}" + (" (called)" if f.get("called") == "1" else " (measure only)"),
             f"lands in {f.get('leadMs', '-')} ms"]
    if area:
        parts.append(f"radius {f.get('radius', '-')}: exposed {f.get('exposedAtCall')}, within {f.get('within', '-')}, "
                     f"avoided {f.get('avoided', '-')}" + (f", stepped out in {f['reactMedianMs']} ms (median)"
                                                         if f.get("reactMedianMs") not in (None, "-") else ""))
    parts.append(f"hit {f.get('hit', '-')}, died {f.get('died', '-')}, damage {f.get('damage', '-')}")
    return "mechanic: " + ", ".join(parts)


def threat_text(f: dict) -> str:
    return (f"threat: {f.get('from', '-')} -> {f.get('to', '-')} ({f.get('toKind', '-')}), {f.get('why', '-')}, "
            f"held {mmss((count(f.get('heldMs')) or 0) // 1000)}")


def role_totals(attempt) -> list[str]:
    if not attempt.roles:
        return []
    rows = [[f.get("role", "-"), f.get("raiders", "-"), f.get("dealtBoss", "-"), f.get("dealtAdds", "-"), f.get("took", "-"),
             f.get("healed", "-"), f.get("overheal", "-"), f.get("deaths", "-"), f.get("cc", "-"), f.get("interrupts", "-"),
             f.get("powerOuts", "-"), (f.get("classes") or "-").replace(",", ", ")] for _, f in attempt.roles]
    return ["", "## Roles at the end", "", "Classes: class:raiders/dealt/healed.", "",
            table(["Role", "Raiders", "Dealt boss", "Dealt adds", "Took", "Healed", "Overheal", "Deaths", "CC", "Interrupts",
                   "Power-outs", "Classes"], rows)]


def timeline(attempt) -> list[str]:
    """The samples with the deaths, mechanic fires and target changes between them, by second into the pull (at the same
    second: the sample first, as it was taken at the start of that second's pulse, then mechanics, threat, deaths)."""
    if not (attempt.samples or attempt.mechanics or attempt.threats):
        return ["", "## Timeline", "", "No RAID_FIGHT_SAMPLE rows for this attempt (an older build, or a budget drop)."]
    events = [(count(f.get("t")) or 0, 0, sample_text(f)) for _, f in attempt.samples]
    events += [(count(f.get("t")) or 0, 1, mechanic_text(f)) for _, f in attempt.mechanics]
    events += [(count(f.get("t")) or 0, 2, threat_text(f)) for _, f in attempt.threats]
    events += [(count(d.get("t")) or 0, 3, death_text(d)) for _, d in attempt.deaths]
    events.sort(key=lambda e: (e[0], e[1]))
    gaps = [b - a for a, b in zip(sorted(count(f.get("t")) or 0 for _, f in attempt.samples)[:-1],
                                  sorted(count(f.get("t")) or 0 for _, f in attempt.samples)[1:])]
    head = f"{len(attempt.samples)} samples, {len(attempt.deaths)} deaths"
    if attempt.mechanics:
        head += f", {len(attempt.mechanics)} mechanic fires"
    if attempt.threats:
        head += f", {len(attempt.threats)} target changes"
    out = ["", f"## Timeline ({head})", ""]
    if any(g > 20 for g in gaps):
        out.append(f"_Gaps between samples: up to {max(gaps)} s (a budget drop or a slow pulse)._")
        out.append("")
    out += [f"- {mmss(t)} {text}" for t, _, text in events]
    return out


def listing(found) -> str:
    if not found:
        return "No RAID_ATTEMPT rows in these logs.\n"
    rows = [[a.key, a.pulled_at or "-", a.boss, a.pull.get("size", "-"), a.outcome, mmss(a.secs), len(a.deaths), len(a.samples)]
            for a in found]
    return table(["Attempt", "Pulled", "Boss", "Size", "Outcome", "Time", "Deaths", "Samples"], rows) + "\n"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("paths", nargs="+", metavar="ATTEMPT LOGS",
                    help="the attempt id (e.g. dungeon-albion-krondon.3; none with --list), then log files or folders")
    ap.add_argument("--list", action="store_true", help="list the attempts in the logs")
    ap.add_argument("--check", action="store_true", help="check the raid rows (fields, pull, end, death counts); exit 1 on a problem")
    ap.add_argument("--run", type=int, help="which pull of an id seen more than once (1, 2, ...; default the last)")
    ap.add_argument("--out", type=Path, help="write the markdown here instead of printing it")
    ap.add_argument("--github", action="store_true", help="with --check: problems as GitHub ::error lines")
    args = ap.parse_args(argv)
    if args.list or args.check:
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
    if args.check:
        problems = check(lines)
        if not found:
            problems.append("no raid attempt in these logs")
        print(listing(found), end="")
        for p in problems:
            print(f"::error title=Raid rows::{p}" if args.github else f"problem: {p}")
        print(f"{len(found)} attempts, {len(problems)} problems.")
        return 1 if problems else 0
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
