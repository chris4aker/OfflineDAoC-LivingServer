# Living Server lore audit (seed 1119)

Reviewer stance: DAoC veteran (2001-2003) and fantasy-fiction editor. Read-only audit of the worktree at
`the repository root/`.
Lore root below = `source/server/GameServer/bots/autonomous/data/lore/`.

Scripts used (all in `docs/1.2.1/lore-audit-scripts/`):
`sample.py` (the seeded sample), `dump.py` (reader), `tics.py` (corpus counts, results also in `tics.json`), `checks.py`
(provenance, meta leakage, duplicates).

Limits: I could not fetch the original 2002 Camelot Herald pages (the camelotherald.com/more/193.php URL is not on the
fetch allowlist). "Canon" below is judged against the files' own `provenance: "mythic"` facts plus my own memory of
period lore, not against a fresh read of the Herald text.

## 1. Sample

`random.seed(1119)`: 3 bosses drawn per realm, then 5 from the rest. The 3 dragons and Legion were added on purpose.

- **Bosses (18):** golestandt, gjalpinulva, cuuldurach-the-glimmer-king, legion (forced); beran-the-supply-master,
  tower-guardian, warlord-dorinakka (Alb); ald-the-bruce, icelord-agmundr, basilius (Mid); azure-thror, paud, king-vian
  (Hib); blight, fostra-orm, allyn, luso, high-lord-baelerdoth (any). Realm count: Alb 4, Mid 5 (ald-the-bruce is filed
  under Midgard), Hib 6, Neutral 2.
- **Locations (8):** tomb-of-mithra, cursed-forest, modernagrav, jordheim, shannon-estuary, faraheim, aegirs-landing,
  forest-sauvage.
- **History (2):** hibernia, neutral.
- **Tales (6 of 90 scenes):** fireside_olcasgean, tale_govannon_mid, fireside_mortufoghus, tale_gjalpinulva,
  fireside_myrddraxis, tale_chieftain-caimheul. I also read tale_golestandt because it is the planned centrepiece.

Every story was read in full. All 8 Golestandt chapters were read, and at least 2 chapters of every boss that has
chapters (the minor bosses and plain mobs in the sample have none).

## 2. Scores

The five scores are: (a) prose, (b) voice (Herald/Mythic rather than generic AI fantasy), (c) canon first, (d)
specificity and (e) veteran appeal, each from 1 to 5. Where an item has no Mythic canon, (c) scores how faithful it is to
the game script and world data.

