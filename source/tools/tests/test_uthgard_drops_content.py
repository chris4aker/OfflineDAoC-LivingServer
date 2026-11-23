"""Tests of uthgard_drops_content.py: it never changes a played world, keeps every loot row the world has, adds Uthgard's
drops only for monsters with no loot, converts the rates to the server's chance, and keeps out later-era items.

    python -m unittest discover -s source/tools/tests -v
"""
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import uthgard_drops_content as udc  # noqa: E402

SCHEMA = """
CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, Name TEXT, Region INT, Realm INT, NPCTemplateID INT);
CREATE TABLE NpcTemplate (NpcTemplate_ID TEXT PRIMARY KEY, TemplateId INT, Name TEXT, Realm INT);
CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT NOT NULL, Realm INT NOT NULL DEFAULT 0, Level INT,
                           Bonus1Type INT, Bonus1 INT, SpellID INT, Price INT NOT NULL, Description TEXT NOT NULL);
CREATE TABLE Spell (Spell_ID TEXT PRIMARY KEY, SpellID INT);
CREATE TABLE LootTemplate (LootTemplate_ID TEXT PRIMARY KEY, TemplateName TEXT, ItemTemplateID TEXT, Chance INT, Count INT);
CREATE TABLE MobXLootTemplate (MobXLootTemplate_ID TEXT PRIMARY KEY, MobName TEXT, LootTemplateName TEXT, DropCount INT);
CREATE TABLE LootOTD (LootOTD_ID TEXT PRIMARY KEY, MobName TEXT, ItemTemplateID TEXT, MinLevel INT);
CREATE TABLE LootGenerator (LootGenerator_ID TEXT PRIMARY KEY, MobName TEXT, MobGuild TEXT, MobFaction TEXT, RegionID INT,
                            LootGeneratorClass TEXT, ExclusivePriority INT);
CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT PRIMARY KEY, Name TEXT);
CREATE TABLE Account (Account_ID TEXT PRIMARY KEY, Name TEXT);
"""

