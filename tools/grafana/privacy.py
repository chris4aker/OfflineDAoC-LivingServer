"""Which rows ship and which of their fields are dropped (issue #628; AGENTS.md "Protect player data").

A field is dropped when the code that fills it reads a name: a player's, a gamebot's, a companion's, a guild's or an
account's. Free text (chat lines) and network addresses are dropped too. The rules below read the expressions
rows.py found; row-policy.json adds the reviewed exceptions (extra drops, keeps, excluded rows, name-free rows).
Gamebot names are dropped as well: they are not private, but a field that holds a bot's name in one row holds
the player's in another (target=, killer=, from=), and the dashboards count rows, they don't need names.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

from rows import Row

HERE = Path(__file__).resolve().parent
POLICY = HERE / "row-policy.json"
PENDING = HERE / "pending-rows.json"

# Fields dropped whatever fills them: free text, accounts, network addresses.
ALWAYS = {"text", "say", "reply", "msg", "words", "quote", "account", "accountName", "addr", "address", "ip",
          "password", "user", "username", "email", "nickname"}
# A field whose expression could not be read is dropped when its key names a person.
PERSON_KEYS = {"player", "playerName", "killer", "victim", "leader", "owner", "speaker", "seller", "buyer", "bot",
               "botName", "target", "guild", "member", "members", "recruit", "carrier", "hirer", "who", "from", "to",
               "name", "names", "friend", "rival", "nemesis", "host", "hosting", "partner", "caller", "holder"}
# A .Name read on one of these is a person's name; on anything else (a keep, an item, a hub) it is world data, unless
# the field's own key names a person (target={x.Name}).
_PERSON_OBJECT = (r"\w*?(?:player|bot|killer|victim|leader|owner|member|speaker|seller|buyer|recruit|carrier|hirer|"
                  r"living|friend|rival|caller|client|account|companion|helper|target|attacker|caster|breaker|"
                  r"winner|loser|sender|receiver|recipient|partner|teller|follower|puller|healer|main|other)"
                  r"(?:s|Name|Body|Player|Living)?|op|who|from|to|p|b|m|gp|pl")
_NAME_MEMBER = re.compile(r"(?P<obj>[A-Za-z_][\w]*)(?:\?)?\.(?:Name|LastName|GuildName|AccountName|PlayerName)\b")
_NAME_PROPERTY = re.compile(r"\b\w*(?:Player|Seller|Buyer|Leader|Owner|Killer|Victim|Speaker|Account|Guild|Bot|Target|"
                            r"Member|Carrier|Hirer|Recruit|Friend|Rival|Nemesis|Caller)Names?\b")
_PERSON_VARS = {"bot", "botName", "player", "playerName", "speaker", "killer", "killerName", "victim",
                "leader", "leaderName", "owner", "ownerName", "target", "targetName", "member", "members", "seller",
                "buyer", "guild", "guildName", "account", "recruit", "hirer", "carrier", "who", "names", "roster",
                "friend", "rival", "nemesis", "host", "caller", "partner", "playerNames", "botNames", "memberNames"}
_WRAPPER = re.compile(r"^(?:[\w.]*\b(?:Field|Quote|Clean|ForLog|Escape|Safe|Short|Trim|Text|Sanitize|Format)\()+(.*?)\)*$")


def expression_reads_a_name(expr: str, key: str = "") -> bool:
    """Whether the code filling the field `key` reads a person's name (a player, a bot, a companion, a guild)."""
    for m in _NAME_MEMBER.finditer(expr):
        if re.fullmatch(_PERSON_OBJECT, m.group("obj"), re.I) or key in PERSON_KEYS:
            return True
    if _NAME_PROPERTY.search(expr) or re.search(r"\.(?:Speaker|Sender|Recipient|Addressee|Hirer)\b", expr):
        return True
    core = expr.strip()
    w = _WRAPPER.match(core)
    if w:
        core = w.group(1)
    core = re.split(r"\s*\?\?\s*", core)[0].strip("() ")
    if core in _PERSON_VARS:
        return True
    if re.search(r"string\.Join\([^)]*\b(?:names|roster|members|Names|Members)\b", expr):
        return True
    return False


