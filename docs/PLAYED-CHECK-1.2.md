# The 1.2 gate: what has to be seen before Beta 1.2 ships

Owner, 2026-11-12: 1.2 is held until it works properly. This page is the list that decides it. A row counts only when
it has been seen on the owner's PC (test copy, `C:\Games\OfflineDAoC\playable-v0.37b-fresh`) on the release candidate
line: test build 2 (`af3fe271`, installed 2026-11-12 16:20) or a later build. Unit tests, CI and container runs are
"tests only" and never close a row. A result from an earlier build is shown as history; it does not count.

**1.2 ships when** every row marked *must* is "Seen", the raid bar below is met, and the release notes say plainly
which fights the raids still lose.

## The raid bar

Each realm's dragon and two of its dungeons are won on the owner's PC at the game's own raid size (a raid the game
opened itself, or one forced with `/epictest start <event> start`, which uses the same size).

The bar can be met by the smaller dungeons while the three big ones (Caer Sidi, Tuscaran Glacier, Galladoria) still
lose. Their rows below are therefore kept and reported whatever the bar says.

History is the owner's PC on build `3e64140` (runs 1 to 3, 2026-11-11 and 11-12), forced raids. Runs 1 and 2 went in
below the game's size; run 3 at it.

| Realm | Raid | History on 3e64140 | On the candidate line |
|---|---|---|---|
| Albion | Golestandt (dragon) | Lost three times (best 9%) | Not run |
| Albion | Caer Sidi | Lost twice; run 3 ended on a wrongly counted second wipe (fixed in #193, tests only) | Lost on `e3b8238` (sweep, 2026-11-14): 18 named down, the 4-hour sweep cap ended it before the archlich; Crypt Lord 85 of 87 dead, Host walked into the archlich. Reworked in #213/#214 (tests only) |
| Albion | Krondon | Stalled three times at Scurceol Hyrde's room | Not run |
| Albion | Avalon City | Won once (run 2) | Not run |
| Albion | Crystal Cave (Xanxicar) | Won once (run 2, second attempt) | Not run |
| Albion | Hall of the Corrupt | Stopped twice: Aidon's illusions (fixed in #191, tests only) | Not run |
| Albion | Summoner's Hall | No finished run | Never reached on `e3b8238` (sweep, 2026-11-14): the route through Hall of the Corrupt walked into a respawned Aidon, 182 deaths in 50 minutes; skipped by the GM. Route bosses are fought as objectives since #213 (tests only) |
| Albion | Darkness Falls | No finished run (24 named in run 3) | Not run |
| Midgard | Gjalpinulva (dragon) | Won twice (runs 1 and 2, second attempt both times) | Not run |
| Midgard | Tuscaran Glacier | Lost three times (run 3: four of the Council down, Icelord Agmundr at 38%) | Lost on `e3b8238` (sweep, 2026-11-14): 22 named down, stalled after 219 minutes; Agmundr wiped the raid twice (81 of 81 in 2.7 minutes; then a muster placed 76 from him). Reworked in #213/#214 (tests only) |
| Midgard | Trollheim | Lost (run 3 reached Nosdoden; nobody regrouped) | Not run |
| Midgard | Iarnvidiur's Lair | Won once (run 2) | Not run |
| Midgard | Dodens Gruva | Won twice | Not run |
| Midgard | Summoner's Hall | Won once (run 2) | Not run |
| Midgard | Darkness Falls | No finished run | Won on `e3b8238` (a raid the game opened itself, 2026-11-14): 41 named and Legion in one attempt (9 minutes), 132 minutes, no wipe. Before the Darkness Falls raid AI work |
| Hibernia | Cuuldurach (dragon) | Lost run 1, won run 2 (second attempt) | Not run |
| Hibernia | Galladoria | Lost three times at Spindler Broodmother (curers' post in #190, tests only) | Lost on `e3b8238` (sweep, 2026-11-14): two wipes at the Broodmother (15% and 22%), mesmerize caught 23 to 78 of about 104, up to 44 newborns. Reworked in #214 (tests only) |
| Hibernia | Tur Suil | Won twice (runs 1 and 3) | Not run |
| Hibernia | Marfach Caverns | Won twice | Not run |
| Hibernia | Summoner's Hall | Lost three times at Summoner Roesia | Not run |
| Hibernia | Darkness Falls | No finished run | Lost on `e3b8238` (a raid the game opened itself, 2026-11-14): 59 named, then Legion twice (lowest 92% and 60%), 382 minutes |

Back in 1.2 (owner, 2026-11-14: "ALL big bosses are in"): the Darkness Falls raid AI, Legion, Behemoth and every other big boss, the open-world ones included; their rows count like the others.

Known causes with no fix yet: Krondon's Lyft Miht room; raids leaving a boss for the monsters around it (High Lord Oro,
Gatekeeper Dommel, High Lord Baln); Golestandt's second attempts; Summoner Roesia.

## Raid behaviour (test build 2)

| What | Must | Status |
|---|---|---|
| A raid the game opens itself: the boss's tale first, then the call across the screen with the horn (#192) | must | Tests only |
| Raids every four hours per realm (#192) | must | Setting seen in the PC log (150 to 240 on 2026-11-12 16:21); no raid opened yet |
| After a wipe the raiders gather at the way in and go back in together (#195) | must | Not working on `e3b8238`: waves fired, but raiders were released to town and reached the way in over 20 to 30 minutes. Since #213 they release at their staging post outside the dungeon (tests only) |
| A pull waits for 70% of the living raid (#195) | must | Not working on `e3b8238`: the early-pull rule counted only raiders already inside (16 of 18 in a raid of 118). Fixed in #213 (tests only) |
| A boss that resets while the raid still stands costs no attempt (#195) | must | Tests only |
| Raiders walking back still count as the raid when an attempt is scored (#193) | must | Tests only |
| Aidon's illusions leave after every fight (#191) | must | Tests only (one container run) |
| The curers' post at Spindler Broodmother (#190) | must | Used on `e3b8238` and the raid still lost twice; replaced by party posts 700 apart, add parties sized to her spawns and a burn below 25% (#214, tests only) |
| A living boss on a raid's way in is fought as an objective, not walked into (#213) | must | Tests only |
| A raid dungeon's epics don't respawn while its raid is inside (#213) | must | Tests only |
| No muster inside another living boss's aggro (#213) | must | Tests only |
| Agmundr, the Crypt Lord's court, the real Host, Ilron's adds and a perched griffon (#214) |  | Tests only |

## Bots that play with you

No played check of these on the 1.2 line is on record. Each needs one.

| What | Must | Status |
|---|---|---|
| A gamebot leader invites you and you can follow (bot-led groups B1) | must | No record |
| `/lead`, `/lead bot`, `/makeleader`, `/invites` and the join mode (B2) | must | No record |
| A raid leader invites you into a raid (B3) | must | No record |
| Replacements ("lf 1 more"), the natural break and the pull call (B4) | must | No record |
| RvR: bots notice you the way a 2003 player would, not from across the zone | must | No record |
| Replies that don't give bots away; `/lswhy me` |  | No record |
| A battleground session: the realm rallies in the breach and takes the keep lord |  | No record |
| Repeatable quests, deliveries and an epic chain step |  | No record |

## Tales and lore

Tale history is the owner's PC on 2026-11-10 (builds `15fcc70`, `5300a87`), staged with `/lstest tale` unless marked;
details in [TALES.md](TALES.md).

| What | Must | Status |
|---|---|---|
| A tale heard line by line to its end | must | Seen on 5300a87 (history); not yet on the candidate line |
| A tale asked for in plain words | must | Seen on 15fcc70 (history) |
| The storyteller answers a question mid-tale |  | Seen on 15fcc70 and 5300a87 (history) |
| A fireside tale in a resting group |  | Seen on 15fcc70 (history) |
| A tale that starts by itself | must | Not seen |
| A rumour that is a raid really forming |  | Not seen (tests only) |
| Boss yells, atmosphere lines and kill broadcasts on screen | must | Seen in the client on an earlier 1.2 build (history); not on the candidate line |
| Loremasters, books, the Herald chronicle and bestiary |  | No record |

## Install

| What | Must | Status |
|---|---|---|
| A 1.1.4 game updated to the 1.2 candidate through Setup, characters and settings kept | must | Not done |
| A new install of the 1.2 candidate (0.37 from Living Server's own downloads) | must | Not done (1.1.4's Setup did this in CI on 2026-11-12) |
| One overnight run without a stall or an error that stops play | must | Not done |
