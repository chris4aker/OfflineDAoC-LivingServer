"""Raid attempts from the #644 part-1 rows (docs/RAID-ATTEMPT-ROWS.md), shared by the raid_attempts section and
raid_postmortem.py. Not a section itself (the loader skips "_" modules).

    RAID_ATTEMPT       the pull: attempt= event= kind= region= boss="" n= size= period= sizePct= sf= roles= classes= ...
    RAID_ATTEMPT_END   the end:  attempt= outcome= secs= engaged= fallen= died= left= deaths= health= lowest=
    RAID_DEATH         a death:  attempt= t= idx= kind= player= class= role= level= killerKind= killer="" source= ...
    RAID_FIGHT_SAMPLE  every 15 s of a pull (parts 2-3): attempt= t= bossPct= phase= adds= alive=T:H:B:A engaged=T:H:B:A
                       healerPow= healersLow= onTank= tgtKind= medDist= mezzed= stunned=
    RAID_MECHANIC_FIRE each signal of a pull and its aftermath (parts 3-5): attempt= boss="" mechanic= t= radius=
                       exposedAtCall= within= hit= died= avoided= called= leadMs= reactMedianMs= damage=
    RAID_THREAT        each change of the boss's target (diagnostics switch): attempt= boss="" t= from= to= toKind= why= heldMs=
    RAID_ROLE_TOTALS   one per role at the end (diagnostics switch): attempt= role= raiders= dealtBoss= dealtAdds= took= healed=
                       overheal= deaths= cc= interrupts= powerOuts= classes=
    RAID_FUNNEL        per raid, not per attempt (parts 2-3): event= stage=start|end asked= recruited= parties= atHub= staged=
                       atBoss= lost=route:n,travel:n,died:n,logout:n,released:n,other:n hubS= stageS= bossS=

Rows are joined on attempt=. The id's count starts again at 1 when the server restarts, so the same id can stand for two
pulls in a log set: a RAID_ATTEMPT row for an id already pulled opens a new attempt (shown as "<id> (run 2)"), and later
deaths and the end go to the newest pull of that id. "-" is an unknown count (None here), never 0.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from living_report_sections._util import FIELD, fields, message, stamp, tag

ROWS = ["RAID_ATTEMPT", "RAID_ATTEMPT_END", "RAID_DEATH", "RAID_FIGHT_SAMPLE", "RAID_MECHANIC_FIRE", "RAID_THREAT",
        "RAID_ROLE_TOTALS"]
FUNNEL = "RAID_FUNNEL"
ROLES = ("tank", "healer", "buffer", "attacker")
FAILED = ("wipe", "reset", "skirmish")


def count(value):
    """An int, or None for "-", missing or not a number."""
    if value in (None, "", "-"):
        return None
    try:
        return int(float(value))
    except ValueError:
        return None


@dataclass
class Attempt:
    id: str                       # the attempt= value
    key: str                      # id, or "id (run N)" when the id was pulled again after a restart
    pull: dict = field(default_factory=dict)   # the RAID_ATTEMPT fields ({} when the pull row is missing)
    pulled_at: str = ""           # its "MM/dd HH:mm:ss"
    end: dict | None = None       # the RAID_ATTEMPT_END fields
    ended_at: str = ""
    deaths: list = field(default_factory=list)  # (stamp, fields) of each RAID_DEATH, in log order
    samples: list = field(default_factory=list)  # (stamp, fields) of each RAID_FIGHT_SAMPLE, in log order
    mechanics: list = field(default_factory=list)  # (stamp, fields) of each RAID_MECHANIC_FIRE
    threats: list = field(default_factory=list)  # (stamp, fields) of each RAID_THREAT
    roles: list = field(default_factory=list)  # (stamp, fields) of each RAID_ROLE_TOTALS

    @property
    def event(self):
        return self.pull.get("event") or self.id.rsplit(".", 1)[0]

    @property
    def boss(self):
        return self.pull.get("boss") or "?"

    @property
    def outcome(self):
        return self.end.get("outcome", "?") if self.end else "open"

    @property
    def secs(self):
        return count(self.end.get("secs")) if self.end else None


def attempts(lines) -> list[Attempt]:
    """Every attempt in the lines, in pull order. Rows without attempt= are ignored."""
    out: list[Attempt] = []
    latest: dict[str, Attempt] = {}
    runs: dict[str, int] = {}

    def for_id(aid: str) -> Attempt:
        if aid not in latest:   # a death or end before (or without) its pull row: an attempt with no pull fields
            runs[aid] = 1
            latest[aid] = Attempt(aid, aid)
            out.append(latest[aid])
        return latest[aid]

    for line in lines:
        kind = tag(line)
        if kind not in ROWS:
            continue
        f = fields(line)
        aid = f.get("attempt")
        if not aid or aid == "-":
            continue
        if kind == "RAID_ATTEMPT":
            current = latest.get(aid)
            if current is not None and (current.pull or current.end):
                runs[aid] += 1
                current = Attempt(aid, f"{aid} (run {runs[aid]})")
                latest[aid] = current
                out.append(current)
            elif current is None:
                current = for_id(aid)
            current.pull, current.pulled_at = f, stamp(line)
        elif kind == "RAID_ATTEMPT_END":
            a = for_id(aid)
            a.end, a.ended_at = f, stamp(line)
        elif kind == "RAID_FIGHT_SAMPLE":
            for_id(aid).samples.append((stamp(line), f))
        elif kind == "RAID_MECHANIC_FIRE":
            for_id(aid).mechanics.append((stamp(line), f))
        elif kind == "RAID_THREAT":
            for_id(aid).threats.append((stamp(line), f))
        elif kind == "RAID_ROLE_TOTALS":
            for_id(aid).roles.append((stamp(line), f))
        else:
            for_id(aid).deaths.append((stamp(line), f))
    return out


def split(value):
    """A T:H:B:A field as {"tank": n, "healer": n, "buffer": n, "attacker": n}, or None when it is not four counts."""
    parts = (value or "").split(":")
    if len(parts) != 4:
        return None
    counts = [count(p) for p in parts]
    return None if None in counts else dict(zip(ROLES, counts))


def total(value):
    """The sum of a T:H:B:A field, or None."""
    s = split(value)
    return None if s is None else sum(s.values())


def lost(value) -> dict:
    """RAID_FUNNEL's lost=route:n,travel:n,... as {"route": n, ...}; unreadable parts are left out."""
    out = {}
    for part in (value or "").split(","):
        name, _, n = part.partition(":")
        if name and count(n) is not None:
            out[name] = count(n)
    return out


