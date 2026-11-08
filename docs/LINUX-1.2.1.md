# Living Server 1.2.1 on Linux: the plan

Owner decision, 2026-11-07 18:51: **option 2**. 1.2.1 is a native Linux build of the server, a launcher and the 0.37c
**new client** (Godot). The original Windows client under Wine is **out of scope**; it is a later add-on once someone
can try it. The owner has no Linux machine, and friends will try it only once it is complete, so every part must be
testable in this container and in GitHub's Linux CI. **Nothing here has been run on a real Linux desktop yet.**

Status: plan plus prototypes (`tools/linux/`, `.github/workflows/linux.yml`). §9 lists what the spikes showed.

## 1. The idea in one paragraph

A Linux game folder is **the same folder as on Windows** (shadowofze's 0.37c game, Living Server installed over it),
plus one new folder, `linux/`, that holds the few things Windows provides differently: a Linux .NET runtime, the
Linux build of the same Godot engine, the Detour pathing library built for Linux, and the Linux launcher. Every
shipped Windows file stays where it is and unchanged. That keeps shadowofze's file fingerprints, his patcher, the
progress importer, Living Server's installer checks and the backups working as they do on Windows, and it leaves room
for the Wine add-on later (the original client is already in the folder). The server and the new client's C# are
.NET IL, so they are **the same files on both systems**; only native code needs a Linux twin.

## 2. What the Linux package contains, and the folder a player ends up with

```
~/Games/OfflineDAoC/                      the game folder (the Windows layout, unchanged)
  runtime/server/                         shadowofze's server + Living Server's GameServer.dll and libraries (same IL)
  runtime/server/lib/Detour.so            NEW: Linux twin of lib/Detour.dll (built by Living Server CI from source)
  runtime/data/opendaoc.sqlite3.db        world and saves (one database for every client and system)
  runtime/account.txt                     the local account (same format; made on first play)
  runtime/client-opendaoc/                the original Windows client (unused on Linux until the Wine add-on)
  new-client/godot/                       the new client: scripts, converted content, built C# (same files)
  new-client/save/                        its settings (Godot's user:// via XDG_DATA_HOME on Linux, APPDATA on Windows)
  linux/                                  NEW, Linux only:
    offlinedaoc                           the launcher (command line; §4)
    dotnet/                               .NET 10 runtime with ASP.NET Core (Microsoft's linux-x64 tarball)
    godot/                                Godot 4.7.2 .NET, linux x86_64 (godotengine's release zip)
    run/                                  the server's pid and console pipe while it runs
  ~/.local/share/applications/            two desktop entries: "Offline DAoC" (play) and "Offline DAoC server"
```

What Living Server itself publishes for Linux is small: one asset per release,
`OfflineDAoC-LivingServer-<build>-linux-x64.tar.gz` = the platform-neutral package (the same files as the Windows zip)
plus the Linux Detour library (`lib/Detour.so`), the launcher, `install.sh` and pinned download hashes for .NET and Godot. No EA file, no
shadowofze file, and (recommended, §6) no copy of .NET or Godot: those come from their publishers.

## 3. A Linux player's day

| Step | What they do | What happens |
|---|---|---|
| Install | `tar xf …-linux-x64.tar.gz && ./install.sh` (asks for a folder, default `~/Games/OfflineDAoC`) | Checks disk space (about 60 GB during install), Vulkan (`vulkaninfo` if present) and glibc ≥ 2.28. Downloads the pinned .NET runtime and Godot, each checked against its publisher's hash. Then shadowofze's game from **his** GitHub releases, part by part, checked against his manifests (0.36b or 0.36 base, the 0.37 game patch, the 0.36c new client, the 0.37c client patch), applies his patches with his own patcher (on the Linux .NET), installs Living Server (backups, verified copies, the content patches), turns on the suggested settings (as Setup does), adds the desktop entries. Stops and resumes like the Windows downloader (finished parts kept). |
| Play | Click **Offline DAoC**, or `linux/offlinedaoc play` | Starts the server if it isn't running (refusal reasons in plain words: port taken, server already running, low memory), waits until it accepts logins, then starts the new client logged in with `runtime/account.txt` (`--custom-class=off` on a 0.37 game), as ENTER REALM does. |
| Bots | `offlinedaoc bots add 20 --level 50` (or `--level 1`), `offlinedaoc settings living --recommended` | The launcher's +Lv.1 / +Lv.50 buttons and Living Server tab, same code (§4). |
| Stop | quit the game, then `offlinedaoc stop` (or tick "stop the server when the game closes") | Types `exit` at the server console (a clean save); never force-kills. |
| Update | `offlinedaoc update` (and a quiet check on `play`, as the Windows launcher does) | Reads the newest release from GitHub, downloads the Linux asset, checks GitHub's SHA-256 digest and the package rules, installs with the same backups (§5). |
| Roll back | `offlinedaoc rollback` | Puts the files of the last install back from `runtime/server/deployment-backups/fork-build-<time>`, as `ROLLBACK.cmd` does. Never puts the database back; the backup's `data/` copy is there for a hand restore. |
| Remove | `offlinedaoc uninstall` | Removes Living Server (rollback), then optionally the desktop entries; the game folder stays theirs to delete. |

## 4. What is reused, ported or replaced

| Part | Windows today | Linux 1.2.1 | How |
|---|---|---|---|
| Server (`GameServer.dll`, CoreBase, CoreDatabase, CoreServer.dll) | framework-dependent .NET 10, `CoreServer.exe` apphost | **reused as is**: `linux/dotnet/dotnet CoreServer.dll` | same IL; the Windows package already carries `runtimes/linux-x64/native/SQLite.Interop.dll` |
| Detour (navmesh pathing) | `lib/Detour.dll` | **ported**: `lib/Detour.so` beside it | cmake from `source/server/Pathing/Detour` in CI. The file must be named `lib/Detour.so`: with a folder in the import name (`lib/Detour`) .NET tries only `lib/Detour.so` and `lib/Detour` (found in the spike) |
| `serverconfig.xml` | relative paths with `/` | **reused** | no change needed |
| .NET runtime | `tools/dotnet` (Windows) | **replaced**: `linux/dotnet` | Microsoft's tarball, SHA-512 from its release metadata |
| New client scripts, content, `MMOEngine.dll` | in `new-client/godot` | **reused as is** | the managed GodotSharp API is byte-identical between the Windows and Linux 4.7.2 engines (§9) |
| Godot engine | `new-client/tools/godot_net/…win64.exe` | **replaced**: `linux/godot/…mono_linux.x86_64` | godotengine's zip, SHA-512 pinned; run with `--path` exactly as on Windows (no export, no export templates) |
| Launcher (WinForms) | `runtime/OfflineDAoC.exe`, `new-client/runtime` | **replaced** for Linux; Windows unchanged | see the options below |
| Setup (`LivingServer-Setup.exe`, `Get-OfflineDAoC.ps1`) | Windows exe + PowerShell | **ported**: `install.sh` + `offlinedaoc setup` | same manifests, same checks, same order |
| shadowofze's patcher (`OfflineDaoc.Patcher.dll`, in his patch zip) | run on `tools/dotnet` | **reused**: run on `linux/dotnet` | it uses `/` paths and `Path.Combine`; it looks for `runtime/OfflineDAoC.exe`, which the folder keeps |
| Living Server install / rollback (`install-server-build.ps1`, Windows PowerShell 5.1) | `INSTALL.cmd`, `ROLLBACK.cmd` | **ported** to C# in the launcher's shared code | the ps1 stays the Windows path; CI runs both on the same fake game folders and compares the results |
| Settings helpers (`tools/claude-version/*.py`) | Python 3 | **reused** | already portable |

### The launcher: three options

What a launcher must keep from shadowofze's (the parts that matter for play): the local account (`account.txt`,
made on first use, never replaced), one server per port with a plain reason when it refuses, a graceful stop through
`exit`, the server log in `runtime/logs/server-console.log`, "the server stopped by itself" with the exit code,
ENTER REALM's arguments (`--server --account --password`, `--custom-class=off` on 0.37), the new client's own save
folder, the low-memory warning, bots (+Lv.1 / +Lv.50, delete), the Living Server switches, XP rates and the GM option.
The dashboards (population, Exchange, groups, RvR, battlegrounds, records, bestiary, Herald) are nice to have.

