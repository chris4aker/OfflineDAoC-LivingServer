# Crafting M0 findings: audit, fixes and calibration

Written 2026-11-15 for milestone M0 of [CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md) (section 11). **Static audit
and unit tests only: nothing here was seen in game.** Sources:

- **World:** a read-only copy of the owner's 0.37b test world (`runtime/data/opendaoc.sqlite3.db` with its WAL, last
  bot save 2026-11-09 11:21 UTC). Called "the 0.37b world" below. Recipe, merchant, station and housing data is world
  data and the same in a fresh install unless a patch changed it; bot purses and objectives are this test world's.
- **Logs:** the PC server logs of 2026-11-14 00:00 to 2026-11-15 16:24 (`server.2026-11-14.0/1.log`,
  `server.2026-11-15.0.log`, `server.log`), five restarts, about 40 hours of server time. A later state of the same
  world than the database (about 3,080 bots in the logs against 2,711 in the database).
- **Code:** `integration/2.0` at `c58ea66e1`.

Only aggregate numbers are given; no player or account data is in this file.

## 1. Recipes

### 1.1 The "19% broken" was a case-sensitive count

The server looks recipes up without regard to case: every string column is `COLLATE NOCASE` in SQLite, and
`ItemTemplate` is pre-cached with lower-cased keys (`DataTableHandler.SetPreCachedObject`). Counting the 8,479
`CraftedItem` rows of the 0.37b world both ways:

| Match | Broken | Product row missing | No ingredient rows | Ingredient row missing |
|---|---|---|---|---|
| Case-sensitive (how the 0.35 count was made) | 1,608 (19.0%) | 1,580 | 2 | 26 |
| **As the server matches (ignoring case)** | **2** | 0 | 2 | 0 |

An external survey of the upstream OpenDAoC database (same 8,479 recipes) got the same two numbers. The 1,606 of the
0.35 note were case mismatches such as `Id_nb` spelled with capitals in one table and not the other; the server
finds those rows.

### 1.2 What the server rejects, per profession

The failure modes, from `Recipe.cs`, `CraftingMgr`, `AbstractCraftingSkill`, `CraftAction` and the skills:

| Problem (`eRecipeProblem`) | What happens today | 0.37b world |
|---|---|---|
| `ProductMissing` | `FindBy` returns null; with `crafting_adjust_product_price` on, `NullReferenceException` | 0 |
| `NoIngredients` | same | **2** |
| `IngredientMissing` | same | 0 |
| `IngredientCount` (0 or less) | free materials; a negative count wraps the `ushort` craft time | 0 |
| `DuplicateIngredientName` | `RemoveUsedMaterials` adds the same bag slot twice: `ArgumentException` mid-craft | 0 |
| `SkillType` (not 1–15, or 5) | `getSkillbyEnum` returns null (0, 5) or throws `IndexOutOfRangeException` (others) | 0 |
| `ProductUnusable` (generic object type in a weapon or armor slot) | crafts a shell with no armor factor, no DPS and speed 0 that repair, salvage and alchemy refuse | **38** |
| Recipe id not a `ushort` | the client cannot ask for it | 0 |

| Profession | Recipes | Usable | Hidden | Of which |
|---|---|---|---|---|
| Weaponcraft | 1,886 | 1,873 | 13 | 13 unusable products |
| Armorcraft | 1,470 | 1,453 | 17 | 17 unusable products |
| Tailoring | 1,288 | 1,280 | 8 | 8 unusable products |
| Spellcraft | 1,456 | 1,456 | 0 | |
| Alchemy | 1,038 | 1,037 | 1 | no ingredients |
| Fletching | 565 | 564 | 1 | no ingredients |
| Woodworking | 336 | 336 | 0 | |
| Metalworking | 200 | 200 | 0 | |
| Clothworking | 150 | 150 | 0 | |
| Leathercraft | 90 | 90 | 0 | |
| Siegecraft, Basic Crafting | 0 | | | no recipes at all |
| **Total** | **8,479** | **8,439** | **40** | |

