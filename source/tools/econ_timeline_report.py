"""Summarize the Crafting 2.0 economy lines (CRAFT_*, ECON_*) of a server log or of an economy rehearsal's ledger log.

The server writes them (WP1-WP8, docs/CRAFTING-2.0-BUILD-PLAN.md 2.5) as `TAG key=value key="text" ...`; the
rehearsal (source/server/Tests/Rehearsal, WP10) writes the same lines into `<scenario>-ledger.log`. This tool reads
either, or several in order, and prints a markdown report: the tags seen, the money supply per realm over time and
whether the flows explain it (the residual), the flows by kind, crafting sessions, titles and firsts, the rehearsal's
gates, and optionally a direction check against a persona_sim.py result (living-world/trade-sim). Fork tags kept as
aliases (AUTONOMOUS_CRAFT_CATALOG, AUTONOMOUS_EXCHANGE_SOLD, BOT_MONEY_LEDGER_165) are counted too.
Standard library only; it writes nothing unless asked to.

    python source/tools/econ_timeline_report.py runtime/server/logs/server.log
    python source/tools/econ_timeline_report.py out/fresh-7pct-ledger.log --out fresh-report.md --check
    python source/tools/econ_timeline_report.py out/aged-legacy-12pct-ledger.log \\
        --persona tools/living-world-sim/results/persona_package_120h.json --persona-case "package, bots 2x"

Exit code: 0; 1 with --check when a supply row's residual is not 0 or a rehearsal gate failed; 2 when no line was found.
Results from a rehearsal are tests, not in-game results; a report says which it read.
"""
import argparse
import csv
import json
import re
import statistics
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

# EconomyLog.AllTags (source/server/GameServer/craft/economy/EconomyLog.cs) and the fork aliases.
CRAFT_TAGS = ["CRAFT_RECIPE_AUDIT", "CRAFT_RECIPE_BROKEN", "CRAFT_WORLD_AUDIT", "CRAFT_ERA_JOIN", "CRAFT_PLAYER_TITLE",
              "CRAFT_ENROLL", "CRAFT_SESSION_START", "CRAFT_MADE", "CRAFT_SESSION_END", "CRAFT_TITLE", "CRAFT_FIRST",
              "CRAFT_EXPLOSION", "CRAFT_STORY"]
ECON_TAGS = ["ECON_SUPPLY", "ECON_FLOWS", "ECON_ORDER", "ECON_COMMISSION", "ECON_LIST", "ECON_SALE", "ECON_RECLAIM",
             "ECON_INDEX", "ECON_GEAR_CHECK", "ECON_TEMPLATE_BUY", "ECON_UNDRESS_GUARD", "ECON_WEAR", "ECON_REPAIR",
             "ECON_CONSUMABLE", "ECON_LUXURY", "ECON_SIEGE_SUPPLY", "ECON_GOVERNOR", "ECON_PLAYER_ORDER",
             "ECON_PLAYER_DELIVERY", "ECON_PLAYER_SELL", "ECON_PLAYER_TRADE", "ECON_SC_JOB", "ECON_HAGGLE", "ECON_GUILD_SPEND", "ECON_REHEARSAL"]
ALIASES = ["AUTONOMOUS_CRAFT_CATALOG", "AUTONOMOUS_EXCHANGE_SOLD", "BOT_MONEY_LEDGER_165"]
KNOWN = set(CRAFT_TAGS + ECON_TAGS + ALIASES)

LINE = re.compile(r"(?:^|[\s|])(?P<tag>(?:CRAFT|ECON)_[A-Z0-9_]+|AUTONOMOUS_CRAFT_CATALOG|AUTONOMOUS_EXCHANGE_SOLD|BOT_MONEY_LEDGER_165)"
                  r"(?P<body>(?:\s+\w[\w.]*=(?:\"[^\"]*\"|\S*))*)\s*$")
TOKEN = re.compile(r"(\w[\w.]*)=(\"[^\"]*\"|\S*)")
PREFIX_TIME = re.compile(r"^(?P<stamp>\d{4}-\d\d-\d\d[ T]\d\d:\d\d(?::\d\d)?)")
COPPER_PER_GOLD = 10_000


@dataclass
class Line:
    tag: str
    values: dict
    when: str = ""
    source: str = ""

    def text(self, key, default=""):
        return self.values.get(key, default)

    def number(self, *keys, default=0.0):
        for key in keys:
            if key in self.values:
                try:
                    return float(self.values[key])
                except ValueError:
                    pass
        return default


