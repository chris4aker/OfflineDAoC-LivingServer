# CI: what each job gates

Issue #618 (owner, 2026-11-25: "make sure each CI or checker is properly written and designed"). Every step of a required
job is a gate: it fails when its check fails **and** when its input is missing or empty. A step that only reports lives
in a job whose name starts with `report`, and no `report` job is required.

## Required checks

The "main: green checks" ruleset requires these five jobs of `build-and-test.yml` before a pull request merges into
`main`: **`server`, `launcher`, `encounter-catalog`, `new-client`, `versions`**. `build-and-test.yml` runs on every push
(main included), every pull request and by hand.

| Job | Runner | Gates (each fails the job) |
|---|---|---|
| `versions` | ubuntu | README, `ForkBuildInfo.BuildName`, the newest released changelog heading and `MainForm.DisplayVersion` name one release (`tools/release/check_versions.py`); no new player-visible "Offline DAoC", "OfflineDAoC" or "shadowofze" outside `tools/release/brand-allowlist.json` and the compatibility names (`tools/release/check_brand.py`, #641); no old internal project or namespace name outside the kept names and legacy paths of `docs/NAMING.md` (`check_brand.py --internal`, #645); the release scripts' tests (`tools/release/tests`); the installer seeding tests (`tools/installer/tests`). |
| `encounter-catalog` | ubuntu | `docs/encounters` matches the boss scripts (`source/tools/epic_encounter_catalog.py --check`); every `changelog.d/` fragment parses and names the version in development (`source/tools/assemble_changelog.py --check`); every test in `source/tools/tests` (the tools, `ALL SERVER COMMANDS.txt` up to date, `tools/check.py`'s mirror, the CI glue `ci_test_results.py` and the excluded-test list's format); the Grafana kit (`tools/grafana/kit.py --check`: `docs/LOG-ROWS.md`, the Alloy allowlist and the dashboards match the server code and name only real or pending rows) and its filter tests (`tools/grafana/tests`, synthetic lines). |
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
| `playtester` | nightly, by hand, pull requests touching the playtester | `source/tools/tests`, the playtester's tests, the scenarios on the minimal worlds (each step `set -o pipefail`; the class-off and death steps also grep for their log lines). | The companion step's `grep ... \|\| true` only prints lines; its scenarios still gate. |
| `navmesh` | pushes to main; pull requests touching `tools/navmesh`, the navmesh test lists, pathfinding, `AutonomousZone*` or the listed test classes; by hand | `publish-script`: the navmesh set tool's tests and check, and the owner's packing script in Windows PowerShell 5.1 on a fake install. `navmesh`: once a set is published, the set's hashes and the navmesh tests (below). | #684. Not a required check: a cold cache or a slow download never holds up a merge. Skips with a notice while `tools/navmesh/manifest.json` is the placeholder. |
| `wayback-fetch` | pushes and pull requests touching `tools/wayback-fetch` | Its tests in Windows PowerShell 5.1 and PowerShell 7, `-RequireE2E` (a missing Python fails). | Sound. |
| `upstream-watch` | weekly, by hand | Lists upstream commits and updates one issue per repo. | A report by design. `gh label create ... \|\| true` only tolerates an existing label. |
| `branch-cleanup` | by hand (mode `show` by default, or `APPLY`); also callable from another workflow | Its rules' tests (`tools/release/tests/test_branch_cleanup.py`), then `tools/release/branch_cleanup.py`. It classifies each branch as protected, open PR, recent (14 days), merged, replaced or never merged, and writes the list to the run page and the `branch-cleanup-<mode>` artifact. | #645; it replaced the 2026-10-18 one-off `cleanup-branches`. A dry run changes nothing. APPLY first tags each unmerged branch `archive/<branch>`, reads the tag back, then deletes the branch with a lease on its tip. It never deletes a tag. The owner approves the dry run's list before APPLY. A branch it could not archive or delete fails the run. |
| `installer` | pushes touching `tools/installer/**` and the launcher's update code; by hand; called by `release` | Setup builds; newest-release choice; the launcher's update path; real installs (e2e). | **Release workflow (#618 audit; fixed in #623).** "Pictures of every page" runs both scales, then `tools/installer/check_shots.py` fails the job on a non-zero exit code or a missing page picture, before Setup is uploaded (the pictures are uploaded either way). The `package_release` default is still `v1.0-beta`. |
| `release` | a published release; by hand | Plans, runs `installer`, attaches Setup (real run) or shows what it would (dry run). | **Audited, not edited.** Sound: every check exits 1, and a dry run has a read-only token. |
| `publish-release` | by hand | Publishes from a build-and-test push run on main whose conclusion is `success`. | **Audited in #618; fixed in #623.** Besides the run's conclusion, "Every required job succeeded" (`publish_release.py check-jobs`) refuses the run unless `server`, `launcher`, `encounter-catalog`, `new-client` and `versions` each concluded `success` in its latest attempt. |

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
  "The navmesh job" below); the rest have no schedule (#687 asks how the database ones could run in CI).
- **A synthetic test never goes on the list whole-class.** Fix it. If it can't be fixed now, quarantine just it as
  `UT_Class.Method  # why (#issue)` with its own issue, and name it in the pull request. Never delete a test.
- #618 took six classes off the list. Each had installed-file tests marked `[Explicit]` beside synthetic ones that had
  never run in CI: `UT_Classic165SpawnRestoration`, `UT_DragonEncounterAdds`, `UT_DragonRallyRoute`,
  `UT_FrontierSupplyMerchants`, `UT_RealmEventRecords` (no installed test at all), `UT_RealmRaidRecruitment`.

## The navmesh job

Issue #684 (owner, 2026-11-26: the navmeshes are published as a versioned set outside Git; CI downloads it once and
caches it). `.github/workflows/navmesh.yml`, job `navmesh`, on Windows:

1. **Is a set published?** `navmesh_set.py status tools/navmesh/manifest.json`. The placeholder (`"placeholder": true`)
   means no set yet: a notice, every later step is skipped and the job is green. A damaged manifest fails.
2. **Cache:** `actions/cache/restore`, keyed by the manifest's SHA-256. On a miss, `navmesh_set.py fetch` downloads each
   zip part from the `navmesh-vN` release (public assets, no token), checks its size and SHA-256, unpacks it, checks
   every file and saves the set to the cache at once (so a failing test doesn't cost the next run a download).
3. **Verify, every run** (a cache hit too): every file's size and SHA-256 against the manifest, and no file the manifest
   doesn't list. A mismatch fails the job (`tools/navmesh/tests/test_navmesh_set.py` plants one in a part, in a file
   inside a good part, and in a restored set).
4. **Build** the server tests, copy `lib/Detour.dll` beside the test assembly (where the server has it, beside
   `GameServer.dll`), and **run** `.github/navmesh-tests.txt` by name (`navmesh_set.py filter`; the classes are
   `[Explicit]`, so only a filter naming them runs them). `OFFLINE_DAOC_NAV_ROOT` is the unpacked set (`navmesh/`,
   `lib/`); `OFFLINE_DAOC_PROOF_OUT` a file under the runner's temp folder.
5. **Results:** `navmesh_set.py results` lists every test with its outcome, and whether the cache hit, on the run page.
   It fails when an entry has no result, or any test did anything but pass (a skipped test proves nothing).

The server job is unchanged: every class stays on `.github/ci-excluded-tests.txt`, whose reasons now say which need only
the navmeshes, which need the world database (#687) and which are input-driven probe tools or report tools with no
assertion.

**What the set is.** The install's `runtime/server/navmesh` (`zoneNNN.nav`, `roads.json`, `seams.json`, `pockets.json`)
and `runtime/server/lib/Detour.dll`, as zip parts of under 1.9 GB each (GitHub takes files under 2 GB), plus
`navmesh-manifest.json`: every file's path, size, SHA-256 and part, each part's size and SHA-256, the install's build and
commit and the date. `tools/navmesh/manifest.json` in Git is a copy of that manifest. No world database, log or save is
in a set.

**Making a set** (the owner, once per version, on the PC with the install; no git, gh or token):
1. Double-click `tools/navmesh/RUN-ME.cmd` (`-NavmeshFolder <game>\runtime\server\navmesh` for another install). It
   only reads the install and writes only to `C:\Games\OfflineDAoC\navmesh-upload\<version>`: the parts and the manifest,
   each part checked again file by file. The same files give the same zips.
2. It opens `https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/new?tag=navmesh-v1&target=main&prerelease=1`
   with the title and description filled in (GitHub's documented release-form query parameters). Drag the parts and the
   manifest in, keep "Set as a pre-release" ticked and "Set as the latest release" unticked, and publish.
3. A pull request then copies the release's `navmesh-manifest.json` over `tools/navmesh/manifest.json`. From that commit
   on, the navmesh job runs the tests.

Why the browser and not a script that uploads with a token: dragging three files is fewer steps than creating a
fine-grained token, there is no secret to type, keep or revoke, and nothing on the PC can create a release, so nothing
can publish a release as "latest" by mistake. Windows PowerShell 5.1 can also buffer a whole request body in memory, which
makes a 1.9 GB upload from a script fragile.

**A new set** (a navmesh rebuild, #582): run it with `-Version v2`, publish `navmesh-v2` and commit its manifest. A
published version is never replaced (the script refuses a version folder that holds files), so a manifest in Git always
names files that exist.

**Never a game update.** A `navmesh-vN` tag is not a build (`BuildVersion` reads only `v<major>.<minor>...`) and its
release carries no `DAoC-LivingServer-*.zip`, so the launcher's update check, its test-build channel and Setup never
offer it, even with "Offer test builds" on or if it were published as the latest release by mistake:
`DAoCLivingServer.Launcher.Tests/NavmeshReleaseTests.cs` and `tools/installer/test-installer.ps1`
("newest-navmesh-release").

**Releases.** Release packages carry no navmeshes. Players get them from the base files plus the byte patches of
`tools/fork-build/content-patches.json`. Once a set is published, `navmesh_set.py check` (in `tools/check.py`) fails when
a navmesh the installer patches is not, in the set, the patch's fixed SHA-256. So the set and what ships agree. Making
those patches from the set instead of a PC folder is #687.

## Every checker fails on a planted defect

Each checker CI runs has a test that plants a defect and sees it fail, and sees the clean input pass:

| Checker | Self-test |
|---|---|
| `tools/check.py` (local mirror) | `source/tools/tests/test_check.py` (stale catalog; issue #615) |
| `source/tools/ci_test_results.py` | `source/tools/tests/test_ci_test_results.py` (missing, unreadable, failed, empty, vanished class, bad excluded list) |
| `tools/navmesh/navmesh_set.py` (fetch, verify, results, check) | `tools/navmesh/tests/test_navmesh_set.py` (a planted part, file and restored-set mismatch; damaged manifests; a failed, inconclusive or missing result; a stale installer patch) |
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
| `tools/grafana/kit.py --check` | `tools/grafana/tests/test_kit.py` (stale catalog, hand-edited Alloy config, stray dashboard, unknown policy row, a dashboard row or field that doesn't exist or is dropped) |
| `tools/release/check_brand.py` (and `--internal`) | `tools/release/tests/test_check_brand.py` (planted launcher string, doc line and script message; planted namespace and workflow path) |
| `tools/fork-build/test-install-scripts.ps1`, `tools/wayback-fetch/tests/run-tests.ps1` | they are tests themselves; each exits 1 on a failed check |
| `tools/newclient-compat/installed_check.py` | none: it needs Godot and the 0.37 release commit (only the `new-client` job has them) |

## Proving a gate

CI on the pull request is the proof. Push a throwaway commit that plants the failure (a launcher test and a server
feature test that `Assert.Fail`), check the run goes red at the expected checks, then revert it in the next commit and
note both run URLs in the pull request.

Locally, `tools/check.sh` (`python tools\check.py` on Windows) runs every Python check of the workflow before a push. A
Python command added to `build-and-test.yml` or `navmesh.yml` goes into `tools/check.py`'s `CHECKS`, or into `NOT_LOCAL` with the reason
when it reads what the dotnet tests write (`ci_test_results.py`, `chat_audit.py`, the bench and evening ratchets);
`source/tools/tests/test_check.py` fails otherwise.
