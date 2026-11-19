"""Turn a Living Server log into an AI_PROFILE soak report: a top-ten table of AI systems and a timeline of the
game-loop tick (p99 over time), with the audit's load-test gates (docs/PERFORMANCE.md).

The server writes one AI_PROFILE line a minute to logs/server.log, and one row per AI system a minute to
logs/ai-profile.csv. Give this tool the log (or several, in order); it finds ai-profile.csv beside the first one.
Standard library only; it reads the files and writes nothing unless asked to.

    python tools/perf/ai_profile_report.py runtime/server/logs/server.log
    python tools/perf/ai_profile_report.py server.log --skip-minutes 20 --gate L300 --out L300-report.md
    python tools/perf/ai_profile_report.py server.log --timeline-csv timeline.csv

Exit code: 0, or 1 when --gate was given and the run failed it, or 2 when the log has no AI_PROFILE line.
"""
import argparse
import csv
import math
import re
import statistics
import sys
from dataclasses import dataclass, field
from pathlib import Path

# The audit's load-test pass criteria (technical audit 2026-11-03, section 8.4). Only the tick numbers and the stalls
# are in the log; heap, handles and database growth are checked by hand (see docs/PERFORMANCE.md).
GATES = {
    "L300": {"p99": 100.0, "max": 500.0, "stall_ms": 1000.0},
    "L600": {"p99": 150.0, "max": None, "stall_ms": 1000.0},
    "L900": {"p99": 200.0, "max": None, "stall_ms": 1000.0},
}

LINE = re.compile(r"\bAI_PROFILE (?=tick_p50=)(?P<body>.*)$")
STALL = re.compile(r"\bSERVER_STALL worstTickMs=(?P<ms>[0-9.]+)")
PREFIX_TIME = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d)\b")
TOKEN = re.compile(r"(\w+)=(\S*)")


@dataclass
class Minute:
    """One AI_PROFILE line."""
    when: str
    values: dict
    top: list = field(default_factory=list)  # (system, calls, self_ms)

    def get(self, key, default=0.0):
        try:
            return float(self.values.get(key, default))
        except ValueError:
            return default


def parse_top(text):
    """'think.npc:12000/850.3,lock.raid:3/0.4' -> [('think.npc', 12000, 850.3), ...]; 'none' -> []."""
    out = []
    if not text or text == "none":
        return out
    for item in text.split(","):
        name, _, rest = item.rpartition(":")
        calls, _, ms = rest.partition("/")
        try:
            out.append((name, int(calls), float(ms)))
        except ValueError:
            continue
    return out


def parse_line(line):
    """The AI_PROFILE fields of one log line, or None. The NLog prefix (date | level | logger |) is optional."""
    match = LINE.search(line)
    if not match:
        return None
    values = dict(TOKEN.findall(match.group("body")))
    when = values.get("utc")
    if not when:
        stamp = PREFIX_TIME.match(line)
        when = stamp.group("stamp") if stamp else ""
    return Minute(when, values, parse_top(values.get("top", "")))


def read_logs(paths):
    minutes, stalls = [], []
    for path in paths:
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line in handle:
                minute = parse_line(line)
                if minute:
                    minutes.append(minute)
                    continue
                stall = STALL.search(line)
                if stall:
                    # Remember how many minutes came before it, so --skip-minutes can leave it out too.
                    stalls.append((len(minutes), float(stall.group("ms"))))
    return minutes, stalls


def read_csv(path, keep_utc=None):
    """Rows of ai-profile.csv, optionally only those whose utc is in keep_utc. Repeated header lines are skipped."""
    rows = []
    with open(path, encoding="utf-8", errors="replace", newline="") as handle:
        for row in csv.DictReader(handle):
            if row.get("utc") in (None, "utc"):
                continue
            if keep_utc is not None and row["utc"] not in keep_utc:
                continue
            rows.append(row)
    return rows


def top_from_csv(rows, minutes_count):
    """Per system: average calls and ms per minute, longest call, share of all self time."""
    totals = {}
    for row in rows:
        entry = totals.setdefault(row["system"], {"calls": 0, "self": 0.0, "total": 0.0, "max": 0.0})
        entry["calls"] += int(row["calls"])
        entry["self"] += float(row["self_ms"])
        entry["total"] += float(row["total_ms"])
        entry["max"] = max(entry["max"], float(row["max_ms"]))
    return summarize_totals(totals, minutes_count, exact=True)


