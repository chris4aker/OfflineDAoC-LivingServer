---
version: 1.2.3
section: Tests
---
- **Released launchers are tested against every build** (#685): a new CI check (`update-contract`) takes the newest
  full release's own launcher and Setup (today 1.2.2) and checks they would take the build as their update: the
  launcher's own update check, download, digest, address and package checks and its own hand-over to INSTALL.cmd on a
  fake game it was installed on, then the released Setup's choice of the newest package. The frozen update contract,
  the three tiers (dev builds, pre-releases, full releases) and the two-step rule for changing the contract are in
  `docs/RELEASE-TRAIN.md`.
