# Harvest 2026-11: one ranked plan from three sweeps

Written 2026-11-24. Docs and issues only: nothing here was built, ported or run. Every item is **tests only** until it is
seen in one of the owner's PC logs (AGENTS.md).

The owner's goal: "pull every fix or feature ... that would improve our version, sans any nerfs or restrictions that
contradict the goal of our version - the most intelligent and accurate offline bots for DAoC that feel completely
alive."

## Inputs

| Sweep | What it covered | Reviewed up to |
|---|---|---|
| OpenDAoC-Core | 632 non-merge commits from our base (about `d51233cb`, 2026-03-13) to master; PR refs 53-101 | `428927b33` (2026-11-23) |
| Offline DAoC family | 51 repos and forks (48 fetched) | True165 `e2af3f2`, DividedRealms `9ef07ca` (`feature/bot-ai-trips`), DetShaft `e77f16a`, FelixGagnonB `1729e07` (`main`) and `277d68f` (`pvp-8v8-m11`) |
| DOL and others | DOLSharp, JVirant (Amtenael/Breamor), Uthgard open source, db-public, DataLoggin, craft_extract and more | DOLSharp `154e150`, JVirant `breamor` `a3a140c`, db-public `e0e6ed2`, Uthgard `0130242`, craft_extract `0f7de6c` |

The full OpenDAoC commit-by-commit ledger is `docs/upstream/OPENDAOC-LEDGER-2026-11.md` on `docs/upstream-ledger`
(PR #566); this page doesn't repeat it. Item codes below are the sweeps' own: **O-** OpenDAoC (A fixes, B features,
C fork-only), **F-/P-** offline family (F fixes, P features), **D-** DOL and others (X fixes, F features, D data).

## Status words

- **in progress**: a port package or an open issue already has it (branch or issue named).
- **to do**: filed in this harvest, issue named.
- **held for owner**: a nerf or restriction under the owner's rules; not taken until he decides.
- **skipped**: not taken, reason given.

## How it is ranked

1. Data safety and crashes first (protect player data).
2. Then what makes bots smarter and more alive: reaction, group roles, healing, CC, travel, not freezing or looping.
3. Then classic accuracy of the rules every bot and player runs on.
4. Then world data (audits before changes), then tooling.