| | A. Avalonia rewrite of the essential tabs | B. A launcher built in Godot | C. Command line (+ desktop entries), TUI later |
|---|---|---|---|
| What | A cross-platform C# window: main screen, Living Server tab, Options | A small Godot project (or a scene in the new client) that starts the server and the game | `offlinedaoc start/stop/status/play/bots/settings/herald/update/rollback`, two desktop entries |
| Reuse | the launcher's UI-free code (§ below) | little: GDScript has no SQLite; C# in Godot can link the same code | the launcher's UI-free code |
| Feels like | the Windows launcher | the game | a Linux tool; one click for "play" |
| Risks | UI work; X11/Wayland/HiDPI testing without a Linux desktop | touches the new client (shared with Windows, shipped as shadowofze's files plus content patches); closing the game must not stop the launcher | less friendly for non-technical players; no dashboards at first |
| CI testable | yes (Avalonia headless) | yes (Godot headless) | yes, completely (the prototype already is) |
| Effort | 3–5 weeks | 3–4 weeks | 1.5–2.5 weeks |

**Recommendation: C for 1.2.1**, built as a .NET console program over the launcher's own non-UI code, so Windows and
Linux run the same logic. Most of it is already UI-free (`PortableCredentials`, `ServerPortProbe`,
`SocialExpansionSettings` and its recommended set, `BotCharacterGenerator`, `RollingServerLog`, `KeepRelicReset`,
`HeraldPage`, `Updates/*`). The bot batches and the XP/GM saves are still inside `MainForm.cs` and move into shared
classes first; the WinForms project links the same files, so Windows behaviour doesn't change and its tests keep
passing. The Herald page is HTML already: `offlinedaoc herald` opens it in the browser. **A** is the follow-up if the
friends ask for a window (the dashboards), and could later replace WinForms on both systems. **B** is not
recommended: it ties the launcher to the client's process and to files that are shadowofze's.

The bash `tools/linux/offlinedaoc` in this branch is the prototype of C's start/stop/status/play/account behaviour,
not the product.

## 5. The auto-updater on Linux

- **Same feed, same rules.** `ReleaseFeed` (GitHub's `/releases/latest`), `PackageDigest` (GitHub's SHA-256 digest of
  the asset, fail closed without one) and `PackageCheck` (one `LivingServer-<build>` folder, `BUILD-INFO.txt` naming
  the tag's build, the server's hash) are reused unchanged. Only the asset name differs:
  `OfflineDAoC-LivingServer-<build>-linux-x64.tar.gz` (`ReleaseInfo.PackageName` gets a platform argument; Windows
  keeps its name). A release without the Linux asset is simply "no Linux update yet".
- **Applying it.** Windows needs `apply-update.ps1` because a running exe can't be replaced. On Linux the launcher
  stops the server (asking first), unpacks the package into `runtime/updates/<build>`, runs the C# install (backups,
  verified copies, content patches) and replaces its own files by rename, which a running program allows. The result
  is written as on Windows (`Build=`, `Result=`, `Detail=`). Refused while the server or the game runs.
