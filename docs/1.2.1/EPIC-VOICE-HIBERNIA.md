# Hibernia's epic voice and teller openings (#405, #406)

Branch `wt11/v-hib`, 2026-11-22. Tests only: the lore checks below ran on this branch. Nothing has been seen in game
yet, and `UT_LoreBible` was not run (no dotnet here; `schema_mirror.py` ran in its place).

## The voice in brief

Midgard's epics speak as sagas. Hibernia's now speak as the old Irish tale and the learned forms around it: the poet's
place-lore, the triad, the rhetoric with its gloss, the brehon's judgment, the colloquy, the charm, and the tale that
names its own kind (an adventure, a voyage, a contention). The form is part of the story: Muadán's judgment leaves the
case open on purpose, the place-poem of Tur Suil stops at two lines because the realm hasn't finished it, and Lisín
leaves one line of the Root's rhetoric unglossed. Plain English, place-names glossed once, verse unrhymed. Each chapter
ends on a custom that is still kept. No Church, no saga formulas, no capped heroes, nothing marked `myth`. The full
guide is the new section "The epic voice" in [VOICE-HIBERNIA.md](VOICE-HIBERNIA.md).

## The encounters given it (one new chapter each, 545-690 words, before the file's last chapter)

| File | Chapter | Form |
|---|---|---|
| `bosses/balor` | The Watch from the One Window | the watchman on the height (Eochaidh's climb, then Fiachna's) |
| `locations/tur-suil` | The Rock of the Watch, in Verse | place-poem in quatrains, its last one left at two lines |
| `bosses/cuuldurach-the-glimmer-king` | The Judgment of the High Summering | brehon's judgment (claim by entry; the suit never closed) |
| `locations/sheeroe-hills` | The Names of the Summer Slopes | prose place-lore of six slope names |
| `bosses/olcasgean` | The Rhetoric of the Root, with Its Gloss | rosc and line-by-line gloss |
| `locations/galladoria` | Triads of the Water's Mouth | the wardens' triads |
| `bosses/easmarach` | The Adventure of Sétna Behind the Water | adventure tale (three wonders on the ledge, the hour that was a night) |
| `locations/fomor` | The Rowing West | voyage tale told backward, by those who stayed |
| `bosses/myrddraxis` | The Herb-Wives' Charm | charm answering the hydrars' song |
| `bosses/black-lady` | The Colloquy at the Bier | question-and-answer colloquy |
| `bosses/norsob-the-annihilator` | The Contention in the Unset Hall | contention for the champion's portion |

Every chapter has an `original` fact. Where a chapter uses something real (the early Irish legal entry, *tellach*, in
outline), the fact says so and claims no more. The chapters agree with their own files and with the files that tell
the same events: Eochaidh's three bands and Fiachna's stone (`balor`, `tur-suil`), Conaing's forty, Brónach, Dubthach
and Fiadh (`cuuldurach`, `sheeroe-hills`), the three hundred and forty, Orlaith's hundred and forty and the month of
sweet springs (`olcasgean`, `galladoria`, `easmarach`), Sétna's Walk and the Connacht spearman (`easmarach`), the bearers
and the seven hides (`balor`, `fomor`, `tur-suil`), Ríonach and the Breifine herb-wives (`myrddraxis`), the second night
and the green shoot (`black-lady`), and the wall between the warlords and Coill Bheag's leaf (`norsob`, `sargan`). The
chapters add one new name, Muadán of Basar, the brehon, which no gamebot list has.

## Three samples for the owner

**1. Balor.** Before (the plain telling, in "Lugh's Stone"):
> The first company to bring him down came up in the next spring, and the realm's records remember that it was small:
> two bands and a third to hold the stair, under a bard of Howth named Fiachna who had studied every account of
> Eochaidh's band. Fiachna carried a smooth stone in his pack all the way up the tower, for Lugh's sake, and his company
> laughed at him for it.

After (the same climb, watched from the one window):
> "What is in the pack?" said the king, and in the bards' telling that is the first time he has to ask.
> "A stone," said the watchman. "Smooth, out of a river."
> The king was quiet a long while, and the watchman waited, because it is not a watchman's place to hurry a king.
> "Not hard to know him," said the king at last. "That is no one. That is a bard of Howth named Fiachna, with a stone
> he will never throw. But I know whose stone it is. I knew it on another field, before I had a tower. Shut the window."
> "The window has no shutter," said the watchman. "You had it built so."
> "Then look away," said the king.

**2. Cuuldurach.** Before (book page): "Cuán's grandmother gave the shepherds' answer when the scribes asked her: the
high summering still, and the flocks will go back up when the grass does." After (the brehon's judgment):
> Therefore the high summering is neither the king's nor the shepherds', but in suit. A suit is kept up by whoever wants
> the ground. Let the shepherds keep it up. [...] The fee for this judgment is one fleece of the first green summer.
>
> The fee has never been paid. The brehons of Connacht keep an empty peg on the wall of their hall at Basar where the
> fleece is to hang, and the shepherds say Muadán got the best of the bargain, as brehons do: he is owed the one fleece
> that would mean the case was won.

