"""Owner, 2026-11-22 (HEROES.md decision 11): gamebots and hired companions don't take the names the lore gives its own
people. The lore keeps its names; the bot name lists leave them out."""
import glob, os, re, unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
LORE = os.path.join(ROOT, "source", "server", "GameServer", "bots", "autonomous", "data", "lore")
ERA = os.path.join(ROOT, "source", "server", "GameServer", "bots", "autonomous", "EraBotNames.cs")
HIRE = os.path.join(ROOT, "source", "server", "GameServer", "mode165", "Hire165.cs")
# Written lists only; the everyday list (Dave, Nate, Faye) and the syllable generators are not lore names.
LEFT_OUT_OF_EVERYDAY = ["Sven", "Gareth", "Cody", "Tobias"]


def lore_text():
    return " ".join(open(f, encoding="utf-8").read() for f in glob.glob(os.path.join(LORE, "**", "*.json"), recursive=True))


def lore_name_lists():
    s = open(ERA, encoding="utf-8").read()
    a = s.index("LoreNameLists = new()")
    block = s[a:s.index("#endregion", a)]
    return {m.group(1) + m.group(2): re.findall(r'"([^"]+)"', m.group(3))
            for m in re.finditer(r"\[\((\d), (true|false)\)\] = \[([^\]]*)\]", block)}


class BotNamesLeaveTheLoreItsNames(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lore = lore_text()

    def used(self, name):
        return re.search(r"\b" + re.escape(name) + r"\b", self.lore) is not None

    def test_the_lore_name_lists_use_no_name_the_lore_uses(self):
        lists = lore_name_lists()
        self.assertEqual(len(lists), 6)
        clashes = {k: [n for n in v if self.used(n)] for k, v in lists.items()}
        self.assertEqual({k: v for k, v in clashes.items() if v}, {})
        for k, v in lists.items():
            self.assertGreaterEqual(len(v), 10, k)

    def test_the_everyday_list_leaves_out_the_lores_people(self):
        s = open(ERA, encoding="utf-8").read()
        everyday = s[s.index("MaleFirstNames ="):s.index("FemaleFirstNames =")]
        for n in LEFT_OUT_OF_EVERYDAY:
            self.assertNotIn(f'"{n}"', everyday)

    def test_midgards_companions_leave_out_sven(self):
        self.assertNotIn('"Sven"', open(HIRE, encoding="utf-8").read())


if __name__ == "__main__":
    unittest.main()
