"""Export the crafting recipes of a world database as the economy rehearsal's recipe fixture (Crafting 2.0 WP10).

Reads WORLD tables only (CraftedItem, CraftedXItem, ItemTemplate, MerchantItem, and Mob for which item lists are coin
merchants') from a copy of a world database opened
read-only, and writes source/server/Tests/Rehearsal/fixtures/recipes.json: every recipe with its product's stats and
price, its ingredients, the materials' merchant prices, and a merchant gear price ladder by level. It never reads a
saved-progress table (the script refuses any table named in progress-policy.json), and the fixture holds no account,
character, bot or inventory data.

Keys are matched without case, as the server matches them (SQLite NOCASE columns, lower-cased template cache; see
docs/CRAFTING-M0-FINDINGS.md 1.1). A recipe the server would hide is kept with a "hidden" reason (the M0 rule:
no ingredients, a missing product or ingredient, a count of 0 or less, a statless product in a weapon or armor slot),
so the rehearsal can skip it as the server does.

    python source/tools/export_recipe_fixture.py COPY-OF/opendaoc.sqlite3.db
    python source/tools/export_recipe_fixture.py COPY-OF/opendaoc.sqlite3.db --out fixture.json --check

Work on a copy: the database is opened with mode=ro, so the original is never written, but a live server's file
should not be read while it runs. Standard library only.
"""
import argparse
import json
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_OUT = HERE.parent / "server" / "Tests" / "Rehearsal" / "fixtures" / "recipes.json"
POLICY = HERE / "OfflineDaoc.ProgressImport" / "progress-policy.json"

# The only tables this script reads. All are world data, the same in a fresh install unless a patch changed them.
WORLD_TABLES = ("crafteditem", "craftedxitem", "itemtemplate", "merchantitem", "mob")
# Merchants that price in coin (seal, bounty and other item-currency merchants price in their own currency).
COIN_MERCHANT_CLASSES = {"dol.gs.gamemerchant"}

# Item_Type values of weapon and armor slots (eInventorySlot): a product there with Object_Type 0 is a statless shell.
WEAPON_ARMOR_SLOTS = {10, 11, 12, 13, 21, 22, 23, 25, 27, 28}
# Object types of the gear the merchant ladder prices (weapons 2-28, armor 32-38, shields 42).
GEAR_OBJECT_TYPES = set(range(2, 29)) | set(range(32, 39)) | {42}
SKILLS = {1: "WeaponCrafting", 2: "ArmorCrafting", 3: "SiegeCrafting", 4: "Alchemy", 6: "MetalWorking",
          7: "LeatherCrafting", 8: "ClothWorking", 9: "GemCutting", 10: "HerbalCrafting", 11: "Tailoring",
          12: "Fletching", 13: "SpellCrafting", 14: "WoodWorking", 15: "BasicCrafting"}


