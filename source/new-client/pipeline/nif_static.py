"""Static DAoC NIF (any version netimmerse.py reads) -> glTF 2.0, for world objects, items, weapons and shields.

    python nif_static.py <file.nif | archive.npk> <out.gltf> [--textures-uri ../textures/original]

Walks the scene graph from the roots, applying each NiAVObject's transform, and exports every visible NiTriShape /
NiTriStrips with its base texture, alpha mode and vertex colours. NiLODNode keeps only its most detailed child;
particles, lights and cameras are skipped. Axes follow DAoC world space (X east, Y south, Z up) mapped to glTF /
Godot (x east, y up, z south) with the model's own Y flipped, so models and zone placements share one frame (a
fixture's yaw is minus its DAoC angle); DAoC units become metres (x0.025).
Every texture slot a shape uses (base, dark, detail, decal, glow, gloss) is exported with its own UV set: the sets
become TEXCOORD_0..3 and the full material lives in the glTF's extras.nif_materials (materials are named
"nifmat:<n>"), so the engine draws every layer and can swap texture sets. Layers that can't be drawn yet (bump /
environment maps) come back as warnings instead of disappearing.
Collision follows the client (game.dll 0x482ba5): EVERY node named "collidee" (any case) outside the door groups
holds wall geometry (find_collidees; until 2026-10-29 only the first, which lost 59 models' walls). The client hides that subtree and tests its triangles; it is exported as
hidden "collision_<n>" nodes, textured or not. Door leaves carry their own "collideeNN" nodes, which the door system
handles. Other untextured, unmapped shapes are hidden and are not walls.
"""
import argparse, base64, json, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from netimmerse import read_nif, spec

UNITS_TO_METRES = 0.025
# NIF model space is right-handed Z-up; DAoC's map frame has Y pointing south, so model Y is flipped on the way
# in. Checked against Lough Derg: signpost text reads correctly and docks run from the shore out over the water.
DAOC_TO_GODOT = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], dtype=np.float64)   # (x, y, z) -> (x, z, -y)
SKIP_TYPES = ('NiParticles', 'NiParticleSystem', 'NiCamera', 'NiLight', 'NiTextureEffect')


def local_matrix(b):
    """Row-vector 4x4 (p' = p @ M) from translation, rotation and scale."""
    m = np.identity(4)
    rot = b.get('rotation')
    if rot is not None:
        r = np.array([[rot[f'm{i}{j}'] for j in (1, 2, 3)] for i in (1, 2, 3)], dtype=np.float64)
        m[:3, :3] = r
    m[:3, :3] *= b.get('scale', 1.0)
    t = b.get('translation')
    if t is not None:
        m[3, :3] = [t['x'], t['y'], t['z']]
    return m


DOOR_LEAF = __import__('re').compile(r'(?i)^door\s*[lr]?\s*\d')
DOOR_GROUP = __import__('re').compile(r'(?i)^door\s*\d')


def is_door_leaf(b, in_group=False):
    """A door leaf: a node with its open animation (NiKeyframeController) that is either named like a leaf ('door
    L01', 'door R01', 'door01') or sits inside a 'doorNNN' group (elftavern2: 'door001' > animated 'collidee' +
    'door visible'). Owner 2026-10-09: doors open on click as in the client - never bake them into the static mesh
    (Mag Mell's were)."""
    if b.type != 'NiNode' or not (in_group or DOOR_LEAF.match(b.get('name') or '')):
        return False
    c = b.get('controller')
    while c is not None:
        if c.type == 'NiKeyframeController':
            return True
        c = c.get('next_controller')
    return False


def quat_row_matrix(q):
    """Row-vector 3x3 of a NIF quaternion key (keys are column-vector rotations; row form = transpose)."""
    x, y, z, w = q
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    return r.T


def door_spec(leaf, parent_m, m_closed, index):
    """{id, name, closed, open, seconds}: the leaf's model-space pose (Godot axes, metres, 4x3 basis + origin) when
    closed and when open (its keyframe controller's first / last rotation key around the leaf's own pivot, and its
    first / last translation key: a portcullis only slides - owner 2026-10-20, Snowdonia Fortress's gates stayed shut
    in the new client while the old one raised them)."""
    c = leaf.get('controller')
    while c is not None and c.type != 'NiKeyframeController':
        c = c.get('next_controller')
    kd = c.get('data') if c is not None else None
    keys = (kd.get('quaternion_keys') or []) if kd is not None else []
    tkeys = ((kd.get('translations') or {}).get('keys') or []) if kd is not None else []
    local = local_matrix(leaf)
    scale = float(leaf.get('scale', 1.0))

    def pose(q, t=None):
        m = local.copy()
        if q is not None:
            m[:3, :3] = quat_row_matrix(q) * scale
        if t is not None:
            m[3, :3] = [t['x'], t['y'], t['z']]
        return m @ parent_m

    def godot(m):
        a = m[:3, :3].T                                  # column form
        basis = DAOC_TO_GODOT @ a @ DAOC_TO_GODOT.T
        origin = DAOC_TO_GODOT @ m[3, :3] * UNITS_TO_METRES
        return [round(float(v), 6) for v in list(basis.T.reshape(-1)) + list(origin)]

    q0 = q1 = None
    if keys:
        q0 = [keys[0]['value'][k] for k in ('x', 'y', 'z', 'w')]
        q1 = [keys[-1]['value'][k] for k in ('x', 'y', 'z', 'w')]
    t0 = tkeys[0]['value'] if tkeys else None
    t1 = tkeys[-1]['value'] if tkeys else None
    return {'id': index, 'name': leaf.get('name'), 'closed': godot(pose(q0, t0) if q0 or t0 else m_closed),
            'open': godot(pose(q1, t1) if q1 or t1 else m_closed),
            'seconds': float(c.get('stop_time', 1.0)) - float(c.get('start_time', 0.0)) if c is not None else 1.0}


def props_of(b):
    out = {}
    for p in b.get('properties') or []:
        if p is not None:
            out[p.type] = p
    return out


def triangles_of(data):
    if data.type == 'NiTriShapeData':
        tri = np.asarray(data.get('triangles'), dtype=np.int64).reshape(-1, 3) if data.get('has_triangles', True) \
            and data.get('triangles') is not None else np.zeros((0, 3), np.int64)
        return tri
    tris = []                                              # NiTriStripsData: alternate winding along each strip
    for strip in data.get('points') or []:
        s = list(np.asarray(strip).tolist())
        for i in range(len(s) - 2):
            a, b, c = s[i], s[i + 1], s[i + 2]
            if a == b or b == c or a == c:
                continue
            tris.append((a, b, c) if i % 2 == 0 else (a, c, b))
    return np.array(tris, dtype=np.int64).reshape(-1, 3)


def texture_slot(tex_prop, slot='base_texture'):
    """(texture basename, UV set) of one NiTexturingProperty slot, or (None, 0)."""
    name = texture_name(tex_prop, slot)
    return name, int(tex_prop[slot].get('uv_set') or 0) if name else 0


EMBEDDED = {}          # texture name -> PIL image, for textures stored inside a NIF (build steps save them)


