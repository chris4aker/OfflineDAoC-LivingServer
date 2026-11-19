"""The 1.2 chat audit's builder checks (DESIGN-045 a-i, CORPUS-A-025, CORPUS-C-005/044, CORPUS-B1-001/002/016/017/022,
TEXT-018), run by build_corpus.py on every corpus line and scene beat.

Each check reports through Audit: a hit in a file whose owner is in STRICT_OWNERS is a problem (the build fails); a hit in any
other file is a warning, printed grouped by owner, until that owner's fixes are in. The coordinator makes them failures
everywhere by adding the owners to STRICT_OWNERS (or setting it to ALL_OWNERS).
"""
import collections
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SERVER = ROOT / 'source/server/GameServer'
VOCABULARY = HERE / 'vocabulary.txt'

# Who edits which line file (scratchpad CHAT-FIX-BRIEF.md, OWNERSHIP). A file no pattern names is "unowned".
# The 1.2 content added after the audit has owners too (chat follow-up F3): tales_more (E6, the tales), storylines_more (E4,
# the storylines) and scenes_live (E8, the scenes). A test fails if a new line file has none.
OWNERS = [
    ('E3', r'ambient_.*|classes.*|personalities|experience|capitals|camp_more|dungeons|moments|extras|living_server|more_realms|'
           r'battlegrounds|battleground_lore|rvr|rvr_more'),
    ('E4', r'storylines|dunce|deeds|news|news_replies|rumours|era_rumours|holidays|spammers|roleplayers|beggars|charity|bonds|'
           r'milestones|memory|memory_more|storylines_more'),
    ('E5', r'replies.*|followups|closings.*|talking_audit|tells|lfg_.*|group_chatter|group_talk|bot_led_groups|combat_calls|'
           r'guild_chat.*|guild_more|trade_crafting|bot_trade|craft_commerce|craft_drama|crafters'),
    ('E6', r'tales|tales_more|lore_.*|data/lore/.*'),
    ('E8', r'debates.*|gen_.*|threads_long|scenes_live'),
]
ALL_OWNERS = {owner for owner, _ in OWNERS} | {'unowned'}
# The owners whose files fail on these checks. E3's PR fixed its own files; the others' hits are warnings until their PRs merge.
STRICT_OWNERS = {'E3'}


def owner_of(where):
    name = where.split(':', 1)[0].split(' ', 1)[0]
    if name.startswith('source/server/GameServer/bots/autonomous/data/lore/'):
        return 'E6'
    stem = name[:-len('.lines')] if name.endswith('.lines') else name
    for owner, pattern in OWNERS:
        if re.fullmatch(pattern, stem):
            return owner
    return 'unowned'


class Audit:
    """Collects the audit checks' hits: problems for strict owners, warnings for the rest."""

    def __init__(self, problems, strict=None):
        self.problems = problems
        self.strict = STRICT_OWNERS if strict is None else strict
        self.warnings = collections.defaultdict(list)

    def __call__(self, where, message, unsure=False):
        owner = owner_of(where)
        if not unsure and (owner in self.strict or 'ALL' in self.strict):
            self.problems.append(f'{where}: {message}')
        else:
            self.warnings[owner].append(f'{where}: {message}' + (' (unsure: a person decides)' if unsure else ''))

    def report(self, show):
        total = sum(len(v) for v in self.warnings.values())
        if not total:
            return
        print(f'{total} warning(s) from the 1.2 chat audit checks (not failures for these owners yet): ' +
              ', '.join(f'{o} {len(v)}' for o, v in sorted(self.warnings.items())))
        if show:
            for owner, items in sorted(self.warnings.items()):
                print(f'  -- {owner} ({len(items)})')
                for item in items:
                    print('    ' + item)
        else:
            print('  (python tools/bot-voices/build_corpus.py --check --warnings lists them)')


SLOT_RE = re.compile(r'\{([^{}]*)\}')

# ---- (a) banned entries beyond the always-banned ones --------------------------------------------------------------------


