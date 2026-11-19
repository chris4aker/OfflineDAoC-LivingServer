"""Monster models for a zone's spawns (ZONE CONTENT PROCESS.md step 9).

The server's model number (old DB Mob.Model) indexes the client's gamedata monsters.csv:
  col 2 = monnifs.csv id (the figure: NIF name, anim set, strides), col 3.. = skins.csv ids for Body / Head / Arms /
  Gloves / LBody / Legs / Boots / Cloak / Helm, col 12 = scale %, col 15 = sound set.
Player-race figures (monnifs type 1: Celt, Elf, Lurikeen, Firbolg, Sylvan ...) are NOT built here - NPCs of those
races use the Catacombs fig3 figures (owner 2026-10-09). Everything else (wolves, beetles, brownies ...) is the
classic monster NIF, converted with nif_character.build (skinned glTF + the anim set's clips), one glTF per
monnifs figure + skin combination: content/monsters/<figure>__<skin>/model.gltf.

    python build_monsters.py --zone=200            # every monster model the zone's spawns use
    python build_monsters.py --pets                # every summoned pet's model (build_pets.py)
    python build_monsters.py --models=1207,1235    # listed model numbers (the flight mounts build_routes.py names)
Writes content/monsters/monsters.json: {model number: {figure, gltf, scale, strides, clips, race prefix or null}}."""
import csv, io, json, re, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
from netimmerse import read_nif, spec
import nif_character as nc
from texture_index import TextureIndex

OUT = ROOT / 'godot/content/monsters'
DB = ROOT / 'external/old_project/runtime/data/opendaoc.sqlite3.db'
SKIN_COLS = ['Body', 'Head', 'Arms', 'Gloves', 'LBody', 'Legs', 'Boots', 'Cloak', 'Helm']
CLIPS = {'Idle': 'idle', 'Walk': 'walk', 'Run': 'run', 'Back': 'walk_back', 'Crawl': 'swim', 'Jump': 'jump',
         'Land': 'land', 'Slide Left': 'strafe_left', 'Slide Right': 'strafe_right'}
CLIPS['Death'] = 'death'
COMBAT_CLIPS = {'C-Idle': 'combat_idle', 'Att Med': 'attack_med', 'Att Low': 'attack_low', 'Att High': 'attack_high',
                'Flinch': 'flinch'}
# The weapon stances (owner 2026-10-15: "bone archers don't pull out and use bows they just swing a sword at range"): a
# figure whose canims row gives the bow / staff / two-hander / polearm their own clips (the Bonedancer minions' skeleton
# uses the human ones) gets them under the race figures' names (build_races COMBAT) - Npc._stance_clip and the
# server's BowPrepare / BowShoot play them. A column naming a clip already built (a wolf's 2H = its bite) is skipped.
WEAPON_CLIPS = {'C-Idle 2H': 'combat_idle_2h', 'Att 2H Med': 'attack_2h_med', 'Att 2H High': 'attack_2h_high',
                'Att 2H Low': 'attack_2h_low', 'C-Idle Staff': 'combat_idle_staff', 'Att Staff-M': 'attack_staff_med',
                'Att Staff H': 'attack_staff_high', 'Att Staff L': 'attack_staff_low', 'C-Idle Pole': 'combat_idle_pole',
                'Att Pole Med': 'attack_pole_med', 'Att Pole H': 'attack_pole_high', 'Att Pole L': 'attack_pole_low',
                'C-Idle Bow': 'combat_idle_bow', 'Bow Ready': 'bow_ready', 'Bow Aim': 'bow_aim', 'Bow Fire': 'bow_fire'}
# ...and every other column the race figures build (owner 2026-10-15: "all clips need to be proper for npcs, companion
# bots, gamebots, player, and monsters so nothing is incorrect or using the wrong clip"): parries / evades / blocks,
# left hand and dual wield, crossbow and thrown, thrust, hand to hand, scythe, flex, and the styles' own clips - a
# humanoid monster's row (dark elves, gnolls, the later races built here) names them, an animal's mostly repeats its
# bite, which the kfa check skips
def _all_weapon_clips():
    from build_races import COMBAT_CLIPS as RACE, style_clips
    every = {**RACE, **style_clips()[0]}
    return {**WEAPON_CLIPS, **{k: v for k, v in every.items() if k not in COMBAT_CLIPS and k not in WEAPON_CLIPS}}


