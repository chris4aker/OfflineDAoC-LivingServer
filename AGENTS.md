# Offline DAoC — instructions for AI assistants and developers

Read this first, then [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). This file is also loaded by
Claude Code as `CLAUDE.md`.

## What this is

Offline DAoC is a single-player DAoC server (OpenDAoC fork, .NET 10, SQLite) with autonomous
gamebots, companion bots and a Windows launcher. The current release is **0.37**, the first one
shipped as an **update** to the 0.36 base (0.36 / 0.36b / 0.36c stay the full downloads). It comes in
three editions:
- **0.37b:** the custom Hibernian Sluaghbinder class is on.
- **0.37:** the class is off. The server setting `classes / enable_sluaghbinder` is `False`, and
  the client is the 0.37b `game.dll` without its two Sluaghbinder patches.
- **0.37c:** the 0.37b or 0.37 game plus the **new client** (Godot 4, C#) in `new-client\`. Its launcher
  runs that game's own server, so both clients share one account, bot roster and world. On a 0.37
  game the launcher starts it with `--custom-class=off` and it offers the classic classes only.

From 0.37 on a version is a game patch (both editions) plus a new-client patch, built by
`source/tools/make_patch.py` against `source/tools/release-baselines/0.36/` and applied by
`source/tools/OfflineDaoc.Patcher` (see [release-baselines/README.md](source/tools/release-baselines/README.md)).

## Layout

| Path | What it is |
|---|---|
| `source/server` | Game server, all bot AI, tests (`Tests/`). The solution is `Dawn of Light.sln`. |
| `source/server/GameServer/bots` | Companion bots, class AI, bot brains |
| `source/server/GameServer/bots/autonomous` | Autonomous gamebots: goals, camps, groups, travel, watchdog |
| `source/tools/OfflineDaoc.Launcher` | Launcher (the in-game version label is `MainForm.DisplayVersion`) |
| `source/tools/OfflineDaoc.ProgressImport` | Progress transfer tool (`progress-policy.json` lists the saved-progress tables) |
| `source/tools/build_release_037.py` | Builds the clean 0.37 reference package from a local install (then `assemble_release_037.py`); `*_036.py` built the 0.36 full downloads |
| `source/tools/make_patch.py` | Builds the update patches (game + new client, world updates per edition) and their download manifests |
| `source/tools/OfflineDaoc.Patcher` | The patcher shipped in every patch: checks, backs up, applies files and world updates, rolls back |
| `source/tools/find_changes_since_release.py` | Lists what changed since a released base (input for the next patch) |
| `source/tools/build_newclient_037.py` | Builds the 0.37c new-client reference package (`build_newclient_036.py` + `seal_newclient_036.py` made the 0.36c full download) |
| `source/tools/make_downloaders_037.py` | Makes the downloader zips (`Download-OfflineDAoC-v0.37*.zip`) |
| `source/tools/smoke_release_037.py` | Starts a package's server and logs in, as a player would |
| `source/tools/release-world-defaults.json` | The default keep owners, levels and doors every release world starts with |
| `source/new-client` | The new client: Godot 4 project (`godot/`), its C# network client and the content pipeline. Converted game content is not in Git. |
| `source/tools/*_035.py`, `*_02.py` | Earlier releases' pipelines, kept for reference |
| `tools/pet-art` | Art pipeline for the private pet models and skins, pet weapons and spell effects. Each install step has a rollback. |
| `docs/` | Player and developer guides; `docs/history/` holds earlier versions' notes |

In a playable folder:
- `runtime/server`: the server binaries and navigation meshes
- `runtime/data/opendaoc.sqlite3.db`: world data plus the player's saves
- `runtime/client-opendaoc/app`: the original game client
- `new-client/` (0.37c only): `godot/` (the new client), `tools/godot_net/` (the engine),
  `runtime/` (its launcher; `server_root.txt` points it at `..\..\runtime`) and `save/` (its settings)

## Rules

- **Protect player data.**
  - Never commit or publish a played database, `account.txt`, logs, bot rosters, backups or
    credentials.
  - Public packages are built only by `build_release_0xx.py`, which empties every saved-progress
    table, restores the default world (keeps, doors, relics) and verifies it. No account ships;
    automatic account creation is on.
  - Never ship DXVK (`d3d9.dll`, `dxvk.conf`), diagnostics switches or the new client's
    `dev_mode.txt`.
- **Keep the owner's gameplay rules.** Preserve these unless the owner asks otherwise:
  - real loot, inventories, coins and equipment upgrades
  - the Realm Exchange
  - stablemaster routes
  - selling only at natural task breaks
  - gamebots start without armor and earn it
  - Darkness Falls is open to solo bots
- **Tell bot types apart.** Real players, companion bots and autonomous gamebots are different.
  Check which one a change affects.
- **Don't start or deploy without permission.**
  - Never start a server or copy builds over a running install without asking.
  - Only one server can use port 10300 at a time.
- **Build and test separately.** Deploy only tested outputs, with a backup of what you replace:
  `GameServer.dll` goes to `runtime/server`, `runtime/server/lib` and `runtime/server/win-x64`.
- **Client files:**
  - Keep hash guards on any binary patch; never patch an unverified `game.dll`.
  - Private art is added under new IDs; stock models, skins and effects are never edited in place.
  - `tools/pet-art` shows the pattern: byte copies of NIFs with same-length texture renames, and
    new catalog rows.
- **Navigation:** don't rebuild or replace navmeshes globally to fix one route without evidence.
- **Be honest in reports.** Report automated tests separately from real in-game checks. Don't
  describe Darkness Falls raid AI, Legion or the hardest level 70+ encounters as implemented.
  - A feature counts as working only once it has been seen working in one of the owner's PC logs
    (owner, 2026-11-08). Until then the notes say "tests only". Unit tests, CI, simulations and test-world
    bot runs are all tests.
  - Never give an earlier build's result as the result of the build being shipped. Say which build each
    result came from.
- **Keep the old releases.** v0.3–v0.36c stay available as legacy releases, and their branches
  and tags are not rewritten.

## Useful facts

- **Edition switch:**
  - The switch is `ServerProperties.Properties.ENABLE_SLUAGHBINDER`. It defaults to on, is read
    from the database and is honored by the bot generator, character creation and launcher bot
    batches.
  - The Sluaghbinder uses the client's Hibernian Mauler slot (class 62), and the novice is stored
    as Acolyte (16) until promotion to class 63.
- **Item model chain:** `objects.csv` model ID → `items.csv` row → `items/<nif>`.
  - Shields that carry an emblem take their base texture from a `pskins.csv` override.
  - `tools/pet-art/nif4_geom.py` reads the old 4.x item NIFs that pyffi can't.
- **Client art, spells and sounds:** read [docs/CLIENT-MODDING-GUIDE.txt](docs/CLIENT-MODDING-GUIDE.txt)
  before changing models, meshes, skins, spell effects, icons or NPC sounds. It lists the client's
  catalog rules (no blank lines, terminators, Expansion Only, ID order), confirmed and unconfirmed
  limits, every tool, and past mistakes.
- **Helmets with no face:** see [docs/HELMET-FACE-FIX.md](docs/HELMET-FACE-FIX.md) and
  `tools/claude-version/helmet_face_check.py`.
- **Two clients, one server (0.36c and later):** the launcher switches to its new-client mode when
  `..\godot\project.godot` exists beside it (`MainForm.IsNewClientFolder`). Both launchers read
  the same `runtime/account.txt` (`PortableCredentials`), so either client logs in to the same
  account.
- **Server settings:** Offline-specific settings are in the database tables
  `offline_population_settings` and `offline_local_options`, and in `runtime/server/bot-goals.json`.

The owner may customize their fork's rules. These are safety defaults, not a ban on changes the
owner asks for.
