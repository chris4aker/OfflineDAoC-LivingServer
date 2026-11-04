# Living Server Setup

One exe that gets a player from nothing to playing Offline DAoC with Living Server, or adds Living Server to the
Offline DAoC they already have.

| The player has | Setup does |
|---|---|
| Nothing | Downloads Offline DAoC 0.37b (or 0.37, optionally with the 0.37c new client) with shadowofze's own downloader, then installs Living Server |
| Offline DAoC 0.37 / 0.37b / 0.37c | Installs Living Server |
| The Social Expansion or an earlier Living Server on 0.37 | Upgrades it to this build (the package's installer recognises its own earlier builds) |
| Offline DAoC 0.36 / 0.36b / 0.36c (with or without the Social Expansion) | Puts the released server back if needed, updates to 0.37 with shadowofze's official updater (saves stay), installs Living Server |
| Offline DAoC 0.35 or older | Offers a new install and brings the account, characters, items, houses and bots over with the game's own progress importer |
| This Living Server build | Checks it (repairs anything missing), adds the new client, or removes Living Server |

It also turns on .NET Framework 3.5 (the original client needs it; Windows asks permission) and adds desktop and
Start menu shortcuts, both optional.

A first install of Living Server (a new game, or a game without it) turns every Living Server feature on at the
suggested settings: Setup runs the installed launcher's `OfflineDAoC.exe --apply-recommended-settings`, which saves
`SocialExpansionSettings.Recommended` through the Living Server tab's own save. It is a ticked option on the prepare
page; a game that already has Living Server keeps its own choices unless it is ticked.

## How it works

Setup doesn't reimplement anything that already has a tested script. It embeds two payloads and runs them:

- **shadowofze's `Get-OfflineDAoC.ps1`** (the repository root's copy, identical to his v0.37/b/c releases and pinned
  by SHA-256 in `build.py`). Every game file comes from his GitHub releases, checked part by part against his
  manifests. Its download cache goes to `Offline DAoC setup files\.downloads` beside the game folder, so a stopped
  install continues where it stopped.
- **the Living Server release package** (`BUILD-INFO.txt` names the build and the release it fits). Setup unpacks it
  into the game folder, as a player would, and runs its `install-server-build.ps1` / `rollback-server-build.ps1`, so
  every check stays: the version check, the refusal while the game runs, backups of every replaced file, the
  hash-verified copies, and `ROLLBACK.cmd`.

Setup only adds a progress view over them: it watches the download cache and the game folder for real byte counts,
reads the scripts' output for the current step, and turns refusals into plain choices ("Replace anyway", "Fresh copy
and bring my characters").

Stopping is offered only while parts download (they are kept). Once the game unpacks into its folder or an update
applies, Setup asks the player to let it finish.

## Build

```
python tools/installer/build.py                       # the published Beta 1.0 package
python tools/installer/build.py --release v1.0-beta   # any release tag
python tools/installer/build.py --package <zip>       # a package built by CI
```

Needs the .NET SDK (8 or later; it targets .NET Framework 4.7.2 through reference assemblies, so it builds on Windows
or Linux). Output: `tools/installer/dist/LivingServer-Setup-<build>.exe` (about 12 MB, the package included).

The art comes from the launcher's own pictures (`source/tools/OfflineDaoc.Launcher/Assets`) with "LIVING SERVER" set
in Cinzel (SIL Open Font License): `python tools/installer/art/make_art.py` rebuilds it.

## Command line (tests and support)

```
LivingServerSetup.exe --silent --new [--dest DIR] [--classic] [--new-client] [--import-from OLD_GAME]
LivingServerSetup.exe --silent --game DIR [--add-client] [--remove] [--force] [--fresh-copy]
     common: [--no-shortcuts] [--netfx] [--recommended-settings | --no-recommended-settings] [--local-assets DIR] [--no-update] [--log FILE]
LivingServerSetup.exe --scan [--game DIR] --log FILE       # what Setup finds, and how it reads each game
LivingServerSetup.exe --info [--no-update] [--release-json FILE] --log FILE   # which package it would install
LivingServerSetup.exe --screenshots DIR [--scale 1.5]      # pictures of every page with made-up data
```

Exit codes: 0 done, 1 failed, 2 stopped, 3 bad arguments or a game Setup can't handle.

## Tests

`.github/workflows/installer.yml` builds the exe on a Windows runner, saves pictures of every page at 100 % and
150 %, and runs `test-installer.ps1` on five more Windows machines with real downloads from shadowofze's releases:

- **install paths:** a new install, a second run (nothing to do), the refusal while the game runs, removal, reinstall,
  a hand-changed server (refused, then replaced with its backup kept), a 0.36b game with the 0.36 Social Expansion
  updated to 0.37 with Living Server
- **new client:** a full 0.37c install (about 33 GB) with Living Server in both launchers
- **stop and crashes:** Stop mid-download, Setup killed mid-download and mid-unpack, then a run that finishes (the
  half-unpacked folder is moved aside, not deleted)
- **old games:** a 0.35b game's progress brought into a new install; a hand-changed 0.36b game moved to a fresh copy
- **.NET 3.5** turned on from "Disabled with Payload Removed"
- **the game runs:** the installed server starts with `FORK_BUILD name=<build>` in its log, and a login like ENTER
  REALM works
- **the window:** `--autopilot` clicks the real pages from Begin to "The realm awaits", with pictures

Not covered: a real player's PC (antivirus, SmartScreen, odd folders), and playing the game.

`.github/workflows/release.yml` runs these same jobs for every published release, then attaches the tested exe and
puts its SHA-256 in the release notes ("Releasing Living Server" in [docs/DEVELOPMENT.md](../../docs/DEVELOPMENT.md)).

## Notes

- The exe isn't code-signed, so Windows SmartScreen shows "Windows protected your PC" the first time: More info, then
  Run anyway.
- **The newest package (Beta 1.1.2).** Before it reads your games, Setup asks GitHub's releases API for Living
  Server's newest release (`PackageUpdate.cs`, with the launcher's shared update code in
  `source/tools/OfflineDaoc.Launcher/Updates`). When that release's package is newer than the embedded one and made
  for the same Offline DAoC release, Setup downloads it, checks it against the SHA-256 digest GitHub publishes and
  the package rules (one `LivingServer-<build>` folder, `BUILD-INFO.txt` naming the tag's build, the scripts, the
  server's hash), and installs it. Anything else (offline, a rate limit, a bad or missing digest, a package for
  another release) keeps the embedded package; the progress page's first step names the build and says why.
  `--no-update` (or `--offline`) skips the question; the test runs pass it so they install the embedded package, and
  the `newest` scenario of `test-installer.ps1` tests the choice. A package for a newer Offline DAoC release still
  needs a newer Setup (`build.py --package`).
- Dark Age of Camelot belongs to Electronic Arts and Broadsword. This is a free, non-commercial fan project.