def top_from_lines(minutes):
    """The same from the top= lists of the log lines: only each minute's ten biggest systems are there, so a system
    that was rarely in the top ten is under-counted."""
    totals = {}
    for minute in minutes:
        for name, calls, self_ms in minute.top:
            entry = totals.setdefault(name, {"calls": 0, "self": 0.0, "total": None, "max": None})
            entry["calls"] += calls
            entry["self"] += self_ms
    return summarize_totals(totals, len(minutes), exact=False)


def summarize_totals(totals, minutes_count, exact):
    all_self = sum(entry["self"] for entry in totals.values()) or 1.0
    per_minute = max(1, minutes_count)
    table = []
    for name, entry in totals.items():
        table.append({
            "system": name,
            "calls_per_min": entry["calls"] / per_minute,
            "self_ms_per_min": entry["self"] / per_minute,
            "total_ms_per_min": None if entry["total"] is None else entry["total"] / per_minute,
            "max_ms": entry["max"],
            "share_pct": entry["self"] * 100.0 / all_self,
        })
    table.sort(key=lambda row: row["self_ms_per_min"], reverse=True)
    return table, exact


def bucket(minutes, every):
    """Groups of `every` minutes: the worst p99 and max, the average of the rest."""
    for start in range(0, len(minutes), every):
        group = minutes[start:start + every]
        yield {
            "when": group[0].when,
            "minutes": len(group),
            "ticks": sum(int(m.get("ticks")) for m in group),
            "p50": statistics.fmean(m.get("tick_p50") for m in group),
            "p95": max(m.get("p95") for m in group),
            "p99": max(m.get("p99") for m in group),
            "max": max(m.get("max") for m in group),
            "serial_ms": statistics.fmean(m.get("serial_ms") for m in group),
            "parallel_ms": statistics.fmean(m.get("parallel_ms") for m in group),
            "locks_ms": statistics.fmean(m.get("locks_ms") for m in group),
            "over_budget": sum(1 for m in group if m.values.get("over_budget")),
        }


def bar(value, scale, width=30):
    if scale <= 0:
        return ""
    return "#" * max(0, min(width, int(round(value / scale * width))))


def gate_verdict(name, minutes, stalls):
    gate = GATES[name]
    worst_p99 = max(m.get("p99") for m in minutes)
    worst_max = max(m.get("max") for m in minutes)
    long_stalls = [ms for ms in stalls if ms > gate["stall_ms"]]
    checks = [(f"p99 tick < {gate['p99']:g} ms in every minute", worst_p99 < gate["p99"],
               f"worst minute p99 {worst_p99:.1f} ms; "
               f"{sum(1 for m in minutes if m.get('p99') >= gate['p99'])} of {len(minutes)} minutes over")]
    if gate["max"] is not None:
        checks.append((f"max tick < {gate['max']:g} ms", worst_max < gate["max"], f"worst tick {worst_max:.1f} ms"))
    checks.append((f"no SERVER_STALL over {gate['stall_ms']:g} ms", not long_stalls,
                   f"{len(long_stalls)} stall minute(s) over, worst {max(stalls) if stalls else 0:.0f} ms"))
    return checks


