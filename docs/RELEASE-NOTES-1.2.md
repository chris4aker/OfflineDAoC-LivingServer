# Living Server Beta 1.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for `v1.2-beta`, "Where Legends Wake". It is a draft for
the owner's approval: the "How it was tested" section is finished after the owner's played check. In the file table the
Setup row must keep exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the
exe's SHA-256. The zip's SHA-256 is filled in by hand when publishing. Publish it as Latest, not as a pre-release: the
launcher's update check reads GitHub's latest release, which leaves pre-releases out. Lines marked "[if it lands: ...]"
describe work still on its own branch: keep them only if it is merged before the tag, and drop the marker. Nothing here
names the Easter eggs or the project's internal vocabulary.

---

### Where Legends Wake

1.1.2 gave the raids their first five boss tactics. 1.2 turns the epic encounters up to full strength, with bots that
have to survive them, and gives every dungeon, dragon and boss a story you can find in the world. The bots also play
with you now: they invite you, lead groups you can simply follow, and notice you in RvR the way a player would.

It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client, like 1.1 to 1.1.2.

### The epic encounters, at full strength

- **The bosses' own rules are back on.** In earlier versions many boss mechanics were softened or switched off so bot
  raids could win. 1.2 turns them back on: the horsemen's tether heal, Morbus's swarm, the Skeletal Sacristan's patrol,
  Soul Reckoner's souls and small room, Aroon's Scaiths in order, the Earthmenders, Beatha healing Xaga, Kvasir's cold
  heal, King Tuscar and Queen Kula healing each other, the dragons' messengers, and more. Of the 18 restored rules that
  are now server settings, 15 are on. Funus's class and weapon puzzle, the original fire patches and Govannon's full
  300,000 health stay off.
- **They hit bots too.** Bosses used to choose their throws, glares, debuffs and "random target" spells from real players
  only. Now they choose from the whole raid: you, your companions and the gamebots. With bots around you are one target
  among many, never chosen more often than before.
- **Thrown bots get back.** The dragons' wing gust is back, and most throws, teleports and jails take bots as well. A thrown
  bot falls and takes the same fall damage you would. It walks back to its party when it can; otherwise it recalls to its
  raid after 5 seconds. Most thrown bots are recalled: in a long run, 4 in 5 were.
- **No floods of adds.** A bot raid can be bigger than a 2003 raid. A few bosses that make adds or heals per raider now
  count at most a period-sized raid, so a big bot raid faces what a full raid of players did.
- **Your companions stay at the fight** when a boss throws you, instead of being dragged along.

### Raid tactics for the real puzzles

A boss with a puzzle can't be beaten by hitting harder. The raids now have a plan for many of them. In tests, Govannon
and Spindler Broodmother have not been beaten yet. The encounter catalog lists 40 encounters with a raid tactic that is
new or reworked in 1.2. Some of them:

- **Funus (Caer Sidi):** his class and weapon puzzle is off by default. Do not turn it on yet: the raid's tactic for it
  is not finished.
- **The Earthmenders (Galladoria):** each one takes damage only from its own classes, so each raider takes one its class
  can hurt, and the Earthmenders die before the primal.
- **Spindler Broodmother:** casters and archers kill each newborn spindler as it comes; the small spindlers that burst
  from her body are cleared before the raid moves on.
- **Grand Summoner Govannon:** in his demon form the casters, archers and healers stand clear of his target. His
  sacrifice and demon are killed on their way to his altar.
- **Tuscaran Glacier:** the raid kills Kula first while one tank holds the King, nobody casts cold at Kvasir, and a team
  clears the Jailer's tunnel guards, so whoever he jails walks out.
- **The dragons:** casters and archers run down the messengers, and the raid steps away from a stare or a breath.
- **Easmarach, Olcasgean, the Sacristan, Soul Reckoner, the Crypt Lord, Orshom's fire, Dra'argus's sphere** and others
  each have a plan of their own. Your party calls each mechanic in /group as it happens ("all on kula", "out of the fire").
- **Raid leaders call it, and a realm learns a boss.** [if it lands: For Xanxicar's stomp, the raid leader now calls when
  to get out, and each bot reacts in its own time, some late. A realm that is new to him loses more raiders to the stomp;
  over its attempts it learns him and loses fewer. This is still being tuned.]

**Some secrets take a realm time to learn.** A raid of bots may still lose these fights for a while: **Spindler
Broodmother** and **Grand Summoner Govannon**. If a fight is too hard on your world, each restored rule is a server
setting of its own and can be switched off alone.

### A world whose stories come alive

- **Every place has its story.** 107 places, 578 bosses and 4 realm histories (Albion, Midgard, Hibernia and the places
  no realm holds), from the dragons and epic dungeons down to the named monsters of every zone.
- **Checked against Mythic's own words.** Where Mythic told a story, it is kept, in our own words, with its source. 832
  Camelot Herald articles from November 2001 to February 2005, read from the web archive, were checked against the
  lore, and where ours disagreed with Mythic's, Mythic's version won: Dartmoor's history, Gjalpinulva's mother, the Legendary
  Afanc's return and Krondon's ogres among them. Where Mythic told no story, the Living Server wrote one. Every compass
  direction in the lore was checked against the world.
