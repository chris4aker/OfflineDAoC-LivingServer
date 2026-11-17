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
        wave = check(sec, "return_wave")
        self.assertEqual(wave["status"], pcl.CONTRADICTED)
        self.assertIn("step=stuck", wave["line"])
        self.assertEqual(check(sec, "result")["status"], pcl.SEEN)

    def test_outcomes(self):
        log = Log().start(at(0))
        for i, outcome in enumerate(pcl.OUTCOMES):
            log.add(at(i + 1), f'REALM_RAID_ENDED event=raid-{i} outcome="{outcome}" minutes=3 named=0 wipes=0')
        sec = only(log.run()[1])
        self.assertEqual([r["result"] for r in sec["raids"]],
                         ["won", "lost", "timed out (stall)", "superseded", "stopped by player", "failed rally"])
        result = check(sec, "result")
        self.assertEqual(result["status"], pcl.CONTRADICTED)
        self.assertIn("Timed out", result["line"])

    def test_return_wave_without_a_stuck_one_is_seen(self):
        sec = only(self.raid_log().run()[1])
        self.assertEqual(check(sec, "return_wave")["status"], pcl.SEEN)
        self.assertEqual(sec["raids"][0]["result"], "no end in this build's log")

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


class Rvr(unittest.TestCase):
    def test_regroups_relayed_on_contradict(self):
        log = Log().start(at(0))
        log.add(at(1), 'RVR_REGROUP phase=start group=1 realm=Hibernia reason="after the fight" relayedTo=0')
        log.add(at(2), 'RVR_REGROUP phase=end group=1 reason="regrouped" seconds=29')
        sec = only(log.run()[1])
        self.assertEqual(check(sec, "regroup")["status"], pcl.SEEN)
        self.assertIn("1 regroup starts", check(sec, "regroup")["detail"])
        log.add(at(3), 'RVR_REGROUP phase=start group=2 realm=Albion reason="relayed regroup" relayedTo=3')
        regroup = check(only(log.run()[1]), "regroup")
        self.assertEqual(regroup["status"], pcl.CONTRADICTED)
        self.assertIn("1 relayed on", regroup["detail"])
        self.assertIn("1 started by a relay", regroup["detail"])

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
        self.assertEqual(check(sec, "census")["status"], pcl.CONTRADICTED)
        self.assertIn("Sluaghbinder=3", check(sec, "census")["line"])
        self.assertEqual(check(sec, "trained")["status"], pcl.CONTRADICTED)


class BotsAsPeople(unittest.TestCase):
    def profiles(self, log, casual, average, veteran):
        n = 0
        for tier, count in (("casual", casual), ("average", average), ("veteran", veteran)):
            for _ in range(count):
                n += 1
                log.add(at(1), f"BOT_SKILL_PROFILE name=B{n} person=b:{n} realm=Albion level=50 tier={tier} "
                               f"start={tier} place=0.42 xp_h=1.0 awareness=normal")

    def test_people_rows(self):
        log = Log().start(at(0))
        log.add(at(1), "BOT_PEOPLE_SUMMARY seconds=300 skill=on rows=skill_profiles:10 scope: x logouts=0")
        self.profiles(log, 31, 49, 20)
        log.add(at(2), "BOT_SKILL_GROWTH name=B1 person=b:1 realm=Albion from=casual to=average start=casual xp_h=9.0")
        log.add(at(2), "BOT_SKILL_MISTAKE kind=pull spared=true name=B2 person=b:2 realm=Albion tier=casual chance=0.050")
        log.add(at(3), "RAID_LEARN raid=dungeon-albion-crystal mechanic=xanxicar-breath record=1 seenShare=0.40 familiarity=0.5")
        log.add(at(3), "BOT_PLACE_LEARNED how=seeded bot=B3 id=3 realm=Albion level=35 place=x")
        log.add(at(4), "BOT_PLACE_LEARNED how=visited bot=B3 id=3 realm=Albion level=35 place=y")
        log.add(at(4), 'BOT_WAY_ASK bot=B4 id=4 realm=Albion level=30 tier=casual dest="Snowdonia" channel=faction said=asked')
        log.add(at(4), 'BOT_WAY_TOLD asker=B4 id=4 answerer=B5 answererId=5 realm=Albion place="x" dest="y"')
        log.add(at(5), 'BOT_WAY_WRONG_ROAD bot=B4 id=4 realm=Albion level=30 tier=casual dest="x" cost_s=95.0 result=back')
        log.add(at(5), "BOT_MOVE_HABITS seconds=300 strafe=3")
        log.add(at(5), 'BOT_MOVE_WALL bot="B6" id=6 region=1 keep="Caer Benowyc"')
        log.add(at(6), "BOT_MILESTONE_REACHED bot=B7 arc=ding50")
        log.add(at(6), "BOT_MILESTONE_TALK bot=B7 arc=ding50 line=halfway")
        sec = only(log.run()[1])
        for cid in ("summary", "skill_profile", "skill_growth", "mistakes", "raid_learn", "places", "way", "movement",
                    "milestones"):
            self.assertEqual(check(sec, cid)["status"], pcl.SEEN, cid)
        self.assertIn("100 profiles: 31/49/20", check(sec, "skill_profile")["detail"])
        log.add(at(7), 'BOT_WAY_WRONG_ROAD bot=B8 id=8 realm=Albion tier=casual dest="x" cost_s=240.5 result=on')
        way = check(only(log.run()[1]), "way")
        self.assertEqual(way["status"], pcl.CONTRADICTED)
        self.assertIn("cost_s=240.5", way["line"])

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


if __name__ == "__main__":
    unittest.main()
