# Release baselines (for patches)

0.36 / 0.36b / 0.36c are the full downloads. Later versions (0.37 / 0.37b / 0.37c and on) ship as **patches** that
bring an unmodified 0.36-family folder up to date, so nobody has to download the whole game again.

Each folder here records exactly what a full release shipped:

| File | What it is |
|---|---|
| `baseline.json` | The release's repository commit, server source commit, edition file hashes and key binaries |
| `0.36b-package.sha256` | Every file of the game package (0.36b; 0.36 is the same plus its two edition files) |
| `0.36c-new-client.sha256` | Every file of the new client package (`new-client\` and its two start files) |
| `v0.36*-download-manifest.json` | The published download manifests |

The released clean world databases (`world-0.36b.sqlite3.db`, `world-0.36.sqlite3.db`) are kept outside Git, in
`release-baselines/0.36/` next to this repository's folder.

## Finding what changed

`source/tools/find_changes_since_release.py --baseline 0.36` reads (never changes) the development install, the new
client's development folder and this repository, and lists everything that differs from the release: server
commits, shipped runtime files, new client files (and whether its C# must be rebuilt), repository commits, and world
database tables whose rows changed.

## How a patch will work

- **Files:** only changed and new files, plus a list of removed ones. Before replacing a file, the patcher checks
  that it still has its released fingerprint; a file the player changed stops the patch (with an option to
  overwrite). Everything replaced is backed up first, so a patch can be undone.
- **World database:** never replaced (it holds the player's saves). Database fixes ship as update scripts that change
  only world rows, each recorded so it runs once, with a backup first.
- **Cumulative:** each patch goes from the 0.36 base straight to the newest version.
- **Editions:** one patch for the game (0.36 and 0.36b, told apart by `EDITION.txt`) and one for the new client.
- Patches are made only when the owner says it is time for the next version.

## 0.37 (the first patch)

`0.37/` records what the published 0.37 update brings an install to: `0.37b-package.sha256` (the game, both editions;
0.37 differs only in its edition files), `0.37c-new-client.sha256` (the new client), the published game download
manifest and the patch report. The next patch stays cumulative from 0.36 and also accepts installs of 0.37:
`make_patch.py --also-from release-baselines/0.37` adds 0.37's fingerprints to every file's accepted starting points.

World updates are field updates where the table has a primary key: only the fields a version changes are written,
and only where the player's row still has the released value of those fields. The running server rewrites some
monster fields itself (size, level and stats are re-rolled, and NPCs whose template lists several models pick one at
spawn), so a whole-row match would wrongly leave real fixes in such rows undone.
