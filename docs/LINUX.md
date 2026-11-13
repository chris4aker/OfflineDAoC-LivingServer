# Living Server on Linux: the plan

Owner decisions, 2026-11-13 12:01:
- **What:** the 0.37 base game plus Living Server, on Linux. The Godot new client (0.36c/0.37c) is **not** carried
  to Linux. The game is played with the **original client under Wine**.
- **For whom:** the owner's friend, and any DAoC fan on Linux. All three uses: a friend on Linux joining a host's
  world, hosting a world on a Linux machine, and playing solo on Linux.
- **When:** after 1.2. Windows 1.2 ships first; Linux follows as its own release.
- **Where it is tried:** WSL2 on the owner's PC for the server, the launcher and the installer. The game client under
  Wine is only proven on a real Linux desktop (the friend's), because WSL's graphics can't stand in for one.

## 0. Route change: everything under Wine first (2026-11-13)

The owner's friend, Ryan, has been playing Offline DAoC 0.37b with Living Server 1.1-beta on Ubuntu (GNOME). He runs
the **whole Windows package under Wine**: the launcher, the server it starts, and the client. His scripts are at
[ryan-grafana/offline-daoc-linux](https://github.com/ryan-grafana/offline-daoc-linux), commit 80dc67b, and the
owner has asked that we take from them.

That is evidence from real play, which this plan didn't have. It changes the order of work:

1. **First, the Wine route (Linux 1.0).** The same Windows files, run under Wine. There is no new launcher and no
   Linux server build. What has to be built:
   - a player setup script, from Ryan's `setup-offline-daoc.sh` cut down to what a player needs: Wine check, prefix,
     shadowofze's 0.36b plus 0.37b with hash checks and his patcher, Living Server;
   - Living Server's install and rollback that work on Linux for **1.2**;
   - a start script with the server memory settings and the fullscreen fix (Ryan's `start-daoc.sh` and
     `daoc_fullscreen.py`);
   - launcher guards for what Wine lacks (the Windows firewall in Host for Friends, the PowerShell update hand-off);
   - a friend's checklist.
2. **Later, only if Wine falls short:** the native route in §1–§10 (Linux .NET server with `Detour.so`, command-line
   launcher). Triggers would be server speed or memory under Wine, or players without a desktop (a headless host).

Ryan's pieces, as of 80dc67b:

| Piece | Use |
|---|---|
| `setup-offline-daoc.sh` + `odaoc_helper.py`: mirrors shadowofze's releases with size and SHA-256 checks (GitHub digests and his `download-manifest.json`), checks the 0.36b archive hash, unpacks it, applies 0.37b with shadowofze's own patcher on Linux .NET, builds the Wine prefix, never runs sudo | **Take**, cut down for players. The source mirrors, NuGet and SDK steps are for building, not playing. |
| `install_living_server.py`: a Python port of `install-server-build.ps1`, same backups and `install-record.json` | **Take the approach, not the file.** It ports the **1.1-beta** script. 1.2's install also copies the server's own libraries, the loading screen and the rollback guards (`git diff v1.1-beta..integration/1.2 -- tools/fork-build/`), so it must not install 1.2 as it is. |
| `start-daoc.sh`: the launcher under Wine; `DOTNET_GCHeapCount=4`, `DOTNET_GCConserveMemory=5` | **Take.** The memory finding may apply on Windows too (below). |
| `daoc_fullscreen.py`: makes the client window fullscreen under GNOME/XWayland so the top bar doesn't push the bottom buttons off-screen | **Take** as is (X11 via ctypes, no dependencies). |
| Prefix verbs `corefonts vcrun2019 d3dx9 dotnet35 dxvk` | **Check each.** DXVK is the player's own choice in their own prefix and is never shipped (AGENTS.md); the default is Wine's own Direct3D. Find out why `dotnet35` is needed: it takes 10+ minutes. |

**Built so far on `feature/linux` (2026-11-13; tests only, nothing run on a Linux desktop yet):**
- **Install and rollback are the Windows scripts themselves.** `install-server-build.ps1` and
  `rollback-server-build.ps1` run unchanged under PowerShell 7 for Linux. All 109 checks of
  `test-install-scripts.ps1` pass there (pwsh 7.6.9, container, the 1.2-beta package of 30a24e7). So there is no
  Linux port to keep in step, and Ryan's Python port isn't needed.
- **`tools/linux/offlinedaoc`** (shipped in every package's `linux/` folder) has these commands:
  - `play`: the launcher under Wine, with Ryan's memory settings and fullscreen helper;
  - `install <package>`: runs the package's own install script with PowerShell 7;
  - `update [--check]`: the newest release, checked against GitHub's SHA-256 digest, as the launcher does;
  - `rollback [--to-release]`;
  - `status`.

  It checks for running game parts by their Wine process names (`CoreServer.exe` and so on) and the game's port
  itself, because PowerShell's own process check on Linux doesn't see Wine's names. `tools/linux/test-offlinedaoc.sh`
  tests it on a fake game folder (install, reinstall, refusal while `CoreServer.exe` runs, rollback, bad packages).
- **CI:** a `linux-install` job in build-and-test runs both test scripts on Ubuntu against the package the server job
  built. `.gitattributes` keeps the Linux scripts LF when the Windows runner packs them.
- **The launcher under Wine** (`WineHost`, detected by Wine's own registry key):
  - Host for Friends skips the Windows Firewall step and shows the ufw and firewalld commands for the game's ports;
  - an update the launcher finds is installed with `linux/offlinedaoc update` instead of `apply-update.ps1`.
- The native prototypes moved to `tools/linux/native/`.

- **`tools/linux/setup.sh`**, the player's setup, cut down from Ryan's. It asks before each step and never uses sudo:
  1. `check`: the tools, with the install commands for Ubuntu, Fedora and Arch;
  2. `dotnet`: Microsoft's Linux .NET runtime, SHA-512 checked;
  3. `game`: shadowofze's 0.36b plus the 0.37b update, checked against his manifests and applied with his own patcher.
     `EDITION=0.37` takes 0.36 plus 0.37 instead;
  4. `prefix`: the Wine folder with `corefonts vcrun2019 d3dx9`;
  5. `living`: the newest Living Server release, checked against GitHub's digest, installed with `offlinedaoc install`;
  6. `desktop`: an "Offline DAoC" menu entry.

  So far only `list` and `dotnet` have run (container). The `game` step is Ryan's own logic, which he used on his
  desktop. `EDITION=0.37` is untried.

Still to do for the Wine route:
- check which prefix verbs the game needs: Ryan's desktop also has `dotnet35` and `dxvk`;
- the friend's checklist;
- the first run of `setup.sh` and `offlinedaoc` on Ryan's desktop;
- a release asset that packs `tools/linux/` for players who don't use git.

**Server memory.** Ryan saw the server grow to about 6 GB in a 48-minute session on a 16-core CPU. Server GC keeps
one heap per logical CPU. The owner's PC has 16 logical CPUs too, so the same may happen on Windows. Check the
server's memory on Windows before changing anything there.

**Licence.** Ryan's repository has no licence file. Before any of his code is copied in, he adds GPL-3.0 (this
repository's licence) or says in writing that we may use it under GPL-3.0. He is credited in the commits
(`Co-authored-by`) and in THIRD_PARTY.md.

Nothing in the plan below has run on a real Linux desktop. Ryan's own setup (1.1-beta) has. Earlier prototypes
(2026-11-07, in a container and in GitHub's Linux CI) are in §9.

## 1. The idea

A Linux game folder is **the same folder as on Windows** (shadowofze's 0.37 or 0.37b game with Living Server installed
over it) plus one new folder, `linux/`, for what Windows provides differently: a Linux .NET runtime, the Detour pathing
library built for Linux, the launcher, and the game's own Wine folder. Every shipped Windows file stays where it is and
unchanged, so shadowofze's file fingerprints and patcher, the progress importer, and Living Server's install checks and
backups work as on Windows. The server is .NET, so it is **the same files on both systems**; only the Detour library
needs a Linux twin. The original client is Windows code and runs under Wine, started the way the Windows launcher
starts it.

## 2. The folder a player ends up with

```
~/Games/OfflineDAoC/                      the game folder (the Windows layout, unchanged)
  runtime/server/                         the server + Living Server (same files as Windows)
  runtime/server/lib/Detour.so            NEW: Linux twin of lib/Detour.dll, built by Living Server's CI from source
  runtime/data/opendaoc.sqlite3.db        the world and your saves
  runtime/account.txt                     the local account (made on first play)
  runtime/client-opendaoc/app/            the original game client (game.dll, connect.exe, ...), run under Wine
  linux/                                  NEW, Linux only:
    offlinedaoc                           the launcher (command line, §4)
    dotnet/                               .NET 10 runtime with ASP.NET Core (Microsoft's linux-x64 tarball)
    wine/                                 the game's own Wine folder (WINEPREFIX); Wine itself comes from the system
    run/                                  the server's pid and console pipe while it runs
  ~/.local/share/applications/            desktop entries: "Offline DAoC" (play) and "Offline DAoC server"
```

What Living Server publishes for Linux is small: one asset per release,
`OfflineDAoC-LivingServer-<build>-linux-x64.tar.gz`: the same files as the Windows package, plus `lib/Detour.so`, the
launcher, `install.sh` and the pinned download hashes for .NET. No EA file, no shadowofze file, no copy of .NET or Wine.

## 3. A Linux player's day

| Step | What they do | What happens |
|---|---|---|
| Install | `tar xf …-linux-x64.tar.gz && ./install.sh` (asks for a folder, default `~/Games/OfflineDAoC`) | Checks disk space, glibc 2.28 or newer, and Wine (`wine --version`, 9 or newer; it says how to install it on Ubuntu, Debian, Fedora, Arch and SteamOS if missing). Downloads the pinned .NET runtime (checked against Microsoft's hash), then shadowofze's 0.36 base and the 0.37 game patch from **his** GitHub releases, part by part, checked against his manifests, and applies the patch with his own patcher on the Linux .NET. Never the new-client parts. Installs Living Server (backups, verified copies, the content patches), turns on the suggested settings as Setup does, makes the Wine folder, adds the desktop entries. Stops and resumes like the Windows downloader. A 0.37 or 0.37b folder copied from Windows works too: `install.sh --game <folder>`. |
| Play | Click **Offline DAoC**, or `offlinedaoc play` | Starts the server if it isn't running (a plain reason if it can't: port taken, already running, low memory), waits until it takes logins, then runs `wine connect.exe game.dll 127.0.0.1 <account> <password>` in the client folder, as ENTER REALM does. |
| Join a friend | `offlinedaoc join <invite code>` | Join Friend's rules: your own account and password, the host password the first time. Starts the client against the host's address under Wine. No server runs here. |
| Host friends | `offlinedaoc host start` / `stop` | Host for Friends without Windows' firewall: the server listens on the home network and Tailscale, the same password rules. Opening a Linux firewall (ufw, firewalld) is shown, not done. |
| Bots | `offlinedaoc bots add 20 --level 50`, `offlinedaoc settings living --recommended` | The launcher's +Lv.1 and +Lv.50 buttons and its Living Server tab, the same code. |
| Stop | quit the game, then `offlinedaoc stop` | Types `exit` at the server console (a clean save); never force-kills. |
| Update | `offlinedaoc update` (and a quiet check on `play`) | The newest release's Linux asset, GitHub's SHA-256 digest and the package checks, installed with the same backups (§5). |
| Roll back | `offlinedaoc rollback` | The last install's files back from `runtime/server/deployment-backups`, as `ROLLBACK.cmd`. Never the database. |

## 4. What is reused, ported or replaced

| Part | Windows | Linux | How |
|---|---|---|---|
| Server (`GameServer.dll`, CoreBase, CoreDatabase, CoreServer.dll) | .NET 10, `CoreServer.exe` | **reused**: `linux/dotnet/dotnet CoreServer.dll` | same files; the package already carries the linux-x64 SQLite library |
| Detour (navmesh pathing) | `lib/Detour.dll` | **ported**: `lib/Detour.so` beside it | built with cmake from `source/server/Pathing/Detour` in CI. The name must be `lib/Detour.so` (§9) |
| `serverconfig.xml` | relative paths with `/` | **reused** | no change |
| .NET runtime | `tools/dotnet` | **replaced**: `linux/dotnet` | Microsoft's tarball, SHA-512 from its release metadata |
| The original client | `connect.exe game.dll …` | **under Wine**: `wine connect.exe game.dll …` | the system's Wine; the game's own prefix in `linux/wine`; the client's ini written as on Windows (borderless window) |
| Launcher (WinForms) | `runtime/OfflineDAoC.exe` | **replaced**: a command-line launcher | the launcher's UI-free code, shared with Windows (below) |
| Setup (`LivingServer-Setup.exe`) | Windows exe | **ported**: `install.sh` + `offlinedaoc setup` | same manifests, same checks, same order |
| shadowofze's patcher | on `tools/dotnet` | **reused** on `linux/dotnet` | it uses `/` paths; on Linux only its port check sees a running game, so the launcher refuses updates while its own server runs |
| Living Server install and rollback (`install-server-build.ps1`) | `INSTALL.cmd`, `ROLLBACK.cmd` | **ported** to C# in the shared launcher code | the ps1 stays the Windows path; CI runs both on the same fake game folders and compares |
| Settings helpers (`tools/claude-version/*.py`) | Python 3 | **reused** | already portable |

**The launcher.** A .NET console program, `offlinedaoc`, over the Windows launcher's own non-UI code, so both
systems run the same logic. Most of it is already UI-free (`PortableCredentials`, `ServerPortProbe`,
`SocialExpansionSettings`, `BotCharacterGenerator`, `RollingServerLog`, `HostForFriends`, `JoinFriend`, `InviteCode`,
`Updates/*`). The bot batches and the XP and GM saves move out of `MainForm.cs` into shared classes first; WinForms
links the same files, so Windows doesn't change and its tests keep passing. A window (Avalonia) can follow if players
ask for one. The bash `tools/linux/native/offlinedaoc` is the prototype of start, stop, status and account, not the product.

## 5. Updates

- **Same feed, same rules.** `ReleaseFeed`, `PackageDigest` (fail closed without GitHub's digest) and `PackageCheck`
  are reused. Only the asset name differs (`…-linux-x64.tar.gz`); a release without it is "no Linux update yet".
- **Applying.** The launcher stops the server (asking first), unpacks the package into `runtime/updates/<build>`, runs
  the C# install and replaces its own files by rename. Refused while the server or the game runs.
- **The base game** doesn't change again; shadowofze's patcher runs once, at install.

## 6. Where the game comes from

- **Exactly as on Windows.** Every EA-derived file (the client, the world data) comes from **shadowofze's own GitHub
  releases**, downloaded by the player's machine and checked against his manifests. Living Server hosts none of it.
  If a part fails its check the installer stops; there is no other source.
- **.NET** (MIT) is downloaded from Microsoft with a pinned hash. **Wine** (LGPL) comes from the player's system.
- **Not shipped:** DXVK or any other graphics layer (AGENTS.md). Wine's own Direct3D runs the client.
- The Linux Detour library is built from the repository's own source (recastnavigation, zlib licence) and credited in
  THIRD_PARTY.md like the Windows DLL.

## 7. CI that proves it

| Job | What it proves | State |
|---|---|---|
| `linux / server` | the server, `lib/Detour.so` and Microsoft's runtime in the package layout; `offlinedaoc start`; two scripted players log in; `stop`, start, stop | prototype green (§9); to rebuild on the C# launcher |
| `linux / launcher` | `dotnet test` of the shared launcher code on Linux | to build |
| `linux / install-parity` | the C# install and rollback against the ps1's fake game folders; the trees must match | to build |
| `linux / real-download` | `install.sh` with real downloads: the 0.36 base, the 0.37 patch with his patcher, Living Server, the server takes a scripted login | to build (about 15 GB without the new client) |
| `linux / wine-client` | the client under Wine on a virtual display (Xvfb): connect.exe starts game.dll, it reaches the server and logs in | to build; a login is the most CI can show, not a played screen |
| `linux / release` | builds the Linux asset from the release's own package | to build |

None of these is a played session.

## 8. Risks

1. **The client under Wine.** DAoC's client is commonly played under Wine, but this exact client (1.127 with
   Living Server's hash-guarded patches) hasn't been tried. Unknowns: rendering on Mesa and NVIDIA drivers, Wayland vs
   X11, sound, mouse capture for right-button steering, full screen. The friend's first evening is the real test,
   with a checklist.
2. **WSL2 can't prove the client.** It runs Linux, but its graphics go through Windows; a client that draws in WSL
   proves little about a Linux desktop, and one that doesn't proves nothing.
3. **Case-sensitive files.** Checked for the 0.36/0.37 file lists: no two paths differ only in case. Names built at run
   time can't be checked without playing.
4. **Process checks written for Windows.** shadowofze's patcher looks for running games by Windows process names; on
   Linux only its port check applies, so the launcher refuses updates while its own server runs.
5. **System libraries.** The server needs libicu; glibc 2.28 or newer. Very old or musl (Alpine) distributions aren't
   supported.

## 9. The prototypes (2026-11-07, container and CI)

| Spike | Result |
|---|---|
| **Linux server in the package layout** | **Worked.** Listening in 22–26 s; `lib/Detour.so` mapped; all 32 `.nav` files of a navmesh subset loaded; two scripted players (`chat-players`, `group-players`) passed; stop via `exit`, then a clean second start and stop. Found: the file must be `lib/Detour.so`, not `libDetour.so` (.NET looks for `lib/Detour.so` and `lib/Detour` only). |
| **CI sketch** | **Worked on GitHub** (ubuntu-22.04, minimal world): run 42434529818. The first run found a start race in the prototype launcher, fixed. |
| shadowofze's patcher on Linux .NET | **Started only**: it refused an empty folder with exit 4, as designed. |
| The Godot client on Linux | Tried then; **dropped** by the owner's decision (2026-11-13). |

Reproduce the server spike:
```
cmake -S source/server/Pathing/Detour -B /tmp/detour -DCMAKE_BUILD_TYPE=Release -DSOVERSION=1 -DLIB_VERSION=1.0 && cmake --build /tmp/detour
dotnet build source/server/CoreServer/CoreServer.csproj -c Release
python3 source/tools/make_test_world.py from-release --world <clean 0.37b world> --out /tmp/test-world.db
tools/linux/native/assemble-server.sh --server-build source/server/Release --detour /tmp/detour/libDetour.so.1.0 \
    --world /tmp/test-world.db --dotnet <aspnetcore runtime folder> --navmesh <navmesh folder> --out /tmp/OfflineDAoC
tools/linux/native/smoke-server.sh --game /tmp/OfflineDAoC --playtester <OfflineDaoc.Playtester.dll> --out /tmp/smoke
```

## 10. Order of work and effort (the native route; see §0 for the Wine route that comes first)

| Part | Effort (working days, the plan's own estimate) |
|---|---|
| Server on Linux: Detour in CI, runtime, layout, start and stop (prototype done) | 2–3 |
| Shared launcher core (out of `MainForm`; Windows unchanged) | 2–3 |
| The command-line launcher: start, stop, status, play, join, host, account, bots, settings, update, rollback | 5–7 |
| The client under Wine: prefix, ini, start and stop, the Xvfb login check | 3–5 |
| Living Server install and rollback in C#, with parity tests against the ps1 | 4–6 |
| `install.sh` (shadowofze's manifests and patcher, resume, disk and Wine checks) | 4–6 |
| The updater on Linux | 2–3 |
| CI jobs and the release asset | 3–5 |
| Docs (PLAY, KNOWN-ISSUES, a friend's checklist) and release notes | 1–2 |
| **Total** | **about 26–40 days**, plus the friend's first evening and its fixes |

Checks on a real machine: the server, the launcher and the installer on WSL2 on the owner's PC; the client under
Wine on the friend's Linux desktop. Nothing is called working on Linux before it has run on one of those.
