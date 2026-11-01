OFFLINE DAOC: LIVING SERVER - BETA 1.0   (github.com/chris4aker/OfflineDAoC-SocialExpansion)
=========================================================================================

An add-on for Offline DAoC 0.37 / 0.37b (with or without the 0.37c new client) that makes the
world feel like a live 2003 server. It replaces the server (GameServer.dll) and the launcher
(OfflineDAoC.exe, which gets a Living Server tab).

ALWAYS ON (the bots' own game, nothing to switch):
  - the endgame runs itself: each realm raids its dragon, its epic dungeon, Summoner's Hall,
    Darkness Falls and nine raid dungeons on a schedule; a wiped raid regroups and tries again
    with the Epic Special Forces; warbands open keep sieges and defenders answer them
  - safer, smarter bots: they walk around monsters far above them, remember where they keep
    dying, and level in their classic zones first
  - talking to bots by name: /send <bot> gets an answer, /invite <name> and /target <name> work
    on nearby bots, and bots read short chat ("ty", "lol", "inc") and answer like a player
  - a dead bot's corpse stays where it fell until it releases
  - fixed game files: Hall of the Corrupt's navmesh (Lord Gildas's room) and, on 0.37c, eight
    keep and hall gates whose portcullis never rose in the new client and the new client's quest
    marker script (the NPC of your current quest step was coloured as if the quest were in
    progress elsewhere) (see INSTALL)
  - on 0.37c, 14 more new-client scripts from shadowofze's final code: abilities (Sprint,
    Stealth ...) drag onto the quickbar; a Stealth row in the Abilities window, /stealth and
    /hide; SELL in the merchant window (APPRAISE shows but does nothing yet: it needs a newer
    MMOEngine.dll); one PLAY click enters the world once; names hide behind walls; fog per
    region; flickering casting hands; arrow / bolt / poison info. Untested in game so far.
    What is held back and why: docs\CLIENT-SCRIPTS-1.2.md

OFF UNTIL YOU SWITCH THEM ON (launcher -> Living Server tab):
  - Bot guilds .................... docs\BOT-GUILDS.md
  - Bot personalities, voices
    and /faction debates .......... docs\BOT-PERSONALITIES.md
  - Server life: bots remember
    you, grats, server firsts,
    rezzes, frontier alarms,
    nemeses, bot schedules,
    breaks, linkdeads and body
    language ...................... docs\LIVING-SERVER.md
  - XP rate by level range ........ docs\XP-BY-LEVEL-RANGE.md
    (the suggested values keep levels 1-34 worth playing and compress the 35-50 grind:
     you 3x / 5x / 12x, bots 1.5x / 2.5x / 6x for levels 1-19 / 20-34 / 35-50)
  - Camelot Herald (a news page; button in the Living Server tab)

Known issues and what hasn't been tested in game yet: docs\KNOWN-ISSUES.md

INSTALL
-------
1. Close the server, the launcher and the game.
2. Put this folder INSIDE your Offline DAoC game folder (the folder with START OFFLINE DAOC.cmd).
3. Double-click INSTALL.cmd. If Windows warns about an unknown app: More info -> Run anyway.

The installer checks that the game is 0.37 / 0.37b (with or without the 0.37c new client; its
launcher in new-client\runtime is replaced too), refuses while anything is running, backs up
every file it replaces (runtime\server\deployment-backups\fork-build-<time>) and checks every
copy. It also puts three settings helpers in tools\claude-version and these docs in
docs\fork-build-<name>.

It also fixes a few game files in place (listed in content-patches.json). It changes a file only
if it is exactly the released one, checks the result, and backs the original up first; a file
that was changed by something else is left alone and named in the output. ROLLBACK.cmd and
PREPARE-FOR-UPDATE.cmd put the originals back.

Check: start the game as usual. The server log shows a line like
  FORK_BUILD name=1.0-beta commit=... bot_guilds=off xp_rate_by_level=off ... bot_voices=off living=off

Had an earlier Social Expansion build (0.36-se.N or 0.37-se.N)? Just run INSTALL.cmd: your
characters, bots, bot guilds, bot memory and settings stay where they are.

Coming from 0.36 with an earlier build installed? The 0.37 update applies only to the released
0.36 files, and it tells the game's version by the launcher, so:
  1. Put this folder in the game folder and double-click PREPARE-FOR-UPDATE.cmd here. It puts the
     released 0.36 server and launcher back from the installer's backups and checks each one.
  2. Update the game to 0.37 with its downloader (choose 1, Update). Your saves stay where they are,
     the Living Server's tables and settings included.
  3. Double-click INSTALL.cmd in this folder.
This build refuses a 0.36 game.
If you installed 0.37 fresh instead and brought your progress over, the game's transfer leaves out
the Living Server's own tables (bot guilds, bot memory, heard lines). Before turning bot guilds
on, copy them from the old game folder:
  python tools\claude-version\carry_social_tables.py --from "C:\path\to\old\game" --apply

SWITCHING FEATURES ON
---------------------
A fresh world has no bots: add some with the + Lv.1 / + Lv.50 buttons on the launcher's main
screen first (a realm needs 8 bots of level 5 or higher before it forms a bot guild).

Open the launcher as usual (START OFFLINE DAOC.cmd) and go to the Living Server tab. With the
server stopped, tick what you want, pick the XP-by-level rates (Off, Suggested or Custom) and click
Save settings. They apply the next time you start the server. ALL OFF puts it back.
The server life switches are only in the tab (no scripts).

The same switches as scripts, for those who prefer a command line (server closed; Python 3):
  python tools\claude-version\set_xp_by_level.py --player "1-19:3;20-34:5;35-50:12" --bots "1-19:1.5;20-34:2.5;35-50:6" --apply
  python tools\claude-version\set_xp_by_level.py --off --apply
  python tools\claude-version\set_bot_guilds.py --on --apply
  python tools\claude-version\set_bot_guilds.py --off --apply
  python tools\claude-version\set_bot_social.py --all on --apply
  python tools\claude-version\set_bot_social.py --all off --apply

Without --apply they only show what they would change. All of them back up the database first.

UNDO
----
Turning a feature off: untick it in the Living Server tab (or ALL OFF), Save settings, restart
the server.

If you used bot guilds: turning them off keeps their guilds, alliances and claimed keeps in your
world. To remove them (server and game closed; Python 3), in your game folder:
  python tools\claude-version\set_bot_guilds.py --purge
  python tools\claude-version\set_bot_guilds.py --purge --apply
The first shows what it would change; the second does it, after backing up the database.
Your characters in a bot guild go back to their realm's starting guild; your own guilds stay.

Uninstalling: close everything and double-click ROLLBACK.cmd. It puts back exactly the files the
install replaced or fixed (server, launcher, game files). If you installed more than one build,
run it once per build. The new database tables and settings stay; the original server ignores them.

BEFORE THE NEXT OFFLINE DAOC UPDATE
-----------------------------------
Double-click PREPARE-FOR-UPDATE.cmd first. It puts the released server, launcher and game files
back so the update can apply, then install the Living Server build for that version.

DON'T install this build on any other release (0.36 or older, or 0.38 or later): it would
change that release's server. The installer refuses it.

BUILD-INFO.txt names the build, the git commit and the SHA-256 of the files.
