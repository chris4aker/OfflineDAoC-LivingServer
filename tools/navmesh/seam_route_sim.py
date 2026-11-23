#!/usr/bin/env python3
"""Zone-seam trip simulator: replays the autonomous gamebots' zone itinerary (AutonomousZoneItinerary.TryNextStep) over
one region with the installed navmesh/seams.json, navmesh/roads.json and zone meshes, step by step, and reports each
border crossing it picks and whether the trip turns back on itself (AUTONOMOUS_ZONE_SEAM_CYCLE: three reversals).

It mirrors the C# planner's order: the seam-area route, then the plain zone chains over listed borders, then a deferred
area loop, then the same over every border; per border the listed crossings grouped by near-side ground (best two per
group), the "reaches the destination's main ground" preference, the pair-area fallback, then the direct crossing and the
33 border samples. Corridor proofs are replaced by mesh reachability (dfprobe batch: a flood over polygon links with the
server's filter), which is what a complete corridor or a road-graph route proves. Height snapping takes the floor under
the point nearest the asked height. Estimates, not the server: use it to find and explain a loop, then pin the rule in
a unit test.

  python3 tools/navmesh/seam_route_sim.py --nav DIR --zones Zones.xml --seams seams.json --roads roads.json \
      --region 200 --start x,y,z --goal x,y,z [--rule old|new]

--rule old: the 1.2.1 pick rule (the main-ground preference looks only at each group's best crossing).
--rule new: the 1.2.2 rule (#421): every crossing, listed or sampled, whose far-side ground cannot reach the
destination's main ground along the chain is left out whenever a listed crossing of the border can.
"""
import argparse, json, math, os, re, subprocess, sys
from collections import deque

ap = argparse.ArgumentParser()
ap.add_argument('--nav', required=True); ap.add_argument('--probe', default=None)
ap.add_argument('--zones', required=True); ap.add_argument('--seams', required=True); ap.add_argument('--roads', required=True)
ap.add_argument('--region', type=int, default=200)
ap.add_argument('--start', required=True); ap.add_argument('--goal', required=True)
ap.add_argument('--rule', default='new', choices=['old', 'new']); ap.add_argument('--trace', action='store_true')
ap.add_argument('--hops', type=int, default=16)
A = ap.parse_args()
P = lambda s: tuple(float(v) for v in s.split(','))
REGION = A.region

# ---- zones ---------------------------------------------------------------------------------------------------------
text = open(A.zones, encoding='utf-8-sig').read()
ZONES = []
for m in re.finditer(r'<Zones>(.*?)</Zones>', text, re.S):
    b = m.group(1); g = lambda k: re.search(rf'<{k}>(.*?)</{k}>', b).group(1)
    if int(g('RegionID')) == REGION:
        ZONES.append(dict(id=int(g('ZoneID')), x=int(g('OffsetX')) * 8192, y=int(g('OffsetY')) * 8192,
                          w=int(g('Width')) * 8192, h=int(g('Height')) * 8192))
ZONES.sort(key=lambda z: z['id']); BY_ID = {z['id']: z for z in ZONES}
def contains(z, p): return z['x'] <= p[0] < z['x'] + z['w'] and z['y'] <= p[1] < z['y'] + z['h']
def zone_at(p): return next((z for z in ZONES if contains(z, p)), None)

def shared_edge(a, b):
    if a is b: return None
    lo, hi = max(a['y'], b['y']), min(a['y'] + a['h'], b['y'] + b['h'])
    if hi - lo >= 256:
        if a['x'] + a['w'] == b['x']: return dict(f=a, t=b, vert=True, border=b['x'], lo=lo, hi=hi, dir=1)
        if b['x'] + b['w'] == a['x']: return dict(f=a, t=b, vert=True, border=a['x'], lo=lo, hi=hi, dir=-1)
    lo, hi = max(a['x'], b['x']), min(a['x'] + a['w'], b['x'] + b['w'])
    if hi - lo >= 256:
        if a['y'] + a['h'] == b['y']: return dict(f=a, t=b, vert=False, border=b['y'], lo=lo, hi=hi, dir=1)
        if b['y'] + b['h'] == a['y']: return dict(f=a, t=b, vert=False, border=a['y'], lo=lo, hi=hi, dir=-1)
    return None

def at(e, along, zin, zout=None):
    along = min(max(along, e['lo'] + 96), e['hi'] - 96); zout = zin if zout is None else zout; b, d = e['border'], e['dir']
    return ((b - d * 64, along, zin), (b + d * 64, along, zout)) if e['vert'] else ((along, b - d * 64, zin), (along, b + d * 64, zout))

