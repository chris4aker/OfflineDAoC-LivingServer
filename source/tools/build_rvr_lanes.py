#!/usr/bin/env python3
"""Builds the RvR lane catalog (source/server/GameServer/bots/autonomous/data/rvr-lanes.json).

Every lane is walked on the installed frontier meshes by the server's own planner and Detour, through the
[Explicit] NUnit tool UT_RvrLaneSurvey (zone steps from AutonomousZoneItinerary.ProbeNextStep, Detour inside a
zone, the zone road route for a partial path). Nothing here is guessed: a lane ships only when both directions
complete and pass the filters below. The result is tests only until it has been seen in game.

Shape (docs/RVR-FIELD-1.2.md, section 4): a trunk from each enemy portal keep to its milegate (lane 0 only), then
a fan of lanes from the gate to every keep; lanes from each realm's border keeps ("entries") into its own
frontier; and keep-to-keep hops for every keep pair within --hop-radius.

Lane 0 is the planner's own route (no via points). Alternates (lane 1, 2) must, in both directions:
  - complete;                                    - be at most 35% longer than lane 0 in the same direction;
  - stay 2,500 units from lane 0 over the middle half of both;
  - pass no keep within 4,000 units other than the lane's own start and goal keeps (portal keeps included);
  - swim at most 1,000 units;
  - pass no level 50+ aggressive monster camp within 1,500 units unless lane 0 passes the same camp.
The second alternate must also stay 2,500 units from the first.

Inputs: a world database with the static tables Keep, Zones and Mob (read only; only keep and zone positions reach
the output, monsters are used as a filter and never written), the navmesh folder (zoneNNN.nav, roads.json,
seams.json, pockets.json, lib/Detour.so or Detour.dll beside it) and a built Tests.dll.

  python3 source/tools/build_rvr_lanes.py --db runtime/data/opendaoc.sqlite3.db --navroot <folder with navmesh/>
      --tests-dll source/server/build/Tests/Debug/lib/Tests.dll --work <scratch dir> --out <rvr-lanes.json>

Needs numpy and scipy. Each region takes a few minutes; dotnet runs take --lock (flock) when given.
"""
import argparse
import collections
import hashlib
import heapq
import itertools
import json
import math
import os
import pickle
import sqlite3
import subprocess
import sys
import time

import numpy as np
from scipy.spatial import cKDTree

FRONTIER = {1: [11, 12, 14, 15], 100: [111, 112, 113, 115], 200: [210, 211, 212, 214]}
# Zones outside the frontier that hold a realm's border keep.
ENTRY_ZONES = {1: [], 100: [102], 200: [206, 207]}
HOME = {1: 1, 100: 2, 200: 3}
REALM = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}
# AutonomousFrontierTransport.Destination: where each invading realm's teleporter lands (its portal keep).
ARRIVALS = {1: {3: (605743, 293676, 4839), 2: (655200, 293217, 4879)},
            100: {3: (596055, 581400, 6031), 1: (596364, 631509, 5971)},
            200: {1: (475835, 343661, 4080), 2: (474107, 295199, 3871)}}
# The only way out of each portal keep's valley (survey 2026-11-16: every walk from the portal keep crossed the wall
# inside one window about 200 units wide). The point is 600 units past the gate on the walked route.
GATES = {1: [("Hadrian's Wall west milegate", (610611, 320102, 3876), 3),
             ("Hadrian's Wall east milegate", (651796, 321126, 4314), 2)],
         100: [("Odin's Gate north milegate", (624230, 584192, 5466), 3),
               ("Odin's Gate south milegate", (622514, 625971, 5032), 1)],
         200: [("Emain Macha north milegate", (448205, 299008, 4259), 2),
               ("Emain Macha south milegate", (448205, 340200, 3459), 1)]}
# AutonomousRvrStaging.BorderKeeps (the arrival point of the home necklace where there is one).
ENTRIES = {1: [('Castle Sauvage', (584285, 477200, 2600)), ('Snowdonia Fortress', (528451, 358290, 8320))],
           100: [('Svasud Faste', (766811, 669605, 5736)), ('Vindsaul Faste', (704022, 738009, 5704))],
           200: [('Druim Ligen', (334386, 420071, 5184)), ('Druim Cain', (421166, 484998, 1976))]}

