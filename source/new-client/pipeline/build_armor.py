"""Worn armour on Catacombs figures (players and NPCs), the client's way (nif_character.cata_outfit):
objects.csv item -> its classic pskins skin per slot -> the pskins set "Cata <same name>" -> that set's texture for
the slot, whose number is the mesh variant (chnBody05 = body mesh 5). Bodies are per gender (_m / _f).

    python build_armor.py        # armour used by every built zone's NPCs + every item in items.json / merchants.json
Writes godot/content/characters/armor.json {model: {"m": {slot: [variant, texture]}, "f": {...}}} and exports the
textures into content/characters/fig3_textures/{original,x4plus} (Real-ESRGAN x4plus 4x, like every character
texture). The race figures carry every mesh variant as hidden nodes "<slot>@<n>" (build_races.py)."""
import json, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import ROOT
import nif_character as nc
from build_races import SLOTS, TEX
from texture_index import TextureIndex
from build_zone import upscale_dir, save_if_changed

OUT = ROOT / 'godot/content/characters/armor.json'
ARMOR_SLOTS = (21, 22, 23, 25, 26, 27, 28)                 # helm, hands, feet, torso, cloak, legs, arms


def wanted_models():
    models = set()
    for f in (ROOT / 'godot/content/zones').glob('zone*/npcs.json'):
        for rows in json.loads(f.read_text(encoding='utf-8')).get('equipment', {}).values():
            models.update(int(r['model']) for r in rows if int(r['slot']) in ARMOR_SLOTS and int(r['model']) > 0)
    for f in ('items.json', 'merchants.json'):
        p = ROOT / 'godot/content/items' / f
        if p.exists():
            for it in json.loads(p.read_text(encoding='utf-8')).get('items', {}).values():
                if 32 <= int(it.get('object_type', 0)) <= 38:
                    models.add(int(it['model']))
    # monster drops too (owner 2026-10-11: 86 looted armour models had no entry and showed as the starter clothes)
    p = ROOT / 'godot/content/loot/loot.json'
    if p.exists():
        def walk(o):
            if isinstance(o, dict):
                if 'object_type' in o and 'model' in o and 32 <= int(o.get('object_type', 0)) <= 38:
                    models.add(int(o['model']))
                for v in o.values():
                    walk(v)
            elif isinstance(o, list):
                for v in o:
                    walk(v)
        walk(json.loads(p.read_text(encoding='utf-8')))
    if '--server' in sys.argv:
        # 0.36c: every armour model the shared server's items and NPCs can wear (ItemTemplate, ItemUnique - ROG, quest
        # rewards, crafted - and NPCEquipment), read only
        import sqlite3
        db = ROOT.parent / 'new class test - CLAUDE VERSION/runtime/data/opendaoc.sqlite3.db'
        con = sqlite3.connect(db.as_uri() + '?mode=ro', uri=True)
        for table in ('ItemTemplate', 'ItemUnique'):
            models.update(m for (m,) in con.execute(f'select distinct Model from {table} where Object_Type between 32 and 38 and Model > 0'))
            models.update(m for (m,) in con.execute(f'select distinct Model from {table} where Item_Type = 26 and Model > 0'))   # cloaks
        slots = ','.join(str(s) for s in ARMOR_SLOTS)
        models.update(m for (m,) in con.execute(f'select distinct Model from NPCEquipment where Slot in ({slots}) and Model > 0'))
        models.update(code_made_models())
    models.discard(0)
    return sorted(models)


def code_made_models():
    """Items the server makes in its scripts rather than its item tables (the Sluaghbinder's Dubh Sluagh epic set,
    SluaghbinderEpicArmor.cs: new("hauberk", "Hauberk of the Dubh Sluagh", 4826, eObjectType.Scale, ...)), read only."""
    import re
    scripts = ROOT.parent / 'new class test - CLAUDE VERSION/development-source/server/GameServer/scripts'
    pat = re.compile(r'new\("[^"]*",\s*"[^"]*",\s*(\d{2,5}),\s*eObjectType\.')
    found = set()
    for f in scripts.rglob('*.cs') if scripts.exists() else []:
        found.update(int(m) for m in pat.findall(f.read_text(encoding='utf-8', errors='ignore')))
    return found


