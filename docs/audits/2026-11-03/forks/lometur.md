# Fork survey: lometur/OfflineDAoC ("HearthDAoC")

Surveyed 2026-11-03, read-only, from `refs/forks/lometur/*` in `/home/claude/wt-12`. Nothing was built, run or played.
Labels: **[V]** read in code or git, **[I]** inferred, **[E]** estimate.

## 1. What it is, and who it's for

- **Theme.** HearthDAoC is an unofficial fork that runs Offline DAoC as an **always-on central multiplayer server**: a Linux
  Docker container that a few people log in to. It keeps its own changes small, marks them, and leans hard on the
  1.65 period ("as the period had it", sourced from Allakhazam and Wayback captures) [V `docs/fork/FORK.md`, `README.md`].
- **Size.** 731 commits that neither upstream nor we have, from 2026-10-05 to 2026-11-03. Authors: 664 Thomas Farral,
  64 Lometur (the merges), 3 from CI [V]. It is synced to upstream 0.37b [V merge-base `ab4df02a`]. All three side branches
  (`proto/player-bots`, `cpu-meter-alloc-dash`, `epic-followups`) are already merged into its main, with nothing of their
  own left over [V `rev-list --count` = 0].
- **Release mechanism.** Merging to main *is* the release. CI (`.github/workflows/server-image.yml`) runs Python, NUnit
  and container smoke tests, then publishes `v<upstream>-hearth.<n>`: a GHCR image plus deploy and client bundles
  [V]. The server side uses `hdc init|update|upgrade-world|carry-rvr|spawns|backup` [V `deploy/hdc`]. Each world fix
  (`deploy/bin/world_fixes.py`) runs at every start, or once per world behind a marker [V]. Players get a Linux client
  bundle that re-applies hash-pinned client patches at each launch [V `client/patches/`].
- **Workflow.** A spec, plan and verification doc per feature. Upstream edits are marked `// HearthDAoC:`, and source
  tests fail when a sync moves a hook [V `deploy/tests/test_bot_cpu.py`].

## 2. Notable features

**Already ours** [V]: the quest code we ported in 1.1 (`QuestJournal`, `QuestIndicators`, `QuestDependencies`,
`QuestDeliveryItems`, `QuestNames`, `EpicChain`, `/epic`, epic-chain data; merge `3dc32926`).

