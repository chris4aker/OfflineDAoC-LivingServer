# Teller openings, Midgard and the shared places (#406)

Branch `wt11/v-mid`. Only lore files whose realm is Midgard or Neutral were touched. Nothing old was cut: every
event and every teller's attribution is still in its passage. Only the frame words moved.

## The voice in brief

Midgard is a realm of witnesses, and the shared places speak in the records of the court below and of the realms that
come down. The old habit opened nearly every witness's page with the frame ("From what Ivrik the kobold lamp-carrier
of Bjarken told the skalds:", "From the readings of Brisa the beam-cutter of Svarhamr:", "From the Door-Book of the
castle ruins, the account of..."), so a player read the frame twice before the scene. Now:

- **Start in the scene, with the teller inside the sentence.** The witness's first act or image comes first, and the
  name sits inside the first sentence like a speech tag ("We went through the hall of weapons with our hands under our
  arms, Ivrik the kobold lamp-carrier of Bjarken told the skalds."). This is what VOICE-MIDGARD.md asks: "Ivrik's
  name once per page, inside the sentence".
- **Start in the document.** A tally, a logbook or a price-board opens on its own first line, and the document is
  named after it ("Seventh attempt on the glacier roost, in the logbook of Asmund...").
- **Start with the image**, then the claim ("A warmth in the floor: you can feel Kurgol's forge from the hall of
  counsel...").
- **Saga inversion, used once in a while** ("Back for Brokk I went", "In a shield-wall I have stood").
- **The writer as the subject** where the frame is the point: the scribe, the novice or the runner ("A novice copied
  the sermon...", "Sister Elaydith's runner, Siencyn of Camelot, went down the Albion stair...").
- In chapters, a paragraph that opened "The skalds of Jordheim say" / "The sculptors say" / "The narfi say" / "The
  Galplen tellers say" / "The druids say" now opens on the claim, and the teller follows it ("It is a piece of the
  sun, the skalds of Jordheim say.").

Left as they were: the three saga chapters (Lydsyg, Dyranapur, Hurjavelen), the Gudlaugr slates and the Fort Atla verse,
whose document openings are the saga form; the VOICE-MIDGARD model passage (`grul-mgnuk`, Orri on the cliff); the
Darkness Falls flagship pages; and a few frames in each series, so that each teller still sounds like a series.
No epic encounter was deepened: the brief allowed one or two only where it clearly lifts them, and the opening work came
first.

## Three samples for the owner (before / after)

1. `bosses/grindvaktare`, book page
   > **Before:** From what Ivrik the kobold lamp-carrier of Bjarken told the skalds: the first thing my lamp showed me
   > in Trollheim was the pieces of the gates, stone as thick as a house wall, lying where they fell.
   >
   > **After:** Stone as thick as a house wall, lying where it fell: the pieces of the gates were the first thing my
   > lamp showed me in Trollheim, Ivrik the kobold lamp-carrier of Bjarken told the skalds.
2. `bosses/hurika`, book page
   > **Before:** From the price-board of a fletcher's house in Jordheim, which hangs inside the door where the buyers
   > can read it. Goose, grey, by the hundred. Goose, white, by the hundred, dearer.
   >
   > **After:** Goose, grey, by the hundred: so begins the price-board of a fletcher's house in Jordheim, which hangs
   > inside the door where the buyers can read it. Goose, white, by the hundred, dearer.
3. `bosses/behemoth`, book page (shared epic)
   > **Before:** From the Door-Book of the castle ruins, under the heading of things in the hollow that the companies
   > should not count. The beast on the ledge. Sir Cadfan Hir took eighty on to it...
   >
   > **After:** The beast on the ledge: the Door-Book of the castle ruins enters it under the heading of things in the
   > hollow that the companies should not count. Sir Cadfan Hir took eighty on to it...

## Opening counts (first two words of every book page and chapter, `tools/lore/repetition.py`)

| Realm | pages | starting "From" | top openings before | top openings after |
|---|---|---|---|---|
| Midgard | 655 | 162 → 78 | "From the" 90, "In the" 19, "From what" 14, "When the" 13, "From Thorgrim" 10, "At the" 10 | "From the" 43, "In the" 19, "When the" 13, "At the" 10, "Cut on" 8, "The morvalt" 6 |
| Neutral | 277 | 56 → 15 | "From the" 21, "From a" 19, "In the" 11, "From The" 9, "When the" 8, "Of the" 7 | "In the" 11, "When the" 8, "Of the" 7, "From the" 6, "The first" 6, "The court" 4 |

The caps in `tools/lore/repetition_caps.json`, lowered by hand for Midgard and Neutral only (`--write-caps` would
also have rewritten Albion and Hibernia, which other writers own). Every change is a decrease. Three new caps sit below
the free limit of 5:

- **Midgard openings:** "From the" 90 → 43, "From what" 14 → 4, "From Thorgrim" 10 → 4, "From Fenrir's" 9 → 3,
  "From The" 9 → 3, "From A" 7 → 2, "From Under" 4 → 2. New caps: "I went" 5, "I was" 4, "My grandmother" 4.
- **Midgard formulas** (per 10,000 words): "From the readings / ledger / trap lines of" 1.09 → 0.41, "From what X told"
  0.57 → 0.19, "as told in" 0.38 → 0.15, "the druids / bards / skalds of ... say" 1.36 → 1.13, "at the far end of the"
  0.57 → 0.56. Covered share 2.59% → 2.44%.
- **Neutral openings:** "From the" 21 → 6, "From a" 19 → 4, "From The" 9 → 4. New cap: "The imp" 4.
- **Neutral formulas:** "From the readings..." 0.25 → 0, "the druids / bards / skalds of ... say" 2.64 → 2.55,
  "never once" 3.14 → 3.13. Covered share 3.25% → 3.22%.

Paragraph openings with a teller line in Midgard and Neutral narrative: "The skalds say" 8 → 5, "The skalds of Jordheim
say" 6 → 0, "The sculptors say" 5 → 0, "The narfi say" 4 → 2, "The Galplen tellers say" 3 → 0, "The druids say" 4 → 2
(the two copies of Darkness Falls' Hibernian loremaster line stay).

`docs/1.2.1/REPETITION.md` was not regenerated, because `--write` rewrites every realm's section. Regenerate it once
the four realm branches are merged.

## Files changed

135 lore files plus the caps file. Midgard has 87 boss files and 6 locations (`delling-crater`,
`iarnwood`, `malmohus`, `muspelheim`, `tuscaran-glacier`, `yggdra-forest`). Neutral has 41 boss files and
`summoners-hall`. Run `git diff d56adeda -- source/server/GameServer/bots/autonomous/data/lore` for the list.

## Nothing cut

A word-subsequence check of every visible string, old against new, that counts a word as cut only when the new
string has fewer of it, finds only frame words:

- "From" (125) and the frame's own "the", "of", "by", "what", "that", "as" and "told";
- possessive forms ("Solvi of Hagall's trap-lines" for "the trap-lines of Solvi of Hagall", "Dame Elen of Caerwent's
  account");
- a verb for its noun ("reported" for "the report of", "wrote" for "written").

Every teller and document is still named in its passage, with its place: Thyri keeps "of Galplen", and Thorgrim
his notes. One continuity rule caught a word order ("Camelot, Sister Elaydith's runner"), and the line now reads "Sister
Elaydith's runner, Siencyn of Camelot".

## Checks (this branch, tests only; nothing was seen in game)

All pass: `depth_report.py`, `schema_mirror.py`, `lint_lore.py` (0 problems), `name_counts.py --check` (0 over cap),
`continuity.py --check` (0 contradictions), `repetition.py --check` (0 problems), `reachability.py --check`, the
`tools/lore` tests, `mechanic_trigger_coverage.py --check`, `build_corpus.py --check` and `--verify`,
`debates_doc.py --check`, `check_runtime_names.py`, `epic_encounter_catalog.py --check`,
`build_encounter_adds.py --check`, the `tools/bot-voices` tests (44) and the `source/tools` tests.
