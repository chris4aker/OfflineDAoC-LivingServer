"""fix_broken_recipes.py on a copy of the audited rows in a temporary database. No real install is read or written.

    python tools/claude-version/tests/test_fix_broken_recipes.py
"""
import sqlite3
import sys
import unittest
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import fix_broken_recipes as tool  # noqa: E402

SCHEMA = """
CREATE TABLE `ItemTemplate` (`Id_nb` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, `Realm` INT(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id_nb`));
CREATE TABLE `CraftedXItem` (`CraftedItemId_nb` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
  `IngredientId_nb` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `Count` INT(11) NOT NULL DEFAULT 0,
  `LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
  `CraftedXItem_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, PRIMARY KEY (`CraftedXItem_ID`));
"""


def world():
    con = sqlite3.connect(":memory:")
    con.executescript(SCHEMA)
    for idnb, (old, _) in tool.REALMS.items():
        con.execute("insert into ItemTemplate values (?, ?)", (idnb, old))
    for idnb in ("heartwood_wooden_boards", "phoenix_feathers", "dolomite_metal_bars", "footed_flight_broadhead_arrows3",
                 "stable_cold_fine_alloy_tincture3", "bystander"):
        con.execute("insert into ItemTemplate values (?, 3)", (idnb,))
    for i, ingredient in enumerate(("cobalt_metal_bars", "icy_distill", "stable_catalyst")):
        con.execute("insert into CraftedXItem values (?, ?, ?, '2000-01-01 00:00:00', ?)", (tool.TYPO_KEY[0], ingredient, 1 + (i == 2), f"typo{i}"))
    con.execute("insert into CraftedXItem values ('bystander', 'icy_distill', 1, '2000-01-01 00:00:00', 'other')")
    return con


def snapshot(con):
    return (con.execute("select * from CraftedXItem order by CraftedXItem_ID").fetchall(),
            con.execute("select * from ItemTemplate order by Id_nb").fetchall())


class FixBrokenRecipes(unittest.TestCase):
    def test_apply_fixes_the_two_recipes_and_eight_realms(self):
        con = world()
        p = tool.apply(con)
        self.assertEqual(len(p["rekey"]), 3)
        self.assertEqual(len(p["insert"]), 3)
        self.assertEqual(len(p["realms"]), 8)
        self.assertEqual(con.execute("select count(*) from CraftedXItem where CraftedItemId_nb='stable_cold_fine_alloy_tincture3'").fetchone()[0], 3)
        self.assertEqual(sorted(r[0] for r in con.execute("select IngredientId_nb from CraftedXItem where CraftedItemId_nb='footed_flight_broadhead_arrows3'")),
                         ["dolomite_metal_bars", "heartwood_wooden_boards", "phoenix_feathers"])
        self.assertEqual(con.execute("select Realm from ItemTemplate where Id_nb='bronze_long_sword2'").fetchone()[0], 2)
        self.assertEqual(con.execute("select Realm from ItemTemplate where Id_nb='diamond_mace3'").fetchone()[0], 3)
        self.assertEqual(con.execute("select count(*) from CraftedXItem where CraftedItemId_nb='bystander'").fetchone()[0], 1)

    def test_a_second_apply_changes_nothing_and_rollback_restores_the_world(self):
        con = world()
        before = snapshot(con)
        p = tool.apply(con)
        again = tool.plan(con)
        self.assertEqual(again, {"rekey": [], "insert": [], "realms": {}})
        tool.rollback(con, p)
        self.assertEqual(snapshot(con), before)

    def test_the_new_rows_have_the_same_ids_on_every_world(self):
        self.assertEqual([r[0] for r in tool.plan(world())["insert"]], [r[0] for r in tool.plan(world())["insert"]])


if __name__ == "__main__":
    unittest.main()
