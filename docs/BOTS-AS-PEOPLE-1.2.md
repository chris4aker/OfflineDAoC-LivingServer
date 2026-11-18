# Bots as people for 1.2: skill, knowledge, movement and milestones

Owner: Chris. Decided 2026-11-17 07:58: 1.2 moves about a week, to ship about **24 November**, and takes in four "bots as
people" items from 2.0 ([RELEASE-TRAIN.md](RELEASE-TRAIN.md), 2.0, "Bots as people"; owner 2026-11-07: "I like options
1-6"). Each item is a 2003 player habit, never random noise, and "more human" must not mean "worse".

| Item | In 1.2 |
|---|---|
| 2. Skill that varies and improves | Yes. Raid learning is a separate work package that needs the owner's yes (the raid freeze) |
| 4. Knowing only what a player would know, in PvE too | Yes |
| 5. Movement that doesn't look scripted | Yes, on top of the movement work already merged |
| 6. Personal milestones that get talked about | Yes, with two arcs changed (owner decisions 2 and 3) |
| 1. Bot players own alts; 3. Friends, regulars and grudges | No: they need the bot-player data model. Items 2, 4, 5 and 6 are keyed so they can move to it later (section "One key for later") |

**Status: decided 2026-11-17 08:22 (section "Owner decisions"); WP0-WP5 and WP2R merged on `integration/1.2`, WP6 (milestones) built, WP7 (integration) under way. Nothing here is seen in game.** This document follows
[RVR-FIELD-1.2.md](RVR-FIELD-1.2.md): the same principles, pure rule classes, skill tiers and proof rules.

## What changes for you

1. Bots differ in how well they play, and they get better the more they play. A Casual bot pulls an add now and then,
   lets a buff run out, runs out of power and reacts late. A Veteran rarely does.
2. Their mistakes fall mostly on strangers. A bot in your group plays at its best (a switch).
3. A bot that has never been somewhere does not know the way. It asks in /faction, tags along behind someone going
   there, or takes a wrong road for a minute or two. A bot knows the zones it levelled through.
4. Bots move like people: melee bots circle-strafe players in RvR, bot groups travel on /stick in a strung-out line,
   bots overshoot where they stop, sit down when they wait, crowd the bank and sit on the walls of keeps their realm
   holds.
5. Bots chase personal goals (the ding to 50, realm rank 5, a dragon trophy) and talk about them as they get close.
   The realm cheers when they get there, and the Camelot Herald lists it.
6. All of it is on by default. Companions, raids (unless the owner lifts the freeze for one package), battleground
   assaults and fights at a keep keep today's rules.

## What is true today (code survey of `integration/1.2` at `b5632aca0`, 2026-11-17)

| Area | Exists today | Gap for this design |
|---|---|---|
| Skill | `RvrGroupSkill`: one tier per RvR group (Casual / Average / Veteran 30/50/20, from the group id and the leader's personality; `rvr_bot_awareness` shifts the mix). `RaidDiscipline`: each raider's reaction delay and lapse from its personality, and the realm's familiarity with a boss from its raid record (wired for Xanxicar's stomp only). `BotIncomingFireReaction`: a 0.3-2.5 s delay scaled by role and the Risk trait | No skill per bot. Nothing improves with play outside raids. Pulls (`AutonomousGroupPullOrder` takes the mob with the fewest neighbours), buffs and power use are near optimal for every bot |
| Knowledge | `AutonomousBotPersonality` zone memory: 32 zones per bot, in memory, forgotten at restart, used only by Explorers' camp taste. The chat corpus already has "how do i get to {dest}" lines (`AutonomousChatFieldTalk.Travel`) | Every bot paths anywhere on the navigation mesh as if it knew the map. The "ask the way" lines are chat only and are not tied to what the bot knows |
| Movement | Merged in `b04d2350a` (feature/1.2-movement): melee circling and caster side steps against monsters, legs of at most 22 units checked on the mesh, re-facing past 30 degrees, a 0-20 unit short stop in melee, town idling (vault keeper 35%, crowd 30%, merchant 20%, trainer 15%). `270116f01`: bots sit while resting (emote 0xF0 / 0xF1, visual only). RvR field WP3 and WP4: backline, spread, kite | No strafing against players, no /stick line in travel, no overshoot, no sitting outside rest, nobody on keep walls. All tests only: the movement row of the 1.2 gate has no played record |
| Milestones | Server firsts (first to 50, first of each class, first to each realm rank), grats chains, the Herald (firsts, realm point leaders, Most Wanted), `REALM_RAID_LOOT` for raid loot, realm rank events for bots (`OnBotRealmLevel`) | No goal a bot works toward and talks about, and no milestone of an ordinary bot reaches the Herald unless it is a server first |

## The seven principles

The first five are RVR-FIELD-1.2's, applied to people instead of groups.

1. **Human-plausible, not optimal.** Every rule has a delay, a spread and a failure rate, set by the bot's skill tier.
2. **A 2003 habit, never noise.** Each behaviour names the habit it copies and its source, or says it is our design.
3. **Not worse.** A habit may cost time, never a character. Every mistake has a cap: on minutes lost, on deaths it may
   cause, and on how often it falls on the player.
4. **Scope guard.** Autonomous gamebots only. Companions, real players, realm raids, battleground keep assaults, fights
   at a keep, siege jobs, relic carriers and stable rides keep today's rules. One pure test, `BotPeopleScope`, guards
   every new rule, as `RvrTacticsScope` does for RvR.
5. **Provable.** Every behaviour has a log line the owner's PC log can show, and a before/after measure.
6. **One key for later.** Every record is keyed by a person key that is the bot character today and can become the bot
   player later, with the merge rules written and tested now.
7. **Cheap.** About 3,000 bots on a home PC. Decisions are made at events (a trip starts, a pull is chosen, a level is
   gained), never polled every tick. The stall gate holds: no game-loop stall of 2 s or more, at most five of 1 s or more
   in an hour.

## Item 2: skill that varies and improves

### The 2003 habit

- **Skill spread was visible and mocked.** Healers charging, area damage breaking mez, never turning the camera, half
  the group afk after a win; "If you die without saying something it is usually your fault"; average groups "box up,
  break mez and dump or hoard long timers" (ERA-RESEARCH section 9: FH Jan 2003, era-dated; Uthgard guide 2011,
  retrospective).
- **PvE mistakes the guides warned about:** "If pulling, be careful not to pull too many"; "Remain aware of nearby
  monsters that may join the fight"; "As a tank, you may end up pulling. Do it, but pay attention to mana bars"; "chants
  use Power. Be judicious about using them" ([Prima official guide, revised and expanded, (c) 2002-2004][prima],
  era-dated).
