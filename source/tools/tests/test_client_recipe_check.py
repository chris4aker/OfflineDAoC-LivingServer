"""Tests of client_recipe_check.py (#580): it only reads its inputs, reads craft_extract's CSV, JSON and SQLite output
the same way (a comma inside a name included), and sorts each client recipe as same, missing, hidden or differing in
name, craft, level or realm, with the world's own extra recipes counted apart.

The client side here is synthetic, written in the layout craft_extract's save_csv, save_json and save_sqlite produce;
no client data and no craft_extract code is used.

    python -m unittest discover -s source/tools/tests -v
"""
import hashlib
import json
import sqlite3
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import client_recipe_check as crc  # noqa: E402

WORLD = """
CREATE TABLE crafteditem (CraftedItemID TEXT, Id_nb TEXT, CraftingLevel INT, CraftingSkillType INT, MakeTemplated INT);
CREATE TABLE craftedxitem (CraftedItemId_nb TEXT, IngredientId_nb TEXT, Count INT);
CREATE TABLE itemtemplate (Id_nb TEXT, Name TEXT, Level INT, Quality INT, DPS_AF INT, SPD_ABS INT, Hand INT,
    Object_Type INT, Item_Type INT, Model INT, Price INT, PackSize INT, MaxCount INT, Realm INT, MaxCondition INT);
CREATE TABLE merchantitem (ItemListID TEXT, ItemTemplateID TEXT);
CREATE TABLE mob (ClassType TEXT, ItemsListTemplateID TEXT);
"""
TEMPLATES = [  # Id_nb, Name, Realm, Object_Type, Item_Type
    ("bronze_bars", "bronze metal bars", 0, 0, 0),
    ("strength_herb", "crushed herb, fine", 0, 0, 0),
    ("bronze_short_sword", "bronze short sword", 1, 3, 10),
    ("iron_short_sword", "iron short sword", 1, 3, 10),
    ("iron_broad_sword", "iron broad sword", 1, 3, 10),
    ("steel_short_sword", "steel short sword", 1, 3, 10),
    ("cloth_cap", "woolen cap", 1, 32, 21),
    ("hib_mace", "bronze mace", 3, 12, 10),
    ("alloy_short_sword", "alloy short sword", 1, 3, 10),
    ("minor_strength", "Potion of Strength, Minor", 1, 0, 40),
]
RECIPES = [  # id, product, level, skill
    (101, "bronze_short_sword", 15, 1),
    (103, "missing_product", 20, 1),
    (104, "iron_broad_sword", 115, 1),
    (105, "steel_short_sword", 100, 1),
    (106, "cloth_cap", 30, 2),
    (107, "hib_mace", 15, 1),
    (108, "alloy_short_sword", 215, 1),
    (110, "minor_strength", 40, 4),
    (150, "iron_short_sword", 115, 1),
]
INGREDIENTS = [(p, "bronze_bars", 4) for _, p, _, _ in RECIPES if p not in ("missing_product", "minor_strength")]
INGREDIENTS += [("minor_strength", "strength_herb", 2)]


def client_recipes():
    """The client's recipes as craft_extract's JSON has them: realm -> [recipe]. base_material names prefix the name."""
    def r(rid, prof, base, base_name, name, skill, level=0, mats=(("bronze", 7, 4, "metal bars"),)):
        return {"profession": prof, "category": "Swords", "name": name, "base_material_name": base_name,
                "base_material": base, "icon": 1, "id": rid, "level": level, "material_level": 1, "skill": skill,
                "materials": [{"base_material_name": bn, "base_material": b, "count": c, "name": n} for bn, b, c, n in
                              [(m[0], m[1], m[2], m[3]) for m in mats]]}
    return {
        "Albion": [
            r(101, "Weaponcrafting", 7, "bronze", "short sword", 15),
            r(101, "Weaponcrafting", 7, "bronze", "short sword", 15),  # listed in a second category
            r(102, "Weaponcrafting", 8, "iron", "short sword", 115),   # world has it as 150
            r(103, "Weaponcrafting", 7, "bronze", "long sword", 20),
            r(104, "Weaponcrafting", 8, "iron", "long sword", 115),
            r(105, "Weaponcrafting", 15, "steel", "short sword", 115),
            r(106, "Tailoring", 0, "", "woolen cap", 30),
            r(107, "Weaponcrafting", 7, "bronze", "mace", 15),
            r(108, "Weaponcrafting", 14, "alloy", "short sword", 215, mats=(("bronze", 7, 3, "metal bars"),)),
            r(109, "Basketweaving", 0, "", "basket", 1),
            r(110, "Alchemy", 0, "", "Potion of Strength, Minor", 40, mats=(("", 0, 2, "crushed herb, fine"),)),
        ],
        "Midgard": [],
        "Hibernia": [],
    }


