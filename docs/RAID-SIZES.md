# Raid sizes

How big each automatic realm raid is, and where the numbers come from (owner, 2026-11-04: raid sizes as they were in
the era). Until then every dragon, epic dungeon, Summoner's Hall and Darkness Falls raid needed 200 level 50s (Krondon,
Trollheim and Tur Suil 160; the others 96-120) and recruited up to 300, and the raids of 2026-11-04 ran with 148-277.

## The rule

- **Full size:** the period's *comfortable* raid at that encounter, not its minimum. It is the encounter's
  `SuggestedBots` in `AutonomousRealmRaid.cs`. A small roster lowers it to half the realm's free level 50s (never
  under 40), and `realm_raid_minimum_bots` replaces it when set.
- **Starting size: 125% of the period size** (`realm_raid_start_size_percent`, owner 2026-11-05). The first night on
  the period sizes won 1 raid in 7 on the owner's test copy (five defeats after two wipes each, with the Special Forces,
  and one time-out): bots play well below 2003 players. The period size stays the measure; the start sits mid-range.
- **Win-rate sizing** (`RealmRaidRecruitmentPolicy.Size`): each win at that encounter trims 10 points of the period
  size; each defeat or time-out adds 20.3 (doubled on 2026-11-05 so a raid finds its size in a few attempts: four losses
  take it from the start to the ceiling). The size settles where the encounter is won about two times in three. Each
  raid's Herald record notes the size it ran at ("Raid size: 145.3%."), and the next raid there carries on from it.
  - **A time-out counts only after a wipe** (owner, 2026-11-05). A raid that runs out its four hours after one or more
    wipes is a loss; one that never wiped is neither a win nor a loss and leaves the size where it was. Marfach
    Caverns timed out after 91 pulls on an encounter it never reached: a routing fault, not a lost fight
    ([NIGHT-SIEGES-AND-MARFACH.md](NIGHT-SIEGES-AND-MARFACH.md)). The Herald still says "Timed out" and now ends with
    the raid's wipes ("Wipes: 0."); an older record without that note counts its time-out as a loss, as before.
  - **Floor 70%:** about the period's "possible, but tough". Apocalypse was "possible with good groups, but tough"
    at 80-90 against a comfortable 90-100.
  - **Ceiling 200%** (owner, 2026-11-05; it was 150%): room for bots that play worse than 2003 players. The raids
    defeated on the first night already had up to 180% of their period size with the Special Forces. Never past the
    realm's 300.
- **Recruit cap:** a quarter above what the raid must stage, in whole parties (RecruitCap of the full size, scaled by
  the same percentage).
- **Short start and hub departure** (owner, 2026-11-11: "go with who came"): after the hub's 30 minutes the raid leaves
  with whoever gathered, and at its staging deadline it goes in with whoever is there, three parties (24) at least.
  Until then it was half of what the sized raid needs: on the owner's PC (build e17a92b) Midgard's and Hibernia's epic
  rallies, sized up to 134 and 142 after losses, gathered 55 and 69 in 90 minutes, never left their hubs and ended
  "Failed rally" without a fight.
- **A loss under strength is no result:** a raid that never had the bots it was sized for (the most it recruited is
  under what it must stage) and is defeated or times out does not move the next raid's size or the Special Forces'
  call-up (`REALM_RAID_ENDED ... mostRecruited= underStrength=True`; in the ledger its details end "Under strength: 55 of
  134."). A win under strength is a win. The Herald still tells it as a defeat.
- **Epic Special Forces** (owner, 2026-11-04): "keep 24 and if they are called in on the same raid type where 24 made
  the second run win, it's 16 now". They don't scale with the raid's size. Per encounter: 24 to start; a win they took
  part in makes the next call-up there a party (8) smaller, down to 8 (they always answer a wipe); a win without them
  changes nothing; a defeat or a time-out after a wipe adds a party back, up to 24 (a time-out without a wipe changes
  nothing). Two losses in a row still bring them from the
  start. The Herald's record of a raid they joined ends "Epic Special Forces joined: N."; older records count as
  raids without them.
- **Forced raids** (launcher) are unchanged: 200 to go in, 300 reserved. Their results are not part of the record.
- **After a restart** no automatic raid opens until the bots have finished logging in (the startup ramp, at most ten
  minutes after its end): the full size is half the realm's free level 50s up to the period size, and two minutes
  after the restart of 2026-11-04 that gave Tuscaran Glacier 58 instead of 100.

## Raising a period size (1.2.2)

Owner, 2026-11-22: "If the mechanics and tactics are working but the period raider size from logs isn't cutting it, and
adding more bots without it becoming a Zerg wins, we should bump the suggested raider and period size."

**No encounter is raised.** 1.2.2 first raised Summoner's Hall 64 → 80, Galladoria 100 → 112 and Tuscaran Glacier
100 → 120 on their sims. An independent audit of those sims (2026-11-23) found that none of them was sound evidence, and
the owner withdrew all three (2026-11-23: "Revert all three"). The Green Knight's raise to 80 had been withdrawn the day
before ("option 1"). Every encounter has its 2026-11-04 period size, and `RaidPeriodSizes.Raises` is empty. The rule
stays, with stricter evidence (below). The withdrawn raises and the audit's reasons are under "Withdrawn on audit".

**The rule.** An encounter's period size (`SuggestedBots`) may be raised when, and only when, all of these hold:
1. **Its tactics work.** The encounter's simulation reproduces the failure seen in a PC log, and with the current
   tactic that failure is gone. A sim whose tactic still fails at every size is no reason to raise it: the tactic is
   fixed first.
2. **It still loses at its period size and wins with more bots.** With the current tactic the sim misses the target
   at the period size and reaches it at a larger one. The target is the win balance's (`realm_raid_target_win_percent`,
   67%: about two wins in three). A sim of one mechanism rather than a whole fight (a spawn timer, a damage race) is
   not enough on its own: see the evidence rules.
