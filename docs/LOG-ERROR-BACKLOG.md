# Log error backlog

Errors and open problems collected for later fixing. Each entry has the evidence (log message, count, when), what we
know, and a suggested fix. Nothing here is fixed yet unless it says so.

**Source of the 2026-10-27 entries:** `runtime/server/logs/server.log` on the owner's PC (playable-v0.36c), four server
starts that day:

| Start | Build | Notes |
|---|---|---|
| 05:43 | af39287 | |
| 08:12 | dabdd13 | |
| 09:17 | b062980 | |
| 11:17 | a058136 | classic launcher |

The logs cover about six hours of server time, with Claudecheck (Albion), Claudemid (Midgard) and Claudehib (Hibernia)
playing parts of it. Counts are for the whole log. Startup errors repeat once per start, so 4 means "every start".

## Priority 1: crash in the bot AI

### NpcService NullReferenceException in `BotBrain.BotState_Aggro.Think`

**Status: fixed in code 2026-10-27, not yet deployed.** Both properties now read their field once, and the ordered pull is
cleared only if it hasn't changed (`Interlocked.CompareExchange`). Watch the next long run for a repeat.

- **Seen:** once, 10/27 07:50:51, build af39287. The service caught it and removed the bot from the world:
  `RemoveFromWorld ... GameBot name=Roisellin ... reg=248`. Region 248 is a realm raid dungeon.
- **Stack:**

  ```
  Critical error encountered in NpcService: System.NullReferenceException
    at DOL.AI.Brain.BotBrain.BotState_Aggro.Think()  BotBrain.cs:line 2935
    at DOL.AI.Brain.BotBrain.Think()                  BotBrain.cs:line 1442
    at DOL.GS.NpcService.TickInternal(ABrain brain)   NpcService.cs:line 71
  ```

- **Code at that line (unchanged since af39287):**
  `if (_brain.ActiveOrderedPullTarget == null && (!_brain.HasAggro || (!_brain.HasCommittedDungeonPull && ...`
- **Likely cause (not confirmed):** a check-then-use race in `HasCommittedDungeonPull` (BotBrain.cs ~190). It reads
  the field `_committedDungeonPull` twice: a null check, then `.IsAlive` and `.CurrentRegion`. `ClearAggroList()` sets
  that field to null, and `CommitDungeonPull` is called from five other files: AutonomousDefensivePull,
  AutonomousDungeonTraversal, AutonomousWorldBotController (x2) and AutonomousBotGroupCoordinator. A clear between the
  two reads gives exactly this NRE. `ActiveOrderedPullTarget` (~776) has the same double-read pattern on
  `_orderedPullTarget`.
- **Suggested fix:** read each field once into a local
  (`var pull = _committedDungeonPull; return pull != null && ...pull...`) in both properties. Add a unit test that
  clears the pull between reads if a seam allows it.

## Priority 2: errors during play (not startup)

### Duplicate merchant page/slot

**Status: fixed in code 2026-10-27, not yet deployed.** The fix is in the server, not the data: `MerchantTradeItems.Layout`
lays each list out once and caches it for five minutes. The rules:

- Rows are read in a stable order.
- A copy of an item already in the list is dropped.
- A different item in a taken slot moves to the first free slot from its own page on.

The window, buying (`GetItem`) and `GetAllItems` all use that one layout, and GM `/merchant articles` edits clear the
cache. One `MERCHANT_LIST_TIDIED` info line per list replaces the error spam.

On the owner's world data: 51 lists, 1,818 copies dropped, 118 items moved to free slots (they could be hidden before),
none without room, and every item still shown. No database rows change, so there's no world update to ship. The data could
still be cleaned in a later release world.

- **Seen:** 361 lines today, 2,076 on 2026-10-22, so this predates the Living Server work. Logged when a merchant list
  loads during play:
  `two merchant items on same page/slot: listID=HibWeaponMerchant5 page=2 slot=6..9`
