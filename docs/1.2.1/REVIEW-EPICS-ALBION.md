# Review: the Albion epic book pages (#402, #405, #406)

The reviewer is independent and did not write the eight Albion commits reviewed on `wt6/epic-all` (`4372e64a`,
`75a88086`, `e885bfff`, `9f9e874f`, `92feed08`, `72f9967e`, `c2f95081`, `3599b58a`): 77 new book pages (a `bookPages`
array after the untouched `bookPage`) and one `original` fact each in the 49 Albion epic boss files of the Hall of the
Corrupt, Caer Sidi, Krondon, Avalon City, the Crystal Cave and Dartmoor. Albion location files were not changed by the
writers; they were read where they retell the same events (`hall-of-the-corrupt`, `caer-sidi`, `krondon`,
`crystal-cave`, `dartmoor`). Paths are relative to `source/server/GameServer/bots/autonomous/data/lore/`.

The fixes are in `fff0e8c7` and the commit that adds this file.

Labels: **code** (checked against the boss's script facts), **data** (the boss's own chapters, story, legend and
facts, and the location files), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a contradiction a player would notice, or a page wrong for the code.
- **Low:** prose, voice, repetition, length.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The writing is good and the voice is Albion's.** Letters, rolls, inventories, inscriptions, rubrics, a sermon's
  heads, a pay roll, a verderer's count, a Roman daybook: chronicle and Church throughout, no Midgard slates or
  Hibernian triads. No old visible string was changed (every field but `bookPages` and the appended fact is identical
  to `fix/1.2.1`; old facts unchanged).