def write_csv(path, data):
    """As craft_extract's save_csv: a ", " header, then unquoted rows; commas inside names are not escaped."""
    head = ("id, realm, realm_name, profession, category, name, base_material, base_material_name, icon, level, "
            "material_level, skill" + "".join(f", mat{i}_base_material, mat{i}_base_material_name, mat{i}_count, "
                                              f"mat{i}_name" for i in range(1, 9)))
    lines = [head]
    for realm, items in data.items():
        for r in items:
            row = [r["id"], crc.REALMS.index(realm), realm, r["profession"], r["category"], r["name"], r["base_material"],
                   r["base_material_name"], r["icon"], r["level"], r["material_level"], r["skill"]]
            for m in r["materials"]:
                row += [m["base_material"], m["base_material_name"], m["count"], m["name"]]
            lines.append(",".join(str(x) for x in row))
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_sqlite(path, data):
    """As craft_extract's save_sqlite (its tables and columns; the about table left empty)."""
    db = sqlite3.connect(path)
    db.executescript("""
        CREATE TABLE about_craft_extract (created_by TEXT, paypal TEXT, github TEXT, patreon TEXT, repo TEXT);
        CREATE TABLE base_materials (id INT, name TEXT);
        CREATE TABLE realms (id INT, name TEXT);
        CREATE TABLE recipes (id INT, realm_id INT, profession TEXT, category TEXT, name TEXT, base_material INT, icon INT,
            level INT, material_level INT, skill INT);
        CREATE TABLE recipes_materials (recipe_id INT, base_material INT, count INT, name TEXT);
    """)
    bases = {}
    for realm, items in data.items():
        db.execute("INSERT INTO realms VALUES (?, ?)", (crc.REALMS.index(realm), realm))
        for r in items:
            bases[r["base_material"]] = r["base_material_name"]
            db.execute("INSERT INTO recipes VALUES (?,?,?,?,?,?,?,?,?,?)",
                       (r["id"], crc.REALMS.index(realm), r["profession"], r["category"], r["name"], r["base_material"],
                        r["icon"], r["level"], r["material_level"], r["skill"]))
            for m in r["materials"]:
                bases[m["base_material"]] = m["base_material_name"]
                db.execute("INSERT INTO recipes_materials VALUES (?,?,?,?)",
                           (r["id"], m["base_material"], m["count"], m["name"]))
    db.executemany("INSERT INTO base_materials VALUES (?, ?)", sorted(bases.items()))
    db.commit()
    db.close()