- **Lists affected today (lines):**

  | List | Lines |
  |---|---|
  | HibWeaponMerchant5 | 224 |
  | AlbWizardStaffs | 80 |
  | AlbTheurgistStaffs | 20 |
  | AlbSorcererStaffs | 20 |
  | 44ac972a-7aab-4d9c-8c2e-2c0fa5313c6a | 12 |
  | HibMetalBarMerchant | 5 |

- **Effect:** one of each pair is probably not shown or not sellable. No crash.
- **Suggested fix:** a data fix in `merchantitem` (renumber the duplicate slots). It goes in the release world defaults so
  new installs get it, and in a world update for existing installs (`make_patch.py`). Check that the bots' vendor upgrades
  (AutonomousBotEconomy) still find the items.

### Live dashboard snapshot: access denied

**Status: fixed in code 2026-10-27, not yet deployed.** The move is retried up to 4 times, 100 ms apart, on the dashboard's timer
thread. If the file stays locked, a one-line warning replaces the stack trace.

- **Seen:** 5 times, e.g. 06:10:45 and 06:30:45 (build af39287):
  `Live bot dashboard snapshot failed ... System.UnauthorizedAccessException: Access to the path is denied.
  at AutonomousBotDashboard.Publish ... line 120 (File.Move)`
- **Likely cause:** the launcher reads the snapshot file at the moment the server replaces it (Windows file lock).
- **Suggested fix:** retry the move once or twice after a short wait, or use `File.Replace`. Log a warning with no stack.

### Double world-enter disconnect (new client)

- **Seen:** 07:15:11. Two PLAY clicks sent two world requests:
  `Object is already in "Midgard"` -> `Failed to add player to world: ... Claudemid` -> `player.Delete()` and the client
  hung on "Entering the world...".
  Also 06:20:57 `GameClient.Player is being replaced but hasn't been deleted yet (current: ... Claudecheck ...)`.
- **Status:** the new client now ignores repeat PLAY clicks for 10 seconds (commit c5a7724). That isn't deployed yet,
  since it is a new-client file.
- **Server side, fixed in code 2026-10-27 (commit 2aafcec), not yet deployed:** a second world-init that arrives while the
  first is still loading the character is ignored (`WORLD_ENTER_REPEAT_IGNORED`). This also covers the classic client.

### PvE matchmaking: most waits run out

**Status: changed in code 2026-10-27 (commit 290a5e9), not yet deployed or measured in game.**

- **Seen (11:17-16:06, build a058136):**
  - 153 PvE parties formed, and 790 waits ran out after 20 minutes.
  - Of the timeouts, 447 were below level 50, mostly levels 16-30, and 343 were level 50s.
  - Parties by realm: Albion 64, Hibernia 46, Midgard 43.
  - Level 50 timeouts by realm: Hibernia 183, Albion 104, Midgard 56.
- **Causes found in the code:**
  - Formation took only bots that weren't fighting, and a waiting bot grinds.
  - Recruits had to be within 8 minutes of the leader, and that never relaxed.
  - Leaders were a 60% random draw, not the longest waits first.
  - A party needed 5 members.
- **Changed:**
  - mid-fight recruits
  - patience that widens travel to 11 and then 14 minutes and allows four-man parties after 12 minutes
  - the longest waits lead first
  - bots remember each other (see [LIVING-SERVER.md](LIVING-SERVER.md))
- **Realm gap, not changed (owner decision):**
  - Albion has three tank classes and three healer classes, and the Paladin counts as both. Midgard and Hibernia have two
    of each.
  - Level 50 healers in the 08:49 roster: Albion 96, Midgard 74, Hibernia 53.
  - The Bard heals in DAoC but isn't a healer in matchmaking. The bots' Bard specs put 16-33 points in Regrowth and its
    brain doesn't act as a healer, so making it one needs the spec and the AI changed too.
