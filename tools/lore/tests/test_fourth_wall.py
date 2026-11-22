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

    def test_narrative_game_terms_fail_and_the_raiders_fields_get_them_as_game_terms(self):
        # Told text has failed on these since decision 8. Since the owner's "Rewrite that" (2026-11-21) the legend and
        # the bot lines are checked for them too, through GAME_TERMS (warnings until GAME_TERMS_FAIL is True).
        for text in ["every level 50 knows him", "a plain mob", "an 80-second respawn", "the best xp", "he cons purple",
                     "the oldest thing in the zone", "Two spawns of Marquis Chaosmar", "Duke Harboris spawns with him"]:
            self.assertTrue(self.words(text, "narrative"), text)
            self.assertEqual(self.words(text, "advice"), [], text)
            self.assertEqual(self.words(text, "chat"), [], text)
            for kind in ("advice", "chat"):
                self.assertTrue(fourth_wall.game_term_hits(text, kind), (kind, text))

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


class GameTermTests(unittest.TestCase):
    """Owner 2026-11-21, "Rewrite that": no game terms in any player-visible field, fight advice included. The word lists
    are the Albion, Midgard and Hibernia writers', merged in fourth_wall.GAME_TERMS."""
    def words(self, text, kind, ban_pending=False):
        return [w for _, w in fourth_wall.game_term_hits(text, kind, ban_pending)]

    def test_game_terms_are_caught_in_every_field(self):
        for text in ["he is a level 54 dwarf", "levels 30-70", "a 45 named", "the roots are 50ish", "come at 50+",
                     "kill the wisps (45) first", "rabid bugbears (40-54)", "the mid 40s crowd", "she runs in the high thirties",
                     "lvl 20", "good xp", "the realm points are poor", "a 3-minute respawn", "he repops", "the mobs here",
                     "lots of nameds", "the epics end here", "the viking epic ends in these hills", "every named in the lair.",
                     "kill the adds first", "an add fight", "RvR", "PvE folk", "the loot table", "a zerg", "the starter zone",
                     "he cons red", "red to you", "logged off", "she's offline", "release to your own portal keep",
                     "the bind point", "he aggros on sight", "deaggro", "lost threat", "hate list", "kiting him",
                     "the main tank", "MT dies", "off-tank him", "hit points", "under 25% she heals", "half his hp",
                     "it one-shots", "a DoT", "the AoE", "PBAE him", "nukes hard", "mezzes the healer", "lifetap", "his procs",
                     "debuffs you", "rebuff", "crowd-control immune", "cc him", "the dps", "damage over time", "oom",
                     "rez the healer", "crits for a lot", "heat resists", "cold resist", "resist pots",
                     "a cooldown", "a timer", "untargetable at night",
                     "a stun break", "the radius", "his coords", "LoS him", "a solo 35", "too many evades for a duo",
                     "grp up", "an FG", "dmg", "his stats", "the teleporter", "ports you round the isles", "gg",
                     "noobs", "a BAF pack", "bring-a-friend", "somebody screenshot it", "the campers",
                     "7 grymvarg with him", "dorbal is a 34 touched", "2,600 tether", "within 350", "a 1500 radius"]:
            for kind in ("narrative", "advice", "chat"):
                # In told text a word NARRATIVE already fails ("xp", "zone") is not reported a second time.
                self.assertTrue(self.words(text, kind) or fourth_wall.hits(text, kind), (kind, text))

    def test_raiders_words_are_checked_in_the_legend_and_bot_lines_only(self):
        # Plain English in a story, the game's own word in a raider's mouth.
        for story, advice in [("Hursk lives on the lowest level of the caves.", "it matches your level"),
                              ("Hunters' snares part on his scales.", "he snares the healers"),
                              ("I will grind your bones to dust!", "grind him down slowly"),
                              ("held her band back at range", "stay at range"),
                              ("its whole faction comes", "it costs faction"),
                              ("as if a hand had let go of a leash", "he leashes back"),
                              ("red kites over the hill", "he kites well"),
                              ("the town's old resistances", "bring resistances"),
                              ("immune to grief", "immune off his post"),
                              ("susceptible to flattery", "susceptible to cold"),
                              ("Not every priest of the dead resisted.", "his blast is resisted"),
                              ("the Danaoin still farming", "farming him for coin"),
                              ("the moon's phases", "the second phase"),
                              ("drink to the king's health", "low health"),
                              ("in my experience", "great experience"),
                              ("the cork popped", "he pops at dusk"),
                              ("a log in the river", "log out there"),
                              ("the marsh and its spawn", "a night spawn"),
                              ("He pulled the girl from the water.", "pull him to the corner")]:
            self.assertEqual(self.words(story, "narrative"), [], story)
            self.assertTrue(self.words(advice, "advice"), advice)
            self.assertTrue(self.words(advice, "chat"), advice)

    def test_in_world_words_and_measures_pass(self):
        for text in ["keep 8 paces apart", "about 20-odd paces", "1 in 3 raiders", "2 to 4 of them", "every 10-15 sec",
                     "the killing of 12", "30 seconds", "every 5 minutes", "3 times", "2 of them", "looters on the road",
                     "the scriptorium", "madder roots", "she went blue to the lips", "the kite's down", "every drop counts",
                     "Banish Immunities", "they pulled the spear out", "a silk farm", "they add up", "ever named",
                     "there is no threat", "rooted to the spot", "the Root", "Ancient Root", "wipe your boots",
                     "Rooted. All of us, rooted.", "he levels his spear", "pulls at your legs", "pull free",
                     "the epic of Gilgamesh", "an epic poem", "his target", "heal his target hard", "Aithne Con keeps the ring",
                     "[aithne con]", "the pit bosses", "the level heath", "a city on three levels", "Mark (5:9)",
                     "a long bowshot of the trees", "Captain Rhodri", "the abbey bell rings; no one pulls it"]:
            for kind in ("narrative", "advice", "chat"):
                if kind == "narrative" or "bowshot" not in text and "pulls it" not in text:
                    self.assertEqual(self.words(text, kind), [], (kind, text))

    def test_pending_words_pass_until_the_owner_bans_them(self):
        for text in ["Krogen (about 343k/658k)", "near 21k, 40k", "(62k, 48k)", "melee only", "the casters", "a full group",
                     "the orc camp", "the quest", "keep mana", "lol", "grats", "gz", "the dagger dropped", "poor loot",
                     "leave the pets", "taunt it", "he stuns", "a stunned man", "line of sight"]:
            for kind in ("narrative", "advice", "chat"):
                self.assertEqual(self.words(text, kind), [], (kind, text))
                self.assertTrue(self.words(text, kind, ban_pending=True), (kind, text))
        # The writers' exceptions hold once they are banned too.
        for text in ["a stunning blow", "stunning breath", "every drop", "looters"]:
            self.assertEqual(self.words(text, "advice", ban_pending=True), [], text)
        self.assertEqual(fourth_wall.BAN_PENDING, False)

    def test_a_game_term_already_failing_is_not_warned_twice(self):
        self.assertTrue(fourth_wall.hits("a level 50 mob", "narrative"))
        self.assertEqual(self.words("a level 50 mob", "narrative"), [])
        self.assertEqual(self.words("a level 50 mob", "advice"), ["level 5", "mob"])

    def test_the_writers_new_software_and_site_words_fail_everywhere(self):
        for text in ["on Freddyshouse they said", "a GameEpicNPC", "GameKeepGuard", "the GuardBaf pack", "use target-nearest",
                     "he clicks on the door"]:
            for kind in ("narrative", "advice", "chat", "chatfile"):
                self.assertTrue([w for _, w in fourth_wall.hits(text, kind)], (kind, text))

    def test_game_terms_warn_until_the_switch_is_thrown(self):
        import shutil, tempfile
        tmp = tempfile.mkdtemp()
        try:
            os.makedirs(os.path.join(tmp, "bosses"))
            boss = {"id": "x", "name": "X", "legend": "He is level 50 and his adds respawn.", "botLines": {"briefing": ["pull him"]}}
            with open(os.path.join(tmp, "bosses", "x.json"), "w", encoding="utf-8") as fh: json.dump(boss, fh)
            empty_chat = os.path.join(tmp, "chat")
            self.assertFalse(fourth_wall.GAME_TERMS_FAIL)
            self.assertEqual(fourth_wall.problems(tmp, empty_chat), [])
            warned = fourth_wall.warnings(tmp)
            self.assertTrue(any("legend" in w and "'level 5" in w for w in warned), warned)
            self.assertTrue(any("botLines.briefing[]" in w and "'pull'" in w for w in warned), warned)
            fourth_wall.GAME_TERMS_FAIL = True
            try:
                self.assertEqual(sorted(fourth_wall.problems(tmp, empty_chat)), sorted(warned))
                self.assertEqual(fourth_wall.warnings(tmp), [])
            finally:
                fourth_wall.GAME_TERMS_FAIL = False
        finally:
            shutil.rmtree(tmp)


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
