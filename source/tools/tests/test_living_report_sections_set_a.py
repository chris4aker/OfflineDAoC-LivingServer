"""Tests of the set A report sections (#625 rows 1-5) on planted synthetic logs (no real log or name from the owner's PC).

    python3 -m unittest source/tools/tests/test_living_report_sections_set_a.py
"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from living_report_sections import bg_picks, bg_population, cc_breaks, interrupts, player_lines  # noqa: E402

PREFIX = "11/25 10:00:00 | INF | DOL.GS.LivingRows | "
SECTIONS = (bg_population, interrupts, cc_breaks, bg_picks, player_lines)


def log(*rows):
    return [PREFIX + row for row in rows]


def population(realm="Albion", bots=10, goal=8, travelling=2, eligible=40, expected=10, player=0, bg="Thidranki"):
    return (f"BG_POPULATION t=1 bg={bg} bracket=20-24 realm={realm} bots={bots} bgGoal={goal} passing={bots - goal} "
            f"travelling={travelling} assigned={goal + travelling} eligible={eligible} expected={expected} companions=1 "
            f"player={player}")


def cast(caster="gamebot", outcome="lost", why="timer", left=1500, spell="DirectDamage"):
    return (f"CAST_INTERRUPT seq=10 every=10 t=1 caster={caster} pet=0 realm=Albion spell={spell} castMs=3000 "
            f"leftMs={left} outcome={outcome} why={why} attacker=player attackerPet=0 region=252 bg=Thidranki")


def ccbreak(side="ally", ms=800, breaker="gamebot", cc="mez", bg="Thidranki"):
    return (f"CC_BREAK t=1 cc={cc} target=gamebot breaker={breaker} breakerPet=0 caster=gamebot side={side} ms={ms} "
            f"attack=spell region=252 bg={bg}")


def pick(kind="person", skipped=0, engines=0, rally=0, src="scan"):
    return (f"BG_TARGET_PICK seq=10 every=10 t=1 bg=Thidranki realm=Albion src={src} pick={kind} target=gamebot "
            f"skippedControlled={skipped} skippedEngines={engines} rally={rally} list=noticed candidates=3")


def rally(ms, nth):
    return f"BG_RALLY_RESPONSE t=1 bg=Thidranki realm=Albion contact=player pet=0 ms={ms} nth={nth}"


def line(ch="say", answered=1, latency=2000, kind="answer", bots=1, intent="question"):
    return (f"PLAYER_LINE_OUTCOME seq=5 t=1 ch={ch} realm=Albion intent={intent} answered={answered} "
            f"latencyMs={latency if answered else -1} kind={kind if answered else 'none'} bots={bots if answered else 0} "
            f"replies={bots if answered else 0} window=30000")


class InterfaceTest(unittest.TestCase):
    def test_every_section_has_the_interface(self):
        for section in SECTIONS:
            with self.subTest(section=section.__name__):
                self.assertIsInstance(section.TITLE, str)
                self.assertTrue(section.TITLE)
                self.assertTrue(section.ROWS and all(isinstance(row, str) and row.isupper() for row in section.ROWS))
                self.assertIsInstance(section.summarize([]), str)
                self.assertIn("No ", section.summarize([]))

    def test_sections_ignore_other_rows_and_a_marker_inside_a_word(self):
        noise = log("CHAT_LINE who=bot ch=say text=\"hi\"", "XBG_POPULATION bots=1", "RVR_DECIDE t=1 bot=x")
        for section in SECTIONS:
            with self.subTest(section=section.__name__):
                self.assertIn("No ", section.summarize(noise))


class BgPopulationTest(unittest.TestCase):
    def test_averages_and_flags_a_battleground_below_its_goal_shares(self):
        text = bg_population.summarize(log(
            population(goal=8, travelling=2, expected=10, player=1), population(goal=6, travelling=2, expected=10),
            population(realm="Midgard", goal=2, travelling=0, expected=10)))
        self.assertIn("| Thidranki (20-24) | Albion | 2 | 10.0 | 7.0 |", text)
        self.assertIn("| 9.0 | 10.0 | 90% |", text)
        self.assertIn("| 1/2 |", text, "the player was there in one of two samples")
        self.assertIn("Below 75% of the goal shares' headcount: Thidranki Midgard (20%)", text)
        self.assertNotIn("Thidranki Albion (", text)


class InterruptsTest(unittest.TestCase):
    def test_lost_share_by_caster_kind(self):
        text = interrupts.summarize(log(
            cast(), cast(left=500), cast(outcome="kept", why="expired", left=-200), cast(outcome="kept", why="immune", left=900),
            cast(caster="player", outcome="kept", why="expired", left=-1)))
        self.assertIn("| gamebot | 4 | 2 (50%) | 1 | 1 | 1000 |", text)
        self.assertIn("| player | 1 | 0 (0%) | 1 | 0 | - |", text)
        self.assertLess(text.index("| player"), text.index("| gamebot"), "player first, then companion, gamebot, npc")
        self.assertIn("DirectDamage 2/5 lost", text)
        self.assertIn("every 10th", text)


class CcBreaksTest(unittest.TestCase):
    def test_counts_ally_breaks_soon_after_landing(self):
        text = cc_breaks.summarize(log(
            ccbreak(), ccbreak(ms=1200), ccbreak(side="enemy", ms=9000, breaker="player"), ccbreak(ms=7000, bg="-"),
            ccbreak(cc="root", breaker="npc", side="enemy", ms=3000)))
        self.assertIn("| battleground | mez | gamebot | ally | 2 | 1000 |", text)
        self.assertIn("| elsewhere | mez | gamebot | ally | 1 | 7000 |", text)
        self.assertIn("Ally breaks: 3 of 5; within 5 s of landing: 2 (2 by a gamebot or companion).", text)


class BgPicksTest(unittest.TestCase):
    def test_picks_and_rally_response_times(self):
        text = bg_picks.summarize(log(
            pick(skipped=2), pick(skipped=0, rally=1), pick(kind="engine", engines=0), pick(kind="door", src="keep"),
            rally(800, 1), rally(1200, 2), rally(3000, 3), rally(600, 1)))
        self.assertIn("| scan | person | 2 | 1.00 | 0.00 | 1 |", text)
        self.assertIn("| keep | door | 1 |", text)
        self.assertIn("Engine picks: 1 of 4 sampled picks.", text)
        self.assertIn("Rally responses: 4; median 1000 ms, 90th percentile 3000 ms; first responder median 700 ms", text)
        self.assertIn("most responders to one contact: 3.", text)

    def test_only_rally_rows(self):
        text = bg_picks.summarize(log(rally(500, 1)))
        self.assertIn("No BG_TARGET_PICK rows.", text)
        self.assertIn("Rally responses: 1;", text)


class PlayerLinesTest(unittest.TestCase):
    def test_answer_rate_latency_and_kinds_by_channel(self):
        text = player_lines.summarize(log(
            line(latency=1000), line(latency=3000, kind="knowledge", bots=2), line(answered=0, intent="where"),
            line(ch="tell", latency=500, kind="ack"), line(ch="faction", answered=0, intent="where")))
        self.assertIn("| say | 3 | 2 (67%) | 2000 | 1.5 | 1 | 1 | 0 | 0 | 1 |", text)
        self.assertIn("| tell | 1 | 1 (100%) | 500 | 1.0 | 0 | 0 | 1 | 0 | 0 |", text)
        self.assertIn("| **all** | 5 | 3 (60%) |", text)
        self.assertLess(text.index("| say"), text.index("| tell"))
        self.assertIn("Unanswered by intent: where 2.", text)


if __name__ == "__main__":
    unittest.main()
