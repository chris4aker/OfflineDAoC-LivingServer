"""The shipped config in a real Grafana Alloy, on synthetic lines (issue #628).

Skipped unless an Alloy binary is given: ALLOY_BIN=/path/to/alloy, or `alloy` on PATH. The config's loki.write is
swapped for loki.echo, which prints each entry Alloy would send; the test then compares Alloy's output with the
Python replay (alloy.Filter), line for line, labels included. This is what keeps the replay honest.

    ALLOY_BIN=~/alloy-linux-amd64 python3 -m unittest tools/grafana/tests/test_alloy_real.py -v
"""
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import alloy  # noqa: E402

REPO = HERE.parents[2]
# #817: the committed copy lags a pull request's code (main regenerates it after the merge), so the tests read the
# config the kit builds from this checkout's code, written to a temp file.
_CONFIG_DIR = None


def current_config() -> Path:
    global _CONFIG_DIR
    if _CONFIG_DIR is None:
        import kit  # noqa: E402
        _CONFIG_DIR = tempfile.TemporaryDirectory()
        (Path(_CONFIG_DIR.name) / "config.alloy").write_text(kit.Kit(REPO).alloy_config(), encoding="utf-8", newline="\n")
    return Path(_CONFIG_DIR.name) / "config.alloy"
ALLOY = os.environ.get("ALLOY_BIN") or shutil.which("alloy")

LINES = [
    '11/25 12:00:01 | INF | DOL.GS.T | CHAT_LINE who=player ch=say realm=Albion from="Sirwhatever" intent=Question zone="Camelot City" text="where is the trainer, Aldwyn?"',
    "11/25 12:00:02 | INF | DOL.GS.T | Player Sirwhatever logged in from 192.168.1.20",
    "11/25 12:00:03 | WRN | DOL.GS.T | ACCOUNT_LOGIN refused, too many wrong passwords from this address lately - acct:10.0.0.5",
    "11/25 12:00:04 | INF | DOL.GS.T | RVR_DECIDE t=1 bot=Aldwyn id=4 realm=Midgard force=f1 layer=group what=target from=keep to=relic rule=r1 why=x",
    '11/25 12:00:05 | INF | DOL.GS.T | AUTONOMOUS_EXCHANGE_SOLD realm=2 item="Ring" quantity=1 price=500 seller="Sirwhatever" sellerId=1 buyer="Aldwyn" buyerId=2 tax=5',
    "System.Exception: boom",
    "   at DOL.GS.Foo()",
    '11/25 12:00:06 | INF | DOL.GS.T | AUTONOMOUS_GOAL_ATTEMPT {"bot":"Aldwyn","realm":1,"outcome":"done","members":[{"name":"Sirwhatever"}]}',
    "11/25 12:00:07 | WRN | DOL.GS.T | FORK_BUILD name=1.2.3 commit=abc bot_guilds=on sluaghbinder=off",
    "11/25 12:00:08 | INF | DOL.GS.T | SERVER_STALL worstTickMs=900 stage=npc gcPauseMs=1 gen2=0 heapMB=800 slowest=10.1.2.3",
    "11/25 12:00:09 | INF | DOL.GS.T | PLAYER_LINE_OUTCOME seq=1 t=2 ch=say realm=Hibernia intent=Q answered=1 latencyMs=900 kind=answer bots=1 replies=1 window=30000",
    '11/25 12:00:10 | INF | DOL.GS.T | QUEST_XP player="Sirwhatever" level=20 source="quest" xp=100',
    '11/25 12:00:11 | WRN | DOL.GS.T | GM_JUMP_BOT gm="Sirwhatever" bot="Aldwyn" kind=gamebot',
    "11/25 12:00:12 | INF | DOL.GS.T | GROUP_ENDED realm=Albion reason=wipe leader=Aldwyn note=Sirwhatever size=5",
]


# Alloy's own start-up complaints about the host, not about this config (a container without cgroup v1, for one).
HOST_ERRORS = re.compile(r"GOMEMLIMIT|memory limit")


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def unescape(value: str) -> str:
    return value.encode("utf-8").decode("unicode_escape")


@unittest.skipUnless(ALLOY, "no Alloy binary (set ALLOY_BIN)")
class RealAlloyTests(unittest.TestCase):
    def test_alloy_ships_what_the_replay_ships(self):
        flt = alloy.load_filter(current_config())
        want = []
        for raw in LINES:
            shipped = flt.apply(raw)
            if shipped:
                want.append((shipped.line, dict(shipped.labels)))
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "logs").mkdir()
            (root / "logs/server.log").write_text("\n".join(LINES) + "\n", encoding="utf-8")
            text = current_config().read_text(encoding="utf-8")
            text = text[:text.index('loki.write "offlinedaoc"')] + 'loki.echo "offlinedaoc" { }\n'
            text = text.replace("forward_to = [loki.write.offlinedaoc.receiver]", "forward_to = [loki.echo.offlinedaoc.receiver]")
            (root / "test.alloy").write_text(text, encoding="utf-8")
            env = dict(os.environ, OFFLINEDAOC_LOGS=str(root / "logs"), OFFLINEDAOC_TZ="UTC")
            proc = subprocess.Popen([ALLOY, "run", "--disable-reporting", f"--server.http.listen-addr=127.0.0.1:{free_port()}",
                                     f"--storage.path={root / 'data'}", str(root / "test.alloy")],
                                    env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            output, deadline = "", time.time() + 60
            try:
                import selectors
                sel = selectors.DefaultSelector()
                sel.register(proc.stdout, selectors.EVENT_READ)
                # os.read, not readline: a buffered reader can hold the last entries while select() waits on the
                # empty pipe, and the test then times out short of them.
                while time.time() < deadline and output.count("received log entry") < len(want):
                    if sel.select(timeout=1):
                        chunk = os.read(proc.stdout.fileno(), 65536)
                        if not chunk:
                            break
                        output += chunk.decode("utf-8", "replace")
            finally:
                proc.terminate()
                try:
                    proc.wait(10)
                except subprocess.TimeoutExpired:
                    proc.kill()
        errors = [ln for ln in output.splitlines() if "level=error" in ln and not HOST_ERRORS.search(ln)]
        self.assertEqual(errors, [], output[-2000:])
        got = []
        for m in re.finditer(r'labels="(?P<labels>(?:[^"\\]|\\.)*)" entry="(?P<entry>(?:[^"\\]|\\.)*)"', output):
            labels = dict(re.findall(r'(\w+)=\\"((?:[^"\\]|\\\\.)*)\\"', m.group("labels")))
            got.append((unescape(m.group("entry")), labels))
        self.assertEqual(len(got), len(want), output[-3000:])
        for (g_line, g_labels), (w_line, w_labels) in zip(got, want):
            self.assertEqual(g_line, w_line)
            self.assertEqual(g_labels, w_labels)
        for secret in ["Sirwhatever", "Aldwyn", "10.1.2.3", "192.168", "trainer", "acct"]:
            self.assertNotIn(secret, " ".join(line for line, _ in got))


if __name__ == "__main__":
    unittest.main()
