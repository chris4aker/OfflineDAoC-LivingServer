"""The Windows portable setup (tools/grafana/windows; issue #628): 127.0.0.1 only, every path in one folder, pinned
downloads, the kit's own Alloy filter.

The PowerShell checks need pwsh (PWSH=/path/to/pwsh, or pwsh on PATH) and are skipped without it: they parse every
script, and run setup.ps1 -ConfigOnly into a temp folder, then check what it wrote.
"""
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import stack  # noqa: E402

REPO = HERE.parents[2]
WIN = REPO / "tools/grafana/windows"
PWSH = os.environ.get("PWSH") or shutil.which("pwsh")
ROOT = "C:/Games/OfflineDAoC/observability"


def render(name: str, root: str = ROOT) -> str:
    return (WIN / "templates" / name).read_text(encoding="utf-8").replace(stack.ROOT, root)


def paths_in(text: str):
    return re.findall(r"(?:[A-Za-z]:)?/[\w./ -]*\w", text)


class TemplateTests(unittest.TestCase):
    def test_loki_listens_on_loopback_and_keeps_its_data_inside(self):
        text = render("loki.yaml")
        self.assertEqual(re.findall(r"(?:http|grpc)_listen_address: (\S+)", text), ["127.0.0.1", "127.0.0.1"])
        self.assertIn("retention_period: 336h", text)
        self.assertIn("reporting_enabled: false", text)
        for p in re.findall(r"(?:directory|path_prefix): (\S+)", text):
            self.assertTrue(p.startswith(ROOT + "/data/loki"), p)

    def test_grafana_listens_on_loopback_without_anonymous_access(self):
        text = render("grafana.ini")
        self.assertRegex(text, r"(?m)^http_addr = 127\.0\.0\.1$")
        self.assertRegex(text, r"(?m)^\[auth\.anonymous\]\nenabled = false$")
        self.assertRegex(text, r"(?m)^allow_sign_up = false$")
        self.assertRegex(text, r"(?m)^reporting_enabled = false$")
        self.assertIn("admin_password = $__file{" + ROOT + "/admin-password.txt}", text)
        for key in ("data", "logs", "plugins", "provisioning"):
            value = re.search(rf"(?m)^{key} = (.+)$", text).group(1)
            self.assertTrue(value.startswith(ROOT + "/"), (key, value))

    def test_provisioning_points_at_the_local_loki_and_folder(self):
        self.assertIn("url: http://127.0.0.1:3100", render("datasource.yaml"))
        self.assertIn(f"path: {ROOT}/dashboards", render("dashboards.yaml"))

    def test_nothing_binds_beyond_loopback_and_the_game_port_is_untouched(self):
        for path in WIN.rglob("*"):
            if path.is_file():
                text = path.read_text(encoding="utf-8")
                self.assertNotIn("0.0.0.0", text, path)
                self.assertNotIn("10300", text, path)
        ps = (WIN / "observability.ps1").read_text(encoding="utf-8")
        self.assertIn("--server.http.listen-addr=127.0.0.1:12345", ps)
        self.assertIn("[System.Net.IPAddress]::Loopback", ps)

    def test_the_docker_stack_publishes_on_loopback_only(self):
        compose = (REPO / "tools/grafana/docker-compose.yml").read_text(encoding="utf-8")
        published = re.findall(r'- "([^"]+:\d+:\d+)"', compose)
        self.assertEqual(len(published), 3)
        for p in published:
            self.assertTrue(p.startswith("127.0.0.1:"), p)


class PinTests(unittest.TestCase):
    def releases(self):
        text = (WIN / "setup.ps1").read_text(encoding="utf-8")
        return re.findall(r"Name = '(\w+)'; Version = '([\d.]+)'; File = '([^']+)'\s+Url = '([^']+)'\s+Sha256 = '([^']+)'", text)

    def test_four_pinned_releases_over_https_from_official_urls(self):
        rel = self.releases()
        self.assertEqual([r[0] for r in rel], ["grafana", "loki", "alloy", "gcx"])
        official = ("https://dl.grafana.com/oss/release/", "https://github.com/grafana/loki/releases/download/",
                    "https://github.com/grafana/alloy/releases/download/", "https://github.com/grafana/gcx/releases/download/")
        for name, version, file, url, sha in rel:
            self.assertRegex(sha, r"^[0-9a-f]{64}$", name)
            self.assertTrue(url.startswith(official), url)
            self.assertTrue(url.endswith("/" + file), url)
            self.assertIn(version, url)
            self.assertIn("windows", file)

    def test_the_versions_match_the_docker_stack(self):
        compose = (REPO / "tools/grafana/docker-compose.yml").read_text(encoding="utf-8")
        versions = {r[0]: r[1] for r in self.releases()}
        self.assertIn(f"grafana/grafana:{versions['grafana']}", compose)
        self.assertIn(f"grafana/loki:{versions['loki']}", compose)
        self.assertIn(f"grafana/alloy:v{versions['alloy']}", compose)

    def test_a_mismatch_is_refused(self):
        text = (WIN / "setup.ps1").read_text(encoding="utf-8")
        self.assertIn("SHA256 mismatch", text)
        self.assertRegex(text, r"if \(\$hash -ne \$r\.Sha256\) \{\s+Remove-Item -LiteralPath \$zip -Force\s+throw")


