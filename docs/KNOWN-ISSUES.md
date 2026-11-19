# Living Server 1.2: known issues and what's not tested yet

For players. What to expect, what hasn't been seen working yet, and known problems. What's in 1.2 and what has been
seen working is in [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md); every change, with its numbers, is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

A feature counts as working only once it has been seen working on the owner's PC. Until then it says **tests only**.
Each result names the build it came from. None of them is from the release build ([BUILD]); its own results:
[TESTS].

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
- **Golestandt:** lost after two wipes (`9a173f4`; won on test build 5). Dragon fights can fall apart partway.
- **The Green Knight** and **Marfoirwort:** lost (`ccba364`).
- **Legion:** some of his rules are in (his adds and heals are capped, wounded raiders step out of his reach), but the
  raid does not play his platform, the way down or his add waves. **Behemoth** is never fought; **Fomor City** and
  **Morgana** have no raid.
- **Xanxicar's stomp:** a realm learns him over its attempts. Still being tuned.

Fixes made after the last raid builds the owner ran are **tests only**:
- **Summoner Roesia:** the split camps for her Summoner Pain made the raid worse and are removed. Raid healers now cure
  Summoner Pain before a top-up heal when it is on two or more raiders and nobody in their party is in danger. On
  `9a173f4` they never did (a heal always came first). **Tests only.** Before the camps the raid also lost to her.
- **Raid cures in general:** healers were seen curing poison and mesmerize on `9a173f4`, but up to a dozen cured the same
  raider in the same second. Each curer now claims its target. **Tests only.**
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

- **Battlegrounds are an endless fight.** Bots storm the keep lord again and again and almost always fail; no
  battleground keep has changed hands.
- **Open-field regroups pass between groups more than planned** in dense warbands: 17.4% of regroups on `9a173f4`
  against a 15% bar. None was passed on a second time.
- **Roaming eight-man groups** rarely find a fight and move on (under investigation).
- **Some gamebots wait a long time at frontier teleporters** before the server moves them on.

## Chat

- **/faction runs at about 5 to 7 lines a minute on a full realm, by design** (owner, 2026-11-18), never more than 7.
  About 5 a minute was seen on `5154cae`. Lower **How much bots talk** in the Living Server tab if it's too much.
- **Fixed after the owner's chat check on `5154cae`, tests only:** "is it far?" now gets a travel answer, not a place's
  description; a bot-to-bot answer no longer lands before yours; a call for help gets an offer or an honest no, not a
  story; another realm's zone gets "thats hib frontier, rvr only", not a route; "an armorcrafter", not "a armorcrafter".
- **"omw" means coming** (tests only to the end): a bot that says it has to go, with a deadline. A reply that is never
  sent leaves a short walk behind it (up to 210 seconds).
- **Replies still guess from your words.** A bot doesn't know what you are doing beyond what you type. A bot that
  remembers you may ask your level right after you said it. Chat has been tested one line at a time more than as long
  conversations.
- **Bot parties never take a new member once they set off**, so an "lfm" from a bot party is a last call while it forms.

## Bots that play with you, and companions

- **Bot-led groups:** gamebots don't share your horse or boat; the leader takes its own and waits at the other end. The
  break at the end of a session ends the party.
- **A bot that offers to group may walk on before you meet**; if it's too far by the time you invite, it says where it is.
- **Hired companions:** joining your fight on a grey monster failed on `b1b6846` and is fixed: **tests only**. The healer's
  [companions] and [hire] links work only with the healer targeted; typed `/hire` words always work. The gear window's
  title reads "House Vault 0" (it reuses the house vault window).
- **Bots don't fall when they die.** The original client plays the lying-dead pose only for players.

## Bots as people and 2003 builds

Most of it is **tests only**: mistakes and their rate, asking the way, milestone talk, raid learning, and the era class
mix on a fresh world. No raid has been fought by an era-built world yet. Whether the original client shows a sitting bot
is still to be checked. [Owner: the PC notes of `b1b6846` record skill tiers, growth, places learned and movement counts
as seen in the log; PLAYED-CHECK-1.2.md still says "Not run". Confirm before this says "seen in the log".]

## Crafting and the economy

- **Crafting as a player** hasn't been seen yet, and needs the original client.
- **Trading with a crafter bot:** tried on `b1b6846`; a reply was cut mid-word, a different piece was offered, and the
  agreed deal lapsed. Fixed: **tests only**. No trade has been finished yet.
- **Craft orders** often lapsed (156 made, 163 expired on `19e7037`). Orders now go only to crafters who stay online and
  are withdrawn from busy ones: **tests only**.
- **Few crafted pieces sell on the Exchange**, mostly because most bot work is practice pieces.
- **The launcher's crafter card and economy page** haven't been checked on the owner's PC.

## Lore

The loremasters' dialogs, the lore books, the Bestiary page and the Herald's chronicle are **tests only**. The `/lore`
window, boss yells, atmosphere lines and first-kill lines were seen on `1dc66b6`.

## Play with friends (experimental)

- It needs the original client; the new client can't use Join Friend yet.
- Only a join over a home network has been seen (test build 7). A session played together and a character passport trip
  between two PCs are **tests only**.

- **Bot guilds stay in your world when you turn them off.** Their guilds, alliances and any keeps
  they claimed remain until you remove them. To remove them, close the game and server and run
  `python tools\claude-version\set_bot_guilds.py --purge --apply` in your game folder. It backs up
  your database first.
