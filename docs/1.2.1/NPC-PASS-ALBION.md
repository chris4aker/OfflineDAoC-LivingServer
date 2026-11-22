# NPC pass, phase 2: Albion

Owner rulings: [HEROES.md](HEROES.md) decisions 10 and 11. The cast is in [CAST-ALBION.md](CAST-ALBION.md) and the voice
in [VOICE-ALBION.md](VOICE-ALBION.md). Branch `wt9/npc-alb`. Tests only: no in-game check yet.

## What changed

- **Real present-day people in place of generic stand-ins** ("the Church of Camelot", "the priest of Caer Gothwaite",
  "the Academy", "the captain of Snowdonia Fortress", "the smith"), each with a moment of their own: a line of speech, a
  habit, a stake or a consequence. They come from `tools/lore/npc_roster.json`. Each file that names one has an
  `original` fact giving the roster source.
- **The recurring voices.** Lady Fridwulf is the second voice decision 11 approved, and she now recurs across Camelot,
  Caer Sidi, Krondon and the housing grant. Father Kenelm ties the Isle of Glass, the Crystal Cave and Caer Sidi
  together. Master Grundelth ties Camelot to the Lethantis Association.
- Elowen gets no new mentions. She is already in 9 files, and CAST-ALBION.md asks phase 2 to leave her. Caer Sidi's
  "the captain" and "the priest" keep their single labels (HEROES.md rule 2). The Priest of Arawn in the Dales of Devwy
  is a real NPC, not a stand-in, so he was left as he is.
- Nothing old was cut outside the one summary below. A word-diff of every changed string against `7b037dfb` shows only
  two kinds of removal:
  - stand-in words replaced by a name ("the captain" becomes "Captain Rhodri", and "The Church of Camelot, which"
    becomes "Lady Fridwulf of Camelot's Church, who");
  - the task-4 fixes below.

## The named NPCs and where they recur

| NPC (roster) | Files | Thread |
|---|---|---|
| **Lady Fridwulf**, cleric trainer, Camelot (probable) | `crypt-lord`, `fames`, `funus`, `lord-sanguis`, `skeletal-sacristan`, `soul-reckoner`, `bane-of-hope`, `krondon`, `albion-housing`, `camelot` (10, cap 12) | She reads Caer Sidi through the last book: Fames is the black horse, Funus the pale one, and the Reckoner a fallen recording angel. At Lent she reads the Sanguis account and the names of the thirteen dead. She blessed the housing grant lot by lot. Her seal is on the letter Wenna answered. She copied the Bane page for the dying at Caer Ulfwych. |
| **Father Kenelm**, cleric trainer, Caer Gothwaite (probable) | `crystal-cave`, `albion-shrouded-isles`, `caer-sidi`, `host`, `apocalypse` (8) | He blesses the last curve of the stair and keeps Corwen's page, and he will burn it only if the Academy says what it means. He would not share a room with the Inconnu for a month, and he is the loudest of their doubters. Brother Cadoc's dissent was left on his desk. He says the first mass for the thirteen. |
| **Master Grundelth**, wizard trainer, Camelot (probable; gives Symbol of the Broken) | `campacorentin-forest`, `cirfans`, `kings-wight`, `black-mountains-north` (5) | He sends the young on the Lethantis errands for the heart and the skull, disputes which school is older, and wants a crimson initiate taken alive. |
| **Captain Rhodri**, Snowdonia Fortress (probable; the Lord of Deceit giver) | `belgrik`, `ellyll-seer`, `isolationist-courier`, `overseer-tepok` (now 10) | He keeps a space on his shelf for the seer's journal. |
| **Captain Leri**, Snowdonia Fortress (probable) | `soulfear` | Leri's watch walks the lake wall on the nights the banshee sings. |
| **Captain Bonswell**, Prydwen Keep (probable) | `bellum` | Every rider of the keep touches Bellum's ONE MINUTE on the way out. |
| **Elvar Ironhand**, smith, Cotswold (probable) | `camelot-hills` | "says is good, as he says of every sword he sells". |
| **Brother Michel**, healer, Camelot (already in the file) | `camelot` | An atmosphere line: the queue is for him. |

