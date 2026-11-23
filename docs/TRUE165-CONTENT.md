# True 1.65's period content in Living Server 1.2

Written 2026-11-14 for the 1.2 port from the True 1.65 project ([zomboden/OfflineDAoC-True165](https://github.com/zomboden/OfflineDAoC-True165),
v0.7, `f68cc75`; GPL-3.0), a different version of Offline DAoC that Living Server borrows some code and ideas from. It says which
of True 1.65's world content fits Living Server, how it can reach a player's world, and what is ready.

## How world content reaches a Living Server world

A Living Server package (tools/fork-build) replaces server files and byte-patches a few game files (navmeshes, the
new client's door data, the loading screen). It backs up the world database but **never changes a world row**. World
rows change only in a release of the base game: the 0.36 full downloads, then the 0.37 patch, whose world update
`make_patch.py` builds by diffing the new reference world (from `build_release_037.py`) against the released one; the
patcher applies it once and leaves any row the player's world changed. So True 1.65's content can reach players only
in a base-game world update. There is no runtime content loader in 1.2 (True 1.65's launcher has one; Living Server's
does not), and adding one would mean hand-applying rows to the player's own database at start.

## What fits and what doesn't

| True 1.65 content | Rows | Fits 1.2? |
|---|---|---|
| Prima scripted encounters: Cuinn, Talvor, the Emmisary of Logi, Brungar, Zargin, the panicked syhr'phint, the ixthiar creeper, Celain Eirich, the Unseelie Envoy convoy, the anglator lures, the scourgin, the Star Lake procession, Arr, Thergus, Gormror, Hamorik | 3 scripts; 16 spawns, 8 templates, a 64-point route; 12 world updates naming a class, brain or template on an existing spawn (some also set the encounter's own level, speed and place, e.g. the Unseelie Envoy at 55) | **Yes.** The scripts are in (`scripts/periodencounters`) and compile; every brain they name exists. They do nothing until a world update names them. |
| Prima monsters the world lacked (restored and new, at CapnBry, Uthgard and Prima positions) | 978 + 168 Cornwall spawns, 258 templates | **Yes, the ones 1.2's world lacks.** Many are rows True 1.65 had archived from the same Offline DAoC world, so 1.2 still has them; the tool skips any with the same key or the same monster within 500 in the region. |
| Prima drop items and quest rewards (period Allakhazam and Uthgard item pages) | 1,533 items | **Yes.** None carries a Trials of Atlantis or later bonus (the tool refuses any that would). |
| Prima quests | 10 | **Yes, if 1.2 lacks them** (skipped on a taken id or name). |
| Period drops for those monsters (drops165.json) | for each new monster | **Only for monsters the update adds**, and only when the world has no loot for that name. True 1.65's drop table for monsters 1.2 already has would replace 1.2's loot, which the owner's "real loot" rule keeps; not taken. |
| Prima corrections to existing spawns (levels, aggression, speed) | about 1,090 updates | **Yes (owner, 2026-11-14).** They change monster difficulty in the existing world. The tool applies them with `--with-corrections`, which the release run uses. |
| Keep lords and guards at the 13 frontier keeps (0.7) | 516 | **No.** They put back guards that True 1.65's own placeholder cleanup had removed (2026-10-30). 1.2's world never ran that cleanup. |
| Darkness Falls door by keep count | code | Taken separately (`fa7c366`). |
| Camp bonus caps | none found | Nothing in True 1.65 to take. |

## The tool

`source/tools/true165_prima_content.py` prepares a release's reference world: it adds the rows above that the world
lacks, refuses any world with saved progress, keeps True 1.65's values, writes only columns 1.2's tables have, and
reports every row it added, skipped or refused. Without `--apply` it changes nothing. Tests:
`source/tools/tests/test_true165_prima_content.py` (made-up worlds, and a read of True 1.65's own files when that
checkout is beside this one).

A dry run of True 1.65's files against an empty world (so every row counts as missing) would add 1,163 spawns, 266
templates, 1,533 items, the convoy route and 10 quests. Against 1.2's real reference world it will add fewer; that run
hasn't been made, because the reference world isn't in this repository.

Uthgard's period drop tables for the monsters the world already has but has no loot for are a separate tool,
`uthgard_drops_content.py` ([UTHGARD-DROPS.md](UTHGARD-DROPS.md)), run inside `build_release_037.py --uthgard-drops`.
It runs before this tool, so it never touches the monsters this tool adds. This tool's `drops` set still gives those
monsters True 1.65's drops.

## To ship it (not done)

The owner decided on 2026-11-14 that this content and the Prima corrections go into the next base-game world update,
and will run a play-test sweep himself.

1. On the owner's PC, build the next release's reference package as usual (`build_release_037.py`, then
   `assemble_release_037.py`).
2. Run the tool on a copy of that package's world: first without `--apply`, read the report, then with `--apply`
   `--with-corrections` (the owner's decision: True 1.65's Prima monster levels, aggression and speed).
3. `make_patch.py` as usual; the world update then carries the rows.
4. A play check of a few encounters (Talvor's Spirit, Cuinn's seedlings, the Unseelie Envoy's convoy) before release.

## Hired companions (branch `feat/hired-companions`, 2026-11-16)

True 1.65's hired companions are ported as code (`source/server/GameServer/mode165/`, the `hired_companions` switch).
Living Server has no traveler's inns, so hiring is reached with `/hire` or a healer's **[companions]** link beside any
healer of the player's realm; no world row is added. A later base-game world update can add True 1.65's inn people from
`runtime/true165/add-inns165.json` (table `Mob`, 60 rows, three inns):

| Realm | Region | Innkeeper (`DOL.GS.Innkeeper165`) | Healer (`DOL.GS.GameHealer`) |
|---|---|---|---|
| Albion | 1 (Avalon Marsh) | Gwenllian 462122, 649820, 1816 | Edric 462433, 649885, 1816 |
| Midgard | 100 (West Svealand, by Huginfell) | Halvar 705691, 790857, 4541 | Bjarni 705946, 791802, 4540 |
| Hibernia | 200 (Shannon Estuary) | Muireann 314694, 634017, 4866 | Fiachra 314766, 634391, 4864 |

With them come 24 `GameGuard`, 6 `GameMerchant`, 3 `RealmExchangeBroker`, 3 `HornHandler165`, 9 `Adventurer165` and 9
`Traveler165` rows. For Living Server take the innkeepers, healers, guards and merchants only: the brokers would move the
Realm Exchange (owner rule: they stay where they are), and the Horn of Rallying, adventurers and travelers are classes
not ported. `Innkeeper165` is in the code already; True 1.65's inn preview spots go in `Hire165.PreviewSpots`.

