---
version: 1.2.3
section: Tests
---
- **The navmesh tests can run in CI** (#684, follows #619; owner, 2026-11-26: the navmeshes are published as a versioned
  set outside Git, downloaded once and cached). `tools/navmesh/Publish-NavmeshSet.ps1` (run by `RUN-ME.cmd`) packs an
  install's `runtime/server/navmesh` and `lib/Detour.dll` into hash-listed zip parts for a `navmesh-vN` pre-release; the
  new `navmesh` workflow downloads them, checks every SHA-256 against `tools/navmesh/manifest.json`, caches the set and
  runs the navmesh-only tests of `.github/navmesh-tests.txt`. Until a set is published the workflow skips with a notice.
  The launcher and Setup never offer a navmesh release as a game update (pinned by tests).
