"""Helms and hats on the Catacombs figures, the client's way (0.36c, owner 2026-10-11: "his wizard hat").

A helm item's objects.csv row lists, besides its classic NIF, the Catacombs helm per race and gender (columns
Nif1 .. Nif12 -> items.csv rows named <set>_<race>_<gender>, e.g. hibrp01_cel_m for the Hibernian "Gandalf hat" on a
Celt male) and the Catacombs texture they wear (the column after Nif12, a pskins row: b_hat01.dds). items.csv gives
each Catacombs helm its 'Cata Helm Hair' (column 23) and 'Cata % Helm Scale' (column 25). The helm NIFs are static
meshes in the head bone's space (with the usual HELD / BELT / BACK frames): converted like the weapons and hung on
Bip01 Head.

    python pipeline/build_helms.py            # every helm the shared server's items / NPCs use + the game's own
Writes godot/content/helms/<nif>/model.gltf, textures in godot/content/helms/textures/{original,x4plus} and
godot/content/helms/helms.json {model: {figure prefix: {file, hair, scale}}}.
"""
import csv
import io
import json
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
from texture_index import TextureIndex
from build_zone import upscale_dir, save_if_changed
import nif_static

OUT = ROOT / 'godot/content/helms'
FIG_TEX = ROOT / 'godot/content/characters/fig3_textures/original'
DB = ROOT.parent / 'new class test - CLAUDE VERSION/runtime/data/opendaoc.sqlite3.db'
PREFIXES = ['bri', 'ava', 'hig', 'sar', 'inc', 'nor', 'tro', 'dwa', 'kob', 'val', 'cel', 'fir', 'elf', 'lur', 'syl']
# objects.csv Nif1 .. Nif12 are the realm's races in this order - the client picks the column by the wearer's race and
# gender, whatever the NIF is named (owner 2026-10-19: Albion hats missing - Briton women's NIFs are "albHelm01_brit_f",
# the Half Ogre columns were skipped as an unknown race, and the full plate helm 93 names albHelm03_Bri_m in all 12)
COLUMN_RACES = {
    'alb': ['bri_m', 'bri_f', 'sar_m', 'sar_f', 'hig_m', 'hig_f', 'ava_m', 'ava_f', 'inc_m', 'inc_f', 'hal_m', 'hal_f'],
    'mid': ['nor_m', 'nor_f', 'dwa_m', 'dwa_f', 'kob_m', 'kob_f', 'tro_m', 'tro_f', 'val_m', 'val_f', 'fro_m', 'fro_f'],
    'hib': ['cel_m', 'cel_f', 'elf_m', 'elf_f', 'lur_m', 'lur_f', 'fir_m', 'fir_f', 'syl_m', 'syl_f', 'sha_m', 'sha_f'],
}
REALM_OF = {r.split('_')[0]: realm for realm, races in COLUMN_RACES.items() for r in races}
REALM_OF.update({'brit': 'alb', 'alb': 'alb', 'mid': 'mid', 'hib': 'hib'})


def row_realm(nifs):
    """The realm a row's Nif1 .. Nif12 belong to, from the race tags / set prefixes in their names (most votes)."""
    import re
    votes = {}
    for nif in nifs:
        for tok in re.split(r'[_\d]+', nif.lower()):
            realm = REALM_OF.get(tok) or REALM_OF.get(tok[:3]) if tok else None
            if realm:
                votes[realm] = votes.get(realm, 0) + 1
    return max(votes, key=votes.get) if votes else None


def wanted_models():
    con = sqlite3.connect(DB.as_uri() + '?mode=ro', uri=True)
    models = set()
    for table in ('ItemTemplate', 'ItemUnique'):
        models.update(m for (m,) in con.execute(f'select distinct Model from {table} where Item_Type = 21 and Model > 0'))
    models.update(m for (m,) in con.execute('select distinct Model from NPCEquipment where Slot = 21 and Model > 0'))
    # helms the server makes in its scripts (the Dubh Sluagh set's Cairnwarden's Helm, 4831)
    from build_armor import code_made_models
    models.update(code_made_models() & helm_rows())
    return sorted(models)


def helm_rows():
    """objects.csv rows that carry Catacombs helms (any Nif1..Nif12 column set)."""
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    rows = list(csv.reader(io.StringIO(g['objects.csv'].decode('latin1'))))
    cols = [rows[1].index(f'Nif{i}') for i in range(1, 13)]
    return {int(r[0]) for r in rows if r and r[0].isdigit() and any(len(r) > c and r[c].strip() not in ('', '0') for c in cols)}