def embedded_image(src):
    """A NiSourceTexture's own pixels (use_external 0 -> NiPixelData): 24-bit RGB / 32-bit RGBA with channel masks,
    the first mipmap. Owner 2026-10-10: Albion red sorcery's hand glow had no texture file and drew white squares."""
    import hashlib
    from PIL import Image
    pd = src.get('pixel_data')
    if pd is None or int(pd.get('pixel_format', -1)) not in (0, 1, 2):
        return None, None
    raw = pd.get('pixel_data')
    raw = raw[0] if isinstance(raw, list) else raw
    data = np.asarray(raw, dtype=np.uint8).tobytes()
    w, h, off = [int(x) for x in pd['mipmaps'][0]]
    if int(pd.get('pixel_format', -1)) == 2:
        # 8-bit paletted (NiPalette, 256 RGBA entries) - Tir na Nog's tir24 building keeps its stone this way
        # (owner 2026-10-14: the wall drew white, its textures skipped)
        pal = pd.get('palette')
        entries = np.asarray(pal.get('palette'), dtype=np.uint8).reshape(-1, 4) if pal is not None else None
        if entries is None or not len(entries):
            return None, None
        idx = np.frombuffer(data[off:off + w * h], dtype=np.uint8).reshape(h, w)
        rgba = entries[np.minimum(idx, len(entries) - 1)]
        if not int(pal.get('unknown_byte') or 0) and rgba[..., 3].max() == 0:
            rgba[..., 3] = 255                             # a palette without alpha: opaque
        name = 'nifpix_' + hashlib.md5(data[off:off + w * h]).hexdigest()[:12]
        return name, Image.fromarray(np.ascontiguousarray(rgba), 'RGBA')
    bpp = int(pd.get('bytes_per_pixel') or int(pd.get('bits_per_pixel', 32)) // 8)
    px = np.frombuffer(data[off:off + w * h * bpp], dtype=np.uint8).reshape(h, w, bpp).astype(np.uint32)
    word = sum(px[..., i] << (8 * i) for i in range(bpp))

    def chan(mask, default=255):
        mask = int(mask or 0)
        if not mask:
            return np.full((h, w), default, np.uint8)
        shift = (mask & -mask).bit_length() - 1
        return ((word & mask) >> shift).astype(np.uint8)
    rgba = np.dstack([chan(pd.get('red_mask')), chan(pd.get('green_mask')), chan(pd.get('blue_mask')),
                      chan(pd.get('alpha_mask')) if bpp == 4 else np.full((h, w), 255, np.uint8)])
    name = 'nifpix_' + hashlib.md5(data[off:off + w * h * bpp]).hexdigest()[:12]
    return name, Image.fromarray(rgba, 'RGBA')


def texture_name(tex_prop, slot='base_texture'):
    if tex_prop is None or not tex_prop.get('has_' + slot):
        return None
    src = tex_prop[slot].get('source')
    if src is None:
        return None
    if src.get('use_external', 1):
        name = src.get('file_name') or ''
    else:
        name = src.get('original_file_name') or ''
        if not name:
            ename, img = embedded_image(src)
            if ename:
                EMBEDDED[ename] = img
                return ename
    name = name.replace('\\', '/').split('/')[-1]
    return name.rsplit('.', 1)[0].lower() if name else None


def alpha_mode(alpha_prop):
    if alpha_prop is None:
        return 'opaque', 0.5
    flags = alpha_prop.get('flags', 0)
    thresh = alpha_prop.get('threshold', 0) / 255.0
    if flags & 0x1 and (flags >> 5) & 15 in (0, 8):     # blend with destination ONE / DESTALPHA: additive glow
        return 'add', 0.0
    if flags & 0x1 and (flags >> 5) & 15 == 3:          # destination INVSRCCOLOR: screen (black adds nothing; the
        return 'screen', 0.0                               # Midgard snow drifts are black-backed, alpha-less clouds)
    if flags & 0x200:                                      # alpha test
        return 'mask', max(thresh, 0.05)
    if flags & 0x1:                                        # alpha blend: foliage and decals read best as cutouts
        return 'mask', 0.5
    return 'opaque', 0.5


def find_collidee(nif):
    """(node, parent world matrix) of the first node named 'collidee' (case-insensitive), depth-first like the
    client's lookup, or (None, None)."""
    def dfs(b, parent_m):
        if b is None:
            return None
        if (b.get('name') or '').lower() == 'collidee':
            return b, parent_m
        m = local_matrix(b) @ parent_m
        for c in b.get('children') or []:
            hit = dfs(c, m)
            if hit:
                return hit
        return None
    for r in nif.roots:
        hit = dfs(r, np.identity(4))
        if hit:
            return hit
    # city collision patches (tirNN_p00.nif): 'collisionswitch' > '__patch_collidee' > 'collider ...' - the whole
    # file is extra wall geometry for its city chunk
    def dfs_patch(b, parent_m):
        if b is None:
            return None
        if (b.get('name') or '').lower() == '__patch_collidee':
            return b, parent_m
        m = local_matrix(b) @ parent_m
        for c in b.get('children') or []:
            hit = dfs_patch(c, m)
            if hit:
                return hit
        return None
    for r in nif.roots:
        hit = dfs_patch(r, np.identity(4))
        if hit:
            return hit
    return None, None


def find_collidees(nif):
    """[(node, parent world matrix)] of every wall node: each node named exactly 'collidee' (any case), depth-first,
    outside the door groups and leaves (their collision is the door system's). The client marks every such node
    triangle-tested and hides it (game.dll 0x482ba5 from 0x478749: an 8-character case-insensitive compare with
    "collidee" on every node, recursing into all children) - not just the first: Caer Ulfwych's tower5 has the
    ladder's 'climb001/collidee' first and its walls in 'collisionswitch/collidee' (owner 2026-10-29: "no collision at
    all i can just walk straight thru its walls"; 59 client NIFs had their walls after another collidee). A NIF whose
    only collidee nodes are in doors keeps the first one, as before (and the city patches' '__patch_collidee')."""
    out = []

    def dfs(b, parent_m, in_door):
        if b is None:
            return
        name = (b.get('name') or '')
        door = in_door or bool(DOOR_GROUP.match(name) or DOOR_LEAF.match(name))
        if name.lower() == 'collidee' and not door:
            out.append((b, parent_m))
            return
        m = local_matrix(b) @ parent_m
        for c in b.get('children') or []:
            dfs(c, m, door)
    for r in nif.roots:
        dfs(r, np.identity(4), False)
    if out:
        return out
    node, m = find_collidee(nif)
    return [(node, m)] if node is not None else []


def climb_shapes(nif):
    """[(shape, data, model matrix)] under every node whose name starts with 'climb' (not 'sclimb': the keep wall
    climb points of the later Climb Walls ability). The client makes those nodes triangle-tested collision (game.dll
    0x482bf0, strncmp 'climb') and a wall contact with one starts climbing (0x464995 strstr 'climb' -> state 7,
    "You start climbing."): ladders - the guard tower's climb001-003 > 'collidee (ladder-low/mid/high)'."""
    sp = spec()
    out = []

    def grab(b, pm):
        if b is None or any(sp.is_a(b.type, t) for t in SKIP_TYPES):
            return
        m = local_matrix(b) @ pm
        if sp.is_a(b.type, 'NiTriBasedGeom'):
            data = b.get('data')
            if data is not None and data.get('num_vertices'):
                out.append((b, data, m))
            return
        for c in b.get('children') or []:
            grab(c, m)

    def walk(b, pm):
        if b is None:
            return
        if (b.get('name') or '').lower().startswith('climb'):
            grab(b, pm)
            return
        m = local_matrix(b) @ pm
        for c in b.get('children') or []:
            walk(c, m)
    for r in nif.roots:
        walk(r, np.identity(4))
    return out


def collidee_shapes(nif):
    """[(shape, data, model matrix)] under the collidee node: every shape, hidden or not (the client hides the whole
    subtree for drawing and tests it for collision)."""
    sp = spec()
    out = []

    def walk(b, pm):
        if b is None or any(sp.is_a(b.type, t) for t in SKIP_TYPES):
            return
        m = local_matrix(b) @ pm
        if sp.is_a(b.type, 'NiTriBasedGeom'):
            data = b.get('data')
            if data is not None and data.get('num_vertices'):
                out.append((b, data, m))
            return
        for c in b.get('children') or []:
            walk(c, m)
    for node, parent_m in find_collidees(nif):
        walk(node, parent_m)
    return out


def _controller(b, kind):
    """The block's first controller of a type (its controller chain), or None."""
    c = b.get('controller')
    seen = set()
    while c is not None and id(c) not in seen:
        seen.add(id(c))
        if c.type == kind:
            return c
        c = c.get('next_controller')
    return None


def _ctl_timing(c):
    """A controller's timing, as the engine plays it: scaled time = t x frequency + phase, then loop / reverse
    (ping-pong) / clamp (play once) within start..stop (flags bits 1-2)."""
    return {'start': float(c.get('start_time') or 0.0), 'stop': float(c.get('stop_time') or 0.0),
            'frequency': float(c.get('frequency') or 1.0), 'phase': float(c.get('phase') or 0.0),
            'cycle': ['loop', 'reverse', 'clamp', 'loop'][(int(c.get('flags') or 0) >> 1) & 3]}


def _key_value(v):
    if isinstance(v, dict):
        return np.array([float(v[k]) for k in ('x', 'y', 'z', 'w') if k in v], dtype=np.float64)
    return np.array([float(v)], dtype=np.float64)


def sample_keys(group_keys, interpolation, t):
    """A NIF key group at time t: linear, or quadratic (Hermite: the key's outgoing tangent is stored in 'backward',
    the next key's incoming one in 'forward', per key interval)."""
    if not group_keys:
        return None
    if t <= group_keys[0]['time']:
        return _key_value(group_keys[0]['value'])
    if t >= group_keys[-1]['time']:
        return _key_value(group_keys[-1]['value'])
    for k0, k1 in zip(group_keys, group_keys[1:]):
        if k0['time'] <= t <= k1['time']:
            u = (t - k0['time']) / max(k1['time'] - k0['time'], 1e-9)
            p0, p1 = _key_value(k0['value']), _key_value(k1['value'])
            if interpolation == 2 and 'backward' in k0 and 'forward' in k1:
                m0, m1 = _key_value(k0['backward']), _key_value(k1['forward'])
                h00, h10, h01, h11 = 2 * u ** 3 - 3 * u ** 2 + 1, u ** 3 - 2 * u ** 2 + u, -2 * u ** 3 + 3 * u ** 2, u ** 3 - u ** 2
                return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1
            return p0 + (p1 - p0) * u
    return _key_value(group_keys[-1]['value'])


def held_roll(raw):
    """A weapon's grip turned about its own length (the HELD node rotated about Z only: 90 degrees for 52 one-handed
    blunts incl. the Dragonslayer hammers, 180 for the Valewalker epic and the Dubh Sluagh scythes; owner 2026-10-16:
    "the hammer is held incorrectly (head down, spike up) and the scythe is completely upside down"): the client grips
    a weapon by that node, so it is drawn turned back by it. In held_frame's format with no offset (the grip point is
    left where the owner checked it), or None for none."""
    nif = read_nif(raw)
    found = []

    def walk(b, pm):
        if b is None or found:
            return
        m = local_matrix(b) @ pm
        if (b.get('name') or '').strip().lower() == 'held':
            found.append(m)
            return
        for c in b.get('children') or []:
            walk(c, m)
    for r in nif.roots:
        walk(r, np.identity(4))
    if not found:
        return None
    r = found[0][:3, :3] / max(float(np.cbrt(abs(np.linalg.det(found[0][:3, :3])))), 1e-9)
    if r[2, 2] < 0.98 or np.allclose(r, np.identity(3), atol=1e-3):
        return None                                        # not a roll about the length (within ~11 degrees), or none
    a = np.identity(4)
    # the grip's own turn, not its inverse (owner 2026-10-16, against the old client: with the inverse the Cairnbreaker's
    # flat face looked backwards - a 90 degree roll the wrong way round; 180 degree rolls are the same either way)
    a[:3, :3] = np.linalg.inv(r)
    return [round(float(x), 5) for x in (a[0, 0], a[1, 0], a[2, 0], a[0, 1], a[1, 1], a[2, 1], a[0, 2], a[1, 2], a[2, 2],
                                         0.0, 0.0, 0.0)]


def held_frame(raw):
    """A weapon NIF's HELD frame as the transform from the model to the hand (owner 2026-10-13: the Bonedancer
    commander's axe hung rolled 90 degrees on its hip): the client grips a weapon by its 'held' node, and a few
    NIFs carry their mesh turned under an 'item' node that the held node turns back (bonedancerax_1h / sword /
    hammer). Column-vector, NIF axes and units: [basis column x, y, z, origin] (12 numbers), or None when it's the
    plain identity / an offset only (nearly every weapon)."""
    nif = read_nif(raw)
    found, items = [], []

    def walk(b, pm):
        if b is None:
            return
        m = local_matrix(b) @ pm
        name = (b.get('name') or '').strip().lower()
        if name == 'held':
            found.append(m)
        elif name == 'item':
            items.append(m)
        for c in b.get('children') or []:
            walk(c, m)
    for r in nif.roots:
        walk(r, np.identity(4))
    # only that structure - the mesh's 'item' node turned and the grip turned with it. Shields and most weapons carry
    # other held turns / offsets that the way they are drawn already matches (the owner's checked look), left alone.
    if not found or not items or np.allclose(items[0][:3, :3], np.identity(3), atol=1e-3)             or not np.allclose(found[0][:3, :3], items[0][:3, :3], atol=1e-3):
        return None
    a = np.linalg.inv(found[0]).T                          # row-vector world -> hand, as a column-vector matrix
    return [round(float(x), 5) for x in (a[0, 0], a[1, 0], a[2, 0], a[0, 1], a[1, 1], a[2, 1], a[0, 2], a[1, 2], a[2, 2],
                                         a[0, 3], a[1, 3], a[2, 3])]


def path_position(pc, t):
    """A NiPathController's place at time t (the node's translation in its parent's space, NIF units): its float
    data says how far along the path it is (0..1) at t, its pos data is the path - keyed points with tangents, their
    times spread over the path - sampled at that fraction of the way. 164 of the client's effect NIFs move parts
    this way (owner 2026-10-13: the Shaman's orange poison mist 'pestilence' - its three emitters climb spiral paths
    from 1 m under the effect's origin; unmoved they stayed under the ground and nothing showed). None without a path."""
    pd, fd = pc.get('pos_data'), pc.get('float_data')
    pk = (pd.get('data') or {}) if pd is not None else {}
    keys = pk.get('keys') or []
    if not keys:
        return None
    fk = (fd.get('data') or {}) if fd is not None else {}
    pct = sample_keys(fk.get('keys'), int(fk.get('interpolation') or 1), t) if fk.get('keys') else None
    pct = min(max(float(pct[0]), 0.0), 1.0) if pct is not None else 0.0
    # the path's points are spaced by that fraction (0..1; Max's exporter adds a last copy at the controller's stop
    # time) - else, spread over their own times
    times = [float(k['time']) for k in keys]
    if len(times) > 1 and (times[-1] <= 1.0001 or times[-2] <= 1.0001):
        param = pct
    else:
        param = times[0] + pct * (times[-1] - times[0])
    return sample_keys(keys, int(pk.get('interpolation') or 1), param)[:3]


def _euler_animated(kd):
    """XYZ rotation keys (rotation type 4) that move: an axis with more than one key."""
    return kd is not None and int(kd.get('rotation_type') or 0) == 4 and \
        any(len(g.get('keys') or []) > 1 for g in (kd.get('xyz_rotations') or [])[:3])


def _euler_matrix(kd, t):
    """Rotation type 4 at time t as a row-vector 3x3 (owner 2026-10-15: the necromancer's Blades of Wrath saw blade
    didn't spin - sawblade1_hit turns its 'sawblade' node five times round Z on quadratic XYZ keys, and only
    quaternion keys were read). Each axis sampled on its own keys, composed Rz * Ry * Rx (nif_character.euler_track)."""
    ang = []
    for g in (kd.get('xyz_rotations') or [])[:3]:
        v = sample_keys(g.get('keys'), int(g.get('interpolation') or 1), t)
        ang.append(float(np.asarray(v, dtype=np.float64).reshape(-1)[0]) if v is not None else 0.0)
    ax, ay, az = (ang + [0.0, 0.0, 0.0])[:3]
    cx, sx, cy, sy, cz, sz = np.cos(ax / 2), np.sin(ax / 2), np.cos(ay / 2), np.sin(ay / 2), np.cos(az / 2), np.sin(az / 2)
    q = [sx * cy * cz - cx * sy * sz, cx * sy * cz + sx * cy * sz, cx * cy * sz - sx * sy * cz, cx * cy * cz + sx * sy * sz]
    return quat_row_matrix(q)


def _slerp_keys(keys, t):
    q = [np.array([k['value'][c] for c in ('x', 'y', 'z', 'w')], dtype=np.float64) for k in keys]
    if t <= keys[0]['time']:
        return q[0]
    if t >= keys[-1]['time']:
        return q[-1]
    for i in range(len(keys) - 1):
        if keys[i]['time'] <= t <= keys[i + 1]['time']:
            u = (t - keys[i]['time']) / max(keys[i + 1]['time'] - keys[i]['time'], 1e-9)
            a, b = q[i], q[i + 1]
            if np.dot(a, b) < 0:
                b = -b
            v = a * (1 - u) + b * u
            return v / max(np.linalg.norm(v), 1e-12)
    return q[-1]


def _rot_to_quat(r):
    """Column-vector 3x3 rotation -> quaternion (x, y, z, w)."""
    tr = r[0, 0] + r[1, 1] + r[2, 2]
    if tr > 0:
        s_ = np.sqrt(tr + 1.0) * 2
        return [(r[2, 1] - r[1, 2]) / s_, (r[0, 2] - r[2, 0]) / s_, (r[1, 0] - r[0, 1]) / s_, 0.25 * s_]
    i = int(np.argmax([r[0, 0], r[1, 1], r[2, 2]]))
    j, k = (i + 1) % 3, (i + 2) % 3
    s_ = np.sqrt(max(1.0 + r[i, i] - r[j, j] - r[k, k], 1e-12)) * 2
    q = [0.0, 0.0, 0.0, 0.0]
    q[i] = 0.25 * s_
    q[j] = (r[j, i] + r[i, j]) / s_
    q[k] = (r[k, i] + r[i, k]) / s_
    q[3] = (r[k, j] - r[j, k]) / s_
    return q


def node_animation(b, parent_m):
    """An animated node's timeline (owner 2026-10-10: Gleam Ray's ray is a hidden 'streak' node that a NiVisController
    shows for half a second while a NiKeyframeController stretches it): {visible (at rest), vis: [[t, 0|1]] + timing,
    frames: [[t, x, y, z, qx, qy, qz, qw, scale]] (model space, Godot axes, metres; 30 per second over the keyframe
    controller's span, or the one rest pose) + timing}."""
    out = {'visible': not (int(b.get('flags') or 0) & 1)}
    vc = _controller(b, 'NiVisController')
    if vc is not None and vc.get('data') is not None:
        out['vis'] = [[round(float(k['time']), 4), int(k['value'])] for k in (vc['data'].get('keys') or [])]
        out['vis_timing'] = _ctl_timing(vc)
    kc = _controller(b, 'NiKeyframeController')
    pc = _controller(b, 'NiPathController')              # a path the node travels (path_position)
    rest = local_matrix(b)
    rest_s = float(b.get('scale', 1.0))
    rest_r = rest[:3, :3] / max(rest_s, 1e-9)
    kd = kc.get('data') if kc is not None else None
    rot_keys = (kd.get('quaternion_keys') or []) if kd is not None and int(kd.get('rotation_type') or 0) in (1, 2, 3) else []
    tg = (kd.get('translations') or {}) if kd is not None else {}
    sg = (kd.get('scales') or {}) if kd is not None else {}
    timing = _ctl_timing(kc) if kc is not None else (_ctl_timing(pc) if pc is not None else
                                                     {'start': 0.0, 'stop': 0.0, 'frequency': 1.0, 'phase': 0.0,
                                                      'cycle': 'clamp'})
    if pc is not None and kc is not None:                # both: the span covering the two
        pt = _ctl_timing(pc)
        timing = dict(timing, start=min(timing['start'], pt['start']), stop=max(timing['stop'], pt['stop']))
    on_path = pc is not None and not tg.get('keys') and path_position(pc, 0.0) is not None
    euler = _euler_animated(kd)
    keyed = bool(rot_keys or euler or tg.get('keys') or sg.get('keys') or on_path)
    times = np.arange(timing['start'], timing['stop'] + 1e-6, 1 / 30.0) if keyed else [timing['start']]
    frames = []
    for t in times:
        r = quat_row_matrix(_slerp_keys(rot_keys, t)) if rot_keys else (_euler_matrix(kd, t) if euler else rest_r)
        tr = sample_keys(tg.get('keys'), int(tg.get('interpolation') or 1), t) if tg.get('keys') else \
            (path_position(pc, t) if on_path else rest[3, :3])
        sc = float(sample_keys(sg.get('keys'), int(sg.get('interpolation') or 1), t)[0]) if sg.get('keys') else rest_s
        local = np.identity(4)
        local[:3, :3] = r * sc
        local[3, :3] = tr[:3]
        w = local @ parent_m
        ws = float(np.cbrt(abs(np.linalg.det(w[:3, :3])))) or 1.0
        g_rot = DAOC_TO_GODOT @ (w[:3, :3] / ws).T @ DAOC_TO_GODOT.T
        pos = (DAOC_TO_GODOT @ w[3, :3]) * UNITS_TO_METRES
        frames.append([round(float(t), 4)] + [round(float(x), 5) for x in pos] +
                      [round(float(x), 5) for x in _rot_to_quat(g_rot)] + [round(ws, 5)])
    out['frames'] = frames
    out['timing'] = timing
    return out


def _animated(b):
    return any(_controller(b, t) is not None for t in ('NiKeyframeController', 'NiPathController', 'NiVisController'))


def _ctl_time(t, c):
    """Engine time -> a controller's own time (as NifScene._ctl): x frequency + phase, then loop / reverse / clamp."""
    start, stop = c['start'], c['stop']
    span = max(stop - start, 1e-4)
    u = t * c['frequency'] + c['phase']
    if c['cycle'] == 'clamp':
        return min(max(u, start), stop)
    if c['cycle'] == 'reverse':
        v = (u - start) % (span * 2)
        return start + (v if v <= span else span * 2 - v)
    return start + (u - start) % span


def _local_at(b, t):
    """A node's local matrix (row vectors, DAoC units) at engine time t: its keyframes / path on its own controller's
    time, else its rest pose (the per-node part of node_animation)."""
    kc = _controller(b, 'NiKeyframeController')
    pc = _controller(b, 'NiPathController')
    rest = local_matrix(b)
    if kc is None and pc is None:
        return rest
    rest_s = float(b.get('scale', 1.0))
    rest_r = rest[:3, :3] / max(rest_s, 1e-9)
    kd = kc.get('data') if kc is not None else None
    rot_keys = (kd.get('quaternion_keys') or []) if kd is not None and int(kd.get('rotation_type') or 0) in (1, 2, 3) else []
    tg = (kd.get('translations') or {}) if kd is not None else {}
    sg = (kd.get('scales') or {}) if kd is not None else {}
    tc = _ctl_time(t, _ctl_timing(kc if kc is not None else pc))
    on_path = pc is not None and not tg.get('keys') and path_position(pc, 0.0) is not None
    r = quat_row_matrix(_slerp_keys(rot_keys, tc)) if rot_keys else (_euler_matrix(kd, tc) if _euler_animated(kd) else rest_r)
    tr = sample_keys(tg.get('keys'), int(tg.get('interpolation') or 1), tc) if tg.get('keys') else \
        (path_position(pc, _ctl_time(t, _ctl_timing(pc))) if on_path else rest[3, :3])
    sc = float(sample_keys(sg.get('keys'), int(sg.get('interpolation') or 1), tc)[0]) if sg.get('keys') else rest_s
    local = np.identity(4)
    local[:3, :3] = r * sc
    local[3, :3] = tr[:3]
    return local


def chain_animation(chain, base_m):
    """The timeline of the last node of `chain` (the nodes from just below a frame of reference down to it) with EVERY
    animated node in it played on its own controller (2026-10-13: Arawn's eyes - the 'eyes' node moves the pair while
    each eyeball has its own keys; only the innermost one was played, and under a camera-facing pivot none). Frames
    relative to base_m (the frame of reference's rest matrix; identity = effect space): node_animation's format."""
    ctls = [c for n in chain for c in (_controller(n, 'NiKeyframeController'), _controller(n, 'NiPathController'))
            if c is not None]
    timings = [_ctl_timing(c) for c in ctls]
    start = min((tm['start'] for tm in timings), default=0.0)
    stop = max((tm['stop'] for tm in timings), default=0.0)
    times = np.arange(start, stop + 1e-6, 1 / 30.0) if ctls and stop > start else [start]
    frames = []
    for t in times:
        w = base_m
        for n in chain:
            w = _local_at(n, t) @ w
        ws = float(np.cbrt(abs(np.linalg.det(w[:3, :3])))) or 1.0
        g_rot = DAOC_TO_GODOT @ (w[:3, :3] / ws).T @ DAOC_TO_GODOT.T
        pos = (DAOC_TO_GODOT @ w[3, :3]) * UNITS_TO_METRES
        frames.append([round(float(t), 4)] + [round(float(x), 5) for x in pos] +
                      [round(float(x), 5) for x in _rot_to_quat(g_rot)] + [round(ws, 5)])
    out = {'visible': all(not (int(n.get('flags') or 0) & 1) or _controller(n, 'NiVisController') is not None
                          for n in chain),
           'frames': frames,
           'timing': {'start': float(start), 'stop': float(stop), 'frequency': 1.0, 'phase': 0.0,
                      'cycle': timings[-1]['cycle'] if timings else 'clamp'}}
    for n in reversed(chain):                             # the deepest visibility switch in the chain
        vc = _controller(n, 'NiVisController')
        if vc is not None and vc.get('data') is not None:
            out['visible'] = not (int(n.get('flags') or 0) & 1)
            out['vis'] = [[round(float(k['time']), 4), int(k['value'])] for k in (vc['data'].get('keys') or [])]
            out['vis_timing'] = _ctl_timing(vc)
            break
    return out


def environment_map(b):
    """The environment map a node's effect list puts on the shapes below it (NiTextureEffect, texture type 2 =
    environment map; coordinate generation 2 = sphere map): its texture name, else None. The shield spells, the
    Sluaghbinder's blood shield, potions, slime blobs and crystals (owner 2026-10-11: their glassy look was missing)."""
    for e in b.get('effects') or []:
        if e is None or e.type != 'NiTextureEffect' or int(e.get('texture_type') or 0) != 2:
            continue
        src = e.get('source_texture')
        name = (src.get('file_name') or '') if src is not None else ''
        name = name.replace('\\', '/').split('/')[-1]
        if name:
            return name.rsplit('.', 1)[0].lower()
    return None


# NiMaterialColorController's colour (NetImmerse 4.x keeps it in flags bits 4-5)
COLOR_TARGETS = {0: 'ambient', 1: 'diffuse', 2: 'specular', 3: 'emissive'}


def color_animations(mat):
    """The material's NiMaterialColorControllers (an effect's colour fading in / out - the hand glows, flares and
    halos keep their colour only there; owner 2026-10-11: drawn black): [{target, keys: [[t, r, g, b]] (30 per
    second) + timing}]."""
    out = []
    c = mat.get('controller') if mat is not None else None
    seen = set()
    while c is not None and id(c) not in seen:
        seen.add(id(c))
        if c.type == 'NiMaterialColorController' and c.get('data') is not None:
            g = c['data'].get('data') or {}
            keys = g.get('keys') or []
            if keys:
                timing = _ctl_timing(c)
                interp = int(g.get('interpolation') or 1)
                ts = np.arange(timing['start'], timing['stop'] + 1e-6, 1 / 30.0)
                ks = []
                for t in ts:
                    v = sample_keys(keys, interp, t)
                    ks.append([round(float(t), 4)] + [round(float(np.clip(x, 0.0, 1.0)), 4) for x in list(v)[:3]])
                out.append(dict(timing, target=COLOR_TARGETS[(int(c.get('flags') or 0) >> 4) & 3], keys=ks))
        c = c.get('next_controller')
    return out


def settled_color(anim):
    """A colour animation's colour when nothing plays it: the last key of a play-once one, else the brightest."""
    keys = anim['keys']
    if anim.get('cycle') == 'clamp':
        return keys[-1][1:]
    return max(keys, key=lambda k: sum(k[1:]))[1:]


def alpha_animation(mat):
    """A NiAlphaController on the material, for one-shot effects: {keys: [[t, alpha]] (30 per second) + timing}."""
    c = _controller(mat, 'NiAlphaController') if mat is not None else None
    g = ((c['data'].get('data') or {}) if c is not None and c.get('data') is not None else {})
    keys = g.get('keys') or []
    if not keys:
        return None
    timing = _ctl_timing(c)
    ts = np.arange(timing['start'], timing['stop'] + 1e-6, 1 / 30.0)
    return dict(timing, keys=[[round(float(t), 4), round(float(sample_keys(keys, int(g.get('interpolation') or 1), t)[0]), 4)]
                              for t in ts])


def collect(nif, node_anims=False):
    """[(mesh arrays, material key)] for every visible shape, in model space (DAoC units, DAoC axes). node_anims
    (effects): also the hidden shapes a NiVisController shows, tagged with their animated node."""
    sp = spec()
    shapes, doors = [], []
    collidees = {id(n) for n, _ in find_collidees(nif)}

    def walk(b, parent_m, inherited):
        if b is None or any(sp.is_a(b.type, t) for t in SKIP_TYPES):
            return
        if id(b) in collidees:                             # walls only, never drawn (see collidee_shapes)
            return
        if b.get('flags', 0) & 1 and not (node_anims and _controller(b, 'NiVisController') is not None):   # hidden
            return
        m = local_matrix(b) @ parent_m
        if np.isnan(m).any():                              # a few scene nodes carry broken transforms (charscreen Box02)
            return
        props = dict(inherited)
        props.update(props_of(b))
        if node_anims:
            # a spell / monster effect: everything in it runs on the effect's timeline - its material fades and colour
            # keys too, not only those of the parts under an animated node (2026-10-13: the earth fingers, a morphing
            # mesh whose material fades in from 0, stayed invisible)
            props['_fx'] = True
        env = environment_map(b)
        if env:
            props['_envmap'] = env
        if DOOR_GROUP.match(b.get('name') or '') and '_door' not in props:
            props['_doorgroup'] = b.get('name')
        if '_door' not in props and is_door_leaf(b, '_doorgroup' in props):
            spec_d = door_spec(b, parent_m, m, len(doors))
            spec_d['group'] = props.get('_doorgroup') or b.get('name')
            doors.append(spec_d)
            props['_door'] = (len(doors) - 1, m)
        if node_anims and '_door' not in props and b is not nif.roots[0] and sp.is_a(b.type, 'NiNode') and (
                _controller(b, 'NiVisController') is not None or _controller(b, 'NiKeyframeController') is not None
                or _controller(b, 'NiPathController') is not None):
            props['_anim'] = (b, m, parent_m)               # an animated node (a spell's streak / flash): node_animation
        if sp.is_a(b.type, 'NiBillboardNode'):
            # camera-facing cards (grass, leaves): mode from the node flags (bits 5-6: 0 face camera, 1 rotate about
            # up, 2 rigid face camera, 3 face centre); the client also tags them "billboardUp" / "billboardFace"
            mode = (int(b.get('flags', 0)) >> 5) & 3
            props['_billboard'] = ('up' if mode == 1 else 'face', m)
            if '_bb_outer' not in props:                   # the outermost one: a pivot for animated parts below it
                props['_bb_outer'] = (b, m, 'up' if mode == 1 else 'face')
        if node_anims:
            props['_path'] = tuple(inherited.get('_path', ())) + (b,)
        if sp.is_a(b.type, 'NiTriBasedGeom'):
            data = b.get('data')
            if data is not None and data.get('num_vertices'):
                shapes.append((b, data, m, props))
            return
        children = [c for c in (b.get('children') or []) if c is not None]
        if sp.is_a(b.type, 'NiLODNode') and children:
            levels = b.get('lod_levels') or (b.get('lod_level_data') or {}).get('lod_levels') or []
            if levels and len(levels) == len(children):
                best = min(range(len(children)), key=lambda i: levels[i]['near_extent'])
            else:
                best = len(children) - 1 if any('near' in (c.get('name') or '').lower() for c in children[-1:]) else 0
                for i, c in enumerate(children):
                    if 'near' in (c.get('name') or '').lower() or 'high' in (c.get('name') or '').lower():
                        best = i
            children = [children[best]]
        for c in children:
            walk(c, m, props)

    for r in nif.roots:
        walk(r, np.identity(4), {})
    return shapes, doors


def shape_arrays(data, m):
    v = np.asarray(data['vertices'], dtype=np.float64)[:, :3]
    pos = (np.c_[v, np.ones(len(v))] @ m)[:, :3] @ DAOC_TO_GODOT.T * UNITS_TO_METRES
    nrm = None
    if data.get('has_normals') and data.get('normals') is not None and len(data['normals']):
        n = np.asarray(data['normals'], dtype=np.float64)[:, :3] @ m[:3, :3]
        n = n @ DAOC_TO_GODOT.T
        nrm = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    uv = []                                                # every UV set; the texture slots say which one they use
    for s in data.get('uv_sets') or []:
        if len(s):
            uv.append(np.asarray(s, dtype=np.float64)[:, :2])
    col = None
    if data.get('has_vertex_colors') and data.get('vertex_colors') is not None:
        col = np.clip(np.asarray(data['vertex_colors'], dtype=np.float64)[:, :4], 0, 1)
    tri = triangles_of(data)
    tri = tri[(tri < len(pos)).all(axis=1)]
    if nrm is None:
        nrm = vertex_normals(pos, tri)
    return pos, nrm, uv, col, tri


def vertex_normals(pos, tri):
    n = np.zeros_like(pos)
    if len(tri):
        f = np.cross(pos[tri[:, 1]] - pos[tri[:, 0]], pos[tri[:, 2]] - pos[tri[:, 0]])
        for k in range(3):
            np.add.at(n, tri[:, k], f)
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)


