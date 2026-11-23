"""depth_report.py mirrors LoreBible.DepthReport: every floor, both ways, on fixtures/depth, and the C# test's own fixture."""
import io, json, os, sys, unittest
from contextlib import redirect_stdout
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import depth_report as d

HERE = os.path.dirname(os.path.abspath(__file__))
FIX = os.path.join(HERE, "fixtures", "depth")
CS_FIX = os.path.join(HERE, "..", "..", "..", "source", "server", "Tests", "UnitTests", "LoreFixtures")


def shorts(r, kind):
    g = next(g for g in r["groups"] if g["kind"] == kind)
    return {f["file"]: [s for m in f["missed"] for s in m["short"]] for f in g["files"]}


class WordsTests(unittest.TestCase):
    def test_words_split_on_dotnet_whitespace(self):
        self.assertEqual(d.words(None), 0)
        self.assertEqual(d.words(" \t\n "), 0)
        self.assertEqual(d.words("a  b\nc"), 3)
        self.assertEqual(d.words("a b c d\x85e"), 5)
        self.assertEqual(d.words("a\x1cb"), 1, "U+001C is no .NET whitespace (Python's split takes it)")
        self.assertEqual(d.words("a​b"), 1)


class FixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.r = d.report(FIX)

    def test_every_summary_line(self):
        expected = [
            "locations: story 500+ words: 1 of 5 below",
            "locations: legend 120+ words: 2 of 5 below",
            "locations: 6+ facts: 2 of 5 below",
            "locations: 4-8 firstEntry lines: 2 of 5 below",
            "locations: an area per room (2+ areas): 2 of 5 below",
            "locations: lore 12+, rumour 8+, tell 6+: 3 of 5 below",
            "locations: loremaster 8+ topics: 3 of 5 below",
            "locations: book 4-8 pages of 100+ words: 3 of 5 below",
            "locations: an era and a faction: 2 of 5 below",
            "bosses: story 250+ words: 1 of 3 below",
            "bosses: legend 100+ words: 1 of 3 below",
            "bosses: aggro 3+, death 2+, slay 3+ yells: 1 of 3 below",
            "bosses: lairEntry 2+ and realmFirstKill: 1 of 3 below",
            "bosses: briefing, aftermathWin, aftermathLoss, rumour 4+ each: 1 of 3 below",
            "bosses: loremasterTopic and bookPage 100+ words: 2 of 3 below",
            "bosses: chronicle win and loss: 1 of 3 below",
            "bosses: an era, a faction or related: 1 of 3 below",
            "minor bosses: story 150+ words: 1 of 2 below",
            "minor bosses: legend 60+ words: 1 of 2 below",
            "minor bosses: 2+ facts: 1 of 2 below",
            "minor bosses: aggro 2+, death 1+, slay 1+ yells: 1 of 2 below",
            "minor bosses: lairEntry 1+: 1 of 2 below",
            "minor bosses: briefing, aftermathWin, rumour 2+ each: 1 of 2 below",
            "minor bosses: loremasterTopic and chronicle win: 1 of 2 below",
            "epic locations: 4-10 chapters: 1 of 2 below",
            "epic locations: chapters 3,000+ words: 0 of 2 below",
            "epic locations: story summary 300-500 words: 1 of 2 below",
            "epic locations: book 8-16 pages: 1 of 2 below",
            "epic bosses: 4+ chapters: 1 of 2 below",
            "epic bosses: chapters 1,000+ words (1,800+ for raid finals and dragons): 1 of 2 below",
            "epic bosses: story summary 300-500 words: 1 of 2 below",
            "epic bosses: bookPage 2-4 pages: 1 of 2 below",
            "history: Albion: 2 eras, 1 factions",
            "history: Midgard: no file yet",
            "history: Hibernia: no file yet",
        ]
        self.assertEqual(self.r["summary"], expected)

    def test_bad_files_are_problems_not_entries(self):
        self.assertEqual(len(self.r["problems"]), 2)
        self.assertTrue(self.r["problems"][0].startswith("history/hibernia.json: not valid JSON"))
        self.assertEqual(self.r["problems"][1], "stray/where.json: lore files go in locations/, bosses/ or history/")

    def test_location_floors_and_reading(self):
        s = shorts(self.r, "location")
        self.assertNotIn("locations/full.json", s)
        self.assertNotIn("locations/epic-place.json", s, "an epic location meeting the full floor")
        self.assertEqual(s["locations/thin.json"], [
            "story 3<500", "legend 0<120", "facts 2<6", "firstEntry 2<4", "areas 1<2", "lore 1<12", "rumour 0<8", "tell 0<6",
            "loremaster topics 6<8", "page 2 99<100", "page 3 0<100", "no era", "no faction"])
        self.assertEqual(s["locations/no-book.json"], ["tell 5<6", "no loremaster", "no book"])
        self.assertIn("locations/epic-good.json", s, "the location floors count epic locations too, as the C# does")
        self.assertEqual(s["locations/epic-good.json"][0], "legend 0<120", "but not the story 500+ floor: a 300-word epic summary holds")

    def test_major_includes_order_and_joins_book_pages(self):
        s = shorts(self.r, "major")
        self.assertNotIn("bosses/full-major.json", s)
        self.assertEqual(s["bosses/joined-pages.json"], ["page 2 40<100"], "bookPage text + bookPages list, blank pages dropped")
        self.assertEqual(s["bosses/thin-major.json"], [
            "story 249<250", "legend 99<100", "aggro yells 2<3", "death yells 1<2", "slay yells 2<3", "lairEntry 1<2",
            "no realmFirstKill", "briefing 3<4", "aftermathLoss 0<4", "no loremasterTopic",
            "page 1 0<100",  # "bookPage": "   " is one page (Str length > 0); the blank list entries are dropped
            "no chronicle loss", "no era, faction or related"])
        bosses = {b["file"]: b for b in d.load(FIX)[1]}
        self.assertEqual(len(bosses["bosses/joined-pages.json"]["bookPages"]), 2)
        self.assertEqual(len(bosses["bosses/epic-boss.json"]["bookPages"]), 2)
        self.assertEqual(len(bosses["bosses/epic-thin.json"]["bookPages"]), 1)

    def test_minor_and_epic_floors(self):
        self.assertEqual(shorts(self.r, "minor"), {"bosses/minor-thin.json": [
            "story 149<150", "legend 59<60", "facts 1<2", "aggro yells 1<2", "death yells 0<1", "slay yells 0<1", "lairEntry 0<1",
            "briefing 1<2", "rumour 0<2", "no loremasterTopic", "no chronicle win"]})
        self.assertEqual(shorts(self.r, "epic-location"), {"locations/epic-place.json": ["chapters 11>10", "story 501>500", "book pages 7<8"]})
        self.assertEqual(shorts(self.r, "epic-boss"), {"bosses/epic-thin.json": [
            "chapters 1<4", "chapter words 999<1000", "story 299<300", "bookPage pages 1<2"]})

    def test_filters(self):
        r = d.report(FIX, realm="hibernia", kinds=["major"])
        self.assertEqual(r["summary"][0], "bosses: story 250+ words: 1 of 1 below")
        self.assertEqual([g["kind"] for g in r["groups"]], ["major"])
        r = d.report(FIX, kinds=["location"], exclude_epic=True)
        self.assertEqual(r["summary"][0], "locations: story 500+ words: 1 of 3 below")

    def test_cli_files_and_json(self):
        out = io.StringIO()
        with redirect_stdout(out):
            d.main(["--lore", FIX, "--files", "--kind", "major", "--by-realm"])
        self.assertIn("bosses/thin-major.json [major, Hibernia]: story 249<250; legend 99<100;", out.getvalue())
        self.assertIn("-- Hibernia: 1 below a floor", out.getvalue())
        out = io.StringIO()
        with redirect_stdout(out):
            d.main(["--lore", FIX, "--json"])
        self.assertEqual(json.loads(out.getvalue())["bosses"], 7)


