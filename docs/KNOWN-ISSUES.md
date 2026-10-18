# Social Expansion: known issues and what's not tested yet

For players. What to expect, what hasn't been checked in game, and known problems. The full list
of changes is in [CHANGELOG-SOCIAL-EXPANSION.md](https://github.com/chris4aker/OfflineDAoC-SocialExpansion/blob/main/CHANGELOG-SOCIAL-EXPANSION.md).

## Always on

These apply even with every Social Expansion switch off:
- **Talking to bots by name.** `/send <bot>` reaches an online bot of your realm (use its exact
  name), and it answers with a tell. `/invite <name>` invites a nearby bot (same region, within
  about 6,000 units), and `/target <name>` targets a nearby bot of your realm. A bot that is busy
  with a siege, a relic or a realm event won't leave it to join you.
- **A dead bot's corpse stays where it fell** until the bot releases. Before, it jumped to the
  bot's spawn point the moment it died, so it couldn't be rezzed. This applies to every bot,
  including your `/spawn` companions.
- **A bot that dies 3 times on the way to its group's meetup** counts as a no-show, so its group
  goes on without it instead of waiting 15 minutes.
- **Bots read short chat (0.36-se.4).** Their answers to what you say in /say and /faction (which the
  base game has too) now understand "ty", "lol", "brb", "inc", "rez pls" and the like; before, /say
  ignored most of them. An insult has to be aimed at someone to count. Place answers match whole
  words, name class trainers, and are worded like a player.

## How it was tested

- **Automated:** every build runs the full server test suite (2,900+ tests) before it's packaged.
- **In game:** every feature was played on the owner's PC with 300 bots per realm and every switch
  on, using the **0.36c new client**. The **original game client wasn't tried**; it runs the same
  server, so the features should behave the same, but that hasn't been checked.

A three-hour playtest on 2026-10-18 (a new Albion Scout from level 1 to 5, no GM powers) also saw a
bot guild recruit the player, guild merit grow, a bot go linkdead in the player's group, and the
meetup fix drop two no-shows (in the server log).

**Not yet seen in game** (they have automated tests, but nobody has watched them happen yet):
- bot guilds claiming keeps
- `/ginvite` and `/gc remove` on a bot
- the `clock` bot schedule (`session` was played)
- nemesis lines in chat
- you rezzing a dead bot, or a passing bot rezzing you (no healer passed in five deaths)
- grats on your own ding from nearby bots
- the exact bot XP-by-level factor (bot XP does go up with it on; the exact rate wasn't measured)

## Known problems

- **Bot guilds stay in your world when you turn them off.** Their guilds, alliances and any keeps
  they claimed remain until you remove them. To remove them, close the game and server and run
  `python tools\claude-version\set_bot_guilds.py --purge --apply` in your game folder. It backs up
  your database first.
- **Rolling back the install doesn't remove saved data.** `ROLLBACK.cmd` restores the files the
  install replaced. The new database tables and settings stay; the original server ignores them.
  Remove bot guilds first (above) if you used them. If you installed more than one build, run
  `ROLLBACK.cmd` once per build.
- **A fresh world needs bots.** Most features need bots online, and a realm needs at least 8 bots
  of level 5 or higher before it forms a bot guild. Use the **+ Lv.1** / **+ Lv.50** buttons in the
  launcher.
- **New client (0.36c):**
  - Rezzing a dead bot from it hasn't worked in testing (the corpse couldn't be targeted). It's not
    confirmed whether that's the client or slow clicking; the original client wasn't tried.
  - It has no guild window, so a bot guild's member list can't be browsed.
  - `/face` keeps facing your target, and the turn keys do nothing until you type `/face` again.
    `/faceloc` doesn't turn you.
  - A bow fires from the Ranged Weapon key (`,`), not the target window's ATTACK button. There's no
    quiver window, so arrows in a bag can't be moved into the quiver slots.
  - The group window still shows your base class after you choose your class at level 5.
- **Bot chat (0.36-se.4):** reworked and not played in game yet. Replies now follow what a line
  invites, but some will still read oddly:
  - A bot answering your "lfg" prefers bots in your zone and near your level, but one from elsewhere
    may still answer.
  - A bot in the frontier may answer your "inc" with "omw" without coming.
  - Bots don't know what you are doing beyond your words: a "gz" is answered only by a bot that just
    levelled, and other replies guess from the words alone.
  - Chat volume hasn't been tuned for very large rosters (1,000+ bots a realm).
- **Added the 0.36c new client after installing this?** Run `INSTALL.cmd` again so the new client's
  launcher gets the Social Expansion tab too.
- **0.37:** this build is for 0.36 / 0.36b only, and the installer refuses anything else. When
  shadowofze releases 0.37, wait for a Social Expansion build for 0.37 rather than installing this
  one over it.

## Reporting a problem

Open an issue on [GitHub](https://github.com/chris4aker/OfflineDAoC-SocialExpansion/issues).
Include the `FORK_BUILD` line from `runtime\server\logs\server.log` (it names the build and which
features are on) and what you were doing.
