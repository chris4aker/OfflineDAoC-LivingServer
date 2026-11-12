"""Every 1.65 (Classic + Shrouded Isles) player race and gender as a Catacombs (fig3) figure with ALL of the client's
character-creation options (owner 2026-10-09: "get the models ready for the other 1.65 classes and all customization
options too").

    python build_races.py                  # all 30 figures -> godot/content/characters/races/<fig>/
    python build_races.py cel_m tro_f      # only these
    python build_races.py --no-upscale     # skip Real-ESRGAN (textures/original only)

Per figure: figure.gltf (skeleton, the Catacombs animations, the race's default "SS" starter clothes, the head with
its face-slider morph targets, and EVERY hair style as its own node "hair_<n>") and figure.json (every option, below).
Textures are shared by all figures: godot/content/characters/fig3_textures/{original,x4plus} (x4plus = Real-ESRGAN
at 4x: characters are viewed close up).

Where everything comes from (gamedata.mpk; game.dll addresses are the client code that reads it):
  monnifs.csv  'Cata <Race> <Gender>' row: name prefix (cel_m), anim set (anims.csv), strides, fig3 index,
               skeleton archive (figures/fig3/sfig00<n>.mpk <prefix>_skeleton.nif)
  monsters.csv the race's player rows (Cata Nif = that monnifs id): CatSkin defaults per slot (the SS starter
               clothes), Cata Tint Arms / hands / Legs (which parts take the skin tint; 0x417938), Cata Scale (%)
  fig3map / fig3parts      part meshes: slot 0 head, 1 body, 3 legs, 4 boots, 5 arms, 6 gloves, 8 hair styles
  fig3descriptions         labels: faces, hair styles (incl. "Bald" = the style without a mesh), tattoos, the four
                           face sliders (Nose / Eyes / Lips / Jaw / Ears ... per race)
  fig3facemap              7 faces (head textures)            fig3eyecolormap   8 eye colours (multiplied over face)
  fig3haircolormap         per style x 16 colours: texture + fig3tints tint
  fig3skincolormap         8 skin tints (fig3tints rgb)        fig3decalmap      tattoos (Celt 5, Troll 15)
  Size: Short x0.95 / Average x1.0 / Tall x1.05 of the Cata Scale (game.dll 0x450527)
  skins.csv 'Tint Chan': 'a' = the skin tint only where the texture's alpha is high (bare hands / arms under the
               clothing); the right copy of each texture is the one in the mskins archive its BaseMap names
               (figures/Mskins/mskinNNN.mpk) - other archives hold copies without that alpha mask
Open (recorded in figure.json 'open'): which Mood targets (codes 21-24) the Mood slider drives.
"""
import csv, io, json, re, sys, collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT, ROOT
import nif_character as nc

OUT = ROOT / 'godot/content/characters'
TEX = OUT / 'fig3_textures'
# (realm, race, male fig3 index, female fig3 index) - the 1.65 Classic + SI races, then the server's later NPC races
RACES = [('alb', 'Briton', 1, 2), ('alb', 'Avalonian', 9, 10), ('alb', 'Highlander', 13, 14), ('alb', 'Saracen', 3, 4),
         ('alb', 'Inconnu', 23, 24),
         ('mid', 'Norse', 15, 16), ('mid', 'Troll', 31, 32), ('mid', 'Dwarf', 21, 22), ('mid', 'Kobold', 19, 20),
         ('mid', 'Valkyn', 27, 28),
         ('hib', 'Celt', 33, 34), ('hib', 'Firbolg', 29, 30), ('hib', 'Elf', 11, 12), ('hib', 'Lurikeen', 17, 18),
         ('hib', 'Sylvan', 35, 36),
         # the later races the server's NPCs use (owner 2026-10-16: Elizabeth <Smith> and the other Half Ogre / Frostalf
         # / Shar NPCs drawn as one classic model with one skin over every part): their Catacombs figures, so their
         # outfits dress them as every other race's - build them alone with `build_races.py hal_m hal_f fro_m ...`
         ('alb', 'Half Ogre', 25, 26), ('mid', 'Frostalf', 5, 6), ('hib', 'Shar', 7, 8)]
