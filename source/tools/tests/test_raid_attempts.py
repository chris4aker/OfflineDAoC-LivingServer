"""Tests of the raid_attempts report section and raid_postmortem.py (#644 part 6) on synthetic rows written in the layout
of LivingRows.Raid.cs's Format* functions (no real log, no name from the owner's PC).

    python3 -m unittest source/tools/tests/test_raid_attempts.py
"""
import contextlib
import io
import re
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import raid_postmortem as pm  # noqa: E402
from living_report_sections import _raid, raid_attempts  # noqa: E402
from living_report_sections._util import fields  # noqa: E402

REPO = HERE.parents[1]
PINS = REPO / "source/server/Tests/UnitTests/UT_RaidAttemptRows644.cs"
SAMPLE_PINS = REPO / "source/server/Tests/UnitTests/UT_RaidFightSampleRows644.cs"
MECHANIC_PINS = REPO / "source/server/Tests/UnitTests/UT_RaidMechanicRows644.cs"


def at(clock):
    return f"11/25 {clock} | INF | DOL.GS.LivingRows | "


def pull(clock, aid, boss="Orylle", n=1, size=96, period=64, pct=150, sf=0, roles="12:30:8:46", prev=None, wait=None,
         gather=None, event=None, kind="dungeon"):
    event = event or aid.rsplit(".", 1)[0]
    row = (f"RAID_ATTEMPT attempt={aid} event={event} kind={kind} region=61 boss=\"{boss}\" n={n} size={size} "
           f"period={period} sizePct={pct} sf={sf} roles={roles} classes=Cleric:20,Armsman:12 players=1 build=abc1234")
    if prev:
        row += f" prev={prev} wipeToPullS={wait}"
    if gather is not None:
        row += f" gatherS={gather}"
    return at(clock) + row


def end(clock, aid, outcome, secs, engaged=96, fallen=70, died=61, left=9, deaths=3, health=100, lowest=31):
    return at(clock) + (f"RAID_ATTEMPT_END attempt={aid} outcome={outcome} secs={secs} engaged={engaged} fallen={fallen} "
                        f"died={died} left={left} deaths={deaths} health={health} lowest={lowest}")


def death(clock, aid, t, idx, role="healer", kind="gamebot", killer_kind="boss", killer="Orylle", source="aoe",
          mechanic="-", healers=3, cls="Cleric", player=0, spell=4321):
    return at(clock) + (f"RAID_DEATH attempt={aid} t={t} idx={idx} kind={kind} player={player} class={cls} role={role} "
                        f"level=50 killerKind={killer_kind} killer=\"{killer}\" source={source} spellId={spell} "
                        f"mechanic={mechanic} dist=812 healersIn={healers} attackers=2")


K = "dungeon-albion-krondon"
NIGHT = [
    pull("22:00:00", f"{K}.1", gather=312),
    death("22:01:13", f"{K}.1", 73, 14, mechanic="breath"),
    death("22:01:20", f"{K}.1", 80, 3, role="tank", cls="Armsman", source="melee", mechanic="breath+adds", spell=0),
    death("22:01:22", f"{K}.1", 82, 15, kind="player", player=1, killer_kind="add", killer="Krondon Guard", healers=0),
    at("22:06:52") + f"REALM_EXPEDITION_WIPE region=61 target=\"Orylle\" level=70 failures=1 engaged=96 attempt={K}.1",
    end("22:06:52", f"{K}.1", "wipe", 412),
    pull("22:14:03", f"{K}.2", n=2, prev=f"{K}.1", wait=431),
    death("22:15:00", f"{K}.2", 57, 3, role="tank", cls="Armsman", source="melee", spell=0),
    end("22:20:03", f"{K}.2", "killed", 360, died=1, left=0, deaths=1, health=0, lowest=0),
    pull("23:00:00", "dragon-albion.1", boss="Golestandt", kind="dragon", size=120, period="-", pct="-", sf=24),
]


def quiet(fn, *args):
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = fn(*args)
    return code, out.getvalue(), err.getvalue()


