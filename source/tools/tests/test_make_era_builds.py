"""Era builds 1.2, WP0: make_era_builds.py turns the research draft and the owner's review into the shipped data file."""
import json
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import make_era_builds as era  # noqa: E402


class MakeEraBuilds(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.draft = json.loads(era.DRAFT.read_text(encoding="utf-8"))
        cls.data, cls.changes = era.build_data(cls.draft)

    def test_the_shipped_file_is_what_the_draft_and_the_decisions_give(self):
        self.assertEqual(era.OUT.read_text(encoding="utf-8"), era.render(self.data))
        self.assertEqual(era.main(["--check"]), 0)

    def test_the_data_passes_every_rule(self):
        self.assertEqual(era.validate(self.data), [])

    def test_the_budget_is_the_servers_formula(self):
        self.assertEqual([era.point_budget(m) for m in (10, 15, 20, 22, 25)], [1494, 2223, 2979, 3253, 3706])
        # the design's check: Minstrel Instruments 50 / Stealth 50 / Slash 29 was 759 over
        self.assertEqual(era.build_cost({"Instruments": 50, "Stealth": 50, "Slash": 29}) - era.point_budget(15), 759)

    def test_a_players_autotrain_levels_are_free(self):
        # GamePlayer.GetAutoTrainPoints: an autotrain line costs nothing up to a quarter of the level, 12 at 50 (77 points)
        self.assertEqual(era.AUTOTRAIN_LEVEL_50, 12)
        self.assertEqual([era.line_cost(n, True) for n in (1, 8, 12, 13, 50)], [0, 0, 0, 13, 1274 - 77])
        self.assertEqual(era.line_cost(50), 1274)
        minstrel = {"Instruments": 50, "Stealth": 50, "Slash": 29}
        self.assertEqual(era.build_cost(minstrel, ["Instruments"]) - era.point_budget(15), 759 - 77)
        self.assertEqual(era.build_cost({"Slash": 50, "Thrust": 5, "Shields": 42}, ["Slash", "Thrust"]),
                         era.build_cost({"Slash": 50, "Shields": 42}) - 77)
        for cls, lines in era.AUTOTRAIN.items():
            self.assertTrue(set(lines) <= set(era.CLASSES[cls][3]), cls)
            self.assertEqual(self.data["classes"][cls]["autotrain"], lines)
        self.assertNotIn("Hunter", era.AUTOTRAIN)   # the game's Hunter list is commented out
        self.assertEqual(sum(1 for c in self.data["classes"].values() if c["autotrain"]), 11)

    def test_every_build_is_fully_trained(self):
        # Owner 2026-11-17 18:59: "bots should be fully trained on what our 1.65 and Wayback build scraping showed".
        for cls, c in self.data["classes"].items():
            for b in c["builds"]:
                left = era.points_left(b["specs"], c["pointBudget50"], c["autotrain"])
                self.assertEqual(left, b["pointsLeft"], b["id"])
                self.assertGreaterEqual(left, 0, b["id"])
                for line, level in b["specs"].items():
                    if level < 50:
                        self.assertLess(left, era.next_level_cost(level, line in c["autotrain"]), f"{b['id']} {line}")
                self.assertTrue(era.is_fully_trained(b["specs"], c["pointBudget50"], c["autotrain"]), b["id"])
                for line in set(b["specs"]) ^ set(b["posted"]):
                    self.assertTrue(b["citations"] or line in c["autotrain"], f"{b['id']}: {line}")
        left = [b["pointsLeft"] for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual((min(left), max(left)), (0, 29))

    def test_validate_refuses_a_build_with_points_to_spare(self):
        data = json.loads(era.render(self.data))
        build = data["classes"]["Armsman"]["builds"][0]
        build["specs"]["Parry"] -= 1
        build["pointsLeft"] += build["specs"]["Parry"] + 1
        self.assertTrue(any("points left, the next level of a line costs" in p for p in era.validate(data)))

    def test_the_rest_goes_to_the_remainder_line_and_trims_come_out_of_it(self):
        fit = era.fully_trained
        # under: the lowest posted line takes the rest (Armsman: Slash and Thrust are free up to 12)
        self.assertEqual(fit({"Polearm": 50, "Shields": 42, "Slash": 39, "Parry": 6}, 2979, ["Slash", "Thrust"])["Parry"], 13)
        # over: the lowest line gives the points back, and a line just trimmed is never filled again
        self.assertEqual(fit({"Earth Magic": 50, "Cold Magic": 26, "Wind Magic": 11}, 1494),
                         {"Earth Magic": 50, "Cold Magic": 20, "Wind Magic": 1})
        # lines posted at the same level come down together
        self.assertEqual(fit({"Mentalism": 45, "Light": 25, "Mana": 25}, 1494), {"Mentalism": 45, "Light": 21, "Mana": 21})
        # a source names the line to trim: Stealth, not the Minstrel's weapon
        self.assertEqual(fit({"Instruments": 50, "Stealth": 50, "Slash": 29}, 2223, ["Instruments"], "Stealth"),
                         {"Instruments": 50, "Stealth": 33, "Slash": 30})
        # as posted when it is already fully trained
        posted = {"Mind Magic": 44, "Body Magic": 31}
        self.assertEqual(fit(posted, 1494), posted)
        builds = {b["id"]: b for c in self.data["classes"].values() for b in c["builds"]}
        # a cited "rest into parry": the source decides, not the general practice
        self.assertEqual(builds["armsman-pole-shld-slash"]["specs"]["Parry"], 13)
        self.assertTrue(builds["armsman-pole-shld-slash"]["changeSourced"])
        self.assertIn("rest in Parry", builds["armsman-pole-shld-slash"]["changeBasis"])
        # no source: the general practice, and the data says so
        self.assertFalse(builds["sorcerer-body-mind"]["changeSourced"])
        self.assertIn("remainder line", builds["sorcerer-body-mind"]["changeBasis"])

    def test_every_build_has_dated_posts_and_the_wayback_check_is_recorded(self):
        builds = [b for c in self.data["classes"].values() for b in c["builds"]]
        self.assertTrue(all(b["postedSourced"] and b["sources"] and b["sourceDates"] for b in builds))
        self.assertEqual(sum(1 for b in builds if not b["sourced"]), 3)   # the Necromancer (low confidence)
        self.assertEqual(sum(1 for b in builds if b["wayback"]), 22)
        self.assertEqual(sum(1 for b in builds if b["check"].startswith("matches")), 9)
        for b in builds:
            self.assertEqual(b["specsSourced"], b["sourced"] and (b["change"] is None or b["changeSourced"]), b["id"])
            for entry in b["wayback"]:
                self.assertTrue(entry["site"] and entry["page"] and entry["captured"] and entry["shows"], b["id"])
                self.assertRegex(entry["captured"], r"^200[2-5]-\d\d-\d\d$")
            self.assertEqual(b["changeBasis"] is not None, b["change"] is not None, b["id"])
        self.assertEqual(era.CHECK_DOC.read_text(encoding="utf-8"), era.render_table(self.data))

    def test_the_drafts_106_builds_and_the_researchs_7_new_ones(self):
        builds = [b for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual(len(builds), 113)
        self.assertEqual(sum(1 for b in builds if b["new"]), 7)
        for cls, c in self.data["classes"].items():
            self.assertAlmostEqual(sum(b["weight"] for b in c["builds"]), 1.0, places=3, msg=cls)
            for b in c["builds"]:
                if not b["new"]:
                    draft = next(d for d in self.draft["classes"][cls] if d["label"] == b["draftLabel"])
                    self.assertEqual(b["posted"], era.ordered(draft["specs"]), b["id"])
                else:
                    self.assertTrue(b["citations"] and b["sourced"], b["id"])
                if b["change"] is None:
                    self.assertEqual(b["specs"], b["posted"], b["id"])
                self.assertFalse(b["weightSourced"], b["id"])
        # ids stay those of the draft (saved plans keep reading): the research changed labels, never ids
        ids = [b["id"] for b in builds if not b["new"]]
        self.assertEqual(len(ids), 106)
        self.assertIn("champion-lw-valor-blades", ids)
        self.assertIn("bard-regrow-nurt", ids)

    def test_the_research_templates_and_the_class_review(self):
        builds = {b["id"]: b for c in self.data["classes"].values() for b in c["builds"]}
        # review of 1f3dcefe2, must fix: the Scout keeps a bow, the Warden a weapon
        self.assertEqual(builds["scout-thrust-shld"]["specs"], {"Thrust": 50, "Shields": 42, "Stealth": 35, "Longbows": 21})
        self.assertEqual(builds["warden-nurt-regrow"]["specs"], {"Nurture": 49, "Regrowth": 33, "Blades": 29, "Parry": 3})
        # the research's templates where it and the review differ
        self.assertEqual(builds["theurgist-earth-cold"]["specs"], {"Earth Magic": 48, "Cold Magic": 24, "Wind Magic": 6})
        self.assertEqual(builds["champion-lw-valor-blades"]["specs"], {"Valor": 50, "Shields": 42, "Blades": 39, "Parry": 6})
        self.assertEqual(builds["champion-lw-valor-blades"]["family"], "OneHandAndShield")
        self.assertNotEqual(builds["infiltrator-thrust-env"]["specs"], builds["infiltrator-thrust-dw"]["specs"])
        self.assertEqual(builds["infiltrator-thrust-env"]["specs"]["Critical Strike"], 39)
        self.assertEqual(builds["minstrel-inst-slash"]["label"], "Instruments/Slash (solo)")
        for b in builds.values():
            if b["base"].startswith("Uthgard"):
                self.assertTrue(b["citations"], b["id"])
                for c in b["citations"]:
                    self.assertTrue(c["url"].startswith("https://") and c["site"] and c["says"], b["id"])
            if b["note"] and "class review" in b["note"]:
                self.assertTrue(b["base"].startswith("Uthgard"), b["id"])
        # low confidence: the Necromancer stays as posted, not sourced
        for b in self.data["classes"]["Necromancer"]["builds"]:
            self.assertFalse(b["sourced"])
            self.assertEqual(b["base"], "dated 2002-2003 posts")
        # every change rests on something; a source where it says so
        for b in builds.values():
            if b["changeSourced"]:
                self.assertTrue(b["citations"] or "autotrain" in b["changeBasis"] or b["wayback"], b["id"])

    def test_every_autotrain_line_is_at_its_free_level(self):
        for cls, c in self.data["classes"].items():
            for b in c["builds"]:
                for line in c["autotrain"]:
                    self.assertGreaterEqual(b["specs"].get(line, 1), era.AUTOTRAIN_LEVEL_50, f"{b['id']} {line}")
        builds = {b["id"]: b for c in self.data["classes"].values() for b in c["builds"]}
        self.assertEqual(builds["armsman-pole-shld-slash"]["specs"]["Thrust"], 12)
        self.assertEqual(builds["warrior-hammer-shld"]["specs"]["Sword"], 12)

    def test_validate_refuses_a_big_fill_without_a_source_and_an_untrained_autotrain_line(self):
        data = json.loads(era.render(self.data))
        b = next(b for b in data["classes"]["Necromancer"]["builds"])
        line = next(k for k, v in b["specs"].items() if v < 30)
        b["specs"][line] += 11
        self.assertTrue(any("levels without a source" in p for p in era.validate(data)))
        data = json.loads(era.render(self.data))
        data["classes"]["Armsman"]["builds"][0]["specs"]["Thrust"] = 1
        self.assertTrue(any("below its free" in p for p in era.validate(data)))

    def test_the_owners_decisions(self):
        midgard = {c: e["share"] for c, e in self.data["realms"]["Midgard"]["classShare"].items()}
        for cls, share in {"Thane": 0.08, "Runemaster": 0.09, "Berserker": 0.08, "Shadowblade": 0.10, "Hunter": 0.05,
                           "Savage": 0.06, "Healer": 0.15, "Shaman": 0.09}.items():
            self.assertAlmostEqual(midgard[cls], share, msg=cls)
        wizard = {b["label"]: b["weight"] for b in self.data["classes"]["Wizard"]["builds"]}
        self.assertEqual(wizard, {"Fire": 0.60, "Ice PBAE": 0.15, "Earth": 0.25})
        self.assertEqual((self.data["offMetaPercent"], self.data["jitterPoints"]), (10, 2))
        for realm, info in self.data["realms"].items():
            self.assertAlmostEqual(sum(e["share"] for e in info["classShare"].values()), 1.0, places=3, msg=realm)
            self.assertFalse(info["sharesSourced"])
            self.assertTrue(all(not e["sourced"] for e in info["classShare"].values()))

    def test_bots_never_play_the_custom_class(self):
        self.assertNotIn("Sluaghbinder", self.data["classes"])
        self.assertNotIn("Sluaghbinder", self.data["realms"]["Hibernia"]["classShare"])
        self.assertIn("Sluaghbinder", self.data["excludedClasses"])
        self.assertEqual(len(self.data["realms"]["Hibernia"]["classShare"]), 13)

    def test_ids_are_unique_and_stable(self):
        ids = [b["id"] for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertIn("sorcerer-mind-body", ids)
        self.assertIn("warrior-axe-shld42", ids)
        self.assertIn("warrior-axe-shld50", ids)


if __name__ == "__main__":
    unittest.main()