def funnels(lines) -> list[tuple[str, dict, dict]]:
    """(event, start fields or {}, end fields or {}) for each raid, in order of its first RAID_FUNNEL row. A start row after an
    end row of the same event is a new raid (the next expedition there)."""
    out: list[tuple[str, dict, dict]] = []
    open_: dict[str, int] = {}
    for line in lines:
        if tag(line) != FUNNEL:
            continue
        f = fields(line)
        event, stage = f.get("event"), f.get("stage")
        if not event or event == "-":
            continue
        f = dict(f, at=stamp(line))
        if stage == "start":
            open_[event] = len(out)
            out.append((event, f, {}))
        elif stage == "end":
            i = open_.pop(event, None)
            if i is None:   # the log starts after the raid began
                out.append((event, {}, f))
            else:
                out[i] = (event, out[i][1], f)
    return out


def killer_of(death: dict) -> str:
    """"boss: Name", "add: Name", or the kind alone for a raider, a pet, the environment or none."""
    kind = death.get("killerKind", "?")
    name = death.get("killer", "-")
    return f"{kind}: {name}" if name and name != "-" else kind


def mechanics_of(death: dict) -> list[str]:
    value = death.get("mechanic", "-")
    return [] if not value or value == "-" else [m for m in value.split("+") if m]


