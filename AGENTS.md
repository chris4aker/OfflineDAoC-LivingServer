# DAoC - Living Server — instructions for AI assistants and developers

Read this first, then [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). Working from another account: also
[docs/CONTRIBUTING.md](docs/CONTRIBUTING.md). Open work, with an issue for each item, is in [docs/OPEN-WORK.md](docs/OPEN-WORK.md). This file is also loaded by
Claude Code as `CLAUDE.md`.

## What this is

DAoC - Living Server is a single-player DAoC server (built on OpenDAoC, .NET 10, SQLite) with autonomous
gamebots, companion bots and a Windows launcher. It is its own repository, owned by the project owner, not a fork.
Releases are numbered **1.x** (1.2.2 is out, 1.2.3 is in release candidates) and ship one edition: the classic classes on the
**original game client**. The product is **DAoC - Living Server** (owner, 2026-11-25, #641): from 1.2.3 the files are
`DAoC-LivingServer-Setup-<version>.exe`, `DAoC-LivingServer-<version>.zip` (plus a copy as
`OfflineDAoC-LivingServer-<version>.zip` for 1.2.x launchers) and the launcher `DAoCLivingServer.exe` (with
`OfflineDAoC.exe` kept as a copy that opens it). Credits live in `CREDITS.md`; `tools/release/check_brand.py` fails on new
player-visible old names, and `--internal` on new internal ones; [docs/NAMING.md](docs/NAMING.md) has the naming standard and every kept old name.

**One edition from now on** (owner, 2026-11-22: "I will support players who already installed the B and C with basic
fixes but I don't want to manage the custom class or incomplete Godot transfer. Everything new moves on as its own
original client version, and I'm about to leave the .37 in the past"):
- **New work targets the original client only.** No new features for the custom Sluaghbinder class (the old "b"
  edition) or the new Godot client (the old "c" edition), and no new work that needs either.
- **Existing b and c installs get basic fixes only:** crashes, data safety and anything that would break their
  saves. Their code stays in the repository so those players keep working; don't delete it, don't extend it.
- **The 0.3x line is history.** 0.37 / 0.37b / 0.37c and earlier stay available as legacy releases (see "Keep the old
  releases"), and their pipelines (`*_037.py`, `make_patch.py`, `release-baselines/`) are kept for reference.

## Layout

| Path | What it is |
|---|---|
| `source/server` | Game server, all bot AI, tests (`Tests/`). The solution is `Dawn of Light.sln`. |
| `source/server/GameServer/bots` | Companion bots, class AI, bot brains |
| `source/server/GameServer/bots/autonomous` | Autonomous gamebots: goals, camps, groups, travel, watchdog |
| `source/tools/DAoCLivingServer.Launcher` | Launcher (the in-game version label is `MainForm.DisplayVersion`) |
| `source/tools/DAoCLivingServer.ProgressImport` | Progress transfer tool (`progress-policy.json` lists the saved-progress tables) |
| `source/tools/build_release_037.py` | Builds the clean 0.37 reference package from a local install (then `assemble_release_037.py`); `*_036.py` built the 0.36 full downloads |
| `source/tools/make_patch.py` | Builds the update patches (game + new client, world updates per edition) and their download manifests |
| `source/tools/DAoCLivingServer.Patcher` | The patcher shipped in every patch: checks, backs up, applies files and world updates, rolls back |
| `source/tools/find_changes_since_release.py` | Lists what changed since a released base (input for the next patch) |
| `source/tools/build_newclient_037.py` | Builds the 0.37c new-client reference package (`build_newclient_036.py` + `seal_newclient_036.py` made the 0.36c full download) |
| `source/tools/make_downloaders_037.py` | Makes the downloader zips (`Download-OfflineDAoC-v0.37*.zip`) |
| `source/tools/smoke_release_037.py` | Starts a package's server and logs in, as a player would |
| `source/tools/release-world-defaults.json` | The default keep owners, levels and doors every release world starts with |
| `source/new-client` | The new client: Godot 4 project (`godot/`), its C# network client and the content pipeline. Converted game content is not in Git. |
| `source/tools/*_035.py`, `*_02.py` | Earlier releases' pipelines, kept for reference |
| `tools/pet-art` | Art pipeline for the private pet models and skins, pet weapons and spell effects. Each install step has a rollback. |
| `docs/` | Player and developer guides; `docs/history/` holds earlier versions' notes |
| `tools/check.sh`, `tools/check.py` | Every Python check of build-and-test, run locally in parallel; required before a push |
| `changelog.d/` | Changelog fragments, one per pull request ([changelog.d/README.md](changelog.d/README.md)) |

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
- **Living Server features are on by default.** Owner, 2026-11-16: "They should be on by default - all our living
  server features should be in by default if someone downloads our installer". This replaces the earlier rule that
  bot-side switches default off.
  - A switch for a player-facing feature defaults on in the server code, in the launcher's
    `SocialExpansionSettings.Defaults` and `Recommended`, and in `source/tools/release-server-settings.json` (what
    `build_release_037.py` writes into the shipped world).
  - When a default changes, add a `LivingServerSettingsUpgrade` line. Worlds that ran an earlier build then move once,
    only where the row still holds the untouched old default.
  - Only these stay off: diagnostics and verbose log switches, test-only switches, anything this file forbids
    shipping, anything that changes the gameplay rules above, the classic rule options (power halving, secondary caps,
    classic quality and losses), the owner's epic encounter options and the bot schedule (`bot_schedule_mode` off: the
    whole roster online; owner, 2026-11-21: "set the server settings so the 'bot schedule' based on time or sessions is
    off by default"). The Sluaghbinder edition switch stays edition-specific.
- **Raid sizes: raise a size, never weaken a boss.** Owner, 2026-11-22: "If the mechanics and tactics are working but the
  period raider size from logs isn't cutting it, and adding more bots without it becoming a Zerg wins, we should bump the
  suggested raider and period size." A period size (`SuggestedBots`) is raised only by the rule in
  [docs/RAID-SIZES.md](docs/RAID-SIZES.md) ("Raising a period size"): the tactic works in a sim, the smallest whole-party
  size reaching about two wins in three (or the limit), at most twice the period source's figure and never past 300, and the rules from
  before the tactic still lose at that size (`RaidPeriodSizes.Raises`).
  - Since 2026-11-23 (owner: "Revert all three"; every 1.2.2 raise was withdrawn on audit), the evidence must also:
    - use the game's loss condition, with real raider deaths;
    - reproduce one PC kill and one PC loss at their logged sizes;
    - check the no-zerg control at the 125% start and with the 24 Special Forces;
    - split a shared raid row first;
    - be pinned in a C# test (`RaidPeriodSizes.Raise.EvidenceTest`).
  - The rule may improve. Owner, 2026-11-23: "if another better way of testing or rule setting is found or feasible,
    suggest and push that too". When a better test or rule is found or becomes feasible (a closer model, a real PC run, a
    sharper control), propose it to the owner and push it as a change to this rule. Don't set it aside because it isn't
    the current rule.
- **Research first, and an issue before building** (owner, 2026-11-24: "Before building any of the new tactics or
  encounter mechanics, you need to check all period sources possible and upstream code to get an understanding. Before
  building new, log each issue or fix needed as an issue on the GitHub repo so we don't lose memory or context or
  direction of your limit hits").
  - Before building a new tactic, encounter mechanic or behaviour, read every period source you can reach (Camelot
    Herald and Wayback archives, period guides, guild sites, patch notes, the repo's research docs) and the upstream
    code that already handles it (OpenDAoC-Core, DOL, the Offline DAoC forks; see docs/UPSTREAM-WATCH.md and
    docs/upstream/). Write down what you found, with sources, before you code.
  - Before building, file each fix or piece of work as a GitHub issue (or comment on the existing one) with the plan,
    the sources and the acceptance test, so the direction survives a usage limit or a lost session. Link the PR to it.
- **Owner decisions come before the code** (owner, 2026-11-25, #615: the 1.2.3 push had "owner decisions after the
  code").
  - When a change needs an owner decision (a gameplay rule, a default, a size, a trade-off), ask it on the issue and add
    the `needs-owner` label before building. Build that part only after the owner answers; build the rest meanwhile.
  - The pull request template's "Owner decisions this needs" lists them; any listed means the issue carries `needs-owner`.
- **Agents: build little, push early, check before pushing** (owner, 2026-11-25, #615: "fix the project fundamentals
  1-5 today, then 6 and 8"; in the 1.2.3 push about 10 agents queued 28 builds behind one lock on a 2-CPU box, and
  work that was committed but never pushed was nearly lost to a restart).
  - **At most 3-4 build-heavy agents at once** (agents that run `dotnet build` or `dotnet test`). Docs, research and
    Python-only agents don't count.
  - **Locally, run the build and the targeted tests only** (`dotnet test --filter` on the classes you touched). The full
    suite is CI's job: every push runs it. Don't run the dotnet build at all unless you changed C#.
  - Share the build machine: `flock /tmp/dotnet.lock dotnet build ... -nodeReuse:false -p:UseSharedCompilation=false`.
  - **Push early.** Open a draft pull request at your first commit and push every checkpoint after it, so nothing
    lives only on a workspace disk. Mark it ready when done.
  - **`tools/check.sh` before every push** (`python tools\check.py` on Windows). It runs every Python check CI runs
    (encounter catalog, command reference, say-gate ratchet, lore lints, `source/tools/tests`, the versions job, the
    changelog fragments) so fast checks don't fail only in CI. A check added to the workflow must be added to
    `tools/check.py` (`source/tools/tests/test_check.py` fails otherwise).
  - **Generated on main, not in pull requests** (owner decision 59A, #817): `docs/LOG-ROWS.md` and
    `tools/grafana/alloy/config.alloy`. A pull request that adds a log row doesn't commit them (CI fails one that does;
    `git checkout origin/main -- <file>` restores main's copy); the `generated-files` workflow regenerates them after
    each merge in the `bot/generated-files` pull request. `kit.py --write` still writes the dashboards, which you commit.
  - **Changelog fragments, not changelog edits.** Each pull request adds `changelog.d/<issue-or-branch>.md` (front
    matter `version` and `section`; [changelog.d/README.md](changelog.d/README.md)) and leaves
    `CHANGELOG-LIVING-SERVER.md` alone. `source/tools/assemble_changelog.py` folds them in at release.
  - **OPEN-WORK.md:** a pull request edits it only to add or remove its own line; anything more goes on the issue.
  - Fill in the pull request template (`.github/pull_request_template.md`): issue link, period sources, owner
    decisions, tests vs in-game, reruns owed.
- **Triage labels and milestone** (owner, 2026-11-25, #615). Every open issue of the release carries its milestone
  (`1.2.3` now) and the labels that fit:
  - `needs-owner`: waits on an owner decision; ask before building that part.
  - `needs-PC`: needs a check on the owner's PC (a log, a playtest) before it counts as working.
  - `tests-only`: built and green in tests, not yet seen in a PC log (the "tests only" of "Be honest in reports").
  - `blocked`: waits on something else (another issue, an upstream fix, a repo setting); say what in a comment.
  - `reruns-owed`: a simulation, sweep or bench must be run again on the current build before its numbers can be
    quoted (e.g. after a rule changed under it).
  - Labels change as the issue moves: drop `tests-only` once a PC log shows it working, drop `needs-owner` once answered.
- **Claim an issue before building it** (owner, 2026-11-25). Anyone outside the owner's own sessions claims the issue
  (assign yourself, or comment "taking this") before starting work. Before building, every agent checks for an
  existing claim, an open pull request or a branch for the issue ([docs/CONTRIBUTING.md](docs/CONTRIBUTING.md)).
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
- **Be honest in reports.** Report automated tests separately from real in-game checks. Every big
  boss is in 1.2's scope (owner, 2026-11-14: the Darkness Falls raid AI, Legion, Behemoth and the other level 70+
  encounters included); describe a raid plan as working only once seen on the owner's PC.
  - A feature counts as working only once it has been seen working in one of the owner's PC logs
    (owner, 2026-11-08). Until then the internal docs and issues say "tests only". Unit tests, CI, simulations and
    test-world bot runs are all tests.
  - Never give an earlier build's result as the result of the build being shipped. Say which build each
    result came from.
  - **Release notes are for players** (owner, 2026-11-22: "Your 1.2.1 notes focus way too much on what's not tested on
    the owner's PC. Don't do that again and stop referring to owner's PC."). Player-facing release notes
    (`docs/RELEASE-NOTES-*.md` below the line) and the GitHub release page say what changed for players, grouped,
    clearly and confidently, with known issues stated plainly. No testing-status framing ("tests only", "seen in
    game", simulation or win-rate numbers) and no references to the owner's PC. That tracking stays in the internal
    docs: OPEN-WORK, issue comments and review docs.
- **The update path is frozen; pre-releases and dev builds never reach players** (owner, 2026-11-26, #685: players
  must not keep having to download a new Setup or launcher). [docs/RELEASE-TRAIN.md](docs/RELEASE-TRAIN.md), first
  section, has the three tiers (dev builds, pre-releases, full releases), the rc and release checklists, and the frozen
  update contract with the code behind each item (repository path, API URLs, download prefix, asset names, digest
  format, how each channel picks a release, the test-build marker, apply-update.ps1's and INSTALL.cmd's interfaces,
  Setup's base-files URLs).
  - **Two-step rule** for changing any contract item: step 1 ships a full release that accepts the old and the new;
    step 2 switches only after that release has been out long enough (the owner decides when).
  - Dev builds and pre-releases install into a test folder, never over the main game, and a pre-release is never
    Latest. Only a game release may be Latest (anything else hides every update).
  - CI's `update-contract` job runs the newest released launcher's own update code against each build; a red one means
    players on that release would not get the build.
- **Keep the old releases.** v0.3–v0.36c stay available as legacy releases, and their branches
  and tags are not rewritten.

## Useful facts

- **Edition switch:**
  - The switch is `ServerProperties.Properties.ENABLE_SLUAGHBINDER`. It defaults to on, is read
    from the database and is honored by character creation (players only).
  - **Bots never play the custom class, in any edition** (owner, 2026-11-17: "the custom class, that should never be
    played by bots"). No path makes a bot of class 63: launcher batches, the server's bot generator, `/spawn` and
    `/raid` helpers, hired companions, `/bot` companions, Epic Special Forces, No Lifer buffbots
    (`DAoCLivingServer.Shared.BotCustomClassRule`), and the era class shares leave it out. A gamebot that already is one is
    kept with its items but never logged in (`BOT_CUSTOM_CLASS_RETIRED` at start-up; the launcher shows it as "Not
    played"). Players keep the class in the 0.37b edition. See [docs/ERA-BUILDS-1.2.md](docs/ERA-BUILDS-1.2.md).
  - The Sluaghbinder uses the client's Hibernian Mauler slot (class 62), and the novice is stored
    as Acolyte (16) until promotion to class 63.
- **Item model chain:** `objects.csv` model ID → `items.csv` row → `items/<nif>`.
  - Shields that carry an emblem take their base texture from a `pskins.csv` override.
  - `tools/pet-art/nif4_geom.py` reads the old 4.x item NIFs that pyffi can't.
- **Lore (1.2.1 on):** before editing anything under `source/server/GameServer/bots/autonomous/data/lore/`, read
  [docs/1.2.1/HEROES.md](docs/1.2.1/HEROES.md) and [docs/1.2.1/HANDOFF.md](docs/1.2.1/HANDOFF.md). The owner's rules
  (2026-11-20):
  - Never trim lore; elaborate it.
  - Each realm's heroes are its real folklore legends, told about as legends, never present-day characters or bots.
  - Arthur rides with his named knights in legend-age stories, and Midgard and Hibernia do the same with their own
    legend companies.
  - DAoC server names don't matter.
  - Present-day friars and captains may keep a name where it adds gravity, and the 12-mention cap still holds.
  - `tools/lore/lint_lore.py` and `tools/lore/name_counts.py` enforce the mechanical rules in CI.
  - No game terms and no real companies in any player-visible lore, fight advice included (owner, 2026-11-21;
    `tools/lore/fourth_wall.py`, strict in CI). Without dotnet, run `python3 tools/lore/schema_mirror.py` for the C# schema
    checks (`UT_LoreBible`).
    - What the ban covers (owner, 2026-11-25: "Yes allow the how to answers, my restriction was really around lore only
      and role play character bots, and fight advice when discussing lore related mechanics"): lore, role-play
      character bots, and fight advice about lore-related mechanics. Ordinary how-to answers in bot chat (for example
      `data/chat/game_guide.json`) may use game terms such as npc, server and the launcher's tab names.
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
