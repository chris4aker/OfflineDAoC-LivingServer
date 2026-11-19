# The 1.2 Install rows: what CI tests, and the steps on the owner's PC

The three Install rows of the 1.2 gate ([PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), "Install") that this page is for:
1. a 1.1.4 game updated to the 1.2 candidate through Setup, characters and settings kept;
2. a new install of the 1.2 candidate, as a player gets it from Living Server's downloads;
3. a class-off smoke session on a copy of a 0.37 (class off) world (owner decision 8).

CI runs most of each as **tests**. A test never closes a row: a row is "Seen" only on the owner's PC, on the candidate
build, with the build named. The steps below are written so each row closes in one sitting.

## What CI tests (tests only)

| Row | CI job | What it checks |
|---|---|---|
| 1 | `candidate-install / update114` | Living Server 1.1.4's published Setup (`LivingServer-Setup-1.1.4-beta.exe`) installs a new 1.1.4 game. `tools/installer/seed_progress.py` seeds a level-20 character with coin, realm points and an item in its backpack, one gamebot per realm, six changed Living Server settings, the bot population target and a launcher option. The candidate's Setup updates the game. Every seeded row must be unchanged, and so must `runtime\account.txt`, `bot-goals.json` and `game.dll`. Setup must not apply the suggested settings over the player's own. The updated server then starts once (`smoke_release_037.py`), and the seed is checked again on the world it ran on |
| 2 | `candidate-install / fresh` | The candidate's package zipped as a release zips it, put in a Setup exe, then a new install from Living Server's copy of shadowofze's 0.37 release files. Then Setup's own paths (scan, run again, refuse while running, remove, reinstall, a hand-changed server, `--force`). The installed server starts with `FORK_BUILD name=1.2` and a login like ENTER REALM works |
| 3 | `candidate-install / fresh` (`classoff`) | The new install is the 0.37 edition: `classes / enable_sluaghbinder` is `False`, `game.dll` is the 0.37 client (SHA-256 `f55ed6b0…`, no Sluaghbinder patches), and no character or gamebot of class 16, 62 or 63 is in the world. On a playtester copy of that world (`make_test_world.py from-release`, the switch kept), the **installed** server runs the playtester's `class-switch` scenario. A player's requests for the client's Sluaghbinder slot (62), the Sluaghbinder (63) and the Acolyte novice (16) are refused, and a Celt Naturalist is made. `login-save-restart` keeps a character over a restart. The server's `FORK_BUILD` line ends `sluaghbinder=off` |
| 3 | `playtester` (Linux) | The same `class-switch` scenario on minimal worlds, one with the switch on (the slot makes the Acolyte novice) and one with it off (`make_test_world.py minimal --class-off`), with `login-save-restart` on the class-off world |

`candidate-install` runs from `build-and-test` after its server job, on pushes to `main`, `integration/1.2`, `release/*`
and `ci/*` branches. It can also be run by hand: Actions, candidate-install, with a build-and-test run id and its package
artifact. Its `Candidate-Setup` artifact is the candidate's Setup exe. CI cannot play: it does not cover the character
screen of the real client, an hour of play, a raid, a real PC (antivirus, SmartScreen) or the launcher's window.

## Before you start (owner's PC)

- **Folders.** Everything stays inside `C:\Games\OfflineDAoC`. Never open, start, update or copy into the main install
  `playable-v0.37b`. The test copy is `playable-v0.37b-fresh`. The rows below make three new folders:
  `playable-v1.1.4-update`, `playable-v1.2-new` and `playable-v0.37-classoff`.
- **One server at a time.** Close every Offline DAoC launcher and server first. Only one server can use port 10300.
- **The candidate Setup.** Open the `build-and-test` run of the candidate commit (the `integration/1.2` tip you are
  testing). Its `candidate-install / setup` job's artifact `Candidate-Setup` holds `LivingServer-Setup-1.2.exe`.
  Write down the run's commit: every result below is reported for that commit.