def archive_image(row):
    """A pskins row's texture from the archive it names (items/pskins/pskinNNN.mpk), or None."""
    from PIL import Image
    if row is None or not row[4].strip().isdigit() or int(row[4]) <= 0:
        return None
    for name in (f'pskin{int(row[4]):03d}.mpk', f'Pskin{int(row[4]):03d}.mpk'):
        p = CLIENT / 'items/pskins' / name
        if p.exists():
            d = {k.lower(): v for k, v in read_mpk(str(p)).items()}
            raw = d.get(row[2].strip().lower())
            if raw is None:
                base = row[2].strip().rsplit('.', 1)[0].lower()
                raw = next((v for k, v in d.items() if k.rsplit('.', 1)[0] == base), None)
            if raw:
                return Image.open(io.BytesIO(raw)).convert('RGBA')
    return None


def nif_textures(path):
    """The texture names a helm NIF's own shapes use (lower case, no extension)."""
    from netimmerse import read_nif
    out = set()
    for b in read_nif(path.read_bytes()).blocks:
        if b.type == 'NiSourceTexture':
            name = str(b.get('file_name') or '').replace('\\', '/').split('/')[-1]
            if name:
                out.add(name.rsplit('.', 1)[0].lower())
    return out


def shape_offset(path):
    """The translation of the helm NIF's first mesh shape (NIF units, x / y / z)."""
    from netimmerse import read_nif
    n = read_nif(path.read_bytes())
    for b in n.blocks:
        if b.type in ('NiTriShape', 'NiTriStrips'):
            m = nif_static.local_matrix(b)
            return [round(float(m[3][0]), 3), round(float(m[3][1]), 3), round(float(m[3][2]), 3)]
    return [0.0, 0.0, 0.0]