By product realm: Albion 3,259 recipes, Midgard 2,579, Hibernia 2,635, none 6 (hidden ones included; Albion's extra
are mostly armor and tailoring variants).

**The 40 hidden recipes** (recipe id, product, level):
- No ingredients (Hibernia): 14388 `stable_cold_fine_alloy_tincture3` (alchemy 659; its three ingredient rows are keyed
  `stable_cold_fine_alloy__incture3`, a typo), 16117 `footed_flight_broadhead_arrows3` (fletching 490).
- Unusable products, Midgard armorcraft: 7947–7950 adamantium/asterite/netherium/arcanium svarkedja hauberk, 7957–7960
  the leggings, 7977–7980 the boots, 7987–7990 the gloves (levels 655–1035), 8038 asterite starkakedja boots.
- Unusable products, Midgard tailoring: 12089 wyvernskin padded vest, 12183 sylvan and 12186 wyvernskin thick padded
  vest, 12198 wyvernskin thick padded sleeves, 12223–12226 sylvan/seamist/nightshade/wyvernskin superior padded steeple hat.
- Unusable products, Midgard weaponcraft: 12929–12934 fine alloy to arcanium dwarven great sword, 12989 netherium moon
  claw, 13009 netherium bladed moon claw.
- Unusable products, Hibernian weaponcraft: 19740 arcanite mace, 19781 copper barbed spear, 19861 copper spiked mace,
  19942 arcanite barbed mace, 20034 arcanite fortified mace.

The unusable rows have `Object_Type` 0 and `DPS_AF`, `SPD_ABS`, `Type_Damage` and `Hand` all 0; their same-name
siblings of lower tiers are chain (35), cloth (32), hand to hand (25) and so on. Fixing them needs the stats, not only
the type, so they are hidden rather than guessed. Midgard's top-tier svarkedja chain line is the visible gap.

### 1.3 Problems the server does not reject

- **Eight products with the wrong realm**, so `NormalServerRules.IsAllowedToCraft` refuses them silently
  (`allow_craft_norealm_items` is off) or gives them to the wrong realm: realm 0 on 16076 `runewood_recurve_bow3` and
  19801/19807–19810 copper, sapphire, diamond, netherite, arcanite `two-hand_spiked_mace3`; realm 1 (Albion) on 19738
  `diamond_mace3` (a Hibernian recipe id and metal) and 12631 `bronze_long_sword2` (a Midgard recipe id). The id blocks
  are Albion 1–6,999, Midgard 7,000–13,999, Hibernia 14,000–20,999.
- **Ingredients nobody sells and nothing drops** (checked against every `MerchantItem` list and `LootTemplate`,
  `DropTemplateXItemTemplate`, `LootOTD`): 186 woodworking furniture recipes (each "small/large" furniture piece needs
  a base piece such as `four_poster_bed` or `plain_wooden_table` that no merchant sells) and 3 alchemy recipes needing
  `boiled_st_johns_wort`. They load and fail at "you are missing".
- **219 alchemy recipes at 1000+ need monster drops** (`ancient_*` reagents, `enriched_quicksilver`); everything else
  in every profession is buyable.
- 260 recipes produce a stackable item with `MakeTemplated` off; the loader already forces it on (logged once per
  recipe now, it was once per load). 444 recipes share a product with another recipe, and with it the ingredient list
  (ingredients are keyed by product, not by recipe).
- 11 recipe products have a null `PackageID`, which made the price correction throw (fixed).

### 1.4 Fixes in code (tests only)

- `RecipeDB.FindBy`: an invalid recipe is loaded once, logged once (`[CRAFTING] Recipe <id> (<product>) is hidden:
  <problem>`) and returns null after; the price correction runs only on valid recipes and its failure is logged, not
  thrown; the caches are thread-safe. Before: an invalid recipe was cached as null (reloaded and logged on every click)
  or as a product-less recipe (`KeyNotFoundException` on the second click), and `crafting_adjust_product_price` threw
  `NullReferenceException` on the first, so the GM `/crafting adjustprices` loop stopped at the first broken row.