- **Next run, compare:** PvE `AUTONOMOUS_GROUP_FORMED` per hour by realm,
  `AUTONOMOUS_GROUP_MATCHMAKING_TIMEOUT` per hour by level, `leaderWaitedSeconds`, no-shows, and four-man parties
  (`size=4`). The before numbers came from a log script kept outside the repository.

## Priority 3: errors at every startup (world data)

These print at each server start (counts are about 4x the per-start number). They predate this work. None stops the
server. Fix them as data clean-ups when convenient.

| Count | Source | Message | Likely meaning |
|---:|---|---|---|
| 928 | SkillBase | `LineXSpell Spell Adding Error: key not present, Line Item Effects, Spell #` | Spell-line rows point at spells missing from `spell` |
| 16 + 16 | SkillBase | same, Lines Mundane Poisons / Potions | same |
| 168 | NpcTemplateMgr | `No npctemplate with ID # found` (5232525, 60166758, 10001, 12967, 604021, 60167801 ...) | Mobs or spawns point at missing templates |
| 60 | NpcTemplate | `Tried to load a null spell into NPC template # spell ID: #` (incl. spell 0) | Template spell lists with bad IDs |
| 140 | DoorMgr | `Failed to register door in pathfinding provider, possible navmesh/object location mismatch` (Postern Door, Keep Door, Relic Gate, door) | Doors off the navmesh. Check per door; no global navmesh rebuilds |
| 8 | Region | `Error loading NPC ClassType: DOL.GS.Scripts.RPTradeInMerchant` (GameNPC used instead) | The class doesn't exist in this build |
| 4 | ScriptMgr | `LoadCommands: An item with the same key has already been added. Key: &tc` | Two command handlers claimed `/tc`. **Fixed 2026-10-27 (not deployed):** `/transfercorpse` lost its `/tc` alias; `/tc` is the Realm Exchange teleport |
| 4 each | ScriptMgr | `Couldn't find a SpellHandler constructor for UniPortal / UniPortalKeep / ArmorFactorBuff` | Spell types without handlers |
| 4 | SkillBase | `Requested value 'ground' was not found ... spellid` | Bad enum value in a spell row |
| 4 each | LootGeneratorOneTimeDrop | `Can't find MobName Hrodrek / Dob / Murgar / Neese / Cheshire` | One-time drops for mobs not in the world |
| 4 each | LootGeneratorTemplate | `ItemTemplate: <item> is not found, referenced from loottemplate: Apocalypse / Golestandt / Dura'ek / Maldahar / Moran / Morvalt` | Loot tables point at missing items |

Related warnings at startup:
- `GameNpcInventoryTemplate: Error adding NPC equipment` (201)
- `Failed loading NPC inventory template: abomination_fiery_sword` (50)
- `Clothing Manager: Guard Inventory is null for GuardCorpseSummoner` (96)
- `Couldn't find a zone for (... ColialtAdds / living entombed ...)` (45+)

## Warnings worth a look (not errors)

These are mostly the bots' own diagnostics, counted over about six hours:

| Count | Warning | Note |
|---:|---|---|
| 6,351 | `AUTONOMOUS_DEATH_ROUTE_REPLAN` | Logged at every solo death, not only repeats (corrected 2026-10-27). In 11:17-16:06 there were 6,412 from 2,011 bots, and 79% were a bot's first death without experience since its last. The real gap was the camp danger bench counting per camp cell: one deadly monster was benched again and again, cell by cell (e.g. rencan tri 8 times over 5 cells). **Fixed in code (commit f3e0233, not deployed):** the monster in its region is benched as a whole (`AUTONOMOUS_SPECIES_DANGER_BENCH`). |
| 799 | `AUTONOMOUS_BETWEEN_TASK_TIMEOUT` | |
| 597 | `AUTONOMOUS_GROUP_MATCHMAKING_TIMEOUT` | |
| 317 | `AUTONOMOUS_GROUP_RESURRECTION_TIMEOUT` | |
| 292 | `BOT_PET_STUCK_RECOVERY` | |
| about 2,600 | "Long tick" timing warnings (MovementService, NpcService, TimerService, AttackService) | Mostly 40-60 ms, some NpcService ticks of 190-270 ms on navigation. Keep an eye on them. |
| 103 | SQL queries over the time budget | |

