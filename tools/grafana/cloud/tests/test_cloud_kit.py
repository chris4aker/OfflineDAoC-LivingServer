"""Tests for the Grafana Cloud kit (issue #649): detect and the env check on all four paths, the install dry run, the
Alloy configs (portable, privacy), and the PowerShell scripts when pwsh is present.

    python3 -m unittest discover -s tools/grafana/cloud/tests
    ALLOY_BIN=/path/to/alloy python3 -m unittest discover -s tools/grafana/cloud/tests   # also runs a real Alloy
    PWSH_BIN=/path/to/pwsh ...                                                            # also parses and runs the .ps1
"""
from __future__ import annotations

import json
import os
import re
import shutil
import socket
import stat
import subprocess
import tempfile
import time
import unittest
import urllib.request
from pathlib import Path

KIT = Path(__file__).resolve().parents[1]
FIXTURE_LOG = KIT / "tests" / "fixtures" / "server.log"
REQUIRED = ["GC_PROM_URL", "GC_PROM_USER", "GC_LOKI_URL", "GC_LOKI_USER", "GC_TOKEN", "LS_LOG_DIR", "LS_INSTANCE"]
SECRET = "glc_THIS_MUST_NEVER_BE_PRINTED"
# Values in the fixture that must never reach Loki: a player's and a bot's name, chat text, a Windows user folder, an IP.
PRIVATE = ["Testplayer", "Testaccount", "Aelwynbot", "Knights", "secret words", "player1", "192.168.1.20", "10.0.0.5",
           "C:\\Users", "boom", "Bot.cs"]


def _stub(folder: Path, name: str, body: str) -> None:
    p = folder / name
    p.write_text("#!/bin/sh\n" + body + "\n")
    p.chmod(p.stat().st_mode | stat.S_IEXEC)


