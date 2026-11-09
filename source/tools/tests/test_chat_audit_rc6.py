"""Tests for the RC6 checks of source/tools/chat_audit.py (lines against the log's facts and the clock): death stories
against AUTONOMOUS_BOT_DEATH, the time of day, internal words, bare-reaction answers, run-ons after a nothing answer, the
/group channel, "back" from nowhere and duo offers declined for a duty. Small synthetic logs, made-up names.

    python3 -m unittest discover -s source/tools/tests -v
"""
import os
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import chat_audit  # noqa: E402


def stamp(second, hour):
    second += hour * 3600
    return f"11/08 {second // 3600 % 24:02d}:{second // 60 % 60:02d}:{second % 60:02d}"


def chat(second, speaker, text, extra="", ch="faction", who="bot"):
    return (second, f'CHAT_LINE who={who} ch={ch} realm=Albion from="{speaker}" {extra} text="{text}"')


def death(second, bot, killer, kind, attackers):
    return (second, f'AUTONOMOUS_BOT_DEATH bot="{bot}" id=1 level=40 class="Reaver" realm=Albion group=1 killer="{killer}" '
                    f'killerKind={kind} killerLevel=40 con=YELLOW attackers={attackers} armorPieces=6 region=1 zone="Testmoor"')


def invite(second, bot, result, duty=""):
    return (second, f'BOT_INVITE_RESULT bot="{bot}" player="Questor" from=player result={result}' + (f" duty={duty}" if duty else ""))


def log(rows, hour=20):
    """rows: (second, body) -> a temporary log file path; the clock starts at hour:00."""
    handle = tempfile.NamedTemporaryFile("w", suffix=".chat.log", delete=False, encoding="utf-8")
    for second, body in sorted(rows, key=lambda r: r[0]):
        handle.write(f"{stamp(second, hour)} | INF | DOL.GS.Test | {body}\n")
    handle.close()
    return handle.name


def story(ex):
    return f"via=AutonomousBotChatCoordinator.PlayStory~ src=story cap=8.0 ex={ex}"


