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
                self.assertEqual(set(b["specs"]), set(b["posted"]), f"{b['id']}: no line added or dropped")
        left = [b["pointsLeft"] for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual((min(left), max(left)), (0, 42))

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
        # over: the lowest line gives the points back, then the rest is spent again (the period's Earth 50 / Ice 20 / Air 4)
        self.assertEqual(fit({"Earth Magic": 50, "Cold Magic": 26, "Wind Magic": 11}, 1494),
                         {"Earth Magic": 50, "Cold Magic": 20, "Wind Magic": 4})
        # lines posted at the same level come down together
        self.assertEqual(fit({"Mentalism": 45, "Light": 25, "Mana": 25}, 1494), {"Mentalism": 45, "Light": 21, "Mana": 21})
        # a source names the line to trim: Stealth, not the Minstrel's weapon
        self.assertEqual(fit({"Instruments": 50, "Stealth": 50, "Slash": 29}, 2223, ["Instruments"], "Stealth"),
                         {"Instruments": 50, "Stealth": 33, "Slash": 30})
        # as posted when it is already fully trained
        posted = {"Mind Magic": 44, "Body Magic": 31}
        self.assertEqual(fit(posted, 1494), posted)
        builds = {b["id"]: b for c in self.data["classes"].values() for b in c["builds"]}
        self.assertEqual(builds["infiltrator-thrust-env"]["specs"]["Dual Wield"], 34)   # "rest dual wield"
        self.assertTrue(builds["infiltrator-thrust-env"]["changeSourced"])
        self.assertEqual(builds["warden-nurt-regrow"]["specs"], {"Nurture": 49, "Regrowth": 33, "Parry": 29})
        self.assertTrue(builds["warden-nurt-regrow"]["changeSourced"])                  # "the remainder in parry"
        self.assertFalse(builds["armsman-pole-shld-slash"]["changeSourced"])            # the general practice only
        self.assertIn("remainder line", builds["armsman-pole-shld-slash"]["changeBasis"])

    def test_every_build_has_dated_posts_and_the_wayback_check_is_recorded(self):
        builds = [b for c in self.data["classes"].values() for b in c["builds"]]
        self.assertTrue(all(b["postedSourced"] and b["sources"] and b["sourceDates"] for b in builds))
        self.assertEqual(sum(1 for b in builds if b["wayback"]), 22)
        self.assertEqual(sum(1 for b in builds if b["check"].startswith("matches")), 9)
        for b in builds:
            self.assertEqual(b["specsSourced"], b["change"] is None, b["id"])
            for entry in b["wayback"]:
                self.assertTrue(entry["site"] and entry["page"] and entry["captured"] and entry["shows"], b["id"])
                self.assertRegex(entry["captured"], r"^200[2-5]-\d\d-\d\d$")
            self.assertEqual(b["changeBasis"] is not None, b["change"] is not None, b["id"])
        self.assertEqual(era.CHECK_DOC.read_text(encoding="utf-8"), era.render_table(self.data))

    def test_nine_trims_forty_fills_and_the_rest_as_posted(self):
        builds = [b for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual(len(builds), 106)
        self.assertEqual(sum(1 for b in builds if (b["change"] or "").startswith("trimmed")), 9)
        self.assertEqual(sum(1 for b in builds if (b["change"] or "").startswith("filled")), 40)
        for b in builds:
            draft = next(d for d in self.draft["classes"][b["id"].split("-")[0].capitalize()] if d["label"] == b["label"])
            self.assertEqual(b["posted"], era.ordered(draft["specs"]), b["id"])
            if b["change"] is None:
                self.assertEqual(b["specs"], b["posted"], b["id"])
            self.assertFalse(b["weightSourced"], b["id"])

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
