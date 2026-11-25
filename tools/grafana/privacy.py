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
        elif any(expression_reads_a_name(e, key) for e in exprs):
            out[key] = "filled from a name"
        elif not exprs and key in PERSON_KEYS:
            out[key] = "names a person; its source was not read"
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
