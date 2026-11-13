# Performance: AI_PROFILE and the load tests

Living Server 1.2 measures what its AI costs before it adds more ("measure before adding", technical audit
2026-11-03, §1, §8.4 and §9). This page says what is measured, how to read it, and how to run the L300, L600 and L900
load tests on your own PC.

- **On by default.** The server property `ai_profile` (category `autonomous_population`) switches it. It only
  measures: no bot decides or acts differently with it on or off.
- **Where to look:** one `AI_PROFILE` line a minute in `runtime/server/logs/server.log`, one row per AI system a minute
  in `runtime/server/logs/ai-profile.csv`, and `/lsprofile` in game (GM).
- **The budget is not enforced.** `ai_budget_ms` is declared but nothing reads it yet (see [The budget](#the-budget)).
- **Credit:** the design borrows from two GPL-3.0 forks: lometur's HearthDAoC bot CPU meter (per-thread padded
  counters, per-window differences, brain time by bot kind) and stefanrows's `BotThinkProfiler` (the scope struct, the
  measured lock and the once-a-minute line). See the file headers of `AiProfile.cs`.

## What is measured

### The tick

The game loop runs every service once a tick (about 30 ticks a second). For each tick the profiler records:

| Part | What it is |
|---|---|
| **tick** | The whole tick: every service, from the game-loop service to the quest services. Sleep between ticks is not counted. |
| **serial** | The planning that runs on the game loop, one bot at a time, before any brain thinks (`NpcService.Tick`): the brain registry, the RvR planning view, the group coordinator's pulses and the camp planning. Any cost here makes every tick longer. |
| **parallel** | The brains' pass: every NPC and bot brain thinks, spread over all cores. The time is the wall time of the pass on the game loop. |
| **other** | The rest of the tick (movement, attacks, casting, effects, clients, timers): tick less serial less parallel. |

Each minute gives the p50, p95, p99 and maximum of the tick (nearest-rank percentiles of every tick in the minute),
and the average and p99 of the serial and parallel parts. The existing `SERVER_WORK` lines still give each service's
average and maximum.

### The systems

A scope around a piece of AI work counts its calls and its time, in the calling thread's own counters. About 70
systems are timed:

| Group | Systems | Where |
|---|---|---|
| Brain turns, by kind | `think.gamebot`, `think.companion`, `think.pet`, `think.npc` | every brain turn in `NpcService` |
| Serial planning | `serial.brain_registry`, `serial.rvr_planning`, `serial.coordinator`, `serial.camp_planning` | `NpcService.Tick` |
| Coordinator pulses | `pulse.realm_event_notices`, `pulse.living_server`, `pulse.realm_event_controls`, `pulse.realm_raid`, `pulse.raid_forced_party`, `pulse.epic_special_forces`, `pulse.rvr_defense`, `pulse.warband_support`, `pulse.objective_assignments`, `pulse.broken_sessions`, `pulse.form_siege_parties`, `pulse.try_form_groups` | each pulse in `AutonomousBotGroupCoordinator.PrepareCoordinatorTick` |
| World controller | `controller.tick` and its branches `controller.stable_travel`, `.rvr`, `.travel_regions`, `.group_combat`, `.rendezvous`, `.follow_leader`, `.select_camp`, `.work_camp`, `.issue_path`, `.route_threats`, `.raid_travel`, `.battleground` | `AutonomousWorldBotController` |
| Group coordinator | `coord.pulse` | `AutonomousBotGroupCoordinator.Pulse(bot)` |
| Routes | `route.stable_find_best`, `route.stable_portal`, `route.raid_dungeon`, `route.df_chain`, `route.zone_step` (outermost step only), `route.corridor`, `route.road`, `route.rvr_rally_check` | the route planners |
| Navmesh queries | `nav.path`, `nav.closest_point`, `nav.other` | the native Detour calls `NavQueryProfile` counts |
| Frontier | `frontier.threat_scan` | the RvR threat scan, not its cheap skip checks |
| Combat AI | `combat.spells`, `combat.heals`, `combat.style`, `combat.movement` | `BotBrain.CheckSpells`, `CheckHeals`, the melee style choice, a melee bot's circle and a caster's step (`BotBrain.CombatMovement`, only when one is due) |
| Pets | `pets.maintain`, `pets.sync` | `AutonomousPetSupport` |
| Persistence | `db.status_batch`, `db.inventory_batch` | the bot status and inventory batch writes |
| Chat | `chat.bot_turn`, `chat.ambient`, `chat.player_reply`, `chat.news` | the bot's chat turn and the chat coordinator |
| Economy and town | `economy.merchant`, `economy.exchange`, `economy.valuation`, `town.idle`, `town.service`, `town.hangout` | merchant and Exchange visits, item valuation, town idling, training and service trips, the search for a hangout (bank, merchant, trainer, crowd) on a town break |
| Housekeeping | `sweep.departed_refs` | the deleted-object holder sweep |
| Lock waits | `lock.coordinator`, `lock.raid`, `lock.rvr_events`, `lock.db_write` | the time spent *waiting* for the group coordinator's, the realm raid's, the RvR event layer's and the database write lock. An uncontended lock adds nothing; `calls` counts waits. |

