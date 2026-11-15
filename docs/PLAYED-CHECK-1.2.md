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

## Owner decisions 2026-11-15

The owner answered the roadmap review's 23 questions at 07:10. Where the owner asked for a recommendation, the
recommendation is recorded as the decision, as the owner asked.

1. **Beta 1.1.4 (main) is merged into 1.2** (a separate pull request). The two Install musts that use Setup wait for it.
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

**Where the bar stands** (2026-11-15, from the rows below):
- **Albion: met on the candidate line.** Golestandt won on test build 5; Hall of the Corrupt and Krondon won on
  `1dc66b6`. Both dungeons' raid code changed after their wins (#229 for Aidon, #239 for Krondon), before the freeze.
- **Midgard: not met.** Dodens Gruva won on test build 5. Gjalpinulva and a second dungeon have no win on the
  candidate line.
- **Hibernia: not met.** Cuuldurach won on test build 5 and Marfach Caverns on builds 2, 4 and 5. A second dungeon has
  no win on the candidate line.

History is the owner's PC on build `3e64140` (runs 1 to 3, 2026-11-11 and 11-12), forced raids. Runs 1 and 2 went in
below the game's size; run 3 at it. Results for test builds 2 to 7 come from the draft release notes (l.237-250),
which give one build for the three losses on their l.246-247.