# ---- Fail closed ------------------------------------------------------------------------------------------------------
# expression_reads_a_name above recognises the shapes a person's name is known to come in. It cannot see every shape
# (`{Name}` inside GamePlayer, `characterName`, `MainOf(bot)?.Name`), and a miss there used to ship the name. The rules
# below work the other way round: any name or text the expression reads is dropped unless it is on a reviewed list of
# world data (a keep, a zone, an item, a class, a spell, an enum's name). A new shape is dropped until someone reviews it.
# What this still cannot see: a helper whose name does not say it returns a name (Label(player)) when its argument is not
# a person-like variable. Reviewers of a new row check its helpers; row-policy.json extra_drop covers what they find.

# Field keys whose value is world data, whatever object's .Name fills it (keep={nearest.Name}, relic={Name}).
WORLD_KEYS = {"keep", "keeps", "relic", "zone", "region", "item", "items", "boss", "hub", "spell", "style", "weapon",
              "recipe", "lane", "lanes", "gate", "gates", "place", "door", "capital", "encounter", "court",
              "destination", "passage", "npc", "mob", "monster", "lord", "service", "porter", "trainer", "merchant",
              "master", "ticket", "battleground", "bg", "town", "camp", "pad", "ability", "skill", "class", "realm",
              "near", "beside", "danger", "add", "pet", "servant", "petTarget", "pending", "raid", "court"}
# Objects whose .Name is world data: NPCs the world spawns, places, items, spells and classes. Compared without case and
# without a leading underscore. Not here on purpose: anything that can be a player (target, subject, corpse, killer,
# blocker, ahead, nearest, first, best, designated, inviter, challenger, gm, crafter, supplier).
SAFE_NAME_OBJECTS = {"keep", "central", "relic", "carried", "item", "purchased", "replaced", "boss", "hub", "zone",
                     "spell", "resurrection", "style", "nextcombatstyle", "weapon", "usable", "ability", "lane", "gate",
                     "place", "door", "capital", "anchor", "location", "bracket", "here", "ticket", "recipe", "job",
                     "characterclass", "rezclass", "raise", "trainer", "merchant", "master", "porter", "broker",
                     "service", "servicenpc", "servant", "pet", "npc", "spawn", "mob", "lord", "home", "townport",
                     "camp"}
# Members ending in Name that hold world data (a monster's, a zone's, a class's, an item's name; an enum key).
SAFE_NAME_MEMBERS = {"MonsterName", "ZoneName", "ClassName", "ItemName", "ProductName", "KeyName", "BracketName",
                     "RendezvousName", "AnchorName"}
# Calls ending in Name that return an enum's or a realm's word, or an id (RvrEngagementRuntime.GroupName: the leader's
# database id).
SAFE_NAME_CALLS = {"RealmToName", "TierName", "KindName", "HowName", "OutcomeName", "ChannelName", "TypeName",
                   "ReasonName", "SkillName", "GroupName", "RealmName", "StageName", "PhaseName", "ModeName"}
# Locals ending in Name that are reviewed (groupName is GroupName's id).
SAFE_NAME_LOCALS = {"groupName"}
# Free text: a chat line, an exception's message (can hold a user folder), a stack trace.
_FREE_TEXT = re.compile(r"(?<![\w.])(?:message|line|text|lastMessage|words|quote|say|reply)\b"
                        r"(?!\s*\(|\??\.(?!Replace|Trim|Substring|ToLower|ToUpper|Normalize))"
                        r"|\.(?:Message|Text|Line|Words|StackTrace)\b|\bStackTrace\b")
_STRING_LITERAL = re.compile(r'"(?:[^"\\]|\\.)*"')
_MEMBER_NAME = re.compile(r"(?P<before>[\w\])]?)\??\.(?P<member>[A-Z]\w*?Name|Name)\b(?!\s*\()")
_LOCAL_NAME = re.compile(r"(?<![\w.])(?P<local>(?:[A-Za-z_]\w*?)?[Nn]ame)\b(?!\s*\()")
_CALL_NAME = re.compile(r"(?<![\w])(?P<call>\w*Name)\s*\(")
_TYPE_NAME = re.compile(r"GetType\(\)\??\.Name\b|\bnameof\(")


