# RvR audit 3: the evidence ledger and a red-team of the RvR tests

Issue #495, part of the RvR audit (#506). Report only: no game behaviour changes. Written 2026-11-24 on `main` at
`de53d0d` (the 1.2.2 code plus the audit docs). Framework and report format: [README.md](README.md).

Evidence levels used below:
- **played**: seen in a log or on screen on the owner's PC, with the build named.
- **world sim**: a run of real server code in a test or container world. No RvR world sim exists (see F2).
- **sim**: a seeded Monte Carlo or replay inside a unit test, on made-up inputs.
- **unit**: a unit test of a pure rule or a world-less object.
- **read**: I read the code or docs. **none**: nothing found.

Sources read: AGENTS.md; RVR-FIELD-1.2.md; PLAYED-CHECK-1.2.md; 1.2.1/PC-RESULTS-1.2.md; 1.2.1/RVR-RALLY-DIAG.md;
NIGHT-SIEGES-AND-MARFACH.md; OPEN-WORK.md; KNOWN-ISSUES.md; RELEASE-NOTES-1.2, 1.2.1, 1.2.2; issues #416-#419, #421,
#425 with comments; 88 RvR issues and PRs by title; the 89 RvR test classes; `source/tools/played_check_log.py`;
the RvR code named in the findings. Git history of `main`, `integration/1.2`, `integration/1.2.1` and
`integration/1.2.2` from 2026-11-13 on (earlier history was not fetched; see "Could not check").

## Test run on main (this audit)

- Commit `de53d0d3e97b8bf30176e322707916f03b093dc6`, Release build, .NET 10, Linux container (no navmeshes, no game).
- Filter: the 89 RvR test classes not in `.github/ci-excluded-tests.txt` (the list is in the appendix).
- **1,057 test cases: 1,056 passed, 1 failed.** The failure is
  `UT_SiegeMusterAndRankOct6.WeightedRankChoiceFavoursTheHighestRankButKeepsLowerOnes`: run without the rest of the
  suite, `AutonomousPetSupport`'s static constructor calls `SkillBase`, whose constructor needs `LanguageMgr`, which is
  null (`TypeInitializationException`). It fails alone too (5 run, 1 failed). It passes in CI only because another test
  sets up the language tables first. Order-dependent, not an RvR defect.
- CI on the same commit (run 45448692648, job `server`): 9,780 passed, 0 failed, 1 skipped (whole suite).
- Not run (they need the PC's game files or env vars): 7 CI-excluded RvR classes (`UT_AllFrontierKeepRoutes`,
  `UT_InstalledKeepRoutes`, `UT_KeepDoorNavmeshProbe`, `UT_SiegeFiringPositions`, `UT_SiegeIncidentNavigation`,
  `UT_SiegeMerchantCatalog`, `UT_FrontierSupplyMerchants`) and 4 `Explicit` probes that ran nothing
  (`UT_RvrCheckSpotCatalog`, `UT_RvrLaneSurvey`, `UT_RvrRouteTrapProbe`, `UT_RelicShrineReach`). So 85 classes (86 with
  a nested one) gave the 1,057 cases.
- 0 tests were skipped or inconclusive in the run: no test passed by `Assume`/`Ignore`.

So "the ~87 RvR unit tests" are 89 selected classes, 85 that run, and 1,057 cases. All green says the pure rules compute what their authors
wrote. Read on for what that is worth.

## The ten findings that most remove false confidence, worst first

### F1. The "bots notice bots like a player" check reads the wrong number, and its test grades itself
- **What it means for a player:** nobody knows whether enemy bots spot each other at human speed. The 1.2 notes say
  they do. The PC row has been "Open" since 2026-11-17 and could never close.
