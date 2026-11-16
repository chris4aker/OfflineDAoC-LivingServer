# Crafting

How crafting works for your characters on a Living Server world from 1.2 on. For the plan behind it see
[CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md); for how it is built, [CRAFTING-2.0-BUILD-PLAN.md](CRAFTING-2.0-BUILD-PLAN.md).

> **Status: automated tests only.** Nothing on this page has been seen in game yet. It describes what the code does.

> **Crafting works in the original game client only, for now.** The new client (0.37c) shows your crafting skills
> and title on the character sheet, but it has no crafting window: you cannot craft, salvage or repair from it. Log in
> with the original client to craft. Both clients share one account, so skills you train there show in the new client.

## Joining an order (era start)

In 1.65 a new character did not know any trade. You walked to a crafting master in your capital and asked to join
the order; the master taught you its crafts. Living Server works that way again (the setting **Era start**, on by
default):

1. Go to a crafting master and right-click (interact with) them.
2. They tell you about the order. Click the order's name in the text, for example **[Tailors]**.
3. Say yes in the box. You are now a member: the order's craft is your primary skill, and the master teaches you its
   crafts and the material skills they need, each at 1.

Until you join an order you have no crafting skills. Trying to craft says *"You have not been trained in ..."*.

**The six orders** and what their masters teach (each at 1):

