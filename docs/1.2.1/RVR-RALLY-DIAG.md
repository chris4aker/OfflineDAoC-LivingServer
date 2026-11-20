# DIAG 43c43aa: RVR_RALLY_DELAY on rvr-keep-77 / rvr-keep-105

Log: `c43.log` (owner PC, build 1.2-43c43aa, 13:20-14:22, restart/FORK_BUILD 13:40:58).
Source: worktree `wt-docs` (integration/1.2, HEAD bd982da77). The commit `43c43aa` is not in this clone, so I could not
diff the worktree against the build. Code line numbers are from this worktree.
"Shown" = in the log or code. "Inferred" = my reading, not directly logged.

## 1. RVR_RALLY_DELAY: what it is and why it fired

### Counts (shown)
- 50 lines, all after the restart (13:51:17-14:21:04). They name 59 member slots and **58 distinct bot ids**, not 49
  (3236 Linaelorbel appears twice: 14:12:55 and 14:21:04).
- By target and side:
  | Target | Side | Lines | Window |
  |---|---|---|---|
  | rvr-keep-77 Hlidskialf Faste | Hibernia attackers (incl. solo rvr-5394/4911/5149) | 26 | 13:51:17-14:12:04 |
  | rvr-keep-77 | Midgard defenders (midgard-191e91-287) | 3 | 14:08:19-14:09:41 |
  | rvr-keep-105 Dun Scathaig | Albion attackers | 15 | 13:59:06-14:21:04 |
  | rvr-keep-105 | Hibernia defenders (-036, -183, -013, -164) | 6 | 14:14:16-14:15:06 |
- The lines are not only "Hibernia on 77 and Albion on 105". Defenders of both keeps were flagged too.
- No class pattern: 22 classes. Blademaster 7, Bard 4, Animist 4, and the rest 1-3 each.

### The rule (code)
- `AutonomousRvrEventLayer.cs:1704-1718`: every 10 s, for every event that is **not yet BattleStarted** (`:1707`), each
  `Travel` entry whose `ProgressTick` is 8 minutes old (`StragglerTimeoutMilliseconds`, `:34`) is grouped by force. The
  code logs RVR_RALLY_DELAY and **resets `ProgressTick` to now** (`:1715`). It never evicts, releases or ends anything.
  The comment says so: "Missing members remain assigned ... Keep bounded diagnostics, not eviction."
- Progress is recorded in `ReportTravel` (`:1467-1485`) only when one of these happens:
  - `arrived` is true
  - the bot moved 256 units or more since its last progress point
  - the bot changed region
  - the bot's distance to its post improved by 128 units or more

  Once the battle has started, nothing is recorded (`:1471`).
- `ReportTravel` has only two callers:
  - `AutonomousWorldBotController.cs:514`: every turn of every shared-event bot, with `distance=+Inf, arrived=false`.
  - `AutonomousWorldBotController.Rally.cs:165`: only on the **keep rally post** path, with `arrived=atPost`.
- **The muster phase never reports arrival.** `HandleSiegeMuster` (`Rally.cs:23-70`) reports
  `ReportMusterAttendance` (`:51`). At its post the bot stops and holds (`:53-60`) and never calls `ReportTravel`
  with `arrived=true`. The same is true of defenders holding a defense muster.
  - So a bot that reaches its muster post on time and stands there as ordered is flagged 8 minutes later, and again
    every 8 minutes until the march (inferred from code; it fits every timing below).

### What the flagged bots were doing (log)
I classified each flagged member by its own route, death and stall lines in the 10 minutes before its flag.

**39 of 50 lines fired while that side was mustering, or within 30 s of its march order.** This is the false-positive case.
- keep-77 attackers:
  - Event opened 13:41:17. RVR_MUSTER_EXTENDED fired at 13:53:46 (mustered=46, needed=64). RVR_SIEGE_MARCH at
    13:58:55 (mustered=67).
  - 19 flag lines fell between 13:51:17 and 13:59:06.
  - Most of these members have no route, stall or porter line at all, only the party formation. Examples:
    - Qwuxella 4609: entered the world 13:42:21, RVR_SIEGE_PARTY_FORMED 13:48:46, flagged 13:51:17.
    - Moonrider 4881: party formed 13:48:25, flagged 13:55:18.
    - Lorcedan and Braninorgan: flagged 13:57:32.
