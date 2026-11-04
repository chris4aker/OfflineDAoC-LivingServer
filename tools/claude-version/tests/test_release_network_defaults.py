"""build_release_037.py's network defaults (Living Server 1.1.2): every shipped serverconfig.xml listens on 127.0.0.1 only
with UPnP off, and the build refuses a config that doesn't. Temporary files only; no install is read or written.

    python tools/claude-version/tests/test_release_network_defaults.py
"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "source" / "tools"))
import build_release_037 as build  # noqa: E402

# The shape of an upstream serverconfig.xml (BOM, CRLF), with upstream's open defaults.
OPEN_CONFIG = ("﻿<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<root>\r\n    <Server>\r\n        <Port>10300</Port>\r\n"
               "        <IP>0.0.0.0</IP>\r\n        <RegionIP>0.0.0.0</RegionIP>\r\n        <UdpIP>0.0.0.0</UdpIP>\r\n"
               "        <UdpPort>10400</UdpPort>\r\n        <EnableUPnP>True</EnableUPnP>\r\n        <DetectRegionIP>True</DetectRegionIP>\r\n"
               "        <AutoAccountCreation>True</AutoAccountCreation>\r\n    </Server>\r\n</root>\r\n")


class NetworkDefaultsTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.runtime = Path(self.folder.name) / "runtime"
        self.config = self.runtime / "server/config/serverconfig.xml"
        self.config.parent.mkdir(parents=True)
        self.config.write_bytes(OPEN_CONFIG.encode("utf-8"))

    def tearDown(self):
        self.folder.cleanup()

    def test_an_open_config_is_refused(self):
        with self.assertRaises(AssertionError) as raised:
            build.check_network_defaults(self.config)
        self.assertIn("127.0.0.1", str(raised.exception))

    def test_the_build_makes_it_this_pc_only_and_keeps_the_rest_of_the_file(self):
        build.set_network_defaults(self.config)
        values = build.check_network_defaults(self.config)
        self.assertEqual(values, {"IP": "127.0.0.1", "UdpIP": "127.0.0.1", "EnableUPnP": "False", "DetectRegionIP": "False"})
        raw = self.config.read_bytes()
        self.assertTrue(raw.startswith(b"\xef\xbb\xbf"), "the BOM stays")
        self.assertNotIn(b"\n", raw.replace(b"\r\n", b""), "CRLF line endings stay")
        self.assertIn(b"<AutoAccountCreation>True</AutoAccountCreation>", raw)
        self.assertIn(b"<Port>10300</Port>", raw)

    def test_a_missing_setting_is_added(self):
        self.config.write_bytes(OPEN_CONFIG.replace("        <EnableUPnP>True</EnableUPnP>\r\n", "").encode("utf-8"))
        build.set_network_defaults(self.config)
        self.assertEqual(build.check_network_defaults(self.config)["EnableUPnP"], "False")

    def test_every_shipped_config_is_checked(self):
        second = self.runtime / "server/win-x64/config/serverconfig.xml"
        second.parent.mkdir(parents=True)
        second.write_bytes(OPEN_CONFIG.encode("utf-8"))
        build.set_network_defaults(self.config)
        with self.assertRaises(AssertionError):
            build.check_shipped_configs(self.runtime)
        build.set_network_defaults(second)
        self.assertEqual(sorted(build.check_shipped_configs(self.runtime)),
                         ["server/config/serverconfig.xml", "server/win-x64/config/serverconfig.xml"])

    def test_a_runtime_without_a_config_is_refused(self):
        self.config.unlink()
        with self.assertRaises(AssertionError):
            build.check_shipped_configs(self.runtime)


if __name__ == "__main__":
    unittest.main()