3. **It is not a zerg.** See below.
4. **The evidence meets every one of these** (owner, 2026-11-23, after the audit):
   - **a. The game's loss condition.** The sim loses the way the game counts a wipe
     (`RealmRaidDungeonRoute.IsWipe`): half of the engaged raiders fallen, or the encounter reset after being taken to
     70% or lower (`ResetWipeLowestPercent`). Raiders really die in it. A model that cannot lose (a spawn count, a phase
     time) cannot show that a size loses.
     A PC log's real deaths per attempt are in `RAID_ATTEMPT_END died=` (against `left=`, the fallen who did not die)
     and one `RAID_DEATH` row per death ([RAID-ATTEMPT-ROWS.md](RAID-ATTEMPT-ROWS.md), #644; tests only so far).
     The Roesia and Green Knight sims write the same rows (`SimRaidRows`, `build=sim`; RAID-ATTEMPT-ROWS.md, "Rows from
     the sims"), so a sim's attempt and a logged one compare line by line for b.
   - **b. It reproduces the PC.** At least one PC kill **and** one PC loss, each at the size its log shows, come out of
     the sim as a win and a loss. A target taken from the model's own estimate of a PC fight is not a log figure.
   - **c. The control at the real sizes.** The no-zerg control (the rules from before the tactic) is checked at the
     size the game really starts the raid at (125% of the raised size, `RaidPeriodSizes.ControlStartPercent`) and at
     that size plus the 24 Epic Special Forces (`AutonomousEpicSpecialForces.MaximumMembers`), not only at 100%. If the
     control wins there, numbers alone win in the real game, and the size is kept.
   - **d. One row, one encounter.** A raid row shared by several encounters (a whole dungeon) is split before one
     boss's evidence raises it.
   - **e. Pinned in C#.** The evidence is pinned in a C# test in the repo (`RaidPeriodSizes.Raise.EvidenceTest`,
     checked by `UT_RaidPeriodSizes`), not only in Python outside it.

It is raised to the **smallest whole-party size** (a multiple of 8) that reaches the target with the current tactic. If
the target lies past the no-zerg limit, it is raised **to the limit** and no further. A raise is per raid row. Only an
encounter with a sim is raised: "keep" means no sim gave grounds to change it.

**Not a zerg** means all of these:
- **At most twice the period source's own figure:** the 2003-2004 guide's group count times 8, or the players it names
  for a comfortable kill (`RaidPeriodSizes.Raise.SourceBots`). Summoner's Hall: "approximately four to five full
  groups", 40, so at most 80. Galladoria and Tuscaran Glacier: about 100
  people, so 200. Twice is the win balance's own ceiling (200%, owner 2026-11-05), the room for "bots that play worse
  than 2003 players".
- **Never above the realm's 300** (`RealmRaidRecruitmentPolicy.MaximumBots`).
- **The extra bots win because of the tactic.** At the raised size, at its 125% start and with the Special Forces, the
  sim's control (the rules from before the tactic) still loses: it wins under a third of its attempts. If the old rules
  win at those sizes too, numbers alone won, and the size is kept.
- **The bosses are never touched.** A flood guard keeps the cap it had (`EncounterFloodGuard`): the Green Knight's trees
  stay capped at the Albion dragon raid's 70, and no guard reads a raised raid's size.

After a raise, win-rate sizing works from the new period size exactly as for every raid: a 125% start, 70-200%, the
Special Forces after a wipe.

**Records carry over in bots, not percentages.** Since 1.2.2 a raid's Herald record says what its size is a percentage
of ("Raid size: 125% of 80."), and the next raid reads it against the period size today (`RaidPeriodSizes.Rebase`). A
row a 1.2.2 test build wrote at a withdrawn raise ("125% of 80" at Summoner's Hall: 100 bots) reads as 156.25% of 64,
the same 100 bots. A row with no "of" (every row before 1.2.2) is a percentage of the period size.

**A raise needs the roster.** The full size is half the realm's free level 50s, up to the period size, so a raise only
takes effect with twice the new size free. With fewer, the roster sets the size, as before; with "go with who came" and
the under-strength rule, a bigger requirement can also mean more attempts that end as no result.

### Withdrawn on audit (2026-11-23)

The independent audit re-ran the sims (tests only, models, no PC result) and found these problems. The owner withdrew
all three raises.

| Raise | Withdrawn because |
|---|---|
| Summoner's Hall 64 → 80 (Summoner Roesia, #410) | At 80 the tactic wins 0 (Midgard), 7 (Hibernia) and 15 (Albion) of 64 with real stats: it does not work by its own model. More curers per party means fewer wins (the fresh-Pain cure starves heals). The heal was fitted by hand (400 → 730) to one log number; at 790 Albion wins 63 of 64 at 80, so a fitted knob decides the result. At the 100 a raid starts at, the 1.2 cure rule (the control) wins about 26-38 of 64 (Albion, Hibernia), so the extra bots would win by numbers. The same position as the Green Knight's withdrawn raise (losing at the limit). |
| Galladoria 100 → 112 (Spindler Broodmother, #408) | The sim is a proxy (newborns alive at her death; no raider deaths, mesmerize, burn, win or loss), and the deciding gap (3.41 against 2.49) is under one newborn. The 2.49 "target" is the model's own value for `ccba364`, not a log figure. A PC log contradicts it: `1dc66b6` killed her in 12.7 minutes at the period size (13 parties) with the old 45% share, the model's "fail" case. The logged failure (`e3b8238`: up to 44 newborns, wipes at 15% and 22%) is far outside anything the model produces. At 350-425 damage a minute 14 parties fail too. The row covers the whole dungeon, raised on one boss. At the 125% start (16 parties) the old share already met the model's target. |
| Tuscaran Glacier 100 → 120 (Queen Kula, #409) | The model (Kula's phase as a damage race) has no loss condition: no deaths, wipes, heals or enrage. The "20.1 min" target is the model's own estimate of `ccba364`, not a logged kill time. The raids lost at King Tuscar (twice on `ccba364`, at about 124 bots, above 120), and the King has no model. The row covers the whole dungeon, raised on Kula. At the 125% start (125, and 150 at the raised size) the old rule already meets the model's target. |

What to measure before any of them is tried again: Spindler's `EPIC_RANGED_ADDS alive=` and her mesmerize counts in a
PC sweep, then a model with deaths and her mesmerize; King Tuscar's hammers against the raid's heals; Roesia's cure
against heal balance (the tactic first, not the size).

### 1.2.2: each encounter with a 1.2.1 sim

Win rates are from Python ports of the 1.2.1 C# sims (each port reproduces the numbers its C# test pins), swept across
raid sizes in whole parties. "1.2" is the control: the rules before the 1.2.1 tactic. Tests only: model results, not
game results. The three raises this table first supported were withdrawn on audit (above); their rows are kept as they
were measured, with the decision column corrected.

| Raid (issue) | Sim | 1.2.1 tactic, by size | 1.2 rules (control) | No-zerg limit | Decision |
|---|---|---|---|---|---|
| Summoner's Hall: Summoner Roesia (#410) | `UT_RaidSim121SummonersGreenKnight`, 64 seeds; real stats `UT_RaidRealStats122` | 1.2.1 (assumed melee): wins of 64 at 64 / 80 / 88 / 96 / 104: Midgard 0 / 14 / 61 / 64 / 64, Albion 0 / 8 / 34 / 56 / 61, Hibernia 0 / 16 / 54 / 61 / 64. **Real stats (1.2.2)**, 64 / 80 / 88 / 96 / 104 / 112: Midgard 0 / 0 / 0 / 0 / 8 / 42, Albion 0 / 15 / 43 / 58 / 64 / 64, Hibernia 0 / 7 / 29 / 46 / 63 / 64 | 1.2.1: 0-1 of 64 at every size to 128; real stats: 0-5 at 80, 13-27 at 88-96 (Albion, Hibernia) | 80 | ~~64 → 80~~ **withdrawn on audit 2026-11-23: stays 64**. The tactic loses at 80 (0-15 of 64), more curers win less, a fitted heal flips it, and the control wins about 26-38 of 64 at the 100 start |
| Summoner's Hall: Grand Summoner Govannon | a cruder attrition model on three 80-bot play tests | demon phases won of 64 at 80 / 96 / 104 / 112: 0 / 30 / 60 / 64 (harsher case 0 / 10 / 37 / 57) | no 1.2.1 tactic for him | (same raid) | was riding on Roesia's raise (withdrawn); not a reason of its own. It was fitted to three 80-bot play tests run with `epic_govannon_full_heal` on (now off), and its no-ring control is assumed. Real stats agree with its premise: his demon (STR 800) hits plate for 3,059 (the cap) every 3.6 s, one raider a swing whatever the raid's size |
| Green Knight (#412) | real stats `UT_RaidRealStats122`; **1.2.2 rework model `UT_RaidSim122GreenKnight`** (every role in reach, his walks to his four points, buffs the bots cast, loss = no raider left; Python mirror `tools/raid-sims/gk_mirror.py`, 32 seeds) | Tag and burn, 80 a second on a tree, wins of 32 at 40 / 48 / 56 / 64 / 72 / 80 / 104: buffed (a Cleric's or Paladin's spec AF) **0** / 5 / 26 / 22 / 27 / 32 / 32; unbuffed 0 / 0 / 1 / 4 / 19 / 28 / 32. At 50 a second on a tree 0 of 16 to 80; at 120, buffed 0 at 40 and 15 of 16 at 56. Every raider contributes (at least 40 of 40) | the 1.2 catalog and 1.2.1 round 2: **0 of 32 at every size 40-104**, buffed or not | 80 | **40, owner's choice ("option 1", 2026-11-22); not yet won at 40 in the model.** By the rule the size would be 56 buffed (26 of 32) or 80 unbuffed. The first 1.2.2 model's win at 40 (17 of 32) came from benching the casters and holding him in place; withdrawn (audits, 2026-11-23) |
| Marfoirwort (#412) | the same file; real stats `UT_RaidRealStats122` | at his real swing (2.71 s, no miss; the 1.2.1 model tried 3.0-4.5 s at 85%): 64 of 64 at 40-80 at 200 and 400 a raider-minute (at 100: 0-36 at 40, 64 from 48) | 0 of 64 at 40-56, 0-10 at 64-80 | 80 | **keep 40**: it wins at 40 |
| Galladoria: Spindler Broodmother (#408) | `UT_SpindlerBroodSim` (her newborns, not a whole fight) | 13 parties (100): 3.41 newborns alive at her death; 14 (112): 1.93, all 8 add parties; the model's value for the `ccba364` kill (128) is 2.49 (no log measured it). Not in the original table: `1dc66b6` killed her at 13 parties with the 45% share, where the model gives 5.6 alive | from 16 parties (128) | 200 | ~~100 → 112~~ **withdrawn on audit 2026-11-23: stays 100**. A proxy with no win or loss; contradicted by `1dc66b6`; the deciding gap is under one newborn; the 125% start already met its target |
| Tuscaran Glacier: Queen Kula (#409) | `UT_TuscaranTrollheimSim` (Kula's phase, a damage race) | Kula's phase: 23.3 min at 100, 21.6 at 108, 20.0 at 120; the model's estimate of the `ccba364` raid (about 124) at the least pet leak is 20.1 (no logged kill time exists) | 21.4-27.0 min at 120 | 200 | ~~100 → 120~~ **withdrawn on audit 2026-11-23: stays 100**. No loss condition; the target is a model estimate; the losses were at King Tuscar (about 124, above 120), who has no model |
| Caer Sidi: the Crypt Lord (#407) | `UT_CryptLordCourtSim` (his patrol and the pull) | 0% joins on a guarded pull at every size 100-200 | 68 / 21 / 32-37% joins at every size | 200 | **keep 100**: size is not the lever |
| Trollheim: Nosdoden (#411) | `UT_TuscaranTrollheimSim` (the stall clock) | 0 of 200 time-outs at 64-128 (true by construction: the regroup is drawn from 45-59 minutes against the 60-minute clock, and attempt 2 is modelled as a win; a regroup 10% longer gives 68 of 200 with the fix) | 36 of 200 at 64, 11 at 128 | 128 | **keep 64**: no evidence about size either way (a raise would also lift his spirits' cap) |
| Golestandt, Gjalpinulva, Cuuldurach (#415) | `UT_DragonThrowReturnWave415`, 300 seeds | won at 70: 84 / 93 / 89% | 24 / 57 / 30% at 70; 99-100% from 80 (in the model; `9a173f4` lost Golestandt with 91 engaged on the old code) | 140 | **keep 70**: nothing argues for a change |
| Darkness Falls: Legion (#413) | `UT_LegionWounded413`, 300 seeds | 100% killed inside 20 min from 48 up (a model where Legion deals no damage and the raid cannot wipe), also at the real raid damage (6.2 a raider-second against his armour, the b1b6846 ledger scaled; the sim assumed 9) | 84% at 64, 98% at 72; at 6.2 a second 0% at 48-80 | 128 | **keep 64**: nothing shows a bigger raid is needed |

### Real stats (1.2.2)

The 1.2.1 sims assumed the bosses' hits. These are the real ones: each boss's row in the public OpenDAoC database
(`npctemplate`), its script, and the server's formulas (`UT_RaidRealStats122` writes them out and checks them). Two facts
change most of the numbers: with `force_mob_autoset_stats` (default on; the database sets no server property) a
template's strength, quickness and intelligence are replaced by 30 + (level - 1) unless the script sets them afterwards,
and an epic boss's damage is 2.25 times a monster's (`GameEpicBoss.DamageFactor`). The target is a level 50 bot in
full-quality armour: "high" without buffs or resists, "low" with a capped spec AF buff and 20% resists. Tests only.

| Who (source) | Level, STR / QUI used | Swing | A landed hit on plate / chain / cloth | On plate a second (high / low) |
|---|---|---|---|---|
| Green Knight (`npctemplate` 60161621; `OFGreenKnight.cs`: two-hander) | 79, 108 / 108 (autoset; template 420 / 100) | 3.62 s | 1,223 / 1,353 / 2,998 | 338 / 234 |
| his trees (`GKTrees`: 800 health, STR 150, QUI 80, level 47-49, no weapon) | 48, 150 / 80 | 2.88 s, 21% miss | 118 / 130 / 282 (its cap) | 32 / 22 (chain 36 / 25, cloth 78 / 53) |
| Summoner Roesia (18804; two-hander) | 75, 104 / 104 | 3.65 s | 1,056 / 1,168 / 2,743 | 289 / 200 |
| Grand Summoner Govannon (18801; two-hander) | 80, 109 / 109; demon STR 800 (script) | 3.61 s | 1,267; demon 3,059 (cap) | 351; demon 848 |
| King Tuscar (60162909; two-hander) | 85, 114 / 114 | 3.57 s | 1,507 / 1,667 / 3,374 | 422 / 292 |
| Queen Kula (60165083; one-hander) | 85, STR 350 with the King alive, 500 after (script) / 114 | 2.68 s | 1,733; 2,181 | 647; 815 |
| Spindler Broodmother (60166449; no weapon) | 81, 110 / 110 | 2.70 s | 895 / 990 / 2,128 | 332 / 229 |
| her newborns (`SBAdds`: 8,000 health, STR 250, QUI 80, level 56-59) | 56-59, 250 / 80 | 2.88 s, 0-9% miss | 218-249 / 242-276 / 355-384 | 68-81 / 47-56 |
| Nosdoden (60164545; one- or two-hander by form) | 80, 109 / 109 | 2.71 / 3.61 s | 864 / 1,267 | 319 / 351 |
| Legion (13333; script sets STR 200, QUI 100; no weapon) | 83, 200 / 100 | 2.76 s | 1,207 / 1,335 / 2,213 | 437 / 303 |
| the dragons (60157497, 694189, 678903) | 80, 109 / 109 | 2.71 s | 600 (`RealmEventPolicy.DragonAttackDamageLimit`) | 222 |
| Marfoirwort (60163696; no weapon) | 80, 109 / 109 | 2.71 s, no miss | (his leech: 450 body, 25% of hits) | - |
| Behemoth (60158340; no weapon) | 99, 128 / 128 (template 1,400 / 200) | 2.59 s (2.16 with the template's QUI) | every swing kills (`OnAttackEnemy` → `Die`, hit or miss) | 23 raiders a minute (28) |

Spells (NPC caster: damage x (1 + INT / 200), variance 0.6-1.0): Roesia's Pain 137-228 a tick before resists (the
sim's 125, from b1b6846's first minute, means about a third resisted: the log stands); her damage shield 80 x the
attacker's swing (280 a 3.5 s swing, the sim assumed 64); Govannon's Shroud 111-185 a 2 s tick; the Green Knight's Might
of the Forest 250 (no variance, the sim's 200 after resists stands). Health, armour and resists were already read from
the scripts and are unchanged. The raid's damage stays the PC ledgers' (b1b6846: 400 a raider-minute at Marfoirwort;
e3b8238: 350-700 on a newborn; `MarfoirwortFight`'s doc gives the 400 base as the Earthmenders' ledger lines, not Marfoirwort's: which fight it came from is unresolved); scaled to each boss's armour it gives the dragon sim's 4 a raider-second (kept) and 6.2
on Legion (the sim had 9).

Roesia with her real melee: the 1.2.1 heal (400) made a raid without cures fall in 0.7 minutes against b1b6846's 1.5, so
the heal was recalibrated (730: 1.05 minutes on average, 0.8-1.6, her lowest 85% against the log's 76%; the closest fit
the model gives). Healers cure Pain only one raider at a time in every realm: the database's Cure Poison spells (1371
Rebirth, 3072 Healer and Shaman Mending, 4983 Druid and Bard Regrowth) are all single-target (`Realm`, radius 0).

Behemoth (#414): 600,000 health, AF 550, 50% absorb, 40% / 70% resists. The raid's damage on him, the b1b6846 ledger
scaled to his armour, is about 145 a raider-minute; losing 23 raiders a minute, a raid without resurrections deals
145 x n^2 / 46 before the last falls, so it needs about 440 raiders (480 at the template's quickness), past the realm's
300, all inside his hall at once, which only Legion's throw reaches (8-16 raiders every 40-65 s). No bot raid is
possible within the raid rules; his exception (signed off 2026-11-15) stands.

### Starting sizes

| Raid | Period size | Starts at (125%) | Recruits up to | Can range (70-200%) | Recruits up to at 200% |
|---|---|---|---|---|---|
| Caer Sidi, Galladoria, Tuscaran Glacier | 100 | 125 | 160 | 70-200 | 256 |
| Crystal Cave | 120 | 150 | 192 | 84-240 | 300 |
| Golestandt, Gjalpinulva, Cuuldurach | 70 | 88 | 112 | 49-140 | 176 |
| Summoner's Hall, Darkness Falls, Krondon, Avalon City, Trollheim, Iarnvidiur's Lair, Marfach Caverns | 64 | 80 | 104 | 45-128 | 160 |
| Hall of the Corrupt | 48 | 60 | 80 | 34-96 | 128 |
| Dodens Gruva, Tur Suil | 40 | 50 | 72 | 28-80 | 112 |

## Sizes and sources

Most of the evidence is from Freddyshouse, the European DAoC community forum, in 2003-2004. Thread dates are given.
"Unverified" means no period count was found for that encounter, and the size follows a comparable boss.

| Raid | Was | Period size | Period evidence | Source |
|---|---|---|---|---|
| Golestandt (Albion dragon) | 200 | 70 | Feb 2003: "a number of players ranging between 125 and 160" in the guide author's four kills. Aug 2003: done with 17-24, "The ideal amount is 24 people", "It does take 2-3 hours to kill him with 23". | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [How many ppl to kill the dragon, Aug 2003](https://forums.freddyshouse.com/threads/how-many-ppl-to-kill-the-dragon.139907/) |
| Cuuldurach (Hibernian dragon) | 200 | 70 | May 2003: "120 brave Hibernians", but "about 30 people attack at any one time" and "5fg attacking the dragon"; the rest held the four entrances against adds. | [How to kill Cuuldurach, May 2003](https://forums.freddyshouse.com/threads/how-to-kill-cuuldurach.124821/) |
| Gjalpinulva (Midgard dragon) | 200 | 70 | Unverified. A Nov 2003 raid killed an unnamed female dragon ("She went down quick and in first try"), likely Gjalpinulva, with 3 groups and an exit-guard group (about 32). | [Lubricador's 3 FG Dragon Raid, Nov 2003](https://forums.freddyshouse.com/threads/lubricador%C2%B4s-3-fg-dragon-raid.159276/page-2) |
| Caer Sidi (Apocalypse) | 200 | 100 | Feb 2003: "a minimum of about 100 people is required", "The lowest number I have killed Apocalypse with on a live server is 105". Jul 2003: "With 80-90 I believe Apocalypse is possible with good groups, but tough. 90-100, Apocalypse should be straight-forward", "Almost everytime 90+ ppl show up", one raid of 130. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Caer Sidi Raid, Jul 2003](https://forums.freddyshouse.com/threads/caer-sidi-raid-sunday-20th-july.135873/) |
| Galladoria (Olcasgean) | 200 | 100 (a 1.2.2 raise to 112 withdrawn) | May 2003: killed by 140 after a 340-strong attempt failed; "I think it can be done with 100 ppl quite nicely". | [Olcasgean has been defeated!, May 2003](https://forums.freddyshouse.com/threads/olcasgean-has-been-defeated.125921/) |
| Tuscaran Glacier (King Tuscar, Queen Kula) | 200 | 100 (a 1.2.2 raise to 120 withdrawn) | May 2003: "we zerged King Tuscar and Queen Kula about a week ago with ~100 ppl", "with 100+ people". Jul 2003 sign-up: "4FGs would be nice, 6 awesome". | [Olcasgean has been defeated!, May 2003](https://forums.freddyshouse.com/threads/olcasgean-has-been-defeated.125921/), [Tuscaren Glacier Raid, Jul 2003](https://forums.freddyshouse.com/threads/tuscaren-glacier-raid-tuesday-15-07-03.135818/) |
| Summoner's Hall (Grand Summoner Govannon) | 200 | 64 (a 1.2.2 raise to 80 withdrawn) | Feb 2003: "approximately four to five full groups" (32-40). 64 because each realm's raid there also fights the other realms' raids on the way in. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/) |
| Darkness Falls (Legion) | 200 | 64 | Feb 2003: "as few as about three groups", "a group of about fifty players" has it easier. Feb 2004: "4 fg took him out", "3-5fg", "first time I think we were 100 or something". 64 for the same contest as Summoner's Hall. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Time to kill Legion?, Feb 2004](https://forums.freddyshouse.com/threads/time-to-kill-legion.4424/) |
| Krondon (Orylle) | 160 | 64 | Feb 2003: Orylle and Orshom Brong need "at least 50 or more players". | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/) |
| Crystal Cave (Xanxicar) | 120 | 120 | Feb 2003: "a raid of well over a hundred players". Jul 2005: "i seem him wipe 5 fg". | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Avalon city dragon raid, Jul 2005](https://forums.freddyshouse.com/threads/avalon-city-dragon-raid-crystal-caves.65679/) |
| Avalon City (Duraek) | 120 | 64 | Unverified: no count found. Sized like the other Shrouded Isles dungeon finals (Orylle). | - |
| Hall of the Corrupt (Aidon, Lord Gildas) | 120 | 48 | Feb 2003: "Archwizard: good for about two groups", likely Aidon. Lord Gildas: no count found. 48 for two finals and the way in. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [The Halls of the Corrupt, Apr 2003](https://forums.freddyshouse.com/threads/the-halls-of-the-corrupt.124084/) |
| Trollheim (Nosdoden) | 160 | 64 | Unverified: no count found for Nosdoden. Sized like the other Shrouded Isles dungeon finals. | [Nosdoden Beckons, Dec 2003](https://forums.freddyshouse.com/threads/nosdoden-beckons.164875/) (no count) |
| Iarnvidiur's Lair (Iarnvidiur) | 120 | 64 | Unverified for the boss. May 2003, a named in the lair: "he wiped about 4fg of us, we beat it with 4fg later on". | [Lair vs Iarnvidur vs Trollhiem?, May 2003](https://forums.freddyshouse.com/threads/lair-vs-iarnvidur-vs-trollhiem.127197/) |
| Dodens Gruva (Spiritmaster Aros) | 96 | 40 | Jan 2003: "At guildevent we did Aros with 3fgs", after "3 fgs and got our butts kicked". | [Dodens Gruva Named mobs, Jan 2003](https://forums.freddyshouse.com/threads/dodens-gruva-named-mobs-etc.110133/) |
| Tur Suil (Balor) | 160 | 40 | Dec 2003: "After all you only need 2fg's"; the raid had 3 groups. | [Balor/Quest Raid, Dec 2003](https://forums.freddyshouse.com/threads/balor-quest-raid-today-friday.163060/) |
| Marfach Caverns (Black Lady, Caimheul, Avartack) | 64 | 64 | Unverified: no count found. Unchanged. | - |

## The open-world big bosses (1.2)

Every big boss is in 1.2 (owner, 2026-11-14). The open-world bosses had no raid at all: ordinary groups walk around an
open-world epic, so the bots never fought them. Each now has its realm's raid (`RealmRaidOpenWorld.cs`), fought by the
dragon-fight engine with the boss's spawn in place of a lair: a hub of the boss's realm in its region, a staging ring
around the spawn, the regroup after a wipe, the Special Forces and win-rate sizing as for every raid. Most of them were one-
to three-group fights by 2003, so most sit at the realm raid's smallest full size (40).

| Raid | Period size | Starts at (125%) | Can range (70-200%) | Period evidence | Source |
|---|---|---|---|---|---|
| Coliath (Albion Shrouded Isles) | 64 | 80 | 45-128 | "about Legion difficulty": sized as Legion | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/) |
| Glacier Giant (Odin's Gate) | 48 | 60 | 34-96 | Dec 2003: two guild groups wiped in seven minutes; "done with 5-6 fgs"; earlier kills with 2 groups (2002-2003) | [FreddysHouse, "Glacier Giant", Dec 2003](https://forums.freddyshouse.com/threads/glacier-giant.604/) |
| Green Knight (Forest Sauvage) | 40 | 50 | 28-80 | Feb 2003: about Grand Summoner difficulty, 4-5 groups. Oct 2002: three groups tried and failed; Jun 2005: nine players with pets (three Theurgists, a Necromancer) took 40 minutes, "mostly because he runs around". 1.2.2 raised him to 80 for a while; withdrawn by the owner (2026-11-22), and his tactic reworked | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Allakhazam bestiary comments, 2002-2005](https://camelot.allakhazam.com/db/search.html?cmob=3753) |
| Evern (Breifine) | 40 | 50 | 28-80 | 2003: a full group was beaten, eight killed him in 20 minutes; the Green Knight's equal | Allakhazam (2003); the Albion guide above |
| Mortufoghus (Modernagrav) | 40 | 50 | 28-80 | Oct 2003: three groups couldn't get it under 50%; Apr 2003: 1.5-3 groups | Allakhazam; JeuxOnline 288849 |
| Spirit of Lord Emthoro (Inishail Island) | 40 | 50 | 28-80 | 2004: more than one full group | Allakhazam (Mar 2004) |
| The five Wretches of Winter (Raumarik) | 40 each | 50 | 28-80 | 2002-2003: groups of about 10; May 2002: "The human form wretch wiped out everyone 3 times" | Allakhazam; [FreddysHouse, May 2002](https://forums.freddyshouse.com/threads/glacier-giant-0-midgard-1.86858/) |
| Raumarik Revenant | 40 | 50 | 28-80 | May 2002: killed with the Wretch of Winter; no count | [FreddysHouse, May 2002](https://forums.freddyshouse.com/threads/glacier-giant-0-midgard-1.86858/) |
| Juggernaut (Cursed Forest) | 40 | 50 | 28-80 | 2002: a full group with bladeturn took it to 25% | Allakhazam |
| Curengkur, Marfoirwort, Mollunero (Hibernia Shrouded Isles) | 40 each | 50 | 28-80 | Unverified: no count found | - |

- **The schedule:** a realm's scheduled roll goes to one of these at `realm_raid_open_world_percent` percent (25 as
  shipped; 0 = never on the schedule, the GM sweep and the launcher still start them), so the dragon, epic dungeon,
  neutral and raid dungeon raids keep three quarters of the rolls, and the realm raids no more often than before. The five
  Wretches of Winter are one choice among Midgard's four. A raid whose start fails on the schedule (its hub has no route to
  the boss) is not picked again for two hours.
- **Not here:** Morgana (a quest NPC in the period, not a raid boss; no spawn in the world database), and Fomor City's
  Myrddraxis and Anurigunda (no path in on its navmesh; their raid plans steer the gamebots that fight there).
- **The GM sweep:** `/epictest sweep start openworld` runs these alone; `all` runs them with the rest.

None of these raids has been played: tests only.

## What the numbers mean for bots

The period sizes are for 2003 players. Two things pull the other way, and win-rate sizing is there to absorb both:
- **Easier over time:** dragons came down from 125-160 in early 2003 to 17-24 by August 2003 as gear and tactics
  improved, and to one group or less by 2008. The owner chose 70 (2026-11-04), between the two; a dragon won with 50
  present on 2026-11-04.
- **Bots play worse:** confirmed on the first night (1 win in 7). Raids start at 125% and grow up to 200% when they
  keep losing. Overnight the defeated raids already had 86-104 bots with the Special Forces against period sizes of
  48-64 (Tuscaran Glacier 94, sized 58 by the restart), so the owner raised the ceiling from 150% to 200%.

## Boss mechanics that count raiders (1.2)

Since 1.2 the bosses count and pick bots too. A few mechanics make an add or a heal per raider, so a bot raid at up to
200% of its period size would face far more than a full 2003 raid did. `EncounterFloodGuard` caps each such count at
the period size in the table above (`SuggestedBots`): Legion's adds and kill heals, High Lord Saeor's kill heals and
Beliathan's minions (Darkness Falls, 64), Nosdoden's spirits (Trollheim, 64), the Green Knight's trees and Oona's undead
(on the dragon raids' way, 70; the Green Knight has his own raid of 40 since 1.2, and the cap stays the dragon raid's 70), and Risnir Cruss's apparitions (no raid: 40, the smallest period size). Up to that size
the mechanic is the original; each cap is its own setting (`EncounterFloodGuard.SetLimit`, 0 = none). The server log's
`EPIC_FLOOD_GUARD` line shows a cap at work.

None of this has been played in game yet. `REALM_RAID_SIZED` in the server log shows each raid's size and why.
