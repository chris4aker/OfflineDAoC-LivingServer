"""Check the bot-voice line files and build corpus.json.

  python tools/bot-voices/build_corpus.py            (check and write corpus.json, print coverage)
  python tools/bot-voices/build_corpus.py --check    (check only; exit 1 on any problem)
  python tools/bot-voices/build_corpus.py --verify   (CI: exit 1 if corpus.json is not what the sources build)

Line files: source/server/GameServer/bots/autonomous/data/chat/*.lines. The format is described in
docs/bot-voices/DESIGN.md (section 3). Every line is new writing for this project.
"""
import argparse
import collections
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHAT = ROOT / 'source/server/GameServer/bots/autonomous/data/chat'
OUT = CHAT / 'corpus.json'

KINDS = {'ambient', 'reply', 'lfg', 'trade', 'rvr', 'greet', 'thanks', 'grudge', 'gossip', 'ding', 'death',
         'camp', 'debate',
         # Living Server (docs/LIVING-SERVER.md)
         'grats', 'rez_ask', 'rez_thanks', 'rez_offer', 'corpse', 'sickness', 'first', 'alarm', 'nemesis', 'revenge',
         'session', 'linkdead', 'combat',
         # 0.36-se.4: group talk (/group with you, /say near you), a starter's closing line, guild idle chat
         'group', 'closing', 'guild'}
# Lines that start a conversation; each says what kind of answer it invites (@invites, or inferred below).
OPENER_KINDS = {'ambient', 'lfg', 'trade', 'rvr', 'camp', 'group', 'guild'}
# What an opener invites; replies say what they answer (@answers). "ask_<topic>" is a specific question that
# falls back to the "ask" replies; "none" gets no reply.
INVITES = {'none', 'chat', 'joke', 'boast', 'complaint', 'news', 'lfg', 'trade', 'ask', 'ask_where', 'ask_advice',
           'ask_opinion', 'ask_anyone', 'ask_howareyou', 'ask_permission', 'ask_choice'}
INVITE_RE = re.compile(r'(none|chat|joke|boast|complaint|news|lfg|trade|ask(_[a-z0-9]+)*)')
TYPES = {'Leveler', 'Crafter', 'Socialite', 'Explorer', 'Daredevil', 'NoLifer'}
REALMS = {'alb', 'mid', 'hib'}
CLASSES = {
    'alb': {'Armsman', 'Paladin', 'Mercenary', 'Reaver', 'Scout', 'Infiltrator', 'Minstrel', 'Cleric', 'Friar',
            'Wizard', 'Theurgist', 'Sorcerer', 'Cabalist', 'Necromancer'},
    'mid': {'Warrior', 'Thane', 'Skald', 'Berserker', 'Savage', 'Hunter', 'Shadowblade', 'Healer', 'Shaman',
            'Runemaster', 'Spiritmaster', 'Bonedancer'},
    'hib': {'Hero', 'Champion', 'Blademaster', 'Warden', 'Druid', 'Bard', 'Ranger', 'Nightshade', 'Eldritch',
            'Enchanter', 'Mentalist', 'Animist', 'Valewalker'},
}
ALL_CLASSES = set().union(*CLASSES.values())
FLAGS = {'in_group', 'solo', 'in_town', 'in_dungeon', 'in_rvr', 'at_camp', 'traveling', 'just_leveled',
         'just_died', 'bag_full', 'has_listing', 'knows_player', 'player_nearby', 'player_guildless',
         # 0.36-se.4: where and when (the PC's clock: day 6-17, evening 17-23, night 23-6), and who the bot is with
         'outdoors', 'in_capital', 'day', 'evening', 'night', 'in_player_group', 'can_rez', 'in_battleground', 'in_frontier',
         'will_rez'}   # will_rez: set only when answering a rez ask the bot will really come to