class ParserTests(unittest.TestCase):
    def test_rows_join_on_the_attempt_id(self):
        found = _raid.attempts(NIGHT)
        self.assertEqual([a.key for a in found], [f"{K}.1", f"{K}.2", "dragon-albion.1"])
        first = found[0]
        self.assertEqual((first.boss, first.outcome, first.secs, len(first.deaths)), ("Orylle", "wipe", 412, 3))
        self.assertEqual(found[2].outcome, "open")
        self.assertIsNone(_raid.count(found[2].pull["period"]), "a dash is unknown, not 0")

    def test_an_id_pulled_again_after_a_restart_is_a_new_attempt(self):
        again = NIGHT[:6] + [pull("23:30:00", f"{K}.1"), death("23:30:40", f"{K}.1", 40, 1)]
        found = _raid.attempts(again)
        self.assertEqual([a.key for a in found], [f"{K}.1", f"{K}.1 (run 2)"])
        self.assertEqual([len(a.deaths) for a in found], [3, 1], "the later death goes to the newest pull")

    def test_a_death_before_its_pull_row_still_counts(self):
        found = _raid.attempts([death("22:01:13", "e.1", 73, 1), end("22:02:00", "e.1", "wipe", 120)])
        self.assertEqual((found[0].pull, found[0].outcome, len(found[0].deaths)), ({}, "wipe", 1))

    def test_the_rows_pinned_in_the_csharp_tests_parse(self):
        """The literal rows UT_RaidAttemptRows644 pins for FormatAttempt, FormatEnd and FormatDeath parse into the fields
        these reports read: if the C# rows change, this fails instead of the report silently misreading them."""
        text = PINS.read_text(encoding="utf-8")
        pinned = {}
        for m in re.finditer(r'Is\.EqualTo\(("RAID_[A-Z_]+ [^;]+?)\)\);', text):
            row = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))).replace('\\"', '"')
            pinned[row.split(" ", 1)[0]] = row
        self.assertEqual(set(pinned), {"RAID_ATTEMPT", "RAID_ATTEMPT_END", "RAID_DEATH"})
        a = _raid.attempts([at("22:00:00") + pinned["RAID_ATTEMPT"]])[0]
        self.assertEqual((a.id, a.boss, a.pull["size"], a.pull["sizePct"], a.pull["roles"], a.pull["gatherS"]),
                         ("dungeon-albion-krondon.1", "Orylle", "96", "150", "12:30:8:46", "312"))
        e = fields(pinned["RAID_ATTEMPT_END"])
        self.assertEqual((e["outcome"], e["secs"], e["died"], e["left"], e["lowest"]), ("wipe", "412", "61", "9", "31"))
        d = fields(pinned["RAID_DEATH"])
        self.assertEqual((_raid.killer_of(d), d["role"], d["source"], _raid.mechanics_of(d), d["healersIn"]),
                         ("boss: Icelord Kvasir", "healer", "aoe", ["breath"], "3"))


class SectionTests(unittest.TestCase):
    def setUp(self):
        self.text = raid_attempts.summarize(NIGHT)

    def test_has_the_section_interface(self):
        self.assertEqual(raid_attempts.ROWS, ["RAID_ATTEMPT", "RAID_ATTEMPT_END", "RAID_DEATH", "RAID_FIGHT_SAMPLE",
                                              "RAID_MECHANIC_FIRE", "RAID_THREAT", "RAID_ROLE_TOTALS", "RAID_FUNNEL"])
        self.assertTrue(raid_attempts.TITLE)

    def test_attempts_per_boss_outcomes_and_time_to_kill(self):
        self.assertIn("3 attempts (2 ended, 1 kill), 4 raider deaths.", self.text)
        row = next(line for line in self.text.splitlines() if line.startswith(f"| {K} | Orylle |"))
        cells = [c.strip() for c in row.strip("|").split("|")]
        # Event, Boss, Pulls, Outcomes, Kills, Median size, Size vs period, Time to kill, Time to fail, Lowest, Deaths
        self.assertEqual(cells, [K, "Orylle", "2", "wipe 1, killed 1", "50%", "96", "150%", "6:00", "6:52", "31%", "2"])
        dragon = next(line for line in self.text.splitlines() if line.startswith("| dragon-albion | Golestandt |"))
        self.assertIn("| open 1 |", dragon)
        self.assertIn("| - |", dragon, "no period size: a dash")
        self.assertIn("No end row (still live when the log stops, or the server stopped): dragon-albion.1.", self.text)

    def test_deaths_by_role_kind_killer_and_mechanic(self):
        self.assertIn("| healer | 2 | 50% |", self.text)
        self.assertIn("| tank | 2 | 50% |", self.text)
        self.assertIn("| gamebot | 3 | 75% |", self.text)
        self.assertIn("| player | 1 | 25% |", self.text)
        self.assertIn("| boss: Orylle | 3 | 75% |", self.text)
        self.assertIn("| add: Krondon Guard | 1 | 25% |", self.text)
        self.assertIn("| breath | 2 |", self.text)
        self.assertIn("| adds | 1 |", self.text)
        self.assertIn("Deaths with no living healer within range: 1 (25%).", self.text)
        self.assertIn("By the last hit: aoe 2 (50%), melee 2 (50%).", self.text)

    def test_died_against_left_and_wipe_to_next_pull(self):
        self.assertIn("died 62, left 9.", self.text)
        self.assertIn("Pulls after a failed attempt: 1. From the failed end to the next pull: median 7:11, longest 7:11.",
                      self.text)
        self.assertIn("Muster or regroup before a pull (1 pull): median 5:12", self.text)

    def test_no_rows_and_no_names(self):
        self.assertIn("No RAID_ATTEMPT", raid_attempts.summarize([]))
        self.assertIn("No RAID_ATTEMPT", raid_attempts.summarize([at("22:00:00") + "RAID_ATTEMPT attempt=- event=x"]))
        self.assertNotRegex(self.text, r"\bidx=|\bname\b")

    def test_the_report_loader_picks_the_section_up(self):
        import living_report as lr
        sections = {s.module: s for s in lr.load_sections()}
        self.assertIn("raid_attempts", sections)
        self.assertIsNone(sections["raid_attempts"].error)
        self.assertNotIn("_raid", sections, "the shared parser is not a section")


class PostmortemTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.logs = Path(self._tmp.name)
        noise = at("22:03:00") + "AUTONOMOUS_KILL_XP bot=1 xp=10"
        other_run = at("21:00:00") + f"REALM_EXPEDITION_ATTEMPT_ENDED region=61 target=\"Orylle\" attempt={K}.1"
        lines = NIGHT[:3] + [noise] + NIGHT[3:]
        (self.logs / "server.log").write_text("\n".join([other_run] + lines) + "\n", encoding="utf-8")

    def tearDown(self):
        self._tmp.cleanup()

    def test_one_attempts_timeline(self):
        code, out, err = quiet(pm.main, [f"{K}.1", str(self.logs)])
        self.assertEqual(code, 0, err)
        self.assertIn(f"# Raid attempt {K}.1", out)
        self.assertIn("Size 96 against a period size of 64 (150%), Special Forces 0", out)
        self.assertIn("Roles: tank 12, healer 30, buffer 8, attacker 46.", out)
        self.assertIn("Muster or regroup before the pull: 5:12.", out)
        self.assertIn("**wipe** after 6:52", out)
        self.assertIn("died 61, left 9", out)
        self.assertIn("## Deaths (3)", out)
        self.assertIn("Raider deaths: first at 1:13.", out, "three deaths of 96: no quarter mark")
        self.assertIn("Deaths per 15 s: 1:00 1, 1:15 2.", out)
        self.assertIn("| 1:22 | 15 | player (you) | Cleric | healer | 50 | add: Krondon Guard | aoe | 4321 | - | 812 | 0 | 2 |", out)
        self.assertIn("| 1:20 | 3 | gamebot | Armsman | tank | 50 | boss: Orylle | melee | - | breath+adds |", out)
        self.assertIn("## Other rows of this attempt (1)", out)
        self.assertIn("- 6:52 `REALM_EXPEDITION_WIPE", out)
        self.assertNotIn("21:00", out, "a row of the same id from another run of the server is left out")
        self.assertNotIn("AUTONOMOUS_KILL_XP", out)

    def test_a_retry_names_the_failed_attempt_and_the_wait(self):
        code, out, _ = quiet(pm.main, [f"{K}.2", str(self.logs / "server.log")])
        self.assertEqual(code, 0)
        self.assertIn(f"After failed attempt `{K}.1`: 7:11 from its end to this pull.", out)
        self.assertIn("**killed** after 6:00", out)

    def test_quarter_and_half_marks(self):
        rows = [pull("22:00:00", "e.1", size=4)] + [death(f"22:00:{10 * i:02d}", "e.1", 10 * i, i) for i in range(1, 4)]
        (self.logs / "small.log").write_text("\n".join(rows) + "\n", encoding="utf-8")
        _, out, _ = quiet(pm.main, ["e.1", str(self.logs / "small.log")])
        self.assertIn("Raider deaths: first at 0:10, a quarter of the pull's 4 at 0:10, half of the pull's 4 at 0:20.", out)

    def test_an_open_attempt_and_a_missing_one(self):
        code, out, _ = quiet(pm.main, ["dragon-albion.1", str(self.logs)])
        self.assertEqual(code, 0)
        self.assertIn("**No end row**", out)
        self.assertIn("No raider died in this attempt.", out)
        code, _, err = quiet(pm.main, ["nope.9", str(self.logs)])
        self.assertEqual(code, 1)
        self.assertIn("not in these logs", err)

    def test_list_and_runs(self):
        code, out, _ = quiet(pm.main, ["--list", str(self.logs)])
        self.assertEqual(code, 0)
        self.assertIn(f"| {K}.1 | 11/25 22:00:00 | Orylle | 96 | wipe | 6:52 | 3 |", out)
        again = self.logs / "restart.log"
        again.write_text("\n".join(NIGHT[:6] + [pull("23:30:00", f"{K}.1"), end("23:31:00", f"{K}.1", "killed", 60)]) + "\n",
                         encoding="utf-8")
        code, out, _ = quiet(pm.main, [f"{K}.1", str(again)])
        self.assertIn("was pulled 2 times", out)
        self.assertIn("**killed** after 1:00", out)
        code, out, _ = quiet(pm.main, [f"{K}.1", str(again), "--run", "1"])
        self.assertIn("**wipe** after 6:52", out)
        self.assertEqual(quiet(pm.main, [f"{K}.1", str(again), "--run", "3"])[0], 2)

    def test_bad_arguments(self):
        self.assertEqual(quiet(pm.main, [f"{K}.1"])[0], 2)
        self.assertEqual(quiet(pm.main, [f"{K}.1", str(self.logs / "none")])[0], 2)

    def test_out_writes_the_file(self):
        target = self.logs / "pm.md"
        self.assertEqual(quiet(pm.main, [f"{K}.1", str(self.logs), "--out", str(target)])[0], 0)
        self.assertIn("## Deaths (3)", target.read_text(encoding="utf-8"))