# texture slots the converter carries, in the order they claim texture coordinates (base first, so it gets UV)
SLOTS = (('base', 'base_texture'), ('dark', 'dark_texture'), ('detail', 'detail_texture'),
         ('decal', 'decal_0_texture'), ('glow', 'glow_texture'), ('gloss', 'gloss_texture'))
NOT_RENDERED = ('bump_map_texture', 'decal_1_texture', 'decal_2_texture', 'decal_3_texture')
MAX_TEXCOORDS = 4                     # UV, UV2 and the two halves of CUSTOM0 in the engine shader


def color3(c):
    return [float(c['r']), float(c['g']), float(c['b'])] if c is not None else [1.0, 1.0, 1.0]


SHADER_SLOT_KEYS = {'Texture1Index': 'tex1', 'Texture2Index': 'tex2', 'DetailIndex': 'detail', 'DarkIndex': 'dark'}


def shader_slots(shape, tp):
    """{slot: (texture name, uv set, wrap)} for shapes whose NiTexturingProperty shader textures are named by the
    shape's NiIntegerExtraData (Texture1Index, Texture2Index, DetailIndex, DarkIndex): the client's two-texture blend
    used on the character-select terrain and trees. Texture1 shows where vertex alpha is 1, Texture2 where it is 0
    (checked: keep walls are ~90% wall texture at alpha 1); Dark is the light map, Detail multiplies x2."""
    if shape is None or tp is None:
        return {}
    idx = {}
    for e in shape.get('extra_data_list') or []:
        if e is not None and e.type == 'NiIntegerExtraData' and e.get('name') in SHADER_SLOT_KEYS:
            idx[SHADER_SLOT_KEYS[e.get('name')]] = int(e.get('integer_data'))
    if 'tex1' not in idx:
        return {}
    shader = tp.get('shader_textures') or []
    out = {}
    for slot, i in idx.items():
        if 0 <= i < len(shader):
            td = shader[i].get('texture_data') if hasattr(shader[i], 'get') else None
            src = td.get('source') if td else None
            if src is None:
                continue
            name = (src.get('file_name') or '').replace('\\', '/').split('/')[-1].rsplit('.', 1)[0].lower()
            if name:
                out[slot] = (name, int(td.get('uv_set') or 0), int(td.get('clamp_mode', 3)) == 3)
    return out


