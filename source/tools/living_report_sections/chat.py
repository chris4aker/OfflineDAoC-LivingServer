"""CHAT_LINE: how much the bots and the player said, per channel (counts only, no names or text)."""
from __future__ import annotations

from collections import Counter

from living_report_sections._util import fields, table

TITLE = "Chat lines"
ROWS = ["CHAT_LINE"]
ORDER = 90


def summarize(lines) -> str:
    counts = Counter()
    sources = Counter()
    kinds = Counter()
    for line in lines:
        f = fields(line)
        counts[(f.get("ch", "?"), f.get("who", "?"))] += 1
        if f.get("who") == "bot":
            sources[f.get("src", "-")] += 1
            kinds[f.get("kind", "?")] += 1
    if not counts:
        return "_No CHAT_LINE rows._"
    channels = sorted({ch for ch, _ in counts}, key=lambda ch: -sum(n for (c, _), n in counts.items() if c == ch))
    rows = [[ch, counts[(ch, "bot")], counts[(ch, "player")], sum(n for (c, _), n in counts.items() if c == ch)]
            for ch in channels]
    out = [f"{sum(counts.values())} lines.", "", table(["Channel", "Bots", "Player", "All"], rows)]
    if kinds:
        out += ["", "Bot lines by kind of bot: " + ", ".join(f"{k} {v}" for k, v in kinds.most_common()) + "."]
    if len(sources) > 1 or "-" not in sources:
        out += ["", "Bot lines by source: " + ", ".join(f"{k} {v}" for k, v in sources.most_common(10)) + "."]
    return "\n".join(out)