UTHGARD = {
    # in the world, no loot: gets Uthgard's drops
    "moor boogey": {"killed": 120, "coins": 80.0, "items": [
        ["Boogey Tooth", 30.4], ["Bone Ring", 0.2], ["Bag of Coins", 90], ["small chest", 5], ["Mystery Thing", 4],
        ["Speed Ring", 3], ["Old Blade", 12]]},
    # in the world with its own loot: kept
    "bwca": {"killed": 50, "items": [["Boogey Tooth", 50]]},
    # not in the world
    "far away beast": {"killed": 9, "items": [["Boogey Tooth", 10]]},
    # a Midgard monster: the realm 2 item of that name
    "wolf": {"killed": 40, "items": [["Wolf Pelt", 100], ["Wolf Pelt", 60]]},
    # its spawns' NPC template has loot
    "template beast": {"killed": 5, "items": [["Boogey Tooth", 10]]},
    # all of its drops are chests and coins
    "chest mimic": {"killed": 5, "items": [["large chest", 10]]},
}
LOOT_ITEM_ROWS = [
    {"Id_nb": "uth_boogey_tooth", "Name": "Boogey Tooth", "Realm": 0, "Level": 0, "Price": 40, "Item_Type": 40},
    {"Id_nb": "uth_bone_ring", "Name": "Bone Ring", "Realm": 0, "Level": 10, "Price": 200, "Bonus1Type": 2, "Bonus1": 3},
    {"Id_nb": "uth_speed_ring", "Name": "Speed Ring", "Realm": 0, "Level": 30, "Price": 200, "Bonus1Type": 150, "Bonus1": 5},
]
DROP_ITEM_ROWS = [
    {"Id_nb": "Golestandt_Claw", "Name": "Golestandt Claw", "Realm": 1, "Level": 50, "Price": 9000, "SpellID": 77},
    {"Id_nb": "Weno_Ring", "Name": "Weno'iak's Ring", "Realm": 1, "Level": 50, "Price": 9000, "SpellID": 999},
]


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.data = root / "data"
        self.data.mkdir()
        (self.data / "uthgard_drops.json").write_text(json.dumps(UTHGARD), encoding="utf-8")
        (self.data / "loot_item_rows.json").write_text(json.dumps(LOOT_ITEM_ROWS), encoding="utf-8")
        (self.data / "drop_item_rows.json").write_text(json.dumps(DROP_ITEM_ROWS), encoding="utf-8")
        self.world = root / "world.db"
        db = sqlite3.connect(self.world)
        db.executescript(SCHEMA)
        db.executemany("INSERT INTO Mob VALUES (?, ?, ?, ?, ?)", [
            ("m1", "Moor Boogey", 1, 0, 0), ("m2", "Moor Boogey", 1, 0, 0), ("m3", "bwca", 1, 0, 0),
            ("m4", "wolf", 100, 0, 0), ("m5", "template beast", 1, 0, 42), ("m6", "chest mimic", 1, 0, 0),
            ("g1", "Far Away Beast", 1, 1, 0)])          # a realm 1 NPC is not a monster
        db.execute("INSERT INTO Spell VALUES ('s77', 77)")
        db.executemany("INSERT INTO ItemTemplate (Id_nb, Name, Realm, Price, Description) VALUES (?, ?, ?, 0, '')", [
            ("old_blade", "Old Blade", 0), ("pelt_alb", "Wolf Pelt", 1), ("pelt_mid", "Wolf Pelt", 2), ("bwca_ear", "Bwca Ear", 0)])
        db.execute("INSERT INTO LootTemplate VALUES ('own-1', 'bwca', 'bwca_ear', 40, 1)")
        db.execute("INSERT INTO LootTemplate VALUES ('own-2', 'Golestandt', 'Golestandt_Claw', 100, 1)")
        db.execute("INSERT INTO LootOTD VALUES ('otd-1', 'Golestandt', 'weno_ring', 40)")
        db.execute("INSERT INTO LootTemplate VALUES ('own-3', 'Golestandt', 'never_built', 10, 1)")
        db.execute("INSERT INTO MobXLootTemplate VALUES ('tpl-42', '42', '42', 1)")
        db.execute("INSERT INTO LootGenerator VALUES ('lg1', NULL, NULL, NULL, 0, 'DOL.GS.LootGeneratorTemplate', 0)")
        db.commit()
        db.close()

    def tearDown(self):
        self.tmp.cleanup()

    def query(self, sql, *args):
        db = sqlite3.connect(self.world)
        try:
            return db.execute(sql, args).fetchall()
        finally:
            db.close()

    def apply(self, **kw):
        return udc.run(self.data, self.world, apply=True, **kw)


class TestChance(unittest.TestCase):
    def test_rates_become_whole_percents(self):
        self.assertEqual(udc.chance_of(30.4), 30)
        self.assertEqual(udc.chance_of(30.5), 31, "half up, not to even")
        self.assertEqual(udc.chance_of(2.5), 3)
        self.assertEqual(udc.chance_of(100), 100)
        self.assertEqual(udc.chance_of(140), 100)

    def test_a_rare_drop_still_drops_unless_asked(self):
        self.assertEqual(udc.chance_of(0.2), 1, "0 would never drop")
        self.assertIsNone(udc.chance_of(0.2, "skip"))
        self.assertEqual(udc.chance_of(0.6, "skip"), 1)
        self.assertIsNone(udc.chance_of(0))
        self.assertIsNone(udc.chance_of("n/a"))

    def test_region_realms(self):
        self.assertEqual([udc.realm_of_region(r) for r in (1, 51, 100, 151, 200, 249, 300)], [1, 1, 2, 2, 3, 0, 0])