| Item | a | b | c | d | e | Avg | Best sentence | Worst sentence |
|---|---|---|---|---|---|---|---|---|
| bosses/golestandt.json (chapters) | 4 | 3 | 4 | 5 | 5 | 4.2 | "Camelot's armourers still forge heat-wards on Fabricius's pattern, and they will only sell them in pairs. One for her, they say, and one for you." | (story field) "Before that summer was out a company carrying Fabricius's tablet brought her down in the Bed itself, and Marshal Gareth cut the names of its nine dead into the Dragon's Walk at Prydwen Keep." It packs 4 names and 2 events into a summary sentence. |
| bosses/golestandt.json (story field) | 3 | 3 | 4 | 4 | 4 | 3.6 | "Her lair keeps scales, a dulled Roman helmet and a knight's spur, not gold." | "The Church fears the moor is spreading north; the hut circles of the moor's first people all turn their doors away from her; and the drovers no longer ask whether she can be killed, only whether she is asleep." Three chapter endings crammed into one sentence. |
| bosses/gjalpinulva.json | 3 | 3 | 2 | 3 | 3 | 2.8 | "it is bare in its bed, like the wolf's daughter." | "The svartalfar who came to the ruins, for the grey stone by their own account and to learn whether her brood could be destroyed by the word of a young scout Midgard's hunters caught, met her gaze..." The sentence is unparseable, and it is where Mythic's own account went. |
| bosses/cuuldurach-the-glimmer-king.json | 3 | 3 | 4 | 3 | 3 | 3.2 | "It did not thunder. It tore, quietly, the way old linen tears, and through the tear came wings wider than the valley, and after the wings, small cold lights in no hurry." | "In the third spring of the isles Dubthach of Ardee's hosting of a hundred and twenty met him in the ring and his court at the four doors, and the hills wept when he fell." |
| bosses/legion.json | 3 | 3 | 2 | 4 | 4 | 3.2 | "Legion is the king of the demon court under the three realms, and a court in a single body." | (legend) "Darkness Falls has no raid tactics in our raid AI yet." Developer text in a player-facing field. |
| bosses/beran-the-supply-master.json | 4 | 3 | 4 | 5 | 4 | 4.0 | "a line of the hall-folk's letters under it that the ghosts read for her. A cask burns where it stands." | "Wenna of Caer Gothwaite read onstal as supply; her clerk Garmon read it as arrangement, the plan of the bays. Both readings are kept." This is footnote material. |
| bosses/tower-guardian.json | 4 | 4 | 4 | 4 | 4 | 4.0 | "put his hand on the Guardian's knee, because he could not reach any higher, and told it to let no one up." | "the shacklers and the heavy metals and the frazzled things that came out of the workshop wrong" (a mob list in costume) |
| bosses/warlord-dorinakka.json | 3 | 3 | 4 | 4 | 3 | 3.4 | "a yard is where you keep a soldier you cannot trust indoors." | "after the night war's weapons fell he drilled his knights to stand about their warlord first" (unreadable without the Bellum file) |
| bosses/ald-the-bruce.json | 3 | 4 | 4 | 3 | 3 | 3.4 | "They are both dead; it is the only peace the frontier has ever made." | "a little way from where Hibernia's informer Fionnuala waits for Midgard's rogues" (a quest pointer inside a ghost story) |
| bosses/icelord-agmundr.json | 4 | 3 | 4 | 4 | 4 | 3.8 | "the runes do not lie, but they do not say when." | "She told him, in the forty-first winter of the Council..." An invented calendar that nobody asked for. |
| bosses/basilius.json | 4 | 3 | 4 | 4 | 4 | 3.8 | "A Mother found is a Mother who might give orders. A Mother being looked for is a people who will do whatever their king says to keep looking." | "Basilius is not a morvalt name." The not-X opener is a corpus tic (see section 3). |
| bosses/azure-thror.json | 3 | 3 | 4 | 3 | 3 | 3.2 | "It is the only entry in the burnings column that is not a burning." | "Where Dsaldor keeps the highest camp and Coseal the preaching-ground, Thror keeps the war-camp between them" (a map dressed as prose) |
| bosses/paud.json | 3 | 3 | 3 | 2 | 2 | 2.6 | "the Watchers learned to act only after it was too late, and now act against the wrong people." | "Paud and Regan are the only two who still remember what that was. Paud does not say." Withheld mystery stands in for content. |
| bosses/king-vian.json | 3 | 3 | 3 | 3 | 2 | 2.8 | "he listens more than he speaks, which in a bear is unsettling." | "The firbolg of Lough Derg say the druids have the wrong losers." (an allusion with no payload) |
| bosses/blight.json | 2 | 2 | 3 | 2 | 2 | 2.2 | "Fire. Rot. Flesh. Bone." (yell) | "It comes up out of the ground in the same order, as a controller in the soil sends it". This is script vocabulary (the encounter's controller mob) leaking into lore. The legend also repeats two of its own sentences, and its yells are duplicated. |
| bosses/fostra-orm.json | 3 | 3 | 3 | 4 | 3 | 3.2 | "in the spring it hatched in his shirt and bit him and he laughed." | "Hallveig's grandmother carried an egg north to Midgard in her pack..." A non-sequitur paragraph with a new character in the last lines. |
| bosses/allyn.json | 4 | 3 | 3 | 4 | 3 | 3.4 | "an apology makes a whip lighter to carry." | "He went round the mountain to the court's door to find out whose picks those were, and found out." (coy) |
| bosses/luso.json | 3 | 2 | 3 | 3 | 2 | 2.6 | "it grew up standing still for hours among things that stand still for hours, and took up waiting." | "the two that look like birds are the dangerous ones." A level-50 cat with two adds gets 380 words of ethnography. |
| bosses/high-lord-baelerdoth.json | 3 | 3 | 3 | 3 | 3 | 3.0 | "The Chancellor has measured it twice." | "near his end every wound he is given draws something out of the men around whoever he is fighting, into their armour first, rings softening and plates dulling from the first blow and staying so while the blows keep landing". This is the -15% armour-absorption aura read aloud. |
| locations/tomb-of-mithra.json | 4 | 3 | 3 | 4 | 5 | 3.8 | "The tomb keeps every promise made in it; that is what it is for." | "What went wrong." / "What keeps house there now." / "Why it matters to Albion." These are template section headers posing as sentences. |
| locations/cursed-forest.json | 2 | 2 | 3 | 3 | 3 | 2.6 | "The Juggernaut is the largest, the debt with legs" | "The horror horse grazes under the moon; the shadow-biting stallion grazes on shadows; the wyvern's ghost circles the place it was fed." A bestiary read out as prose, roughly 700 words of it. |
| locations/modernagrav.json | 4 | 4 | 4 | 4 | 4 | 4.0 | "its ribs standing out of the earth like the beams of a longhouse with the roof burnt off" | "older than the morvalt, older than the trolls, older than the Troll Fathers' first hall" (the "older than" triplet tic) |
| locations/jordheim.json | 4 | 4 | 4 | 5 | 4 | 4.2 | "Nobody in Jordheim lights a candle for Bodgar. Everybody in Jordheim knows somebody who has gone to the bonedancers anyway." | "The two halls still do not light their midwinter fires on the same night." It is repeated almost word for word in the legend. |
| locations/shannon-estuary.json | 3 | 3 | 3 | 3 | 3 | 3.0 | "the water still tastes faintly of hazelnut at the turn of the tide, and they do not mean it as a joke." | "the women of Ardee send for a bladder of a wolfhound and water from a dew sheerie to free him" (a quest step) |
| locations/faraheim.json | 3 | 3 | 3 | 3 | 3 | 3.0 | "Between their grounds lies a strip of wood where nothing lives and the dwarves say nothing ever will until one of the two is dead." | "after the winter of the nine children": a cross-file allusion that is never explained here. |
| locations/aegirs-landing.json | 3 | 4 | 4 | 3 | 3 | 3.4 | "The harbour-reeves say he is still deciding." | "who say that on Aegir a woman's guard is as good as a man's and twice as rude" (sitcom register) |
| locations/forest-sauvage.json | 3 | 3 | 3 | 3 | 3 | 3.0 | "the woodsmen say a beast is woken by being asked." | "The wood at the edge." / "The castle and the keeps." / "What lives in it." / "The keeper." (the header template again) |
| history/hibernia.json | 3 | 3 | 4 | 3 | 3 | 3.2 | (era) "at Mag Tuired Lugh's sling-stone puts out Balor's eye and the Fomorians are broken and go west" | The eras are index summaries plus long boss-id lists: a database, not reading. |
| history/neutral.json | 4 | 3 | 3 | 4 | 4 | 3.6 | "Twenty-nine came armed. The thirtieth came hungry." | "the eleven left standing understood that they were no longer at a feast but on the menu." A pun that breaks the tone. |
| tale_golestandt (tales.lines) | 4 | 4 | 4 | 4 | 4 | 4.0 | "camelot's armourers still cut that ward into the rim of a shield. always twice. once for her and once for you" | "?E: ok that part still gets me / fine, the pairs bit is good, i admit it". The script praises itself. |
| tale_gjalpinulva | 3 | 3 | 2 | 3 | 3 | 2.8 | "?E: and that is still the only place she bleeds. ask me how i know" | "C: good telling {name_b}. the fire feels warmer now" (saccharine) |
| tale_govannon_mid | 4 | 4 | 3 | 4 | 4 | 3.8 | "?E: at half. most of his health back. i have the scars" | "B: like every blow we ever landed went to somebody else" |
| tale_chieftain-caimheul | 4 | 4 | 3 | 4 | 4 | 3.8 | "B: the man in front meets the shield and reels. the man who wont reel he mocks until he comes on careless" | "B: when he casts the shield aside he is hurt deep and thinks you are about to break. be ready then" (a fight tip in the teller's mouth) |
| fireside_olcasgean | 3 | 4 | 3 | 3 | 3 | 3.2 | "B: so we are standing in his mouth. thank you for that" | "A: {name_b} has the whole tale. the two trunks part is worth hearing" (an advert) |
| fireside_mortufoghus | 3 | 4 | 3 | 3 | 3 | 3.2 | "and getting flung by her counts as an honour to them" | "A: {name_b} tells it better, with the digger and the spade" (the same advert closer) |
| fireside_myrddraxis | 3 | 4 | 3 | 3 | 3 | 3.2 | "eat and be whole, eat and be many, eat and be ours" | "A: {name_b} has the full one in faction" (the same closer again; 7 scenes use it) |

**Sample means:** bosses 3.3, locations 3.4, histories 3.4, tales 3.4. Overall about **3.3 / 5**.

## 3. Tics across the whole corpus

Scope: every `story` and every chapter in 578 bosses and 108 locations. That is 1,531 texts (686 stories, 845 chapters),
**523,935 words** and 22,685 sentences. Boss stories average 302 words; 73 of 578 boss stories and **92 of 108 location
stories exceed the schema's 400-word cap**. Chapters total 220,539 words.

| Pattern | Hits | Files | Per 10k words | Example |
|---|---|---|---|---|
| "the X say" attributions | **1,157** | 452 | 22.1 | "the Sylvan say" 103, "the Inconnu say" 83, "the dwarves say" 75, "the Valkyn say" 39, "the ghosts say" 38, "the sculptors say" 36, "the imps say" 35, "the companies say" 35 |
| "still" | 1,116 | 414 | 21.3 | "His calm letters to the Constable still come up from the tower" |
| "nobody" | 678 | 345 | 12.9 | "What Aidon promised the Summoners, nobody alive knows." |
| "the only" | 522 | n/a | n/a | "it is the only" 62; the 4-gram "it is the only" is 86, "is the only one of the" 26 |
| "has never" | 418 | 253 | 8.0 | "The bird has never been asked." |
| "they say" | 367 | 202 | 7.0 | |
| "whether" | 223 | 150 | 4.3 | |
| "the Inconnu ... say/said" | 199 | 37 | 3.8 | the Albion all-purpose explainer |
| "returns / comes back / rises again" | 164 | 110 | 3.1 | the respawn turned into myth: 69 boss files |
| "older than" | 151 | 103 | 2.9 | "older than the morvalt, older than the trolls, older than..." |
| "never once" | 140 | 120 | 2.7 | |
| "ever since" | 137 | 117 | 2.6 | |
| "the way a X ..." simile | 132 | 107 | 2.5 | "the way old linen tears" (good); "the way a man touches a ring" |
| "not X, but Y" | 75 | 68 | 1.4 | |
| "X is not Y. It is Z" | 75 | 64 | 1.4 | "It was not anger. It was the look of someone who had outlived ev..." |
| "Why it matters / X matters because" | 62 | 62 | 1.2 | **44 of 108 locations** close on it; "why it matters to albion" is a 5-gram 26 times |
| "the X say A; the Y, B" | 60 | 50 | 1.1 | dual attribution with a semicolon |
| ", not X." contrast tail | 55 | 46 | 1.0 | "A keeper-tree is chosen, not planted." |
| "nobody/no one knows" | 55 | 48 | 1.0 | |
| "did not come back / not heard of again" | 46 | 41 | 0.9 | |
| lair immunity told as lore | 42 | 29 | 0.8 | "no blow lands", "on ground not his", "the granite takes every blow" |
| "nobody agrees / accounts differ" | 28 | 25 | 0.5 | |
| "the drovers say" | 23 | 8 | 0.4 | 19 uses of "drovers" in golestandt.json alone |
| "for a breath" | 20 | 13 | 0.4 | |
| "some say" | 20 | 13 | 0.4 | |
| em dash / en dash | **0 / 0** | 0 | 0 | scrubbed, but the colon (2,159) and semicolon (1,788) do that work instead |
| "X, Y and Z" lists | 784 | n/a | n/a | |

- **Most common 5- and 6-grams:** "the druids of tir na nog" 68, "the druids of lough gur" 52 ("...say" 25), "the skalds of
  jordheim say" 36, "the readers of caer gothwaite" 32, "the cast out sculptors say" 26, "why it matters to albion" 26,
  "is the only one of the" 26, "on the night of the rising" 16, "the feud of the blade and the hook" 16, "the first company
  of albion to" 14, "the fishermen of the avalon shore" 13, "the drovers of the cornish road" 12.
- **Openings:** "The first company" opens 21 texts and "Dame Ysolde's company" opens 9. Dame Ysolde, an invented Albion
  raid leader, is named **48 times**, Brother Cadoc 56, Brother Odran 28 and Sir Cador 23. A small invented cast "first
  killed" much of Albion: 61 boss files carry a first-kill credit paragraph.
- **Closings:** 350 of 1,531 texts end on a sentence containing never (150), still (117) or nobody (84). The usual last
  beat is an attributed folk saying or an "it still waits / returns" line.
- **Sentences:** mean 23 words, median 21, and 2,984 run over 40 words. The long ones are summary sentences that chain
  3 or 4 events with semicolons.
- **Tales:** all 39 tale scenes follow one script: A asks, C hushes (41 of 90 scenes say hush/shh/quiet), E has heard it
  before, B says "fine, once" (33 scenes), then a question at ~8, a question at ~25, awe, thanks ("every time" 8). The
  firesides end on "{name_b} has the full one / tells it better" (7). Every one of the 1,124 chat lines keeps the
  lower-case 2003 chat voice. The chat voice itself is the best voice work in the project.

**One tone for every boss.** Yes. A level-80 dragon, a level-50 scimitar cat (Luso), a treant with two lines (Paud) and a
crop disease (Blight) all get the same treatment: a quiet folk-ethnographer voice, two or three groups of locals who
"say" competing things, a game mechanic recast as custom, an invented first company, and an ending that is a rumour of
return. Within one file it is often lovely. Across 578 files it is a drone.

## 4. Overclaim, provenance and contradiction

1. **The provenance label "mythic" is wrong most of the time.** There are 262 facts labelled `mythic`, and **214 of them
   are sourced to Allakhazam (173), Prima's eGuide (19), GameBanshee (15), RPGamer, or the fandom wiki**. Examples:
   - gjalpinulva.json labels as "mythic" a 2006 player comment (she "looks like a dragon with a wolf's head"), a 2002 resist
     bug kill on Gawaine, and a 2009 patch.
   - basilius.json and faraheim.json label an Allakhazam bestiary listing as "mythic".

   These are game or legend facts. Only about 48 facts are actually Mythic's words (Herald, patch notes). The "canon first"
   claim in docs/LORE.md rests on this label, and the label does not hold.
2. **About 84% of bosses have no Mythic canon at all.** 484 of 578 boss files carry no `mythic` fact, mislabelled or not.
   The lore bible is overwhelmingly original fan fiction. That is fine, but it must be presented that way.
3. **Mythic's account is buried in the flagship dragon file.** In gjalpinulva.json, Mythic's own words (a svartalf scout:
   "a daughter of Fenris Wolf ... by some huge unknown creature", her loyal brood of upright scaled wolves) are pushed to
   third place behind invented and borrowed mothers (Norse Gjalp, Iarnvidiur). The scout ends up as a subordinate clause in
   an unparseable sentence. Golestandt and Cuuldurach do better: Articus, the Duke's son, Arward's journal and Tyldifyl
   each lead their chapter.
4. **Legion has two origins that contradict each other.**
   - legion.json ("The First Meal"): *two equal princes*, one ate the other.
   - history/neutral.json ("The Feast of Thrones") and high-lord-baelerdoth.json: *thirty lords at a feast*, the thirtieth
     came hungry, eleven knelt.

   legion.json never mentions the Feast. A DF veteran reading both will see the seam.
5. **A player-talk point is stated as canon.** cuuldurach-the-glimmer-king.json says "He is the only he among the three
   realm dragons" as narration. golestandt.json's own facts admit the Golestandt script says "his" and that her being
   female is a house decision from period player talk. That is honest in the facts, but it is stated as settled in the
   prose.
6. **Developer and meta text sits in player-facing `legend` fields: 74 files.** Examples:
   - legion.json: "Darkness Falls has no raid tactics in our raid AI yet." This also sits oddly with AGENTS.md putting the
     DF raid AI in 1.2's scope.
   - beran-the-supply-master.json: "Untested in game with bots: whether the raid AI follows a drag out of the room."
   - agidad.json: "the template the server uses sets aggro 200".
   - celain-elrich.json: "(untested in game)".
   - behemoth.json: "in this server's code".
7. **Copy-paste debris: 55 boss files have duplicated yell lines or doubled legend sentences.** 11 of them are in the
   Cursed Forest (blight, cailean, develin, flame, giant-caithor...). develin.json's legend says the pooka-farming
   instruction twice and has two "Superstition:" lines. The legends also carry visible template labels ("Superstition:"
   in 39 files, "Nicknames:" in 28).
8. **Mechanics are retold as lore, nearly word for word.**
   - high-lord-baelerdoth.json's story is the aura tooltip in prose.
   - blight.json's "as a controller in the soil sends it" is script vocabulary.
   - Mechanic recasting works when it is an image (Golestandt's paired heat-wards for "stack heat resist twice", "the
     zone crashed" as "she did not deign to rise"). It fails when it is a paraphrase.
9. **Invented specifics are stated as plain fact.** Examples: "the forty-first winter of the Council", "the third spring
   of the isles", Marshal Gareth's Dragon's Walk, and "the first company... nine dead". These are fine as original
   writing. The trouble is that the Herald-styled channels (book pages, chronicle) will present them with the same
   authority as Arward's journal, and the prose never marks the seam.

I found no outright contradiction of the game scripts in the sample. Yells quote the scripts correctly (Golestandt's
waking roar, Cuuldurach's warning, Legion's "Your soul give me new strength"), and the class-taunt and lair-immunity
behaviours are honoured.

## 5. Verdict

**Overall grade: C+ (3.3/5).** This is not filler. It is competent and consistent writing, often clever, with no em
dashes and little purple prose, and it is noticeably better written than Mythic's own Herald copy. But it is not Mythic's
voice. Mythic's lore was plainer and pulpier (a scribe's journal, a dragon, the realm's knights), while this is a
literary folk-ethnography register (Susanna Clarke by way of Gene Wolfe). That register repeats so mechanically across
half a million words that a reader who knows the signs will smell a model by the third file. It reads AI-made in bulk,
human-quality in the best single pieces.

**Reddit for DAoC veterans: yes, but only as a curated showcase, never as "578 bosses of lore".**
- Post 2 or 3 hand-trimmed excerpts.
- Say plainly that it is original fan writing built around Mythic's few lines.
- Fix the `mythic` mislabels before anyone looks at the files.
- Strip the dev text out of the legends.

A veteran who opens a random minor boss will get Luso or Paud, and a veteran who opens legion.json will find "our raid
AI".

**Golestandt as the centrepiece: the right choice, the wrong field.**
- The chapters are the best writing in the corpus:
  - "The Name in the Journal" (the table still laid; the journal falling out of a clear sky; giants that "were looking
    south").
  - "The Ward-Eater" (heat-wards sold in pairs).
  - "Every Trade Its Taunt" (the class taunts as Hywel's stolen roll; the children's stare game).
- They also contain the best veteran in-jokes in the project: the 350-man zone crash recast as "the dragon was not at
  home", and "She called one cleric crunchy."
- The `story` field is a 471-word précis (over the 400 cap) with 33 distinct proper names. It reads as a recap of the
  chapters, not a story. Do not post it.
- Post chapter 2 plus the ward paragraph from chapter 4, cut to about 600 words:
  - Drop the Inconnu interjections and two of the three name etymologies.
  - Cut "drovers" from 19 mentions to 3 or 4.
- The in-chat tale_golestandt is a good second exhibit, because it shows the feature working. Remove the self-praising
  "the pairs bit is good, i admit it" line first.

**Best 3 (feature-worthy):**
1. `bosses/golestandt.json`, chapters 2, 4 and 6. Mythic's canon leads, the invented additions grow out of mechanics
   veterans remember, and the images (singed sheep "in a pattern like the grain of the rock", the laid table) are
   concrete and eerie.
2. `locations/jordheim.json`. It is the warmest and most specific piece. It gets the god-houses right (Modi's berserkers,
   Hel's spiritmasters, Bodgar and Kelgor arriving with SI), and the Bodgar candle line is the kind of line a real writer
   lands.
3. `history/neutral.json`, the Feast of Thrones era ("Twenty-nine came armed. The thirtieth came hungry."). It is a clean
   mythic structure that explains DF's Roll of Precedence and the boss names. It is only feature-worthy once legion.json
   is reconciled with it.

Runners-up: `bosses/tower-guardian.json` ("let no one up"), `bosses/icelord-agmundr.json` (the runes "do not say when")
and `bosses/beran-the-supply-master.json` ("A cask burns where it stands").

**Worst 3:**
1. `bosses/blight.json`. It has script vocabulary in the lore ("controller"), duplicated yells, a legend that says the
   same tip twice, and vague "the Sylvan call that its true form. Nobody has asked them how they know."
2. `locations/cursed-forest.json`. About 700 words of bestiary in costume, one mob per clause, and the Cursed Forest boss
   batch it heads holds 11 of the 55 duplicate-yell files.
3. `bosses/luso.json`. It is the clearest case of the one-tone problem: a scimitar cat with two adds gets an origin, a
   Valkyn etymology, a skogsra gait analysis and a moral. The weakest flagship is `bosses/gjalpinulva.json`, where the
   canon is buried and the "mythic" sources are player comments.

## Top 5 systemic problems

1. **The provenance is not trustworthy.** 214 of 262 facts labelled "mythic" are third-party, and 84% of bosses have no
   canon at all. The "canon first" promise is not met.
2. **One voice, one template, everywhere.** There are 1,157 "the X say" attributions. Every boss has a first-company
   paragraph, a return ending and a "nobody knows" beat, and locations use a header-sentence skeleton with "Why it
   matters".
3. **Summaries posing as stories.** The `story` fields are précis of the chapters: 33 names in 471 words, semicolon
   chains, mechanics paraphrased. 165 stories are over the length cap.
4. **Player-facing hygiene.** 74 legends contain dev or meta text, 55 files duplicate lines, and template labels show
   ("Superstition:", "Nicknames:").
5. **Continuity seams.** Legion's origin contradicts the history and Baelerdoth. There are unexplained cross-file
   allusions ("the winter of the nine children", "the night war's weapons fell"), and a small reused cast of invented
   heroes (Dame Ysolde 48 mentions, Brother Cadoc 56).

## Fix plan for 1.2.1, in order

1. **Hygiene (1 day, scriptable).**
   - Strip dev and meta sentences from all 74 legends. Add a lint rule for `raid AI|untested|server|bots?|Living Server`
     in legend.
   - Deduplicate the yells and legend sentences in the 55 files.
   - Fold the "Superstition:" and "Nicknames:" labels into prose or into `botLines`.
2. **Provenance (1 day).** Relabel every `mythic` fact whose source is not a Mythic/Broadsword publication: to `game`
   (bestiary and patch data) or `legend` (player comments, bug kills). Make the checker reject `mythic` without a
   camelotherald or darkageofcamelot source. Then add a short "what Mythic actually wrote" note to LORE.md, and quote it
   honestly in the Reddit post.
3. **Fix the flagships before the post (2 days).**
   - Rewrite gjalpinulva's story so the svartalf scout's account leads and the Gjalp and Iarnvidiur mothers follow as
     skald variants.
   - Reconcile legion.json with the Feast of Thrones, making the imps' two-prince tale explicitly the imps' lie.
   - Recast Cuuldurach's "only he" as what the Cullen watch says.
   - Cut Golestandt's `story` to 300 words with at most 12 names, and prepare the trimmed chapter-2/4 excerpt.
4. **Tighten the tics (ongoing, by realm).**
   - Set a budget of at most 2 "the X say" per 300 words and at most 1 "nobody/never/still" closing per file.
   - Ban "Why it matters" and the header-sentence openers in locations.
   - Run a counter (`tics.py`) in CI as a warning, with per-file thresholds.
   - Start with the 92 overlong location stories, then the 73 overlong boss stories.
5. **Tier the effort (1.2.1 and 1.3).**
   - Minor and plain-mob bosses (176 minor, plus most of the 241 untiered) should get 60-120 words: one image, one fact,
     no ethnography. Cut Luso-style origin stories to a single line.
   - Spend the saved budget on the 157 epics, and diversify their voices: give Midgard skald-terse prose, give the
     Herald-styled book pages Mythic's plainer chronicle register, and keep the folk register for Hibernia.
   - Retire or rotate the reused hero cast (Dame Ysolde, Brother Cadoc).
