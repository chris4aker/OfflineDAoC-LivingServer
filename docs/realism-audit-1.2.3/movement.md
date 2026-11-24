# Movement audit, 1.2.3 — how bots look to a player standing beside them

Auditor: movement. Read at commit `4a00ab75` (local branch `fix/1.2.3-faction-polish`; `origin/` has no branch of that
name and the commit is not in `origin/main`, so I used the local one). Paths are under `source/server/GameServer/`.
Report only: no code changed, no server started, no PR opened.

**Status of every finding: tests only.** All of it is from reading code, plus two bits of arithmetic done in Python.
Nothing was seen in a client. None of it is in the owner's nine screenshots (those are all chat).

## What the classic client can show for a bot (the hard limits)

A gamebot is a `GameNPC` (`bots/GameBot.cs:32`). The client gets it by `NPCCreate` and `ObjectUpdate`, never by the
player packets (`packets/Server/PacketLib1124.cs:125`, `PacketLib168.cs:730`).

| The packet carries | So the client can show | It cannot show |
|---|---|---|
| position, heading, speed, destination, Z speed | running, walking at any speed, standing, turning (a snap per update) | a jump, a real strafe, a back-pedal, a swim stroke state |
| `targetId` (real id only when attacking, standing, in reach; else 65535) (`PacketLib168.cs:832-850`) | combat stance, auto-facing its target | "looking at" a non-enemy |
| flags: realm, ghost, torch, flying, underwater, stealth, statue | those | **sitting**, linkdead, AFK, weapon sheathed |
| `EmoteAnimation` (object id, one byte) (`PacketLib168.cs:1073`) | emote animations 1-46 | emote **text** (that is a chat packet) |
| riding association (`SendRiding`) | a horse ride | |

- **Sitting is a trick, and it is unproven on the client we now ship.** A seated bot is shown by emote `0xF0` and stood
  by `0xF1` (`bots/GameBot.cs:369-389`). Every sit in the game rests on it: resting, waiting, the bank, keep walls.
  `docs/BOTS-AS-PEOPLE-1.2.md:239,448` says it was checked in the new client only. `docs/PLAYED-CHECK-1.2.md:398`
  (row 6) says "Not run". `docs/LIVING-SERVER.md` still says "bots can't jump, sit or strafe".
- **Side steps and backing off are short legs.** A walk of 25 units or less does not turn the NPC
  (`ECS-Components/NpcMovementComponent.cs:878-881`), so legs of 22 units keep the facing (`bots/BotCombatMovement.cs:57`).
  How the classic client animates that (a slide or a step) is not known.
- **A standing turn is a snap** unless sent in steps. `BotTurning.TurnGradually` sends 45 degrees every 110 ms
  (`bots/BotCombatMovement.cs:319-400`). Only body language, town arrival, crafting and way-finding use it.

## What already exists (not re-reported)

Body language (looks, emotes, town steps, greet, ding cheer); town hangouts with 2-6 minute dwells and facing; the
overshoot; sit while waiting; keep walls; the /stick travel line with lost sticks and catch-up; melee circling, flank,
caster side steps, face-the-enemy kite; role formation; the bot death fall, rez animation, lord formation, DF stairs
and nav-link legs. Most are "tests only" in `PLAYED-CHECK-1.2.md` rows 6-7. I did not re-audit their internals, only
where they stop.

## The 15 tells, worst first

The first ten are the brief's "top 10".

### 1. In your own group the bots are statues
- **Seen:** you invite four bots and stop. They stand on fixed spots. Nobody turns, looks at you, emotes, shifts or
  sits for as long as you stand there. In a fight none of them circles or steps.
