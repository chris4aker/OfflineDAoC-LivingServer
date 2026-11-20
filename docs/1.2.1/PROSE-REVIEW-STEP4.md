# Prose review: lore step 4 (heroes), origin/integration/1.2.1 -> origin/lore/step4-heroes

Reviewer: independent, read-only. Before = `origin/integration/1.2.1`, after = `origin/lore/step4-heroes` (tip df2f99ffc).
Scope: the 8 named files read in full (every story, chapter, legend, bookPage, loremaster text, yells and botLines, before and after), then every changed field in the other 44 changed lore files (word-level diff) plus the three new history hero entries.

## Overall verdict: FIX before merge (not a fail, not a pass)

The base prose was good: 2002-era Herald/folk register, concrete detail, consistent witnesses. Most of the rewrite is a careful name-for-role swap, nothing was cut (no event or detail lost; 4 fields are 1 to 2 words shorter, all from name removal), there is no developer vocabulary and nothing after 1.65. Legends are always told about and never put in a scene. **But** the three writing groups (commits 2706cba50 = A, 4cf1e611a = B, 6837991d9 = C) each anonymized the same people a different way, and nobody reconciled them. The result:

1. **One person, three or four names.** Brother Cadoc is now "the company's priest" (group A), "a friar of Caer Gothwaite / the friar" (B), "the company's chaplain / the chaplain" (C), and "the priest of the summer company" (caer-sidi). Dame Ysolde is "the captain" (A, B) or "the commander" (C). Her company is "the third company" (A), "the company that ended the end of the world" (B) or "the company of Caer Gothwaite" (C). A player doing the Caer Sidi raid in order (Crypt Lord -> Host -> Ilron -> Sanguis -> Silencer -> Reckoner -> riders -> Apocalypse) meets all three versions and will read them as different people.
2. **About 17 dangling or wrong antecedents.** The worst are a bookPage told in the first person that says "the captain of that company" (4 files), "her" that now points at Dame Nest so that *Nest's* company gets credit for breaking the Bane of Hope (meaning reversed), and "the captain of that company" in Ilron that now points at Sir Tegid's company, so a female captain leads a dead man's company.
3. **Bolted-on heroes in Caer Sidi.** Six of the eight boss-file hero insertions sit in the one dungeon (Galahad x2 on the same Siege Perilous anecdote, Bedivere x3). Two insertions break the pronoun of the sentence that follows them (Apocalypse ch2, Funus ch2). Host's Galahad lesson contradicts Malory and the branch's own history/albion.json, and Soul Reckoner's Bedivere deed is invented. Lurfos (Gawain) is the one clear fit.
4. **The tooling does not enforce the hero cap.** `tools/lore/name_counts.py` counts only honorific+name, so bare "Galahad", "Bedivere", "Sigurd" and "Gawain" are not counted. Raw narrative counts (facts excluded): Gawain 14, Bedivere 13 (both over the HEROES.md cap of 12 if history counts), Galahad 10, Sigurd 10, Ragnar 11, Cu Chulainn 10.

All of this can be fixed mechanically in about a day: one name scheme across Caer Sidi, about 25 sentence-level edits (listed below), and moving two hero lines. After that it merges.

## Problem counts (all changed files)

| Type | Count | Notes |
|---|---|---|
| Broken or ungrammatical sentences / markup | 3 | double comma (darkness-falls), broken loremaster link `[the captain]` (bane-of-hope), run-on hero clause (silencer story) |
| Awkward but parses | 4 | "fell to the company that ended the end of the world in the end" (morbus); "The company's priest, who is a priest of that Church" (apocalypse); "The priest's company" for the captain's company (apocalypse); "the Door-Book's friar wrote their names in it as they swore it" (prince-asmoien) |
| Dangling / wrong antecedent | 17 | see section P1 |
| Two figures blurred together | 9 classes, 30+ instances | see section P2 |
| Hero misuse (contradicts source, invented deed, breaks the next sentence, bolted on) | 7 | see section P3; plus 1 provenance mislabel and 1 tooling gap |
| Shortened passages | 0 events lost; 4 fields lose 1 to 2 words | one *link* lost: funus story no longer says the Caerwent knight who wrote about the dragonfire harness also led the next summer's company |
| Repetition tics created | 10 phrases | see the frequency table |

### Word-frequency check (narrative text, facts excluded; before -> after, whole lore tree, ~1.08M words)

