"""Build one DAoC zone into MMOEngine runtime content (MMOEngine milestone 1: walk one zone).

    python build_zone.py 200            # Lough Derg
    python build_zone.py 200 --no-upscale

Reads the client's zone files read-only and writes godot/content/zones/zone<id>/:
  zone.json                 name, sky / fog, water bodies, model list, every fixture placement, start points
  heights.f32               257 x 257 float32 terrain heights in metres (row = south, column = east)
  heights_2x.f32            513 x 513 Catmull-Rom refinement of the same grid (keeps every original sample)
  water.png                 256 x 256 water-body id per terrain cell (255 = dry)
  terrain/original/<set>.png    whole-zone ground colour: tex165 = 1.65 painted ground (tex<id>.mpk),
                                lod = later baked ground with roads (lod<id>.mpk); stitched from the 8 x 8 tiles
  models/<nif>.gltf         every placed model (nif_static.py), textures referenced by name
  textures/original/*.png   model textures as shipped
  textures/x4plus/*.png     Real-ESRGAN x4plus, 4x then down to 2x (owner rule: 2x for the bulk of the world)
  terrain/x4plus/<set>.png  the same, upscaled as one image (no seams)
  splat/patch_XX_YY_w0/1.png  ground blend weights (layers 1-8) for the Catacombs-era ground the client draws
Coordinates: DAoC zone units (0..65536, X east, Y south, Z up) become metres with x = X, z = Y, y = Z (x0.025).
Fixtures come from dat<id>.mpk, the zone's own table; later patch tables (csv<id>.mpk) add freeshard extras such as
SI portals, which the clean 1.65 world leaves out. Re-running only redoes textures whose source changed.
"""
import argparse, configparser, csv, io, json, re, shutil, subprocess, sys, tempfile
from pathlib import Path
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None                 # our own x4plus terrain mosaics reach 16384 x 16384 (4096 sources)

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT, ROOT
from texture_index import TextureIndex
import nif_static
import alpha_bleed
import tone_restore

M = nif_static.UNITS_TO_METRES
ESRGAN = ROOT / 'tools/realesrgan/realesrgan-ncnn-vulkan.exe'
BOM = '\N{ZERO WIDTH NO-BREAK SPACE}\xef\xbb\xbf'


def read_csv(raw):
    return [r for r in csv.reader(io.StringIO(raw.decode('latin1')))]


def fixture_radius(r):
    """fixtures.csv Radius (units): a fixture the client doesn't collide with (trees) but gives a Radius gets an
    invisible upright box wall around its trunk (game.dll 0x49084e; ZoneLoader.proxy_faces)."""
    try:
        return int(float(r[9] or 0))
    except (ValueError, IndexError):
        return 0


def sector_ini(raw):
    cp = configparser.ConfigParser(strict=False, interpolation=None, inline_comment_prefixes=(';',))
    cp.read_string(raw.decode('latin1').lstrip(BOM))       # zones.dat starts with a UTF-8 byte-order mark
    return cp


def water_base_color(s):
    """sector.dat [riverNN] base_color_hex AARRGGBB (the Shrouded Isles' textureless water) as [r, g, b, a] 0..1."""
    h = s.get('base_color_hex', '').strip()
    if len(h) != 8:
        return None
    a, r, g, b = (int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4, 6))
    return [round(r, 4), round(g, 4), round(b, 4), round(a, 4)]


def zone_section(zone_id):
    """This zone's [zoneNNN] section of zones.dat as text (the file mixes INI sections with banner lines)."""
    text = read_mpk(str(CLIENT / 'zones/zones.mpk'))['zones.dat'].decode('latin1')
    m = re.search(rf'\[zone{zone_id:03d}\](.*?)(?:\n\[|$)', text, re.S | re.I)
    return m.group(1) if m else ''


SI_REGIONS = {51: 'alb', 151: 'mid', 181: 'hib'}           # the Shrouded Isles regions (zones.dat region=)


