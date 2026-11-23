# How Hibernia's lore sounds (voice guide for the NPC pass)

## Rule 1: phenomenal and tight

Owner, 2026-11-22: "Stories should be phenomenal and tight", "don't be long for the sake of length".

- Every sentence adds a fact, an image or a consequence. A sentence that only restates, frames or announces goes.
- Nothing old is cut (decision 3). Tighten by rewording, never by dropping an event or a detail.
- A new name replaces a vague phrase; it does not sit beside it. "The druids of Tir na Nog say" becomes "Daray says", not
  "The druids of Tir na Nog, among them Daray, say". A name should usually leave the sentence the same length or shorter.

## Who tells Hibernia

Hibernia is a realm of keepers of the spoken word: bards, druids, brehons, keeners and grandmothers. It has no Church.
Its present-day tellers are the bard Aodhan, the fire-keeper Lisín, the druid Muirgen, Nóra the keener's daughter,
Derbáil of Ardee and her grandmother's list. The old world is close: the Tuatha Dé, the Fianna and the sidhe courts are
told as the grandmothers tell them, with a rule attached.

- **Diction:** Irish names in their accented forms (Lisín, Derbáil, Béal an Uisce), glossed once in passing ("the
  water's mouth"). Plain English around them; no stage-Irish.
- **Rhythm:** the triad and the turn. Three things, then the one that matters; a story that ends on a custom still kept
  ("the last of each company out leaves a green leaf on its stones"). A grandmother's saying may close a page.
- **Forms:** the list of places not to go, the keen, the brehon's judgement, the triad, the Sylvan teaching set down by a
  bard, the "whence the name?" opening (the seven triad pages and four "whence" openings stay, decision 10).
- **Stance:** wonder with a warning in it. The teller respects the thing told of and does not pretend to understand it.

## Model passages

1. `locations/galladoria`, story:
   > The water goes in clean and does not come out, and the springs of Domnann sour one by one. Nobody drinks past the
   > mouth, and the last of each company out leaves a green leaf on its stones.

   A wrong place, its cost to the living, and a custom.
2. `locations/marfach-caverns`, book:
   > My mother sang for forty years and never once looked back over her shoulder at a grave. She said the three who sang
   > for Caimheul under Breifine did look back, and that is the whole story of Marfach, if you want it short.

   A trade, a rule of the trade, and the whole dungeon in one broken rule.
3. `bosses/olcasgean`, story:
   > Cut a root, Lisín said, and the tree grows another trunk.

   A named teller, eight words, and the fight's mechanic told as wisdom.

## Passages to avoid

1. `locations/silvermine-mountains`, legend:
   > Howth's chief's hut is quest central: Chief Proinnsias (Nuada's Silver), Keriann, Cathbad (Destructive Forces,
   > Morven's Return, Cad Goddeau).

   A directory, with quest titles in brackets. The same three people in one sentence about what each wants would do.
2. `locations/lough-derg`, legend:
   > Aziliz by the island shore has a huge list of item quests.

   A guide's shrug. Say what Aziliz collects and why.

## Tics to stop repeating

Counted over the player-visible narrative and advice of the 210 Hibernia files, bot chat left out (sentences; files in
brackets), with the same one-off scan as VOICE-ALBION.md.

| Tic | Count | Instead |
|---|---|---|
| "From the ..." as an opening | 12 (11 files): Hibernia's frames name a person instead; 82 openings with "From" | keep it that way |
| "From Derbáil of Ardee's list:" | 27 (27) | Derbáil once per page, inside the sentence; or start with the place on the list |
| "From Coll of the Domnann guides:" | 19 (19) | the same |
| "The Sylvan say / believe / have / call / do not" | 149 (46) | Lisín, Coll or a named warden; or the claim itself |
| "The druids of Tir na Nog" | 49 (33) | Daray (CAST-HIBERNIA.md), Lovernios when the Druid Court acts |
| "The druids of Lough Gur" | 43 (25) | one named druid of Lough Gur |
| "Why does it matter?" | 17 (17) | fold it into the paragraph's first sentence |
| "Plain, no tricks; ..." / "A hardened band ..." as advice openings | 47 (47) / 46 (45) | vary the advice's first words |
| "Nobody ..." as an opening | 113 (66) | the person who did not |
| "says" / "say" | 1,604 (208) | one teller per paragraph |

## The epic voice (#405, 2026-11-22)

Midgard's epics have the saga (Lydsyg, Dyranapur, Hurjavelen). Hibernia's have the old Irish tale and its learned forms:
the teller is a poet, a brehon or a keeper of place-lore, and the form itself is part of what is told. Eleven epic
encounters carry one chapter each in this voice (listed in [EPIC-VOICE-HIBERNIA.md](EPIC-VOICE-HIBERNIA.md)).

**Forms** (use one per chapter, and say which in the first lines, the way the tale-lists name their kinds):
- **The tale and its kind.** An adventure behind the water, a voyage, a death, a contention for the champion's portion.
  It opens on a person and a place ("Sétna was one of the three hundred and forty...") and closes by naming itself
  ("That is the adventure of Sétna behind the water"), with the custom that came of it.
- **The watchman on the height.** One looks out and describes; one who knows names ("Not hard to know him"). Good for a
  company coming up a stair or a pass, told from the other side.
- **Place-lore (dindshenchas).** A name, why it is so called, what was done there, in prose ("Log Fliuch, the wet hollow.
  So called for...") or in quatrains that begin and end on the name, like a ring. The four "whence the name?" pages
  stay; new place-lore uses "So called because" or "why so called", not the capped formula.
- **The triad.** Three things and the turn; a set of triads may end on one that breaks the pattern on purpose.
- **The rhetoric (rosc) and its gloss.** Short, dark, unrhymed lines, then a glossator's line-by-line "that is", with the
  disagreements left in and one line left unglossed.
- **The brehon's judgment.** The pleas, the brehon's questions, the judgment in the law's own words, and the fee.
- **The colloquy.** Question and answer, a line each, with the one question that is not answered.
- **The charm.** Said through smoke or water, three turnings, a verse that answers the thing it unsays, and the rule
  that came with it.

**Cadence:** plain English, short clauses, the verb late when it carries weight ("Shape-walkers the Sylvan have always
had"); questions answered "Not hard"; place-names glossed once; verse unrhymed or half-rhymed, never jingling. Each
chapter ends on a custom still kept or a line left open, not on a moral.

**Avoid:** the saga's "There was a man named X, son of Y" and "Here ends" (Midgard's); saints, monks, scribes'
blessings, "Amen" (Hibernia has no Church); stage-Irish and Celtic-twilight mist; a form for its own sake (every line
of a triad or a gloss adds a fact); "myth" labels on new writing (the forms are old, the chapters are `original`); the
capped heroes (Fionn and Cú Chulainn are at or near the cap of 12: tell of the gods and the present-day tellers instead);
"whence the name", "never once" and "the druids of ... say" (capped formulas).

**Openings (#406):** a page opens in the scene, the document or the image, and the teller comes inside it ("The Cullen
road after dusk is on Derbáil of Ardee's list"). The counts in the tics table above are from before #406; the new ones
are in EPIC-VOICE-HIBERNIA.md.
