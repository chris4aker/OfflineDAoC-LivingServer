"""Preview how bots would say corpus lines: fills slots with sample values and applies the voices.

  python tools/bot-voices/preview.py [count] [seed]

It reads corpus.json and voices.json and prints sample lines and one debate scene per run. The styles are the server's
(voice_style.py ports AutonomousBotVoiceCorpus.Style); the per-bot typing habits (AutonomousChatIdiolect) are left out.
docs/bot-voices/SAMPLE.md is made with it.
"""
import json
import random
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHAT = ROOT / 'source/server/GameServer/bots/autonomous/data/chat'
SAMPLE = {
    'mid': dict(realm='Midgard', capital='Jordheim', enemy='albs', enemy_mod='alb', enemy_realm='Albion', zone='Vale of Mularn', camp='Spindelhalla',
                dungeon='Vendo Caverns', keep='Bledmeer Faste', frontier="Odin's Gate", monster='spindel silkster',
                classes=['Healer', 'Shaman', 'Thane', 'Berserker', 'Runemaster', 'Skald'], player_class='Healer'),
    'alb': dict(realm='Albion', capital='Camelot', enemy='hibs', enemy_mod='hib', enemy_realm='Hibernia', zone='Salisbury Plains', camp='Stonehenge Barrows',
                dungeon='Tomb of Mithra', keep='Caer Benowyc', frontier="Hadrian's Wall", monster='brownie nomad',
                classes=['Cleric', 'Armsman', 'Wizard', 'Minstrel', 'Friar'], player_class='Cleric'),
    'hib': dict(realm='Hibernia', capital='Tir na Nog', enemy='mids', enemy_mod='mid', enemy_realm='Midgard', zone='Lough Derg', camp='Spraggon Den',
                dungeon='Muire Tomb', keep='Dun Crauchon', frontier='Emain Macha', monster='pit spraggon',
                classes=['Druid', 'Bard', 'Hero', 'Eldritch', 'Enchanter'], player_class='Druid'),
}
TYPES = ['Leveler', 'Crafter', 'Socialite', 'Explorer', 'Daredevil', 'NoLifer']


def voice_apply(text, voice, rng, kind=None):
    """The server's writing style (voice_style.py, a port of AutonomousBotVoiceCorpus.Style; 1.2 chat audit DESIGN-032)."""
    import voice_style
    return voice_style.style(text, voice, rng, kind)


CLASS_REALM = {}


def fill(text, realm, rng, level, classes=None):
    s = SAMPLE[realm]
    allowed = [c for c in (classes or []) if CLASS_REALM.get(c) in (None, realm)]
    values = dict(me='Bot', cls=rng.choice(allowed or s['classes']), level=str(level), realm=s['realm'], zone=s['zone'],
                  monster=s['monster'], npc='the trainer', item='Studded Vambraces', count=str(rng.randint(2, 4)),
                  camp=s['camp'], dungeon=s['dungeon'], capital=s['capital'], enemy=s['enemy'], enemy_mod=s['enemy_mod'],
                  enemy_realm=s['enemy_realm'], keep=s['keep'],
                  frontier=s['frontier'], player='Chris', player_class=s['player_class'], place=s['camp'],
                  q=rng.choice(['', 'hey', 'so', 'random question but']))
    values['class'] = values.pop('cls')
    values.update(winner=s['enemy_realm'], loser=s['realm'], other='Brannoc', other_class='Hero', target='Chris',
                  monsters=s['monster'] + 's', destination=s['camp'], size='8', classes='healer, warrior, skald', keep=s['keep'])
    out = re.sub(r'\{(\w+)\}', lambda m: values.get(m.group(1), m.group(1)), text)
    return re.sub(r'\s+', ' ', out).strip()


def main():
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from build_corpus import CLASSES
    for realm, names in CLASSES.items():
        for name in names:
            CLASS_REALM[name] = realm
    count = int(sys.argv[1]) if len(sys.argv) > 1 else 25
    rng = random.Random(int(sys.argv[2]) if len(sys.argv) > 2 else 7)
    corpus = json.loads((CHAT / 'corpus.json').read_text(encoding='utf-8'))
    voices = json.loads((CHAT / 'voices.json').read_text(encoding='utf-8'))['voices']
    names, shares = list(voices), [v['share'] for v in voices.values()]
    for _ in range(count):
        realm = rng.choice(list(SAMPLE))
        line = rng.choice([x for x in corpus['lines'] if x['realm'] in ('*', realm) and
                           (not x['class'] or any(CLASS_REALM.get(c) == realm for c in x['class']))])
        level = rng.randint(*line['levels'])
        voice = rng.choices(names, shares)[0]
        print(f"[{SAMPLE[realm]['realm'][:3]} {line['kind']:7s} {voice:10s}] {voice_apply(fill(line['text'], realm, rng, level, line['class']), voices[voice], rng, line['kind'])}")
    scene = rng.choice(corpus['scenes'])
    realm = scene['realm'] if scene['realm'] != '*' else rng.choice(list(SAMPLE))
    print(f"\n-- debate scene: {scene['id']} ({SAMPLE[realm]['realm']}, /faction) --")
    cast = {}
    for beat in scene['beats']:
        if beat['role'] == '~':
            print(f"   ... quiet for {beat['pause']} seconds ...")
            continue
        if beat.get('optional') and rng.random() < 0.5:
            continue
        who = cast.setdefault(beat['role'], f"{rng.choice(SAMPLE[realm]['classes'])}{len(cast) + 1}")
        voice = rng.choices(names, shares)[0]
        print(f"({who}) <Faction> {voice_apply(fill(rng.choice(beat.get('variants') or [beat['text']]), realm, rng, 40), voices[voice], rng)}")


if __name__ == '__main__':
    main()
