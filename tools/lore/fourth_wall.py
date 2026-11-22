"""Owner decision 8 (2026-11-21): "Nothing in the lore should reference real companies or break the 4th wall so to speak."

Which lore fields a player sees, and the words that may not appear in them. lint_lore.py fails CI on any hit.

Where the fields go (traced in docs/1.2.1/HEROES.md, decision 8):
  narrative  told inside the world: summary, story, chapters, loremaster words, book pages, yells, atmosphere lines,
             chronicle sentences, relations, the order members' lines, and every text of the history files (era and
             faction text, and the text of a history fact, which the /lore codex and the loremaster read out).
  advice     the legend ("What the raid leaders say" in the Bestiary, "What is said" in /lore): raiders' fight advice,
             where the owner's rule of 2026-11-06 keeps levels, health, resists and timers.
  chat       bot lines (briefings, after-raid lines, rumours, answers): 2003 chat voice, checked again by build_corpus.py.
Never shown: ids, catalog keys, spawn names, aliases (matching only), area names (memory keys only), triggers, and every
fact's provenance and source. The text of a location's or boss's own facts is never shown either (only the schema check
reads it), so those facts keep their real sources. Only a history file's facts reach a player, and only their text.

The chat line files the lore feeds (tales.lines, lore_*.lines, era_rumours.lines, battleground_lore.lines) are checked
as chat.
"""
import glob, json, os, re

CHAT_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "source", "server", "GameServer", "bots", "autonomous", "data", "chat")
LORE_CHAT_FILES = ["tales.lines", "lore_albion.lines", "lore_midgard.lines", "lore_hibernia.lines", "lore_shared.lines",
                   "era_rumours.lines", "battleground_lore.lines"]

I = re.IGNORECASE
# (id, pattern, flags). EVERYWHERE: real companies, sites, people and products, patch numbers, developers, and the
# software under the world (bots, NPCs, the server, scripts, templates, data). Checked in every player-visible field.
EVERYWHERE = [
    ("company", r"\bMythic\b", I),                       # "Cryptos Mythicos", the Clifton faction, does not match
    ("company", r"\bElectronic Arts\b|\bEA\b|\bBroadsword\b", 0),
    ("site", r"\ballakhazam\b|\bgame ?banshee\b|\bstratics\b|\buthgard\b|\bcamelot vault\b|\bIGN\b", I),
    ("site", r"\bPrima\b", 0),
    ("site", r"\bCamelot Herald\b|\bherald posts?\b", I),   # the website; a herald of Camelot, Lurfos the Herald stay
    ("product", r"\bDark Age of Camelot\b|\bDAoC\b|\bOpenDAoC\b|\bDawn of Light\b|\bLiving Server\b", I),
    ("product", r"\bexpansions?\b|\bTrials of Atlantis\b|\bDarkness Rising\b|\bLabyrinth of the Minotaur\b|\bNew Frontiers\b", I),
    ("product", r"\bToA\b|\bSI\b|\bCatacombs(?:-era| lore| expansion)\b|(?<!the )(?<!of )(?<!its )\bCatacombs\b", 0),
    ("patch", r"\bpatch(?:es)? ?\d|\bpatch notes?\b|\bpatched\b|\b1\.\d\d\b|\bhotfix|\bbeta\b", I),
    ("patch", r"\b(?:with|by|after|since|every|later|winter|autumn|spring|summer|last|next|one) patch(?:es)?\b(?! of\b)", I),
    ("date", r"(?<![\d,.])(?:19[89]\d|200[1-9])(?![\d,])", 0),   # the real calendar (2000 itself is a distance in bot lines)
    ("developer", r"\bdevs?\b|\bdevelopers?\b", I),
    ("software", r"\b(?:game)?bots?\b|\bNPCs?\b|\bservers?\b|\bGame(?:Player|NPC|Bot)\b|\bdatabase\b|\bclient\b", I),
    ("software", r"\bscript(?:s|ed)?\b|\btemplates?\b|\bworld(?:'s)? data\b|\b(?:the|its|his|her|their|in) code\b(?! of\b)", I),
    ("software", r"\bin(?: the|-)? game\b|\bgame (?:hours?|time|morning|evening|night|clock)\b|\bquest (?:log|text)\b", I),
    ("software", r"\bclick(?:s|ed|ing)? (?:on )?(?:him|her|it|them)\b|\bGMs?\b|\bgame ?masters?\b", I),
    ("player", r"\bplayers?\b|\bold guides?\b|\bguide map\b", I),
]
# NARRATIVE only: game terms used as game terms. Legends and bot lines keep them as raiders' fight advice (owner rule
# 2026-11-06: fight advice belongs in the legend, bot lines and yells... here yells are told in the world, so they count).
NARRATIVE = [
    ("game term", r"\blevels? \d|\blevel (?:fifty|sixty|seventy|eighty)\b|\bxp\b|\bexperience points\b|\bhit ?points\b", I),
    ("game term", r"\brespawn(?:s|ed|ing)?\b|\bmobs?\b|\bRvR\b|\bPvE\b|\bPvP\b|\brealm points\b|\bloot tables?\b", I),
    ("game term", r"\bcons? (?:purple|red|grey|gray|orange|yellow|blue|green)\b|\blogg(?:ed|s|ing) (?:in|on|off|out)\b", I),
]
# The chat line files the lore feeds hold no patch of ground, so there every "patch" is the game's.
CHAT_FILE = [("patch", r"\bpatch(?:es|ed)?\b(?! of\b)", I)]
# In-world words that only look alike. Each is removed from the text before the check.
EXCEPTIONS = [
    r"\b(?:harp|pipe|fidchell|chess|board|flute|horn|lyre|dice|drum|tafl)[- ]players?\b",  # a person who plays
    r"\bthe court's script\b|\bthe old script\b|\bin a script nobody else could see\b",       # handwriting (Baln's war table, Xaga's seed jars, the oghamist)
    r"\bin a mob\b|\bthe mob has a mother\b",             # a crowd of winds (the Cliffs of Moher, Aoife)
    r"\bCatacombs of Cardova\b",                          # a place in Cornwall, not the expansion
    r"\bin the game, as at the hollow\b",                 # the children's dragon game in Svarhamr (Gjalpinulva)
]

