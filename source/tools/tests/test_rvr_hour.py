"""rvr_hour.py, the "RvR hour" report (issue #505): fights rebuilt from an older log's RVR_BOT_KILLED, fights and sieges
from the 1.2.3 record rows, the player's rows, masking, and the timelines;
fights by their biggest side and the multi-group force rows (#599 step 5).

    python -m unittest discover -s source/tools/tests -v
"""
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import rvr_hour  # noqa: E402
import rvr_fight_timeline  # noqa: E402

BUILD = "11/24 10:57:12 | WRN | DOL.GS.ForkBuildInfo | FORK_BUILD name=1.2.3 commit=abcdef123456 bot_guilds=on"


def kill(t, victim, realm, killer, killer_realm, x, y, extra="", killer_type="GameBot"):
    return (f"11/24 {t} | INF | DOL.GS.AutonomousBotRealmPointRewards | RVR_BOT_KILLED victim={victim} id=1 realm={realm} "
            f"goal=RvR force= killer=\"{killer}\" killerRealm={killer_realm} killerType={killer_type} region=253 "
            f"zone=\"Abermenai\" at={x},{y},3000{extra}")


OLD_LOG = [
    BUILD,
    "11/24 11:11:15 | INF | DOL.GS.AutonomousWorldBotController | RVR_NOTICE bot=Claire id=1742 realm=Midgard "
    "target=\"Orrath\" kind=EnemyBot party=solo partyNoticed=1 partyVisible=1 distance=2505 reasonFactors=Combat "
    "chance=0.4 time_since_first_los_ms=1200 delayMs=1598 awareness=normal region=253",
    "11/24 11:11:16 | INF | DOL.GS.AutonomousWorldBotController | RVR_NOTICE bot=Leofrun id=404 realm=Albion "
    "target=\"Ench\" kind=EnemyBot party=solo partyNoticed=2 partyVisible=2 distance=900 reasonFactors=Facing "
    "chance=0.95 time_since_first_los_ms=3000 delayMs=1555 awareness=normal region=253",
    # one fight of three kills (within 30 s and 3,000 units), then a separate one far away
    kill("11:12:00", "A1", "Albion", "H1", "Hibernia", 30000, 30000),
    kill("11:12:10", "A2", "Albion", "H1", "Hibernia", 30500, 30200),
    kill("11:12:20", "H1", "Hibernia", "A3", "Albion", 30200, 30100),
    kill("11:12:25", "M1", "Midgard", "A9", "Albion", 50000, 50000),
    # the player kills a bot: his name must not show
    kill("11:40:00", "M2", "Midgard", "Secretname", "Albion", 10000, 10000, killer_type="GamePlayer"),
    "11/24 11:15:23 | INF | DOL.GS.AutonomousRvrEventLayer | BG_SIEGE_STARTED keep=\"Dun Abermenai\" region=253 "
    "attacker=Hibernia holder=unclaimed",
    "11/24 11:20:00 | INF | DOL.GS.AutonomousWorldBotController | RVR_SIEGE action=hit bot=Snomor keep=rvr-keep-33 "
    "engine=167 hits=1 targetHp=40",
    "11/24 11:21:00 | WRN | DOL.GS.AutonomousWorldBotController | RVR_KEEP_TRAVEL_STALL bot=\"Nermur\" id=2089 "
    "realm=Hibernia target=\"rvr-keep-33\" region=253 from=1,2,3 next=4,5,6 segment=1/1 moving=False stalls=1",
    "11/24 12:12:10 | INF | DOL.GS.AutonomousRvrEventLayer | BG_SIEGE_STARTED keep=\"Dun Abermenai\" region=253 "
    "attacker=Albion holder=unclaimed",
]

