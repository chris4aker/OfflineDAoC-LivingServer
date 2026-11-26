---
version: 1.2.3
section: Tests
---
- **Release workflows fail on failure** (#623; from the CI audit, #618). The installer's screenshot step now fails
  the job when either screenshot run exits non-zero or a page picture is missing (`tools/installer/check_shots.py`),
  so a failing run can no longer still produce Setup; the pictures are uploaded either way. publish-release no longer
  trusts only the build run's conclusion: it refuses the run unless each required job (`server`, `launcher`,
  `encounter-catalog`, `new-client`, `versions`) concluded `success` (`publish_release.py check-jobs`). Triggers,
  secrets and the release flow are unchanged.