**Memory:** no leak seen. Live heap stayed at 1.6-1.8 GB from 05:48 to 11:48 across restarts, and working set at
5.8-7.0 GB.

## Open from the in-game checks (2026-10-27)

Not log errors, but found while playing and not yet fixed or verified:

- **Rumours have never fired in game.** There was no `RUMOUR_NAMED` or `RUMOUR_KEEP` in about six hours with players
  online. Diagnostics were added (`RUMOUR_KEEP_CHECK`, `RUMOUR_NAMED_CHECK`, commits 8d4a31e and 86edd19) and are not yet
  deployed. Next run: check whether keeps ever drop under 30% of their guards and whether rare named respawn while a
  player who would care is online.
- **Deeds have never been earned in game:**
  - Ganked needed 4+ enemies within 2,000 units; a death to 3 Hibernians at Hadrian's Wall didn't count. Now 3+
    (commit 2aafcec, not deployed).
  - A solo named kill needed yellow con or harder, which a level 50 can't easily find. Now blue or harder.
  - `PLAYER_DEED_SKIP` logging (fd6dbbd) is not yet deployed.
- **No way to hand a beggar coin in the new client.** The server takes gifts, but the client has no obvious give or
  trade action.
- **Rage-quit return line not yet seen.** Ederorred's rage quit ran out at 09:05 and survived restarts, but he hadn't
  logged back in by the last check. He returns on his next scheduled login.
- **Classic client test not run.** At 11:18 the classic client was logged in, but screen control stopped when the PC's
  display went to sleep.

## Classic-client play test, Midgard block (2026-10-27 evening, build db57318)

Played as Claudemid (Viking, levels 1-3) from 19:52 to 22:11 (2 h 20 min, not the planned 3 h). **Fixed in code, not yet deployed** (in-game checks after the next
install):

- **Issue #25: NpcService stalls of 1-1.3 s.** Every slow tick credited to `AutonomousRvrRally.HasRoute` was "Collecting
  frontier medallion" (393 in the morning log, 57 in this block). Since 7ef2d66 a distant warband walks to its medallion
  merchant, and each bot ran up to 18 multi-zone route probes every 15 s. The interaction spot is now searched only
  within 2,000 units (064a387). Baseline for this block: NpcService per-minute maximum averaged 310 ms, 14 of 113
  minutes over 500 ms, worst 939 ms.
- **Group calls nobody fits:** "lvl 2 viking lfg pst" and "any mid healers lfg near mularn?" got no reply, because no
  bot is under level 10. One bot now says so honestly (7a06ae6). `/lfg` from a level 3 still draws nothing; the alt
  system is the real fix.
- **Groupmates talking from far away:** Liviardis, across East Svealand, said "assisting you", "here it comes" and "you
  have the patience of a saint with these pulls". Yrilda, in Aegir, said "That one came out of nowhere." and "Gj." when
  the player died in Midgard. Fight calls, group idle talk and death remarks now need the bot within 3,000 units of the
  player or in its own fight (0112f05, 37aedfd). There is also no "gj" after a fight the player died in (cef4187).