- **Standing in it:** "Stick close enough to the group to receive heals, but far enough away to avoid an Area Effect
  stun or mez" (Prima); "Never chase a grouped Hib into the PBAoE" (ERA-RESEARCH section 6, FH Aug 2003, era-dated).
- **Reaction times:** about 300 ms for an average gamer, 150-200 ms for an elite one, more with each extra choice
  (ERA-RESEARCH section 9, WPI 2020, a general study, not DAoC).
- **Raids learned bosses:** the owner's "secrets had to be learned" (2026-11-07), already the idea behind
  `RaidDiscipline`.
- **Improving with play:** no source measured how fast a 2003 player improved. The curve below is our design.

### What the bot does

- Each autonomous gamebot gets a **skill profile** when it is first seen: a tier and six facets. The tier uses
  RVR-FIELD-1.2's tiers and shares (`RvrSkillTier`, 30 / 50 / 20, shifted by `rvr_bot_awareness`). It does not add a
  second system: the facets spread a bot inside its tier's band.
- The six facets, each 0 to 1 inside the tier's band:

| Facet | What it changes | Casual | Average | Veteran |
|---|---|---|---|---|
| Reactions | The delay before reacting to being hit or to a call (`BotIncomingFireReaction`, RvR call-follow) | x1.3 | x1.0 | x0.75 |
| Positioning | How often the bot ends up badly placed: in the monster's face, bunched with another healer in RvR | 12% of repositions | 6% | 2% |
| Pulls | The chance a pull takes a mob with a neighbour inside its aggro range instead of the cleanest one | 10% of pulls | 5% | 1.5% |
| Standing in fire | The delay before stepping out of a ground or point-blank area effect it is taking damage from (RvR field, and raids only with WP2R) | 2-5 s | 1-3 s | 0.5-1.5 s |
| Buffs | The share of buff expiries not renewed at once, and for how long | 25%, 20-90 s | 12%, 10-45 s | 4%, 5-20 s |
| Power use | The chance a caster picks its most expensive useful spell instead of the efficient one, and the power floor it rests at | 30%, rests at 25% | 15%, 35% | 5%, 45% |

  All the values are design values to tune in tests, except the tier shares (RVR-FIELD-1.2, owner-approved
  2026-11-16).
- **Getting better.** Each facet has experience: fights won, pulls made, buffs cast, deaths learned from. A facet moves
  toward the next tier's band by `1 - e^(-experience / scale)`, the curve `RaidDiscipline.Familiarity` already uses. A bot
  can move up **one tier at most** in 1.2 (owner decision 6), about 40 hours of play from the bottom of Casual to
  Average. Skill never falls with play; lowering `rvr_bot_awareness` lowers the start tier and so the cap (owner
  2026-11-17 12:52: appropriate, it is the difficulty knob).
- **Mistakes fall on strangers.** In a group with a real player, a bot's mistake chances are a quarter of its own
  (`bot_skill_spare_player_group`, on). Its delays are not shortened: it still looks like a person, only a careful
  one. The player's companions are not gamebots and never get this system.
- **A mistake is never a death sentence.** An extra pull only takes a mob of the same con the group already fights, never
  a named or a mob more than 2 levels above the group's top. A buff lapse never touches a buff the bot is about to
  fight without at a boss. A Casual caster runs dry, then rests longer; it does not keep casting into a wipe.
- **RvR groups.** `RvrGroupSkill` stays the group's tier in 1.2 (owner decision 5). A member's own reactions facet only
  spreads its delay inside the group tier's band. The RvR field's measures stay readable.

### Raid learning (WP2R, needs the owner's yes)

- **Today:** a raid's familiarity with a boss comes from the realm's record (`RaidDiscipline.FamiliarityOf`: attempts
  and wins). A fresh roster that never saw the boss knows it as well as the veterans did.
- **Proposed:** each raider remembers the mechanics it has seen (`offline_bot_mechanics_seen`). The raid's familiarity
  is the record's familiarity scaled by how much of the present roster has seen the mechanic: full from 60% of the
  raiders, falling to a quarter of it with nobody. Each raider's own reaction and lapse use its own count, so a newcomer
  lapses more than a veteran beside him.
- **Upgrade seeding.** A played world has a record but no "seen" rows, so familiarity would drop at the first start.
  Once, on first start with the switch, each level 50 bot of a realm with a counted attempt is marked as having seen
  that mechanic with a chance equal to the record's familiarity (a fixed roll by bot id). Familiarity stays the same
  on average.