SEP = 2500
MAX_RATIO = 1.35
KEEP_CLEAR = 4000
MAX_SWIM = 1000
CAMP_RADIUS = 1500
CAMP_LINK = 1000
HAZARD_NODE = 4600


def log(*a):
    print(time.strftime('%H:%M:%S'), *a, flush=True)


def P(p):
    return '%d,%d,%d' % (round(p[0]), round(p[1]), round(p[2]))


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


class World:
    def __init__(self, db, region):
        self.region = region
        con = sqlite3.connect('file:%s?mode=ro' % db, uri=True)
        self.zone_ids = FRONTIER[region] + ENTRY_ZONES[region]
        self.zones = {}
        for zid, ox, oy, w, h in con.execute('select ZoneID,OffsetX,OffsetY,Width,Height from Zones where RegionID=?', (region,)):
            if zid in self.zone_ids:
                self.zones[zid] = (ox * 8192, oy * 8192, w * 8192, h * 8192)
        missing = set(self.zone_ids) - set(self.zones)
        if missing:
            raise SystemExit('zones missing from the Zones table: %s' % sorted(missing))
        self.keeps = [dict(id=k, name=n, x=x, y=y, z=z, base=b) for k, n, x, y, z, b in con.execute(
            'select KeepID,Name,X,Y,Z,BaseLevel from Keep where Region=? order by KeepID', (region,))]
        for k in self.keeps:
            k['portal'] = 'Portal Keep' in k['name']
            k['relic'] = not k['portal'] and (k['base'] or 0) >= 60
        spawns = [(x, y) for x, y in con.execute(
            'select X,Y from Mob where Region=? and Level>=50 and Realm=0 and AggroLevel>0', (region,)) if self.zone_at(x, y)]
        con.close()
        self.spawns = np.array(spawns, dtype=float).reshape(-1, 2)
        # Camps: spawns joined by links of at most CAMP_LINK units.
        parent = list(range(len(spawns)))

        def find(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i
        if len(spawns):
            for a, b in cKDTree(self.spawns).query_pairs(CAMP_LINK):
                parent[find(a)] = find(b)
        self.camp = np.array([find(i) for i in range(len(spawns))])
        self.spawn_tree = cKDTree(self.spawns) if len(spawns) else None

    def zone_at(self, x, y):
        for zid, (ox, oy, w, h) in self.zones.items():
            if ox <= x < ox + w and oy <= y < oy + h:
                return zid
        return None

    def zonespec(self):
        return ';'.join('%d:%d:%d:%d:%d' % ((z,) + self.zones[z]) for z in self.zone_ids)


class Graph:
    """The region's zone road graphs (roads.json, main ground only) joined at the seams (seams.json). Used only to
    propose via points; every lane is proven by a walk."""

    def __init__(self, navroot, world):
        roads = json.load(open(os.path.join(navroot, 'navmesh', 'roads.json')))
        seams = json.load(open(os.path.join(navroot, 'navmesh', 'seams.json')))
        r = str(world.region)
        self.main = {int(k): v for k, v in seams['main'][r].items()}
        self.nodes, self.zone_of, index = [], [], {}
        self.adj = collections.defaultdict(list)
        for z in world.zone_ids:
            zz = roads['zones'][str(z)]
            for i, a in enumerate(zz['anchors']):
                if a[3] == self.main[z]:
                    index[(z, i)] = len(self.nodes)
                    self.nodes.append((a[0], a[1], a[2]))
                    self.zone_of.append(z)
            for a, b, c in zz['edges']:
                if (z, a) in index and (z, b) in index:
                    self.adj[index[(z, a)]].append((index[(z, b)], c))
        self.xy = np.array([n[:2] for n in self.nodes], dtype=float)
        self.tree = cKDTree(self.xy)
        for e in seams['regions'][r]:
            f, t = e['from'], e['to']
            if f not in world.zone_ids or t not in world.zone_ids or f > t:
                continue
            last = None
            for c, zf, zt, af, at in e['points']:
                if af != self.main[f] or at != self.main[t] or (last is not None and abs(c - last) < 1024):
                    continue
                last = c
                if e['vertical']:
                    pf, pt = (e['border'] - 200, c), (e['border'] + 200, c)
                else:
                    pf, pt = (c, e['border'] - 200), (c, e['border'] + 200)
                if world.zone_at(*pf) != f:
                    pf, pt = pt, pf
                a, b = self.nearest(pf, zone=f, maxd=3500), self.nearest(pt, zone=t, maxd=3500)
                if a is None or b is None:
                    continue
                d = math.dist(self.nodes[a][:2], self.nodes[b][:2])
                self.adj[a].append((b, d))
                self.adj[b].append((a, d))

    def nearest(self, p, zone=None, maxd=6000, avoid=None):
        found = self.tree.query_ball_point(p[:2], maxd)
        found.sort(key=lambda i: (self.xy[i][0] - p[0]) ** 2 + (self.xy[i][1] - p[1]) ** 2)
        for i in found:
            if (zone is None or self.zone_of[i] == zone) and (avoid is None or i not in avoid):
                return i
        return None

    def near_set(self, pts_xy, d):
        if len(pts_xy) == 0:
            return set()
        hits = cKDTree(pts_xy).query_ball_point(self.xy, d)
        return {i for i, h in enumerate(hits) if h}

    def dijkstra(self, src, dst, banned):
        dist, prev, pq = {src: 0.0}, {}, [(0.0, src)]
        while pq:
            d, u = heapq.heappop(pq)
            if u == dst:
                break
            if d > dist.get(u, 1e18):
                continue
            for v, c in self.adj[u]:
                if v in banned and v != dst:
                    continue
                if d + c < dist.get(v, 1e18):
                    dist[v], prev[v] = d + c, u
                    heapq.heappush(pq, (d + c, v))
        if dst not in dist:
            return None
        path = [dst]
        while path[-1] != src:
            path.append(prev[path[-1]])
        return path[::-1]

    def vias(self, path, n):
        cum = [0.0]
        for a, b in zip(path, path[1:]):
            cum.append(cum[-1] + math.dist(self.nodes[a][:2], self.nodes[b][:2]))
        out = []
        for k in range(1, n + 1):
            j = min(range(len(path)), key=lambda q: abs(cum[q] - cum[-1] * k / (n + 1)))
            if self.nodes[path[j]] not in out:
                out.append(self.nodes[path[j]])
        return out


def densify(pts, step=200):
    """Points every `step` units along a walk (x, y) and the walked distance to each."""
    xy = [np.array(pts[0][:2], dtype=float)[None, :]]
    acc = [np.zeros(1)]
    total = 0.0
    for a, b in zip(pts, pts[1:]):
        d = math.dist(a[:2], b[:2])
        if d <= 0:
            continue
        n = max(1, int(math.ceil(d / step)))
        t = np.arange(1, n + 1) / n
        xy.append(np.array(a[:2], dtype=float) + (np.array(b[:2], dtype=float) - np.array(a[:2], dtype=float)) * t[:, None])
        acc.append(total + d * t)
        total += d
    return np.vstack(xy), np.concatenate(acc)


class Walk:
    """One walked direction: its points, where it passes keeps and camps, and its swim length."""

    def __init__(self, res, world, end_keeps):
        self.ok = res['ok'] == '1'
        self.fail = res.get('fail', '')
        if not self.ok:
            return
        self.len = int(res['len'])
        pts = res['pts']
        self.swim = sum(math.dist(a[:2], b[:2]) for a, b in zip(pts, pts[1:]) if b[3] & 2)
        self.jumps = int(res['jumps'])
        self.xy, self.acc = densify(pts)
        self.tree = cKDTree(self.xy)
        self.keeps = []
        for k in world.keeps:
            if k['id'] in end_keeps:
                continue
            d = self.tree.query((k['x'], k['y']))[0]
            if d < KEEP_CLEAR:
                self.keeps.append(k['id'])
        self.camps = set()
        if world.spawn_tree is not None:
            d = self.tree.query(world.spawns)[0]
            self.camps = set(world.camp[d <= CAMP_RADIUS].tolist())

    def middle(self):
        T = self.acc[-1]
        return (self.acc >= 0.25 * T) & (self.acc <= 0.75 * T)


def separation(a, b):
    """Least distance between the middle half of either walk and the other walk."""
    da = b.tree.query(a.xy[a.middle()])[0]
    db = a.tree.query(b.xy[b.middle()])[0]
    return float(min(da.min(), db.min()))


def max_separation(a, b):
    return float(b.tree.query(a.xy)[0].max())


def judge(alt, main):
    """Why an alternate walk fails the shipping filters against lane 0 in the same direction (None: it passes)."""
    if not alt.ok:
        return 'incomplete'
    if alt.len > MAX_RATIO * main.len:
        return 'too long'
    if separation(alt, main) < SEP:
        return 'too close to lane 0'
    if alt.keeps:
        return 'passes a keep'
    if alt.swim > MAX_SWIM:
        return 'long swim'
    if alt.camps - main.camps:
        return 'new monster camp'
    return None


class Runner:
    def __init__(self, args, world):
        self.args, self.world, self.version = args, world, None

    def run(self, jobs, tag):
        jf = os.path.join(self.args.work, 'jobs-%d-%s.txt' % (self.world.region, tag))
        of = os.path.join(self.args.work, 'out-%d-%s.txt' % (self.world.region, tag))
        with open(jf, 'w') as f:
            f.write('\n'.join(jobs) + '\n')
        if os.path.exists(of):
            os.remove(of)
        env = dict(os.environ, OFFLINE_DAOC_NAV_ROOT=self.args.navroot, OFFLINE_DAOC_REGION=str(self.world.region),
                   OFFLINE_DAOC_ZONES=self.world.zonespec(), LANE_JOBS=jf, LANE_OUT=of)
        cmd = [self.args.dotnet, 'test', self.args.tests_dll, '--filter', 'FullyQualifiedName~UT_RvrLaneSurvey']
        if self.args.lock:
            cmd = ['flock', self.args.lock] + cmd
        started = time.time()
        r = subprocess.run(cmd, env=env, capture_output=True, text=True, cwd=self.args.work)
        if not os.path.exists(of):
            raise RuntimeError(r.stdout[-3000:] + r.stderr[-2000:])
        res = {}
        with open(of) as f:
            for line in f:
                fields = line.rstrip('\n').split(' ')
                if fields[0] == 'version':
                    self.version = fields[1]
                    continue
                kv, pts = {}, []
                for i, tok in enumerate(fields[2:]):
                    if tok.startswith('pts='):
                        pts = [tuple(int(v) for v in p.split(',')) for p in [tok[4:]] + fields[3 + i:] if p]
                        break
                    k, _, v = tok.partition('=')
                    kv[k] = v
                kv['pts'] = pts
                res[fields[1]] = kv
        if self.version is None:
            raise RuntimeError('the survey tool printed no version line; rebuild Tests.dll')
        log('region %d %s: %d jobs in %.0fs' % (self.world.region, tag, len(jobs), time.time() - started))
        return res


def ring(center, toward, radii=(2200, 2800, 1800, 3200)):
    b = math.atan2(toward[1] - center[1], toward[0] - center[0])
    out = []
    for rad in radii:
        for da in (0, 30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180):
            a = b + math.radians(da)
            out.append((center[0] + math.cos(a) * rad, center[1] + math.sin(a) * rad, center[2]))
    return out


def build_region(args, region):
    world = World(args.db, region)
    graph = Graph(args.navroot, world)
    run = Runner(args, world)
    keeps = {k['id']: k for k in world.keeps}
    fighting = [k for k in world.keeps if not k['portal']]
    portal_of = {}
    for realm, arrival in ARRIVALS[region].items():
        portal_of[realm] = min((k for k in world.keeps if k['portal']), key=lambda k: math.dist((k['x'], k['y']), arrival[:2]))

    def keep_ref(k):
        return dict(ref='keep:%d' % k['id'], keep_id=k['id'], name=k['name'])

    # ---- trips ----
    trips = []
    for name, node, realm in GATES[region]:
        pk = portal_of[realm]
        trips.append(dict(kind='trunk', frm=keep_ref(pk), to=dict(ref='milegate:' + name, name=name), ends={pk['id']},
                          S_opts=[ARRIVALS[region][realm]], G_opts=[node], realm=realm))
        for k in fighting:
            trips.append(dict(kind='gate', frm=dict(ref='milegate:' + name, name=name), to=keep_ref(k), ends={k['id']},
                              S_fixed=node, goal_keep=k, realm=realm))
    for name, point in ENTRIES[region]:
        for k in fighting:
            if k['relic']:
                continue
            trips.append(dict(kind='entry', frm=dict(ref='entry:' + name, name=name), to=keep_ref(k), ends={k['id']},
                              S_entry=point, goal_keep=k, realm=HOME[region]))
    pairs_all = 0
    for a, b in itertools.combinations(sorted(fighting, key=lambda k: k['id']), 2):
        if math.dist((a['x'], a['y']), (b['x'], b['y'])) <= args.hop_radius:
            pairs_all += 1
            trips.append(dict(kind='hop', frm=keep_ref(a), to=keep_ref(b), ends={a['id'], b['id']}, start_keep=a, goal_keep=b, realm=0))
    log('region %d: %d trips (%s), %d keep pairs within %d' % (region, len(trips), dict(collections.Counter(t['kind'] for t in trips)), pairs_all, args.hop_radius))

    # ---- endpoints: floor with a local way out, on the zone's main ground ----
    jobs = []
    for i, t in enumerate(trips):
        if t['kind'] == 'trunk':
            continue
        if 'goal_keep' in t:
            k = t['goal_keep']
            other = t.get('S_fixed') or t.get('S_entry') or (t['start_keep']['x'], t['start_keep']['y'])
            for j, c in enumerate(ring((k['x'], k['y'], k['z']), other)):
                jobs.append('snap g%d_%d %s 500' % (i, j, P(c)))
        if 'start_keep' in t:
            k, g = t['start_keep'], t['goal_keep']
            for j, c in enumerate(ring((k['x'], k['y'], k['z']), (g['x'], g['y']))):
                jobs.append('snap s%d_%d %s 500' % (i, j, P(c)))
        if 'S_entry' in t:
            c0 = t['S_entry']
            pts = [c0] + [(c0[0] + math.cos(math.radians(a)) * r, c0[1] + math.sin(math.radians(a)) * r, c0[2])
                          for r in (600, 1200, 1800, 2400) for a in range(0, 360, 30)]
            for j, c in enumerate(pts):
                jobs.append('snap s%d_%d %s 300' % (i, j, P(c)))
        if 'S_fixed' in t:
            jobs.append('snap s%d_0 %s 300' % (i, P(t['S_fixed'])))
    snaps = run.run(jobs, 'snap')

    def picks(prefix, center, lo, hi):
        out = []
        for j in range(64):
            v = snaps.get('%s_%d' % (prefix, j))
            if not v or v['floor'] != '1' or v['exit'] != '1':
                continue
            p = tuple(int(x) for x in v['at'].split(','))
            z = world.zone_at(p[0], p[1])
            if z is None or int(v['area']) != graph.main[z]:
                continue
            if center is None or lo <= math.dist(p[:2], center[:2]) <= hi:
                out.append(p)
        return out
    for i, t in enumerate(trips):
        if t['kind'] == 'trunk':
            continue
        k = t['goal_keep']
        t['G_opts'] = picks('g%d' % i, (k['x'], k['y']), 1500, 3600)[:6]
        if 'start_keep' in t:
            s = t['start_keep']
            t['S_opts'] = picks('s%d' % i, (s['x'], s['y']), 1500, 3600)[:4]
        elif 'S_entry' in t:
            t['S_opts'] = picks('s%d' % i, t['S_entry'], 0, 3600)[:4]
        else:
            t['S_opts'] = picks('s%d' % i, None, 0, 0)[:1]

    # ---- lane 0 both ways: the first endpoint pair whose two walks complete ----
    combos = sorted(itertools.product(range(4), range(6)), key=lambda c: (c[0] + c[1], c[0]))
    for t in trips:
        t['combo'] = 0
        t['main'] = None
    for rnd in range(len(combos)):
        todo = []
        for i, t in enumerate(trips):
            if t['main'] is not None or not t['S_opts'] or not t['G_opts']:
                continue
            while t['combo'] < len(combos):
                si, gi = combos[t['combo']]
                if si < len(t['S_opts']) and gi < len(t['G_opts']):
                    break
                t['combo'] += 1
            if t['combo'] < len(combos):
                todo.append(i)
        if not todo:
            break
        jobs = []
        for i in todo:
            t = trips[i]
            si, gi = combos[t['combo']]
            S, G = t['S_opts'][si], t['G_opts'][gi]
            jobs += ['trip f%d %s>%s' % (i, P(S), P(G)), 'trip b%d %s>%s' % (i, P(G), P(S))]
        res = run.run(jobs, 'main%d' % rnd)
        for i in todo:
            t = trips[i]
            f, b = res['f%d' % i], res['b%d' % i]
            if f['ok'] == '1' and b['ok'] == '1':
                si, gi = combos[t['combo']]
                t['S'], t['G'] = t['S_opts'][si], t['G_opts'][gi]
                t['main'] = (Walk(f, world, t['ends']), Walk(b, world, t['ends']))
                t['main_raw'] = f['pts']
            else:
                t.setdefault('main_fails', []).append((f['ok'], f.get('fail'), b['ok'], b.get('fail')))
                t['combo'] += 1
    ok = [t for t in trips if t['main']]
    log('region %d: lane 0 both ways for %d of %d trips' % (region, len(ok), len(trips)))

    # ---- candidates ----
    def hazard_nodes(t):
        pts = np.array([(k['x'], k['y']) for k in world.keeps if k['id'] not in t['ends']], dtype=float)
        return graph.near_set(pts, HAZARD_NODE)

    def camp_nodes(t):
        main_camps = t['main'][0].camps | t['main'][1].camps
        if world.spawn_tree is None:
            return set()
        mask = np.array([c not in main_camps for c in world.camp])
        return graph.near_set(world.spawns[mask], CAMP_RADIUS + 200)

    def candidates(t, round_two):
        out = []
        fw = t['main'][0]
        banned = hazard_nodes(t) | camp_nodes(t)
        src, dst = graph.nearest(t['S'], maxd=5000), graph.nearest(t['G'], maxd=5000)
        if not round_two:
            T = fw.acc[-1]

            def at(fr):
                return fw.xy[min(len(fw.xy) - 1, int(np.searchsorted(fw.acc, T * fr)))]

            def off(fr, d):
                a, b, c = at(max(0, fr - 0.06)), at(min(1, fr + 0.06)), at(fr)
                dx, dy = b - a
                n = math.hypot(dx, dy) or 1
                x, y = c[0] - dy / n * d, c[1] + dx / n * d
                if world.zone_at(x, y) is None:
                    return None
                return graph.nearest((x, y), maxd=1600, avoid=banned)
            for side in (1, -1):
                for d in (3000, 4000, 5000, 6500, 8000):
                    tri = [off(f, side * d) for f in (1 / 3, 1 / 2, 2 / 3)]
                    if all(v is not None for v in tri):
                        out.append(('o3%+d' % (side * d), [graph.nodes[v] for v in tri]))
                    if tri[1] is not None:
                        out.append(('o1%+d' % (side * d), [graph.nodes[tri[1]]]))
                    if tri[0] is not None and tri[2] is not None:
                        out.append(('o2%+d' % (side * d), [graph.nodes[tri[0]], graph.nodes[tri[2]]]))
        if src is None or dst is None:
            return out
        polys = [fw]
        if round_two:
            polys += [c['walk'][0] for c in t.get('fvalid', [])[:1]]
            configs = ((2800, 0.25, 0.75), (3200, 0.22, 0.78), (4200, 0.2, 0.8), (2800, 0.3, 0.7))
        else:
            configs = ((5000, 0.15, 0.85), (3600, 0.15, 0.85), (3000, 0.22, 0.78), (6500, 0.2, 0.8))
        for cfg, (D, lo, hi) in enumerate(configs):
            ban = set(banned)
            for w in polys:
                T = w.acc[-1]
                ban |= graph.near_set(w.xy[(w.acc >= lo * T) & (w.acc <= hi * T)], D)
            ban -= {src, dst}
            pa = graph.dijkstra(src, dst, ban)
            if not pa:
                continue
            tag = 'r' if round_two else 'g'
            for n in ((3, 5, 8, 12) if round_two else (3, 5, 8)):
                out.append(('%sA%d_%d' % (tag, cfg, n), graph.vias(pa, n)))
            pxy, pacc = densify([graph.nodes[q] for q in pa])
            T = pacc[-1]
            ban2 = (ban | graph.near_set(pxy[(pacc >= lo * T) & (pacc <= hi * T)], D)) - {src, dst}
            pb = graph.dijkstra(src, dst, ban2)
            if pb:
                for n in (3, 5, 8):
                    out.append(('%sB%d_%d' % (tag, cfg, n), graph.vias(pb, n)))
        return out

    def forward(round_two):
        cands, jobs = [], []
        for i, t in enumerate(trips):
            if not t['main'] or t['kind'] == 'trunk':
                continue
            if round_two and len(pick_pair(t.get('fvalid', []), 0)) >= 2:
                continue
            for cid, vias in candidates(t, round_two):
                key = 'c%d_%s' % (i, cid)
                cands.append((i, cid, vias))
                jobs.append('trip %s %s' % (key, '>'.join([P(t['S'])] + [P(v) for v in vias] + [P(t['G'])])))
        if not jobs:
            return
        res = run.run(jobs, 'cand2' if round_two else 'cand1')
        for i, cid, vias in cands:
            t = trips[i]
            w = Walk(res['c%d_%s' % (i, cid)], world, t['ends'])
            why = judge(w, t['main'][0])
            t.setdefault('stats', collections.Counter())['fwd ' + (why or 'pass')] += 1
            if why is None:
                t.setdefault('fvalid', []).append(dict(id=cid, vias=vias, walk=[w, None]))
        for t in trips:
            t.get('fvalid', []).sort(key=lambda c: (round(c['walk'][0].len / t['main'][0].len, 2), len(c['vias'])))

    def pick_pair(pool, d):
        """Up to two lanes from the pool, apart from each other by SEP in each direction that has been walked."""
        distinct = []
        for c in pool:
            if all(separation(c['walk'][0], o['walk'][0]) >= 600 for o in distinct):
                distinct.append(c)
            if len(distinct) >= 20:
                break
        pool = distinct
        best = None
        for x, y in itertools.combinations(pool, 2):
            if all(x['walk'][k] is None or y['walk'][k] is None or separation(x['walk'][k], y['walk'][k]) >= SEP for k in (0, 1)):
                s = x['walk'][0].len + y['walk'][0].len
                if best is None or s < best[0]:
                    best = (s, [x, y])
        if best:
            return best[1]
        return pool[:1]

    forward(False)
    forward(True)

    # ---- the way back: every forward lane walked from the goal through the same via points, reversed ----
    jobs, tested = [], []
    for i, t in enumerate(trips):
        for c in t.get('fvalid', []):
            tested.append((i, c))
            jobs.append('trip r%d_%s %s' % (i, c['id'], '>'.join([P(t['G'])] + [P(v) for v in reversed(c['vias'])] + [P(t['S'])])))
    res = run.run(jobs, 'back') if jobs else {}
    for i, c in tested:
        t = trips[i]
        w = Walk(res['r%d_%s' % (i, c['id'])], world, t['ends'])
        why = judge(w, t['main'][1])
        c['back'] = why or 'pass'
        c['walk'][1] = w if w.ok else None
        t['stats']['back ' + c['back']] += 1

    # ---- the shipped lanes ----
    mesh = {}
    for z in world.zone_ids:
        mesh['zone%03d.nav' % z] = sha256_file(os.path.join(args.navroot, 'navmesh', 'zone%03d.nav' % z))
    for f in ('roads.json', 'seams.json', 'pockets.json'):
        mesh[f] = sha256_file(os.path.join(args.navroot, 'navmesh', f))
    mesh_digest = hashlib.sha256(json.dumps(mesh, sort_keys=True).encode()).hexdigest()

    def proof(points):
        blob = json.dumps(dict(tool=run.version, region=region, meshes=mesh_digest, points=[[round(v) for v in p[:3]] for p in points]),
                          sort_keys=True, separators=(',', ':'))
        return 'sha256:' + hashlib.sha256(blob.encode()).hexdigest()

    lanes, summary = [], []
    for t in trips:
        rec = dict(kind=t['kind'], frm=t['frm']['ref'], to=t['to']['ref'], stats=dict(t.get('stats', {})))
        summary.append(rec)
        if not t['main']:
            rec['lane0'] = 'failed: %s' % (t.get('main_fails') or 'no endpoint on main ground')
            continue
        fw, bw = t['main']
        rec['lane0'] = [fw.len, bw.len]
        base = dict(region=region, kind=t['kind'], **{'from': t['frm']}, to=t['to'], start=[round(v) for v in t['S'][:3]],
                    goal=[round(v) for v in t['G'][:3]])
        lanes.append(dict(base, lane=0, via=[], length=dict(forward=fw.len, back=bw.len), proof=proof([t['S'], t['G']])))
        both = [c for c in t.get('fvalid', []) if c.get('back') == 'pass']
        chosen = pick_pair(both, 0) if t['kind'] != 'trunk' else []
        rec['alternates'] = len(chosen)
        rec['one_way'] = sum(1 for c in t.get('fvalid', []) if c.get('back') == 'incomplete')
        for n, c in enumerate(chosen, 1):
            vias = [[round(v) for v in p[:3]] for p in c['vias']]
            lanes.append(dict(base, lane=n, via=vias, length=dict(forward=c['walk'][0].len, back=c['walk'][1].len),
                              proof=proof([t['S']] + c['vias'] + [t['G']])))
            rec.setdefault('alt', []).append(dict(id=c['id'], ratio=[round(c['walk'][0].len / fw.len, 3), round(c['walk'][1].len / bw.len, 3)],
                                                  sep=[round(separation(c['walk'][0], fw)), round(separation(c['walk'][1], bw))],
                                                  max_sep=round(max_separation(c['walk'][0], fw)), vias=len(vias)))
    objectives = [dict(ref='keep:%d' % k['id'], kind='portal keep' if k['portal'] else 'relic keep' if k['relic'] else 'keep',
                       region=region, keep_id=k['id'], name=k['name'], at=[k['x'], k['y'], k['z']]) for k in world.keeps]
    for name, node, realm in GATES[region]:
        objectives.append(dict(ref='milegate:' + name, kind='milegate', region=region, name=name, at=list(node),
                               portal_keep=portal_of[realm]['id']))
    for name, point in ENTRIES[region]:
        starts = sorted({tuple(t['S']) for t in trips if t['main'] and t['frm']['ref'] == 'entry:' + name})
        objectives.append(dict(ref='entry:' + name, kind='entry', region=region, name=name, at=list(starts[0]) if starts else list(point)))
    return dict(region=region, tool=run.version, meshes=mesh, lanes=lanes, objectives=objectives, summary=summary,
                hop_pairs=pairs_all)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--db', required=True)
    ap.add_argument('--navroot', required=True)
    ap.add_argument('--tests-dll', required=True)
    ap.add_argument('--work', required=True)
    ap.add_argument('--out', help='write the catalog (after all regions in --regions are built)')
    ap.add_argument('--regions', default='1,100,200')
    ap.add_argument('--hop-radius', type=float, default=60000)
    ap.add_argument('--lock', help='flock file for every dotnet run')
    ap.add_argument('--dotnet', default='dotnet')
    ap.add_argument('--built', default=time.strftime('%Y-%m-%d'))
    args = ap.parse_args()
    os.makedirs(args.work, exist_ok=True)
    parts = []
    for r in (int(x) for x in args.regions.split(',')):
        part_file = os.path.join(args.work, 'region-%d.json' % r)
        part = build_region(args, r)
        with open(part_file, 'w') as f:
            json.dump(part, f, indent=1)
        log('region %d: %d lanes' % (r, len(part['lanes'])))
        parts.append(part)
    if not args.out:
        return
    parts = [json.load(open(os.path.join(args.work, 'region-%d.json' % r))) for r in (1, 100, 200)
             if os.path.exists(os.path.join(args.work, 'region-%d.json' % r))]
    tools = {p['tool'] for p in parts}
    if len(tools) != 1:
        raise SystemExit('regions were built with different tool versions: %s' % tools)
    meshes = {}
    for p in parts:
        meshes.update(p['meshes'])
    catalog = collections.OrderedDict(
        version=1, tool=tools.pop(), built=args.built,
        note='Tests only: every lane completed a walk both ways in the server planner (UT_RvrLaneSurvey); none has been '
             'seen in game. Lane 0 is the planner\'s own route. The server re-walks every lane at start.',
        criteria=dict(max_length_ratio=MAX_RATIO, min_separation_middle_half=SEP, keep_clearance=KEEP_CLEAR,
                      max_swim=MAX_SWIM, monster_camp_radius=CAMP_RADIUS, hop_radius=args.hop_radius),
        meshes=dict(sorted(meshes.items())),
        objectives=[o for p in parts for o in p['objectives']],
        lanes=[l for p in parts for l in p['lanes']])
    with open(args.out, 'w', newline='\n') as f:
        json.dump(catalog, f, indent=1)
        f.write('\n')
    log('wrote %s: %d lanes' % (args.out, len(catalog['lanes'])))


if __name__ == '__main__':
    main()
