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

    def test_twelve_trims_seven_fills_and_the_rest_as_posted(self):
        builds = [b for c in self.data["classes"].values() for b in c["builds"]]
        self.assertEqual(len(builds), 106)
        self.assertEqual(sum(1 for b in builds if (b["change"] or "").startswith("trimmed")), 12)
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
