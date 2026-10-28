"""Builds tools/fork-build/content-patches.json: game files the installer changes in place (a fixed navmesh, the new
client's door data), as byte patches against the released file, so no game file is shipped whole.

    python make_content_patch.py <out.json> <relative path> <released file> <fixed file> [<relative path> <released> <fixed> ...]

Each entry names the file (relative to the game folder, forward slashes), its released and fixed SHA-256, and the ops
that turn one into the other: {"copy": [offset, length]} takes bytes of the released file, {"data": base64} inserts new
bytes. The installer (fork-build-common.ps1, Expand-ContentPatch) changes a file only when it has the released
fingerprint, checks the result against the fixed one, and backs the file up first.

An entry may instead name a whole file shipped in the build folder ("File": "content/<name>", no "Ops"), for a file
that is entirely Living Server's own (Beta 1.1: the game's loading screen, pregame/splash.mpk -> content/
pregame-splash.mpk). Those entries are written by hand and kept when this script rewrites the list.
"""
import base64
import difflib
import hashlib
import json
import sys


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


def main(argv):
    if len(argv) < 5 or (len(argv) - 2) % 3:
        print(__doc__)
        return 2
    entries = []
    for i in range(2, len(argv), 3):
        rel, released, fixed = argv[i], argv[i + 1], argv[i + 2]
        old, new = open(released, 'rb').read(), open(fixed, 'rb').read()
        ops = ops_between(old, new)
        assert apply(old, ops) == new, rel
        entries.append({'Path': rel.replace('\\', '/'), 'Released': hashlib.sha256(old).hexdigest(),
                        'Fixed': hashlib.sha256(new).hexdigest(), 'Size': len(new), 'Ops': ops})
        print(f'{rel}: {len(ops)} ops, {sum(len(base64.b64decode(o["data"])) for o in ops if "data" in o)} new bytes')
    # Whole-file entries ("File") are not made here; keep the ones already in the list (paths given here win).
    try:
        with open(argv[1], encoding='utf-8') as f:
            kept = [e for e in json.load(f) if 'File' in e and e['Path'] not in {x['Path'] for x in entries}]
    except FileNotFoundError:
        kept = []
    entries += kept
    with open(argv[1], 'w', encoding='utf-8', newline='\n') as f:
        json.dump(entries, f, indent=1)
        f.write('\n')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
