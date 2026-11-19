# The economy ledger (Crafting 2.0 WP2)

The ledger measures coin; it changes no amount. It is 2.0's first milestone: before it, no log line recorded a gamebot's
coin coming in or going out (M0, `docs/CRAFTING-M0-FINDINGS.md` §4). **Tests only so far: nothing here has been seen in
a log on the owner's PC.**

- Setting: `bots / economy_ledger_enabled`, on by default (owner decision 8). Off: nothing is counted or written.
- Cost: a few `Interlocked` adds per coin move. Once an hour a background task reads the database (never the game loop).
- Code: `source/server/GameServer/craft/economy/ledger/`. The coin-flow sums are a port of shadowofze's
  `BotMoneyLedger165` (a8c793e, df86f9f), via True165, GPL-3.0.

## When lines are written

| When | Lines | Table row |
|---|---|---|
| Server start | `ECON_SUPPLY … baseline=1` per realm and All (the supply only) | none |
| Every UTC hour (21:00:00, 22:00:00 …) | `ECON_SUPPLY` and `ECON_FLOWS` for Albion, Midgard, Hibernia, `None` (only if a move could not be placed in a realm) and `All` | one `offline_economy_ledger` row per realm |
| Server stop | the same pair with `partial=1` for the hour so far | none |

The first hour after a start is shorter than 3600 seconds (`window=` says how long). If a snapshot cannot read the
database, its hour's moves are kept and the next line covers both hours.

## Reading ECON_SUPPLY

All amounts are copper (100 copper = 1 silver, 10,000 = 1 gold).

```
ECON_SUPPLY hour=2026-11-15T21:00:00Z realm=Albion window=3600 supply=… d_supply=… flows=… residual=…
  bot_coin=… player_wallets=… player_proceeds=… guild_banks=… d_bots=… d_players=… d_guilds=…
  bot_flows=… bot_residual=… roster_in=… roster_out=… new_bots=… gone_bots=… bots_unexplained=…
  bots=… online=… crafters=… median=… listed=… listings=… bot_listings=…
  bots_21_30=… coin_21_30=… median_21_30=… (one trio per level band with bots: 1_20, 21_30, 31_40, 41_49, 50)
```

| Key | Meaning |
|---|---|
| `supply` | The realm's money: `bot_coin + player_wallets + player_proceeds + guild_banks` |
| `d_supply` | Change since the last snapshot |
| `flows` | What the ledger saw: faucets − sinks of every bot coin move (transfers excluded) − Exchange tax + `roster_in` − `roster_out` |
| `residual` | `d_supply − flows`: what the ledger did not see (below) |
| `bot_coin` | Every gamebot purse (`offline_world_bots`; a live bot's record) |
| `player_wallets` | `AccountXMoney` per account and realm (an online player's live purse) |
| `player_proceeds` | Exchange sale proceeds credited to a player and not yet claimed at a broker |
| `guild_banks` | Every guild's bank, players' guilds included |
| `bot_flows`, `bot_residual` | The same sum for the bot purses alone (transfers and Exchange trades included). `bot_residual` should stay near 0; it is the bots' coin that no `AddMoney`/`TrySpend` or Exchange sale recorded |
| `roster_in`, `roster_out`, `new_bots`, `gone_bots` | Bots that appeared (launcher batches, new bots) or were removed bring or take their purse |
| `bots_unexplained` | Bots whose purse changed by a different amount than their recorded moves |
| `bots`, `online`, `crafters` | Bot rows, live bots, crafter gamebots (0 until WP4) |
| `median` | Median bot purse; `median_<band>` per level band |
| `listed`, `listings`, `bot_listings` | Asking prices of the Exchange listings (goods, not part of `supply`), how many, how many by bots |

**What the residual holds** (expected, not an error): every player-side coin move (a player's loot, merchants,
trainers, quests), guild dues taken from loot (they go to the bank directly), and raid loot shares
(`RealmRaidLootOwner` pays raid purses directly, so they also show in `bot_residual` and `bots_unexplained`). A coin move
racing the snapshot shows as a small residual in one hour and the opposite in the next. Coin held in craft-order escrow
(WP5) is outside the supply.

## Reading ECON_FLOWS

```
ECON_FLOWS hour=… realm=Albion window=3600 faucets=… sinks=… net=… per_online_bot_hour=…
  transfers_in=… transfers_out=… moves=…
  exchange_sales=… exchange_copper=… exchange_tax=… exchange_crafted=…
  exchange_bots_paid=… exchange_bots_received=… exchange_players_paid=… exchange_players_received=…
  in_lootcoin=copper/count out_vendorgear=copper/count … (True165's keys, one per kind that moved)
  earned_21_30=… net_21_30=… (per level band)
  unattributed="File.cs:Member in … out … xN" (All line only, when a caller could not be named)
```

| Key | Meaning |
|---|---|
| `faucets`, `sinks`, `net` | Per kind, the net of the hour (a refund cancels its spend); kinds with a positive net are faucets, negative sinks; the Exchange tax is a sink |
| `per_online_bot_hour` | `net` per live bot per hour: **the coin-per-hour number M0 could not measure** |
| `transfers_in/out` | Beggar gifts, Exchange credits, guild moves: coin between bot purses and the rest of the supply |
| `exchange_*` | Committed Realm Exchange sales (from the `SaleCommitted` seam): count, value, tax, crafted pieces, who paid and who received |
| `in_<kind>`, `out_<kind>` | Gross coin and the number of moves per kind |
| `earned_<band>`, `net_<band>` | Faucet coin and net per level band (1_20, 21_30, 31_40, 41_49, 50) |

Kinds today (classified from the caller, no call site was edited): `lootcoin` (a bot's pickup after guild dues),
`lootshare` (group-split coin), `vendorsale`, `vendorgear`, `stableticket`, `medallion` (frontier transport), `repairkit`,
`siege`, `dye`, `beggargift`, `exchangesale`. `unattributed` means a new call site that passes no kind; the unit test
`UT_EconomyFlowClassifier` fails until it is named.

## True165's line

Once an hour, beside the All lines, True165's own `BOT_MONEY_LEDGER_165 window=3600s in_total=… out_total=… net=…
in_<kind>=copper/count …` is written as well (every realm together, the `AddMoney`/`TrySpend` moves only, nothing in an
idle hour), so a tool written for True165's logs reads ours unchanged.

## The table

`offline_economy_ledger`, one row per realm and hour: `HourUtc` (the hour's end), `Realm`, `BotCoin`, `PlayerCoin`
(wallets + proceeds), `GuildBanks`, `ListedValue`, `FaucetsJson` and `SinksJson` (per kind, e.g. `{"lootcoin":123}`),
`ResidualCopper`, `Bots`, `Crafters`. Saved progress that stays in its world; release builds empty it.

## The launcher's economy page

The Living Server tab's ECONOMY PAGE button (and the Camelot Herald, which links to it) writes
`runtime\herald\economy.html` from this table and `offline_exchange_index`, read-only: each realm's supply at the last
recorded hour and its parts, the last 24 hours (supply, faucets, sinks, the Exchange tax as its own column, the
residual; every realm together), the top sinks, the flows by kind per realm and the price index's ten bands with the
most stock per realm. It reads the table, not the log, as the launcher reads every other saved state
(`source/tools/OfflineDaoc.Launcher/EconomyPage.cs`). Release builds ship the table empty.
