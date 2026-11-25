"""ECONOMY_PULSE (issue #625 row 8): the Realm Exchange and the gamebots' coin by source, per realm, hourly."""
from __future__ import annotations

from collections import defaultdict

from living_report_sections._util import fields, median, num, table

TITLE = "Economy pulse"
ROWS = ["ECONOMY_PULSE"]
ORDER = 62
REALMS = ("Albion", "Midgard", "Hibernia")
BANDS = ("weapon", "armor", "jewelry", "other")
SOURCES = ("loot", "vendor", "exchange", "repair", "upkeep", "craft", "other")


def money(copper) -> str:
    copper = int(copper)
    sign = "-" if copper < 0 else ""
    copper = abs(copper)
    gold, rest = divmod(copper, 10_000)
    silver, cp = divmod(rest, 100)
    return f"{sign}{gold}g {silver}s" if gold else f"{sign}{silver}s {cp}c"


def summarize(lines) -> str:
    by_realm = defaultdict(list)
    for line in lines:
        f = fields(line)
        by_realm[f.get("realm", "?")].append(f)
    if not by_realm:
        return "_No ECONOMY_PULSE rows (the economy ledger writes them each UTC hour)._"
    hours = sorted({f.get("hour", "?") for rows in by_realm.values() for f in rows})
    out = [f"{len(hours)} hour(s), {hours[0]} to {hours[-1]}."]

    head = ["Realm", "Listings (bot)", "Sold", "Sold value", "Tax"] + [f"{b} listed / sold (median)" for b in BANDS]
    body = []
    for realm in REALMS:
        rows = by_realm.get(realm, [])
        if not rows:
            continue
        last = rows[-1]
        cells = [realm, f"{num(last.get('listings'))} ({num(last.get('bot_listings'))})",
                 sum(num(r.get("sold")) for r in rows), money(sum(num(r.get("sold_copper")) for r in rows)),
                 money(sum(num(r.get("tax")) for r in rows))]
        for band in BANDS:
            sold = sum(num(r.get(f"sold_{band}")) for r in rows)
            med = median([num(r.get(f"sold_{band}_med")) for r in rows if num(r.get(f"sold_{band}"))])
            cells.append(f"{num(last.get(f'list_{band}'))} ({money(num(last.get(f'list_{band}_med')))}) / "
                         f"{sold} ({money(med) if med is not None else '-'})")
        body.append(cells)
    out += ["", "The Exchange (listings at the last hour; sold summed over the hours, median of the hourly medians):", "",
            table(head, body)]

    coin = []
    for realm in REALMS:
        rows = by_realm.get(realm, [])
        if not rows:
            continue
        cells = [realm]
        for source in SOURCES:
            cells.append(f"{money(sum(num(r.get(f'{source}_in')) for r in rows))} / "
                         f"{money(sum(num(r.get(f'{source}_out')) for r in rows))}")
        net = sum(num(r.get("coin_in")) - num(r.get("coin_out")) for r in rows)
        coin.append(cells + [money(net)])
    out += ["", "Gamebot coin in / out by source (training costs no coin):", "",
            table(["Realm"] + list(SOURCES) + ["Net"], coin)]
    return "\n".join(out)
