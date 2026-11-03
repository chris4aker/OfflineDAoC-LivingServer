# Can the technology carry the North Star? Technical audit of Living Server (release/1.2)

Auditor lens: whether the engine, data layer, delivery and test system can carry "a fully alive peak server from the
era", meaning 600–1,000+ bots per realm at prime time on a home PC. Read-only audit; no branch, file or GitHub setting was changed.

- **Tree audited:** `origin/release/1.2` at `50ecb30d`, plus the worktree `/home/claude/wt-12`, the PR #53 worktree
  `/home/claude/wt-perception` (`204d8216`, based on `1ac34065`) and `origin/research/1.2-before-coding`.
- **External audit:** the independent audit (attachment `6229d49a`) was read as a set of claims to check.
- **Labels:** **[V]** verified (I read the code or doc, or ran it), **[I]** inferred from verified facts,
  **[E]** estimate. Doc quotes are what the docs *record*. Where a doc records an in-game measurement, the label
  means "the doc says so", not "I reproduced it".

---

## 0. Executive answer

The engine can plausibly carry 600 bots per realm today, and carrying 900 or more is not yet shown.
- **What has been seen [V, docs]:**
  - 600 per realm overnight;
  - test copies at about 1,000 online;
  - 1,800 total at a 1.7 GB live heap;
  - Lord Gildas killed by 180 bots.
- **Why the headroom is unknown:**
  - Nothing enforces a per-tick budget. `ai_budget_ms` is dead code [V].
  - Only slow brain turns are timed [V].
  - Every new 1.2+ system (chat rules engine, social facts, perception, remarkables, live events, crafting) adds
    per-bot or per-tick cost, and there is no budget line for any of them [V].
- **What blocks the North Star most:** the test system, not raw speed.
  - The world database is not in git, and CI cannot run the server against a world [V].
  - No player journey is proven end to end by automation [V].
  - Every gameplay claim depends on about five hours a week of the owner's play [V, RELEASE-TRAIN.md].
- **The data layer needs a durability decision before months-long worlds:**
  - The server's default SQLite string turns the journal off and sync off [V].
  - Nothing backs up the database routinely or before an install [V].

---

## 1. Scale and performance

### 1.1 What the docs record [V unless marked]