class DetectLinux(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.home = self.tmp / "home"
        self.logs = self.home / "OfflineDAoC" / "LivingServer" / "runtime" / "server" / "logs"
        self.logs.mkdir(parents=True)
        (self.logs / "server.log").write_text("")
        self.stub = self.tmp / "stub"
        self.stub.mkdir()
        # A quiet PC by default: no services, no processes, nothing listening.
        _stub(self.stub, "systemctl", 'echo inactive; exit 3')
        _stub(self.stub, "pgrep", 'exit 1')
        _stub(self.stub, "ss", 'exit 0')
        _stub(self.stub, "timedatectl", 'echo America/New_York')

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def detect(self, *args: str) -> dict:
        env = {"HOME": str(self.home), "PATH": f"{self.stub}:/usr/bin:/bin"}
        out = subprocess.run(["bash", str(KIT / "detect.sh"), *args], env=env, capture_output=True, text=True, check=True)
        return json.loads(out.stdout)

    def write_env(self, complete: bool) -> Path:
        home = self.home / ".local" / "share" / "livingserver-monitoring"
        home.mkdir(parents=True, exist_ok=True)
        lines = [f"{k}={SECRET if k == 'GC_TOKEN' else 'x'}" for k in REQUIRED if complete or k != "GC_TOKEN"]
        env = home / "monitoring.env"
        env.write_text("\n".join(lines) + "\n")
        env.chmod(0o600)
        return env

    def test_path_a_new_account_no_alloy(self):
        r = self.detect("--has-account", "no")
        self.assertEqual(r["path"], "A")
        self.assertEqual(r["logs_dirs"], [str(self.logs)])
        self.assertEqual(r["timezone"], "America/New_York")
        self.assertEqual(r["next"], "user_credentials")
        self.assertEqual(r["env"]["missing_keys"], REQUIRED)
        self.assertEqual(r["free_port"], 12347)

    def test_path_b_new_account_system_alloy(self):
        _stub(self.stub, "systemctl", 'case "$*" in *"is-active alloy.service"*) echo active;; *) echo inactive; exit 3;; esac')
        r = self.detect("--has-account", "no")
        self.assertTrue(r["alloy"]["system_service_active"])
        self.assertEqual(r["path"], "B")

    def test_path_c_existing_account_no_alloy(self):
        self.write_env(complete=True)
        r = self.detect("--has-account", "yes")
        self.assertEqual(r["path"], "C")
        self.assertEqual(r["next"], "install")
        self.assertEqual(r["env"]["missing_keys"], [])

    def test_path_d_existing_account_legacy_agent(self):
        _stub(self.stub, "grafana-agent", 'exit 0')
        r = self.detect("--has-account", "yes")
        self.assertTrue(r["grafana_agent"]["installed"])
        self.assertEqual(r["path"], "D")

    def test_unknown_account_asks(self):
        r = self.detect()
        self.assertIsNone(r["path"])
        self.assertEqual(r["next"], "ask_has_account")

    def test_no_logs_asks_for_folder(self):
        shutil.rmtree(self.home / "OfflineDAoC")
        self.assertEqual(self.detect("--has-account", "no")["next"], "ask_logs_folder")

    def test_port_in_use_is_skipped(self):
        _stub(self.stub, "ss", 'echo "LISTEN 0 4096 127.0.0.1:12347 0.0.0.0:*"')
        self.assertEqual(self.detect()["free_port"], 12348)

    def test_running_kit_goes_to_verify(self):
        _stub(self.stub, "systemctl", 'case "$*" in *"--user is-active livingserver-alloy.service"*) echo active;; *) echo inactive; exit 3;; esac')
        self.assertEqual(self.detect("--has-account", "yes")["next"], "verify")

    def test_check_env_never_prints_values(self):
        env = self.write_env(complete=False)
        bad = subprocess.run(["bash", str(KIT / "detect.sh"), "--check-env", str(env)], capture_output=True, text=True)
        self.assertEqual(bad.returncode, 1)
        self.assertIn("GC_TOKEN", bad.stderr)
        env = self.write_env(complete=True)
        ok = subprocess.run(["bash", str(KIT / "detect.sh"), "--check-env", str(env)], capture_output=True, text=True)
        self.assertEqual(ok.returncode, 0, ok.stderr)
        self.assertNotIn(SECRET, ok.stdout + ok.stderr)
        self.assertNotIn(SECRET, json.dumps(self.detect("--has-account", "yes")))


class InstallDryRun(unittest.TestCase):
    def test_dry_run_changes_nothing_and_writes_a_user_unit(self):
        tmp = Path(tempfile.mkdtemp())
        try:
            env = {"HOME": str(tmp), "PATH": "/usr/bin:/bin", "USER": "player"}
            out = subprocess.run(["bash", str(KIT / "install.sh"), "--dry-run", "--port", "12350"], env=env,
                                 capture_output=True, text=True, check=True).stdout
            self.assertEqual(list(tmp.iterdir()), [], "a dry run must not create files")
            self.assertIn("systemctl --user enable --now livingserver-alloy.service", out)
            self.assertIn("EnvironmentFile=", out)
            self.assertNotIn("sudo", out)
            self.assertNotIn("/etc/", out)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_pins_are_release_hashes(self):
        pins = json.loads((KIT / "pins.json").read_text())["alloy"]
        for key in ("linux-amd64", "linux-arm64", "windows-amd64"):
            p = pins[key]
            self.assertRegex(p["sha256"], r"^[0-9a-f]{64}$")
            self.assertTrue(p["url"].startswith(f"https://github.com/grafana/alloy/releases/download/v{pins['version']}/"))
            self.assertTrue(p["url"].endswith("/" + p["file"]))


class Configs(unittest.TestCase):
    FILES = sorted((KIT / "alloy").rglob("*.alloy"))

    def test_nothing_specific_to_one_pc(self):
        for f in [*self.FILES, *KIT.glob("*.sh"), *KIT.glob("*.ps1"), KIT / "monitoring.env.example"]:
            text = f.read_text()
            for bad in ("/home/", "C:\\Users\\", ".grafana.net", "glc_", "glsa_"):
                self.assertNotIn(bad, text, f"{f.name} holds {bad!r}")

    def test_secrets_come_from_the_environment(self):
        text = "\n".join(f.read_text() for f in self.FILES)
        self.assertIn('password = sys.env("GC_TOKEN")', text)
        self.assertNotRegex(text, r'password\s*=\s*"')

    def test_filename_label_dropped(self):
        logs = (KIT / "alloy" / "common" / "logs.alloy").read_text()
        self.assertIn('stage.label_drop {\n    values = ["filename"]', logs)
        self.assertIn('regex  = "component_id|component_path|filename"', (KIT / "alloy" / "common" / "metrics.alloy").read_text())

    def test_example_env_lists_every_required_key(self):
        text = (KIT / "monitoring.env.example").read_text()
        for k in REQUIRED:
            self.assertRegex(text, rf"(?m)^{k}=$")

    def test_ignored_by_git(self):
        repo = KIT.parents[2]
        out = subprocess.run(["git", "check-ignore", "-q", "tools/grafana/cloud/monitoring.env"], cwd=repo)
        self.assertEqual(out.returncode, 0, "monitoring.env must be gitignored")


class Dashboards(unittest.TestCase):
    """The three ✦ dashboards (issue #652)."""
    DIR = KIT / "dashboards"

    def boards(self):
        return {f.stem: json.loads(f.read_text()) for f in sorted(self.DIR.glob("*.json"))}

    def test_generated_files_are_current(self):
        r = subprocess.run(["python3", str(KIT / "dashboards.py"), "--check"], capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)

    def test_only_the_three_star_dashboards(self):
        titles = sorted(d["title"] for d in self.boards().values())
        self.assertEqual(titles, ["Living Server ✦ Bots & World", "Living Server ✦ Overview",
                                  "Living Server ✦ Server Health"])
        gcx = sorted(f.name for f in (self.DIR / "gcx").glob("*.json"))
        self.assertEqual(gcx, ["dashboard-living-server-bots-world-showcase.json", "dashboard-living-server-health-showcase.json",
                               "dashboard-living-server-overview-showcase.json", "folder-living-server.json"])

    def test_no_hard_coded_datasource_or_stack(self):
        for name, d in self.boards().items():
            text = json.dumps(d)
            uids = set(re.findall(r'"uid": "([^"]*)"', text)) - {d["uid"]}
            self.assertLessEqual(uids, {"${ds_prom}", "${ds_loki}"}, name)
            for bad in (".grafana.net", "/home/", "C:\\Users"):
                self.assertNotIn(bad, text, name)

    def test_gcx_manifests_wrap_the_dashboards(self):
        for name, d in self.boards().items():
            m = json.loads((self.DIR / "gcx" / f"dashboard-{name}.json").read_text())
            self.assertEqual(m["apiVersion"], "dashboard.grafana.app/v1beta1")
            self.assertEqual(m["metadata"]["annotations"]["grafana.app/folder"], "living-server")
            self.assertEqual(m["spec"], d)

    def test_queries_read_metrics_the_pipeline_makes(self):
        alloy = "\n".join(f.read_text() for f in (KIT / "alloy").rglob("*.alloy"))
        made = set(re.findall(r'name\s+=\s+"([a-z0-9_]+)"', alloy))
        for name, d in self.boards().items():
            for metric in set(re.findall(r"loki_process_custom_([a-z0-9_]+)", json.dumps(d))):
                self.assertIn(metric, made, f"{name} reads loki_process_custom_{metric}, which the pipeline doesn't make")
            for expr in _loki_exprs(d):
                for label in re.findall(r"(\w+)=~?\"", expr.split("}")[0]):
                    self.assertIn(label, {"job", "instance", "level", "event"}, f"{name}: Loki label {label}")


def _loki_exprs(node):
    """The expr of every query target that uses the Loki datasource variable."""
    if isinstance(node, dict):
        if "expr" in node and node.get("datasource", {}).get("uid") == "${ds_loki}":
            yield node["expr"]
        for v in node.values():
            yield from _loki_exprs(v)
    elif isinstance(node, list):
        for v in node:
            yield from _loki_exprs(v)


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@unittest.skipUnless(os.environ.get("ALLOY_BIN"), "set ALLOY_BIN to run the configs in a real Alloy")
class RealAlloy(unittest.TestCase):
    """Runs the shipped configs in a real Alloy with loki.write swapped for loki.echo, so nothing is sent anywhere."""

    def test_pipeline(self):
        tmp = Path(tempfile.mkdtemp())
        proc = None
        out = None
        try:
            conf = tmp / "config"
            conf.mkdir()
            for f in [*(KIT / "alloy" / "common").glob("*.alloy"), *(KIT / "alloy" / "linux").glob("*.alloy")]:
                text = f.read_text().replace("forward_to = [loki.write.cloud.receiver]", "forward_to = [loki.echo.test.receiver]")
                (conf / f.name).write_text(text)
            (conf / "echo.alloy").write_text('loki.echo "test" { }\n')
            logs = tmp / "logs"
            logs.mkdir()
            (logs / "server.log").write_text("")
            addr = f"127.0.0.1:{_free_port()}"
            env = dict(os.environ, GC_LOKI_URL="http://127.0.0.1:9/loki/api/v1/push", GC_LOKI_USER="1",
                       GC_PROM_URL="http://127.0.0.1:9/api/prom/push", GC_PROM_USER="1", GC_TOKEN=SECRET,
                       LS_LOG_DIR=str(logs), LS_TZ="America/New_York", LS_INSTANCE="test-pc", LS_ALLOY_ADDR=addr)
            out = open(tmp / "alloy.out", "w")
            proc = subprocess.Popen([os.environ["ALLOY_BIN"], "run", str(conf), f"--server.http.listen-addr={addr}",
                                     f"--storage.path={tmp / 'data'}", "--disable-reporting"], env=env, stdout=out, stderr=out)
            metrics = ""
            for _ in range(60):
                time.sleep(0.5)
                try:
                    urllib.request.urlopen(f"http://{addr}/-/ready", timeout=1)
                    break
                except Exception:
                    pass
            time.sleep(1)
            with open(logs / "server.log", "a") as f:  # tail_from_end: only new lines are read
                f.write(FIXTURE_LOG.read_text())
            for _ in range(40):
                time.sleep(0.5)
                metrics = urllib.request.urlopen(f"http://{addr}/metrics", timeout=2).read().decode()
                if "loki_process_custom_server_memory_heap_mb" in metrics and "AUTONOMOUS_TICK_FAILED" in (tmp / "alloy.out").read_text():
                    break
            echoed = (tmp / "alloy.out").read_text()
            self.assertRegex(metrics, r'loki_process_custom_bots_online\{[^}]*\} 147')
            self.assertRegex(metrics, r'loki_process_custom_bots_online_albion\{[^}]*\} 50')
            self.assertRegex(metrics, r'loki_process_custom_ai_tick_p99_ms\{[^}]*\} 6.47')
            entries = re.findall(r'entry="(.*?)" entry_timestamp', echoed)
            self.assertTrue(any("AUTONOMOUS_BOT_DEATH" in e and "realm=Albion" in e and "zone=Salisbury" in e for e in entries), entries)
            self.assertTrue(any("WRN | DOL.GS.Db | ~" in e for e in entries), entries)
            # Reviewed structural keys keep their value; source= does not (EPIC_FUNUS_HEALED writes a name there).
            self.assertTrue(any('zone=\\"Salisbury Plains\\"' in e for e in entries), entries)
            self.assertTrue(any("EPIC_FUNUS_HEALED source=~ kind=player" in e for e in entries), entries)
            self.assertTrue(any('\\"members\\":[\\"~\\",\\"~\\"]' in e for e in entries), entries)
            self.assertTrue(any("AUTONOMOUS_TICK_FAILED bot=~ error=~" in e and e.endswith("error=~") for e in entries), entries)
            self.assertFalse(any("ACCOUNT_LOGIN" in e or "CLIENT_HANDSHAKE" in e for e in entries), entries)
            joined = "\n".join(entries)
            for word in PRIVATE:
                self.assertNotIn(word, joined)
            self.assertNotIn("CHAT_LINE", joined)
            self.assertNotIn("DBG", joined)
            self.assertNotIn(SECRET, echoed + metrics)
        finally:
            if proc:
                proc.terminate()
                proc.wait(10)
            if out:
                out.close()
            shutil.rmtree(tmp, ignore_errors=True)


PWSH = os.environ.get("PWSH_BIN") or shutil.which("pwsh")


@unittest.skipUnless(PWSH, "PowerShell 7 (pwsh) not found")
class PowerShell(unittest.TestCase):
    def test_scripts_parse(self):
        for f in sorted(KIT.glob("*.ps1")):
            cmd = ("$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile("
                   f"'{f}',[ref]$null,[ref]$e); if($e){{$e|%{{$_.ToString()}}; exit 1}}")
            r = subprocess.run([PWSH, "-NoProfile", "-c", cmd], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, f"{f.name}: {r.stdout}{r.stderr}")

    def test_detect_and_check_env(self):
        tmp = Path(tempfile.mkdtemp())
        try:
            logs = tmp / "logs"
            logs.mkdir()
            (logs / "server.log").write_text("")
            env = dict(os.environ, LOCALAPPDATA=str(tmp / "la"), USERPROFILE=str(tmp))
            r = subprocess.run([PWSH, "-NoProfile", "-File", str(KIT / "detect.ps1"), "-HasAccount", "no", "-Logs", str(logs)],
                               env=env, capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            j = json.loads(r.stdout)
            self.assertEqual(j["path"], "A")
            self.assertEqual(j["next"], "user_credentials")
            envfile = tmp / "monitoring.env"
            envfile.write_text("\n".join(f"{k}={SECRET}" for k in REQUIRED))
            r = subprocess.run([PWSH, "-NoProfile", "-File", str(KIT / "detect.ps1"), "-CheckEnv", str(envfile)],
                               env=env, capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertNotIn(SECRET, r.stdout + r.stderr)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_install_dry_run(self):
        tmp = Path(tempfile.mkdtemp())
        try:
            env = dict(os.environ, LOCALAPPDATA=str(tmp))
            r = subprocess.run([PWSH, "-NoProfile", "-File", str(KIT / "install.ps1"), "-DryRun"], env=env,
                               capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
            self.assertIn("[dry-run] register logon start (LivingServerAlloy)", r.stdout)
            self.assertEqual(list(tmp.iterdir()), [], "a dry run must not create files")
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
