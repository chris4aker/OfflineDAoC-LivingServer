---
version: 1.2.3
section: Tests
---
- **A runner for the tests CI leaves out** (#619). `source/tools/run_installed_tests.py` runs the server test classes on
  `.github/ci-excluded-tests.txt` (they read the installed navmeshes and the world, which are never in Git) on a PC with
  a playable folder: `--list` shows each class's reason and the environment variables it reads; `--game <folder>` sets
  the navmesh root, a copy of the world (made with SQLite's backup API, so the tests never touch the world itself) and a
  proof folder, runs them with `dotnet test` and sums up the results. It refuses to run while a server answers on port
  10300 and never starts one.