**Self and total time.** Scopes nest: `controller.tick` contains `controller.work_camp`, which contains
`controller.issue_path`, which contains `nav.path`. Each system has:
- **total** time: everything inside its scopes, nested scopes included;
- **self** time: total less the scopes nested in it. Self times add up without counting anything twice, so the
  top-ten list and the shares use them. `think.*` self time is everything a brain turn does that no other scope
  claims: for monsters and guards that is nearly all of it.

All system times are summed over every thread for the minute. With 12 cores, 12,000 ms of `think.npc` in a minute
is a whole core busy with NPC brains.

**Not measured here yet** (audit §1.5): allocations per system, SQLite queue depth and write latency (only the batch
time), and chat lines per channel. Heap, handles and threads are in the existing `SERVER_MEMORY` lines, and slow
turns still go to `SERVER_STALL`.

### What it costs

One scope is two `Stopwatch` timestamps and a few adds to the thread's own counters: no lock and no allocation.
`UT_AiProfile.OneScopeCostsAboutAHundredNanosecondsOrLess` measures it on every CI run and fails above about 100 ns
(or, on a machine whose clock itself is slow, above two clock reads plus 40 ns). Measured 2026-11-03: 64 ns a scope on
the Linux build machine (one timestamp 26 ns); 0.6 ns while `ai_profile` is off. To see the profiler's own share of a
minute, add up the `calls` of every system in the CSV and multiply by about 65 ns.

## How to read it

### The AI_PROFILE line

```
AI_PROFILE tick_p50=14.20 p95=31.05 p99=48.70 max=212.40 serial_ms=1.840 parallel_ms=9.310 locks_ms=35.2 top=think.npc:310442/5120.4,nav.path:88210/2210.7,controller.tick:61022/1400.2,... ticks=1795 window_s=60 tick_avg=16.02 serial_p99=4.10 serial_max=7.30 parallel_p99=30.20 parallel_max=190.10 other_ms=4.870 utc=2026-11-03T21:14:00Z
```

| Field | Unit | Meaning |
|---|---|---|
| `tick_p50`, `p95`, `p99`, `max` | ms per tick | The game-loop tick over the minute. `p99` is the slowest 1 in 100 ticks. |
| `serial_ms` | ms per tick, average | The serial planning before the brains. Budget: 5 ms. |
| `parallel_ms` | ms per tick, average | The brains' parallel pass (wall time). |
| `locks_ms` | ms in the minute, all threads | Time spent waiting for the four measured locks. |
| `top` | `name:calls/self ms` in the minute | The ten systems with the most self time. |
| `ticks`, `window_s` | | How many ticks the minute had, and its exact length. |
| `tick_avg`, `serial_p99`, `serial_max`, `parallel_p99`, `parallel_max`, `other_ms` | ms per tick | More of the same; `other_ms` is the rest of the tick. |
| `over_budget` | only when present | What broke the audit's serial budget (§9): `serial_p99:6.2` (serial planning over 5 ms in the p99 tick) or `pulse.x:3.1` (a pulse's longest run over 2 ms). The same goes to `warn.log` as `AI_PROFILE_BUDGET`. Only reported. |
| `utc` | | The end of the minute. |