def player_tables(policy_path=POLICY):
    """Every table the progress policy names (saved progress, optional progress, cleared tables), lower-cased."""
    try:
        policy = json.loads(Path(policy_path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return set()
    names = set()
    for key in ("ProgressTables", "OptionalProgressTables", "ClearTables"):
        names.update(str(name).lower() for name in policy.get(key, []))
    return names


def check_tables(tables=WORLD_TABLES, policy_path=POLICY):
    """Refuses to go on if a table to read is a player table (a guard against a future edit of WORLD_TABLES)."""
    clash = sorted(set(t.lower() for t in tables) & player_tables(policy_path))
    if clash:
        raise SystemExit(f"refusing to read saved-progress tables: {', '.join(clash)}")


def open_read_only(path):
    uri = Path(path).resolve().as_uri() + "?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    connection.row_factory = sqlite3.Row
    return connection


def columns(connection, table):
    return {row[1].lower(): row[1] for row in connection.execute(f"PRAGMA table_info({table})")}


def read(connection, table, wanted):
    """Rows of a world table with the wanted columns (missing columns read as None), keys lower-cased."""
    if table.lower() not in WORLD_TABLES:
        raise ValueError(f"{table} is not a world table this script may read")
    have = columns(connection, table)
    select = ", ".join(f'"{have[c.lower()]}"' if c.lower() in have else "NULL" for c in wanted)
    for row in connection.execute(f"SELECT {select} FROM {table}"):
        yield {c: row[i] for i, c in enumerate(wanted)}


def as_int(value, default=0):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def build_fixture(connection, source_label="world database"):
    templates = {}
    for row in read(connection, "itemtemplate", ["Id_nb", "Name", "Level", "Quality", "DPS_AF", "SPD_ABS", "Hand",
                                                 "Object_Type", "Item_Type", "Model", "Price", "PackSize", "MaxCount",
                                                 "Realm", "MaxCondition"]):
        key = (row["Id_nb"] or "").lower()
        if key:
            templates[key] = row

    coin_lists = set()
    for row in read(connection, "mob", ["ClassType", "ItemsListTemplateID"]):
        if (row["ClassType"] or "").lower() in COIN_MERCHANT_CLASSES and row["ItemsListTemplateID"]:
            coin_lists.add(row["ItemsListTemplateID"].lower())
    sold, sold_for_coin = set(), set()
    for row in read(connection, "merchantitem", ["ItemListID", "ItemTemplateID"]):
        if row["ItemTemplateID"]:
            sold.add(row["ItemTemplateID"].lower())
            if (row["ItemListID"] or "").lower() in coin_lists:
                sold_for_coin.add(row["ItemTemplateID"].lower())

    ingredients = defaultdict(list)
    for row in read(connection, "craftedxitem", ["CraftedItemId_nb", "IngredientId_nb", "Count"]):
        ingredients[(row["CraftedItemId_nb"] or "").lower()].append(((row["IngredientId_nb"] or "").lower(), as_int(row["Count"])))

    recipe_rows = list(read(connection, "crafteditem", ["CraftedItemID", "Id_nb", "CraftingLevel", "CraftingSkillType", "MakeTemplated"]))
    products = {(row["Id_nb"] or "").lower() for row in recipe_rows}

    recipes, materials, hidden = [], {}, defaultdict(int)
    for row in sorted(recipe_rows, key=lambda r: as_int(r["CraftedItemID"])):
        product_key = (row["Id_nb"] or "").lower()
        product = templates.get(product_key)
        parts = ingredients.get(product_key, [])
        problem = None
        if product is None:
            problem = "ProductMissing"
        elif not parts:
            problem = "NoIngredients"
        elif any(key not in templates for key, _ in parts):
            problem = "IngredientMissing"
        elif any(count <= 0 for _, count in parts):
            problem = "IngredientCount"
        elif len({key for key, _ in parts}) != len(parts):
            problem = "DuplicateIngredientName"
        elif as_int(row["CraftingSkillType"]) not in SKILLS or as_int(row["CraftingSkillType"]) == 5:
            problem = "SkillType"
        elif as_int(product["Object_Type"]) == 0 and as_int(product["Item_Type"]) in WEAPON_ARMOR_SLOTS:
            problem = "ProductUnusable"
        if problem:
            hidden[problem] += 1

        stackable = product is not None and (as_int(product["MaxCount"]) > 1 or as_int(product["PackSize"]) > 1)
        entry = {
            "id": as_int(row["CraftedItemID"]),
            "skill": as_int(row["CraftingSkillType"]),
            "level": as_int(row["CraftingLevel"]),
            "product": product_key,
        }
        if product is not None:
            entry.update({
                "name": product["Name"] or "",
                "objectType": as_int(product["Object_Type"]),
                "itemType": as_int(product["Item_Type"]),
                "productLevel": as_int(product["Level"]),
                "realm": as_int(product["Realm"]),
                "price": as_int(product["Price"]),
            })
            # Omitted when 0 / the usual value, to keep the fixture small: the rehearsal reads the same defaults.
            for key, column, usual in (("quality", "Quality", 85), ("dpsAf", "DPS_AF", 0), ("spdAbs", "SPD_ABS", 0),
                                       ("hand", "Hand", 0), ("maxCondition", "MaxCondition", 50000)):
                if as_int(product[column]) != usual:
                    entry[key] = as_int(product[column])
            if stackable:
                entry["stackable"] = True
            if as_int(row["MakeTemplated"]):
                entry["templated"] = True
        entry["materials"] = [[key, count] for key, count in parts]
        if problem:
            entry["hidden"] = problem
        recipes.append(entry)
        for key, _ in parts:
            template = templates.get(key)
            if template is not None and key not in materials:
                materials[key] = {
                    "name": template["Name"] or "",
                    "model": as_int(template["Model"]),
                    "price": as_int(template["Price"]),
                    "packSize": max(1, as_int(template["PackSize"], 1)),
                    "sold": key in sold,
                    "crafted": key in products,
                }

    # The merchant gear ladder: the median price of weapon and armor templates on coin merchants' lists, by item level.
    by_level = defaultdict(list)
    for key in sold_for_coin:
        template = templates.get(key)
        if template is None or as_int(template["Object_Type"]) not in GEAR_OBJECT_TYPES:
            continue
        level, price = as_int(template["Level"]), as_int(template["Price"])
        if 1 <= level <= 51 and price > 0:
            by_level[min(level, 50)].append(price)
    ladder = {str(level): sorted(prices)[len(prices) // 2] for level, prices in sorted(by_level.items())}

    counts = defaultdict(int)
    for recipe in recipes:
        counts[SKILLS.get(recipe["skill"], str(recipe["skill"]))] += 1
    return {
        "about": ("Crafting recipes of the world (CraftedItem, CraftedXItem, ItemTemplate, MerchantItem), exported by "
                  "source/tools/export_recipe_fixture.py for the economy rehearsal. World data only; no player data."),
        "source": source_label,
        "recipeCount": len(recipes),
        "hiddenCount": sum(hidden.values()),
        "hidden": dict(sorted(hidden.items())),
        "recipesBySkill": dict(sorted(counts.items())),
        "merchantGearPriceByLevel": ladder,
        "materials": dict(sorted(materials.items())),
        "recipes": recipes,
    }


def write_fixture(fixture, out):
    """One recipe and one material per line, so a re-export diffs by recipe."""
    out = Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    head = {k: v for k, v in fixture.items() if k not in ("materials", "recipes")}
    lines = ["{"]
    for key, value in head.items():
        lines.append(f"  {json.dumps(key)}: {json.dumps(value, ensure_ascii=False, sort_keys=False)},")
    lines.append('  "materials": {')
    items = list(fixture["materials"].items())
    for index, (key, value) in enumerate(items):
        comma = "," if index < len(items) - 1 else ""
        lines.append(f"    {json.dumps(key)}: {json.dumps(value, ensure_ascii=False, separators=(',', ':'))}{comma}")
    lines.append("  },")
    lines.append('  "recipes": [')
    for index, recipe in enumerate(fixture["recipes"]):
        comma = "," if index < len(fixture["recipes"]) - 1 else ""
        lines.append(f"    {json.dumps(recipe, ensure_ascii=False, separators=(',', ':'))}{comma}")
    lines.append("  ]")
    lines.append("}")
    out.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return out


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("database", help="a COPY of a world database (runtime/data/opendaoc.sqlite3.db)")
    parser.add_argument("--out", default=str(DEFAULT_OUT), help="the fixture to write (default: the rehearsal's)")
    parser.add_argument("--label", default="world database", help="what the source was, written into the fixture")
    parser.add_argument("--check", action="store_true", help="compare with --out instead of writing; exit 1 if different")
    args = parser.parse_args(argv)
    check_tables()
    connection = open_read_only(args.database)
    try:
        fixture = build_fixture(connection, args.label)
    finally:
        connection.close()
    if args.check:
        try:
            current = json.loads(Path(args.out).read_text(encoding="utf-8"))
        except (OSError, ValueError):
            current = None
        same = current is not None and {k: v for k, v in current.items() if k != "source"} == \
            {k: v for k, v in fixture.items() if k != "source"}
        print("fixture up to date" if same else "fixture differs from the database")
        return 0 if same else 1
    path = write_fixture(fixture, args.out)
    print(f"wrote {path}: {fixture['recipeCount']} recipes ({fixture['hiddenCount']} hidden), "
          f"{len(fixture['materials'])} materials, {len(fixture['merchantGearPriceByLevel'])} ladder levels")
    return 0


if __name__ == "__main__":
    sys.exit(main())
