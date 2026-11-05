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
   has none of them. `--bots-per-realm 10` is the default and the most: 30 gamebots in total (faction-rate wants
   them: the server's /faction rate grows with the bots online). Each realm's first one (an Armsman, Warrior or
   Blademaster) is the **helper**: level 12 (`--helper-level`) and already trained for it, with its specs and trained
   level written in its record, as a gamebot of that level would have them. The kill-quest and guild scenarios group
   with it. The others are level 1. The server's startup ramp (`startup_ramp_minutes`) is set to 1 minute in the
   copy, so the whole roster is online a minute after the server starts, not over 15.
4. **Run the scenarios.** Make a fresh test world for each run, or keep a clean copy of `test-world.db` and copy it
   each time. A run adds characters and moves bots.
   ```
   copy C:\Games\OfflineDAoC\playtest\test-world.db C:\Games\OfflineDAoC\playtest\run.db
   dotnet source\tools\OfflineDaoc.Playtester\bin\Release\net10.0\OfflineDaoc.Playtester.dll run ^
       --world C:\Games\OfflineDAoC\playtest\run.db ^
       --server-dir C:\Games\OfflineDAoC\playable-v0.37b-fresh\runtime\server ^
       --out C:\Games\OfflineDAoC\playtest\reports\first
   ```
   The server runs with the folder's bundled .NET (`tools\dotnet`). A full run takes about 50 minutes, mostly waiting:
   the quit timer, gamebots logging in, and up to half an hour of /faction (see faction-rate below).
5. **Read the report**: `report.md` in the `--out` folder. `report.json` holds the same data plus every chat line. The
   server's console logs are in the same folder (`server-console-1.log`, and `-2` after the restart).

Other options:
- `--scenarios login-save-restart,tell-gamebot` runs only those scenarios (`list` shows them all).
- `--attach` uses a server you started yourself on the test world. It skips `login-save-restart`, which restarts the
  server.
- `--reply-seconds`, `--listen-seconds`, `--max-listen-seconds`, `--min-faction-per-minute` and `--bots-online-seconds`
  change the limits.
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
| `playtestergm` | GM (plvl 3) | the quest and guild scenarios: `/jump`, `/player level`, `/player class`, `/player money`, `/player jump group`, `/heal`, `/gc create` |

## The scenarios

| Id | Needs | What it checks |
|---|---|---|
| `login-save-restart` | `--server-dir` | A new character walks 300 units, and the server confirms where it is (`/gloc`). Then it `/quit`s and the save is checked in the database. The server stops (console `exit`) and starts again. The character must be listed with the same level and class and come back within 60 units of where it quit. |
| `chat-players` | any world | Two players at the same start see each other. A `/say` from one is heard by the other, and a tell goes each way. |
| `group-players` | any world | One player invites the other by name and the other accepts the dialog. Both get the group window with two members, a `/g` line is heard, and `/disband` ends the group. |
| `tell-gamebot` | gamebots | Wait for gamebots of the realm to be online (the world database's `IsOnline`). Tell up to three of them "hey, how's it going?". Each must answer with a tell within `--reply-seconds` (default 45). |
| `faction-rate` | gamebots | Wait until 80% of the realm's gamebots are online, say one `/faction` line, then listen. Count the gamebots' /faction lines: lines a minute, speakers, and lines in the first minute. The pass mark is what the server predicts (see below). |
| `delivery-quest` | release world | The GM takes the first delivery quest of the region from the world data (DataQuest whose first step is Deliver). It interacts with the giver and whispers the quest's AcceptText. Then for each step it `/jump`s to the target, gives the step's item from the backpack (or interacts), and checks the quest moves on. It must end with "You have completed the ... quest". |
| `kill-quest-bot-group` | release world, gamebots | A player (not the GM) does a kill quest with a gamebot, set up the way a player would meet it. The quest is the region's kill quest with a hand-in and the weakest target. The GM makes the player an Armsman (`/player class`) and sets its level 4 above the target, inside the quest's range. The player trains at an Armsman trainer (`/train`), and the GM gives it the coin to buy a slashing weapon of its level from a merchant, which it wields. The player takes the quest, meets the helper gamebot (level 12: above the player and 5 above this target), invites it, and the GM brings the group to the target (`/player jump group`). They fight it, with full health first (`/heal`). The kill must count for the quest (the step item, the journal or "completed"), the hand-in steps run, and it must end with "You have completed the ... quest". It reports the experience of the kill and of the hand-in. |
| `guild-dues` | release world, gamebots | The GM forms a guild with the player as leader (`/gc create`, after `/gc quit` from the starting guild). The leader sets dues (`/gc dues 50`; the server caps them at its maximum). Two levels (`/player levelup`) must earn the guild merit. The leader invites the helper gamebot into the guild (`/gc invite`) and the group. The group kills up to 8 monsters 6,000 to 30,000 units from the start (away from the village guards), green to blue for the player, until a kill drops coin. A lost fight is given up: the GM brings the player back (`/player rez`) and the next monster is tried. `/gc info` before and after must show the dues in the guild bank. It reports the coin picked up, the dues paid and any merit lines naming the bot. |

The scenarios run in this order against one server. Each one quits its players at the end. In the first minute after a
server start, the quit timer runs up to 60 seconds, so some scenarios wait that long.

## How faction-rate predicts its pass mark

The server sets each chat channel's pace from `chat-budget.json` (see [bot-voices/CHAT-BUDGETS.md](bot-voices/CHAT-BUDGETS.md)).
faction-rate works out the same number:

expected lines a minute = (the faction target + its reply target) × (gamebots online in the realm ÷ 1000) ^ 0.65 × the
hour factor × `bot_voices_chat_rate` (0 to 3, default 2; for the reply target never below 1)

The reply target (`reply_target`, from Living Server 1.2, PR #82) is the pace of bots' answers, which the 1.2 server
counts apart from the lines bots start. A `chat-budget.json` without it (Beta 1.1.2's) predicts the target alone.

- The **budget** is the server folder's own `chat-budget.json` when it has one, or else the one built into the server
  (the playtester carries a copy of it).
- **Gamebots online** are read from the test world every 15 s while it listens, and the expected rate is their mean.
- The **hour factor** is the `hours` entry for the PC's hour, or the `hours_clock_schedule` entry when
  `bot_schedule_mode` is `clock`. `bot_voices_chat_rate` and `bot_schedule_mode` are read from the test world.
- It **passes** at half the expected rate. `--min-faction-per-minute` sets a fixed mark instead.
- It **flags** a rate above the server's own ceiling, the faction ceiling plus its reply ceiling: the report gets a
  "FLAG" line and the console a "! FLAG" line, and the scenario still passes. `--fail-over-ceiling` makes it fail.
  Against Beta 1.1.2's budget (no reply ceiling, ceiling about 0.7-0.9 at 10 bots a realm) the runs below would be
  flagged; against 1.2's (about 1.5-1.8) they wouldn't.
- It **listens** until 10 lines are expected: at least `--listen-seconds` (180) and at most `--max-listen-seconds`
  (1800). With 10 gamebots at 5 pm that's about 0.34 lines a minute, so about 29 minutes. A short listen can't tell
  1 line from 0, which is why the 3-minute version failed half the time.
- The report shows the measured rate next to the expected one, with the floor, the ceiling, the reply target and
  ceiling, the high mark and each factor.

## What ran for real (in the development container, 2026-11-04)

These runs used the server built from this branch on Linux, without navmeshes or the Detour library:
- **Minimal world:** `login-save-restart`, `chat-players` and `group-players` passed. The five gamebot and quest
  scenarios were skipped. This is the run the nightly workflow does.
- **Test worlds made by `make_test_world.py from-release`** from a copy of the 0.37b test install's world. The copy
  held 1 account, 4 characters and 2,711 bots, none of which were kept; 18 test gamebots were added. In the last full
  run of all eight scenarios (20 minutes), six passed:
  - `login-save-restart` passed. The character came back 0 units from where it quit, and the restart took 24 s.
  - `chat-players` and `group-players` passed.
  - `tell-gamebot` passed: the bots answered in 15.5 s and 5.2 s.
  - `faction-rate` **failed** at 1 line in 3 minutes (0.33 a minute) with 3 gamebots online. An earlier run passed
    with 7 lines from 3 speakers (2.3 a minute). With so few bots and a fixed mark of 0.5, it failed about half the
    time. It now predicts its mark (see above). On a test world with 10 gamebots a realm it **passed in 3 of 3
    runs** (5 to 7 pm):

    | Run | Bots online | Hour factor | Expected a minute | Pass mark | Measured a minute | Lines, speakers |
    |---|---|---|---|---|---|---|
    | 1 | 9.3 | 0.885 | 0.338 | 0.169 | 1.414 | 41 in 29 min, 8 |
    | 2 | 9.6 | 0.920 | 0.360 | 0.180 | 1.402 | 38 in 27 min, 7 |
    | 3 | 9.0 | 0.937 | 0.350 | 0.175 | 1.291 | 35 in 27 min, 9 |
  - `delivery-quest` passed: Simple Request, completed, 2,300 XP.
  - `kill-quest-bot-group` passed. The quest was Hunt for Slith. The Armsman (level 11, Slash 10, an iron bastard
    sword bought from Alburn Hale) and Aldric (level 12) killed Slith. Aldric was seen targeting it and hit it. The
    kill gave "You receive the Slith's Tail from Slith!" and the hand-in completed the quest, worth 9,900 XP and 9
    silver 55 copper. The kill itself showed 459,950 XP (see Found below).
  - `guild-dues` failed in that run: its monsters were by the village, and no kill there gave coin. With the change
    that hunts away from the village, it passed in two of two further runs. In those, 79 and 100 copper were picked
    up, 19 and 25 went to the bank (25%, the server's cap), and merit was 71 from the two levels.
- The server console logs of these runs hold about 700 lines marked ERR, mostly from missing navmeshes on Linux.
  Nobody has gone through them.

Found along the way (not fixed here):
- A character set to a level by the GM (`/player level`, `/player levelup`) got exactly 459,950 XP for its next kill.
  That is the experience of level 11, which it already had. This is probably an effect of the GM's level change,
  not of the kill.
- In the first minute after a server start, `/quit` takes up to 60 s. `/jump to <name>` reads a two-word NPC name as
  a name and a realm.
- /faction ran at about 4 times the chat budget's target, and above its ceiling (0.85 to 0.90 a minute), in all
  three faction-rate runs. The budget gates the lines that start a talk (`TryStartBudgeted`). The answers to them,
  the debates and the gold spammer (10 and 8 lines in two runs) seem to come on top; that is read from the chat, not
  confirmed. The test only checks the lower side.
- Without navmeshes (this container) gamebots do not walk to the player. The scenarios move the group with
  `/player jump group`.

None of this has been run on Windows or against the original client. Gamebot behaviour without navmeshes is not what
it is on your PC.

## Still to do

- **The quest offer window** (Dialog 0x22) is answered through DialogResponse's QuestSubscribe case. No giver in these
  runs opened one, so the accept packet is not confirmed against game.dll.
- **The reward window** (Dialog 0x21, QuestRewardChosen 0x40) of a quest with item choices is not answered yet. The
  kill and delivery quests found here pay coin and experience only.
- **Merit from the gamebot:** a gamebot earns its guild merit when it gains a level. The guild scenario does not
  level the helper, so it only reports any such line.
- **Travel:** movement is straight lines at running speed, with no navmesh of its own. Long trips use the GM's
  `/jump` and `/player jump group`, so the gamebot's own pathing to the player is not tested.