- **The base game** won't change again: shadowofze has stopped (no 0.38), so his patcher runs once, at install. The
  Linux installer accepts 0.37/0.37b/0.37c folders made on Windows too (a friend can copy his game folder over and
  run `install.sh --game <folder>`).
- **Rollback** is per install, as on Windows: the newest backup's files go back, the database never.

## 6. Where the game content comes from (legal)

- **Exactly as on Windows.** Every EA-derived file (the original client, the converted new-client content of about
  33 GB, the world data) comes from **shadowofze's own GitHub releases**, downloaded by the player's machine and
  checked against his published manifests, the way Setup runs his downloader on Windows. Living Server hosts and
  mirrors none of it, and the Linux package contains none of it. The Linux installer refuses to continue if a part
  fails its check; it has no fallback source (PR #51's fallback copy is an open owner and legal decision and is not
  used on Linux).
- **The player's own client, later.** The content pipeline (`source/new-client/pipeline`, Python + Real-ESRGAN, whose
  ncnn-vulkan build exists for Linux) can convert a player's own DAoC client into the new client's content. It takes
  many hours and needs a GPU; it isn't part of 1.2.1.
- **Third-party runtimes** (.NET: MIT; Godot: MIT) are downloaded from their publishers with pinned hashes. Shipping
  them inside the asset instead is allowed by their licences and is an owner choice (§10).
