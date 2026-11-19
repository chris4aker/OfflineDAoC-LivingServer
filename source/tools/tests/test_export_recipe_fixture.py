"""Tests for export_recipe_fixture.py (Crafting 2.0 WP10): world tables only, case-insensitive keys, M0's hidden rules.

    python3 -m unittest discover -s source/tools/tests -v
"""
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import export_recipe_fixture as exp  # noqa: E402

FIXTURE = TOOLS.parent / "server" / "Tests" / "Rehearsal" / "fixtures" / "recipes.json"


def make_world(path):
    db = sqlite3.connect(path)
    db.executescript("""
        CREATE TABLE crafteditem (CraftedItemID TEXT, Id_nb TEXT, CraftingLevel INT, CraftingSkillType INT, MakeTemplated INT);
        CREATE TABLE craftedxitem (CraftedItemId_nb TEXT, IngredientId_nb TEXT, Count INT);
        CREATE TABLE itemtemplate (Id_nb TEXT, Name TEXT, Level INT, Quality INT, DPS_AF INT, SPD_ABS INT, Hand INT,
            Object_Type INT, Item_Type INT, Model INT, Price INT, PackSize INT, MaxCount INT, Realm INT, MaxCondition INT);
        CREATE TABLE merchantitem (ItemListID TEXT, ItemTemplateID TEXT);
        CREATE TABLE mob (ClassType TEXT, ItemsListTemplateID TEXT);
        CREATE TABLE inventory (OwnerID TEXT, ITemplate_Id TEXT);
        INSERT INTO inventory VALUES ('player-secret', 'bronze_sword');
        INSERT INTO itemtemplate VALUES ('Bronze_Sword', 'bronze sword', 5, 85, 22, 37, 0, 3, 10, 1, 900, 1, 1, 1, 50000);
        INSERT INTO itemtemplate VALUES ('bronze_metal_bars', 'bronze metal bars', 0, 85, 0, 0, 0, 0, 0, 519, 40, 1, 1, 0, 50000);
        INSERT INTO itemtemplate VALUES ('shell_hauberk', 'shell hauberk', 40, 85, 0, 0, 0, 0, 25, 1, 9000, 1, 1, 2, 50000);
        INSERT INTO itemtemplate VALUES ('seal_helm', 'seal helm', 40, 85, 80, 0, 0, 35, 21, 1, 20, 1, 1, 0, 50000);
        INSERT INTO itemtemplate VALUES ('merchant_helm', 'merchant helm', 10, 85, 20, 0, 0, 35, 21, 1, 4800, 1, 1, 0, 50000);
        INSERT INTO crafteditem VALUES ('3', 'bronze_sword', 15, 1, 0);
        INSERT INTO crafteditem VALUES ('2', 'shell_hauberk', 400, 2, 0);
        INSERT INTO crafteditem VALUES ('1', 'no_such_product', 10, 1, 0);
        INSERT INTO crafteditem VALUES ('4', 'bronze_metal_bars', 10, 6, 1);
        INSERT INTO craftedxitem VALUES ('BRONZE_SWORD', 'Bronze_Metal_Bars', 4);
        INSERT INTO craftedxitem VALUES ('shell_hauberk', 'bronze_metal_bars', 10);
        INSERT INTO mob VALUES ('DOL.GS.GameMerchant', 'smith');
        INSERT INTO mob VALUES ('DOL.GS.GameDiamondSealsMerchant', 'seals');
        INSERT INTO merchantitem VALUES ('smith', 'bronze_metal_bars');
        INSERT INTO merchantitem VALUES ('smith', 'merchant_helm');
        INSERT INTO merchantitem VALUES ('seals', 'seal_helm');
    """)
    db.commit()
    db.close()


class ExportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.db = Path(self.tmp.name) / "world.db"
        make_world(self.db)

    def tearDown(self):
        self.tmp.cleanup()

    def build(self):
        connection = exp.open_read_only(self.db)
        try:
            return exp.build_fixture(connection, "test world")
        finally:
            connection.close()

    def test_keys_match_without_case_and_problems_are_marked(self):
        fixture = self.build()
        by_id = {r["id"]: r for r in fixture["recipes"]}
        self.assertEqual([r["id"] for r in fixture["recipes"]], [1, 2, 3, 4])
        sword = by_id[3]
        self.assertNotIn("hidden", sword)
        self.assertEqual(sword["materials"], [["bronze_metal_bars", 4]])
        self.assertEqual(sword["dpsAf"], 22)
        self.assertNotIn("quality", sword)          # the usual 85 is left out
        self.assertEqual(by_id[1]["hidden"], "ProductMissing")
        self.assertEqual(by_id[2]["hidden"], "ProductUnusable")
        self.assertEqual(by_id[4]["hidden"], "NoIngredients")
        self.assertEqual(fixture["hiddenCount"], 3)
        self.assertTrue(fixture["materials"]["bronze_metal_bars"]["sold"])
        self.assertTrue(fixture["materials"]["bronze_metal_bars"]["crafted"])

    def test_the_gear_ladder_counts_coin_merchants_only(self):
        self.assertEqual(self.build()["merchantGearPriceByLevel"], {"10": 4800})

    def test_no_player_data_reaches_the_fixture(self):
        text = json.dumps(self.build())
        self.assertNotIn("player-secret", text)
        self.assertNotIn("inventory", exp.WORLD_TABLES)
        with self.assertRaises(ValueError):
            list(exp.read(exp.open_read_only(self.db), "inventory", ["OwnerID"]))

    def test_a_player_table_in_the_list_is_refused(self):
        policy = Path(self.tmp.name) / "policy.json"
        policy.write_text(json.dumps({"ProgressTables": ["Inventory", "DOLCharacters"]}), encoding="utf-8")
        exp.check_tables(exp.WORLD_TABLES, policy)  # the real list is fine
        with self.assertRaises(SystemExit):
            exp.check_tables(exp.WORLD_TABLES + ("inventory",), policy)
        self.assertTrue({"account", "dolcharacters"} <= exp.player_tables())

    def test_the_database_is_opened_read_only(self):
        connection = exp.open_read_only(self.db)
        with self.assertRaises(sqlite3.OperationalError):
            connection.execute("DELETE FROM crafteditem")
        connection.close()

    def test_write_and_check_round_trip(self):
        out = Path(self.tmp.name) / "recipes.json"
        exp.write_fixture(self.build(), out)
        self.assertEqual(json.loads(out.read_text(encoding="utf-8"))["recipeCount"], 4)
        self.assertEqual(exp.main([str(self.db), "--out", str(out), "--check"]), 0)


class CommittedFixtureTests(unittest.TestCase):
    def test_the_committed_fixture_is_world_data_with_m0s_counts(self):
        fixture = json.loads(FIXTURE.read_text(encoding="utf-8"))
        self.assertEqual(fixture["recipeCount"], 8479)
        self.assertEqual(fixture["hidden"], {"NoIngredients": 2, "ProductUnusable": 38})
        self.assertEqual(set(fixture), {"about", "source", "recipeCount", "hiddenCount", "hidden", "recipesBySkill",
                                        "merchantGearPriceByLevel", "materials", "recipes"})
        allowed = {"id", "skill", "level", "product", "name", "objectType", "itemType", "productLevel", "realm", "price",
                   "quality", "dpsAf", "spdAbs", "hand", "maxCondition", "stackable", "templated", "materials", "hidden"}
        for recipe in fixture["recipes"]:
            self.assertLessEqual(set(recipe), allowed)


if __name__ == "__main__":
    unittest.main()
