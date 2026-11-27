# Client recipe check (#580): the client's recipe list against our world

Written 2026-11-27 by the Account #2 session. **Step 1 of 3: the checker and its tests (tests only).** No world row is
changed, and no client data is in this file or anywhere in Git. The diff itself needs the original client, so it is
run on the owner's PC (step 2); its names-and-counts report then goes under "Results" below.

## Why

The original client draws its crafting window from its own recipe file, `tdl.crf` (inside `ifd.mpk`), and sends the
server only a recipe id (`MakeProductHandler` reads a `ushort`; `GamePlayer.CraftItem` calls `RecipeDB.FindBy`, which
looks up `CraftedItem.CraftedItemID`). A recipe the window offers that the world lacks, or that the server hides, used
to do nothing ("like the hotkey was dead", player report 2026-11-08). Since Crafting 2.0 WP1 the player hears "That
recipe cannot be made on this server." (`UT_CraftRequestReplies`), but the recipe still can't be made.
[CRAFTING-M0-FINDINGS.md](../CRAFTING-M0-FINDINGS.md) checked the world's 8,479 `CraftedItem` rows against the world
itself (40 hidden); nothing has checked them against the client.

## Upstream: atom0s/craft_extract

Read at `0f7de6c6619f9fcb76154c8aa49f3fe1dc2fe66d` (cloned 2026-11-27; the repository is archived). C++ (CMake,
Windows), **AGPL-3.0**. Per [HARVEST-2026-11.md](../upstream/HARVEST-2026-11.md) it is a **separate tool only**: run it,
never copy, link, vendor or ship it, and treat its output as client data. Nothing of it is in this repository; the
checker reads only the files it writes. What matters for reading them:

- **Input:** an already extracted `tdl.crf` (it does not open `ifd.mpk`). Header versions `0x66` and `0x67`; the two
  differ only in field order inside the recipe and profession records.
- **What it exports:** per realm (0 Albion, 1 Midgard, 2 Hibernia), every profession, its categories and their
  recipes: `id`, `profession`, `category`, `name`, `base_material` (an index) and `base_material_name` (from a fixed
  list: "bronze", "iron", ... "fine alloy", arrow and gem tiers), `icon`, `level`, `material_level`, `skill`, and up to
  eight materials (`base_material`, `base_material_name`, `count`, `name`). **A recipe with no materials is skipped**,
  so the checker cannot see those. A recipe listed under two categories is written twice.
- **The window's name** is the base material name, then the recipe name ("bronze" + "short sword"); the checker
  matches the world's `ItemTemplate.Name` against either.
- **Which field is the craft level** is not named by craft_extract (`level`, `material_level`, `skill`). The checker
  compares `skill` with `CraftedItem.CraftingLevel` by default and prints a calibration line (how many id matches agree
  for each of the three fields); if another field agrees more, run again with `--level-field`.
- **Output formats:** mode 1 CSV writes fields **unquoted** (a comma in a name shifts the columns; the checker reads
  such rows by position and reports any it can't place), mode 2 JSON (`{"Albion": [...], ...}`), mode 3 SQLite (tables
  `recipes` and `recipes_materials`; the latter has no realm, so an id used in two realms gets no material list).
  **Use JSON or SQLite.**

The repository's own MPAK reader (`source/new-client/pipeline/daoc_mpk.py`, our code) gets `tdl.crf` out of `ifd.mpk`.

## The checker

`source/tools/client_recipe_check.py` (our code, GPL-3.0, standard library only). Tests:
`source/tools/tests/test_client_recipe_check.py`, on synthetic fixtures written in craft_extract's three layouts (run by
CI's `source/tools/tests` step and `tools/check.sh`).

- **Read only.** The world and a craft_extract SQLite file are opened with `mode=ro`; `--crf` and `--mpk` are only
  hashed (and `--mpk` read in memory). The tests check every input's sha256 before and after. The world side is
  `export_recipe_fixture.build_fixture`, the same tables and the same M0 hidden rule, and it refuses any saved-progress
  table (`progress-policy.json`).
- **Kept out of Git.** The full `--report` JSON holds client data and is refused inside the repository; `.gitignore`
  also ignores `tdl.crf`, `ifd.mpk` and craft_extract's usual output names. The `--markdown` report holds recipe ids,
  names, crafts, levels and counts only.
- **Per client recipe** (by id, as the server looks it up; duplicates counted once):
  - `missing`: no `CraftedItem` row. Listed with any world recipe of the same name and craft under another id.
  - `hidden`: a row the server hides (M0 reason: no ingredients, product missing, ...).
  - `name`, `craft`, `level`, `realm`: a usable row that differs (a recipe can be in several); `realm` is the product's
    `ItemTemplate.Realm` against the client realm (0, "any", agrees with all).
  - `same`: none of these. Material lists are compared apart (`materials differ`), since wording can differ.
  - Professions the checker can't map to a craft are listed by name, not guessed.
- **World only:** recipes the client's window doesn't list (unreachable from the window), by craft.

Smoke run (2026-11-27) on a copy of the base-download world another session fetched for #578 (sha256
`d7013934b8dfc3f0...`) with an empty client list: 8,479 world recipes, 40 hidden, by craft as M0's table. The world
copy's sha256 was the same afterwards.

## Run it (owner's PC, step 2)

Work on copies, outside the repository (for example `C:\temp\580`), with the server stopped or a world copy:

```
copy "<game>\runtime\client-opendaoc\app\ifd.mpk" C:\temp\580\
python -I source\new-client\pipeline\daoc_mpk.py C:\temp\580\ifd.mpk tdl.crf > C:\temp\580\tdl.crf
craft_extract.exe --file C:\temp\580\tdl.crf --out C:\temp\580\crafts.json --mode 2
copy "<game>\runtime\data\opendaoc.sqlite3.db" C:\temp\580\world.db
python -I source\tools\client_recipe_check.py --client C:\temp\580\crafts.json --world C:\temp\580\world.db ^
    --mpk C:\temp\580\ifd.mpk --client-build "<client version>" ^
    --report C:\temp\580\report.json --markdown C:\temp\580\CLIENT-RECIPES.md
```

craft_extract is built from its own repository (CMake) or taken from its author's release; it isn't part of this
project. Paste the markdown summary (names and counts) under "Results" below; the JSON stays on the PC.

## Results

Not run yet: waits on the owner's client (`needs-PC`).

## Step 3

Fix the real gaps as a world update with rollback, in step with Crafting 2.0 ([CRAFTING-2.0-BUILD-PLAN.md](../CRAFTING-2.0-BUILD-PLAN.md)).
Which gaps to fill (and whether a client recipe of a later era than the world's should be added at all) is a choice
for the owner, asked on #580 once the counts are in.
