# The automated playtester

A scripted player and a test world that check the game the way a person in the game client would. Nobody has to
type in the client or read the chat window. It runs overnight on your PC or in CI and writes a report.

- **`source/tools/make_test_world.py`** makes the test world: a **new** database file.
- **`source/tools/OfflineDaoc.Playtester`** is the scripted player. It is a .NET 10 console tool that speaks the
  original 1.127 client protocol to a server on `127.0.0.1:10300`.

Everything it does is automated. A passing report means the scenario's checks passed on that run. It is not an
in-game playtest and does not replace one.

## Safety

- `make_test_world.py` opens the world you give it **read-only** and only ever writes a new file. It refuses an
  `--out` that already exists or that is the source.
- On the copy it runs the release builder's own cleaning (`clean_world` in `build_release_037.py`). Every saved-progress
  table is emptied, and keeps, doors and relics go back to their defaults, so the test world has no saves.
- The playtester refuses any database without the `offline_playtester_world` table that `make_test_world.py` writes.
  It cannot run on a played world.
- It runs the server from a **copy** of the server folder, in its report folder. The server writes its run state beside
  itself (`bot-world.json`, `rvr-world.json`, `rvr-relic-campaigns.json`, logs). In a copy, none of that lands in your
  install. The navmeshes are linked with a directory junction, not copied.
- The server runs with your folder's `serverconfig.xml`, changed to use the test world through SQLite, on 127.0.0.1.
  Your install's own database is never opened.
- Only one server can use port 10300. The playtester refuses to start one while the port is taken, so **close the
  launcher and any server first.**

## On your PC (the test copy `C:\Games\OfflineDAoC\playable-v0.37b-fresh`)

You need the .NET 10 SDK (as for building the server) and Python 3.

1. **Build the playtester** (once, and after each pull):
   ```
   dotnet build source\tools\OfflineDaoc.Playtester -c Release
   ```
2. **Close the launcher and the game.** The server of that folder must not be running.
3. **Make the test world** from the clean world of the fresh install. Use a folder outside the install:
   ```
   mkdir C:\Games\OfflineDAoC\playtest
   python source\tools\make_test_world.py from-release ^
       --world C:\Games\OfflineDAoC\playable-v0.37b-fresh\runtime\data\opendaoc.sqlite3.db ^
       --out C:\Games\OfflineDAoC\playtest\test-world.db
   ```
   It prints what it found and did. If you have played in that folder, the "saves in source" line shows it; the copy
   has none of them. `--bots-per-realm 6` is the default: 18 level-1 gamebots in total.
4. **Run the scenarios.** Make a fresh test world for each run, or keep a clean copy of `test-world.db` and copy it
   each time. A run adds characters and moves bots.
   ```
   copy C:\Games\OfflineDAoC\playtest\test-world.db C:\Games\OfflineDAoC\playtest\run.db
   dotnet source\tools\OfflineDaoc.Playtester\bin\Release\net10.0\OfflineDaoc.Playtester.dll run ^
       --world C:\Games\OfflineDAoC\playtest\run.db ^
       --server-dir C:\Games\OfflineDAoC\playable-v0.37b-fresh\runtime\server ^
       --out C:\Games\OfflineDAoC\playtest\reports\first
   ```
   The server runs with the folder's bundled .NET (`tools\dotnet`). A full run takes about 15 minutes, mostly waiting:
   the quit timer, gamebots logging in, and three minutes of /faction.
5. **Read the report**: `report.md` in the `--out` folder. `report.json` holds the same data plus every chat line. The
   server's console logs are in the same folder (`server-console-1.log`, and `-2` after the restart).

Other options:
- `--scenarios login-save-restart,tell-gamebot` runs only those scenarios (`list` shows them all).
- `--attach` uses a server you started yourself on the test world. It skips `login-save-restart`, which restarts the
  server.
- `--reply-seconds`, `--listen-seconds`, `--min-faction-per-minute` and `--bots-online-seconds` change the limits.
- The exit code is 0 when nothing failed (skipped scenarios and incomplete hooks are allowed), 1 when a scenario
  failed, and 2 for bad arguments or setup.
- Set `PLAYTESTER_TRACE=1` to print every packet sent and received, for working out a protocol question.

## Without game files (CI, or a quick check)

`make_test_world.py minimal --out minimal.db` makes a world with one region (Albion: Camelot Hills and Salisbury
Plains), a startup location and the test accounts. On its first start the server creates every other table. It has
no NPCs, quests or gamebots, so the scenarios that need them are **skipped**, never passed. The workflow
`.github/workflows/playtester.yml` builds the server, makes this world and runs `login-save-restart`, `chat-players`
and `group-players` every night (and on demand).

