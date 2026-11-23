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

    def test_every_book_page_is_read(self):
        # LoreBible.BookPagesOf joins "bookPage" and "bookPages", each one page or a list, so the lint reads them all.
        note = "Untested in game with bots: x."
        for d, key in [({"bookPage": ["A fine page.", note]}, "bookPage[1]"), ({"bookPages": [note]}, "bookPages[0]"),
                       ({"bookPages": note}, "bookPages"), ({"bookPage": note}, "bookPage")]:
            hits = [k for k, t in lint_lore.texts(d) if any(DEV_NOTE.search(s) for s in lint_lore.sentences(t))]
            self.assertEqual(hits, [key], d)

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


class CompanionNameTests(unittest.TestCase):
    """Owner 2026-11-21 (decision 7): hired companions may carry lesser legend names, never a name the lore tells of.
    The check covers every legend name the lore tells of (name_counts.legend_names: HEROES, the HEROES.md roster, the
    names in "myth" facts and in legend-age sentences), less the ordinary given names present-day characters share."""
    def suggested(self):
        import re
        root = os.path.join(os.path.dirname(__file__), "..", "..", "..")
        src = open(os.path.join(root, "source", "server", "GameServer", "mode165", "Hire165.cs"), encoding="utf-8").read()
        block = src[src.index("Names = new()"):src.index("};", src.index("Names = new()"))]
        return re.findall(r'"([A-Za-z]+)"', block)

    def test_no_suggested_companion_name_is_a_lore_hero(self):
        import re, name_counts
        names = self.suggested()
        self.assertGreater(len(names), 30)
        clashes = [n for n in names for rx in name_counts.HEROES.values() if re.fullmatch(rx, n)]
        self.assertEqual(clashes, [])

    def test_no_suggested_companion_name_is_a_legend_the_lore_tells_of(self):
        import name_counts
        self.assertEqual(name_counts.companion_clashes(self.suggested()), [])

    def test_a_legend_told_in_the_lore_is_caught(self):
        # Review H3: Grainne was told of by name in giant-caithor while "Grainne" was a suggested companion.
        import json, shutil, tempfile, name_counts
        tmp = tempfile.mkdtemp()
        try:
            os.makedirs(os.path.join(tmp, "bosses"))
            boss = {"id": "giant-caithor", "name": "Giant Caithor",
                    "facts": [{"text": "Diarmuid fled with Gráinne.", "provenance": "myth", "source": "Toraigheacht"}],
                    "story": "Diarmuid and Gráinne slept under the quicken tree."}
            json.dump(boss, open(os.path.join(tmp, "bosses", "giant-caithor.json"), "w", encoding="utf-8"), ensure_ascii=False)
            self.assertIn("Grainne", [n for n, _ in name_counts.companion_clashes(self.suggested(), tmp)])
            # Told only in a legend-age story, not in a fact, it is still caught.
            boss["facts"] = [{"text": "x", "provenance": "original"}]
            json.dump(boss, open(os.path.join(tmp, "bosses", "giant-caithor.json"), "w", encoding="utf-8"), ensure_ascii=False)
            self.assertIn("Grainne", [n for n, _ in name_counts.companion_clashes(["Grainne"], tmp)])
            # Told beside a god who is not a HEROES legend (independent review), it is caught too.
            boss["story"] = "In the old tale Fand left the sea-god Manannán for a mortal."
            json.dump(boss, open(os.path.join(tmp, "bosses", "giant-caithor.json"), "w", encoding="utf-8"), ensure_ascii=False)
            self.assertIn("Fand", [n for n, _ in name_counts.companion_clashes(["Fand"], tmp)])
            # A present-day character's ordinary name (decision 7: Edith, Gunnar, Aine) is not a clash.
            boss["story"] = "Cu Chulainn's charioteer drank with Aine and Gunnar."
            json.dump(boss, open(os.path.join(tmp, "bosses", "giant-caithor.json"), "w", encoding="utf-8"), ensure_ascii=False)
            self.assertEqual(name_counts.companion_clashes(["Aine", "Gunnar", "Maud"], tmp), [])
        finally:
            shutil.rmtree(tmp)
