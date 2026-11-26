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
| `encounter-catalog` | ubuntu | `docs/encounters` matches the boss scripts (`source/tools/epic_encounter_catalog.py --check`); every `changelog.d/` fragment parses and names the version in development (`source/tools/assemble_changelog.py --check`); every test in `source/tools/tests` (the tools, `ALL SERVER COMMANDS.txt` up to date, `tools/check.py`'s mirror, the CI glue `ci_test_results.py` and the excluded-test list's format). |
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
| `wayback-fetch` | pushes and pull requests touching `tools/wayback-fetch` | Its tests in Windows PowerShell 5.1 and PowerShell 7, `-RequireE2E` (a missing Python fails). | Sound. |
| `upstream-watch` | weekly, by hand | Lists upstream commits and updates one issue per repo. | A report by design. `gh label create ... \|\| true` only tolerates an existing label. |
| `branch-cleanup` | by hand (mode `show` by default, or `APPLY`); also callable from another workflow | Its rules' tests (`tools/release/tests/test_branch_cleanup.py`), then `tools/release/branch_cleanup.py`. It classifies each branch as protected, open PR, recent (14 days), merged, replaced or never merged, and writes the list to the run page and the `branch-cleanup-<mode>` artifact. | #645; it replaced the 2026-10-18 one-off `cleanup-branches`. A dry run changes nothing. APPLY first tags each unmerged branch `archive/<branch>`, reads the tag back, then deletes the branch with a lease on its tip. It never deletes a tag. The owner approves the dry run's list before APPLY. A branch it could not archive or delete fails the run. |
| `installer` | pushes touching `tools/installer/**` and the launcher's update code; by hand; called by `release` | Setup builds; newest-release choice; the launcher's update path; real installs (e2e). | **Release workflow: audited, not edited (#618 rule).** Soft spot: "Pictures of every page" prints each screenshot run's exit code but never fails on a non-zero one. The `package_release` default is still `v1.0-beta`. |
| `release` | a published release; by hand | Plans, runs `installer`, attaches Setup (real run) or shows what it would (dry run). | **Audited, not edited.** Sound: every check exits 1, and a dry run has a read-only token. |
| `publish-release` | by hand | Publishes from a build-and-test push run on main whose conclusion is `success`. | **Audited, not edited.** It trusts the run's conclusion: before #618 a run with failing launcher tests still concluded `success` (continue-on-error), so this gate is only now as strong as it looks. |

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
  never starts one, and sums up `installed.trx`. No schedule runs them yet: that needs a self-hosted runner on the
  owner's PC or fixture meshes in Git, both the owner's call (#619).
- **A synthetic test never goes on the list whole-class.** Fix it. If it can't be fixed now, quarantine just it as
  `UT_Class.Method  # why (#issue)` with its own issue, and name it in the pull request. Never delete a test.
- #618 took six classes off the list. Each had installed-file tests marked `[Explicit]` beside synthetic ones that had
  never run in CI: `UT_Classic165SpawnRestoration`, `UT_DragonEncounterAdds`, `UT_DragonRallyRoute`,
  `UT_FrontierSupplyMerchants`, `UT_RealmEventRecords` (no installed test at all), `UT_RealmRaidRecruitment`.

## Every checker fails on a planted defect

Each checker CI runs has a test that plants a defect and sees it fail, and sees the clean input pass:

| Checker | Self-test |
|---|---|
| `tools/check.py` (local mirror) | `source/tools/tests/test_check.py` (stale catalog; issue #615) |
| `source/tools/ci_test_results.py` | `source/tools/tests/test_ci_test_results.py` (missing, unreadable, failed, empty, vanished class, bad excluded list) |
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
| `tools/release/check_brand.py` (and `--internal`) | `tools/release/tests/test_check_brand.py` (planted launcher string, doc line and script message; planted namespace and workflow path) |
| `tools/fork-build/test-install-scripts.ps1`, `tools/wayback-fetch/tests/run-tests.ps1` | they are tests themselves; each exits 1 on a failed check |
| `tools/newclient-compat/installed_check.py` | none: it needs Godot and the 0.37 release commit (only the `new-client` job has them) |

## Proving a gate

CI on the pull request is the proof. Push a throwaway commit that plants the failure (a launcher test and a server
feature test that `Assert.Fail`), check the run goes red at the expected checks, then revert it in the next commit and
note both run URLs in the pull request.

Locally, `tools/check.sh` (`python tools\check.py` on Windows) runs every Python check of the workflow before a push. A
Python command added to `build-and-test.yml` goes into `tools/check.py`'s `CHECKS`, or into `NOT_LOCAL` with the reason
when it reads what the dotnet tests write (`ci_test_results.py`, `chat_audit.py`, the bench and evening ratchets);
`source/tools/tests/test_check.py` fails otherwise.