def banned_audit(text, tags, where, audit, entries):
    said = SLOT_RE.sub('', text)
    allowed = set(tags.get('allow') or [])
    for entry in entries:
        if entry['level'] == 'ban' or entry['id'] in allowed:
            continue
        if entry['id'] == 'url' and tags.get('kind') == 'spam':
            continue
        hit = entry['regex'].search(said)
        if hit:
            audit(where, f'"{hit.group(0)}" ({entry["id"]}: {entry["why"]}): {text}', unsure=entry['level'] == 'unsure')


# ---- (b) length with the longest real slot values (DESIGN-014) --------------------------------------------------------------
# The server's limit is AutonomousBotVoiceCorpus.MaxLength (160); a style may add an emote of up to 5 characters.
MAX_FILLED = 155
WORST = {'me': 20, 'player': 20, 'other': 20, 'target': 20, 'killer': 24, 'name_a': 20, 'name_b': 20, 'customer': 20,
         'class': 12, 'player_class': 12, 'other_class': 12, 'killer_class': 12, 'level': 2, 'realm': 8, 'zone': 22,
         'place': 22, 'camp': 22, 'dungeon': 23, 'destination': 22, 'monster': 28, 'monsters': 29, 'npc': 24, 'item': 40,
         'stock': 40, 'order': 40, 'recipe': 40, 'count': 2, 'capital': 10, 'enemy': 4, 'enemy_mod': 3, 'enemy_realm': 8, 'keep': 18,
         'frontier': 18, 'q': 19, 'relic': 24, 'raid': 24, 'boss': 20, 'winner': 8, 'loser': 8, 'bubbles': 1, 'need': 12,
         'coin': 12, 'hoard': 12, 'nickname': 30, 'site': 24, 'what': 30, 'size': 3, 'classes': 40, 'price': 10, 'title': 30,
         'skill': 4, 'quality': 3, 'budget': 10, 'when': 14, 'terms': 28}


def worst_length(text):
    return len(SLOT_RE.sub(lambda m: 'x' * WORST.get(m.group(1), 20), text))


def length_audit(text, tags, where, audit):
    n = worst_length(text)
    if n > MAX_FILLED:
        audit(where, f'{n} characters with the longest real slot values (max {MAX_FILLED}; the server cuts at 160): {text}')


# ---- (c) realm and class sanity (DESIGN-045 c) -------------------------------------------------------------------------------
REALM_CLASSES = {
    'alb': 'armsman paladin mercenary reaver infiltrator minstrel cleric friar wizard theurgist sorcerer cabalist necromancer',
    'mid': 'thane skald berserker shadowblade shaman runemaster spiritmaster bonedancer',
    'hib': 'champion blademaster druid nightshade eldritch enchanter mentalist animist valewalker',
}
CLASS_REALM = {c: r for r, cs in REALM_CLASSES.items() for c in cs.split()}
OWN_CLASS_RE = re.compile(r"\b(my|as an?|im an?|i'm an?|i am an?|being an?|playing an?|rolled an?|on my)\s+([a-z]+)")


def class_audit(text, tags, where, audit):
    if tags.get('realm', '*') != '*' or tags.get('class'):
        return
    for m in OWN_CLASS_RE.finditer(SLOT_RE.sub(' ', text.lower())):
        word = m.group(2)
        cls = word[:-1] if word.endswith('s') and word[:-1] in CLASS_REALM else word
        if cls in CLASS_REALM:
            audit(where, f'a line for every realm and class says "{m.group(0)}" ({cls} is {CLASS_REALM[cls]} only): add @realm '
                         f'or @class: {text}')
            return


# ---- (d) promise words only where the code acts (DESIGN-045 d, DESIGN-006) ---------------------------------------------------
PROMISE_RE = re.compile(r"\b(omw|otw|on my way|ill be there|i'll be there|inv sent|sending (an |you an |u an )?inv(ite)?)\b")
ACTING_NEEDS = {'will_rez', 'lead_call', 'bot_leads', 'took_order', 'recruiting', 'traveling'}
ACTING_KINDS = {'rez_offer'}