SLOTS = {'body': 1, 'lbody': 2, 'legs': 3, 'boots': 4, 'arms': 5, 'gloves': 6}     # lbody = the Highlander kilt
CATSKIN = {'body': 68, 'lbody': 69, 'arms': 71, 'gloves': 72, 'legs': 73, 'boots': 74}     # monsters.csv columns
SIZES = {'Short': 0.95, 'Average': 1.0, 'Tall': 1.05}
CLIPS = {'Idle': 'idle', 'Walk': 'walk', 'Run': 'run', 'Back': 'walk_back', 'Crawl': 'swim', 'Jump': 'jump',
         'Land': 'land', 'Slide Left': 'strafe_left', 'Slide Right': 'strafe_right', 'Death': 'death',
         'Cast Start': 'cast_start', 'Cast Mid': 'cast_mid', 'Cast End': 'cast_end', 'Sit Down': 'sit_down',
         'Sit Idle': 'sit_idle', 'Stand Up': 'stand_up', 'Ride Horse': 'ride_horse',   # stable horse routes
         'Ride Griffon': 'ride_griffon', 'Ride Amphitere': 'ride_amphitere', 'Ride Dragonfly': 'ride_dragonfly',   # SI flights
         'Ladder': 'ladder'}                          # climbing (Cat_CL_H_up.kfa "climb up"; owner 2026-10-10 ladders)


def cast_clip(column_name):
    """A casting / instrument column of anims.csv -> its clip name: 'Crossed Start' -> 'cast_crossed_start',
    'Lute1' -> 'cast_lute1' ('Cast Start' stays 'cast_start'). spells.csv Anim1-3 name these columns
    (column index = anim number - 1); build_spell_fx.py uses the same names."""
    import re as _re
    slug = _re.sub(r'[^a-z0-9]+', '_', column_name.strip().lower()).strip('_')
    return slug if slug.startswith('cast_') else 'cast_' + slug


# every 1.65 casting style and instrument pose (the Warlock / Zealot columns are later classes)
CAST_COLUMNS = ['1H Circ Start', '1H Circ Mid', '1H Circ End', '2H Wax Start', '2H Wax Mid', '2H Wax End', 'Upraised Start',
                'Upraised Mid', 'Upraised End', 'Bk-Hand Start', 'Bk-Hand Mid', 'Bk-Hand End', 'Stamp Start', 'Stamp Mid',
                'Stamp End', 'Crossed Start', 'Crossed Mid', 'Crossed End', 'Z Start', 'Z Mid', 'Z End',
                'Lute1', 'Lute2', 'Lute3', 'drums 1', 'drums 2', 'drums 3', 'Flute1', 'Flute2', 'Flute3']
