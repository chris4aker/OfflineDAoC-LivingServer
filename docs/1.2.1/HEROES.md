# The famous heroes of each realm (1.2.1 lore pass)

Read this before writing any lore that names a hero, a witness or a company. It holds the owner's decisions, the canon
review's findings and the work left to do.

## The owner's decisions (2026-11-20, verbatim where quoted)

1. **Retire the repeated witnesses.** "Dame being repeated so much is a problem. There should be roughly 2 or 3 'famous
   heroes' of each realm, none of them being bots but legends." Dame Ysolde, Brother Cadoc, Sir Cador, Sir Geraint
   and Brother Odran are retired from their witness roles. Captain Rhodri stays only where he is the real quest giver.
2. **Source:** "Folklore heroes", the real legends of each realm's mythology, not invented ones.
3. **No trimming:** "No lore should be trimmed, in fact the opposite. We should be cleaning up and elaborating where
   needed to give rich history and stories."
4. **Server names don't matter:** "Don't care about the server names, because server names were based on lore and as
   an offshoot of the original DAoC, there aren't 'multiple' servers." Galahad, Lancelot, Percival, Kay and the rest are
   free to use. Ignore the canon review's server-name advice (section 3 of CANON-REVIEW-HEROES.md).
5. **Arthur rides with his famous knights, and the other realms follow the same pattern:** "I like the idea of Arthur
   bringing other famous knights with him in the stories rather than always unnamed knights. Midgard and Hibernia should
   be treated similar." Where a story tells of a legend-age deed and says "Arthur's knights", "six knights" or "a
   company", it names the famous companions instead. Midgard and Hibernia get the same treatment with their own legend
   companies.

6. **Names where they add gravity:** "I like the revisions and rewrites but you can leave names friars or captains
   where it adds gravity." A present-day friar, captain or knight may keep or regain a name (Dame Ysolde, Brother Cadoc
   and the rest included) where the name gives the scene weight: a death, a last stand, a choice, a record someone
   signs. Elsewhere use the role ("the captain", "the priest"). The 12-mention cap still holds for every name, so no
   witness becomes the default teller again.
