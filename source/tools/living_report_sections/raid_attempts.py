"""RAID_ATTEMPT / RAID_ATTEMPT_END / RAID_DEATH (#644 part 6; rows from part 1, docs/RAID-ATTEMPT-ROWS.md): each boss's
pulls and how they ended, time to kill, who died to what (by role, kind and source), the top killers and mechanics, died
against left, and how long the raid took from a failed attempt to its next pull. RAID_FIGHT_SAMPLE (parts 2-3): a fight
curve per attempt (boss health over the pull, raiders alive and engaged, the healers' power, the boss on the tank).
RAID_FUNNEL (parts 2-3): each raid's funnel from asked to at the boss, with the drop-outs and the legs' times. No names:
the rows carry none for raiders; monster names are written. One attempt in detail: source/tools/raid_postmortem.py
<attempt>. Parts 3-5: RAID_MECHANIC_FIRE per boss and mechanic (fires, exposed, within, hit, died, avoided, the reaction
median, damage); with the diagnostics switch, RAID_THREAT (target changes, time on a tank, why) and RAID_ROLE_TOTALS (damage,
healing, overheal, crowd control, interrupts and power-outs by role)."""
from __future__ import annotations

from collections import Counter, defaultdict

from living_report_sections._raid import (FAILED, FUNNEL, ROWS as RAID_ROWS, attempts, count, funnels, killer_of, lost,
                                          mechanics_of, total)
from living_report_sections._util import median, pct, table

TITLE = "Raid attempts"
ROWS = list(RAID_ROWS) + [FUNNEL]
CURVES = 20   # fight curves shown (the most recent attempts with samples)
ORDER = 45
TOP = 10


def _med(values, fmt="{:.0f}"):
    m = median([v for v in values if v is not None])
    return "-" if m is None else fmt.format(m)


def _n(n, word):
    return f"{n} {word}" + ("" if n == 1 else "s")


def _mmss(seconds):
    if seconds is None:
        return "-"
    seconds = int(round(seconds))
    return f"{seconds // 60}:{seconds % 60:02d}"