| Measurement | Value | Source |
|---|---|---|
| Live heap at 1,800 bots | "about 1.7 GB live at 1,800 bots", "server's own memory stays flat" | docs/KNOWN-ISSUES.md "Memory over very long sessions" |
| Windows handle growth | "about 1,700 an hour with 600 bots per realm" (a .NET `Lock` event per monster stat table); "about 40,000 handles … a day, against a limit of millions. Not changed for this release" | KNOWN-ISSUES.md; CHANGELOG-LIVING-SERVER.md:594-600 |
| Six-hour run | "Live heap stayed at 1.6-1.8 GB … working set at 5.8-7.0 GB"; ~2,600 "Long tick" warnings, "mostly 40-60 ms, some NpcService ticks of 190-270 ms on navigation"; 103 SQL queries over budget | docs/LOG-ERROR-BACKLOG.md:173-177 |
| Issue #25 stall | NpcService 1.0–1.3 s; "per-minute maximum averaged 310 ms, 14 of 113 minutes over 500 ms, worst 939 ms" (baseline before fix 064a387) | LOG-ERROR-BACKLOG.md:204-208; LIVE-EVENTS-ENGINE.md §5 |
| Effect leak (rambodolphin13's fork) | "30 million such effects and 326 ms per tick on a 3,000-bot" server; fixed | CHANGELOG-LIVING-SERVER.md:197-199 |
| Recast cascade | "memory went from 2.9 GB to 37 GB in ten minutes and the game stopped" (Lydsyg); cast-queue guard added | CHANGELOG-LIVING-SERVER.md:534 |
| Logged-out bots retained | 1,500 deleted bots in memory overnight, handles 2,200→9,800; fixed by a holder sweep costing "about 60 ms every two minutes" | CHANGELOG-LIVING-SERVER.md:583-593 |
| Allocation hot spots | `IsSafeArea/GetAreasOfZone` "~64 MB per 40 s"; group sweep "117 MB in 40 s" (both fixed) | FrontierThreat.cs:145 comment; AutonomousBotGroupCoordinator.cs:200-202 comment |
| Chat at scale | "Chat volume hasn't been tuned for very large rosters (1,000+ bots a realm)" | KNOWN-ISSUES.md |
| Live events M0 | The cost per monster and per bot at the owner's roster: **not measured yet** ("M0 measures it") | LIVE-EVENTS-ENGINE.md §5, §10 |

Pattern [I]: every big performance problem so far (#25, the effect leak, the 37 GB cascade, retained bots) was found
after the fact, in a log, by luck. Each fix was local. Nothing systematic would catch the next one.

### 1.2 Is `ai_budget_ms` enforced? No [V]

- **The setting:** `AutonomousPopulationProperties.cs:32-33` declares it with the description "Work over budget is
  deferred, never simulated". Setup writes it (`OfflineDaoc.Setup/Program.cs:345, 1075`), and the launcher shows it.
- **No reader:** `AI_BUDGET_MS` has no production reader. `AutonomousAiBudget.MaximumDecisionsThisTick`
  (`AutonomousAiBudget.cs:39`) is called only by `UT_AutonomousAiBudget.cs:29-30`.
- **Consequence [I]:** the setting's description is false today, and a player who changes it changes nothing.
- **What does exist:**
  - Per-mode think intervals (`AutonomousAiBudget.IntervalMilliseconds`: combat 250 ms … planning 2,500 ms).
  - Fidelity scaling by distance to a human and by population (`AutonomousFidelityPolicy.cs:30-66`: Efficient ×2.6,
    and travel and planning slowed up to +80%/+60% from 300 to 1,500 bots).
  - Stable per-bot offsets that prevent synchronized spikes.
- These are rate limits, not a budget. Nothing stops one expensive turn.

### 1.3 Where the per-tick cost goes (code structure) [V]

- **Brains:** brains think in parallel (`NpcService.cs:46`, `GameLoop.ExecuteForEach`). Each turn is timed only when
  it exceeds `LongTickThreshold` (`NpcService.cs:68-83`, feeding `AiTurnStallMonitor` / `SERVER_STALL`).
  `NavQueryProfile` attributes navigation time inside the turn.
- **Coordinator:** before the brains run, `AutonomousBotGroupCoordinator.PrepareCoordinatorTick`
  (`AutonomousBotGroupCoordinator.cs:180-223`, called from `NpcService.cs:30`) runs **serially on the game loop**:
  - `RealmEventNotices.Pulse`, `AutonomousLivingServer.Pulse`, `RealmEventControls.Pulse`;
  - `AutonomousRealmRaid.Pulse` and `RecruitForcedParty`;
  - `AutonomousEpicSpecialForces.Pulse`, `AutonomousRvrEventLayer.PulseDefense`, `RealmWarbandSupport.Pulse`;
  - `AutonomousObjectiveAssignments.ReconcileIfDue`, then `FormSiegeParties` and `TryFormGroups` under a global
    `lock (Sync)`.
- **Serial cost [I]:** any cost there is a direct tick-length cost, and it is not parallel.
- **Contention:** the per-bot `AutonomousBotGroupCoordinator.Pulse(bot)` takes the same global `lock (Sync)` from
  parallel brain threads (`:236-240`). The lock counts are 37 in the group coordinator, 40 in `AutonomousRealmRaid.cs`
  and 35 in `AutonomousRvrEventLayer.cs` [V, grep]. Contention grows with bot count [I]. It is not measured [V].

### 1.4 Planned systems that add cost, and their budgets

Budget numbers are missing for all six [V]:
- **Where I looked:** `docs/BOT-CHAT-FRAMEWORK.md` (research branch) mentions a "rule budget" (:93) and an advice
  "error budget" (:131), but no CPU or time budget.
- **What exists elsewhere:** LIVE-EVENTS-ENGINE §5 has bot and monster caps but says the tick cost is unknown.
- **Nothing found:** I found no time or CPU budget for crafting either.

| Planned system | Cost driver [I] | Scales with |
|---|---|---|
| Chat rules engine with facts (BOT-CHAT-FRAMEWORK §3) | Query of world facts, speaker memory and social facts for each candidate speaker per chat concept; fact write-back with expiry | (chat events) × (candidate speakers) × (rules matched) |
| Social-facts log (§3.3) | Append per labelled event; queries by (actor, target, label); expiry sweep; **SQLite writes** | events/hour × bots; table size over months |
| RvR perception (PR #53) | Per RvR bot per ~1 s scan: up to 8 LOS checks (existing) + per-target roll + `ConcurrentDictionary` memory; spotter fan-out to group + warband | RvR bots × visible enemies; the memory holds (bot, target) pairs for 15 s |
| Remarkables (§3.8) | Per-area detection of a notable thing + cooldown table | areas × remarkables |
| Live events director | Serial pulse in `PrepareCoordinatorTick`; wave spawns; route probes | One major event per realm, but spawns are bursty: "the worst tick during a wave spawn" is the number that matters (ENGINE §5) |
| Crafting simulation | Bot crafting ticks, Exchange listings, valuation at town visits | crafters × recipes; the economy pulse |

### 1.5 What to measure before 1.2 grows

Measure these first, per system per minute (§9 has the implementation):
- **Tick duration:** p50, p95, p99 and max of the game-loop tick, split into serial coordinator time and parallel
  brain time.
- **Brain cost:** per-brain-type think cost (calls and total ms), plus `NavQueryProfile` kinds, at 300, 600 and
  900 per realm.
- **Contention:** wait time on the coordinator, raid and RvR `lock (Sync)`.
- **SQLite:** write latency and queue depth.
- **Growth:** Gen2 heap, handle count and DB file size per hour.
- **Chat:** lines per minute per channel per realm.

---

## 2. Persistence and data

- **The durability setting:**
  - **[V]** The default connection string is
    `Pooling=False;…;Journal Mode=Off;Synchronous=Off;…;Default Timeout=60` (`GameServerConfiguration.cs:386`).
  - **[V]** Setup's world import ends with `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL`
    (`OfflineDaoc.Setup/Program.cs:852`).
  - **[V]** `SqliteObjectDatabase` has a WAL path that is used only when the connection is pooled
    (`SqliteObjectDatabase.cs:735-775`).
  - **[I]** Which string the player's `runtime/server/config/serverconfig.xml` really carries is not in git
    (`assemble_release_037.py:21` skips it).
  - **[I]** If it is the default, a server with `Pooling=False` and `Journal Mode=Off` re-issues
    `journal_mode=OFF` on each open. That can move the file out of WAL, and with journal off a crash mid-transaction
    can corrupt the database. This is SQLite's documented behaviour for journal OFF and synchronous OFF.
  - **Status:** unverified on a real install. It is the first thing to check on the owner's PC.
- **[V]** No routine backup, `integrity_check` or `wal_checkpoint` exists in the server (grep).
  - `install-server-build.ps1` backs up binaries, not the database.
  - Setup's spawn-profile migration does make a consistent backup (`Program.cs:26, 146-152`), as do the world
    scripts (`fix_quest_reward_stats.py:57`) and `set_bot_guilds.py`.
- **Growth:**
  - **[V]** These are capped per bot: 8 killers per bot (LIVING-SERVER.md:144) and 40 bonds per bot (:226).
  - **[I]** These are not shown to be capped: the social-facts log, crafting ledgers and per-player memory.
  - **[E]** At 2,700 bots the existing caps give roughly 130k memory and bond rows. That is fine, but
    "months of play" needs a measured MB/day.
- **"database is locked" history [V]:**
  - Production waits 60 s (`GameServerConfiguration.cs:386`).
  - The CI flakes are detailed in §8.6. The production write path serializes behind `WriteSerializationLock`
    (`SqliteObjectDatabase.cs:752-760`), and its comment says bot status and inventory batch writes take "hundreds
    of ms".
  - 103 SQL queries over the time budget in six hours (LOG-ERROR-BACKLOG.md:174).
- **Separate event ledger:** `realm-event-records.sqlite3` lives in `AppContext.BaseDirectory` (`runtime/server`),
  not in `runtime/data` (`RealmEventRecords.cs:19`).
  - **[I]** Backups and progress transfer that copy only `runtime/data` miss it.

---

## 3. Reliability

- **Watchdog [V]** (`AutonomousStuckWatchdog.cs`, 711 lines):
  - A bot that doesn't move for 15 minutes, or has no goal outcome for 45, is teleported to a capital.
  - Raids, RvR events and groups register exemptions (`:290, :371, :405-414`).
  - **[I]** Every new event type must remember to register an exemption. Forgetting one silently teleports
    participants.
- **Crash isolation [V]:**
  - A brain exception is caught per brain (`NpcService.cs:88-91`).
  - Coordinator maintenance failures retry after 5 s (`AutonomousBotGroupCoordinator.cs:216-222`).
  - The "Priority 1: crash in the bot AI" fix awaits "the next long run for a repeat" (LOG-ERROR-BACKLOG.md:19-24).
- **Long-session leaks:**
  - **[V]** The handle growth of about 1,700 an hour is left open.
  - **[V]** The deleted-object holder sweep is a mitigation, not a root fix.
  - **[I]** Each new system that caches `GameLiving` references (perception memory keys by id, which is good; chat
    facts may not) can bring the retained-object class back.
- **Open known issues [V, KNOWN-ISSUES.md]:**
  - Hibernian raiders stuck 33 minutes at Cruachan Gorge (cause unknown).
  - Fomor City has no navmesh path.
  - Bots don't fall when they die.
  - The 1.1 quest defects (QUESTING.md, 14 rows).
  - Sieges checked only at about 1,000 bots on the test copy.
  - The original client wasn't tried.

---

## 4. Delivery

- **Installer [V]** (`tools/fork-build/install-server-build.ps1`, 320 lines):
  - Checks the game against `PACKAGE MANIFEST.sha256` and hash-identifies every `GameServer.dll` and launcher.
  - Backs up to `deployment-backups/fork-build-<time>[-n]` and verifies copies by SHA-256.
  - `ROLLBACK.cmd` restores.
- **Content patches [V]:** `content-patches.json` holds 11 binary patches. Each has `Released` and `Fixed` SHA-256
  values and copy/data delta ops, so a patch applies only to the exact released file. This is good practice.
- **Rollback leaves data [V]:** KNOWN-ISSUES says rollback "doesn't remove saved data", and the rollback script says
  "Settings it switched on stay in the database" (`rollback-server-build.ps1:182`).
- **Base world scripts [V]:** `fix_quest_reward_stats.py`, `enable_necromancer_creation.py` and
  `remove_sluaghbinder_npcs.py` are in `tools/claude-version`.
  - Neither the installer nor rollback calls any of them (grep).
  - The two quest and Necromancer scripts roll back only by a whole-database copy taken before they run.
  - **[I]** Restoring that copy loses every save made since.
  - **[I]** RELEASE-TRAIN 1.2 wants them "as Setup steps with rollbacks". That needs row-level inverse scripts, like
    `epic_chains.py rollback` does (`epic_chains.py:21-25`).
- **New-client scripts [V]:**
  - The 0.37c client runs GDScript from source, so script fixes ship as content patches.
  - `/target` filling the target window needs a rebuilt `MMOEngine.dll` (RELEASE-TRAIN 1.2), which is undecided.
  - PR #54 (14 scripts) is open and in draft. **[V, audit claim checked only by metadata]** The audit says it has an
    APPRAISE error. I did not run it.
- **Setup's fallback copy of the base files [V]:** this is PR #51 (`feature/setup-base-mirror`, draft, base `main`).
  - **[I]** It turns Living Server into a redistributor of shadowofze's packages, including the copyrighted client.
  - That is an owner and legal decision, not a technical one. I agree with the audit here.
- **Upgrade 1.1 → 1.2 [I]:**
  - The binaries are covered by the hash-identified install.
  - The world-data half (his scripts, epic chains, release-world defaults) has no combined, idempotent
    "migrate to 1.2" step with an inverse.

---

## 5. Testability

- **CI coverage [V]:**
  - CI builds and runs about 4,089 server tests on `windows-latest` (PR #56 job log: "Passed: 4088 … Total: 4089").
  - It excludes 47 tests that need installed game files (`.github/ci-excluded-tests.txt`).
  - It does **not** run on pushes to `main` (`build-and-test.yml`: `branches-ignore: [main]`); PRs into main do run it.
- **Tests that need the owner's files [V]:** navmesh and world tests read `OFFLINE_DAOC_NAV_ROOT` and skip
  otherwise (e.g. `UT_KeepDoorNavmeshProbe.cs:29`, `UT_SiegeFiringPositions.cs:39`).
- **What can't be tested without the owner's PC [V]:** anything that needs the world DB, the navmeshes, Detour, a
  running server with bots, or either client. That covers quests, raids, sieges, perception in real terrain, chat at
  scale and soak behaviour.
- **Precedent for closing the gap [V]:** SE4-CHECKS.md §5 records a **live headless test**.
  - The server ran on Linux on a copy of a played DB (150 bots), with a scripted player made from the new client's
    network code.
  - The scripted client and that DB are not in the repo [V, ls-tree].
  - `quest_driver.gd` exists, but its `plan.json` and results are not (QUESTING.md:36).
- **What closes the gap:** see challenge #1 (§8.1).

---

## 6. Code health

- **Size [V]:**
  - `bots/` is 333 files and 88,445 lines.
  - `AutonomousWorldBotController.cs` is 5,855 lines; with its 12 partials it is 9,149.
  - `BotBrain.cs` is 5,812, `GameBot.cs` 5,042, `AutonomousBotGroupCoordinator.cs` 3,271 and
    `AutonomousBotChatCoordinator.cs` 1,817.
- **Coupling [V, grep]:** the controller references 99 distinct `Autonomous*`/`Realm*`/`Bot*` types, `BotBrain.cs`
  59, the group coordinator 46 and the chat coordinator 31.
- **Implicit arbitration [V]:** behaviour is arbitrated by **statement order** in `AutonomousWorldBotController.Tick`
  (`:286` on, more than 30 early returns; see §8.3). Reordering one `if` changes who wins.
- **Test coverage [V]:**
  - 343 test files and 48.5k lines; 226 test files touch Autonomous, BotBrain or Gamebot code.
  - Coverage is mostly pure-rule unit tests, which is good, plus a few navmesh probes that need installed files.
  - None drives a bot through the world loop end to end [I, from §5].
- **Chat rules-engine rewrite risk:**
  - **[I]** It replaces the chat coordinator, short talk, threads, questions and live answers (more than 4,000 lines
    of tuned behaviour).
  - **[V]** The blind evaluation (PR #56, open) measured a 53% intent-read rate against 95.6% on the authors'
    own sets.
  - **[I]** That justifies the rewrite, but it must be gated by the blind set *and* by a cost budget, and run beside
    the old system behind a switch until the blind score beats it.

---

## 7. Risks ranked for reaching a peak-server feel

| # | Risk | Level | Label |
|---|---|---|---|
| 1 | **No end-to-end proof of player journeys; the world isn't in CI.** Quests, raids, sieges and RvR are certified only by about 5 h/week of play | High | V |
| 2 | **No enforced AI or tick budget; no per-system cost metrics.** Six cost-adding systems are planned; the last four stalls were found by luck | High | V |
| 3 | **SQLite durability:** journal off and sync off by default, no routine backup or integrity check, an event ledger outside `runtime/data`. One power cut can end a months-long world | High (until the owner's config is checked) | V code, I effect |
| 4 | **Competing goal sources arbitrated by code order** (raid reservations, RvR event forces, siege, watchdog, schedules, player invites, each with its own registry). Every new system (events, guild duty, crafting) adds a claimant | High | V |
| 5 | **PR #53 perception still reads as radar at real densities** (§8.2) | Medium | V by simulation of the real formula |
| 6 | **Serial coordinator pulses plus global locks** grow with population and with each new pulse (events director, crafting) | Medium | V structure, I cost |
| 7 | **Event state not durable:** in-memory raids, sieges and Special Forces; restart = "Interrupted" | Medium | V |
| 8 | **Release governance:** main unprotected, no rulesets, CI skips main pushes, README drift | Medium | V |
| 9 | **1.1→1.2 world-data migration without row-level rollback** (his scripts back up the whole DB) | Medium | V/I |
| 10 | **Handle growth (1,700 an hour) and retained-object regressions** over multi-day sessions | Low | V |
| 11 | **Size and coupling of the controller and BotBrain** slow every change and make reviews shallow | Low–Medium | V |

---

## 8. The audit's challenges #1, #2, #3, #6, #8, #9

### 8.1 Challenge #1: Which player journeys are proven end to end, and what harness would prove them?

**What exists [V]:**

| Journey | Automated end to end? | Played? |
|---|---|---|
| Fresh install, login (a new account at player privilege) | Yes, but Windows-only, run by hand: `source/tools/smoke_release_037.py` (copies the package, starts CoreServer, logs in via `connect.exe`, checks the account, shuts down) | Yes |
| Install, re-run, remove, reinstall, 0.36 update | Yes: `.github/workflows/installer.yml` (real downloads, e2e matrix client/resume/import/ui) and `tools/fork-build/test-install-scripts.ps1` | Yes |
| Chat replies to a player | Partly: SE4-CHECKS §5 headless sessions (scripted client not in repo); `BotChatTranscript` simulator tests | Yes |
| Levelling 1–5 with a bot group, guild recruit | No | Once (3-hour Scout playtest, KNOWN-ISSUES) |
| A quest (kill in a bot group, delivery, epic step) | **No.** "nothing drives `AdvanceQuestStep`, `FinishQuest`, hand-ins, kills" (QUESTING.md:35-36) | **No** (KNOWN-ISSUES: "have not been played in game") |
| Raid (form → muster → travel → engage → loot) | No (rules unit-tested) | Yes, several (KNOWN-ISSUES "Raids") |
| Siege with running timers | No | Only on the test copy at about 1,000 bots |
| Save → restart → state intact | No | Incidentally |
| Rollback restores stock behaviour | Install scripts only; the gameplay A/B is planned (RELEASE-TRAIN 1.1.1) | No |
| Original 2003 client | No | "wasn't tried" |

**Verdict [V]:** the only journeys proven end to end by automation are install, rollback and login. **I agree with
the audit's priority.**

**Where I disagree:** the audit asks the harness to "test the original and new clients independently". That is the
wrong first target.
- Driving the 2003 `game.dll` needs Windows UI automation, and it is brittle.
- **[I]** Both clients send the same protocol to the same server, so a protocol-level scripted client covers about
  90% of the journey risk.
- One weekly Windows smoke on the real client covers the rest.

**Missing:** a shareable world fixture, a protocol-level scripted player in the repo, scenario scripts, and a report.

**Smallest practical implementation:**
1. **World fixture builder** (`source/tools/make_test_world.py`).
   - Start from the clean 0.37 reference DB that `build_release_037.py` already produces. It "empties every
     saved-progress table, restores the default world … and verifies it" (AGENTS.md), so it contains no personal
     saves by construction.
   - Subset it to the fixture zones (§8.1 fixtures) and stamp a SHA-256.
   - Store it as a CI artifact or private release asset, **not in git**. **[I]** It is shadowofze's and the
     community DB's world data, so the owner decides whether it may be redistributed. A private, token-gated asset
     avoids publishing it.
2. **Headless scripted player**: a .NET console project, `source/tools/OfflineDaoc.TestClient`, that reuses the new
   client's C# network layer (as SE4 §5 did) with a scenario DSL:
   `login; create Albion Scout; goto x,y; target npc; interact; say; group invite <bot>; kill <mob>; expect quest step
   2; logout; restart server; login; expect …`.
3. **Harness runner**: start the server on the fixture with `OFFLINE_DAOC_NAV_ROOT` pointing at the fixture's
   navmeshes, a port other than 10300, and bots seeded by the launcher's +Lv buttons logic. Run the scenarios and
   write `report.md` (pass/fail, the timeline of `REALM_RAID_*`/`QUEST_*`/`SERVER_STALL` lines, worst tick).
4. **CI:** a nightly `workflow_dispatch` job on `windows-latest` (Detour is Windows), plus a Linux job without
   navmesh for chat and quest-logic scenarios, as SE4 did.

**Fixtures needed (all from the clean release world, no `offline_*` saves, no account):**
- **Two zones in one realm:** Camelot Hills plus Cotswold (Albion), with their navmeshes, quest givers and targets
  for three quests (a kill, a delivery, one epic step) and the epic chain rows.
- **One frontier zone:** Hadrian's Wall or Emain, plus one keep, for sieges and perception.
- **One dragon lair:** Golestandt, for the raid scenario. Roughly a few hundred MB with navmeshes [E].
- **A seeded roster:** 60 bots per realm generated at test start from fixed seeds, not shipped.

**Risks:**
- Licensing of world data and navmeshes (an owner decision).
- Windows runner time: about 45 to 90 minutes a night [E].
- Flakiness from real-time bot AI. Mitigate with fixed seeds, generous timeouts and an "expect within N s" DSL.

**Automated tests:** the scenarios above plus a save/restart/rollback scenario. Each scenario must pass three nights
in a row before it gates a release.

**Gameplay acceptance:** the three-quest A/B from RELEASE-TRAIN 1.1.1 played once by the owner matches the harness
verdict. If they disagree, the harness is wrong and gets fixed first.

**Release:** the fixture and the scripted player in **1.2**; the quest scenarios gate the 1.3 questing items.

### 8.2 Challenge #2: PR #53 perception, simulated

**What exists [V]** (`wt-perception`, commit `204d8216`):
- **Rule:** `AutonomousRvrPerception.cs` (normal profile).
  - The chance is always 1.0 within 1,000 units and 0 beyond 3,500.
  - Between them it is `0.03 + 0.47·(1−t)²` (`:120-139`).
  - Multipliers: combat ×2, casting ×1.5, a group of 3 or more ×1.5, facing ×1.75 (120° cone), standing still ×0.5;
    capped at 0.95.
- **Re-rolling:** every scan re-rolls until noticed. `Memory.Seen` returns `Unnoticed` and the bot rolls again
  (`AutonomousWorldBotController.RvrPerception.cs:67-79`).
- **Scan rate:** one scan per bot every 1,000 + (id % 250) ms (`AutonomousFrontierThreatPolicy.Due`, `:37-42`).
- **Line of sight:** LOS is checked before the roll, and only for the **8 nearest** candidates
  (`VisibilityBudget = 8`, `:8, :46-59`). Stealthed targets are never eligible (`FrontierThreat.cs:147`).
- **Gating that already exists:**
  - Bots in combat, with aggro, passive, temporary helpers, in a player-led group, on stable routes or in safe areas
    don't scan (`FrontierThreat.cs:136-141`).
  - Defenders and siege fighters skip perception entirely (`PerceiveRvrTargets(..., defending || siegeFighter)`).
- **Spotter sharing:** the spotter alerts **its own group and warband** within 2,500 units only (`:95-139`). There
  is no realm-wide broadcast.

**Does 1−(1−p)^N apply?**
- **Yes [V].** Each eligible bot rolls independently; there is no per-target observer cap and no group-level roll.
  At the edge the real chance is 3.08% (5.38% if facing), so the audit's per-scan table is right.
- **The audit understates the problem [V, sim].** Because bots re-roll about once a second, the true quantity is a
  per-second hazard. Even **one** bot at the edge notices within 30 s 66% of the time.
- **Where the audit overstates [V code, I effect]:**
  - The 8-nearest LOS budget means a player behind eight nearer enemy bots is **not checked at all**, so in big
    fights the bias runs the other way.
  - Fighting, aggroed and objective-holding bots don't scan.

**Simulation** (my Monte Carlo of the exact code formula; 1,500 trials per cell):
- Script: `/home/claude/north-star-audit/perception_sim.py`. Output: `/home/claude/north-star-audit/perception_sim_out.md`.
- The player is solo, moving, not fighting or casting. Facing is drawn per scan with p = 1/3.
- The cells show P(at least one bot notices) at 1 / 5 / 10 / 30 s, and the share of bots engaging.

| Scenario | N=1 | N=10 | N=20 | N=50 | N=100 |
|---|---|---|---|---|---|
| Edge (3,400), bots ungrouped: first notice | 4/18/32/66% | 29/82/97/100% | 51/96/100/100% | 83/100/100/100% | 97/100/100/100% |
| Edge, groups of 8 (spotters): **share engaging** | 0/15/29/64% | 2/60/83/97% | 2/65/89/100% | 2/66/90/99% | 3/67/91/100% |
| Spread 1,000–3,500, groups of 8: first notice | 17/50/67/89% | 83/100/100/100% | 97/100/… | 100% | 100% |
| Spread, groups of 8: share engaging | 2/44/65/88% | 10/93/97/100% | 10/97/100/100% | 11/98/99/100% | 11/99/100/100% |
| Spread, 50% without LOS: share engaging | 1/20/30/43% | 5/77/87/93% | 5/82/92/97% | 6/85/94/98% | 6/85/95/99% |
| Player runs in at 191 u/s from 3,500: share engaging | 0/23/75/100% | 3/77/99/100% | 3/82/100/100% | 3/84/100/100% | 3/84/100/100% |
| **Scout model** (one roller per 8, relay 50%/4 s): first notice | 17/50/67/89% | 29/74/90/99% | 41/86/96/100% | 70/99/100/100% | 90/100/100/100% |
| Scout model: share engaging | 2/44/65/88% | 1/38/69/86% | 2/33/71/85% | 1/24/80/89% | 1/21/86/92% |
| **Observer budget** (3 best-placed rollers per target, relay 50%/4 s): share engaging | 2/44/65/88% | 1/20/75/79% | 1/14/73/75% | 0/9/71/72% | 0/9/71/71% |

**Reading [I]:**
- **What PR #53 fixes:** it removes the *instant* sighting. At 1 s at most 11% of bots engage, against 100% before.
- **What it doesn't fix:** with 10 or more bots spread inside 3,500 units, 93–99% of them engage within 5 s. That is
  still "everyone comes at once", only 3 s later. The cause is per-second re-rolls plus whole-group alerts.
- **The scout model** caps the *first* notice at high N only modestly.
- **An observer budget** bounds how many bots act and spreads engagement over 5–10 s. That is closer to how a real
  zerg drips in.
- **What none of these change:** whoever is within 1,000 units always notices.

**Where I disagree with the audit:**
- A full "hierarchical realm awareness" layer is more than 1.2 needs. Two small changes get most of the benefit.
- The audit's scenario lists "false positives". The code can't produce them: it never notices something that isn't
  there. A "false contact" (a bot calls "inc" at a rustle) would be a new feature, not a defect.

**Smallest practical implementation (on PR #53):**
1. **Observer cap per target:** at most k (3 normal, 2 easy, 5 veteran) independent rollers per (target, realm, 1 s
   window). The rest learn only through calls. One `ConcurrentDictionary<(target, realm), (window, count)>`.
2. **Calibrate the hazard, not the scan:** state the target as a median time-to-notice by distance. For example:

   | Distance | Median time to notice |
   |---|---|
   | 1,500 units | 1.5 s |
   | 2,500 units | 6 s |
   | 3,400 units | 20 s |

   Derive p per scan from the scan interval so the numbers don't drift if scan cadence changes.
3. **Partial response to a call:** an alerted mate engages only if its current goal allows it (roles from §8.3:
   healers hold, an objective holds). Otherwise it turns to face the target, which raises its own roll through
   the facing factor.
4. **Logs:** `RVR_NOTICE` and `RVR_SPOT` already exist. Add `time_since_first_los_ms` so the curve can be measured
   live.

**Risks:**
- Cost stays O(RvR bots × 8) per second (unchanged).
- The observer dictionary is tiny.
- Making bots too blind is the gameplay risk, so keep the three profiles.

**Tests:**
- Port the simulator into `UT_RvrPerception` as a seeded statistical test: N = 1, 10, 50, groups of 8. Assert that
  the share engaging at 5 s stays below a target and that the median time-to-notice per distance band falls within
  ±30%.
- A unit test for the observer cap.

**Gameplay acceptance:**
- A solo player crossing a 3,000-unit sightline past a 3-group camp is engaged by fewer than half the camp within
  10 s, and the rest drip in.
- A player who stops out of sight at 2,000 units is often unseen.
- In `/group` you hear "inc" before you're hit.
- Logged medians match the target table.

**Release: 1.2** (PR #53 plus these three changes). The realm-awareness layer goes in 1.3 with the director.

### 8.3 Challenge #3: Independent goal selectors and competing orders

**Map of who can issue an order to an autonomous gamebot [V, file:line]:**

| Source | Mechanism | Own registry |
|---|---|---|
| Durable objective (SoloPve, GroupPve, RvR, …) | `AutonomousObjectiveAssignments`, `AutonomousBotGoalPolicy` (they write `ObjectiveAssignmentId`) | DB record |
| Group sessions and directives (meetup, camp, regroup, dungeon entry, siege parties, forced raid parties) | `AutonomousBotGroupCoordinator(.SiegeParties, .ForcedRaid)`; `Pulse(bot)` → `_groupDirective` | `Sessions` under `lock (Sync)` |
| Realm raids (recruit, reserve, muster, travel, engage) | `AutonomousRealmRaid` + `RealmRaid*` | `Reservations` (`AutonomousRealmRaid.cs:194-204`) |
| Epic Special Forces | `AutonomousEpicSpecialForces.Pulse` | in memory |
| RvR events (rally, defense, relic escort, forced siege) | `AutonomousRvrEventLayer(.Defense, .Relic)` | `IsForceCommitted`, `IsLiveArmyMember` |
| Siege jobs | `BotSiegeRuntime`, `AutonomousRvrDefense.IsCommittedDefender/SiegeFighter` | runtime flags |
| Frontier threat engage | `TryEngageFrontierThreat`, called from `BotBrain.cs:1404` **before** the controller tick | none |
| Controller priority chain | `AutonomousWorldBotController.Tick` (`:286` on): pockets → RvR release → group pulse → stable travel → relic carrier → shared dungeon opponent → combat → DF evacuation → dungeon entry → solo death → raid stable travel → foreign frontier → capital transit → raid expedition → RvR destination → regroup → rendezvous → objective | statement order |
| Stuck watchdog | `AutonomousStuckWatchdog` teleports (`MoveTo`) unless exempted (`:290, :371`) | exemption calls |
| Teleports outside the controller | `MoveTo` in FrontierTransport, CapitalRouting, Battleground, TownTeleport, TemporaryGroupStableTravel, PlayerPilot, GameBot | none |
| Schedules and logout | Living Server schedule (LIVING-SERVER.md:159-170) | conditions in code |
| The player | `/invite` → `BotGroupInvite.DutyOf` (`BotGroupInvite.cs:12-27`: Dead > Siege > Relic > RvrEvent > RealmRaid > StableRoute > Helper; Begging) | derived |
| Economy, beggars, guild social and planner | town visits, begging corner, guild duties | own state |

**Where two can collide [V structure, I instances]:**
- A raid reservation against a group session. The raid recruits "level 50s waiting for a group and … grinding solo"
  (`AutonomousRealmRaid.cs:210-213`).
- RvR event forces against the durable RvR objective (`Tick` lines 25-26, 272-281).
- Frontier engage (it runs before `Tick`) against raid travel or stable travel.
- The watchdog against any new event without an exemption, which LIVE-EVENTS-ENGINE §9 flags.
- A Special Forces call against a raid's own member list.
- `/invite` against all of the above. `DutyOf` is the only place that names a priority, and it is a *reply*, not an
  arbiter.
- Planned for 1.3/1.4: event director recruitment, guild duty and crafter commissions. Each would add a fourth or
  fifth recruiter.

**Where I disagree with the audit:** a four-layer architecture with utility scoring is a rewrite of 9,000+ lines of
tuned behaviour. It isn't needed to stop the collisions. The collisions are about **ownership of a bot's time**, not
about scoring.

**Smallest arbitration layer: a duty lease ledger.**
- **The ledger:** `AutonomousDutyLedger` with
  `Claim(botId, owner, priority, leaseMs, resumeToken) → bool`, `Renew`, `Release` and `Current(botId)`.
- **Priorities:** fixed, extending the existing `Duty` enum: Combat-retaliation > Relic > Siege > RvrEvent >
  RealmRaid > LiveEvent > Group > Objective > Social/Economy.
- **Recruiters claim first:** raid, RvR event, siege, Special Forces, live events and the guild call `Claim` before
  recruiting. Raid `Reservations` becomes a view on the ledger.
- **Who reads it:**
  - `Tick` asks `Current(bot)` once at the top and dispatches to the owner's handler, leaving the existing chain in
    place for "no lease".
  - The watchdog exempts any bot with a live lease, so no per-event exemption is needed.
  - `DutyOf` derives from it.
- **Interrupt and resume:** a higher-priority claim stores the lower lease's `resumeToken`. On release, the
  lower-priority owner gets `Resume(bot, token)`, and the existing objective re-plans from the token.
- **Reason codes:** every claim, reject and preempt logs `DUTY bot= owner= prio= reason=`.

**Cost [E]:**
- One `ConcurrentDictionary<long, Lease>` lookup per bot tick: about 20–50 ns, so about 0.1 ms per second at
  3,000 bots.
- Memory: about 100 B × bots (about 300 KB).
- Migration: one recruiter at a time, starting with raids and RvR events.
- No behaviour change while only `Current` is read.

**Risks:**
- Leases that leak if an owner crashes. Use lease expiry plus a sweep.
- Priority mistakes. Keep them in one table with tests.

**Tests:**
- A pure ledger test: preempt and resume, expiry, and two recruiters racing.
- A property test: no bot has two owners.
- Replay tests of the known collisions: a raid recruit during a group meetup, and a watchdog during an event.

**Gameplay acceptance:**
- **Logs:** no `AUTONOMOUS_GROUP_MATCHMAKING_TIMEOUT` caused by a raid stealing a member, and no watchdog teleport
  of a bot under a lease. Both are visible in logs.
- **Resume:** a raid recruit who was grinding returns to its camp after the raid.

**Release:** the **1.2** ledger with raids and RvR on it; **1.3** live events claim through it from day one.

### 8.4 Challenge #6: The real AI budget

**What exists [V]:**
- Think intervals, fidelity and population scaling (§1.2).
- `AiTurnStallMonitor` and `SERVER_STALL` for slow turns, and `NavQueryProfile` for navigation attribution.
- **`AI_BUDGET_MS` has no production caller, and neither does `MaximumDecisionsThisTick`.**

**Missing:**
- Any enforcement.
- Per-system cost attribution for normal (non-slow) turns and for the serial coordinator pulses.
- p50, p95 and p99 tick durations.

**Likely ten most expensive autonomous systems.** These come from the code and the recorded incidents, so the
ranking is **[I]** and must be confirmed by measurement:
1. **Pathfinding and route planning:** `AutonomousStableRoutePlanner`, `RealmRaidDungeonRoute`,
   `AutonomousZoneItinerary`, `AutonomousRvrRally.HasRoute` (issue #25, 1.0–1.3 s), floor searches. NpcService
   190–270 ms on navigation.
2. **Frontier threat scan:** a 5,000-unit radius player and NPC query per RvR bot per ~1 s, up to 8 LOS raycasts,
   and the area lookups (64 MB per 40 s before the fix).
3. **`AutonomousWorldBotController.Tick` chain:** about 30 checks per bot per think, many of them lookups in other
   subsystems' locked registries.
4. **Group coordinator:** `Pulse(bot)` under a global lock, plus `TryFormGroups` and `FormSiegeParties` serially in
   `PrepareCoordinatorTick` (117 MB per 40 s before the fix).
5. **Combat AI in BotBrain:** spell and style choice, healing scans over group members, cast queue (the Lydsyg 37 GB
   case).
6. **Pets:** `AutonomousPetSupport` (2,142 lines), 292 `BOT_PET_STUCK_RECOVERY` in 6 h.
7. **Realm raid and RvR event pulses:** serial, recruiting by scanning online 50s (`OnlineLevelFifty`).
8. **Persistence:** bot status and inventory batch writes ("hundreds of ms", `SqliteObjectDatabase.cs:744-747`);
   103 slow SQL in 6 h.
9. **Chat coordinator:** listener and speaker selection per message, corpus lookups. It will grow with the rules
   engine.
10. **Economy and town visits:** valuation and merchant item scans (`AutonomousBotEconomy`, 1,129 lines), and the
    deleted-object holder sweep (60 ms every 2 min).

**Smallest practical implementation:**
1. **Profiler first, budget second.**
   - Add `AiProfile.Scope("system")` (the `NavQueryProfile` ThreadStatic pattern: calls and Stopwatch ticks per
     system) around the ten systems above, and around each pulse in `PrepareCoordinatorTick`.
   - Emit once a minute: `AI_PROFILE tick_p50= p95= p99= max= serial_ms= parallel_ms= <system>=calls/ms …`.
   - Cost [E]: two `Stopwatch.GetTimestamp` calls per scope, about 50 ns.
2. **A real budget on what can be deferred:**
   - Use a deferred-work queue for planning work: route plans, camp selection, raid recruitment scans, chat
     follow-ups. `MaximumDecisionsThisTick(recentTickMs, AI_BUDGET_MS, queue)` decides how many run each tick.
   - Combat, movement and retaliation are never deferred.
   - This makes the setting's description true.
3. **What runs when:**

   | Category | What it covers |
   |---|---|
   | Every tick | movement, attacks, casting, retaliation |
   | Interval (budgeted) | planning, regrouping, recruitment, economy, social |
   | Event-driven | chat (on message), perception alerts (on notice), memory writes (on group end) |

**Load-test plan (owner's PC, or a Windows cloud box with the fixture world plus the full navmesh set):**

| Run | Bots per realm | Duration | Mix | Pass criteria |
|---|---|---|---|---|
| L300 | 300 | 2 h | default goals, one raid forced at 30 min, one siege forced at 60 min | p99 tick < 100 ms; max < 500 ms; no `SERVER_STALL` > 1 s |
| L600 | 600 | overnight (8 h) | same, plus a player client scripted in the frontier (perception on) | p99 < 150 ms; heap flat ±10%; handles < 2,000 an hour; DB growth measured |
| L900 | 900 | overnight | same, plus `/lstest` chat burst at hour 2, three-realm RvR | p99 < 200 ms [E target]; no bot "stood at 0 HP"; chat lines per minute per channel recorded |

- **Discipline:** one system changed per run (RELEASE-TRAIN rule).
- **Output:** the timeline report (LIVE-EVENTS-ENGINE §6.3) plus `AI_PROFILE` → a table of the top ten by ms per
  minute.

**Where I disagree with the audit:**
- It demands "a real server-wide AI scheduler" before measuring. Measure first: the profiler is about a day of work;
  the scheduler is a week and may target the wrong system.
- It says 900 per realm overnight is "proposed" by the roadmap. The roadmap's soaks are at 600 per realm, and
  1,000 has been seen only on the test copy [V].

**Risks:**
- Profiler overhead. Sample 1 in N turns if needed.
- A budget that defers planning too much makes bots look idle. Cap the deferral at 3× the interval.

**Tests:**
- Unit tests for the deferral queue: combat never deferred, and fairness.
- A CI micro-benchmark of the frontier scan and the `Tick` chain using fake worlds, with regression thresholds.

**Gameplay acceptance:** at 900 per realm, in a raid and a siege, no visible stalls ("killed bots stood at 0 HP" is
the canary), and the group meets up within the usual time.

**Release:** the profiler and L300/L600 in **1.2**, before the base merge's soak week. The budget enforcement goes in
**1.2** if L600 shows serial or planning time above 25% of the tick; otherwise in **1.3** with the director.

### 8.5 Challenge #8: Do events have durable state and restart recovery today?

**What exists [V]:**
- `RealmEventRecords` (`RealmEventRecords.cs`) is a **write-behind history ledger**: `Begin`, `Progress`, `Finish`,
  flushed every 2 s in batches of 128, with a 4,096-row backlog cap.
- It is stored in `realm-event-records.sqlite3` (`RealmEventRecordStore.cs:27`: `Events(Id, EventId, …, Phase,
  Outcome, …)`).
- On a clean stop, active events are finished as "Interrupted" (`:56-60`).
- On the next start, any row still "In progress" is closed as Interrupted (`RealmEventRecordStore.CloseInterrupted`,
  `:40-45`).
- The Special Forces read past outcomes from it (`AutonomousEpicSpecialForces.cs:140`).

**What is not durable [V]:**
- Raid state (`Raids`, `Reservations`, `Membership`, `Bosses`, `CompletedLoot`, and `LastSuccessful` at
  `AutonomousRealmRaid.cs:181-195`) is in memory only.
- So are RvR event forces and Special Forces ("in memory only, never saved", LIVE-EVENTS-ENGINE §2).
- **[I]** A restart abandons every running raid and siege. `LastSuccessful` is lost, so the "don't repeat the one you
  just won" rule resets.
- **[I]** Keep ownership and relic positions persist through the base game's own tables.

**Answer:** durable *history*, yes; durable *state* and *resume*, no.

**Where I disagree with the audit:** the audit asks to "prove the architecture with a restart/recovery simulation
before building more event content". For a single-player server, **resume is the wrong default**.
- **[I]** A raid's bots, positions and boss health can't be faithfully restored, and a resumed half-dead raid feels
  worse than a clean "Interrupted" with a retold line.
- What does need durability is three things:
  - the outcome and its consequences (the ledger already holds them);
  - the director's *schedule and cooldowns* (so a restart doesn't re-fire or skip);
  - idempotent world mutations (spawns, occupation and keep changes must not double-apply).

**Smallest practical implementation:**
1. **A state table:** `offline_event_state(event_id, realm, state, seed, started_utc, cooldown_until_utc,
   world_mutations_json, version)`, in the main DB (so backups and transfer carry it), not beside the binary.
2. **Explicit states:** Scheduled → Warning → Active → Resolving → Ended/Interrupted.
   - On start: anything Active becomes Interrupted.
   - Its `world_mutations_json` is undone (despawn the tagged spawns, restore services) before anything new runs.
3. **Determinism:** persist the seed; each mutation carries an idempotency key `event_id:beat:n`.
4. **Persist `LastSuccessful` and the raid cooldowns** in the same table (a one-line gap today).
5. **Move or mirror `realm-event-records.sqlite3` into `runtime/data`.**

**Risks:** a schema migration on the player's DB, which needs a backup and rollback (see §2); and undo logic that is
wrong and leaves spawns behind.

**Tests:**
- A rehearsal-simulator restart test: run to the middle of Active, "crash", restart, and assert no duplicate spawns,
  services restored, cooldown respected and the outcome written once.
- A unit test of the state transitions.

**Gameplay acceptance:** kill the server mid-event on the test copy. After restart:
- the Herald shows "Interrupted";
- no orphan monsters remain at the event site;
- the next event respects its cooldown;
- bots mention it ("server crashed mid raid lol") at most once.

**Release:** **1.3**, with live events M1. The `LastSuccessful` persistence and the ledger path can go in **1.2**.

### 8.6 Challenge #9: Release pipeline

**Verified state:**

| Item | Finding | Evidence |
|---|---|---|
| Main protection | **Not protected**; no rulesets | `gh api …/branches/main/protection` → `404 "Branch not protected"`; `…/rulesets` → `[]`; `branches/main` → `"protected": false` |
| CI on main | Pushes to main don't run `build-and-test` | `build-and-test.yml`: `push: branches-ignore: [main]` |
| Code owners | `* @chris4aker` | `.github/CODEOWNERS` (without protection it enforces nothing) |
| README drift | main is `15f521e0 Beta 1.1.1 (#47)`, but its README says "**Beta 1.1 is out.**" and links `v1.1-beta` assets (`README.md:18, 32, 121-135`) and "4,093 automated server tests" (:113) | `git show origin/main:README.md` |
| PR #48 (SQLite busy wait 2 s → 60 s) | **Merged** to main 2026-10-31 (`6833a9aa`); its commit `a58ce4c7` **is an ancestor of `origin/release/1.2`** and of PR #56's head | `git merge-base --is-ancestor` → yes for both |
| PR #56's base | `release/1.2` at `50ecb30d` (merge base of head `681ed890`), so PR #56 already has #48's fix | `gh api pulls/56`; `git merge-base` |
| The PR #56 failure | Not the busy timeout. The test body passed; the failure is `System.IO.IOException: The process cannot access the file '…\daoc-atomic-exchange-<guid>.sqlite3' because it is being used by another process` at `File.Delete` in the `finally` (`UT_AtomicExchangePersistence.cs:161-166`) | Job 124920974517 log, lines 1599-1605; the same commit passed on the push run 41331026280 |

**Root cause [I, strongly suggested]:**
- `SQLiteConnection.ClearAllPools()` closes only idle pooled connections.
- Connections still finalizing from the 480 `Parallel.For` workers, or a handle held by System.Data.SQLite's
  finalizer, keep the file open on Windows for a moment, so `File.Delete` races them.
- This is a **test-cleanup defect, not a data-integrity defect**. I agree with the audit that it "deserves
  investigation", and the investigation says it is cleanup.
- **[I]** It does hint that `SqliteObjectDatabase` may not dispose every pooled connection deterministically, which
  matters for the production WAL path. That is worth one check.

**Smallest fixes:**
- **The flaky test:**
  - In `DeleteTemporaryFile`, retry 10 × 100 ms with `GC.Collect(); GC.WaitForPendingFinalizers();` between tries.
  - If it still fails, leave the GUID file in `%TEMP%` with a warning. The test asserts behaviour, not deletion.
  - Better still, give each test its own temp directory, deleted in `[OneTimeTearDown]`.
- **Branch protection:**
  - Protect `main`: require the `build-and-test / server` check and a PR; disallow force-push and deletion.
  - Allow the owner to bypass. A solo maintainer needs that, so I disagree with the audit's "required review",
    which would block a single-person project.
  - Run CI on push to main too: remove `branches-ignore`.
- **README drift:** a CI check that `README.md`'s "Beta X is out" and release links match the version in the latest
  CHANGELOG header and `MainForm.DisplayVersion`. That is a 20-line script.
- **Release-state document:** RELEASE-TRAIN.md already is the release-state document. The gap is enforcement, not
  another document.

**Release-readiness matrix (for 1.2):**

| Item | State |
|---|---|
| Base merge | 1ac34065 merged into release/1.2 |
| PR #53 (perception) | Open, draft; unit tests only; needs §8.2 |
| PR #54 (new-client scripts) | Open, draft; not run in Godot |
| PR #55 (class builds data) | Open, draft (provisional) |
| PR #56 (chat evaluation) | Open, draft |
| PR #57 (chat and group frameworks) | Open, draft (design) |
| PR #51 (base-files mirror) | Targets **main**, not release/1.2 (a legal/owner decision) |
| PR #50 (1.1.2 candidate) | Open |

**Risks:**
- Protection can block the owner's hotfix flow. Use a bypass.
- The CI flake would block merges until it is fixed, so fix it first.

**Tests:** the README/version consistency script runs in CI; run the flaky test 50× in a loop job after the fix.

**Gameplay acceptance:** none (process).

**Release:** **1.1.2** for the flaky-test fix, protection and README check, before 1.2 work merges.

---

## 9. Budgets and measurements to add before 1.2 grows

| Budget | Value to start with [E] | Enforced by |
|---|---|---|
| Game-loop tick p99 at 600 per realm | ≤ 150 ms; max ≤ 500 ms | L600 soak gate |
| Serial coordinator pulses (`PrepareCoordinatorTick`) | ≤ 5 ms per tick total; each pulse ≤ 2 ms | `AI_PROFILE` + log warning |
| `ai_budget_ms` (deferred planning) | 4 ms, as declared | the deferral queue (§8.4) |
| Per-system share | No single autonomous system > 25% of brain time | `AI_PROFILE` review per soak |
| Perception | ≤ 8 LOS checks per RvR bot per s (existing); ≤ k rollers per target | code cap (§8.2) |
| Chat | ≤ N lines per minute per channel per realm (e.g. /faction 6, /say area 10); rules engine ≤ 0.2 ms per chat event | rate limiter + profile |
| Social facts and memory | ≤ X rows per bot; DB growth ≤ 5 MB a day at 900 per realm | expiry sweep + soak measurement |
| Live events | 24 monsters alive, waves of 8–12, ≤ 64 bots per rally (ENGINE §5); worst tick during a wave ≤ 250 ms | beat caps + timeline report |
| SQLite | write batch ≤ 50 ms p95; no "database is locked" | profile + log grep |
| Long session | handles ≤ 2,000 an hour; Gen2 heap flat ±10% over 8 h | soak gate |

---

## 10. The 10 most important technical actions, in order

1. **Check the owner's SQLite settings and add routine backups (1.1.2).**
   - Read `runtime/server/config/serverconfig.xml` on the PC. If journal is off, move to
     `Pooling=True;Journal Mode=WAL;Synchronous=Normal`. The WAL path already exists in `SqliteObjectDatabase`.
   - Add a daily online backup (`VACUUM INTO` or `con.backup`) to `runtime/data/backups`, keeping 7.
   - Back up the DB before `INSTALL.cmd`.
2. **Fix the release gates (1.1.2):**
   - the `UT_AtomicExchangePersistence` cleanup race;
   - protected main with a required check and owner bypass;
   - CI on push to main;
   - the README/version consistency check.
3. **Add `AI_PROFILE` (1.2):** per-system and per-pulse cost, tick p50/p95/p99, once a minute, plus the timeline
   report script. Nothing else in 1.2 is judged without it.
4. **Run the L300 and L600 baselines on release/1.2 before adding systems (1.2).** Record the top-ten table.
5. **Build the test world and harness (1.2):**
   - `make_test_world.py` from the clean 0.37 reference DB (no saves);
   - `OfflineDaoc.TestClient` protocol bot;
   - three scenarios (login/save/restart, a kill quest in a bot group, a delivery), nightly.
6. **Add the duty lease ledger (1.2):** raids and RvR events on it, the watchdog reading leases, reason codes.
7. **Add the PR #53 changes (1.2):** the observer cap, hazard calibration and partial response, with the seeded
   statistical test (§8.2).
8. **Make `ai_budget_ms` real (1.2 or 1.3, per the L600 result):** a deferral queue for planning work; combat never
   deferred.
9. **Persist event state (1.3):** the `offline_event_state` table, idempotent mutations, undo-on-restart,
   `LastSuccessful`, and the ledger moved into `runtime/data`.
10. **Make the 1.1→1.2 world migration reversible (1.2):** row-level inverse scripts for his world scripts (the
    `epic_chains.py` pattern), run by Setup and by `ROLLBACK.cmd`. Then start splitting `AutonomousWorldBotController`
    by owner along the ledger's duties (ongoing, after action 6).

---

## Appendix: commands and evidence

- `grep -rn "AI_BUDGET_MS\|MaximumDecisionsThisTick" source/server/GameServer` → only the declaration and the method.
- `gh api repos/chris4aker/OfflineDAoC-LivingServer/branches/main/protection` → `Branch not protected (HTTP 404)`.
- PR #56 failing job: `gh api repos/chris4aker/OfflineDAoC-LivingServer/actions/jobs/124920974517/logs`.
- Simulation: `python3 -I /home/claude/north-star-audit/perception_sim.py 1500`. Its output is
  `/home/claude/north-star-audit/perception_sim_out.md`.
- Not done: no server was started, no test was rerun in `wt-12` (the targeted tests were not needed for these
  findings), and nothing was built or changed on any branch.