def promise_audit(text, tags, where, audit):
    hit = PROMISE_RE.search(SLOT_RE.sub(' ', text.lower()))
    # "on my way to {destination}" says where a travelling bot really goes.
    if hit and '{destination}' not in text and tags.get('kind') not in ACTING_KINDS and not ACTING_NEEDS & {n.lstrip('!') for n in tags.get('needs', [])}:
        audit(where, f'promises "{hit.group(0)}" but nothing in the code does it (needs will_rez, lead_call, bot_leads, '
                     f'took_order or recruiting): {text}')


# ---- TEXT-018: {enemy} is the people ("hibs"), {enemy_realm} the realm's name ----------------------------------------------
ENEMY_NOUNS = (r'zergs?|groups?|grps?|army|armies|stealthers?|archers?|healers?|casters?|tanks?|assassins?|scouts?|8mans?|fgs?|'
               r'players?|keeps?|relics?|side|realm|guards?|killers?|buffbots?|mezzers?|bards?|clerics?|druids?|shamans?|'
               r'heals|ganker|gankers|warriors?|zerkers?|thanes?|mids?|minstrels?|necros?|chars?|alts?|kids?|templars?')
ENEMY_BAD_RE = re.compile(r"\{enemy\} (is|has|was|holds|owns)\b|\{enemy\} (" + ENEMY_NOUNS + r")\b|\bthe \{enemy_realm\} (" + ENEMY_NOUNS + r")\b",
                          re.IGNORECASE)


def enemy_audit(text, tags, where, audit):
    m = ENEMY_BAD_RE.search(text)
    if m:
        audit(where, f'"{m.group(0)}": {{enemy}} is the people ("hibs"); before a noun write {{enemy_mod}} ("hib stealther"), '
                     f'for the realm {{enemy_realm}} ("Hibernia"): {text}')


# ---- CORPUS-A-025 ------------------------------------------------------------------------------------------------------------
# The frontier's keeps (1.65): a line that names one claims something about it now, so it needs a siege or keep-news flag.
KEEPS = ['caer benowyc', 'caer berkstead', 'caer erasleigh', 'caer boldiam', 'caer sursbrooke', 'caer renaris', 'caer hurbury',
         'castle excalibur', 'castle myrddin', 'bledmeer faste', 'nottmoor faste', 'hlidskialf faste', 'blendrake faste',
         'glenlock faste', 'fensalir faste', 'arvakr faste', 'mjollner faste', 'grallarhorn faste', 'dun crauchon',
         'dun crimthain', 'dun bolg', 'dun da behnn', 'dun nged', 'dun scathaig', 'dun ailinne', 'dun lamfhota', 'dun dagda']
KEEP_RE = re.compile(r"(?<![a-z])(" + '|'.join(re.escape(k) for k in KEEPS) + r")(?![a-z])", re.IGNORECASE)
KEEP_NEEDS = {'defending', 'attacking', 'keep_news', 'keep_lost_recent', 'keep_won_recent'}
CLAIMS_ABOUT_KEEP_RE = re.compile(r"\b(under attack|getting hit|is hit|needs defenders|fell|falling|fallen|lost|taken|took|"
                                  r"holding|still ours|ours|theirs|changing hands|under siege|sieged|door is down|retak)", re.IGNORECASE)


def keep_audit(text, tags, where, audit, claim_kinds):
    if tags.get('kind') not in claim_kinds or tags.get('about') or '?' in text:
        return   # a question ("castle excalibur still ours?") claims nothing
    m = KEEP_RE.search(text)
    needs = {n.lstrip('!') for n in tags.get('needs', [])}
    if m and CLAIMS_ABOUT_KEEP_RE.search(text) and not needs & KEEP_NEEDS:
        audit(where, f'says how {m.group(1)} stands now with no siege or keep flag (@needs defending, attacking, keep_news, '
                     f'keep_*_recent; or {{keep}}, or @about place): {text}')


MONSTER_AT_CAMP_RE = re.compile(r'\{monsters?\}.*\{camp\}|\{camp\}.*\{monsters?\}')
IN_CAPITAL_RE = re.compile(r'\b(in|at|around) \{capital\}')
IN_CAPITAL_CLAIM_RE = re.compile(r"\b(i'?m|im|i am|we'?re|were|taking a break|sitting|standing|stuck|here) (in|at|around) \{capital\}")