**Reading it:** a p99 that grows while `parallel_ms` stays flat points to the serial planning or another service; a
growing `serial_ms` names its pulse in the CSV (`pulse.*`, `serial.*`); a large `locks_ms` with a slow `coord.pulse`
means brains queue on the coordinator's lock. The audit's target for one system is at most 25% of all brain time.

### The CSV file

`runtime/server/logs/ai-profile.csv`, one row per system that ran, every minute:

```
utc,system,calls,total_ms,self_ms,max_ms,avg_us,share_pct
2026-11-03T21:14:00Z,think.npc,310442,6020.1,5120.4,48.2,19.39,31.2
```

`max_ms` is the longest single call in the minute, `avg_us` the average call in microseconds, and `share_pct` the
system's share of all self time in that minute. Past 50 MB the file becomes `ai-profile.1.csv` and a new one starts.
It opens in Excel or LibreOffice as it is.

### In game

`/lsprofile` (GM) shows the last minute: the tick numbers and the ten costliest systems. `/lsprofile all` lists every
system; `/lsprofile line` shows the log line.

### The soak report

`tools/perf/ai_profile_report.py` (Python 3, nothing to install) turns a log into a Markdown report: the run's tick
numbers, a top-ten table of AI systems (from `ai-profile.csv` when it is beside the log), a timeline of the tick (p99
over time, with a bar per row), and the load-test gate:

```
python tools\perf\ai_profile_report.py runtime\server\logs\server.log --skip-minutes 20 --gate L300 --out L300-report.md
```

- `--skip-minutes N` leaves out the startup ramp.
- `--every N` sets the minutes per timeline row (default: about 60 rows).
- `--timeline-csv file.csv` also writes the per-minute numbers as CSV, for a chart.
- The exit code is 1 when the run failed its gate.

## The budget

`ai_budget_ms` (default 4.0) is declared and written by Setup, but **nothing reads it today**: changing it changes nothing. Its description now says so. The plan (audit §8.4):
1. Measure first: the L300 and L600 baselines below.
2. If L600 shows serial or planning time above 25% of the tick, add a deferral queue for planning work (route plans,
   camp selection, raid recruiting scans, chat follow-ups) in 1.2; otherwise in 1.3. Combat, movement and retaliation
   are never deferred.

The seam is in place and unused: `AiProfile.ElapsedThisTickMs()` gives the time the tick has run so far, and
`AutonomousAiBudget.MaximumDecisionsThisTick(AiProfile.ElapsedThisTickMs(), AI_BUDGET_MS, queue.Count)` would decide
how many queued decisions run.

The audit's budgets (§9) that AI_PROFILE checks by itself: serial planning at most 5 ms a tick and each coordinator
pulse at most 2 ms (`over_budget`). The others (tick p99 per population, no system over 25% of brain time) are read
from the report.

## Load tests: L300, L600, L900

From the audit (§8.4). Change one thing per run (the release-train rule), and run L300 and L600 on `release/1.2` before
1.2 adds new systems: those two runs are the baselines everything else in 1.2 is judged against.

| Run | Bots per realm | Duration | Mix | Pass criteria |
|---|---|---|---|---|
| L300 | 300 | 2 h | default goals; one realm raid forced at 30 min, one keep siege forced at 60 min | p99 tick < 100 ms; max < 500 ms; no `SERVER_STALL` over 1 s |
| L600 | 600 | overnight (8 h) | the same, plus you in the frontier with the client (perception on) | p99 < 150 ms; heap flat ±10%; handles < 2,000 an hour; database growth measured |
| L900 | 900 | overnight | the same, plus a `/lstest` chat burst at hour 2 and three-realm RvR | p99 < 200 ms (a target, not yet a promise); no bot "stood at 0 HP"; chat lines a minute per channel recorded |

