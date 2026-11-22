# Review: Midgard and Neutral boss legends in-world (owner 2026-11-21, "Rewrite that")

The reviewer is independent and did not write these commits. The twelve Midgard/Neutral commits on `wt4/legends-all`
were reviewed (`49afcae5` to `ae247380`, in `git log fix/1.2.1..wt4/legends-all`): every boss file whose realm is
Midgard (185) or Neutral (55), 240 files, and the Midgard/Neutral location files that retell a boss tip (40 files; the
writers changed none of them).

The fixes are in `f70b6179` and `6811a1a6` on `wt4/rev-mid`. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the boss's script, `EpicEncounterMechanics` and `docs/encounters/epic-encounters.json`
in this tree), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a fight tip that is wrong for the code, or a contradiction a player would notice.
- **Low:** prose, consistency, the standard not applied.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The standard held.** 1,415 visible strings changed in the 240 files. Strength tiers: 234 bosses have a level in the
  catalog or the old text, and every one that names a tier names the right one (Neutral as "any realm's"); 22 never
  named a strength and still don't. Health as raiders: 67 of 67 checked against the catalog's `maxHealth` (2,000 a
  raider). Distances: 132 old unit figures turned into paces, none more than a third off (30 units a pace). Damage: 40
  old figures turned into shares of a robed caster, all within the rounding the standard allows. No "champion's
  strength", no Balor fractions, no percentages, no stated distance as a bowshot in the legends, no map positions in
  the boss files. Three slips: two tier phrasings ("a war party of champions") and one bowshot for a stated distance
  in a Legion chapter (L1).
- **Nothing was cut.** A word-diff of every old visible string against the new one: the removed words are the game
  terms themselves ("faction" 55, "level N" about 90, "resist", "debuff", "pull", "named", "aggressive", "cc immune",
  bare numbers) and each has its in-world rewording beside it ("sworn to the", "a foe for...", "lost on him",
  "sapping", "draw", "known", "attacks on sight", "no charm or stun holds him"). The 17 strings that got shorter are bot
  lines that lost a level ("too few for a 58" to "too few for him"). The only true deletions are wrong tips that the
  writer took out (Hurika's "doesnt throw any more, the gust message is harmless") and the typo word "some".
- **The fight tips are right.** About 90 tips were checked against the code across 30 bosses (Hurika, Torst, Hakr,
  Vifil, Kvasir, Agmundr, Otrygg, Ozur, Hord, Vagn, Birghir, Guthlac, Fornfrusenen, the Glacier Giant, Boligar, Cronk,
  Mortufoghus, the Mistress of Runes, Thane Dyggve, Aros, Jarl Ormarr, Gudlaugr, Issorden, Legion, the Behemoth,
  Beliathan, Abdin, Oro, Dagar, Loken): intervals, radii, health, resists, damage, chances, who is picked. Four were
  wrong, all older than this pass and carried over (M5-M8).
- **The flagged narrative contradictions are settled** (M1-M4): Hurika, Hakr, Vifil and Kvasir now tell the favour or
  the habit coming back, as the code ships it, without a word of the old tale cut.
- **Every check passes** (see Tools). All results are tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Narrative against the code | 0 | 4 | 0 | 0 |
| Fight tips and hidden facts | 0 | 6 | 1 | 3 |
| The standard and readability | 0 | 0 | 5 | 3 |
| Locations that retell boss tips | 0 | 1 | 1 | 0 |

## Narrative against the code

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `hurika` story, chapters "Hrafn's Spear" and "The Nest Between Them", loremasterTopic, legend, bot line | Medium | Story, chapter and loremaster said "nothing leaves the ground" since Hrafn's spear. Her gust throws a random raider within 1,000, bots included, every 15-20 s (`TorstAndHurika.cs` 629-653; `AllowDisplacement` and `AllowBotDisplacement` ship on for "Hurika") (**code**). A briefing said "draw her down to the flat", but her patrol is off and the legend says fight her on her perch. | Each keeps its sentence and goes on: "so the giants said for years after the spear... The giants say the favour of the wind came back to her when it would not come back to her wing"; the chapter "That was the word of the first seasons after the spear, and it is not the word now..."; the chicks' "gale that, for years, could not lift anything. It lifts strangers again now, and it still will not lift her"; loremaster "until the wind came back to her". Legend: "within some about" to "within about". Bot line: "she keeps her perch, dont try to draw her down. fight her there". |
| M2 | `icelord-hakr` story, chapter "Touchdown", loremasterTopic, aggro yell, legend | Medium | "He has not thrown anyone since" (story, chapter, loremaster) and the yell "Touchdown! Ha! No... no, not any more." While any iceweaver lives he tosses a random raider, bots included, every 10-20 s (`IcelordHakr.cs` 205-266; both switches ship on for "Hakr") (**code**). | Story and chapter keep the hatchling and Kula's word, then tell the weavers forgiving him: a new royal hatchling from the meltwater pool, the pair back at his feet, the companies coming down bruised; "He throws only while they live to see it; when the last of them is dead his hands go back to his sides, as Kula left them" (which is the code's `iceweaverUp`). Loremaster: "since his weavers forgave him he throws again while they live". Yell: "Touchdown! Ha! No... no, not any more, I told her. The weavers say otherwise." |
| M3 | `jailer-vifil` story, chapters "A Hand on the Collar", "The Last Empty Cell", loremasterTopic, legend | Medium | "He has carried no one since"; the stranger he turns from "is not locked away at all". Every 25-45 s he drops a random raider from his notice and ports him to the cells, bots included (`JailerVifil.cs` 136-230; both switches ship on for "Jailer") (**code**). | The old tale is kept as the giants' saying; then later companies empty his rows, he counts the row short "and the old work came back to his hands", and the stranger he turns from "wakes behind an ice-steel door far down the south tunnels, and has the long walk back up". "The jokes came back when the collaring did." Loremaster and legend say the same in a line. |
| M4 | `icelord-kvasir` story, chapter "The Frost That Mended Him", loremasterTopic, bot line, legend | Medium | "The frost stopped mending him." `EPIC_KVASIR_COLD_HEAL` defaults true: a cold hit heals him a fifth of his maximum health and does no harm (`IcelordKvasir.cs` 30-35) (**code**). An after-win bot line said "runemasters threw ice all fight and it worked". The legend said a cold touch "closes a fifth of his wounds" (it is a fifth of his whole strength). | Story and chapter keep the frost stopping and the seers' reason, then: "some seasons later the favour came back... whatever lies under the ice has had its fill of the cold... Midgard's companies leave the frost at home again." Loremaster: "some seasons later the favour came back, so frost mends him again". Bot line: "runemasters kept their ice at home all fight and it worked". Legend: "gives him back a fifth of his whole strength". |

