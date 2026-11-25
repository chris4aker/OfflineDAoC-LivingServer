"""CC_BREAK (#625 row 3): each break of a mez or breakable root (Value >= 99). Checks #621's "mezzes held".

Row: ``CC_BREAK t= cc= target= breaker= breakerPet= caster= side= ms= attack= region= bg=``
"""
import re

TITLE = "Crowd control breaks (#621)"
ROWS = ["CC_BREAK"]

_FIELD = re.compile(r'(\w+)=("[^"]*"|\S+)')
EARLY_MS = 5_000  # an ally break this soon after the crowd control landed is the mistake #621 is about


def _rows(lines, marker):
    for line in lines:
        at = line.find(marker + " ")
        if at < 0 or (at > 0 and line[at - 1] not in " |"):
            continue
        yield {key: value.strip('"') for key, value in _FIELD.findall(line[at + len(marker):])}


def _median(values):
    values = sorted(values)
    if not values:
        return None
    mid = len(values) // 2
    return values[mid] if len(values) % 2 else (values[mid - 1] + values[mid]) / 2


def _int(text):
    try:
        return int(text)
    except (TypeError, ValueError):
        return None


def summarize(lines) -> str:
    rows = list(_rows(lines, "CC_BREAK"))
    if not rows:
        return "No CC_BREAK rows."
    out = ["| Where | CC | Breaker | Side | Breaks | Median ms after landing |", "|---|---|---|---|---:|---:|"]
    groups = {}
    for r in rows:
        where = "battleground" if r.get("bg", "-") != "-" else "elsewhere"
        breaker = r.get("breaker", "?") + (" (pet)" if r.get("breakerPet") == "1" else "")
        groups.setdefault((where, r.get("cc", "?"), breaker, r.get("side", "?")), []).append(r)
    for key, mine in sorted(groups.items(), key=lambda item: (item[0][0], -len(item[1]))):
        ms = _median([v for v in (_int(r.get("ms")) for r in mine) if v is not None])
        out.append(f"| {' | '.join(key)} | {len(mine)} | {'-' if ms is None else f'{ms:.0f}'} |")
    ally = [r for r in rows if r.get("side") == "ally"]
    early = [r for r in ally if (_int(r.get("ms")) or 0) < EARLY_MS]
    bots = [r for r in early if r.get("breaker") in ("gamebot", "companion")]
    out.append("")
    out.append(f"Ally breaks: {len(ally)} of {len(rows)}; within {EARLY_MS // 1000} s of landing: {len(early)} "
               f"({len(bots)} by a gamebot or companion).")
    return "\n".join(out)
