# Crafting and the economy: the plan for players and bots

Owner: Chris. Written 2026-10-30 as the design for a later Living Server release (proposed: the one after 1.2,
"the economy update"). **Nothing here is built.** Where this file says **verified**, it was checked today in the
code on `main` at Beta 1.1 (`fca49d7`; `main` has since moved only in README), in the living-world studies on
branch `living-world/trade-sim`, or in a dated web source; **era** is a 1.65-era fact with its source;
**inference** is this file's reasoning; **proposed** is a choice for the owner. The owner's standing rules apply
throughout: real loot, inventories and coins; the Realm Exchange; stablemaster routes; selling only at natural
task breaks; gamebots start without armor and earn it.

## 1. What the owner asked for, and two assumptions to challenge

Stated goals: crafting as important as PvE; buying master-crafted gear a definite but costly shortcut versus
farming rare drops; crafters level more slowly but end up richer than anyone except the top farmers, without
being punished; repair and potion costs that never punish a player who skips the economy in some level range; a
fresh-start world, 7% crafters, doubled realm commissions; alts without collapsing the economy; classic accuracy;
the three realms equal; random magic drops kept at the current (Atlas) rate.

**Assumption 1: "crafted gear is the costly shortcut".** Two things push against it.

1. **The era.** In the 1.65 game, 99–100% crafted armor and weapons with spellcrafting were the *endgame floor*,
   not a way around it. Every serious level 50 wore a spellcrafted template of crafted pieces; drops filled the
   jewelry slots and a few weapon slots; the class epic armor from the level-50 quest was the free path and was
   outgeared by crafted gear with spellcraft. "Crafted = shortcut, drops = the real thing" inverts that at 50.
2. **This server's data.** Random magic items already drop from every monster at about 24% per solo kill (the
   Atlas generator, kept by the owner's choice). The living-world model found that these drops roughly **halve**
   the demand for crafted gear, and that "a crafted set as a costly shortcut" never once happened in the model:
   leveling slots fill themselves (verified: `LIVING-WORLD-ROADMAP.md`, the rerun with random drops).

**Proposed resolution: two ladders that cross at 50, and value only crafters can supply.**
- **Levels 1–49:** drops fill most slots on their own. Crafting matters through what drops can't give: a full
  set *now* for a bot or player whose slots lag, repairs, potions and poisons, arrows, dyes, and the realm's own
  orders. Crafted gear is a reliable, costly fill-in, and the plan should say so instead of promising a shortcut
  the numbers won't deliver.
- **Level 50:** the spellcrafted template is the floor and the reason crafting matters structurally. A random
  drop has random stat lines; a template caps the stats and resists a class needs, and **spellcraft and alchemy
  work only on crafted items** (verified, `AdvancedCraftingSkill.IsAllowedToCombine`), so the template needs
  crafters by rule. The server's own numbers make the masterpiece worth chasing: a 100% level-51 piece holds 32
  imbue points, a 99% piece 28, a 96% piece 18 (verified, `SpellCrafting.GetItemMaxImbuePoints`). If bots value
  a capped template the way players did, every serious level-50 bot wants crafted pieces and gems, and the
  demand exists without scripting. The "holy shit" drops are the ceiling on top: procs, the jewelry slots, the
  rare weapon.

That keeps "definite but costly" where it is real (the level-50 template, and set fill-ins on the way), keeps
the drops exciting, and keeps 50 honest. Decision 1.

