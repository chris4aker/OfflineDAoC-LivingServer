"""Each weapon's BELT and BACK frames into content/weapons/weapons.json (owner 2026-10-19: a sheathed two-hander hung
out across the cloak, where the old client tucks it under with only the handle over the shoulder).

game.dll 0x4225a1 / 0x4226ab: a weapon outside the hand hangs from its slot's sheath bone (right hand Bip01 L Belt,
left hand R Belt, two-hand L Back, ranged R Back) by the weapon NIF's own "BELT" (hands) or "BACK" (two-hand, ranged)
node: the node's transform is where the weapon's root sits on the bone (b_2h_2hsword01's BACK, 12 units along -Z,
lifts the sword so its hilt comes up over the shoulder with the blade under the cloak, as the old client - the
inverse hung it down by the leg; n_1h_doubleaxe01's BELT turns the axe head down along the thigh). Stored like
"held" (nif_static.held_frame): column-vector, NIF axes and units - [basis x, y, z, origin], 12 numbers - as
"belt_frame" / "back_frame" ("belt" is objects.csv On Belt).
Run after add_weapon_frames.py / add_h2h_held.py / add_h2h_stab.py (it leaves "held" alone).

    python pipeline/add_sheath_frames.py
"""
import json, sys
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
from netimmerse import read_nif
import nif_static


def frames(raw):
    """{'belt': 4x4, 'back': 4x4} - the nodes' row-vector matrices in the NIF's root space."""
    nif = read_nif(raw)
    found = {}

    def walk(b, pm):
        if b is None:
            return
        m = nif_static.local_matrix(b) @ pm
        name = (b.get('name') or '').strip().lower()
        if name in ('belt', 'back') and name not in found:
            found[name] = m
        for c in b.get('children') or []:
            walk(c, m)
    for r in nif.roots:
        walk(r, np.identity(4))
    return found


def pack(m):
    a = m.T                                                # the frame node as the weapon root's place on the bone (column-vector)
    return [round(float(x), 5) for x in (a[0, 0], a[1, 0], a[2, 0], a[0, 1], a[1, 1], a[2, 1], a[0, 2], a[1, 2], a[2, 2],
                                         a[0, 3], a[1, 3], a[2, 3])]


def main():
    path = ROOT / 'godot/content/weapons/weapons.json'
    data = json.loads(path.read_text(encoding='utf-8'))
    files = {p.name.lower(): p for p in (CLIENT / 'items').iterdir()}
    n = {'belt': 0, 'back': 0}
    moved = 0
    for model, w in data['models'].items():
        src = files.get(str(w.get('nif', '')).lower() + '.nif')
        if src is None:
            continue
        try:
            f = frames(src.read_bytes())
        except Exception:
            continue
        for k in ('belt', 'back'):
            if k in f:
                w[k + '_frame'] = pack(f[k])
                n[k] += 1
            else:
                w.pop(k + '_frame', None)
        if any(k in f and not np.allclose(f[k], np.identity(4), atol=1e-3) for k in ('belt', 'back')):
            moved += 1
    path.write_text(json.dumps(data, indent=1), encoding='utf-8')
    print(n['belt'], 'belt frames,', n['back'], 'back frames;', moved, 'weapons whose sheath frame is not the grip')


if __name__ == '__main__':
    main()
