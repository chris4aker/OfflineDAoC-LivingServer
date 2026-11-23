"""The server's writing styles in Python, for preview.py and the builder's style round trip (build_corpus.py --check).

A port of AutonomousBotVoiceCorpus.Style (1.2 chat audit: DESIGN-012, 013, 014, 032; CORPUS-B1-028). The C# code is the
reference: when Style changes, change this file the same way. Both run the cases in tests/style_cases.json
(tests/test_chat_rules.py here, UT_ChatAuditVoice.TheStyleCasesMatchThePythonPort in the server tests). The typing habits (AutonomousChatIdiolect)
are not ported; they never capitalise, stop or add a laugh to a line that already has one.
"""
import functools
import json
import re
from pathlib import Path

MAX_LENGTH = 160  # AutonomousBotVoiceCorpus.MaxLength

CHAT_TOKENS = {
    'lol', 'heh', 'hehe', 'haha', 'ha', 'rofl', 'lmao', 'gz', 'grats', 'gratz', 'ty', 'thx', 'np', 'k', 'kk', 'ok', 'brb',
    'afk', 'wb', 'gn', 'gl', 'omw', 'inc', 're', 'hm', 'hmm', 'huh', 'yw', 'sry', 'nm', 'nvm', 'pst', 'ding', 'lfg', 'lfm',
    'oom', 'med', 'rez', 'r', 'y', 'n', 'yep', 'nope', 'ikr', 'bah', 'ugh', 'doh', 'w00t', 'woot',
    'yes', 'ya', 'yeah', 'yea', 'no', 'nah', 'sure', 'same', 'true', 'nice', 'cool', 'hey', 'hi', 'yo', 'sup', 'cya', 'later',
    'night', 'rip', 'oof', 'phew', 'gg', 'x', 'this', 'agreed',
    'wts', 'wtb', 'wtt', 'lf', 'lf1m', 'lf2m', 'lf3m', 'lf4m', 'pc', 'sm', 'zerk', 'any1', 'ne1', 'gtg', 'bbl', 'bio', 'rdy',
    'plz', 'pls',
}
NO_EMOTE_KINDS = {'spam', 'alarm', 'rez_ask', 'death', 'news', 'combat', 'roleplay', 'beggar', 'lore'}
FUNCTIONAL_RE = re.compile(r"(?<![a-z0-9])(wts|wtb|wtt|lfg|lfm|lf|lf[1-7]m|pc|pst|inc|/send|/tell|/t|/gu|cc'?d|oom|rdy|bio)(?![a-z0-9])",
                           re.IGNORECASE)
LAUGH_RE = re.compile(r'(?<![a-z])(lol|lmao|rofl|haha+|hehe+|heh|ha|hah)(?![a-z])|[:;]-?[)(PpDdOo/\\|]|\^\^|<g>', re.IGNORECASE)
SENTENCE_WORDS = {'this', 'no', 'yes', 'true', 'same', 'sure', 'nice', 'cool', 'later', 'night', 'agreed', 'hey', 'hi'}
SENTENCE_START_RE = re.compile(r'(?<![.!?])([.!?] )([a-z][a-z0-9]*)')


def is_functional(text):
    return bool(text) and bool(FUNCTIONAL_RE.search(text))


def has_laugh(text):
    return bool(text) and bool(LAUGH_RE.search(text))


def is_shouted(text):
    letters = sum(1 for c in text or '' if c.isalpha())
    upper = sum(1 for c in text or '' if c.isupper())
    return letters >= 6 and upper >= letters * 0.6


# Chat audit 1.2.2 (C10, fix F9): AutonomousBotVoiceCorpus.SeriousWords and AsksAndAnswers.
SERIOUS_RE = re.compile(
    r"(?<![a-z])(dunno|idk|not sure|no idea|no clue|beats me|wish i knew|who knows|hard to say|cant say|can't say|couldnt tell|couldn't tell|"
    r"dont know|don't know|cant heal|cant rez|no rez|careful|watch out|watch ur back|watch your back|beware|stay away|dont go alone|"
    r"don't go alone|full raid|needs a raid|need a raid|bring a raid|wipe|wipes|wiped|died|dead|die|dies|death|deadly|dangerous|"
    r"cant go|can't go|trust nothing|careless|raid|rip|sorry for your loss)(?![a-z])", re.IGNORECASE)
ASKS_AND_ANSWERS_RE = re.compile(r'^[^?]{2,60}\?\s+\S+\s+\S+\s+\S')


def is_serious(text):
    """A line no laugh is tacked onto: a "dunno", a warning, a death, or an answer that says the question back first."""
    return bool(text) and bool(SERIOUS_RE.search(text) or ASKS_AND_ANSWERS_RE.search(text))


