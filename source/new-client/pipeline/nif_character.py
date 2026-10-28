"""DAoC character NIF + .kfa animations -> skinned, animated glTF (MMOEngine characters).

    python nif_character.py            # builds the classic Celt male into godot/content/characters/celt_male/
    python nif_character.py cata       # builds the Catacombs Celt male into godot/content/characters/celt_male_cata/

Owner 2026-10-09: the player and NPCs use the CATACOMBS (fig3) models, every race. Those are assembled by the client
from separate files (see build_cata): skeleton figures/fig3/sfigNNN.mpk <race>_skeleton.nif, parts figures/fig3/
figNNN.mpk (gamedata fig3map.csv row per figure + part slot -> fig3parts.csv id -> filename + archive), textures from
the skin tables (pskins.csv "Cata ..." sets -> mskins ids; fig3facemap / fig3haircolormap / fig3eyecolormap /
fig3skincolormap -> skins.csv ids + fig3tints.csv colours), animations from the Catacombs anim set (anims.csv 265 =
Cat_*_HM.kfa for the Celt male).

Character: figures/<model>.NIF (Gamebryo 10.1) holds the Bip01 skeleton and every body-part variant, skinned
(NiSkinInstance / NiSkinData with per-vertex weights). The chosen parts are exported with a glTF skin over the whole
skeleton: vertices in the model's bind pose, inverse bind matrices from the bone bind transforms.
Animations: anims/<clip>.kfa (NetImmerse 4.2.x). The first NiNode carries two parallel chains: NiStringExtraData
(a bone index into figures/animnode.dat, the client's bone-name list) and NiKeyframeController -> NiKeyframeData
(quaternion rotation keys, translation and scale keys). k-th name goes with k-th controller.
Clip choice per figure comes from gamedata anims.csv (Walk, Back, Run, Idle, Jump, Crawl/swim, ...) and
animnifs.csv (anim id -> kfa file).
Axes: the model stays in NIF space (Z up) under a root node that rotates it to Y up and scales DAoC units to metres;
NIF quaternion keys are standard (column-vector) rotations, the file's 3x3 matrices are their transpose.
"""
import base64, csv, io, json, math, re, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT, ROOT
from netimmerse import read_nif, spec
import nif_static

M = 0.025


