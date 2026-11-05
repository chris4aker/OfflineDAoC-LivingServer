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
raiders would have stood at the same muster. Open question for the owner: should a timeout with no wipe count as a
loss for sizing? This one was a routing fault, not a lost fight.

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
  - `assigned` (the forces committed to this siege);
  - `realmLevel50` and `realmRvr` (the attacking realm's online level 50s, and how many of them are on RvR duty);
  - `inOtherSieges`;
  - `defenseShare`.
- **`RVR_EVENT_ENDED`** adds `marched`, `attackers`, `defenders` and `thirdRealm`.

The next overnight run should show whether thin musters come from few RvR level 50s, from forces assigned but not
arriving (night `RVR_KEEP_ROUTE_FAILED` 505 against 52 by day, 234 of them Albion to Caer Erasleigh), or from too few
forces choosing to join.

**Options if the next run confirms thin musters** (not implemented):
- (a) Size the muster threshold and the march to the realm's RvR level 50s.
- (b) Call off a muster that is still under the deadline-start threshold (32) at its last deadline, as a failed rally.
  A failed rally doesn't move the balance. In this run that would also have cancelled the 26-strong army that captured
  keep 101 by day.
- (c) Leave thin-army results out of the balance.
