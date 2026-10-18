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

# Offline DAoC — single-player Dark Age of Camelot with bots

Offline DAoC is a local, single-player Dark Age of Camelot (Classic + Shrouded Isles, 1.65 rules)
that runs entirely on your own PC. The world is filled with autonomous gamebots that level,
group, trade and fight on their own, and you can recruit companion bots to adventure with you.

It is a community project built on the open-source [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core)
server. The customizations were developed with AI tools (Codex, then Claude) and directed and
play-tested by the project owner. It is not an official DAoC product and is not for sale.

## Current release: 0.36

0.36 carries on from 0.35: bot raids that finish Darkness Falls, Summoner's Hall and Tuscaran Glacier, smarter
keep, relic and battleground armies, bots that find the long way around, a fixed memory leak, classic quests with
their item rewards and period experience, and more. It also brings **a new game client**. Read
[what's new in 0.36](docs/RELEASE-0.36.md), or every change in the [changelog](CHANGELOG.md).

There are three editions. They are the same game on the same server:

| Edition | What it is | Download |
|---|---|---|
| **0.36b** | The original DAoC client, with the custom Hibernian class, the **Sluaghbinder** (a pet-summoning undead binder), for players and bots. | [v0.36b release](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.36b) |
| **0.36** (recommended) | The original DAoC client with the classic Classic + SI class list only. There are no Sluaghbinder players or bots. | [v0.36 release](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.36) |
| **0.36c** (BETA) | The game with **two clients**: the original one and a **new client** built in Godot 4. Both play the same account, characters, bots and world, so you can switch whenever you like. It works with 0.36b and with 0.36: the installer asks which one you want. | [v0.36c release](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.36c) |

**Handy commands:** [QUICK COMMANDS.txt](QUICK%20COMMANDS.txt) lists the in-game commands and launcher shortcuts: teleport to a bot (`/tele`), find monsters of a level (`/mobs`), add 10 or 100 bots at once (Shift or Ctrl + click), and more. Every command is in [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt), and both files are in your game folder too.

## Download and play (no Git or AI needed)

1. **Check the requirements:**
   - A 64-bit Windows 10 or 11 PC with a CPU that supports AVX2.
   - 16 GB RAM recommended.
   - Free disk space: about **20 GB** for 0.36b or 0.36, about **60 GB** while 0.36c installs.
   - For the original client: the Windows **.NET Framework 3.5** feature. [How to turn it on](docs/PLAY.md#before-you-start).
   - For the new client (0.36c): a graphics card that supports Vulkan or DirectX 12, with current drivers.
2. **Get the downloader:** open the release page of your edition (links above) and download its one zip file:
   `Download-OfflineDAoC-v0.36b.zip`, `Download-OfflineDAoC-v0.36.zip` or `Download-OfflineDAoC-v0.36c.zip`.
   Put it in a new, empty folder with a short path, for example `C:\Games\OfflineDAoC`.
3. **Unzip it:** right-click the zip, choose **Extract All…**, and open the extracted folder.
4. **Run it:** double-click `DOWNLOAD-AND-PLAY-v0.36b.cmd` (or `-v0.36` / `-v0.36c`). If Windows asks, choose
   **Run** (or **More info → Run anyway**). It downloads the game in parts, checks every part, and unpacks it into
   a new folder next to it. It never overwrites an existing game. If it stops, run it again: it continues.
5. **Play:** open the new `playable-v0.36…` folder and double-click **START OFFLINE DAOC.cmd**
   (in 0.36c, **START OFFLINE DAOC - NEW CLIENT.cmd** plays with the new client).
   1. Click **START SERVER** and wait until it says **RUNNING**.
   2. Click **ENTER REALM**.
   3. Your own local account is created automatically the first time.
6. **Create your bots** with the bot buttons in the launcher. Every new install starts with an empty world and
   default settings. [QUICK COMMANDS.txt](QUICK%20COMMANDS.txt) has the shortcuts and in-game commands.

**Already have 0.36b or 0.36 and want the new client?** Download `Download-OfflineDAoC-v0.36c.zip`, unzip it,
double-click `ADD-NEW-CLIENT-TO-0.36-OR-0.36b.cmd` and choose your game folder. Your saves stay as they are, and both
clients play them. On a 0.36 game the new client offers the classic classes only, like the original client.

The full walkthrough, with troubleshooting, is in [docs/PLAY.md](docs/PLAY.md). The new client has its own guide:
[docs/NEW-CLIENT.md](docs/NEW-CLIENT.md).

> **Code > Download ZIP** on this page gives you the *source code*, not the playable game. Use the
> downloader from a release page to get the game.

## Already playing an older version?

Your characters, account, items, money, houses **and bots** can come with you. Every 0.36 folder
includes **IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd**:
1. Close the old and the new game.
2. Run the import tool from the new folder.
3. Pick your old folder.

The tool never changes your old folder, and it backs up the new one first. It works with v0.3,
v0.31, v0.31b, v0.32, v0.32b, v0.33, v0.33b, v0.34, v0.34b, v0.35, v0.35b and the "new class test" builds. In
0.36c, both clients then have your progress. See [docs/TRANSFER-PROGRESS.md](docs/TRANSFER-PROGRESS.md).

## Older versions

Every earlier release is still available and unchanged:
- [v0.35](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.35) and
  [v0.35b](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.35b).
- [v0.34](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.34) and
  [v0.34b](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.34b).
- [v0.33](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.33) and
  [v0.33b](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.33b).
- [v0.32](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.32) and
  [v0.32b](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.32b): Darkness Falls beta.
- [v0.31](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.31) and
  [v0.31b](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.31b).
- [v0.3](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.3).

Their download helpers are in [older-versions/](older-versions/), and their notes are in
[docs/history/](docs/history/). Each release's source code stays on its own branch and tag (for
example `release/v0.35-claude-takeover-iii` and `v0.35b`), so older code is always there to compare.

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
  by a bot army yet. See [Known issues](https://github.com/shadowofze/OfflineDAoC/discussions/4).
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
