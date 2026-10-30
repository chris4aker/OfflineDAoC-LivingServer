# Live events engine: how 1.2's events and epic fights get built, and how you see them working

Owner: Chris. Written 2026-10-29 as a design review of [LIVE-EVENTS-1.2.md](LIVE-EVENTS-1.2.md) (branch
`plan/1.2-live-events`) against the code on `main` at Beta 1.1 (`fca49d7`). Nothing here is built. Where this
file says **verified**, it was checked in the code or on GitHub today; **inference** is this file's reasoning;
**proposed** is a choice for the owner.

## 1. The thesis: script the stage, not the crowd

"Perfectly scripted epic fights with the bots" can mean two things, and only one of them is reachable.

- **Choreographed bots:** every raider moves on cue, stacks here, runs there, interrupts on time. Not reachable.
  Gamebots run on real navmeshes, die on the road, straggle, and arrive late (Beta 1.0's changelog: raids going in
  at 70% of strength, 124 raiders dying on the way to Caer Sidi, 23 Hibernians stuck 33 minutes on a border). Any
  design that needs 100 bots to hit a mark will fail in play even when its tests pass.
- **A reliable drama curve:** the *encounter* and the *moments* are scripted and deterministic, and the bots supply
  the crowd through the raid and siege machinery that already works. The player remembers "we were wiping at 30%
  and then the Special Forces came through the door and we turned it", not whether raider 47 stood in the right
  place.

The second is what to build. The one lever a live MMO never had is here: the crowd is obedient and the server owns
pacing. So the engine's job is **beats**: things that happen at known triggers (boss health, a timer, a count of
raiders present, the player's own action), each with a visible sign (a yell, a /faction call, a herald line, a
spawn, a door), and a **director** that decides when a beat may fire so the curve has a start, a near-loss and a
payoff. Everything else stays emergent.

This is the same pattern Leeroy already uses (verified, `AutonomousRaidLeeroy.cs`): pure trigger rules
(`ShouldCharge`, `JumpsThePull`), one action (`brain.OrderPull(boss)`), chat on timers (yell, three /say reactions,
one /faction call, an after-raid line), GM forcing (`/lstest leeroy`, `leeroy now`), and log lines. Leeroy is the
first beat of the engine. Generalize it; don't build something new beside it.

## 2. What already exists to build on (verified on `main`)