- **Epic Special Forces** count as having seen everything (they are the realm's veterans).
- **Where it lands:** only `RaidDiscipline` (a pure function and its tests) and its one call site in
  `EpicEncounterTactics.RaidCalls.cs` (Xanxicar's stomp). It re-arms a re-run of Crystal Cave (Xanxicar), at the game's own
  raid size, twice: once unlearned, once learned.

### Data

| Table | Key | Columns | Size |
|---|---|---|---|
| `offline_bot_skill` | `PersonKey` | `CharacterId`, `Realm`, `Tier`, six facet experiences, `UpdatedUtc` | One row per bot |
| `offline_bot_mechanics_seen` (WP2R) | `PersonKey`, `Mechanic` | `CharacterId`, `Seen`, `LastSeenUtc` | One row per raider and mechanic |

The base facet values are fixed from the bot id (no save). Only experience is saved, batched off the game loop every
5 minutes and at logout, as `AutonomousLivingServerStore` does.

### Rules (pure classes, NUnit)

- `BotSkillProfile.For(personKey, personality, awareness, experience)`: tier, facets.
- `BotSkillGrowth.Step(profile, event)`: the new experience; the tier cap.
- `BotSkillMistakes.Chance(facet, profile, inPlayerGroup, spareSwitch)` and `Delay(...)`: every rule's delay, spread
  and failure rate by tier.
- `BotPeopleScope.Refusal(facts)`: the scope test, built like `RvrTacticsScope.Refusal`.
- WP2R: `RaidDiscipline.RosterFamiliarity(recordFamiliarity, seenShare)` and the seeding roll.

## Item 4: knowing only what a player would know, in PvE too

### The 2003 habit

- **Players asked each other.** "Other players are absolutely the best source of information you can find. Don't
  hesitate to ask" ([Camelot Herald beginner's guide][herald-help], official, undated).
- **They kept track or got lost.** "Always try to keep track of your location when in a group" (Prima, era-dated).
  "The compass might be slightly more reliable than me" ([Tom's Hardware, June 2004][toms], just after the era).
- **Routes were learned by habit.** Hibernia's run: buff at Druim Ligen, race to Crim or Bolg, meet a second group
  (ERA-RESEARCH section 6, FH Jan 2003, era-dated).
- **Horses were the known way.** "in DAoC you can always hire a horse to take you to another location" (Prima). The
  stablemaster is public knowledge, so a bot never has to ask the way to a stable.
- **How often people got lost:** no source. The rates below are our design.

### What the bot does

- **It knows where it has been.** A place is a zone, a town or a dungeon from `data/chat/places.json`. A bot learns a
  place by being in it, or half learns it by being told the way.
- **It starts knowing what a player of its level would.** On first sight a bot knows its capital, its home town, its
  realm's zones whose usual monster level is below its own (it levelled through them), and from level 20 its realm's
  border keep. So new ground is what a player finds new: the next level band's zones, dungeons, deep frontier spots and
  Darkness Falls. A fresh world does not fill /faction with questions.
- **At a trip to an unknown place** (decided once, when the trip starts) the bot does one of these, by tier:

| Choice | Casual | Average | Veteran | What happens |
|---|---|---|---|---|
| Knows it anyway (a guide, a guildmate) | 40% | 55% | 75% | Today's route |
| Asks the way | 30% | 25% | 15% | A line in /faction or /say ("how do i get to lyonesse"); a bot of the realm that knows the place answers truthfully (the stable, the road, the zone to cross). The asker then half knows it |
| Tags along | 15% | 12% | 8% | If a realm bot within 2,500 units is going to the same zone and knows it, the bot follows it at 300-600 units, then leaves it at the zone line |
| Takes a wrong road | 15% | 8% | 2% | Below |

- **A wrong road** is real road, never open ground: at a junction of the road network (`AutonomousZoneRoads`) the bot
  takes another branch for 600-1,500 units, stops, looks around (body language), and paths on from there.
  - It costs at most 3 minutes a trip (owner decision 7) and happens at most once a trip.
  - Never on a stable ride, in a group led by someone who knows the way, in an enemy frontier, inside a dungeon, or
    where the route threat check (`AutonomousRouteThreatPolicy`) sees an aggressive monster more than 2 levels above
    the bot within 1,500 units of the wrong leg.
  - A half-known place cuts the chance in half.
- **Groups** take the way of their leader. If no member knows, the leader asks.
- **Camps.** In 1.2 a bot still weighs a camp by its live spawns, as today (owner decision 9). Gating that by what the
  bot has seen would change levelling speed. It goes to 2.0.
- **It replaces the Explorers' in-memory zone list,** which then reads the saved places.

### Data

| Table | Key | Columns | Size |
|---|---|---|---|
| `offline_bot_places` | `PersonKey`, `Realm` | `CharacterId`, a bit set of known places, a bit set of half-known places, `UpdatedUtc` | One row per bot and realm; about 100 bytes |

### Rules (pure classes, NUnit)

- `BotPlaceKnowledge.Seed(level, realm, places)`, `Learn(...)`, `Knows(...)`.
- `BotWayFinding.Choose(knowledge, tier, situation)`: knows / ask / follow / wrong road, with the exclusions.
- `BotWayFinding.WrongLeg(roadGraph, junction, roll)`: the branch and its length, or none.
- `BotWayFinding.Answer(asker, answerers)`: who answers and what they say (only bots that know the place).

## Item 5: movement that doesn't look scripted

### The 2003 habit

- **Circle-strafe and face:** never turn your back on a Berserker or Savage; circle-strafe and /face so positional
  styles fail. Instant mez and stun need facing (ERA-RESEARCH sections 3 and 5, FH Jan and Aug 2003, era-dated).
- **/stick:** "if he says stick, STICK"; some stuck "the person above you in the mini group window" so the group ran
  strung out; "unstick when resting and keep spinning around" (ERA-RESEARCH section 6, FH Jan and Aug 2003, era-dated).
  "/Stick is a very useful command... /Follow is usually a good way to stay with your buddies during regular travel"
  (Prima). This dates /stick to 2003, which PERIOD-CHAT-RESEARCH left open.
- **Sitting:** "you may find yourself sitting a lot to regain HP" (Prima).
- **Parking:** buffbots parked afk in the portal keep or a town ([research/BUFFBOTS-2003.md](research/BUFFBOTS-2003.md)).
- **Walls:** the leader calls stick and the group "runs around a bit, climbing up wall" (ERA-RESEARCH section 9, FH Jan
  2003, era-dated).
- **Overshooting:** no source. It is a plain fact of keyboard movement and our design.

### What the bot does

Everything walks legs of at most 22 units checked on the mesh first (`AutonomousNavigationSurface.MoveAlongDryGround`),
the primitive the merged movement work uses. A leg shorter than the 25-unit turn threshold keeps the bot facing its
target, which is how a strafe looks on screen. No navigation mesh changes.

| Habit | Who and where | What it does | By tier |
|---|---|---|---|
| Circle-strafe | Melee gamebots in an open-field RvR fight against a player or a bot (`RvrTacticsScope`) | Chained side legs around the target every 3-8 s, keeping inside its own positional window and out of the target's front | Casual 20% of fights, Average 50%, Veteran 85% |
| Face the enemy | Casters and healers in RvR under melee | /face and back off in an arc instead of turning away ("run a bit, throw heal") | Casual 30%, Average 60%, Veteran 90% |
| /stick line | Bot-only groups travelling in the open world (not stable rides, raids, battleground waves, sieges) | Each member follows the one above it at 80-200 units, so the group runs as a line. 1 in 50 legs a member "loses stick" for 3-10 s and catches up | The same for all tiers |
| Overshoot | Arriving on foot at a merchant, trainer, vault keeper, rendezvous or camp spot | Runs 15-60 units past and steps back | Casual 25%, Average 15%, Veteran 8% of arrivals |
| Sit while waiting | Waiting more than 30 s for the group, at a rendezvous, or idling at the bank | Sits (emote 0xF0), stands at once to move, cast or fight | The same for all |
| Crowd the bank | Town idling (already merged) | The crowd spot leans to the vault keeper when 4 or more bots are there, so the bank looks like a bank | The same for all |
| Sit on keep walls | Bots waiting at a keep their realm holds, with no enemy noticed and no siege at it: between roams, at a muster, after a release | Walks to a guard archer's floor on the wall (the positions `AutonomousRvrDefense.Position` already proves) and sits. Stands within 0.3-1 s of noticing an enemy | The same for all |

- **Never** in a raid, a battleground wave, a fight at a keep (committed defenders and siege fighters), on a stable ride,
  in a group with a player, or while casting, stealthed or holding aggro. The merged movement work's own exclusions stay.
- **Sitting is visual only,** as merged: resting and its timing do not change.
- **The original client.** The sit emote has been checked in the new client only. If the original client does not
  show it on an NPC, the sitting habits are invisible there (a risk, checked first in WP5).

### Rules (pure classes, NUnit)

- `BotMovementHabits.Strafe(...)`, `Face(...)`, `Overshoot(...)`, `SitWhileWaiting(...)`: when, how far, by tier.
- `BotStickLine.Order(members)` and `LoseStick(roll)`.
- `BotKeepWallSpots.Choose(keep, guards, roll)`: a wall floor, or none.

## Item 6: personal milestones that get talked about

### The 2003 habit

- **Epic armour was a level 50 quest.** The class's level 50 epic ended in a named fight at a fixed place, then the
  trainer handed over the armour ([Allakhazam quest database, Albion "Symbol of the Broken (level 50)"][epic50], with
  player comments of March-May 2003).
- **Artifacts are Trials of Atlantis** (launched 28 October 2003, after 1.65). Era scope keeps Trials of Atlantis to
  rumours ([PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), owner decision 19 of 2026-11-15).
- **Realm rank 5 abilities are post-1.65** (ERA-RESEARCH: "Do not use from them: RR5 abilities"). Realm rank 5 itself is
  a title the server already counts.
- **Grats and cheering** at 50 are already built (Grats, Server firsts).
- **Talking about the next goal:** no period source quantifies it. The pacing is our design, inside the chat budgets.

### What the bot does

- **One arc at a time per bot**, chosen when it fits and kept until it is reached or no longer possible:

| Arc | Who takes it | Progress from | Reached when |
|---|---|---|---|
| The ding to 50 | Level 45 and up | Its level and experience | Level 50 |
| Realm rank 5 | Level 50 with an RvR leaning (personality goals) | `RealmPoints` and the server's rank table | Realm rank 5 (`OnBotRealmLevel`) |
| A dragon trophy | Level 50 with a raid leaning | Raids joined against its realm's dragon (`RealmRaidRecord`) | An item it won in a dragon or epic-boss raid (`REALM_RAID_LOOT`) |
| Epic armour | Owner decision 2: deferred to 2.0 | | |

- **Talk.** At about 50% and 90% of the way, and at the moment itself, the bot may say so where a real player can hear:
  /faction, its guild, its group, /say near you ("2 bubbles to 50", "halfway to rr5", "finally got a drop off
  cuuldurach"). Lines come from a new corpus file, `milestones.lines`, in each bot's writing style. They pass the
  existing chat budgets and claim only what the bot's real numbers say.
- **The moment.** Reaching it raises the existing grats chain, and from 1 to 3 realm-mates react in /faction. Server
  firsts stay as they are. A bot with a reached arc may mention it later ("since i dinged 50").
- **The Herald.** A new section, "Milestones of the Realms", lists the last 40 reached arcs: name, class, realm, the arc
  and when. Enemy names are shown as in Server Firsts.
- **A small push.** A bot near the end of an arc leans its next goal toward it: within 2 levels of 50 it takes fewer town
  breaks; chasing realm rank 5 it leans to RvR. The tilt goes through `AutonomousBotPersonality.PlanGoals`, so the
  realm's goal mix stays exact (owner decision 10).

### Data

| Table | Key | Columns |
|---|---|---|
| `offline_bot_milestones` | `PersonKey`, `CharacterId`, `Arc` | `Realm`, `Name`, `ClassName`, `State` (active, reached, dropped), `StartedUtc`, `ReachedUtc`, `LastTalkUtc`; WP6 added `TalkPoint` (the highest talk point handled), `Count` (big raid kills without a drop), `Trophy` and `Source` (the item won and its boss) |

### Rules (pure classes, NUnit)

- `BotMilestones.Choose(level, personality, state)`, `Progress(...)`, `Reached(...)`.
- `BotMilestoneTalk.Due(progress, lastTalk, budget)`: when a line may be said.
- The launcher's `HeraldPage` gets the new section and its test.

## One key for later

Items 1 and 3 need a bot player who owns characters. To make that a migration, not a rewrite:

- **`BotPersonKey`** is `(Kind, Id)`, written as text (`c:1234` for a character, later `p:56` for a bot player). Every
  table above has `PersonKey` and `CharacterId`. Today they are the same bot.
- **What belongs to the person, and what to the character:**

| Record | Belongs to | Merge when characters join one bot player |
|---|---|---|
| Skill experience and tier | Person (a player's skill carries to an alt) | Highest experience per facet; the highest tier |
| Places known | Person, per realm (an alt in the same realm knows the map) | The union, per realm |
| Mechanics seen (WP2R) | Person | The sum of `Seen` |
| Milestone arcs | Character (a level is a character's); the talk is the person's ("on my alt") | Kept per character, re-keyed to the person |

- `BotPersonKey.Merge(...)` is written and tested in WP0, so the later model only calls it.

## Data and the release

- Four new tables: `offline_bot_skill`, `offline_bot_places`, `offline_bot_milestones` and, with WP2R,
  `offline_bot_mechanics_seen`. They are saved progress and name the developer's bots.
- They go into `OptionalProgressTables` in `source/tools/OfflineDaoc.ProgressImport/progress-policy.json`, so
  `build_release_037.py`'s `clean_world` empties them and the progress transfer carries them.
- The server declares them in one list, `BotPeopleTables.All`, and the release builder checks that the policy covers it,
  as it does for `EconomyTables.All` (`check_policy_covers`). A table missing from the policy stops the build.
- Writes are batched off the game loop under the bot status lock, every 5 minutes and at logout.

## Settings (all default on)

Every new key is category `bots`, defaults to True in the server code, in the launcher's
`SocialExpansionSettings.Defaults` and `Recommended`, and in `source/tools/release-server-settings.json`. These are new
keys, so a world from an earlier build has no row and gets the code default: no `LivingServerSettingsUpgrade` line is
needed. If any default changes before shipping, it gets a line then. `UT_LivingServerDefaultsOn`,
`SocialExpansionSettingsTests` and `test_release_server_settings.py` list each key, and LIVING-SERVER.md lists every
`bot_living_*` key (its test checks that).

| Key | What it turns on |
|---|---|
| `bot_skill_profiles` | Item 2's master switch: profiles, facets, mistakes |
| `bot_skill_growth` | Skill improves with play |
| `bot_skill_spare_player_group` | Mistakes at a quarter of the rate in a group with a real player |
| `raid_learns_from_raiders` | WP2R only, if approved: raid familiarity from the raiders who have seen the mechanic |
| `bot_place_knowledge` | Item 4's master switch: places known, the seeding |
| `bot_ask_the_way` | Asking and answering in chat, and tagging along |
| `bot_wrong_roads` | Taking a wrong road |
| `bot_movement_habits` | Item 5's master switch |
| `bot_move_strafe` | Circle-strafing and facing in RvR (also needs `rvr_bot_field_tactics`) |
| `bot_move_stick_line` | Groups travel on /stick |
| `bot_move_sit_and_walls` | Sitting while waiting, and on keep walls |
| `bot_living_milestones` | Item 6: arcs, talk, the Herald section, the goal tilt |

The launcher's Living Server tab gets one card, **BOTS AS PEOPLE**, with the four master switches. ALL OFF turns them
off with the rest.

## Performance

- **Where the time goes:** new `AiSystem` scopes for AI_PROFILE: `BotSkill`, `PlaceKnowledge`, `WayFinding`,
  `MovementHabits`, `Milestones`.
- **Skill:** a profile is a cached struct per bot; each mistake is one roll at a decision the bot already makes. No new
  scan.
- **Places:** a bit test per trip start. Tagging along uses a realm index of bots by destination zone, updated at trip
  start, with at most 4 searches a second server-wide (the town hangout budget).
- **Movement:** at most 40 mesh leg checks a second server-wide for the new habits, dropped first when
  `AutonomousAiBudget` says the loop is busy. The stick line replaces the formation target; it adds no path query.
- **Milestones:** events only (level, realm rank, raid result, loot). The Herald reads the database outside the server.
- **Saves:** at most 3,000 rows each 5 minutes, about 10 a second, in the existing batch.
- **The gate:** the 8-hour overnight row on the final candidate, with every switch on: no stall of 2 s or more, at most
  5 of 1 s or more in any hour.

## Log tags

Each tag has a 5-minute summary line, so the log stays small, and one line per event worth seeing.

| WP | Per event | Summary every 5 minutes |
|---|---|---|
| 1 | `BOT_SKILL_PROFILE` (at login: tier, facets), `BOT_SKILL_GROWTH` (a tier change) | `BOT_SKILL_BUDGET` (bots per tier per realm) |
| 2 | `BOT_SKILL_MISTAKE kind=pull\|fire\|buff\|power\|reaction\|position spared=true\|false` | `BOT_SKILL_MISTAKES` (by kind and tier, in and out of player groups) |
| 2R | `RAID_LEARN raid= mechanic= record= seenShare= familiarity=`, `RAID_MECHANIC_SEEN` | |
| 3 | `BOT_PLACE_LEARNED how=visited\|told\|seeded` | `BOT_PLACE_BUDGET` |
| 4 | `BOT_WAY_ASK`, `BOT_WAY_TOLD`, `BOT_WAY_FOLLOW`, `BOT_WAY_WRONG_ROAD cost_s=` | `BOT_WAY_SUMMARY` (choices by tier, minutes lost, deaths within 60 s of a wrong road) |
| 5 | `BOT_MOVE_STRAFE`, `BOT_MOVE_STICK_LOST`, `BOT_MOVE_OVERSHOOT`, `BOT_MOVE_SIT`, `BOT_MOVE_WALL` | `BOT_MOVE_HABITS` (counts, refused legs) |
| 6 | `BOT_MILESTONE_ARC`, `BOT_MILESTONE_PROGRESS`, `BOT_MILESTONE_REACHED`, `BOT_MILESTONE_TALK` | `BOT_MILESTONE_SUMMARY` (arcs active, chosen, points, talks by channel, reached, trophies) |

GM test hooks, so a rare moment can be made due (`/lstest`, GM accounts): `/lstest skill` (the target's profile),
`/lstest lost` (the target's next unknown trip takes a wrong road), `/lstest ask` (the target asks the way now),
`/lstest milestone` (the target's arc reaches its next talk point), `/lstest wall` (bots at the nearest held keep take
the walls). `/lswhy` gains a `skill` and a `way` line for the target.

## Work packages

Sizes are agent-hours: one agent's working hours, including its unit tests and a CI run, not counting the played checks.

| WP | Delivers | Main files | Tests | Settings | Size |
|---|---|---|---|---|---|
| 0 Foundation | `BotPersonKey` and `Merge`, `BotPeopleScope`, `BotPeopleTables.All` and the store, the policy entries and the release check, the settings, the AI_PROFILE scopes, `/lstest` and `/lswhy` stubs, the launcher card | new `BotPeople*.cs` in `bots/autonomous`, `AiSystem.cs`, `ServerProperties.cs`, `progress-policy.json`, `build_release_037.py`, `release-server-settings.json`, `LivingServerTestCommand.cs`, `SocialExpansionSettingsControl.cs` | `UT_BotPeopleScope`, `UT_BotPersonKey`, `UT_LivingServerDefaultsOn`, the release policy test, `SocialExpansionSettingsTests` | all keys | 6 |
| 1 Skill profiles | Tier and facets, growth, saving | `BotSkillProfile.cs`, `BotSkillGrowth.cs`, `AutonomousLivingServerStore.cs` | `UT_BotSkillProfile` (shares 30/50/20 within 2 points over 10,000 ids, the awareness shift, growth curve, one-tier cap, the save round trip) | `bot_skill_profiles`, `bot_skill_growth` | 7 |
| 2 Skill in the hands | Mistakes for reactions, positioning, pulls, fire (RvR), buffs, power; sparing the player's group | `BotSkillMistakes.cs`; hooks in `BotIncomingFireReaction.cs`, `AutonomousGroupPullOrder.cs`, `BotBuffReservations.cs`, `BotSpellPower.cs`, `BotRestRecovery.cs`, `BotBrain.CombatMovement.cs`, `BotBrain.RvrPositioning.cs` | `UT_BotSkillMistakes` (rates by tier, the caps: no named, no mob over +2, the quarter rate in a player group), the raid scope test (no hook acts in a raid) | `bot_skill_spare_player_group` | 10 |
| 2R Raid learning (owner's yes) | Roster familiarity, seen memory, the upgrade seeding | `RaidDiscipline.cs`, `EpicEncounterTactics.RaidCalls.cs` (one call) | `UT_RaidDiscipline` extended: seen share, seeding keeps the average, Epic Special Forces | `raid_learns_from_raiders` | 5 |
| 3 Place knowledge | Places, seeding by level, learning, the Explorers' list moved over | `BotPlaceKnowledge.cs`, `AutonomousBotPersonality.cs` (zone memory), the controller's trip start | `UT_BotPlaceKnowledge` (a level 30 knows the 1-29 zones of its realm; dungeons unknown; save round trip) | `bot_place_knowledge` | 6 |
| 4 Way finding | Ask, answer, tag along, wrong road | `BotWayFinding.cs`; hooks in `AutonomousWorldBotController.cs` (issuing a path), `AutonomousZoneRoads.cs`, `AutonomousChatFieldTalk.cs` (`Travel`), `AutonomousRouteThreatPolicy.cs` | `UT_BotWayFinding` (choice shares, the 3-minute cap, every exclusion, answers only from bots that know) | `bot_ask_the_way`, `bot_wrong_roads` | 10 |
| 5 Movement habits | Strafe and face in RvR, the stick line, overshoot, sitting while waiting, keep walls, the bank crowd | `BotMovementHabits.cs`, `BotStickLine.cs`, `BotKeepWallSpots.cs`; hooks in `BotBrain.RvrPositioning.cs`, `AutonomousGroupFormationFollow.cs`, `AutonomousTownHangouts.cs`, `GameBot.cs` (sitting), `AutonomousRvrDefense.cs` (wall floors) | `UT_BotMovementHabits`, `UT_BotStickLine`, `UT_BotKeepWallSpots`, `UT_BotCombatMovement` and `UT_TownHangouts` still green | `bot_movement_habits`, `bot_move_strafe`, `bot_move_stick_line`, `bot_move_sit_and_walls` | 10 |
| 6 Milestones | Arcs, talk, the Herald section, the goal tilt | `BotMilestones.cs`, `data/chat/milestones.lines`, `AutonomousLivingServer.Events.cs`, `AutonomousBotPersonality.PlanGoals`, `HeraldPage.cs` | `UT_BotMilestones`, `UT_AutonomousBotVoices` (every slot filled), `HeraldPageTests`, `tools/bot-voices/build_corpus.py --verify` | `bot_living_milestones` | 9 |
| 7 Integration | One branch with everything, the test world at 1,000 bots a realm for the AI_PROFILE read, docs (LIVING-SERVER.md, release notes), the played-check rows | docs, `tools/perf` | the full suite, a 2-hour test-world soak (tests only) | | 5 |

**Total:** 63 agent-hours, 68 with WP2R. WP0 lands first. Then three lanes run side by side: skill (WP1, WP2, WP2R),
knowledge (WP3, WP4) and movement and milestones (WP5, WP6). WP7 merges them.

## Played checks (on the owner's PC)

The owner cannot play these days, so Claude drives the game by screen on the owner's PC, with the owner's permission
for each install and each server start (AGENTS.md: never start a server or copy builds over a running install without
asking). Claude plays a non-GM character for what a player would see and a GM character for `/lstest`, takes
screenshots, and reads the server log. A row counts only when it is in the PC log of the build being shipped.

| WP | What is shown | Proof in the log |
|---|---|---|
| 1 | `/lstest skill` on five bots shows different tiers; a bot that played overnight has more experience | `BOT_SKILL_PROFILE` shares within 5 points of 30/50/20 across the session; `BOT_SKILL_GROWTH` after the overnight |
| 2 | A bot group pulls an add now and then out in the world; in a bot-led group with Claude's character, it rarely does | `BOT_SKILL_MISTAKES` with `spared=true` in the player's group at about a quarter of the strangers' rate; no death of a gamebot within 30 s of a `kind=pull` mistake more often than without one (compared over the session) |
| 2R | Crystal Cave run twice at the game's raid size | `RAID_LEARN` with `seenShare` rising between the runs; both runs end in a win or a loss the log review calls correct, never a stall |
| 3 | `/lswhy way` on a level 35 bot names the zones it knows | `BOT_PLACE_LEARNED how=seeded` at the first start, `how=visited` later |
| 4 | An "how do i get to ..." in /faction on screen, answered by another bot; a bot standing at a wrong junction, then going on | `BOT_WAY_ASK` and `BOT_WAY_TOLD` pairs; `BOT_WAY_WRONG_ROAD` with `cost_s` 180 or less; no rise in `AUTONOMOUS_STUCK_15M_RECOVERY` and other stuck-watchdog recoveries against the session before |
| 5 | Screenshots: a bot group running in a line; bots sitting on a held keep's wall, standing when Claude's enemy character comes near; a melee bot circling in an RvR fight; bots crowding the vault keeper | `BOT_MOVE_HABITS` counts; `BOT_MOVE_WALL` and `BOT_MOVE_SIT`; the sit emote seen in the original client (the first check of WP5) |
| 6 | A bot's "halfway to 50" in /faction; a reached arc in the Herald's new section | `BOT_MILESTONE_TALK`, `BOT_MILESTONE_REACHED`; the Herald page opened from the launcher |
| All | The overnight row with every switch on | No `SERVER_STALL` of 2 s or more; at most 5 of 1 s or more in any hour; AI_PROFILE time for the new scopes in the report |

## How we'll know it worked

The same kind of session on the owner's PC, before and after, from the server log. Until then it is all tests only.

| Measure | Today | Target |
|---|---|---|
| Bots per skill tier | One tier per RvR group, none per bot | 30 / 50 / 20 within 5 points |
| Mistakes in the player's group against strangers | No mistakes exist | About a quarter of the strangers' rate |
| Levelling speed (experience per bot-hour, levels 20-49) | To be read from the "before" session | No more than 5% below it ("not worse") |
| Minutes lost to wrong roads | 0 | Under 3 a trip, a few a bot-day |
| Deaths within 60 s of a wrong road | None (no wrong roads) | No more than on the bot's other trips |
| "Ask the way" lines that are true | Chat only, never tied to knowledge | Every `BOT_WAY_ASK` from a bot that did not know the place, every answer from one that did |
| Bots seen sitting outside rest | 0 | Present at waits, at the bank and on held keep walls |
| Arcs reached and told | 0 | Present, and each claim matches the bot's numbers |
| Raid results (the bar) | Met on the candidate line (PLAYED-CHECK-1.2.md) | Still met on the final candidate |
| Stalls | The gate's threshold | Unchanged |

## Risks

| Risk | What could go wrong | Answer |
|---|---|---|
| Worse, not human | Extra pulls and wrong roads slow levelling, or kill bots | Caps in every rule; the levelling-speed measure; the switches |
| Raid results move | Skill hooks run in shared combat code that raids also use | `BotPeopleScope` refuses raids, with a test per hook; the raid bar is re-run on the final candidate anyway |
| RvR balance moves | Per-bot reaction spread and strafing change open-field fights; marching columns travel as a line | The group tier stays; siege balance (35-65% attacker wins) is read in the check days |
| Chat flood | A fresh world with thousands of bots asking the way, or arcs talking | Seeding by level; asks and arc talk inside the existing chat budgets; a realm cap of one ask in /faction per 10 minutes |
| Stuck bots | Wrong roads and the stick line confuse the stuck watchdog | Wrong roads only on the road network; the watchdog's own reason for a planned stop; stuck-watchdog recoveries (`AUTONOMOUS_STUCK_15M_RECOVERY` and the others) compared before and after |
| Sitting not shown | The original client may not show emote 0xF0 on an NPC | Checked first in WP5; if it fails, the sitting habits ship for the new client only and the notes say so |
| Upgrade drop | WP2R would make played worlds forget their bosses | The seeding rule, tested to keep the average |
| Era | An arc or line names something after 1.65 (artifacts, RR5 abilities) | Owner decisions 2 and 3; the corpus era check |
| Save size and privacy | New tables name the developer's bots | Listed in the policy and checked empty by the release builder |
| Time | 63-68 agent-hours in 4 days | Three lanes; anything not green by the end of day 4 ships off (its switch False, with a reason in `KeptOff`) or waits for 2.0, the owner's call |

## Schedule: 4 days of build, 2 of checks, ship on 24 November

Times are America/New_York. Each night's overnight run is on the owner's test copy, started with his permission.

| Day | Build lanes | Checks and the raid gate |
|---|---|---|
| Tue 17 (afternoon) | Owner decisions; WP0 | The current gate's work goes on as planned (the morning install checks on `4be6fab`) |
| Wed 18 (build day 1) | WP0 merged; WP1, WP3, WP5 | CI only |
| Thu 19 (build day 2) | WP2, WP4, WP6 | CI; overnight: test-world soak of WP0, WP1, WP3 and WP5 (tests only) |
| Fri 20 (build day 3) | WP2, WP4, WP6 finished; WP2R if approved | First install of a test build on the owner's PC: Claude checks the sit emote in the original client, and WP1 and WP3 in a short session |
| Sat 21 (build day 4) | WP7: integration, docs, release notes; fixes | **Final candidate** built in the evening. Overnight 21-22: **the raid gate re-run**, three sweep lanes side by side (each realm's dragon and two dungeons for the bar; Crystal Cave twice with WP2R), with every switch on |
| Sun 22 (check day 1) | Fixes only, each listed | Read the sweep. Claude plays the WP2 to WP6 rows by screen. Overnight 22-23: **the 8-hour stall row** with every switch on, plus the open-field RvR rows and siege balance |
| Mon 23 (check day 2) | Fixes only; a fix that touches raid code re-arms the raids it touches, run that day | Read the overnight; the install checks on the final candidate's Setup (only if Setup, patcher, launcher or install files changed after `4be6fab`); release notes from what was seen |
| Tue 24 | | Merge to `main`; publish (the owner's step) |

**The raid gate re-run this forces.** These packages put new hooks in combat code that raids share, and the owner's
rule is that an earlier build's result never stands for the shipped build. So the raid bar (each realm's dragon and
two of its dungeons) is re-run on the final candidate on the night of 21 to 22, and the big three (Caer Sidi, Tuscaran
Glacier, Galladoria) are run if the lanes have time, as PLAYED-CHECK-1.2.md already asks. With WP2R, Crystal Cave
(Xanxicar) is added twice. A raid that loses because of a bot, route or tactic bug blocks the release until it is fixed
and run again (owner, 2026-11-15).

## Owner decisions (2026-11-17)

The owner answered at 08:22 on 2026-11-17: "yes to your recommendations". Decisions 5, 8 and 10 were not asked one by
one; they are built as recommended. Decisions 12 and 13 are still open.

| # | Decision | Answer (2026-11-17 08:22) |
|---|---|---|
| 1 | WP2R, raid learning from raiders, lifts the raid freeze for one function | **Yes, narrowly**: only `RaidDiscipline`'s familiarity input and its one call site, behind `raid_learns_from_raiders`, with the upgrade seeding and Crystal Cave run twice |
| 2 | Epic armour arc | **Deferred to 2.0** with the quest harness. In 1.2 no bot claims epic armour |
| 3 | The artifact arc | **Replaced by a dragon trophy**: an item the bot really won in a dragon or epic-boss raid |
| 4 | Mistakes in the player's group | **A quarter of the rate**, `bot_skill_spare_player_group` on |
| 5 | RvR groups keep their group tier in 1.2 | **Built as recommended** (not asked separately): a member's own facet only spreads its delay inside the band |
| 6 | Skill grows, never falls, one tier at most in 1.2 | **Yes**. `rvr_bot_awareness` shifts the per-bot mix too: it is the one difficulty knob (owner 2026-11-17 12:52: lowering it may drop a grown bot a tier, since the cap is today's start tier plus one; that is appropriate) |
| 7 | Getting lost | **Yes, the limits as written**: once a trip, 3 minutes, roads only, never in an enemy frontier, a dungeon or near monsters more than 2 levels above; one ask in /faction per realm every 10 minutes. Note, owner 2026-11-17 12:46: wrong roads may follow proven walking legs across open ground |
| 8 | What a bot knows at the start | **Built as recommended** (not asked separately): its capital, its home town, its realm's zones below its level and, from level 20, its border keep |
| 9 | Camp choice by what the bot has seen | **Not in 1.2** (2.0): it would change levelling speed |
| 10 | Arcs lean the next goal | **Built as recommended** (not asked separately), inside the allocator's exact mix |
| 11 | Keep walls | **Only at keeps the realm holds, with no siege there and no enemy noticed**. Committed defenders and siege fighters keep today's rules |
| 12 | The plan of 2026-11-17 06:47 (one final build at about 11:00, then a freeze) is replaced by this schedule | **Pending.** Recommended: confirm, and keep the rows already seen for the parts these packages do not touch (the lore, tales, install checks on an unchanged Setup), while the raid bar, open-field RvR, movement and the stall row are seen again on the final candidate. WP0 is built on this schedule meanwhile (pushed by 16:00 on 17 November) |
| 13 | A package not green by the end of build day 4 | **Pending.** Recommended: the owner chooses per package on Sat 21 (switch off, recorded in `KeptOff`, or 2.0), with this document's measures as the evidence |

The recommendations as first written (kept for the record):

1. **WP2R, raid learning from raiders, lifts the raid freeze for one function.** Recommended: **yes**, narrowly: only
   `RaidDiscipline`'s familiarity input and its one call site, behind `raid_learns_from_raiders`, with the upgrade
   seeding and Crystal Cave run twice. If no, item 2 ships without raid learning and the realm keeps learning from its
   record, as today.
2. **Epic armour arc.** Bots do not run quests, so no gamebot can earn epic armour today, and handing it over without
   the quest would break "gamebots start without armor and earn it". Recommended: **defer it to 2.0** with the quest
   harness. In 1.2 no bot claims epic armour.
3. **The artifact arc.** Artifacts are Trials of Atlantis (after 1.65; rumours only). Recommended: **replace it with a
   dragon trophy**: an item the bot really won in a dragon or epic-boss raid.
4. **Mistakes in the player's group at a quarter of the rate**, switch on. Recommended: **yes**, a quarter.
5. **RvR groups keep their group tier in 1.2;** a member's own facet only spreads its delay inside the band.
   Recommended: **yes**. Deriving the group tier from its members waits for 2.0, when the RvR field has a played record.
6. **Skill grows, never falls, and by one tier at most in 1.2** (about 40 hours from the bottom of Casual to Average).
   Recommended: **yes**. Also: `rvr_bot_awareness` shifts the per-bot mix too, so there is one difficulty knob.
7. **Getting lost:** at most once a trip and 3 minutes, only on roads, never in an enemy frontier, a dungeon or near
   monsters more than 2 levels above; at most one ask the way in /faction per realm every 10 minutes. Recommended:
   **yes**.
8. **What a bot knows at the start:** its capital, its home town, its realm's zones below its level and, from level 20,
   its border keep. Recommended: **yes**.
9. **Camp choice by what the bot has seen** (instead of live spawn counts). Recommended: **not in 1.2**; it changes
   levelling speed. 2.0.
10. **Arcs lean the next goal,** inside the allocator's exact mix. Recommended: **yes**.
11. **Keep walls only at keeps the realm holds, with no siege there and no enemy noticed.** Committed defenders and
    siege fighters keep today's rules. Recommended: **yes**.
12. **The plan of 2026-11-17 06:47** (one final build at about 11:00, then a freeze; ship tomorrow if the gate is not
    met) is replaced by this schedule. Recommended: **confirm**, and keep the rows already seen for the parts these
    packages do not touch (the lore, tales, install checks on an unchanged Setup), while the raid bar, open-field RvR,
    movement and the stall row are seen again on the final candidate.
13. **A package not green by the end of build day 4** ships with its switch off (recorded in `KeptOff`) or moves to
    2.0. Recommended: **the owner chooses per package on Sat 21**, with this document's measures as the evidence.

## What is not in this design

- Item 1 (bot players own alts) and item 3 (friends, regulars and grudges).
- Epic armour (decision 2), camp choice by knowledge (decision 9), deriving RvR group tiers from members (decision 5).
- Companions, real players, raids (apart from WP2R), battleground keep assaults, fights at a keep.
- Navigation mesh changes. Every new leg is walked on today's mesh.
- The "who's real?" evening stays 2.0's measure.

## Sources

Era-dated: written 2002 to January 2004. Retrospective: written later. Most RvR sources are in
[research/rvr-field-1.2/ERA-RESEARCH.md](research/rvr-field-1.2/ERA-RESEARCH.md).

- [Prima official guide, Dark Age of Camelot revised and expanded][prima], (c) 2002-2004 (era-dated; the expanded
  edition may postdate January 2004): pulling, power, sitting, area effects, /stick and /follow, horses.
- [Camelot Herald, Beginner's Guide: Getting Help][herald-help] (official, undated): asking other players.
- [Tom's Hardware forum, June 2004][toms] (just after the era): the compass joke.
- [Allakhazam, "Symbol of the Broken (level 50)"][epic50] (database page, player comments March-May 2003): a level 50
  epic's last steps and the armour.
- [Patch notes 1.65][patch165] (wiki summary): 8 October 2003; nothing on epics, artifacts or realm rank.
- ERA-RESEARCH sections 3, 5, 6 and 9: circle-strafing, /face, /stick, resting, walls, mocked mistakes, reaction times.
- [research/BUFFBOTS-2003.md](research/BUFFBOTS-2003.md): parked buffbots.

[prima]: https://ia803102.us.archive.org/20/items/Dark_Age_of_Camelot_Revised_and_Expanded_Prima_Official_eGuide/Dark_Age_of_Camelot_Revised_and_Expanded_Prima_Official_eGuide.pdf
[herald-help]: https://camelotherald.com/node/255
[toms]: https://forums.tomshardware.com/threads/newbie-questions.168086/
[epic50]: https://camelot.allakhazam.com/db/quests.html?cquest=648
[patch165]: https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65

## Appendix: for the builders

- **Scope.** `BotPeopleScope.Refusal` mirrors `RvrTacticsScope.Refusal`, adding stable rides and dungeon raids. Every
  hook asks it first and logs nothing when refused (the summary lines count refusals).
- **Tiers.** Reuse `RvrSkillTier` and `RvrGroupSkill.Shares(awareness)`. A bot's roll uses
  `AutonomousBotPersonality.Mix(id, salt)` with its own salt, never the group's, so a bot's tier does not follow its
  group's.
- **The movement primitive.** `AutonomousNavigationSurface.MoveAlongDryGround`, legs of 22 units at most, as in
  `BotCombatMovement`. Nothing calls the pathfinder per tick.
- **Sitting.** `GameBot.SetSeatedForClients` is private; WP5 makes an internal wrapper that stands the bot up on any
  move, cast or attack, as resting does.
- **Wall spots.** Guard archer positions floored on the mesh and reached by a proven route, as
  `AutonomousRvrDefense.Position` already does. No invented wall-top point.
- **Chat.** New lines go through the corpus (`build_corpus.py --verify`, the era words check) and the existing budgets
  (`AutonomousChatBudget`, `AutonomousChatReplyBudget`). The Travel scene's `ask_way` lines become the asking bot's
  real question.
- **Tests to keep green.** `UT_BotCombatMovement`, `UT_TownHangouts`, `UT_RaidDiscipline`, `UT_AutonomousBotPersonality`,
  `UT_RvrPerception*`, `UT_SiegeBalanceNov06`, `UT_DutyLeaseRealmRaid`, `UT_DutyLeaseBattleground`,
  `UT_DutyLeasePlayerGroup`.
