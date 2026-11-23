#!/usr/bin/env python3
"""The Green Knight, positional model with every role (1.2.2 rework, #412): the mirror of
UT_RaidSim122GreenKnight.GreenKnightFight.

His side (OFGreenKnight.cs, GKTrees, StandardMobBrain, HealSpellHandler), as in the first 1.2.2 model:
- 100,000 health; one hit on his target a swing (3.616 s) for tank_hit x 3.616 (x 1.106 on mail); on 30% of his swings
  Might of the Forest, 200 (assumed after resists), on everyone within 500 of his target, pets too.
- Every 40-60 s a random healer within 2,500: he walks to it at 400 and fights it 6 s (assumed), then walks back.
- Every 25-35 s a tree (800 health) at every living raider within 4,000 of him (players and bots; never at a pet:
  EncounterTargets.InRadius), at most 70 a wave, in a random order.
- A tree takes one living thing within 800 of it, pets included (StandardMobBrain.CheckNpcAggro), walks at 225, hits by
  its victim's armour and turns to the highest effective threat (damage; half of each heal on the one it hits), counted
  less past 250 from it (a third at 1,500). No threat and nobody within 800: back to where it grew. Never despawns.
- A tree hitting a caster, archer or healer silences it (94% a swing; here, while it hits).
- At every 10% he walks at 400 to one of his four points (OFGreenKnight.GkTeleport; not the one he last healed at),
  attacks no one on the way, heals 400 there and 4 s later goes back for his target. Tag and burn: the raid holds its
  ground (he comes back to his tank); the 1.2 rules: the raid follows him there.
The raid: parties of 8: tank, three melee, A, B, two healers. A: a Wizard (area spell) in even parties, a pet owner with a
lasting pet (Necromancer or Cabalist) in parties 1, 5, 9 ..., a Theurgist in parties 3, 7, 11 ...; B: a caster without an
area spell in even parties, an archer in odd ones. Everyone's damage counts only from where it reaches: melee within 200
of him (GameLiving.MeleeAttackRange), spells 1,500, bows 1,700 (GamePlayer bow ranges, longbow 1,760), heals 2,000, a
Theurgist's summon 2,000 (SummonTheurgistPet). 400 a raider-minute on him (b1b6846 ledger); dps a second on a tree from a
melee raider, a shot or an arrow (assumed); a Wizard's 134 on every tree within 350 of its aim; a 400 heal every 3 s; 191
a second on foot; a choice every 2 s. Pets (assumed): a lasting pet has 2,600 health, mail-like armour, 400 a minute on him
and dps on a tree, and comes back 20 s after it dies, walking from its owner; a Theurgist's elemental lives 30 s (or till
its target dies), has 900 health and deals half a raider's damage. Pet owners stand 4,600 out (no tree grows at a pet).
Lost when no raider is left fighting (the game ends an attempt when he leaves combat; it is a wipe when half the engaged
have fallen), or after 45 minutes. Buffs as the bots cast them: 'buffed' is a Cleric's or Paladin's capped spec AF;
Albion casts no melee resist, and nothing resists slash.
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


TANK, MELEE, CASTER, HEALER, WIZARD, ARCHER, OWNER, THEURG, PET, TPET = range(10)
ROLE_NAMES = ["tank", "melee", "caster", "healer", "wizard", "archer", "petowner", "theurgist", "pet", "elemental"]
MAXHP = [2600.0, 2000.0, 1400.0, 1600.0, 1400.0, 1700.0, 1400.0, 1400.0, 2600.0, 900.0]
# a tree's hit a second by its victim's armour (UT_RaidRealStats122): plate, mail, cloth, mail, cloth, leather, cloth,
# cloth, a lasting pet as mail, an elemental as mail. unbuffed: no AF buff; buffed: the capped spec AF buff a Cleric
# (Holy Barrier) or Paladin (Crusader's Mantle) casts. Albion has no melee resist buff, and his trees and his sword
# slash, which no Albion armour resists (SkillBase.InitArmorResists).
PLATE, MAIL, CLOTH, LEATHER = (32.5, 28.1), (35.9, 31.1), (77.7, 66.6), (44.3, 38.3)
ARMOUR = [PLATE, MAIL, CLOTH, MAIL, CLOTH, LEATHER, CLOTH, CLOTH, MAIL, MAIL]
GK_HIT = (338.0, 292.5)
STEP = 0.25
TREE_HP, WAVE_CAP, TREE_RUN, RUN = 800.0, 70, 225.0, 191.0
TREE_AGGRO, GROW_RADIUS, PICK_RADIUS, HEAT_RADIUS = 800.0, 4000.0, 2500.0, 500.0
HEAL_RANGE, SPELL_RANGE, BOW_RANGE, SUMMON_RANGE = 2000.0, 1500.0, 1700.0, 2000.0
AREA_RADIUS, REACH, GK_REACH = 350.0, 150.0, 220.0
ON_GK, AREA_HIT, HEAT_HIT, HEAL, CAST, SWING, PICK_S = 400.0 / 60.0, 134.0, 200.0, 400.0, 3.0, 3.616, 6.0
PET_BACK_S, TPET_LIFE, TPET_SHARE, TPETS = 20.0, 30.0, 0.5, 10
# his four walk points (OFGreenKnight.GkTeleport), from his spawn (592990, 418687)
POINTS = [(203.0, -2206.0), (266.0, 2093.0), (3063.0, 1484.0), (-2114.0, -635.0)]
GK_RUN, ARRIVE, AFTER_HEAL_S = 400.0, 40.0, 4.0
HOLD = True                    # tag and burn: the raid holds its ground while he walks; he comes back to his target
DECAY = math.log(1 / 3.0) / (1500 - 250)
TANK_AT, FIGHT_AT = (-180.0, 0.0), (180.0, 0.0)
SPREAD_RING, STACK_AT = 1000.0, (750.0, 0.0)
HEAL_SPOT, BURN_SPOT, OUT_SPOT = (650.0, 0.0), (700.0, 0.0), (-4600.0, 0.0)
TAG, TAG_REACH, PEEL_REACH, GUARD_REACH, GUARDS = 240.0, 300.0, 500.0, 300.0, 1
TANK_HEAL_BELOW, HEAL_BELOW, TREED_HEAL_BELOW, OWN_BELOW = 98, 85, 35, 50
SHOOTERS_AT = (1300.0, 0.0)    # the casters and archers without an area spell stand 1,300 from him (tag and burn)
THEURG_AT = (850.0, 0.0)       # Theurgists behind the healers

TANK_HEALERS = 4               # healers (nearest his target) who heal it first

BEFORE, ROUND2, TAGBURN = 0, 1, 2
RULE_NAMES = {0: "Before", 1: "Round2", 2: "TagBurn"}


def dist(ax, ay, bx, by):
    dx = ax - bx
    dy = ay - by
    return math.sqrt(dx * dx + dy * dy)


def eff(raw, d):
    return raw if d <= 250 else raw * math.exp(DECAY * (d - 250))


def soft(r):
    """Silenced by a tree's hits: casters, archers, healers, pet owners, Theurgists."""
    return CASTER <= r <= THEURG