# The fields of each row, in the order the server's Format* functions write them (docs/RAID-ATTEMPT-ROWS.md). The tests hold
# them to the rows the C# tests pin, so a change to a row fails there. RAID_ATTEMPT may end with its optional fields.
KEYS = {
    "RAID_ATTEMPT": ["attempt", "event", "kind", "region", "boss", "n", "size", "period", "sizePct", "sf", "roles", "classes",
                     "players", "build"],
    "RAID_ATTEMPT_END": ["attempt", "outcome", "secs", "engaged", "fallen", "died", "left", "deaths", "health", "lowest"],
    "RAID_DEATH": ["attempt", "t", "idx", "kind", "player", "class", "role", "level", "killerKind", "killer", "source", "spellId",
                   "mechanic", "dist", "healersIn", "attackers"],
    "RAID_FIGHT_SAMPLE": ["attempt", "t", "bossPct", "phase", "adds", "alive", "engaged", "healerPow", "healersLow", "onTank",
                          "tgtKind", "medDist", "mezzed", "stunned"],
    "RAID_MECHANIC_FIRE": ["attempt", "boss", "mechanic", "t", "radius", "exposedAtCall", "within", "hit", "died", "avoided",
                           "called", "leadMs", "reactMedianMs", "damage"],
    "RAID_THREAT": ["attempt", "boss", "t", "from", "to", "toKind", "why", "heldMs"],
    "RAID_ROLE_TOTALS": ["attempt", "role", "raiders", "dealtBoss", "dealtAdds", "took", "healed", "overheal", "deaths", "cc",
                         "interrupts", "powerOuts", "classes"],
}
OPTIONAL = {"RAID_ATTEMPT": ["prev", "wipeToPullS", "gatherS"]}


def keys_problem(kind: str, keys: list[str]) -> str | None:
    """Why a row's field names are not its row's (None when they are): KEYS in order, then only its optional fields."""
    want = KEYS.get(kind)
    if want is None:
        return None
    if keys[:len(want)] != want:
        return f"fields {' '.join(keys)}; the server writes {' '.join(want)}"
    rest = keys[len(want):]
    optional = OPTIONAL.get(kind, [])
    if any(k not in optional for k in rest) or rest != [k for k in optional if k in rest]:
        return f"extra fields {' '.join(rest)}"
    return None


def check(lines) -> list[str]:
    """Problems with the raid rows of a log (raid_postmortem.py --check; the server job reads the sims' rows with it): every
    row has its row's fields in order, every attempt has a pull and an end, its RAID_DEATH rows are the end's deaths= and
    died + left is fallen. An attempt still open when the log stops is no problem in a server log, so only the sims' attempts
    (build=sim) must have their end."""
    problems = []
    for line in lines:
        kind = tag(line)
        if kind in KEYS:
            rest = message(line).split(" ", 1)
            names = [m.group(1) for m in FIELD.finditer(rest[1] if len(rest) == 2 else "")]
            why = keys_problem(kind, names)
            if why:
                problems.append(f"{kind} {stamp(line)}: {why}")
    for a in attempts(lines):
        sim = a.pull.get("build") == "sim"
        if not a.pull:
            problems.append(f"{a.key}: rows but no RAID_ATTEMPT")
        if a.end is None:
            if sim:
                problems.append(f"{a.key}: no RAID_ATTEMPT_END")
            continue
        deaths = count(a.end.get("deaths"))
        if deaths is not None and deaths != len(a.deaths):
            problems.append(f"{a.key}: deaths={deaths} but {len(a.deaths)} RAID_DEATH rows")
        died, left, fallen = (count(a.end.get(k)) for k in ("died", "left", "fallen"))
        if None not in (died, left, fallen) and died + left != fallen:
            problems.append(f"{a.key}: died {died} + left {left} is not fallen {fallen}")
    return problems
