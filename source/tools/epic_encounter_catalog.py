#!/usr/bin/env python3
"""Epic encounter catalog: what every scripted boss in namedmobs/ does, read from its script.

Step 1 of "bots play epic encounters properly". It changes no gameplay: it reads the boss scripts under
source/server/GameServer/scripts/namedmobs/ and the raid definitions, and writes

    docs/encounters/epic-encounters.json   one entry per encounter, each rule with its file, line and confidence
    docs/encounters/EPIC-ENCOUNTERS.md     the same for people, priority list first

Hand-checked facts (verified rules, corrected summaries, tactics, difficulty, the priority list, entries for scripts the
parser can't read) live in docs/encounters/hand-notes.json and are merged in. A hand note records the hash of the
script it was checked against; when the script changes the note no longer counts as verified until it is checked again.

    python3 source/tools/epic_encounter_catalog.py           regenerate both files
    python3 source/tools/epic_encounter_catalog.py --check   exit 1 if either file differs from a fresh run (CI)

The parser is a pattern reader, not a C# compiler: every rule it finds says whether it was read directly ("read": a
literal value or an exact call) or inferred ("inferred": the meaning of a condition, a count, a trigger).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SCRIPTS = Path("source/server/GameServer/scripts/namedmobs")
RAID_FILE = Path("source/server/GameServer/bots/autonomous/AutonomousRealmRaid.cs")
NEUTRAL_FILE = Path("source/server/GameServer/bots/autonomous/RealmRaidNeutralEvents.cs")
ROUTE_FILE = Path("source/server/GameServer/bots/autonomous/RealmRaidDungeonRoute.cs")
SWITCH_FILE = SCRIPTS / "EpicEncounterMechanics.cs"
OUT_JSON = Path("docs/encounters/epic-encounters.json")
OUT_MD = Path("docs/encounters/EPIC-ENCOUNTERS.md")
HAND_NOTES = Path("docs/encounters/hand-notes.json")
WORLD_SNAPSHOT = Path("docs/encounters/world-spawns.json")
TACTICS_CS = Path("source/server/GameServer/bots/autonomous/EpicEncounterTactics.Catalog.cs")

SCHEMA = 1

# Folder -> (dungeon, region). Regions are the ones the raid code uses (AutonomousRealmRaid.cs); the rest are read
# from the scripts' own MoveTo/CurrentRegionID values when they name one.
FOLDERS = {
    "CaerSidi": ("Caer Sidi", 60),
    "TuscarianGlacier": ("Tuscaran Glacier", 160),
    "Galladoria": ("Galladoria", 191),
    "Krondon": ("Krondon", 61),
    "AvalonCity/CrystalCave": ("Crystal Cave", 62),
    "AvalonCity": ("Avalon City", 50),
    "HallOfTheCorrupt": ("Hall of the Corrupt", 277),
    "Trollheim": ("Trollheim", 150),
    "IarnvidiurLair": ("Iarnvidiur's Lair", 161),
    "Dodens": ("Dodens Gruva", 246),
    "TurSuil": ("Tur Suil", 190),
    "MarfachCaverns": ("Marfach Caverns", 276),
    "SummonersHall": ("Summoner's Hall", 248),
    "DarknessFalls": ("Darkness Falls", 249),
    "Fomor": ("Fomor City", None),
    "Dragons": ("Dragon lairs", None),
}
DRAGON_REGIONS = {"AlbGolestandt": 1, "MidGjalpinulva": 100, "HibCuuldurach": 200}

RULE_KINDS = {
    "immune_while": "Takes no damage while a condition holds (adds alive, drawn past its tether, attacker position).",
    "damage_reduced": "Damage it takes is scaled down or redirected.",
    "reflect": "Hits back at whoever damages it (damage shield, punishing spell on the attacker).",
    "adds": "Spawns adds: when, how many, which class and brain.",
    "phase": "Does something new at a health threshold.",
    "tether": "Leash, tether or reset distance.",
    "reset": "Resets (walks home at full health, adds cleared) when nobody is on its aggro list.",
    "teleport": "Moves someone: air toss, jail, throw to a fixed spot, or teleports itself.",
    "position_puzzle": "Needs raiders in particular places (split, stand-in/stand-out), or a scripted placement puzzle.",
    "empowered": "Gets stronger (a stat raised) while a condition holds: the period's 'enrage'.",
    "enrage_timer": "Gets stronger after a fixed fight time.",
    "pet_devour": "Eats or kills pets near it and heals.",
    "aggro_wipe": "Clears its aggro list in the fight (the tank loses it).",
    "linked_pack": "Pulls other monsters into the fight with it (bring-a-friend by package or brain).",
    "heal": "Heals itself or another encounter.",
    "hazard": "An untargetable, immobile or invulnerable piece of the room that hurts raiders.",
    "attacker_filter": "Only some attackers can damage it at all.",
    "offline_change": "The original mechanic was edited for offline bot raids (a comment in the script says so); no switch "
                      "brings it back.",
}

DIFFICULTY = ("easy for the current raid AI", "needs new raid AI", "unsafe for bots today", "off today")

# ---------------------------------------------------------------------------------------------------------------------
# Reading C#
# ---------------------------------------------------------------------------------------------------------------------


def read_text(path: Path) -> str:
    raw = path.read_bytes()
    text = raw.decode("utf-8-sig", errors="replace")
    return text.replace("\r\n", "\n").replace("\r", "\n")


def script_hash(path: Path) -> str:
    """SHA-256 of the script with line endings and BOM normalised (a Windows checkout gives the same hash)."""
    return hashlib.sha256(read_text(path).encode("utf-8")).hexdigest()


def mask(text: str) -> str:
    """Comments and string/char literal contents replaced by spaces, same length and newlines kept."""
    out = list(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = " "
            i = j
        elif c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if out[k] != "\n":
                    out[k] = " "
            i = j
        elif c == '"' or (c in "@$" and nxt == '"') or (c in "@$" and nxt in "@$" and i + 2 < n and text[i + 2] == '"'):
            verbatim = False
            start = i
            while text[i] in "@$":
                verbatim = verbatim or text[i] == "@"
                i += 1
            i += 1  # opening quote
            while i < n:
                if verbatim and text[i] == '"' and i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                if not verbatim and text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    break
                if text[i] == "\n" and not verbatim:
                    break
                i += 1
            for k in range(start + 1, min(i, n)):
                if out[k] != "\n":
                    out[k] = " "
            i += 1
        elif c == "'":
            j = i + 1
            if j < n and text[j] == "\\":
                j += 2
            else:
                j += 1
            if j < n and text[j] == "'":
                for k in range(i + 1, j):
                    out[k] = " "
                i = j + 1
            else:
                i += 1
        else:
            i += 1
    return "".join(out)


def match_brace(masked: str, open_pos: int) -> int:
    depth = 0
    for i in range(open_pos, len(masked)):
        ch = masked[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i
    return len(masked) - 1


class Source:
    def __init__(self, rel: Path):
        self.rel = rel
        self.text = read_text(REPO / rel)
        self.masked = mask(self.text)
        self.line_starts = [0]
        for i, ch in enumerate(self.text):
            if ch == "\n":
                self.line_starts.append(i + 1)
        self.lines = self.text.split("\n")
        self.hash = hashlib.sha256(self.text.encode("utf-8")).hexdigest()

    def line_of(self, pos: int) -> int:
        lo, hi = 0, len(self.line_starts) - 1
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if self.line_starts[mid] <= pos:
                lo = mid
            else:
                hi = mid - 1
        return lo + 1

    def line(self, number: int) -> str:
        return self.lines[number - 1] if 0 < number <= len(self.lines) else ""


CLASS_RE = re.compile(r"\bclass\s+(\w+)\s*(?:<[^>{]*>)?\s*(?::\s*([^{]+?))?\s*\{")
METHOD_NAME_RE = re.compile(r"(\w+)\s*\([^()]*(?:\([^()]*\)[^()]*)*\)\s*(?::\s*(?:base|this)\s*\([^)]*\))?\s*$", re.S)
EXPR_MEMBER_RE = re.compile(r"\boverride\s+[\w<>\.]+\s+(\w+)\s*=>\s*([^;]+);")


class Member:
    def __init__(self, cls: "CsClass", name: str, header: str, start: int, open_pos: int, end: int, is_method: bool):
        self.cls, self.name, self.header = cls, name, header
        self.start, self.open_pos, self.end = start, open_pos, end
        self.is_method = is_method
        src = cls.src
        self.first_line = src.line_of(start)
        self.last_line = src.line_of(end)
        self._blocks = None

    @property
    def body(self) -> str:
        return self.cls.src.text[self.open_pos:self.end + 1]

    @property
    def mbody(self) -> str:
        return self.cls.src.masked[self.open_pos:self.end + 1]

    def blocks(self):
        """(open, close, header) of every brace block inside this member, positions in the file."""
        if self._blocks is None:
            m = self.cls.src.masked
            res, stack = [], []
            boundary = self.open_pos
            paren = 0
            for i in range(self.open_pos + 1, self.end):
                ch = m[i]
                if ch == "(":
                    paren += 1
                elif ch == ")":
                    paren = max(0, paren - 1)
                elif ch == "{":
                    header = m[boundary + 1:i].strip()
                    stack.append((i, header))
                    boundary = i
                elif ch == "}":
                    if stack:
                        o, h = stack.pop()
                        res.append((o, i, h))
                    boundary = i
                elif ch == ";" and paren == 0:
                    boundary = i
            self._blocks = res
        return self._blocks

    def conditions_at(self, pos: int) -> list[str]:
        """Conditions of the if/else-if/while/for blocks enclosing pos, outermost first, plus a brace-less if on the
        line itself or the line before."""
        src = self.cls.src
        conds = []
        blocks = sorted(self.blocks(), key=lambda b: b[0])
        for o, c, h in blocks:
            if o < pos < c:
                hh = " ".join(h.split())
                if hh == "else":
                    # The negation of the if just before it.
                    prev = [b for b in blocks if b[1] < o and not any(b2[0] < b[0] and b[1] < b2[1] and b2[0] > self.open_pos
                                                                       and b2[1] < o and b2[0] < b[0] for b2 in [])]
                    prev = [b for b in prev if src.masked[b[1] + 1:o].strip() == "else"]
                    if prev:
                        ph = " ".join(prev[-1][2].split())
                        ph = re.sub(r"^(else\s+)?if\s*", "", ph)
                        conds.append(f"else (not {ph})")
                        continue
                hh = re.sub(r"^.*?\b(else\s+if|if|while|for|foreach|case[^:]*:|else)\b", r"\1", hh) if re.search(
                    r"\b(if|while|for|foreach|else)\b", hh) else hh
                conds.append(hh)
        line_no = src.line_of(pos)
        for ln in (line_no - 1, line_no):
            mtext = src.masked[src.line_starts[ln - 1]:src.line_starts[ln] if ln < len(src.line_starts) else len(src.masked)]
            if ln == line_no:
                mtext = mtext[:pos - src.line_starts[ln - 1]]
            for mm in re.finditer(r"\bif\s*\(", mtext):
                cond = balanced_paren(mtext, mm.end() - 1)
                if cond is not None and (ln == line_no or not mtext.rstrip().endswith("{")):
                    if ln == line_no or re.match(r"^\s*(else\s+)?if\s*\(", mtext) and not mtext.rstrip().endswith(";"):
                        conds.append("if (" + " ".join(cond.split()) + ")")
        return conds


def balanced_paren(text: str, open_pos: int):
    depth = 0
    for i in range(open_pos, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return text[open_pos + 1:i]
    return None


class CsClass:
    def __init__(self, src: Source, name: str, bases: str, start: int, open_pos: int, end: int):
        self.src, self.name = src, name
        self.bases = [b.strip().split("<")[0].split(".")[-1] for b in (bases or "").split(",") if b.strip()]
        self.base = self.bases[0] if self.bases else ""
        self.start, self.open_pos, self.end = start, open_pos, end
        self.first_line, self.last_line = src.line_of(start), src.line_of(end)
        self.members: list[Member] = []
        self.expr_members: dict[str, tuple[str, int]] = {}
        self._parse_members()

    def _parse_members(self):
        m = self.src.masked
        depth = 0
        boundary = self.open_pos
        i = self.open_pos + 1
        while i < self.end:
            ch = m[i]
            if ch == "{":
                header = m[boundary + 1:i]
                close = match_brace(m, i)
                hs = " ".join(header.split())
                is_class = re.search(r"\b(class|struct|enum|interface)\b", hs)
                if not is_class:
                    mm = METHOD_NAME_RE.search(hs)
                    if mm and "=" not in hs.split("(")[0]:
                        self.members.append(Member(self, mm.group(1), hs, boundary + 1, i, close, True))
                    elif "=" not in hs and "(" not in hs and hs:
                        name = hs.split()[-1]
                        self.members.append(Member(self, name, hs, boundary + 1, i, close, False))
                i = close + 1
                boundary = close
                continue
            if ch == ";":
                boundary = i
            elif ch == "}":
                boundary = i
            i += 1
        body = m[self.open_pos:self.end]
        for mm in EXPR_MEMBER_RE.finditer(body):
            self.expr_members[mm.group(1)] = (mm.group(2).strip(), self.src.line_of(self.open_pos + mm.start()))

    def member(self, name: str):
        return [mb for mb in self.members if mb.name == name]


def parse_classes(src: Source) -> list[CsClass]:
    classes = []
    pos = 0
    while True:
        mm = CLASS_RE.search(src.masked, pos)
        if not mm:
            break
        open_pos = mm.end() - 1
        close = match_brace(src.masked, open_pos)
        classes.append(CsClass(src, mm.group(1), mm.group(2), mm.start(), open_pos, close))
        # Nested classes are rare in these scripts; continue after the header so they are found too.
        pos = open_pos + 1
    return classes


# ---------------------------------------------------------------------------------------------------------------------
# The world of classes
# ---------------------------------------------------------------------------------------------------------------------

NPC_ROOTS = {"GameNPC", "GameEpicNPC", "GameEpicBoss", "GameEpicAros", "GameDragon", "GameSummonedPet"}
BRAIN_ROOTS = {"StandardMobBrain", "EpicBossBrain", "APlayerVicinityBrain", "DragonEncounterAddBrain", "ABrain",
               "ControlledMobBrain"}


class World:
    def __init__(self):
        self.sources: dict[Path, Source] = {}
        self.classes: dict[str, CsClass] = {}
        self.duplicates: dict[str, list[CsClass]] = defaultdict(list)
        for path in sorted((REPO / SCRIPTS).rglob("*.cs")):
            rel = path.relative_to(REPO)
            src = Source(rel)
            self.sources[rel] = src
            for cls in parse_classes(src):
                if cls.name in self.classes:
                    self.duplicates[cls.name].append(cls)
                else:
                    self.classes[cls.name] = cls

    def root_of(self, name: str, seen=None) -> str:
        seen = seen or set()
        cls = self.classes.get(name)
        if cls is None or name in seen:
            return name
        seen.add(name)
        if cls.base in NPC_ROOTS | BRAIN_ROOTS:
            return cls.base if cls.base not in self.classes else self.root_of(cls.base, seen)
        return self.root_of(cls.base, seen) if cls.base else name

    def is_npc(self, name: str) -> bool:
        return self.root_of(name) in NPC_ROOTS or name in NPC_ROOTS

    def is_brain(self, name: str) -> bool:
        return self.root_of(name) in BRAIN_ROOTS

    def is_boss(self, name: str) -> bool:
        cls = self.classes.get(name)
        if cls is None:
            return False
        chain, cur = [], cls
        while cur is not None and cur.name not in chain:
            chain.append(cur.name)
            if cur.base in ("GameEpicBoss", "GameEpicAros"):
                return True
            cur = self.classes.get(cur.base)
        return False

    def is_epic(self, name: str) -> bool:
        """IGameEpicNpc: what the raid route treats as a named encounter."""
        return self.is_boss(name) or self.root_of(name) == "GameEpicNPC" or (
            name in self.classes and self.classes[name].base == "GameEpicNPC")


NEW_RE = re.compile(r"\bnew\s+([A-Z]\w*)\s*\(")
BRAIN_ASSIGN_RE = re.compile(r"SetOwnBrain\s*\(\s*(?:new\s+(\w+)\s*\(|(\w+)\s*\))")


def brain_of(world: World, cls: CsClass) -> str | None:
    for mb in cls.member("AddToWorld") + cls.members:
        body = mb.mbody
        for mm in BRAIN_ASSIGN_RE.finditer(body):
            if mm.group(1) and world.is_brain(mm.group(1)):
                return mm.group(1)
            var = mm.group(2)
            if var:
                decl = re.search(r"\b(\w+)\s+" + re.escape(var) + r"\s*=\s*new\s+(\w+)", body)
                if decl and world.is_brain(decl.group(2)):
                    return decl.group(2)
                decl = re.search(r"\b" + re.escape(var) + r"\s*=\s*new\s+(\w+)", body)
                if decl and world.is_brain(decl.group(1)):
                    return decl.group(1)
    return None


def literal(cls: CsClass, prop: str) -> tuple[str, int] | None:
    """A property set in AddToWorld (Name = "x"; Level = 81;) or returned by its getter (MaxHealth)."""
    src = cls.src
    for mb in cls.member("AddToWorld"):
        for mm in re.finditer(r"(?<![\w])(?:this\.)?(?<!\.)" + prop + r"\s*=\s*([^;]+);", mb.body):
            pos = mb.open_pos + mm.start()
            return mm.group(1).strip(), src.line_of(pos)
    for mb in cls.member(prop):
        mm = re.search(r"return\s+([^;]+);", mb.body)
        if mm:
            return mm.group(1).strip(), src.line_of(mb.open_pos + mm.start())
    if prop in cls.expr_members:
        return cls.expr_members[prop]
    return None


def int_or_none(value: str | None):
    if value is None:
        return None
    v = value.strip().strip("()").replace("(byte)", "").replace("(ushort)", "").strip()
    return int(v) if re.fullmatch(r"-?\d+", v) else None


# ---------------------------------------------------------------------------------------------------------------------
# Rule detectors
# ---------------------------------------------------------------------------------------------------------------------

SWITCH_RE = re.compile(r"EpicEncounterMechanics\.(Allow\w+|PetDevourIntervalMilliseconds|FightIsOver)")
ALL_DAMAGE = {"Body", "Cold", "Energy", "Heat", "Matter", "Spirit", "Crush", "Thrust", "Slash"}


class Rule(dict):
    pass


def rel(src: Source) -> str:
    return src.rel.as_posix()


def make_rule(kind, src: Source, line, line_end, summary, confidence, owner: CsClass, member: Member | None, **extra):
    anchor = src.line(line).strip()
    r = Rule(kind=kind, summary=summary, file=rel(src), line=line, lineEnd=max(line, line_end), anchor=anchor[:160],
             confidence=confidence, sourceClass=owner.name, method=member.name if member else None)
    r.update({k: v for k, v in extra.items() if v is not None})
    return r


def switches_for(member: Member, pos: int, world_ctx) -> list[str]:
    """EpicEncounterMechanics switches that gate the code at pos: on its line, in its enclosing conditions, in an early
    return at the top of its method, or at the place that starts its timer."""
    src = member.cls.src
    found = []
    texts = [src.masked[src.line_starts[src.line_of(pos) - 1]:pos + 200].split("\n")[0]] + member.conditions_at(pos)
    body = member.body
    early = re.search(r"if\s*\(\s*!\s*(?:DOL\.GS\.)?EpicEncounterMechanics\.(Allow\w+)\s*\)\s*return", body)
    if early and member.open_pos + early.start() < pos:
        found.append(early.group(1))
    # if (!Allow...) { ...; return; } before pos: the code after it runs only with the switch on.
    for blk in re.finditer(r"if\s*\(\s*!\s*(?:DOL\.GS\.)?EpicEncounterMechanics\.(Allow\w+)\s*\)\s*\{[^{}]*\breturn\b[^{}]*\}",
                           member.mbody):
        if member.open_pos + blk.end() < pos:
            found.append(blk.group(1))
    for t in texts:
        found += [m for m in SWITCH_RE.findall(t) if m.startswith("Allow")]
    # A timer callback: the switch at the place that starts it.
    for site in world_ctx.get("timer_sites", {}).get((member.cls.name, member.name), []):
        smember, spos = site
        if smember is member:
            continue
        stexts = [src.masked[src.line_starts[src.line_of(spos) - 1]:spos]] + smember.conditions_at(spos)
        for t in stexts:
            found += [m for m in SWITCH_RE.findall(t) if m.startswith("Allow")]
    out = []
    for f in found:
        if f not in out:
            out.append(f)
    return out


def picks_from_player_list(member: Member) -> bool:
    """The method picks its victim from a List<GamePlayer> field (filled from GetPlayersInRadius): players only."""
    cls_m = member.cls.src.masked[member.cls.open_pos:member.cls.end]
    lists = re.findall(r"List\s*<\s*GamePlayer\s*>\s+(\w+)", cls_m)
    return any(re.search(r"\b" + re.escape(l) + r"\s*\[", member.mbody) for l in lists) and \
        not re.search(r"GameBot|EpicEncounterActors", member.mbody)


def player_only(member: Member, receiver: str | None, pos: int | None = None) -> bool:
    """True when the code picks only real players (GamePlayer): bots (GameBot is a GameNPC) are never chosen."""
    body = member.mbody
    if picks_from_player_list(member):
        return True
    # A death handler that starts with "GamePlayer player = sender as GamePlayer; if (player == null) return;".
    gp = re.search(r"GamePlayer\s+(\w+)\s*=\s*\w+\s+as\s+GamePlayer\s*;", body)
    if gp and re.search(r"if\s*\(\s*" + gp.group(1) + r"\s*==\s*null\s*\)\s*return", body):
        return True
    if pos is not None:
        conds = " ".join(member.conditions_at(pos))
        if re.search(r"\bis\s+GamePlayer\b|PrivLevel", conds) and not re.search(r"GameBot|EpicEncounterActors", conds):
            return True
    cls_masked = member.cls.src.masked[member.cls.open_pos:member.cls.end]
    if receiver:
        r = re.escape(receiver.split(".")[-1])
        if re.search(r"\bGamePlayer\s+" + r + r"\b", body) or re.search(r"\bGamePlayer\s+" + r + r"\b", cls_masked) or \
           re.search(r"foreach\s*\(\s*GamePlayer\s+" + r + r"\b", body):
            if not re.search(r"\bGameBot\b|EpicEncounterActors", body):
                return True
            return False
        if re.search(r"\b(GameLiving|GameObject)\s+" + r + r"\b", body + cls_masked):
            return False
    return False


def condition_summary(conds: list[str]) -> str:
    keep = [c for c in conds if not re.search(r"NamedMobDamage\.CountsAsAttacker|EpicEncounterActors\.IsCombatant", c)
            and not re.fullmatch(r"(if|else if)\s*\(\s*source\s+is\s+\w+(\s*\|\|\s*source\s+is\s+\w+)*\s*\)", c)]
    out = []
    for c in keep:
        types = set(re.findall(r"eDamageType\.(\w+)", c))
        if types and types >= ALL_DAMAGE and not re.sub(r"damageType\s*==\s*eDamageType\.\w+|[|()\s]|if|else", "", c):
            continue
        out.append(c)
    return " && ".join(out)


def detect_take_damage(world: World, cls: CsClass, ctx) -> list[Rule]:
    rules = []
    src = cls.src
    for mb in cls.member("TakeDamage"):
        body, mbody = mb.body, mb.mbody
        # Zero-damage exits: base.TakeDamage(..., 0, 0) or a bare return before any base call.
        for mm in re.finditer(r"base\.TakeDamage\s*\(\s*\w+\s*,\s*\w+\s*,\s*0\s*,\s*0\s*\)|\breturn\s*;|damageAmount\s*=\s*0\s*;",
                              mbody):
            pos = mb.open_pos + mm.start()
            conds = mb.conditions_at(pos)
            summary_cond = condition_summary(conds)
            if mm.group(0).startswith("return") and not summary_cond:
                continue
            if mm.group(0).startswith("return"):
                # A return right after the zero-damage call is the same exit.
                before = mbody[max(0, mm.start() - 240):mm.start()]
                before = before[before.rfind(";", 0, len(before) - 1) - 200 if before.rfind(";", 0, len(before) - 1) > 200 else 0:]
                if re.search(r"base\.TakeDamage\s*\([^;]*0\s*,\s*0\s*\)\s*;\s*$", before):
                    continue
                # A return after the real base call is no immunity.
                if re.search(r"base\.TakeDamage\s*\(\s*source\s*,\s*damageType\s*,\s*damageAmount", before):
                    continue
            line = src.line_of(pos)
            sw = switches_for(mb, pos, ctx)
            subtype, text = classify_immunity(world, summary_cond, cls)
            types = sorted(set(re.findall(r"eDamageType\.(\w+)", " ".join(conds))))
            dmg = "" if not types or set(types) >= ALL_DAMAGE or subtype == "damage_type" else f" (damage types {', '.join(types)} only)"
            start_line = src.line_of(min([o for o, c, h in mb.blocks() if o < pos < c] or [pos]))
            rules.append(make_rule("immune_while", src, start_line, line, f"Takes no damage {text}{dmg}.",
                                   "read" if subtype in ("out_of_tether", "adds_alive") else "inferred", cls, mb,
                                   subtype=subtype, condition=summary_cond or None, switch=sw or None))
        # Damage scaled down.
        for mm in re.finditer(r"damageAmount\s*(?:[*/-]=|=\s*(?!0\s*;)[^;]*damageAmount)[^;]*;|base\.TakeDamage\s*\([^;]*damageAmount\s*[*/]\s*[^;]*\)",
                              mbody):
            pos = mb.open_pos + mm.start()
            conds = condition_summary(mb.conditions_at(pos))
            rules.append(make_rule("damage_reduced", src, src.line_of(pos), src.line_of(pos),
                                   "Damage it takes is scaled: " + " ".join(body[mm.start():mm.end()].split()) +
                                   (f" while {conds}" if conds else ""), "inferred", cls, mb, condition=conds or None,
                                   switch=switches_for(mb, pos, ctx) or None))
        # A fixed small amount from some attackers (Corrupt Primal of Air: pets deal 5).
        for mm in re.finditer(r"base\.TakeDamage\s*\(\s*\w+\s*,\s*\w+\s*,\s*([1-9]\d*)\s*,\s*\d+\s*\)", mbody):
            pos = mb.open_pos + mm.start()
            conds = " && ".join(mb.conditions_at(pos))
            rules.append(make_rule("damage_reduced", src, src.line_of(pos), src.line_of(pos),
                                   f"Takes a fixed {mm.group(1)} damage per hit" + (f" while {conds}" if conds else ""), "read",
                                   cls, mb, condition=conds or None))
        # Punishing the attacker.
        for mm in re.finditer(r"\b(source|ad\.Attacker|truc|attacker)\b[^;\n]{0,40}\.TakeDamage\s*\(|CastSpell\s*\(", mbody):
            pos = mb.open_pos + mm.start()
            rules.append(make_rule("reflect", src, src.line_of(pos), src.line_of(pos),
                                   "Hits back when damaged: " + " ".join(src.line(src.line_of(pos)).split())[:140],
                                   "inferred", cls, mb, switch=switches_for(mb, pos, ctx) or None))
        # Attacker filter that leaves bots out.
        first_if = re.search(r"if\s*\(([^{]*)\)\s*\{", mbody)
        if first_if and re.search(r"source\s+is\s+GamePlayer", first_if.group(1)) and "GameBot" not in first_if.group(1) \
                and "NamedMobDamage" not in first_if.group(1):
            pos = mb.open_pos + first_if.start()
            rules.append(make_rule("attacker_filter", src, src.line_of(pos), src.line_of(pos),
                                   "Only these attackers can damage it: " + " ".join(first_if.group(1).split()), "inferred",
                                   cls, mb))
    return rules


def classify_immunity(world: World, cond: str, cls: CsClass) -> tuple[str, str]:
    # A switch in the condition is reported as the rule's switch; classify what is left.
    cond = re.sub(r"!?\s*(?:DOL\.GS\.)?EpicEncounterMechanics\.Allow\w+\s*&&\s*", "", cond or "")
    stripped = re.sub(r"^(else if|if)\s*\((.*)\)$", r"\2", cond.strip())
    if stripped and re.fullmatch(r"[\s(]*damageType\s*==\s*eDamageType\.\w+(\s*\|\|\s*damageType\s*==\s*eDamageType\.\w+)*[\s)]*", stripped):
        types = re.findall(r"eDamageType\.(\w+)", stripped)
        if not set(types) >= ALL_DAMAGE:
            return "damage_type", "from " + "/".join(types) + " damage"
    if not cond:
        return "always", "from these attackers (no condition found)"
    if "IsOutOfTetherRange" in cond:
        return "out_of_tether", "while it is drawn past its tether"
    counts = re.findall(r"(\w+)\.(\w*[Cc]ount\w*)\s*(>|>=|!=)\s*0", cond)
    alive = re.findall(r"(\w+)\s*\.\s*IsAlive", cond)
    if counts or re.search(r"\w+[Cc]ount\w*\s*>\s*0", cond):
        names = sorted({c[0] for c in counts}) or re.findall(r"(\w+[Cc]ount\w*)\s*>\s*0", cond)
        return "adds_alive", "while any of " + ", ".join(display_name(world, n) for n in names) + " lives"
    near = re.search(r"!\s*source\.IsWithinRadius\s*\(\s*(this|\w+)\s*,\s*(\w+)\s*\)", cond)
    if near and near.group(1) == "this":
        return "attacker_position", f"from attackers further than {near.group(2)} from it (only close attackers hurt it)"
    if re.search(r"IsWithinRadius\s*\(\s*(spawn|SpawnPoint)", cond) or "spawn" in cond.lower():
        return "attacker_position", "unless the attacker stands near its spawn (" + cond + ")"
    if near:
        return "attacker_position", f"from attackers further than {near.group(2)} from {near.group(1)}"
    if alive:
        return "adds_alive", "while " + ", ".join(sorted(set(alive))) + " is alive"
    neg = re.fullmatch(r"else \(not \((.*)\)\)", cond.strip())
    if neg:
        types = re.findall(r"eDamageType\.(\w+)", neg.group(1))
        if types and re.fullmatch(r"[\sdamageTyp=eDT.\w|]+", neg.group(1)):
            return "damage_type", "except from " + "/".join(types) + " damage"
        return "condition", "unless " + neg.group(1)
    return "condition", "while " + cond


def display_name(world: World | None, class_name: str) -> str:
    cls = world.classes.get(class_name) if world is not None else None
    if cls is not None:
        lit = literal(cls, "Name")
        if lit and lit[0].startswith('"'):
            return f"{lit[0].strip(chr(34))} ({class_name})"
    return class_name


def detect_adds(world: World, cls: CsClass, ctx) -> list[Rule]:
    rules = []
    src = cls.src
    for mb in cls.members:
        mbody = mb.mbody
        spawned = defaultdict(list)
        for mm in NEW_RE.finditer(mbody):
            name = mm.group(1)
            if not world.is_npc(name) or world.is_brain(name) or name in ("GameNPC",) and "AddToWorld" not in mbody:
                continue
            if name == "GameNPC" or name in NPC_ROOTS and name not in world.classes:
                # A plain new GameNPC() is usually a temporary caster/effect object.
                tail = mbody[mm.end():mm.end() + 600]
                if not re.search(r"\.AddToWorld\s*\(", tail):
                    continue
            spawned[name].append(mb.open_pos + mm.start())
        for name, positions in spawned.items():
            if "AddToWorld" not in mbody and mb.name != "AddToWorld":
                continue
            # A script that puts its own boss into the world (ScriptLoaded), or a class spawning its own kind.
            if mb.name == "ScriptLoaded" or name == cls.name or name in ctx.get("bodies_of", {}).get(cls.name, []):
                continue
            pos = positions[0]
            line = src.line_of(pos)
            trig, conf = spawn_trigger(mb, pos, ctx)
            loops = [c for c in mb.conditions_at(pos) if c.startswith("for ")]
            count = None
            count_range = None
            for c in loops:
                lm = re.search(r"<\s*=?\s*(\d+)", c)
                rm = re.search(r"<\s*Util\.Random\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)", c)
                if rm:
                    count_range = f"{rm.group(1)}-{rm.group(2)}"
                    count = int(rm.group(2))
                elif lm:
                    count = int(lm.group(1)) + (1 if "<=" in c else 0)
            if count is None:
                count = len(positions)
                count_text = f"{count} spawn call{'s' if count > 1 else ''} in {mb.name}"
            elif count_range:
                count_text = f"about {count_range} (loop to a random bound)"
            else:
                count_text = f"{count} (loop)"
            cap = None
            for c in mb.conditions_at(pos):
                cm = re.search(r"(\w+Count\w*)\s*<\s*(\d+)", c)
                if cm:
                    cap = int(cm.group(2))
            add = world.classes.get(name)
            add_brain = brain_of(world, add) if add else None
            base = world.root_of(name) if add else name
            from_script = None
            if add:
                lfs = literal(add, "LoadedFromScript")
                from_script = lfs[0] if lfs else None
            epic = world.is_epic(name)
            players = player_only(mb, None, pos)
            rules.append(make_rule(
                "adds", src, line, src.line_of(positions[-1]),
                f"Spawns {display_name(world, name)}: {trig}; count {count_text}" + (f", at most {cap} alive" if cap else ""),
                conf, cls, mb, add=name, addBase=base, addBrain=add_brain, count=count if loops else None, cap=cap,
                trigger=trig, addIsEpic=epic, switch=switches_for(mb, pos, ctx) or None,
                affects="real players only (bots are never picked)" if players else None))
    return rules


def spawn_trigger(mb: Member, pos: int, ctx) -> tuple[str, str]:
    conds = mb.conditions_at(pos)
    hp = [c for c in conds if "HealthPercent" in c or "MaxHealth" in c]
    chance = [c for c in conds if "Util.Chance" in c]
    timers = ctx.get("timer_sites", {}).get((mb.cls.name, mb.name), [])
    if mb.name == "AddToWorld":
        return "when it spawns", "read"
    if mb.name == "Die":
        return "when it dies", "read"
    if hp:
        return "when " + " and ".join(hp), "inferred"
    if chance:
        return "at random while fighting (" + " ".join(chance) + ")", "inferred"
    if timers:
        delays = sorted({d for d in (timer_delay(s[0], s[1]) for s in timers) if d})
        return "on a timer" + (f" ({', '.join(delays)} ms)" if delays else ""), "inferred"
    callers = ctx.get("callers", {}).get((mb.cls.name, mb.name), [])
    for cm, cpos in callers:
        if cm.name == "AddToWorld":
            return f"when it spawns (via {mb.name})", "inferred"
        if cm.name == "Die":
            return f"when it dies (via {mb.name})", "inferred"
        ch = [c for c in cm.conditions_at(cpos) if "HealthPercent" in c or "Util.Chance" in c or "MaxHealth" in c]
        if ch:
            return f"from {cm.name} when " + " and ".join(ch), "inferred"
        ct = ctx.get("timer_sites", {}).get((cm.cls.name, cm.name), [])
        if ct:
            return f"on a timer (via {cm.name})", "inferred"
    if mb.name == "Think":
        return "while fighting (each brain turn the condition holds)", "inferred"
    return f"from {mb.name}", "inferred"


def timer_delay(member: Member, pos: int) -> str | None:
    seg = member.cls.src.text[pos:pos + 240].split(";")[0]
    mm = re.search(r"\)\s*,\s*([^;]+?)\)\s*$", seg)
    if mm:
        d = " ".join(mm.group(1).split())
        return d if len(d) < 50 else None
    return None


def detect_phases(world: World, cls: CsClass, ctx) -> list[Rule]:
    rules = []
    src = cls.src
    seen = set()
    for mb in cls.members:
        if mb.name in ("TakeDamage",):
            continue
        for mm in re.finditer(r"(?:Body\.|this\.)?HealthPercent\s*(<=|<|>=|>|==)\s*(\d+)|Health\s*(<=|<)\s*(?:Body\.)?MaxHealth\s*\*\s*([\d.]+)|Health\s*(<=|<)\s*\(?\s*(?:Body\.)?MaxHealth\s*/\s*(\d+)",
                              mb.mbody):
            pos = mb.open_pos + mm.start()
            if mm.group(2):
                op, pct = mm.group(1), int(mm.group(2))
            elif mm.group(4):
                op, pct = mm.group(3), int(round(float(mm.group(4)) * 100))
            else:
                op, pct = mm.group(5), int(round(100 / int(mm.group(6))))
            if op in (">=", ">") or pct >= 100:
                continue
            line = src.line_of(pos)
            key = (mb.name, pct, line)
            if key in seen:
                continue
            seen.add(key)
            block = enclosing_block_after(mb, pos)
            actions = summarize_actions(world, src.text[block[0]:block[1]] if block else src.line(line))
            end_line = src.line_of(block[1]) if block else line
            rules.append(make_rule("phase", src, line, end_line,
                                   f"At {pct}% health ({mb.name}): " + (actions or "see the block"), "inferred", cls, mb,
                                   threshold=pct, switch=switches_for(mb, pos, ctx) or None))
    return rules


def enclosing_block_after(mb: Member, pos: int):
    """The block opened by the if whose condition contains pos."""
    m = mb.cls.src.masked
    depth = 0
    i = pos
    # Walk to the end of the condition's parentheses.
    start = m.rfind("(", mb.open_pos, pos)
    while start > mb.open_pos and not re.search(r"\b(if|while)\s*$", m[max(mb.open_pos, start - 12):start]):
        start = m.rfind("(", mb.open_pos, start)
    if start <= mb.open_pos:
        return None
    for i in range(start, mb.end):
        if m[i] == "(":
            depth += 1
        elif m[i] == ")":
            depth -= 1
            if depth == 0:
                break
    j = i + 1
    while j < mb.end and m[j] in " \t\n":
        j += 1
    if j < mb.end and m[j] == "{":
        return (j, match_brace(m, j))
    k = m.find(";", j)
    return (j, k if k > 0 else j)


CALL_RE = re.compile(r"\b(?:Body\.)?(\w+)\s*\(")
NOT_ACTIONS = {"if", "for", "foreach", "while", "new", "return", "switch", "Random", "Chance", "IsWithinRadius",
               "GetSpellLine", "SendMessage", "GetPlayersInRadius", "GetNPCsInRadius", "GetNPCsFromRegion", "Add",
               "Contains", "Remove", "Format", "BroadcastMessage", "ToString", "ECSTimerCallback", "ECSGameTimer",
               "StandardMobBrain", "Count", "Any", "Where", "First", "FirstOrDefault", "Cast", "Notify"}


def summarize_actions(world: World, code: str) -> str:
    parts = []
    for name in re.findall(r"ECSTimerCallback\s*\(\s*(\w+)\s*\)", code):
        parts.append(f"starts timer {name}")
    for name in re.findall(r"CastSpell\s*\(\s*(\w+)", code):
        parts.append(f"casts {name}")
    for name in re.findall(r"\bnew\s+([A-Z]\w*)\s*\(", code):
        if world.is_npc(name) and not world.is_brain(name):
            parts.append(f"spawns {name}")
    for name in CALL_RE.findall(code):
        if name in NOT_ACTIONS or name.startswith("Get") or name in ("CastSpell", "StartAttack", "TurnTo"):
            continue
        if name[0].isupper() and name not in ("AddToWorld",) and not world.is_npc(name):
            parts.append(f"calls {name}")
    for mm in re.finditer(r"(?:Body\.)?(Strength|Dexterity|Quickness|Empathy|Constitution|Intelligence|Piety)\s*=\s*([^;]+);", code):
        parts.append(f"{mm.group(1)} = {mm.group(2).strip()}")
    out = []
    for p in parts:
        if p not in out:
            out.append(p)
    return "; ".join(out[:8])


def detect_tether_reset(world: World, cls: CsClass, ctx) -> list[Rule]:
    rules = []
    src = cls.src
    lit = literal(cls, "TetherRange")
    if lit and int_or_none(lit[0]) is not None and int_or_none(lit[0]) > 0:
        rules.append(make_rule("tether", src, lit[1], lit[1], f"Tether range {int_or_none(lit[0])}: past it the boss goes home "
                               "(and, where its TakeDamage says so, is immune until it is back inside).", "read", cls,
                               (cls.member("AddToWorld") or [None])[0], range=int_or_none(lit[0])))
    for mb in cls.members:
        mbody = mb.mbody
        for mm in re.finditer(r"!\s*CheckProximityAggro\s*\(\s*\)", mbody):
            pos = mb.open_pos + mm.start()
            block = enclosing_block_after(mb, pos)
            code = src.text[block[0]:block[1]] if block else ""
            if "RETURN_TO_SPAWN" not in code and "MaxHealth" not in code:
                continue
            gated = "FightIsOver" in src.masked[pos - 200:pos + 200]
            heals = "Health = Body.MaxHealth" in code.replace("  ", " ") or "MaxHealth" in code
            rules.append(make_rule("reset", src, src.line_of(pos), src.line_of(block[1]) if block else src.line_of(pos),
                                   "Resets when its aggro list is empty: walks home" + (" at full health" if heals else "") +
                                   (", adds cleared" if re.search(r"RemoveFromWorld|Count\s*=\s*0", code) else "") +
                                   (" (waits until 8 s out of combat: EpicEncounterMechanics.FightIsOver)" if gated else ""),
                                   "read", cls, mb, fightIsOverGate=gated or None))
        for mm in re.finditer(r"(?:!\s*)?(?:Body\.)?IsWithinRadius\s*\(\s*(?:Body\.)?(?:SpawnPoint|spawn\w*)\s*,\s*(\d+)\s*\)|GetDistanceTo\s*\(\s*(?:Body\.)?SpawnPoint\s*\)\s*>\s*(\d+)",
                              mbody):
            if mb.name == "TakeDamage":
                continue
            pos = mb.open_pos + mm.start()
            block = enclosing_block_after(mb, pos)
            code = src.text[block[0]:block[1]] if block else src.line(src.line_of(pos))
            if not re.search(r"RETURN_TO_SPAWN|ReturnToSpawnPoint|MaxHealth|ClearAggro|MoveTo", code):
                continue
            dist = mm.group(1) or mm.group(2)
            rules.append(make_rule("tether", src, src.line_of(pos), src.line_of(pos),
                                   f"Leashed by distance from its spawn ({dist}): " + summarize_actions(world, code),
                                   "inferred", cls, mb, range=int(dist)))
    return rules


def detect_teleports(world: World, cls: CsClass, ctx) -> list[Rule]:
    rules = []
    src = cls.src
    for mb in cls.members:
        for mm in re.finditer(r"([\w\.\[\]]+)\s*\.\s*MoveTo\s*\(([^;]*)\)\s*;", mb.mbody):
            receiver = mm.group(1)
            args = " ".join(src.text[mb.open_pos + mm.start(2):mb.open_pos + mm.end(2)].split())
            pos = mb.open_pos + mm.start()
            line = src.line_of(pos)
            near = src.text[max(mb.open_pos, pos - 500):pos + 300].lower()
            if receiver in ("Body", "this") or receiver.startswith("Body.") and receiver.count(".") == 0:
                sub, what = "self", "Teleports itself"
            elif receiver.lower().startswith(("add", "npc", "boss", "mob")) and not re.search(r"player|target", receiver.lower()):
                sub, what = "moves_npc", f"Moves the NPC {receiver}"
            elif re.search(r"Z\s*\+|\.Z\s*\+", args) or "air" in near or "hurl" in near or "toss" in near or "throw" in near:
                sub, what = "air_toss", f"Throws {receiver} into the air or across the room"
            elif "jail" in near or "prison" in near or "cage" in near or "ice tomb" in near:
                sub, what = "jail", f"Jails {receiver}"
            else:
                sub, what = "fixed_spot", f"Teleports {receiver} to a fixed spot"
            if sub in ("self", "moves_npc") and "SpawnPoint" in args:
                continue
            sw = switches_for(mb, pos, ctx)
            players = player_only(mb, receiver, pos)
            rules.append(make_rule("teleport", src, line, line, f"{what} (MoveTo {args})", "inferred" if sub != "self" else "read",
                                   cls, mb, subtype=sub, switch=sw or None,
                                   affects="real players only (bots are never picked)" if players else None))
    return rules


def detect_misc(world: World, cls: CsClass, ctx) -> list[Rule]:
    rules = []
    src = cls.src
    for mb in cls.members:
        mbody, body = mb.mbody, mb.body
        # Position puzzle switch.
        for mm in re.finditer(r"EpicEncounterMechanics\.(AllowPositionPuzzle|AllowAddShield|AllowInvulnerableHazards|AllowGlacierGriffonPatrol)", mbody):
            pos = mb.open_pos + mm.start()
            if mb.name == "TakeDamage" and mm.group(1) == "AllowPositionPuzzle":
                continue  # reported as immune_while (attacker position)
            if mb.name == "TakeDamage":
                continue  # reported by the TakeDamage reader with its condition
            kind = {"AllowPositionPuzzle": "position_puzzle", "AllowAddShield": "immune_while",
                    "AllowInvulnerableHazards": "hazard", "AllowGlacierGriffonPatrol": "position_puzzle"}[mm.group(1)]
            block = enclosing_block_after(mb, pos)
            code = src.text[block[0]:block[1]] if block else src.line(src.line_of(pos))
            rules.append(make_rule(kind, src, src.line_of(pos), src.line_of(block[1]) if block else src.line_of(pos),
                                   f"Switched by EpicEncounterMechanics.{mm.group(1)} in {mb.name}: " +
                                   (summarize_actions(world, code) or "see the code"), "read", cls, mb,
                                   subtype="add_shield" if kind == "immune_while" else None, switch=[mm.group(1)]))
        if mb.name in ("AddToWorld", cls.name, "Die", "ScriptLoaded"):
            continue
        # Empowered / enrage (a stat changed in the fight).
        for mm in re.finditer(r"(?:Body\.)?(Strength|Quickness|Empathy|Dexterity|Intelligence|Piety)\s*=\s*([^;]+);", mbody):
            pos = mb.open_pos + mm.start()
            conds = [c for c in mb.conditions_at(pos) if not c.startswith(("for", "foreach"))]
            cond = condition_summary(conds)
            if not cond or "CheckProximityAggro" in cond:
                continue
            npc_line = src.line(src.line_of(pos))
            value = body[mm.start(2):mm.end(2)].strip()
            if re.search(r"Properties\.|npcTemplate", value):
                continue
            # Style and positional AI (stat tweaks by the angle to its target, its next style, a class copy).
            if re.search(r"\bangle\b|styles|Styles|ContainsEffect|PackageID\s*==|IsWithinRadius\s*\(\s*Body\.TargetObject|"
                         r"Switch_to_Ranged|CanCreate", cond):
                continue
            # Stats set whenever it fights are its normal stats, not a mechanic.
            if not re.sub(r"\b(if|else|HasAggro|Body|IsAlive|TargetObject|InCombat|null|enemy|is|GamePlayer|GameLiving|"
                          r"GameBot|true|false|this)\b|[!=&|()\s.]", "", cond):
                continue
            kind = "enrage_timer" if re.search(r"Minutes|CombatTime|Enrage|Berserk", cond, re.I) else "empowered"
            key = (kind, mb.name, cond)
            prev = next((r for r in rules if r.get("_key") == key and abs(r["line"] - src.line_of(pos)) <= 6), None)
            if prev:
                prev["summary"] = prev["summary"].replace(" while ", f", {mm.group(1)} {value} while ", 1)
                prev["lineEnd"] = max(prev["lineEnd"], src.line_of(pos))
                continue
            r = make_rule(kind, src, src.line_of(pos), src.line_of(pos),
                          f"{mm.group(1)} becomes {value} while {cond}", "inferred", cls, mb, condition=cond)
            r["_key"] = key
            rules.append(r)
        # Form changes (Hurionthex): a new model in the fight.
        # (Model = 1 is the invisible placeholder a hidden encounter wears until it is triggered: not a form.)
        if mb.name not in ("AddToWorld",) and re.search(r"\bBody\.Model\s*=\s*(?!1\s*;)\d+", mbody) and cls.name.endswith("Brain"):
            mm = re.search(r"\bBody\.Model\s*=\s*(?!1\s*;)\d+", mbody)
            pos = mb.open_pos + mm.start()
            rules.append(make_rule("phase", src, src.line_of(pos), src.line_of(pos),
                                   f"Changes form in {mb.name} ({summarize_actions(world, mb.body) or 'new model'})", "inferred",
                                   cls, mb, subtype="form"))
        # Pet devour.
        if re.search(r"GameSummonedPet|IControlledBrain|ControlledNpc|NearestDevourablePet|is\s+\w*Pet\b", mbody) and \
                re.search(r"\.Die\s*\(|RemoveFromWorld|Health\s*\+=|\.Health\s*=", mbody) and mb.name != "TakeDamage":
            mm = re.search(r"GameSummonedPet|IControlledBrain|ControlledNpc|NearestDevourablePet|is\s+\w*Pet\b", mbody)
            pos = mb.open_pos + mm.start()
            rules.append(make_rule("pet_devour", src, src.line_of(pos), src.line_of(mb.end),
                                   f"Handles pets in {mb.name} (kills or removes them" +
                                   (", heals by their health" if re.search(r"Health\s*\+=", mbody) else "") + ")",
                                   "inferred", cls, mb,
                                   switch=["PetDevourIntervalMilliseconds"] if "PetDevourInterval" in mbody else None))
        # Aggro wipe in the fight (not the reset).
        for mm in re.finditer(r"ClearAggroList\s*\(\s*\)|AggroList\.Clear\s*\(\s*\)|AggroTable\.Clear\s*\(\s*\)", mbody):
            pos = mb.open_pos + mm.start()
            conds = " ".join(mb.conditions_at(pos))
            if "CheckProximityAggro" in conds or mb.name in ("Die", "AddToWorld", "OnRemove", "RemoveFromWorld"):
                continue
            if re.search(r"!\s*HasAggro|IsAlive\s*==\s*false|!\s*(?:Body\.)?IsAlive|FightIsOver|IsReturningToSpawnPoint", conds):
                continue
            if reset_context(conds):
                add_reset(rules, src, cls, mb, pos, conds, ctx)
                continue
            pl = picks_from_player_list(mb)
            rules.append(make_rule("aggro_wipe", src, src.line_of(pos), src.line_of(pos),
                                   f"Clears its aggro list in {mb.name}" + (" and puts one random real player on it" if pl else "") +
                                   (f" when {condition_summary(mb.conditions_at(pos))}" if condition_summary(mb.conditions_at(pos)) else ""),
                                   "inferred", cls, mb, affects="real players only (bots are never picked)" if pl else None))
        # Linked packs.
        if not re.search(r"AddAggroListTo\s*\(|AddToAggroList\s*\(", mbody):
            pack_iter = []
        else:
            pack_iter = re.finditer(r"AddAggroListTo\s*\(|AddToAggroList\s*\(|PackageID\s*[!=]=\s*\"([^\"]*)\"", body)
        for mm in pack_iter:
            pos = mb.open_pos + mm.start()
            pk = re.search(r"PackageID\s*[!=]=\s*\"([^\"]*)\"", body)
            brains = re.findall(r"Brain\s+is\s+(\w+)", body[max(0, mm.start() - 400):mm.start() + 50])
            rules.append(make_rule("linked_pack", src, src.line_of(pos), src.line_of(pos),
                                   "Brings others into the fight" + (f": every NPC with PackageID \"{pk.group(1)}\"" if pk else "") +
                                   (f": {', '.join(sorted(set(brains)))}" if brains and not pk else ""), "read" if pk else "inferred",
                                   cls, mb))
            break
        # Heals another encounter or itself mid-fight.
        for mm in re.finditer(r"(\w+)\.Health\s*\+=|\b(\w+)\.Health\s*=\s*\2\.MaxHealth", body):
            pos = mb.open_pos + mm.start()
            conds = " ".join(mb.conditions_at(pos))
            if "CheckProximityAggro" in conds:
                continue
            if reset_context(conds):
                add_reset(rules, src, cls, mb, pos, conds, ctx)
                continue
            if re.match(r"(Body|this)$", mm.group(2) or "") and not conds:
                continue
            rules.append(make_rule("heal", src, src.line_of(pos), src.line_of(pos),
                                   "Heals: " + " ".join(src.line(src.line_of(pos)).split())[:140], "inferred", cls, mb,
                                   affects="real players only (bots are never picked)" if player_only(mb, None, pos) else None))
    return rules


def reset_context(conds: str) -> str | None:
    if "IsOutOfTetherRange" in conds:
        return "out_of_tether"
    if re.search(r"InCombatInLast\s*\([^)]*\)\s*==\s*false|!\s*(?:this\.)?(?:Body\.)?InCombatInLast", conds):
        return "out_of_combat"
    if re.search(r"!\s*(?:Body\.)?InCombat\b|InCombat\s*==\s*false", conds):
        return "out_of_combat"
    return None


def add_reset(rules, src, cls, mb, pos, conds, ctx):
    sub = reset_context(conds)
    line = src.line_of(pos)
    for r in rules:
        if r["kind"] == "reset" and r.get("subtype") == sub and r["method"] == mb.name and abs(r["line"] - line) <= 12:
            r["line"], r["lineEnd"] = min(r["line"], line), max(r["lineEnd"], line)
            r["anchor"] = src.line(r["line"]).strip()[:160]
            return
    secs = re.search(r"InCombatInLast\s*\(\s*(\d+)\s*\*\s*1000", conds)
    text = ("Resets while drawn past its tether: aggro cleared, back to full health" if sub == "out_of_tether" else
            "Resets after " + (f"{secs.group(1)} s" if secs else "a while") + " out of combat: aggro cleared, full health")
    rules.append(make_rule("reset", src, line, line, text, "read", cls, mb, subtype=sub))


def group_rules(rules: list[Rule]) -> list[Rule]:
    """One teleport rule per method and kind of move; one adds rule per spawning method."""
    out, groups = [], {}
    for r in rules:
        if r["kind"] == "teleport":
            key = ("teleport", r["sourceClass"], r["method"], r.get("subtype"), r.get("affects"))
        elif r["kind"] == "adds":
            key = ("adds", r["sourceClass"], r["method"], r.get("trigger"))
        else:
            out.append(r)
            continue
        g = groups.get(key)
        if g is None:
            groups[key] = r
            r["_n"] = 1
            out.append(r)
            continue
        g["_n"] += 1
        g["lineEnd"] = max(g["lineEnd"], r["lineEnd"])
        if r["kind"] == "teleport":
            g.setdefault("_dest", [g["summary"].split("(MoveTo ", 1)[-1].rstrip(")")])
            g["_dest"].append(r["summary"].split("(MoveTo ", 1)[-1].rstrip(")"))
        else:
            adds = g["add"] if isinstance(g["add"], list) else [g["add"]]
            if r["add"] not in adds:
                adds.append(r["add"])
            g["add"] = adds
            g["addIsEpic"] = g.get("addIsEpic") or r.get("addIsEpic")
            g.setdefault("_sum", [g["summary"]]).append(r["summary"])
    for r in out:
        n = r.pop("_n", None)
        if "_dest" in r:
            head = r["summary"].split(" (MoveTo ", 1)[0]
            r["summary"] = f"{head}: one of {len(r['_dest'])} spots (MoveTo {'; '.join(r.pop('_dest'))})"
        if "_sum" in r:
            parts = r.pop("_sum")
            names = [re.match(r"Spawns (.*?): ", p).group(1) for p in parts if re.match(r"Spawns (.*?): ", p)]
            rest = parts[0].split(": ", 1)[1] if ": " in parts[0] else ""
            rest = re.sub(r"; count .*", "", rest)
            r["summary"] = f"Spawns {len(parts)} kinds of add ({', '.join(names)}): {rest}"
            r["addBase"] = None
            r["addBrain"] = None
    # Thresholds in one method that do the same thing: one entry.
    phased, by_action = [], {}
    for r in out:
        if r["kind"] == "phase":
            action = r["summary"].split("): ", 1)[-1]
            key = (r["sourceClass"], r["method"], action)
            if key in by_action:
                g = by_action[key]
                g["thresholds"] = g.get("thresholds", [g["threshold"]]) + [r["threshold"]]
                g["lineEnd"] = max(g["lineEnd"], r["lineEnd"])
                g["summary"] = f"At {'/'.join(str(t) for t in g['thresholds'])}% health ({r['method']}): {action}"
                continue
            by_action[key] = r
        phased.append(r)
    out = phased
    # The same rule in sibling classes (four orbs with one brain each): one entry.
    merged, by_text = [], {}
    for r in out:
        key = (r["kind"], r["summary"], r["method"] if r["kind"] != "offline_change" else "", r["file"])
        if key in by_text:
            by_text[key].setdefault("alsoIn", []).append(r["sourceClass"])
            continue
        by_text[key] = r
        merged.append(r)
    return merged


def detect_spells(world: World, cls: CsClass) -> list[dict]:
    spells = []
    src = cls.src
    for mb in cls.members:
        body = mb.body
        for mm in re.finditer(r"DbSpell\s+(\w+)\s*=\s*new\s+DbSpell\s*\(\s*\)\s*;", body):
            var = mm.group(1)
            seg = body[mm.end():]
            end = re.search(r"new\s+Spell\s*\(\s*" + re.escape(var), seg)
            seg = seg[:end.start()] if end else seg[:3000]

            def get(prop):
                pm = re.search(re.escape(var) + r"\." + prop + r"\s*=\s*([^;]+);", seg)
                return " ".join(pm.group(1).split()) if pm else None
            name = get("Name")
            spells.append({
                "name": name.strip('"') if name else None, "member": mb.name,
                "type": (get("Type") or "").replace(".ToString()", "").replace("eSpellType.", "").strip('"') or None,
                "damage": get("Damage"), "radius": get("Radius"), "range": get("Range"), "duration": get("Duration"),
                "value": get("Value"), "target": (get("Target") or "").replace(".ToString()", "").replace("eSpellTarget.", "") or None,
                "file": rel(src), "line": src.line_of(mb.open_pos + mm.start()), "sourceClass": cls.name,
            })
    return spells


OFFLINE_COMMENT_RE = re.compile(r"//\s*(Offline DAoC:|Offline raids|Owner \d{4}-\d\d-\d\d|Run \d+ \(\d{4}-|run \d+:|Player-only|"
                                r"Mechanic change|Goal \d+ \(owner|.*owner permi)", re.I)


def detect_offline_changes(world: World, cls: CsClass, ctx) -> list[Rule]:
    """Comments the fork left where it changed a boss for bot raids."""
    rules = []
    src = cls.src
    seen_lines = set()
    for i in range(cls.first_line, cls.last_line + 1):
        line = src.line(i)
        if i in seen_lines or not OFFLINE_COMMENT_RE.search(line):
            continue
        # The whole comment: this line and the comment lines right after it.
        text, j = [], i
        while j <= cls.last_line and "//" in src.line(j) and (j == i or src.line(j).strip().startswith("//")):
            text.append(src.line(j).split("//", 1)[1].strip())
            seen_lines.add(j)
            j += 1
        member = next((m for m in cls.members if m.first_line <= i <= m.last_line), None)
        near = "\n".join(src.line(k) for k in range(max(1, i - 8), min(len(src.lines), j + 8) + 1))
        sw = sorted(set(re.findall(r"EpicEncounterMechanics\.(Allow\w+)", near)))
        rules.append(make_rule("offline_change", src, i, j - 1, " ".join(text)[:400], "read", cls, member,
                               switch=sw or None, restored=bool(sw) or None))
    return rules


DETECTORS = (detect_offline_changes, detect_take_damage, detect_adds, detect_phases, detect_tether_reset, detect_teleports, detect_misc)

# ---------------------------------------------------------------------------------------------------------------------
# Raid definitions
# ---------------------------------------------------------------------------------------------------------------------


def read_raids() -> tuple[list[dict], dict]:
    text = read_text(REPO / RAID_FILE)
    raids = []
    for mm in re.finditer(r'new\("((?:dungeon|dragon|epic|summoners|darkness)-[\w-]+)",\s*"([^"]+)",\s*eRealm\.(\w+),\s*(\d+),\s*"(\w+)",\s*(\d+)'
                          r'(?:,\s*new\(([-\d, ]+)\),\s*\[([^\]]*)\])?', text):
        finals = [f.strip().strip('"') for f in (mm.group(8) or "").split(",") if f.strip()]
        raids.append({"id": mm.group(1), "name": mm.group(2), "realm": mm.group(3), "region": int(mm.group(4)),
                      "bossType": mm.group(5), "suggestedBots": int(mm.group(6)), "finals": finals or None,
                      "line": text[:mm.start()].count("\n") + 1})
    finals_switch = {}
    fm = re.search(r"public string\[\] FinalTypes => Region switch\s*\{(.*?)\};", text, re.S)
    if fm:
        for r, names in re.findall(r"(\d+)\s*=>\s*\[([^\]]*)\]", fm.group(1)):
            finals_switch[int(r)] = [n.strip().strip('"') for n in names.split(",")]
    for raid in raids:
        if not raid["finals"]:
            raid["finals"] = finals_switch.get(raid["region"], [raid["bossType"]])
    ntext = read_text(REPO / NEUTRAL_FILE)
    objectives = {}
    om = re.search(r"public static string\[\] Objectives\(ushort region\) => region switch\s*\{(.*?)\};", ntext, re.S)
    if om:
        for key, names in re.findall(r"(SummonersHall|DarknessFalls)\s*=>\s*\[([^\]]*)\]", om.group(1)):
            objectives[{"SummonersHall": 248, "DarknessFalls": 249}[key]] = re.findall(r'"([^"]+)"', names)
    return raids, objectives


def read_switches() -> list[dict]:
    src = Source(SWITCH_FILE)
    out = []
    for i, line in enumerate(src.lines, 1):
        mm = re.search(r"public static bool (Allow\w+)\s*=>\s*(\w+);", line)
        if mm:
            out.append({"name": mm.group(1), "value": mm.group(2), "file": rel(src), "line": i})
        mm = re.search(r"public static bool (Allow\w+)\s*\{\s*get;\s*set;\s*\}", line)
        if mm:
            doc = src.line(i - 1).strip()
            doc = re.sub(r"^///\s*<summary>|</summary>$", "", doc).strip()
            out.append({"name": mm.group(1), "value": "false", "file": rel(src), "line": i, "restores": True,
                        "about": doc if not doc.startswith("//") or doc.startswith("///") else ""})
        mm = re.search(r"public const int (\w+)\s*=\s*([\d_]+);", line)
        if mm:
            out.append({"name": mm.group(1), "value": mm.group(2).replace("_", ""), "file": rel(src), "line": i})
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------------------------------------------------


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def dungeon_of(rel_path: Path, class_name: str, src: Source):
    parts = rel_path.relative_to(SCRIPTS).as_posix()
    for folder, (name, region) in sorted(FOLDERS.items(), key=lambda kv: -len(kv[0])):
        if parts.startswith(folder + "/"):
            if folder == "Dragons":
                region = DRAGON_REGIONS.get(rel_path.stem)
                name = {1: "Golestandt's lair (Dartmoor)", 100: "Gjalpinulva's lair (Myrkwood)", 200: "Cuuldurach's lair (Sheeroe Hills)"}.get(region, name)
            return name, region, "read" if region else None
    segs = parts.split("/")
    name = " / ".join(segs[:-1]) if len(segs) > 1 else "Open world"
    found = re.findall(r"CurrentRegionID\s*(?:==|=)\s*(\d+)|MoveTo\s*\(\s*(\d+)\s*,", src.masked)
    nums = [int(a or b) for a, b in found]
    if nums:
        best = max(set(nums), key=nums.count)
        return name, best, "inferred"
    return name, None, None


def build(world: World):
    timer_sites = defaultdict(list)
    callers = defaultdict(list)
    for cls in world.classes.values():
        for mb in cls.members:
            for mm in re.finditer(r"ECSTimerCallback\s*\(\s*(\w+)\s*\)", mb.mbody):
                timer_sites[(cls.name, mm.group(1))].append((mb, mb.open_pos + mm.start()))
            for other in cls.members:
                if other is mb or not other.is_method or other.name in ("Think", "AddToWorld", "Die"):
                    continue
                for mm in re.finditer(r"(?<![\.\w])" + re.escape(other.name) + r"\s*\(", mb.mbody):
                    callers[(cls.name, other.name)].append((mb, mb.open_pos + mm.start()))
    ctx = {"timer_sites": timer_sites, "callers": callers, "bodies_of": {}}

    brain_bodies = defaultdict(list)
    body_brain = {}
    for name, cls in world.classes.items():
        if world.is_npc(name):
            b = brain_of(world, cls)
            if b:
                body_brain[name] = b
                brain_bodies[b].append(name)

    ctx["bodies_of"] = brain_bodies

    def owners(name):
        return brain_bodies.get(name, []) if world.is_brain(name) else [name]

    spawn_edges = defaultdict(set)   # owner npc -> spawned npc classes
    spawned_by = defaultdict(set)
    for name, cls in world.classes.items():
        for mb in cls.members:
            if "AddToWorld" not in mb.mbody:
                continue
            for mm in NEW_RE.finditer(mb.mbody):
                add = mm.group(1)
                if add in world.classes and world.is_npc(add) and not world.is_brain(add) and add != name:
                    for o in owners(name):
                        if o != add:
                            spawn_edges[o].add(add)
                            spawned_by[add].add(o)

    # Encounter controllers (Olcasgean's initializator spawns the primals, their Earthmenders and the vortices): adds a
    # controller spawns in the same method as a boss belong to that boss; the rest make the controller an entry.
    controllers = set()

    def words(n):
        return {w for w in re.findall(r"[A-Z][a-z]+", n) if len(w) >= 4} - {"Primal", "Brain", "Spawn", "Adds", "Initializator"}
    for name, cls in world.classes.items():
        if world.is_boss(name) or not spawn_edges.get(name):
            continue
        made_all = set(spawn_edges[name])
        for b in [m for m in made_all if world.is_boss(m)]:
            for o in [m for m in made_all if not world.is_boss(m) and any(wd in m for wd in words(b))]:
                spawn_edges[b].add(o)
                spawned_by[o].add(b)
        brain = world.classes.get(body_brain.get(name, ""))
        for mb in cls.members + (brain.members if brain else []):
            made = [m.group(1) for m in NEW_RE.finditer(mb.mbody) if m.group(1) in spawn_edges[name]]
            bosses = {m for m in made if world.is_boss(m)}
            others = {m for m in made if not world.is_boss(m)}
            for b in bosses:
                for o in others:
                    spawn_edges[b].add(o)
                    spawned_by[o].add(b)
            if others and not bosses and any(world.is_boss(x) for x in spawn_edges[name]):
                controllers.add(name)

    raids, objectives = read_raids()
    roots = [n for n in world.classes if world.is_boss(n)] + sorted(controllers)
    # Standalone epic NPCs nobody spawns are encounters too (the route fights every IGameEpicNpc).
    roots += [n for n in world.classes if not world.is_boss(n) and world.is_epic(n) and not spawned_by.get(n)]
    # Scripted named monsters that are no epic NPC (Hall of the Corrupt's lieutenants): a class with its own name, a
    # brain or damage rule of its own, and nobody spawning it.
    for n, c in world.classes.items():
        if n in roots or spawned_by.get(n) or not world.is_npc(n) or world.is_brain(n) or n in NPC_ROOTS:
            continue
        if re.search(r"Init(ializator)?$", n):
            continue
        if c.member("TakeDamage") or n in body_brain:
            roots.append(n)
    roots = sorted(set(roots), key=lambda n: (world.classes[n].src.rel.as_posix(), world.classes[n].first_line))

    encounters = []
    for name in roots:
        cls = world.classes[name]
        src = cls.src
        components, queue = [], [name]
        while queue:
            cur = queue.pop(0)
            if cur in components:
                continue
            components.append(cur)
            if cur in body_brain:
                components.append(body_brain[cur]) if body_brain[cur] not in components else None
            for add in sorted(spawn_edges.get(cur, [])):
                # A controller's adds that belong to one of its bosses are listed with that boss.
                if name in controllers and any(world.is_boss(o) for o in spawned_by.get(add, [])):
                    continue
                if not world.is_boss(add) and add not in components:
                    queue.append(add)
        comp_classes = [world.classes[c] for c in components if c in world.classes]
        rules, spells = [], []
        for cc in comp_classes:
            for det in DETECTORS:
                for r in det(world, cc, ctx):
                    r.pop("_key", None)
                    if cc.name != name and cc.name != body_brain.get(name):
                        r["from"] = "add"
                    rules.append(r)
            cs = detect_spells(world, cc)
            spells += cs
            for sp in cs:
                if sp.get("type") and re.search(r"DamageShield|Reflect", sp["type"]):
                    hs = world.sources[Path(sp["file"])]
                    rules.append(make_rule("reflect", hs, sp["line"], sp["line"],
                                           f"Has a damage shield / reflect spell: {sp['name']} ({sp['type']}, damage "
                                           f"{sp.get('damage')}, target {sp.get('target')})", "read", cc,
                                           next((m for m in cc.members if m.name == sp["member"]), None)))
                if sp.get("type") and "Heal" in sp["type"]:
                    hs = world.sources[Path(sp["file"])]
                    r = make_rule("heal", hs, sp["line"], sp["line"],
                                  f"Has a heal spell: {sp['name']} ({sp['type']}, value {sp.get('value')}, target "
                                  f"{sp.get('target')}{', radius ' + sp['radius'] if sp.get('radius') else ''})",
                                  "read", cc, next((m for m in cc.members if m.name == sp["member"]), None))
                    if cc.name != name and cc.name != body_brain.get(name):
                        r["from"] = "add"
                    rules.append(r)
        # Everything an add does that only comes when a real player dies (Nosdoden's spirits) is players-only too.
        player_adds = set()
        for r in rules:
            if r["kind"] == "adds" and r.get("affects"):
                player_adds |= set(r["add"] if isinstance(r["add"], list) else [r["add"]])
        tree, queue = set(), list(player_adds)
        while queue:
            cur = queue.pop()
            if cur in tree:
                continue
            tree.add(cur)
            if cur in body_brain:
                tree.add(body_brain[cur])
            queue += list(spawn_edges.get(cur, []))
        for r in rules:
            if r["sourceClass"] in tree and not r.get("affects"):
                r["affects"] = "only after a real player dies (bots never trigger it)"
        rules = group_rules(rules)
        # Dedupe identical rules (same kind, file, line).
        uniq, seen = [], set()
        for r in rules:
            key = (r["kind"], r["file"], r["line"], str(r.get("add")), r.get("threshold"))
            if key in seen:
                continue
            seen.add(key)
            uniq.append(r)
        rules = sorted(uniq, key=lambda r: (list(RULE_KINDS).index(r["kind"]), r["file"], r["line"]))
        for r in rules:
            if r["kind"] == "tether" and r["summary"].startswith("Tether range"):
                owner = r["sourceClass"]
                immune_tether = any(x["kind"] == "immune_while" and x.get("subtype") == "out_of_tether" and
                                    (x["sourceClass"] == owner or owner in x.get("alsoIn", [])) for x in rules)
                who = "" if owner == name else f" ({display_name(world, owner)})"
                r["summary"] = (f"Tether range {r['range']}{who}: drawn further from its spawn it is immune (its "
                                "TakeDamage) until it is back inside, and walks home." if immune_tether else
                                f"Tether range {r['range']}{who}: drawn further from its spawn it walks home.")
        dungeon, region, region_conf = dungeon_of(src.rel, name, src)
        boss_name = literal(cls, "Name")
        level = literal(cls, "Level")
        health = literal(cls, "MaxHealth")
        tmpl = re.search(r"GetTemplate\s*\(\s*(\d+)\s*\)", "".join(m.body for m in cls.member("AddToWorld")))
        display = boss_name[0].strip('"') if boss_name and boss_name[0].startswith('"') else None
        if not display:
            display = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", name)
        adds = []
        for c in components[1:]:
            if c in world.classes and world.is_npc(c) and c != name:
                ac = world.classes[c]
                an = literal(ac, "Name")
                adds.append({"class": c, "name": an[0].strip('"') if an and an[0].startswith('"') else None,
                             "base": ac.base, "brain": body_brain.get(c), "epic": world.is_epic(c),
                             "loadedFromScript": (literal(ac, "LoadedFromScript") or [None])[0],
                             "flags": (literal(ac, "Flags") or [None])[0],
                             "maxHealth": int_or_none((literal(ac, "MaxHealth") or [None])[0]),
                             "level": (literal(ac, "Level") or [None])[0],
                             "file": rel(ac.src), "line": ac.first_line})
        enc = {
            "id": slug(dungeon) + "/" + slug(name),
            "boss": display,
            "class": name,
            "base": cls.base,
            "brain": body_brain.get(name),
            "file": rel(src),
            "lines": [cls.first_line, cls.last_line],
            "dungeon": dungeon,
            "region": region,
            "regionConfidence": region_conf,
            "level": int_or_none(level[0]) if level else None,
            "levelText": level[0] if level and int_or_none(level[0]) is None else None,
            "maxHealth": int_or_none(health[0]) if health else None,
            "npcTemplate": int(tmpl.group(1)) if tmpl else None,
            "spawnedBy": sorted(spawned_by.get(name, [])) or None,
            "spawnsEncounters": sorted(a for a in spawn_edges.get(name, []) if world.is_boss(a)) or None,
            "partners": sorted(c.name for c in world.classes.values() if c.src is src and world.is_boss(c.name) and c.name != name) or None,
            "adds": adds,
            "rules": rules,
            "spells": spells,
            "eraNotes": "",
        }
        encounters.append(enc)
    return encounters, raids, objectives, ctx


# ---------------------------------------------------------------------------------------------------------------------
# World data (optional): where the database spawns each scripted class, and its npc templates
# ---------------------------------------------------------------------------------------------------------------------


def write_world_snapshot(world: World, db_path: str) -> None:
    """Reads a release world database (never a played one: the mob and npctemplate tables only, read-only) and writes
    the rows the catalog uses to docs/encounters/world-spawns.json, so CI needs no database."""
    import sqlite3
    db = sqlite3.connect(f"file:{db_path}?mode=ro&immutable=1", uri=True)
    names = {n for n in world.classes if world.is_npc(n)}
    mobs = defaultdict(list)
    for row in db.execute("select ClassType, Name, Region, X, Y, Z, Level, NPCTemplateID, PackageID from mob "
                          "where ClassType like 'DOL.%'"):
        cls = row[0].split(".")[-1]
        if cls in names:
            mobs[cls].append({"name": row[1], "region": row[2], "x": row[3], "y": row[4], "z": row[5], "level": row[6],
                              "npcTemplate": row[7] if row[7] and row[7] > 0 else None})
    template_ids = set()
    packages = set()
    for src in world.sources.values():
        template_ids |= {int(t) for t in re.findall(r"GetTemplate\s*\(\s*(\d+)\s*\)", src.masked)}
        packages |= set(re.findall(r'PackageID\s*[!=]=\s*"([^"]+)"', src.text))
    template_ids |= {m["npcTemplate"] for rows in mobs.values() for m in rows if m["npcTemplate"]}
    templates = {}
    for tid in sorted(template_ids):
        row = db.execute("select Name, Level, TetherRange, AggroLevel, AggroRange, MaxSpeed, Flags, PackageID from npctemplate "
                         "where TemplateId = ?", (tid,)).fetchone()
        if row:
            templates[str(tid)] = {"name": row[0], "level": row[1], "tetherRange": row[2], "aggroLevel": row[3],
                                   "aggroRange": row[4], "maxSpeed": row[5], "flags": row[6], "packageID": row[7] or None}
    pk = {}
    for p in sorted(packages):
        rows = db.execute("select Name, Region, count(*) from mob where PackageID = ? group by Name, Region order by Name", (p,)).fetchall()
        pk[p] = [{"name": r[0], "region": r[1], "count": r[2]} for r in rows]
    snapshot = {
        "about": "Rows of a release world database (mob and npctemplate tables) the epic encounter catalog reads. "
                 "Written by epic_encounter_catalog.py --world-db; world data only, no player data.",
        "database": Path(db_path).name,
        "mobsByClass": {k: sorted(v, key=lambda m: (m["region"], m["x"], m["y"])) for k, v in sorted(mobs.items())},
        "npcTemplates": templates,
        "packages": pk,
    }
    (REPO / WORLD_SNAPSHOT).parent.mkdir(parents=True, exist_ok=True)
    (REPO / WORLD_SNAPSHOT).write_text(json.dumps(snapshot, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def apply_world(encounters: list[dict]) -> None:
    if not (REPO / WORLD_SNAPSHOT).exists():
        return
    snap = json.loads(read_text(REPO / WORLD_SNAPSHOT))
    mobs, templates, packages = snap["mobsByClass"], snap["npcTemplates"], snap["packages"]
    for e in encounters:
        rows = mobs.get(e["class"], [])
        tmpl = templates.get(str(e.get("npcTemplate"))) if e.get("npcTemplate") else None
        if not tmpl and rows and rows[0].get("npcTemplate"):
            tmpl = templates.get(str(rows[0]["npcTemplate"]))
        if rows:
            e["spawns"] = [{k: m[k] for k in ("region", "x", "y", "z")} for m in rows[:4]]
            if len(rows) > 4:
                e["spawnCount"] = len(rows)
            if e.get("region") is None or e.get("regionConfidence") == "inferred":
                regions = sorted({m["region"] for m in rows})
                e["region"] = regions[0]
                e["regionConfidence"] = "world database"
        if e.get("level") is None:
            lv = (tmpl or {}).get("level") or (rows[0]["level"] if rows else None)
            if lv is not None and str(lv).isdigit():
                e["level"] = int(lv)
                e["levelSource"] = "npc template" if tmpl and tmpl.get("level") else "world database"
                e.pop("levelText", None)
        if tmpl:
            if e["boss"] == re.sub(r"(?<=[a-z])(?=[A-Z])", " ", e["class"]) and tmpl.get("name"):
                e["boss"] = tmpl["name"]
            if tmpl.get("tetherRange") and not any(r["kind"] == "tether" and r.get("range") for r in e["rules"]):
                e["templateTetherRange"] = tmpl["tetherRange"]
        elif rows and e["boss"] == re.sub(r"(?<=[a-z])(?=[A-Z])", " ", e["class"]):
            e["boss"] = rows[0]["name"]
        for r in e["rules"]:
            if r["kind"] == "linked_pack":
                mm = re.search(r'PackageID "([^"]+)"', r["summary"])
                if mm and mm.group(1) in packages:
                    members = packages[mm.group(1)]
                    r["packSize"] = sum(m["count"] for m in members)
                    r["summary"] += (f" ({r['packSize']} in the world database: " +
                                     ", ".join(f"{m['count']} {m['name']}" for m in members[:5]) + ")"
                                     if members else " (none in the world database)")


def relink_raids(encounters, raids, objectives):
    for e in encounters:
        region, display, name = e.get("region"), e["boss"], e["class"]
        links = []
        for raid in raids:
            if raid["region"] != region:
                continue
            role = "final" if name in raid["finals"] or name == raid["bossType"] else "named"
            if region in objectives:
                role = "final" if display == objectives[region][-1] else (
                    "objective" if display in objectives[region] else "fought if met")
            elif not (e["base"] in ("GameEpicBoss", "GameEpicNPC", "GameEpicAros")):
                role = "monster on the way (not an epic NPC)"
            links.append({"id": raid["id"], "role": role})
        e["raids"] = links
        ai = "no raid"
        if links:
            ai = "dragon fight" if any(r["id"].startswith("dragon") for r in links) else "generic dungeon route"
        if region == 249:
            ai = "no raid AI"
        e["raidAi"] = ai


# ---------------------------------------------------------------------------------------------------------------------
# Hand notes, tactics, priority
# ---------------------------------------------------------------------------------------------------------------------


def short_key(enc: dict, rule: dict) -> str:
    """How hand notes name a rule: kind@line in the encounter's file, kind@File.cs:line in another."""
    if rule["file"] == enc["file"]:
        return f"{rule['kind']}@{rule['line']}"
    return f"{rule['kind']}@{Path(rule['file']).name}:{rule['line']}"


