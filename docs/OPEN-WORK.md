# Open work (Living Server, from 1.2.1)

Everything known to be unfinished, with a GitHub issue each, so contributors and agents can pick an item up. Read
[AGENTS.md](../AGENTS.md) first: it holds the owner's rules. 1.2.1 is released; branch from `integration/1.2.2` and
open a pull request against it (it merges into main at the 1.2.2 release). Comment on an issue before starting so two
people don't take the same one.

A fix counts as working only once it is seen in a log from the owner's PC. Until then it is "tests only", and every
result names the build it came from. Player-facing status is in [KNOWN-ISSUES.md](KNOWN-ISSUES.md); every change is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

**Status on 2026-11-22:** 1.2.1 is released ([RELEASE-NOTES-1.2.1.md](RELEASE-NOTES-1.2.1.md)). Every 1.2.1 change
below is **tests only**; nothing in 1.2.1 has been seen on the owner's PC. Closed since 1.2: #399, #400, #401, #402,
#403, #404, #419. **Closed on 2026-11-22 as done in 1.2.1** (18; each issue's last comment says what shipped, and it
is reopened if the owner's log shows it again): #396, #397, #405, #406, #407, #408, #409, #411, #413, #415, #416,
#417, #418, #420, #422, #423, #424, #438. Still open from 1.2.1: #410, #412, #414 (raids), #421 (a seam loop), #425
(the PC check).

