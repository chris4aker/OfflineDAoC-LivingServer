"""Pure scorers of the 1.2.2 chat audit (docs/bot-voices/CHAT-AUDIT-1.2.2.md): what one bot reply to one player question is.

Categories, one per reply (first that applies):
  SILENT      nobody answered (fine when the question has silence_ok)
  STUB        the reply needs the live world the bench does not have (a "[world: ...]" marker)
  WRONG       it holds a fact that contradicts the question's facts (wrong_any)
  ANSWERED    it holds one of the facts a good answer must hold (must_any)
  NON_ANSWER  a deflection with no fact: "not sure", "depends", "ask around", "no idea", "try /advice", a bare "ya?"
  FLUFF       a content-free line: "lol", "nice", "heh true", "good times"
  OFF_TOPIC   anything else: a line that says something, but not about what was asked

Flags, any number per reply (a reply can be ANSWERED and CLUNKY):
  CLUNKY      stitched fragments that don't agree (clunky_issues: doubled words, a/an, number agreement, a dangling connector,
              an unfilled slot or marker, a repeated clause, broken punctuation, a second sentence that restarts the first)
  ECHO        it repeats four or more words of the question back
  PADDED      an answer with a content-free tail or head stuck on ("yeah, ..., lol idk")

Python standard library only.
"""
import re

# ---------------------------------------------------------------------------------------------------------------- matching

_APOS = re.compile(r"[’'`]")


def norm(text):
    """Lower case, apostrophes dropped, anything not a letter, digit, slash, plus, comma, percent or hyphen made a space."""
    t = _APOS.sub('', (text or '').lower())
    t = re.sub(r"[^a-z0-9/+,%\- ]", ' ', t)
    return re.sub(r'\s+', ' ', t).strip()


def matches(fact, text):
    """A fact in a reply: "re:..." is a regular expression on the lower-cased reply; else whole words, apostrophes ignored."""
    if not fact or text is None:
        return False
    if fact.startswith('re:'):
        return re.search(fact[3:], _APOS.sub('', text.lower())) is not None
    f = norm(fact)
    if not f:
        return False
    # "friar" holds "friars", "lurker" holds "lurkers".
    return re.search(r'(?<![a-z0-9])' + re.escape(f) + r'(s|es)?(?![a-z0-9])', norm(text)) is not None


def any_match(facts, text):
    return next((f for f in facts or [] if matches(f, text)), None)


# ------------------------------------------------------------------------------------------------------------- non-answers

NON_ANSWER_PATTERNS = [
    r"\bnot sure\b", r"\bno idea\b", r"\bno clue\b", r"\bdunno\b", r"\bidk\b", r"\bidunno\b", r"\bcouldnt (tell|say)\b",
    r"\bcant (say|remember|recall)\b", r"\bdont know\b", r"\bdont remember\b", r"\bnever (been|done|tried)\b",
    r"\b(it )?depends\b", r"\bask (around|in|on|a guard|the guards|someone|your trainer|ur trainer|a vet|in advice|/advice)\b",
    r"\btry /advice\b", r"/advice", r"\bgood question\b", r"\bwho knows\b", r"\bbeats me\b", r"\bhard to say\b",
    r"\bsomeone else (would|might|will) know\b", r"\bi forget\b", r"\bforgot\b", r"\bhmm+\b\W*$", r"^\W*(ya|yes|yeah|yep|hm+|huh|what|wat)\W*\?+\W*$",
    r"^\W*\?+\W*$", r"\bwhat do you mean\b", r"\bwhy do you ask\b", r"\bnot my thing\b", r"\bdepends what\b",
]
_NON_ANSWER = [re.compile(p) for p in NON_ANSWER_PATTERNS]


def is_non_answer(text):
    """A deflection: says it doesn't know, asks back, or sends the player elsewhere."""
    t = _APOS.sub('', (text or '').lower()).strip()
    return bool(t) and any(p.search(t) for p in _NON_ANSWER)


# What is left of a deflection once its own words are gone ("not sure, try /advice" -> ", try"): chat filler and the words a
# deflection is padded with. 1.2.3 (#477): a must_any such as "re:sure|ask" passed "not sure, try /advice".
_DEFLECTION_PAD = {'tbh', 'sry', 'sorry', 'srry', 'soz', 'mate', 'try', 'there', 'about', 'that', 'one', 'exactly', 'atm', 'rn', 'tho', 'though',
                   'myself', 'either', 'havent', 'camped', 'them', 'dude'}


