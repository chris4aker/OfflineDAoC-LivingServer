"""Chat 1.2 follow-up (F1): the chat string literals in code (check_runtime_names.py) are clean before the code check becomes
strict. Word lists the code reads in player text carry "// chat-check: input"; a bot line that keeps a word the corpus would
allow with @allow carries "// chat-check: allow <id> (why)".

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import check_runtime_names  # noqa: E402


class CodeLiterals(unittest.TestCase):
    def test_no_warnings_left_in_code(self):
        problems = []
        check_runtime_names.check_code_lines(problems, show=False)
        self.assertEqual(problems, [])
        warnings = []
        for where, owner, text, allowed in check_runtime_names.chat_literals():
            said = check_runtime_names.re.sub(r'\{[^{}]*\}', ' ', text)
            for entry in check_runtime_names.BANNED_ENTRIES:
                if entry['id'] == 'url' and 'GoldSpammers' in where or entry['id'] in allowed:
                    continue
                if entry['regex'].search(said):
                    warnings.append(f'{where} {entry["id"]}: {text}')
                    break
        self.assertEqual(warnings, [], 'mark word lists "// chat-check: input" or fix the bot line')

    def test_allow_tag_names_ids_and_a_reason(self):
        m = check_runtime_names.ALLOW_RE.search('    ["ml"] = "x",   // chat-check: allow ml, master-level, atlantis (the rumour answer)')
        self.assertEqual({a.strip() for a in m.group(1).split(',')}, {'ml', 'master-level', 'atlantis'})
        self.assertEqual(check_runtime_names.ALLOW_RE.search('// chat-check: allow minotaur').group(1), 'minotaur')
        self.assertIsNone(check_runtime_names.ALLOW_RE.search('// chat-check: input'))


if __name__ == '__main__':
    unittest.main()
