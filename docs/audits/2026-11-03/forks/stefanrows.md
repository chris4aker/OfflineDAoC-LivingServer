# Fork survey: stefanrows/OfflineDAoC

Surveyed 2026-11-03 from `refs/forks/stefanrows/*` in `/home/claude/wt-12` (read-only, git only).
Labels: [V] verified in git, [I] inferred, [E] estimated.

## 1. What it is and who it's for

- **Scale.** 684 commits in neither upstream nor ours [V]. They are by Aaron Zielke (453, the "owner") and Stefan Rows (231) [V].
  - It forked at `fad5a027` (2026-09-16), which is 314 commits behind upstream main [V].
  - It adds about 252k lines in 1,185 files, including 243 new test files. It has about 5,070 test attributes; we have 4,240 [V].
  - Every listed branch is already merged into its `main` [V].
- **Theme.** It started as a **Camlann/Mordred full-PvP conversion**: 1.65, Old Frontiers, pre-ToA, guild-owned keeps and relics (CAMLANN.md tiers 0–9) [V].
  - Since 0.332.0 it has **two worlds with separate saves**: `Realm-1` (classic RvR, the default) and `Camlann-1` [V].
  - The marker `offline_local_options.WorldModel` decides which world a save is. The two RvR event layers sit behind one facade (`AutonomousRvrEventLayer`) [V].
  - The fork is now **porting upstream's own 0.38c RvR bots back** into Realm-1. Its own RvR extensions are switched off there (RVR_FORK_EXTENSIONS.md) [V].
  - Since 2026-10-24 it also adds Trials of Atlantis and Master Levels data packs [V].
- **Audience.** The owner's own play, in German (the night reports and the "Kriegstafel" window are German) [V]. The repo is public "to fork".
- **Releases and updates.**
  - Versions use semver 0.x.y. The CHANGELOG is at 0.348.0 (2026-10-26) [V]. I did not check the GitHub releases list.
  - Players install shadow's **v0.3** full download, then run `UPDATE-OFFLINE-DAOC.cmd` and `Update-OfflineDAoC.ps1` (`tools/release/`) [V].
  - The update pack has a SHA-256 manifest. It backs up what it replaces to `update-backups`, prints an undo command and refuses to run while the game is open [V].
  - The pack replaces the client's `game.dll` and UI XML, guarded against the v0.3 hash `67dcf68a…` [V].
  - It is published from a local script with `gh release create` (`publish-fork-release.sh`), with no CI [V].
  - The AGENTS "fast shipping" rule says to **skip automated tests and post-deploy checks** when shipping [V].

## 2. Notable features

The "Have?" column says whether we have it: on our release/1.2 branch, or upstream.

