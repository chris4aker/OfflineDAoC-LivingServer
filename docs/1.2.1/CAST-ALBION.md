# Albion's present-day cast (NPC pass, phase 1)

The people a story of Albion can hand a line to, so that Avalon, Krondon, Caer Sidi and the frontier know each other.
Owner decision 10 (2026-11-22): present-day characters belong in the stories and should recur across places and
bosses. Legends stay legends ([HEROES.md](HEROES.md)); nothing after 1.65.

**How to read the counts.** "Mentions" is the bare name in the narrative strings, facts and chapter or page titles
left out: `python3 tools/lore/build_npc_roster.py --counts "<name>"`. `tools/lore/name_counts.py` caps only titled names
(Sir, Dame, Lady, Master, Captain...), so a bare name such as Elowen or Wenna is **not checked by CI** today. The cap of
12 is applied here to the bare name too, and the names over it are marked **over**. Phase 2 must not add to an **over**
name; it should carry new lines on the cast with room.

**Sources.** *Real* means a 1.65 NPC in `tools/lore/npc_roster.json`, with its era (*confirmed*: a dated period page;
*probable*: the live capture or a quest script in a 1.65 region). *Ours* means Living Server writing.

## The recurring cast already in the lore

**Elowen of Caer Gothwaite.** *Ours.* Mentions **89, over** (9 files).
- Home and role: the last reader of the college of the old tongues of Avalon, now at Caer Gothwaite. She walked the
  causeway at dusk for eleven years before the burning.
- Knows: keeps the list of the colleges in their founding order (`locations/avalon-city`); keeps the map with the
  two-headed beast (`bosses/duraek-the-empowered`, `bosses/xanxicar`); copied Sys'sro's ledger
  (`bosses/syssro-the-ruthless`); collected the three vyr'em accounts (`bosses/veraerius-the-brave`); grants the Academy
  one point on the Crystal Cave (`locations/crystal-cave`); also in `kovos`, `draargus-the-mighty`,
  `wenoiak-the-enlightened`.
- Talks: short declaratives, a scholar's precision, dry refusals ("Elowen says the marsh is not rising"). Writes "with
  great reluctance" and without comment.
- Ties: Sefin (walked the burned city with her), Mistress Angharad (her master's master), Chancellor Rhydderch's saved
  books, Magus Hedydd (learned her dusk-reading).
- Phase 2: no new mentions. Let her list, her map and "the readers of Caer Gothwaite" carry the next lines.

**Wenna of Caer Gothwaite.** *Ours.* Mentions **129, over** (16 files).
- Home and role: a reader of Caer Gothwaite who went down into Krondon twice; takes rubbings and reads the hall-folk's
  titles.
- Knows: Krondon's law and its titles (`locations/krondon`, `bosses/ulor-se-bysen`, `bosses/drihten-elreden`), the stores
  (`bosses/beran-the-supply-master`), the wet passage (`bosses/fuladl`, `bosses/fulafeallan`), the school slate
  (`bosses/morgus-urgalorg`), the count of the walk out (`bosses/orylle`: "forty-four walked out").
- Talks: notes and rubbings, one-line verdicts ("a guard is a kind of grief"); refuses to copy what should not be copied.
- Ties: her clerk Garmon (4), the colleges' seals in Krondon (`locations/avalon-city`), Elowen.
- Phase 2: no new mentions. Garmon can take her clerk's lines.

**Sefin of the Inconnu.** *Ours.* Mentions 12, at the cap (4 files).
- Role: the Inconnu guide who walked the burned city with Elowen and guided Sir Maelon's two hundred
  (`locations/avalon-city`).
- Knows: why the drakoran lord to fear is the one who reads (`bosses/wenoiak-the-enlightened`), Dra'argus at the stair
  (`bosses/draargus-the-mighty`), why the Inconnu stopped sending spirit-speakers (`bosses/veraerius-the-brave`).
- Talks: aphorism, said once ("the difference between a reader and a reeve").
- Phase 2: at the cap; call him "the Inconnu guide".

**Chancellor Rhydderch.** *Ours.* Mentions 11 (5 files).
- Role: the last chancellor of Avalon, "Rhydderch of the law", in the chair nineteen years and in it on the first night
  of the burning; saved the charters and the chancellors' court book (`locations/avalon-city`, `locations/crystal-cave`).
- Knows: the cloister and its blessed well, now Sys'sro's pit (`bosses/syssro-the-ruthless`); his commentaries sit on
  Weno'iak's shelves (`bosses/wenoiak-the-enlightened`).
- Caution: `history/albion` also has "the White Book of Rhydderch" (the Welsh manuscript); keep the two apart.
- Room: 1. Spend it on the saved court book, not on a new scene.

**Magus Hedydd of the Academy.** *Ours.* Mentions 9 (3 files).
- Role: an Academy magus who learned the readers' dusk-reading and asked to leave the school of air after the mound
  (`locations/crystal-cave`); keeps a commonplace book (`bosses/wenoiak-the-enlightened`).
- Talks: a scholar's guesses, set down as guesses ("thought Weno'iak had not finished learning the working").
- Ties: Magus Corwen (14, **over**), who wrote while Hedydd held out her hands to the light.
- **Contradiction:** `locations/crystal-cave` has "her hands"; `bosses/wenoiak-the-enlightened` has "after his own school
  failed him". Mythic is silent, so phase 2 picks one (the Crystal Cave scene is the stronger one: keep her).
- Room: 3. The best voice for the Academy's side of the Avalon war.

**Gracien Adamson, name registrar of Fort Gwyntell.** *Ours* (Fort Gwyntell is a 1.65 Shrouded Isles fort; no registrar
of that name is in the capture). Mentions **29, over** (17 files).
- Knows: every household and drakoran of the Dales of Devwy, from "the second book" of his register
  (`locations/dales-of-devwy`, `bosses/adelon`, `bosses/bevvny`, `bosses/the-hoges-household`, `bosses/tower-guardian`...).
- Talks: dry, short with time-wasters, minded to note that "he did not ask for this".
- Phase 2: none. His book's title already opens 9 pages: vary the opening instead (see VOICE-ALBION.md).

**Mordwywr, enchanter of Little Haven.** *Ours.* Mentions **16, over** (11 files).
- Knows: the Ledger of Little Haven, what is paid for proof of the dead on Caldey (`locations/caldey`, `bosses/durval`,
  `bosses/rose`, `bosses/eralyne`...).
- Phase 2: none. Ten pages open "From the Ledger of Little Haven, kept by Mordwywr": vary them.

**The clerk Tobias of the Hall of the Corrupt.** *Ours.* Mentions **59, over** (13 files).
- Knows: the last nights of Lord Gildas's watch, every captain and sergeant (`locations/hall-of-the-corrupt`,
  `bosses/lord-gildas`, `bosses/captain-heathyr`, `bosses/sergeant-eddison`...). His reports went to the Constable.
