#!/usr/bin/env python3
"""The "RvR hour": one server.log turned into an hour-by-hour report of the realm war (issue #505, evidence.md §5.1).

    python3 source/tools/rvr_hour.py runtime/server/logs/server.log
    python3 source/tools/rvr_hour.py server.log --json
    python3 source/tools/rvr_hour.py server.log --fights          # every fight's timeline
    python3 source/tools/rvr_hour.py server.log --fight 12        # one fight
    python3 source/tools/rvr_hour.py server.log --sieges          # every siege's timeline
    python3 source/tools/rvr_hour.py server.log --forces          # every battlegroup's and zerg's timeline
    python3 source/tools/played_check_log.py server.log --rvr-hour   # the same report from the played check

Per hour and per realm: bots in the war, perception (notices by distance band, median time_since_first_los_ms), fights
by size and outcome (with the busiest 4,000-unit cell's share), kills on and by the player and by what, called-target
kills, assist calls, fights by their biggest side (one group, two, three, a zerg) and the multi-group forces that formed
(battlegroups, zergs, battleground battlegroups, the 3,000-unit clusters; #599), roaming checks, sieges begun / finished / stalled with reasons, travel stalls, teleports in the
player's sight, the player's idle minutes in the war, his /rvrodd flags, and the decision trace (RVR_DECIDE) by layer and
rule. Every figure is stamped with the log section's FORK_BUILD commit.

It reads the 1.2.3 record rows (RVR_FIGHT_OPEN/END, RVR_PLAYER_*, RVR_SIEGE_*, RVR_TELEPORT, RVR_HOUR, RVR_DECIDE, ...)
when the log has them. On an older log (1.2.2 and before) it rebuilds what it can from the tags those builds wrote:
fights from RVR_BOT_KILLED clusters, sieges from BG_SIEGE_STARTED / BG_KEEP_STATUS / RVR_SIEGE / RVR_EVENT_*; a figure
it cannot make says which row it needs. Logs stay out of git (AGENTS.md); player names are masked as played_check_log.py
masks them.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import statistics
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path

KV_RE = re.compile(r'(\w+)=("([^"]*)"|\S+)')
MASK_RE = re.compile(r'\b(player|account|owner|guest)=("[^"]+"|[^\s"]+)')
REALMS = ("Albion", "Midgard", "Hibernia")
BANDS = ((0, 1000), (1000, 2000), (2000, 3000), (3000, 10 ** 9))
CELL = 4000
FIGHT_GAP_S = 30            # a rebuilt fight ends after this long without a kill (as RvrFightLedger.IdleEndMilliseconds)
FIGHT_RADIUS = 3000         # a kill joins a rebuilt fight within this of its centre (RvrFightLedger.JoinRadius)
SIZE_BUCKETS = ((1, 2, "1v1"), (3, 8, "small (3-8)"), (9, 24, "group (9-24)"), (25, 10 ** 6, "zerg (25+)"))
# A fight by its biggest side, in groups of 8 (#599): one group, a 2-group battlegroup, 3 groups or a small zerg, a zerg.
SIDE_BUCKETS = ((1, 2, "1-2"), (3, 8, "3-8"), (9, 16, "9-16"), (17, 24, "17-24"), (25, 10 ** 6, "25+"))
# The multi-group force rows (#599 steps 1, 3 and 4), by the name the report gives them.
FORCE_TAGS = {"RVR_FORCE_BATTLEGROUP": "battlegroup", "RVR_FORCE_ZERG": "zerg", "RVR_FORCE_BG_BATTLEGROUP": "bg_battlegroup"}
# Per-bot traces that name a keep: counted on the siege, not listed in its timeline.
NOISY_SIEGE_TAGS = {"BG_STORM_TRACE", "BG_LORD_PATH", "BG_TOWER_FOOT", "RVR_LORD_FORMATION", "RVR_KEEP_TRAVEL_STALL",
                    "BG_RALLY_CANDIDATES", "BG_WAVE_STATUS", "BG_RALLY_STATUS"}
WAR_REGIONS = {1, 100, 200, 163, 165, 249, 250, 251, 252, 253}   # old frontiers, NF, DF, battlegrounds


# ---- Lines -----------------------------------------------------------------------------------------------------------

def parse_head(line: str):
    if len(line) < 17 or line[2] != "/" or line[5] != " " or line[8] != ":" or line[11] != ":" or line[14:17] != " | ":
        return None
    try:
        return int(line[0:2]), int(line[3:5]), int(line[6:8]), int(line[9:11]), int(line[12:14]), line[17:]
    except ValueError:
        return None


def split_rest(rest: str):
    level = ""
    if len(rest) >= 6 and rest[3:6] == " | ":
        level, rest = rest[:3], rest[6:]
    logger, sep, message = rest.partition(" | ")
    return (level, logger.strip(), message) if sep else (level, rest.strip(), "")


def kv(message: str) -> dict:
    return {k: (q if v.startswith('"') else v) for k, v, q in KV_RE.findall(message)}


def num(value, default=None):
    try:
        return float(str(value).rstrip("%"))
    except (TypeError, ValueError):
        return default


def xy(value):
    parts = str(value or "").split(":")[-1].split(",")
    try:
        return float(parts[0]), float(parts[1])
    except (ValueError, IndexError):
        return None


KILLER_RE = re.compile(r'\bkiller=("[^"]*"|\S+)')


def mask(message: str) -> str:
    """Player names out, as played_check_log.py does (and the killer's when the killer was the player)."""
    text = MASK_RE.sub(lambda m: f"{m.group(1)}=<player>", message.strip())
    if "killerType=GamePlayer" in text:
        text = KILLER_RE.sub("killer=<player>", text)
    return text


def median(values):
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


@dataclass
class Line:
    t: dt.datetime
    tag: str
    f: dict
    text: str
    build: str


def read(path: Path, year: int, since: dt.datetime | None = None):
    """The RvR-relevant lines of a log, in order, each with its section's build."""
    build = "before-FORK_BUILD"
    last = None
    wanted = re.compile(r"^(RVR_|BG_|SIEGE_|FORK_BUILD|AUTONOMOUS_PVP_STALEMATE|AUTONOMOUS_UNREACHABLE_TARGET|BOT_SOLO_BAIL)")
    with path.open("r", encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            head = parse_head(raw)
            if not head:
                continue
            month, day, h, m, s, rest = head
            _, _, message = split_rest(rest)
            tag = message.split(" ", 1)[0]
            if not wanted.match(tag):
                continue
            try:
                t = dt.datetime(year, month, day, h, m, s)
            except ValueError:
                continue
            if last and t < last - dt.timedelta(days=200):
                t = t.replace(year=t.year + 1)
            last = t
            f = kv(message)
            if tag == "FORK_BUILD":
                build = f"{f.get('name', '?')}@{f.get('commit', '?')[:7]}"
            if since and t < since:
                continue
            yield Line(t, tag, f, message.rstrip("\n"), build)


# ---- Fights ----------------------------------------------------------------------------------------------------------

@dataclass
class Fight:
    id: str
    region: int
    x: float
    y: float
    start: dt.datetime
    end: dt.datetime
    build: str
    source: str                      # "record" (RVR_FIGHT_*) or "rebuilt" (RVR_BOT_KILLED clusters)
    members: dict = field(default_factory=lambda: defaultdict(set))
    deaths: Counter = field(default_factory=Counter)
    winner: str = "none"
    held: str = "none"
    first_death_s: float | None = None
    first_cc_s: float | None = None
    player_in: bool = False
    player_deaths: int = 0
    player_kills: int = 0
    called_kills: int = 0
    kills: int = 0
    events: list = field(default_factory=list)
    groups: dict = field(default_factory=lambda: defaultdict(set))   # realm -> group ids (leader ids, RVR_FIGHT_* groups=)

    @property
    def size(self) -> int:
        return sum(len(v) for v in self.members.values())

    @property
    def sides(self) -> dict:
        return {realm: len(m) for realm, m in self.members.items() if realm in REALMS and m}

    @property
    def biggest_side(self) -> int:
        return max(self.sides.values(), default=0)

    @property
    def matchup(self) -> str:
        """The sides biggest first, as '16v8' ('16v8v4' in a three-way fight)."""
        return "v".join(str(n) for n in sorted(self.sides.values(), reverse=True)) or "none"

    @property
    def most_groups(self) -> int:
        """The most groups on one side (from groups=; 0 when the log doesn't name them)."""
        return max((len(g) for realm, g in self.groups.items() if realm in REALMS), default=0)

    @property
    def seconds(self) -> float:
        return (self.end - self.start).total_seconds()


def size_bucket(size: int) -> str:
    for low, high, name in SIZE_BUCKETS:
        if low <= size <= high:
            return name
    return "none"


def side_bucket(size: int) -> str:
    for low, high, name in SIDE_BUCKETS:
        if low <= size <= high:
            return name
    return "none"


def parse_groups(value) -> dict:
    """'Albion:10+11;Midgard:20' -> {'Albion': {'10', '11'}, 'Midgard': {'20'}} (RVR_FIGHT_OPEN / RVR_FIGHT_END groups=)."""
    out = defaultdict(set)
    for part in str(value or "").split(";"):
        realm, sep, ids = part.partition(":")
        if sep and realm:
            out[realm].update(g for g in ids.split("+") if g)
    return out


def outcome(sizes: dict, deaths: Counter) -> str:
    """As RvrFightLedger.Outcome: the realm with the fewest deaths, 'even' on a tie, 'none' when nobody died."""
    if sum(deaths.values()) == 0 or not sizes:
        return "none"
    ranked = sorted(sizes, key=lambda r: deaths.get(r, 0))
    fewest = deaths.get(ranked[0], 0)
    return "even" if sum(1 for r in ranked if deaths.get(r, 0) == fewest) > 1 else ranked[0]


def rebuild_fights(kills: list[Line]) -> list[Fight]:
    """Fights from RVR_BOT_KILLED alone (older logs): kills in one region within FIGHT_RADIUS and FIGHT_GAP_S of a fight."""
    fights: list[Fight] = []
    live: list[Fight] = []
    for line in kills:
        f = line.f
        at = xy(f.get("at"))
        region = int(num(f.get("region"), 0) or 0)
        if not at:
            continue
        live = [fight for fight in live if (line.t - fight.end).total_seconds() <= FIGHT_GAP_S]
        fight = None
        for candidate in live:
            if candidate.region == region and (candidate.x - at[0]) ** 2 + (candidate.y - at[1]) ** 2 <= FIGHT_RADIUS ** 2:
                fight = candidate
                break
        if fight is None:
            fight = Fight(f"r{len(fights) + 1}", region, at[0], at[1], line.t, line.t, line.build, "rebuilt")
            fights.append(fight)
            live.append(fight)
        n = fight.kills + 1
        fight.x += (at[0] - fight.x) / min(n, 20)
        fight.y += (at[1] - fight.y) / min(n, 20)
        fight.end = line.t
        add_kill(fight, line)
    for fight in fights:
        sizes = {r: len(m) for r, m in fight.members.items()}
        fight.winner = outcome(sizes, fight.deaths)
        fight.held = fight.winner if fight.winner in REALMS else "none"
    return fights


def add_kill(fight: Fight, line: Line):
    f = line.f
    victim_realm, killer_realm = f.get("realm", "None"), f.get("killerRealm", "None")
    fight.members[victim_realm].add(f.get("victim", "?"))
    killer_type = f.get("killerType", "")
    killer = "player" if killer_type == "GamePlayer" else f.get("killer", "?")
    fight.members[killer_realm].add(killer)
    fight.deaths[victim_realm] += 1
    fight.kills += 1
    for realm, group in ((victim_realm, f.get("victimGroup")), (killer_realm, f.get("killerGroup"))):
        if group and group not in ("0", "-", "none"):
            fight.groups[realm].add(group)
    if fight.first_death_s is None:
        fight.first_death_s = (line.t - fight.start).total_seconds()
    if killer_type == "GamePlayer":
        fight.player_in = True
        fight.player_kills += 1
    if f.get("calledTarget") == "true":
        fight.called_kills += 1
    fight.events.append(line)


def record_fights(lines: list[Line]) -> list[Fight]:
    """Fights from RVR_FIGHT_OPEN / RVR_FIGHT_END and every row stamped fight=."""
    fights: dict[str, Fight] = {}
    for line in lines:
        f = line.f
        fid = f.get("fight")
        if line.tag == "RVR_FIGHT_OPEN" and fid:
            fid = f"{line.build}#{fid}"
            fights[fid] = Fight(fid, int(num(f.get("region"), 0)), num(f.get("x"), 0), num(f.get("y"), 0), line.t, line.t,
                                line.build, "record")
            fights[fid].groups = parse_groups(f.get("groups"))
            fights[fid].events.append(line)
            continue
        if not fid or fid == "0":
            continue
        key = f"{line.build}#{fid}"
        fight = fights.get(key)
        if fight is None:
            continue
        fight.events.append(line)
        if line.tag == "RVR_BOT_KILLED":
            add_kill(fight, line)
        elif line.tag == "RVR_PLAYER_KILLED":
            fight.player_in = True
            fight.player_deaths += 1
        elif line.tag == "RVR_FIGHT_END":
            fight.end = line.t
            fight.members = defaultdict(set)
            for part in str(f.get("sizes", "")).split(","):
                if ":" in part:
                    realm, n = part.split(":", 1)
                    fight.members[realm] = set(range(int(num(n, 0))))
            fight.deaths = Counter({p.split(":")[0]: int(num(p.split(":")[1], 0))
                                    for p in str(f.get("deaths", "")).split(",") if ":" in p})
            if f.get("groups"):
                fight.groups = parse_groups(f.get("groups"))
            fight.winner, fight.held = f.get("winner", "none"), f.get("held", "none")
            fight.first_death_s = num(f.get("firstDeathS"))
            fight.first_cc_s = num(f.get("firstCcS"))
            fight.player_in = f.get("playerIn") == "true"
            fight.player_deaths = int(num(f.get("playerDeaths"), 0))
            fight.player_kills = int(num(f.get("playerKills"), 0))
    return list(fights.values())


# ---- Forces (#599) ---------------------------------------------------------------------------------------------------

@dataclass
class Force:
    """One battlegroup, zerg or battleground battlegroup, from its RVR_FORCE_* rows: formed to ended."""
    key: str
    id: str
    kind: str
    realm: str
    region: int
    start: dt.datetime
    build: str
    end: dt.datetime | None = None
    end_why: str | None = None
    formed_seen: bool = True          # False when the log starts after it formed (its first row is not what=formed)
    peak_heads: int = 0
    peak_groups: int = 0
    joins: int = 0
    leaves: Counter = field(default_factory=Counter)
    minutes: float | None = None
    events: list = field(default_factory=list)

    def alive_in(self, hour: dt.datetime) -> bool:
        return hour_of(self.start) <= hour and (self.end is None or hour_of(self.end) >= hour)


def forces(lines: list[Line]) -> list[Force]:
    """Every force in the log. An id is a new force after its 'ended' (battleground ids repeat per realm and keep)."""
    out: list[Force] = []
    live: dict[str, Force] = {}
    for line in lines:
        kind = FORCE_TAGS.get(line.tag)
        if kind is None:
            continue
        f = line.f
        fid = f.get("id")
        if not fid:
            continue
        key = f"{line.build}#{fid}"
        what = f.get("what", "?")
        force = live.get(key)
        if force is None or what == "formed":
            force = Force(key, fid, kind, f.get("realm", "?"), int(num(f.get("region"), 0) or 0), line.t, line.build,
                          formed_seen=what == "formed")
            out.append(force)
            live[key] = force
        force.events.append(line)
        force.peak_heads = max(force.peak_heads, int(num(f.get("heads"), 0) or 0))
        force.peak_groups = max(force.peak_groups, int(num(f.get("groups"), 0) or 0))
        if what == "joined":
            force.joins += 1
        elif what == "left":
            force.leaves[f.get("why", "?")] += 1
        elif what == "ended":
            force.end, force.end_why = line.t, f.get("why", "?")
            force.minutes = num(f.get("minutes"))
            del live[key]
    return out


def force_hour(all_forces: list[Force], hl: list[Line], hour: dt.datetime, any_rows: set) -> dict:
    """Per kind and realm: formed, ended, alive, joins and leaves with reasons, peak heads and groups, lifetime."""
    out = {}
    for tag, kind in FORCE_TAGS.items():
        if tag not in any_rows:
            out[kind] = f"needs {tag}"
            continue
        rows = [l for l in hl if l.tag == tag]
        by_realm = {}
        for realm in REALMS:
            alive = [fo for fo in all_forces if fo.kind == kind and fo.realm == realm and fo.alive_in(hour)]
            rr = [l.f for l in rows if l.f.get("realm") == realm]
            if not alive and not rr:
                continue
            ended = [fo for fo in alive if fo.end and hour_of(fo.end) == hour]
            # a force that carries over from the hour before counts at its size then, until a row of this hour changes it
            carried = [(int(num(before[-1].f.get("heads"), 0) or 0), int(num(before[-1].f.get("groups"), 0) or 0))
                       for fo in alive if (before := [l for l in fo.events if l.t < hour])]
            by_realm[realm] = {
                "formed": sum(1 for f in rr if f.get("what") == "formed"),
                "ended": len(ended),
                "alive": len(alive),
                "joins": sum(1 for f in rr if f.get("what") == "joined"),
                "leaves": dict(Counter(f.get("why", "?") for f in rr if f.get("what") == "left")),
                "peak_heads": max([int(num(f.get("heads"), 0) or 0) for f in rr] + [c[0] for c in carried], default=0),
                "peak_groups": max([int(num(f.get("groups"), 0) or 0) for f in rr] + [c[1] for c in carried], default=0),
                "median_minutes_ended": median([fo.minutes for fo in ended]),
            }
            if kind == "zerg":
                by_realm[realm]["moves"] = sum(1 for f in rr if f.get("what") == "moved")
        out[kind] = by_realm
    return out


def cluster_hour(hl: list[Line], any_rows: set) -> dict | str:
    """RVR_FORCE_CLUSTER per realm: samples, the biggest cluster, samples at 9-15 and at 16+, and those over 24."""
    if "RVR_FORCE_CLUSTER" not in any_rows:
        return "needs RVR_FORCE_CLUSTER"
    out = {}
    for realm in REALMS:
        biggest = [int(num(l.f.get("biggest"), 0) or 0) for l in hl if l.tag == "RVR_FORCE_CLUSTER" and l.f.get("realm") == realm]
        over24 = [int(num(l.f.get("over24"), 0) or 0) for l in hl if l.tag == "RVR_FORCE_CLUSTER" and l.f.get("realm") == realm]
        if biggest:
            out[realm] = {"samples": len(biggest), "biggest": max(biggest), "median_biggest": median(biggest),
                          "samples_9_15": sum(1 for b in biggest if 9 <= b <= 15),
                          "samples_16_plus": sum(1 for b in biggest if b >= 16),
                          "samples_over24": sum(1 for n in over24 if n > 0)}
    return out


def board_hour(hl: list[Line], any_rows: set) -> dict | str:
    """RVR_FIGHT_BOARD in contact: how many enemies the groups saw, by the side buckets."""
    if "RVR_FIGHT_BOARD" not in any_rows:
        return "needs RVR_FIGHT_BOARD"
    boards = [l.f for l in hl if l.tag == "RVR_FIGHT_BOARD" and l.f.get("contact") == "true"]
    seen = [int(num(f.get("enemies"), 0) or 0) for f in boards]
    return {"in_contact": len(boards),
            "enemies_seen": {name: sum(1 for n in seen if low <= n <= high) for low, high, name in SIDE_BUCKETS
                             if any(low <= n <= high for n in seen)},
            "most_enemies_seen": max(seen, default=0)}


# ---- Sieges ----------------------------------------------------------------------------------------------------------

@dataclass
class Siege:
    key: str
    keep: str
    region: int | None
    start: dt.datetime
    build: str
    kind: str
    attacker: str = "?"
    end: dt.datetime | None = None
    result: str = "running at end of log"
    stalls: Counter = field(default_factory=Counter)
    events: list = field(default_factory=list)
    ram_hits: int = 0
    engines_lost: int = 0
    lowest_door: float | None = None
    lowest_lord: float | None = None
    counts: Counter = field(default_factory=Counter)


def keep_ids(lines: list[Line]) -> dict:
    """'rvr-keep-N' -> (name, region) from the lines that name both, and BG regions from travel lines."""
    names, regions = {}, {}
    for line in lines:
        f = line.f
        if line.tag == "RVR_EVENT_OPENED" and f.get("target", "").startswith("rvr-keep-"):
            names[f["target"]] = f.get("name", f["target"])
        if line.tag in ("RVR_KEEP_TAKEN",) and f.get("id"):
            names[f"rvr-keep-{f['id']}"] = f.get("keep", "?")
            regions[f"rvr-keep-{f['id']}"] = int(num(f.get("region"), 0))
        if line.tag.startswith("RVR_SIEGE_") and f.get("siege"):
            names[f["siege"]] = f.get("keep", "?")
            regions[f["siege"]] = int(num(f.get("region"), 0))
        if f.get("target", "").startswith("rvr-keep-") and f.get("region"):
            regions.setdefault(f["target"], int(num(f.get("region"), 0)))
    return {k: (names.get(k), regions.get(k)) for k in set(names) | set(regions)}


def sieges(lines: list[Line]) -> list[Siege]:
    ids = keep_ids(lines)
    bg_keep_by_region = {}
    for line in lines:
        if line.tag == "BG_SIEGE_STARTED":
            bg_keep_by_region[int(num(line.f.get("region"), 0))] = line.f.get("keep")
    region_of_name = {name: region for region, name in bg_keep_by_region.items()}

    def siege_key(line: Line):
        f = line.f
        if f.get("siege"):
            return f["siege"]
        target = f.get("target") or f.get("keep") or ""
        if str(target).startswith("rvr-keep-"):
            return target
        name = f.get("keep")
        if name:
            for key, (known, region) in ids.items():
                if known == name or (region is not None and region_of_name.get(name) == region):
                    return key
            return f"keep:{name}"
        return None

    def name_of(key):
        known = ids.get(key, (None, None))
        if known[0]:
            return known[0]
        if known[1] in bg_keep_by_region:
            return bg_keep_by_region[known[1]]
        return key.removeprefix("keep:")

    def same(key_a, key_b):
        return key_a == key_b or name_of(key_a) == name_of(key_b)

    out: list[Siege] = []
    open_by_name: dict[str, Siege] = {}
    last_status: dict[str, str] = {}
    for line in lines:
        f = line.f
        tag = line.tag
        key = siege_key(line)
        if key is None:
            continue
        name = name_of(key)
        siege = open_by_name.get(name)
        starts = tag in ("RVR_EVENT_OPENED", "BG_SIEGE_STARTED", "RVR_SIEGE_BEGIN")
        if starts and (siege is None or tag != "RVR_SIEGE_BEGIN"):
            if siege and siege.end is None:
                siege.end = line.t
                siege.result = ("next assault began (no capture)" if tag == "BG_SIEGE_STARTED" else "superseded by a new siege")
            kind = "battleground" if tag == "BG_SIEGE_STARTED" or f.get("battleground") == "true" else "frontier"
            siege = Siege(key, name, ids.get(key, (None, None))[1] or (int(num(f.get("region"), 0)) or None), line.t,
                          line.build, kind, f.get("attacker", "?"))
            out.append(siege)
            open_by_name[name] = siege
        if siege is None:
            continue
        if tag in NOISY_SIEGE_TAGS:
            siege.counts[tag] += 1
            continue
        if tag == "BG_KEEP_STATUS":
            status = " ".join(f"{k}={f.get(k)}" for k in ("holder", "doors", "doorHealth", "guards", "lord"))
            if last_status.get(name) == status:
                continue
            last_status[name] = status
            siege.lowest_door = min(x for x in (siege.lowest_door, num(f.get("doorHealth"))) if x is not None)
            lord = 0.0 if f.get("lord") == "dead" else num(f.get("lord"))
            if lord is not None:
                siege.lowest_lord = lord if siege.lowest_lord is None else min(siege.lowest_lord, lord)
        if tag == "RVR_SIEGE":
            action = f.get("action")
            if action == "hit":
                siege.ram_hits += 1
                hp = num(f.get("targetHp"))
                if hp is not None:
                    siege.lowest_door = hp if siege.lowest_door is None else min(siege.lowest_door, hp)
                continue   # hits are counted, not listed
            if action == "destroyed":
                siege.engines_lost += 1
        if tag == "RVR_SIEGE_STALL":
            siege.stalls[f.get("why", "?")] += 1
        siege.events.append(line)
        if tag in ("RVR_EVENT_ENDED", "RVR_SIEGE_END") or tag == "RVR_KEEP_TAKEN":
            siege.end = line.t
            siege.result = (f"taken by {f.get('realm')}" if tag == "RVR_KEEP_TAKEN" else
                            f.get("reason") or f.get("result", "ended"))
            del open_by_name[name]
    return out


# ---- The hour --------------------------------------------------------------------------------------------------------

def hour_of(t: dt.datetime) -> dt.datetime:
    return t.replace(minute=0, second=0)


def band(distance: float) -> str:
    for low, high in BANDS:
        if low <= distance < high:
            return f"{low}-{high}" if high < 10 ** 9 else f"{low}+"
    return "?"


def report(lines: list[Line]) -> dict:
    by_hour: dict[dt.datetime, list[Line]] = defaultdict(list)
    for line in lines:
        if line.tag != "FORK_BUILD":
            by_hour[hour_of(line.t)].append(line)
    has_record = any(line.tag == "RVR_FIGHT_END" for line in lines)
    fights = record_fights(lines) if has_record else rebuild_fights([l for l in lines if l.tag == "RVR_BOT_KILLED"])
    all_sieges = sieges(lines)
    all_forces = forces(lines)
    any_rows = {line.tag for line in lines}
    named_groups = any(fi.groups for fi in fights)
    hours = []
    for hour in sorted(by_hour):
        hl = by_hour[hour]
        builds = sorted({line.build for line in hl})
        h = {"hour": hour.strftime("%m/%d %H:00"), "builds": builds, "realms": {}, "notes": []}
        tags = Counter(line.tag for line in hl)

        # population
        rows = [l for l in hl if l.tag == "RVR_HOUR"]
        seen = defaultdict(set)
        for l in hl:
            f = l.f
            if l.tag == "RVR_NOTICE":
                seen[f.get("realm")].add(f.get("bot"))
            elif l.tag == "RVR_BOT_KILLED":
                seen[f.get("realm")].add(f.get("victim"))
                if f.get("killerType") == "GameBot":
                    seen[f.get("killerRealm")].add(f.get("killer"))
            elif l.tag in ("BG_STORM_TRACE", "BG_LORD_PATH", "RVR_STEALTH_OPEN", "RVR_STEALTH_ARCHER_OPEN", "RVR_LORD_FORMATION", "RVR_KEEP_TRAVEL_STALL",
                           "RVR_BORDER_STAGING", "RVR_DECIDE", "RVR_ENGAGE"):
                seen[f.get("realm")].add(f.get("bot"))
        # perception
        notices = [l for l in hl if l.tag == "RVR_NOTICE"]
        # kills
        kills = [l for l in hl if l.tag == "RVR_BOT_KILLED"]
        player_deaths = [l for l in hl if l.tag == "RVR_PLAYER_KILLED"]
        for realm in REALMS:
            r = {}
            hour_rows = [l.f for l in rows if l.f.get("realm") == realm]
            if hour_rows:
                x = hour_rows[-1]
                r["bots_in_war"] = {k: int(num(x.get(k), 0)) for k in ("botsInWar", "solo", "small", "eight")}
            r["bots_seen"] = len(seen.get(realm, set()) - {None})
            rn = [l.f for l in notices if l.f.get("realm") == realm]
            bands = defaultdict(list)
            for f in rn:
                d = num(f.get("distance"))
                if d is not None:
                    bands[band(d)].append(num(f.get("time_since_first_los_ms")))
            r["notices"] = {b: {"n": len(v), "median_los_ms": median(v)} for b, v in sorted(bands.items(), key=lambda p: num(p[0].split("-")[0].rstrip("+")))}
            r["notices_by_kind"] = dict(Counter(f.get("kind", "?") for f in rn))
            r["kills"] = sum(1 for l in kills if l.f.get("killerRealm") == realm)
            r["deaths"] = sum(1 for l in kills if l.f.get("realm") == realm)
            r["deaths_by"] = dict(Counter((l.f.get("by") or l.f.get("killerType", "?")) for l in kills if l.f.get("realm") == realm))
            role = Counter(l.f.get("victimRole") for l in kills if l.f.get("realm") == realm and l.f.get("victimRole"))
            if role:
                r["deaths_by_role"] = dict(role)
            rf = [fi for fi in fights if hour_of(fi.start) == hour and realm in fi.members]
            r["fights"] = len(rf)
            r["fights_won"] = sum(1 for fi in rf if fi.winner == realm)
            r["fights_by_side"] = dict(Counter(side_bucket(fi.sides.get(realm, 0)) for fi in rf if fi.sides.get(realm)))
            h["realms"][realm] = r

        # fights
        hf = [fi for fi in fights if hour_of(fi.start) == hour]
        cells = Counter((fi.region, int(fi.x // CELL), int(fi.y // CELL)) for fi in hf)
        busiest = cells.most_common(1)[0] if cells else None
        h["fights"] = {
            "source": "RVR_FIGHT_END" if has_record else "rebuilt from RVR_BOT_KILLED (no RVR_FIGHT_* rows in this log)",
            "count": len(hf),
            "by_size": dict(Counter(size_bucket(fi.size) for fi in hf)),
            "by_outcome": dict(Counter(fi.winner for fi in hf)),
            "deaths": sum(sum(fi.deaths.values()) for fi in hf),
            "median_seconds": median([fi.seconds for fi in hf]),
            "median_first_death_s": median([fi.first_death_s for fi in hf]),
            "median_first_cc_s": median([fi.first_cc_s for fi in hf]) if has_record else "needs RVR_FIGHT_END",
            "busiest_cell": (f"region {busiest[0][0]} cell {busiest[0][1]},{busiest[0][2]}: {busiest[1]} of {len(hf)} "
                             f"({100 * busiest[1] / len(hf):.0f}%)") if busiest else "none",
            "with_player": sum(1 for fi in hf if fi.player_in),
        }
        # fights by their biggest side and the multi-group forces (#599)
        h["fight_sizes"] = {
            "by_side": {name: n for _, _, name in SIDE_BUCKETS
                        if (n := sum(1 for fi in hf if side_bucket(fi.biggest_side) == name))},
            "matchups": dict(Counter(fi.matchup for fi in hf).most_common(6)),
            "multi_group": sum(1 for fi in hf if fi.most_groups >= 2) if named_groups else "needs RVR_FIGHT_END groups=",
            "three_groups": sum(1 for fi in hf if fi.most_groups >= 3) if named_groups else "needs RVR_FIGHT_END groups=",
            "biggest_side": max((fi.biggest_side for fi in hf), default=0),
        }
        h["forces"] = force_hour(all_forces, hl, hour, any_rows)
        h["clusters"] = cluster_hour(hl, any_rows)
        h["boards"] = board_hour(hl, any_rows)
        called = sum(1 for l in kills if l.f.get("calledTarget") == "true")
        has_called = any("calledTarget" in l.f for l in kills)
        h["tactics"] = {
            "assist_calls": tags["RVR_ASSIST_CALL"], "assist_switches": tags["RVR_ASSIST_SWITCH"],
            "cc_assigns": tags["RVR_CC_ASSIGN"], "peels": tags["RVR_PEEL"], "kites": tags["RVR_KITE"],
            "stances": tags["RVR_STANCE"], "retreats": tags["RVR_RETREAT"],
            "assist_calls_per_fight": round(tags["RVR_ASSIST_CALL"] / len(hf), 2) if hf else None,
            "called_target_kills": f"{called} of {len(kills)}" if has_called else "needs RVR_BOT_KILLED calledTarget=",
        }
        # the player
        player_kills = [l for l in kills if l.f.get("killerType") == "GamePlayer"]
        pn = [l.f for l in hl if l.tag == "RVR_PLAYER_NOTICED"]
        seats = [l.f for l in hl if l.tag == "RVR_PLAYER_SEAT"]
        views = [l for l in hl if l.tag == "RVR_PLAYER_VIEW"]
        first_view = views[0].t if views else None
        first_enemy = next((l.t for l in views if num(l.f.get("enemiesNear"), 0) > 0), None)
        h["player"] = {
            "kills_by_player": len(player_kills),
            "deaths_of_player": len(player_deaths) if "RVR_PLAYER_KILLED" in tags or any(l.tag == "RVR_PLAYER_KILLED" for l in lines)
            else "needs RVR_PLAYER_KILLED",
            "killed_by": dict(Counter(f"{l.f.get('killerKind', '?')}/{l.f.get('killerClass', '?')}" for l in player_deaths)),
            "median_s_noticed_to_death": median([num(l.f.get("sinceNoticedMs")) / 1000 for l in player_deaths
                                                 if num(l.f.get("sinceNoticedMs"), -1) >= 0]),
            "noticed": len(pn) if pn or any(l.tag == "RVR_PLAYER_NOTICED" for l in lines) else
            ("0 (no RVR_NOTICE of kind=Player either)" if not any(l.f.get("kind") == "Player" for l in notices) else
             sum(1 for l in notices if l.f.get("kind") == "Player")),
            "noticed_median_distance": median([num(f.get("distance")) for f in pn]),
            "noticed_median_los_ms": median([num(f.get("time_since_first_los_ms")) for f in pn
                                             if num(f.get("time_since_first_los_ms"), -1) >= 0]),
            "noticed_by_call": sum(1 for f in pn if f.get("called") == "true"),
            "time_to_first_enemy_s": (first_enemy - first_view).total_seconds() if first_view and first_enemy else
            ("needs RVR_PLAYER_VIEW" if not views else "no enemy within 3,000"),
            "idle_minutes_in_war": sum(num(f.get("minutesInWar"), 0) - num(f.get("minutesEnemyWithin3000"), 0) for f in seats)
            if seats else "needs RVR_PLAYER_SEAT",
            "odd_flags": [mask(l.text) for l in hl if l.tag == "RVR_ODD"],
        }
        # roaming, travel, teleports
        roam = [l.f for l in hl if l.tag == "RVR_ROAM_CHECK"]
        h["roaming"] = {"checks": len(roam), "by_result": dict(Counter(f.get("result", "?") for f in roam)),
                        "hunts": tags["RVR_ROAM_HUNT"], "chases": tags["RVR_ROAM_CHASE"], "move_ons": tags["RVR_ROAM_MOVE_ON"]}
        stalls = [l.f for l in hl if l.tag == "RVR_KEEP_TRAVEL_STALL"]
        h["travel"] = {"keep_travel_stalls": len(stalls),
                       "stalls_by_place": dict(Counter(f"{f.get('target', '?')}@{f.get('region', '?')}" for f in stalls)),
                       "route_failed": tags["RVR_KEEP_ROUTE_FAILED"], "route_restart": tags["RVR_KEEP_ROUTE_RESTART"],
                       "frontier_wait_long": tags["RVR_FRONTIER_WAIT_LONG"], "lane_rejected": tags["RVR_LANE_REJECTED"],
                       "pvp_stalemates": tags["AUTONOMOUS_PVP_STALEMATE"], "unreachable": tags["AUTONOMOUS_UNREACHABLE_TARGET"]}
        tp = [l.f for l in hl if l.tag == "RVR_TELEPORT"]
        h["teleports"] = {"count": len(tp), "seen_by_player": sum(1 for f in tp if f.get("playerSaw", "none") != "none"),
                          "by": dict(Counter(f.get("by", "?") for f in tp).most_common(6))} if tp or any(
            l.tag == "RVR_TELEPORT" for l in lines) else "needs RVR_TELEPORT"
        # sieges
        hs = [s for s in all_sieges if hour_of(s.start) == hour]
        ended = [s for s in all_sieges if s.end and hour_of(s.end) == hour]
        stall_rows = [l.f for l in hl if l.tag == "RVR_SIEGE_STALL"]
        h["sieges"] = {"begun": len(hs), "by_kind": dict(Counter(s.kind for s in hs)), "finished": len(ended),
                       "results": dict(Counter(s.result for s in ended)),
                       "thin": sum(1 for l in hl if l.tag == "RVR_EVENT_ENDED" and l.f.get("thin") == "true"),
                       "stalls": len(stall_rows) if stall_rows or any(l.tag == "RVR_SIEGE_STALL" for l in lines) else
                       "needs RVR_SIEGE_STALL",
                       "stalls_by_reason": dict(Counter(f.get("why", "?") for f in stall_rows)),
                       "ram_hits": tags["RVR_SIEGE"] and sum(1 for l in hl if l.tag == "RVR_SIEGE" and l.f.get("action") == "hit"),
                       "engines_destroyed": sum(1 for l in hl if l.tag == "RVR_SIEGE" and l.f.get("action") == "destroyed"),
                       "keeps_taken": tags["RVR_KEEP_TAKEN"]}
        # decisions
        dec = [l.f for l in hl if l.tag == "RVR_DECIDE"]
        h["decisions"] = {"rows": len(dec), "by_what": dict(Counter(f.get("what") for f in dec)),
                          "by_layer": dict(Counter(f.get("layer") for f in dec)),
                          "top_rules": dict(Counter(f.get("rule") for f in dec).most_common(8))} if dec else "needs RVR_DECIDE"
        dropped = [l.f.get("dropped") for l in rows if l.f.get("dropped") not in (None, "0")]
        if dropped:
            h["notes"].append(f"rate-limited rows dropped: {', '.join(sorted(set(dropped)))}")
        hours.append(h)
    return {"hours": hours, "fights": fights, "sieges": all_sieges, "forces": all_forces, "has_record": has_record}


# ---- Text ------------------------------------------------------------------------------------------------------------

def fmt(value) -> str:
    if isinstance(value, float):
        return f"{value:.1f}".rstrip("0").rstrip(".")
    if isinstance(value, dict):
        return ", ".join(f"{k}: {fmt(v)}" for k, v in value.items()) or "none"
    return str(value)


def text(result: dict, path: Path) -> str:
    out = [f"RvR hour report: {path.name}"]
    if not result["has_record"]:
        out.append("(No RVR_FIGHT_* rows: an older build. Fights are rebuilt from RVR_BOT_KILLED; figures that need the "
                   "1.2.3 record say so.)")
    for h in result["hours"]:
        out.append("")
        out.append(f"== {h['hour']}  build {', '.join(h['builds'])}")
        for realm, r in h["realms"].items():
            pop = r.get("bots_in_war")
            pop_text = (f"{pop['botsInWar']} in war (solo {pop['solo']}, small {pop['small']}, eight {pop['eight']})" if pop
                        else f"{r['bots_seen']} seen")
            out.append(f"  {realm}: bots {pop_text}; fights {r['fights']} (won {r['fights_won']}); kills {r['kills']}, "
                       f"deaths {r['deaths']} by {fmt(r['deaths_by'])}")
            if r["notices"]:
                out.append("    notices: " + "; ".join(f"{b}: {v['n']} (median los {fmt(v['median_los_ms'])} ms)"
                                                       for b, v in r["notices"].items()) + f" | by kind {fmt(r['notices_by_kind'])}")
            if r.get("deaths_by_role"):
                out.append(f"    deaths by role: {fmt(r['deaths_by_role'])}")
        f = h["fights"]
        out.append(f"  fights: {f['count']} [{f['source']}] by size {fmt(f['by_size'])}; outcome {fmt(f['by_outcome'])}; "
                   f"deaths {f['deaths']}; median {fmt(f['median_seconds'])} s, first death {fmt(f['median_first_death_s'])} s, "
                   f"first CC {fmt(f['median_first_cc_s'])}; busiest {f['busiest_cell']}; with the player {f['with_player']}")
        fs = h["fight_sizes"]
        out.append(f"  fights by biggest side: {fmt(fs['by_side'])}; matchups {fmt(fs['matchups'])}; a side of 2+ groups "
                   f"{fs['multi_group']}, of 3+ groups {fs['three_groups']}; biggest side {fs['biggest_side']}")
        for kind, block in h["forces"].items():
            if isinstance(block, str):
                out.append(f"  {kind}: {block}")
                continue
            for realm, x in block.items():
                extra = f", moves {x['moves']}" if "moves" in x else ""
                out.append(f"  {kind} {realm}: formed {x['formed']}, ended {x['ended']}, alive {x['alive']}, joins "
                           f"{x['joins']}, leaves {fmt(x['leaves'])}, peak {x['peak_heads']} heads in {x['peak_groups']} "
                           f"groups, median life {fmt(x['median_minutes_ended']) if x['median_minutes_ended'] is not None else '-'} min{extra}")
            if not block:
                out.append(f"  {kind}: none")
        cl = h["clusters"]
        out.append("  clusters: " + (cl if isinstance(cl, str) else "; ".join(f"{r}: {fmt(v)}" for r, v in cl.items()) or "none"))
        out.append(f"  boards in contact: {fmt(h['boards'])}")
        out.append(f"  tactics: {fmt(h['tactics'])}")
        p = dict(h["player"])
        odd = p.pop("odd_flags")
        out.append(f"  player: {fmt(p)}")
        for flag in odd:
            out.append(f"    /rvrodd: {flag}")
        out.append(f"  roaming: {fmt(h['roaming'])}")
        out.append(f"  travel: {fmt(h['travel'])}")
        out.append(f"  teleports: {fmt(h['teleports'])}")
        out.append(f"  sieges: {fmt(h['sieges'])}")
        out.append(f"  decisions: {fmt(h['decisions'])}")
        for note in h["notes"]:
            out.append(f"  note: {note}")
    out.append("")
    out.append(f"Forces ({len(result['forces'])}):")
    for fo in result["forces"]:
        out.append(f"  {force_summary(fo)}")
    out.append("")
    out.append(f"Sieges ({len(result['sieges'])}):")
    for s in result["sieges"]:
        out.append(f"  {s.start:%H:%M}-{(s.end.strftime('%H:%M') if s.end else 'end')} {s.keep} ({s.kind}, attacker "
                   f"{s.attacker}): {s.result}; ram hits {s.ram_hits}, engines lost {s.engines_lost}, lowest door "
                   f"{fmt(s.lowest_door)}, lowest lord {fmt(s.lowest_lord)}, stalls {fmt(dict(s.stalls)) if s.stalls else 0}")
    return "\n".join(out)


def fight_timeline(fight: Fight) -> str:
    out = [f"fight {fight.id} [{fight.source}, build {fight.build}] region {fight.region} at {fight.x:.0f},{fight.y:.0f} "
           f"{fight.start:%m/%d %H:%M:%S}-{fight.end:%H:%M:%S} ({fight.seconds:.0f} s) size {fight.size} "
           f"sides {fmt({r: len(m) for r, m in fight.members.items()})} deaths {fmt(dict(fight.deaths))} "
           f"winner {fight.winner} held {fight.held} first death {fmt(fight.first_death_s)} s first CC {fmt(fight.first_cc_s)} "
           f"player in {fight.player_in}"]
    for line in fight.events:
        out.append(f"  +{(line.t - fight.start).total_seconds():5.0f}s {mask(line.text)}")
    return "\n".join(out)


def force_summary(force: Force) -> str:
    return (f"{force.start:%m/%d %H:%M}-{(force.end.strftime('%H:%M') if force.end else 'end')} {force.kind} {force.id} "
            f"{force.realm} region {force.region}{'' if force.formed_seen else ' (formed before the log)'}: peak "
            f"{force.peak_heads} heads in {force.peak_groups} groups, joins {force.joins}, leaves "
            f"{fmt(dict(force.leaves)) if force.leaves else 0}, ended {force.end_why or 'no'}")


def force_timeline(force: Force) -> str:
    out = [f"force {force_summary(force)} [build {force.build}]"]
    for line in force.events:
        out.append(f"  +{(line.t - force.start).total_seconds() / 60:5.1f}m {mask(line.text)}")
    return "\n".join(out)


def siege_timeline(siege: Siege) -> str:
    out = [f"siege {siege.key} {siege.keep} [{siege.kind}, build {siege.build}] {siege.start:%m/%d %H:%M:%S}-"
           f"{(siege.end.strftime('%H:%M:%S') if siege.end else 'end of log')}: {siege.result}; ram hits {siege.ram_hits}"]
    for line in siege.events:
        out.append(f"  +{(line.t - siege.start).total_seconds() / 60:5.1f}m {mask(line.text)}")
    return "\n".join(out)


def as_json(result: dict) -> dict:
    return {
        "hours": result["hours"],
        "fights": [{"id": f.id, "source": f.source, "build": f.build, "region": f.region, "start": f.start.isoformat(),
                    "seconds": f.seconds, "size": f.size, "sides": {r: len(m) for r, m in f.members.items()},
                    "deaths": dict(f.deaths), "winner": f.winner, "held": f.held, "first_death_s": f.first_death_s,
                    "first_cc_s": f.first_cc_s, "player_in": f.player_in, "biggest_side": f.biggest_side,
                    "matchup": f.matchup, "groups": {r: len(g) for r, g in f.groups.items()}} for f in result["fights"]],
        "forces": [{"id": fo.id, "kind": fo.kind, "build": fo.build, "realm": fo.realm, "region": fo.region,
                    "start": fo.start.isoformat(), "end": fo.end.isoformat() if fo.end else None, "end_why": fo.end_why,
                    "formed_seen": fo.formed_seen, "peak_heads": fo.peak_heads, "peak_groups": fo.peak_groups,
                    "joins": fo.joins, "leaves": dict(fo.leaves), "minutes": fo.minutes} for fo in result["forces"]],
        "sieges": [{"key": s.key, "keep": s.keep, "kind": s.kind, "attacker": s.attacker, "start": s.start.isoformat(),
                    "end": s.end.isoformat() if s.end else None, "result": s.result, "ram_hits": s.ram_hits,
                    "stalls": dict(s.stalls)} for s in result["sieges"]],
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("log")
    parser.add_argument("--year", type=int, help="the log's year (default this year)")
    parser.add_argument("--since", help='only lines at or after "MM/DD HH:MM"')
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--fights", action="store_true", help="every fight's timeline")
    parser.add_argument("--fight", help="one fight's timeline, by id (r12 for a rebuilt fight)")
    parser.add_argument("--sieges", action="store_true", help="every siege's timeline")
    parser.add_argument("--forces", action="store_true", help="every battlegroup's, zerg's and battleground battlegroup's timeline")
    parser.add_argument("--min-size", type=int, default=0, help="with --fights: only fights at least this big")
    args = parser.parse_args(argv)
    path = Path(args.log)
    if not path.is_file():
        print(f"no such file: {path}", file=sys.stderr)
        return 2
    year = args.year or dt.date.today().year
    since = None
    if args.since:
        since = dt.datetime.strptime(f"{year}/{args.since}", "%Y/%m/%d %H:%M")
    lines = list(read(path, year, since))
    result = report(lines)
    if args.json:
        print(json.dumps(as_json(result), indent=2, default=str))
    elif args.fight:
        match = [f for f in result["fights"] if f.id == args.fight or f.id.endswith(f"#{args.fight}")]
        if not match:
            print(f"no fight {args.fight}", file=sys.stderr)
            return 1
        print("\n\n".join(fight_timeline(f) for f in match))
    elif args.fights:
        print("\n\n".join(fight_timeline(f) for f in result["fights"] if f.size >= args.min_size))
    elif args.sieges:
        print("\n\n".join(siege_timeline(s) for s in result["sieges"]))
    elif args.forces:
        print("\n\n".join(force_timeline(fo) for fo in result["forces"]) or "no RVR_FORCE_* rows")
    else:
        print(text(result, path))
    return 0 if lines else 1


if __name__ == "__main__":
    sys.exit(main())