- **Rolling back the install doesn't remove saved data.** `ROLLBACK.cmd` restores the files the
  install replaced. The new database tables and settings stay; the original server ignores them.
  Remove bot guilds first (above) if you used them. If you installed more than one build, run
  `ROLLBACK.cmd` once per build.
- **A maximized launcher ignores clicks on START SERVER, STOP SERVER or ENTER REALM (seen on the owner's PC,
  2026-11-05 and 2026-11-18, build b1b6846).** Maximized, those buttons sit at the top right of the screen; when another
  window is on top there, the click goes to that window, so nothing happens and nothing is logged, while the tabs further
  left still answer. On the owner's PC that window was most likely the Claude app's (the helper's screenshots mask it; the
  2026-11-05 notes name it; on 2026-11-18 it wasn't confirmed). Restoring the window
  made STOP SERVER work first try both times. The launcher itself puts nothing over the buttons at any size up to 4K
  (`LauncherWindowSizeTests`, tests only). Move the other window or use the launcher at its normal size.
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
  - A bot that answers "omw" (or "coming", "ill be there") to an alarm, a keep call, free realm points, a call for help, heals
    or a rez now has to go there, with a deadline, and a bot that can't go says something else (1.2, tests only; the
    `CHAT_PROMISE` lines in the server log show each promise made, kept or broken). A promise is made when the reply is
    chosen: a reply that is then never sent leaves a short walk behind it (up to 210 seconds) before it ends `unsaid`.
  - Bots don't know what you are doing beyond your words: a "gz" is answered only by a bot that just
    levelled, and other replies guess from the words alone.
  - Chat volume: about 5 to 7 /faction lines a minute on a full realm, by design; see "/faction is busy by design" in
    [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md). On build `c81eb5d` it settled at 3.9 a minute when no scene played;
    the budget now keeps a minimum of 5 (tests only, not yet counted on the owner's PC).
  - Chat has been tested one line at a time, not as conversations over several turns.
- **Added the 0.37c new client after installing this?** Run `INSTALL.cmd` again so the new client's
  launcher gets the Living Server tab and its gate fixes too.
- **Versions:** Living Server is for Offline DAoC 0.37 / 0.37b / 0.37c only, and the installer refuses anything
  else. On 2026-10-31 shadowofze put Offline DAoC on hold ("will not receive any more updates"; the 0.37 downloads
  stay up), so there is no 0.38 to wait for: 0.37 stays the game, and the later fixes from his unreleased work
  come to you through Living Server releases instead. If an update of his ever does appear, run
  `PREPARE-FOR-UPDATE.cmd` before applying it (it also puts the released game files back, all but the Living Server
  loading screen) and wait for a Living
  Server build for that version before installing one again.
- **The new client's own fixes in Living Server's source weren't shipped (Beta 1.0 and 1.1).** `/target <name>`
  filling the target window, the FACE/FOLLOW/STICK buttons, the stick toggle, `/lfg` and `/trade` shown as realm
  channels and the PLAY double-click guard were fixed in `source/new-client` and played on the owner's PC, but the
  installer only ships the gate models; a 0.37c player has shadowofze's unchanged client. The quest-marker colour
  fix (1.1.1) is the first client script the installer carries, and the others follow the same way in 1.2.
- **Memory over very long sessions:** Windows handles grow about 1,700 an hour with 600 bots per realm
  (a lock inside each monster's stat table keeps the event it waited on once). That is a few MB of
  Windows memory a day; the server's own memory stays flat (about 1.7 GB live at 1,800 bots).
- **Bots in your own guild (0.37):** inviting gamebots into your guild is now the base game's feature. Those
  bots don't count for your guild's merit, realm points, keep claims or buffs (the base game's choice); bots in
  a bot-run guild still do for theirs. With bot voices on, bots in your guild talk with Living Server's
  own lines; with them off, they use the base game's own lines. Not played in game yet.
- **Raids:** Darkness Falls, Legion and the other level 70+ encounters are won on numbers: their special
  mechanics aren't scripted for bots, and a raid can still lose. Raided in game so far: the three dragons,
  Galladoria, Summoner's Hall, Darkness Falls, Avalon City, Crystal Cave, Marfach Caverns and Hall of the
  Corrupt. Lord Gildas fell to a live raid on Beta 1.0 (2026-10-22, 180 bots in 61 minutes); the Trollheim route
  fix was proven against the real pathfinding library but hasn't run in a live raid yet. Late Hibernian raiders once stood 33 minutes on the Cruachan Gorge
  border before finding their way; the cause isn't found yet. Fomor City is not raided (no navmesh path to
  Myrddraxis).
- **Moving to 0.37:** `PREPARE-FOR-UPDATE.cmd` was used for a real update (the owner's 0.36c game to 0.37c).
  `carry_social_tables.py` was tested on test folders and databases only.

## Launcher, install and updates

- **Updating a 1.1.4 game through Setup, a new install from Setup, and a 0.37 (class-off) world** haven't been run on the
  owner's PC for 1.2. CI runs them: **tests only**.
- **Updating a 0.36, 0.36b or 0.36c game** through Setup is **tests only** for 1.2. On 0.36 with the Social Expansion,
  run `PREPARE-FOR-UPDATE.cmd` from the package first.
- **The online count on the main screen** stuck at "5 online" for half an hour on `5154cae`. Fixed: **tests only**.
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