NEW_LOG = [
    BUILD,
    "11/24 11:00:00 | INF | DOL.GS.RvrFights | RVR_FIGHT_OPEN fight=1 region=253 x=30000 y=30000 by=engage "
    "realms=Albion,Hibernia groups=Albion:10;Hibernia:20 playerIn=true playerDistance=400",
    "11/24 11:00:05 | INF | DOL.AI.Brain.BotBrain | RVR_ASSIST_CALL group=20 realm=Hibernia region=253 "
    "mainAssist=\"X\" target=\"A1\" targetRealm=Albion targetClass=Cleric role=Healer score=1 tier=Average rising=False "
    "hold=1 fight=1",
    kill("11:00:10", "A1", "Albion", "H1", "Hibernia", 30000, 30000,
         " fight=1 victimGroup=10 victimRole=Healer victimClass=Cleric killerGroup=20 by=Bot calledTarget=true"),
    "11/24 11:00:20 | INF | DOL.GS.RvrPlayerRecord | RVR_PLAYER_NOTICED t=1 player=Secretname looker=H2 group=20 "
    "realm=Hibernia distance=1800 time_since_first_los_ms=2500 called=false seen=player fight=1 region=253",
    "11/24 11:00:30 | INF | DOL.GS.RvrPlayerRecord | RVR_PLAYER_KILLED t=2 player=Secretname killer=H2 killerKind=Bot "
    "by=Bot killerRealm=Hibernia killerGroup=20 killerClass=Ranger sinceNoticedMs=10000 fight=1 region=253 at=1,2,3",
    "11/24 11:01:00 | INF | DOL.GS.RvrFights | RVR_FIGHT_END fight=1 region=253 x=30000 y=30000 seconds=50 by=engage "
    "end=quiet size=6 sizes=Albion:3,Hibernia:3 deaths=Albion:2,Hibernia:0 groups=Albion:10;Hibernia:20 firstDeathS=10 "
    "firstDeathRealm=Albion firstCcS=4 winner=Hibernia held=Hibernia playerIn=true playerDeaths=1 playerKills=0 "
    "playerNearest=0",
    "11/24 11:02:00 | INF | DOL.GS.RvrSieges | RVR_SIEGE_BEGIN t=3 keep=\"Caer Benowyc\" id=50 siege=rvr-keep-50 "
    "region=1 battleground=false holder=Midgard doors=2/2 doorHealth=100 lord=100% guards=10/10 attackers=8 "
    "attackersAtDoor=0 defenders=0 engines=0",
    "11/24 11:07:00 | INF | DOL.GS.RvrSieges | RVR_SIEGE_STALL t=4 keep=\"Caer Benowyc\" id=50 siege=rvr-keep-50 "
    "region=1 battleground=false minute=5 stalledMinutes=5 why=noAttackersAtDoor holder=Midgard doors=2/2 "
    "doorHealth=100 lord=100% guards=10/10 attackers=8 attackersAtDoor=0 defenders=0 engines=0",
    "11/24 11:09:00 | INF | DOL.GS.RvrSieges | RVR_SIEGE_DOOR t=5 keep=\"Caer Benowyc\" id=50 siege=rvr-keep-50 "
    "region=1 battleground=false minute=7 down=1 holder=Midgard doors=1/2 doorHealth=0 lord=100% guards=10/10 "
    "attackers=8 attackersAtDoor=4 defenders=0 engines=1",
    "11/24 11:20:00 | INF | DOL.GS.AutonomousRvrDashboard | RVR_KEEP_TAKEN keep=\"Caer Benowyc\" id=50 region=1 "
    "realm=Albion battleground=false",
    "11/24 11:21:00 | INF | DOL.GS.RvrTeleports | RVR_TELEPORT t=6 bot=B1 id=9 realm=Albion how=moveTo "
    "by=AutonomousStuckWatchdog.Rescue from=1:1,1 to=1:900,900 distance=1272 playerFrom=500 playerTo=900 playerSaw=both "
    "alive=true inCombat=false",
    "11/24 11:22:00 | INF | DOL.GS.RvrDecide | RVR_DECIDE t=7 bot=B1 id=9 realm=Albion force=rvr-9 layer=controller "
    "what=intent from=- to=\"Traveling to keep\" rule=exec.ExecuteRvrTurn.traveling.to why=x",
    "11/24 11:23:00 | INF | DOL.GS.RvrOdd | RVR_ODD seq=1 t=8 player=Secretname realm=Albion region=1 at=1,2 inWar=true "
    "fight=0 siege=rvr-keep-50 note=\"they walked past me\"",
    "11/24 11:30:00 | INF | DOL.GS.RvrPlayerRecord | RVR_PLAYER_VIEW t=9 player=Secretname realm=Albion region=1 "
    "at=1,2 enemiesNear=0 friendsNear=3 enemiesInView=0 friendsInView=3 enemiesFighting=0 fights=none inCombat=false",
    "11/24 11:31:30 | INF | DOL.GS.RvrPlayerRecord | RVR_PLAYER_VIEW t=10 player=Secretname realm=Albion region=1 "
    "at=1,2 enemiesNear=2 friendsNear=3 enemiesInView=2 friendsInView=3 enemiesFighting=0 fights=none inCombat=false",
    "11/24 11:59:00 | INF | DOL.GS.RvrPlayerRecord | RVR_PLAYER_SEAT t=11 player=Secretname minutesInWar=50 "
    "minutesEnemyWithin3000=12 mostEnemiesWithin3000=4 timesAttacked=3 noticesReceived=1 deaths=1 kills=0",
    "11/24 11:59:30 | INF | DOL.GS.RvrRecordPulse | RVR_HOUR t=12 minutes=60 realm=Albion botsInWar=40 solo=10 small=14 "
    "eight=16 fights=3 kills=5 deaths=6 captures=1 keepsHeld=4 stalls=1 dropped=RVR_TELEPORT:3 topRules=a:1",
]


