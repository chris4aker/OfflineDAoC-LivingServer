"""Living Server 1.2 (owner 2026-11-16: "all our living server features should be in by default if someone downloads our
installer"): what the package tells the player matches. On the owner's PC (build b1b6846, 2026-11-18) INSTALL.cmd ended
with "Every new feature is OFF until you switch it on", which was no longer true. The installer's last words, the package
README and the launcher's Living Server tab say the features are on by default, with their switches in the tab.

    python -m unittest discover -s source/tools/tests -v
"""
import json
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
INSTALL = REPO / "tools/fork-build/install-server-build.ps1"
PACKAGE_README = REPO / "tools/fork-build/README.txt"
TAB = REPO / "source/tools/OfflineDaoc.Launcher/SocialExpansionSettingsControl.cs"
SETTINGS = json.loads((REPO / "source/tools/release-server-settings.json").read_text(encoding="utf-8"))["Settings"]

# Wording that says the player-facing features start off (each one was in the package or the tab before this fix).
STALE = [
    re.compile(r"every new feature is off", re.I),
    re.compile(r"off until you switch", re.I),
    re.compile(r"everything here starts off", re.I),
    re.compile(r"bot_guilds=off \.\.\. bot_voices=off living=off", re.I),
]


class LivingServerOnByDefaultText(unittest.TestCase):
    def test_the_features_the_text_names_ship_on(self):
        # The text below is true only while these ship on; if one is turned off by default again, the text must change too.
        for key in ("bot_guilds_enabled", "bot_personalities_enabled", "bot_voices_enabled", "bot_voices_debates",
                    "bot_living_memory", "bot_living_grats", "bot_living_firsts", "bot_living_nemesis", "bot_living_breaks",
                    "bot_living_body_language"):
            self.assertEqual(SETTINGS[key], "True", key)
        self.assertEqual(SETTINGS["bot_schedule_mode"], "clock")

    def test_no_text_says_the_features_start_off(self):
        for path in (INSTALL, PACKAGE_README, TAB):
            text = path.read_text(encoding="utf-8")
            for pattern in STALE:
                self.assertIsNone(pattern.search(text), f"{path.relative_to(REPO)}: {pattern.pattern}")

    def test_the_installer_ends_by_saying_they_are_on_and_where_the_switches_are(self):
        lines = [line for line in INSTALL.read_text(encoding="utf-8").splitlines() if line.lstrip().startswith("Write-Host")]
        said = [line for line in lines if "by default" in line]
        self.assertEqual(len(said), 1, "one closing line about the defaults")
        self.assertRegex(said[0], r"Living Server features are ON by default")
        self.assertIn("Living Server tab", said[0])
        self.assertIn("diagnostics and test switches stay off", said[0])

    def test_the_package_readme_lists_them_as_on(self):
        text = PACKAGE_README.read_text(encoding="utf-8")
        self.assertIn("ON BY DEFAULT", text)
        self.assertIn("bot_guilds=on", text)

    def test_the_tab_says_they_are_on(self):
        self.assertIn("Everything here is on by default", TAB.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
