# Period loot and merchants (#575): the diff against our world, and what waits on the owner

Report only. Written 2026-11-26 by the Account #2 session. **No world row, loot rate or price is changed.** No row from
another project is copied here: this file holds counts, names and the method. The counts come from
`source/tools/period_loot_diff.py`, which anyone can run again on a world copy (see "Run it again").

## Summary

- **Our base world.** Compared: the world a new install starts from, the `editions/0.36-no-custom-class` database of
  the 0.36b base download (Living Server's copy, `OfflineDAoC-base-files` v0.36b, zip entry CRC `1015451335`, sha256
  `4fdb7ca498500e8a...`), read-only. The 0.37 world update on top of it (`db-0.37.json`) changes no `LootTemplate`,
  `MobXLootTemplate` or `MerchantItem` row, and none of the alchemy items, so the counts hold for 0.37 too. Since 1.0 a
  Living Server release changes world rows only through a one-time start-up repair (`living_server_world_repairs`,
  `FrontierKeepLevelRepair`); there is no other world update path in 1.x.
- **Drops (True 1.65 `drops165.json`, 2,325 monsters).** Our world has named loot for only 352 of them. **1,198 have a
  `MobXLootTemplate` row whose loot template holds no item** (1,110 of them spawned), and 703 have no loot row at all, so
  all 1,901 drop only coins and random items today. 72 are not in our world. For the 352 with loot: 5 rows the same,
  149 with another chance, 2,805 items True 1.65 has and we lack, 1,018 items only we have.
- **The Uthgard import has the same gap.** `uthgard_drops_content.py` counts any `MobXLootTemplate` row as "has loot"
  (`loot_names()`), so it skips every monster with an empty table. The 1,198 above (and the Uthgard monsters like them)
  would get nothing from it. This is a bug in that tool against its own rule ("loot for monsters with no loot").
