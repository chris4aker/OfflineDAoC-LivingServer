# Review: the epic voice and the teller openings (#405, #406)

The reviewer is independent and did not write these passages. It reviewed `git diff a88622ae HEAD` on `wt11/voice-all`:
- `4ac55bfb`, `d82d4940`, `5d9f214c`: Albion (ten epic-voice chapters, the openings, the caps)
- `e5891ec7`, `5a34b86e`, `52f2e296`: Hibernia (eleven chapters in the old Irish tale's forms, the openings, the caps)
- `17f06bd4`, `b6c29522`, `f82ecb49`: Midgard and Neutral openings
- `bee973f5`: the realms combined; the caps take the lower value per key

The fixes are in the commit that adds this file. Paths are relative to `source/server/GameServer/bots/autonomous/data/lore/`.
Tests only: nothing here has been seen in game, and `UT_LoreBible` was not run (no dotnet; `schema_mirror.py` ran in its
place).

Labels: **verified** (checked against a translation online this time), **known** (from the reviewer's knowledge of the
text), **unverified**. Severity: **High** breaks an owner rule; **Medium** a canon error, a contradiction a player would
notice, or a failed floor; **Low** prose, repetition or citation detail; **Info** checked and fine.

## Verdict: pass after fixes

- The 21 chapters are the strongest writing in the lore. Each is a real form of its realm, used for what it does, not
  worn as a costume: the annal's silences (Camlann left blank), the hermit's "betoken", the vow as what one person does
  for the one beside him, the watchman on the height, the brehon's fee, the place-poem that stops at two lines. None reads
  as mock-Malory or stage-Irish. No "ye", no "-eth", no mist, no saints in Hibernia, no capped hero named.
- Every chapter adds something its file lacked: a document, a custom still kept, or an unanswered question. None
  retells its file's story in other words.
- Every `myth` fact holds against its text. None had to be relabelled `original`.
- One contradiction with Mythic's own text (the Dartmoor annal) and a handful with the files' own details are fixed.
- #406 reads better almost everywhere. Of 403 changed strings, 18 openings came out garbled or clumsy, and they are
  reworded. Nothing old was cut.
- Four Midgard book pages fell below the 100-word floor when their frames came off. Each now has one real detail.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Albion chapters | 0 | 1 | 4 | 3 |
| Hibernia chapters | 0 | 0 | 3 | 3 |
| Openings (all realms) | 0 | 1 | 1 | 3 |

## Sourcing (`myth` facts)

| # | File | Claim | Check | Result |
|---|---|---|---|---|
| S1 | `bosses/bellum` | *Annales Cambriae*: Badon (the cross three days and three nights, the Britons victors), Camlann (Arthur and Medraut fell; death in Britain and Ireland), Maelgwn's great death | Fordham's translation and the Rochester Camelot Project (Lupack): 516 Badon, 537 Camlann, 547 "the great death in which Maelgwn, king of Gwynedd died" (**verified**). Years 72, 93 and 103 of the annals' count are 516, 537 and 547. "Fell asleep" renders *pausat* (**known**). | Holds. The source now names year 103 too, with the AD years. |
| S2 | `bosses/bellum` | *Historia Brittonum* ch. 56: the twelve battles, Agned, nine hundred and sixty at Badon by Arthur's one charge | Morris's translation, ch. 56, via the Camelot Project: "The eleventh battle was on the hill called Agned", "nine hundred and sixty men fell in one day" (**verified**). Giles's translation numbers it ch. 50 and has Breguoin and 940; the chapter follows the Harleian text, as Morris does. | Holds. |
| S3 | `locations/avalon-city` | Malory, book 21: the barge, the black hoods, the vale of Avilion, the hermitage "betwixt two holts hoar", the hermit once Bishop of Canterbury whom Mordred "flemed", the hundred tapers and hundred bezants, the three queens and Nimue, "More of the death of King Arthur could I never find", *Hic jacet Arthurus, Rex quondam Rexque futurus* | Book 21, ch. 5-7 (Caxton, via the open Early British Literature edition) (**verified**). Malory also has Arthur lay his head in one queen's lap, and that queen say "Ah, dear brother, why have ye tarried so long from me?" | Holds. The chapter said "a queen laid his head in her lap"; it now follows Malory, and the "dear brother" line is in (see A4). |
| S4 | `bosses/cuuldurach-the-glimmer-king` (`original`) | The claim by entry follows *tellach* "only in outline" | Legal entry: three entries at ten-day intervals with more horses (a woman claimant with ewes) and more witnesses each time; if the occupier neither disputes it, goes to arbitration nor leaves, the land passes after the third (**verified** for the woman's form and the intervals; the man's horses **known**, from Kelly's *Guide to Early Irish Law*). The fire and the night's sleep are the chapter's own. | Fair as an `original` outline. The hidden fact now says exactly which parts are the law and which are the chapter's. |

## Albion

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| A1 | `locations/dartmoor` "The Annal on the Back of the Map" | Medium | The annal had Duke Marcus's son ride out "and says nothing... He says nothing to the grave." The Herald's own Dartmoor text, kept in the file, has him come back "saying only that Articus and everyone of his household" had died. Mythic's canon wins (decision 10). | "...rides out alone, burnt, and says only that Articus and all his house are dead. He is Mark, who will be king at Tintagel. Of the rest he says nothing to the grave." |
| A2 | `bosses/bellum` "Under the Years of the Battles" | Low | "Here, for the thirteenth time": the reader had already written "here" thirteen times (twelve battles, Badon twice), so the count did not add up. | "here, against the thirteenth battle in the book", which keeps the echo of the thirteen dead. |
| A3 | `bosses/warlord-dorinakka` "The Lay of the Headland" | Low | The fishermen's verse said "They have never dredged for it". The file's story has them dredge up horse-skulls for a hundred years and throw them back. | "They have dredged up horse-skulls enough in a hundred years and thrown every one back, but they have never dredged in that cove." |
| A4 | `locations/avalon-city` "The Hermitage Between Two Holts" | Low | The chapter repeated the file's line "a college keeps a king's word whether anyone asks it to or not" (its third use in the file) and the "No barge came. For forty years" pair from chapter 2. Malory's "dear brother" was missing, though it is what makes Mistress Angharad's question fair. | The queen calls him dear brother (Malory); the readers' note now reads "the king in the barge had said Avalon, which was all a college of healing needed to be told"; "No barge came, and the bed was made and the water drawn for forty years all the same." |
| A5 | `bosses/bane-of-hope` "The Walkers' Vow" | Low | The chapter put a new line of Dame Nest's "below the eleven names". The roll's foot already holds "called, called, called", struck through. | The line now stands between the eleven marked names and the three struck words. |
| A6 | `bosses/silencer` | Info | "The captain had lost eleven of her eighty" before the inner halls matches the order in `apocalypse` (crypts nine, gallery two, then the inner halls), and sixty-seven reach the steward's landing (`fates-chosen`). | None. |
| A7 | `bosses/duraek-the-empowered`, `locations/avalon-city` | Info | The annal's second night ("the college of healing burned to the ground, and the bed with it"), Ifor Netmender, Rhydderch and the charters, Talwen and Sir Kenrick agree with Avalon City, Crystal Cave and Dura'ek's own story. | None. |
| A8 | `bosses/apocalypse`, `locations/hall-of-the-corrupt`, `bosses/scurceol-hyrde` | Info | The lintel's words, the smooth stone, Brother Cadoc's "turnkeys"; Leofric, Vibius's saying, Dame Morwen's formula and Tobias's last name on the roll; the Gwennol in the ledger's words. All agree. | None. |

## Hibernia

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | `bosses/balor` "The Watch from the One Window" | Low | Its last line gave Daray the same habit the file already gives him (asking each new class a question about the king and the eye), as if new. | It now builds on the habit: Daray, who already asks which came first, the king or the eye, asks those who hear this telling which of the two was looking out of the window. |
| H2 | `locations/galladoria` "Triads of the Water's Mouth" | Low | Two triads broke the pattern on purpose ("three eyes" with one thing in it three times, and the root's triad with no turn). Two tricks in one set weaken both, and the eyes' triad only set up the next one. | The eyes' triad is gone, and its detail (the eyes in the stone look at one person, close, and harm no one) is folded into the "worst counsels" triad. The root's triad keeps the set's one break. |
| H3 | `locations/sheeroe-hills` (hidden fact) | Low | The source note read "the Irish forms to verify". The six forms are good Irish: *Sruthán na mBan* (the women's stream), *Móinéar Fada*, *Cnoc an Uain*, *Log Fliuch*, *Buaile Ard* (a booley), *Druim Folamh* (**known**). | The note now glosses them. |
| H4 | all eleven chapters | Info | Each agrees with the files that tell the same events: Eochaidh chosen last by the eye and Fiachna's stone on the sill (`balor`, `tur-suil`), the seven hides (`fomor`, `tur-suil`), Conaing's forty in the high fold and seven home, the single stroke of the Cullen bell, Fiadh and Basar (`cuuldurach`, `sheeroe-hills`, `connacht`), Dara's hundred and the axe (`easmarach`, `galladoria`), Elro at World's End holding and Aroon rotting (`olcasgean`), the hydrars' three-line song (`myrddraxis`), Fearghal's leaf for Coill Bheag (`norsob`). | None. |
| H5 | all eleven | Info | No Church, no saga formulas, no capped hero, no "whence the name". Muadán of Basar is the one new name; `test_bot_names.py` passes. | None. |
| H6 | `bosses/black-lady`, `bosses/norsob-the-annihilator`, `bosses/easmarach` | Info | The colloquy's one unanswered question, the contention whose third boast was a wall, and the adventure whose way back is a man standing still each turn the form into the story. | None. |

## The owner's three questions

1. **Albion's Avalon chapter (Mistress Angharad's question whether Morgana steered the barge elsewhere): keep.** Malory
   names Morgan le Fay among the three queens, and the queen who holds the king calls him "dear brother", so her question
   rests on the text. The lore already makes Morgana a pupil of the colleges and has Angharad teach her the dusk-reading.
   It stays a question, and the readers "never decided". Mythic says nothing about the barge, so the rule of cool decides.
2. **Hibernia's brehon "claim by entry": keep.** Real early Irish law (*tellach*) has the three entries, each larger than
   the last, and the entry exists to make the occupier answer. The chapter's fire, its night's sleep and the suit that
   never closes are marked as its own in the hidden fact. Mythic is silent, and it is the best law-tale in the batch.
3. **The Fomorian voyage's three invented islands: keep all three.** Each one earns its place. The island of turned
   backs gives the wardens' custom that closes the chapter. The empty tower is Balor's Tory, gone on ahead. The island that
   sinks is Hy Brasil's own legend, and it sets up "found because nobody was searching". Cutting two would lose the
   closing custom or the landing.

## Openings (#406)

| # | Files | Severity | Finding | Change |
|---|---|---|---|---|
| O1 | `bosses/arnulv`, `dalicren`, `magister-kurgol`, `trude` | Medium | Removing the "From the ... of X:" frames left four Midgard major-boss book pages at 97-99 words, below the 100-word floor. At `a88622ae` all were at or above it; on HEAD `depth_report.py` printed "4 of 245 below" (it still exits 0). | One real detail each (decision 10): Arnulv's two lines "Neither line has ended yet"; the rope left lying; the guide wipes her hand on her coat; Solvi sets his lines further off. All four are at 100 or more. |
| O2 | 18 pages | Low | Garbled or clumsy rewordings: an attribution wedged into the clause ("Down the slope to the Master's door, in The Slope, ... the demons stand"; "First came the noise, in The Parade of the Roll"; "as black as the bottom of a well, in The Marshal's Table"), a lost subject ("Derbáil of Ardee's list keeps her word for it, and flew them over the court wood"), a garden path ("Near the hall where the old morvalt teaches the Valkyn guide would not go"), a dangling "and that" (`tur-suil` page 2), and inverted openers that read badly ("A staff with two ends was the crier's", "A chapel of his own was the magistrate's portion, ... and they gave it him", "Two silver, paid out by Mordwywr ... to old Cadan", "Received, in the daybook ... from the far south-west"). | Reworded with the same words: `apologet-nesnuir`, `bjornvarg`, `captain-heathyr`, `chthonic-knights`, `cirfans`, `director-kobil`, `duke-alloc`, `eralyne`, `field-marshal-nebir`, `kleng`, `lord-sanguis`, `lurfos-the-herald`, `marquis-valupa`, `mikka`, `morgus-urgalorg`, `valmak`, `wyvernic-wraith`, `tur-suil`. |
| O3 | sample of 40+ across the realms | Info | Read old against new. Most read better, starting in the scene or the document ("The Cullen road after dusk is on Derbáil of Ardee's list"; "Be patient, stone; you were patient when the world was water. So runs a Sylvan stone-song"; "To Tobias, clerk, greeting."). Every one keeps its teller and every event. | None. |
| O4 | all 403 changed strings | Info | **Nothing old was cut.** A word-multiset comparison of every changed visible string against `a88622ae` finds only the frames gone ("from" 257, "of" 116, "the" 101, "by", "say", "kept", "writes"). The only other words gone are the section headings of Albion location pages ("Of the orders."), and each subject is still named in the page's first sentence. The three apparent large removals (`balor`, `olcasgean`, `galladoria` chapter 6) are index shifts: the new chapter was inserted before the last, and the old chapters are intact at the next index. | None. |
| O5 | `tools/lore/repetition_caps.json` | Info | Against `a88622ae`: 76 values lowered, none raised, none removed. Hibernia's covered share is 3.43 (was 3.63). Four opening caps were added where the rewording made a new opening common: Midgard "I was" 4, "I went" 5, "My grandmother" 4; Neutral "The imp" 4. Without a cap these would fail the free limit, so the additions hold them where they are. Midgard still has the most "From the" openings (43 of 655; 90 before). | None. A later pass can lower Midgard further. |

## The five best passages

1. **`bosses/balor`, the watchman on the height:** "Not hard to know him," said the king at last. "That is no one. That is a
   bard of Howth named Fiachna, with a stone he will never throw. But I know whose stone it is. I knew it on another
   field, before I had a tower. Shut the window." / "The window has no shutter," said the watchman. "You had it built
   so." / "Then look away," said the king.
2. **`bosses/bellum`, the annal:** Against Camlann the reader wrote nothing. The space is ruled and empty, as if he meant
   to come back to it.
3. **`bosses/silencer`, the romance:** And the company's priest wrote that he heard, after, the first sound in the inner
   halls since the rising, and it was the ten breathing.
4. **`bosses/cuuldurach-the-glimmer-king`, the judgment:** The brehons of Connacht keep an empty peg on the wall of their
   hall at Basar where the fleece is to hang, and the shepherds say Muadán got the best of the bargain, as brehons do: he
   is owed the one fleece that would mean the case was won.
5. **`locations/tur-suil`, the place-poem's last quatrain, left at two lines:** Carraig na Faire was the rock of the
   watch. / The watch is kept now from the other side.

Close behind: the hermit's "That, the hermit said, betokens you" (`apocalypse`), and "this one is the only one they know
where the way back was a man standing still" (`easmarach`).

## Checks (tests only, on `wt11/voice-all` after the fixes)

All pass: `depth_report.py` (0 below in every group), `schema_mirror.py` (0 problems), `lint_lore.py` (0 problems, 0
game-term warnings), `name_counts.py --check` (0 over cap), `continuity.py --check` (47 facts, 0 contradictions),
`repetition.py --check` (0 problems), `reachability.py --check`, `mechanic_trigger_coverage.py --check` (15 of 15 fire),
`build_corpus.py --check` and `--verify`, `debates_doc.py --check`, `check_runtime_names.py`,
`epic_encounter_catalog.py --check`, `build_encounter_adds.py --check` (`data/encounter_adds.json`), `tools/lore/tests`
(65 passed), `tools/bot-voices/tests` (44 passed), `source/tools/tests` (294 passed, 1 skipped). `REPETITION.md` and
`CONTINUITY.md` are regenerated. `build_corpus --write-vocabulary` was not run.