def match_moved_keys(enc: dict, rule_notes: dict) -> dict:
    """Current rule key -> the hand note's key, for notes whose rule moved (lines added above it): same kind and file,
    nearest line within 200. The note then still applies; the script hash makes its check stale until it is stamped."""
    current = {short_key(enc, r): r for r in enc["rules"]}
    out = {}
    for key in rule_notes:
        if key in current or rule_notes[key].get("extra"):
            continue
        kind, _, where = key.partition("@")
        fname, _, line = where.rpartition(":")
        if not line.isdigit():
            continue
        best = None
        for ck, r in current.items():
            if r["kind"] != kind or ck in out or ck in rule_notes:
                continue
            if (Path(r["file"]).name if fname else enc["file"]) != (fname or enc["file"]):
                continue
            d = abs(r["line"] - int(line))
            if d <= 200 and (best is None or d < best[0]):
                best = (d, ck)
        if best:
            out[best[1]] = key
    return out


def rule_key(rule: dict) -> str:
    add = rule.get("add")
    extra = ("+".join(add) if isinstance(add, list) else add) or rule.get("threshold") or rule.get("subtype") or ""
    return f"{rule['kind']}|{rule['sourceClass']}|{rule.get('method')}|{extra}|{' '.join(rule['anchor'].split())[:80]}"


