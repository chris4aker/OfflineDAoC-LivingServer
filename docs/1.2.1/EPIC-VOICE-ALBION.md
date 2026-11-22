# Albion's epic voice and teller openings (#405, #406)

Branch `wt11/v-alb`. Lore text was only added to or reworded. Every event and every teller's attribution is kept.
Tests only: the lore tools and the Python tests passed (listed at the end). `UT_LoreBible` was not run, because there is
no dotnet here. Nothing has been seen in game yet.

## The voice in brief

Albion tells its epics in the forms of the matter of Britain: the chronicle that records, and the romance that tells.
The full guide is in [VOICE-ALBION.md](VOICE-ALBION.md), "The epic voice".

- **The annal:** a line to a year, nothing set down that was not seen, the comment in the margin in another hand.
- **The romance's adventure:** "Now leave we... and tell we", long sentences joined by "and", the turn on "Then" or
  "Now". It can end, as Malory does, on what the book could not find out.
- **The vow:** the oath set out line by line, then who made it and what each line answers.
- **The hermit's interpretation:** a knight brings a sign to a hermit, who reads back what each part "betokens".

## The encounters given the voice (one new chapter each, 422-588 words)

| File | Chapter | Form | Sourcing |
|---|---|---|---|
| `bosses/bellum` | Under the Years of the Battles | annal | `myth`: *Annales Cambriae* (Badon, Camlann, Maelgwn's great death), *Historia Brittonum* ch. 56 (the twelve battles); the copy and its margins `original` |
| `bosses/apocalypse` | The Hermit Reads the Lintel | hermit | `original` |
| `bosses/bane-of-hope` | The Walkers' Vow | vow | `original` |
| `bosses/silencer` | How the Ten Went In | romance | `original` (retells the story's events; the toll is unchanged) |
| `bosses/duraek-the-empowered` | The Annal of the Three Nights | annal | `original` |
| `bosses/warlord-dorinakka` | The Lay of the Headland | romance (a lay with its refrain) | `original` |
| `bosses/scurceol-hyrde` | The Weather-Book's Last Leaves | annal | `original` |
| `locations/avalon-city` | The Hermitage Between Two Holts | romance with the readers' gloss | `myth`: Malory, book 21 (the barge, the hermitage, the queens, "rex quondam rexque futurus"); the readers' reading `original` |
| `locations/hall-of-the-corrupt` | The Oath of the Post | vow | `original` |
| `locations/dartmoor` | The Annal on the Back of the Map | annal | `original` (adds no event) |

Continuity: the chapters keep the ledger's forms (the Caer Sidi toll of 80, 9, 2, 2 and 67; Dame Nest's eleven; the
Gwennol "did not right herself"; Arthur told of, never present). Bedivere, Gawain and Lancelot are at the cap of 12, so
the Avalon chapter calls Bedivere "the king's last knight". Golestandt's chapters were left alone (the audit's "keep").

## Three samples for the owner

**1. `bosses/bellum`, new chapter (the annal).** Before, the story had one line: "the bards put him at all twelve of
Arthur's battles and on both sides at Camlann." After:

> Year ninety-three. The strife of Camlann, in which Arthur and Medraut fell; and there was a great death in Britain and
> in Ireland.
>
> Against Camlann the reader wrote nothing. The space is ruled and empty, as if he meant to come back to it. [...]
>
> The year of the rising. The third company climbed the turning fortress in the autumn. War gave them his minute, and
> their wizards brought him down, and the iron in his hall lay down at once.
>
> The year after. Prydwen Keep cut the minute into its gate.
>
> Under that, in the same hand, one more word against the year of the rising: here, for the thirteenth time. Then the
> margin runs out.

