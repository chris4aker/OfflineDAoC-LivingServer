"""Owner decision 8 (2026-11-21): "Nothing in the lore should reference real companies or break the 4th wall so to speak."
"""
import json, os, sys, unittest
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import fourth_wall
from lore_text import LORE_DIR


class FourthWallRuleTests(unittest.TestCase):
    def words(self, text, kind="narrative"):
        return [w for _, w in fourth_wall.hits(text, kind)]

    def test_the_lore_and_its_chat_files_are_clean(self):
        self.assertEqual(fourth_wall.problems(LORE_DIR), [])

    def test_companies_sites_and_products_are_caught_everywhere(self):
        for kind in ("narrative", "advice", "chat", "chatfile"):
            for text in ["Mythic's quest names him Seleucus.", "The old Allakhazam notes put three deserters with him.",
                         "Prima's guide says so.", "the Camelot Herald posted it", "GameBanshee lists it", "on Uthgard he drops",
                         "Darkness Falls opened in patch 1.49.", "Abermenai came with 1.62.", "The Shrouded Isles expansion added it.",
                         "Trials of Atlantis brought it.", "since Catacombs he hits harder", "the devs promised relics",
                         "in Living Server they no longer run", "Offline DAoC spawns no Lilith", "a Dec 2002 comment warned"]:
                self.assertTrue(self.words(text, kind), (kind, text))

    def test_the_software_under_the_world_is_caught_everywhere(self):
        for kind in ("narrative", "advice", "chat"):
            for text in ["bots get thrown too", "a level 54 Dellingstad NPC", "in this server's code", "no script, 3-minute respawn",
                         "his template puts him in Morvalt Samhalle", "the world data names her", "only GamePlayer damage counts",
                         "every hit from a real player", "the old guides said", "reading the quest log", "in game he wears a model",
                         "you cannot click him", "a gm held a funeral", "game hours 10 to 13",
                         # independent review (2026-11-21): what the first pass missed
                         "No spells or styles in the data", "not in this world's spawn data", "lots of data quests",
                         "she is flagged aggressive", "an isle of the region, with its own file", "(file to come)",
                         "the region overview", "the raid AI", "the in-game map"]:
                self.assertTrue(self.words(text, kind), (kind, text))

    def test_game_terms_are_caught_in_narrative_only(self):
        # Raiders' fight advice (legend) and chat voice keep levels, mobs and timers (owner rule 2026-11-06).
        for text in ["every level 50 knows him", "a plain mob", "an 80-second respawn", "the best xp", "he cons purple",
                     "the oldest thing in the zone", "Two spawns of Marquis Chaosmar", "Duke Harboris spawns with him"]:
            self.assertTrue(self.words(text, "narrative"), text)
            self.assertEqual(self.words(text, "advice"), [], text)
            self.assertEqual(self.words(text, "chat"), [], text)

    def test_in_world_words_that_only_look_alike_pass(self):
        for text in ["Like the rest of Clifton she is Cryptos Mythicos.", "a patch of ground where nothing grows",
                     "Cronwort's patch is nearer the road", "The Catacombs of Cardova are the proof that Rome reached west.",
                     "the catacombs beneath the city", "Lurfos the Herald cries the road.", "A herald of Camelot waits.",
                     "the Herald records each first kill", "a harp player of Tara", "The gales come in a mob.",
                     "the scriptorium rises", "labelled in the old script", "drawn in the court's script",
                     "a children's game of it on the Cornish road", "the vault below the hall", "she resets past 2000",
                     "a man who serves at the king's table", "the quest for the Grail", "the protector of the foundations",
                     "the file lies down", "a file-leader calls his file", "a strip of flagged floor", "Svarm and its brood of spawn",
                     "under a truce flag", "the Academy's files"]:
            self.assertEqual(self.words(text), [], text)
        # Bot chat keeps fire patches and distances; only the lore's chat files hold no patch of ground.
        self.assertEqual(self.words("we stood in patches", "chat"), [])
        self.assertTrue(self.words("those came with a patch", "chatfile"))
        self.assertEqual(self.words("a patch of heather", "chatfile"), [])


class FieldMapTests(unittest.TestCase):
    """What a player sees: traced in docs/1.2.1/HEROES.md (decision 8) and fourth_wall.py."""
    def test_sources_provenance_and_own_facts_are_never_read_as_visible(self):
        d = {"id": "x", "name": "X", "summary": "s", "aliases": ["the bot"], "npcNames": ["Server"],
             "facts": [{"text": "Mythic says", "provenance": "mythic", "source": "https://camelot.allakhazam.com/x"}],
             "legend": "l", "botLines": {"tell": ["t"]}, "atmosphere": {"areas": [{"name": "NPC corner", "lines": ["a"]}]},
             "loremaster": {"intro": "i", "topics": {"kw": "v"}}}
        seen = {(p, t, k) for p, t, k in fourth_wall.visible(d, "locations")}
        texts = {t for _, t, _ in seen}
        self.assertNotIn("Mythic says", texts)
        self.assertFalse(any("allakhazam" in t for t in texts))
        self.assertNotIn("the bot", texts)
        self.assertNotIn("Server", texts)
        self.assertNotIn("NPC corner", texts)
        self.assertIn(("locations.legend", "l", "advice"), seen)
        self.assertIn(("locations.botLines.tell[]", "t", "chat"), seen)
        self.assertIn(("locations.loremaster.topics.<keyword>", "kw", "narrative"), seen)

    def test_a_history_fact_text_is_visible_and_its_source_is_not(self):
        h = {"realm": "Albion", "eras": [{"id": "e", "name": "E", "summary": "s",
             "events": [{"text": "told", "provenance": "legend", "source": "patch 1.49"}]}]}
        texts = [t for _, t, _ in fourth_wall.visible(h, "history")]
        self.assertIn("told", texts)
        self.assertNotIn("patch 1.49", texts)

    def test_the_catacombs_era_herald_facts_carry_their_date(self):
        # Review X3: the four facts from Mythic's Catacombs lore pages (December 2004) keep their source, dated.
        for rel in ["locations/avalon-city.json", "bosses/morgana.json", "locations/caer-sidi.json", "locations/jordheim.json"]:
            d = json.load(open(os.path.join(LORE_DIR, rel), encoding="utf-8"))
            dated = [x for x in d["facts"] if "Catacombs lore" in x["text"]]
            self.assertTrue(dated, rel)
            for x in dated:
                self.assertTrue(x["text"].startswith("Camelot Herald (2004)"), rel)
                self.assertIn("camelotherald.com", x["source"], rel)
                self.assertEqual(x["provenance"], "mythic", rel)


if __name__ == "__main__":
    unittest.main()