def default_tactic(enc: dict, rule: dict) -> tuple[str, str]:
    kind, sub = rule["kind"], rule.get("subtype")
    sw = rule.get("switch") or []
    allow = [s for s in sw if s.startswith("Allow")]
    if allow and kind != "immune_while" or allow and sub in ("attacker_position", "add_shield"):
        return ("Off today (" + ", ".join(allow) + " is false). To play it: " + switched_tactic(kind, sub),
                "off today")
    if rule.get("affects"):
        return ("Off for bots today: it " + ("picks real players only" if rule["affects"].startswith("real") else
                "only follows a real player's death") + " (GameBot is no GamePlayer). To play it with bots: " +
                switched_tactic(kind, sub), "off today")
    if kind == "immune_while":
        if sub == "out_of_tether":
            return ("Fight inside the tether: stop hitting it once it is drawn out and lead it home "
                    "(AutonomousTetherReset already does this).", "easy for the current raid AI")
        if sub == "adds_alive":
            return ("Split parties onto the adds that shield it, kill them, then turn on the boss; stop attacking the boss "
                    "while it is immune.", "needs new raid AI")
        if sub == "attacker_position" and "close attackers" in rule["summary"]:
            return ("Fight it up close: casters and archers move in to the allowed distance or switch to the adds.",
                    "needs new raid AI")
        if sub == "attacker_position":
            return "Attack only from where the script allows (see the condition).", "needs new raid AI"
        if sub == "damage_type" and "except" in rule["summary"]:
            return "Only the named damage type hurts it: classes that deal it attack, the rest stand off.", "needs new raid AI"
        if sub == "damage_type":
            return "Those damage types do nothing: classes that deal them switch weapon or target.", "needs new raid AI"
        return "Stop attacking while the condition holds; meet the condition first.", "needs new raid AI"
    if kind == "damage_reduced":
        return "Expect a slower kill; meet the condition that lifts the reduction if there is one.", "easy for the current raid AI"
    if kind == "reflect":
        return "Check who it punishes; switch damage type or attacker if it reflects.", "needs new raid AI"
    if kind == "adds":
        if rule.get("addIsEpic"):
            return ("Its adds are epic NPCs: the route treats them as separate encounters, not adds. Assign parties to them "
                    "as they spawn.", "needs new raid AI")
        return ("Kill the adds as they spawn (the route already puts script-spawned adds near the front first).",
                "easy for the current raid AI")
    if kind == "phase":
        return "Know the threshold: get ready for what it starts (adds, a big hit) before pushing it past.", "needs new raid AI"
    if kind == "tether":
        return "Fight it where it stands; don't pull it past its tether.", "easy for the current raid AI"
    if kind == "reset":
        return ("Keep someone on its aggro list until it dies; a wipe resets it (the 8 s quiet rule keeps a big raid's "
                "gaps from resetting it).", "easy for the current raid AI")
    if kind == "teleport":
        if sub == "self":
            return "Follow it when it moves; ranged keep hitting.", "easy for the current raid AI"
        return "Recover the moved raider: walk back to the fight, or free them.", "unsafe for bots today"
    if kind == "position_puzzle":
        return switched_tactic(kind, sub), "needs new raid AI"
    if kind in ("empowered", "enrage_timer"):
        return "Remove what empowers it first, or burn it fast; healers ready for bigger hits.", "needs new raid AI"
    if kind == "pet_devour":
        return "Pet classes keep their pets out of its reach (park or dismiss them) while it lives.", "needs new raid AI"
    if kind == "aggro_wipe":
        return "Off-tanks pick it up after the wipe; healers keep the casters alive.", "easy for the current raid AI"
    if kind == "linked_pack":
        return "Pull its linked pack with it on purpose: clear them first or fight them at the muster.", "easy for the current raid AI"
    if kind == "heal":
        return "Kill or interrupt whatever heals it first.", "needs new raid AI"
    if kind == "hazard":
        return "Stay out of the hazard; don't target it.", "needs new raid AI"
    if kind == "offline_change" and rule.get("switch"):
        return ("The original is restored behind " + ", ".join(rule["switch"]) + " (off). Turn it on once the raid AI has "
                "its tactic.", "off today")
    if kind == "offline_change":
        return ("Decide with the owner whether to restore the original rule behind a switch, then teach the raid its "
                "tactic.", "off today")
    if kind == "attacker_filter":
        return "Only the listed attackers can damage it: check bots are among them.", "unsafe for bots today"
    return "", "needs new raid AI"