- **Real player:** fidgets, turns to the speaker, sits when the group sits, wanders a step.
- **Where:** `AutonomousWorldBotController.cs:402-408` returns before `Ambient` (line 414) when `IsPlayerLedGroup`.
  `AutonomousBotBodyLanguage.cs:157` (`Eligible`) needs an autonomous gamebot, so /bot, hired and /spawn companions
  never qualify. `BotBrain.cs:3185-3188`: "Idle bots stand in formation; there is no idle wandering". Combat movement
  is refused for a player-led group (`BotCombatMovement.cs:79`, `BotBrain.CombatMovement.cs:100,115`, `BotBrain.Flank.cs:139`).
- **How often:** always, for every bot you lead and every companion. This is the case the owner watches closest.
- **Class:** the realism layers were scoped away from the player ("kept as before") to protect companion behaviour.
- **Fix:** let the idle-life layer (below) run for bots in a player-led group and for companions when the leader has
  stood still 5 s: looks, look-at, emote, sit-with-leader. No steps (formation stays put). Packets: heading, emote.
- **Size:** M. **Risk:** low for combat (idle-only gate, same `Eligible` checks); companion rest and follow must not
  be woken by a look (a look is a heading change, not a walk; `BeginRecoveryRest` is not touched).

### 2. Followers in a standing bot party twitch: every look is undone within 0.4 s
- **Seen:** a party stands at camp or a meeting point. A follower turns its head away, then snaps straight back to
  face the leader. All followers face the leader, all the time.
- **Where:** a look is 30-150 degrees (`AutonomousBotBodyLanguage.cs:134-139`). An arrived formation follower re-faces
  its leader when more than 30 degrees off (`NpcMovementComponent.cs:1266-1270`, `BotCombatMovement.cs:323,359-362`),
  every 400 ms (`AutonomousGroupFormationFollow.cs:22`). The re-face is a snap (`TurnTo`), not gradual.
- **How often:** 119 of 120 possible look angles are past the tolerance (341 heading units; Python check of the
  integer maths). So about 99% of a follower's looks, each time the leader stands. The leader's own looks are kept.
- **Class:** two layers own the same state (heading) and the older one wins.
- **Fix:** an arrived formation follower of a standing leader keeps whatever heading it has; face the leader once on
  arrival only. Or give body language a "heading hold until" tick that `HoldsFollowHeading` honours.
- **Size:** S. **Risk:** low. Do not change the moving case or the player-led case (`HoldsFollowHeading` already
  excludes it). Lord formation and raid stations use their own facing.

### 3. Sitting may not show at all in the original client
- **Seen (if it fails):** bots never sit. They rest, wait and "sit on the wall" standing up. The owner's ask
  ("sitting when not in battle") suggests this is what he sees, but I cannot tell from here.
- **Where:** `GameBot.cs:369-389`, `PacketLib1124.cs` (re-send on create). Unverified: `PLAYED-CHECK-1.2.md:398`.
- **How often:** every rest and every long wait, if the emote is not drawn.
- **Class:** a feature shipped for the Godot client, never proven on the client that is now the only edition.
- **Fix:** first a 5-minute PC check: stand by a resting bot, look for `BOT_MOVE_SIT` in the log and a seated bot on
  screen. If it does not show, find an NPC sit animation the classic client does play (the client-modding guide says
  emote values are `animnifs.csv` ids, `docs/CLIENT-MODDING-GUIDE.txt:82`), or accept "bots do not sit" and drop
  the sit rows from the docs.
- **Size:** S to check, M/L if another animation id must be found. **Risk:** none to combat; visual only.

### 4. The rest is a sit-bounce: down for 3-10 seconds, then straight up
- **Seen:** a bot at 60% health sits, and four seconds later stands. A caster out of power sits about ten seconds.
- **Real player:** sat 30 s to 2 minutes between pulls in 2002.
- **Where:** resting bots regain at least 10% of maximum a second (`BotRestRecovery.cs:26-27`, `GameBot.cs:525-568`).
  They rest below 70% health, 25-45% power or 35% endurance (`AutonomousBotDecisionEngine.cs:413-433`) and stand the
  moment they are full (`AutonomousWorldBotController.cs:3372-3376`, `GameBot.cs:3238-3243`).
