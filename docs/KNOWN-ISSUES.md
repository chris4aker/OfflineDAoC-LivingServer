# Living Server 1.2.1: known issues and what's not tested yet

For players. What to expect, what hasn't been seen working yet, and known problems. What changed in 1.2.1 is in
[RELEASE-NOTES-1.2.1.md](RELEASE-NOTES-1.2.1.md); all of Living Server and what has been seen working is in
[RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md); every change, with its numbers, is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

A feature counts as working only once it has been seen working on the owner's PC. Until then it says **tests only**.
Each result names the build it came from. None of them is from the release build (its commit is in BUILD-INFO.txt and
the server log's FORK_BUILD line). The last builds checked on the owner's PC are 1.2's `c81eb5d`, `43c43aa` and
`aa537ce`. **Nothing that is new in 1.2.1 has been seen on the owner's PC yet: every 1.2.1 change is tests only.** The
automated results are in the release notes.

**1.2.1 safety switch:** if dead bots look wrong in the original client, turn off **Bots fall when they die**
(`bot_death_animation`) in the Living Server tab; everything about dead bots is then as it was in 1.2.

Everything still to do, with a GitHub issue each, is in [OPEN-WORK.md](OPEN-WORK.md).

## 1.2's fix list, and where 1.2.1 leaves it

Seen on the owner's PC or in its log on 1.2 builds, and left for 1.2.1 (owner, 2026-11-19: publish unless there are
major bugs, crashes or instability, and add the rest to the 1.2.1 list). Each 1.2.1 change below is **tests only**.

- **/faction pace:** about 5 lines a minute on average, but quiet minutes drop to 2 (`43c43aa`: 4.9 a minute, 13 of 39
  minutes under 5). 1.2.1 fills quiet minutes back to about 5, under the same cap of 7: **tests only**.