def switched_tactic(kind, sub):
    if kind == "adds":
        return "kill the adds as they come."
    if kind == "linked_pack":
        return "expect its helpers to join."
    if kind == "teleport":
        return "recover the moved raider: walk back to the fight, or free them."
    if kind == "position_puzzle":
        return "place parties where the puzzle needs them, then fight."
    if kind == "hazard":
        return "stay out of the hazard; don't target it."
    if sub == "add_shield":
        return "kill the shielding adds first, then the boss."
    if sub == "attacker_position":
        return "attack only from inside the allowed area."
    return "follow the mechanic."


def apply_hand_notes(encounters: list[dict], notes: dict, world: World) -> list[str]:
    problems = []
    by_id = {e["id"]: e for e in encounters}
    for hand in notes.get("handEntries", []):
        e = dict(hand)
        e["hand"] = True
        files = sorted({e["file"]} | {r["file"] for r in e.get("rules", [])})
        stale = any((hand.get("scriptHashes") or {}).get(f) != script_hash(REPO / f) for f in files)
        e.pop("scriptHashes", None)
        if stale:
            e["verificationStale"] = True
        e.setdefault("eraNotes", "")
        e.setdefault("spells", [])
        e.setdefault("adds", [])
        e["verified"] = not stale
        for r in e.get("rules", []):
            r.setdefault("confidence", "read")
            r["verified"] = not stale
            r["hand"] = True
        by_id[e["id"]] = e
        encounters.append(e)
    for eid, note in notes.get("encounters", {}).items():
        enc = by_id.get(eid)
        if enc is None:
            problems.append(f"hand note for unknown encounter {eid}")
            continue
        files = sorted({enc["file"]} | {r["file"] for r in enc["rules"]})
        current = {f: script_hash(REPO / f) for f in files}
        stale = note.get("scriptHashes") and any(note["scriptHashes"].get(f) != h for f, h in current.items())
        if note.get("verified") and stale:
            enc["verificationStale"] = True
        verified = bool(note.get("verified")) and not stale
        for field in ("boss", "level", "maxHealth", "notes", "priority", "currentRaidAi", "raidAi", "levelNote", "aiTactics"):
            if field in note:
                enc[field] = note[field]
        rule_notes = note.get("rules", {})
        moved = match_moved_keys(enc, rule_notes)
        kept = []
        used = set()
        for r in enc["rules"]:
            key = short_key(enc, r)
            key = moved.get(key, key)
            rn = rule_notes.get(key)
            if rn:
                used.add(key)
                if rn.get("drop"):
                    continue
                for field in ("kind", "summary", "tactic", "difficulty", "affects", "subtype", "confidence", "note", "trigger", "count"):
                    if field in rn:
                        r[field] = rn[field]
            r["verified"] = verified
            kept.append(r)
        for key in rule_notes:
            if key not in used and not rule_notes[key].get("extra"):
                problems.append(f"{eid}: hand note for a rule the generator no longer finds: {key}")
        for extra in note.get("extraRules", []):
            r = dict(extra)
            r["hand"] = True
            r["verified"] = verified
            r.setdefault("confidence", "read")
            kept.append(r)
        enc["rules"] = kept
        enc["verified"] = verified
    for enc in encounters:
        if enc.get("aiTactics") and enc.get("raidAi") == "generic dungeon route":
            enc["raidAi"] = "generic dungeon route + tactics (EpicEncounterTactics)"
        enc.setdefault("verified", False)
        for r in enc["rules"]:
            r.setdefault("verified", False)
            if "tactic" not in r or "difficulty" not in r:
                t, d = default_tactic(enc, r)
                r.setdefault("tactic", t)
                r.setdefault("difficulty", d)
                r.setdefault("tacticSource", "default for its kind")
            r["key"] = short_key(enc, r)
    return problems