TIME_FLAGS = {'day', 'evening', 'night'}
INTENTS = {'unknown', 'greeting', 'banter', 'abuse', 'grouping', 'grinding', 'travel', 'dungeon', 'rvr',
           'trading', 'help', 'farewell', 'ignoreoutofworld',
           # 0.36-se.4: short chat players type
           'thanks', 'congrats', 'ding', 'laugh', 'away', 'back', 'rez', 'alarm', 'classtalk', 'botcheck'}
MOODS = {'friendly', 'neutral', 'cool', 'rival'}
SLOTS = {'me', 'class', 'level', 'realm', 'zone', 'monster', 'npc', 'item', 'count', 'camp', 'dungeon',
         'capital', 'enemy', 'keep', 'frontier', 'player', 'player_class', 'place', 'q',
         # Living Server
         'target', 'what', 'size', 'classes', 'killer', 'killer_class',
         # 0.36-se.4: the plural of {monster} ("2 hill cats")
         'monsters'}
MEMORY_SLOTS = {'player', 'player_class', 'place'}
MAX_RAW = 140

# Out of era (Trials of Atlantis, New Frontiers, Catacombs), the custom class, real-world names, profanity.
BANNED = [
    r'atlantis', r'\btoa\b', r'artifact', r'master ?levels?', r'\bml\d*\b', r'champion levels?', r'\bcl\d*\b',
    r'catacombs', r'labyrinth', r'new frontiers?', r'\btowers?\b', r'oceanus', r'stygia', r'aerus', r'volcanus',
    r'mauler', r'vampiir', r'bainshee', r'heretic', r'valkyr', r'warlock', r'frostalf', r'\bshar\b', r'deifrang',
    r'minotaur', r'sluagh', r'mythic', r'\bign\b', r'\bvault\b', r'allakhazam', r'everquest', r'world of warcraft',
    r'\bfuck', r'\bshit', r'\bbitch', r'\bdamn', r'\bass\b', r'\bcrap\b', r'\bhell\b',
    # Later internet phrases (audit 2026-10-18)
    r'change my mind', r'fight me', r'\bvibes?\b', r'love language', r'no thoughts', r'\blifestyle\b', r'\bsalty\b',
    r'\bthe meta\b', r'i felt that', r'\bpog', r'\bbased\b', r'\bno cap\b', r'\bgg ez\b', r'\bsus\b', r'\bmain character\b',
]
# A line that assumes a time of day needs a time flag (in game a noon session heard "late night crew checking in").
# "tonight" and "all night" need none: by day the server says "today" and "all day" (AutonomousBotVoiceCorpus.ForTimeOfDay).
TIME_WORDS_RE = re.compile(r"\b(late night|so late|up late|off to bed|to bed|bedtime|in the morning|"
                           r"good morning|good evening|midnight|cant sleep|past my bedtime|wee hours)\b", re.IGNORECASE)
# The {monster} slot is singular: "{count} {monster}" and "{monster} are" read "2 hill cat"; use {monsters}.
PLURAL_MONSTER_RE = re.compile(r'\{count\} \{monster\}|\{monster\} (are|were|keep|give|drop|hit)\b')
BANNED_RE = re.compile('|'.join(BANNED), re.IGNORECASE)
SLOT_RE = re.compile(r'\{([^{}]*)\}')


class Problem(Exception):
    pass


def parse_levels(value, where):
    if value == '*':
        return [1, 50]
    m = re.fullmatch(r'(\d+)-(\d+)', value)
    if not m or not 1 <= int(m.group(1)) <= int(m.group(2)) <= 50:
        raise Problem(f'{where}: bad @levels "{value}"')
    return [int(m.group(1)), int(m.group(2))]


def split_list(value):
    return [] if value in ('', '*') else [v.strip() for v in value.split(',') if v.strip()]


