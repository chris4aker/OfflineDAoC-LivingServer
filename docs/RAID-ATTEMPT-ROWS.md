# Raid attempt rows (#644, part 1)

Server-log rows that say, for one pull of a raid boss, who was there, who died, to what, and how it ended. Logging only:
they change no boss, no tactic and no raid rule ("raise a size, never weaken a boss" stands). On by default. No player,
account or bot names; monster names are written. Code: `source/server/GameServer/bots/autonomous/LivingRows.Raid.cs`
(pure `Format*` functions, `RaidAttemptTracker`, `RaidAttempt`); tests: `UT_RaidAttemptRows644`.

Status: **tests only**. Not yet seen in a PC log.

## The attempt id

Every row of a pull carries `attempt=<event>.<n>`: the raid's event id (`AutonomousRealmRaid.Definition.Id`, for example
`dungeon-albion-krondon`, `dragon-albion`) and the event's attempt count since the server started (1, 2, ...). A raid's
route gate (a boss in another dungeon on the walk in) and its own route count in the same sequence. The count starts
again at 1 when the server restarts, so a post-mortem reads one run of the server.

One grep gives a whole attempt:

```powershell
Select-String -Path server*.log -Pattern 'attempt=dungeon-albion-krondon\.3\b' | ForEach-Object Line
```

The four existing attempt-end rows carry the same field at their end (owner decision b, 2026-11-25); every field before
it is unchanged: `REALM_EXPEDITION_ATTEMPT_ENDED`, `REALM_EXPEDITION_ENCOUNTER_RESET`, `REALM_EXPEDITION_SKIRMISH_LOST`
and `REALM_EXPEDITION_WIPE` (the one written by `RealmRaidDungeonRoute.EndAttempt`). `REALM_EXPEDITION_WIPE`'s other
writer, "raid-wide deaths outside an attempt", has no attempt and is unchanged. `source/tools/tests/test_played_check_log.py`
pins that `played_check_log.py` judges the rows the same with and without the field.

## Rows

### `RAID_ATTEMPT`: each pull

```
RAID_ATTEMPT attempt= event= kind= region= boss="" n= size= period= sizePct= sf= roles=T:H:B:A classes= players= build= [prev= wipeToPullS=] [gatherS=]
```

