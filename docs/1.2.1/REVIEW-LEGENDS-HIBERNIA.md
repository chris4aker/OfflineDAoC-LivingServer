# Review: the Hibernian boss legends rewritten in-world (owner, 2026-11-21: "Rewrite that")

The reviewer is independent and wrote none of the eleven commits reviewed on `wt4/legends-all`
(`29055a21`..`b6664eb9`, "Hibernian boss legends in-world (1/n)" to "(11/n)"): the `legend` and game-term bot lines of
the 175 boss files whose realm is Hibernia. The Hibernian location files that retell a boss tip (Galladoria, Hy Brasil,
Sheeroe Hills, World's End, Cothrom Gorge, Fomor, Tur Suil, Marfach) were read as well; the rewrite did not touch them.

The fixes are in `a5637518` and the commit that adds this file. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the boss's script, `EpicEncounterMechanics` and `docs/encounters/epic-encounters.json`
in this tree), **data** (checked against the boss's own facts, which come from the world database), **read** (judged
as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a fight tip that is wrong for the code, or a contradiction a player would notice.
- **Low:** prose, consistency, the shared standard.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The writer's own scales.** The Hibernia writer measured distance in arm's reach, a few paces, a dozen paces, a
  stone's throw, half a bowshot, a bowshot and a long bowshot (about 800, 1,500 and 2,500 units for the three bowshots,
  read off the old numbers), anchored add health to the boss's own strength and big health to Balor's 40,000. All of it is
  converted to the shared standard: **68 distances** to paces at 30 units, **7 damage figures** recomputed against a robed
  caster of 1,000 health (the writer's anchor drifted: 400 was "half", 700 "more than half", 800 "most"), **14 health
  figures** to "as much killing as N raiders" at 2,000. "A dozen paces" for 350 units is the standard already (12 paces)
  and stays. Percentages stay fractions. In all, 83 conversions in 44 files, and the four mechanic fixes below use the
  standard too.
- **Tips checked against the code: 166.** Every tier phrase of the 175 legends against the boss's level (by script, all
  pass), 94 return times against the facts (all pass; Rinne's "about ten minutes" is 580 s), and about 70 spell, radius,
  health, trigger and switch claims in 50 epic and scripted legends read against their scripts. **Wrong for the code: 7
  tips (M1-M7)**, all but one older than the rewrite (the writer kept them); the rewrite itself made none wrong.
- **The known issues:** Olcasgean (one 250,000 pool, die together, puzzle, throw and bot throw on) was already right in
  the boss file, its story and chapters; **Easmarach's** legend was right but its own chapter and Galladoria's chapter
  still said the falls throw nobody now (M2). **Maldahar** "a band's quarry, not a raid's" with "no tricks" is wrong for
  the code (M1). **Tinka** is right (GameEpicNPC: 1.5 damage, 18,125 health, confusion and nearsight immune, 1,500 s). The
  **Earthmender crafts** in the Earth primal's legend match `EarthmenderAccepts` exactly (Guardian: blademaster, hero,
  champion; Magical: eldritch, enchanter, mentalist; Natural: bard, druid, warden; Shadowy: nightshade, ranger; animist and
  valewalker all four; pets always, `source is not GameSummonedPet`), and `AllowEarthmenderClassPuzzle` is on since 1.2.
- **Nothing was cut.** A word-difference of every old visible string against the new one (2,541 changed runs) shows only
  game words removed and replaced; of the 203 pure deletions, all but a handful are bare levels, "named", "resists",
  "spawn", "tether" and the old Olcasgean "kill one trunk then the other" advice that the code contradicts. The fixes here
  remove nothing but the false half of two chapters (kept as the past: "for a few seasons...").
- **Fourth wall:** no software word in any visible Hibernian field. Two game words the writer kept ("rebuff" twice in the
  Oghamist legend, "cooldowns" twice in Kierac's lines) are reworded. **Pending chat words** (left, counted in legends and
  bot lines): grats 116, lol 114, melee 98, group 69, caster(s) 97, camp(s) 59, quest 37, mana 34, pet(s) 40, drop(s) and
  dropped 42 (most are "when he drops"), taunt(s) 6, loot 2. **Map positions** like "(about 31.9k/30.7k)": 94 in legends
  and bot lines.
- **Chat lines:** none over 140 characters at their longest choice. **Duplicates:** 12 repeated bot lines (8 made by the
  rewrite, 4 older, one of them inside one file) are now distinct (L3).
- All of this is tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Fight tips against the code | 0 | 7 | 1 | 3 |
| Standard and wording | 0 | 0 | 4 | 2 |

