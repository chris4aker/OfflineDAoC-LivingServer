"""BG_POPULATION (#625 row 1): battleground headcounts every five minutes, against #610's goal shares.

Row: ``BG_POPULATION t= bg= bracket= realm= bots= bgGoal= passing= travelling= assigned= eligible= expected= companions= player=``
"""
import re

TITLE = "Battleground population (#610)"
ROWS = ["BG_POPULATION"]

_FIELD = re.compile(r'(\w+)=("[^"]*"|\S+)')
LOW = 0.75  # assigned below 75% of the goal shares' headcount is flagged


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


def summarize(lines) -> str:
    groups = {}
    for row in _rows(lines, "BG_POPULATION"):
        groups.setdefault((row.get("bg", "?"), row.get("bracket", "?"), row.get("realm", "?")), []).append(row)
    if not groups:
        return "No BG_POPULATION rows."
    out = ["| Battleground | Realm | Samples | Bots in | Battleground goal | Passing | Assigned | Expected | Assigned/expected | "
           "Companions | Player present |",
           "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
    flagged = []
    for (bg, bracket, realm), rows in sorted(groups.items()):
        n = len(rows)

        def avg(key):
            return sum(_int(r, key) for r in rows) / n

        assigned, expected = avg("assigned"), avg("expected")
        ratio = assigned / expected if expected > 0 else None
        present = sum(1 for r in rows if _int(r, "player") > 0)
        out.append(f"| {bg} ({bracket}) | {realm} | {n} | {avg('bots'):.1f} | {avg('bgGoal'):.1f} | {avg('passing'):.1f} | "
                   f"{assigned:.1f} | {expected:.1f} | {'-' if ratio is None else f'{ratio:.0%}'} | {avg('companions'):.1f} | "
                   f"{present}/{n} |")
        if ratio is not None and ratio < LOW:
            flagged.append(f"{bg} {realm} ({ratio:.0%})")
    if flagged:
        out.append("")
        out.append(f"Below {LOW:.0%} of the goal shares' headcount: " + ", ".join(flagged) + ".")
    return "\n".join(out)
