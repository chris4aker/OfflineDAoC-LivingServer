"""Smoke test a built 0.37 package the way a new player uses it, in a throwaway copy.

    python smoke_release_037.py <package folder> <empty test folder> [--edition no-custom-class]

1. copies runtime/server, runtime/data and the launcher into the test folder,
2. lets the launcher create this install's own account.txt (--prepare-portable-account),
3. starts CoreServer with ONLY the bundled .NET (tools/dotnet),
4. logs in through connect.exe exactly like ENTER REALM,
5. checks that the server created that fresh account at player privilege,
6. shuts the server down cleanly. Nothing outside the test folder is touched.
"""
import json
import os
import shutil
import socket
import sqlite3
import subprocess
import sys
import time
from pathlib import Path


def processes():
    data = json.loads(subprocess.check_output(["powershell", "-NoProfile", "-Command",
        "Get-CimInstance Win32_Process | Select-Object ProcessId,Name,ExecutablePath | ConvertTo-Json -Compress"], text=True))
    return data if isinstance(data, list) else [data]


def log_tail(log, lines=40):
    """The last lines of the server console log, so a failed start shows why in the CI output."""
    try:
        text = Path(log).read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as e:
        return f"(no server log: {e})"
    return "\n".join(text[-lines:]) if text else "(the server log is empty)"


def wait_for_startup(server, log, timeout=600, poll=2):
    """Waits for "GameServer startup completed" in the log. Raises RuntimeError with the log's tail if the server exits
    or the time runs out."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if server.poll() is not None:
            raise RuntimeError(f"Server exited during startup (exit code {server.returncode}); see {log}. "
                               f"Its last lines:\n{log_tail(log)}")
        if "GameServer startup completed" in Path(log).read_text(encoding="utf-8", errors="replace"):
            return
        time.sleep(poll)
    raise RuntimeError(f"Startup timeout after {timeout} s; see {log}. Its last lines:\n{log_tail(log)}")


def main():
    package, test = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
    edition = sys.argv[4] if len(sys.argv) > 4 and sys.argv[3] == "--edition" else "sluaghbinder"
    if any((p.get("Name") or "").lower() == "coreserver.exe" for p in processes()):
        raise SystemExit("Another server is running")
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", 10300)) == 0:
            raise SystemExit("Port 10300 is in use")
    test.mkdir(parents=True, exist_ok=False)
    runtime = package / "runtime"
    shutil.copytree(runtime / "server", test / "server")
    shutil.copytree(runtime / "data", test / "data")
    if edition == "no-custom-class":
        shutil.copy2(package / "editions/0.37-no-custom-class/runtime/data/opendaoc.sqlite3.db", test / "data/opendaoc.sqlite3.db")
    # The launcher: DAoCLivingServer.* from 1.2.3 (#641; OfflineDAoC.exe is then a copy of its apphost, which needs
    # DAoCLivingServer.dll beside it), else the older OfflineDAoC.*.
    renamed = (runtime / "DAoCLivingServer.dll").exists()
    launcher = "DAoCLivingServer" if renamed else "OfflineDAoC"
    names = [f"{launcher}.exe", f"{launcher}.dll", f"{launcher}.deps.json", f"{launcher}.runtimeconfig.json",
             "System.Data.SQLite.dll", "SQLite.Interop.dll"]
    if renamed and (runtime / "OfflineDAoC.exe").exists():
        names.append("OfflineDAoC.exe")
    for name in names:
        shutil.copy2(runtime / name, test / name)
    if (runtime / "runtimes").exists():
        shutil.copytree(runtime / "runtimes", test / "runtimes")

    env = os.environ.copy()
    env["DOTNET_ROOT"] = str(package / "tools/dotnet")
    env["DOTNET_ROOT_X64"] = env["DOTNET_ROOT"]
    env["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    # Through OfflineDAoC.exe, as the game's start file and old shortcuts open it: from 1.2.3 that copy must open the new
    # launcher (#641).
    subprocess.run([str(test / "OfflineDAoC.exe"), "--prepare-portable-account"], cwd=test, env=env, check=True, timeout=120)
    creds = dict(line.split(":", 1) for line in (test / "account.txt").read_text().splitlines() if ":" in line)
    account, password = creds["Account"].strip(), creds["Password"].strip()
    assert account == "offline" and len(password) == 20, "portable account.txt was not created as expected"
    print("PASS: the launcher created this install's own account.txt", flush=True)

    log = test / "console.log"
    clients = []
    with log.open("w", encoding="utf-8") as output:
        server = subprocess.Popen([str(test / "server/CoreServer.exe")], cwd=test / "server", env=env, stdin=subprocess.PIPE,
                                  stdout=output, stderr=subprocess.STDOUT, text=True, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            wait_for_startup(server, log)
            print("PASS: server started with only the bundled .NET", flush=True)
            app = runtime / "client-opendaoc/app"
            before = {p["ProcessId"] for p in processes()}
            subprocess.Popen([str(app / "connect.exe"), "game.dll", "127.0.0.1", account, password], cwd=app, env=env,
                             creationflags=subprocess.CREATE_NO_WINDOW)
            deadline = time.monotonic() + 180
            while time.monotonic() < deadline:
                for p in processes():
                    path = p.get("ExecutablePath")
                    if p["ProcessId"] not in before and path and Path(path).parent == app and p["ProcessId"] not in clients:
                        clients.append(p["ProcessId"])
                with sqlite3.connect((test / "data/opendaoc.sqlite3.db").as_uri() + "?mode=ro", uri=True) as db:
                    found = db.execute("SELECT count(*) FROM Account WHERE Name=? AND PrivLevel=1", (account,)).fetchone()[0]
                    edition_value = db.execute("SELECT Value FROM ServerProperty WHERE `Key`='enable_sluaghbinder'").fetchone()
                if found:
                    break
                time.sleep(2)
            else:
                raise RuntimeError(f"ENTER REALM login did not create the account; the server log's last lines:\n{log_tail(log)}")
            print(f"PASS: ENTER REALM login created the fresh '{account}' account (player privilege); "
                  f"enable_sluaghbinder={edition_value[0] if edition_value else 'missing'}", flush=True)
        finally:
            for pid in clients:
                subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
            if server.poll() is None:
                server.stdin.write("exit\n")
                server.stdin.flush()
                try:
                    server.wait(timeout=180)
                except subprocess.TimeoutExpired:
                    server.terminate()
                    raise RuntimeError("Server needed a forced stop")
            print("Server stopped, exit code", server.returncode, flush=True)


if __name__ == "__main__":
    main()
