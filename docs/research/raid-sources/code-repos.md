# Raid logic, boss mechanics and encounter data in other code repositories

Research notes, 2026-11-14. Nothing here is implemented. Nothing here was tried in game.

- **Our baseline:** `chris4aker/offlinedaoc-livingserver`, `origin/integration/1.2` at `a0402d44e`. Our boss scripts are
  in `source/server/GameServer/scripts/namedmobs/`, and "ours" below means a path under it.
- **Upstream baseline:** `OpenDAoC/OpenDAoC-Core` `master` at `446a99f1f` (2026-11-12). "Upstream" means
  `GameServer/scripts/namedmobs/` in that tree.
- **Method:**
  - I cloned every repo below with `--filter=blob:none` and compared git blob hashes against every version of our
    namedmobs and bot files in our history, which is 6,791 blobs.
  - I diffed the files that came out different.
  - For upstream I checked each commit after our base: do its `+` lines appear in our current file?
- **API access:** GitHub repo-level API calls are blocked in this session (403 "not enabled"). Search and anonymous
  `git clone` work, so last activity below comes from `git log` of the clone.
- **Our fork point:** our first import (`4d8b513f7`, 2026-09-15) matches upstream `f7b226657` (2026-02-27) most
  closely. That is 1,925 differing lines over all namedmobs. Every other upstream commit differs by more: 2,097 to
  15,697 lines.
- **So our boss scripts are about 8 months behind upstream.** None of upstream's June–Aug 2026 boss-script work is in
  our tree. Checked per commit; see the "Upstream commits after our base" table.

---

## 1. Per repo

### 1.1 OpenDAoC/OpenDAoC-Core (upstream, and also the Atlas source)
- **URL:** https://github.com/OpenDAoC/OpenDAoC-Core
- **License:** GPL-3.0, the same as ours. Copying code is fine; keep the headers.
- **Last activity:** 2026-11-12.
- **Atlas:** there is no separate Atlas repo. OpenDAoC-Core *is* the open-sourced Atlas freeshard code. Its namedmobs
  history starts 2021-12. 627 of the 959 namedmobs commits are by "Glimmer Warshade", who wrote the Atlas boss
  scripts.
- **Bots:** none. It has no bot or raid AI; everything useful is boss-side.

**Upstream commits after our base that touch boss scripts.** None are in our tree.

| Commit | Date | What | Bosses touched |
|---|---|---|---|
| `0b581e3fb` | 08-04 | `Die()` → `ProcessDeath()` plus despawn safety: adds and objects are removed when the boss dies | Olcasgean (1,300-line rewrite), Morgana, Lord Sanguis, Aroon, Olcasar, Broodmother, Green Maw, Blight, Myrddraxis, Scurceol, dragons, Evern, Legion, Beliathan, Nahemah, Aros, about 90 files |
| `12872751a` | 08-04 | `WalkTo` → `PathTo` (navmesh-routed scripted moves) | Apocalypse, Easmarach, Green Knight, Govannon, Steinvor/Skuf |
| `ac841e9f1` | 08-04 | Patrol bosses restart their loop path when combat stops it (`if (!Body.IsMovingOnPath) Body.MoveOnPath(..)`) | Skeletal Sacristan, Spectral Provisioner |
| `c2a55c5b9`, `cadc7b8c0`, `9b86b7401`, `0fbdbcb1a`, `f13f17667`, `abb420a99`, `dfad601d2` | 08-04 | Path cleanup: chains of `WalkTo` flags replaced by `MovementMgr.CreatePath(EPathType.Loop, speed, points[])` | Crypt Lord, Host (52 `WalkTo` → 1 loop), Sacristan, Provisioner, Torst/Hurika, Oghamist, Xaga |
| `fca5900d7` / `403d084dc` / `66dbe4220` | 08-07 | "Shared variable safety": per-encounter `public static` state → instance fields, adds hold an `OwnerBrain`, navmesh-safe add spawn points, adds removed on boss death | 94 files, including Legion, Apocalypse, Morgana, Govannon and the summoners, Green Knight, Tuscar/Kula, Hjalmar/Suttung, Aroon, Myrddraxis, Glacier Giant, Evern |
| `6f8b6cf49` | 08-07 | Encounter gating: `EncounterGateAdd` and `EncounterKillCounter` (kill N adds to spawn the named) | Throatripper, Queen Major, Snarls, Stripe, Zrit-Zrit (`scripts/mobs`) |
| `da1990cea` | 08-31 | Legion `ThrowPlayer`: drops a redundant HasAggro check | Legion |
| `c0476c41b` | 07-29 | **Host initializer: `System.Timers.Timer` (thread pool, off the game loop, 1 s) → `ECSGameTimer` (10 s)** | Host |
| `b3b9a24f7` | 07-05 | Dragons return to spawn through `FSM.SetCurrentState(RETURN_TO_SPAWN)`; the roam path ends at `SpawnPoint`; the `SpawnHeading` accessor is fixed | 3 dragons |
| `9ba38be6a` | 06-25 | Torst/Eddies NRE | Torst |
| `e2893f92e` | 07-29 | Engine: `AggroTable` with aggro strategies | 23 boss files call the new API. **Engine dependency, don't cherry-pick.** |
| `9f3d5c6da`, `903b608bf`, `21737045a` | 06–07 | Engine: return-to-spawn, FSM, aggro round-robin | Aros, Evern, Uaimh. Engine dependency. |