- **How often:** every rest. Arithmetic: 70% to full is 3 ticks; empty power is 10.
- **Class:** a progress rule (fast rest) shown raw as an animation.
- **Fix (visual only, no regen change):** do not stand at "full". Keep the seat until the bot's next move, cast or
  attack (the habit seat already works this way: `GameBot.cs:391-417`), and when a player is watching add a short
  spread, 0.4-1.5 s, between "decided to go" and standing. Offer the owner a separate choice to slow watched rests.
- **Size:** S. **Risk:** low; `StandFromHabit` already runs before any walk (`NpcMovementComponent.cs:859`). Raid
  musters use the same rest call, so test that a seated raider still answers a muster order.

### 5. Bots never look at anything
- **Seen:** you say something to a bot, send it a tell, buff it, rez it, trade with it, or wave at it. It answers in
  chat without turning. A monster walks up behind an idle bot; it does not turn until it is hit.
- **Where:** every idle look is a random 30-150 degrees left or right (`AutonomousBotBodyLanguage.cs:113,134`). The
  only look-at is one greeting per player per 20 minutes within 650 units (`:37,222-251`) and the ding cheer.
  No chat path turns a bot: the only `TurnTo`/`TurnGradually` callers are combat, town arrival, crafting, beggars and
  way-finding. A player's emote reaches only beggars (`commands/playercommands/emotes.cs:365-367`).
- **How often:** every spoken exchange at /say range and every buff or rez. Chat is the owner's first priority, so
  this is seen constantly.
- **Class:** chat and body are two systems with no link.
- **Fix:** one entry point, `IdleLife.LookAt(bot, object, reason)`, called from: a /say or /group line addressed to or
  answered by the bot when the speaker is within 700 units; a tell from a player within sight; a helpful spell or a
  rez landing on it; a player's targeted emote; a new player or aggressive monster entering 350 units of an idle bot.
  Delay 300-1,200 ms, gradual turn, hold 3-8 s. Idle bots only.
- **Size:** M. **Risk:** low; refused in combat, while casting, moving, seated-resting or in a raid station.

### 6. A standing party is a perfect ring
- **Seen:** eight bots on a circle of 88 units, 45 degrees apart, all facing the middle. A meeting point with three
  parties shows three rings.
- **Where:** `AutonomousBotGroupCoordinator.cs:1429-1436`: radius `64 + 3n`, angle `2*pi*i/n`. Role formation uses even
  arcs too (`AutonomousBotGroupCoordinator.RoleFormation.cs:46-80`). Player-led: a fixed compass bearing by name hash
  (`AutonomousBotDecisionEngine.cs:478-488`), so the group does not turn with you.
