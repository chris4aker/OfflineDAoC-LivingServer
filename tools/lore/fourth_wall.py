"""Owner decision 8 (2026-11-21): "Nothing in the lore should reference real companies or break the 4th wall so to speak."

Which lore fields a player sees, and the words that may not appear in them. lint_lore.py fails CI on any hit of
EVERYWHERE (and of NARRATIVE in a narrative field), and of GAME_TERMS while GAME_TERMS_FAIL is True (on since the clean-up
review, 2026-11-22; False turns them back into warnings).

Where the fields go (traced in docs/1.2.1/HEROES.md, decision 8):
  narrative  told inside the world: summary, story, chapters, loremaster words, book pages, yells, atmosphere lines,
             chronicle sentences, relations, the order members' lines, and every text of the history files (era and
             faction text, and the text of a history fact, which the /lore codex and the loremaster read out).
  advice     the legend ("What the raid leaders say" in the Bestiary, "What is said" in /lore): raiders' fight advice.
  chat       bot lines (briefings, after-raid lines, rumours, answers): 2003 chat voice, checked again by build_corpus.py.
Owner, 2026-11-21 ("Rewrite that"): the legend and the bot lines are told in the world too, with no game terms in the fight
advice either. GAME_TERMS holds those words for every field; PENDING_OWNER_DECISION the words the owner has yet to rule on.
Never shown: ids, catalog keys, spawn names, aliases (matching only), area names (memory keys only), triggers, and every
fact's provenance and source. The text of a location's or boss's own facts is never shown either (only the schema check
reads it), so those facts keep their real sources. Only a history file's facts reach a player, and only their text.

The chat line files the lore feeds (tales.lines, lore_*.lines, era_rumours.lines, battleground_lore.lines) are checked
as chat against EVERYWHERE; GAME_TERMS do not reach them yet (they are the period's chat voice: owner question 5 in
docs/1.2.1/REVIEW-FOURTH-WALL.md).
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
    ("software", r"\bin(?: the | |-)game\b|\bgame (?:hours?|time|morning|evening|night|clock)\b|\bquest (?:log|text)\b", I),
    # Review 2026-11-21: "no spells in the data", "spawn data", "data quests", "flagged aggressive", "with its own file".
    ("software", r"\b(?:the|this|his|her|its|quest|spawn) data\b|\bdata quests?\b|\bflagged (?:aggressive|peaceful|as)\b|\bghost flag\b", I),
    ("software", r"\bwith (?:its|their) own files?\b|\bfiles? to come\b|\boverview\b|\bdev ?team\b", I),
    ("software", r"\bAI\b", 0),
    ("software", r"\bclick(?:s|ed|ing)? (?:on )?(?:him|her|it|them)\b|\bGMs?\b|\bgame ?masters?\b", I),
    ("player", r"\bplayers?\b|\bold guides?\b|\bguide map\b", I),
    # The three in-world writers (2026-11-21) found these gaps: a real forum, the server's class names and pack ids,
    # and the client's own words.
    ("site", r"\bFreddy'?s ?house\b", I),
    ("software", r"\bGame[A-Z]\w*|\b\w*[a-z]Baf\b", 0),           # GameEpicNPC, GamePlayer; pack ids such as GuardBaf
    ("software", r"\btarget-nearest\b|\bclick(?:s|ed|ing)? on\b", I),
]
# NARRATIVE only, and always a failure: game terms used as game terms in told text (the first pass of decision 8).
# GAME_TERMS below extends the ban to legends and bot lines.
NARRATIVE = [
    ("game term", r"\blevels? \d|\blevel (?:fifty|sixty|seventy|eighty)\b|\bxp\b|\bexperience points\b|\bhit ?points\b", I),
    ("game term", r"\brespawn(?:s|ed|ing)?\b|\bmobs?\b|\bRvR\b|\bPvE\b|\bPvP\b|\brealm points\b|\bloot tables?\b", I),
    ("game term", r"\bcons? (?:purple|red|grey|gray|orange|yellow|blue|green)\b|\blogg(?:ed|s|ing) (?:in|on|off|out)\b", I),
    ("game term", r"\bzones?\b|\b(?:two|three|its|the) spawns\b|\bspawns (?:with|of)\b", I),   # Svarm's brood of spawn stays
]
# GAME_TERMS: the game's own words, banned in EVERY player-visible field, legends and bot lines included (owner,
# 2026-11-21, "Rewrite that": fight advice is told in the world too, so this replaces the 2026-11-06 exemption). Merged
# from the Albion, Midgard and Hibernia writers' lists. Each rule: (id, pattern, flags[, kinds]). A rule without kinds is
# checked in narrative, advice and chat. A word whose plain English sense is common in a story ("the lowest level",
# "hunters' snares", "grind their bones", "at range", "its whole faction") is checked only in the raiders' fields, ADVICE
# and CHAT, where the game sense is the usual one. Numbers are told in words or in paces and seconds; a map mark is
# PENDING_OWNER_DECISION below.
RAIDERS = ("advice", "chat")
NUMBER = (r"(?<![\w.,/:])(?<!\bin )(?<!\bto )(?<!killing of ){}(?:[,.]\d+)*k?\b(?![,.:]\d)(?!(?:-odd)? paces\b)"
          r"(?! ?[-–] ?\d+ ?sec)(?! ?(?:in|to) \d)(?! ?(?:sec(?:ond)?s?|mins?|minutes?|hours?|days?|nights?|weeks?|months?|years?|"
          r"winters?|summers?|times|of|am|pm|o'clock)\b)")
GAME_TERMS = [
    # Levels: "level 50", "N level", "lvl", "the 50s", "mid-thirties", "50ish", "50+", "(45)", "(40-54)"; "your level" in
    # the raiders' fields.
    ("level", r"\blevels? \d|\blvls?\b|\blv\b|\b\d+(?:st|nd|rd|th)? levels?\b|\blevel (?:fifty|sixty|seventy|eighty)\b", I),
    ("level", r"\blevels?\b", I, RAIDERS),
    ("level", r"\b(?:low|mid|high|early|late)[- ]?(?:\d0s|teens|twenties|thirties|forties|fifties|sixties|seventies)\b"
              r"|\bin the (?:teens|twenties|thirties|forties|fifties|sixties|seventies)\b|\b[1-9]0s\b(?! ?(?:sec|min))"
              r"|\b\d+ish\b|\b\d+\+|\(\d+(?:[-–]\d+)?\)", I),
    # Levels in words (the clean-up review, 2026-11-22): "twenties", "teens to twenties", "fifty-ish", "sixty-plus",
    # "four fifties", "a forty could solo it", "heals at thirty" (a share of health in tens). A head count stays: "twenty-
    # eight fish", "keep it under fifty", "at fifty or more he gets stronger", "the thirty lords", "at fifteen" (an age).
    ("level", r"\b(?:teens|twenties|thirties|forties|fifties|sixties|seventies|eighties)\b", I, RAIDERS),
    ("level", r"\b(?:ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|"
              r"fifty|sixty|seventy|eighty|ninety)(?:-(?:one|two|three|four|five|six|seven|eight|nine))?[- ]?(?:ish|plus)\b", I),
    ("level", r"\b(?:a|an|any|lone|solo) (?:fifteen|twenty|thirty|forty|fifty|sixty|seventy|eighty)(?:-(?:one|two|three|four|"
              r"five|six|seven|eight|nine))?\b(?= (?:can|could|will|would|should|alone|duo|solo|healers?|casters?|tanks?|"
              r"needs?|or)\b|\s*[.,!?;)\]|]|\s*$)", I, RAIDERS),
    ("health", r"\b(?:heals?|healed|itself|himself|herself|runs?|ran|retreats?|flees?|held|hold|burst \w+|it|he|she) at (?:ten|"
               r"twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)\b(?! (?:paces|seconds|sec|or|and)\b)"
               r"|\bat (?:ten|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)\b(?=\s*(?:[.,;]|\[|$|(?:once|when|it|he|she|like)\b))", I, RAIDERS),
    ("slang", r"\bcharacters?\b", I, RAIDERS),                       # a man of good character stays in a story
    ("xp", r"\bexperience\b", I, RAIDERS),                        # "in my experience" stays in a story
    ("xp", r"\bxp\b|\bexp\b|\bexperience points\b|\brealm points?\b|\brps?\b|\bbounty points?\b|\brespec(?:s|ced|cing)?\b", I),
    ("spawn", r"\b(?:re|de)spawn\w*\b|\brepop\w*\b|\bspawn ?points?\b", I),
    # A cork popped, a fish spawned and the Svarm's spawn stay in a story.
    ("spawn", r"\bpop(?:s|ped|ping)?\b|\bspawn(?:s|ed|ing|ers?)\b|\b(?:a|day|night|time|timed|the) spawn\b(?! of\b)", I, RAIDERS),
    # "epic" as the game's quest chain ("the viking epic", "epics"); "the epic of" and an epic poem or tale stay.
    ("mob", r"\bmobs?\b|\bnameds\b|\bepics\b|\bepic\b(?! of\b)(?! (?:poems?|songs?|tales?|sagas?|lays?|cycles?)\b)", I),
    ("mob", r"\bboss(?:es)?\b", I, RAIDERS),
    ("mob", r"\b(?:a|the|other|every|each|that|this|any|one|two|three|four|five|six|some|big|few|many|no) named\b"
            r"(?=\s*(?:[.,;:!?)\]|]|$)|\s+(?:in|of|at|is|are|was|near|up|here|there|drops?|spawns?|hits|has|with|and|or)\b)", I),
    ("mob", r"\b(?:the|his|her|its|their|kill|no|more|with|of|for|two|three|four|five|few|many|mind the) adds\b"
            r"|\badds (?:come|spawn|pop|first|down|die)\b|\ban add\b|\badd fights?\b", I),
    ("world", r"\bRvR\b|\bPvE\b|\bPvP\b|\bloot tables?\b|\bzerg\w*\b|\bzones?\b|\binstances?\b|\bquest ?givers?\b|\bnpcs?\b", I),
    ("con", r"\bcons?\b|\bconn(?:ing|ed)\b", 0),                     # lower case: Aithne Con keeps her name
    ("con", r"\b(?:purple|red|orange|yellow|blue|green|grey|gray)s? (?:con|to (?:you|us|me|them|him|her|everyone|anyone|most|all))\b", I),
    ("login", r"\blogg(?:ed|ing) (?:in|on|off|out)\b|\blogins?\b|\bafk\b|\blfg\b|\b(?:off|on)line\b", I),
    ("login", r"\blogs? (?:in|on|off|out)\b", I, RAIDERS),          # a log in the river stays in a story
    ("login", r"\brelease(?:d|s)? (?:to|at) (?:your|the|their) (?:own )?(?:bind\w*|portal|keep|graveyard|altar)\b"
               r"|\brelease (?:world|timer|point)s?\b|\bbind ?(?:area|point|spot)s?\b|\brebind\w*\b", I),
    ("aggro", r"\b(?:de)?ag+ro\w*\b|\bpathing\b|\bhate (?:list|table)s?\b|\bdetaunt\w*\b|\bkit(?:ed|ing)\b|\bkites? (?:him|her|it|them)\b"
              r"|\b(?:lost|lose|loses|losing|kills?|build|builds|building|hold|holds|held|holding) (?:the )?threat\b", I),
    ("aggro", r"\bleash\w*\b|\bkites?\b", I, RAIDERS),                 # a dog's leash and the red kites stay in a story
    ("pull", r"\bpull(?:s|ed|ing|ers?)?\b(?! (?:at|free|loose|in|down|up|back|away|open|off|on|tight|together|toward|towards)\b)", I, RAIDERS),
    ("tank", r"\b(?:off-?)?tank\w*\b", I),
    ("tank", r"\bMT\b|\bOT\b|\bMA\b", 0),
    ("health", r"\bhit ?points\b|\bhp\b|\bone-?shot\w*\b|\d ?%|\bper ?cent\b", I),
    ("health", r"\bhealth\b", I, RAIDERS),                           # "to the king's health" stays in a story
    ("spell", r"\bDoTs?\b|\bDDs?\b|\bAoEs?\b|\bPBA?o?E\b|\bLoS\b", 0),
    ("spell", r"\bmez\w*\b|\bnuk(?:e|es|ed|ing|er|ers)\b|\blifetaps?\b|\bprocs?\b|\b(?:re|de)?buff(?:s|ed|ing|er|ers)?\b"
              r"(?! (?:coat|leather|jerkin|hide)s?\b)|\bcc\b|\bcrowd[- ]control\w*\b|\bdps\b|\bdamage over time\b"
              r"|\bdirect damage\b|\barea of effect\b|\boom\b|\bcast ?bars?\b|\bcrits?\b|\bcritt(?:ed|ing)\b|\brez(?:z?ed|zing|zes|z|s)?\b", I),
    # Snares, roots and a resisted spell: in a story they are a hunter's snares, a tree's roots, a priest who resisted.
    ("spell", r"\bsnar(?:e|es|ed|ing)\b|\broot(?:s|ed|ing)? (?:you|him|her|them|us|everyone|anyone|whoever)\b"
              r"|\b(?:be|get|gets|got|is|are|stay|stays|you're|youre) rooted\b|\broots?(?: and| or|,) mez\w*\b|\broot (?:spells?|breaks?|lasts?)\b", I, RAIDERS),
    ("resist", r"\b(?:heat|cold|matter|body|spirit|energy|crush|slash|thrust|fire|frost|ice|magic|spell|essence|elemental|melee|all) "
               r"resist(?:s|ance|ances)?\b|\bresist (?:pots?|potions?|buffs?|gear|caps?)\b", I),
    ("resist", r"\bresistances?\b|\bimmun(?:e|ity|ities)\b|\bsusceptib\w*\b|\bresists\b(?! (?:the|him|her|them|all|every)\b)|\bresisted\b(?! (?:the|him|her|them|all|every|his|their)\b)", I, RAIDERS),
    ("mechanic", r"\bcool ?downs?\b|(?<!-)\btimers?\b|\bmechanics?\b|\buntargetable\b|\btarget(?:able|ing)\b|\bstun ?breaks?\b"
              r"|\bcoords?\b|\bloc\b|\bradius\b", I),
    # The moon's phases, a clock's ticks, a set bone and a man who evades a question stay in a story.
    ("mechanic", r"\bticks?\b|\bphases?\b|\btrigger\w*\b|\bresets?\b|\bevad(?:e|es)\b|\bunits?\b", I, RAIDERS),
    ("mechanic", r"\b(?:at|in|out of|aggro|long|short|max|spell|cast|casting|bow|melee|close) range\b|\d+ range\b|\brange of \d"
              r"|\bbowshots?\b", I, RAIDERS),                          # "a long bowshot of the knotted trees" stays in a story
    ("slang", r"\bsolo\w*\b|\bduo(?:s|ing|ed)?\b|\bgrp\b|\bdmg\b|\bstats?\b|\binc\b|\bgg\b|\bn[o0]{2}bs?\b|\bBAF\b|\bbring-a-friend\b"
              r"|\bscreenshots?\b|\bcampers?\b|\bcamping (?:zone|spot|group)s?\b|\bports? (?:you|him|her|them|us|everyone|in|out|round|back)\b"
              r"|\bported\b|\bport\)|\bteleport\w*\b", I),
    ("slang", r"\bFG\b", 0),
    # The clean-up review (2026-11-22): the camp chat's words for the young and the catalog's foe tiers.
    ("slang", r"\blowbies?\b|\blow-?levels?\b|\bnewbies?\b|\bnewbs?\b|\bnoob(?:s|ies?)?\b|\btwinks?\b|\btwinked\b|\btimed out\b"
              r"|\blesser named\b", I),
    ("slang", r"\b[Tt]ier [ABC]\b|\bTIER [ABC]\b", 0),
    ("slang", r"\bwip(?:e|es|ed|ing)\b(?! (?:your|their|his|her|my|our) (?:boots|feet|brow|blades?|eyes|face|hands|mouth)\b)"
              r"|\bfactions?\b|\bgrind(?:s|ing)? (?:him|her|it|them|xp|levels?|mobs?|there|here)\b|\b(?:a|the) grind\b"
              r"|\bfarm(?:s|ed|ing)? (?:him|it|them|xp|coin|gold|loot)\b|\bfarming\b"
              r"|\bcamp(?:s|ed|ing)? (?:it|him|her|them|the (?!night|winter|summer|first|last|next))\b", I, RAIDERS),
    # Numbers: any figure but paces, a time, a count of times, "N of", "N in N", "N to N", "N-N sec" and "killing of N".
    # "number" is 10 and over (all three writers); "small number" is a bare 1-9 ("splits into 4"), the Hibernia
    # writer's "any digit" rule.
    ("number", NUMBER.format(r"(?:\d\d+|\d(?:[,.]\d+)+k?|\dk)"), I),
    ("small number", NUMBER.format(r"\d"), I),
]
# In-world words that only look like game terms. Each is removed from the text before GAME_TERMS are checked.
GAME_TERM_EXCEPTIONS = [
    r"\bpull(?:s|ed)?\]? (?:\w+ ){0,3}out\b|\bpulls? at\b|\bpull free\b",           # Midgard and Hibernia writers
    r"\blevels? (?:his|her|their) (?:spear|sword|lance|bow|staff|axe|blade|gaze)\b|\blevel (?:heath|ground|floor|plain|land|top|"
    r"stretch|field|shelf|bank|sand|turf|grass|stone|with)\b|\b(?:on|of|at|into) (?:two|three|four|five|the upper|the lower|every|each|"
    r"several|many) levels\b|\bsea level\b|\bon a level\b",            # a flat heath, a city on three levels
    r"\bthe kite\b|\bas a kite\b|\ba red kite\b",                         # Ris'nir's nickname; the bird and the toy
    r"\bevery drop\b|\blooters?\b|\bmadder roots\b|\bthe Root\b|\bAncient Root\b|\brooted to the spot\b|\bRooted\. All of us, rooted\.",
    r"\bBanish Immunities\b|\bthey add up\b|\bever named\b|\bno threat\b|\bsilk farm\b|\bwipe your boots\b",
    r"\b(?:blue|grey|gray|red) to the\b|\bstunning (?:blow|breath)\b|\bthe epic of\b|\b(?:pit|shield|gang|mine) boss(?:es)?\b|\bwho is boss\b"
    r"|\bhave bosses\b",                                                  # the goblins of the mine and their pit bosses
    r"\baithne con\b",                                                    # a loremaster's keyword for Aithne Con
    # The spraggonoll named Boss in the Silvermine's roofless barn (his name is the one trade word his kind learned),
    # and a bell rope (the clean-up review, 2026-11-22).
    r"\bits boss\s*$|^boss is a spraggonoll\b|\bcall(?:s|ed)? (?:their|its|his) (?:leader|chief) Boss\b",
    r"\bpull(?:s|ed|ing)? the (?:bell[- ]?)?rope\b",
]
# PENDING_OWNER_DECISION: chat words and map marks the writers kept or argued over; the owner has not ruled (2026-11-21).
# They pass for now. To ban them, set BAN_PENDING = True: they then count as GAME_TERMS.
BAN_PENDING = False
PENDING_OWNER_DECISION = [
    ("map mark", r"\(?\b(?:about|near|around|at|by) \d+(?:\.\d+)?k?,? ?/? ?\d+(?:\.\d+)?k\b\)?|\(\d+(?:\.\d+)?k?\s*[/,]\s*\d+(?:\.\d+)?k\)"
                 r"|\b\d+(?:\.\d+)?k\s*/\s*\d+(?:\.\d+)?k\b", I),              # "(about 343k/658k)", "near 21k, 40k"
    ("chat word", r"\bmelee\b|\bcasters?\b|\bgroups?\b|\bcamps?\b|\bquests?\b|\bmana\b|\blol\b|\bgrats\b|\bgz\b|\bpets?\b"
                  r"|\btaunt(?:s|ed|ing)?\b|\bstun\w*\b(?! ?breaks?\b)|\bline of sight\b", I),
    ("chat word", r"\bloot\w*\b(?! tables?\b)|\bdrop(?:s|ped|ping)?\b", I),
]
GAME_TERMS_FAIL = True    # the clean-up review (2026-11-22): every GAME_TERMS hit is a lint failure (False turns them back into warnings)

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

ALL_KINDS = ("narrative", "advice", "chat")

def compile_rules(rules):
    return [(r[0], re.compile(r[1], r[2])) + ((r[3] if len(r) > 3 else ALL_KINDS),) for r in rules]

EVERYWHERE_RE = compile_rules(EVERYWHERE)
NARRATIVE_RE = compile_rules(NARRATIVE)
CHAT_FILE_RE = compile_rules(CHAT_FILE)
GAME_TERMS_RE = compile_rules(GAME_TERMS)
PENDING_RE = compile_rules(PENDING_OWNER_DECISION)
EXCEPTION_RE = re.compile("|".join(EXCEPTIONS), I)
GAME_TERM_EXCEPTION_RE = re.compile("|".join(GAME_TERM_EXCEPTIONS), I)

def hits(text, kind):
    """(rule kind, matched text) for each forbidden word in a text of this field kind (narrative, advice, chat, or
    chatfile for a line of the lore's chat files). These always fail the lint. Pure."""
    t = EXCEPTION_RE.sub(" ", text or "")
    rules = EVERYWHERE_RE + (NARRATIVE_RE if kind == "narrative" else CHAT_FILE_RE if kind == "chatfile" else [])
    return [(k, m.group(0)) for k, rx, _ in rules for m in rx.finditer(t)]

def game_term_hits(text, kind, ban_pending=None):
    """(rule kind, matched text) for each game term in a lore JSON field of this kind (narrative, advice or chat), less
    any stretch of text that hits() already fails. With ban_pending (default BAN_PENDING) the PENDING_OWNER_DECISION words count too;
    otherwise they are removed first, so a map mark's figures are not read as numbers. The lore's .lines chat files are
    not checked here: they are the period's chat voice (REVIEW-FOURTH-WALL.md, owner question 5). Pure."""
    if kind not in ALL_KINDS: return []
    ban = BAN_PENDING if ban_pending is None else ban_pending
    blank = lambda m: " " * len(m.group(0))
    t = EXCEPTION_RE.sub(blank, text or "")
    hard_rules = EVERYWHERE_RE + (NARRATIVE_RE if kind == "narrative" else [])
    taken = [m.span() for _, rx, _ in hard_rules for m in rx.finditer(t)]    # already failing in hits()
    t = GAME_TERM_EXCEPTION_RE.sub(blank, t)
    found = []
    if ban:
        found = [(k, m) for k, rx, kinds in PENDING_RE if kind in kinds for m in rx.finditer(t)]
    else:
        for _, rx, _ in PENDING_RE: t = rx.sub(lambda m: " " * len(m.group(0)), t)
    found += [(k, m) for k, rx, kinds in GAME_TERMS_RE if kind in kinds for m in rx.finditer(t)]
    out = []
    for k, m in sorted(found, key=lambda x: (x[1].start(), -x[1].end())):   # one hit per stretch of text
        if any(m.start() < e and s < m.end() for s, e in taken): continue
        taken.append(m.span()); out.append((k, m.group(0)))
    return out

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

def game_term_problems(lore_dir):
    """Every game term in a player-visible lore JSON field: (file, field path, kind, rule, word, text)."""
    out = []
    for f in sorted(glob.glob(os.path.join(lore_dir, "*", "*.json"))):
        folder = os.path.basename(os.path.dirname(f))
        rel = f"{folder}/{os.path.basename(f)}"
        for path, text, kind in visible(json.load(open(f, encoding="utf-8")), folder):
            for k, word in game_term_hits(text, kind):
                out.append((rel, path, kind, k, word, text))
    return out

def format_game_term(rel, path, kind, k, word, text):
    return f"{rel} {path}: game term ({k}, '{word}'; owner 2026-11-21): {text[:90]}"

def warnings(lore_dir):
    """The game-term hits while GAME_TERMS_FAIL is False (then problems() leaves them out); else none."""
    return [] if GAME_TERMS_FAIL else [format_game_term(*x) for x in game_term_problems(lore_dir)]

def problems(lore_dir, chat_dir=CHAT_DIR):
    out = []
    for f in sorted(glob.glob(os.path.join(lore_dir, "*", "*.json"))):
        folder = os.path.basename(os.path.dirname(f))
        rel = f"{folder}/{os.path.basename(f)}"
        for path, text, kind in visible(json.load(open(f, encoding="utf-8")), folder):
            for k, word in hits(text, kind):
                out.append(f"{rel} {path}: fourth wall ({k}, '{word}'; owner decision 8): {text[:90]}")
    if GAME_TERMS_FAIL:
        out += [format_game_term(*x) for x in game_term_problems(lore_dir)]
    for name in LORE_CHAT_FILES:
        p = os.path.join(chat_dir, name)
        if not os.path.exists(p): continue
        for n, text in chat_lines(p):
            for k, word in hits(text, "chatfile"):
                out.append(f"chat/{name}:{n}: fourth wall ({k}, '{word}'; owner decision 8): {text[:90]}")
    return out

REPORT = os.path.join(os.path.dirname(__file__), "..", "..", "docs", "1.2.1", "LINT-LEGENDS-HITS.md")

def report(lore_dir):
    """docs/1.2.1/LINT-LEGENDS-HITS.md: every game-term hit, by realm, file and field."""
    from collections import Counter, defaultdict
    hits_ = game_term_problems(lore_dir)
    realm = {}
    for f in glob.glob(os.path.join(lore_dir, "*", "*.json")):
        realm[f"{os.path.basename(os.path.dirname(f))}/{os.path.basename(f)}"] = json.load(open(f, encoding="utf-8")).get("realm") or "Neutral"
    realms = ["Albion", "Midgard", "Hibernia", "Neutral"]
    rules = sorted({x[3] for x in hits_})
    by_rule = Counter((realm[x[0]], x[3]) for x in hits_)
    by_kind = Counter((realm[x[0]], x[2]) for x in hits_)
    out = ["# Game terms left in player-visible lore (generated)", "",
           "Generated by `python3 tools/lore/fourth_wall.py --report`. Do not edit by hand: clear the words in the lore files and",
           "run it again. The rules are `GAME_TERMS` in `tools/lore/fourth_wall.py` (owner 2026-11-21, \"Rewrite that\": no game",
           "terms in any player-visible field, fight advice included). " + ("`GAME_TERMS_FAIL = True`: `lint_lore.py` fails on each"
           " of these." if GAME_TERMS_FAIL else "`lint_lore.py` prints these as warnings until `GAME_TERMS_FAIL = True`."),
           "Words in `PENDING_OWNER_DECISION` (map marks; melee, casters, group,",
           "camp, quest, mana, lol, grats, dropped, loot, pets, taunt, stun, line of sight) are not listed: the owner has not ruled.",
           "",
           "- **Fields:** `narrative` is told text (story, chapters, book pages, loremaster words, yells, history); `advice` is",
           "  the legend (\"What the raid leaders say\"); `chat` is the bot lines.",
           "- **A hit is a word, not a line:** one line can hold several.",
           f"- **Total:** {len(hits_)} hits in {len({x[0] for x in hits_})} files.", "",
           "## By realm and kind", "",
           "| Realm | " + "".join(k + " | " for k in rules) + "narrative | advice | chat | total |",
           "|---|" + "---|" * (len(rules) + 4)]
    for r in realms:
        n = sum(1 for x in hits_ if realm[x[0]] == r)
        out.append(f"| {r} | " + "".join(f"{by_rule[(r, k)]} | " for k in rules)
                   + " | ".join(str(by_kind[(r, k)]) for k in ALL_KINDS) + f" | {n} |")
    for r in realms:
        files = defaultdict(lambda: defaultdict(list))
        for rel, path, kind, k, word, _ in hits_:
            if realm[rel] == r: files[rel][path].append(word)
        out += ["", f"## {r} ({sum(len(w) for p in files.values() for w in p.values())} hits, {len(files)} files)", ""]
        for rel in sorted(files):
            out.append(f"- **{rel}**")
            for path, words in files[rel].items():
                out.append(f"  - `{path.split('.', 1)[1]}`: " + ", ".join(f"`{w}`" for w in words))
    return "\n".join(out) + "\n"

if __name__ == "__main__":
    import sys
    from lore_text import LORE_DIR
    if "--report" in sys.argv:
        with open(REPORT, "w", encoding="utf-8", newline="\n") as fh: fh.write(report(LORE_DIR))
        print(f"wrote {os.path.normpath(REPORT)}")