# ---- #644 parts 2-3: RAID_FIGHT_SAMPLE and RAID_FUNNEL --------------------------------------------------------------------

def sample(clock, aid, t, boss=100, alive="4:12:2:30", engaged="4:12:2:30", pow_=95, low=0, on="1", kind="tank", dist=640,
           mez=0, stun=0):
    return at(clock) + (f"RAID_FIGHT_SAMPLE attempt={aid} t={t} bossPct={boss} phase=- adds=- alive={alive} engaged={engaged} "
                        f"healerPow={pow_} healersLow={low} onTank={on} tgtKind={kind} medDist={dist} mezzed={mez} stunned={stun}")


def funnel(clock, event, stage, asked=96, recruited=112, parties=14, hub=90, staged=88, boss="-", lost_="route:3,travel:1,"
           "died:2,logout:4,released:1,other:6", hub_s=612, stage_s=1430, boss_s="-"):
    return at(clock) + (f"RAID_FUNNEL event={event} stage={stage} asked={asked} recruited={recruited} parties={parties} "
                        f"atHub={hub} staged={staged} atBoss={boss} lost={lost_} hubS={hub_s} stageS={stage_s} bossS={boss_s}")


FIGHT = [
    funnel("21:59:00", K, "start", lost_="route:3,travel:1,died:2,logout:0,released:1,other:0"),
    NIGHT[0],
    sample("22:00:00", f"{K}.1", 0),
    sample("22:00:15", f"{K}.1", 15, boss=91, engaged="3:10:2:25"),
    sample("22:00:30", f"{K}.1", 30, boss=80, pow_=60, on="0", kind="healer"),
    sample("22:00:45", f"{K}.1", 45, boss=72, alive="3:11:2:30", engaged="3:9:1:20", pow_=41, low=2, mez=3),
    sample("22:01:15", f"{K}.1", 75, boss=64, alive="2:9:2:28", engaged="1:6:1:12", pow_=12, low=5, on="-", kind="none",
           stun=4),
] + NIGHT[1:4] + [
    sample("22:01:30", f"{K}.1", 90, boss=63, alive="1:5:1:20", engaged="0:2:0:5", pow_=4, low=4, on="0", kind="other",
           dist=1900),
] + NIGHT[4:9] + [
    funnel("22:20:30", K, "end", boss=96, boss_s=95),
    funnel("23:40:00", "dragon-midgard", "end", recruited=40, parties=5, hub=30, staged=22, lost_="route:0,travel:0,died:0,"
           "logout:0,released:0,other:0", hub_s=600, stage_s=1800),
]


