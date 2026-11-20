import os, sys, unittest
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import lint_lore, clean_lore
from lore_text import DEV_NOTE

class LintTests(unittest.TestCase):
    def test_the_lore_files_pass(self):
        problems, _ = lint_lore.lint()
        self.assertEqual(problems, [])

    def test_dev_notes_are_caught(self):
        for s in ["Untested in game with bots: x.", "Darkness Falls has no raid tactics in our raid AI yet.", "A controller in the soil sends it."]:
            self.assertTrue(DEV_NOTE.search(s), s)
        self.assertFalse(DEV_NOTE.search("The old raiders swore frost healed him."))

    def test_cleaner_drops_dev_notes_and_duplicates_only(self):
        d = {"legend": "He hits hard. Untested in game with bots: x. Bring a healer.", "yells": {"aggro": ["a", "b", "a"]}}
        clean_lore.clean(d)
        self.assertEqual(d["legend"], "He hits hard. Bring a healer.")
        self.assertEqual(d["yells"]["aggro"], ["a", "b"])

class ProvenanceTests(unittest.TestCase):
    def test_mythic_needs_a_mythic_source(self):
        from lore_text import MYTHIC_SOURCE
        for ok in ["https://www.camelotherald.com/news/x", "Camelot Herald, Nov 2001", "darkageofcamelot.com patch notes", "Mythic patch 1.65"]:
            self.assertTrue(MYTHIC_SOURCE.search(ok), ok)
        for bad in ["https://camelot.allakhazam.com/db/mob.html?x", "Prima Official eGuide", "www.gamebanshee.com"]:
            self.assertFalse(MYTHIC_SOURCE.search(bad), bad)

    def test_relabel_changes_only_the_label(self):
        import json
        d = {"facts": [{"text": "t", "provenance": "mythic", "source": "https://camelot.allakhazam.com/x"},
                       {"text": "u", "provenance": "mythic", "source": "https://camelotherald.com/y"}]}
        from lore_text import all_facts, MYTHIC_SOURCE
        for x in all_facts(d):
            if x["provenance"] == "mythic" and not MYTHIC_SOURCE.search(x["source"]): x["provenance"] = "legend"
        self.assertEqual([x["provenance"] for x in d["facts"]], ["legend", "mythic"])

if __name__ == "__main__":
    unittest.main()