def write(lines):
    handle = tempfile.NamedTemporaryFile("w", suffix=".log", delete=False, encoding="utf-8")
    handle.write("\n".join(lines) + "\n")
    handle.close()
    return Path(handle.name)


def run(argv):
    out = io.StringIO()
    with redirect_stdout(out):
        code = rvr_hour.main(argv)
    return code, out.getvalue()


class OldLog(unittest.TestCase):
    def setUp(self):
        self.path = write(OLD_LOG)
        self.result = rvr_hour.report(list(rvr_hour.read(self.path, 2026)))

    def test_fights_are_rebuilt_from_kills(self):
        fights = self.result["fights"]
        self.assertFalse(self.result["has_record"])
        self.assertEqual(len(fights), 3, [f.id for f in fights])
        first = fights[0]
        self.assertEqual(first.kills, 3)
        self.assertEqual(dict(first.deaths), {"Albion": 2, "Hibernia": 1})
        self.assertEqual(first.winner, "Hibernia")   # fewest deaths
        self.assertEqual(first.seconds, 20)

    def test_the_hour_names_what_needs_the_new_rows(self):
        hour = self.result["hours"][0]
        self.assertEqual(hour["builds"], ["1.2.3@abcdef1"])
        self.assertEqual(hour["teleports"], "needs RVR_TELEPORT")
        self.assertEqual(hour["sieges"]["stalls"], "needs RVR_SIEGE_STALL")
        self.assertEqual(hour["player"]["kills_by_player"], 1)
        self.assertEqual(hour["realms"]["Albion"]["notices"]["0-1000"]["n"], 1)
        self.assertEqual(hour["realms"]["Midgard"]["notices"]["2000-3000"]["median_los_ms"], 1200)
        self.assertEqual(hour["travel"]["stalls_by_place"], {"rvr-keep-33@253": 1})

    def test_battleground_sieges_follow_the_assaults(self):
        sieges = self.result["sieges"]
        self.assertEqual([s.attacker for s in sieges], ["Hibernia", "Albion"])
        self.assertEqual(sieges[0].keep, "Dun Abermenai")
        self.assertEqual(sieges[0].ram_hits, 1)
        self.assertEqual(sieges[0].lowest_door, 40)
        self.assertEqual(sieges[0].result, "next assault began (no capture)")

    def test_the_player_is_masked(self):
        code, text = run([str(self.path), "--year", "2026", "--fights"])
        self.assertEqual(code, 0)
        self.assertNotIn("Secretname", text)
        self.assertIn("killer=<player>", text)


