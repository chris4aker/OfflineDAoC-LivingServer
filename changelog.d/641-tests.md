---
version: 1.2.3
section: Tests
---
- **Brand check** (#641). `tools/release/check_brand.py` runs in the versions job and in `tools/check.py`. It fails on
  a new player-visible "Offline DAoC", "OfflineDAoC" or "shadowofze" in the launcher's, Setup's and the server's string
  literals, the install scripts' messages and the player docs. Credits, records of earlier releases and compatibility
  names (old file names, the repositories, the base-game files) are allowed, each with its reason, in
  `tools/release/brand-allowlist.json` (`test_check_brand.py` plants a defect in each kind of file).