**What comes next:** 1.2.2 is the raid sizes (#441) and small fixes ([1.2.2](#122-in-development)); Uthgard's drop
tables and the fixes from the PC checks come in [1.2.3](#123); the language selector (#437) in [1.3](#13).

## 1.2.2 (in development)

The raid-size release on `integration/1.2.2` (owner, 2026-11-22: 1.2.2 is the raid sizes). **Tests only.**

- [#441](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/441) Raid sizes raised at Summoner's Hall, the Green Knight, Galladoria and Tuscaran Glacier, where
  the 1.2.1 simulations show working tactics short of bots. **Merged; tests only.**
- The launcher shows the Living Server build ("LIVING SERVER 1.2.2", from `ForkBuildInfo.BuildName`) instead of the
  retired "VERSION 0.37". **Tests only.**
- GM `/jump` refuses a dead bot and says why (its body stays where it fell); a jumped gamebot plans its trip again
  from the new spot (#438 follow-up). **Tests only.**

## 1.2.3

- **Uthgard drops:** run `source/tools/uthgard_drops_content.py` (dry run first) as [UTHGARD-DROPS.md](UTHGARD-DROPS.md)
  says; the tool is in the code since 1.2.1, and its tables ship in 1.2.3 (owner, 2026-11-22).
- **The fixes from the owner's PC checks** of 1.2.1 and 1.2.2 (the list below).

## 1.3

- [#437](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/437) Language selector, German and French (original client only). **Open, not started.** Moved from 1.2.2
  to 1.3.

## Lore

Read [docs/1.2.1/HEROES.md](1.2.1/HEROES.md) and [docs/1.2.1/HANDOFF.md](1.2.1/HANDOFF.md) first. Every batch gets an independent review, and the owner sees samples before it merges.

- ~~[#399](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/399) Lore: no real companies and nothing that breaks the fourth wall in player-visible text (owner decision 8)~~ **Closed.** About 860 lines reworded; `tools/lore/fourth_wall.py` is strict in CI.
- ~~[#400](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/400) Lore: the companion-name test should check every legend name the lore tells of (owner decision 7)~~ **Closed.**
- ~~[#401](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/401) Lore: rewrite the Avalon City summary in fresh words~~ **Closed.** Written in 1.2.1 (`6ba1ab30`, reviewed in [REVIEW-FOURTH-WALL](1.2.1/REVIEW-FOURTH-WALL.md)).
- ~~[#402](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/402) Lore: bring the epic bosses up to the epic floors~~ **Closed.** All 157 epic bosses at the floors; three realm reviews.
- ~~[#403](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/403) Lore: bring Midgard's and Hibernia's major bosses up to the depth floors~~ **Closed** (review: [REVIEW-DEPTH](1.2.1/REVIEW-DEPTH.md)).
- ~~[#404](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/404) Lore: bring the places up to the depth floors (mostly facts, book pages and loremaster topics)~~ **Closed** (review: [REVIEW-DEPTH](1.2.1/REVIEW-DEPTH.md)).
- ~~[#405](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/405) Lore: give Albion and Hibernia an epic voice of their own~~ **Done in 1.2.1** (closed 2026-11-22): 10 Albion and 11 Hibernian chapters in their realms' old forms, reviewed in `docs/1.2.1/REVIEW-EPIC-VOICE.md`.
- ~~[#406](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/406) Lore: vary the "The X say" teller line that opens most new passages~~ **Done in 1.2.1** (closed 2026-11-22); [REPETITION.md](1.2.1/REPETITION.md) and its CI ratchet keep it varied.

## Epic raids

Never weaken a boss. A raid plan counts as working only once it is won on the owner's PC; most of these need the owner's raid log first.

**Tests only.** The simulation-based fixes shipped in 1.2.1 (#431, #428, #430, #432), and six raid issues were closed
on 2026-11-22 as done in 1.2.1; none of those raids has been won on the owner's PC yet, and an issue is reopened if
its raid's log shows it lost again. #410, #412 and #414 stay open; 1.2.2 raises the raid sizes for #410 and #412 (#441).

- ~~[#407](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/407) Caer Sidi: the bot raid loses at the Crypt Lord~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- ~~[#408](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/408) Galladoria: the bot raid loses at Spindler Broodmother and never reaches Olcasgean~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- ~~[#409](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/409) Tuscaran Glacier: the bot raid loses at Queen Kula, Suttung and the Council, and King Tuscar~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- [#410](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/410) Summoner's Hall: Summoner Roesia wins every time, and Grand Summoner Govannon has never been beaten. **Open;** bigger raid in 1.2.2 (#441).
- ~~[#411](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/411) Trollheim: the bot raid times out before Nosdoden falls~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- [#412](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/412) The Green Knight and Marfoirwort: bot raids lose. **Open;** bigger Green Knight raid in 1.2.2 (#441).
- ~~[#413](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/413) Legion: play his platform, the way down and his add waves~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- [#414](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/414) Behemoth, Fomor City and Morgana: no bot raid yet. **Open.**
- ~~[#415](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/415) Dragon fights fall apart partway (Golestandt, Cuuldurach)~~ **Done in 1.2.1, tests only** (closed 2026-11-22)

## RvR and travel

Fix routes spot by spot; never rebuild navmeshes globally. #421 needs the owner's log lines.

- ~~[#416](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/416) Battleground keeps rarely change hands~~ **Done in 1.2.1, tests only** (closed 2026-11-22): stormers strike the lord only from within 350 (battlegrounds and frontier; `BG_RALLY_STATUS inLordReach=`, `RVR_LORD_STEP_IN`).
- ~~[#417](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/417) Roaming eight-man RvR groups rarely find a fight~~ **Done in 1.2.1, tests only** (closed 2026-11-22): patrols weigh enemy RvR bots, eights chase the planner's hunts (`RVR_ROAM_CHASE`).
- ~~[#418](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/418) Gamebots wait a long time at frontier teleporters~~ **Done in 1.2.1, tests only** (closed 2026-11-22): bots pick a porter that sells their medallion; waits past 5 minutes log `RVR_FRONTIER_WAIT_LONG`.
- ~~[#419](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/419) Open-field regroups pass between groups more than planned~~ **Closed.** 17.4% on `9a173f4` is under the owner's 20% bar; the log checker now shows each call's spread.
- ~~[#420](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/420) Connacht: repeated route rescues uphill to a hunting camp (200:314846,471535)~~ **Done in 1.2.1, tests only** (closed 2026-11-22): after two identical rescues for one camp within 30 minutes the bot picks another camp (`AUTONOMOUS_ROUTE_POCKET_ESCAPE_LOOP`, PR #434).
- [#421](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/421) Lough Derg / Bri Leith seam loops at region 200. **Open, not changed;** needs the owner's log lines.

## Other bot behaviour

- ~~[#396](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/396) No xp for kills in Thid~~ **Done in 1.2.1, tests only** (closed 2026-11-22): enemy gamebot kills pay what a player kill pays.
- ~~[#397](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/397) Training hires doesn't work once the skill tree grows outside the training window~~ **Done in 1.2.1, tests only** (closed 2026-11-22): `/hire train` by line name; the trainer window shows the next skill first.
- ~~[#422](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/422) Crafting: a trade with a crafter bot has never been finished, and few crafted pieces sell~~ **Done in 1.2.1, tests only** (closed 2026-11-22): the hand-over runs while the break lasts (`ECON_PLAYER_ORDER outcome="ready_told"`, `"handover_tried"`); `ECON_INDEX craftedListed20`.
- ~~[#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423) Bot parties never take a new member once they set off~~ **Done in 1.2.1, tests only** (closed 2026-11-22; `BOT_LED_RECRUIT ... kind=open_seat`).
- ~~[#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424) Click-targeting a bot in the original client (unconfirmed)~~ **Done in 1.2.1, tests only** (closed 2026-11-22): each click is logged (`PLAYER_CLICK_TARGET`) and the `bot-click-target` playtester scenario covers it; one played check is still on the list below.
- ~~[#438](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/438) Darkness Falls (Albion side): following bots stuck on a step of the entrance stairs; GM `/jump <name> to me` didn't work for bots~~ **Done in 1.2.1, tests only** (closed 2026-11-22). 1.2.2 adds a dead bot's refusal and a gamebot's fresh trip after a jump.

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
10. **Uthgard drops:** moved to [1.2.3](#123).
11. **RvR:** `RVR_LORD_STEP_IN` at sieges and battleground storms, `RVR_ROAM_CHASE` for roaming eights, no `RVR_FRONTIER_WAIT_LONG` storm; `RVR_DEFENSE_MARCH` never shows `assigned=0`; `RVR_KEEP_ROUTE_RESTART` for Albion in region 1 shows `to=` near Castle Sauvage; a GM `/jump` into an enemy keep stays there.
12. **Linux (Ryan):** the launcher's tooltips and alt-tab under Wine (#393) and `linux/start-offline-daoc.sh` (#395), both merged to main.
13. **Click-targeting a bot** ([#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424)), original client, your own mouse (not a tool): left-click a living gamebot standing still near you, then a moving one, then a companion. Each click should select it and log `PLAYER_CLICK_TARGET ... kind=gamebot ... result=set alive=True`. No line at all: the client never sent the click (its picking); `result=no_object` or `out_of_range`: the server dropped it.
14. **A party picks up one more** ([#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423)): `BOT_LED_RECRUIT outcome=searching kind=open_seat` for parties that set off, then `outcome=recruited kind=open_seat` now and then; in /lfg a travelling party's "lf1m ..." and "ya, 1 spot" to "room?". Join one by /lfg or a tell while it travels.
15. **Raids** (#407-#415): any raid won, with its log (each issue's raid); for 1.2.2, the raised sizes (#441).

## Not planned

- [#209](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/209) Crownfire Lance (Pendragon Sovereign): not part of Living Server.
- Random magic items and coin stay as they are (owner, 2026-11-21); Uthgard's drops are added on top.
