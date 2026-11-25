# Open work (Living Server, from 1.2.1)

Everything known to be unfinished, with a GitHub issue each, so contributors and agents can pick an item up. Read
[AGENTS.md](../AGENTS.md) first: it holds the owner's rules. 1.2.1 is released; branch from `integration/1.2.2` and
open a pull request against it (it merges into main at the 1.2.2 release). Comment on an issue before starting so two
people don't take the same one.

A fix counts as working only once it is seen in a log from the owner's PC. Until then it is "tests only", and every
result names the build it came from. Player-facing status is in [KNOWN-ISSUES.md](KNOWN-ISSUES.md); every change is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

**Status on 2026-11-22:** 1.2.1 is released ([RELEASE-NOTES-1.2.1.md](RELEASE-NOTES-1.2.1.md)). Every 1.2.1 change
below is **tests only**; nothing in 1.2.1 has been seen on the owner's PC. Closed since 1.2: #399, #400, #401, #402,
#403, #404, #419. **Closed on 2026-11-22 as done in 1.2.1** (18; each issue's last comment says what shipped, and it
is reopened if the owner's log shows it again): #396, #397, #405, #406, #407, #408, #409, #411, #413, #415, #416,
#417, #418, #420, #422, #423, #424, #438. Still open from 1.2.1: #410, #412, #414 (raids), #421 (a seam loop), #425
(the PC check).