- `RecipeDB.FindProblem`: the whole rule as a pure function, used by the loader; the audit above applies the same rule.
  It adds three runtime guards the loader lacked: ingredient counts of 0 or less, duplicate ingredient names, invalid
  skill types; and hides the 38 statless shells.
- `Recipe.SetRecommendedProductPriceInDB`: a null `PackageID` no longer throws.
- `CraftingMgr.getSkillbyEnum`: a value outside the table is no skill instead of `IndexOutOfRangeException`.
- `GamePlayer.CraftItem`: a hidden recipe tells the player "That recipe cannot be made on this server." instead of
  nothing (and no longer logs a warning per click). The original client builds its recipe list from its own files, so
  a hidden recipe still shows in the list; the server is the gate.
- `/crafting adjustprices` reports how many recipes it checked and how many are hidden.
- `Repair`: the skill-gate integer division the plan names was already fixed on 2026-10-30 (`d058073c6`,
  `UT_RepairSkillGate`). The two remaining divisions are fixed here: `RepairChancePercent` (a craft level of 0 or less
  gave the cast of infinity, NaN or a negative slope; such an item is now 100%) and `SiegeRepairChancePercent`
  (`50 / woodworking` threw `DivideByZeroException` inside the repair timer at 0, and gave 100% when untrained; both
  now count as 1). The formula for real items is unchanged (a test compares it with the old one).
- Tests: `UT_RecipeDB` (caching, the price setting, every `eRecipeProblem`, skill bounds), `UT_RepairChance`.

Not touched: `Salvage.GetYieldPenalty` still divides by the item's craft level in integers (0 for a type with no craft
level; such items are refused earlier in practice). The survey's two spellcraft bugs (a fifth gem bonus accepted,
focus gems not limited to staves; upstream `b62885a86`, `aa8b5232b`) are in `SpellCrafting.cs`, outside this change.

### 1.5 Proposed world update (not applied)

`tools/claude-version/fix_broken_recipes.py` (with `--dry-run`, a backup, a ledger and `rollback`; tested by
`tools/claude-version/tests/test_fix_broken_recipes.py`). World data reaches players only through the clean build and
`make_patch.py`'s world update, which diffs the released world against the new clean one, so the script is run on the
developer's world with the server stopped and the next patch carries it. It:
1. re-keys the three typo rows to `stable_cold_fine_alloy_tincture3` (recipe 14388);
2. gives `footed_flight_broadhead_arrows3` (16117) the ingredients of its Hibernian siblings at level 490 (heartwood
   wooden boards, phoenix feathers, dolomite metal bars, one each), with fixed row ids;
3. sets the realm of the eight products in 1.3 to their id block's realm (Hibernian merchants already sell
   `runewood_recurve_bow3`; no player inventory in the test world holds any of the eight).

Its dry run on the 0.37b world copy: 3 re-keyed, 3 added, 8 realms changed. The 38 shells and the 189 unsourced
recipes need owner decisions (stats from a source for the shells; sell base furniture at the housing merchants or
leave furniture upgrades out).

## 2. Masters, stations and material merchants

Functional masters are the `CraftNPC` classes; the world also has **57 decoration-only masters** (`GameNPC` with a
master's guild title, which cannot admit anyone). Stations are static items within 512 units: forge (model 478 or the
name "forge"), lathe (481), alchemy table (820). "Materials" are the base ingredients (not themselves a recipe
product) that a merchant of the realm sells.

