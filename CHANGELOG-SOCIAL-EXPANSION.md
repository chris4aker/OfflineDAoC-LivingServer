# Social Expansion changelog

What this fork adds to shadowofze's Offline DAoC. The base game's own changes are in
[CHANGELOG.md](CHANGELOG.md). The newest version is first.

## 0.36-se.3 — 2026-10-18

Fixes from a three-hour playtest (a new Albion Scout from level 1 to 5, every feature on). Same game
versions as before (0.36 / 0.36b, with or without 0.36c). Install it over 0.36-se.2, 0.36-se.1 or a
clean game.

**Bot chat**
- A bot no longer seems to answer its own /faction line. Its closing "Fair enough. Safe travels." is
  said only after somebody actually replied; before, it went out even when every reply was dropped.
- A bot in a group no longer asks in chat who wants to group "just for the company".
- Asking for a guild below the level bot guilds take members ("lf guild" at level 3) gets an answer
  saying when to come back, once every 10 minutes, instead of silence.
- "where are you?" by `/send` gets the bot's zone, level and class.

**Logs**
- The rez-ask line is written only when a player could have answered it, and a bot's normal release
  is logged at Debug. Together they were about 8% of the playtest's server log.
- `AUTONOMOUS_GROUP_MEETUP_DEATHS` gives the real wait; it always said 900 seconds.

**Tested**
- **Automated:** the full server suite on CI, with new tests for each fix.
- **In game:** the playtest that found these is in the docs; the fixes themselves haven't been played yet.

## 0.36-se.2 — 2026-10-17

Fixes from an audit of the first release. Same game versions as before (0.36 / 0.36b, with or
without 0.36c). Install it over 0.36-se.1 or a clean game.

**Docs said too much**
- "With everything off it plays exactly like 0.36" wasn't true. Talking to bots by name and two
  bot fixes are always on; the README, the zip's README.txt and the docs now say so.
- New [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md): what's always on, what hasn't been seen in game
  yet, and known problems. The zip ships it instead of the internal release plan.
- The README now has **Getting started** (make bots first, which switches to try, where to check)
  and **Undo and uninstall**. It also says plainly that testing used the 0.36c new client only.

**Removing bot guilds**
- `python tools\claude-version\set_bot_guilds.py --purge --apply` (server and game closed) removes
  the guilds bots formed: it releases keeps they claimed, ends alliances they led, moves your
  characters in a bot guild back to the starting guild and deletes the bot guilds. It backs up the
  database first, refuses while the game is running, and changes nothing on a second run. Without
  `--apply` it only shows what it would do.
- With bot guilds off, the server no longer rewrites bot guilds' ranks and dues at every start.

**Fixes**
- `/invite <name>` no longer pulls a bot out of a siege, a relic run, a realm event or a realm raid,
  or off a stable route; you're told why.
- The log line on every bot death (with a stack trace) is now written only when the Living Server's
  rez part is on, and the stack trace only for moves other than the bot's own release.
- Founding a bot guild and recording heard debates now write to the database under the shared
  write lock, and the debate history is written in batches off the game loop.
- A bot guild's invite box is shown from the right thread.
- The voice lines load at server start (in the background) instead of the first time a bot speaks.
- Bot memory and nemesis rows for deleted characters are removed at start, and old one-kill
  nemesis rows every 6 hours instead of only at start.

**Tested**
- **Automated:** the full server suite on CI, plus 8 Python tests for the purge. The purge was
  also run on a copy of a played database (15 bot guilds, 3 alliances, one character moved back).
- **In game:** not yet. These changes haven't been played.

## 0.36-se.1 — 2026-10-17

The first release, for Offline DAoC **0.36 / 0.36b** (with or without the 0.36c new client). It
replaces the server (`GameServer.dll`) and the launcher (which gets a **Social Expansion** tab).
Every feature starts **switched off**. (Corrected in 0.36-se.2: talking to bots by name and the
two base-game bot fixes below are always on.)

**Features**
- **Bot guilds.** Each realm's bots form guilds with a purpose (leveling, social, RvR, raiding,
  crafting). They wear guild names, talk in /gu, group with guildmates and recruit you when you
  ask "lf guild", even from your realm's starting guild. Guilds claim and defend keeps, pay dues,
  earn merit, buy guild buffs and form alliances. You can also /ginvite bots into your own
  guild. See [docs/BOT-GUILDS.md](docs/BOT-GUILDS.md).
- **Bot personalities.** Six types (Leveler, Socialite, Daredevil, Explorer, Crafter,
  No-lifer) and five traits tilt how each bot spends its time. `/personality` on a bot shows
  its type.
- **Bot voices and /faction debates.** 979 lines written for 2002–2003, chosen by realm, class,
  level, situation and personality, in five writing styles. 296 multi-bot /faction scenes, one
  every 30–45 minutes while you play. A busy speaker is waited for, so scenes play to the end.
  See [docs/BOT-PERSONALITIES.md](docs/BOT-PERSONALITIES.md).
- **XP by level range.** Separate player and bot rates for level bands. See
  [docs/XP-BY-LEVEL-RANGE.md](docs/XP-BY-LEVEL-RANGE.md).
- **Living Server.** Bots remember you (greetings, thanks, gossip by name). Dings get grats, and
  server firsts are announced. Dead bots ask you for a rez, and passing healers rez you. Frontier
  bots call enemy groups in /faction. Bots keep nemesis records. Bots log on and off on a
  schedule, take "brb" breaks and now and then go linkdead. The **Camelot Herald** is a news page
  of your world. Each part has its own switch. See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).
- **Talking to bots by name.** `/send <bot>` reaches an online bot of your realm, and it answers
  with a tell. Ask to join and it tells you to invite it: you lead, bots join. `/invite <name>`
  invites a nearby bot (same region, within 6,000 units). `/target <name>` targets a nearby
  friendly bot.

**Fixes to the base game's bots** (also with every feature off)
- A bot that dies 3 times on its way to its group's meetup now counts as a no-show. Before, a
  low-level group could spend the full 15-minute wait dying (about 70 deaths in one case).
- Dead bots no longer jump to their spawn point the moment they die. Their corpse stays where it
  fell until they release.

**New client (0.36c) fixes, in the source only.** The target window's FACE, FOLLOW and STICK
buttons, `/target <name>` and `/assist` now work, and a second `/stick` or `/follow` turns it off.
These are in [source/new-client](source/new-client); this release's zip doesn't include new-client
files.

**Tested**
- **Automated:** 2,911 server tests pass on CI.
- **In game** (owner's PC, new client, 300 bots per realm, every feature on):
  - Seen working: guild recruiting and alliances, debates, frontier alarms, breaks, bot memory and
    gossip, rez asks, a bot rezzing you, grats and server firsts, tells, /invite and /target by
    name, and XP by level for players. Bot XP by level raises bots' XP in game, but the exact
    factor wasn't measured; see docs/XP-BY-LEVEL-RANGE.md.
  - Not yet seen in game: keep claims, guild merit, linkdeads, nemesis chat lines, you rezzing a
    bot, and the meetup fix.
  - The original game client wasn't tried.