class TestUthgardDrops(Fixture):
    def test_a_dry_run_writes_nothing(self):
        before = [self.query(f"SELECT count(*) FROM {t}") for t in ("LootTemplate", "MobXLootTemplate", "ItemTemplate")]
        report = udc.run(self.data, self.world)
        self.assertFalse(report["applied"])
        self.assertEqual(report["counts"]["monsters_added"], 2)
        self.assertEqual(before, [self.query(f"SELECT count(*) FROM {t}") for t in ("LootTemplate", "MobXLootTemplate", "ItemTemplate")])

    def test_a_played_world_is_refused(self):
        db = sqlite3.connect(self.world)
        db.execute("INSERT INTO DOLCharacters VALUES ('c1', 'Hero')")
        db.commit()
        db.close()
        with self.assertRaises(udc.Refused):
            self.apply()
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate"), [(3,)])

    def test_a_missing_world_or_data_is_refused(self):
        with self.assertRaises(udc.Refused):
            udc.run(self.data, Path(self.tmp.name) / "typo.db", apply=True)
        self.assertFalse((Path(self.tmp.name) / "typo.db").exists(), "no empty database is made")
        (self.data / "uthgard_drops.json").unlink()
        with self.assertRaises(udc.Refused):
            udc.run(self.data, self.world)

    def test_adds_links_and_loot_with_the_worlds_spelling(self):
        report = self.apply()
        rows = self.query("SELECT ItemTemplateID, Chance, Count FROM LootTemplate WHERE TemplateName='Moor Boogey' ORDER BY 1")
        self.assertEqual(rows, [("old_blade", 12, 1), ("uth_bone_ring", 1, 1), ("uth_boogey_tooth", 30, 1)])
        self.assertEqual(self.query("SELECT MobName, LootTemplateName, DropCount, MobXLootTemplate_ID FROM MobXLootTemplate "
                                    "WHERE lower(MobName)='moor boogey'"),
                         [("Moor Boogey", "Moor Boogey", 3, "uthdrop:moor boogey")], "every winner of its roll drops")
        self.assertEqual(report["counts"]["chance_rounded_up_to_1"], 1)
        self.assertEqual(report["items_missing"], {"Mystery Thing": 1})
        self.assertEqual(report["counts"]["left_out_chests_and_coins"], 3, "two of the boogey's, the mimic's chest")
        self.assertEqual(report["counts"]["drops_with_a_refused_item"], 1, "the Speed Ring")

    def test_keeps_the_worlds_own_loot(self):
        self.apply()
        self.assertEqual(self.query("SELECT LootTemplate_ID, ItemTemplateID, Chance FROM LootTemplate WHERE TemplateName='bwca'"),
                         [("own-1", "bwca_ear", 40)])
        self.assertEqual(self.query("SELECT count(*) FROM MobXLootTemplate WHERE MobName='bwca'"), [(0,)])
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate WHERE TemplateName='template beast'"), [(0,)],
                         "the NPC template's loot comes first on the server")
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate WHERE lower(TemplateName)='far away beast'"), [(0,)],
                         "a realm NPC of that name is not a monster")
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate WHERE TemplateName='chest mimic'"), [(0,)])

    def test_items_match_the_monsters_realm(self):
        self.apply()
        self.assertEqual(self.query("SELECT ItemTemplateID, Chance FROM LootTemplate WHERE TemplateName='wolf'"),
                         [("pelt_mid", 100)], "a Midgard region's realm 2 pelt, the higher of two rates")

    def test_adds_only_missing_period_items(self):
        report = self.apply()
        added = {r[0] for r in self.query("SELECT Id_nb FROM ItemTemplate")} - {"old_blade", "pelt_alb", "pelt_mid", "bwca_ear"}
        self.assertEqual(added, {"uth_boogey_tooth", "uth_bone_ring", "Golestandt_Claw"})
        refused = {e["key"]: e["why"] for e in report["refused"]}
        self.assertIn("Bonus1Type=150", refused["uth_speed_ring"], "a Trials of Atlantis or later bonus")
        self.assertIn("no such spell", refused["weno_ring"])
        self.assertEqual(self.query("SELECT Price, Description, Realm FROM ItemTemplate WHERE Id_nb='uth_boogey_tooth'"),
                         [(40, "", 0)], "a NOT NULL column the row lacks is filled")

    def test_world_references_get_their_items(self):
        report = self.apply()
        self.assertEqual(self.query("SELECT Name FROM ItemTemplate WHERE Id_nb='Golestandt_Claw'"), [("Golestandt Claw",)])
        self.assertEqual(report["counts"]["world_references_fixed"], 1)
        self.assertEqual(report["counts"]["world_references_still_missing"], 1, "never_built: no row anywhere")
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate WHERE TemplateName='Golestandt'"), [(2,)], "rows unchanged")

    def test_rare_drops_can_be_left_out(self):
        self.apply(rare="skip")
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate WHERE ItemTemplateID='uth_bone_ring'"), [(0,)])
        self.assertEqual(self.query("SELECT count(*) FROM ItemTemplate WHERE Id_nb='uth_bone_ring'"), [(0,)])

    def test_a_second_run_adds_nothing(self):
        self.apply()
        loot = self.query("SELECT count(*) FROM LootTemplate")
        again = self.apply()
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate"), loot)
        self.assertEqual(again["added"], [])
        self.assertIn("already has this tool's Uthgard drops",
                      {e["why"] for e in again["skipped"] if e["key"] == "Moor Boogey"})

    def test_an_old_db_gives_items_the_world_lacks(self):
        old = Path(self.tmp.name) / "old.db"
        db = sqlite3.connect(old)
        db.execute("CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT, Realm INT, Level INT, Price INT, Bonus1Type INT)")
        db.execute("INSERT INTO ItemTemplate VALUES ('mystery_thing', 'Mystery Thing', 0, 5, 12, 0)")
        db.commit()
        db.close()
        report = udc.run(self.data, self.world, old_db=old, apply=True)
        self.assertEqual(self.query("SELECT Chance FROM LootTemplate WHERE ItemTemplateID='mystery_thing'"), [(4,)])
        self.assertEqual(report["items_missing"], {})
        self.assertEqual(report["counts"]["items_added_from_old DB"], 1)

    def test_warns_when_the_template_generator_is_not_registered(self):
        db = sqlite3.connect(self.world)
        db.execute("DELETE FROM LootGenerator")
        db.commit()
        db.close()
        report = udc.run(self.data, self.world)
        self.assertTrue(any("LootGeneratorTemplate" in w for w in report["warnings"]))

    def test_the_summary_names_the_counts(self):
        line = udc.summary(udc.run(self.data, self.world))
        self.assertTrue(line.startswith("Would apply: monsters matched 5 (of 6 Uthgard names with drops), added 2 with 4 loot rows"), line)


class TestReleaseHook(Fixture):
    def test_the_release_build_adds_the_drops_and_writes_its_report(self):
        import build_release_037 as br
        out = Path(self.tmp.name) / "OfflineDAoC-v0.37b"
        summary = br.add_uthgard_drops(self.world, self.data, None, out)
        self.assertEqual(summary["counts"]["monsters_added"], 2)
        self.assertTrue((out.parent / "OfflineDAoC-v0.37b-uthgard-drops-report.json").is_file())
        self.assertEqual(self.query("SELECT count(*) FROM MobXLootTemplate WHERE MobXLootTemplate_ID LIKE 'uthdrop:%'"), [(2,)])

    def test_without_the_data_the_release_adds_none(self):
        import build_release_037 as br
        self.assertIsNone(br.add_uthgard_drops(self.world, None, None, Path(self.tmp.name) / "out"))
        self.assertEqual(self.query("SELECT count(*) FROM MobXLootTemplate WHERE MobXLootTemplate_ID LIKE 'uthdrop:%'"), [(0,)])


if __name__ == "__main__":
    unittest.main()