**Assumption 2: "crafters end up rich".** Every simulator run with the owner's own settings (7% crafters,
doubled commissions) left the median crafter poorer than the median hunter, because crafting income is
winner-take-most: the top fifth of crafters were richer than the median hunter, the other four fifths were not
(verified in the roadmap's "hunting main + crafter alt" follow-up). Reaching "richer than anyone except the top
farmers" for the *typical* crafter needs paid apprentice work and a spending policy, not a bigger share.
Section 6 designs both; decision 8 settles how far to go.

## 2. Era facts the design rests on

| Fact | Status | Source |
|---|---|---|
| In 1.65 a character joins a crafting order by speaking to a Master NPC in a capital. "Basic Crafting" for every new character came in **1.76b (June 2005)** and is not era | era, verified | Dark Age of Camelot Test Version 1.76b release notes (FreddysHouse archive) |
| Orders had a primary skill (cap 1000) and secondary skills at a fraction of it; a 2002 player: "If a tailor, fletching can only be raised to 75% of your tailoring skill, and all other trade skills 40%". Primaries and secondaries were removed in **1.87 (2007)** | era, player report | Allakhazam Tailoring page comments |
| 1.63 (June 2003) flattened Armorcraft and Weaponcraft recipes (no more studs, wires, scales, strapping), made recipes show the item level, and raised salvage-fodder drops from 80% to 89% quality | era, verified | 1.63 patch notes |
| Stations: forge (armorcraft, weaponcraft), lathe (fletching), alchemy table (alchemy, spellcrafting), anywhere (tailoring, siegecraft) | era | darkageofcamelot.com tradeskills overview (a modern page; the station rules are unchanged since classic) |
| Grey recipes give no skill; orange is the best skill per attempt | era | same page |
| Vendors: "buy back at 50% of retail cost on materials, and around 65% for finished products" | era, player report | Allakhazam comments (2002) |
| Spellcraft overcharge: only anecdotes survive on the web (a 98.6% mean gem-and-item quality for a +5.5 overcharge at 1000 skill). The server's formula is the authority | era, weak | Uthgard forum thread |
| Consignment merchants arrived with Foundations housing (June 2003, in era). Whether housing zones are in this Classic + Shrouded Isles world is **not checked**; the housing code (consignment merchant, market explorer) is in the server | era | Foundations release; `gameobjects/ConsignmentMerchant.cs` |
| Siegecraft became a full 5–1000 tradeskill in 1.90; in 1.65 siege engines came from siege merchants at keeps and a small siegecraft skill | era | darkageofcamelot.com tradeskills overview |
| Crafting titles by skill: Helper (0–99), Junior Apprentice (100s), Apprentice, Neophyte, Assistant, Junior, Journeyman, Senior, Master (800s), Grandmaster (900s), Legendary (1000–1099), Legendary Grandmaster (1100+) | verified in code; the 1100 rung is post-era | `craft/AbstractProfession.cs:34-55`, `language/EN/Crafting.txt:20-41` |

## 3. What exists today (verified)

### 3.1 The crafting system is complete for players, with post-era rules in it

All in `source/server/GameServer/craft/` (OpenDAoC's crafting, unchanged by this fork; a second, independent
code map confirmed every line below):

- **Skills:** Weaponcraft, Armorcraft, Siegecraft, Alchemy, Tailoring, Fletching, Spellcrafting, Basic Crafting,
  and the secondaries Metalworking, Leatherworking, Clothworking, Gemcutting, Herbcraft, Woodworking
  (`CraftingMgr.cs:30-48`), plus `Repair.cs`, `Salvage.cs`, `SalvageCalculator.cs`.
- **Stations** within 512 units: forge (model 478) for armorcraft, weaponcraft, metalworking and any tailoring
  recipe with metal bars; lathe (481) for fletching; alchemy table (820) for alchemy and spellcraft; nothing for
  tailoring, siegecraft and the other secondaries. GMs bypass the check.
- **Success by recipe con** (`AbstractCraftingSkill.CalculateChanceToMakeItem:554`): grey, green and blue always;
  yellow 92%; orange 84%; red 68%; purple never. **A failure consumes no materials** (`CraftAction.cs:106-110`).
- **Skill gain per success** (`CalculateChanceToGainPoint:587`): green 15%, blue 30%, yellow 45%, orange 55%, red
  65%, plus 5 in a capital (the 1.86 rule). One point per gain. Secondary skills gain only when the primary does,
  up to 1,300.
- **Con bands** (`GetItemCon:781`): grey at 50 or more skill above the recipe, green 31–49 above, blue 11–30,
  yellow 0–10, orange 1–19 below, red 20–49 below, impossible beyond.
- **Quality** (`GetQuality:715`): **96–99%**, never 94 or 95 (the comment: "1.87 patch raises min quality to
  96%"); a **2% masterpiece (100%)** only at skill 1000 or more, or on a grey recipe; yellow and orange recipes
  lean to 96%. The era had 94–99 with a small masterpiece chance at any skill (from memory of the live game, not
  verified against a dated source).
- **Crafting time** (`GetCraftingTime:634`): `(floor(level ÷ 100) + 1) × materials ÷ 4` seconds, ÷ `CraftingSpeed`
  (`crafting_speed` × guild crafting-haste buff, or × the capital bonus), −5% per relic held, keep bonuses, × a con
  factor (grey 0.4, green 0.6, blue 0.8); capped by `max_craft_time` when set; GMs craft instantly.
- **Every primary skill can be held by one character; the primary only drives the title** ("Luhz Crafting
  Update"). Crafting skill is stored **per account and realm** (`AccountXCrafting`), so the owner's three
  characters in a realm share one set of crafting skills. Both are post-era.
- **Every new character starts with all crafting skills at 1 and Basic Crafting as primary**
  (`scripts/startup/script/CharacterCreationCraft.cs:62-87`, the 2005 rule). Masters exist for the eight orders
  (`gameobjects/CraftingMaster/`); whether they stand in the shipped world is an M0 check.
- **Spellcrafting** (`SpellCrafting.cs`): only crafted items can be imbued; imbue capacity by quality (above);
  gems at most 4 bonuses; overcharge up to +5 by the 1.87 calculator formula (`CalculateChanceToOverchargeItem:487`);
  on an explosion the gems are destroyed, **the item survives, the crafter dies**, and a trade partner dies 40% of
  the time (`:362-397`). The era destroyed the item.
- **Alchemy** (`Alchemy.cs`): one tincture per crafted, unimbued item (procs or charges, 2–10 by quality); potions
  and poisons are ordinary recipes.
- **Salvage** (`Salvage.cs`): yield × `salvage_yield_multiplier` 0.5; **Atlas random items always salvage to 2
  units**; needs a secondary skill of 0.75 × the item's craft level.
- **Repair:** a player repairs **only another player's item, in a trade window** (`PlayerTradeWindow.cs:159-214`;
  `SelfCraftWindow.cs:131-138` refuses your own), with a skill-based chance (`Repair.cs:174-186`); the Smith NPC
  charges the lost share of the item's price (`DbInventoryItem.RepairCost`) plus 20% for "repair all"; every
  repair spends durability equal to the condition restored. **Condition is lost only by `GamePlayer` owners**
  (`GameInventoryItem.OnStrikeTarget:262`), 5% chance per hit while above 70%; **bots' gear never wears**.
- **Realm news at 1000 skill** (`GamePlayer.GainCraftingSkill:11604`); a title event every hundred.
- **Server properties** (`serverproperty/ServerProperties.cs`): `crafting_speed` 1.0; `capital_city_crafting_speed_bonus`
  1.0; `keep_crafting_speed_bonus` 1.0; `capital_city_crafting_skill_gain_bonus` 5; `guild_buff_crafting` 5;
  `max_craft_time` 0; `crafting_adjust_product_price` off; `crafting_sellback_percent` 95 (only with the former
  on); `item_sell_ratio` 50; `item_condition_loss_chance` 5; `money_drop` 1.0; `salvage_yield_multiplier` 0.5;
  `market_fee_percent` 20 (housing market only). **No skill-gain multiplier exists.** The random-drop rate is a
  **code constant** (`ROGMobGenerator.BASE_ROG_CHANCE = 14`, +10 solo), and the Exchange has **no fee setting**.
- **The world data** (0.35 world, 2026-10-11): **8,479 `CraftedItem` recipes, 1,606 unusable** (a missing
  ingredient or product row, about 19%). The database isn't in Git; per-skill counts need the PC.
- **Two bugs to fix before anything else:** `RecipeDB.FindBy` calls `SetRecommendedProductPriceInDB` on a null
  recipe when `crafting_adjust_product_price` is on (`Recipe.cs:136-141`), so turning that setting on with 19%
  broken recipes will throw; and `Repair.IsAllowedToBeginWork` has an integer division that makes its skill gate
  0 (`Repair.cs:150`). `Salvage.GetYieldPenalty` also mutates the item's `Object_Type` (`Salvage.cs:198`).
- **The 0.37c new client has no crafting window**: it shows the skill and title only (`character_sheet.gd:85-94`).
  Checked 2026-10-31: shadowofze's unreleased `main` gives the new client a crafting window with salvage and repair
  (`game/ui/craft_window.gd`), fixes the `Salvage` type bug above and lets a bot hold three Exchange listings; none
  of it is released, and Living Server would ship it only through the base merge
  ([RELEASE-TRAIN.md](RELEASE-TRAIN.md), decision 3). The numbers in this document describe what players have.
  **Player crafting is an original-client feature** until shadowofze's client grows one.

### 3.2 Bots do not craft, and barely shop

- **No bot crafts.** The crafting code takes a `GamePlayer` everywhere; `GameBot` is a `GameNPC`. The decision
  engine's "craft at a station" branch (`AutonomousBotDecisionEngine.Choose:124`) is called by nothing, not even
  a test. `offline_world_bots.SerializedCraftingSkills` exists and **nothing writes it**, so the economy code's
  "trained crafter" branches (buying material stacks, keeping stackables from the vendor,
  `AutonomousBotEconomy.cs:146-193, 566-612`) are dead.
- **The Crafter personality is 12% of bots, not 7%** (`AutonomousBotPersonality.TypeSharePercent = [28, 20, 20,
  16, 12, 4]` in the order Leveler, Socialite, Daredevil, Explorer, Crafter, No-lifer; `:36`). A bot's type is a
  fixed bucket of a hash of its id, so **changing the shares re-types existing bots** near the bucket edges,
  including which ones are beggars and how they talk; a test pins the shares (`UT_AutonomousBotPersonality:15`).
  With `bot_personalities_enabled` (off in code, **on** after Setup's Recommended profile) a Crafter takes longer
  town breaks (about 28–32%, ×1.4 long), avoids RvR, prefers easy camps and dungeons, and chats more; it **does
  nothing craft-related**. Always on: Crafters draw the `trade` voice lines, 4% of them are beggars, their away
  time is ×1.2, and they are the least likely Leeroy.
- **"7% crafters" and "doubled realm commissions" exist only in the simulator** (`persona_sim.py:919-920`:
  `crafter_share=7`, `commission_mult=2.0`; 30 realm orders an hour at 3× materials, doubled to 60). Nothing in
  the server, the launcher or the bot generator knows either number.
- **The Realm Exchange** is a broker NPC in each capital (`RealmExchangeBroker.cs`, NPC guild "Realm Exchange";
  `/tc` teleports to it). It uses the client's market-explorer window to browse and a consignment chest to list.
  **No NPC stock** (`AllowSyntheticListings=false`); every lot is a real item from a player or a bot. **No fee.**
  Player listings never expire; **a bot may list one item, and an unsold bot listing is deleted after 24 hours**
  (`RealmExchangeExpiry.cs:52-69`), an item sink. The last 50 sales go to `realm_exchange_sales`; the launcher's
  Realm Exchange tab shows listings and the sales.
- **Bots shop rarely.** A bot visits the Exchange or a vendor only at a task boundary with a nearly full bag (6 or
  fewer of 40 slots free, 95% of the time), ungrouped, and the Exchange only in a capital
  (`AutonomousObjectiveAssignments.cs:48-50, 672-687`; `AutonomousWorldBotController.cs:1033-1197`). Then: list
  one valuable item, vendor the trash, buy one upgrade. **A market for crafted goods has almost no bot demand
  today**, whatever the supply.
- **Prices and valuation** (`AutonomousAuctionValuation.Recommend:22-44`): equipment = level² × 8 + utility × 30
  + rarity² × 50, × quality × (0.45 + 0.55 × condition), × (1 + disposition from −0.35 to +0.50); rarity is +2 for
  an Atlas item and **+1 for a crafted one**; a bot buys when the improvement is at least 8 and **prefers a
  player's listing by +650 score** (`AutonomousBotEconomy.cs:157-196, 864-901`). Vendor sales pay
  `item_sell_ratio` 50%; a vendor upgrade costs the full template price, once per task.
- **Earning armor:** level-1 bots start with starter weapons and instruments only (`GameBot.cs:2531-2546`); gear
  comes from loot (equipped on pickup if an upgrade), vendor upgrades and Exchange upgrades. **Two exceptions:**
  the Epic Special Forces' unsaved kit, and **the launcher's level-50 batches, which arrive with a quality-100,
  bonus-35 "Offline level 50 {class}" kit and 10,000 gold each** (`MainForm.cs:2224-2257`). In a 600-per-realm
  test world that is 3,000,000 gold a realm, and those bots never need a crafter.
- **Coin flows** (`AutonomousBotEconomy.cs`, `GameBot.cs`, `AutonomousBotGuildEconomy.cs`): in from monster coin,
  group splits, vendor and Exchange sales, beggar gifts; out to stable tickets, frontier medallions, siege supplies
  and 5-gold repair kits, and guild dues (RvR and Raiding 5%, Crafting 3%, Leveling 2%). Purses are
  `offline_world_bots.MoneyCopper`. **Guild banks fill and are never spent**; Crafting-charter guilds buy a
  crafting-haste buff that does nothing for bots; guild placement is by level, not by crafter type (two
  doc-versus-code mismatches). No bank, no shared purse.
- **Consumables:** bot poisons are free (`BotPoisonSupply.cs`); no potion or arrow use by bots was found.
- **Chat:** the corpus has about 350 crafting and trade lines across 69 files, concentrated in `trade_crafting.lines`
  (33, kind `trade`, topics selling, buying, crafting, `@who Crafter`), the realm thread files and `guild_chat*`
  (63 guild crafting lines); only 8 wts/wtb lines. With voices on, `trade` and `lfg` lines go to /trade or /lfg 70%
  of the time; **listing adverts go to /faction**; **a player's own /trade line is heard only by beggars**
  (`commands/playercommands/trade.cs:30`).
- **Hooks for commerce with the player:** beggars take an item or coin dragged onto them
  (`AutonomousBeggars.TryReceiveItem:655`, `TryReceiveMoney:694`), NPCs give items, bots already prefer a
  player's Exchange listing, and the talking update reads "wtb", "wts" and price questions.
- **Persistence:** bot purses and the unused crafting column in `offline_world_bots`; bot items as `Inventory`
  rows owned by `offlinebot:{id}` with generated items in `ItemUnique`; `progress-policy.json` and
  `build_release_037.py` empty the saved-progress tables; a new table must be registered in the policy, in the
  launcher's bot-purge SQL (`MainForm.cs:2532-2570`), and is then skipped by patches automatically.
- **Tests:** none for crafting, salvage, repair, spellcraft or recipes; 62 for the Exchange, 7 for valuation, 26
  for loot flow, 37 for beggars, 21 for personalities (`Tests/UnitTests/`).
- **No NPC crafting animation exists.** Player crafting is the client's progress bar plus a sound; `eEmote` has
  no craft emote. A crafter bot's presence has to come from its place, its walks, its emotes and its talk.

### 3.3 The living-world simulator already modeled this

`tools/living-world-sim/persona_sim.py` and `lw_economy.py` (branch `living-world/trade-sim`) model crafters,
orders with deadlines, the Exchange at fixed prices (no supply-and-demand), repairs, potions, commissions, a 5%
tax, salvage and luxury sinks. With the owner's settings they found: random drops halve crafted demand; no bot
ever finished a set mostly from bought crafted gear; crafters end 8–12 levels behind (as wanted) and poorer than
hunters (not wanted; winner-take-most); commissions are what lifted crafters at all; gold in the economy roughly
doubled in 24 weeks; at 30% material supply crafting nearly stopped; orders waited a median of about 5 hours and
a quarter to a third were never collected. Their recommended direction is the one this plan takes: commissions
including apprentice work, repairs, potions, poisons and arrows, siege, and goods only crafters make. Every
number is an assumption-driven model to be recalibrated from live logs (M0).

## 4. The coverage frame

Every facet, with the era rule, what exists, what is new for the player, what is new for the bots, and its switch.
"Ledger" means a realm-level model (skills, coins, stock) with real items made only when someone equips, sells or
receives them; "in world" means a bot visibly does it. Everything bot-side is off by default with a switch in the
launcher's Living Server tab.

| # | Facet | Era rule | Exists today | New: player | New: bots | Switch |
|---|---|---|---|---|---|---|
| A | **Orders and masters** | Join at a Master; primary cap 1000; secondaries 75% / 40% (player report) | All eight masters in code; new characters start with Basic Crafting; any primary can be held; skills shared per account and realm | **Era start** (proposed, on): a new character has no order until a Master gives one; optional **secondary caps** at 75% / 40% of the primary; Basic Crafting is the off position. Per-character skills are too deep a schema change for now; note it | Each Crafter bot takes one order at creation, weighted toward its realm's needs (proposed: armorcraft 24%, tailoring 22%, weaponcraft 18%, spellcraft 14%, alchemy 12%, fletching 8%, siegecraft 2%), balanced per realm | `crafting_era_start`, `bot_crafters` |
| B | **Recipes, materials, stations** | Materials from merchants; forge, lathe, alchemy table | 8,479 recipes, 19% broken (0.35); stations and material merchants not audited; `/craftmacro buy` buys from a targeted merchant | M0 audit on the PC: recipes per order, material merchants and stations in each capital and major town; fix or hide broken recipes (the HearthDAoC quest-fix pattern); fix the `Recipe.cs` null case | Crafters buy materials at the merchant's price (ledger); the station spot per capital is where they stand | — |
| C | **The craft** (time, success, quality, gain) | 94–99 with masterpieces at any skill; grey gives nothing; failure lost materials | 96–99, 2% masterpiece at 1000 or grey; failure costs nothing; tables in 3.1 | Unchanged by default. Optional **classic quality** (94–99, a small masterpiece chance at every skill) and **failure costs materials** (proposed, both off) | Bots use the **same functions**, refactored to take a skill value instead of a player (`CalculateChanceToMakeItem`, `CalculateChanceToGainPoint`, `GetQuality`, `GetCraftingTime`), so a bot's craft rolls exactly as a player's | `crafting_classic_quality` |
| D | **Progression pace** | Weeks and much coin to 1000 | `crafting_speed` only; no gain multiplier | **Player crafting pace**: time ÷ and points per gain (1, 2 or 3); default classic | **Bot crafting pace**: the model showed crafters lagging the bots who need gear; at 5× bot XP they'd never catch up. Proposed default: bot crafters gain at the bot XP multiplier for their level band; the launch cohort's crafters start at 1 like everyone | `crafting_player_pace`, `bot_crafting_pace` |
| E | **Titles, prestige, firsts** | Titles by hundred; LGM was fame | Title ladder; realm news at 1000; the Herald | Crafting **server firsts** (first Legendary of each order per realm) through `bot_living_firsts`; the Herald lists top crafters per order | Bots earn the same firsts; bots address a Legendary crafter by title; a grats chain in /faction | part of `bot_living_firsts` |
| F | **Spellcrafting** | 99–100% items, gems, overcharge, explosions that destroyed the item | Full rules in 3.1; crafted items only; the item survives an explosion | Unchanged; optional classic explosion (item lost) | Bot spellcrafters make gems (ledger) and imbue level-50 templates; **bot valuation becomes template-aware at 50** (caps on stats and resists count) so the demand exists; an explosion is a true event told in /faction, and a death the realm hears about | `bot_crafters` |
| G | **Alchemy** | Dyes, tinctures, potions, poisons | Recipes; tinctures on crafted items; free bot poisons | Unchanged | Dyes in guild colours for bot guilds (real and visible); potions bought for raids and sieges and used at wipes and breaks; poisons bought instead of free; procs on level-50 crafted weapons | `bot_crafters`, `bot_luxury_spending` |
| H | **Salvage and trinketing** | Salvage to materials; trinketing's date **unverified** (skip) | `Salvage.cs`; Atlas items give 2 units | Unchanged | Crafters salvage junk loot bought from the Exchange for materials (the model's rule); never gear they wear | — |
| I | **Repair, condition, durability** | Condition lost in use; repair costs durability; smith or crafter | Only players' gear wears (5% per hit above 70%); the smith's price rule; player repair needs a second player; durability spent on every repair | **Upkeep setting**: classic / light (half cost, no durability loss) / free; default light. A crafter bot repairs an item dragged onto it for a fee (the beggar hook), which is the only solo path to a crafter repair | **Bots' gear wears too** (new rule, same 5%), so repairs become a real sink at task breaks: 60% to a crafter bot (ledger), the rest to the smith (the model's 2% of income) | `player_upkeep`, `bot_repairs` |
| J | **Siegecraft** | Siege merchants at keeps; small skill | Bots already pay for siege supplies and 5-gold repair kits (`BotSiegeRuntime.cs`) | Unchanged | Siegecrafters supply engines and kits below merchant price (ledger); the 1.2 war-effort chapter counts crafted supplies | `bot_siege_supply` |
| K | **Markets** | /trade in capitals; consignment with housing | The Exchange (no stock, no fee, 1 bot listing, 24 h deletion); /trade routing 70%; adverts in /faction; the player's /trade heard by beggars only | **Bots read the player's /trade**; buy from and sell to bots directly (section 8); optionally sell to the Exchange at the index price with a per-band daily cap | Crafters list stock on the Exchange (more than one listing for crafters; unsold stock returns to the crafter instead of being deleted); an **order board** of open WTB orders with deadlines, picked by profit (the model's cure for 5-hour waits); adverts go to /trade | `bot_crafters` |
| L | **Prices and money supply** | Vendor spread; prices by scarcity | Valuation formula with disposition; vendor pays 50%; no fee setting | **Exchange tax** (proposed 5%) on trades only, so a player outside the economy never pays it | A price index per band from stock against demand, bounded below by vendor value and above by a multiple of material cost; money supply logged hourly per realm; the **luxury sink** for rich bots | `exchange_tax`, `bot_luxury_spending` |
| M | **Bot gear pipeline** | Earn, buy, replace | Loot, vendor and Exchange upgrades; shopping only on a full bag; the level-50 kit and 10,000 gold exception | — | A **second shopping trigger**: a gear check at every town visit (a slot empty or below the bot's band, or a 50 without its template), independent of the bag; crafted tiers in the Exchange; **the level-50 button's kit and gold become a setting, off for a fresh-start world** | `bot_crafters`, `launcher_level50_kit` |
| N | **Crafter bots as people** | Crafters at the capital forges, spamming /trade | The Crafter type (12%); town idle routing; schedule; voices; beggars | — | Section 6: the station as a place; the visible loop; emotes; ads with real stock; grumbles; the LGM moment; drama; **the share change applies to new bots only** (existing bots keep their type) | `bot_crafters`, `bot_voices` |
| O | **Player and bot commerce** | Tells, trade windows, haggling | Beggar drag hooks; NPC item gifts; +650 preference for player listings; intents for wtb/wts | Section 8 | Section 8 | `bot_crafters` |
| P | **Guilds** | Guild crafters | Crafting charter (3% dues), a bank nothing spends, a useless haste buff, placement by level | Guild crafters make for guildmates at material cost (the 1.2 tier plan's "crafter access") | The bank buys materials and raid potions; Crafting-charter guilds take crafter bots first; drop the haste buy | with the 1.2 guild package |
| Q | **Alts and legacy** | Crafter alts | No shared purse between a bot's characters | — | A crafter is usually an **alt beside a hunting main** where the roster design exists (the owner's rule that alts add to a main's playtime); purse sharing is a decision of that design | `bot_crafters` |
| R | **Live events and holidays** (1.2) | Festival goods | Holiday chatter; the 1.2 plan | Festival dyes and food from alchemists | War-effort hand-ins count crafted goods; invasions damage doors that siegecrafters repair | with 1.2 |
| S | **Clients** | — | The new client has no crafting window | Player crafting on the original client only, said plainly in the docs; the new client's Exchange filter "player crafted only" already exists | — | — |
| T | **Persistence and release** | — | The unused `SerializedCraftingSkills` column; policy and purge lists | Section 10 | — | — |
| U | **Tests and rehearsal** | — | The simulator; the chat-simulator pattern; no crafting tests | Unit tests on the pure rules; an **economy rehearsal** that plays a week of a realm in minutes and writes a readable ledger; `/lstest craft`, `/lstest wtb`, `/lstest commission`; `ECON_*` and `CRAFT_*` lines in the timeline report | — | — |
| V | **Out of scope** | — | — | No client UI; no NPC crafting animation; no trade windows with bots; consignment only if housing proves present; trinketing until dated; nothing claimed until seen in game | — | — |

## 5. The economy as a model

**Population and capacity (inference, at 1,000 bots per realm).** 7% crafters is about 70 per realm, about 10
per order; today's 12% would be 120. A craft takes seconds to a minute of real time, so capacity is never the
constraint (the model had crafters idle 94% of forge time). The constraints that make crafted gear cost something
are the real game's: **materials cost coin** (a median level-50 armor recipe's materials are about 47 same-level
kills of coin, a weapon's 97), **skill gates what can be made** (nobody makes level-50 gear until a crafter is past
900; on a fresh start that takes weeks and much coin, and it should), and **quality is a roll** (2% masterpieces
at 1000 skill; the 96–99% results sell at a loss). Scarcity is a property of the server's age, as on a launch
server, which is the fresh start the owner chose.

**Faucets and sinks.** Coin enters through monster coin, vendoring loot and quest rewards; it leaves through
materials, stable tickets, medallions, siege supplies, guild dues, and (new) repairs, potions and dyes, the tax
and the luxury sink. A crafter is a converter: coin in as materials, goods out, margin kept. The model's warning
stands: gold doubled in 24 weeks. Sinks in this plan, by size: the **luxury sink** (a bot above a wealth threshold,
by its highest character's level and personality, spends part of its surplus on real things: dyes, horse routes,
guild dues and buffs, potions, an upgrade it didn't strictly need; housing only if present), materials (real
already), **repairs** (bots' gear must wear for this to exist), consumables, the tax. The player pays none of
these unless he trades. Money supply per realm is logged hourly (`ECON_SUPPLY`) and shown in the Herald.

**Three ledgers per realm.** (1) Money: every bot's real purse. (2) Goods: the Exchange's stock by item band and
quality, fed by crafters and by loot. (3) Prices: an index per band from stock against demand, bounded below by
vendor value and above by a multiple of material cost; it replaces the fixed prices the model used. Bots trade
through the Exchange as ledger entries; the player trades with bots at asking prices derived from the index and
the bot's disposition (the existing −0.35 to +0.50).

**Realm commissions** (the owner's "doubled commissions"). The model's mechanism was NPC orders for the realm, 30
an hour at 3× materials, doubled to 60, open to crafters above skill 50. In the world that is the **realm's
quartermaster**: keep guards' armor, siege supplies and raid potions ordered from crafters, sized to the crafter's
skill and paid for time, so an Apprentice gets apprentice work. It is the one lever the model found that lifts
the typical crafter, and it is lore-honest. Decision 8.

**The player's coin at 10× XP (inference).** Coin per kill follows monster level, not XP, so at 10× XP the player
reaches 50 with about a tenth of a classic player's coin. "Costly" becomes "out of reach" and repairs bite.
Proposed: a **player coin multiplier by level band** that follows the XP multiplier (the XP-by-level mechanism;
`money_drop` is global and applies to bots too, so it is the wrong lever), default on. Decision 4.

**The level-50 button (inference from 3.2).** Each launcher level-50 bot arrives with 10,000 gold and a
quality-100 kit. In a fresh-start world that button is the opposite of the owner's plan; in a test world it is
what makes raids possible. Proposed: the kit and gold become a launcher option, on for test worlds, off for a
fresh start. Decision 10.

**Anti-exploit.** A bot's buy price is below its sell price by at least the vendor spread; the index moves against
anyone who floods one band; a bot's budget is its purse; direct Exchange buying from the player is capped per band
per day. No infinite-coin loop.

## 6. Bot crafters as people

**Who.** The crafter share is a setting (default 7, the owner's number; the model suggests the typical crafter
does better at a lower share). Because a bot's type is a hash bucket, **the new share applies to bots created
from then on**, and existing bots keep their type; the type is stored in the new crafters table so a later change
never re-types anyone again. Where the roster design gives a bot player several characters, the crafter is an alt
beside a hunting main. Each crafter has an order, a skill in it (ledger, in the existing `SerializedCraftingSkills`
column), a spending policy (invest at most half of net profit in skill-ups, keep a cushion; the model's fix for
"crafters reinvest every coin") and the same title ladder as a player.

**Where.** At the forge, lathe or alchemy table of its capital, sometimes a town's; the town idle routing gets a
"station" destination. The visible loop is merchant (materials), station (crafting, with emotes from the
body-language set), vendor (failures), and off to hunt now and then (the model gave crafters 55% of their time at
the station; a Crafter who never leaves town is furniture).

**The day.** The bot schedule applies. A crafter logs in, reads the order board, takes orders it can make at a
profit, buys materials, crafts them in real time (the ledger advances on the game loop like every other pulse),
lists finished stock, sells failures, pays dues. Skill advances by the real rules; a title change is said in chat;
the LGM moment at 1000 is a server first with its grats chain.

**The voice.** /trade ads that name real stock and real prices; /say grumbles at the station ("97. again."); true
answers to "can you make X"; price talk and debates ("LGM is a waste of money" against "you wear my armor");
lore about the old smiths. Written in the corpus files (`trade_crafting.lines` and the realm thread files) under
the 1.1 truth rule: a line that names stock needs the stock.

**The drama.** Storyline arcs: a price war between two tailors; a crafter who took materials and "went linkdead";
the 100% hauberk that exploded (a true event: the gems gone, the crafter dead, told in /faction within a minute);
the Apprentice who finally hit Legendary. Personalities decide who undercuts, who hoards, who gives guildmates a
discount.

**Memory.** A crafter remembers its customers: a repeat customer gets a greeting by name and a rounded-down
price; a player who stiffed a bot on a commission gets "pay up front this time".

## 7. The gear pipeline with crafters in it

What happens today: a bot equips loot on pickup when it is an upgrade of 8 or more; at a task boundary with a
nearly full bag it lists one item, vendors the trash and buys one upgrade (vendor or Exchange); it prefers a
player's listing. Nothing else moves gear.

What changes:
1. **Supply.** Crafters make real items from real recipes (`CraftedItem` rows for the order, the slot and the
   level band), pay the materials from their purse, roll quality with the real function, and list them on the
   Exchange with the crafted rarity bonus the valuation already gives. Unsold crafted stock returns to the
   crafter; only loot listings expire.
2. **Demand, two triggers.** A **gear check at every town visit** (not only with a full bag): a slot empty or
   below the bot's level band is filled from the Exchange if the purse allows; a level-50 bot without its
   template buys 99–100% pieces and gems as a template, valued as a set. The existing full-bag visit stays.
3. **Template-aware valuation at 50.** Caps on stats and resists count in the score, so a 96% random piece with
   odd lines loses to a 99% crafted piece with the right gems. This is the one change to what bots wear and it
   needs its own test and an overnight check: bots must not undress.
4. **Bots earn their armor.** Every crafted piece is bought from a purse. The Special Forces exception stays as
   written in 1.0; the level-50 button's kit becomes a setting (decision 10).
5. **Dyes follow guild colours** for bots in guilds: the single most visible sign in the world that an economy
   exists.

## 8. Player and bot commerce

**Buying from a bot crafter.** A tell or a /say by name ("wtb 99% hauberk", "can you sc this") is read by the
intent model; the crafter answers with what it can make at its skill, the price (index plus its margin) and a
time; it asks for coin up front or on delivery by personality. The craft takes real time, during which the
crafter is at its station. Delivery is a hand-over: the bot gives the item the way quest NPCs do, and says so. A
spellcraft or repair job needs the item dragged onto the bot first (the beggar hook) and comes back imbued or
repaired, or, rarely, with an honest "it exploded" and the bot covering the materials by personality. Haggling is
a short bounded exchange; a bot that likes you rounds down; one you insulted doesn't deal.

**Selling to bots.** The player's crafts have buyers. Bots post WTB in /trade with budgets drawn from their purses
and their real gear gaps ("wtb 96%+ chain set lvl 23, 3g"); the player's /trade is read by bots, not only by
beggars. Dragging the item onto the bot that asked is the sale: it pays, equips it if it fits, and remembers who
made it. Listing on the Exchange works today and bots already prefer a player's lot. A player who becomes the
realm's best armorcrafter earns a reputation through deeds ("he made my whole template"), bots ask him first, and
the Herald lists him.

**Repairs and potions.** Bots repair at breaks and buy potions before raids and sieges. The player's own upkeep
follows the facet I setting, default light.

**The market's voice.** /trade carries WTS, WTB and "lf sc" at a budgeted rate per realm; every ad is true of the
stock behind it; a "sold" is a real sale in the ledger.

## 9. What stays out, and why

- No new client UI; everything goes through chat, tells, drag-and-drop, merchants and the launcher's tab. Player
  crafting itself is an original-client feature (the new client has no crafting window).
- No NPC crafting animation; presence does the work.
- No consignment merchants unless housing proves present in the world (M0).
- No trade windows with bots; bot-to-bot commerce is the ledger.
- No trinketing until its era date is checked; no per-character crafting skills (schema).
- Nothing described as working until seen in game (AGENTS.md).

## 10. Persistence and release

New or reused storage: `offline_world_bots.SerializedCraftingSkills` (exists, unused) for bot skills, plus the
crafter's order, type and spending policy as new columns (the schema reconciler adds columns at start-up);
`offline_exchange_index` (band, quality, count, price index per realm); `offline_crafting_orders` (open orders,
including the player's, with deadlines); `offline_economy_ledger` (hourly money supply and index per realm, for
the Herald and the timeline report). Each new table is registered in `progress-policy.json` and in the
launcher's bot-purge SQL; patches then skip it. The pure crafting functions are refactored to take a skill value
so players and bots call the same code. Settings are server properties like the other `bot_living_*` switches,
written by the launcher's tab.

## 11. Build order (proposed)

| | Milestone | Done when |
|---|---|---|
| M0 | **Audit, fix, calibrate** on the PC: recipes per order (fix or hide the broken rows), masters, material merchants and stations in each capital and major town, housing present or not; the `Recipe.cs` null case and the `Repair.cs` division fixed; kill speed, coin per hour and Exchange visit rates measured from logs; the simulator rerun with live numbers | A findings note with the numbers and the fixed recipe list; the simulator's directions confirmed or corrected |
| M1 | **The player's side**: era start, crafting pace, upkeep, coin by band, the optional classic rules, crafting firsts, the Herald's top crafters, the new-client caveat in the docs | Played on the original client: join an order at a Master, level a skill at the chosen pace, craft, see the title and the Herald; a first is announced |
| M2 | **Crafters exist**: the ledger crafters with orders, skills, stations, the visible loop, titles, /trade ads with real stock, grumbles, the LGM first; the share setting for new bots | Played: crafters at the stations of all three capitals, ads that match stock, a title said in chat; `CRAFT_*` lines in the timeline |
| M3 | **Crafting matters**: stock into the Exchange, the gear check at town visits, template-aware valuation at 50, the quartermaster's commissions, materials at real prices, dyes in guild colours, the level-50 kit setting | Overnight at 600 per realm: crafted share of worn gear by band logged and rising; level-50 bots buying templates; no bot undressed; crafters' median wealth tracked against hunters' |
| M4 | **The player trades**: commissions by tell, hand-over delivery, drag-to-sell, bots reading /trade and buying the player's crafts, haggling, memory and deeds | Played: buy a set from a bot, sell a set to bots, commission a spellcraft; every step true in the log |
| M5 | **Sinks and services**: bots' gear wears, repairs at breaks with the crafter share, potions and poisons bought, siege supply, the luxury sink, the Exchange tax, the money-supply governor | Two weeks of overnight runs: money supply flat or slowly rising, no price running away |
| M6 | **The social layer**: storylines, price wars, the explosion, apprentice work, Crafting-charter guild crafters and the tier benefit | Played: one storyline seen; a guild crafter makes for a guildmate |

Each milestone ships only after its in-game check, reported separately from automated tests and the rehearsal.
M1 is small enough to ship early (even in 1.2) if the owner wants crafting pace and upkeep now.

## 12. Settings (launcher, Living Server tab)

| Row | Default | Notes |
|---|---|---|
| Era start (join an order at a Master) | On | off = Basic Crafting as today |
| Secondary skill caps (75% / 40%) | Off | authenticity option |
| Player crafting pace | Classic | time ÷ and points per gain |
| Player upkeep | Light | classic / light / free |
| Player coin by level band | Follows XP by level | off = classic |
| Classic crafting quality (94–99, masterpieces at any skill) | Off | |
| Classic explosions (item lost) and failures that cost materials | Off | |
| Bot crafters | Off until M2 | share %, default 7, new bots only |
| Bot crafting pace | Follows bot XP by level | |
| Realm commissions | Doubled | classic / doubled / off |
| Exchange tax | 5% | 0 for none |
| Bots' gear wears; bot repairs and consumables | On with crafters | |
| Bot luxury spending | On with crafters | the inflation sink |
| Siege supply | On with crafters | |
| Level-50 button kit and gold | On | off for a fresh-start world |
| Crafter chat rate | Normal | per-realm budget |

## 13. Decisions for the owner

1. **The two ladders** (section 1): crafted as fill-in and services at 1–49 and the template floor at 50, or
   insist on "crafted = shortcut at every level" against the data.
2. **Bot crafting pace** tied to the bot XP multiplier (proposed), or classic pace (crafters lag for months).
3. **Player crafting pace**: classic, or faster to match 10× XP.
4. **Player coin by level band** following XP (proposed), or classic coin.
5. **Upkeep default**: light (proposed), classic, or free.
6. **Practice-craft sellback**: 50% as released (costly leveling, scarce crafters) or the 95% rule (turn on
   `crafting_adjust_product_price` after the null-recipe fix; crafters keep up but the trade is cheap).
7. **Exchange tax** at 5% on trades, or none.
8. **Commissions and apprentice work**: the quartermaster paying by skill and time (proposed), or
   winner-take-most as the real game had it.
9. **Crafter share**: 7% for new bots as chosen (existing bots keep their 12%-era types), or lower if the typical
   crafter's wealth matters more than the count.
10. **The level-50 button**: keep its kit and 10,000 gold as a test-world option, off for a fresh start.
11. **Classic rules**: era start on (proposed); secondary caps, classic quality, item-destroying explosions and
    failures that cost materials as options, all off.
12. **Bots' gear wears** (needed for any repair economy), or bots stay wear-free and repairs are a player sink only.
13. **Release placement**: the release after 1.2 as a whole, with M1 possibly folded into 1.2.

## 14. What this file does not know

- The current recipe count and the broken rows on the 0.37 world (M0).
- Whether housing zones and the consignment merchants exist in this world.
- Real kill speed, coin per hour and how often live bots reach the Exchange (M0 measures).
- Whether template-aware valuation can be added without bots undressing (a test, then an overnight run).
- Whether the roster design (bot players with alts and a shared purse) is built; the crafter-as-alt rule depends
  on it.
- Whether a crowd of crafters at a forge reads as a living city or as furniture: only play answers it.
