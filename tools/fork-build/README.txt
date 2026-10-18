OFFLINE DAOC - SOCIAL EXPANSION (github.com/chris4aker/OfflineDAoC-SocialExpansion)
==================================================================================

This is a build for Offline DAoC 0.36 / 0.36b, the "Social Expansion". It replaces the server
(GameServer.dll) and the launcher (OfflineDAoC.exe, which gets a Social Expansion tab), and adds
these features, all OFF until you switch them on:

  - XP rate by level range ........ docs\XP-BY-LEVEL-RANGE.md
    (the plan's values keep levels 1-34 worth playing and compress the 35-50 grind:
     you 3x / 5x / 12x, bots 1.5x / 2.5x / 6x for levels 1-19 / 20-34 / 35-50)
  - Bot guilds .................... docs\BOT-GUILDS.md
  - Bot personalities, voices
    and debates ................... docs\BOT-PERSONALITIES.md
  - Living Server: bots remember
    you, grats, server firsts,
    rezzes, frontier alarms,
    nemeses, bot schedules,
    breaks and linkdeads .......... docs\LIVING-SERVER.md
  - Camelot Herald (a news page;
    button in the Social
    Expansion tab) ................ docs\LIVING-SERVER.md

Always on, even with every switch off (docs\KNOWN-ISSUES.md):
  - talking to bots by name: /send <bot> gets an answer, /invite <name> and /target <name> work
    on nearby bots
  - a dead bot's corpse stays where it fell until it releases (all bots, companions included)
  - a bot that dies 3 times on the way to its group's meetup stops waiting for it
  - bots read short chat ("ty", "lol", "inc") and answer place and trainer questions like a player
Everything else plays like the normal 0.36 while its switch is off.

Known issues and what hasn't been tested in game yet: docs\KNOWN-ISSUES.md

NEW CLIENT (0.36c): the new client runs the same server, so this build applies to it too.
It was tested in game with the new client; the original client wasn't tried. The new client's
own fixes from this release (target window buttons, /target <name>, the /stick toggle) are in
the source code only: this build doesn't change any new-client files.

INSTALL
-------
1. Close the server, the launcher and the game.
2. Put this folder INSIDE your Offline DAoC game folder (the folder with START OFFLINE DAOC.cmd).
3. Double-click INSTALL.cmd. If Windows warns about an unknown app: More info -> Run anyway.

The installer checks that the game is 0.36 / 0.36b (with or without the 0.36c new client; its
launcher in new-client\runtime is replaced too), refuses while anything is running, backs up
every file it replaces (runtime\server\deployment-backups\fork-build-<time>) and checks every
copy. It also puts three settings helpers in tools\claude-version and these docs in
docs\fork-build-<name>.

Check: start the game as usual. The server log shows a line like
  FORK_BUILD name=0.36-se.4 commit=... bot_guilds=off xp_rate_by_level=off ... bot_voices=off living=off bot_schedule=off

UNDO
----
Turning a feature off: untick it in the Social Expansion tab (or ALL OFF), Save settings, restart
the server.

If you used bot guilds: turning them off keeps their guilds, alliances and claimed keeps in your
world. To remove them (server and game closed; Python 3), in your game folder:
  python tools\claude-version\set_bot_guilds.py --purge
  python tools\claude-version\set_bot_guilds.py --purge --apply
The first shows what it would change; the second does it, after backing up the database.
Your characters in a bot guild go back to their realm's starting guild; your own guilds stay.

Uninstalling: close everything and double-click ROLLBACK.cmd. It puts back exactly the files the
install replaced (server and launcher). If you installed more than one build, run it once per
build. The new database tables and settings stay; the original server ignores them.

SWITCHING FEATURES ON
---------------------
Open the launcher as usual (START OFFLINE DAOC.cmd) and go to the Social Expansion tab. With the
server stopped, tick what you want, pick the XP-by-level rates (Off, Suggested or Custom) and click
Save settings. They apply the next time you start the server. ALL OFF puts it back.
The Living Server switches are only in the tab (no scripts).

The same switches as scripts, for those who prefer a command line (server closed; Python 3):
  python tools\claude-version\set_xp_by_level.py --player "1-19:3;20-34:5;35-50:12" --bots "1-19:1.5;20-34:2.5;35-50:6" --apply
  python tools\claude-version\set_xp_by_level.py --off --apply
  python tools\claude-version\set_bot_guilds.py --on --apply
  python tools\claude-version\set_bot_guilds.py --off --apply
  python tools\claude-version\set_bot_social.py --all on --apply
  python tools\claude-version\set_bot_social.py --all off --apply

Without --apply they only show what they would change. All of them back up the database first.

DON'T install this build on any other release (0.35 or older, or 0.37 or later): it would
change that release's server. The installer refuses it.

BUILD-INFO.txt names the build, the git commit and the SHA-256 of the files.