def fighter(r):
    """Holds a tree with its hits: tanks, melee, pets."""
    return r <= MELEE or r >= PET


def shoot_range(r):
    return BOW_RANGE if r == ARCHER else SPELL_RANGE


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


def layout(n):
    role = []
    for i in range(n):
        p, s = i // 8, i % 8
        if s == 0:
            role.append(TANK)
        elif s <= 3:
            role.append(MELEE)
        elif s == 4:
            role.append(WIZARD if p % 2 == 0 else OWNER if p % 4 == 1 else THEURG)
        elif s == 5:
            role.append(CASTER if p % 2 == 0 else ARCHER)
        else:
            role.append(HEALER)
    return role


def fight(seed, rule, n, dps, case="low", minutes=45, stats=None):
    """Returns (won, minutes, max_trees, fallen, his_health_left_percent). stats (a dict) gets each role's damage on him,
    on trees, trees killed and heals."""
    ci = 0 if case == "unbuffed" else 1
    tank_hit = GK_HIT[ci]
    rng = Rng(seed)
    role = layout(n)
    owner = [-1] * n
    for i in range(n):
        if role[i] == OWNER:
            role.append(PET)
            owner.append(i)
    for i in range(n):
        if role[i] == THEURG:
            for _ in range(TPETS):
                role.append(TPET)
                owner.append(i)
    N = len(role)
    party = [i // 8 if i < n else owner[i] // 8 for i in range(N)]
    mx = [MAXHP[r] for r in role]
    hp = mx[:]
    dead = [role[i] == TPET for i in range(N)]
    inside = [True] * N
    guard = [False] * N
    home = [FIGHT_AT] * N
    tank = 0
    k = 0
    soft_count = sum(1 for i in range(n) if soft(role[i]))
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
        elif r == THEURG:
            home[i] = THEURG_AT
        elif r == WIZARD:
            home[i] = BURN_SPOT
        elif r == OWNER:
            home[i] = OUT_SPOT
            inside[i] = False
        else:
            home[i] = SHOOTERS_AT
    if rule == TAGBURN:
        for p in range(party[n - 1] + 1):
            if not any(role[i] == WIZARD and party[i] == p for i in range(n)):
                continue
            g = 0
            for i in range(n):
                if party[i] == p and role[i] == MELEE and g < GUARDS:
                    guard[i] = True
                    home[i] = BURN_SPOT
                    g += 1
    if rule == TAGBURN:
        for i in range(n, N):
            if role[i] == PET:
                home[i] = BURN_SPOT
                guard[i] = True
    px = [h[0] for h in home]
    py = [h[1] for h in home]
    for i in range(n, N):
        if role[i] == TPET:
            px[i], py[i] = px[owner[i]], py[owner[i]]
    on = [-1] * N
    cast_end = [-1.0] * N
    cast_on = [-1] * N
    expire = [0.0] * N
    back_at = [-1.0] * N
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
    walking = False
    walk_end = -1.0
    last_point = -1
    ox, oy = 0.0, 0.0
    wx, wy = 0.0, 0.0
    contrib = [0.0] * n
    credit = [i if i < n else owner[i] for i in range(N)]
    next_choice = 0.0
    fallen = 0
    max_trees = 0
    end = minutes * 60.0
    R = len(ROLE_NAMES)
    on_gk = [0.0] * R
    on_trees = [0.0] * R
    kills = [0] * R
    heals = [0.0] * R
    last_hit = {}

    def hit_tree(t, i, d):
        contrib[credit[i]] += d
        t.hp -= d
        t.threat[i] = t.threat.get(i, 0.0) + d
        on_trees[role[i]] += d
        last_hit[t.id] = role[i]

    result = None
    while now < end:
        paused = walking
        if now >= next_wave:
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
        if now >= next_pick and not walking:
            hs = [i for i in range(n) if role[i] == HEALER and not dead[i] and dist(px[i], py[i], gkx, gky) <= PICK_RADIUS]
            if hs:
                picked = hs[rng.below(len(hs))]
                pick_until = now + PICK_S
            next_pick = now + 40 + rng.below(20001) / 1000.0
        if int(math.floor((100000 - gk) / 10000)) > walks and walks < 9 and not walking:
            walks += 1
            choices = [q for q in range(4) if q != last_point]
            last_point = choices[rng.below(len(choices))]
            wx, wy = POINTS[last_point]
            if rule != TAGBURN or not HOLD:
                ox, oy = wx, wy
            walking = True
            walk_end = -1.0
            pick_until = -1.0
            paused = True
        if walking:
            d = dist(gkx, gky, wx, wy)
            if d > ARRIVE:
                s = min(GK_RUN * STEP, d)
                gkx += (wx - gkx) / d * s
                gky += (wy - gky) / d * s
            elif walk_end < 0:
                walk_end = now + AFTER_HEAL_S
                gk = min(100000.0, gk + 400)
            elif now >= walk_end:
                walking = False
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
            guard[tank] = False
        target = picked if now < pick_until and picked >= 0 and not dead[picked] else tank
        if not walking:
            ax, ay = (px[target], py[target]) if target != tank else (ox, oy)
            d = dist(gkx, gky, ax, ay)
            lim = 180.0 if target != tank else 0.0
            if d > lim:
                s = min(400 * STEP, d - lim)
                gkx += (ax - gkx) / d * s
                gky += (ay - gky) / d * s
        # ---- pets come and go --------------------------------------------------------------------------------------
        for i in range(n, N):
            if role[i] == PET and dead[i] and back_at[i] >= 0 and now >= back_at[i] and not dead[owner[i]]:
                dead[i] = False
                hp[i] = mx[i]
                px[i], py[i] = px[owner[i]], py[owner[i]]
                back_at[i] = -1.0
            elif role[i] == TPET and not dead[i]:
                t = by_id.get(on[i]) if on[i] >= 0 else None
                if now >= expire[i] or on[i] >= 0 and (t is None or t.hp <= 0):
                    dead[i] = True
        # ---- the trees ---------------------------------------------------------------------------------------------
        tick = int(now * 4)
        for t in trees:
            if (tick + t.id) % 4 == 0:
                th = t.threat
                for q in [q for q in th if dead[q]]:
                    del th[q]
                if not th:
                    near = [i for i in range(N) if not dead[i] and inside[i] and dist(px[i], py[i], t.x, t.y) <= TREE_AGGRO]
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
        # ---- the raid chooses --------------------------------------------------------------------------------------
        if now >= next_choice:
            next_choice = now + 2
            choose(rule, n, role, party, guard, dead, inside, tank, px, py, trees, on, cast_on, dps, gkx, gky)
        # ---- the raid acts -----------------------------------------------------------------------------------------
        for i in range(N):
            if dead[i] or not inside[i]:
                continue
            r = role[i]
            t = by_id.get(on[i]) if on[i] >= 0 else None
            if t is not None and t.hp <= 0:
                t = None
            melee_like = r <= MELEE or r >= PET
            if melee_like and t is not None:
                gx, gy, lim = t.x, t.y, REACH * 0.6
            elif r == TPET or r == PET:
                # a pet with no tree to take goes for him (its owner's target)
                gx, gy, lim = gkx, gky, GK_REACH * 0.8
            else:
                gx, gy, lim = ox + home[i][0], oy + home[i][1], 20.0
            d = dist(px[i], py[i], gx, gy)
            moving = d > lim
            if moving:
                s = min(RUN * STEP, d - lim)
                px[i] += (gx - px[i]) / d * s
                py[i] += (gy - py[i]) / d * s
            if melee_like:
                share = TPET_SHARE if r == TPET else 1.0
                if t is not None:
                    if dist(px[i], py[i], t.x, t.y) <= REACH:
                        hit_tree(t, i, dps * share * STEP)
                elif on[i] < 0 and dist(px[i], py[i], gkx, gky) <= GK_REACH:
                    gk -= ON_GK * share * STEP
                    on_gk[r] += ON_GK * share * STEP
                    contrib[credit[i]] += ON_GK * share * STEP
                continue
            if i in hitting or moving:
                cast_end[i] = -1.0
                continue
            if r == WIZARD and on[i] == -2:
                if cast_end[i] < 0:
                    cast_end[i] = now + CAST
                elif now >= cast_end[i]:
                    c = by_id.get(cast_on[i])
                    if c is not None and c.hp > 0 and dist(px[i], py[i], c.x, c.y) <= SPELL_RANGE:
                        cx, cy = c.x, c.y
                        for u in trees:
                            if u.hp > 0 and dist(u.x, u.y, cx, cy) <= AREA_RADIUS:
                                hit_tree(u, i, AREA_HIT)
                    cast_end[i] = -1.0
                continue
            if r == THEURG:
                if cast_end[i] < 0:
                    cast_end[i] = now + CAST
                elif now >= cast_end[i]:
                    cast_end[i] = -1.0
                    c = by_id.get(cast_on[i]) if cast_on[i] >= 0 else None
                    reach = dist(px[i], py[i], c.x, c.y) if c is not None and c.hp > 0 else dist(px[i], py[i], gkx, gky)
                    if (cast_on[i] < 0 or c is not None and c.hp > 0) and reach <= SUMMON_RANGE:
                        for e in range(n, N):
                            if role[e] == TPET and owner[e] == i and dead[e]:
                                dead[e] = False
                                hp[e] = mx[e]
                                px[e], py[e] = px[i], py[i]
                                on[e] = cast_on[i]
                                expire[e] = now + TPET_LIFE
                                break
                continue
            if r != HEALER:
                cast_end[i] = -1.0
                if t is not None:
                    if dist(px[i], py[i], t.x, t.y) <= shoot_range(r):
                        hit_tree(t, i, dps * STEP)
                elif dist(px[i], py[i], gkx, gky) <= shoot_range(r):
                    gk -= ON_GK * STEP
                    on_gk[r] += ON_GK * STEP
                    contrib[i] += ON_GK * STEP
                continue
            if cast_end[i] >= 0 and now >= cast_end[i]:
                m = cast_on[i]
                if not dead[m] and dist(px[i], py[i], px[m], py[m]) <= HEAL_RANGE:
                    amount = min(HEAL, mx[m] - hp[m])
                    hp[m] += amount
                    heals[HEALER] += amount
                    contrib[i] += amount
                    for u in hitting.get(m, ()):
                        u.threat[i] = u.threat.get(i, 0.0) + 0.5 * amount
                cast_end[i] = -1.0
            if cast_end[i] < 0:
                m = patient(rule, i, n, role, party, dead, hp, mx, px, py, target, hitting)
                if m >= 0:
                    cast_on[i] = m
                    cast_end[i] = now + CAST
        for t in trees:
            if t.hp <= 0:
                kills[last_hit[t.id]] += 1
        trees = [t for t in trees if t.hp > 0]
        if len(by_id) > 4 * len(trees) + 64:
            by_id = {t.id: t for t in trees}
        if len(trees) > max_trees:
            max_trees = len(trees)
        # ---- damage on the raid and its pets ------------------------------------------------------------------------
        for v in sorted(hitting):
            if not dead[v]:
                hp[v] -= ARMOUR[role[v]][ci] * STEP * sum(1 for t in hitting[v] if t.hp > 0)
        if not paused and dist(gkx, gky, px[target], py[target]) <= GK_REACH + 20:
            if now >= next_swing:
                next_swing = now + SWING
                hp[target] -= tank_hit * SWING * (1.0 if role[target] == TANK else 1.106)
                if rng.next() < 0.3:
                    cx, cy = px[target], py[target]
                    for i in range(N):
                        if not dead[i] and inside[i] and dist(px[i], py[i], cx, cy) <= HEAT_RADIUS:
                            hp[i] -= HEAT_HIT
        elif now >= next_swing:
            next_swing = now
        for i in range(N):
            if not dead[i] and hp[i] <= 0:
                dead[i] = True
                if i < n:
                    fallen += 1
                elif role[i] == PET:
                    back_at[i] = now + PET_BACK_S
        if gk <= 0:
            result = (True, now / 60, max_trees, fallen, 0.0)
            break
        if not any(not dead[i] and inside[i] for i in range(n)):
            result = (False, now / 60, max_trees, fallen, gk / 1000)
            break
        now += STEP
    if result is None:
        result = (False, now / 60, max_trees, fallen, gk / 1000)
    if stats is not None:
        stats.setdefault("contributed", []).append(sum(1 for i in range(n) if contrib[i] > 0))
        stats.setdefault("raiders", []).append(n)
        stats.setdefault("walks", []).append(walks)
        for r in range(R):
            s = stats.setdefault(ROLE_NAMES[r], [0.0, 0.0, 0, 0.0])
            s[0] += on_gk[r]
            s[1] += on_trees[r]
            s[2] += kills[r]
            s[3] += heals[r]
    return result


def patient(rule, i, n, role, party, dead, hp, mx, px, py, target, hitting):
    """Whom a free healer heals (raiders only). Before and round 2: the most hurt under 80% of its party, else of the
    raid. Tag and burn: his target under 98% first; then the most hurt under 85%, leaving a raider a tree hits until under
    half."""
    if rule == TAGBURN and not dead[target] and hp[target] * 100 / mx[target] < TANK_HEAL_BELOW and \
            dist(px[i], py[i], px[target], py[target]) <= HEAL_RANGE and \
            i in tank_healers(n, role, dead, px, py, target):
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


def tank_healers(n, role, dead, px, py, target):
    """EpicEncounterTactics.GreenKnightTankHealers: the TANK_HEALERS living healers nearest his target (ties: lowest)."""
    hs = sorted((dist(px[m], py[m], px[target], py[target]), m) for m in range(n)
                if role[m] == HEALER and not dead[m] and m != target)
    return [m for _, m in hs[:TANK_HEALERS]]


def clump(ts, x, y, need):
    best, bc = None, 0
    for t in ts:
        if dist(x, y, t.x, t.y) > SPELL_RANGE:
            continue
        c = sum(1 for u in ts if dist(u.x, u.y, t.x, t.y) <= AREA_RADIUS)
        if c > bc:
            best, bc = t, c
    return best if bc >= need else None


def holder(t, role):
    """A fighter's (tank, melee, pet) threat on the tree it hits; 0 when it hits anyone else."""
    v = t.target
    return t.threat.get(v, 0.0) if v >= 0 and fighter(role[v]) else 0.0


def pct(t):
    return int(math.floor(t.hp * 100 / TREE_HP))


def choose(rule, n, role, party, guard, dead, inside, tank, px, py, trees, on, cast_on, dps, gkx, gky):
    live = [t for t in trees if t.hp > 0]
    area_left = any(role[i] == WIZARD and not dead[i] for i in range(n))
    for i in range(len(role)):
        if i >= n and not (role[i] == PET and guard[i]):
            continue
        if dead[i] or not inside[i] or i == tank:
            continue
        r = role[i]
        if r <= MELEE or r == PET:
            if rule == BEFORE:
                on[i] = -1
            elif rule == ROUND2:
                on[i] = round2_tree(i, live, role, px, py)
            else:
                on[i] = tagburn_tree(i, guard[i], live, role, px, py, area_left)
            continue
        if r == HEALER:
            continue
        on[i] = -1
        cast_on[i] = -1 if r == THEURG else cast_on[i]
        if rule == BEFORE:
            if r == WIZARD:
                c = clump(live, px[i], py[i], 2)
                if c is not None:
                    on[i] = -2
                    cast_on[i] = c.id
                    continue
            if live:
                pick = live[party[i] % len(live)].id
                if r == THEURG:
                    cast_on[i] = pick
                else:
                    on[i] = pick
        elif rule == ROUND2:
            if r == WIZARD:
                c = clump([t for t in live if dist(t.x, t.y, 0.0, 0.0) <= 400], px[i], py[i], 2)
                if c is not None:
                    on[i] = -2
                    cast_on[i] = c.id
        elif r == WIZARD:
            c = burn_clump(i, live, role, px, py)
            if c is not None:
                on[i] = -2
                cast_on[i] = c.id
        elif r == THEURG:
            cast_on[i] = summon_tree(i, live, role, px, py)
        else:
            on[i] = shot_tree(i, live, role, px, py, dps * 2, shoot_range(r))


def round2_tree(i, live, role, px, py):
    """1.2.1 round 2's peel: a tree on an area caster, then a healer, then any other caster or archer within 1,200
    (spread by raider), else the most hurt on me, else him."""
    def rank(v):
        return 0 if role[v] == WIZARD else 1 if role[v] == HEALER else 2 if soft(role[v]) else 3
    best = 99
    for t in live:
        v = t.target
        if v < 0 or v == i or v >= len(px) or dist(px[i], py[i], t.x, t.y) > 1200:
            continue
        best = min(best, rank(v))
    if best <= 2:
        cands = [t for t in live if t.target >= 0 and t.target != i and dist(px[i], py[i], t.x, t.y) <= 1200 and rank(t.target) == best]
        return cands[i % len(cands)].id
    pick = None
    for t in live:
        if t.target == i and (pick is None or t.hp < pick.hp):
            pick = t
    return pick.id if pick is not None else -1


def tagburn_tree(i, is_guard, live, role, px, py, area_left):
    """EpicEncounterTactics.GreenKnightTagTree (unchanged)."""
    for t in live:
        mine = t.threat.get(i, 0.0)
        if t.target == i and 0 < mine < TAG and dist(px[i], py[i], t.x, t.y) <= TAG_REACH:
            return t.id
    sft = [t for t in live if t.target >= 0 and t.target != i and soft(role[t.target]) and
           dist(px[i], py[i], t.x, t.y) <= PEEL_REACH]
    if sft:
        return sft[i % len(sft)].id
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


def burn_clump(i, live, role, px, py):
    """EpicEncounterTactics.GreenKnightBurnTree (unchanged)."""
    ok = []
    for t in live:
        d = dist(px[i], py[i], t.x, t.y)
        h = holder(t, role)
        if d > SPELL_RANGE or h < TAG or eff(t.threat.get(i, 0.0) + AREA_HIT, d) >= h:
            continue
        ok.append(t)
    return clump(ok, px[i], py[i], 2)


def shot_tree(i, live, role, px, py, shot, reach):
    """EpicEncounterTactics.GreenKnightShotTree: a caster or archer without an area spell shoots the most hurt tree in
    its reach that a fighter holds and that its next shots (shot threat more) would not pull; else him."""
    pick = None
    for t in live:
        d = dist(px[i], py[i], t.x, t.y)
        h = holder(t, role)
        if d > reach or h < TAG or eff(t.threat.get(i, 0.0) + shot, d) >= h:
            continue
        if pick is None or pct(t) < pct(pick):
            pick = t
    return pick.id if pick is not None else -1


def summon_tree(i, live, role, px, py):
    """EpicEncounterTactics.GreenKnightSummonTree: a Theurgist's elemental goes for the nearest tree hitting a healer,
    caster, archer or Theurgist within its summon range (it takes the tree's threat), else one hitting itself; else
    him (-1)."""
    best, bd = None, 0.0
    for t in live:
        v = t.target
        if v < 0 or not soft(role[v]):
            continue
        d = dist(px[i], py[i], t.x, t.y)
        if d > SUMMON_RANGE:
            continue
        if best is None or d < bd:
            best, bd = t, d
    return best.id if best is not None else -1


if __name__ == "__main__":
    import time
    t0 = time.time()
    rule = int(sys.argv[1]) if len(sys.argv) > 1 else TAGBURN
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    case = sys.argv[3] if len(sys.argv) > 3 else "low"
    st = {}
    for s in range(4):
        print(fight(s, rule, n, 80.0, case, stats=st))
    for k, v in st.items():
        if k in ("contributed", "raiders", "walks"):
            print(k, v)
            continue
        print(f"{k:10s} on him {v[0]/4:8.0f}  on trees {v[1]/4:8.0f}  trees killed {v[2]/4:5.1f}  healed {v[3]/4:8.0f}")
    print("t", round(time.time() - t0, 2))
