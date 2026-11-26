"""living_report.py, the morning summary (issue #625): the loader, the log reading, broken or missing sections, and a
planted-input test for each set B section and the sections reusing kill_xp_report, rvr_hour and CHAT_LINE.
Synthetic lines only (no log or name from the owner's PC).

    python -m unittest discover -s source/tools/tests -p test_living_report.py -v
"""
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import living_report as lr  # noqa: E402
from living_report_sections import chat, economy, group_ended, kill_xp, level_spread, rvr, stuck_hotspots  # noqa: E402


def stamp(t, logger, message, level="INF"):
    return f"{t} | {level} | {logger} | {message}"


SPREAD = "DOL.GS.LivingLogPulse"
LEVEL_LINES = [
    stamp("11/25 10:00:30", SPREAD, "REALM_LEVEL_SPREAD hour=2026-11-25T10:00Z realm=Albion band=20-24 bots=2 gained=0 "
          "levelled=0 new=0 rerolled=0 gone=0 baseline=1"),
    stamp("11/25 11:00:30", SPREAD, "REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Albion band=20-24 bots=1 gained=3 "
          "levelled=1 new=0 rerolled=0 gone=0 baseline=0"),
    stamp("11/25 11:00:30", SPREAD, "REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Albion band=05-09 bots=1 gained=0 "
          "levelled=0 new=0 rerolled=1 gone=0 baseline=0"),
    stamp("11/25 11:00:30", SPREAD, "REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Hibernia band=00-04 bots=4 gained=2 "
          "levelled=2 new=4 rerolled=0 gone=1 baseline=0"),
]

GROUP = "DOL.GS.GroupEndedLog"


def ended(reason, cause, dur=1800, gc="-", left_by="-", open_=0, start=6, end=5, every=1, dropped=0, objective="GroupPve"):
    return stamp("11/25 11:10:00", GROUP,
                 f"GROUP_ENDED seq=1 every={every} dropped={dropped} group=albion-ab12-001 realm=Albion objective={objective} "
                 f"reason={reason} cause={cause} last_left=- phase=\"Grinding\" task_started=1 camp=1 dur_s={dur} "
                 f"size_start={start} size_end={end} alive_end={end} left=0 left_by={left_by} wipes=0 gc={gc} gc_total=0 "
                 f"open={open_} hosted=0")


GROUP_LINES = [
    ended("finished", "expired", dur=3600, gc="assembling:4,casualty:2", open_=5),
    ended("finished", "expired", dur=2400, gc="meetup_plan:1", open_=5),
    ended("member_left", "too_few", dur=600, left_by="logout:3,other:1", end=1, open_=1, dropped=2),
    ended("wipe", "none_alive", dur=900, gc="casualty:3", end=6, objective="RvR"),
]

PULSE = "DOL.GS.CraftingEconomy.EconomyLedger"


def pulse(hour, realm, sold, sold_weapon, med, loot_in, repair_out):
    return stamp("11/25 11:00:02", PULSE,
                 f"ECONOMY_PULSE hour={hour} realm={realm} listings=12 bot_listings=9 list_other=2 list_other_med=50 "
                 f"list_weapon=4 list_weapon_med=12000 list_armor=6 list_armor_med=8000 list_jewelry=0 list_jewelry_med=0 "
                 f"sold={sold} sold_copper={sold * 1000} tax=0 sold_other=0 sold_other_med=0 sold_weapon={sold_weapon} "
                 f"sold_weapon_med={med} sold_armor=0 sold_armor_med=0 sold_jewelry=0 sold_jewelry_med=0 "
                 f"loot_in={loot_in} loot_out=0 vendor_in=200 vendor_out=0 exchange_in=1000 exchange_out=3000 repair_in=0 "
                 f"repair_out={repair_out} upkeep_in=0 upkeep_out=30 craft_in=0 craft_out=0 other_in=40 other_out=0 "
                 f"coin_in={loot_in + 1240} coin_out={repair_out + 3030}")


ECONOMY_LINES = [
    pulse("2026-11-25T11:00Z", "Albion", 3, 2, 10_000, 50_000, 80),
    pulse("2026-11-25T12:00Z", "Albion", 1, 1, 30_000, 25_000, 20),
    pulse("2026-11-25T12:00Z", "Midgard", 0, 0, 0, 700, 0),
]