# ---- seams / roads ---------------------------------------------------------------------------------------------------
S = json.load(open(A.seams)); MAIN = {int(k): v for k, v in S['main'].get(str(REGION), {}).items()}
SEAMS = {(e['from'], e['to']): e['points'] for e in S['regions'].get(str(REGION), [])}
GRAPH = {}
for (f, t), pts in SEAMS.items():
    for p in pts:
        if p[3] >= 0 and p[4] >= 0: GRAPH.setdefault((f, p[3]), {})[(t, p[4])] = None
R = json.load(open(A.roads))['zones']
def area_at(zid, p):
    zr = R.get(str(zid)); cell = 2048
    if not zr: return None
    cx, cy = math.floor(p[0] / cell), math.floor(p[1] / cell); best = None
    for a in zr['anchors']:
        if abs(math.floor(a[0] / cell) - cx) <= 1 and abs(math.floor(a[1] / cell) - cy) <= 1:
            s = (a[0] - p[0]) ** 2 + (a[1] - p[1]) ** 2 + ((a[2] - p[2]) * 2) ** 2
            if best is None or s < best[0]: best = (s, a[3])
    return best and best[1]
EDGES = {z['id']: [e for o in ZONES if (e := shared_edge(z, o))] for z in ZONES}
SEAM_EDGES = {k: [e for e in v if (e['f']['id'], e['t']['id']) in SEAMS] for k, v in EDGES.items()}

