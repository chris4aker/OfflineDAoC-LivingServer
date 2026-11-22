# Review: the Hibernian epic book pages and raised story summaries (#402, #405, #406)

The reviewer is independent and wrote none of the text reviewed: the five Hibernian commits of `wt6/epic-all`
(`3a5c19a4`, `d65a8a25`, `f5928adc`, `e2452739`, `c2902e34`). They give the 36 Hibernian epic bosses of Tur Suil, Fomor,
Galladoria, Marfach and Sheeroe 50 new book pages (a `bookPages` array after the untouched `bookPage`), one new
`original` fact per file, and a new paragraph for 14 story summaries (Abomos, Beatha, the Primals of Earth, Fire and
Water, Kierac, the Master of Pain, Norsob, the Geomancer, the mechanism, Qunilaria, the Broodmother, Tine and the
Lairmaster). Every new page and paragraph was read against its boss's chapters, story, legend, yells and facts, and
against the location files that retell the same events (`tur-suil`, `fomor`, `galladoria`, `marfach-caverns`,
`sheeroe-hills`, `connacht`, `tir-na-nog`, `history/hibernia`).

The fixes are in `b2e4ff7f` and the commit that adds this file. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **data** (the boss's own chapters, story and facts, or a location file), **code** (the mechanic as the boss's
`game` facts and legend give it), **canon** (HEROES.md), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a contradiction a player would notice, or a mechanic told wrong.
- **Low:** prose, repetition within the realm, the standard.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The writing is good, in the realm's own voice, and nothing was cut.** The pages are real in-world documents: brehon
  judgements (the freed spirits, the nine hostages, Muireann's face), triads, ogham at the ford of the Erne, the keen's
  verses and the terms of the keen for iron, letters, a tally stick, a satire, a children's rhyme. The 14 summary
  paragraphs summarise their chapters faithfully; none retells another paragraph of its story
  (`test_no_story_retells_a_paragraph` passes).
- **No owner rule is broken.** No hero stands in a present-day scene, there is no saint or churchman, nothing is after
  1.65, no companion is mentioned, and every name is under the cap.
- **Seven contradictions with the chapters or the location files** (M1-M7), all in new text, are fixed by rewording the
  new text. The worst were Bréanainn going back up the tower, which his chapter says he never did, and a second cairn of
  smooth stones on the shore that `balor` and `tur-suil` do not have.
- **The realm leaned hard on two forms.** "Whence the name? Not hard to say" or "whence its name?" opened 11 of the 50
  pages, and two pages used the same "the same ground under a second name" turn. Seven are recast; four remain. Four
  first-person pages shared the "X of Place, of the Y" heading and are varied.
- **Every check passes** (see Tools). All of this is tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Consistency with the chapters and places | 0 | 7 | 6 | 1 |
| Voice and repetition | 0 | 0 | 5 | 1 |
| Canon, facts and mechanics | 0 | 0 | 0 | 4 |
| Cuts, lengths and the schema | 0 | 0 | 0 | 2 |

## Consistency with the chapters and places

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `bosses/master-of-pain` page 1 | Medium | Bréanainn's tally said "I did go back rested". Kierac's chapter "Two in One Body" says he "never went back up the tower after that night" (**data**). His healer also read the stick, though his three menders "would never talk about it". | He leaves room on the stick for the next band, "I never climbed again", and the bands after him cut their notches on his stick. A clerk of the Howth barracks reads it. |
| M2 | `bosses/balor` page 2 | Medium | A cairn of smooth stones on the shore, begun by Fiachna's own stone. Balor's chapter "Lugh's Stone" and `tur-suil` put Fiachna's stone, and the companies' stones, on the sill of the one window, and the only cairn is the Gleann Glas cairn at the Vale's edge, Taise's custom, where companies leave a stone beside the Sylvan's (**data**). | The page is now about the Gleann Glas cairn: the Sylvan began it, Taise's watchers add to it, the smooth stone stays on the sill, and each company lays a second stone from its own country's stream on the way home. The fact is reworded to match. |
| M3 | `bosses/balor` page 1 | Medium | "The Sylvan will not say the second name." The chapter "The Vale of Ash" says the Sylvan call it the Vale of Balor (**data**). | "The Sylvan say the second name only when they must, and never at their fires." |
| M4 | `bosses/uaimh-lairmaster` page 1 | Medium | Cuach's last trade went once a season, the cave-folk came up to her, the den-keeper did not come and she left the honey. Her chapter: once a year; nobody came; she went in and found them digging; the lairmaster looked at her and turned back; she came home with the honey unsold (**data**). | Rewritten to the chapter's events in her voice. The fact is reworded. |
| M5 | `bosses/hurionthex` page 1 | Medium | The brehon "heard ... the two who had gone forward". In "The Face of Muireann" those two did not come back (**data**). | She hears the old walker, "the warden she held back", and the kin; the two were drawn "to their deaths". |
| M6 | `bosses/xaga` page 1 | Medium | The driftwood message went on after the garden sentence. The sporite chapter "Spores from a Staff" says the message "ended" with it (**data**). | The garden sentence is now the message's last; the cracked line below it is "not counted as part of the message". |
| M7 | `bosses/beatha` page 1 | Medium | "The women who kept the seed-jars with Xaga" (Xaga's chapter: "in every village one woman keeps them"); and the fire-keeper keeps the lid, though Xaga's seed-house is "kept exactly as she left it" (**data**). | Lisín had the triads "as a girl at the door of Xaga's seed-house". The lid is on the one jar Xaga left unsealed, still in the seed-house. |
| L1 | `bosses/kierac-the-destroyer` page 2 | Low | The western Sylvan "lay a leaf for him". Fainche's stone is "the only grave he has", and the leaf is Coill Bheag's custom in Norsob's hall (**data**). | "The western Sylvan are the ones who speak of him kindly." |
| L2 | `bosses/norsob-the-annihilator` page 1 | Low | A heap of leaves grows in the clearing, which his chapter and book page found with nothing in it; the realm's leaf is laid in his hall (**data**). | The heap began with the two leaves Muirgen and her warden laid there, and grows from those who come home from his hall. |
| L3 | `bosses/queen-qunilaria` page 2 | Low | Neasa spoke Pádraigín's saying ("do not cheer until the floor is still") as her own; her chapter has her repeat it after Pádraigín (**data**). | She credits Pádraigín: "I thought a queen was no spinner." |
| L4 | `bosses/chieftain-caimheul` page 1 | Low | "The winter after Tuathal Garbh broke the peace"; the chapter puts the sickness in the same winter (**data**). | "In the winter Tuathal Garbh broke the peace". |
| L5 | `locations/galladoria` chapter 4 | Low | The Uaimh "traded honey with the Sylvan for salt"; the Lairmaster's file and the new page trade honey for pale crystals (**data**). | "for salt and the pale crystals of their tunnels"; only words added. |
| L6 | `bosses/conservator` chapter "Taught to Hoard" (older text) | Low | The wardens say "arrows now find it from across the cave", then "Arrows fall short", and the story says "that door is shut as fast today"; its code reaches it only from close in (**code**: 800 units, the legend's 27 paces). | Words added only: "They said so for a season, and were wrong, and say so now: the door is shut as fast as ever." |
| I1 | the other 36 files' pages and the location retellings | Info | Checked against chapters and places: Ruadhán, Conn and Ailbhe (Tine, Beatha, Xaga); Pádraigín's six and five; Lorcán's three of eleven; Fearghal's four; Neasa and two; Bréanainn's twelve and four; Orlaith's 140, the 340 and the Connacht 250; Fiachna, Taise, Seanán and Clann na Fuinseoige; Gormflaith and the Ardee six; Ríonach and Nuala in `fomor` and `history/hibernia`; the nine hostages and Mochta; Gobnait; Lassar; Áed Dub and the ford of the Erne. | No other change. |

## Voice and repetition

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| L7 | 11 place-lore pages | Low | "Whence the name? Not hard to say" (6) or "whence its name?" (5), and Balor and Cuuldurach both turned on "the same ground under a second name" (**read**). | Kept in four, where the form carries the page: Balor (Gleann Glas), the Black Oak (Móin na Darach), Myrddraxis (the pool of the five mouths) and the Red Lady (Cnoc na Naonúr). Recast: Earth (a child's question about the empty valley, and the scar shown after rain), Water (an entry in the roll of Domnann's springs), the sporite colony, Cuuldurach ("Two names lie on the high ground"), Maldahar (the last road on the shepherds' count), Norsob and Olcasgean (named plainly). |
| L8 | `bosses/corrupt-primal-of-fire` page 1 | Low | Its fire-keepers' triads were a twin of Beatha's seed-keepers' triads, said in the same village in the same three-by-three shape; eight pages in the realm were triads (**read**). | Now the new fire-keeper's vow at the stone ring, every fire and rule kept. Seven triad pages remain, each a different kind (seed-keepers, wardens of the eldest, the walkers' ogham stone, the workshop, the silences of Tur Suil, the mothers' summers, the rules of bog oak). |
| L9 | `bosses/tine` page 1, `nogoribando` page 1, `norsob-the-annihilator` page 2, `queen-qunilaria` page 2 | Low | Nine first-person pages opened "X of Place, of the Y, as told..."; Tine's page and Xaga's page 2 both opened "Ruadhán of Domnann, warden, of the..." (**read**). | Tine: an entry in the fire-keepers' book of fires. Nuala: her words first, her name at the end. Fearghal: an entry in the roll of the Connacht barracks. Neasa: her answer to the young spears of Ardee. The other five keep their headings, which differ. |
| L10 | `bosses/myrddraxis` page 2 | Low | Ríonach's account restated the chapter "The City's Luck" sentence by sentence (**read**). | Now the druids' questions at her return and her answers, ending on a new point: "Count it how you like. I counted my company coming up the stair, and that is the count I keep." |
| L11 | `bosses/organic-energy-mechanism` and `corrupt-primal-of-water` stories | Low | The mechanism's new paragraph ended on a clause that joined Feidhlim, the watch and the Conservator's missed beat; the water's "the morning that news came" had no news before it (**read**). | The mechanism paragraph is in event order, with both sides of the heart and the purse; "the morning the news of Orlaith's company came". |
| I2 | the 14 summary paragraphs | Info | Each summarises its chapters (names, numbers, customs) and none retells a paragraph of its own story. Earth's and Abomos's keep close to their chapters' wording, which is fine for a summary. | None. |

## Canon, facts and mechanics

| # | Severity | Finding |
|---|---|---|
| I3 | Info | **Canon (HEROES.md):** no hero, Saint Patrick or churchman in any new text; Mag Tuired appears only as old legend; nothing after 1.65; no companion is mentioned (decision 7); `name_counts.py --check` 0 over the cap. The Azure's "what is open is holy" is their own firbolg sky-faith, not a church. |
| I4 | Info | **Facts from memory**, checked for plausibility and left: the brehons' honour-price (a person with no lord or standing has none) and body-price (paid by the one in whose keeping a person dies) fit early Irish law; a hostage's life forfeit when the surety's word breaks fits it too; the satire that raises blotches on a king's face is the Néde and Caier story of the glossaries; "of Avartack, son of Áed Dub" follows the ogham "X son of Y" formula. All are marked `original`. |
| I5 | Info | **The new facts** name each file's own documents; the Hibernian writers used no shared sentence. Four were reworded to follow the fixes (Balor, the Lairmaster, Myrddraxis, Fire). |
| I6 | Info | **Mechanics stated in new text** agree with the boss's `game` facts and legend: the Broodmother's snatch, the Master of Pain's curse on the shoulder, Kierac's shield of pain, the Geomancer's grip once hurt, the water guardian's one forgiveness, the Conservator's reach, Easmarach's throw, the eldest's door, the workshop throw. One lore detail is left: Scoth sees the stitched giant throw a gentarn, its own guard, while the code throws only raiders. She watched before any fight; it reads as the giant's temper, not a tip. |

## Cuts, lengths and the schema

- I7 **Nothing cut.** A word-subsequence check of every old visible string (`fix/1.2.1`) against the new one, in all 36
  boss files and `locations/galladoria`, finds every old word in order. Only new text was reworded, and old text only
  gained words (L5, L6).
- I8 **Lengths:** every new page is 100-160 words after the fixes (the recasts were trimmed back inside the band);
  every raised summary is 300-500.

## Tools

These ran on the tree with this file, all exit 0:
- `tools/lore/depth_report.py --kind epic-boss`: 0 of 157 below on chapters, summaries and book pages.
- `tools/lore/schema_mirror.py`: 108 locations, 578 bosses, 4 history files, 0 problems.
- `tools/lore/lint_lore.py`: 0 problems, 0 game-term warnings, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 38 OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK.
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK (`test_no_story_retells_a_paragraph` included).
- `python3 -m unittest discover -s source/tools/tests`: 295 OK (1 skipped).

Nothing needed regenerating. `UT_LoreBible` (dotnet) could not run here; it must run in CI or on the owner's PC before
merge. All of this is tests only; nothing has been seen in game.

## For the owner

1. **Two stones up Tur Suil.** Balor's second page now has each company carry a second stone, from its own country's
   stream, for the Gleann Glas cairn, besides the smooth stone left on the sill. `tur-suil` already has companies leave
   "a stone" at the cairn on the way down, so this only names where it comes from. Recommendation: keep it.
2. **Triads and place-lore.** Seven triad pages and four "whence the name?" pages remain across the 36 files.
   Recommendation: keep these as the realm's marks; ask for fewer if they still read as a habit in samples.
3. **Scoth's thrown gentarn** (I6). Recommendation: keep; if the owner wants every lore throw to match the code, the
   gentarn can become a Sylvan captive.
