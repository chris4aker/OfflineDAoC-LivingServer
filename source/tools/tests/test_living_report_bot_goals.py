"""Tests of the bot goals report section (#669, #670) on planted synthetic rows (no real log or name).

    python3 -m unittest source/tools/tests/test_living_report_bot_goals.py
"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from living_report_sections import bot_goals  # noqa: E402

PREFIX = "11/26 10:00:00 | INF | DOL.GS.LivingRows | "


def log(*rows):
    return [PREFIX + row for row in rows]


def goal_pass(realm="Albion", band=20, pool=2, eligible=2, open_="0/40/12/48", planned="0/1/0/1"):
    return (f"GOAL_PASS t=1 realm={realm} band={band} members=196 pool={pool} bgEligible={eligible} "
            f"target=59/59/29/49 held=59/19/17/1 open={open_} planned={planned} personalities=on")


class BotGoalsSectionTest(unittest.TestCase):
    def test_contract(self):
        self.assertEqual(bot_goals.ROWS, ["BOT_GOALS", "GOAL_PASS"])
        self.assertTrue(callable(bot_goals.summarize))

    def test_no_rows(self):
        text = bot_goals.summarize([])
        self.assertIn("No BOT_GOALS row", text)
        self.assertIn("No GOAL_PASS rows.", text)

    def test_startup_row_shows_the_source(self):
        text = bot_goals.summarize(log("BOT_GOALS source=defaults reason=start l1_19=55/35/0/10 l20_49=30/30/15/25 l50=20/40/40/0"))
        self.assertIn("| defaults | start | 55/35/0/10 | 30/30/15/25 | 20/40/40/0 |", text)

    def test_pass_rows_sum_open_and_planned_per_realm_and_band(self):
        text = bot_goals.summarize(log(goal_pass(), goal_pass(planned="0/0/1/1"), goal_pass(realm="Midgard", band=0,
                                                                                          open_="5/5/0/0", planned="1/0/0/0")))
        self.assertIn("| Albion | 20-49 | 2 | 2.0 | 2.0 | 0/80/24/96 | 0/1/1/2 | 50% / 48% |", text)
        self.assertIn("| Midgard | 1-19 | 1 |", text)
        self.assertIn("0% / 0%", text)


if __name__ == "__main__":
    unittest.main()