# ---- mesh (dfprobe batch, one process per zone) --------------------------------------------------------------------
probe = A.probe or os.path.join(A.nav, 'dfprobe'); PROCS = {}
def nav(zid):
    if zid not in PROCS:
        f = os.path.join(A.nav, f'zone{zid:03d}.nav')
        PROCS[zid] = subprocess.Popen([probe, f, 'batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                      text=True, bufsize=1) if os.path.exists(f) else None
    return PROCS[zid]
def ask(zid, line):
    p = nav(zid); p.stdin.write(line + '\n'); p.stdin.flush(); return p.stdout.readline().split()
FLOOD = {}
def flood(zid, p):
    key = (zid, round(p[0]), round(p[1]), round(p[2]))
    if FLOOD.get(zid) != key: ask(zid, 'from %f %f %f' % p); FLOOD[zid] = key
def reaches(zid, a, b):
    if nav(zid) is None: return True   # no mesh here: unknown passes
    flood(zid, a); return ask(zid, 'at %f %f %f' % b)[2] == '1'
def snap(zid, p, reach=4096):
    if nav(zid) is None: return p
    r = ask(zid, 'floors %f %f %f %f' % (p[0], p[1], p[2] - reach, p[2] + reach))[1:]
    fl = [float(r[i]) for i in range(0, len(r), 2)]
    return (p[0], p[1], min(fl, key=lambda z: abs(z - p[2]))) if fl else None
def resolve(e, step):
    i, o = snap(e['f']['id'], step[0]), snap(e['t']['id'], step[1])
    return (i, o) if i and o and contains(e['f'], i) and contains(e['t'], o) else None

# ---- planner ---------------------------------------------------------------------------------------------------------
def seam_ground_at(zid, p):
    best, best_along = None, 1e18
    for e in EDGES[zid]:
        across, along = (p[0], p[1]) if e['vert'] else (p[1], p[0])
        if abs(across - e['border']) > 96 or along < e['lo'] or along > e['hi']: continue
        for c in SEAMS.get((zid, e['t']['id']), []):
            off = abs(c[0] - along)
            if c[3] < 0 or off > 96 or off >= best_along or abs(c[1] - p[2]) > 160: continue
            best, best_along = c[3], off
    return best

def area_route(f, t, start, goal):
    sa = seam_ground_at(f, start); sa = area_at(f, start) if sa is None else sa
    ga = area_at(t, goal); ga = MAIN.get(t) if ga is None else ga
    if sa is None or ga is None: return []
    o, tg = (f, sa), (t, ga); prev = {o: o}; q = deque([o])
    while q:
        c = q.popleft()
        if c == tg:
            out = [c[0]]
            while c != o: c = prev[c]; out.append(c[0])
            return out[::-1]
        for n in GRAPH.get(c, {}):
            if n not in prev and n[0] in BY_ID: prev[n] = c; q.append(n)
    return []

def zone_route(f, t, excluded, seams_only):
    g = SEAM_EDGES if seams_only else EDGES; prev = {f: None}; q = deque([f])
    while q:
        c = q.popleft()
        if c == t:
            out = [];
            while c is not None: out.append(c); c = prev[c]
            return out[::-1]
        for e in g[c]:
            k = (e['f']['id'], e['t']['id'])
            if k in excluded or e['t']['id'] in prev: continue
            prev[e['t']['id']] = c; q.append(e['t']['id'])
    return []

def chain_continues(route, out):
    if out < 0: return True
    areas = {out}
    for i in range(1, len(route) - 1):
        nxt = SEAMS.get((route[i], route[i + 1]))
        if nxt is None or any(c[3] < 0 for c in nxt): return True
        areas = {c[4] for c in nxt if c[3] in areas}
        if not areas: return False
    return True

def chain_main(route, out):
    if out < 0 or len(route) < 2: return None
    main = MAIN.get(route[-1])
    if main is None: return None
    areas = {out}
    for i in range(1, len(route) - 1):
        nxt = SEAMS.get((route[i], route[i + 1]))
        if nxt is None or any(c[3] < 0 for c in nxt): return None
        areas = {c[4] for c in nxt if c[3] in areas}
        if not areas: return False
    return main in areas

STEPS = {}
def seam_steps(e):
    k = (e['f']['id'], e['t']['id'])
    if k not in STEPS:
        out = []
        for c in SEAMS.get(k, [])[::2]:
            r = resolve(e, at(e, c[0], c[1], c[2]))
            if r: out.append((r, c[3], c[4]))
        STEPS[k] = out
    return STEPS[k]

def d2(a, b): return sum((x - y) ** 2 for x, y in zip(a, b))
def trace(*a):
    if A.trace: print('     ', *a)

def edge_step(e, route, start, goal):
    f, t = e['f']['id'], e['t']['id']; steps = seam_steps(e); cands, fallback = [], []; any_main = False
    if (f, t) in SEAMS:
        groups = {}
        for s in steps: groups.setdefault(s[1], []).append(s)
        cont = {}; main = {}
        C = lambda o: cont.setdefault(o, chain_continues(route, o))
        M = lambda o: main.setdefault(o, chain_main(route, o))
        ga = area_at(route[-1], goal)
        any_main = A.rule == 'new' and not contains(e['t'], goal) and (ga is None or ga == MAIN.get(route[-1])) and any(C(s[2]) and M(s[2]) is True for s in steps)
        picks = []
        for g in groups.values():
            ok = [s for s in g if C(s[2]) and not (any_main and M(s[2]) is False)]
            ok.sort(key=lambda s: d2(start, s[0][0]) + d2(s[0][1], goal))
            if ok: picks.append((d2(start, ok[0][0][0]) + d2(ok[0][0][1], goal), ok[0], ok[1] if len(ok) > 1 else None))
        if any(M(p[1][2]) is True for p in picks): picks = [p for p in picks if M(p[1][2]) is not False]
        picks.sort(key=lambda p: p[0])
        for p in picks:
            if len(cands) >= 24: break
            cands.append(p[1][0])
            if p[2] and len(cands) < 24: cands.append(p[2][0])
        pairs = {}
        for s in steps: pairs.setdefault((s[1], s[2]), []).append(s)
        extra = []
        for g in pairs.values():
            if not C(g[0][2]) or M(g[0][2]) is False: continue
            g = sorted(g, key=lambda s: d2(start, s[0][0]) + d2(s[0][1], goal))
            extra += [(d2(start, s[0][0]) + d2(s[0][1], goal), s[0]) for s in g[:2] if s[0] not in cands]
        fallback = [s for _, s in sorted(extra, key=lambda x: x[0])[:24]]
    preferred = len(cands); unlisted = []
    delta = goal[0] - start[0] if e['vert'] else goal[1] - start[1]
    tt = min(max((e['border'] - (start[0] if e['vert'] else start[1])) / delta, 0), 1) if abs(delta) > 0.001 else 0
    coord = start[1] + (goal[1] - start[1]) * tt if e['vert'] else start[0] + (goal[0] - start[0]) * tt
    r = resolve(e, at(e, coord, start[2]))
    if r: unlisted.append(r)
    band = min(max(int(start[2]) // 1024, 0), 31)
    for i in range(33):
        r = resolve(e, at(e, e['lo'] + 96 + (e['hi'] - e['lo'] - 192) * i / 32, band * 1024 + 512))
        if r: unlisted.append(r)
    unlisted.sort(key=lambda c: d2(start, c[0]) + d2(c[1], goal))
    goal_in = contains(e['t'], goal)
    for c, listed in [(c, True) for c in cands] + [(c, False) for c in unlisted] + [(c, True) for c in fallback]:
        area = None
        if not listed and (f, t) in SEAMS:
            along = c[0][1] if e['vert'] else c[0][0]
            m = [p for p in SEAMS[(f, t)] if abs(p[0] - along) <= 96 and abs(p[1] - c[0][2]) <= 160]
            area = m[0][4] if m else 'none'
            if area == 'none' or (not goal_in and not chain_continues(route, area)): continue
            if any_main and not goal_in and chain_main(route, area) is False:
                trace(f'{f}>{t}', 'sample', fmt(c[1]), 'far ground', area, 'never reaches the main ground'); continue
        if not reaches(f, start, c[0]): trace(f'{f}>{t}', 'seam' if listed else 'sample', fmt(c[0]), 'not reached from start'); continue
        if goal_in and not reaches(t, c[1], goal): trace(f'{f}>{t}', 'seam' if listed else 'sample', fmt(c[1]), 'no way on to goal'); continue
        trace(f'{f}>{t}', 'seam' if listed else 'sample', 'chosen', fmt(c[0]), '>', fmt(c[1]), 'far ground', area if area is not None else 'listed')
        return c
    return None

def try_routes(f, t, start, goal, seams_only, failed):
    deferred = None
    if seams_only:
        g = area_route(f, t, start, goal); trace('area route', g)
        if 2 <= len(g) <= 32 and len(set(g)) < len(g): deferred = g
        elif 2 <= len(g) <= 32 and (e := shared_edge(BY_ID[f], BY_ID[g[1]])):
            s = edge_step(e, g, start, goal)
            if s: return s, g
            failed.add(tuple(g))
    excluded = set()
    for _ in range(min(8, len(EDGES[f]))):
        route = zone_route(f, t, excluded, seams_only); trace('zone route', 'seams' if seams_only else 'all', route)
        if len(route) < 2 or len(route) > 32: break
        e = shared_edge(BY_ID[f], BY_ID[route[1]])
        if tuple(route) not in failed and nav(route[1]) is not None:
            s = edge_step(e, route, start, goal)
            if s: return s, route
        excluded.add((f, route[1])); failed.add(tuple(route))
    if deferred and (e := shared_edge(BY_ID[f], BY_ID[deferred[1]])):
        trace('deferred loop', deferred); s = edge_step(e, deferred, start, goal)
        if s: return s, deferred
    return None, None

def next_step(f, t, start, goal):
    start = snap(f, start, 256) or start; failed = set()
    s = try_routes(f, t, start, goal, True, failed)
    return s if s[0] else try_routes(f, t, start, goal, False, failed)

def reversal(a, b):
    v1 = (a[1][0] - a[0][0], a[1][1] - a[0][1]); v2 = (b[1][0] - b[0][0], b[1][1] - b[0][1])
    l1, l2 = math.hypot(*v1), math.hypot(*v2)
    if l1 < 1 or l2 < 1 or (v1[0] * v2[0] + v1[1] * v2[1]) / (l1 * l2) > -0.7: return False
    m1 = ((a[0][0] + a[1][0]) / 2, (a[0][1] + a[1][1]) / 2); m2 = ((b[0][0] + b[1][0]) / 2, (b[0][1] + b[1][1]) / 2)
    return math.dist(m1, m2) <= 300

def fmt(p): return '%d,%d,%d' % tuple(round(v) for v in p)

start, goal = P(A.start), P(A.goal); gz = zone_at(goal)['id']; last = None; reversals = 0
print(f'trip {fmt(start)} (zone {zone_at(start)["id"]}) -> {fmt(goal)} (zone {gz}), rule {A.rule}')
for hop in range(A.hops):
    z = zone_at(start)['id']
    if z == gz:
        ok = reaches(z, start, goal); print(f'  in the goal zone {z}: {"reaches the goal" if ok else "DOES NOT reach the goal"}'); break
    step, route = next_step(z, gz, start, goal)
    if not step: print(f'  zone {z}: no step (the bot would try capital transit, a pocket escape or give the camp up)'); break
    rev = last is not None and reversal(last, step); reversals = reversals + 1 if rev else 0
    print(f'  hop {hop}: zone {z} route {">".join(map(str, route))}: {fmt(step[0])} > {fmt(step[1])}{"  REVERSAL" if rev else ""}')
    if reversals >= 3: print(f'  AUTONOMOUS_ZONE_SEAM_CYCLE at {fmt(step[0])}'); break
    last = step; start = step[1]
else: print('  hop limit')
