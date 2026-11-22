# Open work (Living Server, from 1.2.1)

Everything known to be unfinished, with a GitHub issue each, so contributors and agents can pick an item up. Read
[AGENTS.md](../AGENTS.md) first: it holds the owner's rules. Branch from `fix/1.2.1` and open a pull request
against it (it merges into `integration/1.2.1`, then main). Comment on an issue before starting so two people don't take the same one.

A fix counts as working only once it is seen in a log from the owner's PC. Until then it is "tests only", and every
result names the build it came from. Player-facing status is in [KNOWN-ISSUES.md](KNOWN-ISSUES.md); every change is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

**Status on 2026-11-22** (`fix/1.2.1` at `d56adeda`): every 1.2.1 change below is **tests only**; nothing in 1.2.1 has
been seen on the owner's PC. 1.2.1 ships that way ([1.2.1/RELEASE-PLAN-1.2.1.md](1.2.1/RELEASE-PLAN-1.2.1.md)); the
player notes are [RELEASE-NOTES-1.2.1.md](RELEASE-NOTES-1.2.1.md). Closed since 1.2: #399, #400, #402, #403, #404,
#419.

## Lore

Read [docs/1.2.1/HEROES.md](1.2.1/HEROES.md) and [docs/1.2.1/HANDOFF.md](1.2.1/HANDOFF.md) first. Every batch gets an independent review, and the owner sees samples before it merges.

