# DAoC - Living Server quick commands

These are the everyday commands, not the exhaustive list of every
inherited game/server command. They are registered for normal players in this
release; some still have level, target, party or state requirements.

## Launcher bot generation

Click **+ Lv.1**, **+ Lv.20** or **+ Lv.50** under a faction to create bots for that faction.
**+ Lv.20** makes bots at a random level from 20 to 24, the Thidranki battleground bracket; they go
there for the **Battlegrounds %** share set for Levels 20–49 on the Bot Goals Setting tab. At their first
login the server gives them plain merchant armour and weapons for their level, free; **+ Lv.1** bots still earn theirs.
Hold **Ctrl** for **+100 bots** per click, or **Shift** for **+10**. Without a
modifier, create one bot. Start small and increase population for your PC's capacity.

## Travel and finding mobs

| Command | What it does |
|---|---|
| `/tele X` | Teleport to a gamebot; replace X with its name. You can also right-click bot names in the launcher to teleport. |
| `/mobs X` | List mob names at a level; replace X with the level number. |
| `/tele mob X` | Teleport to a mob spawn; use the mob's exact name for X. Dungeon targets use the configured entrance approach where applicable. |
| `/tc` | Teleport to your realm's Realm Exchange NPC. |
| `/stables` | List every stablemaster horse route in your realm, one page at a time, including the neutral dragonfly, gryphon and wyvern routes. `/stables X` shows page X. |
| `/stables classic` | Jump to your realm's Classic stable routes. |
| `/stables si` | Jump to your realm's Shrouded Isles stable routes. |

## Helper groups, grinding, and raids

| Command | What it does |
|---|---|
| `/grind` | Start automated grinding with a helper group, including for AFK use. |
| `/pull` | Order your helper group and pets to engage your selected enemy. When possible, a tank makes first contact before the rest of the group joins the fight. |
| `/train <line> <level>` | Train a specialization to the chosen level using your available specialization points. Select a valid trainer for your class first. |
| `/spawn` | Open the menu of valid helpers to summon. |
| `/spawn X` | Summon a helper by class name instead of using the menu; useful for macros. Its build (spec) is random. |
| `/spawn specs` | Open a menu of every class of your realm with its builds, for example Healer (Mending), Healer (Augmentation), Healer (Pacification). Each build is named after the spec line it trains highest. |
| `/spawn X Y` | Summon class X with spec Y, written in full as `/spawn specs` shows it, for example `/spawn healer augmentation`; useful for macros (`/macro aug /spawn healer augmentation`). |
| `/raid 40` | Enable a 40-member helper raid. Use **before** `/spawn`. Requires level 50; the total includes you. |
| `/raid 80` | Enable an 80-member helper raid. Use **before** `/spawn`. Requires level 50; the total includes you. |
| `/aggressive` | Helpers and companions automatically engage what you attack, while retaining defensive reactions. |
| `/defensive` | Helpers and companions stay near you and engage nearby threats, letting you pull mobs back to the group; useful for harder content. |
| `/hold` | Every bot in your group (helpers, companions and gamebots grouped with you) only fights back what attacks it: no new fights, no assisting, no pulls. Healers keep healing. For sneaking past a fight or waiting at a keep. `/aggressive` or `/defensive` ends it. |
| `/order attack`, `/order assist`, `/order hold`, `/order regroup` | Order your group in a fight; a macro works too (`/macro Atk /order attack`), and so does the word alone in `/group`. **attack**: everyone goes for your selected target now. **assist**: they fight only your fight and switch when you switch. **hold**: as `/hold`, until the fight ends. **regroup**: they break off and come back to you, and defend you and the group once back. An order lasts until your next order or the end of the fight; then your stance from before (`/defensive`, `/aggressive`, `/hold`) comes back. `/order` alone lists them. |

`/spawn 40` and `/spawn 80` are **not** the raid-size commands. Use `/raid` first.

Helpers you `/spawn` level up with you while they are in your group. After you ding, each one catches up to your level once it is out of combat, with its specializations, spells, styles, equipment and pet brought up to date, so you don't need to respawn them. They never level down.

The aggressive/defensive commands control your /spawn helpers and hired companions, not gamebots.
The `/raid` command does not implement Darkness Falls raid AI. In the v0.32/
v0.32b Darkness Falls Beta, Legion, the hardest level-70+ encounters,
unreachable flying targets, and unverified routes are excluded from ordinary
autonomous bot goals.

For the complete advanced reference, see **ALL SERVER COMMANDS.txt** inside the
download. That file separates normal-player, GM and administrator registrations.
