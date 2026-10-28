"""Check the bot-voice line files and build corpus.json.

  python tools/bot-voices/build_corpus.py            (check and write corpus.json, print coverage)
  python tools/bot-voices/build_corpus.py --check    (check only; exit 1 on any problem)
  python tools/bot-voices/build_corpus.py --verify   (CI: exit 1 if corpus.json is not what the sources build)

Line files: source/server/GameServer/bots/autonomous/data/chat/*.lines. The format is described in
docs/bot-voices/DESIGN.md (section 3). Every line is new writing for this project.

Lines built from parts: "[a|b|c]" in a line is one of a, b or c ("[a|]" is a or nothing). The build writes each way
of saying it as its own line (all of them up to FAMILY_CAP, else a fixed sample), so the no-repeat window works line by
line as before, and the family shares one line's weight times the square root of its size: a family is picked more
often than a single line, and it rarely says the same words twice (the month simulation, UT_ChatMonth, 2026-10-26).
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
PLACES_FILE = CHAT / 'places.json'

KINDS = {'ambient', 'reply', 'lfg', 'trade', 'rvr', 'greet', 'thanks', 'grudge', 'gossip', 'ding', 'death',
         'camp', 'debate',
         # Living Server (docs/LIVING-SERVER.md)
         'grats', 'rez_ask', 'rez_thanks', 'rez_offer', 'corpse', 'sickness', 'first', 'alarm', 'nemesis', 'revenge',
         'session', 'linkdead', 'combat',
         # 0.36-se.4: group talk (/group with you, /say near you), a starter's closing line, guild idle chat
         'group', 'closing', 'guild',
         # The talking update: news of the world (AutonomousChatNews), and private tells a bot starts
         'news', 'tell',
         # Beggars (AutonomousBeggars): a few Crafters who live on their capital's corners
         'beggar',
         # Deeds (AutonomousPlayerDeeds): a witness tells the realm what the player did, others react
         'deed', 'deed_reply',
         # Storylines (AutonomousBotStorylines): the protagonists' tells to a player who took a side
         'storyline',
         # Gold spammers (AutonomousGoldSpammers) and the realm's reactions; roleplayers (AutonomousRoleplayers)
         'spam', 'spam_react', 'roleplay',
         # True rumours (AutonomousRumours): a real rare spawn or an undefended keep, and reactions
         'rumour', 'rumour_reply',
         # Bonds (AutonomousBotBonds): a bot tells /faction about a friend, a reunion or a no-show, others react
         'bond', 'bond_reply'}
# Lines that start a conversation; each says what kind of answer it invites (@invites, or inferred below).
OPENER_KINDS = {'ambient', 'lfg', 'trade', 'rvr', 'camp', 'group', 'guild', 'news', 'tell'}
# Kinds whose lines speak about the speaker's own here and now, so a place they name must be true (or @about).
CLAIM_KINDS = OPENER_KINDS | {'ding', 'death', 'first', 'session', 'combat', 'news'}
# What an opener invites; replies say what they answer (@answers). "ask_<topic>" is a specific question that
# falls back to the "ask" replies; "none" gets no reply.
INVITES = {'none', 'chat', 'joke', 'boast', 'complaint', 'news', 'lfg', 'trade', 'ask', 'ask_where', 'ask_advice',
           'ask_opinion', 'ask_anyone', 'ask_howareyou', 'ask_permission', 'ask_choice', 'ask_doing', 'ask_plans'}
# "news_<topic>" (the talking update): a reaction answers that piece of news, then any news. "followup_<word>": a bot's
# answer when a player asks "why?", "really?", "how?", "what?" or "where?" right after the bot's own line.
# "player_<shape>" (the talking update audit): replies to a kind of player statement the reply path reads (good news,
# an offer of help or items, a request to wait).
INVITE_RE = re.compile(r'(none|chat|joke|boast|complaint|news(_[a-z0-9]+)*|lfg|trade|ask(_[a-z0-9]+)*|followup_[a-z]+|player_[a-z_]+|debate_[a-z]+)')
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
         'will_rez',   # will_rez: set only when answering a rez ask the bot will really come to
         'player_rezzed_me',   # gossip: the player really rezzed this bot (bot memory)
         'player_leveling',    # tells: the player is under the level cap (lines about levelling; in-game check 2026-10-27)
         # The talking update: what is true in the world right now (AutonomousChatNews and the RvR event layer).
         'df_ours',            # the bot's realm holds Darkness Falls
         'defending',          # one of the realm's keeps is under siege now ({keep} is that keep)
         'attacking',          # the realm's army is besieging an enemy keep now ({keep} is the target)
         'keep_news',          # a frontier keep changed hands in the last hour
         'keep_lost_recent', 'keep_won_recent',   # the realm lost or took a keep in the last hour ({keep})
         'relic_lost_recent', 'relic_won_recent',  # the realm lost or took a relic in the last few hours ({relic})
         'raid_won_recent', 'raid_wiped_recent',   # a realm raid won or wiped in the last hour ({raid}, {boss})
         'df_changed_recent',  # Darkness Falls changed hands in the last hour
         'in_combat',          # the bot is fighting right now
         'witness',            # news: the bot was at the place when it happened
         'recruiting',         # lfg: the bot's group is really looking for members ({need} is what it needs)
         'xp_low', 'xp_mid', 'xp_high',   # under 2 bubbles, 2-7, 8+ into the level ({bubbles})
         'is_healer', 'is_tank', 'is_caster', 'is_support',   # the bot's role in a group
         'newbie', 'veteran',  # level 1-14, level 40+ (voices.json experience tiers)
         # Holiday chatter (AutonomousHolidays): the days around a holiday, and the day itself
         'holiday', 'holiday_today', 'holiday_halloween', 'holiday_thanksgiving', 'holiday_christmas', 'holiday_newyear',
         'holiday_valentines', 'holiday_stpatricks', 'holiday_aprilfools', 'holiday_easter', 'holiday_midsummer'}
# Places a line may claim the speaker is at, heading to, or either (@needs at:<place>, going:<place>, near:<place>).
PLACE_NEED_RE = re.compile(r'(at|going|near):(.+)')


def load_places():
    data = json.loads(PLACES_FILE.read_text(encoding='utf-8'))['places']
    aliases = sorted(((a, p) for p in data for a in p['aliases']), key=lambda x: -len(x[0]))
    return {p['key']: p for p in data}, [(re.compile(r"(?<![a-z'])" + re.escape(a) + r"(?![a-z])"), p) for a, p in aliases]


PLACES, PLACE_ALIASES = load_places()


def places_in(text):
    """The places a text names, by whole-word alias (longest first, so "tomb of mithra" is not also "mithra")."""
    t = SLOT_RE.sub(' ', text.lower())
    found = []
    for pattern, place in PLACE_ALIASES:
        if pattern.search(t):
            if place['key'] not in (f['key'] for f in found):
                found.append(place)
            t = pattern.sub(' ', t)
    return found
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
         # 0.36-se.4: the plural of {monster} ("2 hill cats"), and where a travelling bot is going
         'monsters', 'destination',
         # The talking update: what a piece of news is about
         'relic', 'raid', 'boss', 'winner', 'loser', 'bubbles', 'need',
         # Beggars: a gift or sale ({coin}) and the beggar's whole purse ({hoard})
         'coin', 'hoard',
         # Deeds: the player's earned nickname; storylines: the two protagonists; spammers: the fake site
         'nickname', 'name_a', 'name_b', 'site',
         # Bonds: the other gamebot and its class
         'other', 'other_class'}
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
                slot == 'player' and tags['kind'] in ('reply', 'closing')) and not (
                tags['kind'] in ('beggar', 'deed', 'deed_reply', 'storyline', 'rumour', 'rumour_reply', 'bond', 'bond_reply')) and not (
                tags['kind'] == 'debate' and str(tags.get('topic', '')).startswith('story_')):
                # in a reply, {player} is whoever is answered; a beggar's, whoever it begs or chases; a deed's, who did it;
                # a storyline's, the player who took a side
            problems.append(f'{where}: memory slot {{{slot}}} needs @needs knows_player or a memory kind')
    if text.count('{') != text.count('}'):
        problems.append(f'{where}: unbalanced braces')
    hit = BANNED_RE.search(SLOT_RE.sub('', text))
    if hit:
        problems.append(f'{where}: out-of-era or banned word "{hit.group(0)}": {text}')
    if PLURAL_MONSTER_RE.search(text):
        problems.append(f'{where}: {{monster}} is one monster; write {{monsters}} for the plural: {text}')
    needs = set(tags.get('needs', []))
    named = places_in(text)
    # The talking update: a line that names a place either claims the speaker is there or on the way (@needs at:,
    # going:, near:) or says it is only talking about the place (@about place). Live 2026-10: "heading into myrkwood"
    # was said from anywhere in Midgard, and darkness falls lines from inside Tomb of Mithra.
    if named and tags['kind'] in CLAIM_KINDS and not tags.get('about'):
        claimed = {PLACE_NEED_RE.fullmatch(n.lstrip('!')).group(2) for n in needs if PLACE_NEED_RE.fullmatch(n.lstrip('!'))}
        if not claimed & {p['key'] for p in named}:
            problems.append(f'{where}: names {", ".join(p["key"] for p in named)}; add @needs at:/going:/near:<place> '
                            f'if the speaker is there, or @about place if it only talks about it: {text}')
    for place in named:
        if place['realm'] != '*' and place['type'] not in ('frontier', 'capital') and tags.get('realm', '*') != place['realm']:
            problems.append(f'{where}: names {place["key"]} ({place["realm"]}) in a line for realm {tags.get("realm", "*")}: {text}')
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
        place = PLACE_NEED_RE.fullmatch(f.lstrip('!'))
        if place:
            if place.group(2) not in PLACES:
                problems.append(f'{where}: unknown place {place.group(2)} (see places.json)')
        elif f.lstrip('!') not in FLAGS:
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
                intent='', mood='', weight=1.0, invites='', answers=[], about=False)


DOING_RE = re.compile(r"\bwhat (is|are|r) (every(one|body)|you|u|yall|you (all|guys)|u guys)( all)? (up to|doing)\b|"
                      r"\bwhats (every(one|body)|you|u) (up to|doing)\b|\bwhat (you|u) (up to|doing)\b|\bwyd\b")
LATER_RE = re.compile(r"\b(tonight|later|tomorrow|this weekend|after)\b")


NOT_A_CHOICE_RE = re.compile(
    r"\bor (something|anything|what|so|not|is it just me|is that just me|just me|is it me|am i|is that a myth|is that true|just a|no|nah|"
    r"is it|is that|a bot|bot|an npc|a waste|waste|real|human|a person|a real)\b|\b(real|human) or\b")


def infer_invites(text, kind):
    """What an opener without @invites invites: questions by their shape, otherwise a light reaction."""
    t = SLOT_RE.sub('x', text.lower()).strip()
    # "anyone know who camps x? want to take turns": the question is what comes before the last "?" (live test 2026-10-18).
    # lfg and trade lines keep their own answers ("want to join us? sending an invite").
    if '?' in t and kind not in ('lfg', 'trade'):
        t = t[:t.rfind('?') + 1]
    if t.endswith('?'):
        if re.search(r"^((hey|hi|hello|heya|so|ok|yo|evening|morning)( all| everyone| guys| guildies| folks)?[ ,!]+|just got (home|on|back|in)[ ,]+)?(how (is|are|r) (every|you|u|ya|yall|things|it)|hows (every|it|things)|how goes|"
                     r"what did i miss|whats new|whats up)", t):
            return 'ask_howareyou'
        if re.search(r"\b(mind if i|ok if i|okay if i|is it ok if)\b", t):
            return 'ask_permission'
        # Not a choice: "or just me", "or not", "or is it a myth", "real or scripted" (the same list as
        # AutonomousChatShortTalk.IsEitherOr; in-game check 2026-10-27: "anyone else waiting on a group or just me?" drew
        # "whichever is more fun").
        if re.search(r"\bor\b", t) and not NOT_A_CHOICE_RE.search(t):
            return 'ask_choice'
        # "where to next?": where the group goes, answered "your call" or "somewhere with more xp", not directions.
        if re.search(r"\bwhere (to next|now|next|are we (heading|going|off to)|should we (go|head)|we (going|heading))\b", t):
            return 'ask_next'
        if re.search(r"\bwhere\b|\bwheres\b|how do (i|you) get|which way|how far", t):
            return 'ask_where'
        if re.search(r"\b(best(?! (thing|part|moment|day))|better|worth it|what do (you|u) think|thoughts on|should i|is it worth|favorite|favourite)\b", t):
            return 'ask_opinion'
        # "tell me your best death story": a story, not "pretty sure yes" (live test 2026-10-18).
        if re.search(r"\b(tell me|whats|what is) your (best|worst|favorite|funniest|dumbest)\b|\bbest thing that happened\b", t):
            return 'ask_story'
        # "what level is everyone at?", "how close are you to a level?", "what are we doing this weekend?"
        if re.search(r"\bwhat level (is|are) (every|you|u|yall)", t):
            return 'ask_level'
        if re.search(r"\bhow close (are|r) (you|u|yall|every)", t):
            return 'ask_xp'
        if re.search(r"\bwhat (are|r) we (doing|up to)\b", t):
            return 'ask_plans'
        # "what is everyone up to?": what each is doing now ("camping x", "heading to y"), or, with "tonight" or
        # "later", their plans. Before 0.37-se.1 it read as "how is everyone?" and drew "not bad, and you?".
        if DOING_RE.search(t):
            return 'ask_plans' if LATER_RE.search(t) else 'ask_doing'
        # "castle excalibur still ours?", "how much do merchants pay for x?": what someone may know
        if re.search(r"\b(still ours|still holding|ours right now|did we lose|how much do (merchants|vendors) pay)\b", t):
            return 'ask_know'
        # "anyone know if x is ours?": a question about the world, answered "no idea" or "last i saw" (live test 2026-10-18).
        if re.search(r"\b(anyone|anybody|any1|does anyone|someone) (know|knows|seen|heard)\b", t):
            return 'ask_know'
        # "anyone else up at this hour?": company, answered "same here", not "sorry, busy".
        if re.search(r"\b(anyone|anybody|any1) else\b", t):
            return 'chat'
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
            elif key == 'exit':
                # "@exit A": when the scene plays to its end, the bot cast as A logs off for a few hours (the rage quit).
                if scene is None or not re.fullmatch(r'[A-H]', value):
                    problems.append(f'{where}: @exit takes one role (A-H) inside a scene')
                    continue
                scene['exit'] = value
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
                    elif key == 'about':
                        # "@about place": the following lines name places without claiming to be there; "@about" ends it.
                        if value not in ('', 'place'):
                            problems.append(f'{where}: @about takes "place" or nothing')
                        tags['about'] = value == 'place'
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
            m = re.fullmatch(r'(\?)?([A-H]):\s*(.+)', text)
            if not m:
                problems.append(f'{where}: scene beats look like "A: text" (roles A-H, "?" for optional)')
                continue
            optional, role, beat = m.groups()
            variants = [v.strip() for v in beat.split(' | ')]
            if any(not v for v in variants):
                problems.append(f'{where}: empty alternative')
            for v in variants:
                check_text(v, dict(kind='debate', needs=scene['needs'], realm=scene['realm'], topic=scene.get('topic') or ''), where, problems)
                if re.match(r'\??[A-H]:\s', v):
                    problems.append(f'{where}: an alternative starts with a role marker: {v}')
            scene['beats'].append(dict(role=role, text=variants[0], variants=variants, optional=bool(optional),
                                       where=where))
            continue
        check_tags(tags, where, problems)
        if '[' in text and not CHOICE_RE.search(text):
            problems.append(f'{where}: "[" without a "|" choice: {text}')
        family = expand(text)
        weight = tags['weight'] * (len(family) ** 0.5) / len(family)
        for said in family:
            check_text(said, tags, where, problems)
            line = dict(tags, text=said, where=where, **{'class': list(tags['class'])})
            # Kept only when set and the line names a place, so a test can tell talk about a place from a claim to be there.
            if not (line.pop('about') and places_in(said)):
                pass
            else:
                line['about'] = True
            if len(family) > 1:
                line['weight'] = round(weight, 4)
                line['_family'] = True
            if tags['kind'] in OPENER_KINDS:
                line['invites'] = tags['invites'] or infer_invites(said, tags['kind'])
                # A plain "ask" tag takes a more specific question type when the words show one ("how is everyone?").
                if line['invites'] == 'ask' and infer_invites(said, tags['kind']).startswith('ask_'):
                    line['invites'] = infer_invites(said, tags['kind'])
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
        if s.get('exit') and s['exit'] not in roles_used:
            problems.append(f'{s["where"]}: scene {s["id"]} exits role {s["exit"]}, which never speaks')
        for r in s['roles']:
            if r not in roles_used:
                problems.append(f'{s["where"]}: scene {s["id"]} declares role {r} that never speaks')
    return lines, scenes


FAMILY_CAP = 48
CHOICE_RE = re.compile(r'\[([^\[\]]*\|[^\[\]]*)\]')


def expand(text):
    """The ways to say a line with "[a|b]" parts: all of them up to FAMILY_CAP, else a fixed sample. Pure."""
    import itertools
    import random
    groups = [m.group(1).split('|') for m in CHOICE_RE.finditer(text)]
    if not groups:
        return [text]
    pieces = CHOICE_RE.split(text)
    # CHOICE_RE.split gives text, group, text, group...: rebuild from a choice per group.
    def build(choice):
        out = []
        for i, piece in enumerate(pieces):
            out.append(choice[i // 2] if i % 2 else piece)
        return re.sub(r'\s+', ' ', ''.join(out)).strip().replace(' ,', ',').replace(' ?', '?').replace(' !', '!')
    total = 1
    for g in groups:
        total *= len(g)
    if total <= FAMILY_CAP:
        combos = itertools.product(*groups)
    else:
        rng = random.Random(hashlib.sha1(text.encode()).hexdigest())
        picked = set()
        while len(picked) < FAMILY_CAP:
            picked.add(tuple(rng.choice(g) for g in groups))
        combos = sorted(picked)
    seen, out = set(), []
    for combo in combos:
        said = build(combo)
        if said and norm(said) not in seen:
            seen.add(norm(said))
            out.append(said)
    return out


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
    # One way of saying a family line that is already a line of its own ("[ha|heh], too true" and "ha, too true") is
    # dropped quietly: the family still has its other ways. A plain line said twice is a mistake.
    seen, kept = {}, []
    for line in sorted(lines, key=lambda x: x.get('_family', False)):
        key = (line['kind'], norm(line['text']))
        if key in seen:
            if line.get('_family'):
                continue
            problems.append(f'{line["where"]}: duplicate of {seen[key]}: {line["text"]}')
        seen.setdefault(key, line['where'])
        kept.append(line)
    order = {id(x): i for i, x in enumerate(lines)}
    lines = sorted(kept, key=lambda x: order[id(x)])
    for line in lines:
        line.pop('_family', None)
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
               if v and re.match(r'^\??[A-H]:\s', v)]
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