| Need | What's there | Where |
|---|---|---|
| A raid that forms, walks, musters, engages, wipes, regroups and ends with an outcome | Realm raids: 17 targets (dragons, epic dungeons, Summoner's Hall, Darkness Falls, nine raid dungeons), hub muster, formation slots, staging posts, final engage, wipe rule (half the fighters fall, or the boss heals back to 95%), regroup, "Defeated" after two failed attempts, loot ledger, contested-kill credit | `AutonomousRealmRaid.cs`, `RealmRaidMuster.cs`, `RealmRaidFormation.cs`, `RealmRaidStaging.cs`, `RealmRaidRecruitmentPolicy.cs`, `RealmRaidLootOwner.cs` |
| Reinforcements as a dramatic beat | Epic Special Forces: up to 24 geared level 50s arrive at the hub after a wipe, sized by the dungeon's win record; in memory only, never saved, take no loot | `AutonomousEpicSpecialForces.cs` (`Request`, `Pulse`, `CallUpParties`) |
| A scripted moment inside a raid | Leeroy: trigger rules, the charge, timed reactions, after-raid line, GM force | `AutonomousRaidLeeroy.cs` |
| Armies that muster, march, besiege, defend and break off | RvR event layer: attackers 128 (keep) / 240 (relic), defenders by share (0.75, steered by the last six sieges), third-realm interference, home defence, musters, siege timers (now running since 1.1) | `AutonomousRvrEventLayer.cs`, `AutonomousRvrEventLayer.Defense.cs`, `AutonomousRvrRally.cs`, `RealmEventPolicy.cs`. `AutonomousRvrDirector.cs` is a pure objective chooser (defend / assault / hunt / regroup) that only the tests call: a template for a director, not a running one |
| Bosses already made bot-friendly | `EpicEncounterMechanics` switches off displacement, position puzzles, invulnerable hazards and add shields for about 50 boss scripts; dragons spawn up to 6 adds from 85% health, one per 15% (`DragonEncounterAdds`, navmesh-checked spawn points); dragon hits are capped at 600; Caer Sidi's horsemen and Galladoria's primals start for raid bots | `scripts/namedmobs/EpicEncounterMechanics.cs`, `EpicEncounterActors.cs`, `Dragons/DragonEncounterAdds.cs`, `CaerSidi/ApocalypseAnd4Horsemans .cs`, `Galladoria/OlcasgeanAndPrimals.cs` |
| Target calling | After 90 s with no damage on the current target, idle raiders within 1,500 units are pointed at it; busy ones after 3 minutes; no bot says it in chat | `RealmRaidDungeonRoute.CallTarget` |
| Telling the realm | Formal herald notices, realm-wide announcements and server firsts, and the 1.1 rule that bots tell /faction in their own words about a minute after real news ("heard we lost Caer Benowyc") | `RealmEventNotices.cs`, `GameWideAnnouncements.cs`, `RealmEventBanter.cs`, the talking update's news lines |
| Rumours as a warning channel | True rumours (a rare named up, a keep with its guards down) | `AutonomousLivingWorld.cs`, `bot_living_rumours` |
| The player's session | `AutonomousLivingWorld.IsNewSession` (first login, or back after 30 minutes), `OnSessionStart` hooks per feature, the first-half-hour pacing, a one-second `Pulse` on the game loop | `AutonomousLivingWorld.cs`, `AutonomousLivingServer.cs` |
| Holidays on the PC calendar | `AutonomousHolidays` and `/lstest holiday <name>` | `AutonomousHolidays.cs` |
| On-demand testing | `/lstest` fires each Living Server part now and says why when nothing fits; `/lstest raid [dragon] [realm]` opens a raid sized to the realm's free 50s | `commands/gmcommands/LivingServerTestCommand.cs` |
| Outside control | The launcher's Realm Events tab talks to the server through a file mailbox (`realm-events.request.json`), with "Stop event" | `RealmEventControls.cs`, `AutonomousRealmRaid.Stop` |
| News that bots retell | A notice queued with a news topic and slots is told once per realm, then retold by bots in their own words; `VoicedRecently` stops repeats | `RealmEventNotices.Queue(id, realm, text, newsTopic, newsSlots)`, `QueueScreen` |
| Records | Realm Events records and the Herald page (raid outcomes, sizes, lengths; Special Forces history reads them) | `RealmEventRecords.cs`, `RealmEventRecordStore.cs` |
| Spectating | `/gmstealth`, `/tele`, GM accounts (launcher "Make me GM") | `commands/gmcommands/gmstealth.cs` |
| Headless rehearsal | The chat simulator plays an evening through the real corpus with no world, writes a readable transcript and asserts rules | `Tests/UnitTests/UT_ChatSimulation.cs` (the pattern to copy) |
| Weather | A weather manager and a GM `/weather` command exist in the base server | `Managers/worldmanager/WeatherManager.cs` (what it can show on the 2003 client is **unverified**) |
| Load guards | `SERVER_STALL` and `Long NpcService.Tick` log lines (the evidence that found issue #25); think intervals that stretch with distance from a human and with population (`AutonomousFidelityPolicy`); the cast-queue guard added after Lydsyg's recast cascade took the server to 37 GB. Note: `ai_budget_ms` is declared and shown by the launcher but no production code reads it (only a test does), so there is no per-tick AI budget to lean on | `AutonomousFidelityPolicy.cs`, `AutonomousAiBudget.cs`, 0.37-se.2 fixes |
| Watchdog | A bot that doesn't move for 15 minutes or has no goal outcome for 45 is teleported to a capital unless something exempts it; raids and sieges register exemptions | `AutonomousStuckWatchdog.cs`, `AutonomousRealmRaid.Protects` (the pattern an event must copy) |

Not there (verified by search): an event script runtime, a cross-feature director (raids, sieges, storylines,
debates and the first-half-hour each keep their own timers), an event-state table, a "chronicle", a monster army
that marches on a town, any boss mechanic the bots understand (AGENTS.md: Darkness Falls, Legion and level 70+ are
won on numbers), a raid *leader* bot (realm raids have parties and a loot ledger but nobody who calls the fight;
combat calls are per group, `AutonomousBotCombatCalls.cs`), a `/events` command.

## 3. The engine in five parts

```
Script (data)  ->  Stage (runtime)  ->  Crowd (existing bot AI)
                        ^                      |
                    Director  <--  Chorus (voices, notices)  -->  Chronicle (records, Herald)
```

**Script.** One file per event or encounter, data not code, so a new boss fight is a file and a test, not a
server change. A script is a list of **beats**. Each beat has a trigger, actions, a budget and a log line.

```
event: albion-invasion-m1              # or encounter: dragon-golestandt
realm: Albion
place: <frontier town or outpost chosen in M0>   # region, position, radius
cap: {monsters: 24, bots: 64, near_player: 48}   # the lag guard, per beat and in total
beats:
  - id: scouts        trigger: start               actions: [rumour "drakoran scouts near {place}", log]
  - id: herald        trigger: after scouts 6m     actions: [notice realm "{place} is under threat", bot_news]
  - id: sign          trigger: after herald 3m     actions: [spawn fires {place} 4, weather {region} fog?]
  - id: horn          trigger: after sign 3m       actions: [yell_at {place} "the horn sounds", open_defense cap 64]
  - id: wave1         trigger: after horn 1m       actions: [spawn {family.l20} 8 from {road_point}, march {place}]
  - id: wave2         trigger: wave1 dead or +4m   actions: [spawn {family.l25} 10 from {road_point}, march {place}]
  - id: champion      trigger: wave2 dead or +5m   actions: [spawn {family.champion} 1, yell "...", focus]
  - id: objective     trigger: champion < 30%      actions: [spawn banner {place}; player_only use; fallback bot after 4m]
  - id: win           trigger: champion dead       actions: [notice, bot_news, chronicle "held", reward tokens]
  - id: lose          trigger: defenders < 25% or +25m   actions: [retreat, occupy {place} 2h, chronicle "lost"]
```

**Stage.** The runtime: reads a script, owns the event's state machine, evaluates triggers once a second on the game
loop (beside `AutonomousLivingServer.Pulse`), performs actions through existing systems, enforces the caps, and
writes `EVENT_*` log lines for every transition. It never touches bot AI directly: it *asks* (a defence rally, a
raid, a Special Forces call-up, an order to a raid brain) the way Leeroy asks with `OrderPull`. Event state stays in
memory, like a raid's: a restart abandons the event and records it "Interrupted" (`RealmEventRecords.Stop` already
does this for raids), which avoids the whole class of resume bugs the research warns about. Only the war-effort
chapter needs durable counters, and that table joins `progress-policy.json` so release worlds ship without it
(AGENTS.md). Occupation after a lost invasion is the one middle case: a timestamp in the ledger is enough, read at
start-up.

**Crowd.** The bots as they are. For an invasion, the defence side is the siege defence rally (recruits idle 50s up
to a cap, musters, goes in together); today it is keyed to an enemy *realm* damaging a keep
(`LastKeepPressure`, `AutonomousRvrEventLayer.Defense.cs`), so it needs a second trigger, monster pressure on a
place, and a cap of its own (`ResponseCap` is 240 for defenders, far above an event's budget). For a world boss, the realm raid. For a war effort, keep sieges and
expeditions. The one new bot behaviour worth adding is **"hold at"** and **"focus"** as raid-level orders, which
`BotBrain.OrderPull`, `AssistPlayerAttack` and `PrepareForTankPull` already show the shape of.

**Chorus.** Every beat speaks through the channels that exist: a herald notice (`RealmEventNotices`), a bot telling
/faction in its own words a minute later (the 1.1 news rule), reactions in /say from bots near the player, a tell
from a friend ("you coming? {place} is getting hit"), and the after-event line. The bots' lines come from the
corpus with the same truth rule as 1.1 (a line that names a place needs the event to be real). Each beat carries a
chat budget so an invasion never floods /faction the way the backlog warns.

**Director.** Small and pure, in the shape of `AutonomousRvrDirector` (which production code never calls, so it
is a template, not a dependency). It hooks into `PrepareCoordinatorTick`, where every other pulse runs. It owns: the
player's session clock (today there is none: sessions only fire one-shot `OnSessionStart` callbacks, so "minutes
into the session" is new state); what the player is
doing (fighting, in town, in the frontier, idle, in a raid); a **tension** number that rises with a fight and falls in
quiet; the rule "one major event per realm at a time", counting raids and sieges as majors; quiet gaps between
events; the bot budget (never commit more than a share of a realm's free 50s to raids, sieges and events together);
and the "better than even" steering (it reads the Chronicle's win rate per event per realm and tunes wave sizes and
the Special Forces call, never the boss). It picks *when* a scripted beat may fire; the script says *what*.

One more thing the director should own, because of five hours a week: **foreshadowing across sessions**. The
scouts-and-rumours beat of a big event can land in one session and the horn in the next, so a Tuesday's "drakoran
seen on the Avalon road" pays off on Saturday. The storyline engine already carries arcs across days
(`AutonomousBotStorylines`); an event's warning beats are an arc of the same kind.

**Chronicle.** Every event ends with a record (type, realm, place, outcome, who was there, length, the player's part)
in the Realm Events ledger (`RealmEventRecords.Begin/Progress/Finish`, the separate `realm-event-records.sqlite3`
the launcher's Herald already reads, with `kind` telling an event from a raid) so the Herald, the Special Forces
history, bot talk ("remember when the drakoran took the retreat?") and the per-realm balance audit all read one
source.

## 4. Scripted epic fights: what bots can and can't carry

**Mechanics the bots can carry today** (verified in the raid code): going in together; a wipe and a regroup;
reinforcements; switching to an epic that attacks them (`REALM_EXPEDITION_TARGET_SWITCH`); not pulling what they
didn't come for; a focused pull on an order; target calling after 90 s of no damage; rezzing and rebuffing between
attempts; walking a cleared corridor to a final room; leading a dragon back into its lair; adds that arrive by boss
health (the dragons already do this through `DragonEncounterAdds`). Mechanics the bots can't do are already switched
off at the boss end (`EpicEncounterMechanics`), which is the seam a scripted fight extends rather than a new one.

**Mechanics they can't:** moving out of an area effect, stacking, interrupting on cue, kiting, tank swaps, splitting
by platform. Don't write fights that need them. A fight that needs 60 bots to move out of fire is a wipe generator.

**So an epic fight is built from these pieces (proposed):**

- **Phases on boss health** with a visible beat at each: at 75% the boss summons adds (a spawn at the boss's feet,
  focus order to the raid), at 50% a door opens or a second named enters, at 25% enrage (the boss casts its real
  ability more often, within the cast-queue guard). Each phase is a spawn or a cast, never a positioning test.
- **The near-loss.** The director lets the first attempt wipe or stall more often than the numbers would (holding the
  Special Forces until the regroup), then the cavalry arrives with its own herald line. The existing wipe rule and
  Special Forces mechanic are the drama engine; they only need a *director* deciding when, instead of a fixed rule.
- **The player's beat.** One objective only a player can do (a usable object: light the brazier, close the breach,
  pick up the standard), announced by the raid leader bot, with a bot fallback after a few minutes so the raid never
  deadlocks while the player is away. The reward is the chronicle credit and the title, not loot.
- **The raid leader's voice.** One bot is leader for the fight and calls the phases in /say and the raid's channel:
  "adds, kill them first", "healers rez and med", "all in, burn him". The 1.1 combat calls already cover the small
  version (pull, adds, low health, oop, wipe).
- **The Leeroy variant** stays as it is (5%), and `/lstest leeroy` becomes one beat among many.

**What a dragon fight could look like with only the above** (proposed, Golestandt as the example since the raid
already lands him and `/lstest raid dragon` opens one on demand; note that the wipe, regroup and Special Forces
call are wired on the dungeon route (`OnWipe` in `AutonomousRealmRaid.cs`) and the changelog says dragon raids
don't call them yet, so M1 closes that gap first): staging as now; the leader calls the pull; at 75% "the ground shakes" and two drakes spawn at
the lair mouth with a focus order; at 50% the dragon's own breath cadence doubles (cast guard respected); at 30% a
scripted near-wipe window where no reinforcements are allowed; on a wipe, the regroup plus the Special Forces with a
herald line naming the player if present; at 10% the player's brazier beat; the kill writes the chronicle and the
bots talk about it for days (storyline hook). Nothing in that list needs a bot to stand in a particular place.

## 5. The lag budget, in numbers from this server

- Issue #25 (verified): one route call stalled the tick for 1.0–1.3 s with level 50s at 75% RvR; killed bots stood
  at 0 HP. The fix was a cap and a wait. Events must follow the same rule: no beat may do unbounded work in one tick.
  There is no per-tick AI budget to fall back on (`ai_budget_ms` isn't read in production), so the cap has to be in
  the beat itself: spawn a wave over several ticks, probe a few routes a pulse the way the dungeon planner does (3).
- Caps that are already proven at ~600–1,000 bots per realm: raids 200 (automatic raids half the online 50s,
  40–200), keep siege 128 vs 64, relic 240 vs 120, Special Forces 24. The dragon kill in the 1.1 test took 56 bots.
- Proposed event caps: 64 bots on the defence rally, 48 within the player's area, 24 monsters alive at once, waves
  of 8–12, one major event per realm, measured in M0 as `cost per monster` and `cost per bot` from `Long
  NpcService.Tick` lines at the owner's real roster. The number that matters is the worst tick during a wave spawn,
  not the average.
- Spawn through the standard template path with standard brains; any boss script that recasts on being hit must
  respect the cast-queue guard (the Lydsyg lesson).
- Park the rest of the realm on its normal goals; an event never changes `bot-goals.json` weights at run time.

## 6. How you see it working with five hours a week

The scarce resource is play time, so the engine has to show itself without the PC.

1. **Rehearsal simulator (cloud, every change).** A test in the chat-simulator pattern: run a script with a fake
   world (bots as counts and roles, the boss as scripted health, time as a clock) and write a readable transcript:
   `[12:00] herald: ...`, `[12:06] wave1: 8 spawned`, `[12:14] champion 30% -> objective`, `[12:18] WIN`. Assert the
   rules: warnings in order, caps never exceeded, every outcome reachable, a loss never worse than not defending, no
   beat fires while the player is offline. Chris reads the transcript the way he reads the simulated evening.
2. **`/lstest event <name> [here] [fast]` (PC, 30–45 minutes).** Fires the event at the player's realm or position
   with every warning compressed (`fast`: minutes become 20–30 seconds), the director bypassed, and a line saying
   why when it can't. `/gmstealth` and `/tele` to watch; `/lstest event stop` to end it. For a fight on a raid,
   `fast` must also shorten the raid's own 15-minute minimum staging (`AutonomousMinimumStagingMilliseconds`),
   which `/lstest raid` keeps today, and needs 40 online level 50s in the realm. Realm notices are dropped when no
   player of the realm is online (`RealmEventNotices.Pulse`), which already matches the rule that nothing fires
   offline.
3. **Timeline report (after every session).** A small script over `server.log` that renders every `EVENT_*`,
   `REALM_RAID_*`, `RVR_EVENT_*`, `SERVER_STALL` and `Long NpcService.Tick` line into one timeline with the worst
   tick per minute. Two minutes to read instead of an hour to watch, and it is the artifact Claude reviews.
4. **Overnight at the real roster.** The director runs naturally at 600 per realm; the morning timeline shows what
   fired, what won, and what it cost.
5. **Honesty gate.** A beat is "seen in game" only when the timeline shows it in a session Chris played; the
   changelog reports automated rehearsals separately, as 1.1 did.

## 7. Build order: a vertical slice before the headline (proposed, differs from the plan)

[LIVE-EVENTS-1.2.md](LIVE-EVENTS-1.2.md) orders M1 as the realm invasion. The invasion is the right headline, but it
is the most expensive first step: a monster army that marches on a town, an occupied state, protected services and a
loss that persists are four new systems before the first beat is seen. mod-city-siege, the closest prior art, warns
that hand-placed march routes leave units stuck.

Proposed order:

| | Milestone | Why first | Done when |
|---|---|---|---|
| M0 | Groundwork as planned, plus the **cost measurement** and the **timeline report** | Without the report nothing can be judged | A findings note with worst-tick cost per monster and bot at the owner's roster |
| M1 | **The engine on the cheapest event: a scripted epic fight** on an existing dragon raid (one realm, then mirrored) with Script, Stage, Chorus, Chronicle and `/lstest event` | Reuses the raid pipeline unchanged; shows the drama curve; proves the lag budget on the thing most likely to break it | A rehearsal transcript and a played fight in each realm, with the near-loss and the player's beat seen |
| M2 | **Director**: session, tension, one-major-at-a-time, bot budget, quiet gaps, win-rate steering; the roaming threat / world boss as the first director-paced event | Cheapest event type, solves the few-hours-a-week problem, and the director is what makes everything else land | Two normal sessions where something happened without `/lstest` and nothing fired while offline |
| M3 | **The realm invasion** as the headline, now on a working engine: staged warnings, waves, champion, defence rally, the player's beat, win and occupied outcomes | The headline ships with the risk retired | One play session per realm with the win rate logged |
| M4 | Holiday festivals (next holiday first), then the war-effort chapter last | As planned | As planned |

The counter-case: an **invasion-lite** (waves spawned at the edge of a frontier outpost, no march, win or retreat
only, occupation deferred) could be the vertical slice instead, and it is the feature the owner asked for. It costs
two things the dragon doesn't: the monster-pressure trigger for the defence rally, and bots who go to a *place* and
fight whatever is there rather than a named target the raid pipeline already knows. If the headline matters more
than the least new code, swap M1 and M3 and keep everything else.

The guild social package (branch `living-world/guild-tiers`) and the historical server are separate workstreams;
the Chronicle is their meeting point (a historical guild's banner in a war-effort total, raid slots for Closed
guilds reading the same event records).

## 8. Decisions for the owner

1. **The thesis:** drama curve over choreography. If "perfectly scripted" means choreographed bots, say so now, because
   the engine above does not deliver that and nothing on this engine can.
2. **The build order** in section 7 (fight first, invasion third) or the plan's (invasion first).
3. **The player's beat:** a usable object with a bot fallback (proposed), the final blow (fragile with bot damage), or
   none (the research's Warfronts warning).
4. **How far the director may bend outcomes:** holding reinforcements to make a near-loss is a thumb on the scale.
   Proposed: it may delay help, never add damage or change the boss.
5. **Occupation:** when an invasion is lost, do services move to a fallback (proposed) or stay usable with only the
   look changed (the plan's open question 2).
6. **Event monsters' loot:** normal loot and capped no-trade tokens (the plan) or normal loot only.

## 9. Checked after a full code map (2026-10-29, later the same day)

A second, file-by-file pass over the raid, siege, messaging and test code confirmed the table in section 2 and
added these, each of which changes a detail above:
- `ai_budget_ms` is not read by production code; section 5 now says so.
- `AutonomousRvrDirector` is only called by tests.
- Dragons already spawn adds by health, and boss mechanics the bots can't handle are already switched off per
  script: the scripted-fight layer extends those seams.
- The watchdog teleports idle bots away after 15 minutes unless exempted; raids and sieges register exemptions and
  an event must too.
- The "80 free level-50 bots" rule counts every online eligible level 50 of the realm, busy or not
  (`OnlineLevelFifty`); `/lstest raid` needs 40.
- The trade, persona and RvR simulators named in RELEASE-PLAN.md live on branch `living-world/trade-sim`, which
  wasn't inspected.

## 10. What this file does not know

- Whether the weather manager shows anything on the 2003 client, and whether NPC armies can attack keep doors
  (both matter for the invasion's "sign" and "border keep" variants).
- The real tick cost of a spawned wave at the owner's roster (M0 measures it).
- Whether a crowd of bots at an event feels alive to a human: the research's open question, answered only by play.