class CSharpFixtureTests(unittest.TestCase):
    def test_the_csharp_tests_depth_lines(self):
        # UT_LoreBible.TheDepthReportCountsEntriesBelowTheFloor, on the same LoreFixtures folder.
        if not os.path.isdir(CS_FIX):
            self.skipTest("no LoreFixtures")
        s = d.report(CS_FIX)["summary"]
        for line in ["locations: story 500+ words: 1 of 1 below", "locations: an era and a faction: 0 of 1 below",
                     "history: Albion: 2 eras, 1 factions", "history: Midgard: no file yet"]:
            self.assertIn(line, s)

    def test_an_epic_krondon_skips_the_story_floor(self):
        # UT_LoreBible.ChaptersAndTheEpicTierParseAndAreChecked: krondon made epic (183-word story) is not below 500+.
        if not os.path.isdir(CS_FIX):
            self.skipTest("no LoreFixtures")
        import shutil, tempfile
        with tempfile.TemporaryDirectory() as tmp:
            dst = os.path.join(tmp, "lore")
            shutil.copytree(CS_FIX, dst)
            p = os.path.join(dst, "locations", "krondon.json")
            with open(p, encoding="utf-8") as f:
                j = json.load(f)
            j["tier"] = "epic"
            with open(p, "w", encoding="utf-8") as f:
                json.dump(j, f)
            self.assertIn("locations: story 500+ words: 0 of 1 below", d.report(dst)["summary"])


if __name__ == "__main__":
    unittest.main()