def past_deflection(text):
    """The reply with its deflection taken out, for matching the facts a good answer holds: a deflection's own words ("not
    sure", "ask around", "no idea") are no answer, and a reply that is nothing but a deflection holds no fact at all."""
    t = _APOS.sub('', (text or '').lower())
    if not is_non_answer(t):
        return text
    for p in _NON_ANSWER:
        t = p.sub(' ', t)
    rest = [w for w in content_words(t) if w not in _DEFLECTION_PAD]
    return t if rest else ''


# ------------------------------------------------------------------------------------------------------------------ fluff

# Words that carry nothing on their own in a reply (chat filler, laughs, agreement, pronouns, the commonest verbs).
FILLER = set('''
lol lmao rofl haha hehe heh hah ha lul kek xd ok okay k kk ya yah yeah yep yup yes no nope nah sure true same agreed indeed nice
cool neat wow woot sweet gz grats gratz ty thx np yw hm hmm hmmm huh oh ah aha eh meh ugh oof well so like just really very pretty
totally kinda sorta maybe prob probably actually honestly seriously literally basically def definitely
i im me my mine you u ur your yours we us our it its that thats this these those there theyre they them he she his her
is are was were be been am do does did done have has had get got go going gonna wanna can could would should will
a an the and or but of to in on at for with too also again still now then here what who how why when
good great fine bad times thing things stuff something anything everything nothing all always never ever lot lots much more
yea yeh ye aye mhm mm hey hi heya hello sup yo dude man mate bro guys all folks friend
^ + +1 :) :( :p :d ;) xd o/ <3
'''.split())


def content_words(text):
    words = re.findall(r"[a-z0-9/+%]+", _APOS.sub('', (text or '').lower()))
    return [w for w in words if w not in FILLER and not re.fullmatch(r'(lo+l|ha(ha)+|he(he)+|hm+|a+h+|o+h+|u+g+h+)', w)]


def is_fluff(text):
    """A content-free line: nothing in it but filler, laughs, agreement and the commonest words."""
    t = (text or '').strip()
    return bool(t) and not content_words(t)


# ---------------------------------------------------------------------------------------------------------------- clunky

_VOWEL_SOUND_EXCEPTIONS = ('one', 'once', 'uni', 'use', 'usu', 'uti', 'ure', 'uri', 'eu', 'ewe', 'ubi')
_H_SILENT = ('hour', 'honest', 'honor', 'honour', 'heir')
_NOT_NOUN = {'a', 'an', 'and', 'or', 'of', 'on', 'in', 'at', 'as', 'is', 'it', 'if', 'its', 'i', 'im', 'are', 'am', 'also', 'all', 'any',
             'up', 'us', 'off', 'out', 'over', 'into', 'onto', 'else', 'ever', 'each', 'either', 'even', 'oh', 'ok', 'okay', 'until'}
_DOUBLE_OK = {'ha', 'he', 'lol', 'no', 'very', 'so', 'go', 'ty', 'gz', 'bye', 'yes', 'yeah', 'yep', 'hey', 'hi', 'that', 'had', 'k',
              'omg', 'wow', 'woo', 'more', 'run', 'now', 'far', 'many', 'again', 'pull', 'kill', 'nom', 'boom', 'knock', 'tick', 'rr', 'pew', 'bang'}
# Words a sentence can't end on ("someone to talk to", "you know who you are" can).
_CONNECTORS = {'and', 'but', 'or', 'the', 'a', 'an', 'because', 'if', 'than', 'my', 'your', 'their', 'our',
               'very'}
_PLURAL_AFTER_ONE = re.compile(r"\b(1|one|a single) (levels|mobs|players|bots|groups|hours|minutes|mins|days|weeks|rps|points|kills|"
                               r"times|pieces|items|camps|keeps|relics|spots|zones)\b")
_SINGULAR_AFTER_MANY = re.compile(r"\b([2-9]|[1-9]\d+|two|three|four|five|six|several|many|few|couple of|lots of) "
                                  r"(level|mob|player|bot|group|hour|minute|day|week|kill|time|piece|item|camp|keep|relic|spot|zone)\b(?=\s*($|[.,!?;]|(ago|left|more|and|so|in|at|for|to go)\b))")