def parse_line(text, source=""):
    """The tag and key=value pairs of one log line (quoted values unquoted), or None. Any log prefix is allowed."""
    match = LINE.search(text.rstrip("\r\n"))
    if not match:
        return None
    values = {key: value[1:-1] if len(value) >= 2 and value[0] == value[-1] == '"' else value
              for key, value in TOKEN.findall(match.group("body"))}
    when = values.get("hour") or values.get("utc") or ""
    if not when:
        stamp = PREFIX_TIME.match(text)
        when = stamp.group("stamp") if stamp else ""
    return Line(match.group("tag"), values, when, source)


def read_lines(paths):
    lines = []
    for path in paths:
        with open(path, encoding="utf-8", errors="replace") as handle:
            for text in handle:
                parsed = parse_line(text, str(path))
                if parsed:
                    lines.append(parsed)
    return lines


@dataclass
class Report:
    lines: list
    tags: dict = field(default_factory=dict)
    supply: dict = field(default_factory=dict)       # realm -> [Line]
    flows: dict = field(default_factory=dict)        # realm -> kind -> [in, out, count, transfer]
    gates: list = field(default_factory=list)
    summaries: list = field(default_factory=list)
    rehearsal: bool = False

    @property
    def residual_rows(self):
        return [line for rows in self.supply.values() for line in rows if line.number("residualCopper", "residual") != 0]

    @property
    def failed_gates(self):
        return [line for line in self.gates if line.text("status").lower() == "fail"]


def analyse(lines):
    report = Report(lines)
    counts = defaultdict(int)
    supply = defaultdict(list)
    flows = defaultdict(lambda: defaultdict(lambda: [0, 0, 0, False]))
    for line in lines:
        counts[line.tag] += 1
        if line.text("source") == "rehearsal" or line.tag == "ECON_REHEARSAL":
            report.rehearsal = True
        if line.tag == "ECON_SUPPLY":
            supply[line.text("realm", "?")].append(line)
        elif line.tag == "ECON_FLOWS":
            entry = flows[line.text("realm", "?")][line.text("kind", "?")]
            entry[0] += int(line.number("inCopper", "in"))
            entry[1] += int(line.number("outCopper", "out"))
            entry[2] += int(line.number("count"))
            entry[3] = entry[3] or line.text("transfer") in ("1", "True", "true")
        elif line.tag == "ECON_REHEARSAL":
            (report.summaries if line.text("summary") == "1" else report.gates).append(line)
    report.tags = dict(sorted(counts.items()))
    report.supply = {realm: rows for realm, rows in sorted(supply.items())}
    report.flows = {realm: dict(sorted(kinds.items())) for realm, kinds in sorted(flows.items())}
    return report


def gold(copper):
    return f"{copper / COPPER_PER_GOLD:,.1f}"


# ---------------- persona_sim direction check ----------------

def persona_directions(results, case=None):
    """Direction metrics of a persona_sim.py result (mean over seeds) for a case (first whose name starts with it)."""
    names = list(results)
    if case:
        names = [n for n in names if n.startswith(case)] or [n for n in names if case in n]
    if not names:
        raise KeyError(f"no persona case matching {case!r}; cases: {', '.join(results)}")
    name = names[0]
    runs = results[name]

    def mean(values):
        values = [v for v in values if v is not None]
        return statistics.mean(values) if values else None

    def ratio(run):
        w = run.get("wealth") or {}
        crafters = w.get("crafters_median", run.get("gold_crafter_players"))
        others = w.get("others_median", run.get("gold_other_players"))
        return crafters / others if crafters is not None and others else None

    def hunting_share(run):
        gold_in = run.get("gold_in") or {}
        total = sum(gold_in.values())
        return gold_in.get("hunting", 0) / total if total else None

    def growth(run):
        snaps = run.get("snaps") or []
        if len(snaps) < 2 or not snaps[1].get("gold_total"):
            return None
        return snaps[-1]["gold_total"] / snaps[1]["gold_total"] - 1

    def filled(run):
        stats = run.get("stats") or {}
        placed = stats.get("orders_placed", 0)
        return stats.get("orders_filled", 0) / placed if placed else None

    return name, {
        "crafterToHunterWealth": mean(ratio(r) for r in runs),
        "huntingShareOfFaucets": mean(hunting_share(r) for r in runs),
        "supplyGrowth": mean(growth(r) for r in runs),
        "ordersFilledShare": mean(filled(r) for r in runs),
    }