def slot_place_audit(text, tags, where, audit, opener_kinds):
    needs = {n.lstrip('!') for n in tags.get('needs', [])}
    if MONSTER_AT_CAMP_RE.search(text) and 'at_camp' not in needs and tags.get('kind') in opener_kinds:
        audit(where, f'{{monster}} with {{camp}}: {{camp}} is only where the bot is when it is at a camp; add @needs at_camp: {text}')
    if IN_CAPITAL_CLAIM_RE.search(text) and 'in_capital' not in needs and tags.get('kind') in opener_kinds:
        audit(where, f'claims to be in {{capital}}; add @needs in_capital (or say "in town"): {text}')


# Single-word choices side by side build broken sentences ("snowdonia [is|are] [cold|colder] than"): CORPUS-A-002.
CHOICE_GROUP_RE = re.compile(r'\[([^\[\]]*\|[^\[\]]*)\]')
VERB_PAIRS = [{'is', 'are'}, {'was', 'were'}, {'has', 'have'}, {'does', 'do'}, {'doesnt', 'dont'}]


def _mixed_verbs(group):
    words = {x.strip() for x in group}
    return any(len(pair & words) == 2 or (pair & words and any(w.endswith('s') for w in words if w not in pair)) for pair in VERB_PAIRS)


def _mixed_number(group):
    words = [x.strip() for x in group if x.strip()]
    return any(w.startswith(('a ', 'an ', 'one ')) for w in words) and any(not w.startswith(('a ', 'an ', 'one ')) for w in words) or \
        len({w.endswith('s') for w in words if len(w.split()) == 1}) == 2


def agreement_audit(template, where, audit):
    """CORPUS-A-002: a subject choice that mixes singular and plural ("[lurikeens|a lurikeen]") next to a verb choice
    ("[are|is]") builds broken sentences; write whole-phrase choices. Pure check, a warning."""
    pieces = CHOICE_GROUP_RE.split(template)
    groups = [(i, pieces[i].split('|')) for i in range(1, len(pieces), 2)]
    for (i, a), (j, b) in zip(groups, groups[1:]):
        if pieces[i + 1].strip():
            continue
        if _mixed_number(a) and (_mixed_verbs(b) or any(x.strip() in ('is', 'are', 'was', 'were', 'has', 'have') for x in b)):
            audit(where, f'"[{"|".join(a)}] [{"|".join(b)}]": a subject choice that mixes one and many next to a verb choice '
                         f'builds broken sentences; write whole-phrase choices: {template}', unsure=True)
            return


# ---- CORPUS-C-005: question types inferred too widely (the fix is in build_corpus.infer_invites); A-025 ask_where -----------
ASK_WHERE_NO_PLACE_RE = re.compile(r"^(how do i get my|how do i get (to )?(a|my|new|more)\b|where do i (spend|sell|put|get my|learn))")


def invite_audit(text, invites, where, audit, places_in):
    if invites == 'ask_where' and ASK_WHERE_NO_PLACE_RE.search(text.lower()) and not places_in(text):
        audit(where, f'guessed ask_where (answered with directions) but names no place; set @invites: {text}')


# ---- CORPUS-B1-001, 002, 017 and the scenes' near-duplicates (B1-022) --------------------------------------------------------
ROLE_LETTER_RE = re.compile(r"(?<![A-Za-z0-9'])([B-H]|A(?= (says|said|has|is|was|learned|knows|thinks|died)\b))(?![A-Za-z0-9'])")


Q_SLOT_RE = re.compile(r'\{q\}\s*(.*)$')
Q_DOUBLE_RE = re.compile(r'^,?\s*(serious|honest|real|quick|random|dumb|stupid) question\b')


def beat_audit(text, where, audit):
    said = SLOT_RE.sub(' ', text)
    letters = sum(1 for c in said if c.isalpha())
    shouted = letters >= 6 and sum(1 for c in said if c.isupper()) >= letters * 0.6
    m = None if shouted else ROLE_LETTER_RE.search(said)
    if m:
        audit(where, f'a capital role letter "{m.group(1)}" is said as a name (the server never fills role letters): {text}')
    q_audit(text, where, audit)


