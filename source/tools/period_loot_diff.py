"""Compares period loot, merchant and price rows from other projects with a Living Server world (#575). Read only.

    python -I source/tools/period_loot_diff.py --world <copy of a world>/opendaoc.sqlite3.db
        [--true165 <OfflineDAoC-True165 checkout>] [--dbpublic <db-public checkout>] [--moras <items.xml.midgard>]
        [--report out.json]

It never writes to the world: the database is opened read-only. Each source is optional; a section runs only when its
source is given. The report counts, for each row set, what the world is missing, what differs and what is the same, so a
later world repair can take only what a period source supports (docs/research/PERIOD-LOOT-MERCHANTS-575.md).

Sections:
  drops     True 1.65 runtime/true165/drops165.json, monster by monster. A monster is "not in the world", "has named
            loot" (a LootTemplate item under one of its MobXLootTemplate rows), "has an empty loot table" (a
            MobXLootTemplate row whose LootTemplate has no item: the server then drops only coins and random items), or
            "has no loot rows". Item rows of a monster with named loot are "same" (same item, same chance), "different
            chance" or "missing"; the world's own rows True 1.65 lacks are "ours only".
  focus     the monsters of the True 1.65 commits #575 names (Trollheim c52f109a, Tepok 46d1a00d), row by row.
  spawns    True 1.65 add-trollheim165.json: whether the world has a monster of that name.
  merchants True 1.65 add-inn-dye-*.json (the Traveler's Inn dye merchants): the merchants, their lists and items.
  prices    True 1.65 period_alchemy_prices.csv: the world's ItemTemplate price against the CSV's old and period price.
  thidranki db-public's Thidranki (every region a Regions row names Thidranki) against the world's: realm NPCs that sell
            (an ItemsListTemplateID or a merchant class), their lists, and whether the world has the items.
  later     the world's own loot rows (LootTemplate) and the merchant rows of lists a spawned merchant uses whose item
            carries a Trials of Atlantis or later bonus type (true165_prima_content.py's LATER_BONUS_TYPES, less the
            period-attested 156 and 163-165). Always runs.
  moras     an Uth-Moras items.xml (audit reference only: it has no licence; nothing is copied): for the dragon and epic
            zones, each item's level, bonus level, armor factor, DPS, speed and bonuses against the world's item of
            that name. Moras shows armor factor at the level-50 cap (100) and levels above 50 as 50; those are not counted
            as differences.
"""
import argparse
import collections
import csv
import json
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

FOCUS = ("Olen den Bartender", "Snemel den Forsumlig", "Remandun den Laerd", "goblin imperator", "Overseer Tepok")
MORAS_ZONES = ("Malmohus", "Summoner's Hall", "Darkness Falls")
# Moras effect ids -> eProperty (source/server/GameServer/Enums/eProperty.cs)
MORAS_PROPERTY = {
    "STRENGTH": 1, "DEXTERITY": 2, "CONSTITUTION": 3, "QUICKNESS": 4, "INTELLIGENCE": 5, "PIETY": 6, "EMPATHY": 7,
    "CHARISMA": 8, "POWER": 9, "HITPOINTS": 10, "RES_BODY": 11, "RES_COLD": 12, "RES_CRUSH": 13, "RES_ENERGY": 14,
    "RES_HEAT": 15, "RES_MATTER": 16, "RES_SLASH": 17, "RES_SPIRIT": 18, "RES_THRUST": 19, "CRITICAL_STRIKE": 23,
    "ENVENOM": 31, "PARRY": 40, "SHIELD": 43, "STEALTH": 49, "SWORD": 52, "HAMMER": 53, "AXE": 54, "LEFT_AXE": 55,
    "SPEAR": 56, "MENDING": 57, "AUGMENTATION": 58, "DARKNESS": 60, "SUPPRESSION": 61, "RUNECARVING": 62,
    "STORMCALLING": 63, "BEASTCRAFT": 64, "COMPOSITE_BOW": 68, "BATTLESONGS": 69, "SUBTERRANEAN": 85, "BONE_ARMY": 86,
    "THROWN_WEAPONS": 91, "SUMMONING": 98, "DARKNESS_FOCUS": 120, "SUPPRESSION_FOCUS": 121, "RUNECARVING_FOCUS": 122,
    "SUMMONING_FOCUS": 137, "BONE_ARMY_FOCUS": 138, "ACUITY": 156, "ALL_MAGIC_BONUS": 163, "ALL_MELEE_BONUS": 164,
    "ALL_MAGIC_FOCUS": 165, "ALL_DUAL_WIELD_BONUS": 167,
}
# as true165_prima_content.py: 99-119 and 145-256 are later than 1.65, except the period-attested 156, 163, 164, 165
LATER_BONUS_TYPES = set(range(99, 120)) | set(range(145, 257))
PERIOD_ATTESTED_BONUS_TYPES = {156, 163, 164, 165}
BONUS_SLOTS = [(f"Bonus{i}Type", f"Bonus{i}") for i in range(1, 11)] + [("ExtraBonusType", "ExtraBonus")]