**Unmerged upstream branches.** Fetch with `git fetch origin fen/raid-encounters`.
- **`fen/raid-encounters`** (`68144888d`, 2026-09-01, by Fenyn). The most relevant code found anywhere. It is +3,844 / −4,763 lines over 37
  files:
  - `GameServer/gameutils/RaidEncounter.cs` (722 lines):
    - The roster is snapshotted at pull (keyed by character InternalID, so people who reconnect stay on it).
    - HP multiplier: `1 + 0.10 × (roster − 8)`, with the roster clamped to 8–80.
    - Adds scale with `ScaleUnitCount(playersPerUnit, maxCount)`.
    - AF scales with an activity window of 30 s, counting anyone who attacked, healed, buffed or debuffed. The
      expected active fraction is 0.75.
    - Bonus loot rolls are added per 5 roster members above the baseline.
    - Outsiders are locked out of helping (`IsAllowedToHelp`), and outsiders' buffs are stripped from the roster.
    - Participants who quit stay eligible for 20 minutes.
  - `GameServer/gameutils/EpicNpcArmorFactor.cs` is the single seam for the legacy per-attacker AF formula:
    `1.6 − 0.04·players − 0.01·min(pets, cap)`, never below 0.4.
  - `GameServer/serverproperty/ServerProperties.cs` adds 9 `raid_scaling_*` properties.
  - The three dragons collapse into a shared `Dragons/GameDragon.cs`, `DragonBrain.cs` (583 lines),
    `DragonConfig.cs` and `DragonMinions.cs`.
  - `DragonBrain` drives every ability with a small `TimedAbility { RollCooldown, CanFire, Execute, NextFireAt }`
    record.
  - **Breath:**
    - It fires once per 10 % HP band, 9 bands in all.
    - It picks an "anchor" player and telegraphs it: "fixes its deadly gaze upon YOU! Lead its breath away from your
      allies!".
    - It casts as a 2,000-unit, 120° cone, then a resist debuff 6.1 s later.
  - **The other abilities:**
    - Glare has a 40–60 s cooldown and is telegraphed.
    - Stun works only at ≤75 % HP, on a 120–180 s cooldown.
    - Throw has a 60–80 s cooldown. It **moves the victim's pets with them (`MovePetsWithPlayer`)** and drops the
      victim from the aggro list.
    - The messenger wave fires at ≤50 % HP on an 80–90 s cooldown, and its size scales with the roster.
    - Night roam happens once per game-day, from 21:00 to 23:00.
  - `Tests/IntegrationTests/IT_DragonBrain.cs` (469 lines) has a full integration-test harness (`IT_Harness`,
    `TestWorld`, `TestGameServer`) with 12 behaviour tests. Use it as an executable spec.
  - Test files: `UT_RaidEncounterScaling.cs` and `UT_EpicNpcArmorFactor.cs`.
  - `GameServer/commands/gmcommands/EncounterCommand.cs` adds a GM `/encounter` command.
- **`fen/shared-variable-safety` and `fen/scripted-mob-cleanup2`:** the branches behind the August master commits
  above. `scripted-mob-cleanup2` also has `d832cde95` "Add path creation helper" (`MovementMgr.CreatePath`, merged;
  upstream `GameServer/gameutils/MovementMgr.cs:183`).
