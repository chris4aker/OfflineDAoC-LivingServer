# The 1.2.1 install checks: what CI covered, the results, and what still needs the owner's PC

1.2.1 ships one edition, as 1.2 did: the classic classes on the original client, one Setup exe and one zip (owner rule,
AGENTS.md). It has the same three install checks as 1.2 ([INSTALL-CHECKS-1.2.md](INSTALL-CHECKS-1.2.md)), with one change:
the update now starts from **1.2**, the release players have, as well as from Beta 1.1.4:
1. **an update over 1.2** (and over 1.1.4) through Setup, with characters and settings kept;
2. **a new install** of 1.2.1, as a player gets it from Living Server's downloads;
3. **class-off**: the new install is the 0.37 edition, and the custom class is refused on it.

Plan step 6 ([1.2.1/RELEASE-PLAN-1.2.1.md](1.2.1/RELEASE-PLAN-1.2.1.md)) also asks for `release.yml` as a dry run with
the tag `v1.2.1`.

All of this is CI on GitHub's Windows and Linux runners, so all of it is **tests only**. None of these checks has been
run on the owner's PC.

## What each check covered

| Check | CI job | What it checks |
|---|---|---|
| 1, from 1.2 | `candidate-install / update12` (new in 1.2.1) | 1.2's published Setup (`LivingServer-Setup-1.2.exe`, release `v1.2`) installs a new 1.2 game, from the project's copy of the 0.37 files. `seed_progress.py` seeds a level-20 character with coin, realm points and a backpack item, one gamebot per realm, six changed Living Server settings, the population target and a launcher option. Setup's scan must say `LivingServer=OtherBuild (1.2)`. The 1.2.1 Setup updates the game without the suggested settings. Every seeded row must be unchanged, and so must `game.dll`. The updated server then starts (`smoke_release_037.py`), logs `FORK_BUILD name=1.2.1` and takes a login like ENTER REALM. The seed is then checked again on the world that server ran on |
| 1, from 1.1.4 | `candidate-install / update114` | The same, from Beta 1.1.4's published Setup |
| 2 | `candidate-install / fresh` | The 1.2.1 package, zipped as a release zips it, goes into a Setup exe. That Setup makes a new install from the project's copy of the 0.37 release files, then runs its own paths: scan, run again, refuse while running, remove, reinstall, a hand-changed server, `--force`. The installed server starts with `FORK_BUILD name=1.2.1`, and a login works |
| 3 | `candidate-install / fresh` (`classoff`) | The new install is the 0.37 edition: `enable_sluaghbinder` is `False`, `game.dll` is the 0.37 client, and the world has no class 16, 62 or 63 character or gamebot. On a playtester copy of that world, the installed server refuses classes 62, 63 and 16 and makes a Celt Naturalist. `login-save-restart` keeps a character over a restart, and `FORK_BUILD` ends `sluaghbinder=off` |
| Release | `release` (dry run, `package_run_id`) | Setup is built on Windows from the 1.2.1 package of a green build-and-test run, since the `v1.2.1` release doesn't exist yet. The installer workflow's tests run on it: pictures of every page, which package Setup installs, the launcher update path, and the real-download end-to-end runs (`e2e`: new install, re-run, remove, reinstall, forced replace, the 0.36 update; `e2e-resume`, `e2e-freshcopy`, `e2e-ui`). Then publish's dry run prints what it would upload and how the notes' Setup row would change. Nothing is uploaded or edited: the token is read-only, and the publish job is skipped |
| Death and rez | `playtester` (by hand) | Plan step 2, which is not an install check: `bot-death`, `bot-corpse-target` and `bot-rez` against a real server with a scripted original-client player |

## Results