class NewLog(unittest.TestCase):
    def setUp(self):
        self.path = write(NEW_LOG)
        self.result = rvr_hour.report(list(rvr_hour.read(self.path, 2026)))

    def test_fights_come_from_the_record(self):
        self.assertTrue(self.result["has_record"])
        (fight,) = self.result["fights"]
        self.assertEqual(fight.size, 6)
        self.assertEqual(fight.winner, "Hibernia")
        self.assertEqual(fight.first_cc_s, 4)
        self.assertEqual(fight.player_deaths, 1)
        tags = [line.tag for line in fight.events]
        self.assertEqual(tags[0], "RVR_FIGHT_OPEN")
        self.assertIn("RVR_ASSIST_CALL", tags)
        self.assertIn("RVR_PLAYER_KILLED", tags)
        self.assertEqual(tags[-1], "RVR_FIGHT_END")

    def test_the_hour(self):
        hour = self.result["hours"][0]
        self.assertEqual(hour["realms"]["Albion"]["bots_in_war"]["eight"], 16)
        self.assertEqual(hour["realms"]["Albion"]["deaths_by_role"], {"Healer": 1})
        self.assertEqual(hour["tactics"]["called_target_kills"], "1 of 1")
        player = hour["player"]
        self.assertEqual(player["deaths_of_player"], 1)
        self.assertEqual(player["median_s_noticed_to_death"], 10)
        self.assertEqual(player["noticed"], 1)
        self.assertEqual(player["noticed_median_los_ms"], 2500)
        self.assertEqual(player["time_to_first_enemy_s"], 90)
        self.assertEqual(player["idle_minutes_in_war"], 38)
        self.assertEqual(len(player["odd_flags"]), 1)
        self.assertEqual(hour["teleports"]["seen_by_player"], 1)
        self.assertEqual(hour["sieges"]["stalls_by_reason"], {"noAttackersAtDoor": 1})
        self.assertEqual(hour["decisions"]["by_what"], {"intent": 1})
        self.assertIn("rate-limited rows dropped: RVR_TELEPORT:3", hour["notes"])

    def test_a_siege_timeline_runs_from_begin_to_capture(self):
        (siege,) = [s for s in self.result["sieges"] if s.key == "rvr-keep-50"]
        self.assertEqual(siege.keep, "Caer Benowyc")
        self.assertEqual(siege.result, "taken by Albion")
        self.assertEqual(dict(siege.stalls), {"noAttackersAtDoor": 1})
        self.assertEqual([line.tag for line in siege.events],
                         ["RVR_SIEGE_BEGIN", "RVR_SIEGE_STALL", "RVR_SIEGE_DOOR", "RVR_KEEP_TAKEN"])

    def test_text_json_and_the_timeline_front_door(self):
        code, text = run([str(self.path), "--year", "2026"])
        self.assertEqual(code, 0)
        self.assertIn("fights: 1 [RVR_FIGHT_END]", text)
        self.assertNotIn("Secretname", text)
        code, js = run([str(self.path), "--year", "2026", "--json"])
        self.assertIn('"winner": "Hibernia"', js)
        out = io.StringIO()
        with redirect_stdout(out):
            self.assertEqual(rvr_fight_timeline.main([str(self.path), "1", "--year", "2026"]), 0)
        self.assertIn("RVR_FIGHT_END", out.getvalue())
        self.assertNotIn("Secretname", out.getvalue())


class Pure(unittest.TestCase):
    def test_outcome(self):
        from collections import Counter
        self.assertEqual(rvr_hour.outcome({"Albion": 3, "Midgard": 3}, Counter()), "none")
        self.assertEqual(rvr_hour.outcome({"Albion": 3, "Midgard": 3}, Counter({"Albion": 1, "Midgard": 1})), "even")
        self.assertEqual(rvr_hour.outcome({"Albion": 3, "Midgard": 3}, Counter({"Albion": 2})), "Midgard")

    def test_size_buckets(self):
        self.assertEqual(rvr_hour.size_bucket(2), "1v1")
        self.assertEqual(rvr_hour.size_bucket(8), "small (3-8)")
        self.assertEqual(rvr_hour.size_bucket(30), "zerg (25+)")


# ---- #599 step 5: fights by side, and the multi-group forces --------------------------------------------------------

def fight_end(t, fid, sizes, groups, deaths="Albion:0,Midgard:1", winner="Albion"):
    size = sum(int(p.split(":")[1]) for p in sizes.split(","))
    return (f"11/24 {t} | INF | DOL.GS.RvrFights | RVR_FIGHT_END fight={fid} region=163 x=1000 y=1000 seconds=40 "
            f"by=engage end=quiet size={size} sizes={sizes} deaths={deaths} groups={groups} firstDeathS=5 "
            f"firstDeathRealm=Midgard firstCcS=2 winner={winner} held={winner} playerIn=false playerDeaths=0 "
            f"playerKills=0 playerNearest=-1")


