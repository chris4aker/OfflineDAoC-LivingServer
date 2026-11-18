"""Living Server 1.2 (owner 2026-11-16: "all our living server features should be in by default if someone downloads our
installer"): every release world ships each Living Server switch at its release value (release-server-settings.json),
which is the server's code default, whatever the developer's install had; a patch's world update moves only a row the
player never changed.

    python -m unittest discover -s source/tools/tests -v
"""
import json
import re
import sqlite3
import sys
import tempfile
import unittest
from contextlib import closing
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
import build_release_037 as release  # noqa: E402
import make_patch  # noqa: E402

SETTINGS_FILE = json.loads((HERE / "release-server-settings.json").read_text(encoding="utf-8"))
POLICY = json.loads((HERE / "OfflineDaoc.ProgressImport/progress-policy.json").read_text(encoding="utf-8"))
ATTRIBUTE = re.compile(r'\[(?:[\w.]+\.)?ServerProperty\(\s*"[^"]+"\s*,\s*"([^"]+)"\s*,\s*"(?:[^"\\]|\\.)*"\s*,\s*([^)]*?)\)\]')


def code_defaults() -> dict:
    """Each server setting's code default as the server stores it (True/False, numbers and text as written)."""
    out = {}
    for path in (REPO / "source/server/GameServer").rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        if "ServerProperty(" not in text:
            continue
        for key, default in ATTRIBUTE.findall(text):
            default = default.strip()
            out[key] = {"true": "True", "false": "False"}.get(default, default.strip('"'))
    return out


def upgrade_changes(since: str) -> dict:
    source = (REPO / "source/server/GameServer/serverproperty/LivingServerSettingsUpgrade.cs").read_text(encoding="utf-8")
    return {key: (old, new) for s, key, old, new in
            re.findall(r'new\("([\d.]+)", "(\w+)", "([^"]*)", "([^"]*)"\)', source) if s == since}


def server_property_table(con):
    con.execute("CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT PRIMARY KEY, Description TEXT, DefaultValue TEXT, "
                "Value TEXT, LastTimeRowUpdated TEXT, ServerProperty_ID TEXT)")