# ---------------------------------------------------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------------------------------------------------


def catalog(world: World) -> tuple[dict, list[str]]:
    encounters, raids, objectives, _ = build(world)
    apply_world(encounters)
    relink_raids(encounters, raids, objectives)
    notes = json.loads(read_text(REPO / HAND_NOTES)) if (REPO / HAND_NOTES).exists() else {}
    problems = apply_hand_notes(encounters, notes, world)
    encounters.sort(key=lambda e: (e.get("dungeon") or "", e["file"], e["lines"][0] if e.get("lines") else 0, e["id"]))
    files = sorted({p.as_posix() for p in world.sources})
    unparsed = []
    covered = {e["file"] for e in encounters} | {a["file"] for e in encounters for a in e.get("adds", [])}
    for f in files:
        src = world.sources[Path(f)]
        if f not in covered and re.search(r"\bclass\s+\w+\s*:\s*(GameEpicBoss|GameEpicNPC|GameNPC)", src.masked):
            unparsed.append(f)
    counts = defaultdict(int)
    for e in encounters:
        for r in e["rules"]:
            counts[r["kind"]] += 1
    data = {
        "schema": SCHEMA,
        "about": "Generated by source/tools/epic_encounter_catalog.py from the scripts and docs/encounters/hand-notes.json. "
                 "Do not edit by hand; edit hand-notes.json and regenerate.",
        "honesty": "A catalog only: no tactic here is implemented. Darkness Falls raid AI, Legion and the hardest level 70+ "
                   "encounters have no raid AI; they are listed for completeness.",
        "scriptsRoot": SCRIPTS.as_posix(),
        "scriptHashes": {f: world.sources[Path(f)].hash for f in files},
        "switches": read_switches(),
        "ruleKinds": RULE_KINDS,
        "difficulties": list(DIFFICULTY),
        "raids": raids,
        "neutralObjectives": {str(k): v for k, v in objectives.items()},
        "priority": notes.get("priority", []),
        "upstream": notes.get("upstream"),
        "openQuestions": notes.get("openQuestions", []),
        "counts": {
            "scripts": len(files),
            "encounters": len(encounters),
            "encountersWithRules": sum(1 for e in encounters if e["rules"]),
            "rules": sum(len(e["rules"]) for e in encounters),
            "rulesByKind": dict(sorted(counts.items())),
            "rulesVerified": sum(1 for e in encounters for r in e["rules"] if r.get("verified")),
            "rulesRead": sum(1 for e in encounters for r in e["rules"] if r.get("confidence") == "read"),
            "rulesInferred": sum(1 for e in encounters for r in e["rules"] if r.get("confidence") == "inferred"),
            "handEntries": sum(1 for e in encounters if e.get("hand")),
        },
        "scriptsWithoutEncounter": unparsed,
        "encounters": encounters,
    }
    return data, problems