- keep-105 attackers:
  - Event opened 13:46:45. RVR_MUSTER_EXTENDED fired at 13:59:16 (mustered=50). RVR_SIEGE_MARCH at 14:04:23
    (mustered=63, thin=true).
  - 11 flag lines fell between 13:59:06 and 14:03:33.
  - Example: the whole of albion-191e91-291 (Cedenorwin, Eadburhe, Wilen and others) formed 13:54:11 and was flagged
    14:02:22-14:02:52.
- keep-77 defenders: RVR_DEFENSE_MUSTER at 13:58:56, extended at 14:05:09, RVR_DEFENSE_MARCH at 14:09:13. The 3 Midgard
  flags fell at 14:08:19-14:09:41.
- keep-105 defenders: RVR_DEFENSE_MUSTER at 14:04:25, extended at 14:10:33, RVR_DEFENSE_MARCH at 14:14:37. The 6 Hibernia
  flags fell at 14:14:16-14:15:06.
- Both armies did march and fight (shown). Most flagged members later die in their target zone:
  - Hibernia attackers of keep-77 in Jamtland Mountains (region 100), 14:14:43-14:21:48: Qwuxella, Siotick, Lorcedan,
    Svygrek and others.
  - Albion attackers of keep-105 in Breifine (region 200), 14:16:25-14:21:14.

**About 11 lines show a real route problem.** Four places stand out:
1. **Odin's Gate, the Hibernian muster for Midgard frontier** (`AutonomousFrontierTransport.cs:119`, "Odin Hib"
   100:596055,581400).
   - Bots near their muster posts loop through AUTONOMOUS_ROUTE_RECOVERY: PartialPathFound to a spot only 100-220
     units away. AUTONOMOUS_ROUTE_POCKET_ESCAPE then sends them to **Tir na Nog** (201:33197,31200,
     `AutonomousStuckWatchdog.cs:81`).
   - Miadur 4598: 13:57:03-13:58:00 at 596917,581121, escaped 13:58:00, flagged 14:06:28.
   - Yvik 4824: escaped 13:58:04, flagged 14:07:28.
   - Blotiogg 4677: escaped 13:58:09, flagged 14:07:38, no log line after that.
   - All three were sent home roughly one minute before the 13:58:55 march.
   - Poshozois 4911 and Clumsychampion 5149 at 596048,582909:
     - RVR_LANE_FALLBACK "no planner route onto the lane" at 13:59:07
     - RVR_KEEP_TRAVEL_STALL at 14:00:56
     - RVR_KEEP_ROUTE_FAILED target=rvr-keep-77 "No connected exterior route" at 14:04:15, 14:04:47, 14:07:23,
       14:09:59 and 14:11:30
     - flags at 14:07:59 and 14:08:09
   - Both are back in region 201 by 14:17-14:20.
2. **Emain Macha zone 211/212 seam at 200:384960,385504.** This is on the walk to Druim Ligen, the medallion vendor
   (goal 333231,420812).
   - After the restart there are 206 AUTONOMOUS_ZONE_SEAM_CYCLE lines here, from **75 distinct Hibernian bots** (pre-restart: 14).
   - The same spot also logs AUTONOMOUS_GROUP_UNREACHABLE_RENDEZVOUS and ROUTE_STALL_TRACE.
   - Each cycle ends in a pocket escape to Tir na Nog (108 such escapes after the restart).
   - Flagged members that went through it: Siotick (13:47:20, flagged 13:57:12), Qwira99 (13:48:48, flagged 13:58:44)
     and Siomara (14:03:29, flagged 14:12:04).
   - Svygrek, Ciaraorlan, Lilspud, Kiarrain, Braninorgan and Stevewyn hit it at 13:43-13:48. Their flags came later,
     during the muster.
   - Inferred: this costs the Hibernian army minutes of travel per bot. It is a pre-existing route problem, not a
     siege-logic one.