def boots_twin(idx, tex):
    """The Catacombs boots meshes are laid out for 512 x 256 sheets (every race's starter boots); pskins names some boots
    by a twin of the other spelling (cthboots01_05 for "Cata Norse Cloth" is a square 256 sheet of another layout -
    white, patchy boots on every race, owner 2026-10-11; its cthboot01_05 twin is the 512 x 256 one). Of the two
    spellings, the 2:1 sheet wins; with no 2:1 twin the named one stays."""
    import re
    m = re.match(r'([a-z]+?)(boots?)(\d\d_.*)', tex)
    if not m:
        return tex
    twin = m.group(1) + ('boot' if m.group(2) == 'boots' else 'boots') + m.group(3)
    if twin not in idx.entries:
        return tex
    a, b = idx.image(tex), idx.image(twin)
    wide = lambda im: im is not None and im.size[0] == 2 * im.size[1]
    return twin if wide(b) and not wide(a) else tex


def cloak_textures(rows, idx):
    """{objects.csv id: Catacombs cloak texture} for cloak items: objects.csv Skins 'Cloak' column (11) -> its classic
    pskins texture (cloak.tga, mage_cloak1.tga, B_cloak1.tga ...) -> the Catacombs twin the client draws on the fig3
    cloak meshes: Cat_<same name> (Cat_cloak, Cat_mage_cloak1), else without its trailing number (Cat_B_cloak), else
    the plain Cat_cloak."""
    import re
    pskins = {r[0]: r for r in rows('pskins.csv') if r and r[0].isdigit()}
    out = {}
    for r in rows('objects.csv'):
        if not r or not r[0].isdigit() or len(r) < 12 or r[11].strip() in ('', '0'):
            continue
        classic = pskins.get(r[11].strip())
        if classic is None:
            continue
        base = classic[2].rsplit('.', 1)[0].lower()
        for cand in ('cat_' + base, 'cat_' + re.sub(r'\d+$', '', base), 'cat_cloak'):
            if cand in idx.entries:
                out[r[0]] = cand
                break
    return out


CATA_COLS = {66: 'body_m', 67: 'body_f', 68: 'arms', 69: 'gloves', 70: 'legs', 71: 'boots', 72: 'cloak'}


def cata_columns(rows, idx, skip):
    """Armour the name match (nif_character.cata_outfit: pskins "Cata <name>") can't dress, the way the client dresses
    Catacombs figures (owner 2026-10-15: the Sluaghbinder's Dubh Sluagh epic set, objects 4826-4832 - its items are
    made in server code, so no list named their models, and its own skins have no "Cata" twin): Cata Status (objects
    col 65) on, a pskins id per slot (66 body m, 67 body f, 68 arms, 69 gloves, 70 legs, 71 boots), the pskin's BaseTex
    an mskins row = the texture in figures/Mskins; a cloak (col 72) wears the pskin's own texture. Every slot on mesh 1,
    as the stock sets here. Objects in `skip` (already dressed) are left alone. {model: {m/f: {slot: [1, tex]}}}."""
    pskins = {r[0].strip(): r for r in rows('pskins.csv') if r and r[0].strip().isdigit()}
    mskins = {r[0].strip(): r for r in rows('mskins.csv') if r and r[0].strip().isdigit()}
    out = {}
    for r in rows('objects.csv'):
        if not r or not r[0].strip().isdigit() or len(r) < 73 or r[0].strip() in skip:
            continue
        status = r[65].strip() == '1'
        parts_m, parts_f = {}, {}
        for col, slot in CATA_COLS.items():
            pid = r[col].strip()
            if not pid or pid not in pskins or (slot != 'cloak' and not status):
                continue
            ps = pskins[pid]
            if ps[1].strip().lower().startswith('cata'):
                continue                                   # a stock Catacombs skin: the name match covers it
            base = ps[6].strip() if len(ps) > 6 else ''
            dds = (mskins[base][2] if base in mskins and slot != 'cloak' else ps[2]).strip()
            tex = dds.rsplit('.', 1)[0].lower()
            if not tex or idx.locate(tex) is None:
                continue
            if slot == 'body_m':
                parts_m['body'] = [1, tex]
            elif slot == 'body_f':
                parts_f['body'] = [1, tex]
            else:
                parts_m[slot] = parts_f[slot] = [1, tex]
        if parts_m or parts_f:
            out[r[0].strip()] = {'m': parts_m, 'f': parts_f}
    return out