def main():
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    rows = list(csv.reader(io.StringIO(g['objects.csv'].decode('latin1'))))
    header = rows[1]
    nif_cols = [header.index(f'Nif{i}') for i in range(1, 13)]
    tex_col = nif_cols[-1] + 1
    objects = {r[0]: r for r in rows if r and r[0].isdigit()}
    items = {r[0]: r for r in csv.reader(io.StringIO(g['items.csv'].decode('latin1'))) if r and r[0].isdigit()}
    pskins = {r[0]: r for r in csv.reader(io.StringIO(g['pskins.csv'].decode('latin1'))) if r and r[0].isdigit()}
    files = {p.name.lower(): p for p in (CLIENT / 'items').iterdir()}
    idx = TextureIndex()
    tex_dir = OUT / 'textures/original'
    tex_dir.mkdir(parents=True, exist_ok=True)
    num = lambda v, d=0: int(v) if v.strip().lstrip('-').isdigit() else d
    out, missing, built = {}, [], {}
    for model in wanted_models():
        o = objects.get(str(model))
        if o is None:
            missing.append((model, 'no objects.csv row'))
            continue
        # its texture: the Catacombs texture column (the layout the Catacombs helm's UVs use - for the Gandalf hats
        # b_hat01.dds: cone, brim ring, crown ring and the edge band), else the item's Helm skin (column 12) - as
        # game.dll dresses a Catacombs helm (0x4221ea: the Helm skin, replaced by the Cata Helm Texture when set; it
        # never reads Apply Texture, column 22, which names the mesh's base atlas: owner 2026-10-19, the Norse Cloth
        # Cap and 23 more drew N_helm's chain and scales instead of their padded / cloth / reinforced skins) -
        # taken from the exact pskins archive the row names (pskin042.mpk ...): other archives hold same-named copies
        # in the classic layout (owner 2026-10-11: the hat's band was missing / misplaced)
        tex, tex_row = '', None
        for col in (tex_col, 12):
            if not tex and len(o) > col and o[col].strip() not in ('', '0') and o[col].strip() in pskins:
                tex_row = pskins[o[col].strip()]
                tex = tex_row[2].rsplit('.', 1)[0].lower() + (f'__a{int(tex_row[4])}' if tex_row[4].strip().isdigit() else '')
        # no Catacombs / applied texture: the helm mesh's own texture, from the archive the item's classic Helm skin
        # (column 12) names when that skin is the same texture - other archives keep older same-named copies
        # (owner 2026-10-11: the Albion full plate helm 93 drew pskin001's plain 128 px b_plt1_helm2, not pskin007's)
        helm_row = pskins.get(o[12].strip()) if not tex and len(o) > 12 else None
        entry = {}
        row_items = [items.get(o[col].strip()) if len(o) > col else None for col in nif_cols]
        races = COLUMN_RACES.get(row_realm([it[2].strip() for it in row_items if it is not None]), [])
        for ci, it in enumerate(row_items):
            if it is None or ci >= len(races):
                continue
            nif = it[2].strip()
            prefix = races[ci]                             # the column's race, as the client picks it
            path = files.get(nif.lower() + '.nif')
            if path is None:
                missing.append((model, nif + '.nif not in app/items'))
                continue
            row_tex, row = tex, tex_row
            if helm_row is not None and helm_row[4].strip().isdigit() and int(helm_row[4]) > 0 \
                    and helm_row[2].rsplit('.', 1)[0].lower() in nif_textures(path):
                row = helm_row
                row_tex = helm_row[2].rsplit('.', 1)[0].lower() + f'__a{int(helm_row[4])}'
            folder = nif.lower() + (f'__{row_tex}' if row_tex else '')
            # the mesh shape's own offset (e.g. hibrp01: 5.24 units down) - recorded only; the game keeps it (owner
            # 2026-10-11: undoing it floated the hat above the head)
            offset = shape_offset(path)
            if folder not in built:
                d = OUT / folder
                d.mkdir(parents=True, exist_ok=True)
                info = nif_static.convert(path.read_bytes(), str(d / 'model.gltf'), '../textures/x4plus')
                used = set(info['textures'])
                if tex:                                    # the item's Catacombs texture on every surface
                    doc = json.loads((d / 'model.gltf').read_text(encoding='utf-8'))
                    for im in doc.get('images', []):
                        im['uri'] = f'../textures/x4plus/{tex}.png'
                    (d / 'model.gltf').write_text(json.dumps(doc), encoding='utf-8')
                    used = {tex}
                elif row_tex:                              # the mesh's own texture, from the Helm skin's archive
                    base = row_tex.split('__a')[0]
                    doc = json.loads((d / 'model.gltf').read_text(encoding='utf-8'))
                    for im in doc.get('images', []):
                        if im['uri'].rsplit('/', 1)[-1].rsplit('.', 1)[0].lower() == base:
                            im['uri'] = f'../textures/x4plus/{row_tex}.png'
                    (d / 'model.gltf').write_text(json.dumps(doc), encoding='utf-8')
                    used = (used - {base}) | {row_tex}
                for t in sorted(used):
                    img = archive_image(row) if row_tex and t == row_tex else None
                    img = img or idx.image(t.split('__a')[0])
                    if img is None:
                        missing.append((model, 'texture ' + t))
                        continue
                    save_if_changed(img, tex_dir / f'{t}.png')
                built[folder] = True
            entry[prefix] = {'file': f'{folder}/model.gltf', 'hair': num(it[23]) if len(it) > 23 else 0,
                             'scale': num(it[25], 100) if len(it) > 25 else 100, 'offset': offset}
        # a head-swap helm (objects "Nif" column 95 + "Head Swap" 96: the Dragonsworn helms, the Dubh Sluagh
        # Cairnwarden's Helm - owner 2026-10-16): no per-race helm NIFs; the "Nif" number picks the race's full helm part
        # (fig3map slot 9, built into every figure: build_races full_helms) and the Helm skin (column 12) is drawn on it
        if not entry and len(o) > 96 and o[95].strip().isdigit() and int(o[95]) > 0 and o[96].strip() == '1':
            hrow = pskins.get(o[12].strip()) if len(o) > 12 else None
            img = archive_image(hrow)
            if hrow is not None and img is not None:
                ht = hrow[2].rsplit('.', 1)[0].lower() + (f'__a{int(hrow[4])}' if hrow[4].strip().isdigit() else '')
                FIG_TEX.mkdir(parents=True, exist_ok=True)
                save_if_changed(img, FIG_TEX / f'{ht}.png')
                entry = {'full': int(o[95]), 'tex': ht}
        if entry:
            out[str(model)] = entry
        else:
            missing.append((model, 'no Catacombs helm'))
    upscale_dir(tex_dir, OUT / 'textures/x4plus', factor_after=4)
    upscale_dir(FIG_TEX, FIG_TEX.parent / 'x4plus', factor_after=4)       # the full helms' skins (figure textures)
    (OUT / 'helms.json').write_text(json.dumps({'models': out, 'missing': missing}, indent=1), encoding='utf-8')
    print(len(out), 'helm models,', len(built), 'helm meshes,', len(missing), 'missing:', missing[:10])


if __name__ == '__main__':
    main()
