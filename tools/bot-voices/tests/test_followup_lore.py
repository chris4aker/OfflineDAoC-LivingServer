"""Chat follow-up F3 (1.2): every line file has an owner, the lore and tales files and the files that had no owner build
without a chat audit warning, and the lore fixes stay fixed (Evern and Juggernaut tell their story once and Evern does not
leap; five bosses whose scripts pick bots no longer say they never do).

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import json
import re
import subprocess
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import build_corpus  # noqa: E402
import chat_rules  # noqa: E402

BOSSES = chat_rules.SERVER / 'bots/autonomous/data/lore/bosses'
# The files F3 cleared: E6's lore and tales, and the three line files that had no owner.
F3_FILE_RE = re.compile(r'^(tales|tales_more|lore_[a-z_]+|storylines_more|scenes_live)\.lines$|^source/server/GameServer/bots/'
                        r'autonomous/data/lore/')


def boss(name):
    return json.loads((BOSSES / f'{name}.json').read_text(encoding='utf-8'))


def all_text(value):
    if isinstance(value, str):
        return [value]
    if isinstance(value, dict):
        return [t for v in value.values() for t in all_text(v)]
    if isinstance(value, list):
        return [t for v in value for t in all_text(v)]
    return []


def words(text):
    return set(re.findall(r"[a-z']+", text.lower()))


class Owners(unittest.TestCase):
    def test_every_line_file_has_an_owner(self):
        unowned = [p.name for p in sorted(build_corpus.CHAT.glob('*.lines')) if chat_rules.owner_of(p.name) == 'unowned']
        self.assertEqual(unowned, [])

    def test_the_files_added_after_the_audit(self):
        self.assertEqual(chat_rules.owner_of('tales_more.lines:12'), 'E6')
        self.assertEqual(chat_rules.owner_of('storylines_more.lines:12'), 'E4')
        self.assertEqual(chat_rules.owner_of('scenes_live.lines:12'), 'E8')
        self.assertEqual(chat_rules.owner_of('tales.lines:1'), 'E6')


class NoWarnings(unittest.TestCase):
    def test_lore_tales_and_formerly_unowned_files_build_clean(self):
        out = subprocess.run([sys.executable, str(HERE.parent / 'build_corpus.py'), '--check', '--warnings'],
                             capture_output=True, text=True, encoding='utf-8').stdout
        hits = [line.strip() for line in out.splitlines()
                if line.startswith('    ') and F3_FILE_RE.match(line.strip().split(':', 1)[0].split(' ', 1)[0])]
        self.assertEqual(hits, [], '\n'.join(hits[:20]))


class StoryOnce(unittest.TestCase):
    def test_evern_and_juggernaut_tell_their_story_once(self):
        for name in ('evern', 'juggernaut'):
            paragraphs = [p for p in boss(name)['story'].split('\n\n') if p.strip()]
            firsts = [re.split(r'(?<=[.!?])\s', p.strip())[0] for p in paragraphs]
            self.assertEqual(len(firsts), len(set(firsts)), name)
            for i, a in enumerate(paragraphs):
                for b in paragraphs[i + 1:]:
                    shared = len(words(a) & words(b)) / min(len(words(a)), len(words(b)))
                    self.assertLess(shared, 0.6, f'{name}: two paragraphs tell the same thing')

    def test_juggernaut_says_each_thing_once(self):
        j = boss('juggernaut')
        self.assertEqual(j['legend'].count('broken trees'), 1)
        self.assertEqual(len(j['yells']['aggro']), len(set(j['yells']['aggro'])))

    def test_evern_does_not_leap(self):
        # Evern.cs has no leap or jump: the one move its catalog lists (EvernFairyBrain.IsAtPillar) is a fairy flying
        # to a stone.
        script = (chat_rules.SERVER / 'scripts/namedmobs/Evern.cs').read_text(encoding='utf-8-sig')
        self.assertNotIn('Leap', script)
        for text in all_text(boss('evern')):
            self.assertIsNone(re.search(r'\b(leaps?|leapt|jumps?|jumped)\b', text, re.IGNORECASE), text)


class BotsArePicked(unittest.TestCase):
    # Each of these scripts picks its victims with EncounterTargets.InRadius (bots in the pool), or with
    # DisplacesBots(Body) where the encounter's bot displacement ships on (ShadeOfAelfgar, Krackenschtein).
    SCRIPTS = {
        'grand-summoner-govannon': 'SummonersHall/GrandSummonerGovannon.cs',
        'mistress-of-runes': 'Dodens/MinstressofRunes.cs',
        'morgus-urgalorg': 'Krondon/MorgusUrgalorg .cs',
        'krackenschtein': 'Fomor/Krackenschtein.cs',
        'elder-council-guthlac': 'TuscarianGlacier/ElderCouncilBirghirAndGuthlac.cs',
    }
    CLAIM = re.compile(r'real player|only the human|only human|never (picked|chosen|touches the bots|bots)|bots are never|'
                       r'gamebots are never', re.IGNORECASE)

    def test_the_lore_does_not_say_bots_are_never_picked(self):
        for name, script in self.SCRIPTS.items():
            source = (chat_rules.SERVER / 'scripts/namedmobs' / script).read_text(encoding='utf-8-sig')
            self.assertIn('EncounterTargets.InRadius', source, script)
            for text in all_text(boss(name)):
                self.assertIsNone(self.CLAIM.search(text), f'{name}: {text}')

    def test_the_two_throws_ship_with_bots_in_the_pool(self):
        mechanics = (chat_rules.SERVER / 'scripts/namedmobs/EpicEncounterMechanics.cs').read_text(encoding='utf-8-sig')
        for encounter in ('ShadeOfAelfgar', 'Krackenschtein'):
            self.assertIn(f'Ship(nameof(AllowBotDisplacement), "{encounter}", true)', mechanics)


if __name__ == '__main__':
    unittest.main()
