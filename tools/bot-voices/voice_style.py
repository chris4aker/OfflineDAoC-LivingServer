"""The server's writing styles in Python, for preview.py and the builder's style round trip (build_corpus.py --check).

A port of AutonomousBotVoiceCorpus.Style (1.2 chat audit: DESIGN-012, 013, 014, 032; CORPUS-B1-028). The C# code is the
reference: when Style changes, change this file the same way. Both run the cases in tests/style_cases.json
(tests/test_chat_rules.py here, UT_ChatAuditVoice.TheStyleCasesMatchThePythonPort in the server tests). The typing habits (AutonomousChatIdiolect)
are not ported; they never capitalise, stop or add a laugh to a line that already has one.
"""
import re

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
NO_EMOTE_KINDS = {'spam', 'alarm', 'rez_ask', 'death', 'news', 'combat', 'roleplay', 'beggar'}
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


def takes_emote(kind, text):
    return kind not in NO_EMOTE_KINDS and not is_functional(text) and not has_laugh(text) and not is_shouted(text)


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
        result += ' ' + rng.choice(voice['emotes'])
    if voice.get('capitalize') and result and not chat_token:
        result = result[0].upper() + result[1:]
    if voice.get('capitalize_sentences') and not chat_token:
        result = SENTENCE_START_RE.sub(lambda m: m.group(0) if m.group(2) in CHAT_TOKENS and m.group(2) not in SENTENCE_WORDS else m.group(1) + m.group(2)[0].upper() + m.group(2)[1:], result)
    if voice.get('end_period') and result and not chat_token and len(result.split()) > 3:
        if not any(result.endswith(e) for e in (voice.get('no_period_after') or ['.', '!', '?'])):
            result += '.'
    return cut_at_word(result)