def q_audit(text, where, audit):
    m = Q_SLOT_RE.search(text)
    if not m:
        return
    rest = m.group(1).strip().lower()
    if Q_DOUBLE_RE.search(rest):
        audit(where, f'{{q}} ("quick question,", "random question but") before another question opener: {text}')
    elif '?' not in rest:
        audit(where, f'{{q}} ("quick question,") before a statement; {{q}} goes only before a question: {text}')


# Times of day the old TIME_WORDS_RE did not know (CORPUS-B1-017): audited, so others' lines warn until fixed.
TIME_WORDS_NEW_RE = re.compile(r"\b(\d ?[ap]m|night all|gn|sleep well|quiet night|slow night|then bed|dinner|breakfast|"
                               r"at this hour|evening all|morning all|morning everyone)\b", re.IGNORECASE)
TIME_FLAGS = {'day', 'evening', 'night'}


def time_audit(text, needs, where, audit):
    m = TIME_WORDS_NEW_RE.search(SLOT_RE.sub(' ', text))
    if m and not any(f.lstrip('!') in TIME_FLAGS for f in needs):
        audit(where, f'says a time of day ("{m.group(0)}"); add @needs evening, night, day or !day: {text}')


def words_of(text):
    return [w for w in re.findall(r"[a-z0-9']+", SLOT_RE.sub(' ', text.lower())) if w]


def overlap(a, b):
    sa, sb = set(a), set(b)
    if not sa or not sb:
        return 0.0
    return len(sa & sb) / max(len(sa), len(sb))