def material_spec(props, uv_count, has_colors, warnings, shape_name, shape=None):
    """Everything the engine needs to draw a shape like the original: every texture slot with the UV set it uses
    (renumbered to the texture coordinates exported for this mesh), alpha, sidedness, vertex colours and
    material colour. Returns (spec, [source UV set per exported texcoord], [texture names])."""
    tp = props.get('NiTexturingProperty')
    spec = {'slots': {}}
    sets, names = [], []
    for slot, field in SLOTS:
        name, uvset = texture_slot(tp, field) if tp is not None else (None, 0)
        if not name:
            continue
        if uvset >= uv_count:
            warnings.append(f'{shape_name}: {slot} texture {name} uses UV set {uvset} but the mesh has {uv_count}')
            uvset = 0
        if uvset not in sets:
            if len(sets) == MAX_TEXCOORDS:
                warnings.append(f'{shape_name}: {slot} texture {name} needs a 5th distinct UV set; drawn with UV set 0')
                uvset = sets[0]
            else:
                sets.append(uvset)
        # NetImmerse detail maps modulate x2 over the base: kept raw as "<name>__x2" and doubled in the shader
        # (detail_x2 - a pre-doubled png clipped at white and could only darken; owner 2026-10-14, Malmohus' pines)
        tex = f'{name}__x2' if slot == 'detail' else name
        if slot == 'detail':
            spec['detail_x2'] = True
        clamp = int(tp[field].get('clamp_mode', 3))                  # 3 = wrap both ways; decals usually clamp
        spec['slots'][slot] = {'tex': tex, 'uv': sets.index(uvset), 'wrap': clamp == 3}
        names.append(tex)
    blend = shader_slots(shape, tp)
    # Blend direction, fitted to the owner's client screenshots (2026-10-09): Texture1 shows at vertex alpha 1
    # (Midgard walls / rocks, Albion terrain: rock under the character), EXCEPT the Hibernia terrain, the one blend
    # shape with a third (detail / light map) shader texture, where the client shows Texture1 (grass) at alpha ~0.
    # Swap that case. Not understood from the client code yet - re-check against screenshots for new scenes.
    base_name = spec['slots'].get('base', {}).get('tex')
    if 'tex1' in blend and 'tex2' in blend and 'detail' in blend and blend['tex2'][0] == base_name             and blend['tex1'][0] != base_name:
        blend['tex1'], blend['tex2'] = blend['tex2'], blend['tex1']
    for slot, (name, uvset, wrap) in blend.items():
        if uvset >= uv_count:
            uvset = 0
        if uvset not in sets:
            if len(sets) == MAX_TEXCOORDS:
                uvset = sets[0]
            else:
                sets.append(uvset)
        tex = name                                          # shader-index detail: true modulate x2 in the shader
        spec['slots'][slot] = {'tex': tex, 'uv': sets.index(uvset), 'wrap': wrap}
        if slot == 'detail':                                # (white = 2x brighter, as the client draws it;
            spec['detail_x2'] = True                        # a pre-doubled png would clip that at 1.0)
        names.append(tex)
    if blend:
        spec['blend2'] = True                               # base = mix(tex2, tex1, vertex alpha); alpha isn't opacity
    if tp is not None:
        for field in NOT_RENDERED:
            if tp.get('has_' + field):
                warnings.append(f'{shape_name}: {field} {texture_name(tp, field)} is not rendered yet')
        if tp.get('num_shader_textures') and not blend:
            warnings.append(f'{shape_name}: {tp.get("num_shader_textures")} shader texture(s) not rendered yet')
        spec['apply'] = int(tp.get('apply_mode', 2))
    if props.get('_envmap'):                               # NiTextureEffect sphere map (see environment_map)
        spec['slots']['env'] = {'tex': props['_envmap'], 'uv': 0, 'wrap': True}
        names.append(props['_envmap'])
    mode, cutoff = alpha_mode(props.get('NiAlphaProperty'))
    ap = props.get('NiAlphaProperty')
    # an effect's sprite (an animated node's card): blend on with a zero alpha test is a soft blend - as a cut-out at
    # 0.05 the hand glows' soft cores drew hard-edged discs (scenery keeps its cut-outs)
    timed = '_anim' in props or '_fx' in props             # on an effect's timeline (NifScene plays its keys)
    if timed and ap is not None and mode == 'mask' and int(ap.get('flags', 0)) & 1 \
            and int(ap.get('threshold', 0)) == 0:
        mode = 'blend'
    mat = props.get('NiMaterialProperty')
    alpha = float(mat.get('alpha', 1.0)) if mat is not None else 1.0
    aa = alpha_animation(mat) if timed else None
    if aa is not None:                                     # an effect's fade (played by NifScene): drawn at its keys,
        alpha = max(k[1] for k in aa['keys'])              # an additive glow stays additive
    else:
        alpha = controlled_alpha(mat, alpha)
    if alpha < 0.99 and mode not in ('screen', 'add' if aa is not None else 'screen'):   # screen: alpha = opacity
        mode = 'blend'
    # source factor DESTCOLOR (destination ONE / DESTALPHA / INVSRCALPHA): the layer only brightens / tints what's
    # behind it - behind x (1 + colour), or behind x (colour + 1 - alpha) - black adds nothing whatever its alpha. As
    # a blend (material alpha < 1) the paladin's Refreshment column drew a see-through grey box that stayed after its
    # colour faded (owner 2026-10-15; 96 effects: cleric buffs, Crusader's Barrier, void / disease effects ...)
    if ap is not None and int(ap.get('flags', 0)) & 1 and (int(ap.get('flags', 0)) >> 1) & 15 == 4             and (int(ap.get('flags', 0)) >> 5) & 15 in (0, 7, 8):
        mode = 'destcolor'
        spec['dest_keep'] = 'invsrcalpha' if (int(ap.get('flags', 0)) >> 5) & 15 == 7 else 'one'
    spec.update(alpha=mode, cut=round(cutoff, 3), opacity=round(alpha, 3))
    # source factor INVDESTCOLOR (corpse lights, death blasts, life-tap skulls, lava pools ... 55 effects): the colour
    # is added in full where what's behind is dark and the alpha only dims what's behind - a glow over the dark,
    # not a see-through grey card (owner 2026-10-15: "the corpselight npc's are especially messed up")
    if ap is not None and int(ap.get('flags', 0)) & 1 and (int(ap.get('flags', 0)) >> 1) & 15 == 5             and mode in ('add', 'blend'):
        spec['src_invdest'] = True
    diffuse = color3(mat.get('diffuse_color')) if mat is not None else [1.0, 1.0, 1.0]
    peak = max(max(diffuse), 1e-3)
    # keep the material's tint but not its darkness: the old fixed-function lighting scaled by it, ours doesn't
    spec['tint'] = [round(c / peak, 3) for c in diffuse] if 'base' in spec['slots'] else [round(c, 3) for c in diffuse]
    # the darkness that was dropped: the client's ambient-lit scenes (character select) multiply by the material's
    # ambient colour when there are no vertex colours (NifMaterials.use_material_light)
    amb = color3(mat.get('ambient_color')) if mat is not None else [1.0, 1.0, 1.0]
    spec['mat_light'] = round(max(max(amb), 0.0), 3) if 'base' in spec['slots'] else 1.0
    # an ambient colour of another hue than the diffuse (Jordheim's city grass: ambient white, diffuse green; its
    # paths: diffuse orange): the client lights with emissive + ambient x ambient light + diffuse x sun x N.L, so the
    # ambient light shows the ambient colour and only the direct light the diffuse one. Tint = the ambient's, and
    # diffuse_tint = diffuse / ambient for the direct light (NifMaterials' split-light shader). Owner 2026-10-14: the
    # Jordheim grass was bright green and the paths orange - the diffuse tinted everything
    _vcp = props.get('NiVertexColorProperty')
    _vmode = int(_vcp.get('vertex_mode', 2)) if _vcp is not None else 2
    if mat is not None and 'base' in spec['slots'] and not (has_colors and _vmode != 0):
        pa, pd_ = max(max(amb), 1e-3), max(max(diffuse), 1e-3)
        an = [c / pa for c in amb]
        dn = [c / pd_ for c in diffuse]
        if max(abs(a - d) for a, d in zip(an, dn)) > 0.15 and max(amb) > 1e-3:
            spec['tint'] = [round(c, 3) for c in an]
            spec['diffuse_tint'] = [round(min(d / max(a, 0.05), 4.0), 3) for a, d in zip(an, dn)]
            # the diffuse colour itself: a ratio can't carry it where the ambient has no such channel (owner
            # 2026-10-15: Trollheim's ice wall, ambient 0 / 0.72 / 1, diffuse near white - ambient x 4 stayed
            # cyan in the sun). NifMaterials lights the untinted texture with it.
            spec['diffuse'] = [round(c, 3) for c in dn]
    spec['emissive'] = [round(c, 3) for c in color3(mat.get('emissive_color'))] if mat is not None else [0, 0, 0]
    canims = color_animations(mat) if mat is not None else []
    for ca in canims:                                      # the animated colours, settled for a still drawing
        col = settled_color(ca) if not timed else max(ca['keys'], key=lambda k: sum(k[1:]))[1:]
        if ca['target'] == 'emissive':
            spec['emissive'] = [round(float(x), 3) for x in col]
        elif ca['target'] == 'diffuse':
            peak = max(max(max(k[1:]) for k in ca['keys']), 1e-3)
            ca['peak'] = round(peak, 4)
            spec['tint'] = [round(float(x) / peak, 3) for x in col] if 'base' in spec['slots'] else [round(float(x), 3) for x in col]
    if canims and timed:
        spec['color_anims'] = [ca for ca in canims if ca['target'] in ('emissive', 'diffuse')]   # played by NifScene
    vcp = props.get('NiVertexColorProperty')
    vmode = int(vcp.get('vertex_mode', 2)) if vcp is not None else 2
    spec['vertex_colors'] = bool(has_colors) and vmode != 0          # source-ignore turns them off
    # lighting mode 0 (emissive only): the client lit nothing - texture x emissive (the vertex colour when the vertex
    # mode is source-emissive) plus the glow / env layers. An effect's piece only (2026-10-13: the Sluagh Blood
    # Shield is black + a dark red env map; lit with sun and specular it drew grey, the client's is near black)
    if timed and vcp is not None and int(vcp.get('lighting_mode', 1)) == 0 and mode != 'add':
        spec['unlit'] = True
        if vmode == 1 and has_colors:
            spec['emissive'] = [1.0, 1.0, 1.0]
        else:
            spec['vertex_colors'] = False
    stencil = props.get('NiStencilProperty')
    spec['double_sided'] = mode != 'opaque' or (stencil is not None and int(stencil.get('draw_mode', 0)) == 3)
    spec['specular'] = 'NiSpecularProperty' in props and bool(props['NiSpecularProperty'].get('flags', 0) & 1)
    if '_billboard' in props:
        spec['billboard'] = props['_billboard'][0]
    scroll = uv_scrolls(tp, warnings, shape_name) if tp is not None else {}
    timed_uv = {}
    for slot, sc in uv_controller_scrolls(shape, sets, spec['slots']).items():
        if isinstance(sc, dict):
            timed_uv[slot] = sc
        else:
            scroll.setdefault(slot, sc)
    if scroll:
        spec['uv_scroll'] = scroll
    if timed_uv:
        spec['uv_anim'] = timed_uv                         # played by NifScene's effect timeline
    if aa is not None:
        spec['alpha_anim'] = aa                            # played by NifScene's effect timeline
    return spec, sets, names


