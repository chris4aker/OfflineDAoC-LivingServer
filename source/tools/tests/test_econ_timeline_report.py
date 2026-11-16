"""Tests for econ_timeline_report.py (Crafting 2.0 WP10): CRAFT_*/ECON_* lines from server logs and rehearsal ledgers.

    python3 -m unittest discover -s source/tools/tests -v
"""
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import econ_timeline_report as rep  # noqa: E402

REHEARSAL_LOG = """\
CRAFT_ENROLL hour=2026-01-05T00:40:00Z realm=Albion bot="Alb0003" skill=Tailoring source="rehearsal"
CRAFT_SESSION_START hour=2026-01-05T00:40:00Z realm=Albion bot="Alb0003" skill=Tailoring value=1 minutes=30 source="rehearsal"
CRAFT_SESSION_END hour=2026-01-05T00:40:00Z realm=Albion bot="Alb0003" skill=Tailoring value=9 crafts=20 made=19 points=8 spentCopper=1200 soldCopper=300 source="rehearsal"
ECON_SUPPLY hour=2026-01-05T00:00:00Z realm=Albion botCoin=150000 deltaCopper=50000 flowsCopper=50000 residualCopper=0 faucetsCopper=60000 sinksCopper=10000 transfersCopper=0 listedValue=0 bots=10 crafters=1 source="rehearsal" scenario="fresh-7pct"
ECON_FLOWS hour=2026-01-05T00:00:00Z realm=Albion kind=LootCoin inCopper=55000 outCopper=0 count=40 transfer=0 source="rehearsal" scenario="fresh-7pct"
ECON_FLOWS hour=2026-01-05T00:00:00Z realm=Albion kind=VendorSale inCopper=5000 outCopper=0 count=8 transfer=0 source="rehearsal" scenario="fresh-7pct"
ECON_FLOWS hour=2026-01-05T00:00:00Z realm=Albion kind=VendorGear inCopper=0 outCopper=10000 count=1 transfer=0 source="rehearsal" scenario="fresh-7pct"
ECON_FLOWS hour=2026-01-05T00:00:00Z realm=Albion kind=ExchangeSale inCopper=900 outCopper=900 count=2 transfer=1 source="rehearsal" scenario="fresh-7pct"
ECON_SUPPLY hour=2026-01-05T01:00:00Z realm=Albion botCoin=210000 deltaCopper=60000 flowsCopper=60000 residualCopper=0 faucetsCopper=60000 sinksCopper=0 transfersCopper=0 listedValue=0 bots=10 crafters=1 source="rehearsal" scenario="fresh-7pct"
CRAFT_FIRST hour=2026-01-05T01:10:00Z realm=Albion bot="Alb0003" title="Legendary" skill=Tailoring source="rehearsal"
ECON_REHEARSAL scenario="fresh-7pct" gate="no negative purse" status=pass detail="0 negative moves; lowest purse 0 copper" build="1.2-beta abc123"
ECON_REHEARSAL scenario="fresh-7pct" gate="median order wait under the limit" status=notapplicable detail="no order board ran (WP5 not built yet)" build="1.2-beta abc123"
ECON_REHEARSAL scenario="fresh-7pct" summary=1 build="1.2-beta abc123" realms=1 botsPerRealm=10 minutes=120 seed=1 gatesPass=1 lootCoinShareOfFaucets=0.917 ordersCollected=0 ordersPosted=0 supplyEndCopper=210000 supplyStartCopper=100000 wealthCrafterMedian=5000 wealthHunterMedian=20000
"""

SERVER_LOG = """\
2026-11-20 10:00:00.1234|INFO|DOL.GS.CraftingEconomy.EconomyLedger|ECON_SUPPLY hour=2026-11-20T10:00:00Z realm=Midgard botCoin=1000000 deltaCopper=2000 flowsCopper=1500 residualCopper=500 bots=900 crafters=60
2026-11-20 10:00:00.1250|INFO|DOL.GS.CraftingEconomy.EconomyLedger|ECON_FLOWS hour=2026-11-20T10:00:00Z realm=Midgard kind=LootCoin inCopper=1500 outCopper=0 count=3
2026-11-20 10:05:00.0000|INFO|DOL.GS.Autonomous|AUTONOMOUS_EXCHANGE_SOLD bot="Thorvald" item="axe" price=5000 crafted=1
2026-11-20 10:06:00.0000|INFO|DOL.GS.Autonomous|AUTONOMOUS_EQUIPMENT_UPGRADE bot="Thorvald" source=loot
2026-11-20 10:07:00.0000|INFO|DOL.GS|ECON_MYSTERY a=1
"""


def write(folder, name, text):
    path = Path(folder) / name
    path.write_text(text, encoding="utf-8")
    return path


class ParseTests(unittest.TestCase):
    def test_a_line_with_quotes_and_a_prefix(self):
        line = rep.parse_line('2026-11-20 10:00:00|INFO|x|CRAFT_TITLE hour=2026-01-05T01:00:00Z bot="Ann Lee" value=100')
        self.assertEqual(line.tag, "CRAFT_TITLE")
        self.assertEqual(line.values["bot"], "Ann Lee")
        self.assertEqual(line.number("value"), 100)
        self.assertEqual(line.when, "2026-01-05T01:00:00Z")

    def test_the_prefix_time_when_no_hour(self):
        line = rep.parse_line("2026-11-20 10:05:00.0000|INFO|x|AUTONOMOUS_EXCHANGE_SOLD bot=\"T\" price=5")
        self.assertEqual(line.when, "2026-11-20 10:05:00")

    def test_other_lines_are_ignored(self):
        self.assertIsNone(rep.parse_line("AUTONOMOUS_EQUIPMENT_UPGRADE bot=x"))
        self.assertIsNone(rep.parse_line("plain text with ECON_SUPPLY inside a sentence, not a line"))


class RehearsalReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.log = write(self.tmp.name, "fresh-7pct-ledger.log", REHEARSAL_LOG)

    def tearDown(self):
        self.tmp.cleanup()

    def test_the_report_names_a_rehearsal_and_its_build(self):
        report = rep.analyse(rep.read_lines([self.log]))
        self.assertTrue(report.rehearsal)
        text = rep.markdown(report, [self.log])
        self.assertIn("tests only, not an in-game result", text)
        self.assertIn("build 1.2-beta abc123", text)
        self.assertIn("| Albion | 2 |", text)          # two supply rows
        self.assertIn("| Albion | LootCoin | 5.5 |", text)
        self.assertIn("1 sessions: 20 crafts, 19 made, 8 skill points", text)
        self.assertIn("Albion Legendary at 2026-01-05T01:10:00Z (Alb0003)", text)
        self.assertIn("| fresh-7pct | no negative purse | pass |", text)
        self.assertEqual(report.residual_rows, [])
        self.assertEqual(report.failed_gates, [])

    def test_check_passes_and_csv_is_written(self):
        csv_path = Path(self.tmp.name) / "timeline.csv"
        with redirect_stdout(io.StringIO()):
            code = rep.main([str(self.log), "--check", "--timeline-csv", str(csv_path), "--out", str(Path(self.tmp.name) / "r.md")])
        self.assertEqual(code, 0)
        rows = csv_path.read_text(encoding="utf-8").splitlines()
        self.assertEqual(len(rows), 3)
        self.assertTrue(rows[1].startswith("2026-01-05T00:00:00Z,Albion,150000,50000,50000,0"))

    def test_a_failed_gate_fails_the_check(self):
        log = write(self.tmp.name, "bad.log", REHEARSAL_LOG.replace("status=pass", "status=fail"))
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(rep.main([str(log), "--check"]), 1)

    def test_direction_against_persona_sim(self):
        persona = {
            "package, bots 2x": [
                {"wealth": {"crafters_median": 6000, "others_median": 20000}, "gold_in": {"hunting": 75, "orders_earned": 25},
                 "snaps": [{"gold_total": 1}, {"gold_total": 100}, {"gold_total": 150}], "stats": {"orders_placed": 10, "orders_filled": 9}},
            ],
            "other": [],
        }
        path = write(self.tmp.name, "persona.json", json.dumps(persona))
        name, theirs = rep.persona_directions(json.loads(path.read_text()), "package")
        self.assertEqual(name, "package, bots 2x")
        self.assertAlmostEqual(theirs["crafterToHunterWealth"], 0.3)
        self.assertAlmostEqual(theirs["supplyGrowth"], 0.5)
        ours = rep.our_directions(rep.analyse(rep.read_lines([self.log])))
        self.assertAlmostEqual(ours["crafterToHunterWealth"], 0.25)
        self.assertAlmostEqual(ours["supplyGrowth"], 1.1)
        self.assertIsNone(ours["ordersFilledShare"])
        verdicts = {key: verdict for key, _, _, verdict in rep.direction_rows(ours, theirs)}
        self.assertEqual(verdicts, {"crafterToHunterWealth": "agree", "huntingShareOfFaucets": "agree", "supplyGrowth": "agree",
                                    "ordersFilledShare": "n/a"})
        out = io.StringIO()
        with redirect_stdout(out):
            rep.main([str(self.log), "--persona", str(path), "--persona-case", "package"])
        self.assertIn("## Direction against persona_sim.py", out.getvalue())
        with self.assertRaises(KeyError):
            rep.persona_directions(persona, "nothing like it")


class ServerLogTests(unittest.TestCase):
    def test_a_server_log_with_a_residual_and_an_alias(self):
        with tempfile.TemporaryDirectory() as folder:
            log = write(folder, "server.log", SERVER_LOG)
            report = rep.analyse(rep.read_lines([log]))
            self.assertFalse(report.rehearsal)
            self.assertEqual(report.tags, {"AUTONOMOUS_EXCHANGE_SOLD": 1, "ECON_FLOWS": 1, "ECON_MYSTERY": 1, "ECON_SUPPLY": 1})
            self.assertEqual(len(report.residual_rows), 1)
            text = rep.markdown(report, [log])
            self.assertIn("this is a server log", text)
            self.assertIn("Unknown tags (not in EconomyLog.AllTags): ECON_MYSTERY", text)
            ours = rep.our_directions(report)
            self.assertAlmostEqual(ours["huntingShareOfFaucets"], 1.0)
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(rep.main([str(log), "--check"]), 1)

    def test_no_lines_is_exit_two(self):
        with tempfile.TemporaryDirectory() as folder:
            log = write(folder, "empty.log", "nothing here\n")
            with redirect_stderr(io.StringIO()):
                self.assertEqual(rep.main([str(log)]), 2)

    def test_the_tag_list_matches_economy_log(self):
        source = (TOOLS.parent / "server" / "GameServer" / "craft" / "economy" / "EconomyLog.cs").read_text(encoding="utf-8")
        import re
        tags = set(re.findall(r'public const string \w+ = "((?:CRAFT|ECON)_[A-Z_]+)"', source))
        self.assertEqual(tags, set(rep.CRAFT_TAGS + rep.ECON_TAGS))


if __name__ == "__main__":
    unittest.main()