- **How often:** every time a bot party stands (camp, rendezvous, waiting for a member).
- **Class:** deterministic geometry with no spread.
- **Fix:** per member, by id hash: radius x0.75-1.35, angle +/-14 degrees, keep 32 units of personal space (the town
  rule's value). Standing members keep their arrival heading (tell 2). Pure function, same call sites.
- **Size:** S. **Risk:** low to medium: slots are resolved on the mesh already (`TryResolveFormationSlot`); keep the
  dungeon "tight" radius inside its current bound so no slot lands through a wall. Do not touch raid formations.

### 7. Emotes are silent, and bots never answer one
- **Seen:** a bot waves. No "Brannoc waves at you." line appears. You bow to a bot; nothing. A bot you rezzed or
  buffed never bows or thanks with an emote.
- **Real player:** /bow after a rez or buff and /wave back were near-universal in 2002. A player's emote always prints
  a line (`emotes.cs:403-424`, `CT_Emote`).
- **Where:** `GameLiving.Emote` sends the animation only (`gameobjects/GameLiving.cs:3155-3161`); `GameBot.Emote`
  (`GameBot.cs:333-338`) adds a transcript row, not a message.
- **How often:** every bot emote (about one per bot per 90 s in town).
- **Class:** NPC primitive used for a player act.
- **Fix:** `GameBot.Emote` also sends the standard emote text to players in emote range, with the target form when
  it greets or answers ("waves at you"). Add replies: bow or wave back 0.8-2.5 s after a player's targeted emote
  (once a minute per pair); bow, thank or salute after a rez or an out-of-combat buff from a player (60%).
- **Size:** S (text) + S (replies). **Risk:** none to combat. Mask enemy-realm names as `AutonomousNameMask` does.

### 8. No pause after a kill
- **Seen:** the monster drops and, inside about a second, the bot has turned and is running at the next one. Every
  kill, the same.
- **Real player:** loots, glances at health, maybe sits: 2-8 s before the next pull.
- **Where:** `AutonomousWorldBotController.cs:3056-3059`: next target search 0.9-1.4 s after the last. Fast recovery
  starts 2 s after combat even while moving (`BotRestRecovery.cs:8,63-79`), so most kills need no rest.
- **How often:** every solo kill that does not trigger a rest.
- **Class:** machine cadence.
- **Fix:** a "breather" before the next pull **only while a real player can see the bot**: 1.5-7 s, skewed short,
  longer by skill tier Casual. During it: a look at the corpse or around, sometimes the sit. Unwatched bots keep
  today's pace, so the world's progress is unchanged.
- **Size:** S. **Risk:** low to PvE pace near the player only; never in a group fight with adds, never in raids or RvR.

### 9. Services are used at arm's length and in zero time
- **Seen:** a bot runs at a merchant, stops well short (up to 256 units, about three body-widths of a house), and
  leaves. Bots coming from the same side stop along the same line. At the stable it reaches the spot and is on the
  horse the same instant.
- **Real player:** runs up close, stands facing the NPC for 5-30 s with a window open.
- **Where:** stop as soon as within `WORLD_PICKUP_DISTANCE` (256) of the NPC's own position
  (`AutonomousWorldBotController.cs:1252-1254`, `BotBrain.cs:2069-2090`); one transaction per 3 s, the first on
  arrival (`BotBrain.cs:2101`). Stable: `AutonomousWorldBotController.cs:5005-5021`. Personal space exists only for
  frontier supply merchants (`AutonomousSupplyMerchantSpace.cs`).
- **How often:** every sell, buy, repair, train and ride near a player; towns are where players stand longest.
- **Class:** an interaction range used as a standing distance; no dwell.
- **Fix:** reuse the town-hangout spot rule for real errands (in front of the NPC, 70-140 units, 32 personal space,
  face it: `AutonomousTownHangouts.cs:44-58`), then dwell 3-12 s before the first transaction and 2-6 s before
  boarding. Only when a player is in sight.
- **Size:** M. **Risk:** medium: service reachability has a long bug history (`AUTONOMOUS_SERVICE_UNREACHABLE`,
  the stuck watchdog). Fall back to today's stop when no spot is proven; mark watchdog progress during the dwell.

### 10. Every bot walks the same line
- **Seen:** on a road out of town, bots pass one after another on exactly the same track, cutting each corner at the
  same blade of grass, turning in instant snaps at each bend.
- **Where:** routes are mesh corridors with no per-bot offset; 37 `IssuePath`/`PathTo` sites in
  `AutonomousWorldBotController.cs` and only RvR approach has a lateral spread (`:2491`,
  `((DatabaseID % 17) - 8) * 60`). Turns at path nodes are single heading updates.
- **How often:** wherever two bots share a route within sight: town gates, stable to gate, zone roads.
- **Class:** deterministic pathing.
- **Fix:** a per-bot lane: shift each interior waypoint sideways by a fixed hash offset (up to +/-70 units outdoors,
  0 indoors and in dungeons), accepted only when the mesh allows the shifted point (the habits' `ProveLeg` and its
  40-checks-a-second budget). No navmesh change.
- **Size:** M. **Risk:** medium to travel: every stuck fix assumes the corridor. Outdoor zones only, off for seams,
  zone points, bridges, doors, DF, raids and RvR lanes; watch stuck-watchdog counts.

### 11. Logout: a line, then gone standing up in three seconds
- **Seen:** "gtg cya" and 2.5-4.5 s later the bot blinks out, standing.
- **Real player:** /quit sits you down for 20 s first.
- **Where:** `AutonomousBotSchedule.cs:359-391`.
- **How often:** about one break per bot per four hours plus schedule logoffs; seen now and then in town.
- **Fix:** if sitting shows (tell 3): sit, wait 18-24 s, delete; cancel if attacked. If it does not show: stand
  still 15-22 s. **Size:** S. **Risk:** none.

### 12. A wipe releases in one beat
- **Seen:** four bots die together with no healer. All four corpses vanish within the same 3 s, about 21 s later.
- **Where:** `GameBot.cs:1155-1203`: 3 s timer, fixed 20 s (90 s with a rezzer).
- **How often:** each wipe near a player. **Class:** fixed delay.
- **Fix:** per bot 15-45 s by hash and tier; keep 90 s and the siege wait. **Size:** S. **Risk:** low; group
  release-and-rejoin timing reads the same clock, so keep its order.

### 13. Bots in your group do not sit when you sit, and shuffle on your every step
- **Seen:** you sit to rest; your bots stand around you (unless they need rest themselves). You take two steps; all of
  them re-walk to their exact spot, at once.
- **Where:** no bot reads the leader's `IsSitting` for posture. Slot re-walk past 20 units (`BotBrain.cs:1284-1300`).
- **How often:** every rest and every small move in a player-led group.
- **Fix:** sit 1-6 s after the leader sits (spread, 80% of bots), stand 0.5-3 s after the leader stands or at once on
  aggro. Re-walk only past 60-110 units (per bot), after 0.3-1.5 s. **Size:** S/M. **Risk:** low; keep today's
  tolerance while the leader is in combat or moving far, and for healers' range holds.

### 14. Sprint is only for running away
- **Seen:** bots never sprint to a pull, to catch the group, or to a dying friend.
- **Where:** the only caller is `BotBrain.Retreat.cs:271,324`. Catch-up is a hidden 25% speed bonus
  (`AutonomousGroupPace.cs`), which a player beside them sees as a bot running faster than a player can.
- **How often:** group travel with a straggler. **Class:** invisible mechanic instead of the visible one.
- **Fix:** when the catch-up bonus applies and endurance allows, turn Sprint on (the client shows the effect) instead
  of an unexplained speed. **Size:** S. **Risk:** low to medium: endurance spent before a fight; cap at 60% endurance.

### 15. Standing bots that are not "waiting" never move their feet
- **Seen:** a bot at a merchant, a trainer, the Exchange or on a road stands on one point until it leaves.
- **Where:** steps are allowed only on a town break (`AutonomousBotBodyLanguage.cs:176-181,197`; `Ambient` passes
  `allowStep: false`).
- **How often:** constant, low salience. **Fix:** allow one proven 22-unit leg now and then for a solo, ungrouped,
  idle bot (not in formation). **Size:** S. **Risk:** low; uses the existing leg proof and budget.

## Design: an "idle life" layer

One class, `BotIdleLife`, that owns a bot's posture while it is idle. Body language, the sit habit and the new
look-at become its pieces, so two layers never fight over heading again (tell 2).

**Who:** autonomous gamebots anywhere; gamebots in a player's group; companions (/bot, hired, /spawn) for looks,
look-at, emote replies and sit-with-leader only. Never real players.

**When (the gate, checked first, a handful of field reads):** alive, active, not in combat, not attacking, no aggro,
not casting, not stealthed, not crowd-controlled, not on a stable route, not moving, not in a raid station or lord
formation, and **a real player within sight** (cache the nearest watcher per bot for 3 s).

**Pieces**

| Piece | What the bot does | Sent as | Timing (per bot, random) |
|---|---|---|---|
| Look-at | turns to a speaker, a newcomer, a caster who helped it, a monster drifting within 350 | heading, in 45-degree steps | starts after 0.3-1.2 s, holds 3-8 s |
| Glance | today's random look, 30-150 degrees | heading | every 8-30 s (today's values) |
| Shift | one or two 22-unit legs, mesh-proven | short walk | every 40-120 s; never in formation or resting |
| Sit / stand | rest seat kept until the next action; wait seat (today's 30-50 s, 60%); sit with a sitting leader; sit before logout | `0xF0`/`0xF1` (if tell 3 passes) | stand spread 0.4-1.5 s |
| Emote | today's pools, plus replies (wave back, bow after rez or buff) | emote animation + emote text | reply after 0.8-2.5 s; pair cooldown 60 s |
| Breather | pause after a kill, at a merchant, before a horse | nothing (a delay) | 1.5-7 s, 3-12 s, 2-6 s |

**Priority:** anything the brain or controller wants to do wins at once. Idle life never queues, blocks or delays a
heal, a pull order, a follow, a muster or an attack. A piece in progress is dropped (the gradual turn already drops
itself when something else turns the bot: `BotCombatMovement.cs:384-390`).

**Budget**

- No new timers per bot: it runs inside the existing controller tick and brain think, as `Ambient` does today.
- Unwatched bots cost one cached boolean. With one player online, only bots within sight (tens, not 1,500) do work.
- At most 1 action per bot per 3 s; server-wide caps per second: 40 mesh leg checks (the habits' existing budget,
  `BotMovementHabits.cs:340-343`), 20 emote sends, 60 heading sends.
- No work when the game loop is busy (`LoopBusy`, tick over 50 ms): skip, do not queue.
- One switch (`bot_living_idle_life`, on by default per AGENTS.md) and one per piece; a 5-minute `BOT_IDLE_LIFE`
  summary line with counts and drops, like `BOT_MOVE_HABITS`.

**Testing each piece without a client**

- **Pure rules (NUnit):** every decision as a pure function of state and rolls, like `BotMovementHabits`. Tests: the
  gate refuses in each busy state; look-at picks the right object and delay range; seat kept until an action; ring
  spread keeps 32 units apart for 2-8 members over 10,000 hashes.
- **Ownership test:** a standing follower with a held look is not re-faced by `FollowTick` (pins tell 2).
- **Packet spy:** a fake `IPacketLib` that records `SendObjectUpdate`, `SendEmoteAnimation`, `SendMessage` per
  viewer. Scripted scenes (party stands 2 minutes; player says a line; player sits; bot rests and is ordered to pull)
  assert the sent sequence and its spacing.
- **Statue metric:** in the existing test-world runs, per bot in a player's sight and standing: seconds since its
  last heading change, emote, sit or step. Print `MOVE_BENCH_ROW` lines as the chat bench does. Ratchet the median
  and the share of bots still for over 60 s.
- **Machine-timing metric:** for delays that should spread (post-kill, release, stand-up, re-walk), the bench prints
  the distinct values across 50 bots; a ratchet fails when fewer than N distinct values or a spread under X.
- **Budget test:** 1,500 bots, no watcher: zero sends, zero mesh checks. 1,500 bots with a watcher on each: the caps
  hold and the tick stays under its bound (`AiProfile`).
- **No-regression:** the existing combat, travel and raid suites run with the switch on; the stuck-watchdog recovery
  count in a test-world run must not rise.
- **PC log:** `BOT_IDLE_LIFE` counts above zero and screenshots. Only this makes any piece "working".

## Checked and found fine

- **Run speed:** all bots run at the player base speed; real players do too. Catch-up is capped at 25% and off in combat.
- **Group travel:** the /stick line, lookahead and lost sticks are designed and merged (tests only). Not a block.
- **Timing phases:** controller and brain timers are offset by `ObjectID`, so bots do not act on the same tick.
- **Facing in fights:** melee re-faces past 30 degrees (`BotBrain.cs:1349-1361`); casters turn when they cast
  (`spells/SpellHandler.cs:485,877,1029`).
- **Town breaks:** spots in front of NPCs, 32 units personal space, 2-6 minute dwells, gradual facing, overshoot.
- **Camps:** arrival radius 1,250, so bots do not stack on one camp point.
- **Body language cost:** runs only with a watcher in sight; emote share was already cut after 2,303 emotes in 7 hours.
- **Wait sits and wall sits:** the rules and their stand-up triggers read correctly (subject to tell 3).

## Could not check, and why

- **Anything on screen.** No client here. In particular: whether `0xF0` seats an NPC in the original client; how a
  22-unit sideways leg is animated; how emotes 1-46 look on a player-model NPC.
- **How the classic client labels a bot** (name colour, the NPC-names toggle, target window text). Bots are NPCs of
  your realm; a player may read that at a glance. Not movement, but it frames everything above.
- **Frequencies.** I had no PC log. "How often" above is from code paths, not counts.
- **Whether the attack stance lingers after a fight** (a comment at `AutonomousWorldBotController.cs:1333-1337` says
  an old stance was once left on). Needs a packet spy or a client.
- **Pets:** placement and following were not read in depth.
- **Watchdog teleports in sight of a player** (`AutonomousStuckWatchdog.cs:526,618,656,693` call `MoveTo` with no
  watcher check). Rate unknown without logs.
- **PvE assist reaction time** in a bot party (RvR has tiered delays; I did not trace the PvE pull order).
- **Doors, boats, swimming.** Not reached.
- **C# runs.** I ran none; no draft PR was opened. The two numeric claims (119 of 120 looks re-faced; rest lengths)
  are arithmetic on constants.

## Measuring tool worth keeping

Nothing was built. Worth building: the **packet spy + statue metric** above, printed as `MOVE_BENCH_ROW`. It can be a
CI ratchet in the same way as `bench_ratchet.json`: median still-seconds of a watched idle bot, share of looks
undone within 1 s (today about 99% for followers), share of seats shorter than 15 s, distinct-delay counts.

## Hand-back summary

1. Bots are NPCs to the client: no sit flag, no jump, no strafe. Sitting is emote `0xF0`, never proven on the
   original client, which is now the only edition. Check that first; half the owner's ask depends on it.
2. In the player's own group, and for all companions, there is no idle life at all: every realism layer excludes
   `IsPlayerLedGroup`. This is the biggest gap and the one the owner sees.
3. Likely bug: a standing follower's every look is snapped back to its leader within 0.4 s (99% of looks). Small fix.
4. Rests last 3-10 s because resting regains 10% a second; a visual-only fix keeps the seat until the next action.
5. Bots never look at a speaker, a helper, a newcomer or a nearby monster. Chat and body are not linked.
6. Standing parties form an exact even ring facing the centre.
7. Bot emotes print no text; bots never answer an emote or bow after a rez or buff.
8. No pause after a kill; services used from up to 256 units with no dwell; instant mount.
9. All bots walk the identical track; logout and wipe release use fixed short delays; sprint only when fleeing.
10. Proposed: one `BotIdleLife` layer owning idle posture, gated on "a player can see it", with server-wide caps,
    no per-bot timers, and tests by pure rules, a packet spy and a "statue seconds" bench that can ratchet in CI.
11. Small and safe first: tells 2, 4, 6, 7, 11, 12. Medium: 1, 5, 9, 13. Riskiest to travel: 10 (lanes).
12. Everything here is tests only (code reading). Nothing was run in game, no code changed, no PR opened.