- **The issue's True 1.65 commits:**
  - `c52f109a` (Trollheim): Olen den Bartender and Snemel den Forsumlig are spawned with empty loot tables; every item
    True 1.65 gives them is in our `ItemTemplate`. Remandun den Laerd is not spawned in our world (we have his loot
    row, the Hooded Mantle at 22%). True 1.65's own text: his "exact period coordinates are unavailable"; its 25%
    chances are its own estimate (its drops report: "named magical 15 %, raid/epic 25 %" when no Uthgard figure exists).
  - `46d1a00d` (Tepok's Mine): Overseer Tepok is spawned with an empty table. True 1.65's Robe of the Overseer at 35%
    is Prima's figure; Water Opal 8% has no named source. The Goblin Imperator is only an `NpcTemplate` in our world
    (no spawn), so his eight drops would do nothing until he is spawned.
  - `cdd9515e` (dye merchants): the three merchants stand in True 1.65's Traveler's Inns, which our world doesn't have
    (TRUE165-CONTENT.md, "Hired companions"). 294 list rows, 4 items we lack; 65 of our own merchant lists (the
    capital-city dye merchants) already sell these dyes.
  - `c41415df` (alchemy prices): **all 551 items differ**: our price is True 1.65's "old price" in every one. 382 would
    go up (dyes and enamels about 1.8 to 2 times), 169 down (the elixirs, e.g. Strong Elixir of Healing 75,000 to 25,000).
    Its source: Prissy's Alchemy Recipe Reference (tradeskills.de), at the 50% `item_sell_ratio` both worlds use.
- **Thidranki (db-public D2): nothing to take.** db-public's Thidranki is region 238, the later live map. Our
  battleground Thidranki is region 252 (its `Regions` row says "Thidrankiold"; `BattlegroundBrackets.cs`, the lore file).
  db-public's 12 sellers there are: four siegemasters selling the Hills of Claret's siege list (the issue skips Claret),
  three spell-token merchants (skipped), and three armor merchants selling "Hero of Thidranki's" gear and Guardian's
  Mythirians. Mythirians and battleground bounty-point gear are later than 1.65 (patch 1.110: "new armor and weapons
  available for purchase with Bounty Points"; 1.48: "Bounty Points are no longer given in the Battlefields"). None of
  their 297 items is in our world. Our region 252 already has its siegemasters (Salendar, Cadman, Ordrand, Sigfreid,
  Camey, Erinden, each with a five-row siege list) and the Void Merchants.
- **Uth-Moras (D5): audit only, nothing changed.** Of 177 Malmohus, 33 Summoner's Hall and 277 Darkness Falls items in
  its Midgard file, 186 match our item, 231 differ, 70 have no item of that name in our world. The differences are
  bonuses (131), level (90), bonus level (88), DPS (19), armor factor (27), speed (12). The file is Uthgard 2.0's
  (2018), player-collected, and its README says items "have changed, no longer drop, or have never been available on
  Uthgard". It is not a period source, so no correction is written from it.

## Sources

| Source | Version | Reached? | Licence |
|---|---|---|---|
| zomboden/OfflineDAoC-True165 | `e2af3f2` (the harvest's), checked against `main` `50c407d` | Yes (git) | GPL-3.0 |
| Eve-of-Darkness/db-public | `e0e6ed2`; the Thidranki commits `7141a32`, `e7a1669`, `6d3e623` | Yes (git) | MIT since `e0e6ed2`; the rows are Mythic game data |
| Kabouik/Uth-Moras `items.xml.midgard` | `0024157` | Yes (git) | none: audit reference only |
| OfflineDAoC-base-files v0.36b / v0.37b | release assets | Yes (only the world entry, by byte range) | ours |
| Camelot Herald patch notes (fandom mirror) | 1.48, 1.60, 1.110 | Search results only: the pages and web.archive.org are refused by the network proxy (HTTP 403) | |
| Prima Atlas, period Allakhazam pages | | Not reached; cited only as True 1.65 cites them | |

True 1.65's `drops165.json` has no per-row source. Its drops report (`tools/claude-version/true165/audit_fix_2026-10-31/
drops/REPORT.md`) gives only totals: 8,553 rows from Uthgard's measured drops on that monster, 2,262 from period
Allakhazam "Known Loot", 590 from live Allakhazam pages with an early comment, 214 from period item pages; and for rates
not measured on the monster, 1,160 from Uthgard's rate on another monster and 1,906 estimates (3%, 15%, 25%, 20%). The
per-row evidence (`work/drops_report.json`) is not in its repository. So the issue's "label each row's source" can be
done only for the commits above, from their own text.

## Run it again

```
python -I source/tools/period_loot_diff.py --world <copy of a world>\opendaoc.sqlite3.db ^
    --true165 <OfflineDAoC-True165 checkout> --dbpublic <db-public checkout> --moras <items.xml.midgard> --report out.json
```

The world is opened read-only (a test checks the file is unchanged). On the owner's PC, run it on a copy of the 1.2.3
test install's world to confirm the counts on the release world.

## Owner questions (`needs-owner`)

1. **Fill empty loot tables?** Should a monster whose loot table holds no item count as a monster with no loot, so it
   gets period drops? Recommended: yes. Today 1,901 of True 1.65's monsters drop no named item in our world, named ones
   like Olen den Bartender and Overseer Tepok among them. The "real loot" rule keeps every item a monster already drops;
   an empty table drops nothing.
2. **Which rates?** For those monsters:
   - (a) Uthgard's measured rates only (your 2026-11-21 decision for the Uthgard import), plus the figures a period
     source gives (Prima's 35% Robe of the Overseer). Recommended.
   - (b) also True 1.65's estimates (25% for the Trollheim named items, 1% for the Hob Hunter Ring and the other
     estimated rows).
   - (c) neither: leave them coin-only.
3. **Monsters with loot (352):** keep ours as is (the "real loot" rule; recommended), or add the 2,805 items True 1.65
   has and we lack, or take its chance on the 149 that differ?
4. **Remandun den Laerd:** spawn him in Trollheim at True 1.65's spot (level 69, its own pick on walkable floor, no
   period coordinates), or leave him out? Recommended: leave him out until a period spot is found.
5. **Alchemy prices:** take True 1.65's 551 period prices? It changes crafting income both ways (dyes up about 2x,
   elixirs down to a third), for players and for bots that sell. Recommended: yes, as one change with its own changelog
   line, since it rests on a named period reference; but it is an economy change.

Not asked: the dye merchants (they need True 1.65's inns, which TRUE165-CONTENT.md keeps for a later world update) and
db-public's Thidranki (nothing in it is 1.65).

## Plan once answered (each step its own pull request)

1. **This PR:** the diff tool, its tests and this report.
2. **The Uthgard tool's empty-table gap** (if question 1 is yes): count a `MobXLootTemplate` row as loot only when one
   of its templates has an item; a test with an empty table.
3. **A one-time loot repair at server start** (`living_server_world_repairs`), the way `FrontierKeepLevelRepair` does
   it, so fresh installs and existing worlds both get it. It adds `LootTemplate` rows only, for a monster whose tables
   are still empty and only items the world has, with ids marked as its own, and logs each one. A rollback removes
   exactly those rows. Its test: apply, check rows, run again (nothing changes), roll back. The rows are the ones
   question 2 allows. It never touches a save table.
4. **Alchemy prices** (if question 5 is yes): the same kind of repair, a row changed only while it still holds the
   old price; test and rollback as in step 3.
5. On the PC: kill a few of the repaired monsters and check the named drops, and the start-up log line. Tests only until
   then.

## Aside: later-era bonuses in our merchant lists

While matching items, the tool's check found 7 loot rows and 13 merchant rows of lists a spawned merchant uses that carry a later-era archery
bonus type (115 or 168: Skill_Archery, AllArcherySkills), for example the Orb merchants' Glacier Skins and four
Darkness Falls pieces (two vests, a bracer, a sword) on unnamed lists. Outside this issue; worth its own look.
