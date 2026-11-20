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


class NameCountTests(unittest.TestCase):
    def test_no_named_person_is_overused(self):
        import name_counts
        c, _ = name_counts.count()
        self.assertEqual(name_counts.problems(c, name_counts.hero_count()), [])

    def test_heroes_are_counted_by_bare_name(self):
        import name_counts
        h = name_counts.hero_count()
        self.assertEqual(set(h), set(name_counts.HEROES))
        self.assertGreater(h["Galahad"], 0)

    def test_the_old_witnesses_stay_under_the_cap(self):
        # Owner, 2026-11-20: they may keep a name where it adds gravity, never as the default teller again.
        import name_counts
        c, _ = name_counts.count()
        for name in ["Dame Ysolde", "Brother Cadoc", "Sir Cador", "Sir Geraint", "Brother Odran"]:
            self.assertLessEqual(c[name], name_counts.MAX_MENTIONS, name)
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