def compile_rules(rules):
    return [(k, re.compile(p, f)) for k, p, f in rules]

EVERYWHERE_RE = compile_rules(EVERYWHERE)
NARRATIVE_RE = compile_rules(NARRATIVE)
CHAT_FILE_RE = compile_rules(CHAT_FILE)
EXCEPTION_RE = re.compile("|".join(EXCEPTIONS), I)

def hits(text, kind):
    """(rule kind, matched text) for each forbidden word in a text of this field kind (narrative, advice, chat, or
    chatfile for a line of the lore's chat files). Pure."""
    t = EXCEPTION_RE.sub(" ", text or "")
    rules = EVERYWHERE_RE + (NARRATIVE_RE if kind == "narrative" else CHAT_FILE_RE if kind == "chatfile" else [])
    return [(k, m.group(0)) for k, rx in rules for m in rx.finditer(t)]

HIDDEN = {"id", "realm", "regions", "zones", "aliases", "catalogKey", "npcNames", "locationId", "era", "factions",
          "bosses", "places", "tier", "trigger", "provenance", "source", "x", "y", "z", "radius", "region", "order",
          "level", "keywordOnly"}

def visible(d, folder):
    """Every player-visible string of a lore file: (path, text, kind). Hidden fields and location/boss facts are skipped."""
    out = []
    def walk(v, path, kind):
        if isinstance(v, str):
            out.append((path, v, kind))
        elif isinstance(v, dict):
            for k, x in v.items():
                if k in HIDDEN: continue
                if k == "facts" and folder != "history": continue
                if k == "name" and path.endswith("areas[]"): continue     # an area's name is a memory key, never shown
                if path.endswith(".topics"): out.append((path + ".<keyword>", k, kind))
                sub = "chat" if k == "botLines" else "advice" if k == "legend" else kind
                walk(x, f"{path}.{k}", sub)
        elif isinstance(v, list):
            for x in v: walk(x, path + "[]", kind)
    walk(d, folder, "narrative")
    return out

def chat_lines(path):
    """The said text of a .lines file: every line that is not a comment or a directive, role prefix removed."""
    for n, line in enumerate(open(path, encoding="utf-8"), 1):
        s = line.strip()
        if not s or s.startswith("#") or s.startswith("@"): continue
        yield n, re.sub(r"^\??[A-Z]:\s*", "", s)

def problems(lore_dir, chat_dir=CHAT_DIR):
    out = []
    for f in sorted(glob.glob(os.path.join(lore_dir, "*", "*.json"))):
        folder = os.path.basename(os.path.dirname(f))
        rel = f"{folder}/{os.path.basename(f)}"
        for path, text, kind in visible(json.load(open(f, encoding="utf-8")), folder):
            for k, word in hits(text, kind):
                out.append(f"{rel} {path}: fourth wall ({k}, '{word}'; owner decision 8): {text[:90]}")
    for name in LORE_CHAT_FILES:
        p = os.path.join(chat_dir, name)
        if not os.path.exists(p): continue
        for n, text in chat_lines(p):
            for k, word in hits(text, "chatfile"):
                out.append(f"chat/{name}:{n}: fourth wall ({k}, '{word}'; owner decision 8): {text[:90]}")
    return out
