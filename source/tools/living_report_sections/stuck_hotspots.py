"""STUCK_HOTSPOT (issue #625 row 9, for #555-type finds): where gamebots got stuck, merged across bots and recovery
kinds on a 500-unit grid per region."""
from __future__ import annotations

from collections import defaultdict

from living_report_sections._util import fields, num, table

TITLE = "Stuck hotspots"
ROWS = ["STUCK_HOTSPOT"]
ORDER = 63
KINDS = ("stuck15m", "stall45m", "position", "seam")
TOP = 15


def summarize(lines) -> str:
    cells = defaultdict(lambda: {"total": 0, "bots": 0, "hours": 0, **{k: 0 for k in KINDS}})
    hours, total, dropped = set(), 0, 0
    for line in lines:
        f = fields(line)
        hours.add(f.get("hour"))
        if f.get("rank") == "0":
            total += num(f.get("total"))
            dropped += num(f.get("dropped"))
            continue
        cell = cells[(num(f.get("region")), num(f.get("x")), num(f.get("y")))]
        cell["total"] += num(f.get("total"))
        cell["bots"] = max(cell["bots"], num(f.get("bots")))
        cell["hours"] += 1
        for k in KINDS:
            cell[k] += num(f.get(k))
    if not hours:
        return "_No STUCK_HOTSPOT rows._"
    out = [f"{len(hours)} hour(s); {total} stuck or recovery events in all"
           + (f" ({dropped} in cells past the hourly cap)" if dropped else "") + "."]
    if not cells:
        return "\n".join(out + ["", "No hotspot cells."])
    ranked = sorted(cells.items(), key=lambda kv: (-kv[1]["total"], -kv[1]["hours"], kv[0]))[:TOP]
    out += ["", "Busiest cells (each hour's top ten, summed; bots = the most in one hour):", "",
            table(["Region", "x", "y", "Events", "Hours", "Bots"] + list(KINDS),
                  [[r, x, y, c["total"], c["hours"], c["bots"]] + [c[k] for k in KINDS] for (r, x, y), c in ranked])]
    repeat = [key for key, c in ranked if c["hours"] >= 3]
    if repeat:
        out += ["", f"{len(repeat)} cell(s) are in the top ten for 3 or more hours: look there first."]
    return "\n".join(out)