# NiTextureTransformController (the texturing property's controllers): texture slot number -> engine slot, and the
# operation (0 translate U, 1 translate V, 2 rotate, 3 scale U, 4 scale V)
TT_SLOTS = {0: 'base', 1: 'dark', 2: 'detail', 3: 'gloss', 4: 'glow', 6: 'decal'}


def uv_scrolls(tp, warnings, shape_name):
    """{slot: [du, dv, period]}: a looping texture scroll (flames moving up a torch's fire card, the glow of a lamp,
    sky clouds drifting) - the controller's float keys run from the first to the last over its key time, then repeat
    (cycle type loop). Rotate / scale transforms are reported, not drawn."""
    out = {}
    c = tp.get('controller')
    seen = set()
    while c is not None and id(c) not in seen:
        seen.add(id(c))
        if c.type == 'NiTextureTransformController':
            d = c.get('data')
            keys = ((d.get('data') or {}).get('keys') or []) if d is not None else []
            slot = TT_SLOTS.get(int(c.get('texture_slot') or 0))
            op = int(c.get('operation') or 0)
            if len(keys) >= 2 and slot and op in (0, 1):
                span = float(keys[-1]['time']) - float(keys[0]['time'])
                delta = float(keys[-1]['value']) - float(keys[0]['value'])
                if span > 0 and delta != 0:
                    cur = out.setdefault(slot, [0.0, 0.0, span])
                    cur[op] = delta
                    cur[2] = span
            elif keys:
                warnings.append(f'{shape_name}: texture transform op {op} on slot {c.get("texture_slot")} not drawn yet')
        c = c.get('next_controller')
    return out