class SampleParserTests(unittest.TestCase):
    def test_samples_join_their_attempt(self):
        found = _raid.attempts(FIGHT)
        self.assertEqual([len(a.samples) for a in found], [6, 0])
        self.assertEqual(found[0].samples[3][1]["healersLow"], "2")
        self.assertEqual(_raid.split("1:2:3:4"), {"tank": 1, "healer": 2, "buffer": 3, "attacker": 4})
        self.assertIsNone(_raid.split("1:2:3"))
        self.assertIsNone(_raid.total("1:-:3:4"), "a dash is unknown, not 0")
        self.assertEqual(_raid.total("4:12:2:30"), 48)

    def test_funnels_pair_start_and_end_per_raid(self):
        raids = _raid.funnels(FIGHT)
        self.assertEqual([(e, bool(s), bool(x)) for e, s, x in raids], [(K, True, True), ("dragon-midgard", False, True)])
        self.assertEqual(_raid.lost(raids[0][2]["lost"]), {"route": 3, "travel": 1, "died": 2, "logout": 4, "released": 1,
                                                           "other": 6})
        again = FIGHT + [funnel("23:50:00", K, "start")]
        self.assertEqual(len(_raid.funnels(again)), 3, "a new start after an end is the next raid there")
        self.assertEqual(_raid.lost("route:x,died:2,junk"), {"died": 2})

    def test_the_rows_pinned_in_the_csharp_tests_parse(self):
        """The literal rows UT_RaidFightSampleRows644 pins for FormatSample and FormatFunnel parse into the fields these
        reports read: a change to the C# rows fails here."""
        text = SAMPLE_PINS.read_text(encoding="utf-8")
        pinned = {}
        for m in re.finditer(r'Is\.EqualTo\(("RAID_[A-Z_]+ [^;]+?)\)\);', text):
            row = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))).replace('\\"', '"')
            pinned[row.split(" ", 1)[0]] = row
        self.assertEqual(set(pinned), {"RAID_FIGHT_SAMPLE", "RAID_FUNNEL"})
        a = _raid.attempts([at("22:00:00") + pinned["RAID_FIGHT_SAMPLE"]])[0]
        f = a.samples[0][1]
        self.assertEqual((a.id, f["t"], f["bossPct"], _raid.total(f["alive"]), _raid.split(f["engaged"])["healer"],
                          f["healerPow"], f["onTank"], f["tgtKind"], f["medDist"]),
                         ("dungeon-albion-krondon.3", "45", "72", 48, 10, "61", "1", "tank", "640"))
        raids = _raid.funnels([at("22:00:00") + pinned["RAID_FUNNEL"]])
        self.assertEqual(raids[0][0], "dungeon-albion-krondon")
        e = raids[0][2]
        self.assertEqual((e["stage"], e["asked"], e["atBoss"], _raid.lost(e["lost"])["released"], e["bossS"]),
                         ("end", "96", "84", 1, "95"))


class SampleSectionTests(unittest.TestCase):
    def setUp(self):
        self.text = raid_attempts.summarize(FIGHT)

    def test_a_fight_curve_per_attempt(self):
        self.assertIn("Fight curves (1 attempt with RAID_FIGHT_SAMPLE rows)", self.text)
        # 6 samples: picks at 0, 1, 3, 4 and the last: 100, 91, 72, 64, 63.
        self.assertIn(f"| {K}.1 | Orylle | wipe | 6 | 100 > 91 > 72 > 64 > 63 | 48 > 27 | 48 > 7 | 50% (4%) | 5 | 60% | 640 | 3 / 4 |",
                      self.text)

    def test_a_funnel_table_per_raid(self):
        self.assertIn("Raid funnel (RAID_FUNNEL, 2 raids)", self.text)
        self.assertIn(f"| {K} | 11/25 22:20:30 | end | 96 | 112 | 14 | 90 | 88 | 96 | route 3, travel 1, died 2, logout 4, "
                      "released 1, other 6 | 10:12 | 23:50 | 1:35 |", self.text)
        self.assertIn("| dragon-midgard | 11/25 23:40:00 | end | 96 | 40 | 5 | 30 | 22 | - | none | 10:00 | 30:00 | - |",
                      self.text)

    def test_a_funnel_alone_and_a_start_only(self):
        text = raid_attempts.summarize([funnel("21:00:00", "e", "start")])
        self.assertIn("_No RAID_ATTEMPT", text)
        self.assertIn("| e | 11/25 21:00:00 | start only |", text)

    def test_attempts_without_samples_have_no_curves(self):
        self.assertNotIn("Fight curves", raid_attempts.summarize(NIGHT))
        self.assertNotIn("Raid funnel", raid_attempts.summarize(NIGHT))


class SamplePostmortemTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.log = Path(self._tmp.name) / "server.log"
        self.log.write_text("\n".join(FIGHT) + "\n", encoding="utf-8")

    def tearDown(self):
        self._tmp.cleanup()

    def test_the_samples_are_interleaved_with_the_deaths(self):
        code, out, err = quiet(pm.main, [f"{K}.1", str(self.log)])
        self.assertEqual(code, 0, err)
        self.assertIn("## Timeline (6 samples, 3 deaths)", out)
        lines = [line for line in out.splitlines() if line.startswith("- ") and ("sample:" in line or "death:" in line)]
        self.assertEqual([line.split(" ", 2)[1] for line in lines], ["0:00", "0:15", "0:30", "0:45", "1:13", "1:15", "1:20",
                                                                     "1:22", "1:30"], "by second into the pull")
        self.assertIn("- 0:45 sample: boss 72%, alive T3 H11 B2 A30, engaged T3 H9 B1 A20, healer power 41% (2 under 10%), "
                      "on a tank, median distance 640, mezzed 3, stunned 0", out)
        self.assertIn("- 0:30 sample: boss 80%, alive T4 H12 B2 A30, engaged T4 H12 B2 A30, healer power 60%, on a healer", out)
        self.assertIn("- 1:15 sample: boss 64%", out)
        self.assertIn("no target", out)
        self.assertIn("on someone outside the attempt", out)
        self.assertIn("- 1:20 death: #3 tank Armsman (gamebot), boss: Orylle, melee, during breath+adds", out)
        self.assertIn("- 1:22 death: #15 healer Cleric (player (you)), add: Krondon Guard, aoe", out)
        self.assertIn("_Gaps between samples: up to 30 s", out)
        self.assertNotIn("`RAID_FIGHT_SAMPLE", out, "samples are in the timeline, not repeated under other rows")
        self.assertNotRegex(out, r"\bname=|\bbot=")

    def test_no_samples_says_so(self):
        _, out, _ = quiet(pm.main, [f"{K}.2", str(self.log)])
        self.assertIn("No RAID_FIGHT_SAMPLE rows for this attempt", out)

    def test_the_list_counts_samples(self):
        _, out, _ = quiet(pm.main, ["--list", str(self.log)])
        self.assertIn(f"| {K}.1 | 11/25 22:00:00 | Orylle | 96 | wipe | 6:52 | 3 | 6 |", out)


