> **Disclaimer.** Dark Age of Camelot is the intellectual property of Electronic Arts and Broadsword
> Online Games. Offline DAoC is not affiliated with, endorsed by, or connected to either company. The
> game client, textures, models and other assets it uses belong to them, and this project is built on
> top of that copyrighted work.
>
> This is a free, non-commercial fan project. It is not a product and it is not for sale. No donations
> are accepted, and the project owner has not made, and will not make, any money from it. The client
> files are the same publicly available ones every free shard uses; the work original to this
> repository is the bot system, the server customizations and the new client's code. Because the code is open source, the
> owner cannot control what forks or other people do with it.
>
> This "time capsule" of the 2002-2003 game was never meant to compete with the current retail Dark Age
> of Camelot, which is a very different, modern experience. If that interests you, please visit the
> official site: [darkageofcamelot.com](https://www.darkageofcamelot.com/).

# Offline DAoC: Living Server

### Where Legends Wake and the Realms are Alive. Offline. Never alone.

**1.2 is out.**

![Living Server](tools/installer/src/Assets/hero.jpg)

It's a weeknight in Camelot and the bank is packed. Someone is selling a breastplate they made themselves. A guild
you've never heard of is arguing about Paladin specs. Past the wall, Midgard just took a keep, and a horn is about to
tell your whole realm what happens next. All of it is running on your PC.

**Offline DAoC** is single-player Classic and Shrouded Isles with 1.65 rules, on your own PC, with bots who level, group
and fight. **Living Server** is the part of it, maintained by [chris4aker](https://github.com/chris4aker), that gives those bots a life: they talk like 2003 players, keep their own hours, join guilds,
remember you, craft and trade, call their own raids, take each other's keeps, and invite you along.

> **[Download Living Server 1.2](https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/latest)**: one Setup
> installs Offline DAoC 0.37 and Living Server from nothing, or adds Living Server to the 0.37 game you have.
> [What's new in 1.2](docs/RELEASE-NOTES-1.2.md) · [Known issues](docs/KNOWN-ISSUES.md)

Formerly the **Social Expansion** (0.36-se.1 to 0.37-se.2). Install over any earlier build and keep everything.

## What Living Server adds

The tag says which release brought each part. **Offline DAoC** marks what the game already did before Living Server, listed only where Living Server
builds on it. **Tests only** means it is in the release but hasn't been seen working on the owner's PC yet.

### A realm full of people

- **Personalities** *(1.0)*. Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer, with five traits. Target a
  gamebot and type `/personality`.
- **They talk like 2003** *(1.0; rewritten 1.1; tuned since)*. Hand-written period chat in five writing styles, in every
  channel, chosen by realm, class, level, zone, time and what is really happening. About 5 to 7 /faction lines a minute
  on a full realm *(1.2)*.
- **They answer you** *(1.0; first, on their own budget, in 1.2)*. A tell, or a line that names a bot, always gets an
  answer. A bot that says "omw" is coming *(1.2)*.
- **Bot guilds** *(1.0)*: names over their heads, /gu, recruiting, dues, merit, buffs, alliances and keep claims; guild
  crafters who make for guildmates at cost *(1.2)*.
- **They remember you** *(1.0; 1.1.3)*: your rezzes, your insults, your dings.
- **Server life** *(1.0)*: grats, server firsts, rezzes and corpse runs, frontier alarms, nemeses, breaks, body language,
  and an optional real-clock schedule so the capital is busiest in the evening (off by default since 1.2.1: the whole
  roster stays online).
- **Names like a 2003 server** *(1.1)* and **the Herald** *(1.0)*, your world's news page.
- **They dress up** *(1.2)*: dyes and weapon lusters at the merchants' prices. Never glows.
- **Arguments that run for days** in /faction *(1.1; ten more in 1.2)*.

### Bots that play the game, and play with you

- **Levelling like people** *(Offline DAoC; improved from 1.0)*. Gamebots start without armor and earn it.
- **Talk to bots by name** *(1.0)*: `/send` any online bot of your realm; `/invite` or `/target` a nearby one.
- **Bot leaders invite you** *(1.1.3; 1.2)*. Say yes and the bot leads the camp, the pulls and the breaks. `/lead`,
  `/lead bot`, `/invites off` *(1.2)*. Raid leaders offer level 50s a spot *(1.2)*.
- **Quests with your bot group** *(1.1, 1.1.2)*; epic chains linked on every install *(1.2)*.
- **XP by level range** *(1.0)*.
- **Bots as people** *(1.2; mostly tests only)*: skill that grows, mistakes mostly on strangers, knowing the way, moving
  like people, personal milestones.
- **Built like 2003** *(1.2; tests only)*: new bots come in each realm's 2003 class mix with a period build for life.
  Bots already in your world keep theirs.

### The endgame runs itself

- **Realm raids** *(1.0)*: the dragon, the epic dungeon, Summoner's Hall, Darkness Falls and the raid dungeons, on each
  realm's own schedule. **The tale, then the horn** *(1.2)*: the boss's story in /faction, a call across the screen,
  then the muster. One raid every four hours; no clock inside the dungeon *(1.2)*.
- **The epics at full strength** *(1.2)*: the bosses' own rules back on, each a server setting, and their throws and
  glares pick bots too. Raid sizes from the period and the first tactics *(1.1.2)*; a plan for the real puzzles *(1.2)*.
  **Still being tuned:** the bots lose some of the hardest fights. The notes name them.
- **The frontier** *(1.0; 1.2)*: keep sieges with a defence; enemies who notice you by chance and distance; groups that
  call targets, mez and peel; battleground waves and storms.

### Somebody made that

- **The Realm Exchange and real loot** *(Offline DAoC, kept)*; bots sell only at natural task breaks.
- **Bot crafters** *(1.2)* at the forges: titles, craft orders, haggling, and sales on the Exchange. **Join an order**
  yourself at a crafting master, as in 1.65 *(1.2; original client; tests only)*.

### Every place has its story

- **Lore** *(1.1; 1.2)*: 108 places, 578 bosses and four realm histories, checked against 832 archived Camelot Herald
  articles. Loremasters in each capital, lore books, the Bestiary in **Realm Records**, `/lore` in either client, bosses
  that speak, and **Atmosphere lines** on screen.

### Company

- **Companions** *(Offline DAoC; more in 1.2)*: `/spawn` a helper of any class and spec. They stay at the fight when a boss
  throws you *(1.2)*.
- **Hired companions** *(1.2)*: up to seven of your own, hired beside a healer of your realm with `/hire`; you gear and
  train them. Some of the code is borrowed from the True 1.65 project.
- **Play with friends** *(1.2, experimental)*: **HOST FOR FRIENDS**, **JOIN FRIEND** and the **CHARACTER PASSPORT…**.
  Original client only.

### Yours to run

- **The Living Server tab** *(1.0)*: a switch for every part, every part on from the start *(1.2)*. **ALL OFF** gives
  you the plain server.
- **One Setup** *(1.1; 1.1.4)*, **updates from the launcher** *(1.1.2)*, **backups and `ROLLBACK.cmd`** *(1.0; daily
  copies 1.1.2)*.
- **The rules that never change:** real loot, the Realm Exchange, stablemaster routes, selling at natural breaks, bots
  that earn their armor, and Darkness Falls open to solo bots.

## Seen, and tests only

A feature counts as working only once it has been seen working on the owner's PC. 1.2's notes say, feature by feature,
what has been seen and on which build, and what is still tests only. The parts still tests only include most of Bots as
people, era builds on a fresh world, crafting as a player, a session played with a friend, and every fix made after the
last test build the owner ran. See [the 1.2 notes](docs/RELEASE-NOTES-1.2.md#3-seen-and-tests-only).

## What you need

- A 64-bit Windows 10 or 11 PC with a CPU that supports AVX2.
- 16 GB of memory recommended. More bots need more; start small.
- About 20 GB free for a new install of 0.37 or 0.37b (about 60 GB while 0.37c installs).
- For the original client: the Windows feature **.NET Framework 3.5** ([how](docs/PLAY.md#before-you-start)).

## Install

**With Setup.**
1. Download `LivingServer-Setup-1.2.exe` from the [release](https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/latest)
   and run it. No game yet? It downloads Offline DAoC 0.37 from the project's own copy of the 0.37 release files
   and checks every file. Have the game? It adds Living Server.
2. Windows SmartScreen may say "Windows protected your PC", because the exe isn't code-signed. Click **More info**, then
   **Run anyway**.
3. Open the launcher. It has a **Living Server** tab.

**By hand.** Have Offline DAoC 0.37 or 0.37b ([0.37 files](https://github.com/chris4aker/OfflineDAoC-base-files/releases/tag/v0.37)),
unzip `OfflineDAoC-LivingServer-1.2.zip` into your game folder, close the game and server, and run `INSTALL.cmd`.

**On 0.36, 0.36b or 0.36c?** Setup updates the game to 0.37, keeping your saves, then adds Living Server (**tests only** for 1.2: not yet run on the owner's PC). On 0.36 with the Social Expansion, run `PREPARE-FOR-UPDATE.cmd` from this package first.

## Update

- **From 1.1.2 or later:** **Check for updates** in the launcher; START SERVER then reads **GET UPDATE** (Install, Later
  or Skip this version).
- **From anything earlier:** run Setup, or install by hand. Characters, bots, guilds, bot memory and settings stay.
- A world from an earlier build turns on each feature it still had at the old default; your own switches stay.

## Your first evening

1. **Make your realms.** On the launcher's main screen use **+ Lv.1**, **+ Lv.20** and **+ Lv.50** for each realm (Shift
   adds 10, Ctrl adds 100). Raids need level 50s; a realm needs 8 bots of level 5 or higher before it forms a guild.
2. **START SERVER**, then **ENTER REALM**. Your local account is made the first time.
3. Stand in your capital in the evening with /faction open. Type `/lfg`. Ask a bot something.
4. `runtime\server\logs\server.log` has a `FORK_BUILD name=1.2` line listing what's on.

## Handy commands

| Command | What it does |
|---|---|
| `/personality` | The personality of the gamebot you have targeted |
| `/send <name>` | Talk to any online bot of your realm |
| `/lfg <message>` | Look for a group; bot leaders may invite you sooner |
| `/lead`, `/lead bot [name]` | Take the lead of a bot-led group, or hand it to a gamebot |
| `/invites on`, `/invites off` | Bot leaders' unasked group and raid invites |
| `/lore`, `/lore <place or boss> [page]` | Your realm's places and bosses, and whether your realm has slain them |
| `/lswhy`, `/lswhy <name>`, `/lswhy me` | Why a bot spoke, or why bots answered your lines, or didn't |
| `/hire`, `/hire list`, `/hire help` | Hire and manage your companions, beside a healer of your realm |
| `/spawn` | A helper of any class and spec |
| `/tele <bot>`, `/tc`, `/stables` | Travel helpers from Offline DAoC |

Every command: [QUICK COMMANDS.txt](QUICK%20COMMANDS.txt) and [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt).

## Undo and backups

- **Turn a part off:** untick it in the Living Server tab (or **ALL OFF**), save, restart the server.
- **Remove bot guilds:** with the game and server closed, run `python tools\claude-version\set_bot_guilds.py --purge --apply`
  in your game folder. It backs up your database first.
- **Uninstall:** `ROLLBACK.cmd` puts back every file the install replaced. It never puts your world back.
- **Daily backups** go to `runtime\data\backups` (the last 7 are kept), and every install copies your world first.
- **The server listens only on this PC** unless you host for friends; the log warns at each start if it doesn't
  (`NETWORK_EXPOSURE`).

## More

[Known issues](docs/KNOWN-ISSUES.md) · [Every change](CHANGELOG-LIVING-SERVER.md) · [Server life and the Herald](docs/LIVING-SERVER.md) ·
[Bot guilds](docs/BOT-GUILDS.md) · [Personalities and voices](docs/BOT-PERSONALITIES.md) · [Lore](docs/LORE.md) ·
[Crafting](docs/CRAFTING.md) · [Play with friends](docs/PLAY-WITH-FRIENDS.md) · [XP by level range](docs/XP-BY-LEVEL-RANGE.md)

Report a problem on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the `FORK_BUILD` line
from `runtime\server\logs\server.log`.

## Credits

Offline DAoC with Living Server is maintained by [chris4aker](https://github.com/chris4aker). Offline DAoC was created by
[shadowofze](https://github.com/shadowofze), and [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core) is the server
underneath. Some code and ideas are borrowed from the [True 1.65 project](https://github.com/zomboden/OfflineDAoC-True165)
(zomboden/OfflineDAoC-True165): hired companions and several boss and camp fixes in 1.2. Bot-guild membership, route bends and
danger memory are adapted from [stefanrows' fork](https://github.com/stefanrows/OfflineDAoC). Fixes and ideas also come
from rambodolphin13's DividedRealms, lometur's HearthDAoC (the save-safety design among them), Cancain (bots returning
from battlegrounds, `/friend` with gamebots, `/allrelease`), Nikse77, Rmarcoux86, MoomoocowPlus and shadowofze's 0.38
work; the lag fix for large
populations came from Krolgosh-web's report (issue #25). Thanks to everyone on GitHub and r/daoc who reported what they
found. The code is written with Claude (Anthropic). Same license as the original: GPL-3.0. Third-party notices: [THIRD_PARTY.md](THIRD_PARTY.md).

---

*Everything below is the original Offline DAoC README.*

# Offline DAoC — single-player Dark Age of Camelot with bots

Offline DAoC is a local, single-player Dark Age of Camelot (Classic + Shrouded Isles, 1.65 rules)
that runs entirely on your own PC. The world is filled with autonomous gamebots that level,
group, trade and fight on their own, and you can recruit companion bots to adventure with you.

It is a community project built on the open-source [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core)
server. Everything it adds to OpenDAoC, and the whole new client, was written by AI tools (Codex, then Claude). The
project owner, who has no game development or coding experience, directed and play-tested it. It is not an official
DAoC product and is not for sale.

## Last release: 0.37

0.37 carries on from 0.36: you can form your own guild and bring gamebots into it, bot armies attack together and
raise their fallen at sieges, groups travel as one pack, raids get further in Caer Sidi and Galladoria, there is far
less lag on a busy server, and the new client gets many fixes. Read [what's new in 0.37](docs/RELEASE-0.37.md), or
every change in the [changelog](CHANGELOG.md).

**0.37 is an update.** If you already play 0.36, 0.36b or 0.36c, you only download the update (much smaller than the
game) and your account, characters, bots and settings stay as they are. If you are new, the same file installs the
complete game for you.

Living Server installs one edition for new players: **0.37**, the original DAoC client with the Classic + Shrouded Isles
classes ([0.37 files](https://github.com/chris4aker/OfflineDAoC-base-files/releases/tag/v0.37)). The other two 0.37 editions are no longer offered for new installs: 0.37b (the custom
Sluaghbinder class) and 0.37c (the new Godot client). If you already play
0.37b or 0.37c, your game keeps working with Living Server and keeps getting its updates.

**Handy commands:** [QUICK COMMANDS.txt](QUICK%20COMMANDS.txt) lists the in-game commands and launcher shortcuts: teleport to a bot (`/tele`), find monsters of a level (`/mobs`), add 10 or 100 bots at once (Shift or Ctrl + click), and more. Every command is in [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt), and both files are in your game folder too.

## Download and play (no Git or AI needed)

1. **Check the requirements:**
   - A 64-bit Windows 10 or 11 PC with a CPU that supports AVX2.
   - 16 GB RAM recommended.
   - Free disk space for a new install: about **20 GB** for 0.37 or 0.37b, about **60 GB** while 0.37c installs.
   - For the original client: the Windows **.NET Framework 3.5** feature. [How to turn it on](docs/PLAY.md#before-you-start).
   - For the new client (0.37c): a graphics card that supports Vulkan or DirectX 12, with current drivers.
2. **Get the downloader:** open the release page of your edition (links above) and download its one zip file:
   `Download-OfflineDAoC-v0.37.zip`, `Download-OfflineDAoC-v0.37b.zip` or `Download-OfflineDAoC-v0.37c.zip`.
   Put it in a folder with a short path, for example `C:\Games\OfflineDAoC`.
3. **Unzip it:** right-click the zip, choose **Extract All…**, and open the extracted folder.
4. **Run it:** double-click `GET-OFFLINE-DAOC-v0.37.cmd` (or `-v0.37b` / `-v0.37c`). If Windows asks, choose
   **Run** (or **More info → Run anyway**). It asks one question:
   - **1 = Update my game** (you have 0.36, 0.36b or 0.36c): close the game, choose your game folder, and only the
     update is downloaded and applied. Everything it replaces is backed up first. If you have the new client, it is
     updated too.
   - **2 = New install** (you don't have the game yet): it downloads the complete game in checked parts into a new
     `playable-v0.37…` folder next to it, then updates it. If it stops, run it again: it continues.
5. **Play:** open your game folder and double-click **START OFFLINE DAOC.cmd** (in 0.37c,
   **START OFFLINE DAOC - NEW CLIENT.cmd** plays with the new client). The launcher shows **VERSION 0.37** (or 0.37b /
   0.37c).
   1. Click **START SERVER** and wait until it says **RUNNING**.
   2. Click **ENTER REALM**.
   3. Your own local account is created automatically the first time.
6. **Create your bots** with the bot buttons in the launcher. Every new install starts with an empty world and
   default settings. [QUICK COMMANDS.txt](QUICK%20COMMANDS.txt) has the shortcuts and in-game commands.

**Want the new client too?** Run `GET-OFFLINE-DAOC-v0.37c.cmd` from the v0.37c release and choose **1 = Update my
game**: if your game doesn't have the new client yet, it offers to add it. Your saves stay as they are, and both
clients play them.

The full walkthrough, with troubleshooting, is in [docs/PLAY.md](docs/PLAY.md). The new client has its own guide:
[docs/NEW-CLIENT.md](docs/NEW-CLIENT.md).

> **Code > Download ZIP** on this page gives you the *source code*, not the playable game. Use the
> downloader from a release page to get the game.

## Already playing an older version?

**0.36, 0.36b or 0.36c:** just update (step 4, choice 1). Nothing else is needed.

**0.35 or older:** install 0.37 (choice 2), then bring your characters, account, items, money, houses **and bots**
over with **IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd** in the new folder:
1. Close the old and the new game.
2. Run the import tool from the new folder.
3. Pick your old folder.

The tool never changes your old folder, and it backs up the new one first. It works with v0.3,
v0.31, v0.31b, v0.32, v0.32b, v0.33, v0.33b, v0.34, v0.34b, v0.35, v0.35b, v0.36, v0.36b and the "new class test"
builds. In 0.37c, both clients then have your progress. See [docs/TRANSFER-PROGRESS.md](docs/TRANSFER-PROGRESS.md).

## Older versions

Earlier releases (v0.3 to v0.36c) were first published in the original Offline DAoC repository, which is no longer
online. This project keeps the 0.36 and 0.37 download files in [OfflineDAoC-base-files](https://github.com/chris4aker/OfflineDAoC-base-files/releases).

Their notes are in [docs/history/](docs/history/). The download helpers in [older-versions/](older-versions/) point at
that repository and no longer download anything.

## For developers and AI assistants

- **Where to start:** [AGENTS.md](AGENTS.md) (also read by Claude as [CLAUDE.md](CLAUDE.md)) has
  the project rules.
- **Guides:** [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) covers building and testing, and
  [docs/LLM-QUICKSTART.md](docs/LLM-QUICKSTART.md) covers customizing with your own AI.
- **Where the code is:**
  - `source/server`: the game server and all bot AI.
  - `source/tools`: the launcher, the progress importer and the release tools.
  - `source/new-client`: the new client (Godot 4 project and its content pipeline). See its
    [README](source/new-client/README.md).
  - `tools/pet-art`: the art pipeline used for the custom pets, weapons and spell effects.
- **Forking:** you don't need permission to fork this repository and make it your own. Keep the
  existing licenses and credits ([THIRD_PARTY.md](THIRD_PARTY.md)).

## Good to know

- **What the download includes:**
  - the full game client (0.36c: both clients)
  - clean world data and navigation meshes
  - the launcher and server
  - the progress import tool
  - the bundled .NET runtime
  - the source code

  It does **not** include anyone's account, characters, bots or settings. Every install makes its
  own.
- **Raids are still being tuned.** Bot raids have finished Darkness Falls, Summoner's Hall and
  Tuscaran Glacier, but not yet every epic dungeon to its last boss, and no relic has been captured
  by a bot army yet. See [Known issues](docs/KNOWN-ISSUES.md).
- **AI-written code can have bugs.** Keep a backup of your save before installing a new build or
  mod. There's no guarantee against regressions.
- **Licensing:** the original game client is a binary dependency. This repository has the server,
  tools, customization source and the new client's source, not the original client's source code.
  The new client's content is converted from the original client's files and ships only in the 0.36c
  download. The server's license doesn't
  relicense third-party client assets or dependencies. See [THIRD_PARTY.md](THIRD_PARTY.md).
- **Local only:** the default setup is for one PC. Running a public multiplayer server would need
  its own security work.

## Videos

[![Optional Hibernian Sluaghbinder class](https://i.ytimg.com/vi/EowrCcjigBY/hqdefault.jpg)](https://www.youtube.com/watch?v=EowrCcjigBY)
[![Raids and realm events](https://i.ytimg.com/vi/zmh7YkajRx0/hqdefault.jpg)](https://www.youtube.com/watch?v=zmh7YkajRx0)

## Changes

See the [changelog](CHANGELOG.md).