- **Where you meet it:**
  - **Loremasters** in each capital: Aldwin in Camelot's library, Sigrun at Jordheim's temple, Eithne by the bards of
    Tir na Nog. Ask them about the [places] of your realm.
  - **Lore books**, one per place, read a page at a time. The loremasters copy them for a silver, and now and then one
    turns up on a monster near the place it tells of. They never crowd out real loot.
  - **The Herald:** each raid in Realm Records ends with a sentence from its boss's story.
  - **The Bestiary**, a new Realm Records page: every boss of your realm's places, with what raid leaders say about it.
    Its full story opens once your realm has brought it down. Raids won before 1.2 count.
  - **`/lore`** in either client. In the new client the journal's LORE button opens it in its own window.
  - **Bosses speak** their own lines as a fight starts, at their phases, at their big moments and when they die.
  - **Short lines on screen** the first time each day you enter a dungeon, when you step into a boss's lair, when a
    dragon takes to the sky, and at a realm's first kill of a boss. At most one every 30 seconds, and realm news always
    comes first. **Atmosphere lines** in the launcher's Living Server tab turns them off.
  - **Bots know the stories.** A raid leader tells the realm what the boss does as the raid sets out. Ask a bot "whats in
    krondon?" and it answers from the lore.

### Bots that play with you

- **Bot leaders invite you.** The leader of a gamebot party near your level may send you a tell and an invite. Yes puts
  you in its group, and the bot leads: the camp, the pulls, the breaks. You travel yourself; it waits for you. Unasked
  invites come at most once every 45 minutes. `/lfg`, or a tell like "room for a cleric?", gets you one sooner.
  `/invites off` stops them.
- **Take the lead, or hand it back.** `/lead` makes you the leader. `/lead bot` hands it to a gamebot.
- **Bot-led parties carry on.** When someone leaves, the leader looks for a replacement in /lfg. It calls its own pulls,
  and it ends at a natural break with "last pull then town".
- **Raid leaders invite you.** When a dragon or epic dungeon raid forms in your realm and you are level 50, its leader may
  offer you a spot, once per raid.
- **Loot in a group with gamebots, whoever leads:** an upgrade for you goes to you, an upgrade for a bot goes to the bot
  that needs it most, and the rest is shared with two turns for you. Coin counts you twice. **Your loot share** is a
  launcher setting.
- **RvR perception.** Gamebots in the frontier, battlegrounds and Darkness Falls used to see you from 5,000 units, all at
  once. Now they notice you by chance: always within 1,000 units, never beyond 3,500 on their own. In the seeded
  simulations, the median time for a bot to notice you is about 1.5 seconds at 1,500 units and 20 seconds at 3,400.
  At most 3 gamebots of a realm look for you each second; the rest hear it when one calls it.
- **Replies that don't give bots away.** Bots answer you first: a tell, or a line that names a bot, always gets an answer,
  however busy the channel. Answers have their own budget, so on a busy channel bots stop answering each other before they
  stop answering you. A bot no longer claims to be another class, role or level. `/lswhy me` shows why bots answered your
  lines, or didn't.
- **Quieter /faction on a small server.** With 10 gamebots a realm, /faction ran about four times faster than its own
  budget. In the automated playtester's runs (10 gamebots a realm, two half-hour listens each, on a development machine) it went from 1.20 and 1.33
  lines a minute to 0.87 and 0.97. A full server is not quieter: its channels were rebalanced, not cut.

### Questing and battlegrounds

- **The epic chains are linked on every install**, by the server as it reads the quests. Nothing is written to your
  world, and no Python is needed.
- **Repeatable quests are offered again**, deliveries in quests without a collect column can be handed in, and a
  collection quest takes its item whatever the case of its name.

### Movement that doesn't look scripted

- **Flanking.** A melee gamebot that knows a back or side style steps behind or beside a monster that is fighting
  someone else, and swings that style. It only walks where the navigation mesh says it can. Never for tanks, companions,
  bots in a group you lead, raids or epic encounters.
- **Positional styles.** Bots used to swing front styles even when they stood behind a boss. Now they use their side,
  back and front styles when they are really there.
- **Smarter style choice.** Melee gamebots chain their styles, use reactive styles after an evade, block or parry, and
  stop spamming taunts unless they tank for a group.
- **No more jerking once a second** in a moving party.
- [if it lands: **Circling and stepping.** Melee bots circle and adjust in a fight, and casters step between casts.
  Stops and turns no longer snap to a point. Bots idle at the bank and the usual crowd spots in town.]

### Fixes from other forks of Offline DAoC

Each is credited to its author.
- **Two Darkness Falls exits work:** the one by Legion's hall and the one near the Midgard boss now take you home (by
  rambodolphin13). Nothing is written to your world.