WEAPON_CLIPS = _all_weapon_clips()
# Casting (owner 2026-10-13: "Necromancer pets are not using their spells in the right animations all I see is hands
# glowing"): anims.csv gives every monster anim set a clip for each casting-style column too - the zombie magician
# undead_sp_st / mid / end, the ghastly healer the banshee's SPA / B / C, the necroservants RD_Cast_A / B / C, the
# animist mushrooms Mushroom_spellA / B / C, the rock elementals idle then attack. The few real clips are built once
# ('cast_k_<kfa>', '..._mid' for a style's middle so it loops while casting) and every style's clip name maps to one
# of them (monsters.json 'cast_clips': {"cast_bk_hand_start": "cast_k_rd_cast_a", ...}; an existing clip is reused).
CAST_SLOTS = ['Cast Start', 'Cast Mid', 'Cast End', '1H Circ Start', '1H Circ Mid', '1H Circ End', '2H Wax Start',
              '2H Wax Mid', '2H Wax End', 'Upraised Start', 'Upraised Mid', 'Upraised End', 'Bk-Hand Start', 'Bk-Hand Mid',
              'Bk-Hand End', 'Stamp Start', 'Stamp Mid', 'Stamp End', 'Crossed Start', 'Crossed Mid', 'Crossed End',
              'Z Start', 'Z Mid', 'Z End']                  # (no instrument poses: pets never play songs)


_ANIMS = None


def anim_files():
    """The client's animation files (lower-case names)."""
    global _ANIMS
    if _ANIMS is None:
        _ANIMS = {p.name.lower() for p in (CLIENT / 'anims').iterdir()}
    return _ANIMS


def cast_clips(anim_set, clips):
    """The anim set's casting clips added to `clips` (deduplicated by kfa file); returns {style clip name: clip}."""
    from build_races import cast_clip
    try:
        files = nc.clip_files(int(anim_set), tuple(CAST_SLOTS))
    except KeyError:
        return {}
    have = {v[0].lower(): name for name, v in clips.items()}      # a cast column naming the idle / an attack: reuse it
    mids = {v[0].lower() for k, v in files.items() if 'mid' in k.lower() or k.rstrip().endswith('2')}
    alias = {}
    for slot, v in files.items():
        kfa = v[0].lower()
        name = have.get(kfa)
        if name is None:
            stem = re.sub(r'[^a-z0-9]+', '_', kfa.rsplit('.', 1)[0]).strip('_')
            name = 'cast_k_' + stem + ('_mid' if kfa in mids else '')
            clips[name] = v
            have[kfa] = name
        alias[cast_clip(slot)] = name
    return alias
# player-race classic figures -> the Catacombs (fig3) prefix NPCs of that race use
RACE_FIGURES = {'hceltm': 'cel_m', 'hceltf': 'cel_f', 'helfm': 'elf_m', 'helff': 'elf_f', 'hlurikeenm': 'lur_m',
                'hlurikeenf': 'lur_f', 'hfirbolgm': 'fir_m', 'hfirbolgf': 'fir_f', 'hsylvanm_2': 'syl_m',
                'hsylvanf_2': 'syl_f', 'bbritonm': 'bri_m', 'bbritonf': 'bri_f', 'nnorsem': 'nor_m', 'nnorsef': 'nor_f',
                # Valkyn NPCs (the vetted DB uses them - Hrodny, Myrkwood's Bonedancer trainer, the Valkyn shopkeepers):
                # their race figure, so their outfits dress them (owner 2026-10-11: her texture was wrong)
                'nvalkynf_2': 'val_f', 'nvalkynm_2': 'val_m',
                # Albion / Midgard (the 1.65 races)
                'britonm1': 'bri_m', 'bcommonm': 'bri_m', 'bcommonf': 'bri_f', 'bavalonm': 'ava_m', 'bavalonf': 'ava_f',
                'bhilandm': 'hig_m', 'bhilandf': 'hig_f', 'bsaracenm': 'sar_m', 'bsaracenf': 'sar_f',
                'binconnum_2': 'inc_m', 'binconnuf_2': 'inc_f', 'nvikingm': 'nor_m', 'nvikingf': 'nor_f',
                'ndwarfm': 'dwa_m', 'ndwarff': 'dwa_f', 'nkoboldm': 'kob_m', 'nkoboldf': 'kob_f',
                'ntrollm': 'tro_m', 'ntrollf': 'tro_f',
                # the later races (build_races.py builds their Catacombs figures; owner 2026-10-16: Elizabeth <Smith>)
                'bhalfogrem': 'hal_m', 'bhalfogref': 'hal_f', 'nsvartalfm': 'fro_m', 'nsvartalff': 'fro_f',
                'hsharm': 'sha_m', 'hsharf': 'sha_f'}


