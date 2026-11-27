---
version: 1.2.3
section: Tests
---
- **Navmesh patches come from the published navmesh set** (#687 part 2, step 1). `tools/fork-build/make_content_patch.py`
  makes a `runtime/server/navmesh/...` patch only from a set that `navmesh_set.py fetch` unpacked and checked
  (`--navmesh-set DIR`, fixed file `set`): the mesh is taken by its manifest path after checking the folder was fetched
  against that manifest and the file still has its SHA-256. A navmesh given as a PC file is refused.
  `navmesh_set.py check` also compares the patch's size with the set's, and
  `source/tools/tests/test_make_content_patch.py` (encounter-catalog job) pins the shipped zone180 and zone277 patches to
  navmesh-v1. Tools only, no game change.