- **`fen/refactor-attempt`** (2025-06): an SRD/rules documentation dump. Not useful.
- **bm01/OpenDAoC-Core** (Baptiste Marie's fork): 69 commits behind master, nothing new.

### 1.2 OpenDAoC/OpenDAoC-Database
- **URL:** https://github.com/OpenDAoC/OpenDAoC-Database
- **License:** GPL-3.0, changed from AGPL on 2026-09-29.
- **Last activity:** 2026-11-01.
- **Contents:** SQL dumps (mob, npctemplate, spell, loottemplate, area, door). Recent commits are doors, housing, spell
  fixes and "Parthanan farms". No boss-spawn changes since mid-2026.
- **Usefulness:** a source of truth if one of our boss rows or spells looks wrong.

### 1.3 Dawn-of-Light/DOLSharp
- **URL:** https://github.com/Dawn-of-Light/DOLSharp
- **License:** GPL-2.0-or-later. The file headers say "or (at your option) any later version", so it is GPL-3.0
  compatible.
- **Last activity:** 2026-11-01.
- **Contents:** no namedmobs, no Darkness Falls, no Legion, no epic dungeons. The only raid content is the 2008
  dragons by "Aredhel":
  - `GameServer/gameobjects/Dragons/GameDragon.cs` (858 lines)
  - `Golestandt.cs`, `Cuuldurach.cs`, `Gjalpinulva.cs`
  - `GameServer/ai/brain/DragonBrain.cs` (371 lines)
- **Ideas we lack:**
  - `DragonDifficulty` (a % of full strength).
  - Encounter "stage" tracking: 10 HP stages, so a dragon that regains a little health does not repeat its PBAoE or
    adds (DragonBrain ~L230).
  - Timed adds with an uptime and a "retriever" flag: adds that run to a spot and call `OnRetrieverArrived`.
- **Usefulness:** otherwise weaker than our Atlas dragons.
- Other Dawn-of-Light repos are tools (QuestDesigner, DOLToolbox, and so on).

### 1.4 zomboden/OfflineDAoC-True165 (shadowofze's moved True 1.65 repo)
- **URL:** https://github.com/zomboden/OfflineDAoC-True165. Also mirrored as `avalyra/…`, an older copy is
  `FriendMaple/…`, and our `ls165` remote is the same lineage.
- **License:** GPL-3.0.
- **Last activity:** 2026-11-13 (`f68cc75`).
- **Shared code:** the same bot lineage as ours. `bots/RaidBossTraits.cs` and the raid route code are shared.
- **Boss scripts:** 98 namedmobs files differ from every version we ever had. Most of the difference is the older
  player-only targeting (`GamePlayer`, `PrivLevel == 1`) where we moved to `EncounterTargets`, plus True-1.65
  period gates. A sibling, not a source of boss mechanics.
- **What it has that we don't:**
  - **`development-source/server/GameServer/bots/autonomous/AutonomousRealmRaid.Reinforcements.cs`:**
    - Released and late raiders gather first, then go in as a wave of 8 (60 s short wait, 180 s maximum, 600 gather
      radius).
    - **It also covers dragons:** a raider farther than 6,700 from the lair counts as "not with the raid". Our
      `RealmRaidReturnWave.cs` covers dungeons only.
  - **Raid end rules** (CHANGELOG "Horn raids"):
    - The rally clock is 160, 200 or 300 s by size.
    - The raid is called off after 3 wipes on the final encounter or a 1-hour stall.
    - Players can call it off: `/horn calloff`.
    - Every boss gets a muster.
  - **Tests:** `UT_RealmRaidRouteFixes.cs`, `UT_RealmRaidTravelFixes.cs`, `UT_HornRaidLifecycle.cs`,
    `UT_AutonomousMobClaimsRaidSide.cs`.
  - **`bots/autonomous/KeepRaidDoors.cs`:** RvR keeps, not PvE.
  - **`tools/claude-version/true165/prima_batch_2026-11-07/ENCOUNTER SPECS.md`** (147 KB) with
    `encounters165.json` and `build_encounters165.py`:
    - Period-sourced specs (Prima guides, Allakhazam via Wayback, Freddyshouse) for about 20 open-world named
      encounters: Cuinn's town call at 10 % with 6 treant seedlings, the Ixthiar creeper, Earl Gerald, Aine, Bodb,
      the Delling Crater forge, and others.
    - Every number is labelled PERIOD or CHOICE.
    - Not raid bosses, but **the method is the template to copy** for our big bosses.
  - **`mode165/Epics165.cs`:** epic *quest* finales, not raids.

### 1.5 rambodolphin13/DividedRealms (and catastrophic-impact/daoc-divided-linux, its Linux port)
- **URLs:** https://github.com/rambodolphin13/DividedRealms and https://github.com/catastrophic-impact/daoc-divided-linux
- **License:** GPL-3.0.
- **Last activity:** 2026-11-11 (`a0555cd`, tag `DR-dev-0.1g`). The Linux port's last activity is 2026-11-12.
- **Lineage:** a fork of *our* Living Server code, from around 2026-11-07. 19 `EpicEncounterTactics*` files; many are
  identical to past versions of ours. What it added on its own:
  - **`source/server/GameServer/bots/autonomous/RealmRaidDungeonRoute.cs`:**
    - `ProbeBudgetMilliseconds = 100`. It came out of an overnight 4,500-bot run where the next-encounter search
      stalled a tick for 7.7 s. Now the search stops after 100 ms and resumes from the same candidate and anchor
      next pass (`_probeResumeNpc` / `_probeResumeAnchor`).
    - `ApproachDungeonConnects(nav, outer, inner)` proves a walk through an approach dungeon before routing.
      Summoner's Hall through Hall of the Corrupt (region 277) had no walk; 146 raiders were held for 72 minutes.
  - **`AutonomousRealmRaid.cs`:**
    - `QueueDungeonExits` / `TryTakeDungeonExit` / `DungeonExitWindowMilliseconds = 10 min`: raiders left inside a
      dungeon when the raid ends are taken to the hub. 128 raiders had sat in Summoner's Hall.
    - `IsInRaidParty(bot)`.
  - We have none of `ProbeBudget`, `ApproachDungeonConnects`, `DungeonExit*` or `IsInRaidParty` (checked with
    `git grep` on our branch).
  - **Tests:**
    - `Tests/UnitTests/UT_RaidRouteRules.cs`: progress on a self-healing boss — a new lowest HP, or damage after a full
      heal, counts as progress. Also "a raid with no target turns on the named attacking it".
    - `UT_RaidStrandedRelease.cs`.
  - **Dragons (`scripts/namedmobs/Dragons/AlbGolestandt.cs`, `HibCuuldurach.cs`, `MidGjalpinulva.cs`):**
    - `DragonLairPlacement.Adopt(realm, row.X, row.Y, row.Z)` takes the lair from the DB row.
    - Add models and sizes: granite giant stonelord 616 (150–170), pounder 615 (130–150), outlooker 615 (130–140);
      glimmer knight 620, deathwatcher 619, geist 618 (50–55).
  - **`Host.cs`:** the same `ECSGameTimer` fix as upstream `c0476c41b`.
  - **`Fomor/Krackenschtein.cs`:** port-list cleanup (`RemoveAll(dead / other region / >2000)`).
  - The rest is the Pendragon custom class, Atlantean Glass loot and ToA artifact encounters. Not raid.

### 1.6 stefanrows/OfflineDAoC
- **URL:** https://github.com/stefanrows/OfflineDAoC
- **License:** GPL-3.0.
- **Last activity:** `main` 2026-10-26; tag `v0.235.0`. Branches include `codex/camlann-tier*` and
  `rvr-group-doctrine`. It is a full-PvP world.
- **What it has:**
  - **`source/server/GameServer/bots/autonomous/RealmRaidBattleGroup.cs`:** puts a whole raid in one `BattleGroup` so
    parties don't fight each other (PvP world). Not needed in our three-realm world.
  - **`AutonomousRaidSchedule.cs`:** a raid calendar. A raid is announced for a time, level-50 bots sign up and carry
    on, then gather. Too few sign-ups cancel it. Flavour; our raids are on-demand.
  - **`AutonomousGuildEncounterMemory.cs`:** win/loss memory between guilds (PvP).
  - **`source/server/tools/build_raid_click_fix_client.py` and `tools/dev/Install-RaidClickFix.ps1`:**
    - Fixes click-to-target in the *native* 40- and 80-member raid windows.
    - The fix writes event IDs `0x600+i` into the four raid XMLs. `game.dll` is hash-checked and never modified.
    - Useful if our player raids with bots and wants a raid window.
  - ToA artifact encounter targets/spawns SQL (`tools/toa/sql/070-*.sql`, `071-*.sql`). Not 1.65.

### 1.7 l3tal123/OfflineDAoC-BotRealm
- **URL:** https://github.com/l3tal123/OfflineDAoC-BotRealm
- **License:** GPL-3.0.
- **Last activity:** 2026-11-03.
- **Contents:** `AutonomousRaidHothead.cs` and `AutonomousDungeonBossGoalProperties.cs` are ports *of ours*
  (`AutonomousRaidLeeroy`, our dungeon boss goals). Nothing new for raids.

### 1.8 TTom03/DOLServer-DAoC-NewFrontiers
- **URL:** https://github.com/TTom03/DOLServer-DAoC-NewFrontiers
- **License:** GPL-3.0.
- **Last activity:** 2026-03-01. It is an OpenDAoC fork at about `f7b226657`, the same as our base.
- **35 boss files changed:**
  - Freeshard tweaks: currency drops on kill (Legion 500 seals, Beliathan 100, Golestandt 500 scales, Govannon 300
    grimoire pages).
  - Respawn jitter ±20 % (Legion 4 h, Oro 1 h).
  - `DungeonTeleporter.Create(this)` on Tuscar.
  - Golestandt registers an `Area.Circle("Golestandt's Lair", …, LairRadius)` (`Dragons/AlbGolestandt.cs`).
  - `GameServer/gameobjects/GameEpicUkobat.cs` adds a new epic NPC kind.
- **Mechanics:** none.

### 1.9 Other Offline DAoC forks checked: nothing raid-related of their own
Each of these has no namedmobs change and no raid file we lack:
- Nikse77/OfflineDAoC: frozen 2026-11-05. It continues in `Nikse77/OfflineDAoC-Crown`, which is private or
  unreachable.
- catastrophic-impact/daoc-linux-server-bots: 2026-10-27, no license file.
- trozzelle/OfflineDAoC-macOS-server: GPL-3.0.
- reap3rx/offline-daoc-remastered: GPL-3.0. Its `CompanionRaid*` files are older shared code.
- ChristianHomrighausen/OfflineDAoC
- 007berthold/OfflineDAoC
- PikkonMG/OfflineDAoC
- Alaarik/OfflineDAoC-Hidden_Realms: it adds epic *armor quests* only.
- FriendMaple/OfflineDAoC-True165

These names from the task did not resolve:
- `HearthDAoC`, `shadowofze/OfflineDAoC` and `Cancain/OfflineDAoC` are not reachable anonymously (private, moved or
  gone).
- `ryan-grafana/offline-daoc-linux` holds Wine scripts only.

### 1.10 Non-DAoC bot frameworks (design only)
- **mod-playerbots/mod-playerbots** (AzerothCore, https://github.com/mod-playerbots/mod-playerbots)
  - License: GPL-2.0-or-later. It is C++, so take ideas only.
  - Last activity: 2026-11-13.
  - Per-raid folders (`src/Ai/Raid/<Raid>/`): `*Strategy.cpp`, `*Triggers.cpp`, `*Actions.cpp`, `*Multipliers.cpp`,
    and sometimes `*Helpers` or `*BossHelper`. There are about 25 raids.
  - **A strategy is a list of `TriggerNode(trigger, {NextAction(name, priority)})` entries.** Examples
    (`src/Ai/Raid/Naxx/NaxxStrategy.cpp`):
    - "mutating injection melee" → "grobbulus move away" (ACTION_RAID+2)
    - "heigan melee" → "heigan dance melee"
  - **Multipliers** (`NaxxMultipliers.cpp`) **veto generic actions during a mechanic**. They return 0.0 for
    `AvoidAoeAction` on the main tank, or block movement and casting while dancing. This is the piece we lack.
  - Onyxia (`src/Ai/Raid/Ony/`) is the closest analogue to a DAoC dragon:
    - "ony near tail" → move to side (90° off facing)
    - "deep breath warning" → move to the nearest of the fixed safe zones (ACTION_EMERGENCY+5)
    - "fireball splash" → spread 9 yd
    - "whelps spawn" → kill whelps (it doesn't swap if already on one)
  - `src/Ai/Base/Actions/WipeAction.cpp` handles wipe recovery.
- **cmangos/playerbots** (https://github.com/cmangos/playerbots)
  - No license file. Ideas only.
  - The same trigger/action/multiplier model, with generic strategies under `playerbot/strategy/`.
- **nickelm/irish-sea** (https://github.com/nickelm/irish-sea)
  - No license. Ideas only.
  - An OpenDAoC bot framework built on weighted "facets", a shared blackboard and superposed vector fields
    (`programs/lib/fields/avoid_train.json`, `keep_range.json`, `funnel.json`).
  - Tunables live in JSON with hot reload. Movement-by-fields is a clean way to express "stand in the safe ring, away
    from the cone, near your party".

---

## 2. Per boss: what other code does differently from ours

"Static" means per-class `public static` encounter state. It leaks across resets, respawns and the two copies of an
encounter: a reset that misses one flag leaves the boss stuck or unspawning. Repeated bot attempts hit exactly that
case. Static counts are from `grep 'public static (bool|int|GamePlayer|GameLiving|long)'`, ours vs upstream:

| Ours file | statics ours/up | `Die` overrides ours/up | `WalkTo` ours/up |
|---|---|---|---|
| DarknessFalls/Legion.cs | 24/0 | 1/0 | 0/0 |
| CaerSidi/ApocalypseAnd4Horsemans .cs | 48/0 | 8/2 | 2/0 |
| CaerSidi/Host.cs | 59/0 | 1/0 | 52/0 |
| Galladoria/OlcasgeanAndPrimals.cs | 33/0 | 8/2 | 17/0 |
| Galladoria/Aroon.cs | 29/0 | 9/9 | 0/0 |
| Fomor/Myrddraxis.cs | 25/0 | 5/0 | 0/0 |
| Dragons/AlbGolestandt.cs (and the other two) | 24/24 | 1/0 | 26/25 |
| OFGreenKnight.cs | 23/2 | 0/0 | 4/0 |
| TuscarianGlacier/KingTuscarAndQueenKula.cs | 20/10 | 2/0 | 0/0 |
| TuscarianGlacier/ElderIcelordHjalmarAndSuttung.cs | 13/4 | 2/0 | 0/0 |
| Galladoria/Xaga.cs | 11/0 | 3/0 | 8/0 |
| Albion…/MorganaBechardSilcharde.cs | 9/0 | 3/0 | 0/0 |
| SummonersHall/GrandSummonerGovannon.cs | 8/0 | 4/2 | 2/0 |
| SummonersHall/SummonerLossren.cs | 8/0 | 2/0 | 0/0 |

The dragons are 24/24 because upstream master kept their statics. The `fen/raid-encounters` branch replaces them
with `DragonBrain`.

**Legion and Behemoth (Darkness Falls).**
- **Add bands:**
  - Ours: 19 static flags, `Legion.cs:312-338`.
  - Upstream: `bool[19] addsSpawned` plus a loop, `Legion.cs:308,359-367`.
- **Head count:**
  - Ours: a static head count (`PlayerCountInLegionLair`), reset 19 times (`Legion.cs:418-453`).
  - Upstream: a local in `SpawnAdds` (`Legion.cs:424-438`).
- **Throw:** upstream `da1990cea` removes a HasAggro check from `ThrowPlayer`.
- **Ours is ahead:**
  - It counts bots (`EncounterTargets.InRadius`) and caps through `EncounterFloodGuard.Cap("Legion.Adds", …)`.
  - It throws through `DragonCombatGeometry.ThrowPlayer`.
  - It has the blast lock-out as brain state.
  - `RaidConsumesBladeturn` is in `bots/RaidBossTraits.cs`.
- **Behemoth:** the same class in all repos (`Legion.cs:824-` in ours). No repo has more Behemoth mechanics than
  ours. No repo anywhere has Darkness Falls raid AI.

**Apocalypse and the Horsemen (Caer Sidi).**
- Upstream `66dbe4220`: 48 statics → 0, over 290 lines.
- Upstream `12872751a`: the walk-in uses `PathTo`.
- Upstream `403d084dc` adds add cleanup.
- Upstream still uses `System.Timers.Timer` in the initializer (upstream `ApocalypseAnd4Horsemans .cs:41`). Ours no
  longer does.
- True165 is the same as ours apart from player-only targeting.

**Host and Crypt Lord (Caer Sidi).**
- **Host timer:** ours `Host.cs:25-26` still runs `new System.Timers.Timer()` with `ElapsedEventHandler`. That is a
  thread-pool callback every 1 s mutating encounter statics off the game loop. Upstream `c0476c41b` changes it to
  `ECSGameTimer` returning 10,000 ms (upstream `Host.cs`); DividedRealms has the same fix.
- **Host patrol:** upstream `cadc7b8c0` replaces 52 `WalkTo` stages with one
  `MovementMgr.CreatePath(EPathType.Loop, PATROL_SPEED, _patrolPoints)` (upstream `Host.cs:228`).
- **Crypt Lord:** gets the same path cleanup (`c2a55c5b9`).
- **Sacristan and Provisioner patrols** (upstream `SkeletalSacristan.cs:102`, `SpectralProvisioner.cs:166`):
  - They resume the loop with `MoveOnPath` after combat.
  - The waypoint arrays are in the file, so our `EpicEncounterTactics.Patrols.cs` could read fixed coordinates
    instead of inferring them.

**Olcasgean (Galladoria).**
- **Upstream rewrite** (`0b581e3fb`, `OlcasgeanAndPrimals.cs`):
  - The initializer owns `Boss`/`EventStarted` (`:91`, `:465`).
  - The primal death counter is per instance, through `OnPrimalDied()` (`:330`).
  - **Both bodies share one health pool** (`:370`, `:834`).
  - The Air primal endlessly circles the room on a loop path even in combat, restarting it when interrupted
    (`:1014-1019`, `:1200`).
  - On death it removes Vortex and WaterfallAntipass.
- **Ours** (`OlcasgeanAndPrimals.cs:66-67,561-574,1301-1311`):
  - Same mechanics, but static `DeadPrimalsCount`, `startevent`, `Spawn_Copy` and `path1..path11`.
  - The copy's health is synced at `:378` and `:924`.
  - Ours adds bot hooks: `BotTriggerRadius` and the `EpicEncounterMechanics.AllowPositionPuzzle` gates.
- **Take:** the state model and the loop-path primal, keeping our hooks.

**Morgana, Bechard and Silcharde (Salisbury).**
- **Upstream** (`MorganaBechardSilcharde.cs:78,300,338,486,524`):
  - Kill counts are per brain (`BechardDemonicMinionsKilled`).
  - Adds find their owner through `ResolveOwnerBrain()`.
  - **Demonic minions spawn at `PathfindingProvider.Instance.GetRandomPoint(zone, pos, 150, filters)`** instead of a
    raw ±150 offset. The comment: "so that minions can't spawn inside walls".
  - Deaths go through `ProcessDeath`.
- **Ours:** `MorganaBechardSilcharde.cs:22-27,77,300,452` has six static counters plus static `BechardKilled` and
  `SilchardeKilled`, and raw offsets.
- **Escorts:** True165 adds period-sourced Salisbury Morgana escort specs (undead lieutenant, armswoman, cleric,
  mercenary, paladin; all level 31) in `ENCOUNTER SPECS.md`.

**Grand Summoner Govannon and the summoners (Summoner's Hall).**
- **Upstream** (`GrandSummonerGovannon.cs:340,468,473,553,558`):
  - The sacrifice/demon gate uses per-brain `SacrificeCompleted` / `DemonCompleted` flags and an owned `_shade`.
    Static counters (`SacrificeKilledCount` and the rest) are gone.
  - Sacrifices and demons walk with `PathTo`.
- **Ours:** `GrandSummonerGovannon.cs:222-223,438,523,608,686-693`, all static.
- **Lossren:** upstream `SummonerLossren.cs:84` removes Tortured Souls and Explode Undead when Lossren dies. Ours
  overrides `Die` (`:374`, `:535`) without that cleanup, so stale adds can outlive the boss and catch bots.
- **Other forks:** True165 lowered Govannon to 200k HP ("run 18: 300k fell ~1 %/min under 60 raiders"). DividedRealms
  restored the heal-to-max on reset.

**King Tuscar, Queen Kula, Hjalmar and Suttung (Tuscaran Glacier).**
- Upstream `403d084dc`:
  - Partner checks re-resolve if the cached partner is gone or has the wrong brain (`IsKingTuscarUp()` at upstream
    `KingTuscarAndQueenKula.cs:140`).
  - Hjalmar and Suttung check "any other living Suttung or Hjalmar in the region" (`IsOtherSuttungUp`,
    `IsOtherHjalmarUp`).
- Ours caches `QueenKulaUp` / `KingTuscarUp` bools and has 20 + 13 statics.
- The "one dies, the other enrages" link is where stale caches give a wrong enrage.

**Green Knight (open world).**
- Upstream `OFGreenKnight.cs:371-395`: `PathTo` for the walk to the tree spots, and instance state.
- Ours: `OFGreenKnight.cs:364-369` has statics `walk1..9`, `Pick_healer`, `IsSpawningTrees` and the `CanHeal` flags;
  `WalkTo` at `:415-439`.
- Our raid-challenge helpers (`CanChallenge`, `RaidChallengeDue`, `:227-232`) are ours alone.

**Glacier Giant and Evern.**
- Glacier Giant: upstream changed 9 lines (statics → instance, `ProcessDeath`). No mechanic change.
- Evern: upstream changed 3 lines. No mechanic change.
- No other repo has more.

**Dragons: Golestandt, Cuuldurach, Gjalpinulva.**
- **Upstream master** (`b3b9a24f7`):
  - The roaming path ends at the dragon's real `SpawnPoint`, passed into the brain.
  - It returns through `FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN)`.
  - The `SpawnHeading` getter is fixed.
  - Ours still overrides `ReturnToSpawnPoint` (`AlbGolestandt.cs:198`, `HibCuuldurach.cs:199`,
    `MidGjalpinulva.cs:197`) and has hard-coded roam points.
- **Upstream branch `fen/raid-encounters`:**
  - The unified `DragonBrain` and `DragonConfig` are described in 1.1.
  - Differences from ours:
    - The breath is a *directional cone at a telegraphed anchor*, so the anchor should face it away from the raid.
    - Pets are thrown with their owner.
    - The messenger wave scales with the roster.
  - The cone breath is a design choice in that branch, not an established period fact. Check it against the period
    sources before adopting it.
- **DividedRealms:** lair from the DB row, plus add models and sizes.
- **DOLSharp:** HP stages so a regen doesn't repeat phases, a difficulty %, and retriever adds.
- **TTom03:** a lair `Area.Circle`.
- **Ours is the most complete on landing, flight, throw geometry and adds:** `DragonFlightLanding.cs`,
  `DragonCombatGeometry.cs`, `DragonEncounterAdds.cs`, `bots/autonomous/RealmRaidDragonFight.cs`.

**Beliathan, the princes and the high lords (Darkness Falls).**
- Upstream changed only `ProcessDeath` and statics.
- TTom03 added seal drops and respawn jitter.
- No repo has princess or prince devour mechanics beyond ours.

**Aroon, Xaga, Myrddraxis, Iarnvidiur, Balor and the other epic-dungeon bosses.**
- The same upstream state and `ProcessDeath` refactor applies: Aroon 208 lines, Xaga `dfad601d2` (loop path plus add
  cleanup), Myrddraxis 54 lines, Iarnvidiur 12, Balor 15.
- No fork has different mechanics.

**Kill-count-gated named.**
- Upstream `EncounterGateAdd` / `EncounterKillCounter` (`GameServer/gameobjects/EncounterGateAdd.cs`,
  `ai/brain/Special/EncounterKillCounter.cs`) is used by Throatripper, Queen Major, Snarls, Stripe and Zrit-Zrit.
  We have neither class.
- Bots could read the counter to know "kill N adds first".

**A gap of our own, found by comparing with upstream's `EpicNpcArmorFactor`.**
- Our `source/server/GameServer/gameutils/AttackerTracker.cs:153` lowers an epic boss's AF scaling only for
  `pair.Key is GamePlayer`: −0.04 each, floor 0.4.
- `GameBot : GameNPC, IGamePlayer` (`bots/GameBot.cs:32`) is neither `GamePlayer` nor `GameSummonedPet`.
- **So bot attackers never lower the boss's armor.** A 60-bot raid fights every `GameEpicBoss` at the full 1.6 factor
  (`GameEpicBoss.cs:9`). Sixty real players would bring it down to 0.4.
- This likely accounts for part of the "boss HP barely moves under 60 raiders" reports (the True165 Govannon note).
  Owner decision: count `IGamePlayer`, or move to the upstream roster model.

---

## 3. Take this: ranked

Effort: S < 1 day, M 1–3 days, L > 3 days. Risk is the chance of breaking encounters or bots.

| # | Item | From | Effort | Risk |
|---|---|---|---|---|
| 1 | Count bots in epic AF scaling (`AttackerTracker.cs:153`, `is GamePlayer` → `IGamePlayer`, or a bot weight); put it behind a server property | ours vs upstream `EpicNpcArmorFactor.cs` | S | M (balance; owner decides) |
| 2 | Host initializer: `System.Timers.Timer` → `ECSGameTimer` (`Host.cs:25`) | upstream `c0476c41b`, DividedRealms | S | L |
| 3 | Boss-death cleanup: port the `ProcessDeath` overrides that remove adds and objects (Lossren souls, Olcasgean vortex/antipass, Morgana minions, Xaga adds and others) | upstream `0b581e3fb`, `403d084dc` | M | L–M |
| 4 | Static → instance encounter state, owner-brain links for adds, robust partner checks. Start with Legion, Olcasgean, Morgana, Govannon, Tuscar/Kula, Hjalmar/Suttung, Green Knight, Apocalypse, Host. Merge by hand around our bot hooks. | upstream `fca5900d7`, `66dbe4220` (branch `fen/shared-variable-safety`) | L (M per boss) | M |
| 5 | Navmesh-safe add spawn points (`GetRandomPoint(zone, pos, r, filters) ?? pos`) as one helper beside `EpicSpawnPlacement`, used by every add spawner | upstream Morgana `:338` | S–M | L |
| 6 | `WalkTo` → `PathTo` for scripted boss and add moves (Green Knight trees, Govannon sacrifices, Steinvor, Easmarach, Apocalypse) | upstream `12872751a` | S | L–M (navmesh gaps) |
| 7 | Patrols as loop paths (`MovementMgr.CreatePath` helper plus `MoveOnPath` restart): Host, Crypt Lord, Sacristan, Provisioner, Xaga, Torst, the Air primal. Expose the waypoint arrays to `EpicEncounterTactics.Patrols.cs`. | upstream `d832cde95`, `ac841e9f1`, `cadc7b8c0`, `0fbdbcb1a` | M | L–M |
| 8 | Raid route robustness: next-encounter probe time budget with resume, proven walk through approach dungeons, dungeon exits after a raid ends, and their tests | DividedRealms `RealmRaidDungeonRoute.cs`, `AutonomousRealmRaid.cs`, `UT_RaidRouteRules`, `UT_RaidStrandedRelease` | M | L–M |
| 9 | Reinforcement waves for dragon raids (gather beyond 6,700 from the lair, wave of 8, 60/180 s) and the raid end rules (3 wipes on the final encounter / 1 h stall / call off) | True165 `AutonomousRealmRaid.Reinforcements.cs`, CHANGELOG | S–M | L |
| 10 | Use `IT_DragonBrain.cs`'s 12 tests as the behaviour checklist for our three dragons (breath per HP band, stun ≤75 %, messenger wave ≤50 %, reset, leash, night roam), and port `MovePetsWithPlayer` into our throw | upstream branch `fen/raid-encounters` | M | L (tests) / M (throw) |
| 11 | Dragon return-to-spawn: FSM `RETURN_TO_SPAWN`, roam path ending at `SpawnPoint` (fits `DragonLairPlacement`), `SpawnHeading` fix | upstream `b3b9a24f7`, DividedRealms `DragonLairPlacement.Adopt` | S | M (our flight/landing code overrides it) |
| 12 | One encounter-size model in place of per-boss flood caps: roster snapshot at pull, HP `+10 %/member over 8` capped at 80, `ScaleUnitCount` for adds, activity-window AF. It must count `GameBot` (the branch keys on `GamePlayer.InternalID`). Leave out the outsider help lock and buff strip. | upstream branch `RaidEncounter.cs` | L | M–H (unmerged upstream; changes every epic) |
| 13 | Encounter kill gates (`EncounterGateAdd`, `EncounterKillCounter`) and a bot read of the counter | upstream `6f8b6cf49` | S–M | L |
| 14 | Per-boss "mechanic multipliers": while a mechanic is live, veto generic bot actions (AoE avoid on the main tank, casting while moving out of a cone, chasing a thrown target), in the style of mod-playerbots `*Multipliers.cpp`. Keep the trigger → action(priority) shape in `EpicEncounterTactics`. | mod-playerbots (ideas only) | M | M |
| 15 | Period-sourced encounter spec per big boss (PERIOD vs CHOICE labels, Prima, Allakhazam via Wayback, Freddyshouse), feeding `docs/encounters/hand-notes.json` | True165 `ENCOUNTER SPECS.md` method | M per batch | L |
| 16 | Dragon add models and sizes (granite giants 615/616, glimmer 618–620) | DividedRealms dragon scripts | S | L |
| 17 | Native raid window click-to-target fix (client XML only, hash-guarded) for a player raiding with bots | stefanrows `build_raid_click_fix_client.py` | S–M | L–M (client file; keep hash guards) |
| 18 | Dragon HP "stage" memory so a small regen never repeats a phase; check that our dragons already latch | DOLSharp `DragonBrain.cs` ~L230 | S | L |

**Don't take:**
- Upstream `e2893f92e` (AggroTable) and the FSM/return-to-spawn engine commits as cherry-picks. They need the engine
  changes underneath; take them only with a full engine sync.
- TTom03's currency drops and respawn jitter. Freeshard economy, not mechanics.
- The `fen/raid-encounters` outsider help lockout and buff stripping. It would break bots and companions helping a
  raid.
- stefanrows' raid battlegroups and guild memory. PvP world only.

**No repo anywhere has Darkness Falls raid AI, Legion or Behemoth tactics, or level-70+ encounter AI.** Our
`EpicEncounterTactics.DarknessFalls.cs` and `bots/RaidBossTraits.cs` are already the most developed versions found.

---

## 4. Licenses
| Repo | License | Can we copy code? |
|---|---|---|
| OpenDAoC-Core (and the Atlas history) | GPL-3.0 | yes |
| OpenDAoC-Database | GPL-3.0 | yes |
| DOLSharp | GPL-2.0-or-later | yes, as GPL-3.0 |
| zomboden True165, DividedRealms / daoc-divided-linux, stefanrows, l3tal123, TTom03, trozzelle, reap3rx | GPL-3.0 | yes, with attribution in the commit and history |
| catastrophic-impact/daoc-linux-server-bots | no license file | no; it is derived from GPL code, but take ideas only |
| mod-playerbots | GPL-2.0-or-later (C++) | ideas only (another language and engine) |
| cmangos/playerbots, nickelm/irish-sea | no license file | ideas only |

---

## 5. Sources
- https://github.com/OpenDAoC/OpenDAoC-Core (branches `master`, `fen/raid-encounters`, `fen/shared-variable-safety`, `fen/scripted-mob-cleanup2`)
- https://github.com/OpenDAoC/OpenDAoC-Database
- https://github.com/bm01/OpenDAoC-Core
- https://github.com/Dawn-of-Light/DOLSharp
- https://github.com/zomboden/OfflineDAoC-True165
- https://github.com/rambodolphin13/DividedRealms
- https://github.com/catastrophic-impact/daoc-divided-linux
- https://github.com/stefanrows/OfflineDAoC
- https://github.com/l3tal123/OfflineDAoC-BotRealm
- https://github.com/TTom03/DOLServer-DAoC-NewFrontiers
- https://github.com/Nikse77/OfflineDAoC
- https://github.com/mod-playerbots/mod-playerbots
- https://github.com/cmangos/playerbots
- https://github.com/nickelm/irish-sea
- Web search ("Dark Age of Camelot emulator source code epic boss scripts"): https://dolserver.sourceforge.net/, https://github.com/Dawn-of-Light, https://github.com/bm01/OpenDAoC-Core