def write_mpk(path, entries):
    """A small MPAK archive in the layout daoc_mpk.py reads (XOR-masked header, zlib name, directory and entries)."""
    import zlib
    blobs, directory, offset = b"", b"", 0
    for name, data in entries.items():
        packed = zlib.compress(data)
        directory += name.encode("latin1").ljust(256, b"\0") + struct.pack("<7I", 0, 0, 0, len(data), offset,
                                                                            len(packed), 0)
        blobs += packed
        offset += len(packed)
    name = zlib.compress(b"ifd.mpk\0")
    directory = zlib.compress(directory)
    header = bytes(b ^ i for i, b in enumerate(struct.pack("<4I", 0, len(directory), len(name), len(entries))))
    Path(path).write_bytes(b"MPAK" + b"\0" + header + name + directory + blobs)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.world = root / "world.db"
        db = sqlite3.connect(self.world)
        db.executescript(WORLD)
        for t in TEMPLATES:
            db.execute("INSERT INTO itemtemplate (Id_nb, Name, Realm, Object_Type, Item_Type) VALUES (?,?,?,?,?)", t)
        db.executemany("INSERT INTO crafteditem VALUES (?,?,?,?,0)", RECIPES)
        db.executemany("INSERT INTO craftedxitem VALUES (?,?,?)", INGREDIENTS)
        db.commit()
        db.close()
        self.data = client_recipes()
        self.json = root / "crafts.json"
        self.json.write_text(json.dumps(self.data, indent=2), encoding="utf-8")
        self.csv = root / "crafts.csv"
        write_csv(self.csv, self.data)
        self.sqlite = root / "crafts.sqlite"
        write_sqlite(self.sqlite, self.data)
        self.crf = root / "tdl.crf"
        self.crf.write_bytes(struct.pack("<I", 0x67) + b"\0" * 60)
        self.root = root

    def tearDown(self):
        self.tmp.cleanup()

    def report(self, path, **kw):
        client, unparsed, fmt = crc.read_client(path)
        report = crc.diff(client, crc.read_world(self.world), **kw)
        report["unparsed"] = unparsed
        report["format"] = fmt
        return report


class ReadOnly(Fixture):
    def test_inputs_are_never_written(self):
        before = {p: digest(p) for p in (self.world, self.json, self.csv, self.sqlite, self.crf)}
        out = self.root / "out"
        out.mkdir()
        for client in (self.json, self.csv, self.sqlite):
            self.assertEqual(crc.main(["--client", str(client), "--world", str(self.world), "--crf", str(self.crf),
                                       "--report", str(out / "r.json"), "--markdown", str(out / "r.md")]), 0)
        self.assertEqual(before, {p: digest(p) for p in before})
        self.assertFalse(Path(str(self.world) + "-wal").exists())

    def test_mpk_entry_read_in_memory(self):
        mpk = self.root / "ifd.mpk"
        write_mpk(mpk, {"other.dat": b"x" * 10, "tdl.crf": self.crf.read_bytes()})
        before = digest(mpk)
        info = crc.mpk_crf_info(mpk)
        self.assertEqual((info["version"], info["sha256"]), ("v67", digest(self.crf)))
        self.assertEqual(info["archive_sha256"], before)
        self.assertEqual(digest(mpk), before)
        self.assertEqual(sorted(p.name for p in self.root.iterdir()),
                         ["crafts.csv", "crafts.json", "crafts.sqlite", "ifd.mpk", "tdl.crf", "world.db"])

    def test_full_report_refused_inside_the_repository(self):
        inside = crc.REPO / "docs" / "research" / "client-recipes.json"
        with self.assertRaises(SystemExit):
            crc.main(["--client", str(self.json), "--world", str(self.world), "--report", str(inside)])
        self.assertFalse(inside.exists())

    def test_crf_header(self):
        info = crc.crf_info(self.crf)
        self.assertEqual(info["version"], "v67")
        self.assertEqual(info["sha256"], digest(self.crf))
        self.crf.write_bytes(struct.pack("<I", 0x42))
        self.assertTrue(crc.crf_info(self.crf)["version"].startswith("unknown"))