def summarize(lines) -> str:
    found = attempts(lines)
    raids = funnels(lines)
    if not found and not raids:
        return "_No RAID_ATTEMPT, RAID_ATTEMPT_END or RAID_DEATH rows._"
    if not found:
        return "\n".join(["_No RAID_ATTEMPT, RAID_ATTEMPT_END or RAID_DEATH rows._", ""] + funnel_table(raids))
    deaths = [d for a in found for _, d in a.deaths]
    ended = [a for a in found if a.end]
    kills = [a for a in found if a.outcome == "killed"]
    out = [f"{_n(len(found), 'attempt')} ({len(ended)} ended, {_n(len(kills), 'kill')}), {_n(len(deaths), 'raider death')}."]
    open_ = [a.key for a in found if not a.end]
    if open_:
        out.append(f"No end row (still live when the log stops, or the server stopped): {', '.join(open_[:TOP])}"
                   + (f" and {len(open_) - TOP} more" if len(open_) > TOP else "") + ".")
    no_pull = [a.key for a in found if not a.pull]
    if no_pull:
        out.append(f"No pull row (the log starts mid-attempt): {', '.join(no_pull[:TOP])}.")

    # ---- per boss
    by_boss = defaultdict(list)
    for a in found:
        by_boss[(a.event, a.boss)].append(a)
    rows = []
    for (event, boss), group in sorted(by_boss.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        outcomes = Counter(a.outcome for a in group)
        won = [a for a in group if a.outcome == "killed"]
        lost = [a for a in group if a.outcome in FAILED]
        rows.append([
            event, boss, len(group),
            ", ".join(f"{o} {n}" for o, n in outcomes.most_common()),
            pct(len(won), len(group)),
            _med([count(a.pull.get("size")) for a in group]),
            _med([count(a.pull.get("sizePct")) for a in group], "{:.0f}%"),
            _mmss(median([a.secs for a in won if a.secs is not None])),
            _mmss(median([a.secs for a in lost if a.secs is not None])),
            _med([count(a.end.get("lowest")) for a in lost], "{:.0f}%"),
            _med([len(a.deaths) for a in group]),
        ])
    out += ["", table(["Event", "Boss", "Pulls", "Outcomes", "Kills", "Median size", "Size vs period", "Time to kill",
                       "Time to fail", "Boss lowest (fails)", "Deaths a pull"], rows)]

    # ---- deaths
    if deaths:
        out += ["", "Deaths by role and by kind (share of all raider deaths):", ""]
        roles = Counter(d.get("role", "-") for d in deaths)
        kinds = Counter(d.get("kind", "-") for d in deaths)
        sources = Counter(d.get("source", "-") for d in deaths)
        out.append(table(["Role", "Deaths", "Share"], [[r, n, pct(n, len(deaths))] for r, n in roles.most_common()]))
        out += ["", table(["Kind", "Deaths", "Share"], [[k, n, pct(n, len(deaths))] for k, n in kinds.most_common()])]
        out += ["", "By the last hit: " + ", ".join(f"{s} {n} ({pct(n, len(deaths))})" for s, n in sources.most_common()) + "."]
        killers = Counter(killer_of(d) for d in deaths)
        out += ["", f"Top killers (of {len(deaths)} deaths):", "",
                table(["Killer", "Deaths", "Share"], [[k, n, pct(n, len(deaths))] for k, n in killers.most_common(TOP)])]
        mech = Counter(m for d in deaths for m in mechanics_of(d))
        with_mech = sum(1 for d in deaths if mechanics_of(d))
        if mech:
            out += ["", f"Mechanics in force at a death ({with_mech} deaths, {pct(with_mech, len(deaths))}; one death can "
                        "carry several):", "",
                    table(["Mechanic", "Deaths"], [[m, n] for m, n in mech.most_common(TOP)])]
        else:
            out += ["", "No death had a boss mechanic in force (only bosses whose scripts raise signals have any)."]
        no_healer = sum(1 for d in deaths if count(d.get("healersIn")) == 0)
        out += ["", f"Deaths with no living healer within range: {no_healer} ({pct(no_healer, len(deaths))})."]

    # ---- died against left
    died = [count(a.end.get("died")) for a in ended]
    left = [count(a.end.get("left")) for a in ended]
    known = [(d, l) for d, l in zip(died, left) if d is not None and l is not None]
    if known:
        out += ["", f"Fallen raiders who really died against those who only left the region or walked back (ended "
                    f"attempts with both counts, {len(known)}): died {sum(d for d, _ in known)}, left "
                    f"{sum(l for _, l in known)}."]

    # ---- wipe to next pull
    retries = [a for a in found if a.pull.get("prev")]
    if retries:
        waits = [count(a.pull.get("wipeToPullS")) for a in retries]
        known_waits = [w for w in waits if w is not None]
        out += ["", f"Pulls after a failed attempt: {len(retries)}. From the failed end to the next pull: median "
                    f"{_mmss(median(known_waits))}, longest {_mmss(max(known_waits)) if known_waits else '-'}."]
    gathers = [count(a.pull.get("gatherS")) for a in found if a.pull.get("gatherS") is not None]
    gathers = [g for g in gathers if g is not None]
    if gathers:
        out.append(f"Muster or regroup before a pull ({_n(len(gathers), 'pull')}): median {_mmss(median(gathers))}, "
                   f"longest {_mmss(max(gathers))}.")

    # ---- fight curves (RAID_FIGHT_SAMPLE)
    sampled = [a for a in found if a.samples]
    if sampled:
        out += ["", f"Fight curves ({_n(len(sampled), 'attempt')} with RAID_FIGHT_SAMPLE rows"
                    + (f"; the last {CURVES}" if len(sampled) > CURVES else "") + "). Boss health at the start, a quarter, half "
                    "and three quarters into the samples, and the last; alive and engaged are raiders (all roles) at the first and "
                    "the last sample; healer power is the healers' mean, lowest sample; on tank is the share of samples with the "
                    "boss on a tank (of those with a target):", "",
                table(["Attempt", "Boss", "Outcome", "Samples", "Boss %", "Alive", "Engaged", "Healer power (low)",
                       "Healers <10% (peak)", "On tank", "Median distance", "Mezzed / stunned (peak)"],
                      [curve_row(a) for a in sampled[-CURVES:]])]
    out += mechanic_table(found) + threat_summary(found) + role_table(found)
    if raids:
        out += [""] + funnel_table(raids)
    return "\n".join(out)


def _sum(values):
    known = [v for v in values if v is not None]
    return sum(known) if known else None


def _cell(value):
    return "-" if value is None else value


def mechanic_table(found) -> list[str]:
    fires = [(a, f) for a in found for _, f in a.mechanics]
    if not fires:
        return []
    groups = defaultdict(list)
    for a, f in fires:
        groups[(f.get("boss", "?"), f.get("mechanic", "?"))].append(f)
    rows = []
    for (boss, mech), fs in sorted(groups.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        exposed = _sum(count(f.get("exposedAtCall")) for f in fs)
        avoided = _sum(count(f.get("avoided")) for f in fs)
        avoided_of = _sum(count(f.get("exposedAtCall")) for f in fs if count(f.get("avoided")) is not None)
        rows.append([boss, mech, len(fs), "yes" if any(f.get("called") == "1" for f in fs) else "no",
                     _cell(exposed), _cell(_sum(count(f.get("within")) for f in fs)),
                     _cell(_sum(count(f.get("hit")) for f in fs)), _cell(_sum(count(f.get("died")) for f in fs)),
                     "-" if avoided is None else f"{avoided} ({pct(avoided, avoided_of)})",
                     _med([count(f.get("leadMs")) for f in fs], "{:.0f} ms"),
                     _med([count(f.get("reactMedianMs")) for f in fs], "{:.0f} ms"),
                     _cell(_sum(count(f.get("damage")) for f in fs))])
    return ["", f"Mechanics (RAID_MECHANIC_FIRE, {_n(len(fires), 'fire')}; called: the raid AI was told of it, else a "
                "measure-only signal; avoided: exposed at the call, alive, not in the area at the landing and not hit; "
                "react: when the exposed stepped out, median of the per-fire medians):", "",
            table(["Boss", "Mechanic", "Fires", "Called", "Exposed", "Within", "Hit", "Died", "Avoided", "Lead", "React",
                   "Damage"], rows)]


def threat_summary(found) -> list[str]:
    with_threat = [a for a in found if a.threats]
    if not with_threat:
        return []
    rows = []
    for a in with_threat[-CURVES:]:
        ts = [f for _, f in a.threats]
        held = defaultdict(int)
        for f in ts:
            held[f.get("from", "-")] += count(f.get("heldMs")) or 0
        total = sum(held.values())
        why = Counter(f.get("why", "-") for f in ts)
        rows.append([a.key, a.boss, len(ts), pct(held.get("tank", 0), total),
                     ", ".join(f"{k} {v}" for k, v in why.most_common()),
                     ", ".join(f"{k} {n}" for k, n in Counter(f.get("to", "-") for f in ts).most_common())])
    return ["", "Boss threat (RAID_THREAT, diagnostics switch; on a tank: the share of the time between changes the boss "
                "held a tank, counted from each change's heldMs):", "",
            table(["Attempt", "Boss", "Changes", "On a tank", "Why", "To"], rows)]


def role_table(found) -> list[str]:
    totals = [f for a in found for _, f in a.roles]
    if not totals:
        return []
    by_role = defaultdict(list)
    for f in totals:
        by_role[f.get("role", "-")].append(f)
    rows = []
    for role in ("tank", "healer", "buffer", "attacker"):
        fs = by_role.get(role, [])
        if not fs:
            continue
        healed = _sum(count(f.get("healed")) for f in fs) or 0
        over = _sum(count(f.get("overheal")) for f in fs) or 0
        rows.append([role, len(fs), _sum(count(f.get("raiders")) for f in fs) or 0, _sum(count(f.get("dealtBoss")) for f in fs) or 0,
                     _sum(count(f.get("dealtAdds")) for f in fs) or 0, _sum(count(f.get("took")) for f in fs) or 0, healed,
                     pct(over, healed + over), _sum(count(f.get("deaths")) for f in fs) or 0, _sum(count(f.get("cc")) for f in fs) or 0,
                     _sum(count(f.get("interrupts")) for f in fs) or 0, _sum(count(f.get("powerOuts")) for f in fs) or 0])
    return ["", f"Roles (RAID_ROLE_TOTALS, diagnostics switch; summed over {_n(len({f.get('attempt') for f in totals}), 'attempt')}; "
                "overheal as a share of all healing cast):", "",
            table(["Role", "Attempts", "Raiders", "Dealt boss", "Dealt adds", "Took", "Healed", "Overheal", "Deaths", "CC",
                   "Interrupts", "Power-outs"], rows)]


def _at_shares(values, shares=(0, 0.25, 0.5, 0.75)):
    if not values:
        return []
    picks = [values[min(len(values) - 1, int(share * len(values)))] for share in shares] + [values[-1]]
    return picks


def curve_row(a) -> list:
    samples = [f for _, f in sorted(a.samples, key=lambda sf: count(sf[1].get("t")) or 0)]
    boss = " > ".join("-" if v is None else str(v) for v in _at_shares([count(f.get("bossPct")) for f in samples]))
    alive = [total(f.get("alive")) for f in samples]
    engaged = [total(f.get("engaged")) for f in samples]
    pows = [count(f.get("healerPow")) for f in samples]
    known_pows = [p for p in pows if p is not None]
    low = [count(f.get("healersLow")) for f in samples]
    on = [f.get("onTank") for f in samples if f.get("onTank") in ("0", "1")]
    mez = [count(f.get("mezzed")) or 0 for f in samples]
    stun = [count(f.get("stunned")) or 0 for f in samples]

    def ends(values):
        return f"{'-' if values[0] is None else values[0]} > {'-' if values[-1] is None else values[-1]}"

    return [a.key, a.boss, a.outcome, len(samples), boss, ends(alive), ends(engaged),
            "-" if not known_pows else f"{median(known_pows):.0f}% ({min(known_pows)}%)",
            f"{max((x for x in low if x is not None), default=0)}",
            pct(on.count("1"), len(on)),
            _med([count(f.get("medDist")) for f in samples]),
            f"{max(mez)} / {max(stun)}"]


def funnel_table(raids) -> list[str]:
    """One row per raid: its end row (or its start row when the log stops before the end)."""
    rows = []
    reasons = ("route", "travel", "died", "logout", "released", "other")
    for event, start, end_ in raids:
        f = end_ or start
        drops = lost(f.get("lost"))
        rows.append([event, f.get("at", "-"), "end" if end_ else "start only", f.get("asked", "-"), f.get("recruited", "-"),
                     f.get("parties", "-"), f.get("atHub", "-"), f.get("staged", "-"), f.get("atBoss", "-"),
                     ", ".join(f"{r} {drops[r]}" for r in reasons if drops.get(r)) or "none",
                     _mmss(count(f.get("hubS"))), _mmss(count(f.get("stageS"))), _mmss(count(f.get("bossS")))])
    return [f"Raid funnel (RAID_FUNNEL, {_n(len(raids), 'raid')}): asked is the size required; recruited, parties, at the hub and "
            "staged are the most at once; at the boss is the most raiders in one of the raid's attempts. Drop-outs count "
            "distinct raiders per reason (route and travel are failures a raider can recover from; logout, released and other "
            "left the raid; died is a death outside a pull):", "",
            table(["Event", "At", "Rows", "Asked", "Recruited", "Parties", "At hub", "Staged", "At boss", "Drop-outs",
                   "Hub muster", "Hub to start", "Start to first pull"], rows)]