CLIPS.update({c: cast_clip(c) for c in CAST_COLUMNS})
# melee (gamedata canims.csv, the figure's combat row): attack mode stance, swings, getting hit, parry / evade
COMBAT_CLIPS = {'C-Idle': 'combat_idle', 'Att Med': 'attack_med', 'Att Low': 'attack_low', 'Att High': 'attack_high',
                'Flinch': 'flinch', 'C-Idle 2H': 'combat_idle_2h', 'Att 2H Med': 'attack_2h_med',
                'Att 2H High': 'attack_2h_high', 'Att 2H Low': 'attack_2h_low', 'C-Idle Staff': 'combat_idle_staff',
                'Att Staff-M': 'attack_staff_med', 'Att Staff H': 'attack_staff_high', 'Att Staff L': 'attack_staff_low',
                'C-Idle Pole': 'combat_idle_pole', 'Att Pole Med': 'attack_pole_med', 'Att Pole H': 'attack_pole_high',
                'Att Pole L': 'attack_pole_low', 'Parry 1H L': 'parry_low', 'Parry 1H H': 'parry_high',
                'Evade 1H': 'evade',
                # the defences of the other stances, shield blocks, bows / crossbows / thrown, left hand and dual wield
                'Parry Pole L': 'parry_pole_low', 'Parry Pole H': 'parry_pole_high', 'Evade Pole': 'evade_pole',
                'Parry 2H L': 'parry_2h_low', 'Parry 2H H': 'parry_2h_high', 'Evade 2H': 'evade_2h',
                'Block L': 'block_low', 'Block H': 'block_high', 'C-Idle Bow': 'combat_idle_bow',
                'Bow Ready': 'bow_ready', 'Bow Aim': 'bow_aim', 'Bow Fire': 'bow_fire', 'Xbow Idle': 'xbow_idle',
                'Xbow Ready': 'xbow_ready', 'Xbow Aim': 'xbow_aim', 'Xbow Fire': 'xbow_fire',
                'Throw Ready': 'throw_ready', 'Throw Aim': 'throw_aim', 'Throw Fire': 'throw_fire',
                'Att Left M': 'attack_left_med', 'Att Left L': 'attack_left_low', 'Att Left H': 'attack_left_high',
                'Att Dual M': 'attack_dual_med', 'Att Dual L': 'attack_dual_low', 'Att Dual H': 'attack_dual_high',
                # hand to hand (objects.csv stance 13: the Savage's claws / greaves - owner 2026-10-15: "fist weapons
                # are held the wrong way and the attack animation is wrong") and the scythe (stance 14, Valewalker):
                # their own combat idle and swings, the fist / blade toward the enemy
                'H2H Cidle': 'combat_idle_h2h', 'H2H Att M': 'attack_h2h_med', 'H2H Att H': 'attack_h2h_high',
                'H2H Att L': 'attack_h2h_low', 'Scy C-Idle': 'combat_idle_scy', 'Scy Att M': 'attack_scy_med',
                'Scy Att H': 'attack_scy_high', 'Scy Att L': 'attack_scy_low',
                # flexible weapons (Reaver): objects.csv stances 9 / 10 / 11 (chains / flails / whips) in the order of
                # canims.csv's small / medium / large flex columns; their combat idle is the one-hand one (the client
                # has no flex C-Idle column)
                'Att FWS M': 'attack_fws_med', 'Att FWS H': 'attack_fws_high', 'Att FWS L': 'attack_fws_low',
                'Att FWM M': 'attack_fwm_med', 'Att FWM H': 'attack_fwm_high', 'Att FWM L': 'attack_fwm_low',
                'Att FWL M': 'attack_fwl_med', 'Att FWL H': 'attack_fwl_high', 'Att FWL L': 'attack_fwl_low',
                'Parry FWS': 'parry_fws', 'Parry FWM': 'parry_fwm', 'Parry FWL': 'parry_fwl',
                # thrust weapons (stance 7: dirks, rapiers, epees - the 1HT swings)
                '1HT med': 'attack_1ht_med', '1HT Low': 'attack_1ht_low', '1HT High': 'attack_1ht_high'}


def emote_slots():
    """The emotes (gamedata emotes.csv: id, description, anims.csv column - header index column + 2 -, spell overlay):
    {anims.csv header: 'emote_<id>'} for ids 1-46 (the classic emotes; 47 on are horse / later ones)."""
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    header = nc.animation_tables()[0]
    out = {}
    for r in csv.reader(io.StringIO(g['emotes.csv'].decode('latin1'))):
        if len(r) > 2 and r[0].strip().isdigit() and r[2].strip().isdigit():
            i, col = int(r[0]), int(r[2])
            if 1 <= i <= 46 and col > 0 and col + 2 < len(header):
                out[header[col + 2]] = f'emote_{i}'
    return out


def canims_header():
    from nif_character import read_mpk
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    return list(csv.reader(io.StringIO(g['canims.csv'].decode('latin1'))))[1]


def style_clips():
    """The style animations the 1.65 classes use (client styles.csv Animation n = canims.csv column n + 2, e.g.
    42 = 'Style 1HC 1'): {canims column: clip name}, and the map {animation id: clip name} for the game."""
    header = canims_header()
    styles = json.loads((OUT.parent / 'classes/styles.json').read_text(encoding='utf-8'))
    every = styles.values() if isinstance(styles, dict) else styles
    used = sorted({int(st.get(k, 0) or 0) for st in every for k in ('animation', 'animation_2h')})
    cols, by_id = {}, {}
    for a in used:
        if 0 <= a + 2 < len(header) and header[a + 2].strip():
            col = header[a + 2]
            clip = COMBAT_CLIPS.get(col) or re.sub(r'[^a-z0-9]+', '_', col.lower()).strip('_')
            cols[col] = clip
            by_id[a] = clip
    return cols, by_id