| Field | Meaning |
|---|---|
| `kind` | `dungeon` (a dungeon route's muster pull, early pull or Leeroy), `dragon` or `open` (a dragon's or an open-world boss's attempt, `RealmRaidDragonFight`) |
| `boss` | the encounter pulled (the route's target; the dragon or open-world boss) |
| `n` | the pull of this boss in this raid: 1 for the first, 2 for the retry |
| `size` | the raid's living members in the boss's region at the pull, the player and companions of its parties included |
| `period` | the raid's period size (`SuggestedBots`); `-` for a route gate's boss, which has none |
| `sizePct` | `size` as a percent of `period`; `-` without one |
| `sf` | Epic Special Forces among `size` |
| `roles` | `size` by role, `tank:healer:buffer:attacker`. Tank: `BotPartyRoles.For` gives Tank (the Paladin included). Healer: a healing class, or the Bard. Buffer: the Minstrel and the Skald. Attacker: every other class |
| `classes` | `size` by class, most first: `Cleric:20,Armsman:12,...` |
| `players` | real players among `size` |
| `build` | the commit, seven characters, or `local` |
| `prev`, `wipeToPullS` | only after a failed attempt (wipe, reset or skirmish) of the same route or fight: its id and the seconds from its end to this pull |
| `gatherS` | the seconds the muster (dungeon), the regroup (dragon retry) or the staging since the hub (first dragon attempt) waited before the pull; left out when unknown |

Raiders rezzed between attempts (`rezzed=`) are not counted yet: that needs a resurrection hook and comes in a later part.

### `RAID_ATTEMPT_END`: each end

```
RAID_ATTEMPT_END attempt= outcome= secs= engaged= fallen= died= left= deaths= health= lowest=
```

| Field | Meaning |
|---|---|
| `outcome` | from the decision already made (`RealmRaidDungeonRoute.EndAttempt`, `RealmRaidDragonFight`), or how the attempt was left: `killed`, `wipe`, `reset` (a free reset, or the encounter reset to full without a counted wipe), `skirmish`, `broke-off` (no wipe: the raid left combat or never entered it, or no damage for five minutes), `route-blocked`, `stalled`, `timeout`, `stopped` (the raid ended for another reason, the route moved to another target, or a new pull began first) |
| `secs` | from the pull to the end |
| `engaged`, `fallen` | the same counts the existing rows give (`fallen`: dead, gone from the region, or walking back after a release, at any look during the attempt) |
| `died` | raiders of the attempt who **really died** in it at least once |
| `left` | raiders counted in `fallen` who never died in it (they left the region or were walking back) |
| `deaths` | `RAID_DEATH` rows of the attempt (a raider who died twice counts twice) |
| `health`, `lowest` | the boss's health now and its lowest in the attempt, in percent |

`-` is an unknown count (an attempt ended by the raid's end has no fallen count for a dragon).

### `RAID_DEATH`: each raider death in a live attempt

```
RAID_DEATH attempt= t= idx= kind= player= class= role= level= killerKind= killer="" source= spellId= mechanic= dist= healersIn= attackers=
```

| Field | Meaning |
|---|---|
| `t` | seconds into the pull |
| `idx` | the raider's index in this attempt (1, 2, ... in the order they joined it): the same raider keeps it, and no name is written |
| `kind`, `player` | `player`, `companion` or `gamebot` (`LivingRows.Kind`); `player=1` for the real player |
| `class`, `role`, `level` | the raider's class, its role as in `RAID_ATTEMPT`, its level |
| `killerKind` | `boss` (the attempt's boss), `add` (another monster), `pet`, `env` (a fall, or the raider itself), `raider` (a player or bot), `none` |
| `killer` | the monster's name; `-` for a raider, a raider's pet or none |
| `source` | from the last hit in the 5 s before the death: `melee`, `spell`, `dot` (damage over time), `aoe` (a spell with a radius), `fall`, `other` (a ranged shot, another hit, or no recent hit) |
| `spellId` | the last hit's spell id, 0 for none |
| `mechanic` | the boss's encounter signals in force (`EncounterSignals.Active`, joined with `+`); `-` when none. Only bosses whose scripts raise signals have any (#644 decision d adds more in a later part) |
| `dist` | distance to the boss; `-` in another region |
| `healersIn` | living healing-class raiders of the attempt within 2,000 |
| `attackers` | how many were on the raider (its attacker tracker) |

Written for every raider in the attempt (those sent in, those who joined during it, and the parties' player and
companions in the region), for every death, with **no cap** during a live attempt (owner decision a). After the attempt
ends no more are written for it. `AUTONOMOUS_BOT_DEATH` is unchanged.

## Volume [I]

At about 160 raiders and one live raid: `RAID_ATTEMPT` and `RAID_ATTEMPT_END` 2-10 an hour each (budget 10 a minute
each); `RAID_DEATH` 20-300 an hour, up to the raid's size in one minute at a wipe. Not measured.

## Cost

The hit hook (`GameLiving.TakeDamage`) checks one empty dictionary outside raids; during a live attempt it does one lookup
per hit and one write per hit on a raider. The death hook (`GamePlayer` and `GameBot.ProcessDeath`) does one lookup per
death and, for a raider, one pass over the attempt's raiders (healers in range).

## Reports (#644 part 6)

- **`living_report.py`'s `raid_attempts` section** (`source/tools/living_report_sections/raid_attempts.py`) joins the three rows
  on `attempt=` and reports, per raid and boss: pulls, outcomes, the kill share, the median size and size against the period
  size, time to kill and to fail, the boss's lowest health on a failed pull, and deaths a pull. Then all deaths by role, kind
  and last hit, the top killers and mechanics, deaths with no healer in range, `died` against `left`, the wait from a failed
  attempt to the next pull (`wipeToPullS`) and the muster before a pull (`gatherS`).
- **`source/tools/raid_postmortem.py <attempt> <logs>`** prints one attempt: the pull, the end, every death in order with its
  second into the pull, the deaths per 15 s, when the first raider and a quarter and half of the pull had died, and every
  other row carrying the same `attempt=`. `--list` lists the attempts; `--run` picks one pull of an id that a server restart
  reused.
- Both read the rows through one parser (`living_report_sections/_raid.py`). A restart starts the attempt count at 1 again,
  so an id pulled a second time in a log set is a new attempt (`<id> (run 2)`). Tests: `source/tools/tests/test_raid_attempts.py`,
  on synthetic rows; it also parses the rows `UT_RaidAttemptRows644` pins for the `Format*` functions, so a change to the rows
  fails there.

Not in part 1 (later parts of #644): `RAID_FIGHT_SAMPLE`, `RAID_FUNNEL`, `RAID_MECHANIC_FIRE`, `RAID_THREAT` and
`RAID_ROLE_TOTALS` (the last two behind the diagnostics switch, decision c), damage and heal totals on the end row,
`rezzed=`, the sims writing these rows, and the Grafana panels.