## Fight tips against the code

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `maldahar-the-glimmer-prince` legend, bot line | Medium | "He is a band's quarry, not a raid's ... a strong group with no tricks can do it". In code he is a `GameEpicBoss` with 40,000 health at 73 (a war party's foe, twenty raiders of killing) and two spells the legend never named: a lifedrain on his target and everyone within 250 of it, and an energy burst on his target and everyone within 550 of it, 1,150 each, 10 s recast (**code**: `MaldaharTheGlimmerPrince.cs` `LifeTap`, `PBAoe`). | The old hands' "band's quarry" kept as their saying; then "he is a foe for a war party of the realm's champions and takes as much killing as twenty raiders", both tricks told (8 and 18 paces, more than a robed caster's whole strength), "only the shield-bearer stands at him". Bot line "a strong war party can do him ... stay off whoever hes on". |
| M2 | `easmarach` chapter 2, `locations/galladoria` chapter 6 | Medium | Both said the current "has slackened in the seasons since, and the falls no longer throw anyone back". The push behind the falls ships on, bots too (`Ship(AllowDisplacement/AllowBotDisplacement, "Easmarach")`), as does close range (`AllowPositionPuzzle`) (**code**). The legend, story and bot lines were already right. | Elaborated, not cut: "For a few seasons the Sylvan said the current had slackened ... Then the current came back as strong as it ever was, and it still is"; "the slack seasons were the falls drawing breath". |
| M3 | `easmarach` legend, fact | Medium | "At thirty it runs back into the waterfall, heals an eighth" read as every time. With `epic_easmarach_endless_falls_heal` off it heals once a fight and comes back down 50-80 s later (`EasmarachRetreats`, `EndWalk`) (**code**). The hidden fact said it pushes "a player". | "At thirty, once a fight, ... and fifty to eighty seconds later it comes back down to fight on". Fact: the push takes players and bots within 1,000 of the way past it; the heal is once a fight. |
| M4 | `cuuldurach-the-glimmer-king` legend; `locations/sheeroe-hills` legend | Medium | Neither told the **wing-gust throw** (2-5 raiders within 2,500 every 60-80 s, bots on the aggro list too, the fall up to about 60% of a raider) or the **messengers** (from half health, every 80-90 s, four to six runners that each call glimmers where their path ends). Both ship on for him (`AllowDragonThrow`, `AllowBotDisplacement`, `AllowDragonMessengers` for `HibCuuldurach`) (**code**). Gjalpinulva's legend tells both. | Both told in the boss legend (83 paces, "more than half your strength", "casters and archers run the messengers down"; a messenger takes less killing than one raider, 1,500 health) and in one sentence of the Sheeroe Hills legend. |
| M5 | `spindler-broodmother` legend, fact | Medium | Her teleport was missing from the legend, and the hidden fact said "her teleport is off for bot raids". Every 25-45 s she takes one raider within 2,500 who is not her target and moves them 3 s later; `AllowDisplacement` and `AllowBotDisplacement` ship on for her (**code**). | Told: "snatches one raider within 83 paces ... companions and the realm's adventurers as well as you ... walks back". Fact corrected. |
| M6 | `giant-sporite-cluster` legend | Medium | "It is eight, not one" without the shared life: every body's damage sets every other body's health, and when one dies all die (`GiantSporiteCluster.TakeDamage`, `ProcessDeath`) (**code**). | "they share one life, so a wound to any bloom is a wound to all of them and they die together, as much killing as fifty raiders between the eight". |
| M7 | `conservator` legend | Medium | "Its energy blast hits everyone within a long bowshot" (about 2,500 on the writer's scale); `COaoe` has radius 1,200 (**code**). | "within 40 paces of the one it is fighting"; close range "more than 27 paces from its pool" (800). |
| L1 | `orey-eyed-oghamist` legend | Low | Unchanged from before the rewrite and thin: no numbers, no word that he never walks home (`ReturnToSpawnPoint` does nothing while he lives). | Elaborated from the script: the blast (five seconds to gather, every 20-30 s, four-fifths of a robed caster within 33 paces), the curse (13 paces round the one he fights), the ranged cut (every 3 s, half a robed caster, on anyone he fights from more than 10 paces), no home to walk back to. |
| I1 | `olcasgean` legend, story, chapters, yells; `galladoria`, `hy-brasil` | Info | One 250,000 pool, die together, two safe spots within 200 of each spot, throws every 12-20 s below half within 1,500, bots too (**code**). Every Hibernian file now tells it that way; the old "full life each" and "kill the first trunk, then the second" survive nowhere. Distances converted (7 and 50 paces). | Distances only. |
| I2 | `tinka`, `corrupt-primal-of-earth`, `elro-the-ancient`, `archilus` | Info | Tinka as above (**code**). The Earthmender crafts as above (**code**). Elro's treant windows (95-90%, 60-55%; the 25-30% window can never fire) and Archilus's 90% are right, and only `GamePlayer` hits count for both (**code**). | Health and distances only. |
| I3 | 44 epic and scripted legends | Info | The rest of the sample holds: Krackenschtein (300 close range, 250 in 1,000, bolt, four throw spots, bots thrown), Myrddraxis (heals, stuns at fifths, annihilation at quarters, 1,800, 1,100 in 450, 800), Anurigunda (14 household within 2,500, ring 350, waves at 40 and 20), Nogoribando (debuff 350, haste 50%), the octonoid mothers (tether 2,600, broods, resists), Kontar, Curengkur, Cronwort, Marfoirwort, Gneiss, Dremcis and Beomarbhan, the three Ladies, Avartack, Caimheul, Kierac and the Master of Pain, Balor's eye, Aroon (300,000, tether 3,000, the damage order kept as an old account), Beatha's heal kept as an old account (off), Xaga, Evern, the primals, Uaimh (**code**). | None. |

## Standard and wording

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| L2 | 44 boss files | Low | The writer's own scales (see Verdict). Notable drifts: "half a robed caster's strength" for 400 (Cronwort, Curengkur), "more than half" for 700 (the Ancient Black Oak), "most" for 800 (the Blue Lady), "more than a robed caster's whole strength" for 2,000 (Myrddraxis); Aroon "more than seven times Balor's"; Tinka "more than half the Red Lady's strength"; adds as "a sixtieth/fifteenth/twelfth of her strength". "Well beyond a long bowshot" stood for a 2,600 leash and for a 2,500 call radius alike. | Converted: two-fifths, seven-tenths, four-fifths, twice; "as much killing as a hundred and fifty raiders", "nine raiders", "a quarter of the killing of one raider" (500), "one raider" (2,000), "a raider and a quarter" (2,500), "two and a half raiders" (5,000); "more than 87 paces" for 2,600, "from as far as 83 paces" for 2,500. Unstated distances ("a few hundred units") are "some ten paces". |
| L3 | 21 boss files | Low | Twelve bot lines repeated across files ("group too low" in eight, "one band of our best is fine" in three, "back 3 min after he drops", "drew them singly", "two shields held both", "enemy realm showed up", "interrupted her every cast"), and Giant Caithor's first rumour twice in one list (older). | Each made distinct, in the same voice ("group too green for the outlaws", "atur is back 3 min after he drops"). |
| L4 | `kierac-the-destroyer`, `orey-eyed-oghamist` | Low | "save cooldowns", "saved cooldowns", "rebuff" (twice): game words the rewrite kept. | "save the once-a-fight arts", "saved the big arts", "chant them back". |
| L5 | legends and bot lines | Low | "purge" stays in five bot lines and two legends; Albion and Midgard keep it too (17). It is a game word, but it is also a plain one. | None. Owner question 3. |
| I4 | 175 legends | Info | Tier phrases match the boss's level in every file that has one; the 50 raid legends name none, as in the other realms. "A full war party of champions" (Fuilwort, Tachtwort) is the 70+ tier, not health. | None. |
| I5 | Hibernian location files | Info | The location legends and bot lines were not part of the rewrite and still speak the old way (levels, "named", "pull", "AoE"). Their boss tips agree with the code after M2 and M4 (World's End's Archilus and Elro, Cothrom Gorge's worts, Fomor's households, Tur Suil's eye, Marfach's oak). | Owner question 2. |

## Tools

These ran on the commit that adds this file, all exit 0:
- `tools/lore/lint_lore.py`: 0 problems, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 19 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK (corpus.json matches its sources).
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK (with `test_lore_claims`).
- `python3 -m unittest discover -s source/tools/tests`: 295 OK (1 skipped).

Nothing needed regenerating. `UT_LoreBible` (dotnet) could not run here; it must run in CI or on the owner's PC before
merge. All of this is tests only; nothing has been seen in game.

## For the owner

1. **Maldahar.** The old advice called him a band's quarry; the code makes him a 40,000-health war-party foe with two
   heavy spells. The legend now keeps the old saying as the Sheeroe hands' and tells what he does. Recommendation: keep
   it so; if he is meant to be a band's fight, that is a change to his script, not his legend.
2. **The Hibernian location legends** were not rewritten in-world (levels, "named", "pull", "AoE", "tanks"). Albion's and
   Midgard's reviews will show whether theirs were. Recommendation: one pass over all location legends on the same
   standard, so the bestiary and the region pages speak alike.
3. **"Purge"** and the other pending chat words (counts above). Recommendation: keep "purge" with them in the chat
   decision; in legends "shake it off" would do if the owner wants it gone.
