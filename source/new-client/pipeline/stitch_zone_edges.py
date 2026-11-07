"""Seamless zone borders: every outdoor zone's height grids are read through ONE region-wide height function, so
neighbouring zones share their border samples exactly (no crack) and the rendered ground's spline and normals run
on across the line.

Why: a zone's terrain.pcx has 256 x 256 samples (one per 256 units); the 257th row / column - the zone's far edge -
is the NEXT zone's first row / column in the client's region. build_zone.heights() pads it with the zone's own last
row (it built one zone alone), which left a step of up to 6.6 m along every border - the blue crack.

Writes, per outdoor zone (content/zones/zoneNNN):
  heights.f32           257 x 257  the client's samples, the far edge from the neighbours
  heights_2x.f32        513 x 513  Catmull-Rom refinement, the spline running through the neighbours' samples
  heights_2x_apron.f32  515 x 515  the same with one more sample all round (the neighbours') - for the edge normals
and adds 'heights_2x_apron' / 'heights_2x_apron_size' to zone.json. Where a region has no zone beyond an edge, the
edge sample is the zone's own (as before).

  python pipeline/stitch_zone_edges.py              all built outdoor zones
  python pipeline/stitch_zone_edges.py 200 201      only these (their neighbours are read, not written)
"""
import io, json, re, sys
from pathlib import Path
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT, ROOT
import nif_static

M = nif_static.UNITS_TO_METRES
N = 256                                                    # samples per zone side
CELL = 256                                                 # units between samples
BLOCK = 8192                                               # zones.dat offsets / sizes are in these


def _zones_dat():
    text = read_mpk(str(CLIENT / 'zones/zones.mpk'))['zones.dat'].decode('latin1')
    out = {}
    for m in re.finditer(r'\[zone(\d+)\]([^\[]*)', text):
        d = {k: v.strip() for k, v in re.findall(r'^(\w+)=(.*)$', m.group(2), re.M)}
        r = d.get('region', '')
        if not r.isdigit():
            continue
        out[int(m.group(1))] = {'region': int(r), 'type': d.get('type', '0') or '0',
                                'ox': int(d.get('region_offset_x', '0') or 0), 'oy': int(d.get('region_offset_y', '0') or 0),
                                'w': int(d.get('width', '8') or 8), 'h': int(d.get('height', '8') or 8)}
    return out


def _raw(zone_id):
    """The zone's 256 x 256 client samples in units (row = y, column = x), or None (no terrain: city / dungeon)."""
    for base in (CLIENT / 'zones', CLIENT / 'frontiers/zones'):
        p = base / f'zone{zone_id:03d}/dat{zone_id:03d}.mpk'
        if p.exists():
            break
    else:
        return None
    dat = read_mpk(str(p))
    if 'terrain.pcx' not in dat or 'offset.pcx' not in dat:
        return None
    import configparser
    cp = configparser.ConfigParser(strict=False, interpolation=None)
    cp.read_string(dat['sector.dat'].decode('latin1').lstrip('﻿'))
    terrain = np.asarray(Image.open(io.BytesIO(dat['terrain.pcx'])), dtype=np.float64)
    offset = np.asarray(Image.open(io.BytesIO(dat['offset.pcx'])), dtype=np.float64)
    return terrain * cp.getfloat('terrain', 'scalefactor') + offset * cp.getfloat('terrain', 'offsetfactor')


# the points around one, nearest first (the order fixed: every zone picks the same one)
_NEAR = sorted(((dx, dy) for dx in range(-3, 4) for dy in range(-3, 4) if (dx, dy) != (0, 0)),
               key=lambda d: (max(abs(d[0]), abs(d[1])), abs(d[0]) + abs(d[1]), d[1], d[0]))


