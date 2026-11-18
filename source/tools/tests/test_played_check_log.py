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
        self.assertIn("(c) relay back at its sender within 60 s: 0", row["detail"])
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

    def test_c_a_relay_back_at_its_sender_within_60_s(self):
        # 4891 -> 5219 -> 4891, as at 12:36-12:37 on 453e01a
        log = self.stance_log()
        log.add(at(5, 0), self.RETREAT.format(g=4891, r="in contact: fight lost", to=3, relay="sent", relayed="False"))
        log.add(at(5, 9), self.RETREAT.format(g=5219, r="relayed retreat", to=0, relay="none", relayed="True"))
        log.add(at(5, 30), self.REGROUP.format(g=5219, r="retreat over", to=4, relay="sent"))
        log.add(at(5, 37), self.REGROUP.format(g=4891, r="relayed regroup", to=0, relay="none"))
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("(c) relay back at its sender within 60 s: 1", row["detail"])
        self.assertIn("(b) regroup ending a relayed retreat relayed: 1", row["detail"])
        log = self.stance_log()
        log.add(at(5, 0), self.RETREAT.format(g=4891, r="in contact: fight lost", to=3, relay="sent", relayed="False"))
        log.add(at(5, 50), self.REGROUP.format(g=6000, r="after the fight", to=2, relay="sent"))
        log.add(at(6, 5), self.REGROUP.format(g=4891, r="relayed regroup", to=0, relay="none"))   # 65 s: too late
        self.assertIn("(c) relay back at its sender within 60 s: 0", self.regroup(log)["detail"])

    def test_d_relay_started_regroups_over_ten_percent(self):
        log = self.stance_log(own_regroups=3)
        row = self.regroup(log)
        self.assertEqual(row["status"], pcl.CONTRADICTED)
        self.assertIn("1 started by a relay (20.0%, at most 10%)", row["detail"])
        self.assertIn('reason="relayed retreat"', row["line"])

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
