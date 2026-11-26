# How to install and play DAoC - Living Server

You don't need Git, programming knowledge or an AI tool to play.

## Before you start

**What you need:**
- A 64-bit Windows 10 or 11 PC whose CPU supports **AVX2**. Most CPUs from 2014 onward do.
- 16 GB RAM is recommended; 8 GB may work but is untested.
- For a new install, about **20 GB of free disk space** (5 GB of downloads, 5 GB while they are unpacked, 8 GB for the
  game, and room for saves). An update needs much less. The downloads can be deleted afterwards (the
  `Living Server setup files` folder beside the game).

**Turn on the Windows .NET Framework 3.5 feature.** The original game client's connector needs it,
even though the game bundles its own modern .NET.
1. Open the Start menu and type **Turn Windows features on or off**, then open it.
2. Tick **.NET Framework 3.5 (includes .NET 2.0 and 3.0)**.
3. Click **OK** and let Windows download and install it.
4. Restart the PC if Windows asks.

If the game window never appears after ENTER REALM, this feature is the usual cause. Setup offers to turn it on.

## 1. Install

1. Download **DAoC - Living Server Setup** from the
   [latest release](https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/latest) and run it.
2. Windows SmartScreen may say "Windows protected your PC", because the exe isn't code-signed. Click **More info**, then
   **Run anyway**.
3. **No game yet?** Setup downloads the base game files, several parts at once, checks every part against its SHA-256,
   unpacks them into `C:\Games\LivingServer` (or the folder you choose) and adds Living Server. If the download stops,
   run Setup again: it continues where it stopped.
4. **Already have the game?** Setup finds it and adds Living Server in place. Your account, characters, bots and settings
   stay, and the game stays in its folder.
5. Setup adds **DAoC - Living Server** to the Start menu and the desktop.

## 2. Play

1. Open **DAoC - Living Server** from the Start menu or the desktop (or double-click **START OFFLINE DAOC.cmd** in the
   game folder: it opens the same launcher).
2. In the launcher, click **START SERVER** and wait until it shows **RUNNING**. The first start
   takes longer.
3. Click **ENTER REALM**. The first time, your own local account is created automatically.
4. Create your character and play.
5. When you're done, quit the game, then click **STOP SERVER** in the launcher and wait for it to
   finish saving.

### Bots

A new install starts with an empty world:
1. Each realm's card in the launcher (**ALBION**, **MIDGARD**, **HIBERNIA**) has buttons to create
   level 1 or level 50 gamebots, 1, 10 or 100 at a time.
2. New bots log in gradually and then play on their own.

The launcher's bot goals settings choose how much bots solo, group or go to RvR at each level
range. You can recruit companion bots in game.

#### How many bots?

Counts are gamebots per realm (Albion / Midgard / Hibernia):

| Bots per realm | Experience |
|---|---|
| **500 / 500 / 500** or fewer | Low population. |
| **1000 / 1000 / 1000** | Regular populated server. Enough level 50 bots to complete all content, and a living world. Going above this may cause server lag or database locks more often. |
| **1500 / 1500 / 1500** | High population. |
| **2000 / 2000 / 2000** | Full server. |
| More than 2000 per realm | Likely impractical. |

With a lot of load, bots may also move more slowly.

There is a hard ceiling from bot name generation. Each realm has its own male and female name
pools, 20,414 unique names in total:
- Albion has 7,496.
- Midgard has 6,494.
- Hibernia has 6,424.

The launcher picks each bot's gender at random. A realm's batch fails once that realm's smaller
pool is used up, so creation stops at about:
- **6,900 Albion**, **5,700 Midgard** and **6,200 Hibernia** bots;
- **about 18,800 in total**.

That means 6000 / 6000 / 6000 isn't reachable, because Midgard runs out first. It wouldn't be
practical anyway.

Handy in-game commands are listed in [QUICK-COMMANDS.md](QUICK-COMMANDS.md). The complete list is
in `ALL SERVER COMMANDS.txt`.

## Updates

Click **Check for updates** in the launcher. START SERVER then reads **GET UPDATE**. Every update is checked against
its SHA-256 before it installs, backs up every file it replaces, and keeps your world. `ROLLBACK.cmd` in the update's
folder undoes it.

## Moving from an older game

**From an older base game (0.36):** run Setup. It brings the game up to date in place, keeping your saves.

**From 0.35 or older:** your old game keeps working and isn't changed. Install a new game with Setup, then bring your
progress (account, characters, items, money, houses and bots) over, following [TRANSFER-PROGRESS.md](TRANSFER-PROGRESS.md).
In short:
1. Stop both games.
2. Run **IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd** in the new folder.
3. Pick your old folder.

## Troubleshooting

| Problem | Try this |
|---|---|
| A download part fails its check | Run Setup again; it downloads only what's missing or broken. |
| Setup or an update says the game or launcher is running | Click STOP SERVER, close the launcher and the game, then try again. |
| ENTER REALM stays greyed out | Wait until the launcher shows RUNNING. The first start can take a few minutes. |
| The game window doesn't appear | Turn on the .NET Framework 3.5 feature (see above), then try again. |
| "Port 10300 is in use" or the server won't start | Another game server is running. Stop it first; only one can run at a time. |
| Windows SmartScreen blocks a file | Choose **More info → Run anyway**. The files are unsigned community builds. |
| Two launchers are open | Use one at a time: both start and stop the same server. Close one before you start the server. |

## Older versions

The 0.3x releases this project started from stay available as records: the guide to their own downloaders is in
[history/PLAY-0.37.md](history/PLAY-0.37.md), their notes are in [docs/history](history/), and their files are kept in
[the base-files repository](https://github.com/chris4aker/OfflineDAoC-base-files/releases).