# ---- #644 parts 3-5: RAID_MECHANIC_FIRE, RAID_THREAT, RAID_ROLE_TOTALS -------------------------------------------------

def fire(clock, aid, t, mech="soulBlast", boss="Orylle", radius=1000, exposed=22, within=14, hit=12, died=5, avoided=8, called=0,
         lead=0, react=450, damage=41200):
    return at(clock) + (f"RAID_MECHANIC_FIRE attempt={aid} boss=\"{boss}\" mechanic={mech} t={t} radius={radius} "
                        f"exposedAtCall={exposed} within={within} hit={hit} died={died} avoided={avoided} called={called} "
                        f"leadMs={lead} reactMedianMs={react} damage={damage}")


def threat(clock, aid, t, from_="tank", to="healer", kind="gamebot", why="threat", held=12400, boss="Orylle"):
    return at(clock) + (f"RAID_THREAT attempt={aid} boss=\"{boss}\" t={t} from={from_} to={to} toKind={kind} why={why} "
                        f"heldMs={held}")


def roles(clock, aid, role, raiders=10, boss=40000, adds=5000, took=60000, healed=0, over=0, deaths=1, cc=0, intr=2, outs=0,
          classes="Armsman:10/45000/0"):
    return at(clock) + (f"RAID_ROLE_TOTALS attempt={aid} role={role} raiders={raiders} dealtBoss={boss} dealtAdds={adds} "
                        f"took={took} healed={healed} overheal={over} deaths={deaths} cc={cc} interrupts={intr} "
                        f"powerOuts={outs} classes={classes}")


MECH = NIGHT[:4] + [
    fire("22:01:24", f"{K}.1", 84),
    fire("22:01:40", f"{K}.1", 100, mech="adds", radius=0, exposed="-", within="-", hit=3, died=1, avoided="-", react="-",
         damage=900),
    threat("22:00:05", f"{K}.1", 5, from_="none", to="tank", held=5000),
    threat("22:00:35", f"{K}.1", 35, from_="tank", to="healer", held=30000),
    threat("22:00:45", f"{K}.1", 45, from_="healer", to="tank", why="taunt", held=10000),
] + NIGHT[4:6] + [
    roles("22:06:52", f"{K}.1", "tank"),
    roles("22:06:52", f"{K}.1", "healer", raiders=30, boss=0, adds=100, took=9000, healed=16000, over=4000, deaths=2, intr=0,
          outs=3, classes="Cleric:20/100/12000,Friar:10/0/4000"),
] + NIGHT[6:]


class MechanicParserTests(unittest.TestCase):
    def test_the_rows_join_their_attempt(self):
        a = _raid.attempts(MECH)[0]
        self.assertEqual((len(a.mechanics), len(a.threats), len(a.roles)), (2, 3, 2))

    def test_the_rows_pinned_in_the_csharp_tests_parse(self):
        text = MECHANIC_PINS.read_text(encoding="utf-8")
        pinned = {}
        for m in re.finditer(r'Is\.EqualTo\(("RAID_[A-Z_]+ [^;]+?)\)\);', text):
            row = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))).replace('\\"', '"')
            pinned[row.split(" ", 1)[0]] = row
        self.assertEqual(set(pinned), {"RAID_MECHANIC_FIRE", "RAID_THREAT", "RAID_ROLE_TOTALS"})
        found = _raid.attempts([at("22:00:00") + pinned[k] for k in sorted(pinned)])
        a = next(x for x in found if x.mechanics)
        f = a.mechanics[0][1]
        self.assertEqual((a.id, f["boss"], f["mechanic"], f["exposedAtCall"], f["avoided"], f["reactMedianMs"], f["damage"]),
                         ("darkness-albion.2", "Legion", "soulBlast", "22", "8", "450", "41200"))
        t = a.threats[0][1]
        self.assertEqual((t["from"], t["to"], t["why"], t["heldMs"]), ("tank", "healer", "threat", "12400"))
        r = next(x for x in found if x.roles).roles[0][1]
        self.assertEqual((r["role"], r["dealtBoss"], r["interrupts"], r["classes"]), ("tank", "40000", "2", "Armsman:1/45000/0"))