def clutter_realm(zone_id):
    """(realm, expansion) for the ground clutter set: the Shrouded Isles zones use grass_<realm>exp (owner 2026-10-11:
    Isle of Glass / Avalon Isle have the sparse dry grass of the expansion set) - zones.dat has no expansion flag for
    them, so the region decides; Hibernia's SI zones (181+) are Hibernian, not zone_id // 100's Midgard."""
    reg = re.search(r'^\s*region\s*=\s*0*(\d+)', zone_section(zone_id), re.M)
    region = int(reg.group(1)) if reg else -1
    if region in SI_REGIONS:
        return SI_REGIONS[region], True
    exp_match = re.search(r'expansion\s*=\s*(\d+)', zone_section(zone_id))
    return {0: 'alb', 1: 'mid', 2: 'hib'}.get(zone_id // 100), bool(exp_match and int(exp_match.group(1)) == 1)


def clutter_items(grass_csv):
    """The clutter kinds of a grass_*.csv (name, group, chance, shape, sizes, atlas rect, tint)."""
    items = []
    for r in read_csv(grass_csv.read_bytes())[2:]:
        if len(r) < 16 or not r[0].strip().isdigit():
            continue
        num = lambda k: float(r[k]) if k < len(r) and r[k].strip() not in ('',) else 0.0
        tint = [num(16) / 255, num(17) / 255, num(18) / 255] if len(r) > 18 and r[16].strip() else [1.0, 1.0, 1.0]
        items.append({'name': r[1].strip(), 'group': int(num(2)), 'chance': num(3), 'shape': int(num(4)),
                      'height': num(5) * M, 'd_height': num(6) * M, 'length': num(7) * M, 'd_length': num(8) * M,
                      'width': num(9) * M, 'd_width': num(10) * M, 'd_scale': num(11) / 100.0,
                      'rect': [num(12), num(13), num(14), num(15)], 'tint': tint})
    return items


def zone_name(zone_id):
    name = re.search(r'^\s*name\s*=\s*(.+?)\s*$', zone_section(zone_id), re.M)
    return name.group(1) if name else f'zone{zone_id:03d}'


def heights(dat, sector):
    terrain = np.asarray(Image.open(io.BytesIO(dat['terrain.pcx'])), dtype=np.float64)
    offset = np.asarray(Image.open(io.BytesIO(dat['offset.pcx'])), dtype=np.float64)
    scale = sector.getfloat('terrain', 'scalefactor')
    off = sector.getfloat('terrain', 'offsetfactor')
    h = terrain * scale + offset * off                      # DAoC units, one sample per 256 units
    return np.pad(h, ((0, 1), (0, 1)), mode='edge')         # 257 x 257 so the grid reaches the zone edge


def catmull_rom_2x(h):
    """Double the grid resolution with Catmull-Rom splines; every original sample is kept exactly."""
    def axis(a):
        p = np.pad(a, ((1, 2), (0, 0)), mode='edge')
        p0, p1, p2, p3 = p[:-3], p[1:-2], p[2:-1], p[3:]
        mid = (-p0 + 9 * p1 + 9 * p2 - p3) / 16.0          # t = 0.5 on the Catmull-Rom curve
        out = np.empty((a.shape[0] * 2 - 1, a.shape[1]))
        out[0::2] = a
        out[1::2] = mid[:a.shape[0] - 1]
        return out
    return axis(axis(h).T).T


def height_at(h, x, y, cell=256.0):
    n = h.shape[0] - 1
    gx, gy = np.clip(x / cell, 0, n - 0.001), np.clip(y / cell, 0, n - 0.001)
    x0, y0 = int(gx), int(gy)
    fx, fy = gx - x0, gy - y0
    top = h[y0, x0] * (1 - fx) + h[y0, x0 + 1] * fx
    bottom = h[y0 + 1, x0] * (1 - fx) + h[y0 + 1, x0 + 1] * fx
    return top * (1 - fy) + bottom * fy


def ambient_only(nif_row):
    """nifs.csv column 3, under the "Ambient" group header: "Only" - the client lights the model with the ambient light
    alone (every tree, evergreen and bush; owner 2026-10-14: Malmohus' pines drew black trunk columns in sunlight)."""
    return len(nif_row) > 3 and nif_row[3].strip() == '1'


def save_if_changed(img, path):
    buf = io.BytesIO()
    img.save(buf, 'PNG')
    if not path.exists() or path.read_bytes() != buf.getvalue():
        path.write_bytes(buf.getvalue())


def upscale_dir(src, dst, factor_after=2):
    """Real-ESRGAN x4plus on every PNG in src not yet done, then Lanczos down to 2x of the original into dst."""
    dst.mkdir(parents=True, exist_ok=True)
    todo = [p for p in sorted(src.glob('*.png'))
            if not (dst / p.name).exists() or (dst / p.name).stat().st_mtime < p.stat().st_mtime]
    if not todo:
        return
    with tempfile.TemporaryDirectory() as tmp, tempfile.TemporaryDirectory() as pending:
        for p in todo:                                     # cut-outs bled first: no dark outline (alpha_bleed.py)
            img = Image.open(p)
            if alpha_bleed.has_cutout(img):
                alpha_bleed.bleed(img).save(Path(pending) / p.name)
            else:
                shutil.copy2(p, Path(pending) / p.name)
        subprocess.run([str(ESRGAN), '-i', pending, '-o', tmp, '-n', 'realesrgan-x4plus', '-s', '4', '-f', 'png'],
                       cwd=ESRGAN.parent, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for p in todo:
            w, h = Image.open(p).size
            size = (w * factor_after, h * factor_after)
            if _plain(p):
                _finish(p, _plain_resize(p, size)).save(dst / p.name)
                continue
            big = Image.open(Path(tmp) / p.name)
            if _mismatch(p, big):                          # folder mode once paired outputs with the wrong names
                big = _upscale_one(p, Path(tmp))           # (ui/classic, 2026-10-09): redo this one on its own
            if _mismatch(p, big):                          # still not the picture: the network's own failure
                _finish(p, _plain_resize(p, size)).save(dst / p.name)
                continue
            # the client's own colours back at its resolution (Real-ESRGAN darkens dark pictures: tone_restore.py)
            _finish(p, tone_restore.restore(Image.open(p), big.resize(size, Image.LANCZOS))).save(dst / p.name)


def _finish(src, img):
    """A cut-out's hidden colour bled again after the resize (clean mipmaps: owner 2026-10-14, black lines on pines)."""
    s = Image.open(src)
    return alpha_bleed.bleed(img, alpha_bleed.is_binary(s)) if alpha_bleed.has_cutout(s) else img


# Real-ESRGAN fails on tiny pictures and flat fills (2026-10-13: the Sluagh Blood Shield's 8x8 solid black came back
# as a 32x32 grey smear with alpha 0-111 - the opaque dark shield drew as a striped see-through cage; the 4x64 glow
# cones, eyerim 8x128, hcrstglo 8x32 the same). Those get a plain resize: there's no detail for the network to add.
def _plain(src):
    img = Image.open(src)
    return min(img.size) < 16 or len(img.convert('RGBA').getcolors(4) or ()) in (1, 2)


def _plain_resize(src, size):
    img = Image.open(src)
    img = alpha_bleed.bleed(img) if alpha_bleed.has_cutout(img) else img.convert('RGBA')
    return img.resize(size, Image.NEAREST if len(img.getcolors(4) or ()) == 1 else Image.BICUBIC)


FIDELITY = 0.85


def _mismatch(src, big):
    """The upscale isn't the source picture: colour where it's opaque, or its alpha (premultiplied), off by over 30;
    or its detail not the source's - brought back to the source's size, its brightness correlates under FIDELITY with
    the source's over the opaque pixels (2026-10-14, owner: Jordheim's grass "blocky and patchy" - Real-ESRGAN turns
    fine natural noise, grass / hay / canvas, into blotches with seams; r 0.55 there, a faithful upscale is ~0.95)."""
    import numpy as np
    a = Image.open(src).convert('RGBA')
    b = big.convert('RGBA').resize(a.size, Image.BILINEAR)
    A, B = np.asarray(a, np.float32), np.asarray(b, np.float32)
    w = A[..., 3] > 128
    if w.sum() >= 16 and float(np.abs(A[..., :3] - B[..., :3])[w].mean()) > 30.0:
        return True
    if w.sum() >= 64 and min(a.size) >= 16:
        la = np.asarray(a.convert('L'), np.float32)[w]
        lb = np.asarray(big.convert('L').resize(a.size, Image.BOX), np.float32)[w]
        if la.std() >= 2 and lb.std() > 0 and float(np.corrcoef(la, lb)[0, 1]) < FIDELITY:
            return True
    pa = A[..., :3] * A[..., 3:] / 255.0
    pb = B[..., :3] * B[..., 3:] / 255.0
    return float(np.abs(A[..., 3] - B[..., 3]).mean()) > 30.0 or float(np.abs(pa - pb).mean()) > 30.0


def _upscale_one(src, tmp):
    one_in, one_out = tmp / '_one_in', tmp / '_one_out'
    for d in (one_in, one_out):
        shutil.rmtree(d, ignore_errors=True)
        d.mkdir()
    shutil.copy2(src, one_in / src.name)
    subprocess.run([str(ESRGAN), '-i', str(one_in), '-o', str(one_out), '-n', 'realesrgan-x4plus', '-s', '4', '-f', 'png'],
                   cwd=ESRGAN.parent, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return Image.open(one_out / src.name).copy()


# The old server's prop objects (tents, campfires, crates, banners, forges...) were DB rows, not client zone files:
# WorldObject (GameStaticItem) model -> gamedata objects.csv row -> NIF # -> items.csv name -> items/<name>.nif.
# Owner 2026-10-09: they become real world objects in the zone data, not tied to any server code, game-wide.
# Read-only, as data. Untargetable prop NPCs (Mob rows with the CANTTARGET flag whose model is a prop) go the same way;
# in Lough Derg those are all creatures/controllers (ambient rats, pixies, flies, siabra), so none qualify there.
OLD_DB = ROOT / 'external/old_project/runtime/data/opendaoc.sqlite3.db'


def object_effects(g, objects, region):
    """{objects.csv id: [(effect nif filename, attach node name, visible range units)]} via the client's Realm Effect
    chain: objects.csv cols 56/57/58 (Alb/Mid/Hib, picked by the region's realm) -> moneffectsets.csv (up to 6
    effects) -> moneffects.csv (NIF num, attach node, max visible range) -> moneffectnifs.csv filename."""
    col = 58 if 200 <= region < 300 else 57 if 100 <= region < 200 else 56
    sets = {r[0].strip(): r for r in read_csv(g['moneffectsets.csv']) if r and r[0].strip().isdigit()}
    effs = {r[0].strip(): r for r in read_csv(g['moneffects.csv']) if r and r[0].strip().isdigit()}
    nifs = {r[0].strip(): r for r in read_csv(g['moneffectnifs.csv']) if r and r[0].strip().isdigit()}
    out = {}
    for oid, r in objects.items():
        eset = r[col].strip() if len(r) > col else ''
        if not eset.isdigit() or eset == '0' or eset not in sets:
            continue
        for e in sets[eset][2:8]:
            e = e.strip()
            if e.isdigit() and e != '0' and e in effs and effs[e][2].strip() in nifs:
                er = effs[e]
                rng = float(er[4]) if len(er) > 4 and er[4].strip() else 4000.0
                out.setdefault(oid, []).append((nifs[er[2].strip()][2].strip(), er[3].strip(), rng))
    return out


# Old-DB props placed wrong (owner 2026-10-10, "a floating flag, no tower"): {(region, name, x, y): (x, y, z)} global
# units. Camelot Hills: the banner for the guard tower at zone 19456, 26368 (fixture 36) stood 4625 units north of it
# on an empty hill; it goes on the tower's roof with the same offset as the banner on the other guard tower (fixture 4:
# tower 39936, 32000, 2848 -> banner -68, +4, +1056).
WORLD_OBJECT_FIX = {(1, 'Albion Banner', 67 * 8192 + 19412, 59 * 8192 + 30993): (67 * 8192 + 19456 - 68, 59 * 8192 + 26368 + 4, 2818 + 1056)}
# Old-DB props that float over the client's ground (owner 2026-10-10 "check all 31 ... in game": tests/props_probe.gd
# measured the gap from the model's lowest point to the surface under it): {(region, name, x, y): gap in metres}; the
# prop goes down by the gap + 0.1 m (the props that sit right are 0.1-0.2 m into their floor). The Dartmoor building
# supplies hung 7-14 m over the hillside; the Statue of Flitzitina 5.9 m over the garden. Left as they are: the Dartmoor smoldering rubble (the four
# small piles lie on the burnt tower's wall tops and the large pile spans between them - a fallen burnt roof) and the
# banner poles the probe put 1-1.3 m up (its ray hit the pole's own collision; the pole tips touch the ground).
WORLD_OBJECT_DROP = {
    (1, 'Statue of Flitzitina', 505080, 496334): 5.9,
    (1, 'building supplies', 387045, 721976): 7.8, (1, 'building supplies', 388601, 723658): 11.4,
    (1, 'building supplies', 386996, 721975): 7.3, (1, 'building supplies', 386989, 722014): 7.1,
    (1, 'building supplies', 388732, 723583): 13.9,
    # campfires (wardnode + campfire effect) hung over the ground (owner 2026-10-20: "a floating campfire" at Camelot
    # Hills loc 51322 38985): the old DB's height against the client's ground at the object's origin
    (1, 'New Object', 600246, 522344): 0.78, (1, 'New Object', 558213, 524362): 1.07,
    (1, 'New Object', 489433, 437934): 0.54, (100, 'campfire', 747601, 757900): 3.91}
# Old-DB props for post-1.65 buildings (owner 2026-10-10 "another missing tower"): the Cotswold pennant (New Object,
# pennant_a, 29 m up) tops the New Frontiers guard tower the later client added at 13916, 26591 ("Guard tower
# (Instance 2)", next to the NF fences); the 1.65 client has no tower there, so the pennant is left out.
WORLD_OBJECT_SKIP = {(1, 'New Object', 562795, 510008)}


def world_objects(zone_id):
    """[(name, nif_filename, x, y, z, heading_4096, effects)] for the zone's server prop objects, zone units."""
    import sqlite3
    if not OLD_DB.exists():
        return []
    sec = dict(l.split('=', 1) for l in zone_section(zone_id).splitlines() if '=' in l)
    sec = {k.strip().lower(): v.strip() for k, v in sec.items()}
    region = int(sec.get('region', zone_id))
    ox, oy = int(sec.get('region_offset_x', 0)) * 8192, int(sec.get('region_offset_y', 0)) * 8192
    w, h = int(sec.get('width', 8)) * 8192, int(sec.get('height', 8)) * 8192
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    objects = {r[0].strip(): r for r in read_csv(g['objects.csv']) if r and r[0].strip().isdigit()}
    items = {r[0].strip(): r for r in read_csv(g['items.csv']) if r and r[0].strip().isdigit()}
    effects = object_effects(g, objects, region)
    con = sqlite3.connect(f'file:{OLD_DB.as_posix()}?mode=ro', uri=True)
    out = []
    for name, x, y, z, heading, model in con.execute(
            'select Name, X, Y, Z, Heading, Model from WorldObject where Region=? and X>=? and X<? and Y>=? and Y<?',
            (region, ox, ox + w, oy, oy + h)):
        obj = objects.get(str(model))
        item = items.get(obj[2].strip()) if obj and len(obj) > 2 else None
        if item is None or len(item) < 3 or not item[2].strip():
            print('world object without a client model:', name, model)
            continue
        fix = next((v for (r, n, fx, fy), v in WORLD_OBJECT_FIX.items() if r == region and n == name
                    and abs(fx - x) < 8 and abs(fy - y) < 8), None)
        if any(r == region and n == name and abs(fx - x) < 4 and abs(fy - y) < 4 for r, n, fx, fy in WORLD_OBJECT_SKIP):
            continue
        if fix:
            x, y, z = fix
        drop = next((v for (r, n, fx, fy), v in WORLD_OBJECT_DROP.items() if r == region and n == name
                     and abs(fx - x) < 4 and abs(fy - y) < 4), None)
        if drop:
            z -= round((drop + 0.1) * 40)                     # 40 units a metre
        out.append((name, item[2].strip(), x - ox, y - oy, z, heading or 0, effects.get(str(model), [])))
    con.close()
    return out


# City zones (zones.dat type=1: Tir na Nog, Camelot, Jordheim) have no terrain: dat<id>.mpk city.csv lists the city
# chunk NIFs (zones/zone<id>/nifs/<name>NN.npk, each with optional <name>NN_pMM.npk collision patches whose
# '__patch_collidee' node is extra wall geometry). The chunks are modelled in one city frame; the client turns that
# frame by sector.dat [rotate] rotate (x 90 degrees) and puts its origin at CITY_ORIGIN. Tir na Nog's origin was
# measured (2026-10-09) by fitting the old server's 284 region-201 NPC positions onto the chunk floors: rotate=2 (180
# degrees), origin (23808, 23808, 8000) zone units - 262 of 284 NPCs stand within 8 units of a floor there.
# Camelot (26) and Avalon City (50) measured the same way (scratch city_fit.py, 2026-10-09): the same origin and the
# same 180 degree turn - 217 of 230 and 917 of 1020 NPCs on a floor - although their sector.dat has no [rotate]
# section, so a missing rotate means 2. Jordheim (120: rotate=2) takes the same frame: 181 of 187 NPCs on a floor.
CITY_ORIGIN = {209: (23808, 23808, 8000), 26: (23808, 23808, 8000), 50: (23808, 23808, 8000),
               120: (23808, 23808, 8000)}
# Dungeons (zones.dat type 2) the same way: dat<id>.mpk dungeon.chunk = piece NIF names (zones/Dnifs/<name>.npk),
# dungeon.place = one line per placed piece: chunk index, x, y, z, angle (radians), axis x, y, z, flag. The pieces
# share a dungeon frame turned 180 degrees with its origin at DUNGEON_ORIGIN - measured on Coruscating Mine (220) from
# the old DB's 296 NPC positions (288 within 8 units of a floor at 23808, 23808, 16000; the exit box sits at 16000).
# The same origin as the cities' X / Y. Other dungeons: check with the fit (scratch dungeon_fit.py) when built.
DUNGEON_ORIGIN = (23808, 23808, 16000)


def _axis_angle(angle, ax):
    ax = np.array(ax, float)
    n = np.linalg.norm(ax)
    if n < 1e-9 or abs(angle) < 1e-9:
        return np.identity(3)
    x, y, z = ax / n
    c, s_ = np.cos(angle), np.sin(angle)
    C = 1 - c
    return np.array([[c + x * x * C, x * y * C - z * s_, x * z * C + y * s_],
                     [y * x * C + z * s_, c + y * y * C, y * z * C - x * s_],
                     [z * x * C - y * s_, z * y * C + x * s_, c + z * z * C]])


def build_dungeon(zone_id, upscale, zdir, out, dat, sector):
    """A dungeon: its pieces placed by dungeon.place in the dungeon frame (see DUNGEON_ORIGIN). Each placement is a
    fixture with a full basis (a few pieces carry tiny tilts)."""
    ox, oy, oz = DUNGEON_ORIGIN
    files = {p.name.lower(): p for p in (CLIENT / 'zones/Dnifs').iterdir()}
    mdir = out / 'models'
    mdir.mkdir(exist_ok=True)
    chunks = [l.strip() for l in dat['dungeon.chunk'].decode('latin1').splitlines() if l.strip()]
    tex_names, models, placements, index = set(), [], [], {}
    # NIF (x, y, z up) -> Godot (x, z, -y); the frame: 180 degrees about up, then the origin
    conv = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], float)
    turn = np.array([[-1, 0, 0], [0, 1, 0], [0, 0, -1]], float)
    origin = np.array([ox * M, oz * M, oy * M])
    for line in dat['dungeon.place'].decode('latin1').splitlines():
        if not line.strip():
            continue
        v = [float(x) for x in line.split(',')]
        ci = int(v[0])
        if ci >= len(chunks):
            continue
        base = chunks[ci].rsplit('.', 1)[0].lower()
        if base not in index:
            src = files.get(base + '.npk')
            if src is None:
                print('missing dungeon piece', base)
                index[base] = None
                continue
            raw = next(x for k, x in read_mpk(str(src)).items() if k.endswith('.nif'))
            try:
                info = nif_static.convert(raw, str(mdir / f'{base}.gltf'), '../textures/original')
            except Exception as e:
                print('dungeon piece failed', base, type(e).__name__, e)
                index[base] = None
                continue
            tex_names |= info['textures']
            index[base] = len(models)
            models.append({'nif': base, 'name': base, 'file': f'models/{base}.gltf', 'triangles': info['triangles'],
                           'collision_triangles': info['collision_triangles'],
                           'warnings': [f'{base}: {w}' for w in info['warnings']]})
        if index[base] is None:
            continue
        # the angle turns the other way round (NetImmerse's row-vector rotations): with -angle 288 of the 296 NPCs
        # stand within 8 units of a floor, with +angle only 192
        R = turn @ conv @ _axis_angle(-v[4], v[5:8]) @ conv.T
        pos = turn @ (conv @ np.array(v[1:4]) * M) + origin
        placements.append({'m': index[base], 'name': base, 'pos': [round(float(x), 4) for x in pos],
                           'basis': [round(float(x), 6) for x in R.T.reshape(-1)],     # columns x, y, z
                           'yaw': 0.0, 'scale': 1.0, 'collide': True, 'id': len(placements)})
    lights = zone_lights(dat)
    exits = zone_exits(dat)
    idx = TextureIndex()
    odir = out / 'textures/original'
    odir.mkdir(parents=True, exist_ok=True)
    missing = []
    for n in sorted(tex_names):
        doubled = n.endswith(('__d2', '__x2'))   # __x2: the raw detail map (detail_x2.py)
        img = idx.image(n[:-4] if doubled else n) or nif_static.EMBEDDED.get(n[:-4] if doubled else n)   # (a NIF's own pixels)
        if img is None:
            missing.append(n)
            continue
        if doubled and n.endswith('__d2'):
            rgb = np.asarray(img.convert('RGB'), dtype=np.float32) * 2.0
            img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
        save_if_changed(img, odir / f'{n}.png')
    if upscale:
        upscale_dir(odir, out / 'textures/x4plus')
    # the fallback height grid: the dungeon's highest floor per point (the engine casts from a reference height)
    city_heights_placed(out, models, placements)
    spots = entrance_spots(zone_id)                       # where the old server's ZonePoints bring you in
    spots_file = HERE / 'data' / 'zone_spots.json'
    if spots_file.exists():
        for sp in json.loads(spots_file.read_text(encoding='utf-8')).get(str(zone_id), []):
            spots.append({'name': sp['name'], 'pos': [float(sp['x']) * M, float(sp['z']) * M, float(sp['y']) * M],
                          'face': float(sp.get('face', 0)), 'start': bool(sp.get('start', False))})
    warnings = sorted({w for m in models for w in m.get('warnings', [])})
    zone = {'id': zone_id, 'name': zone_name(zone_id), 'city': True, 'dungeon': True, 'size_m': 65536 * M,
            'cell_m': 256 * M, 'heights': 'heights_2x.f32', 'heights_size': 513, 'heights_2x': 'heights_2x.f32',
            'heights_2x_size': 513, 'water': [], 'terrain_sets': {},
            # the dungeon's own fog and sky colours (sector.dat; Darkness Falls: purple fog, amount 85 - owner 2026-10-10
            # "missing all its atmosphere": these were left black)
            'fog': dungeon_fog(sector), 'sky': [sector.getint('sky', c, fallback=0) / 255.0 for c in ('red', 'green', 'blue')],
            'models': models,
            'fixtures': placements, 'jump_points': {}, 'exits': exits, 'spots': spots,
            'texture_sets': {'original': 'textures/original',
                             **({'x4plus': 'textures/x4plus'} if upscale or (out / 'textures/x4plus').exists() else {})},
            'lights': lights, 'missing_textures': missing, 'warnings': warnings}
    (out / 'zone.json').write_text(json.dumps(zone, indent=1), encoding='utf-8')
    print(f"{zone['name']} (dungeon): {len(models)} pieces, {len(placements)} placed, {len(lights)} lights, "
          f"{len(exits)} exits, {len(tex_names)} textures ({len(missing)} missing: {missing[:8]})")


def city_heights_placed(out, models, placements):
    """The fallback height grid from placed fixtures with a basis (dungeons): the highest floor over each point."""
    n, size = 513, 65536 * M
    step = size / (n - 1)
    grid = np.full((n, n), -1e9)
    tri_cache = {}
    for f in placements:
        m = models[f['m']]
        if m['file'] not in tri_cache:
            tri_cache[m['file']] = gltf_triangles(out / m['file'])
        t = tri_cache[m['file']]
        if len(t) == 0:
            continue
        B = np.array(f['basis'], float).reshape(3, 3).T             # rows -> the basis matrix (columns stored)
        w = t @ B.T + np.array(f['pos'])
        wx, wy, wz = w[..., 0], w[..., 1], w[..., 2]
        for i in range(len(w)):
            gx = np.arange(int(np.ceil(wx[i].min() / step)), int(np.floor(wx[i].max() / step)) + 1)
            gz = np.arange(int(np.ceil(wz[i].min() / step)), int(np.floor(wz[i].max() / step)) + 1)
            if len(gx) == 0 or len(gz) == 0:
                continue
            PX, PZ = np.meshgrid(gx * step, gz * step)
            ax, az, bx, bz, cx, cz = wx[i, 0], wz[i, 0], wx[i, 1], wz[i, 1], wx[i, 2], wz[i, 2]
            d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
            if abs(d) < 1e-9:
                continue
            l1 = ((bz - cz) * (PX - cx) + (cx - bx) * (PZ - cz)) / d
            l2 = ((cz - az) * (PX - cx) + (ax - cx) * (PZ - cz)) / d
            l3 = 1 - l1 - l2
            inside = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
            h = l1 * wy[i, 0] + l2 * wy[i, 1] + l3 * wy[i, 2]
            for (r, k) in zip(*np.nonzero(inside)):
                zi, xi = gz[r], gx[k]
                if 0 <= zi < n and 0 <= xi < n and h[r, k] > grid[zi, xi]:
                    grid[zi, xi] = h[r, k]
    known = grid > -1e8
    grid[~known] = grid[known].min() if known.any() else 0.0
    grid.astype('<f4').tofile(out / 'heights_2x.f32')


def build_city(zone_id, upscale, zdir, out, dat, sector):
    origin = CITY_ORIGIN.get(zone_id)
    if origin is None:
        raise SystemExit(f'zone {zone_id}: city origin not measured yet (see CITY_ORIGIN)')
    rot = sector.getint('rotate', 'rotate', fallback=2) * 90.0     # no [rotate]: 2 (Camelot / Avalon City fits)
    files = {p.name.lower(): p for p in (zdir / 'nifs').iterdir()}
    mdir = out / 'models'
    mdir.mkdir(exist_ok=True)
    chunks = [r[1].strip() for r in read_csv(dat['city.csv']) if len(r) > 1 and r[0].strip().isdigit() and r[1].strip()]
    tex_names, models, placements = set(), [], []
    ox, oy, oz = origin
    for name in chunks:
        base = name.rsplit('.', 1)[0].lower()
        for part in [base] + sorted(n[:-4] for n in files if n.startswith(base + '_p') and n.endswith('.npk')):
            src = files.get(part + '.npk')
            if src is None:
                print('missing city chunk', part)
                continue
            raw = next(v for k, v in read_mpk(str(src)).items() if k.endswith('.nif'))
            try:
                info = nif_static.convert(raw, str(mdir / f'{part}.gltf'), '../textures/original')
            except Exception as e:
                print('city chunk failed', part, type(e).__name__, e, raw[:40])
                continue
            tex_names |= info['textures']
            placements.append({'m': len(models), 'name': part, 'pos': [ox * M, oz * M, oy * M], 'yaw': rot,
                               'scale': 1.0, 'collide': True, 'id': len(placements)})
            models.append({'nif': part, 'name': part, 'file': f'models/{part}.gltf', 'triangles': info['triangles'],
                           'collision_triangles': info['collision_triangles'],
                           'warnings': [f'{part}: {w}' for w in info['warnings']]})
    # a fallback ground grid (513 x 513, like heights_2x): the highest city floor over each point, for the engine's
    # height lookups away from a reference height (the engine casts onto the floors when it has one)
    city_heights(out, mdir, models, origin, rot)
    # point lights (absolute heights) and the zone's exits: zonejump.csv = id, name, x1, y1, x2, y2, z1, z2, target
    lights = zone_lights(dat)
    exits = zone_exits(dat)
    # textures: the city's own folder first (other zones ship textures with the same names)
    idx = TextureIndex()
    idx.first = [f'zones/zone{zone_id:03d}/nifs']
    odir = out / 'textures/original'
    odir.mkdir(parents=True, exist_ok=True)
    missing = []
    for n in sorted(tex_names):
        doubled = n.endswith(('__d2', '__x2'))   # __x2: the raw detail map (detail_x2.py)
        img = idx.image(n[:-4] if doubled else n) or nif_static.EMBEDDED.get(n[:-4] if doubled else n)   # (a NIF's own pixels)
        if img is None:
            missing.append(n)
            continue
        if doubled and n.endswith('__d2'):
            rgb = np.asarray(img.convert('RGB'), dtype=np.float32) * 2.0
            img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
        save_if_changed(img, odir / f'{n}.png')
    if upscale:
        upscale_dir(odir, out / 'textures/x4plus')
    spots = []
    spots_file = HERE / 'data' / 'zone_spots.json'
    if spots_file.exists():
        for sp in json.loads(spots_file.read_text(encoding='utf-8')).get(str(zone_id), []):
            spots.append({'name': sp['name'], 'pos': [float(sp['x']) * M, float(sp['z']) * M, float(sp['y']) * M],
                          'face': float(sp.get('face', 0)), 'start': bool(sp.get('start', False))})

    def rgb(sec):
        return [sector.getint(sec, c, fallback=0) / 255.0 for c in ('red', 'green', 'blue')]

    warnings = sorted({w for m in models for w in m.get('warnings', [])})
    zone = {'id': zone_id, 'name': zone_name(zone_id), 'city': True, 'size_m': 65536 * M, 'cell_m': 256 * M,
            'heights': 'heights_2x.f32', 'heights_size': 513, 'heights_2x': 'heights_2x.f32', 'heights_2x_size': 513,
            'water': [], 'terrain_sets': {},
            'fog': {'color': rgb('fog'), 'clip_m': sector.getfloat('fog', 'clip', fallback=16000) * M,
                    'amount': sector.getint('fog', 'amount', fallback=0)},
            'sky': rgb('sky'), 'models': models, 'fixtures': placements, 'jump_points': {}, 'exits': exits,
            'spots': spots,
            'texture_sets': {'original': 'textures/original',
                             **({'x4plus': 'textures/x4plus'} if upscale or (out / 'textures/x4plus').exists() else {})},
            'lights': lights, 'missing_textures': missing, 'warnings': warnings}
    (out / 'zone.json').write_text(json.dumps(zone, indent=1), encoding='utf-8')
    print(f"{zone['name']} (city): {len(models)} chunk models, {len(lights)} lights, {len(exits)} exits, "
          f"{len(tex_names)} textures ({len(missing)} missing: {missing[:8]})")


def gltf_triangles(path):
    """[n, 3, 3] float64 triangles (metres, model space) of a converted model's drawn meshes."""
    import base64
    g = json.loads(Path(path).read_text())
    if not g.get('buffers'):
        return np.zeros((0, 3, 3))
    uri = g['buffers'][0]['uri']
    data = base64.b64decode(uri.split(',', 1)[1]) if uri.startswith('data:') else (Path(path).parent / uri).read_bytes()

    def acc(i):
        a = g['accessors'][i]
        bv = g['bufferViews'][a['bufferView']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        dt = {5126: np.float32, 5123: np.uint16, 5125: np.uint32}[a['componentType']]
        n = a['count'] * (3 if a['type'] == 'VEC3' else 1)
        return np.frombuffer(data, dtype=dt, count=n, offset=off)
    out = []
    for nd in g['nodes']:
        if 'mesh' not in nd or nd.get('name', '').startswith('collision'):
            continue
        for prim in g['meshes'][nd['mesh']]['primitives']:
            pos = acc(prim['attributes']['POSITION']).reshape(-1, 3).astype(np.float64) + np.array(nd.get('translation', [0, 0, 0]))
            out.append(pos[acc(prim['indices']).reshape(-1, 3)])
    return np.concatenate(out) if out else np.zeros((0, 3, 3))


def city_heights(out, mdir, models, origin, rot_deg):
    n, size = 513, 65536 * M
    step = size / (n - 1)
    grid = np.full((n, n), -1e9)
    c, s = np.cos(np.radians(rot_deg)), np.sin(np.radians(rot_deg))
    for m in models:
        t = gltf_triangles(out / m['file'])
        if len(t) == 0:
            continue
        x, y, z = t[..., 0], t[..., 1], t[..., 2]
        # the placement: Basis(UP, yaw) - x' = x cos + z sin, z' = -x sin + z cos - then the origin
        wx = x * c + z * s + origin[0] * M
        wz = -x * s + z * c + origin[1] * M
        wy = y + origin[2] * M
        for i in range(len(t)):
            x0, x1 = wx[i].min(), wx[i].max()
            z0, z1 = wz[i].min(), wz[i].max()
            gx = np.arange(int(np.ceil(x0 / step)), int(np.floor(x1 / step)) + 1)
            gz = np.arange(int(np.ceil(z0 / step)), int(np.floor(z1 / step)) + 1)
            if len(gx) == 0 or len(gz) == 0:
                continue
            PX, PZ = np.meshgrid(gx * step, gz * step)
            ax, az, bx, bz, cx, cz = wx[i, 0], wz[i, 0], wx[i, 1], wz[i, 1], wx[i, 2], wz[i, 2]
            d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
            if abs(d) < 1e-9:
                continue
            l1 = ((bz - cz) * (PX - cx) + (cx - bx) * (PZ - cz)) / d
            l2 = ((cz - az) * (PX - cx) + (ax - cx) * (PZ - cz)) / d
            l3 = 1 - l1 - l2
            inside = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
            h = l1 * wy[i, 0] + l2 * wy[i, 1] + l3 * wy[i, 2]
            for (r, k) in zip(*np.nonzero(inside)):
                zi, xi = gz[r], gx[k]
                if 0 <= zi < n and 0 <= xi < n and h[r, k] > grid[zi, xi]:
                    grid[zi, xi] = h[r, k]
    known = grid > -1e8
    grid[~known] = grid[known].min() if known.any() else 0.0
    grid.astype('<f4').tofile(out / 'heights_2x.f32')


# the old DB sends some exits to a second copy of a dungeon (the client ships both with identical dat files: same
# map, same coordinates) - the copy that is built: Hall of the Corrupt 277 -> 245, Marfach Caverns 276 -> 247
ZONE_COPIES = {277: 245, 276: 247}
HOUSING_ZONES = {13, 114, 213}                         # Caerwent / Erikstaad / Meath housing (post-1.65)


def client_zonejump(zdir, zone_id):
    """The zone's zonejump.csv as the client has it: its csvNNN.mpk copy when there is one (the patched tables - owner
    2026-10-17: Delling Crater's Tuscaran Glacier entrance and Abermenai's three realm exits are only there, so the
    zone-in spots were missing), else None (datNNN.mpk's own)."""
    p = zdir / f'csv{zone_id:03d}.mpk'
    if not p.exists():
        return None
    try:
        return read_mpk(str(p)).get('zonejump.csv')
    except ValueError:                                     # a few csv archives aren't MPAK (zone009): the dat copy
        return None


def zone_exits(dat):
    """The zone's exits: zonejump.csv rows (id, name, x1, y1, x2, y2, z1, z2, target) are boxes in zone units whose
    target is the old server's ZonePoint id (checked: Tir na Nog 19 = 'TNN > LOUGH DERG HIB', Lough Derg 13 =
    'LOUGH DERG > TNN HIB'); ZonePoint gives the arrival point (region X / Y / Z, heading), resolved here to the
    zones.dat zone that holds it. Read-only. Rows come in pairs (both corner orders) - kept once."""
    import sqlite3
    con = sqlite3.connect(f'file:{OLD_DB.as_posix()}?mode=ro', uri=True) if OLD_DB.exists() else None
    out, seen = [], set()
    for r in read_csv(dat.get('zonejump.csv', b'')):
        if len(r) < 9 or not r[0].strip().isdigit():
            continue
        x1, y1, x2, y2, z1, z2 = (float(r[k] or 0) for k in range(2, 8))
        target = int(float(r[8] or 0))                     # an empty field reads as 0 (atoi): an exit to nowhere
        key = (min(x1, x2), min(y1, y2), max(x1, x2), max(y1, y2), target)
        if key in seen:
            continue
        seen.add(key)
        # the client (game.dll 0x5802..: one 'jump%03d' quad per row in gspZoneJumpCollisionGroup) makes a vertical
        # quad from (x1, y1) to (x2, y2), z1..z2: the jump fires when the player's collider touches it - 'a' / 'b'
        # keep the real ends (min / max alone mirror a diagonal exit)
        e = {'name': r[1].strip(), 'min': [key[0] * M, min(z1, z2) * M, key[1] * M],
             'max': [key[2] * M, max(z1, z2) * M, key[3] * M], 'a': [x1 * M, y1 * M], 'b': [x2 * M, y2 * M],
             'target': target}
        row = con.execute('select TargetX, TargetY, TargetZ, TargetRegion, TargetHeading, ZonePoint_ID from ZonePoint '
                          'where Id=?', (target,)).fetchone() if con else None
        if row:
            tx, ty, tz, region, heading, label = row
            zone = zone_of(region, tx, ty)
            if zone and zone[0] in HOUSING_ZONES:
                continue                                   # player housing came after 1.65: no way in
            if zone:
                zid, ox, oy = zone
                zid = ZONE_COPIES.get(zid, zid)
                e.update({'to_zone': zid, 'to_pos': [(tx - ox) * M, tz * M, (ty - oy) * M],
                          'to_face': (180.0 + (heading or 0) * 360.0 / 4096.0) % 360.0, 'label': label})
        out.append(e)
    if con:
        con.close()
    return out


def zone_of(region, x, y):
    """(zone id, region offset x units, offset y units) of the zones.dat zone in region holding the point, or None."""
    text = read_mpk(str(CLIENT / 'zones/zones.mpk'))['zones.dat'].decode('latin1')
    for m in re.finditer(r'\[zone(\d+)\]([^\[]*)', text):
        d = dict(re.findall(r'^(\w+)=(.*)$', m.group(2), re.M))
        r = d.get('region', '').strip()
        if not r.isdigit() or int(r) != int(region):              # zones.dat writes some as '060'
            continue
        ox = int(d.get('region_offset_x', '0').strip() or 0) * 8192
        oy = int(d.get('region_offset_y', '0').strip() or 0) * 8192
        w = int(d.get('width', '8').strip() or 8) * 8192
        h = int(d.get('height', '8').strip() or 8) * 8192
        if ox <= x < ox + w and oy <= y < oy + h:
            return int(m.group(1)), ox, oy
    return None


def entrance_spots(zone_id):
    """Spots at every ZonePoint arrival inside the zone (the old DB, read only): name = its label, start = the first."""
    import sqlite3
    if not OLD_DB.exists():
        return []
    sec = dict(l.split('=', 1) for l in zone_section(zone_id).splitlines() if '=' in l)
    sec = {k.strip().lower(): v.strip() for k, v in sec.items()}
    region = int(sec.get('region', zone_id))
    ox, oy = int(sec.get('region_offset_x', 0)) * 8192, int(sec.get('region_offset_y', 0)) * 8192
    w, h = int(sec.get('width', 8)) * 8192, int(sec.get('height', 8)) * 8192
    con = sqlite3.connect(f'file:{OLD_DB.as_posix()}?mode=ro', uri=True)
    out, seen = [], set()
    for tx, ty, tz, heading, label in con.execute('select TargetX, TargetY, TargetZ, TargetHeading, ZonePoint_ID from '
                                                   'ZonePoint where TargetRegion=? order by Id', (region,)):
        if not (ox <= tx < ox + w and oy <= ty < oy + h) or (tx, ty) in seen:
            continue
        seen.add((tx, ty))
        out.append({'name': str(label), 'pos': [(tx - ox) * M, tz * M, (ty - oy) * M],
                    'face': (180.0 + (heading or 0) * 360.0 / 4096.0) % 360.0, 'start': not out})
    con.close()
    return out


def dungeon_fog(sector):
    """sector.dat [fog]: colour, clip (units -> m), amount (0-100)."""
    return {'color': [sector.getint('fog', c, fallback=0) / 255.0 for c in ('red', 'green', 'blue')],
            'clip_m': sector.getfloat('fog', 'clip', fallback=16000) * M, 'amount': sector.getint('fog', 'amount', fallback=0)}


def zone_lights(dat):
    """lights.csv rows are x, y, z (absolute height), light type; zones/textures/LIGHTDEF.CSV defines each type (id,
    name, r, g, b, unknown 10, radius in units, enabled), e.g. 6 = purple, 9 = fire-light, 1x = BIG, 2x = GIANT."""
    lights, lightdef = [], {}
    ldef_path = next((p for p in (CLIENT / 'zones/textures').iterdir() if p.name.lower() == 'lightdef.csv'), None)
    if ldef_path is not None:
        for r in read_csv(ldef_path.read_bytes()):
            if len(r) > 6 and r[0].strip().isdigit():
                lightdef[int(r[0])] = {'name': r[1].strip(), 'color': [float(r[k]) / 255.0 for k in (2, 3, 4)],
                                       'radius_m': float(r[6]) * M, 'enabled': r[7].strip() != '0' if len(r) > 7 else True}
    for r in read_csv(dat.get('lights.csv', b'')):
        if len(r) >= 4 and r[0].strip().lstrip('-').isdigit():
            lx, ly, lz, kind = float(r[0]), float(r[1]), float(r[2]), int(float(r[3]))
            d = lightdef.get(kind)
            if d is None or not d['enabled']:
                continue
            lights.append({'pos': [lx * M, lz * M, ly * M], 'type': kind, 'name': d['name'], 'color': d['color'],
                           'range_m': d['radius_m']})
    return lights


def build(zone_id, upscale=True):
    zdir = CLIENT / f'zones/zone{zone_id:03d}'
    if not zdir.exists():                                  # the Albion epic dungeons (233, 244) ship in frontiers/zones
        zdir = CLIENT / f'frontiers/zones/zone{zone_id:03d}'
    out = ROOT / f'godot/content/zones/zone{zone_id:03d}'
    out.mkdir(parents=True, exist_ok=True)
    (ROOT / 'godot/content/.gdignore').touch()             # runtime-loaded content: keep it out of the editor import
    dat = read_mpk(str(zdir / f'dat{zone_id:03d}.mpk'))
    jump = client_zonejump(zdir, zone_id)
    if jump is not None:
        dat['zonejump.csv'] = jump
    sector = sector_ini(dat['sector.dat'])
    if 'city.csv' in dat:
        return build_city(zone_id, upscale, zdir, out, dat, sector)
    if 'dungeon.place' in dat:
        return build_dungeon(zone_id, upscale, zdir, out, dat, sector)

    # terrain
    h = heights(dat, sector)
    (h * M).astype('<f4').tofile(out / 'heights.f32')
    hi = catmull_rom_2x(h)                                 # 513 x 513, 3.2 m spacing, used for the rendered ground
    (hi * M).astype('<f4').tofile(out / 'heights_2x.f32')
    water = np.asarray(Image.open(io.BytesIO(dat['water.pcx'])), dtype=np.uint8)
    Image.fromarray(water).save(out / 'water.png')
    # ground colour: the 8 x 8 tiles are stitched into one image per set and upscaled in one pass, so there are no
    # tile edges for the upscaler or the mipmaps to break (per-tile processing left visible seams)
    tile_sets = {}
    gdir = out / 'terrain' / 'original'
    gdir.mkdir(parents=True, exist_ok=True)
    for set_name, archive, prefix, ext in (('tex165', f'tex{zone_id:03d}.mpk', 'tex', 'bmp'),
                                           ('lod', f'lod{zone_id:03d}.mpk', 'lod', 'dds')):
        if not (zdir / archive).exists():
            continue
        tiles = read_mpk(str(zdir / archive))
        if f'{prefix}00-00.{ext}' not in tiles:                # some zones ship the painted ground as .dds
            ext = next((k.rsplit('.', 1)[1] for k in tiles if k.startswith(f'{prefix}00-00.')), ext)
        first = Image.open(io.BytesIO(tiles[f'{prefix}00-00.{ext}']))
        tw = first.width
        mosaic = Image.new('RGB', (tw * 8, tw * 8))
        for x in range(8):
            for y in range(8):
                raw = tiles.get(f'{prefix}{x:02d}-{y:02d}.{ext}')
                if raw is not None:
                    mosaic.paste(Image.open(io.BytesIO(raw)).convert('RGB').resize((tw, tw)), (x * tw, y * tw))
        save_if_changed(mosaic, gdir / f'{set_name}.png')
        tile_sets[set_name] = f'terrain/original/{set_name}.png'
        if upscale or (out / 'terrain' / 'x4plus' / f'{set_name}.png').exists():   # --no-upscale keeps earlier ones
            tile_sets[f'{set_name}_x4plus'] = f'terrain/x4plus/{set_name}.png'
        print('terrain', set_name, mosaic.size)
    if upscale:
        upscale_dir(gdir, out / 'terrain' / 'x4plus')
    for stale in ('tex165', 'lod', 'tex165_x4plus', 'lod_x4plus'):   # per-tile folders from the first build
        if (out / 'terrain' / stale).is_dir():
            shutil.rmtree(out / 'terrain' / stale)

    # Catacombs-era ground blend (what the current client draws): per patch, a base texture and up to 8 layers laid
    # over it by weight maps, ending with the zone colour map (see terrain_splat.py; verified against the lod bake)
    splat = None
    if (zdir / f'ter{zone_id:03d}.mpk').exists():
        import terrain_splat
        sdir = out / 'splat'
        sdir.mkdir(exist_ok=True)
        splat = {'patches': {}}
        for (px, py), layers in sorted(terrain_splat.decode(zone_id).items()):
            if len(layers) > 9:
                print(f'WARNING patch {px},{py}: {len(layers)} ground layers, engine draws base + 8')
            over = layers[1:9]
            files = []
            for k in range(0, 8, 4):
                chans = [over[i]['weight'] if i < len(over) else np.zeros((128, 128), np.float32) for i in range(k, k + 4)]
                img = Image.fromarray((np.clip(np.stack(chans, axis=2), 0, 1) * 255).astype(np.uint8), 'RGBA')
                name = f'patch_{px:02d}_{py:02d}_w{k // 4}.png'
                save_if_changed(img, sdir / name)
                files.append(f'splat/{name}')
            splat['patches'][f'{px}_{py}'] = {'layers': [{'tex': l['tex'], 'tiles': l['tiles'], 'tileable': l['tileable']}
                                                         for l in layers[:9]], 'weights': files}
        splat_textures = {l['tex'] for pch in splat['patches'].values() for l in pch['layers']}
        print('ground blend:', len(splat['patches']), 'patches,', len(splat_textures), 'ground textures')

    # ground clutter (grass tufts, flowers, pebbles): zones/textures/grass_<realm>[exp].csv + its sprite sheet,
    # placed by grassmap.pcx (value / 10 = group) and densemap.pcx (0-255 density per 256-unit cell)
    clutter = None
    realm, expansion = clutter_realm(zone_id)
    grass_name = f'grass_{realm}{"exp" if expansion else ""}' if realm else None
    grass_csv = CLIENT / 'zones/textures' / f'{grass_name}.csv' if grass_name else None
    if grass_csv and grass_csv.exists() and 'grassmap.pcx' in dat:
        items = clutter_items(grass_csv)
        atlas = Image.open(CLIENT / 'zones/textures' / f'{grass_name}.dds')
        Image.open(io.BytesIO(dat['grassmap.pcx'])).convert('L').save(out / 'grassmap.png')
        Image.open(io.BytesIO(dat['densemap.pcx'])).convert('L').save(out / 'densemap.png')
        clutter = {'atlas': grass_name, 'atlas_size': list(atlas.size), 'items': items,
                   'grassmap': 'grassmap.png', 'densemap': 'densemap.png'}
        print('ground clutter:', grass_name, len(items), 'kinds, atlas', atlas.size)

    # water surface maps the client draws water with: app/water/bump3.tga is an environment-bump (du/dv) map that
    # wobbles the reflected sky; a sky cloud layer stands in for the reflected sky. Data maps: never upscaled.
    raw = out / 'textures' / 'raw'
    raw.mkdir(parents=True, exist_ok=True)
    water_maps = {}
    for key, src, name in (('bump', CLIENT / 'water/bump3.tga', 'water_bump3'),
                           ('env', CLIENT / 'zones/sky/cloudsA.dds', 'sky_clouds_a')):
        if src.exists():
            save_if_changed(Image.open(src).convert('RGB'), raw / f'{name}.png')
            water_maps[key] = f'textures/raw/{name}.png'

    # water bodies (river00.. in sector.dat; their index is the id used in water.pcx)
    bodies = []
    i = 0
    while sector.has_section(f'river{i:02d}'):
        s = sector[f'river{i:02d}']
        tex = s.get('texture', '').strip().rsplit('.', 1)[0].lower()
        bodies.append({'id': i, 'name': s.get('name', ''), 'height': float(s.get('height', 0) or 0) * M,   # an empty height reads as 0 (atof)
                       'type': s.get('type', ''), 'cells': int((water == i).sum()), 'texture': tex,
                       'base_color': water_base_color(s),   # Shrouded Isles water: no texture, this colour (ARGB hex)
                       'flow': float(s.get('flow', 0) or 0)})
        i += 1

    # models and fixtures
    nifs = {int(r[0]): r for r in read_csv(dat['nifs.csv']) if r and r[0].strip().isdigit()}
    fixtures = [r for r in read_csv(dat['fixtures.csv'])[2:] if len(r) > 11 and r[1].strip().isdigit()]
    npk_files = {p.name.lower(): p for p in (CLIENT / 'zones/Nifs').iterdir()}
    mdir = out / 'models'
    mdir.mkdir(exist_ok=True)
    tex_names, models, model_index = set(), [], {}
    for nif_id in sorted({int(r[1]) for r in fixtures}):
        filename = nifs[nif_id][2].strip()
        base = filename.rsplit('.', 1)[0].lower()
        npk = npk_files.get(base + '.npk')
        if npk is None:
            print('missing model', nif_id, filename)
            continue
        raw = next(v for k, v in read_mpk(str(npk)).items() if k.endswith('.nif'))
        try:
            info = nif_static.convert(raw, str(mdir / f'{base}.gltf'), '../textures/original')
        except Exception as e:                             # pre-3.3 NIFs are not read yet
            print('model failed', filename, type(e).__name__, e)
            continue
        tex_names |= info['textures']
        model_index[nif_id] = len(models)
        models.append({'nif': nif_id, 'name': nifs[nif_id][1].strip(), 'file': f'models/{base}.gltf',
                       'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                       'ambient_only': ambient_only(nifs[nif_id]),
                       'warnings': [f'{base}: {w}' for w in info['warnings']]})
    # server prop objects -> world objects (models from app/items)
    item_files = {p.name.lower(): p for p in (CLIENT / 'items').iterdir()}
    wobj_models = {}
    wobjs = world_objects(zone_id)
    effect_files = {p.name.lower(): p for p in (CLIENT / 'effects').iterdir()}
    effect_models = {}
    for *_, effs in wobjs:                                 # attached effects (campfire flames, smoke, embers...)
        for eff_name, _attach, rng in effs:
            base = eff_name.lower()
            src = effect_files.get(base + '.nif')
            if base in effect_models or src is None:
                if src is None:
                    print('missing effect', eff_name)
                continue
            info = nif_static.convert(src.read_bytes(), str(mdir / f'effect_{base}.gltf'), '../textures/original')
            tex_names |= info['textures']
            effect_models[base] = len(models)
            models.append({'nif': f'effects/{base}', 'name': f'effect {base}', 'file': f'models/effect_{base}.gltf',
                           'triangles': info['triangles'], 'collision_triangles': 0, 'particles': info['particles'],
                           'effect_range_m': rng * M, 'warnings': [f'{base}: {w}' for w in info['warnings']]})
    for name, nif_name, *_ in wobjs:
        base = nif_name.lower()
        if base in wobj_models or base + '.nif' not in item_files:
            if base + '.nif' not in item_files:
                print('missing item model', nif_name)
            continue
        try:
            info = nif_static.convert(item_files[base + '.nif'].read_bytes(), str(mdir / f'item_{base}.gltf'),
                                      '../textures/original')
        except Exception as e:
            print('model failed', nif_name, type(e).__name__, e)
            continue
        tex_names |= info['textures']
        wobj_models[base] = len(models)
        models.append({'nif': f'items/{base}', 'name': base, 'file': f'models/item_{base}.gltf',
                       'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                       'warnings': [f'{base}: {w}' for w in info['warnings']]})
    placements = []
    for name, nif_name, x, y, z, heading, effs in wobjs:
        for eff_name, attach, _rng in effs:               # at the object's attach node (wardnode etc.: model origin)
            if eff_name.lower() in effect_models:
                placements.append({'m': effect_models[eff_name.lower()], 'name': f'{name} effect', 'pos': [x * M, z * M, y * M],
                                   'yaw': -heading * 360.0 / 4096.0, 'scale': 1.0, 'collide': False,
                                   'no_collision': True, 'source': 'world_object_effect', 'attach': attach, 'id': -1})
        if nif_name.lower() in wobj_models:
            # DAoC heading 0..4095 is a full turn; same handedness as fixture angles (yaw = -degrees). Unverified:
            # check the bandit camp tent (21236,13221) against the owner's client screenshot.
            placements.append({'m': wobj_models[nif_name.lower()], 'name': name, 'pos': [x * M, z * M, y * M],
                               'yaw': -heading * 360.0 / 4096.0, 'scale': 1.0, 'collide': False,
                               'no_collision': True, 'source': 'world_object', 'id': -1})
    for r in fixtures:
        nif_id = int(r[1])
        if nif_id not in model_index:
            continue
        x, y, z, a, scale = (float(r[k] or 0) for k in (3, 4, 5, 6, 7))
        on_ground = r[11].strip() != '0'
        gy = height_at(hi, x, y, 128.0) if on_ground else z
        placements.append({'m': model_index[nif_id], 'name': r[2].strip(), 'pos': [x * M, gy * M, y * M],
                           'yaw': -a, 'scale': scale / 100.0, 'collide': r[8].strip() not in ('', '0'),
                           'radius': fixture_radius(r), 'id': int(r[0])})

    lights = zone_lights(dat)

    tex_names |= {b['texture'] for b in bodies if b['texture']}
    if splat:
        tex_names |= splat_textures
    if clutter:
        tex_names.add(clutter['atlas'])
    warnings = sorted({w for m in models for w in m.get('warnings', [])})

    # textures
    idx = TextureIndex()
    odir = out / 'textures/original'
    odir.mkdir(parents=True, exist_ok=True)
    missing = []
    for n in sorted(tex_names):
        doubled = n.endswith(('__d2', '__x2'))   # __x2: the raw detail map (detail_x2.py)                       # detail map: modulate-2x baked in (see nif_static)
        img = idx.image(n[:-4] if doubled else n) or nif_static.EMBEDDED.get(n[:-4] if doubled else n)   # (a NIF's own pixels)
        if img is None:
            missing.append(n)
            continue
        if doubled and n.endswith('__d2'):
            rgb = np.asarray(img.convert('RGB'), dtype=np.float32) * 2.0
            img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
        save_if_changed(img, odir / f'{n}.png')
    # close-up ground grain: the zone's most used tiling ground texture (ter<id>.mpk textures.csv), shaded in by
    # the terrain material at a few metres per repeat
    detail = None
    ter = zdir / f'ter{zone_id:03d}.mpk'
    if ter.exists():
        rows = read_csv(read_mpk(str(ter)).get('textures.csv', b''))[1:]
        counts = {}
        for r in rows:
            if len(r) > 10 and r[10].strip() == '1':
                counts[r[2].strip().lower()] = counts.get(r[2].strip().lower(), 0) + 1
        for name in sorted(counts, key=counts.get, reverse=True):
            img = idx.image(name)
            if img is not None:
                detail = f'terrain_detail_{name}'
                save_if_changed(img.convert('RGB'), odir / f'{detail}.png')
                break
    if upscale:
        upscale_dir(odir, out / 'textures/x4plus')

    jumps = {}
    for sec in sector.sections():
        if sec.startswith('jump') and sector.has_option(sec, 'x'):
            jx, jy = sector.getfloat(sec, 'x'), sector.getfloat(sec, 'y')
            jumps[sector.get(sec, 'name').strip()] = [jx * M, height_at(hi, jx, jy, 128.0) * M, jy * M]

    # designer teleport spots (pipeline/data/zone_spots.json), placed on the ground or at the water surface
    spots = []
    spots_file = HERE / 'data' / 'zone_spots.json'
    if spots_file.exists():
        for sp in json.loads(spots_file.read_text(encoding='utf-8')).get(str(zone_id), []):
            sx, sy = float(sp['x']), float(sp['y'])
            gy = height_at(hi, sx, sy, 128.0)
            cell = water[int(np.clip(sy / 256, 0, 255)), int(np.clip(sx / 256, 0, 255))]
            if cell != 255 and cell < len(bodies):
                gy = max(gy, bodies[cell]['height'] / M - 50)   # standing in water: start near the surface
            spots.append({'name': sp['name'], 'pos': [sx * M, gy * M, sy * M], 'face': float(sp.get('face', 0)),
                          'start': bool(sp.get('start', False))})
    if not spots:
        spots = entrance_spots(zone_id)                   # no designer spots: where the old ZonePoints bring you in

    def rgb(sec):
        return [sector.getint(sec, c, fallback=0) / 255.0 for c in ('red', 'green', 'blue')]

    zone = {'id': zone_id, 'name': zone_name(zone_id), 'size_m': 65536 * M, 'cell_m': 256 * M,
            'heights': 'heights.f32', 'heights_size': 257, 'heights_2x': 'heights_2x.f32', 'heights_2x_size': 513, 'water_mask': 'water.png', 'water': bodies,
            'terrain_sets': tile_sets,
            'fog': {'color': rgb('fog'), 'clip_m': sector.getfloat('fog', 'clip', fallback=16000) * M,
                    'amount': sector.getint('fog', 'amount', fallback=0)},
            'sky': rgb('sky'), 'models': models, 'fixtures': placements, 'jump_points': jumps, 'spots': spots,
            'exits': zone_exits(dat),
            'texture_sets': {'original': 'textures/original',
                             **({'x4plus': 'textures/x4plus'} if upscale or (out / 'textures/x4plus').exists() else {})},
            'terrain_detail': detail, 'ground_splat': splat, 'clutter': clutter, 'water_maps': water_maps, 'lights': lights, 'missing_textures': missing, 'warnings': warnings}
    (out / 'zone.json').write_text(json.dumps(zone, indent=1), encoding='utf-8')
    import stitch_zone_edges                                # the far edges from the neighbour zones (seamless borders)
    stitch_zone_edges.stitch([zone_id])
    for w in warnings:                                      # texture layers the engine can't draw yet: say so
        print('WARNING', w)
    print(f"{zone['name']}: {len(models)} models, {len(placements)} fixtures, {len(tex_names)} textures "
          f"({len(missing)} missing: {missing[:8]}), water {[(b['name'], b['cells']) for b in bodies]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('zone', type=int)
    ap.add_argument('--no-upscale', action='store_true')
    a = ap.parse_args()
    build(a.zone, not a.no_upscale)


if __name__ == '__main__':
    main()