def takes_emote(kind, text):
    return kind not in NO_EMOTE_KINDS and not is_functional(text) and not has_laugh(text) and not is_shouted(text) and not is_serious(text)


# ---- names (chat audit 1.2.2, C11, fix F10): AutonomousChatNames --------------------------------------------------------------

ROOT = Path(__file__).resolve().parents[2]
SMALL = {'of', 'the', 'na', 'an', 'a', 'and', 'in', 'on', 'at'}
PLACE_STOP = {'barrows', 'coruscating', 'hills', 'plains', 'marsh', 'wall', 'hall', 'keep', 'mine', 'mines', 'tomb', 'cave', 'caves',
              'forest', 'lair', 'den', 'gate', 'city'}
COMMON_WORDS = set('''
air alchemist ambassador ancient annihilator apprentice archivist archwizard bane bartender black blight blue bound brave broodmother burr
cabalist cackler captain chamberlain champion chancellor chief chieftain chosen clan cluster conqueror conservator cook corrupt council
courier crafter crypt dagger den destroyer director dreaded duke earl earth elder emperor empowered energy enlightened eye eyed eyes fairy
faerie fanged fester field fire flame forgotten frat gatekeeper geomancer giant glacier glimmer gnat grand grant green grim gruff grunge
guard guardian hand heavy herald high hills hope host household i inquisitors isolationist fate's king's marshal's mutilators icelord
jailer king knight knights lad lady lair lairmaster leader lecherous legendary legion lich lieutenant lieutenants lord lords magister mark
marquis marsh marshal masher master matriarch matron maw mechanism melancholic metal mighty mistress monger mouth necromantic oak old
organic overman overseer pain pathfinder patrol pet priestess primal prince princess protector prototype provisioner purity queen quiet
ranger reckoner red revenant rob rose runes ruthless sacristan saffron scratch seer sergeant shade shaman silencer silver sister skeletal
smasher soul spectral spirit spiritmaster sporite stripe summoned summoner supply table thane throttler tine touched tower traitor treefall
tusker twirl undead underviewer ursine viewer warlord water weaponmaster webweaver wight winter wizard wraith wrath wretch
'''.split())
BOSS_ALLOW = {'legion', 'green knight'}
NAME_SHAPE_RE = re.compile(r"^[A-Za-z' ]+$")
TOKEN_RE = re.compile(r"[A-Za-z][A-Za-z']*")


def title(phrase):
    words = (phrase or '').split()
    return ' '.join(w if i and w in SMALL else w[0].upper() + w[1:] for i, w in enumerate(words))


def _strip_the(phrase):
    return phrase[4:] if phrase.lower().startswith('the ') else phrase


def build_names(places, locations, bosses):
    """AutonomousChatNames.Build: places are dicts of places.json; locations and bosses are (name, aliases) pairs of the bible."""
    names = {}

    def add(cased):
        cased = re.sub(r'\s+', ' ', _strip_the((cased or '').strip()))
        if len(cased) < 4 or not NAME_SHAPE_RE.match(cased):
            return
        names.setdefault(cased.lower(), cased)
    for realm in ('Albion', 'Midgard', 'Hibernia', 'Avalon'):
        add(realm)
    for name, _aliases in locations:
        add(name)
    for place in places:
        for phrase in [place['key']] + list(place.get('aliases') or []):
            plain = _strip_the((phrase or '').strip().lower())
            if ' ' not in plain and plain in PLACE_STOP:
                continue
            add(title(plain))
    for name, aliases in bosses:
        for phrase in [name] + [a for a in aliases or [] if not a.lower().startswith('the ')]:
            plain = _strip_the((phrase or '').strip())
            lower = plain.lower()
            if lower in BOSS_ALLOW or any(w not in SMALL and w not in COMMON_WORDS for w in lower.split()):
                add(plain if any(c.isupper() for c in plain) else title(plain))
    return names


@functools.lru_cache(maxsize=1)
def proper_names():
    """The world's names from the repository's data (places.json and the lore bible), as the server builds them."""
    auto = ROOT / 'source/server/GameServer/bots/autonomous'
    places = json.loads((auto / 'data/chat/places.json').read_text(encoding='utf-8'))['places']

    def read(folder):
        out = []
        for path in sorted((auto / 'data/lore' / folder).glob('*.json')):
            d = json.loads(path.read_text(encoding='utf-8'))
            out.append((d.get('name', ''), d.get('aliases') or []))
        return out
    return build_names(places, read('locations'), read('bosses'))