class Readers(Fixture):
    def test_three_formats_read_alike(self):
        def key(rows):
            return sorted((r["realm"], r["id"], r["profession"], r["name"], r["base_material_name"], r["skill"],
                           tuple(sorted(r["materials"]))) for r in rows)
        j, _, fj = crc.read_client(self.json)
        c, unparsed, fc = crc.read_client(self.csv)
        s, _, fs = crc.read_client(self.sqlite)
        self.assertEqual((fj, fc, fs), ("json", "csv", "sqlite"))
        self.assertEqual(unparsed, [])
        self.assertEqual(key(j), key(c))
        # recipe 101 is listed twice (two categories): its materials are written twice and read back once
        self.assertEqual(key(s), key(j))

    def test_csv_comma_in_names(self):
        rows, unparsed = crc.read_client_csv(self.csv.read_text(encoding="utf-8"))
        potion = next(r for r in rows if r["id"] == 110)
        self.assertEqual(potion["name"], "Potion of Strength, Minor")
        self.assertEqual(potion["skill"], 40)
        self.assertEqual(potion["materials"], [("crushed herb, fine", 2)])
        self.assertEqual(unparsed, [])

    def test_csv_bad_rows_reported(self):
        rows, unparsed = crc.read_client_csv("id, realm\n1,0,Albion,Alchemy\nnot,a,row,at,all,x,y,z,1,2,3,4\n")
        self.assertEqual(rows, [])
        self.assertEqual(unparsed, [2, 3])

    def test_sqlite_id_in_two_realms_has_unknown_materials(self):
        self.data["Midgard"] = [dict(self.data["Albion"][0])]
        write_sqlite(self.root / "two.sqlite", self.data)
        rows = crc.read_client_sqlite(self.root / "two.sqlite")
        self.assertTrue(all(r["materials"] is None for r in rows if r["id"] == 101))

    def test_professions(self):
        self.assertEqual(crc.profession_skill("Weaponcrafting"), 1)
        self.assertEqual(crc.profession_skill("Armor Crafting"), 2)
        self.assertEqual(crc.profession_skill("Spellcrafting"), 13)
        self.assertIsNone(crc.profession_skill("Basketweaving"))


class Diff(Fixture):
    def check(self, report):
        self.assertEqual(report["client"]["recipes"], 10)
        self.assertEqual(report["client"]["duplicates"], 1)
        self.assertEqual(report["client"]["unknown_professions"], {"Basketweaving": 1})
        c = report["counts"]
        self.assertEqual(c["same"], 3, report["rows"])  # 101, 108 (materials only), 110
        self.assertEqual(c["missing"], 2)               # 102, 109
        self.assertEqual(c["hidden"], 1)                # 103
        self.assertEqual(c["name"], 1)                  # 104
        self.assertEqual(c["level"], 1)                 # 105
        self.assertEqual(c["craft"], 1)                 # 106
        self.assertEqual(c["realm"], 1)                 # 107
        self.assertEqual(report["materials_differ"], 1)  # 108
        rows = report["rows"]
        moved = next(r for r in rows["missing"] if r["id"] == 102)
        self.assertEqual(moved["same_name_elsewhere"], [150])
        self.assertEqual(rows["hidden"][0]["hidden"], "ProductMissing")
        self.assertEqual(rows["name"][0]["world_name"], "iron broad sword")
        self.assertEqual((rows["level"][0]["client_level"], rows["level"][0]["world_level"]), (115, 100))
        self.assertEqual(rows["craft"][0]["world_craft"], "ArmorCrafting")
        self.assertEqual(rows["realm"][0]["world_realm"], 3)
        self.assertEqual(report["world_only"]["recipes"], 1)
        self.assertEqual(report["world_only_rows"][0]["id"], 150)

    def test_json(self):
        self.check(self.report(self.json))

    def test_csv(self):
        self.check(self.report(self.csv))

    def test_sqlite(self):
        self.check(self.report(self.sqlite))

    def test_level_field_and_calibration(self):
        report = self.report(self.json)
        cal = report["level_calibration"]
        self.assertEqual(cal["usable_matches"], 7)
        self.assertEqual(cal["skill"], 6)  # every usable match but 105
        self.assertEqual(cal["level"], 0)
        other = self.report(self.json, level_field="level")
        self.assertEqual(other["counts"]["level"], 7)

    def test_markdown_names_and_counts(self):
        report = self.report(self.json)
        report["source"] = {"client_build": "test build", "client_format": "json"}
        md = crc.markdown(report, limit=1)
        self.assertIn("Client build: test build", md)
        self.assertIn("## missing (2)", md)
        self.assertIn("same name and craft in the world as 150", md)
        self.assertIn("... and 1 more", md)
        self.assertNotIn("metal bars", md)  # no materials or other client fields beyond names and levels


if __name__ == "__main__":
    unittest.main()