def fight_open(t, fid, groups):
    return (f"11/24 {t} | INF | DOL.GS.RvrFights | RVR_FIGHT_OPEN fight={fid} region=163 x=1000 y=1000 by=engage "
            f"realms=Albion,Midgard groups={groups} playerIn=false playerDistance=-1")


def force(t, tag, what, fid, realm, groups, heads, why="-", minutes=0, extra=""):
    return (f"11/24 {t} | INF | DOL.GS.AutonomousRvrEventLayer | {tag} what={what} id={fid} realm={realm} region=163 "
            f"lead=s1 groups={groups} heads={heads}{extra} force=s{groups} why={why} minutes={minutes}")


def cluster(t, realm, biggest, over24=0):
    return (f"11/24 {t} | INF | DOL.GS.RvrRecordPulse | RVR_FORCE_CLUSTER t=1 realm={realm} parties=4 bots=30 "
            f"clusters=2 biggest={biggest} over24={over24}")


def board(t, enemies, contact="true"):
    return (f"11/24 {t} | INF | DOL.GS.RvrFightBoards | RVR_FIGHT_BOARD group=10 leader=\"L\" realm=Albion region=163 "
            f"main=True members=8 enemies={enemies} enemyPets=0 contact={contact} stance=Engage")


ZG = " target=24 online=90 free=72 schedule=off attack=- anchor=amg"
FORCE_LOG = [
    BUILD,
    # four fights in the 20:00 hour: 16v8 (two Albion groups), 24v8 (three), 4v4 and 1v1
    fight_open("20:01:00", 1, "Albion:10+11;Midgard:20"),
    fight_end("20:01:40", 1, "Albion:16,Midgard:8", "Albion:10+11;Midgard:20"),
    fight_open("20:10:00", 2, "Albion:10+11+12;Midgard:21"),
    fight_end("20:10:40", 2, "Albion:24,Midgard:8", "Albion:10+11+12;Midgard:21"),
    fight_open("20:20:00", 3, "Albion:13;Midgard:22"),
    fight_end("20:20:40", 3, "Albion:4,Midgard:4", "Albion:13;Midgard:22", deaths="Albion:1,Midgard:1", winner="even"),
    fight_open("20:30:00", 4, "Albion:14;Midgard:23"),
    fight_end("20:30:40", 4, "Albion:1,Midgard:1", "Albion:14;Midgard:23"),
    # a battlegroup: formed, joined, a leave on the leash, ended
    force("20:00:10", "RVR_FORCE_BATTLEGROUP", "formed", "bgrp-albion-001", "Albion", 2, 16),
    force("20:05:00", "RVR_FORCE_BATTLEGROUP", "joined", "bgrp-albion-001", "Albion", 3, 22),
    force("20:15:00", "RVR_FORCE_BATTLEGROUP", "left", "bgrp-albion-001", "Albion", 2, 14, why="leash", minutes=15),
    force("20:25:00", "RVR_FORCE_BATTLEGROUP", "ended", "bgrp-albion-001", "Albion", 1, 8, why="toosmall", minutes=25),
    # a zerg: formed at 16 in the 20:00 hour, topped up to 24, rolled on, ended in the 21:00 hour
    force("20:40:00", "RVR_FORCE_ZERG", "formed", "zerg-midgard-001", "Midgard", 2, 16, extra=ZG),
    force("20:41:00", "RVR_FORCE_ZERG", "joined", "zerg-midgard-001", "Midgard", 3, 24, extra=ZG, minutes=1),
    force("20:49:00", "RVR_FORCE_ZERG", "moved", "zerg-midgard-001", "Midgard", 3, 24, why="hold", extra=ZG, minutes=9),
    force("21:10:00", "RVR_FORCE_ZERG", "ended", "zerg-midgard-001", "Midgard", 3, 24, why="event", extra=ZG, minutes=30),
    # a battleground battlegroup, its id reused after it ended
    force("20:50:00", "RVR_FORCE_BG_BATTLEGROUP", "formed", "bgbg-albion-bg-keep-7", "Albion", 2, 14, why="wave",
          extra=" keep=bg-keep-7"),
    force("20:55:00", "RVR_FORCE_BG_BATTLEGROUP", "ended", "bgbg-albion-bg-keep-7", "Albion", 1, 7, why="toosmall",
          minutes=5, extra=" keep=bg-keep-7"),
    force("20:58:00", "RVR_FORCE_BG_BATTLEGROUP", "formed", "bgbg-albion-bg-keep-7", "Albion", 2, 12, why="wave",
          extra=" keep=bg-keep-7"),
    cluster("20:05:00", "Albion", 6),
    cluster("20:10:00", "Albion", 12),
    cluster("20:15:00", "Albion", 20),
    cluster("20:20:00", "Midgard", 30, over24=1),
    board("20:10:05", 8),
    board("20:10:06", 24),
    board("20:10:07", 3, contact="false"),
    "11/24 21:20:00 | INF | DOL.GS.RvrRecordPulse | RVR_HOUR t=12 minutes=60 realm=Albion botsInWar=40 solo=10 small=14 "
    "eight=16 fights=0 kills=0 deaths=0 captures=0 keepsHeld=4 stalls=0 dropped=0 topRules=a:1",
]