class MechanicSectionTests(unittest.TestCase):
    def setUp(self):
        self.text = raid_attempts.summarize(MECH)

    def test_a_mechanic_table_per_boss_and_mechanic(self):
        self.assertIn("Mechanics (RAID_MECHANIC_FIRE, 2 fires", self.text)
        self.assertIn("| Orylle | soulBlast | 1 | no | 22 | 14 | 12 | 5 | 8 (36%) | 0 ms | 450 ms | 41200 |", self.text)
        self.assertIn("| Orylle | adds | 1 | no | - | - | 3 | 1 | - | 0 ms | - | 900 |", self.text)

    def test_threat_and_roles(self):
        self.assertIn("Boss threat (RAID_THREAT", self.text)
        # Held from tank 30 s, from healer 10 s, from none 5 s: 30 of 45 s on a tank.
        self.assertIn(f"| {K}.1 | Orylle | 3 | 67% | threat 2, taunt 1 | tank 2, healer 1 |", self.text)
        self.assertIn("Roles (RAID_ROLE_TOTALS, diagnostics switch; summed over 1 attempt;", self.text)
        self.assertIn("| tank | 1 | 10 | 40000 | 5000 | 60000 | 0 | - | 1 | 0 | 2 | 0 |", self.text)
        self.assertIn("| healer | 1 | 30 | 0 | 100 | 9000 | 16000 | 20% | 2 | 0 | 0 | 3 |", self.text)

    def test_none_without_the_rows(self):
        text = raid_attempts.summarize(NIGHT)
        for title in ("Mechanics (", "Boss threat", "Roles ("):
            self.assertNotIn(title, text)


class MechanicPostmortemTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.log = Path(self._tmp.name) / "server.log"
        self.log.write_text("\n".join(MECH) + "\n", encoding="utf-8")

    def tearDown(self):
        self._tmp.cleanup()

    def test_mechanics_and_threat_in_the_timeline_and_the_roles_at_the_end(self):
        code, out, err = quiet(pm.main, [f"{K}.1", str(self.log)])
        self.assertEqual(code, 0, err)
        self.assertIn("## Timeline (0 samples, 3 deaths, 2 mechanic fires, 3 target changes)", out)
        kinds = [m.group(1) for m in re.finditer(r"^- \d+:\d\d (sample|mechanic|threat|death):", out, re.M)]
        self.assertEqual(kinds, ["threat", "threat", "threat", "death", "death", "death", "mechanic", "mechanic"])
        self.assertIn("- 1:24 mechanic: soulBlast of Orylle (measure only), lands in 0 ms, radius 1000: exposed 22, within 14, "
                      "avoided 8, stepped out in 450 ms (median), hit 12, died 5, damage 41200", out)
        self.assertIn("- 1:40 mechanic: adds of Orylle (measure only), lands in 0 ms, hit 3, died 1, damage 900", out)
        self.assertIn("- 0:45 threat: healer -> tank (gamebot), taunt, held 0:10", out)
        self.assertIn("## Roles at the end", out)
        self.assertIn("| healer | 30 | 0 | 100 | 9000 | 16000 | 4000 | 2 | 0 | 0 | 3 | Cleric:20/100/12000, Friar:10/0/4000 |", out)
        self.assertNotIn("`RAID_MECHANIC_FIRE", out, "in the timeline, not repeated under other rows")


S = "summoners-albion.1"
SIM = [
    at("00:00:00") + (f"RAID_ATTEMPT attempt={S} event=summoners-albion kind=dungeon region=248 boss=\"Summoner Roesia\" n=1 "
                      "size=16 period=64 sizePct=25 sf=0 roles=2:4:0:10 classes=Mercenary:6,Wizard:4,Armsman:2,Cleric:4 players=0 build=sim"),
    at("00:00:00") + f"RAID_THREAT attempt={S} boss=\"Summoner Roesia\" t=0 from=none to=tank toKind=gamebot why=threat heldMs=0",
    at("00:00:00") + (f"RAID_FIGHT_SAMPLE attempt={S} t=0 bossPct=100 phase=- adds=- alive=2:4:0:10 engaged=2:4:0:10 healerPow=- "
                      "healersLow=0 onTank=1 tgtKind=tank medDist=- mezzed=0 stunned=0"),
    at("00:00:50") + (f"RAID_DEATH attempt={S} t=50 idx=7 kind=gamebot player=0 class=Cleric role=healer level=50 killerKind=boss "
                      "killer=\"Summoner Roesia\" source=dot spellId=11756 mechanic=- dist=- healersIn=3 attackers=0"),
    at("00:01:30") + f"RAID_ATTEMPT_END attempt={S} outcome=wipe secs=90 engaged=16 fallen=1 died=1 left=0 deaths=1 health=80 lowest=80",
    at("00:01:30") + (f"RAID_ROLE_TOTALS attempt={S} role=tank raiders=2 dealtBoss=300 dealtAdds=0 took=9000 healed=0 overheal=0 "
                      "deaths=0 cc=0 interrupts=0 powerOuts=0 classes=Armsman:2/300/0"),
]