3. **The Albion muster for Emain Macha** ("Emain Alb" 200:475835,343661, `AutonomousFrontierTransport.cs:115`).
   - Linaelorbel 3236 sits at 476630,343539 (about 800 units away) from 14:04:43 to the end of the log.
   - It logs ROUTE_RECOVERY and then RVR_KEEP_ROUTE_FAILED target=rvr-keep-105 "No connected exterior route" 5 times:
     14:07:17, 14:10:34, 14:13:46, 14:18:01 and 14:21:04+.
   - It is flagged twice, 8 minutes apart. This is the only bot that stays stuck for the whole log.
   - Maraelaline 3118 logs AUTONOMOUS_ROUTE_STALL_TRACE (speed 0) at 474924,343159, 13:58:07.
4. **Deaths.** 4 members died before their flag and released or walked back: Rarkie, Qwolkea, Hundaff and Akerd4.

### Verdict on Q1
- The dominant cause (39/50) is **expected behaviour mis-labelled**. A siege party holding its border-keep or outpost
  muster post for 8 minutes is reported as "no approach progress", because the muster path never reports arrival.
- It is not a muster that never completes: both musters marched (67 and 63).
- It is not a stuck path for these bots: they marched and died at the target.
- The minority (about 11 lines) points at **real, separate route bugs**:
  - the Emain Macha 211/212 seam trap, affecting the most bots, all Hibernian
  - posts at Odin's Gate (Hib muster in Midgard frontier) that cannot be reached, followed by a pocket escape to the
    capital just before the march
  - one "No connected exterior route" pocket near the Emain Alb muster
- The diagnostic itself hides these cases:
  - A pocket escape counts as progress, because the region changes.
  - The reset means a permanently stuck bot shows up only once every 8 minutes.
  - Nothing is logged at all after the battle starts (Blotiogg was never flagged again after the 14:13:20 battle
    start on 77).

## 2. Captures and siege progress

### Captures (shown)
- The capture log events are:
  - `RVR_KEEP_TAKEN` (`AutonomousRvrDashboard.cs:257`, every keep or tower capture, frontier and battleground)
  - `RVR_EVENT_ENDED` (`AutonomousRvrEventLayer.cs:1778`, with reason "Keep captured: ownership changed" `:1949` or
    "Keep captured: siege ended" `:2028`)
  - `RELIC_CAPTURE` (`AutonomousWorldBotController.Relic.cs:120`)
- **The log has 0 lines of any of the three.** No frontier keep or tower changed hands in 13:20-14:22.

### rvr-keep-31, Caer Caledon (battleground, shown)
BG_KEEP_STATUS every 5.5 minutes, 13:20:40 to 14:19:29:
- Holder: Neutral throughout.
- Doors: `doors=1/3` every time. One door is up, and `doorsUp` names 250000302.
- `doorHealth=0` every time. That field is the minimum over all doors, dead ones included (`AutonomousRvrDashboard.cs:137`),
  so it says nothing about the door still standing.
- Lord: 100%, or 99% once at 13:31:40.
- Guards: 13-17 of 17. Guard and lord levels changed after the restart (1-48/57 became 1-39/46).
- Bots near the keep grew from 0/0/0 at 13:51 to 1/20/1 at 14:19.
- RVR_SIEGE action=hit for keep-31: 98 after the restart.
  - Engine `targetHp` values range from 100 down to 1 and keep resetting.
  - That is the engine's current target (`Siege.cs:266`), not proof of door hp.
  - 12 engines were destroyed in total, 4 of them after the restart.
- BG_RALLY_STATUS shows storms reaching the lord floor without a kill:
  - Midgard atLord=5 at 14:05:23
  - Albion atLord=1 at 14:12:47
  - Midgard atLord=2 at 13:30:52
