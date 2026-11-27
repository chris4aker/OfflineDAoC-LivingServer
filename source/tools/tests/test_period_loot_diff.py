"""Tests of period_loot_diff.py (#575): it only reads the world, sorts each monster's loot state right (an empty loot table
is not loot), counts drop rows, prices, dye merchants, Thidranki sellers and Moras items as missing, different or same.

    python -m unittest discover -s source/tools/tests -v
"""
import csv
import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import period_loot_diff as pld  # noqa: E402

SCHEMA = """
CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT, Region INT, Realm INT, ClassType TEXT, ItemsListTemplateID TEXT);
CREATE TABLE NpcTemplate (NpcTemplate_ID TEXT PRIMARY KEY, Name TEXT);
CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT, Realm INT, Level INT, Bonus INT, DPS_AF INT, SPD_ABS INT,
    Price INT, Bonus1Type INT, Bonus1 INT, Bonus2Type INT, Bonus2 INT, Bonus3Type INT, Bonus3 INT, Bonus4Type INT, Bonus4 INT,
    Bonus5Type INT, Bonus5 INT, Bonus6Type INT, Bonus6 INT, Bonus7Type INT, Bonus7 INT, Bonus8Type INT, Bonus8 INT,
    Bonus9Type INT, Bonus9 INT, Bonus10Type INT, Bonus10 INT, ExtraBonusType INT, ExtraBonus INT);
CREATE TABLE LootTemplate (LootTemplate_ID TEXT PRIMARY KEY, TemplateName TEXT, ItemTemplateID TEXT, Chance INT);
CREATE TABLE MobXLootTemplate (MobXLootTemplate_ID TEXT PRIMARY KEY, MobName TEXT, LootTemplateName TEXT, DropCount INT);
CREATE TABLE MerchantItem (MerchantItem_ID TEXT PRIMARY KEY, ItemListID TEXT, ItemTemplateID TEXT);
CREATE TABLE Regions (RegionID INT PRIMARY KEY, Name TEXT, Description TEXT);
"""


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.world = root / "world.db"
        db = sqlite3.connect(self.world)
        db.executescript(SCHEMA)
        db.executemany("INSERT INTO Mob VALUES (?,?,?,?,?,?)", [
            ("m1", "Olen den Bartender", 150, 0, "DOL.GS.GameNPC", None),
            ("m2", "bwca", 1, 0, "DOL.GS.GameNPC", None),
            ("m3", "Snemel den Forsumlig ", 150, 0, "DOL.GS.GameNPC", None),
            ("m4", "Siegemaster Cadman", 252, 1, "DOL.GS.GameMerchant", "SiegeMerchantAlb"),
            ("m5", "Captain Circa", 252, 1, "DOL.GS.GameNPC", None),
            ("m6", "pike", 252, 0, "DOL.GS.GameNPC", None),
        ])
        db.execute("INSERT INTO NpcTemplate VALUES ('t1', 'goblin imperator')")
        items = [
            ("Olens_Mug", "Olen's Mug", 2, 48, 35, 0, 0, 100, 1, 10),
            ("Bwca_Tooth", "Bwca Tooth", 0, 1, 0, 0, 0, 5, None, None),
            ("Bwca_Ring", "Bwca Ring", 0, 10, 0, 0, 0, 5, None, None),
            ("light_blue_cloth_dye", "Light Blue Cloth Dye", 0, 1, 0, 0, 0, 50, None, None),
            ("blue_cloth_dye", "Blue Cloth Dye", 0, 1, 0, 0, 0, 2500, None, None),
            ("Dragon_Bracer", "Dragon Bracer", 2, 51, 35, 0, 0, 1, 2, 15),
            ("Dragon_Hammer", "Dragon Hammer", 2, 51, 35, 165, 38, 1, 1, 20),
            ("siege_ram", "siege ram", 1, 1, 0, 0, 0, 1, None, None),
        ]
        for i in items:
            db.execute("INSERT INTO ItemTemplate (Id_nb, Name, Realm, Level, Bonus, DPS_AF, SPD_ABS, Price, Bonus1Type, Bonus1) "
                       "VALUES (?,?,?,?,?,?,?,?,?,?)", i)
        db.executemany("INSERT INTO LootTemplate VALUES (?,?,?,?)", [
            ("l1", "bwca", "Bwca_Tooth", 30), ("l2", "bwca", "Bwca_Ring", 5)])
        db.executemany("INSERT INTO MobXLootTemplate VALUES (?,?,?,?)", [
            ("x1", "bwca", "bwca", 1), ("x2", "Olen den Bartender", "Olen den Bartender", 1)])
        db.execute("INSERT INTO MerchantItem VALUES ('mi1', 'SiegeMerchantAlb', 'siege_ram')")
        db.execute("INSERT INTO MerchantItem VALUES ('mi2', 'capital_dyes', 'blue_cloth_dye')")
        db.executemany("INSERT INTO Regions VALUES (?,?,?)", [(252, "Region252", "Thidranki"), (1, "Region001", "Albion")])
        db.commit()
        db.close()
        self.digest = hashlib.sha256(self.world.read_bytes()).hexdigest()

        t165 = root / "t165" / "runtime" / "true165"
        t165.mkdir(parents=True)
        (t165 / "drops165.json").write_text(json.dumps({"monsters": {
            "Olen den Bartender": {"drop_count": 2, "items": [["Olens_Mug", 25], ["Unknown_Mug", 25]]},
            "bwca": {"drop_count": 1, "items": [["Bwca_Tooth", 30], ["Bwca_Ring", 7], ["Bwca_Hide", 10]]},
            "Snemel den Forsumlig ": {"drop_count": 1, "items": [["Olens_Mug", 25]]},
            "goblin imperator": {"drop_count": 1, "items": [["Bwca_Ring", 4]]},
            "Remandun den Laerd": {"drop_count": 1, "items": [["Olens_Mug", 25]]},
        }}))
        (t165 / "add-trollheim165.json").write_text(json.dumps({"rows": [
            {"Name": "Remandun den Laerd", "Region": 150, "Level": 69}]}))
        (t165 / "add-inn-dye-mob165.json").write_text(json.dumps({"rows": [
            {"Name": "Maerwynn Dyeford", "Region": 1, "ItemsListTemplateID": "inn_alb_dye"}]}))
        (t165 / "add-inn-dye-merchantitem165.json").write_text(json.dumps({"rows": [
            {"ItemListID": "inn_alb_dye", "ItemTemplateID": "blue_cloth_dye"},
            {"ItemListID": "inn_alb_dye", "ItemTemplateID": "no_such_dye"}]}))
        tools = root / "t165" / "development-source" / "server" / "tools"
        tools.mkdir(parents=True)
        with (tools / "period_alchemy_prices.csv").open("w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["recipe_id", "item_id", "old_price", "period_price"])
            w.writerow(["6", "light_blue_cloth_dye", "50", "100"])
            w.writerow(["17", "blue_cloth_dye", "1375", "2500"])
            w.writerow(["", "no_such_item", "1", "2"])
        self.t165 = root / "t165"

        data = root / "dbp" / "data"
        data.mkdir(parents=True)
        (data / "Mob.0.json").write_text(json.dumps([
            {"Name": "Geledh", "Region": 238, "Realm": 1, "ClassType": "DOL.GS.GameMerchant", "ItemsListTemplateID": "ThidAlb"},
            {"Name": "Fighter", "Region": 238, "Realm": 1, "ClassType": "DOL.GS.Keeps.GuardFighter"},
            {"Name": "trader", "Region": 1, "Realm": 1, "ClassType": "DOL.GS.GameMerchant", "ItemsListTemplateID": "x"},
        ]))
        (data / "MerchantItem.json").write_text(json.dumps([
            {"ItemListID": "ThidAlb", "ItemTemplateID": "Alb_Thidranki_Longsword"},
            {"ItemListID": "ThidAlb", "ItemTemplateID": "siege_ram"}]))
        (data / "Regions.json").write_text(json.dumps([{"RegionID": 238, "Name": "Region238", "Description": "Thidranki"}]))
        self.dbp = root / "dbp"

        self.moras = root / "items.xml"
        self.moras.write_bytes("""<?xml version="1.0" encoding="iso-8859-1" ?>
<daoc_items>
<item><name>Dragon Bracer (M)</name><origin>Zone: Malmohus
Mob: Gjalpinulva</origin><level>50</level><bonus>35</bonus><effect id="DEXTERITY">15</effect></item>
<item><name>Dragon Hammer (M)</name><origin>Zone: Malmohus
Mob: Gjalpinulva</origin><level>51</level><bonus>35</bonus><weapon dps="16,5" speed="3,8">HAMMER</weapon>
<effect id="STRENGTH">25</effect></item>
<item><name>Lost Ring (M)</name><origin>Zone: Darkness Falls.
Mob: Legion</origin><level>50</level><bonus>35</bonus></item>
<item><name>Field Cap (M)</name><origin>Zone: Gotar</origin><level>20</level></item>
</daoc_items>""".encode("iso-8859-1"))

    def tearDown(self):
        self.tmp.cleanup()

    def run_tool(self, **extra):
        args = ["--world", str(self.world), "--true165", str(self.t165), "--dbpublic", str(self.dbp),
                "--moras", str(self.moras)]
        out = Path(self.tmp.name) / "report.json"
        args += ["--report", str(out)]
        self.assertEqual(pld.main(args), 0)
        return json.loads(out.read_text(encoding="utf-8"))


class TestPeriodLootDiff(Fixture):
    def test_never_changes_the_world(self):
        self.run_tool()
        self.assertEqual(hashlib.sha256(self.world.read_bytes()).hexdigest(), self.digest)

    def test_world_is_opened_read_only(self):
        db = pld.open_world(self.world)
        with self.assertRaises(sqlite3.OperationalError):
            db.execute("DELETE FROM Mob")

    def test_loot_states(self):
        world = pld.World(pld.open_world(self.world))
        self.assertEqual(world.loot_state("bwca"), "has_named_loot")
        # a MobXLootTemplate row whose template has no item is not loot
        self.assertEqual(world.loot_state("Olen den Bartender"), "has_empty_loot_table")
        # names match without the trailing space the world and True 1.65 both carry
        self.assertEqual(world.loot_state("Snemel den Forsumlig"), "has_no_loot_rows")
        self.assertEqual(world.loot_state("goblin imperator"), "has_no_loot_rows")  # an NpcTemplate only
        self.assertEqual(world.loot_state("Remandun den Laerd"), "not_in_world")

    def test_drop_rows(self):
        r = self.run_tool()
        d = r["drops"]
        self.assertEqual(d["monsters"], {"has_named_loot": 1, "has_empty_loot_table": 1, "has_no_loot_rows": 2,
                                         "not_in_world": 1})
        self.assertEqual(d["rows"]["same"], 1)               # Bwca_Tooth 30
        self.assertEqual(d["rows"]["different_chance"], 1)   # Bwca_Ring 5 vs 7
        self.assertEqual(d["rows"]["missing"], 1)            # Bwca_Hide
        self.assertEqual(d["rows"].get("ours_only", 0), 0)
        self.assertEqual(d["rows"]["has_empty_loot_table:item_in_world"], 1)
        self.assertEqual(d["rows"]["has_empty_loot_table:item_not_in_world"], 1)
        self.assertEqual(d["spawned"]["has_no_loot_rows:template_only"], 1)

    def test_focus_lists_the_issue_monsters(self):
        r = self.run_tool()
        olen = next(f for f in r["focus"] if f["monster"] == "Olen den Bartender")
        self.assertEqual(olen["state"], "has_empty_loot_table")
        self.assertEqual(olen["spawns"], 1)
        self.assertEqual([x["item_in_world"] for x in olen["true165"]], [True, False])
        self.assertEqual(len(r["focus"]), len(pld.FOCUS))

    def test_spawns_merchants_prices(self):
        r = self.run_tool()
        self.assertEqual([s["name"] for s in r["spawns"]["missing"]], ["Remandun den Laerd"])
        m = r["merchants"]
        self.assertFalse(m["merchants"][0]["in_world"])
        self.assertEqual((m["list_rows"], m["items_missing"], m["world_lists_selling_these_items"]), (2, 1, 1))
        p = r["prices"]["items"]
        self.assertEqual(p.get("different:ours_is_true165_old_price"), 1)  # light blue: ours 50 = old
        self.assertEqual(p.get("different:other_price"), None)
        self.assertEqual(p.get("same"), 1)                                   # blue: ours 2500 = period
        self.assertEqual(p.get("missing"), 1)

    def test_thidranki_compares_by_region_description(self):
        t = self.run_tool()["thidranki"]
        self.assertEqual(t["world_regions"], [252])
        self.assertEqual(t["dbpublic_regions"], [238])
        self.assertEqual([x["name"] for x in t["dbpublic"]], ["Geledh"])        # sellers only, no guards
        self.assertEqual(t["dbpublic"][0]["items_in_world"], 1)
        self.assertEqual([x["name"] for x in t["world"]], ["Siegemaster Cadman"])

    def test_moras_audit(self):
        mo = self.run_tool()["moras"]
        self.assertEqual(mo["zones"]["Malmohus"], {"same": 1, "different": 1})
        self.assertEqual(mo["zones"]["Darkness Falls"], {"missing": 1})
        self.assertNotIn("Gotar", mo["zones"])
        hammer = next(x for x in mo["rows"] if x["item"].startswith("Dragon Hammer"))
        # level 51 against 51 and DPS/speed agree; only the bonus value differs
        self.assertEqual(hammer["fields"], ["bonuses"])

    def test_moras_level_cap_is_not_a_difference(self):
        m = {"level": "50", "bonus": None, "af": "100", "dps": None, "speed": None, "effects": []}
        self.assertEqual(pld.moras_differences(m, {"Level": 51, "DPS_AF": 102}), [])
        self.assertEqual(pld.moras_differences(dict(m, level="45"), {"Level": 51, "DPS_AF": 102}), ["level"])

    def test_sections_are_optional(self):
        out = Path(self.tmp.name) / "only.json"
        self.assertEqual(pld.main(["--world", str(self.world), "--report", str(out)]), 0)
        self.assertEqual(set(json.loads(out.read_text(encoding="utf-8"))), {"world"})


if __name__ == "__main__":
    unittest.main()