def key(name):
    return (name or "").strip().lower()


def open_world(path):
    path = Path(path).resolve(strict=True)
    db = sqlite3.connect(f"{path.as_uri()}?mode=ro", uri=True)
    db.row_factory = sqlite3.Row
    return db


def tables(db):
    return {r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}


def columns(db, table):
    return [r[1] for r in db.execute(f'PRAGMA table_info("{table}")')]


def rows(db, table):
    return [dict(r) for r in db.execute(f'SELECT * FROM "{table}"')] if table in tables(db) else []


class World:
    """The world's loot, merchants and items, by lower-case name."""

    def __init__(self, db):
        self.db = db
        self.items = {key(r["Id_nb"]): r for r in rows(db, "ItemTemplate")}
        self.mobs = collections.defaultdict(list)
        for r in rows(db, "Mob"):
            self.mobs[key(r.get("Name"))].append(r)
        self.templates = {key(r.get("Name")) for r in rows(db, "NpcTemplate")}
        self.mob_loot = collections.defaultdict(list)
        for r in rows(db, "MobXLootTemplate"):
            self.mob_loot[key(r.get("MobName"))].append(key(r.get("LootTemplateName")))
        self.loot = collections.defaultdict(dict)
        for r in rows(db, "LootTemplate"):
            self.loot[key(r.get("TemplateName"))][key(r.get("ItemTemplateID"))] = r.get("Chance")
        self.lists = collections.defaultdict(list)
        for r in rows(db, "MerchantItem"):
            self.lists[key(r.get("ItemListID"))].append(r)

    def named_loot(self, monster):
        out = {}
        for template in self.mob_loot.get(key(monster), []):
            out.update(self.loot.get(template, {}))
        return out

    def loot_state(self, monster):
        k = key(monster)
        if k not in self.mobs and k not in self.templates:
            return "not_in_world"
        if self.named_loot(monster):
            return "has_named_loot"
        return "has_empty_loot_table" if k in self.mob_loot else "has_no_loot_rows"


def load_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def true165_file(root, name):
    root = Path(root)
    for candidate in (root / "runtime" / "true165" / name, root / name,
                      root / "development-source" / "server" / "tools" / name):
        if candidate.is_file():
            return candidate
    return None


def diff_drops(world, drops):
    monsters = drops.get("monsters", drops)
    out = {"monsters": collections.Counter(), "rows": collections.Counter(), "spawned": collections.Counter(),
           "examples": collections.defaultdict(list)}
    for name, entry in sorted(monsters.items()):
        state = world.loot_state(name)
        out["monsters"][state] += 1
        if state != "not_in_world":
            out["spawned"][state + (":spawned" if key(name) in world.mobs else ":template_only")] += 1
        if len(out["examples"][state]) < 8:
            out["examples"][state].append(name.strip())
        ours = world.named_loot(name)
        theirs = {key(item): chance for item, chance in entry.get("items", [])}
        for item, chance in theirs.items():
            if state == "has_named_loot":
                if item not in ours:
                    out["rows"]["missing"] += 1
                elif int(ours[item] or 0) == int(chance):
                    out["rows"]["same"] += 1
                else:
                    out["rows"]["different_chance"] += 1
            else:
                out["rows"][f"{state}:item_" + ("in_world" if item in world.items else "not_in_world")] += 1
        if state == "has_named_loot":
            out["rows"]["ours_only"] += sum(1 for item in ours if item not in theirs)
    return out


def focus_rows(world, drops, names=FOCUS):
    monsters = {key(k): v for k, v in drops.get("monsters", drops).items()}
    out = []
    for name in names:
        entry = monsters.get(key(name), {})
        ours = world.named_loot(name)
        out.append({
            "monster": name, "state": world.loot_state(name), "spawns": len(world.mobs.get(key(name), [])),
            "ours": sorted(ours.items()),
            "true165": [{"item": item, "chance": chance, "ours": ours.get(key(item)),
                         "item_in_world": key(item) in world.items} for item, chance in entry.get("items", [])],
        })
    return out