def render_tactics_cs(data: dict) -> str:
    """The raid AI's tactic table (EpicEncounterTactics.Catalog), from the hand notes' aiTactics."""
    rows = []
    for e in data["encounters"]:
        t = e.get("aiTactics")
        if not t:
            continue
        adds = ", ".join(f'"{a}"' for a in t.get("requiredAdds", []))
        dies = f'"{t["diesWith"]}"' if t.get("diesWith") else "null"
        extra = ""
        if t.get("keepAlive") or t.get("blastThresholds"):
            blast = ", ".join(str(int(x)) for x in t.get("blastThresholds", []))
            extra = (f", {int(t.get('keepAlive', 0))}, " + (f"[{blast}]" if blast else "null") +
                     f", {int(t.get('blastRadius', 0))}")
        if t.get("hazards"):
            # Ground hazards (Orshom's Fire): named arguments, so the rows above keep their shape.
            hazards = ", ".join(f'"{h}"' for h in t["hazards"])
            vulnerable = f'"{t["hazardDamageType"]}"' if t.get("hazardDamageType") else "null"
            extra += (f", Hazards: [{hazards}], HazardRadius: {int(t.get('hazardRadius', 0))}, "
                      f"HazardDamageType: {vulnerable}")
        rows.append(f'            new("{e["class"]}", "{e["id"]}", [{adds}], {int(t.get("fightAtHomeRadius", 0))}, {dies}{extra}),')
    leashes = []
    for e in data["encounters"]:
        if not e.get("raids") or e.get("hand"):
            continue
        own = {e["class"], e.get("brain")}
        tether = max([r.get("range") or 0 for r in e["rules"] if r["kind"] == "tether" and r["sourceClass"] == e["class"]] +
                     [e.get("templateTetherRange") or 0])
        resets = any(r["kind"] == "reset" and r["sourceClass"] in own for r in e["rules"])
        if tether or resets:
            leashes.append(f'            new("{e["class"]}", {int(tether)}, {"true" if resets else "false"}),')
    lines = [
        "// <auto-generated>",
        "// Written by source/tools/epic_encounter_catalog.py from docs/encounters/hand-notes.json (\"aiTactics\").",
        "// Don't edit: change the hand notes and run the generator; its --check fails while this file is out of date.",
        "// </auto-generated>",
        "",
        "namespace DOL.GS",
        "{",
        "    public static partial class EpicEncounterTactics",
        "    {",
        "        /// <summary>One row per encounter with a raid tactic (boss class, catalog id, required adds, fight-at-home",
        "        /// radius, dies with).</summary>",
        "        public static readonly Tactic[] Catalog =",
        "        [",
        *rows,
        "        ];",
        "",
        "        /// <summary>Every raided encounter's leash (tether from its script or npc template, and whether it resets when",
        "        /// its aggro list empties), for the early-pull decision.</summary>",
        "        public static readonly Leash[] Leashes =",
        "        [",
        *sorted(set(leashes)),
        "        ];",
        "    }",
        "}",
    ]
    return "\n".join(lines) + "\n"


