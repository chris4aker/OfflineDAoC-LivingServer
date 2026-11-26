---
version: 1.2.3
section: Tools
---
- **Branch cleanup workflow** (#645). `branch-cleanup.yml` and `tools/release/branch_cleanup.py` replace the one-off
  2026-10-18 cleanup. Each branch is classed as protected, open PR, recent, merged, replaced or never merged, and the
  list goes to the run page.
  - A dry run is the default.
  - APPLY tags every unmerged branch `archive/<branch>` before deleting it, so nothing is lost. It never touches main,
    `integration/*`, the legacy `release/0.*` lines, the release lines, protected branches, branches with an open pull
    request, branches with a commit in the last 14 days, or any tag.
