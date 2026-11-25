"""REALM_LEVEL_SPREAD (issue #625 row 6, for #544): how each realm's gamebots spread over the 5-level bands and how
many levels they gained. Hourly rows; the first hour after a start is a baseline."""
from __future__ import annotations

from collections import defaultdict

from living_report_sections._util import fields, num, table

TITLE = "Realm level spread (gamebots)"
ROWS = ["REALM_LEVEL_SPREAD"]
ORDER = 60
REALMS = ("Albion", "Midgard", "Hibernia")


def summarize(lines) -> str:
    hours = defaultdict(list)
    for line in lines:
        f = fields(line)
        hours[f.get("hour", "?")].append(f)
    if not hours:
        return "_No REALM_LEVEL_SPREAD rows (the server writes them each UTC hour)._"
    counted = {h: rows for h, rows in hours.items() if not any(r.get("baseline") == "1" for r in rows)}
    last_hour = max(hours)
    latest = hours[last_hour]
    out = [f"Latest hour {last_hour} ({len(hours)} hour(s) in the logs, {len(counted)} after a baseline)."]

    totals = {realm: defaultdict(int) for realm in REALMS}
    for rows in counted.values():
        for r in rows:
            if r.get("realm") in totals:
                for key in ("gained", "levelled", "new", "rerolled", "gone"):
                    totals[r["realm"]][key] += num(r.get(key))
    now = {realm: sum(num(r.get("bots")) for r in latest if r.get("realm") == realm) for realm in REALMS}
    out += ["", table(["Realm", "Bots now", "Levels gained", "Bots that levelled", "New", "Re-rolled", "Gone",
                       "Levels / bot / hour"],
                      [[realm, now[realm], totals[realm]["gained"], totals[realm]["levelled"], totals[realm]["new"],
                        totals[realm]["rerolled"], totals[realm]["gone"],
                        f"{totals[realm]['gained'] / now[realm] / len(counted):.2f}" if now[realm] and counted else "-"]
                       for realm in REALMS])]

    bands = sorted({r.get("band", "?") for r in latest})
    cell = {(r.get("realm"), r.get("band")): r for r in latest}
    out += ["", f"Bots per band at {last_hour} (gained that hour in brackets):", "",
            table(["Band"] + list(REALMS),
                  [[band] + [f"{num(cell[(realm, band)].get('bots'))} ({num(cell[(realm, band)].get('gained'))})"
                             if (realm, band) in cell else "" for realm in REALMS] for band in bands])]
    flagged = sum(totals[realm]["rerolled"] for realm in REALMS)
    if flagged:
        out += ["", f"{flagged} re-rolled bot(s) flagged (level fell or class changed under the same id): leave them "
                    "out of pace comparisons."]
    return "\n".join(out)
