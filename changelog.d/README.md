# Changelog fragments

Each pull request adds one file here instead of editing `CHANGELOG-LIVING-SERVER.md` (issue #615), so pull requests no
longer conflict on the changelog. `source/tools/assemble_changelog.py` folds the fragments into the changelog at release
and deletes them; CI (`encounter-catalog` job) and `tools/check.sh` validate them on every push.

## Format

File name: `changelog.d/<issue-or-branch>.md`, e.g. `615.md` or `fix-fomor-ladders.md` (letters, digits, `.`, `_`, `-`).
One file per pull request; a second entry for another section goes in a second file (`615-tools.md`).

```markdown
---
version: 1.2.3
section: Bosses
---
- **Fomor City has a raid** (#414; owner, 2026-11-24: "Start it now"). ...
```

- `version`: the version in development, as in its changelog heading (`## 1.2.3 (in development)`). A released
  version is refused.
- `section`: the bold section line of that version's block, without the `**` (`Bosses`, `Chat`, `RvR`, `Tests`, ...). A
  section the block doesn't have yet is added at the end of the block.
- Below the front matter: the changelog text as it should read, usually `- ` bullets in the changelog's own style. No
  `#` headings.
- Only `version` and `section` are allowed in the front matter.

## Order

Stable and predictable: within a section the fragments go in by file name, after the entries the section already holds.
New sections are added in the order of their first fragment's file name.

## Commands

```bash
python3 source/tools/assemble_changelog.py --check    # validate (CI)
python3 source/tools/assemble_changelog.py --dry-run  # print the changelog as it would be written
python3 source/tools/assemble_changelog.py            # fold the fragments in and delete them (at release)
```