- **Smarter style choice** (by rambodolphin13).
- **The flank step** (by shadowofze, from his OfflineDAoC-True165).
- **No more jerking in a moving party** (by Nikse77, via rambodolphin13).
- **New client (0.37c): skills on the quickbar, Stealth in the Abilities list, and a SELL button at merchants**
  (shadowofze's own 1.2 script fixes, installed only over the released scripts; ROLLBACK.cmd puts them back).

### Other changes

- **Setup retries a busy download server.** When the download server answers "busy" (429, 500, 502, 503 or 504), Setup
  tries again by itself after 30, 60, 120 and 240 seconds, keeping what it already downloaded.
- **The loading screen says LIVING SERVER**, and upgrades from 1.1, 1.1.1 and 1.1.2 get it too.
- **Commands typed in the server window work again.**

### Install

- **Have 1.1.2:** the launcher's **Check for updates** finds 1.2. START SERVER reads **GET UPDATE**: Install, Later or
  Skip this version. This is the first release to come that way.
- **New to Offline DAoC, or adding Living Server:** run `LivingServer-Setup-1.2-beta.exe`. Windows SmartScreen may say
  "Windows protected your PC" because the exe isn't code-signed: click More info, then Run anyway.
- **Have 1.1, 1.1.1 or 1.1.2:** run the Setup, or unzip `OfflineDAoC-LivingServer-1.2-beta.zip` into your game folder and run
  `INSTALL.cmd` with the game and server closed. Your characters, bots, guilds and settings stay.
- **To undo:** `ROLLBACK.cmd`. It puts every replaced file back; it never puts your database back, because that would
  undo your play since.

Real loot, the Realm Exchange, stablemaster routes, selling at natural breaks, bots that earn their armor and Darkness
Falls open to solo bots are untouched.

### How it was tested

A feature counts as working here only once it has been seen working on a real install. Everything else is "tests
only".

- **Automated (tests only):** [filled in from this release's CI run: the server suite's count, and the Windows launcher
  and installer tests]
- **Bot raids in a test world (tests only, bots only):** Easmarach killed (build 8ab2279e), Soul Reckoner killed
  (4cc22ab4) and Torst killed (8ab2279e). Govannon was not beaten in about 12 runs, and Spindler Broodmother not in
  about 9. Funus was beaten once, only with fixes that are not in this build.
- **On a real install** (an overnight run of development build 76600af7, bots only, 7.8 hours): Fornfrusenen, the
  Council Vagn and Nokkvi, Torst and Hurika were killed; Tuscaran Glacier was lost at Icelord Agmundr. Bot raids skipped Scurceol Hyrde: they could not shoot his orbs.
  In the battlegrounds, 168 of 169 storms on a keep lord failed and no keep changed hands. The server stalled for 4 to
  6 seconds three times.
- **Played:** [owner: one line on the played check, filled in before publishing]
- Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
  `FORK_BUILD` line from `runtime\server\logs\server.log`.

### Known issues (it's a beta)

- **Darkness Falls' raid bosses, Legion and the hardest level 70+ encounters are not implemented for bot raids.** Legion
  has a few new rules (wounded raiders step out of his reach, no bladeturns near him), but the raid does not play his
  platform, the way down or his add waves.
- **Most of 1.2 has not been seen in a played game yet.** The new tactics pass their automated tests, and some have been
  run by bot raids on a test server, but none in a raid with a player in it. The lore, the loremasters, the books and
  the bot-led groups are checked by automated tests. Real play may still find cases they miss.
- **Some fights are harder than before**, and a raid of bots may lose them for a while (see above).
- **/faction can get too busy.** On an evening with about 600 bots a realm, it ran well above its ceiling, up to 20
  lines a minute, with bursts of 10 to 16 reactions. Lower **How much they talk** in the Living Server tab if it is too
  much.
- **A very busy server can stall for a few seconds.** In an overnight run with about 600 bots a realm, it stopped for
  up to 6.2 seconds.
- **Some gamebots wait a long time at frontier teleporters.** In one night, 368 times a gamebot stood 15 minutes waiting
  for one before the server moved it on.
- **Battlegrounds are an endless fight.** Bots storm the keep lord again and again and almost always fail, so the keeps
  don't change hands and the same bots die over and over.
- **Bot-led groups:** gamebots don't share your horse or boat; the leader takes its own and waits at the other end. The
  break at the end of a session ends the party: it doesn't walk to town and back with you.
- **New client:** APPRAISE stays hidden, and you don't see yourself as see-through in Stealth yet. Both need a newer
  network client than this update installs.
- **More:** [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md).
  Every change, with its numbers: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

### Coming next

- **1.2.x, play with friends (experimental):** a private game for a few friends in your bot world.
- **1.2.2:** a Linux build of the server, a launcher and the new client.
- **2.0, the complete realm:** live events, the guild package, bots with alts, friends and grudges, crafting and a
  server with a past.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2-beta.zip` | (owner: the zip's SHA-256, filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC) and
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). Fixes ported from the forks of rambodolphin13 and Nikse77, and
from shadowofze's OfflineDAoC-True165; the AI cost meter builds on lometur's HearthDAoC and stefanrows's bot profiler.
Boss lore checked against Mythic's Camelot Herald. Reports from the community on GitHub and r/daoc. Directed and
play-tested by [chris4aker](https://github.com/chris4aker), coded with Claude (Anthropic). GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
