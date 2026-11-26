# Raid attempt rows (#644, parts 1-6)

Server-log rows that say, for one pull of a raid boss, who was there, who died, to what, and how it ended. Logging only:
they change no boss, no tactic and no raid rule ("raise a size, never weaken a boss" stands). On by default. No player,
account or bot names; monster names are written. Code: `source/server/GameServer/bots/autonomous/LivingRows.Raid.cs`
(pure `Format*` functions, `RaidAttemptTracker`, `RaidAttempt`); tests: `UT_RaidAttemptRows644`. The fight samples and
the funnel (parts 2-3): `LivingRows.RaidSample.cs` (`RaidFightSampleRows`, `RaidFunnel`); tests: `UT_RaidFightSampleRows644`. Mechanics, threat and role totals (parts 3-5):
`LivingRows.RaidMechanics.cs` (`RaidBossMechanics`, `RaidMechanicRows`, `RaidDiagnosticRows`); tests: `UT_RaidMechanicRows644`.

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

### `RAID_FIGHT_SAMPLE`: every 15 s of a live attempt (parts 2-3)

```
RAID_FIGHT_SAMPLE attempt= t= bossPct= phase= adds= alive=T:H:B:A engaged=T:H:B:A healerPow= healersLow= onTank= tgtKind= medDist= mezzed= stunned=
```

Written for dungeon encounters, dragons and open-world bosses alike, from the attempt's own raiders (the same set
`RAID_DEATH` writes for: no scan of the world) and its boss. The first sample is at the pull (`t=0`), then one every 15 s
while the attempt is live; the raid pulse looks every 5 s, so a sample is at most 5 s late. None after the attempt ends.

