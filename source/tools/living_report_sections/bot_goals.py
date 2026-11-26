"""BOT_GOALS and GOAL_PASS (#669, #670): which Bot Goals settings ran, and what the one-minute goal pass saw and gave.

Rows (source/server/GameServer/bots/autonomous/LivingRows.Goals.cs):
``BOT_GOALS source=file|override|defaults reason=start|upgrade l1_19=S/G/R/B l20_49=S/G/R/B l50=S/G/R/B``
``GOAL_PASS t= realm= band= members= pool= bgEligible= target=S/G/R/B held=S/G/R/B open=S/G/R/B planned=S/G/R/B personalities=``
"""
import re

TITLE = "Bot goals and the goal pass (#669, #670)"
ROWS = ["BOT_GOALS", "GOAL_PASS"]
ORDER = 59

_FIELD = re.compile(r'(\w+)=("[^"]*"|\S+)')
KINDS = ("solo", "group", "RvR", "battleground")


def _rows(lines, marker):
    for line in lines:
        at = line.find(marker + " ")
        if at < 0 or (at > 0 and line[at - 1] not in " |"):
            continue
        yield {key: value.strip('"') for key, value in _FIELD.findall(line[at + len(marker):])}


def _int(row, key):
    try:
        return int(row.get(key, "0"))
    except ValueError:
        return 0


def _four(row, key):
    parts = row.get(key, "").split("/")
    if len(parts) != 4:
        return [0, 0, 0, 0]
    try:
        return [int(p) for p in parts]
    except ValueError:
        return [0, 0, 0, 0]


def _band(value):
    return {"0": "1-19", "20": "20-49", "50": "50"}.get(value, value)


def summarize(lines) -> str:
    out = []
    starts = list(_rows(lines, "BOT_GOALS"))
    if starts:
        out.append("| Source | Reason | 1-19 | 20-49 | 50 |")
        out.append("|---|---|---|---|---|")
        for row in starts:
            out.append(f"| {row.get('source', '?')} | {row.get('reason', '?')} | {row.get('l1_19', '-')} | "
                       f"{row.get('l20_49', '-')} | {row.get('l50', '-')} |")
    else:
        out.append("No BOT_GOALS row (a build before 1.2.3's #669, or the log starts after start-up).")
    groups = {}
    for row in _rows(lines, "GOAL_PASS"):
        groups.setdefault((row.get("realm", "?"), row.get("band", "?")), []).append(row)
    out.append("")
    if not groups:
        out.append("No GOAL_PASS rows.")
        return "\n".join(out)
    out.append("Shares are S/G/R/B (solo, group, RvR, battleground). Open and planned are summed over the sampled passes.")
    out.append("")
    out.append("| Realm | Band | Passes | Avg pool | Avg bg-eligible | Open (sum) | Planned (sum) | Battleground planned / open share |")
    out.append("|---|---|---:|---:|---:|---|---|---|")
    for (realm, band), rows in sorted(groups.items()):
        n = len(rows)
        pool = sum(_int(r, "pool") for r in rows) / n
        eligible = sum(_int(r, "bgEligible") for r in rows) / n
        opened = [sum(_four(r, "open")[k] for r in rows) for k in range(4)]
        planned = [sum(_four(r, "planned")[k] for r in rows) for k in range(4)]
        open_share = opened[3] / sum(opened) if sum(opened) else None
        planned_share = planned[3] / sum(planned) if sum(planned) else None
        share = ("-" if planned_share is None else f"{planned_share:.0%}") + " / " + \
                ("-" if open_share is None else f"{open_share:.0%}")
        out.append(f"| {realm} | {_band(band)} | {n} | {pool:.1f} | {eligible:.1f} | {'/'.join(map(str, opened))} | "
                   f"{'/'.join(map(str, planned))} | {share} |")
    return "\n".join(out)
