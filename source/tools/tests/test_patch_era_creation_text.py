"""Tests for source/tools/patch_era_creation_text.py (#634), on a synthetic game.dll (no client files in CI).

    python3 -m unittest discover -s source/tools/tests -p test_patch_era_creation_text.py -v
"""
import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(REPO / "tools" / "fork-build"))

import make_content_patch  # noqa: E402
import patch_era_creation_text as era  # noqa: E402

# Made-up texts laid out as the 1.127e client lays out its creation block (#634): class descriptions on their own,
# naming the class somewhere; each race's description followed by NUL padding and the race's name.
CLASS_TEXTS = {
    "Heretic": "Heretics are zealots of an old and forbidden faith, who bend blood and fire to their will in battle.",
    "Warlock": "Born of a cult in the deep caves of the north, Warlocks bind their own life into devastating chambers.",
    "Valkyrie": "Valkyries are shieldmaidens of the north, warriors blessed by the hand of the gods to fight and heal.",
    "Vampiir": "The Vampiir class is a fighter of the underworld who feeds upon the strength of the foes it strikes.",
    "Bainshee": "The Bainshee is a spirit of the dead whose wailing voice breaks the minds of the living around her.",
    "Mauler": "Maulers are martial artists who draw their power from the earth and the moon to strike their foes.",
}
RACE_TEXTS = {
    "Shar": "The Shar came up from below at their queen's word, and the elves have never trusted them since then.",
    "Frostalf": "The Frostalf are distant kin of the dark elves, sent into exile long ago into the frozen deep caves.",
    "Minotaur": "The Minotaur, with their bull-like heads on powerful bodies, are the last of an ancient proud people.",
    "HalfOgre": "Hailing from an isle of mists, this half-breed race is as clever as it is strong and fears nothing.",
}