def table(name):
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    rows = list(csv.reader(io.StringIO(g[name].decode('latin1'))))
    return {r[0].strip(): r for r in rows[2:] if r and r[0].strip().isdigit()}


def zone_models(zone_id):
    import sqlite3
    from build_zone import zone_section
    sec = dict(line.split('=', 1) for line in zone_section(zone_id).splitlines() if '=' in line)
    x0, y0 = int(sec['region_offset_x']) * 8192, int(sec['region_offset_y']) * 8192
    x1, y1 = x0 + int(sec['width']) * 8192, y0 + int(sec['height']) * 8192
    db = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
    models = {str(m) for (m,) in db.execute('select Model from Mob where Region=? and X>=? and X<? and Y>=? and Y<?',
                                            (int(sec['region']), x0, x1, y0, y1))}
    # templates with ReplaceMobValues pick their model from a list (build_npcs.py 'models'): build those too
    npcs = ROOT / f'godot/content/zones/zone{zone_id:03d}/npcs.json'
    if npcs.exists():
        for s in json.loads(npcs.read_text(encoding='utf-8')).get('spawns', []):
            models.update(str(m) for m in s.get('models', []))
    # quest event monsters (build_quests.py: a step's spawn, its own model or its NpcTemplate's model list)
    quests = ROOT / 'godot/content/quests/quests.json'
    if quests.exists():
        for q in json.loads(quests.read_text(encoding='utf-8')).get('quests', {}).values():
            for st in q.get('steps', []):
                sp = st.get('spawn')
                if not sp or int(sp.get('zone', -1)) != zone_id:
                    continue
                if int(sp.get('model', 0) or 0) > 0:
                    models.add(str(sp['model']))
                tm = str((sp.get('npc_template') or {}).get('model') or '')
                models.update(m.strip() for m in tm.replace(',', ';').split(';') if m.strip().isdigit())
    return sorted(models)


def pet_models():
    """Every model a summoned pet can have (build_pets.py -> content/classes/pets.json)."""
    pets = json.loads((ROOT / 'godot/content/classes/pets.json').read_text(encoding='utf-8'))['templates']
    return sorted({str(m) for t in pets.values() for m in t['models']})


def float_units(mr):
    """monsters.csv "min float height" / "max float height" (cols 32 / 33, world units): the client lifts the figure
    by a random height between them (game.dll entity+0x44, WORLD IMPORT LESSONS) - pixies, ghosts, mylings, wisps,
    the necromancer's shade; sea creatures swim at theirs; a few sink a little (Drakoran -9). [] when 0 / 0."""
    try:
        lo, hi = float(mr[32] or 0), float(mr[33] or 0)
    except (ValueError, IndexError):
        return []
    return [lo, hi] if lo or hi else []


def figure_alpha(mr, nif_name, skin_png):
    """How the client draws a figure's skin: 'cut' (alpha test - skeleton ribs, harpy feathers: monsters.csv Body
    Alpha 2, or the NIF's own alpha test), 'blend_depth' (Body Alpha 1 - treant leaves, djinn smoke), 'blend' (the
    NIF's own alpha blend - ghosts; or a skin with see-through texels and no setting at all - the necromancer's shade,
    99% clear, which the server only sends as a model change), '' opaque. Owner 2026-10-13: the shade had no
    transparency - every figure skin was drawn opaque."""
    body = (mr[39].strip() if len(mr) > 39 else '')
    if body == '2':
        return 'cut'
    if body == '1':
        return 'blend_depth'
    if nif_name:
        nif = read_nif((CLIENT / 'figures' / nif_name).read_bytes())
        for b in (nif.blocks if hasattr(nif, 'blocks') else nif):
            if b.type == 'NiAlphaProperty':
                f = int(b.get('flags') or 0)
                if f & 1:
                    return 'blend'
                if f & 0x200:
                    return 'cut'
    if skin_png is not None and skin_png.exists():
        import numpy as np
        from PIL import Image
        a = np.asarray(Image.open(skin_png).convert('RGBA'))[..., 3]
        if float((a < 128).mean()) > 0.02:
            return 'blend'
    return ''


# monsters.csv per part: its skin column and its two multi-texture columns ("Multi", "Multi2"); a part the NIF names
# otherwise (Scene Root, a cart, a tail) takes the body's
PART_COLS = {'body': (3, (27, 53)), 'head': (4, (37, 54)), 'arms': (5, (36, 55)), 'gloves': (6, (50, 56)),
             'lbody': (7, (52, 57)), 'legs': (8, (41, 59)), 'boots': (9, (51, 58))}
