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
autonomous gamebots craft and trade under these settings; your /spawn companions never do. **Tests only:** none of
the bot side has been seen in game yet.

| Setting | Default | What it does |
|---|---|---|
| Bot crafters | Off | Crafter gamebots join an order, buy real materials, work at the capital's stations and sell what they make. Off: no gamebot crafts |
| Crafters among new bots | 7% | Share of new gamebots that are Crafters; existing bots keep their type |
| Bot crafting pace | Bot XP rate | Points per gain follow the bot XP multiplier of the crafter's level; Classic: one point |
| Craft orders | Off | Gamebots pay a crafter of their realm for an upgrade, the coin held until the piece is handed over. Needs bot crafters |
| Realm commissions | Doubled | The quartermaster's orders to crafter bots: Classic 30 a realm an hour, Doubled 60, Off |
| A crafter's Exchange posts | 6 | Realm Exchange posts a crafter bot may hold for its own work (3 to 100; other bots hold 3) |
| Crafters' chat | 1× | Crafters' trade and station chat, 0 (silent) to 3 |
| Realm Exchange tax | 0% | Kept from the seller's proceeds of each Exchange sale, yours and the bots' (0 to 50; 5 is the classic option) |
| Economy ledger | On | Counts each realm's coin, faucets and sinks once an hour (log lines and one row an hour). Changes no amount |
| Level-50 kit and gold | On | "+ Lv.50" bots arrive with the quality-100 kit and 10,000 gold. Off (a fresh-start world): no kit, a 25-gold purse, and plain merchant gear for their level from the server at their first login |

**ECONOMY PAGE** writes and opens `runtime\herald\economy.html`: each realm's money supply, the last 24 recorded hours
with faucets, sinks, the Exchange tax and the residual, the biggest sinks, the flows by kind and the Exchange's price
index, all as the ledger records them ([ECONOMY-LEDGER.md](ECONOMY-LEDGER.md)). The Camelot Herald links to it and lists
the top five crafters of each order. ALL OFF switches bot crafters and craft orders off; it leaves the tax, the ledger
and the kit as they are.

Server keys: `bot_crafters`, `bot_crafter_share_percent`, `bot_crafting_pace` (xp / classic), `bot_craft_orders`,
`realm_commissions` (classic / doubled / off), `bot_crafter_exchange_listings`, `bot_crafter_chat_rate`,
`exchange_tax_percent`, `economy_ledger_enabled`, `launcher_level50_kit` (read by the launcher).

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

## For developers

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
