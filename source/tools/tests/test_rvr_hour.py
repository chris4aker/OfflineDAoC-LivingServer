"""rvr_hour.py, the "RvR hour" report (issue #505): fights rebuilt from an older log's RVR_BOT_KILLED, fights and sieges
from the 1.2.3 record rows, the player's rows, masking, and the timelines.

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


if __name__ == "__main__":
    unittest.main()