def our_directions(report):
    """The same metrics from the lines: the rehearsal's summary when present, else the ECON_SUPPLY/FLOWS rows."""
    out = {"crafterToHunterWealth": None, "huntingShareOfFaucets": None, "supplyGrowth": None, "ordersFilledShare": None}
    summary = report.summaries[-1] if report.summaries else None
    if summary:
        crafters, hunters = summary.number("wealthCrafterMedian"), summary.number("wealthHunterMedian")
        out["crafterToHunterWealth"] = crafters / hunters if hunters else None
        out["huntingShareOfFaucets"] = summary.number("lootCoinShareOfFaucets", default=None)
        start, end = summary.number("supplyStartCopper"), summary.number("supplyEndCopper")
        out["supplyGrowth"] = end / start - 1 if start else None
        posted = summary.number("ordersPosted")
        out["ordersFilledShare"] = summary.number("ordersCollected") / posted if posted else None
        return out
    starts = [rows[0].number("botCoin", "supplyCopper") - rows[0].number("deltaCopper") for rows in report.supply.values() if rows]
    ends = [rows[-1].number("botCoin", "supplyCopper") for rows in report.supply.values() if rows]
    if starts and sum(starts):
        out["supplyGrowth"] = sum(ends) / sum(starts) - 1
    faucets = sum(e[0] for kinds in report.flows.values() for e in kinds.values() if not e[3])
    loot = sum(kinds.get("LootCoin", [0])[0] for kinds in report.flows.values())
    out["huntingShareOfFaucets"] = loot / faucets if faucets else None
    return out


def direction_rows(ours, theirs):
    """(metric, ours, theirs, verdict): agree when both sit on the same side of the metric's pivot."""
    pivots = {"crafterToHunterWealth": 1.0, "huntingShareOfFaucets": 0.5, "supplyGrowth": 0.0, "ordersFilledShare": 0.5}
    rows = []
    for key, pivot in pivots.items():
        a, b = ours.get(key), theirs.get(key)
        if a is None or b is None:
            verdict = "n/a"
        else:
            verdict = "agree" if (a > pivot) == (b > pivot) else "DIFFER"
        rows.append((key, a, b, verdict))
    return rows


# ---------------- output ----------------

def fmt(value, digits=3):
    return "" if value is None else f"{value:.{digits}f}"