| Realm | Place | Functional masters | Forge / lathe / table | Material merchants | Base materials sold |
|---|---|---|---|---|---|
| Albion | City of Camelot (10) | armor, weapon, tailor, fletch, spellcraft, basic; **no alchemy** (2 decoration alchemists) | 3 / 2 / 6 | 10 | 247 of 248 |
| Albion | Avalon City, Isle of Glass (51) | all six | 2 / 2 / 2 | 8 (20 in the region) | 247 of 248 |
| Midgard | Jordheim (101) | all six | 2 / 2 / 3 | 13 | 247 of 247 |
| Midgard | Aegir's Landing (151) | five; **spellcraft only a decoration** | 2 / 2 / 2 | 8 (21 in the region) | 247 of 247 |
| Hibernia | Tir na Nog (201) | all six (two armorcrafters) | 2 / 3 / 3 | 7 | 257 of 257 |
| Hibernia | Domnann (181) | five; **tailoring only a decoration** | 1 / 1 / 1 | 9 (22 in the region) | 256 of 257 |

Albion's one missing material is `diamond_metal_bars`, which only the mislabeled `diamond_mace3` needs (fixed by 1.5);
Domnann lacks `fox_glove`, sold in Tir na Nog.

- **Housing villages** (regions 2, 102, 202) hold most of the other masters: Albion 8 villages with 31 functional
  masters, Midgard 9 with 41, Hibernia 9 with 40; most villages have a material merchant (not Chiltern, Aylesbury or
  Aberillan) and **none has a station** (players bring their own house's crafting items).
- **Mainland towns** (regions 1, 100, 200) have no masters; material merchants stand in a few zones (Black Mountains
  North and South, Salisbury Plains; East and West Svealand; Connacht, Lough Derg), and stations stand in
  Cornwall, Dartmoor, Avalon Marsh, Hadrian's Wall, Forest Sauvage, Myrkwood Forest, Uppland, Odin's Gate, Malmohus,
  Vale of Mularn, Lough Gur, Cruachan Gorge, Sheeroe Hills, Emain Macha. **The Shrouded Isles towns** (Munin Sound,
  Faraheim, Gripklosa; Caillte Garran, Vigilant Rock, Vale of Balor; Avalon Isle, Aldland, Dales of Devwy) each have a
  full or near-full set of stations and 4–6 material merchants. The frontier (163) and the battleground (165) keeps
  have forges and lathes.
- **No Siegecraft master and no siegecraft recipe exist** (one decoration master in Domnann), matching the era (siege
  engines from keep merchants). The one Basic Crafting master is in Camelot.

**Gaps per realm, for M2's "station spot per capital":** every main capital has all three stations and its full
material list. Functional masters are missing for **Albion alchemy in Camelot** (nearest: Avalon City and four housing
villages), **Midgard spellcraft in Aegir's Landing** and **Hibernian tailoring in Domnann**. Proposed: turn one
decoration master in each into the functional class (a `Mob.ClassType` world update like 1.5), if the owner wants
every capital complete; the era answer is "join in the other capital".

## 3. Housing

**Present in the world data.** `Regions` 2, 102 and 202 ("Albion/Midgard/Hibernia Housing") have `HousingEnabled` 1;
`DBHouse` has 1,800 lots per realm (5,400), none built or owned in the test world; the deeds (cottage to mansion per
realm, the consignment deed, porch deeds) and the housing merchant lists (lot markers, indoor and outdoor shops,
apprentice and grandmaster merchants, dyes, incantations, `housing_indoor_craft`) exist; 36 Market Explorers stand in
the villages (11 / 13 / 12) beside the Realm Exchange brokers of the three capitals. `HouseConsignmentMerchant` has 0
rows: a consignment merchant appears only when a house owner places one. The six "Consignment Bankers" (two per realm)
and the 213 "Vault Bankers" in the villages are plain `GameNPC` decorations. `market_enabled` is on with
`market_fee_percent` 20. Whether a player can buy a lot, place a consignment merchant and sell through the Market
Explorer on 0.37b has **not been checked in game**; the consignment path competes with the Realm Exchange (decide
before M3).

## 4. Calibration

Methods first; every number is from the test world (launcher batches of level-50 and level-20 bots), not a fresh start.

- **Kill speed** (database, 11-09 snapshot): for each bot on a PvE objective at least 15 minutes old,
  `ObjectivePveKills` over the time from `ObjectiveAssignedUtc` to `LastUpdateUtc`. Median kills per hour:

  | Band | Solo (bots) | Group (bots) |
  |---|---|---|
  | 21–30 | 34.7 (45) | 19.8 (75) |
  | 31–40 | 44.9 (113) | 21.0 (53) |
  | 41–49 | 61.9 (24) | (1) |
  | 50 | 31.8 (525) | 25.4 (475) |

  A group's count is per member; the coin is split.
- **Coin per kill** (code, `LootGeneratorMoney`): `2 + (L+1)³/8`, plus up to half again, × `money_drop` 1.0; mean at
  monster level 25: 27 silver; 35: 73 s; 45: 1 g 52 s; 52: 2 g 33 s; 60: 3 g 55 s. Bots pick up coin
  (`GameBot.TryAutoPickUpMoney`), less guild dues.
- **Coin per hour, gross from kills** (the two above, monster level about the bot's): solo 21–30 about **9.5 g/h**,
  31–40 about **33 g/h**, 41–49 about **94 g/h**, 50 (monsters about 52) about **74 g/h**.
- **Net purse change** (two snapshots of the same 2,711 bots, 11-09 11:21 and 11-11 19:36, 56 wall-clock hours; the
  server's uptime between them is unknown): median 21 g (21–30), 42 g (31–40), 359 g (41–49), 129 g (50). That is far
  below the gross rate for any plausible uptime, so either spending (vendor and Exchange buys, stable tickets, siege
  supplies, dues) takes most of the income, the uptime was short, or coin is lost before the purse. **The logs cannot
  settle it: no line records coin in or out.** Purses at the 11-09 snapshot: median 119 g (21–30), 262 g (31–40),
  1,246 g (41–49), 11,521 g (50, the launcher's 10,000 included).
- **Shopping** (logs, `AUTONOMOUS_EQUIPMENT_UPGRADE` by `source`, 40 server hours, about 3,080 bots): per hour, 245 loot
  upgrades, 54 vendor buys, 99 Realm Exchange buys, 9 merchant-service buys. Over 21 hours of 11-14/15, 1,278 distinct
  bots bought from the Exchange and 981 from a vendor. Per bot-hour that is about **0.03 Exchange purchases and 0.02
  vendor purchases**; on 11-14/15, 93% of the Exchange buys were by level-50 bots, and level 21–30 bots bought 169 from the
  Exchange and 4 from vendors. Exchange adverts in /faction: 1.5 an hour (chat-budgeted). Town idles: 69 an hour.
- **Not measurable from these logs:** Exchange listings and sales (no log line; `realm_exchange_sales` keeps only the
  last 50), vendor sales, coin flows, PvE kills (only RvR kills are logged: 1,660 an hour). M3 needs
  `ECON_PURSE`/`ECON_SUPPLY` (hourly purse totals per realm and band), `ECON_SALE` (vendor and Exchange, both sides)
  and a PvE kill counter in the timeline before the simulator can be recalibrated; the simulator rerun of M0 is
  therefore not done.

## 5. What changes in the plan

- "Fix 19% broken recipes" becomes: 2 data fixes, 8 realm fixes, 38 statless products hidden (fix with real stats
  later), 189 recipes with unsourced ingredients to decide.
- Housing and consignment exist in the data; section 9's "consignment only if housing proves present" now hangs on an
  in-game check and the Exchange decision.
- Materials are not a constraint in any capital; masters are, in three secondary capitals and Camelot's alchemy.
- Bots already reach the Exchange about once per 30 bot-hours at level 50 and rarely below; crafted-goods demand
  under 50 needs the gear-check trigger of M3, as the plan assumed.
