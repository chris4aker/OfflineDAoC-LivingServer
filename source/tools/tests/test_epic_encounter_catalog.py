"""Tests for the epic encounter catalog (docs/encounters/epic-encounters.json and its generator).

    python3 -m unittest discover -s source/tools/tests -v
"""
import json
import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import epic_encounter_catalog as cat  # noqa: E402

REPO = cat.REPO
CATALOG = REPO / cat.OUT_JSON


def lines_of(rel):
    return cat.read_text(REPO / rel).split("\n")


class CatalogFileTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(CATALOG.read_text(encoding="utf-8"))
        cls.cache = {}

    def file_lines(self, rel):
        if rel not in self.cache:
            self.assertTrue((REPO / rel).is_file(), f"{rel} does not exist")
            self.cache[rel] = lines_of(rel)
        return self.cache[rel]

    def test_parses_with_counts(self):
        self.assertEqual(self.data["schema"], cat.SCHEMA)
        c = self.data["counts"]
        self.assertEqual(c["encounters"], len(self.data["encounters"]))
        self.assertEqual(c["rules"], sum(len(e["rules"]) for e in self.data["encounters"]))
        self.assertGreater(c["encounters"], 200)

    def test_every_encounter_points_at_an_existing_file_and_line_range(self):
        for e in self.data["encounters"]:
            with self.subTest(encounter=e["id"]):
                lines = self.file_lines(e["file"])
                first, last = e["lines"]
                self.assertTrue(1 <= first <= last <= len(lines), f"{e['file']} {first}-{last} of {len(lines)}")

    def test_every_rule_points_at_its_line_and_says_how_it_is_known(self):
        kinds, difficulties = set(self.data["ruleKinds"]), set(self.data["difficulties"])
        for e in self.data["encounters"]:
            for r in e["rules"]:
                with self.subTest(encounter=e["id"], rule=r.get("key")):
                    lines = self.file_lines(r["file"])
                    self.assertTrue(1 <= r["line"] <= r["lineEnd"] <= len(lines),
                                    f"{r['file']} {r['line']}-{r['lineEnd']} of {len(lines)}")
                    span = "\n".join(lines[r["line"] - 1:r["lineEnd"]])
                    self.assertIn(" ".join(r["anchor"].split())[:60], " ".join(span.split()))
                    self.assertIn(r["kind"], kinds)
                    self.assertIn(r["confidence"], ("read", "inferred"))
                    self.assertIn(r["difficulty"], difficulties)
                    self.assertTrue(r["tactic"].strip())
                    self.assertIsInstance(r["verified"], bool)

    def test_era_notes_are_left_for_the_next_step(self):
        for e in self.data["encounters"]:
            self.assertEqual(e["eraNotes"], "", e["id"])

    def test_priority_names_catalogued_encounters(self):
        ids = {e["id"] for e in self.data["encounters"]}
        self.assertGreaterEqual(len(self.data["priority"]), 10)
        for p in self.data["priority"]:
            for eid in [p["id"]] + p.get("also", []):
                self.assertIn(eid, ids)

    def test_darkness_falls_is_marked_no_raid_ai(self):
        df = [e for e in self.data["encounters"] if e.get("region") == 249]
        self.assertTrue(df)
        for e in df:
            self.assertEqual(e["raidAi"], "no raid AI", e["id"])

    def test_script_hashes_match_the_scripts(self):
        for rel, digest in self.data["scriptHashes"].items():
            with self.subTest(script=rel):
                self.assertEqual(cat.script_hash(REPO / rel), digest, "regenerate the catalog")

    def test_known_blockers_are_read(self):
        by_id = {e["id"]: e for e in self.data["encounters"]}
        scurceol = by_id["krondon/scurceolhyrde"]
        immune = [r for r in scurceol["rules"] if r["kind"] == "immune_while"]
        self.assertEqual(immune[0]["subtype"], "adds_alive")
        self.assertEqual(scurceol["region"], 61)
        self.assertIn({"id": "dungeon-albion-krondon", "role": "named"}, scurceol["raids"])
        nosdoden = by_id["trollheim/nosdoden"]
        self.assertTrue(any(r["kind"] == "phase" and r.get("thresholds") == [90, 80, 70, 60, 50, 40, 30, 20, 10]
                            for r in nosdoden["rules"]))
        conservator = by_id["galladoria/conservator"]
        self.assertTrue(any("AllowPositionPuzzle" in (r.get("switch") or []) for r in conservator["rules"]))