**2. `bosses/silencer`, new chapter (the romance's cadence).** Before, the summary: "Unable to make herself heard, its
captain went along the line with her hand on each shoulder and sent in ten." After:

> Now the ten went in against the keeper, and none of them spoke. When his stillness gathered they stood back out of it
> and waited the length of a slow paternoster, which the priests said under their breath and the swords counted by, and
> when it lifted they went in again. [...] And the company's priest wrote that he heard, after, the first sound in the
> inner halls since the rising, and it was the ten breathing.

**3. Openings (#406).**

| File | Before | After |
|---|---|---|
| `bosses/amphiptere` | From the Ledger of Little Haven, kept by Mordwywr. Paid nothing. The clan came back... | Paid nothing: so the Ledger of Little Haven, kept by Mordwywr, enters it. The clan came back... |
| `bosses/sergeant-reede` | From the copy-book of the Constable's office at Camelot, the answer to the clerk of the Pennine watch, sent north with Sir Hamon Brede in the spring. To Tobias, clerk, greeting. | To Tobias, clerk, greeting. So the copy-book of the Constable's office at Camelot gives its answer to the clerk of the Pennine watch, sent north with Sir Hamon Brede in the spring. |
| `locations/avalon-marsh` | Of the shamblers. You call them monsters. They are the marsh, standing up. | The shamblers you call monsters. They are the marsh, standing up. |

## Opening counts (Albion book pages and chapters; `repetition.py`)

| Opening | Before | After |
|---|---|---|
| pages and chapters | 682 | 692 (ten new chapters) |
| first word "From" | 78 | 7 |
| "From the" | 72 | 1 (`golestandt`, left alone) |
| "Of the" | 95 | 40 |
| "The hall-folk" | 8 | 4 |
| "A page" | 5 | 3 |
| "The keepers" | 4 | 3 |
| formula "the bards / druids / friars of X say" (per 10,000 words) | 0.56 (16) | 0 |
| formula "From the ledger / roll / count of" | 0.6 (17) | 0.03 (1) |
| words in repeated phrases | 2.74% | 2.67% |

The caps in `tools/lore/repetition_caps.json` are lowered to these counts: Albion's openings, its formula rates and its
share, and the phrases its rewording broke up ("from the ledger of little haven" 10 to 0, "the second book of gracien
adamson" 8 to 5, the Barfog Book phrases). Every change is a decrease, checked key by key; no cap was raised or removed,
and no other realm's caps were touched.

**What was removed.** A word-count comparison of every changed string against the old one (149 strings) finds that
the only words gone are the frames themselves: "from" (70), "of" and "the", "kept by" (the keeper is still named), "say"
in the teller lines (the teller is still there), and the section headings of location pages whose first sentence already
names the subject ("Of the bishop. Your bishop came..." is now "Your bishop came..."). Where the heading's subject was not
in the sentence, it is folded in ("The fair folk, our neighbours, hunt..."; "The padfoot of the night roads..."). Some
headings were kept ("Of the marsh", "Of the lammia", "Of the cattle"), because the page would otherwise fall below its
100 words, or because the sentence after it opens "In the", "On the" or "When the", which are at their caps.

## Files changed

103 lore files (79 bosses, 24 locations), listed by `git diff --name-only d56adeda -- source`: the 10 with a new chapter
(above) and the rest with reworded openings or teller lines. Tools: `tools/lore/repetition_caps.json` (lowered). Docs:
[VOICE-ALBION.md](VOICE-ALBION.md) (the epic voice and openings sections), [REPETITION.md](REPETITION.md)
(regenerated) and this file.

## Checks (tests only, on `wt11/v-alb`)

All pass: `depth_report.py` (0 below), `schema_mirror.py`, `lint_lore.py`, `name_counts.py --check`,
`continuity.py --check`, `repetition.py --check`, `reachability.py --check`, `mechanic_trigger_coverage.py --check`,
`build_corpus.py --check` and `--verify`, `debates_doc.py --check`, `check_runtime_names.py`,
`epic_encounter_catalog.py --check`, `build_encounter_adds.py --check`, and the tests in `tools/lore/tests`,
`tools/bot-voices/tests` and `source/tools/tests`.

## For the owner

- An independent prose review of this batch is still to do (#405 and #406 ask for one, by someone who did not write it).
- Should the Avalon chapter's reading (Mistress Angharad's question whether Morgana, the queen in Malory's barge, steered
  it elsewhere) stand? It is marked `original` and told as the readers' question, not as fact.
- "Of the" stays at 40: those pages are a narrator's book in sections, a real chronicle form. Lower it further only if
  you want.