| Feature | Where | Quality | We have it? | Verdict |
|---|---|---|---|---|
| **Bot CPU meter** `/botcpu`, `bot_cpu_meter` (default off), including `allocMBSec` (#148, #150) | `scripts/hearthdaoc/BotCpu{Meter,Report,Script,Command}.cs` (2,733 lines); hooks: 2 lines in `GameLoop.ExecuteForEach`, 3 in `NpcService.Tick` | About 350 `[Test]` lines across 3 suites [V]. The live-check doc's `Result:` lines are all blank [V], so no in-game check is on record. | No. `ai_budget_ms` is declared and never read [V `AutonomousPopulationProperties.cs:32`]. | **Borrow, easy (1–2 days [E]).** Both hook files match upstream in ours [V]. It charges CPU and allocations by *kind* and by the 17 tick stages; all fleet planning (`Prepare{Brain,Rvr,Coordinator,Camp}Tick`) is one `Fleet` scope [V]. `AI_PROFILE` needs a sub-scope per Prepare call and tick p95/p99 (its p50/p95 spread across bots, not ticks) [V]. The `/proc` thread readers give `-` on Windows [V]. |
| **Headless client**: a real `GameClient` with no socket that logs in through the real login handlers (#137) | `playerbots/HeadlessClient.cs`, `HeadlessPacketLib.cs`, `HeadlessInbox.cs`; 3 marked blocks in `ClientService.cs` | `UT_HeadlessClient`, `UT_PlayerBotNetwork` [V] | No. We have only `BotDummyClient` [V]. | **Adapt (3–5 days [E])** as the base of our scripted-player harness (audit challenge 1); our `ClientService.cs` matches upstream [V]. In process, so not the protocol-level player the audit wants [I]. |
| **Player bots**: real `GamePlayer`s the server plays, as solo PvE hunters | `playerbots/` (`PlayerBotDriver.cs` alone is 6,085 lines); 22 marked edits in `PlayerTravelPath.cs`; new Detour exports | 1,237 `[Test]` lines in the driver suite; many measured 15-minute test legs [V `player-bots.md`]. Off by default, "test-world tool" [V]. | No | **Watch.** With the wake lever, 390 bots cost about 0.44× the GameBots [V doc; their estimate]; 0.53 deaths per bot-hour [V]; heavy native Detour changes. No grouping or invite code [V grep], but a GamePlayer bot could lead a real `Group`: relevant to bot-led groups [I]. |
| **Play sessions**: a log-off like a player's (`/quit` and its timer, walks out of aggro, a forced log-off only with no real player in sight) (#160) | `PlayerBotSessions`, `PlayerBotSpawner` | `UT_PlayerBotSessions`, `UT_PlayerBotSpawnerSessions` [V] | Partly: our gamebots have `bot_schedule_mode=session` [V `LIVING-SERVER.md:149`] | **Adapt the rules (small):** the visible `/quit`, no forced log-off in a player's sight, the death cap carried over. |
| **SQLite WAL and online backups** | `deploy/bin/gen_config.py:84` (WAL, `Synchronous=Normal`, `BusyTimeout=10000`); `deploy/bin/backup.py` (`sqlite3.backup`, `quick_check`, atomic rename, rotation, a `pre-bots` label) | `deploy/tests/test_backup.py` [V] | No. Our default string is `Journal Mode=Off;Synchronous=Off;Pooling=False` [V `GameServerConfiguration.cs:386`]. | **Borrow the design (1–2 days [E])** as C# or launcher code. Our `SqliteObjectDatabase.cs:736-784` already copes with WAL [V]. |
| Double buff removal and chant strength fixes (#190, #193) | 4–5 effect files | Unit tests [V] | No. Every file but `EffectListComponent.cs` matches ours [V]. | **Borrow, easy (hours).** |
| Relic carriers: players and player bots carry relics, and a carried relic survives a region crossing for 60 s (#189) | `GameRelic.cs` and others | Tests [V] | Partly: our gamebots carry relics; our `GameRelic.cs` is identical to upstream [V] | **Borrow the crossing fix (easy).** |
| RP timer waits: a released bot waits at a safe spot and comes back worth realm points (#136) | `RpTimerWait*.cs`, 6 marked lines in the autonomous controller | Unit tests plus an in-game doc [V] | No [V] | **Adapt (medium).** Our controller has diverged [I]. It makes RvR look smarter. |
| Bridge decks on horse routes (11 bridges) | `deploy/bin/route_fixes.json` | `test_route_fixes.py` [V] | No | **Borrow as world data (low–medium).** It improves stablemaster routes. |
| Sourced period data: 126 mob, item and quest fixes, start towns, quest camps, dialogue | `deploy/bin/{mob_fixes,start_towns,quest_camps,quest_dialogue}.*` | Each fix carries a dated source [V] | We took the epic chains only | **Adapt.** It fits our rule R8 (every era rule carries a fact id and a date). |
| Quest extras: "Do you accept?", a grant handed once, `<Player>` whispers, linked camps, `/harm` quest credit | `DataQuestOffers`, `QuestGrants`, `QuestWhispers`, `LinkedCamps`, `harm.cs` | Unit tests [V] | No [V] | **Borrow (small each).** These feed our priority 3, quests that work with bots. |
| Container deploy, `hdc`, Linux client bundles, client patch sets, SI start choice, classic battlegrounds | `deploy/`, `client/`, `tools/linux/` | Heavily tested [V] | n/a | **Skip.** A different audience. The battlegrounds work deletes 12 upstream BG quest scripts [V]. |
| Guild founding needs 8, no starter guilds; `/tele` and `/tc` GM-only | `GuildForming.cs`, `command_plvl_overrides` | Tests [V] | n/a | **Skip.** These are multiplayer rules. |

**Gaps the fork doesn't touch** [V by grep, otherwise I]: bots inviting the player and bot-led groups, chat realism, RvR
perception, an economy ledger, and a population preset. Its only population tool is `add-bots.sh` with a backup first.
It avoids "nothing happens while the server is off" only by never turning the server off [I].

## 3. Conflicts with our owner's gameplay rules

| Rule | Finding |
|---|---|
| Real loot, inventories, coins, upgrades | No conflict. Player bots loot (`TryLoot`) [V], and the reward dedupes make each reward real and handed once [V]. |
| Realm Exchange | **Conflict if copied as is.** The default `HEARTHDAOC_GM_ONLY_COMMANDS="/tele;/tc"` makes the Exchange teleport GM-only [V `.env.example:17`]. #162 itself keeps `/tc` as the Exchange teleport, which is fine [V]. |
| Stablemaster routes | No conflict. Bridge decks and the quit-marker fix in `TemporaryGroupStableTravel` improve them [V]. |
| Sell only at natural breaks | No conflict. Player bots never sell [V grep]. Adapting them would mean adding natural-break selling. |
| Gamebots start without armour and earn it | **Conflict if applied to gamebots.** Player bots placed at a level get GameBot-rule armour at first login (`PlayerBotKitGear.cs`) [V]. That is acceptable for a test harness only. The fork never touches `BotEquipment.cs` [V]. |
| DF open to solo bots | No conflict. The fork has no Darkness Falls changes [V]. |

## 4. Legal and licensing

- **License.** GPL-3. The fork's `LICENSE` is byte-identical to ours [V]. Borrowing is fine with attribution.
- **The 54 EA files**, removed in `cd851d51` (merged as #198) [V]:
  - `source/tools/observer-model-inspection/`: **51 files.** These are the stock 1.127 `gamedata.mpk` unpacked byte
    for byte: `animnifs, anims, animsound, bowsprit, canims, emotes, fig3decalmap, fig3descriptions, fig3eyecolormap,
    fig3facemap, fig3haircolormap, fig3map, fig3parts, fig3skincolormap, fig3tints, fig3tofighaircolor, fig3tofigmain,
    figtofig3haircolor, figtofig3main, fixexcept, hair, horsearmorthememap, horsemap, horsematerialmap, icons, insignia,
    items, jumppoints, meshmateriallist, moneffectnifs, moneffects, moneffectsets, monnifs, monsters, mskins, nifswap,
    objects, patterns, pskins, siege, siegeeffects, skins, speffects, spells, spnifs, spweapons, stylemap, styles,
    tints, tooltips` (`.csv`), plus `loading.dat`.
  - `source/reference/client-launcher/WPFLauncher/img/options-bg.png`: the client's login background.
  - `tools/pet-art/work/barrow-deflection/barrow_deflection_32.png` and `barrow_deflection_preview.png`: a new glyph
    drawn on a stock icon frame.
- **In our repo: all 54 are present in both `origin/release/1.2` and `origin/main`, and the blobs are byte-identical to
  the fork's before removal** [V]. shadowofze added them in `4d8b513f` (2026-09-15) and `08ebffe3` (2026-10-01) [V].
- **What depends on them in ours** [V]:
  - `tools/pet-art/install_bulwark_update.py:41` reads `barrow_deflection_32.png`. `draw_icon.py`, beside it, can rebuild
    the PNG from a player's own client.
  - `OptionsWindow.xaml:40` and `WPFLauncher.csproj:163` reference `options-bg.png`.
  - The 0.36b and 0.37b baseline manifests list all 54 by hash. Those are hashes, not the files, so they can stay, as the
    fork kept them.
- **Recommendation.** Remove the 54 at HEAD on both branches; build the bulwark icon with `draw_icon.py`; give the XAML
  a plain colour. They stay in our public history [I]; purging needs a history rewrite, which the owner's rules forbid
  for legacy branches and tags, so that is the owner's call. The fork scanned upstream's files, not ours: **have the
  owner confirm the provenance of** `tools/fork-build/content/pregame-splash.mpk`, `tools/installer/art/*.jpg` and
  `tools/installer/src/Assets/*` (5 images), and of the launcher's `offline-daoc-client-splash.mpk` ("OfflineDAoC's"
  art per the fork [V comment]) [I].

## 5. Top 5 to borrow (North Star value per unit of effort)

1. **SQLite WAL and online backup** (1–2 days [E]). This fixes our top data-safety priority. Port `backup.py`'s
   sequence (online backup API, `quick_check`, atomic rename, rotation, a labelled copy before risky operations) and its
   connection string.
2. **The CPU meter as the base of `AI_PROFILE`** (1–2 days, then about 1 day for per-system scopes and tick
   percentiles [E]). The hooks apply cleanly [V]. It unblocks "measure before adding", and could finally give
   `ai_budget_ms` something to read.
3. **The headless client as the scripted-player harness** (3–5 days [E]). Take `HeadlessClient` and its packet library
   and the three `ClientService` blocks, but not the 6,000-line driver. Write scenarios such as login, accept a quest, kill,
   turn in. Add protocol-level coverage later.
4. **Quest extras and the buff fixes** (1–2 days in total [E]): `DataQuestOffers`, `QuestGrants`, `QuestWhispers`,
   `LinkedCamps`, `/harm` credit, #190/#193 and the #189 relic crossing. Each is small and has unit tests. They fix bugs
   a player would notice.
5. **Horse-route bridge decks and the RP timer waits** (2–4 days [E]). Horses stop swimming under bridges, and released
   RvR bots come back worth points. Both make the world look like it has players in it, with no rule conflict.

**Credit.** Keep the pattern we already use [V]:
- a header line in each file, "From lometur/HearthDAoC" plus the fork commit or PR number;
- an entry in `CHANGELOG-LIVING-SERVER.md`;
- the thanks line in the README (`README.md:191`, which already names lometur's HearthDAoC).

Credit them under the handle "lometur (HearthDAoC)", not a personal name. Keep their GPL headers and notices.
