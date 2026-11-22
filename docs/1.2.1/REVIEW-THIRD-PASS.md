# Review: the third lore pass (HANDOFF 1.5, deepening)

The reviewer is independent and did not write these passages. Three commits were reviewed on `wt3/lore3-review`:
- `dde65a09`: Albion (41 files; King Mark as Duke Marcus's son, the Caer Sidi labels, the epic summaries, the Shrouded
  Isles, thin bosses, book pages)
- `3a20336a`: Midgard and the neutral proving fields (Caledon, Abermenai, Thidranki, Murdaigean, the overview; Luso and
  six minor bosses; saga-voice chapters for Lydsyg, Dyranapur and Hurjavelen)
- `7c1e1b71`: Hibernia (Blight, the Cursed Forest and 14 thin bosses)

The fixes are in `d9763dd5`. Paths are relative to `source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **verified** (checked against a translation or a reliable summary online), **known** (checked from the
reviewer's knowledge of the text, not looked up this time), **unverified**.

Severity:
- **High:** breaks an owner rule.
- **Medium:** a myth error, or a contradiction a player would notice.
- **Low:** prose, repetition, citation detail.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- The `myth` facts hold up well. Two were wrong (De Gabáil in t-Sída, Clíodhna's birds) and two had citation details
  wrong (Egil's chapters, Hlöd's kinship). All four are corrected. No `myth` fact had to be relabelled `original`;
  Clíodhna's birds moved to a fact of their own that cites the later tales.
- **Nothing was cut.** A word-subsequence check of every old string against the new files found only intended
  rewordings: "the priest" relabelled to Ilron, Brother Cadoc or Dame Ysolde; "Sir Arward's company" to "the knights of
  the scorched journal"; "Catacombs lore" out of `luriquay`; and Mark joined to the Tintagel topic. One detail had
  dropped, "a priest of Caer Gothwaite" in `fames`, and it is restored. The Murdaigean writer kept the old Keating fact
  and added a second one beside it.
- No hero stands in a present-day scene. Arthur stays dead: the Avalon bed waits for a barge that never came, and the
  Isle of Apples page tells of the king brought there to be healed. Merlin stays missing.
- No Saint Patrick and no churchmen in the new Hibernian text. Caílte's *Acallam* is told without its listener.
- There is nothing after 1.65 in the new text. The last "Catacombs" label in `hibernia-wilds.json` is reworded (see
  X3 for the four older ones). There is no developer vocabulary.
- Every name is under the cap. Caílte went from 4 to 10, Diarmuid from 3 to 8, and Conán Maol from 2 to 4. Brother
  Cadoc and Dame Ysolde are both at 10.
- One decision-7 breach: the companion name Gráinne (H3).

| Realm | High | Medium | Low | Info |
|---|---|---|---|---|
| Albion | 0 | 1 | 4 | 5 |
| Midgard and Neutral | 0 | 1 | 5 | 5 |
| Hibernia | 1 | 2 | 3 | 6 |
| Across realms | 0 | 0 | 1 | 2 |

## Albion (`dde65a09`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| A1 | `caer-sidi`, `apocalypse`, `soul-reckoner`, `silencer`, `crypt-lord`, `lich-lord-ilron`, `fates-chosen` | Medium | **The toll did not add up.** The files said 71 of 80 came home, so nine were lost. But the crypts took nine (crypt-lord, the Caer Sidi page 3, fates-chosen, apocalypse), Ilron's gallery took two (lich-lord-ilron, apocalypse, fates-chosen), and the Silencer took two "in that first hour". That makes thirteen dead. The Silencer chapter also had the company go at him "eighty strong". **Source:** none. The third company, Dame Ysolde and the eighty are Living Server inventions. Neither the Camelot Herald nor the period guides tell of any company's losses in Caer Sidi. | **Owner decision (2026-11-21):** make the files agree. Every death the stories tell is kept, so the count changes: **sixty-seven of eighty** came home, in `caer-sidi` (story, Hedd's page, the lamp chapter), `apocalypse` (story, chapter, the number cut over the chapel door), `soul-reckoner` ("eighty in; sixty-seven out", twice) and the `chat/tales.lines` line ("sixty seven came home"), with the corpus and DEBATES.md regenerated. In `silencer`, the company now goes at him "with all that was left of its eighty" and "every one of the eighty who was still on their feet". "The ten did what eighty could not" is kept as the saying. |
| A2 | `caer-sidi` story | Low | "The first company of Albion to climb, the summer company... and three more went up in the autumn. The first was lost in the crypts". This reads as if the summer company was lost in the crypts. | Now "The first of the autumn companies". |
| A3 | `fames` | Low | The rewording to "The third company's priest" dropped "of Caer Gothwaite". | Now "The third company's priest, a Caer Gothwaite man". |
| A4 | `avalon-city` story | Low | The new story paragraph retells the "college of healing kept one bed" chapter almost word for word, A7's sentence from the previous review included. Stories are summaries, so this is allowed, but it is the clearest case of padding to reach the 500-word floor. The other epic summaries (crystal-cave, dartmoor, hall-of-the-corrupt, krondon, caer-sidi) add new matter. | No change. The next writer can make the summary shorter in wording (never in events) and spend the words elsewhere. |
| A5 | 23 location book pages | Low | **The one-sentence book-page additions.** About 40 of them bring short pages to 100 words. Most fit the page's voice and add a detail ("Their drums carry across the water to the fort on still nights"; "He oils the hinges so they make no sound at night"; "The grooms throw stones at them, and the stones come back wet"). A few are filler: "Nob has stopped trying", "There are a great many in the north", "It smells of windfalls". None is wrong or out of voice. | No change. |
| A6 | `cornwall`, `dartmoor`, `history/albion` | Info | **King Mark as Duke Marcus's son** (owner decision): the two files agree (Uther's day, the moor, the shut man, king at Tintagel in Arthur's day). Both are marked `original`, and the Herald text is unchanged. Malory's Mark fits the chronology. | None. |
| A7 | `albion-shrouded-isles` | Info | *Vita Merlini*: the Island of Apples, the Fortunate Isle, fields that need no plough, nine sisters, Morgen's healing, her shape-changing and her flight, Barinthus the pilot who knows the sea and stars, the golden bed, "if he stays long with her" (**known**). **Caliburn forged in Avalon:** *Historia Regum Britanniae* IX.4, "Caliburno, gladio optimo, qui in insula Avallonis fabricatus fuerat" (**known**). Both are right. The hint that the first sister's name is close to Morgana's is kept (owner decision). | None. |
| A8 | `crystal-cave` | Info | Malory, book 4: Merlin "assotted upon" Nimue, one of the damsels of the lake. In Cornwall she makes him go under a great stone by his own craft, and he never comes out (**known**). The story does not name Nimue, which keeps her apart from the quest giver Lady Nimue. | None. |
| A9 | `caer-sidi`, `funus`, `silencer`, `lich-lord-ilron` | Info | **The labels.** "The priest" no longer means both Ilron and the company's priest. Brother Cadoc and Dame Ysolde are named at the choice, the sill and the account (decision 6). "The third company" still has many mentions, but in the Caer Sidi chapters it reads as a name, not a tic. | None. |
| A10 | `tepoks-mine` | Info | The present-day "Sir Bors" comes from the game's Hidden Insurrection quest ("Sir Bors' chest"). He is not the Grail knight. | **Owner decision:** keep him as he is. Noted in HEROES.md. The counter's Bors count (7) includes him. |

## Midgard and Neutral (`3a20336a`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `caledon` story and `wild man of the wood` topic | Medium | "The Academy has sent young searchers into Caledonia" for Merlin. This contradicts `crystal-cave.json`: "The Academy holds that the cave is... where Morgana trapped Merlin". It does not contradict "Merlin is missing, not an NPC", because nobody finds him. | Kept as the brief allows. Now the masters hold that Morgana shut him in the crystal under Avalon, and young searchers "unwilling to take the masters' word" or "who doubt their masters' account" go north, and "none has found him". |
| M2 | `proving-fields` Egil fact | Low | Cited as "chapter 52" for the whole story. Ch. 52 is the enhazelling (**verified**, wikisaga). Thorolf's burial on the field with his weapons and raiment is ch. 55 (**verified**, voluspa.org). | Now "chapters 52-55", with the burial and its chapter added, and the voluspa page added to the source. |
| M3 | `proving-fields` Hervarar fact | Low | "Angantyr kills his brother Hlöd". Hlöd is Heidrek's son by a Hunnish mother, so he is Angantyr's half-brother. The story already said so. | Fact now says "half-brother". |
| M4 | `murdaigean` fact, story and topic | Low | Keating's third article (**verified**, libraryireland.com): it is charity, refusing no one what he has, not "food for any who ask". The rest is right: the twelve books, the field of green sedge, the shield and hazel stake, nine warriors at nine ridges' distance, the plaited hair, a tree's breadth of start, the brow and knee, the thorn, steady hands, nine men of another nation, and the kin's pledge. | Reworded in all three places. |
| M5 | the three saga-voice chapters | Low | They read well and in era: the patronymic opening, the plain statement, the dialogue in short lines, Thorgrim's verse, the "Here ends" close. Two habits repeat across them: all three open "There was a X named Y, son/daughter of Z", and two close "Here ends...". That is the saga convention, so it is kept. Lydsyg's "as has been told" points back correctly to the story (Bodvar woke weeping) and to "The Singer's Night" (the last slate). Hurjavelen's Hrefna and Stanga's wall match the iarnvidiurs files. | No change. |
| M6 | Midgard minor bosses | Low | The inserts end on the deed, as the last review asked ("Nobody draws"; "the morvalt hang their charms on it"; "deeper"). Smastein's ending, a children's game, is the weakest and least tied to the boss. | No change. |
| M7 | `caledon` | Info | *Cad Goddeu*: the roebuck and whelp from Annwn, Arawn's champion beaten when Gwydion names him Bran from the alder sprigs (**known**, the Myvyrian triad note, as cited). "The alders in the first line, they made the commencement" (**known**, Skene). Setting the battle in Celidon's wood is `original` and covered by the "grovewood as Gwydion's trees" fact. Myrddin: *Yr Afallennau* and *Yr Oianau*, "fifty years" in the forest, the apple tree, the pig and Rhydderch's dogs, Arfderydd in 573 (**verified**, Wikipedia's summary of the poems). *Historia Brittonum* 56, the seventh battle in silva Celidonis (**known**). | None. |
| M8 | `abermenai` | Info | Second Branch (**verified**, Guest's translation via Mary Jones): "they went from Aber Menei with thirteen ships"; the Lake of the Cauldron; the dead "not able to speak"; Efnisien "rent the cauldron into four pieces, and burst his own heart also"; seven survivors. **Lleu's naming is the Fourth Branch**, and the fact says so; the story does not call it the Second. The boat of sedge and seaweed and the wren struck in the leg are right (**known**). *Ongulsey* and *Cytiau'r Gwyddelod* are right. | None. |
| M9 | `thidranki` | Info | *Þiðranda þáttr*: the third knock, the nine in black from the north, the nine in white from the south, the seer's warning (**verified**, Wikipedia's summary). He is found wounded in the morning, tells what he saw, and dies (**known**; the summary does not cover it). "Their due" fits the seer's reading that the old dísir took their share. The tomte and the hidden butter, and Gwarwyn-a-throt (Rhys, 1901) are right (**known**). | None. |
| M10 | `proving-fields` | Info | Malory book 21's adder (**known**). The boy-troop of Emain, thrice fifty, none surviving (**known**, the *Táin*). Hervor's fort, Gizur naming Dunheath and the Dylgja vales under the Jassar fells, and Odin's spear (**known**). | None. |
| M11 | names | Info | Bodvar, Hrafn, Gaut, Ulla and Kolr are new present-day or minor names, all under the cap. None is a hired-companion name. | None. |

## Hibernia (`7c1e1b71`)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | `cursed-forest` fact, story and `division` topic | Medium | **De Gabáil in t-Sída** (**verified**): there the Mac Óc asks *the Dagda himself* for a day and a night in his own dwelling and keeps it. Elcmar is tricked in *Tochmarc Étaíne*, on the Dagda's advice. The fact had the two reversed, and the story and topic followed it. | Fact corrected. The story and topic now tell the De Gabáil version and keep Elcmar as "another telling". No detail is lost. |
| H2 | `queen-cliodna` fact | Medium | "In the Dindshenchas... Her three birds eat apples of an otherworld tree." The birds are not in the *Dindshenchas* (**verified**: Wikipedia sources them to modern handbooks and to the Tadg tale in the Book of Lismore). | The Dindshenchas fact now ends at the wave. A new fact cites *Echtra Thaidg mheic Céin* (O'Grady, *Silva Gadelica*). The story now says "In the later tales her birds...". The source is only partly checked (**unverified** that the Lismore text has the apples). |
| H3 | `giant-caithor` story and fact | **High** | Decision 7: Gráinne is a hired-companion name in `Hire165.cs` ("Grainne"), and the new story told of Gráinne by name twice. `tools/lore/tests` cannot catch it, because Gráinne is not in `name_counts.py` HEROES. | Now "the high king's daughter, the bride Fionn had been promised" and "the king's daughter, who was carrying Diarmuid's child". The fact says "the woman he fled with". The tale's title still names her, as a citation. |
| H4 | `develin` | Low | Fergus mac Léti could be taken for the Red Branch's Fergus mac Róich, who appears in five other files. | Now "long before the Red Branch and its own Fergus, the son of Róich". The Fergus mac Róich count is unchanged. |
| H5 | `uafas-capall` book page | Low | The Gilla Decair (**verified**, Rolleston): Conán and thirteen more on the mare, and Liagan the Swift on her tail. The fact and story say fourteen plus one. The book page said only "fourteen". | Now "fourteen of the Fianna into the sea on its back, and a fifteenth by the tail". |
| H6 | `green-maw`, `wyvernic-wraith`, `uafas-capall`, `skeaghshee` | Low | Formulas: "the grandmothers say" plus an aphorism (green-maw); "The X tell that tale over Y for its Z" three times (skeaghshee, wyvernic-wraith, uafas-capall); "the druids of Lough Gur" in five of the new inserts. That last one is the established Hibernian teller (in 30 files), so it is left. | Green-maw now ends on the deed (the scraped pit and the sleeping god). The wraith and Uafas lines are reworded. Skeaghshee keeps the original form. |
| H7 | `blight`, `juggernaut`, `green-maw`, `skeaghshee`, `luriquay` | Info | *Cath Maige Tuired*: the well Sláine with Dian Cécht, Octriuil, Miach and Airmed, filled with stones of the Drowes at Ochtriallach's bidding; Miach's 365 herbs on Airmed's cloak, mixed by her father; Ogma's firewood and the Dagda's rath, Cridenbél and the three gold pieces, the black-maned heifer whose lowing brings the cattle home; the porridge pit, the ladle a man and a woman could lie in, the finger on the gravel; Goibniu's three strokes, Luchta and Credne, Ruadán and Brig's first keening; Lugh at the door and the sage's seat (all **known**). The story leaves Miach out of the singing at Sláine because he is already dead. The source text itself lists him, and the fact follows the source. | None. |
| H8 | `badb` | Info | The first battle (**verified**, Fraser, §29): "Badb and Macha and Morrigan went to the Knoll of the Taking of the Hostages... a furious rain of fire, with a downpour of red blood", for "three days and nights". The writer's flag is cleared. The Morrígan's prophecy after the second battle (**known**). The washer at the ford in *Bruiden Da Choca*, washing Cormac Cond Longas's chariot and harness (**verified** in substance, Wikipedia). | None. |
| H9 | `cailean`, `flame`, `orey-eyed-oghamist` | Info | Boand at Nechtan's well, three cupbearers, widdershins, thigh, hand and eye, drowned at the river-mouth (**known**, Metrical Dindshenchas). The hazels come from the Segais tradition, which is acceptable. Mide's fire at Uisnech: seven years, a sack and a pig, the druids' tongues buried (**known**). The ogham tract: seven b's on a birch, sent to Lugh (**known**). | None. |
| H10 | `giant-caithor`, `develin`, `wyvernic-wraith`, `melancholic-fairy-queen` | Info | Searbhán: one-eyed, harmed only by three blows of his own iron club, sleeping in the tree by night (**known**). *Echtra Fergusa maic Léti*: the lúchorpáin, the three seized, Loch Rudraige, the muirdris (**known**). *Aislinge Óenguso*: 150 maidens in silver chains at Loch Bél Dracon, the music of three days (**known**). *Echtra Nerai* (**verified**, Mary Jones): the withy, a year in the síd though it seemed three days, the wild garlic, primrose and golden fern, and "nor will he come till Doom". | None. |
| H11 | `cursed-forest` | Info | Caílte in the *Acallam* telling the lore of places is told without Patrick. | None. |
| H12 | `luriquay` | Info | "Catacombs lore says" is now "the lurikeens' own tales say" (post-1.65 reference gone). | None. |

## Across the realms

- **X1 (Low): endings.** The previous review's R1 ("...and that is why") is much less frequent in this pass. Midgard
  ends on deeds. Albion's book pages end on plain facts. Hibernia's bosses mostly end on an image of the boss. The
  remaining habit is the "X say" teller line at the start of each insert ("The druids of Lough Gur say", "The brehons of
  Tir na Nog", "The storytellers of Ardee", "The falconers", "The stablemasters", "The smiths"). It is varied enough to
  stand.
- **X2 (Info): consistency.**
  - King Mark agrees across cornwall, dartmoor and history.
  - The Constable's drawers agree across camelot, hall-of-the-corrupt and krondon.
  - Caer Sidi's toll now agrees: 9 + 2 + 2 = 13 dead, 67 home.
  - Fergus mac Léti and Fergus mac Róich are kept apart.
  - The Academy's Merlin agrees across crystal-cave and caledon.
- **X3 (Info, owner): the "Catacombs" labels.** The brief asked only for `hibernia-wilds`, and that one is fixed.
  Four older facts still cite "Mythic's Catacombs lore":
  - `avalon-city` (Morgana trapped Merlin in a crystal);
  - `morgana` (the same);
  - `caer-sidi` (Arawn's nine maidens of the cauldron);
  - `jordheim` (the founding, and Sigrun and Brynhild).

  They are source notes on Herald pages written for Catacombs (December 2004). Rewording the label would hide the
  date, so they are listed for the owner instead (see below).

## Not checked online

- Malory book 4, *Vita Merlini*, *Historia Regum Britanniae* IX.4, *Cath Maige Tuired*, the Dindshenchas poems, the ogham
  tract, *Aislinge Óenguso*, the Fergus and Searbhán tales, *Cad Goddeu* and the tomte and bwca tales were checked from
  the reviewer's knowledge (**known**). They agree with the facts as written.
- Thidrandi's dying words come from the reviewer's knowledge of the *þáttr*. The online summary stops before them.
- Clíodhna's apples in the Lismore Tadg tale are not confirmed.

## Tools

These ran on `d9763dd5`, all exit 0:
- `tools/lore/lint_lore.py`: 0 problems, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 9 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK. The corpus was stale after the Caer Sidi chat line
  changed, and was regenerated with the tool.
- `tools/bot-voices/debates_doc.py --check`: OK, after regenerating (the same line is in one scene).
- `tools/bot-voices/check_runtime_names.py`: OK.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `python3 -m unittest discover -s tools/bot-voices/tests`: OK.

`UT_LoreBible` (dotnet) could not run here because dotnet is not installed. It must run in CI or on the owner's PC
before merge. All of this is tests only; nothing has been seen in game.

## For the owner

1. **The Catacombs-era Herald facts (X3).** Should facts drawn from the Herald's Catacombs-launch lore pages stay as
   sources? Morgana trapping Merlin in the Crystal Cave, for example, is told in `crystal-cave`'s story. Or are they
   post-1.65 and to be replaced? Recommendation: keep them. They are the only Mythic account of these places. Mark
   them "Herald (2004)" so their date stays visible, rather than hiding it.
2. **Gudrun and Áine.** Both are hired-companion names in `Hire165.cs`, and both appear in the lore as present-day game
   NPCs: Gudrun the enchanter of Svarhamr in `malmohus`, Áine the fire-keeper in `abomos-the-soultrapper`. They are not
   legends, so decision 7 is not broken. Recommendation: leave them. Add Gráinne (and any legend the lore now tells
   of) to the test's check so H3 cannot recur.
3. **Avalon City's summary (A4).** Should the new summary paragraph that repeats the "one bed" chapter be rewritten in
   fresh words? Recommendation: yes, in a later pass, keeping every event.
