"""Tests of true165_prima_content.py: it never changes a played world, adds only what the world lacks, and keeps out
anything from Trials of Atlantis or later.

    python -m unittest discover -s source/tools/tests -v
"""
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import true165_prima_content as tpc  # noqa: E402

TRUE165 = Path(__file__).resolve().parents[4] / "ls165" / "runtime" / "true165"

SCHEMA = """
CREATE TABLE Mob (Mob_ID TEXT PRIMARY KEY, ClassType TEXT, Name TEXT, X INT, Y INT, Z INT, Region INT, Level INT,
                  AggroLevel INT, Speed INT, Brain TEXT, FactionID INT, NPCTemplateID INT, PathID TEXT, PackageID TEXT,
                  RoamingRange INT, Model INT);
CREATE TABLE NpcTemplate (NpcTemplate_ID TEXT PRIMARY KEY, TemplateId INT, Name TEXT, Level TEXT);
CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT, Bonus1Type INT, Bonus1 INT);
CREATE TABLE Path (Path_ID TEXT PRIMARY KEY, PathID TEXT);
CREATE TABLE PathPoints (PathPoints_ID TEXT PRIMARY KEY, PathID TEXT, X INT, Y INT, Z INT);
CREATE TABLE DataQuest (ID INT PRIMARY KEY, Name TEXT);
CREATE TABLE LootTemplate (LootTemplate_ID TEXT PRIMARY KEY, TemplateName TEXT, ItemTemplateID TEXT, Chance INT, Count INT);
CREATE TABLE MobXLootTemplate (MobXLootTemplate_ID TEXT PRIMARY KEY, MobName TEXT, LootTemplateName TEXT, DropCount INT);
CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT PRIMARY KEY, Name TEXT);
CREATE TABLE Account (Account_ID TEXT PRIMARY KEY, Name TEXT);
"""