def fake_dll(extra: bytes = b"", seed: bytes = b"1.127e") -> bytes:
    """A small x86 PE image with the creation block laid out as in the real client."""
    head = bytearray(0x200)
    head[:2] = b"MZ"
    struct.pack_into("<I", head, 0x3C, 0x80)
    head[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<H", head, 0x84, 0x14C)
    head[0x100:0x100 + len(seed)] = seed
    body = bytearray(b"\0Mauler\0\0Warlock\0Vampiir\0Bainshee\0Valkyrie\0Heretic\0%s+%d Vampiir Bonus\0Mauler Staff\0")
    for text in CLASS_TEXTS.values():
        body += text.encode() + b"\0" * 3
    body += b"The basic fighting class of the realm, who can use almost every weapon and armor that there is.\0\0"
    for name, text in RACE_TEXTS.items():
        body += text.encode() + b"\0" * 4 + name.encode() + b"\0" * 4
    body += b"The Britons are the people of Albion, steady and many and strong, and fear no foe at all.\0\0Briton\0\0"
    body += b"Sluaghbinder\0\0\0\0Sluaghbinders are Hibernian battle-mystics who bind the dead to their will.\0"
    body += b"Minotaur (Korazh)\0Half-Ogre\0Shar\0Frostalf\0"
    return bytes(head + body + extra)


class EraTextTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.app = Path(self.tmp.name) / "app"
        self.app.mkdir()
        self.original = fake_dll()
        self.released = hashlib.sha256(self.original).hexdigest()
        self.dll = self.app / "game.dll"
        self.dll.write_bytes(self.original)

    def tearDown(self):
        self.tmp.cleanup()

    def test_wrong_hash_is_refused(self):
        with self.assertRaises(era.Refused):
            era.patch_bytes(self.original)               # the real RELEASED_SHA256, not this file's
        with self.assertRaises(era.Refused):
            era.do_patch(self.dll)
        self.assertEqual(self.dll.read_bytes(), self.original)
        self.assertFalse((self.app / era.BACKUP).exists())

    def test_037b_like_file_is_refused(self):
        b_client = fake_dll(seed=b"0.37b Sluaghbinder")
        self.dll.write_bytes(b_client)
        with self.assertRaises(era.Refused) as e:
            era.do_patch(self.dll, self.released)
        self.assertEqual(self.dll.read_bytes(), b_client)
        self.assertEqual(era.state(self.dll, self.released, fixed=set()), "other")
        with self.assertRaises(era.Refused):
            era.do_restore(self.dll, self.released)
        self.assertIn("refusing", str(e.exception))
        # The real 0.37b hash is named as such.
        self.assertIn("0.37b", era.KNOWN_OTHER[era.SLUAGH_SHA256])
        self.assertNotEqual(era.SLUAGH_SHA256, era.RELEASED_SHA256)

    def test_patch_preserves_lengths_and_offsets(self):
        patched, slots = era.patch_bytes(self.original, self.released)
        self.assertEqual(len(patched), len(self.original))
        self.assertEqual({s.target.label for s in slots}, set(CLASS_TEXTS) | {"Shar", "Frostalf", "Minotaur", "Half Ogre"})
        for slot in slots:
            new = patched[slot.offset:slot.offset + slot.length]
            self.assertEqual(len(new), slot.length)
            self.assertEqual(patched[slot.offset + slot.length], 0, "terminator kept")
            text = new.rstrip(b"\0").decode("ascii")
            self.assertIn(text, era.TEXTS)
            self.assertEqual(new, text.encode() + b"\0" * (slot.length - len(text)), "NUL-padded")
            self.assertTrue(patched[slot.offset:].startswith(b"Not available in this era (Classic and Shrouded Isles)."))
            if slot.target.kind == "race":
                name = slot.target.names[0].encode()
                self.assertEqual(patched[slot.name_offset:slot.name_offset + len(name)], name, "name kept")
        # Era classes and races, the name strings and the Sluaghbinder text are untouched.
        for keep in (b"The Britons are the people", b"Sluaghbinders are Hibernian", b"The basic fighting class",
                     b"\0Mauler\0", b"Vampiir Bonus", b"Mauler Staff", b"Minotaur (Korazh)", b"\0Shar\0"):
            self.assertIn(keep, patched)
        # Only the descriptions and the PE checksum changed.
        changed = era.changed_ranges(self.original, patched)
        inside = [(a, b) for a, b in changed if not any(s.offset <= a and b <= s.offset + s.length for s in slots)]
        checksum = 0x80 + 24 + 64
        self.assertTrue(all(checksum <= a and b <= checksum + 4 for a, b in inside), inside)

    def test_patch_restore_round_trip(self):
        slots = era.do_patch(self.dll, self.released)
        self.assertTrue(slots)
        saved = self.app / era.BACKUP
        self.assertEqual(saved.read_bytes(), self.original)
        self.assertEqual(era.state(self.dll, self.released, fixed=set()), "patched")
        self.assertEqual(era.do_patch(self.dll, self.released), [], "a second run changes nothing")
        era.do_restore(self.dll, self.released)
        self.assertEqual(self.dll.read_bytes(), self.original, "byte for byte")
        self.assertEqual(era.state(self.dll, self.released, fixed=set()), "released")
        # Patch again from the kept backup: the same bytes.
        era.do_patch(self.dll, self.released)
        self.assertEqual(self.dll.read_bytes(), era.patch_bytes(self.original, self.released)[0])

    def test_restore_refuses_a_later_change(self):
        era.do_patch(self.dll, self.released)
        changed = bytearray(self.dll.read_bytes())
        changed[-1] ^= 0xFF
        self.dll.write_bytes(bytes(changed))
        with self.assertRaises(era.Refused):
            era.do_restore(self.dll, self.released)
        self.assertEqual(self.dll.read_bytes(), bytes(changed))

    def test_bad_backup_is_never_overwritten(self):
        saved = self.app / era.BACKUP
        saved.parent.mkdir(parents=True)
        saved.write_bytes(b"someone else's file")
        with self.assertRaises(era.Refused):
            era.do_patch(self.dll, self.released)
        self.assertEqual(saved.read_bytes(), b"someone else's file")
        self.assertEqual(self.dll.read_bytes(), self.original)

    def test_unexpected_counts_refuse(self):
        # A second Heretic description: the layout is not the one expected.
        extra = b"\0A second text about Heretics, long enough to be taken for a creation description by the finder.\0"
        data = fake_dll(extra)
        with self.assertRaises(era.Refused) as e:
            era.patch_bytes(data, hashlib.sha256(data).hexdigest())
        self.assertIn("Heretic: found 2", str(e.exception))
        # A missing class description refuses too.
        data = fake_dll().replace(b"Warlocks bind", b"Wxrlocks bind")
        with self.assertRaises(era.Refused) as e:
            era.patch_bytes(data, hashlib.sha256(data).hexdigest())
        self.assertIn("Warlock: found 0", str(e.exception))

    def test_content_patch_entry_matches_installer_format(self):
        entry = era.content_entry(self.original, self.released)
        patched = era.patch_bytes(self.original, self.released)[0]
        self.assertEqual(entry["Path"], "runtime/client-opendaoc/app/game.dll")
        self.assertEqual(entry["Released"], self.released)
        self.assertEqual(entry["Fixed"], hashlib.sha256(patched).hexdigest())
        self.assertEqual(entry["Size"], len(patched))
        # The installer's own reader (make_content_patch.apply) rebuilds the patched file.
        self.assertEqual(make_content_patch.apply(self.original, entry["Ops"]), patched)
        out = Path(self.tmp.name) / "content-patches.json"
        out.write_text(json.dumps([{"Path": "runtime/server/navmesh/zone1.nav", "Released": "a", "Fixed": "b",
                                    "Size": 1, "Ops": []}]), encoding="utf-8")
        era.write_content_entry(out, entry)
        era.write_content_entry(out, entry)          # replaces, never duplicates
        entries = json.loads(out.read_text(encoding="utf-8"))
        self.assertEqual([e["Path"] for e in entries], ["runtime/server/navmesh/zone1.nav", entry["Path"]])

    def test_repository_content_patch_only_targets_the_released_client(self):
        entries = json.loads((REPO / "tools/fork-build/content-patches.json").read_text(encoding="utf-8"))
        for e in entries:
            if e["Path"].lower().endswith("game.dll"):
                self.assertEqual(e["Path"], era.CONTENT_PATH)
                self.assertEqual(e["Released"], era.RELEASED_SHA256, "only the verified 1.127e client")
                self.assertNotEqual(e["Fixed"], era.SLUAGH_SHA256)
                self.assertNotIn("File", e, "byte ops against the released file, never a shipped game.dll")

    def test_cli_find_and_verify(self):
        self.assertEqual(era.main(["find", str(self.app)]), 2, "find refuses a file without the released hash")
        self.assertEqual(era.main(["verify", str(self.dll)]), 2)


if __name__ == "__main__":
    unittest.main()
