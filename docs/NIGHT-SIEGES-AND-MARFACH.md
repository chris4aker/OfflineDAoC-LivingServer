# Overnight 2026-11-04/05: the Marfach timeout and the night sieges

Evidence: the owner's test copy, build `616190a` (started 20:46, local time) and `c49f7a3` (from 07:54), console log
and `server.log`. Counts are log lines; times are local. No game was played for this; nothing here was checked in game.

## 1. Hibernia's Marfach Caverns raid timed out

**What the raid did.** Sized 64 at 00:55, it left Druim Cain with nine parties at 01:05 and was inside by 01:16.

| Time | What happened |
|---|---|
| 01:16-01:25 | Cleared the way in (withered oaks, wolfhounds, living dead). |
| 01:25-01:26 | Avartack the Champion attacked the raid and died in 0.7 minutes. This was the only named kill. |
| 01:26-01:28 | Black Lady attacked. The raid took her to 44%, then she was drawn 1,618 units from her spawn. She leashed home and healed to 100% (`AUTONOMOUS_TETHER_RESET`, `TARGET_HEALED returning=True`). |
| 01:28:22 | `TARGET_SWITCH` to the **Ancient Black Oak** (level 68, "it is fighting the raid"). The Oak is spawned by Black Lady's script at 30091,37620,15049. |
| 01:30-05:19 | Mustered at 31012,35916,14994, about 1,940 units from the Oak. The raid was sent in **91 times** (`REALM_EXPEDITION_FINAL_ENGAGE`, 36-76 mustered each time). |
| 05:19 | The four-hour window ended: "Timed out", support 77, one named kill, no wipe, no Special Forces. |

**Why it never fought the Oak.**
- **The raid never got close.** In all 228 one-minute `TARGET_STATUS` samples of the Oak it was at 100%, out of combat,
  with **0 raiders within 1,000 units**.
- **The parties never left the muster.** All 535 camps the raid handed its parties for the Oak (`CAMP_REPOSITIONED`
  "from") were 1,600-2,200 units from it. The bots' own positions in 547 `CAMP_WAIT` lines were 1,500-2,250 away.
- **This is how a party stays put.** `TryBattlePost` found no formation post around the Oak. A party with no post
  falls back to its old post, and it only walks toward the front anyway when that post is more than 2,500 units away.
  The muster was about 1,940 away, so every party waited where it was.
- **Why no post.** The switch made the Oak's own position the front. Every other target's front is a walkable
  approach that has been checked from the raid (`AutonomousZonePointApproach`). A formation post needs line of sight
  and a corridor from the centre point, and none passed from the Oak's spawn point. That point is 48 units above
  Black Lady's spawn floor and probably inside the tree's geometry. This is inferred: zone276.nav is not in the build
  container, so it was not probed. `UT_RaidDungeonRoutesProbe.TheBlackOakIsFoughtFromAnApproach` checks it on an
  install with navmeshes.
- **Why it looped instead of moving on.** Each pull waited the two-minute grace, found the Oak out of combat and ended
  "left combat" with nobody fallen, so it was not a wipe. The muster still held 36 or more, so the next pull went at
  once. Nothing set the Oak aside:
  - the muster was never empty, so `MUSTER_UNREACHABLE` did not fire;
  - mustering resets the no-damage clock;
  - the raid was not blocked, so `ROUTE_BLOCKED` did not fire.
- **Side effect.** 185 raiders stood still for 15 minutes and were sent to Tir na Nog by the stuck watchdog
  (`AUTONOMOUS_STUCK_15M_RECOVERY`). They walked back in, which is why the muster count swings.

**Not a navmesh hole on the route.** The way in was walked; Avartack and Black Lady were fought; in 3.5 hours only 2
`REALM_EXPEDITION_ROUTE_RETRY` lines came from inside the dungeon. No navmesh change is proposed.

**Other Marfach runs:** none in these logs. This was the first Marfach raid since the 616190a and c49f7a3 starts. The
uploaded `realm-event-records.sqlite3` is dated 2026-11-04 16:38 and holds no overnight rows (its WAL file was not
uploaded), so the ledger could not be checked.

**Timeouts and the Special Forces.** A timeout already counts as a loss: `AutonomousEpicSpecialForces.IsLoss`
includes "Timed out". So the next Marfach raid starts one loss step larger, the Special Forces call-up grows a party,
and two losses in a row bring the Special Forces from the start. Dragons and dungeons follow the same rule. The Special
Forces are called on a wipe, and this raid never wiped. Calling them on this timeout would not have helped: 24 more
raiders would have stood at the same muster. Owner's decision (2026-11-05): **a timeout with no wipe is neither a win nor a loss**, for the raid's size and for
the Special Forces record alike. A timeout after one or more wipes still counts as a loss. The Herald still records
"Timed out"; every raid's details now end with its wipes ("Wipes: N."), and only the sizing record reads them
(`AutonomousEpicSpecialForces.RecordOutcome`, `RealmEventRecordStore.WipesIn`). Rows written before this change have no
wipe count and keep counting a timeout as a loss, so this overnight Marfach row on the owner's world still counts as
one loss.