class FightsBySide(unittest.TestCase):
    def setUp(self):
        self.path = write(FORCE_LOG)
        self.result = rvr_hour.report(list(rvr_hour.read(self.path, 2026)))
        self.hour = self.result["hours"][0]

    def test_side_buckets(self):
        for size, name in ((1, "1-2"), (2, "1-2"), (3, "3-8"), (8, "3-8"), (9, "9-16"), (16, "9-16"), (17, "17-24"),
                           (24, "17-24"), (25, "25+"), (0, "none")):
            self.assertEqual(rvr_hour.side_bucket(size), name, size)

    def test_parse_groups(self):
        self.assertEqual(rvr_hour.parse_groups("Albion:10+11;Midgard:20"), {"Albion": {"10", "11"}, "Midgard": {"20"}})
        self.assertEqual(rvr_hour.parse_groups(""), {})

    def test_the_hour_counts_fights_by_their_biggest_side(self):
        sizes = self.hour["fight_sizes"]
        self.assertEqual(sizes["by_side"], {"1-2": 1, "3-8": 1, "9-16": 1, "17-24": 1})
        self.assertEqual(list(sizes["by_side"]), ["1-2", "3-8", "9-16", "17-24"])   # smallest first
        self.assertEqual(sizes["matchups"], {"16v8": 1, "24v8": 1, "4v4": 1, "1v1": 1})
        self.assertEqual(sizes["multi_group"], 2)
        self.assertEqual(sizes["three_groups"], 1)
        self.assertEqual(sizes["biggest_side"], 24)
        # the total-size buckets stay as they were (24v8 is 32 in all)
        self.assertEqual(self.hour["fights"]["by_size"], {"group (9-24)": 1, "zerg (25+)": 1, "small (3-8)": 1, "1v1": 1})

    def test_each_realm_counts_its_own_side(self):
        self.assertEqual(self.hour["realms"]["Albion"]["fights_by_side"], {"9-16": 1, "17-24": 1, "3-8": 1, "1-2": 1})
        self.assertEqual(self.hour["realms"]["Midgard"]["fights_by_side"], {"3-8": 3, "1-2": 1})

    def test_the_forces_of_the_hour(self):
        forces = self.hour["forces"]
        bg = forces["battlegroup"]["Albion"]
        self.assertEqual((bg["formed"], bg["ended"], bg["alive"], bg["joins"]), (1, 1, 1, 1))
        self.assertEqual(bg["leaves"], {"leash": 1})
        self.assertEqual((bg["peak_heads"], bg["peak_groups"]), (22, 3))
        self.assertEqual(bg["median_minutes_ended"], 25)
        zerg = forces["zerg"]["Midgard"]
        self.assertEqual((zerg["formed"], zerg["ended"], zerg["alive"], zerg["peak_heads"], zerg["moves"]), (1, 0, 1, 24, 1))
        self.assertEqual(forces["bg_battlegroup"]["Albion"]["formed"], 2)
        self.assertEqual(forces["bg_battlegroup"]["Albion"]["alive"], 2)
        self.assertNotIn("Albion", forces["zerg"])

    def test_a_force_alive_across_hours_counts_in_both(self):
        later = self.result["hours"][1]
        zerg = later["forces"]["zerg"]["Midgard"]
        self.assertEqual((zerg["formed"], zerg["ended"], zerg["alive"]), (0, 1, 1))
        self.assertEqual(zerg["median_minutes_ended"], 30)
        # the battleground battlegroup re-formed at 20:58 and never ended: still alive at 21:00
        self.assertEqual(later["forces"]["bg_battlegroup"]["Albion"]["alive"], 1)
        # with no row of its own this hour, it counts at the size it carried over
        self.assertEqual(later["forces"]["bg_battlegroup"]["Albion"]["peak_heads"], 12)
        self.assertEqual(later["forces"]["battlegroup"], {})

    def test_forces_are_split_on_a_reused_id(self):
        bgbg = [fo for fo in self.result["forces"] if fo.kind == "bg_battlegroup"]
        self.assertEqual(len(bgbg), 2)
        self.assertEqual(bgbg[0].end_why, "toosmall")
        self.assertIsNone(bgbg[1].end)

    def test_clusters_and_boards(self):
        clusters = self.hour["clusters"]
        self.assertEqual(clusters["Albion"]["biggest"], 20)
        self.assertEqual((clusters["Albion"]["samples"], clusters["Albion"]["samples_9_15"],
                          clusters["Albion"]["samples_16_plus"]), (3, 1, 1))
        self.assertEqual(clusters["Midgard"]["samples_over24"], 1)
        boards = self.hour["boards"]
        self.assertEqual(boards["in_contact"], 2)
        self.assertEqual(boards["enemies_seen"], {"3-8": 1, "17-24": 1})
        self.assertEqual(boards["most_enemies_seen"], 24)

    def test_text_json_and_the_force_timelines(self):
        code, text = run([str(self.path), "--year", "2026"])
        self.assertEqual(code, 0)
        self.assertIn("fights by biggest side: 1-2: 1, 3-8: 1, 9-16: 1, 17-24: 1", text)
        self.assertIn("a side of 2+ groups 2, of 3+ groups 1", text)
        self.assertIn("zerg Midgard: formed 1", text)
        self.assertIn("Forces (4):", text)
        code, js = run([str(self.path), "--year", "2026", "--json"])
        self.assertIn('"matchup": "24v8"', js)
        self.assertIn('"kind": "zerg"', js)
        code, timeline = run([str(self.path), "--year", "2026", "--forces"])
        self.assertEqual(code, 0)
        self.assertIn("force 11/24 20:40-21:10 zerg zerg-midgard-001 Midgard", timeline)
        self.assertIn("what=moved", timeline)


