# Living Server Beta 1.0: known issues and what's not tested yet

For players. What to expect, what hasn't been checked in game, and known problems. The full list
of changes is in [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-SocialExpansion/blob/main/CHANGELOG-LIVING-SERVER.md).
The Living Server was called the Social Expansion up to 0.37-se.2; Beta 1.0 is that build.

## Always on

These apply even with every Living Server switch off:
- **The endgame runs itself.** Each realm schedules its own raids (its dragon, its epic dungeon,
  Summoner's Hall, Darkness Falls and nine raid dungeons), warbands open keep sieges and defenders
  answer them. `realm_raid_schedule_minutes` = 0 turns the raid schedule off.
- **Safer bots.** Bots walk around monsters far above them and remember where they keep dying.
- **Two game files are fixed in place:** Hall of the Corrupt's navmesh (`zone277.nav`) and, on the
  0.37c new client, eight gate models. The installer changes them only when they are the released
  files, and `ROLLBACK.cmd` puts them back.
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

- **Automated:** every build runs the full server test suite (3,400+ tests) and the installer tests
  before it's packaged.
- **In game:** every feature was played on the owner's PC with every switch on, using the **new
  client** (0.36c, then 0.37c): 300 bots per realm at first, 600 per realm overnight since
  2026-10-19. The **original game client wasn't tried**; it runs the same server, so the features
  should behave the same, but that hasn't been checked.

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
- body language: steps in town and cheers (looks, emotes and greetings were seen in the new client); the
  original client wasn't tried
- the between-pulls group wait (next build) didn't raise the number of bot groups in a 39-minute check

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
- **Bot chat:** tested live with a scripted player (see [bot-voices/SE4-CHECKS.md](bot-voices/SE4-CHECKS.md))
  and in the owner's own play, with every reply that didn't answer fixed. Some replies will still read oddly:
  - A bot that remembers you may ask your level right after you said it ("i would group with you again,
    what level are you now?").
  - Questions with no answer set of their own get general answers ("how much xp to 6?" -> "depends on the
    bonuses").
  - A bot answering your "lfg" prefers bots in your zone and near your level, but one from elsewhere
    may still answer.
  - A bot in the frontier may answer your "inc" with "omw" without coming.
  - Bots don't know what you are doing beyond your words: a "gz" is answered only by a bot that just
    levelled, and other replies guess from the words alone.
  - Chat volume hasn't been tuned for very large rosters (1,000+ bots a realm).
  - In development for Beta 1.1 (the talking update, [bot-voices/TALKING-UPDATE.md](bot-voices/TALKING-UPDATE.md)):
    true-only lines, conversation threads, combat calls, bot tells and /lfg and /trade traffic. They're tested
    without a running world only; its "Not checked in game yet" list is what's left.
- **Added the 0.37c new client after installing this?** Run `INSTALL.cmd` again so the new client's
  launcher gets the Living Server tab and its gate fixes too.
- **Versions:** Beta 1.0 is for 0.37 / 0.37b only, and the installer refuses anything else. When
  shadowofze releases the next update, run `PREPARE-FOR-UPDATE.cmd` before updating (it also puts the
  released game files back), and wait for a Living Server build for that version before installing one
  again.
- **Memory over very long sessions:** Windows handles grow about 1,700 an hour with 600 bots per realm
  (a lock inside each monster's stat table keeps the event it waited on once). That is a few MB of
  Windows memory a day; the server's own memory stays flat (about 1.7 GB live at 1,800 bots).
- **Bots in your own guild (0.37):** inviting gamebots into your guild is now the base game's feature. Those
  bots don't count for your guild's merit, realm points, keep claims or buffs (the base game's choice); bots in
  a bot-run guild still do for theirs. With bot voices on, bots in your guild talk from the voices corpus;
  with them off, they use the base game's own lines. Not played in game yet.
- **Raids:** Darkness Falls, Legion and the other level 70+ encounters are won on numbers: their special
  mechanics aren't scripted for bots, and a raid can still lose. Raided in game so far: the three dragons,
  Galladoria, Summoner's Hall, Darkness Falls, Avalon City, Crystal Cave, Marfach Caverns and Hall of the
  Corrupt. Lord Gildas fell to a live raid on Beta 1.0 (2026-10-22, 180 bots in 61 minutes); the Trollheim route
  fix was proven against the real pathfinding library but hasn't run in a live raid yet. Late Hibernian raiders once stood 33 minutes on the Cruachan Gorge
  border before finding their way; the cause isn't found yet. Fomor City is not raided (no navmesh path to
  Myrddraxis).
- **Moving to 0.37:** `PREPARE-FOR-UPDATE.cmd` was used for a real update (the owner's 0.36c game to 0.37c).
  `carry_social_tables.py` was tested on test folders and databases only.

## Reporting a problem

Open an issue on [GitHub](https://github.com/chris4aker/OfflineDAoC-SocialExpansion/issues).
Include the `FORK_BUILD` line from `runtime\server\logs\server.log` (it names the build and which
features are on) and what you were doing.