HOT = "DOL.GS.LivingLogPulse"
STUCK_LINES = [
    stamp("11/25 11:00:30", HOT, "STUCK_HOTSPOT hour=2026-11-25T11:00Z rank=1 region=51 x=23500 y=41000 grid=500 total=3 "
          "bots=2 stuck15m=1 stall45m=1 position=0 seam=1"),
    stamp("11/25 11:00:30", HOT, "STUCK_HOTSPOT hour=2026-11-25T11:00Z rank=0 cells=1 regions=1 total=3 dropped=0 grid=500"),
    stamp("11/25 12:00:30", HOT, "STUCK_HOTSPOT hour=2026-11-25T12:00Z rank=1 region=51 x=23500 y=41000 grid=500 total=4 "
          "bots=3 stuck15m=0 stall45m=0 position=0 seam=4"),
    stamp("11/25 12:00:30", HOT, "STUCK_HOTSPOT hour=2026-11-25T12:00Z rank=2 region=100 x=1000 y=1000 grid=500 total=1 "
          "bots=1 stuck15m=1 stall45m=0 position=0 seam=0"),
    stamp("11/25 12:00:30", HOT, "STUCK_HOTSPOT hour=2026-11-25T12:00Z rank=0 cells=2 regions=2 total=5 dropped=0 grid=500"),
]

KILL_LINE = stamp("11/25 10:40:00", "DOL.GS.AutonomousKillXpDiagnostics",
                  "AUTONOMOUS_KILL_XP seq=25 every=25 dropped=0 realm=Albion zone=\"Salisbury Plains\" region=1 npc=\"bandit\" "
                  "npcLevel=14 npcXp=52000 con=ORANGE party=group size=5 contributors=4 minLevel=11 maxLevel=13 avgLevel=12.3 "
                  "levelDiff=1 camp=\"c:1\" campTarget=\"giant frog\" onTarget=1 sameName=0 xpPerMember=8061/8061 xpSum=16122 "
                  "share=1.000 otherParties=0 lost=0.000 shares=\"group/Albion/4/1.000/killer\"")
GOAL_LINE = stamp("11/25 10:41:00", "DOL.GS.AutonomousGoalDiagnostics",
                  'AUTONOMOUS_GOAL_ATTEMPT {"schema":1,"realm":1,"zone":"Salisbury Plains","xp":9000,"targetXp":3000,'
                  '"bot":"Secretbot","botId":7,"level":13}')
CHAT_LINES = [
    stamp("11/25 10:00:01", "DOL.GS.BotChatTranscript", 'CHAT_LINE who=bot ch=say realm=Albion from="Secretbot" src=field kind=gamebot'),
    stamp("11/25 10:00:02", "DOL.GS.BotChatTranscript", 'CHAT_LINE who=bot ch=group realm=Albion from="Secretpal" kind=companion'),
    stamp("11/25 10:00:03", "DOL.GS.BotChatTranscript", 'CHAT_LINE who=player ch=say realm=Albion from="Secretname" text="hi"'),
]
RVR_LINES = [
    stamp("11/24 10:57:12", "DOL.GS.ForkBuildInfo", "FORK_BUILD name=1.2.3 commit=abcdef123456", "WRN"),
    stamp("11/24 11:59:30", "DOL.GS.RvrRecordPulse", "RVR_HOUR t=12 minutes=60 realm=Albion botsInWar=40 solo=10 small=14 "
          "eight=16 fights=3 kills=5 deaths=6 captures=1 keepsHeld=4 stalls=1 dropped=RVR_TELEPORT:3 topRules=a:1"),
]
NAMES = ("Secretbot", "Secretpal", "Secretname")


def write(folder: Path, name: str, lines):
    path = folder / name
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


