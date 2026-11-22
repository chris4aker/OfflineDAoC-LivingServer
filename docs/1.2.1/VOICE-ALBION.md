# How Albion's lore sounds (voice guide for the NPC pass)

## Rule 1: phenomenal and tight

Owner, 2026-11-22: "Stories should be phenomenal and tight", "don't be long for the sake of length".

- Every sentence adds a fact, an image or a consequence. A sentence that only restates, frames or announces goes.
- Nothing old is cut (decision 3). Tighten by rewording, never by dropping an event or a detail.
- A new name replaces a vague phrase; it does not sit beside it. "The priest of the company, a man named..." is wrong;
  "Brother Cadoc" is right. A name should usually leave the sentence the same length or shorter.

## Who tells Albion

Albion is a realm of records. Its tellers are clerks, readers, registrars and knights who sign what they saw: the
Constable's office, the readers of Caer Gothwaite, the Academy's magi, the Church's Door-Book, a scribe's scorched
journal. The voice is that of a document that knows it may be the only one left.

- **Diction:** plain English with Latin and Saxon underneath (onstal, ealdor, bysen, Caerwent, Venta). Offices and titles
  are exact: reeve, registrar, constable, chancellor, prior. Old words are glossed once, in passing, never twice.
- **Rhythm:** a long sentence that sets the place, then a short one that lands. "Albion learned her name exactly once,
  and paid thirty knights for it." The short sentence carries the cost.
- **Forms:** the clerk's report, the register entry, the ledger line, the letter with its greeting ("To Tobias, clerk,
  greeting."), the commonplace book, the note in another hand below the text ("Below, in another hand, Elowen's: she was
  not."), the roll of names.
- **Stance:** the teller reports and lets the reader judge. Grief is shown by what is counted ("forty-four walked out"),
  not by adjectives.

## Model passages

1. `bosses/golestandt`, chapter "The Name in the Journal":
   > Albion learned her name exactly once, and paid thirty knights for it.

   One fact, one cost, eleven words. The chapter then earns it with Articus's castle and Hywel's journal.
2. `bosses/captain-heathyr`, story:
   > Her father Cenred made the doors of half of Camelot Hills and taught her that a door is only as good as the moment it
   > closes.

   A present-day person defined by a trade and a saying, and the saying is the fight to come.
3. `locations/hall-of-the-corrupt`, story:
   > Vibius walled it up and gave it to the gods below on a tablet that now hangs in the captains' hall, and the Pennine
   > miners kept one saying from it: never answer a question asked from below.

   An object that is still there, and a rule the living keep.

## Passages to avoid

1. `locations/snowdonia`, legend:
   > Snowdonia Fortress in the southern valley is the realm's frontier town: Captain Rhodri (Path of the Renegade, lots
   > of quests), Captain Leri, ...

   A directory with parentheses. Names with no deed, and "lots of quests" is a guide's voice.
2. `bosses/adelon`, legend:
   > The quest runs: Pazia's key in the house below the hall, Tyian's sphere on the second floor, ...

   A walkthrough. The same facts read in-world: who keeps the key, who wants the sphere and why.

## The epic voice: romance and chronicle (#405)

Midgard's epics have the saga ("There was a dwarf named Bodvar... Here ends the tale"). Albion's have the matter of
Britain: the chronicle that records and the romance that tells. An epic chapter takes one of four forms, and says so in
its first lines (a document named once, or the cadence itself). The teller stays a person of the realm: a reader, a
clerk, a sexton, a hermit. The legends are told of, never present.

