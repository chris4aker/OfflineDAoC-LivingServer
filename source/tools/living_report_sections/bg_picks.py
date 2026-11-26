"""BG_TARGET_PICK and BG_RALLY_RESPONSE (#625 row 4): battleground picks and the rally's reaction (#621, FarmVibes' points).

Rows: ``BG_TARGET_PICK seq= every= t= bg= realm= src= pick= target= skippedControlled= skippedEngines= rally= list= candidates=``
and ``BG_RALLY_RESPONSE t= bg= realm= contact= pet= ms= nth=``
"""
import re

TITLE = "Battleground picks and rally response (#621)"
ROWS = ["BG_TARGET_PICK", "BG_RALLY_RESPONSE"]

_FIELD = re.compile(r'(\w+)=("[^"]*"|\S+)')


def _rows(lines, marker):
    for line in lines:
        at = line.find(marker + " ")
        if at < 0 or (at > 0 and line[at - 1] not in " |"):
            continue
        yield {key: value.strip('"') for key, value in _FIELD.findall(line[at + len(marker):])}


def _int(text):
    try:
        return int(text)
    except (TypeError, ValueError):
        return 0


def _median(values):
    values = sorted(values)
    if not values:
        return None
    mid = len(values) // 2
    return values[mid] if len(values) % 2 else (values[mid - 1] + values[mid]) / 2


def _percentile(values, share):
    values = sorted(values)
    if not values:
        return None
    return values[min(len(values) - 1, int(round(share * (len(values) - 1))))]


def summarize(lines) -> str:
    lines = list(lines)
    picks = list(_rows(lines, "BG_TARGET_PICK"))
    responses = list(_rows(lines, "BG_RALLY_RESPONSE"))
    if not picks and not responses:
        return "No BG_TARGET_PICK or BG_RALLY_RESPONSE rows."
    out = []
    if picks:
        out += ["| Source | Pick | Sampled | Controlled skipped (avg) | Engines skipped (avg) | From the rally board |",
                "|---|---|---:|---:|---:|---:|"]
        groups = {}
        for r in picks:
            groups.setdefault((r.get("src", "?"), r.get("pick", "?")), []).append(r)
        for (src, pick), mine in sorted(groups.items(), key=lambda item: (item[0][0], -len(item[1]))):
            n = len(mine)
            out.append(f"| {src} | {pick} | {n} | {sum(_int(r.get('skippedControlled')) for r in mine) / n:.2f} | "
                       f"{sum(_int(r.get('skippedEngines')) for r in mine) / n:.2f} | "
                       f"{sum(1 for r in mine if r.get('rally') == '1')} |")
        engines = [r for r in picks if r.get("pick") == "engine"]
        out.append("")
        out.append(f"Engine picks: {len(engines)} of {len(picks)} sampled picks.")
    else:
        out.append("No BG_TARGET_PICK rows.")
    out.append("")
    if responses:
        ms = [_int(r.get("ms")) for r in responses]
        first = [_int(r.get("ms")) for r in responses if r.get("nth") == "1"]
        out.append(f"Rally responses: {len(responses)}; median {_median(ms):.0f} ms, 90th percentile "
                   f"{_percentile(ms, 0.9)} ms; first responder median {f'{_median(first):.0f}' if first else '-'} ms; "
                   f"most responders to one contact: {max(_int(r.get('nth')) for r in responses)}.")
    else:
        out.append("No BG_RALLY_RESPONSE rows.")
    return "\n".join(out)