**Fix (`RealmRaidDungeonRoute`):**
1. **The switch fights from an approach.** When the raid turns on an encounter that is attacking it, the front is a
   walkable approach within 220 units, checked from the front or a party leader, as for every other target. If none
   is found it keeps the old raw position. `TARGET_SWITCH` now logs `front=` and `approach=resolved|raw`.
2. **Pulls that reach nobody.** A pull after which the encounter never entered combat reached nobody. After three in
   a row (`MaximumPullsWithoutContact`, about 6-7 minutes), the encounter is set aside for 15 minutes like an
   unreachable muster, and the raid goes on to its other encounters. This logs `REALM_EXPEDITION_PULL_UNREACHED` with
   the target, the front, the muster and the nearest raider's distance. A pull that gets a fight going resets the
   count, and a wipe stays a failed attempt. On this run the Oak would have been set aside at about 01:47 instead of
   being pulled until 05:19.

## 1b. Krondon, build 8d7baa3 (2026-11-05 12:35-15:40): a second case

The Albion Krondon raid (region 61) held "Mustering for Beran the Supply Master (2/50)" from 14:28 to the end of the log.
That build doesn't have the fix above, but the fix wouldn't have helped: no pull was ever made, so the no-contact rule
never counted.

**What happened:**
- **The Beran muster never filled.** 30 `FINAL_MUSTER` lines at 48906,25589, 1,400 units short of Beran, counted 0-5
  of 50. All 57 Beran status lines read 100%, out of combat, 0 raiders within 1,000.
- **The raid was fighting somebody else.** At the same time, 59-77 of the raid's 101 stood about 4,200 units south,
  fighting **Scurceol Hyrde** at 49000,21379. He was 3,219 units from his spawn, inside his 3,800 tether, and stayed at
  100%: he is immune while his four Lyft Miht orbs live. All 42 raider deaths in 14:27-15:40 were to him, and 3 of
  them had Beran as their goal.
- **A 20-minute cycle.** Scurceol was skipped ("no damage for five minutes") at 14:27, 14:47, 15:08 and 15:28. His
  15-minute retry then ran out, and the raid switched back to him ("it is fighting the raid") at 14:42, 15:02 and
  15:23. Each return to Beran started a fresh muster clock.
- **No existing rule fired.** The muster was never empty, so `MUSTER_UNREACHABLE` didn't fire. The one-third patience
  (20 minutes) was never reached, because each muster window lasted only about 14 minutes.
- **The muster point itself was reachable.** It lies on the walked path from the raid's main body, and 2-5 raiders
  got there. The rest were held in combat.
- **The orbs were never a target.** "Lyft" appears in no log line of the run, so the logs can't say whether the orbs
  were alive, or why they weren't picked.
- **The 2.82 s NpcService stall at 14:27:57.** That tick ran 85 AI turns of about 70 ms each, plus a 483 ms GC pause,
  two seconds after the raid's destination moved to the Beran muster. One of the five slowest bots named is a Krondon
  raider. It is likely that the whole raid re-pathed to a point 4,000 units away in one tick, but that is not proven,
  and it is not changed here.

**Fix (`RealmRaidDungeonRoute`):**
- **A stalled muster is set aside.** A muster that hasn't gained a raider for 20 minutes
  (`MusterStalledMilliseconds`), and still has fewer than it needs, sets its encounter aside for 15 minutes. The log
  line is `REALM_EXPEDITION_MUSTER_STALLED`. It gives the best count and how long ago it was reached, the muster point,
  the raid's main body, and `pinnedBy`: the epic encounters the raiders are fighting instead, with counts.
- **The clock follows the encounter.** It keeps running while the raid turns away and back, so Krondon's Beran
  muster would have been set aside at 15:02. A pull resets it.
- **More on the hold line.** `REALM_EXPEDITION_HOLD` now lists every live epic encounter with why the route passes it
  by (it was Galladoria only), and `pinnedBy`. The next run will show whether the Lyft Miht orbs are alive and why
  they aren't picked.
- **What it doesn't fix.** It stops the hour-long hold, but it doesn't free raiders from a fight with an immune
  encounter. The real Krondon fix is to kill the orbs, or to break off and draw Scurceol past his tether. That needs
  the next run's encounter list and is a follow-up.

## 2. Night sieges

Overnight (20:46-07:54): 2 captured, 7 defended, 2 failed rallies, 1 still open at the restart. Day (07:54-12:36): 4
captured, 1 defended.

**The "fewer bots online at night" idea does not hold for this run:**
- **No login schedule.** The build ran with `bot_schedule=off` (`FORK_BUILD`). The 1,189 `LIVING_SERVER_LOGOUT`
  lines are short "brb" breaks, `mode=Off`.
- **Online level 50s were flat.** `REALM_RAID_SCHEDULED online=` counts each realm's online level-50 gamebots. It read
  590-597 all night and 581-595 by day.