_SLOT = re.compile(r"\{[a-z_]+\}|\[[a-z_ :]+\]|<[a-z_]+>|\$\{|%s|\bnull\b|\bundefined\b|\bNaN\b")
# Broken punctuation: a space before a comma or full stop, doubled commas, ",." and the like. Emoticons (" :)") and a leading
# "..." are typing, not breakage.
_PUNCT = re.compile(r"\s[,.;](?![\w.)(])|,,|\.\.,|,\.(?!\.)|\?\s*,|![,.](?!\.)|\s{2,}|^\s*[,;]")


def takes_an(word):
    w = (word or '').lower().lstrip("'\"(")
    if not w:
        return False
    if w[0].isdigit():
        digits = re.match(r'\d+', w).group(0)
        return digits[0] == '8' or digits in ('11', '18')
    if w.startswith(_H_SILENT):
        return True
    if w[0] in 'aeiou':
        return not w.startswith(_VOWEL_SOUND_EXCEPTIONS) and not (w[0] == 'u' and len(w) == 1)
    return False


# Proper names a tidy writer capitalizes; in a line that opens with a capital they read as a slip ("The trainer in camelot").
PROPER = ('camelot', 'jordheim', 'tir na nog', 'albion', 'midgard', 'hibernia', 'cotswold', 'mularn', 'mag mell', 'avalon', 'emain macha',
          'darkness falls', 'legion', 'myrkwood', 'lyonesse')
_LAUGH_TAIL = re.compile(r"\s(lol|haha+|heh|hehe|lmao|rofl|:p|:d|xd|:\)|;\))\W*$", re.IGNORECASE)


def clunky_issues(text, proper=PROPER):
    """What reads as stitched together in a line: a list of (code, detail). Empty when it reads clean."""
    issues = []
    if not text:
        return issues
    raw = text
    low = _APOS.sub('', raw.lower())
    words = re.findall(r"[a-z0-9%+]+", low)
    # Only a word typed twice with nothing between ("the the"); "shoot, shoot again" is a choice of rhythm.
    for m in re.finditer(r"(?<![\w-])([a-z]+) \1(?![\w-])", low):
        if m.group(1) not in _DOUBLE_OK:
            issues.append(('doubled_word', m.group(0)))
    for m in re.finditer(r"\b(a|an) ([a-z0-9][\w%]*)", low):
        art, nxt = m.group(1), m.group(2)
        # An acronym is said by its letters ("an rvr guild", "an MP"); the bench can't hear that, so it leaves them alone.
        if nxt in _NOT_NOUN or len(nxt) <= 3 and not re.search(r'[aeiou]', nxt[1:] or 'x') or len(nxt) == 1:
            continue
        if art == 'a' and takes_an(nxt):
            issues.append(('a_an', f'a {nxt}'))
        elif art == 'an' and not takes_an(nxt) and not nxt[0].isdigit():
            issues.append(('a_an', f'an {nxt}'))
    for m in _PLURAL_AFTER_ONE.finditer(low):
        issues.append(('number_agreement', m.group(0)))
    for m in _SINGULAR_AFTER_MANY.finditer(low):
        issues.append(('number_agreement', m.group(0)))
    for m in _SLOT.finditer(raw):
        issues.append(('unfilled_slot', m.group(0)))
    last = words[-1] if words else ''
    if last in _CONNECTORS and not raw.rstrip().endswith(('?', ')')) and len(words) > 2:
        issues.append(('dangling_connector', last))
    if re.search(r"[,;:]\s*$", raw.rstrip()) or re.search(r"\b(and|but|or),", low):
        issues.append(('dangling_connector', raw.rstrip()[-12:]))
    for m in _PUNCT.finditer(raw):
        issues.append(('punctuation', repr(m.group(0))))
    # A clause said twice ("bring a group ... bring a group").
    # Five words in the same order twice; three or four is often parallelism on purpose ("hit and heal, hit and heal").
    grams = [' '.join(words[i:i + 5]) for i in range(len(words) - 4)]
    seen = set()
    for g in grams:
        if g in seen and len(set(g.split())) >= 4:
            issues.append(('repeated_clause', g))
            break
        seen.add(g)
    # Two sentences that disagree on yes and no ("yes, ... no, ...").
    if re.search(r"^\W*(yes|yep|yeah)\b.*[.,]\s*(no|nope)\s*[,.]", low) or re.search(r"^\W*(no|nope)\b.*[.,]\s*(yes|yep|yeah)\s*[,.]", low):
        issues.append(('contradicts_itself', 'yes ... no'))
    # Tense: "did ... goes", "will ... went" in one clause.
    tense = re.search(r"\b(did|didnt|does|doesnt|will|wont|cant|can)( (you|u|i|he|she|they|we|it))? (went|was|came|saw|took|gave|ate)\b", low)
    if tense:
        issues.append(('tense', tense.group(0)))
    # A laugh stuck on a line that has nothing funny in it: a "dunno", or a long serious answer ("check before you ride out lol").
    tail = _LAUGH_TAIL.search(raw)
    if tail:
        body = raw[:tail.start()].strip()
        if is_non_answer(body) or body.endswith('.') or re.search(r"\? \w", body) or \
                re.search(r"\b(careful|dont go alone|wipe|wiped|die|dead|died|raid|cant go|careless|trust nothing)\b", body.lower()):
            issues.append(('tacked_laugh', tail.group(1)))
    # A reply that opens with a question of its own and then answers it ("who is eraliwyr? the alchemist..." to "what lvl is
    # spraggon den?"): a written Q-and-A line picked for another question.
    own = re.match(r"^\W*((whats|what s|what is|who is|whos|who holds|where is|wheres|where s|is the|is it|how do|how is|why is)\b[^?]{0,48})\?\s+\S", low)
    if own:
        issues.append(('own_question', own.group(1).strip()))
    # A line that opens like a tidy sentence but leaves a name in lower case: two writing styles stitched together.
    if raw[:1].isupper():
        for name in proper:
            if re.search(r'(?<![A-Za-z])' + re.escape(name) + r'(?![a-z])', raw):
                issues.append(('case_mix', name))
                break
    return issues


