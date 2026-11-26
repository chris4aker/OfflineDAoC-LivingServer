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
       'realm={realm} zone="{zone}" region=1 npc="bandit" npcLevel=14 npcXp=52000 con={con} party={party} size=5 '
       'contributors=4 minLevel={minlevel} maxLevel={maxlevel} avgLevel=12.3 levelDiff={diff} camp="c:1" campTarget="giant frog" '
       '{camplevel}onTarget={on} sameName=0 xpPerMember={xp} xpSum=0 share={share} otherParties={others} lost={lost} '
       'shares="group/{realm}/4/{share}/killer"')
GOAL = ('{t} | INF | DOL.GS.AutonomousGoalDiagnostics | AUTONOMOUS_GOAL_ATTEMPT '
        '{{"schema":1,"realm":{realm},"zone":"Domnann","xp":{xp},"targetXp":{target},"bot":"Someone"}}')
GOAL2 = ('{t} | INF | DOL.GS.AutonomousGoalDiagnostics | AUTONOMOUS_GOAL_ATTEMPT ' + '{body}')


def row(t="11/24 10:40:00", seq=25, dropped=0, realm="Albion", zone="Salisbury Plains", con="ORANGE", diff=1, on=1,
        xp="10000/10000", share="1.000", others=0, lost="0.000", party="group", maxlevel=13, camplevel=None, minlevel=11):
    # camplevel=None: a row from before #544's campLevel field (old logs stay readable)
    return ROW.format(t=t, seq=seq, dropped=dropped, realm=realm, zone=zone, con=con, diff=diff, on=on, xp=xp,
                      share=share, others=others, lost=lost, party=party, maxlevel=maxlevel, minlevel=minlevel,
                      camplevel="" if camplevel is None else f"campLevel={camplevel} ")


