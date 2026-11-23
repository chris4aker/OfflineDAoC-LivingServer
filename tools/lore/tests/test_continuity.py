import os, sys, unittest
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import continuity as C


def bad(result):
    return {k: [b[3] for b in v["bad"]] for k, v in result.items() if v["bad"]}


class Numbers(unittest.TestCase):
    def test_number_words(self):
        self.assertEqual([C.number(w) for w in ["sixty-seven", "Eighty", "80", "1,200", "eleven", "thirty-one", "third", "many"]],
                         [67, 80, 80, 1200, 11, 31, None, None])


class LedgerRules(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ledger = C.load_ledger()

    def run_on(self, *files):
        return bad(C.check(list(files), self.ledger))

    def test_the_real_tellings_pass(self):
        caer = ("locations/caer-sidi", {"id": "caer-sidi", "story":
                "The third went up eighty strong behind Ennyn. They lost nine in the crypts, two fell to the Silencer in the inner "
                "halls, and two stayed in the gallery of the lich. Sixty-seven came home. Arthur went in three ships and seven came home."})
        self.assertEqual(self.run_on(caer), {})

    def test_a_different_count_is_flagged(self):
        caer = ("locations/caer-sidi", {"id": "caer-sidi", "story": "The third went up eighty strong. Seventy-one came home."})
        self.assertEqual(self.run_on(caer), {"caer-sidi-third-home": ["Seventy-one"]})

    def test_a_boss_of_the_place_is_in_its_scope(self):
        boss = ("bosses/x", {"id": "x", "locationId": "caer-sidi", "chapters": [{"title": "t", "text": "Sixty came home."}]})
        other = ("bosses/y", {"id": "y", "locationId": "krondon", "story": "Sixty came home."})
        self.assertEqual(self.run_on(boss, other), {"caer-sidi-third-home": ["Sixty"]})

    def test_forbidden_fates_are_flagged(self):
        files = [("bosses/master-of-pain", {"id": "master-of-pain", "bookPages": ["I went back up the tower in the spring."]}),
                 ("bosses/kierac-the-destroyer", {"id": "kierac-the-destroyer", "story": "Bréanainn never went back up the tower."}),
                 ("bosses/dyranapur", {"id": "dyranapur", "story": "Ulla carried the cup for forty winters. She carried it for thirty dark moons."})]
        got = self.run_on(*files)
        self.assertEqual(got["breanainn-never-climbs-again"], ["went back up"])
        self.assertEqual(got["dyranapur-dark-moons"], ["thirty"])
        self.assertEqual(got["dyranapur-forty-winters-is-after"], ["carried the cup for forty winters"])

    def test_a_tally_must_count_the_hush(self):
        told = ("bosses/apocalypse", {"id": "apocalypse", "locationId": "caer-sidi", "story":
                "Eighty went up; nine stayed in the crypts, two to the hush, two in the gallery; sixty-seven came home."})
        short = ("bosses/apocalypse", {"id": "apocalypse", "locationId": "caer-sidi", "story":
                 "Eighty went up; they lost nine to the Crypt Lord's court and two in the gallery; sixty-seven came home."})
        self.assertNotIn("caer-sidi-tally-counts-the-hush", self.run_on(told))
        self.assertIn("caer-sidi-tally-counts-the-hush", self.run_on(short))

    def test_unless_keeps_an_allowed_form(self):
        ok = ("bosses/wenoiak-the-enlightened", {"id": "wenoiak-the-enlightened", "story":
              "Raiders found only ash; the readers think he moves the books before any company climbs."})
        no = ("bosses/wenoiak-the-enlightened", {"id": "wenoiak-the-enlightened", "story": "Raiders found only ash in his observatory."})
        self.assertNotIn("wenoiak-books", self.run_on(ok))
        self.assertIn("wenoiak-books", self.run_on(no))


class LoreLedger(unittest.TestCase):
    def test_every_ledger_fact_is_still_told_somewhere(self):
        # A pattern that matches nothing any more is a stale entry: fix the pattern, or the fact left the lore.
        result = C.check(C.load_lore(), C.load_ledger())
        self.assertEqual([k for k, v in result.items() if not v["told"]], [])

    def test_ids_are_unique(self):
        ids = [e["id"] for e in C.load_ledger()]
        self.assertEqual(len(ids), len(set(ids)))

    def test_suggestions_leave_out_what_the_ledger_reads(self):
        rows = C.suggestions(C.load_lore(), C.load_ledger())
        self.assertTrue(rows)
        self.assertFalse(any(k == ("Hallbjorn's", "eighty") for _, _, k, _ in rows))


if __name__ == "__main__":
    unittest.main()