def diff_spawns(world, doc):
    out = {"missing": [], "same_name_in_world": []}
    for r in doc.get("rows", []):
        name = r.get("Name")
        bucket = "same_name_in_world" if key(name) in world.mobs else "missing"
        out[bucket].append({"name": name, "region": r.get("Region"), "level": r.get("Level")})
    return out


def diff_merchants(world, mob_doc, item_doc):
    lists = collections.defaultdict(list)
    for r in item_doc.get("rows", []):
        lists[key(r.get("ItemListID"))].append(r)
    out = {"merchants": [], "list_rows": 0, "items_missing": 0, "lists_in_world": 0}
    for r in mob_doc.get("rows", []):
        lid = key(r.get("ItemsListTemplateID"))
        same = [m for m in world.mobs.get(key(r.get("Name")), []) if m.get("Region") == r.get("Region")]
        out["merchants"].append({"name": r.get("Name"), "region": r.get("Region"), "in_world": bool(same),
                                 "list": r.get("ItemsListTemplateID"), "list_in_world": lid in world.lists})
    for lid, items in lists.items():
        out["list_rows"] += len(items)
        out["lists_in_world"] += lid in world.lists
        out["items_missing"] += sum(1 for i in items if key(i.get("ItemTemplateID")) not in world.items)
    sold = set()
    for items in lists.values():
        sold |= {key(i.get("ItemTemplateID")) for i in items}
    out["world_lists_selling_these_items"] = sum(
        1 for items in world.lists.values() if any(key(i.get("ItemTemplateID")) in sold for i in items))
    return out


def diff_prices(world, csv_path):
    out = collections.Counter()
    direction = collections.Counter()
    for r in csv.DictReader(Path(csv_path).open(encoding="utf-8", newline="")):
        item = world.items.get(key(r["item_id"]))
        old, period = int(r["old_price"]), int(r["period_price"])
        direction["up" if period > old else "down" if period < old else "unchanged"] += 1
        if item is None:
            out["missing"] += 1
        elif int(item.get("Price") or 0) == period:
            out["same"] += 1
        elif int(item.get("Price") or 0) == old:
            out["different:ours_is_true165_old_price"] += 1
        else:
            out["different:other_price"] += 1
    return {"items": out, "period_vs_old": direction}


def seller(mob):
    return bool(mob.get("ItemsListTemplateID")) or "merchant" in (mob.get("ClassType") or "").lower()


def thidranki_regions(region_rows):
    return sorted(int(r.get("RegionID")) for r in region_rows
                  if "thidranki" in (str(r.get("Description") or "") + str(r.get("Name") or "")).lower())


def diff_thidranki(world, dbp_mobs, dbp_items, dbp_regions):
    dbp_lists = collections.defaultdict(list)
    for r in dbp_items:
        dbp_lists[key(r.get("ItemListID"))].append(r)
    ours_regions = thidranki_regions(rows(world.db, "Regions"))
    theirs_regions = thidranki_regions(dbp_regions)
    out = {"world_regions": ours_regions, "dbpublic_regions": theirs_regions, "dbpublic": [], "world": []}
    for m in dbp_mobs:
        if m.get("Region") in theirs_regions and int(m.get("Realm") or 0) != 0 and seller(m):
            lid = key(m.get("ItemsListTemplateID"))
            items = dbp_lists.get(lid, [])
            out["dbpublic"].append({
                "name": m.get("Name"), "region": m.get("Region"), "realm": m.get("Realm"), "class": m.get("ClassType"),
                "list": m.get("ItemsListTemplateID"), "list_rows": len(items),
                "items_in_world": sum(1 for i in items if key(i.get("ItemTemplateID")) in world.items),
                "same_name_in_world_thidranki": any(x.get("Region") in ours_regions for x in world.mobs.get(key(m.get("Name")), [])),
            })
    for name, mobs in world.mobs.items():
        for m in mobs:
            if m.get("Region") in ours_regions and int(m.get("Realm") or 0) != 0 and seller(m):
                out["world"].append({"name": m.get("Name"), "region": m.get("Region"), "realm": m.get("Realm"),
                                     "list": m.get("ItemsListTemplateID"),
                                     "list_rows": len(world.lists.get(key(m.get("ItemsListTemplateID")), []))})
    out["world"].sort(key=lambda r: (r["region"], r["realm"], r["name"]))
    return out