## Fight tips and hidden facts

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M5 | `legion` legend, bot line | Medium | "Kill each graspering soul as it tears loose, one at every twentieth": at each 5% step he raises one soul for every four raiders within 2,000, at least one, at most 16 (`Legion.cs` 369-460) (**code**). His throw was not told at all: every 40-65 s he sends 8 to 16 raiders off his hate list, never the top five, bots included, up to the Behemoth's hall (`Legion.cs` 491-530; `AllowBotDisplacement` ships on for "Legion"). | "a batch at every twentieth of his strength, about one for every four raiders near him", and the throw told, with "keep wide of" the Behemoth on the walk back. Bot line: "a batch every twentieth of him, one per four of us". |
| M6 | `behemoth` legend | Medium | "only notices you within fifteen paces": its aggro range is 500, about seventeen paces (450 is its reach) (**code**, `Legion.cs` 716). The legend also kept "six hundred thousand" as a bare health figure. | "within about seventeen paces"; "three hundred raiders' worth of killing". |
| M7 | `mistress-of-runes` bot line | Medium | "her nearsight only ever goes for you, not us". The script that spawns, `MistressRunes` (`MinstressofRunes.cs`), picks any raider within 2,000 through `EncounterTargets.InRadius`, bots included; the legend says so. (The other file, `MistressOfRunes.cs`, picks players only, which is where the line came from; the catalog's encounter is `MistressRunes`.) (**code**) | "her nearsight goes for any of us, companions too, not only you". |
| M8 | hidden facts of `jailer-vifil`, `icelord-kvasir` (two), `beliathan`, `high-lord-oro` | Medium | The facts said Vifil's port to the cells and Kvasir's cold heal are "OFF in Living Server", that Vifil "sends a player", that Beliathan's minion rises from "a GamePlayer" and that Oro drains "every GamePlayer". Both switches ship on; Beliathan uses `EncounterTargets.AsRaider` and Oro `EncounterTargets.InRadius`, so bots count (**code**). | Corrected because they were wrong; sources kept. |
| M9 | `cronk`, `prince-abdin` legends | Medium (owner question) | "(by seventy-eight, for a minute)" and "(the raiders rate it at seventy)": a stat point and a parry chance are game numbers no raider counts. | "(deeply, enough to leave a fighter slow and clumsy, for a minute)"; "(from his front he turns about seven blows in ten)" (`ParryChance => 70`). |
| M10 | `council-ozur` legend, bot lines | Info | Crowd counts ("at 50 or more", "under 50 of us") are heads a raid leader counts: in-world enough, and the thresholds are the code's (`Ozur.cs`: 50 and 100 within sight; under 50 a fifth more lost on him; 50-99 strength 200; 100+ strength 350 and a fifth less lost). The same goes for Gjalpinulva's "27 people to over 100" and Nosdoden's "80 to 100". | None. |
| L1 | `icelord-steinvor`, `mutilators` legends; `legion` chapter "The Choir" | Low | "a war party of champions" (three times) is not a tier phrase; a chapter gave Legion's 1,400 as "a little more than a long bowshot". | "a war party of the realm's champions" / "of any realm's champions"; "a little more than forty-five paces". The other bowshots and stone's throws in scope (Boligar's and Torst's book pages, Hrolleif's story) are images, not stated figures, and stay. |
| I1 | 30 bosses, about 90 tips | Info | Checked and right: e.g. Torst's eddies (10,000, 110 cold, radius 300 = ten paces) and his switch to a raider within fifty paces; Agmundr's 650 cold in 500 (two-thirds, seventeen paces); Birghir's 15-20 s bolts within 2,500 (eighty paces); Guthlac's 35-60 s spike and 35-45 s pin; Fornfrusenen's 200-unit shield (seven paces), shards of 50,000 each taking a quarter off it above seven-twentieths; the Glacier Giant's six landing spots and its last three-twentieths; Boligar's 72 s trance in 450, every 60 s, ports every 25-45 s to four spots; Cronk's 400 heat on seven hits in ten; Mortufoghus's throw within fifty paces and the fall's fifth; Thane Dyggve's 4 s Mjollnir every 30 s in 350; Abdin's reach of 450 (fifteen paces); the Behemoth's kill on every swing and half its blows swallowed. | None. |
| I2 | `test_lore_claims` | Info | No corrected fact or line says "real player" or "never a bot"; the test passes. | None. |
| I3 | Other realms (outside scope) | Info | Two narrative lines in other realms still say a shipped throw is gone: `bane-of-hope` (Albion) bot line "bane of hope doesnt throw anyone any more" (`BaneOfHope` ships both switches on) and `easmarach` (Hibernia) "the falls no longer throw anyone back" (his push ships on). Left for those realms' reviewers. | None here. |

## The standard and readability

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| L2 | 18 bosses' bot lines | Low | "reset"/"resets" (28) and "tether" (3) were left in Midgard/Neutral bot lines, while Albion and Hibernia have none ("goes home whole", "walks home"). | All 30 lines reworded the way the legends already say it: "goes home whole", "whole again after 30 sec alone", "they never turn home", "chase a long way before they turn home". |
| L3 | 18 bot lines in 17 bosses | Low | **New duplicate bot lines.** Taking the level out made distinct lines identical across bosses: "easy 54" / "easy 50" to "easy one" (five bosses), "too few for a 53" / "a 58" to "too few for him", "56, no tricks" / "54, no tricks", "65 hits hard", and older shared ones ("bad pull" x4, "cc immune, dont bother" x2). | Each made particular to its boss ("easy one, small for raumarik", "too few for the wyvern", "the captain hits hard", "bad draw, the pack came with him"...). No two bot lines in scope are now the same. |
| L4 | `behemoth` legend | Low (settled) | "beyond even the whole muster of any realm" goes past the top tier. It is true for the code (level 99, 300 raiders' worth, every swing kills, `Legion.cs` 700-705), so it stays, and opens with the standard's own colour: "certain death, beyond even the whole muster of any realm". | As described. |
| L5 | 18 legends and bot lines | Low | "in this world" stands for the old "in the data" ("the medicine men have no spells in this world", "Arica isn't actually in this world"). The last review accepted it; it reads as a raider contrasting old accounts with what stands there now. | None; counted. |
| L6 | `hurika` legend | Low | Typo "within some about thirty-five paces". | "within about thirty-five paces". |
| I4 | 1,415 changed strings | Info | Readability: a random 30 legends and their briefings were read in full. They keep the raid leaders' voice and read well; the tiers sit naturally ("a foe for the realm's best, a full band of them"), "shield-bearer", "draw", "entrance", "pin to the ground", "sapping" are used the same way in every file. No bot line or yell over 140 characters once templates are chosen (`build_corpus.py --check`). | None. |
| I5 | pending chat words | Info | Left for the owner and counted (boss files in scope, all fields): camp 258 (most are in-world camps), caster 148, quest 144, group 140, drop/dropped 103, melee 94, lol 90, pet 81, mana 23, grats/gz 17, loot 8, taunt 5. Map positions: none in the boss files; 5 in the Midgard/Neutral location files' chat. | None. |
| I6 | game terms in narrative | Info | `fourth_wall.py` passes. Words that look like game terms in narrative are in-world: "named" (a name given), "roots" (trees), "cast" (a spell is cast), "kite" (Loken bound "as a kite is held to its string"), "dots" (tally marks on Svarhamr's beam). | None. |

## Locations that retell boss tips

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M11 | `locations/tuscaran-glacier` legend and bot lines | Medium | "Otrygg is an add fight" (his pack refills to eight at once; the boss file says put the damage on him); "When Kvasir dies, regroup, because the glacier heard it" and a lore line "the giants start moving" (nothing comes; the boss file says so); "send only the tanks and a few melee" to Ozur, who counts everyone in sight of him. | "Otrygg is a fight against his pack, which comes back as fast as it dies: hold it off the healers and kill him"; "regroup if you like... but nothing comes: the rumble is only noise"; "rumbles like the giants are moving. [nothing comes though\|...]"; "with fifty or more of you in sight of him he hits hard, so send the shield-bearers and a few blades and keep the rest back out of his sight". |
| L7 | `shrouded-isles`, `faraheim`, `varulvhamn`, `dodens-gruva`, `iarnvidiurs-lair`, `spindelhalla`, `darkness-falls`, `trollheim` | Low | The writers changed no location file, so the boss tips they retell kept the old game terms beside boss files that no longer use them: Gnat "level 35", "below 95% health", ants "(30-34)", "no experience", "AoE the ants", "a solo 35"; Valmak's "mez"; "pull" four times; "aggro range"; the Behemoth "level 99"; "big hp". | On the standard: "a foe for a hardened band of veterans", "nineteen-twentieths of its strength", "each a foe for a seasoned band", "they teach a hunter nothing", "blast the ants all together", "a lone raider who is a fair match for it"; "trance"; "draw"; "they dont see you from far"; "beyond any muster... every swing kills"; "a lot of killing each". The rest of the location texts (not boss tips) are untouched. |

## Tools

These ran on `6811a1a6`, all exit 0:
- `tools/lore/lint_lore.py`: 0 problems, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 19 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK (16,471 lore bot lines; corpus.json matches its sources).
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK.
- `python3 -m unittest discover -s source/tools/tests`: 295 OK (1 skipped).

Nothing needed regenerating. `UT_LoreBible` (dotnet) could not run here; it must run in CI or on the owner's PC before
merge. All of this is tests only; nothing has been seen in game.

## For the owner

1. **Stat numbers (M9).** A debuff's 78 points and a 70 parry chance were reworded as what a fighter feels ("slow and
   clumsy", "seven blows in ten"). Recommendation: keep it so; a raider counts heads and seconds, not points.
   Crowd counts (Ozur's 50 and 100) stay as numbers because they are heads.
2. **The Behemoth (L4).** Kept as "certain death, beyond even the whole muster of any realm". Recommendation: allow
   the one step past the top tier for the one foe the code makes unkillable in practice.
3. **"In this world" (L5).** Eighteen Midgard/Neutral lines contrast the old accounts with "this world". Recommendation:
   keep; if the owner reads it as a wink at the software, the alternative is "here" or "these days".
4. **Location legends.** Only the boss tips the locations retell were put on the standard. The rest of the Midgard and
   Neutral location legends still use the old raiders' shorthand (levels, "tank", "melee", "named"), which the owner's
   rule of 2026-11-06 allows in advice. Recommendation: a later pass, if the owner wants the locations to match.
5. **Two other realms' leftovers (I3):** Bane of Hope's "doesnt throw anyone any more" and Easmarach's "the falls no
   longer throw anyone back" contradict switches that ship on.
