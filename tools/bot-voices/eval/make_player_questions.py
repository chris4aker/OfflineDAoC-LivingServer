#!/usr/bin/env python3
"""Writes player_questions.json, the question bench of the 1.2.2 chat audit (docs/bot-voices/CHAT-AUDIT-1.2.2.md).

Each question is a line a 1.65-era player types (in /faction mostly, some in /say, /group and by tell), tagged with its intent
and with the facts a good answer must hold, taken from the repo's own data, never from memory:

  * place levels: data/chat/places.json (tools/bot-voices/place_levels.py measured them from the world database)
  * dungeons for a level: AutonomousBotVoices.DungeonFor (DUNGEON_TABLE below is a copy; the bench reports where it and
    places.json disagree)
  * capitals, trainers, dragons, realm ranks, battleground brackets, chat terms, commands: the server code named beside each
  * the stub world the bench runs against (WORLD below): who holds Darkness Falls, the relics and three keeps

Fields of a question:
  id, text, channel (faction/say/group/tell), realm (alb/mid/hib, the asker's), cls, level (the asker's), intent,
  must_any: a good answer holds one of these (a plain string matches as whole words, case and apostrophes ignored;
            "re:" starts a regular expression),
  wrong_any: an answer holding one of these says something false,
  silence_ok: nobody answering is fine (a lone "lol" in /faction),
  to: the bot the line is said to (tells and named lines), source: where the facts come from.

Deterministic: python3 tools/bot-voices/eval/make_player_questions.py [--check]
"""
import json
import random
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PLACES = ROOT / 'source/server/GameServer/bots/autonomous/data/chat/places.json'
OUT = HERE / 'player_questions.json'
HOLDOUT = HERE / 'player_questions_holdout.json'

REALMS = ('alb', 'mid', 'hib')
REALM_NAME = {'alb': 'Albion', 'mid': 'Midgard', 'hib': 'Hibernia'}
# AutonomousChatFieldTalk.CapitalWord
CAPITAL = {'alb': 'camelot', 'mid': 'jordheim', 'hib': 'tir na nog'}
CLASSES = {
    'alb': ['Armsman', 'Paladin', 'Mercenary', 'Reaver', 'Cleric', 'Friar', 'Minstrel', 'Scout', 'Infiltrator', 'Sorcerer', 'Wizard',
            'Theurgist', 'Cabalist', 'Necromancer'],
    'mid': ['Warrior', 'Thane', 'Skald', 'Berserker', 'Savage', 'Hunter', 'Shadowblade', 'Healer', 'Shaman', 'Runemaster',
            'Spiritmaster', 'Bonedancer'],
    'hib': ['Hero', 'Champion', 'Blademaster', 'Ranger', 'Nightshade', 'Druid', 'Warden', 'Bard', 'Eldritch', 'Enchanter',
            'Mentalist', 'Animist', 'Valewalker'],
}
# AutonomousBotVoices.DungeonFor (copy; the bench compares it with places.json)
DUNGEON_TABLE = {
    'alb': [("Tomb of Mithra", 7, 17), ("Tepok's Mine", 16, 24), ("Keltoi Fogou", 19, 32), ("Stonehenge Barrows", 32, 50)],
    'mid': [("Nisse's Lair", 8, 21), ("Cursed Tomb", 18, 28), ("Vendo Caverns", 20, 35), ("Varulvhamn", 25, 40), ("Spindelhalla", 33, 50)],
    'hib': [("Muire Tomb", 12, 20), ("Spraggon Den", 15, 25), ("Koalinth Tribal Caverns", 20, 30), ("Treibh Caillte", 27, 40),
            ("Coruscating Mine", 30, 45)],
}
# AutonomousChatKnowledge.TryAnswerDragon
DRAGON = {'alb': ('Golestandt', 'Dartmoor'), 'mid': ('Gjalpinulva', 'Malmohus'), 'hib': ('Cuuldurach', 'Sheeroe Hills')}
# GamePlayer.REALMPOINTS_FOR_LEVEL at (rr - 1) * 10
RR = {2: 7125, 3: 61750, 4: 213875, 5: 513500, 6: 1010625, 7: 1755250, 8: 2797375, 9: 4187000, 10: 5974125}
# The stub world (also read by UT_ChatQuestionBench): Hibernia holds Darkness Falls; Albion holds Midgard's Relic of Thor.
WORLD = {
    'df_owner': 'hib',
    'relics': [{'name': "Hibernia's Relic of Lugh", 'owner': 'hib', 'home': 'hib'},
               {'name': "Midgard's Relic of Thor", 'owner': 'alb', 'home': 'mid'},
               {'name': "Midgard's Relic of Odin", 'owner': 'mid', 'home': 'mid'},
               {'name': "Albion's Relic of Arthur", 'owner': 'alb', 'home': 'alb'},
               {'name': "Albion's Relic of Merlin", 'owner': 'alb', 'home': 'alb'},
               {'name': "Hibernia's Relic of Nuada", 'owner': 'hib', 'home': 'hib'}],
    'keeps': [{'name': 'Bledmeer Faste', 'owner': 'mid'}, {'name': 'Caer Benowyc', 'owner': 'alb'}, {'name': 'Dun Crauchon', 'owner': 'hib'},
              {'name': 'Nottmoor Faste', 'owner': 'alb'}, {'name': 'Caer Erasleigh', 'owner': 'alb'}, {'name': 'Dun Crimthainn', 'owner': 'hib'}],
    # 1.2.3: towns of the world (area circles; the zone each stands in, from the world database's Area and Zones tables), which the
    # chat's places catalog doesn't hold: "where is Humberton Castle" in a bot-led group drew "not sure tbh".
    'towns': [{'name': 'Humberton Castle', 'zone': 'Black Mtns. South', 'realm': 'alb'},
              {'name': 'Cotswold Village', 'zone': 'Camelot Hills', 'realm': 'alb'},
              {'name': 'Ludlow Village', 'zone': 'Black Mtns. South', 'realm': 'alb'},
              {'name': 'Haggerfel', 'zone': 'Vale of Mularn', 'realm': 'mid'},
              {'name': 'Fort Atla', 'zone': 'Gotar', 'realm': 'mid'},
              {'name': 'Ardee', 'zone': 'Connacht', 'realm': 'hib'},
              {'name': 'Tir na mBeo', 'zone': 'Lough Derg', 'realm': 'hib'}],
}
# The bot that answers in each realm (stub facts; UT_ChatQuestionBench builds its BotFacts from these).
RESPONDERS = {
    'alb': {'id': 701, 'name': 'Aldwyn', 'cls': 'Cleric', 'level': 32, 'zone': 'Lyonesse', 'target': 'Lyonesse Lurker', 'spec': '33 rejuvenation, 24 enhancement'},
    'mid': {'id': 702, 'name': 'Brannoc', 'cls': 'Thane', 'level': 30, 'zone': 'Myrkwood Forest', 'target': 'Large Frost Giant', 'spec': '39 hammer, 30 shield'},
    'hib': {'id': 703, 'name': 'Siobhan', 'cls': 'Druid', 'level': 31, 'zone': 'Cliffs of Moher', 'target': 'Moher Ghoul', 'spec': '34 nurture, 26 regrowth'},
}

