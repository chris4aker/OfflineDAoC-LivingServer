# Developing Offline DAoC

This guide is for people (and AI assistants) who change the code. Players only need
[PLAY.md](PLAY.md).

## Layout

| Where | What |
|---|---|
| `source/server` | OpenDAoC-based server (`Dawn of Light.sln`): game logic, companion bots, autonomous gamebots, tests |
| `source/tools/OfflineDaoc.Launcher` | Windows launcher (server control, bot creation, dashboards) with tests in `OfflineDaoc.Launcher.Tests`. The same program is the new client's launcher in 0.36c. |
| `source/tools/OfflineDaoc.ProgressImport` | Progress transfer tool |
| `source/tools/build_release_036.py`, `assemble_release_036.py`, `seal_release_036.py`, `smoke_release_036.py`, `test_release_import_036.py` | Build, assemble, seal and test the public 0.36 / 0.36b package |
| `source/tools/build_newclient_036.py`, `seal_newclient_036.py` | Build and seal the 0.36c new-client package |
| `source/tools/build_release_037.py`, `assemble_release_037.py`, `build_newclient_037.py` | Build the 0.37 reference packages (what a fresh install of 0.37 is; never uploaded) |
| `source/tools/make_patch.py`, `OfflineDaoc.Patcher`, `make_downloaders_037.py` | Build the 0.37 update patches, the patcher they carry, and the downloader zips |
| `source/tools/release-baselines/`, `find_changes_since_release.py` | What each full release shipped, and what changed since |
| `source/tools/make_downloaders_036.py` | Makes the small downloader zips of the three release pages |
| `source/tools/release-world-defaults.json` | Default owner and level of every keep and the state of every keep door, applied to every release world |
| `source/new-client` | The new client (Godot 4, C#) and its content pipeline. See its [README](../source/new-client/README.md). |
| `source/development-tools` | Navigation mesh builder and pathing source |
| `tools/pet-art`, `tools/asset-tool` | Client art pipeline and the MPK and texture helpers |
| `tools/claude-version` | Small database and client helpers used since 0.33 |

A playable folder has:
- `runtime/server`: server binaries plus `navmesh/`
- `runtime/data/opendaoc.sqlite3.db`: world plus saves
- `runtime/client-opendaoc/app`: the original game client
- `runtime/OfflineDAoC.exe`: the launcher
- `runtime/account.txt`: the local account (made on the first ENTER REALM)
- in 0.36c, `new-client/`: the new client, its engine, its launcher and its settings (see below)

## Build and test

You need the **.NET 10 SDK** on Windows. The playable download bundles only the runtime.

```bash
cp source/server/CoreServer/config/serverconfig.example.xml source/server/CoreServer/config/serverconfig.xml
dotnet build "source/server/Dawn of Light.sln" -c Release
dotnet test source/server/Tests/Tests.csproj -c Release
dotnet test source/tools/OfflineDaoc.Launcher.Tests/OfflineDaoc.Launcher.Tests.csproj -c Release
```

- `serverconfig.xml` is local and ignored by git. The build only needs it to exist.
- Some launcher tests expect that no local DAoC server is running.
- At 0.37: 2,790 server tests and 107 launcher tests pass.

The new client builds separately; see [source/new-client/README.md](../source/new-client/README.md).

## Deploy a build into a playable folder

1. **Stop everything:** the launcher, the game and the server.
2. **Back up the files you'll replace.**
3. **Copy the server build:** `source/server/Release/lib/GameServer.dll` (and its `.pdb`) goes to
   `runtime/server`, `runtime/server/lib` and `runtime/server/win-x64`.
4. **Copy the launcher build** if you changed it: the `OfflineDAoC.*` files from
   `source/tools/OfflineDaoc.Launcher/bin/Release/net10.0-windows` go to `runtime/` (and, in 0.36c, to
   `new-client/runtime/`).
5. **Test in game.** Automated tests are not a substitute for an in-game check.

## Editions and the custom class

- **The switch:** the server setting `classes / enable_sluaghbinder`
  (`ServerProperties.Properties.ENABLE_SLUAGHBINDER`) is on by default.
- **When it's off:**
  - `AutonomousBotIdentityGenerator` never rolls or lists the Sluaghbinder.
  - The launcher's bot batches skip class 63.
  - Character creation treats the Hibernian Mauler slot as the disabled native class again.
- **The 0.36 database** sets the switch off and removes the Sluaghbinder trainer, the wisp and the
  class's skill rows. The 0.36 client (SHA-256 `f55ed6b0…`, unchanged since 0.35) is the 0.36b client
  without its two Sluaghbinder patches: the normal v0.32 `game.dll` (`67dcf68a…`) with
  `patch_bounty_map_client.py`, `patch_quest_marker_range_client.py`, `patch_classic_warmap_client.py`
  and `patch_quest_journal_button_client.py` applied, in that order.
- **0.36b** uses the Sluaghbinder client `game.dll` (`e1d471bb…`), which relabels the Mauler slot.
- **The launcher** reads the same switch and shows VERSION 0.36b or VERSION 0.36.

## The new client and the shared server (0.36c)

- A 0.36c folder is a 0.36b or 0.36 folder plus `new-client\`:
  - `godot\` (the Godot project with converted content and the built C# network client)
  - `tools\godot_net\` (the Godot .NET engine)
  - `runtime\` (the launcher, plus `server_root.txt` = `..\..\runtime`)
  - `save\` (the new client's settings, keybinds and logs; the engine's `APPDATA`)
- The launcher switches to its new-client mode when `..\godot\project.godot` exists beside it. In that
  mode it runs the server named in `server_root.txt`, reads that folder's `account.txt`, and starts
  the engine with `--path godot -- --server=127.0.0.1 --account=… --password=…` on the game's bundled
  .NET (`tools\dotnet`).
- So both launchers use one server, one database and one account: a character, bot roster and world
  continue from one client to the other. Only one launcher may run at a time (one server, port 10300).
- On a 0.36 game (`classes / enable_sluaghbinder` = `False`) the launcher adds `--custom-class=off`, and the
  new client leaves the Sluaghbinder (class 63) out of character creation (`game/net/server.gd`). The server is
  the same program in both editions, so nothing else differs.
- The progress importer also checks that the new client is closed before it changes a save.

## Building the public packages

`source/tools/build_release_036.py` takes a 1:1 snapshot of a development install and produces a
clean 0.36b package with the 0.36 edition files.

What it copies and changes:
- It copies the runtime without logs, backups, accounts, dumps, DXVK files or run-state and
  diagnostics switches.
- It empties every saved-progress table and resets relics, houses and guild earnings.
- It restores every keep's owner and level and every keep door from `release-world-defaults.json`.
- It sets the public defaults: zero bots, GM off, 1× XP and automatic account creation.
- It installs the release server and launcher builds.
- It bundles the .NET runtime.
- It writes the 0.36 edition files.

Checks:
- Everything it copies is recorded with a hash; it verifies the cleaned world (no accounts,
  characters or bots, default keeps and doors, relics at home) and the shipped world data.
- `assemble_release_036.py` adds the player files, docs, source and tools, and
  `seal_release_036.py` scans for anything personal, hashes every file, zips the package, re-checks
  every zip entry and splits it into release parts with their download manifests.
- `smoke_release_036.py` then starts the server with only the bundled .NET and logs in through
  `connect.exe`, confirming that a fresh account is created.
- `test_release_import_036.py` imports earlier versions' saves into the package.

`source/tools/build_newclient_036.py` builds the 0.36c package from a new-client development copy
(no developer mode, debug probes or `save` folder; the network client must be built from the current
code), and `seal_newclient_036.py` packs it into independent, verified zip parts below 2 GB. Each part
unpacks on its own, so the downloader removes a part as soon as it is unpacked.

## Updates (patches) from 0.37 on

0.36 / 0.36b / 0.36c are the full downloads; later versions are patches on that base, so players never download
the whole game again. [source/tools/release-baselines/README.md](../source/tools/release-baselines/README.md) has the
design. To make one:
1. `find_changes_since_release.py --baseline 0.36 …` lists what the server and client have changed.
2. Build the reference packages of the new version exactly as a full release (`build_release_037.py`,
   `assemble_release_037.py`, `build_newclient_037.py`): they are what a fresh install must equal.
3. `make_patch.py --version 0.37 …` diffs them against the baseline into a game patch (both editions; world updates
   per edition, checked by applying them to the released world) and a client patch, packs verified zip parts and
   writes the three releases' download manifests; `make_downloaders_037.py` adds the downloader zips.
4. The patcher (`OfflineDaoc.Patcher`, run with the game's bundled .NET) checks every file it replaces against its
   released fingerprints, backs up to `patch-backups\`, applies the world update once (recorded in
   `offline_patch_log`; a world row changes only if it still has its released values, save tables never), writes
   the files with the launcher last, and rolls back on any failure. Exit codes: 0 updated, 2 already up to date,
   3 changed files (listed), 4 unknown folder, 5 game running, 1 failed and restored.
5. `Get-OfflineDAoC.ps1` mode `patch`: `-Update <folder|pick>` updates a game (and its new client, or offers to add
   one with 0.37c); without it, a new install of the 0.36 base and then the update. Changed files lead to an offer of
   a fresh install with the progress importer.

Patches are cumulative from the base: the next one (0.38) is built against the same 0.36 baseline, with the
previous patch's file fingerprints accepted too.

## Releasing Living Server

The owner publishes the release with its package and notes; the release workflow does the rest.

1. **Name the release on main.** Rename the changelog's `## Beta X (in development)` heading to `## Beta X — <date>`,
   set `ForkBuildInfo.BuildName` to `X-beta`, and move README.md to Beta X: its "**Beta X is out.**" sentence, its
   release links and the file names it quotes. Build-and-test's `versions` job fails while these disagree
   (`python tools/release/check_versions.py` says what is off). A heading marked "(in development)" isn't released, so
   README.md stays on the last release until then.
2. **Publish the release.** Make it as a draft with tag `vX-beta`, attach the package
   `OfflineDAoC-LivingServer-X-beta.zip`, write the notes and publish. In the notes' file table, the Setup row is
   ```
   | `LivingServer-Setup-X-beta.exe` | (filled in when the exe is built) |
   ```
   The package must be attached before publishing: the workflow starts at that moment.
3. **The release workflow takes over** (`.github/workflows/release.yml`). It builds Setup on Windows from the release's
   own package (the installer workflow's build and end-to-end jobs, called as a reusable workflow), runs the real
   download tests (one to three hours), then attaches `LivingServer-Setup-X-beta.exe` and replaces the placeholder with
   its SHA-256. It checks that the package Setup embeds is the release's zip, byte for byte. If a test fails, nothing
   is attached or edited.

Running it again is safe (`tools/release/publish_setup.py`): an exe already attached with the same SHA-256 isn't
uploaded again, notes that show it aren't edited, and an exe attached with a different SHA-256 is never replaced (the
run stops and says so; delete the asset by hand to attach a new build). To run it by hand, use Actions → release →
Run workflow with the tag. **Dry run** is on by default: it builds and tests, then prints what it would upload and the
notes it would write, with a read-only token. On publish the workflow runs from the tag's commit; by hand, from the
branch you pick (GitHub only offers it once the file is on main).

### Recommended settings for main

Not applied by any script; the owner sets them under Settings → Branches (or Rules):
- require a pull request before merging, with no required review (a single maintainer couldn't merge otherwise);
- require the status checks `server` and `versions` (build-and-test) to pass;
- block force pushes and block deletion;
- let the owner (repository admin) bypass, for hotfixes.

CI runs on every push to main as well, so a direct push or a merge is still tested afterwards.

## Sharing safely

- **Never commit a played database.** It contains accounts, characters, bot rosters, inventories
  and economy history. Also keep `account.txt`, logs, dumps and backups out of commits.
- **Review before pushing.** Run `git diff --cached` before every push; `.gitignore` is a safety
  net only.
- **Local only:** the default setup is for one PC. Don't expose it to the internet as-is.