| Phrase | Before | After |
|---|---|---|
| the captain | 102 | 163 |
| the company | 152 | 201 |
| the commander | 17 | 52 |
| the friar | 5 | 33 |
| the third company | 2 | 28 |
| the chaplain | 1 | 23 |
| the company's priest | 3 | 22 |
| that company | 3 | 19 |
| a friar of Caer Gothwaite | 0 | 15 |
| the company that ended the end of the world | 0 | 13 (5 in morbus.json alone) |
| a knight of Caerwent | 0 | 13 |
| a knight of the Isle of Glass | 2 | 12 |
| the company of Caer Gothwaite | 0 | 8 |
| the captain of that company | 0 | 7 |
| as the bards say | 1 | 2 (not a tic) |
| the old knights of Albion | 0 | 2 |

The phrases the brief flagged: "a friar of Caer Gothwaite" (15) is a real tic and is always indefinite, so every use reads like a new friar. "The company" (+49) is overused but mostly harmless. "As the bards say" is not a problem. The worst new tic is "the company that ended the end of the world": the phrase already repeats "end", it is used as a running epithet, and in Morbus it appears in consecutive sentences.

Retiring names, after: Ysolde 2, Cadoc 2 (both in caer-sidi, which is fine as the anchors), Cador 0, Geraint 0, Odran 6, Captain Rhodri 6 (Rhodri is a real Mythic NPC at Snowdonia Fortress, so removing him from the Snowdonia/Tepok stories trades canon for variety. The owner chose that, but it should be noted).

## Per-file grades (8 files, read in full)

Scale A to F. "Hero" is n/a where no hero was added. Before grades are for the base text on integration/1.2.1.

| File | Clarity B / A | Voice B / A | Continuity B / A | Hero use (after) | One-line reason |
|---|---|---|---|---|---|
| silencer.json | A / A- | A / B+ | A / B | C | Clean swap in ch2 and ch3. But ch0's "A friar of Caer Gothwaite wrote" and ch2's "the company's chaplain wrote" are the same man under two names. The Galahad clause is bolted onto the story with "and", and two descriptions of the Siege Perilous disagree. |
| apocalypse.json | A / B- | A / B+ | A / C- | C | "The third company" appears unexplained while the same text says "the first company of Albion". The ch2 Bedivere insert breaks "it". "A captain of Caer Gothwaite told her company" reads as a second captain. |
| host.json | A / C+ | A / B | A / D | D | The story and bookPage say "the captain of that company" with no antecedent (the bookPage is first person). The Galahad lesson contradicts Malory and history/albion.json, and calls the Grail songs "older than the gaol". |
| funus.json | A / C+ | A / B | A / D | D | The bookPage POV is broken. "The captain's" appears twice with no captain introduced. "That company" now points at a hypothetical company. The Bedivere insert takes over the next sentence's "He". |
| soul-reckoner.json | A / A- | A / A- | A / B+ | C- | The swap is clean inside the file. The Bedivere-counts-the-fallen deed is invented, contradicts ch0's origin ("a scribe of some king long before Rome"), and has no fact entry. |
| lurfos-the-herald.json | A / B | A / B+ | A / C+ | B+ | The Gawain answer-by-name parallel is the best hero use on the branch. But "A knight of the Isle of Glass", "the commander" and "the knight who held the crier" blur together, and the story about giving one's name now has a nameless answerer. |
| warlord-dorinakka.json | A / A | A / A- | A / A- | C+ | The cleanest swap of the eight. The Gawain line misstates the myth (his strength waxed to noon, not "failed at dusk"), echoes the Prydwen line two sentences earlier, and has no myth fact. |
| morbus.json | A / B | A / B | A / B- | n/a | Every event is kept, but "the company that ended the end of the world" is used five times, twice in consecutive sentences, and the ch3 opener reads "...ended the end of the world in the end". |

Group pattern: group A files (apocalypse, bane-of-hope, bellum, fames, crypt-lord, caer-sidi...) use **third company / the captain / the company's priest**. Group B files (host, funus, morbus, ilron, sanguis x2, lurfos, krondon...) use **the company that ended the end of the world / the captain / a friar of Caer Gothwaite**. Group C files (silencer, soul-reckoner, dorinakka, sacristan, provisioner...) use **the company of Caer Gothwaite / the commander / the chaplain**. Groups B and C also use **the commander** for Sir Cador (a man) in the Krondon files, so "the commander" is a woman in Caer Sidi and a man in Krondon.

## Problems and fixes

### P0. Pick one name scheme for Caer Sidi (fixes most of P2)