def goal(realm=1, level=20, target_level=None, bot=7, started="2026-11-24T10:00:00Z", group="", size=1,
         arrived=None, xp=1000, seconds=3600):
    body = {"schema": 1, "realm": realm, "level": level, "botId": bot, "bot": "Someone", "startedUtc": started,
            "groupId": group, "groupSize": size, "arrivedUtc": arrived, "xp": xp, "targetXp": xp,
            "elapsedSeconds": seconds, "zone": "Domnann"}
    if target_level is not None:
        body["targetLevel"] = target_level
    return GOAL2.format(t="11/24 10:40:00", body=json.dumps(body))


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


    # GitHub #544 follow-up (FarmVibes): target con, zone names, level bands, re-rolls, arrival.
    def test_target_con_per_realm_solo_and_group(self):
        log = self.write(
            row(party="solo", maxlevel=20, camplevel=19, xp="54000"),
            row(party="solo", maxlevel=21, camplevel=20, xp="55000"),
            row(party="group", maxlevel=22, camplevel=24, xp="30000/30000"),
            row(party="solo", maxlevel=20, xp="1"),  # an old row without campLevel: not counted as con 0
            row(realm="Hibernia", zone="Domnann", party="solo", maxlevel=20, camplevel=21, xp="73000"),
        )
        kills, goals = kxr.read_logs([log], year=2026)
        report = kxr.build_report(kills, goals, min_rows=1)
        alb = report["realmParties"]["Albion"]
        self.assertEqual(alb["solo"]["targetConMedian"], -1)
        self.assertEqual(alb["solo"]["targetConRows"], 2, "the row without campLevel is left out")
        self.assertEqual(alb["group"]["targetConMedian"], 2)
        self.assertEqual(report["realmParties"]["Hibernia"]["solo"]["targetConMedian"], 1)
        self.assertIsNone(kxr.kill_row(kxr.parse_fields("campLevel=? maxLevel=5"))["targetCon"])
        text = kxr.render(report)
        self.assertIn("tcon", text)
        self.assertRegex(text, r"  solo .* -1 ")
        self.assertRegex(text, r"  group .* \+2 ")

    def test_zone_spellings_merge(self):
        for a, b in (("Black Mtns. North", "Black Mtns North"), ("Silvermine Mts", "Silvermine Mts."),
                     ("Aegirs Landing", "Aegir's Landing")):
            self.assertEqual(kxr.zone_key(a), kxr.zone_key(b), (a, b))
        self.assertNotEqual(kxr.zone_key("Black Mtns North"), kxr.zone_key("Black Mtns South"))
        log = self.write(row(realm="Midgard", zone="Aegirs Landing"), row(realm="Midgard", zone="Aegir's Landing"),
                         row(realm="Midgard", zone="Aegir's Landing"), row(zone="Black Mtns. North"),
                         row(zone="Black Mtns North"))
        kills, goals = kxr.read_logs([log], year=2026)
        zones = {(z["realm"], z["zone"]): z["rows"] for z in kxr.build_report(kills, goals, min_rows=1)["zones"]}
        self.assertEqual(zones, {("Midgard", "Aegir's Landing"): 3, ("Albion", "Black Mtns. North"): 2})

    def test_level_bands_and_rerolled_bots(self):
        log = self.write(
            row(maxlevel=12, xp="9000/9000"), row(maxlevel=23, camplevel=23, xp="70000/70000"),
            goal(bot=1, level=24, target_level=23, started="2026-11-24T09:00:00Z", xp=2000, seconds=1800),
            goal(bot=2, level=26, started="2026-11-24T09:00:00Z"),
            goal(bot=2, level=1, started="2026-11-24T11:00:00Z", xp=500),   # re-rolled: level dropped
            goal(bot=2, level=2, started="2026-11-24T12:00:00Z", xp=500),
            goal(realm=3, bot=3, level=21, target_level=22),
        )
        kills, goals = kxr.read_logs([log], year=2026)
        report = kxr.build_report(kills, goals, min_rows=1)
        self.assertEqual(set(report["killBands"]["Albion"]), {"10-14", "20-24"})
        self.assertEqual(report["killBands"]["Albion"]["20-24"]["targetConMedian"], 0)
        self.assertEqual(report["rerolledBots"], {"Albion": 1})
        alb = report["goalBands"]["Albion"]
        self.assertEqual(set(alb), {"20-24"}, "the re-rolled bot's attempts are left out of every band")
        self.assertEqual(alb["20-24"], {"attempts": 1, "xpPerHour": 4000, "targetConMedian": -1})
        self.assertEqual(report["goalBands"]["Hibernia"]["20-24"]["targetConMedian"], 1)
        self.assertEqual(kxr.band_of(0), "00-04")
        self.assertEqual(kxr.band_of(29), "25-29")
        self.assertIn("re-rolled bots (level dropped): Albion 1", kxr.render(report))

    def test_arrival_share_solo_and_group(self):
        log = self.write(goal(bot=1, arrived="2026-11-24T10:03:00Z"), goal(bot=2),
                         goal(bot=3, group="g1", size=3, arrived="2026-11-24T10:05:00Z"),
                         goal(bot=4, group="g1", size=3), goal(bot=5, group="g1", size=3))
        _, goals = kxr.read_logs([log], year=2026)
        alb = kxr.build_report([], goals)["goalAttempts"]["Albion"]
        self.assertEqual(alb["solo"]["arrivedShare"], 0.5)
        self.assertEqual(alb["group"]["arrivedShare"], 0.333)
        self.assertIsNone(alb["solo"]["targetConMedian"], "attempts from before targetLevel read as unknown")


    def test_level_cap_kills_leave_the_xp_median(self):
        # #671: level-50 parties are paid nothing by design; their 0s must not make the group median 0.
        log = self.write(
            row(party="group", minlevel=50, maxlevel=50, xp="0/0/0"),
            row(party="group", minlevel=50, maxlevel=50, xp="0/0/0"),
            row(party="group", minlevel=21, maxlevel=23, xp="100/100"),
        )
        kills, goals = kxr.read_logs([log], year=2026)
        report = kxr.build_report(kills, goals, min_rows=1)
        parties = report["realmParties"]["Albion"]
        self.assertEqual(parties["group <50"]["xpPerMemberMedian"], 100)
        self.assertEqual(parties["group 50"]["cappedRows"], 2)
        self.assertEqual(parties["group 50"]["cappedValues"], 6)
        self.assertIsNone(parties["group 50"]["xpPerMemberMedian"])
        self.assertEqual(parties["group"]["xpPerMemberMedian"], 100, "no 0 median for the group line")
        self.assertEqual(report["realms"]["Albion"]["xpPerMemberMedian"], 100)
        text = kxr.render(report)
        self.assertIn("capped", text)
        self.assertRegex(text, r"  group <50 .*100")
        self.assertRegex(text, r"  group 50 .* 2/6")
        self.assertNotRegex(text, r"\n  group  ")
        self.assertIn("not XP lost", text)

    def test_solo_at_the_cap_without_min_level(self):
        fields = kxr.parse_fields("party=solo maxLevel=50 xpPerMember=0")
        self.assertTrue(kxr.kill_row(fields)["capped"])
        self.assertFalse(kxr.kill_row(kxr.parse_fields("party=group maxLevel=50 xpPerMember=0/9"))["capped"],
                         "a mixed group without minLevel is not assumed capped")

    def test_output_unchanged_without_level_cap_kills(self):
        log = self.write(row(xp="7000/7000"), row(party="solo", xp="9000"), row(realm="Hibernia", zone="Domnann"))
        kills, goals = kxr.read_logs([log], year=2026)
        report = kxr.build_report(kills, goals, min_rows=1)
        self.assertEqual(set(report["realmParties"]["Albion"]), {"solo", "group"})
        self.assertTrue(all(s["cappedRows"] == 0 for s in report["realms"].values()))
        text = kxr.render(report)
        self.assertNotIn("capped", text)
        tables = text.split("\n\nshare = ")[0]
        self.assertTrue(tables.splitlines()[1].rstrip().endswith("off"), "no capped column")
        self.assertRegex(text, r"  group .*7,000")


if __name__ == "__main__":
    unittest.main()