def uv_controller_scrolls(shape, sets, slots):
    """The older NiUVController on the shape itself (owner 2026-10-15: the corpse lights' streaks drip like a melting
    candle in the client; 5 controllers on the corpse torch alone were not read): its NiUVData U / V offset keys run
    over the controller's time and loop - {slot: [du, dv, period]} for every slot drawn with that controller's UV set
    (its 'unknown_short'). Tiling keys are not drawn. Only the looping controllers scroll forever; the 627 clamp (play
    once) / reverse ones in the effects - the flasks' one-step offset at 4.23 s - come back as {slot: {'u': keys,
    'v': keys, timing}} for NifScene to play on the effect's own time (looped they flickered every 0.07 s)."""
    out = {}
    if shape is None:
        return out
    c = shape.get('controller')
    seen = set()
    while c is not None and id(c) not in seen:
        seen.add(id(c))
        if c.type == 'NiUVController' and c.get('data') is not None:
            groups = c.get('data').get('uv_groups') or []
            uv_set = int(c.get('unknown_short') or 0)
            move = [0.0, 0.0]
            span = 0.0
            for i in (0, 1):                               # U offset, V offset
                keys = (groups[i].get('keys') or []) if i < len(groups) else []
                if len(keys) >= 2:
                    t = float(keys[-1]['time']) - float(keys[0]['time'])
                    if t > 0:
                        move[i] = float(keys[-1]['value']) - float(keys[0]['value'])
                        span = max(span, t)
            timing = _ctl_timing(c)
            if span > 0 and (move[0] or move[1]):
                if timing['cycle'] == 'loop':
                    val = [move[0], move[1], span]
                else:
                    val = dict(timing, **{ax: [[float(k['time']), float(k['value'])] for k in (groups[i].get('keys') or [])]
                                          for i, ax in ((0, 'u'), (1, 'v')) if i < len(groups)})
                for slot, info in slots.items():
                    src = sets[int(info['uv'])] if int(info['uv']) < len(sets) else 0
                    if src == uv_set:
                        out[slot] = val
        c = c.get('next_controller')
    return out


class Gltf:
    def __init__(self):
        self.buf = bytearray()
        self.doc = {'asset': {'version': '2.0', 'generator': 'MMOEngine nif_static'}, 'scene': 0,
                    'scenes': [{'nodes': []}], 'nodes': [], 'meshes': [], 'materials': [], 'textures': [],
                    'images': [], 'samplers': [{'magFilter': 9729, 'minFilter': 9987}], 'accessors': [],
                    'bufferViews': [], 'buffers': [], 'extras': {'nif_materials': [], 'warnings': []}}
        self.mat_index = {}

    def accessor(self, arr, comp, kind, target):
        while len(self.buf) % 4:
            self.buf.append(0)
        raw = arr.tobytes()
        self.doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(self.buf), 'byteLength': len(raw), 'target': target})
        self.buf += raw
        acc = {'bufferView': len(self.doc['bufferViews']) - 1, 'componentType': comp, 'count': len(arr), 'type': kind}
        if kind == 'VEC3' and comp == 5126:
            acc['min'], acc['max'] = arr.min(0).tolist(), arr.max(0).tolist()
        self.doc['accessors'].append(acc)
        return len(self.doc['accessors']) - 1

    def material(self, spec, texture_uri):
        """glTF material named "nifmat:<n>"; the full spec lives in extras.nif_materials[n] for the engine.
        The base texture is also wired up the standard way so any glTF viewer shows something sensible."""
        key = json.dumps(spec, sort_keys=True)
        if key in self.mat_index:
            return self.mat_index[key]
        n = len(self.doc['extras']['nif_materials'])
        self.doc['extras']['nif_materials'].append(spec)
        mat = {'name': f'nifmat:{n}', 'doubleSided': bool(spec.get('double_sided')),
               'pbrMetallicRoughness': {'baseColorFactor': [*spec.get('tint', [1, 1, 1]), 1.0], 'metallicFactor': 0.0,
                                        'roughnessFactor': 0.9}}
        base = spec.get('slots', {}).get('base')
        if base:
            self.doc['images'].append({'uri': f'{texture_uri}/{base["tex"]}.png'})
            self.doc['textures'].append({'sampler': 0, 'source': len(self.doc['images']) - 1})
            mat['pbrMetallicRoughness']['baseColorTexture'] = {'index': len(self.doc['textures']) - 1,
                                                               'texCoord': base['uv']}
        if spec.get('alpha') == 'mask':
            mat['alphaMode'] = 'MASK'
            mat['alphaCutoff'] = spec.get('cut', 0.5)
        elif spec.get('alpha') == 'blend':
            mat['alphaMode'] = 'BLEND'
        self.doc['materials'].append(mat)
        self.mat_index[key] = len(self.doc['materials']) - 1
        return self.mat_index[key]

    def mesh(self, name, pos, nrm, uvs, col, tri, material, translation=None, targets=None):
        attrs = {'POSITION': self.accessor(pos.astype(np.float32), 5126, 'VEC3', 34962),
                 'NORMAL': self.accessor(nrm.astype(np.float32), 5126, 'VEC3', 34962)}
        for k, uv in enumerate(uvs):
            attrs[f'TEXCOORD_{k}'] = self.accessor(uv.astype(np.float32), 5126, 'VEC2', 34962)
        if col is not None:
            attrs['COLOR_0'] = self.accessor(col.astype(np.float32), 5126, 'VEC4', 34962)
        idx = self.accessor(tri.reshape(-1).astype(np.uint32), 5125, 'SCALAR', 34963)
        prim = {'attributes': attrs, 'indices': idx}
        if material is not None:
            prim['material'] = material
        mesh = {'name': name, 'primitives': [prim]}
        if targets:                                        # morph frames (NiGeomMorpherController), as displacements
            prim['targets'] = [{'POSITION': self.accessor(t.astype(np.float32), 5126, 'VEC3', 34962)} for t in targets]
            mesh['weights'] = [0.0] * len(targets)
        self.doc['meshes'].append(mesh)
        node = {'name': name, 'mesh': len(self.doc['meshes']) - 1}
        if translation is not None:
            node['translation'] = [float(v) for v in translation]
        self.doc['nodes'].append(node)
        self.doc['scenes'][0]['nodes'].append(len(self.doc['nodes']) - 1)

    def save(self, path):
        self.doc['buffers'] = [{'byteLength': len(self.buf),
                                'uri': 'data:application/octet-stream;base64,' + base64.b64encode(bytes(self.buf)).decode()}]
        Path(path).write_text(json.dumps(self.doc))