class Tables:
    def __init__(self):
        self.g = read_mpk(str(CLIENT / 'gamedata.mpk'))
        r = self.rows
        # first row per id wins: the client's fig3parts.csv ends with 12 junk lines reusing ids 189-200 (Briton female
        # hair 02 x6, Highlander male hair 03 x6) - read last-wins they replaced the Inconnu female's hair 1-4 and
        # hood cloaks and the Half Ogre male's cloaks / hair (owner 2026-10-29: Inconnu women bald under a circlet).
        # fig3map.csv names 191-194 as inc_f hair, whose real files (inc_f_Hair01..04) the client ships.
        self.parts = {}
        for x in r('fig3parts.csv'):
            if x and x[0].isdigit():
                self.parts.setdefault(x[0], x)
        self.fmap = {(int(x[0]), int(x[1])): [c.strip() for c in x[3:] if c.strip()]
                     for x in r('fig3map.csv') if x and x[0].isdigit() and len(x) > 3}
        self.desc = {(int(x[0]), int(x[1])): [c.strip() for c in x[3:] if c.strip()]
                     for x in r('fig3descriptions.csv') if x and x[0].isdigit()}
        self.skins = {x[0]: x for x in r('skins.csv') if x and x[0].isdigit()}
        self.mskins = {x[0]: x for x in r('mskins.csv') if x and x[0].isdigit()}
        self.tints = {x[0]: x for x in r('fig3tints.csv') if x and x[0].isdigit()}
        self.monnifs = [x for x in r('monnifs.csv') if x and x[0].strip().isdigit()]
        self.monsters = r('monsters.csv')[2:]
        self.by_fig = lambda name: {int(x[0].strip()): x for x in r(name) if x and x[0].strip().isdigit()}
        self.faces, self.eyes = self.by_fig('fig3facemap.csv'), self.by_fig('fig3eyecolormap.csv')
        self.skin_tones, self.decals = self.by_fig('fig3skincolormap.csv'), self.by_fig('fig3decalmap.csv')
        self.hair = {}                                   # (first row per key, as fig3parts)
        for x in r('fig3haircolormap.csv'):
            if x and x[0].isdigit() and x[1].isdigit():
                self.hair.setdefault((int(x[0]), int(x[1])), x)
        self._archives = {}

    def rows(self, name):
        return list(csv.reader(io.StringIO(self.g[name].decode('latin1'))))

    def tint(self, tid):
        t = self.tints.get(str(tid).strip())
        return [round(int(t[k]) / 255.0, 4) for k in (2, 3, 4)] if t else [1.0, 1.0, 1.0]

    def skin(self, sid):
        """skins.csv id -> {'tex': basename, 'chan': Tint Chan, 'archive': mskins archive or None}."""
        r = self.skins.get(str(sid).strip())
        if r is None:
            return None
        m = self.mskins.get(r[8].strip()) if len(r) > 8 else None
        return {'tex': r[2].rsplit('.', 1)[0], 'chan': r[9].strip() if len(r) > 9 else '',
                'archive': int(m[4]) if m and m[4].strip().isdigit() else None}

    def part(self, pid):
        r = self.parts.get(pid)
        return (f'fig{int(r[5]):03d}.mpk', r[2] + '.nif') if r else None

    def labelled_head(self, pid):
        """The head with its face-slider labels: fig3parts names fig014.mpk for some heads (bri_f, sar_f, val_f) whose
        copy there has the morph targets but no "FM1=3 ..." map; fig015.mpk carries the labelled ones."""
        arc, name = self.part(pid)
        for a in [arc] + [f'fig{i:03d}.mpk' for i in range(1, 30)]:
            p = CLIENT / 'figures/fig3' / a
            if not p.exists():
                continue
            if a not in self._archives:
                self._archives[a] = {k.lower(): v for k, v in read_mpk(str(p)).items()}
            raw = self._archives[a].get(name.lower())
            if raw and b'FM1=' in raw:
                return a, name
        return arc, name

    def texture_bytes(self, info):
        """The exact copy the client loads: the mskins archive its BaseMap names, else any archive (TextureIndex)."""
        if info.get('archive'):
            for name in (f'mskin{info["archive"]:03d}.mpk', f'Mskin{info["archive"]:03d}.mpk'):
                p = CLIENT / 'figures/Mskins' / name
                if p.exists():
                    if p not in self._archives:
                        self._archives[p] = {k.lower(): v for k, v in read_mpk(str(p)).items()}
                    for ext in ('.dds', '.tga', '.bmp'):
                        data = self._archives[p].get(info['tex'].lower() + ext)
                        if data:
                            return data
        return None