- Phase 2: none. He is the Hall's own chronicler and should stay there.

**Captain Rhodri of Snowdonia Fortress.** *Real*, probable (OpenDAoC mob, Snowdonia Fortress). "Captain Rhodri" 6;
bare "Rhodri" 9, but three of those are a different Rhodri, a dead man on Dame Nest's roll (`bosses/bane-of-hope`).
- Role: the frontier captain who takes the Lord of Deceit papers (`bosses/belgrik`, `bosses/ellyll-seer`,
  `bosses/isolationist-courier`, `bosses/overseer-tepok`, `locations/snowdonia`, `locations/black-mountains-north`).
- HEROES.md decision 1 keeps him only where he is the real quest giver. Room: 6.
- Caution: rename the dead Rhodri on the roll in phase 2 (a different name, same place), so that one name is not two men.

**Lieutenant Kuebler of Castle Sauvage.** *Real*, probable (Castle Sauvage). Mentions 11.
- Knows: Belgrik's Book of Lore "because he has asked the right people" (`bosses/belgrik`); counts Saffron among the signs
  of a war (`bosses/saffron`); takes the notes the Cotswold masters send north (`locations/camelot-hills`,
  `locations/forest-sauvage`).
- Ties: his clerk Anwen (*ours*), Lord Mathius (real, Castle Sauvage), Sir Tilian.
- Room: 1.

**Loremaster Alain of the Academy.** *Real*, **confirmed** (Allakhazam, *Symbol of the Broken*, comments from May 2003;
OpenDAoC mob in Camelot). "Loremaster Alain" 12, at the cap (bare "Alain" 13 counts Sergeant Alain, another man).
- Knows: the shades of the Barrows, Uther's borrowed face, the Scroll of Fate (`bosses/shade-of-uther-pendragon`,
  `bosses/shade-of-ambrosius-aurelianus`, `bosses/dathmul`, `locations/stonehenge-barrows`, `locations/camelot`).
- Phase 2: at the cap; use "the Academy's loremaster".

**Sister Elaydith of Camelot.** *Real*, probable (Camelot). Mentions 13, **over** by one (`OVER_CAP` holds her at 13).
- Knows: Lord Aryon's warded box and its stolen key (`locations/salisbury-plains`, `locations/avalon-marsh`,
  `locations/camelot`); nursed the plague's first guard (`bosses/marquis-dortaleon`); sent her runner down the Albion stair
  of Darkness Falls (`bosses/gatekeeper-dommel`, `bosses/malroch-the-cook`).
