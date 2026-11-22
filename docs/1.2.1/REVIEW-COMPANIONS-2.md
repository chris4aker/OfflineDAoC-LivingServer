# Review: decision 5, second pass (the legend companions)

The reviewer is independent and did not write these passages. Three commits were reviewed on `wt2/lore-review`:
- `8a616acd`: Albion
- `348594f0`: Midgard
- `7b417b34`: Hibernia

The fixes are in `247c5b7a`. Paths are relative to `source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **verified** (checked against a translation or a reliable summary online), **known** (checked from the
reviewer's knowledge of the text, not looked up this time), **unverified**.

Severity:
- **High:** breaks an owner rule.
- **Medium:** a myth error, or a contradiction a player would notice.
- **Low:** prose, repetition, citation detail.
- **Info:** checked, and fine.

## Verdict: pass after fixes

The three writers did careful work:
- Every `myth` fact the brief flagged is in its source, give or take a detail. Three details were wrong or
  incomplete and are now corrected. Nothing needed relabelling from `myth` to `original`.
- Nothing was cut. A word-subsequence check of every old string against the new files found no lost text. Midgard
  respelled the existing "Bjorn" and "Snake-Eye" as "Björn" and "Snake-in-the-Eye".
- No hero stands in a present-day scene. Arthur stays dead (the Avalon bed waits for a barge that never came).
- There is nothing after 1.65 and no developer vocabulary.
- Every name is at 12 mentions or fewer. Lancelot is at 11, Starkad and Conall Cernach at 9, Tristan at 8 and
  Deirdre at 7.

The one owner-rule breach was older than this round: Saint Patrick in `history/hibernia.json`. The biggest shared
weakness was the same closing device in all three realms (see R1).

| Realm | High | Medium | Low | Info |
|---|---|---|---|---|
| Albion | 0 | 3 | 5 | 3 |
| Midgard | 0 | 1 | 3 | 4 |
| Hibernia | 1 | 1 | 4 | 6 |
| Across realms | 0 | 0 | 1 | 1 |

## Albion (`8a616acd`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| A1 | `bosses/morgana.json` story | Medium | The story gave Accolon only the sword. Then it said "Accolon bled at none", which only the scabbard explains, and it had Morgana ride off with the scabbard straight after the fight. In Malory, book 4 (**known**), Accolon has both. Arthur wins them back, and Morgan steals the scabbard from his bedside at the abbey while he sleeps. The damsel of the lake makes the sword fall by enchantment; she does not strike it. | Story and myth fact corrected, and the abbey theft added. "Found asleep" had no subject; it is now "she and three other queens found him asleep". |
| A2 | `bosses/morgana.json` | Medium | "The realm keeps a scabbard at Castle Excalibur" is stated as fact, while `forest-sauvage.json` hedges ("the riders say") and Malory has the scabbard thrown into a lake. | Now "The realm says it keeps a scabbard... and calls it the true one". The three files now agree: the game's relic, the realm's claim, the Academy's doubt. |
| A3 | `locations/camelot.json` story | Medium | "One seat... stood empty for years, until the Pentecost when the Grail went through the hall". This reads as if the Siege Perilous stayed empty. `history/albion.json` and `host.json` have Galahad sit it at that Pentecost. | Lancelot's son now sits it unharmed at that feast, unnamed, so the Galahad count stays at 10. |
| A4 | `locations/cornwall.json` | Low | Name confusion: "Isolde" is Dame Ysolde's name (the present-day Caer Sidi captain, 7 mentions) and a hired-companion name in `Hire165.cs`. | Now "Isoud the Fair", Malory's own form. The myth facts already say La Beale Isoud. |
| A5 | `locations/cornwall.json`, `camelot.json`, `history/albion.json` | Low | Citations. | Book numbers 1, 3, 4, 6, 8 and 21 hold for the facts they are on (**known**; book 17's 24 days **verified** in the book 17, chapter 16 heading). The book 17 fact is about Corbenic and the three achievers, which is right; the Grail Pentecost itself is book 13, and no fact cites it. Marhaus's seat is book 10, chapter 6 (**verified**: "This is the siege of the noble knight, Sir Tristram"); sources now cite it. Mark killing Tristram was "recalled in the closing books" and is now book 19, chapter 11 (**verified**: "that traitor king slew the noble knight Sir Tristram, as he sat harping afore his lady La Beale Isoud"). |
| A6 | `locations/lyonesse.json`, `camelot.json`, `cornwall.json`, `avalon-marsh.json` | Low | "The songs say" three times and "the knights' songs" four times. | Reworded: "which is as much as to say a sorrowful birth" (Malory's phrase), "the minstrels sing", "the harpers sing", "Camelot's minstrels say". |
| A7 | `locations/avalon-city.json` | Low | "They wrote only that they had not been asked" is opaque. | Now "that the king had named Avalon, and that a college keeps faith with a king's word whether anyone asks it to or not". |
| A8 | `locations/camelot.json`, `history/albion.json` | Low | "Percival out of the Welsh woods" follows Chrétien and *Peredur*. Malory's Percival is King Pellinore's son. The passage is `original` framing and matches Caer Sidi's Peredur. | No change. |
| A9 | `locations/cornwall.json` | Info, owner | Name confusion: `dartmoor.json` has Duke Marcus of Cornwall (a Herald name) in Uther's day, and his son came home alone from Dartmoor. King Mark is now king of Cornwall "a generation later". A reader may take Mark for the duke's son. Nothing says so and nothing denies it. | No change; listed for the owner. |
| A10 | `shade-of-uther-pendragon.json`, `avalon-marsh.json`, `avalon-city.json` | Info | The postern gate, cloth of gold, Ector and Kay, "within two years", the sword in the stone and Kay's oath on a book are all book 1. So are Pellinore at the well, the arm in white samite, and "the scabbard is worth ten of the sword". The three queens in black hoods are book 21 (**known**). Lancelot raised by the Lady after his mother leaves him by the lake for the dying Ban is in the Prose Lancelot (**known**). | None. |
| A11 | `lyonesse.json`, `cornwall.json` | Info | Tristram's birth in the forest and the name "sorrowful birth"; Marhaus on the island, the piece of sword, the healing by Isoud, the drink; Marhaus as the Irish queen's brother and a knight of the Round Table. All book 8 (**known**). | None. |

## Midgard (`348594f0`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `locations/east-svealand.json` | Medium | In the story Ivar's arrows end Sibilja. In the saga (**verified**) she comes on blinded; Ivar has himself thrown onto her, makes himself "as heavy as a boulder" and breaks every bone in her body. | Story and myth fact elaborated. The saga also has Sigurd Snake-in-the-Eye, aged three, going on this vengeance (**verified**, summary). That is now told, which also explains why he is in the list. |
| M2 | `locations/jordheim.json` | Low | "And will not say which of the songs is the oldest" repeats a tic the brief named. Jordheim is a "keep" file. | Now "have never settled among themselves which...". "What the skalds sing" in the intro is now "what the skalds make". |
| M3 | `cursed-tomb.json`, `east-svealand.json` topics | Low | The same epithet appears in Hibernia: "The warrior of the old songs", "a king of the Svear in the old songs". | Now "A warrior of giant stock, in the skalds' songs" and "in the saga of Ragnar's sons". |
| M4 | `history/midgard.json` | Low | The ox-hide story leaves out that the brothers first attacked Aella without Ivar and lost. The saga sets Ivar's stronghold at London (*Tale of Ragnar's Sons*: York). Neither is contradicted, since the story names no city. | No change. The story still parses ("when his brothers came with their army"). |
| M5 | `history/midgard.json` | Info | The sons' reactions (**verified**): Björn's hand-print on the spear shaft, Hvitserk's tafl piece and blood from under the nails, Sigurd cutting to the bone, Ivar changing colour, the ox-hide, the eagle. | None. |
| M6 | `cursed-tomb.json`, `history/midgard.json` | Info | Starkad: Odin's three lifetimes and victory, and Thor's nithing's deed in each (**verified**). Vikar chosen by lot and hanged with calf's guts and a reed that become rope and spear (**verified**: *Gautreks saga*; Saxo uses a willow withy). Olo, whom Starkad served, killed for gold and repented; Hather, son of the slain Lennius, paid with that gold; the head bites the ground (**verified**). | None. |
| M7 | `gotar.json`, `mahattava.json`, `varulvhamn.json`, `malmohus.json` | Info | *Beowulf*: "the most part of a day" to the bottom, the Danes going home, the melting blade, fifty winters, the dragon (**known**). Thora's serpent in a box with gold under it, and the tarred shaggy breeches (**verified**). The wolf-skins, the tenth day, seven men, eleven killed, the weasel, the raven's leaf (**known**). | None. |
| M8 | names | Info | Name confusion: the present-day Bjorn the archer (Myrkwood) and Bjorn the Younger (Dödens Gruva). The legend Björn is always "Björn Ironside" on first mention and carries the diacritic. The hired-companion name "Eirik" also matches Ragnar's son, which is harmless. | None. |

## Hibernia (`7b417b34`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | `history/hibernia.json`, Oisín fact and faction text | **High** | Older than this round. Patrick, "a churchman", "to preach", "his psalms" and "keeps no church" break the owner's rule: no Saint Patrick or churchmen in Hibernian legend. | Reworded to "a stranger out of Albion, a Briton who had crossed the water with a teaching of his own" and "the stranger's quiet hours were duller than the hunt". The ending is now "The later ballads give the stranger a name and a calling; Hibernia, which keeps neither, calls him only the stranger from over the sea". The source line now cites "the Agallamh ballads" and says Caílte is the *Acallam*'s chief teller without naming Patrick. Every event is kept. |
| H2 | `bosses/ailbe.json` | Medium | "Struck off its head on the plain." In the tale (**verified**, Meyer via gaeilge.org §19-20) the hound seizes the pole of Ailill and Medb's chariot. Their charioteer Fer Loga strikes its body away, and the head stays on the pole until it falls at Ibar Cinn Chon, the ford of the hound's head. | Story and myth fact elaborated. "Forelegs" is right: the text gives Connacht only the two fore-quarters. |
| H3 | `locations/mount-collory.json` | Low | "In one bound from the bank to the middle and a second to the far side." *Tochmarc Emire* (**verified**, CELT T301021) has three failed tries, then the salmon-leap onto the middle, and he is over the far end "before it had fully raised itself". | Reworded to the text. Nothing added beyond it. |
| H4 | `connacht.json`, `cruachan-gorge.json`, `mount-collory.json`, `emain-macha.json` | Low | "The songs say" four times, and twice in one Connacht paragraph. "The songs call" comes two sentences after "In the songs". | Now "as Ardee tells it", "it is told of him", "the hero-tales say" and "a hall called the Red Branch". |
| H5 | `cruachan-gorge.json`, `emain-macha.json` topics | Low | The epithets "The queen of the old songs" and "Deirdre of the Sorrows, of the old songs" match Midgard's. | Now "The queen of the cattle-raid tale" and "in the tales of the Red Branch". |
| H6 | `cruachan-gorge.json` | Low | The closing "...and that is why no chieftain..." (see R1). | Now "...will count the relic-house's goods aloud after dark, for fear of starting another". |
| H7 | `connacht.json` | Info | Ferdiad (**verified** in substance, Book of Leinster version): Medb's wine, promises and the poets' satire; nightly embraces; herbs sent across and food sent back; one paddock and one fire. Láeg was told to revile his master if he faltered, and sent the gáe bolga down the stream. "Half" stands for the text's "equal share", which is acceptable. | None. |
| H8 | `bosses/avartack-the-champion.json` | Info | Fergus carries Cú Chulainn's terms to the camp (**verified**, Dunn's "The Proposals"): one champion a day, and the army marches while that combat lasts. Medb binds Fergus with sureties first. | None. |
| H9 | `emain-macha.json`, `history/hibernia.json` | Info | *Longes mac nUislenn* (**known**): Cathbad's prophecy, Deirdre reared apart, Naoise and his two brothers to Alba, Fergus, Dubthach and Cormac as sureties, Eogan's spear, Fiachu's death, the burning of Emain, the year without a smile, the stone. Conall's oath to avenge before nightfall (**verified**). | None. |
| H10 | `cruachan-gorge.json` | Info | The pillow talk and Finnbennach (Book of Leinster version, as cited), and Fergus leading the army astray (**known**). The writer's line "She is not Maeve the Crone" keeps Medb apart from the present-day quest giver. | None. |
| H11 | names | Info | Name confusion: Deirdre is an arrow merchant in region 200 and a hired-companion name. Every first mention is "Deirdre of the Sorrows". | None. |
| H12 | all | Info | No churchmen in the new Hibernian text. Every hero is told about, never present-day. | None. |

## Across the three realms

- **R1 (Low): one closing device.**
  - All three writers end almost every legend insert the same way: a local group says an aphorism that explains a
    present-day custom ("...say X, and that is why Y").
  - Examples:
    - Ardee's rangers, the gorge folk, Dun Crauchon's gate and the Scathaig chieftains;
    - the Vasudheim skalds, Gotar's grandmothers, the furriers, the dwarves and the wolfaur;
    - the bogmen, the Academy, the barrow-keepers and the Constable's clerks.
  - Each one reads well alone. Read in a row, they read as a formula.
  - Changes: one ending is reworded, and three "the songs say" and three "of the old songs" are varied. "The songs
    say" is back to its count before this round (8), and "of the old songs" is down from 18 to 15. "That is why"
    went from 95 to 97 over the round.
  - The next writer should end some inserts on the deed itself.
- **R2 (Info): consistency.**
  - The same events agree across the files:
    - Tristan and Marhaus in camelot, cornwall, lyonesse and history;
    - the Ragnar's-sons news in history and jordheim;
    - Sibilja in history and east-svealand;
    - Deirdre and Fergus's exile in emain-macha, cruachan-gorge and history;
    - Ferdiad and the gáe bolga in connacht and mount-collory.
  - The scabbard now agrees across morgana, avalon-marsh and forest-sauvage. The game's relic is the Scabbard of
    Excalibur, and Castle Excalibur holding it is still marked "to verify" in `forest-sauvage.json`. Malory's scabbard
    is in a lake. The realm *says* it has the true one.
- **Voice:** Albion is court and minstrel, Midgard skald and saga, Hibernia bard and hero-tale. All three hold.
  Midgard's "the skalds sing" doubled to 10, which is in voice and was left.

## Not checked online

- Malory books 1, 3, 4, 6, 8 and 21 were checked from the reviewer's knowledge of Caxton's divisions. Gutenberg could
  not be reached from this session, and its HTML page is cut off at book 13. Books 10, 17 and 19 were confirmed online.
- The Prose Lancelot's lakeshore scene, the *Volsunga saga* wolf-skins, the *Beowulf* details and *Longes mac
  nUislenn* were not looked up this time. They agree with the reviewer's knowledge and with the canon review.

## Tools

These ran on `247c5b7a`, all exit 0:
- `tools/lore/lint_lore.py`: 0 problems.
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 8 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: the corpus matches its sources.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `tools/bot-voices/debates_doc.py --check`: up to date.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.

Nothing had to be regenerated. These passages change no `botLines`, yells or encounter data.

`UT_LoreBible` (dotnet) could not run here because dotnet is not installed. It must run in CI or on the owner's PC
before merge. All of this is tests only; nothing has been seen in game.

## For the owner

1. **Duke Marcus and King Mark (A9).** Should Cornwall's lore say whether King Mark is the son of the Herald's Duke
   Marcus, the one who came home alone from Dartmoor? Or should it keep the two apart in so many words? Right now it
   is left open.
2. **Hired-companion names.** `Hire165.cs` gives hired bots the names Isolde, Brangwen, Gareth, Diarmuid, Deirdre
   and Eirik. These are companion bots, not heroes, and the lore no longer uses "Isolde". But a bot named Deirdre or
   Diarmuid sits close to the rule that no hero is a bot.
