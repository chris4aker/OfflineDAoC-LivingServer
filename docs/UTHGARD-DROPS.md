# Uthgard's drop tables in the server (1.2.1)

Owner decision, 2026-11-21: "import Uthgard's drop tables" into the server, so the original client (and the new client
in server mode) gets the period drops the new client's offline mode already has (`build_loot.py`).

## What is imported

`source/tools/uthgard_drops_content.py` adds rows to a **clean reference world** only. A player's world gets them
from the next release's world update (`make_patch.py` diffs the reference world against the released one; the patcher
applies the difference once).

- **Loot for monsters with no loot.** For each monster of the world (a `Mob` or `NpcTemplate` of realm 0) that the
  Uthgard bestiary has item drops for, and that has **no** loot row in the world (`MobXLootTemplate`, `LootTemplate`,
  `MobDropTemplate`, `DropTemplateXItemTemplate`, or template-id loot on its spawns), it writes:
  - one `LootTemplate` row per item (id `uthdrop:<monster>:<item>`)
  - one `MobXLootTemplate` row (id `uthdrop:<monster>`)
- **Items the world lacks.** It adds the `ItemTemplate` row for each of those drops, taken from:
  - the old DB (`--old-db`) when the old DB has an item of that name
  - otherwise `loot_item_rows.json` (`uth_...` items: Uthgard gear, or vendor trash at Uthgard's sell value)
- **Items the world's own loot rows lack.** `LootTemplate`, `LootOTD` and `DropTemplateXItemTemplate` rows that name a
  missing item get it from `drop_item_rows.json` or `loot_item_rows.json`. These are the rows behind the start-up
  warning `LOOT_ITEMS_MISSING`.

## What is kept

- **Every loot row the world already has.** This is the "real loot" rule. A monster with any loot keeps its loot, and
  the tool adds nothing for it.
- **The random-item generator (ROG)** and its about 14% chance a kill. The bots' gear economy needs it.
- **Coins (`LootGeneratorMoney`).** Uthgard's "bag of coins" rates are not imported.
- **No new one-time drops.** Uthgard's data has none, so no `LootOTD` row is written.
- **Uthgard's "small chest" / "large chest" are left out.** They are Uthgard's random-item containers.
- **Every existing `ItemTemplate` row stays as it is.**

## How a rate becomes a chance

- **The server's roll.** It rolls each `LootTemplate` row as `Chance >= d100` (`LootList.GetLoot`). So `Chance` is a
  whole percent, and 100 always drops.
- **The conversion.** Uthgard's rate (the percent of kills that dropped the item) is rounded half up and capped at 100.
- **Rare drops.** A rate under 0.5% would round to 0, which never drops:
  - `--rare round-up` (the default) gives it 1%
  - `--rare skip` leaves it out
  - the report counts both (`chance_rounded_up_to_1`, `left_out_rare`)
- **DropCount.** The `MobXLootTemplate` row's `DropCount` is the monster's item count. So every item that wins its
  roll drops, as Uthgard measured each rate on its own. The new client's offline mode rolls the same way.

## How names are matched

- **Monster names.** The server finds loot by the monster's name in lower case (`LootGeneratorTemplate`). Uthgard's
  names are lower case, so names match without regard to case. The rows are written in the world's own spelling of the
  name (the one most of its spawns use).
- **Item names.** As `build_loot.py` does: first the item of the monster's realm (from its region: under 100 Albion,
  under 200 Midgard, under 300 Hibernia, Darkness Falls none), then realm 0, then any.

## Era and safety

- **Era.** Every `ItemTemplate` row is checked again: no Trials of Atlantis or later bonus type (the same check as
  `true165_prima_content.py`). A row whose spell the world lacks is refused too.
- **Played worlds are refused.** If any saved-progress table of `progress-policy.json` has rows, the tool changes
  nothing. A release world's three starting guilds are world data, not progress.
- **Dry run first.** Without `--apply` it only reports. A mistyped path never makes an empty database.

## On the owner's PC

The pipeline data files (`uthgard_drops.json`, `loot_item_rows.json`, `drop_item_rows.json`) are only on the owner's
PC, in the new client pipeline's `data` folder (`<new-client>\pipeline\data`). They are never committed.

1. **Make sure the pipeline data is current**, in `<new-client>\pipeline`:
   - `python parse_uthgard_drops.py`
   - `python build_loot.py`
   - `python build_loot_items.py`
   - `python build_drop_items.py`

   This step is only needed if they were never run or the bestiary changed.
2. **Dry run** on a copy of the last clean reference world (never the played install):

   ```
   python -I source\tools\uthgard_drops_content.py --pipeline-data <new-client>\pipeline\data ^
       --world <copy of the reference world>\opendaoc.sqlite3.db ^
       --old-db <new-client>\external\old_project\runtime\data\opendaoc.sqlite3.db --report uthgard-dry-run.json
   ```

   Read the summary line and `uthgard-dry-run.json`.
3. **Build the release with the drops.** `build_release_037.py` takes two new arguments and runs the tool with
   `--apply` on the clean world. It does this before it copies the 0.37 edition's database, so both editions get the
   rows:

   ```
   python -I source\tools\build_release_037.py --snapshot ... --repo ... --edition-game-dll ... --out <staging>\OfflineDAoC-v0.37b ^
       --uthgard-drops <new-client>\pipeline\data ^
       --uthgard-old-db <new-client>\external\old_project\runtime\data\opendaoc.sqlite3.db
   ```

   - It writes `<staging>\OfflineDAoC-v0.37b-uthgard-drops-report.json` beside the build report.
   - Without `--uthgard-drops` it prints `Uthgard drops: NOT added` and adds none.
4. **Assemble and patch** as usual: `assemble_release_037.py`, then `make_patch.py`. The world update
   (`db-<version>b.json`, `db-<version>.json`) then holds the new `LootTemplate`, `MobXLootTemplate` and `ItemTemplate`
   rows.

True 1.65's Prima content (`true165_prima_content.py`, [TRUE165-CONTENT.md](TRUE165-CONTENT.md)) is run on the
built world afterwards. Its `drops` set only covers the monsters that tool adds, so the two never write loot for the
same monster.

## What to look for

- **The summary line.** Check monsters matched, added and skipped for having loot, the items added, the item names
  still missing, the world references fixed, refused rows, and rare chances rounded up.
- **Warnings.** A warning that `LootGeneratorTemplate` is not registered (in the `LootGenerator` table) means the rows
  would not drop. Look at the world's `LootGenerator` rows before shipping.
- **`refused`.** These are items with a later-era bonus or an unknown spell. Their drops are left out.
- **`items_missing`.** These are Uthgard drop names with no item anywhere. The new client's `MISSING DROP ITEMS.txt`
  lists the same names.
- **In game, on the patched install:**
  - The server log's `LOOT_ITEMS_MISSING` lines should show fewer rows than before.
  - Kill a few monsters that had no loot before (the report's `added` list) and check that they drop their Uthgard
    items, alongside ROG items and coins.
- **Until then, "tests only".** Until this is seen in the owner's PC logs, the notes say so.