# skins.csv MULTI: 2 dark map (multiplied over the skin), 3 glow (lit by itself), 4 gloss (shiny where light),
# 5 environment map, 6 bump (the env map's ripple), 7 detail (modulated 2x). 5 and 6 are the client's chrome
# reflection: the engine's own sky reflections stand in for them on the glossy parts
DARK, GLOW, GLOSS, DETAIL = '2', '3', '4', '7'


def part_key(node_name):
    k = str(node_name).lower()
    return k if k in PART_COLS else 'body'


def _skin_name(row):
    return row[2].strip().rsplit('.', 1)[0].lower()


def part_looks(mr, skins, parts):
    """{part node: {'base', 'dark', 'detail', 'glow', 'gloss', 'tex'}} - each part's own skin (owner 2026-10-14: every
    part wore the body skin - a hydra's head, a sphinx's face, a fomorian king's arms) and its multi textures; 'tex' is
    the drawn texture's name (the skin with its dark / detail maps baked in). The Cabalist's simulacrums: a tiling
    marble times giantdark.tga - the face, runes and muscles (owner 2026-10-14: "Jade Simulacrum ... lacking any
    details")."""
    def cell(i):
        v = mr[i].strip() if i < len(mr) else ''
        return skins.get(v) if v not in ('', '0') else None
    out = {}
    for p in parts:
        col, mcols = PART_COLS[part_key(p)]
        row = cell(col)
        if row is None:                                  # no skin of its own: the body's skin and maps
            col, mcols = PART_COLS['body']
            row = cell(col)
        look = {'base': _skin_name(row), 'dark': None, 'detail': None, 'glow': None, 'gloss': None}
        for c in mcols:
            m = cell(c)
            kind = m[3].strip() if m else ''
            slot = {DARK: 'dark', DETAIL: 'detail', GLOW: 'glow', GLOSS: 'gloss'}.get(kind)
            if slot and look[slot] is None:
                look[slot] = _skin_name(m)
        look['tex'] = '__'.join([look['base']] + [look[k] for k in ('dark', 'detail') if look[k]])
        out[p] = look
    return out


def body_tex(looks, body_row):
    """The drawn body texture: the body part's, else the first part's, else (no skinned part) the body skin's name."""
    for pn, lk in looks.items():
        if part_key(pn) == 'body':
            return lk['tex']
    return next(iter(looks.values()))['tex'] if looks else _skin_name(body_row)


def look_key(fig, body_tex, looks):
    """The model folder: figure__body texture, plus a short hash when parts differ or glow / gloss maps come with it
    (one folder per drawn combination)."""
    import hashlib
    plain = all(lk['tex'] == body_tex and not lk['glow'] and not lk['gloss'] for lk in looks.values())
    key = f'{fig.lower()}__{body_tex}'
    if not plain:
        sig = repr(sorted((p, tuple(sorted(lk.items()))) for p, lk in looks.items()))
        key += '__' + hashlib.md5(sig.encode()).hexdigest()[:6]
    return key


def bake_multi(base, other, scale):
    """The skin times another map on the same UVs, at the larger size: a dark map (x1) or a detail map (x2)."""
    import numpy as np
    from PIL import Image
    size = (max(base.width, other.width), max(base.height, other.height))
    b = np.asarray(base.convert('RGBA').resize(size, Image.LANCZOS)).astype(np.float32)
    d = np.asarray(other.convert('RGB').resize(size, Image.LANCZOS)).astype(np.float32) / 255.0
    b[..., :3] *= d * scale
    return Image.fromarray(np.clip(b, 0, 255).astype(np.uint8), 'RGBA')