def check_text(text, tags, where, problems):
    if len(text) > MAX_RAW:
        problems.append(f'{where}: {len(text)} characters (max {MAX_RAW}): {text}')
    for slot in SLOT_RE.findall(text):
        if slot not in SLOTS:
            problems.append(f'{where}: unknown slot {{{slot}}}')
        elif slot in MEMORY_SLOTS and 'knows_player' not in tags['needs'] and 'player_nearby' not in tags['needs'] and tags['kind'] not in (
                'greet', 'thanks', 'grudge', 'gossip', 'alarm', 'nemesis', 'revenge') and not (
                slot == 'player' and tags['kind'] in ('reply', 'closing')):   # in a reply, {player} is whoever is answered
            problems.append(f'{where}: memory slot {{{slot}}} needs @needs knows_player or a memory kind')
    if text.count('{') != text.count('}'):
        problems.append(f'{where}: unbalanced braces')
    hit = BANNED_RE.search(SLOT_RE.sub('', text))
    if hit:
        problems.append(f'{where}: out-of-era or banned word "{hit.group(0)}": {text}')
    if PLURAL_MONSTER_RE.search(text):
        problems.append(f'{where}: {{monster}} is one monster; write {{monsters}} for the plural: {text}')
    needs = set(tags.get('needs', []))
    if TIME_WORDS_RE.search(text) and not any(f.lstrip('!') in TIME_FLAGS for f in needs):
        problems.append(f'{where}: says a time of day; add @needs evening or night (or day, !day...): {text}')
    if '{zone}' in text and tags['kind'] in OPENER_KINDS and not needs & {
            'outdoors', 'in_dungeon', 'in_capital', 'in_rvr', 'at_camp', 'traveling', 'in_town', 'in_battleground', 'in_frontier'}:
        problems.append(f'{where}: {{zone}} needs a place flag (outdoors, at_camp, in_dungeon...) so it is not said '
                        f'from a city or a dungeon by mistake: {text}')


def check_tags(tags, where, problems):
    if tags['kind'] not in KINDS:
        problems.append(f'{where}: unknown @kind {tags["kind"]}')
    for t in tags['who']:
        if t not in TYPES:
            problems.append(f'{where}: unknown type {t}')
    if tags['realm'] not in REALMS | {'*'}:
        problems.append(f'{where}: unknown @realm {tags["realm"]}')
    allowed = CLASSES.get(tags['realm'], ALL_CLASSES)
    for c in tags['class']:
        if c not in allowed:
            problems.append(f'{where}: class {c} is not in realm {tags["realm"]}')
    for f in tags['needs']:
        if f.lstrip('!') not in FLAGS:
            problems.append(f'{where}: unknown flag {f}')
    if tags['intent'] and tags['intent'] not in INTENTS:
        problems.append(f'{where}: unknown @intent {tags["intent"]}')
    if tags['kind'] == 'reply' and not tags['intent'] and not tags['answers']:
        problems.append(f'{where}: reply lines need @intent (a player\'s line) or @answers (a bot\'s opener)')
    if tags['kind'] == 'closing' and not tags['answers']:
        problems.append(f'{where}: closing lines need @answers (what kind of opener they close)')
    for a in tags['answers']:
        if not INVITE_RE.fullmatch(a) or a == 'none':
            problems.append(f'{where}: unknown @answers {a}')
    if tags['invites'] and not INVITE_RE.fullmatch(tags['invites']):
        problems.append(f'{where}: unknown @invites {tags["invites"]}')
    if tags['invites'] and tags['kind'] not in OPENER_KINDS:
        problems.append(f'{where}: @invites is for lines that start a conversation ({", ".join(sorted(OPENER_KINDS))})')
    if tags['mood'] and tags['mood'] not in MOODS:
        problems.append(f'{where}: unknown @mood {tags["mood"]}')


def fresh_tags():
    return dict(kind='ambient', topic='general', who=[], realm='*', **{'class': []}, levels=[1, 50], needs=[],
                intent='', mood='', weight=1.0, invites='', answers=[])