Within a tier, smaller effort and more players affected go first. Duplicates across sweeps are merged into one row
(for example the two-singers effect bug was found by FelixGagnonB and True165; casting through walls by the DOL sweep
and in OpenDAoC's NPC rules).

## The ranked list

### Tier 1: data safety, crashes, leaks

| # | Item | Sweep refs | Key sources | Status |
|---|---|---|---|---|
| 1 | Saves marked saved before the transaction commits; no needless inventory saves | O-A2 | OpenDAoC `b49257e7f`, `4198e9bc4`, `19123fffd` | in progress, P1 `fix/1.2.3-upstream-core` |
| 2 | Memory-leak bundle (LastAttacker, attack component, effects of deleted owners, static items) | O-A3 | `08c896ac0`, `c707d057f`, `7b38e3dbd`, `d9d2b6816`, `307015f01`, `e04fb3af8`, `84f1a46a6` | in progress, P1 |
| 3 | Game loop clock drift; trade deadlock | O-A12, O-A13 | `c955cc64c`, `7b31c6910`; `662f315fd` | in progress, P1 |
| 4 | Keep capture throws before the save; bots left at the old wall height | D-X2 | DOL `0554bfb0e8b2` | in progress, P4 `fix/1.2.3-upstream-ai-world` |
| 5 | Charm left permanent | O-A5 | `c928369c4`, `f22ebbe1c` | in progress, P2 `fix/1.2.3-upstream-effects` |
| 6 | A corrupt `.nav` crashes the native loader | F9 | DividedRealms `a5f19f5c` | to do, #574 (unless P5 takes it) |
| 7 | Crafting master primary-skill change never saved | O-A10 | `20a563b05` | in progress, P3 `fix/1.2.3-upstream-combat` |

### Tier 2: bots that think and feel alive

| # | Item | Sweep refs | Key sources | Status |
|---|---|---|---|---|
| 8 | Two singers: a replaced effect copy takes its bonus twice (endurance stuck at 0) | F1 | Felix `471b04186`; True165 `3615e416d` | in progress, P5 `fix/1.2.3-fork-bot-fixes` |
| 9 | No kill stealing: mob claims between bot groups | P1 | True165 `e4d2498ca` | in progress, P5 |
| 10 | Event wake-ups (adds, low groupmates, enemy casts) | P4 | True165 `8abbc61a8` | in progress, P5 |
| 11 | Group roles by spec (shield tank, Rejuv healer, one CC) | P5 | True165 `d2fc3fe3d` | in progress, P5 |
| 12 | Grouped bots freeze after a door pass or `MoveTo` | F4 | DetShaft `a303aefaf` | in progress, P5 |
| 13 | Performers drop songs and chants when hit | F5 | True165 `48b20439`; Felix `30de39b9b`, `8eda5e1aa` | in progress, P5 |
| 14 | A party re-pulls an unreachable monster forever | F8 | DividedRealms `1d18f864`, `7e736ca8` | in progress, P5 |
| 15 | Heals lose sub-spells; double class/style proc; Doom Hammer; Vifil adds; guild rank toggles | F2, F3, F6, F7 | DividedRealms `cfad6c50`, `bebdbb71`; True165 `78526630`, `bc2440ca` | in progress, P5 |
| 16 | Mobs and bots cast through walls with no player client near (navmesh raycast) | D-X1 | ours | in progress, P4 |
| 17 | Healer triage, HoT precheck, travel cast hold, pet buffs, healer queue, Bard healer | P8, P9, P13 | True165 `1add69478`, `03dce76ee`, `b45daa93f` | to do, #567 |
| 18 | Veteran CC, utility spells (Life Transfer, Amnesia, Confusion, Nearsight), fixed cast order, CC claim cleanup, mezzed-add rule | P6, P7, P15, F12, P22 | True165 `a95ef07c4`, `edcfb0f4d`, `66f782d8d`, `5a96d4b`, `19eef1d`; DetShaft `abd63c964`; DividedRealms `9e18aa53` | to do, #568 |
| 19 | True165 party play never taken: main assist, party pull turn, danger retreat, meetups, service clearance, weapon sets, Critical Shot opener, spec budget | True165 files | True165 `897ddfa`, `0e0a397`, `fb7487a`, `14dcaa5`, `78a70c0`, `5fb359b`, `2b5bc12`, `27164a0`, `908fef5`, `edafa9a` | to do, #569 |
| 20 | Learned dangers and solo life: road hotspots, route-block memory, seam guard (#555), hunting ground, corpse walk, bind deaths, solo root kite, PvE stealth opener, kill tasks | P10, P11, P12, P14, P16 | True165 `a24f34987`, `feecfac1d`, `34cc91e`, `d1320e6`, `adcc77843`, `ade75cbe4`, `680d5dcfd`, `2b937ddc5`, `ede5dd326`, `1910be4e9`, `b87133f72`, `f357d8d` | to do, #570 |
| 21 | The player's group: DetShaft fight mode, leader focus, RP/XP shares with companions, DF companion paths, `/spawn` helper levels | P17, P22, F11, P25 | DetShaft `77e74f670` and seven more; DividedRealms `ad8e087c`; True165 `74334692`, `de83551a`, `1630980`, `1ed3b24` | to do, #571 (feeds #527, #486) |
| 22 | RvR and siege group AI: Felix 8v8 rules, ram seats, siege kit check, battleground waves, scouts and "inc" reports, warband assist, raid reinforcements, F13 siege fixes | P3, P18, P19, P21, F13 | Felix `bb35395c7` and more; True165 `433740c6f`, `919e9d03`, `c4ba72cb`, `27f4641`, `16284ba`, `d51171f`, `28a0642`, `18f67d7`; DividedRealms `bc40a7dd`; DetShaft `81e7f9a5a` | to do, #572 (feeds #522-#533) |
| 23 | Keep holders repair damaged doors | P2 | True165 `e332ea3cc` | in progress, #525 step 3 |
| 24 | Bot memory, emotes, party calls, period channels, banter, gear value, money ledger, guild ranks for players, loot class fit | P20, P24, True165 files | True165 `92cefbb`, `562ad09`, `389631d`, `fc2634d`, `9ac18c9`, `1edbcf5`; DividedRealms `f6f2d1c5`; DetShaft `192d75cef` | to do, #573 (feeds #479-#488) |
| 25 | Stealth rules for players and bots | (several) | | in progress, #522 |
| 26 | One force intent | | | in progress, #530 (PR #560) |
| 27 | No non-answers in chat | | | in progress, #478 |

### Tier 3: classic rules everyone runs on

| # | Item | Sweep refs | Key sources | Status |
|---|---|---|---|---|
| 28 | Group XP when a member dings mid-kill | O-A1 | `6ac049c6e` | in progress, P3 |
| 29 | Live-like spell and ranged interrupt | O-B1 | `21eda0f90`, `79bba259b` and follow-ups | in progress, P2 (owner-approved, **no switch**) |
| 30 | CC and effect stacking (stun/mez overlap, root over snare, stat debuffs, taunt, proc frequency, pulsing refresh) | O-A4 | `d352aa1c9`, `5356d4930`, `c03822a01`, `7d7ec3d41`, `b7d4dc053`, `39502cb35` | in progress, P2 |
| 31 | RA training order, speed buffs and Speed of Sound, Necromancer pet queue, turrets | O-A14, A15, A16, B10, B11, A17 | see the ledger | in progress, P2 (except `e6fe1661a`, held) |
| 32 | Damage formula slips, attack-speed bundle, evade by attacker count and 120 degrees, hand-to-hand and Reflex Attack | O-A6, B3, B7, B8 | `d0cafbd5e`, `16fa2c467`, `e142cbdb1`, `57a8584ff`, `8274b06c1`, `a36db6f43`, `50027c4f7` and more | in progress, P3 |
| 33 | NPC ladders, doors and cross-zone pathing; mob AI correctness | O-A7, A8, A9 | `9071ad689`, `b693fee89`, `799b4f507`, `40fa0f68f`, `9f3d5c6da` and more | in progress, P4 |
| 34 | Guards and NPC casters finish casts; guards aggro detected stealthers and riders (GameBots exempt) | O-B4, B5 | `cbe604ce1`, `978b991fc`, `0abde7b61`, `99c97fbd9` | in progress, P4 |
| 35 | Siege fixes; keep side-door pass-through distance | O-A11, D-X3 | `d79a497e6`, `5b60e16ab`, `2fecd64f0`; DOL `223917681aa7` | in progress, P4 |
| 36 | Group-mates can't see stealthed group-mates (1.65), login invulnerability, strafing misses, Distraction, Savage buffs | O-B6, B14 | `eb06c09aa`, `9a665b536` and more | in progress per the ledger (PR #566) |
| 37 | Jump in place interrupts a cast; Mag Mell release spots; quest hand-in order and quest gear; realm-neutral armor; companion follow on geometry; stable ride drops the player's pets | F10, F14, F15, F16 | True165 `c41415df`, `4b0b1ac0`, `897ce980`, `b390827f`, `ef0dcd8b`; DividedRealms `32f08331` | to do, #574 |
| 38 | Mezzed mob picks up new aggro (check only); crowded radius queries (idea) | D-X6, D-F5 | DOL `479a3ee06bb5`, `caecaf720b06`, `5f3a8edabc9b` | to do, #574 |
| 39 | Gravestones that survive a restart; quest-list locking | O-B12, A17 | `25c8e9c5e`, `22197f1f6` | in progress, ledger issue #565 |
| 40 | Group minimap and group window; 1.127 client display and chat | O-B13 | `f3943156a` and follow-ups | in progress, ledger issue #564 |
| 41 | Player and GM command fixes; housing, guild and market | ledger | | in progress, ledger issues #563, #562 |
| 42 | Joe Judge PR #101 messages; Josh L PRs #76, #83 (perf) | O-C | `35a6b306`, `50f1536e`, `ce910de1`, `0a396dc4` | per the ledger (PR #566) |

### Tier 4: world data (audit first) and tooling

| # | Item | Sweep refs | Key sources | Status |
|---|---|---|---|---|
| 43 | Geometry line of sight for mobs and bots, `/checklos` | D-F1, D-F6 | JVirant `30d3c56c1e08`, `5afb31ea3291`, `9c3ee382478b`, `c5911661178d`, `ff94d2417479` | to do, #581 (needs the PC) |
| 44 | Keep patrol paths | D-X5 | db-public `e67704598b10` | to do, #577 |
| 45 | Charm body types; old archery spec levels | D-X4, D-D3 | db-public `764bda56fee9`, `d462a86` | to do, #579 |
| 46 | Classic starter quests (96 DOL scripts to sort) | D-F3 | DOLSharp `154e150`, `GameServerScripts/quests/` | to do, #578 |
| 47 | Period spawn, merchant and item audit from live logs | D-D1 | DataLoggin DB v1.2 (2004-03-22) | to do, #576 (audit only) |
| 48 | Period loot and merchants: True165 rows, Thidranki, Uth-Moras audit | P23, D-D2, D-D5 | True165 `c52f109a`, `46d1a00d`, `cdd9515e`, `c41415df`; db-public `7141a3249aff` and two more | to do, #575 |
| 49 | Client recipe check | D-F4 | craft_extract `0f7de6c` | to do, #580 (separate tool) |
| 50 | Single-zone navmesh rebuild and `.obj` export | D-F2 | Uthgard `0130242` `pathing/buildnav`; DigitalBox98 `8c4f9ca`..`8c1aeb2`; Shursan `Origins` | to do, #582 (needs the PC) |

### Held for the owner

| Item | Why | Source |
|---|---|---|
| O-B9 NPC defense and damage package, with `098878c01` (NPC out-of-combat regen 12.5% to 10%) | Weakens mobs and possibly bosses; branches on `is GameNPC` and would hit GameBots | `ecaf945d0`, `dee9ee086`, `7539cebb9`, `c3067e84d` and more |
| O-B2 power pool tested against a live server | A felt restriction for low-level casters; bots (`GameBot.cs:610`) must change with players | `7ca97273d` |
| Necromancer pet parry from dex | Lowers the pet's parry (the rest of the Necromancer pet fixes are in P2) | `e6fe1661a` |
| F13 keep-lord aggro floor and lord shield (DetShaft) | Could weaken a keep lord; the stun and stair fixes go ahead in #572 | `81e7f9a5a`, `977360900` |
| True165 `mode165/*` rules (death penalty, XP rates, coin, supplies, item wear and more; 34 files) | Change gameplay rules or overlap the owner's classic rule options; triage only, nothing filed | True165 `development-source/server/GameServer/mode165/` |
| Keep patrols versus the earlier choice not to restore True 1.65's keep guards | Only if the patrol rows overlap that choice (#577 checks) | `true165_prima_content.py` |

### Skipped

| Item | Reason |
|---|---|
| OpenDAoC AggroTable refactor `e2893f92e`, round-robin `21737045a`, ThreatStrategy `dd9659f8c` | Big engine refactors touching 23 boss files and bot threat code; only in a planned engine sync |
| OpenDAoC object-store and game-loop sharding, UDP `a981b22f7`, 1.127/1.130 RSA work | Infrastructure for modern clients |
| OpenDAoC `42024abdc` | Reverted upstream (`509c699f9`) |
| `87be7afe1` (drop the objects-in-radius cache) | Our bot code calls `ClearObjectsInRadiusCache`; measure first (ledger) |
| Wezley Singleton PR #73 zone ecology loot | Not 1.65; conflicts with real loot |
| tegstewart PR #91 `IsBot`; bedrock1977 PR #78 Heretic/Warlock/Vampiir | We have a richer model; post-1.65 classes |
| DividedRealms ToA, Master Levels, New Frontiers, Mithril store, rest XP, 1.87 regen; reap3rx action combat | Out of era |
| DividedRealms `bots_use_custom_classes` | Breaks "bots never play the custom class" |
| DetShaft need/greed roll window | Not 1.65 (its `LootClassFit` scoring is taken in #573) |
| True165 `978bdb57` (queued spell keeps its target) | Its own commit says it isn't historical |
| True165 `20f1a9be`, `407d5405`, `a9ce75f3`; NerdAnvil `a3e219835`; DividedRealms `492e035e`; DetShaft `608cb733f` | Not needed or already in ours |
| True165 `population/*` (14 files) | Our population and bot schedule work differently (schedule off by default) |
| Amtenael custom rules (GvG, stat flattening, damage model, gem loot) | Not classic |
| Warshard SVN | OSL-3.0, not GPL-compatible; not read |
| Caliburn GOAP "Mimic" bots | Ideas only; a planning layer over our brain would be its own design |

## True165 bot AI never taken

True 1.65 `main` (`e2af3f2`) has 152 server `.cs` files that aren't in our tree: 94 under `bots/` and 58 elsewhere.
All but three predate its v0.7 (`f68cc75`), the last release Living Server ported. Where they went:

- Named in earlier items: `AutonomousMobClaims`, `BotBrain.EventWake`, `BotGroupRoles`, `StatBuffBonusLedger`,
  `BotRegenBonusRepair` (P5); `KeepDoorRepair165` (#525); `PlayerStablePetCleanup` (#574).
- Healing and support: #567. Spells and CC: #568. Party play and travel: #569. Solo life and dangers: #570.
  Companions: #571. RvR and battlegrounds: #572. Memory, social, gear and economy: #573.
- Not filed: `population/*` (skipped), `mode165/*` (held), the True165 Realm Exchange UI and market files and
  `craft/CraftAssist165*` (the Realm Exchange is an owner rule and our crafting is Crafting 2.0; compare only if a gap
  shows up), `scripts/true165/PrimaEncounters165*.cs` (check against our True165 content port, TRUE165-CONTENT.md).

A missing file isn't proof of a missing feature: our code moved on. Each issue starts with a have/partial/no check.

## License notes

| Source | License | How we may use it |
|---|---|---|
| OpenDAoC-Core and its PR forks | GPL-3.0 | Port with headers kept; credit authors in the changelog and commits |
| zomboden/OfflineDAoC-True165, rambodolphin13/DividedRealms, DetShaft/OfflineDAoC-LivingServer, FelixGagnonB/OfflineDAoC, Fusseli/CaliburnWorkingBranch, reap3rx | GPL-3.0 | Port with credit ("Port of <repo> <sha>") |
| NerdAnvil PerSoPer, catastrophic-impact/daoc-linux-server-bots | No license file | Ideas only; no code copied |
| Bifrost-Technologies/Camelot-Core | No license | Not used |
| Dawn-of-Light/DOLSharp and its forks, JVirant/DOLSharp | GPL-2.0-or-later | Fine for GPL-3.0; keep headers, credit Jérémy Virant for `LosCheckMgr` |
| thekroko/uthgard-opensource, Shursan fork | GPL-3.0 | Tooling port (`buildnav`) |
| Eve-of-Darkness/db-public | MIT since `e0e6ed2` (2026-10-22) | Rows may be used; the game data inside is Mythic/Broadsword's, as with our other data |
| DOL DataLoggin (SourceForge) | No license stated | **Audit reference only**: no rows copied; corrections written from a period source |
| Kabouik/Uth-Moras | No license (ships two `.exe` files) | **Audit reference only** |
| atom0s/craft_extract | AGPL-3.0 | **Separate tool only**: run it, never copy, link or ship it; its output is client data, not committed |
| Warshard SVN | OSL-3.0 | Not GPL-compatible; not read |
| Client-derived files (`.obj` geometry, navmeshes from `buildnav`) | Client data | Never shipped; Setup or the player's PC builds them, as the navmesh rule already says |

## Watched from now on

`.github/upstream-watch.json` now also follows the forks worth watching (weekly, one issue each; see
[UPSTREAM-WATCH.md](../UPSTREAM-WATCH.md)): zomboden/OfflineDAoC-True165 `main`, rambodolphin13/DividedRealms
`feature/bot-ai-trips`, DetShaft/OfflineDAoC-LivingServer `main`, FelixGagnonB/OfflineDAoC `main` and `pvp-8v8-m11`.
Each `reviewed` is the tip this harvest read.