def write(folder, name, table, key, rows, reason="test rows"):
    (folder / name).write_text(json.dumps({"table": table, "key": key, "reason": reason, "rows": rows}), encoding="utf-8")


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.content = root / "true165"
        self.content.mkdir()
        self.world = root / "world.db"
        db = sqlite3.connect(self.world)
        db.executescript(SCHEMA)
        db.execute("INSERT INTO Mob (Mob_ID, Name, X, Y, Region, Level) VALUES ('old-1', 'moor boogey', 1000, 1000, 1, 20)")
        db.execute("INSERT INTO Mob (Mob_ID, Name, X, Y, Region, Level) VALUES ('talvor', 'Talvor', 5, 5, 51, 24)")
        db.execute("INSERT INTO NpcTemplate VALUES ('t-old', 77, 'taken', '5')")
        db.execute("INSERT INTO DataQuest VALUES (5, 'An Old Quest')")
        db.commit()
        db.close()
        c = self.content
        write(c, "add-primatemplates165.json", "NpcTemplate", "NpcTemplate_ID",
              [{"NpcTemplate_ID": "t-new", "TemplateId": 900, "Name": "drakulv klok", "Level": "40"},
               {"NpcTemplate_ID": "t-dup", "TemplateId": 77, "Name": "clash", "Level": "1"}])
        write(c, "add-primaenctemplates165.json", "NpcTemplate", "NpcTemplate_ID", [])
        write(c, "add-primaitems165.json", "ItemTemplate", "Id_nb",
              [{"Id_nb": "p165_a", "Name": "Period Ring", "Bonus1Type": 2, "Bonus1": 6},
               {"Id_nb": "p165_toa", "Name": "Speed Ring", "Bonus1Type": 150, "Bonus1": 6}])
        write(c, "add-primaquestitems165.json", "ItemTemplate", "Id_nb", [])
        write(c, "add-primapaths165.json", "Path", "Path_ID", [{"Path_ID": "p1", "PathID": "convoy"}])
        write(c, "add-primapathpoints165.json", "PathPoints", "PathPoints_ID", [{"PathPoints_ID": "pp1", "PathID": "convoy", "X": 1}])
        write(c, "add-primaspawns165.json", "Mob", "Mob_ID",
              [{"Mob_ID": "new-1", "ClassType": "DOL.GS.GameNPC", "Name": "drakulv klok", "X": 0, "Y": 0, "Region": 51, "Level": 40,
                "NotAColumn": 1},
               {"Mob_ID": "restored-1", "Name": "Moor Boogey", "X": 1200, "Y": 1100, "Region": 1, "Level": 20},
               {"Mob_ID": "old-1", "Name": "moor boogey", "X": 1000, "Y": 1000, "Region": 1}])
        write(c, "add-cornwallprima165.json", "Mob", "Mob_ID", [])
        write(c, "add-primaencounters165.json", "Mob", "Mob_ID",
              [{"Mob_ID": "enc-1", "ClassType": "DOL.GS.Scripts.ThergusOgreWar165", "Name": "Thergus", "X": 9, "Y": 9, "Region": 100}])
        write(c, "add-restoredspawns165.json", "Mob", "Mob_ID",
              [{"Mob_ID": "celain", "Name": "Celain Eirich", "Brain": "DOL.AI.Brain.CelainEirichBrain", "X": 1, "Y": 1, "Region": 200},
               {"Mob_ID": "other", "Name": "some rabbit", "X": 1, "Y": 1, "Region": 200}])
        write(c, "add-primaquests165.json", "DataQuest", "ID", [{"ID": 6, "Name": "A Prima Quest"}, {"ID": 7, "Name": "an old quest"}])
        (c / "true165-rules.json").write_text(json.dumps({"updates": [
            {"sql": "UPDATE Mob SET ClassType='DOL.GS.Scripts.Talvor165' WHERE Mob_ID='talvor'",
             "reason": "True 1.65 Prima encounter (owner 2026-11-07): Talvor's Spirit"},
            {"sql": "UPDATE Mob SET Brain='DOL.AI.Brain.CuinnBrain', FactionID=98 WHERE Mob_ID='cuinn-not-here'",
             "reason": "True 1.65 Prima encounter (owner 2026-11-07): Cuinn"},
            {"sql": "UPDATE Mob SET AggroLevel = 1, Level = 22 WHERE Mob_ID IN ('old-1')",
             "reason": "True 1.65 Prima monster: AggroLevel 1, Level 22 (1 spawns)"}]}), encoding="utf-8")
        (c / "drops165.json").write_text(json.dumps({"source": "test drops", "min_kills": 20, "monsters": {
            "drakulv klok": {"drop_count": 2, "items": [["p165_a", 30], ["missing_item", 10]]},
            "moor boogey": {"drop_count": 1, "items": [["p165_a", 5]]}}}), encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def query(self, sql, *args):
        db = sqlite3.connect(self.world)
        try:
            return db.execute(sql, args).fetchall()
        finally:
            db.close()


class TestTrue165PrimaContent(Fixture):
    def test_a_report_changes_nothing(self):
        before = self.query("SELECT count(*) FROM Mob")
        report = tpc.run(self.content, self.world)
        self.assertTrue(report["added"])
        self.assertEqual(self.query("SELECT count(*) FROM Mob"), before)

    def test_a_played_world_is_refused(self):
        db = sqlite3.connect(self.world)
        db.execute("INSERT INTO DOLCharacters VALUES ('c1', 'Hero')")
        db.commit()
        db.close()
        with self.assertRaises(tpc.Refused):
            tpc.run(self.content, self.world, apply=True)
        self.assertEqual(self.query("SELECT count(*) FROM Mob WHERE Mob_ID='new-1'"), [(0,)])

    def test_adds_only_what_the_world_lacks(self):
        report = tpc.run(self.content, self.world, apply=True)
        self.assertEqual(self.query("SELECT Name, Level FROM Mob WHERE Mob_ID='new-1'"), [("drakulv klok", 40)])
        self.assertEqual(self.query("SELECT count(*) FROM Mob WHERE Mob_ID='restored-1'"), [(0,)],
                         "the world's own copy of a restored spawn stands within 500")
        self.assertEqual(self.query("SELECT count(*) FROM NpcTemplate WHERE NpcTemplate_ID='t-dup'"), [(0,)], "TemplateId taken")
        self.assertEqual(self.query("SELECT count(*) FROM NpcTemplate WHERE NpcTemplate_ID='t-new'"), [(1,)])
        self.assertEqual(self.query("SELECT ID FROM DataQuest ORDER BY ID"), [(5,), (6,)], "a quest of a taken name is skipped")
        self.assertEqual(self.query("SELECT count(*) FROM PathPoints"), [(1,)])
        skipped = {e["key"] for e in report["skipped"]}
        self.assertIn("old-1", skipped)

    def test_no_trials_of_atlantis_bonus(self):
        report = tpc.run(self.content, self.world, apply=True)
        self.assertEqual(self.query("SELECT Id_nb FROM ItemTemplate ORDER BY Id_nb"), [("p165_a",)])
        self.assertIn("p165_toa", {e["key"] for e in report["refused"]})

    def test_encounters_bind_only_spawns_the_world_has(self):
        tpc.run(self.content, self.world, apply=True)
        self.assertEqual(self.query("SELECT ClassType FROM Mob WHERE Mob_ID='talvor'"), [("DOL.GS.Scripts.Talvor165",)])
        self.assertEqual(self.query("SELECT ClassType FROM Mob WHERE Mob_ID='enc-1'"), [("DOL.GS.Scripts.ThergusOgreWar165",)])
        self.assertEqual(self.query("SELECT Brain FROM Mob WHERE Mob_ID='celain'"), [("DOL.AI.Brain.CelainEirichBrain",)])
        self.assertEqual(self.query("SELECT count(*) FROM Mob WHERE Mob_ID='other'"), [(0,)], "only encounter rows of that file")

    def test_monster_corrections_only_when_asked(self):
        tpc.run(self.content, self.world, apply=True)
        self.assertEqual(self.query("SELECT Level FROM Mob WHERE Mob_ID='old-1'"), [(20,)])
        tpc.run(self.content, self.world, sets=[], corrections=True, apply=True)
        self.assertEqual(self.query("SELECT Level, AggroLevel FROM Mob WHERE Mob_ID='old-1'"), [(22, 1)])

    def test_drops_only_for_new_monsters_and_existing_items(self):
        tpc.run(self.content, self.world, apply=True)
        self.assertEqual(self.query("SELECT ItemTemplateID, Chance FROM LootTemplate WHERE TemplateName='drakulv klok'"),
                         [("p165_a", 30)])
        self.assertEqual(self.query("SELECT DropCount FROM MobXLootTemplate WHERE MobName='drakulv klok'"), [(2,)])
        self.assertEqual(self.query("SELECT count(*) FROM LootTemplate WHERE TemplateName='moor boogey'"), [(0,)],
                         "a monster the world already had keeps its own loot")

    def test_a_second_run_adds_nothing(self):
        tpc.run(self.content, self.world, apply=True)
        again = tpc.run(self.content, self.world, apply=True)
        self.assertEqual([e for e in again["added"] if e["table"] != "LootTemplate"], [])


@unittest.skipUnless((TRUE165 / "true165-rules.json").exists(), "True 1.65's content is not beside this checkout")
class TestOnTrue165sOwnFiles(unittest.TestCase):
    def test_every_prima_file_reads_and_reports(self):
        with tempfile.TemporaryDirectory() as tmp:
            world = Path(tmp) / "world.db"
            db = sqlite3.connect(world)
            db.executescript(SCHEMA)
            db.close()
            report = tpc.run(TRUE165, world)
            added = {e["table"] for e in report["added"]}
            self.assertTrue({"Mob", "NpcTemplate", "ItemTemplate", "Path", "PathPoints", "DataQuest"} <= added, added)
            self.assertEqual([e for e in report["refused"] if e["table"] == "ItemTemplate"], [],
                             "the Prima items carry no Trials of Atlantis bonus")


if __name__ == "__main__":
    unittest.main()