# Classes with no spells to train (1.65): their trainer trains styles and specs.
MELEE = {'Armsman', 'Mercenary', 'Scout', 'Infiltrator', 'Warrior', 'Berserker', 'Savage', 'Shadowblade', 'Hero', 'Blademaster'}
GREETING = r're:\b(hi|hey|heya|hello|hail|evening|evenin|morning|mornin|yo|sup|greetings|howdy|hiya|o/|well met|welcome)\b'
HOWAREYOU = r're:\b(good|fine|great|not bad|tired|slow|ok|okay|alright|well|grinding|busy|meh|decent|could be worse|cant complain|xp)\b'
THANKS = r're:\b(np|yw|welcome|anytime|no problem|no prob|sure thing|glad)\b'
FAREWELL = r're:\b(night|gn|gnight|nite|later|l8r|cya|bye|sleep well|take care|see ya|see you|o/)\b'


def load_places():
    return json.loads(PLACES.read_text(encoding='utf-8'))['places']


def names(place):
    out = {place['key']}
    out.update(a for a in place.get('aliases', []) if len(a) >= 4)
    return sorted(out)


def own(places, realm):
    return [p for p in places if p.get('realm') == realm and p.get('levels') and p.get('type') in ('zone', 'dungeon', 'town')]


def fits(place, level, slack=4):
    low, high = place['levels']
    return low - slack <= level <= high + slack


def far(place, level):
    low, high = place['levels']
    return high < level - 10 or low > level + 8


def level_facts(places, realm, level):
    good, bad = set(), set()
    for p in own(places, realm):
        (good if fits(p, level) else bad if far(p, level) else set()).update(names(p))
    for p in places:
        if p.get('realm') == '*' and p.get('levels') and p['type'] in ('dungeon', 'battleground'):
            if p['type'] == 'battleground':
                low, high = p['levels']
                (good if low <= level <= high else bad).update(names(p))
            elif fits(p, level, 2):
                good.update(names(p))
        elif p.get('realm') not in (realm, '*') and p.get('type') in ('zone', 'dungeon', 'town'):
            bad.update(names(p))
    # A name both good and bad (an alias two places share) says nothing either way.
    shared = good & bad
    return sorted(good - shared), sorted(bad - shared)


class Bench:
    def __init__(self):
        self.items = []
        self.seen = set()

    def add(self, text, intent, must, realm='mid', cls=None, level=None, channel='faction', wrong=None, silence_ok=False, to=None,
            source=None, note=None):
        key = (text.lower(), channel, realm)
        if key in self.seen:
            return
        self.seen.add(key)
        cls = cls or {'alb': 'Wizard', 'mid': 'Healer', 'hib': 'Ranger'}[realm]
        level = level if level is not None else 20
        item = {'id': f'q{len(self.items) + 1:03d}', 'text': text, 'channel': channel, 'realm': realm, 'cls': cls, 'level': level,
                'intent': intent, 'must_any': list(must)}
        if wrong:
            item['wrong_any'] = list(wrong)
        if silence_ok:
            item['silence_ok'] = True
        if to:
            item['to'] = to
        if source:
            item['source'] = source
        if note:
            item['note'] = note
        self.items.append(item)


