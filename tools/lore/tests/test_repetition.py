import json, os, sys, tempfile, unittest
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import repetition as R


def write(root, rel, d):
    path = os.path.join(root, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(d, fh)


class RepetitionRules(unittest.TestCase):
    def test_a_phrase_in_three_files_is_found_and_grown(self):
        line = "the old drovers of the cornish road swear it"
        items = [(f"f{i}", "Albion", f"Once upon a time. {line} at dusk {i}.") for i in range(3)]
        items.append(("g", "Albion", "Nothing like it here at all, the old drovers of the cornish road swear it."))
        merged, grams, covered = R.phrases(items)
        self.assertIn("the old drovers of the cornish road swear it", merged)
        e = merged["the old drovers of the cornish road swear it"]
        self.assertEqual((len(e["files"]), e["count"]), (4, 4))
        self.assertEqual(grams["the old drovers of the cornish"], 4)
        self.assertGreater(covered["Albion"], 0)

    def test_two_files_are_not_a_repetition(self):
        merged, grams, _ = R.phrases([("a", "Midgard", "the frost giants of the high fells came"), ("b", "Midgard", "the frost giants of the high fells came")])
        self.assertEqual((merged, grams), ({}, {}))

    def test_choices_read_as_their_first_and_templates_are_skipped(self):
        self.assertEqual(R.tokens("the [old|grey] wall’s stones"), ["the", "old", "wall's", "stones"])

    def test_measure_and_the_ratchet(self):
        with tempfile.TemporaryDirectory() as root:
            for i in range(3):
                write(root, f"locations/p{i}.json", {"id": f"p{i}", "realm": "Hibernia",
                       "story": "From Coll of the Domnann guides the tale goes that the bog never once slept.",
                       "book": {"title": "T", "pages": ["From Coll, a page.", "Of the bog, a page."]},
                       "chronicle": {"win": "{realm} won under {raidLeader} at the bog"}})
            m = R.measure(root)
            self.assertEqual(m["openings"]["Hibernia"]["From Coll,"], 3)
            self.assertEqual(m["formulas"]["\"never once\""]["Hibernia"], 3)
            self.assertTrue(any(p.startswith("from coll of the domnann guides") for p in m["narrative"]["phrases"]))
            caps = R.current_caps(m)
            self.assertEqual(R.problems(m, caps), [], "the current counts pass their own caps")
            caps["openings"]["Hibernia"]["From Coll,"] = 2
            caps["formula_rates"]["Hibernia"]["\"never once\""] = 0.0
            got = R.problems(m, caps)
            self.assertTrue(any("page opening \"From Coll,\": 3 pages, cap 2" in p for p in got), got)
            self.assertTrue(any("never once" in p for p in got), got)


class LoreRepetition(unittest.TestCase):
    def test_the_lore_does_not_repeat_itself_more_than_its_caps(self):
        # The ratchet (tools/lore/repetition_caps.json): lower the caps when a rewrite brings a count down.
        self.assertEqual(R.problems(R.measure(), R.load_caps()), [])


if __name__ == "__main__":
    unittest.main()
