"""PLAYER_LINE_OUTCOME (#625 row 5): what came of each line the player typed to bots, within 30 s. The real-play answer rate.

Row: ``PLAYER_LINE_OUTCOME seq= t= ch= realm= intent= answered= latencyMs= kind= bots= replies= window=``
"""
import re

TITLE = "Player lines answered"
ROWS = ["PLAYER_LINE_OUTCOME"]

_FIELD = re.compile(r'(\w+)=("[^"]*"|\S+)')
CHANNELS = ("say", "group", "tell", "faction", "trade")
KINDS = ("answer", "knowledge", "ack", "emote", "none")


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
        return 0


def summarize(lines) -> str:
    rows = list(_rows(lines, "PLAYER_LINE_OUTCOME"))
    if not rows:
        return "No PLAYER_LINE_OUTCOME rows."
    out = ["| Channel | Lines | Answered in 30 s | Median latency ms | Avg answering bots | " + " | ".join(KINDS) + " |",
           "|---|---:|---:|---:|---:|" + "---:|" * len(KINDS)]
    channels = sorted({r.get("ch", "?") for r in rows}, key=lambda c: (CHANNELS.index(c) if c in CHANNELS else 9, c))
    for channel in channels + ["all"]:
        mine = rows if channel == "all" else [r for r in rows if r.get("ch") == channel]
        answered = [r for r in mine if r.get("answered") == "1"]
        latency = _median([_int(r.get("latencyMs")) for r in answered])
        bots = sum(_int(r.get("bots")) for r in answered) / len(answered) if answered else 0
        kinds = " | ".join(str(sum(1 for r in mine if r.get("kind") == kind)) for kind in KINDS)
        name = "**all**" if channel == "all" else channel
        out.append(f"| {name} | {len(mine)} | {len(answered)} ({len(answered) / len(mine):.0%}) | "
                   f"{'-' if latency is None else f'{latency:.0f}'} | {bots:.1f} | {kinds} |")
    unanswered = {}
    for r in rows:
        if r.get("answered") != "1":
            unanswered[r.get("intent", "?")] = unanswered.get(r.get("intent", "?"), 0) + 1
    if unanswered:
        top = sorted(unanswered.items(), key=lambda item: -item[1])[:5]
        out.append("")
        out.append("Unanswered by intent: " + ", ".join(f"{intent} {n}" for intent, n in top) + ".")
    return "\n".join(out)
