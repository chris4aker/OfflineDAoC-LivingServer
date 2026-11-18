# The 1.2 gate: what has to be seen before Beta 1.2 ships

Owner, 2026-11-12: 1.2 is held until it works properly. This page is the list that decides it. A row counts only when
it has been seen on the owner's PC (test copy, `C:\Games\OfflineDAoC\playable-v0.37b-fresh`) on the release candidate
line: test build 2 (`af3fe271`, installed 2026-11-12 16:20) or a later build. Unit tests, CI and container runs are
"tests only" and never close a row. A result from an earlier build is shown as history; it does not count.

**1.2 ships when** every row marked *must* is "Seen", the raid bar below is met, and the release notes say plainly
which fights the raids still lose.

The builds on the candidate line, as the repo records them:
- Test build 2 (`af3fe271`, 2026-11-12) and test builds 3 to 7 (2026-11-12 and 11-13). The draft release notes
  ([RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md), "How it was tested") give results for builds 3 to 7 by number; their
  commits are not recorded in the repo.
- `e3b8238`: the sweep and the raids the game opened itself, 2026-11-14.
- `1dc66b6`: installed 2026-11-14 21:16. It ran the night of 2026-11-14/15 with a GM character, bots only in the sweep
  (no guest seat).
- `3d67b6f` (the integration tip with the Lv.20 button, #242): the battleground test, 2026-11-15.
- Battleground builds of 2026-11-15, each the CI package of an integration tip or PR: `ea882e6` (#247), `3b808b4`
  (#248), `d64d005` (integration `0ae3590`: keep level 1, the lord rally #251, the battleground lore #253) and
  `0026378` (integration `5ab82a2`: a separate rally spot per realm #255, Cathal Valley left out #254).
- `88b6ab3`: sweep 1, 2026-11-16, until the server was stopped at 13:59.
- `ccba364`: installed 2026-11-16 14:01 (`FORK_BUILD` in the PC log), carrying Tur Suil's stall fix (#291). Sweep 3
  started at 14:30: three lanes side by side, cap 480 minutes. (Sweep 2 started at 14:12 and was stopped by the GM at
  14:29 before any raid ended.) It ran until the server stopped at 01:31 on 11-17.
- `19e7037`: installed 2026-11-17 01:33. It carries the open-field RvR work (WP0-WP6, #296 to #302), the Kite stance
  (#303) and the Emthoro muster fix (#293). It ran until a clean stop at 06:15.
- `4be6fab`: installed 2026-11-17 06:18, the RvR regroup fix (#304). The integration tip.

## Owner decisions 2026-11-15

The owner answered the roadmap review's 23 questions at 07:10. Where the owner asked for a recommendation, the
recommendation is recorded as the decision, as the owner asked.

1. **Beta 1.1.4 (main) is merged into 1.2** (a separate pull request). Done in #245, so the two Install musts that use
   Setup no longer wait for it.
2. **Raid code is frozen.** Since #240 (`35d52b6f8`, 2026-11-15 06:49) raid code takes only fixes for bugs seen on the
   owner's PC. Raid code here is the raid AI, routes, musters, staging, encounter plans and raid settings. Each such fix
   re-arms a re-run of every raid it touches, and is listed under "Re-runs owed" below with the raids it re-arms and
   the build that carries it.
3. **Behemoth is never fought: signed off.** Legion's throw still sends raiders to Behemoth's hall, and the raid walks
   back. The raid never fights Behemoth.
4. **Fomor City without a raid: signed off.** It has no path in on its navmesh. Myrddraxis, Anurigunda and Krackenschtein
   keep their plans for the gamebots that fight there.
5. **Morgana without a raid: signed off.** She is the event's host and can't be attacked; she has no spawn in the world
   database.
6. **`/epictest sweep` raids count toward the raid bar** at the game's own raid size (the question asked about sweeps
   with the GM's guest seat).
7. **Leeroy stays** in play-test and sweep raids. A raid he rolled on counts like any other.
8. **A class-off smoke session before tagging** (the owner asked for a recommendation: yes). New *must* row under
   Install.
9. **The overnight row's stall threshold** (recommendation): no game-loop stall of 2.0 s or more, and at most 5 stalls
   of 1.0 s or more in any hour, over an 8-hour run. See Install.
10. **The L600 baseline comes after 1.2** (recommendation). For 1.2 the overnight row with the stall threshold stands
    in for it ([RELEASE-TRAIN.md](RELEASE-TRAIN.md)).
11. **The Beta 1.0 items never seen are to be seen first.** They are *must* rows now (section below).
12. **The purity rules R0-R10** ([PR #58](https://github.com/chris4aker/OfflineDAoC-LivingServer/pull/58), never
    merged): **not confirmed.** The owner asked for a recommendation per rule first.
13. **The economy ledger** (recommendation): restored as 2.0's first milestone, because 2.0's tax and crafting rules
    depend on it. Nothing in 1.2.
14. **The RvR and battleground kill quests (#243) stay**, and the Caledonia quests follow the 30-35 bracket (a separate
    code change). Both are tests only until seen.
15. **A player-called realm raid** (recommendation): 1.2.x. A player command calls their own realm's next raid, with
    the existing cooldowns.
16. **The new client (0.37c) is maintenance-only.** This gate has no new-client rows.
17. **Storytellers, fireside tales, loremasters and the Epic Special Forces are teased features.** The release notes
    hint at them without explaining them or naming the eggs.
18. **Post-2003 jokes are cut** (a separate change to the lines).
19. **Era scope:** Trials of Atlantis and Catacombs appear only as rumours, from a lore perspective, never as played
    content ([bot-voices/WRITING-GUIDE.md](bot-voices/WRITING-GUIDE.md)).
20. **The small chat-only Easter eggs** (recommendation): 1.2.x, after the raid freeze and this gate.
21. **Eggs that change behaviour are allowed** (loot rolls, the debt repaid, nemesis hunts). Planned for 2.0 unless
    noted.
22. **The five-egg sealed file is dropped** (recommendation). Good ideas go into the idea backlog
    ([RELEASE-TRAIN.md](RELEASE-TRAIN.md)).
23. **Rage quits keep ignoring the schedule's `off`.**

## Owner decisions 2026-11-17

The owner answered at 06:47: all as recommended.

1. **One final build at about 11:00, then a freeze.** Bugs found after it go in the release notes as known issues,
   unless they stop play.
2. **Midgard runs in two tracks.** The sweep lane takes Midgard's dungeons. Its eight open-world raids are forced beside
   it with `/epictest start <raid> start`.
3. **The install checks run this morning on `4be6fab`'s Setup.** They count for the final build if the 11:00 build
   changes no Setup, patcher, launcher or install file.
4. **Tuscaran Glacier's and Summoner's Hall (Hibernia)'s losses count** if the log review shows a correct raid.
5. **Crafting and open-field RvR get must rows** (the two sections below).
6. **Darkness Falls (Midgard) stays out of the sweep.** Its win on `e3b8238` stands.
7. **A raid's own tale closes "A tale that starts by itself"** (seen on `1dc66b6`).
8. **If the gate isn't met tonight, 1.2 ships tomorrow**, not with a must row open.

## The raid bar

Each realm's dragon and two of its dungeons are won on the owner's PC at the game's own raid size (a raid the game
opened itself, one forced with `/epictest start <event> start`, which uses the same size, or a raid of
`/epictest sweep`, owner 2026-11-15).

The bar can be met by the smaller dungeons while the three big ones (Caer Sidi, Tuscaran Glacier, Galladoria) still
lose. Their rows below are therefore kept and reported whatever the bar says.

Settled 2026-11-14 (the owner asked for it to be fixed): **a win in one of the big three is not required to ship.**
Each of the three must be run on the release candidate at the game's own raid size, and the release notes say how
each went: won, or how far it got and what beat it. No boss is weakened to get a win. A raid that stalls (never
reaches its final boss, or stops fighting, for a route or code reason rather than a lost fight) blocks the release
like any *must* row, big three included.

Sharpened 2026-11-15 (owner): a big-three loss is acceptable only when the boss's own mechanics beat a raid that
played correctly. A loss traced to a bot, route or tactic bug (the raid healing the boss, a muster or pull the plan got
wrong, a route that cannot reach the fight) blocks the release like a stall until it is fixed **and the raid is run
again** on a candidate with the fix. On build `1dc66b6` (2026-11-15) all three big raids hit such bugs: Galladoria
stalled behind Easmarach's falls (#230), our Spiritmasters' pets healed Kvasir in Tuscaran Glacier (#235), and Caer
Sidi's plans at Dorinakka and the Crypt Lord were wrong (#236). Each is to be re-run on the final candidate.

**Where the bar stands** (2026-11-17, from the rows below):
- **Albion: met on the candidate line.** Golestandt won on test build 5; Hall of the Corrupt and Krondon won on
  `1dc66b6`. Both dungeons' raid code changed after their wins (#229 for Aidon, #239 for Krondon), before the freeze.
  Avalon City and Crystal Cave also won on `ccba364`.
- **Midgard: met on the candidate line.** Gjalpinulva and Iarnvidiur's Lair won on `88b6ab3`; Dodens Gruva won on
  test build 5 (and lost on `1dc66b6`).
- **Hibernia: met on `ccba364`.** Cuuldurach won on test build 5 and Marfach Caverns on builds 2, 4 and 5. Tur Suil won
  in sweep 3 on `ccba364` (17:18, no wipe). One caveat: a Tur Suil raid the game opened itself on `19e7037` stalled
  (04:33, no named kill). A stall blocks the release like a must row until it is explained or fixed.

**The night that should close it:** [OVERNIGHT-SWEEP-1.2.md](OVERNIGHT-SWEEP-1.2.md) gives the commands, in order (one
lane per realm side by side: the bar first, then the big three's re-runs, the other re-runs and the open-world bosses),
the time each raid is expected to take, the log lines for a win, a loss and a stall, and how to read the results the
next morning. Its sweep options are **seen** on `ccba364`: sweep 3 ran three lanes side by side with cap 480
(`EPIC_SWEEP_START … lanes=3 cap=480`, 14:30), and `add` queued the rest (`EPIC_SWEEP_ADD`, 14:30-14:32).

**Sweep 3 on `ccba364`** (2026-11-16 14:30 to 11-17 01:31, bots only, from `REALM_RAID_ENDED` in the PC log):
- Won: Tur Suil (17:18), Avalon City (18:56), Mollunero (19:01), Crystal Cave (19:50), Evern (19:53).
- Lost: Marfoirwort (18:40), Green Knight (20:23), Tuscaran Glacier (20:41), Summoner's Hall Hibernia (20:45), Darkness
  Falls Albion (23:07).
- No result: Trollheim timed out (01:12). Emthoro failed its rally (16:03). Curengkur was given up after 3 tries (no
  route). Caer Sidi, Darkness Falls Hibernia and Summoner's Hall Midgard were cut off by the restart at 01:31.
- Not reached before the restart: Albion's Summoner's Hall, Dodens Gruva, Darkness Falls Midgard and Midgard's eight
  open-world raids. Midgard's lane spent 365 minutes in Tuscaran Glacier and 264 in Trollheim, so the open-world raids
  now run beside the sweep (owner decision 2 of 2026-11-17).

History is the owner's PC on build `3e64140` (runs 1 to 3, 2026-11-11 and 11-12), forced raids. Runs 1 and 2 went in
below the game's size; run 3 at it. Results for test builds 2 to 7 come from the draft release notes (l.237-250),
which give one build for the three losses on their l.246-247.

| Realm | Raid | History on 3e64140 | On the candidate line |
|---|---|---|---|
| Albion | Golestandt (dragon) | Lost three times (best 9%) | **Won on test build 5** (second attempt; a raid leader's invite seated the player). Not run since |
| Albion | Caer Sidi | Lost twice; run 3 ended on a wrongly counted second wipe (fixed in #193, tests only) | Lost on test build 5: cut off by the old four-hour limit with 12 named down. Lost on `e3b8238` (sweep, 2026-11-14): 18 named down, the 4-hour sweep cap ended it before the archlich; Crypt Lord 85 of 87 dead, Host walked into the archlich. Reworked in #213/#214 (tests only). **Lost on `1dc66b6`** (sweep, bots only, the GM in the raid, 2026-11-15 02:09-03:04): Lich Lord Ilron killed (02:43), one wipe at Warlord Dorinakka and his 12 crypt knights (02:50, his lowest 96%), then two at the Crypt Lord (03:00 and 03:04, 99%; raid-wide deaths to his court and patrol); 54 minutes, 10 named. Plans fixed in #236 (tests only). **Re-run owed** **Lost on `88b6ab3`** (sweep, bots only, 2026-11-16 10:31-12:45), carrying #236: two wipes at the Crypt Lord, 130 minutes, 11 named (Lich Lord Ilron among them), 24 Epic Special Forces joined. **No result on `ccba364`** (sweep 3): queued at 23:10, the assault started at 00:26 with 70 of the 80 required present, and the restart at 01:31 cut it off. **Re-run owed** |
| Albion | Krondon | Stalled three times at Scurceol Hyrde's room | **Won on `1dc66b6`** (sweep, forced at the game's size, the GM in the raid, 2026-11-15 04:03): Drihten Elreden, Lurfos the Herald and Orylle, no wipe, 54 minutes, 63 raiders. A slow re-clear of respawning ogres after Drihten (13 minutes) is changed in #239 (tests only) |
| Albion | Avalon City | Won once (run 2) | **Won on `ccba364`** (sweep 3, bots only, 2026-11-16 16:06-18:56): 167 minutes, 5 named, one wipe at Dura'ek the Empowered (18:45), 24 Epic Special Forces joined. Left out of later sweeps |
| Albion | Crystal Cave (Xanxicar) | Won once (run 2, second attempt) | **Won on `ccba364`** (sweep 3, bots only, 2026-11-16 18:59-19:50): 49 minutes, one wipe at Xanxicar (19:31, 120 of 120 engaged fell), then won; 24 Epic Special Forces joined. Left out of later sweeps |
| Albion | Hall of the Corrupt | Stopped twice: Aidon's illusions (fixed in #191, tests only) | **Won on `1dc66b6`** (a raid the game opened itself, its tale first, the GM in the raid, 2026-11-15 00:44): Aidon in one attempt, 71 minutes, 5 named, no wipe. A second raid there (05:41) held Aidon out of his tether, immune at 100% for 35 minutes and more: the lead-home bug, fixed in #229 after `1dc66b6` (tests only) |
| Albion | Summoner's Hall | No finished run | Never reached on `e3b8238` (sweep, 2026-11-14): the route through Hall of the Corrupt walked into a respawned Aidon, 182 deaths in 50 minutes; skipped by the GM. Route bosses are fought as objectives since #213 (tests only). **Lost on `1dc66b6`** (sweep, bots only, 2026-11-14 22:40-23:31): the raid mustered for Aidon on its way in and lost to him twice in 58 minutes. Its healers dragged him past his tether and he healed 58% to 100% (#229); raiders walking through the Pennine Mountains were killed by RvR gamebots (92 deaths; #240). Both fixes are tests only. **Re-run owed** **Lost on `88b6ab3`** (sweep, bots only, 2026-11-16 12:49-13:40), carrying #229 and #240: two wipes at Aidon, 50 minutes, 24 Epic Special Forces joined. Queued in sweep 3 on `ccba364` (added 20:31) and not reached before the restart at 01:31. **Re-run owed** |
| Albion | Darkness Falls | No finished run (24 named in run 3) | **Lost on `ccba364`** (sweep 3, bots only, 2026-11-16 20:26-23:07): 158 minutes, 8 named (Prince Abdin at 22:00 among them), lost at Chthonic Knight Ukobat. Both wipes were counted with almost nobody dead: 0 fallen (22:55) and 2 fallen (23:07) of 54 and 68 engaged. The log also has "Chthonic Knight Ukobat" defeated 15 times from 20:58 to 23:04. Under investigation as a false wipe |
| Midgard | Gjalpinulva (dragon) | Won twice (runs 1 and 2, second attempt both times) | **Won on `88b6ab3`** (sweep, bots only, 2026-11-16 10:31-11:24): 52 minutes, no wipe, 72 raiders. Left out of later sweeps |
| Midgard | Tuscaran Glacier | Lost three times (run 3: four of the Council down, Icelord Agmundr at 38%) | Lost on `e3b8238` (sweep, 2026-11-14): 22 named down, stalled after 219 minutes; Agmundr wiped the raid twice (81 of 81 in 2.7 minutes; then a muster placed 76 from him). Reworked in #213/#214 (tests only). **Stopped on `1dc66b6`** (sweep, bots only, 2026-11-15 00:35-02:06): Icelord Agmundr killed (01:30, 10.3 minutes), then Icelord Kvasir held at 81-99% for half an hour: our Spiritmasters' pets' cold damage healed him (on screen: "thank you spirit champion for healing me"). Skipped by the GM. Fixed in #235 (tests only). **Re-run owed** Started on `88b6ab3` (sweep, 13:39) and cut short when the server was stopped at 13:59 to install `ccba364`: no result. **Lost on `ccba364`** (sweep 3, bots only, 2026-11-16 14:31-20:41) at King Tuscar: 365 minutes, 40 named, 7 wipes (Agmundr, Skuf, Suttung, Jailer Vifil, Queen Kula, then King Tuscar twice), 24 Epic Special Forces joined. Every Icelord (Agmundr, Kvasir, Hakr, Steinvor, Skuf, Elder Icelord Suttung) and the Council (Guthlac, Birghir, Hord, Otrygg, Vagn, Nokkvi) were killed. Icelord Kvasir died in 7.3 minutes (15:53): the first sign that #235 works. On `19e7037` a raid the game opened itself (04:04) killed Kvasir in 5.3 minutes (05:02) and reached Suttung (06:01) before the stop at 06:15: no result. The loss counts if the log review shows a correct raid (owner decision 4 of 2026-11-17) |
| Midgard | Trollheim | Lost (run 3 reached Nosdoden; nobody regrouped) | Lost at Nosdoden on test build 5. **Stopped by the owner on `1dc66b6`** (sweep, 2026-11-15 05:23-06:28) after 78 minutes, before Nosdoden, to test battlegrounds on a new build: no wipe, Fostra Orm killed, 131 targets down. No result; not finished on the candidate line. **Stalled on `ccba364`** (sweep 3, bots only, 2026-11-16 20:45 to 11-17 01:12): ended "Timed out" after 264 minutes with 8 named (Fostra Orm and The Rift among them) and one wipe at Nosdoden (00:12); no kill, no new low and no wipe for 60 minutes after it. Under investigation |
| Midgard | Iarnvidiur's Lair | Won once (run 2) | **Won on `88b6ab3`** (sweep, bots only, 2026-11-16, ended 13:36): 127 minutes, 10 named, no wipe, 64 raiders. Left out of later sweeps |
| Midgard | Dodens Gruva | Won twice | **Won on test build 5.** **Lost on `1dc66b6`** (a raid the game opened itself, 2026-11-15, bots only): two wipes at Aros the Spiritmaster, 103 minutes, the Epic Special Forces joined; the defeat line was seen in the client (01:24). On `ccba364` a raid the game opened itself ended "Stopped by player" after 3 minutes (01:15), with the log's reason "superseded by the forced summoners-midgard expedition" (the sweep's next raid). Under investigation. Not in a sweep since |
| Midgard | Summoner's Hall | Won once (run 2) | **Lost on `1dc66b6`** (a raid the game opened itself, 2026-11-14 23:16-23:44, bots only): the client showed "driven out of Summoner's Hall by Summoner Roesia". Before #229 that line named the raid's final boss rather than the one that beat it, so where it was lost is not certain. On `ccba364` the sweep sent it at 01:15; the restart at 01:31 came before its assault: no result |
| Midgard | Darkness Falls | No finished run | Won on `e3b8238` (a raid the game opened itself, 2026-11-14): 41 named and Legion in one attempt (9 minutes), 132 minutes, no wipe. Before the Darkness Falls raid AI work. Stays out of the sweep on this win (owner decision 6 of 2026-11-17) |
| Hibernia | Cuuldurach (dragon) | Lost run 1, won run 2 (second attempt) | **Won on test build 5** (a raid the game opened itself). Not run since |
| Hibernia | Galladoria | Lost three times at Spindler Broodmother (curers' post in #190, tests only) | Spindler Broodmother killed, then lost at the Organic-Energy Mechanism, on test build 5. Lost on `e3b8238` (sweep, 2026-11-14): two wipes at the Broodmother (15% and 22%), mesmerize caught 23 to 78 of about 104, up to 44 newborns. Reworked in #214 (tests only). **Stalled on `1dc66b6`** (sweep, bots only, 2026-11-14 23:49 to 11-15 00:32): Spindler Broodmother killed in 12.7 minutes with #214's party posts (00:16), then the final muster was placed behind Easmarach's falls while he lived and the raiders bounced off them 165 times for 15 minutes. Skipped by the GM. Fixed in #230 (tests only). **Re-run owed** **Lost on `88b6ab3`** (sweep, bots only, 2026-11-16 11:51-12:50), carrying #230: two wipes at Spindler Broodmother, 57 minutes, 8 named, 24 Epic Special Forces joined. **No result on `ccba364`**: a raid the game opened itself (22:44, 128 present) killed Spindler Broodmother in 4.0 minutes (23:01), the Organic-Energy Mechanism (23:34), Aroon the Urlamhai, Xaga, Hurionthex and the three Corrupt Primals (the last at 01:11), with no wipe, and was cut off by the restart at 01:31 (`REALM_EXPEDITION_TARGET_DEFEATED`, region 191). Galladoria was not queued in sweep 3. **Re-run owed** |
| Hibernia | Tur Suil | Won twice (runs 1 and 3) | **Stalled on `88b6ab3`** (sweep, bots only, 2026-11-16 10:31-11:48): ended after 75 minutes with no named kill and no wipe, because the stall clock ran while the raid cleared the first hour of trash. Fixed in #291 (the owner lifted the raid freeze for this fix, 2026-11-16; tests only). **Won on `ccba364`** (sweep 3, bots only, 2026-11-16 14:30-17:18): 166 minutes, 6 named, no wipe. #291 seen working. Left out of later sweeps. **Stalled on `19e7037`**: a raid the game opened itself (02:33, 28 present) ended "Timed out" at 04:33 after 139 minutes with no named kill and no wipe. Under investigation |
| Hibernia | Marfach Caverns | Won twice | **Won on test builds 2, 4 and 5** |
| Hibernia | Summoner's Hall | Lost three times at Summoner Roesia | **Lost on `1dc66b6`** (sweep, bots only, 2026-11-15, lost at 05:05): two wipes at Summoner Lossren (lowest 63%, then 20%), 57 minutes, 24 Epic Special Forces joined. Her tortured souls killed 98 of 170 raiders: she called a new wave whenever none lived. Fixed in #237 (keep one soul alive and mesmerized). **Lost on `ccba364`** (sweep 3, bots only, 2026-11-16 19:57-20:45) at Summoner Roesia: Summoner Lossren killed in 9.7 minutes (20:29), so #237 is seen working; then two wipes at Roesia (20:31 and 20:45), 47 minutes, 24 Epic Special Forces joined. The loss counts if the log review shows a correct raid (owner decision 4 of 2026-11-17) |
| Hibernia | Darkness Falls | No finished run | Lost on `e3b8238` (a raid the game opened itself, 2026-11-14): 59 named, then Legion twice (lowest 92% and 60%), 382 minutes. On `1dc66b6` a raid the game opened itself (01:39, 136 raiders) was still running at 05:21 with 69 targets down and no wipe; its end is not recorded. **No result on `ccba364`** (sweep 3): its assault started at 23:23 and the restart at 01:31 cut it off |

Back in 1.2 (owner, 2026-11-14: "ALL big bosses are in"): the Darkness Falls raid AI, Legion, Behemoth and every other big boss, the open-world ones included; their rows count like the others.
Since 2026-11-15 three of them are signed off as exceptions (owner decisions 3 to 5): Behemoth is never fought, and
Fomor City and Morgana have no raid. They have no row here, and the release notes say so.

Known causes with no fix yet: Krondon's Lyft Miht room; raids leaving a boss for the monsters around it (High Lord Oro,
Gatekeeper Dommel, High Lord Baln); Golestandt's second attempts; Summoner Roesia.

### The open-world raids

Sixteen raids since 1.2 ([RAID-SIZES.md](RAID-SIZES.md), "The open-world big bosses"). Their rows count like the
others (owner, 2026-11-14): the stall rule applies, and the release notes say how each went. `/epictest sweep start
openworld` runs them alone. Not yet in the table: the PC log of `1dc66b6` has a win on the Wretch of Winter (crab) by a
raid the game opened itself (`REALM_RAID_ENDED … outcome="Boss defeated"` at 2026-11-14 22:07, 20 minutes with its
muster, no wipe); the overnight sweep runs it again ([OVERNIGHT-SWEEP-1.2.md](OVERNIGHT-SWEEP-1.2.md)). Midgard's
eight were queued in sweep 3 on `ccba364` and never reached before the restart. They now run beside the sweep, forced
with `/epictest start <raid> start` (owner decision 2 of 2026-11-17).

| Realm | Raid | On the candidate line |
|---|---|---|
| Albion | Coliath | **Won on `1dc66b6`** (a raid the game opened itself, the GM in the raid, 2026-11-15): called 04:03, Coliath killed 04:57, Albion's first kill of him, on screen. 27 raiders died to the monsters in Aldland on the way. **Won again on `19e7037`** (a raid the game opened itself, bots only, 2026-11-17 02:32): 38 minutes with its muster, no wipe |
| Albion | Green Knight | Started on `88b6ab3` (sweep, 13:43) and cut short when the server was stopped at 13:59: no result. **Lost on `ccba364`** (sweep 3, second try, bots only, 2026-11-16 19:53-20:23): two wipes (20:12 and 20:23), 29 minutes, 24 Epic Special Forces joined |
| Albion | Spirit of Lord Emthoro | **Failed rally on `ccba364`** (sweep 3, 2026-11-16 14:32-16:03): 8 of 40 arrived after 90 minutes ("Staging failed: 8 adventurers arrived"). The muster now starts at Fort Gwyntell (#293, carried by `19e7037` and later; tests only). Not run since |
| Midgard | Glacier Giant | Not run |
| Midgard | Wretch of Winter (jotun) | Not run |
| Midgard | Wretch of Winter (arachite) | Not run |
| Midgard | Wretch of Winter (spider) | Not run |
| Midgard | Wretch of Winter (crab) | Not run |
| Midgard | Wretch of Winter (scorpion) | Not run |
| Midgard | Raumarik Revenant | Not run |
| Midgard | Mortufoghus | Not run |
| Hibernia | Evern | **Won on `ccba364`** (sweep 3, bots only, 2026-11-16 19:04-19:53): 48 minutes, one wipe (19:34), then won; 24 Epic Special Forces joined. Left out of later sweeps |
| Hibernia | Juggernaut | **Won on `88b6ab3`** (sweep, bots only, 2026-11-16, ended 13:13): 19 minutes, no wipe, 40 raiders. Left out of later sweeps |
| Hibernia | Curengkur | Not started on `88b6ab3` (sweep, 2026-11-16 13:16): "No complete connected hub-to-encounter route was found", so no party was sent. **The same on `ccba364`** (sweep 3, 18:43, 20:48 and 20:49): three tries, the same reason, then given up. Under investigation |
| Hibernia | Marfoirwort | Started on `88b6ab3` (sweep, 13:16) and cut short when the server was stopped at 13:59: no result. **Lost on `ccba364`** (sweep 3, bots only, 2026-11-16 17:21-18:40): two wipes (17:45 and 18:40), 77 minutes, 24 Epic Special Forces joined |
| Hibernia | Mollunero | **Won on `ccba364`** (sweep 3, bots only, 2026-11-16 18:44-19:01): 17 minutes, no wipe, 40 raiders. Left out of later sweeps |

### Re-runs owed

Each raid below has to be run again on a candidate that carries its fix. A fix made after the freeze (owner decision 2)
is added here, with the build that carries it, before it is merged.

| Raid | Why | Fix (tests only) | Carried by | On the owner's PC since |
|---|---|---|---|---|
| Caer Sidi | Lost to wrong plans at Dorinakka and the Crypt Lord (`1dc66b6`) | #236 | `3d67b6f` and later | Lost on `88b6ab3` at the Crypt Lord; cut off by the restart on `ccba364`. Still owed |
| Tuscaran Glacier | Kvasir healed by our own pets (`1dc66b6`) | #235 | `3d67b6f` and later | **#235 seen** on `ccba364`: Kvasir died in 7.3 minutes (and in 5.3 on `19e7037`). The raid lost at King Tuscar; the loss counts if the log review shows a correct raid |
| Galladoria | Stalled behind Easmarach's falls (`1dc66b6`) | #230 | `3d67b6f` and later | Lost on `88b6ab3` at the Broodmother; on `ccba364` a raid the game opened itself reached the Corrupt Primals and was cut off by the restart. Still owed |
| Summoner's Hall (Albion) | Lost at Aidon after he was dragged out of his tether; frontier walk (`1dc66b6`) | #229, #240 | `3d67b6f` and later | Lost on `88b6ab3` at Aidon; not reached on `ccba364`. Still owed |
| Summoner's Hall (Hibernia) | Lost to Lossren's endless waves (`1dc66b6`) | #237 | `3d67b6f` and later | **#237 seen** on `ccba364`: Lossren killed in 9.7 minutes. The raid lost at Roesia; the loss counts if the log review shows a correct raid |
| Trollheim | Stopped by the owner before Nosdoden (`1dc66b6`); not finished on the candidate line | none | | Timed out on `ccba364` after a wipe at Nosdoden. Under investigation |
| Tur Suil | Stalled while clearing its first hour of trash (`88b6ab3`) | #291 | `ccba364` and later | **#291 seen**: won in sweep 3 on `ccba364`, no wipe. A raid the game opened itself stalled on `19e7037` (below) |
| Spirit of Lord Emthoro | Failed rally: 8 of 40 arrived (`ccba364`) | #293 | `19e7037` and later | Not run since |
| Galladoria | Aroon and Xaga's room never pulled as an attempt; one fight in Xaga's room scored as four wipes; Aroon set aside mid-fight (`5378dbe`) | #336 (adds only inside an attempt; wipes per encounter; stands on the boss's spawn floor) | not yet on a build | Owed: a `FINAL_ENGAGE` or `EARLY_PULL` for Aroon and Xaga before their first required-add `REALM_EXPEDITION_TACTIC`, and at most two wipes counted at Xaga |
| Caer Sidi | Two full wipes at the Crypt Lord scored "killed; it came back since" (`5378dbe`): a move home taken for a respawn | #336 (kill needs a seen death) | not yet on a build | Owed: seen when a wipe at a boss moved home is scored `REALM_EXPEDITION_WIPE`, and a real respawn still as a win |
| Tuscaran Glacier | Fornfrusenen's retry after a wipe raised no shards and he died to melee in 2.2 minutes (`b1b6846`, 09:15:44): healed to full without the reset that re-arms them | `fix/sweep6b-raids` (re-arm on a heal to full with no shard alive) | not yet on a build | Owed: a retry on him with an `addsSpawn` lore line and "shards first" retargets; the checker's "A retry of a boss that raises adds raises them again" row Seen |
| Galladoria | Aroon's attempts 1-3 ended "never entered combat" between his Scaiths (14, 35 and 19 fallen) and he was set aside as unreached; Easmarach set aside 9 s after he became the target by a 105-minute-old muster record (`b1b6846`) | `fix/sweep6b-raids` (an add fight is the attempt's contact and restarts the grace; old muster records dropped) | not yet on a build | Owed: Aroon killed with no `ATTEMPT_ENDED how="never entered combat"` at 10 or more fallen (the checker's row), and no `MUSTER_STALLED` in a new muster's first 20 minutes |

Under investigation from `ccba364` and `19e7037` (no fix yet):
- **Darkness Falls (Albion), a false wipe?** Both wipes at Chthonic Knight Ukobat were counted with 0 and 2 raiders
  dead (`ccba364`, 22:55 and 23:07).
- **Trollheim timed out** after 264 minutes, 60 minutes after its wipe at Nosdoden (`ccba364`, 01:12).
- **Dodens Gruva "Stopped by player"** after 3 minutes, superseded by the sweep's Summoner's Hall Midgard (`ccba364`,
  01:15).
- **Curengkur has no route:** "No complete connected hub-to-encounter route was found", three tries (`ccba364`), as on
  `88b6ab3`.
- **Tur Suil timed out** in a raid the game opened itself, with no named kill (`19e7037`, 02:33-04:33).

### Won on the owner's PC: left out of later sweeps

Owner, 2026-11-16: "Each time you do an install I don't need us repeating the same exact encounters". A raid won on
the owner's PC at the game's own size stays out of later sweeps, after a new install too. It goes back in only when a
change alters that boss's own fight, and then only after asking the owner. The release notes report each one by the
build it was won on.

| Raid | Won on | Changed since the win |
|---|---|---|
| Golestandt (Albion dragon) | test build 5 | Nothing known |
| Krondon | `1dc66b6` | #239: the re-clear of respawning ogres after Drihten (the route, not a boss) |
| Hall of the Corrupt | `1dc66b6` | #229: Aidon's lead-home. Aidon is fought again on the way into Albion's Summoner's Hall |
| Coliath | `1dc66b6` | Nothing known |
| Cuuldurach (Hibernia dragon) | test build 5 | Nothing known |
| Marfach Caverns | test builds 2, 4 and 5 | Nothing known |
| Gjalpinulva (Midgard dragon) | `88b6ab3` | Nothing |
| Iarnvidiur's Lair | `88b6ab3` | Nothing |
| Juggernaut | `88b6ab3` | Nothing |
| Darkness Falls (Midgard) | `e3b8238` | The Darkness Falls raid AI work came after this win. It stays out of the sweep (owner decision 6 of 2026-11-17) |
| Tur Suil | `ccba364` | Nothing known |
| Avalon City | `ccba364` | Nothing known |
| Crystal Cave (Xanxicar) | `ccba364` | Nothing known |
| Mollunero | `ccba364` | Nothing known |
| Evern | `ccba364` | Nothing known |

With no standing win and not finished in sweep 3: Dodens Gruva (won on test build 5, lost on `1dc66b6`, not reached on
`ccba364`) and Darkness Falls Hibernia (cut off on `ccba364`). Darkness Falls Albion lost on `ccba364`.

## Raid behaviour (test build 2)

| What | Must | Status |
|---|---|---|
| A raid the game opens itself: the boss's tale first, then the call across the screen with the horn (#192) | must | **Seen** on test builds 3 and 4, and on `1dc66b6` (2026-11-14 23:31-23:36, Hall of the Corrupt: the tale on screen in /faction, then the call and horn sent to the client, in the PC log; the notice itself wasn't caught in a screenshot). Coliath (04:03) has no tale and went straight to the call |
| Raids every four hours per realm (#192) | must | **Seen** on `1dc66b6`: raids the game opened itself in all three realms; Albion called Hall of the Corrupt at 23:36 and Coliath at 04:03, and `REALM_RAID_SIZED` logged the next in 240 minutes. Setting seen in the PC log on build 2 (150 to 240, 2026-11-12 16:21) |
| After a wipe the raiders gather at the way in and go back in together (#195) | must | Seen in the PC log on `1dc66b6` (Albion's Summoner's Hall raid: return waves waited 240, 40 and 120 seconds) and on test build 3. On `5378dbe` the gather and the walk in were seen: epic-hibernia wave 5 gathered 54 and went in with 123. 6 `step=stuck` lines came from 3 waves, 2 of which made headway again. The checker now reports stuck waves in their own row ("A stuck return wave makes headway again"), not as a contradiction of this claim. Their causes are in DIAG-5378-regroup. In the frontier the raiders released at their staging post were killed walking back by RvR gamebots: since #240 they release at one raid-wide post, or the hub when it's threatened (tests only). Not working on `e3b8238` (raiders released to town). Open until the frontier case is seen. On `5378dbe` two code bugs stranded waves: Galladoria's 123 stood ~18 minutes on the landing because the muster was placed where the stranded-regroup guard refused it, and an Avalon wave of 2-3 called the main body back 11 times (cap 2) while the raid mustered. Both fixed in #335 (musters placed against the guard's dangers, `REGROUP_REFUSED` names the danger; the help cap and no helpers from a mustering raid; tests only). |
| A pull waits for 70% of the living raid (#195) | must | Seen on test build 3. Not working on `e3b8238`: the early-pull rule counted only raiders already inside (16 of 18 in a raid of 118). Fixed in #213. On `1dc66b6` the Hall of the Corrupt pull came with 90 raiders inside, but the 70% count wasn't checked. Tests only since #213. **Contradicted** on `5378dbe`: Aroon and Xaga's room (Galladoria) were pulled through a required-adds switch and wiped outside an attempt, with no `FINAL_ENGAGE` or `EARLY_PULL` for either boss. "30 final engages at the needed count, 0 below" hid them. The checker now counts such pulls (2 on `5378dbe`). Fixed in #336 (a boss's required adds are fought only inside an attempt on it, and adds in combat trigger the early pull; tests only). |
| A retry of a boss that raises adds raises them again (checker row `retry_adds`, sweep 6b) | must | **Contradicted** on `b1b6846`: Fornfrusenen's retry at 09:15:44 showed no shards (no `addsSpawn` line, no shard retarget or damage). Fixed in `fix/sweep6b-raids` (tests only). A boss whose adds stay dead by design is the log review's call |
| No attempt ends "never entered combat" with 10 or more fallen (checker row `attempt_contact`, sweep 6b) | must | **Contradicted** on `b1b6846`: Aroon's three attempts at 09:04:49, 09:12:19 and 09:21:59 (14, 35 and 19 fallen at his Scaiths). Fixed in `fix/sweep6b-raids` (tests only) |
| A boss that resets while the raid still stands costs no attempt (#195) | must | Seen once in the PC log on `1dc66b6`: Aidon's tether reset at 23:00 cost no attempt (the first wipe was counted at 23:19) |
| Raiders walking back still count as the raid when an attempt is scored (#193) | must | Tests only. On `1dc66b6` the wipe line counted released raiders as living ("living=76" at a wipe where 65 of 65 engaged fell); the line is clearer since #229 |
| Aidon's illusions leave after every fight (#191) | must | Tests only (one container run). Not looked for on `1dc66b6` |
| The curers' post at Spindler Broodmother (#190) | must | Used on `e3b8238` and the raid still lost twice; replaced by party posts 700 apart, add parties sized to her spawns and a burn below 25% (#214). **Seen** in the PC log on `1dc66b6`: she died in 12.7 minutes with the party posts (bots only) |
| A living boss on a raid's way in is fought as an objective, not walked into (#213) | must | **Seen** in the PC log on `1dc66b6`: the route held Albion's Summoner's Hall raid for Aidon ("a living boss covers the walk"), and it mustered and fought him |
| A raid dungeon's epics don't respawn while its raid is inside (#213) | must | Tests only. On `1dc66b6` the hold on trash worked only in Darkness Falls and Summoner's Hall (Krondon's respawning ogres), changed in #239 |
| No muster inside another living boss's aggro (#213) | must | Not working on `1dc66b6`: Galladoria's final muster lay behind Easmarach's falls (#230), and the Crypt Lord's musters in Caer Sidi lay in his court's and patrol's reach (#236). Both fixes are tests only |
| Agmundr, the Crypt Lord's court, the real Host, Ilron's adds and a perched griffon (#214) |  | On `1dc66b6`: Agmundr killed (log) and Ilron killed with the GM in the raid; the Crypt Lord's court beat the raid (#236). The rest tests only. On `5378dbe`: Agmundr's second attempt went in with all six of his hrimthursa alive (89 of 90 fell); the Crypt Lord joined 5-7 s after two court pulls the window called clean (90% of the deaths were his court). Fixed in #335 (his pack is cleared again before a retry; the court window also times his patrol against the raid's walk from the muster), tests only. The raid plans count only once seen on the PC |

## Bots that play with you

| What | Must | Status |
|---|---|---|
| A gamebot leader invites you and you can follow (bot-led groups B1) | must | **Seen** on test builds 3 to 5, and on `1dc66b6` (asked invites after /lfg in Lyonesse, Dartmoor and Caer Diogel, unasked ones after a raid; following, fighting, loot shares). Findings on `1dc66b6`: a party started a dungeon while the player was /afk; a party idled in Caer Diogel for half an hour while calling pulls (#234, tests only) |
| `/lead`, `/lead bot`, `/makeleader`, `/invites` and the join mode (B2) | must | **Seen** on `1dc66b6` (2026-11-14 22:45-22:50: `/lead`, `/lead bot`, `/makeleader` refused mid-fight and then done, `/invites`; the default join mode, follow). `/lead` and `/lead bot` also on test builds 3 to 5. The `lead` join mode was not tried |
| A raid leader invites you into a raid (B3) | must | **Seen** on test build 5 (Golestandt) and on `1dc66b6`: Hall of the Corrupt (after /lfg), Caer Sidi and Krondon (sweep raids) and Coliath, each box accepted and a seat in a party |
| Replacements ("lf 1 more"), the natural break and the pull call (B4) | must | Partly. The let-go after 30 minutes away and the replacement call: seen on test builds 3 to 5 and on `1dc66b6` (23:20). The natural break (bags): in the PC log only on `1dc66b6` (05:17), not watched. The pull call: **not working on `1dc66b6`**, bot-led parties called no pulls in /group (fixed in #234, tests only) |
| RvR: bots notice you the way a 2003 player would, not from across the zone | must | **Seen** on `3d67b6f` (2026-11-15 07:22, Manawatch with GM off, at Hadrian's Wall): Midgard bots noticed him at 516-1,575 (`RVR_NOTICE`: facing and still at 1,462 with chance 0.77 after 724 ms, in combat at 1,275, close at 660 and 516), then mezzed, rooted and killed him. Open question: no `RVR_SPOT` line was logged. On `19e7037` the log has 643 `RVR_SPOT` lines (bots calling enemy bots, "inc 1 se"); a spot called on the player is not looked for yet |
| Replies that don't give bots away; `/lswhy me` |  | Partly on `1dc66b6`: bots answered tells and /lfg; a group question ("where we headed?") went unanswered and `/lswhy me` had nothing recorded for it |
| A battleground session: the realm rallies in the breach and takes the keep lord |  | **Not seen: no keep has changed hands.** Thidranki, 2026-11-15, with 50-100 Lv.20 bots per realm. On `3d67b6f` the bots arrived trained and both realms laid siege (the siege notice on screen), but waves formed only after 22 minutes and died to the guards one by one. On `ea882e6` waves gathered in 1-4 minutes and cued each other, but died on the way. On `3b808b4` they marched on the door and all six doors fell by about 10:31; the lord was never reached (the rally point stood inside, 276 deaths to guards); the keep had gone to level 4 on capture (guards 33, lord 38). On `d64d005` the keep is level 1 again (guards 28, lord 33, seen in `BG_KEEP_STATUS`) and each realm rallies outside the breach: 12 attempts, none reached the lord's floor, because both realms used the same rally spot and killed each other's stormers. On `0026378` the rallies stand 2,546 apart (`BG_RALLY_PLACED`); 6 attempts by 14:00, none reached the gate, killed by guards, Albion bots and the third realm; 183 of 410 battleground deaths had no armor (the Lv.20 bots arrive without gear; owner 2026-11-15: they will arrive with starter gear) |
| Repeatable quests, deliveries and an epic chain step |  | No record. The RvR and battleground kill quests count enemy gamebots since #243 (tests only). On `1dc66b6` the GM's character was a base class, which has no epic chain |

## Open-field RvR (owner 2026-11-16, in 1.2)

The plan is [RVR-FIELD-1.2.md](RVR-FIELD-1.2.md) (WP0-WP6, #296 to #303). Must rows since owner decision 5 of
2026-11-17. First run on the owner's PC: `19e7037`, 2026-11-17 01:33-06:15, every feature's log line fired (counts
below). Then `4be6fab`, 06:18-06:48.

| What | Must | Status |
|---|---|---|
| Regroups no longer flap | must | Flapped on `19e7037`: 3,000 `RVR_REGROUP` lines in 4 h 42 min, mostly relayed from group to group (861 "relayed regroup" and 213 "relayed retreat" starts). Fixed in #304. **Seen** on `4be6fab` (06:21-06:47): 38 regroups in 30 minutes, none relayed on (`relayedTo=0` on all 38). Only 30 minutes so far. **Contradicted** on `453e01a` (2026-11-17 11:33-14:00, sweep 4): 1,311 starts in 2 h 26 min, 176 started by a relay; #304's rule held (every relayed start logged `relay=none`), but the regroup that ends a *relayed* retreat was passed on again, reaching a second set of groups and echoing back to the group that fought (group 4891 at 12:36-12:37). Fixed in `fix/rvr-regroup-relay` (tests only). **Criterion (corrected 2026-11-17):** no relay is passed on again: relayed starts log `relay=none`, no regroup ending a relayed retreat relays, no relay returns to its sender within 60 s, and relay-started regroups stay at or under 11.5% of starts (the bar was "about 10%" until the owner's decision of 2026-11-18, below). ("None relayed on" was only what 30 minutes on `4be6fab` showed; #304 allows one hop.) **Contradicted** on `5378dbe` (2026-11-17 21:52 to 11-18 02:31, sweep 5) by the share only: 347 of 2,377 starts (14.6%) were relay-started, and the hop checks (a) and (b) were 0 (all 512 relayed stances logged `relay=none`). 123 of the 347 were a group in contact and inside its own 60 s retreat cooldown taking a relayed regroup that the next board ended ("contact while regrouping", 121 of them after 1 s). Fixed in `fix/rvr-relay-and-checker-5378`: in contact the relay no longer starts a regroup (tests only). The checker's (c) was a pattern, not proof: in a fight of several groups every group's own retreat reaches the others. Relayed starts now log their sender (`from=<group>`), and (c) is exact on a build that logs it; on `5378dbe` it reads "not judged". The checker also counts relayed regroups ended by contact within 1 s (123 on `5378dbe`; the fix should make it about 0). **The 10% bar (reasoning, 2026-11-18).** It was set in RVR-REGROUP-1117 without a derivation. The share depends on how many own stances send and how many groups each reaches, not only on hops. On `5378dbe`, with the 123 in-contact starts gone, the estimate is 224 of 2,254 = 9.9%; up to 10 of those 123 groups left contact while the relay was still valid and could still regroup, giving up to 10.3%. A second hop, as on `453e01a`, would add about 72 starts: each of the 165 relayed retreats would pass on its "retreat over", at this log's 0.44 relayed regroups per send, giving about 12.7% (`453e01a` measured 13.4%). The one-hop share's count noise is about ±0.7 points (Poisson on about 230). So 10% sits on the one-hop value and would fail a correct build about half the time. **Owner decision (2026-11-18): the bar is 11.5%.** "One hop, no echo" is decided by the exact checks (a), (b) and (c) with `from=`; the share is only a backstop, at **11.5%**: about halfway between the one-hop estimate (about 9.9-10.1%) and the two-hop one (12.7%; 13.4% measured on `453e01a`), about 2 standard deviations from each, and well below the in-contact bug's 14.6%. The checker's `RELAY_SHARE_BAR` is 11.5 (`fix/relay-bar-and-buffbot-copies`). First PC result after the in-contact fix, on `b1b6846`: 17 of 179 starts relay-started (9.5%), (c) = 0 exact |
| Bots notice bots by the player rules | must | Fired on `19e7037`: 3,002 `RVR_NOTICE kind=EnemyBot` (and 507 of enemy pets), 643 `RVR_SPOT`. Not yet held against the perception table: the median `delayMs` is about 1 s at every range (465 notices under 1,000, 500 at 3,000 and more). Open |
| The frontier isn't dead: at least half the old kill rate per online bot at daytime population | must | On `4be6fab`: 130 `RVR_BOT_KILLED` in 30 minutes, about 40% of `ccba364`'s rate per online bot. A rough figure: the log gives online counts only in the raid scheduler's lines (`REALM_RAID_SCHEDULED online=`), 153 for Albion at 06:38 against 854-871 per realm on `ccba364`'s evening. Not yet at daytime population. Open |
| Roaming eights find fights and move on | must | On `19e7037`: 96 `RVR_ROAM_CHECK`, 4 `RVR_ROAM_HUNT`, 0 `RVR_ROAM_MOVE_ON`. On `4be6fab`: 10 checks, 1 hunt. Under investigation |
| Group tactics look like players (the owner's 20-minute look) | must | Not done. The tactics fired on `19e7037`: 1,022 assist lines (386 calls, 636 switches), 750 crowd control assignments, 118 interrupts, 202 peels, 340 kites, 56 stealth openers, 265 parties formed |
| March lanes arrive | must | On `19e7037`: 47 lane assignments (`RVR_LANE_ASSIGN`), 17 fallbacks, 15 rejected. Four marches reached their keep and started a siege ("the marching army reached the keep"). Arrival by lane is not counted yet. Open |
| Sieges still happen and keeps change hands | must | **Seen** on `19e7037`: 4 sieges started (02:03, 03:16, 04:05, 04:55), 2 keeps taken: Caer Erasleigh by Midgard (02:15), then back by Albion (03:39) |
| Catalogs rebuilt from a clean release world | must | Loaded and walked on `19e7037`: 98 check spots and 18 circuits (400 walks, 0 failed), 318 of 332 lanes validated (14 rejected). Whether the shipped catalogs were built from a clean release world is not in the log. Open |

## Era builds (merged into 1.2 in #326)

The plan is [ERA-BUILDS-1.2.md](ERA-BUILDS-1.2.md) (WP0-WP5; the checks are its WP6). Everything is tests only. The owner
sets which rows are must. Each row is checked on the era candidate's build, from the owner's log; checks 1, 2, 4 and 6
need a test copy (new batches; 1 and 2 a fresh world with no other gamebots), check 3 the owner's world.

| What | Must | Status |
|---|---|---|
| 1. New bots are era-built: a fresh test world with no other gamebots, **Bot schedule Off** in the launcher's Living Server tab (with `clock`, the 1.2 default, only the bots scheduled online log in, and a bot draws its build when it first logs in), `+ Lv.50 ×100` per realm, start and let the population log in (the checker reads `ERA_BUILD_TRAINED` as "Not applicable" when no `draw=era` assignment is in the log, as on the owner's world on `5378dbe`). One `ERA_BUILD_ASSIGNED` line for each bot that logged in (300 once all have), about 10% of them `draw=offmeta`, and an `ERA_BUILD_TRAINED match=yes` for each `draw=era` one, ending `unspent=N` with N less than the next level of the build's lowest line (fully trained: N buys no further level in any line of the build; `/target` one and compare its trainer window with its row in `docs/research/ERA-BUILDS-CHECK.md`) |  | Not run (tests only) |
| 2. The class mix: the same kind of fresh world, Bot schedule Off, three batches of 100 per realm (`+ Lv.1`, `+ Lv.20`, `+ Lv.50`); start, let them all log in, restart. In each realm's `ERA_BUILD_CENSUS`, `new=0`, and `eraClasses=` (the classes counted over the era-built bots only, so older bots never weigh in) has each class within 4 points of its share |  | Not run (tests only) |
| 3. Existing bots unchanged: the owner's world after the update. `ERA_BUILD_CENSUS` has `era=0 new=0` and `kept=` the roster; no `ERA_BUILD_ASSIGNED` for a bot with a saved plan; three named bots show the same spec levels before and after (trainer window or `/target`) |  | Not run (tests only). On `5378dbe` the 13 `ERA_BUILD_ASSIGNED` lines (all `draw=nearest`, level 50 healers) were not existing bots. Each was a No Lifer buffbot saved as a new roster row when it logged out, which then logged in as a gamebot. Fixed in `fix/rvr-relay-and-checker-5378` (tests only). The rows already made are retired once by `fix/relay-bar-and-buffbot-copies` (next row); the owner plays on a fresh install, which has none |
| 3b. Buffbot copies retired (owner decision 2026-11-18): on a world that ran a build before #334, the first start logs `BOT_BUFFBOT_COPIES retired=N found=N` with one `BOT_BUFFBOT_COPY_RETIRED` per copy (bot, id, main, mainId); no retired copy logs a line of its own afterwards (`bot=<name> id=<id>`); the next start logs nothing (once per world); a fresh world logs `retired=0 found=0`. The rows stay in `offline_world_bots` with their items (`IsRetired=1`); the launcher's bot list shows each as "Retired: copy of <main>'s buffbot (kept, never logs in)" with the state "Retired", not "Deletion pending" (Delete is unchanged) |  | Not run (tests only: `UT_BuffBotCopyRetirement`). How a copy is told from a gamebot: no `ClassName`, a buffbot class at level 50, and the realm, class, gender, race and one of the possible names of the buffbot of a main with a lower id. The 48 `BUFFBOT_CREATED` names in `c5378.log.all` and `cb1b6.log.all` are all among the names it looks for (checked against the logs, not on a world). The checker's row is "Buffbot copies from earlier builds retired once and never played" |
| 4. No custom-class bots, either edition: on a 0.37b and on a 0.37 world, `ERA_BUILDS_LOADED customClass=never`, and `Sluaghbinder=0` in Hibernia's `ERA_BUILD_CENSUS` after a `+ Lv.50 ×100` Hibernia batch; on a test world with Sluaghbinder gamebots, one `BOT_CUSTOM_CLASS_RETIRED` line each and none of them online. The census counts retired custom-class bots too, so the checker compares the census's `Sluaghbinder=` with the number retired; it is contradicted only by more counted than retired, or by a line of a retired bot playing (`bot=<name> id=<id>`). On `5378dbe`, 79 were counted and 79 retired, and none played |  | Not run (tests only) |
| 5. Battleground: Thidranki with `+ Lv.20` era bots. `RVR_PARTY_FORMED` lines with a crowd-control role filled (`cc=Healer(healer-cc)`, `cc=Sorcerer(cc-mezzer)`, `cc=Bard(cc-mezzer)`); the pet count chasing the player against the 2026-11 numbers (BOT-BUILDS-AND-RVR, "How we'll know") |  | Not run (tests only) |
| 6. One raid per realm on an era-mix world (owner's review D12): a dragon per realm forced with `/epictest start`, era-built bots only. Reported as era-mix results, apart from the raid bar |  | Not run. Optional (D12: if Sunday allows; otherwise the notes say it wasn't done) |
| The install checks again on the era candidate (D11): the launcher changed |  | Not run |

## Bots as people (in 1.2 since #316-#328)

The plan is [BOTS-AS-PEOPLE-1.2.md](BOTS-AS-PEOPLE-1.2.md) ("Played checks"). Work packages 1-5 and 2R are merged; 6
(milestones) is built. Everything is tests only. The owner sets which rows are must. Every switch is on (the
default). Rows 1-5 and 7 need a **fresh test world**: no other gamebots, **Bot schedule Off** in the launcher's Living
Server tab (with `clock` only the bots scheduled online log in), a GM character for `/lstest` and a non-GM one for what
a player sees. The log is read with `python3 source/tools/played_check_log.py <server.log> --build <commit>`; its
`people` checks are named in each row. Every tag below is written as the server writes it (checked against the code on
2026-11-17).

| What | Must | Status |
|---|---|---|
| 1. Skill tiers (WP1): `+ Lv.50 ×100` per realm, start and let them all log in. `BOT_SKILL_PROFILE` lines (at most 30 a minute; the rest are counted in `BOT_SKILL_BUDGET`) with `start=` shares within 5 points of 30/50/20 (check `skill_profile`), and `BOT_SKILL_BUDGET start=` (percent of the online bots by start tier, server-wide) near 30/50/20; its `alb=`, `mid=` and `hib=` are each realm's current tiers (Casual/Average/Veteran counts), which on a fresh world should sit near the same mix. `/lstest skill` on five bots shows different tiers. Growth: after an overnight on the same world, `BOT_SKILL_GROWTH` lines (check `skill_growth`) and more experience in `/lswhy skill` than the evening before |  | Not run (tests only) |
| 2. Mistakes, spared in your group (WP2): the same world, `+ Lv.20 ×100` per realm as well; play an hour in a bot-led group (accept a gamebot leader's invite) while the others level. `BOT_SKILL_MISTAKES`, summed over the session, has `rate_in` about a quarter of `rate_out` (check `mistakes`: in/out at most 0.5, with 100 or more decisions in your group), with `BOT_SKILL_MISTAKE spared=true` lines from your group and `kind=pull` lines with `group=` 3 or more. The log review compares gamebot deaths within 30 s of a `kind=pull` mistake against the session's other pulls |  | Not run (tests only) |
| 3. Raid learning (WP2R): the same fresh world, so nobody has seen Xanxicar (no seeding on a world without a counted attempt): Crystal Cave forced twice at the game's raid size with `/epictest start` (Albion level 50s, 60 or more), **bots only**: a raid with a player in its parties keeps the record's familiarity and credits nobody. `RAID_LEARN` with `seenShare` near 0 in run 1 and higher in run 2 (check `raid_learn`; a forced run never joins the record, so `record=` and the familiarity stay low on a fresh world), `RAID_MECHANIC_SEEN how=raid` crediting the raiders in each run; both runs end in a win or a loss the log review calls correct, never a stall. On an upgraded world (the owner's), `RAID_MECHANIC_SEEN how=seeded` once at the first start |  | Not run (tests only) |
| 4. Place knowledge (WP3): `BOT_PLACE_LEARNED how=seeded` lines at the first start, then `how=visited` as bots travel (check `places`), and `BOT_PLACE_BUDGET` every 5 minutes. `/lswhy way` on a level 35 bot names its capital, home town and the zones of levels below 35, no dungeon and no frontier |  | Not run (tests only) |
| 5. Way finding (WP4): `/lstest ask` on a gamebot shows "how do i get to ..." in /faction or /say, answered by another bot; `/lstest lost` on one shows it standing off its road, then going on. In the log: `BOT_WAY_ASK said=said` followed by `BOT_WAY_TOLD`, `BOT_WAY_FOLLOW`, and `BOT_WAY_WRONG_ROAD` with `cost_s` 180 or less and `result=back` (check `way`); `BOT_WAY_SUMMARY` every 5 minutes. No rise in `AUTONOMOUS_STUCK_15M_RECOVERY` and the other stuck-watchdog recoveries against a session on the same world with `bot_ask_the_way` and `bot_wrong_roads` False |  | Not run (tests only) |
| 6. The sit emote (WP5, first): a gamebot seated by a wait (`BOT_MOVE_SIT`) is seen sitting in the original client. If it is not, the sitting habits are for the new client only and the notes say so |  | Not run (tests only) |
| 7. Movement habits (WP5): screenshots of a bot group running in a line, bots crowding the vault keeper, bots sitting at a wait, and a melee bot circling in an open-field RvR fight. At a keep its realm holds, bots sitting on the wall (`/lstest wall`), standing when Claude's enemy character comes within 2,500. `BOT_MOVE_HABITS` every 5 minutes with `sits`, `stickLost` and `overshoots` above 0 (check `movement`, which sums them), `strafes` above 0 after the RvR fight and `wallSits` after the keep; `BOT_MOVE_WALL` and `BOT_MOVE_SIT` lines; no rise in stuck-watchdog recoveries |  | Not run (tests only) |
| 8. The 5-minute summaries: every 5 minutes one `BOT_PEOPLE_SUMMARY` (all twelve switches `=on`, the rows saved, the scope's answers), then one each of `BOT_MILESTONE_SUMMARY`, `BOT_MOVE_HABITS`, `BOT_PLACE_BUDGET`, `BOT_SKILL_BUDGET`, `BOT_SKILL_MISTAKES` and `BOT_WAY_SUMMARY` (check `summary`); the AI_PROFILE report names `people.skill`, `people.places`, `people.way_finding`, `people.movement` and `people.milestones` |  | Not run (tests only) |
| 9. Personal milestones (WP6): on the row 1 world (`+ Lv.50` bots; add `+ Lv.45 ×50` per realm for the ding), with Claude's character online in the realm. `BOT_MILESTONE_ARC state=active` lines at the first start (at most 30 a minute; the rest counted in `BOT_MILESTONE_SUMMARY chosen=`), `BOT_MILESTONE_PROGRESS` as bots pass 50% and 90%, and a "halfway to 50" or "bubbles to 50" line in /faction or /say on screen with its `BOT_MILESTONE_TALK` (check `milestones`: Contradicted if any talk claims more than the logged `xp=`, `rp=` or `count=`, or its `bubbles=` / `left_k=` are not those numbers'). A line due in a fight shows `BOT_MILESTONE_PROGRESS talk=later reason=in_combat` and its `BOT_MILESTONE_TALK` within a few minutes (`tries=`). `/lstest milestone` on a bot whose numbers have passed a point says it now. `BOT_MILESTONE_REACHED arc=ding50` at a level 50 ding; after a dragon raid (row 3's or the raid bar's) `BOT_MILESTONE_REACHED arc=dragon_trophy trophy= source=` naming an item from that raid's `REALM_RAID_LOOT` lines for the same bot and the boss itself, never one of its adds (`sought=false` for a bot that was chasing something else); `BOT_MILESTONE_SUMMARY raidKills=` rises by one a boss, not by one an add. Then open the Herald from the launcher: **Milestones of the Realm** lists them. `BOT_MILESTONE_SUMMARY` every 5 minutes after `BOT_PEOPLE_SUMMARY`; the AI_PROFILE report names `people.milestones` |  | Not run (tests only) |

## Crafting and the economy (in 1.2 since #270)

The plan is [CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md). Must rows since owner decision 5 of 2026-11-17.

| What | Must | Status |
|---|---|---|
| Bot crafters craft at stations | must | **Seen** on `19e7037`: 255 sessions started at 5 stations, 240 "Finished at the station", 971 pieces made (`CRAFT_SESSION_START`, `CRAFT_SESSION_END`). On `ccba364` (14:01-01:31): 2,109 sessions started |
| Craft orders are mostly filled | must | On `19e7037`: 156 made, 163 expired (`ECON_ORDER`, all from quartermasters). Under investigation |
| The economy doesn't run away | must | On `19e7037` the ledger wrote every hour (`ECON_SUPPLY`, `ECON_FLOWS`). The sink governor's smoothed ratio stayed at 0.02-0.04 against its target of 0.5 (`ECON_GOVERNOR`). The full-day report is pending. Open |
| No bot strips its own gear | must | On `19e7037`: 0 blocked in every hourly check, 152 to 209 checks an hour (`ECON_UNDRESS_GUARD`, `blocked_since_start=0`). Open until the full-day report |
| You craft as a player | must | Not done |
| You trade with a crafter bot | must | Tried on `b1b6846` (2026-11-18 07:33-07:36, Testrunner by /send): "what do you make?" was cut mid-word, "can you make a fine alloy needle mace" offered a different stock piece, and an agreed stock "deal" lapsed after 15 minutes with no log line. The piece is now set aside until the deal lapses (`ECON_PLAYER_ORDER outcome="stock_held"` / `"stock_released"`) and the crafter tells the player once at its break. Fixed by tests only (`UT_CrafterRepliesFit`); not yet seen working. No trade completed |
| The launcher's Bot crafters card and economy page | must | Not done |
| Crafted goods reach the Exchange |  | On `19e7037`: 7 crafted sales overnight (`ECON_FLOWS exchange_crafted`), of 434 Exchange sales. Under investigation |
| Wear, repairs and potions |  | On `19e7037`: 12 `ECON_WEAR` and 10 `ECON_CONSUMABLE` lines. Wear rolls happen, but no item fell below 88% and none was repaired; no potion was bought or drunk (vials for weapon coats were bought). Open |
| `/lscraft` |  | Used on `b1b6846` (2026-11-18): `/lscraft craft` printed only "Crafter roster: registered." It now lists the crafters in the game (tests only) |

## Tales and lore

Tale history is the owner's PC on 2026-11-10 (builds `15fcc70`, `5300a87`), staged with `/lstest tale` unless marked;
details in [TALES.md](TALES.md).

| What | Must | Status |
|---|---|---|
| A tale heard line by line to its end | must | **Seen** on `1dc66b6` (2026-11-14 23:21-23:24, 24 lines on screen; and the raid's tale at 23:31-23:36). On 5300a87 (history) |
| A tale asked for in plain words | must | **Seen** on `1dc66b6` (23:21, in /faction) and test build 4. On 15fcc70 (history) |
| The storyteller answers a question mid-tale |  | On `1dc66b6` the aside was addressed to the player but didn't answer the question. Seen on 15fcc70 and 5300a87 (history) |
| A fireside tale in a resting group |  | Seen on 15fcc70 (history) |
| A tale that starts by itself | must | **Seen** on `1dc66b6` as a raid's tale: at 23:31 a bot asked for Aidon's tale before Hall of the Corrupt, with no `/lstest` and no request from the player. The raid's own tale closes the row (owner decision 7 of 2026-11-17). A tale that starts on its own away from a raid (about 25 minutes into a session) is not seen. How to see it: [PLAYCHECK-BETA10.md](PLAYCHECK-BETA10.md), step 9 |
| A rumour that is a raid really forming |  | Not seen (tests only) |
| Boss yells, atmosphere lines and kill broadcasts on screen | must | **Seen** on `1dc66b6`: realm first-kill lines (22:52, 23:07, 04:57), the Aidon win line (00:44), an atmosphere line on arriving in Avalon Marsh (02:20) and a boss yell in Krondon (03:35). An aggro yell at a boss's death was fixed in #231 (tests only) |
| Loremasters, books, the Herald chronicle and bestiary |  | Partly on `1dc66b6`: the `/lore` window (23:25) and a loremaster standing in Camelot's library (01:05, his dialog not opened). Books, the Herald chronicle and the bestiary: no record |

## Bot chat: promises to come (owner decision 2 of 2026-11-18, `fix/chat-promises-real`)

A bot that says "omw", "coming", "on my way", "ill be there" or "heading there" must really go, and a bot that won't go
must never say it (chat audit DESIGN-006, CORPUS-C-018, CORPUS-B2-047, LOG-019). The gate is `ChatPromises`; every line
below is written as the server writes it. Checker: `chat` / `promises`.

| What | Must | Status |
|---|---|---|
| A bot that says it is coming arrives. With a level 50 character in the frontier, type "inc <keep>" (a keep of your realm, e.g. "inc bled") and "free rps at <keep>" in /faction a few times over an hour, and "help" in /say beside a gamebot while a monster fights you. Each "omw" (or "coming", "on my way") on screen has a `CHAT_PROMISE made bot="<the bot>"` line within a second (`reason=`, `target=`, `eta=`, `deadline=`), then `CHAT_PROMISE kept ... after=<s>` before its deadline: the bot is seen at the keep (`/lswhy` shows "Defending keep" or "Entering friendly keep") or beside you, fighting your monster. Bots that don't go answer "how many?", "where are you?" or "might make it", and log `CHAT_PROMISE refused ... why=<code>` (`not_on_rvr`, `level`, `too_far`, `in_combat`, `on_realm_raid` and so on). The checker reads Seen with at least one kept promise and kept at 70% or more of kept plus timed-out; `broken why=died`, `taken`, `logged_out`, `keep_fell` and `asker_gone` are honest ends and not counted against. Contradicted by more than 30% `why=timeout` (5 or more judged), and, on screen, by any "omw" with no `CHAT_PROMISE made` for that bot | must | Not run (tests only: `UT_ChatPromises`, `test_played_check_log.py` ChatPromises). Which replies go through the gate depends on the chat branches that call it (`fix/chat-reading-replies` and the field-talk, news and scene fixes): until they merge, nothing calls it and no `CHAT_PROMISE` line is written |

## Beta 1.0 features never seen (must since 2026-11-15)

Owner decision 11: the Beta 1.0 items that were never seen in game ([KNOWN-ISSUES.md](KNOWN-ISSUES.md), "Not yet seen
in game"; [RELEASE-PLAN.md](RELEASE-PLAN.md), "Before the stage 7 merge") are seen first, not shipped marked.

A two-hour script that covers every row below and "A tale that starts by itself": [PLAYCHECK-BETA10.md](PLAYCHECK-BETA10.md).

| What | Must | Status |
|---|---|---|
| Bot guilds claiming keeps | must | Not seen |
| `/ginvite` and `/gc remove` on a bot | must | Not seen. On `1dc66b6` a bot guild invited the player and its members welcomed them in /guild (the other way round) |
| The `clock` bot schedule (`session` was played) | must | Not seen |
| Nemesis lines in chat | must | Not seen (the records were seen; the lines never) |
| You rezzing a dead bot, or a passing bot rezzing you | must | Not seen |
| Grats on your own ding from nearby bots | must | Not seen |
| The bot XP-by-level factor at its exact rate | must | Not seen (bot XP goes up with it on; the rate was never measured) |
| Body language: steps in town and cheers on your ding (looks, emotes and greetings were seen) | must | Not seen |
| The between-pulls group wait raises the number of bot groups | must | Not seen (a 39-minute check showed no rise) |
| Linkdeads | must | A bot went linkdead in the player's group on 2026-10-18 (Beta 1.0 history, KNOWN-ISSUES); not on the candidate line |

## Install

The steps on the owner's PC for the first three rows, and what CI tests for them, are in
[INSTALL-CHECKS-1.2.md](INSTALL-CHECKS-1.2.md). CI runs them on Windows runners (`candidate-install`, from
`build-and-test`) and the playtester's `class-switch` scenario on Linux. They are tests only and close no row.

Owner decision 3 of 2026-11-17: the install checks run this morning on `4be6fab`'s Setup. They count for the final
build (about 11:00) if that build changes no Setup, patcher, launcher or install file.

| What | Must | Status |
|---|---|---|
| A 1.1.4 game updated to the 1.2 candidate through Setup, characters and settings kept | must | Not done on the PC (row 1 of INSTALL-CHECKS-1.2.md). The 1.1.4 merge is in (#245). Tests only: CI's `update114` job installs 1.1.4 with its published Setup, seeds a character, a roster and settings, updates with the candidate's Setup and checks them kept |
| A new install of the 1.2 candidate (0.37 from Living Server's own downloads) | must | Not done on the PC (row 2). Tests only: CI's `fresh` job does it with the candidate's Setup (1.1.4's Setup did it in CI on 2026-11-12) |
| A class-off smoke session on a copy of a 0.37 (class off) world: the server starts, character creation offers no Sluaghbinder, an hour of play, a raid starts (owner decision 8) | must | Not done on the PC (row 3: a class-off copy `playable-v0.37-classoff` of the test world, made by `make_classoff_copy.py`). Every 1.2 result so far is from a 0.37b (class on) world. Tests only: CI's `fresh` job checks the new 0.37 install's switch, client and saves, and that its installed server refuses the Sluaghbinder at character creation (the playtester's `class-switch`, a protocol client, not the game's character screen) |
| One overnight run without a stall or an error that stops play: 8 hours, no game-loop stall of 2.0 s or more, at most 5 stalls of 1.0 s or more in any hour (owner decision 9); checked with `source/tools/check_overnight_run.py` | must | Not done. The night of 2026-11-14/15 on `1dc66b6` was a GM sweep and was not checked against the threshold. On `19e7037` (2026-11-17 01:33-06:15, 4 h 42 min, clean stop) `check_overnight_run.py` found one game-loop stall of 1.05 s (01:46, NpcService), none of 2.0 s or more, and no error that stops play. It fails only on length |