def quat_from_matrix(m):
    """(x, y, z, w) of a column-vector rotation matrix."""
    t = np.trace(m)
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        return np.array([(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, 0.25 * s])
    i = int(np.argmax([m[0, 0], m[1, 1], m[2, 2]]))
    j, k = (i + 1) % 3, (i + 2) % 3
    s = math.sqrt(1.0 + m[i, i] - m[j, j] - m[k, k]) * 2
    q = np.zeros(4)
    q[i] = 0.25 * s
    q[j] = (m[j, i] + m[i, j]) / s
    q[k] = (m[k, i] + m[i, k]) / s
    q[3] = (m[k, j] - m[j, k]) / s
    return q


def node_trs(b):
    """Local translation, rotation quaternion (x,y,z,w), uniform scale of a NiAVObject (column-vector convention)."""
    t = b.get('translation')
    r = b.get('rotation')
    rr = np.array([[r[f'm{i}{j}'] for j in (1, 2, 3)] for i in (1, 2, 3)]) if r is not None else np.identity(3)
    q = quat_from_matrix(rr.T)
    # NaN read as 0: dr_xaphan.nif's second 'Bip01 R / L Shield' nodes hold NaN translations (written as NaN the glTF
    # would not load)
    return np.nan_to_num(np.array([t['x'], t['y'], t['z']], float)), q / np.linalg.norm(q), float(b.get('scale', 1.0))


def animation_tables():
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    anims = list(csv.reader(io.StringIO(g['anims.csv'].decode('latin1'))))
    header = anims[1]
    figures = {r[0]: r for r in anims[2:] if r and r[0].strip().isdigit()}
    files = {r[0]: r for r in csv.reader(io.StringIO(g['animnifs.csv'].decode('latin1'))) if r and r[0].strip().isdigit()}
    return header, figures, files


def clip_files(figure_id, slots=('Idle', 'Walk', 'Run', 'Back', 'Crawl', 'Jump', 'Land', 'Slide Left', 'Slide Right')):
    """{slot: (kfa filename, playback speed)} for one anims.csv figure row. animnifs.csv gives each clip's playback
    fps next to its authored fps (column ' fps', 15): idle plays at 4 of 15 (30 frames = 7.5 s in the client), swim
    at 6, landing at 20. Speed = fps / authored fps."""
    header, figures, files = animation_tables()
    row = figures[str(figure_id)]
    out = {}
    for slot in slots:
        if slot in header:
            anim_id = row[header.index(slot)].strip()
            if anim_id in files and files[anim_id][2].strip():
                r = files[anim_id]
                try:
                    speed = float(r[4]) / float(r[5])
                except (ValueError, IndexError, ZeroDivisionError):
                    speed = 1.0
                try:                                       # the client plays 'frames' frames at the authored fps;
                    length = float(r[3]) / float(r[5])     # Catacombs kfa keys run past that (Jump A: 6 of 60)
                except (ValueError, IndexError, ZeroDivisionError):
                    length = None
                out[slot] = (r[2].strip(), speed, length)
    return out


def combat_clip_files(figure_id, slots):
    """{slot: (kfa, speed, length)} from gamedata canims.csv (the combat set: Att Med / Low / High, C-Idle, Flinch,
    2H / staff / pole variants, parry, evade - same row ids as anims.csv), plus {slot: hit seconds}: animnifs.csv
    'hit time' (frames) is when the swing lands."""
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    rows = list(csv.reader(io.StringIO(g['canims.csv'].decode('latin1'))))
    header = rows[1]
    row = next((r for r in rows[2:] if r and r[0].strip() == str(figure_id)), None)
    _, _, files = animation_tables()
    out, hits = {}, {}
    if row is None:
        return out, hits
    for slot in slots:
        if slot not in header or header.index(slot) >= len(row):
            continue
        anim_id = row[header.index(slot)].strip()
        if anim_id in files and files[anim_id][2].strip():
            r = files[anim_id]
            try:
                speed = float(r[4]) / float(r[5])
            except (ValueError, IndexError, ZeroDivisionError):
                speed = 1.0
            try:
                length = float(r[3]) / float(r[5])
            except (ValueError, IndexError, ZeroDivisionError):
                length = None
            out[slot] = (r[2].strip(), speed, length)
            try:
                hits[slot] = float(r[7]) / float(r[5]) / speed if float(r[7]) > 0 else None
            except (ValueError, IndexError, ZeroDivisionError):
                hits[slot] = None
    return out, hits


def trim(tracks, duration, length):
    """Cut every track at the client's clip length (animnifs frames / authored fps)."""
    if not length or length >= duration:
        return tracks, duration
    out = {}
    for bone, tr in tracks.items():
        out[bone] = {}
        for key, (times, vals) in tr.items():
            keep = times <= length + 1e-4
            keep[0] = True
            out[bone][key] = (times[keep], vals[keep])
    return out, length


# A pose file (flex weapons' idle, I_1FW_M.kfa: controllers without keyframe data) holds the pose in the kfa's own
# bone nodes - the n-th node after the root for the n-th bone named. Off by default (build_flex_weapons turns it on).
KFA_STATIC_POSES = False


def read_clip(kfa_name, bone_names):
    """{bone name: {'rot': (times, quats xyzw), 'pos': (times, xyz), 'scale': (times, s)}}, duration."""
    path = next(p for p in (CLIENT / 'anims').iterdir() if p.name.lower() == kfa_name.lower())
    nif = read_nif(path.read_bytes())
    root = nif.blocks[0]
    extras, ctrls = [], []
    e = root.get('extra_data')                             # NetImmerse 4.x: linked chain
    while e is not None:
        extras.append(e)
        e = e.get('next_extra_data')
    if not extras:                                         # Gamebryo 10.x (Catacombs Cat_*.kfa): a list
        extras = [x for x in (root.get('extra_data_list') or []) if x is not None and x.type == 'NiStringExtraData']
    c = root.get('controller')
    while c is not None:
        ctrls.append(c)
        c = c.get('next_controller')
    tracks, duration = {}, 0.0
    pose_nodes = [b for b in nif.blocks if b.type == 'NiNode'][1:]
    for k, (ex, ctl) in enumerate(zip(extras, ctrls)):
        sd = str(ex['string_data']).strip()
        try:
            bone = bone_names[int(sd)]                     # bone index into figures/animnode.dat
        except ValueError:
            bone = sd                                      # or the bone name itself
        except IndexError:
            continue
        duration = max(duration, float(ctl.get('stop_time', 0.0)))
        kd = ctl.get('data')
        if kd is None:
            if KFA_STATIC_POSES and k < len(pose_nodes):          # the pose node's own transform, one key
                t, q, _s = node_trs(pose_nodes[k])
                tracks[bone] = {'rot': (np.zeros(1, np.float32), np.asarray([q], np.float32)),
                                'pos': (np.zeros(1, np.float32), np.asarray([t], np.float32))}
            continue
        tr = {}
        rtype = kd.get('rotation_type')
        if kd.get('num_rotation_keys') and rtype in (1, 2, 3):
            keys = kd['quaternion_keys']
            times = np.array([k['time'] for k in keys], np.float32)
            quats = np.array([[k['value']['x'], k['value']['y'], k['value']['z'], k['value']['w']] for k in keys], np.float32)
            for i in range(1, len(quats)):                 # keep neighbouring keys in the same hemisphere
                if np.dot(quats[i], quats[i - 1]) < 0:
                    quats[i] = -quats[i]
            tr['rot'] = (times, quats)
        elif rtype == 4:
            tr['rot'] = euler_track(kd)
        t = kd.get('translations') or {}
        if t.get('num_keys'):
            tr['pos'] = (np.array([k['time'] for k in t['keys']], np.float32),
                         np.array([[k['value']['x'], k['value']['y'], k['value']['z']] for k in t['keys']], np.float32))
        s = kd.get('scales') or {}
        if s.get('num_keys'):
            tr['scale'] = (np.array([k['time'] for k in s['keys']], np.float32),
                           np.array([k['value'] for k in s['keys']], np.float32))
        if tr:
            tracks[bone] = tr
    return tracks, duration


EMBEDDED_CLIP = '__embedded__'


def embedded_clip(nif):
    """The NIF's own animation as one clip: every node's NiKeyframeController, by node name (the siege catapult and
    trebuchet carry their throw in the model; no .kfa - owner 2026-10-14)."""
    tracks, duration = {}, 0.0
    for b in nif.blocks:
        if b.type != 'NiNode':
            continue
        c = b.get('controller')
        while c is not None and c.type != 'NiKeyframeController':
            c = c.get('next_controller')
        if c is None or c.get('data') is None:
            continue
        duration = max(duration, float(c.get('stop_time', 0.0)))
        kd = c['data']
        tr = {}
        rtype = kd.get('rotation_type')
        if kd.get('num_rotation_keys') and rtype in (1, 2, 3):
            keys = kd['quaternion_keys']
            times = np.array([k['time'] for k in keys], np.float32)
            quats = np.array([[k['value']['x'], k['value']['y'], k['value']['z'], k['value']['w']] for k in keys], np.float32)
            for i in range(1, len(quats)):
                if np.dot(quats[i], quats[i - 1]) < 0:
                    quats[i] = -quats[i]
            tr['rot'] = (times, quats)
        elif rtype == 4:
            tr['rot'] = euler_track(kd)
        t = kd.get('translations') or {}
        if t.get('num_keys'):
            tr['pos'] = (np.array([k['time'] for k in t['keys']], np.float32),
                         np.array([[k['value']['x'], k['value']['y'], k['value']['z']] for k in t['keys']], np.float32))
        if tr and b.get('name'):
            tracks[b.get('name')] = tr
    return tracks, duration


def euler_track(kd):
    """XYZ rotation keys (rotation type 4) sampled at 30 fps into quaternions."""
    groups = kd.get('xyz_rotations') or []
    times = sorted({k['time'] for g in groups for k in (g.get('keys') or [])}) or [0.0]
    ts = np.arange(times[0], times[-1] + 1e-6, 1 / 30.0)

    def sample(g, t):
        ks = g.get('keys') or []
        if not ks:
            return 0.0
        tt = [k['time'] for k in ks]
        vv = [k['value'] for k in ks]
        return float(np.interp(t, tt, vv))
    out = []
    for t in ts:
        ax, ay, az = (sample(g, t) for g in groups[:3])
        cx, sx, cy, sy, cz, sz = math.cos(ax / 2), math.sin(ax / 2), math.cos(ay / 2), math.sin(ay / 2), math.cos(az / 2), math.sin(az / 2)
        # rotation = Rz * Ry * Rx
        out.append([sx * cy * cz - cx * sy * sz, cx * sy * cz + sx * cy * sz, cx * cy * sz - sx * sy * cz, cx * cy * cz + sx * sy * sz])
    return ts.astype(np.float32), np.array(out, np.float32)


class Doc:
    def __init__(self):
        self.buf = bytearray()
        self.g = {'asset': {'version': '2.0', 'generator': 'MMOEngine nif_character'}, 'scene': 0,
                  'scenes': [{'nodes': [0]}], 'nodes': [], 'meshes': [], 'materials': [], 'textures': [], 'images': [],
                  'samplers': [{'magFilter': 9729, 'minFilter': 9987}], 'accessors': [], 'bufferViews': [],
                  'buffers': [], 'skins': [], 'animations': []}

    def acc(self, arr, comp, kind, target=None, minmax=False):
        while len(self.buf) % 4:
            self.buf.append(0)
        raw = arr.tobytes()
        view = {'buffer': 0, 'byteOffset': len(self.buf), 'byteLength': len(raw)}
        if target:
            view['target'] = target
        self.g['bufferViews'].append(view)
        self.buf += raw
        a = {'bufferView': len(self.g['bufferViews']) - 1, 'componentType': comp, 'count': len(arr), 'type': kind}
        if minmax:
            flat = arr.reshape(len(arr), -1)
            a['min'], a['max'] = flat.min(0).tolist(), flat.max(0).tolist()
        self.g['accessors'].append(a)
        return len(self.g['accessors']) - 1

    def save(self, path):
        self.g['buffers'] = [{'byteLength': len(self.buf),
                              'uri': 'data:application/octet-stream;base64,' + base64.b64encode(bytes(self.buf)).decode()}]
        Path(path).write_text(json.dumps(self.g))


def double_sided(*holders):
    """Whether the client draws a shape's back faces: a stencil property set to draw both sides, or an alpha property
    (fur cards, wings, see-through parts) on the shape or its node - otherwise one-sided like the client (owner
    2026-10-11: drawn double-sided, the wolf's mouth roof showed through its forehead)."""
    for h in holders:
        for p in (h.get('properties') or []) if h is not None else []:
            if p is None:
                continue
            if p.type == 'NiStencilProperty' and int(p.get('draw_mode', 0)) == 3:
                return True
            if p.type == 'NiAlphaProperty':
                return True
    return False


def quat_matrix(q):
    """Column-vector rotation matrix of a quaternion (x, y, z, w)."""
    x, y, z, w = (float(c) for c in q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def skinned_shapes(sp, by_name, parts):
    """(node, shape) of every skinned shape the build exports."""
    for part in parts:
        node = by_name[part.partition('@')[0]]
        for shape in [c for c in node.get('children') or [] if c is not None and sp.is_a(c.type, 'NiTriBasedGeom')]:
            if shape.get('skin_instance') is not None:
                yield node, shape


def skin_binds(sp, by_name, parts, world):
    """{id(bone): bind world} from the skin data - inv(bone_list skin transform) @ the shape's world - or None when the
    shapes disagree on a bone or a skin's own transform isn't the inverse of its shape's (a few ambient critters)."""
    binds = {}
    for node, shape in skinned_shapes(sp, by_name, parts):
        si = shape['skin_instance']
        sm = nif_static.local_matrix(shape) @ world[id(node)]
        if np.abs(nif_static.local_matrix(si['data']['skin_transform']) @ sm - np.identity(4)).max() > 0.05:
            return None
        for bone, bl in zip(si['bones'], si['data']['bone_list']):
            if bone is None or id(bone) not in world:
                continue
            g = np.linalg.inv(nif_static.local_matrix(bl['skin_transform'])) @ sm
            if id(bone) in binds and np.abs(binds[id(bone)] - g).max() > 0.05:
                return None
            binds[id(bone)] = g
    return binds


def bind_distortion(sp, nif, by_name, parts, world, bind, tracks):
    """Mean |log| change of the skinned mesh's edge lengths at a clip's first frame, skinned with the bind worlds
    `bind` - how far the clip stretches the mesh."""
    lower = {str(k).lower(): v for k, v in by_name.items() if k}
    pose = {}
    for bone, tr in tracks.items():
        nd = by_name.get(bone) or lower.get(str(bone).lower())
        if nd is None:
            continue
        t, q, s = node_trs(nd)
        if 'rot' in tr:
            q = tr['rot'][1][0]
        if 'pos' in tr and not borrowed_length(bone, tr['pos'][1], t):
            t = tr['pos'][1][0]
        if 'scale' in tr:
            s = float(np.asarray(tr['scale'][1]).reshape(-1)[0])
        m = np.identity(4)
        m[:3, :3] = quat_matrix(q).T * s
        m[3, :3] = t
        pose[id(nd)] = m
    posed = {}

    def walk(b, pm):
        m = pose.get(id(b), nif_static.local_matrix(b)) @ pm
        posed[id(b)] = m
        for c in b.get('children') or []:
            if c is not None and c.type == 'NiNode':
                walk(c, m)
    for r in nif.roots:
        if r is not None and r.type == 'NiNode':
            walk(r, np.identity(4))
    errs = []
    for node, shape in skinned_shapes(sp, by_name, parts):
        si, data = shape['skin_instance'], shape['data']
        v = np.asarray(data['vertices'], np.float64)[:, :3]
        rest = np.c_[v, np.ones(len(v))] @ (nif_static.local_matrix(shape) @ world[id(node)])
        acc, wsum = np.zeros((len(v), 4)), np.zeros(len(v))
        for bone, bl in zip(si['bones'], si['data']['bone_list']):
            if bone is None or id(bone) not in posed or id(bone) not in bind:
                continue
            mat = np.linalg.inv(bind[id(bone)]) @ posed[id(bone)]
            vws = bl.get('vertex_weights')
            for row in (vws if vws is not None else []):
                i, wt = int(row[0]), float(row[1])
                acc[i] += wt * (rest[i] @ mat)
                wsum[i] += wt
        pos = np.where((wsum > 0)[:, None], acc[:, :3] / np.maximum(wsum, 1e-9)[:, None], rest[:, :3])
        tri = nif_static.triangles_of(data).astype(int)
        for a, b in ((0, 1), (1, 2), (2, 0)):
            if len(tri) == 0:
                continue
            r0 = np.linalg.norm(rest[tri[:, a], :3] - rest[tri[:, b], :3], axis=1)
            r1 = np.linalg.norm(pos[tri[:, a]] - pos[tri[:, b]], axis=1)
            k = r0 > 1e-3
            errs.append(np.abs(np.log(np.maximum(r1[k], 1e-6) / r0[k])))
    return float(np.mean(np.concatenate(errs))) if errs else 0.0


def nodes_by_name(nif, index):
    """{name: NiNode}, the first of a name in tree order - the node the client's name lookup finds (a depth-first
    search) when a skeleton names two alike (ava_m_skeleton.nif: 'R Cloak Control01' and, misnamed, its child -
    the run's track went to the child, the right cloak panel's own control never moved and the legs went through
    it; owner 2026-10-20)."""
    out = {}
    for b in sorted((b for b in nif.blocks if b.type == 'NiNode' and id(b) in index), key=lambda b: index[id(b)]):
        out.setdefault(b.get('name'), b)
    return out


def build(model, parts, clips, out_path, texture_uri, file_bind=False):
    """parts: {node name[@uvset]: texture basename}; clips: {glTF animation name: kfa file}. file_bind: skin by the NIF's
    skin data where its bones were saved out of the skinned pose (see below)."""
    sp = spec()
    nif = read_nif((CLIENT / 'figures' / model).read_bytes())
    bone_names = (CLIENT / 'figures/animnode.dat').read_bytes().decode('latin1').split('\r\n')
    d = Doc()
    # root: Z-up NIF space -> Y-up, DAoC units -> metres
    d.g['nodes'].append({'name': model.rsplit('.', 1)[0], 'rotation': [-math.sqrt(0.5), 0.0, 0.0, math.sqrt(0.5)],
                         'scale': [M, M, M], 'children': []})
    index, world = {}, {}

    def add_node(b, parent_idx, parent_m):
        t, q, s = node_trs(b)
        local = np.nan_to_num(nif_static.local_matrix(b))  # row-vector matrix for bind-pose maths (NaN: node_trs)
        m = local @ parent_m
        i = len(d.g['nodes'])
        d.g['nodes'].append({'name': b.get('name') or f'node{i}', 'translation': t.tolist(), 'rotation': q.tolist(),
                             'scale': [s, s, s]})
        d.g['nodes'][parent_idx].setdefault('children', []).append(i)
        index[id(b)] = i
        world[id(b)] = m
        for c in b.get('children') or []:
            if c is not None and c.type == 'NiNode':
                add_node(c, i, m)

    for r in nif.roots:
        if r is not None and r.type == 'NiNode':
            add_node(r, 0, np.identity(4))
    by_name = nodes_by_name(nif, index)
    skeleton = [b for b in nif.blocks if b.type == 'NiNode' and (b.get('name') or '').startswith('Bip01')]
    # some monsters skin to helper bones outside Bip01 (Beetle: 'Dummy Head'): every skin bone is a joint too
    for b in nif.blocks:
        if sp.is_a(b.type, 'NiTriBasedGeom') and b.get('skin_instance') is not None:
            for bone in b['skin_instance']['bones']:
                if bone is not None and bone not in skeleton and id(bone) in index:
                    skeleton.append(bone)
    joint_of = {id(b): k for k, b in enumerate(skeleton)}
    # the inverse of a row-vector matrix, written row by row, is exactly the column-major layout glTF reads for
    # the column-vector inverse bind matrix (transposing here was the stretched-arms bug)
    bind = {id(b): world[id(b)] for b in skeleton}
    if file_bind:
        # Some monster NIFs were saved with their bones out of the pose the mesh was skinned in (owner 2026-10-20: the
        # Crafted Keeper - CWHuman.nif - has its arms straight out in a T while the mesh is skinned arms-down; bound to
        # the saved pose its idle / walk pulled the arms into long strands). The client skins by the skin data's own
        # bind (each bone's skin-to-bone transform) whatever pose the nodes were saved in. Used where that fits the
        # figure's own idle / walk clearly better and no worse (a few files' skin data is a little off the other way)
        binds = skin_binds(sp, by_name, parts, world)
        if binds and any(np.abs(g - bind[k]).max() > 0.05 for k, g in binds.items() if k in bind):
            filed = dict(bind)
            filed.update({k: g for k, g in binds.items() if k in bind})
            gains = []
            for anim_name in ('idle', 'walk'):
                kfa = clips.get(anim_name, (EMBEDDED_CLIP,))[0]
                if kfa == EMBEDDED_CLIP:
                    continue
                try:
                    tracks, _ = read_clip(kfa, bone_names)
                except Exception:
                    continue
                gains.append(bind_distortion(sp, nif, by_name, parts, world, bind, tracks)
                             - bind_distortion(sp, nif, by_name, parts, world, filed, tracks))
            if gains and min(gains) > -0.005 and max(gains) > 0.01:
                bind = filed
                print(f'  {model}: skin-data bind (idle / walk stretch down {", ".join(f"{g:.3f}" for g in gains)})')
    ibm = np.stack([np.linalg.inv(bind[id(b)]) for b in skeleton]).astype(np.float32)
    skin = {'joints': [index[id(b)] for b in skeleton], 'skeleton': index[id(skeleton[0])],
            'inverseBindMatrices': d.acc(ibm, 5126, 'MAT4')}
    d.g['skins'].append(skin)

    for part, tex in parts.items():
        name, _, uvs = part.partition('@')
        node = by_name[name]
        for shape in [c for c in node.get('children') or [] if c is not None and sp.is_a(c.type, 'NiTriBasedGeom')]:
            data = shape['data']
            if not data.get('uv_sets') or len(data['uv_sets']) <= int(uvs or 0):
                continue                                   # untextured helper geometry (Dem.NIF)
            v = np.asarray(data['vertices'], np.float64)[:, :3]
            w = world[id(node)] @ np.identity(4)
            sm = nif_static.local_matrix(shape) @ world[id(node)]
            pos = (np.c_[v, np.ones(len(v))] @ sm)[:, :3].astype(np.float32)
            nrm = (np.asarray(data['normals'], np.float64)[:, :3] @ sm[:3, :3])
            nrm = (nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)).astype(np.float32)
            uv = np.asarray(data['uv_sets'][int(uvs or 0)], np.float32)[:, :2]
            tri = nif_static.triangles_of(data).astype(np.uint32)
            joints = np.zeros((len(v), 4), np.uint16)
            weights = np.zeros((len(v), 4), np.float32)
            lists = [[] for _ in range(len(v))]
            si = shape['skin_instance']
            for bone, bl in zip(si['bones'], si['data']['bone_list']):
                vws = bl.get('vertex_weights')                # fixed-layout compound: rows of (index, weight)
                for row in (vws if vws is not None else []):
                    lists[int(row[0])].append((float(row[1]), joint_of[id(bone)]))
            for i, lst in enumerate(lists):
                lst = sorted(lst, reverse=True)[:4] or [(1.0, joint_of[id(by_name['Bip01'])])]
                total = sum(x for x, _ in lst)
                for k, (wt, j) in enumerate(lst):
                    joints[i, k], weights[i, k] = j, wt / total
            d.g['images'].append({'uri': f'{texture_uri}/{tex}.png'})
            d.g['textures'].append({'sampler': 0, 'source': len(d.g['images']) - 1})
            d.g['materials'].append({'name': tex, 'doubleSided': double_sided(shape, node),
                                     'pbrMetallicRoughness': {'baseColorTexture': {'index': len(d.g['textures']) - 1},
                                                              'metallicFactor': 0.0, 'roughnessFactor': 0.9}})
            attrs = {'POSITION': d.acc(pos, 5126, 'VEC3', 34962, True), 'NORMAL': d.acc(nrm, 5126, 'VEC3', 34962),
                     'TEXCOORD_0': d.acc(uv, 5126, 'VEC2', 34962), 'JOINTS_0': d.acc(joints, 5123, 'VEC4', 34962),
                     'WEIGHTS_0': d.acc(weights, 5126, 'VEC4', 34962)}
            # the shape's next UV set as TEXCOORD_1: a monster's glow map is laid out on it (owner 2026-10-20: the
            # abomination's - Glowing Demon Knight's - glowing eye sat on its chest armour; its glow map on set 1 covers
            # the helmet's eye slits, on set 0 the chest); build_monsters.add_look_maps points the glow at it
            nxt = int(uvs or 0) + 1
            if len(data['uv_sets']) > nxt and len(data['uv_sets'][nxt]) == len(v):
                attrs['TEXCOORD_1'] = d.acc(np.asarray(data['uv_sets'][nxt], np.float32)[:, :2], 5126, 'VEC2', 34962)
            d.g['meshes'].append({'name': name, 'primitives': [{'attributes': attrs, 'indices': d.acc(tri.reshape(-1), 5125, 'SCALAR', 34963),
                                                              'material': len(d.g['materials']) - 1}]})
            d.g['nodes'].append({'name': f'{name}_mesh', 'mesh': len(d.g['meshes']) - 1, 'skin': 0})
            d.g['nodes'][0]['children'].append(len(d.g['nodes']) - 1)

    report = {}
    # every clip keeps the body height its .kfa gives, as the client draws it (owner 2026-10-17: praying mantids floated
    # 1.1 m - their idle crouches the body to 40 units while the model's rest pose stands it at 68). This used to move
    # a clip whose root height was far from the rest height onto it - written for the necromancer's shade, whose
    # hover is its monsters.csv float height (applied by the game) - and it misplaced ~40 figures: mantids and lying
    # corpses lifted, cicadas, dragonflies, gryphon / amphitere mounts, terror birds and harpies pulled into the ground
    for anim_name, (kfa, speed, *rest) in clips.items():
        try:
            if kfa == EMBEDDED_CLIP:                       # the model's own keyframes (siege catapult / trebuchet)
                tracks, duration = embedded_clip(nif)
            else:
                tracks, duration = read_clip(kfa, bone_names)
            tracks, duration = trim(tracks, duration, rest[0] if rest else None)
        except Exception as e:                             # one clip that won't read (a file the client lacks, keys
            if anim_name in REQUIRED_CLIPS:                # for another skeleton): left out, the rest still built
                raise
            report[anim_name] = {'file': kfa, 'failed': f'{type(e).__name__}: {e}'}
            continue
        stretch = 1.0 / speed if speed > 0 else 1.0           # play at the client's rate
        channels, samplers = [], []
        matched = 0
        # bone names matched whatever their case: the clips name 'C Cloak Control01', the Kobold / Lurikeen male /
        # Valkyn female skeletons 'C cloak control01' - their cloak tracks were dropped and the cloak never moved with
        # the run, the legs going through it (owner 2026-10-15: "When running on a kobold ... the feet clip thru the cloak")
        lower = {str(k).lower(): v for k, v in by_name.items() if k}
        for bone, tr in tracks.items():
            node = by_name.get(bone) or lower.get(str(bone).lower())
            if node is None:
                continue
            matched += 1
            target = index[id(node)]
            for key, path in (('rot', 'rotation'), ('pos', 'translation'), ('scale', 'scale')):
                if key not in tr:
                    continue
                times, vals = tr[key]
                if key == 'pos' and borrowed_length(bone, vals, node_trs(node)[0]):
                    continue
                times = times * stretch
                if key == 'scale':
                    vals = np.repeat(vals[:, None], 3, axis=1)
                samplers.append({'input': d.acc(times, 5126, 'SCALAR', minmax=True),
                                 'output': d.acc(vals.astype(np.float32), 5126, {'rot': 'VEC4'}.get(key, 'VEC3')),
                                 'interpolation': 'LINEAR'})
                channels.append({'sampler': len(samplers) - 1, 'target': {'node': target, 'path': path}})
        d.g['animations'].append({'name': anim_name, 'channels': channels, 'samplers': samplers})
        report[anim_name] = {'file': kfa, 'bones': matched, 'of': len(tracks), 'seconds': round(duration * stretch, 2), 'speed': round(speed, 3)}
    d.save(out_path)
    return report


REQUIRED_CLIPS = ('idle', 'walk', 'run')

KEEP_POS = ('bip01', 'bip01 pelvis')


def borrowed_length(bone, vals, bind) -> bool:
    """A clip's translation that only holds another skeleton's bone length: unchanging through the clip and off the
    figure's own. An anim set shared by figures of other proportions (the Bonedancer commander plays a bigger
    skeleton's walk / run / attacks: its forearm 10.8 units out, its own 8.1 - owner 2026-10-15: "weird empty space"
    at the elbows and knees); the old client kept each figure's bones. Moving keys (a jaw, a tail, a cloak) stay, as
    do the root and pelvis (the body's height and sway)."""
    if str(bone).lower() in KEEP_POS or len(vals) == 0:
        return False
    v = np.asarray(vals, float)
    if float(np.ptp(v, axis=0).max()) > 0.01:
        return False
    return float(np.abs(v[0] - np.asarray(bind, float)).max()) > 0.02 * max(float(np.linalg.norm(bind)), 1.0)

def fig3_tables():
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    rows = lambda n: list(csv.reader(io.StringIO(g[n].decode('latin1'))))
    parts = {r[0]: r for r in rows('fig3parts.csv') if r and r[0].isdigit()}
    fmap = {}
    for r in rows('fig3map.csv'):
        if r and r[0].isdigit() and len(r) > 3:
            fmap[(int(r[0]), int(r[1]))] = [x.strip() for x in r[3:] if x.strip()]
    skins = {}
    for n in ('skins.csv', 'mskins.csv', 'pskins.csv'):
        skins[n] = {r[0]: r for r in rows(n) if r and r[0].isdigit()}
    tints = {r[0]: r for r in rows('fig3tints.csv') if r and r[0].isdigit()}
    return g, rows, parts, fmap, skins, tints


def build_cata(skeleton, part_list, clips, out_path, texture_uri):
    """skeleton: (archive, nif name); part_list: [(archive, nif name, texture basename, (r, g, b) tint 0..1[, node
    name])] - the optional node name lets the engine find and swap a part (every hair style is its own node).
    Parts are skinned to bones by NAME (each part's NiStringsExtraData lists its skin bones in order); the skeleton's
    rest pose is the parts' bind pose (checked: every bone's skin transform rebuilds the same vertex position)."""
    sp = spec()
    sk = read_nif(read_mpk(str(CLIENT / 'figures/fig3' / skeleton[0]))[skeleton[1]])
    bone_names = (CLIENT / 'figures/animnode.dat').read_bytes().decode('latin1').split('\r\n')
    d = Doc()
    d.g['nodes'].append({'name': skeleton[1].rsplit('.', 1)[0], 'rotation': [-math.sqrt(0.5), 0.0, 0.0, math.sqrt(0.5)],
                         'scale': [M, M, M], 'children': []})
    index, world = {}, {}

    def add_node(b, parent_idx, parent_m):
        t, q, sc = node_trs(b)
        m = nif_static.local_matrix(b) @ parent_m
        i = len(d.g['nodes'])
        d.g['nodes'].append({'name': b.get('name') or f'node{i}', 'translation': t.tolist(), 'rotation': q.tolist(),
                             'scale': [sc, sc, sc]})
        d.g['nodes'][parent_idx].setdefault('children', []).append(i)
        index[id(b)] = i
        world[id(b)] = m
        for c in b.get('children') or []:
            if c is not None and c.type == 'NiNode':
                add_node(c, i, m)

    for r in sk.roots:
        if r is not None and r.type == 'NiNode':
            add_node(r, 0, np.identity(4))
    by_name = nodes_by_name(sk, index)
    skeleton_nodes = [b for b in sk.blocks if b.type == 'NiNode' and (b.get('name') or '').startswith('Bip01')]
    joint_of = {b.get('name'): k for k, b in enumerate(skeleton_nodes)}
    ibm = np.stack([np.linalg.inv(world[id(b)]) for b in skeleton_nodes]).astype(np.float32)
    d.g['skins'].append({'joints': [index[id(b)] for b in skeleton_nodes], 'skeleton': index[id(skeleton_nodes[0])],
                         'inverseBindMatrices': d.acc(ibm, 5126, 'MAT4')})
    textures = set()
    for archive, nif_name, tex, tint, *node_name in part_list:
        nif_name, _, uv_set = nif_name.partition('@')      # 'Cloak01_cel_m.nif@1': the part's texture coordinates set
        n = read_nif(read_mpk(str(CLIENT / 'figures/fig3' / archive))[nif_name.lower()])
        names = next((b.get('data') for b in n.blocks if b.type == 'NiStringsExtraData'), [])

        def shapes_under(b, parent_m):
            m = nif_static.local_matrix(b) @ parent_m
            if int(b.get('flags', 0)) & 1:
                return                                     # hidden (Sar_f_Head01 in fig014 carries 10 hidden boxes)
            if re.fullmatch(r'box\d*', (b.get('name') or '').strip().lower()):
                return                                     # 3ds Max helper boxes left in hig_f_Hair02 (not hidden)
            if sp.is_a(b.type, 'NiTriBasedGeom'):
                yield b, m
            for c in b.get('children') or []:
                if c is not None:
                    yield from shapes_under(c, m)
        for r in n.roots:
            for shape, sm in shapes_under(r, np.identity(4)):
                data = shape['data']
                v = np.asarray(data['vertices'], np.float64)[:, :3]
                pos = (np.c_[v, np.ones(len(v))] @ sm)[:, :3].astype(np.float32)
                nrm = np.asarray(data['normals'], np.float64)[:, :3] @ sm[:3, :3]
                nrm = (nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)).astype(np.float32)
                uvs = data.get('uv_sets') or []
                k = int(uv_set or 0) if len(uvs) > int(uv_set or 0) else 0
                uv = (np.asarray(uvs[k], np.float32)[:, :2] if len(uvs) and len(uvs[k])
                      else np.zeros((len(v), 2), np.float32))         # a part without texture coordinates
                tri = nif_static.triangles_of(data).astype(np.uint32)
                joints = np.zeros((len(v), 4), np.uint16)
                weights = np.zeros((len(v), 4), np.float32)
                lists = [[] for _ in range(len(v))]
                si = shape.get('skin_instance')
                if si is not None:
                    for bi, bl in enumerate(si['data']['bone_list']):
                        bname = names[bi] if bi < len(names) else None
                        if bname is None:                  # name list shorter than the bone list (hig_f_Hair02):
                            bones = si.get('bones') or []  # the skin instance's own bone node carries the name
                            bname = bones[bi].get('name') if bi < len(bones) and bones[bi] is not None else None
                        if bname not in joint_of:
                            print('  unknown bone', nif_name, bname)
                            continue
                        vws = bl.get('vertex_weights')
                        for row in (vws if vws is not None else []):
                            lists[int(row[0])].append((float(row[1]), joint_of[bname]))
                for i, lst in enumerate(lists):
                    lst = sorted(lst, reverse=True)[:4] or [(1.0, joint_of['Bip01'])]
                    total = sum(x for x, _ in lst)
                    for k, (wt, j) in enumerate(lst):
                        joints[i, k], weights[i, k] = j, wt / total
                textures.add(tex)
                d.g['images'].append({'uri': f'{texture_uri}/{tex.lower()}.png'})
                d.g['textures'].append({'sampler': 0, 'source': len(d.g['images']) - 1})
                d.g['materials'].append({'name': tex, 'doubleSided': True,
                                         'pbrMetallicRoughness': {'baseColorTexture': {'index': len(d.g['textures']) - 1},
                                                                  'baseColorFactor': [*tint, 1.0],
                                                                  'metallicFactor': 0.0, 'roughnessFactor': 0.9}})
                attrs = {'POSITION': d.acc(pos, 5126, 'VEC3', 34962, True), 'NORMAL': d.acc(nrm, 5126, 'VEC3', 34962),
                         'TEXCOORD_0': d.acc(uv, 5126, 'VEC2', 34962), 'JOINTS_0': d.acc(joints, 5123, 'VEC4', 34962),
                         'WEIGHTS_0': d.acc(weights, 5126, 'VEC4', 34962)}
                if uv_set and len(uvs) > 1:
                    # a part drawn on another set ('@1': the cloaks) keeps the rest in order as TEXCOORD_1 / _2 - a cloak's
                    # set 0 (the guild emblem's pattern bands) and set 2 (its emblem patch): Godot's UV2 and CUSTOM0
                    # (Fig3Figure's cloak emblem; owner 2026-10-17)
                    others = [i for i in range(len(uvs)) if i != k and len(uvs[i]) == len(v)][:2]
                    for n_, i in enumerate(others, start=1):
                        attrs[f'TEXCOORD_{n_}'] = d.acc(np.asarray(uvs[i], np.float32)[:, :2], 5126, 'VEC2', 34962)
                mname = nif_name.rsplit('.', 1)[0]
                prim = {'attributes': attrs, 'indices': d.acc(tri.reshape(-1), 5125, 'SCALAR', 34963),
                        'material': len(d.g['materials']) - 1}
                mesh = {'name': mname, 'primitives': [prim]}
                # NiGeomMorpherController morph targets (relative). The NiStringExtraData "FM1=3 FM2=4 ..." gives each
                # target (FMn = target n) the client's feature code: heads 1/2 Nose, 3/4 Eyes, 5/6 Lips, 7/8 Jaw,
                # 9/10 Ears ... (each feature's pair in the race's fig3descriptions Morphs order), 21-24 Mood; hair
                # has HMn codes (hair squashed under helms). Blend shapes are named FMn / HMn, all at weight 0 (the
                # neutral face: figtofig3main's default slider value is 4 of 0..8); extras.codes carries the codes
                morph = next((b for b in n.blocks if b.type == 'NiMorphData'), None)
                label = next((str(b.get('string_data')) for b in n.blocks if b.type == 'NiStringExtraData'
                              and '=' in str(b.get('string_data'))), '')
                code_of, prefix = {}, 'morph'
                for line in label.replace('\r', '').split('\n'):
                    if '=' in line:
                        key, val = line.split('=', 1)
                        key = key.strip()
                        digits = key.lstrip('ABCDEFGHIJKLMNOPQRSTUVWXYZ')
                        if digits.isdigit() and val.strip().lstrip('-').isdigit():
                            prefix = key[:len(key) - len(digits)]
                            code_of[int(digits)] = int(val)
                if morph is not None and morph.get('num_vertices') == len(v):
                    targets, tnames, codes = [], [], []
                    for ti, mm in enumerate(morph.get('morphs') or []):
                        if ti == 0:
                            continue                               # target 0 is the base shape
                        vec = np.asarray(mm['vectors'], np.float64)[:, :3] @ sm[:3, :3]
                        targets.append({'POSITION': d.acc(vec.astype(np.float32), 5126, 'VEC3', 34962, True)})
                        tnames.append(f'{prefix}{ti}')
                        codes.append(code_of.get(ti, 0))
                    if targets:
                        prim['targets'] = targets
                        mesh['weights'] = [0.0] * len(targets)
                        mesh['extras'] = {'targetNames': tnames, 'codes': codes}
                d.g['meshes'].append(mesh)
                shape_no = sum(1 for nd in d.g['nodes'] if nd.get('name', '').split('__')[0] == (node_name[0] if node_name else ''))
                label = (node_name[0] + (f'__{shape_no}' if shape_no else '')) if node_name else f'{mname}_mesh'
                d.g['nodes'].append({'name': label,                     # a part's extra shapes: <part>__1, __2 ...
                                     'mesh': len(d.g['meshes']) - 1, 'skin': 0})
                d.g['nodes'][0]['children'].append(len(d.g['nodes']) - 1)
    report = {}
    for anim_name, (kfa, speed, *rest) in clips.items():
        tracks, duration = read_clip(kfa, bone_names)
        tracks, duration = trim(tracks, duration, rest[0] if rest else None)
        stretch = 1.0 / speed if speed > 0 else 1.0
        channels, samplers, matched = [], [], 0
        # bone names matched whatever their case: the clips name 'C Cloak Control01', the Kobold / Lurikeen male /
        # Valkyn female skeletons 'C cloak control01' - their cloak tracks were dropped and the cloak never moved with
        # the run, the legs going through it (owner 2026-10-15: "When running on a kobold ... the feet clip thru the cloak")
        lower = {str(k).lower(): v for k, v in by_name.items() if k}
        for bone, tr in tracks.items():
            node = by_name.get(bone) or lower.get(str(bone).lower())
            if node is None:
                continue
            matched += 1
            target = index[id(node)]
            for key, path in (('rot', 'rotation'), ('pos', 'translation'), ('scale', 'scale')):
                if key not in tr:
                    continue
                if key == 'pos' and bone != 'Bip01':
                    # rotations only below the root: the Catacombs clips are shared across races and some were keyed
                    # on other proportions (Cat_troll_idle puts the troll's eyelids 2.8 units off, cloak controls
                    # up to 34) - their translations would stretch the bones; every skeleton keeps its own lengths
                    continue
                times, vals = tr[key]
                times = times * stretch
                if key == 'scale':
                    vals = np.repeat(vals[:, None], 3, axis=1)
                samplers.append({'input': d.acc(times, 5126, 'SCALAR', minmax=True),
                                 'output': d.acc(vals.astype(np.float32), 5126, {'rot': 'VEC4'}.get(key, 'VEC3')),
                                 'interpolation': 'LINEAR'})
                channels.append({'sampler': len(samplers) - 1, 'target': {'node': target, 'path': path}})
        d.g['animations'].append({'name': anim_name, 'channels': channels, 'samplers': samplers})
        report[anim_name] = {'file': kfa, 'bones': matched, 'of': len(tracks), 'seconds': round(duration * stretch, 2),
                             'speed': round(speed, 3)}
    d.save(out_path)
    return report, textures


