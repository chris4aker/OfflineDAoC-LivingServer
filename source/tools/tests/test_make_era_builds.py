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

    def test_the_builds_autotrain_lets_fit_are_as_posted_again(self):
        builds = {b["id"]: (cls, b) for cls, c in self.data["classes"].items() for b in c["builds"]}
        for build_id in ("shadowblade-axe-la", "shadowblade-axe-cs", "ranger-bow-path"):
            cls, b = builds[build_id]
            info = self.data["classes"][cls]
            self.assertIsNone(b["change"], build_id)
            self.assertEqual(b["specs"], b["posted"], build_id)
            self.assertGreater(era.build_cost(b["posted"]), info["pointBudget50"], f"{build_id}: over, every level counted")
            self.assertLessEqual(era.build_cost(b["posted"], info["autotrain"]), info["pointBudget50"], build_id)
        # the two Minstrel builds are still over: the same line is trimmed, by less, to the most that fits
        for build_id, line, level in (("minstrel-inst-stl", "Stealth", 33), ("minstrel-inst-slash", "Slash", 42)):
            cls, b = builds[build_id]
            info = self.data["classes"][cls]
            self.assertEqual(b["specs"][line], level, build_id)
            self.assertLessEqual(era.build_cost(b["specs"], info["autotrain"]), info["pointBudget50"])
            self.assertGreater(era.build_cost({**b["specs"], line: level + 1}, info["autotrain"]), info["pointBudget50"])
        # every trim left is one a player's points still need
        for cls, c in self.data["classes"].items():
            for b in c["builds"]:
                if (b["change"] or "").startswith("trimmed"):
                    self.assertGreater(era.build_cost(b["posted"], c["autotrain"]), c["pointBudget50"], b["id"])

    def test_nine_trims_seven_fills_and_the_rest_as_posted(self):
        builds = [b for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual(len(builds), 106)
        self.assertEqual(sum(1 for b in builds if (b["change"] or "").startswith("trimmed")), 9)
        self.assertEqual(sum(1 for b in builds if (b["change"] or "").startswith("filled")), 7)
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