- **Chain link:** perceive.
- **Where:** `RVR_NOTICE` logs `delayMs` (the reaction delay after a notice: 0.5-2 s at normal, by design, whatever the
  range) and `time_since_first_los_ms` (the real time to notice)
  (`AutonomousWorldBotController.RvrPerception.cs:218-221`). The log checker reads only `delayMs`
  (`source/tools/played_check_log.py:1512`, row text at `:2390`). PLAYED-CHECK-1.2.md line 343 then reports "the median
  `delayMs` is about 1 s at every range" as a failure to meet the perception table. That is the designed reaction
  delay, not a failure. `time_since_first_los_ms` has been in the line since WP1 (#301, `f6071164`, on `19e7037`) and
  has never been read.
- **The test:** `UT_RvrPerceptionCalibration.The_median_time_to_notice_matches_the_target_for_each_level_and_band`
  is green. But the chance per scan is solved from the same target table
  (`UT_RvrPerception.The_chance_per_scan_puts_exactly_half_the_sightings_past_the_median`). The target stands still at
  a fixed distance, line of sight is always open, facing is drawn, every bot is level 50 with no personality. It
  checks the formula against the numbers the formula was fitted to.
- **How often:** every frontier notice (3,002 bot-on-bot `RVR_NOTICE` on `19e7037`).
- **Evidence:** read (checker and code); played (the counts on `19e7037`).
- **Class:** measurement defect (instrument reads the wrong field), plus a circular test. Belongs in the instruments.
- **Fix:** checker reads `time_since_first_los_ms` by distance band and holds it against the table; re-read the
  `19e7037`, `5378dbe` and `9a173f4` logs. Size S, risk none (tool only).

### F2. Nothing ever runs a fight: no end-to-end test of group against group
- **What it means:** "Groups fight like groups" (1.2 notes) has no test that two groups meet, fight and one side wins.
- **Chain link:** perceive → decide → move → fight → outcome, all the joins.
- **Where:** every tactics class (`UT_RvrAssist`, `UT_RvrCrowdControlPlan`, `UT_RvrInterruptPlan`, `UT_RvrPeelPlan`,
  `UT_RvrPositioning`, `UT_RvrEngagement`, `UT_RvrSoloEngage`; 119 cases) runs on hand-made `RvrFightBoard`
  records or world-less gamebots (`DutyLeaseTestKit`). The code that builds a board from the world
  (`RvrFightBoards.Get` → `BuildBoard`, `RvrFightBoards.cs:129`) is untested; `UT_RvrFightBoard`'s own summary says
  so. No RvR test calls `Think()`, deals damage or casts. The only world object in any RvR test is a bare `Region`
  for a relic pickup (`UT_RvrExpansion`). The `Rehearsal` harness (`Tests/Rehearsal`) exists for the economy only.
- **What the PC shows instead:** counts that the tactic lines fired (`19e7037`: 386 assist calls, 750 crowd control
  assignments, 118 interrupts, 202 peels, 340 kites). "Fired" is not "worked". The owner's 20-minute look at group
  tactics is "Not done" (PLAYED-CHECK-1.2.md line 346).
- **Evidence:** read. **Class:** missing integration layer; belongs in a CI harness (Instruments, 5.2).
- **Fix:** the fight harness in 5.2. Size L, risk none (test only).

### F3. The regroup row passes because its bar was raised three times
- **What it means:** the "no regroup flap" must-row was failed by four PC builds and now passes on a bar set after the
  last result.
- **Chain link:** decide (warband relay).
- **History (played):** flapped on `19e7037` (3,000 `RVR_REGROUP` in 4 h 42 min); "Seen" on `4be6fab` after 30
  minutes; contradicted on `453e01a`, `5378dbe` (14.6%) and `b1b6846` (12.6%). Bar: about 10% → 11.5% (owner,
  2026-11-18) → 15% (owner, 2026-11-18 18:19) → 20% (owner, 2026-11-19), each time after a build came in over it
  (`played_check_log.py:76-82`). `9a173f4` gave 17.4%; #419 was closed as under the 20% bar.
- **What is sound:** the exact checks (a) relayed stance relayed again, (b) regroup ending a relayed retreat relayed,
  (c) echo to the sender, were 0 on `5378dbe`, `b1b6846` and `9a173f4`. They are the real evidence that one hop holds.
  The share no longer constrains anything.
- **The tests:** `UT_RvrEngagement`'s relay cases were green on `19e7037` (the flap) and before each later
  contradiction. Each fix added a case for the shape just seen. None models a dense warband, so none could predict the
  fan-out that pushed the share up.
- **Evidence:** played (counts), read (bar history). **Class:** a moving target in the instrument. Owner decisions, so
  legitimate, but the docs should say the share is a tally, not a gate.
- **Fix:** drop the share as a pass/fail or freeze it; gate on (a), (b), (c) and the per-group fan-out #419 added.
  Size S.

### F4. The tests are self-graded, and issues were closed on them
- **What it means:** green tests and closed issues are the fixers grading their own work.
- **Measured:** of the RvR test classes whose history is in the fetched range (added since 2026-11-13), 48 of 50 were
  added in the same commit as the code they test. The other 2 are probe tools that never run in CI.
- **Issues:** #416, #417 and #418 each carry a fixer's comment "Leaving this open until a capture/it is seen in
  play/a log confirms it" (2026-11-22), then were closed the next day as "Fixed ... Shipped in 1.2.1 ... not yet seen
  on the owner's PC". OPEN-WORK.md marks them "Done in 1.2.1, tests only". #416's own fix (`48e85d51`) and test
  (`UT_BattlegroundLordReach`) were written together; so were #417 (`95040bfc`, `UT_RvrRoamFindsFights`) and #418
  (`0eb83ecb`, `UT_FrontierPorterWait418`; the next commit, `dda28f3f`, restored code the #418 change had dropped).
- **Evidence:** read (git, issues). **Class:** process. Belongs in the blind review protocol (5.3).
- **Fix:** reopen #416, #417, #418 as "fix shipped, owed in play" or keep a "shipped, unseen" state that is not
  closed. Size S.

### F5. Every RvR "Seen" is from a build older than the RvR code that shipped
- **What it means:** the 1.2 notes' RvR evidence, and all later claims, rest on builds that do not carry today's code.
- **Played builds with RvR evidence** (all on 2026-11-15 to 11-19): `3d67b6f`, the battleground builds, `88b6ab3`,
  `ccba364`, `19e7037`, `4be6fab`, `453e01a`, `5378dbe`, `b1b6846`, `9a173f4`, `c81eb5d`, `43c43aa`, `aa537ce`. No RvR
  PC log exists for 1.2.1 or 1.2.2.
- **Changed since:** from `631ba1f` (the code of candidate `aa537ce`) to `main`, the RvR files changed by 1,824
  insertions and 1,110 deletions in 13 files (lord reach #416, roam weighting #417, porters #418, defence stand-down,
  keep-route restart, lord formation, the raid-tactic audit's guards).
- **Stale claims in the 1.2 notes' Seen table:** "Bots noticing a player at 516-1,575 units" is from `3d67b6f`, before
  WP1 (#301) put lookers and a per-group cap on the player path too (owner decision 7). "Keeps changing hands: 15 in one
  evening" is from `ccba364`, before WP0-WP6 and before the siege-lord changes. "Stance changes, assist calls ... in the
  log" (`c81eb5d`, `19e7037`) is "fired", not "worked".
- **A stale doc row:** PLAYED-CHECK-1.2.md line 331 still says "Not seen: no keep has changed hands" for battlegrounds;
  PC-RESULTS-1.2.md records Caer Caledon taken by Albion on `43c43aa` (14:25).
- **Evidence:** read (git diff, docs). **Fix:** stamp each "Seen" with "on build X; code changed since: yes/no" (a
  script over `git diff` of the RvR paths). Size S.

### F6. Battlegrounds: the most tests, the least working
- **What it means:** 274 test cases in 10 battleground classes; one keep capture in about eight PC battleground builds.
- **Played:** Thidranki and Caledonia on `3d67b6f`, `ea882e6`, `3b808b4`, `d64d005`, `0026378`, `4d48f42`, `c3cd93c`,
  `c81eb5d`: no capture. `43c43aa`: Caer Caledon taken once, after its keep levels were repaired (a data fix, not a
  tactic). On `43c43aa` the lord at Caer Caledon dropped only to 97%.
- **The tests:** each class encodes the shape of the last failed log (`TheHalfRealmStormsOf4d48f42NoLongerGo`,
  `BeforeTheFixBothRealmsGotTheSamePoint`, `TheHibernianWaveOfEightOutOfTwentyOneNowWaitsForEleven`). They prove that
  the last log's exact shape cannot recur. They say nothing on whether a storm now takes the keep. `UT_BattlegroundLordRally`
  alone has 49 tests (111 cases), all pure, all "the rally itself needs a live battleground".
- **Sound:** `UT_BattlegroundLordReach.TheStrikeReachIsInsideTheLordsDamageRadius` ties the bots' strike reach to the
  upstream `GuardLord.KeepDamageRadius` (400) and `TowerDamageRadius` (300). That is a real cross-check.
- **Class:** regression tests standing in for outcome tests. **Fix:** a battleground storm in the harness (5.2), and
  `RVR_KEEP_TAKEN battleground=True` per hour as the PC measure. Size M.

### F7. Route and muster fixes are tested on a fake mesh; the real-mesh probes do not run in CI
- **What it means:** "fixed, tests only" for a route trap means "the selection logic is right if the mesh is as the
  test pretends".
- **Where:** `UT_RvrRouteTraps43c` uses a stand-in pathing service: `HasLineOfSight` always true, `GetClosestPoint`
  returns its input, reachability is "inside a listed pocket" (`UT_RvrRouteTraps43c.cs:100-129`). `UT_RvrLaneChoice`
  uses "a made-up frontier" with a stand-in walker. The real-mesh probes (`UT_RvrRouteTrapProbe`,
  `UT_RvrCheckSpotCatalog`, `UT_RvrLaneSurvey`, `UT_RelicShrineReach`; and the 5 CI-excluded navmesh classes) need
  the PC's files. The repo
  records no result for most of them.
- **Sound (played):** the catalogs are re-walked at server start: on `19e7037`, 98 check spots and 18 circuits (400
  walks, 0 failed) and 318 of 332 lanes. On `aa537ce` the Cruachan Gorge column walked single file with none stuck
  (PC-RESULTS-1.2.md). That is the only real-mesh RvR route evidence.
- **Class:** fake boundary. **Fix:** run the three probes on the PC once per release and commit their summary lines.
  Size S.

### F8. A green test that can no longer tell a working hook from a dead one
- **Test:** `UT_RvrTacticsHooks.With_every_switch_on_each_brain_hook_has_no_opinion` (and the controller twin).
- **What it was:** WP0's check that the empty hook stubs change nothing. Its summary still says "Every hook is a stub".
- **What it is now:** WP2-WP6 filled those hooks. The test still passes because its bots have no world, so no board
  is built and every hook returns null. It proves "with no world, nothing happens". A hook that never fires in play
  would pass it too.
- **Also pins constants:** `Every_switch_is_a_bots_setting_that_is_on_by_default...` and
  `Every_switch_ships_on_in_the_release_world` pin the defaults (useful; the owner's rule), not behaviour.
- **Fix:** rename it to what it proves, or delete the stub cases. Size S.

### F9. The siege-balance sim assumes the lever it is meant to test
- **Claim:** "The target is about 50% attacker wins, in a band of 35-65%" (RVR-FIELD-1.2.md, Safeguards).
- **Test:** `UT_SiegeBalanceNov06.TheBalanceSettlesNearHalf` and `OnAverageTheBalanceSettlesAtHalf` feed the real
  `SiegeDefenseBalance.NextShift` with outcomes drawn from a made-up logistic `AttackerChance(shift)`. That assumes the
  balance moves the win rate.
- **The PC said otherwise:** on `168f55d` (2026-11-05/06) the share sat at its limit all night and attackers took 13 of
  17; earlier, "at share 0.5, armies of 17-51 still lost: walls, doors and keep guards don't scale with the balance"
  (NIGHT-SIEGES-AND-MARFACH.md). `TheOwnersNightWouldHaveTrimmedTheAttackersByTheThirdCapture` asserts the trim fires;
  its "attackers still took 13 of 17" is the input, not a result.
- **Evidence for the 35-65% band:** sim only. No PC night since 1.1.2 has a counted attacker win rate in the docs.
- **Fix:** count `RVR_EVENT_ENDED` outcomes per night by `kind` and `thin` (the fields exist). Size S.

### F10. The population split and the eights: a pinned assumption that play contradicted
- **Claim:** RvR bots split 55% warbands, 30% eights, 15% solo/duo/stealth (owner decision 2, stated as an assumption).
- **Test:** `UT_RvrRoamSplit.Over_many_bots_the_split_settles_at_55_30_15` checks the picker's arithmetic on a pure pool.
- **Played (`19e7037`):** 27 eights formed against 235 warbands; 178 of those were pairs; 18 of the 27 eights were
  drafted into keep events and only 2 walked a circuit (from `UT_RvrQuietFrontier`'s own summary). 0
  `RVR_ROAM_MOVE_ON`. The fix (`UT_RvrQuietFrontier`, #306) was green; #417 was then filed for the same symptom; its
  fix (`UT_RvrRoamFindsFights`) is green and unseen.
- **Fix:** log the split as formed each hour (`RVR_PARTY_FORMED` kinds per hour). Size S.

## 1. The evidence ledger

One row per RvR claim in the docs, release notes, known issues and issues. "Stale" = RvR code it depends on changed
after the build that gave the evidence. Played builds are as PLAYED-CHECK-1.2.md (PC) and PC-RESULTS-1.2.md (PCR) give
them. Line numbers are of PLAYED-CHECK-1.2.md unless named.

| # | Claim (where) | Best evidence | Level | Build | Stale? |
|---|---|---|---|---|---|
| 1 | Bots notice the player by chance and distance, not at 5,000 (RN-1.2 §RvR; PC 329) | Midgard bots noticed the owner's character at 516-1,575, then mezzed, rooted, killed him | played | `3d67b6f` | Yes: WP1 (#301) put lookers and the per-group cap on the player path |
| 2 | The time-to-notice table holds for the player (LIVING-SERVER, RVR-FIELD decision 7) | `UT_RvrPerceptionCalibration` | sim (circular, F1) | - | - |
| 3 | A spotter calls the player to its group (`RVR_SPOT`) (RN-1.2 "the rest hear it") | Not logged on `3d67b6f`; 643 `RVR_SPOT` on `19e7037` are bots calling bots; a call on the player "not looked for yet" (PC 329) | none for the player | - | - |
| 4 | Bots notice bots by the player rule (RVR-FIELD WP1; PC 343) | 3,002 `RVR_NOTICE kind=EnemyBot` fired; table never checked (F1) | played (fired only) | `19e7037` | Yes |
| 5 | Bots notice a stealther only close up (RVR-FIELD Eyes) | `UT_RvrPerceptionBots.A_stealther_is_noticed_only_close_up...` | unit | - | - |
| 6 | Scan cost halves: about 416 scans/s vs 800 (RVR-FIELD Eyes, "Not measured") | `UT_RvrPerceptionCalibration` ray counts; `RVR_SCAN_BUDGET` exists, never reported | sim | - | - |
| 7 | Assist train: one called target, members follow late (RN-1.2; RVR-FIELD Mind) | 386 `RVR_ASSIST_CALL`, 636 switches fired; damage share on the call never measured (no damage log) | played (fired) | `19e7037` | Yes |
| 8 | Kill order: casters, healers ... tanks last (RVR-FIELD) | `UT_RvrAssist` | unit | - | - |
| 9 | Crowd control plan; kill target never mezzed (RN-1.2) | 750 `RVR_CC_ASSIGN` fired; `UT_RvrCrowdControlPlan` | played (fired), unit | `19e7037` | Yes |
| 10 | Interrupts (RN-1.2) | 118 `RVR_INTERRUPT` fired; `UT_RvrInterruptPlan` | played (fired), unit | `19e7037` | Yes |
| 11 | Peel for healers (RN-1.2) | 202 `RVR_PEEL`; `UT_RvrPeelPlan` | played (fired), unit | `19e7037` | Yes |
| 12 | Kite (RN-1.2) | 340 `RVR_KITE`; `UT_RvrPositioning` | played (fired), unit | `19e7037` | Yes |
| 13 | Backline and 350 spread (RVR-FIELD Hands) | `UT_RvrPositioning` only; no `RVR_BACKLINE` count in any doc | unit | - | - |
| 14 | Open from stealth (RN-1.2) | 56 `RVR_STEALTH_OPEN`; never watched | played (fired) | `19e7037` | Yes |
| 15 | Stances: size up, watch, retreat, regroup (RVR-FIELD WP4) | Stance lines fired (`c81eb5d`, log only); `UT_RvrEngagement` | played (fired), unit | `c81eb5d` | Yes |
| 16 | Regroups no longer flap (PC 342, must) | Exact checks 0; share passes only on the raised bar (F3) | played | `9a173f4` | Yes |
| 17 | Group tactics look like players (PC 346, must) | Not done | none | - | - |
| 18 | Armies leave a milegate by more than one road (RN-1.2) | 47 `RVR_LANE_ASSIGN`, 17 fallbacks, 15 rejected; arrival by lane not counted (PC 347) | played (assigned only) | `19e7037` | Yes |
| 19 | The column arrives at the siege camp within the stated spread (RVR-FIELD Feet) | `UT_RvrLaneChoice` on a made-up frontier | unit | - | - |
| 20 | Catalogs rebuilt from a clean release world (PC 349, must) | 98 spots, 18 circuits, 400 walks 0 failed, 318/332 lanes loaded; clean source not in the log | played (loaded) | `19e7037` | Partly (#417 changed weighting) |
| 21 | Eight-man groups roam the fringes (RN-1.2) | 96 checks, 4 hunts, 0 move-ons (`19e7037`); 10 checks, 1 hunt (`4be6fab`); "Under investigation" (PC 345) | played (mostly failing) | `4be6fab` | Yes |
| 22 | Roaming eights find fights (RN-1.2.1, #417 closed) | `UT_RvrRoamFindsFights` | unit | - | Never played |
| 23 | Population split 55/30/15 (RVR-FIELD) | `UT_RvrRoamSplit`; played contradicts (F10) | unit vs played | `19e7037` | Yes |
| 24 | Skill tiers 30/50/20 (RVR-FIELD) | 32/48/20 across 535 bots (RN-1.2 Seen) | played | `c81eb5d` | No known change |
| 25 | A gank group moves on within 3 minutes (RVR-FIELD Safeguards) | 0 `RVR_ROAM_MOVE_ON` (`19e7037`); `UT_RvrRoamCircuits` | unit; played shows none | `19e7037` | Yes |
| 26 | Levelling bots hunted by eights (RVR-FIELD measures) | 4 `RVR_ROAM_HUNT` (`19e7037`) | played (barely) | `19e7037` | Yes |
| 27 | The frontier isn't dead: half the old kill rate per bot (PC 344, must) | about 40% of `ccba364`'s rate, 30 minutes, low population | played (failing, open) | `4be6fab` | Yes |
| 28 | Sieges still happen and keeps change hands (PC 348, must) | 4 sieges, 2 keeps taken (Caer Erasleigh, twice) | played | `19e7037` | Yes (#416 lord reach, 1.2.2 formation) |
| 29 | Keeps changing hands: 15 in one evening (RN-1.2 Seen) | As stated | played | `ccba364` | Yes: before WP0-WP6 |
| 30 | Keep sieges with a fight on both sides; defenders called at the march (RN-1.2, 1.1.3) | 1.1.3 RC nights: 12 of 18 captured before the fix; `UT_SiegeDefenseAtMarch` after | unit (after fix) | - | No PC count since |
| 31 | Siege balance near 50%, band 35-65% (RVR-FIELD) | `UT_SiegeBalanceNov06` (F9) | sim | - | - |
| 32 | Attackers walk into a lord's reach before striking (RN-1.2.1, #416) | `UT_BattlegroundLordReach`; `RVR_LORD_STEP_IN` never seen | unit | - | Never played |
| 33 | Bots fight keep and tower lords in formation (RN-1.2.2) | `UT_RvrLordFormation` (pure geometry; spots snapped to mesh only in play) | unit | - | Never played |
| 34 | Battleground waves and storms on the keep lord (RN-1.2) | One capture in about eight builds (F6) | played (one success) | `43c43aa` | Yes (#416, 1.2.2) |
| 35 | Battleground keeps stay at level 1 (#249) | `BG_KEEP_STATUS` guards 28, lord 33 | played | `d64d005` | No known change |
| 36 | A rally spot per realm (#255) | `BG_RALLY_PLACED` 2,546 apart | played | `0026378` | Yes (#271, #277) |
| 37 | Battleground guards and lords at their bracket (#373) | Caer Caledon repaired 46→37 on first start | played | `43c43aa` | No known change |
| 38 | + Lv.20 bots arrive with starter gear (RN-1.2) | 183 of 410 battleground deaths had no armour (`0026378`); fix not looked at since | none after fix | - | - |
| 39 | Battleground keeps can't be claimed by guilds (RN-1.2) | `UT_BattlegroundKeepLevel` | unit | - | - |
| 40 | Bot guilds claim keeps (RN-1.2 "keep claims"; PC 497, must) | "Not seen" | unit (`UT_AutonomousBotGuildKeeps`) | - | - |
| 41 | Bots sit on a held keep's walls (RN-1.2 Movement) | PC 399: not run; `UT_BotKeepWallSpots` | unit | - | - |
| 42 | Circle-strafing in RvR (RN-1.2 Movement) | "movement habits" in the log only (`c81eb5d`), not watched | played (fired) | `c81eb5d` | - |
| 43 | Route traps fixed: Cruachan seam, Odin's Gate posts, keep-route restart, rally-delay alarm (#376) | Cruachan column walked, none stuck; alarm 5 in 32 min vs 50 in 40; 2 bots restarted (`RVR_KEEP_ROUTE_RESTART`) | played | `aa537ce` | Yes (1.2.1 restart change `3cafbb1f`) |
| 44 | Odin's Gate muster posts reachable (#376) | Castle Sauvage muster seen walkable; Odin's Gate not reported | none for Odin's Gate | - | - |
| 45 | A keep defence with no defenders stands down (RN-1.2.1) | `UT_RvrRoutesAa537` | unit | - | Never played |
| 46 | Bots that logged out beside an enemy keep log in at their capital (RN-1.2.1) | `UT_RvrRoutesAa537` | unit | - | Never played |
| 47 | Frontier porters: the nearest that sells the medallion (#418, RN-1.2.1) | `UT_FrontierPorterWait418`; `RVR_FRONTIER_WAIT_LONG` never seen | unit | - | Never played |
| 48 | Lough Derg / Bri Leith seam loops fixed (#421, 1.2.2) | `seam_route_sim.py` on the 1.2 download's meshes; `UT_SeamChoiceSince12` | sim (real mesh data) | - | Never played; issue open |
| 49 | RvR and battleground kill quests count gamebots (#243) | `UT_RvrQuestTargets`; PC 332: no record | unit | - | - |
| 50 | A player and seven companions can take a relic (owner 2026-10-15) | `UT_RelicPadAttackers` | unit | - | - |
| 51 | Relic raids resume as campaigns (relic audit 2026-10-31) | `UT_RelicRaidFixes` | unit | - | - |
| 52 | A passing healer rezzes a player after RvR (1.2 prep) | `UT_RezStrangersAfterRvr` | unit | - | - |
| 53 | Night sieges: thin musters explained (NIGHT-SIEGES §2) | "Why night musters were thinner is not shown by these logs" | played (open) | `616190a` | Yes |
| 54 | Known issue: battleground keeps change hands only now and then (KNOWN-ISSUES) | Matches played | played | `43c43aa` | - |
| 55 | Known issue: regroup can reach a neighbouring group (KNOWN-ISSUES) | Matches played | played | `9a173f4` | - |

**Count.** 55 claims. Played and working: 8 (rows 1, 24, 28, 29, 35, 36, 37, 43), all on builds whose RvR code has
changed since, bar rows 24, 35 and 37. Played, one success in many tries: 1 (row 34). Played, fired only (the log line
appeared; nobody judged the behaviour): 11. Played and failing or open: 6 (rows 16, 21, 25, 26, 27, 53). Tests only
(unit or sim): 23. Nothing: 4 (rows 3, 17, 38, 44). Known issues that match play: 2.

## 2. Red-team of the RvR tests and sims

### 2.1 What the 89 classes are

A rough split by my reading of each class (case counts from the run; the first, fourth and sixth rows are estimates).

| Kind | Classes | Cases | What they prove |
|---|---|---|---|
| Pure rules on plain records (tactics, perception, stances, lanes, circuits, rosters, formation) | 34 | about 450 | The rule computes what its author wrote |
| Pure battleground rules | 10 | 274 | The last failed log's shape cannot recur |
| World-less gamebots (`DutyLeaseTestKit`: scope, duty, hooks) | 9 | about 70 | Who is in or out of scope; leases |
| Siege and event layer rules (`RealmEventPolicy`, `SiegeDefenseBalance`, equipment) | 20 | about 200 | Bookkeeping, caps, timers, payments |
| In-test sims (perception Monte Carlo, siege balance, roam split, Crypt Lord court) | 5 | about 40 | Formulas against made-up inputs |
| Fake-mesh route rules | 4 | about 35 | Selection logic, if the mesh is as stubbed |
| Real-mesh probes and catalog checks (Explicit or CI-excluded) | 11 | not run | Routes on the installed meshes (PC only) |

### 2.2 What every RvR test assumes
- **No world:** no region ticks, no `Think()`, no combat, no spells, no damage, no deaths. Every "fight" is a list of
  records.
- **Perfect perception:** boards list enemies as given. The perception sims give open line of sight and drawn facing.
- **No pathing:** walkers and pathing services are stand-ins; a walk succeeds because the test says so.
- **Fixed rosters:** pools and parties are built by hand; no login waves, no deaths, no drafting by other events.
- **One step:** a rule is asked once or a few times; no test runs a minute of a fight.

### 2.3 Real failures no RvR test can see
- A board built wrong from the world (`BuildBoard` is untested): wrong side, stale positions, a pet counted as a healer.
- Two layers fighting over one bot (event layer vs brain hook vs combat movement), the class F2 points at.
- A group that plans well and never meets an enemy (#417's whole symptom).
- Pathing: a backline spot or a kite step that is off the mesh, on a wall or through a keep door.
- Combat rules: a lord immune beyond 400 (#416 was found in code, not by a test), mez immunity timers, interrupts.
- Load: 2,875 online (`43c43aa`); budgets are checked only by `UT_RvrPerceptionCalibration`'s ray counts.
- What the player sees: no test or log line records the player's own RvR death, who killed him, or what he saw.

### 2.4 Tests that pin a constant or an assumption, not a behaviour
- `UT_RvrRoamSplit.Over_many_bots_the_split_settles_at_55_30_15` (an owner assumption; F10).
- `UT_RvrGroupSkill.Groups_are_thirty_fifty_twenty_at_normal` (an owner decision; played agrees, row 24).
- `UT_RvrPerceptionCalibration` medians (fitted to the same table; F1).
- `UT_RvrTacticsHooks.Every_switch_ships_on_in_the_release_world` (the owner's default rule; useful as a guard).
- `UT_MarfachAndNightSieges.TheMusterWaitsForSixtyFourOrAHundredAndTwenty`, `UT_RvrLordFormation.TheRadiiAreUpstreams`,
  `UT_BattlegroundKeepGuardLevels.ReleaseWorldDefaults_*` (data guards; fine as such).
- `UT_SiegeBalanceNov06.TheOwnersNightWouldHaveTrimmedTheAttackersByTheThirdCapture` (replays inputs; F9).

### 2.5 Sims written by the change they grade
Every in-test sim whose origin is in the fetched history was added in the same commit as the code it grades: siege
balance (`6a6930c9`), roam split (#302, `312752a6`), the quiet-frontier pass sim (#306, `0a09767c`), the Crypt Lord
court sim (#407, `8cccd127`; a raid sim, listed because it shares the pattern). The perception calibration (#80)
predates the fetched history. No sim was found written by someone other than the rule's author. (Git, read.)

### 2.6 Tests that would pass with broken behaviour
- `UT_RvrTacticsHooks` stub cases: pass if every hook is dead in play (F8).
- All tactics classes: pass if `BuildBoard` puts no enemy on the board (F2).
- `UT_RvrRouteTraps43c`: passes if the chosen muster spot is off the real mesh (F7).
- `UT_RvrLaneChoice.The_column_arrives_at_the_siege_camp_within_the_stated_spread`: passes if real lanes never
  rejoin (made-up frontier).
- `UT_FrontierPorterWait418.TheNearestPorterThatSellsTheTicketIsChosen`: passes if the bot still declines boarding
  for another reason (the long-wait line was added for exactly that case and has never been seen).
- `UT_BattlegroundLordRally` (111 cases): passes if no storm reaches the lord's floor.
- `UT_SiegeMusterAndRankOct6.WeightedRank...`: passes or fails on test order, not on its rule (run above).

### 2.7 Missing tests
- One group-vs-group fight on a real region, with real combat (5.2).
- A battleground storm and a frontier keep siege to the lord, on real meshes.
- `RvrFightBoards.BuildBoard` from real `GameLiving`s in a region.
- The player as target: a player in range of a bot group is noticed at the table's times with the lookers' cap on.
- The checker's own RvR rows on a recorded log excerpt (the regroup row has tests in
  `source/tools/tests/test_played_check_log.py`; the notice row has none that would have caught F1).

### 2.8 The five most misleading green tests
1. `UT_RvrPerceptionCalibration.The_median_time_to_notice_matches_the_target_for_each_level_and_band`: cited as the
   perception table holding; it checks a fitted formula against its own targets (F1).
2. `UT_RvrTacticsHooks.With_every_switch_on_each_brain_hook_has_no_opinion`: says "stub"; the hooks are live; passes
   because nothing has a world (F8).
3. `UT_SiegeBalanceNov06.TheBalanceSettlesNearHalf`: cited for the 35-65% band; assumes the balance moves win rates,
   which the PC nights contradict (F9).
4. `UT_RvrRoamFindsFights.With_no_levellers_about_the_circuit_with_the_enemy_rvr_bots_draws_the_eight`: #417 was
   closed on it; the previous green fix for the same symptom (`UT_RvrQuietFrontier`) failed in play (F10, F4).
5. `UT_RvrEngagement.One_fight_reaches_the_warband_once_and_never_echoes_back`: the relay class was green on every
   build the PC contradicted; it has no dense warband (F3).

## 3. The owner-seen record: RvR defects seen in play

| Defect seen in play | Build | Test before? | Fix | Fix seen in play? | Recurred? |
|---|---|---|---|---|---|
| Bots lock on at 5,000 units | before 1.2 | No | #53, #80 | Yes, for the player (`3d67b6f`) | Bot-on-bot never checked (F1) |
| No `RVR_SPOT` on the player | `3d67b6f` | No | none | No | Not looked for |
| Battleground keep at level 4 after capture | `3b808b4` | No | #249 | Yes (`d64d005`) | No |
| Both realms on one rally spot | `d64d005` | No | #255, #271, #277 | Yes (`0026378`) | - |
| Battleground waves late, die one by one, never reach the lord | `3d67b6f` to `c3cd93c` | Pure tests after each | #247, #248, #251, #259, #280, #284, #288 | One capture (`43c43aa`), after a data repair | Yes, every build |
| Battleground guards and lord over bracket | `c81eb5d` | No | #373 | Yes (`43c43aa`) | No |
| Battleground lord loses only 1-3% | `43c43aa` | No | #416 lord reach | No | Unknown |
| Regroup flap | `19e7037` | Yes (green) | #304 | 30 min on `4be6fab` | Yes: `453e01a`, `5378dbe`, `b1b6846` (F3) |
| Roaming eights rarely hunt | `19e7037` | Yes (green) | #306 | No | Yes: #417 |
| Roaming eights rarely find a fight | `19e7037`, `4be6fab` | Yes (green) | #417 | No | Unknown |
| Frontier kill rate fell to about 40% | `4be6fab` | No | none | No | Open |
| Cruachan Gorge seam loop, 75 bots | `43c43aa` | No | #376 | Yes (`aa537ce`) | No |
| Odin's Gate muster posts unreachable | `43c43aa` | No | #376 | Not reported | - |
| "No connected exterior route" repeats | `43c43aa` | No | #376 | Yes (`aa537ce`: restarts) | - |
| Rally-delay false alarms (39 of 50) | `43c43aa` | No | #376 | Yes (5 in 32 min, `aa537ce`) | - |
| Defence marched with 0 defenders | `aa537ce` | No | `2a25d2fc` | No | - |
| Bots log in beside an enemy keep | `aa537ce` | No | 1.2.1 | No | - |
| Long waits at frontier porters | before 1.2.1 | No | #418 | No | - |
| Lough Derg / Bri Leith seam loops | before 1.2.1 | No | #434, #447 | No | Open |
| Night sieges thin; attackers take 13 of 17 | `616190a`, `168f55d` | No | #77, #88 | No count since | - |
| 12 of 18 sieges captured, defenders late | 1.1.3 RC | No | 1.1.3 defence at march | No count since | - |
| Battleground bots arrive without armour | `0026378` | No | owner rule (starter gear) | No | - |

**Totals.** 22 defects seen in play. A test existed before the defect in 3 (all green while it happened). Fix seen in
play since: 7 (player perception, battleground keep level, rally spots, bracket levels, the Cruachan seam, the exterior
route restart, the rally-delay alarm). Mixed: 2 (regroups: the exact checks hold, the share passes only on a raised bar;
battleground storms: one capture). **Fixes still tests only: 11** (#306, #416, #417, #418, #421, the Odin's Gate posts,
the defence stand-down, the enemy-keep login, the night-siege trim, defence at the march, starter gear). No fix: 2 (the
spot call on the player, the kill rate). No Reddit report about RvR is in the repo (`docs/1.2.1/REDDIT-POST-1.2.md` is
our own post).

## 4. The log tags

98 tags with the prefixes `RVR_`, `BG_` and `SIEGE_` are written by the server (count from the code). Layers:

| Layer | Tags (count) | Writer |
|---|---|---|
| Perception | `RVR_NOTICE`, `RVR_SPOT`, `RVR_SCAN_BUDGET` (3) | `AutonomousWorldBotController.RvrPerception.cs`, `RvrScanRoster.cs` |
| Fight board and stances | `RVR_FIGHT_BOARD`, `RVR_STANCE`, `RVR_SIZE_UP`, `RVR_WATCH`, `RVR_RETREAT`, `RVR_REGROUP` (6) | `RvrFightBoards.cs`, `RvrEngagementRuntime.cs` |
| Brain tactics | `RVR_ASSIST_CALL`, `RVR_ASSIST_SWITCH`, `RVR_CC_ASSIGN`, `RVR_INTERRUPT`, `RVR_CHASE`, `RVR_PEEL`, `RVR_BACKLINE`, `RVR_KITE`, `RVR_STEALTH_OPEN` (9) | `BotBrain.RvrTactics.cs`, `BotBrain.RvrPositioning.cs`, `BotBrain.TankPeel.cs`, `BotRvrAmbush.cs` |
| Roaming | `RVR_PARTY_FORMED`, `RVR_PARTY_WAIT`, `RVR_ROAM_CHECK`, `RVR_ROAM_CIRCUIT`, `RVR_ROAM_HUNT`, `RVR_ROAM_CHASE`, `RVR_ROAM_MOVE_ON`, `RVR_CHECKSPOT_*` (3) (10) | `AutonomousBotGroupCoordinator.RvrTactics.cs`, `AutonomousWorldBotController.RvrTactics.cs`, `RvrFrontierPoi.cs` |
| Travel and lanes | `RVR_LANE_*` (4), `RVR_MARCH_*` (3), `RVR_KEEP_ROUTE_FAILED`, `RVR_KEEP_ROUTE_RESTART`, `RVR_KEEP_TRAVEL_STALL`, `RVR_FRONTIER_DEPARTURE`, `RVR_FRONTIER_WAIT_LONG`, `RVR_BORDER_STAGING` (14) | `...Rally.cs`, `...KeepTravel.cs`, `...FrontierTransport.cs`, `RvrLaneCatalog.cs` |
| Events and sieges | `RVR_EVENT_*` (3), `RVR_SIEGE*` (5), `RVR_DEFENSE_*` (5), `RVR_MUSTER_EXTENDED`, `RVR_RALLY_DELAY`, `RVR_REINFORCEMENTS`, `RVR_RETAKE_STARTED`, `RVR_HOME_DEFENSE_BREAKOFF`, `RVR_DEFENSE_POST_RETRY`, `SIEGE_DEFENSE_BALANCE` (21) | `AutonomousRvrEventLayer*.cs`, `SiegeDefenseBalance.cs`, `...Siege.cs` |
| Relics | `RVR_RELIC_*` (12) | `AutonomousRvrEventLayer*.cs`, `AutonomousRvrRelicPersistence.cs` |
| Lords | `RVR_LORD_STEP_IN`, `RVR_LORD_FORMATION` (2) | `...Relic.cs`, `...LordFormation.cs` |
| Outcomes | `RVR_BOT_KILLED`, `RVR_KEEP_TAKEN` (2) | `AutonomousBotRealmPointRewards.cs`, `AutonomousRvrDashboard.cs` |
| Battlegrounds | `BG_*` (20) | `AutonomousBattlegroundKeepRally.cs`, `AutonomousBattlegroundWave.cs`, `...Battleground.cs`, `AutonomousRvrDashboard.cs` |

(`SIEGE_SALVAGE_MATERIALS` is crafting.) `RVR_MUSTER`, named in PLAYED-CHECK-1.2.md line 361, is not a tag the code
writes; the nearest are `RVR_SIEGE_MARCH` and `RVR_MUSTER_EXTENDED`.

**What a reader cannot reconstruct today (read):**
- **A fight.** No fight id. `RVR_STANCE`, `RVR_ASSIST_*` and `RVR_CC_ASSIGN` carry `group=`; `RVR_BOT_KILLED` carries
  victim and killer names, but no group of either. Kills cannot be tied to the stance, call or plan that led to them.
- **Its outcome.** No line says a fight ended, who held the field, or how many died on each side.
- **Who perceived whom, for the player.** `RVR_NOTICE` covers the player as a target, but nothing logs the player being
  called (`RVR_SPOT` on the player) by default, and nothing logs the player's own death in RvR or who killed him.
- **Damage on the called target.** RVR-FIELD's "share of a group's damage on the called target" target has no log.
- **First kills by role.** `RVR_BOT_KILLED` has no victim role; the "first kills are healers/casters" measure needs one.
- **Why a fight was chosen or refused.** `RVR_SIZE_UP` has `odds` and `decision`; there is no line when a group sees an
  enemy and decides nothing (out of scope, duty, capped).
- **Why a siege stalled.** `RVR_SIEGE_PRESENCE` gives committed and present every 5 minutes; no line names what holds a
  siege (door hit points not falling, no ram placed, guards killing, attackers not at the door).
- **Where bots got stuck.** `RVR_KEEP_TRAVEL_STALL` and the `AUTONOMOUS_ROUTE_*` lines exist; there is no hourly roll-up.
- **What the player saw.** Nothing: no line marks bots within the player's view, or a fight the player could see.

**Rows to add** (logging only; behaviour unchanged):
1. `RVR_FIGHT_OPEN` / `RVR_FIGHT_END`: fight id, region, centre, groups and realms in it, start/end, deaths per realm,
   who held the field. Stamp `fight=` on `RVR_STANCE`, `RVR_ASSIST_*`, `RVR_CC_ASSIGN`, `RVR_PEEL`, `RVR_KITE`,
   `RVR_BOT_KILLED`.
2. `RVR_BOT_KILLED`: add `victimGroup=`, `victimRole=`, `killerGroup=`, `calledTarget=true|false`.
3. `RVR_PLAYER_KILLED`: the human's death in RvR: killer, killer group, how long since first noticed by that group.
4. `RVR_PLAYER_NOTICED`: a bot group first noticing the player: distance, `time_since_first_los_ms`, looker, called.
5. `RVR_SIEGE_STALL` every 5 minutes of a battle with no door or lord progress: attackers at the door, engines placed,
   door hit points, guard kills, deaths.
6. `RVR_HOUR`: an hourly roll-up per realm: bots in the frontier by kind (warband/eight/solo), fights, kills, captures,
   stalls by place.

## 5. Instruments

### 5.1 An "RvR hour" report from one PC log
A section of `played_check_log.py` (or a sibling script) that reads one log and prints, per hour and per realm:
- population: bots in the frontier by kind (from `RVR_PARTY_FORMED` and `RVR_ROAM_*` until `RVR_HOUR` exists);
- perception: notices by distance band with the median of `time_since_first_los_ms`, against the table (fixes F1);
- fights: count, size, deaths, where (a 4,000-unit grid), the busiest cell's share (RVR-FIELD's 18% hot-spot measure);
- tactics: assist calls and switches per fight; called-target kills over all kills (once row 2 above exists);
- roaming: checks, hunts, chases, move-ons per eight;
- sieges: started, captured, defended, failed rally, by `kind` and `thin`; attacker win rate (F9);
- travel: route failures, restarts, porter waits, stalls by place;
- each figure stamped with the log's `FORK_BUILD` commit and whether RvR code changed since (F5).

### 5.2 An end-to-end fight harness worth building in CI
- A `Rehearsal`-style fixture for RvR: one real `Region` with a small flat stand-in mesh (or one real zone's mesh
  checked into test data if its size allows), two bot-only groups of 8 built by `RvrRosterTemplates`, real classes,
  real spells and the game loop ticked for 120 simulated seconds.
- Asserts on outcomes, not calls: someone noticed the other inside the table's band; a target was called; at least
  half the first three deaths were healers or casters; a mezzed bot was not the kill target; the losing side retreated
  or died; the run is deterministic by seed.
- Second scenario: a 24-bot storm on a battleground lord's floor, asserting the lord's health falls below 80%.
- Cost: L. It would catch F2's whole class and F8.

### 5.3 A blind review protocol
- Scenarios are written by someone who did not write the code (another agent or the owner), from the reference rubric
  (`reference.md`, #493), before reading the implementation.
- Each scenario names a place, a starting state and what a 2003 player would see. The reviewer runs it (harness or PC)
  and grades against the rubric, not against the code's comments.
- Fixers do not edit the scenarios. A failed scenario reopens the issue. Issues close only on a played result (F4).

## 6. PC checklist for the next frontier session

Ordered by how much each settles. For #503. On the newest build; note `FORK_BUILD`. Keep the log.

| # | Where | Do | Watch | Log tag that confirms |
|---|---|---|---|---|
| 1 | Hadrian's Wall, level 50, GM off | Stand still in the open 2,500 from a bot group for 2 minutes | When and from how far they notice you; whether others come together or drip in | `RVR_NOTICE kind=Player distance= time_since_first_los_ms=`; `RVR_SPOT target=<you>` |
| 2 | Same | Run past a bot group at 3,000 | Do they ignore you most passes? | `RVR_NOTICE` absent or late |
| 3 | Pennine / Hadrian's border hot spot | Watch one bot-vs-bot fight for 20 minutes from cover (the owner's 20-minute look) | One target at a time? Healers die first? Mez on the right bot? A side runs when losing? | `RVR_ASSIST_CALL`, `RVR_CC_ASSIGN`, `RVR_RETREAT`, `RVR_BOT_KILLED` |
| 4 | Any frontier | Join a bot-only fight as a target: let one group attack you | Who hits you first, how fast you die, whether a peel or mez lands on you | `RVR_ASSIST_CALL target=<you>`, `RVR_CC_ASSIGN subject=<you>` |
| 5 | A frontier keep under bot siege | Watch the lord room once doors fall | Melee in front, casters behind, lord's health falls | `RVR_LORD_STEP_IN walk=`, `RVR_LORD_FORMATION`, `RVR_KEEP_TAKEN` |
| 6 | Thidranki | One hour with + Lv.20 bots | Any capture; storms reach the lord; lord below 80% | `BG_RALLY_STATUS inLordReach=`, `RVR_KEEP_TAKEN battleground=True` |
| 7 | Whole log | Run the server 4 hours at daytime population | Keeps taken and held per realm | `RVR_EVENT_ENDED` by `kind`, `thin`; `SIEGE_DEFENSE_BALANCE` |
| 8 | Whole log | Same run | Roaming eights: formed, walked, hunted, moved on | `RVR_PARTY_FORMED`, `RVR_ROAM_CIRCUIT enemiesNear=`, `RVR_ROAM_CHASE`, `RVR_ROAM_MOVE_ON` |
| 9 | A milegate an army passes | Watch the march leave | More than one road used | `RVR_LANE_ASSIGN`, `RVR_LANE_FALLBACK` |
| 10 | Whole log | Same run | Kill rate per online bot against `ccba364` | `RVR_BOT_KILLED` per hour; online count |
| 11 | Odin's Gate muster (100:596055,581400) | Watch a Midgard or enemy muster there | Every slot on walkable ground, none sent home | no `AUTONOMOUS_ROUTE_RECOVERY` within 1,500 |
| 12 | Frontier porters at a border keep | Watch 10 minutes at muster time | No bot waits past 5 minutes | no `RVR_FRONTIER_WAIT_LONG`; else its `phase=` |
| 13 | Lough Derg / Bri Leith (200:384960,486688) | Watch bots crossing | No looping | no `AUTONOMOUS_ZONE_SEAM_CYCLE` there |
| 14 | A keep your realm holds | Walk up with no siege on | Bots on the walls stand when an enemy shows | `BOT_MOVE_HABITS` (wall) |
| 15 | A bot stealther's milegate | Walk through 5 times | Are you opened on from stealth? Fair? | `RVR_STEALTH_OPEN target=<you>` |
| 16 | Any frontier | Stealth (if your class can) near a bot group | Noticed only close up | `RVR_NOTICE` with `stealthLimit=` |
| 17 | Whole log | Same run | Regroup relay: exact checks still 0 | checker rows (a), (b), (c), fan-out |
| 18 | A defended keep | Start a siege with a GM army, let the defence muster | Defence with no defenders stands down | `RVR_DEFENSE_MUSTER_EMPTY` |
| 19 | Log in a bot beside an enemy keep (restart) | Restart the server | It logs in at its capital | the login line for that bot |
| 20 | Battleground | Check a + Lv.20 bot's gear | It arrives with starter gear | inspect; `BG_` death lines with armour |

## What I checked and found sound

- The exact relay checks (a), (b), (c) and their PC results: real one-hop evidence (F3).
- `UT_BattlegroundLordReach`'s tie to the upstream lord damage radius (F6).
- The start-up re-walk of the check-spot and lane catalogs, and its PC result on `19e7037` (F7).
- Scope fencing: `UT_RvrTacticsScope` covers every lease kind there is (`Every_lease_kind_there_is_has_a_case_above`).
- The perception code logs the right field (`time_since_first_los_ms`); only the checker misses it (F1).
- No RvR test passed by `Assume` or `Ignore` in this run.

## What I could not check

- Git history before 2026-11-13 (not fetched), so the self-grading count (F4) covers 50 of 89 classes.
- Commits `43c43aa` and `aa537ce` are not in the fetched branches; I used `631ba1f` (#376, the code `aa537ce` carried).
- The PC logs themselves: every played figure here is as the docs record it.
- The real-mesh probes: no navmeshes in this container.

## What only the PC can settle

The checklist in section 6. Rows 1, 3 and 6 settle the most: whether bots perceive and fight like people, and whether a
keep can be taken.

## Hand-back

- `docs/rvr-audit-1.2.3/evidence.md`, docs only. RvR tests on `main` `de53d0d`: 1,057 cases, 1,056 passed, 1 failed
  (`UT_SiegeMusterAndRankOct6.WeightedRank...`, order-dependent: fails alone, passes in the full CI suite, 9,780/0).
- F1: the bot-on-bot perception row reads `delayMs` (designed 0.5-2 s) instead of `time_since_first_los_ms`; its
  calibration test checks a formula against the table it was fitted to.
- F2: no test runs a fight; `RvrFightBoards.BuildBoard` is untested; PC evidence for tactics is "fired" counts.
- F3: the regroup share bar went 10 → 11.5 → 15 → 20% after builds exceeded it; the exact checks are the real proof.
- F4: 48 of 50 recent RvR test classes came in the fix's own commit; #416-#418 were closed on tests only.
- F5: all RvR "Seen" is from builds before 1.2.1/1.2.2 (1,824+/1,110- lines of RvR code since `631ba1f`).
- F6-F10: battleground tests vs one capture; fake-mesh route tests; a stale stub test; a siege-balance sim that assumes
  its lever; a population split that play contradicted.
- Ledger: 55 claims; 8 played and working (5 on builds older than today's code), 11 "fired" only, 23 tests only,
  4 nothing.
- Owner-seen: 22 defects; 7 fixes seen in play since; 11 still tests only; 3 had a green test while they happened.
- Five most misleading green tests: section 2.8. Log rows to add: section 4. PC checklist (20 rows): section 6.

## Appendix: the 89 classes run

UT_AutonomousBotGuildKeeps, UT_AutonomousPopulationRamp, UT_AutonomousRvrDirector, UT_AutonomousRvrEventLayer,
UT_AutonomousSiegeEquipment, UT_AutonomousSiegePolicy, UT_BardBotCrowdControlPolicy, UT_BattlegroundGateClear,
UT_BattlegroundGoalsOct7, UT_BattlegroundKeepGuardLevels, UT_BattlegroundKeepLevel, UT_BattlegroundKeepRally,
UT_BattlegroundLordRally, UT_BattlegroundLordReach, UT_BattlegroundLore, UT_BattlegroundStormRoute,
UT_BattlegroundWaveSmallPopulation, UT_BotKeepWallSpots, UT_CompanionCrowdControlRow, UT_CompanionFrontierAggro,
UT_CompanionRaidSiege, UT_CompanionStealth, UT_CryptLordCourtSim, UT_DutyLeaseBattleground, UT_DutyLeaseRvrEvent,
UT_EncounterFloodGuard, UT_FrontierPorterWait418, UT_FrontierThreatScan, UT_FrontierTravelContracts, UT_GroupPullOrder,
UT_KeepApproachNavigation, UT_MarfachAndNightSieges, UT_NpcRecastGuard, UT_PetCasterUpkeepOct5, UT_PetOrderTargetGuard,
UT_PhysicalSiegeRally, UT_PlayerKeepDefense, UT_QuestMonsterFellowsAndGuards, UT_RaidStagingAndCrowdControl,
UT_RelicPadAttackers, UT_RelicRaidFixes, UT_RelicShrineReach (Explicit), UT_RezStrangersAfterRvr, UT_RvrAssist,
UT_RvrCheckSpotCatalog (Explicit), UT_RvrCheckSpotLoad, UT_RvrCrowdControlPlan, UT_RvrEngagement, UT_RvrExpansion,
UT_RvrFightBoard, UT_RvrGroupSkill, UT_RvrInterruptPlan, UT_RvrLaneCatalog, UT_RvrLaneChoice,
UT_RvrLaneSurvey (Explicit), UT_RvrLeaderStaging, UT_RvrLordFormation, UT_RvrPeelPlan, UT_RvrPerception,
UT_RvrPerceptionBots, UT_RvrPerceptionCalibration, UT_RvrPerceptionDuty, UT_RvrPetCap, UT_RvrPositioning,
UT_RvrQuestTargets, UT_RvrQuietFrontier, UT_RvrRoamCircuits, UT_RvrRoamFindsFights, UT_RvrRoamScope, UT_RvrRoamSplit,
UT_RvrRosterBuildRoles, UT_RvrRosterTemplates, UT_RvrRouteTrapProbe (Explicit), UT_RvrRouteTraps43c, UT_RvrRouteWork,
UT_RvrRoutesAa537, UT_RvrSoloEngage, UT_RvrTacticsHooks, UT_RvrTacticsScope, UT_SiegeBalanceNov06,
UT_SiegeDefenseAtMarch, UT_SiegeDoorNumber, UT_SiegeMusterAndRankOct6, UT_SiegeOperatorContract, UT_SiegeParties,
UT_SiegePlacement, UT_SiegeSchedulingOct7, UT_StagedSiegeOct6, UT_UnreachableServicesAndDoorDetours.
(`UT_RvrQuietFrontierRoster` ran as a nested class of `UT_RvrQuietFrontier`.)
