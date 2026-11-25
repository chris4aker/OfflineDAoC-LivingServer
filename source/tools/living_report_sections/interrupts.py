"""CAST_INTERRUPT (#625 row 2): sampled casts an attacker's interrupt timer touched, decided at the half-cast point (#596).

Row: ``CAST_INTERRUPT seq= every= t= caster= pet= realm= spell= castMs= leftMs= outcome= why= attacker= attackerPet= region= bg=``
"""
import re

TITLE = "Live interrupt (#596)"
ROWS = ["CAST_INTERRUPT"]

_FIELD = re.compile(r'(\w+)=("[^"]*"|\S+)')
KINDS = ("player", "companion", "gamebot", "npc")


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
    rows = list(_rows(lines, "CAST_INTERRUPT"))
    if not rows:
        return "No CAST_INTERRUPT rows."
    out = ["| Caster | Sampled casts | Lost | Kept, timer ran out | Kept, immune or uninterruptible | Median ms left when lost |",
           "|---|---:|---:|---:|---:|---:|"]
    kinds = sorted({r.get("caster", "?") for r in rows}, key=lambda k: (KINDS.index(k) if k in KINDS else 9, k))
    for kind in kinds:
        mine = [r for r in rows if r.get("caster") == kind]
        lost = [r for r in mine if r.get("outcome") == "lost"]
        expired = sum(1 for r in mine if r.get("why") == "expired")
        immune = sum(1 for r in mine if r.get("why") in ("immune", "uninterruptible"))
        left = _median([v for v in (_int(r.get("leftMs")) for r in lost) if v is not None])
        out.append(f"| {kind} | {len(mine)} | {len(lost)} ({len(lost) / len(mine):.0%}) | {expired} | {immune} | "
                   f"{'-' if left is None else f'{left:.0f}'} |")
    spells = {}
    for r in rows:
        total, lost = spells.get(r.get("spell", "?"), (0, 0))
        spells[r.get("spell", "?")] = (total + 1, lost + (r.get("outcome") == "lost"))
    top = sorted(spells.items(), key=lambda item: -item[1][0])[:5]
    out.append("")
    out.append("Most sampled spell types: " + ", ".join(f"{name} {lost}/{total} lost" for name, (total, lost) in top) + ".")
    every = {r.get("every") for r in rows if r.get("every")}
    if every:
        out.append(f"Sampling: every {'/'.join(sorted(every))}th touched cast.")
    return "\n".join(out)
