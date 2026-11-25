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

## Merging

- `main` requires the green checks `server`, `launcher`, `encounter-catalog`, `new-client` and `versions`.
- Turn on GitHub auto-merge, or run `gh pr merge --auto`. It waits for every check to pass.
- **Only the owner merges into `main`**, unless he says otherwise.

## Tests only, until the owner's log shows it

Only sessions linked to the owner's PC can read its logs or play-test. Work from outside therefore stays "tests only"
(label `tests-only`) in the issues and internal docs until one of the owner's PC logs shows it working
([AGENTS.md](../AGENTS.md), "Be honest in reports").
