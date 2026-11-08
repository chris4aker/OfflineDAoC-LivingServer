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
    raw: str = ""

    @property
    def stream(self) -> tuple[str, str]:
        return (self.ch, self.realm)


def parse_stamp(stamp: str) -> int:
    month, rest = stamp.split("/", 1)
    day, clock = rest.split(" ", 1)
    h, m, s = (int(x) for x in clock.split(":"))
    return ((int(month) * 31 + int(day)) * 24 + h) * 3600 + m * 60 + s


def read(path: str) -> list[Line]:
    lines: list[Line] = []
    first = None
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            match = ROW.match(raw.rstrip("\n"))
            if not match:
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
            lines.append(Line(len(lines), stamp - first, fields.get("who", "bot"), fields.get("ch", "?"), fields.get("realm", "?"),
                              fields.get("from", "?"), text, fields.get("via", ""), fields.get("src", ""), fields.get("ex", ""),
                              int(index) if index and index.lstrip("-").isdigit() else None, fields.get("role", ""), fields.get("cls", ""),
                              int(lvl) if lvl and lvl.isdigit() else None, raw.rstrip("\n")))
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
    for k, token in enumerate(tokens):
        cls = WORD_CLASS.get(token)
        if not cls:
            continue  # plurals ("any reavers on?") ask about a class, they don't claim one
        before = tokens[k - 1] if k > 0 else ""
        after = tokens[k + 1] if k + 1 < len(tokens) else ""
        number = int(before) if before.isdigit() else int(after) if after.isdigit() else None
        if number is not None and not 1 <= number <= 50:
            number = None
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
    examples: dict = field(default_factory=lambda: defaultdict(list))

    def flag(self, kind: str, *lines: Line, note: str = ""):
        self.counts[kind] += 1
        if len(self.examples[kind]) < 12:
            self.examples[kind].append(" | ".join(f"{l.t // 60:>3}:{l.t % 60:02d} {l.speaker}: {l.text}" for l in lines) + (f"  [{note}]" if note else ""))


def audit(path: str, channel_filter: str | None = None) -> Report:
    lines = read(path)
    report = Report(path, len(lines))
    if not lines:
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
            # A tagged reaction reacts to its own exchange's first line.
            first = next((p for p in seq[max(0, k - 60):k] if p.ex == line.ex and p.speaker != line.speaker), None)
            if first is not None:
                reactions_to[first.n].append(line) if line.src in ("burst", "reaction") else None
                if line.t - first.t > 45:
                    report.flag("reaction_late", first, line, note=f"{line.t - first.t}s")
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
        if len(reacting) > 2:
            report.counts["pileon_lines_over_two"] += len(reacting) - 2
            trig = next(l for l in seq if l.n == trigger_n)
            report.flag("pileon", trig, *reacting, note=f"{len(reacting)} reactions")


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
                if not JOIN_ACK.match(t):
                    continue
                cls = reply.cls.lower() if reply.cls else None
                if cls is None:
                    # The class the speaker names in this line or its next line within 20 s.
                    near = [reply] + [n for n in seq[seq.index(reply) + 1:seq.index(reply) + 6] if n.speaker == reply.speaker and n.t - reply.t <= 20]
                    found = [c for n in near for c, _ in class_claims(n.text)]
                    cls = found[0] if found else None
                if cls and cls not in ROLE_CLASSES.get(role, {cls}):
                    report.flag("role_answer_false", line, reply, note=f"{cls} for {role}")
                break


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
    ("reaction_no_trigger", "reaction with no trigger in 30 s"),
    ("reaction_late", "reaction more than 30 s after its trigger"),
    ("pileon", "triggers with more than two reactions"),
    ("pileon_lines_over_two", "reaction lines past the second"),
    ("reaction_repeat_10min", "same reaction again within 10 min"),
    ("short_line_repeat_10min", "same short line (<=3 words) again within 10 min"),
]


def summarize(report: Report) -> dict:
    per_hour = 60.0 / report.minutes if report.minutes else 0
    return {
        "log": report.path, "lines": report.lines, "minutes": round(report.minutes, 1), "tagged_lines": report.tagged,
        "lines_per_minute": report.per_minute, "busiest_minute": report.busiest_minute,
        "reactions": report.counts["reactions"], "exchanges": report.counts["exchanges"], "exchanges_multi_line": report.counts["exchanges_multi_line"],
        "counts": {k: report.counts[k] for k, _ in KINDS},
        "per_hour": {k: round(report.counts[k] * per_hour, 1) for k, _ in KINDS},
        "examples": {k: v for k, v in report.examples.items()},
    }


def print_report(summary: dict, show: int):
    print(f"== {summary['log']}")
    print(f"   {summary['lines']} lines over {summary['minutes']} min; {summary['tagged_lines']} tagged with an exchange; "
          f"{summary['exchanges']} exchanges ({summary['exchanges_multi_line']} multi-line); {summary['reactions']} reactions")
    print("   lines/min: " + ", ".join(f"{ch} {rate}" for ch, rate in summary["lines_per_minute"].items()) +
          "   busiest minute: " + ", ".join(f"{ch} {n}" for ch, n in summary["busiest_minute"].items()))
    for key, label in KINDS:
        print(f"   {summary['counts'][key]:>5}  ({summary['per_hour'][key]:>6}/h)  {label}")
    if show:
        for key, label in KINDS:
            for example in summary["examples"].get(key, [])[:show]:
                print(f"      [{key}] {example}")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--json", help="write the summaries as JSON here")
    parser.add_argument("--channel", help="only this channel's dialogue checks (lines/min cover all)")
    parser.add_argument("--show", type=int, default=0, help="examples per check")
    parser.add_argument("--github", action="store_true", help="also print one GitHub Actions notice per log")
    args = parser.parse_args(argv)
    summaries = []
    for path in args.logs:
        summary = summarize(audit(path, args.channel))
        summaries.append(summary)
        print_report(summary, args.show)
        if args.github:
            c = summary["counts"]
            name = summary["log"].replace("\\", "/").rsplit("/", 1)[-1]
            print(f"::notice title=Chat audit {name}::{summary['lines']} lines over {summary['minutes']} min "
                  f"({summary['lines_per_minute'].get('faction', 0)}/min faction); out of order {c['exact_out_of_order']}, "
                  f"role swaps {c['exact_role_swap']}, false role answers {c['role_answer_false']}, false class {c['class_claim_false']}, "
                  f"false level {c['level_claim_false']}, thanks without answer {c['thanks_without_answer']}, follow-up before answer "
                  f"{c['followup_before_answer']}, pile-ons {c['pileon']} (+{c['pileon_lines_over_two']} lines), late reactions {c['reaction_late']}, "
                  f"no-trigger reactions {c['reaction_no_trigger']}, repeated reactions {c['reaction_repeat_10min']}")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(summaries, handle, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