| Feature | Where | Quality | Have? | Verdict |
|---|---|---|---|---|
| **Group doctrine**: 14 doctrines from the group's composition, fight appetite, retreat, formation | `AutonomousRvrDoctrine.cs`, `…DoctrineRuntime.cs` (37 KB), RVR_GROUP_DOCTRINE.md, branch `rvr-group-doctrine` (0.108) | `UT_AutonomousRvrDoctrine`. Bots-only log runs (NIGHT_REPORT: RvR deaths per hour 1,763→790, hotspot share 54%→13%) [V]. "Not checked: real client" [V] | No [V] | **Adapt**, large: everything hangs off one predicate (`Applies`), so it has to be decoupled first [V] |
| **Observe before engaging**: hold at 2,200 units, third-party add after three enemies are down, pick off stragglers, move on | `AutonomousRvrObserve.cs` (45 KB), `…Controller.RvrObserve.cs` | `UT_AutonomousRvrObserve` (18 KB) [V] | No. It complements PR #53 (we decide *noticing*, this decides *engaging*) [I] | **Borrow**, medium |
| **Hub fan, route variants, danger memory**: groups leave by different sides, take flank or cover routes, avoid death sites for about an hour | `AutonomousRvrRoutePolicy.cs`, `AutonomousRvrDangerMemory.cs`, `VaryKeepDeparture` (0.142–0.147) | `UT_AutonomousRvrDangerMemory`. Own bug 174 (fewer fan inserts). The fork itself notes the dog-legs "may read as wandering" [V] | No. We ported only PvE death-spot memory and mob bends (`dd699868`, `c388ad98`) [V] | **Borrow**, medium. Roaming legs only, never on siege marches [V their advice] |
| **Movement flow**: retarget while moving, main-body leader succession, formation steering in the movement tick | `AutonomousRouteRecoveryPolicy.ShouldRetargetMovingOrder`, `ChooseMainBodyLeader`, `NpcMovementComponent.FormationFollowTick` (tasks 135–138), branch `feat/rvr-movement-flow` | Unit tests. Every item is "real-client check pending". Bug 173 (leaders re-elected within 5 s) [V] | Partly. We have upstream `AutonomousGroupFormationFollow` and the route give-up fixes (`333529b6`) [V] | **Borrow 135/136**, small. **Watch 138** (Detour load) |
| **Healer support by spec**: smite or nuke only when a second healer covers, a pac Healer or cave Shaman mezzes before healing, Bards don't melee | `BotBrain.SupportOffense.cs`, `AutonomousRvrSupportOffense.cs` | `UT_AutonomousRvrSupportOffense`. Every 5 minutes a log line. Overflow bug fixed in 0.347.0 [V] | No [V] | **Borrow**, small |
| **Stealther loop, speed habits** | `AutonomousRvrStealthLoop.cs`, `AutonomousRvrSpeed.cs` | Tests. Speed song active only 6–12% of travel (their task 69) [V] | No | Stealth: **borrow**, small. Speed: **watch** |
| **Build catalog**: 118 plans for 39 classes, sourced, plus realm ability paths per class | `CompanionBuildPlanCatalog.cs`, COMPANION_BUILD_RESEARCH.md, AUTONOMOUS_RA_BUILDS.md | Sources are mostly Uthgard advice; the doc says they "do not establish retail-era popularity" [V] | No era meta builds (our gap) | **Adapt** the data as an input, small–medium |
| **BOT_THINK_PROFILE**: about 40 timed phases, a log line every minute, slow turns | `BotThinkProfiler.cs` (367 lines, used in 29 files) | `UT_BotThinkCostBounds*` [V] | No (our gap: no AI cost profiling) | **Borrow**, small–medium |
| **World Speed** 1–20×, only while no client is connected | `OfflineWorldSpeedControl.cs`, launcher `WorldSpeedProtocol.cs` | 29 tests. At 3,900 bots, 10× reached about 1.7× (task 107) [V] | No [V] | **Adapt**, medium: it can age an empty world unattended [I] |
| **Saved `/companions` roster**: 78 recruits per character, gear, builds, Companion Manager window | `PlayerCompanionRoster.cs` (147 KB), `CompanionManager.cs` (79 KB), 32 test files | Stages 2/3/5 accepted by the owner. Stages 4/6 real-client pending. The window needs a patched `game.dll` [V] | No. We have temporary `/spawn` helpers | **Watch**. Large, and the window couples to a client patch. Command fallbacks exist [V] |
| **Open BG raid planner**: Thidranki director, archetype presets, `/bgsetup`, OPEN BG window | `battlegrounds/open/*` (Director 82 KB, Roster 89 KB), `OpenBgWindow*.cs`, branch `obg-window` | About 20 test files and live bots-only runs (OPEN_BG_KEEP_TEST: the keep falls in 5 of 5 raids) [V] | No | **Watch**. Off by default in their RvR world since its crash bugs (175a/179). The archetype table (OPEN_BG_GROUP_PRESETS.md) is the reusable part [V] |
| **Camlann full-PvP conversion** (and the Gothwaite PvP mode) | CAMLANN.md, GOTHWAITE_PVP_MODE.md, branches `codex/camlann-tier0/1`, `feat/gothwaite-pvp` | Tiers 0–9 have offline gates (1,900+ tests). The final real-client gate is open [V] | No | **Skip**. Not a 2003 realm server, and its own owner moved the default back to RvR [V] |
| **ToA / Master Levels data packs** | `CoreServer/data-packs/*.sql(.gz)`, `tools/toa/sql` | "No server has booted with the packs" [V] | No (upstream switched ToA off in 0.38) [V] | **Watch** (era and licence, see §4) |
| **Player types, guild KOS lists, realm-rank nameplates** | `AutonomousPlayerType`, FEATURES.md | Tests [V] | Not by those names [V]. Partly overlaps our personalities [I] | KOS and nameplates: **adapt**, small |
| **Free buff NPC "Realm Enchanter"** | 0.178.0 | n/a | No | **Skip** (anti-era) |

## 3. Conflicts with our owner's gameplay rules

