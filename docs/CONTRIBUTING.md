# Contributing

For friends working on Living Server, usually through Claude on their own accounts. The project rules are in
[AGENTS.md](../AGENTS.md). Read it first. Claude Code loads it on its own (`CLAUDE.md` points to `AGENTS.md`).
Protecting player data and the other hard rules live there, not here.

## Access

- **Fork and pull request:** fork the repository, push to your fork, and open a pull request against `main`.
- **Collaborator:** the owner can invite you; then you can push branches here.

## Pick an issue, then claim it

An issue is free to take when it:
- is open;
- is in the `1.2.3` milestone or listed in [OPEN-WORK.md](OPEN-WORK.md) (the roadmap);
- has no assignee and no "taking this" comment;
- carries none of these labels:
  - `needs-owner`: waits on an owner decision. Nothing is built until he answers.
  - `needs-PC`: needs a check on the owner's PC (a log or a playtest), which only his sessions can do.
  - `blocked`: waits on something else (another issue, an upstream fix, a repository setting). The issue says what.

Two other labels don't stop you:
- `tests-only`: built and green in tests, not yet seen in one of the owner's PC logs.
- `reruns-owed`: a simulation, sweep or bench must be run again on the current build before its numbers count.

**Claim it before you start:** assign yourself, or comment "taking this" on the issue. Also check that nobody has an
open pull request or a branch for it. Then two sessions never build the same issue.

## While you work

- Open a **draft pull request at your first commit**, and push every checkpoint after it.
- Run **`tools/check.sh`** before every push (`python tools\check.py` on Windows). It runs the Python checks CI runs.
- Add a **changelog fragment**, `changelog.d/<issue-or-branch>.md` ([format](../changelog.d/README.md)). Never edit
  `CHANGELOG-LIVING-SERVER.md` directly.
- Fill in the **pull request template**: the issue, period sources, owner decisions needed, tests vs in-game, reruns
  owed.
- Building and testing: [DEVELOPMENT.md](DEVELOPMENT.md).
- Charting the log rows in Grafana (gcx, the local stack, the dashboards): [GRAFANA.md](GRAFANA.md); every row is in
  [LOG-ROWS.md](LOG-ROWS.md). A pull request that adds or changes a row runs `python3 tools/grafana/kit.py --write`.

## Merging

- `main` requires the green checks `server`, `launcher`, `encounter-catalog`, `new-client` and `versions`.
- Turn on GitHub auto-merge, or run `gh pr merge --auto`. It waits for every check to pass.
- **Only the owner merges into `main`**, unless he says otherwise.

## Tests only, until the owner's log shows it

Only sessions linked to the owner's PC can read its logs or play-test. Work from outside therefore stays "tests only"
(label `tests-only`) in the issues and internal docs until one of the owner's PC logs shows it working
([AGENTS.md](../AGENTS.md), "Be honest in reports").

## Test builds

A test build is a release candidate of the version in development, published as a GitHub **pre-release** with a tag
such as `v1.2.3-rc.1` (issue #637). It is never marked Latest, so players on normal updates never see it.

**Get one (testers):**
1. In the launcher, beside **Check for updates**, turn on **Offer test builds (pre-releases)**. It is off by default
   and stored in `runtime\updates\update-settings.json`.
2. Click **Check for updates**. The newest test build newer than yours shows as a **TEST BUILD**, and START SERVER
   reads GET TEST BUILD. It is downloaded and checked against GitHub's SHA-256 digest, the same as every update.
3. Choose **Install into a new test folder**. Close the server first: nothing starts while port 10300 answers.
   - The launcher asks for a folder name (default `<your folder>-test`). The folder must be new or empty and sits in the
     same parent folder as your install.
   - It copies the game files, but never your world database, `account.txt`, logs, backups, bot files, downloaded
     updates or package folders.
   - It makes a clean world from your world database, the same way the release builder does: every saved-progress
     table of `progress-policy.json` emptied, keeps, doors and relics back to their defaults, every Living Server switch
     at its release value. It then checks that no account, character, bot or saved row is left.
   - It installs the test build there with that folder's `INSTALL.cmd`. The launcher closes for the install and then
     opens the test folder's launcher (or yours again, if you said No).
   - Your own install, world and characters are only read. **Install here** updates your own install instead,
     backed up as usual (`ROLLBACK.cmd` undoes it).
4. The test folder has test builds on, so it is offered the next candidate (`-rc.2`) and then the final release.

**Publish one (owner or a maintainer):**
- `ForkBuildInfo.BuildName` on `main` must be the version in development (for example `1.2.3`, with
  `## 1.2.3 (in development)` in the changelog). The candidate's package is that build. The launcher remembers which
  candidate it installed (`runtime\updates\installed-test-build.txt`, valid only while `GameServer.dll` is that
  build's), so `-rc.2` and the final are still offered.
- Run **publish-release** with `tag` `v1.2.3-rc.<N>`, the green build-and-test push run on `main` as `run_id`, and
  `prerelease` on. Keep `dry_run` on first: it checks everything, prints the notes and publishes nothing. Then run it
  again with `dry_run` off.
- The release carries `DAoC-LivingServer-1.2.3-rc.<N>.zip`, and the same file as `OfflineDAoC-LivingServer-1.2.3-rc.<N>.zip`
  for 1.2.x launchers (#641), each with GitHub's digest, under short generated notes.