- The Linux Detour library (`lib/Detour.so`) is built from the repository's own source (recastnavigation, zlib licence), credited in
  THIRD_PARTY.md like the Windows DLL.

## 7. The CI jobs that prove it

| Job | Runner | What it proves | State |
|---|---|---|---|
| `linux / linux-package` | ubuntu-22.04 | server build + the Linux Detour library + Microsoft's runtime assembled in the package layout; `offlinedaoc start`; two scripted players log in (playtester `chat-players`, `group-players`); the released 0.37c new client (scripts + content patches) compiles on the Linux Godot .NET engine with its C# built on Linux and **logs in headless with the folder's account**; `stop` (`exit`), start, stop | in this branch (`.github/workflows/linux.yml`), green on GitHub; runs on `workflow_dispatch` and on changes under `tools/linux/` |
| `linux / launcher` | ubuntu-latest | the C# launcher: `dotnet test` of the shared code on Linux (account file, port probe, settings save, bot batch, update check with recorded answers) | to build |
| `linux / install-parity` | ubuntu + windows | the C# install and rollback against the fake game folders of `test-install-scripts.ps1`; the resulting trees and `install-record.json` must equal the ps1's | to build |
| `linux / real-download` | ubuntu-latest (large disk) | `install.sh` with real downloads: the base game, the 0.37 patch with his patcher on Linux .NET, Living Server, `offlinedaoc play --headless` logging in; stop and resume a part | to build; needs about 45–60 GB of disk (see risks) |
| `linux / release` | ubuntu | builds the Linux asset from the release's own package and attaches it next to Setup (the release workflow's pattern) | to build |

Existing jobs are not changed. Automated results are what they are: none of them is a played session.

## 8. Risks and unknowns

1. **No real Linux desktop has run it.** Rendering (Vulkan on Mesa and NVIDIA drivers), Wayland vs X11, audio
   (PulseAudio/PipeWire), mouse capture for right-button steering, HiDPI, game controllers: untested. The first
   friend's evening is the real test; a short checklist for them goes with the release.
2. **Case-sensitive file systems.** Windows ignores letter case; Linux doesn't. Checked: none of the 0.36/0.37 file
   lists has two paths differing only in case, nor does any 0.37 path differ in case from its 0.36 file, and the
   converted content is lower case except 40 icon sheets (`ui/icons/original/SPL_0.png`…), which the code names in
   the same case. Not checkable without content: names built at run time from server data or catalogs. A wrong one
   shows as a missing model or texture in play. Fallback: a case-folding directory (ext4 casefold) for `content/`.
3. **CI disk.** The real 0.37c install needs about 60 GB while installing; GitHub's standard Linux runners have far
   less free. The real-download job may need a larger runner or must test the new client's parts on a subset.