- **Route loops at region 200 seams:** the Cruachan Gorge / Breifine seam that sent 75 Hibernian bots home on
  `43c43aa` is fixed in 1.2 (**tests only**; seen walked single file on `aa537ce`). The other seam loops of that log
  (Lough Derg / Bri Leith, #421) and the Connacht hunting-camp rescues (#420) are not changed in 1.2.1 and can still
  loop.
- **A bot removed from the world by a casting crash** (`43c43aa`, one bot). Fixed in 1.2 with a null guard: **tests
  only**. On the list in case it comes back.
- **A gap in the casting line-of-sight check** (`CastingComponent`, `LosChecker.Out` can be null) behind that crash.
  1.2.1 treats a client it can't ask as no check: **tests only**.
- **Caer Caledon's guards can't be picked with /target** by players (checked as a GM only). 1.2.1's `/target` finds a
  nearby NPC for players too: **tests only**.
- **Click-targeting a bot** didn't work for the test helper on `c81eb5d` (`/target` works). Unconfirmed: it may be the
  test tool, not the game. The server has no rule that refuses a click on a living bot: no bot is flagged
  untargetable or peaceful, only stealth hides one, and the one unusual bit, "has owner" (a bot's brain is a controlled
  brain, as a pet's is), is the one pets carry, and pets can be clicked; 1.2.1 logs each click on a bot (`PLAYER_CLICK_TARGET`), so one played click settles it.
- **Old merchant item templates missing at startup:** about 760 error lines at every start from the stock world data. No
  effect seen in play. 1.2.1 makes them one warning with a count (and missing loot items one `LOOT_ITEMS_MISSING` line
  per source).
- **A "DrainTo in progress" error from QueuePacket**, seen once (`43c43aa`), with no effect seen. In 1.2.1 the packet
  waits instead: **tests only**.
- **Also from `aa537ce`, changed in 1.2.1 (tests only):** a Midgard keep defence that marched with 0 bots now stands
  down; 11 Midgard bots that logged in beside an enemy keep now log in at their capital; a GM's `/jump` into an enemy
  keep stays there; Albion bots with no route out of Castle Sauvage restart from the border keep's muster; the
  launcher's chat caption reads "HOW MUCH BOTS TALK".
- **Epic raids:** still being tuned (below).

## Epic raids: still being tuned

Bot raids fight the bosses by their own rules now, and the bots lose some of them. These were lost on the owner's PC,
bots only, at the game's own raid size:

- **Caer Sidi:** lost at the Crypt Lord, whose court and patrol beat the raid (`b1b6846`, `9a173f4`).
- **Galladoria:** lost at Spindler Broodmother (`9a173f4`). On `b1b6846` the raid ran six hours and never reached
  Olcasgean; the fixes for the Corrupt Primals since then are **tests only**.
- **Tuscaran Glacier:** lost at Queen Kula (`b1b6846`, every Kula mechanic handled), at Elder Icelord Suttung and the
  Council (`9a173f4`), and at King Tuscar (`ccba364`).
- **Summoner's Hall, all three realms:** lost at Summoner Roesia (`b1b6846`, `9a173f4`). **Grand Summoner Govannon has
  never been beaten by bots.**
- **Trollheim:** timed out before Nosdoden fell (`ccba364`, `b1b6846`).
- **Darkness Falls (Albion):** the raid looped on a chthonic knight behind it (`b1b6846`). Fixed: **tests only**.
- **Golestandt:** lost after two wipes (`9a173f4`, `c81eb5d`); won on test build 5 and on `43c43aa`. **Cuuldurach:** lost
  after two wipes (`c81eb5d`, `43c43aa`); won on test build 5. Dragon fights can fall apart partway.
- **The Green Knight** and **Marfoirwort:** lost (`ccba364`).
- **Legion:** some of his rules are in (his adds and heals are capped, wounded raiders step out of his reach), but the
  raid does not play his platform, the way down or his add waves. **Behemoth** is never fought; **Fomor City** and
  **Morgana** have no raid.
- **Xanxicar's stomp:** a realm learns him over its attempts. Still being tuned.

**1.2.1:** the raid issues #407 to #415 stay open. The raid fixes in 1.2.1 come from these logs and from simulations,
and are **tests only** until a raid is seen won on the owner's PC. Elro the Ancient's third treant, which never came,
now comes between three-tenths and a quarter of his strength left (**tests only**).

Fixes made after the last raid builds the owner ran are **tests only**:
- **Summoner Roesia:** the split camps for her Summoner Pain made the raid worse and are removed. Raid healers now cure
  Summoner Pain before a top-up heal when it is on two or more raiders and nobody in their party is in danger. On
  `9a173f4` they never did (a heal always came first). **Tests only.** Before the camps the raid also lost to her.
- **Raid cures in general:** healers were seen curing poison and mesmerize on `9a173f4` and `c81eb5d`; on `9a173f4` up to
  a dozen cured the same raider in the same second. Each curer now claims its target: **tests only**.
- **Curengkur:** the raid never formed (no route to its staging post). The next staging posts are now tried. **Tests
  only.**
- **Hibernia's dragon:** "No muster post could be placed at the hub" at Innis Carthaig on `9a173f4`; it started first try
  on `5154cae`. The check that names a refused post is **tests only**.
- **Tuscaran Glacier:** Suttung pulled with nobody at his muster after stranded raiders were moved beside him, and
  Steinvor's last Pillar of Ice outliving him. Both fixed: **tests only**.
- **A stall as two raids end together** (3.0 s on `9a173f4`). Fixed: **tests only**.

If a fight is too hard on your world, each restored boss rule is a server setting of its own and can be switched off
alone. **Don't turn on `epic_earthmender_mutual_heal`:** with it on, the four Earthmenders heal each other and the bots
can't win (on `b1b6846` they held at 99-100% through 48 minutes).

## RvR and battlegrounds

- **Battleground keeps rarely change hands (#416).** Bots storm the keep lord again and again and mostly fail. The first
  capture was Caer Caledon, taken by Albion on `43c43aa` after its keep levels were repaired (guards 39, lord 46). The
  cause found: stormers struck the lord from up to 900 away, but he takes no damage from beyond 400. In 1.2.1 they
  walk to within 350 first, on the frontier too (`RVR_LORD_STEP_IN`): **tests only**.
- **Open-field regroups pass between groups more often in dense warbands**: 17.4% of regroups on `9a173f4` were
  started by another group's call. None was passed on a second time and none echoed back. The owner raised the bar to
  20% on 2026-11-19, so this passes; the log checker now also prints how many groups each call reached, to tell
  denser warbands from a call that over-fires (#419, closed).
- **Roaming eight-man groups** rarely find a fight and move on (#417). In 1.2.1 their patrols favour where enemy RvR
  bots are, and they chase the planner's hunts (`RVR_ROAM_CHASE`): **tests only**.
- **Some gamebots wait a long time at frontier teleporters** before the server moves them on (#418). In 1.2.1 they go to
  a porter that sells their medallion, and a wait past 5 minutes is logged (`RVR_FRONTIER_WAIT_LONG`): **tests only**.
- **Enemy gamebot kills gave no experience** (#396, Thidranki). In 1.2.1 they pay what a player kill pays: **tests
  only**.

## Chat

- **/faction runs at about 5 to 7 lines a minute on a full realm, by design** (owner, 2026-11-18), never more than 7.
  4.9 a minute on average was seen on `43c43aa` (quiet minutes lower: see the fix list). Lower **How much bots talk** in
  the Living Server tab if it's too much.
- **Seen since the owner's chat check on `5154cae`:** "is it far?" gets a travel answer (`43c43aa`); a call for help gets
  offers or honest refusals from several bots (`43c43aa`); another realm's zone gets "thats hib frontier rvr only"
  (`c81eb5d`); a /broadcast question is answered in /broadcast (`43c43aa`). **Tests only:** a bot-to-bot answer no longer
  landing before yours.
- **"omw" means coming** (tests only to the end): a bot that says it has to go, with a deadline. A reply that is never
  sent leaves a short walk behind it (up to 210 seconds).
- **Replies still guess from your words.** A bot doesn't know what you are doing beyond what you type. A bot that
  remembers you may ask your level right after you said it. Chat has been tested one line at a time more than as long
  conversations.
- **Bot parties take new members on the way only now and then** (1.2.1, #423, tests only): a party that set off with a seat free
  looks for one more for five minutes, two minutes after it leaves and then every ten. Between those looks its "lfm" has stopped, and
  it takes one member at a time.

## Bots that play with you, and companions

- **Bot-led groups:** gamebots don't share your horse or boat; the leader takes its own and waits at the other end. The
  break at the end of a session ends the party.
- **A bot that offers to group may walk on before you meet**; if it's too far by the time you invite, it says where it is.
- **Hired companions:** joining your fight on a grey monster failed on `b1b6846` and is fixed: **tests only**. In 1.2.1
  they join at your level, trained and in merchant gear, and companions hired on 1.2 catch up once
  (`HIRED_COMPANION_RAISED`); `/hire train` takes line names (#397): **tests only**. The healer's
  [companions] and [hire] links work only with the healer targeted; typed `/hire` words always work. The gear window's
  title reads "House Vault 0" (it reuses the house vault window).
- **Bots don't fall when they die** in the original client (1.2). From 1.2.1 they fall and lie like a monster's corpse,
  and a resurrection cast with nothing selected reaches a dead bot nearby. CI's playtester checks the packets
  (`b644529a`), not what the client draws: **tests only** (#425). Switch it off with **Bots fall when they die**. Dead
  bots still can't be clicked in the original client; `/target <name>` or a rez with nothing selected works.
- **Clicking a living bot** may not select it in the original client (#424, unconfirmed); 1.2.1 logs each click
  (`PLAYER_CLICK_TARGET`).

## Bots as people and 2003 builds

Seen in the server log on `c81eb5d` (not watched on screen): skill tiers 32/48/20 across 535 bots, bots growing a tier,
asking the way, movement habits, and the era builds loading. **Tests only:** mistakes and their rate, milestone talk,
raid learning, places seeded on a new world, and the era class mix on a fresh world. No raid has been fought by an
era-built world yet. Whether the original client shows a sitting bot is still to be checked.

## Crafting and the economy

- **Crafting as a player** hasn't been seen yet, and needs the original client.
- **Trading with a crafter bot:** tried on `b1b6846`; a reply was cut mid-word, a different piece was offered, and the
  agreed deal lapsed. Fixed: **tests only**. No trade has been finished yet. Since then (issue #422): a made commission was
  never handed over or announced, because the hand-over ran only at the very edges of the crafter's break, where it saw no
  break; the crafter now hands it over, or says where to collect it, while its break lasts. A stock trade window that could
  not open now says why and keeps the deal. **Tests only.**
- **Craft orders** often lapsed on `19e7037` (156 made, 163 expired). Orders now go only to crafters who stay online and
  are withdrawn from busy ones; on `c81eb5d` 44 were made and 1 expired (server log).
- **Few crafted pieces sell on the Exchange**, mostly because most bot work is practice pieces. `ECON_INDEX` now also
  carries `craftedListed20` (crafted bot listings of level 20 and up, what the Exchange's upgrade buyers can use).
- **The launcher's crafter card and economy page** haven't been checked on the owner's PC.

## Lore

The loremasters' dialogs, the lore books, the Bestiary page and the Herald's chronicle are **tests only**. The `/lore`
window, boss yells, atmosphere lines and first-kill lines were seen on `1dc66b6`. The 1.2.1 lore (every epic boss's
book, the major bosses and places at full depth, fight advice and place lore without game terms) is checked by CI's
lore rules and independent reviews, and has not been read in game: **tests only**.

## Play with friends (experimental)

- It needs the original client; the new client can't use Join Friend yet.
- Only a join over a home network has been seen (test build 7). A session played together and a character passport trip
  between two PCs are **tests only**.

## Launcher, install and updates

- **Updating a 1.1.4 game through Setup, a new install from Setup, and a 0.37 (class-off) world** haven't been run on the
  owner's PC for 1.2. CI runs them: **tests only**. The same goes for **updating 1.2 to 1.2.1** from the launcher.
- **The bot schedule is off by default from 1.2.1:** the whole roster is online. A world that ran 1.2 with the untouched
  real-clock default moves to off once; a schedule you chose is kept.
- **Updating a 0.36, 0.36b or 0.36c game** through Setup is **tests only** for 1.2. On 0.36 with the Social Expansion,
  run `PREPARE-FOR-UPDATE.cmd` from the package first.
- **The online count on the main screen** stuck at "5 online" for half an hour on `5154cae`. Fixed: seen updating from 5
  to 2,875 on `c81eb5d` and `43c43aa`.
- **A maximized launcher ignores START SERVER, STOP SERVER or ENTER REALM** when another window sits over those buttons
  (seen on the owner's PC, 2026-11-05 and 2026-11-18). Use the launcher at its normal size, or move the other window.
- **Releases marked as pre-releases are never offered** by the launcher's update check.
- **Windows SmartScreen** may warn that Setup isn't code-signed: More info, then Run anyway.

## The server

- **A very busy server can stall for a few seconds**, most often as raids end. An 8-hour run without a 2-second stall
  hasn't been completed yet (4 h 42 min on `19e7037` had one stall of 1.05 s).
- **A bot can search for a camp for minutes** (mostly level 50s at Tir na Nog's bind). It clears its search after 3
  minutes; the cause isn't found yet.
- **Memory over very long sessions:** Windows handles grow slowly (about 1,700 an hour with 600 bots a realm). The server's
  own memory stays flat.

## New client (0.37c)

- No crafting and no Join Friend.
- APPRAISE stays hidden, and you don't see yourself as see-through in Stealth yet.
- No guild window; `/face` keeps facing your target until you type it again; a bow fires from the Ranged Weapon key.
- Added the new client after installing Living Server? Run `INSTALL.cmd` again.

## Always the case

- **A fresh world needs bots.** Use **+ Lv.1**, **+ Lv.20** and **+ Lv.50** in the launcher. A realm needs 8 bots of level
  5 or higher before it forms a bot guild; automatic raids need 80 eligible level-50 bots of the realm online.
- **Bot guilds stay in your world when you turn them off.** Remove them with the game and server closed:
  `python tools\claude-version\set_bot_guilds.py --purge --apply` in your game folder (it backs up your database first).
- **`ROLLBACK.cmd` doesn't remove saved data.** It puts back the files the install replaced; new tables and settings
  stay, and the original server ignores them. Run it once per build you installed.
- **Versions:** Living Server is for Offline DAoC 0.37, 0.37b and 0.37c only. 0.37 stays the game; later
  fixes come in Living Server releases.
- **The server listens only on this PC** unless you host for friends. If your settings let other computers in, the log
  warns at each start (`NETWORK_EXPOSURE`).

## Reporting a problem

Open an issue on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues). Include the `FORK_BUILD` line
from `runtime\server\logs\server.log` (it names the build and which features are on) and what you were doing.
