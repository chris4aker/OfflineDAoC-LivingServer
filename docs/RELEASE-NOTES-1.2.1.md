# Living Server 1.2.1: release notes (draft)

The text below the line is the draft of the GitHub release notes for 1.2.1, a fix release on top of 1.2. It lists only
what changed since 1.2; [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md) still describes the whole of Living Server.
Publish it as Latest, not as a pre-release: the launcher's update check reads GitHub's latest release.

Release plan: [docs/1.2.1/RELEASE-PLAN-1.2.1.md](1.2.1/RELEASE-PLAN-1.2.1.md). The owner can't run PC checks before
this release, so almost everything below is **tests only** (AGENTS.md: a feature counts as working only once an owner
PC log shows it).

Before publishing:
- **Raid results:** replace the `@@RAID_RESULTS@@` line in "Bosses and raids" with what the raid branches merged
  (#428, #430, #431, #432 or their successors), from the changelog's **Bosses** lines, each one **tests only** unless
  an owner PC log shows the win. `publish_release.py notes` refuses the notes while the marker is there.
- **Uthgard's drops:** filled in (2026-11-22). The Living Server package is built by CI (server, launcher, scripts) and
  installs over the 0.37 world; release.yml, installer.yml and Setup build no release world, and no
  `build_release_037.py --uthgard-drops` world was built for 1.2.1. If one is built and shipped as a world update before
  publishing, change the bullet back to "This release's world update adds them." 
- **Seen in game:** if an owner PC log shows a 1.2.1 change before publishing, move it from "Tests only" to "Seen in
  game", with the build and the log line.
- **Automated results:** update the CI numbers in "How this was tested" to the release candidate's run, naming its
  commit.
- **Build name:** `ForkBuildInfo.BuildName` must read `1.2.1` and the changelog heading `## 1.2.1 — <date>`
  (`tools/release/check_versions.py --tag v1.2.1` checks both).
- **Files table:** the Setup row must keep exactly `(filled in when the exe is built)` and the zip row exactly
  `(zip SHA-256: filled in by hand at publish)`; the workflows replace those.
- Nothing below the line may contain a parenthesis that opens with the word "owner": the publish check reads it as a
  placeholder left in.

---

# Living Server 1.2.1

*Offline. Never alone.*

> Living Server is an unofficial, free, non-commercial fan project. It is not affiliated with, endorsed by or
> connected to Electronic Arts or Broadsword Online Games. Dark Age of Camelot belongs to them.

A fix release for **1.2**: bots that fall when they die, rezzing a dead bot in the original client, hired companions
at your level, experience for enemy gamebot kills, RvR and crafting fixes, and a lot more lore.

It installs over Offline DAoC **0.37** and plays on the **original game client**. Existing 0.37b and 0.37c installs keep
working and get these fixes, but from now on new Living Server features are made for the original client only.
Everything Living Server does is still described in the [1.2 release notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md).

## How to read these notes

- **Seen in game** means seen on the owner's PC, on screen or in its server log, on the build named.
- **Tests only** means unit tests, CI, simulations or test-world bot runs. It has **not** been seen in a real game yet.
- **Almost all of 1.2.1 is tests only.** The owner could not play-test this release before it shipped, so it went out
  with every change that passes CI, each one labelled. If something looks wrong, there is a switch or a check for it
  below, and a log line to send back.

---

## 1. Seen in game

**Nothing that is new in 1.2.1 has been seen in game yet.**

What 1.2.1 builds on, seen on the owner's PC on 1.2 builds and not changed since:

| Area | What was seen | Build |
|---|---|---|
| Companions | `/hire`, a companion following you, gear handed over and visible, `/hire train` by line number, a companion summoned at login | `5ddd003`, `b1b6846` |
| RvR | A battleground keep taken: Caer Caledon by Albion (`RVR_KEEP_TAKEN`) | `43c43aa` |
| RvR | A Hibernian siege column walking the Cruachan Gorge seam single file, none stuck; Albion's muster at Castle Sauvage on walkable ground | `aa537ce` |
| Raids won | Gjalpinulva, Golestandt (after one wipe) and Cuuldurach (after two wipes) | `43c43aa` |

Some 1.2.1 fixes answer problems that an owner PC log showed. Those logs show the **problem**, not the fix; each
such item below says which build showed it.

---

## 2. Tests only

Everything in this list passed CI, and none of it has been seen in a real game.

### Death and rez

- **Bots fall when they die in the original client.** A companion, hired companion or gamebot now falls and lies
  like a monster's corpse instead of standing at zero health. It stays dead to the server exactly as before.
  - Raised, it stands up where it lay.
  - Released, it appears at its bind point, with no second body left behind.
  - The new client already showed the fall and gets exactly what it got before. The server tells the two clients apart
    by the first packet they send (`CLIENT_HANDSHAKE ... client=original` in the log).
  - **Safety switch:** **Bots fall when they die** in the launcher's Living Server tab (server setting
    `bot_death_animation`), on by default. **Turn it off and every packet is as it was in 1.2.**
  - The CI scenarios check the packets the server sends. **Whether the old client really draws the fall is not
    checked yet** (issue #425).
- **Rezzing a dead bot in the original client.** That client won't let you select a dead bot, so a rez answered "You
  must select a target for this spell!". Now a resurrection cast with nothing dead selected goes to a dead bot of your
  realm in range:
  - first, the one you named with `/target <name>`;
  - otherwise, one from your group;
  - otherwise, the nearest.
  - A line tells you who it goes to ("You have no dead target selected: your spell goes to ...").
  - Works for companions, hired companions and gamebots. Their bodies still can't be clicked.

### Bots and companions

- **Companions no longer get stuck on Darkness Falls' entrance stairs (#438).** Going down the stairs from a realm's
  entrance, a companion could stop on one step and stay there. It now steps off each ledge and lands on the next, as
  gamebots already did. Albion, Midgard and Hibernia alike. **Tests only.**
- **GM `/jump <bot name> to me` works (#438)**, for companions, hired companions and gamebots, and so do `/jump
  <name> to <name>` and `/jump <name> to x y z`. **Tests only.**
- **Hired companions join at your level**, not level 5, with their spec points spent in their class's build (respec
  stays free). Above level 5 they arrive in plain merchant armour, with a weapon and, where their class carries one, a
  shield.
- **Companions hired on 1.2 catch up once**, the next time they come to you. They keep their gear, bag, coin, realm
  points and trained lines; only their new points are spent, and basic gear goes only into empty slots. The training
  weapon counts as empty and is swapped for a merchant weapon. Gear that can't be found is tried again at the next
  summon.
- **`/hire train` reads line names (#397):** `/hire train 1 smite`, `/hire train 1 mind twisting`, or the start of a
  name, with an optional level to train to (`/hire train 1 smite 10`). Line numbers still work. An unclear name lists
  the companion's lines and what to type.
- **The trainer window shows the next skill at the top of each line**, so it no longer slides below the window (#397).
- **New companion names:** the hire window no longer suggests the names of the lore's own heroes. Companions already
  hired keep their names.
- **No pet cap for players.** An Animist can place as many turrets as power and casting allow, and a Theurgist's pets
  have no cap. Companions and gamebots keep their limits.
- **Bot parties take one more on the way (#423).** A bot party that set off with a seat free looks for the role it
  lacks (healer, else tank, else damage) two minutes after leaving and then every ten minutes, for five minutes each
  time. It can pick a free gamebot, or you through /lfg or a tell. Raid, RvR and regrouping parties never do this.
- **Clicks on bots are logged (#424)** so a click that doesn't select a bot can be traced. No gameplay change.

### Rewards and loot

- **Enemy gamebot kills give experience (#396).** They only ever paid realm points. A kill now pays what a player kill
  pays: experience as for a player of its level (shared by damage, at most 125% of your own worth, with the keep
  bonus) and, outside battlegrounds, bounty points. A bot killed again within five minutes is still worth nothing.
- **Named drops in gamebot-led groups.** A kill you finished in a group a gamebot leads dropped none of the monster's
  own loot (coin and random items still dropped). It drops again.
- **Uthgard's drop tables: not in this release's world.** The tool that adds the classic-era drops measured on the
  Uthgard server (hides, teeth, gems, vendor loot, period gear) to monsters with no loot of their own is in the release
  tools, but 1.2.1 has no world update: a new install gets Offline DAoC 0.37's world as before, and an update changes
  the server and the launcher only. The drops come with the first release whose world is built with them; random magic
  items and coin stay as they are.
- **Quieter start-up log:** loot rows that name a missing item are one `LOOT_ITEMS_MISSING` warning per loot source,
  with a count.

### RvR and travel

- **Battleground keep lords take damage from the storm (#416).** A keep lord takes no damage from anyone more than
  400 away, but stormers struck him from up to 900, so casters, archers and healers in his room did nothing. They now
  strike only from within 350 and walk in otherwise. Keep, guard and lord levels are unchanged.
- **Frontier sieges too.** Attackers now walk to within 350 of a keep lord or 250 of a tower lord before striking,
  and never step back from a lord.
- **Roaming eight-man groups find fights (#417).** Their patrols favour the parts of the frontier where enemy RvR bots
  are, and an eight chases an enemy group the planner is hunting within 10,000 in its own region instead of walking
  past it.
- **Frontier porters (#418).** A gamebot without a medallion now goes to the nearest porter that sells its medallion,
  instead of waiting at one that doesn't until the 15-minute watchdog moved it. A wait past 5 minutes is logged.
- **Albion bots stuck leaving Castle Sauvage** restart from the army's muster at the border keep (problem seen on
  `aa537ce`).
- **Gamebots that logged out beside another realm's frontier keep** log in at their capital, as players do (problem
  seen on `aa537ce`: 11 Midgard bots in Cruachan Gorge).
- **A keep defence with no defenders left stands down** instead of marching empty (problem seen on `aa537ce`: a
  defence of Dun nGed marched with 0 bots).
- **A GM's `/jump` into an enemy keep's area** is no longer sent to the bind point (problem seen on `aa537ce`).
- **The log checker shows how far each regroup call spread (#419).** The regroup relay itself is unchanged: 17.4% on
  `9a173f4` is under the 20% bar.

### Crafting (#422)

- **A piece you ordered from a crafter bot is handed over.** A made commission was never handed over or announced and
  lapsed after six hours. Now, while the crafter's break lasts, it opens the trade if you are near, or tells you once
  where to collect it.
- **A stock trade window that can't open** makes the crafter say why, and the deal is kept. A bot never holds a trade
  for a player who is already trading.
- The economy log counts the crafted bot listings of level 20 and up, to show whether sellable work reaches the Realm
  Exchange.
- No trade with a crafter bot has been finished in a real game yet (tried on `b1b6846`, before these fixes).

### Bosses and raids

- **Elro the Ancient's third treant** now comes. His script asked for health under a quarter and over three-tenths
  at once; it now comes between three-tenths and a quarter of his strength left, and his fight advice names all three
  moments.
- **Raid fixes, all tests only** (each worked out from the owner's earlier raid logs and checked in a simulation in CI; no
  boss is changed):
  - **Caer Sidi, the Crypt Lord (#407):** when the long wait on his court runs out, the raid still waits for his patrol
    to be clear before it pulls, so he no longer walks in with his whole court.
  - **Galladoria, Spindler Broodmother (#408):** more parties go after her newborns (7 of 13 instead of 5).
  - **Tuscaran Glacier (#409):** while Queen Kula lives, nothing but the King's own target hits King Tuscar (no pets,
    no area spells), so the raid no longer heals her; after she dies, casters keep clear of his hammers.
  - **Summoner Roesia (#410):** a fresh poison is cured even while someone in the party is hurt, the lowest-health
    poisoned raider first, and cures go out twice as fast. In the simulation the raid wins once it grows to about 93;
    the first, smaller attempt can still lose. Grand Summoner Govannon is not simulated.
  - **Trollheim, Nosdoden (#411):** a retry after a wipe restarts the stall clock, so the raid is no longer ended in
    its retry's first minutes.
  - **Marfoirwort (#412):** his target holds close, melee fight from behind him, everyone at range stands clear and no
    pet attacks him, so his life-leech heals him from one raider instead of about 25.
  - **The Green Knight (#412):** casters and healers stand together away from his target, and the melee peel his trees
    off them. The simulation wins at about 80 raiders or more; at the period's 50 it still loses.
  - **Legion (#413):** the wounded step out toward the raid's healers instead of away from them, so his souls no longer
    find them alone.
  - **Golestandt and Cuuldurach (#415):** a raider thrown far by a wing-gust stays part of the fight instead of being
    held at the staging ring.
  - **Behemoth, Fomor City and Morgana (#414):** still no bot raid (signed off as exceptions on 2026-11-15).
- **Every raid fix is tests only**, worked out from the owner's earlier raid logs and from simulations. The raid issues
  (#407 to #415) stay open until each raid is seen won on the owner's PC. See "Known issues".

### Lore

- **Every great foe has its book (#399-#402).** All 157 epic bosses have at least four chapters, a 300 to 500 word
  story and two to four book pages written as documents of their world: letters, tallies, slates, daybooks, triads.
  The Behemoth has a fifth chapter.
- **Major bosses and places at full depth (#403, #404).** Midgard's and Hibernia's major bosses, and the places of all
  three realms and the proving fields, have fuller book pages (each page 100 words or more), facts and loremaster
  topics.
- **The realms' own legends ride with their heroes.** Arthur's knights in Albion; Starkad, Ragnar's sons and Beowulf
  in Midgard; Ferdiad, Scáthach, the Red Branch, Medb and Fergus in Hibernia. Each realm's present-day captains,
  priests and tellers recur across places and bosses.
- **Nothing breaks the fourth wall (#399).** About 860 lines that named real companies, websites, patches, years or game
  software are reworded in-world.
- **Fight advice in-world.** Every boss's "What the raid leaders say" and its bot lines, about 3,800 strings across 578
  bosses, are written without game terms: strength as a tier, distance in paces, times exact. Over 300 tips were
  checked against the bosses' code, and every one that was wrong is fixed.
- **Place lore in-world too.** A place's level range now says who it suits ("hunting for seasoned bands").
- **Contradictions settled.** Independent reviews checked the new pages against the old chapters and place lore and
  fixed what disagreed by rewording the new text. Nothing was cut.
- **Bot names give way to the lore.** A few names the lore uses for its own characters are no longer given to new
  gamebots. Bots already in your world keep their names.
- Lore is checked by automated rules in CI. Reading it in game (loremasters, books, `/lore`) is tests only for these
  new pages.

### Settings

- **Bot schedule off by default.** `bot_schedule_mode` now defaults to off: the whole roster is online. A world that
  ran 1.2 with the untouched real-clock default moves to off once; a schedule you chose in the launcher is kept. Bots'
  breaks are a separate setting and stay on.
- **New switch: Bots fall when they die** (`bot_death_animation`), on. See "Death and rez".
- The launcher's chat setting reads "HOW MUCH BOTS TALK", as the docs do.

### Also

- **/faction quiet minutes** fill back to about 5 lines a minute, while the cap of 7 still holds (problem seen on
  `43c43aa`: 13 of 39 minutes under 5).
- **`/target <name>`** finds a nearby NPC for a player, such as Caer Caledon's guards, not only for a GM.
- **Stability:** the casting line-of-sight gap behind 1.2's casting crash is closed (crash seen on `43c43aa`); a packet
  queued while its queue is drained waits instead of raising "DrainTo in progress" (seen once on `43c43aa`); the missing
  stock merchant item templates at start-up are one warning with a count.
- **Linux:** the launcher's tooltip bleed-through and alt-tab crash under Wine (#393), and a Linux start script that
  installs launcher updates and keeps the game fullscreen (#395). Neither has run under Wine since the review changes.

### How this was tested

- **CI on `d56adeda`** (fix/1.2.1, 22 Nov): the server suite, 9,258 passed, 0 failed, 1 skipped; the launcher, new
  client, encounter catalog and version checks passed.
- **Playtester on `b644529a`** (22 Nov): a real server with a scripted original-client player. The `bot-death`,
  `bot-corpse-target` and `bot-rez` scenarios passed: the death packets arrive in order with no "alive" update while
  the bot is dead, `/target <bot>` selects the corpse, and a rez with nothing selected picks the groupmate first and the
  bot stands up again. These check packets, not what the client draws.
- **None of these results is from the release build.** Its commit is in `BUILD-INFO.txt` and in the server log's
  `FORK_BUILD` line.

---

## 3. Checks you can run, and what to send back

You don't have to, but a log line from your PC is how a "tests only" item becomes "seen in game".

**The death fall, as a GM.** Tick **Make me GM** in the launcher's Options with the server stopped, start the server,
target a bot, then:

| Command | What it does |
|---|---|
| `/lstest deathanim` | Kills the targeted bot (or shows the fall again on a dead one) and lists every packet about it to your client for 2 minutes |
| `/lstest deathanim raise` | Brings it back as a rez does, at your side |
| `/lstest deathanim release` | Sends it to its bind point |
| `/lstest deathanim rez` | Casts a resurrection the way a player's cast goes: with nothing dead selected, the server picks the corpse |
| `/lstest deathanim off` | Stops the packet list |

**Without GM:** let a bot of your group die, cast a resurrection with nothing selected, and see whether it goes to the
bot and the bot stands up.

**Log lines to look for** in `runtime\server\logs\server.log`:

| Line | What it shows |
|---|---|
| `CLIENT_HANDSHAKE ... client=original` | The server recognised the original client (the fall is sent only to it) |
| `BOT_DEATH_ANIM fall` / `BOT_DEATH_ANIM raised` | A bot's fall and its raise were sent |
| `HIRED_COMPANION_RAISED` | A companion hired on 1.2 caught up to your level (once) |
| `HIRED_COMPANION_TRAINED` | A new hire arrived trained and geared |
| `RVR_LORD_STEP_IN` | Siege attackers walked into a keep or tower lord's reach |
| `RVR_ROAM_CHASE` | A roaming eight went after an enemy group |
| `RVR_FRONTIER_WAIT_LONG` | A gamebot waited over 5 minutes at a frontier porter, and what held it |
| `PLAYER_CLICK_TARGET` | Your click on a bot reached the server, and what it did (`result=`) |
| `LOOT_ITEMS_MISSING` | Loot rows that name a missing item, one line per loot source |
| `BOT_LED_RECRUIT ... kind=open_seat` | A travelling bot party looking for one more |
| `REALM_EXPEDITION_PULL_WAIT_GUARDED`, `..._GUARD_CLEAR`, `..._GUARD_EXPIRED` | Caer Sidi: the raid waiting for a safe moment to pull one of the Crypt Lord's court (#407) |

Send the lines, with the `FORK_BUILD` line from the top of the log, in a
[GitHub issue](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues).

**If the death fall looks wrong:** turn off **Bots fall when they die** in the Living Server tab (with the server
stopped) and everything about dead bots is as it was in 1.2. Please tell us what you saw.

---

## 4. Known issues

- **Bot raids still lose some fights (#407 to #415):** Caer Sidi, Galladoria, Tuscaran Glacier, Summoner's Hall,
  Trollheim, the Green Knight and Marfoirwort, Legion, the dragons. The Behemoth is never fought, and Fomor City and
  Morgana have no bot raid. The fixes in 1.2.1 are **tests only**, and the issues stay open until each raid is seen won on the owner's PC.
  Each restored boss rule is a server setting of its own if a fight is too hard on your world.
- **Route loops in Hibernia, region 200 (#420, #421):** repeated route rescues uphill to a hunting camp in Connacht, and
  the Lough Derg / Bri Leith seam loops. Not changed in 1.2.1; they need log lines from a PC first.
- **The death fall in the old client is unconfirmed (#425).** If it misbehaves, turn it off (see above).
- **Dead bots still can't be clicked** in the original client; use `/target <name>` or a rez with nothing selected.
- **Clicking a living bot may not select it** in the original client (#424, unconfirmed). `/target <name>` works.
- **Battleground keeps still rarely change hands** (#416); the lord-reach fix is tests only.
- **Few crafted pieces sell on the Exchange**, and no trade with a crafter bot has been finished in a real game yet.
- Everything else known, with what has and hasn't been seen:
  [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md). Every
  change: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

---

## 5. How to update

- **Have 1.2, 1.1.4, 1.1.3 or 1.1.2:** in the launcher, **Check for updates**, then **GET UPDATE**: Install, Later or
  Skip this version. It installs only with the game and server closed, and only a package that matches the SHA-256
  GitHub publishes. (CI runs an update over 1.2; not yet run on the owner's PC: **tests only**.)
- **Have 1.0, 1.1 or 1.1.1, or the Social Expansion on 0.37:** run Setup, or unzip
  `OfflineDAoC-LivingServer-1.2.1.zip` into your game folder and run `INSTALL.cmd` with the game and server closed.
- **New to Offline DAoC:** run `LivingServer-Setup-1.2.1.exe`. It installs Offline DAoC 0.37 and Living Server.
  Windows SmartScreen may warn that the exe isn't code-signed: More info, then Run anyway.
- **What stays:** your characters, bots, guilds, bot memory, companions and settings.
- **What changes on an existing world, once:**
  - `bot_schedule_mode` moves to off if it still held 1.2's untouched default (the log says `bot_schedule_mode set to
    the new default off`); a schedule you chose stays;
  - **Bots fall when they die** is on;
  - companions hired on 1.2 catch up to your level the next time they come to you.
- **To undo:** `ROLLBACK.cmd` puts every replaced file back. It never puts your world back, because that would undo
  your play since. Your daily backups are in `runtime\data\backups`.

Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
`FORK_BUILD` line from `runtime\server\logs\server.log`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2.1.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2.1.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Offline DAoC with Living Server is maintained by [chris4aker](https://github.com/chris4aker) and coded with Claude
(Anthropic). Offline DAoC was created by shadowofze; the server underneath is [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core).
Some code and ideas are borrowed from the [True 1.65 project](https://github.com/zomboden/OfflineDAoC-True165), and fixes
from other Offline DAoC forks, all credited in the README. Reports from the community on GitHub and r/daoc. GPL-3.0.

Living Server is a free, non-commercial, unofficial fan project, not affiliated with Electronic Arts or Broadsword.