- ~~[#399](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/399) Lore: no real companies and nothing that breaks the fourth wall in player-visible text (owner decision 8)~~ **Closed.** About 860 lines reworded; `tools/lore/fourth_wall.py` is strict in CI.
- ~~[#400](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/400) Lore: the companion-name test should check every legend name the lore tells of (owner decision 7)~~ **Closed.**
- [#401](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/401) Lore: rewrite the Avalon City summary in fresh words. **Written** in 1.2.1 (`6ba1ab30`, reviewed in [REVIEW-FOURTH-WALL](1.2.1/REVIEW-FOURTH-WALL.md)); the issue is still open for the owner to close.
- ~~[#402](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/402) Lore: bring the epic bosses up to the epic floors~~ **Closed.** All 157 epic bosses at the floors; three realm reviews.
- ~~[#403](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/403) Lore: bring Midgard's and Hibernia's major bosses up to the depth floors~~ **Closed** (review: [REVIEW-DEPTH](1.2.1/REVIEW-DEPTH.md)).
- ~~[#404](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/404) Lore: bring the places up to the depth floors (mostly facts, book pages and loremaster topics)~~ **Closed** (review: [REVIEW-DEPTH](1.2.1/REVIEW-DEPTH.md)).
- [#405](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/405) Lore: give Albion and Hibernia an epic voice of their own. **Open.** The epic pages (#402) and the realm voice guides (`docs/1.2.1/VOICE-*.md`, the NPC pass) went toward it.
- [#406](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/406) Lore: vary the "The X say" teller line that opens most new passages. **Open.** Varied in the epic pages; [REPETITION.md](1.2.1/REPETITION.md) and its CI ratchet track the rest.

## Epic raids

Never weaken a boss. A raid plan counts as working only once it is won on the owner's PC; most of these need the owner's raid log first.

**All open, tests only.** Simulation-based fixes go into `fix/1.2.1` by pull request: #431 (#407, #408) merged on
2026-11-22; #428 (#409, #411), #430 (#413, #414, #415) and #432 (#410, #412) open on 2026-11-22. Each issue stays open until its raid is
won on the owner's PC.

- [#407](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/407) Caer Sidi: the bot raid loses at the Crypt Lord
- [#408](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/408) Galladoria: the bot raid loses at Spindler Broodmother and never reaches Olcasgean
- [#409](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/409) Tuscaran Glacier: the bot raid loses at Queen Kula, Suttung and the Council, and King Tuscar
- [#410](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/410) Summoner's Hall: Summoner Roesia wins every time, and Grand Summoner Govannon has never been beaten
- [#411](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/411) Trollheim: the bot raid times out before Nosdoden falls
- [#412](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/412) The Green Knight and Marfoirwort: bot raids lose
- [#413](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/413) Legion: play his platform, the way down and his add waves
- [#414](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/414) Behemoth, Fomor City and Morgana: no bot raid yet
- [#415](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/415) Dragon fights fall apart partway (Golestandt, Cuuldurach)

## RvR and travel

Fix routes spot by spot; never rebuild navmeshes globally. #420 and #421 need the owner's log lines.

- [#416](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/416) Battleground keeps rarely change hands. **Open, fix tests only:** stormers strike the lord only from within 350 (battlegrounds and frontier; `BG_RALLY_STATUS inLordReach=`, `RVR_LORD_STEP_IN`).
- [#417](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/417) Roaming eight-man RvR groups rarely find a fight. **Open, fix tests only:** patrols weigh enemy RvR bots, eights chase the planner's hunts (`RVR_ROAM_CHASE`).
- [#418](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/418) Gamebots wait a long time at frontier teleporters. **Open, fix tests only:** bots pick a porter that sells their medallion; waits past 5 minutes log `RVR_FRONTIER_WAIT_LONG`.
- ~~[#419](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/419) Open-field regroups pass between groups more than planned~~ **Closed.** 17.4% on `9a173f4` is under the owner's 20% bar; the log checker now shows each call's spread.
- [#420](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/420) Connacht: repeated route rescues uphill to a hunting camp (200:314846,471535). **Open, not changed;** needs the owner's log lines.
- [#421](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/421) Lough Derg / Bri Leith seam loops at region 200. **Open, not changed;** needs the owner's log lines.

## Other bot behaviour

- [#396](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/396) No xp for kills in Thid. **Open, fix tests only:** enemy gamebot kills pay what a player kill pays.
- [#397](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/397) Training hires doesn't work once the skill tree grows outside the training window. **Open, fix tests only:** `/hire train` by line name; the trainer window shows the next skill first.
- [#422](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/422) Crafting: a trade with a crafter bot has never been finished, and few crafted pieces sell. **Open, fix tests only:** the hand-over runs while the break lasts (`ECON_PLAYER_ORDER outcome="ready_told"`, `"handover_tried"`); `ECON_INDEX craftedListed20`.
- [#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423) Bot parties never take a new member once they set off. **Open, fix tests only** (`BOT_LED_RECRUIT ... kind=open_seat`).
- [#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424) Click-targeting a bot in the original client (unconfirmed). **Open:** no server-side cause found; each click is logged (`PLAYER_CLICK_TARGET`) for one played check.

## Checks on a PC

- [#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425) 1.2.1 PC check: the original client's handshake build, and the bot death fall. **Open.** CI's playtester scenarios `bot-death`, `bot-corpse-target` and `bot-rez` pass (on `b644529a`), but they check packets, not what the client draws.

## The 1.2.1 check on the owner's PC

`fix/1.2.1` passes CI (`d56adeda`: 9,258 server tests passed, 0 failed), but nothing in it has been seen in game. Build
it (or install the 1.2.1 release), then check (GM account where noted). Each row moves from tests only to seen in game
once a log shows it:

1. **Death fall** ([#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425)): `CLIENT_HANDSHAKE ... client=original` in the log; `/lstest deathanim`, `raise`, `release` on a targeted bot (`BOT_DEATH_ANIM fall`, `BOT_DEATH_ANIM raised`). With `bot_death_animation` off, nothing about dead bots differs from 1.2.
2. **Rez:** kill a bot and cast a resurrection spell with nothing selected (or `/lstest deathanim rez`); it goes to the bot and the bot stands up.
3. **Companions:** a 1.2 companion catches up once (`HIRED_COMPANION_RAISED level=5->NN`, `training_weapon_replaced=`); log in again and there is no second raise; a new hire at level 20+ arrives trained and in merchant gear (`HIRED_COMPANION_TRAINED`).
4. **`/hire train`** by line name: `/hire train 1 smite 10`.
5. **Thidranki:** killing enemy gamebots gives experience.
6. **Pets:** an Animist places more than 12 turrets; companion and gamebot Animists keep the cap.
7. **/faction:** no minute under about 5 lines on a full realm, none over 7.
8. **Bot schedule:** a world that ran 1.2 logs `bot_schedule_mode set to the new default off`; the whole roster is online.
9. **Loot:** in a gamebot-led group your kills drop the monster's named loot; the start-up log has one `LOOT_ITEMS_MISSING` line per source.
10. **Uthgard drops:** run `source/tools/uthgard_drops_content.py` (dry run first) as [UTHGARD-DROPS.md](UTHGARD-DROPS.md) says.
11. **RvR:** `RVR_LORD_STEP_IN` at sieges and battleground storms, `RVR_ROAM_CHASE` for roaming eights, no `RVR_FRONTIER_WAIT_LONG` storm; `RVR_DEFENSE_MARCH` never shows `assigned=0`; `RVR_KEEP_ROUTE_RESTART` for Albion in region 1 shows `to=` near Castle Sauvage; a GM `/jump` into an enemy keep stays there.
12. **Linux (Ryan):** the launcher's tooltips and alt-tab under Wine (#393) and `linux/start-offline-daoc.sh` (#395), both merged to main.
13. **Click-targeting a bot** ([#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424)), original client, your own mouse (not a tool): left-click a living gamebot standing still near you, then a moving one, then a companion. Each click should select it and log `PLAYER_CLICK_TARGET ... kind=gamebot ... result=set alive=True`. No line at all: the client never sent the click (its picking); `result=no_object` or `out_of_range`: the server dropped it.
14. **A party picks up one more** ([#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423)): `BOT_LED_RECRUIT outcome=searching kind=open_seat` for parties that set off, then `outcome=recruited kind=open_seat` now and then; in /lfg a travelling party's "lf1m ..." and "ya, 1 spot" to "room?". Join one by /lfg or a tell while it travels.
15. **Raids** (#407-#415): any raid won, with its log (each issue's raid).

## Not planned

- [#209](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/209) Crownfire Lance (Pendragon Sovereign): not part of Living Server.
- Random magic items and coin stay as they are (owner, 2026-11-21); Uthgard's drops are added on top.
