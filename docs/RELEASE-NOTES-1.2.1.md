# Living Server 1.2.1: release notes

The text below the line is the GitHub release notes for 1.2.1, a fix release on top of 1.2, published as Latest on
2026-11-22 (the launcher's update check reads GitHub's latest release). It lists only what changed since 1.2;
[RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md) still describes the whole of Living Server.

Release plan: [docs/1.2.1/RELEASE-PLAN-1.2.1.md](1.2.1/RELEASE-PLAN-1.2.1.md). What has and hasn't been checked in a
played game is tracked in [OPEN-WORK.md](OPEN-WORK.md) and the issues, not here.

Rules for the text below the line:
- **Voice (AGENTS.md, "Be honest in reports"):** it says what changed for players, clearly and confidently, with no
  testing-status framing and no word on where or by whom a change was checked.
- **Build name:** `ForkBuildInfo.BuildName` must read `1.2.1` and the changelog heading `## 1.2.1 — <date>`
  (`tools/release/check_versions.py --tag v1.2.1` checks both).
- **Uthgard's drops:** no `build_release_037.py --uthgard-drops` world was built for 1.2.1, so the release has no world
  update. If a later release ships one, its notes say "This release's world update adds them."
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
at your level, experience for enemy gamebot kills, RvR and crafting fixes, smarter bot raids and a lot more lore.

It installs over Offline DAoC **0.37** and plays on the **original game client**. Existing 0.37b and 0.37c installs keep
working and get these fixes, but from now on new Living Server features are made for the original client only.
Everything Living Server does is still described in the [1.2 release notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md).

---

## What's new and fixed

### Death and rez

- **Bots fall when they die in the original client.** A companion, hired companion or gamebot now falls and lies
  like a monster's corpse instead of standing at zero health. It stays dead to the server exactly as before.
  - Raised, it stands up where it lay.
  - Released, it appears at its bind point, with no second body left behind.
  - The new client already showed the fall and gets exactly what it got before. The server tells the two clients apart
    by the first packet they send (`CLIENT_HANDSHAKE ... client=original` in the log).
  - **Switch:** **Bots fall when they die** in the launcher's Living Server tab (server setting
    `bot_death_animation`), on by default. **Turn it off and every packet is as it was in 1.2.**
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
  gamebots already did. Albion, Midgard and Hibernia alike.