def morph_animation(shape, m):
    """A NiGeomMorpherController on the shape: the client's vertex animation (the pregame banners' wave, grass
    shoots, mushrooms). NiMorphData morph 0 is the base mesh; the others are frames, relative displacements when
    relative_targets is set, each with its own weight keys. Returns (displacements in engine units, timing spec) or
    None. The weights are sampled at every key time of every frame (keys are linear or quadratic with flat
    tangents here: 0 / 1 per frame, so linear between keys is the client's crossfade)."""
    c = shape.get('controller')
    while c is not None and c.type != 'NiGeomMorpherController':
        c = c.get('next_controller')
    if c is None or c.get('data') is None:
        return None
    d = c['data']
    morphs = d.get('morphs') or []
    if len(morphs) < 2:
        return None
    base = np.asarray(morphs[0]['vectors'], dtype=np.float64)[:, :3]
    rel = bool(d.get('relative_targets', 1))
    rot = m[:3, :3]
    targets, keysets = [], []
    for mo in morphs[1:]:
        v = np.asarray(mo['vectors'], dtype=np.float64)[:, :3]
        disp = v if rel else v - base
        targets.append((disp @ rot) @ DAOC_TO_GODOT.T * UNITS_TO_METRES)
        keysets.append([(float(k['time']), float(k['value'])) for k in (mo.get('keys') or [])])
    times = sorted({t for ks in keysets for t, _ in ks})
    if not times:
        return None

    def at(ks, t):
        if not ks:
            return 0.0
        if t <= ks[0][0]:
            return ks[0][1]
        for (t0, v0), (t1, v1) in zip(ks, ks[1:]):
            if t0 <= t <= t1:
                return v0 if t1 == t0 else v0 + (v1 - v0) * (t - t0) / (t1 - t0)
        return ks[-1][1]

    weights = [[round(at(ks, t), 4) for ks in keysets] for t in times]
    flags = int(c.get('flags', 0))
    spec = {'times': [round(t, 4) for t in times], 'weights': weights,
            'start': float(c.get('start_time', times[0])), 'stop': float(c.get('stop_time', times[-1])),
            'frequency': float(c.get('frequency', 1.0) or 1.0), 'phase': float(c.get('phase', 0.0)),
            'cycle': ['loop', 'reverse', 'clamp', 'loop'][(flags >> 1) & 3], 'active': bool(flags & 8)}
    return targets, spec


PARTICLE_TYPES = ('NiParticles',)                    # NiAutoNormalParticles, NiRotatingParticles, ... (4.x systems)


def controlled_alpha(mat, alpha):
    """A NiAlphaController on the material animates its alpha. The engine draws the settled state: the last key for a
    clamped (play-once) controller, e.g. the campfire logs fading in 0 -> 1 over their first second, otherwise the
    largest key."""
    c = mat.get('controller') if mat is not None else None
    while c is not None and c.type != 'NiAlphaController':
        c = c.get('next_controller')
    if c is None or c.get('data') is None:
        return alpha
    keys = (c['data'].get('data') or {}).get('keys') or []
    if not keys:
        return alpha
    cycle = (int(c.get('flags', 0)) >> 1) & 3            # 0 loop, 1 reverse, 2 clamp
    vals = [float(k['value']) for k in keys]
    return vals[-1] if cycle == 2 else max(vals)


def particle_blend(alpha_prop):
    """'add' when the destination factor keeps what is behind (ONE / DESTALPHA), 'screen' for INVSRCCOLOR,
    'multiply' for a DESTCOLOR source, else 'mix' (INVSRCALPHA)."""
    flags = int(alpha_prop.get('flags', 0)) if alpha_prop is not None else 0
    if not flags & 1:
        return 'opaque'
    src, dst = (flags >> 1) & 15, (flags >> 5) & 15
    if src == 5 and dst in (0, 7, 8):
        # INVDESTCOLOR: the particle's colour in full over the dark (a glow, not grey smoke); with INVSRCALPHA its
        # alpha dims what's behind (NifParticles 'invdest' / 'invdest_add')
        return 'invdest' if dst == 7 else 'invdest_add'
    if src == 4 and dst in (1, 7):
        # DESTCOLOR with ZERO / INVSRCALPHA: the scene behind times the particle (plus what its alpha lets through) -
        # a tint that only darkens or colours (30 effect NIFs: the Shaman's orange poison mist 'pestilence', green
        # creep rot, the death blasts, evil fire ... owner 2026-10-13: drawn as solid blobs they weren't a mist)
        return 'multiply'
    return 'add' if dst in (0, 8) else ('screen' if dst == 3 else 'mix')


def _godot_xform(m):
    """Row-vector NIF matrix (DAoC model space, units) -> {'basis': 3x3 rows, 'origin': xyz metres} in Godot axes."""
    a = m[:3, :3].T                                      # column-vector linear part
    b = DAOC_TO_GODOT @ a @ DAOC_TO_GODOT.T
    o = DAOC_TO_GODOT @ m[3, :3] * UNITS_TO_METRES
    return {'basis': b.tolist(), 'origin': o.tolist()}


def _gvec(v):
    return (DAOC_TO_GODOT @ np.array([v['x'], v['y'], v['z']], dtype=np.float64)).tolist()


def particle_systems(nif, textures, warnings, node_anims=False):
    """NetImmerse 4.x particle systems (NiParticleSystemController on a NiParticles shape) as engine-ready specs:
    emitter transform, emission (rate, speed, declination + variation, lifetime, emitter box = start_random), size,
    max particles, modifiers (NiGravity planar/spherical, NiParticleColorModifier colour keys over life,
    NiParticleGrowFade, NiParticleBomb), texture and blend. Units are metres; angles radians."""
    sp = spec()
    world, parent = {}, {}

    def dfs(b, pm, par):
        if b is None:
            return
        m = local_matrix(b) @ pm
        world[id(b)] = m
        parent[id(b)] = par
        for c in b.get('children') or []:
            dfs(c, m, b)
    for r in nif.roots:
        dfs(r, np.identity(4), None)

    def hidden(b):
        while b is not None:
            if int(b.get('flags', 0)) & 1 and not (node_anims and _controller(b, 'NiVisController') is not None):
                return True
            b = parent.get(id(b))
        return False

    def vis_of(b):
        """Effects: the visibility controller that shows a hidden particle system partway in (the explosion's
        'cores' are emitted at once but drawn from 0.1 / 0.23 / 0.37 s: they appear already billowing)."""
        while b is not None:
            vc = _controller(b, 'NiVisController')
            if vc is not None and vc.get('data') is not None:
                return {'keys': [[round(float(k['time']), 4), int(k['value'])] for k in (vc['data'].get('keys') or [])],
                        **_ctl_timing(vc)}
            b = parent.get(id(b))
        return None
    out = []
    for b in nif.blocks:
        if not any(sp.is_a(b.type, t) for t in PARTICLE_TYPES) or id(b) not in world or hidden(b):
            continue
        c = b.get('controller')
        while c is not None and c.type != 'NiParticleSystemController':
            c = c.get('next_controller')
        if c is None:
            continue
        props = {}
        x = b
        while x is not None:                              # properties inherit down the scene graph
            for k, v in props_of(x).items():
                props.setdefault(k, v)
            x = parent.get(id(x))
        tex = texture_name(props.get('NiTexturingProperty'))
        if tex:
            textures.add(tex)
        em = c.get('emitter')
        em_anim = None
        if em is not None and id(em) in world and (_controller(em, 'NiKeyframeController') is not None
                                                    or _controller(em, 'NiPathController') is not None):
            par = parent.get(id(em))
            em_anim = node_animation(em, world[id(par)] if par is not None else np.identity(4))
        sr = c.get('start_random') or {'x': 0, 'y': 0, 'z': 0}
        mods = []
        mesh_quad = None
        mesh_props = {}
        m = c.get('particle_extra')
        while m is not None:
            if m.type == 'NiGravity':
                mods.append({'type': 'gravity', 'kind': 'spherical' if int(m.get('type', 0)) == 1 else 'planar',
                             'force': float(m.get('force', 0.0)) * UNITS_TO_METRES,
                             'direction': _gvec(m.get('direction')),
                             'position': (np.array(_gvec(m.get('position'))) * UNITS_TO_METRES).tolist()})
            elif m.type == 'NiParticleColorModifier':
                cd = m.get('color_data')
                keys = []
                kd = (cd.get('data') if cd is not None else None) or {}
                for k in kd.get('keys') or []:
                    v = k['value']
                    keys.append([float(k['time']), [float(v['r']), float(v['g']), float(v['b']), float(v['a'])]])
                mods.append({'type': 'color', 'keys': keys})
            elif m.type == 'NiParticleGrowFade':
                mods.append({'type': 'growfade', 'grow': float(m.get('grow', 0.0)), 'fade': float(m.get('fade', 0.0))})
            elif m.type == 'NiParticleRotation':          # spin, radians per second
                mods.append({'type': 'rotation', 'speed': float(m.get('rotation_speed', 0.0)),
                             'random_axis': int(m.get('random_initial_axis', 0) or 0)})
            elif m.type == 'NiParticleMeshModifier':      # each particle draws a camera-facing plane (NiBillboardNode)
                lo, hi = np.full(3, np.inf), np.full(3, -np.inf)   # scaled by the particle radius, not a radius-sized sprite
                for mesh in m.get('particle_meshes') or []:
                    for ch in ([mesh] + list(mesh.get('children') or [])) if mesh is not None else []:
                        for k, v in props_of(ch).items():       # the planes carry their own texture / blend (Albion
                            mesh_props.setdefault(k, v)          # fire bolts' flame cards drew as white squares)
                        d = ch.get('data') if ch is not None else None
                        if d is not None and d.get('vertices') is not None and len(d.get('vertices')):
                            v = np.asarray(d.get('vertices'), dtype=np.float64)[:, :3] * float(ch.get('scale', 1.0))
                            lo, hi = np.minimum(lo, v.min(0)), np.maximum(hi, v.max(0))
                if np.isfinite(lo).all():
                    ext = hi - lo
                    mesh_quad = sorted(ext.tolist(), reverse=True)[:2]   # the plane's two spans, model units
            elif m.type == 'NiParticleBomb':
                bomb = {k: m.get(k) for k in m.keys() if k not in ('next_modifier', 'controller', 'position', 'direction')}
                bomb['position'] = (np.array(_gvec(m.get('position'))) * UNITS_TO_METRES).tolist()
                bomb['direction'] = _gvec(m.get('direction'))
                bomb['deltav'] = float(m.get('deltav', 0.0)) * UNITS_TO_METRES
                mods.append({'type': 'bomb', **bomb})
            else:
                warnings.append(f'particle modifier {m.type} not drawn yet')
            m = m.get('next_modifier')
        if not tex and texture_name(mesh_props.get('NiTexturingProperty')):
            tex = texture_name(mesh_props.get('NiTexturingProperty'))
            textures.add(tex)
            if 'NiAlphaProperty' not in props and 'NiAlphaProperty' in mesh_props:
                props['NiAlphaProperty'] = mesh_props['NiAlphaProperty']
        out.append({'name': b.get('name') or '', 'emitter': _godot_xform(world[id(em)] if em is not None and id(em) in world
                                                                          else world[id(b)]),
                    'texture': tex, 'blend': particle_blend(props.get('NiAlphaProperty')),
                    'size': float(c.get('size', 1.0)) * UNITS_TO_METRES,
                    'quad': [q * float(c.get('size', 1.0)) * UNITS_TO_METRES for q in mesh_quad] if mesh_quad else None,
                    'speed': float(c.get('speed', 0.0)) * UNITS_TO_METRES,
                    'speed_random': float(c.get('speed_random', 0.0)) * UNITS_TO_METRES,
                    'declination': float(c.get('vertical_direction', 0.0)),
                    'declination_variation': float(c.get('vertical_angle', 0.0)),
                    'planar_angle': float(c.get('horizontal_direction', 0.0)),
                    'planar_angle_variation': float(c.get('horizontal_angle', 0.0)),
                    'emit_rate': float(c.get('emit_rate', 0.0)), 'lifetime': float(c.get('lifetime', 1.0)),
                    'emit_flags': int(c.get('emit_flags', 0) or 0),   # bit 0: the emit rate counts (else the pool)
                    'lifetime_random': float(c.get('lifetime_random', 0.0)),
                    'box': (np.abs(np.array(_gvec(sr))) * UNITS_TO_METRES).tolist(),
                    'max_particles': int(c.get('num_particles', 0) or 0),
                    'emit_start': float(c.get('emit_start_time', 0.0)), 'emit_stop': float(c.get('emit_stop_time', 0.0)),
                    'cycle': float(c.get('stop_time', 0.0)) - float(c.get('start_time', 0.0)),
                    'modifiers': mods})
        vis = vis_of(b) if node_anims else None
        if vis is not None:
            out[-1]['vis'] = vis
        if em_anim is not None and len({tuple(f[1:]) for f in em_anim['frames']}) > 1:   # it really moves
            out[-1]['emitter_anim'] = {'frames': em_anim['frames'], **em_anim['timing']}
    return out