- **Real loot, inventories, coins and equipment upgrades.**
  - Real loot is kept in name (their AGENTS) [V].
  - Since 0.347.0, guards, lords, gamebots, companions and pets **drop no items** ("upstream NoItemLoot") [V]. We may already carry the upstream piece; check that before porting nearby code [I].
  - Companion "personal gear drops" add an extra item faucet [V].
- **The Realm Exchange.** Kept: own realm in RvR, a foreign capital in Camlann [V]. No conflict for RvR.
- **Stablemaster routes.** Kept [V]. Camlann teleporters list all three realms' destinations [V]. Added frontier porters are a soft conflict [I].
- **Selling only at natural task breaks.** FEATURES says selling is retained [V]. I found no conflicting change [I].
- **Gamebots start without armour and earn it.** **Conflict** [V]:
  - A fresh RvR world seeds 600 bots per realm at level 50 "with class loadouts".
  - "Established" batches come with matching equipment.
  - A world-shape borrow must leave the gear out.
- **Darkness Falls is open to solo bots.** **Possible conflict**:
  - In Realm-1, DF opens only to the realm holding the most keeps [V].
  - Hibernian bots get no DF goals while the Wardens hold both Hibernian DF keeps [V].
  - I found no gate that applies only to solo bots [I].

## 4. Legal notes (EA client material in git)

- **Inherited, and also present in ours and upstream** [V]:
  - `source/tools/observer-model-inspection/*.csv` (50 client data tables: `items.csv`, `objects.csv`, `pskins.csv`, `monsters.csv` and others) plus `loading.dat`.
  - `source/tools/OfflineDaoc.Launcher/Assets/offline-daoc-client-splash.mpk` (17 MB, an MPK client archive; contents not checked).
  - Not EA, but third-party binaries: `source/reference/daocportal/connect.exe` and `DOLLoader.zip`.
- **Added by the fork** [V]:
  - No `.dll`, `.mpk`, `.nif` or client `.csv` files.
  - `source/server/tools/client_map_labels.json`: place names hand-copied from stock map textures. Derived data and low risk [I].
  - Mock PNGs in `docs/open-bg-redesign/` drawn "from the real builder XML". They may include client skin art; unchecked [I].
- **Not in git, but distributed** [V]: the release packs ship a patched EA `game.dll` and stock-derived UI XML (`build_fork_release.py`). That is the same exposure as upstream's patches.
- **ToA SQL** comes from Pactor/gaheris and OpenDAoC db-public [V]. TASKS 134 says "Licence is not a concern (owner: private only)", yet the files are public [V]. Check the licence before borrowing.

## 5. Top 5 to borrow (North Star value per effort)

1. **BOT_THINK_PROFILE** (`BotThinkProfiler.cs` plus its cost-bound tests) [E: 1–2 days].
   - It is the base our audit's "measure before adding" priority (#4) asks for.
   - We would add tick p50/p95/p99 ourselves; the fork logs per-phase totals and slow turns [V].
2. **Hub fan, route variants and RvR danger memory** [E: 2–3 days, gated to roaming legs].
   - It answers "warbands share one route" directly.
   - It is the fork's best-measured change (hotspot share 54%→13% in a bots-only log run, real client unchecked) [V].
3. **Observe before engaging, on top of PR #53** [E: 3–4 days, decoupled from the doctrine predicate].
   - It turns "noticed" into 2003 third-party behaviour. Wire it after our observer cap.
4. **Healer support by spec, plus the build catalog as input data** [E: 1 day, plus 2–3 days for builds].
   - It gives cheap, visible era behaviour and seeds "era meta builds".
   - Label the build sources as Uthgard-era advice.
5. **World Speed fast-forward** [E: 3–5 days].
   - It ages a fresh world unattended, which addresses "the world starts empty".
   - Leave out the level-50 loadout seeding (armour rule).
   - Quick extras: leader succession and retarget while moving (tasks 135/136, small).

**Credit.**
- Per file: a header line, `Ported from stefanrows/OfflineDAoC (Stefan Rows, Aaron Zielke), <commit>, GPL-3.0`.
- Per commit: `Port from stefanrows/OfflineDAoC <sha>` in the message, the same as `dd699868`.
- Docs: a "Borrowed from forks" section in docs/LIVING-SERVER.md and a CHANGELOG-LIVING-SERVER line naming the fork and both authors.
- Licence: their code is GPL-3.0 like ours, so porting it is compatible [V].