def dbpublic_rows(root, table):
    data = Path(root) / "data"
    files = sorted(data.glob(f"{table}.*.json")) or [data / f"{table}.json"]
    out = []
    for f in files:
        if f.is_file():
            out.extend(load_json(f))
    return out


def parse_moras(path):
    raw = Path(path).read_bytes().decode("iso-8859-1")
    raw = re.sub(r"^<\?xml[^>]*\?>", "", raw.lstrip())
    items = []
    for it in ET.fromstring(raw).findall("item"):
        origin = it.findtext("origin") or ""
        zone = re.search(r"Zone:\s*([^\n]+)", origin)
        mob = re.search(r"Mob:\s*([^\n]+)", origin)
        weapon, armor = it.find("weapon"), it.find("armor")
        items.append({
            "name": (it.findtext("name") or "").strip(),
            "zone": re.sub(r"\s+", " ", zone.group(1)).strip().rstrip(".") if zone else None,
            "mob": mob.group(1).strip() if mob else None,
            "level": it.findtext("level"), "bonus": it.findtext("bonus"),
            "af": armor.get("af") if armor is not None else None,
            "dps": weapon.get("dps") if weapon is not None else None,
            "speed": weapon.get("speed") if weapon is not None else None,
            "effects": [(e.get("id"), e.text) for e in it.findall("effect")],
        })
    return items


def item_bonuses(item):
    out = {}
    for t, v in BONUS_SLOTS:
        if item.get(t) and item.get(v):
            out[int(item[t])] = out.get(int(item[t]), 0) + int(item[v])
    return out


def moras_differences(m, ours):
    diffs = []
    level, our_level = int(m["level"] or 0), int(ours.get("Level") or 0)
    if m["level"] and level != our_level and not (level >= 50 and our_level >= 50):
        diffs.append("level")
    if m["bonus"] and int(m["bonus"]) != int(ours.get("Bonus") or 0):
        diffs.append("bonus_level")
    af, our_af = int(m["af"] or 0), int(ours.get("DPS_AF") or 0)
    if m["af"] and af != our_af and not (af == 100 and our_af >= 100) and not (af == 50 and our_af in (50, 51)):
        diffs.append("armor_factor")
    if m["dps"] and round(float(m["dps"].replace(",", ".")) * 10) != our_af:
        diffs.append("dps")
    if m["speed"] and round(float(m["speed"].replace(",", ".")) * 10) != int(ours.get("SPD_ABS") or 0):
        diffs.append("speed")
    wanted = {}
    for eid, value in m["effects"]:
        if eid in MORAS_PROPERTY:
            wanted[MORAS_PROPERTY[eid]] = wanted.get(MORAS_PROPERTY[eid], 0) + int(value or 0)
    if wanted != item_bonuses(ours):
        diffs.append("bonuses")
    return diffs


def diff_moras(world, moras_items, zones=MORAS_ZONES):
    by_name = collections.defaultdict(list)
    for item in world.items.values():
        by_name[key(item.get("Name"))].append(item)
    out = {"zones": {}, "fields": collections.Counter(), "rows": []}
    for m in moras_items:
        zone = (m["zone"] or "").replace("Fallls", "Falls")
        if zone not in zones:
            continue
        counts = out["zones"].setdefault(zone, collections.Counter())
        name = re.sub(r"\s*\([A-Z]\)\s*$", "", m["name"]).strip()
        candidates = [i for i in by_name.get(key(name), []) if int(i.get("Realm") or 0) in (0, 2)] or by_name.get(key(name), [])
        if not candidates:
            counts["missing"] += 1
            out["rows"].append({"zone": zone, "mob": m["mob"], "item": m["name"], "result": "missing"})
            continue
        best = min((moras_differences(m, c) for c in candidates), key=len)
        counts["different" if best else "same"] += 1
        out["fields"].update(best)
        out["rows"].append({"zone": zone, "mob": m["mob"], "item": m["name"],
                            "result": "different" if best else "same", "fields": best})
    return out


def later_bonus(item):
    """The first later-era bonus type the item carries, or None."""
    for t, v in BONUS_SLOTS:
        kind = item.get(t)
        if kind and item.get(v) and int(kind) in LATER_BONUS_TYPES and int(kind) not in PERIOD_ATTESTED_BONUS_TYPES:
            return int(kind)
    return None