The report checks the tick and stall criteria strictly (every minute's p99 under the limit). Heap, handles, database
growth and what the bots looked like are checked by hand, as below.

### Getting ready (once)

1. **Use a copy of the game folder, not your world.** The test creates hundreds of bots. Close the server, the
   launcher and the game, then copy the whole Offline DAoC folder (the one with `START OFFLINE DAOC.cmd`) to, for
   example, `D:\OfflineDAoC-L300`. Everything below happens in the copy. Your own world is not touched.
2. **Only one server at a time:** the copy uses the same port (10300), so don't run both.
3. **Install the 1.2 build in the copy.** Download the server build of the `release/1.2` commit you test (the
   `OfflineDAoC-LivingServer-...` artifact of its GitHub Actions run), put its folder inside the copy and run
   `INSTALL.cmd`. Note the commit: the server log's `FORK_BUILD` line shows it.
4. **Living Server settings:** leave them as you play, but write them down; every run uses the same ones.
5. **`ai_profile`** is on by default (a row of the database's `ServerProperty` table, category `autonomous_population`).
   If you turned it off, set it back to `True`.

### Running L300

1. Start the launcher in the copy and count each realm's gamebots in its bot list. On each realm's card (ALBION,
   MIDGARD, HIBERNIA) create gamebots until each realm has **300** (1, 10 or 100 at a time; mix level 1 and level 50
   as you normally would, and write the mix down). If a realm already has more than the run needs, start from a
   copy of a fresh install with no bots instead: removing bots is not part of this test.
2. Write down the time, then press START SERVER. Bots log in over the startup ramp (`startup_ramp_minutes`, 15 by
   default).
3. Log in with your GM character and stay in a quiet town (not the frontier) unless the run says otherwise.
4. **At 30 minutes:** force a realm raid: `/lstest raid` (your realm), or `/lstest raid dragon`.
5. **At 60 minutes:** force a keep siege: launcher, **Realm Events** tab, start a keep siege.
6. Now and then, `/lsprofile` shows the last minute. Note anything that looks wrong in game (bots frozen, a killed bot
   standing at 0 HP, a party that never meets up), with the time.
7. **At 2 hours:** stop the server from the launcher.
8. Make the report (in the copy's folder):

   ```
   python tools\perf\ai_profile_report.py runtime\server\logs\server.log --skip-minutes 20 --gate L300 --out L300-report.md
   ```

   If the server log rolled over during the run, give all the log files, oldest first.
9. **By hand**, from `server.log`:
   - `SERVER_MEMORY` lines: `liveMB` at the start and the end (flat within ±10%?), and `handles` growth per hour.
   - `runtime\data\opendaoc.sqlite3.db`: its size before and after.
10. Keep `L300-report.md`, `runtime\server\logs\ai-profile.csv`, the build commit and your notes together, for
    example in a folder `perf-runs\2026-11-xx-L300`. Keep logs and the database out of the repo.

### L600 and L900

The same steps, with 600 or 900 bots per realm, run overnight, and:
- **L600:** log in and stand in the frontier with the client for part of the run, so the RvR perception and frontier
  scans run near a player. Use `--gate L600`. Record heap, handles and database growth per hour.
- **L900:** also a `/lstest debate` or `/lstest storyline` chat burst at hour 2, and RvR in all three realms. Use
  `--gate L900`. Count chat lines a minute per channel from the log.

### What to send back

- The top-ten table and the timeline from the report (paste the Markdown).
- PASS or FAIL per gate line, and your by-hand numbers (heap, handles, database size).
- Anything you saw in game, with the time, so it can be matched to the timeline.

The decision that follows (audit §8.4): if L600 shows serial or planning time above 25% of the tick, the budget
enforcement comes in 1.2; otherwise it waits for 1.3.
