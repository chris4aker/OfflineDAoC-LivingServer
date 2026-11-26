---
version: 1.2.3
section: Tests
---
- **Navmesh tests run in CI** (#684, #687). The `navmesh-v1` set (103 zones, the route data, the pathing library and a
  clean world, made in CI from a fresh install) is published as a pre-release, and its manifest is now in
  `tools/navmesh/manifest.json`, so the navmesh job runs its 26 tests on every push.
