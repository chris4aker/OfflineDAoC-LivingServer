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
        self.assertEqual(name_counts.problems(c), [])

    def test_the_retired_witnesses_stay_retired(self):
        import name_counts
        c, _ = name_counts.count()
        for name in ["Dame Ysolde", "Brother Cadoc", "Sir Cador", "Sir Geraint", "Brother Odran"]:
            self.assertLessEqual(c[name], 6, name)

if __name__ == "__main__":
    unittest.main()