| Field | Meaning |
|---|---|
| `t` | seconds into the pull |
| `bossPct` | the boss's health in percent (0 once it is dead); `-` when unknown |
| `phase` | **always `-`**: no boss script exposes its fight phase in a common way (a dragon's flight is out of combat, not a fight phase). Left out rather than guessed |
| `adds` | **always `-`**: only the Green Knight's trees and the ranged-add parties count their adds (`EPIC_GK_TREES`, `EPIC_RANGED_ADDS`), and a general count needs a scan around the boss. Left out rather than guessed |
| `alive` | the attempt's raiders alive and in its region, by role, `tank:healer:buffer:attacker` (the roles of `RAID_ATTEMPT`; a raider with no class is in no slot) |
| `engaged` | of `alive`, those in combat (the game's `InCombat`: hit or hitting in the last seconds, or crowd-controlled) and within 1,500 of the boss |
| `healerPow` | the mean power percent of the living healers (the healer role, the Bard included); `-` with none |
| `healersLow` | living healers under 10% power |
| `onTank` | `1` when the boss's target is a tank of the attempt, `0` when it is anyone else, `-` when it has no target |
| `tgtKind` | whom the boss fights: `tank`, `healer`, `buffer`, `attacker` (a raider of the attempt, by role), `raider` (one with no class), `pet` (a raider's pet), `other` (anyone outside the attempt), `none` |
| `medDist` | the median distance of the living raiders in the region to the boss (the lower middle for an even count); `-` with none |
| `mezzed`, `stunned` | living raiders in the region mesmerized, stunned |

Budget (`LivingRows.Take`): 4 a minute per attempt (tag `RAID_FIGHT_SAMPLE.<attempt>`, forgotten when the attempt ends)
and 30 a minute overall (tag `RAID_FIGHT_SAMPLE`). A refused sample is counted in `RVR_HOUR`'s `dropped=` under the tag
that refused it; the post-mortem shows the gap. Cost: one read of each raider per sample (alive, region, class, distance,
combat, power, mez, stun) into buffers the attempt allocates once and grows only when raiders join.

### `RAID_FUNNEL`: each raid's way to the fight (parts 2-3)

```
RAID_FUNNEL event= stage=start|end asked= recruited= parties= atHub= staged= atBoss= lost=route:n,travel:n,died:n,logout:n,released:n,other:n hubS= stageS= bossS=
```

`stage=start` right after `REALM_RAID_STARTED`, `stage=end` right after `REALM_RAID_ENDED`. A raid that never starts
(a failed rally) writes only the end row. Keyed by `event=` (no attempt).

| Field | Meaning |
|---|---|
| `asked` | the size the raid required (`REALM_RAID_STARTED`'s `required`) |
| `recruited` | the most gamebots the raid had recruited at once (`mostRecruited`) |
| `parties` | the most parties the raid had at once |
| `atHub` | the most of its gamebots at the hub at once before it left the hub |
| `staged` | the most at the staging posts at once (`StagingBest`) |
| `atBoss` | the most raiders in one of the raid's own attempts (its dungeon route's, or its dragon or open-world fight's; a route gate's boss is not the raid's); `-` before any pull |
| `lost` | distinct gamebots per reason, counted from the existing sites (whose per-bot rows stay): `route` had a hub route failure (`REALM_RAID_HUB_ROUTE_FAILED`; some recover), `travel` had a stable travel cancelled (`REALM_RAID_TRAVEL_CANCEL`; it retries), `died` was seen dead at a raid pulse while in no live attempt (on the way in, at staging or between pulls; deaths in a pull are `RAID_DEATH`'s), `logout` was in a party that left the raid and had logged off, `released` was released from its party (no route to the hub, `REALM_RAID_HUB_ROUTE_RELEASED`), `other` was in a party that left the raid for another reason (a member no longer eligible or out of the group). Reasons can overlap (route, then released) |
| `hubS` | seconds from the raid's call to its hub departure; `-` when it never left |
| `stageS` | seconds from the hub departure to the start (to the end for a raid that never started) |
| `bossS` | seconds from the start to the first pull; `-` before any |

No budget: two rows a raid.

### `RAID_MECHANIC_FIRE`: each mechanic of a live attempt (parts 3-5)

```
RAID_MECHANIC_FIRE attempt= boss="" mechanic= t= radius= exposedAtCall= within= hit= died= avoided= called= leadMs= reactMedianMs= damage=
```

One row for every encounter signal of a live attempt's boss (or of a monster in the attempt's region, such as an add's)
and its aftermath, `EPIC_BLAST_LANDED`'s fields as the template. It hears both kinds of signal: those the raid AI reacts to
(`EncounterSignals.Raise`: the dragons, Xanxicar, Olcasgean, Nosdoden, Balor) and the measure-only ones of decision d
(below). The row is written 6 s after the landing (or at the signal's end, if later), or when the attempt ends first.

| Field | Meaning |
|---|---|
| `boss`, `mechanic` | the monster that signalled (a name) and the mechanic's name |
| `t` | seconds into the pull at the signal |
| `radius` | its area; 0 for a mechanic with no area (an adds wave, a summon) |
| `exposedAtCall` | the attempt's living raiders in the area when it was signalled (around its target, or the boss); for a single-target spell, its target; `-` with no area |
| `within` | the same at the landing; `-` with no area, or when the attempt ended before it landed |
| `hit` | raiders hit by a spell of the signalling monster from 0.5 s before the landing to the signal's end (its melee is not counted) |
| `died` | raiders within at the landing or hit, dead at the count |
| `avoided` | raiders exposed at the call, alive, neither within at the landing nor hit; `-` with no area or no landing |
| `called` | `1` when the raid AI was told of it (`Raise`), `0` for a measure-only signal |
| `leadMs` | from the signal to the landing (the warning the raid had; the spell's cast time for a measure-only cast) |
| `reactMedianMs` | the median time the exposed raiders took to step out of the area before the landing (looked at every 100 ms); `-` with no area or when none stepped out |
| `damage` | the damage of those hits |

Budget: 20 a minute (`LivingRows.Take`), on by default.

**Decision d (owner, 2026-11-25; approved again 2026-11-26): measure-only signals.** The raid bosses that raised none now
call `RaidBossMechanics` beside what they already do:

| Boss | Mechanics |
|---|---|
| Legion | `soulBlast` (the soul consumption blast), `adds` (each adds wave) |
| Grand Summoner Govannon | `dot` (the demon form's DoT), `sacrifice` (the 80% sacrifice and demon) |
| Green Knight | `heat` (the heat proc), `heal` (the heal at each walk point), `trees` (each tree wave) |
| Spindler Broodmother | `mezz` (the mez on a random raider), `adds` (each spawn) |
| Queen Kula | `cold` (the cold DD), `frostMezz`, `frostRoot` (the centre rule) |
| King Tuscar | `hammers`, `thunder`, `bleed` (the after-style spells) |

Each `RaidBossMechanics.Cast` takes the script's own `CastSpell(...)` call as its first argument, so the cast, its spell,
its timing and its target are exactly as before; the signal is written only when the cast started. Each signal goes through
`EncounterSignals.Measure`, **not** `Raise`: it is not live for the tactics, the /group calls or the lore engine, so the
raid AI does not react to it and the fight is unchanged. `UT_RaidMechanicRows644` checks each boss's script and the
signal. Making any of them a full `Raise` (so the bots react) would be a tactic change, with its own research and issue.
Other raid bosses with no signal yet: Apocalypse, Orylle, Duraek, Aidon, Iarnvidiur, Aros, the Black Lady, Myrddraxis and
the open-world bosses other than the Green Knight.

### `RAID_THREAT`: each change of the boss's target (diagnostics switch)

```
RAID_THREAT attempt= boss="" t= from= to= toKind= why= heldMs=
```

| Field | Meaning |
|---|---|
| `from`, `to` | whom the boss fought before and now: `tank`, `healer`, `buffer`, `attacker` (a raider of the attempt), `raider` (one with no class), `pet` (a raider's pet), `other`, `none` |
| `toKind` | `player`, `companion`, `gamebot`, `pet`, `npc`, or `-` for none |
| `why` | `death` (the last target died), `taunt` (the new target landed a taunt spell on the boss in the last 3 s; taunt styles count as threat), `mechanic` (a raid-visible signal of the boss was in force), else `threat` |
| `heldMs` | how long the last target held the boss (from the pull for the first change) |

The raid pulse compares the boss's target every game tick. Budget: 20 a minute per attempt (one boss).

### `RAID_ROLE_TOTALS`: per role at each attempt end (diagnostics switch)

```
RAID_ROLE_TOTALS attempt= role= raiders= dealtBoss= dealtAdds= took= healed= overheal= deaths= cc= interrupts= powerOuts= classes=
```

Four rows (tank, healer, buffer, attacker) right after `RAID_ATTEMPT_END`. Budget: 40 a minute.

| Field | Meaning |
|---|---|
| `raiders` | the attempt's raiders of the role (a raider with no class is in no role) |
| `dealtBoss`, `dealtAdds` | damage (with critical damage) the role's raiders and their pets did to the boss, and to every other monster |
| `took` | damage the role's raiders took |
| `healed`, `overheal` | health the role's heal spells restored, and the rest of what they cast (`HealSpellHandler`) |
| `deaths` | every death of the role's raiders in the attempt |
| `cc` | mezzes and stuns the role's raiders put on monsters (counted after the spell's immunity checks; a resist may still follow) |
| `interrupts` | monster casts the role's raiders interrupted |
| `powerOuts` | the role's raiders seen at 0% power at a fight sample (every 15 s; a short power-out between samples is missed) |
| `classes` | per class, `Class:raiders/dealt/healed` |

### The diagnostics switch

`RAID_THREAT` and `RAID_ROLE_TOTALS` are written only with the server property **`raid_diagnostic_rows`** (category
`autonomous_diagnostics`) set to `True`. It is off by default, release worlds write it `False`
(`release-server-settings.json`, `KeptOff`), and player installs never turn it on (#644 decision c). To turn it on in a test
install, set it in the `ServerProperty` table (`UPDATE ServerProperty SET Value = 'True' WHERE Key = 'raid_diagnostic_rows';`
with the server stopped, or `/serverproperties refresh` as an admin after the change). With it off the hit path is part 1's.
With it on, a hit on a monster during a live attempt adds one lookup of the attacker (and its pet's owner).

## Volume [I]

At about 160 raiders and one live raid: `RAID_ATTEMPT` and `RAID_ATTEMPT_END` 2-10 an hour each (budget 10 a minute
each); `RAID_DEATH` 20-300 an hour, up to the raid's size in one minute at a wipe; `RAID_FIGHT_SAMPLE` 240 an hour per
live attempt (at most 1,800 an hour overall); `RAID_FUNNEL` 2 a raid; `RAID_MECHANIC_FIRE` 30-240 an hour per live raid
(at most 1,200); with the diagnostics switch, `RAID_THREAT` 60-600 an hour per boss (at most 1,200 per attempt) and
`RAID_ROLE_TOTALS` 4 an attempt. Not measured.

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
  From parts 2-3 it also gives a **fight curve** per attempt with `RAID_FIGHT_SAMPLE` rows (the boss's health at the start,
  a quarter, half and three quarters into the samples and the last; raiders alive and engaged at the first and last
  sample; the healers' median and lowest power; the peak of healers under 10%; the share of samples with the boss on a
  tank; the median distance; the mez and stun peaks) and a **funnel table** per raid from `RAID_FUNNEL`.
- **`source/tools/raid_postmortem.py <attempt> <logs>`** prints one attempt: the pull, the end, every death in order with its
  second into the pull, the deaths per 15 s, when the first raider and a quarter and half of the pull had died, a
  **timeline** of the fight samples with the deaths between them by second (and a note when samples are more than 20 s
  apart), and every other row carrying the same `attempt=`. `--list` lists the attempts; `--run` picks one pull of an id that a server restart
  reused.
- Both read the rows through one parser (`living_report_sections/_raid.py`). A restart starts the attempt count at 1 again,
  so an id pulled a second time in a log set is a new attempt (`<id> (run 2)`). Tests: `source/tools/tests/test_raid_attempts.py`,
  on synthetic rows; it also parses the rows `UT_RaidAttemptRows644`, `UT_RaidFightSampleRows644` and `UT_RaidMechanicRows644`
  pin for the `Format*` functions, so a change to the rows fails there.
- From parts 3-5 the section adds a **mechanics table** per boss and mechanic (fires, called, exposed, within, hit, died,
  avoided and its share, lead, reaction, damage), and, with the diagnostics rows, a **boss threat** table per attempt
  (changes, the share of time on a tank, why, to whom) and a **roles** table summed over the attempts. The post-mortem puts
  the mechanic fires and target changes in its timeline and the role totals at the end.

## Rows from the sims (#644 part 6)

Two raid simulations with a whole fight write each simulated pull as these rows, and their row attempts end the game's way
(`RealmRaidDungeonRoute.IsWipe`: half of the engaged fallen), so a simulated attempt
reads with the same parser as a logged one and compares with it line by line (docs/RAID-SIZES.md, rule 4: the evidence must
reproduce a PC kill and a PC loss). Tests only: a sim is a model, not a game result.

- **Which sims:** Summoner Roesia (`UT_RaidSim121SummonersGreenKnight.RoesiaFight.Run`), whose model loses the game's way
  (half the raid fallen, deaths at 0 health, no rez), and the Green Knight (`UT_RaidSim122GreenKnight.GreenKnightFight`),
  whose model loses only when no raider is left fighting. For the Green Knight the **row attempt** ends as `wipe` at the
  first step half the raid has fallen, and nothing more is written for it; the model fights on unchanged (its pinned grids
  count its own loss). Each takes an optional `SimRaidRows rows`; without it nothing is written.
- **The same rows:** `SimRaidRows` (`source/server/Tests/UnitTests/SimRaidRows.cs`) writes every row through the server's
  `Format*` functions, and the server's own helpers decide each value they cover: `Role`, `RoleCounts`, `ClassCounts`,
  `Source` (from the last hit in the 5 s before a death), `SplitFallen`, `RaidFightSampleRows.Count` and `TargetKind`,
  `ThreatWhy`, `MechanicOutcome` and `RoleTotals`. The minute budgets are the server's, on the sim's clock. The rows are
  those of a server with the diagnostics switch on (`RAID_THREAT`, `RAID_ROLE_TOTALS`).
- **How a sim's rows differ:** `build=sim` on `RAID_ATTEMPT`. The attempt id is `<event>.<seed + 1>` with the raid's real
  event id (`summoners-albion`, `openworld-albion-greenknight`), so a sim sits under the same event and boss as a logged
  pull. Every raider is a level 50 gamebot; the model's roles stand for one class each (Roesia: the realm's tank, melee,
  caster and healer classes; the Green Knight: Armsman, Mercenary, Sorcerer, Cleric, Wizard, Scout, Cabalist, Theurgist),
  and the row's role is the server's for that class. A pet is never a raider.
- **What a model cannot know is written as unknown, never guessed:** Roesia's model has no positions, so `dist=` and
  `medDist=` are `-`, every living healer counts in `healersIn=`, and every living raider is engaged. Neither model has
  power (`healerPow=-`), crowd control, interrupts or reactions before a landing (`reactMedianMs=-`). `phase` and `adds` are
  `-`, as on the server.
- **Mechanics:** only where the server has a signal. The Green Knight's three measure-only mechanics (`heat`, no cast time;
  `heal`, which names himself; `trees`, no area) are written with `called=0`. The `heat` row's area is the model's own: around
  his target, radius 500, the raiders inside his trees' reach, so its `within` is the set the model hits. The script's spell
  has no range and lands around him; the model's centre differs, which is the model's to fix (#730, reruns owed), not the
  rows'. Roesia has none on the server, so her sim writes none.
- **Outcomes:** `killed`; `wipe` (half the raid fallen, the game's wipe); `stopped` otherwise: the model's clock ran out (15
  minutes for Roesia, 45 for the Green Knight), or the Green Knight model's own loss (no raider left fighting) came before
  half had fallen. The model's own loss is never written as `wipe`.
- **Logging only:** a sim calls the recorder beside what it already does and never draws a random number for it, so its
  results are the same with and without it. `UT_SimRaidRows644` pins that, and that a simulated attempt's rows carry the
  server's fields in the server's order (read with `_util.py`'s key=value rule), join on one id and agree with each other
  and the fight. It also writes `raid-sim/raid-sims.raid.log` (`RAID_SIM_DIR` to move it), which the server job reads with
  **`raid_postmortem.py --check`** (every row's fields in order, a pull and an end per simulated attempt, the death rows
  equal to `deaths=`, `died + left = fallen`) and uploads as the `raid-sim` artifact. `--check` works on a server log too: an
  attempt still open when the log stops is no problem there, and `died + left >= fallen` is enough (a raider who died and was
  rezzed between two looks is in `died` but not in `fallen`). The field lists are `_raid.py`'s `KEYS`, held to the rows
  the C# tests pin.

Not yet (#644): damage and heal totals on the end row (they are in `RAID_ROLE_TOTALS`, with the diagnostics switch),
`rezzed=`, signals for the other raid bosses (above), the other sims (the dragon, Legion, Spindler and Tuscaran models
are no whole fight with the game's loss) and the Grafana panels.
