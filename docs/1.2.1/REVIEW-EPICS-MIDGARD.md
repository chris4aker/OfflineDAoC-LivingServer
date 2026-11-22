# Review: the epic floors, Midgard and Neutral (#402, #405, #406)

The reviewer is independent and did not write the commits reviewed on `wt6/rev-epic-mid`: the Midgard and Neutral book
pages of `wt6/epic-all` (`0c24de92`, `1255248b`, `cd093032`, `e15e10a9`, `297e7aaf`, `bad54106`, `872dd797`, `a68d64bf`,
`e5264e89`, `3f1eac59`, `f8003ab1`, `18ac076b`, `891504ce`). They cover 45 Midgard and 15 Neutral epic bosses: 98 new
Midgard pages (a `bookPages` array after the untouched `bookPage`) and 30 new Neutral pages (appended to the `bookPage`
list), with one `original` fact per file. No location or history file changed, so the location check was a read of the
places that retell the same events (`darkness-falls`, `malmohus`, `dodens-gruva` through the boss chapters,
`history/neutral`).

The fixes are in `7a468b11` and the commit that adds this file. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the boss's script facts and its legend), **data** (the boss's own chapters, story and
facts, or another file), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a contradiction a player would notice, or a page that states a mechanic wrongly.
- **Low:** prose, repetition, a page that retells its chapter, the generic fact.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The writing is good.** The Midgard pages read as saga documents (slates, beams, rune-cut inscriptions, skalds'
  verses, letters home, steading reckonings), and the Neutral pages pair one court document (a Roll, a gloss, a ruling,
  a testimony, a cord, a doorway law) with one realm's telling, in that realm's own voice. Every page is now 100 to 160
  words.