ROBE_BODY = 7


def robe_bodies(rows, out):
    """Robes on the long robe body mesh (owner 2026-10-19: a troll Bonedancer companion's robe drawn on the vest body
    with trousers under it). game.dll 0x41b5c1 picks the torso's fig3 body mesh: an item whose objects.csv "Lbody #"
    (column 15) is 3 - every robe - wears body mesh 7, any other the item's extension; Fig3Figure hides the arms, legs
    and kilt under it and puts boots / gloves on mesh 3 (0x424817). Sets the body variant of those models in `out`."""
    objects = {r[0].strip(): r for r in rows('objects.csv') if r and r[0].strip().isdigit()}
    n = 0
    for model, entry in out.items():
        r = objects.get(model)
        if r is None or len(r) < 16 or r[15].strip() != '3':
            continue
        for parts in entry.values():
            if 'body' in parts:
                parts['body'][0] = ROBE_BODY
                n += 1
    return n


def patch_robes():
    """python build_armor.py --robes: only the robe rule on the existing armor.json (no texture export)."""
    from build_races import Tables
    data = json.loads(OUT.read_text(encoding='utf-8'))
    n = robe_bodies(Tables().rows, data['models'])
    OUT.write_text(json.dumps(data, indent=1), encoding='utf-8')
    print(n, 'robe bodies on mesh', ROBE_BODY)


def main():
    from build_races import Tables
    rows = Tables().rows                                   # gamedata.mpk csv reader
    slot_name = {v: k for k, v in SLOTS.items()}
    cata_slot = {v: k for k, v in nc.CATA_SLOTS.items()}
    out, texs = {}, set()
    idx = TextureIndex()
    # the Catacombs figure skins first (owner 2026-10-15: a sorcerer's boots with white patches, a shirt "not a texture
    # i've ever seen"): 107 armour textures - cthboot01_01, cthbody01_01_f, leabody01-04, ctharms / cthlegs ... - are
    # also in items/pskins under the same name, laid out for the classic figures (cthboot01_01 there is a 256 square
    # of another layout, the Catacombs one 512 x 256), and items/ ranked first; the fig3 meshes need the mskins copy
    idx.first = ['figures/mskins']
    cloaks = cloak_textures(rows, idx)
    for model in wanted_models():
        entry = {}
        for g in ('m', 'f'):
            outfit = nc.cata_outfit(rows, [model], g)
            parts = {}
            for slot_no, (variant, tex) in outfit.items():
                name = slot_name.get(slot_no) or cata_slot.get(slot_no)
                if name:
                    tex = boots_twin(idx, tex.lower()) if name == 'boots' else tex.lower()
                    parts[name] = [variant, tex]
                    texs.add(tex)
            if str(model) in cloaks:                      # a cloak: its texture on the race's cloak meshes
                parts['cloak'] = [1, cloaks[str(model)]]
                texs.add(cloaks[str(model)])
            if parts:
                entry[g] = parts
        if entry:
            out[str(model)] = entry
    # what the name match left undressed, by the client's own Catacombs columns (custom sets: the Dubh Sluagh)
    dressed = {m for m, e in out.items() if any(set(p) - {'cloak'} for p in e.values())}
    plain_cloaks = {m for m, e in out.items() if all(set(p) == {'cloak'} and p['cloak'][1] == 'cat_cloak' for p in e.values())}
    used = {str(m) for m in wanted_models()}
    for model, entry in cata_columns(rows, idx, dressed).items():
        if model not in used or (model in out and model not in plain_cloaks):
            continue
        out[model] = entry
        for p in entry.values():
            texs.update(t for _v, t in p.values())
    (TEX / 'original').mkdir(parents=True, exist_ok=True)
    missing = []
    for t in sorted(texs):
        img = idx.image(t)
        if img is None:
            missing.append(t)
            continue
        save_if_changed(img, TEX / 'original' / f'{t}.png')
    upscale_dir(TEX / 'original', TEX / 'x4plus', factor_after=4)
    robe_bodies(rows, out)
    OUT.write_text(json.dumps({'models': out, 'missing_textures': missing}, indent=1), encoding='utf-8')
    print(len(out), 'armour models,', len(texs), 'textures,', len(missing), 'missing', missing[:8])


if __name__ == '__main__':
    patch_robes() if '--robes' in sys.argv else main()