def gloss_map(img):
    """A gloss map as glTF metallic-roughness: rough (0.9) where it is black, smooth (0.25) where it is white - shine
    on the gold, the armour; never metallic (the colour stays the skin's)."""
    import numpy as np
    from PIL import Image
    g = np.asarray(img.convert('L')).astype(np.float32) / 255.0
    out = np.zeros(g.shape + (3,), np.uint8)
    out[..., 0] = 255
    out[..., 1] = np.clip((0.9 - 0.65 * g) * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(out, 'RGB')


def write_look_textures(model, looks, idx, d):
    from build_zone import save_if_changed
    done = set()
    for lk in looks.values():
        if lk['tex'] not in done:
            done.add(lk['tex'])
            img = idx.image(lk['base'])
            if img is None:
                print(model, 'missing texture', lk['base'])
            else:
                for slot, scale in (('dark', 1.0), ('detail', 2.0)):
                    if lk[slot]:
                        m = idx.image(lk[slot])
                        if m is None:
                            print(model, 'missing', slot, 'map', lk[slot])
                        else:
                            img = bake_multi(img, m, scale)
                save_if_changed(img, d / 'textures/original' / f"{lk['tex']}.png")   # unchanged: no new x4plus pass
        for slot in ('glow', 'gloss'):
            name = lk[slot]
            if name and (slot, name) not in done:
                done.add((slot, name))
                m = idx.image(name)
                if m is None:
                    print(model, 'missing', slot, 'map', name)
                elif slot == 'glow':
                    save_if_changed(m.convert('RGB'), d / 'textures/original' / f'{name}.png')
                else:
                    save_if_changed(gloss_map(m), d / 'textures/original' / f'{name}__mr.png')


def add_look_maps(gltf_path, looks, texture_uri):
    """The glow map as the material's emission, the gloss map as its roughness (by material: the texture's name)."""
    g = json.loads(Path(gltf_path).read_text(encoding='utf-8'))
    by_tex = {lk['tex']: lk for lk in looks.values()}
    changed = False

    def texture(name):
        g['images'].append({'uri': f'{texture_uri}/{name}.png'})
        g['textures'].append({'sampler': 0, 'source': len(g['images']) - 1})
        return len(g['textures']) - 1
    for mat in g.get('materials', []):
        lk = by_tex.get(mat.get('name'))
        if not lk:
            continue
        if lk['glow']:
            mat['emissiveTexture'] = {'index': texture(lk['glow'])}
            # on the shape's second UV set when it has one (nif_character.build: TEXCOORD_1) - the client lays the glow
            # map out there (the Glowing Demon Knight's eye slits); NpcManager turns this into emission_on_uv2
            mi = g['materials'].index(mat)
            if any(pr.get('material') == mi and 'TEXCOORD_1' in pr.get('attributes', {})
                   for me in g.get('meshes', []) for pr in me.get('primitives', [])):
                mat['emissiveTexture']['texCoord'] = 1
            mat['emissiveFactor'] = [1.0, 1.0, 1.0]
            changed = True
        if lk['gloss']:
            pbr = mat.setdefault('pbrMetallicRoughness', {})
            pbr['metallicRoughnessTexture'] = {'index': texture(lk['gloss'] + '__mr')}
            pbr['roughnessFactor'] = 1.0
            pbr['metallicFactor'] = 0.0
            changed = True
    if changed:
        Path(gltf_path).write_text(json.dumps(g), encoding='utf-8')


def table_fields(out_meta, mon):
    """The client-table fields of every listed model (float heights, skin alpha) - no rebuild: --table-fields."""
    for model, entry in out_meta.items():
        mr = mon.get(str(int(model) & 0x7FFF))
        if mr is None:
            continue
        fl = float_units(mr)
        entry.pop('float_units', None)
        if fl:
            entry['float_units'] = fl
        entry.pop('alpha', None)
        if entry.get('gltf') and 'race' not in entry:
            d = OUT / entry['gltf'].replace('res://content/monsters/', '').rsplit('/', 1)[0]
            pngs = sorted((d / 'textures/original').glob('*.png'))
            body_png = d / 'textures/original' / f"{entry.get('skin', '')}.png"
            a = figure_alpha(mr, find_figure(entry['figure']), body_png if body_png.exists() else (pngs[0] if pngs else None))
            if a:
                entry['alpha'] = a


def find_figure(name):
    want = name.lower() + '.nif'
    for p in (CLIENT / 'figures').iterdir():
        if p.name.lower() == want:
            return p.name
    return None


def skinned_parts(nif_name):
    """Names of the NiNodes holding skinned geometry (the parts nif_character.build exports)."""
    sp = spec()
    nif = read_nif((CLIENT / 'figures' / nif_name).read_bytes())
    out = []
    for b in nif.blocks:
        if b.type != 'NiNode':
            continue
        for c in b.get('children') or []:
            if c is not None and sp.is_a(c.type, 'NiTriBasedGeom') and c.get('skin_instance') is not None \
                    and not (int(c.get('flags', 0)) & 1):
                if b.get('name') not in out:
                    out.append(b.get('name'))
    return out


EFFECT_COL = 40                                           # monsters.csv 'Effect' (an effect set)
EFX = OUT / 'effects'


def monster_effects(mr, sets, effs, nifs):
    """The client's monster effect chain (glows, flames, wisps): monsters.csv Effect -> moneffectsets.csv (up to 6
    effects) -> moneffects.csv (NIF num, attach node, max visible range, AutoScale) -> moneffectnifs.csv filename.
    [{'nif': name, 'attach': node, 'range_m': m, 'autoscale': bool}]"""
    eset = mr[EFFECT_COL].strip() if len(mr) > EFFECT_COL else ''
    out = []
    if not eset.isdigit() or eset == '0' or eset not in sets:
        return out
    for e in sets[eset][2:8]:
        e = e.strip()
        if e.isdigit() and e != '0' and e in effs and effs[e][2].strip() in nifs:
            er = effs[e]
            rng = float(er[4]) if len(er) > 4 and er[4].strip() else 4000.0
            out.append({'nif': nifs[er[2].strip()][2].strip().lower(), 'attach': er[3].strip(),
                        'range_m': round(rng * 0.025, 2), 'autoscale': len(er) > 7 and er[7].strip() == '1'})
    return out


# Textures an effect NIF names that this client doesn't have, drawn with the nearest client texture instead (owner
# 2026-10-10; listed in "1.65 EXCEPTIONS MADE.txt"): Albion's Spirit Ruin hit names greyflar - every other flare
# in the series is coloured (blu / grn / orn / red / cold), and dim_greyflame is the client's only grey one.
TEXTURE_SUBSTITUTES = {'greyflar': 'dim_greyflame'}


def build_effect(base, idx, effect_files):
    """One effect NIF (app/effects) -> content/monsters/effects/<base>/model.gltf (meshes, particle systems as glTF
    extras) and its textures. False when the client lacks it."""
    import nif_static
    from build_zone import save_if_changed
    from PIL import Image
    import numpy as np
    d = EFX / base
    if (d / 'model.gltf').exists() and '--reconvert-effects' not in sys.argv:   # re-run the NIF converter on all
        return True
    src = effect_files.get(base + '.nif')
    if src is None:
        print('missing effect', base)
        return False
    (d / 'textures/original').mkdir(parents=True, exist_ok=True)
    info = nif_static.convert(src.read_bytes(), str(d / 'model.gltf'), 'textures/original', node_anims=True)
    for n in sorted(info['textures']):
        doubled = n.endswith(('__d2', '__x2'))   # __x2: the raw detail map (detail_x2.py)
        key = n[:-4] if doubled else n
        img = idx.image(key) or nif_static.EMBEDDED.get(key) or (idx.image(TEXTURE_SUBSTITUTES[key])
                                                                 if key in TEXTURE_SUBSTITUTES else None)
        if img is None:
            print('effect', base, 'missing texture', n)
            continue
        if doubled and n.endswith('__d2'):
            rgb = np.asarray(img.convert('RGB'), dtype=np.float32) * 2.0
            img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
        save_if_changed(img, d / 'textures/original' / f'{n}.png')
    print('effect', base, info['particles'] if 'particles' in info else '', 'particles')
    return True


def main():
    zone = int(next((a.split('=')[1] for a in sys.argv if a.startswith('--zone=')), 200))
    mon, nifs, skins = table('monsters.csv'), table('monnifs.csv'), table('skins.csv')
    out_meta = json.loads((OUT / 'monsters.json').read_text()) if (OUT / 'monsters.json').exists() else {}
    idx = TextureIndex()
    idx.figures_first = True                             # a monster's skin is a figure skin (not a same-named housing /
                                                         # dungeon / item texture)
    built = {}
    built_cast = {}                                      # figure key -> its cast_clips (style clip -> built clip)
    sets, effs, enifs = table('moneffectsets.csv'), table('moneffects.csv'), table('moneffectnifs.csv')
    effect_files = {p.name.lower(): p for p in (CLIENT / 'effects').iterdir()}
    new_effects = set()
    if '--table-fields' in sys.argv:                     # every model already listed: float heights + skin alpha
        table_fields(out_meta, mon)
        (OUT / 'monsters.json').write_text(json.dumps(out_meta, indent=1), encoding='utf-8')
        print(sum(1 for e in out_meta.values() if e.get('float_units')), 'floating,',
              sum(1 for e in out_meta.values() if e.get('alpha')), 'with skin alpha')
        return
    if '--effects-only' in sys.argv:                     # every model already listed: its effects only
        for model, entry in out_meta.items():
            mr = mon.get(str(int(model) & 0x7FFF))
            if mr is None:
                continue
            fx = [e for e in monster_effects(mr, sets, effs, enifs) if build_effect(e['nif'], idx, effect_files)]
            for e in fx:
                e['gltf'] = f"res://content/monsters/effects/{e['nif']}/model.gltf"
                new_effects.add(e['nif'])
            if fx:
                entry['effects'] = fx
        for base in sorted(new_effects):
            if '--no-upscale' not in sys.argv:
                from build_zone import upscale_dir
                d = EFX / base
                upscale_dir(d / 'textures/original', d / 'textures/x4plus', factor_after=4)
        (OUT / 'monsters.json').write_text(json.dumps(out_meta, indent=1), encoding='utf-8')
        print(sum(1 for e in out_meta.values() if e.get('effects')), 'models with effects,', len(new_effects), 'effects')
        return
    # model folder -> the anim set it holds: that of the lowest model number listed in it (see the key below)
    owners = {}
    for m in sorted(out_meta, key=lambda x: (int(x) & 0x7FFF, int(x))):
        mr = mon.get(str(int(m) & 0x7FFF))
        nr = nifs.get(mr[2].strip()) if mr else None
        folder = str(out_meta[m].get('gltf', '')).rsplit('/', 2)[-2] if out_meta[m].get('gltf') else ''
        if folder and nr is not None:
            owners.setdefault(folder, nr[3].strip())
    listed = next((a.split('=')[1].split(',') for a in sys.argv if a.startswith('--models=')), None)   # e.g. route mounts
    for model in (listed if listed else pet_models() if '--pets' in sys.argv else zone_models(zone)):
        mr = mon.get(str(int(model) & 0x7FFF))           # the server sets a high flag bit on some (32891 = 0x8000 | 123)
        if mr is None:
            print('model', model, 'not in monsters.csv')
            continue
        nr = nifs.get(mr[2].strip())
        if nr is None:
            print('model', model, 'figure', mr[2], 'not in monnifs.csv')
            continue
        fig = nr[2].strip()
        entry = {'name': mr[1], 'figure': fig, 'scale': float(mr[12] or 100) / 100.0,
                 'sound_set': int(mr[15] or 0) if mr[15].strip().isdigit() else 0,
                 'strides_units': {k: float(nr[i] or 0) for k, i in (('run', 4), ('walk', 5), ('back', 6), ('strafe', 7))}}
        fx = [e for e in monster_effects(mr, sets, effs, enifs) if build_effect(e['nif'], idx, effect_files)]
        for e in fx:                                      # glows / flames / wisps the client attaches to the model
            e['gltf'] = f"res://content/monsters/effects/{e['nif']}/model.gltf"
        if fx:
            entry['effects'] = fx
        # a player-race figure: drawn as our Catacombs race figure - one we have no figure for (the later races the
        # server's NPCs use: Half Ogre, Shar, Svartalf - owner 2026-10-15: Mularn's Elizabeth <Smith> was invisible)
        # is built below from its classic figure and skins like any model
        if fig.lower() in RACE_FIGURES:
            entry['race'] = RACE_FIGURES.get(fig.lower())
            out_meta[model] = entry
            continue
        nif_name = find_figure(fig)
        body = skins.get(mr[3].strip())
        if nif_name is None or body is None:
            entry['missing'] = 'figure file' if nif_name is None else 'skin %s' % mr[3]
            out_meta[model] = entry
            print(model, mr[1], 'MISSING', entry['missing'])
            continue
        looks = part_looks(mr, skins, skinned_parts(nif_name))
        if fig.lower() == 'cabalpet':
            # the Cabalist's simulacrums: the marble and giantdark.tga sit on different UV sets (the marble tiles on
            # the first, giantdark covers the body once on the second) - baked on one UV set the marble came out
            # stretched and grainy. Written here as giantdark alone, then pipeline/bake_cabalpet.py lays the marble
            # in on its own coordinates (owner 2026-10-17: the old client's amber simulacrum is tan-tinted stone)
            for lk in looks.values():
                if lk['dark']:
                    lk['base'], lk['dark'] = lk['dark'], None
        tex = body_tex(looks, body)
        key = look_key(fig, tex, looks)
        # the clips are the model's own anim set (monnifs column 3): two models drawn alike but animated by different
        # sets get a folder each (owner 2026-10-19: Snow Wolves died standing - the "puppy" pets, the same wolf figure
        # and snow skin on anim set 221 whose Death is the idle, were built into wolf__wolf_snow after the Snow Wolf's
        # set 7 with its wlf_deth; also the living Undead Minotaur in the dead one's set, Fomorians / Red Caps with and
        # without weapons). The folder's owner is its lowest model number; another set gets "__a<set>".
        anim_set = nr[3].strip()
        if owners.setdefault(key, anim_set) != anim_set:
            key += f'__a{anim_set}'
        entry['gltf'] = f'res://content/monsters/{key}/model.gltf'
        entry['skin'] = tex
        if key not in built:
            d = OUT / key
            (d / 'textures/original').mkdir(parents=True, exist_ok=True)
            parts = {p: lk['tex'] for p, lk in looks.items()}
            if fig.lower() == 'cabalpet':
                # giantdark sits on the mesh's second UV set (0..1 over the body); the first is the marble's tiling
                # one (-1.5..2.5) - on it giantdark repeated all over (owner 2026-10-16)
                parts = {f'{p}@1': t for p, t in parts.items()}
            # (an anim set the client doesn't list - set 0 on some later models: no clips, the figure stands)
            try:
                files = nc.clip_files(int(nr[3]), tuple(CLIPS)) if nr[3].strip().isdigit() else {}
            except KeyError:
                files = {}
            clips = {CLIPS[k]: v for k, v in files.items() if k in CLIPS}
            if nr[3].strip().isdigit():                  # melee: canims.csv (attack, combat idle, flinch)
                try:
                    combat, _ = nc.combat_clip_files(int(nr[3]), tuple(COMBAT_CLIPS))
                except KeyError:
                    combat = {}
                clips.update({COMBAT_CLIPS[k]: v for k, v in combat.items()})
                try:
                    weapon, _ = nc.combat_clip_files(int(nr[3]), tuple(WEAPON_CLIPS))
                except KeyError:
                    weapon = {}
                kfas = {v[0].lower() for v in clips.values()}
                for k, v in weapon.items():
                    if v[0].lower() not in kfas:
                        clips[WEAPON_CLIPS[k]] = v
                        kfas.add(v[0].lower())
            # a clip whose .kfa the client doesn't ship is left out (owner 2026-10-29, grindylows invisible: model 923
            # "On Stomach Legless Zombie 1", anim set 136 names ZomW_run1.kfa - only ZomW_run.kfa exists - and the
            # missing file failed the whole model; the client draws it without the clip, NPCs walk instead of run)
            for c, v in list(clips.items()):
                if str(v[0]).lower() not in anim_files():
                    print(model, mr[1], 'clip', c, v[0], 'not in the client: left out')
                    clips.pop(c)
            built_cast[key] = cast_clips(nr[3], clips) if nr[3].strip().isdigit() else {}
            try:
                try:
                    report = nc.build(nif_name, parts, clips, str(d / 'model.gltf'), 'textures/original', file_bind=True)
                except Exception:
                    extra = [c for c in clips if c in WEAPON_CLIPS.values()]
                    if not built_cast.get(key) and not extra:
                        raise
                    # its cast / weapon clips don't fit this figure (a set borrowed from another skeleton): built
                    # without them
                    for c in set(built_cast.get(key, {}).values()):
                        if c.startswith('cast_k_'):
                            clips.pop(c, None)
                    for c in extra:
                        clips.pop(c, None)
                    built_cast[key] = {}
                    report = nc.build(nif_name, parts, clips, str(d / 'model.gltf'), 'textures/original', file_bind=True)
            except Exception as e:
                print(model, mr[1], 'BUILD FAILED', type(e).__name__, repr(e))
                entry['missing'] = f'build failed: {type(e).__name__} {e}'
                out_meta[model] = entry
                continue
            write_look_textures(model, looks, idx, d)
            if fig.lower() == 'cabalpet':
                import bake_cabalpet                       # the marble on its own UV set (see above)
                bake_cabalpet.main()
            add_look_maps(d / 'model.gltf', looks, 'textures/original')
            built[key] = {k: v['seconds'] for k, v in report.items() if 'seconds' in v} if isinstance(report, dict) else {}
            failed = sorted(k for k, v in report.items() if 'failed' in v) if isinstance(report, dict) else []
            if failed:
                print('  clips left out:', failed, report[failed[0]]['failed'])
            print('built', key, sorted(built[key]))
        entry['clip_seconds'] = built[key]
        if built_cast.get(key):
            entry['cast_clips'] = built_cast[key]
        out_meta[model] = entry
    # owner 2026-10-09: nothing goes in without the high-quality pass - 4x Real-ESRGAN x4plus for every character /
    # NPC / monster texture (2x is for environments), one file at a time (folder mode swapped names)
    if '--no-upscale' not in sys.argv:
        from build_zone import upscale_dir
        for key in sorted(built):
            d = OUT / key
            upscale_dir(d / 'textures/original', d / 'textures/x4plus', factor_after=4)
            g = (d / 'model.gltf').read_text(encoding='utf-8').replace('"textures/original/', '"textures/x4plus/')
            (d / 'model.gltf').write_text(g, encoding='utf-8')
    table_fields(out_meta, mon)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'monsters.json').write_text(json.dumps(out_meta, indent=1), encoding='utf-8')
    print(len(out_meta), 'model numbers,', len(built), 'monster models built')


if __name__ == '__main__':
    main()
