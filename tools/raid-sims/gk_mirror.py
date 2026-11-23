#!/usr/bin/env python3
"""The Green Knight, positional model (1.2.2, #412): the exact mirror of UT_RaidSim122GreenKnight.GreenKnightFight.

His side, from his script and the server (OFGreenKnight.cs, GKTrees, StandardMobBrain, HealSpellHandler):
- 100,000 health. He hits his target once a swing (3.616 s) for tank_hit x 3.616 (x 1.106 on mail); on 30% of his swings
  Might of the Forest, 200 (250 heat after resists, assumed) on every raider within 500 of his target. Bots hit him from
  within 200 of him (GameLiving.MeleeAttackRange), so his fighters stand in it.
- Every 40-60 s he picks a random healer within 2,500, walks to it at 400 and fights it for 6 s (assumed: till a tank
  takes him back), then walks back.
- Every 25-35 s a tree (800 health) at every living raider within 4,000 of him, at most 70 a wave, in a random order.
- A tree takes one raider within 800 of it by proximity (CheckNpcAggro adds the first and returns: uniform here), walks
  at 225, hits by its victim's armour (tree_hit a second) and turns to the highest effective threat: damage; half of
  each heal on a raider it hits (HealSpellHandler); past 250 from the tree a threat counts less, a third at 1,500
  (StandardMobBrain EFFECTIVE_AGGRO_EXPONENT). Ties keep the current target, then the lowest raider. No threat left
  and no one within 800: it goes back to where it grew. Trees never despawn.
- A tree hitting a caster or healer silences it (GameLiving.StartInterruptTimer: 94% a swing; here: all the time).
- At every 10% he walks to one of four spots and heals 400 there: a pause of 12 s (assumed) in which no one hits him and
  he hits no one.
The raid: parties of 8 (tank, three melee, two casters (the first a Wizard in every other party), two healers); runs at
191 (GamePlayer.PLAYER_BASE_SPEED); 400 a raider-minute on him (b1b6846 ledger); dps a second on a tree; 400 a heal, a
3 s cast, range 2,000; Wizards 134 (Ebullient Blast 158, radius 350, range 1,500, 15% resists) on every tree around the
one they aim at, every 3 s; a choice every 2 s. The fight is lost when half the raid has fallen.
"""
import math
import sys

M64 = (1 << 64) - 1


class Rng:
    """UT_RaidSim121SummonersGreenKnight.Rng: 64-bit LCG."""
    __slots__ = ("x",)

    def __init__(self, seed):
        self.x = (seed * 2654435761 + 12345) & M64

    def next(self):
        self.x = (self.x * 6364136223846793005 + 1442695040888963407) & M64
        return (self.x >> 11) * (1.0 / (1 << 53))

    def below(self, n):
        return min(n - 1, int(self.next() * n))


TANK, MELEE, CASTER, HEALER = 0, 1, 2, 3
LAYOUT = [TANK, MELEE, MELEE, MELEE, CASTER, CASTER, HEALER, HEALER]
MAXHP = [2600.0, 2000.0, 1400.0, 1600.0]
STEP = 0.25
TREE_HP, WAVE_CAP, TREE_RUN, RUN = 800.0, 70, 225.0, 191.0
TREE_AGGRO, GROW_RADIUS, PICK_RADIUS, HEAT_RADIUS = 800.0, 4000.0, 2500.0, 500.0
HEAL_RANGE, SPELL_RANGE, AREA_RADIUS, REACH, GK_REACH = 2000.0, 1500.0, 350.0, 150.0, 220.0
ON_GK, AREA_HIT, HEAT_HIT, HEAL, CAST, SWING, PICK_S, WALK_S = 400.0 / 60.0, 134.0, 200.0, 400.0, 3.0, 3.616, 6.0, 12.0
DECAY = math.log(1 / 3.0) / (1500 - 250)
# places (him at 0,0; his tank west of him; "away from his target" is east)
TANK_AT, FIGHT_AT = (-180.0, 0.0), (180.0, 0.0)
SPREAD_RING, STACK_AT = 1000.0, (750.0, 0.0)                    # the 1.2 catalog's ring; 1.2.1 round 2's stack
# 1.2.2 tag and burn (EpicEncounterTactics.GreenKnight*): healers 850 east, area casters 500 east with two melee of
# their party, the casters and archers without an area spell 4,600 west (past 4,000: no tree grows at them).
HEAL_SPOT, BURN_SPOT, OUT_SPOT = (850.0, 0.0), (500.0, 0.0), (-4600.0, 0.0)
TAG, TAG_REACH, PEEL_REACH, GUARD_REACH, GUARDS = 240.0, 300.0, 500.0, 300.0, 2
TANK_HEAL_BELOW, HEAL_BELOW, TREED_HEAL_BELOW, OWN_BELOW = 98, 85, 50, 50

