"""Tests of kill_xp_report.py on small synthetic logs (no real log or name from the owner's PC).

    python3 -m pytest -q source/tools/tests/test_kill_xp_report.py
"""
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import kill_xp_report as kxr  # noqa: E402

ROW = ('{t} | INF | DOL.GS.AutonomousKillXpDiagnostics | AUTONOMOUS_KILL_XP seq={seq} every=25 dropped={dropped} '
       'realm={realm} zone="{zone}" region=1 npc="bandit" npcLevel=14 npcXp=52000 con={con} party=group size=5 '
       'contributors=4 minLevel=11 maxLevel=13 avgLevel=12.3 levelDiff={diff} camp="c:1" campTarget="giant frog" '
       'onTarget={on} sameName=0 xpPerMember={xp} xpSum=0 share={share} otherParties={others} lost={lost} '
       'shares="group/{realm}/4/{share}/killer"')
GOAL = ('{t} | INF | DOL.GS.AutonomousGoalDiagnostics | AUTONOMOUS_GOAL_ATTEMPT '
        '{{"schema":1,"realm":{realm},"zone":"Domnann","xp":{xp},"targetXp":{target},"bot":"Someone"}}')


def row(t="11/24 10:40:00", seq=25, dropped=0, realm="Albion", zone="Salisbury Plains", con="ORANGE", diff=1, on=1,
        xp="10000/10000", share="1.000", others=0, lost="0.000"):
    return ROW.format(t=t, seq=seq, dropped=dropped, realm=realm, zone=zone, con=con, diff=diff, on=on, xp=xp,
                      share=share, others=others, lost=lost)


class KillXpReportTest(unittest.TestCase):
    def write(self, *lines):
        handle = tempfile.NamedTemporaryFile("w", suffix=".log", delete=False, encoding="utf-8")
        handle.write("\n".join(lines) + "\n")
        handle.close()
        self.addCleanup(Path(handle.name).unlink)
        return Path(handle.name)

    def test_parses_the_server_row_format(self):
        fields = kxr.parse_fields(row(zone="Forest Sauvage", on="?", xp="5/0/7").split("AUTONOMOUS_KILL_XP ", 1)[1])
        parsed = kxr.kill_row(fields)
        self.assertEqual(parsed["zone"], "Forest Sauvage")
        self.assertEqual(parsed["xp"], [5, 0, 7])
        self.assertIsNone(parsed["onTarget"])
        self.assertEqual(parsed["every"], 25)

    def test_per_realm_per_zone_split_and_off_target(self):
        log = self.write(
            row(xp="10000/10000"),
            row(xp="4000/4000", share="0.400", others=1, lost="0.100", on=0, con="BLUE", diff=-2),
            row(realm="Hibernia", zone="Domnann", xp="40000/40000/40000", dropped=2),
            "11/24 10:41:00 | INF | DOL.GS.Other | an unrelated line",
        )
        kills, goals = kxr.read_logs([log], year=2026)
        report = kxr.build_report(kills, goals, min_rows=1)
        alb, hib = report["realms"]["Albion"], report["realms"]["Hibernia"]
        self.assertEqual(alb["rows"], 2)
        self.assertEqual(alb["kills"], 50)
        self.assertEqual(alb["xpPerMemberMedian"], 7000)
        self.assertEqual(alb["splitShare"], 0.5)
        self.assertEqual(alb["offTargetShare"], 0.5)
        self.assertEqual(alb["cons"], {"ORANGE": 0.5, "BLUE": 0.5})
        self.assertEqual(hib["kills"], 75, "a row with dropped=2 stands for three sampled kills")
        self.assertEqual(hib["xpPerMemberMedian"], 40000)
        self.assertEqual([z["zone"] for z in report["zones"]], ["Salisbury Plains", "Domnann"])
        text = kxr.render(report)
        self.assertIn("Albion", text)
        self.assertIn("Hib Domnann", text)

    def test_one_played_hour_window(self):
        log = self.write(row(t="11/24 10:30:00"), row(t="11/24 10:40:00"), row(t="11/24 11:39:59"),
                         row(t="11/24 11:40:00"))
        kills, _ = kxr.read_logs([log], kxr.parse_since("11/24 10:40", 2026),
                                 kxr.parse_since("11/24 11:40", 2026), 2026)
        self.assertEqual(len(kills), 2)

    def test_goal_attempts_from_detail_log(self):
        detail = self.write(GOAL.format(t="11/24 10:40:00", realm=3, xp=1000, target=750),
                            GOAL.format(t="11/24 10:41:00", realm=1, xp=0, target=0))
        kills, goals = kxr.read_logs([detail], year=2026)
        report = kxr.build_report(kills, goals)
        self.assertEqual(report["goalAttempts"]["Hibernia"]["offTargetXpShare"], 0.25)
        self.assertIsNone(report["goalAttempts"]["Albion"]["offTargetXpShare"])
        self.assertNotIn("Someone", kxr.render(report), "names are not printed")

    def test_cli_json_and_nothing_to_report(self):
        log = self.write(row())
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(kxr.main([str(log), "--json", "--min-rows", "1"]), 0)
        self.assertEqual(json.loads(out.getvalue())["realms"]["Albion"]["rows"], 1)
        empty = self.write("11/24 10:41:00 | INF | DOL.GS.Other | nothing")
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(kxr.main([str(empty)]), 2)


if __name__ == "__main__":
    unittest.main()
