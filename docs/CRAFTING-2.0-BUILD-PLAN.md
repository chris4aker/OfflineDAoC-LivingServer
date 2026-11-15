# Crafting 2.0: the build plan

Owner: Chris. Written 2026-11-15 from the planner's build plan (code read at `integration/2.0` = `c58ea66`), reconciled
the same day with the fork survey: **two GPL-3.0 forks on our own base already built much of this**, and the owner's
rule is to port proven work where it fits. Design: [CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md) (facets A–V,
milestones M0–M6, settings §12, decisions §13, build status §15). Release order: [RELEASE-TRAIN.md](RELEASE-TRAIN.md)
§2.0 (the economy ledger is 2.0's first milestone). **Nothing here has been seen in game; every result is tests only.**

**Owner decisions** are marked **[owner]**. Everything else follows §13's proposed defaults and the standing rules:
every bot-side switch defaults off; crafters are our Crafter personality, 7% for new bots only (existing bots keep
their type); selling only at natural task breaks; gamebots start without armor and earn it; companions and real
players are untouched by bot-side packages.

## 0. Facts that shape the plan

- The crafting maths take a `GamePlayer` (`AbstractCraftingSkill`, `SpellCrafting`); only `GetItemCon` is pure.
  DividedRealms (DR) already split them by skill value (`ChanceToMakeAt`, `BaseChanceToGainAt`, `QualityAt`,
  `CraftingTimeAt`, `93c25b69`). WP0 does the same as static functions (`CraftRules`), so the rehearsal runs them too.
- Recipes load one at a time (`RecipeDB.FindBy`). DR's catalog loads them all, matching keys **without case**; the
  survey showed our "19% broken" was a case-sensitive audit (2 of 8,479 broken ignoring case).
- Town visits: a task boundary writes a `between-` assignment with flags `T` (train), `I` (inventory), `D` (downtime);
  `AutonomousBotEconomy.GetNeededService` (cached per bot) routes to Vendor, RealmExchange or SealMerchant; `BotBrain`
  trades on arrival. DR adds a craft trip as flag `C`, executed in the world controller.
- Bot coin moves through `AutonomousBotEconomy.AddMoney`/`TrySpend` (~25 call sites); Exchange sales credit sellers
  directly (`RealmExchangeTransactions.SellerPayment`) and are recorded by `RealmExchangeSaleLedger.CommitSale`.
- Since the design doc: a bot holds **3** listings; purchases score by `EquipmentValue`; dyes exist
  (`AutonomousBotCosmetics`); the Repair integer division (`d058073c6`) and the Salvage type mutation (`c7ee233d0`)
  are already fixed in our tree; the `Recipe.cs` null-recipe pricing is not (DR `abe7b327` fixes it).
- Brain turns run in parallel: seams called from `BotBrain` must be thread-safe and lock-free (copy-on-write registries).
- A namespace `DOL.GS.Economy` would shadow the `Economy` alias in existing tests, so the contracts live in
  `DOL.GS.Crafting` (rules, recipes) and `DOL.GS.CraftingEconomy` (everything else).

## 1. Ports: which fork code each package takes

Credit line for the changelog, as for earlier ports: "From <author> (<sha>), via <fork>, GPL-3.0". Clones:
`/home/claude/ext/dividedrealms` (rambodolphin13, @ `a0555cd4`), `/home/claude/ext/true165` (shadowofze, @ `f68cc75`).
Re-check both just before WP4 starts; they move fast.

| Package | Ports | What changes in the port |
|---|---|---|
| **WP0** Foundation | DR `AutonomousBotCrafting.ParseSkills/SerializeSkills/Join/Gain` (`93c25b69`) as `BotCraftingSkills` (the skill format); DR `OfflineBotCraftOrderRecord` columns and table name (`ceac061a`); True165 `BotMoneyLedger165.Kind` names in `EconomyFlowKind` (`a8c793e`, `df86f9f`); True165's `crafting_skill_rate` key (`00f9fbf`) | Gain takes a points count and cap; order record gains Source, BuyerId, BuyerName, MinQuality, Count, PayUpFront; nothing else ported runs in WP0 |
| **WP1** Core + M0 fixes | DR `abe7b327` (null-recipe guard in `RecipeDB.FindBy`, realm refusal message, `UT_CraftRequestReplies`); DR `fc6d2a95` / True165 `4bf3b8f3e` (unknown-recipe message); upstream `b62885a86` + `aa8b5232b` via DR `a3f3e4b8`, `60d3a4fb` (spellcraft 4-bonus check, focus gems on staves); upstream `5302c79e9` (salvage null-refs, merged by hand with our `c7ee233d0`); True165 `CraftingRate165` (`00f9fbf`, player pace and `TitlesReached`); DR's catalog `BuildCatalog` (`93c25b69`) as `RecipeCatalog` + `ICraftWorldIndex` | Callers delegate to `CraftRules`/`SpellcraftRules`; DR's four `…At` members kept as one-line delegations so DR code ports unchanged; the catalog adds alchemy tables (820) and all seven crafts; its `AUTONOMOUS_CRAFT_CATALOG` line becomes `CRAFT_RECIPE_AUDIT` + `CRAFT_WORLD_AUDIT` (old tag kept as alias); pace reads `CraftingEconomyProperties` |
| **WP2** Ledger | True165 `BotMoneyLedger165` (`a8c793e`, `df86f9f`) as the `IEconomyFlowSink` | Kind from the caller's explicit kind, else classified from `[CallerFilePath]`/`[CallerMemberName]` (no call-site edits); per realm; Exchange transfers and tax from `SaleCommitted`; hourly `ECON_SUPPLY` snapshot row in `offline_economy_ledger` with a residual |
| **WP3** Player side | upstream `ad789ee4d` (drop Basic Crafting titles) with era start; upstream `7b0df1b12` (drop the post-era Legendary Grandmaster title) **[owner]** | Era start, secondary caps, upkeep cost and coin by band are ours |
| **WP4** Bot crafters | DR `AutonomousBotCrafting.cs` + `AutonomousWorldBotController.Crafting.cs` (`93c25b69` → `ceac061a` → `e37f8810`); True165 `CrafterBots165` "list only the best of a session" and siege tiers (`b097caf`, `75267dd`); True165 `BotEnchant165` for spellcraft and alchemy (`5f31c2b` → `a8c793e`) | Gate by our Crafter type with stored types (`offline_bot_types`) and the 7% new-bot share instead of DR's "any bot of level 5+, 10%"; DR's `bot_crafting` → our `bot_crafters` (off); orders by our realm weights, DR's class rule as tie-break **[owner]**; bot pace added (True165's `Points` with the bot XP rate); enchanting uses `SpellcraftRules`, not True165's copy; trip hooks through `IBotBreakActivity` ('C') and the partial `RunBreakActivities`; material and own-work rules through `IBotItemPolicy`; DR's quest runner (same root commit) is **not** taken here **[owner]** |
| **WP5** Market | DR `AutonomousBotCraftOrders.cs` (`ceac061a`, `e37f8810`, `24f8a2e7`); DR `RealmExchangeSaturation.cs` (`702ef108`); DR own-work listings (`ceac061a`); DR `AUTONOMOUS_EXCHANGE_SOLD … crafted=` line; True165 `CraftOrders165` milestone sets (`f6dbe48`) as a second order source | Orders on `ICraftOrderBoard`/`OfflineBotCraftOrderRecord`; `AtTaskBreak` through `IBotTaskBreakHook`; `IsHeldForOrder` through `IBotItemPolicy.IsReserved`; saturation through `BlocksListing`/`OnListed`; extra posts through `IExchangeListingPolicy` (`bot_crafter_exchange_listings` 6); the sale line from `SaleCommitted`; switches default off. Ours: price index, quartermaster commissions, Exchange tax (built, default **0**, 5% documented) |
| **WP6** Demand | True165 `ItemValue165` (`32a88e0`, `145bdee`) as the `IPriceIndex` floor/fair/ask; True165 `BotGearValue165` (`1edbcf5`, cap-aware value) as the base of template-aware valuation | Outside True165's 1.65 Mode switch; the gear check at task breaks, the level-50 template and the no-undress guard are ours |
| **WP7** Sinks | True165 `ItemWear165` (`36358de`; the survey had not located it: `mode165/ItemWear165.cs`); `TownErrands165` repair errand at 88% (`36358de`); `BotPotions165` (`2e4212e`) | Gated by `bot_upkeep` (off), not 1.65 Mode; autonomous gamebots only (True165 also wears hired companions'); the errand is an `IBotTownVisitNeed`, the repair a merchant `IBotServiceStep`; paid poisons, luxury sink and governor are ours |
| **WP8** Commerce | none (ideas only: mod-playerbots' per-pair discount, cmangos guild orders; no code) | — |
| **WP9** Launcher, **WP10** Rehearsal | none | The rehearsal runs the ported pure parts (`CraftRules`, DR's `ChooseRecipe`, True165's `ItemValue165`) |

Not ported: True165's player crafting assistant and craft-anywhere (a declared deviation from 1.65, needs client ops);
DR's Bountycraft, Mauler/Dragonsworn recipes and fishing (post-era); bazaarBot (optional, prototype in the simulator
first). True165's 845 recipes and period merchant stock are M0 data, derived from the EA client **[owner]**.

## 2. Architecture

Pure decisions in static classes that take records; a thin glue class touches `GameBot`, the database and NPCs (the
`AutonomousBotCosmetics` convention). Later packages **register** implementations; they do not edit the shared files.

| Layer | Where | What |
|---|---|---|
| Rules | `craft/CraftRules.cs`, `craft/SpellcraftRules.cs` (`DOL.GS.Crafting`) | Con, chance to make, gain, time, quality, secondary rules; imbue, gems, overcharge, preserve |
| Contracts | `craft/economy/*.cs` (`DOL.GS.Crafting`, `DOL.GS.CraftingEconomy`) | Interfaces, records, holders, seams, tables, settings, log format |
| Bot crafters | `bots/autonomous/crafting/` | DR's crafting and trip, roster, stored types, pace (WP4) |
| Market | `craft/economy/market/` | DR's orders and saturation, index, commissions, tax, listing policy (WP5) |
| Demand | `bots/autonomous/gear/` + `AutonomousBotEconomy.cs` | True165's valuation, gear check, template, WTB (WP6) |
| Sinks | `craft/economy/sinks/` | True165's wear, errands, potions; luxury, governor (WP7) |
| Commerce | `craft/economy/commerce/` | Tells, quotes, haggling, delivery, jobs, memory (WP8) |
| Ledger | `craft/economy/ledger/` | True165's ledger as the flow sink, snapshot (WP2) |

### 2.1 Seams (WP0; every default is today's behaviour)

| Seam | Called from | Default | First user |
|---|---|---|---|
| `EconomySeams.Listings` (`IExchangeListingPolicy`: `MaxListings`, `ListingCap(owner, item)`) | `FindFreeListingSlot(owner, item)`, `FindValuableListingCandidate`, `CanListNow` | 3 for every item | WP5 (DR own work +3) |
| `EconomySeams.Register(IBotItemPolicy)`: `IsReserved`, `KeepFromVendor`, `IsMaterialFor`, `BlocksListing`, `AlwaysWorthListing`, `ListingPriority`, `OnListed` | listing candidate, vendor protection, equip check, blocked-bag clear, `TryList`, `ScorePurchase` | none (today's rules; `IsMaterialFor` falls back to "any trained crafter keeps every stack") | WP4, WP5 |
| `EconomySeams.TryReclaimExpiredListing` | `RealmExchangeExpiry.ExpireDue` | take nothing (24 h deletion) | WP5 |
| `EconomySeams.SellerProceeds` (price, realm, seller, buyer) | `PrepareSellerPayment` (bots and players) | whole price | WP5 tax |
| `EconomySeams.SaleCommitted` (`Sale` with `TaxCopper`, `Crafted`) | `RealmExchangeSaleLedger.CommitSale` after the save | no subscriber | WP2, WP5 |
| `AddMoney`/`TrySpend(…, EconomyFlowKind kind = Unattributed, [CallerFilePath], [CallerMemberName])` → `EconomyFlows.Sink` | every coin move | null sink | WP2 |
| `BotServiceSeams.Register(IBotTownVisitNeed)` | `TryBeginBetweenTaskServices` (OR into `I`), end of `EvaluateNeededService` | none | WP4, WP6, WP7 |
| `BotServiceSeams.Register(IBotServiceStep)` | `BotBrain` merchant handler (before cosmetics), Exchange handler (after listing, before purchase) | none | WP5–WP7 |
| `BotServiceSeams.Register(IBotBreakActivity)` + partial `AutonomousWorldBotController.RunBreakActivities` | `TryBeginBetweenTaskServices` (flag letters), `TryCompleteBetweenTaskServicesIfSatisfied`, controller between training and inventory | none; draws no random number; the partial compiles away | WP4 (DR craft trip, 'C') |
| `BotServiceSeams.Register(IBotTaskBreakHook)` (`Begin`, before the group check; `End`, in `CompleteBetweenTaskServices`) | assignments | none | WP5 (DR `AtTaskBreak`) |
| `EconomyLifecycle.Register(IEconomyStore)` (Load, Pulse, Flush) | `AutonomousLivingServer` start, pulse, flush | none | WP2, WP4, WP5 |
| `BotCommerceSeams.TryReceiveItem/TryReceiveMoney` | `GameBot.ReceiveItem/ReceiveMoney` after the beggars | take nothing | WP8 |

Lock order when a seam runs: `RealmExchangeBroker.TransactionLock` → `typeof(AutonomousBotEconomy)` →
`DatabaseWriteLock` → package locks. A provider calls `MarkEconomyChanged(botId)` when its answer changes (the
service choice is cached).

### 2.2 Contracts (WP0)

- `CraftRules`, `CraftContext`, `CraftCon`; `SpellcraftRules` (verbatim copies; parity-tested against the originals).
- Recipes: `CraftMaterial`, `CraftRecipeInfo` (pure data, plus the server `Recipe` when built from the world),
  `IRecipeCatalog`, `ICraftWorldIndex` (masters, stations, material sellers), `RecipeCatalogs` (empty holders).
- Crafters: `CrafterCohort`, `CrafterSpendPolicy`, `CrafterState`, `IBotCrafterRoster`, `BotCrafters` (null roster),
  `BotCraftingSkills` (DR's `SerializedCraftingSkills` format).
- Market: `CraftOrder` (DR's fields first), `CraftOrderSource`, `CraftOrderState`, `ICraftOrderBoard`, `CraftOrders`;
  `MarketBand` (DR's five-level band), `IPriceIndex` (True165's floor/fair/ask shape), `PriceIndexes` (today's numbers).
- `UpkeepPolicy` (classic / light / free).

### 2.3 Tables (WP0, `craft/economy/EconomyTables.cs`; all `StaysInWorld`, all in `OptionalProgressTables`)

| Table | Key | Notes |
|---|---|---|
| `offline_bot_types` | BotId | Every bot's type stored on first sight, so the 7% share never re-types anyone |
| `offline_bot_crafters` | BotId | Order, policy, counters; skills stay in `offline_world_bots.SerializedCraftingSkills` (DR's format) |
| `offline_bot_craft_orders` | OrderId | **DR's table and columns**, plus Source, BuyerId, BuyerName, MinQuality, Count, PayUpFront |
| `offline_exchange_index` | BandKey | WP5 |
| `offline_economy_ledger` | (HourUtc, Realm) | WP2 |
| `offline_crafter_customers` | (CrafterBotId, CustomerId) | WP8 |

The planner's `offline_crafter_stock` is dropped: DR's crafters make **real items** in real bags (`IsCrafted`,
`Creator`), so stock is inventory, not a ledger. No column is added to `offline_world_bots`. The launcher's purge SQL
for these tables is WP9's.

### 2.4 Settings (WP0, `CraftingEconomyProperties`; nothing reads them in WP0)

| Key | Default | Read by |
|---|---|---|
| `crafting_era_start` | True | WP3 |
| `crafting_secondary_caps` | False | WP3 |
| `crafting_skill_rate` (True165's key; replaces `crafting_player_pace`) | 1.0 | WP1 |
| `player_upkeep` (classic / light / free) | light | WP1, WP3 |
| `player_coin_by_level` | True | WP3 |
| `crafting_classic_quality`, `crafting_classic_losses` | False | WP1 |
| `bot_crafters` (master switch) | **False** | WP4–WP8 |
| `bot_craft_orders` (DR's second switch) | **False** | WP5 |
| `bot_crafter_share_percent` | 7 (new bots only) | WP4 |
| `bot_crafting_pace` (xp / classic) | xp | WP4 |
| `realm_commissions` (classic / doubled / off) | doubled | WP5 |
| `exchange_tax_percent` | **0** (owner 2026-11-15: until the ledger shows a need; 5 is the documented option) | WP5 |
| `bot_upkeep`, `bot_luxury_spending`, `bot_siege_supply` | **False** (the planner had "on with crafters") **[owner]** | WP7 |
| `bot_crafter_chat_rate` | 1.0 | WP4, WP5, WP8 |
| `bot_crafter_exchange_listings` | 6 (DR's 3 + 3) | WP5 |
| `launcher_level50_kit` | True (today's launcher) | WP9 |
| `economy_ledger_enabled` | True (logs and one row an hour; no amount changes) **[owner]** | WP2 |
| `craft_made_lines`, `econ_trade_lines` (diagnostics) | False | WP4, WP5 |

Modes (pace, commissions) keep their proposed values but do nothing while `bot_crafters` is off.

### 2.5 Logs (`EconomyLog`: tags plus one formatter)

`TAG key=value key="text"`, invariant culture, copper whole. `CRAFT_`: RECIPE_AUDIT, RECIPE_BROKEN, WORLD_AUDIT,
ERA_JOIN, PLAYER_TITLE, ENROLL, SESSION_START, MADE (gated), SESSION_END, TITLE, FIRST, EXPLOSION, STORY. `ECON_`:
SUPPLY, FLOWS, ORDER, COMMISSION, LIST, SALE, RECLAIM, INDEX, GEAR_CHECK, TEMPLATE_BUY, UNDRESS_GUARD, WEAR, REPAIR,
CONSUMABLE, LUXURY, SIEGE_SUPPLY, GOVERNOR, PLAYER_ORDER, PLAYER_DELIVERY, PLAYER_SELL, SC_JOB, HAGGLE, GUILD_SPEND,
REHEARSAL. Fork tags kept as aliases where a tool reads them: `AUTONOMOUS_CRAFT_CATALOG`, `AUTONOMOUS_EXCHANGE_SOLD`,
`BOT_MONEY_LEDGER_165`. AI_PROFILE scopes: `craft.session`, `economy.market`, `economy.ledger`, `economy.upkeep`,
`craft.commerce`.

### 2.6 Bot types

| Who | What changes |
|---|---|
| Real players | WP1 rules and options; WP3 era start, caps, upkeep, coin by band; the tax only if they trade; WP8 commerce they start |
| Companion bots (`/bot`, `IsTemporaryGroupHelper`) | Nothing, anywhere: no wear, crafting or shopping (True165's companion wear is not ported) |
| Autonomous gamebots | Everything bot-side; every glue method guards `IsAutonomousWorldBot && !IsTemporaryGroupHelper` |

## 3. Work packages

One owner per file per wave. Append-only: `CHANGELOG-LIVING-SERVER.md` (`### WPn` under "## 2.0: crafting and the
economy"), the `build_corpus.py` registries (`# WPn`), §15 of the design doc (own row). `corpus.json` conflicts are
resolved by re-running `build_corpus.py`. A package needing a new table, setting or interface member amends WP0's
files in a small PR first.

- **WP0 Foundation** — built; see §15 of the design doc and the changelog.
- **WP1 Core and M0 fixes.** Owns `craft/AbstractCraftingSkill.cs`, `AdvancedCraftingSkill.cs`, the professions,
  `CraftingMgr.cs`, `Recipe.cs`, `Repair.cs`, `Salvage.cs`, `CraftAction.cs`, `CraftComponent.cs`, `CraftRules.cs`,
  `SpellcraftRules.cs`, new `RecipeCatalog.cs`. Does: delegate to the rules; classic quality and losses; player pace
  (True165); the ports in §1; `Repair` reads `UpkeepPolicy.ConsumesDurability`. Tests: `UT_CraftRules`,
  `UT_CraftRulesClassic`, `UT_SpellcraftRules`, `UT_RecipeCatalog` (case-insensitive; adjust-price on does not throw),
  DR's `UT_CraftRequestReplies`, True165's `UT_CraftingRate165`.
- **WP2 Ledger** (merges first in wave A, so the soak starts). True165's ledger as the sink; classifier; snapshot;
  `SaleCommitted` subscriber. Tests: True165's `UT_BotMoneyLedger165` adapted; `UT_EconomyFlowClassifier`;
  `UT_EconomyLedgerSnapshot` (residual = Δsupply − Σflows).
- **WP3 Player side (M1).** Owns the crafting region of `GamePlayer.cs`, `CharacterCreationCraft.cs`,
  `CraftingMaster/*`, `AbstractProfession.cs`, `Blacksmith.cs`, `LootGeneratorMoney.cs`/`LootGeneratorChest.cs`,
  `Crafting.txt`, new `PlayerCraftingRules.cs`, `docs/CRAFTING.md` (original client only for now).
- **WP4 Bot crafters (M2).** Ports DR's crafting and trip (§1) into `bots/autonomous/crafting/` and the controller's
  `RunBreakActivities` partial; owns `AutonomousBotPersonality.cs` (`TypeFor` reads stored types; new bots use the 7%
  table) and, for station presence after the trip works, `AutonomousTownHangouts.cs`. Tests: DR's
  `UT_AutonomousBotQuestsCrafting` crafting half adapted; `UT_BotCrafterRoster` (legacy 12% kept, 7% for new bots,
  stored types never change, beggars excluded); the existing `UT_AutonomousBotPersonality` pin stays green.
- **WP5 Market.** Ports DR's orders and saturation (§1); price index, commissions, tax. Owns
  `AutonomousBotChatCoordinator.cs` (adverts with real stock). Tests: DR's `UT_AutonomousBotCraftingOrders` and
  `UT_RealmExchangeSaturation` adapted; `UT_PriceIndex` (no buy-sell loop makes coin); `UT_ExchangeTax` (0 and 5%; a
  player who never trades pays nothing). Merges after a week of ledger data.
- **WP6 Demand (M3).** Owns `AutonomousBotEconomy.cs` (after WP0) and `AutonomousAuctionValuation.cs`. True165's
  valuation (§1); gear check at task breaks; template at 50; no-undress guard. Tests: True165's `UT_BotGearValue165`;
  `UT_TemplateValuation`; `UT_NoUndress` (10,000 random loadouts).
- **WP7 Sinks (M5).** Owns `GameInventoryItem.cs`, `BotPoisonSupply.cs`, `BotSiegeRuntime.cs`. True165's wear,
  errands, potions (§1); luxury; governor. After WP2's soak shows a need.
- **WP8 Commerce and social (M4, M6).** Owns `trade.cs`, `AutonomousLivingServer.Tells.cs`,
  `AutonomousChatIntentModel.cs`; 8b `AutonomousBotStorylines.cs`, `AutonomousBotGuildEconomy.cs`, `AutonomousBotGuilds.cs`.
- **WP9 Launcher.** Settings rows for merged features, the level-50 kit toggle, purge SQL for `EconomyTables.All`,
  Herald top crafters, economy page, tax column.
- **WP10 Rehearsal.** A week of a realm in minutes on the packages' pure code; `/lscraft`; `econ_timeline_report.py`.

Merge order: WP0 → wave A (WP2 first, WP1, WP9, WP10) → WP3 (play M1) → wave B (WP4 for M2; WP5 after a week of
ledger data; WP6 for M3) → wave C (WP7 after the soak shows a need; WP8). Each milestone ships only after its in-game
check on the owner's PC, reported apart from tests and the rehearsal.

## 4. Risks

1. **Parallel brain turns.** Registries are copy-on-write; seams must not lock or touch the database in an AI turn.
2. **The service cache.** A provider must call `MarkEconomyChanged`.
3. **Re-typing bots.** Stored types plus the cohort keep existing bots' 12%-era types, beggars and voices.
4. **Practice flooding** (DR's test world: 9,056 practice pieces, 2,180 woolen cloaks). DR's saturation cap and
   True165's "list only the best of a session" both go in with WP4/WP5.
5. **Bot crafting pace.** Both forks found player pace far too slow (DR: median skill 8 after a night; True165: best
   crafter at 5 after five hours); the bot pace follows the bot XP rate (decision 2).
6. **Undressing.** Template-aware valuation needs its property test, the guard log and an overnight run.
7. **Database growth.** Every crafted piece is an `ItemUnique`; listings are capped and saturated; watched in `ECON_SUPPLY`.
8. **Inflation.** Commissions and coin by band are faucets: ledger first, governor before 2.0.
9. **Natural task breaks.** Trips, orders, gear checks and repairs run only in the between-task phase.
10. **Fork drift.** DR and True165 are moving; port from a pinned SHA and record it.

## 5. Decisions that remain the owner's

1. Crafter orders by realm weights (proposed) or DR's class rule (archers fletch, casters tailor), or a blend.
2. Bot order price: DR's materials + 25% (at least 100 copper) or True165's materials × 1.75 (period 1.5–2).
3. Unsold crafted listings: back to the crafter (proposed) or True165's undercut-relist-pay-at-merchant cycle.
4. True165's recipe and merchant data (derived from the EA client's `tdl.irf`) as M0's era source.
5. DR's quest runner (bundled in `93c25b69`): take it separately, or leave it.
6. Drop the post-era Legendary Grandmaster title (upstream `7b0df1b12`).
7. `bot_upkeep`, `bot_luxury_spending` and `bot_siege_supply` off by default even with crafters on (WP0 built them off).
8. `economy_ledger_enabled` on by default (logs and one row an hour only).
9. The design doc's §13 decisions 1–6 and 8–13 (two ladders, paces, coin by band, upkeep light, commissions, the 7%
   share, the level-50 kit, classic options, bots' gear wearing, release placement) stay as proposed until the owner
   says otherwise. Decision 7 (the tax) is settled: built, default 0, 5% documented.