CATA_SLOTS = {'body': 1, 'legs': 3, 'boot': 4, 'arms': 5, 'glove': 6}     # texture-name word -> fig3map part slot
OBJECT_SKIN_COLS = {4: 'body', 6: 'arms', 7: 'glove', 9: 'legs', 10: 'boot'}  # objects.csv Skins columns (male)


def cata_outfit(rows, item_ids, gender='m'):
    """{fig3 part slot: (variant, texture)} for worn items, the way the client dresses Catacombs figures:
    objects.csv item -> its classic pskins skin per slot -> the pskins set named "Cata <same name>" -> that set's
    Catacombs texture for the slot, whose name carries the mesh variant (leaBody01 = body mesh 1, chnBody05 = 5,
    cthArms01 = arms 1, cthboot01 = boots 1)."""
    import re
    objects = {r[0]: r for r in rows('objects.csv') if r and r[0].isdigit()}
    pskins = {r[0]: r for r in rows('pskins.csv') if r and r[0].isdigit()}
    by_name = {}
    for r in pskins.values():
        by_name.setdefault(r[1].strip().lower(), []).append(r)
    out = {}
    for item in item_ids:
        r = objects.get(str(item))
        if r is None:
            print('unknown item', item)
            continue
        for col, word in OBJECT_SKIN_COLS.items():
            classic = pskins.get(r[col].strip()) if len(r) > col else None
            # a woman's body: the row's own female body skin (column 5, "Fem Body") when it has one - robes and other
            # sets name the female "Cata" texture there (Robe 1 Body female -> robBody01_02_f; owner 2026-10-11: a
            # female underhill zealot had no robe)
            if col == 4 and gender == 'f' and len(r) > 5 and r[5].strip() not in ('', '0') and r[5].strip() in pskins:
                classic = pskins[r[5].strip()]
            if classic is None:
                continue
            for c in by_name.get('cata ' + classic[1].strip().lower(), []):
                tex = c[2].rsplit('.', 1)[0]
                low = tex.lower()
                m = re.search(r'(body|arms|legs|boots?|gloves?)(\d\d)', low)
                if not m or not m.group(1).startswith(word[:4]):
                    continue
                if m.group(1) == 'body' and not low.endswith('_' + gender):
                    continue
                out[CATA_SLOTS[word]] = (int(m.group(2)), tex)
    return out


