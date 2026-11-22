import os, sys, unittest
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import reachability as R


def loc(id, regions=(), zones=None, first=("A line.",), areas=()):
    d = {"id": id, "regions": list(regions), "atmosphere": {"firstEntry": list(first), "areas": list(areas)}}
    if zones is not None:
        d["zones"] = list(zones)
    return d


class MirrorTests(unittest.TestCase):
    def test_location_at_is_zone_first_then_narrowest(self):
        locs = [loc("open", [200]), loc("lair", [200], [204]), loc("wide", [200, 1])]
        self.assertEqual(R.location_at(locs, 200, 204), "lair")
        self.assertEqual(R.location_at(locs, 200, 201), "open")
        self.assertEqual(R.location_at(locs, 1, 0), "wide")
        self.assertIsNone(R.location_at(locs, 5, 0))

    def test_never_here_matches_the_engine(self):
        # LoreEntryRules.NeverHere: no zones, first-entry lines, and every zone of every listed region is another file's.
        isles = loc("isles", [], areas=[{"name": "a", "region": 151, "x": 1, "y": 1, "z": 0, "radius": 500}])
        alb = loc("alb", [51])
        glass = loc("glass", [51], [51, 52])
        locs = [isles, alb, glass]
        zones = {51: [51, 52]}
        self.assertTrue(R.never_here(locs, isles, zones))
        self.assertTrue(R.never_here(locs, alb, zones))
        self.assertFalse(R.never_here(locs, glass, zones), "a zone file")
        self.assertFalse(R.never_here(locs, alb, {51: [51, 52, 53]}), "zone 53 is its own")
        self.assertFalse(R.never_here(locs, alb, {}), "zones not known")
        self.assertEqual(R.regions_covered(isles), [151])

    def test_book_drops_need_a_listed_region(self):
        book = {"title": "T", "pages": ["p"]}
        a = dict(loc("a", [51], areas=[{"name": "x", "x": 1, "y": 1, "z": 0, "radius": 500}]), book=book)
        b = dict(loc("b", [51]), book=book)
        c = dict(loc("c", [], areas=[{"name": "y", "region": 51, "x": 1, "y": 1, "z": 0, "radius": 500}]), book=book)
        self.assertTrue(R.book_drops([a, b, c], a), "near its own area")
        self.assertFalse(R.book_drops([a, b, c], b), "a shared region, no area")
        self.assertFalse(R.book_drops([a, b, c], c), "lists no region")
        self.assertTrue(R.book_drops([b], b), "the only file of its region")

    def test_triggers_sort_into_kinds(self):
        self.assertEqual([R.trigger_kind(t) for t in ["health<=50", "health <= 10", "addsDead", "partnerDead", "wake", "death", "glare", "health<=0"]],
                         ["health", "health", "adds", "partner", "wake", "event", "mechanic", "bad"])


class LoreTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows, cls.readers, cls.drops, cls.locs = R.build()

    def test_every_reader_is_still_in_the_code(self):
        missing = [k for k, v in self.readers.items() if v["reader"] != "nothing" and not v["found"]]
        self.assertEqual(missing, [])

    def test_unreachable_counts_stay_under_their_caps(self):
        over = {k: n for k, n in R.unreachable(self.rows).items() if n > R.KNOWN_UNREACHABLE.get(k, 0)}
        self.assertEqual(over, {})

    def test_every_first_entry_can_show(self):
        # 1.2.1: the overview files (proving fields, Shrouded Isles...) show theirs on walking into a region they cover.
        self.assertEqual([r["file"] for r in self.rows if r["field"] == "firstEntry" and not r["ok"]], [])
        overviews = {r["file"] for r in self.rows if r["why"].startswith("overview")}
        self.assertIn("locations/proving-fields.json", overviews)
        self.assertIn("locations/albion-shrouded-isles.json", overviews)


if __name__ == "__main__":
    unittest.main()