**What comes next:** 1.2.2 is the raids (its size raises, #441, withdrawn on audit 2026-11-23) and small fixes ([1.2.2](#122-in-development)); Uthgard's drop
tables and the fixes from the PC checks come in [1.2.3](#123); the language selector (#437) in [1.3](#13).

## 1.2.2 (in development)

The raid release on `integration/1.2.2` (owner, 2026-11-22: 1.2.2 is the raids). **Tests only.**

- [#441](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/441) Raid sizes raised at Summoner's Hall, the Green Knight, Galladoria and Tuscaran Glacier.
  **Withdrawn:** the Green Knight's on 2026-11-22, the other three on audit on 2026-11-23 (owner: "Revert all three";
  [RAID-SIZES.md](RAID-SIZES.md), "Withdrawn on audit"). The raise rule stays, with stricter evidence.
- The launcher shows the Living Server build ("LIVING SERVER 1.2.2", from `ForkBuildInfo.BuildName`) instead of the
  retired "VERSION 0.37". **Tests only.**
- GM `/jump` refuses a dead bot and says why (its body stays where it fell); a jumped gamebot plans its trip again
  from the new spot (#438 follow-up). **Tests only.**

## 1.2.3

- **Uthgard drops:** run `source/tools/uthgard_drops_content.py` (dry run first) as [UTHGARD-DROPS.md](UTHGARD-DROPS.md)
  says; the tool is in the code since 1.2.1, and its tables ship in 1.2.3 (owner, 2026-11-22).
- **The fixes from the owner's PC checks** of 1.2.1 and 1.2.2 (the list below).
- **OpenDAoC upstream ports:** every upstream commit from our base `d51233cb` to `428927b33` (632) is classified in
  [upstream/OPENDAOC-LEDGER-2026-11.md](upstream/OPENDAOC-LEDGER-2026-11.md). Ports by area: P1 core, P2 effects, P3
  combat, P4 AI and world (the `fix/1.2.3-upstream-*` branches), plus four extra packages:
  [#562](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/562) housing, guild and market,
  [#563](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/563) player and GM commands,
  [#564](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/564) client display, chat and group window,
  [#565](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/565) quests, gravestones and timed services.
  26 commits wait for the owner (the ledger's "Held for the owner" list: the live interrupt and power pool, the NPC
  defense package, PvE speed, stealth group visibility and others). Nothing ported yet.
- **OpenDAoC upstream port P3** (combat, XP, crafting and items; branch `fix/1.2.3-upstream-combat`, changelog 1.2.3
  "From OpenDAoC upstream: combat, XP, crafting and items"). **Tests only.** Left for later: the attack-speed recalculation
  series in `AttackAction` (`6258566a3`, `27bc0293f`, `a7468c046`, `109a38cf0`), kept apart from the interrupt port in
  the same file; the Savage buff effect refactor (`1713a0e8f`, `9f0bd91b1`, effect code). Held for the owner: the NPC evade
  arc and attacker rule and the NPC guard rule (weaker monsters).
- [#485](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/485) Realism 3a, invites, joins and logouts with a
  person behind them (one invite decision for words and act, human accept pauses, bot-led range and "want in?" waiting for
  a yes, seated logout, wipe releases 15-45 s apart, `bot_show_as_bots` off by default). **In review, tests only**
  (`UT_InvitesLogouts485`). PC check: /invite an idle bot, a fighting bot and a bot in its party; /lfg; watch a "brb"
  logout and a wipe.
- **OpenDAoC upstream port P4** (NPC AI, pathing, keeps and world; branch `fix/1.2.3-upstream-ai-world`). **Tests only.**
  Owed on the PC: a monster or guard landing a spell after its target stepped behind a wall; no monster or gamebot cast
  through a dungeon or keep wall away from the player; a mob climbing a ladder without stalling; a mob chasing over a zone
  border; a gravestone still standing after a restart; group dots on the minimap. Re-run the raid sims before any period
  size decision (bosses now land started casts). Held for the owner: B1/B2/B9 (as briefed), `2ffc5c0ff` (5% interrupt step:
  low mobs interrupt less), `44ae62e96` (stuck NPCs become immune), `76f4a6fd0` (NPCs walk up to a blocking door: changes
  the bot door holds), `b80fae038` (door polygon registration: navmesh evidence needed). Skipped (owner): B6 `eb06c09aa`,
  `970282fba`. Finding for #521: `BotCastSight` checks bot-to-bot casts at any height, so a bot caster on a keep wall may
  be refused at bots below (the navmesh ray can't cross a drop); shots use the one-floor rule.
- [#487](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/487) Realism 4a, movement's small safe fixes (follower
  heading, rest seat kept, spread party ring, emote text, post-kill breather). **In review, tests only.** Open PC check
  below.
- [#475](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/475) Realism 1: record real play (the say gate as a
  pass-through in all 17 exits, `logs/session-chat.log`, `/odd`, `tools/bot-voices/session_transcript.py`, the raw-send
  ratchet). **Built, tests only;** done when a played session's `session-chat.log` gives a transcript an agent can read
  line by line and a count of defects per hour, per channel, per build (baseline: 9 in about an hour on 1.2.2). Next:
  #489's step 2 (the checks in the gate).
- [#478](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/478) Realism 2a: no non-answer as a whole answer, in any
  channel. **Built, tests only** (branch `fix/1.2.3-no-non-answers`, on top of #549). The say gate's step 2 holds the class
  (dunno, try elsewhere, echo word, bare laugh, stock agreement; `BotSayGate.NonAnswer.cs`), the reply planner settles with
  it (`AutonomousChatReplyPath.Settled`), and the "dunno" fallbacks and the `@answers ask` set are gone. Bench: non-answers
  0 in every channel, no good share fell, bad silence rose where the dunnos were (tell 0 to 71, /group 42 to 86, follow-ups
  in a tell 0 to 33): those lines need knowledge (#482). Open: see it in a PC log (`CHAT_HELD rule=non-answer`, and no
  "dunno"/"not sure" rows in `session-chat.log`).
- [#482](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/482) Realism 2e: knowledge a new player needs, as one
  grounded lookup. **Built, tests only** (branch `fix/1.2.3-knowledge`, on top of #585). `AutonomousChatLookup` (typo fold,
  question type, providers per type, every answer with its source) and `data/chat/game_guide.json`. OPENWORK_LINE
- **The honest chat bench** ([#477](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/477), realism 1b): done
  on `feature/1.2.3-honest-bench`, **tests only**. The bench's headline is the realism audit's fresh lines (155 of 262 good,
  59.2%), reported per kind and per channel, with the side benches (every channel path, 54 follow-ups, 40 lines of the
  moment, the two-line bench) and the evening ratchet (`evening_read.py`, `evening_ratchet.json`). The baselines were reset
  to these numbers on 2026-11-24. Draft PRs #470-#472 can be closed once it merges. Left: the 167 data-gap lines wait for
  facts from the world database; the evening stream is still a loop of its own beside `UT_ChatBudgetSimulation.Evening`
  (same fixture, same items and keeps; a change to Evening's start rules must be made in both); new holdout lines come
  from the owner's real lines, written by someone who did not write the fix.
- [#521](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/521) RvR work 3, player/bot rule parity: autonomous
  gamebots take the player's PvP rules in RvR (mez/CC immunity, interrupts, crits, facing, speed, sprint, stealth speed,
  rez sickness, cast line of sight, spell level, RP window, regeneration; `PvpRules`, `UT_PvpRuleParity`). **In review,
  tests only.** Companions unchanged. Open: the fight harness rows (#520) before/after, the PC checks P-mech 1-7
  (mechanics.md) on a named build, and the realm-ability gaps (finding 6) and companion RvR jobs (finding 10), which
  are not part of this change.
- [#520](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/520) An end-to-end RvR fight harness in CI.
  **Built, tests only** (`UT_RvrFightRehearsal`): three report rows in the CI log, none a gate until a PC log agrees
  (evidence.md 5.3). Open: the two fight scenarios do not yet repeat exactly by seed (`Random.Shared` in the server's
  fight code); the world database's spells, specs and keeps are stood in.
- [#475](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/475) Realism 1: record real play (the say gate as a
  pass-through in all 17 exits, `logs/session-chat.log`, `/odd`, `tools/bot-voices/session_transcript.py`, the raw-send
  ratchet). **Built, tests only;** done when a played session's `session-chat.log` gives a transcript an agent can read
  line by line and a count of defects per hour, per channel, per build (baseline: 9 in about an hour on 1.2.2). Next:
  #489's step 2 (the checks in the gate).

- **The 2026-11 upstream harvest** ([upstream/HARVEST-2026-11.md](upstream/HARVEST-2026-11.md)): one ranked list from
  the OpenDAoC-Core, Offline DAoC family and DOL sweeps. In progress: port packages P1-P5 (`fix/1.2.3-upstream-core`,
  `-effects`, `-combat`, `-ai-world`, `fix/1.2.3-fork-bot-fixes`) and the ledger issues #562-#565. To do, **nothing
  started**: #567-#574 (bot behaviour from True 1.65, DividedRealms, DetShaft and FelixGagnonB), #575-#580 (world data
  audits and checks), #581 (geometry line of sight) and #582 (single-zone navmesh tooling); #581 and #582 need the PC.
- [#522](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/522) RvR work 4, stealth: one stealth-rules module
  for the player and the gamebots (`StealthRules`: speed, too close, uncover, see), gamebots out of the base uncover timer
  (`RVR_STEALTH_UNCOVER` logged), the stealth notice at objectives, the planning filter, the ambush scope, travel hidden
  with duo followers (`BotStealthTravel`), the archer opener (`BotStealthArcher`), the "edge gone" bail
  (`BotStealthEdge`) and See Hidden at 1.62 strength. **In review, tests only** (`UT_StealthRules`). Open: stealth.md PC
  checks 1-8 and defense.md PC check 6 on a named build; openers per chokepoint per hour from the RvR hour report before
  and after; the owner's call on the See Hidden cut's shape and on whether every guard should use the rubric's short range
  (it would weaken `GuardStealther`, so it is not done); Rapid Fire, Camouflage, Vanish and True Sight for bots (#531);
  the duo's edge-gone retreat (only soloers run); the fight harness rows (#520).

- **OpenDAoC upstream, server core (P1):** database save safety, the memory-leak bundle (#540), game-loop drift, the
  trade deadlock and small core fixes, on `fix/1.2.3-upstream-core`. **In review, tests only.** Left: the quest-list
  locking refactor (`22197f1f6`, about 100 call sites incl. the quest harness and journal), the equipment broadcast
  rework (`ec54287b1`) and the UDP receive hardening (`a981b22f7`, does not apply to our diverged network code) were not
  ported. PC check: a long session's log shows the periodic saves and no growth in held deleted objects.

## 1.3

- [#437](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/437) Language selector, German and French (original client only). **Open, not started.** Moved from 1.2.2
  to 1.3.

## Lore

Read [docs/1.2.1/HEROES.md](1.2.1/HEROES.md) and [docs/1.2.1/HANDOFF.md](1.2.1/HANDOFF.md) first. Every batch gets an independent review, and the owner sees samples before it merges.

- ~~[#399](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/399) Lore: no real companies and nothing that breaks the fourth wall in player-visible text (owner decision 8)~~ **Closed.** About 860 lines reworded; `tools/lore/fourth_wall.py` is strict in CI.
- ~~[#400](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/400) Lore: the companion-name test should check every legend name the lore tells of (owner decision 7)~~ **Closed.**
- ~~[#401](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/401) Lore: rewrite the Avalon City summary in fresh words~~ **Closed.** Written in 1.2.1 (`6ba1ab30`, reviewed in [REVIEW-FOURTH-WALL](1.2.1/REVIEW-FOURTH-WALL.md)).
- ~~[#402](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/402) Lore: bring the epic bosses up to the epic floors~~ **Closed.** All 157 epic bosses at the floors; three realm reviews.
- ~~[#403](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/403) Lore: bring Midgard's and Hibernia's major bosses up to the depth floors~~ **Closed** (review: [REVIEW-DEPTH](1.2.1/REVIEW-DEPTH.md)).
- ~~[#404](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/404) Lore: bring the places up to the depth floors (mostly facts, book pages and loremaster topics)~~ **Closed** (review: [REVIEW-DEPTH](1.2.1/REVIEW-DEPTH.md)).
- ~~[#405](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/405) Lore: give Albion and Hibernia an epic voice of their own~~ **Done in 1.2.1** (closed 2026-11-22): 10 Albion and 11 Hibernian chapters in their realms' old forms, reviewed in `docs/1.2.1/REVIEW-EPIC-VOICE.md`.
- ~~[#406](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/406) Lore: vary the "The X say" teller line that opens most new passages~~ **Done in 1.2.1** (closed 2026-11-22); [REPETITION.md](1.2.1/REPETITION.md) and its CI ratchet keep it varied.

## Epic raids

Never weaken a boss. A raid plan counts as working only once it is won on the owner's PC; most of these need the owner's raid log first.

**Tests only.** The simulation-based fixes shipped in 1.2.1 (#431, #428, #430, #432), and six raid issues were closed
on 2026-11-22 as done in 1.2.1; none of those raids has been won on the owner's PC yet, and an issue is reopened if
its raid's log shows it lost again. #410, #412 and #414 stay open; 1.2.2's size raises for #410 and #412 (#441) were withdrawn.

- ~~[#407](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/407) Caer Sidi: the bot raid loses at the Crypt Lord~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- ~~[#408](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/408) Galladoria: the bot raid loses at Spindler Broodmother and never reaches Olcasgean~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- ~~[#409](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/409) Tuscaran Glacier: the bot raid loses at Queen Kula, Suttung and the Council, and King Tuscar~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- [#410](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/410) Summoner's Hall: Summoner Roesia wins every time, and Grand Summoner Govannon has never been beaten. **Open;** bigger raid in 1.2.2 (#441).
- ~~[#411](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/411) Trollheim: the bot raid times out before Nosdoden falls~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- [#412](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/412) The Green Knight and Marfoirwort: bot raids lose. **Open;** bigger Green Knight raid in 1.2.2 (#441).
- ~~[#413](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/413) Legion: play his platform, the way down and his add waves~~ **Done in 1.2.1, tests only** (closed 2026-11-22)
- [#414](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/414) Behemoth, Fomor City and Morgana: no bot raid yet. **Open.** 1.2.3: Fomor City has its raid (`dungeon-hibernia-fomor`, a zone180.nav walk-link patch and the period's named order; **tests only**, for the next sweep). Behemoth: no raid, by design (Mythic, 2008: "not killable"). Morgana: no raid, a quest host in the period too; a raid would need her changed (owner's call).
- ~~[#415](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/415) Dragon fights fall apart partway (Golestandt, Cuuldurach)~~ **Done in 1.2.1, tests only** (closed 2026-11-22)

## RvR and travel

Fix routes spot by spot; never rebuild navmeshes globally. #421 needs the owner's log lines.

- [#525](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/525) RvR work 7: siegecraft stand-ins. **Built in 1.2.3, tests only** (branch `fix/1.2.3-siegecraft`): synthetic engines built in sight at half the period time (ram 30 s, ballista 20 s, catapult 45 s, trebuchet 60 s; one multiplier), interrupted by an attack or death, replaced only by a new build, and a finite per-siege stock; the true status text; bot crew for the player's ram and no ram place taken by it; `RVR_SIEGE_OUTPUT`; defending operators that hold their engines; defenders that repair doors with wood by the player's rules (self-repair stays). Owed on the PC: a defender engine's `RVR_SIEGE action=hit defending=true`, a player ram's `RVR_SIEGE_OUTPUT operator=player`, `build_started`/`deployed built=` timings, `RVR_DOOR_REPAIR action=repaired` and the gate's health an hour after a defended siege. Owner checks: the chosen period build times (no source found), the untrained repair skill of 100, the stock size, wood sellers in the frontier (none in a region: no bot repair there), and boiling oil (rubric 1.2 against 5.2).

- [#519](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/519) Honest measuring (RvR audit 3): the log checker's notice, regroup, siege-outcome and party rows, the misleading test names, the Seen stamps. **Done in 1.2.3, tools and tests only.** Owed on the PC: re-read the `19e7037`, `5378dbe` and `9a173f4` logs with the notice row; run the real-mesh probes once per release (`docs/RELEASE-TRAIN.md`). Owner decisions: the regroup share bars (now a tally), and a "shipped, owed in play" state for #416, #417 and #418. First read on `868a82d` (2026-11-24): scans about 4.6 s apart instead of 1.0-1.25 s, so the time to notice from first sight is about 2.3 s at every range (the table: 0.5 s at 1,000-1,500, 11.8 s past 3,400); the notice row reads Contradicted.
- [#530](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/530) RvR work 12 (piece C): one force intent owned by the event layer. **Steps 1-4 built in 1.2.3, tests only** (branch `fix/1.2.3-force-intent`): the dead director and `ObjectiveState.Next` deleted, the caller check in CI, one `ForceStrength` siege rule, every intent write proposed to the event layer and written once per turn (`RVR_DECIDE what=force`), followers read `IntentOf`, the shadow (`what=shadow`). Owed on the PC: `what=destination` and `what=force` show one writer per change, and the `shadow.mismatch` count falls to 0. Open: step 5 (the battleground keep as an objective with its wave as muster, the small strike with #523, the slow realm force), and then retiring the TempProperty mirror.
- [#505](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/505) RvR: record real frontier play (audit phase 0). **Built in 1.2.3, tests only** (branch `feature/1.2.3-rvr-record`): `RVR_DECIDE`, fight ids, the player's rows, siege stalls, teleports, `RVR_HOUR`, `/rvrodd`, `rvr_hour.py`. Open until a played frontier hour yields the report; still to add: `RVR_LORD_BLOCKED`, `RVR_EVENT_OPENED reason= depth=`, the stealth rows (#522), `RVR_COMPANION_ENGAGE`, `RVR_LEGS_LOST`, ram output per engine, `RVR_RANKS`, the CI ratchet on rule codes, and folding `/rvrodd` into #475's `/odd`.
- [#505](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/505) RvR: record real frontier play (audit phase 0). **Built in 1.2.3, tests only** (branch `feature/1.2.3-rvr-record`): `RVR_DECIDE`, fight ids, the player's rows, siege stalls, teleports, `RVR_HOUR`, `/rvrodd`, `rvr_hour.py`. Open until a played frontier hour yields the report; still to add: `RVR_LORD_BLOCKED`, `RVR_EVENT_OPENED reason= depth=`, `RVR_COMPANION_ENGAGE`, `RVR_LEGS_LOST`, ram output per engine, `RVR_RANKS`, the CI ratchet on rule codes, and folding `/rvrodd` into #475's `/odd`. The stealth rows (`RVR_STEALTH_UNCOVER`, `_ARCHER_OPEN`, `_TRAVEL`) come with #522.
- ~~[#416](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/416) Battleground keeps rarely change hands~~ **Done in 1.2.1, tests only** (closed 2026-11-22): stormers strike the lord only from within 350 (battlegrounds and frontier; `BG_RALLY_STATUS inLordReach=`, `RVR_LORD_STEP_IN`).
- ~~[#417](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/417) Roaming eight-man RvR groups rarely find a fight~~ **Done in 1.2.1, tests only** (closed 2026-11-22): patrols weigh enemy RvR bots, eights chase the planner's hunts (`RVR_ROAM_CHASE`).
- ~~[#418](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/418) Gamebots wait a long time at frontier teleporters~~ **Done in 1.2.1, tests only** (closed 2026-11-22): bots pick a porter that sells their medallion; waits past 5 minutes log `RVR_FRONTIER_WAIT_LONG`.
- ~~[#419](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/419) Open-field regroups pass between groups more than planned~~ **Closed.** 17.4% on `9a173f4` is under the owner's 20% bar; the log checker now shows each call's spread.
- ~~[#420](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/420) Connacht: repeated route rescues uphill to a hunting camp (200:314846,471535)~~ **Done in 1.2.1, tests only** (closed 2026-11-22): after two identical rescues for one camp within 30 minutes the bot picks another camp (`AUTONOMOUS_ROUTE_POCKET_ESCAPE_LOOP`, PR #434).
- [#421](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/421) Lough Derg / Bri Leith seam loops at region 200. **Fixed in 1.2.2, tests only** (the zone itinerary's main-ground rule; `tools/navmesh/seam_route_sim.py`); open until a PC log shows no `AUTONOMOUS_ZONE_SEAM_CYCLE` there.
- [#538](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/538) Party members stall at zone borders, so resurrections time out with the healer out of range. **Fixed in 1.2.3, tests only** (`AutonomousGroupFormationFollow.NeedsSeamRoute`: the AI turn takes the itinerary's seam crossing to a leader over the border; a stalled member is moved only out of a player's sight). Open until a PC log shows few cross-zone `AUTONOMOUS_GROUP_MEMBER_STALLED` (11/24: 1,784 in 5 h) and `seamRoutes=` in `AUTONOMOUS_GROUP_FORMATION`. Not done: retrying a resurrection blocked for power within the 60 s window (66 of 2,780 timeouts).
- [#539](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/539) Parties stand at one target for up to 107 minutes on an unreachable ranged pull. **Fixed in 1.2.3, tests only** (`AutonomousDefensivePull.UnreachableBench`: two tries then an ordinary pull; three benched targets give the camp up). Open until a PC log shows no same-shooter, same-target `AUTONOMOUS_DEFENSIVE_PULL_UNREACHABLE` run past a few minutes.
- [#526](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/526) World data and map one-offs. **Item 1 fixed
  in 1.2.3, tests only:** Dun nGed, Da Behnn, Ailinne and Crauchon at level 5, with a one-time repair for older worlds
  (`FrontierKeepLevelRepair`); PC check: `/keep info` at nGed on a fresh build and on an old world
  (`KEEP_LEVEL_DEFAULT_RAISED`, doors at 50,000). **Owner decisions open:** Darkness Falls on a fresh or tied world, and
  the 15-minute grace (item 2; the holder isn't persisted until then). **PC (#503):** home-realm circuits (P13), Odin's
  Gate Hibernian exit (P4), lifts (P5), Midgard's walk to Caer Benowyc (P3). Item 7: `GetRealmKeepBonusLevel` is called
  by `UpdateBaseLevels` when `use_keep_balancing` is on (off by default), so it is kept. Item 8 is the battleground-fill
  issue's.

## Other bot behaviour

- Fork package P5 (1.2.3, branch `fix/1.2.3-fork-bot-fixes`): fixes from the Offline DAoC forks (FelixGagnonB, True165,
  DividedRealms, DetShaft), listed in CHANGELOG-LIVING-SERVER.md 1.2.3 "From the Offline DAoC forks". **Tests only.** Owed
  on the PC: a two-Bard group's endurance with the song up (`EFFECT_STALE_COPY_RETIRED`); a grouped Bard keeping its drum
  and song in a fight; a group through a keep door without freezing; no camp shared by two parties (`AutonomousMobClaims`);
  `AUTONOMOUS_DEFENSIVE_PULL_IGNORED` runs of two at most. Not taken, held for the owner: the jump-interrupts-casting rule
  (True165 c41415df, a new casting restriction on real players); realm-0 armor filtered from drops (DividedRealms 32f08331,
  fewer drops); releasing a rider's pets when it boards a stable horse (True165 ef0dcd8b, a charmed pet would turn on
  its owner). Left for later: True165's role sheet (only the main tank sets the kill target, the off-tank peels; d2fc3fe3d
  needs its BotBrain.MainAssist), its Mag Mell release spots (needs its BesideBindstone) and the Trollheim pillar follow
  fix (b390827f, a spot fix in core movement).
- [#544](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/544) Hibernian bots level about twice as fast as Albion's. **Open.** Albion has the fewest camps per band from 5 to 19 (world data, owner decision); the 10-19 group XP gap is not explained yet. 1.2.3 adds the sampled `AUTONOMOUS_KILL_XP` row and `source/tools/kill_xp_report.py` (tests only); next: one played hour at 10-19 to tell shared kills from off-target kills.
- [#491](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/491) Buffers waste buffs on targets that can't use them; split base and spec buffs between healers and spec buffers. **Fixed in 1.2.3, tests only** (`BotBuffTargetPolicy`, `UT_BotBuffTargets491`); open until a PC log shows a group buffed without power regen or acuity on melee.
- [#535](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/535) Solo bots below 50 plan camps that are grey on arrival. **Fixed in 1.2.3, tests only** (`AutonomousSoloCampFloor`, `UT_CampChoiceAndDeaths123`); open until a played log shows `AUTONOMOUS_CAMP_OUTLEVELED` well under the 1.2.2 rate (about 1,040/h with about 2,930 bots).
- [#536](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/536) Group PvE camps purple for low-level parties. **Fixed in 1.2.3, tests only** (`AutonomousGroupCampBand`); open until a played log shows the purple share of grouped deaths (75% on 1.2.2) and the "No reachable non-grey group camp" disbands (24%) fall. Parties spread more than a con band wide can still find no camp.
- [#537](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/537) Solo bots die to purple monsters en route; the killer is never benched. **Fixed in 1.2.3, tests only** (`AutonomousPveBossDanger` by con, `AutonomousDeathBlame`); open until a played log shows fewer purple deaths with `arrivedAtCamp=False` and fewer at the howling maiden cell (Black Mtns. North, 514k,380k). That spot was already marked on 1.2.2 (ten levels above) and still killed 120 bots, mostly while they searched for a camp or met up with a group near it: the avoidance (route bend, rendezvous and release spots) may need more work.
- [#473](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/473) /classes showed party roles that /spawn won't accept as builds. **Fixed in 1.2.3, tests only** (`ClassesCommandHandler.Lines`, `UT_ClassesCommandBuilds473`).
- ~~[#396](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/396) No xp for kills in Thid~~ **Done in 1.2.1, tests only** (closed 2026-11-22): enemy gamebot kills pay what a player kill pays.
- ~~[#397](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/397) Training hires doesn't work once the skill tree grows outside the training window~~ **Done in 1.2.1, tests only** (closed 2026-11-22): `/hire train` by line name; the trainer window shows the next skill first.
- ~~[#422](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/422) Crafting: a trade with a crafter bot has never been finished, and few crafted pieces sell~~ **Done in 1.2.1, tests only** (closed 2026-11-22): the hand-over runs while the break lasts (`ECON_PLAYER_ORDER outcome="ready_told"`, `"handover_tried"`); `ECON_INDEX craftedListed20`.
- ~~[#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423) Bot parties never take a new member once they set off~~ **Done in 1.2.1, tests only** (closed 2026-11-22; `BOT_LED_RECRUIT ... kind=open_seat`).
- ~~[#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424) Click-targeting a bot in the original client (unconfirmed)~~ **Done in 1.2.1, tests only** (closed 2026-11-22): each click is logged (`PLAYER_CLICK_TARGET`) and the `bot-click-target` playtester scenario covers it; one played check is still on the list below.
- ~~[#438](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/438) Darkness Falls (Albion side): following bots stuck on a step of the entrance stairs; GM `/jump <name> to me` didn't work for bots~~ **Done in 1.2.1, tests only** (closed 2026-11-22). 1.2.2 adds a dead bot's refusal and a gamebot's fresh trip after a jump.

## Checks on a PC

- [#487](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/487) Does a resting or waiting bot show as
  sitting on the original client (emote 0xF0, `BOT_MOVE_SIT` in the log)? **Open.** If not, change or drop it in
  `BotIdleLife` (`ShowSeat`, `SitAnimation`, `StandAnimation`); the seat's logic stays.
- [#540](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/540) Blocking gen-2 GC pauses of 1.1-2.4 s (49 in 5 h on 1.2.2 `868a82d`) and handles growing about 700 an hour. **Fixed in part in 1.2.3, tests only:** the gear checks' allocations (the log's largest allocator, 27-35% of all bytes) are gone, the stat tables no longer hold a `Lock` (the 2026-10-22 census's main owner of Windows events), and `SERVER_HANDLE_CENSUS` (handles by type, every 10 min) and the memory-load fields of `SERVER_MEMORY` / `SERVER_GC_PAUSE` run in every session. Open until a 6 h+ PC log on 1.2.3 shows: `topAllocations` without the tuple arrays, the pause count and length, which handle type grows, and whether `memoryLoadPct` is at `highLoadPct` when a blocking collection runs (then the fix is memory on the PC or the launcher's "Lean server memory", not a GC setting).
- [#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425) 1.2.1 PC check: the original client's handshake build, and the bot death fall. **Open.** CI's playtester scenarios `bot-death`, `bot-corpse-target` and `bot-rez` pass (on `b644529a`), but they check packets, not what the client draws.

## The 1.2.1 check on the owner's PC

`fix/1.2.1` passes CI (`d56adeda`: 9,258 server tests passed, 0 failed), but nothing in it has been seen in game. Build
it (or install the 1.2.1 release), then check (GM account where noted). Each row moves from tests only to seen in game
once a log shows it:

1. **Death fall** ([#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425)): `CLIENT_HANDSHAKE ... client=original` in the log; `/lstest deathanim`, `raise`, `release` on a targeted bot (`BOT_DEATH_ANIM fall`, `BOT_DEATH_ANIM raised`). With `bot_death_animation` off, nothing about dead bots differs from 1.2.
2. **Rez:** kill a bot and cast a resurrection spell with nothing selected (or `/lstest deathanim rez`); it goes to the bot and the bot stands up.
3. **Companions:** a 1.2 companion catches up once (`HIRED_COMPANION_RAISED level=5->NN`, `training_weapon_replaced=`); log in again and there is no second raise; a new hire at level 20+ arrives trained and in merchant gear (`HIRED_COMPANION_TRAINED`).
4. **`/hire train`** by line name: `/hire train 1 smite 10`.
5. **Thidranki:** killing enemy gamebots gives experience.
6. **Pets:** an Animist places more than 12 turrets; companion and gamebot Animists keep the cap.
7. **/faction:** no minute under about 5 lines on a full realm, none over 7.
8. **Bot schedule:** a world that ran 1.2 logs `bot_schedule_mode set to the new default off`; the whole roster is online.
9. **Loot:** in a gamebot-led group your kills drop the monster's named loot; the start-up log has one `LOOT_ITEMS_MISSING` line per source.
10. **Uthgard drops:** moved to [1.2.3](#123).
11. **RvR:** `RVR_LORD_STEP_IN` at sieges and battleground storms, `RVR_ROAM_CHASE` for roaming eights, no `RVR_FRONTIER_WAIT_LONG` storm; `RVR_DEFENSE_MARCH` never shows `assigned=0`; `RVR_KEEP_ROUTE_RESTART` for Albion in region 1 shows `to=` near Castle Sauvage; a GM `/jump` into an enemy keep stays there.
12. **Linux (Ryan):** the launcher's tooltips and alt-tab under Wine (#393) and `linux/start-offline-daoc.sh` (#395), both merged to main.
13. **Click-targeting a bot** ([#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424)), original client, your own mouse (not a tool): left-click a living gamebot standing still near you, then a moving one, then a companion. Each click should select it and log `PLAYER_CLICK_TARGET ... kind=gamebot ... result=set alive=True`. No line at all: the client never sent the click (its picking); `result=no_object` or `out_of_range`: the server dropped it.
14. **A party picks up one more** ([#423](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/423)): `BOT_LED_RECRUIT outcome=searching kind=open_seat` for parties that set off, then `outcome=recruited kind=open_seat` now and then; in /lfg a travelling party's "lf1m ..." and "ya, 1 spot" to "room?". Join one by /lfg or a tell while it travels.
15. **Raids** (#407-#415): any raid won, with its log (each issue's raid); every raid at its period size (1.2.2's raises withdrawn).

## Evidence behind the player known-issues list

[KNOWN-ISSUES.md](KNOWN-ISSUES.md) ships to players and is written in their voice (owner, 2026-11-22: player-facing
text says what changed and what to do, with no testing-status framing). The status it used to carry, build by build,
is kept here (moved on 2026-11-22). A feature counts as working only once it is seen in a log from the owner's PC;
until then it is **tests only**. The last builds checked on the owner's PC are 1.2's `c81eb5d`, `43c43aa` and
`aa537ce`; nothing new in 1.2.1 or 1.2.2 has been seen there yet.

### 1.2's fix list, and where 1.2.1 leaves it

Seen on the owner's PC or in its log on 1.2 builds, and left for 1.2.1 (owner, 2026-11-19: publish unless there are
major bugs, crashes or instability, and add the rest to the 1.2.1 list). Each 1.2.1 change below is **tests only**.

- **/faction pace:** about 5 lines a minute on average, but quiet minutes drop to 2 (`43c43aa`: 4.9 a minute, 13 of 39
  minutes under 5). 1.2.1 fills quiet minutes back to about 5, under the same cap of 7: **tests only**.
- **Route loops at region 200 seams:** the Cruachan Gorge / Breifine seam that sent 75 Hibernian bots home on
  `43c43aa` is fixed in 1.2 (**tests only**; seen walked single file on `aa537ce`). The other seam loops of that log
  (Lough Derg / Bri Leith, #421) is not changed and can still loop. The Connacht hunting-camp rescues (#420): from 1.2.1
  a bot rescued twice for the same camp within 30 minutes picks another camp (`AUTONOMOUS_ROUTE_POCKET_ESCAPE_LOOP`):
  **tests only** (done in 1.2.1, issue closed 2026-11-22).
- **A bot removed from the world by a casting crash** (`43c43aa`, one bot). Fixed in 1.2 with a null guard: **tests
  only**. On the list in case it comes back.
- **A gap in the casting line-of-sight check** (`CastingComponent`, `LosChecker.Out` can be null) behind that crash.
  1.2.1 treats a client it can't ask as no check: **tests only**.
- **Caer Caledon's guards can't be picked with /target** by players (checked as a GM only). 1.2.1's `/target` finds a
  nearby NPC for players too: **tests only**.
- **Click-targeting a bot** didn't work for the test helper on `c81eb5d` (`/target` works). Unconfirmed: it may be the
  test tool, not the game. The server has no rule that refuses a click on a living bot: no bot is flagged
  untargetable or peaceful, only stealth hides one, and the one unusual bit, "has owner" (a bot's brain is a controlled
  brain, as a pet's is), is the one pets carry, and pets can be clicked; 1.2.1 logs each click on a bot (`PLAYER_CLICK_TARGET`), so one played click settles it (#424, done in 1.2.1, issue closed 2026-11-22).
- **Old merchant item templates missing at startup:** about 760 error lines at every start from the stock world data. No
  effect seen in play. 1.2.1 makes them one warning with a count (and missing loot items one `LOOT_ITEMS_MISSING` line
  per source).
- **A "DrainTo in progress" error from QueuePacket**, seen once (`43c43aa`), with no effect seen. In 1.2.1 the packet
  waits instead: **tests only**.
- **Also from `aa537ce`, changed in 1.2.1 (tests only):** a Midgard keep defence that marched with 0 bots now stands
  down; 11 Midgard bots that logged in beside an enemy keep now log in at their capital; a GM's `/jump` into an enemy
  keep stays there; Albion bots with no route out of Castle Sauvage restart from the border keep's muster; the
  launcher's chat caption reads "HOW MUCH BOTS TALK".
- **Epic raids:** still being tuned (below).

### Epic raids: still being tuned

Bot raids fight the bosses by their own rules now, and the bots lose some of them. These were lost on the owner's PC,
bots only, at the game's own raid size:

- **Caer Sidi:** lost at the Crypt Lord, whose court and patrol beat the raid (`b1b6846`, `9a173f4`).
- **Galladoria:** lost at Spindler Broodmother (`9a173f4`). On `b1b6846` the raid ran six hours and never reached
  Olcasgean; the fixes for the Corrupt Primals since then are **tests only**.
- **Tuscaran Glacier:** lost at Queen Kula (`b1b6846`, every Kula mechanic handled), at Elder Icelord Suttung and the
  Council (`9a173f4`), and at King Tuscar (`ccba364`).
- **Summoner's Hall, all three realms:** lost at Summoner Roesia (`b1b6846`, `9a173f4`). **Grand Summoner Govannon has
  never been beaten by bots.**
- **Trollheim:** timed out before Nosdoden fell (`ccba364`, `b1b6846`).
- **Darkness Falls (Albion):** the raid looped on a chthonic knight behind it (`b1b6846`). Fixed: **tests only**.
- **Golestandt:** lost after two wipes (`9a173f4`, `c81eb5d`); won on test build 5 and on `43c43aa`. **Cuuldurach:** lost
  after two wipes (`c81eb5d`, `43c43aa`); won on test build 5. Dragon fights can fall apart partway.
- **The Green Knight** and **Marfoirwort:** lost (`ccba364`).
- **Legion:** some of his rules are in (his adds and heals are capped, wounded raiders step out of his reach), but the
  raid does not play his platform, the way down or his add waves. **Behemoth** is never fought (by design: Mythic,
  2008, "This mob is not killable"); **Morgana** has no raid (a quest host); **Fomor City** has a raid since 1.2.3
  (**tests only**).
- **Xanxicar's stomp:** a realm learns him over its attempts. Still being tuned.

**1.2.1:** the raid fixes in 1.2.1 come from these logs and from simulations, and are **tests only** until a raid is seen
won on the owner's PC. #407, #408, #409, #411, #413 and #415 were closed on 2026-11-22 as done in 1.2.1 (reopened if a
raid log shows them lost again); #410 (Summoner's Hall), #412 (the Green Knight, Marfoirwort) and #414 (Behemoth, Fomor
City, Morgana) stay open. **1.2.2** raised the raid size at Summoner's Hall, the Green Knight, Galladoria and Tuscaran
Glacier (#441); all four raises were withdrawn (2026-11-22 and, on audit, 2026-11-23). Elro the Ancient's third treant, which never came,
now comes between three-tenths and a quarter of his strength left (**tests only**).

Fixes made after the last raid builds the owner ran are **tests only**:
- **Summoner Roesia:** the split camps for her Summoner Pain made the raid worse and are removed. Raid healers now cure
  Summoner Pain before a top-up heal when it is on two or more raiders and nobody in their party is in danger. On
  `9a173f4` they never did (a heal always came first). **Tests only.** Before the camps the raid also lost to her.
- **Raid cures in general:** healers were seen curing poison and mesmerize on `9a173f4` and `c81eb5d`; on `9a173f4` up to
  a dozen cured the same raider in the same second. Each curer now claims its target: **tests only**.
- **Curengkur:** the raid never formed (no route to its staging post). The next staging posts are now tried. **Tests
  only.**
- **Hibernia's dragon:** "No muster post could be placed at the hub" at Innis Carthaig on `9a173f4`; it started first try
  on `5154cae`. The check that names a refused post is **tests only**.
- **Tuscaran Glacier:** Suttung pulled with nobody at his muster after stranded raiders were moved beside him, and
  Steinvor's last Pillar of Ice outliving him. Both fixed: **tests only**.
- **A stall as two raids end together** (3.0 s on `9a173f4`). Fixed: **tests only**.

If a fight is too hard on your world, each restored boss rule is a server setting of its own and can be switched off
alone. **Don't turn on `epic_earthmender_mutual_heal`:** with it on, the four Earthmenders heal each other and the bots
can't win (on `b1b6846` they held at 99-100% through 48 minutes).

### RvR and battlegrounds

- **Battleground keeps rarely change hands (#416).** Bots storm the keep lord again and again and mostly fail. The first
  capture was Caer Caledon, taken by Albion on `43c43aa` after its keep levels were repaired (guards 39, lord 46). The
  cause found: stormers struck the lord from up to 900 away, but he takes no damage from beyond 400. In 1.2.1 they
  walk to within 350 first, on the frontier too (`RVR_LORD_STEP_IN`): **tests only** (done in 1.2.1, issue closed 2026-11-22).
- **Open-field regroups pass between groups more often in dense warbands**: 17.4% of regroups on `9a173f4` were
  started by another group's call. None was passed on a second time and none echoed back. The owner raised the bar to
  20% on 2026-11-19, so this passes; the log checker now also prints how many groups each call reached, to tell
  denser warbands from a call that over-fires (#419, closed).
- **Roaming eight-man groups** rarely find a fight and move on (#417). In 1.2.1 their patrols favour where enemy RvR
  bots are, and they chase the planner's hunts (`RVR_ROAM_CHASE`): **tests only** (done in 1.2.1, issue closed 2026-11-22).
- **Some gamebots wait a long time at frontier teleporters** before the server moves them on (#418). In 1.2.1 they go to
  a porter that sells their medallion, and a wait past 5 minutes is logged (`RVR_FRONTIER_WAIT_LONG`): **tests only** (done in 1.2.1, issue closed 2026-11-22).
- **Enemy gamebot kills gave no experience** (#396, Thidranki). In 1.2.1 they pay what a player kill pays: **tests
  only** (done in 1.2.1, issue closed 2026-11-22).

### Chat

- **/faction runs at about 5 to 7 lines a minute on a full realm, by design** (owner, 2026-11-18), never more than 7.
  4.9 a minute on average was seen on `43c43aa` (quiet minutes lower: see the fix list). Lower **How much bots talk** in
  the Living Server tab if it's too much.
- **Seen since the owner's chat check on `5154cae`:** "is it far?" gets a travel answer (`43c43aa`); a call for help gets
  offers or honest refusals from several bots (`43c43aa`); another realm's zone gets "thats hib frontier rvr only"
  (`c81eb5d`); a /broadcast question is answered in /broadcast (`43c43aa`). **Tests only:** a bot-to-bot answer no longer
  landing before yours.
- **"omw" means coming** (tests only to the end): a bot that says it has to go, with a deadline. A reply that is never
  sent leaves a short walk behind it (up to 210 seconds).
- **Replies still guess from your words.** A bot doesn't know what you are doing beyond what you type. A bot that
  remembers you may ask your level right after you said it. Chat has been tested one line at a time more than as long
  conversations.
- **Bot parties take new members on the way only now and then** (1.2.1, #423, tests only; issue closed 2026-11-22): a party that set off with a seat free
  looks for one more for five minutes, two minutes after it leaves and then every ten. Between those looks its "lfm" has stopped, and
  it takes one member at a time.

### Bots that play with you, and companions

- **Bot-led groups:** gamebots don't share your horse or boat; the leader takes its own and waits at the other end. The
  break at the end of a session ends the party.
- **A bot that offers to group may walk on before you meet**; if it's too far by the time you invite, it says where it is.
- **Hired companions:** joining your fight on a grey monster failed on `b1b6846` and is fixed: **tests only**. In 1.2.1
  they join at your level, trained and in merchant gear, and companions hired on 1.2 catch up once
  (`HIRED_COMPANION_RAISED`); `/hire train` takes line names (#397): **tests only** (done in 1.2.1, issue closed 2026-11-22). The healer's
  [companions] and [hire] links work only with the healer targeted; typed `/hire` words always work. The gear window's
  title reads "House Vault 0" (it reuses the house vault window).
- **Bots don't fall when they die** in the original client (1.2). From 1.2.1 they fall and lie like a monster's corpse,
  and a resurrection cast with nothing selected reaches a dead bot nearby. CI's playtester checks the packets
  (`b644529a`), not what the client draws: **tests only** (#425). Switch it off with **Bots fall when they die**. Dead
  bots still can't be clicked in the original client; `/target <name>` or a rez with nothing selected works.
- **Darkness Falls stairs (#438):** companions stuck on a step of the entrance stairs is fixed in 1.2.1: **tests only**
  (issue closed 2026-11-22).
  The stairs are one-way in the navigation mesh: a companion following you back **up** them has no path and waits at
  the bottom. Use the exit portal below, or GM `/jump <name> to me`. From 1.2.2 `/jump` says so when the bot is dead
  (its body stays where it fell: resurrect it there), and a jumped gamebot plans its trip again from where it lands:
  **tests only**.
- **Clicking a living bot** may not select it in the original client (#424, unconfirmed); 1.2.1 logs each click
  (`PLAYER_CLICK_TARGET`), and the issue was closed on 2026-11-22 (the `bot-click-target` playtester scenario passes;
  **tests only**, one played click still to come).

### Bots as people and 2003 builds

Seen in the server log on `c81eb5d` (not watched on screen): skill tiers 32/48/20 across 535 bots, bots growing a tier,
asking the way, movement habits, and the era builds loading. **Tests only:** mistakes and their rate, milestone talk,
raid learning, places seeded on a new world, and the era class mix on a fresh world. No raid has been fought by an
era-built world yet. Whether the original client shows a sitting bot is still to be checked.

### Crafting and the economy

- **Crafting as a player** hasn't been seen yet, and needs the original client.
- **Trading with a crafter bot:** tried on `b1b6846`; a reply was cut mid-word, a different piece was offered, and the
  agreed deal lapsed. Fixed: **tests only**. No trade has been finished yet. Since then (issue #422): a made commission was
  never handed over or announced, because the hand-over ran only at the very edges of the crafter's break, where it saw no
  break; the crafter now hands it over, or says where to collect it, while its break lasts. A stock trade window that could
  not open now says why and keeps the deal. **Tests only** (done in 1.2.1, issue closed 2026-11-22).
- **Craft orders** often lapsed on `19e7037` (156 made, 163 expired). Orders now go only to crafters who stay online and
  are withdrawn from busy ones; on `c81eb5d` 44 were made and 1 expired (server log).
- **Few crafted pieces sell on the Exchange**, mostly because most bot work is practice pieces. `ECON_INDEX` now also
  carries `craftedListed20` (crafted bot listings of level 20 and up, what the Exchange's upgrade buyers can use).
- **The launcher's crafter card and economy page** haven't been checked on the owner's PC.

### Lore

The loremasters' dialogs, the lore books, the Bestiary page and the Herald's chronicle are **tests only**. The `/lore`
window, boss yells, atmosphere lines and first-kill lines were seen on `1dc66b6`. The 1.2.1 lore (every epic boss's
book, the major bosses and places at full depth, fight advice and place lore without game terms) is checked by CI's
lore rules and independent reviews, and has not been read in game: **tests only**. Albion's and Hibernia's own epic
voice (#405) and varied teller lines (#406) are done in 1.2.1 (issues closed 2026-11-22). Lore, chat and menus are in
English; German and French come with the language selector in 1.3 (#437).

### Play with friends (experimental)

- It needs the original client; the new client can't use Join Friend yet.
- Only a join over a home network has been seen (test build 7). A session played together and a character passport trip
  between two PCs are **tests only**.

### Launcher, install and updates

- **Updating a 1.1.4 game through Setup, a new install from Setup, and a 0.37 (class-off) world** haven't been run on the
  owner's PC for 1.2. CI runs them: **tests only**. The same goes for **updating 1.2 to 1.2.1** from the launcher.
- **The launcher's version label** reads "LIVING SERVER 1.2.2" from 1.2.2 on, taken from the build itself; up to 1.2.1
  it read "VERSION 0.37" (the Offline DAoC base, which is still what the game must be): **tests only**.
- **The bot schedule is off by default from 1.2.1:** the whole roster is online. A world that ran 1.2 with the untouched
  real-clock default moves to off once; a schedule you chose is kept.
- **Updating a 0.36, 0.36b or 0.36c game** through Setup is **tests only** for 1.2. On 0.36 with the Social Expansion,
  run `PREPARE-FOR-UPDATE.cmd` from the package first.
- **The online count on the main screen** stuck at "5 online" for half an hour on `5154cae`. Fixed: seen updating from 5
  to 2,875 on `c81eb5d` and `43c43aa`.
- **A maximized launcher ignores START SERVER, STOP SERVER or ENTER REALM** when another window sits over those buttons
  (seen on the owner's PC, 2026-11-05 and 2026-11-18). Use the launcher at its normal size, or move the other window.
- **Releases marked as pre-releases are never offered** by the launcher's update check.
- **Windows SmartScreen** may warn that Setup isn't code-signed: More info, then Run anyway.

### The server

- **A very busy server can stall for a few seconds**, most often as raids end. An 8-hour run without a 2-second stall
  hasn't been completed yet (4 h 42 min on `19e7037` had one stall of 1.05 s).
- **A bot can search for a camp for minutes** (mostly level 50s at Tir na Nog's bind). It clears its search after 3
  minutes; the cause isn't found yet.
- **Memory over very long sessions:** Windows handles grow slowly (about 1,700 an hour with 600 bots a realm; about 700
  an hour on 1.2.2 with 2,930 bots). The live heap grows about 75 MB an hour (1.2.2), and blocking gen-2 collections froze
  the server 1.1-2.4 s every 10-25 minutes (#540: 1.2.3 removes the largest allocator and the stat tables' Locks; tests
  only).

### New client (0.37c)

- No crafting and no Join Friend.
- APPRAISE stays hidden, and you don't see yourself as see-through in Stealth yet.
- No guild window; `/face` keeps facing your target until you type it again; a bow fires from the Ranged Weapon key.
- Added the new client after installing Living Server? Run `INSTALL.cmd` again.

### Always the case

- **A fresh world needs bots.** Use **+ Lv.1**, **+ Lv.20** and **+ Lv.50** in the launcher. A realm needs 8 bots of level
  5 or higher before it forms a bot guild; automatic raids need 80 eligible level-50 bots of the realm online.
- **Bot guilds stay in your world when you turn them off.** Remove them with the game and server closed:
  `python tools\claude-version\set_bot_guilds.py --purge --apply` in your game folder (it backs up your database first).
- **`ROLLBACK.cmd` doesn't remove saved data.** It puts back the files the install replaced; new tables and settings
  stay, and the original server ignores them. Run it once per build you installed.
- **Versions:** Living Server is for Offline DAoC 0.37, 0.37b and 0.37c only. 0.37 stays the game; later
  fixes come in Living Server releases.
- **The server listens only on this PC** unless you host for friends. If your settings let other computers in, the log
  warns at each start (`NETWORK_EXPOSURE`).

## Not planned

- [#209](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/209) Crownfire Lance (Pendragon Sovereign): not part of Living Server.
- Random magic items and coin stay as they are (owner, 2026-11-21); Uthgard's drops are added on top.