Each result names its commit. The branch is `release/1.2.1-dryrun` (PR #435).

**Commit `a1ba4da`**: the final dry run. This is fix/1.2.1 at `d1e4259` (the one-edition rule in AGENTS.md); its code
is the same as `71679d8`.
- build-and-test, push: [run 45074754222](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45074754222). Success, with the server suite at 9,294 passed, 0 failed.
  - `versions` passed, and so did `candidate-install / setup`, `fresh` (with `smoke` and `classoff`), `update114` and `update12`.
- release dry run, tag `v1.2.1`, `e2e` on, package from run 45074754222:
  [run 45077660421](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45077660421). Success.
  - Every installer job passed, and so did `dry-run`; `publish` was skipped.
  - It built `LivingServer-Setup-1.2.1.exe` (SHA-256 `00464a9d…`) around package `a685e51e…`, and said it "would upload"
    the exe and fill in the Setup row.
  - The notes passed publish-release's placeholder check.

**Commit `71679d8`** (fix/1.2.1 at `0c98501`; an earlier tip):
- build-and-test, push: [run 45068138261](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45068138261). Success, with the server suite at 9,294 passed, 0 failed.
  - `versions` passed, and so did `candidate-install / setup`, `fresh` (with `smoke` and `classoff`), `update114` and `update12`.
- release dry run, tag `v1.2.1`, `e2e` on, package from run 45068138261:
  [run 45071075809](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45071075809). Success.
  - Its first attempt was cancelled by a GitHub Actions internal error ("GitHub Actions has encountered an internal
    error when running your job"). The failed jobs were re-run (attempt 2), and every job passed: `plan`, `build`,
    `setup-newest`, `launcher-update`, `e2e`, `e2e-resume`, `e2e-freshcopy`, `e2e-ui` and `dry-run`. `publish` was skipped.
  - The notes passed publish-release's placeholder check (no markers left).

**Commit `8a6d55b`** (fix/1.2.1 up to #431 and #433 merged; an earlier tip):
- build-and-test, push: [run 45053310595](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45053310595). Success, with the server suite at 9,263 passed, 0 failed.
  - `versions` passed (`check_versions.py`: 1.2.1, tag v1.2.1).
  - `candidate-install / setup`, `fresh` (with `smoke` and `classoff`), `update114` and `update12` all passed.
- release dry run, tag `v1.2.1`, `e2e` on, package from run 45053310595:
  [run 45055983980](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45055983980). Success.
  - Every installer job passed (`build`, `setup-newest`, `launcher-update`, `e2e`, `e2e-resume`, `e2e-freshcopy`,
    `e2e-ui`), and so did `dry-run`; `publish` was skipped.
  - The notes carried the `@@RAID_RESULTS@@` marker, which still needs filling in: the dry run warns about it, and
    publish-release will refuse the notes until it is filled.

**Commit `cf37c8a`** (the first commit of the branch: the version changes and the new checks):
- build-and-test, push: [run 45049269827](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45049269827). Success.
  - `update12` passed all its checks: it installed 1.2 (`BuildName` 1.2), seeded, was recognised as `OtherBuild (1.2)`, updated to 1.2.1 and kept everything, and the server started with `FORK_BUILD name=1.2.1` and took a login.
  - `update114`, `fresh` and `classoff` all passed every check ("All checks passed").
- release dry run, tag `v1.2.1`, `e2e` on:
  [run 45052131340](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45052131340). Success.
  - It built `LivingServer-Setup-1.2.1.exe` (SHA-256 `edd6f58c…`) around package `5fac183d…`.
  - The installer jobs all passed (`e2e`, `e2e-resume`, `e2e-freshcopy`, `e2e-ui`, `setup-newest`, `launcher-update`).
  - The dry run said it "would upload" the exe and fill in the Setup row; `publish` was skipped.
  - An earlier attempt, [run 45051147102](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45051147102), stopped on purpose: the package's build-and-test run had not finished yet.
- playtester, by hand: [run 45052178254](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45052178254). Success: `bot-death`, `bot-corpse-target` and `bot-rez` passed, along with the class-switch and login scenarios.
- installer, push: [run 45049269920](https://github.com/chris4aker/OfflineDAoC-LivingServer/actions/runs/45049269920). Success.

The packages these runs built have the branch's name in BUILD-INFO (`Branch=release/1.2.1-dryrun`). The release itself
is published from a push run on `main` (publish-release checks this), so its Setup and zip are new builds. These
results are for the commits named above, not for that build.

## What still needs the owner's PC

The rows close only on the owner's PC, on the release build, with the build named. The steps are those of
[INSTALL-CHECKS-1.2.md](INSTALL-CHECKS-1.2.md) with 1.2.1 in place of 1.2:
- **Update over 1.2.** Use a 1.2 test copy (never `playable-v0.37b`). Run `LivingServer-Setup-1.2.1.exe` with the
  suggested settings unticked, then confirm:
  - `seed_progress.py check` prints `KEPT everything seeded`;
  - the Living Server tab, the roster and your character are as you left them;
  - the server log has `FORK_BUILD name=1.2.1 commit=<release commit>`;
  - the bot schedule moved to off only if it was 1.2's untouched default;
  - the launcher's own GET UPDATE also offers 1.2.1 on a 1.2 game (CI tests this without the window).
- **New install.** Run Setup on a new folder and ENTER REALM. Antivirus, SmartScreen and the launcher window have not
  been checked by CI.
- **Class-off.** Play one session on a 0.37 world: the Mauler slot is refused (`CUSTOM_CLASS_REFUSED ... class=62`),
  there are no `ERR` lines naming the custom class, and a raid starts.
- **The death fall and the rez in the original client.** CI checks the packets only (#425). What the old client draws
  is seen only on the PC.

## Publishing (not done here)

1. Merge this PR into `fix/1.2.1`, then `integration/1.2.1` into `main` (plan step 7).
2. On `main`:
   - replace `[RELEASE DATE]` in `CHANGELOG-LIVING-SERVER.md` with the date;
   - fill `@@RAID_RESULTS@@` in `docs/RELEASE-NOTES-1.2.1.md`;
   - wait for build-and-test's push run on `main` to pass.
3. The owner, or someone with the owner's go-ahead, runs `publish-release`:
   - first as a dry run: tag `v1.2.1`, `run_id` = that main run, a title, dry run on;
   - then for real. It creates the tag and the release and attaches the zip.
4. Run `release` by hand with tag `v1.2.1` and dry run off. A release published by the workflow's token does not start it
   by itself. It builds and tests Setup, attaches it and fills in the Setup row.