def diff_later_bonuses(world):
    out = {"loot_rows": [], "merchant_rows": []}
    for template, items in sorted(world.loot.items()):
        for iid in sorted(items):
            item = world.items.get(iid)
            if item is not None and (kind := later_bonus(item)) is not None:
                out["loot_rows"].append({"template": template, "item": item["Id_nb"], "bonus_type": kind})
    used = {key(m.get("ItemsListTemplateID")) for mobs in world.mobs.values() for m in mobs if m.get("ItemsListTemplateID")}
    for lid in sorted(used):
        for r in world.lists.get(lid, []):
            item = world.items.get(key(r.get("ItemTemplateID")))
            if item is not None and (kind := later_bonus(item)) is not None:
                out["merchant_rows"].append({"list": r.get("ItemListID"), "item": item["Id_nb"], "bonus_type": kind})
    return out


def run(args):
    db = open_world(args.world)
    world = World(db)
    report = {"world": str(args.world), "later": diff_later_bonuses(world)}
    if args.true165:
        drops_file = true165_file(args.true165, "drops165.json")
        if drops_file:
            drops = load_json(drops_file)
            report["drops"] = diff_drops(world, drops)
            report["focus"] = focus_rows(world, drops)
        spawn_file = true165_file(args.true165, "add-trollheim165.json")
        if spawn_file:
            report["spawns"] = diff_spawns(world, load_json(spawn_file))
        mob_file = true165_file(args.true165, "add-inn-dye-mob165.json")
        item_file = true165_file(args.true165, "add-inn-dye-merchantitem165.json")
        if mob_file and item_file:
            report["merchants"] = diff_merchants(world, load_json(mob_file), load_json(item_file))
        price_file = true165_file(args.true165, "period_alchemy_prices.csv")
        if price_file:
            report["prices"] = diff_prices(world, price_file)
    if args.dbpublic:
        report["thidranki"] = diff_thidranki(world, dbpublic_rows(args.dbpublic, "Mob"),
                                             dbpublic_rows(args.dbpublic, "MerchantItem"),
                                             dbpublic_rows(args.dbpublic, "Regions"))
    if args.moras:
        report["moras"] = diff_moras(world, parse_moras(args.moras))
    return report


def summary(report):
    lines = []
    if "drops" in report:
        d = report["drops"]
        lines.append("drops: monsters " + ", ".join(f"{k} {v}" for k, v in sorted(d["monsters"].items())))
        lines.append("drops: rows " + ", ".join(f"{k} {v}" for k, v in sorted(d["rows"].items())))
    if "spawns" in report:
        lines.append(f"spawns: missing {len(report['spawns']['missing'])}, "
                     f"same name in world {len(report['spawns']['same_name_in_world'])}")
    if "merchants" in report:
        m = report["merchants"]
        lines.append(f"dye merchants: {sum(not x['in_world'] for x in m['merchants'])} of {len(m['merchants'])} missing, "
                     f"{m['list_rows']} list rows, {m['items_missing']} items missing, "
                     f"{m['world_lists_selling_these_items']} world lists already sell these items")
    if "prices" in report:
        lines.append("prices: " + ", ".join(f"{k} {v}" for k, v in sorted(report["prices"]["items"].items())))
    if "thidranki" in report:
        t = report["thidranki"]
        lines.append(f"thidranki: world regions {t['world_regions']}, db-public regions {t['dbpublic_regions']}, "
                     f"db-public sellers {len(t['dbpublic'])}, world sellers {len(t['world'])}")
    if "later" in report:
        lines.append(f"later-era bonuses: {len(report['later']['loot_rows'])} loot rows, "
                     f"{len(report['later']['merchant_rows'])} merchant rows of lists a spawned merchant uses")
    if "moras" in report:
        for zone, counts in sorted(report["moras"]["zones"].items()):
            lines.append(f"moras {zone}: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--world", required=True, help="a copy of a world database (opened read-only)")
    parser.add_argument("--true165", help="an OfflineDAoC-True165 checkout (or a folder with its runtime/true165 files)")
    parser.add_argument("--dbpublic", help="an Eve-of-Darkness/db-public checkout (its data/*.json)")
    parser.add_argument("--moras", help="an Uth-Moras items.xml file (audit reference only)")
    parser.add_argument("--report", help="write the full report here as JSON")
    args = parser.parse_args(argv)
    report = run(args)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=1, default=list), encoding="utf-8")
    print(summary(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
