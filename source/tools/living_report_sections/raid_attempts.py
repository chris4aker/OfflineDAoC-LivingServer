"""RAID_ATTEMPT / RAID_ATTEMPT_END / RAID_DEATH (#644 part 6; rows from part 1, docs/RAID-ATTEMPT-ROWS.md): each boss's
pulls and how they ended, time to kill, who died to what (by role, kind and source), the top killers and mechanics, died
against left, and how long the raid took from a failed attempt to its next pull. No names: the rows carry none for
raiders; monster names are written. One attempt in detail: source/tools/raid_postmortem.py <attempt>."""
from __future__ import annotations

from collections import Counter, defaultdict

from living_report_sections._raid import FAILED, ROWS as RAID_ROWS, attempts, count, killer_of, mechanics_of
from living_report_sections._util import median, pct, table

TITLE = "Raid attempts"
ROWS = list(RAID_ROWS)
ORDER = 45
TOP = 10


def _med(values, fmt="{:.0f}"):
    m = median([v for v in values if v is not None])
    return "-" if m is None else fmt.format(m)


def _n(n, word):
    return f"{n} {word}" + ("" if n == 1 else "s")


def _mmss(seconds):
    if seconds is None:
        return "-"
    seconds = int(round(seconds))
    return f"{seconds // 60}:{seconds % 60:02d}"


def summarize(lines) -> str:
    found = attempts(lines)
    if not found:
        return "_No RAID_ATTEMPT, RAID_ATTEMPT_END or RAID_DEATH rows._"
    deaths = [d for a in found for _, d in a.deaths]
    ended = [a for a in found if a.end]
    kills = [a for a in found if a.outcome == "killed"]
    out = [f"{_n(len(found), 'attempt')} ({len(ended)} ended, {_n(len(kills), 'kill')}), {_n(len(deaths), 'raider death')}."]
    open_ = [a.key for a in found if not a.end]
    if open_:
        out.append(f"No end row (still live when the log stops, or the server stopped): {', '.join(open_[:TOP])}"
                   + (f" and {len(open_) - TOP} more" if len(open_) > TOP else "") + ".")
    no_pull = [a.key for a in found if not a.pull]
    if no_pull:
        out.append(f"No pull row (the log starts mid-attempt): {', '.join(no_pull[:TOP])}.")

    # ---- per boss
    by_boss = defaultdict(list)
    for a in found:
        by_boss[(a.event, a.boss)].append(a)
    rows = []
    for (event, boss), group in sorted(by_boss.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        outcomes = Counter(a.outcome for a in group)
        won = [a for a in group if a.outcome == "killed"]
        lost = [a for a in group if a.outcome in FAILED]
        rows.append([
            event, boss, len(group),
            ", ".join(f"{o} {n}" for o, n in outcomes.most_common()),
            pct(len(won), len(group)),
            _med([count(a.pull.get("size")) for a in group]),
            _med([count(a.pull.get("sizePct")) for a in group], "{:.0f}%"),
            _mmss(median([a.secs for a in won if a.secs is not None])),
            _mmss(median([a.secs for a in lost if a.secs is not None])),
            _med([count(a.end.get("lowest")) for a in lost], "{:.0f}%"),
            _med([len(a.deaths) for a in group]),
        ])
    out += ["", table(["Event", "Boss", "Pulls", "Outcomes", "Kills", "Median size", "Size vs period", "Time to kill",
                       "Time to fail", "Boss lowest (fails)", "Deaths a pull"], rows)]

    # ---- deaths
    if deaths:
        out += ["", "Deaths by role and by kind (share of all raider deaths):", ""]
        roles = Counter(d.get("role", "-") for d in deaths)
        kinds = Counter(d.get("kind", "-") for d in deaths)
        sources = Counter(d.get("source", "-") for d in deaths)
        out.append(table(["Role", "Deaths", "Share"], [[r, n, pct(n, len(deaths))] for r, n in roles.most_common()]))
        out += ["", table(["Kind", "Deaths", "Share"], [[k, n, pct(n, len(deaths))] for k, n in kinds.most_common()])]
        out += ["", "By the last hit: " + ", ".join(f"{s} {n} ({pct(n, len(deaths))})" for s, n in sources.most_common()) + "."]
        killers = Counter(killer_of(d) for d in deaths)
        out += ["", f"Top killers (of {len(deaths)} deaths):", "",
                table(["Killer", "Deaths", "Share"], [[k, n, pct(n, len(deaths))] for k, n in killers.most_common(TOP)])]
        mech = Counter(m for d in deaths for m in mechanics_of(d))
        with_mech = sum(1 for d in deaths if mechanics_of(d))
        if mech:
            out += ["", f"Mechanics in force at a death ({with_mech} deaths, {pct(with_mech, len(deaths))}; one death can "
                        "carry several):", "",
                    table(["Mechanic", "Deaths"], [[m, n] for m, n in mech.most_common(TOP)])]
        else:
            out += ["", "No death had a boss mechanic in force (only bosses whose scripts raise signals have any)."]
        no_healer = sum(1 for d in deaths if count(d.get("healersIn")) == 0)
        out += ["", f"Deaths with no living healer within range: {no_healer} ({pct(no_healer, len(deaths))})."]

    # ---- died against left
    died = [count(a.end.get("died")) for a in ended]
    left = [count(a.end.get("left")) for a in ended]
    known = [(d, l) for d, l in zip(died, left) if d is not None and l is not None]
    if known:
        out += ["", f"Fallen raiders who really died against those who only left the region or walked back (ended "
                    f"attempts with both counts, {len(known)}): died {sum(d for d, _ in known)}, left "
                    f"{sum(l for _, l in known)}."]

    # ---- wipe to next pull
    retries = [a for a in found if a.pull.get("prev")]
    if retries:
        waits = [count(a.pull.get("wipeToPullS")) for a in retries]
        known_waits = [w for w in waits if w is not None]
        out += ["", f"Pulls after a failed attempt: {len(retries)}. From the failed end to the next pull: median "
                    f"{_mmss(median(known_waits))}, longest {_mmss(max(known_waits)) if known_waits else '-'}."]
    gathers = [count(a.pull.get("gatherS")) for a in found if a.pull.get("gatherS") is not None]
    gathers = [g for g in gathers if g is not None]
    if gathers:
        out.append(f"Muster or regroup before a pull ({_n(len(gathers), 'pull')}): median {_mmss(median(gathers))}, "
                   f"longest {_mmss(max(gathers))}.")
    return "\n".join(out)