def build():
    rnd = random.Random(1222)
    places = load_places()
    b = Bench()
    by_key = {p['key']: p for p in places}

    # 1. Where to level (each realm, levels 3-48, varied phrasing and classes).
    phrasings = ['where should i level at {lvl}?', 'good place to lvl at {lvl}?', 'where do {lvl}s hunt?', 'any good spots for a {lvl} {cls}?',
                 'where can a {lvl} {cls} solo?', 'lvl {lvl} where to xp?', 'best xp for {lvl}?', 'im {lvl}, where should i go now?',
                 'where to level {lvl}-{lvl2}?', 'good camp for {lvl}?', 'where do i go after {lvl}?', 'anyone know a good spot for {lvl}?']
    for realm in REALMS:
        for i, lvl in enumerate([3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 40, 44, 48]):
            cls = CLASSES[realm][(i * 5) % len(CLASSES[realm])]
            text = phrasings[(i + REALMS.index(realm) * 4) % len(phrasings)].format(lvl=lvl, lvl2=lvl + 5, cls=cls.lower())
            good, bad = level_facts(places, realm, lvl)
            b.add(text, 'level_where', good, realm, cls, lvl, wrong=bad, source='places.json levels (+-4)')

    # 2. A place's levels.
    level_q = ['what lvl is {p}?', 'what level is {p}', '{p} what lvl?', 'is {p} ok at {lvl}?', 'can a {lvl} solo in {p}?',
               'whats the level range for {p}?']
    picks = [p for p in places if p.get('levels') and p['type'] in ('dungeon', 'zone') and p['realm'] in REALMS]
    rnd.shuffle(picks)
    for i, p in enumerate(picks[:36]):
        realm = p['realm']
        low, high = p['levels']
        form = level_q[i % len(level_q)]
        lvl = rnd.choice([low - 6, low, (low + high) // 2, high + 6])
        lvl = max(2, min(50, lvl))
        alias = rnd.choice(names(p))
        text = form.format(p=alias, lvl=lvl)
        # AutonomousChatQuestions.Range: "low-high", or "low+" (low at most 50) when the top is past 50.
        must = [rf're:\b{min(low, 50)}\+' if high > 50 else rf're:\b{low}\b.{{0,12}}\b{high}\b']
        wrong = None
        if '{lvl}' in form:
            if lvl < low - 2:
                wrong = [r're:\b(is fine there|should be ok|fine at|yes|yep)\b']
            elif lvl > high + 3:
                wrong = [r're:\b(a bit high|too high)\b']
        b.add(text, 'place_level', must, realm, level=lvl, wrong=wrong, source=f'places.json {p["key"]} {low}-{high}')
    for bg, (low, high) in {'thidranki': (20, 24), 'abermenai': (15, 19), 'murdaigean': (25, 29), 'caledonia': (30, 35)}.items():
        for realm, lvl in (('alb', low + 1), ('mid', high + 2), ('hib', low - 1)):
            yes = low <= lvl <= high
            word = {'thidranki': 'thid', 'murdaigean': 'murd', 'caledonia': 'cale'}.get(bg, bg)
            text = rnd.choice(['is {b} open for lvl {l}?', 'can i go to {b} at {l}?', 'what lvl for {b}?']).format(b=word, l=lvl)
            if 'what lvl' in text:
                must = [rf're:\b{low}\b.{{0,4}}\b{high}\b']
            else:
                must = [r're:\b(yes|yep|yeah|sure)\b'] if yes else [r're:\b(no|nope|cant|too (low|high))\b', rf're:\b{low}\b.{{0,4}}\b{high}\b']
            b.add(text, 'place_level', must, realm, level=lvl, source=f'BattlegroundBrackets {bg} {low}-{high}')

    # 3. Where to buy things.
    buys = [
        ('where can i buy a better sword?', ['exchange', 'weaponcrafter', 'weaponsmith', 'weapon merchant', 'weapons merchant', 'smith']),
        ('where do i buy arrows', ['fletcher', 'arrow merchant', 'merchant', 'exchange']),
        ('where can i get a new staff for my caster?', ['fletcher', 'exchange', 'staff merchant', 'merchant']),
        ('who sells horse tickets?', ['stable master', 'stable', 'stablemaster']),
        ('where do you buy potions?', ['alchemist', 'merchant', 'exchange', 'potion merchant']),
        ('where can i buy dyes', ['dye merchant', 'dye', 'merchant', 'tailor']),
        ('where do i get spellcraft gems?', ['spellcrafter', 'spellcraft', 'gem merchant', 'exchange', 'merchant']),
        ('where can i buy better armor at 20?', ['armorcrafter', 'armorsmith', 'tailor', 'exchange', 'armor merchant', 'merchant']),
        ('anyone sell chain armor?', ['armorcrafter', 'exchange', 'armorsmith', 'merchant']),
        ('where can i buy poison?', ['poison merchant', 'poison', 'merchant', 'alchemist']),
        ('where do i get crafting tools', ['merchant', 'crafting merchant', 'tools', 'trainer']),
        ('where to buy a shield?', ['armorcrafter', 'exchange', 'merchant', 'shield']),
        ('where can i get a cloak', ['tailor', 'exchange', 'merchant', 'drops', 'drop']),
        ('where do i buy food for my horse lol', ['re:\\b(no|dont|don t|doesnt|you dont|not a thing|lol)\\b'], ),
        ('where do i get a better bow?', ['fletcher', 'exchange', 'bowyer', 'merchant']),
        ('who sells rings and necklaces?', ['drops', 'drop', 'exchange', 'jewel']),
        ('where can i buy a robe', ['tailor', 'exchange', 'merchant']),
        ('where do i buy bags?', ['merchant', 'tailor', 'exchange', 'bag']),
        ('where do i sell my loot?', ['merchant', 'vendor', 'exchange', 'any merchant']),
        ('wheres the realm exchange?', ['re:\\b(camelot|jordheim|tir na nog)\\b', 'capital']),
    ]
    for i, (text, must) in enumerate(buys):
        realm = REALMS[i % 3]
        b.add(text, 'buy_where', must, realm, level=10 + (i * 3) % 35, source='AutonomousChatKnowledge.TryAnswerBuyGear; 1.65 merchants')
    for realm in REALMS:
        b.add(f'where is the realm exchange in {CAPITAL[realm]}?', 'buy_where', [CAPITAL[realm], 'capital', 'center', 'middle', 'near'], realm,
              source='Realm Exchange (capital)')

    # 4. How to get somewhere (own realm, the other realms, the frontier).
    travel_q = ['how do i get to {p}?', 'where is {p}?', 'how to get to {p}', 'wheres {p}', 'how far is {p}?']
    for realm in REALMS:
        mine = [p for p in places if p.get('realm') == realm and p['type'] in ('dungeon', 'zone', 'town', 'capital')]
        rnd.shuffle(mine)
        for i, p in enumerate(mine[:9]):
            zones = [z for z in p.get('zones', []) if z.lower() != p['key']]
            must = names(p) + zones + ['stable', 'horse', 'ticket', 'road']
            b.add(travel_q[i % len(travel_q)].format(p=rnd.choice(names(p))), 'travel', must, realm, level=p.get('levels', [20, 20])[0] or 20,
                  wrong=[r're:\b(cant|can t|enemy|other realm|their side)\b'], source='places.json zones; AutonomousChatKnowledge.TryAnswerLocation')
        other = [p for p in places if p.get('realm') not in (realm, '*') and p['type'] in ('dungeon', 'town', 'capital')]
        rnd.shuffle(other)
        for i, p in enumerate(other[:3]):
            b.add(travel_q[i % 2].format(p=rnd.choice(names(p))), 'travel',
                  [r're:\b(cant|can t|enemy|other realm|their|theirs|not ours|rvr|frontier|albion|midgard|hibernia|alb|mid|hib)\b'], realm,
                  source='AutonomousChatKnowledge.OtherRealmLine')
    for realm in REALMS:
        other = [r for r in REALMS if r != realm][0]
        b.add(f'how do i get to {REALM_NAME[other].lower()} from {REALM_NAME[realm].lower()}?', 'travel',
              [r're:\b(cant|can t|enemy|enemies|frontier|rvr|kill)\b'], realm, source='AutonomousChatKnowledge.TryAnswerHowTo')
        b.add('how do i get into df?', 'travel', [r're:\b(frontier|portal|keep|keeps|teleport|ours|own|hold|hibs|albs|mids|we have|we dont)\b'],
              realm, level=40, source='Darkness Falls: the realm with most keeps holds the way in')
        b.add('how do i get to the frontier?', 'travel', [r're:\b(portal|teleport|porter|keep|horse|stable|border|teleporter)\b'], realm,
              level=25, source='1.65 frontier portals')
        b.add('how do i get to the battlegrounds?', 'travel', [r're:\b(portal|teleport|porter|keep|border|teleporter)\b'], realm, level=22,
              source='battleground portals at the portal keeps')

    # 5. Trainers.
    for realm in REALMS:
        for i, cls in enumerate(CLASSES[realm]):
            if i % 2:
                continue
            asker = CLASSES[realm][(i + 3) % len(CLASSES[realm])] if i % 4 else cls
            form = ['where is the {c} trainer?', 'where do i train as a {c}?', 'who trains {p}?', 'wheres my trainer?'][i % 4]
            plural = cls.lower()[:-1] + 'ies' if cls.endswith('y') else cls.lower() + 's'
            text = form.format(c=cls.lower(), p=plural)
            if form.startswith('wheres my'):
                asker = cls
            towns = [p['key'] for p in places if p.get('realm') == realm and p['type'] in ('town', 'capital')]
            wrong = [CAPITAL[r] for r in REALMS if r != realm]
            if asker != cls:
                # Another class's trainer is not "your" trainer.
                wrong.append(rf're:\b(your|ur) {cls.lower()} trainer')
            if cls in MELEE:
                wrong.append(r're:\bspells?\b')
            b.add(text, 'trainer', [CAPITAL[realm], 'capital'] + towns, realm, asker, 5 + i * 3,
                  wrong=wrong, source='AutonomousChatKnowledge.GenericTrainerAnswer (every trainer in the capital)')
        foreign = CLASSES[[r for r in REALMS if r != realm][1]][2]
        b.add(f'where is the {foreign.lower()} trainer?', 'trainer',
              [r're:\b(not ours|their|theirs|other realm|cant|can t|albion|midgard|hibernia|alb|mid|hib|enemy)\b'], realm,
              wrong=[CAPITAL[realm]], source='a class of another realm trains there')
        b.add('where do i get my spells trained?', 'trainer', [CAPITAL[realm], 'trainer', 'capital'], realm, CLASSES[realm][7], 12,
              source='AutonomousChatKnowledge.AsksWhereToTrain')
        b.add('how do i spend my spec points?', 'trainer', ['trainer'], realm, level=6, source='AutonomousChatKnowledge.TryAnswerHowTo')

    # 6. Quests and the epic.
    quests = [
        ('where do i hand in this quest?', ['journal', 'quest giver', 'whoever gave', 'gave it', 'npc']),
        ('how do i turn in a quest', ['journal', 'quest giver', 'whoever gave', 'gave it', 'npc']),
        ('where do i finish my quest? cant find the npc', ['journal', 'quest giver', 'gave it', 'npc', 'log']),
        ('any good quests at 15?', [r're:\b(quest|quests|town|npc|guard|kill task|task)\b']),
        ('how do i get kill tasks?', ['guard', 'town', 'npc', 'task']),
        ('how do i start my epic?', ['trainer', '50', '45', 'chain']),
        ('when can i do my epic quest?', ['50', '45', 'trainer']),
        ('is the epic worth doing?', ['re:\\b(yes|yeah|yep|armor|worth|long|chain|group)\\b']),
        ('how long is the epic?', ['long', 'chain', 'steps', 're:\\b(hours|days|forever|while)\\b']),
        ('do i need a group for my epic?', ['group', 'grp', 're:\\b(yes|yeah|yep|some parts|help)\\b']),
        ('what level for the epic armor?', ['50', '45']),
        ('where do i get my epic armor?', ['epic', 'trainer', 'quest', 'chain']),
    ]
    for i, (text, must) in enumerate(quests):
        realm = REALMS[i % 3]
        b.add(text, 'quest' if 'epic' not in text else 'epic', must, realm, level=15 if 'epic' not in text else 46,
              source='AutonomousChatKnowledge.TryAnswerCommon')

    # 7. Raids, dragons, big bosses and lore.
    for realm in REALMS:
        name, zone = DRAGON[realm]
        others = [DRAGON[r][0] for r in REALMS if r != realm]
        b.add('where is the dragon?', 'raid', [name, zone], realm, level=48, wrong=others, source='AutonomousChatKnowledge.TryAnswerDragon')
        b.add(f'is there a dragon in {REALM_NAME[realm].lower()}?', 'raid', [name, zone], realm, level=40, wrong=others,
              source='AutonomousChatKnowledge.TryAnswerDragon')
        b.add('any raids going tonight?', 'raid', [r're:\b(raid|nothing running|gathering|underway|signed up|50s)\b'], realm, level=50,
              source='AutonomousChatKnowledge.TryAnswerRealmRaid (stub: no raid running)')
        b.add('when is the next dragon raid?', 'raid', [r're:\b(raid|nothing running|gathering|called|50s|enough)\b', name], realm, level=50,
              source='AutonomousChatKnowledge.TryAnswerRealmRaid')
        b.add('how long does it take to hit 50?', 'raid', [r're:\b(weeks?|days?|played|hours)\b'], realm, level=12,
              source='AutonomousChatKnowledge.TryAnswerTimeToCap')
    lore_q = [('alb', 'whats in krondon?', ['krondon', 'stone', 'earth', 'deep', 'below', 're:\\b(golem|elemental|mine|dwarf|giant|stone)']),
              ('alb', 'who is legion?', ['legion', 'darkness falls', 'df', 'demon']),
              ('mid', 'what is tuscaran glacier?', ['tuscaran', 'glacier', 'ice', 'giant', 'frost', 'cold']),
              ('hib', 'whats galladoria?', ['galladoria', 'water', 'lake', 'sunken', 'deep']),
              ('alb', 'what is caer sidi?', ['sidi', 'caer sidi', 'undead', 'tower', 'dead', 'lich']),
              ('mid', 'what is atlantis?', ['atlantis', 'sea', 'rumour', 'rumor', 'sunk', 'legend', 'story']),
              ('hib', 'who is balor?', ['balor', 'eye', 'fomor', 'fomorian', 'giant']),
              ('alb', 'what happened to arthur?', ['arthur', 'king', 'avalon', 'camlann', 'mordred', 'sleep']),
              ('mid', 'who is the storm keeper?', ['storm', 'keeper']),
              ('hib', 'what is in the cursed forest?', ['cursed forest', 'forest', 'cursed', 'dead', 'trees']),
              ('mid', 'whats up with trollheim?', ['troll', 'trolls', 'trollheim']),
              ('alb', 'is there anything under camelot?', ['camelot', 'below', 'under', 'catacomb', 'rumour', 'rumor', 'tunnel', 'sewer', 'story'])]
    for realm, text, must in lore_q:
        b.add(text, 'lore', must, realm, level=35, source='LoreChat / LoreLegends (data/lore)')
    big = [('mid', 'what level is darkness falls?', [r're:\b29\b.{0,6}50\+?|\b29\b.{0,8}\b57\b|\b(30|35|40)s?\b']),
           ('hib', 'what lvl is galladoria?', [r're:\b50\+|\b75\b|\braid\b|\b50s?\b']),
           ('alb', 'what lvl is caer sidi?', [r're:\b50\+|\b75\b|\braid\b|\b50s?\b']),
           ('mid', 'what lvl is tuscaran?', [r're:\b50\+|\b66\b|\braid\b|\b50s?\b']),
           ('alb', 'can we kill legion?', [r're:\b(raid|50s|big group|full group|lots of|everyone)\b']),
           ('hib', 'anyone want to do galladoria?', [r're:\b(raid|50s|im in|sure|count me|me|not me|too low|cant)\b'])]
    for realm, text, must in big:
        b.add(text, 'raid', must, realm, level=50, source='places.json levels; raid content')

    # 8. RvR status (against the stub world).
    for realm in REALMS:
        mine = [r for r in WORLD['relics'] if r['home'] == realm]
        lost = [r for r in mine if r['owner'] != realm]
        must = [r're:^(no|nope)\b|\b(lost|they have|the (albs|mids|hibs) have)\b'] if lost else [r're:\b(yes|yep|yeah|still have|home|ours|both)\b']
        wrong = [r're:\b(yep|yes|still have|both of ours are home)\b'] if lost else [r're:\b(lost|they have)\b']
        b.add('do we still have our relics?', 'rvr_status', must, realm, level=50, wrong=wrong, source='stub world relics')
        b.add('who has our relics?', 'rvr_status', must, realm, level=40, wrong=wrong, source='stub world relics')
        df_ours = WORLD['df_owner'] == realm
        b.add('is df ours?', 'rvr_status', [r're:\b(yes|yep|yeah|ours|we have)\b'] if df_ours else [r're:\b(no|nope|have it|has df|albs|mids|hibs|albion|midgard|hibernia)\b'],
              realm, level=40, wrong=[r're:\b(no|nope)\b'] if df_ours else [r're:\b(yep|yeah|ours)\b'], source='stub world: Hibernia holds DF')
        b.add('who has darkness falls right now?', 'rvr_status', [r're:\b(hibs|hibernia|hib|ours|we have)\b'], realm, level=40,
              wrong=[r're:\b(albs|albion|mids|midgard)\b'] if realm == 'hib' else None, source='stub world: Hibernia holds DF')
        for keep in WORLD['keeps']:
            if keep['owner'] == realm or keep['name'] in ('Caer Benowyc', 'Bledmeer Faste', 'Dun Crauchon'):
                key = keep['name'].split()[1 if keep['name'].startswith(('Caer', 'Dun')) else 0].lower()
                ours = keep['owner'] == realm
                b.add(f'do we still own {key}?', 'rvr_status',
                      [r're:\b(yes|yep|yeah|is ours|still have)\b'] if ours else [r're:^(no|nope)\b|\b(albs|mids|hibs) have\b|\b(albion|midgard|hibernia) has\b'], realm, level=40,
                      wrong=[r're:\b(no|nope)\b'] if ours else [r're:\b(yep|ours|still have)\b'], source=f'stub world keeps: {keep["name"]} {keep["owner"]}')
        b.add('where are the enemies?', 'rvr_status', [r're:\b(emain|hadrian|odin|uppland|jamtland|breifine|pennine|cruachan|collory|snowdonia|sauvage|yggdra|quiet|nothing|none|seen|saw)\b'],
              realm, level=45, source='AutonomousChatLiveAnswers.EnemySighting (live news board)')
        b.add('is emain safe right now?' if realm == 'hib' else 'is the frontier busy?', 'rvr_status',
              [r're:\b(quiet|busy|zerg|safe|careful|enemies|nothing|seen|saw|yes|no|dead)\b'], realm, level=40, source='live sightings')
        b.add('any rvr groups going?', 'rvr_status', [r're:\b(rvr|group|grp|emain|frontier|keep|me|im in|not right now|nope|none|soon|later)\b'],
              realm, level=48, source='grouping')
        b.add('how do realm points work?', 'rvr_status', [r're:\b(kill|kills|enemy|players|keep|keeps|rvr|frontier)\b'], realm, level=30,
              source='AutonomousChatKnowledge.TryAnswerCommon')
    for rr, rps in RR.items():
        if rr % 2 == 0 or rr == 3:
            realm = REALMS[rr % 3]
            b.add(f'how many rps for rr{rr}?', 'rvr_status', [f'{rps:,}', str(rps)], realm, level=50,
                  source='GamePlayer.REALMPOINTS_FOR_LEVEL')

    # 9. Crafting.
    craft = [
        ('how do i start crafting?', ['crafting trainer', 'trainer', 'tools', 'merchant']),
        ('where do i learn weaponcrafting?', ['trainer', 'crafting trainer', 'capital', r're:\b(camelot|jordheim|tir na nog)\b']),
        ('which craft makes the most money?', [r're:\b(alchemy|spellcraft|spellcrafting|sc|tailoring|weaponcraft|armorcraft|fletching|weaponcrafting|armorcrafting)\b']),
        ('how do i get my tailoring up?', [r're:\b(make|craft|orders|trainer|grind|leather|cloth|patterns|recipes)\b']),
        ('what do i need for alchemy?', [r're:\b(herb|herbs|tincture|tinctures|vials|merchant|ingredients|spellcraft)\b']),
        ('how does spellcrafting work?', [r're:\b(gem|gems|imbue|points|crafted|armor|stats|overcharge)\b']),
        ('where do i buy crafting materials?', ['merchant', 'crafting merchant', 'supply', 'trainer']),
        ('is crafting worth it?', [r're:\b(yes|yeah|money|gold|sc|spellcraft|depends|worth|grind|slow)\b']),
        ('anyone make 99% armor?', [r're:\b(me|i can|crafter|armorcrafter|ask|legendary|master|pst)\b']),
        ('can someone make me a bow?', [r're:\b(fletcher|me|i can|sure|pst|what level|how much)\b']),
        ('whats a good secondary craft?', [r're:\b(alchemy|spellcraft|sc|tailoring|metalwork|leatherwork|clothwork|woodwork|gemcutting|herbcraft)\b']),
        ('how do i see my craft skill?', [r're:\b(/craft|skills window|skill window|trainer|window)\b']),
    ]
    for i, (text, must) in enumerate(craft):
        b.add(text, 'crafting', must, REALMS[i % 3], level=20 + i, source='1.65 crafting; AutonomousChatKnowledge.TryAnswerCommon')

    # 10. Items, drops and specs.
    for realm in REALMS:
        for i, cls in enumerate(CLASSES[realm]):
            if i % 3 == 0:
                b.add(rnd.choice(['what should i spec as a {c}?', 'how do i spec my {c}?', 'what spec for {c}?']).format(c=cls.lower()), 'spec',
                      spec_facts(cls), realm, cls, 20 + i, source='AutonomousChatFieldTalk.SpecAdvice')
            elif i % 3 == 1:
                b.add(f'what weapon should a {cls.lower()} use?', 'items', weapon_facts(cls), realm, cls, 15 + i,
                      source='AutonomousChatFieldTalk.WeaponAdvice')
    for lvl, must in ((25, ['drops']), (38, ['drops', 'sc at 45', 'sc at 50']), (50, ['sc', 'spellcraft']), (30, ['drops'])):
        realm = REALMS[lvl % 3]
        b.add(f'sc or drops at {lvl}?', 'items', must, realm, level=lvl, source='AutonomousChatFieldTalk.GearAdvice')
    items = [
        ('whats a good weapon for a 20 armsman?', ['polearm', '2h', 'two hand', 'halberd', 'pike']),
        ('where does the spirit kiss drop?', [r're:\b(drop|drops|named|camp|dungeon)\b']),
        ('what does con mean on items?', ['constitution', 'hit points', 'hp', 'health']),
        ('is 89% quality good?', [r're:\b(fine|ok|good|okay|drop|crafted|99|100|meh)\b']),
        ('what armor does a thane wear?', ['chain']),
        ('what armor can a ranger wear?', ['reinforced', 'leather']),
        ('what armor does a cleric wear?', ['chain']),
        ('what armor does a wizard wear?', ['cloth']),
        ('does strength matter for a healer?', [r're:\b(no|nope|piety|pie|not really)\b']),
        ('what stats for a sorcerer?', ['intelligence', 'int', 'dexterity', 'dex']),
        ('what stats should a druid get?', ['empathy', 'emp', 'con', 'dex']),
        ('what is the best armor in the game?', [r're:\b(crafted|sc|spellcraft|epic|drops|99|legendary)\b']),
        ('anyone know where to get a speed potion?', [r're:\b(alchemist|merchant|exchange|bard|skald|minstrel)\b']),
        ('how do i repair my armor?', [r're:\b(smith|blacksmith|repair|merchant|crafter|armorcrafter)\b']),
    ]
    for i, (text, must) in enumerate(items):
        b.add(text, 'items', must, REALMS[i % 3], level=25, source='1.65 items; AutonomousChatGear.ArmorWord')

    # 11. NPC factions.
    factions = [
        ('mid', 'how do i raise my faction with the svartalfs?', [r're:\b(kill|quest|quests|their enemies|enemies|task|tasks)\b']),
        ('hib', 'why are the guards attacking me?', [r're:\b(faction|killed|kill|enemy|hate|reputation|guards)\b']),
        ('alb', 'how do i fix bad faction?', [r're:\b(kill|quest|quests|enemies|time|task)\b']),
        ('mid', 'what faction are the trolls in?', [r're:\b(faction|troll|trolls|friendly|enemy)\b']),
        ('hib', 'how do i get the sylvan to like me?', [r're:\b(kill|quest|quests|enemies|faction)\b']),
        ('alb', 'why wont the merchant talk to me?', [r're:\b(faction|hate|killed|enemy|reputation)\b']),
        ('mid', 'does killing dwarves hurt my faction?', [r're:\b(yes|yeah|faction|no|nope)\b']),
        ('hib', 'whats faction for?', [r're:\b(npc|npcs|guards|merchants|aggro|friendly|attack|like|hate)\b']),
        ('alb', 'how do i check my faction?', [r're:\b(/faction|con|npc|cant|no way)\b']),
        ('mid', 'can i get kos with a town?', [r're:\b(yes|yeah|faction|kill|guards)\b']),
    ]
    for realm, text, must in factions:
        b.add(text, 'faction', must, realm, level=22, source='1.65 NPC faction')

    # 12. The bots' own plans and state (said to a bot: by tell, in /group, or named in /say and /faction).
    self_q = [
        ('what lvl are you?', lambda r: [str(r['level'])]),
        ('what class are you?', lambda r: [r['cls'].lower()]),
        ('where are you?', lambda r: [r['zone'].split()[0].lower(), r['zone'].lower()]),
        ('whats your spec?', lambda r: [r['spec'].split(',')[0].split()[1], r['spec'].split(',')[0].split()[0]]),
        ('what are you hunting?', lambda r: [r['target'].lower(), r['target'].split()[-1].lower(), r['zone'].split()[0].lower()]),
        ('you in a group?', lambda r: ['solo', 'soloing', r're:\b(no|nope|nah)\b']),
        ('what are you doing tonight?', lambda r: [r're:\b(level|leveling|lvling|rvr|camp|camping|hunt|hunting|craft|crafting|quest|grind|grinding|xp|exp)\b', r['zone'].split()[0].lower()]),
        ('you going to rvr later?', lambda r: [r're:\b(yes|yeah|yep|no|nope|maybe|prob|probably|not yet|too low|after|level|leveling)\b']),
        ('whats your realm rank?', lambda r: [r're:\b\dl\d\b|\brr ?\d\b|\brank\b']),
        ('are you in a guild?', lambda r: [r're:\b(no|nope|nah|guildless|not yet|yes|yeah)\b']),
        ('how long have you been playing?', lambda r: [r're:\b(since|years?|months?|weeks?|launch|release|beta|while|forever)\b']),
        ('did you finish your epic?', lambda r: [r're:\b(not yet|no|nope|only 30|im 30|too low|50|45|level)\b']),
        ('got any pots?', lambda r: [r're:\b(\d|no|nope|some|few|couple|yes|yeah|out)\b']),
        ('can you make me something?', lambda r: [r're:\b(no|nope|not a crafter|dont craft|cant|sure|what)\b']),
        ('what are you up to?', lambda r: [r['target'].split()[-1].lower(), r['zone'].split()[0].lower(), r're:\b(level|leveling|lvling|camp|camping|hunt|hunting|xp|grind|grinding)\b']),
    ]
    channels = ['tell', 'group', 'say', 'faction']
    for realm in REALMS:
        r = RESPONDERS[realm]
        for i, (text, facts) in enumerate(self_q):
            ch = channels[(i + REALMS.index(realm)) % len(channels)]
            line = text if ch in ('tell', 'group') else f'{r["name"].lower()} {text}'
            b.add(line, 'bot_plans', facts(r), realm, level=r['level'] - 2, channel=ch, to=r['name'],
                  wrong=[str(lv) for lv in (10, 20, 40, 50) if abs(lv - r['level']) > 3] if 'lvl' in text else None,
                  source='the responder stub (RESPONDERS)')

    # 13. Small talk.
    small = [
        ('hi all', 'small_talk', [GREETING], False), ('evening everyone', 'small_talk', [GREETING], False),
        ('hey guys whats up', 'small_talk', [GREETING, HOWAREYOU, r're:\b(not much|nm|nothing|leveling|camping|grinding)\b'], False),
        ('how is everyone tonight?', 'small_talk', [HOWAREYOU], False), ('morning realm', 'small_talk', [GREETING], False),
        ('gn all', 'small_talk', [FAREWELL], True), ('heading to bed, night all', 'small_talk', [FAREWELL], True),
        ('ty for the help earlier', 'small_talk', [THANKS], True), ('lol', 'small_talk', [r're:\b(lol|haha|heh)\b'], True),
        ('anyone else lagging?', 'small_talk', [r're:\b(lag|lagging|fine|ok|smooth|yes|yeah|no|nope|me too|same|bit)\b'], False),
        ('happy halloween all!', 'small_talk', [r're:\b(halloween|happy|you too|u too)\b'], False),
        ('back', 'small_talk', [r're:\b(wb|welcome back)\b'], True), ('brb', 'small_talk', [r're:\b(k|ok|kk)\b'], True),
        ('ding 20!', 'small_talk', [r're:\b(gz|grats|gratz|congrats|nice|woot)\b'], True),
        ('finally 50!!!', 'small_talk', [r're:\b(gz|grats|gratz|congrats|nice|woot)\b'], False),
        ('this camp is dead lol', 'small_talk', [r're:\b(lol|yeah|same|dead|quiet|move|try)\b'], True),
        ('anyone on?', 'small_talk', [r're:\b(here|me|yep|yeah|im on|on)\b'], False),
        ('is this server always this quiet?', 'small_talk', [r're:\b(quiet|busy|evening|night|weekend|depends|usually|later|yes|no)\b'], False),
        ('what time is it there?', 'small_talk', [r're:\b(\d|night|morning|evening|late|early|pm|am)\b'], True),
        ('any good music to grind to?', 'small_talk', [r're:\b(music|listen|metal|rock|radio|silence|game music)\b'], True),
        ('so bored grinding', 'small_talk', [r're:\b(same|grind|bored|boring|group|lol|yeah)\b'], True),
        ('just died to a grey mob lol', 'small_talk', [r're:\b(lol|oof|ouch|happens|rip|haha|heh)\b'], True),
        ('first night playing, any tips?', 'small_talk', [r're:\b(bind|trainer|quest|group|kill tasks|tasks|level|camelot hills|mularn|mag mell|welcome)\b'], False),
        ('are you guys bots?', 'small_talk', [r're:\b(no|lol|nope|maybe|beep|what)\b'], True),
        ('i love this game', 'small_talk', [r're:\b(same|me too|yeah|best|love|lol)\b'], True),
    ]
    for i, (text, intent, must, ok) in enumerate(small):
        b.add(text, intent, must, REALMS[i % 3], level=10 + (i * 7) % 40, silence_ok=ok, source='small talk')

    # 14. Chat terms and how-to.
    terms = [('what does lfg mean?', ['looking for group']), ('whats pbae?', ['point blank', 'area']), ('what does inc mean', ['incoming']),
             ('what is a con?', ['color', 'colour', 'tough']), ('what does oom mean?', ['out of mana', 'mana']),
             ('whats rvr?', ['realm vs realm', 'realm versus realm', 'frontier']), ('what does pst mean?', ['send tell', 'tell']),
             ('what does ks mean?', ['kill steal']), ('whats a ra?', ['realm ability']), ('what is mez?', ['mesmerize', 'hold', 'still']),
             ('how do i bind?', ['/bind', 'bindstone']), ('how do i go anon?', ['/anon']), ('im stuck in a rock, help?', ['/stuck']),
             ('how do i equip this?', ['drag', 'inventory', 'paperdoll']), ('how do i see my played time?', ['/played']),
             ('how do i check the relics?', ['/relic']), ('how do i invite someone to a group?', ['/invite']),
             ('how do i talk to my guild?', ['/gu']), ('how do i release?', ['/release']), ('whats the command for a tell?', ['/send', '/tell', '/t ']),
             ('how do i get a horse?', ['stable', 'stable master', 'ticket']), ('how do i buy a house?', [r're:\b(housing|house|merchant|lot|not here|cant|deed)\b']),
             ('how do i use my realm abilities?', [r're:\b(trainer|ra|realm ability|realm abilities|window|abilities)\b']),
             ('how do i set my bind point?', ['/bind', 'bindstone'])]
    for i, (text, must) in enumerate(terms):
        b.add(text, 'terms' if 'mean' in text or text.startswith(('whats', 'what is')) else 'howto', must, REALMS[i % 3], level=8 + i,
              source='AutonomousChatKnowledge.Terms / TryAnswerHowTo / AutonomousChatQuestions.AnswerCommand')

    # 15. Class questions (opinions, with a fact to hold).
    opinions = [('alb', 'is friar good?', ['friar', 'staff', 'heal', 'heals', 'rejuv']), ('mid', 'are savages fun?', ['savage', 'h2h', 'claw', 'hand to hand']),
                ('hib', 'is a bard needed in groups?', ['bard', 'speed', 'mez', 'heal', 'heals', 'songs', 'music']),
                ('alb', 'should i roll a necro?', ['necro', 'pet', 'necromancer']), ('mid', 'whats better, warrior or thane?', ['warrior', 'thane']),
                ('hib', 'is valewalker any good?', ['valewalker', 'scythe', 'vw']), ('alb', 'which class solos best?', [r're:\b(necro|necromancer|theurg|theurgist|cabalist|cab|scout|infiltrator|infil|friar|paladin)\b']),
                ('mid', 'which healer should i roll, healer or shaman?', ['healer', 'shaman']),
                ('hib', 'is ranger good for rvr?', ['ranger', 'bow', 'stealth', 'archer']),
                ('alb', 'are minstrels good?', ['minstrel', 'mez', 'speed', 'stealth', 'songs']),
                ('mid', 'what does a skald do?', ['skald', 'speed', 'songs', 'damage', 'dd', 'chant']),
                ('hib', 'is eldritch better than enchanter?', ['eldritch', 'enchanter', 'pet', 'light', 'void'])]
    for realm, text, must in opinions:
        b.add(text, 'spec', must, realm, level=5, source='class talk: names the class or its tools')

    # 16. Where a town is (1.2.3, the owner's game: in a bot-led group "where is Humberton Castle" drew "not sure tbh" and
    # "stablemaster is your friend"). Asked in /group, as there, and in /faction; answered with the town's zone.
    town_q = [('where is {t}', 'group'), ('how do i get to {t}?', 'group'), ('wheres {t}?', 'faction')]
    for i, town in enumerate(WORLD['towns']):
        zone = town['zone'].lower()
        must = [zone, zone.replace('mtns.', 'mountains'), zone.replace('mtns.', 'mtns')]
        text, channel = town_q[i % len(town_q)]
        b.add(text.format(t=town['name'].lower() if i % 2 else town['name']), 'travel', sorted(set(must)), town['realm'], level=5,
              channel=channel, wrong=[r're:\b(not sure|dunno|no idea|idk)\b', 'stablemaster is your friend'],
              source="world Area table: the town's zone (AutonomousChatKnowledge.TryAnswerTown)")

    return b.items


def spec_facts(cls):
    words = {
        'Armsman': ['polearm', 'shield'], 'Paladin': ['chants', '2h', 'slash'], 'Mercenary': ['dual wield', 'dw', 'slash'],
        'Reaver': ['soulrending', 'flex'], 'Cleric': ['rejuv', 'enhance', 'smite'], 'Friar': ['staff', 'rejuv', 'enhance'],
        'Minstrel': ['instruments', 'slash'], 'Scout': ['longbow', 'bow', 'stealth'], 'Infiltrator': ['critical strike', 'cs', 'envenom'],
        'Sorcerer': ['body', 'mind', 'matter'], 'Wizard': ['fire', 'earth', 'ice'], 'Theurgist': ['earth', 'air', 'ice'],
        'Cabalist': ['matter', 'body', 'spirit'], 'Necromancer': ['deathsight', 'painworking'], 'Warrior': ['shield', 'hammer', 'axe', 'parry'],
        'Thane': ['stormcalling', 'storm', 'hammer'], 'Skald': ['battlesongs', 'songs', '2h'], 'Berserker': ['left axe', 'lefthand', 'axe'],
        'Savage': ['savagery', 'h2h', 'hand to hand'], 'Hunter': ['beastcraft', 'bow', 'spear'], 'Shadowblade': ['critical strike', 'cs', 'left axe'],
        'Healer': ['mending', 'pac', 'augmentation'], 'Shaman': ['mending', 'cave', 'augmentation'], 'Runemaster': ['darkness', 'runecarving', 'suppression'],
        'Spiritmaster': ['darkness', 'summoning'], 'Bonedancer': ['bone army', 'army', 'darkness'], 'Hero': ['large weapons', 'large', 'shield', 'blades'],
        'Champion': ['valor', 'large', 'blades'], 'Blademaster': ['celtic dual', 'cd', 'blades'], 'Ranger': ['recurve', 'bow', 'pathfinding'],
        'Nightshade': ['critical strike', 'cs', 'stealth'], 'Druid': ['nurture', 'nature', 'regrowth'], 'Warden': ['regrowth', 'nurture', 'blades'],
        'Bard': ['music', 'regrowth', 'nurture'], 'Eldritch': ['light', 'void', 'mana'], 'Enchanter': ['light', 'enchantments', 'mana'],
        'Mentalist': ['mentalism', 'light', 'mana'], 'Animist': ['arboreal', 'creeping'], 'Valewalker': ['scythe', 'arboreal'],
    }
    return words[cls]


def weapon_facts(cls):
    words = {
        'Paladin': ['2h', 'slash', 'two hand'], 'Reaver': ['flex', 'whip'], 'Minstrel': ['slash', 'rapier', 'blade'], 'Sorcerer': ['staff'],
        'Theurgist': ['staff'], 'Thane': ['hammer'], 'Berserker': ['axe', 'axes'], 'Hunter': ['bow', 'spear'], 'Shaman': ['hammer', 'shield'],
        'Spiritmaster': ['staff'], 'Champion': ['large', 'blades'], 'Ranger': ['bow', 'recurve'], 'Warden': ['blades', 'blunt', 'shield'],
        'Eldritch': ['staff'], 'Mentalist': ['staff'], 'Valewalker': ['scythe'], 'Mercenary': ['slash', 'thrust', 'dw', 'dual'],
        'Cleric': ['mace', 'crush', 'shield'], 'Infiltrator': ['dagger', 'daggers', 'thrust'], 'Necromancer': ['staff'], 'Skald': ['2h', 'hammer', 'axe'],
        'Shadowblade': ['axe', 'axes', '2h', 'sword'], 'Spiritmaster ': ['staff'], 'Blademaster': ['blades', 'blade'], 'Druid': ['blunt', 'hammer', 'shield'],
        'Enchanter': ['staff'], 'Animist': ['staff'], 'Bonedancer': ['staff'], 'Warrior': ['hammer', 'axe', 'shield', '2h'], 'Savage': ['h2h', 'claw', 'claws', 'hand to hand'],
        'Healer': ['hammer', 'shield'], 'Runemaster': ['staff'], 'Hero': ['large', 'spear'], 'Nightshade': ['piercer', 'piercing', 'blades'], 'Bard': ['blades', 'blunt'],
        'Armsman': ['polearm', '2h'], 'Friar': ['staff', 'quarterstaff'], 'Scout': ['longbow', 'bow'], 'Wizard': ['staff'], 'Cabalist': ['staff'],
    }
    return words[cls]


def build_holdout():
    """The holdout set (60 questions): written after the 1.2.2 routing fixes (F0, F1, F4, F6, F7) and never used to tune them, so the
    ratchet can tell fixes that generalise from fixes fitted to the bench. New phrasings of the same intents, facts from the same
    data. Ids h001...; never edit a question to make it pass: add a new one."""
    rnd = random.Random(20261123)
    places = load_places()
    b = Bench()
    # Where to level: phrasings the bench doesn't use.
    forms = ['where do people level around {lvl}?', '{lvl} {cls} looking for a spot to grind, ideas?', 'whats a good zone for {lvl}?']
    for realm in REALMS:
        for i, lvl in enumerate([8, 23, 38]):
            cls = CLASSES[realm][(i * 4 + 1) % len(CLASSES[realm])]
            good, bad = level_facts(places, realm, lvl)
            b.add(forms[(i + REALMS.index(realm)) % 3].format(lvl=lvl, cls=cls.lower()), 'level_where', good, realm, cls, lvl, wrong=bad,
                  source='places.json levels (+-4)')
    # A place's levels.
    level_q = ['what level mobs are in {p}?', 'is {p} too hard for a {lvl}?', '{p} good for {lvl}s?']
    picks = [p for p in places if p.get('levels') and p['type'] in ('dungeon', 'zone') and p['realm'] in REALMS]
    rnd.shuffle(picks)
    for i, p in enumerate(picks[:9]):
        low, high = p['levels']
        lvl = max(2, min(50, rnd.choice([low, (low + high) // 2])))
        must = [rf're:\b{min(low, 50)}\+' if high > 50 else rf're:\b{low}\b.{{0,12}}\b{high}\b']
        b.add(level_q[i % 3].format(p=rnd.choice(names(p)), lvl=lvl), 'place_level', must, p['realm'], level=lvl,
              source=f'places.json {p["key"]} {low}-{high}')
    # Buying.
    buys = [('where do i buy a new mace?', ['exchange', 'weaponcrafter', 'weaponsmith', 'merchant', 'smith']),
            ('anyone know who sells arrows?', ['fletcher', 'arrow merchant', 'merchant', 'exchange']),
            ('where can i get leather armor?', ['armorcrafter', 'armorsmith', 'tailor', 'exchange', 'merchant']),
            ('who buys loot?', ['merchant', 'vendor', 'any merchant', 'exchange']),
            ('where do i get better gear at 30?', ['exchange', 'crafter', 'drops', 'drop', 'sc', 'spellcraft', 'merchant']),
            ('where can i buy a horse ticket?', ['stable master', 'stable', 'stablemaster'])]
    for i, (text, must) in enumerate(buys):
        b.add(text, 'buy_where', must, REALMS[i % 3], level=12 + i * 4, source='1.65 merchants; AutonomousChatKnowledge.TryAnswerBuyGear')
    # Travel in the asker's own realm.
    for realm in REALMS:
        mine = [p for p in places if p.get('realm') == realm and p['type'] in ('dungeon', 'zone', 'town')]
        rnd.shuffle(mine)
        for i, p in enumerate(mine[:2]):
            zones = [z for z in p.get('zones', []) if z.lower() != p['key']]
            b.add(['which way to {p}?', 'how do i find {p}?'][i].format(p=rnd.choice(names(p))), 'travel', names(p) + zones + ['stable', 'horse', 'ticket', 'road'],
                  realm, level=(p.get('levels') or [20])[0] or 20, wrong=[r're:\b(cant|can t|enemy|other realm|their side)\b'],
                  source='places.json zones')
    # Trainers.
    for realm in REALMS:
        for cls in (CLASSES[realm][0], CLASSES[realm][-1]):
            wrong = [CAPITAL[r] for r in REALMS if r != realm]
            if cls in MELEE:
                wrong.append(r're:\bspells?\b')
            b.add(f'where can i find the {cls.lower()} trainer?', 'trainer', [CAPITAL[realm], 'capital'], realm, cls, 10, wrong=wrong,
                  source='every trainer in the capital')
    # The epic and quests.
    epic = [('at what level does the epic start?', 'epic', ['50', '45', 'trainer']),
            ('can i solo my epic?', 'epic', ['group', 'grp', 'solo', r're:\b(no|nope|some|parts)\b']),
            ('what do i get from the epic?', 'epic', ['armor', 'armour', 'weapon', 'gear', 'set']),
            ('where do i turn in quests?', 'quest', ['journal', 'quest giver', 'gave it', 'whoever gave', 'npc'])]
    for i, (text, intent, must) in enumerate(epic):
        b.add(text, intent, must, REALMS[i % 3], level=46 if intent == 'epic' else 14, source='AutonomousChatKnowledge.TryAnswerCommon; 1.65 epics')
    # Raids, the world, Darkness Falls.
    for realm in REALMS:
        name, zone = DRAGON[realm]
        b.add(f'where does the {REALM_NAME[realm].lower()} dragon live?', 'raid', [name, zone], realm, level=45,
              wrong=[DRAGON[r][0] for r in REALMS if r != realm], source='AutonomousChatKnowledge.TryAnswerDragon')
        b.add('who owns darkness falls?', 'rvr_status', [r're:\b(hibs|hibernia|hib|ours|we have)\b'], realm, level=40,
              wrong=[r're:\b(albs|albion|mids|midgard)\b'] if realm == 'hib' else None, source='stub world: Hibernia holds DF')
        mine = [r for r in WORLD['relics'] if r['home'] == realm]
        lost = [r for r in mine if r['owner'] != realm]
        b.add('do we have all our relics?', 'rvr_status',
              [r're:^(no|nope)\b|\b(lost|they have|the (albs|mids|hibs) have)\b'] if lost else [r're:\b(yes|yep|yeah|still have|home|ours|both)\b'],
              realm, level=45, wrong=[r're:\b(yep|yes|still have|both of ours are home)\b'] if lost else [r're:\b(lost|they have)\b'], source='stub world relics')
    # Terms.
    terms = [('what does lfm mean?', ['looking for more', 'more']), ('what does afk mean?', ['away from keyboard', 'away']),
             ('what does brb mean?', ['be right back'])]
    for i, (text, must) in enumerate(terms):
        b.add(text, 'terms', must, REALMS[i % 3], level=9 + i, source='chat terms of the era')
    # Questions about grouping (F6) that are not group posts.
    grouping = [('do i need a group for caer sidi?', 'raid', ['group', 'grp', 'raid', r're:\b(yes|yeah|yep|need|full)\b']),
                ('what does a tank do in a group?', 'spec', ['aggro', 'taunt', 'guard', 'hold', 'pull', 'protect', 'tank']),
                ('is it better to solo or group at 20?', 'level_where', ['group', 'grp', 'faster', 'xp', 'solo'])]
    for i, (text, intent, must) in enumerate(grouping):
        b.add(text, intent, must, REALMS[i % 3], level=20, source='1.65 grouping')
    # Specs.
    for realm in REALMS:
        cls = CLASSES[realm][4]
        b.add(f'whats the best spec for a {cls.lower()}?', 'spec', spec_facts(cls), realm, cls, 24, source='AutonomousChatFieldTalk.SpecAdvice')
    # Small talk.
    small = [('evening all', [GREETING], False), ('good night everyone', [FAREWELL], True)]
    for i, (text, must, ok) in enumerate(small):
        b.add(text, 'small_talk', must, REALMS[i % 3], level=20, silence_ok=ok, source='small talk')
    for item in b.items:
        item['id'] = 'h' + item['id'][1:]
    return b.items


def main():
    items = build()
    data = {
        'note': 'The 1.2.2 chat audit question bench (tools/bot-voices/eval/make_player_questions.py writes it; do not edit by hand). '
                'UT_ChatQuestionBench answers every question through the real reply path; score_bench.py scores the answers.',
        'world': WORLD,
        'responders': RESPONDERS,
        'dungeon_table': {r: [list(d) for d in v] for r, v in DUNGEON_TABLE.items()},
        'questions': items,
    }
    text = json.dumps(data, indent=1, ensure_ascii=False) + '\n'
    held = build_holdout()
    holdout = json.dumps({'note': 'The holdout set of the chat bench (make_player_questions.py build_holdout writes it; do not edit by hand): '
                                  'written after the 1.2.2 routing fixes, never used to tune them. The bench answers it with the world '
                                  'and responders of player_questions.json; score_bench.py ratchet reports it.',
                          'questions': held}, indent=1, ensure_ascii=False) + '\n'
    if '--check' in sys.argv:
        if OUT.read_text(encoding='utf-8') != text:
            raise SystemExit('player_questions.json is out of date: run tools/bot-voices/eval/make_player_questions.py')
        if not HOLDOUT.exists() or HOLDOUT.read_text(encoding='utf-8') != holdout:
            raise SystemExit('player_questions_holdout.json is out of date: run tools/bot-voices/eval/make_player_questions.py')
        print(f'player_questions.json is up to date ({len(items)} questions, {len(held)} held out)')
        return
    OUT.write_text(text, encoding='utf-8')
    HOLDOUT.write_text(holdout, encoding='utf-8')
    counts = {}
    for item in items:
        counts[item['intent']] = counts.get(item['intent'], 0) + 1
    print(f'{len(items)} questions:', ', '.join(f'{k} {v}' for k, v in sorted(counts.items())))


if __name__ == '__main__':
    main()