7. **Companions may carry lesser legend names, never the lore's** (2026-11-21): "Companions can be 'B' tier heroes with
   famous names but nothing referenced in the lore itself." A hired companion's suggested name may be a lesser hero's
   (Dinadan, Lynette, Brangwen, Loegaire, Fand, Grainne), but never a hero or legend companion the lore tells of, and the
   lore never mentions a companion. `tools/lore/tests` checks the list in `Hire165.cs` against every legend name the
   lore tells of (`name_counts.legend_names`: HEROES, the roster table below, the names in `myth` facts and in sentences
   that name a HEROES legend), less the ordinary given names present-day characters share (`SHARED_GIVEN_NAMES`: Edith,
   Gunnar, Áine, and Eirik, the Audliten quest giver, though Ragnar's son Eirik is in `history/midgard`).
   Gareth, Isolde, Diarmuid and Deirdre left the list; companions already hired keep their names.
8. **No real companies, nothing that breaks the fourth wall** (2026-11-21): "Nothing in the lore should reference real
   companies or break the 4th wall so to speak." No player-visible lore text names Mythic, the Camelot Herald as a
   website, Prima, Allakhazam, GameBanshee, Uthgard, EA or Broadsword, a patch or its number, an expansion as a product
   (Catacombs, Trials of Atlantis, Shrouded Isles as "SI"), developers, real years, or the software under the world
   (bots, NPCs, the server, scripts, templates, world data, "real players", quest logs, game hours). Narrative fields
   also never use game terms as game terms (levels, mobs, respawns, xp). In-world words that only look alike stay:
   a royal herald, Lurfos the Herald and the Herald's chronicle (the realm's own herald), Cryptos Mythicos, a patch of
   ground, the Catacombs of Cardova, the scriptorium, a crowd of winds "in a mob".
   - **What a player sees** (traced from the code): the `/lore` codex (`LoreCodex.cs`: summary, story, chapters, legend
     as "What is said", relations, history era and faction text, and each history fact's **text**), the loremasters
     (`LoremasterDialogue.cs`: intro, topics, boss topics, chapters, era text with its facts' text), lore books
     (`LoreBooks.cs`: title, pages, boss pages, the summary in the delve), the Herald's chronicle and the Bestiary
     (`LoreHerald.cs`, the launcher's `BestiaryControl.cs`: name, place, story, legend as "What the raid leaders say",
     chronicle sentences), boss yells and mechanic lines (`LoreBossWatch.cs`), atmosphere lines (`LoreAtmosphere.cs`),
     first-kill lines, bot lines (`LoreChat.cs`) and the tales and lore chat files.
   - **Never shown:** every fact's `provenance` and `source` (no code reads them but the schema check), and the text of a
     location's or boss's own facts. Ids, catalog keys, spawn names, aliases (matching only) and area names (memory keys)
     are not shown either. So facts keep their real sources, and the five facts from Mythic's Catacombs lore pages
     (December 2004: `avalon-city`, `morgana`, `caer-sidi`, `jordheim` twice) now read "Camelot Herald (2004)".
   - **Fight advice is in-world too** (owner, 2026-11-21, "Rewrite that"; this replaces the 2026-11-06 rule that kept
     levels, health, resists, timers, "mob" and "respawn" in the legend and bot lines). `fourth_wall.GAME_TERMS` (the
     three writers' lists merged) checks every visible field. `GAME_TERMS_FAIL = True` since the clean-up review
     (2026-11-22, [REVIEW-GAME-TERMS.md](REVIEW-GAME-TERMS.md)): every hit fails the lint. The list (now empty) is [LINT-LEGENDS-HITS.md](LINT-LEGENDS-HITS.md) (`python3 tools/lore/fourth_wall.py --report`). Map marks
     and the chat words melee, casters, group, camp, quest, mana, lol, grats, dropped, loot, pets, taunt, stun and line of
     sight wait for the owner (`PENDING_OWNER_DECISION`; `BAN_PENDING = True` bans them).
   - `tools/lore/fourth_wall.py` holds the field map, the word lists and the exceptions; `lint_lore.py` fails CI on any
     hit. `LoreBible.GameTalkPatterns` stays the twin of `chapter_lint.py` (warnings only, pinned at 38 by `UT_LoreBible`).

### Owner notes for later passes (2026-11-21; do not undo)

- **Keep the St Patrick's Day holiday chat line** (`holidays.lines`). It is a present-day holiday line, not Hibernian
  legend, and the rule against Saint Patrick in legend does not reach it. Later passes must not remove it.
- **Leave the present-day "Sir Bors" in `tepoks-mine`** as he is. He is the game's Hidden Insurrection quest NPC, not
  the Grail knight. Don't rename him. The counter's Bors count includes him.
- **Keep the Shrouded Isles hint** that the first of the nine sisters of the Isle of Apples has a name close to
  Morgana's.
- **Caer Sidi's toll is settled:** the third company went up eighty strong and came home **sixty-seven**. It lost nine in
  the crypts, two in Ilron's gallery and two to the Silencer. No period source gives a number, so the owner had the
  files made to agree. Keep every file to these numbers.
- **King Mark is Duke Marcus's son**, the son who came home alone from Dartmoor (`cornwall`, `dartmoor`; `original`).


9. **Owner rulings on the fight advice and stories** (2026-11-22, answering the reviews' questions):
   - The Bane of Hope throws again in every telling, as its code does; the old "has not moved a man since" stays as its past.
   - Maldahar stays the war party's foe his script makes him (40,000 health, a life drain and an energy burst).
   - Weno'iak's household is told without a count (the texts disagreed: 20, 13, 16).
   - Map positions such as "(about 32k/37k)" become in-world landmarks where the lore already names one near the spot;
     otherwise they stay as the raiders' map marks.
   - The everyday words of 2003 players stay in fight advice and bot chat: melee, casters, group, camp, quest, mana,
     pets, taunt, stun, line of sight, purge, dropped, loot, lol, grats, gz (`fourth_wall.PENDING_OWNER_DECISION` is
     allowed for good; `BAN_PENDING` stays False).
   - "Foes of name" / "great foes" everywhere, bot chat included.
   - The Behemoth may go one step past the top strength tier ("certain death, beyond even the whole muster of any
     realm").
   - Head counts and raid sizes stay numbers, spelled out (Ozur's fifty and a hundred, a raid seventy-four strong).
   - Elro's third treant window, which his script could never reach, is fixed (three-tenths to a quarter of his
     strength left), and his fight advice tells all three.
10. **Contradictions and present-day characters** (2026-11-22): "Fix the contradictions of the story, erring first on
   what was actual lore from Mythic, and if none, go with the rule of cool." And: "Present day NPCs can absolutely be a
   part of the lore, in fact that should be the case to make the realms feel more connected and alive with their
   stories."
   - When two texts disagree, Mythic's canon (the Camelot Herald, the Prima guides, the in-game texts) wins. Where
     Mythic said nothing, reword to the version that tells best, adding words rather than cutting.
   - Present-day characters (the realm's living NPCs, captains, friars, marshals) belong in the stories, and should
     turn up across places and bosses so the realms feel connected. A present-day character may share a name with a
     legend companion (Golestandt's Marshal Gareth stays), as long as the text never makes him the legend's man.
     Legend heroes stay legends (rule 1) and the 12-mention cap still holds.
   - First fixes: Iorwerth now sang in the deepest cellar but never went past the walled vein (Crystal Cave and
     Xanxicar agree); Weno'iak's books are on his shelves, but he moves them before any company comes, and leaves ash.
## Canon (from CANON-REVIEW-HEROES.md, checked against the Camelot Herald archive)

- At 1.65 **Arthur is dead** ("the late King of Camelot"), and the Round Table belongs to a lost age. Albion is in
  decline after his reign. All the heroes are **legends of the past**: they are told about, and they never stand in a
  present-day scene. Mythic's own Arthurian figures in the game are Morgana (epic boss), Lady Nimue (quest giver), the
  Lady of the Lake, the shades of Uther and Ambrosius, and the relics, the Scabbard of Excalibur and Merlin's Staff.
  Merlin is missing, not an NPC.
- **Galahad and Gawain die before Camlann. Bedivere outlives Arthur** (Malory). Geoffrey of Monmouth has Bedivere die
  earlier, so cite Malory for "the last survivor".
- **Caer Sidi is Arthur's own raid.** In *Preiddeu Annwfn*, Arthur sails to Caer Sidi with three shiploads, and seven
  come back. This is the honest place for Arthur and his knights in our Caer Sidi lore. The poem does not name the seven.
- **Midgard:** the Norse gods are alive and Ragnarok has not come. Sigurd, Ragnar and Hervor are heroes of older
  ages. Ragnar belongs to the 9th century, and he died in Northumbria, on Albion's island.
- **Hibernia:** DAoC's Hibernia has no Church. Never bring Saint Patrick or churchmen into Hibernian legend. Use the
  Tuatha Dé Danann, the Fianna and the Red Branch.

## Who tells the stories: leaders and companions

Each realm has **leaders**, the names a story reaches for first, and **companions** who ride with them in legend-age
stories, in place of unnamed knights. One cap of 12 mentions covers all of them (`tools/lore/name_counts.py`). Spread the
load, and name only the companions a story can use.

| Realm | Leaders | Companions (in legend-age stories, as the myth places them) | Myth sources |
|---|---|---|---|
| Albion | Arthur (dead at 1.65, told about), Sir Galahad, Sir Gawain, Sir Bedivere | Lancelot, Percival, Kay, Bors, Tristan, Gareth, Lamorak, Pellinore, Palomides, Gaheris, Lucan, Ector | Malory, *Le Morte d'Arthur*; *Sir Gawain and the Green Knight*; *Preiddeu Annwfn*; *Culhwch and Olwen* (Kay, Bedwyr) |
| Midgard | Sigurd, Ragnar Lodbrok, Hervor | Sigmund and Sinfjötli, Brynhild, Regin, Ragnar's sons (Ivar the Boneless, Björn Ironside, Sigurd Snake-in-the-Eye), Angantyr and his brothers, Starkad, Beowulf (giant-kin stories) | *Volsunga saga*; Poetic Edda; *Ragnars saga loðbrókar*; *Hervarar saga*; *Gesta Danorum*; *Beowulf* |
| Hibernia | Cú Chulainn, Fionn mac Cumhaill, Oisín | the Fianna: Diarmuid Ua Duibhne, Oscar, Caílte, Goll mac Morna, Conán; the Red Branch: Conall Cernach, Fergus mac Róich, Láeg; Nuada of the Silver Hand; Deirdre | the Ulster Cycle (*Táin Bó Cúailnge*); the Fenian Cycle (*Acallam na Senórach*, *Tóraigheacht Dhiarmada agus Ghráinne*); the Mythological Cycle |

Spellings use the accented forms: Cú Chulainn, Oisín, Fionn mac Cumhaill, Caílte, Láeg, Sinfjötli. The files are UTF-8.

The canon review also proposed new **leaders** for story shapes no current hero fills. These are the owner's call and
are not added yet:
- Albion: Pwyll and Taliesin.
- Midgard: Beowulf, and Sigmund with Sinfjötli.
- Hibernia: Nuada and Deirdre.

## Rules for the stories

1. **Legends, not witnesses.** A hero is told about ("the songs say", "when Arthur rode to Caer Sidi with Kay and
   Bedivere..."). No hero stands in a present-day scene, and none is a bot.
2. **Present-day companies use roles by default, and names where they add gravity** (decision 6). Keep one label each: Caer Sidi's "the third company", "the captain
   of the third company" then "the captain", and "the company's priest" then "the priest". Elsewhere "the knight-commander
   of the Isle of Glass", "the warden of the castle ruins", and "the captain of Snowdonia Fortress". Never let one label
   mean two people.
3. **Legend-age companies are named** (decision 5). Replace "six knights", "Arthur's company" and "the Fianna went" with
   the named companions the myth gives, or plausible ones from the table, marked `"original"` when the myth doesn't place
   them there.
4. **Mark the sourcing honestly.** Use `"myth"` only for what the source text says, and cite its title. Add a book or
   chapter number only if you checked it. Everything we add is `"original"`.
5. **Never shorten.** Keep every event and detail. Elaborate where a passage is thin.
6. **Era:** nothing after 1.65 (8 October 2003). No developer vocabulary.
7. **Caps:** one named person may have 12 mentions or fewer, counted by bare name for the heroes, history files included.
   Fix an overrun with pronouns and varied epithets ("the knight", "the chief of the Fianna"), never by cutting an event.

## Status and what is left (keep this up to date)

- **Owner sign-off (2026-11-20):** the owner read #380 (flagships) and #386 (heroes, with #387, #390 and #391) and approved
  them. Both are merged into `integration/1.2.1`. Nothing is on main.

- **Done.** On `lore/step4-heroes` (PR #386):
  - The roster, the counter, the nine hero profiles and the 48-file rewrite (#382-#385).
  - The prose review's fixes (#387: one naming scheme, 17 references that pointed at the wrong person, the new
    repetition).
  - The canon fixes on the hero passages (#390: the six myth errors, Saint Patrick out, invented "myth" facts relabelled
    `original`, one spelling per hero, every hero at 12 mentions or fewer).
  - **Decision 5, first pass** (branch `lore/step4-companions`). The legend companions are now named in these files:
    - `bosses/apocalypse.json`: Arthur's six who came home from Caer Sidi are Bedivere, Kay, Lucan, Bors, Percival
      and the steersman Iddon. Kay goes first down the stair. The naming is `original`.
    - `locations/caer-sidi.json`: the clerk's strip lists the same seven (Arthur, Bedwyr, Cei, Lucan, Bors, Peredur
      and the steersman Iddon), with Bedivere, Kay and Percival in their Welsh forms, so the two files agree.
      `original`. Gareth was swapped for Bors because Marshal Gareth of Prydwen Keep keeps the same roll.
    - `bosses/wrath-of-mordred.json`: Mordred trained beside his half-brothers Gawain, Agravaine and Gaheris; Gareth
      came later as Beaumains and kept out of his plotting. `myth`, from Malory.
    - `locations/camelot.json`: Lucan dies lifting Arthur after Camlann, and his brother Bedivere is left for the
      water. `myth`, from Malory.
    - `history/midgard.json`:
      - Sigmund and Sinfjötli in their wolf-skins (`myth`, *Volsunga saga*);
      - the sons of Arngrim (Angantyr, Hjorvard, Hervard and Hrani) and their killers, Hjalmar the Brave and Orvar-Odd (`myth`,
        *Hervarar saga*).
    - `history/hibernia.json`:
      - Fionn's named Fianna: Caílte, Diarmuid, Goll mac Morna, Conán Maol and Oscar;
      - Cú Chulainn's charioteer Láeg, his foster-father Fergus mac Róich, and Conall Cernach's oath of vengeance.
      - All `myth`.
    - `locations/murdaigean.json`: Caílte sets the pace of the Fianna's run. `original`.
  - **Left alone on purpose:**
    - The Llyn Barfog afanc: Arthur and Llamrei alone is the folktale.
    - Bellum's twelve battles and the Camlann adder: no myth names the knights there.
    - Oscar's lay: its "king's son" detail needs a canon check of its own before anyone names Cairbre.
    - Midgard's present-day warbands (Hallbjorn's eighty): they are not legend-age.
  - **Gaps:** Tristan, Starkad and Deirdre were in the table but not used yet. The second pass below uses all three.
  - **Companions review fixes** (REVIEW-COMPANIONS.md, applied on `lore/step4-companions`): the Cú Chulainn and
    Fionn antecedents, the Caer Sidi muster contradiction, Murdaigean, the Camelot sentence, Mordred and Gareth, Bors
    for Gareth, Hjalmar the Brave, the Sigurd and Hervor wording, the Fianna and Keating sources, and Hall of the
    Corrupt's Sir Peredur renamed Sir Pedrog so he is not taken for Percival.
  - **Decision 6, first pass** (names where they add gravity), in the bare-name counts:
    - Dame Ysolde, 8: Dorinakka's charge and her rebuke on the stair, Fate's Chosen's oath, Apocalypse's foot of
      the stair, Bane of Hope, the Host's count of the bodies, and the Caer Sidi sill.
    - Brother Cadoc, 9: the oath he set down, Apocalypse's "turnkeys", Bane of Hope's walk back, Funus's office
      said over the rider, the Host's account, and the Caer Sidi book.
    - Sir Cador, 2: Lurfos's killer and the first knot.
    - Sir Geraint, 1: the Host's body.
    - Each name is introduced with its role. Roles still carry most mentions.
  - **Small fixes:** the Host's "He wrote" is now Cadoc; Cathbad names Cú Chulainn; and the warp-spasm is described
    from the *Táin*.
  - **Decision 5, second pass** (branch `wt2/lore-review`; writers `8a616acd`, `348594f0`, `7b417b34`; review fixes
    `247c5b7a`). Text was only added or reworded; nothing was cut.
    - **Albion:**
      - Tristan, son of Meliodas, is in `lyonesse`, `cornwall` (Marhaus, Isoud, Mark at Tintagel) and `camelot`.
      - The Round Table roll is in `camelot`: Leodegrance, Kay, Lancelot, Tristan and Percival, the Grail Pentecost
        and Lancelot's 24 days.
      - Pellinore, Excalibur, the scabbard and Lancelot of the Lake are in `avalon-marsh`.
      - Accolon and the four queens are in `morgana`.
      - The child given to Ector, Kay and the sword in the stone are in `shade-of-uther-pendragon`.
      - In `avalon-city`, the college of healing's bed waits for the barge.
      - In `history/albion`, Lancelot dies at the hermitage.
    - **Midgard:**
      - In `history/midgard`: Ragnar's sons take the news, Ivar's ox-hide, and Starkad, Vikar and Hather.
      - Beowulf and Thora are in `gotar`, and Beowulf's mere is in `mahattava`.
      - Starkad is in `cursed-tomb`, Sibilja in `east-svealand`, the wolf-skins in `varulvhamn` and the hairy breeches
        in `malmohus`.
      - In `jordheim`, the founders are sung beside Starkad and Ragnar's sons.
    - **Hibernia:**
      - Mac Dathó's feast is in `ailbe` and Fergus's terms of the fords in `avartack-the-champion`.
      - Ferdiad at Ardee is in `connacht`, Medb and the Brown Bull in `cruachan-gorge`, Deirdre and the Red Branch in
        `emain-macha`, and Scáthach's bridge in `mount-collory`.
    - **Independent review** ([REVIEW-COMPANIONS-2.md](REVIEW-COMPANIONS-2.md)): 1 High, 5 Medium, 13 Low. All are
      fixed.
      - The High was older than this round: Patrick and the churchman are out of the Oisín fact and faction text in
        `history/hibernia`.
      - The Mediums were Accolon and the scabbard, the scabbard claim hedged, the Siege Perilous at the Grail
        Pentecost, Ivar crushing Sibilja, and the hound's head on the chariot pole.
      - Isolde is now Isoud the Fair, so she is not taken for Dame Ysolde.
      - Malory book 10 and book 19 are now cited.
      - The shared repetition is varied: "the songs say", "of the old songs", and the closing "that is why" device.
      - No `myth` fact needed relabelling.
      - `UT_LoreBible` was not run, because there is no dotnet in that environment.
    - **Open for the owner:** whether King Mark is Duke Marcus's son (A9 in the review), and the hired-companion
      names that match legend names.
  - **Third pass: deepening (HANDOFF 1.5)**, on branch `wt3/lore3-review`. Text was only added or reworded; nothing was
    cut.
    - **Writers:**
      - Albion, `dde65a09`: King Mark as Duke Marcus's son; the Caer Sidi labels ("the priest" no longer means Ilron);
        every Albion location and boss at its DepthReport floors; the epic summaries at 500 words; the Isle of Apples,
        the Inconnu and two letters in the Shrouded Isles; five thin minor bosses; book pages at 100 words.
      - Midgard and Neutral, `3a20336a`: the four proving fields and their overview (Cad Goddeu and Myrddin in
        Caledon, the Second and Fourth Branches in Abermenai, Thidrandi and the dísir, Keating's tests, Vinheath and
        Dunheath); Luso and six minor bosses; saga-voice chapters for Lydsyg, Dyranapur and Hurjavelen.
      - Hibernia, `7c1e1b71`: Blight, the Cursed Forest and 14 thin bosses, from the Mythological, Ulster and Fenian
        cycles.
    - **Independent review** ([REVIEW-THIRD-PASS.md](REVIEW-THIRD-PASS.md), fixes in `d9763dd5`): 1 High, 4 Medium,
      13 Low. All are fixed.
      - The High: the companion name Gráinne told of in `giant-caithor` (decision 7).
      - The Mediums:
        - Caer Sidi's toll (now 67 of 80 everywhere);
        - *De Gabáil in t-Sída* reversed (the Mac Óc tricks the Dagda; Elcmar is *Tochmarc Étaíne*);
        - Clíodhna's birds, which are not in the *Dindshenchas*;
        - the Academy's Merlin searchers in Caledon, now doubters of the Crystal Cave account.
      - The Lows include the two Ferguses, Egil's chapters, Hlöd as half-brother, Keating's third article and the
        repeated teller formulas.
      - No `myth` fact needed relabelling `original`.
      - The last "Catacombs-era" label in `hibernia-wilds` is reworded.
      - `UT_LoreBible` was not run (no dotnet).
    - **Open for the owner (done on `wt3/fourthwall`, below):**
      - the four older facts that cite "Mythic's Catacombs lore" (review X3): kept, dated "Camelot Herald (2004)";
      - adding legend names such as Gráinne to the companion-name test (#400);
      - rewording the Avalon City summary that repeats its chapter (#401).
  - **Decision 8 and the review's open items** (branch `wt3/fourthwall`, #399-#401). Text was only reworded; nothing was
    cut.
    - 468 player-visible strings in 315 files (lore JSON and the lore chat files) reworded in-world. A word-subsequence
      check of every old visible string against the new one found only the fourth-wall words themselves removed. Where a
      history fact's text lost a real-world detail (a patch number, a date, "Mythic's quest"), the detail went into its
      source note, so nothing is lost from the file.
    - Bots are "companions" (hired companions, or the realm's warbands and adventurers for gamebots), a real player is
      "you", "no script" is "no tricks", "the old guides" are "the old raiders' accounts", a real year is "the early
      raids" or "the first winter of the war".
    - Avalon City's summary is new writing at 498 words with every event kept.
    - The companion-name test covers every legend the lore tells of; Eirik is allowed as a shared given name (owner to
      confirm).
    - Tests only: `UT_LoreBible` was not run (no dotnet).
    - **Independent review** (`docs/1.2.1/REVIEW-FOURTH-WALL.md`): 137 more visible strings the first pass and its
      lint missed ("no spells or styles in the data", "spawn data", "data quests", "flagged aggressive", relations "with
      its own file" and "the region overview", "zone" in told text) are reworded, and the lint now catches them. Seven
      fight tips that contradicted their boss's code (Saeor, Sys'sro, the Afanc, Oona, Oro, Krevo, the two Princes'
      lines) now agree with it. "Companions" in a raid legend means anyone of the raid who is not you.
- **Next:**
  1. **Decision 5, second pass.** Done and reviewed (see above). Show the owner samples before merging. The list it
     worked from:
     - Midgard's Jordheim and dragon legends, which could use Starkad and Ragnar's sons;
     - Hibernia's Red Branch places, such as the Ulster fords in Connacht and Mount Collory's Dún Scáith;
     - Albion's Avalon and Grail lore, which could use Lancelot and Tristan.
  2. **Re-check the repetition.** The prose fixes left "the third company" at 68 mentions and "the priest" at 132,
     and decision 6 lowers both a little. Have a reviewer who didn't write them judge whether they read as
     repetitive, and restore more names (up to the cap) where the reviewer finds a scene that needs one.
  3. **Independent prose review.** Run one after each batch. The reviewer must not be the writer. Apply its fixes.
     Show the owner samples before merging.
  4. **HANDOFF.md step 1.5, deepening.** The third pass (above) did Albion, the proving fields, Blight, the Cursed Forest
     and the thinnest Hibernian and Midgard bosses. What is left:
     - the rest of the 484 bosses with no Mythic canon, against the DepthReport floors (`UT_LoreBible` lists them);
     - Midgard's and Hibernia's locations below the floors;
     - an epic voice for each realm beyond the three Midgard saga chapters.
     Show the owner samples from the third pass before merging, and run `UT_LoreBible` in CI or on the owner's PC.
- **Issues:** the lore still to do is filed as #399-#406 ([../OPEN-WORK.md](../OPEN-WORK.md)).
- **Reviews:** [CANON-REVIEW-HEROES.md](CANON-REVIEW-HEROES.md) and [PROSE-REVIEW-STEP4.md](PROSE-REVIEW-STEP4.md).
  Their findings are summarised above. Where they disagree with an owner decision here, the owner decision wins.