4. **Godot's engine binary as the runtime.** The Windows release runs the editor build with `--path`; Linux does the
   same. It works headless here; a desktop run with real content is unverified (shader compile time, memory).
5. **Process checks written for Windows.** shadowofze's patcher detects a running game by process names (`CoreServer`,
   `game`, the Windows Godot name) and by port 10300. On Linux the server is a `dotnet` process, so only the port check
   applies; the Linux launcher must refuse updates while its own pid file is live. The same goes for
   `fork-build-common.ps1`'s `Get-Process` list, which the C# port replaces.
6. **System libraries.** Godot needs glibc 2.28+ (Ubuntu 18.10+, Debian 10+); the Detour library needs 2.14; the server
   needs libicu (globalization is on); `SQLite.Interop.dll` (linux-x64) needs 2.14. Very old or musl distributions
   (Alpine) aren't supported.
7. **Memory.** The server's server-GC with a large bot world plus the client on one machine: as on Windows (16 GB
   recommended), not measured on Linux.
8. **Found while prototyping, not Linux-specific:** `source/new-client/godot/game/net/server_world.gd` on
   `integration/1.2` declares `_server_chose_target` and `_change_target` twice (a duplicated block from the phase A
   merge, 1ac34065), so the source tree's `server_world.gd`, `server.gd` and `zone_viewer.gd` don't parse. Players
   aren't affected (that file isn't one of the content patches; they run the released one), but any future content
   patch of it would break the new client. To fix on its own branch.

## 9. The prototypes in this branch (2026-11-07, in the container)

All automated: in this container (Ubuntu 24.04, no GPU, no display, no converted content) and, for the CI row, on
GitHub's ubuntu-22.04 runner. None is a played session.

