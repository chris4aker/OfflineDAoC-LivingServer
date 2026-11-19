"""Tests of played_check_log.py on small synthetic server logs (no real log or name from the owner's PC).

    python3 -m pytest -q source/tools/tests/test_played_check_log.py
"""
import contextlib
import datetime as dt
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import played_check_log as pcl  # noqa: E402

T0 = dt.datetime(2026, 11, 17, 6, 0, 0)
FORK = ("FORK_BUILD name=1.2-beta commit={commit} bot_guilds=on mana_regen=1.5 "
        "living=memory,grats sluaghbinder={edition}")


class Log:
    """server.log lines as logconfig.xml lays them out: 'MM/DD HH:MM:SS | LVL | logger | message'."""

    def __init__(self):
        self.lines = []

    def add(self, t, message, level="INF", logger="DOL.GS.Test", *continuation):
        self.lines.append(f"{t.strftime('%m/%d %H:%M:%S')} | {level} | {logger} | {message}")
        self.lines.extend(continuation)
        return self

    def start(self, t, commit="abc1234def56", edition="on", fork=True):
        self.add(t, "Current directory is: C:\\Games\\OfflineDAoC\\runtime\\server", "DBG", "DOL.GS.GameServer")
        self.add(t, "No npctemplate with ID 123 found.", "ERR", "DOL.GS.NpcTemplateMgr")
        self.add(t, "a data-loading error while the server starts", "ERR", "DOL.GS.Other")
        if fork:
            self.add(t + dt.timedelta(seconds=8), FORK.format(commit=commit, edition=edition), "WRN",
                     "DOL.GS.ForkBuildInfo")
        self.add(t + dt.timedelta(seconds=15), "GameServer startup completed in 14.6 seconds", "INF",
                 "DOL.GS.GameServer")
        return self

    def stop(self, t):
        return self.add(t, "Stopping server...", "INF", "DOL.GS.GameServer")

    def run(self, *args):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "server.log"
            path.write_text("\n".join(self.lines) + "\n", encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
                code = pcl.main([str(path), "--json", "--year", "2026", *args])
            return code, json.loads(out.getvalue())


def at(minutes, seconds=0):
    return T0 + dt.timedelta(minutes=minutes, seconds=seconds)


def builds(result):
    return [s for s in result["sections"] if s["judged"]]


def check(section, cid):
    found = [c for c in section["checks"] if c["id"] == cid]
    assert len(found) == 1, cid
    return found[0]


def only(result):
    judged = builds(result)
    assert len(judged) == 1
    return judged[0]


class BuildSplit(unittest.TestCase):
    def test_each_build_is_judged_apart_and_the_tail_without_fork_build_is_not(self):
        log = Log()
        # The tail of an earlier run, with no FORK_BUILD: a won raid that must not be credited to any build.
        log.add(at(0), 'REALM_RAID_ENDED event=dragon-albion outcome="Boss defeated" minutes=50 named=1 wipes=0',
                logger="DOL.GS.AutonomousRealmRaid")
        log.stop(at(1))
        log.start(at(2), commit="1111111aaaaa")
        log.add(at(5), 'REALM_RAID_ENDED event=epic-midgard outcome="Defeated" minutes=55 named=9 wipes=2')
        log.stop(at(10))
        log.start(at(11), commit="2222222bbbbb")
        log.add(at(20), 'REALM_RAID_ENDED event=epic-hibernia outcome="Boss defeated" minutes=40 named=3 wipes=0')
        code, result = log.run()
        self.assertEqual(code, 0)
        parts = result["sections"]
        self.assertEqual(len(parts), 3)
        self.assertFalse(parts[0]["judged"])
        self.assertIn("no FORK_BUILD", parts[0]["why"])
        first, second = builds(result)
        self.assertEqual((first["build"], second["build"]), ("1111111", "2222222"))
        self.assertEqual(check(first, "won")["status"], pcl.NOT_SEEN)
        self.assertEqual(check(first, "result")["status"], pcl.SEEN)
        self.assertEqual(check(second, "won")["status"], pcl.SEEN)
        self.assertEqual(check(second, "won")["detail"], "epic-hibernia")
        self.assertTrue(first["clean_stop"])
        self.assertFalse(second["clean_stop"])

    def test_build_filter_and_nothing_to_judge(self):
        log = Log().start(at(0), commit="1111111aaaaa").stop(at(5)).start(at(6), commit="2222222bbbbb")
        code, result = log.run("--build", "2222222")
        self.assertEqual(code, 0)
        self.assertEqual([s["build"] for s in builds(result)], ["2222222"])
        code, result = log.run("--build", "9999999")
        self.assertEqual(code, 2)
        code, result = Log().start(at(0), fork=False).run()
        self.assertEqual(code, 2)
        self.assertFalse(result["sections"][0]["judged"])

    def test_a_fork_build_after_a_tail_with_no_start_line_opens_a_new_part(self):
        log = Log()
        log.add(at(0), "RVR_BOT_KILLED victim=A id=1 realm=Albion killer=\"B\"")
        log.add(at(1), FORK.format(commit="3333333ccccc", edition="on"), "WRN", "DOL.GS.ForkBuildInfo")
        log.add(at(2), "RVR_BOT_KILLED victim=C id=2 realm=Albion killer=\"D\"")
        code, result = log.run()
        self.assertFalse(result["sections"][0]["judged"])
        self.assertIn("1 kills", check(only(result), "frontier")["detail"])

    def test_since_skips_earlier_lines_but_keeps_the_build(self):
        log = Log().start(at(0), commit="4444444ddddd")
        log.add(at(5), 'REALM_RAID_ENDED event=epic-midgard outcome="Boss defeated" minutes=50 named=1 wipes=0')
        log.add(at(30), 'REALM_RAID_ENDED event=epic-albion outcome="Defeated" minutes=50 named=1 wipes=2')
        code, result = log.run("--since", at(10).strftime("%m/%d %H:%M"))
        sec = only(result)
        self.assertEqual(sec["build"], "4444444")
        self.assertEqual(check(sec, "won")["status"], pcl.NOT_SEEN)
        self.assertEqual([r["event"] for r in sec["raids"]], ["epic-albion"])


class Raids(unittest.TestCase):
    def raid_log(self):
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_STARTED event=epic-midgard present=82 required=80 support=96 minutes=21 forced=True")
        log.add(at(20), 'REALM_RAID_WIPE event=epic-midgard boss="Icelord Agmundr" failures=1 support=96 living=92 '
                        'engaged=92 fallen=92 minutes=47')
        log.add(at(21), "REALM_RAID_RETURN_WAVE event=epic-midgard waiting=2 outside=2 fallen=0 waited_s=90")
        log.add(at(22), "REALM_RAID_RETURN_WAVE event=epic-midgard step=formed wave=1 members=2 steps=39 walk=44035")
        return log

    def test_a_raid_from_start_to_a_loss(self):
        log = self.raid_log()
        log.add(at(26), 'REALM_RAID_RETURN_WAVE event=epic-midgard step=stuck wave=1 at=35/49 living=92 atStep=0 '
                        'reason="no headway for 4 minutes and no help left; the stall clock decides"', "WRN")
        log.add(at(30), 'REALM_RAID_WIPE event=epic-midgard boss="Icelord Agmundr" failures=2 living=78 engaged=96 fallen=48')
        log.add(at(30), 'REALM_RAID_ENDED event=epic-midgard outcome="Defeated" support=120 required=80 forced=True '
                        'minutes=55 named=9 wipes=2 reason="Defeated at Icelord Agmundr after 2 failed attempts."')
        sec = only(log.run()[1])
        raid = sec["raids"][0]
        self.assertEqual((raid["event"], raid["outcome"], raid["result"]), ("epic-midgard", "Defeated", "lost"))
        self.assertEqual((raid["minutes"], raid["named"], raid["wipes"]), ("55", "9", 2))
        self.assertEqual(raid["stuck_return_waves"], 1)
        self.assertEqual(raid["present"], "82")
        # the stuck wave is its own row, not the gather-and-go-in claim (DIAG-5378-regroup issue 2)
        self.assertEqual(check(sec, "return_wave")["status"], pcl.NOT_SEEN, "formed, never went in")
        stuck = check(sec, "return_stuck")
        self.assertEqual(stuck["status"], pcl.CONTRADICTED)
        self.assertIn("step=stuck", stuck["line"])
        self.assertIn("1 wave(s) stuck at least once: 0 made headway again, 1 never did", stuck["detail"])
        self.assertEqual(check(sec, "result")["status"], pcl.SEEN)

    def test_outcomes(self):
        log = Log().start(at(0))
        for i, outcome in enumerate(pcl.OUTCOMES):
            log.add(at(i + 1), f'REALM_RAID_ENDED event=raid-{i} outcome="{outcome}" minutes=3 named=0 wipes=0')
        sec = only(log.run()[1])
        self.assertEqual([r["result"] for r in sec["raids"]],
                         ["won", "lost", "timed out (stall)", "superseded", "stopped by player", "failed rally",
                          "sweep cap"])
        result = check(sec, "result")
        self.assertEqual(result["status"], pcl.CONTRADICTED)
        self.assertIn("Timed out", result["line"])

    def test_return_wave_without_a_stuck_one_is_seen(self):
        log = self.raid_log()
        log.add(at(23), "REALM_RAID_RETURN_WAVE event=epic-midgard step=moving wave=1 at=2/39 living=2 together=2")
        sec = only(log.run()[1])
        self.assertEqual(check(sec, "return_wave")["status"], pcl.SEEN)
        self.assertEqual(check(sec, "return_stuck")["status"], pcl.NOT_SEEN)
        self.assertFalse(check(sec, "return_stuck")["must"])
        self.assertEqual(sec["raids"][0]["result"], "no end in this build's log")

    def test_a_stuck_wave_that_makes_headway_again_is_reported_apart(self):
        # epic-hibernia wave 5 on 5378dbe: gathered (54), went in together (123), then stuck at the way in
        log = self.raid_log()
        log.add(at(23), "REALM_RAID_RETURN_WAVE event=epic-midgard step=moving wave=1 at=2/39 living=54 together=54")
        log.add(at(27), 'REALM_RAID_RETURN_WAVE event=epic-midgard step=stuck wave=1 at=5/39 living=13 atStep=4 '
                        'reason="no headway for 4 minutes and no help left; the stall clock decides"', "WRN")
        log.add(at(33), "REALM_RAID_RETURN_WAVE event=epic-midgard step=passable wave=1 at=5/39 kills_at_step=14")
        sec = only(log.run()[1])
        self.assertEqual(check(sec, "return_wave")["status"], pcl.SEEN, "the gather and go-in claim stands")
        stuck = check(sec, "return_stuck")
        self.assertEqual(stuck["status"], pcl.SEEN)
        self.assertIn("1 made headway again, 0 never did", stuck["detail"])
        # a second wave that never moves again contradicts the stuck row only
        log.add(at(40), 'REALM_RAID_RETURN_WAVE event=epic-midgard step=stuck wave=2 at=2/42 living=123 atStep=0 '
                        'reason="no headway"', "WRN")
        sec = only(log.run()[1])
        self.assertEqual(check(sec, "return_wave")["status"], pcl.SEEN)
        self.assertEqual(check(sec, "return_stuck")["status"], pcl.CONTRADICTED)
        self.assertIn("wave=2", check(sec, "return_stuck")["line"])

    def test_teleport_near_a_resting_boss(self):
        log = self.raid_log()
        status = ('REALM_EXPEDITION_TARGET_STATUS region=160 target="Icelord Agmundr" level=81 health={h}% '
                  'inCombat={c} raidersWithin1000=0 targeting=0 at=15037,50114,12408 spawnDistance=0')
        stranded = ("REALM_EXPEDITION_STRANDED_REGROUP bot=Bjagira id=4109 region=160 from=25812,35822,14461 "
                    "to={to}")
        log.add(at(23), status.format(h=60, c="True"))
        log.add(at(23, 10), stranded.format(to="15037,49914,12424"), "WRN")      # boss fighting: not flagged
        log.add(at(24), status.format(h=100, c="False"))
        log.add(at(24, 30), stranded.format(to="40000,40000,12424"), "WRN")      # far away: not flagged
        log.add(at(25), stranded.format(to="15037,49914,12424"), "WRN")          # 200 from the resting boss
        sec = only(log.run()[1])
        tele = check(sec, "teleport_resting_boss")
        self.assertEqual(tele["status"], pcl.CONTRADICTED)
        self.assertIn("3 stranded regroups, 1 within 900", tele["detail"])
        self.assertIn("201 from resting Icelord Agmundr", tele["line"])  # 3D distance
        self.assertEqual(sec["raids"][0]["teleports_near_resting_boss"], 1)
        # a defeated boss is no longer resting there
        log.add(at(26), 'REALM_EXPEDITION_TARGET_DEFEATED region=160 target="Icelord Agmundr" level=81 minutes=3')
        log.add(at(27), stranded.format(to="15037,49914,12424"), "WRN")
        self.assertIn("4 stranded regroups, 1 within", check(only(log.run()[1]), "teleport_resting_boss")["detail"])

    STATUS = ('REALM_EXPEDITION_TARGET_STATUS region={r} target="{b}" level=81 health={h}% '
              'inCombat={c} raidersWithin1000=0 targeting=0 at=15037,50114,12408 spawnDistance=0')
    NEAR = "REALM_EXPEDITION_STRANDED_REGROUP bot=Bjagira id=4109 region={r} from=25812,35822,14461 to=15037,49914,12424"

    def teleports_near(self, log):
        return check(only(log.run()[1]), "teleport_resting_boss")

    def test_an_engaged_boss_is_not_resting_until_its_wipe(self):
        # Icelord Agmundr on 5378dbe: status inCombat=False at 23:17:31, FINAL_ENGAGE 23:18:22, teleports 0-6 s later
        log = self.raid_log()
        log.add(at(23), self.STATUS.format(r=160, b="Icelord Agmundr", h=100, c="False"))
        log.add(at(24), 'REALM_EXPEDITION_FINAL_ENGAGE region=160 target="Icelord Agmundr" mustered=69 needed=50 '
                        'living=97 attempt=2')
        log.add(at(24, 5), self.NEAR.format(r=160), "WRN")
        row = self.teleports_near(log)
        self.assertEqual(row["status"], pcl.SEEN, "engaged: not resting")
        self.assertIn("1 stranded regroups, 0 within", row["detail"])
        log.add(at(26), 'REALM_EXPEDITION_WIPE region=160 target="Icelord Agmundr" level=81 failures=2 of=2 engaged=90 '
                        'fallen=89 health=100 lowest=74 how="left combat"', "WRN")
        log.add(at(26, 10), self.NEAR.format(r=160), "WRN")
        self.assertEqual(self.teleports_near(log)["status"], pcl.SEEN, "the status from before the fight is spent")
        log.add(at(27), self.STATUS.format(r=160, b="Icelord Agmundr", h=100, c="False"))
        log.add(at(27, 10), self.NEAR.format(r=160), "WRN")
        self.assertEqual(self.teleports_near(log)["status"], pcl.CONTRADICTED, "after the wipe it rests again")

    def test_each_engage_signal_and_its_ends(self):
        signals = {
            "early pull commit": 'REALM_EXPEDITION_EARLY_PULL region=191 target="Boss" near=67 inside=123 raid=124 '
                                 'needed=86 decision=Commit health=97',
            "target call": 'REALM_EXPEDITION_TARGET_CALLED region=191 target="Boss" health=100 called=16 idleSeconds=0',
            "ledger, it hits the raid": 'EPIC_FIGHT_LEDGER region=191 target="Other" seconds=60 '
                                        'raid_dealt="Other:100/2/2" raid_took="Other:5/1/1,Boss:18613/17/9"',
            "ledger, the raid hits it": 'EPIC_FIGHT_LEDGER region=191 target="Other" seconds=60 raid_dealt="Boss:9/1/1"',
            "required-adds switch": 'REALM_EXPEDITION_TACTIC region=191 from="Boss" to="Add" health=99 '
                                    'reason="required-adds of "Boss" alive=2; ranged" tactic=galladoria/xaga',
        }
        ends = {
            "defeat": 'REALM_EXPEDITION_TARGET_DEFEATED region=191 target="Boss" level=81 minutes=3',
            "heal": 'REALM_EXPEDITION_TARGET_HEALED region=191 target="Boss" from=74 to=100 inCombat=False',
            "wipe": 'REALM_EXPEDITION_WIPE region=191 target="Boss" level=81 failures=1 of=2 how="left combat"',
        }
        for name, signal in signals.items():
            with self.subTest(signal=name):
                log = self.raid_log()
                log.add(at(23), self.STATUS.format(r=191, b="Boss", h=100, c="False"))
                log.add(at(24), signal)
                log.add(at(25), self.NEAR.format(r=191), "WRN")
                self.assertEqual(self.teleports_near(log)["status"], pcl.SEEN)
        for name, end in ends.items():
            with self.subTest(end=name):
                log = self.raid_log()
                log.add(at(23), self.STATUS.format(r=191, b="Boss", h=100, c="False"))
                log.add(at(24), signals["target call"])
                log.add(at(25), end)
                log.add(at(25, 10), self.STATUS.format(r=191, b="Boss", h=100, c="False"))
                log.add(at(26), self.NEAR.format(r=191), "WRN")
                self.assertEqual(self.teleports_near(log)["status"], pcl.CONTRADICTED)
        # an early pull held, the muster and a non-adds tactic are not an engage
        for name, line in {"hold": signals["early pull commit"].replace("Commit", "Hold"),
                           "clear-first": 'REALM_EXPEDITION_TACTIC region=191 from="Boss" to="forerunner" '
                                          'reason="clear-first of "Boss" is up again: cleared before the pull"'}.items():
            with self.subTest(not_engaged=name):
                log = self.raid_log()
                log.add(at(23), self.STATUS.format(r=191, b="Boss", h=100, c="False"))
                log.add(at(24), line)
                log.add(at(25), self.NEAR.format(r=191), "WRN")
                self.assertEqual(self.teleports_near(log)["status"], pcl.CONTRADICTED)
        # an engage lapses after 15 minutes without a sign of the fight
        log = self.raid_log()
        log.add(at(1), signals["target call"])
        log.add(at(10), self.STATUS.format(r=191, b="Boss", h=100, c="False"))
        log.add(at(17), self.NEAR.format(r=191), "WRN")
        self.assertEqual(self.teleports_near(log)["status"], pcl.CONTRADICTED)

    def test_xaga_in_the_ledger_is_not_resting(self):
        # Xaga on 5378dbe: one status inCombat=False at 00:08:48, then in the ledger every minute (173 false hits)
        log = self.raid_log()
        log.add(at(23), self.STATUS.format(r=191, b="Xaga", h=100, c="False"))
        log.add(at(23, 4), self.NEAR.format(r=191), "WRN")                       # before any fight: a real hit
        for minute in range(25, 37):
            log.add(at(minute), 'EPIC_FIGHT_LEDGER region=191 target="spindler" seconds=60 '
                                'raid_dealt="Beatha:97347/288/73" raid_took="Beatha:1/1/1,Xaga:18613/17/9"')
            log.add(at(minute, 20), self.NEAR.format(r=191), "WRN")
        row = self.teleports_near(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("13 stranded regroups, 1 within", row["detail"])
        # 00:23:38: a wipe scored at Xaga in the middle of the fight; the status before the fight no longer counts, so a
        # teleport before the next ledger line is not "near a resting Xaga"; a new status after the wipe would be
        log.add(at(37), 'REALM_EXPEDITION_WIPE region=191 target="Xaga" level=81 failures=1 of=2 '
                        'how="raid-wide deaths outside an attempt" raidTarget="Tine"', "WRN")
        log.add(at(37, 5), self.NEAR.format(r=191), "WRN")
        self.assertIn("14 stranded regroups, 1 within", self.teleports_near(log)["detail"])
        log.add(at(38), self.STATUS.format(r=191, b="Xaga", h=100, c="False"))
        log.add(at(38, 5), self.NEAR.format(r=191), "WRN")
        self.assertIn("15 stranded regroups, 2 within", self.teleports_near(log)["detail"])

    def test_teleports_far_from_bosses_are_seen(self):
        log = self.raid_log()
        log.add(at(23), "REALM_EXPEDITION_STRANDED_REGROUP bot=X id=1 region=160 from=1,1,1 to=9000,9000,9000", "WRN")
        self.assertEqual(check(only(log.run()[1]), "teleport_resting_boss")["status"], pcl.SEEN)
        self.assertEqual(check(only(Log().start(at(0)).run()[1]), "teleport_resting_boss")["status"], pcl.NOT_SEEN)

    def test_raid_rows_195_213_192_and_the_sweep(self):
        log = Log().start(at(0))
        log.add(at(1), "EPIC_SWEEP_START raids=3 lanes=3 cap=480 order=a|b|c guest=\"\" guestRealm=_First")
        log.add(at(1), "EPIC_SWEEP_ADD added=5 skipped=0 order=d,e lanes=Albion:3")
        log.add(at(2), 'EPIC_SWEEP_RAID_START event=epic-albion name="Caer Sidi" size=100 try=1 lane=Albion')
        log.add(at(3), 'REALM_RAID_TALE_FIRST event=dungeon-albion-avalon realm=Albion forced=False')
        log.add(at(4), 'REALM_RAID_CALLED event=dungeon-albion-avalon realm=Albion forced=False '
                       'after="the tale was told" opened=True')
        log.add(at(5), 'REALM_EXPEDITION_FINAL_ENGAGE region=60 target="lich\'s eye" mustered=81 needed=81 living=116')
        log.add(at(6), 'REALM_EXPEDITION_ENCOUNTER_RESET region=160 target="Icelord Hakr" engaged=74 fallen=6 '
                       'reason="the encounter reset while the raid still stood; the raid musters again and no attempt '
                       'is used"', "WRN")
        log.add(at(7), 'REALM_EXPEDITION_ROUTE_GATE event=summoners-albion region=277 into=248 '
                       'reason="a living boss covers the walk to the way in"', "WRN")
        log.add(at(8), 'REALM_RAID_EPIC_RESPAWN_HELD npc="blue cattail sporite" region=191 respawn_s=1260')
        log.add(at(9), 'EPIC_SWEEP_END why="server stopping" results=1 guest=""')
        sec = only(log.run()[1])
        for cid in ("tale_call", "pull_70", "reset_no_attempt", "route_boss", "respawn_held", "sweep"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertIn("lanes=3 cap=480", check(sec, "sweep")["detail"])
        self.assertIn('why=server stopping', check(sec, "sweep")["detail"])
        log.add(at(10), 'REALM_EXPEDITION_FINAL_ENGAGE region=60 target="x" mustered=50 needed=81 living=116')
        self.assertEqual(check(only(log.run()[1]), "pull_70")["status"], pcl.CONTRADICTED)

    def test_a_pull_that_never_logged_an_engage(self):
        # Xaga and Aroon on 5378dbe: a required-adds switch and wipes outside an attempt, with no FINAL_ENGAGE or
        # EARLY_PULL for either boss, under "30 final engages at the needed count, 0 below it"
        engage = 'REALM_EXPEDITION_FINAL_ENGAGE region=191 target="spindler" mustered=94 needed=86 living=124'
        switch = ('REALM_EXPEDITION_TACTIC region=191 from="Xaga" to="Beatha" health=99 approach=ranged-stand '
                  'reason="required-adds of "Xaga" alive=2; ranged from the floor 620 away" tactic=galladoria/xaga')
        wipe = ('REALM_EXPEDITION_WIPE region=191 target="{t}" level=67 failures=1 of=2 engaged=125 fallen=71 '
                'health=39 how="raid-wide deaths outside an attempt" raidTarget="{t}"')
        log = Log().start(at(0))
        log.add(at(1), engage)
        self.assertEqual(check(only(log.run()[1]), "pull_70")["status"], pcl.SEEN)
        log.add(at(2), switch)
        log.add(at(8), wipe.format(t="Beatha"), "WRN")                  # the same encounter: counted once
        log.add(at(9), wipe.format(t="Aroon the Urlamhai"), "WRN")      # a wipe with no switch before it
        row = check(only(log.run()[1]), "pull_70")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("1 final engages at the needed count, 0 below it, 2 pull(s) with no FINAL_ENGAGE", row["detail"])
        self.assertIn("Xaga, Aroon the Urlamhai", row["detail"])
        self.assertIn('from="Xaga"', row["line"])
        # with its engage first, the same switch and wipe are an attempt's
        log = Log().start(at(0))
        log.add(at(1), 'REALM_EXPEDITION_EARLY_PULL region=191 target="Xaga" near=67 decision=Hold health=100')
        log.add(at(2), switch)
        log.add(at(8), wipe.format(t="Beatha"), "WRN")
        log.add(at(9), 'REALM_EXPEDITION_FINAL_ENGAGE region=191 target="Aroon the Urlamhai" mustered=90 needed=86')
        log.add(at(10), wipe.format(t="Aroon the Urlamhai"), "WRN")
        self.assertEqual(check(only(log.run()[1]), "pull_70")["status"], pcl.SEEN)
        # an engage in an earlier raid in the region does not count for the next one
        log = Log().start(at(0))
        log.add(at(1), 'REALM_EXPEDITION_FINAL_ENGAGE region=191 target="Xaga" mustered=90 needed=86')
        log.add(at(5), "REALM_RAID_STARTED event=epic-hibernia present=86 required=80")
        log.add(at(6), switch)
        self.assertEqual(check(only(log.run()[1]), "pull_70")["status"], pcl.CONTRADICTED)

    # Sweep 6b (b1b6846): Fornfrusenen's retry at 09:15:44 raised no shards and died to plain melee.
    RETRY = ('REALM_EXPEDITION_FINAL_ENGAGE region=160 target="Boss" mustered=89 needed=87 living=124 attempt=2 '
             'retry=True')

    def _first_fight(self, log, adds_line):
        log.add(at(1), 'REALM_EXPEDITION_FINAL_ENGAGE region=160 target="Boss" mustered=79 needed=70 living=100 attempt=1 '
                       'retry=False')
        log.add(at(2), adds_line)
        log.add(at(9), 'REALM_EXPEDITION_WIPE region=160 target="Boss" level=75 failures=1 of=2 engaged=99 fallen=86 '
                       'health=49 lowest=49 how="left combat"', "WRN")
        return log

    def test_a_retry_with_no_adds_contradicts(self):
        lore = 'LORE_LINE boss="Boss" kind=screen trigger=addsSpawn text="Shards of living ice break from the walls!"'
        log = self._first_fight(Log().start(at(0)), lore)
        log.add(at(20), self.RETRY)
        log.add(at(21), 'EPIC_TACTIC_BOT bot=A region=160 target="Boss" action=Reposition to="Boss" reason="close in"')
        log.add(at(22), 'REALM_EXPEDITION_TARGET_DEFEATED region=160 target="Boss" level=75 minutes=27.7')
        row = check(only(log.run()[1]), "retry_adds")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("retry=True", row["line"])
        self.assertIn("0 retry(ies) of a boss with adds showed its adds, 1 showed none", row["detail"])

    def test_a_retry_whose_adds_come_back_is_seen(self):
        retarget = ('EPIC_TACTIC_BOT bot=A region=160 target="Boss" action=Retarget to="Boss Shard" '
                    'reason="shards first (3 left)"')
        for again in (retarget, 'EPIC_FIGHT_LEDGER region=160 target="Boss" seconds=60 raid_dealt="Boss Shard:5000/9/3"'):
            log = self._first_fight(Log().start(at(0)), retarget)
            log.add(at(20), self.RETRY)
            log.add(at(21), again)
            log.add(at(22), 'REALM_EXPEDITION_TARGET_DEFEATED region=160 target="Boss" level=75 minutes=27.7')
            self.assertEqual(check(only(log.run()[1]), "retry_adds")["status"], pcl.SEEN, again)

    def test_a_retry_of_a_boss_without_adds_is_not_judged(self):
        log = self._first_fight(Log().start(at(0)), 'EPIC_TACTIC_BOT bot=A region=160 target="Boss" action=Reposition to="Boss"')
        log.add(at(20), self.RETRY)
        log.add(at(22), 'REALM_EXPEDITION_TARGET_DEFEATED region=160 target="Boss" level=75 minutes=10.0')
        self.assertEqual(check(only(log.run()[1]), "retry_adds")["status"], pcl.NOT_SEEN)

    def test_an_attempt_that_never_entered_combat_with_ten_fallen_contradicts(self):
        # Aroon on b1b6846: three attempts ended between his Scaiths (fallen 14, 35, 19).
        ended = ('REALM_EXPEDITION_ATTEMPT_ENDED region=191 target="Aroon the Urlamhai" engaged={e} fallen={f} health=100 '
                 'lowest=100 how="{how}" wipe=False')
        log = Log().start(at(0))
        log.add(at(5), ended.format(e=80, f=0, how="never entered combat"))
        log.add(at(6), ended.format(e=90, f=40, how="left combat"))
        row = check(only(log.run()[1]), "attempt_contact")
        self.assertEqual(row["status"], pcl.SEEN, "no one fell, or the raid was in combat")
        log.add(at(7), ended.format(e=97, f=14, how="never entered combat"))
        row = check(only(log.run()[1]), "attempt_contact")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("fallen=14", row["line"])
        self.assertIn('3 attempt end(s), 1 "never entered combat" with 10+ fallen', row["detail"])
        log = Log().start(at(0)).add(at(5), ended.format(e=97, f=9, how="never entered combat"))
        self.assertEqual(check(only(log.run()[1]), "attempt_contact")["status"], pcl.SEEN, "nine fallen is under the bar")


class Rvr(unittest.TestCase):
    REGROUP = ('RVR_REGROUP phase=start group={g} realm=Hibernia region=1 reason="{r}" skill=Average members=4 '
               'relayedTo={to} relay={relay} minSeconds=15')
    RETREAT = ('RVR_RETREAT group={g} realm=Hibernia region=1 reason="{r}" odds=0.3 skill=Average runners=4 '
               'relayedTo={to} relay={relay} relayed={relayed}')

    def stance_log(self, own_regroups=10):
        """Own regroups and an own retreat relayed one hop (allowed by #304), received once by another group."""
        log = Log().start(at(0))
        for i in range(own_regroups):
            log.add(at(1, i), self.REGROUP.format(g=100 + i, r="after the fight", to=0, relay="window"))
        log.add(at(2), self.RETREAT.format(g=1, r="in contact: fight lost", to=2, relay="sent", relayed="False"))
        log.add(at(2, 8), self.REGROUP.format(g=2, r="relayed retreat", to=0, relay="none"))
        log.add(at(2, 30), self.REGROUP.format(g=1, r="retreat over", to=3, relay="sent"))
        return log

    def regroup(self, log):
        return check(only(log.run()[1]), "regroup")

    def test_one_hop_is_seen_with_the_rates(self):
        row = self.regroup(self.stance_log())
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("12 regroup starts", row["detail"])
        self.assertIn("1 started by a relay (8.3%", row["detail"])
        self.assertIn("(a) relayed stance relayed again: 0", row["detail"])
        self.assertIn("(b) regroup ending a relayed retreat relayed: 0", row["detail"])
        self.assertIn("(c) relay back at its sender within 60 s: not judged", row["detail"])
        self.assertIn("(c) relay back at its sender within 60 s: 0 (exact, from= on 1",
                      self.regroup(self.named(self.stance_log()))["detail"])
        self.assertEqual(self.regroup(Log().start(at(0)))["status"], pcl.NOT_SEEN)

    def test_a_relayed_stance_that_relays_again(self):
        log = self.stance_log()
        log.add(at(3), self.RETREAT.format(g=5, r="relayed retreat", to=2, relay="sent", relayed="True"))
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("(a) relayed stance relayed again: 1", row["detail"])
        self.assertIn("group=5", row["line"])

    def test_b_the_regroup_ending_a_relayed_retreat_relays(self):
        log = self.stance_log()
        log.add(at(4), self.RETREAT.format(g=7, r="relayed retreat", to=0, relay="none", relayed="True"))
        log.add(at(4, 20), self.REGROUP.format(g=7, r="retreat over", to=0, relay="sent"))
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("(b) regroup ending a relayed retreat relayed: 1", row["detail"])
        self.assertIn("group=7", row["line"])
        # the same group's own retreat first, then its regroup relaying: one hop, allowed
        log = self.stance_log()
        log.add(at(4), self.RETREAT.format(g=7, r="relayed retreat", to=0, relay="none", relayed="True"))
        log.add(at(5), self.RETREAT.format(g=7, r="in contact: bad odds", to=0, relay="sent", relayed="False"))
        log.add(at(5, 20), self.REGROUP.format(g=7, r="retreat over", to=2, relay="sent"))
        self.assertIn("(b) regroup ending a relayed retreat relayed: 0", self.regroup(log)["detail"])

    def named(self, log):
        """The stance log with from= on its relayed start (builds after DIAG-5378-misc 1)."""
        log.lines = [line.replace('reason="relayed retreat" skill=Average members=4 relayedTo=0 relay=none minSeconds=15',
                                  'reason="relayed retreat" skill=Average members=4 relayedTo=0 relay=none minSeconds=15 '
                                  'from=1') for line in log.lines]
        return log

    def test_c_a_relay_back_at_its_sender_within_60_s(self):
        # 4891 -> 5219 -> 4891, as at 12:36-12:37 on 453e01a, with the sender logged (from=)
        log = self.named(self.stance_log())
        log.add(at(5, 0), self.RETREAT.format(g=4891, r="in contact: fight lost", to=3, relay="sent", relayed="False"))
        log.add(at(5, 9), self.RETREAT.format(g=5219, r="relayed retreat", to=0, relay="none", relayed="True") + " from=4891")
        log.add(at(5, 30), self.REGROUP.format(g=5219, r="retreat over", to=4, relay="sent"))
        log.add(at(5, 37), self.REGROUP.format(g=4891, r="relayed regroup", to=0, relay="none") + " from=5219")
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("(c) relay back at its sender within 60 s: 1 (exact, from= on 3 relayed start(s))", row["detail"])
        self.assertIn("(b) regroup ending a relayed retreat relayed: 1", row["detail"])
        # an echo needs a second hop ((a) or (b)), so (c) always comes with one of them
        # too late: 65 s after the sender passed it on
        log = self.named(self.stance_log())
        log.add(at(5, 0), self.RETREAT.format(g=4891, r="in contact: fight lost", to=3, relay="sent", relayed="False"))
        log.add(at(5, 1), self.RETREAT.format(g=5219, r="relayed retreat", to=0, relay="none", relayed="True") + " from=4891")
        log.add(at(5, 2), self.REGROUP.format(g=5219, r="retreat over", to=4, relay="sent"))
        log.add(at(6, 7), self.REGROUP.format(g=4891, r="relayed regroup", to=0, relay="none") + " from=5219")
        self.assertIn("(c) relay back at its sender within 60 s: 0", self.regroup(log)["detail"])

    def test_c_relays_among_the_groups_of_one_fight_are_no_echo(self):
        # 4308 and 4093 on 5378dbe: each sent its OWN retreat in one fight, and each got the other's. The old pattern
        # counted that as "back at its sender" (86 times); with from= it is exact and 0.
        log = self.named(self.stance_log())
        log.add(at(5, 0), self.RETREAT.format(g=4308, r="in contact: bad odds", to=2, relay="sent", relayed="False"))
        log.add(at(5, 3), self.RETREAT.format(g=4093, r="in contact: fight lost", to=2, relay="sent", relayed="False"))
        log.add(at(5, 9), self.REGROUP.format(g=4308, r="relayed retreat", to=0, relay="none") + " from=4093")
        row = self.regroup(log)
        self.assertIn("(c) relay back at its sender within 60 s: 0 (exact", row["detail"])
        # a build without from=: not judged, the pattern is reported and does not contradict
        log = self.stance_log(own_regroups=30)
        log.add(at(5, 0), self.RETREAT.format(g=4308, r="in contact: bad odds", to=2, relay="sent", relayed="False"))
        log.add(at(5, 3), self.RETREAT.format(g=4093, r="in contact: fight lost", to=2, relay="sent", relayed="False"))
        log.add(at(5, 9), self.REGROUP.format(g=4308, r="relayed retreat", to=0, relay="none"))
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("(c) relay back at its sender within 60 s: not judged", row["detail"])
        self.assertIn("relays among the groups of one fight: 1", row["detail"])

    def test_relayed_regroups_ended_by_contact_at_once_are_counted(self):
        # group 4308 on 5378dbe: a relayed regroup in contact, ended a second later (123 of them)
        log = self.stance_log(own_regroups=30)
        log.add(at(3), 'RVR_REGROUP phase=end group=2 region=1 next=Kite reason="contact while regrouping: odds below fair" '
                       'started="relayed retreat" seconds=1 members=4 health=90 power=80')
        log.add(at(3), 'RVR_REGROUP phase=end group=3 region=1 next=Engage reason="regrouped" '
                       'started="relayed retreat" seconds=40 members=4 health=90 power=80')
        log.add(at(3), 'RVR_REGROUP phase=end group=4 region=1 next=Kite reason="contact while regrouping: odds below fair" '
                       'started="retreat over" seconds=0 members=4 health=90 power=80')
        row = self.regroup(log)
        self.assertIn("relayed regroups ended by contact within 1 s: 1", row["detail"])
        self.assertIn("32 regroup starts", row["detail"], "an end line is not a start")

    def test_d_relay_started_regroups_over_the_bar(self):
        # Owner decision 2026-11-18 18:19: the bar is 15% (it was 11.5%; dense warbands fan one hop out to 12-14%).
        self.assertEqual(pcl.RELAY_SHARE_BAR, 15.0)
        log = self.stance_log(own_regroups=3)
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("1 started by a relay (20.0%, at most 15%)", row["detail"])
        self.assertIn('reason="relayed retreat"', row["line"])
        self.assertIn("relay-started regroups 15% of starts or less", row["row"])
        # 1 of 6 (16.7%) is over the bar
        self.assertEqual(self.regroup(self.stance_log(own_regroups=4))["status"], pcl.CONTRADICTED)
        # 1 of 8 (12.5%) failed the old 11.5% bar and passes the owner's 15%
        self.assertEqual(self.regroup(self.stance_log(own_regroups=6))["status"], pcl.SEEN)

    def test_d_a_share_between_ten_and_eleven_and_a_half_percent_passes(self):
        # 1 of 9 (11.1%): failed the old 10% bar, under the owner's 15%
        row = self.regroup(self.stance_log(own_regroups=7))
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("1 started by a relay (11.1%, at most 15%)", row["detail"])

    def test_field_rows(self):
        log = Log().start(at(0))
        log.add(at(1), 'RVR_NOTICE bot=A id=1 realm=Midgard target="B" kind=EnemyBot distance=631 delayMs=967')
        log.add(at(1), 'RVR_SPOT bot=A id=1 target="B" kind=EnemyBot call="inc 1 se"')
        log.add(at(2), 'RVR_BOT_KILLED victim=B id=2 realm=Hibernia killer="A" killerRealm=Midgard')
        log.add(at(3), 'REALM_RAID_SCHEDULED event=x realm=Albion online=153 required=80 nextMinutes=240')
        log.add(at(4), 'RVR_ROAM_HUNT group=g realm=Midgard prey="C" seen=1')
        log.add(at(5), 'RVR_ROAM_MOVE_ON group=g realm=Midgard kills=1')
        log.add(at(6), 'RVR_LANE_ASSIGN force="rvr-keep-52" group="g" lane=0')
        log.add(at(7), 'RVR_SIEGE_STARTED target=rvr-keep-52 reason="the marching army reached the keep and attacks"')
        log.add(at(8), 'RVR_KEEP_TAKEN keep="Caer Erasleigh" id=52 region=1 realm=Midgard battleground=False')
        log.add(at(9), 'RVR_CHECKSPOT_LOADED ok=True version=1 spots=98 circuits=18 parseRejected=0')
        log.add(at(9), 'RVR_CHECKSPOT_VALIDATED ok=True walked=400 failed=0')
        log.add(at(9), 'RVR_LANE_LOADED lanes=332 trips=149 validated=318 rejected=14')
        log.add(at(10), 'RVR_KITE group=g bot=A')
        sec = only(log.run()[1])
        for cid in ("notice", "frontier", "roam", "tactics", "lanes", "sieges", "catalogs"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertIn("Albion 153", check(sec, "frontier")["detail"])
        self.assertIn("median delayMs 967 ms", check(sec, "notice")["detail"])
        log.add(at(11), 'RVR_CHECKSPOT_VALIDATED ok=True walked=400 failed=2')
        self.assertEqual(check(only(log.run()[1]), "catalogs")["status"], pcl.CONTRADICTED)


class Crafting(unittest.TestCase):
    def test_stations_orders_and_gear(self):
        log = Log().start(at(0))
        log.add(at(1), 'CRAFT_SESSION_START bot="Szedogg" id=2747 craft="Fletching" station=481')
        log.add(at(1), 'CRAFT_SESSION_END bot="Szedogg" id=2747 attempts=1 made=3 reason="Finished at the station; made 3"')
        for i in range(15):
            log.add(at(2), f'ECON_ORDER outcome="made" source="quartermaster" order="{i}" item="x"')
        for i in range(10):
            log.add(at(3), f'ECON_ORDER outcome="expired" source="quartermaster" order="e{i}" item="x"')
        log.add(at(4), 'ECON_UNDRESS_GUARD event="hour" checked=448 blocked=0 blocked_since_start=0')
        log.add(at(5), 'ECON_SUPPLY hour=2026-11-17T05:00:00Z realm=Albion supply=1')
        log.add(at(5), 'ECON_FLOWS hour=2026-11-17T05:00:00Z realm=Albion exchange_sales=80 exchange_crafted=2')
        log.add(at(5), 'ECON_GOVERNOR realm=Albion ratio=0.08 smoothed=0.09 target=0.5')
        sec = only(log.run()[1])
        for cid in ("stations", "orders", "no_strip", "economy", "exchange"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertIn("3 pieces made", check(sec, "stations")["detail"])
        self.assertIn("60% of made+expired", check(sec, "orders")["detail"])
        log.add(at(6), 'ECON_UNDRESS_GUARD event="hour" checked=100 blocked=2 blocked_since_start=2', "WRN")
        for i in range(10):
            log.add(at(7), f'ECON_ORDER outcome="expired" source="quartermaster" order="f{i}" item="x"')
        sec = only(log.run()[1])
        self.assertEqual(check(sec, "no_strip")["status"], pcl.CONTRADICTED)
        self.assertEqual(check(sec, "orders")["status"], pcl.CONTRADICTED)


class Companions(unittest.TestCase):
    def test_hire_to_login_and_player_names_masked(self):
        log = Log().start(at(0))
        log.add(at(1), 'HIRE_WORD_165 player=Playerone word=hire args="" from=typed', logger="DOL.GS.Commands.HireText165")
        log.add(at(1), 'HIRED_COMPANION owner="Playerone" bot=5836 "Pannax" Briton Armsman cost=0')
        log.add(at(2), 'HIRED_COMPANION_REVIVED owner="Playerone" bot=5836 "Pannax" body=faded')
        log.add(at(3), 'HIRED_COMPANION_LOGOUT owner="Playerone" parked=1')
        log.add(at(4), 'HIRED_COMPANION_LOGIN owner="Playerone" summoning=1')
        code, result = log.run()
        sec = only(result)
        for cid in ("hire_word", "hired", "revived", "parked"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertNotIn("Playerone", json.dumps(result))
        self.assertEqual(pcl.quote('RVR_NOTICE bot=A target="Playerone" kind=Player distance=500'),
                         "RVR_NOTICE bot=A target=<name> kind=Player distance=500")
        self.assertIn('"Pannax"', check(sec, "hired")["line"])
        log.add(at(5), "HIRED_COMPANION_DELETE_FAILED bot=5836: its record stays, retired", "ERR")
        self.assertEqual(check(only(log.run()[1]), "hired")["status"], pcl.CONTRADICTED)


class Install(unittest.TestCase):
    def test_class_off_session(self):
        log = Log().start(at(0), edition="off")
        log.add(at(5), "CUSTOM_CLASS_REFUSED account=someacct name=Somechar class=62 race=Celt "
                       "(classes/enable_sluaghbinder is off)", "WRN")
        log.add(at(40), "REALM_RAID_STARTED event=dragon-albion present=70 required=70 forced=True")
        code, result = log.run()
        sec = only(result)
        self.assertEqual(sec["sluaghbinder"], "off")
        for cid in ("fork_build", "startup", "classoff_flag", "custom_class_refused", "classoff_raid", "classoff_errors"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        text = json.dumps(result)
        self.assertNotIn("someacct", text)
        self.assertNotIn("Somechar", text)
        log.add(at(41), "Trainer Muirenn: no Sluaghbinder specialization", "ERR", "DOL.GS.Trainer")
        self.assertEqual(check(only(log.run()[1]), "classoff_errors")["status"], pcl.CONTRADICTED)

    def test_class_on_world_is_not_the_class_off_row(self):
        sec = only(Log().start(at(0)).run()[1])
        self.assertEqual(check(sec, "classoff_flag")["status"], pcl.NOT_SEEN)
        self.assertNotIn("classoff_raid", [c["id"] for c in sec["checks"]])


class EraBuilds(unittest.TestCase):
    def test_era_rows(self):
        log = Log().start(at(0))
        log.add(at(0, 20), 'ERA_BUILDS_LOADED version=1 status="approved 2026-11-17" classes=39 builds=106 dropped=0 '
                           'offMeta=10% jitter=2 customClass=never loaded=yes classShares=on')
        for i in range(9):
            log.add(at(1), f'ERA_BUILD_ASSIGNED bot="B{i}" id={i} class=Sorcerer level=50 build=sorc-mind-body draw=era')
        log.add(at(1), 'ERA_BUILD_ASSIGNED bot="B9" id=9 class=Sorcerer level=50 build=sorc-body draw=offmeta')
        log.add(at(2), 'ERA_BUILD_TRAINED bot="B0" id=0 build=sorc-mind-body level=50 specs="Mind Magic:44" match=yes')
        log.add(at(3), 'ERA_BUILD_CENSUS realm=Hibernia bots=900 era=120 kept=780 Druid=139(15.4%/15%) Sluaghbinder=0')
        log.add(at(3), 'BOT_CUSTOM_CLASS_RETIRED bot="Oldbinder" id=77 class=Sluaghbinder level=50 realm=Hibernia')
        sec = only(log.run()[1])
        for cid in ("loaded", "assigned", "trained", "census", "retired"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertIn("10% offmeta", check(sec, "assigned")["detail"])
        log.add(at(4), 'ERA_BUILD_CENSUS realm=Hibernia bots=900 era=120 kept=780 Sluaghbinder=3(0.3%/0%)')
        log.add(at(4), 'ERA_BUILD_TRAINED bot="B1" id=1 build=sorc-mind-body level=50 match=no')
        sec = only(log.run()[1])
        self.assertEqual(check(sec, "census")["status"], pcl.CONTRADICTED, "3 counted, 1 retired")
        self.assertIn("Sluaghbinder=3", check(sec, "census")["line"])
        self.assertIn("Sluaghbinder counted 3, retired 1", check(sec, "census")["detail"])
        self.assertEqual(check(sec, "trained")["status"], pcl.CONTRADICTED)

    def test_census_against_the_retired(self):
        # 5378dbe: Sluaghbinder=79 in Hibernia's census and 79 BOT_CUSTOM_CLASS_RETIRED, none of them online
        retired = ('BOT_CUSTOM_CLASS_RETIRED bot="Bind{i}" id={i} class=Sluaghbinder level=50 realm=Hibernia: kept with '
                   'its items, never logged in (bots never play the custom class)')
        log = Log().start(at(0))
        log.add(at(0, 17), 'ERA_BUILD_CENSUS realm=Hibernia bots=1092 era=0 kept=1092 new=0 Sluaghbinder=3(0.3%/0%)')
        for i in range(70, 73):
            log.add(at(0, 17), retired.format(i=i))
        log.add(at(1), 'AUTONOMOUS_EXCHANGE_SOLD seller="Bind70" sellerId=70 item="x"')    # an old listing sold
        log.add(at(1), 'AUTONOMOUS_BOT_DEATH bot="Kyle" id=999 level=50 class="Healer" killer="x"')  # a namesake
        sec = only(log.run()[1])
        row = check(sec, "census")
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("Sluaghbinder counted 3, retired 3; 0 line(s) of a retired one playing", row["detail"])
        log.add(at(2), 'AUTONOMOUS_BOT_DEATH bot="Bind71" id=71 level=50 class="Sluaghbinder" killer="x"')
        row = check(only(log.run()[1]), "census")
        self.assertEqual(row["status"], pcl.CONTRADICTED, "a retired one acts")
        self.assertIn('bot="Bind71" id=71', row["line"])

    def test_buffbot_copies_retired_and_not_played(self):
        # Owner decision 2026-11-18: the copies an earlier build saved (5876 "Eilriorrin" on 5378dbe) are retired once
        copy = ('BOT_BUFFBOT_COPY_RETIRED bot="Eilriorrin" id=5876 class=Druid level=50 realm=Hibernia main="Ashfury" '
                'mainId=5147: a buffbot an earlier build saved as a gamebot; kept with its items, never logged in')
        log = Log().start(at(0))
        self.assertEqual(check(only(log.run()[1]), "buffbot_copies")["status"], pcl.NOT_SEEN)
        log.add(at(0, 17), copy)
        log.add(at(0, 17), 'BOT_BUFFBOT_COPIES retired=1 found=1 suspected=0 mains=212')
        # the buffbot itself, with its own id, is not the copy
        log.add(at(1), 'BUFFBOT_LOGOUT buffbot="Eilriorrin" id=1800005147 main="Ashfury" reason="main logged out"')
        row = check(only(log.run()[1]), "buffbot_copies")
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("retired=1 found=1 suspected=0 mains=212; 1 retired, 0 suspected", row["detail"])
        log.add(at(2), 'ERA_BUILD_ASSIGNED bot="Eilriorrin" id=5876 class=Druid level=50 build=druid-regrow-nurt draw=nearest')
        row = check(only(log.run()[1]), "buffbot_copies")
        self.assertEqual(row["status"], pcl.CONTRADICTED, "a retired copy plays")
        self.assertIn('id=5876', row["line"])

    def test_trained_is_not_applicable_without_era_draws(self):
        # 5378dbe: 13 ERA_BUILD_ASSIGNED, all draw=nearest at level 50; TRAINED is only for draw=era
        log = Log().start(at(0))
        log.add(at(1), 'ERA_BUILD_ASSIGNED bot="Eilriorrin" id=5876 class=Druid level=50 build=druid-regrow-nurt '
                       'role=healer draw=nearest jitter=""')
        sec = only(log.run()[1])
        row = check(sec, "trained")
        self.assertEqual(row["status"], pcl.NOT_APPLICABLE)
        self.assertIn("0 draw=era assignment(s)", row["detail"])
        log.add(at(2), 'ERA_BUILD_ASSIGNED bot="B1" id=1 class=Sorcerer level=50 build=sorc-body draw=era')
        self.assertEqual(check(only(log.run()[1]), "trained")["status"], pcl.NOT_SEEN, "a draw=era bot: must be seen")
        code = Log().start(at(0)).add(at(1), 'ERA_BUILD_ASSIGNED bot="E" id=5 class=Druid level=50 draw=nearest')
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "server.log"
            path.write_text("\n".join(code.lines) + "\n", encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
                pcl.main([str(path), "--year", "2026"])
        self.assertIn("[Not applicable on build abc1234] must ERA_BUILD_TRAINED match=yes", out.getvalue())


class BotsAsPeople(unittest.TestCase):
    """The fixtures are the lines as the server writes them (the C# format strings of WP0-WP5 and WP2R, 2026-11-17)."""

    def profiles(self, log, casual, average, veteran, grown="average"):
        n = 0
        for tier, count in (("casual", casual), ("average", average), ("veteran", veteran)):
            for _ in range(count):
                n += 1
                # tier= may have grown with play; the shares are judged on start=
                log.add(at(1), f"BOT_SKILL_PROFILE name=Bot{n} person=c:{n} realm=alb level=50 tier={grown} start={tier} "
                               f"place=0.42 reactions=1.20 positioning=1.31 pulls=1.18 fire=1.02 buffs=1.40 power=1.25 "
                               f"xp_h=0.0 awareness=normal")

    @staticmethod
    def mistakes(out=(10, 100), inside=(1, 40)):
        """BOT_SKILL_MISTAKES with every chance kind at these counts (casual column; average and veteran zero)."""
        parts = ["BOT_SKILL_MISTAKES seconds=300"]
        for kind in ("reaction", "position", "pull", "fire", "buff", "power"):
            parts.append(f"out.{kind}=c{out[0]}/{out[1]},a0/0,v0/0")
            parts.append(f"in.{kind}=c{inside[0]}/{inside[1]},a0/0,v0/0")
        parts.append("rate_out=0.100 rate_in=0.025 lines=12 suppressed=0")
        return " ".join(parts)

    def played_session(self):
        log = Log().start(at(0))
        log.add(at(1), "BOT_PEOPLE_SUMMARY seconds=300 skill=on growth=on spare=on raidLearn=on places=on ask=on "
                       "wrongRoads=on movement=on strafe=on stick=on sit=on milestones=on "
                       "rows=skill:120,places:120,milestones:0,mechanics_seen:0 scope: records=500/0 mistakes=900/40 "
                       "way=30/2 movement=200/10 refused=player_group:12,realm_raid:40 logouts=3")
        self.profiles(log, 31, 49, 20)
        log.add(at(2), "BOT_SKILL_GROWTH name=Bot1 person=c:1 realm=alb from=casual to=average start=casual xp_h=41.0")
        log.add(at(2), "BOT_SKILL_MISTAKE kind=pull spared=true name=Bot2 person=c:2 realm=alb tier=casual chance=0.025 "
                       "group=4 add=1")
        log.add(at(3), self.mistakes())
        log.add(at(3), "RAID_LEARN raid=albion-crystal-cave mechanic=Xanxicar/blast record=0.39 seenShare=0.00 "
                       "familiarity=0.29 raiders=60 seen=0 specialForces=0 knowledge=0.00 newcomer=0.29 veteran=0.29 run=1000 health=100")
        log.add(at(5), "RAID_LEARN raid=albion-crystal-cave mechanic=Xanxicar/blast record=0.39 seenShare=0.75 "
                       "familiarity=0.39 raiders=62 seen=46 specialForces=0 knowledge=0.37 newcomer=0.22 veteran=0.48 run=2000 health=100")
        log.add(at(3), 'BOT_PLACE_LEARNED how=seeded bot=Bot3 id=3 realm=Albion level=35 places=24 at="Camelot City"')
        log.add(at(4), 'BOT_PLACE_LEARNED how=visited bot=Bot3 id=3 realm=Albion level=35 place="Llyn Barfog" '
                       'zone="Llyn Barfog"')
        log.add(at(4), 'BOT_WAY_ASK bot=Bot4 id=4 realm=Albion level=30 tier=casual dest="Snowdonia" channel=faction said=said')
        log.add(at(4), 'BOT_WAY_TOLD asker=Bot4 id=4 answerer=Bot5 answererId=5 realm=Albion place="Snowdonia" '
                       'dest="Snowdonia"')
        log.add(at(5), 'BOT_WAY_WRONG_ROAD bot=Bot4 id=4 realm=Albion level=30 tier=casual dest="Snowdonia" '
                       'zone="Black Mountains South" cost_s=95.0 length=1100 look_s=6.0 result=back at=1,2,3')
        log.add(at(5), "BOT_MOVE_HABITS seconds=300 strafeFights=2 strafes=3 faces=1 stickTurns=40 stickLost=1 "
                       "overshoots=2 stepBacks=2 sits=9 wallWalks=1 wallSits=1 wallStands=1 wallGivenUp=0 bankLeans=1 "
                       "refusedLegs=0 busyDrops=0 budgetDrops=0")
        log.add(at(5), 'BOT_MOVE_SIT bot="Bot6" id=6 region=1 where=group waited_s=41')
        log.add(at(5), 'BOT_MOVE_WALL bot="Bot6" id=6 region=1 keep="Caer Benowyc" x=1 y=2 z=3 waited_s=38')
        log.add(at(5), 'BOT_MILESTONE_ARC bot="Bot7" id=7 person=c:7 realm=Albion level=46 class="Armsman" xp=60000000000 '
                       'rr=1L0 rp=0 arc=ding50 state=active progress=5')
        log.add(at(6), 'BOT_MILESTONE_TALK bot="Bot7" id=7 person=c:7 realm=Albion level=48 class="Armsman" xp=115000000000 '
                       'rr=1L0 rp=0 arc=ding50 point=50 topic=ding50_half channel=faction bubbles=0 left_k=514 count=0 tries=1 '
                       'line="48 now, over halfway 2 50"')
        log.add(at(6), 'BOT_MILESTONE_REACHED bot="Bot8" id=8 person=c:8 realm=Albion level=50 class="Cleric" xp=0 '
                       'rr=5L0 rp=513500 arc=rr5 trophy="" source="" dry_kills=0 sought=true announce=yes '
                       'started="2026-11-17T10:00:00.0000000Z"')
        log.add(at(6), 'BOT_MILESTONE_TALK bot="Bot8" id=8 person=c:8 realm=Albion level=50 class="Cleric" xp=0 '
                       'rr=5L0 rp=513500 arc=rr5 point=100 topic=rr5_reached channel=faction bubbles=0 left_k=0 count=0 '
                       'trophy="" source="" line="finally rr5!"')
        log.add(at(6), 'BOT_MILESTONE_TALK bot="Bot10" id=10 person=c:10 realm=Albion level=49 class="Wizard" xp=158400000000 '
                       'rr=1L0 rp=0 arc=ding50 point=90 topic=ding50_bubbles channel=say bubbles=4 left_k=514 count=0 tries=0 '
                       'line="4 bubbles 2 50"')
        log.add(at(6), 'BOT_MILESTONE_TALK bot="Bot11" id=11 person=c:11 realm=Albion level=50 class="Friar" xp=0 '
                       'rr=4L9 rp=500000 arc=rr5 point=90 topic=rr5_close channel=guild bubbles=0 left_k=14 count=0 tries=2 '
                       'line="about 14k rps 2 rr5"')
        log.add(at(6), 'BOT_MILESTONE_TALK bot="Bot12" id=12 person=c:12 realm=Albion level=50 class="Scout" xp=0 '
                       'rr=1L0 rp=0 arc=dragon_trophy point=90 topic=trophy_long channel=faction bubbles=0 left_k=514 count=5 '
                       'tries=0 line="5 raid kills now and still no drop 4 me"')
        log.add(at(6), 'BOT_MILESTONE_TALK bot="Bot13" id=13 person=c:13 realm=Albion level=50 class="Scout" xp=0 '
                       'rr=1L0 rp=0 arc=dragon_trophy point=100 topic=trophy_reached channel=faction bubbles=0 left_k=514 '
                       'count=0 trophy="Ring of 100 Scales" source="Golestandt" line="Ring of 100 Scales off Golestandt!!"')
        return log

    def test_a_milestone_line_that_claims_more_than_the_numbers_is_contradicted(self):
        log = self.played_session()
        log.add(at(7), 'BOT_MILESTONE_TALK bot="Bot9" id=9 person=c:9 realm=Albion level=50 class="Cleric" xp=0 '
                       'rr=4L5 rp=300000 arc=rr5 point=90 topic=rr5_close channel=say count=0 line="almost rr5"')
        row = check(only(log.run()[1]), "milestones")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("false_claims=1", row["detail"])
        claim = pcl.milestone_claim_true
        self.assertTrue(claim({"arc": "ding50", "point": "90", "level": "49", "xp": "158400000000"}))
        self.assertFalse(claim({"arc": "ding50", "point": "90", "level": "49", "xp": "150000000000"}))
        # The line's own numbers: the bubbles and the thousands left are the logged experience's and realm points'.
        bubbles = {"arc": "ding50", "point": "90", "level": "49", "xp": "158400000000", "topic": "ding50_bubbles"}
        self.assertEqual(pcl.milestone_bubbles_left(49, 158_400_000_000), 4)
        self.assertTrue(claim(bubbles | {"bubbles": "4", "line": "4 bubbles 2 50"}))
        self.assertFalse(claim(bubbles | {"bubbles": "2", "line": "2 bubbles to 50"}), "the slot is not the experience's")
        self.assertFalse(claim(bubbles | {"bubbles": "4", "line": "2 bubbles to 50"}), "the line does not say its number")
        close = {"arc": "rr5", "point": "90", "rp": "500000", "topic": "rr5_close"}
        self.assertTrue(claim(close | {"left_k": "14", "line": "about 14k rps to rr5"}))
        self.assertFalse(claim(close | {"left_k": "14", "line": "about 4k rps to rr5"}))
        self.assertFalse(claim(close | {"left_k": "9", "line": "almost rr5"}))
        # The trophy's talk points are big kills without a drop: the 2nd and the 5th; the moment names item and boss.
        trophy = {"arc": "dragon_trophy", "topic": "trophy_try", "line": "no drop off Golestandt for me again"}
        self.assertTrue(claim(trophy | {"point": "50", "count": "2"}))
        self.assertFalse(claim(trophy | {"point": "50", "count": "1"}))
        self.assertFalse(claim(trophy | {"point": "90", "count": "4"}))
        self.assertTrue(claim(trophy | {"point": "90", "count": "5", "topic": "trophy_long", "line": "5 big kills, zero drops"}))
        self.assertFalse(claim(trophy | {"point": "90", "count": "5", "topic": "trophy_long", "line": "7 big kills, zero drops"}))
        self.assertFalse(claim({"arc": "dragon_trophy", "point": "100", "trophy": "", "source": "Golestandt"}))
        self.assertTrue(claim({"arc": "dragon_trophy", "point": "100", "trophy": "Ring", "source": "Golestandt"}))

    def test_people_rows_from_the_lines_the_server_writes(self):
        log = self.played_session()
        sec = only(log.run()[1])
        for cid in ("summary", "skill_profile", "skill_growth", "mistakes", "raid_learn", "places", "way", "movement"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertEqual(check(sec, "milestones")["status"], pcl.SEEN)
        self.assertIn("talk=6", check(sec, "milestones")["detail"])
        self.assertIn("100 profiles: 31/49/20", check(sec, "skill_profile")["detail"])
        self.assertIn("in/out=0.25", check(sec, "mistakes")["detail"])
        self.assertIn("rose between runs in albion-crystal-cave", check(sec, "raid_learn")["detail"])
        self.assertIn("sits=9", check(sec, "movement")["detail"])
        log.add(at(7), 'BOT_WAY_WRONG_ROAD bot=Bot8 id=8 realm=Albion level=30 tier=casual dest="x" zone="y" cost_s=240.5 '
                       'length=900 look_s=5.0 result=back at=1,2,3')
        way = check(only(log.run()[1]), "way")
        self.assertEqual(way["status"], pcl.CONTRADICTED)
        self.assertIn("cost_s=240.5", way["line"])

    def test_mistakes_in_the_players_group_at_the_strangers_rate_contradict(self):
        log = Log().start(at(0))
        log.add(at(1), "BOT_SKILL_MISTAKE kind=buff spared=true name=Bot2 person=c:2 realm=alb tier=casual chance=0.062")
        log.add(at(2), self.mistakes(out=(10, 100), inside=(9, 100)))
        self.assertEqual(check(only(log.run()[1]), "mistakes")["status"], pcl.CONTRADICTED)
        log = Log().start(at(0))
        log.add(at(2), self.mistakes(out=(10, 100), inside=(0, 5)))
        mistakes = check(only(log.run()[1]), "mistakes")
        self.assertEqual(mistakes["status"], pcl.NOT_SEEN)
        self.assertIn("too few decisions", mistakes["detail"])
        log = Log().start(at(0))
        log.add(at(1), "BOT_SKILL_MISTAKE kind=buff spared=true name=Bot2 person=c:2 realm=alb tier=casual chance=0.062")
        log.add(at(2), self.mistakes(out=(10, 100), inside=(1, 500)))
        mistakes = check(only(log.run()[1]), "mistakes")
        self.assertEqual(mistakes["status"], pcl.NOT_SEEN, "in/out 0.02 is not about a quarter")
        self.assertIn("below 0.1", mistakes["detail"])

    def test_movement_needs_sits_stick_losses_and_overshoots(self):
        log = Log().start(at(0))
        log.add(at(1), "BOT_MOVE_HABITS seconds=300 strafes=3 stickLost=0 overshoots=2 sits=9")
        log.add(at(2), 'BOT_MOVE_SIT bot="Bot6" id=6 region=1 where=group waited_s=41')
        self.assertEqual(check(only(log.run()[1]), "movement")["status"], pcl.NOT_SEEN)
        log.add(at(6), "BOT_MOVE_HABITS seconds=300 strafes=0 stickLost=1 overshoots=0 sits=2")
        self.assertEqual(check(only(log.run()[1]), "movement")["status"], pcl.SEEN)

    @staticmethod
    def learn(share, run=None, raiders=60):
        return (f"RAID_LEARN raid=crystal-cave mechanic=Xanxicar/blast record=0.00 seenShare={share:.2f} familiarity=0.00 "
                f"raiders={raiders} seen=0 specialForces=0 knowledge=0.00 newcomer=0.00 veteran=0.00 "
                + (f"run={run} " if run is not None else "") + "health=100")

    def test_a_rise_inside_one_run_is_not_a_rise_between_runs(self):
        log = Log().start(at(0))
        log.add(at(1), self.learn(0.00, run=1000))
        log.add(at(2), self.learn(0.40, run=1000, raiders=70))  # late veterans and Special Forces join run 1
        learn = check(only(log.run()[1]), "raid_learn")
        self.assertEqual(learn["status"], pcl.NOT_SEEN)
        self.assertIn("in 1 run(s)", learn["detail"])
        log.add(at(30), self.learn(0.35, run=2000))
        self.assertEqual(check(only(log.run()[1]), "raid_learn")["status"], pcl.SEEN, "run 2 starts above run 1's start")

    def test_lines_without_a_run_are_split_at_the_raid_starts(self):
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_STARTED event=crystal-cave present=60 required=40 forced=true")
        log.add(at(2), self.learn(0.00))
        log.add(at(3), self.learn(0.50, raiders=70))
        self.assertEqual(check(only(log.run()[1]), "raid_learn")["status"], pcl.NOT_SEEN)
        log.add(at(20), 'REALM_RAID_ENDED event=crystal-cave outcome="Boss defeated" minutes=19 named=1')
        log.add(at(21), "REALM_RAID_STARTED event=crystal-cave present=60 required=40 forced=true")
        log.add(at(22), self.learn(0.70))
        self.assertEqual(check(only(log.run()[1]), "raid_learn")["status"], pcl.SEEN)

    def test_raid_learning_without_a_rise_is_not_seen(self):
        log = Log().start(at(0))
        log.add(at(3), "RAID_LEARN raid=albion-crystal-cave mechanic=Xanxicar/blast record=0.39 seenShare=0.40 "
                       "familiarity=0.33 raiders=60 seen=24 specialForces=0 knowledge=0.20 newcomer=0.29 veteran=0.40 health=100")
        self.assertEqual(check(only(log.run()[1]), "raid_learn")["status"], pcl.NOT_SEEN)

    def test_skill_shares_off_the_mark(self):
        log = Log().start(at(0))
        self.profiles(log, 60, 30, 10)
        self.assertEqual(check(only(log.run()[1]), "skill_profile")["status"], pcl.CONTRADICTED)
        log = Log().start(at(0))
        self.profiles(log, 6, 3, 1)
        self.assertEqual(check(only(log.run()[1]), "skill_profile")["status"], pcl.NOT_SEEN)


class StallsAndErrors(unittest.TestCase):
    def stall(self, log, t, ms, gc=50):
        log.add(t, f'SERVER_STALL worstTickMs={ms} stage=NpcService gcPauseMs={gc} likely="x"', "WRN",
                "DOL.GS.GameLoopWorkMetrics")

    def test_an_eight_hour_run_under_the_threshold_is_seen(self):
        log = Log().start(at(0))
        self.stall(log, at(60), 1050)
        log.add(at(60, 30), "AI_PROFILE tick_p50=0.3 p95=7 p99=20 max=1100.5 serial_ms=1", logger="DOL.GS.AiProfile")
        self.stall(log, at(120), 300, gc=1200)    # GC-only minute: not a stall
        log.stop(at(8 * 60 + 5))
        sec = only(log.run()[1])
        row = check(sec, "overnight")
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("1 stall(s) of 1.0 s or more, 0 of 2.0 s", row["detail"])

    def test_a_two_second_stall_or_six_in_an_hour_contradicts(self):
        log = Log().start(at(0))
        self.stall(log, at(30), 2400)
        log.stop(at(9 * 60))
        row = check(only(log.run()[1]), "overnight")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("worstTickMs=2400", row["line"])
        log = Log().start(at(0))
        for m in range(6):
            self.stall(log, at(10 + 5 * m), 1200)
        log.stop(at(9 * 60))
        row = check(only(log.run()[1]), "overnight")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("at most 6 in an hour", row["detail"])

    def test_each_minute_of_a_stalling_hour_counts_once(self):
        # b1b6846 on the owner's PC: SERVER_STALL at mm:03 and AI_PROFILE at mm:04, every minute for 13 minutes. The next
        # minute's SERVER_STALL is 59 s after this minute's AI_PROFILE: it is a stall of its own, not a third line.
        # The 06:35-06:47 values of that log (worstTickMs, then AI_PROFILE max): a minute longer than the last one's AI line
        # was merged into it.
        minutes = [(1290, 1294.81), (1241, 1241.83), (1654, 1657.88), (1356, 1356.74), (1515, 1519.31), (1431, 1432.06),
                   (1411, 1415.43), (1798, 1799.28), (1501, 1512.84), (1701, 1702.40), (1579, 1580.66), (1291, 1293.18),
                   (1078, 1079.70)]
        log = Log().start(at(0))
        for m, (ss, ai) in enumerate(minutes):
            self.stall(log, at(2 + m, 3), ss)
            log.add(at(2 + m, 4), f"AI_PROFILE tick_p50=0.66 p95=8.85 p99=30.48 max={ai} serial_ms=1.179",
                    logger="DOL.GS.AiProfile")
        self.stall(log, at(90, 4), 1091)
        log.stop(at(9 * 60))
        row = check(only(log.run()[1]), "overnight")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("14 stall(s) of 1.0 s or more", row["detail"])
        self.assertIn("at most 13 in an hour", row["detail"])

    def test_a_short_run_is_not_seen(self):
        log = Log().start(at(0))
        self.stall(log, at(30), 1100)
        log.stop(at(4 * 60 + 42))
        row = check(only(log.run()[1]), "overnight")
        self.assertEqual(row["status"], pcl.NOT_SEEN)
        self.assertIn("4 h 41 min", row["detail"])
        self.assertIn("shorter than 8 hours", row["detail"])

    def test_errors(self):
        log = Log().start(at(0))
        log.add(at(5), "No npctemplate with ID 99 found.", "ERR", "DOL.GS.NpcTemplateMgr")
        log.add(at(6), "QueuePacket failed when adding to _tcpPacketQueue.", "ERR", "DOL.GS.PacketHandler.PacketProcessor")
        log.add(at(7), "Critical error encountered in NpcService: System.NullReferenceException", "ERR",
                "DOL.GS.GameServiceUtils", "   at Something()", "No other action performed")
        sec = only(log.run()[1])
        row = check(sec, "stop_play")
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("2 ERR after startup", row["detail"])
        self.assertIn("2 known start-up", row["detail"])
        self.assertIn("1 other start-up ERR", row["detail"])
        log.add(at(8), "Critical error encountered in NpcService: System.NullReferenceException", "ERR",
                "DOL.GS.GameServiceUtils", "   at Something()", "Calling KickPlayerToCharScreen")
        self.assertEqual(check(only(log.run()[1]), "stop_play")["status"], pcl.CONTRADICTED)
        log.add(at(9), "Critical error encountered in GameLoop", "FTL", "DOL.GS.GameLoop")
        row = check(only(log.run()[1]), "stop_play")
        self.assertEqual(row["status"], pcl.CONTRADICTED)


class ChatPromises(unittest.TestCase):
    """"omw" made true: CHAT_PROMISE made / kept / broken, as ChatPromiseBook writes them."""

    @staticmethod
    def made(n, minute):
        return (f'CHAT_PROMISE made bot="Bot{n}" id={n} promise={n} realm=Albion reason=Alarm mode=RvrObjective '
                f'target="Caer Benowyc" kind=Keep region=1 eta=60s deadline=210s asker="x"', at(minute))

    @staticmethod
    def ended(n, minute, why=None):
        head = f'CHAT_PROMISE {"kept" if why is None else "broken"} bot="Bot{n}" id={n} promise={n} reason=Alarm ' \
               f'mode=RvrObjective target="Caer Benowyc"'
        return (head + (" after=95s eta=60s" if why is None else f" why={why} after=210s deadline=210s"), at(minute, 30))

    def session(self, kept, timeouts, died=0):
        log = Log().start(at(0))
        n = 0
        for whys in ([None] * kept, ["timeout"] * timeouts, ["died"] * died):
            for why in whys:
                n += 1
                for message, t in (self.made(n, 1 + n), self.ended(n, 2 + n, why)):
                    log.add(t, message)
        log.add(at(40), 'CHAT_PROMISE refused bot="Bot99" id=99 reason=Alarm place="bled" why=not_on_rvr')
        return check(only(log.run()[1]), "promises")

    def test_kept_promises_are_seen(self):
        row = self.session(kept=4, timeouts=1, died=3)
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("made=8 kept=4 broken=4 (timeout=1, unsaid=0, other=3) refused=1", row["detail"])
        self.assertIn("kept 80% of kept+timeout", row["detail"])
        self.assertIn("CHAT_PROMISE kept", row["line"])

    def test_bots_that_say_omw_and_never_arrive_contradict(self):
        row = self.session(kept=2, timeouts=4)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("why=timeout", row["line"])

    def test_an_unsaid_promise_is_no_failure(self):
        # Chat 1.2 follow-up: a promise whose line was never sent ends "unsaid"; it is neither kept nor a timeout.
        log = Log().start(at(0))
        for n in range(1, 7):
            message, t = self.made(n, n)
            log.add(t, message)
            message, t = self.ended(n, n + 1, None if n == 1 else "unsaid")
            log.add(t, message)
        row = check(only(log.run()[1]), "promises")
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("broken=5 (timeout=0, unsaid=5, other=0)", row["detail"])
        self.assertIn("kept 100% of kept+timeout", row["detail"])

    def test_no_kept_promise_is_not_seen(self):
        self.assertEqual(self.session(kept=0, timeouts=2)["status"], pcl.NOT_SEEN, "too few to contradict")
        self.assertEqual(check(only(Log().start(at(0)).run()[1]), "promises")["status"], pcl.NOT_SEEN)


class Sweep6c(unittest.TestCase):
    """Rows from DIAG-b1b6-sweep6c: a boss the leash never brings home, a regroup point at a monster's raw position, and a
    return wave that holds one step (synthetic lines shaped like build b1b6846's)."""

    @staticmethod
    def leash(log, minute, from_spawn, boss="Corrupt Primal of Earth"):
        log.add(at(minute), f'AUTONOMOUS_TETHER_LEASH boss="{boss}" region=191 fromSpawn={from_spawn} tether=890 '
                            f'reason="out of its tether 30 s with nobody fighting it in reach"', "INF",
                "DOL.GS.AutonomousTetherReset")

    def test_a_boss_leashed_again_and_again_from_the_same_spot_contradicts(self):
        log = Log().start(at(0))
        for minute in (10, 12, 14, 16):
            self.leash(log, minute, 1044 if minute != 14 else 1040)
        row = check(only(log.run()[1]), "leash_home")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("Corrupt Primal of Earth at fromSpawn=1044 x4", row["detail"])
        self.assertIn("fromSpawn=1044", row["line"])

    def test_a_boss_that_goes_home_between_leashes_is_seen(self):
        log = Log().start(at(0))
        for minute, distance in ((10, 1044), (20, 940), (30, 1200)):
            self.leash(log, minute, distance)
        self.assertEqual(check(only(log.run()[1]), "leash_home")["status"], pcl.SEEN)
        self.assertEqual(check(only(Log().start(at(0)).run()[1]), "leash_home")["status"], pcl.NOT_SEEN)

    @staticmethod
    def raw_front(log, approach="raw", regroups=12, retries=12):
        log.add(at(5), 'REALM_EXPEDITION_TARGET_SWITCH region=191 from="Corrupt Primal of Water" to="Corrupt Primal of Air" '
                       f'level=83 front=38492,62016,12372 approach={approach} reason="it is fighting the raid"', "WRN")
        for i in range(regroups):
            log.add(at(6, i), f"REALM_EXPEDITION_STRANDED_REGROUP bot=B{i} id={i} region=191 from=1,2,3 to=38492,62016,12372",
                    "WRN")
        for i in range(retries):
            log.add(at(8, i), f"REALM_EXPEDITION_ROUTE_RETRY bot=B{i} id={i} region=191 position=38492,62016,12372 "
                              "reason=No navigable dungeon corridor to the next route stage", "WRN")
        return check(only(log.run()[1]), "raw_front")

    def test_a_raw_front_raiders_regroup_to_and_cannot_walk_from_contradicts(self):
        row = self.raw_front(Log().start(at(0)))
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("12 stranded regroups to it, 12 route retries from it", row["detail"])
        self.assertIn("approach=raw", row["line"])

    def test_a_resolved_front_or_a_raw_one_nobody_is_stuck_at_is_not_a_finding(self):
        self.assertEqual(self.raw_front(Log().start(at(0)), approach="resolved")["status"], pcl.SEEN)
        self.assertEqual(self.raw_front(Log().start(at(0)), retries=3)["status"], pcl.SEEN)
        self.assertEqual(self.raw_front(Log().start(at(0)), approach="unresolved-front-kept")["status"], pcl.SEEN)

    @staticmethod
    def wave(log, minute, step, at_step, extra=""):
        log.add(at(minute), f"REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step={step} wave=1 {extra}"
                            f"at={at_step}/34 members=7", "INF", "DOL.GS.AutonomousRealmRaid")

    def test_a_wave_that_holds_one_step_for_fifteen_minutes_contradicts_even_after_headway_lines(self):
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step=formed wave=1 members=7 steps=34", "INF")
        self.wave(log, 2, "moving", 13)
        self.wave(log, 3, "rebased", 14)
        log.add(at(13), 'REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step=stuck wave=1 at=14/34 living=7 helps=1 '
                        'reason="no headway for 4 minutes and no help left; the stall clock decides"', "WRN")
        self.wave(log, 14, "ahead", 14, "reached=1 ")
        self.wave(log, 23, "joined", 14, "joined=8 ")
        sec = only(log.run()[1])
        row = check(sec, "wave_moves")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("dungeon-albion-krondon wave 1 at step 14 from 06:03:00 (counted as headway)", row["detail"])
        self.assertEqual(check(sec, "return_stuck")["status"], pcl.SEEN, "the old row counts step=ahead as headway")

    def test_a_wave_that_moves_on_or_is_released_is_not_a_finding(self):
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step=formed wave=1 members=7 steps=34", "INF")
        for minute in range(2, 40, 5):
            self.wave(log, minute, "moving", minute)
        self.assertEqual(check(only(log.run()[1]), "wave_moves")["status"], pcl.SEEN)
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step=formed wave=1 members=7 steps=34", "INF")
        self.wave(log, 3, "rebased", 14)
        log.add(at(8), 'REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step=stuck wave=1 at=14/34 living=7 helps=1', "WRN")
        log.add(at(12), 'REALM_RAID_RETURN_WAVE event=dungeon-albion-krondon step=released wave=1 at=14/34 living=7 helps=1',
                "WRN")
        self.wave(log, 30, "joined", 1)
        sec = only(log.run()[1])
        self.assertNotEqual(check(sec, "wave_moves")["status"], pcl.CONTRADICTED)
        self.assertIn("1 stuck wave(s) released to the raid", check(sec, "wave_moves")["detail"])
        self.assertNotEqual(check(sec, "return_stuck")["status"], pcl.CONTRADICTED, "released, not left stuck")


class TextOutput(unittest.TestCase):
    def test_wording_names_the_build_and_never_says_works(self):
        log = Log().start(at(0), commit="5555555eeeee")
        log.add(at(5), 'REALM_RAID_ENDED event=epic-hibernia outcome="Boss defeated" minutes=40 named=3 wipes=0')
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "server.log"
            path.write_text("\n".join(log.lines) + "\n", encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(pcl.main([str(path), "--year", "2026"]), 0)
        text = out.getvalue()
        self.assertIn("Seen in log on build 5555555 (06:05:00)", text)
        self.assertIn("epic-hibernia: no REALM_RAID_STARTED", text)
        self.assertNotIn(" works", text)


class Sweep6d(unittest.TestCase):
    """Rows from DIAG-b1b6-sweep6d: a required-adds phase with no add death while the raid out-dealt the adds' health,
    raiders killed by an epic after their raid ended, and raiders who ended a raid at a regroup point they could not walk
    from (synthetic lines shaped like build b1b6846's)."""

    ADDS = ("Guardian Earthmender", "Magical Earthmender", "Natural Earthmender", "Shadowy Earthmender")

    def earthmenders(self, log, minutes, per_add=4000, defeated_at=None):
        for i, add in enumerate(self.ADDS):
            log.add(at(1, i), f'EPIC_TACTIC_BOT bot=B{i} region=191 target="Corrupt Primal of Earth" action=Retarget '
                              f'to="{add}" reason="required-add {i + 1}/4 of "Corrupt Primal of Earth"" '
                              'encounter=galladoria/earth', "INF", "DOL.GS.EpicEncounterTactics")
        for minute in range(2, 2 + minutes):
            if defeated_at == minute:
                log.add(at(minute, 30), 'REALM_EXPEDITION_TARGET_DEFEATED region=191 target="Natural Earthmender" '
                                        'level=73 minutes=1.0')
            dealt = ",".join(f"{add}:{per_add}/100/20" for add in self.ADDS)
            log.add(at(minute), f'EPIC_FIGHT_LEDGER region=191 target="Magical Earthmender" seconds=60 '
                                f'raid_dealt="{dealt},Corrupt Primal of Earth:1026/63/8" '
                                'raid_took="Corrupt Primal of Earth:17561/23/1"', "INF", "DOL.GS.EpicFightLedger")
        return check(only(log.run()[1]), "adds_progress")

    def test_adds_that_take_more_than_their_health_and_never_die_contradict(self):
        # 4 x 4,000 a minute for 21 minutes = 336,000 on 240,000 health (catalog: 60,000 each), no death.
        row = self.earthmenders(Log().start(at(0)), 22)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("Corrupt Primal of Earth in region 191", row["detail"])
        self.assertIn("240,000", row["line"])

    def test_an_add_death_or_too_little_damage_is_progress(self):
        self.assertEqual(self.earthmenders(Log().start(at(0)), 22, defeated_at=12)["status"], pcl.SEEN)
        self.assertEqual(self.earthmenders(Log().start(at(0)), 30, per_add=1500)["status"], pcl.SEEN,
                         "180,000 in 30 minutes is less than their health")
        self.assertEqual(check(only(Log().start(at(0)).run()[1]), "adds_progress")["status"], pcl.NOT_SEEN)

    def test_the_servers_no_progress_line_contradicts(self):
        log = Log().start(at(0))
        log.add(at(25), 'REALM_EXPEDITION_NO_PROGRESS region=191 boss="Corrupt Primal of Earth" target="Magical Earthmender" '
                        'adds=4 health=240000 dealt=310000 minutes=20 reason="information only"', "WRN")
        row = check(only(log.run()[1]), "adds_progress")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("REALM_EXPEDITION_NO_PROGRESS", row["detail"])

    @staticmethod
    def ended(log, minute, members, event="epic-hibernia", reason="Sweep cap: the play-test sweep's 360-minute limit."):
        listed = ",".join(f'{{"name":"{n}","id":{i},"role":"Attacker","alive":true,"region":{r},"x":{x},"y":{y},"z":{z},'
                          f'"activity":"Fighting Natural Earthmender"}}' for i, (n, r, x, y, z) in enumerate(members))
        log.add(at(minute), 'AUTONOMOUS_GROUP_OUTCOME {"group":"hibernia-1","objective":"GroupPve","phase":"Traveling",'
                            f'"reason":"x","camp":"realm-event-{event}","target":"Magical Earthmender","zone":"Galladoria",'
                            f'"members":[{listed}]}}', "INF", "DOL.GS.AutonomousBotGroupCoordinator")
        log.add(at(minute), f'REALM_RAID_ENDED event={event} outcome="Sweep cap" minutes=360 named=41 wipes=0 '
                            f'reason="{reason}"')

    @staticmethod
    def death(log, minute, bot, killer="Corrupt Primal of Earth", region=191):
        log.add(at(minute), f'AUTONOMOUS_BOT_DEATH bot="{bot}" id=1 level=50 class="Eldritch" realm=Hibernia group=8 '
                            f'killer="{killer}" killerKind=monster killerLevel=83 con=PURPLE attackers=2 '
                            f'activity="Fighting Natural Earthmender" goal="Grind" region={region} zone="Galladoria" '
                            'at=39973,62407,11682', "INF", "DOL.GS.AutonomousDeathDiagnostics")

    def test_a_raider_killed_by_an_epic_soon_after_its_raid_ended_contradicts(self):
        log = Log().start(at(0))
        log.add(at(1), 'REALM_RAID_EPIC_RESPAWN_HELD npc="Lurfos the Herald" region=61 respawn_s=3600 reason="held"')
        self.ended(log, 5, [("Fionnait", 191, 1, 2, 3), ("Aine", 191, 4, 5, 6)])
        self.death(log, 9, "Fionnait")
        self.death(log, 10, "Stranger")                       # not one of the raid's raiders
        self.death(log, 11, "Aine", killer="cave drifter")    # trash, not an epic
        self.death(log, 20, "Aine")                           # after ten minutes
        row = check(only(log.run()[1]), "deaths_after_end")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("1 death(s), 1 raider(s): epic-hibernia (Corrupt Primal of Earth) x1", row["detail"])
        self.assertIn('bot="Fionnait"', row["line"])

    def test_deaths_after_an_end_to_trash_only_are_seen(self):
        log = Log().start(at(0))
        self.ended(log, 5, [("Aine", 191, 4, 5, 6)])
        self.death(log, 9, "Aine", killer="cave drifter")
        self.assertEqual(check(only(log.run()[1]), "deaths_after_end")["status"], pcl.SEEN)

    def test_raiders_ending_at_a_stranded_point_they_could_not_walk_from_contradict(self):
        log = Log().start(at(0))
        for i in range(3):
            log.add(at(2, i), f"REALM_EXPEDITION_STRANDED_REGROUP bot=B{i} id={i} region=191 from=1,2,3 to=38492,62016,12372",
                    "WRN")
            log.add(at(3, i), f"REALM_EXPEDITION_ROUTE_RETRY bot=B{i} id={i} region=191 position=38492,62016,12372 "
                              "reason=No navigable dungeon corridor to the next route stage", "WRN")
        self.ended(log, 5, [("Fionnait", 191, 38492, 62016, 12372), ("Aine", 191, 38492, 62016, 12372),
                            ("Brann", 191, 39659, 62412, 11686)])
        row = check(only(log.run()[1]), "stranded_at_end")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("2 at 191:38492,62016,12372", row["detail"])

    def test_raiders_ending_on_the_floor_are_seen(self):
        log = Log().start(at(0))
        log.add(at(2), "REALM_EXPEDITION_STRANDED_REGROUP bot=B id=1 region=191 from=1,2,3 to=39659,62412,11686", "WRN")
        self.ended(log, 5, [("Brann", 191, 39659, 62412, 11686)])
        self.assertEqual(check(only(log.run()[1]), "stranded_at_end")["status"], pcl.SEEN, "no route retry from it")

    def test_a_sweep_cap_is_its_own_result(self):
        log = Log().start(at(0))
        self.ended(log, 5, [("Aine", 191, 4, 5, 6)])
        self.assertEqual(only(log.run()[1])["raids"][0]["result"], "sweep cap")


class Sweep6e(unittest.TestCase):
    """Rows from DIAG-b1b6-sweep6e: Aidon healed to full past his home (S-1), deaths after his reset inside an attempt
    left open (S-2), raiders killed by the epic trash on a raid's way in (W-1), a muster set aside while a return wave
    walked to it (T-1), the way-in region in the post-raid deaths row and a wave stuck just before a win (synthetic lines
    shaped like build b1b6846's)."""

    AIDON = "Aidon the Archwizard"

    def healed(self, log, minute, frm, to, from_spawn, in_combat=True):
        log.add(at(minute), f'REALM_EXPEDITION_TARGET_HEALED region=277 target="{self.AIDON}" from={frm} to={to} '
                            f'inCombat={in_combat} lastHitAgoMs=3400 aggro=0 targeting=0 inReach=0 returning=False '
                            f'fromSpawn={from_spawn} model=61', "WRN", "DOL.GS.RealmRaidDungeonRoute")

    def status(self, log, minute, health, in_combat=True, second=0):
        log.add(at(minute, second), f'REALM_EXPEDITION_TARGET_STATUS region=277 target="{self.AIDON}" level=75 '
                                    f'health={health}% inCombat={in_combat} raidersWithin1000=16 targeting=12 '
                                    'at=31412,36551,14886 spawnDistance=1084', "INF", "DOL.GS.RealmRaidDungeonRoute")

    @staticmethod
    def death(log, minute, bot, killer, region, second=0, goal="Grind"):
        log.add(at(minute, second), f'AUTONOMOUS_BOT_DEATH bot="{bot}" id=1 level=50 class="Cleric" realm=Albion group=8 '
                                    f'killer="{killer}" killerKind=monster killerLevel=75 con=PURPLE attackers=2 '
                                    f'activity="Fighting" goal="{goal}" region={region} zone="x" at=1,2,3', "INF",
                "DOL.GS.AutonomousDeathDiagnostics")

    def test_aidon_healed_to_full_past_his_home_while_fighting_contradicts(self):
        log = Log().start(at(0))
        self.healed(log, 5, 71, 100, 1256)          # 14:05:19: past his 1,100 home (catalog), inside his 1,500 tether
        self.healed(log, 6, 86, 98, 1000)           # inside his home: the fight's
        self.healed(log, 7, 60, 100, 1800, in_combat=False)
        row = check(only(log.run()[1]), "tether_heal")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn(f"{self.AIDON} x1", row["detail"])
        self.assertIn("fromSpawn=1256", row["line"])

    def test_no_heal_past_home_is_seen(self):
        log = Log().start(at(0))
        log.add(at(1), f'REALM_EXPEDITION_FINAL_ENGAGE region=277 target="{self.AIDON}" mustered=46 needed=44 living=60')
        self.healed(log, 5, 86, 98, 1000)
        self.assertEqual(check(only(log.run()[1]), "tether_heal")["status"], pcl.SEEN)

    def attempt(self, log, close_at):
        log.add(at(1), f'REALM_EXPEDITION_FINAL_ENGAGE region=277 target="{self.AIDON}" mustered=62 needed=62 living=88 '
                       'attempt=2 retry=True')
        self.status(log, 2, 6)
        log.add(at(3), f'AUTONOMOUS_TETHER_LEASH boss="{self.AIDON}" region=277 fromSpawn=1598 tether=1500')
        self.status(log, 4, 100, in_combat=False)  # reset: back at full after 6%
        for i in range(12):
            self.death(log, 5, f"R{i}", self.AIDON if i % 2 else f"Illusion of {self.AIDON}", 277, second=i + 1)
        log.add(at(close_at), f'REALM_EXPEDITION_WIPE region=277 target="{self.AIDON}" level=75 failures=2 of=2 '
                              'engaged=88 fallen=88 health=100 lowest=1 how="left combat"', "WRN")

    def test_deaths_after_a_reset_inside_an_open_attempt_contradict(self):
        log = Log().start(at(0))
        self.attempt(log, 20)
        row = check(only(log.run()[1]), "reset_closes")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn(f"{self.AIDON} in region 277", row["detail"])
        self.assertIn("12 death(s)", row["detail"])

    def test_an_attempt_closed_at_its_reset_is_seen(self):
        log = Log().start(at(0))
        log.add(at(1), f'REALM_EXPEDITION_FINAL_ENGAGE region=277 target="{self.AIDON}" mustered=62 needed=62 living=88')
        self.status(log, 2, 6)
        log.add(at(3), f'REALM_EXPEDITION_WIPE region=277 target="{self.AIDON}" level=75 failures=2 of=2 engaged=88 '
                       'fallen=60 health=100 lowest=1 how="reset to full"', "WRN")
        self.status(log, 4, 100, in_combat=False)
        for i in range(12):
            self.death(log, 5, f"R{i}", self.AIDON, 277, second=i)
        self.assertEqual(check(only(log.run()[1]), "reset_closes")["status"], pcl.SEEN)

    def crystal(self, log, end_minute=30):
        log.add(at(1), "REALM_RAID_STARTED event=dungeon-albion-crystal present=103 required=96 support=112 minutes=15",
                "INF", "DOL.GS.AutonomousRealmRaid")
        log.add(at(2), 'AUTONOMOUS_DUNGEON_EPIC_AVOIDED bot=B id=1 level=50 target="xanxicarian sentry" targetLevel=77 '
                       'aggro=200 action=pass region=50 at=24010,38261,8671 group=8', "INF")
        self.death(log, 3, "Vermellebuffer", "xanxicarian sentry", 50)
        self.death(log, 4, "Klurik", "ordinary rat", 50)
        self.death(log, 5, "Other", "xanxicarian sentry", 62)
        log.add(at(end_minute), 'REALM_RAID_ENDED event=dungeon-albion-crystal outcome="Boss defeated" minutes=31 named=1 '
                                'wipes=0', "INF", "DOL.GS.AutonomousRealmRaid")

    def test_raiders_killed_by_epic_trash_on_the_way_in_contradict(self):
        log = Log().start(at(0))
        self.crystal(log)
        self.death(log, 35, "After", "xanxicarian sentry", 50)   # after the end: the post-raid row's
        sec = only(log.run()[1])
        row = check(sec, "way_in_epics")
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("1 death(s) while the raid ran: dungeon-albion-crystal in region 50 (xanxicarian sentry) x1",
                      row["detail"])
        after = check(sec, "deaths_after_end")
        self.assertEqual(after["status"], pcl.CONTRADICTED, "region 50 is Crystal Cave's way in (sweep 6d judged 62 only)")
        self.assertIn("dungeon-albion-crystal (xanxicarian sentry) x1", after["detail"])

    def test_a_gate_boss_on_the_way_in_is_the_raids_fight_not_epic_trash(self):
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_STARTED event=summoners-albion present=56 required=52", "INF", "DOL.GS.AutonomousRealmRaid")
        log.add(at(1), f'REALM_EXPEDITION_ROUTE_GATE event=summoners-albion region=277 into=248 living=64 past=0 walk=208 '
                       f'bosses="{self.AIDON}@31353,37634,14873" reason="a living boss covers the walk"', "WRN")
        log.add(at(2), f'AUTONOMOUS_DUNGEON_EPIC_AVOIDED bot=B id=1 level=50 target="{self.AIDON}" targetLevel=75 '
                       'aggro=600 action=pass region=277 at=1,2,3 group=8', "INF")
        self.death(log, 3, "R", self.AIDON, 277)
        self.assertEqual(check(only(log.run()[1]), "way_in_epics")["status"], pcl.SEEN)

    @staticmethod
    def troll_wave(log, minute, step, extra):
        log.add(at(minute), f"REALM_RAID_RETURN_WAVE event=dungeon-midgard-trollheim step={step} wave=4 {extra}", "INF",
                "DOL.GS.AutonomousRealmRaid")

    def troll(self, log, last_headway):
        log.add(at(1), "REALM_RAID_STARTED event=dungeon-midgard-trollheim present=64 required=52", "INF",
                "DOL.GS.AutonomousRealmRaid")
        self.troll_wave(log, 2, "formed", "members=1 steps=27")
        self.troll_wave(log, 5, "fighting", 'target="morvalt soldat" level=50 at=1,2,3 respawned=True living=20 kills=10')
        self.troll_wave(log, last_headway, "moving", "at=12/27 living=20 together=10 kills=30 minutes=44")
        self.troll_wave(log, 29, "fighting", 'target="blood broms" level=41 at=1,2,3 respawned=True living=20 kills=30')
        log.add(at(30), 'REALM_EXPEDITION_MUSTER_STALLED region=150 target="Storrow" mustered=3 needed=32 best=3 minutes=20 '
                        'living=64 muster=28288,35959,13532 body=32204,42815,14906 reason="the muster gained nobody"', "WRN")
        return check(only(log.run()[1]), "muster_wave")

    def test_a_muster_set_aside_while_a_moving_wave_walks_to_it_contradicts(self):
        row = self.troll(Log().start(at(0)), 28)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn('dungeon-midgard-trollheim at 06:30:00: "Storrow" set aside, wave 4 headway 120 s before', row["detail"])

    def test_a_muster_set_aside_beside_a_wave_stuck_four_minutes_is_seen(self):
        row = self.troll(Log().start(at(0)), 24)
        self.assertEqual(row["status"], pcl.SEEN, "the wave had no headway for 6 minutes: the stall applies")

    def test_a_wave_stuck_just_before_its_raid_won_is_not_contradicted(self):
        log = Log().start(at(0))
        log.add(at(1), "REALM_RAID_STARTED event=dungeon-albion-crystal present=103 required=96", "INF",
                "DOL.GS.AutonomousRealmRaid")
        log.add(at(2), "REALM_RAID_RETURN_WAVE event=dungeon-albion-crystal step=formed wave=2 members=6 steps=6", "INF")
        log.add(at(10), 'REALM_RAID_RETURN_WAVE event=dungeon-albion-crystal step=stuck wave=2 at=1/6 living=6 helps=1', "WRN")
        log.add(at(10, 51), 'REALM_RAID_ENDED event=dungeon-albion-crystal outcome="Boss defeated" minutes=31 named=1 '
                            'wipes=0', "INF", "DOL.GS.AutonomousRealmRaid")
        row = check(only(log.run()[1]), "return_stuck")
        self.assertNotEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("1 stuck less than 5 minutes before its raid won (not counted)", row["detail"])
        # Stuck long before a win, or before a loss: still a finding.
        log = Log().start(at(0))
        log.add(at(2), "REALM_RAID_RETURN_WAVE event=dungeon-albion-crystal step=formed wave=2 members=6 steps=6", "INF")
        log.add(at(10), 'REALM_RAID_RETURN_WAVE event=dungeon-albion-crystal step=stuck wave=2 at=1/6 living=6 helps=1', "WRN")
        log.add(at(10, 51), 'REALM_RAID_ENDED event=dungeon-albion-crystal outcome="Defeated" minutes=31 named=0 wipes=2',
                "INF", "DOL.GS.AutonomousRealmRaid")
        self.assertEqual(check(only(log.run()[1]), "return_stuck")["status"], pcl.CONTRADICTED)


class Sweep6f(unittest.TestCase):
    """Row from DIAG-b1b6-sweep6f: a return wave rebased to a step none of its raiders stood at that then sat silent (W-2;
    synthetic lines shaped like build b1b6846's Tur Suil and Trollheim waves, and the fixed build's standing= field)."""

    EV = "dungeon-hibernia-tursuil"

    def wave(self, log, minute, rest, level="INF"):
        log.add(at(minute), f"REALM_RAID_RETURN_WAVE event={self.EV} {rest}", level, "DOL.GS.AutonomousRealmRaid")

    def row(self, log):
        return check(only(log.run()[1]), "rebase_silent")

    def test_a_rebase_silent_27_minutes_contradicts(self):
        log = Log().start(at(0))
        self.wave(log, 1, "step=formed wave=1 members=30 steps=27")
        self.wave(log, 5, 'step=rebased wave=1 at=10/27 members=19 catching=14 reason="no raider of the wave walks at its step"')
        self.wave(log, 25, "step=joined wave=1 joined=20 members=39 at=10/27 catching=20 from=1")
        self.wave(log, 28, "step=stuck wave=1 at=10/27 living=19 atStep=0 inside=19 kills=3 helps=0", "WRN")
        self.wave(log, 32, 'step=fighting wave=1 target="balorian" level=50 living=39 atStep=13 step_at=10/27')
        row = self.row(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("dungeon-hibernia-tursuil wave 1 rebased at 06:05:00 to 10/27 (standing=not logged), silent 27.0 min "
                      "until step=fighting", row["detail"])

    def test_a_rebase_whose_core_stands_at_its_step_or_that_moves_soon_is_seen(self):
        log = Log().start(at(0))
        self.wave(log, 1, "step=formed wave=1 members=30 steps=27")
        self.wave(log, 5, "step=rebased wave=1 at=9/27 members=19 core=5 standing=4 catching=14")
        self.wave(log, 30, "step=moving wave=1 at=10/27 living=19 together=12 kills=3 minutes=29")
        self.wave(log, 31, "step=rebased wave=1 at=12/27 members=19 core=5 standing=0 catching=14")
        for m in (33, 35, 37, 39):
            self.wave(log, m, "step=waiting wave=1 at=12/27 living=5 atStep=0 needed=3 catching=14 waiting_s=120")
        self.wave(log, 40, "step=moving wave=1 at=13/27 living=19 together=12 kills=3 minutes=39")
        row = self.row(log)
        self.assertEqual(row["status"], pcl.SEEN, "standing=4 is a core at its step; 9 minutes is under the bar")
        self.assertIn("2 rebase(s)", row["detail"])

    def test_a_silent_rebase_until_the_raid_ends_or_the_log_ends_counts(self):
        log = Log().start(at(0))
        self.wave(log, 5, "step=rebased wave=4 at=15/33 members=38 core=1 standing=0 catching=37")
        log.add(at(20), f'REALM_RAID_ENDED event={self.EV} outcome="Timed out" minutes=220 named=6 wipes=0', "INF",
                "DOL.GS.AutonomousRealmRaid")
        row = self.row(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("(standing=0), silent 15.0 min until the raid ended", row["detail"])
        log = Log().start(at(0))
        self.wave(log, 5, "step=rebased wave=4 at=15/33 members=38 catching=37")
        log.add(at(16), "REALM_RAID_RETURN_WAVE event=other step=moving wave=4 at=2/9 living=9", "INF")
        self.assertIn("until the log ends", self.row(log)["detail"])


class DfKnightLoop(unittest.TestCase):
    """Row from the DF knight loop (build b1b6846): the Albion Darkness Falls raid killed Chthonic Knight Ukobat 14 times in a
    row from 16:42:30 to 18:14:46 with no other target killed (synthetic lines shaped like those)."""

    EV = "darkness-albion"

    def kill(self, log, minute, target, region=249):
        log.add(at(minute), f'REALM_EXPEDITION_TARGET_DEFEATED region={region} target="{target}" level=66 minutes=2.0',
                "INF", "DOL.GS.RealmRaidDungeonRoute")

    def row(self, log):
        return check(only(log.run()[1]), "rekill_loop")

    def started(self):
        log = Log().start(at(0))
        log.add(at(1), f"REALM_RAID_STARTED event={self.EV} present=63 required=52", "INF", "DOL.GS.AutonomousRealmRaid")
        return log

    def test_the_same_knight_killed_six_times_in_a_row_contradicts(self):
        log = self.started()
        self.kill(log, 5, "Prince Abdin")
        for i in range(6):
            self.kill(log, 10 + i * 6, "Chthonic Knight Ukobat")
        row = self.row(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn('"Chthonic Knight Ukobat" in region 249 (darkness-albion): 6 kills in a row from 06:10:00 to 06:40:00',
                      row["detail"])

    def test_five_kills_another_named_between_a_pack_or_an_add_is_seen(self):
        log = self.started()
        for i in range(5):
            self.kill(log, 10 + i * 6, "Chthonic Knight Ukobat")
        self.kill(log, 41, "Prince Asmoien")
        for i in range(4):
            self.kill(log, 45 + i * 6, "Chthonic Knight Ukobat")
        for i in range(9):   # a pack of trash with one name: different monsters
            self.kill(log, 70 + i, "essence shredder")
        # An encounter's add the log showed raised for its boss (EPIC_TACTIC_BOT Retarget), killed again and again.
        log.add(at(79), 'EPIC_TACTIC_BOT bot="A" action=Retarget target="Aidon the Archwizard" to="Illusion of Aidon" '
                        'reason="its adds first"', "INF", "DOL.GS.EpicEncounterTactics")
        for i in range(8):
            self.kill(log, 80 + i, "Illusion of Aidon", region=277)
        row = self.row(log)
        self.assertEqual(row["status"], pcl.SEEN)
        self.assertIn("no named target killed 6+ times in a row", row["detail"])

    def test_a_raid_end_breaks_the_streak(self):
        log = self.started()
        for i in range(3):
            self.kill(log, 10 + i * 6, "Chthonic Knight Ukobat")
        log.add(at(30), f'REALM_RAID_ENDED event={self.EV} outcome="Timed out" minutes=200 named=4 wipes=0', "INF",
                "DOL.GS.AutonomousRealmRaid")
        log.add(at(31), f"REALM_RAID_STARTED event={self.EV} present=63 required=52", "INF", "DOL.GS.AutonomousRealmRaid")
        for i in range(3):
            self.kill(log, 35 + i * 6, "Chthonic Knight Ukobat")
        self.assertEqual(self.row(log)["status"], pcl.SEEN, "two raids, three kills each")


if __name__ == "__main__":
    unittest.main()