def bake_eyes(face_tex, eye_tex, out_dir):
    """The client multiplies the eye-colour texture (white except the iris, same UVs as the face) over the face."""
    from texture_index import TextureIndex
    idx = TextureIndex()
    face = idx.image(face_tex).convert('RGBA')
    eye = idx.image(eye_tex).convert('RGB').resize(face.size, Image.BILINEAR)
    f = np.asarray(face).astype(np.float32)
    e = np.asarray(eye).astype(np.float32) / 255.0
    f[..., :3] *= e
    name = f'{face_tex}__{eye_tex}'.lower()
    Image.fromarray(np.clip(f, 0, 255).astype(np.uint8)).save(out_dir / f'{name}.png')
    return name


def main_cata():
    """Catacombs Celt male (fig3map figure 33) in the client's own Celtic starter clothes ("Cata Celtic Cloth 1",
    pskins 4281-4285), face 1, hair style 1 in colour 5 (fig3haircolormap: cel_m_hair01_blonde + "Hair Tint 3" red,
    closest to the classic Celt's red hair), skin colour 1 (no tint). Body/boots/gloves variant 1 = cloth."""
    from texture_index import TextureIndex
    name = next((a.split('=', 1)[1] for a in sys.argv if a.startswith('--name=')), '')
    out = ROOT / ('godot/content/characters/celt_male_cata' + (f'_{name}' if name else ''))   # --name=chain -> a variant folder
    g, rows, parts, fmap, skins, tints = fig3_tables()
    fig = 33

    def part(slot, variant=1):
        pid = fmap[(fig, slot)][variant - 1]
        r = parts[pid]
        return f'fig{int(r[5]):03d}.mpk', r[2] + '.nif'
    tint = lambda tid: tuple(int(tints[str(tid)][k]) / 255.0 for k in (2, 3, 4))
    none = (1.0, 1.0, 1.0)
    hair = [r for r in rows('fig3haircolormap.csv') if r and r[0] == str(fig) and r[1] == '1'][0]
    colour = 5
    hair_tex = skins['skins.csv'][hair[3 + (colour - 1) * 3]][2].rsplit('.', 1)[0]
    hair_tint = tint(hair[4 + (colour - 1) * 3])
    face = [r for r in rows('fig3facemap.csv') if r and r[0].strip() == str(fig)][0]
    face_tex = skins['skins.csv'][face[2]][2].rsplit('.', 1)[0]
    cloth = {r[0]: r for r in rows('pskins.csv') if r and r[0].isdigit()}
    tex = lambda pid: cloth[pid][2].rsplit('.', 1)[0]
    eye_row = [r for r in rows('fig3eyecolormap.csv') if r and r[0].strip() == str(fig)][0]
    eye_tex = skins['skins.csv'][eye_row[2]][2].rsplit('.', 1)[0]            # eye colour 1
    (out / 'textures/original').mkdir(parents=True, exist_ok=True)
    face_eyes = bake_eyes(face_tex, eye_tex, out / 'textures/original')
    # clothing: empty slots wear the race's own Catacombs default clothes, skins.csv "cth<Part>01_SS_<race>" (the
    # Celt's green plaid shirt + blue trousers = the Catacombs counterpart of the classic NPC's c_cth0 set, monsters.csv
    # skins 277-283); --outfit=<objects.csv ids> dresses the figure in worn items on top
    race = 'cel'
    ss = {}
    for r in skins['skins.csv'].values():
        n = r[2].rsplit('.', 1)[0]
        low = n.lower()
        for word, slot in (('body', 1), ('legs', 3), ('boots', 4), ('arms', 5), ('gloves', 6)):
            if low == f'cth{word}01_ss_{race}' + ('_m' if word == 'body' else ''):
                ss[slot] = (1, n)
    slots = dict(ss)
    outfit = [int(x) for a in sys.argv if a.startswith('--outfit=') for x in a.split('=', 1)[1].split(',')]
    slots.update(cata_outfit(rows, outfit))
    part_list = [(*part(0), face_eyes, none)]                     # head (Briton head mesh, Celt face + eye colour)
    for slot in (1, 3, 4, 5, 6):                                  # body, legs, boots, arms, gloves
        variant, t = slots[slot]
        part_list.append((*part(slot, variant), t, none))
    part_list.append((*part(8), hair_tex, hair_tint))            # hair
    files = clip_files(265)                                         # anims.csv 265 = Catacombs Celt male (Cat_*_HM)
    names = {'Idle': 'idle', 'Walk': 'walk', 'Run': 'run', 'Back': 'walk_back', 'Crawl': 'swim', 'Jump': 'jump',
             'Land': 'land', 'Slide Left': 'strafe_left', 'Slide Right': 'strafe_right'}
    clips = {names[k]: v for k, v in files.items()}
    (out / 'textures/original').mkdir(parents=True, exist_ok=True)
    upscale = '--no-upscale' not in sys.argv
    tex_set = 'textures/x4plus' if upscale else 'textures/original'
    report, texs = build_cata(('sfig001.mpk', 'cel_m_skeleton.nif'), part_list, clips,
                              str(out / 'celt_animated.gltf'), tex_set)
    idx = TextureIndex()
    for t in sorted(texs):
        if (out / 'textures/original' / f'{t.lower()}.png').exists() and '__' in t:
            continue                                               # baked here (face x eye colour)
        img = idx.image(t)
        if img is None:
            print('missing texture', t)
            continue
        img.save(out / 'textures/original' / f'{t.lower()}.png')
    if upscale:                                     # characters are close-up: full Real-ESRGAN x4plus, no downscale
        from build_zone import upscale_dir
        upscale_dir(out / 'textures/original', out / 'textures/x4plus', factor_after=4)
    strides = {}
    for r in rows('monnifs.csv'):
        if len(r) > 7 and r[2].strip().lower() == 'cel_m':
            strides = {'run': float(r[4]), 'walk': float(r[5]), 'back': float(r[6]), 'strafe': float(r[7])}
            break
    meta = {'figure': 'cel_m', 'sound_figure': 'hceltm', 'fig3': fig, 'anim_set': 265, 'strides_units': strides,
            'outfit_items': outfit, 'eye_texture': eye_tex,
            'clip_seconds': {k: v['seconds'] for k, v in report.items()},
            'parts': [p[1] for p in part_list], 'textures': sorted(texs)}
    (out / 'celt_animated.json').write_text(json.dumps(meta, indent=1), encoding='utf-8')
    print(json.dumps(report, indent=1))
    print(json.dumps(meta, indent=1))