def report(minutes, stalls, top, exact, every, gate, sources):
    out = []
    first, last = minutes[0].when, minutes[-1].when
    p99s = [m.get("p99") for m in minutes]
    out.append("# AI_PROFILE soak report")
    out.append("")
    out.append(f"- Log: {', '.join(sources)}")
    out.append(f"- Minutes: {len(minutes)} ({first} to {last}), ticks: {sum(int(m.get('ticks')) for m in minutes):,}")
    out.append(f"- Tick p99 per minute: median {statistics.median(p99s):.1f} ms, worst {max(p99s):.1f} ms; "
               f"worst single tick {max(m.get('max') for m in minutes):.1f} ms")
    out.append(f"- Per tick on average: serial planning {statistics.fmean(m.get('serial_ms') for m in minutes):.2f} ms, "
               f"brains {statistics.fmean(m.get('parallel_ms') for m in minutes):.2f} ms, "
               f"other services {statistics.fmean(m.get('other_ms') for m in minutes):.2f} ms")
    out.append(f"- Lock waits: {statistics.fmean(m.get('locks_ms') for m in minutes):.1f} ms a minute on average")
    out.append(f"- Minutes over the serial budget (5 ms a tick, 2 ms a pulse): "
               f"{sum(1 for m in minutes if m.values.get('over_budget'))}")
    out.append(f"- SERVER_STALL minutes: {len(stalls)}" + (f", worst {max(stalls):.0f} ms" if stalls else ""))
    out.append("")

    if gate:
        out.append(f"## Gate {gate}")
        out.append("")
        passed = True
        for label, ok, detail in gate_verdict(gate, minutes, stalls):
            passed &= ok
            out.append(f"- {'PASS' if ok else 'FAIL'}: {label} ({detail})")
        out.append("")
        out.append("Heap, handles, database growth and what the bots looked like are checked by hand "
                   "(docs/PERFORMANCE.md).")
        out.append(f"**Result: {'PASS' if passed else 'FAIL'}** on the logged criteria.")
        out.append("")
    else:
        passed = True

    out.append("## Top ten AI systems (self time, all threads)")
    out.append("")
    if not exact:
        out.append("From the log lines' top= lists only (no ai-profile.csv found): each minute names its ten biggest "
                   "systems, so rarer ones are under-counted.")
        out.append("")
    out.append("| # | System | Calls/min | Self ms/min | Total ms/min | Longest ms | Share |")
    out.append("|---|---|---:|---:|---:|---:|---:|")
    for rank, row in enumerate(top[:10], 1):
        total = "-" if row["total_ms_per_min"] is None else f"{row['total_ms_per_min']:.1f}"
        longest = "-" if row["max_ms"] is None else f"{row['max_ms']:.2f}"
        out.append(f"| {rank} | {row['system']} | {row['calls_per_min']:,.0f} | {row['self_ms_per_min']:.1f} | "
                   f"{total} | {longest} | {row['share_pct']:.1f}% |")
    out.append("")
    out.append("Self time leaves out the scopes nested inside a system, so the rows add up; total time includes them.")
    out.append("")

    out.append(f"## Timeline (tick ms; {every} minute(s) a row: worst p95/p99/max, average of the rest)")
    out.append("")
    rows = list(bucket(minutes, every))
    scale = max(row["p99"] for row in rows)
    out.append("| From | p50 | p95 | p99 | max | serial | brains | locks ms | p99 |")
    out.append("|---|---:|---:|---:|---:|---:|---:|---:|---|")
    for row in rows:
        flag = " *" if row["over_budget"] else ""
        out.append(f"| {row['when']} | {row['p50']:.1f} | {row['p95']:.1f} | {row['p99']:.1f} | {row['max']:.1f} | "
                   f"{row['serial_ms']:.2f}{flag} | {row['parallel_ms']:.2f} | {row['locks_ms']:.1f} | "
                   f"`{bar(row['p99'], scale)}` |")
    out.append("")
    out.append("`*` = a minute in the row was over the serial budget (see its over_budget field in the log).")
    return "\n".join(out) + "\n", passed


def write_timeline_csv(path, minutes):
    with open(path, "w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        keys = ["utc", "ticks", "tick_p50", "p95", "p99", "max", "tick_avg", "serial_ms", "serial_p99", "parallel_ms",
                "parallel_p99", "other_ms", "locks_ms", "over_budget"]
        writer.writerow(keys)
        for minute in minutes:
            writer.writerow([minute.when if key == "utc" else minute.values.get(key, "") for key in keys])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("logs", nargs="+", type=Path, help="server.log files, oldest first")
    parser.add_argument("--csv", type=Path, help="ai-profile.csv (default: the one beside the first log)")
    parser.add_argument("--skip-minutes", type=int, default=0, help="leave out the first N minutes (startup ramp)")
    parser.add_argument("--every", type=int, default=0, help="minutes per timeline row (default: about 60 rows)")
    parser.add_argument("--gate", choices=sorted(GATES), help="check the audit's pass criteria for this run")
    parser.add_argument("--out", type=Path, help="write the report here (Markdown) instead of printing it")
    parser.add_argument("--timeline-csv", type=Path, help="also write the per-minute timeline as CSV")
    args = parser.parse_args(argv)

    minutes, stalls = read_logs(args.logs)
    skip = max(0, args.skip_minutes)
    minutes = minutes[skip:]
    stalls = [ms for before, ms in stalls if before >= skip]
    if not minutes:
        print("No AI_PROFILE lines found (is the server property ai_profile on?)", file=sys.stderr)
        return 2

    csv_path = args.csv or args.logs[0].with_name("ai-profile.csv")
    if csv_path.exists():
        keep = {m.when for m in minutes} if args.skip_minutes else None
        top, exact = top_from_csv(read_csv(csv_path, keep), len(minutes))
    else:
        top, exact = top_from_lines(minutes)

    every = args.every or max(1, math.ceil(len(minutes) / 60))
    text, passed = report(minutes, stalls, top, exact, every, args.gate, [str(p) for p in args.logs])
    if args.out:
        args.out.write_text(text, encoding="utf-8")
        print(f"Wrote {args.out}")
    else:
        sys.stdout.write(text)
    if args.timeline_csv:
        write_timeline_csv(args.timeline_csv, minutes)
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