**3. Tur Suil.** Before (Muirgen's page): "Their village of Gleann Glas lit one on the black crag for their boats on
nights of mist [...] The eye's first sight on Hy Brasil was the village that had lit its way." After (the place-poem):
> They steered for it: where else would they steer?
> They grounded on the shingle below the glen.
> The hides slipped on the beach for one breath's length;
> the eye's first sight was the house that lit the way.
>
> [...]
>
> Carraig na Faire was the rock of the watch.
> The watch is kept now from the other side.

**Openings (#406), three before and after:**
- `badb`: "From Derbáil of Ardee's list: the Cullen road after dusk." became "The Cullen road after dusk is on Derbáil of
  Ardee's list."
- `olcasar-geomancer`: "A Sylvan stone-song, with Aodhan's apology for the translation: Be patient, stone; you were
  patient when the world was water." became "Be patient, stone; you were patient when the world was water. So runs a
  Sylvan stone-song, with Aodhan's apology for the translation."
- `hurionthex`: "The Sylvan have always had shape-walkers:" became "Shape-walkers the Sylvan have always had:"

## Opening counts, before and after (Hibernia's 532 to 543 book pages and chapters; `tools/lore/repetition.py`)

| Opening | before (`d56adeda`) | after |
|---|---|---|
| From Derbáil | 27 | 1 |
| The Sylvan | 24 | 4 |
| From Coll | 19 | 1 |
| Noirin the | 9 | 1 |
| Tieru of | 7 | 3 |
| A Sylvan | 7 | 4 |
| From the | 7 | 4 |
| From Nóra's | 6 | 1 |
| Aodhan writes: | 4 | 1 |
| From Cuán's | 4 | 1 |
| From Aodhan's, From Muirgen's | 3, 3 | 1, 1 |
| Muirgen writes: | 3 | 1 |
| From Flann | 2 | 1 |
| first word "From" | 74 | 14 |

The top Hibernian opening is now "The first" (18, unchanged). The teller's-list formula fell from 1.14 to 0.04 per
10,000 words, "the druids / bards ... say" from 2.44 to 2.24, and Hibernia's share of narrative words in repeated
phrases from 3.63% to 3.42%. Eight story paragraphs that opened "The druids of Lough Gur say" and the like now open on
the claim. The caps in `tools/lore/repetition_caps.json` were lowered to these counts: Hibernia's openings, formula
rates and share, and the seven phrases these edits shrank. Every change is a decrease, and the keys are kept, so no cap
fell back to the looser cap for phrases and openings that have none. `--write-caps` was not used as it stands, because
it drops a key below its threshold and would loosen "From Derbáil" from 1 to the free 5. Other realms' caps are
untouched.

## Nothing old was cut

A word check against `d56adeda` compared every changed string with its new form. The only words gone are the formula's
"From" (61 pages), and "from the day" became "since the day" once. The eleven chapters were added without touching any
old string.

## Checks (all pass on this branch)

`depth_report.py` (0 below), `schema_mirror.py`, `lint_lore.py`, `name_counts.py --check`, `continuity.py --check` (47
facts, 0 contradictions), `repetition.py --check`, `reachability.py --check`, `tools/lore` tests,
`mechanic_trigger_coverage.py --check`, `build_corpus.py --check` and `--verify`, `debates_doc.py --check`,
`check_runtime_names.py`, `epic_encounter_catalog.py --check`, `build_encounter_adds.py --check`, the bot-voices tests,
the `source/tools` tests.

## Files changed

The 11 chapter files above. 84 more boss files for the openings: abomos-the-soultrapper, adar-llwch-gwin, adwyth,
ancient-black-oak, anhylaw, aodhnait, archilus, avartack-the-champion, azure-coseal, azure-dsaldor, azure-thror, badb,
balere, beatha, beomarbhan, blight, blue-lady, cailean, chieftain-caimheul, conservator, corrupt-primal-of-air,
corrupt-primal-of-earth, corrupt-primal-of-fire, cronwort, cuinn, curengkur, develin, donal, dremcis-fuiloltair,
drygau, eimear, elnara-erea, elnaras-pet, elro-the-ancient, evern, faerie-queen, fedail, flame, fuilwort,
geas-bound-protector, giant-caithor, giant-sporite-cluster, gneiss, goldenleaf, green-maw, hurionthex, ide,
ixthiar-broodmother, juggernaut, kierac-the-destroyer, kontar-the-corrupt, kunsgnos, lhia, lobigan, luriquay, madawg,
marfoirwort, master-of-pain, melancholic-fairy-queen, mikka, mollunero, nogoribando, olcasar-geomancer,
orey-eyed-oghamist, organic-energy-mechanism, overseer-of-the-silver-hand, queen-cliodna, razorbeak, red-lady, saemus,
sargan-the-conqueror, skeaghshee, spindler-broodmother, tachtwort, tawi-shon, thuxra-the-cackler, tine, treal, tudeg,
uafas-capall, uileog, windskimmer, wyvernic-wraith and xaga. Some of the 11 chapter files (balor, black-lady,
galladoria, olcasgean, tur-suil, fomor, sheeroe-hills) also had openings reworded. Also `tools/lore/repetition_caps.json`,
`docs/1.2.1/REPETITION.md` (regenerated), `docs/1.2.1/VOICE-HIBERNIA.md` (the epic voice section) and this file.

## For the owner

- Is the brehon's "claim by entry" (three entries, the third with a fire and a night's sleep) welcome as Hibernian law?
  It follows early Irish law only in outline and is marked `original`.
- The Fomorian voyage invents three islands (turned backs, the empty one-windowed tower, the island that sank). Keep them
  as the Fomorians' own account, or trim to one?
- An independent prose review of these eleven chapters, by someone who did not write them, is still to do (HANDOFF 1.5).