class Region:
    """One region's terrain as a single height function on the 256-unit sample lattice."""

    def __init__(self, zones, region):
        self.cells = {}                                    # (sample x, sample y) of a zone's corner -> grid
        self.blocks = {}                                   # (block x, block y) -> that zone's corner (offsets need not be zone multiples)
        for zid, z in zones.items():
            if z['region'] != region or z['type'] != '0' or z['w'] != 8 or z['h'] != 8:
                continue
            g = _raw(zid)
            if g is not None and g.shape == (N, N):
                corner = (z['ox'] * BLOCK // CELL, z['oy'] * BLOCK // CELL)
                self.cells[corner] = g
                for bx in range(z['ox'], z['ox'] + 8):
                    for by in range(z['oy'], z['oy'] + 8):
                        self.blocks[(bx, by)] = corner

    def sample(self, sx, sy, home):
        """Height at lattice point (sx, sy) of the region; a point no zone covers falls back to the home zone's own
        nearest sample (an edge of the region's terrain)."""
        corner = self.blocks.get((sx * CELL // BLOCK, sy * CELL // BLOCK))
        if corner is not None:
            cx, cy = corner
            return self.cells[corner][sy - cy, sx - cx]
        # a point past the region's terrain next to a covered one takes that one's height, the same whichever zone
        # asks (2026-10-29, owner: "white lines for every zone boundry"): each zone fell back to its own nearest
        # sample, so where two zones' edges end at the region's border (Black Mtns. South / North: 53 m) they gave
        # their shared corner different heights - a torn corner
        for dx, dy in _NEAR:
            c2 = self.blocks.get(((sx + dx) * CELL // BLOCK, (sy + dy) * CELL // BLOCK))
            if c2 is not None:
                cx, cy = c2
                return self.cells[c2][sy + dy - cy, sx + dx - cx]
        hx, hy, hg = home
        return hg[min(max(sy - hy, 0), N - 1), min(max(sx - hx, 0), N - 1)]

    def grid(self, corner, pad):
        """The home zone's samples from -pad to N + pad (inclusive of the far edge): (N + 1 + 2 pad) square."""
        cx, cy = corner
        home = (cx, cy, self.cells[corner])
        r = range(-pad, N + 1 + pad)
        return np.array([[self.sample(cx + i, cy + j, home) for i in r] for j in r], dtype=np.float64)


def catmull_rom_2x(h):
    """Same refinement as build_zone.catmull_rom_2x (every original sample kept exactly)."""
    def axis(a):
        p = np.pad(a, ((1, 2), (0, 0)), mode='edge')
        p0, p1, p2, p3 = p[:-3], p[1:-2], p[2:-1], p[3:]
        mid = (-p0 + 9 * p1 + 9 * p2 - p3) / 16.0
        out = np.empty((a.shape[0] * 2 - 1, a.shape[1]))
        out[0::2] = a
        out[1::2] = mid[:a.shape[0] - 1]
        return out
    return axis(axis(h).T).T


def stitch(zone_ids=None):
    zones = _zones_dat()
    if zone_ids is None:
        zone_ids = []
        for zid in sorted(zones):
            info_p = ROOT / f'godot/content/zones/zone{zid:03d}/zone.json'
            if info_p.exists() and (ROOT / f'godot/content/zones/zone{zid:03d}/heights.f32').exists():
                info = json.loads(info_p.read_text(encoding='utf-8'))
                if not info.get('city') and not info.get('dungeon'):
                    zone_ids.append(zid)
    regions = {}
    for zid in zone_ids:
        z = zones.get(zid)
        if z is None:
            print(f'zone {zid}: not in zones.dat'); continue
        reg = regions.get(z['region']) or regions.setdefault(z['region'], Region(zones, z['region']))
        corner = (z['ox'] * BLOCK // CELL, z['oy'] * BLOCK // CELL)
        if corner not in reg.cells:
            print(f'zone {zid}: no outdoor terrain'); continue
        out = ROOT / f'godot/content/zones/zone{zid:03d}'
        g = reg.grid(corner, 2)                            # samples -2 .. 258
        fine = catmull_rom_2x(g)                           # index 2k = sample k - 2
        base = g[2:2 + N + 1, 2:2 + N + 1]                 # 257
        hi = fine[4:4 + 2 * N + 1, 4:4 + 2 * N + 1]       # 513
        apron = fine[3:3 + 2 * N + 3, 3:3 + 2 * N + 3]    # 515
        old = np.fromfile(out / 'heights.f32', dtype='<f4').reshape(N + 1, N + 1)
        assert np.allclose(old[:N, :N], base[:N, :N] * M, atol=1e-3), f'zone {zid}: interior samples differ from the build'
        (base * M).astype('<f4').tofile(out / 'heights.f32')
        (hi * M).astype('<f4').tofile(out / 'heights_2x.f32')
        (apron * M).astype('<f4').tofile(out / 'heights_2x_apron.f32')
        info_p = out / 'zone.json'
        info = json.loads(info_p.read_text(encoding='utf-8'))
        if info.get('heights_2x_apron') != 'heights_2x_apron.f32':
            info['heights_2x_apron'] = 'heights_2x_apron.f32'
            info['heights_2x_apron_size'] = 2 * N + 3
            info_p.write_text(json.dumps(info, indent=1), encoding='utf-8')
        edge = np.abs(old[N, :] - base[N, :] * M).max(), np.abs(old[:, N] - base[:, N] * M).max()
        print(f'zone {zid}: stitched (far edges moved up to {edge[0]:.2f} m south, {edge[1]:.2f} m east)')


if __name__ == '__main__':
    stitch([int(a) for a in sys.argv[1:]] or None)
