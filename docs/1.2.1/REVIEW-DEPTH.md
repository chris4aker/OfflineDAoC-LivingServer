# Review: the major bosses and the places at their depth floors (#403, #404)

The reviewer is independent and did not write the nine commits reviewed on `wt6/depth-all` (`f7df71b3` to `285aa3bf`,
`git log fix/1.2.1..wt6/depth-all` before this review). They bring the major bosses and the places up to the DepthReport
floors:
- 34 new yells for 13 Hibernian bosses;
- 133 book pages raised past 100 words (73 Hibernia, 52 Midgard, 8 Neutral) and 5 new fourth pages;
- 100 new bot lines;
- 6 first-entry lines and 7 areas for the proving fields, the Shrouded Isles, Albion's Shrouded Isles and Odin's Gate;
- Caer Sidi's summary reworded back to 500 words;
- 18 Neutral boss files whose `bookPages[0]` repeated `bookPage`, de-duplicated;
- `lint_lore.texts()` now reads every page of `bookPage` and `bookPages`.

The fixes are in `24fa1391` and the commit that adds this file. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the scripts and the lore engine in this tree), **source** (checked against the period
text online), **data** (the file's own facts and chapters), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a fact or tip that is wrong for its source or the code, or a contradiction a player would notice.
- **Low:** prose, repetition, a guess worth a later look.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **Nothing was cut.** I ran a word-subsequence check of every old string against the new one, in all 117 changed JSON
  files. Only four strings fail it, and each is fine:
  - Mutilators' old second page now stands, one sentence longer, as the first page.
  - The "si dungeons" tell is now "dungeons of the isles" (decision 8).
  - Caer Sidi's summary was reworded (C1).
  - Abermenai's hidden wren fact was corrected (F1).

  Every other change only adds words.
- **The writing is good.** The raised pages stay in their tellers' voices (Kolfinna to her daughter, Thyri's Galplen
  tales, the Bjarken bark-gatherers, Derbáil's list, Noirin and Coll of the Domnann guides). Most added sentences carry an
  image or a fact the page did not have. Four did not: they repeated a nearby page, pointed at the wrong noun, broke a
  referent, or used a formula the batch had already used. All four are reworked (P1-P4). No owner rule is broken. No High
  findings.
- **Three yells were muddled** (Y1-Y3) and are reworded. **One bot line put a myth in the wrong place and gave it a
  detail the source lacks** (F1).
- **Every check passes** (see Tools). All of this is tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Book pages | 0 | 0 | 4 | 1 |
| Yells | 0 | 0 | 3 | 1 |
| Bot lines and facts | 0 | 1 | 0 | 3 |
| Areas and first entries | 0 | 0 | 2 | 4 |
| De-duplication, Caer Sidi, cuts, lint | 0 | 0 | 0 | 4 |

## Book pages

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| P1 | `locations/caledon` page 1 | Low | The new sentence (the huscarl touches the cairn by the Midgard gate each time she passes) repeats what page 3 says of her almost word for word (**read**). | Page 1 now gives the cairn its own detail from the story ("the grovewood have let it be"): "the grovewood stand close round it and have never once leaned on it". Page 3 keeps the huscarl. |
| P2 | `locations/vale-of-balor` page 2 | Low | "Their mother is the size of a cart" followed the sentence about the Fomorians, so it read as the Fomorians' mother (**read**). | "The swarm's mother is the size of a cart". |
| P3 | `locations/worlds-end` page 0 | Low | "A proper asking is made once, they say, and never twice" went in between "a proper asking should be answered" and "They do not say how", so "how" lost its referent (**read**). | "They do not say how, only that a proper asking is made once and never twice." The old sentence stays whole. |
| P4 | `locations/hibernia-wilds` page 2 | Low | "She said it with her hand on my arm, the way she said everything that mattered" uses the batch's own formula a second time (`wretch-of-winter`: "the way she said everything about winter"), and it adds nothing (**read**). | The grandmother gets a story that fits the bog queen's birds: they sang her own sister to sleep on that road one autumn, and the sister woke at home a week later humming a tune nobody else knew. |
| I1 | 133 raised pages and the 5 new fourth pages | Info | Read in full. Checked against the files' stories and facts, and they agree, for example: Hugi's slow thinking and the cyclopes' eye; Vorkald's whisker box; Thor's servant killing the clay giant (Þjálfi and Mökkurkálfi); the Murdaigean braids that come loose, which is the Fianna's failure in Keating; Thidranki's crust on the stone set against the tomte's butter tale; Abermenai's dumb dead from the cauldron; the Lurikeen trader at Mag Mell. That last one is only a trader in the town (the town has an elven smith in the quest data), so it does not make Mag Mell a Lurikeen town. Nothing to fix. Small habits that are not worth undoing: "knotted cord" is now used for both Cothrom Gorge and Gripklosa; "she said it with her hands..." is used once. | None. |

## Yells (13 Hibernian bosses, 34 new lines, all 200 characters or fewer)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| Y1 | `bosses/develin` aggro | Low | "Every pooka in this wood owes me. Every one of them will collect." Pookas that owe him do not collect from anyone. The story makes him their master, bound by the bargain he won (**data**). | "Every pooka in this wood answers to me. Every one of them will come." (His fight is the pookas at his side.) |
| Y2 | `bosses/melancholic-fairy-queen` aggro | Low | "The trumpets sound for you. They sound for no one now." contradicts itself. The trumpets are her own fanfare (**data**). | "The trumpets sounded for me once. They sound for no one now." |
| Y3 | `bosses/wyvernic-wraith` slay | Low | "Fed to the hatchling. Fed." The wraith is the ghost of the hatchling the courts fed (**data**). | "Fed, as the masters fed the hatchling." |
| I2 | the other 31 | Info | Each speaks in its boss's voice and agrees with its story and fight. Badb's hooded crow over the ford. Cailean's trees. Flame's hearth bargain. Caithor bound by the dark men. Green Maw's mouth. Luriquay's spark coming out as the Flame. The Oghamist's strokes. Skeaghshee's rag-tree. Uafas Capall's water-horse, told in the third person as its older lines are. No game terms. | None. |

## Bot lines and facts

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| F1 | `locations/abermenai` lore line 10 and its myth fact | Medium | The line said the boy with no name "got his name on the abermenai shore, hitting a wren with one stone". In the Fourth Branch the naming happens on Gwydion's boat at Arianrhod's gate. The boat was made of sedge and seaweed on the shore toward Aber Menei. The text says only that the boy shot or cast at the wren and hit it in the leg between the sinew and the bone: no stone (**source**: Celtic Literature Collective, *Math*). The `myth` fact said "with a stone". | Line: "the boy with no name sailed in a boat of sedge made below abermenai, and got his name [hitting a wren in the leg\|for one cast at a wren]" (lower case, 128 characters at its longest). The myth fact now says what the text says and cites the CLC page. A new `original` fact owns the stone, which the story and book keep (owner rule: nothing cut). |
| I3 | `locations/emain-macha` page 2 | Info | "Her twins were born at the finish of that race, and the place took its name from them": *Noínden Ulad* gives a son and a daughter at the end of the race, and one etymology derives Emain Macha from that birth (**source**: UCC). The file's myth fact did not say so. | New `myth` fact citing UCC. |
| I4 | the other 99 new bot lines | Info | Every line is lower case and 140 characters or fewer at its longest choice. Where a line states a mechanic, it agrees with the code and data:<br>- porters offer only your own field: `BattlegroundTeleportOptions`;<br>- you release to your own portal keep;<br>- portal-keep guards kill any enemy (they are of no strength any band can match);<br>- renegades hold Abermenai, Murdaigean and Caledon, and Thidranki Faste is whoever took it last (no renegade there);<br>- Haluka is as strong as anyone Thidranki lets in (24, in a 20-24 field);<br>- the Murdaigean goblins, and the ire and rage wolves, bring their kin (faction bring-a-friend, `StandardMobBrain.BringFriends`);<br>- the field order Abermenai, Thidranki, Murdaigean, Caledonia.<br>Myth lines checked against their sources:<br>- Branwen's thirteen ships from Aber Menei, the seven survivors, and the cauldron's dumb dead and its four pieces (CLC, *Branwen*);<br>- Cad Goddeu's white roebuck and whelp, and Bran named from the alder sprigs on his shield (CLC, Cad Goddeu note);<br>- Myrddin's fifty years in Celyddon (CLC, *Afallennau*), the apple tree and the little pig;<br>- Keating's twelve books of poetry, nine javelins with a shield and hazel stake, and the four articles: a wife for her manners, no violence to a woman, refuse nobody, never flee nine (Library Ireland);<br>- Thidrandi, with nine riders in black before nine in white;<br>- Gwarwyn-a-throt's milk and bread, and how he drops the wheel when his name is called (SurLaLune, after Rhys);<br>- the Isle of Apples, its nine sisters and its first sister who heals and flies, and Caliburn forged on Avalon (Geoffrey);<br>- Camlann's adder (Malory);<br>- the boy troop of Emain (the *Táin*).<br>"Mag Mell" in the new text is never called a Lurikeen town. | None. |
| I5 | `locations/mount-collory` page 1 | Info | "it was the Dagda's, and ... no company ever went hungry from it" agrees with the file's myth fact (the cauldron that left no company unsatisfied, from the Four Jewels in *Cath Maige Tuired*). | None. |

## Areas and first entries

How areas are used (**code**):
- `LoreAtmosphere.Poll` checks every 3 s every location's areas in the player's region, whichever file they belong to, with `LoreEntryRules.InArea`: 2D within the radius, and `z: 0` means any height.
- An area line shows once per session. Its memory key is `area:<location>:<name>`, and it is set only when the line is actually shown, so two overlapping areas show their lines 30 s apart.
- Lore books (`LoreBooks.Near`) drop within twice an area's radius, but only for locations that list the region. That check compares the height with the area's `z` literally.

| # | Area | Severity | Finding | Change |
|---|---|---|---|---|
| A1 | `odins-gate` "the high snowfield" (642185, 620183, r 2,500) | Info | This is the sixth throw spot exactly: `GlacierGiant.cs` line 185, `642185 + Util.Random(-2000, 2000), 620183 + ...`, so the 2,000 scatter falls inside the 2,500 circle. It is 11,900 units from "the giant's ground". No other area in region 100 is within 21,000. | None. |
| A2 | `shrouded-isles` "Gnat's road" (region 181, 315400, 347300, r 1,200) | Info | It sits on Gnat's spawn as the file's own world-data fact gives it (Vale of Balor, by Aalid Feie). It is 6,800 from Aalid Feie's centre (r 3,000) and clear of the gap in the wall. | None. |
| A3 | `shrouded-isles` "the Aegir tide-line" (region 151, 282324, 354456, r 1,500) | Low | It has the same centre as `aegirs-landing`'s "the Aegirhamn shore" (r 2,000), so it is on the shore the writer meant. A player walking onto the shore gets both lines, 30 s apart (the throttle). They do not clash: pyslings and Soxulfr, then salt-weed. | None; noted. |
| A4 | `proving-fields` "the Albion road in Thidranki" (252: 35195, 45316) and "the Hibernian road in Murdaigean" (251: 25725, 28264), r 1,500 | Info | Exact midpoints of the portal keep and the central keep (the Keep table coordinates in the file's facts). Each is 3,700 and 12,400 from either keep, clear of the keep areas (r 1,200-1,500). "The same strides ... in every field" is literally true: the four fields share their keep coordinates. | None. |
| A5 | `albion-shrouded-isles` "the earth wall" (51: 536500, 511000, r 1,000) and "the wall's western reach" (525250, 510500, r 800) | Low | These are guesses. One is the midpoint of Anniogel and Caifelle, the other of the west fort and Anniogel, on the wall the Isle of Glass file describes. Neither circle overlaps an Isle of Glass area: the scout's path, Haralder's field, the west fort and Anniogel are 2,400-3,300 away. If the wall runs a little north or south, the lines are general enough ("the wall", "ring-fort to ring-fort") to read true nearby. Book drops cannot misfire: `LoreBooks.Near` compares the kill's height (about 4,500 on the isle) with the area's literal `z: 0`, so these areas never drop a book and cannot take the Isle of Glass book's drops. | None; flagged for an in-game walk (see owner question 2). |
| A6 | first-entry lines of `proving-fields`, `shrouded-isles` and `albion-shrouded-isles` | Low | `LoreEntryRules.LocationAt` never returns these files. The first two list no regions, and the third (region 51, no zones) is beaten by its seven zone files, which cover zones 51-57 (`Zones.xml`). So their first-entry lines, the six new ones included, are never shown in game. LORE.md says as much for files with no regions. They count toward the depth floor and do no harm. | None; owner question 1. |
| I6 | all seven areas | Info | Unique names in their files; 3 lines each, 60 characters or fewer; radius 800-2,500 within 100-30,000; each region in the file's regions or given (schema mirror 0). | None. |

## De-duplication, Caer Sidi, cuts and the lint

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| I7 | 18 Neutral boss files | Info | `LoreBible.BookPagesOf` joins `bookPage` and `bookPages`, so a `bookPages[0]` equal to `bookPage` was shown twice (**code**). In 17 files `bookPage` is unchanged and the new `bookPages` equals the old `bookPages[1:]` exactly, with no other key changed. In the 18th, `mutilators`, the old second page is now the only `bookPages` entry, one sentence longer ("Its shadow crossed the work tables again and again..."). No lore file has a repeated page any more. | None. |
| I8 | `locations/caer-sidi` story (507 to 500 words) | Info | Four rewordings: "Caldey's fishermen", "Albion's first company to climb", "The first autumn company", "crypts; none of its people". Every event, name and number is kept: nine lamps, Hedd's stick, the three shiploads and seven, the eighth mark, the summer company in Ilron's gallery, the three autumn companies, Dame Nest's eleven, the eighty behind Ennyn, the Bane broken for a time, and Dame Ysolde's sixty-seven. | None. |
| I9 | `tools/lore/lint_lore.py`, `tests/test_lint_lore.py` | Info | `texts()` yields `bookPage` and `bookPages` whether each is one page or a list, as `BookPagesOf` reads them, and a test covers all four shapes. A location's `book.pages` are checked by `fourth_wall` (narrative field), not by `texts()`'s developer-note check. That was already so before these commits. | None. |
| I10 | everything | Info | Nothing cut (see the verdict). | None. |

## Owner questions

1. **First-entry lines that never show** (A6). The overview files (`proving-fields`, `shrouded-isles`,
   `albion-shrouded-isles`) have first-entry lines for the depth floor that the engine never shows, because a zone file
   or no region always wins. They could be shown on entering any field or isle once a day, or left as codex-only
   writing.
2. **`LoreBooks.Near` and `z: 0`.** An atmosphere area with `z: 0` means "any height", but the book drop compares the
   height with 0 literally. So no `z: 0` area drops a book unless its radius is very large (Malmöhus's "frozen ruins").
   This is older than these commits and is a code change, so it was not made here (no dotnet). It decides whether the new
   wall areas will ever drop Albion's Shrouded Isles book.
3. **Walk the guessed wall areas** (A5) once in game: `/lstest lore` on the Isle of Glass earth wall.

## Tools

All on this branch after the fixes, without dotnet (`UT_LoreBible` not run):

- `python3 tools/lore/depth_report.py --exclude-epic`: 0 below for major bosses, minor bosses and locations (the only
  non-zero lines are epic bosses, outside #403/#404: story summary 14 of 157, bookPage 2-4 pages 145 of 157).
- `python3 tools/lore/schema_mirror.py`: 108 locations, 578 bosses, 4 history files, 0 problems.
- `python3 tools/lore/lint_lore.py`: 0 problems, 0 game-term warnings, 69 template labels (cap 69).
- `python3 tools/lore/name_counts.py --check`: 0 over cap.
- `python3 -m unittest discover -s tools/lore/tests`: 39 tests OK.
- `python3 tools/lore/mechanic_trigger_coverage.py --check`: 15 fire, 0 can't.
- `python3 tools/bot-voices/build_corpus.py --check` and `--verify`: pass, corpus matches its sources (16,611 lore bot
  lines checked).
- `python3 tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `python3 source/tools/epic_encounter_catalog.py --check`, `python3 tools/lore/build_encounter_adds.py --check`: pass.
- `python3 tools/bot-voices/check_runtime_names.py`: ok.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 tests OK.
- `python3 -m unittest discover -s source/tools/tests`: 295 tests OK (1 skipped).

Nothing needed regenerating.