Recommendation, using the label the branch already anchors in locations/caer-sidi.json ("Brother Cadoc ... who went up with the third company of the autumn"; "She trained the third company instead"; "The third was Dame Ysolde's"):
- Ysolde's company -> **the third company** on first mention in each file ("the third company of that autumn" the first time), then "the company".
- Ysolde -> **the captain of the third company**, then **the captain**. Never "commander" in Caer Sidi. Keep "the commander" only for Cador in Krondon.
- Cadoc -> **the company's priest** first, then **the priest**. Drop "friar" and "chaplain" in the Caer Sidi files. Where the account is quoted, write "the priest who wrote the third company's account" once per file.
- Replace "the company that ended the end of the world" everywhere (13) with "the third company". Where the point needs it, use "the third company, the one that ended the end of the world", once per file at most.
- Replace "the company of Caer Gothwaite" (8) with "the third company".

### P1. Dangling or wrong antecedents (17)

1. host.json story: "The captain of that company believed Ennyn that there was no tell." -> "The captain of the third company believed Ennyn that there was no tell."
2. host.json bookPage: "and the captain of that company shouted that only one of them was real" -> "and our captain shouted that only one of them was real"
3. funus.json bookPage: "and the captain of that company said only: now." -> "and our captain said only: now."
4. lord-sanguis.json bookPage: "The captain of that company shouted that nobody was to touch his corpse" -> "Our captain shouted that nobody was to touch his corpse"
5. funus.json ch1: "saw its priests glow as the captain's had" -> "saw its priests glow as the third company's had"
6. funus.json ch3: "The captain's candle was put out by Ennyn of the Inconnu" -> "The third company's candle was put out by Ennyn of the Inconnu"
7. funus.json ch2 (the hero insert takes over "He"): "...a king who might not be dead. He thought afterwards that the rider had listened." -> move the Bedivere sentence after "He thought afterwards that the rider had listened." and start it "The friars of Caer Gothwaite say the old songs give...". Better: cut it (see P3).
8. funus.json ch2: "That company did not let it go slack." (now points at the hypothetical "A company that lets the fighting go slack") -> "The third company did not let it go slack."
9. apocalypse.json ch2: "Albion's tellers say Sir Bedivere was the last of them down the stair, and the one who carried the story to the shore. The Inconnu think it the wisest thing any king of Albion ever said." -> swap the two sentences: "...and that is why seven came home. The Inconnu think it the wisest thing any king of Albion ever said. Albion's tellers add that Sir Bedivere was the last of the six down the stair, and the one who carried the story to the shore."
10. lich-lord-sanguis.json story: "The company that ended the end of the world wore it down ... Her swordsmen could not find it." ("her" has no antecedent) -> "The third company wore it down ... The captain's swordsmen could not find it." (the following "her priests ... her wizards" then work)
11. bane-of-hope.json story: "It scattered the third company too, and her company broke it" ("her" = Dame Nest, so the meaning is reversed) -> "It scattered the third company too, and the third company broke it"
12. bane-of-hope.json ch1: "and the company's priest stood up three halls away from her with the dead turning to look at him" ("her" = Nest) -> "and the company's priest stood up three halls away from his captain with the dead turning to look at him"
13. lich-lord-ilron.json story: "The captain of that company set her company along the wall" ("that company" = Sir Tegid's, a man's, which is dead) -> "The captain of the third company set her company along the wall"
14. lich-lord-ilron.json bookPage: "That is where the company I went up with the summer before had broken. This time the captain of that company waited" -> "...had broken. This time our captain waited"
15. lich-lord-ilron.json loremasterTopic: "The squire who found him for the captain of that company said:" -> "The squire who found him for the captain of the third company said:"
16. apocalypse.json story and ch6: "the third company" is never introduced, and ch6 says "The first company of Albion to bring Apocalypse down". Story: "That priest wrote that the third company were not the end's conquerors but its turnkeys." -> "That priest wrote that his company, the third to climb that autumn, were not the end's conquerors but its turnkeys." ch6 opener: "The first company of Albion to bring Apocalypse down was led by a captain of Caer Gothwaite" -> "The first company of Albion to bring Apocalypse down, the third to climb that autumn, was led by a captain of Caer Gothwaite"
17. funus.json story: "It was true for the company whose priests and bowmen laid him down" -> "It was true for the third company of Caer Gothwaite, whose priests and bowmen laid him down"

### P2. Two figures blurred together (9 classes)