def convert(raw, out_path, texture_uri='textures', node_anims=False):
    """Write out_path; return texture names, triangle counts, the visible AABB (metres) and any warnings about
    texture layers or UV sets that could not be carried over (never dropped silently)."""
    nif = read_nif(raw)
    g = Gltf()
    textures, warnings, ntri, ncol = set(), [], 0, 0
    lo, hi = np.full(3, np.inf), np.full(3, -np.inf)
    for b in nif.blocks:
        if b.type == 'NiTextureEffect' and int(b.get('texture_type') or 0) == 2:
            continue                                       # environment maps are drawn (environment_map)
        if b.type in ('NiTextureEffect', 'NiMultiTextureProperty', 'NiTextureProperty'):
            warnings.append(f'{b.type} {b.get("name") or ""} not rendered yet')
    for i, (shape, data, m) in enumerate(collidee_shapes(nif)):
        pos, nrm, uv, col, tri = shape_arrays(data, m)
        if len(tri):
            g.mesh(f'collision_{i}', pos, nrm, [], None, tri, None)
            ncol += len(tri)
    nclimb = 0
    for i, (shape, data, m) in enumerate(climb_shapes(nif)):
        pos, nrm, uv, col, tri = shape_arrays(data, m)
        if len(tri):
            g.mesh(f'climb_{i}', pos, nrm, [], None, tri, None)    # hidden: a ladder's climb wall
            nclimb += len(tri)
    hidden = 0
    morph_specs = []
    anim_specs = {}
    shapes, doors = collect(nif, node_anims)
    for i, (shape, data, m, props) in enumerate(shapes):
        if '_door' in props:
            # a door leaf's panel / collision, in the leaf's own frame (the engine poses the leaf: closed / open)
            did, m_leaf = props['_door']
            pos, nrm, uv, col, tri = shape_arrays(data, m @ np.linalg.inv(m_leaf))
            if not len(tri):
                continue
            tp = props.get('NiTexturingProperty')
            if not uv and texture_name(tp) is None:
                g.mesh(f'doorcol{did}_{i}', pos, nrm, [], None, tri, None)
                continue
            spec, sets, names = material_spec(props, len(uv), col is not None, warnings, shape.get('name') or 'door', shape)
            uvs = [uv[k] for k in sets] or ([uv[0]] if uv else [])
            g.mesh(f'door{did}_{i}', pos, nrm, uvs, col if spec['vertex_colors'] else None, tri, g.material(spec, texture_uri))
            textures.update(names)
            continue
        pivot = None
        anim_key = None
        drop_billboard = False
        path = props.get('_path', ())
        outer = props.get('_bb_outer')
        chain = None
        if outer is not None and path:
            # under a camera-facing node: animated parts below it play inside a pivot the engine turns to the camera
            # (NifScene), with their keys relative to it - the shader billboard only had their rest pose
            k = next(i for i, n in enumerate(path) if n is outer[0])
            below = list(path[k + 1:-1])
            if any(_animated(n) for n in below):
                anchors = [n for n in below if _animated(n) or n.type == 'NiBillboardNode']
                chain = below[:below.index(anchors[-1]) + 1]
                base = outer[1]
        elif path and sum(1 for n in path[1:-1] if _animated(n)) >= 2:
            anchors = [n for n in path[1:-1] if _animated(n)]   # nested animated nodes: all of them, in effect space
            chain = list(path[1:path.index(anchors[-1]) + 1])
            base = local_matrix(path[0])
        if chain:
            anchor = chain[-1]
            anim_key = ('chain', id(anchor))
            if anim_key not in anim_specs:
                # under a pivot: relative to it (the engine places and turns the pivot); else in effect space. A
                # face-the-camera pivot's own rotation counts for nothing (the camera sets all of it); one that turns
                # about the up axis keeps it - the scare faces' billboards swap y / z so the face looks along +y, and
                # without it the face looked up and away (2026-10-13)
                rel = np.identity(4)
                if outer is not None and outer[2] == 'up':
                    rel[:3, :3] = local_matrix(outer[0])[:3, :3]
                a = chain_animation(chain, rel if outer is not None else base)
                a.update(meshes=[], name=anchor.get('name') or '')
                if outer is not None:
                    a['pivot'] = [round(float(x), 5) for x in (DAOC_TO_GODOT @ outer[1][3, :3]) * UNITS_TO_METRES]
                    a['pivot_mode'] = outer[2]
                anim_specs[anim_key] = a
            m_anchor = base
            for n in chain:
                m_anchor = local_matrix(n) @ m_anchor
            m = m @ np.linalg.inv(m_anchor)                # in the anchor's frame (its keys move it)
            # an inner camera-facing card keeps its shader billboard at the anchor; other parts turn with the pivot
            drop_billboard = outer is not None and anchor.type != 'NiBillboardNode'
        elif '_anim' in props and '_billboard' not in props:
            nb, m_node, pm = props['_anim']
            anim_key = id(nb)
            if anim_key not in anim_specs:
                anim_specs[anim_key] = dict(node_animation(nb, pm), meshes=[], name=nb.get('name') or '')
            m = m @ np.linalg.inv(m_node)                  # in the animated node's frame
        if '_billboard' in props and not chain:
            m_bb = props['_billboard'][1]
            m = m @ np.linalg.inv(m_bb)                    # in the billboard node's frame; the engine turns it
            pivot = (DAOC_TO_GODOT @ m_bb[3, :3]) * UNITS_TO_METRES
        pos, nrm, uv, col, tri = shape_arrays(data, m)
        if not len(tri):
            continue
        name = shape.get('name') or f'shape{i}'
        tp = props.get('NiTexturingProperty')
        # untextured helper geometry (e.g. door 'collideeNN'): not drawn - except an effect's untextured shape whose
        # material is animated (NiAlphaController / NiMaterialColorController): drawn in its material's colour, fading
        # as keyed (owner 2026-10-20: the paladin heal chant's chalice - alb_palgrpheal_hit's 1,104-triangle 'PolyMesh'
        # - was left out as collision; so were blesshalo's halo and the Mjolnir / Gungnir weapons). The effects'
        # unanimated untextured planes and boxes (alpha 0 helpers, emitter boxes) stay out.
        mp_ = props.get('NiMaterialProperty')
        keep = node_anims and mp_ is not None and 'collid' not in str(name).lower() and any(
            _controller(mp_, t) is not None for t in ('NiAlphaController', 'NiMaterialColorController'))
        if not uv and texture_name(tp) is None and not keep:
            hidden += len(tri)
            continue
        spec, sets, names = material_spec(props, len(uv), col is not None, warnings, name, shape)
        if drop_billboard:
            spec.pop('billboard', None)
        uvs = [uv[k] for k in sets] or ([uv[0]] if uv else [])
        anim = morph_animation(shape, m)
        targets = None
        if anim is not None and len(anim[0][0]) == len(pos):
            targets, mspec = anim
            name = f'morph_{len(morph_specs)}'               # a unique node name the engine finds the timing by
            mspec['node'] = name
            morph_specs.append(mspec)
        if anim_key is not None:
            if not name.startswith('morph_'):
                name = f'anim_{len(anim_specs) - 1}_{i}'
            anim_specs[anim_key]['meshes'].append(name)
        g.mesh(name, pos, nrm, uvs, col if spec['vertex_colors'] else None, tri, g.material(spec, texture_uri), pivot,
               targets)
        textures.update(names)
        ntri += len(tri)
        wpos = pos + pivot if pivot is not None else pos
        lo, hi = np.minimum(lo, wpos.min(0)), np.maximum(hi, wpos.max(0))
    if hidden:
        warnings.append(f'{hidden} untextured triangles outside the collidee node left out (door collision etc.)')
    particles = particle_systems(nif, textures, warnings, node_anims)
    if particles:
        g.doc['extras']['nif_particles'] = particles
    if morph_specs:
        g.doc['extras']['nif_morphs'] = morph_specs
    if anim_specs:
        g.doc['extras']['nif_node_anims'] = [a for a in anim_specs.values() if a['meshes']]
    if doors:
        g.doc['extras']['nif_doors'] = doors
    g.doc['extras']['warnings'] = sorted(set(warnings))
    g.save(out_path)
    return {'textures': textures, 'triangles': ntri, 'collision_triangles': ncol, 'climb_triangles': nclimb, 'aabb': (lo.tolist(), hi.tolist()),
            'particles': len(particles), 'morphs': len(morph_specs), 'doors': len(doors),
            'warnings': sorted(set(warnings))}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('src')
    ap.add_argument('out')
    ap.add_argument('--textures-uri', default='textures')
    a = ap.parse_args()
    if a.src.lower().endswith(('.npk', '.mpk')):
        from daoc_mpk import read_mpk
        raw = next(v for k, v in read_mpk(a.src).items() if k.endswith('.nif'))
    else:
        raw = open(a.src, 'rb').read()
    print(convert(raw, a.out, a.textures_uri))


if __name__ == '__main__':
    main()
