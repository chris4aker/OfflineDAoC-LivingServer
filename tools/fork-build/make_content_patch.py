"""Builds tools/fork-build/content-patches.json: game files the installer changes in place (a fixed navmesh, the new
client's door data), as byte patches against the released file, so no game file is shipped whole.

    python make_content_patch.py [--navmesh-set DIR [--manifest FILE]] <out.json> <relative path> <released file> <fixed file>
                                 [<relative path> <released> <fixed> ...]

A navmesh (runtime/server/navmesh/...) is patched only to the mesh of the published navmesh set (issue #687), never to a
file from a PC folder: pass --navmesh-set, the folder `navmesh_set.py fetch tools/navmesh/manifest.json --dest DIR`
unpacked and checked, and write `set` for its fixed file. The mesh is taken by its manifest path (navmesh/zoneNNN.nav),
after checking that DIR was fetched against that manifest (default tools/navmesh/manifest.json) and that the file still
has the manifest's SHA-256. `navmesh_set.py check` then fails if the list and the set ever drift apart. A navmesh
rebuild (#582) publishes navmesh-vN+1 and its patches are made from that set.

Each entry names the file (relative to the game folder, forward slashes), its released and fixed SHA-256, and the ops
that turn one into the other: {"copy": [offset, length]} takes bytes of the released file, {"data": base64} inserts new
bytes. The installer (fork-build-common.ps1, Expand-ContentPatch) changes a file only when it has the released
fingerprint, checks the result against the fixed one, and backs the file up first.

An entry may instead name a whole file shipped in the build folder ("File": "content/<name>", no "Ops"), for a file
that is entirely Living Server's own (Beta 1.1: the game's loading screen, pregame/splash.mpk -> content/
pregame-splash.mpk). Those entries are written by hand (the loading screen's by make_pregame_splash.py
--update-patches) and kept as they are when this script rewrites the list, with their two optional fields:
  "Replaces": [sha256, ...]  earlier Living Server versions of the file the installer may also replace (1.2: the 1.1
                             loading screen); a file that is none of these nor the released one is left alone.
  "Keep": true               Living Server's own file stays on rollback: ROLLBACK.cmd puts back only an earlier Living
                             Server version, never the released file, and PREPARE-FOR-UPDATE.cmd leaves it.
Both are honoured only on "File" entries (byte ops need the released file), and this script never writes them.
"""
import base64
import difflib
import hashlib
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1] / "tools" / "navmesh"))
import navmesh_set  # noqa: E402

NAVMESH_PREFIX = "runtime/server/navmesh/"
FROM_SET = "set"


def ops_between(old: bytes, new: bytes):
    prefix = 0
    limit = min(len(old), len(new))
    while prefix < limit and old[prefix] == new[prefix]:
        prefix += 1
    suffix = 0
    while suffix < limit - prefix and old[len(old) - 1 - suffix] == new[len(new) - 1 - suffix]:
        suffix += 1
    ops = []

    def copy(offset, length):
        if length <= 0:
            return
        if ops and 'copy' in ops[-1] and sum(ops[-1]['copy']) == offset:
            ops[-1]['copy'][1] += length
        else:
            ops.append({'copy': [offset, length]})

    def data(chunk):
        if chunk:
            ops.append({'data': chunk})

    copy(0, prefix)
    a, b = old[prefix:len(old) - suffix], new[prefix:len(new) - suffix]
    matcher = difflib.SequenceMatcher(None, a, b, autojunk=False)
    pending = b''
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        # Short matches cost more as a copy op than as data.
        if tag == 'equal' and i2 - i1 >= 16:
            data(pending)
            pending = b''
            copy(prefix + i1, i2 - i1)
        else:
            pending += b[j1:j2]
    data(pending)
    copy(len(old) - suffix, suffix)
    for op in ops:
        if 'data' in op:
            op['data'] = base64.b64encode(op['data']).decode('ascii')
    return ops


def apply(old: bytes, ops) -> bytes:
    out = bytearray()
    for op in ops:
        if 'copy' in op:
            offset, length = op['copy']
            out += old[offset:offset + length]
        else:
            out += base64.b64decode(op['data'])
    return bytes(out)


def fixed_bytes(rel: str, fixed: str, navmesh_set_dir, manifest) -> bytes:
    """The fixed file's bytes: a navmesh from the verified set (issue #687), anything else from the file given."""
    if not rel.startswith(NAVMESH_PREFIX):
        if fixed == FROM_SET:
            raise navmesh_set.SetError(f"{rel}: only a navmesh ({NAVMESH_PREFIX}...) comes from the navmesh set")
        return open(fixed, 'rb').read()
    if navmesh_set_dir is None or fixed != FROM_SET:
        raise navmesh_set.SetError(
            f"{rel}: a navmesh patch is made from the published navmesh set, not from {fixed!r}: fetch it "
            f"(navmesh_set.py fetch tools/navmesh/manifest.json --dest DIR), pass --navmesh-set DIR and write "
            f"'{FROM_SET}' for the fixed file (#687)")
    return navmesh_set.set_file(manifest, navmesh_set_dir, rel[len('runtime/server/'):])


def parse_args(argv):
    """(navmesh set folder or None, manifest, positional args) from argv[1:]."""
    rest, set_dir, manifest = [], None, navmesh_set.MANIFEST
    args = iter(argv[1:])
    for a in args:
        if a in ('--navmesh-set', '--manifest'):
            value = next(args, None)
            if value is None:
                raise navmesh_set.SetError(f"{a} needs a value")
            if a == '--navmesh-set':
                set_dir = Path(value)
            else:
                manifest = Path(value)
        else:
            rest.append(a)
    return set_dir, manifest, rest


def main(argv):
    try:
        set_dir, manifest, args = parse_args(argv)
    except navmesh_set.SetError as e:
        print(f"error: {e}")
        return 2
    if len(args) < 4 or (len(args) - 1) % 3:
        print(__doc__)
        return 2
    argv = [argv[0]] + args
    entries = []
    for i in range(2, len(argv), 3):
        rel, released, fixed = argv[i].replace('\\', '/'), argv[i + 1], argv[i + 2]
        try:
            new = fixed_bytes(rel, fixed, set_dir, manifest)
        except navmesh_set.SetError as e:
            print(f"error: {e}")
            return 1
        old = open(released, 'rb').read()
        ops = ops_between(old, new)
        assert apply(old, ops) == new, rel
        entries.append({'Path': rel.replace('\\', '/'), 'Released': hashlib.sha256(old).hexdigest(),
                        'Fixed': hashlib.sha256(new).hexdigest(), 'Size': len(new), 'Ops': ops})
        print(f'{rel}: {len(ops)} ops, {sum(len(base64.b64decode(o["data"])) for o in ops if "data" in o)} new bytes')
    # Whole-file entries ("File") are not made here; keep the ones already in the list (paths given here win).
    try:
        with open(argv[1], encoding='utf-8') as f:
            kept = [e for e in json.load(f) if 'File' in e and e['Path'] not in {x['Path'] for x in entries}]
        for e in entries + kept:
            assert 'File' in e or not ('Replaces' in e or 'Keep' in e), f"{e['Path']}: Replaces and Keep need a File entry"
    except FileNotFoundError:
        kept = []
    entries += kept
    with open(argv[1], 'w', encoding='utf-8', newline='\n') as f:
        json.dump(entries, f, indent=1)
        f.write('\n')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
