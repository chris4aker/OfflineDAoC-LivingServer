"""Raid attempts from the #644 part-1 rows (docs/RAID-ATTEMPT-ROWS.md), shared by the raid_attempts section and
raid_postmortem.py. Not a section itself (the loader skips "_" modules).

    RAID_ATTEMPT       the pull: attempt= event= kind= region= boss="" n= size= period= sizePct= sf= roles= classes= ...
    RAID_ATTEMPT_END   the end:  attempt= outcome= secs= engaged= fallen= died= left= deaths= health= lowest=
    RAID_DEATH         a death:  attempt= t= idx= kind= player= class= role= level= killerKind= killer="" source= ...

Rows are joined on attempt=. The id's count starts again at 1 when the server restarts, so the same id can stand for two
pulls in a log set: a RAID_ATTEMPT row for an id already pulled opens a new attempt (shown as "<id> (run 2)"), and later
deaths and the end go to the newest pull of that id. "-" is an unknown count (None here), never 0.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from living_report_sections._util import fields, stamp, tag

ROWS = ["RAID_ATTEMPT", "RAID_ATTEMPT_END", "RAID_DEATH"]
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
        else:
            for_id(aid).deaths.append((stamp(line), f))
    return out


def killer_of(death: dict) -> str:
    """"boss: Name", "add: Name", or the kind alone for a raider, a pet, the environment or none."""
    kind = death.get("killerKind", "?")
    name = death.get("killer", "-")
    return f"{kind}: {name}" if name and name != "-" else kind


def mechanics_of(death: dict) -> list[str]:
    value = death.get("mechanic", "-")
    return [] if not value or value == "-" else [m for m in value.split("+") if m]