def dumps(data) -> str:
    return json.dumps(data, indent=1, ensure_ascii=False, sort_keys=False) + "\n"


def md_escape(text) -> str:
    return str(text if text is not None else "").replace("|", "\\|").replace("\n", " ")


def render_md(data: dict) -> str:
    c = data["counts"]
    lines = [
        "# Epic encounters: what the scripts do, and what bots would have to do",
        "",
        "Generated by `source/tools/epic_encounter_catalog.py` from the boss scripts in "
        "`source/server/GameServer/scripts/namedmobs/` and the hand checks in `docs/encounters/hand-notes.json`. "
        "Don't edit this file: edit the hand notes and run the generator (`--check` tells CI whether it is up to date). "
        "The machine-readable version is [epic-encounters.json](epic-encounters.json).",
        "",
        "**This is a catalog, step 1 of \"bots play epic encounters properly\". Nothing in it is implemented.** "
        "Every tactic is a proposal. Darkness Falls raid AI, Legion and the hardest level 70+ encounters have no raid AI; "
        "they are listed, marked *no raid AI*. No rule here was checked in game: \"verified\" means a person read the "
        "rule against its script.",
        "",
        f"- Scripts read: {c['scripts']}. Encounters: {c['encounters']} ({c['encountersWithRules']} with rules, "
        f"{c['handEntries']} written by hand).",
        f"- Rules: {c['rules']}; verified against the script: {c['rulesVerified']}; confidence read directly: "
        f"{c['rulesRead']}, inferred: {c['rulesInferred']}.",
        "- By kind: " + ", ".join(f"{k} {v}" for k, v in c["rulesByKind"].items()) + ".",
        "",
        "Difficulty: *easy for the current raid AI* (it would do this with small changes, or already does), "
        "*needs new raid AI* (target choice, splitting, timing the raid doesn't have), *unsafe for bots today* "
        "(bots can't recover from it yet), *off today* (an `EpicEncounterMechanics` switch disables it, or the script "
        "only ever picks real players: `GameBot` is a `GameNPC`, not a `GamePlayer`, so `GetPlayersInRadius` and "
        "`is GamePlayer` never choose a bot).",
        "",
        "## Switches that turn mechanics off today",
        "",
        "| Switch | Value | Where |",
        "|---|---|---|",
    ]
    for s in data["switches"]:
        lines.append(f"| `{s['name']}` | {s['value']} | `{s['file']}:{s['line']}` |")
    tact = [e for e in data["encounters"] if e.get("aiTactics")]
    if tact:
        lines += ["", "## Encounters with raid tactics", "",
                  "The raid AI (`EpicEncounterTactics`, autonomous gamebots only) plays these tactics, read from the hand "
                  "notes' `aiTactics` into `EpicEncounterTactics.Catalog.cs`. Tested by unit and harness tests only; not "
                  "yet seen in an overnight run (its log lines: `REALM_EXPEDITION_TACTIC`, `EPIC_TACTIC_BOT`).", "",
                  "| Encounter | Tactic |", "|---|---|"]
        for e in tact:
            lines.append(f"| {e['boss']} ({e['dungeon']}) | {md_escape(e['aiTactics'].get('summary', ''))} |")
    up = data.get("upstream")
    if up:
        lines += ["", "## Restored behind switches", "", "Where the fork had changed a mechanic for bots with no switch, the "
                  "original is back behind an `EpicEncounterMechanics` switch, off by default (the game plays as before). "
                  "Originals read from " + up["source"], "",
                  "| Encounter | Places | Original | Fork today | Switch |", "|---|---|---|---|---|"]
        for c in up["changes"]:
            lines.append(f"| {', '.join(c['encounters'])} | {c['places']} | {md_escape(c['original'])} | "
                         f"{md_escape(c['ours'])} | `{c['switch']}` |")
        lines += ["", "**Scurceol's orbs upstream:** " + up["scurceolOrbs"], "",
                  "**Helper packs upstream:** " + up["helperPacks"]]
    po = [(e, r) for e in data["encounters"] for r in e["rules"] if r.get("affects") and r["kind"] != "offline_change"]
    if po:
        lines += ["", "## Player-only mechanics (candidates to target bots later)", "",
                  "These pick real players only (`GetPlayersInRadius`, `is GamePlayer`), so bots are never chosen. They stay "
                  "player-only for now.", ""]
        for e, r in po:
            lines.append(f"- {e['boss']} ({e['dungeon']}): {r['kind']} `{Path(r['file']).name}:{r['line']}`: {md_escape(r['summary'][:160])}")
    lines += ["", "## Priority: encounters that block raids today", ""]
    by_id = {e["id"]: e for e in data["encounters"]}
    for i, p in enumerate(data["priority"], 1):
        e = by_id.get(p["id"])
        lines.append(f"### {i}. {p.get('title') or (e['boss'] if e else p['id'])}")
        lines.append("")
        if p.get("evidence"):
            lines.append("**Run evidence:** " + p["evidence"])
            lines.append("")
        if p.get("why"):
            lines.append("**Why it blocks:** " + p["why"])
            lines.append("")
        if p.get("plan"):
            lines.append("**Proposed tactic:** " + p["plan"])
            lines.append("")
        for eid in [p["id"]] + p.get("also", []):
            e = by_id.get(eid)
            if not e:
                continue
            lines.append(f"Rules of {e['boss']} (`{e['file']}`):")
            lines.append("")
            for r in e["rules"]:
                lines.append(f"- **{r['kind']}** ({'verified' if r.get('verified') else r['confidence']}): {r['summary']} "
                             f"`{Path(r['file']).name}:{r['line']}`. *{r['difficulty']}*: {r['tactic']}")
            lines.append("")
    if data.get("openQuestions"):
        lines += ["## Open questions for the owner", ""]
        lines += [f"- {q}" for q in data["openQuestions"]]
        lines.append("")
    lines += ["## All encounters", ""]
    current = None
    for e in data["encounters"]:
        if e["dungeon"] != current:
            current = e["dungeon"]
            lines += ["", f"### {current}" + (f" (region {e['region']})" if e.get("region") else ""), ""]
        bits = []
        if e.get("level") is not None:
            bits.append(f"level {e['level']}")
        elif e.get("levelText"):
            bits.append(f"level {e['levelText']}")
        if e.get("maxHealth"):
            bits.append(f"{e['maxHealth']:,} health")
        if e.get("npcTemplate"):
            bits.append(f"npc template {e['npcTemplate']}")
        if e.get("raids"):
            bits.append("raids: " + ", ".join(f"{r['id']} ({r['role']})" for r in e["raids"]))
        bits.append(f"raid AI: {e.get('raidAi')}")
        if e.get("spawnedBy"):
            bits.append("spawned by " + ", ".join(e["spawnedBy"]))
        if e.get("hand"):
            bits.append("written by hand")
        if e.get("verificationStale"):
            bits.append("**script changed since it was verified**")
        lines.append(f"#### {e['boss']} (`{e['class']}`)")
        lines.append("")
        lines.append(f"`{e['file']}` lines {e['lines'][0]}-{e['lines'][1]}. " + "; ".join(bits) + ".")
        if e.get("notes"):
            lines += ["", e["notes"]]
        lines.append("")
        if e["rules"]:
            lines.append("| Kind | Rule | Where | Confidence | Switch | Difficulty | Proposed tactic |")
            lines.append("|---|---|---|---|---|---|---|")
            for r in e["rules"]:
                conf = r["confidence"] + (", verified" if r.get("verified") else "")
                sw = ", ".join(r.get("switch") or []) or ""
                lines.append(f"| {r['kind']}{(' / ' + r['subtype']) if r.get('subtype') else ''} | {md_escape(r['summary'])}"
                             f"{(' *(' + md_escape(r['affects']) + ')*') if r.get('affects') else ''} | "
                             f"`{Path(r['file']).name}:{r['line']}` | {conf} | {sw} | {r['difficulty']} | {md_escape(r['tactic'])} |")
        else:
            lines.append("No scripted rule found (normal fight: stats, resists, spells).")
        big = [s for s in e.get("spells", []) if s.get("radius") and int_or_none(s["radius"]) and int_or_none(s["radius"]) >= 300]
        if big:
            lines.append("")
            lines.append("Area spells: " + "; ".join(
                f"{s['name']} ({s['type']}, damage {s['damage']}, radius {s['radius']})" for s in big) + ".")
        lines.append("")
    if data["scriptsWithoutEncounter"]:
        lines += ["## Scripts with no encounter entry", "",
                  "These scripts define monsters but no boss the generator recognises (helpers, trash, or adds of a boss "
                  "in another file):", ""]
        lines += [f"- `{f}`" for f in data["scriptsWithoutEncounter"]]
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def stamp(world: World, ids: list[str]) -> None:
    """Records that a person checked these encounters: verified, with the hashes of the scripts their rules live in."""
    notes = json.loads(read_text(REPO / HAND_NOTES)) if (REPO / HAND_NOTES).exists() else {}
    encounters, *_ = build(world)
    by_id = {e["id"]: e for e in encounters}
    for eid in ids:
        enc = by_id.get(eid)
        if enc is None:
            hand = next((h for h in notes.get("handEntries", []) if h["id"] == eid), None)
            if hand is None:
                raise SystemExit(f"--stamp: no encounter {eid}")
            files = sorted({hand["file"]} | {r["file"] for r in hand.get("rules", [])})
            hand["scriptHashes"] = {f: script_hash(REPO / f) for f in files}
            continue
        note = notes.setdefault("encounters", {}).setdefault(eid, {})
        files = sorted({enc["file"]} | {r["file"] for r in enc["rules"]} |
                       {r["file"] for r in note.get("extraRules", [])})
        note["verified"] = True
        note["scriptHashes"] = {f: script_hash(REPO / f) for f in files}
        moved = match_moved_keys(enc, note.get("rules", {}))
        for new_key, old_key in moved.items():
            note["rules"][new_key] = note["rules"].pop(old_key)
    notes["encounters"] = dict(sorted(notes.get("encounters", {}).items()))
    (REPO / HAND_NOTES).write_text(json.dumps(notes, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--check", action="store_true", help="fail when the committed catalog differs from a fresh run")
    ap.add_argument("--world-db", metavar="SQLITE", help="refresh docs/encounters/world-spawns.json from a release world database")
    ap.add_argument("--stamp", nargs="+", metavar="ENCOUNTER",
                    help="after checking these encounters by hand: mark them verified against their scripts as they are now")
    ap.add_argument("--dump", metavar="ENCOUNTER", help="print one encounter's generated entry (for hand checks)")
    args = ap.parse_args(argv)
    world = World()
    if args.world_db:
        write_world_snapshot(world, args.world_db)
    if args.stamp:
        stamp(world, args.stamp)
    data, problems = catalog(world)
    if args.dump:
        for e in data["encounters"]:
            if args.dump in (e["id"], e["class"]):
                print(dumps(e))
        return 0
    js, md, cs = dumps(data), render_md(data), render_tactics_cs(data)
    for p in problems:
        print("hand-notes:", p, file=sys.stderr)
    if args.check:
        ok = True
        for path, text in ((OUT_JSON, js), (OUT_MD, md), (TACTICS_CS, cs)):
            target = REPO / path
            if not target.exists() or read_text(target) != text:
                print(f"{path} is out of date: run python3 source/tools/epic_encounter_catalog.py", file=sys.stderr)
                ok = False
        stale = [e["id"] for e in data["encounters"] if e.get("verificationStale")]
        if stale:
            print("scripts changed since their hand check: " + ", ".join(stale), file=sys.stderr)
            ok = False
        if problems:
            ok = False
        return 0 if ok else 1
    (REPO / OUT_JSON).parent.mkdir(parents=True, exist_ok=True)
    (REPO / OUT_JSON).write_text(js, encoding="utf-8", newline="\n")
    (REPO / OUT_MD).write_text(md, encoding="utf-8", newline="\n")
    (REPO / TACTICS_CS).write_text(cs, encoding="utf-8", newline="\n")
    print(f"{OUT_JSON}: {data['counts']['encounters']} encounters, {data['counts']['rules']} rules "
          f"({data['counts']['rulesVerified']} verified)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
