"""Lore follow-up (1.2): boss and place lore says who a mechanic picks the way the script picks (bots included where
EncounterTargets puts them in the pool), and no lore story tells itself twice.

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import json
import re
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import chat_rules  # noqa: E402

LORE = chat_rules.SERVER / 'bots/autonomous/data/lore'
SCRIPTS = chat_rules.SERVER / 'scripts'

# Files whose "real player" is what the script does: the mechanic takes GamePlayer (GetPlayersInRadius or `source as
# GamePlayer`), so bots never set it off. Every other lore file must not say a mechanic picks real players only.
PLAYERS_ONLY = {
    'bosses/amminus-pilus': 'mobs/AlbionMobs/PilusFury.cs',             # GetPlayersInRadius + summoned pets
    'bosses/archilus': 'namedmobs/Archilus.cs',                        # TakeDamage: source as GamePlayer
    'bosses/elro-the-ancient': 'namedmobs/ElroTheAncient.cs',          # source as GamePlayer, GetPlayersInRadius(2500)
    'bosses/morgana': 'namedmobs/Albion Named Mobs/Classic Zones/MorganaBechardSilcharde.cs',  # GetPlayersInRadius (quest step)
    'locations/catacombs-of-cardova': 'mobs/AlbionMobs/PilusFury.cs',
    'locations/tomb-of-mithra': 'mobs/AlbionMobs/HighPriestAndania.cs',  # GetPlayersInRadius(500)
    'locations/hy-brasil': 'namedmobs/Archilus.cs',
    'locations/worlds-end': 'namedmobs/ElroTheAncient.cs',
}
# "Real player" said of something other than a mechanic's pick.
NOT_A_PICK = {
    'bosses/summoner-roesia',   # "a GameBot can be its centre as well as a real player"
    'locations/caledon',        # "the renegades there are tougher than real players"
}
CLAIM = re.compile(r'real player|only the human|bots?( \([^)]*\))? (are|is) never|gamebots are (never|not) (picked|chosen|set|thrown|moved|counted)|'
                   r'never (at )?a bot|bots never (get|fly|are|picked|thrown)|never bots|not bots|pet or bot', re.IGNORECASE)


def lore_files():
    for path in sorted(LORE.glob('*/*.json')):
        yield f'{path.parent.name}/{path.stem}', json.loads(path.read_text(encoding='utf-8'))


def texts(value, key=''):
    if isinstance(value, str):
        if not key.endswith(('source', 'provenance')):
            yield value
    elif isinstance(value, dict):
        for k, v in value.items():
            yield from texts(v, k)
    elif isinstance(value, list):
        for v in value:
            yield from texts(v, key)


def words(text):
    return re.findall(r"[a-z0-9']+", text.lower())


def trigrams(text):
    w = words(text)
    return {tuple(w[i:i + 3]) for i in range(len(w) - 2)}


class WhoIsPicked(unittest.TestCase):
    def test_only_player_only_mechanics_say_real_player(self):
        wrong = []
        for name, data in lore_files():
            if name in PLAYERS_ONLY or name in NOT_A_PICK:
                continue
            for text in texts(data):
                for sentence in re.split(r'(?<=[.;!?])\s+', text):
                    if CLAIM.search(sentence):
                        wrong.append(f'{name}: {sentence}')
        self.assertEqual(wrong, [], '\n'.join(wrong[:20]))

    def test_the_player_only_list_is_still_player_only(self):
        for name, script in PLAYERS_ONLY.items():
            source = (SCRIPTS / script).read_text(encoding='utf-8-sig')
            self.assertTrue('GetPlayersInRadius' in source or 'as GamePlayer' in source, script)
            self.assertTrue((LORE / f'{name}.json').exists(), name)

    def test_the_princes_eat_pets_never_bots(self):
        mechanics = (SCRIPTS / 'namedmobs/EpicEncounterMechanics.cs').read_text(encoding='utf-8-sig')
        self.assertRegex(mechanics, r'IsDevourablePet\(GameNPC npc\) =>\s*npc != null && npc is not GameBot')
        for name in ('bosses/prince-asmoien', 'bosses/prince-baalorien'):
            data = json.loads((LORE / f'{name}.json').read_text(encoding='utf-8'))
            for text in texts(data):
                self.assertNotRegex(text.lower(), r'eats the closest bot|dead bot and a big heal|candidates for the devour, not only pets', name)

    def test_switches_the_fixes_rely_on_ship_on(self):
        mechanics = (SCRIPTS / 'namedmobs/EpicEncounterMechanics.cs').read_text(encoding='utf-8-sig')
        for encounter in ('Anurigunda', 'Boligar', 'DraugynSphere', 'GlacierGiant', 'Hurika', 'Hakr', 'Jailer', 'KrevoRicik',
                          'LegendaryAfanc', 'MoranTheMighty', 'Mortufoghus', 'Orylle', 'LyftMihtOne', 'SummonerCunovinda',
                          'SyssroRuthless', 'Xanxicar', 'Ydenia'):
            self.assertIn(f'Ship(nameof(AllowBotDisplacement), "{encounter}", true)', mechanics, encounter)
        for encounter in ('Hurika', 'Hakr'):
            self.assertIn(f'Ship(nameof(AllowDisplacement), "{encounter}", true)', mechanics, encounter)


class StoriesTellOnce(unittest.TestCase):
    # A story's summary paragraph retold by the paragraphs after it (the pattern Evern, Juggernaut and 33 others had)
    # shares a fifth or more of its word trigrams with them; told once, no two paragraphs come near it.
    SHARE = 0.2

    def test_no_story_repeats_a_sentence(self):
        repeats = []
        for name, data in lore_files():
            story = data.get('story')
            if not isinstance(story, str):
                continue
            seen = set()
            for sentence in re.split(r'(?<=[.!?])\s+', story):
                key = ' '.join(words(sentence))
                if len(key.split()) >= 4 and key in seen:
                    repeats.append(f'{name}: {sentence}')
                seen.add(key)
        self.assertEqual(repeats, [], '\n'.join(repeats))

    def test_no_story_retells_a_paragraph(self):
        retold = []
        for name, data in lore_files():
            story = data.get('story')
            if not isinstance(story, str):
                continue
            paragraphs = [p for p in story.split('\n\n') if p.strip()]
            grams = [trigrams(p) for p in paragraphs]
            for i in range(len(paragraphs)):
                for j in range(i + 1, len(paragraphs)):
                    shared = len(grams[i] & grams[j]) / max(1, min(len(grams[i]), len(grams[j])))
                    if shared >= self.SHARE:
                        retold.append(f'{name}: paragraphs {i} and {j} share {shared:.0%}')
        self.assertEqual(retold, [], '\n'.join(retold))


if __name__ == '__main__':
    unittest.main()