def figure_rows(t, fig):
    mn = next(x for x in t.monnifs if x[1].strip().lower().startswith('cata ') and x[13].strip() == str(fig))
    players = [x for x in t.monsters if len(x) > 81 and x[67].strip() == mn[0].strip()]
    pick = lambda col: collections.Counter(x[col].strip() for x in players).most_common(1)[0][0]
    return mn, players, pick


def build_figure(t, realm, race, gender, fig, upscale_list):
    mn, players, pick = figure_rows(t, fig)
    prefix = mn[2].strip().lower()
    out = OUT / 'races' / prefix
    out.mkdir(parents=True, exist_ok=True)
    textures = {}                                           # basename -> skin info (for export)

    def use(info):
        if info:
            textures[info['tex'].lower()] = info
        return info
    # the race's default starter clothes (CatSkin columns of its player rows), mesh variant from the texture name
    import re
    clothes = {}
    for slot, col in CATSKIN.items():
        info = use(t.skin(pick(col)))
        if info is None and slot == 'lbody':
            continue                                    # only Highlanders wear a default kilt
        m = re.search(r'(\d\d)', info['tex']) if info else None
        clothes[slot] = {'variant': int(m.group(1)) if m else 1, **(info or {'tex': '', 'chan': ''})}
    tint_flags = {'arms': pick(78) == '1', 'gloves': pick(79) == '1', 'legs': pick(80) == '1'}
    cata_scale = float(pick(81) or 100) / 100.0
    sound_set = int(pick(15) or 0)
    # options
    faces = [use(t.skin(x)) for x in t.faces[fig][2:] if x.strip()][:len(t.desc.get((fig, 0), [])) or 7]
    eyes = [use(t.skin(x)) for x in t.eyes[fig][2:10] if x.strip()]
    tones = [t.tint(x) for x in t.skin_tones[fig][2:10] if x.strip()]
    tattoo_labels = t.desc.get((fig, 2), [])
    tattoos = []
    if tattoo_labels:
        row = t.decals.get(fig, [])
        for i, label in enumerate(tattoo_labels):
            sid = row[2 + i].strip() if len(row) > 2 + i else ''
            tattoos.append({'label': label, 'tex': use(t.skin(sid))['tex'] if sid and sid != '0' and t.skin(sid) else ''})
    hair_labels = t.desc.get((fig, 1), [])
    hair_meshes = [p for p in t.fmap.get((fig, 8), []) if t.part(p)]
    styles, k = [], 0
    for si, label in enumerate(hair_labels, start=1):
        bald = label.lower() == 'bald' and len(hair_labels) > len(hair_meshes)
        node = None
        if not bald and k < len(hair_meshes):
            node = f'hair_{si}'
            k += 1
        colours = []
        row = t.hair.get((fig, si))
        for c in range(16):
            if row is None or len(row) < 5 + c * 3:
                break
            sid, tid = row[3 + c * 3].strip(), row[4 + c * 3].strip()
            info = use(t.skin(sid)) if sid else None
            if info:
                colours.append({'tex': info['tex'], 'chan': info['chan'], 'tint': t.tint(tid)})
        styles.append({'label': label, 'node': node, 'mesh': t.part(hair_meshes[k - 1])[1] if node else None,
                       'colours': colours})
    # parts for the glTF
    part_list = [(*t.labelled_head(t.fmap[(fig, 0)][0]), faces[0]['tex'], (1.0, 1.0, 1.0), 'head')]
    variants = {}
    for slot, n in SLOTS.items():
        meshes = t.fmap.get((fig, n), [])
        v = clothes[slot]['variant'] if slot in clothes else 0
        pid = meshes[v - 1] if 0 < v <= len(meshes) else (meshes[0] if meshes and v else None)
        if pid and t.part(pid):
            part_list.append((*t.part(pid), clothes[slot]['tex'], (1.0, 1.0, 1.0), slot))
        # every other mesh variant of the slot, hidden until armour asks for it (the client swaps body / arms / legs /
        # boots / gloves meshes by the Catacombs texture's number: chnBody05 = body mesh 5); node "<slot>_v<n>"
        variants[slot] = []
        for k, other in enumerate(meshes, start=1):
            if other and t.part(other) and (k != v or not pid):
                part_list.append((*t.part(other), clothes[slot]['tex'] if slot in clothes else '', (1.0, 1.0, 1.0),
                                  f'{slot}_v{k}'))
                variants[slot].append(k)
            elif other and k == v:
                variants[slot].append(k)
    # the race's cloak meshes (fig3map slot 7: Cloak01 hood down, Cloak02 hood up - its extra hood geometry -,
    # Cloak03 hoodless), hidden until a cloak is worn; the cloak item's Catacombs texture goes on them (armor.json)
    cloaks = []
    default_cloak = use({'tex': 'Cat_cloak', 'chan': '', 'archive': None})
    for k, pid in enumerate(t.fmap.get((fig, 7), []), start=1):
        if pid and t.part(pid):
            # the cloak meshes carry 3 texture coordinate sets; the Catacombs cloak textures (Cat_cloak ...) are laid
            # out for set 1 - collar ring, hood, back panel, side strip (owner 2026-10-11: set 0 smeared the cloak)
            arc, nif = t.part(pid)
            part_list.append((arc, nif + '@1', default_cloak['tex'], (1.0, 1.0, 1.0), f'cloak_{k}'))
            cloaks.append(k)
    # the race's full helms (fig3map slot 9 "Full Helm": the head-swap helms - Dragonsworn heavy / medium / light, the
    # Dubh Sluagh Cairnwarden's Helm): an item's objects.csv "Nif" column picks one by its place in this list and its
    # Helm skin (column 12) is drawn on it; worn, it stands in for the head (owner 2026-10-16: the Sluaghbinder's epic
    # helm). Hidden until worn, like the cloaks (Fig3Figure.set_helm, helms.json "full")
    full_helms = []
    default_helm = use({'tex': 'ADR_Dragonsworn_Helm_Plate', 'chan': '', 'archive': None})
    for k, pid in enumerate(t.fmap.get((fig, 9), []), start=1):
        if pid and t.part(pid):
            arc, nif = t.part(pid)
            part_list.append((arc, nif, default_helm['tex'], (1.0, 1.0, 1.0), f'fullhelm_{k}'))
            full_helms.append(k)
    for st in styles:
        if st['node']:
            first = st['colours'][0] if st['colours'] else {'tex': '', 'tint': [1, 1, 1]}
            pid = next(p for p in hair_meshes if t.part(p)[1] == st['mesh'])
            part_list.append((*t.part(pid), first['tex'], tuple(first['tint']), st['node']))
    clips = {CLIPS[k]: v for k, v in nc.clip_files(int(mn[3]), tuple(CLIPS)).items() if k in CLIPS}
    em = emote_slots()                                        # /wave, /bow ... and the frenzies (emotes.csv)
    have = {p.name.lower() for p in (CLIENT / 'anims').iterdir()}
    clips.update({em[k]: v for k, v in nc.clip_files(int(mn[3]), tuple(em)).items()
                  if v[0].lower() in have})                  # the client lacks EM_MEDITATE.kfa
    every = {**COMBAT_CLIPS, **style_clips()[0]}
    combat, _ = nc.combat_clip_files(int(mn[3]), tuple(every))   # animnifs 'hit time' is mostly 0: unused
    clips.update({every[k]: v for k, v in combat.items()})
    skeleton = (f'sfig{int(mn[15] or 1):03d}.mpk' if '--skel1' not in sys.argv else 'sfig001.mpk', f'{prefix}_skeleton.nif')
    report, used = nc.build_cata(skeleton, part_list, clips, str(out / 'figure.gltf'), '../../fig3_textures/x4plus'
                                 if '--no-upscale' not in sys.argv else '../../fig3_textures/original')
    # morph sliders: the head's feature codes, paired (odd = low end of the slider, even = high end), in the race's
    # fig3descriptions Morphs order; codes 21-24 are the Mood targets
    head = json.loads((out / 'figure.gltf').read_text())
    head_mesh = next((n['mesh'] for n in head['nodes'] if n.get('name') == 'head' and 'mesh' in n), None)
    codes = head['meshes'][head_mesh].get('extras', {}).get('codes', []) if head_mesh is not None else []
    pairs = sorted({(c + 1) // 2 for c in codes if 0 < c < 21})
    sliders = []
    for label, p in zip(t.desc.get((fig, 4), []), pairs):
        sliders.append({'label': label, 'low': f'FM{codes.index(2 * p - 1) + 1}' if 2 * p - 1 in codes else '',
                        'high': f'FM{codes.index(2 * p) + 1}' if 2 * p in codes else ''})
    mood = {f'FM{i + 1}': c for i, c in enumerate(codes) if 21 <= c <= 24}
    meta = {
        'realm': realm, 'race': race, 'gender': gender, 'fig3': fig, 'prefix': prefix, 'monnifs': int(mn[0]),
        'anim_set': int(mn[3]), 'strides_units': {'run': float(mn[4]), 'walk': float(mn[5]), 'back': float(mn[6]),
                                                  'strafe': float(mn[7])},
        'clip_seconds': {k: v['seconds'] for k, v in report.items()},
        'sound_set': sound_set, 'scale': cata_scale, 'sizes': SIZES,
        'clothes': clothes, 'skin_tint_parts': {'head': True, **tint_flags}, 'variants': variants, 'cloaks': cloaks, 'full_helms': full_helms,
        'faces': [{'label': lab, 'tex': f['tex']} for lab, f in zip(t.desc.get((fig, 0), []), faces)],
        'eyes': [e['tex'] for e in eyes], 'skin_tones': tones,
        'hair_styles': styles, 'tattoos': tattoos, 'sliders': sliders, 'mood_targets': mood,
        'slider_range': [0, 8], 'slider_default': 4,
        'open': ['Mood slider: which of the mood targets (codes 21-24) it drives and how is not traced in game.dll yet'],
    }
    (out / 'figure.json').write_text(json.dumps(meta, indent=1), encoding='utf-8')
    upscale_list.update(textures)
    print(f'{prefix}: {race} {gender} fig {fig}, {len(faces)} faces, {len(eyes)} eyes, {len(tones)} tones, '
          f'{len(styles)} hair styles ({sum(1 for s in styles if s["node"])} meshes), {len(tattoos)} tattoos, '
          f'sliders {[s["label"] for s in sliders]}, mood {list(mood.values())}, scale {cata_scale}, '
          f'tint {tint_flags}, clips {sorted(report)}')
    return meta


def export_textures(t, infos):
    from texture_index import TextureIndex
    from PIL import Image
    idx = TextureIndex()
    (TEX / 'original').mkdir(parents=True, exist_ok=True)
    missing = []
    for name, info in sorted(infos.items()):
        dst = TEX / 'original' / f'{name}.png'
        data = t.texture_bytes(info)
        img = Image.open(io.BytesIO(data)) if data else idx.image(info['tex'])
        if img is None:
            missing.append(info['tex'])
            continue
        img = img.convert('RGBA')
        if not dst.exists() or Image.open(dst).tobytes() != img.tobytes():
            img.save(dst)
    return missing


def main():
    want = [a for a in sys.argv[1:] if not a.startswith('--')]
    t = Tables()
    infos, metas = {}, []
    for realm, race, male, female in RACES:
        for gender, fig in (('male', male), ('female', female)):
            prefix = figure_rows(t, fig)[0][2].strip().lower()
            if want and prefix not in want:
                continue
            metas.append(build_figure(t, realm, race, gender, fig, infos))
    missing = export_textures(t, infos)
    print(len(infos), 'textures,', 'missing:', missing)
    index = OUT / 'races/races.json'
    old = json.loads(index.read_text()) if index.exists() else {}
    for m in metas:
        old[m['prefix']] = {'realm': m['realm'], 'race': m['race'], 'gender': m['gender'],
                            'file': f'races/{m["prefix"]}/figure.gltf'}
    index.write_text(json.dumps(old, indent=1), encoding='utf-8')
    import subprocess                                       # the hair styles' helm morphs into figure.json
    subprocess.run([sys.executable, str(HERE / 'add_helm_hair_morphs.py')], check=False)
    (OUT / 'style_clips.json').write_text(json.dumps({str(k): v for k, v in style_clips()[1].items()}, indent=1),
                                          encoding='utf-8')
    if '--no-upscale' not in sys.argv:
        from build_zone import upscale_dir
        upscale_dir(TEX / 'original', TEX / 'x4plus', factor_after=4)


if __name__ == '__main__':
    main()