- **The two helper scripts** run from this repository's checkout (`<repo>` below) with Python 3:
  `tools\installer\seed_progress.py` and `source\tools\make_classoff_copy.py`. Neither writes outside the folders
  you give it, and each refuses `playable-v0.37b`.
- **Shortcuts.** In both Setups, untick the desktop and Start menu shortcuts, so the shortcut to your main install
  stays as it is.

## Row 1: a 1.1.4 game updated to the 1.2 candidate through Setup

1. Download `LivingServer-Setup-1.1.4-beta.exe` from the
   [v1.1.4-beta release](https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/tag/v1.1.4-beta). Run it and
   choose a new install. Set the folder to `C:\Games\OfflineDAoC\playable-v1.1.4-update` and untick the shortcuts. It
   downloads Offline DAoC 0.37 (a few GB) and installs Living Server 1.1.4.
2. Before the first start, seed a known character and settings (optional, but it makes the check exact):
   `python <repo>\tools\installer\seed_progress.py seed --game C:\Games\OfflineDAoC\playable-v1.1.4-update --record C:\Games\OfflineDAoC\update114-seed.json`.
   It prints `SEEDED Seedkeeper level 20 (1 item), 3 gamebots, 6 settings, ...`.
3. Play it as a player would, for about 10 minutes. Start `START OFFLINE DAOC.cmd` in that folder. In the launcher,
   generate a small bot roster. In the Living Server tab, change two settings and write them down (or take a screenshot).
   ENTER REALM, make a character of your own, and get a level and some loot. Note its name, level, coin and one bag item.
   Quit the game and close the launcher.
4. Run `LivingServer-Setup-1.2.exe`. Pick `playable-v1.1.4-update` on its games page, and no other game (it lists
   every game it finds, `playable-v0.37b` included). It offers to update Living Server: leave "suggested settings"
   unticked and update. It ends with "The realm awaits".
5. Check:
   - `python <repo>\tools\installer\seed_progress.py check --game C:\Games\OfflineDAoC\playable-v1.1.4-update --record C:\Games\OfflineDAoC\update114-seed.json`
     prints `KEPT everything seeded`.
   - Start the launcher. The Living Server tab shows your two settings, and the roster is there. ENTER REALM: your
     character is listed at the same level, with its coin and bag item.
