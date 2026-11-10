#!/usr/bin/env python3
"""Audits bot chat transcripts (CHAT_LINE rows of the server log) for coherence.

The server writes one row per chat line (BotChatTranscript):

    CHAT_LINE who=bot ch=faction realm=Albion from="Cabowyn" via=... src=thread ex=12 i=2 role=asker cls="Reaver" lvl=50 text="..."

From 1.1.3 a bot line carries src= (the feature), and inside an exchange ex= (its id), i= (the line's index) and role=, plus the
speaker's real cls= and lvl=. Older logs have only via=; for them the checks fall back to heuristics over the line stream.

Reports, per log:
  * lines per minute per channel (and the busiest minute)
  * exchanges out of order or with swapped roles (exact, from ex/i/role)
  * dialogue-order slips (heuristic): a presence answer ("sup", "me") after the asker already closed, a level or class
    question after the invite, thanks with no answer since the thanker's question, a follow-up ("which part?") before
    anyone answered, a trade closing ("ill keep it then") after a reply that turned nothing down
  * class and role contradictions: a class or level claim that is not the speaker's (exact with cls=/lvl=), a join answer
    to a role ask from a class that can't fill it, one speaker claiming two classes
  * reactions with no trigger in the previous 30 s, late reactions, pile-ons (more than two reactions to one trigger)
  * repeated short reactions (the same reaction within 10 minutes in one channel) and repeated short lines of any kind
  * lines in system voice: a bot line in /faction, /say, /lfg, /trade or /broadcast that reads as a game announcement ("level-50
    adventurers", "There is an opportunity", "Join them at", a semicolon, a formally capitalised paragraph); a VERDICT failure.
    Also, for information, the same reaction of three words or more again within 60 minutes in one channel
  * silent features: a gold spammer, rumour or roleplayer line that was due (LIVING_FEATURE_DUE feature= realm= ex=) and
    never said in /faction (no CHAT_LINE with that src= and ex=), whether it gave up (LIVING_FEATURE_DROPPED) or not; one
    dropped because nobody was left to hear it (reason=no_audience) isn't counted, nor one due in the log's last minutes
  * bot questions with no reply within 60 s (see check_unanswered for what counts as a question)
  * /lfg and /trade posts with any reply (informational)
  * (RC6, 2026-11-09) lines against the log's facts and the clock (see check_story_facts): a death story that doesn't match
    the teller's last AUTONOMOUS_BOT_DEATH (RvR words for a monster kill, a monster for a player kill, more attackers than
    there were), "tonight" in the morning, internal words ("rendezvous"), a question answered by a bare "oof", a run-on after
    a "quiet atm", one bot twice in /group within 3 s, too much hunting banter in /group, "back" from nowhere; and, for
    information, /group hunting talk and a duo offer by tell that the bot then declined for a duty (BOT_INVITE_RESULT)

Python standard library only. Usage:
    python3 chat_audit.py LOG [LOG ...] [--json OUT] [--channel faction] [--show N]
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field

ROW = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*?CHAT_LINE (?P<body>.*)$")
FEATURE_ROW = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*?LIVING_FEATURE_(?P<kind>DUE|DROPPED) (?P<body>.*)$")
# How long a due feature may wait for its turn (AutonomousChatExchange.DueWaitMs), plus a little slack.
DUE_WAIT_S = 180 + 30
PAIR = re.compile(r'(\w+)=(?:"([^"]*)"|(\S+))')


@dataclass
class Line:
    n: int
    t: int  # seconds since the first line
    who: str
    ch: str
    realm: str
    speaker: str
    text: str
    via: str = ""
    src: str = ""
    ex: str = ""
    i: int | None = None
    role: str = ""
    cls: str = ""
    lvl: int | None = None
    cap: float | None = None
    raw: str = ""
    clock: int = 0  # the log's own stamp (parse_stamp), for the time of day and the AUTONOMOUS_* rows

    @property
    def stream(self) -> tuple[str, str]:
        return (self.ch, self.realm)


def parse_stamp(stamp: str) -> int:
    month, rest = stamp.split("/", 1)
    day, clock = rest.split(" ", 1)
    h, m, s = (int(x) for x in clock.split(":"))
    return ((int(month) * 31 + int(day)) * 24 + h) * 3600 + m * 60 + s


@dataclass
class Feature:
    t: int
    kind: str  # DUE or DROPPED
    feature: str
    realm: str
    ex: str
    reason: str = ""


def read(path: str, features: list[Feature] | None = None) -> list[Line]:
    """The CHAT_LINE rows; with a list, the LIVING_FEATURE_DUE/DROPPED rows go into it (same clock)."""
    lines: list[Line] = []
    first = None
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            match = ROW.match(raw.rstrip("\n"))
            if not match:
                due = FEATURE_ROW.match(raw.rstrip("\n")) if features is not None else None
                if due:
                    fields = {key: quoted if quoted or bare == "" else bare for key, quoted, bare in PAIR.findall(due.group("body"))}
                    stamp = parse_stamp(due.group("stamp"))
                    first = stamp if first is None else first
                    features.append(Feature(stamp - first, due.group("kind"), fields.get("feature", "?"), fields.get("realm", "?"),
                                            fields.get("ex", ""), fields.get("reason", "")))
                continue
            fields: dict[str, str] = {}
            body = match.group("body")
            # text= is last and may hold anything; take it first.
            text_at = body.find(' text="')
            text = ""
            if text_at >= 0:
                text = body[text_at + 7:]
                text = text[:-1] if text.endswith('"') else text
                body = body[:text_at]
            for key, quoted, bare in PAIR.findall(body):
                fields[key] = quoted if quoted or bare == "" else bare
            stamp = parse_stamp(match.group("stamp"))
            first = stamp if first is None else first
            lvl = fields.get("lvl")
            index = fields.get("i")
            cap = fields.get("cap")
            # ex=0 is no exchange (1.1.3 RC1 tagged every reply to a player ex=0).
            ex = fields.get("ex", "")
            ex = "" if ex in ("0", "-") else ex
            lines.append(Line(len(lines), stamp - first, fields.get("who", "bot"), fields.get("ch", "?"), fields.get("realm", "?"),
                              fields.get("from", "?"), text, fields.get("via", ""), fields.get("src", ""), ex,
                              int(index) if index and index.lstrip("-").isdigit() else None, fields.get("role", ""), fields.get("cls", ""),
                              int(lvl) if lvl and lvl.isdigit() else None, float(cap) if cap and re.match(r"^\d+(\.\d+)?$", cap) else None,
                              raw.rstrip("\n"), stamp))
    return lines


def norm(text: str) -> str:
    return re.sub(r"\s+", " ", re.sub(r"[^a-z0-9]+", " ", text.lower())).strip()


def words(text: str) -> list[str]:
    return norm(text).split()


# ---- lexicons -----------------------------------------------------------------------------------------------------------

CLASS_WORDS = {
    "armsman": ["arms", "armsman", "armsmen"], "paladin": ["pally", "paladin", "pal", "pala"], "mercenary": ["merc", "mercenary"],
    "reaver": ["reaver"], "cleric": ["cleric", "clr"], "friar": ["friar"], "minstrel": ["mins", "minstrel", "minst"], "scout": ["scout"],
    "infiltrator": ["inf", "infil", "infiltrator"], "sorcerer": ["sorc", "sorcerer"], "wizard": ["wiz", "wizard"],
    "theurgist": ["theu", "theurg", "theurgist"], "cabalist": ["cab", "cabby", "cabalist"], "necromancer": ["necro", "necromancer"],
    "warrior": ["warrior"], "thane": ["thane"], "skald": ["skald"], "berserker": ["zerk", "zerker", "berserker"],
    "savage": ["sav", "savage"], "hunter": ["hunter"], "shadowblade": ["sb", "shadowblade"], "healer": ["healer"],
    "shaman": ["shaman", "shammy"], "runemaster": ["rm", "runemaster"], "spiritmaster": ["sm", "spiritmaster"],
    "bonedancer": ["bd", "bonedancer"], "hero": ["hero"], "champion": ["champ", "champion"], "blademaster": ["bm", "blademaster"],
    "ranger": ["ranger"], "nightshade": ["ns", "nightshade"], "druid": ["druid"], "bard": ["bard"], "warden": ["warden"],
    "eldritch": ["eld", "eldritch"], "enchanter": ["ench", "enchanter"], "mentalist": ["menta", "ment", "mentalist"],
    "animist": ["animist"], "valewalker": ["vw", "valewalker"], "sluaghbinder": ["sluaghbinder"],
}
WORD_CLASS = {w: cls for cls, ws in CLASS_WORDS.items() for w in ws}
# "healer" is a class and a role word; a claim "healer here" from a cleric is fine, so it is only a class in "{n} healer".
AMBIGUOUS = {"healer", "hunter"}
# Class words that are also armour slots: "wtb leather arms 4 26 inf" is an infiltrator buying arm armour, not an armsman
# (RC2 live check, 2026-11-09). In a trade line, or after an armour word, such a word is the item.
SLOT_CLASS_WORDS = {"arms"}
TRADE_WORDS = {"wtb", "wts", "wtt", "selling", "buying", "sell", "buy", "pc", "price", "trade", "trading"}
ARMOUR_WORDS = {"leather", "studded", "chain", "plate", "cloth", "scale", "reinforced", "mail", "quilted", "padded", "roman",
                "crafted", "spellcrafted", "sc", "armor", "armour", "my", "new", "old", "dragonslayer", "epic", "drop", "the"}

ROLE_CLASSES = {
    "tank": {"paladin", "armsman", "reaver", "warrior", "thane", "hero", "champion", "sluaghbinder"},
    "healer": {"cleric", "friar", "druid", "warden", "healer", "shaman"},
    "cc": {"sorcerer", "minstrel", "healer", "bard", "mentalist"},
    "mezzer": {"sorcerer", "minstrel", "healer", "bard", "mentalist"},
    "puller": {"scout", "hunter", "ranger", "paladin", "armsman", "reaver", "warrior", "thane", "hero", "champion", "sluaghbinder"},
    "cleric": {"cleric"}, "druid": {"druid"}, "shaman": {"shaman"}, "bard": {"bard"},
}
ROLE_ASK = re.compile(r"\b(?:need|lf|any|looking for)\s+(?:a |an |\d )?(?P<role>tank|healer|cc|mezzer|puller|cleric|druid|shaman|bard)s?\b")
JOIN_ACK = re.compile(r"^(?:me|x|me pls|x me|me!|sec,? me|can i come|inv pls|me,? .*|\d{1,2} \w+ here|\w+ \d{1,2},? x\??|\w+,? \d{1,2})$")
NOT_JOIN = re.compile(r"^(?:afk|brb|back|bubs|bubbles|still|ding|lvl|level|almost)\b")

PRESENCE_ACK = {"me", "x", "sup", "me sup", "me whats up", "here", "o", "me pls", "x me", "sec me", "inv pls", "ya", "me too"}
THANKS = re.compile(r"^(?:ty|thx|thanks|k ty|kk|ty!?|ty all|ty ty|cool ty|makes sense ty|ah ok|ah ty|ok thx|thx makes sense|got one ty|ty omw|k ty|"
                    r"ty inv|kk inv|inv sent|k x|good to know|will do|makes sense|heh ty|ugh ya)$")
INVITE = re.compile(r"^(?:kk inv|ty inv|inv sent|k x|inv)$")
DECLINE_CLOSING = {"ill keep it then", "might hold onto it then", "k ty anyway", "fair ill think about it"}
TURN_DOWN = re.compile(r"\b(?:pass|no thanks|no thx|nah|too much|too rich|overpriced|not for me|not interested|not buying|cant afford|broke|not worth|no ty)\b")
FOLLOW_UPS = {
    "which part", "where exactly", "near what", "what lvl for that", "how long does it take", "solo or grp", "what race", "grp or solo",
    "which camp there", "how far in", "is it busy", "solo ok there", "what mobs", "what lvl r u", "where u at", "what class", "u close",
    "how far r u", "how many up", "is it camped", "which side", "who took it", "where r they", "which way", "what time", "who s leading",
    "what stats", "what lvl is it", "where are the mobs for it", "how do u like it", "solo much", "solo or grp spec",
}
LEVEL_OR_CLASS_Q = re.compile(r"^(?:what (?:lvl|level)|what class|lvl\?|class\?)")
ANSWERS = {
    "me", "x", "sup", "me sup", "me whats up", "sec me", "main line first", "max ur main line", "sc gear if u can afford it", "sc if u can afford it",
    "whatever drops lol", "drops till 50 lol", "depends solo or grp", "grp is better", "solo outside is ok", "front rooms are easy",
    "back rooms if u have a healer", "was quiet earlier", "the map helps", "crafters pst one", "merchants in the capital", "the broker",
    "dunno exactly ask a guard", "near the town i think", "go see the crafting trainer", "tools from the merchant", "lots of money lol",
    "at the bindstone in town", "nearest town", "by the inn", "port from the capital", "horse to the border keep", "porter in town",
    "did it kill 10 then go back", "dunno that one", "which one", "the one by the town", "back in town", "the guy that gave it",
    "dunno check the journal", "dunno never got it", "decent stuff", "jewelry mostly i think", "kill enemies get rps", "kills and keeps",
    "keeps give a lot", "not yet", "need a grp for it", "ask in guild", "not really", "ok for now", "sell it", "dunno try the broker",
    "not much", "no run", "horse most of the way", "dunno", "ya", "think so", "few", "2 last i saw", "was empty", "dunno just got here",
    "north side", "earlier tonight", "big grp", "small grp", "few grps", "through the mg", "9ish", "after dinner", "gl is", "love it",
    "its ok", "fun in a grp", "grp always", "depends what u do most", "grp mostly", "some", "out past the road", "next to the merchants",
}

REACTION = re.compile(
    r"^(?:oof|rip|lol rip|ouch|ugh|bleh|gz|gz!+|gz gz|grats|gratz|grats!|grats grats|grats man|nice|nice!|nice gz|woot|wow nice|sweet|lucky|"
    r"lucky you|lucky you lol|jealous|show off|well earned|well played|well done|nice going|nice score|good stuff|go you|way to go|"
    r"look at you|must be nice|not bad at all|very nice|brag brag brag|teach me|pics or it didnt happen|classic|happens|happens lol|"
    r"rip grp|that stinks|that sucks|ugh that sucks|ugh same|ugh the worst|rough|rough night huh|bad luck|been there|"
    r"bleh been there|hang in there|it gets better|ah man|sorry that is rough|oof sorry|oof sorry heh|thats annoying|"
    r"argh hate that|ugh i feel you|the dice hate us all|the worst feeling|thats just mean|dont let it get to you|happens to the best of us|"
    r"yep happened to me too|next one will go better|take a break maybe|been there haha|grr i know that one|been like that all night haha|"
    r"aww sorry that is awful|ha welcome to my week|yep it does that|forgive me unlucky|gz u earned it|gz you earned it|"
    r"ha nice now find something meaner|now thats a good night|lol|heh|haha|true|lol true|ha|nice gz)$")
TRIGGER = re.compile(
    r"\b(?:died|dead|die|killed|got me|rip me|wiped?|wipe|ding|dinged|50!+|finally|got that|is down|owe it|has it out|camping|"
    r"took|lost|captured|relic|keep|grats|hate me|ganked|train|nemesis|again|even|got him|got her|told u|dropped|drop|loot|won|beat|"
    r"keeps killing|killing me|got the|epic|lvl|level|bubble)\b", re.I)


def is_reaction(line: Line) -> bool:
    if line.src in ("debate", "storyline"):
        return False  # a scene's written lines (a dunce's roast is the scene, not a pile-on)
    if line.src in ("burst", "reaction") or line.role in ("reactor", "riff"):
        return True
    return bool(REACTION.match(norm(line.text)))


def is_question(text: str) -> bool:
    t = norm(text)
    return text.rstrip().endswith("?") or bool(re.match(r"^(?:any|anyone|any1|who|where|what|how|is|does|lf|need|lfg|lfm|ne1)\b", t))


def class_claims(text: str) -> list[tuple[str, int | None]]:
    """("cabalist", 50) for "cab, 50", "me, cab 50", "50 cab here", "lfg 28 cab"."""
    t = norm(text)
    tokens = t.split()
    claims = []
    trade = any(token in TRADE_WORDS for token in tokens)
    for k, token in enumerate(tokens):
        cls = WORD_CLASS.get(token)
        if not cls:
            continue  # plurals ("any reavers on?") ask about a class, they don't claim one
        before = tokens[k - 1] if k > 0 else ""
        after = tokens[k + 1] if k + 1 < len(tokens) else ""
        if token in SLOT_CLASS_WORDS and (trade or before in ARMOUR_WORDS):
            continue  # an armour slot ("wtb leather arms"), not a class
        number = int(before) if before.isdigit() else int(after) if after.isdigit() else None
        if number is not None and not 1 <= number <= 50:
            number = None
        if before in ("lf", "need", "any", "for", "lfm", "wanted"):
            continue  # "lf healer 4-8" asks for a class, it doesn't claim one
        if before in ("any", "need", "for", "the", "a", "an", "lf", "vs", "that", "those", "some", "other", "about", "my", "ur", "your",
                      "his", "her", "elf", "dwarf", "norseman", "celt", "briton", "troll", "kobold", "lurikeen", "firbolg", "highlander",
                      "saracen", "avalonian", "inconnu", "valkyn", "shar", "half", "minotaur", "same", "this", "every", "alb", "mid", "hib",
                      "of", "no", "on", "like", "play", "what", "which", "per", "and", "or", "with") and number is None:
            continue
        if token in AMBIGUOUS and number is None:
            continue
        first_person = number is not None or k == 0 and (len(tokens) == 1 or after in ("here", "lfg", "lf", "pst", "me")) or \
            before in ("me", "im", "i m") or after == "here"
        if first_person:
            claims.append((cls, number))
    return claims


# ---- the checks ---------------------------------------------------------------------------------------------------------


@dataclass
class Report:
    path: str
    lines: int = 0
    minutes: float = 0.0
    tagged: int = 0
    per_minute: dict = field(default_factory=dict)
    busiest_minute: dict = field(default_factory=dict)
    counts: Counter = field(default_factory=Counter)
    peak_rate: dict = field(default_factory=dict)
    ceiling_known: bool = True
    examples: dict = field(default_factory=lambda: defaultdict(list))

    def flag(self, kind: str, *lines: Line, note: str = ""):
        self.counts[kind] += 1
        if len(self.examples[kind]) < 12:
            self.examples[kind].append(" | ".join(f"{l.t // 60:>3}:{l.t % 60:02d} {l.speaker}: {l.text}" for l in lines) + (f"  [{note}]" if note else ""))


# Lines the /faction ceiling doesn't hold: a realm event said on purpose, and lines to or about the real player.
CEILING_EXEMPT = ("event", "player")


def check_ceiling(report: Report, lines: list[Line], ceiling: float | None, channel: str = "faction"):
    """Seconds and minutes in which the bots' lines in a channel the ceiling holds ran above it (two-minute rate, as the
    server measures it). The ceiling comes from each line's cap= field, else from --ceiling (/faction) or --say-ceiling.
    /say: only the lines the player heard are logged, so this is the rate around the player (1.1.3 RC1 live check: about 25
    "released, running back" lines in 45 s at Camelot's bind)."""
    prefix = "" if channel == "faction" else channel + "_"
    by_realm: dict[str, list[Line]] = defaultdict(list)
    for l in lines:
        if l.ch == channel and l.who == "bot":
            by_realm[l.realm].append(l)
    if channel == "faction":
        report.ceiling_known = True
    for realm, seq in by_realm.items():
        counted = [l.t for l in seq if l.src not in CEILING_EXEMPT]
        caps = [(l.t, l.cap) for l in seq if l.cap is not None]
        if not caps and ceiling is None:
            if channel == "faction":
                report.ceiling_known = False
            continue
        lo = hi = 0
        c = 0
        cap_now = caps[0][1] if caps else ceiling
        peak = 0.0
        minutes = set()
        for t in range(seq[0].t, seq[-1].t + 1):
            while c < len(caps) and caps[c][0] <= t:
                cap_now = caps[c][1]
                c += 1
            while hi < len(counted) and counted[hi] <= t:
                hi += 1
            while lo < hi and counted[lo] <= t - 120:
                lo += 1
            rate = (hi - lo) / 2.0
            peak = max(peak, rate)
            if rate > cap_now + 1e-9:
                report.counts[prefix + "seconds_over_ceiling"] += 1
                minutes.add(t // 60)
        report.counts[prefix + "minutes_over_ceiling"] += len(minutes)
        report.peak_rate[realm if channel == "faction" else f"{realm} /{channel}"] = (peak, cap_now)


def check_features(report: Report, lines: list[Line], features: list[Feature]):
    """Every feature that was due spoke: a /faction line with its src= and ex= (see the module notes)."""
    end = max([l.t for l in lines] + [f.t for f in features] + [0])
    spoke = {(l.src, l.realm, l.ex) for l in lines if l.ch == "faction" and l.ex}
    dropped = {(f.feature, f.realm, f.ex): f.reason for f in features if f.kind == "DROPPED"}
    for f in features:
        if f.kind != "DUE":
            continue
        report.counts["features_due"] += 1
        key = (f.feature, f.realm, f.ex)
        if key in spoke:
            continue
        reason = dropped.get(key)
        if reason == "no_audience" or reason is None and end - f.t < DUE_WAIT_S:
            continue
        if reason is not None:
            report.counts["features_given_up"] += 1
        report.counts["features_silent"] += 1
        report.examples["features_silent"].append(f"{f.t // 60:>3}:{f.t % 60:02d} {f.feature} ex={f.ex} ({f.realm}) never spoke" +
                                                  (f"  [gave up: {reason}]" if reason else ""))


def audit(path: str, channel_filter: str | None = None, ceiling: float | None = None, say_ceiling: float | None = None) -> Report:
    features: list[Feature] = []
    lines = read(path, features)
    report = Report(path, len(lines))
    if not lines:
        check_features(report, lines, features)
        return report
    span = max(60, lines[-1].t - lines[0].t + 1)
    report.minutes = span / 60.0
    report.tagged = sum(1 for l in lines if l.ex)
    by_channel = Counter(l.ch for l in lines)
    report.per_minute = {ch: round(n / report.minutes, 2) for ch, n in sorted(by_channel.items())}
    for ch in by_channel:
        minutes = Counter(l.t // 60 for l in lines if l.ch == ch)
        report.busiest_minute[ch] = max(minutes.values())

    streams: dict[tuple[str, str], list[Line]] = defaultdict(list)
    for l in lines:
        if channel_filter and l.ch != channel_filter:
            continue
        if l.ch in ("lfg", "trade", "broadcast", "tell"):
            continue  # one-way ads and private lines: no dialogue to check
        streams[l.stream].append(l)

    # ---- exact: exchanges ----
    exchanges: dict[str, list[Line]] = defaultdict(list)
    for l in lines:
        if l.ex and l.i is not None:
            exchanges[(l.ch, l.realm, l.ex)].append(l)
    for key, ex_lines in exchanges.items():
        report.counts["exchanges"] += 1
        if len(ex_lines) > 1:
            report.counts["exchanges_multi_line"] += 1
        highest = -1
        for l in ex_lines:
            if l.i < highest:
                report.flag("exact_out_of_order", *ex_lines, note=f"ex={key[2]} i={l.i} after i={highest}")
                break
            highest = max(highest, l.i)
        speakers_by_role: dict[str, set] = defaultdict(set)
        roles_by_speaker: dict[str, set] = defaultdict(set)
        for l in ex_lines:
            if l.role and l.role not in ("reactor", "riff", "third", "speaker"):
                speakers_by_role[l.role].add(l.speaker)
                roles_by_speaker[l.speaker].add(l.role.rstrip("0123456789"))
        swapped = any(len(s) > 1 for s in speakers_by_role.values()) or any(
            {"asker", "answerer"} <= roles or {"teller", "answerer"} <= roles for roles in roles_by_speaker.values())
        if swapped:
            report.flag("exact_role_swap", *ex_lines, note=f"ex={key[2]}")

    for stream, seq in streams.items():
        check_dialogue(report, seq)
        check_reactions(report, seq)
        check_repeats(report, seq)
    check_claims(report, lines)
    check_system_voice(report, lines)
    for seq in streams.values():
        check_reaction_hour(report, seq)
    check_speaker_fit(report, lines, path)
    check_ceiling(report, lines, ceiling)
    check_ceiling(report, lines, say_ceiling, "say")
    check_features(report, lines, features)
    check_unanswered(report, lines)
    check_market_replies(report, lines)
    check_story_facts(report, lines, path)
    return report


def check_dialogue(report: Report, seq: list[Line]):
    for k, line in enumerate(seq):
        t = norm(line.text)
        window = [p for p in seq[max(0, k - 40):k] if line.t - p.t <= 180]
        # The speaker's own last question, and who spoke since.
        own_q = next((p for p in reversed(window) if p.speaker == line.speaker and is_question(p.text)), None)
        others_since_q = [p for p in window if own_q is not None and p.n > own_q.n and p.speaker != line.speaker]

        # Inside a tagged exchange, an earlier line of the same exchange by someone else is what a line refers to.
        same_ex_other = bool(line.ex) and any(p.ex == line.ex and p.speaker != line.speaker for p in window)
        if same_ex_other:
            continue

        # Thanks with no answer since the thanker's question (or no question at all).
        if THANKS.match(t) and not INVITE.match(t):
            if own_q is None:
                report.flag("thanks_without_question", line)
            elif not any(norm(p.text) in ANSWERS or not is_question(p.text) for p in others_since_q):
                report.flag("thanks_without_answer", own_q, line)

        # A referential follow-up ("which part?") before anyone answered the speaker's question.
        if t in FOLLOW_UPS and own_q is not None and norm(own_q.text) not in FOLLOW_UPS:
            answered = any(norm(p.text) in ANSWERS or JOIN_ACK.match(norm(p.text)) for p in others_since_q)
            if not answered:
                report.flag("followup_before_answer", own_q, line)

        # A level or class question after the asker already invited.
        if LEVEL_OR_CLASS_Q.match(t):
            invited = next((p for p in reversed(window) if p.speaker == line.speaker and INVITE.match(norm(p.text)) and line.t - p.t <= 90), None)
            if invited:
                report.flag("asks_after_invite", invited, line)

        # A presence answer ("sup", "me") after the asker closed its thread with thanks.
        if t in PRESENCE_ACK:
            for p in reversed(window):
                if line.t - p.t > 60:
                    break
                if p.speaker != line.speaker and THANKS.match(norm(p.text)):
                    # The asker's opener (its first question in the window): the presence answer belonged right after it.
                    asked = next((q for q in window if q.speaker == p.speaker and q.n < p.n and is_question(q.text)), None)
                    mine_between = asked is not None and any(m.speaker == line.speaker and asked.n < m.n < p.n for m in window)
                    if mine_between:
                        report.flag("answer_after_close", asked, p, line)
                    break

        # "ill keep it then" after a reply that turned nothing down.
        if t in DECLINE_CLOSING:
            last_other = next((p for p in reversed(window) if p.speaker != line.speaker and line.t - p.t <= 60), None)
            if last_other is None or not TURN_DOWN.search(norm(last_other.text)):
                report.flag("closing_mismatch", *( [last_other] if last_other else [] ), line)


def check_reactions(report: Report, seq: list[Line]):
    reactions_to: dict[int, list[Line]] = defaultdict(list)
    for k, line in enumerate(seq):
        if not is_reaction(line) or line.who != "bot":
            continue
        report.counts["reactions"] += 1
        if line.ex:
            # A tagged reaction reacts to its own exchange: a burst to its trigger, a riff to the line just before it.
            same = [p for p in seq[max(0, k - 60):k] if p.ex == line.ex and p.speaker != line.speaker]
            if same:
                burst = line.src in ("burst", "reaction", "event")
                ref = same[0] if burst else same[-1]
                if burst:
                    reactions_to[ref.n].append(line)
                if line.t - ref.t > 45:
                    report.flag("reaction_late", ref, line, note=f"{line.t - ref.t}s")
                continue
        prior = [p for p in seq[max(0, k - 30):k] if line.t - p.t <= 30 and p.speaker != line.speaker]
        trigger = next((p for p in reversed(prior) if not is_reaction(p) and TRIGGER.search(p.text)), None)
        if trigger is None:
            # A reaction to the same line as the reactions just before it still counts against that line.
            longer = [p for p in seq[max(0, k - 30):k] if line.t - p.t <= 90 and p.speaker != line.speaker and not is_reaction(p) and TRIGGER.search(p.text)]
            if longer:
                late = longer[-1]
                between = sum(1 for p in seq if late.n < p.n < line.n)
                reactions_to[late.n].append(line)
                report.flag("reaction_late", late, line, note=f"{line.t - late.t}s, {between} lines between")
            else:
                report.flag("reaction_no_trigger", line)
            continue
        reactions_to[trigger.n].append(line)
    for trigger_n, reacting in reactions_to.items():
        trig = next(l for l in seq if l.n == trigger_n)
        # The caps: a realm event (src=event) draws up to three reactions, anything else up to two.
        cap = 3 if trig.src == "event" else 2
        if len(reacting) > cap:
            report.counts["pileon_lines_over_two"] += len(reacting) - cap
            report.flag("pileon", trig, *reacting, note=f"{len(reacting)} reactions, cap {cap}")


def check_repeats(report: Report, seq: list[Line]):
    last_said: dict[str, int] = {}
    last_reaction: dict[str, int] = {}
    for line in seq:
        if line.who != "bot":
            continue
        t = norm(line.text)
        if not t or len(t.split()) > 3:
            continue
        if t in last_said and line.t - last_said[t] < 600:
            report.counts["short_line_repeat_10min"] += 1
        last_said[t] = line.t
        if is_reaction(line):
            if t in last_reaction and line.t - last_reaction[t] < 600:
                report.flag("reaction_repeat_10min", line, note=f"again after {line.t - last_reaction[t]}s")
            last_reaction[t] = line.t


def check_claims(report: Report, lines: list[Line]):
    claimed: dict[tuple[str, str], Counter] = defaultdict(Counter)
    by_stream: dict[tuple[str, str], list[Line]] = defaultdict(list)
    for l in lines:
        by_stream[l.stream].append(l)
    for l in lines:
        if l.who != "bot":
            continue
        for cls, number in class_claims(l.text):
            claimed[(l.realm, l.speaker)][cls] += 1
            if l.cls:
                if cls != l.cls.lower() and not (cls == "healer" and l.cls.lower() in ROLE_CLASSES["healer"]):
                    report.flag("class_claim_false", l, note=f"is {l.cls}")
                elif number is not None and l.lvl is not None and number != l.lvl:
                    report.flag("level_claim_false", l, note=f"is {l.lvl}")
    for (realm, speaker), classes in claimed.items():
        if len(classes) > 1:
            report.flag("two_classes_claimed", note=f"{speaker} ({realm}): {dict(classes)}")
    # A join answer to a role ask from a class that can't fill the role.
    for seq in by_stream.values():
        for k, line in enumerate(seq):
            match = ROLE_ASK.search(norm(line.text))
            if not match:
                continue
            role = match.group("role")
            for reply in seq[k + 1:k + 12]:
                if reply.t - line.t > 45:
                    break
                if reply.speaker == line.speaker:
                    continue
                t = norm(reply.text)
                if not JOIN_ACK.match(t) or NOT_JOIN.match(t):
                    continue
                if line.ex and reply.ex and line.ex != reply.ex:
                    continue  # tagged: an answer to another exchange
                cls = reply.cls.lower() if reply.cls else None
                if cls is None:
                    # The class the speaker names in this line or its next line within 20 s.
                    near = [reply] + [n for n in seq[seq.index(reply) + 1:seq.index(reply) + 6] if n.speaker == reply.speaker and n.t - reply.t <= 20]
                    found = [c for n in near for c, _ in class_claims(n.text)]
                    cls = found[0] if found else None
                if cls and cls not in ROLE_CLASSES.get(role, {cls}):
                    report.flag("role_answer_false", line, reply, note=f"{cls} for {role}")
                break


# ---- a line fits its speaker (1.1.3, after RC5) -------------------------------------------------------------------------
# On the owner's PC (RC3-RC5, 2026-11-09): Thorroir, a 50 minstrel, said "minst lfg 50, x me", "lfm barrows 47-50",
# "lf1m 47-50 xp grp stonehenge" and "any minsts around? need tips" within twelve minutes; "99% adroit focus staff any good
# for pally?"; Albion bots asked "where do i hand in the uppland quest?". These checks read one speaker's lines together.

# The same stance words as AutonomousChatStance.SeekOf: forming a group ("lfm") or looking for one ("lfg").
SEEK_FORMING = re.compile(
    r"\blf\d*m\b|\blf ?\d more\b|\bneed (?:a |an |\d )?(?:heal|heals|healer|tank|cc|mezzer|dps|puller|caster|support|cleric|druid|shaman|"
    r"bard|\d more)\b(?! pots?\b)|\blf (?:a |an )?(?:heal|healer|tank|cc|mezzer|dps|puller|caster|support|cleric|druid|shaman|bard|"
    r"\d more)\b(?! pots?\b)|\bforming\b|\bgrp up for\b|\bif anyone wants to join\b|\bany1 lf grp\b|\banyone up for\b|\bgrp lf\d*m\b|\broom for \d\b")
SEEK_LOOKING = re.compile(
    r"\blfg\b|\blf (?:xp |rvr |a )?gr(?:ou)?p\b|\blooking for a gr(?:ou)?p\b|\bany gr(?:ou)?ps (?:for|need)\b|\bany room for\b|"
    r"\bfree for grps\b|\bwants a grp\b|\blf duo\b|\bwanna duo\b|\bany rvr grps lf\b")
# A post that ends the story: "nm got a grp", "nvm, full" (AutonomousChatFieldTalk's outrun closing) and the like.
SEEK_CLOSED = re.compile(r"^(?:nm|nvm|nevermind)?,? ?(?:got a grp|grouped|found a grp|found one|full|grp full|full now|heading out|going as is|"
                         r"we re off)\b|\b(?:got a grp|grp full|nm full)\b")
# Lines that are a scene's or a feature's script, or someone else's turn: not the speaker's own story.
NOT_OWN_STORY_SRC = {"debate", "storyline", "story", "spammer", "rumour", "roleplayer", "news", "event", "burst", "reaction"}
NOT_OWN_STORY_ROLE = {"answerer", "reactor", "riff", "third", "B", "C"}

DUNGEON_WORDS = {
    "tomb of mithra": ["tomb of mithra", "mithra tomb", "mithra", "tomb"], "tepok's mine": ["tepok's mine", "tepoks", "tepok"],
    "keltoi fogou": ["keltoi fogou", "keltoi", "fogou"], "stonehenge barrows": ["stonehenge barrows", "sh barrows", "barrows", "stonehenge"],
    "nisse's lair": ["nisse's lair", "nisses", "nisse"], "cursed tomb": ["cursed tomb", "ct"], "vendo caverns": ["vendo caverns", "vendos", "vendo"],
    "varulvhamn": ["varulvhamn", "varulv", "varul"], "spindelhalla": ["spindelhalla", "spindel", "spindle"],
    "muire tomb": ["muire tomb", "muire"], "spraggon den": ["spraggon den", "sprag den", "spraggon"],
    "koalinth tribal caverns": ["koalinth tribal caverns", "koalinth", "koal"], "treibh caillte": ["treibh caillte", "treibh", "tc"],
    "coruscating mine": ["coruscating mine", "coruscating", "cm"],
}
DUNGEON_ALIAS = sorted(((w, d) for d, ws in DUNGEON_WORDS.items() for w in ws), key=lambda pair: -len(pair[0]))

# Frontier zones by realm (AutonomousChatLiveAnswers.FrontierHome), as ShortZone writes them too.
FRONTIER_ZONES = {
    "albion": ["forest sauvage", "snowdonia", "pennine mountains", "pennine mtns", "hadrian's wall"],
    "midgard": ["uppland", "yggdra forest", "jamtland mountains", "jamtland mtns", "odin's gate"],
    "hibernia": ["emain macha", "breifine", "cruachan gorge", "mount collory"],
}
PVE_WORDS = re.compile(r"\b(?:quest|hand in|stable|bind|trainer|camp|camping|grinding|xp|duo|hate me|on the horse|back in|third time tonight|"
                       r"same happened to me)\b")

# Items and classes, the common cases (AutonomousChatGear has the full rule): the heaviest armour a class wears at a level,
# who may hold a staff, a focus staff, a shield of a size, a bow, a two-hander, an instrument.
ARMOR_RANK = {"cloth": 1, "leather": 2, "studded": 3, "reinforced": 3, "chain": 4, "scale": 4, "plate": 5}
HIB_ARMOR = {"reinforced", "scale"}
CLASS_ARMOR = {  # (from level, armour), as GeneratedUniqueItem.Get*ArmorType
    "armsman": [(1, "studded"), (5, "chain"), (15, "plate")], "paladin": [(1, "studded"), (10, "chain"), (20, "plate")],
    "mercenary": [(1, "studded"), (10, "chain")], "reaver": [(1, "studded"), (10, "chain")],
    "minstrel": [(1, "leather"), (10, "studded"), (20, "chain")], "cleric": [(1, "leather"), (10, "studded"), (20, "chain")],
    "scout": [(1, "leather"), (10, "studded")], "friar": [(1, "leather")], "infiltrator": [(1, "leather")],
    "cabalist": [(1, "cloth")], "necromancer": [(1, "cloth")], "sorcerer": [(1, "cloth")], "theurgist": [(1, "cloth")], "wizard": [(1, "cloth")],
    "warrior": [(1, "studded"), (10, "chain")], "thane": [(1, "studded"), (12, "chain")], "skald": [(1, "studded"), (20, "chain")],
    "berserker": [(1, "studded")], "savage": [(1, "studded")], "shadowblade": [(1, "leather")], "hunter": [(1, "leather"), (10, "studded")],
    "healer": [(1, "leather"), (10, "studded"), (20, "chain")], "shaman": [(1, "leather"), (10, "studded"), (20, "chain")],
    "runemaster": [(1, "cloth")], "spiritmaster": [(1, "cloth")], "bonedancer": [(1, "cloth")],
    "hero": [(1, "reinforced"), (15, "scale")], "champion": [(1, "reinforced"), (20, "scale")], "blademaster": [(1, "reinforced")],
    "warden": [(1, "leather"), (10, "reinforced"), (20, "scale")], "druid": [(1, "leather"), (10, "reinforced"), (20, "scale")],
    "bard": [(1, "leather"), (15, "reinforced")], "ranger": [(1, "leather"), (10, "reinforced")], "nightshade": [(1, "leather")],
    "valewalker": [(1, "cloth")], "eldritch": [(1, "cloth")], "enchanter": [(1, "cloth")], "mentalist": [(1, "cloth")], "animist": [(1, "cloth")],
    "sluaghbinder": [(1, "reinforced"), (20, "scale")],
}
HIB_CLASSES = {"hero", "champion", "blademaster", "warden", "druid", "bard", "ranger", "nightshade", "valewalker", "eldritch", "enchanter",
               "mentalist", "animist", "sluaghbinder"}
FOCUS_CASTERS = {"cabalist", "necromancer", "sorcerer", "theurgist", "wizard", "runemaster", "spiritmaster", "bonedancer", "eldritch",
                 "enchanter", "mentalist", "animist"}
STAFF_CLASSES = FOCUS_CASTERS | {"cleric", "friar", "druid", "healer", "shaman"}
SHIELD_SIZE = {  # the largest shield a class carries; a class not listed carries none
    "armsman": 3, "paladin": 3, "reaver": 3, "warrior": 3, "hero": 3, "sluaghbinder": 3, "mercenary": 2, "cleric": 2, "thane": 2,
    "champion": 2, "blademaster": 2, "warden": 2, "friar": 1, "minstrel": 1, "scout": 1, "infiltrator": 1, "skald": 1, "berserker": 1,
    "savage": 1, "shadowblade": 1, "healer": 1, "shaman": 1, "druid": 1, "bard": 1, "ranger": 1, "nightshade": 1,
}
ITEM_USERS = [  # an item word and the classes that use it
    (re.compile(r"\blongbow\b"), {"scout"}), (re.compile(r"\bcrossbow\b"), {"armsman", "infiltrator"}),
    (re.compile(r"\b(?:composite|compound) bow\b"), {"hunter"}), (re.compile(r"\brecurved? bow\b"), {"ranger"}),
    (re.compile(r"\b(?:lute|drum|flute|harp|lyre)\b"), {"minstrel", "bard"}), (re.compile(r"\bscythe\b"), {"valewalker", "sluaghbinder"}),
    (re.compile(r"\b(?:halberd|lucerne|lochaber|partisan|polearm)\b"), {"armsman"}), (re.compile(r"\bflail\b"), {"reaver"}),
    (re.compile(r"\b(?:great sword|greatsword|great axe|great hammer|two handed|claymore)\b"),
     {"armsman", "paladin", "warrior", "thane", "skald", "berserker", "savage", "shadowblade", "hero", "champion"}),
]


def armor_at(cls: str, level: int | None) -> str | None:
    steps = CLASS_ARMOR.get(cls)
    if not steps:
        return None
    level = 50 if level is None else level
    return [armor for start, armor in steps if level >= start][-1]


def can_use(cls: str, level: int | None, item: str) -> bool:
    """The common cases of AutonomousChatGear.CanUse, from the item's name."""
    item = item.lower()
    if cls not in CLASS_ARMOR:
        return True
    if re.search(r"\bfocus\b", item):
        return cls in FOCUS_CASTERS
    if re.search(r"\b(?:shield|buckler)\b", item):
        size = 3 if re.search(r"\b(?:large|tower)\b", item) else 2 if "medium" in item else 1
        return SHIELD_SIZE.get(cls, 0) >= size
    if re.search(r"\b(?:quarter)?staff\b", item):
        return cls in STAFF_CLASSES
    for pattern, users in ITEM_USERS:
        if pattern.search(item):
            return cls in users
    armor = next((a for a in ARMOR_RANK if re.search(rf"\b{a}\b", item)), None)
    if armor:
        if (armor in HIB_ARMOR) != (cls in HIB_CLASSES) and armor not in ("cloth", "leather"):
            return False
        return ARMOR_RANK[armor] <= ARMOR_RANK[armor_at(cls, level)]
    return True


ITEM_FOR_CLASS = [
    re.compile(r"^(?:\d+% )?(?P<item>.+?) any good for (?:a |an |my )?(?:\d+ )?(?P<cls>[a-z]+)\b"),
    re.compile(r"\b(?:wtb|wts|selling|buying|anyone selling|looking for|lf) (?P<item>.+?) for (?:a |an |my )?(?:lvl |level )?(?:\d+ )?(?P<cls>[a-z]+)\b"),
]
OWN_GEAR = re.compile(r"\b(?:wtb|anyone selling|anyone have|anyone got spare|need|looking for|lf|buying|who has) (?P<item>(?:\d+ )?"
                      r"(?:cloth|leather|studded|chain|plate|reinforced|scale) (?:helm|gloves|boots|sleeves|legs|vest|arms|armor|armour))\b")
OWN_TRAINER = re.compile(r"\bwhere(?:'s| is| s)? the (?:(?P<cls>[a-z]+) )?trainer\b")
OWN_TIPS = re.compile(r"\bany (?P<cls>[a-z]+?)s? (?:around|on)\b.*\bneed tips\b|\bneed tips\b")


def own_story(l: Line) -> bool:
    return l.who == "bot" and l.src not in NOT_OWN_STORY_SRC and l.role not in NOT_OWN_STORY_ROLE and l.ch != "tell"


def seek_of(text: str) -> str | None:
    t = text.lower()
    if SEEK_FORMING.search(t):
        return "lfm"
    return "lfg" if SEEK_LOOKING.search(t) else None


def dungeon_in(text: str) -> str | None:
    t = text.lower()
    for word, dungeon in DUNGEON_ALIAS:
        if re.search(rf"(?<![a-z0-9']){re.escape(word)}(?![a-z0-9'])", t):
            return dungeon
    return None


def check_self_contradiction(report: Report, lines: list[Line]):
    """One speaker's group posts across /lfg, /broadcast, /trade, /faction, /say: looking for a group and forming one within 30
    minutes (no "nm got a grp" between), two dungeons within 10 minutes; and a 50 asking where its class trainer is or for
    tips on its own class."""
    by_speaker: dict[tuple[str, str], list[Line]] = defaultdict(list)
    for l in lines:
        if own_story(l):
            by_speaker[(l.realm, l.speaker)].append(l)
    for seq in by_speaker.values():
        flagged_seek = flagged_place = False
        for k, line in enumerate(seq):
            seek = seek_of(line.text)
            if seek:
                report.counts["group_posts"] += 1
            place = dungeon_in(line.text) if seek else None
            for later in seq[k + 1:]:
                if later.t - line.t >= 1800:
                    break
                if SEEK_CLOSED.search(norm(later.text)):
                    break
                other = seek_of(later.text)
                if seek and other and other != seek and not flagged_seek:
                    report.flag("self_lfg_and_lfm", line, later, note=f"{later.t - line.t}s apart")
                    flagged_seek = True
                there = dungeon_in(later.text) if other else None
                if place and there and there != place and later.t - line.t < 600 and not flagged_place:
                    report.flag("self_two_destinations", line, later, note=f"{place} / {there}")
                    flagged_place = True
            cls = (line.cls or "").lower()
            if line.lvl is not None and line.lvl >= 50 and cls:
                t = norm(line.text).replace("where s", "where's")
                trainer = OWN_TRAINER.search(line.text.lower())
                tips = OWN_TIPS.search(t)
                named = (trainer or tips).group("cls") if (trainer or tips) else None
                if (trainer or tips) and (named is None or WORD_CLASS.get(named) == cls or WORD_CLASS.get(named.rstrip("s")) == cls):
                    report.flag("self_own_class_ask", line, note=f"is a {line.lvl} {line.cls}")


def check_item_class(report: Report, lines: list[Line]):
    """An item paired with a class that can't use it ("99% adroit focus staff any good for pally?"), and a want of armour the
    speaker's own class can't wear ("wtb chain boots" from a sorcerer)."""
    for l in lines:
        if l.who != "bot" or l.role in NOT_OWN_STORY_ROLE:
            continue
        t = l.text.lower()
        for pattern in ITEM_FOR_CLASS:
            match = pattern.search(t)
            if match and match.group("cls") in WORD_CLASS:
                cls = WORD_CLASS[match.group("cls")]
                level = l.lvl if l.cls and l.cls.lower() == cls else None
                if not can_use(cls, level, match.group("item")):
                    report.flag("item_class_unusable", l, note=f"{cls} can't use it")
                break
        else:
            own = OWN_GEAR.search(t)
            if own and l.cls and "alt" not in t and not can_use(l.cls.lower(), l.lvl, own.group("item")):
                report.flag("item_class_unusable", l, note=f"a {l.lvl} {l.cls} can't wear it")


def check_enemy_zone(report: Report, lines: list[Line]):
    """A PvE line (a quest, a stable, a bind, a trainer, a camp, xp, a monster death) naming another realm's frontier zone
    ("where do i hand in the uppland quest?" from an Albion bot). Talk of the frontier itself (inc, keeps, rvr) may name it."""
    for l in lines:
        if l.who != "bot" or l.src in ("event", "news", "spammer"):
            continue
        realm = l.realm.lower()
        if realm not in FRONTIER_ZONES:
            continue
        t = l.text.lower()
        enemy = next((z for r, zones in FRONTIER_ZONES.items() if r != realm for z in zones
                      if re.search(rf"(?<![a-z']){re.escape(z)}(?![a-z'])", t)), None)
        # An rvr line may name the frontier and still say "duo" ("rvr in odin's gate, its busy, wanna duo?", RC7).
        if enemy and PVE_WORDS.search(t) and not re.search(r"\b(?:rvr|realm points|keeps?|frontier)\b", t) and not RVR_WORDS.search(t):
            report.flag("pve_enemy_zone", l, note=f"{l.realm} bot, {enemy}")


GROUP_FORMED = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*AUTONOMOUS_GROUP_FORMED group=(?P<group>\S+) .*?members=\"(?P<members>[^\"]*)\"")
GROUP_ENDED = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*AUTONOMOUS_GROUP_TASK_ENDED group=(?P<group>\S+) .*?members=\"(?P<members>[^\"]*)\"")
GROUP_NO_SHOW = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*AUTONOMOUS_GROUP_NO_SHOW \{\"group\":\"(?P<group>[^\"]+)\",\"bot\":\"(?P<bot>[^\"]+)\"")


def check_lfg_while_grouped(report: Report, lines: list[Line], path: str):
    """Informational: an "lfg" post from a bot the log shows in a group then (AUTONOMOUS_GROUP_FORMED until its
    AUTONOMOUS_GROUP_TASK_ENDED or the bot's AUTONOMOUS_GROUP_NO_SHOW)."""
    first = None
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            match = ROW.match(raw)
            if match:
                first = parse_stamp(match.group("stamp"))
                break
    if first is None:
        return
    joined: dict[str, list[list[int]]] = defaultdict(list)
    open_at: dict[tuple[str, str], int] = {}
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            if "AUTONOMOUS_GROUP_" not in raw:
                continue
            for pattern, start in ((GROUP_FORMED, True), (GROUP_ENDED, False)):
                match = pattern.match(raw)
                if match:
                    t = parse_stamp(match.group("stamp")) - first
                    for member in filter(None, match.group("members").split(",")):
                        if start:
                            open_at[(match.group("group"), member)] = t
                        elif (match.group("group"), member) in open_at:
                            joined[member].append([open_at.pop((match.group("group"), member)), t])
            match = GROUP_NO_SHOW.match(raw)
            if match and (match.group("group"), match.group("bot")) in open_at:
                joined[match.group("bot")].append([open_at.pop((match.group("group"), match.group("bot"))), parse_stamp(match.group("stamp")) - first])
    for (group, member), t in open_at.items():
        joined[member].append([t, 10 ** 9])
    if not joined:
        return
    report.counts["info_group_log_known"] = 1
    for l in lines:
        if own_story(l) and seek_of(l.text) == "lfg" and any(a <= l.t < b for a, b in joined.get(l.speaker, [])):
            report.flag("info_lfg_while_grouped", l, note="the log shows it in a group")


def check_speaker_fit(report: Report, lines: list[Line], path: str | None = None):
    check_self_contradiction(report, lines)
    check_item_class(report, lines)
    check_enemy_zone(report, lines)
    if path:
        check_lfg_while_grouped(report, lines, path)


KINDS = [
    ("exact_out_of_order", "exchange lines out of order (exact)"),
    ("exact_role_swap", "exchange roles swapped (exact)"),
    ("answer_after_close", "presence answer after the asker closed"),
    ("asks_after_invite", "level/class asked after the invite"),
    ("followup_before_answer", "follow-up question before any answer"),
    ("thanks_without_answer", "thanks with no answer since the question"),
    ("thanks_without_question", "thanks with no question before"),
    ("closing_mismatch", "trade closing that doesn't follow"),
    ("role_answer_false", "join answer from a class that can't fill the role"),
    ("class_claim_false", "class claim not the speaker's (cls=)"),
    ("level_claim_false", "level claim not the speaker's (lvl=)"),
    ("two_classes_claimed", "one speaker claims two classes"),
    ("group_posts", "group posts (lfg/lfm) by their own speakers"),
    ("self_lfg_and_lfm", "a speaker looks for a group and forms one within 30 min"),
    ("self_two_destinations", "a speaker's group posts name two dungeons within 10 min"),
    ("self_own_class_ask", "a 50 asks where its class trainer is or for tips on its own class"),
    ("item_class_unusable", "an item paired with a class that can't use it"),
    ("pve_enemy_zone", "a PvE line names an enemy realm's zone"),
    ("info_lfg_while_grouped", "(info) an lfg post from a bot the log shows grouped"),
    ("reaction_no_trigger", "reaction with no trigger in 30 s"),
    ("reaction_late", "reaction more than 30 s after its trigger"),
    ("pileon", "triggers with more than two reactions"),
    ("pileon_lines_over_two", "reaction lines past the second"),
    ("reaction_repeat_10min", "same reaction again within 10 min"),
    ("short_line_repeat_10min", "same short line (<=3 words) again within 10 min"),
    ("seconds_over_ceiling", "seconds /faction ran above its ceiling (lines it holds)"),
    ("minutes_over_ceiling", "minutes with any second above the ceiling"),
    ("say_seconds_over_ceiling", "seconds /say around the player ran above its ceiling (lines it holds)"),
    ("say_minutes_over_ceiling", "minutes with any second of /say above its ceiling"),
    ("features_due", "spammers, rumours, roleplayer lines due (LIVING_FEATURE_DUE)"),
    ("features_silent", "of them never said in /faction"),
    ("features_given_up", "of those, given up (LIVING_FEATURE_DROPPED)"),
]


# ---- the voice checks (1.1.3, after RC5) ------------------------------------------------------------------------------

# A game announcement in a bot's mouth (owner's PC, RC5: "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel;
# at least 93 must arrive before assault." in /faction from a bot). Bots type like players; only the system herald writes so.
SYSTEM_VOICE = re.compile(
    r"level-\d+ adventurers|\bthere is an opportunity\b|\bjoin them at\b|\brecruiting up to\b|\bmust arrive before\b|"
    r"\bhas broken out at\b|\bexpedition is advancing\b|\bdeparting together\b|\blate arrivals\b|\bstaged level\b|"
    r"\breinforcements (?:are welcome|join)\b|\brally to its defen[cs]e\b|\bwarbands are (?:gathering|marching)\b|"
    r"\bseize your opportunity\b|\b(?:available|nearby) warbands\b|\bform up at\b|\bhold the walls\b|\bstand by (?:your|ur) realm\b|"
    r"\bthe army marches\b|\bdefen[cs]e rally marches\b|\bthe expedition\b|\barrived at the hub\b", re.I)
# ";)" and ";P" are smileys, not a herald's semicolon.
SEMICOLON = re.compile(r";(?![)PpDd(])")
VOICE_CHANNELS = ("faction", "say", "lfg", "trade", "broadcast")
# The system's own speaker (RealmEventNotices with no bot to say it): it keeps the written notice.
SYSTEM_SPEAKERS = ("Realm herald",)
FORMAL_WORDS = 15


def system_voice_reason(text: str) -> str | None:
    """Why a line reads as a game announcement rather than typed chat, or None: a herald's phrase, a semicolon, or a line
    over FORMAL_WORDS words in two or more sentences that each start with a capital (a narrator's paragraph)."""
    hit = SYSTEM_VOICE.search(text)
    if hit:
        return f'"{hit.group(0)}"'
    if SEMICOLON.search(text):
        return "semicolon"
    stripped = text.strip()
    sentences = [s for s in re.split(r"(?<=[.!?])\s+", stripped) if s]
    letters = [ch for ch in stripped if ch.isalpha()]
    # A shout in capitals (a gold spammer's "CHEAP GOLD!!!") is no herald.
    shouting = letters and sum(ch.isupper() for ch in letters) > len(letters) / 2
    tokens = stripped.split()
    # Spaced-out letters (a spammer's "w w w . F A S T G 0 L D") are no sentences.
    spaced = sum(1 for token in tokens if len(token) == 1) > len(tokens) / 3
    if not shouting and not spaced and len(tokens) > FORMAL_WORDS and len(sentences) >= 2 and all(s[0].isupper() for s in sentences):
        return "formal paragraph"
    return None


def check_system_voice(report: Report, lines: list[Line]):
    """Bot lines in /faction, /say, /lfg, /trade and /broadcast in system voice (a VERDICT failure)."""
    for line in lines:
        if line.who != "bot" or line.ch not in VOICE_CHANNELS or line.speaker in SYSTEM_SPEAKERS:
            continue
        reason = system_voice_reason(line.text)
        if reason:
            report.flag("system_voice", line, note=f"/{line.ch} {line.src or line.via}: {reason}")


# Interjections a reaction may start or end with ("ugh, that one again" is "that one again").
INTERJECTIONS = {"lol", "heh", "hehe", "haha", "ha", "ugh", "oh", "hmm", "oof", "lmao", "bah", "meh", "well"}


def reaction_skeleton(text: str) -> str:
    w = words(text)
    while w and w[0] in INTERJECTIONS:
        w = w[1:]
    while w and w[-1] in INTERJECTIONS:
        w = w[:-1]
    return " ".join(w) or norm(text)


def check_reaction_hour(report: Report, seq: list[Line]):
    """Informational: the same reaction of three words or more again within 60 minutes in one channel (by its skeleton, so
    "ugh, that one again" and "ugh that one again" are one)."""
    last: dict[str, int] = {}
    for line in seq:
        if line.who != "bot" or not is_reaction(line) or len(words(line.text)) < 3:
            continue
        key = reaction_skeleton(line.text)
        if key in last and line.t - last[key] < 3600:
            report.flag("reaction_repeat_60min", line, note=f"again after {(line.t - last[key]) // 60} min")
        last[key] = line.t


VOICE_KINDS = [
    ("system_voice", "bot lines in system voice (/faction, /say, /lfg, /trade, /broadcast)"),
    ("reaction_repeat_60min", "same 3+ word reaction again within 60 min (informational)"),
]


def voice_checks(counts) -> list[tuple[str, int]]:
    """The voice checks the verdict fails on: any bot line in system voice."""
    return [("lines in system voice", counts["system_voice"])]

KINDS.extend(VOICE_KINDS)


def verdict(summary: dict) -> tuple[bool, list[str]]:
    """PASS only with: 0 out of order (exact when the log has exchange ids, else the heuristic dialogue slips), 0 untrue
    role/class/level answers, 0 late or repeated reactions, pile-ons only within the caps, 0 minutes above the ceiling, and no
    feature that was due and never spoke, and 0 minutes of /say above its ceiling (when the log has it)."""
    c = summary["counts"]
    out_of_order = c["exact_out_of_order"] + c["exact_role_swap"] if summary["tagged_lines"] else (
        c["answer_after_close"] + c["asks_after_invite"] + c["followup_before_answer"] + c["thanks_without_answer"])
    checks = [
        ("out of order", out_of_order),
        ("untrue role/class/level", c["role_answer_false"] + c["class_claim_false"] + c["level_claim_false"]),
        ("speaker contradicts itself", c["self_lfg_and_lfm"] + c["self_two_destinations"] + c["self_own_class_ask"]),
        ("item for a class that can't use it", c["item_class_unusable"]),
        ("PvE line names an enemy zone", c["pve_enemy_zone"]),
        ("late reactions", c["reaction_late"]),
        ("repeated reactions", c["reaction_repeat_10min"]),
        ("pile-ons over the caps", c["pileon"]),
        ("minutes above the ceiling", c["minutes_over_ceiling"]),
        ("minutes /say above its ceiling", c["say_minutes_over_ceiling"]),
        ("silent features", c["features_silent"]),
    ]
    checks += voice_checks(c)
    checks += story_fact_checks(c)
    reasons = [f"{name} {n}" for name, n in checks if n]
    reasons += unanswered_reasons(summary)
    if not summary["ceiling_known"]:
        reasons.append("ceiling unknown (no cap= in the log; pass --ceiling)")
    if not summary["tagged_lines"]:
        reasons.append("no exchange ids (a pre-1.1.3 log: heuristic checks only)")
    return not reasons, reasons


def summarize(report: Report) -> dict:
    per_hour = 60.0 / report.minutes if report.minutes else 0
    return {
        "log": report.path, "lines": report.lines, "minutes": round(report.minutes, 1), "tagged_lines": report.tagged,
        "lines_per_minute": report.per_minute, "busiest_minute": report.busiest_minute,
        "reactions": report.counts["reactions"], "exchanges": report.counts["exchanges"], "exchanges_multi_line": report.counts["exchanges_multi_line"],
        "counts": {k: report.counts[k] for k, _ in KINDS},
        "per_hour": {k: round(report.counts[k] * per_hour, 1) for k, _ in KINDS},
        "examples": {k: v for k, v in report.examples.items()},
        "peak_rate": {realm: {"peak": round(p, 1), "ceiling": round(cap, 1)} for realm, (p, cap) in report.peak_rate.items()},
        "ceiling_known": report.ceiling_known,
    }


def print_report(summary: dict, show: int):
    print(f"== {summary['log']}")
    print(f"   {summary['lines']} lines over {summary['minutes']} min; {summary['tagged_lines']} tagged with an exchange; "
          f"{summary['exchanges']} exchanges ({summary['exchanges_multi_line']} multi-line); {summary['reactions']} reactions")
    print("   lines/min: " + ", ".join(f"{ch} {rate}" for ch, rate in summary["lines_per_minute"].items()) +
          "   busiest minute: " + ", ".join(f"{ch} {n}" for ch, n in summary["busiest_minute"].items()))
    for key, label in KINDS:
        print(f"   {summary['counts'][key]:>5}  ({summary['per_hour'][key]:>6}/h)  {label}")
    print_unanswered(summary)
    print_market(summary)
    for realm, rate in summary["peak_rate"].items():
        print(f"   {realm}: busiest two minutes {rate['peak']}/min against a ceiling of {rate['ceiling']}/min (events and lines to the player aside)")
    print("   /say lines a minute (bots and you): " + str(summary["lines_per_minute"].get("say", 0)) + ", busiest minute " + str(summary["busiest_minute"].get("say", 0)))
    if show:
        for key, label in KINDS:
            for example in summary["examples"].get(key, [])[:show]:
                print(f"      [{key}] {example}")


# ---- informational: engagement, follow-ups, regulars (1.1.3; not part of the verdict) ------------------------------------

TO_FIELD = re.compile(r' to="([^"]*)"')


def player_names(lines: list[Line]) -> set[str]:
    """The real players of a log: who spoke as who=player, and who bot tells went to (to=)."""
    names = {l.speaker for l in lines if l.who == "player" and l.speaker not in ("?", "")}
    for l in lines:
        if l.who == "bot" and l.ch == "tell":
            match = TO_FIELD.search(l.raw)
            if match:
                names.add(match.group(1))
    return names


def player_directed(lines: list[Line], names: set[str] | None = None) -> dict:
    """Bot lines aimed at or naming a real player: a tell to them, src=player, or their name in the text. Per hour, by channel."""
    names = player_names(lines) if names is None else names
    pattern = re.compile(r"\b(" + "|".join(re.escape(n) for n in sorted(names)) + r")\b", re.I) if names else None
    hits = []
    for l in lines:
        if l.who != "bot":
            continue
        to = TO_FIELD.search(l.raw)
        if (l.ch == "tell" and to and to.group(1) in names) or l.src == "player" or (pattern and pattern.search(l.text)):
            hits.append(l)
    minutes = max(1.0, (lines[-1].t - lines[0].t + 1) / 60.0) if lines else 1.0
    by_channel = Counter(l.ch for l in hits)
    return {"players": sorted(names), "lines": len(hits), "per_hour": round(len(hits) * 60.0 / minutes, 1),
            "by_channel": dict(sorted(by_channel.items())), "by_role": dict(Counter(l.role or l.src for l in hits).most_common(12))}


def speaker_concentration(lines: list[Line], channel: str = "faction", top: int = 10) -> dict:
    """How much of a channel its busiest speakers carry: distinct speakers, the top N's share, the busiest's count (per realm)."""
    out = {}
    by_realm: dict[str, Counter] = defaultdict(Counter)
    for l in lines:
        if l.ch == channel and l.who == "bot":
            by_realm[l.realm][l.speaker] += 1
    for realm, counts in by_realm.items():
        total = sum(counts.values())
        best = counts.most_common(top)
        out[realm] = {"lines": total, "speakers": len(counts), f"top{top}_share": round(sum(n for _, n in best) / total, 3) if total else 0.0,
                      "busiest": best[0] if best else None, "top": best}
    return out


FOLLOWUP_MIN_AGE_S = 10 * 60


def follow_ups(lines: list[Line]) -> dict:
    """Follow-up exchanges (src=followup): how many, and how many refer back to a real earlier line: the bot asked after
    (role=answerer) spoke in the same realm's channel at least ten minutes before the question, and the question names it."""
    exchanges: dict[tuple, list[Line]] = defaultdict(list)
    for l in lines:
        if l.src == "followup" and l.ex:
            exchanges[(l.ch, l.realm, l.ex)].append(l)
    anchored = unanchored = 0
    examples = []
    for (ch, realm, _), seq in exchanges.items():
        ask = next((l for l in seq if l.role == "asker"), None)
        answer = next((l for l in seq if l.role == "answerer"), None)
        subject = answer.speaker if answer else None
        if ask is None or subject is None:
            continue
        earlier = any(p.ch == ch and p.realm == realm and p.speaker == subject and p.src != "followup" and ask.t - p.t >= FOLLOWUP_MIN_AGE_S
                      for p in lines if p.n < ask.n)
        named = subject.lower() in ask.text.lower()
        if earlier and named:
            anchored += 1
        else:
            unanchored += 1
        if len(examples) < 6:
            examples.append(f"{ask.t // 60:>3}:{ask.t % 60:02d} {ask.speaker}: {ask.text} | {subject}: {answer.text}" + ("" if earlier and named else "  [no earlier line]"))
    return {"exchanges": len(exchanges), "lines": sum(len(v) for v in exchanges.values()), "anchored": anchored, "unanchored": unanchored,
            "bits": sum(1 for l in lines if l.src == "bit"), "examples": examples}


def engagement(path: str) -> dict:
    """The informational metrics of one log (printed after its report, written with --json)."""
    lines = read(path)
    return {"player_directed": player_directed(lines), "faction_concentration": speaker_concentration(lines), "follow_ups": follow_ups(lines)}


def print_engagement(info: dict, show: int):
    pd = info["player_directed"]
    print(f"   [info] lines aimed at or naming the player ({', '.join(pd['players']) or 'none seen'}): {pd['lines']} ({pd['per_hour']}/h) {pd['by_channel']}")
    for realm, c in info["faction_concentration"].items():
        print(f"   [info] {realm} /faction: {c['lines']} lines from {c['speakers']} speakers, top 10 carry {c['top10_share'] * 100:.0f}%, "
              f"busiest {c['busiest'][0]} {c['busiest'][1]}" if c["busiest"] else f"   [info] {realm} /faction: no lines")
    fu = info["follow_ups"]
    print(f"   [info] follow-ups: {fu['exchanges']} exchanges ({fu['lines']} lines), {fu['anchored']} refer to a real earlier line, "
          f"{fu['unanchored']} don't; running bits {fu['bits']}")
    for example in fu["examples"][:show]:
        print(f"      [followup] {example}")
# ---- bot questions nobody answered (1.1.3, after RC5) ---------------------------------------------------------------------
#
# The owner's PC, RC3 and RC5 (2026-11-09): most questions bots asked in /faction drew no reply at all, so the channel read
# like people talking past each other. A question here is a bot's field line that opens its exchange (src=field i=0) in
# /faction or /say and ends in "?". A rhetorical or ambient one is left out: one word ("lag?"), a line that opens with a laugh
# or a groan ("lol", "ugh"), or the realm-pride tags ("only 4 keeps?", "not bad?"). It is answered when another speaker says a
# line of the same exchange (same channel, realm and ex=) within 60 s; the asker's own "still lf..." is not an answer. Logs
# without exchange ids (before 1.1.3) aren't counted. The verdict fails when more than 25% of at least 10 questions went
# unanswered.

UNANSWERED_WITHIN_S = 60
UNANSWERED_MAX_SHARE = 0.25
UNANSWERED_MIN_QUESTIONS = 10
RHETORICAL = re.compile(r"^(?:\S+|(?:lol|heh|haha|ugh|bleh|argh|ok|so|well)\b.*|only \d+ \w+|.*\bnot bad|.*\bkeeps not bad)$")
UNANSWERED_KINDS = [
    ("questions_asked", "bot questions (field openers ending in ?, rhetorical ones aside)"),
    ("questions_unanswered", f"of them with no reply within {UNANSWERED_WITHIN_S} s"),
]


def is_open_question(line: Line) -> bool:
    """A bot's question that wants an answer (see the notes above)."""
    if line.who != "bot" or line.src != "field" or line.i != 0 or line.ch not in ("faction", "say") or not line.ex:
        return False
    return line.text.rstrip().endswith("?") and not RHETORICAL.match(norm(line.text))


def check_unanswered(report: Report, lines: list[Line]):
    """Bot questions with no reply from anyone else in their exchange within 60 s."""
    by_exchange: dict[tuple[str, str, str], list[Line]] = defaultdict(list)
    for l in lines:
        if l.ex:
            by_exchange[(l.ch, l.realm, l.ex)].append(l)
    end = lines[-1].t if lines else 0
    for q in lines:
        if not is_open_question(q) or end - q.t < UNANSWERED_WITHIN_S:
            continue  # a question in the log's last minute may still be answered after it
        report.counts["questions_asked"] += 1
        replies = [l for l in by_exchange[(q.ch, q.realm, q.ex)] if l.speaker != q.speaker and 0 <= l.t - q.t <= UNANSWERED_WITHIN_S]
        if not replies:
            report.flag("questions_unanswered", q, note=f"/{q.ch} ex={q.ex}")


def unanswered_share(counts) -> float:
    return counts["questions_unanswered"] / counts["questions_asked"] if counts["questions_asked"] else 0.0


def unanswered_reasons(summary: dict) -> list[str]:
    """The verdict's reason when too many bot questions went unanswered."""
    c = summary["counts"]
    share = unanswered_share(c)
    if c["questions_asked"] >= UNANSWERED_MIN_QUESTIONS and share > UNANSWERED_MAX_SHARE:
        return [f"unanswered questions {c['questions_unanswered']} of {c['questions_asked']} ({share:.0%}, more than {UNANSWERED_MAX_SHARE:.0%})"]
    return []


def print_unanswered(summary: dict):
    c = summary["counts"]
    if c["questions_asked"]:
        print(f"   bot questions answered within {UNANSWERED_WITHIN_S} s: {c['questions_asked'] - c['questions_unanswered']} of {c['questions_asked']} "
              f"({1 - unanswered_share(c):.0%}; the verdict fails above {UNANSWERED_MAX_SHARE:.0%} unanswered)")


KINDS.extend(UNANSWERED_KINDS)


# ---- replies to /lfg and /trade posts (1.1.3, after RC5; informational) ------------------------------------------------
#
# RC3 on the owner's PC: 260 /lfg and 213 /trade posts, no reply to any of them. Most of this went by /send in 2003, so most
# posts stay unanswered; the share of posts (src=field i=0 lines in /lfg and /trade) with a reply from another speaker in
# their exchange is reported, not judged.

MARKET_KINDS = [
    ("lfg_posts", "/lfg posts (field openers)"),
    ("lfg_posts_replied", "of them with a reply"),
    ("trade_posts", "/trade posts (field openers)"),
    ("trade_posts_replied", "of them with a reply"),
]


def check_market_replies(report: Report, lines: list[Line]):
    """/lfg and /trade posts that drew a reply from anyone else in their exchange (informational)."""
    speakers: dict[tuple[str, str, str], set] = defaultdict(set)
    for l in lines:
        if l.ex and l.ch in ("lfg", "trade"):
            speakers[(l.ch, l.realm, l.ex)].add(l.speaker)
    for l in lines:
        if l.ch in ("lfg", "trade") and l.src == "field" and l.i == 0 and l.ex:
            report.counts[l.ch + "_posts"] += 1
            if speakers[(l.ch, l.realm, l.ex)] - {l.speaker}:
                report.counts[l.ch + "_posts_replied"] += 1


def print_market(summary: dict):
    c = summary["counts"]
    for ch in ("lfg", "trade"):
        if c[ch + "_posts"]:
            print(f"   /{ch} posts with a reply: {c[ch + '_posts_replied']} of {c[ch + '_posts']} ({c[ch + '_posts_replied'] / c[ch + '_posts']:.0%}, informational)")


KINDS.extend(MARKET_KINDS)


# ---- lines against the log's facts and the clock (1.1.3, after RC6) ------------------------------------------------------
#
# RC6 on the owner's PC (2026-11-09, 23 minutes, VERDICT PASS) still read wrong in places: death stories that weren't the
# death the server logged (a monster kill told as "got caught by the bridge", one attacker told as "had 2 on me", two as "a
# zerg, 20+", a bot kill told as "the guards got me"), "busy tonight" at 10:44, "which stable 4 group rendezvous?", "rez in
# murdaigean?" answered "oof", "quiet atm" followed by "how many?" / "small grp" / "omw", a group bot saying two lines in one
# second and eleven lines of hunting banter in six minutes, "ok back" from a bot that had been nowhere, and a login "im solo
# atm if u want company" from a bot that declined the invite five minutes later for its stable route. The server rows these
# read besides CHAT_LINE:
#
#     AUTONOMOUS_BOT_DEATH bot="X" ... realm=Albion ... killer="Y" killerKind=monster|bot|player|pet ... attackers=N ...
#     BOT_INVITE_RESULT bot="X" player="P" ... result=grouped|declined [duty=StableRoute ...]

DEATH_ROW = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*?AUTONOMOUS_BOT_DEATH (?P<body>.*)$")
INVITE_ROW = re.compile(r"^(?P<stamp>\d\d/\d\d \d\d:\d\d:\d\d).*?BOT_INVITE_RESULT (?P<body>.*)$")


@dataclass
class Death:
    clock: int
    bot: str
    realm: str
    killer: str
    kind: str  # monster, bot, player, pet
    attackers: int | None
    group: int = 1  # the bot's group size (group=), 1 when solo
    zone: str = ""


@dataclass
class InviteResult:
    clock: int
    bot: str
    player: str
    result: str
    duty: str


def read_facts(path: str) -> tuple[list[Death], list[InviteResult]]:
    """The AUTONOMOUS_BOT_DEATH and BOT_INVITE_RESULT rows of a log (clock as Line.clock)."""
    deaths: list[Death] = []
    invites: list[InviteResult] = []
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            if "AUTONOMOUS_BOT_DEATH" not in raw and "BOT_INVITE_RESULT" not in raw:
                continue
            raw = raw.rstrip("\n")
            for pattern in (DEATH_ROW, INVITE_ROW):
                match = pattern.match(raw)
                if not match:
                    continue
                f = {key: quoted if quoted or bare == "" else bare for key, quoted, bare in PAIR.findall(match.group("body"))}
                clock = parse_stamp(match.group("stamp"))
                if pattern is DEATH_ROW:
                    attackers = f.get("attackers", "")
                    group = f.get("group", "")
                    deaths.append(Death(clock, f.get("bot", "?"), f.get("realm", "?"), f.get("killer", "?"), f.get("killerKind", "?").lower(),
                                        int(attackers) if attackers.isdigit() else None, int(group) if group.isdigit() else 1,
                                        f.get("zone", "")))
                else:
                    invites.append(InviteResult(clock, f.get("bot", "?"), f.get("player", "?"), f.get("result", "?"), f.get("duty", "")))
                break
    return deaths, invites


def at(line: Line) -> str:
    """The line's clock time, for notes."""
    return f"{line.clock % 86400 // 3600:02d}:{line.clock % 3600 // 60:02d}:{line.clock % 60:02d}"


# 1. Death stories against the death -----------------------------------------------------------------------------------------
STORY_DEATH_WITHIN_S = 600
DEATH_LINE = re.compile(r"\b(?:died|dead|ganked|got me|killed by)\b")
# RvR words: a death at the hands of the enemy realm. A monster kill told so didn't happen.
RVR_WORDS = re.compile(
    r"\b(?:gank(?:ed|ers?|ing)?|zerg(?:ed|s|ing)?|stealth(?:ed|ers?|ies)?|farmed|jumped|picked off|milegate|mg|albs|mids|hibs|"
    r"ran into a (?:grp|group)|caught (?:by|at|on) the bridge)\b")
# Monster words: a kill by monsters. A kill by an enemy player told so didn't happen (the log's own monster killers are
# added per log).
MONSTER_WORDS = re.compile(
    r"\b(?:guards? got me|the guards?|guards|frontier patrol|patrols?|mobs?|adds|the named|golems?|giants?|spiders?|wolf|wolves|"
    r"bears?|orcs?|skeletons?|zombies?|ghosts?|undead|serpents?|drakes?|dragons?|wyverns?|beasts?|monsters?|critters?)\b")
NUMBER_WORDS = {"one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10, "a couple": 2}
NUM = r"(\d+|one|two|three|four|five|six|seven|eight|nine|ten|a couple)"
# How many attackers a line claims: "2 on me", "x3", "20+", "like 8", "3 or 4", "4 v 1", "1 v 5", "a grp of 6", "full grp", a zerg.
CLAIMS = [
    (re.compile(rf"\b{NUM}\s*(?:on me|of (?:them|em)|guys|ppl|people|enemies|adds|at once)\b"), lambda m: m.group(1)),
    (re.compile(r"(?<![a-z0-9])x\s?(\d+)\b|\b(\d+)x\b"), lambda m: m.group(1) or m.group(2)),
    (re.compile(r"\b(\d+)\s*\+"), lambda m: m.group(1)),
    (re.compile(rf"\blike {NUM}\b"), lambda m: m.group(1)),
    (re.compile(r"\b(\d+) or (\d+)\b"), lambda m: m.group(1)),
    (re.compile(r"\b(\d+) ?vs? ?(\d+)\b"), lambda m: max(int(m.group(1)), int(m.group(2)))),
    (re.compile(rf"\b(?:grp|group) of {NUM}\b"), lambda m: m.group(1)),
    (re.compile(r"\b(?:full|whole) (?:grp|group|realm)\b|\bzerg(?:ed|s|ing)?\b"), lambda m: 8),
    (re.compile(r"\b(?:ran into|by|jumped by|caught by) (?:a|some) (?:grp|group)\b"), lambda m: 3),
]


def claimed_attackers(text: str) -> int | None:
    """The most attackers a line claims, or None."""
    t = text.lower()
    best = None
    for pattern, value in CLAIMS:
        for m in pattern.finditer(t):
            v = value(m)
            n = v if isinstance(v, int) else NUMBER_WORDS.get(v) if v in NUMBER_WORDS else int(v)
            if n is not None and n <= 200:
                best = n if best is None else max(best, n)
    return best


def monster_names(deaths: list[Death]) -> re.Pattern | None:
    """The log's monster killers ("elder beech", "lammia enchantress"), singular or plural, as one pattern."""
    names = sorted({d.killer.lower() for d in deaths if d.kind == "monster" and d.killer not in ("?", "")}, key=len, reverse=True)
    if not names:
        return None
    return re.compile(r"(?<![a-z0-9])(?:" + "|".join(re.escape(n) for n in names) + r")(?:e?s)?(?![a-z0-9])")


def death_stories(lines: list[Line]) -> list[tuple[str, list[Line]]]:
    """(teller, lines) for each /faction death story: a told story (src=story, role=teller) with all its exchange's lines
    (the riffs and the answers belong to it), and a field or ambient line that says the speaker died, with its exchange."""
    by_ex: dict[tuple[str, str], list[Line]] = defaultdict(list)
    for l in lines:
        if l.ch == "faction" and l.ex:
            by_ex[(l.realm, l.ex)].append(l)
    stories = []
    seen = set()
    for l in lines:
        if l.ch != "faction" or l.who != "bot":
            continue
        key = (l.realm, l.ex)
        story = l.src == "story" and l.role == "teller"
        field_death = l.src in ("field", "ambient") and (l.i in (0, None)) and DEATH_LINE.search(l.text.lower())
        if not (story or field_death):
            continue
        if l.ex:
            if key in seen:
                continue
            seen.add(key)
            stories.append((l.speaker, by_ex[key]))
        else:
            stories.append((l.speaker, [l]))
    return stories


# A grouped bot's death within this of groupmates' deaths in the same zone is one fight: a wipe is told for the group.
GROUP_FIGHT_S = 30


def fight_attackers(death: Death, deaths: list[Death]) -> int | None:
    """How many attackers a death story may claim: the bot's own, or for a grouped bot the fight's, the most on any of the
    bots of its realm that died in that zone within GROUP_FIGHT_S, or as many different killers as they had (RC10 on the
    owner's PC: a group of five wiped in Darkness Falls to a nightmare, a mahr and a crawler, and "pulled 3 at once" was
    true of the group though one crawler was on the teller)."""
    if death.attackers is None or death.group < 2 or not death.zone:
        return death.attackers
    fight = [d for d in deaths if d.realm == death.realm and d.zone == death.zone and d.group >= 2
             and abs(d.clock - death.clock) <= GROUP_FIGHT_S]
    most = max((d.attackers for d in fight if d.attackers is not None), default=death.attackers)
    return max(most, len({d.killer for d in fight}))


def check_death_stories(report: Report, lines: list[Line], deaths: list[Death]):
    by_bot: dict[tuple[str, str], list[Death]] = defaultdict(list)
    for d in deaths:
        by_bot[(d.realm, d.bot)].append(d)
    monsters = monster_names(deaths)
    for teller, seq in death_stories(lines):
        first = next(l for l in seq if l.speaker == teller)
        report.counts["death_stories"] += 1
        death = next((d for d in reversed(by_bot.get((first.realm, teller), []))
                      if first.clock - STORY_DEATH_WITHIN_S <= d.clock <= first.clock + 5), None)
        if death is None:
            if deaths:  # a log with no death rows at all (a simulated one) can't say
                report.flag("info_death_story_no_death", first,
                            note=f"no AUTONOMOUS_BOT_DEATH for {teller} in the {STORY_DEATH_WITHIN_S // 60} min before")
            continue
        fact = f"died {first.clock - death.clock}s before to {death.killer} ({death.kind}, attackers={death.attackers})"
        for l in seq:
            t = l.text.lower()
            if death.kind == "monster":
                hit = RVR_WORDS.search(t)
                if hit:
                    report.flag("death_story_rvr_for_monster", l, note=f'"{hit.group(0)}"; {teller} {fact}')
            else:
                hit = MONSTER_WORDS.search(t) or (monsters.search(t) if monsters else None)
                if hit:
                    report.flag("death_story_monster_for_rvr", l, note=f'"{hit.group(0)}"; {teller} {fact}')
            claimed = claimed_attackers(l.text)
            had = fight_attackers(death, deaths)
            if had is not None and had <= 2 and claimed is not None and claimed > had:
                note = f"claims {claimed}; {teller} {fact}" + (f", the fight {had}" if had != death.attackers else "")
                report.flag("death_story_attackers", l, note=note)


# 2. Time of day -----------------------------------------------------------------------------------------------------------
EVENING_WORDS = re.compile(r"\b(?:tonight|tonite|this evening)\b")
MORNING_WORDS = re.compile(r"\bthis morning\b")


def check_time_of_day(report: Report, lines: list[Line]):
    """"tonight" before 17:00 and "this morning" from 12:00 on, by the log's own clock."""
    for l in lines:
        if l.who != "bot":
            continue
        hour = l.clock % 86400 // 3600
        t = l.text.lower()
        if hour < 17 and EVENING_WORDS.search(t):
            report.flag("time_of_day_wrong", l, note=f"at {at(l)}")
        elif hour >= 12 and MORNING_WORDS.search(t):
            report.flag("time_of_day_wrong", l, note=f"at {at(l)}")


# 3. Internal vocabulary ----------------------------------------------------------------------------------------------------
INTERNAL_WORDS = re.compile(r"\b(?:rendezvous(?:es)?|objectives?|frontier patrols?|matchmaking|staging|egress|waypoints?|assignments?)\b")


def check_internal_words(report: Report, lines: list[Line]):
    """The server's own words (goal, group and route code) in a bot's mouth."""
    for l in lines:
        if l.who == "bot" and l.speaker not in SYSTEM_SPEAKERS:
            hit = INTERNAL_WORDS.search(l.text.lower())
            if hit:
                report.flag("internal_vocabulary", l, note=f'/{l.ch} "{hit.group(0)}"')


# 4 and 5. Answers inside an exchange ---------------------------------------------------------------------------------------
BARE_REACTION = {"oof", "rip", "lol", "ouch", "heh", "lol rip", "rip lol", "nice", "gz", "ugh", "haha", "ha", "lmao", "oof lol", "f"}
NOTHING_ANSWER = re.compile(r"\b(?:quiet|havent seen|haven t seen|not seen|dunno|nope|no idea|nobody|no one|noone|nothing|none)\b")
# The asker's referential follow-up to an answer ("how many?", "which way?"); "anyone else know?" asks again and is fine.
RUN_ON_QUESTION = re.compile(r"^(?:how many|how big|how far|how long|which|where|what|who|when|u sure|really)\b")
RUN_ON = re.compile(r"^(?:k |ok |kk )?(?:omw|on my way|otw|count me in|coming|heading over|be right there|inv me|x me)\b")


def exchanges_of(lines: list[Line]) -> dict[tuple[str, str, str], list[Line]]:
    out: dict[tuple[str, str, str], list[Line]] = defaultdict(list)
    for l in lines:
        if l.ex and l.i is not None:
            out[(l.ch, l.realm, l.ex)].append(l)
    for seq in out.values():
        seq.sort(key=lambda l: (l.i, l.n))
    return out


def check_exchange_answers(report: Report, lines: list[Line]):
    """A question whose answer is only a reaction ("rez in murdaigean?" / "oof"), and an exchange that runs on after its
    answer said there was nothing ("any grps out?" / "quiet atm" / "how many?" ... "omw")."""
    for (ch, realm, ex), seq in exchanges_of(lines).items():
        for k, line in enumerate(seq[:-1]):
            if not line.text.rstrip().endswith("?") or line.role in ("riff", "reactor") or RHETORICAL.match(norm(line.text)):
                continue
            reply = next((r for r in seq[k + 1:] if r.speaker != line.speaker), None)
            if reply is not None and reply.role == "answerer" and norm(reply.text) in BARE_REACTION:
                report.flag("question_bare_reaction", line, reply, note=f"/{ch} ex={ex}")
        asker = next((l.speaker for l in seq if l.role == "asker"), seq[0].speaker)
        for k, answer in enumerate(seq):
            if answer.role != "answerer" or answer.speaker == asker or not NOTHING_ANSWER.search(norm(answer.text)):
                continue
            later = [l for l in seq[k + 1:] if RUN_ON.match(norm(l.text)) or (
                l.speaker == asker and l.text.rstrip().endswith("?") and RUN_ON_QUESTION.match(norm(l.text)) and len(words(l.text)) <= 5)]
            if later:
                report.flag("run_on_after_nothing", answer, *later, note=f"/{ch} ex={ex}")
            break


# 6, 7. The group channel ---------------------------------------------------------------------------------------------------
GROUP_REPEAT_S = 3
HUNT_TALK = re.compile(r"\b(?:pulls?|pulling|pulled|camp|xp|drops?|dropping|dropped|loot|kills?|killing|oom|med|buffs?)\b")
HUNT_BURST_LINES = 6
HUNT_BURST_S = 600


def check_group_channel(report: Report, lines: list[Line]):
    """One bot twice in /group within 3 s; and the group banter (TryGroupLine) that talks of hunting: counted, and more than
    six such lines in ten minutes in one group is a failure (too chatty for a duo). The log shows the /group lines the player
    sees, so a realm's lines are the player's group."""
    group = [l for l in lines if l.ch == "group" and l.who == "bot"]
    last: dict[tuple[str, str], Line] = {}
    for l in group:
        prev = last.get((l.realm, l.speaker))
        if prev is not None and l.clock - prev.clock <= GROUP_REPEAT_S:
            report.flag("group_same_bot_3s", prev, l, note=f"{l.clock - prev.clock}s apart")
        last[(l.realm, l.speaker)] = l
    hunt: dict[str, list[Line]] = defaultdict(list)
    for l in group:
        if "TryGroupLine" in l.via and HUNT_TALK.search(l.text.lower()):
            report.flag("info_group_hunt_talk", l)
            hunt[l.realm].append(l)
    for realm, seq in hunt.items():
        lo = 0
        bursting = False
        for hi, l in enumerate(seq):
            while seq[lo].clock <= l.clock - HUNT_BURST_S:
                lo += 1
            window = seq[lo:hi + 1]
            if len(window) > HUNT_BURST_LINES and not bursting:
                bursting = True
                start = window[0]
                end = next((x for x in reversed(seq) if x.clock - start.clock < HUNT_BURST_S), l)
                n = sum(1 for x in seq if start.clock <= x.clock < start.clock + HUNT_BURST_S)
                report.flag("group_hunt_talk_burst", start, end,
                            note=f"{n} hunting lines from {at(start)} within {HUNT_BURST_S // 60} min ({', '.join(sorted({x.speaker for x in window}))})")
            elif len(window) <= HUNT_BURST_LINES:
                bursting = False


# 8. "back" from nowhere ------------------------------------------------------------------------------------------------
BACK_LINE = re.compile(r"^(?:ok |k |kk |and )?(?:back|re|im back|i m back|back now|bk|back again)$")
AWAY_LINE = re.compile(r"\b(?:brb|afk|bio|bbiab|bbl|away|phone|door|dinner|food|shall return|be right back|be back|gtg|g2g|sec|smoke|kids?)\b")
BACK_WITHIN_S = 1800


def check_back_lines(report: Report, lines: list[Line], deaths: list[Death]):
    """A bot's /faction "back" ("ok back", "re") with no brb/afk/away line of its own and no AUTONOMOUS_BOT_DEATH in the 30 min
    before. A log with no AUTONOMOUS_BOT_DEATH rows (a simulated one) can't say whether the bot died, so it isn't checked."""
    if not deaths:
        return
    for k, l in enumerate(lines):
        if l.who != "bot" or l.ch != "faction" or not BACK_LINE.match(norm(l.text)):
            continue
        away = any(p.speaker == l.speaker and p.realm == l.realm and l.clock - p.clock <= BACK_WITHIN_S and AWAY_LINE.search(norm(p.text))
                   for p in lines[:k])
        died = any(d.bot == l.speaker and d.realm == l.realm and 0 <= l.clock - d.clock <= BACK_WITHIN_S for d in deaths)
        if not away and not died:
            report.flag("back_from_nowhere", l, note=f"no brb/afk line or death in the {BACK_WITHIN_S // 60} min before")


# 9. A duo offer, then declined for a duty (informational) ---------------------------------------------------------------
DUO_OFFER = re.compile(r"\b(?:solo atm|want company|wanna duo|want to duo|wanna team up|want to team up|lf duo|up for a duo)\b")
OFFER_DECLINED_WITHIN_S = 600


def check_offer_declined(report: Report, lines: list[Line], invites: list[InviteResult]):
    """A bot's tell offering the player company, then the bot declining the player's invite within 10 min for a duty."""
    for l in lines:
        if l.who != "bot" or l.ch != "tell" or not DUO_OFFER.search(l.text.lower()):
            continue
        to = TO_FIELD.search(l.raw)
        declined = next((r for r in invites if r.bot == l.speaker and r.result == "declined" and 0 <= r.clock - l.clock <= OFFER_DECLINED_WITHIN_S
                         and (to is None or r.player == to.group(1))), None)
        if declined:
            report.flag("info_offer_then_declined", l, note=f"declined {declined.clock - l.clock}s later, duty={declined.duty or '?'}")


def check_story_facts(report: Report, lines: list[Line], path: str):
    deaths, invites = read_facts(path)
    report.counts["info_deaths_logged"] += len(deaths)
    check_death_stories(report, lines, deaths)
    check_time_of_day(report, lines)
    check_internal_words(report, lines)
    check_exchange_answers(report, lines)
    check_group_channel(report, lines)
    check_back_lines(report, lines, deaths)
    check_offer_declined(report, lines, invites)


STORY_FACT_KINDS = [
    ("info_deaths_logged", "(info) AUTONOMOUS_BOT_DEATH rows in the log"),
    ("death_stories", "/faction death stories (told stories and field death lines)"),
    ("death_story_rvr_for_monster", "death story lines with RvR words for a monster kill"),
    ("death_story_monster_for_rvr", "death story lines naming monsters or guards for a player/bot kill"),
    ("death_story_attackers", "death story lines claiming more attackers than there were (1-2)"),
    ("info_death_story_no_death", "(info) death stories with no death of the teller in the 10 min before"),
    ("time_of_day_wrong", "\"tonight\" before 17:00 or \"this morning\" after 12:00"),
    ("internal_vocabulary", "bot lines with internal words (rendezvous, objective, staging...)"),
    ("question_bare_reaction", "questions answered by a bare reaction (oof, rip, lol...)"),
    ("run_on_after_nothing", "exchanges that run on after a nothing answer (quiet, dunno...)"),
    ("group_same_bot_3s", "one bot twice in /group within 3 s"),
    ("info_group_hunt_talk", "(info) /group banter lines with hunting talk (TryGroupLine)"),
    ("group_hunt_talk_burst", f"/group hunting banter over {HUNT_BURST_LINES} lines in {HUNT_BURST_S // 60} min"),
    ("back_from_nowhere", "\"back\" with no brb/afk or death in the 30 min before (logs with death rows)"),
    ("info_offer_then_declined", "(info) duo offers by tell, then the invite declined for a duty"),
]


def story_fact_checks(counts) -> list[tuple[str, int]]:
    """The RC6 checks the verdict fails on."""
    return [
        ("death story contradicts the death", counts["death_story_rvr_for_monster"] + counts["death_story_monster_for_rvr"] + counts["death_story_attackers"]),
        ("wrong time of day", counts["time_of_day_wrong"]),
        ("internal vocabulary", counts["internal_vocabulary"]),
        ("questions answered by a bare reaction", counts["question_bare_reaction"]),
        ("run-on after a nothing answer", counts["run_on_after_nothing"]),
        ("same bot twice in /group within 3 s", counts["group_same_bot_3s"]),
        ("/group hunting banter bursts", counts["group_hunt_talk_burst"]),
        ("back from nowhere", counts["back_from_nowhere"]),
    ]


KINDS.extend(STORY_FACT_KINDS)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--json", help="write the summaries as JSON here")
    parser.add_argument("--channel", help="only this channel's dialogue checks (lines/min cover all)")
    parser.add_argument("--show", type=int, default=0, help="examples per check")
    parser.add_argument("--github", action="store_true", help="also print one GitHub Actions notice per log")
    parser.add_argument("--ceiling", type=float, help="/faction ceiling in lines a minute, for logs without cap= fields")
    parser.add_argument("--say-ceiling", type=float, help="/say ceiling in lines a minute, for logs whose /say lines have no cap=")
    args = parser.parse_args(argv)
    summaries = []
    for path in args.logs:
        summary = summarize(audit(path, args.channel, args.ceiling, args.say_ceiling))
        passed, reasons = verdict(summary)
        summary["verdict"] = "PASS" if passed else "FAIL"
        summary["verdict_reasons"] = reasons
        summary["engagement"] = engagement(path)
        summaries.append(summary)
        print_report(summary, args.show)
        print_engagement(summary["engagement"], args.show)
        if args.github:
            c = summary["counts"]
            name = summary["log"].replace("\\", "/").rsplit("/", 1)[-1]
            print(f"::notice title=Chat audit {name}::{summary['lines']} lines over {summary['minutes']} min "
                  f"({summary['lines_per_minute'].get('faction', 0)}/min faction); out of order {c['exact_out_of_order']}, "
                  f"role swaps {c['exact_role_swap']}, false role answers {c['role_answer_false']}, false class {c['class_claim_false']}, "
                  f"false level {c['level_claim_false']}, thanks without answer {c['thanks_without_answer']}, follow-up before answer "
                  f"{c['followup_before_answer']}, pile-ons {c['pileon']} (+{c['pileon_lines_over_two']} lines), late reactions {c['reaction_late']}, "
                  f"no-trigger reactions {c['reaction_no_trigger']}, repeated reactions {c['reaction_repeat_10min']}, "
                  f"silent features {c['features_silent']} of {c['features_due']} due")
    for summary in summaries:
        name = summary["log"].replace("\\", "/").rsplit("/", 1)[-1]
        print(f"VERDICT {summary['verdict']}: {name}" + (f" ({'; '.join(summary['verdict_reasons'])})" if summary["verdict_reasons"] else ""))
        if args.github:
            print(f"::notice title=Chat audit verdict {name}::{summary['verdict']}" +
                  (f" ({'; '.join(summary['verdict_reasons'])})" if summary["verdict_reasons"] else ""))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(summaries, handle, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