- **No owner rule was broken.** Arthur is only told about (the king's seven, a grandfather at Camlann); no hero stands
  in a present-day scene; no companion is mentioned; nothing after 1.65; no game terms (lint 0).
- **Fourteen contradictions with the bosses' own texts or the location files** (M1-M14), all fixed by rewording the new
  page. The four writer flags are settled (M1, M2, M9, I1).
- **Every page is now 100-160 words** (two were over: Orylle 163, and three grew past 160 during fixes and were
  trimmed).
- All of this is tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Consistency with the boss and the place | 0 | 14 | 0 | 3 |
| Canon and era | 0 | 0 | 0 | 2 |
| Voice, repetition, length | 0 | 0 | 5 | 1 |
| Facts, cuts, names | 0 | 0 | 1 | 2 |

## Consistency with the boss and the place

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `lady-darra` page 2 | Medium | **Writer flag.** The chapter says the dalesmen learned the remedy "from Hob's wife, who ... read the letter before Hob carried it", yet the prioress's answer "came north in the spring, with Sir Hamon Brede's riders" and was never opened at the Pennines; `lieutenant-elva` says Hob's wife had read Darra's last letter (**data**). Old text cannot be cut. | The page now says the answer lay a month at Long Dale waiting for the road, and Hob's wife, "who read every letter before Hob carried it", opened it at her hearth and sealed it again; then the Constable's riders took it north and it came back sealed. All three files then agree (she read both letters). Fact updated. |
| M2 | `scurceol-hyrde` page | Medium | **Writer flag.** The page had the Gwennol right herself and lose "two men and nine sacks"; the chapter has her put on her side 300 yards off the Caldey landing "so quickly that the boy at the tiller was found still holding it", "the winter after the Gwennol went over", and `krondon` calls her "a grain-boat the squall took" (**data**). | She does not right; the boats find her at dusk keel up, the boy still holding the tiller, and the sea keeps two men and every sack. The 300 yards and the boy come from the chapter. |
| M3 | `sergeant-reede` page | Medium | The Constable's answer promised "the cooper after"; `hall-of-the-corrupt` says the cooper "never came; Hob carried the request south three times, and it was never answered" (**data**). | "Of a cooper this office finds no request in its books." |
| M4 | `captain-heathyr` page 2 | Medium | Headed as a report of "riders sent north in the spring with the Constable's order to seal the road"; in `hall-of-the-corrupt` Hamon's riders went to learn why the watch was silent, and the seal order came after, when Hamon asked to be relieved (**data**). | The leaf is now the riders' captain's report on the captains' door, kept beside the later seal order: "my orders were to learn why the watch was silent, not to break it ... If my lord means to seal the road behind that door, I ask him to send the order to someone else." Heathyr's story ("that is why the order to seal the road was never carried out") still holds. |
| M5 | `captain-atwell` page 1 | Medium | The letter said "I had its haft cut a hand longer, as I always meant to"; the story and chapter have him ask for it at fifteen, and the bill stay on its pegs that whole winter (**data**). | "The bill is on its pegs, with the haft Humberton cut a hand longer for me, and I have not taken it down this winter." |
| M6 | `lich-lord-ilron` page 2 | Medium | The priest reads the sill "on the feast of the dead"; `caer-sidi` has him read it "on the night of the autumn's first frost". The sill also listed the third company's dead as nine and two, leaving out the Silencer's two (**data**). | First frost; the sill now lists "the nine ... in the crypts, the two the hush took in the inner halls, and the two who fell in the same gallery as Tegid". |
| M7 | `apocalypse` page 1, `fates-chosen` page 2 | Medium | Apocalypse put the Silencer's two "on the stair"; the Silencer's chapter has them die in the inner halls, and after it the company went on to "the stair to the galleries", so the Silencer comes before the steward's landing. The oath at the landing counted only the crypts and the gallery (**data**). | "Two fell to the Silencer in the inner halls"; the oath now reads "nine in the crypts, two to the hush in the inner halls and two in the gallery". The owner's 80 / 67 / 9 + 2 + 2 is kept. |
| M8 | `host` page 2 | Medium | The tally strikes down two bodies for nothing, then the real one, "and the other seven fell"; only five were left (**data**, **code**: killing the real one kills the rest). | "the other five". |
| M9 | `veraerius-the-brave` page | Medium | Maelon's sword was "notched in eleven places", the same as the Caerwent knight's sword in `bellum` page 2, and both hang in the same chapel; Vera'erius's chapter gives it one notch "the width of a thumb that his smith could not grind out" (**data**). | "His last stroke left a notch the width of a thumb in my sword that the smith cannot grind out." |
| M10 | `duraek-the-empowered` page 2 | Medium | Kenrick's order had every Academy wizard leave his fire books on the wall; the legend says "Leave the fire and ice wizards on his avowries" (**code**: fire and frost heal only Dura'ek). | The fire and frost wizards spend themselves on his six sworn guards and nothing else; the rest go in with steel and the crushing words. Kenrick has each wizard of fire and frost say the order back to him. |
| M11 | `wenoiak-the-enlightened` page | Medium | **Writer flag** (Hedydd's half-learned working). The page had the college teach it "in two halves", the second a gentle working, and said "the second he never learned"; the chapter says the burst *is* "the second working", that the books were "meant for three masters reading together", and that he "is learning" (**data**). | The book teaches it to three masters: two gather and send the light to one point, the third gives it back gently. He has the first part; the third "he has not learned yet", and when pressed "out comes what the readers call his second working": the gathered light every way at once. Fact updated. |
| M12 | `lord-sanguis` page | Medium | The page had Titus Senecio carry the tally to the living magistrate; the chapter says that in Senecio's year the basilica "stood empty", and that he had been told why "and did not believe it" (**data**). | The entry is now his grandfather Gaius Senecio's, from the old wax, with Titus's margin note in the year the legions sailed: "the basilica is empty now; I have been told why, and I do not believe it." Fact updated. |
| M13 | `lieutenant-elva` page | Medium | The inventory listed Gildas's sword and then "a blade by the door, half dressed" as a separate item; the loremaster says "the half-dressed blade by her door was meant for Lord Gildas" (**data**). | "Item, his blade, laid by the door, half dressed." |
| M14 | `krevo-ricik` page 1 | Medium | Wenna wrote "Someone should go who can read", but she was the hall's best reader, and on her second descent she "read the titles over every door on the way out, aloud" (`krondon` book) (**data**). | "Someone should go who can stand in that passage long enough to read its doors." |
| I1 | `golestandt` page 1 | Info | **Writer flag.** The Fabricius tablet breaks off mid-sentence ("orders a third"). The heading says the clerk "notes that the last lines are broken", and the story finishes the thought (the legate marched a third time and was not heard of again). Right by design. | None. |
| I2 | every page | Info | Checked against the chapters and places: Caer Sidi's 80 / 67 and the chapel customs (door open, candle at the door, lamp in the window, names on the sill); Dame Nest's eleven; Iolo and the eight; Corwen's five on the sphere (**code**: the sphere drags the edge inward); Orylle's 44 of 50 and Magus Tegau; Deben's three lost after the horn; Bardalph's twenty-two marks; Saxe's four generations; Xanxicar's Iorwerth, forty years and the youngest master burned; the spring riders, Hob, Annis's grandchildren. All right. | None. |
| I3 | old text, not changed | Info | Two old contradictions the pages do not touch: `crystal-cave` says Iorwerth "never went down to the cellars at all" while `xanxicar` has him sing the Quieting at the Heart in the deepest cellar; Weno'iak's story has the books on his observatory shelves while his legend says raiders found "only ash and a few torn pages". | Left for the owner (see below). |

## Canon and era

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| I4 | all 77 pages | Info | Arthur only as the dead king (the seven, Camlann); no named hero in a present-day scene; no companion name; no ToA or Catacombs fact. The 12-mention cap holds: a fix that named "Sir Hamon Brede" pushed him to 13, so the new text says "the riders' captain" and "the Constable's riders" (`name_counts.py --check` 0). | As described. |
| I5 | `silencer`, `apocalypse`, `lady-darra`, `lieutenant-meade`, `lord-gildas` | Info | Church and calendar from memory: the "still small voice" after wind, earthquake and fire (1 Kings 19); All Souls; Lady Day letters; Lammas levies; the Ides in a Roman daybook; the abbey that keeps Old Sarum's fair roll (the story already has its abbot). All plausible for Albion and the era. | None. |

## Voice, repetition, length

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| L1 | 22 of 77 pages | Low | **Teller-formula openings:** "From the account of ...", "From the inventory of ...", "From the ledger ..." opened 22 pages, twice "From the account of Elowen" in Avalon. | Six pages now open inside the document and name it at the end ("So Elowen of Caer Gothwaite, of the ledger ...", "The third company's priest wrote this on the leaf ..."): `crypt-lord` 2, `jehrog-se-bysen` 2, `duraek-the-empowered` 1, `syssro-the-ruthless`, `bellum` 2, `sergeant-eddison`. 16 left. |
| L2 | `jehrog-se-bysen` page 2 | Low | **Restated a chapter** and ended on the chapter's own aphorism (Wenna's "a guard is a kind of grief" margin line). | New close: Collen says the office for the dead over both brothers, "because nobody else in that room knew the words and the young ones in the doorway were listening"; Wenna's line is only pointed to. |
| L3 | `krevo-ricik`, `crypt-lord` 2, `beran-the-supply-master`, `deben-se-gecynde` 1, `golestandt` 1, `skeletal-sacristan` | Low | These pages lean hard on their chapter or story (the same events, in a document's voice, with one or two new details each: the titles of the doors, the counting under his breath, the rings on the plan, Eormen's dismissal, the bellows, the key in the list). | Left: each adds something, and rewriting them whole would be a new writing pass. |
| L4 | the realm as a whole | Low | **Caer Gothwaite** carries most pages (its chapel, readers, garrison, Wenna, Elowen); **Wenna** is in 12 Krondon pages and "as the ghosts read it for Wenna" recurs. Each page is a different document, so it reads as an archive, not a formula. | Left; noted for a later pass. |
| L5 | `orylle` 1, and three pages lengthened by fixes | Low | 163 words (cap 160). | Trimmed to 159; `wenoiak` 160, `veraerius` 160, `duraek` 158, `heathyr` 160. |
| L6 | `captain-bardalph` page 1 | Low | The castellan's "bishop who complained to me" sat beside the chapter's Pennine bishop who complained of her; readers would take them for one man. | "an abbot". |
| I6 | endings | Info | No "Here ends"; aphorism endings are few and in-world (a cut line over a door, an Academy maxim, a letter's warning). | None. |

## Facts, cuts, names

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| L7 | 25 files (`9f9e874f`) | Low | The `original` facts share one sentence ("... drawn from events the chapters already tell"), but each already names its own documents in brackets, so they satisfy the request; the other 24 use "The book pages after the first (...)". | Kept; the four facts whose pages changed in substance (Darra, Heathyr, Weno'iak, Lord Sanguis) now name the new detail. |
| I7 | all 49 files | Info | **Nothing cut:** every non-`bookPages` field equals `fix/1.2.1`, old facts unchanged, only one fact appended. No old book page under 100 words in Albion. | None. |
| I8 | schema | Info | Schema mirror 0 problems. | None. |

## Tools

These ran on `fff0e8c7`, all exit 0:
- `tools/lore/depth_report.py --kind epic-boss`: 0 of 157 below on every line.
- `tools/lore/schema_mirror.py`: 108 locations, 578 bosses, 4 history files, 0 problems.
- `tools/lore/lint_lore.py`: 0 problems, 0 game-term warnings, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK; corpus.json matches its sources.
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check`, `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK (`test_no_story_retells_a_paragraph` included).
- `python3 -m unittest discover -s source/tools/tests`: 295 OK (1 skipped).

Nothing needed regenerating. `UT_LoreBible` (dotnet) could not run here; it must run in CI or on the owner's PC.
All of this is tests only; nothing has been seen in game.

## For the owner

1. **Old contradictions outside the new pages** (I3): Iorwerth "never went down to the cellars" (`crystal-cave`) against
   his forty Quietings at the Heart (`xanxicar`); Weno'iak's books on his shelves (story) against "only ash" (legend).
   Recommendation: let a later pass reword the `crystal-cave` line ("never went down past the Heart") and tell the
   legend's ash as the raiders' report of the shelves he lets them see.
2. **Present-day "Marshal Gareth"** cuts the nine on the Dragon's Walk (`golestandt` story, old text). Gareth is one of
   Albion's legend companions in HEROES.md. Recommendation: rename him in a later pass, or rule that a present-day
   marshal may share the name.
3. **Caer Gothwaite and Wenna carry most of Albion's archive** (L4). Recommendation: when the next pages are written,
   give some to Prydwen Keep, Castle Sauvage, the Isle of Glass and Camelot's own archive.