class Rc6CheckTests(unittest.TestCase):
    def summary(self, rows, hour=20):
        path = log(rows, hour)
        try:
            s = chat_audit.summarize(chat_audit.audit(path))
            s["verdict_ok"], s["verdict_reasons"] = chat_audit.verdict(s)
            return s
        finally:
            os.unlink(path)

    def test_a_monster_death_told_as_a_gank_fails(self):
        s = self.summary([
            death(0, "Aldric", "shard golem", "monster", 1),
            chat(5, "Aldric", "got caught by the bridge", story(1) + " i=0 role=teller"),
            chat(8, "Brenna", "rip", story(1) + " i=1 role=riff"),
            chat(10, "Aldric", "lost a bubble too", story(1) + " i=2 role=teller"),
        ])
        self.assertEqual(s["counts"]["death_stories"], 1)
        self.assertEqual(s["counts"]["death_story_rvr_for_monster"], 1, s["examples"])
        self.assertFalse(s["verdict_ok"])
        self.assertIn("death story contradicts the death 1", s["verdict_reasons"])

    def test_a_player_kill_told_as_guards_or_the_log_s_monsters_fails(self):
        s = self.summary([
            death(-100, "Corwin", "marsh crawler", "monster", 3),  # another bot's monster death: "marsh crawler" is a monster here
            death(0, "Dagny", "Enemyone", "bot", 2),
            chat(4, "Dagny", "the guards got me on the bridge", story(2) + " i=0 role=teller"),
            death(100, "Elspeth", "Enemytwo", "player", 1),
            chat(110, "Elspeth", "died to marsh crawlers ugh", "via=AutonomousBotChatCoordinator.TryField src=field ex=3 i=0 role=asker"),
            death(200, "Fenn", "Enemythree", "bot", 1),
            chat(205, "Fenn", "got ganked at the milegate lol", story(4) + " i=0 role=teller"),
        ])
        self.assertEqual(s["counts"]["death_story_monster_for_rvr"], 2, s["examples"])
        self.assertEqual(s["counts"]["death_story_rvr_for_monster"], 0, "Fenn really was ganked")

    def test_more_attackers_than_there_were_fails(self):
        s = self.summary([
            death(0, "Garr", "Enemyone", "bot", 1),
            chat(3, "Garr", "got farmed on the bridge heh", story(5) + " i=0 role=teller"),
            chat(10, "Garr", "had 2 on me before i could run", story(5) + " i=1 role=teller"),
            death(100, "Hild", "Enemytwo", "bot", 2),
            chat(103, "Hild", "got ganked lol", story(6) + " i=0 role=teller"),
            chat(106, "Ivo", "by who?", story(6) + " i=1 role=riff"),
            chat(110, "Hild", "a zerg, 20+", story(6) + " i=2 role=teller"),
            death(200, "Jory", "elder beech", "monster", 2),
            chat(204, "Jory", "elder beeches x3, no chance", story(7) + " i=0 role=teller"),
            death(300, "Kell", "Enemythree", "bot", 5),
            chat(304, "Kell", "a zerg got me", story(8) + " i=0 role=teller"),
        ])
        self.assertEqual(s["counts"]["death_story_attackers"], 3, s["examples"])
        self.assertEqual(chat_audit.claimed_attackers("4 v 1 at the mg"), 4)
        self.assertEqual(chat_audit.claimed_attackers("1 v 5 lol"), 5)
        self.assertEqual(chat_audit.claimed_attackers("a grp of 6 jumped me"), 6)
        self.assertEqual(chat_audit.claimed_attackers("like 8 of them, 3 or 4 casters"), 8)
        self.assertEqual(chat_audit.claimed_attackers("full grp"), 8)
        self.assertIsNone(chat_audit.claimed_attackers("killed by elder beeches at 26"))

    def test_a_wipe_counts_the_whole_group_s_fight(self):
        # RC10 on the owner's PC: a group of five wiped in Darkness Falls to three different monsters; the teller had one on it.
        def grouped(second, bot, killer, attackers, zone="Darkness Falls"):
            return (second, f'AUTONOMOUS_BOT_DEATH bot="{bot}" id=1 level=50 class="Reaver" realm=Albion group=5 killer="{killer}" '
                            f'killerKind=monster killerLevel=60 con=PURPLE attackers={attackers} armorPieces=6 region=249 zone="{zone}"')
        s = self.summary([
            grouped(0, "Neil", "nightmare", 1),
            grouped(1, "Leeo", "mahr", 1),
            grouped(9, "Toxic", "chthonian crawler", 1),
            chat(40, "Toxic", "wiped lol", story(9) + " i=0 role=teller"),
            chat(48, "Toxic", "pulled 3 at once", story(9) + " i=2 role=teller"),
            grouped(300, "Ulf", "nightmare", 1),
            grouped(305, "Vala", "nightmare", 2),
            grouped(310, "Wenn", "mahr", 1, zone="Otherplace"),
            chat(330, "Ulf", "wiped lol", story(10) + " i=0 role=teller"),
            chat(338, "Ulf", "pulled 3 at once", story(10) + " i=2 role=teller"),
        ])
        self.assertEqual(s["counts"]["death_story_attackers"], 1, s["examples"])

    def test_a_story_long_after_the_death_is_informational(self):
        s = self.summary([
            death(0, "Aldric", "shard golem", "monster", 1),
            chat(900, "Aldric", "got ganked lol", story(1) + " i=0 role=teller"),
        ])
        self.assertEqual(s["counts"]["info_death_story_no_death"], 1)
        self.assertEqual(s["counts"]["death_story_rvr_for_monster"], 0)
        self.assertTrue(s["verdict_ok"], s["verdict_reasons"])

    def test_tonight_in_the_morning_and_this_morning_at_night(self):
        rows = [chat(0, "Aldric", "busy tonight", "src=field ex=1 i=0 role=asker cap=8.0"),
                chat(30, "Brenna", "quiet this morning", "src=field ex=2 i=0 role=asker cap=8.0")]
        self.assertEqual(self.summary(rows, hour=10)["counts"]["time_of_day_wrong"], 1, "tonight at 10:00")
        self.assertEqual(self.summary(rows, hour=20)["counts"]["time_of_day_wrong"], 1, "this morning at 20:00")
        self.assertEqual(self.summary(rows, hour=11)["counts"]["time_of_day_wrong"], 1)
        self.assertEqual(self.summary([chat(0, "Aldric", "busy tonight", "src=field ex=1 i=0 role=asker cap=8.0")], hour=18)["counts"]["time_of_day_wrong"], 0)

    def test_internal_words(self):
        s = self.summary([
            chat(0, "Aldric", "which stable 4 group rendezvous?", "src=field ex=1 i=0 role=asker cap=8.0"),
            chat(10, "Brenna", "died to frontier patrol ugh", "src=field ex=2 i=0 role=asker cap=8.0"),
            chat(20, "Cadoc", "staging at the mg, objective is the keep", "src=other", ch="group"),
            chat(30, "Dagny", "the stable master has routes", "src=field ex=3 i=0 role=asker cap=8.0"),
            # RC10 on the owner's PC, in /say: a goal name as a monster.
            chat(40, "Cedric", "careful group rendezvouses link", "src=field ex=4 i=0 role=asker cap=5.6", ch="say"),
        ])
        self.assertEqual(s["counts"]["internal_vocabulary"], 4, s["examples"])
        self.assertFalse(s["verdict_ok"])

    def test_a_question_answered_by_a_bare_reaction(self):
        s = self.summary([
            chat(0, "Aldric", "rez in testmoor?", "src=field ex=1 i=0 role=asker cap=8.0"),
            chat(3, "Brenna", "oof", "src=thread ex=1 i=1 role=answerer cap=8.0"),
            chat(10, "Cadoc", "got me again", story(2) + " i=0 role=teller"),
            chat(12, "Dagny", "where?", story(2) + " i=1 role=riff"),
            chat(14, "Cadoc", "by the camp", story(2) + " i=2 role=teller"),
            chat(16, "Edda", "lol rip", story(2) + " i=3 role=riff"),
            chat(20, "Fenn", "only 2 keeps?", "src=field ex=3 i=0 role=asker cap=8.0"),
            chat(23, "Garr", "lol", "src=thread ex=3 i=1 role=answerer cap=8.0"),
            chat(30, "Hild", "port up?", "src=field ex=4 i=0 role=asker cap=8.0"),
            chat(33, "Ivo", "ya, porter in town", "src=thread ex=4 i=1 role=answerer cap=8.0"),
        ])
        self.assertEqual(s["counts"]["question_bare_reaction"], 1, s["examples"])

    def test_a_run_on_after_a_nothing_answer(self):
        s = self.summary([
            chat(0, "Aldric", "any grps out?", "src=field ex=1 i=0 role=asker cap=8.0"),
            chat(4, "Brenna", "quiet atm", "src=thread ex=1 i=1 role=answerer cap=8.0"),
            chat(6, "Aldric", "how many?", "src=thread ex=1 i=2 role=asker cap=8.0"),
            chat(9, "Brenna", "small grp", "src=thread ex=1 i=3 role=answerer cap=8.0"),
            chat(12, "Cadoc", "count me in", "src=thread ex=1 i=4 role=third cap=8.0"),
            chat(20, "Aldric", "omw", "src=thread ex=1 i=5 role=asker cap=8.0"),
            chat(60, "Dagny", "where's the fletcher?", "src=field ex=2 i=0 role=asker cap=8.0"),
            chat(64, "Edda", "dunno", "src=thread ex=2 i=1 role=answerer cap=8.0"),
            chat(66, "Dagny", "anyone else know?", "src=thread ex=2 i=2 role=asker cap=8.0"),
            chat(100, "Fenn", "any grps out?", "src=field ex=3 i=0 role=asker cap=8.0"),
            chat(104, "Garr", "few grps north side", "src=thread ex=3 i=1 role=answerer cap=8.0"),
            chat(106, "Fenn", "k omw", "src=thread ex=3 i=2 role=asker cap=8.0"),
        ])
        self.assertEqual(s["counts"]["run_on_after_nothing"], 1, s["examples"])

    def test_the_group_channel(self):
        rows = [chat(0, "Aldric", "long run back", "via=AutonomousLivingServer.OnBotReleasing src=other", ch="group"),
                chat(0, "Aldric", "released, running back", "via=GameBot.TellOwnGroup src=other", ch="group"),
                chat(10, "Aldric", "lol", "via=AutonomousBotGroupChatter.TryGroupLine src=other", ch="group"),
                chat(14, "Brenna", "inc", "via=AutonomousBotCombatCalls.CallForGroup src=other", ch="group")]
        hunt = ["good camp", "clean pull", "xp is flying", "oom sec", "need a med", "buffs dropping", "loot is yours"]
        rows += [chat(60 + 40 * k, "Aldric", text, "via=AutonomousBotGroupChatter.TryGroupLine src=other", ch="group") for k, text in enumerate(hunt)]
        s = self.summary(rows)
        self.assertEqual(s["counts"]["group_same_bot_3s"], 1)
        self.assertEqual(s["counts"]["info_group_hunt_talk"], 7)
        self.assertEqual(s["counts"]["group_hunt_talk_burst"], 1, "seven hunting lines in five minutes")
        s = self.summary(rows[:4] + [chat(60 + 120 * k, "Aldric", text, "via=AutonomousBotGroupChatter.TryGroupLine src=other", ch="group")
                                     for k, text in enumerate(hunt)])
        self.assertEqual(s["counts"]["group_hunt_talk_burst"], 0, "seven lines over twelve minutes")
        self.assertIn("/group hunting banter bursts", " ".join(self.summary(rows)["verdict_reasons"]))

    def test_back_from_nowhere(self):
        s = self.summary([
            death(-50, "Someoneelse", "shard golem", "monster", 1),
            chat(0, "Aldric", "ok back", "src=field ex=1 i=0 role=asker cap=8.0"),
            chat(10, "Brenna", "brb phone", "src=field ex=2 i=0 role=asker cap=8.0"),
            chat(300, "Brenna", "back", "src=field ex=3 i=0 role=asker cap=8.0"),
            death(400, "Cadoc", "shard golem", "monster", 1),
            chat(500, "Cadoc", "re", "src=field ex=4 i=0 role=asker cap=8.0"),
            chat(600, "Dagny", "back in testmoor", "src=field ex=5 i=0 role=asker cap=8.0"),
        ])
        self.assertEqual(s["counts"]["back_from_nowhere"], 1, s["examples"])
        s = self.summary([chat(0, "Aldric", "ok back", "src=field ex=1 i=0 role=asker cap=8.0")])
        self.assertEqual(s["counts"]["back_from_nowhere"], 0, "no death rows in the log: can't tell")

    def test_a_duo_offer_then_declined_is_informational(self):
        s = self.summary([
            chat(0, "Aldric", "oh hey Questor, im solo atm if u want company", 'to="Questor" src=player ex=1 i=0 role=login', ch="tell"),
            invite(300, "Aldric", "declined", "StableRoute"),
            chat(400, "Brenna", "50 wizard here, want to team up for a bit?", 'to="Questor" src=other', ch="tell"),
            invite(450, "Brenna", "grouped"),
            chat(500, "Cadoc", "wanna duo?", 'to="Questor" src=other', ch="tell"),
            invite(1200, "Cadoc", "declined", "StableRoute"),
        ])
        self.assertEqual(s["counts"]["info_offer_then_declined"], 1, s["examples"])
        self.assertTrue(s["verdict_ok"], s["verdict_reasons"])

    def test_the_new_rows_are_in_the_json_counts(self):
        s = self.summary([chat(0, "Aldric", "hi all", "src=field ex=1 i=0 role=asker cap=8.0")])
        for key, _ in chat_audit.STORY_FACT_KINDS:
            self.assertIn(key, s["counts"])
            self.assertIn(key, s["per_hour"])


if __name__ == "__main__":
    unittest.main()