def _object_before(expr: str, dot: int) -> str:
    """The identifier just before the `.` at dot (`taken.Keep.Name` -> Keep), or '' after a call or an index."""
    i = dot
    if i > 0 and expr[i - 1] == "?":
        i -= 1
    j = i
    while j > 0 and (expr[j - 1].isalnum() or expr[j - 1] == "_"):
        j -= 1
    return expr[j:i]


def unreviewed_reads(expr: str, key: str = "") -> list[str]:
    """What in the expression may carry a person's name or free text and is not reviewed world data. Empty: safe."""
    found: list[str] = []
    if _FREE_TEXT.search(_STRING_LITERAL.sub('""', expr)):
        found.append("free text")
    clean = _TYPE_NAME.sub("", expr)
    world_key = key in WORLD_KEYS
    for m in _MEMBER_NAME.finditer(clean):
        member = m.group("member")
        if member in SAFE_NAME_MEMBERS:
            continue
        obj = _object_before(clean, m.start("member") - 1).lstrip("_").lower()
        if member == "Name" and (obj in SAFE_NAME_OBJECTS or world_key):
            continue
        found.append(f"{obj or '(...)'}.{member}")
    for m in _LOCAL_NAME.finditer(clean):
        local = m.group("local")
        if local in SAFE_NAME_LOCALS or world_key:
            continue
        found.append(local)
    for m in _CALL_NAME.finditer(clean):
        call = m.group("call")
        if call in SAFE_NAME_CALLS or world_key:
            continue
        found.append(call + "()")
    return found


# A file path can hold the user's folder name (C:\\Users\\<name>\\...), so it never ships.
FILE_KEYS = {"path", "file", "dir", "folder", "directory", "filename"}
_FILE_PATH = re.compile(r"\.(?:Path|FullName|DirectoryName)\b|\bPath\.(?:Combine|GetFullPath|GetTempPath)\b|BaseDirectory|"
                        r"Environment\.(?:CurrentDirectory|GetFolderPath)")

_RISKY_UNREAD_KEY = re.compile(r"name|message|text|line|note|example|body|journal|who|by$|from|^to$", re.I)


@dataclass
class Policy:
    exclude: dict[str, str]
    name_free: dict[str, str]
    extra_drop: dict[str, dict[str, str]]
    keep: dict[str, dict[str, str]]
    json_keys: list[str]
    frequency: dict[str, str]
    pending: dict[str, dict]


def load_policy(policy: Path = POLICY, pending: Path = PENDING) -> Policy:
    p = json.loads(policy.read_text(encoding="utf-8"))
    q = json.loads(pending.read_text(encoding="utf-8"))
    return Policy(p["exclude_rows"], p["name_free_rows"], p["extra_drop"], p["keep"], p["json_name_keys"],
                  p.get("frequency", {}), q["rows"])


def dropped_fields(row: Row, policy: Policy) -> dict[str, str]:
    """The row's fields the pipeline removes, each with why."""
    if row.marker in policy.name_free:
        return {}
    out: dict[str, str] = {}
    keep = policy.keep.get(row.marker, {})
    for key, exprs in sorted(row.fields.items()):
        if key in keep:
            continue
        if key in ALWAYS:
            out[key] = "free text or account data"
        elif key in FILE_KEYS or any(_FILE_PATH.search(e) for e in exprs):
            out[key] = "a file path (can hold the user's folder name)"
        elif any(expression_reads_a_name(e, key) for e in exprs):
            out[key] = "filled from a name"
        elif any(unreviewed_reads(e, key) for e in exprs):
            reads = sorted({r for e in exprs for r in unreviewed_reads(e, key)})
            out[key] = "free text" if reads == ["free text"] else "reads " + ", ".join(reads) + ", not reviewed world data"
        elif not exprs and (key in PERSON_KEYS or _RISKY_UNREAD_KEY.search(key)):
            out[key] = "may name a person or hold text; its source was not read"
    for key, why in policy.extra_drop.get(row.marker, {}).items():
        out[key] = why
    return out


def shipped(row: Row, policy: Policy) -> tuple[bool, str]:
    """Whether a row in code goes through the filter, and why not."""
    if row.marker in policy.exclude:
        return False, policy.exclude[row.marker]
    if row.log not in ("server.log", "detail.log"):
        return False, f"written to {row.log}, which is not tailed"
    if not row.fields and not row.json:
        return False, "free text, no key=value fields"
    return True, ""