1. **Cadoc under four names**, including within one file:
   - silencer ch0 "A friar of Caer Gothwaite wrote that the Silencer's shape changes" vs ch2/ch3 "the company's chaplain". Fix: "The company's chaplain wrote that the Silencer's shape changes" (or adopt P0's "priest" in all three).
   - spectral-provisioner ch2 "A friar of Caer Gothwaite counted its turnings" and, in the same chapter, "the chaplain wrote". Fix: "The company's chaplain counted its turnings and lost count."
   - skeletal-sacristan ch1 "A friar of Caer Gothwaite wrote that Ennyn" vs ch2 "The chaplain wrote". Fix: "The company's chaplain wrote that Ennyn".
   - host ch3 "A friar of Caer Gothwaite keeps a hawthorn sprig in his breviary." (an indefinite article for the friar who wrote ch2) -> "The friar keeps a hawthorn sprig in his breviary."
   - caer-sidi ch6 "The priest of the summer company reads the sill" reads as a different man from the third company's priest -> "The priest, who went up with both the summer company and the third, reads the sill"
2. **Ysolde = captain (A, B) or commander (C); Cador = commander (B, C).** Fix with P0: in silencer, soul-reckoner, warlord-dorinakka, skeletal-sacristan and spectral-provisioner, change "commander" to "captain" (for example dorinakka ch2 "Their commander did not try to break it in the middle." -> "Their captain did not try to break it in the middle."; dorinakka ch2 "The commander rebuked him for it on the stair" -> "The captain rebuked him for it on the stair").
3. **Three labels for Ysolde's company**: see P0.
4. **fames.json ch2, "the captain" is two people in two sentences**: "Ennyn said afterwards that he had seen the rider choose a captain only once before, and that the captain had been a king. The captain said yes." -> "...and that that captain had been a king. The captain of the third company said yes."
5. **lurfos.json, the killer, the commander and the tank**: story "A knight of the Isle of Glass killed him on his second descent ... the commander kept the company a long stride from the shoulder of the knight who held the crier" -> "The knight-commander of the Isle of Glass killed him on his second descent ... he kept the company a long stride from the shoulder of the knight who held the crier". ch3: "A knight of the Isle of Glass killed Lurfos on his second descent" -> "The knight-commander of the Isle of Glass killed Lurfos on his second descent" (orshom-brong already uses "knight-commander").
6. **apocalypse.json ch2, indefinite captain**: "the one a captain of Caer Gothwaite told her company at the foot of the stair in a later age" -> "the one the captain of the third company told her company at the foot of the stair in a later age"
7. **Odran's Door-Book under three names** ("the Door-Book of the castle ruins", "the Door-Book of Camelot Hills", "the muster friars' Door-Book"), and its keeper under five ("the warden of the castle ruins", "the castle warden", "a friar of Camelot Hills", "the Door-Book's friar", "its keeper"). Fix: always "the Door-Book" for the book, and "the warden of the castle ruins" first, then "the warden", for the keeper. For example high-lord-oro bookPage "From the muster friars' Door-Book" -> "From the Door-Book of the castle ruins"; prince-abdin story "read to a friar of Camelot Hills by an imp" -> "read to the warden of the castle ruins by an imp".
8. **"A knight of Caerwent" (13)** merges Geraint with the named Caerwent people (Sir Cynwrig Goch, Dame Elen of Caerwent). In funus the old link is lost: "a knight of Caerwent wrote" and "a company out of Caerwent found the next summer". Fix (funus story): "but a company out of Caerwent found the next summer" -> "but the same Caerwent knight's company found the next summer". The same in funus ch1: "A company out of Caerwent, the summer after," -> "That knight's own company, the summer after,"
9. **morbus/funus/host, "the company that ended the end of the world" as a running epithet**: see P0. Morbus ch3: "Morbus fell to the company that ended the end of the world in the end, and Sister Tangwen" -> "Morbus fell to the third company in the end, and Sister Tangwen"

### P3. Hero use (7, plus provenance and tooling)

1. **host.json ch2, Galahad contradicts the source and the chronology.** "The priests of Caer Gothwaite say the lesson is older than the gaol: in the songs of the Grail, Sir Galahad took the one seat at the Round Table that destroyed any man not meant for it, and was not harmed, because he never looked for a sign that he was the right one. The right body does not show it, they say, and the count is the only honest way to find it." In Malory the Siege Perilous *does* show it: the letters change to Galahad's name, as history/albion.json on this branch says. And Galahad comes after Arthur's climb, so the lesson is not "older than the gaol". It is also the second Siege Perilous use in Caer Sidi. **Fix: cut both sentences** (the chapter worked without them). If a hero is wanted, use the count: "The priests of Caer Gothwaite say the Round Table itself kept the same rule: the seat was written with the right knight's name only after he had sat in it."
2. **silencer.json story, bolted on with "and"**: "The company's chaplain has preached the Silencer every year since, never the same sermon twice, and the knights of Albion set the ten beside Galahad, who sat the seat at the Round Table that every other knight had left empty for fear." -> "The company's chaplain has preached the Silencer every year since, never the same sermon twice; in the fourth year he preached on Galahad and the seat at the Round Table that killed every knight who tried it." This also makes it agree with ch3's version ("had killed every knight who tried it") instead of "left empty for fear". In ch3 the lesson "kind to the one who goes in quiet" does not fit the ten. Fix: "kind to the one who goes in quiet" -> "kind to the few who go in quiet".
3. **apocalypse.json ch2 Bedivere breaks "it"**: see P1-9. Also facts[4]: "Later tellers count Sir Bedivere (Welsh Bedwyr) ... among those who came home." is marked provenance `myth`, but no source names Bedwyr among the seven of *Preiddeu Annwfn*. Fix: split it. Keep the Prydwen/seven sentence as `myth` and move "Albion's tellers count Sir Bedivere among the six" to the `original` fact (facts[5]).
4. **funus.json ch2 Bedivere breaks "He" and does not fit**: "The friars of Caer Gothwaite say the old songs give the same helplessness to Sir Bedivere, who stood on the shore when the barge took the wounded Arthur out of sight and found that he had no office to say over a king who might not be dead." The friar *did* have an office to say, so this is not "the same helplessness". Fix: cut it. Funus is the third Bedivere use in the rider/apocalypse sequence.
5. **soul-reckoner.json ch3 invented deed**: "The old knights of Albion remember Bedivere, who stayed on the field at Camlann to count the fallen and then carried the tale away, and say the Reckoner is that same clerk, never let to leave the field." Bedivere does not count the fallen in Malory. "That same clerk" contradicts ch0 (a scribe "long before Rome"), and there is no myth fact for it. Fix: cut it, or reframe as an explicit comparison: "The old knights of Albion say he is what Bedivere would have become, had he been left on the field at Camlann with the dead instead of being sent to the water with the sword." Then add an `original` fact.
6. **warlord-dorinakka.json ch3, Gawain misstated**: "whose strength rose with the morning and failed at dusk" -> "whose strength grew through the morning to its height at noon and waned after". The rest of the line repeats the Prydwen line ("no one has ever come to tell him it has"), so trim it to "...and say Dorinakka has the harder lot, because his strength never wanes and his war never ends." Add a myth fact (Malory, Le Morte d'Arthur, the Gawain and Lancelot combat in Book XX).
7. **behemoth.json ch3, Cu Chulainn**: "as the bards say Cú Chulainn would have" is unsupported, since Cu Chulainn carried a famous shield. Fix: cut the clause. Also spell the name one way: the branch has "Cú Chulainn" 2 times and "Cu Chulainn" 8 times.
8. **Tooling**: name_counts.py counts only `Honorific Name`, so hero caps are not enforced. Fix: add the nine hero names (bare) to the counter with cap 12. Gawain (14) and Bedivere (13) are over the cap today if history counts.
9. **Clustering**: six of the eight boss-file hero insertions are in Caer Sidi (Galahad x3 mentions in two files, Bedivere x4 in three files, Gawain in Bellum). After the cuts above, the dungeon has one Galahad (Silencer), one Bedivere (Apocalypse) and one Gawain (Bellum).

### P4. Broken or ungrammatical (3) and awkward (4)

1. darkness-falls.json story: "kept a Door-Book of the openings, hour by hour,, which answer to the frontier keeps" -> "kept a Door-Book of the openings, hour by hour, which answer to the frontier keeps"
2. bane-of-hope.json loremasterTopic: "[the captain]'s company broke that." (brackets make it a loremaster link that resolves to nothing) -> "the third company broke that."
3. silencer.json story run-on: see P3-2.
4. morbus.json ch3: see P2-9.
5. apocalypse.json ch5: "The company's priest, who is a priest of that Church, wrote" -> "The company's priest, who serves that Church himself, wrote"
6. apocalypse.json ch4: "The priest's company understood the remark on the way down" -> "The company understood the remark on the way down"
7. prince-asmoien.json ch3: "and the Door-Book's friar wrote their names in it as they swore it" -> "and the warden wrote their names in the Door-Book as they swore it"

### P5. Lost link (not a cut)

funus.json story: before, "Sir Geraint of Caerwent wrote that knights in harness proofed against dragonfire stood up to him longest" and "Sir Geraint of Caerwent's company found the next summer..." were the same man. Now they are "a knight of Caerwent" and "a company out of Caerwent". Fix: see P2-8.
## Samples for the owner (verbatim)

### BEST 1: warlord-dorinakka.json, chapter 3 ("The company of Caer Gothwaite came into the yard...")

**Before** (title: The Best-Ordered Charge)

> Dame Ysolde's company came into the yard while the dead knights were at drill, and Brother Cadoc wrote that it was the best-ordered charge any of them had ever faced.
>
> The knights were in a line, and their warlord was walking the line as a living sergeant would, correcting the angle of a shield here, the set of a foot there. He saw the company and did not hurry. He formed his file up. He told them the living were at the gate. Then he came at the company at their head, and the line came with him in a single wall of pale iron, every shield touching the next, and Cadoc said it was almost a pity to break it.
>
> Ysolde did not try to break it in the middle. She had been a knight of the frontier for twenty years and she knew what the Romans taught every auxiliary on the Wall: a line is rolled up from its end. She held the warlord with her best shield, Sir Cai's brother Sir Emrys, in the gate of the yard where Dorinakka had come to meet them and where he stood his ground, and she sent her knights in pairs against the last man of the file, and then the next, and the next, inward, until the warlord stood alone in the middle of his yard with his file lying around him. Her wizards she kept back against the gate wall, where the knights could not reach them; they did little else, and she did not ask them to.
>
> Dorinakka fought on alone, longer than Cadoc thought any dead thing could. When he fell he lifted his sword toward the riders' hall, as he does before every drill, and Sir Emrys, who had held him, returned the salute. Ysolde rebuked him for it on the stair, and then, Cadoc wrote, said that she would have done the same.

**After** (title: The Best-Ordered Charge)

> The company of Caer Gothwaite came into the yard while the dead knights were at drill, and its chaplain wrote that it was the best-ordered charge any of them had ever faced.
>
> The knights were in a line, and their warlord was walking the line as a living sergeant would, correcting the angle of a shield here, the set of a foot there. He saw the company and did not hurry. He formed his file up. He told them the living were at the gate. Then he came at the company at their head, and the line came with him in a single wall of pale iron, every shield touching the next, and the chaplain said it was almost a pity to break it.
>
> Their commander did not try to break it in the middle. She had been a knight of the frontier for twenty years and she knew what the Romans taught every auxiliary on the Wall: a line is rolled up from its end. She held the warlord with her best shield, Sir Cai's brother Sir Emrys, in the gate of the yard where Dorinakka had come to meet them and where he stood his ground, and she sent her knights in pairs against the last man of the file, and then the next, and the next, inward, until the warlord stood alone in the middle of his yard with his file lying around him. Her wizards she kept back against the gate wall, where the knights could not reach them; they did little else, and she did not ask them to.
>
> Dorinakka fought on alone, longer than the chaplain thought any dead thing could. When he fell he lifted his sword toward the riders' hall, as he does before every drill, and Sir Emrys, who had held him, returned the salute. The commander rebuked him for it on the stair, and then, the chaplain wrote, said that she would have done the same.

**Verdict:** The cleanest swap on the branch. Every beat is kept: the drill, Sir Emrys, the Wall tactic, the rebuke on the stair. The commander keeps her twenty years on the frontier and her half-smiling reversal, so the scene's personality survives losing the name. The only flaw is outside the chapter: this file calls her "commander" while the Apocalypse/Host files call the same woman "captain" (P0).

### BEST 2: lurfos-the-herald.json, chapter 4 ("A knight of the Isle of Glass killed Lurfos...")

**Before** (title: Announced)

> Sir Cador of the Isle of Glass killed Lurfos on his second descent, and he began by answering the question.
>
> Cador had come up that road once before, with twenty, and been thrown out of the queen's hall, and he had come back with fifty and with Wenna's drawing of Aelfnoth's staff. When the great voice called out from the narrows to know who came, Cador stopped the company and called back in the old form, as Wenna had taught him from the carvings: his name, his father's name, his hall, and his realm. The ghosts of the road say it was the first time since the taking that anyone coming up the road had answered properly. Lurfos cried the name down to the queen. Then he cried the other word.
>
> Wenna stood behind the knights and watched the blade. When it burned she called fire, and the company's priests laid their wards against fire on the men in front; when it went white she called frost, and they changed them. Cador kept the company apart from the knight who held the crier, a long stride from his shoulder, so that what spilled off the blade fell on one man and not on five. Wenna wrote that she had never had a more useful hour in her life, and that she spent it saying two words.
>
> Lurfos died at Aelfnoth's post, and his last words were the ones the ghosts say every crier of the road hoped to say at the end of a long life: announced, at last. Wenna took the cord from his belt. It is in the readers' store at Caer Gothwaite now, coiled in a box, and the readers have never counted the knots. The last knot on it, Wenna wrote, is new, and tight, and she knows whose it is. The ogres say Lurfos was back at the narrows within the season, with a new cord, and that the first knot on it was Cador's again.

**After** (title: Announced)

> A knight of the Isle of Glass killed Lurfos on his second descent, and he began by answering the question.
>
> The commander had come up that road once before, with twenty, and been thrown out of the queen's hall, and he had come back with fifty and with Wenna's drawing of Aelfnoth's staff. When the great voice called out from the narrows to know who came, the commander stopped the company and called back in the old form, as Wenna had taught him from the carvings: his name, his father's name, his hall, and his realm. The old songs of Albion give the same courtesy to Sir Gawain, who answered the Green Knight's challenge by name and kept his bargain to the last stroke, and the knights of Caer Gothwaite say that is the answer the commander had in mind: a name given freely is a debt the giver does not run from. The ghosts of the road say it was the first time since the taking that anyone coming up the road had answered properly. Lurfos cried the name down to the queen. Then he cried the other word.
>
> Wenna stood behind the knights and watched the blade. When it burned she called fire, and the company's priests laid their wards against fire on the men in front; when it went white she called frost, and they changed them. The commander kept the company apart from the knight who held the crier, a long stride from his shoulder, so that what spilled off the blade fell on one man and not on five. Wenna wrote that she had never had a more useful hour in her life, and that she spent it saying two words.
>
> Lurfos died at Aelfnoth's post, and his last words were the ones the ghosts say every crier of the road hoped to say at the end of a long life: announced, at last. Wenna took the cord from his belt. It is in the readers' store at Caer Gothwaite now, coiled in a box, and the readers have never counted the knots. The last knot on it, Wenna wrote, is new, and tight, and she knows whose it is. The ogres say Lurfos was back at the narrows within the season, with a new cord, and that the first knot on it was the commander's again.

**Verdict:** The only hero insertion that grows out of the plot: a story about answering a challenge in the old form, set beside Gawain answering the Green Knight by name. It is told as what "the old songs" and "the knights of Caer Gothwaite" say, never as a presence. It loses marks for opening with "A knight" and then calling him "the commander" (one man, two labels), and for the irony that the man who gives his name now has none. Apply P2-5.

### WEAKEST 1: host.json, chapter 3 ("The Company in the Crypts")

**Before** (title: Ysolde's Company in the Crypts)

> Dame Ysolde's company met the Host in the deep crypts after the warden's court, and Brother Cadoc wrote the shortest account of the climb about it: they found him by elimination.
>
> The eight came out of their niches together, in step, and the household came with them. Ysolde had been told by Ennyn that there was no tell, and she had believed him, which Cadoc thought was the wisest thing she did that day. She did not let her wizards guess. She put a shield about her priests against the household and sent her knights out to the eight in small parties, a party to each body, with orders to mark each one with a wound and then hold it, and she watched.
>
> It took them most of an hour. The bodies fought like the men they had been, a shepherd's body with a shepherd's reach and a soldier's body with a soldier's guard, and the household pressed the priests the whole time. Twice a party brought its body down and nothing else happened, and the body lay where it fell, and Ysolde sent that party to the next. The third time a party brought its body down, all seven of the rest dropped where they stood, in the middle of their strokes, and the household broke and ran into the crypts.
>
> Cadoc wrote that it was a magistrate's son's body, by its rings, and that it had a face that did not belong to it, which he did not describe. Sir Geraint of Caerwent, who had brought it down with his party, said afterwards that there was nothing to choose between it and the others until it fell, and that anyone who claimed otherwise was a liar or a minstrel. Ysolde had the company count the bodies as they lay, all eight, before she let anyone move on. The garrison of Caer Gothwaite says the count over every fallen company in the deep crypts since: count the bodies as they fall, not before.

**After** (title: The Company in the Crypts)

> The company that ended the end of the world met the Host in the deep crypts after the warden's court, and a friar of Caer Gothwaite wrote the shortest account of the climb about it: they found him by elimination.
>
> The eight came out of their niches together, in step, and the household came with them. The captain had been told by Ennyn that there was no tell, and she had believed him, which the friar thought was the wisest thing she did that day. She did not let her wizards guess. She put a shield about her priests against the household and sent her knights out to the eight in small parties, a party to each body, with orders to mark each one with a wound and then hold it, and she watched.
>
> It took them most of an hour. The bodies fought like the men they had been, a shepherd's body with a shepherd's reach and a soldier's body with a soldier's guard, and the household pressed the priests the whole time. Twice a party brought its body down and nothing else happened, and the body lay where it fell, and the captain sent that party to the next. The third time a party brought its body down, all seven of the rest dropped where they stood, in the middle of their strokes, and the household broke and ran into the crypts.
>
> The friar wrote that it was a magistrate's son's body, by its rings, and that it had a face that did not belong to it, which he did not describe. A knight of Caerwent, who had brought it down with his party, said afterwards that there was nothing to choose between it and the others until it fell, and that anyone who claimed otherwise was a liar or a minstrel. The captain had the company count the bodies as they lay, all eight, before she let anyone move on. The priests of Caer Gothwaite say the lesson is older than the gaol: in the songs of the Grail, Sir Galahad took the one seat at the Round Table that destroyed any man not meant for it, and was not harmed, because he never looked for a sign that he was the right one. The right body does not show it, they say, and the count is the only honest way to find it. The garrison of Caer Gothwaite says the count over every fallen company in the deep crypts since: count the bodies as they fall, not before.

**Verdict:** The swap is fine, but the added Galahad passage is wrong on three counts. It contradicts Malory and the branch's own history/albion.json (the Siege Perilous does announce Galahad's name). It calls the Grail songs "older than the gaol" when Arthur climbed the gaol before Galahad's time. And it repeats the Siege Perilous anecdote the Silencer file also uses. "The company that ended the end of the world" is also an odd label for a company still on its way up. Cut the two sentences (P3-1).

### WEAKEST 2: funus.json, chapter 3 ("A funeral is not held just anywhere...")

**Before** (title: His Own Ground)

> A funeral is not held just anywhere, and Funus will not be laid to rest away from his own ground.
>
> Roman law gave every town its road of tombs and every family its plot along it, and a man buried in a stranger's ground was thought to wander. The keepers of Caer Sidi gave Funus the floor of the riders' hall as his ground, the last stretch of the last road, and the Inconnu say nothing can lay him to rest anywhere else. The dead of the lich's company found it out on the night of the rising. They drove him back across his hall and out toward the stair, and on the stair their blows went into him like spades into a dry pond, and he turned and walked back to his floor, unhurried, and they had to follow him there.
>
> The Church keeps a rule for the office of burial that the priests of Caer Gothwaite say is older than the Church in Albion. If the office is broken off, for a storm or a quarrel or a war party on the road, it is not taken up where it stopped. It is begun again from the first word, because a rite that has paused is a rite that has not been said. Brother Cadoc, who has said the office over more of the dead of Caer Sidi than any priest of the realm, wrote that Funus keeps the same rule. A company that lets the fighting go slack, even for the space of a few psalms, finds the rider whole again and the rite to begin from the first word.
>
> Ysolde's company did not let it go slack. It fought Funus where he stood, on his own ground, and kept at him, and Cadoc said the office over him as he fell, the whole of it, without a pause, because he did not know what else to say over a funeral. He thought afterwards that the rider had listened.
>
> When Funus fell, there was no grace. A voice came off every wall of the hall at once and thanked the company, quite warmly, for besting its harbingers, and before Cadoc understood that the voice meant the four riders, the door at the end of the hall was open.

**After** (title: His Own Ground)

> A funeral is not held just anywhere, and Funus will not be laid to rest away from his own ground.
>
> Roman law gave every town its road of tombs and every family its plot along it, and a man buried in a stranger's ground was thought to wander. The keepers of Caer Sidi gave Funus the floor of the riders' hall as his ground, the last stretch of the last road, and the Inconnu say nothing can lay him to rest anywhere else. The dead of the lich's company found it out on the night of the rising. They drove him back across his hall and out toward the stair, and on the stair their blows went into him like spades into a dry pond, and he turned and walked back to his floor, unhurried, and they had to follow him there.
>
> The Church keeps a rule for the office of burial that the priests of Caer Gothwaite say is older than the Church in Albion. If the office is broken off, for a storm or a quarrel or a war party on the road, it is not taken up where it stopped. It is begun again from the first word, because a rite that has paused is a rite that has not been said. A friar of Caer Gothwaite, who has said the office over more of the dead of Caer Sidi than any priest of the realm, wrote that Funus keeps the same rule. A company that lets the fighting go slack, even for the space of a few psalms, finds the rider whole again and the rite to begin from the first word.
>
> That company did not let it go slack. It fought Funus where he stood, on his own ground, and kept at him, and the friar said the office over him as he fell, the whole of it, without a pause, because he did not know what else to say over a funeral. The friars of Caer Gothwaite say the old songs give the same helplessness to Sir Bedivere, who stood on the shore when the barge took the wounded Arthur out of sight and found that he had no office to say over a king who might not be dead. He thought afterwards that the rider had listened.
>
> When Funus fell, there was no grace. A voice came off every wall of the hall at once and thanked the company, quite warmly, for besting its harbingers, and before the friar understood that the voice meant the four riders, the door at the end of the hall was open.

**Verdict:** Two continuity breaks in four paragraphs. "That company did not let it go slack" now follows a hypothetical "A company that lets the fighting go slack", so it reads as the same careless company. The Bedivere sentence sits between the friar's office and "He thought afterwards that the rider had listened", so "He" now means Bedivere. The Bedivere comparison ("the same helplessness") also does not match a friar who did have an office to say. Apply P1-7, P1-8 and P3-4.