- **A groupmate walked off the map and was dropped silently.** Liviardis walked back after a release to coordinates
  outside every zone. The watchdog moved him to Jordheim, removed him from the player's group without a word, and the
  schedule logged him off. The watchdog now moves a gamebot in the player's group to the player and keeps the group
  (0112f05; the commit message doesn't mention it, fb58701 does). **Root cause of the walk off the map is not found.**
- **Formal return line:** "I have caught up. Returning to formation." is the companion voice. Gamebots now say
  "back" and similar lines (0112f05).
- **Dead players couldn't use /faction**, so "anyone near for a rez?" was refused (fb58701).
- **A bot named while busy said nothing.** "Yrilda wanna group?" got no reply while she rode a stable route. A named
  bot now answers after the fight or ride, within 90 s (7536897, c58e527).
- **"can u come to mularn?"** was read as banter. A groupmate asked to come now says honestly whether it can
  (cef4187).

**Seen, not fixed:**

- An autonomous gamebot in the player's group doesn't follow the player into another region. Only companion helpers
  are moved along on a region change (`OnOwnerRegionChanged`). A real groupmate would take the same porter.
- `/tele <bot>` and `/who` are correct, but bots on stable routes move fast, so `/invite` right after `/tele` can be
  "too far away".
- A level 3 Viking hits level 3-4 monsters for 1-4 damage and dies to most of them. This is untrained weapon skill.
  Training needs the trainer window, which remote control can't open.
- **Answered correctly:** "what lvl r u all?" ("28 here") and "where do i train my axe skill?" ("viking trainer is
  Saeunn in East Svealand"). Bots also told the gold-spam bot off ("stfu Dristyrongg, nobody wants your gold"), and
  Yrilda buffed the player on arrival and came to rez.

## Classic-client play test, Hibernia block (2026-10-27 night, build db57318)

Played as Claudehib (Magician, level 1) from 22:11 to 23:08, when the owner stopped the test for an install. The build
was not reinstalled for this block. **Fixed in code, not yet deployed:**

- **Gamebot groupmates walked in a straight line to the player.** `FollowFormation` used `WalkTo` for any bot that
  isn't a companion, at any distance. Lorcinellan (Druid 17), asked to come from across Shannon Estuary, never
  arrived. This is the likely cause of Liviardis walking off the map in Midgard. A gamebot in the player's group
  more than 600 units from its slot now paths (8887d19).
- **Gamebot groupmates left behind at portals.** A gamebot beside the player when the player teleports now arrives
  with them (4f87a37).
- **`/tele mob` ignored level.** `/mobs 3` lists myrkcat and `/tele mob myrkcat` went to level 27 myrkcats. "badger
  cub" (listed at 1) went to level 5s. The camp nearest the player's level now wins (c19e083).
- **A gamebot rez never landed.** Lorcinellan cast resurrection about ten times. Each cast was followed by "You
  decline to be resurrected." with no input from the player, and accepting said "No one is currently trying to
  resurrect you." The resurrect handler now sends no second dialog while one is pending, and bots wait 60 s after a
  decline (28e07ff). **The cause is a hypothesis** (a second dialog replaced the first); check it in game.
- **Chat:**
  - "where should i level?" at level 1 now names the starter zones (1719c3b).
  - "how long to 50?" now gets an answer (c3fc68e).
  - "wtb a staff" no longer draws "how much are you asking?" (a3a1f52).
  - "come with me?" is now treated as a request to come (eb52aa2).
  - Group talk about pulls and loot now waits for a real fight (ec5c9c5).

**Seen, not fixed:**

- **New characters can barely fight.** A level 1 Magician (about 30 HP) dies in three hits to a level 1 grass spirit
  (about 10 damage a hit), while Gleam Ray lands for 2-5. A level 3 Viking hits for 1-4. This may be stock OpenDAoC
  numbers plus untrained skills; it needs a look before the alt system brings new low-level bots.
- **Retracted: "the server log runs behind real time."** This came from my wrong estimate of the clock, not from the log;
  at 23:09 the log was one minute behind. No change was made.
- **`/mobs` lists every realm's monsters for a Midgard character but only Hibernian ones for a Hibernian.** For example,
  Midgard's `/mobs 4` showed dryad sprig and skeletal seafarer. Check the zone-access filter.
