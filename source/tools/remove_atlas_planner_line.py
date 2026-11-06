"""Remove the Atlas planner line from the original client's character creation screen.

The 1.127 client's creation canvas (pregame/pregame.mpk, character_creation.tga) has the Atlas freeshard's
"Class / Race Combination: https://planner.atlasfreeshard.com" painted across its top stone bar. This copies that
strip (x 20-545, y 0-40, above the curved border) from the character selection canvas, which has the same stones
without text. Every other entry of pregame.mpk is copied unchanged. The original is kept in
<client>/backups/no-atlas-planner-text-20261030/pregame.mpk (written once, never overwritten).

    python remove_atlas_planner_line.py <client folder>     e.g. runtime/client-opendaoc/app

Needs Pillow. Safe to run twice (the second run writes the same file again from the kept original).
"""
import io
import os
import shutil
import struct
import sys
import zlib

from PIL import Image

TEXT_BOX = (20, 0, 546, 41)
BACKUP = os.path.join('backups', 'no-atlas-planner-text-20261030', 'pregame.mpk')


def unpack_mpk(data):
    if data[:5] != b"MPAK\x02":
        raise ValueError("not an MPK archive")
    crc, dir_size, name_size, count = struct.unpack("<4I", bytes(v ^ i for i, v in enumerate(data[5:21])))
    name = zlib.decompress(data[21:21 + name_size])
    cdir = data[21 + name_size:21 + name_size + dir_size]
    if zlib.crc32(cdir) & 0xFFFFFFFF != crc:
        raise ValueError("MPK directory checksum mismatch")
    directory = zlib.decompress(cdir)
    payload = data[21 + name_size + dir_size:]
    entries = []
    for k in range(count):
        row = directory[k * 284:(k + 1) * 284]
        fname = row[:256].split(b"\0", 1)[0].decode("latin1")
        stamp, flags, memory, size, offset, csize, fcrc = struct.unpack("<7I", row[256:])
        content = zlib.decompress(payload[offset:offset + csize])
        if len(content) != size:
            raise ValueError(f"{fname}: bad size")
        entries.append([fname, content, stamp, flags])
    return name, entries


def pack_mpk(name, entries):
    directory, payload, memory = bytearray(), bytearray(), 0
    for fname, content, stamp, flags in entries:
        packed = zlib.compress(content, 9)
        directory += fname.encode("latin1").ljust(256, b"\0")
        directory += struct.pack("<7I", stamp, flags, memory, len(content), len(payload), len(packed),
                                 zlib.crc32(packed) & 0xFFFFFFFF)
        memory += len(content)
        payload += packed
    names = zlib.compress(name, 9)
    cdir = zlib.compress(directory, 9)
    header = struct.pack("<4I", zlib.crc32(cdir) & 0xFFFFFFFF, len(cdir), len(names), len(entries))
    out = b"MPAK\x02" + bytes(v ^ i for i, v in enumerate(header)) + names + cdir + payload
    back_name, back = unpack_mpk(out)               # round trip check
    assert back_name == name and [(e[0], e[1]) for e in back] == [(e[0], e[1]) for e in entries]
    return out


def main():
    client = sys.argv[1] if len(sys.argv) > 1 else sys.exit(__doc__)
    target = os.path.join(client, 'pregame', 'pregame.mpk')
    backup = os.path.join(client, BACKUP)
    if not os.path.exists(backup):
        os.makedirs(os.path.dirname(backup), exist_ok=True)
        shutil.copy2(target, backup)
    name, entries = unpack_mpk(open(backup, 'rb').read())
    by = {e[0].lower(): e for e in entries}
    creation = Image.open(io.BytesIO(by['character_creation.tga'][1]))
    selection = Image.open(io.BytesIO(by['character_selection.tga'][1]))
    assert creation.size == selection.size == (1024, 768)
    creation = creation.copy()
    creation.paste(selection.crop(TEXT_BOX), TEXT_BOX[:2])
    buf = io.BytesIO()
    creation.save(buf, 'TGA', compression=None)
    by['character_creation.tga'][1] = buf.getvalue()
    data = pack_mpk(name, entries)
    open(target + '.new', 'wb').write(data)
    os.replace(target + '.new', target)            # a new file: other hard links keep the old one
    print('written', target, '(original kept in', backup + ')')


if __name__ == '__main__':
    main()