| Realm | Raid | History on 3e64140 | On the candidate line |
|---|---|---|---|
| Albion | Golestandt (dragon) | Lost three times (best 9%) | **Won on test build 5** (second attempt; a raid leader's invite seated the player). Not run since |
| Albion | Caer Sidi | Lost twice; run 3 ended on a wrongly counted second wipe (fixed in #193, tests only) | Lost on test build 5: cut off by the old four-hour limit with 12 named down. Lost on `e3b8238` (sweep, 2026-11-14): 18 named down, the 4-hour sweep cap ended it before the archlich; Crypt Lord 85 of 87 dead, Host walked into the archlich. Reworked in #213/#214 (tests only). **Lost on `1dc66b6`** (sweep, bots only, the GM in the raid, 2026-11-15 02:09-03:04): Lich Lord Ilron killed (02:43), one wipe at Warlord Dorinakka and his 12 crypt knights (02:50, his lowest 96%), then two at the Crypt Lord (03:00 and 03:04, 99%; raid-wide deaths to his court and patrol); 54 minutes, 10 named. Plans fixed in #236 (tests only). **Re-run owed** |
| Albion | Krondon | Stalled three times at Scurceol Hyrde's room | **Won on `1dc66b6`** (sweep, forced at the game's size, the GM in the raid, 2026-11-15 04:03): Drihten Elreden, Lurfos the Herald and Orylle, no wipe, 54 minutes, 63 raiders. A slow re-clear of respawning ogres after Drihten (13 minutes) is changed in #239 (tests only) |
| Albion | Avalon City | Won once (run 2) | Not run |
| Albion | Crystal Cave (Xanxicar) | Won once (run 2, second attempt) | Not run |
| Albion | Hall of the Corrupt | Stopped twice: Aidon's illusions (fixed in #191, tests only) | **Won on `1dc66b6`** (a raid the game opened itself, its tale first, the GM in the raid, 2026-11-15 00:44): Aidon in one attempt, 71 minutes, 5 named, no wipe. A second raid there (05:41) held Aidon out of his tether, immune at 100% for 35 minutes and more: the lead-home bug, fixed in #229 after `1dc66b6` (tests only) |
| Albion | Summoner's Hall | No finished run | Never reached on `e3b8238` (sweep, 2026-11-14): the route through Hall of the Corrupt walked into a respawned Aidon, 182 deaths in 50 minutes; skipped by the GM. Route bosses are fought as objectives since #213 (tests only). **Lost on `1dc66b6`** (sweep, bots only, 2026-11-14 22:40-23:31): the raid mustered for Aidon on its way in and lost to him twice in 58 minutes. Its healers dragged him past his tether and he healed 58% to 100% (#229); raiders walking through the Pennine Mountains were killed by RvR gamebots (92 deaths; #240). Both fixes are tests only. **Re-run owed** |
| Albion | Darkness Falls | No finished run (24 named in run 3) | Not run |
| Midgard | Gjalpinulva (dragon) | Won twice (runs 1 and 2, second attempt both times) | Not run |
| Midgard | Tuscaran Glacier | Lost three times (run 3: four of the Council down, Icelord Agmundr at 38%) | Lost on `e3b8238` (sweep, 2026-11-14): 22 named down, stalled after 219 minutes; Agmundr wiped the raid twice (81 of 81 in 2.7 minutes; then a muster placed 76 from him). Reworked in #213/#214 (tests only). **Stopped on `1dc66b6`** (sweep, bots only, 2026-11-15 00:35-02:06): Icelord Agmundr killed (01:30, 10.3 minutes), then Icelord Kvasir held at 81-99% for half an hour: our Spiritmasters' pets' cold damage healed him (on screen: "thank you spirit champion for healing me"). Skipped by the GM. Fixed in #235 (tests only). **Re-run owed** |
| Midgard | Trollheim | Lost (run 3 reached Nosdoden; nobody regrouped) | Lost at Nosdoden on test build 5. **Stopped by the owner on `1dc66b6`** (sweep, 2026-11-15 05:23-06:28) after 78 minutes, before Nosdoden, to test battlegrounds on a new build: no wipe, Fostra Orm killed, 131 targets down. No result; not finished on the candidate line |
| Midgard | Iarnvidiur's Lair | Won once (run 2) | Not run |
| Midgard | Dodens Gruva | Won twice | **Won on test build 5.** **Lost on `1dc66b6`** (a raid the game opened itself, 2026-11-15, bots only): two wipes at Aros the Spiritmaster, 103 minutes, the Epic Special Forces joined; the defeat line was seen in the client (01:24) |
| Midgard | Summoner's Hall | Won once (run 2) | **Lost on `1dc66b6`** (a raid the game opened itself, 2026-11-14 23:16-23:44, bots only): the client showed "driven out of Summoner's Hall by Summoner Roesia". Before #229 that line named the raid's final boss rather than the one that beat it, so where it was lost is not certain |
| Midgard | Darkness Falls | No finished run | Won on `e3b8238` (a raid the game opened itself, 2026-11-14): 41 named and Legion in one attempt (9 minutes), 132 minutes, no wipe. Before the Darkness Falls raid AI work |
| Hibernia | Cuuldurach (dragon) | Lost run 1, won run 2 (second attempt) | **Won on test build 5** (a raid the game opened itself). Not run since |
| Hibernia | Galladoria | Lost three times at Spindler Broodmother (curers' post in #190, tests only) | Spindler Broodmother killed, then lost at the Organic-Energy Mechanism, on test build 5. Lost on `e3b8238` (sweep, 2026-11-14): two wipes at the Broodmother (15% and 22%), mesmerize caught 23 to 78 of about 104, up to 44 newborns. Reworked in #214 (tests only). **Stalled on `1dc66b6`** (sweep, bots only, 2026-11-14 23:49 to 11-15 00:32): Spindler Broodmother killed in 12.7 minutes with #214's party posts (00:16), then the final muster was placed behind Easmarach's falls while he lived and the raiders bounced off them 165 times for 15 minutes. Skipped by the GM. Fixed in #230 (tests only). **Re-run owed** |
| Hibernia | Tur Suil | Won twice (runs 1 and 3) | Not run |
| Hibernia | Marfach Caverns | Won twice | **Won on test builds 2, 4 and 5** |
| Hibernia | Summoner's Hall | Lost three times at Summoner Roesia | **Lost on `1dc66b6`** (sweep, bots only, 2026-11-15, lost at 05:05): two wipes at Summoner Lossren (lowest 63%, then 20%), 57 minutes, 24 Epic Special Forces joined. Her tortured souls killed 98 of 170 raiders: she called a new wave whenever none lived. Fixed in #237 (keep one soul alive and mesmerized; tests only). **Re-run owed** |
| Hibernia | Darkness Falls | No finished run | Lost on `e3b8238` (a raid the game opened itself, 2026-11-14): 59 named, then Legion twice (lowest 92% and 60%), 382 minutes. On `1dc66b6` a raid the game opened itself (01:39, 136 raiders) was still running at 05:21 with 69 targets down and no wipe; its end is not recorded |

Back in 1.2 (owner, 2026-11-14: "ALL big bosses are in"): the Darkness Falls raid AI, Legion, Behemoth and every other big boss, the open-world ones included; their rows count like the others.
Since 2026-11-15 three of them are signed off as exceptions (owner decisions 3 to 5): Behemoth is never fought, and
Fomor City and Morgana have no raid. They have no row here, and the release notes say so.

Known causes with no fix yet: Krondon's Lyft Miht room; raids leaving a boss for the monsters around it (High Lord Oro,
Gatekeeper Dommel, High Lord Baln); Golestandt's second attempts; Summoner Roesia.

### The open-world raids

Sixteen raids since 1.2 ([RAID-SIZES.md](RAID-SIZES.md), "The open-world big bosses"). Their rows count like the
others (owner, 2026-11-14): the stall rule applies, and the release notes say how each went. `/epictest sweep start
openworld` runs them alone.

| Realm | Raid | On the candidate line |
|---|---|---|
| Albion | Coliath | **Won on `1dc66b6`** (a raid the game opened itself, the GM in the raid, 2026-11-15): called 04:03, Coliath killed 04:57, Albion's first kill of him, on screen. 27 raiders died to the monsters in Aldland on the way |
| Albion | Green Knight | Not run |
| Albion | Spirit of Lord Emthoro | Not run |
| Midgard | Glacier Giant | Not run |
| Midgard | Wretch of Winter (jotun) | Not run |
| Midgard | Wretch of Winter (arachite) | Not run |
| Midgard | Wretch of Winter (spider) | Not run |
| Midgard | Wretch of Winter (crab) | Not run |
| Midgard | Wretch of Winter (scorpion) | Not run |
| Midgard | Raumarik Revenant | Not run |
| Midgard | Mortufoghus | Not run |
| Hibernia | Evern | Not run |
| Hibernia | Juggernaut | Not run |
| Hibernia | Curengkur | Not run |
| Hibernia | Marfoirwort | Not run |
| Hibernia | Mollunero | Not run |

### Re-runs owed

Each raid below has to be run again on a candidate that carries its fix. A fix made after the freeze (owner decision 2)
is added here, with the build that carries it, before it is merged.

| Raid | Why | Fix (tests only) | Carried by |
|---|---|---|---|
| Caer Sidi | Lost to wrong plans at Dorinakka and the Crypt Lord (`1dc66b6`) | #236 | `3d67b6f` and later |
| Tuscaran Glacier | Kvasir healed by our own pets (`1dc66b6`) | #235 | `3d67b6f` and later |
| Galladoria | Stalled behind Easmarach's falls (`1dc66b6`) | #230 | `3d67b6f` and later |
| Summoner's Hall (Albion) | Lost at Aidon after he was dragged out of his tether; frontier walk (`1dc66b6`) | #229, #240 | `3d67b6f` and later |
| Summoner's Hall (Hibernia) | Lost to Lossren's endless waves (`1dc66b6`) | #237 | `3d67b6f` and later |
| Trollheim | Stopped by the owner before Nosdoden (`1dc66b6`); not finished on the candidate line | none | |

## Raid behaviour (test build 2)

| What | Must | Status |
|---|---|---|
| A raid the game opens itself: the boss's tale first, then the call across the screen with the horn (#192) | must | **Seen** on test builds 3 and 4, and on `1dc66b6` (2026-11-14 23:31-23:36, Hall of the Corrupt: the tale on screen in /faction, then the call and horn sent to the client, in the PC log; the notice itself wasn't caught in a screenshot). Coliath (04:03) has no tale and went straight to the call |
| Raids every four hours per realm (#192) | must | **Seen** on `1dc66b6`: raids the game opened itself in all three realms; Albion called Hall of the Corrupt at 23:36 and Coliath at 04:03, and `REALM_RAID_SIZED` logged the next in 240 minutes. Setting seen in the PC log on build 2 (150 to 240, 2026-11-12 16:21) |
| After a wipe the raiders gather at the way in and go back in together (#195) | must | Seen in the PC log on `1dc66b6` (Albion's Summoner's Hall raid: return waves waited 240, 40 and 120 seconds) and on test build 3. In the frontier the raiders released at their staging post were killed walking back by RvR gamebots: since #240 they release at one raid-wide post, or the hub when it's threatened (tests only). Not working on `e3b8238` (raiders released to town). Open until the frontier case is seen |
| A pull waits for 70% of the living raid (#195) | must | Seen on test build 3. Not working on `e3b8238`: the early-pull rule counted only raiders already inside (16 of 18 in a raid of 118). Fixed in #213. On `1dc66b6` the Hall of the Corrupt pull came with 90 raiders inside, but the 70% count wasn't checked. Tests only since #213 |
| A boss that resets while the raid still stands costs no attempt (#195) | must | Seen once in the PC log on `1dc66b6`: Aidon's tether reset at 23:00 cost no attempt (the first wipe was counted at 23:19) |
| Raiders walking back still count as the raid when an attempt is scored (#193) | must | Tests only. On `1dc66b6` the wipe line counted released raiders as living ("living=76" at a wipe where 65 of 65 engaged fell); the line is clearer since #229 |
| Aidon's illusions leave after every fight (#191) | must | Tests only (one container run). Not looked for on `1dc66b6` |
| The curers' post at Spindler Broodmother (#190) | must | Used on `e3b8238` and the raid still lost twice; replaced by party posts 700 apart, add parties sized to her spawns and a burn below 25% (#214). **Seen** in the PC log on `1dc66b6`: she died in 12.7 minutes with the party posts (bots only) |
| A living boss on a raid's way in is fought as an objective, not walked into (#213) | must | **Seen** in the PC log on `1dc66b6`: the route held Albion's Summoner's Hall raid for Aidon ("a living boss covers the walk"), and it mustered and fought him |
| A raid dungeon's epics don't respawn while its raid is inside (#213) | must | Tests only. On `1dc66b6` the hold on trash worked only in Darkness Falls and Summoner's Hall (Krondon's respawning ogres), changed in #239 |
| No muster inside another living boss's aggro (#213) | must | Not working on `1dc66b6`: Galladoria's final muster lay behind Easmarach's falls (#230), and the Crypt Lord's musters in Caer Sidi lay in his court's and patrol's reach (#236). Both fixes are tests only |
| Agmundr, the Crypt Lord's court, the real Host, Ilron's adds and a perched griffon (#214) |  | On `1dc66b6`: Agmundr killed (log) and Ilron killed with the GM in the raid; the Crypt Lord's court beat the raid (#236). The rest tests only |

## Bots that play with you

| What | Must | Status |
|---|---|---|
| A gamebot leader invites you and you can follow (bot-led groups B1) | must | **Seen** on test builds 3 to 5, and on `1dc66b6` (asked invites after /lfg in Lyonesse, Dartmoor and Caer Diogel, unasked ones after a raid; following, fighting, loot shares). Findings on `1dc66b6`: a party started a dungeon while the player was /afk; a party idled in Caer Diogel for half an hour while calling pulls (#234, tests only) |
| `/lead`, `/lead bot`, `/makeleader`, `/invites` and the join mode (B2) | must | **Seen** on `1dc66b6` (2026-11-14 22:45-22:50: `/lead`, `/lead bot`, `/makeleader` refused mid-fight and then done, `/invites`; the default join mode, follow). `/lead` and `/lead bot` also on test builds 3 to 5. The `lead` join mode was not tried |
| A raid leader invites you into a raid (B3) | must | **Seen** on test build 5 (Golestandt) and on `1dc66b6`: Hall of the Corrupt (after /lfg), Caer Sidi and Krondon (sweep raids) and Coliath, each box accepted and a seat in a party |
| Replacements ("lf 1 more"), the natural break and the pull call (B4) | must | Partly. The let-go after 30 minutes away and the replacement call: seen on test builds 3 to 5 and on `1dc66b6` (23:20). The natural break (bags): in the PC log only on `1dc66b6` (05:17), not watched. The pull call: **not working on `1dc66b6`**, bot-led parties called no pulls in /group (fixed in #234, tests only) |
| RvR: bots notice you the way a 2003 player would, not from across the zone | must | **Seen** on `3d67b6f` (2026-11-15 07:22, Manawatch with GM off, at Hadrian's Wall): Midgard bots noticed him at 516-1,575 (`RVR_NOTICE`: facing and still at 1,462 with chance 0.77 after 724 ms, in combat at 1,275, close at 660 and 516), then mezzed, rooted and killed him. Open question: no `RVR_SPOT` line was logged |
| Replies that don't give bots away; `/lswhy me` |  | Partly on `1dc66b6`: bots answered tells and /lfg; a group question ("where we headed?") went unanswered and `/lswhy me` had nothing recorded for it |
| A battleground session: the realm rallies in the breach and takes the keep lord |  | **Not seen: no keep has changed hands.** Thidranki, 2026-11-15, with 50-100 Lv.20 bots per realm. On `3d67b6f` the bots arrived trained and both realms laid siege (the siege notice on screen), but waves formed only after 22 minutes and died to the guards one by one. On `ea882e6` waves gathered in 1-4 minutes and cued each other, but died on the way. On `3b808b4` they marched on the door and all six doors fell by about 10:31; the lord was never reached (the rally point stood inside, 276 deaths to guards); the keep had gone to level 4 on capture (guards 33, lord 38). On `d64d005` the keep is level 1 again (guards 28, lord 33, seen in `BG_KEEP_STATUS`) and each realm rallies outside the breach: 12 attempts, none reached the lord's floor, because both realms used the same rally spot and killed each other's stormers. On `0026378` the rallies stand 2,546 apart (`BG_RALLY_PLACED`); 6 attempts by 14:00, none reached the gate, killed by guards, Albion bots and the third realm; 183 of 410 battleground deaths had no armor (the Lv.20 bots arrive without gear; owner 2026-11-15: they will arrive with starter gear) |
| Repeatable quests, deliveries and an epic chain step |  | No record. The RvR and battleground kill quests count enemy gamebots since #243 (tests only). On `1dc66b6` the GM's character was a base class, which has no epic chain |

## Tales and lore

Tale history is the owner's PC on 2026-11-10 (builds `15fcc70`, `5300a87`), staged with `/lstest tale` unless marked;
details in [TALES.md](TALES.md).

| What | Must | Status |
|---|---|---|
| A tale heard line by line to its end | must | **Seen** on `1dc66b6` (2026-11-14 23:21-23:24, 24 lines on screen; and the raid's tale at 23:31-23:36). On 5300a87 (history) |
| A tale asked for in plain words | must | **Seen** on `1dc66b6` (23:21, in /faction) and test build 4. On 15fcc70 (history) |
| The storyteller answers a question mid-tale |  | On `1dc66b6` the aside was addressed to the player but didn't answer the question. Seen on 15fcc70 and 5300a87 (history) |
| A fireside tale in a resting group |  | Seen on 15fcc70 (history) |
| A tale that starts by itself | must | Seen once on `1dc66b6` as a raid's tale: at 23:31 a bot asked for Aidon's tale before Hall of the Corrupt, with no `/lstest` and no request from the player. A tale that starts on its own away from a raid (about 25 minutes into a session) is not seen. Open until the owner says whether the raid's tale closes the row |
| A rumour that is a raid really forming |  | Not seen (tests only) |
| Boss yells, atmosphere lines and kill broadcasts on screen | must | **Seen** on `1dc66b6`: realm first-kill lines (22:52, 23:07, 04:57), the Aidon win line (00:44), an atmosphere line on arriving in Avalon Marsh (02:20) and a boss yell in Krondon (03:35). An aggro yell at a boss's death was fixed in #231 (tests only) |
| Loremasters, books, the Herald chronicle and bestiary |  | Partly on `1dc66b6`: the `/lore` window (23:25) and a loremaster standing in Camelot's library (01:05, his dialog not opened). Books, the Herald chronicle and the bestiary: no record |

## Beta 1.0 features never seen (must since 2026-11-15)

Owner decision 11: the Beta 1.0 items that were never seen in game ([KNOWN-ISSUES.md](KNOWN-ISSUES.md), "Not yet seen
in game"; [RELEASE-PLAN.md](RELEASE-PLAN.md), "Before the stage 7 merge") are seen first, not shipped marked.

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

| What | Must | Status |
|---|---|---|
| A 1.1.4 game updated to the 1.2 candidate through Setup, characters and settings kept | must | Not done. Waits for the 1.1.4 merge into 1.2 (owner decision 1) |
| A new install of the 1.2 candidate (0.37 from Living Server's own downloads) | must | Not done (1.1.4's Setup did this in CI on 2026-11-12). Waits for the 1.1.4 merge |
| A class-off smoke session on a copy of a 0.37 (class off) world: the server starts, character creation offers no Sluaghbinder, an hour of play, a raid starts (owner decision 8) | must | Not done. Every 1.2 result so far is from a 0.37b (class on) world |
| One overnight run without a stall or an error that stops play: 8 hours, no game-loop stall of 2.0 s or more, at most 5 stalls of 1.0 s or more in any hour (owner decision 9); checked with `source/tools/check_overnight_run.py` | must | Not done. The night of 2026-11-14/15 on `1dc66b6` was a GM sweep and was not checked against the threshold |