- **RvR activity was flat.** Per hour, overnight and day alike: siege parties formed 43-103, frontier departures
  312-720, RvR kills 1,084-2,199.
- **Raids don't recruit RvR bots.** Raids take level 50s waiting for a group or grinding solo
  (`MayAnswerAutomaticRaid`), so they don't drain the siege pool directly.
- **No clock in the code.** Siege code has no time-of-day logic.

**What the outcomes do track: the size of the army that marched.** The muster needs 64 for a keep and marches at the
12+5 minute deadline with whoever is there.

| Keep sieges (not relic), both sessions | Captured | Defended | Failed rally |
|---|---|---|---|
| Marched 9-45 (11 sieges) | 2 (26, 39) | 7 (9, 18, 30, 32, 36, 44 and the 4-hour 36) | 2 (27, 45) |
| Marched 49-96 (4 sieges) | 4 (49, 68, 69, 96) | 0 | 0 |

- **Thin marches were mostly at night.** 9 of the 11 night sieges marched with 9-45.
- **But day and night aren't a fair comparison.** Three of the four large musters opened within 50 minutes of a
  restart, when every bot had just logged in: 20:48 (69), 07:57 (49 and the 115 relic army) and 08:33 (68). The day
  sample is 5 sieges.
- **Why night musters were thinner is not shown by these logs.** `RVR_SIEGE_MARCH` logged only who had gathered. It
  did not log how many forces were assigned, or how many level 50s the realm had on RvR duty.

**The siege balance did what it is built to do.** It started at +0.25 (defender share 1.0). After the night's
defences it reached its attacker-side limit, -0.25 (share 0.5), by 03:44 (window `DDDDDD`), and stayed there into the
day. At share 0.5, armies of 17-51 still lost: walls, doors and keep guards don't scale with the balance. Per-period
(day and night) windows would answer a population difference that isn't in the evidence, so they were not added.

**Era realism.** Night raids on undermanned realms were real, but they won because the defenders were offline. Here
the defenders were online and scale with the attacking army, so a thin night muster is simply a weak army, not a
period feature.

**Change in this PR: logging only.** The evidence doesn't support a gameplay change yet:
- **`RVR_SIEGE_MARCH`** adds:
  - `kind` (`keep-siege`, `relic-siege`, `retake`, `player-attack-defense`);
  - `needed` (what the muster waited for: 64 for a keep, 120 for a relic keep) and `thin` (`true` when a keep or relic
    siege marched short of it);
  - `assigned` (the forces committed to this siege);
  - `realmLevel50` and `realmRvr` (the attacking realm's online level 50s, and how many of them are on RvR duty);
  - `inOtherSieges`;
  - `defenseShare`.
- **`RVR_EVENT_ENDED`** adds `kind`, `thin`, `marched`, `attackers`, `defenders` and `thirdRealm`.

The next overnight run should show whether thin musters come from few RvR level 50s, from forces assigned but not
arriving (night `RVR_KEEP_ROUTE_FAILED` 505 against 52 by day, 234 of them Albion to Caer Erasleigh), or from too few
forces choosing to join.

## 3. Design direction for the siege follow-up (owner, 2026-11-05)

Not implemented. The next run's logging decides the cause first; this is what the follow-up should build towards.

- **The owner's words:** "small raiding parties or attacks are still realistic from time to time, not every raid or
  dungeon was a huge coordinated event", but it must stay balanced.
- **Most sieges muster to the realm's level 50s on RvR duty.** A full siege waits for an army sized to what the realm
  actually has in the frontier (`realmRvr`), not a fixed 64 it may never reach. This is the fix for the accidental thin
  keep siege.
- **A deliberate minority are small strikes.** A small force goes out on purpose, picks a target a small force can
  take (towers, lightly held keeps), and is planned and logged as a strike. Today a small army is just a keep siege
  that marched short of its muster.
- **Small strikes don't swing the siege balance.** Their results are weighted so a run of strike wins or losses
  doesn't move the defenders' share the way full sieges do. Full sieges stay the balance's measure, so neither side
  gets free wins.
- **Telling the two apart in the logs.** `kind` names the attack. A deliberate strike would get its own kind (for
  example `tower-strike`), and `thin` is only ever `true` for a `keep-siege` or `relic-siege` that marched below
  `needed`. So the next run's `thin=true` lines are all accidental thin musters; after the follow-up, a strike is
  never counted as one (`AutonomousRvrEventLayer.MarchedThin`).

**Options considered before the owner's direction** (kept for the record):
- (a) Size the muster threshold and the march to the realm's RvR level 50s. This is now the direction above.
- (b) Call off a muster that is still under the deadline-start threshold (32) at its last deadline, as a failed rally.
  A failed rally doesn't move the balance. In this run that would also have cancelled the 26-strong army that captured
  keep 101 by day.
- (c) Leave thin-army results out of the balance. The direction above weights strikes instead.
