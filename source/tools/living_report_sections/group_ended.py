"""GROUP_ENDED (issue #625 row 7, for #544: 71-80% of group camp attempts end GroupChanged): why gamebot groups end,
how long they last, who left, and what the GroupChanged attempt ends really were."""
from __future__ import annotations

from collections import Counter, defaultdict

from living_report_sections._util import fields, median, num, pct, table

TITLE = "Gamebot groups ended"
ROWS = ["GROUP_ENDED"]
ORDER = 61
GC_NOTE = {
    "joined": "joined a new group",
    "resized": "group size changed, choosing a target again",
    "assembling": "group back to assembly with a camp in hand",
    "meetup_plan": "leader's meetup camp pick (an attempt opened and closed at once)",
    "casualty": "party casualty, regroup",
}


def pairs(text: str) -> dict:
    out = {}
    if text and text != "-":
        for part in text.split(","):
            key, _, value = part.partition(":")
            out[key] = num(value)
    return out


def summarize(lines) -> str:
    rows = [fields(line) for line in lines]
    if not rows:
        return "_No GROUP_ENDED rows._"
    stands_for = sum(max(1, num(r.get("every"), 1)) * (1 + num(r.get("dropped"))) for r in rows)
    out = [f"{len(rows)} rows, standing for about {stands_for} ended groups."]

    by_reason = defaultdict(list)
    for r in rows:
        by_reason[(r.get("reason", "?"), r.get("cause", "-"))].append(r)
    out += ["", table(["Reason", "Cause", "Groups", "Share", "Median minutes", "Median size start > end", "Task started"],
                      [[reason, cause, len(rs), pct(len(rs), len(rows)),
                        f"{median([num(r.get('dur_s')) / 60 for r in rs]):.0f}",
                        f"{median([num(r.get('size_start')) for r in rs]):g} > {median([num(r.get('size_end')) for r in rs]):g}",
                        pct(sum(1 for r in rs if r.get("task_started") == "1"), len(rs))]
                       for (reason, cause), rs in sorted(by_reason.items(), key=lambda kv: -len(kv[1]))])]

    gc = Counter()
    for r in rows:
        gc.update(pairs(r.get("gc", "-")))
    open_ = sum(num(r.get("open")) for r in rows)
    total = sum(gc.values()) + open_
    if total:
        out += ["", "Camp attempts that ended GroupChanged, by the controller's cause (open = closed as \"group "
                    "disbanded\" after the end):", "",
                table(["Cause", "Attempts", "Share", "What it is"],
                      [[cause, n, pct(n, total), GC_NOTE.get(cause, "")] for cause, n in gc.most_common()] +
                      [["open at end", open_, pct(open_, total), "the group ended; members close their attempt"]])]

    left = Counter()
    for r in rows:
        left.update(pairs(r.get("left_by", "-")))
    if left:
        out += ["", "Members who left during a group: " + ", ".join(f"{k} {v}" for k, v in left.most_common()) + "."]
    by_objective = Counter(r.get("objective", "?") for r in rows)
    wipes = sum(1 for r in rows if r.get("reason") == "wipe")
    hosted = sum(1 for r in rows if r.get("hosted") == "1")
    out += ["", f"By objective: {', '.join(f'{k} {v}' for k, v in by_objective.most_common())}. Wipes at the end: {wipes}. "
                f"Groups that hosted the player: {hosted}."]
    return "\n".join(out)