class OlderLogsSayWhatTheyNeed(unittest.TestCase):
    def test_no_force_rows(self):
        result = rvr_hour.report(list(rvr_hour.read(write(OLD_LOG), 2026)))
        hour = result["hours"][0]
        self.assertEqual(hour["forces"], {"battlegroup": "needs RVR_FORCE_BATTLEGROUP", "zerg": "needs RVR_FORCE_ZERG",
                                          "bg_battlegroup": "needs RVR_FORCE_BG_BATTLEGROUP"})
        self.assertEqual(hour["clusters"], "needs RVR_FORCE_CLUSTER")
        self.assertEqual(hour["boards"], "needs RVR_FIGHT_BOARD")
        # rebuilt fights without killerGroup= don't name their groups
        self.assertEqual(hour["fight_sizes"]["multi_group"], "needs RVR_FIGHT_END groups=")
        self.assertEqual(hour["fight_sizes"]["by_side"], {"1-2": 2, "3-8": 1})
        self.assertEqual(result["forces"], [])
        code, text = run([str(write(OLD_LOG)), "--year", "2026", "--forces"])
        self.assertIn("no RVR_FORCE_* rows", text)

    def test_a_force_seen_after_it_formed(self):
        lines = [BUILD, force("20:05:00", "RVR_FORCE_ZERG", "left", "zerg-albion-003", "Albion", 2, 16, why="late", extra=ZG)]
        (fo,) = rvr_hour.forces(list(rvr_hour.read(write(lines), 2026)))
        self.assertFalse(fo.formed_seen)
        self.assertEqual(dict(fo.leaves), {"late": 1})


if __name__ == "__main__":
    unittest.main()