class TacticsAndRestores(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(CATALOG.read_text(encoding="utf-8"))

    def test_tactic_table_names_the_first_encounters(self):
        cs = cat.read_text(REPO / cat.TACTICS_CS)
        self.assertIn('new("ScurceolHyrde", "krondon/scurceolhyrde", ["LyftMihtOne", "LyftMihtTwo", "LyftMihtThree", "LyftMihtFour"], 1500, null)', cs)
        self.assertIn('new("AncientBlackOak", "marfach-caverns/ancientblackoak", [], 0, "BlackLady")', cs)
        self.assertIn('new("BlackLady", "marfach-caverns/blacklady", [], 1100, null)', cs)
        self.assertIn('new("AidonTheArchwizard", "hall-of-the-corrupt/aidonthearchwizard", ["AidonCopyFire", "AidonCopyIce", '
                      '"AidonCopyAir", "AidonCopyEarth"], 1100, null, 1, null, 0)', cs)
        self.assertIn('new("Xanxicar", "crystal-cave/xanxicar", [], 0, null, 0, [80, 60, 40, 20], 2500)', cs)
        self.assertIn('new("Nosdoden", "trollheim/nosdoden", ["NosdodenSummonedAdds"], 0, null)', cs)
        self.assertIn('new("AidonTheArchwizard", 1500, true)', cs)

    def test_every_offline_change_but_the_old_puzzle_switch_names_its_restore_switch(self):
        restore = {s["name"] for s in self.data["switches"] if s.get("restores")}
        self.assertEqual(len(restore), 17)
        changes = [(e["id"], r) for e in self.data["encounters"] for r in e["rules"] if r["kind"] == "offline_change"]
        self.assertGreaterEqual(len(changes), 23)
        for eid, r in changes:
            with self.subTest(encounter=eid, line=r["line"]):
                self.assertTrue(r.get("switch"), "an offline change without a switch")
                self.assertTrue(set(r["switch"]) & (restore | {"AllowPositionPuzzle"}))


class CatalogIsUpToDate(unittest.TestCase):
    def test_regenerating_gives_the_committed_files(self):
        data, problems = cat.catalog(cat.World())
        self.assertEqual(problems, [])
        self.assertEqual(cat.dumps(data), cat.read_text(REPO / cat.OUT_JSON), "run epic_encounter_catalog.py")
        self.assertEqual(cat.render_md(data), cat.read_text(REPO / cat.OUT_MD), "run epic_encounter_catalog.py")
        self.assertEqual(cat.render_tactics_cs(data), cat.read_text(REPO / cat.TACTICS_CS), "run epic_encounter_catalog.py")
        self.assertEqual([e["id"] for e in data["encounters"] if e.get("verificationStale")], [])


class ParserTests(unittest.TestCase):
    def test_mask_blanks_comments_and_strings_keeping_lines(self):
        src = 'a = "x{y}"; // }\n/* { */ b = \'{\';\n'
        m = cat.mask(src)
        self.assertEqual(len(m), len(src))
        self.assertEqual(m.count("\n"), 2)
        self.assertNotIn("{", m)
        self.assertNotIn("}", m)

    def write_source(self, text):
        import shutil
        import tempfile
        tmp = Path(tempfile.mkdtemp(dir=REPO / "source" / "tools" / "tests"))
        self.addCleanup(shutil.rmtree, tmp, True)
        path = tmp / "Boss.cs"
        path.write_text(text, encoding="utf-8")
        return cat.Source(path.relative_to(REPO))

    def test_else_branch_reads_as_the_negated_if(self):
        src = self.write_source(
            "class Fire : GameNPC {\n"
            " public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount) {\n"
            "  if (damageType == eDamageType.Cold) {\n"
            "   base.TakeDamage(source, damageType, damageAmount, criticalAmount);\n"
            "  } else {\n"
            "   base.TakeDamage(source, damageType, 0, 0);\n"
            "  }\n"
            " }\n"
            "}\n")
        cls = cat.parse_classes(src)[0]
        world = cat.World.__new__(cat.World)
        world.classes, world.sources = {cls.name: cls}, {src.rel: src}
        rules = cat.detect_take_damage(world, cls, {})
        self.assertEqual(len(rules), 1)
        self.assertEqual(rules[0]["subtype"], "damage_type")
        self.assertIn("except from Cold", rules[0]["summary"])

    def test_adds_counter_immunity_is_adds_alive(self):
        sub, text = cat.classify_immunity(None, "if (OrbOne.OrbCount > 0 || OrbTwo.OrbCount > 0)", None)
        self.assertEqual(sub, "adds_alive")
        sub, _ = cat.classify_immunity(None, "if (EpicEncounterMechanics.AllowAddShield && Swarm_count > 0)", None)
        self.assertEqual(sub, "adds_alive")
        sub, text = cat.classify_immunity(None, "if (!source.IsWithinRadius(this, 200))", None)
        self.assertEqual(sub, "attacker_position")
        self.assertIn("200", text)
        sub, _ = cat.classify_immunity(None, "if (IsOutOfTetherRange)", None)
        self.assertEqual(sub, "out_of_tether")


if __name__ == "__main__":
    unittest.main()