def markdown(report, sources, persona=None):
    out = []
    kind = "an economy rehearsal (tests only, not an in-game result)" if report.rehearsal else "a server log"
    out.append("# Economy timeline report")
    out.append("")
    out.append(f"Read {len(report.lines):,} CRAFT_/ECON_ lines from {', '.join(str(s) for s in sources)}; this is {kind}.")
    if report.summaries:
        build = report.summaries[-1].text("build")
        out.append(f"Rehearsal scenario `{report.summaries[-1].text('scenario')}`, build {build}.")
    out.append("")
    out.append("## Tags")
    out.append("")
    unknown = [tag for tag in report.tags if tag not in KNOWN]
    out.append(", ".join(f"{tag} {count:,}" for tag, count in report.tags.items()) or "none")
    if unknown:
        out.append("")
        out.append("Unknown tags (not in EconomyLog.AllTags): " + ", ".join(unknown))
    out.append("")

    out.append("## Money supply")
    out.append("")
    if not report.supply:
        out.append("No ECON_SUPPLY line (the ledger, WP2, is not in this log).")
    else:
        out.append("| Realm | Hours | First hour | Last hour | Supply at start | at end (gold) | Change | Rows with residual | Largest residual |")
        out.append("|---|---|---|---|---|---|---|---|---|")
        for realm, rows in report.supply.items():
            first, last = rows[0], rows[-1]
            start = first.number("botCoin", "supplyCopper") - first.number("deltaCopper")
            end = last.number("botCoin", "supplyCopper")
            residuals = [abs(r.number("residualCopper", "residual")) for r in rows]
            out.append(f"| {realm} | {len(rows)} | {first.when} | {last.when} | {gold(start)} | {gold(end)} | {gold(end - start)} | "
                       f"{sum(1 for r in residuals if r)} | {gold(max(residuals))} |")
    out.append("")

    out.append("## Flows by kind")
    out.append("")
    if not report.flows:
        out.append("No ECON_FLOWS line.")
    else:
        out.append("| Realm | Kind | In (gold) | Out (gold) | Net | Moves | Transfer between bots |")
        out.append("|---|---|---|---|---|---|---|")
        for realm, kinds in report.flows.items():
            for name, (inc, outc, count, transfer) in sorted(kinds.items(), key=lambda kv: -(kv[1][0] + kv[1][1])):
                out.append(f"| {realm} | {name} | {gold(inc)} | {gold(outc)} | {gold(inc - outc)} | {count:,} | {'yes' if transfer else ''} |")
    out.append("")

    sessions = [l for l in report.lines if l.tag == "CRAFT_SESSION_END"]
    out.append("## Crafting")
    out.append("")
    if sessions:
        out.append(f"{len(sessions):,} sessions: {int(sum(l.number('crafts') for l in sessions)):,} crafts, "
                   f"{int(sum(l.number('made') for l in sessions)):,} made, {int(sum(l.number('points') for l in sessions)):,} skill points, "
                   f"materials {gold(sum(l.number('spentCopper') for l in sessions))} gold.")
    else:
        out.append("No CRAFT_SESSION_END line.")
    titles = [l for l in report.lines if l.tag in ("CRAFT_TITLE", "CRAFT_PLAYER_TITLE")]
    firsts = [l for l in report.lines if l.tag == "CRAFT_FIRST"]
    out.append(f"Titles: {len(titles):,}. Firsts: " + (", ".join(f"{l.text('realm')} {l.text('title')} at {l.when} ({l.text('bot')})"
                                                          for l in firsts) or "none") + ".")
    for tag in ("CRAFT_RECIPE_AUDIT", "CRAFT_WORLD_AUDIT", "AUTONOMOUS_CRAFT_CATALOG"):
        for line in [l for l in report.lines if l.tag == tag][-3:]:
            out.append(f"- {tag}: " + " ".join(f"{k}={v}" for k, v in line.values.items()))
    out.append("")

    if report.gates or report.summaries:
        out.append("## Rehearsal gates (tests only)")
        out.append("")
        out.append("| Scenario | Gate | Status | Detail |")
        out.append("|---|---|---|---|")
        for line in report.gates:
            out.append(f"| {line.text('scenario')} | {line.text('gate')} | {line.text('status')} | {line.text('detail')} |")
        out.append("")

    if persona:
        name, theirs, ours = persona
        out.append("## Direction against persona_sim.py")
        out.append("")
        out.append(f"persona_sim case `{name}` (a design model on living-world/trade-sim; compare directions, not absolutes).")
        out.append("")
        out.append("| Metric | This log | persona_sim | Direction |")
        out.append("|---|---|---|---|")
        for key, a, b, verdict in direction_rows(ours, theirs):
            out.append(f"| {key} | {fmt(a)} | {fmt(b)} | {verdict} |")
        out.append("")
    return "\n".join(out) + "\n"


def write_timeline_csv(report, path):
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["hour", "realm", "supplyCopper", "deltaCopper", "flowsCopper", "residualCopper", "faucetsCopper", "sinksCopper", "bots"])
        for realm, rows in report.supply.items():
            for r in rows:
                writer.writerow([r.when, realm, int(r.number("botCoin", "supplyCopper")), int(r.number("deltaCopper")),
                                 int(r.number("flowsCopper")), int(r.number("residualCopper", "residual")),
                                 int(r.number("faucetsCopper")), int(r.number("sinksCopper")), int(r.number("bots"))])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("logs", nargs="+", help="server logs or rehearsal <scenario>-ledger.log files, in order")
    parser.add_argument("--out", help="write the markdown report here instead of printing it")
    parser.add_argument("--timeline-csv", help="also write the ECON_SUPPLY rows as CSV")
    parser.add_argument("--check", action="store_true", help="exit 1 when a residual is not 0 or a rehearsal gate failed")
    parser.add_argument("--persona", help="a persona_sim.py results JSON for a direction check")
    parser.add_argument("--persona-case", help="the persona case to compare (a name prefix; default the first)")
    args = parser.parse_args(argv)

    lines = read_lines(args.logs)
    if not lines:
        print("no CRAFT_/ECON_ line found", file=sys.stderr)
        return 2
    report = analyse(lines)
    persona = None
    if args.persona:
        name, theirs = persona_directions(json.loads(Path(args.persona).read_text(encoding="utf-8")), args.persona_case)
        persona = (name, theirs, our_directions(report))
    text = markdown(report, args.logs, persona)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"wrote {args.out}")
    else:
        sys.stdout.write(text)
    if args.timeline_csv:
        write_timeline_csv(report, args.timeline_csv)
    if args.check and (report.residual_rows or report.failed_gates):
        print(f"check failed: {len(report.residual_rows)} supply rows with a residual, {len(report.failed_gates)} failed gates",
              file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
