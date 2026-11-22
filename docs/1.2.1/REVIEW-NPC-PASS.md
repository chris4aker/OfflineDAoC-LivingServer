# Review: the NPC pass (HEROES.md decisions 10 and 11)

The reviewer is independent and wrote none of these passages. Eleven writer commits were reviewed on `wt9/npc-all`,
`7b037dfb..eced82c7`:
- Albion: `017c40ff`, `15253e11`
- Midgard: `6d9a8b67`, `99546975`, `dad9abba`
- Hibernia: `6416316b`, `4c4e8c73`, `539367fb`, `3f12e6db`
- Neutral: `fba22cf9`
- the merge: `eced82c7`

The fixes are in `2af8c74c` and `996b9044`, and in the commit that adds this file. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`. Tests only: nothing here has been seen in game.

Severity:
- **High:** breaks an owner rule, or makes a fact false.
- **Medium:** a contradiction a player would notice, or a moment that does not work.
- **Low:** prose, repetition, a slip.
- **Info:** checked, and fine.

## Verdict: pass after fixes

Most of the new moments are real moments, not name swaps. The best of them change how a scene reads:
- Hurjavelen's dagger sold twice to Morlin Caan;
- Lady Fridwulf's jug of holy water, empty by Sherborne;
- Daray's jar of sap on the teaching stone;
- Lasairiona's champion who "was never afraid of anything in her life", and whom she has not sent down again;
- Sven stopping one verse short every longest night.

Nothing old was cut outside the Avalon City and Krondon summaries. A word-subsequence check of every visible string
against `7b037dfb`, with the renames mapped, finds only the stand-ins a name replaced, "Brother" before Esras, the
Market Explorer words, and the three renamed topic keys.

The review fixed these:
- **One false game fact.** Orlaith is the real Necht NPC, so she keeps her name.
- **One new name clash.** Jofrid is a real NPC.
- **Two contradictions.** Lasairiona's road skipped Nock, and Grimma's hall disagreed with the trainers' topic.
- **Five bolted-on or bare swaps.** They were reworked or undone.
- **Five sentence slips.**
- **The owner's five rulings** of 2026-11-22.

| Realm | High | Medium | Low | Info |
|---|---|---|---|---|
| Albion | 0 | 2 | 6 | 3 |
| Midgard | 0 | 1 | 3 | 3 |
| Hibernia | 1 | 1 | 2 | 3 |
| Neutral | 1 | 1 | 2 | 1 |
| Across realms | 0 | 2 | 1 | 2 |

## Albion (`017c40ff`, `15253e11`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| A1 | `bosses/bellum` ch. 4 | Medium | "Captain Bonswell has every rider of the keep touch the letters on the way out" repeated the old text two sentences later ("The soldiers touch it going out"). It was bolted on, and it added a third touch-for-luck habit beside Morlin Caan's doorpost and Captain Circa's ram. | Bonswell now "has the letters cut fresh every spring, before the frost can wear them soft". The fact now says so. |
| A2 | `bosses/soul-reckoner` ch. 1 | Medium | "The Church of Camelot, in Lady Fridwulf's reading, says" was a bare name swap that spent one of her 12 mentions. | Undone (it is the old text again). The file's Fridwulf fact is removed. |
| A3 | `bosses/fames` ch. 4 | Low | Only a role line ("who teaches the realm's young clerics their scripture"); no moment. | Added: "she has her clerics say the rider's cry aloud, a measure of wheat for a penny, until they hear the famine in it." |
| A4 | `bosses/host` ch. 4 | Low | "Father Kenelm first among them" repeated Caer Sidi's "Father Kenelm the loudest of them": the same device, and a bare swap. | The old sentence is back, and after it: "In Father Kenelm's chapel the word is said of nothing but the bread." |
| A5 | `locations/campacorentin-forest` story | Low | The inserted clause left "with a tall house of platforms" hanging on Grundelth instead of the society. | The old sentence is whole again, and Grundelth's moment is its own sentence after it ("once said the reverse from one of those platforms at midsummer, and was not asked back"). |
| A6 | `bosses/ellyll-seer` story | Low | A run-on with "and ... and ... and". | Split: "a space for it has stood empty on the shelf behind his table since the first courier's papers came in. Sir Tilian ..." |
| A7 | `locations/avalon-city` story (rewritten summary) | Low | Two slips in an otherwise better rewrite. "Elowen keeps the colleges in their founding order; on the first night the flamethrowers kept it too" has no "it". "After the five burned the cloister" reads as though the five colleges burned the cloister. | "Elowen keeps the order the colleges were founded in, and on the first night..." and "After the five went the cloister". 497 words. Every event is kept (checked one by one: the colleges in order, Rhydderch, Angharad, the bed, Glastonbury, the cold spring, Ifor, nine chests, Krondon's books, the four warnings, Maelon, Sefin, the list). The rewrite is better than the old text: the bed paragraph no longer repeats its chapter, and the closing "in the order the colleges burned" pays off the opening. |
| A8 | `locations/krondon` story, fourth paragraph | Low (owner) | Flat (owner: "Just make it the best possible"). | Rewritten, with every event kept. The ogres were Avalon's allies before the drakoran came and were not yet masters of the hall. Avalonians married among them, and the half ogres in Albion's colours are those marriages' children. The break came in the confusion of the drakoran invasion, each side tells it differently, and Albion has been the enemy ever since. 499 words. |
| A9 | `locations/camelot` church topic | Low | "and the names of the realm's dead" added a sixth "keeps the names of the dead" moment (see X1). | Dropped. The topic says she teaches the young clerics their scripture. |
| A10 | Lady Fridwulf in `locations/darkness-falls` ch. 8 (Neutral) | Low | "Lady Fridwulf of the Church of Camelot calls it the Mouth" made one person Albion's answer, while Midgard's and Hibernia's answers stay with institutions. It broke the three-way parallel and was a bare swap. | Undone ("The Church of Camelot calls it the Mouth"). The fact is updated. |
| A11 | Hedydd, Tilian, Meirion, housing | Info | Hedydd is "she" in every file (`crystal-cave`, `wenoiak-the-enlightened`, `veraerius-the-brave`). Tilian has one spelling. Rhodri on Dame Nest's roll is Meirion, a name used nowhere else. The housing's market crier is in-world. | None. |
| A12 | Father Kenelm, 8 files | Info | He is consistent everywhere: the Caer Gothwaite priest who mistrusts the Inconnu, blesses the last curve, keeps Corwen's page (he would burn it only if the Academy said what it meant), says the first mass for the thirteen, and has his clerics read Job before Darkness Falls. | None. |
| A13 | Captain Rhodri, Leri, Elvar Ironhand, Brother Michel | Info | Rhodri is the Lord of Deceit giver in all six files. Leri is "she", with her lake-wall watch matching her night lamp in `snowdonia`. | None. |

## Midgard (`6d9a8b67`, `99546975`, `dad9abba`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `locations/jordheim` | Medium | Grimma's home. The story (old text) puts Ymir's shamans in the eastern hall, and the pass placed Grimma there. The trainers' topic (old text) puts every pair, "Grimma and Dane" included, in the rows by the southern gates. | Added to the topic: "The eastern houses teach in their own hall a street off, and walk their young down to the gates to leave with the rest." This also explains why Grimma reads Thorkatla's omens at the southern gates. |
| M2 | `locations/dodens-gruva` ch. 8 | Low | Sven's clause was inserted mid-prophecy, so "and the picks will fall silent" read as part of his verse-making. | The prophecy is whole again. Then: "The skald has the verse made, with every name of the shift in it, and has never yet sung it, because that morning has not come." |
| M3 | `bosses/thorkatla` story | Low | "the seers of Jordheim say" was cut without a name replacing it. | Restored. Grimma's sentence stays. |
| M4 | Skeggi (`vendo-caverns`), the sentry Steinar (`hurjavelen`) | Low (owner) | One-file witnesses with gamebot names. | Renamed **Tjorvi** and **Kolli**, each with a fact. Neither name is in a gamebot list, `Hire165.cs`, `legend_names`, the roster or the lore. Ragnfrid's father Skeggi (`fostra-orm`), a dead man of another age, keeps his name. |
| M5 | Morlin Caan, 6 files | Info | His forge is by the temple everywhere. The old legend's "a smith near the middle" agrees. | None. |
| M6 | Grimma, 5 files; Miri, Kalbin, Aud | Info | Grimma reads the omens and keeps the tally of the dead. Miri and Aud were already real NPCs in their files. | None. |
| M7 | Renames | Info | Svanlaug (the loremaster, in `Loremasters.cs`, `UT_LoreBible.cs` and `docs/LORE.md`), Geirlaug, Oddbjorg, Thurid, Vilborg, Tofa and Asvor are gone from every old spelling. The code change is a string literal in both files; it compiles as before. | None. |

## Hibernia (`6416316b`, `4c4e8c73`, `539367fb`, `3f12e6db`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | Orlaith, renamed Uallach in 16 files | **High** | Orlaith is a real 1.65 NPC in Necht (`npc_roster.json`: region 181, Vigilant Rock). The lore's leader "lives in Necht now" and wrote "What the Rock Watches, by Orlaith of Necht". The rename made the `game` fact in `vigilant-rock` false ("A Necht NPC named Uallach (40) gives the Block of Sanidon Sand"). It also cut the lore off from the NPC a player can meet. The Hibernia and Midgard writers' own rule kept real NPCs (Eoghan of Lough Derg, Vigdis, Elin); Orlaith was missed. | Reverted everywhere: the lore, `tales.lines`, `corpus.json`, `DEBATES.md` and the vocabulary. The ledger entry is now `orlaith-hundred-and-forty`. The rule is in HEROES.md decision 11. |
| H2 | `bosses/faerie-queen` story | Medium | "The trainers' road is shorter and starts at an anvil ... then straight to the court" contradicted `lough-derg`, where Lasairiona wants Nock's head "for the first step of the moonstone's road". | "...then to Nock on the Lough Derg shore, and only then to the court for the first half." |
| H3 | `locations/tur-suil` ch. 7 | Low | Daray "sets it to the young druids every year as a question, as the grove's teacher answers every question, with an older one under it" repeated `balor`, where Daray asks each new class which came first, the king or the eye. It was the same subject and the same device, and the wording was clumsy. | Undone (the old text is back). The fact is removed. Daray is at 10. |
| H4 | `bosses/corrupt-primal-of-fire` story | Low | "...never regrind it. It leaves its footsteps" made "It" point at the blade, and "never regrind" had no reason. | "...because a new edge on that steel goes soft by the next going-down. The guardian leaves its footsteps..." |
| H5 | Daray, 8 files | Info | No pronoun anywhere, as the capture leaves Daray's sex unset. Daray always doubts and tests. The quarrels with Nóra (a keener's daughter, as CAST-HIBERNIA has her) and Lisín read true. | None. |
| H6 | `bosses/tine` | Info | Lasairiona's champion is one of the Bri Leith company's two survivors, not Ruarc. That fits. | None. |
| H7 | Esras, the market crier, the other renames | Info | No churchman is left in Hibernia. Flann, Cuán, Ruarc, Faílbe Rua, Dubhán, Cerball, Muiredach, Cellach and Ségán clash with nothing. | None. |

## Neutral (`fba22cf9`, `eced82c7`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| N1 | Jofrid the Lame (`locations/darkness-falls`, `bosses/princess-nahemah`) | **High** | The new name clashes with a real NPC: Jofrid Spioner, the rogue trainer of Aegir's Landing (`npc_roster.json`). | Renamed **Thorhild** the Lame, in both files, the ledger and the docs. The name is in no list and nowhere else in the lore. |
| N2 | Sven, in `bosses/grand-summoner-govannon` and `locations/darkness-falls` | Medium | The two realms wrote two different men. Midgard's Sven "puts only the dead in his songs". The Neutral Sven kept the Summoner's name out "because a mask has no name to sing", and sang Thorhild's page "with every name of the dead in it and never hers", with no reason given. | One rule, applied everywhere: "he puts only the dead in his songs, and a mask never lived to die"; "and never hers, because she is still living". Legion's "because Hrolf is still living" already agreed. |
| N3 | `locations/darkness-falls` ch. 5 | Low | A second Sven mention in the same file, singing at the southern gates "every name in it". It repeated the book page's moment. | Undone. The old gapped sentence ("and Hibernia's bards a lament for theirs") is back. |
| N4 | `bosses/chamberlain` ch. 4 | Low | "Etho ... says he can tell you how many" made the Chamberlain's tally Etho's. | "says the Chamberlain can tell you ...; Etho will not sell that number for less than three candle-ends." |
| N5 | Captain Cody (`locations/thidranki`) | Info (owner) | Cody is off the gamebot list now. | Hibernia's take now has a real moment for him: "walks each new band past the eldest's corner bareheaded, so they see how it is done." It has a fact with the roster and Herald source. |

## Across the realms

| # | Severity | Finding | Change |
|---|---|---|---|
| X1 | Medium | **One note played too often.** Before the review, about twelve of the new moments were someone keeping, reading or singing the names or tally of the dead: Sven (five), Grimma (two), Lady Fridwulf (Lent, the Camelot topic), Kenelm's masses, and Lasairiona (the cursed wood, Legion). Each is fine alone; together they flatten the realms into one habit. | Three are cut or turned: N3, A9, and Legion's Lasairiona, who now asks which of Ealadhach's two descents a champion would have gone on, without reading out names. Sven keeps the note, because it is his rule. |
| X2 | Medium | **Real NPCs and the rename rule.** H1 and N1 show the rule needs a limit. | HEROES.md decision 11 now says: a real NPC keeps their name although a gamebot can share it, and a new name must not be a real NPC's. |
| X3 | Low | Three touch-for-luck habits from the pass: Bonswell, Circa and Morlin Caan. | Bonswell's is changed (A1). Circa counts the hands at dusk, which is the point of his habit. Morlin's doorpost mirrors the morvalt in the same sentence, by design. |
| X4 | Info | **Name totals** (visible text, bare name, after the fixes): see the table below. | - |
| X5 | Info | **Other gamebot names still in the lore, left for the owner.** Albion's Old English characters share `EraBotNames` lore-list names: Wynflaed (31 mentions, 9 files, Krondon's ghost), Wystan (16), Leofric (16), Eadric (9) and Osric (23; Lord Osric is also a real NPC). The Albion brief had no rename task, and these are dead or present-day story people, not tellers. Also left: Lydsyg's Ulfhild (dead), Hrafn, Ketil, Orm, Jorunn, Sigrid and Thordis (Midgard's dead and story people), and a second Hrolf, "the Counter", in `duke-satori` beside Legion's Hrolf the Tall. | None; for the owner. |

| Voice | Mentions | Files | Notes |
|---|---|---|---|
| Sven (untitled, exempt) | 14 | 13 | Was 15. Each mention is in a different file and does something different (the horn song, the moon-chaser, the drone, the shift's verse, the Troll Father's payments, Geirlaug's last verse, the Summoner, Hrolf's roll, Thorhild). It does not read as repetitive across a realm, because a player meets him once per place. The weak point was the shared opening "Sven of Jordheim sings that...", which is now broken up in places (`dodens-gruva`, `jordheim`). Keep him at about this level. |
| Lady Fridwulf (titled, cap 12) | 10 | 10 | Was 12 (A2, A10). |
| Captain Rhodri (titled) | 10 | 6 | |
| Daray | 10 | 8 | Was 11 (H3). |
| Father Kenelm | 9 | 6 | |
| Morlin Caan | 7 | 6 | |
| Lasairiona | 7 | 6 | |
| Grimma | 6 | 5 | |
| Master Grundelth | 5 | 4 | |
| Ilisa | 4 | 4 | |
| Captain Cody | 1 | 1 | New (owner). |

## Roster and facts

Every named present-day NPC is in `tools/lore/npc_roster.json` with a source. Every file that names one has an
`original` fact naming them and the roster source. The script check finds no file without one, except those where the
NPC was already in the file (Brother Michel in `camelot`).

Pico is not in the roster. He is the lore's own imp, already in three files before the pass, and his facts cite
`locations/darkness-falls`. The renamed tellers have no per-file rename fact. The NPC-PASS docs and the ledger record
them, which is enough, because a fact's text is never shown.

## Five best moments (after the review)

1. `bosses/hurjavelen`: the dagger sold twice to Morlin Caan, "and both times it was back on the wolf within the
   season; he will not buy it a third time."
2. `bosses/tine`: Daray "thinks that is fear talking. Lasairiona, who trained one of the two, says that champion was
   never afraid of anything in her life, and has not sent her down again."
3. `locations/albion-housing`: Lady Fridwulf "walked the bounds of every lot with a cleric's bell and a jug of holy
   water, and the jug was empty by Sherborne".
4. `bosses/gjalpinulva`: Sven "sings it on the longest night and stops one verse short, every year, because Hallbjorn
   did not hear the last verse either."
5. `bosses/skeletal-sacristan`: "'And if he were a Christian sacristan,' she tells her clerics, 'he would be a saint,
   and I would light his candles myself.'"

## Tools

All checks were run after the fixes on this branch (tests only):

| Check | Result |
|---|---|
| `depth_report.py` | 0 below |
| `schema_mirror.py` | 0 problems |
| `lint_lore.py` | 0 problems, 0 game-term hits |
| `name_counts.py --check` | 0 over cap |
| `continuity.py --check` | 47 facts, 0 contradictions (`CONTINUITY.md` regenerated) |
| `repetition.py --check` | 0 problems |
| `reachability.py --check` | pass |
| `tools/lore/tests` | 62 OK |
| `mechanic_trigger_coverage.py --check` | 15 of 15 fire |
| `build_corpus.py --check`, `--verify` | pass, the corpus matches its sources |
| `debates_doc.py --check` | up to date |
| `check_runtime_names.py` | ok |
| `epic_encounter_catalog.py --check`, `build_encounter_adds.py --check` | pass |
| `tools/bot-voices/tests` | 44 OK |
| `source/tools/tests` | 294 passed, 1 skipped |

`build_corpus --write-vocabulary` was not run. `uallach` was taken out of the vocabulary by hand, and `orlaith` was
already in it. There is no dotnet here. The only code changes are the two `Loremaster Svanlaug` string literals in
`Loremasters.cs` and `UT_LoreBible.cs`; they were read and compile as before.

## For the owner

- **Orlaith** (H1). Is the revert right? The rule as written ("a teller who has [a gamebot name] is renamed") would
  rename her. But she is the real Necht NPC, and the renamed lore no longer described the person a player can talk
  to. This review kept her name.
- **Albion's Old English names** (X5): rename Wynflaed, Wystan, Leofric and Eadric too, or leave the dead and the
  story people alone, as Midgard did?
- **Sven at 14** across 13 files is fine as he stands. No further Sven scenes are recommended without retiring one.
