# Naming

Issue #645 (owner, 2026-11-25: "DAoC-LivingServer should be the name, and standard"). It follows #641, which renamed
what players see.

## The standard

| Where | Name | Example |
|---|---|---|
| What players read: Setup, launcher titles, Start menu, About, README title | **DAoC - Living Server** ("Living Server" for short in running text) | "DAoC - Living Server" |
| Repositories, folders, files, packages, tools, scripts and docs | **`DAoC-LivingServer`** | `DAoC-LivingServer-1.2.3.zip`, `DAoC-LivingServer-Setup-1.2.3.exe`, `Assets/DAoC-LivingServer-header.png` |
| Where a hyphen can't go: C# namespaces, project and assembly names, the launcher exe | **`DAoCLivingServer`** | `DAoCLivingServer.Launcher` (namespace, project and its folder `source/tools/DAoCLivingServer.Launcher`), `DAoCLivingServer.exe` |

- The default install folder is `C:\Games\LivingServer` (#641).
- The repositories keep `OfflineDAoC-LivingServer` and `OfflineDAoC-base-files` until the owner renames them in GitHub's
  settings. A follow-up pull request then updates every URL.
- A project folder carries its project's name, so it uses the no-hyphen form (`DAoCLivingServer.Passport`).

## What #645 renamed

- The twelve projects under `source/tools`, each with its folder, csproj and namespace: `OfflineDaoc.Launcher`,
  `.Launcher.Tests`, `.ProgressImport`, `.ProgressImport.Tests`, `.Patcher`, `.Passport`, `.Passport.Cli`,
  `.Passport.Tests`, `.Playtester`, `.Playtester.Tests`, `.Mpk` and `.Setup` all became `DAoCLivingServer.*`.
  `Dawn of Light.sln` lists none of them, so it was unchanged.
- The namespaces those projects declare, and the two the server shares with the launcher through linked files
  (`OfflineDaoc.Shared`, `OfflineDaoc.Configuration`). They are now `DAoCLivingServer.*`.
- The assembly names of the developer and CI tools: Passport, Passport.Cli, Playtester, Mpk, Setup and the test
  projects.
- The embedded resource names inside the launcher and the server: `DAoCLivingServer.Updates.*`,
  `DAoCLivingServer.era-builds-165.json`, `DAoCLivingServer/lore/` and the header and emblem images. Each is read only
  by the assembly that holds it.
- The launcher's art files (`Assets/DAoC-LivingServer-*.png`, `.mpk`).
- The in-memory TempProperties keys (`DAoCLivingServer.BafFightTick`, `.ActiveSpawnClassMenu`, `.PlayerCharmMenu`,
  `.PlayerCharmChoice`). They are never saved.
- The paths in the workflows, the active tools and tests, and the internal docs (AGENTS.md, DEVELOPMENT, CONTRIBUTING,
  CI, OPEN-WORK and the others).

## Kept names

Each of these keeps its old spelling for the reason given. `tools/release/check_brand.py --internal` reads the table
between the markers below: a line outside the legacy paths that still contains `OfflineDaoc` fails unless a pattern
here covers it. Every row needs a pattern (a Python regular expression in backticks) and a reason. The player-visible
check (`check_brand.py` without `--internal`) keeps its own compatibility list and `tools/release/brand-allowlist.json`
(#641); its names are listed here too, so this file is the full list.

<!-- naming-allowlist:start -->
| Pattern | Where | Why it stays |
|---|---|---|
| `OfflineDaoc\.ProgressImport\b` | `DAoCLivingServer.ProgressImport`'s assembly name, so its exe | Installed games run `tools\ProgressImporter\OfflineDaoc.ProgressImport.exe`, and so do Setup (`Engine.cs`), `IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd` and the pinned `Get-OfflineDAoC.ps1`. The project and namespace were renamed. |
| `OfflineDaoc\.Patcher\b` | `DAoCLivingServer.Patcher`'s assembly name, so its dll | The 0.3x update patches run `patcher\OfflineDaoc.Patcher.dll`, and the pinned `Get-OfflineDAoC.ps1` runs it by that name. |
| `OfflineDaoc\.WorldPatch\.exe` | `Get-OfflineDAoC.ps1` | The base game's own world patcher, not ours. |
| `\bOfflineDAoC\.(?:exe\|dll\|pdb\|deps\.json\|runtimeconfig\.json)\b` | launcher, INSTALL/ROLLBACK scripts, Setup, package | The launcher's file names in existing installs. `OfflineDAoC.exe` ships as a copy that opens `DAoCLivingServer.exe` (#641). |
| `\bOfflineDAoC-LivingServer-` | publish-release, `ReleaseInfo`, `LauncherUpdater` | The package name up to 1.2.2. Each release also carries it, for 1.2.x launchers (#641). |
| `chris4aker/OfflineDAoC-(?:LivingServer\|base-files)\b` | every URL | The repositories keep their names until the owner renames them (#645 part 3). |
| `\bGet-OfflineDAoC\.ps1\b` | repository root, Setup | The base game's downloader, pinned by SHA-256. |
| `\bGET-OFFLINE-DAOC-v0\.37[bc]?\.cmd\b` | repository root | The 0.37 legacy downloaders (AGENTS.md "Keep the old releases"). |
| `START OFFLINE DAOC(?: - NEW CLIENT)?\.cmd` | `package-files/`, the base game | The game's start file. Shortcuts and old launchers open it. |
| `IMPORT PROGRESS FROM OLD OFFLINE DAOC\.cmd` | `package-files/` | On disk in installed games, which the docs point players to. |
| `start-offline-daoc\.sh\|offline-daoc-living-server(?:\.desktop\|\.png\|\.svg)?` | `linux/` | Existing Linux installs' menu entries point at the script, desktop file and icon names. |
| `~/OfflineDAoC/` | `linux/` | The Linux scripts' default game folder, where existing Linux installs are. |
| `%LOCALAPPDATA%\\OfflineDAoC\b\|[\"']OfflineDAoC[\"']` | launcher (`JoinFriendProfile`, passports), update scripts | Where existing saved friend profiles and passports live, and the old launcher's process name (an update refuses while it runs). |
| `\bOfflineDAoC Join Friend v1\b\|Offline DAoC Join Friend` | `JoinFriendProfile` | The DPAPI entropy and description that encrypt saved friend passwords. Changing them loses the passwords. |
| `Offline DAoC friends ` | `HostForFriends` | The prefix of the firewall rules a 1.2.x launcher made. They keep their names and are still removed. |
| `\bOfflineDAoC036\b` | launcher display profiles | A display profile name stored in players' settings. |
| `'OfflineDAoC\*'\|playable-v0\.3` | Setup, launcher install discovery | Game folders are found by their old names. |
| `OFFLINE_DAOC_[A-Z0-9_]+` | server tests, `source/server/tools`, `tools/` | About 60 environment variables (`OFFLINE_DAOC_NAV_ROOT`, `OFFLINE_DAOC_DB_PATH`, ...). The owner's PC run commands and the installed-file tests (#619) set them. |
| `\boffline_[a-z_]+\b` | database, launcher, server | The database tables (`offline_population_settings`, `offline_local_options`, `offline_world_bots`, ...) and their settings keys, in every installed world. |
| `(?:zomboden\|stefanrows\|FelixGagnonB\|DetShaft\|reap3rx\|ryan-grafana)/[Oo]ffline-?DAoC[\w-]*\|OfflineDAoC-True165` | credits, research and upstream docs | The upstream forks' repository names. |
| `shadowofze` | credits, research docs | The original author (GPL v3 section 5 attribution, `CREDITS.md`). |
<!-- naming-allowlist:end -->

## Legacy paths

The internal check skips these paths. They are records, kept as they were (AGENTS.md "Keep the old releases"; #641).
The legacy pipelines (`*_02.py`, `*_035.py`, `*_036.py`, `*_037.py`) keep their names, but the source paths inside
them follow the move, so they still run.

<!-- naming-legacy:start -->
| Path | Why |
|---|---|
| `docs/history/**` | earlier versions' notes |
| `source/tools/release-baselines/**` | the released packages' file lists, with the paths they had |
| `CHANGELOG.md` | the original project's changelog |
| `CHANGELOG-LIVING-SERVER.md` | released changelog entries name the paths of their time |
| `changelog.d/**` | other pull requests' fragments, folded in unchanged at release |
| `docs/RELEASE-NOTES-*.md` | release notes of releases made under the old names |
| `older-versions/**` | the legacy downloaders |
| `package-files-0.36c/**` | the 0.36c package files |
| `Get-OfflineDAoC.ps1` | pinned by SHA-256 |
| `GET-OFFLINE-DAOC-v0.37*.cmd` | the 0.37 legacy downloaders |
| `docs/NAMING.md` | this file names the old spellings |
| `tools/release/check_brand.py` | the checker names the old spellings |
| `tools/release/tests/test_check_brand.py` | the checker's tests plant them |
<!-- naming-legacy:end -->