def infer_invites(text, kind):
    """What an opener without @invites invites: questions by their shape, otherwise a light reaction."""
    t = SLOT_RE.sub('x', text.lower()).strip()
    if t.endswith('?'):
        if re.search(r"^((hey|hi|so|ok|yo)[ ,]+)?(how (is|are|r) (every|you|u|ya|yall|things|it)|hows (every|it|things)|how goes|"
                     r"what did i miss|whats new|whats up|what is everyone (up to|doing))", t):
            return 'ask_howareyou'
        if re.search(r"\b(mind if i|ok if i|okay if i|is it ok if)\b", t):
            return 'ask_permission'
        if re.search(r"\bor\b", t) and not re.search(r"\bor (something|so|anything|what)\b", t):
            return 'ask_choice'
        if re.search(r"\bwhere\b|\bwheres\b|how do (i|you) get|which way|how far", t):
            return 'ask_where'
        if re.search(r"\b(best|better|worth it|what do (you|u) think|thoughts on|should i|is it worth|favorite|favourite)\b", t):
            return 'ask_opinion'
        if re.match(r"(anyone|any1|anybody|who|any )", t) or re.search(r"\b(anyone|anybody|any1)\b", t):
            return 'ask_anyone'
        if re.search(r"\b(how|what|why|when|which)\b", t):
            return 'ask_advice'
        return 'ask'
    if kind == 'lfg':
        return 'lfg'
    if kind == 'trade':
        return 'trade'
    return 'chat'


def parse_file(path, problems):
    lines, scenes = [], []
    tags = fresh_tags()
    scene = None
    for number, raw in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        where = f'{path.name}:{number}'
        text = raw.strip()
        if not text or text.startswith('#'):
            continue
        if text.startswith('@'):
            key, _, value = text[1:].partition(' ')
            value = value.split(' #', 1)[0].strip()
            if key == 'scene':
                scene = dict(id=value, topic=tags['topic'], realm=tags['realm'], levels=tags['levels'], roles={},
                             beats=[], where=where, needs=[n for n in tags['needs'] if n.lstrip('!') in TIME_FLAGS])
                bad = [n for n in tags['needs'] if n.lstrip('!') not in TIME_FLAGS]
                if bad:
                    problems.append(f'{where}: a scene can only need a time of day (day, evening, night), not {bad}')
                scenes.append(scene)
            elif key == 'role':
                if scene is None:
                    problems.append(f'{where}: @role outside a scene')
                    continue
                role, _, spec = value.partition(' ')
                req = dict(p.split('=', 1) for p in spec.split() if '=' in p)
                for t in split_list(req.get('type', '')):
                    if t not in TYPES:
                        problems.append(f'{where}: unknown type {t}')
                for c in split_list(req.get('class', '')):
                    if c not in ALL_CLASSES:
                        problems.append(f'{where}: unknown class {c}')
                scene['roles'][role] = dict(type=split_list(req.get('type', '')), **{'class': split_list(req.get('class', ''))})
                # levels=35-49: a role whose lines only fit some levels ("i am not 50 yet"; live test 2026-10-18).
                if 'levels' in req:
                    scene['roles'][role]['levels'] = parse_levels(req['levels'], where)
            elif key == 'end':
                scene = None
            else:
                scene = None
                try:
                    if key == 'kind':
                        tags = fresh_tags() | dict(kind=value)
                    elif key == 'topic':
                        tags['topic'] = value
                    elif key == 'who':
                        tags['who'] = split_list(value)
                    elif key == 'realm':
                        tags['realm'] = value
                    elif key == 'class':
                        tags['class'] = split_list(value)
                    elif key == 'levels':
                        tags['levels'] = parse_levels(value, where)
                    elif key == 'needs':
                        tags['needs'] = split_list(value)
                    elif key == 'intent':
                        tags['intent'] = value.lower()
                    elif key == 'mood':
                        tags['mood'] = value
                    elif key == 'weight':
                        tags['weight'] = float(value)
                    elif key == 'invites':
                        tags['invites'] = value
                    elif key == 'answers':
                        tags['answers'] = split_list(value)
                    else:
                        problems.append(f'{where}: unknown directive @{key}')
                except Problem as error:
                    problems.append(str(error))
            continue
        if scene is not None:
            pause = re.fullmatch(r'~\s*(\d+)', text)
            if pause:
                seconds = int(pause.group(1))
                if not 5 <= seconds <= 600:
                    problems.append(f'{where}: a pause must be 5-600 seconds')
                scene['beats'].append(dict(role='~', pause=seconds))
                continue
            m = re.fullmatch(r'(\?)?([A-F]):\s*(.+)', text)
            if not m:
                problems.append(f'{where}: scene beats look like "A: text" (roles A-F, "?" for optional)')
                continue
            optional, role, beat = m.groups()
            variants = [v.strip() for v in beat.split(' | ')]
            if any(not v for v in variants):
                problems.append(f'{where}: empty alternative')
            for v in variants:
                check_text(v, dict(kind='debate', needs=scene['needs']), where, problems)
                if re.match(r'\??[A-F]:\s', v):
                    problems.append(f'{where}: an alternative starts with a role marker: {v}')
            scene['beats'].append(dict(role=role, text=variants[0], variants=variants, optional=bool(optional),
                                       where=where))
            continue
        check_tags(tags, where, problems)
        check_text(text, tags, where, problems)
        line = dict(tags, text=text, where=where, **{'class': list(tags['class'])})
        if tags['kind'] in OPENER_KINDS:
            line['invites'] = tags['invites'] or infer_invites(text, tags['kind'])
            # A plain "ask" tag takes a more specific question type when the words show one ("how is everyone?").
            if line['invites'] == 'ask' and infer_invites(text, tags['kind']).startswith('ask_'):
                line['invites'] = infer_invites(text, tags['kind'])
        else:
            line.pop('invites')
        if not line['answers']:
            line.pop('answers')
        lines.append(line)
    for s in scenes:
        roles_used = {b['role'] for b in s['beats'] if b['role'] != '~'}
        if sum(1 for b in s['beats'] if b['role'] != '~') < 3:
            problems.append(f'{s["where"]}: scene {s["id"]} needs at least 3 beats')
        if len(roles_used) < 2:
            problems.append(f'{s["where"]}: scene {s["id"]} needs at least 2 speakers')
        for r in s['roles']:
            if r not in roles_used:
                problems.append(f'{s["where"]}: scene {s["id"]} declares role {r} that never speaks')
    return lines, scenes