- Phase 2: none; "Sister Elaydith's runner" can carry Darkness Falls.

**Master Ferowl of the Academy, Cotswold.** *Real*, probable (the Academy epic script `Academy50.cs`, Cotswold).
Mentions 13, **over** by one.
- Knows: Aidon the Archwizard and the masters who did not come back (`bosses/aidon-the-archwizard`); sends his best to
  Morgana (`bosses/morgana`); "as I said" on the Morgus report (`bosses/morgus-urgalorg`).
- Phase 2: none; he is the Academy's head and can be "the Academy's master".

**Lidmann Halsey of Adibard's Retreat.** *Real*, probable (Defenders epic scripts, OpenDAoC mob). Mentions 14, **over**.
- Knows: the Defenders' road to Lyonesse and the Cailleach (`bosses/cailleach-uragaig`, `bosses/soulfear`,
  `bosses/cirfans`, `bosses/kings-wight`, `locations/avalon-marsh`). Phase 2: none.

**Sir Arward and his scribe Hywel of Caerwent.** Sir Arward is the Golestandt quest knight (*real*, confirmed by the
Camelot Herald's 1.45 notes, cited in `bosses/golestandt`); "Sir Arward" is held at 25 by `OVER_CAP`, bare 30. Hywel
(*ours*) 20, **over**. Both belong to the year the dragons woke: a recent past, not a present-day teller. No new lines.

**Lady Nimue.** *Real* quest giver (`OVER_CAP` 15). She shares her name with the Lady of the Lake legend; the lore
already treats her as the present-day Nimue. No new lines.

## Real 1.65 NPCs to bring in

Each fills a generic role the lore uses a great deal; `tools/lore/npc_role_map.json` lists where.

| Name | Where | Role | Era | Mentions | Fits |
|---|---|---|---|---|---|
| Lady Fridwulf | Camelot | cleric trainer | probable | 0 | "the priest" of Camelot's Church where a scene is in the city; Caer Sidi's company priest stays "the priest" |
| Brother Ethelbald | Camelot | friar trainer | probable | 0 | the friar who blesses a company at the city gate |
| Master Grundelth | Camelot | wizard trainer | probable | 0 | the Academy's voice when Ferowl and Alain are capped |
| Sir Amren | Camelot, the Defenders' building | the Defenders' epic steps (Shrouded Isles) | **confirmed** | 0 | the Defenders' side of the Isles war; pairs with Lidmann Halsey |
| Sister Finella | Fort Gwyntell | cleric trainer | probable | 0 | "the priest" of the Isles (Ilron, Caer Sidi's road); suggested for `bosses/lich-lord-ilron` |
| Captain Leri, Lieutenant Brude, Lieutenant Niane | Snowdonia Fortress | guard officers, quest givers | probable | 4 / 0 / 0 | the frontier's watch beside Captain Rhodri |
| Lord Mathius | Castle Sauvage | the castle's lord | probable | 6 (titled) | "the marshal" of the southern frontier |
| Captain Bonswell | Prydwen Keep | guard officer | probable | 0 | the keep that received Hywel's scorched journal |
| Captain Circa | the Thidranki portal keep | battleground captain | **confirmed** (Herald) | 0 | Albion's young at Thidranki |
| Lord Benowyc, Lord Erasleigh, Lord Boldiam and the other keep lords | the frontier keeps | keep lords | probable (named by the server's rule) | 8 / 0 / 0 | "the marshal" or "the captain" of a frontier scene |

Brother Codeth (10, from the classic data quests of Camelot Hills) and Captain Dillon (7) are already present and near
the cap.

## Proposed new recurring figure

Albion already has its Elowen. If one more voice is wanted, take **Lady Fridwulf of the Church in Camelot**: a real
cleric trainer, 0 mentions, in the city every company starts from. She can receive the dead's names that Brother
Cadoc and Dame Ysolde used to carry, without becoming the default teller (12 at most).

## Cautions

- **Two spellings, one knight:** the lore has "Sir Tillian" (3, Lord of Deceit) and "Sir Tilian" (5, Castle Sauvage);
  the capture has Sir Tilian. Use one spelling in phase 2.
- **Prydwen:** "Lord Prydwen" is Camelot's paladin trainer (real) and Prydwen Keep a place; avoid him beside the keep.
- **The Constable** (148 mentions, a title, not a name) receives every report in Albion. He is the lore's busiest
  figure and is not capped. Phase 2 can name his clerk instead of adding to him.