def main():
    out = ROOT / 'godot/content/characters/celt_male'
    parts = {'HeadA1@1': 'c_mhed1', 'Body1': 'c_cth0_body1m', 'LBody1': 'c_cth0_lbody1', 'Arms1': 'c_cth0_arm1',
             'Legs1': 'c_cth0_leg1', 'Gloves1': 'c_cth0_glov1', 'Boots1': 'c_cth0_boot1'}
    files = clip_files(48)                                  # anims.csv 48 = Celt/Elf Male
    names = {'Idle': 'idle', 'Walk': 'walk', 'Run': 'run', 'Back': 'walk_back', 'Crawl': 'swim', 'Jump': 'jump',
             'Land': 'land', 'Slide Left': 'strafe_left', 'Slide Right': 'strafe_right'}
    clips = {names[k]: v for k, v in files.items()}
    report = build('HCeltM.NIF', parts, clips, str(out / 'celt_animated.gltf'), 'textures/x4plus')
    # gamedata monnifs.csv gives the figure's stride per animation cycle (units moved per loop of Run / Walk / Back /
    # Strafe). The engine plays each clip at movement speed / (stride / cycle length) so feet don't slide.
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    strides = {}
    for r in csv.reader(io.StringIO(g['monnifs.csv'].decode('latin1'))):
        if len(r) > 7 and r[2].strip().lower() == 'hceltm':
            strides = {'run': float(r[4]), 'walk': float(r[5]), 'back': float(r[6]), 'strafe': float(r[7])}
            break
    meta = {'figure': 'hceltm', 'anim_set': 48, 'strides_units': strides,
            'clip_seconds': {k: v['seconds'] for k, v in report.items()}}
    (out / 'celt_animated.json').write_text(json.dumps(meta, indent=1), encoding='utf-8')
    print(json.dumps(report, indent=1))
    print(json.dumps(meta, indent=1))


if __name__ == '__main__':
    main_cata() if sys.argv[1:2] == ['cata'] else main()
