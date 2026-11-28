# CI: what each job gates

Issue #618 (owner, 2026-11-25: "make sure each CI or checker is properly written and designed"). Every step of a required
job is a gate: it fails when its check fails **and** when its input is missing or empty. A step that only reports lives
in a job whose name starts with `report`, and no `report` job is required.

## Required checks

The "main: green checks" ruleset requires these six checks before a pull request merges into
`main`: **`server`, `launcher`, `encounter-catalog`, `new-client`, `versions`** (jobs of `build-and-test.yml`) and
**`update-contract / released launcher takes this build`** (its called workflow, since 2026-11-26, #689). `build-and-test.yml` runs on every push
(main included), every pull request and by hand.

| Job | Runner | Gates (each fails the job) |
|---|---|---|
| `versions` | ubuntu | README, `ForkBuildInfo.BuildName`, the newest released changelog heading and `MainForm.DisplayVersion` name one release (`tools/release/check_versions.py`); no new player-visible "Offline DAoC", "OfflineDAoC" or "shadowofze" outside `tools/release/brand-allowlist.json` and the compatibility names (`tools/release/check_brand.py`, #641); no old internal project or namespace name outside the kept names and legacy paths of `docs/NAMING.md` (`check_brand.py --internal`, #645); the release scripts' tests (`tools/release/tests`); the installer seeding tests (`tools/installer/tests`). |
| `encounter-catalog` | ubuntu | `docs/encounters` matches the boss scripts (`source/tools/epic_encounter_catalog.py --check`); every `changelog.d/` fragment parses and names the version in development (`source/tools/assemble_changelog.py --check`); every test in `source/tools/tests` (the tools, `ALL SERVER COMMANDS.txt` up to date, `tools/check.py`'s mirror, the CI glue `ci_test_results.py` and the excluded-test list's format); the Grafana kit (`tools/grafana/kit.py --check`: the dashboards match the server code and name only real or pending rows, and `docs/LOG-ROWS.md` and the Alloy allowlist build; since #817 those two are generated on main, not compared) and its filter tests (`tools/grafana/tests`, synthetic lines, against the allowlist built from the checkout's code); on every branch but `main` and `bot/generated-files`, that it doesn't commit `docs/LOG-ROWS.md` or `tools/grafana/alloy/config.alloy` (`tools/grafana/generated_guard.py --strict`: `--base HEAD^1` on the pull request run's merge commit, `--base origin/main` on the push run, so both same-named checks carry it; `--strict` fails if git can't read the range, #817). |
| `new-client` | ubuntu | The 0.37c new client's released scripts plus `tools/fork-build/content-patches.json` compile in Godot 4.7.2 (pinned by SHA-512) against the released class cache, and issue #34's probe passes (`tools/newclient-compat/installed_check.py`). |
| `server` | windows | See below. |
| `launcher` | windows | Every launcher test (`DAoCLivingServer.Launcher.Tests`), and at least 400 of them ran (411 in 1.2.3). |

### The `server` job, step by step

1. **Bot voices and lore** (Python, before the build): the corpus is what its `.lines` build (`build_corpus.py --verify`),
   names chosen in code pass the corpus rules (`check_runtime_names.py`), `DEBATES.md` is current (`debates_doc.py
   --check`), the say-gate ratchet (`say_gate_ratchet.py`), the lore lint, repetition, reachability, continuity and depth
   checks, and the unit tests of `tools/bot-voices`, `tools/lore`, `tools/claude-version` and `tools/perf`.
2. **Build** the server solution.
3. **Test server**: the whole suite but `.github/ci-excluded-tests.txt` (below). The filter comes from
   `ci_test_results.py excluded-filter`, which fails on an entry without a reason and an issue link.
4. **Server test results, feature tests and the passport lock**: read from `TestResults/server.trx`, no second run. Fails
   when the file is missing, when fewer than **9000** tests ran (about 10 170 run in 1.2.3: a filter that drops most of
   the suite is not a pass), or when any failed (each one is an `::error` line with its message). Then the branch's
   feature classes (`REPORT_TEST_CLASSES`, default the bot guild, personality, voices, Living Server and XP classes) and
   `UT_PassportLock`: each class must have tests in the results and all must pass.
5. **The simulated chat transcripts exist**: the eight `chat-sim/faction-sim-*.chat.log` files the chat tests write.
   They go to the `chat-sim` artifact for the `report: chat audit` job.
6. **Evening transcripts ratchet**: `evenings.tsv` must exist; `evening_read.py --metrics` must run; the seven counts may
   not get worse than `tools/bot-voices/eval/evening_ratchet.json`.
7. **Chat question bench ratchet**: all six bench files must exist (replies, holdout, channels, follow-ups, moments,
   two-line); the bench report and the line scan must run (they report and exit 0 on what they find; a crash fails); the
   ratchet may not fall below `bench_ratchet.json`.
8. **Living Server tab tests**: the launcher builds, the tab's test classes pass, and every class of the filter has tests
   in the results (a renamed class can't drop out unseen).
9. **Character passport** and **progress transfer** tests: pass, and at least one test ran.
   Then the **source-world Setup tool** (`DAoCLivingServer.Setup`) builds: nothing else compiled it, so #725 went unseen.
   It has no tests of its own; `source/tools/tests/test_setup_period_manifest.py` pins its manifest rule. (Added under
   #618's rule for #725.)
10. **Package the server build** (fails on any missing file) and **test the installer scripts** on fake game folders
    (Windows PowerShell 5.1, `tools/fork-build/test-install-scripts.ps1`).

## Report jobs (not required)

| Job | What it reports | Why it is not a gate |
|---|---|---|
| `report: chat audit` | `source/tools/chat_audit.py`'s verdicts on the simulated transcripts (artifact `chat-audit`). | The C# tests that write the transcripts are the gate and assert their own numbers. Each `*-before` transcript is the old scheduling and fails the audit on purpose; the channels transcript has no repliers, so its "unanswered questions" verdict is not a defect. A missing transcript fails `server`; a crash of the audit fails this job (red, but not blocking). |

## Other workflows

| Workflow | Runs on | Gates | Notes from the #618 audit |
|---|---|---|---|
| `candidate-install` | called by build-and-test after `server` on pushes to main, `integration/1.2`, `release/*`, `ci/*`; by hand | Setup built from this commit's package; a fresh install, smoke and the class-off edition; updates from 1.1.4, 1.2 and 1.2.1 with saves kept. | Not a required check (30-45 runner minutes). Every test step exits with the script's code. The playtester report step only copies a file to the run page. |
| `update-contract` | called by build-and-test after `server` on every push and pull request; by hand with a run's package | The newest full release older than this build (v1.2.2 today) takes it as its update: that release's own launcher code (`LauncherUpdater.Check`, `PrepareFromRelease`, `StartHandoff`, loaded from its installed dll by `source/tools/DAoCLivingServer.UpdateContract`) against a fake GitHub serving the candidate release `tools/release/update_contract.py` builds as publish-release would; the game is then the candidate (server, libraries, launcher, world untouched); the released Setup's `--info` picks it; this commit's own launcher takes it under either repository path (`ReleaseFeed.AcceptedRepositories`, any case) and refuses lookalikes (`update_contract.py paths`, #689); the repository is exactly `ReleaseFeed.Repository` (`tools/release/test-update-contract.ps1`). | #685, [RELEASE-TRAIN.md](RELEASE-TRAIN.md) "The frozen update contract". Its own workflow, so the release workflows (`installer`, `release`, `publish-release`) are not edited; publish-release's `check-jobs` requires its job to have succeeded (#689). A required check of main's ruleset since 2026-11-26 (`update-contract / released launcher takes this build`; the owner's setting, #689). Runs on D: (#680). Planted breaks (repo path, asset name, digest format) fail it: run URLs in PR #688. |
| `generated-files` | pushes to main; by hand | `tools/grafana/kit.py --write --generated` on main. When `docs/LOG-ROWS.md` or `tools/grafana/alloy/config.alloy` changed, it merges main into `bot/generated-files` (never a force-push), commits the two files, pushes, opens that branch's pull request if none is open and starts build-and-test on it by hand. It fails if the branch differs from main in any other file. | #817 (owner decision 59A). Pull requests stop committing the two files, so two pull requests that each add a log row no longer conflict. It can't push to main: the "main: green checks" ruleset requires the checks on every commit and has no bypass, and a commit by the workflow's token starts no push run, which publish-release needs on main's tip. The merge train merges the bot pull request like any other; it touches only the two files. If "Allow GitHub Actions to create and approve pull requests" is off, the run warns with a compare link and the pull request is opened by hand. `candidate-install` is skipped on that branch's dispatched runs. |
| `playtester` | nightly, by hand, pull requests touching the playtester | `source/tools/tests`, the playtester's tests, the scenarios on the minimal worlds (each step `set -o pipefail`; the class-off and death steps also grep for their log lines). | The companion step's `grep ... \|\| true` only prints lines; its scenarios still gate. |
| `navmesh` | pushes to main; pull requests touching `tools/navmesh`, the navmesh test lists, pathfinding, `AutonomousZone*`, the listed test classes or the clean-world rules; by hand | `publish-script`: the navmesh set tool's tests and check, and the packing script in Windows PowerShell 5.1 on a fake install. `navmesh`: once a set is published, the set's hashes, its world checked clean, and the navmesh tests (below). | #684, #687. Not a required check: a cold cache or a slow download never holds up a merge. Skips with a notice while `tools/navmesh/manifest.json` is the placeholder. |
| `navmesh-publish` | by hand (`version`, `dry_run` on by default, `run_id`); pull requests touching it, `clean_world.py` or the packer, as a dry run | Makes the navmesh set from a fresh Setup install on a runner, checks its world clean (fail closed, twice), reads it back as the navmesh job does, runs the navmesh tests on it, and publishes `navmesh-vN` (main only, dry run off): a pre-release, never Latest, never replaced. | #687. The test step is a report: a failing test is a test to fix, not a reason to withhold the set. Below, "Making a set". |
| `wayback-fetch` | pushes and pull requests touching `tools/wayback-fetch` | Its tests in Windows PowerShell 5.1 and PowerShell 7, `-RequireE2E` (a missing Python fails). | Sound. |
| `upstream-watch` | weekly, by hand | Lists upstream commits and updates one issue per repo. | A report by design. `gh label create ... \|\| true` only tolerates an existing label. |
| `branch-cleanup` | by hand (mode `show` by default, or `APPLY`); also callable from another workflow | Its rules' tests (`tools/release/tests/test_branch_cleanup.py`), then `tools/release/branch_cleanup.py`. It classifies each branch as protected, open PR, recent (14 days), merged, replaced or never merged, and writes the list to the run page and the `branch-cleanup-<mode>` artifact. | #645; it replaced the 2026-10-18 one-off `cleanup-branches`. A dry run changes nothing. APPLY first tags each unmerged branch `archive/<branch>`, reads the tag back, then deletes the branch with a lease on its tip. It never deletes a tag. The owner approves the dry run's list before APPLY. A branch it could not archive or delete fails the run. |
| `installer` | pushes touching `tools/installer/**` and the launcher's update code; by hand; called by `release` | Setup builds; newest-release choice; the launcher's update path; real installs (e2e). | **Release workflow (#618 audit; fixed in #623).** "Pictures of every page" runs both scales, then `tools/installer/check_shots.py` fails the job on a non-zero exit code or a missing page picture, before Setup is uploaded (the pictures are uploaded either way). The `package_release` default is still `v1.0-beta`. **Edited under #618's rule for #680:** `e2e` works in `D:\lstest` (it had about 4 GB to spare on C:), as the `e2e-more` jobs, `update-contract` and `navmesh-publish` already did, and logs free space on every drive at the start and the end of its step (the end too when the tests fail, plus a notice on the run page); its tests and exit code are unchanged. `setup-newest` and `launcher-update` stay on C: (no game download). |
| `release` | a published release; by hand | Plans, runs `installer`, attaches Setup (real run) or shows what it would (dry run). | **Audited, not edited.** Sound: every check exits 1, and a dry run has a read-only token. |
| `publish-release` | by hand | Publishes from a build-and-test push run on main whose conclusion is `success`. | **Audited in #618; fixed in #623.** Besides the run's conclusion, "Every required job succeeded" (`publish_release.py check-jobs`) refuses the run unless `server`, `launcher`, `encounter-catalog`, `new-client` and `versions` each concluded `success` in its latest attempt, and (#689) unless the run has `update-contract`'s job (`update-contract / released launcher takes this build`: the jobs API names a called workflow's jobs `<caller> / <job>`) and every such job concluded `success` (`REQUIRED_CALLED`). |

## Excluded tests

`.github/ci-excluded-tests.txt` lists the server test classes CI leaves out, one per line, each with its reason and an
issue link:

```
UT_InstalledKeepRoutes  # read-only installed keep navigation: needs the installed game files, [Explicit] (#619)
```

- A whole class goes on the list only when all of it needs the installed game files (navmeshes, a played or release
  database, captured logs), and its fixture is `[Explicit]`. `source/tools/tests/test_ci_test_results.py` fails
  otherwise. Issue #619 tracks the list and how to run them on the owner's PC.
- **Running them** (on a PC with a playable folder and the .NET SDK; #619):
  ```
  python source/tools/run_installed_tests.py --list                     # each class, its reason, the variables it reads
  python source/tools/run_installed_tests.py --game D:\Games\LivingServer [--only UT_A UT_B] [--dry-run]
  ```
  It sets `OFFLINE_DAOC_NAV_ROOT` (`<game>/runtime/server`, read in place), `OFFLINE_DAOC_DB_PATH` and `OFFLINE_DAOC_DB`
  (a copy of the world made with SQLite's backup API, never the world itself) and `OFFLINE_DAOC_PROOF_OUT`; other
  variables a class reads pass through from the environment. It refuses to run while a server answers on port 10300,
  never starts one, and sums up `installed.trx`. The ones that need only the navmeshes also run in the navmesh job (#684,
  "The navmesh job" below), and since #687 so do the ones that need the navmeshes and the world database: the set
  carries a clean world. Only the input-driven probes, the report tools with no assertion and the ones that need
  captured lists stay on the PC.
- **A synthetic test never goes on the list whole-class.** Fix it. If it can't be fixed now, quarantine just it as
  `UT_Class.Method  # why (#issue)` with its own issue, and name it in the pull request. Never delete a test.
- #618 took six classes off the list. Each had installed-file tests marked `[Explicit]` beside synthetic ones that had
  never run in CI: `UT_Classic165SpawnRestoration`, `UT_DragonEncounterAdds`, `UT_DragonRallyRoute`,
  `UT_FrontierSupplyMerchants`, `UT_RealmEventRecords` (no installed test at all), `UT_RealmRaidRecruitment`.

## The navmesh job

Issues #684 and #687 (owner, 2026-11-26: the navmeshes are published as a versioned set outside Git, CI downloads it
once and caches it; option 1 of #687: a clean world database is published with it, a played database never).
`.github/workflows/navmesh.yml`, job `navmesh`, on Windows:

1. **Is a set published?** `navmesh_set.py status tools/navmesh/manifest.json`. The placeholder (`"placeholder": true`)
   means no set yet: a notice, every later step is skipped and the job is green. A damaged manifest fails. It also says
   whether the set has a world (`world=true`).
2. **Cache:** `actions/cache/restore` of `navmesh-ci/set`, keyed by the manifest's SHA-256. On a miss,
   `navmesh_set.py fetch` downloads each zip part from the `navmesh-vN` release (public assets, no token), checks its
   size and SHA-256, unpacks it, checks every file and saves the set to the cache at once (so a failing test doesn't cost
   the next run a download).
3. **Verify, every run** (a cache hit too): every file's size and SHA-256 against the manifest, and no file the manifest
   doesn't list (`navmesh/`, `lib/`, `world/`). A mismatch fails the job (`tools/navmesh/tests/test_navmesh_set.py`
   plants one in a part, in a file inside a good part, and in a restored set).
4. **The world is clean, every run** (#687): `clean_world.py verify navmesh-ci/set/world/opendaoc.sqlite3.db` must pass
   the release builder's own verify step before any test reads it (fail closed; below). The tests get a copy at
   `navmesh-ci/data/opendaoc.sqlite3.db`, which is `../data` beside the meshes, and in `OFFLINE_DAOC_DB_PATH`.
5. **Build** the server tests, copy `lib/Detour.dll` beside the test assembly (where the server has it, beside
   `GameServer.dll`), and **run** `.github/navmesh-tests.txt` by name (`navmesh_set.py filter`; the classes are
   `[Explicit]`, so only a filter naming them runs them). `OFFLINE_DAOC_NAV_ROOT` is the unpacked set (`navmesh/`,
   `lib/`) as a Windows path; `OFFLINE_DAOC_PROOF_OUT` a file under the runner's temp folder.
6. **Results:** `navmesh_set.py results` lists every test with its outcome, and whether the cache hit, on the run page.
   It fails when an entry has no result, or any test did anything but pass (a skipped test proves nothing).

The server job is unchanged: every class stays on `.github/ci-excluded-tests.txt`. `.github/navmesh-tests.txt` lists the
ones the navmesh job runs, in three groups: navmeshes only, navmeshes and the world, the world only.

**What the set is.** A fresh install's `runtime/server/navmesh` (`zoneNNN.nav`, `roads.json`, `seams.json`,
`pockets.json`), its `runtime/server/lib/Detour.dll` and (#687) its world database cleaned, as
`world/opendaoc.sqlite3.db`: zip parts of under 1.9 GB each (GitHub takes files under 2 GB), plus
`navmesh-manifest.json`: every file's path, size, SHA-256 and part, each part's size and SHA-256, the install's build
and commit, the date, and a `world` block naming the database (name, path, size, SHA-256, `clean: true` and the check
that passed it), which must agree with its file entry. `tools/navmesh/manifest.json` in Git is a copy of that manifest.
No played database, log, save or anything from a PC is in a set.

**The clean world** (`tools/navmesh/clean_world.py`; it reuses `source/tools/build_release_037.py` and
`progress-policy.json`, so there is one copy of the rules):
- `clean SOURCE --out NEW --require-fresh` copies the installed world with SQLite's online backup (the source opened
  read-only), refuses a source holding any account, character or gamebot (a world whose server never started holds
  none), runs `clean_world()` (every saved-progress and clear table emptied, the default keeps, doors and relics, the
  starting guilds only), keeps the install's edition switch, and runs the check below. A half-made file is deleted.
- `verify DB` fails closed (exit 1, an `::error`) unless `verify_clean()` passes (every table the release builder
  empties holds no row, no account, character or gamebot, only the starting guilds, the default keeps, doors and relics)
  and `verify_tables_empty()` passes for every saved-progress table the server declares (Crafting 2.0, hired companions,
  bots as people), each of which must also be in the policy. A missing, damaged or incomplete world fails it too.
  `tools/navmesh/tests/test_clean_world.py` plants a row in each of the policy's tables, a player guild, a guild rank and
  a captured keep: each one fails it.

**Making a set** (`.github/workflows/navmesh-publish.yml`, #687). Nothing to upload: the set is made on a runner.
1. A dry run first (the default; also every pull request that changes the workflow, `clean_world.py` or the packer):
   Actions, navmesh-publish, Run workflow, version `v1`.
2. The run installs the game with the real Setup on D: (`--silent --no-update --new`), from the public base files and
   the `Candidate-Setup` of the newest green build-and-test push run on main (or `run_id`), as a new player gets it, with
   `content-patches.json` applied. It counts and checks the meshes, cleans and checks the world, packs it all with
   `Publish-NavmeshSet.ps1 -WorldDatabase` (which takes the world only with clean_world.py's report naming its hash, and
   never one inside a game install), then reads the parts back as the navmesh job does: `fetch`, `verify`, `check
   --manifest` (the installer's navmesh patches are the set's meshes) and `clean_world.py verify` on the packed copy.
   It builds the server tests and runs `.github/navmesh-tests.txt` on that copy (a report).
3. To publish: the same from main with "Dry run" unticked. `navmesh_set.py publish` refuses an existing tag or release,
   checks every part, creates a draft pre-release on the run's commit, uploads the parts and the manifest, checks
   GitHub's SHA-256 of each against the manifest (a mismatch leaves the draft unpublished), and publishes it as a
   pre-release with `make_latest: false` (then checks it isn't Latest). It uses the workflow's own token, so the
   release doesn't start `release.yml`.
4. A pull request then copies the release's `navmesh-manifest.json` over `tools/navmesh/manifest.json`. From that commit
   on, the navmesh job runs the tests.

**Why CI and not the owner's PC** (#687): the owner's test install has played (its database is a played world, and the
server runs there), and the PC may have no Python to run the release builder's cleaning. A runner's fresh install has a
world that never ran, and its meshes are the ones every player gets (the base files plus `content-patches.json`). The
dry runs of #694 found 103 zone meshes (2.07 GB), the same 103 zones as the 1.2 download (`navmesh-drops.json`); the
owner's test install has 104, one more than players get. The whole set (with the 92 MB world) is 2.33 GB in 108 files,
one zip part of 653 MB; the run takes about 10 minutes (build 2, Setup 3, packing 2, tests 1).

**Which classes run.** Of the 29 candidates (the 3 navmesh-only classes of #684 and the 26 database classes with an
assertion and no input file), 19 pass on the fresh set and are on `.github/navmesh-tests.txt`. The other 10 fail on it
and stay PC tests until #704 settles each one (a real fix, or a PC-only reason; never a loosened assertion). Run
together, 11 classes first failed with "A resolver is already set for the assembly": each set its own Detour resolver.
`Tests/UnitTests/InstalledDetour.cs` now sets it once per test process. Still PC-only: the input-driven probes, the
report tools with no assertion, `UT_DungeonProofGenerator` and `UT_NavGapProbe` (proof generators, no assertion), and
`UT_KeepDoorNavmeshProbe` and `UT_RealmEventNavigation` (a captured door list, a captured ladder list).

**The PC fallback** (#684): `tools/navmesh/RUN-ME.cmd` still packs an install's meshes on a PC with nothing to install,
and opens GitHub's release form for a browser upload. It makes a navmesh-only set (no Python there for the clean
world), so it is the fallback only: with a navmesh-only set the database classes of `.github/navmesh-tests.txt` fail and
go back to the PC list.

**A new set** (a navmesh rebuild, #582): run navmesh-publish with `v2` and commit its manifest. A published version is
never replaced (the workflow refuses an existing tag or release, the script a version folder that holds files), so a
manifest in Git always names files that exist.

**Never a game update.** A `navmesh-vN` tag is not a build (`BuildVersion` reads only `v<major>.<minor>...`) and its
release carries no `DAoC-LivingServer-*.zip` (the world is inside a zip part), so the launcher's update check, its
test-build channel and Setup never offer it, even with "Offer test builds" on or if it were published as the latest
release by mistake: `DAoCLivingServer.Launcher.Tests/NavmeshReleaseTests.cs` and `tools/installer/test-installer.ps1`
("newest-navmesh-release").

**Releases.** Release packages carry no navmeshes. Players get them from the base files plus the byte patches of
`tools/fork-build/content-patches.json`. Once a set is published, `navmesh_set.py check` (in `tools/check.py`) fails when
a navmesh the installer patches is not, in the set, the patch's fixed SHA-256 and size. So the set and what ships agree.
The patches are made from the set (#687): `tools/fork-build/make_content_patch.py --navmesh-set DIR` takes each fixed
navmesh by its manifest path from a folder `navmesh_set.py fetch` unpacked (its `.navmesh-set.json` must be the manifest,
and the file is hashed again), with `set` for the fixed file; a navmesh given as a file from a PC folder is refused.
`source/tools/tests/test_make_content_patch.py` (encounter-catalog job, so on every push) pins the shipped navmesh
patches to `tools/navmesh/manifest.json`. A navmesh rebuild (#582) publishes `navmesh-vN+1`; its patches are made from
that set.

## Every checker fails on a planted defect

Each checker CI runs has a test that plants a defect and sees it fail, and sees the clean input pass:

| Checker | Self-test |
|---|---|
| `tools/check.py` (local mirror) | `source/tools/tests/test_check.py` (stale catalog; issue #615) |
| `source/tools/ci_test_results.py` | `source/tools/tests/test_ci_test_results.py` (missing, unreadable, failed, empty, vanished class, bad excluded list) |
| `tools/navmesh/navmesh_set.py` (fetch, verify, results, check) | `tools/navmesh/tests/test_navmesh_set.py` (a planted part, file and restored-set mismatch; damaged manifests; a failed, inconclusive or missing result; a stale installer patch); `source/tools/tests/test_make_content_patch.py` (a navmesh from a PC folder, an unfetched or other-manifest set, a mesh changed after the fetch, a stale size) |
| `tools/navmesh/Publish-NavmeshSet.ps1` | `tools/navmesh/tests/test-publish-script.ps1` (identical zips twice, read back by `navmesh_set.py`, a planted mismatch, the install untouched, each refusal) |
| `source/tools/run_installed_tests.py` | `source/tools/tests/test_run_installed_tests.py` (server running, not a game folder, unlisted class, no results; the world is copied, never used) |
| `source/tools/make_command_reference.py --check` | `source/tools/tests/test_checkers_fail.py` |
| `source/tools/epic_encounter_catalog.py --check` | `source/tools/tests/test_epic_encounter_catalog.py`, `test_check.py` |
| `source/tools/chat_audit.py` | `source/tools/tests/test_chat_audit*.py` |
| `tools/bot-voices/build_corpus.py --verify`, `check_runtime_names.py`, `debates_doc.py --check` | `tools/bot-voices/tests/test_checkers_fail.py` |
| `tools/bot-voices/say_gate_ratchet.py` | `tools/bot-voices/tests/test_session_transcript.py` |
| `evening_read.py --ratchet`, `score_bench.py ratchet` | `tools/bot-voices/tests/test_chat_bench.py` |
| `tools/lore/lint_lore.py`, `repetition.py`, `continuity.py`, `depth_report.py`, `fourth_wall.py` | `tools/lore/tests/` |
| `tools/lore/reachability.py --check` | `tools/lore/tests/test_reachability_check.py` |
| `tools/release/check_versions.py` | `tools/release/tests/test_check_versions.py` |
| `tools/grafana/kit.py --check` | `tools/grafana/tests/test_kit.py` (stale catalog and hand-edited Alloy config with `--generated`; without it a branch's lagging copies are only a note and `--write` leaves them alone (#817); stale or stray dashboard, unknown policy row, a dashboard row or field that doesn't exist or is dropped) |
| `tools/grafana/generated_guard.py` | `tools/grafana/tests/test_generated_guard.py` (a branch that commits either generated file fails; code-only branches, main's own regeneration and the branch merging main pass; the merge commit CI checks out, `--base HEAD^1`; `--strict` fails on a range git can't read; build-and-test.yml keeps both guard steps) |
| `tools/release/check_brand.py` (and `--internal`) | `tools/release/tests/test_check_brand.py` (planted launcher string, doc line and script message; planted namespace and workflow path) |
| `tools/fork-build/test-install-scripts.ps1`, `tools/wayback-fetch/tests/run-tests.ps1` | they are tests themselves; each exits 1 on a failed check |
| `tools/release/update_contract.py`, `DAoCLivingServer.UpdateContract` | `tools/release/tests/test_update_contract.py` (planted repo path, asset name and digest format each break the candidate release a released launcher reads; the clean one matches the contract; #689: the new launcher's mirror takes both paths in any case and refuses lookalikes, and `paths` lists every case) and the `update-contract` job's planted-failure runs (PR #688) |
| `tools/newclient-compat/installed_check.py` | none: it needs Godot and the 0.37 release commit (only the `new-client` job has them) |

## Proving a gate

CI on the pull request is the proof. Push a throwaway commit that plants the failure (a launcher test and a server
feature test that `Assert.Fail`), check the run goes red at the expected checks, then revert it in the next commit and
note both run URLs in the pull request.

Locally, `tools/check.sh` (`python tools\check.py` on Windows) runs every Python check of the workflow before a push. A
Python command added to `build-and-test.yml` or `navmesh.yml` goes into `tools/check.py`'s `CHECKS`, or into `NOT_LOCAL` with the reason
when it reads what the dotnet tests write (`ci_test_results.py`, `chat_audit.py`, the bench and evening ratchets);
`source/tools/tests/test_check.py` fails otherwise.
