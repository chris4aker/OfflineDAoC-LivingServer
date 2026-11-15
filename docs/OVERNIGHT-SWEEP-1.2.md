# The overnight raid sweep for 1.2

One night of `/epictest sweep` on the owner's PC, planned to close the raid bar and run what
[PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md) still owes: Midgard's and Hibernia's missing wins, the big three's re-runs,
both Summoner's Halls, Trollheim and the open-world bosses that were never run. Written 2026-11-15. Nothing on this
page has run yet: the parallel sweep, `add` and `cap=` are **tests only** until a PC log shows them.

Raid code is frozen (owner decision 2). The sweep is a GM test tool, not raid code: this plan changes nothing in the raid
AI, routes, musters, staging, encounter plans or raid settings, and no boss is weakened.

## What the sweep does

`/epictest sweep` (`EpicPlaytestSweep.cs`, the command in `EpicPlaytestCommand.cs`) starts each raid the way
`/epictest start <event> era` does: a forced raid at its period size (the size the owner counts for the bar, decision 6),
its reserved level 50s moved to the realm's muster hub, and from there everything is the game's own (parties, the walk,
staging, the route, the tactics). It runs on the server alone: a locked screen or a lost link to the PC can't stop it.

| Question | Answer |
|---|---|
| Can it be told which raids to run, in what order? | Yes. A comma list (no spaces) runs in the order given. `all` is the game's order, `openworld` the 16 open-world bosses. Darkness Falls always moves to the end. |
| Per realm? Realms side by side? | **New:** `parallel` runs one lane per realm side by side, one raid at a time in each lane. Each realm raids with its own bots, so no two sweep raids ever ask one realm's bots at once. Without `parallel` it is one raid at a time, as before. |
| A long list? | **New:** `/epictest sweep add <list>` adds raids to the running sweep, each to its realm's lane, after those waiting. The night's list is over 800 characters, and the game client's chat line is short, so it is typed as a start and five adds (each under 170 characters; the client's exact limit was not checked). A raid already in the sweep is left out, so typing a line twice does no harm. |
| Time limits? | Each raid ends on its own clocks: won, lost (two failed attempts at one boss), "Route blocked" (20 minutes with nothing reachable), or "Stalled" (60 minutes with no named or boss kill, no new low and no wipe; a sweep raid is a play test raid, so the 60-minute clock applies, not the forced raid's 3 hours). On top, the sweep stops a raid after 4 hours. **New:** `cap=<minutes>` changes that (30 or more), `cap=off` drops it. Caer Sidi was cut off by the 4-hour cap twice (test build 5, and `e3b8238` with 18 named down), so the night uses `cap=360`. |
| Guest seat? | `guest` seats the GM who starts it in each raid. A guest sits in one raid at a time, so a `parallel` sweep refuses it. The night runs bots only, like the sweep on `1dc66b6`. |
| Raid size? | The period size from `AutonomousRealmRaid.Definitions` (`/epictest list`), made whole parties: dragons 72, the big three 100, Crystal Cave 120, Summoner's Hall, Iarnvidiur's Lair, Trollheim and Avalon 64, Tur Suil 40, the open-world bosses 40 to 48. The same as `/epictest start <event> era`. |
| A raid that can't start? | It goes to the end of its lane and is tried again, three times in all (`EPIC_SWEEP_RAID_NOT_STARTED`), then "not run". |
| Shared dungeons? | **New:** Summoner's Hall and Darkness Falls are one dungeon for all three realms. A lane never sends its raid into one while another realm's raid (the sweep's or the realm's own) is inside: it starts its next raid instead and comes back to it (`EPIC_SWEEP_HELD`). On `1dc66b6` Albion's and Midgard's Summoner's Hall raids overlapped. |
| The realms' own raids? | They stay on. A forced start ends a realm's own raid that hasn't gone in yet, and a realm with a raid running opens none of its own, so with all three lanes busy the schedule mostly waits. A raid the game opened itself that finishes counts like any other. |
| How results are logged | `EPIC_SWEEP_*` for the sweep, `REALM_RAID_*` for each raid, `REALM_EXPEDITION_*` for each fight inside a dungeon. All in `runtime\server\logs\server.log` (it rolls at 100 MB and daily into `server.<date>.<n>.log`, kept 3 days: read the morning after, not later). |

## Before the night

1. The game on the test copy (`C:\Games\OfflineDAoC\playable-v0.37b-fresh`) must be a candidate that carries the raid
   fixes (#229, #230, #235, #236, #237, #240: `3d67b6f` or later) **and** this sweep change. The `FORK_BUILD` line at the
   top of `server.log` names the commit: write it down, every result is that build's. Installing a build is the owner's
   step (or Claude's, with the owner's yes, never over a running server).
2. Keep the PC awake all night: Windows sleep stops the server. The screen may lock.
3. Start the server from the launcher and log in with the GM character. Wait **10 minutes** after the server start
   before typing anything, so the realms' level 50s are online: a raid that can't reserve its bots counts a failed try.

## The commands, in order

Type each line in game, as the GM, and wait for its reply before the next.

```
/epictest sweep start dragon-midgard,dungeon-hibernia-tursuil,epic-albion parallel cap=360
/epictest sweep add dungeon-midgard-iarnvidiur,summoners-midgard,epic-midgard,summoners-hibernia,epic-hibernia,summoners-albion
/epictest sweep add dungeon-midgard-trollheim,openworld-albion-greenknight,openworld-albion-emthoro,dungeon-albion-avalon,dungeon-albion-crystal
/epictest sweep add openworld-hibernia-juggernaut,openworld-hibernia-curengkur,openworld-hibernia-marfoirwort,openworld-hibernia-mollunero
/epictest sweep add openworld-hibernia-evern,openworld-midgard-glaciergiant,openworld-midgard-wreth-jotun,openworld-midgard-wreth-arachite
/epictest sweep add openworld-midgard-wreth-spider,openworld-midgard-wreth-scorpion,openworld-midgard-wreth-crab,openworld-midgard-revenant,openworld-midgard-mortufoghus
/epictest sweep status
```

The replies to expect:
- The start: `Sweep started: 3 raids in 3 lanes side by side, one raid at a time in each (Midgard: dragon-midgard;
  Hibernia: dungeon-hibernia-tursuil; Albion: epic-albion); each stopped after 360 minutes at most; bots only.`
- Each add: `Added N raids to their realms' lanes. Waiting now: M.` (6, 5, 4, 4 and 5 raids; 24 waiting after the last,
  or up to 27 if a lane's first raid hasn't started yet).
- The status: `Sweep: 0 done, 24 waiting; now Midgard dragon-midgard (… min); Hibernia …; Albion …; cap 360 min`. The
  lanes start a minute apart.

Then leave the GM where it stands (a GM can't be attacked) or log out: the sweep runs either way. Don't start other
raids by hand during the night.

`UT_EpicPlaytestSweepLanes` checks that these exact lines parse, fit 170 characters and build the lanes below.

## The night's order and how long it takes

Each lane runs its realm's raids in this order: the bar first, then the big three, then the other re-runs, then the
open-world bosses. The times are minutes from the raid's start to its end (muster and walk included), from the PC runs
named; where none exists, an estimate from the dungeon's size. Add 3 minutes between raids.

**Midgard** (the longest lane)

| # | Raid | Why | Estimate | From |
|---|---|---|---|---|
| 1 | Gjalpinulva | the bar (dragon) | 75 | won in 75 with one wipe on `e90bf21` (1.1.3, history, 2026-11-11) |
| 2 | Iarnvidiur's Lair | the bar (second dungeon) | 90 | no timed run (won once, run 2); 11 encounters in the dungeon, one hard rule in the catalog |
| 3 | Summoner's Hall | the bar, if Iarnvidiur's Lair loses | 45 | won in 38 on `e90bf21` (history), lost in 39 on `1dc66b6` |
| 4 | Tuscaran Glacier | big three, re-run owed (#235) | 240 | stopped at Kvasir after 89 on `1dc66b6`; 219 before it stalled on `e3b8238` |
| 5 | Trollheim | re-run owed (stopped before Nosdoden) | 150 | won in 157 on 2026-11-08 (history, an earlier build); 78 to Fostra Orm on `1dc66b6` |
| 6-13 | Glacier Giant, the five Wretches of Winter, Raumarik Revenant, Mortufoghus | open world, never run | 30 each | the crab Wretch won in 20 (muster included) on `1dc66b6`; Coliath's fight itself took about a minute |

Running total: the bar by about 3 hours (3.7 with Summoner's Hall), Tuscaran Glacier done by about 7.7 hours,
Trollheim by about 10.3, the open-world bosses by about 14.7.

**Hibernia**

| # | Raid | Why | Estimate | From |
|---|---|---|---|---|
| 1 | Tur Suil | the bar (second dungeon) | 180 | won in 180 with two wipes, a raid the game opened itself, 2026-11-11 (history; that log has no build line) |
| 2 | Summoner's Hall | re-run owed (#237), and the bar if Tur Suil loses | 75 | lost in 57 on `1dc66b6` |
| 3 | Galladoria | big three, re-run owed (#230) | 180 | stalled after 43 on `1dc66b6` (Broodmother dead at 27); never finished |
| 4-8 | Juggernaut, Curengkur, Marfoirwort, Mollunero, Evern | open world, never run | 30 each | as Midgard's |

Running total: the bar by about 3 hours (4.3 with Summoner's Hall), Galladoria by about 7.4, everything by about 10.1.

**Albion** (its bar is met)

| # | Raid | Why | Estimate | From |
|---|---|---|---|---|
| 1 | Caer Sidi | big three, re-run owed (#236) | 270 | lost in 54 (10 named) on `1dc66b6`; 228 (18 named) on `e17a92b` (1.1.3, history, 2026-11-11); cut by the 4-hour cap on `e3b8238` |
| 2 | Summoner's Hall | re-run owed (#229, #240) | 75 | lost in 58 on `1dc66b6` |
| 3-4 | Green Knight, Spirit of Lord Emthoro | open world, never run | 30 each | as Midgard's |
| 5 | Avalon City | not run on the candidate line | 60 | won once (run 2, no time) |
| 6 | Crystal Cave | not run on the candidate line | 45 | won in 31 and 48 on 2026-11-08 (history, an earlier build) |

Running total: Caer Sidi by about 4.5 hours, everything by about 8.8.

**What fits in 8 to 10 hours:** the whole bar (Gjalpinulva, a second Midgard dungeon, Tur Suil), all three big-three
re-runs, both owed Summoner's Halls and Albion's and Hibernia's open-world bosses, if the estimates hold. Trollheim lands
around 10.3 hours and Midgard's eight open-world bosses don't fit: they are left for the morning or the next evening
(the sweep keeps going until each raid has run; `/epictest sweep stop` ends it). One raid at a time, the same list would
take about 33 hours.

Everything above is an estimate. A raid that loses early frees its lane sooner; one that fights to its cap holds it 6
hours.

## The log lines, per outcome

Every raid ends with one `REALM_RAID_ENDED` line, then the sweep's `EPIC_SWEEP_RAID_DONE` for it:

```
REALM_RAID_ENDED event=<id> outcome="<outcome>" support=… required=… forced=True minutes=… named=… specialForces=… specialForcesJoined=… wipes=… reason="…"
EPIC_SWEEP_RAID_DONE event=<id> minutes=… done=… left=… lane=<realm>
```

| Outcome | It means | Lines before it |
|---|---|---|
| `outcome="Boss defeated"` | **Won.** A dungeon: `reason="The final dungeon encounter has been defeated."` (or "…expedition defeated the final encounter."). A dragon or open-world boss: `reason="The dragon encounter has ended…"` / `"The boss encounter has ended…"` | `REALM_EXPEDITION_TARGET_DEFEATED region=… target="<boss>"` for each named and the final boss; `REALM_RAID_FINAL_CREDIT` when a final boss dies |
| `outcome="Defeated"` | **Lost:** two failed attempts at one boss. `reason="Defeated at <boss> after 2 failed attempts. Named encounters defeated: …"` | `REALM_RAID_WIPE event=<id> boss="…" failures=1` and `=2`; in a dungeon `REALM_EXPEDITION_WIPE … lowest=<boss's lowest %> how="…"`, for a dragon `REALM_RAID_DRAGON_WIPE` |
| `outcome="Timed out"` with `reason="Stalled: …"` | **Stall** (blocks the release): 60 minutes without progress | `REALM_RAID_STALLED event=<id> minutes=60 named=… target="…" lowest=…` |
| `outcome="Route blocked"` | **Stall:** nothing reachable for 20 minutes | `REALM_EXPEDITION_ROUTE_BLOCKED region=… nearest="…"` |
| `outcome="Failed rally"` | **Stall:** the raid never staged | `REALM_RAID_FORCED_PATIENCE`, no `REALM_RAID_STARTED` |
| `outcome="Stopped by player"` | **No result:** the sweep's cap, a skip or a stop | `EPIC_SWEEP_CAP event=<id> minutes=360 cap=360 lane=…` when it was the cap |
| `outcome="Beaten to it"` | another realm killed the final boss first | `REALM_RAID_FINAL_CREDIT event=<other realm's id>` |
| `EPIC_SWEEP_RAID_NOT_STARTED … again=False` | never started (three tries) | its `reason="…"` says why |

The release rule (PLAYED-CHECK-1.2, "The raid bar"): a stall blocks the release, and a big-three loss is acceptable only
when the boss's own mechanics beat a raid that played correctly. So for every loss, read the fight's lines before the
wipe (below) and say what beat it.

## The morning: reading the results

In PowerShell, in the test copy's `runtime\server\logs` folder:

```powershell
cd C:\Games\OfflineDAoC\playable-v0.37b-fresh\runtime\server\logs

# 1. The build and the sweep's own timeline (start, adds, each raid's start and end, holds, caps, the end).
Select-String -Path server*.log -Pattern 'FORK_BUILD|EPIC_SWEEP_' | ForEach-Object Line

# 2. Every raid's result: one line each.
Select-String -Path server*.log -Pattern 'REALM_RAID_ENDED' | ForEach-Object Line

# 3. One raid's fight in order (replace the id and its region: Tuscaran Glacier is epic-midgard, region 160).
Select-String -Path server*.log -Pattern 'event=epic-midgard|region=160 ' |
    Where-Object Line -Match 'REALM_RAID_(STARTED|WIPE|STALLED|ENDED)|REALM_EXPEDITION_(TARGET_DEFEATED|WIPE|TARGET_HEALED|ROUTE_BLOCKED|MUSTER_)' |
    ForEach-Object Line

# 4. Stalls and blocked routes anywhere.
Select-String -Path server*.log -Pattern 'REALM_RAID_STALLED|REALM_EXPEDITION_ROUTE_BLOCKED|REALM_EXPEDITION_MUSTER_STALLED|EPIC_SWEEP_CAP' | ForEach-Object Line

# 5. Server stalls per hour, for the overnight row (owner decision 9: none of 2.0 s or more, at most 5 of 1.0 s or more an hour).
Select-String -Path server*.log -Pattern 'SERVER_STALL worstTickMs=(\d+)' | ForEach-Object {
    [pscustomobject]@{ Hour = $_.Line.Substring(0, 8); Ms = [int]$_.Matches[0].Groups[1].Value } } |
    Group-Object Hour | ForEach-Object {
        '{0}h  minutes with a stall: {1}  of 2 s or more: {2}  worst: {3} ms' -f $_.Name, $_.Count,
        ($_.Group | Where-Object Ms -ge 2000).Count, ($_.Group | Measure-Object Ms -Maximum).Maximum }
```

`SERVER_STALL` is written at most once a minute (the worst tick of that minute, or its GC pause), so step 5 counts
minutes with a stall; it can undercount when one minute held several. Three raids at once is a heavier load than the
night of `1dc66b6`; if the night fails the threshold, that is a finding for the overnight row, not a raid result.

The regions, for step 3: Gjalpinulva 100, Iarnvidiur's Lair 161, Summoner's Hall 248 (all three realms: filter by
`event=` for the raid lines, and by the times the raid ran for the expedition lines), Tuscaran Glacier 160, Trollheim 150,
Tur Suil 190, Galladoria 191, Caer Sidi 60, Avalon City 50, Crystal Cave 62. The open-world bosses' raid lines carry
`event=openworld-…`.

Lines worth a look for the gate's "Raid behaviour" rows that are still tests only:
- Kvasir healed by our pets (#235): `REALM_EXPEDITION_TARGET_HEALED` with `target="Icelord Kvasir"`.
- The frontier walk (#240, Albion's Summoner's Hall): `REALM_RAID_RELEASE_POST post=raid|party|hub`.
- Return waves after a wipe: `REALM_RAID_RETURN_WAVE`. The 70% pull: `REALM_EXPEDITION_PULL_WAIT`, `REALM_EXPEDITION_EARLY_PULL`.
- Epics held while a raid is inside: `REALM_RAID_EPIC_RESPAWN_HELD`. No muster in a boss's aggro (#230, #236):
  `REALM_EXPEDITION_MUSTER_UNSAFE`, `REALM_EXPEDITION_MUSTER_GUARDED`, `REALM_EXPEDITION_MUSTER_MOVED`.

Then `/epictest sweep status` in game lists each raid's line, and `/epictest sweep stop` ends the sweep if it still runs
(the raid running then ends "Stopped by player": no result for it).

Record each result in [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md) with the build commit, the times, named down, wipes and
what beat a lost raid. A result counts only with its `REALM_RAID_ENDED` line in the PC log.

## If something goes wrong

- **A lane sits idle with raids waiting:** `EPIC_SWEEP_HELD lane=… event=… by=…` says which other realm's raid holds the
  shared dungeon; it starts when that one ends. `EPIC_SWEEP_RAID_NOT_STARTED` lines say why a start failed.
- **A raid fights on and on without getting anywhere** (it will stall out after 60 minutes on its own):
  `/epictest sweep skip <event>` ends it and its lane goes on.
- **The server stalls badly with three raids at once:** `/epictest sweep stop`, then start the rest one at a time
  (below).
- **The build on the PC doesn't have this change:** the start then answers `Sweep started: 3 raids, one after another`
  (an older build ignores `parallel` and `cap=`), and `add` only shows the status. Stop it (`/epictest sweep stop`):
  only one line fits, one raid at a time, with the 4-hour cap. The bar and the big three, in order:

  ```
  /epictest sweep start dragon-midgard,dungeon-hibernia-tursuil,dungeon-midgard-iarnvidiur,epic-midgard,epic-albion,epic-hibernia
  ```

  About 17 hours if each runs its estimate; Caer Sidi may be cut by the 4-hour cap again.

## Already in the logs, not yet in the gate

The PC log of `1dc66b6` (2026-11-14, ended 22:07) has a raid the game opened itself on the Wretch of Winter (crab):
`REALM_RAID_ENDED event=openworld-midgard-wreth-crab outcome="Boss defeated"`, 20 minutes, no wipe. The gate's open-world
table still says "Not run". It stays in the night's list (20 minutes) so the result is seen again on the final candidate.
