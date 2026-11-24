# RvR audit 10: the war as a whole, and the player's seat in it

Agent: war (issue #502). Report only; no game behaviour changes. Judged against the period rubric
([reference.md](reference.md), PR for #493; cited as "rubric 2.3" and so on). Structure and evidence findings are cited from
[structure.md](structure.md) (#494) and [evidence.md](evidence.md) (#495), not repeated. Roams, stealth, sieges, defense,
pathing and mechanics have their own audits; this one takes the war's shape (who is out, when, where, why, what it adds up
to over weeks) and the one human in it.

Code read on `origin/main` at `de53d0d3` (2026-11-24). Line numbers are of that commit.

**Evidence tags.** **played** = seen in a PC log (doc and build named); **sim** = a world sim or test-world run;
**unit** = a unit test; **read** = read in code or data only; **computed** = arithmetic from code constants and a played
number, with the inputs named. No dotnet was run for this audit.

**The one-line verdict.** The war is a clock that runs the same at 04:00 as at 21:00, spreads its fights evenly over
every keep and zone, and treats the player in two opposite ways: invisible as a fighter (no group will take them, nothing
hunts them) and centre of the world as a keep attacker (one hit on a guard rallies three realms for four hours). The war
machinery itself is sound in places (realm-balanced logins, relic retakes first, the Herald page). What most decides
whether a player comes back a second night is having a seat: a group to run with and a war that has a shape.

---

## Top 10 findings, worst first

Ranked by how much each decides whether a player goes back to the frontier a second night.

### 1. There is no seat for a human in the realm's RvR groups.

- **What the player sees.** A level 50 walks into Emain and types `/lfg`, or sends a tell to a bot leader: nothing. No
  RvR group invites them, none will take them, none waits for them. The realm's warbands march past. The only way to run
  with bots is to `/invite` free gamebots and lead them yourself. Your group is then outside the war: it gets no siege
  call, no defence call and no part in the event layer.
- **Chain link that fails.** Decide (who groups with whom), at the very first step.
- **Where.**
  - Invites are never sent and never answered in a frontier or battleground: `BotLedGroups.StateBlocks` returns
    `in_frontier` / `in_battleground`, "It holds for asked invites too" (`BotLedGroups.cs:101-121`).
  - An RvR party can never invite: `CandidateBlocks` returns `on_duty` for a raid or RvR party (`BotLedGroups.cs:126-143`).
  - A group with a human in it is "group owned" and skipped by the allocator (`AutonomousObjectiveAssignments.cs:248-249`).
    The defence recruiter skips any party with a non-gamebot or player-held member (`AutonomousRvrEventLayer.Defense.cs:303-305`).
    The siege planner's forces are built from gamebots only (`AutonomousWorldBotController.cs:2673-2691`).
  - The player is not an objective either (structure.md finding 5).
- **How often.** Every frontier and battleground session. **Evidence:** read. The bot-led groups design states the frontier
  block on purpose (docs/BOT-LED-GROUPS.md, "Never ... in a frontier, battleground or raid").
- **Period.** Pugs were common and took anyone (rubric 2.2); the zerg followed a loose lead in realm chat (rubric 2.1);
  guild groups formed on voice. A lone 50 in 2003 got a group in Emain in minutes.
- **Class.** Missing join path. This is not a bug in one place: the force model has no notion of a human member.
  **Where it belongs:** the objective and force model (structure.md piece C). A force is "a group", whatever is in it.
- **Fix.** Size **M**, risk **M**. In steps:
  1. Let a bot RvR party answer `/lfg` and tells in the frontier when it has room and is not marching (lift `in_frontier`
     for RvR parties only, keep it for PvE invites).
  2. A party that takes a human keeps its force id and its event-layer plan. The human counts as a member in `Force`.
  3. The party waits for the human at the muster the way a PvE bot-led group waits at camp (BOT-LED-GROUPS "Lagging behind").
  4. A player-led group of gamebots may ask to join an event ("x for beno").

  Risk: the player-led group exemptions (`IsPlayerLedGroup` in docs/DUTY-LEASE.md) were made so automatic work never takes a
  player's bots. Keep that rule for groups the player *leads*. This change is only for a bot-led group the player *joins*.

### 2. One hit on a keep guard by a solo player rallies three realms for four hours, with a horn.

- **What the player sees.** A level 50 stealther opens on a guard at Bledmeer, or a caster pulls a wall archer. At once:
  - a war horn and "Your attack on Bledmeer Faste has raised the alarm! A small defense force has been rallied!" in the
    centre of the screen, three times in 15 s;
  - realm notices to all three realms;
  - up to 24 level 50 defenders, plus up to 8 helpers from the player's own realm and 8 from the third realm, drawn from
    anywhere in the world, nearest first.

  The event does not end when the player leaves. It runs until the keep falls or four hours pass. A group of 2-8 draws 48
  defenders; a raid of 9-40 draws 128; over 40 draws 240.

  If the player does take the keep, the losing realm is sent back once to retake it, every time. A bot capture triggers no
  such retake.
- **Chain link that fails.** Perceive → decide on the defending side: the realm reacts to a *player* instigator, not to
  what its people saw.
- **Where.**
  - `ObserveKeepAttack` runs on any damage to a door or a guard (`GameKeepDoor.cs:145`, `GameKeepGuard.cs:228`) and raises
    an alarm only for a player instigator (`AutonomousRvrEventLayer.Defense.cs:57-76`).
  - Sizes: `ScaleOf` and `ResponseCap` (`Defense.cs:22-28, 133-139`).
  - The 4-hour event: `ExpiresTick = now + DefenseResponseMilliseconds` (`Defense.cs:173`, `DefenseResponseMilliseconds = 4 h`
    at `:15`).
  - The repel rule skips it: the 10-minute "no attacker reached the keep" end applies only to `!DefenseReaction`
    (`AutonomousRvrEventLayer.cs:1729-1737`). It ends only at expiry or capture (`:1738-1741`, `:2076-2081`).
  - Horn and screen line: `Defense.cs:219-227`. Notices to all three realms: `:233-237`. Recruiting by distance, any region
    (`float.MaxValue` only sorts last): `:286-288`.
  - The retake after a player capture: `AutonomousRvrEventLayer.cs:2079-2081`.
- **How often.** Every time the player damages any keep guard or door. For a stealther or archer working the roads near
  keeps, that is several times an evening. **Evidence:** read. No PC log in the repo shows `PLAYER_KEEP_DEFENSE_SIZED`
  (checked PLAYED-CHECK-1.2.md).
- **Period.** A keep-under-attack alert went to the owning guild with an enemy count (rubric 5.1). Defence was "decided by
  who arrives" (rubric 5.5-5.6). An empty keep fell in 10-20 minutes to 1-3 FGs. A lone ganker who hit a guard and left
  drew nothing.
- **Class.** Staging around the player (the realm is told, not shown), and a commitment with no quiet exit. **Where it
  belongs:** the keep alarm for any enemy, raised through contacts and answered through the objective model
  (structure.md finding 7, pieces B and C). Then the player's attack and a bot attack go through the same door.
- **Fix.** Owner decision needed: Goal 11 (owner, 2026-10-07) chose the sized response on purpose. Proposed, size **S**,
  risk **low-M**:
  1. End a defence reaction 10 minutes after the last pressure, the way bot sieges end (reuse `RepelMilliseconds`).
  2. Raise no event for guard damage alone below Standard scale until the guard dies or a door is hit.
  3. Play the horn only from Standard scale up.
  4. Make the retake a chance, like a bot capture's aftermath, not a certainty.

### 3. The war has no hours: the same load at 04:00 as at 21:00, every day.

- **What the player sees.**
  - At any hour you log in, the frontier has the same number of enemy bots, the same siege pace and the same chat.
  - No quiet morning for a solo, no prime-time push, no night raid on a sleeping realm.
  - A player who plays 20:00-23:00 and a player who plays 06:00-08:00 get the same war.
- **Chain link that fails.** The war's clock (who is out, and when).
- **Where.**
  - The whole roster is online by default (`bot_schedule_mode` off, AGENTS.md rule; owner 2026-11-21).
  - The allocator sends a fixed share of each realm-and-level bucket to RvR every minute with no time input:
    Level 50 = 20/40/40 solo/group/RvR (`BotGoalSettings.cs:41`; `AutonomousObjectiveAssignments.cs:286-300`).
  - Sieges start on a per-minute roll with no clock (`AutonomousRvrEventLayer.cs:433-470`). With none running, the chance is
    25% plus 25% per idle minute, so a new siege starts within about 2-3 minutes of the last one ending.
- **How often.** All day. **Evidence: played** (old build). docs/NIGHT-SIEGES-AND-MARFACH.md section 2, build `168f55d`
  (2026-11-05/06):
  - online level 50s: 590-597 per realm overnight, 581-595 by day;
  - siege parties formed: 43-103 per hour;
  - frontier departures: 312-720 per hour;
  - RvR kills: 1,084-2,199 per hour, "overnight and day alike";
  - "No clock in the code".

  The RvR code changed since (evidence.md F5). The allocator and the siege roll have not gained a clock. **Read** on main.
- **Period.** Population swung 3-5 times from trough to peak (rubric 2.3). Quiet hours had a handful of stealthers and 0-2
  FGs per realm. Night raids won because defenders were asleep. The repo's own night-siege note says the same.
- **Class.** A missing input (time of day) at the top of the war. **Where it belongs:** the allocator (who is on RvR duty)
  and the siege roll (how often a realm opens one). Not in the login schedule, which the owner keeps off.
- **Fix.** Size **M**, risk **M**. The owner rule keeps every bot online, so the proposal changes what bots do, not
  whether they are online:
  1. Multiply the RvR weight in `TargetForPopulation` by an hour-of-day factor, for example 0.35 at 04:00-12:00 up to 1.0
     at 19:00-01:00. The surplus goes to PvE, Darkness Falls and crafting.
  2. Scale `SiegeStartChancePerMinute` by the same curve.
  3. Optionally give each realm a phase offset, so one realm's "night" lets another raid it.

  Make it a Living Server setting, on by default per the AGENTS.md rule, with a `LivingServerSettingsUpgrade` line.
  Measure before and after: departures, sieges opened and kills per hour (all already logged).

### 4. Where the war happens is spread evenly over every keep and zone, so it has no front.

- **What the player sees.**
  - Deep keeps (Renaris, Arvakr, Ailinne) are besieged as often as the front keeps (Benowyc, Bledmeer, Crauchon).
  - Hadrian's Wall gets as much war as Emain.
  - A roam can meet a warband anywhere, because roam anchors are every keep plus random camp cells in every zone of every
    reachable frontier.
  - There is no milegate the whole realm watches, and no reason given for a siege beyond "it is that keep's turn".
- **Chain link that fails.** Decide (where and why to fight).
- **Where.**
  - "All legal reachable frontier keeps get equal selection weight" (`AutonomousWorldBotController.cs:2614-2643`). Reachable
    includes all porter frontiers (`:2552-2555`).
  - `ChooseVariedTarget` picks uniformly among keeps not yet used and clears the used set only when every keep has been hit
    (`AutonomousRvrEventLayer.cs:368-375`, called at `:600-603`). So the targets rotate through all 21 keeps before any
    repeats.
  - Roam objectives: a patrol anchor at every keep, plus 24 random frontier camp cells (`AutonomousWorldBotController.cs:2636-2667`).
  - No reason weighs in: Darkness Falls ownership is never read by the planner (`grep` of the event layer and planning view
    finds no `DarknessFall`). The one reason that exists is the relic feeder (`AutonomousRvrEventLayer.cs:604-617`).
- **How often.** Every siege choice. Computed: with the rotation, a front keep and a deep keep each come up once per cycle
  of the enemy keeps (14 for each attacker). The front keep's share is about 1 in 14, not the period's "several times a day"
  versus "rarely". **Evidence:** read and computed. Roam-circuit weighting toward milegates is the roam audit's (#496); this
  finding is about the strategic layer above it.
- **Period.** Emain carried about 80% of the fighting (rubric 1.5). Milegates were the busiest spot. Front keeps flipped
  often and deep ones rarely (rubric 1.2). DF was the commonest reason for a take on a quiet night (rubric 1.8).
  Rubric "Fails": "deep keeps flipping as often as the front ones; fights spread evenly over all twelve zones".
- **Class.** A designed-in uniform rule (the comment says so) where the period had strong gradients. **Where it belongs:**
  the objective model (structure.md piece C), as a weight on each objective.
- **Fix.** Size **S-M**, risk **M** (siege routes to deep keeps are where the pathing fixes live; fewer deep sieges means
  fewer of those walks):
  1. Weight keep targets by depth: about 4 for the front keep, 2 for the mid keeps, 1 for the deep keeps. Add a bonus for a
     keep that would flip Darkness Falls.
  2. Replace the rotation with "not the same keep twice in a row".
  3. Weight the frontier choice by how much fighting it holds: Emain first, Hadrian's Wall last.
  4. Log `reason=` on `RVR_EVENT_OPENED` (front, DF, relic feeder, retake).

### 5. Over weeks, the bots out-rank any human: realm rank inflation with no brake.

- **What the player sees.** In the first week the enemy are RR1-RR2, as a new server should be. After a month or two of
  the server running, a level 50 meets RR4-RR6 bots everywhere, with every realm ability they can buy. A new player, or one
  who plays an evening a week, falls further behind each week, and the gap never closes.
- **Chain link that fails.** Win or lose → what next, over weeks.
- **Where.**
  - Every gamebot kill pays realm points by the player formula, `(level-20)^2 + realmLevel`, so about 900 for a level 50
    (`AutonomousBotRealmPointRewards.cs:24-29`).
  - The group bonus is 12.5% per extra contributor (`:50-51`). There is a 5-minute repeat-kill window (`:123-131`,
    `rp_worth_seconds` 300).
  - The whole roster is online whenever the server runs (finding 3). Realm rank 5 bots lean to RvR goals
    (`AutonomousObjectiveAssignments.cs:356`, `BotMilestoneLife.RvrLean`), a positive feedback.
- **How often.** **Computed.** Inputs:
  - played kill rates of 1,084-2,199 RvR kills per hour (NIGHT-SIEGES, `168f55d`);
  - about 236 RvR level 50s per realm (590 × 40%);
  - 900-1,690 realm points paid out per kill (solo to a full group of 8).

  That gives about 1,400-5,200 RP per RvR-bot-hour, or 560-2,100 per online level 50 hour.

  Realm rank thresholds [M]: RR3 61,750; RR4 213,875; RR5 513,500.

  So the average bot reaches RR4 in about 100-380 server hours and RR5 in about 245-920. A server left running nights
  (the owner's runs went 11-16 hours) gets there in weeks to a few months. No PC log in the repo gives a rank histogram, so
  this is a stated estimate.
- **Period.** By late 2003 most RvR regulars were RR2-RR4, RR5 marked a serious player, RR6-7 a handful per server
  (rubric 2.5). "Fails: lots of RR7+".
- **Class.** No sink for an always-on population. **Where it belongs:** the reward rule plus the allocator (finding 3 cuts
  the hours; this one bounds the rate).
- **Fix.**
  1. Size **S**, risk **none**: log a realm-rank histogram per realm per hour (`RVR_RANKS realm= rr1= rr2= ...`).
     Measure before acting.
  2. If confirmed, size **S-M**, risk **M**, owner decision: a "bot realm point rate" setting that slows bot gains toward
     the period spread, or a decay of bot realm points for bots above the realm's median. Never touch the player's own
     points.

### 6. Battleground filling depends on the goal settings and the roster's levels; on the owner's 1.2.2 world, Hibernia sends about 3x the others and the 20-35 brackets are nearly empty.

- **Correction (2026-11-24).** The first version of this finding said "Abermenai never fills". That was wrong for the
  owner's world. It holds only for the code's default goal rows. The played numbers below replace it.
- **Played, Living Server 1.2.2, owner's PC.** A fresh 1.2 install from 2026-11-20 that started at level 1, so the roster
  skews low. Launcher totals:
  - 2,940 bots online: 979 Albion, 982 Midgard, so about 979 Hibernia.
  - Battlegrounds tab: "67 bots inside 2 of 4 battlegrounds"; every keep Neutral.

  | Battleground | Albion | Midgard | Hibernia | Total | On their way (A/M/H) |
  |---|---|---|---|---|---|
  | Abermenai (15-19) | 12 | 13 | 37 | 62 | 2/2/4 |
  | Thidranki (20-24) | 1 | 2 | 2 | 5 | 2/0/0 |
  | Murdaigean (25-29) | 0 | 0 | 0 | 0 | - |
  | Caledonia (30-35) | 0 | 0 | 0 | 0 | - |

  The owner's Bot Goals rows are not the code defaults. They are, Solo PvE / Group PvE / RvR / Battlegrounds %:
  - Levels 1-19: 40/50/0/10.
  - Levels 20-49: 30/40/10/20.
  - Level 50: 20/40/40/0.

  The code defaults are 60/40/0/0, 35/35/15/15 and 20/40/40/0 (`BotGoalSettings.cs:41`). The release excludes
  `bot-goals.json` (`build_release_037.py:69`), so an installer user starts on the code defaults.
- **What the player sees.**
  - On the code defaults, a level 17 in Abermenai is alone. Levels 1-19 have no battleground share, and the low band's
    allocation reads that row's share (`AutonomousObjectiveAssignments.cs:178-182`). This is **read**. The owner's world
    fills Abermenai only because its 1-19 row gives 10%. That is **played** on 1.2.2.
  - Abermenai is lopsided: Hibernia has 37 inside against 12-13 for the other realms, with near-equal rosters. The
    allocator works per realm-and-level bucket with the same shares (`:286-300`), so a 3x skew is not by design.
  - A level 24 in Thidranki meets 5 bots in all, and the 25-35 brackets have none, even with 20% of levels 20-49 sent to
    battlegrounds. On the owner's earlier test world every gamebot was 50, so every battleground was empty until the
    launcher's "+ Lv.20" button made 50-100 per realm (`BattlegroundBotLevels.cs:4-7`, build `1dc66b6`).
  - When a battleground does fill, a realm needs 8 of its bots inside before a wave goes (`AutonomousBattlegroundWave.cs:29, 85`).
    One keep capture is recorded in about eight played battleground builds (evidence.md F6). All keeps were Neutral on 1.2.2.
- **Chain link that fails.** Population (who is sent where), then fight → win.
- **Where.**
  - Levels 20-49 send their battleground share capped by how many bots are 20-35 (`:188-196`). Each bot goes to its own
    level's bracket. Nothing tops up a bracket a player is in.
  - Nothing balances realms inside a bracket.
- **What would settle the two open questions.**
  - *The Hibernian 3x.* Counts per realm, from the database (`offline` bot records: `Level`, `ObjectiveKind`) or the
    launcher's realm cards:
    - bots at levels 15-19;
    - bots with the Battleground goal;
    - bots inside Abermenai.

    If Hibernia simply has 3x the 15-19 bots, it is level spread. If it has the same number but more hold the goal, the
    cause is the allocator or the personality planner (`AutonomousBotPersonality.PlanGoals`, `:351-357`). If the goals are
    equal but more are inside, the cause is the trip or the exit: Albion and Midgard bots failing to arrive, or Hibernian
    bots not leaving. In that case, check the battleground arrival and exit lines in the server log per realm over an hour.
  - *The empty 20-35 brackets.* The same counts per realm for levels 20-24, 25-29 and 30-35, and how many of those hold the
    Battleground goal. Few bots at those levels on a 4-day-old world means it is level spread, and the brackets fill as the
    roster levels. Bots that hold the goal but are not inside or on their way mean the goal does not lead to arrival.
- **How often.** Every battleground visit. **Evidence:**
  - **played** on 1.2.2 for the counts above;
  - **played** on `1dc66b6` for the empty all-50 world;
  - **played** on `43c43aa` for the single capture;
  - **read** for the code defaults.
- **Period.** Battlegrounds were the busy RvR of levels 15-35 (rubric 1.9). Thidranki at prime time was crowded.
- **Class.** Population sent by quota of the roster, with no per-realm balance inside a bracket. **Where it belongs:** the
  allocator.
- **Fix.** Size **S-M**, risk **low**:
  1. Give the code default for levels 1-19 a battleground share, as the owner's row has (10%), with a
     `LivingServerSettingsUpgrade` line.
  2. Balance realms inside a bracket: a realm far above the others' count gets no new battleground goals there until the
     others catch up.
  3. Add a per-bracket floor: while a player is in a battleground, the allocator prefers that bracket's eligible bots.
  4. Have the launcher warn "no bots at levels 20-24: Thidranki will be empty" on the Battlegrounds panel.

  Keep-capture tactics are the siege audit's (#498).

### 7. Frontier "inc" calls name a real keep but make up the attackers and the count.

- **What the player sees.** Realm chat: "15 hibs at beno". The player rides to Benowyc and finds 40 Midgard bots. Or: "inc
  crauchon, 9+" when nobody saw anything; the keep is in a siege list, that is all. Field incs ("inc 2fg amg", "8 hibs
  left of mmg") never happen: chat speaks only of keeps.
- **Chain link that fails.** Perceive → tell (the world tells the player what is happening).
- **Where.** `AutonomousChatFieldTalk.Inc` (`AutonomousChatFieldTalk.cs:1229-1244`):
  - it picks a keep from `AttackedKeeps` (real);
  - the attacking realm is drawn at random from the two enemy realms (`EnemyRealm`, `:2779-2783`);
  - the count is `3 + random.Next(20)`.

  The real attacker, defender and stage are available in `AutonomousRvrEventLayer.ActiveSieges` (`:2047-2054`, already
  built "for chat").
- **How often.** Inc is the heaviest frontier talk kind (weight 4 in RvR, `:1117`). The wrong realm comes up half the time
  by construction. **Evidence:** read.
- **Period.** "INC", count and direction, 1-5 s after the enemy is seen, "often a little wrong in the count" (rubric 3.1,
  7). A wrong realm is not "a little wrong". A wrong realm in inc calls is a tell that nobody is watching.
- **Class.** Talk not tied to state (the realism audit's class). **Where it belongs:** the say gate of the realism plan
  (#475): a frontier line may only state what a speaker could know.
- **Fix.** Size **S**, risk **low**:
  1. Take the realm and a fuzzed count (plus or minus 30%) from `ActiveSieges` and its attendance.
  2. Have a bot that saw the enemy say the inc.
  3. Later add field incs from contacts (`RVR_SPOT`; structure.md piece B), with zone and milegate names.

### 8. Hibernia starts the war weaker, and nothing pulls a losing realm back.

- **What the player sees.** A Hibernian player watches nGed, Da Behnn and Ailinne fall first and most often. Darkness Falls
  goes to Albion or Midgard and stays. A Hib relic keep loses its sentinels early. An Albion player sees the reverse: an
  easy Hib frontier.
- **Chain link that fails.** War balance over an evening and over weeks.
- **Where.**
  - The release world starts every Albion and Midgard claimable keep at level 5. Hibernia's Crauchon starts at 4, and nGed,
    Da Behnn and Ailinne at 1 (`source/tools/release-world-defaults.json`, keeps 100-106; also rubric 9.2).
  - The upstream catch-up `KeepManager.GetRealmKeepBonusLevel` (`7 - keeps held`, `KeepManager.cs:720-724`) has no caller
    outside `KeepManager` (grep). It is dead.
  - The only catch-ups in play:
    - the server-wide siege balance (`SiegeDefenseBalance`), which looks at win share, not at realms;
    - relic retakes first (`AutonomousRvrEventLayer.cs:598-600`);
    - the size of the target pool: a realm holding more keeps offers more targets, so with uniform choice it is attacked
      more (finding 4).
  - Logins are balanced per realm (`AutonomousRealmLoginBalancer.cs`). That is sound.
- **How often.** Every fresh world. **Evidence:** read (data). How keep level sets guard strength in this engine is for the
  siege audit (#498). Whether Hib keeps fall faster is a PC count (below).
- **Period.** Keeps started even. Imbalance came from population and skill, not from data.
- **Class.** A data accident plus a dead balance hook. **Where it belongs:** release data, then the objective weights.
- **Fix.** Size **S**, risk **low**: set the four Hib keeps to level 5 in `release-world-defaults.json`. This raises them,
  which fits the "never weaken keeps" rule. Owner sign-off needed. Log keeps held per realm per hour. Decide on a catch-up
  only after a count shows a snowball.

### 9. Darkness Falls is shut to every realm on a fresh world, and after a restart with tied keep counts.

- **What the player sees.** On a new install, or after a restart while two realms tie on keeps, the Darkness Falls
  entrance refuses everyone (realm and bots alike) until a keep changes hands. The 15-minute "will lose access" grace that
  follows a flip is a later-patch rule.
- **Chain link that fails.** War → reward (DF is the war's prize for most players).
- **Where.**
  - `DFEnterJumpPoint.DarknessFallOwner` starts at `None` and is recomputed at script load (`DFEnterJumpPoint.cs:48, 70-73`).
  - `NextOwner` keeps a tied *holder*, but with no holder a tie returns `None` (`AutonomousDarknessFallsPolicy.cs:79-93`).
  - The release world is 7/7/7. The holder is not persisted.
  - `allow_all_realms_df` is false (`ServerProperties.cs:1440`; not set in `release-server-settings.json`).
  - The grace: `DFEnterJumpPoint.cs:26-27, 51` (rubric 1.8: a 1.81 change).
- **How often.** Every fresh world until the first capture. After restarts when counts tie. **Evidence:** read.
- **Period.** Whoever had the most keeps found the portal open (rubric 1.8). At 1.65 the flip was immediate for new
  entries.
- **Class.** State not persisted across restarts.
- **Fix.** Size **S**, risk **low**:
  1. Persist the DF holder (as relic raids are, `AutonomousRvrRelicPersistence`) and use it as the tie-breaker at start.
  2. For a fresh tied world, owner decision: leave it shut, or open it to all until the first flip.
  3. The grace: owner decision (period says none).

### 10. Kills have no texture: no emote over the corpse, no word after a win.

- **What the player sees.** A bot group kills the player and walks off in silence. The player kills a bot and nobody
  reacts on the field. In 2003 the corpse got a `/laugh`, `/spit`, `/rude` or a `/bow` after a fair duel.
- **Chain link that fails.** Win or lose → texture.
- **Where.**
  - Bot emotes exist, but the cheer fires only on a level-up (`AutonomousBotBodyLanguage.CheerDing`,
    `AutonomousBotBodyLanguage.cs:276-300`).
  - No RvR kill hook calls an emote (grep of `eEmote.Laugh|Spit|Rude|Cheer|Bow` outside beggars and dings).
  - Second-hand chat about the player's RvR record exists (`AutonomousPlayerDeeds.cs:58, 91-97, 146-152`, tests only).
- **How often.** Every kill. **Evidence:** read.
- **Period.** Rubric 7: emotes after kills, varied; "Fails: none, or the same one every time".
- **Class.** A missing reaction. **Where it belongs:** the kill event (`AutonomousBotRealmPointRewards.Award` and
  `PayGamebotsForPlayerKill` already see every RvR kill).
- **Fix.** Size **S**, risk **low**. On an RvR kill, with some chance, one nearby killer emotes at the corpse:
  - `/laugh`, `/spit` or `/rude` mostly;
  - a `/bow` when it was one-on-one;
  - never twice on the same corpse.

  Respect the realism say gate for any words.

---

## An evening in the frontier, by seat

Built from the code above and the played rates of `168f55d`. **[E]** = estimate. These are the same for every realm except
the Hibernian keep levels (finding 8) and each realm's muster distances. The owner's world has about 590 level 50s per
realm. Rates scale with the roster.

| Hour | Level 50 solo or duo with a companion (Emain) | Level 35 (frontier) | Level 24 (Thidranki) |
|---|---|---|---|
| Any hour, 20:00 or 04:00 alike | Enemy warbands everywhere along keep roads; 1-4 sieges running somewhere; realm notices at most 1 a minute (`RealmEventNotices.cs:125`) [E] | Grey to every level 50 bot. No rule spares greys (no con check in `FindRvrTarget`, `AutonomousWorldBotController.cs:2272` on); greyganking was period (rubric 1.5). Level 20-49 RvR bots (15%) only roam or hunt (`AutonomousRvrEventLayer.cs:508-509`) | Bots only if the roster has 20-24s. With them: waves of 8+, a siege notice when a realm first hits the keep (`Defense.cs:86-108`) |
| Finding a group | None (finding 1). `/invite` free gamebots and lead | Same | Same (`in_battleground` block) |
| Meeting the enemy | By chance on a bot's walk to a bot objective; nothing hunts the player (structure.md 5) | Same | Same |
| Hitting a keep | 24-240 defenders and a horn for 4 h (finding 2) | Same | Battleground keeps: an announcement, no defence event |
| Dying | Release to bind; RP worth window 5 min; no emote (finding 10) | Same | Same |
| Winning | RP as for a player kill (`AutonomousBotRealmPointRewards.cs:24-55`); deeds chat after 25-50 kills | Little RP from greys | XP and RP per kill (#396) |
| Weeks later | RR4-6 enemies (finding 5) | Same | Bots leave the bracket on level-up (`AutonomousWorldBotController.Battleground.cs:33-37`) |

**With no player in the frontier** everything above runs anyway (sieges, relic raids, rank gains). Notices are dropped when
no player of that realm is online (`RealmEventNotices.cs:133`). That is right for a living world. It is also why a player
who comes back after a week finds the war moved on without them, which the Herald page reports (`HeraldPage.cs`).

**Balanced around the player's realm, or neutral?** Neutral: logins balance by realm, and the allocator and siege roll are
per realm with no reference to the player's realm. The two player-centred parts are the defence reaction and the retake
(finding 2).

## What I checked and found sound

- **Realm balance of logins**: the next login goes to the realm with the fewest active and pending bots
  (`AutonomousRealmLoginBalancer.cs:11-31`). Read.
- **Relic campaign logic**:
  - a realm whose relic is held goes for that keep first (`AutonomousRvrEventLayer.cs:598-600`);
  - a relic keep with more than two sentinel keeps held turns a relic choice into a feeder keep siege (`:604-617`, the 1.65
    rule, rubric 1.7);
  - relic sieges are about 12% of siege starts, for six or more strong bots only (`:425, 624`).

  Relic raids are big and comparatively rare in shape; their hour is not period (finding 3). Read.
- **Relic raids survive a restart**, and a carried relic goes back to its pad, as in the period (`AutonomousRvrRelicPersistence.cs:4-14`).
  Read.
- **One attack per realm at a time, with a catch-up roll** so one realm cannot open every siege (`AutonomousRvrEventLayer.cs:441-470`).
  Read; the 2026-10-19 case is cited in the code.
- **The Herald**: the launcher writes a read-only realm news page from the database (`HeraldPage.cs`). This matches the
  period's realm war page (rubric 5.7). Read.
- **Realm-wide news** of sieges, captures, relics and DF flips reaches players by bot voices, with written notices as the
  fallback, rate-limited to one a minute per realm (`RealmEventNotices.cs:9-140`). Read.
- **Chat promises**: a bot that says "omw" to a keep call actually goes (`AutonomousWorldBotController.ChatPromise.cs:7-19`).
  This is the one way a player's words move bots in the frontier. Read; tests only.
- **Player kill rewards**: gamebots pay realm points, bounty points and experience as players of their level would (#396;
  `AutonomousBotRealmPointRewards.cs:24-95`). Read.
- **Players' companion raids** siege with the player and take jobs (rams, engines, wall clear) (`CompanionRaidSiege.cs:23-33`).
  Read.

## What I could not check, and why

- **Navmeshes and a live server**: not used (report only, no dotnet run). Every frequency above is read or computed.
- **How keep level sets guard and lord strength** in this engine (finding 8): for the siege audit.
- **Realm rank distribution of bots on any played world**: no log in the repo carries it (finding 5).
- **The 1.65 client's in-game war map**: the server's `WarMapMgr` is off (`enable_warmapmgr` false,
  `ServerProperties.cs:1477`) and records fights only in region 163, New Frontiers (`AbstractServerRules.cs:1945`). Whether
  the classic client showed Old Frontiers fights was not settled; the Herald covers keep owners.
- **Battleground realm-rank caps** (`DbBattleground.MaxRealmLevel`) and whether bots are moved out on passing them: for the
  mechanics audit.
- **Companions in the frontier** (`CompanionPvpEngagement`, `CompanionStealth`): read only in passing; for the stealth and
  mechanics audits.

## What only the owner's PC can settle

Each is a place, an action and a log tag.

1. **The hourly shape.** Run overnight. Count per hour:
   - `RVR_EVENT_OPENED`;
   - frontier departures;
   - `RVR_BOT_KILLED`;
   - `RVR_PARTY_FORMED` by realm.

   Expected today: flat (finding 3).
2. **Where sieges go.** Count `RVR_EVENT_OPENED target=` by keep over a day. Expected today: about even over the 21 keeps
   (finding 4). Then count `RVR_KEEP_TAKEN` per realm per hour. Do Hib keeps fall first (finding 8)?
3. **The player's keep alarm.** Hit one guard at Bledmeer with a level 50, then ride away. Look for
   `PLAYER_KEEP_DEFENSE_SIZED`. Then look for the event's end line (`RVR_EVENT_ENDED`, reason "four-hour defense response
   expired"), and see how long defenders stand at Bledmeer (finding 2).
4. **Joining a group.** In Emain, `/lfg` and send a tell to a bot leader of an RvR party. Expect no answer and the
   `in_frontier` block in the log (finding 1).
5. **Inc calls.** Over an hour of realm chat in the frontier, compare each inc's realm and count with the
   `RVR_EVENT_OPENED` attacker and the attendance (finding 7).
6. **DF on a fresh world.** Start a released world. Try the DF entrance before any keep falls (finding 9).
7. **Battlegrounds by bracket.** With the default goals and a mixed-level roster, count bots per battleground region
   (`BG_KEEP_STATUS near=`) for an hour, with the per-realm level and goal counts of finding 6. On 1.2.2 (owner's goal rows), Abermenai had 62 inside with Hibernia at 37.

## Measuring tools worth building

- **The "war hour" line**: one log line per hour per realm, with:
  - bots on RvR duty and in each frontier;
  - sieges opened and their reasons;
  - keeps held;
  - relics held;
  - the DF holder;
  - a realm-rank histogram;
  - kills and deaths.

  Size S. It answers findings 3, 4, 5 and 8 with numbers, and feeds evidence.md 5.1 (the "RvR hour").
- **A player-seat line**: per player per hour, with:
  - enemies within 3,000;
  - times attacked;
  - deaths;
  - groups offered and joined;
  - notices received;
  - defence events caused.

  Size S. This is the "what one human sees per hour" number the brief asks for, and nothing logs it today.
- **A war replay**: from the hourly lines, a day as a strip chart (keeps per realm, sieges by keep). A Python script over
  the log. Size S.

---

## Hand-back

- Report: `docs/rvr-audit-1.2.3/war.md`. Report only, docs-only.
- Top defects, worst first:
  1. No seat for a human in any RvR group: invites are blocked in frontiers and battlegrounds, RvR parties are "on duty",
     and player groups are outside the event layer (`BotLedGroups.cs:101-143`, `AutonomousObjectiveAssignments.cs:248`).
  2. One player hit on a keep guard opens a 4-hour, three-realm defence of up to 24-240 bots with a horn, and it never ends
     on quiet (`Defense.cs:57-76,133-139,173`; repel excluded at `AutonomousRvrEventLayer.cs:1729`).
  3. No hours: flat population and siege pace all day (played on `168f55d`; no clock in the allocator or the siege roll).
  4. Uniform targets over every keep and zone, with a rotation through all keeps and no DF or front weighting
     (`AutonomousWorldBotController.cs:2615`, `AutonomousRvrEventLayer.cs:368-375`).
  5. Rank inflation: computed RR5 in about 245-920 server hours, with no brake and RR5 leaning to RvR.
  6. Battleground filling (corrected; played on 1.2.2): Abermenai 62 inside with Hibernia at about 3x the others; Thidranki 5; Murdaigean and Caledonia 0. On the code defaults Abermenai gets no bots.
  7. Inc calls name a random enemy realm and a random count (`AutonomousChatFieldTalk.cs:1229-1244`).
  8. Hib keeps start at levels 1/1/1/4 against 5 everywhere else; the upstream catch-up hook is dead.
  9. DF is shut on a fresh world and after a tied restart; the holder is not persisted.
  10. No emote or word after RvR kills.
- Sound: realm-balanced logins, relic retakes and sentinel feeders, relic persistence, one siege per realm with catch-up,
  the Herald page, chat promises, player-equivalent rewards.
- Evidence: mostly read and computed. Played for the flat night (`168f55d`), the empty battlegrounds (`1dc66b6`) and the 1.2.2 battleground counts.
  Every played item comes from builds older than main.
- First moves: the war-hour and player-seat log lines (S), then findings 2, 7 and 9 (all S, low risk), then 1 and 3 (M,
  owner decisions).