## The test accounts

The test world has three accounts. Each password is the same as the account name, and the server stores it hashed at
the first login:

| Account | Level | Used by |
|---|---|---|
| `playtester` | player | most scenarios |
| `playtester2` | player | the second player in the chat and group scenarios |
| `playtestergm` | GM (plvl 3) | the quest scenarios (`/jump`, `/player level`) |

## The scenarios

| Id | Needs | What it checks |
|---|---|---|
| `login-save-restart` | `--server-dir` | A new character walks 300 units, and the server confirms where it is (`/gloc`). Then it `/quit`s and the save is checked in the database. The server stops (console `exit`) and starts again. The character must be listed with the same level and class and come back within 60 units of where it quit. |
| `chat-players` | any world | Two players at the same start see each other. A `/say` from one is heard by the other, and a tell goes each way. |
| `group-players` | any world | One player invites the other by name and the other accepts the dialog. Both get the group window with two members, a `/g` line is heard, and `/disband` ends the group. |
| `tell-gamebot` | gamebots | Wait for gamebots of the realm to be online (the world database's `IsOnline`). Tell up to three of them "hey, how's it going?". Each must answer with a tell within `--reply-seconds` (default 45). |
| `faction-rate` | gamebots | Say one `/faction` line, then listen for `--listen-seconds` (default 180). Count the gamebots' /faction lines: lines a minute, speakers, and lines in the first minute. It fails below `--min-faction-per-minute` (default 0.5). |
| `delivery-quest` | release world | The GM takes the first delivery quest of the region from the world data (DataQuest whose first step is Deliver). It interacts with the giver and whispers the quest's AcceptText. Then for each step it `/jump`s to the target, gives the step's item from the backpack (or interacts), and checks the quest moves on. It must end with "You have completed the ... quest". |
| `kill-quest-bot-group` | release world, gamebots | **A hook, reported as Incomplete.** The GM `/jump`s to an online gamebot, invites it by name and checks it joins the group. It sets its level to 5 and takes the first kill quest of the region. Killing the target with the bot and the turn-in are TODO in the scenario and in the report. |

The scenarios run in this order against one server. Each one quits its players at the end. In the first minute after a
server start, the quit timer runs up to 60 seconds, so some scenarios wait that long.

## What ran for real (in the development container, 2026-11-04)

These runs used the server built from this branch on Linux, without navmeshes or the Detour library:
- **Minimal world:** `login-save-restart`, `chat-players` and `group-players` passed. The four gamebot and quest
  scenarios were skipped. This is the run the nightly workflow does.
- **A test world made by `make_test_world.py from-release`** from a copy of the 0.37b test install's world. The copy
  held 1 account, 4 characters and 2,711 bots, none of which were kept; 18 test gamebots were added. All seven
  scenarios ran in one 13-minute run:
  - `login-save-restart` passed. The character came back 0 units from where it quit, and the restart took 42 s.
  - `chat-players` and `group-players` passed.
  - `tell-gamebot` passed. Aldric and Brennan answered in 11.8 s and 7.1 s ("same old, killing things", "tired but
    still leveling" in an earlier run).
  - `faction-rate` passed: 7 lines from 3 speakers in 3 minutes (2.3 a minute) with 3 gamebots online.
  - `delivery-quest` passed. Simple Request went from Lieutenant Brude to Lieutenant Kuebler and back, ended with "You
    have completed the Simple Request quest!" and gave 2,300 XP.
  - `kill-quest-bot-group` was Incomplete, as designed. Aldric joined the GM's group and the Ripper quest was taken;
    killing Throatripper and the turn-in are the two TODOs.
- The server console logs of that run hold about 700 lines marked ERR, mostly from missing navmeshes on Linux.
  Nobody has gone through them.

None of this has been run on Windows or against the original client. Gamebot behaviour without navmeshes is not what
it is on your PC.

## Still to do

- **Kill quest in a bot group:** walk or `/jump` the group to the target's camp, `/target` and attack with the bot
  assisting, wait for the journal goal, then turn in at the giver. That means answering the reward window
  (QuestRewardChosen, 0x40).
- **The quest offer window** (Dialog 0x22) is answered through DialogResponse's QuestSubscribe case. No giver in these
  runs opened one, so the accept packet is not confirmed against game.dll.
- **A guild with gamebots paying dues**, the third check the 1.1.2 playtest could not do. It is not a scenario yet:
  `/gc` commands plus a wait for the dues line.
- Movement is straight lines at running speed, with no navmesh of its own. Long walks should use `/jump` (GM).