| Order (master's title) | Primary craft | Also taught |
|---|---|---|
| Weaponcrafters (Weaponsmiths Master) | Weaponcraft | Armorcraft, Tailoring, Fletching, Siegecraft; metal, leather, cloth and wood working |
| Armorcrafters (Armorsmiths Master) | Armorcraft | Weaponcraft, Tailoring, Fletching, Siegecraft; metal, leather, cloth and wood working |
| Tailors (Tailors Master) | Tailoring | Weaponcraft, Armorcraft, Fletching, Siegecraft; metal, leather, cloth and wood working |
| Fletchers (Fletchers Master) | Fletching | Weaponcraft, Armorcraft, Tailoring, Siegecraft; metal, leather, cloth and wood working |
| Alchemists (Alchemists Master) | Alchemy | Spellcraft, Siegecraft; gemcutting and herbcraft |
| Spellcrafters (Spellcrafters Master) | Spellcraft | Alchemy, Siegecraft; gemcutting and herbcraft |

**Where the masters are.** Every order has a master in both capitals of each realm:

| Realm | Capital | Weapon | Armor | Tailor | Fletch | Alchemy | Spellcraft |
|---|---|---|---|---|---|---|---|
| Albion | Camelot | Hephas Elgen | Loraine Elgen | Arliss Eadig | Runthal Devyn, Acey Dalston | Adelaide Dinsmore | Clayton Gage |
| Albion | Avalon City (Isle of Glass) | Grayson Sanion | Morgance Scott | Bronwyn MacEgan | Alec Marah | Mirelle Edmunds | Stephen Cathal |
| Midgard | Jordheim | Aase | Gest | Eskil | Gils | Armod Dag | Hradi Thorleif |
| Midgard | Aegir's Landing | Beiwe | Lofn | Geiti | Brono | Edony | Baakli |
| Hibernia | Tir na Nog | Hendrika | Tegvan, Dunstan | Armin | Arziqua | Aslander | Shannen |
| Hibernia | Domnann | Clora | Monaghan, Taltena | Eriano | Jynaia | Mairsil | Laisren |

Most housing villages have masters too. Adelaide Dinsmore (Camelot), Baakli (Aegir's Landing) and Eriano (Domnann) are
masters through their title, which the server turns into the master when `use_npcguildscripts` is on (the default and
the shipped world's value; the server log warns at start-up if it is off).

**Changing your primary.** Speak to the master of another order whose craft you already have (1 or more): they make it
your primary, with no new joining. You can also join a second order you have none of the crafts of (for example a
Tailor joining the Alchemists): its crafts are added at 1 and become your primary.

**Not orders in 1.65:**
- **Basic Crafting** came in 1.76b (2005). With era start on, the Basic Crafters Master in Camelot (Jon Truban) takes no
  new members and sends you to the six orders. A character already in Basic Crafting keeps it.
- **Siegecraft** became a full trade in 1.90. Every order teaches it as a secondary skill, but there are no siegecraft
  recipes in this world and no order takes siegecrafters. Siege engines come from the siege merchants at the keeps.

With era start off, a new character starts as before 1.2: Basic Crafting, every skill at 1.

**Crafting is shared per account and realm.** All your characters in one realm share one set of crafting skills and one
order (the server's rule since OpenDAoC). So "a new character starts without an order" means the first character of
your account in a realm; a second character there shares the first one's crafts.

## Titles

Your crafting title follows your primary craft by the hundred: Helper, Junior Apprentice, Apprentice, Neophyte, Assistant,
Junior, Journeyman, Senior, Master, Grandmaster (900), Legendary (1000 and above). There is **no Legendary Grandmaster**
(it did not exist in 1.65) and **no Basic Crafting title**. A character without an order has no crafting title.

## Settings (launcher, Living Server tab, Crafting card)

Changes take effect at the next server start, like the tab's other settings. ALL OFF leaves them as they are.

| Setting | Default | What it does |
|---|---|---|
| Era start | On | New characters join an order at a master (above). Off: Basic Crafting with every skill at 1. Never changes a character that already crafts |
| Crafting pace | 1 per gain | Skill points each time a craft raises your skill, 1 (classic) to 10. A grey recipe still gives nothing |
| Repair upkeep | Light | What the smith charges, and whether a repair (by the smith or by another player in a trade window) wears the item out. **Classic**: the full price, and the repair uses up as much durability as it restores (an item at 0 durability can't be repaired any more). **Light**: half price, rounded up, and no durability used. **Free**: nothing, no durability used. "Repair all" adds 20% before the setting applies |
| Coin by level | On | Coin from your kills (and your pet's or your /bot companions') follows your XP rate for your level (the flat XP rate, or the XP-by-level range that covers your level), so a level earns the coin it did at 1× XP. Never lowers coin; at 1× nothing changes. Gamebots' coin is never changed |
| Secondary skill caps | Off | Another order's craft stops at a share of your primary, as the masters describe it: 75% for the order's "good skills", 40% for its "great difficulty" (a Tailor: Fletching 75%, Weaponcraft and Armorcraft 40%). Material skills are never capped. A skill already above its cap keeps its value; it only stops rising. Not applied to a Basic Crafting primary, or to GMs |
| Classic quality | Off | 94-99% quality with a small chance of a masterpiece at any skill. Off: 96-99%, masterpieces only at 1000 skill or on grey recipes |
| Classic losses | Off | A failed craft costs its materials, and a spellcraft explosion destroys the item. Off: a failure costs nothing, the item survives an explosion |

The secondary caps, by order (percent of the primary skill):

| Primary | Weaponcraft | Armorcraft | Tailoring | Fletching | Siegecraft | Alchemy | Spellcraft |
|---|---|---|---|---|---|---|---|
| Weaponcraft | — | 75 | 40 | 40 | 75 | | |
| Armorcraft | 75 | — | 75 | 40 | 40 | | |
| Tailoring | 40 | 40 | — | 75 | 40 | | |
| Fletching | 75 | 40 | 40 | — | 75 | | |
| Alchemy | | | | | 40 | — | 75 |
| Spellcraft | | | | | 40 | 75 | — |

Server keys, for helper scripts: `crafting_era_start`, `crafting_skill_rate`, `player_upkeep` (classic / light / free),
`player_coin_by_level`, `crafting_secondary_caps`, `crafting_classic_quality`, `crafting_classic_losses`.

## Bot crafters and the economy (launcher, Living Server tab)

The card below the Crafting card. Like the rest of the tab, changes take effect at the next server start. Only
autonomous gamebots craft and trade under these settings; your /spawn companions never do. Every bot-side switch is
on by default since 1.2 (owner 2026-11-16: "all our living server features should be in by default if someone
downloads our installer"); only the diagnostics lines (`craft_made_lines`, `econ_trade_lines`) stay off. **Tests
only:** none of the bot side has been seen in game yet.

| Setting | Default | What it does |
|---|---|---|
| Bot crafters | On | Crafter gamebots join an order, buy real materials, work at the capital's stations and sell what they make. Off: no gamebot crafts |
| Crafters among new bots | 7% | Share of new gamebots that are Crafters; existing bots keep their type |
| Bot crafting pace | Bot XP rate | Points per gain follow the bot XP multiplier of the crafter's level; Classic: one point |
| Craft orders | On | Gamebots pay a crafter of their realm for an upgrade, the coin held until the piece is handed over. Needs bot crafters |
| Realm commissions | Doubled | The quartermaster's orders to crafter bots: Classic 30 a realm an hour, Doubled 60, Off |
| A crafter's Exchange posts | 6 | Realm Exchange posts a crafter bot may hold for its own work (3 to 100; other bots hold 3) |
| Crafters' chat | 1× | Crafters' trade and station chat, 0 (silent) to 3 |
| Realm Exchange tax | 0% | Kept from the seller's proceeds of each Exchange sale, yours and the bots' (0 to 50; 5 is the classic option) |
| Economy ledger | On | Counts each realm's coin, faucets and sinks once an hour (log lines and one row an hour). Changes no amount |
| Bot upkeep (server setting `bot_upkeep`, not in the tab) | On | Gamebots' gear wears like a player's and they pay for repairs, potions and poisons. Needs bot crafters |
| Luxury spending (server setting `bot_luxury_spending`) | On | Rich gamebots spend part of their surplus on real goods and services. Needs bot crafters |
| Siege supply (server setting `bot_siege_supply`) | On | Siegecrafter gamebots supply siege engines and kits below the merchant price. Needs bot crafters |
| Level-50 kit and gold | On | "+ Lv.50" bots arrive with the quality-100 kit and 10,000 gold. Off (a fresh-start world): no kit, a 25-gold purse, and plain merchant gear for their level from the server at their first login |

**ECONOMY PAGE** writes and opens `runtime\herald\economy.html`: each realm's money supply, the last 24 recorded hours
with faucets, sinks, the Exchange tax and the residual, the biggest sinks, the flows by kind and the Exchange's price
index, all as the ledger records them ([ECONOMY-LEDGER.md](ECONOMY-LEDGER.md)). The Camelot Herald links to it and lists
the top five crafters of each order. ALL OFF switches bot crafters and craft orders off; it leaves the tax, the ledger
and the kit as they are.

Server keys: `bot_crafters`, `bot_crafter_share_percent`, `bot_crafting_pace` (xp / classic), `bot_craft_orders`,
`realm_commissions` (classic / doubled / off), `bot_crafter_exchange_listings`, `bot_crafter_chat_rate`,
`exchange_tax_percent`, `economy_ledger_enabled`, `launcher_level50_kit` (read by the launcher), `bot_upkeep`,
`bot_luxury_spending`, `bot_siege_supply`, `bot_crafter_commerce` and `bot_crafting_social` (WP8, server settings only, on).
Diagnostics, off: `craft_made_lines`, `econ_trade_lines`.

## Updating a world from 1.1.4

Your world keeps every character's crafting. Exactly what you see:

- **Your characters keep their skills and their primary order**, whatever it was (most likely Basic Crafting, with
  every skill at 1 or higher). Era start never removes a skill or an order from a crafting record that exists.
- **A character whose primary is Basic Crafting has no crafting title any more**, and the title list no longer offers
  one. To get a title, speak to any order's master: as you already have every craft, the master just makes it your
  primary (no joining). Jon Truban, Camelot's Basic Crafters Master, still lets an existing Basic Crafter make Basic
  Crafting the primary again.
- **A crafter at 1100 or more** reads "Legendary ..." instead of "Legendary Grandmaster ...".
- **A new character in a realm your account already plays** shares that realm's crafting, so it has the same skills.
  **A new character in a realm your account never logged into** (or a new account) starts with no order and no skills:
  join one at a master.
- **The smith charges half** and repairs (the smith's and a player's in a trade window) no longer use up durability
  (Repair upkeep: Light). Choose Classic in the tab for the old rules.
- **Coin from kills grows with your XP rate.** At 1× XP nothing changes; at 10× XP (the Options tab, or a Living Server XP
  range), a monster drops ten times its coin for you. Switch Coin by level off in the tab for the old coin.
- Secondary caps, classic quality and classic losses are off, and the crafting pace is 1, so crafting itself plays as
  before until you change them.
- **Bot crafters, craft orders, bot upkeep, luxury spending, siege supply, crafter commerce and crafting social life come on** at the first start of 1.2, and
  so do bot guilds, bot personalities, the Server life parts that were off and the real-clock bot schedule. The server
  changes only a setting the world still holds at the old default it wrote itself; one you set in the launcher's
  Living Server tab is kept. The log says `Living Server 1.2.0.1: <key> set to the new default ...` for each one.

## Buying from crafter gamebots

> **Status: automated tests only.** On by default since 1.2 (owner 2026-11-16): it needs the server settings
> `bot_crafters` and `bot_crafter_commerce`, both on unless you switch one off. Nothing here has been seen in game yet.

Crafter gamebots of your realm answer you by `/send` (and a `wtb ...` in `/trade`) from their real skill and the crafted
pieces in their bag:

- **"what do you make?"**: their title, skill and craft, what they make surely, and their stock with prices.
- **"wtb chain hauberk lvl 23"**, **"can you make a studded helm"**, **"full leather set lvl 20"**: a price (materials plus
  the crafter's fee; less for a regular customer, materials only for a guildmate), when it will be ready and how you pay.
  They say so when they cannot make it, and they never promise a quality.
- **"too much"**, **"how about 80s"**: a short haggle. **"deal"** takes the quote. **"ready?"** and **"cancel"** work too.
- **Paying.** Up front: drag the coin onto the crafter when you are near it (only the price is taken); it is held until
  the piece is handed over and returned if it is not made in 6 hours (at your next login if you are offline then). On
  delivery: the crafter opens a trade window with the piece and its price; put the coin in and accept.
- **Hand-over.** Gamebots craft and trade only at their natural task breaks, never during a hunt: when your piece is made
  the crafter tells you where it is; stand near it during its break and say "ready".
- **Stock.** A piece it already holds you may buy at once, even while it hunts, if you stand next to it and it is not
  fighting: see "Trading with gamebots" below.
- A piece you ordered to pay on delivery and never collected stays the crafter's, and it will want your coin up front
  next time. Insult a crafter and it will not deal with you.

## Trading with gamebots

> **Status: automated tests only.** Off by default: `bot_crafters` and `bot_crafter_commerce` must both be on (the guild
> parts also need `bot_crafting_social`). Nothing here has been seen in game yet.

Any autonomous gamebot of your realm trades with you in the game's own trade window, the way a player does. A `/bot`
companion never trades (give it gear with `/bot`), and only you start a trade.

**Opening a trade.** Stand next to the bot (about two steps) and either drag an item or coin from your backpack onto it,
or target it and type `/tradewindow`. A tell works too: "trade me", "lets trade", or "buy 2" once you have its list. The
window is titled **Trading with &lt;name&gt;**, as for a player.

**What it sells.** When the window opens, the system window lists what the bot would part with, numbered:

```
[Aldwyn sells] 2 pieces: say "buy <number>" to Aldwyn, or /tradewindow buy <number>.
[Aldwyn sells] 1. Studded Vest (96%, level 23): 1 gold, 20 silver
[Aldwyn sells] 2. Hunting Bow (level 20): 25 silver (on the Realm Exchange)
```

- a crafter's own crafted pieces, at the price it quotes by tell;
- loot and spare gear in its bag, at the market's asking price (the price index);
- its own Realm Exchange listings, at the posted price: "buy" takes the listing back off the Exchange into its bag for you.

It never sells what it wears, what it keeps for its own work (materials, a backup weapon, a piece made for someone's order).
Ask "what are you selling?" by tell to get the list without opening a window.

**What it buys.** Put a piece of yours in the window. The bot says in the system window what it is worth to it, or why it
does not want it:

- it wants a piece that fills an empty slot or beats what it wears by its set score (the same test as every purchase it
  makes), or one that answers one of its own open craft orders (that order is called off and its coin pays you);
- it bids the market's buying price (a little more for an empty slot), and never more than **80% of its purse**: short of
  that it offers what it can spare and says so ("pays you 30 silver, all it can spare (worth 45 silver to it)");
- it refuses anything it cannot use or that is no better than what it wears, with the reason. A refused piece must come out
  before it accepts: it never takes anything for free.

What it buys it puts on at once (through the guard that keeps a bot from ever undressing). Some bots post their want in
`/trade`: "wtb chain chest piece lvl 25, paying up to 2g, pst".

**Swaps.** Both at once: the coin settles the difference either way (you pay it, or the bot does), or an even swap moves no
coin.

**Jobs.** Instead of a sale:

- **Repair:** put in one worn piece and tick **Repair**. A bot with the material skill at the item's craft level (as a
  player must have in a trade window) repairs it for half the smith's price; the system window gives the chance. If it fails,
  your coin comes back.
- **Spellcraft:** put in your crafted piece and the gems and tick **Combine**. A bot spellcrafter imbues them by the player's
  rules, for 10% of the gems' value (at least 1 silver a gem). It never overcharges: a piece that would go over its imbue
  points is refused, so nothing explodes.

**Accepting.** The bot accepts by itself once the terms hold ("Aldwyn has accepted the trade.", the same words as a
player's); any change takes that back. Nothing moves until you accept too, and then everything moves at once or nothing does.
Exactly what is owed is taken: coin you put in beyond it stays yours.

**A busy bot.** You may interrupt a bot hunting or travelling: it stops where it stands, faces you and trades for up to two
minutes. But a bot that is fighting (or its group is), casting, dead, or on a raid, siege or battleground duty says so
("in a fight, give me a min") and no window opens. A fight starting closes the window. Bots still sell their own things only
at their natural task breaks; a trade you start is yours.

**Commissions** (above, "Buying from crafter gamebots") come through the same window: a piece paid on delivery shows its
price, a piece paid up front shows "already paid".

**The commands** (the new client's trade window uses them; the original client has its own window for most):
`/tradewindow` (open, with the bot targeted), `list`, `buy <n>`, `put <slot>`, `take <slot>`, `coin <copper>`,
`repair on|off`, `combine on|off`, `accept`, `close`.

### In each client

- **Original client:** the game's own trade window, no client change. The bot's side shows its real pieces (shift-click or
  information on one shows its delve as for any item) and the coin it pays; your side what you drag in and the coin you put
  in; the Repair and Combine boxes ask for a job. The terms, each piece's price and the confirmations come as system-window
  lines; "&lt;name&gt; has accepted the trade." when it accepts.
- **New client (0.37c):** it had no trade window. Living Server 1.2 adds one (see the changelog for exactly what it shows): the
  title from the server, the bot's offer and yours with icons, quality, level and delve hover boxes, the coin each side puts
  in, the bot's word in green (accepted) or red (what it waits for), its last terms, its goods with a BUY button each, REPAIR,
  COMBINE, ACCEPT and CANCEL. Drag items from your backpack onto the window; click one of yours to take it out; type gold,
  silver and copper and press SET. It works through `/tradewindow`, as the 0.37c network client cannot send the original's
  trade packet.

### Guilds and crafters (bot_crafting_social)

- A Crafting-charter bot guild takes the realm's crafter gamebots first when it places new members.
- A gamebot that commissions a piece asks a crafter of its own guild first and pays it the materials only; a Crafting-charter
  bot guild's bank pays the crafter the fee it waived.
- A Crafting-charter bot guild's bank pays for its crafters' materials while it holds ten times the price.
- A Raiding-charter bot guild's bank buys a heal draught for each raider (level 40+) at a merchant visit, up to two carried
  (also needs `bot_upkeep`, under which gamebots drink draughts at all).

## For developers

- Trading with gamebots: `craft/economy/commerce/BotTrades.cs` (the glue), `BotTradeTerms.cs` (the terms, pure),
  `BotTradeWindow.cs` (the window), `BotGuildCommerce.cs` (the guild side), `commands/playercommands/tradewindow.cs`; the new
  client's `net/server.gd` (the 0xEA packet) and `ui/merchant_window.gd` (`MerchantWindow.TradeWindow`). Log lines
  `ECON_PLAYER_TRADE`, `ECON_PLAYER_SELL`, `ECON_PLAYER_DELIVERY`, `ECON_SC_JOB`, `ECON_GUILD_SPEND`. Tests: `UT_BotTrade`,
  `tests/trade_probe.gd` (in `tools/newclient-compat/installed_check.py`).
- Code: `craft/PlayerCraftingRules.cs` (the rules, pure, and their log lines), `GamePlayer` (crafting load, gains, the
  untrained check), `CustomNPC/CraftNPC.cs` (who a master admits), `CharacterCreationCraft.cs`, `CustomNPC/Blacksmith.cs`
  (upkeep), `LootGeneratorMoney.cs` / `LootGeneratorChest.cs` (coin), `AbstractProfession.cs` and `CraftTitle.cs` (titles).
- Log lines: `CRAFT_ERA_JOIN event=start|join|join_another player=... realm=... order=... era=1` (a new crafting record
  without an order; joining an order) and `CRAFT_PLAYER_TITLE player=... realm=... skill=... value=... primary=1|0` (a new
  title, every hundred to 1000).
- The crafting pace, the two classic options and whether a repair spends durability are the crafting core's (WP1:
  `CraftingRate165`, `CraftRules`, `Repair.ModifyConditionAndDurability`); the smith's price, the caps and era start are
  here, and so are the tab rows for all of them. With secondary caps on, `GamePlayer.GainCraftingSkill` clips a gain of
  several points (the pace) at the cap.
- Tests: `UT_PlayerCraftingRules` (server) and `SocialExpansionSettingsTests.CraftingRowsAreSavedReadAndChecked`
  (launcher). The bot and economy card, the economy page, the Herald's crafters and the bot purge: WP9's
  `SocialExpansionSettingsTests.EconomyRowsAreSavedReadAndChecked`, `CraftingEconomyLauncherTests`,
  `HeraldPageTests.TheHeraldListsTheTopCraftersOfEachOrder`.
- Ports: upstream OpenDAoC `7b0df1b12` (no Legendary Grandmaster) and `ad789ee4d` (no Basic Crafting title), from
  Baptiste Marie (bm01), GPL.