def norm(text):
    return re.sub(r'[^a-z0-9{}]+', ' ', text.lower()).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--verify', action='store_true', help='fail if the committed corpus.json differs from the sources')
    args = parser.parse_args()
    problems, lines, scenes = [], [], []
    files = sorted(CHAT.glob('*.lines'))
    if not files:
        raise SystemExit('No .lines files found in ' + str(CHAT))
    for path in files:
        l, s = parse_file(path, problems)
        lines += l
        scenes += s
    # A sentence may appear in only one scene, or every thread starts sounding the same.
    beat_seen = {}
    for sc in scenes:
        for b in sc['beats']:
            for v in b.get('variants', []):
                key = norm(v)
                if len(key) > 12 and key in beat_seen and beat_seen[key] != sc['id']:
                    problems.append(f'{b["where"]}: same line as scene {beat_seen[key]}: {v}')
                beat_seen.setdefault(key, sc['id'])
        for b in sc['beats']:
            b.pop('where', None)
    seen = {}
    for line in lines:
        key = (line['kind'], norm(line['text']))
        if key in seen:
            problems.append(f'{line["where"]}: duplicate of {seen[key]}: {line["text"]}')
        seen.setdefault(key, line['where'])
    scene_ids = collections.Counter(s['id'] for s in scenes)
    problems += [f'duplicate scene id {k}' for k, n in scene_ids.items() if n > 1]

    # Coverage minimums: every realm and level band needs ambient and lfg material it can use.
    def usable(kind, realm, lo, hi):
        return sum(1 for x in lines if x['kind'] == kind and x['realm'] in ('*', realm)
                   and x['levels'][0] <= hi and x['levels'][1] >= lo)
    bands = [(1, 19), (20, 34), (35, 50)]
    minimum = {'ambient': 60, 'lfg': 15, 'reply': 40, 'greet': 10, 'thanks': 10}
    # Every invite a bot's opener can carry has replies, and every family has closings (0.36-se.4).
    invited = collections.Counter(x['invites'] for x in lines if 'invites' in x)
    answered = collections.Counter(a for x in lines if x['kind'] == 'reply' for a in x.get('answers', []))
    closed = collections.Counter(a for x in lines if x['kind'] == 'closing' for a in x.get('answers', []))
    for invite in sorted(invited):
        if invite in ('none', 'chat'):
            continue
        family = invite.split('_')[0]
        if answered[invite] + answered[family] < 5:
            problems.append(f'coverage: openers invite "{invite}" ({invited[invite]} lines) but only '
                            f'{answered[invite] + answered[family]} replies answer it or "{family}"')
    if invited and sum(answered.values()) and sum(closed.values()) == 0:
        problems.append('coverage: no closing lines')
    for realm in sorted(REALMS):
        for lo, hi in bands:
            for kind, need in minimum.items():
                have = usable(kind, realm, lo, hi)
                if have < need:
                    problems.append(f'coverage: {kind} for {realm} {lo}-{hi} has {have}, needs {need}')

    if problems:
        print(f'{len(problems)} problem(s):')
        for p in problems:
            print('  ' + p)
        raise SystemExit(1)

    by_kind = collections.Counter(x['kind'] for x in lines)
    spoken = [sum(1 for b in s['beats'] if b['role'] != '~') for s in scenes]
    import math
    ways = [math.prod(len(b['variants']) * (2 if b['optional'] else 1) for b in s['beats'] if b['role'] != '~')
            for s in scenes]
    print(f'scene variations: median {sorted(ways)[len(ways) // 2]} ways to play a scene, '
          f'{sum(1 for w in ways if w >= 100)} scenes with 100+ ways')
    print(f'{len(lines)} lines, {len(scenes)} debate scenes, {sum(spoken)} scene lines '
          f'(longest {max(spoken)}, {sum(1 for n in spoken if n >= 20)} scenes of 20+ lines)')
    print('by kind: ' + ', '.join(f'{k} {n}' for k, n in sorted(by_kind.items())))
    print('openers invite: ' + ', '.join(f'{k} {n}' for k, n in sorted(collections.Counter(
        x['invites'] for x in lines if 'invites' in x).items())))
    print('by realm: ' + ', '.join(f'{k} {n}' for k, n in sorted(collections.Counter(x["realm"] for x in lines).items())))
    print('by type (lines that name types): ' + ', '.join(
        f'{t} {sum(1 for x in lines if t in x["who"])}' for t in sorted(TYPES)))
    if args.check:
        return
    out = dict(version=1, note='New lines written for Offline DAoC; see docs/bot-voices. Built by tools/bot-voices/build_corpus.py.',
               lines=[{k: v for k, v in dict(x, id=hashlib.sha1((x['kind'] + '|' + x['text']).encode()).hexdigest()[:12]).items()
                       if k != 'where'} for x in lines],
               scenes=[{k: v for k, v in s.items() if k != 'where'} for s in scenes])
    # Scene beats must never start with a role marker ("?D: ..."): the server sends them as they are.
    markers = [v for sc in out['scenes'] for b in sc['beats'] for v in b.get('variants', [b.get('text', '')])
               if v and re.match(r'^\??[A-F]:\s', v)]
    if markers:
        raise SystemExit(f'{len(markers)} scene line(s) start with a role marker, e.g. {markers[0]!r}')
    if args.verify:
        committed = json.loads(OUT.read_text(encoding='utf-8'))
        if committed != json.loads(json.dumps(out, ensure_ascii=False)):
            raise SystemExit('corpus.json is stale: run python tools/bot-voices/build_corpus.py and commit it')
        print('corpus.json matches its sources')
        return
    OUT.write_text(json.dumps(out, ensure_ascii=False, indent=1) + '\n', encoding='utf-8', newline='\n')
    print('wrote', OUT.relative_to(ROOT))


if __name__ == '__main__':
    main()
