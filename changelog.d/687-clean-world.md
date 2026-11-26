---
version: 1.2.3
section: Tests
---
- **The navmesh set gets a clean world, made in CI** (#687; owner, 2026-11-26: option 1). A new workflow,
  `navmesh-publish.yml`, makes the navmesh set on a CI runner from a fresh Setup install (the base release files plus a
  green main build's package, its navmesh patches included), never from a PC: the installed meshes and Detour library,
  and the install's never-played world copied and cleaned by the release builder's own code
  (`tools/navmesh/clean_world.py`: `build_release_037.clean_world`, then its `verify_clean` and the server-declared
  saved-progress tables). The check fails closed, before packing, on the packed copy and in the navmesh job on every run;
  a planted row in any saved-progress table fails it. The manifest names the world (name, size, SHA-256), a dry run
  builds, checks and tests the set without publishing, and publishing (main only) makes a `navmesh-vN` pre-release that
  is never Latest and never replaced. The navmesh job puts the world beside the meshes, so the database classes run
  there too (`.github/navmesh-tests.txt`). Nothing changes in what players download.