- **No owner rule was broken.** No hero stands in a present-day scene, no companion name appears (`Hire165.cs` checked
  for every new name), the Norse gods are alive (Odin as "the Hanged One", Hel as Loki's daughter), nothing is from after
  1.65, no game terms (the lint is strict and passes).
- **Five contradictions and one cross-file slip** are fixed by rewording new text (M1-M5, L1), plus the writer's
  Dyranapur flag, which was between two old chapters and is settled by adding words, none cut.
- **Four pages only retold their chapters** (L2) and are rewritten as new documents; the repeated forms are varied (L3).
- **Nothing was cut:** a word-subsequence check of every old visible string in all 145 changed files against the new one
  finds 0 missing words.
- **Every check passes** (see Tools). All of this is tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Contradictions and the code | 0 | 5 | 1 | 4 |
| Repetition and retelling | 0 | 0 | 4 | 2 |
| Writer flags | 0 | 1 | 2 | 2 |
| Old pages, facts, cuts | 0 | 0 | 2 | 1 |

## Contradictions and the code

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `bosses/torst` page 2 of 3, `bosses/hurika` page 2 of 3 | Medium | Torst's list of Asmund's seven attempts gave every loss to the eddies and none to the hen, while his own chapters and Hurika's (and Hurika's new roll) say she **threw him four times**; the list also repeated Hurika's roll in form. Code: the eddies are Torst's adds, the throw is Hurika's gust (**code**). | Recast as the prentices' finger-count. Throws on the first, third, fifth and sixth tries; the eddies take his three griffons on the first, fifth and sixth (chapter: "lost three griffons to them") and Hrafn's on the seventh (old page); the cock's pin and the switch to Ketil on the fourth (**code**: root, then a random target). Hurika's roll: Hrafn is now "in the same age, a younger rider", since he flew Asmund's seventh. |
| M2 | `bosses/council-hord` pages 2 and 3 | Medium | The bent-fingered hand Hrimgrim carved in the fortieth winter (chapter "Hrimgrim's Arm") was called "the first gift anyone remembers", though the gifts began in the citadel's first years (chapter 1). | "Low in the heap, worn smooth under forty winters of later gifts"; Hrimgrim's page says "low in the heap". |
| M3 | `bosses/council-otrygg` page 3 | Medium | The rooks' memory said Hjalmar "came up after dawn"; the chapter says the rooks **carried him out**. | "and we carried him up after dawn without the torch". |
| M4 | `bosses/summoner-lossren` page 3 | Medium | Asgaut "went down the deep stair ... before Aros bound the rest of the miners", but Aros's old page names the whole last shift (Hamar, Bjorn, Kol, Ulf Half-hand, the pup) and Asgaut is not on it; his father Ulf also read as Ulf Half-hand ("the tale of the sons of Ulf"). | Asgaut, son of Thorbrand, a hauler not of the last shift, hears the calling at the broken stair on the night the shift is lost and goes down before Aros comes after the jarl. The fact now says "Grimkel Thorbrandsson's tale". |
| M5 | `bosses/council-vagn` page 3 | Medium | "The outer seats have tried for a century to finish it": Hjalmar struck him in the century's sixtieth winter (chapter 2). | "every winter since". |
| L1 | `bosses/aros-the-spiritmaster` page 4 | Low | "on the morning of the year the seam opened" does not parse as the anniversary it means. | "each year on the morning of the day the seam opened". |
| I1 | 60 files | Info | Mechanics stated in new pages agree with the code facts: Hjalmar's Morkimma heal only themselves (the scribe's margin); Mahattava strikes only a target already infected, so a quick cure keeps it "choosing and never striking"; Yar heals whole after a minute out of the fight and his five kin come once at three-tenths; Kula drags raiders to the frozen circle and fights at full strength when Tuscar dies first (Bergljot's verse); Legion cannot be hurt from beyond the ring (Ealadhach's spear's cast); Oro's copies cut hard and burn easily, the real one the reverse; Govannon grows stronger, then a demon steps out whole; Baln's first strike from the door wakes every demon in the hall; Ba'alorien eats the nearest pet and heals (Fionnuala's wolf); Hakr throws no one after the last weaver dies; Kvasir's cold-heal history (off, then on again); Steinvor's naming and pillar; Vifil counting his row. | None. |
| I2 | `bosses/prince-asmoien` page 3, `prince-abdin`, `princess-nahemah`, `high-lord-oro`, `high-lord-baelerdoth`, `behemoth` | Info | The Rolls are dated against `history/neutral`: the feud at the Gate in the Fourth Roll, Oro three in the Third, Lilith away in the Seventh, the doors opened in the Tenth. All agree. | None. |
| I3 | `bosses/grand-summoner-govannon`, `summoner-lossren`, `aros-the-spiritmaster` | Info | Midgard reaches Summoner's Hall down the broken stair of Dodens Gruva, "past the jarl's hound": this agrees with Aros's chapter (the lights and robed figures in the hall below). Albion comes up from the Hall of the Corrupt, Hibernia down the stair of Marfach. | None. |
| I4 | `bosses/agmundr` (new page 2), `council-ozur` page 2 | Info | The spy of the sixtieth winter is now one man in both files: Leif Asgautsson, handed by Ozur's one jailer to "an icelord of the old houses" (Agmundr, of the young icelords of the old houses in Nokkvi's chapter), and sent home with his beard frozen and a message (Agmundr's chapter). | Written so (see L2). |

## Repetition and retelling

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| L2 | `bosses/icelord-agmundr` pages 2 and 3, `bosses/stanga` page 2, `bosses/iarnvidiur` page 4 | Low | **Pages that retold a chapter.** Agmundr's song of Bergljot's hundred was chapter 3 in verse-free summary, and the scout's drakulv note was chapter 2; Hrefna's slates restated Stanga's chapter 2 line for line; Vemund's letter retold the warband chapter. | Agmundr: the message he sent home with Leif Asgautsson (I4), and the varulv blood-price on his depredator ("for Grar, for his sister, and for the young one who had no name yet"), which shows the household running to the roar without retelling the drakulv. Stanga: Hrefna's own words of the day she counted, as her grandson Ottar heard them (the wolf waiting at the far end, the grey hairs in the old marks and none in the new). Vemund: his own spear in her side, the wound shut before the point was clear. |
| L3 | Midgard pages | Low | **Repeated forms.** The slate-house at Faraheim had spread to the Tuscaran files (Suttung, Ozur, Kula); "From the notes of the Valkyn scout who lay in a crack in Delling's rim" opened three pages; Ashild of Glenlock's "tally" opened two; three pages ended on room left for the next line (the syver spears, Hanar's slate, Guthlac's bone). | Kula's count of the ice is now Dellingstad's stones, told by the people of the outermost house; Ozur's page is Leif's telling at his hearth; Suttung's, the slate-keepers' words over the shard; Skoll's listeners are said aloud in the guides' hall; Lydsyg's lamp is the custom of the singer's night; the wise-women's receipt is said over the jar; Issorden's scout note and Ormarr's Ashild note have new openings; Guthlac's bone ends with the splitters still going to it. The slate-house stays where the old chapters put things in it (Hanar's axe, Nyr's shard, the lullaby-stone, Bodvar's slates). |
| L4 | Neutral pages | Low | **"Here ends the tale of ..."** closed four Midgard tellings in Neutral files (Bardi, Ottar, Thorkel, the sons of Ulf), beside the Mistress's "Here ends the copy" and Ulla's old chapter. | Thorkel's now ends on the count he told only to the widows; the sons of Ulf's on the verse Audliten still sings. Bardi's and Ottar's keep the saga close. |
| L5 | `bosses/hurika` page 2 and `torst` page 2 | Low | Two attempt-lists for the same riders in paired bosses, both cut on the cliff-top post. | Torst's is now spoken (M1). |
| I5 | all | Info | **Inscriptions** ("Cut on ...") open 10 of the 98 Midgard pages: the realm's rune-stones and slates, which the voice brief asks for. Each is a distinct object (a gatepost, a lintel, a grave, a beam, a wall, an axe-rack). | None. |
| I6 | Neutral pages | Info | The Hibernian forms in Neutral files ("whence is it named? Not hard" for the Choir and the Room of High Seats, a triad of three sleeps, a brehons' judgment, a satire) appear once each, in pages told by Connacht's poets and brehons, beside the court's own Rolls, glosses and rulings. No teller-formula openings; endings close on events, not sayings. | None. |

## Writer flags

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| M6 | `bosses/dyranapur` chapters "The Dark-Moon Cup" and "What Is Told of Ulla" | Medium | Both **old** chapters: Ulla carried the cup "forty winters" and "for thirty-one dark moons". | Words added, none cut: the Remnants' oldest slate gave forty winters, but it counted the years she lived among her people after she gave the cup up; the svartalfar count thirty-one dark moons of carrying, longer than any bearer before or since (Kolr "lived to be old, which among those who carry the cup is not common"). The law of the cup page agrees: Ulla broke the fifth line, looking back. |
| L6 | `bosses/gjalpinulva` page 2 | Low | **Hallbjorn** at 12 in visible text, 13 with the chapter title "Hallbjorn's Warband". | The beam's line is "The thane of Fort Atla, the son of Ulf, with eighty": 11 mentions, 12 with the title. |
| L7 | `bosses/behemoth` chapters | Low | 1,313 words against the 1,800 aim for raid finals. | New chapter in the court's voice, **"Under the Heading of Weather"** (520 words, now 1,833): the Archivist measures the beast's breathing as a tide because the Chancellor ruled it cannot be listed (its page "Not entered" files the answer under weather); the one night the column is empty (the Master called it down, chapter 3); the Inquisitors' examiner of the Second Roll who stopped a long stride off, "close enough to its nose" (**code**: it ends what it swings at, aggro range 500), referred as irregular to Ba'alorien under his own addition to the law of portions; the breaths shorter since the doors opened in the Tenth Roll. Placed before "Those Who Went Up", so the Door-Book's last line still ends the file. Its fact names the chapter. |
| I7 | `bosses/prince-asmoien` page 3 | Info | **Skuli's hound** reads as intended: Snjall went down "in a summer when the door stood open" (the doors opened in the Tenth Roll), and the page says Gress has the Blade and the Hook fighting over her soul "in the Fourth Roll, long before any door opened", then "Gress tells most things otherwise". The chapter and story keep Gress's version as hers. | None. |
| I8 | memory facts flagged by writers | Info | Plausible for realm and era: the satire that raises no blister because demons do not blister (the Irish satire that raises blotches, `high-lord-baelerdoth`); Michaelmas burials in a Pennine parish (`summoner-lossren`); Dyggvi the Yngling taken by Hel in his bed (*Ynglingatal*, `thane-dyggve`); brehon sureties (`summoner-roesia`, now "the brehons' fee" rather than "the poet's fee", since brehons, not poets, give judgments). The Connacht druid Fionnuala shares a name with Lir's daughter; she is not a hero, a companion or on the roster. | Roesia's fee reworded. |

## Old pages, facts, cuts

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| L8 | `bosses/delegalt` (97), `icelord-steinvor` (95), `jailer-vifil` (97), `mistress-of-runes` (99), `torst` (99) | Low | Old book pages under 100 words. | Every old word kept, each raised in its own voice: Delegalt talks of "the others"; the scout says the two giants' talk was the longest he ever heard and he understood every word; the kobold was glad the jailer counted him; the runemaster told Jordheim to go and read it themselves; Hrafn would not leave off looking for his griffon. Now 109, 121, 132, 121 and 110. |
| L9 | 45 Midgard files | Low | **The generic fact.** Two shared sentences ("The two pages in bookPages, and the people ..." and "The book pages after the first (letters, slates, verses and inscriptions ...)") stood in every Midgard file. | Each fact now names its own pages and new people, e.g. Ancient Syver: "the spear-slate of the Remnants with Hogni the Old, Thrand, Ketil the Lame and the sisters Asa and Groa, and the wise-women's receipt against the rot". The 15 Neutral facts already named their pages. |
| I9 | all lore | Info | **Nothing cut.** A word-subsequence check of every old visible string (fix/1.2.1) against the new file, 145 files, finds 0 strings with a word missing. | None. |

## Tools

Run on this branch after the fixes (tests only; nothing seen in game):
- `python3 tools/lore/depth_report.py --kind epic-boss`: 0 below on every floor (157 epic bosses).
- `python3 tools/lore/schema_mirror.py`: 0 problems.
- `python3 tools/lore/lint_lore.py`: 0 problems, 0 game-term warnings.
- `python3 tools/lore/name_counts.py --check`: 0 over cap.
- `python3 -m unittest discover -s tools/lore/tests`: 38 OK.
- `tools/lore/mechanic_trigger_coverage.py --check`, `tools/bot-voices/build_corpus.py --check` and `--verify`,
  `tools/bot-voices/debates_doc.py --check`, `source/tools/epic_encounter_catalog.py --check`,
  `tools/lore/build_encounter_adds.py --check`, `tools/bot-voices/check_runtime_names.py`: all pass, nothing stale.
- `python3 -m unittest discover -s tools/bot-voices/tests` (with `test_no_story_retells_a_paragraph`): OK.
- `python3 -m unittest discover -s source/tools/tests`: OK (1 skipped).
- `UT_LoreBible` was not run (no dotnet); the schema mirror stands in for it.

## For the owner

- **Dyranapur:** Ulla's carrying is thirty-one dark moons, and "forty winters" is kept as the Remnants' slate counting
  her years after. The other way round (forty winters of carrying) would mean rewording an old chapter and its fact.
- **The Behemoth's new chapter** is court-voice, 520 words; it is the only new chapter in this pass and the only text
  that adds to a raid final's chapters.
- **Hallbjorn** sits at 12 counting his chapter title. If titles should not count, the beam could have his name back.
