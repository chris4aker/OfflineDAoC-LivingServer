# RvR audit, 2026-11-24: framework, audit list and work plan

Internal working doc. The owner asked for "a full audit on RvR - gank squads, solo gankers, keep siege and defense,
pathing, detection, tactics, movement and everything", thought through first with the right framework. The audit is
**report only**: no game behaviour changes until the reports are in and the owner agrees a plan. It is tracked in the
master issue "RvR that passes for the real realm war" and one issue per audit.

## The framework

Last week's chat audit ([../realism-audit-1.2.3/](../realism-audit-1.2.3/README.md)) taught four things: a bench the
fixers wrote said 90% while fresh lines scored 39%; the causes were structural; the measuring graded itself; and only real
play showed it. So this audit:

1. **Judges against the period, not the code's intent.** The target is Old Frontiers RvR as it was played in 2002-2003
   (patch 1.65), written down as a checkable rubric (`reference.md`).
2. **Asks the structure question up front** (`structure.md`): who decides what a bot does in the frontier, do the layers
   agree, and does a fix land once or in one path only.
3. **Distrusts our own tests** (`evidence.md`): every claim is marked played / world sim / unit test / read; the green
   tests that mislead are named; an hour of frontier play becomes an auditable record.
4. **Follows whole chains from the player's seat**: perceive → decide → move → fight → win or lose → what next. For each
   link: what one human sees, how often per hour, and whether it is fair.

## The audit list

| Report | Covers |
|---|---|
| `reference.md` | The rubric of period RvR: the map of the war, who was out there, how an 8-man fought, stealth and detection, sieges, movement, etiquette. A one-page tester's checklist. |
| `structure.md` | Every RvR decision maker, hand-off and state store; structural causes; a target structure; how to record and measure. |
| `evidence.md` | The evidence ledger (played or tests only), a red-team of the RvR tests and sims, the log-tag inventory, instruments, a PC checklist. |
| `roam.md` | Gank squads and the zerg: forming, roaming, choosing fights, crowd control, assist, interrupts, peel, disengage, what a solo player meets. |
| `stealth.md` | Solo gankers (assassins, archers, soloers) and the whole perception model: how any bot or guard comes to know an enemy exists. |
| `siege.md` | Attack: target choice, muster, travel, rams, doors, guards, the lord room, claiming, towers, relic raids, battleground keeps. |
| `defense.md` | Defense: the alarm, who answers, wall spots, oil, repairs, the last stand, relic defense, fought or decided by formula. |
| `pathing.md` | The real frontier navmeshes (gates, bridges, stairs, walls, lord rooms, seams), stalls and teleports, group travel, movement in a fight. |
| `mechanics.md` | The PvP rules as applied to bots and to the player against 1.65, and each class playing its RvR job. |
| `war.md` | The war as a whole (population, pacing, snowballing, staging) and the player's seat (groups, companions, dying, winning, fairness, chat). |

Each report: the ten findings that most break the war for a player, worst first; for each, what a player would see, the
chain link that fails, file:line, how often, the evidence level, the class of defect and where it belongs structurally, a
fix with size and risk; then what was sound, what could not be checked, what only the PC can settle, and tools worth
building.

## Status

The ten audits were started on 2026-11-24 and stopped to save usage before any report was finished. Only a first
draft of section 1 of the reference rubric exists: [reference-draft.md](reference-draft.md). Each audit is now an issue to run
from the owner's PC session or another agent.

## The work plan

1. **Audit** (the ten reports above, in this folder).
2. **Synthesis**: one plan built on `structure.md` and `evidence.md`; work issues by class of defect; a PC checklist.
3. **Record real frontier play first** (no behaviour change): the missing log rows, a flag command, a per-fight and
   per-siege timeline tool, and the "RvR hour" numbers. Shares the say gate and `/odd` of the realism plan where chat is
   involved.
4. **Fix by class where the structure puts it**; patch only true one-offs. Each fix says which build and PC log it was
   seen in; until then it is tests only.

## The shared brief every audit follows


Owner's ask: "a full audit on RvR - gank squads, solo gankers, keep siege and defense, pathing, detection, tactics,
movement and everything." Offline DAoC Living Server is a single-player classic DAoC server (classic + Shrouded Isles,
1.65 rules, **Old Frontiers**, the original client). The other realms' players are autonomous gamebots. The goal: a player
who walks into the frontier meets a realm war that passes for the real one of 2002-2003 — fought by people, not scripts —
and that is fair and fun for one human among bots.

