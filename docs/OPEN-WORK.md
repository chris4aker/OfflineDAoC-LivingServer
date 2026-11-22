# Open work (Living Server, from 1.2.1)

Everything known to be unfinished, with a GitHub issue each, so contributors and agents can pick an item up. Read
[AGENTS.md](../AGENTS.md) first: it holds the owner's rules. Branch from `integration/1.2.1` and open a pull request
against it. Comment on an issue before starting so two people don't take the same one.

A fix counts as working only once it is seen in a log from the owner's PC. Until then it is "tests only", and every
result names the build it came from. Player-facing status is in [KNOWN-ISSUES.md](KNOWN-ISSUES.md); every change is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

## Lore

Read [docs/1.2.1/HEROES.md](1.2.1/HEROES.md) and [docs/1.2.1/HANDOFF.md](1.2.1/HANDOFF.md) first. Every batch gets an independent review, and the owner sees samples before it merges.

- [#399](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/399) Lore: no real companies and nothing that breaks the fourth wall in player-visible text (owner decision 8)
- [#400](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/400) Lore: the companion-name test should check every legend name the lore tells of (owner decision 7)
- [#401](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/401) Lore: rewrite the Avalon City summary in fresh words
- [#402](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/402) Lore: bring the epic bosses up to the epic floors
- [#403](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/403) Lore: bring Midgard's and Hibernia's major bosses up to the depth floors
- [#404](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/404) Lore: bring the places up to the depth floors (mostly facts, book pages and loremaster topics)
- [#405](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/405) Lore: give Albion and Hibernia an epic voice of their own
- [#406](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/406) Lore: vary the "The X say" teller line that opens most new passages

## Epic raids

Never weaken a boss. A raid plan counts as working only once it is won on the owner's PC; most of these need the owner's raid log first.

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

- [#416](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/416) Battleground keeps rarely change hands
- [#417](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/417) Roaming eight-man RvR groups rarely find a fight
- [#418](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/418) Gamebots wait a long time at frontier teleporters
- [#419](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/419) Open-field regroups pass between groups more than planned
- [#420](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/420) Connacht: repeated route rescues uphill to a hunting camp (200:314846,471535)
- [#421](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/421) Lough Derg / Bri Leith seam loops at region 200

## Other bot behaviour

- [#422](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/422) Crafting: a trade with a crafter bot has never been finished, and few crafted pieces sell
- [#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423) Bot parties never take a new member once they set off
- [#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424) Click-targeting a bot in the original client (unconfirmed)

## Checks on a PC

- [#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425) 1.2.1 PC check: the original client's handshake build, and the bot death fall

## The 1.2.1 check on the owner's PC

PR [#398](https://github.com/chris4aker/OfflineDAoC-LivingServer/pull/398) passes CI, but nothing in it has been seen in game.
Build it, then check (GM account where noted):

1. **Death fall** ([#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425)): `CLIENT_HANDSHAKE ... client=original` in the log; `/lstest deathanim`, `raise`, `release` on a targeted bot.
2. **Rez:** kill a bot and cast a resurrection spell with nothing selected; it goes to the bot.
3. **Companions:** a 1.2 companion catches up once (`HIRED_COMPANION_RAISED level=5->NN`, `training_weapon_replaced=`); log in again and there is no second raise; a new hire at level 20+ arrives trained and in merchant gear (`HIRED_COMPANION_TRAINED`).
4. **`/hire train`** by line name: `/hire train 1 smite 10`.
5. **Thidranki:** killing enemy gamebots gives experience.
6. **Pets:** an Animist places more than 12 turrets; companion and gamebot Animists keep the cap.
7. **/faction:** no minute under about 5 lines on a full realm, none over 7.
8. **Bot schedule:** a world that ran 1.2 logs `bot_schedule_mode set to the new default off`; the whole roster is online.
9. **Loot:** in a gamebot-led group your kills drop the monster's named loot; the start-up log has one `LOOT_ITEMS_MISSING` line per source.
10. **Uthgard drops:** run `source/tools/uthgard_drops_content.py` (dry run first) as [UTHGARD-DROPS.md](UTHGARD-DROPS.md) says.
11. **RvR:** `RVR_DEFENSE_MARCH` never shows `assigned=0`; `RVR_KEEP_ROUTE_RESTART` for Albion in region 1 shows `to=` near Castle Sauvage; a GM `/jump` into an enemy keep stays there.
12. **Linux (Ryan):** the launcher's tooltips and alt-tab under Wine (#393) and `linux/start-offline-daoc.sh` (#395), both merged to main.

## Not planned

- [#209](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/209) Crownfire Lance (Pendragon Sovereign): not part of Living Server.
- Random magic items and coin stay as they are (owner, 2026-11-21); Uthgard's drops are added on top.
