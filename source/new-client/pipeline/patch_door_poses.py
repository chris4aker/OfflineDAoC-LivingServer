"""Re-read the door leaves' closed / open poses (nif_doors) of every converted zone model with doors, without rebuilding
the zones: each model's NIF is converted again to a scratch file and only its nif_doors list is copied over, when the
leaves are the same ones (names, groups, closed poses) and only an open pose changed.

Written for nif_static.door_spec's translation keys (owner 2026-10-20: Snowdonia Fortress's portcullises stayed shut -
they only slide up, and only rotation keys were read).

    python patch_door_poses.py [--dry]
"""
import json, sys, tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
import nif_static

ZONES = ROOT / 'godot/content/zones'


def sources():
    """{nif base name: [npk files]} - the zone, dungeon and city model archives build_zone converts from."""
    out = {}
    dirs = [CLIENT / 'zones/Nifs', CLIENT / 'zones/Dnifs']
    dirs += sorted(p / 'nifs' for p in (CLIENT / 'zones').glob('zone*') if (p / 'nifs').is_dir())
    dirs += sorted(p / 'nifs' for p in (CLIENT / 'frontiers/zones').glob('zone*') if (p / 'nifs').is_dir())
    for d in dirs:
        if d.is_dir():
            for p in d.iterdir():
                if p.suffix.lower() == '.npk':
                    out.setdefault(p.stem.lower(), []).append(p)
    return out


def same_leaves(a, b):
    return len(a) == len(b) and all(x['name'] == y['name'] and x.get('group') == y.get('group')
                                    and max(abs(u - v) for u, v in zip(x['closed'], y['closed'])) < 1e-4
                                    for x, y in zip(a, b))


def main():
    dry = '--dry' in sys.argv
    src = sources()
    changed, skipped = [], []
    with tempfile.TemporaryDirectory() as tmp:
        for path in sorted(ZONES.glob('zone*/models/*.gltf')):
            text = path.read_text(encoding='utf-8')
            if '"nif_doors"' not in text:
                continue
            doc = json.loads(text)
            old = doc.get('extras', {}).get('nif_doors') or []
            found = None
            for npk in src.get(path.stem.lower(), []):
                raw = next((v for k, v in read_mpk(str(npk)).items() if k.lower().endswith('.nif')), None)
                if raw is None:
                    continue
                try:
                    nif_static.convert(raw, str(Path(tmp) / 'x.gltf'), '../textures/original')
                except Exception:
                    continue
                new = json.loads((Path(tmp) / 'x.gltf').read_text()).get('extras', {}).get('nif_doors') or []
                if same_leaves(old, new):
                    found = new
                    break
            if found is None:
                skipped.append(str(path.relative_to(ZONES)))
                continue
            if found == old:
                continue
            moved = [d['name'] for d, o in zip(found, old) if d['open'] != o['open']]
            changed.append((str(path.relative_to(ZONES)), moved))
            if not dry:
                doc['extras']['nif_doors'] = found
                path.write_text(json.dumps(doc), encoding='utf-8')
    for p, moved in changed:
        print('patched' if not dry else 'would patch', p, moved)
    print(len(changed), 'models with new door poses;', len(skipped), 'not matched to a source NIF:', skipped[:20])


if __name__ == '__main__':
    main()