BEFORE, ROUND2, TAGBURN = 0, 1, 2
RULE_NAMES = {0: "Before", 1: "Round2", 2: "TagBurn"}

# a tree's hit a second by its victim's armour (UT_RaidRealStats122): tank plate, melee and healers mail, casters cloth;
# his hit a second on plate. high: no buffs or resists; low: a capped spec AF buff and 20% resists.
CASES = {"high": ([32.5, 35.9, 77.7, 35.9], 338.0), "low": ([22.5, 24.9, 53.3, 24.9], 234.0)}


def dist(ax, ay, bx, by):
    dx = ax - bx
    dy = ay - by
    return math.sqrt(dx * dx + dy * dy)


def eff(raw, d):
    return raw if d <= 250 else raw * math.exp(DECAY * (d - 250))


class Tree:
    __slots__ = ("id", "hp", "x", "y", "hx", "hy", "target", "threat")

    def __init__(self, id_, x, y):
        self.id = id_
        self.hp = TREE_HP
        self.x = x
        self.y = y
        self.hx = x
        self.hy = y
        self.target = -1
        self.threat = {}


def fight(seed, rule, n, dps, case="high", minutes=45):
    """Returns (won, minutes, max_trees, fallen, his_health_left_percent)."""
    tree_hit, tank_hit = CASES[case]
    rng = Rng(seed)
    role = [LAYOUT[i % 8] for i in range(n)]
    party = [i // 8 for i in range(n)]
    wiz = [i % 8 == 4 and party[i] % 2 == 0 for i in range(n)]
    mx = [MAXHP[r] for r in role]
    hp = mx[:]
    dead = [False] * n
    inside = [True] * n
    guard = [False] * n
    home = [FIGHT_AT] * n
    tank = 0
    k = 0
    soft_count = sum(1 for r in role if r >= CASTER)
    for i in range(n):
        r = role[i]
        if i == tank:
            home[i] = TANK_AT
        elif r <= MELEE:
            home[i] = FIGHT_AT
        elif rule == BEFORE:
            a = 2 * math.pi * k / soft_count
            k += 1
            home[i] = (SPREAD_RING * math.cos(a), SPREAD_RING * math.sin(a))
        elif rule == ROUND2:
            home[i] = STACK_AT
        elif r == HEALER:
            home[i] = HEAL_SPOT
        elif wiz[i]:
            home[i] = BURN_SPOT
        else:
            home[i] = OUT_SPOT
            inside[i] = False
    if rule == TAGBURN:
        for p in range(party[n - 1] + 1):
            if not any(wiz[i] and party[i] == p for i in range(n)):
                continue
            g = 0
            for i in range(n):
                if party[i] == p and role[i] == MELEE and g < GUARDS:
                    guard[i] = True
                    home[i] = BURN_SPOT
                    g += 1
    px = [h[0] for h in home]
    py = [h[1] for h in home]
    on = [-1] * n
    cast_end = [-1.0] * n
    cast_on = [-1] * n
    trees = []
    by_id = {}
    nid = 0
    now = 0.0
    gk = 100000.0
    gkx, gky = 0.0, 0.0
    next_wave = 25 + rng.below(10001) / 1000.0
    next_pick = 40 + rng.below(20001) / 1000.0
    pick_until = -1.0
    picked = -1
    next_swing = SWING
    walks = 0
    pause_until = -1.0
    next_choice = 0.0
    fallen = 0
    max_trees = 0
    end = minutes * 60.0
    while now < end:
        # ---- his waves, picks and walks --------------------------------------------------------------------------
        paused = now < pause_until
        if now >= next_wave and not paused:
            order = [i for i in range(n) if not dead[i] and inside[i] and dist(px[i], py[i], gkx, gky) <= GROW_RADIUS]
            for j in range(len(order) - 1, 0, -1):
                q = rng.below(j + 1)
                order[j], order[q] = order[q], order[j]
            for i in order[:WAVE_CAP]:
                t = Tree(nid, px[i], py[i])
                nid += 1
                trees.append(t)
                by_id[t.id] = t
            next_wave = now + 25 + rng.below(10001) / 1000.0
        if now >= next_pick:
            hs = [i for i in range(n) if role[i] == HEALER and not dead[i] and dist(px[i], py[i], gkx, gky) <= PICK_RADIUS]
            if hs:
                picked = hs[rng.below(len(hs))]
                pick_until = now + PICK_S
            next_pick = now + 40 + rng.below(20001) / 1000.0
        if int(math.floor((100000 - gk) / 10000)) > walks and walks < 9 and not paused:
            walks += 1
            pause_until = now + WALK_S
            gk = min(100000.0, gk + 400)
            paused = True
        if dead[tank]:
            c = -1
            for want in (TANK, MELEE, -1):
                for i in range(n):
                    if not dead[i] and inside[i] and (want < 0 or role[i] == want) and not guard[i]:
                        c = i
                        break
                if c >= 0:
                    break
            if c < 0:
                break
            tank = c
            home[tank] = TANK_AT
        target = picked if now < pick_until and picked >= 0 and not dead[picked] else tank
        if not paused:
            ax, ay = (px[target], py[target]) if target != tank else (0.0, 0.0)
            d = dist(gkx, gky, ax, ay)
            lim = 180.0 if target != tank else 0.0
            if d > lim:
                s = min(400 * STEP, d - lim)
                gkx += (ax - gkx) / d * s
                gky += (ay - gky) / d * s
        # ---- the trees (each thinks once a second) ---------------------------------------------------------------
        tick = int(now * 4)
        for t in trees:
            if (tick + t.id) % 4 == 0:
                th = t.threat
                for q in [q for q in th if dead[q]]:
                    del th[q]
                if not th:
                    near = [i for i in range(n) if not dead[i] and dist(px[i], py[i], t.x, t.y) <= TREE_AGGRO]
                    if near:
                        th[near[rng.below(len(near))]] = 1.0
                best, bv = -1, -1.0
                for q in sorted(th):
                    e = eff(th[q], dist(px[q], py[q], t.x, t.y))
                    if e > bv:
                        best, bv = q, e
                if t.target in th and best != t.target and eff(th[t.target], dist(px[t.target], py[t.target], t.x, t.y)) >= bv:
                    best = t.target
                t.target = best
            if t.target >= 0:
                gx, gy, lim = px[t.target], py[t.target], REACH * 0.8
            else:
                gx, gy, lim = t.hx, t.hy, 0.0
            d = dist(t.x, t.y, gx, gy)
            if d > lim:
                s = min(TREE_RUN * STEP, d - lim)
                t.x += (gx - t.x) / d * s
                t.y += (gy - t.y) / d * s
        hitting = {}
        for t in trees:
            if t.target >= 0 and dist(t.x, t.y, px[t.target], py[t.target]) <= REACH:
                hitting.setdefault(t.target, []).append(t)
        # ---- the raid chooses ------------------------------------------------------------------------------------
        if now >= next_choice:
            next_choice = now + 2
            choose(rule, n, role, party, wiz, guard, dead, inside, tank, px, py, trees, on, cast_on)
        # ---- the raid acts ---------------------------------------------------------------------------------------
        for i in range(n):
            if dead[i] or not inside[i]:
                continue
            r = role[i]
            t = by_id.get(on[i]) if on[i] >= 0 else None
            if t is not None and t.hp <= 0:
                t = None
            if r <= MELEE and t is not None:
                gx, gy, lim = t.x, t.y, REACH * 0.6
            else:
                gx, gy, lim = home[i][0], home[i][1], 20.0
            d = dist(px[i], py[i], gx, gy)
            moving = d > lim
            if moving:
                s = min(RUN * STEP, d - lim)
                px[i] += (gx - px[i]) / d * s
                py[i] += (gy - py[i]) / d * s
            if r <= MELEE:
                if t is not None:
                    if dist(px[i], py[i], t.x, t.y) <= REACH:
                        t.hp -= dps * STEP
                        t.threat[i] = t.threat.get(i, 0.0) + dps * STEP
                elif on[i] < 0 and not paused and dist(px[i], py[i], gkx, gky) <= GK_REACH:
                    gk -= ON_GK * STEP
                continue
            if i in hitting or moving:
                cast_end[i] = -1.0
                continue
            if r == CASTER:
                if on[i] == -2:
                    if cast_end[i] < 0:
                        cast_end[i] = now + CAST
                    elif now >= cast_end[i]:
                        c = by_id.get(cast_on[i])
                        if c is not None and c.hp > 0 and dist(px[i], py[i], c.x, c.y) <= SPELL_RANGE:
                            cx, cy = c.x, c.y
                            for u in trees:
                                if u.hp > 0 and dist(u.x, u.y, cx, cy) <= AREA_RADIUS:
                                    u.hp -= AREA_HIT
                                    u.threat[i] = u.threat.get(i, 0.0) + AREA_HIT
                        cast_end[i] = -1.0
                    continue
                cast_end[i] = -1.0
                if t is not None:
                    if dist(px[i], py[i], t.x, t.y) <= SPELL_RANGE:
                        t.hp -= dps * STEP
                        t.threat[i] = t.threat.get(i, 0.0) + dps * STEP
                elif not paused and dist(px[i], py[i], gkx, gky) <= SPELL_RANGE:
                    gk -= ON_GK * STEP
                continue
            # healers
            if cast_end[i] >= 0 and now >= cast_end[i]:
                m = cast_on[i]
                if not dead[m] and dist(px[i], py[i], px[m], py[m]) <= HEAL_RANGE:
                    amount = min(HEAL, mx[m] - hp[m])
                    hp[m] += amount
                    for u in hitting.get(m, ()):
                        u.threat[i] = u.threat.get(i, 0.0) + 0.5 * amount
                cast_end[i] = -1.0
            if cast_end[i] < 0:
                m = patient(rule, i, n, role, party, dead, hp, mx, px, py, target, hitting)
                if m >= 0:
                    cast_on[i] = m
                    cast_end[i] = now + CAST
        trees = [t for t in trees if t.hp > 0]
        if len(by_id) > 4 * len(trees) + 64:
            by_id = {t.id: t for t in trees}
        if len(trees) > max_trees:
            max_trees = len(trees)
        # ---- damage on the raid ------------------------------------------------------------------------------------
        for v in sorted(hitting):
            if not dead[v]:
                hp[v] -= tree_hit[role[v]] * STEP * sum(1 for t in hitting[v] if t.hp > 0)
        if not paused and dist(gkx, gky, px[target], py[target]) <= GK_REACH + 20:
            if now >= next_swing:
                next_swing = now + SWING
                hp[target] -= tank_hit * SWING * (1.0 if role[target] == TANK else 1.106)
                if rng.next() < 0.3:
                    cx, cy = px[target], py[target]
                    for i in range(n):
                        if not dead[i] and dist(px[i], py[i], cx, cy) <= HEAT_RADIUS:
                            hp[i] -= HEAT_HIT
        elif now >= next_swing:
            next_swing = now
        for i in range(n):
            if not dead[i] and hp[i] <= 0:
                dead[i] = True
                fallen += 1
        if gk <= 0:
            return (True, now / 60, max_trees, fallen, 0.0)
        if fallen * 2 >= n:
            return (False, now / 60, max_trees, fallen, gk / 1000)
        now += STEP
    return (False, now / 60, max_trees, fallen, gk / 1000)


def patient(rule, i, n, role, party, dead, hp, mx, px, py, target, hitting):
    """Whom a free healer heals. Before and round 2: the most hurt under 80% of its party, else of the raid. Tag and
    burn (EpicEncounterTactics.GreenKnightTankPatient, GreenKnightHoldsHeal): his target under 98% first; then the most
    hurt under 85% of its party, else of the raid, leaving a raider a tree hits until under half (half of a heal on it is
    threat on that tree, and pulls it onto the healer)."""
    if rule == TAGBURN and not dead[target] and hp[target] * 100 / mx[target] < TANK_HEAL_BELOW and \
            dist(px[i], py[i], px[target], py[target]) <= HEAL_RANGE:
        return target
    below = HEAL_BELOW if rule == TAGBURN else 80
    for own in (True, False):
        best = -1
        for m in range(n):
            if dead[m] or (own and party[m] != party[i]) or hp[m] * 100 / mx[m] >= below:
                continue
            if dist(px[i], py[i], px[m], py[m]) > HEAL_RANGE:
                continue
            if rule == TAGBURN and m in hitting and hp[m] * 100 / mx[m] >= TREED_HEAL_BELOW:
                continue
            if best < 0 or hp[m] * mx[best] < hp[best] * mx[m]:
                best = m
        if best >= 0:
            return best
    return -1


def clump(ts, x, y, need):
    """The tree at the centre of the most trees within the area radius, within spell range; at least need."""
    best, bc = None, 0
    for t in ts:
        if dist(x, y, t.x, t.y) > SPELL_RANGE:
            continue
        c = sum(1 for u in ts if dist(u.x, u.y, t.x, t.y) <= AREA_RADIUS)
        if c > bc:
            best, bc = t, c
    return best if bc >= need else None


def holder(t, role):
    """A fighter's threat on the tree it hits (0: it hits no fighter)."""
    v = t.target
    return t.threat.get(v, 0.0) if v >= 0 and role[v] <= MELEE else 0.0


def choose(rule, n, role, party, wiz, guard, dead, inside, tank, px, py, trees, on, cast_on):
    live = [t for t in trees if t.hp > 0]
    area_left = any(wiz[i] and not dead[i] and inside[i] for i in range(n))
    for i in range(n):
        if dead[i] or not inside[i] or i == tank:
            continue
        r = role[i]
        if r <= MELEE:
            if rule == BEFORE:
                on[i] = -1
            elif rule == ROUND2:
                on[i] = round2_tree(i, live, role, wiz, px, py)
            else:
                on[i] = tagburn_tree(i, guard[i], live, role, px, py, area_left)
        elif r == CASTER:
            on[i] = -1
            if rule == BEFORE:
                if wiz[i]:
                    c = clump(live, px[i], py[i], 2)
                    if c is not None:
                        on[i] = -2
                        cast_on[i] = c.id
                        continue
                if live:
                    on[i] = live[party[i] % len(live)].id
            elif rule == ROUND2:
                if wiz[i]:
                    c = clump([t for t in live if dist(t.x, t.y, 0.0, 0.0) <= 400], px[i], py[i], 2)
                    if c is not None:
                        on[i] = -2
                        cast_on[i] = c.id
            elif wiz[i]:
                c = burn_clump(i, live, role, px, py)
                if c is not None:
                    on[i] = -2
                    cast_on[i] = c.id


def round2_tree(i, live, role, wiz, px, py):
    """EpicEncounterTactics.GreenKnightPeelTree (1.2.1 round 2): a tree on an area caster, then a healer, then a caster
    within 1,200 (spread by raider), else the most hurt on me, else him."""
    best = 99
    for t in live:
        v = t.target
        if v < 0 or v == i or dist(px[i], py[i], t.x, t.y) > 1200:
            continue
        rank = 0 if wiz[v] else 1 if role[v] == HEALER else 2 if role[v] == CASTER else 3
        if rank < best:
            best = rank
    if best <= 2:
        soft = [t for t in live if t.target >= 0 and t.target != i and dist(px[i], py[i], t.x, t.y) <= 1200 and
                (0 if wiz[t.target] else 1 if role[t.target] == HEALER else 2 if role[t.target] == CASTER else 3) == best]
        return soft[i % len(soft)].id
    pick = None
    for t in live:
        if t.target == i and (pick is None or t.hp < pick.hp):
            pick = t
    return pick.id if pick is not None else -1


def tagburn_tree(i, is_guard, live, role, px, py, area_left):
    """EpicEncounterTactics.GreenKnightTagTree: finish my tag on a tree that hits me (my threat under TAG); else a tree
    on a healer or caster within PEEL_REACH (spread by raider); else a tree within TAG_REACH that no fighter holds yet,
    mine first, nearest; then the most hurt tree on me (a guard: or within GUARD_REACH) that is at most OWN_BELOW% (any,
    for a guard or with no area caster left: the area casters burn the held trees); else him."""
    for t in live:
        mine = t.threat.get(i, 0.0)
        if t.target == i and 0 < mine < TAG and dist(px[i], py[i], t.x, t.y) <= TAG_REACH:
            return t.id
    soft = [t for t in live if t.target >= 0 and t.target != i and role[t.target] >= CASTER and
            dist(px[i], py[i], t.x, t.y) <= PEEL_REACH]
    if soft:
        return soft[i % len(soft)].id
    best, key = None, None
    for t in live:
        d = dist(px[i], py[i], t.x, t.y)
        if d > TAG_REACH or holder(t, role) >= TAG:
            continue
        kk = (0 if t.target == i else 1, d)
        if key is None or kk < key:
            best, key = t, kk
    if best is not None:
        return best.id
    pick = None
    for t in live:
        if not is_guard and area_left and pct(t) > OWN_BELOW:
            continue
        if (t.target == i or is_guard and dist(px[i], py[i], t.x, t.y) <= GUARD_REACH) and (pick is None or pct(t) < pct(pick)):
            pick = t
    return pick.id if pick is not None else -1


def pct(t):
    return int(math.floor(t.hp * 100 / TREE_HP))


def burn_clump(i, live, role, px, py):
    """EpicEncounterTactics.GreenKnightBurnTree: the held trees (a fighter's threat at least TAG) within spell range that
    this caster's next spell would not pull (its threat after the spell, counted at its distance, below the holder's);
    the centre of the biggest clump of two or more."""
    ok = []
    for t in live:
        d = dist(px[i], py[i], t.x, t.y)
        h = holder(t, role)
        if d > SPELL_RANGE or h < TAG or eff(t.threat.get(i, 0.0) + AREA_HIT, d) >= h:
            continue
        ok.append(t)
    return clump(ok, px[i], py[i], 2)


if __name__ == "__main__":
    import time
    t0 = time.time()
    rule = int(sys.argv[1]) if len(sys.argv) > 1 else TAGBURN
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    case = sys.argv[3] if len(sys.argv) > 3 else "low"
    for s in range(4):
        print(fight(s, rule, n, 80.0, case))
    print("t", round(time.time() - t0, 2))