class CheckTests(unittest.TestCase):
    """--check (#644 part 6): the server job reads the rows the raid sims write (UT_SimRaidRows644) with it."""

    def test_the_field_lists_are_the_rows_the_csharp_tests_pin(self):
        pinned = {}
        for path in (PINS, SAMPLE_PINS, MECHANIC_PINS):
            text = path.read_text(encoding="utf-8")
            for m in re.finditer(r'Is\.EqualTo\(("RAID_[A-Z_]+ [^;]+?)\)\);', text):
                row = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))).replace('\\"', '"')
                pinned[row.split(" ", 1)[0]] = row
        self.assertEqual(set(pinned) - {"RAID_FUNNEL"}, set(_raid.KEYS))
        for kind, row in pinned.items():
            if kind in _raid.KEYS:
                keys = [m.group(1) for m in re.finditer(r'(\w+)=("([^"]*)"|\S+)', row.split(" ", 1)[1])]
                self.assertIsNone(_raid.keys_problem(kind, keys), row)

    def test_a_simulated_attempt_checks_clean(self):
        self.assertEqual(_raid.check(SIM), [])
        self.assertEqual(_raid.check(NIGHT[:6]), [], "a server attempt passes too")
        self.assertEqual(_raid.check(NIGHT[-1:]), [], "a server attempt still open when the log stops is no problem")

    def test_problems_are_named(self):
        self.assertEqual(_raid.check(SIM[:4]), [f"{S}: no RAID_ATTEMPT_END"], "a sim's attempt must end")
        swapped = SIM[:4] + [SIM[4].replace("deaths=1", "deaths=2")]
        self.assertEqual(_raid.check(swapped), [f"{S}: deaths=2 but 1 RAID_DEATH rows"])
        self.assertEqual(_raid.check([SIM[0], SIM[3], SIM[4].replace("left=0", "left=1")]), [f"{S}: died 1 + left 1 is not fallen 1"])

    def test_a_server_attempt_may_count_a_rezzed_death_outside_fallen(self):
        server = [SIM[0].replace("build=sim", "build=abc1234"), SIM[3], SIM[4].replace("fallen=1", "fallen=0")]
        self.assertEqual(_raid.check(server), [], "died and rezzed between two looks: in died, not in fallen")
        under = [server[0], server[1], SIM[4].replace("fallen=1", "fallen=3")]
        self.assertEqual(_raid.check(under), [f"{S}: died 1 + left 0 is under fallen 3"])
        sim = [SIM[0], SIM[3], SIM[4].replace("fallen=1", "fallen=0")]
        self.assertEqual(_raid.check(sim), [f"{S}: died 1 + left 0 is not fallen 0"], "exact for a sim")
        extra = _raid.check([SIM[0] + " junk=1"])
        self.assertTrue(extra and extra[0].startswith("RAID_ATTEMPT 11/25 00:00:00: extra fields junk"), extra)
        order = _raid.check([SIM[2].replace("phase=- adds=-", "adds=- phase=-")])
        self.assertTrue(order and "the server writes" in order[0], order)
        self.assertIn(f"{S}: rows but no RAID_ATTEMPT", _raid.check(SIM[1:]))

    def test_the_command_exits_one_on_a_problem(self):
        with tempfile.TemporaryDirectory() as tmp:
            good, bad = Path(tmp, "good.raid.log"), Path(tmp, "bad.raid.log")
            good.write_text("\n".join(SIM) + "\n", encoding="utf-8")
            bad.write_text("\n".join(SIM[:4]) + "\n", encoding="utf-8")
            code, out, _ = quiet(pm.main, ["--check", str(good)])
            self.assertEqual(code, 0, out)
            self.assertIn("1 attempts, 0 problems.", out)
            self.assertIn(f"| {S} |", out)
            code, out, _ = quiet(pm.main, ["--check", "--github", str(bad)])
            self.assertEqual(code, 1)
            self.assertIn(f"::error title=Raid rows::{S}: no RAID_ATTEMPT_END", out)
            empty = Path(tmp, "empty.raid.log")
            empty.write_text("", encoding="utf-8")
            self.assertEqual(quiet(pm.main, ["--check", str(empty)])[0], 1, "no attempt is a failure, not a pass")


if __name__ == "__main__":
    unittest.main()