def scene_duplicates_audit(scenes, audit):
    """A beat sharing 80% or more of its words with another scene's beat (CORPUS-B1-022); beats of five words or more.
    Candidates come from the beat's rarest words: two sets that overlap by 80% share one of any 21% of either's words."""
    beats = []
    for sc in scenes:
        for b in sc['beats']:
            for v in b.get('variants', []):
                w = set(words_of(v))
                if len(w) >= 5:
                    beats.append((sc['id'], b.get('where', sc.get('where', '?')), v, w))
    freq = collections.Counter(word for beat in beats for word in beat[3])
    index = collections.defaultdict(list)
    for n, (sid, where, v, w) in enumerate(beats):
        keys = sorted(w, key=lambda word: (freq[word], word))[:len(w) // 4 + 1]
        best = None
        for key in keys:
            for m in index[key]:
                if beats[m][0] != sid and overlap(w, beats[m][3]) >= 0.8:
                    best = m
                    break
            if best is not None:
                break
        if best is not None:
            audit(where, f'shares 80% of its words with scene {beats[best][0]} ("{beats[best][2]}"): {v}')
        for key in keys:
            index[key].append(n)


# ---- (e) near-duplicate lines by skeleton (a port of AutonomousChatFiller.Skeleton) ------------------------------------------
INTERJECTIONS = set('ha haha hah heh hehe lol lmao rofl yes yep yeah ya yea yup oh hmm hm ah ok k kk so well wow man dude honestly '
                    'tbh imo hey hi yo and but just really pretty very d p xd'.split())


def skeleton(text):
    words = []
    for w in re.findall(r"[a-z0-9']+", text.lower()):
        w = re.sub(r'\d+', '#', w.replace("'", ''))
        if w:
            words.append(w)
    start, end = 0, len(words)
    while start < end and words[start] in INTERJECTIONS:
        start += 1
    while end > start and words[end - 1] in INTERJECTIONS:
        end -= 1
    return ' '.join(words) if start >= end else ' '.join(words[start:end])


def duplicates_audit(lines, audit):
    by = collections.defaultdict(list)
    for line in lines:
        sk = skeleton(SLOT_RE.sub(lambda m: m.group(1), line['text']))
        if len(sk) > 12:
            by[sk].append(line)
    for sk, group in by.items():
        kinds = {x['kind'] for x in group}
        templates = {x['where'] for x in group}
        if len(templates) < 2:
            continue
        first = group[0]
        for other in group[1:]:
            if other['where'] == first['where']:
                continue
            if other['kind'] == first['kind']:
                audit(other['where'], f'near-copy of {first["where"]} ("{first["text"]}"): {other["text"]}')
            else:
                audit(other['where'], f'the same words as {first["where"]} under kind {first["kind"]}: {other["text"]}', unsure=True)
            break


# ---- (f) reviewed vocabulary ---------------------------------------------------------------------------------------------------


def load_vocabulary():
    if not VOCABULARY.exists():
        return None
    return {w.strip() for w in VOCABULARY.read_text(encoding='utf-8').splitlines() if w.strip() and not w.startswith('#')}


def vocabulary_audit(items, audit, vocab):
    """items: (where, text). A word nobody has reviewed stops the build in a strict owner's file (a person adds it)."""
    if vocab is None:
        return
    for where, text in items:
        new = sorted({w for w in words_of(text) if w not in vocab and not w.isdigit()})
        if new:
            audit(where, f'new word(s) {", ".join(new)}: check them, then add them to tools/bot-voices/vocabulary.txt '
                         f'(build_corpus.py --write-vocabulary): {text}')


def write_vocabulary(items):
    words = sorted({w for _, text in items for w in words_of(text) if not w.isdigit()})
    VOCABULARY.write_text('# Every word the bot chat corpus uses, reviewed (1.2 chat audit, DESIGN-045 f). A word not here stops '
                          'the build\n# in a strict owner\'s file until a person checks it and runs build_corpus.py '
                          '--write-vocabulary.\n' + '\n'.join(words) + '\n', encoding='utf-8', newline='\n')
    return len(words)


# ---- (h) the style round trip ---------------------------------------------------------------------------------------------------


def style_audit(lines, audit):
    """Each line through every writing style (voice_style.py, the server's Style): no capital before a chat word, no stop after
    "pst" or "/send", no second laugh at the end, nothing over 160 characters with the longest real slot values."""
    import random
    import voice_style
    voices = json.loads((ROOT / 'source/server/GameServer/bots/autonomous/data/chat/voices.json').read_text(encoding='utf-8'))['voices']
    rng = random.Random(5)
    laugh = re.compile(r'^(lol|haha|heh|hehe|:\)|:p|:d|lmao|rofl)$', re.IGNORECASE)
    for line in lines:
        text = SLOT_RE.sub(lambda m: '' * min(WORST.get(m.group(1), 20), 1), line['text'])
        for name, voice in voices.items():
            styled = voice_style.style(text, dict(voice, typo_chance=0), rng, line['kind'], emote=True)
            words = styled.split()
            for m in re.finditer(r'(^|[.!?] )([A-Z][a-z0-9]*)\b', styled):
                if m.group(2).lower() in voice_style.CHAT_TOKENS and m.group(2).lower() not in ('no', 'yes', 'sure', 'this', 'true', 'nice', 'cool', 'later', 'night', 'same', 'agreed', 'hey', 'hi'):
                    audit(line['where'], f'the {name} style capitalises the chat word "{m.group(2)}": {styled}')
                    break
            if re.search(r'\b(pst|/send)[.]$', styled):
                audit(line['where'], f'the {name} style puts a stop after "pst" or "/send": {styled}')
            if len(words) >= 2 and laugh.match(words[-1]) and laugh.match(words[-2].strip(',.!')):
                audit(line['where'], f'the {name} style ends in two laughs: {styled}')


# ---- (i) every @needs flag is one the server sets --------------------------------------------------------------------------------


def flags_audit(flags, problems):
    code = []
    for path in SERVER.rglob('*.cs'):
        parts = path.parts
        if 'bots' in parts or 'craft' in parts or 'commands' in parts or 'mode165' in parts:
            code.append(path.read_text(encoding='utf-8', errors='replace'))
    text = '\n'.join(code)
    literals = set(re.findall(r'"([a-z_0-9:!]+)"', text))
    dynamic = ('holiday_', 'xp_', 'at:', 'going:', 'near:')
    for flag in sorted(flags):
        if flag in literals or any(flag.startswith(p) and f'"{p}' in text for p in dynamic):
            continue
        problems.append(f'flag "{flag}" is in FLAGS but no server code sets it (no "{flag}" string in the bot code)')