- Inferred: assaults reach the lord but never hurt it enough to show in the 5-minute status.

### Other keeps (shown)
- **rvr-keep-77:**
  - RVR_SIEGE_STARTED at 14:13:20 by the march leader's arrival (`ReportArmyArrived` `:824`), with present=0/46/0.
  - RVR_SIEGE_PRESENCE at 14:18:26: present=2/33/4.
  - The only siege lines are engine hits, mostly by Midgard defenders: targetHp 2, 90 and 99 at 14:17-14:18. Then
    "no target in firing range" and release.
  - No door-hp line. No capture.
- **rvr-keep-105:** the march started at 14:04:23 and the leader was replaced at 14:05:11. **The battle had not started by
  14:22.** There are no engine lines. The Albion army is fighting the Hibernian defense march in Breifine
  (14:16-14:21 deaths).
- **rvr-keep-32 (battleground):** 3 hits at 14:12-14:16, targetHp 77, 48 and 100.
- **Before the restart:** rvr-keep-50, -54 and -102 had sieges with engine hits (for example keep-50 targetHp=4-5 at
  13:28-13:31). Those events are gone after the restart and logged no end line. Inferred: the in-memory events did not
  survive the restart.
- No log event records frontier door hp over time. Engine `targetHp` is the nearest substitute.

## 3. Does "no approach progress" free bots or end the event?
- **No.** The sweep only logs and resets the timer (`:1712-1717`). The members stay assigned.
- The event is still bounded by other timers, so a siege cannot stay stuck forever:
  - **Muster:** 12 minutes (`HomeMusterMilliseconds`, `:803`), one 5-minute extension when thin (`:1652-1661`, keep not
    relic), then a forced march (`:1663`).
  - **March:** `ExpiresTick` is set to at least march + 60 minutes (`:989-990`, `RealmEventPolicy.cs:19`). When it
    expires (`:1696-1702`), the battle starts if `Ready`. Otherwise the event ends with "Rally failed: insufficient
    physical attackers at the one-hour deadline".
  - **Battle:** it ends after 10 minutes with no attacker at the keep, once attackers have been seen (`:1688-1693`,
    `RepelMilliseconds` `:33`), or at the 4-hour cap (`BattleLifetimeMilliseconds` `:20`, `:1698`).
- **Individual bots** are not freed by this path. A bot in a route pocket (for example Linaelorbel) keeps its siege
  assignment until:
  - its own route recovery or pocket escape fires (which usually throws it to its capital), or
  - the event ends.

  Inferred: it can fail and retry for up to about 60+ minutes before the march deadline ends the event.

## 4. Severity for a public release
- **RVR_RALLY_DELAY itself: known issue (log noise).**
  - It has no gameplay effect.
  - It gives misleading diagnostics: 39 of 50 lines flag bots that are mustering correctly.
  - Cheap fix: in `HandleSiegeMuster` at post, call `ReportTravel(..., arrived: true)`, or skip members present in
    `Mustered`/`DefenseMustered` in the sweep.
- **The underlying route issues: should fix, not a blocker.**
  - The Emain Macha 211/212 seam trap (75 Hibernian bots, sent back to Tir na Nog) and the unreachable posts at Odin's
    Gate (Hibernians sent home just before the march) cost each siege bots and minutes.
  - Both armies still marched with 67 and 63, and fought.
  - These are frontier navigation and seam problems. Per AGENTS.md, do not rebuild navmeshes globally; fix them per spot.
- Not a blocker. No siege stalled forever in the log, and the code bounds every phase.
- The absence of any capture in an hour is consistent with no siege lasting long enough:
  - keep-77 battle 9 minutes old at the end of the log
  - keep-105 not started
  - Caer Caledon lord never damaged

  The Caer Caledon lord result is separate from this question and may deserve its own look.

Evidence is from the owner's PC log of build 43c43aa only. No tests were run for this note.