class ReleaseServerSettings(unittest.TestCase):
    def test_every_release_value_is_the_code_default(self):
        defaults = code_defaults()
        self.assertGreater(len(defaults), 500, "the parser found the server's settings")
        for key, value in SETTINGS_FILE["Settings"].items():
            self.assertIn(key, defaults, key)
            self.assertEqual(defaults[key], value, key)

    def test_the_features_ship_on_and_each_switch_kept_off_says_why(self):
        settings = SETTINGS_FILE["Settings"]
        off = {key for key, value in settings.items() if value == "False"}
        self.assertEqual(off, set(SETTINGS_FILE["KeptOff"]), "every switch that ships off has its reason, and only those")
        for key in ("bot_crafters", "bot_craft_orders", "bot_upkeep", "bot_luxury_spending", "bot_siege_supply",
                    "bot_guilds_enabled", "bot_personalities_enabled", "bot_living_breaks", "bot_living_body_language",
                    "bot_nolifer_buffbots", "economy_ledger_enabled", "launcher_level50_kit"):
            self.assertEqual(settings[key], "True", key)
        self.assertEqual(settings["bot_schedule_mode"], "clock")
        for key in ("craft_made_lines", "econ_trade_lines", "crafting_classic_losses", "epic_funus_weapon_puzzle"):
            self.assertEqual(settings[key], "False", key)
        self.assertNotIn("enable_sluaghbinder", settings, "the edition switch is set by set_edition, per edition")

    def test_the_bots_as_people_switches_ship_on_and_need_no_upgrade_line(self):
        """docs/BOTS-AS-PEOPLE-1.2.md, "Settings": twelve new keys, all on in the code and in the release world. New keys,
        so a world from an earlier build has no row and takes the code default: no LivingServerSettingsUpgrade line."""
        keys = ("bot_skill_profiles", "bot_skill_growth", "bot_skill_spare_player_group", "raid_learns_from_raiders",
                "bot_place_knowledge", "bot_ask_the_way", "bot_wrong_roads", "bot_movement_habits", "bot_move_strafe",
                "bot_move_stick_line", "bot_move_sit_and_walls", "bot_living_milestones")
        defaults = code_defaults()
        upgraded = set()
        for since in ("1.1", "1.1.2", "1.1.2.1", "1.2", "1.2.0.1", "1.2.0.2"):
            upgraded |= set(upgrade_changes(since))
        for key in keys:
            self.assertEqual(defaults.get(key), "True", key)
            self.assertEqual(SETTINGS_FILE["Settings"].get(key), "True", key)
            self.assertNotIn(key, upgraded, key)

    def test_each_default_1_2_changed_is_shipped_at_its_new_value(self):
        changes = upgrade_changes("1.2.0.1")
        self.assertTrue(changes)
        for key, (_old, new) in changes.items():
            self.assertEqual(SETTINGS_FILE["Settings"][key], new, key)

    def test_the_earthmenders_mutual_heal_ships_off_with_its_upgrade_line(self):
        """Owner 2026-11-18, "Mutual heal off": off in the code, in the release world, and moved once on older worlds."""
        self.assertEqual(upgrade_changes("1.2.0.2"), {"epic_earthmender_mutual_heal": ("True", "False")})
        self.assertEqual(code_defaults()["epic_earthmender_mutual_heal"], "False")
        self.assertEqual(SETTINGS_FILE["Settings"]["epic_earthmender_mutual_heal"], "False")
        self.assertIn("epic_earthmender_mutual_heal", SETTINGS_FILE["KeptOff"])
        self.assertEqual(SETTINGS_FILE["Settings"]["epic_earthmender_class_puzzle"], "True", "the class puzzle stays on")

    def test_the_developers_rows_are_replaced_and_checked(self):
        with tempfile.TemporaryDirectory() as tmp, closing(sqlite3.connect(Path(tmp) / "w.db")) as con:
            server_property_table(con)
            con.executemany("INSERT INTO ServerProperty VALUES ('bots', ?, 'd', ?, ?, '', ?)", [
                ("econ_trade_lines", "False", "True", "econ_trade_lines"),   # a diagnostics switch the developer turned on
                ("bot_crafters", "", "False", "bot_crafters"),               # the developer's launcher choice
                ("bot_upkeep", "False", "False", "bot_upkeep"),              # an old build's untouched default
            ])
            settings = SETTINGS_FILE["Settings"]
            with self.assertRaisesRegex(AssertionError, "econ_trade_lines"):
                release.verify_server_settings(con, settings)
            report = release.ship_server_settings(con, settings)
            self.assertEqual(report["bot_crafters"], "True")
            self.assertEqual(report["bot_guilds_enabled"], "no row (server default)")
            rows = {k: (v, d) for k, v, d in con.execute("SELECT `Key`, Value, DefaultValue FROM ServerProperty")}
            self.assertEqual(rows, {"econ_trade_lines": ("False", "False"), "bot_crafters": ("True", "True"),
                                    "bot_upkeep": ("True", "True")})
            self.assertEqual(release.verify_server_settings(con, settings),
                             {"checked": 3, "without_row": len(settings) - 3})

    def test_a_world_update_moves_only_a_row_the_player_never_changed(self):
        """make_patch's field update: applied where the player's row still has the released (Value, DefaultValue)."""
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            base, target, player = tmp / "base.db", tmp / "target.db", tmp / "player.db"
            for path, value in ((base, "False"), (target, "True")):
                with closing(sqlite3.connect(path)) as con:
                    server_property_table(con)
                    con.executemany("INSERT INTO ServerProperty VALUES ('bots', ?, 'd', ?, ?, '', ?)",
                                    [(k, value, value, k) for k in ("bot_crafters", "bot_upkeep")])
                    con.commit()
            update = make_patch.world_update(base, target, POLICY, "1.2")
            [table] = [t for t in update["Tables"] if t["Table"] == "ServerProperty"]
            with closing(sqlite3.connect(player)) as con:
                server_property_table(con)
                con.executemany("INSERT INTO ServerProperty VALUES ('bots', ?, 'd', ?, ?, '', ?)", [
                    ("bot_crafters", "False", "False", "bot_crafters"),   # untouched since the release
                    ("bot_upkeep", "", "False", "bot_upkeep"),           # switched off in the launcher
                ])
                cols = table["Columns"]
                for row in table["Update"]:
                    s = [cols.index(c) for c in row["Set"]]
                    k = [cols.index(c) for c in table["Key"]]
                    con.execute(f'UPDATE ServerProperty SET {",".join(chr(34) + cols[i] + chr(34) + "=?" for i in s)} '
                                f'WHERE {" AND ".join(chr(34) + cols[i] + chr(34) + " IS ?" for i in k + s)}',
                                [row["New"][i] for i in s] + [row["Old"][i] for i in k + s])
                rows = {k: (v, d) for k, v, d in con.execute("SELECT `Key`, Value, DefaultValue FROM ServerProperty")}
            self.assertEqual(rows, {"bot_crafters": ("True", "True"), "bot_upkeep": ("False", "")})


if __name__ == "__main__":
    unittest.main()