6. Proof to record: the Setup log (`%TEMP%\LivingServer-Setup-<time>.log`, its `RESULT Success` line), and
   `runtime\server\deployment-backups\fork-build-*\install-record.json` with `BuildName` `1.2`. Also
   `runtime\server\logs\server.log`'s `FORK_BUILD name=1.2 commit=<the candidate's commit>` line, whose settings
   (for example `xp_rate_by_level=`, `bot_schedule=`) show yours.

## Row 2: a new install of the 1.2 candidate

1. Run `LivingServer-Setup-1.2.exe` and choose a new install. Set the folder to
   `C:\Games\OfflineDAoC\playable-v1.2-new`. The folder it suggests may be another one; never use `playable-v0.37b`.
   Untick the shortcuts. Leave "suggested settings" ticked, as a new player has it.
2. When it ends, start `START OFFLINE DAOC.cmd` in that folder. ENTER REALM makes your account. Make a character and
   enter the world.
3. On the character screen, choose Hibernia: the class list has **no Sluaghbinder**. The 0.37 client does not hide
   the Hibernian Mauler slot: for a Celt it can be picked (it shows the stock Mauler text; for a Shar it is greyed).
   The server refuses that slot on a class-off world. **Try it once:** a Celt in the Mauler slot, any name, create.
   No message is shown: the server saves nothing and sends the character list back, and the new character is not in
   it. `server.log` has `CUSTOM_CLASS_REFUSED account=<you> name=<name> class=62 race=<race> (classes/enable_sluaghbinder
   is off)` from this change on (an older build refuses it too, without this line). This is the first
   part of row 3's "character creation offers no Sluaghbinder", seen in the real client.
4. Proof to record: `python <repo>\source\tools\make_classoff_copy.py status --game C:\Games\OfflineDAoC\playable-v1.2-new`
   prints `EDITION enable_sluaghbinder=False game.dll=classic` and `CUSTOM-CLASS characters=0 gamebots=0`. Also the
   Setup log's `RESULT Success` and `server.log`'s `FORK_BUILD name=1.2 commit=<commit> ... sluaghbinder=off`.
   `sluaghbinder=` is in the line from this change on. On an older build, use the `status` line.

## Row 3: a class-off smoke session on a copy of the test world

A new install has no level-50 gamebots, so its raids would not start for a long time. The session therefore uses a
class-off copy of the test world `playable-v0.37b-fresh`, made the way `build_release_037.py` makes the 0.37 edition.

1. **Make the copy** (the test copy's server stopped; nothing in it changes):
   `python <repo>\source\tools\make_classoff_copy.py make --from C:\Games\OfflineDAoC\playable-v0.37b-fresh --to C:\Games\OfflineDAoC\playable-v0.37-classoff --classic-game-dll C:\Games\OfflineDAoC\playable-v1.2-new\runtime\client-opendaoc\app\game.dll`
   - It copies the folder, then **flips the setting in the copy's world**: `classes / enable_sluaghbinder` is `False`.
     It also removes the Sluaghbinder trainer and wisp and the class's specialization rows.
   - It replaces the copy's `game.dll` with the 0.37 client from row 2's install. Both files are hash-checked: the copy's
     must be the 0.37b client, and the replacement the 0.37 one (`f55ed6b0…`).
   - It ends with `EDITION enable_sluaghbinder=False game.dll=classic`. It names any Sluaghbinder characters and
     gamebots the test world already had (`NOTE ...`), and leaves them as they are. A 0.37 world never has them, so
     play a classic-class character.
   - If the copy needs the candidate build, run the candidate Setup on `playable-v0.37-classoff` (the copy only).
2. **Start it.** Start `START OFFLINE DAOC.cmd` in `playable-v0.37-classoff`. `runtime\server\logs\server.log` must have:
   - `GameServer startup completed`;
   - `FORK_BUILD name=1.2 commit=<the candidate's commit> ... sluaghbinder=off`.
3. **Character creation.** On the character screen in Hibernia, the class list has no Sluaghbinder. The Mauler slot
   can still be picked for a Celt (row 2 step 3): try it once. It is refused with no message; the character list
   comes back without it, and `server.log` has `CUSTOM_CLASS_REFUSED ... class=62`. Then make a classic character
   (for example a Celt Naturalist): it is created. After the session,
   `make_classoff_copy.py status --game C:\Games\OfflineDAoC\playable-v0.37-classoff` must still count the same
   custom-class characters as the copy step's `NOTE` lines (none new).
4. **An hour of play** with a classic-class character: questing, a group with gamebots, a town visit, the Living
   Server tab open once. Look for:
   - no crash or disconnect;
   - no `| ERR |` or `| FATAL |` line in `server.log` that names Sluaghbinder, Acolyte, class 62 or 63, or the trainer
     Muirenn;
   - no gamebot of the custom class newly made (the launcher's bot batches and the server's generator honour the
     switch);
   - the Sluaghbinder quests not offered.
5. **A raid starts.** Either wait for the realm's own raid call or force one:
   - The call: `REALM_RAID_SIZED` logs when the next one is due, then `REALM_RAID_SCHEDULED event=... realm=...` and
     the call on screen.
   - Forced: with GM access (the launcher's "Make me GM", server stopped), run `/epictest list`, then
     `/epictest start <event> start`.
   - Either way the proof is `REALM_RAID_STARTED event=<event> present=... required=...` in `server.log`, on this copy,
     with the `FORK_BUILD ... sluaghbinder=off` line above it in the same log.
6. **Record**: the candidate commit, the times of the `FORK_BUILD`, `REALM_RAID_SCHEDULED` and `REALM_RAID_STARTED`
   lines, and the session's length. Also record any `ERR` lines and what was seen at character creation.

When a row is done, its status in [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md) says "Seen on `<commit>`", with the
date and the proof lines. Until then it says which CI run tested it ("tests only").