def capitalize_names(text, names=None):
    """AutonomousChatNames.Capitalize: every name in its capitals, the longest first; a typed capital is kept."""
    names = proper_names() if names is None else names
    if not text or not names:
        return text
    longest = max(k.count(' ') + 1 for k in names)
    tokens = list(TOKEN_RE.finditer(text))
    out, written, i = [], 0, 0
    while i < len(tokens):
        cased, taken = None, 0
        for n in range(min(longest, len(tokens) - i), 0, -1):
            if any(tokens[k + 1].start() != tokens[k].end() + 1 or text[tokens[k].end()] != ' ' for k in range(i, i + n - 1)):
                continue
            phrase = ' '.join(tokens[k].group(0).lower() for k in range(i, i + n))
            if phrase in names:
                cased, taken = names[phrase], n
                break
            if phrase.endswith("'s") and phrase[:-2] in names:
                cased, taken = names[phrase[:-2]] + "'s", n
                break
        if cased is None:
            i += 1
            continue
        words = cased.split(' ')
        out.append(text[written:tokens[i].start()])
        said = []
        for k in range(taken):
            typed, word = tokens[i + k].group(0), words[k]
            said.append(word[0].upper() + word[1:] if typed[0].isupper() else word)
        out.append(' '.join(said))
        written = tokens[i + taken - 1].end()
        i += taken
    out.append(text[written:])
    return ''.join(out)


def is_comma_list(text):
    parts = (text or '').rstrip('.!?').split(',')
    return len(parts) >= 3 and all(1 <= len(p.split()) <= 2 for p in parts[1:])


def starts_with_chat_token(text):
    m = re.match(r'[a-zA-Z0-9]+', text or '')
    return bool(m) and m.group(0).lower() in CHAT_TOKENS and m.group(0).lower() != 'i'


def is_chat_token(text):
    words = re.sub(r'[^a-zA-Z0-9 ]', ' ', text or '').split()
    return 0 < len(words) <= 3 and all(w.lower() in CHAT_TOKENS for w in words)


def cut_at_word(text, limit=MAX_LENGTH):
    """AutonomousBotVoiceCorpus.Fit: at the last whole word that fits, then "..."."""
    if not text or len(text) <= limit:
        return text
    room = max(1, limit - 3)
    cut = text.rfind(' ', 0, min(room, len(text) - 1) + 1)
    head = (text[:cut] if cut > 0 else text[:room]).rstrip(' ,;:.-')
    return head + '...'


def style(text, voice, rng, kind=None, emote=None):
    """A line in a writing style (voices.json entry). emote: None draws as the server does, True/False forces it."""
    if not text or not voice:
        return text
    result = text
    functional, shouted = is_functional(text), is_shouted(text)
    chat_token = is_chat_token(text) or starts_with_chat_token(text) or functional or shouted
    for k, v in sorted((voice.get('abbreviate') or {}).items(), key=lambda kv: -len(kv[0])):
        result = re.sub(r'(?:(?<=^)|(?<=\s))' + re.escape(k) + r'(?=$|[\s,.?!])', v, result)
    if voice.get('expand') and not chat_token:
        for k, v in voice['expand'].items():
            result = re.sub(r'(?:(?<=^)|(?<=\s))' + re.escape(k) + r'(?=$|[\s,.?!])', v, result)
    if voice.get('strip_punctuation'):
        result = result.rstrip('.') if shouted else result.rstrip('.!')
        if not is_comma_list(result):
            result = result.replace(',', '')
        result = re.sub(r'(?<!\.)\. (?=\S)', ', ', result)
    if voice.get('typo_chance') and rng.random() < voice['typo_chance']:
        words = result.split(' ')
        eligible = [i for i, w in enumerate(words) if len(w) >= 5 and w.isalpha() and w.islower()]
        if eligible:
            i = rng.choice(eligible)
            w = words[i]
            at = 1 + rng.randrange(len(w) - 2)
            words[i] = w[:at] + w[at + 1:] if rng.random() < 0.75 else w[:at] + w[at + 1] + w[at] + w[at + 2:]
            result = ' '.join(words)
    may = takes_emote(kind, text)
    draw = (rng.random() < voice.get('emote_chance', 0)) if emote is None else emote
    if may and voice.get('emotes') and voice.get('emote_chance') and draw:
        if result.endswith('.') and not result.endswith('..'):
            result = result[:-1]
        result += ' ' + rng.choice(voice['emotes'])
    if voice.get('capitalize') and result and not chat_token:
        result = result[0].upper() + result[1:]
    if voice.get('capitalize') and not chat_token:
        result = capitalize_names(result)
    if voice.get('capitalize_sentences') and not chat_token:
        result = SENTENCE_START_RE.sub(lambda m: m.group(0) if m.group(2) in CHAT_TOKENS and m.group(2) not in SENTENCE_WORDS else m.group(1) + m.group(2)[0].upper() + m.group(2)[1:], result)
    if voice.get('end_period') and result and not chat_token and len(result.split()) > 3:
        if not any(result.endswith(e) for e in (voice.get('no_period_after') or ['.', '!', '?'])):
            result += '.'
    return cut_at_word(result)