- **GM `/jump <bot name> to me` works (#438)**, for companions, hired companions and gamebots, and so do `/jump
  <name> to <name>` and `/jump <name> to x y z`.
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
- **Albion bots stuck leaving Castle Sauvage** restart from the army's muster at the border keep.
- **Gamebots that logged out beside another realm's frontier keep** log in at their capital, as players do (for
  example, Midgard bots left in Cruachan Gorge).
- **A keep defence with no defenders left stands down** instead of marching empty (a defence of Dun nGed could march
  with no bots at all).
- **A GM's `/jump` into an enemy keep's area** is no longer sent to the bind point.
- **The log checker shows how far each regroup call spread (#419).** The regroup relay itself is unchanged.

### Crafting (#422)

- **A piece you ordered from a crafter bot is handed over.** A made commission was never handed over or announced and
  lapsed after six hours. Now, while the crafter's break lasts, it opens the trade if you are near, or tells you once
  where to collect it.
- **A stock trade window that can't open** makes the crafter say why, and the deal is kept. A bot never holds a trade
  for a player who is already trading.
- The economy log counts the crafted bot listings of level 20 and up, to show whether sellable work reaches the Realm
  Exchange.

### Bosses and raids

No boss is changed; what changed is how the bot raids fight them.

- **Elro the Ancient's third treant** now comes. His script asked for health under a quarter and over three-tenths
  at once; it now comes between three-tenths and a quarter of his strength left, and his fight advice names all three
  moments.
- **Caer Sidi, the Crypt Lord (#407):** when the long wait on his court runs out, the raid still waits for his patrol to
  be clear before it pulls, so he no longer walks in with his whole court.
- **Galladoria, Spindler Broodmother (#408):** more parties go after her newborns (7 of 13 instead of 5).
- **Tuscaran Glacier (#409):** while Queen Kula lives, nothing but the King's own target hits King Tuscar (no pets, no
  area spells), so the raid no longer heals her; after she dies, casters keep clear of his hammers.
- **Summoner Roesia (#410):** a fresh poison is cured even while someone in the party is hurt, the lowest-health
  poisoned raider first, and cures go out twice as fast. The first, smaller attempt can still lose.
- **Trollheim, Nosdoden (#411):** a retry after a wipe restarts the stall clock, so the raid is no longer ended in its
  retry's first minutes.
- **Marfoirwort (#412):** his target holds close, melee fight from behind him, everyone at range stands clear and no
  pet attacks him, so his life-leech heals him from one raider instead of about 25.
- **The Green Knight (#412):** casters and healers stand together away from his target, and the melee peel his trees
  off them. He still wants a big raid.
- **Legion (#413):** the wounded step out toward the raid's healers instead of away from them, so his souls no longer
  find them alone.
- **Golestandt and Cuuldurach (#415):** a raider thrown far by a wing-gust stays part of the fight instead of being held
  at the staging ring.
- **Behemoth, Fomor City and Morgana (#414):** still no bot raid.

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

### Settings

- **Bot schedule off by default.** `bot_schedule_mode` now defaults to off: the whole roster is online. A world that
  ran 1.2 with the untouched real-clock default moves to off once; a schedule you chose in the launcher is kept. Bots'
  breaks are a separate setting and stay on.
- **New switch: Bots fall when they die** (`bot_death_animation`), on. See "Death and rez".
- The launcher's chat setting reads "HOW MUCH BOTS TALK", as the docs do.

### Also

- **/faction quiet minutes** fill back to about 5 lines a minute, while the cap of 7 still holds.
- **`/target <name>`** finds a nearby NPC for a player, such as Caer Caledon's guards, not only for a GM.
- **Stability:** the casting line-of-sight gap behind 1.2's casting crash is closed; a packet queued while its queue is
  drained waits instead of raising "DrainTo in progress"; the missing stock merchant item templates at start-up are one
  warning with a count.
- **Linux:** the launcher's tooltip bleed-through and alt-tab crash under Wine are fixed (#393), and a Linux start
  script installs launcher updates and keeps the game fullscreen (#395).

---

## Player tools

**The death fall, as a GM.** Tick **Make me GM** in the launcher's Options with the server stopped, start the server,
target a bot, then:

| Command | What it does |
|---|---|
| `/lstest deathanim` | Kills the targeted bot (or shows the fall again on a dead one) and lists every packet about it to your client for 2 minutes |
| `/lstest deathanim raise` | Brings it back as a rez does, at your side |
| `/lstest deathanim release` | Sends it to its bind point |
| `/lstest deathanim rez` | Casts a resurrection the way a player's cast goes: with nothing dead selected, the server picks the corpse |
| `/lstest deathanim off` | Stops the packet list |

**Without GM:** when a bot of your group dies, cast a resurrection with nothing selected; it goes to the bot and the
bot stands up.

**Don't like the fall?** Turn off **Bots fall when they die** in the Living Server tab (with the server stopped) and
everything about dead bots is as it was in 1.2.

**Log lines** in `runtime\server\logs\server.log` that show the new behaviour:

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

If something looks wrong, open a [GitHub issue](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
lines and the `FORK_BUILD` line from the top of the log.

---

## Known issues

- **Bot raids still lose some fights (#407 to #415):** Caer Sidi, Galladoria, Tuscaran Glacier, Summoner's Hall,
  Trollheim, the Green Knight and Marfoirwort, Legion, the dragons. The Behemoth is never fought, and Fomor City and
  Morgana have no bot raid. Each restored boss rule is a server setting of its own if a fight is too hard on your world.
- **Route loops in Hibernia, region 200 (#420, #421):** repeated route rescues uphill to a hunting camp in Connacht, and
  the Lough Derg / Bri Leith seam loops. Not changed in 1.2.1.
- **The death fall in the original client (#425):** if a dead bot looks wrong, turn off **Bots fall when they die**
  (see "Player tools").
- **Dead bots still can't be clicked** in the original client; use `/target <name>` or a rez with nothing selected.
- **Clicking a living bot may not select it** in the original client (#424). `/target <name>` works.
- **Battleground keeps still rarely change hands** (#416).
- **Few crafted pieces sell on the Exchange.**
- Everything else known:
  [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md). Every
  change: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

---

## How to update

- **Have 1.2, 1.1.4, 1.1.3 or 1.1.2:** in the launcher, **Check for updates**, then **GET UPDATE**: Install, Later or
  Skip this version. It installs only with the game and server closed, and only a package that matches the SHA-256
  GitHub publishes.
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
