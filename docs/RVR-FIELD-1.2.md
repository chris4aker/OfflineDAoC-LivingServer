# Open-field RvR for 1.2: bots that fight like people

Owner: Chris. Ordered 2026-11-16: "bots should spot bots the same way, bots should be unmistakable for human players
is the goal. You need to do the real navigation data and mapping, and fix the rest of these open field RvR tactics.
Come up with the full analysis and framework and correct design to make this incredible. To your point, focus on
tactics but the routing and pathing should be huge too. Just like 2003 there should be some roaming 8 man groups off
on the fringes or checking PvE or dungeon or named spots for enemy realm players. This should all be in 1.2".
("Unmistakable" here means you cannot tell a bot from a human player.)

This is the design for realm-versus-realm (RvR) fights away from keeps. It builds on
[BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md) and the RvR perception part of [LIVING-SERVER.md](LIVING-SERVER.md).
**Status: approved by the owner on 2026-11-16 (all nine recommendations below); being built for 1.2. Nothing here
is seen in game yet.** The route and check-spot data were proven by probes
on the navigation mesh (the server's map of walkable ground): tests only.

## What changes for you

1. Enemy bots notice each other the way they notice you: by chance, after a delay, with a spotter calling it out.
   The instant lock at 5,000 units goes.
2. A bot group fights as a group. One member calls the target, the others follow a beat late, crowd control holds
   the rest, and tanks guard their healers.
3. Groups think first. They watch, count heads, run from bad odds, regroup and come back.
4. They make the mistakes people made: tunnel vision, broken mez, chasing too far, resting where they just fought.
5. Armies leave a milegate on up to three roads instead of one line.
6. Eight-man groups roam the fringes, checking milegates, levelling camps, dungeon doors and named monsters for you
   and for enemy levelling bots.
7. Each realm brings its own 2003 group, and bots pick targets in the 2003 kill order.
8. All of it is on by default. Fights at a keep, raids and battleground assaults stay as they are in 1.2.

## What is true today (measured 2026-11-16)

From the owner's PC log, builds 88b6ab3 and ccba364, 11:45 to 16:39:

- **Volume:** about 5,500 bot-on-bot frontier kills, in about 710 separate open-field fights. 46 fights had 16 or
  more deaths. All three realms died in about a quarter of the fights with 8 or more deaths.
- **Where:** an estimated 57% of the kills were away from keeps (keeps were located from where guards made kills).
  The check-spot survey's stricter test, 4,000 units or more from a keep's centre, gives about 73%. Most fighting
  is in the open field either way.
- **One hot spot:** about 550 of roughly 3,100 open-field deaths (about 18%) fell in one small area on the Pennine
  Mountains / Hadrian's Wall border. The lane survey adds that 81% of the deaths in Albion's frontier lie within
  1,500 units of a portal-keep main route.
- **Levelling bots:** 513 frontier deaths were bots with a levelling goal. Nobody hunted them; they were met by
  chance.
- **Seeing you:** seen working on the owner's PC on build 3d67b6f (2026-11-15). Bots noticed the owner's character at
  516 to 1,575 units, by chance and with a delay. The spotter call (`RVR_SPOT`) was not seen.
- **Seeing each other:** bots still acquire enemy bots instantly at 5,000 units.

What the code does in a field fight today (from a code survey):

- **It does:** mez (freeze) and root the densest clump without breaking its own mez; cure mez; heal first; play speed
  songs. Stealthers shadow a target but have no opening attack.
- **It does not:** run an assist train (everyone on one called target; targets are spread on purpose today); pick
  priority targets; interrupt casters; peel (pull an attacker off a friend) when the attacker is a player or a bot;
  position, spread or kite (fight while backing away) against players or bots; size up, retreat or regroup. That
  last logic exists but is not connected.
- **Travel:** a siege march follows one leader on one route. Roamers pick a random objective and never hunt
  levelling bots.

## The seven principles

1. **Human-plausible, not optimal.** Every rule has a delay, a spread and a failure rate, set by a skill tier per
   group.
2. **One perception rule for everyone.** Bots notice bots exactly as they notice you.
3. **A group thinks together.** One shared plan per group. Between the groups of a warband, coordination is loose
   and lossy.
4. **Realm flavour.** Group make-up and kill order differ by realm, as in 2003.
5. **Geography is data.** Lanes, check spots and circuits ship as data proven on the navigation mesh and checked at
   server start. Today's route is the fallback.
6. **Scope guard.** Raids, battleground keep assaults, fights at a keep, and any group a player leads or is in keep
   today's rules in 1.2.
7. **Provable.** Every behaviour has a log line and a before/after measure on the owner's PC.

## The four layers

### 1. Eyes: one perception rule for everyone

- **Same rule, new subjects.** Enemy bots and their pets are noticed by the rule already used for you. On normal:
  always within 1,000 units, never beyond 3,500, a chance per look in between, then a reaction delay of 0.5 to 2
  seconds. The bot that notices calls it to its group.
- **Noticing a group.** Noticing one enemy means noticing its group mates within 1,500 units of it.
- **Who looks.** Two members of each group take turns as lookers. Soloers look for themselves. The rest take their
  target from the group's plan. At most 3 bots of a realm look for the same enemy group in each second.
- **How far.** Bots away from an objective scan to where noticing stops (3,500 on normal, never less than 2,600)
  instead of 5,000. That is about half the area.
- **Unchanged.** A bot that is hit fights back at once. Bots holding a keep or working a siege acquire enemies
  instantly, as today.
- **It applies to you too.** The lookers and the per-group cap also apply when the enemy is you. The time-to-notice
  table in LIVING-SERVER.md stays the target; tests must show it still holds.
- **Cost.** About 416 scans a second against 800 today, and about 2,000 sight checks against up to 6,400, on an
  assumed load of 300 RvR bots per realm. Not measured.
- **Stealth.** Bots cannot see stealthed enemies at all today, so a bot stealther is invisible to every bot until it
  strikes. The design adds a minimal rule: a bot may notice a stealthed enemy only at close range, by chance, and
  never better than a human in its place could. The exact rule is not written yet.

### 2. Mind: the group's fight plan

Each bot-only group in the open field shares one plan, refreshed every second or two. Its parts:

- **Main assist and the called target.** One fighter is the main assist, and his target is the group's target. A
  call is held 4 to 8 seconds. As in 2003, a caster seen using Mastery of Concentration, or an enemy just
  resurrected, becomes the call.
- **Kill order.** The 2003 order: casters, healers, bladeturn casters, crowd controllers, tanks last ("anything that
  wears a dress and moves their arms about"). Realm variants are in the playbooks. A target the group cannot reach,
  such as one on a wall, is never called.
- **Crowd control plan.** The mezzer opens on the charge. The kill target is never mezzed. A resisted mez is cast
  again.
- **Interrupts.** Each enemy healer or caster that is not the kill target gets one bot assigned to keep it from
  casting. No 2003 source describes an interrupt rotation, so this is our design.
- **Protect assignments.** Shield tanks guard a named healer or caster and slam whoever gets close. Peeling now
  triggers when the attacker is a bot or a player.

The plan also holds a stance, the group's mood:

| Stance | When | What the group does |
|---|---|---|
| Observe | An enemy group, or a fight between the other two realms, is in view | Watches and sizes up: heads, healers, levels, health, power |
| Engage | The odds look fair or better | Runs the fight plan |
| Hold | Members are strung out or still arriving | Stays together and waits |
| Kite | The enemy is a bigger or melee-heavy force | Fights while backing away |
| Retreat | The odds are bad or the fight is lost | Drops targets, sprints, uses escapes |
| Regroup | After a retreat or a wipe | Meets at a rally point, rests, re-forms, goes back out |

- A stance lasts at least 5 seconds. The exact odds for each change are not set; they are tuned in tests.
- Impatient groups stop watching early. A group remembers its recent wipes.
- A bot that is hit always fights back, whatever the stance.
- Between groups of a warband, a call or a retreat is passed on half the time, 4 seconds late.

### 3. Hands: how each role carries it out, and gets it wrong

| Role | Does | Goes wrong like a person |
|---|---|---|
| Main assist | Picks by the kill order, holds the call | Scores wobble by about 20%; 5-15% of calls land on a tank |
| Fighters and casters | Switch to the call after their own delay | Some tunnel: they stay on the nearest enemy or their own attacker |
| Crowd control | Mez on the charge, recast on a resist | Area damage breaks the mez; a mezzed mezzer is not covered for 2-4 s |
| Interrupters | Keep one enemy caster or healer busy | Look again only every 1.5-3 s |
| Shield tanks | Guard a healer, slam what gets close | Over-extend and leave the healer |
| Healers | Heal first, kite ("run a bit, throw heal") | Stand too close together; charge with the tanks |

- **Positioning.** Casters and healers stand behind the tanks. Members keep more than 350 units apart, so one mez
  cannot catch two healers. On "incoming" the group scatters.
- **After a fight.** The 2003 advice: "The place you just fought will be the first place the enemy come looking."
  Good groups leave and rest elsewhere. Casual groups rest where they fought.
- **Chasing.** "Do not chase enemy to Alb TK... you WILL die." Bold bots chase too far anyway.
- **Retreats are ragged.** Members react 0.3 to 2 seconds apart, and some never run.

Most of these mistakes were mocked by players at the time. The delays and shares are our design values. No source
measured how fast a real group switched target.

### 4. Feet: lanes, circuits and check spots

**What the survey found (tests only).** A probe walked 165 trips in the server's own route planner and found 220
alternate routes. Three of its four trip kinds:

| Trip kind | Trips | No alternate | One | Two |
|---|---|---|---|---|
| Portal keep to keep, whole trip | 54 | 31 (57%) | 16 | 7 |
| The same trips, measured from the milegate | 54 | 2 (4%) | 19 | 33 (61%) |
| Home entry to keep | 21 | 0 | 2 | 19 (90%) |

Each enemy portal keep sits in a closed valley whose only exit is one milegate about 200 units wide. So the shape
is a **trunk, then a fan**: one shared road from the portal keep to its gate, then up to three lanes from the gate.

- **Marching.** The groups of a force split 50 / 25 / 25 across the main lane and two alternates. The march leader
  stays the pace-setter, and groups on the other lanes keep level with him. Where only one alternate is proven the
  force uses two lanes. Where none is, everyone takes today's route.
- **Left out.** Alternates with a long swim (40 of the 220 swim over 1,000 units), a lane that passes within 4,000
  units of a keep the enemy holds, and any lane that fails its check at server start.
- **What lanes will not do.** In the Pennine hot spot a second lane "splits the column; it does not take it out of
  the fight". Expect the hot spot to spread, not vanish.
- **Monsters.** 100 of the 220 alternates pass a level 50+ aggressive monster within 1,500 units. The existing
  hazard check can reject a lane.

**Roaming eights.** The second survey produced 45 check spots, 15 per frontier, and 18 patrol circuits, three for
each enemy realm in each frontier. Every leg was proven by probe (tests only).

| Spot type | Examples |
|---|---|
| Chokepoint | The five milegates; hasteners on the open road |
| Levelling camp | Fellwoods and angry bwca in Pennine; grogans and "Grannies" in Emain |
| Dungeon door | Hall of the Corrupt, Dodens Gruva, Marfach Caverns |
| Named monster | Draco Magnificens, Glacier Giant, Evern |

- A group walks its circuit in order and checks each spot for 30 to 60 seconds.
- Circuits are weighted toward where enemy levelling bots are at that moment, by live count. Today their deaths
  cluster in Pennine, near Bledmeer in Odin's Gate, and in Emain.
- Deep spots are reached through the circuit, because direct routes pass close to keeps.
- Forest Sauvage, Snowdonia, Mount Collory and Cruachan Gorge showed nobody to hunt. Their camps are in reserve.

**Soloers and stealthers.** In 2003 visible soloing was close to impossible ("invisible or join a zerg"), and
stealthers camped the milegates in packs. Bot stealthers get an opening attack. A soloer opens only when the odds
allow it.

## Realm playbooks

| Realm | The 2003 eight | How its enemies killed it |
|---|---|---|
| Albion | 2 Clerics, Sorcerer, Theurgist, Paladin, Minstrel, 2 Mercenaries (retrospective). Opens with Speed of Sound (era-dated) | No Albion order was found; the general order applies |
| Midgard | 2 Healers, Shaman, Runemaster, Skald or Savage, Savage, Berserker, Warrior (retrospective) | "mezz the healers, kill the shaman and then kill the tanks" (era-dated) |
| Hibernia | 2 Druids, Bard, Warden, 2 point-blank area casters, 2 tanks (era-dated) | The Bard first, then the Eldritch, then the Druids (retrospective) |

- **Filling a group.** Every eight has the same skeleton: two main healers, one main crowd controller, one shield
  tank, a speed class, two who can cure mez, plus a Shaman (Midgard) or Theurgist (Albion). The skeleton is from a
  retrospective guide.
- **Short pool.** If a role is missing, the next best fit takes the slot and the group still forms.
- **Pets.** The existing pet cap stays: 2 pet classes in a group of five or more, 1 in a smaller one.

## Who is out there

For RvR bots that are not in a siege:

| Kind | Share | What they do |
|---|---|---|
| Warband roamers | 55% | Several groups moving together on the main roads |
| Eight-man groups | 30% | Patrol circuits and check spots |
| Soloers, duos and stealthers | 15% | Chokepoints and camps |

**This split is an assumption.** The research found no source for the 2003 proportions. The owner can change it
with a setting. Siege parties still form first.

## Skill tiers

Each group gets a tier when it forms.

| Tier | Share of groups | Delay to follow a call | Members who ignore the call |
|---|---|---|---|
| Casual | 30% | 1-4 s | 40% |
| Average | 50% | 0.6-2.5 s | 25% |
| Veteran | 20% | 0.3-1.5 s | 10% |

The existing difficulty setting `rvr_bot_awareness` (easy, normal, veteran) shifts this mix. Bold personalities
tunnel and over-extend more.

## Safeguards

- **A gank group moves on.** No corpse camping. After a kill at a check spot the group leaves within 3 minutes. The
  3 minutes is an assumption.
- **Levelling in the frontier stays.** Levelling bots are hunted, but who levels in a frontier, and where, is
  unchanged.
- **Siege balance is watched.** The target is about 50% attacker wins, in a band of 35-65%. Two changes can move it:
  marching columns now have to notice their enemies, and three lanes change march losses and arrival times.
- **Raids and battlegrounds are fenced off.** One scope test guards every new rule. No call may carry a raid duty.
- **One switch to know.** Turning `bot_veteran_retreat` off also turns RvR retreat off.

## Work packages

| WP | What it delivers | Settings (all default on) | Log tags |
|---|---|---|---|
| 0 Foundation | The scope test, the shared plan, skill tiers, empty hook points | `rvr_bot_field_tactics` | `RVR_FIGHT_BOARD` |
| 1 Eyes | Bots notice bots by the player rule; lookers | `rvr_bot_perception_bots`, `rvr_bot_group_lookers` | `RVR_NOTICE kind=EnemyBot`, `RVR_SCAN_BUDGET` |
| 2 Assist, control, interrupts | Called target, kill order, control plan, interrupt duty | `rvr_bot_assist`, `rvr_bot_cc_plan`, `rvr_bot_interrupts` | `RVR_ASSIST_CALL`, `RVR_ASSIST_SWITCH`, `RVR_CC_ASSIGN`, `RVR_INTERRUPT` |
| 3 Protect, positioning | Peeling for healers; backline, spread, kiting | `rvr_bot_peel`, `rvr_bot_positioning` | `RVR_PEEL`, `RVR_BACKLINE`, `RVR_KITE` |
| 4 Stances | Size-up, watching, retreat, regroup | `rvr_bot_size_up`, `rvr_bot_disengage`, `rvr_bot_regroup` | `RVR_SIZE_UP`, `RVR_WATCH`, `RVR_RETREAT`, `RVR_REGROUP` |
| 5 Lanes | Lane catalog, a lane per group, fallback | `rvr_march_lanes`, `rvr_roam_lanes` | `RVR_LANE_LOADED`, `RVR_LANE_ASSIGN`, `RVR_LANE_FALLBACK` |
| 6 Roamers, stealth | Role-built eights, circuits, check spots, openers | `rvr_roaming_parties`, `rvr_roam_checks`, `rvr_bot_stealth_openers` | `RVR_PARTY_FORMED`, `RVR_ROAM_CHECK`, `RVR_STEALTH_OPEN` |

- **Order.** WP0 lands first. Then WP1, WP5 and WP6 run in parallel with WP2, WP3 and WP4, which need WP0's shared
  plan.
- **Missing from the builders' plan.** The population split, the move-on rule and stealth detection have no setting
  or log tag yet. WP6 must add them, or principle 7 fails.
- **Data that ships.** A lane catalog (the draft holds 165 main routes and 220 alternates) and a check-spot and
  circuit catalog (45 spots, 18 circuits). The check-spot catalog is built from a clean release world, never from a
  played database. The server re-walks every lane at start and turns off any that fail.
- **Gaps to close first.**
  - The meshes for Hibernia's border keeps (Druim Ligen, Druim Cain) and Vindsaul Faste were missing from the survey.
  - Lanes were walked one way only.
  - Keep-to-keep hops cover each keep's two nearest neighbours, not every pair.
  - From Albion's portal keep in Emain, 16 of 30 direct probes failed.

## How we'll know it worked

The same kind of session on the owner's PC, before and after, from the server log. Until then it is all tests only.

| Measure | Today | Target |
|---|---|---|
| Time for a bot to notice an enemy bot | Instant at 5,000 units | The perception table (normal: median 1.5 s at 1,500, 6 s at 2,500, 20 s at 3,400) |
| Spotter calls (`RVR_SPOT`) | Not seen | Seen, for bots and for you |
| Share of a group's damage on the called target | No call exists | Proposal: about 60% Casual, 75% Average, 90% Veteran |
| First kills that are healers, casters or crowd controllers | Not measured | Proposal: a clear majority; measure today's share first |
| Open-field deaths in the busiest small area | About 18% | Proposal: 12% or less |
| Distinct lanes used per march | 1 | Up to 3 from the gate, split 50 / 25 / 25 |
| Check-spot visits by roaming eights | 0 | Every spot on a circuit visited |
| Levelling bots killed by hunting eights | 0 by design | Above 0, and the group gone within 3 minutes |
| Fights watched, retreats and regroups logged | 0 | Present in fights with bad odds |
| Attacker siege win rate | About 50% is the target | Stays in the 35-65% band |
| AI time for the frontier scan | To be read before WP1 | Not higher than today |

## Owner decisions (2026-11-16)

The owner accepted all nine recommendations: "Great, let's build all that with your proposed recommendations".
The list below is kept as decided.


1. **Fights at a keep keep today's rules in 1.2.** Recommended: yes. It protects siege balance, and it is the main
   part of "all in 1.2" that this design defers.
2. **Population split 55 / 30 / 15** (warbands / eights / soloers and stealthers). Recommended: start there. It is
   an assumption.
3. **Skill mix 30 / 50 / 20** (Casual / Average / Veteran). Recommended: yes.
4. **Move-on rule of 3 minutes.** Recommended: yes.
5. **Minimal stealth detection for bots.** Recommended: yes, close range only, never better than a human could.
6. **Leave out lanes with a long swim.** Recommended: yes, for alternates. 17 of today's main routes also swim and
   stay as they are.
7. **Lookers and the per-group cap also apply when the enemy is you.** Recommended: accept, if the time-to-notice
   table still holds in tests. The cap on lookers moves from per enemy to per enemy group.
8. **Climb points.** A summary of the 1.65 patch notes mentions climb points and a new keep layout, which conflicts
   with treating them as later than 1.65. Recommended: read the notes word for word before any keep work.
9. **Unsourced numbers.** Speed 5 and 6 percentages, sprint length, the interrupt duration at 1.65, and whether
   style stuns share the one-minute immunity have no source. Recommended: keep the server's current values.

## What is not in this design

- Tactics for fights at a keep, and relic carriers.
- Battleground keep assaults.
- Raids, including the Darkness Falls raid.
- Companions, and any group a player leads or is in.
- New classes, or abilities later than 1.65.
- Rebuilding navigation meshes. Lanes use the meshes as they are.

## Sources

Era-dated: written 2002 to January 2004. Retrospective: written later about that time, mostly by players of
Uthgard, a 1.65 freeshard. FH is the FreddysHouse forum. The patch notes are a wiki summary, not a word-for-word
read.

Era-dated:

- [FH Jan 2003](https://forums.freddyshouse.com/threads/rvr-tips-guide.108888/): kill order, guard, resting,
  chasing, mocked mistakes.
- [FH Aug 2003 a](https://forums.freddyshouse.com/threads/what-is-a-good-tactic-to.140721/) and
  [b](https://forums.freddyshouse.com/threads/feds-rvr-tips.144566/): group make-ups, assist, scatter, kiting.
- [FH Nov 2003](https://forums.freddyshouse.com/threads/assist.160018/): the main assist method.
- [FH Jul 2003](https://forums.freddyshouse.com/threads/the-battle-of-benowyc.135669/) and
  [FH Nov 2003](https://forums.freddyshouse.com/threads/amg-tonight.157865/): milegate camping.
- [1.65 patch notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65) (wiki summary).

Retrospective:

- [Uthgard guide 2011](https://uthgard.net/forum/viewtopic.php?f=65&t=22425): the Albion eight, the role skeleton,
  the 350-unit spread.
- [Uthgard 2016](https://www.uthgard.net/forum/viewtopic.php?f=61&t=37247): a Midgard guild's 1.65 group.
- [Uthgard 2011](https://www.uthgard.net/forum/viewtopic.php?f=60&t=24492): kill order against Hibernia.
- [Old realm ability list](https://www.uthgard.net/forum/viewtopic.php?f=32&t=20197): Mastery of Concentration,
  Speed of Sound.
- [Uthgard 2009](https://www.uthgard.net/forum/viewtopic.php?f=32&t=12717) and
  [Uthgard 2011](https://uthgard.net/forum/viewtopic.php?f=32&t=22430): stealth groups, "invisible or join a zerg".

## Appendix: for the builders

The evidence is in four working notes of 2026-11-16 in [research/rvr-field-1.2](research/rvr-field-1.2/):
`ERA-RESEARCH.md`, `ARCHITECTURE.md`, `LANES-FINDINGS.md` and `CHECKSPOTS-FINDINGS.md`. The draft catalogs
(`rvr-lanes-draft.json`, `rvr-checkspots-draft.json`) came from a played world's static tables and are not in Git;
WP5 and WP6 rebuild them from a clean release world. The hook points, in short:

- **Scope.** One pure test, `RvrTacticsScope.Allows`, mirrors `AutonomousRvrPerception.MayAnswerCall`. Every hook
  checks it first. WP0 adds each hook line, calling stubs in `BotBrain.RvrTactics.cs`.
- **Eyes.** New kinds `EnemyBot` and `EnemyBotPet`; `RvrScanRoster` picks the lookers; `RvrPartyKey` keys memory and
  the cap.
- **Mind.** `RvrFightBoard` and `RvrFightPlan` copy the pattern of `GroupFightBoard`, not the board.
  `SelectDistributedRvrTarget` is not changed. Retreat reuses `BotGroupRetreat.Publish`.
- **Feet.** `RvrLaneCatalog` loads `navmesh/rvr-lanes.json`. Lanes are injected as `_keepTravelPoints`; `MarchLeader`
  stays. With no proven lane, `RvrKeepRoute.TryBuild` runs unchanged.
- **Roamers.** `RvrRosterTemplates`, `RvrRoamCircuit`, `RvrStealthOpener`. A release check replays every circuit leg
  with `UT_TripProbe`.
- **Tests to keep green.** `UT_SiegeBalanceNov06`, `UT_DutyLeaseRealmRaid`, `UT_DutyLeaseBattleground`,
  `UT_DutyLeasePlayerGroup`.