## The 5 best moments

1. `locations/crystal-cave`, chapter:
   - Before: "...to the priest of Caer Gothwaite who gave the company its blessing... The page is in the priests' chapel
     at Caer Gothwaite."
   - After: "...to **Father Kenelm**, the priest of Caer Gothwaite who gave the company its blessing... The page is in
     the priests' chapel at Caer Gothwaite, and Kenelm reads it aloud to every magus who comes asking for it."
2. `bosses/skeletal-sacristan`:
   - Before: "The Church of Camelot ... says the sacristan is the one soul in it doing honest work, and that if he were a
     Christian sacristan he would be a saint."
   - After: "**Lady Fridwulf** of Camelot's Church ... says the sacristan is the one soul in it doing honest work. 'And
     if he were a Christian sacristan,' she tells her clerics, 'he would be a saint, and I would light his candles
     myself.'"
3. `locations/caer-sidi`:
   - Before: "...its priests at Caer Gothwaite say the stair is only the place where the Inconnu stop pretending... The
     priest, who is one of those priests himself, disagreed in writing."
   - After: "...its priests at Caer Gothwaite, **Father Kenelm** the loudest of them, say... The priest ... disagreed in
     writing, and left the page on Kenelm's desk."
4. `locations/albion-housing`:
   - Before: "...the Church of Camelot says the land was blessed when it was granted; the old soldiers say..."
   - After: "...blessed when it was granted: **Lady Fridwulf** walked the bounds of every lot with a cleric's bell and a
     jug of holy water, and the jug was empty by Sherborne; the old soldiers say..."
5. `bosses/lord-sanguis`:
   - Before: "...its priest's account of it is the one that the Church of Camelot reads at Lent."
   - After: "...reads at Lent: **Lady Fridwulf** reads it from the step below the altar, and the names of the company's
     thirteen dead after it." This pairs with `apocalypse`, where Father Kenelm says the first of the masses for the
     thirteen.

Also: `bosses/kings-wight`. Master Grundelth "says the Lethantis want a first king because they want to be first at
everything".

## Renames and fixes (task 4)

- **Sir Tillian became Sir Tilian** in `belgrik`, `ellyll-seer` and `forest-sauvage`. That spelling had 5 of the 8
  mentions, and it is the one the 1.65 world uses.
- **Magus Hedydd is "her" everywhere.** `wenoiak-the-enlightened` read "after his own school failed him ... He wrote",
  and now reads "her ... She wrote", to match `crystal-cave` (the majority, and the stronger scene).
- **The dead Rhodri on Dame Nest's roll is now Meirion** (`bane-of-hope`, 3 mentions), so the name no longer means two
  men. Meirion is used nowhere else in the lore and in no name generator.
- **Market Explorer became market crier** in `albion-housing`: in the story, the legend, a bot line and the Caerwent
  topic. The hidden game fact notes that the Market Explorer came after 1.65.

## Summaries (task 2)

- **`locations/avalon-city` story: rewritten**, 498 words to 495, and every event kept. The review's A4 found the bed
  paragraph repeated its chapter almost word for word.
  - That paragraph now opens on the bed and its water, then gives the king it waited for.
  - The landings are sharper: "No barge came." "It has run cold since." "...in the order the colleges burned."
  - The drakoran lords are now one sequence of warnings.
  - The rewrite came out better, so nothing was trimmed beyond the rewrite.
- **Read and left as they are:** the other 500-word epic summaries (`caer-sidi`, `crystal-cave`, `dartmoor`,
  `hall-of-the-corrupt`, `krondon`). The third-pass review already found they add new matter. Krondon's fourth
  paragraph (the old alliance) is the flattest, and an owner sample could decide whether it gets the same treatment.

## Ledger

Six entries were added to `tools/lore/continuity.json`:
- `tilian-one-spelling`
- `hedydd-is-she`
- `rhodri-is-the-captain`
- `fridwulf-four-riders`
- `thirteen-remembered`
- `albion-housing-no-market-explorer`

`CONTINUITY.md` was not regenerated, because no check marked it stale and the other realms' writers touch the same
file. `python3 tools/lore/continuity.py --write` refreshes it at merge.
