"""Re-run the NIF converter on some of a converted zone's models (after a converter fix) without rebuilding the zone:
the model glTF, any new textures it names (a NIF's own embedded pixels too), their x4plus upscale, the model's
zone.json entry (triangles, warnings). 2026-10-14: paletted embedded textures (Tir na Nog's tir24 drew a white wall;
Camelot's cam35 and seven Darkness Falls room / tunnel pieces the same).

    python pipeline/reconvert_zone_models.py 209 tir24
    python pipeline/reconvert_zone_models.py 249 demonroom2a demonroom2b ...
"""
import json, sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
from texture_index import TextureIndex
from build_zone import save_if_changed, upscale_dir
import nif_static

zone_id = int(sys.argv[1])
names = [a.lower() for a in sys.argv[2:]]
out = ROOT / f'godot/content/zones/zone{zone_id:03d}'
zone = json.loads((out / 'zone.json').read_text(encoding='utf-8'))
npks = {p.stem.lower(): p for p in (CLIENT / 'zones/Dnifs').iterdir() if p.suffix.lower() == '.npk'}   # dungeon pieces
npks.update({p.stem.lower(): p for p in (CLIENT / 'zones/Nifs').iterdir() if p.suffix.lower() == '.npk'})
from build_zone import zone_local_npks
npks.update({Path(k).stem: v for k, v in zone_local_npks(zone_id).items()})   # the zone's own first (2026-10-29)
idx = TextureIndex()
idx.first = [f'zones/zone{zone_id:03d}/nifs']
odir = out / 'textures/original'
for name in names:
    entry = next((m for m in zone['models'] if str(m.get('nif', '')).lower() == name or
                  Path(m['file']).stem.lower() == name), None)
    npk = npks.get(name)
    if entry is None or npk is None:
        print(name, 'not in this zone' if entry is None else 'no npk in the client')
        continue
    raw = next(v for k, v in read_mpk(str(npk)).items() if k.lower().endswith('.nif'))
    try:
        info = nif_static.convert(raw, str(out / entry['file']), '../textures/original')
    except Exception as e:                                 # pre-3.3 NIFs are not read yet (as build_zone)
        print(name, 'failed', type(e).__name__, e)
        continue
    for n in sorted(info['textures']):
        doubled = n.endswith(('__d2', '__x2'))   # __x2: the raw detail map (detail_x2.py)
        key = n[:-4] if doubled else n
        img = idx.image(key) or nif_static.EMBEDDED.get(key)
        if img is None:
            print(name, 'missing texture', n)
            continue
        if doubled and n.endswith('__d2'):
            rgb = np.asarray(img.convert('RGB'), dtype=np.float32) * 2.0
            img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
        save_if_changed(img, odir / f'{n}.png')
    entry['triangles'] = info['triangles']
    entry['collision_triangles'] = info['collision_triangles']
    entry['warnings'] = [f'{name}: {w}' for w in info['warnings']]
    print(name, info['triangles'], 'triangles, textures', sorted(info['textures']))
(out / 'zone.json').write_text(json.dumps(zone, indent=1), encoding='utf-8')
if '--no-upscale' not in sys.argv:
    upscale_dir(odir, out / 'textures/x4plus')