@unittest.skipUnless(PWSH, "no pwsh (set PWSH)")
class PowerShellTests(unittest.TestCase):
    def pwsh(self, *args, **kw):
        return subprocess.run([PWSH, "-NoProfile", "-NonInteractive", *args], capture_output=True, text=True, timeout=180, **kw)

    def test_every_script_parses(self):
        for ps1 in sorted(WIN.glob("*.ps1")):
            script = ("$e = $null; $t = $null; [System.Management.Automation.Language.Parser]::ParseFile('"
                      + str(ps1) + "', [ref]$t, [ref]$e) | Out-Null; $e | ForEach-Object { $_.ToString() }; exit $e.Count")
            r = self.pwsh("-Command", script)
            self.assertEqual(r.returncode, 0, f"{ps1.name}: {r.stdout}{r.stderr}")

    def test_config_only_writes_everything_inside_the_folder(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            root, server = tmp / "observability", tmp / "LivingServer"
            (server / "runtime/server/logs").mkdir(parents=True)
            before = sorted(p for p in tmp.rglob("*"))
            r = self.pwsh("-File", str(WIN / "setup.ps1"), "-InstallRoot", str(server), "-ObservabilityRoot", str(root),
                          "-AllowOtherRoot", "-ConfigOnly")
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
            after = sorted(p for p in tmp.rglob("*"))
            outside = [p for p in after if p not in before and root not in p.parents and p != root]
            self.assertEqual(outside, [])
            self.assertEqual(sorted(p for p in server.rglob("*")), sorted(p for p in before if server in p.parents))
            slash = str(root).replace("\\", "/")
            loki = (root / "loki.yaml").read_text(encoding="utf-8")
            self.assertEqual(re.findall(r"(?:http|grpc)_listen_address: (\S+)", loki), ["127.0.0.1", "127.0.0.1"])
            self.assertIn(f"path_prefix: {slash}/data/loki", loki)
            ini = (root / "grafana.ini").read_text(encoding="utf-8")
            self.assertRegex(ini, r"(?m)^http_addr = 127\.0\.0\.1$")
            self.assertIn(f"data = {slash}/data/grafana", ini)
            self.assertNotIn("@@ROOT@@", loki + ini)
            # The kit's own filter: the allowlist and the name drops, byte for byte.
            self.assertEqual((root / "alloy/config.alloy").read_bytes(),
                             (REPO / "tools/grafana/alloy/config.alloy").read_bytes())
            self.assertEqual(len(list((root / "resources/dashboards").glob("*.json"))),
                             len(list((REPO / "tools/grafana/resources/dashboards").glob("*.json"))))
            password = (root / "admin-password.txt").read_text(encoding="utf-8")
            self.assertGreaterEqual(len(password), 24)
            settings = json.loads((root / "settings.json").read_text(encoding="utf-8"))
            self.assertEqual(Path(settings["LogsDir"]), server / "runtime/server/logs")
            # A second run keeps the password.
            r = self.pwsh("-File", str(WIN / "setup.ps1"), "-InstallRoot", str(server), "-ObservabilityRoot", str(root),
                          "-AllowOtherRoot", "-ConfigOnly")
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
            self.assertEqual((root / "admin-password.txt").read_text(encoding="utf-8"), password)

    def test_a_root_outside_the_games_folder_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            r = self.pwsh("-File", str(WIN / "setup.ps1"), "-InstallRoot", tmp, "-ObservabilityRoot",
                          str(Path(tmp) / "obs"), "-ConfigOnly")
            self.assertNotEqual(r.returncode, 0)
            self.assertIn("must be under", r.stdout + r.stderr)
            self.assertFalse((Path(tmp) / "obs").exists())


if __name__ == "__main__":
    unittest.main()