class LoaderTest(unittest.TestCase):
    def test_the_shipped_sections_all_load(self):
        sections = lr.load_sections()
        errors = [(s.module, s.error) for s in sections if s.error]
        self.assertEqual(errors, [])
        names = {s.module for s in sections}
        for mine in ("level_spread", "group_ended", "economy", "stuck_hotspots", "kill_xp", "rvr", "chat"):
            self.assertIn(mine, names)
        self.assertNotIn("_util", names)

    def test_a_missing_sections_folder_still_gives_a_report(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = write(Path(tmp), "server.log", CHAT_LINES)
            report, errors = lr.build([log], folder=Path(tmp) / "no-such-folder")
        self.assertEqual(errors, 0)
        self.assertIn("# Living Server morning summary", report)
        self.assertIn("_No sections in", report)

    def test_an_empty_sections_folder_still_gives_a_report(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "sections").mkdir()
            report, errors = lr.build([write(Path(tmp), "server.log", CHAT_LINES)], folder=Path(tmp) / "sections")
        self.assertEqual(errors, 0)
        self.assertIn("_No sections in", report)

    def test_broken_sections_show_as_errors_and_the_rest_still_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "sections"
            folder.mkdir()
            (folder / "a_syntax.py").write_text("def oops(:\n", encoding="utf-8")
            (folder / "b_import.py").write_text("import no_such_module_625\n", encoding="utf-8")
            (folder / "c_missing.py").write_text("TITLE = 'Half'\n", encoding="utf-8")
            (folder / "d_raises.py").write_text(
                "TITLE = 'Raises'\nROWS = ['CHAT_LINE']\ndef summarize(lines):\n    raise ValueError('planted')\n",
                encoding="utf-8")
            (folder / "e_not_text.py").write_text(
                "TITLE = 'Not text'\nROWS = ['CHAT_LINE']\ndef summarize(lines):\n    return 3\n", encoding="utf-8")
            (folder / "f_good.py").write_text(
                "TITLE = 'Good'\nROWS = ['CHAT_LINE']\ndef summarize(lines):\n    return f'{len(lines)} chat lines'\n",
                encoding="utf-8")
            (folder / "_helper.py").write_text("raise RuntimeError('never loaded')\n", encoding="utf-8")
            report, errors = lr.build([write(Path(tmp), "server.log", CHAT_LINES)], folder=folder)
        self.assertEqual(errors, 5)
        self.assertIn("## a_syntax", report)
        self.assertIn("could not load: SyntaxError", report)
        self.assertIn("could not load: ModuleNotFoundError", report)
        self.assertIn("**Section error:** missing ROWS, summarize", report)
        self.assertIn("**Section error:** ValueError: planted (d_raises.py:4)", report)
        self.assertIn("summarize returned int, not text", report)
        self.assertIn("## Good", report)
        self.assertIn("3 chat lines", report)
        self.assertNotIn("never loaded", report)

    def test_strict_exits_one_on_a_section_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "sections"
            folder.mkdir()
            (folder / "bad.py").write_text("raise RuntimeError('x')\n", encoding="utf-8")
            log = write(Path(tmp), "server.log", CHAT_LINES)
            with redirect_stdout(io.StringIO()):
                self.assertEqual(lr.main([str(log), "--strict", "--sections", str(folder)]), 1)
                self.assertEqual(lr.main([str(log), "--sections", str(folder)]), 0)


class ReadingTest(unittest.TestCase):
    def test_a_folder_reads_rotated_logs_first_and_merges_by_time(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            write(folder, "server.log.2026-11-24.1", [stamp("11/24 23:00:00", "x", "CHAT_LINE who=bot ch=b")])
            write(folder, "server.log.2026-11-24.0", [stamp("11/24 22:00:00", "x", "CHAT_LINE who=bot ch=a")])
            write(folder, "server.log", [stamp("11/25 01:00:00", "x", "CHAT_LINE who=bot ch=d"), "not a log line",
                                         stamp("11/25 01:00:01", "x", "OTHER_ROW a=1")])
            write(folder, "detail.log", [stamp("11/25 00:30:00", "x", "CHAT_LINE who=bot ch=c")])
            write(folder, "warn.log", [stamp("11/25 00:00:00", "x", "CHAT_LINE who=bot ch=never")])
            files = [p.name for p in lr.log_files([folder])]
            self.assertEqual(files, ["server.log.2026-11-24.0", "server.log.2026-11-24.1", "server.log", "detail.log"])
            sections = [s for s in lr.load_sections() if s.module == "chat"]
            per, stats = lr.read_lines(lr.log_files([folder]), sections, year=2026)
        self.assertEqual([line.split("ch=")[1] for line in per["chat"]], ["a", "b", "c", "d"])
        self.assertEqual(stats["first"], "11/24 22:00:00")

    def test_since_and_hours_keep_only_the_window(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = write(Path(tmp), "server.log", CHAT_LINES)
            out = io.StringIO()
            with redirect_stdout(out):
                lr.main([str(log), "--since", "11/25 10:00", "--hours", "0.0006", "--year", "2026"])
        self.assertIn("<sub>chat: 2 rows of CHAT_LINE</sub>", out.getvalue())

    def test_a_prefix_row_takes_every_tag_with_it(self):
        section = lr.Section("rvr", "RvR", ["RVR_*", "FORK_BUILD"])
        self.assertTrue(section.wants("RVR_HOUR"))
        self.assertTrue(section.wants("FORK_BUILD"))
        self.assertFalse(section.wants("FORK_BUILDER"))
        self.assertFalse(section.wants("BG_POPULATION"))

    def test_the_whole_report_prints_no_names(self):
        with tempfile.TemporaryDirectory() as tmp:
            lines = LEVEL_LINES + GROUP_LINES + ECONOMY_LINES + STUCK_LINES + [KILL_LINE, GOAL_LINE] + CHAT_LINES + RVR_LINES
            report, errors = lr.build([write(Path(tmp), "server.log", lines)])
        self.assertEqual(errors, 0, report)
        for name in NAMES:
            self.assertNotIn(name, report)


class SectionTest(unittest.TestCase):
    def test_level_spread(self):
        text = level_spread.summarize(LEVEL_LINES)
        self.assertIn("Latest hour 2026-11-25T11:00Z (2 hour(s) in the logs, 1 after a baseline)", text)
        self.assertIn("| Albion | 2 | 3 | 1 | 0 | 1 | 0 | 1.50 |", text)
        self.assertIn("| Hibernia | 4 | 2 | 2 | 4 | 0 | 1 | 0.50 |", text)
        self.assertIn("| 20-24 | 1 (3) |  |  |", text)
        self.assertIn("1 re-rolled bot(s) flagged", text)
        self.assertIn("_No REALM_LEVEL_SPREAD rows", level_spread.summarize([]))

    def test_group_ended(self):
        text = group_ended.summarize(GROUP_LINES)
        self.assertIn("4 rows, standing for about 6 ended groups.", text)
        self.assertIn("| finished | expired | 2 | 50% | 50 | 6 > 5 | 100% |", text)
        self.assertIn("| member_left | too_few | 1 | 25% | 10 | 6 > 1 | 100% |", text)
        self.assertIn("| casualty | 5 | 24% |", text)
        self.assertIn("| assembling | 4 | 19% |", text)
        self.assertIn("| open at end | 11 | 52% |", text)
        self.assertIn("Members who left during a group: logout 3, other 1.", text)
        self.assertIn("By objective: GroupPve 3, RvR 1. Wipes at the end: 1.", text)

    def test_economy(self):
        text = economy.summarize(ECONOMY_LINES)
        self.assertIn("2 hour(s), 2026-11-25T11:00Z to 2026-11-25T12:00Z.", text)
        self.assertIn("| Albion | 12 (9) | 4 | 40s 0c | 0s 0c | 4 (1g 20s) / 3 (2g 0s) |", text)
        self.assertIn("| Albion | 7g 50s / 0s 0c |", text)
        self.assertIn("| 0s 0c / 1s 0c |", text, "repair out summed: 100c")
        self.assertIn("| Midgard |", text)
        self.assertNotIn("| Hibernia |", text)
        self.assertEqual(economy.money(12_345), "1g 23s")
        self.assertEqual(economy.money(-45), "-0s 45c")

    def test_stuck_hotspots(self):
        text = stuck_hotspots.summarize(STUCK_LINES)
        self.assertIn("2 hour(s); 8 stuck or recovery events in all.", text)
        self.assertIn("| 51 | 23500 | 41000 | 7 | 2 | 3 | 1 | 1 | 0 | 5 |", text)
        self.assertIn("| 100 | 1000 | 1000 | 1 | 1 | 1 | 1 | 0 | 0 | 0 |", text)
        self.assertNotIn("3 or more hours", text)

    def test_kill_xp(self):
        text = kill_xp.summarize([KILL_LINE, GOAL_LINE])
        self.assertIn("Per realm (AUTONOMOUS_KILL_XP)", text)
        self.assertIn("Albion", text)
        self.assertIn("Camp attempts", text)
        self.assertNotIn("Secretbot", text)
        self.assertIn("_No AUTONOMOUS_KILL_XP", kill_xp.summarize([]))

    def test_rvr(self):
        text = rvr.summarize(RVR_LINES)
        self.assertIn("RvR hour report", text)
        self.assertIn("build 1.2.3@abcdef1", text)
        self.assertIn("40 in war", text)
        self.assertIn("_No RvR rows._", rvr.summarize(RVR_LINES[:1]))

    def test_chat(self):
        text = chat.summarize(CHAT_LINES)
        self.assertIn("3 lines.", text)
        self.assertIn("| say | 1 | 1 | 2 |", text)
        self.assertIn("| group | 1 | 0 | 1 |", text)
        self.assertIn("Bot lines by kind of bot: gamebot 1, companion 1.", text)
        for name in NAMES:
            self.assertNotIn(name, text)


if __name__ == "__main__":
    unittest.main()