| Spike | Result |
|---|---|
| **Linux server in the package layout** | **Worked.** `dotnet build` of the server on Linux (the same IL the Windows package ships), Detour built with cmake (needs glibc 2.14), Microsoft's ASP.NET Core 10.0.14 runtime (SHA-512 checked against its release metadata) as `linux/dotnet`, the release `serverconfig.xml` unchanged, a clean test world (`make_test_world.py from-release`). `offlinedaoc start` → listening in 22–26 s; `FORK_BUILD` logged; `lib/Detour.so` mapped; **all 32 `.nav` files of the uploaded navmesh subset loaded through Detour**; two scripted players (playtester `--attach`: `chat-players`, `group-players`) passed; `stop` via `exit`; second start and stop clean. |
| ↳ found on the way | The Detour file must be **`lib/Detour.so`**, not `libDetour.so`: with a folder in the import name (`lib/Detour`) .NET tries only `lib/Detour.so` and `lib/Detour` (its own error message lists the paths). The first run failed on exactly this. |
| **Godot Linux new client** | **Worked, headless, without content.** Godot 4.7.2 .NET for Linux (SHA-512 checked). The managed API (`GodotSharp/Api/*`) is **byte-identical** to the Windows engine's: 67 of 73 shared files match, the rest are Windows-only IDE helpers and NuGet containers. The released 0.37c scripts with Living Server's content patches: the C# network client builds on Linux and **all 95 scripts compile** in the mono engine (`load_check.gd`), and `DaocClient.cs` instantiates. Then `offlinedaoc play --headless` (the folder's `account.txt`, the launcher's ENTER REALM arguments) **logged in to the Linux server**: "User offline logging on", "New account created: offline", the Account row present; the client went on to the pregame (realm/character select) scene and stopped there on missing converted content (`content/pregame/pregame.json`), as expected here. Rendering, sound and input were not tried (no GPU or display). No export and no export templates are needed (§4). |
| ↳ found on the way | The source tree's `server_world.gd` doesn't parse (risk 8): the check uses the released scripts, as players have them. And on a fresh install the first login is the only account creation; the server allows one per IP every 15 minutes (`time_between_account_creation_sameip`), so the smoke logs the new client in before the scripted players. |
| shadowofze's patcher on Linux .NET | **Started only.** Built from `source/tools/OfflineDaoc.Patcher` and run on `linux/dotnet` against an empty folder: it refused it with exit 4, as designed. Not run on a real 0.36 folder (5 GB download). |
| **CI job sketch** | **Worked on GitHub.** `.github/workflows/linux.yml` (new file, `workflow_dispatch` and `tools/linux/**` changes only) runs the same smoke on ubuntu-22.04 with the minimal world, no game files: every check passed in run 42434529818 (navmeshes skipped: Detour loads, no zone meshes in CI). The first run (42434303637) found a race in the prototype launcher's start (it checked the process before it had become `dotnet`, 5 ms after starting); fixed. |
| `install-server-build.ps1` under PowerShell 7 on Linux | **Not tried** (the plan ports it to C# instead, §4). |

Container notes: the server ran with `DOTNET_gcServer=0` (a 7 GB container shared with another agent's server); the
release keeps its server GC setting. The world was a test world; the navmeshes were a 32-zone subset, linked, not copied.

Reproduce (paths are examples):
```
cmake -S source/server/Pathing/Detour -B /tmp/detour -DCMAKE_BUILD_TYPE=Release -DSOVERSION=1 -DLIB_VERSION=1.0 && cmake --build /tmp/detour
dotnet build source/server/CoreServer/CoreServer.csproj -c Release
python3 source/tools/make_test_world.py from-release --world <clean 0.37b world> --out /tmp/test-world.db
tools/linux/assemble-server.sh --server-build source/server/Release --detour /tmp/detour/libDetour.so.1.0 \
    --world /tmp/test-world.db --dotnet <aspnetcore runtime folder> --navmesh <navmesh folder> --out /tmp/OfflineDAoC
# put Godot 4.7.2 .NET for Linux in /tmp/OfflineDAoC/linux/godot, build the playtester, then:
tools/linux/smoke-server.sh --game /tmp/OfflineDAoC --playtester <OfflineDaoc.Playtester.dll> \
    --new-client /tmp/OfflineDAoC/linux/godot/Godot_v4.7.2-stable_mono_linux.x86_64 --out /tmp/smoke
```

## 10. Open decisions for the owner

1. **The launcher:** C (command line, recommended) for 1.2.1, with A (a window) later if the friends ask?
2. **.NET and Godot downloaded from their publishers** (small asset, recommended) or **shipped inside** the Linux asset
   (about 160 MB more, no third-party download at install)?
3. **Extract the original Windows client** on Linux (keeps the folder identical and ready for the Wine add-on;
   recommended) or skip it to save disk?
4. **Linux in the release workflow from 1.2.1 on**, or a separate "Linux preview" release first?
5. **Who runs the first real check:** one friend, with the checklist, before 1.2.1 is announced?

## 11. Effort

| Part | Effort (working days) |
|---|---|
| Server on Linux: Detour in CI, runtime, layout, start/stop (prototype done) | 2–3 |
| Shared launcher core (move the bot batches and settings saves out of `MainForm`; Windows unchanged) | 2–3 |
| The Linux launcher, option C (start, stop, status, play, account, bots, settings, herald, update, rollback) | 5–7 |
| Setup on Linux (`install.sh`, shadowofze's manifests and patcher, resume, disk and driver checks) | 5–8 |
| Living Server install and rollback in C#, with the parity tests against the ps1 | 4–6 |
| The new client on Linux (engine download, save folder, desktop entries, the case check) | 2–4 |
| The updater on Linux | 2–3 |
| CI: the four jobs still to build, and the release asset | 3–5 |
| Docs (PLAY, KNOWN-ISSUES, a friend's checklist) and release notes | 1–2 |
| **Total** | **about 26–41 days**, plus the friends' first evening and its fixes |