## How we audit (the lesson of last week's chat audit — docs/realism-audit-1.2.3)
A bench that the fixers wrote said 90%; an hour of real play showed nine defects; fresh lines scored 39%. The causes were
structural (many paths, no shared state, rules added per path) and the measuring graded itself. So:
1. **Judge against the period, not against the code's intent.** The reference agent writes the rubric of what real
   Old Frontiers RvR looked like; where you need it before it lands, use what you know of the era and say so.
2. **Hunt classes of defect, not instances**, and say where in the structure each class belongs.
3. **Distrust every existing sim and test.** A unit test of a pure rule says the rule computes; it does not say a fight
   happens. Say for every claim whether it was seen in a played game (cite docs/PLAYED-CHECK-*.md or a build), run in a
   world sim, unit-tested, or only read.
4. **Take the player's seat.** For every behaviour: what does one human see, from where, how often per hour in the
   frontier, and is it fair to them (a level 50 solo, a duo with a companion, a member of a bot group, a level 24 in a
   battleground)?
5. **Trace whole chains, not parts**: perceive → decide → move → fight → win or lose → what next. Most failures are at
   the joins (a group that plans a perfect assist train and never finds the enemy).

## Ground rules
- REPORT ONLY. Change no game behaviour. No merges, no publishing, no AGENTS.md or lore edits, never start a server,
  never rebuild navmeshes.
- Owner's rules that bind any proposal: never weaken bosses, guards, lords or keep levels; bots never play class 63; keep
  the gameplay rules in AGENTS.md; Living Server features default on; tell bot types apart (autonomous gamebots,
  companion bots, real players).
- Repo: chris4aker/offlinedaoc-livingserver. Work on a fresh checkout or worktree of `origin/main`, one per audit.
- RvR code: source/server/GameServer/bots/autonomous/ (Rvr*.cs, AutonomousRvr*.cs, AutonomousSiege*.cs, *Keep*.cs,
  *Relic*.cs, *Battleground*.cs, *Frontier*.cs, AutonomousWorldBotController.Rvr*.cs/.Siege.cs/.Relic.cs, RvrLordFormation,
  BotKeepWallSpots, RealmWarbandSupport, SiegeDefenseBalance...), source/server/GameServer/bots/ (BotBrain.Rvr*.cs,
  BotBrain.PvpCrowdControl.cs, BotRvrAmbush.cs, BotPvpCrowdControl.cs, BotCombatMovement.cs, BotRangedCombat.cs,
  CompanionPvpEngagement.cs, CompanionStealth.cs, the class policies), the base server's keep, guard, siege, stealth and
  PvP code, about 87 RvR unit tests in source/server/Tests/UnitTests, about 100 RVR_* log tags, docs/RVR-FIELD-1.2.md,
  docs/NIGHT-SIEGES-AND-MARFACH.md, docs/PLAYED-CHECK-1.2.md, docs/OPEN-WORK.md, issues #421 and #425.
- No dotnet here. Reading and Python are your main tools. If you truly need to run C# (an existing sim, a report-only
  test printing rows to the CI log), push a branch `audit/rvr-<yourname>`, open a DRAFT PR into main via REST
  (`gh api repos/.../pulls`; GraphQL is blocked; push with `git -c push.negotiate=true push`), read the job log
  (`gh api repos/.../actions/jobs/<id>/logs`; artifacts are blocked), and close the PR when done. Never merge it.
- Navmeshes are not in git. On the owner's PC they are in the install's `runtime/server` folder; elsewhere they can be read from the v0.36b base download (see tools/navmesh in the
  repo: dfprobe.cpp, navscan.cpp, seam_route_sim.py), use it read-only; if it is gone, say what you could not check.
- Web research is allowed for the period (Camelot Herald archives, 2001-2004 guides, forum threads, patch notes up to
  1.65). Paraphrase; never copy passages. Say which facts are from memory and which from a source.

## Your report: docs/rvr-audit-1.2.3/<yourname>.md (in the repo, by a docs-only PR)
- Top: the 10 findings that most break the war for a player, worst first.
- Each finding: what a player would see (concrete: who, where, what happens); the chain link that fails; where it comes
  from (file:line); how often (measured, computed, or a stated guess); evidence level (played / world sim / unit test /
  read); the class of defect and where it belongs structurally; a proposed fix, its size (S/M/L) and its risk to what
  works today.
- Then: what you checked and found sound; what you could not check and why; what only the owner's PC can settle (as
  exact things to look at: a place, an action, a log tag); measuring tools worth building.
- Plain, short sentences. No padding. End with a hand-back summary of at most 25 lines.
