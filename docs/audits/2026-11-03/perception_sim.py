"""Monte Carlo of PR #53 RvR perception (AutonomousRvrPerception.cs, normal profile) vs alternatives.

Faithful to the code on wt-perception (commit 204d8216):
- each eligible bot scans every 1000 + (id % 250) ms (AutonomousFrontierThreatPolicy.Due), random phase
- per scan chance = Far + (Near-Far)*(1-t)^2, t=(d-1000)/(3500-1000); 1.0 within 1000; 0 beyond 3500
- x2 combat, x1.5 casting, x1.5 group>=3, x1.75 facing (120 deg cone), x0.5 still & out of combat; cap 0.95
- every scan re-rolls until noticed (Memory.Seen returns Unnoticed) -> per-bot hazard, not one roll
- spotter: on notice, group/warband mates within 2500 are alerted (Alert), engage after their own 0.5-2 s delay
- LOS checked before the roll; only the 8 nearest candidates are LOS-checked (VisibilityBudget = 8)
Not modelled: think-interval gating (bots in combat/aggro/passive/safe areas don't scan), stealth (stealthed = invisible).
"""
import random, math, sys

NEAR, FAR, ALWAYS, OUTER, CAP = 0.50, 0.03, 1000, 3500, 0.95

def chance(d, facing, combat=False, casting=False, group=1, still=False):
    if d <= ALWAYS: return 1.0
    if d > OUTER: return 0.0
    t = (d - ALWAYS) / (OUTER - ALWAYS)
    c = FAR + (NEAR - FAR) * (1 - t) ** 2
    if combat: c *= 2.0
    if casting: c *= 1.5
    if group >= 3: c *= 1.5
    if facing: c *= 1.75
    if still and not combat: c *= 0.5
    return min(CAP, c)

def reaction_ms(rng, level=50, bold=0.5):
    speed = 0.45 * (level - 1) / 49 + 0.35 * bold + 0.20 * rng.random()
    return 2000 - 1500 * speed

def sample_distance(rng, mode):
    if mode == "edge": return 3400.0
    if mode == "mid": return 2500.0
    # uniform over the annulus 1000..3500 by area
    r2 = rng.uniform(ALWAYS ** 2, OUTER ** 2)
    return math.sqrt(r2)

def run(model, n, dist_mode, horizon_s=30, trials=4000, group_size=8, los=1.0, crowd_blocks=False,
        approach=False, observers_k=3, relay_ms=4000, relay_p=0.5, seed=1, player_group=1):
    """Returns P(first notice by T) and mean fraction of bots aware (engaging) by T for T in (1,5,10,30)."""
    rng = random.Random(seed)
    Ts = (1, 5, 10, 30)
    first = {T: 0 for T in Ts}
    aware = {T: 0.0 for T in Ts}
    H = horizon_s * 1000
    for _ in range(trials):
        bots = []
        for i in range(n):
            period = 1000 + rng.randrange(250)
            phase = rng.uniform(0, period)
            d = sample_distance(rng, dist_mode)
            has_los = rng.random() < los
            bots.append([period, phase, d, has_los])
        # engage time per bot (ms), inf if never
        engage = [math.inf] * n
        notice_first = math.inf
        groups = [list(range(g, min(n, g + group_size))) for g in range(0, n, group_size)]
        gid = {b: gi for gi, g in enumerate(groups) for b in g}
        if model == "scout":
            rollers = {g[0] for g in groups}          # one designated scout per group
        elif model == "budget":
            # only the k best-placed bots per target roll (closest first); everyone else via relay
            order = sorted(range(n), key=lambda b: bots[b][2])
            rollers = set(order[:observers_k])
        else:
            rollers = set(range(n))
        # event loop over scans
        events = []
        for b in rollers:
            period, phase, d, has_los = bots[b]
            t = phase
            while t <= H:
                events.append((t, b)); t += period
        events.sort()
        noticed = [False] * n
        for t, b in events:
            if noticed[b]: continue
            period, phase, d0, has_los = bots[b]
            if not has_los or crowd_blocks: continue
            d = d0
            if approach:  # player runs straight in from 3500 toward the bots' area at 191 u/s
                d = max(0.0, d0 - 191.0 * t / 1000.0)
            facing = rng.random() < 120 / 360
            if rng.random() < chance(d, facing, group=player_group):
                noticed[b] = True
                e = t + reaction_ms(rng)
                engage[b] = min(engage[b], e)
                notice_first = min(notice_first, t)
                if model in ("pr53", "scout"):
                    for m in groups[gid[b]]:  # spotter call: group mates within 2500 (assume all are)
                        if m != b and not noticed[m]:
                            noticed[m] = True
                            engage[m] = min(engage[m], t + reaction_ms(rng))
                if model in ("scout", "budget"):
                    # imperfect relay to other groups / the realm, with delay
                    for m in range(n):
                        if not noticed[m] and rng.random() < relay_p:
                            noticed[m] = True
                            engage[m] = min(engage[m], t + relay_ms + reaction_ms(rng))
        for T in Ts:
            if notice_first <= T * 1000: first[T] += 1
            aware[T] += sum(1 for e in engage if e <= T * 1000) / max(1, n)
    return {T: (first[T] / trials, aware[T] / trials) for T in Ts}

def table(title, model, dist_mode, **kw):
    print(f"\n### {title}")
    print("| N | P(first notice) 1s / 5s / 10s / 30s | share of bots engaging 1s / 5s / 10s / 30s |")
    print("|---|---|---|")
    for n in (1, 10, 20, 50, 100):
        r = run(model, n, dist_mode, **kw)
        p = " / ".join(f"{r[T][0]*100:.0f}%" for T in (1, 5, 10, 30))
        a = " / ".join(f"{r[T][1]*100:.0f}%" for T in (1, 5, 10, 30))
        print(f"| {n} | {p} | {a} |")

if __name__ == "__main__":
    # sanity: audit formula, one scan, edge, no modifiers
    print("audit single-scan 1-(0.97)^N:", {n: round(1 - 0.97 ** n, 3) for n in (1, 10, 20, 50, 100)})
    print("code chance at 3400, not facing:", round(chance(3400, False), 4), " facing:", round(chance(3400, True), 4))
    print("code chance at 2500, not facing:", round(chance(2500, False), 4), " facing:", round(chance(2500, True), 4))
    tr = int(sys.argv[1]) if len(sys.argv) > 1 else 3000
    table("PR #53 as coded, solo player at 3,400 (edge), open LOS, bots ungrouped", "pr53", "edge", group_size=1, trials=tr)
    table("PR #53 as coded, solo player at 3,400 (edge), open LOS, bots in groups of 8 (spotters on)", "pr53", "edge", group_size=8, trials=tr)
    table("PR #53 as coded, bots spread 1,000-3,500 (uniform by area), groups of 8", "pr53", "spread", group_size=8, trials=tr)
    table("PR #53 as coded, spread, groups of 8, 50% of bots without LOS (broken terrain)", "pr53", "spread", group_size=8, los=0.5, trials=tr)
    table("PR #53 as coded, player running in from 3,500 at 191 u/s, groups of 8", "pr53", "edge", group_size=8, approach=True, trials=tr)
    table("PR #53, player in a group of 3+ (x1.5), spread, groups of 8", "pr53", "spread", group_size=8, player_group=3, trials=tr)
    table("Scout model: one roller per group of 8, relay 50% / 4 s to other groups, spread", "scout", "spread", group_size=8, trials=tr)
    table("Observer budget: 3 best-placed rollers per target, relay 50% / 4 s, spread", "budget", "spread", observers_k=3, trials=tr)