def echoes(question, reply, n=4):
    """Whether a reply repeats n or more words of the question in a row (a quote back, not an answer)."""
    q = norm(question).split()
    r = ' ' + norm(reply) + ' '
    for i in range(len(q) - n + 1):
        if ' ' + ' '.join(q[i:i + n]) + ' ' in r:
            return True
    return False


def padded(reply):
    """An answer with a content-free piece stuck on: a fragment of the line, split at commas or full stops, that is pure filler
    or a deflection, beside a fragment that says something."""
    parts = [p.strip() for p in re.split(r"[,.;!]| - ", reply or '') if p.strip()]
    if len(parts) < 2:
        return False
    empty = [p for p in parts if is_fluff(p) and len(p.split()) >= 2 or is_non_answer(p)]
    full = [p for p in parts if not is_fluff(p) and not is_non_answer(p)]
    return bool(empty) and bool(full)


# ---------------------------------------------------------------------------------------------------------------- scoring

CATEGORIES = ('ANSWERED', 'WRONG', 'NON_ANSWER', 'OFF_TOPIC', 'FLUFF', 'SILENT', 'STUB')
FLAGS = ('CLUNKY', 'ECHO', 'PADDED')


def score(question, reply):
    """(category, flags, why) for one reply to one question (a dict of player_questions.json)."""
    flags = []
    if reply is None or not str(reply).strip():
        return 'SILENT', flags, 'silence is fine here' if question.get('silence_ok') else 'nobody answered'
    reply = str(reply)
    if reply.startswith('[world') or reply.startswith('[stub'):
        return 'STUB', flags, reply
    issues = clunky_issues(reply)
    if issues:
        flags.append('CLUNKY')
    if echoes(question.get('text', ''), reply):
        flags.append('ECHO')
    wrong = any_match(question.get('wrong_any'), reply)
    good = any_match(question.get('must_any'), past_deflection(reply))
    if good and padded(reply):
        flags.append('PADDED')
    if wrong:
        return 'WRONG', flags, f'says "{wrong}"'
    if good:
        return 'ANSWERED', flags, f'holds "{good}"'
    if is_non_answer(reply):
        return 'NON_ANSWER', flags, 'a deflection with no fact'
    if is_fluff(reply):
        return 'FLUFF', flags, 'content-free'
    return 'OFF_TOPIC', flags, 'none of: ' + ', '.join((question.get('must_any') or [])[:4])


def ok(question, category):
    """Whether a category is a good outcome for the question: an answer, or silence where silence is fine."""
    return category == 'ANSWERED' or category == 'SILENT' and bool(question.get('silence_ok'))