| Form | What it is | Cadence | Model |
|---|---|---|---|
| **The annal** | A line to a year, nothing set down that was not seen. The old Welsh annals are the pattern. | "Year seventy-two. The battle of Badon..." Short entries, a year or a night as the heading, no adjective. A later hand in the margin is the only comment, and it is one word or one line. | `bosses/bellum` "Under the Years of the Battles"; `bosses/duraek-the-empowered` "The Annal of the Three Nights"; `locations/dartmoor` "The Annal on the Back of the Map"; `bosses/scurceol-hyrde` "The Weather-Book's Last Leaves" |
| **The romance's adventure** | An event told in the cadence of the old romances: "Now leave we... and tell we", "and so", "Thus it went". Long sentences joined by "and". | Parataxis, plain verbs, the turn marked by "Then" or "Now". It may end, as Malory does, on what the book could not find out. | `bosses/silencer` "How the Ten Went In"; `locations/avalon-city` "The Hermitage Between Two Holts"; `bosses/warlord-dorinakka` "The Lay of the Headland" (with its refrain) |
| **The vow** | The words sworn, set out line by line in the first person, then who wrote them and what each line answers. | "I will... If I am... I will not..." Each line a deed the encounter asks for, so the fight is in the oath, never as advice. | `bosses/bane-of-hope` "The Walkers' Vow"; `locations/hall-of-the-corrupt` "The Oath of the Post" |
| **The hermit's interpretation** | As in the Grail romances, a knight brings a sign (a dream, a lintel, a wound) to a hermit, who reads it back to him. | "Now I shall tell you what these numbers betoken." One reading per sign, then the question the hermit will not answer for him. | `bosses/apocalypse` "The Hermit Reads the Lintel" |

**Avoid:**
- Pastiche spelling ("ye olde", "-eth" on every verb). The cadence carries it; "betoken" and "Now leave we" are enough.
- An annal that comments on itself. The comment goes in the margin, in another hand, and is short.
- A vow that is fight advice in disguise ("I will interrupt his casting"). The vow says what a person does for the one
  beside him; the deed is the same, the words are not.
- New "myth": only what the cited text says (the Welsh annals, the *Historia Brittonum*, Malory) is marked `myth`. The
  in-world copy, its margins and every reading of it are `original`.
- The heroes at the cap (Bedivere, Gawain, Lancelot at 12): give them their deed by epithet ("the king's last knight",
  "Sir Lucan's brother").

## Teller openings (#406)

An Albion page used to open by naming its document: "From the Ledger of Little Haven, kept by Mordwywr." (72 pages) or by
a section heading, "Of the cattle." (95). The page now starts with its first fact, image or line ("Paid nothing: so the
Ledger of Little Haven, kept by Mordwywr, enters it."; "To Tobias, clerk, greeting."), and the document and its keeper
come after, in the same passage. Keep it so: name the document once, after the first fact, and vary how (a clause, a
colon, "as X records it", the keeper as the subject). `tools/lore/repetition.py --check` holds the counts
([EPIC-VOICE-ALBION.md](EPIC-VOICE-ALBION.md) has them).

## Tics to stop repeating

Counted over the player-visible narrative and advice of the 198 Albion files, bot chat left out (sentences; files in
brackets). A one-off scan on 2026-11-22: every string `fourth_wall.visible` returns, the `chat` kind left out, split
with `lore_text.sentences`.

| Tic | Count | Instead |
|---|---|---|
| "From the ..." as an opening | 81 (76 files); 98 openings with "From" (page openings after #406: "From the" 1 of 692, "From" 7) | name the document once, after the first fact, and start with the fact |
| "From the Ledger of Little Haven, kept by Mordwywr" | 10 (10) | vary the frame, or start with the entry itself |
| "From the second book of Gracien Adamson, name registrar of Fort Gwyntell" | 8 (8) | the same |
| "The Inconnu say ..." | 97 (28) | a named Inconnu guide (Sefin is at the cap), or the claim without a frame |
| "The readers of Caer Gothwaite" | 23 (15) | the reader who said it |
| "The Church of Camelot" | 24 (21) | the cleric who acted (Lady Fridwulf, Brother Ethelbald) or the Door-Book |
| "Why it matters to Albion." / "What went wrong." / "What lives there now." as sentences | 27 / 17 / 12 | fold the heading into the first sentence of its paragraph |
| "Nobody ..." as an opening | 89 (57) | the person who did not, by name or role |
| "I do not know" as an opening | 27 (23) | once per teller at most; then say what the teller does know |
| "says" / "say" | 2,024 (190) | one teller per paragraph; after that, tell the thing |
| "a foe for the realm's ..." | 68 (65) | fight advice only, never in a story |
