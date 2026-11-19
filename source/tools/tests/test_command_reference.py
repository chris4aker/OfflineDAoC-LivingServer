"""ALL SERVER COMMANDS.txt is written from the server's command registrations (chat audit 2026-11-18, TEXT-045: it had been
written once from the 0.3 assemblies and listed none of /hire, /lswhy, /personality, /invites, /lead, /lore, /tradewindow,
/lstest or /lscraft).

    python -m unittest discover -s source/tools/tests -v
"""
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import make_command_reference as reference  # noqa: E402


class CommandReference(unittest.TestCase):
    def test_the_file_is_up_to_date(self):
        expected = reference.render(reference.commands())
        current = reference.OUT.read_text(encoding="utf-8").replace("\r\n", "\n")
        self.assertEqual(current, expected, "run python3 source/tools/make_command_reference.py")

    def test_the_living_server_commands_are_listed(self):
        names = {c.name for c in reference.commands()}
        for name in ("hire", "lswhy", "personality", "invites", "lead", "lore", "tradewindow", "lstest", "lscraft", "spawn",
                     "faction", "release", "train", "team", "account"):
            self.assertIn(name, names, name)

    def test_aliases_headers_and_levels_are_read(self):
        by_name = {c.name: c for c in reference.commands()}
        self.assertEqual(by_name["release"].aliases, ["rel"], "a collection-expression alias list")
        self.assertEqual(by_name["team"].level, "GM", "a header key before the level")
        self.assertEqual(by_name["account"].level, "Admin")
        self.assertEqual(by_name["hire"].handler, "DOL.GS.Commands.HireCommand165Handler")


if __name__ == "__main__":
    unittest.main()
