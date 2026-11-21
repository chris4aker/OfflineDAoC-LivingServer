# Canon review: the nine legendary heroes (branch `origin/lore/step4-heroes`)

Reviewer stance: independent and neutral. Nothing was edited, committed or pushed. Branch reviewed at `5ea931052`.
Paths below are relative to the repo unless they are absolute. `L/` means
`source/server/GameServer/bots/autonomous/data/lore/`. "Archive" means
`era-work/text/`. "World DB" means the 0.37b
fresh world `/mnt/user-data/uploads/playable-v0.37b-fresh--runtime/data/opendaoc.sqlite3.db`, opened read-only.

Labels: **verified** (quoted from a file or URL), **evidence** (points one way but is not decisive), **inference**
(my reasoning), **uncertain** (could not check).

---

## Recommendation summary

1. **Canon: Arthur is dead at 1.65, and the Round Table belongs to a lost age.** Camelot is Arthur's city, but the
   game's Camelot is the realm *after* him. Treating the Albion three as past legends is correct. No change in framing
   is needed.
2. **Albion's three heroes are all US server names** (Galahad, "Gawaine", "Bedevere"). The risk is real but
   manageable. **Keep all three and embrace the overlap deliberately**, with two rules:
   - spell the knights *Gawain* and *Bedivere* and the servers *Gawaine* and *Bedevere* (the corpus already cites
     "the Gawaine server");
   - never write "on Galahad" / "from Galahad" in a sense that could mean the server.
3. **Factual fixes are needed before ship** (section 6):
   - Aslaug is wrongly called a shield-maiden.
   - The "little pigs" line is credited to the Krákumál; it comes from the saga.
   - The Oisín–Patrick dialogue is credited to the *Acallam* (its teller there is Caílte).
   - HEROES.md cites Geoffrey for Bedivere as "the last survivor", but in Geoffrey he dies before Camlann.
   - The apocalypse.json fact that puts Bedivere among the seven of Caer Sidi is marked `myth`; it is invention.
   - Bedivere's `places` should include `avalon-marsh` (the game's Excalibur lake).
4. **Canon frictions to soften:**
   - Saint Patrick in DAoC Hibernia, which has no Church. Suggest "a churchman out of Albion".
   - Ragnar's Northumbria/Ælla, a 9th-century English kingdom on Albion's own island.
   - "Arthur's sister Morgan" on the Avalon barge, when the realm's Morgana is its living enemy. Acknowledge it rather
     than leave it silent.
5. **More heroes: add fewer than asked.** The role-gap map (section 5) supports:
   - **Albion:** Pwyll (the Arawn alliance) and Taliesin (the true voice of Caer Sidi).
   - **Midgard:** Beowulf (giant-kin and the mere) and Sigmund with Sinfjötli (the wolf-skins).
   - **Hibernia:** Nuada (the maimed king) and Deirdre (the broken safe-conduct).

   Each fills a gap the current nine leave open, and none is a server name. Do **not** add more Round Table knights:
   every remaining famous one (Lancelot, Percival, Bors, Kay, Tristan, Pellinor, Palomides, Gaheris) was a live
   server, and the gaps they would fill are either covered or better filled by Welsh figures.

---

## 1. Mythic's canon at 1.65: Arthur, the Round Table, Camelot

### Verdict
Arthur is **dead**. The game is set in the era *after* him. The Round Table is not a living court in any official
text I found: it is the age the realms are the "remnants" of. Albion's Camelot is Arthur's city as it stands after his
death, still Albion's capital and still loyal to his memory.

### Decisive lines (verified)
| Quote | Source |
|---|---|
| "For many years now **since the death of Arthur**, the remnants of his realm have been locked in a bitter stalemate." | ToA overview, "The Finding of Atlantis". Archive `2-patch-notes__www.camelotherald.com__547635be60e502bf_20050428113652.txt`; the repo already cites the same text as camelotherald.com/more/1131.shtml (2003-10-23) in `L/history/albion.json` |
| "Paladins are holy men who vow to defend the honor of the Church of Albion in the name of **Arthur Pendragon, the late King of Camelot** and leader of the realm of Albion." | Herald class page, archive `2-patch-notes__www.camelotherald.com__795472d7caccf6e1_20050209114934.txt` |
| "The Britons are the human inhabitants of Albion, the kingdom that **Arthur founded and kept intact for his reign. Now, however, it is starting to decline**..." | "Official DAoC Briton History", archive `3-builds-specs__www.classesofcamelot.com__50bc29f1f2a2d7f8_20030823000857.txt` (captured 2003-08-23, before 1.65) |
| "...set in the **era after the reign of King Arthur** when three realms were struggling for power." | Classes of Camelot FAQ, archive `6-mechanics__www.classesofcamelot.com__34927d5adb43cc6a_20050306170159.txt` |
| "The Britons are the people of Camelot, and the **original subjects of King Arthur**." | Herald race page, archive `2-patch-notes__www.camelotherald.com__95c7c35637494fa6_20050209031647.txt` |
| "It was no accident that Arthur chose that very spot for the location of his city..." | "The Lore of Camelot: The Aqueducts of Albion", archive `2-patch-notes__www.camelotherald.com__f22c860b910856bd_20050517040822.txt` |
| Saracens descend from "**Sir Palomides, one of Arthur's most devoted knights**" | archive `3-builds-specs__www.classesofcamelot.com__50bc29f1f2a2d7f8_20030823000857.txt` |
| The Academy "founded by Merlin, sage and advisor to King Arthur"; the Church's "Holy Grail symbol can be found in all villages and towns" | same file |
| "It is set in the period after King Arthur's death, when his kingdom has split into three realms." | [Wikipedia, Dark Age of Camelot](https://en.wikipedia.org/wiki/Dark_Age_of_Camelot) |
| The people of Albion "still owe allegiance to the throne of the late King Arthur"; "after Arthur fell, each of the world's powerful relics ... were separated among the three realms." | [GameSpot, DAoC Updated Preview](https://www.gamespot.com/articles/dark-age-of-camelot-updated-preview/1100-2799868/) |

The only official knight of the Round Table named as such is Palomides, and only as an ancestor. That is **evidence**
that Mythic treats the knights as the past.

### Counter-evidence (evidence; it does not overturn the verdict)
- In Morgana's encounter script (`source/server/GameServer/scripts/namedmobs/Albion Named Mobs/Classic Zones/MorganaBechardSilcharde.cs:93`)
  she says: "I expected a whole army leaded by my brother Arthur..." Read literally, that implies Arthur is alive.
  - The OpenDAoC text is very likely taken from live logs, but that is **uncertain**.
  - It is one taunt against every descriptive source, and a villain can taunt a dead brother's realm.
  - Treat it as a known in-game inconsistency, not as canon.
- `Academy50.cs:1209` (Ferowl): "she's the evil sister of King Arthur, tried to take his throne". The tense there is
  neutral.

### Arthurian figures in the game world (world DB and scripts)
| Figure | What it is | Status |
|---|---|---|
| **Morgana** | Albion level-50 epic encounter at the Lyonesse tower; she cannot be attacked, swears to return | verified (script above; `L/bosses/morgana.json`) |
| **Lady Nimue** | Quest-giver in Camelot Hills (Mob row, region 1, level 95) for "Wizard Lost", "Legend of the Lake", "Forged Excellence", "Barbaric Tales", "Heart of Albion" and "Arc of Ages" | verified in world DB (DataQuest 20007, 20419 and others). These are the Living Server's classic-quest rows (`Tests/UnitTests/UT_ClassicQuestsConfig.cs`); how faithfully they match live is **uncertain** |
| **Lady of the Lake** | Night-time quest figure in Avalon Marsh. The quest text: "We must find [Excalibur], and the Lady of the Lake may know where it lies." | verified (DataQuest "Legend of the Lake") |
| **Merlin** | Not an NPC. He is missing: "Ask Olorustos ... about Merlin", "Ask Magess Islia about Merlin being on another plane" (Wizard Lost). Merlin's Tower in Camelot is a place. The Catacombs lore (trapped in a crystal) is 2004–05, after 1.65, though the repo uses it. | verified (DataQuest 20007; archive "The Discovery of the Dark Spire") |
| **Uther, Ambrosius** | "Shade of Uther Pendragon" and the Shade of Ambrosius in the Stonehenge Barrows (region 20) | verified (Mob table) |
| **Mordred** | "Wrath of Mordred" (level 71, Llyn Barfog): his anger, not the man | verified (Mob table, `L/bosses/wrath-of-mordred.json`). Whether it is live 1.65 content is **uncertain** |
| **Green Knight** | Forest Sauvage epic (`OFGreenKnight.cs`) | verified in data. Live and 1.65 provenance **uncertain** (the "OF" prefix suggests a classic-frontier reconstruction) |
| **Excalibur, Merlin** | Albion's relics, "Scabbard of Excalibur" and "Merlin's Staff" (`keeps/Relics/GameRelic.cs:665-670`); also Castle Excalibur and Castle Myrddin keeps | verified |
| **Arthur** | Five world-DB rows named "Arthur" (GameNPC, model 2351, level 75, flag 16) in Camelot City, Tutorial, Atlantis (two) and Cathal Valley | They exist in the data. They are **not 1.65 canon**: two are in post-1.65 regions, and the model and placement look like OpenDAoC or Atlas additions (inference). Their source is **uncertain**. Recommend not citing them |
| **"Mordred, Lord of Battle"** | Five event NPCs (`DOL.GS.LordOfBattle`) in Brimstone Caverns, Temple of Twilight, Atlantis and the Midgard Event region | not canon (custom event class) |

**Key link (inference):** canon keeps the *scabbard* in Albion's relic temple while the *sword* is lost in a lake the
Lady of the Lake knows. That is Bedivere's throw. The game sets up exactly the legend the Bedivere profile tells.

---

## 2. Are Galahad, Gawain and Bedivere correctly framed?

**Verdict: yes.** "Legends of the past, told about" matches canon. One refinement: the phrase "knights of Arthur's
court, now dead with him" is not quite right. In Malory:
- Galahad dies at Sarras **before** the fall (Book XVII).
- Gawain dies of Lancelot's reopened wound at Dover, **before** Camlann (Book XXI, ch. 2).
- Only Bedivere outlives Arthur (Book XXI).

The canon-fitting frame is: **the knights of the lost Round Table, whose fellowship broke with the Grail quest and
ended at Camlann. Albion's knights still swear by them, but no one living sat with them.** The profiles already speak
in that voice ("Albion's knights still say...", "the old ones add that Camelot never filled that seat again").

### Against our own Albion timeline (`L/history/albion.json`)
- Era 3, "The Old Kingdoms (Uther and Arthur)": "Arthur was king in Camelot ... and fell at Camlann to Mordred."
- Era 4, "The Dark Age (after Arthur: the realm wars)": "After Camlann the three realms went to war."
- All nine hero `events` for Albion sit in era 3. **No contradiction.** The profiles never put a knight in a
  present-day scene.

### Frictions (fix or acknowledge)
1. **Morgan on the barge.** The Bedivere profile: "queens waited, among them Arthur's sister Morgan."
   - Malory has this, and it is accurate to the source.
   - But in our Albion, Morgana is the living enemy who "wanted her brother's throne" (era 3; `L/bosses/morgana.json`).
   - A veteran will read Morgan = Morgana and wonder why she is ferrying Arthur to healing.
   - Either drop the name ("three queens in black hoods"), or embrace it with one line of the realm's unease (marked
     `original`). "Morgan Le Fay" was also a server (section 3).
2. **Bedivere's `places`** are `["llyn-barfog", "avalon-isle"]`. In the game, Excalibur's lake is **Avalon Marsh**
   (Legend of the Lake; `L/locations/avalon-marsh.json`: "your king's sword went into a lake, and they want it back").
   Add `avalon-marsh`. Llyn Barfog is Mordred's Wrath and the afanc; keep it only if a story there needs Bedivere.
3. **Invented deeds marked `myth`.**
   - `L/bosses/apocalypse.json`, fact: "Later tellers count Sir Bedivere ... among those who came home [from Caer
     Sidi]". Provenance is `myth`. *Preiddeu Annwfn* names no survivor, and I know of no tradition that does. It
     should be `original`; better, see Taliesin in section 5.
   - `L/bosses/soul-reckoner.json` (Bedivere "stayed on the field at Camlann to count the fallen") is story text, not
     a fact. That is acceptable as embellishment, but it is not Malory.
4. **The Green Knight** is a present-day boss (`L/bosses/green-knight.json`), while Gawain is a legend. That works,
   and is good: the knight outlived his challenger. The boss file and the hero profile agree.

---

## 3. Server-name collisions

### Evidence
The Camelot Herald's server-status widget, parsed from every archive capture:
- **US list at 1.65 (captures 2003-09-03 onward):** Bedevere, Bors, Gaheris, Galahad, Gawaine, Guinevere, Igraine,
  Iseult, Kay, Lancelot, Merlin, Mordred (PvP), Morgan Le Fay, Nimue (RP), Palomides, Pellinor, Pendragon (Test),
  Percival (RP), Tristan.
- **Andred (PvP):** present 2002-07 to 2003-04, gone by 2003-05.
- **Added in 2005:** Hector, Ector, Gareth, Lamorak.

Example capture with populations: archive `2-patch-notes__camelotherald.com__8a5edab282632443_20020921164846.txt`
("... Bedevere 830 ... Galahad 898 ... Gawaine 916 ...").

**European (GOA) English servers:** Excalibur and Prydwen.
- Evidence: the FreddysHouse thread "[Return to Prydwen or Excalibur?](https://forums.freddyshouse.com/threads/return-to-prydwen-or-excalibur.105679)",
  and the repo's own `RealmRaidOpenWorld.cs:70` ("on Excalibur (FreddysHouse, May 2002)").
- Other GOA names (French and German servers) are **uncertain**; I could not verify a list.

The corpus already uses a server name in lore: `L/bosses/gjalpinulva.json`, "killed in about 30 seconds on the
Gawaine server".

### Roster judgement
| Name | Server? | Veteran reads it as a server first? | Recommendation |
|---|---|---|---|
| Sir Galahad | **Yes**, "Galahad" (2002–2005) | Moderate. A bare "Galahad" in chat reads as the server; "Sir Galahad" with the Siege Perilous does not | **Keep, embrace.** Always "Sir Galahad" in hero text; never "on Galahad" |
| Sir Gawain | **Yes**, spelled "Gawaine" | Low to moderate; the spelling differs | **Keep.** Lock the spellings: knight *Gawain*, server *Gawaine* |
| Sir Bedivere | **Yes**, spelled "Bedevere" | Low; the spelling differs, and it was a smaller server | **Keep** |
| Sigurd, Ragnar Lodbrok, Hervor | No | None | Keep. World-data collision: "Ragnar", an armour merchant in Midgard region 100, and "Brynhild", the Realm Exchange broker in Jordheim (Sigurd profile). Minor |
| Cú Chulainn, Fionn mac Cumhaill, Oisín | No | None | Keep. Note that "Fionnuala" (a druid) exists and the name counter must not confuse the two |
| Lancelot, Percival, Bors, Kay, Tristan, Pellinor, Palomides, Gaheris, Igraine, Iseult, Guinevere, Nimue, Merlin, Mordred, Morgan Le Fay | **All servers** | High for Lancelot, Merlin, Mordred and Guinevere (large or famous servers); moderate for the rest | **Do not add as heroes.** Merlin, Mordred, Morgana and Nimue are already NPCs or bosses with their own canon |
| Lamorak, Gareth, Ector/Hector | Servers from 2005 (after this era) | Moderate for veterans | Avoid |
| Excalibur, Prydwen | GOA servers; also canon objects (the relic scabbard, Prydwen Keep) | Low in context | Already in use; fine |

Why embracing works (inference): Mythic named the servers *after* these knights. A veteran who sees "Sir Galahad, who
sat the Siege Perilous" gets a double resonance, not confusion. Confusion arises only with bare names in
server-shaped phrases.

---

## 4. Midgard and Hibernia

### Midgard
- **The gods are alive and active in DAoC's present; Ragnarök is still to come.**
  - Midgard's classes are trained by the *Houses* of the gods ("Viking House: House of Thor", "House of Odin";
    archive `2-patch-notes__www.camelotherald.com__484ced958d952876_20050209201550.txt` and `...3c37dacba9fe1c6c...`),
    and Hel's house trains Warlocks.
  - Valkyn gods "have lain dormant for many centuries ... now ... the gods are again stirring" (archive
    `6-mechanics__www.classesofcamelot.com__0c17df9af0d25608_20051210072826.txt`, captured 2005).
  - Midgard holds "Thor's Hammer" as a relic (`GameRelic.cs`).
  - Our own timeline puts the binding of Fenrir and Loki in the past, and the bosses taunt about a future Ragnarök
    (`L/bosses/hati.json`, `L/locations/muspelheim.json`).
- **Where the heroes sit:** all three are in era `mid-era-jarls`, before the realm war. That suits Sigurd (Völsung
  legend, a memory of the 5th century) and Hervor (Gothic and Hunnic wars, 4th century).
- **Ragnar is the anachronism.** Historically he belongs to the 9th century, about 300 years after any Arthurian
  date, and his death comes at the hands of Ælla of Northumbria, an English kingdom on Albion's own island.
  - DAoC is mythic, so this is friction, not error.
  - Recommendation: keep Ragnar, but write his last voyage as "a king over the western sea" (marked `original`), or
    accept the anachronism knowingly.
  - If strictness matters, Starkad is a period-safe swap with a similar defiant-old-warrior shape (section 5).

### Hibernia
- **Canon present:**
  - The old powers have withdrawn but are remembered. Keep names honour Lugh and the Dagda (Dún Lámfhota, Dún
    Dagda); the relics are Lugh's spear and the Dagda's cauldron.
  - In our lore the elves "raised the Veil and went behind it" (`L/locations/tir-na-nog.json`).
  - Tír na nÓg is the capital city, named after the otherworld. The lore handles this well.
  - DAoC Hibernia has **no Church**; Christianity is Albion's alone (inference from the class and guild structure
    and the absence of any Hibernian church).
- **Hero eras:**
  - Cú Chulainn (Ulster Cycle, roughly 1st century) and Fionn (Fenian Cycle, roughly 3rd century) sit well in
    `hib-the-old-families`.
  - Oisín's return falls in Patrick's 5th century, just before DAoC's present. That chronology is neat.
- **Friction:**
  - The Oisín profile and event name **Saint Patrick** and psalms.
  - Recommendation: "a churchman out of Albion took him in". Patrick was a Romano-Briton, so this is honest and ties
    into Albion's Church. Mark the adaptation `original`.
  - Alternatively, keep "Patrick" and accept one Christian intrusion in Hibernia.

---

## 5. Role-gap map and proposals

### Method
- Machine-tagged every story of 400 characters or more in `L/bosses` and `L/locations` (197 Albion, 217 Midgard,
  209 Hibernia) for 20 recurring shapes.
- Then read the openings of 48 sampled stories (16 per realm) plus about 15 targeted ones.

Files read include:
- **Albion:** hadrians-wall, caldey, krondon, avalon-city, avalon-isle, green-knight, moran-the-mighty, cauld-lad,
  golestandt, lord-caddug, malefact, cnieda, eralyne, bane-of-hope, the-swett-household,
  shade-of-ambrosius-aurelianus, gwyddneau, lyonesse, legendary-afanc, caer-sidi, sister-blythe, apocalypse,
  soul-reckoner, funus, morgana.
- **Midgard:** midgard-housing, delling-crater, faraheim, vanern-swamp, vale-of-mularn, ydenia-of-the-seithkona,
  mokerkialfi, mortufoghus, vasrem, mokkurvalve, jarl-ormarr, apologet-nesnuir, grindvaktare, queen-kula, tebui,
  icelord-skuf, varulvhamn, mahattava, ulf.
- **Hibernia:** caillte-garran, cothrom-gorge, hibernia-housing, silvermine-mountains, breifine, aodhnait,
  elro-the-ancient, cuuldurach-the-glimmer-king, aroon-the-urlamhai, ulicia, curengkur, rioldna, ide,
  kierac-the-destroyer, flame, blue-lady, black-lady, chieftain-caimheul, overseer-of-the-silver-hand, balor,
  tir-na-nog.

Number of stories tagged with each shape (a tag means two or more hits; the counts are rough):

| Shape | Albion | Midgard | Hibernia |
|---|---|---|---|
| the dead risen, haunting | 42 | 16 | 15 |
| faithful steward or keeper | 31 | 16 | 40 |
| kin-strife | 29 | 37 | 24 |
| dragon | 16 | 24 | 0 |
| giants, elemental foes | 11 | **59** | 3 |
| madness or transformation (wolves) | 6 | **36** | 4 |
| drowned or sunken land | **12** | 4 | 0 |
| oath or bargain kept at a cost | 9 | 6 | 12 |
| return from or traffic with the otherworld | 4 (and Annwn/Arawn throughout) | 2 | 12 |
| wisdom bought at a price | 11 | 1 | 6 |
| unhealing wound, curse, maimed king | 2 | 5 | 5 |
| betrayal and ruin | 1 | 1 | (broken bargains: Cursed Forest, Unseelie) |

### What the current nine cover
| Hero | Shapes covered |
|---|---|
| Galahad | the long quest; the seat that kills the unworthy; the maimed king healed |
| Gawain | the oath or bargain kept at a cost; strength that waxes and wanes |
| Bedivere | the witness and teller; the treasure surrendered (Excalibur); the end of an age |
| Sigurd | the dragon; cursed gold |
| Ragnar | defiant death; serpent-slaying |
| Hervor | waking the barrow-dead; the cursed sword; kin-doom |
| Cú Chulainn | the lone stand at the ford; kin-slaying (Ferdiad); dying on one's feet |
| Fionn | wisdom bought; leading a company |
| Oisín | return from the otherworld; outliving one's age |

### Uncovered gaps that matter (by evidence)
**Albion**
- The **alliance with Arawn and the dead**. It runs through caer-sidi, sister-blythe, the Inconnu and the Temple of
  Arawn. No hero explains why the lord of Annwn would befriend mortals.
- **Who made the song of Caer Sidi.** caer-sidi.json says the song "was made by someone who stood on that strand",
  and apocalypse.json invents Bedivere for it.
- The drowned land (lyonesse, gwyddneau) is already told well by unnamed voices. Its myth figures are Tristan (a
  server) and Seithenyn (a drunk, not a hero).

**Midgard**
- **Giants and giant-kin**, the realm's largest shape. All mortal giant-fighting is currently unvoiced; only Thor, a
  god, is used.
- **Wolf-shape and berserk transformation** (varulvhamn, skona-ravine, ulf, the Wolf's Brood) has no mortal legend.

**Hibernia**
- **The maimed king who cannot rule.** overseer-of-the-silver-hand already tells Nuada unnamed, and balor and fomor
  set up the Fomorian war.
- **Betrayal and broken faith by the powerful** (the Cursed Forest's broken bargain, the Unseelie, the keeners of
  Breifine). The realm's three heroes are all men, and none is a story of betrayal.

### Proposals
Every name below was checked against the Herald server lists (none was a US server) and the world DB Mob table.

**Albion**

1. **Pwyll, Lord of Dyfed ("Pwyll Pen Annwn")**
   - **Gap:** the alliance with Arawn.
   - **Myth:** Pwyll trades shapes and kingdoms with Arawn for a year. He kills Hafgan with a single blow and refuses
     the second, keeps chaste beside Arawn's wife, and earns Arawn's lasting friendship.
   - **Fits:**
     - `L/locations/caer-sidi.json`
     - `L/bosses/sister-blythe.json`, whose theme is exactly why the realm can accept Arawn's help
     - `L/bosses/nyaegha.json`
     - `L/locations/snowdonia.json`
     - `L/bosses/spectral-provisioner.json`
   - **Source:** the First Branch of the Mabinogi (*Pwyll Pendefig Dyfed*), White Book of Rhydderch. Public domain
     (Lady Charlotte Guest's translation, 1838–49).
   - **Era:** before Arthur (era 1 or 3). This is the deep reason for the Shrouded Isles alliance, and it is
     consistent with Mythic's canon that Arawn chooses to help Albion.
   - **Server risk:** none.
   - **Overlap:** the chastity test echoes Gawain's, so tell it from Arawn's side (the friendship), not the
     temptation.
2. **Taliesin**
   - **Gap:** the true teller of Caer Sidi, and the cauldron.
   - **Myth:** *Preiddeu Annwfn* is spoken in Taliesin's voice ("except seven, none returned from Caer Sidi"), and the
     raid seeks the cauldron of the chief of Annwn. *Hanes Taliesin* adds Ceridwen's cauldron and the three drops of
     wisdom.
   - **Fits:** this replaces the invented Bedivere-among-the-seven.
     - `L/bosses/apocalypse.json`
     - `L/locations/caer-sidi.json`
     - `L/bosses/soul-reckoner.json`
     - `L/bosses/funus.json`
     - the sacristan or cauldron keepers of Caer Sidi
   - **Source:** the Book of Taliesin (*Preiddeu Annwfn*); *Hanes Taliesin* (Guest, 1849).
   - **Era:** Arthur's own age.
   - **Server risk:** none.
   - **Overlap:** some with Fionn's "wisdom bought" across realms (Hibernia vs Albion), so it is acceptable.
3. **Third: none.**
   - *Rejected: Bran the Blessed.* His shape is excellent (the wounded giant-king, the guardian head, the door toward
     Cornwall that must not be opened), but the world DB has **"Bran the Giant"** and **"Bran" (Stalker trainer)** in
     Hibernia (region 200). Too confusing.
   - *Rejected: Lancelot and Tristan* (server names; betrayal is already carried by Mordred's Wrath).

**Midgard**

1. **Beowulf**
   - **Gap:** giant-kin and the mere.
   - **Myth:**
     - Grendel, of the giant-kin, raids Heorot by night.
     - Beowulf tears off his arm, then dives into the mere to kill Grendel's mother in her underwater hall.
     - In old age he dies killing a dragon (keep the dragon for Sigurd; tell the mere).
   - **Fits:**
     - `L/locations/vanern-swamp.json` (a drowned lake and a mound household)
     - `L/bosses/mahattava.json` (the bottomless lake under the fall that "stood up and took" a pup)
     - `L/bosses/mokkurvalve.json` and `L/bosses/mokerkialfi.json` (made giants)
     - `L/locations/gotar.json` (Gotar is Götaland, Beowulf's Geats)
   - **Source:** *Beowulf* (Old English, Cotton Vitellius A.xv). Public domain.
   - **Era:** Hygelac's raid, about 521, the same century as Arthur. This is the best era fit of any candidate.
   - **Server risk:** none.
   - **Caveat:** the poem is Old English, not Old Norse, but it is wholly Scandinavian in setting.
2. **Sigmund and Sinfjötli**
   - **Gap:** the wolf-skins, the realm's second-largest uncovered shape.
   - **Myth:** father and son put on enchanted wolf-skins in the forest, cannot take them off for days, speak as
     wolves, and the father bites the son's throat in rage and heals him with a leaf.
   - **Fits:**
     - `L/locations/varulvhamn.json` (a court of "wolf and man and the thing between")
     - `L/locations/skona-ravine.json`
     - `L/bosses/hresvelgr.json`
     - `L/bosses/ulf.json`
     - `L/bosses/bjornvarg.json`
   - **Source:** *Völsunga saga*, ch. 8 (I am confident of this chapter number); *Frá dauða Sinfjötla* (Poetic Edda).
   - **Era:** the generation before Sigurd; same age.
   - **Server risk:** none.
   - **Count:** this is one hero for the name-counter's purposes; count "Sigmund" and "Sinfjötli" together.
3. **Optional, only if Ragnar is swapped out: Starkad (Starkaðr)**
   - **Shape:** the warrior cursed by Thor and blessed by Odin with three lifetimes, each with a nithing's deed, who
     hangs his own king Víkarr in a mock sacrifice that turns real (betrayal and ruin, an unhealing curse).
   - **Fits:**
     - `L/locations/cursed-tomb.json` (the dishonoured dead)
     - `L/bosses/ydenia-of-the-seithkona.json`
     - `L/bosses/jarl-ormarr.json`
   - **Source:** *Gautreks saga* ch. 7; Saxo, *Gesta Danorum* books 6–8.
   - **Server risk:** none.
   - Otherwise skip: four Midgard legends would crowd the name cap.

**Hibernia**

1. **Nuada Airgetlám**
   - **Gap:** the maimed king who cannot rule.
   - **Myth:**
     - He loses his arm at the first battle of Mag Tuired and must give up the kingship, because a king must be
       unblemished.
     - Dian Cécht makes him a silver arm, and Miach regrows flesh on it (and is killed by his father for it).
     - Nuada takes back the throne, yields command to Lugh, and is killed by Balor.
   - **Fits:**
     - `L/bosses/overseer-of-the-silver-hand.json` (already tells him unnamed, with "Airgetlám")
     - `L/bosses/balor.json`
     - `L/locations/fomor.json`
     - `L/locations/vale-of-balor.json`
     - `L/bosses/sargan-the-conqueror.json`
   - **Source:** *Cath Maige Tuired* (Harleian MS 5280); public-domain translation by Whitley Stokes (1891).
   - **Era:** `hib-age-of-the-fomorians`. He is a Tuatha king, a legend of the deep past, which fits canon's
     withdrawn old powers.
   - **Server risk:** none. The name already appears in 7 lore files; check the cap.
2. **Deirdre of the Sorrows**
   - **Gap:** betrayal and broken faith by the powerful, and a woman's legend.
   - **Myth:** King Conchobar lures the exiled Naoise home under Fergus's safe-conduct, has him killed, and Deirdre
     laments and dies rather than be given away.
   - **Fits:**
     - `L/bosses/black-lady.json`, `L/bosses/blue-lady.json` and `L/bosses/chieftain-caimheul.json` (the keeners and
       the bier)
     - `L/locations/cursed-forest.json` (the bargain broken across the hearth)
     - `L/bosses/rioldna.json`
   - **Source:** *Longes mac nUislenn* (Book of Leinster), Ulster Cycle. Public domain.
   - **Era:** Cú Chulainn's generation; same era.
   - **Server risk:** none. World-data collision: "Deirdre", an arrow merchant in Hibernia (region 200). Low; always
     write "Deirdre of the Sorrows" on first mention.
3. **Third: none.** Scáthach and Diarmuid were considered. Scáthach is already mentioned (`L/locations/mount-collory.json`
   area facts), and Diarmuid's story is best told inside Fionn's, as the profile already hints.

---

## 6. Accuracy spot-check of the nine profiles

### Accurate (verified against my knowledge of the sources)
**Galahad**
- Lancelot's son by Elaine of Corbenic; raised in a nunnery.
- Pentecost; the Siege Perilous and his name on it.
- The sword in the floating stone the same day (Malory XIII).
- Percival and Bors; healing the Maimed King; death at Sarras.
- A hand from heaven takes the vessel (Malory XVII).

**Gawain**
- New Year's feast; the holly and the axe; a year and a day; Bertilak; the three days of exchange.
- The girdle hidden; two feints and a nick; the court wears green baldrics.
- Fitts 1–4 are cited correctly.

**Bedivere**
- Twice hides Excalibur, the third time throws it; the arm catches it and brandishes it three times.
- The barge of queens; a hermit near Glastonbury (Malory XXI).
- In *Culhwch*, Bedwyr is one-handed, with the spear that makes one wound entering and nine withdrawing.

**Sigurd**
- Regin's foster-son; Gram splits the anvil.
- The advice to dig more pits; the dying Fafnir's warning.
- The burned thumb and the birds; Regin killed; Hindarfjall and Brynhild.
- Chapters 15, 18 and 19 fit the standard Völsunga numbering.

**Hervor**
- Daughter of Angantyr, one of Arngrím's twelve sons killed at Samsey.
- Disguised as Hervard; the barrow-fires; the fleeing herdsman.
- Tyrfing must kill whenever drawn and will destroy her kin.

**Cú Chulainn**
- Setanta, son of Deichtine and Lugh; Culann's hound.
- Holding the fords while Ulster lies in the curse; Ferdiad.
- Tied to the pillar stone; the raven or crow.

**Fionn**
- Finnegas's seven years at the Boyne; the burned thumb; the thumb of knowledge.
- The Fianna's tests; Diarmuid.
- Sources: *Macgnímartha Finn*.

**Oisín**
- Son of Sadb, the deer-woman; Niamh on the white horse.
- Three years that are three hundred; the girth breaks while helping lift a stone.
- Source: Michael Comyn, *Laoi Oisín ar Thír na nÓg* (c. 1750). Correct.

### Errors to fix
1. **Ragnar: "after her the shield-maiden Aslaug".** Wrong. Aslaug (Kráka) is Sigurd and Brynhild's daughter, raised
   as a peasant girl and famous for wit, not arms. The shield-maiden wife is **Lagertha**, and she appears only in
   Saxo. Suggest "after her Aslaug, the daughter of Sigurd and Brynhild". That also links two Midgard heroes
   honestly.
2. **Ragnar: the "little pigs would grunt" line credited to the Krákumál** (profile text and the history event,
   "his dying song, the Krakumal, ... says that the little pigs would grunt").
   - The piglets verse is in *Ragnars saga* (the snake-pit chapter) and *Ragnarssona þáttr*, not in the Krákumál.
   - The Krákumál gives the battle-list refrain, the ale from curved horns in Odin's hall, and "laughing shall I die".
   - Split the attribution.
3. **Ragnar: "King Herraud of the Geats" and "serpents ... killed them".**
   - In the saga, Herrauðr is a *jarl* of Gautland, and there is **one** serpent (lindworm) that grew around the
     bower.
   - Saxo has a king and two snakes.
   - Either say "jarl ... the serpent" (the saga, which is the cited source) or cite Saxo.
4. **Oisín's event: the source for "tells the tales of the Fianna to Saint Patrick" is the *Acallam na Senórach*.**
   - In the *Acallam*, the chief teller is **Caílte mac Rónáin**; Oisín is a lesser presence.
   - The Oisín–Patrick quarrel (psalms duller than the hunt) belongs to the later Fenian ballads (*Agallamh Oisín agus
     Phádraig*; *Duanaire Finn*).
   - Re-cite. The canon point about Patrick is in section 4.
5. **HEROES.md: Bedivere's source includes "Geoffrey of Monmouth" for the role "the last survivor, the one who saw the
   end".** In Geoffrey (*Historia Regum Britanniae*, Book X), Bedivere the butler is **killed fighting Lucius's
   Romans and buried at Bayeux**, before Camlann. Geoffrey supports Bedivere as cupbearer, not as survivor. Drop
   Geoffrey, or note that the role comes from Malory.
6. **`L/bosses/apocalypse.json`:** the Bedivere-at-Caer-Sidi fact should be `original`, not `myth` (section 2).

### Minor or embellishment (acceptable but should be known)
- **Cú Chulainn and Ferdiad "three days":** the Book of Leinster Táin has the killing on the **fourth** day. Recension
  I is shorter; I am **uncertain** of its count. "Days" is safe.
- **Cú Chulainn's exemption** from the curse: the explanations vary (boyhood, or not being of Ulster's stock).
  "Not of the line" is defensible.
- **Sigurd's "old man with one eye":** the saga gives a long-bearded old man (understood as Odin). The one eye is
  interpretive; acceptable.
- **Hervor "would burn the barrow if he refused":** in *Hervararkviða* she threatens a curse (that they rot in their
  mounds). A fire threat is **uncertain** to me; consider "would curse the barrow".
- **Fionn's recruit test** compresses several tests (the run without breaking a twig or loosening a braid; the
  defence from a pit with a shield against nine spears). Fine as told.
- **"Camelot never filled that seat again"** (Galahad) and **"Bedwyr ... whose spear no man could match"** are
  fair embellishments, presented as tellers' words.

### Citations I could not verify exactly
- Ragnars saga chapter numbers (ch. 2–3 for Thora, ch. 17 for the snake pit). Editions differ, and the pit falls
  around ch. 15–17. **Uncertain.**
- Hervarar saga ch. 3–4. Plausible in the R/U redactions. **Uncertain.**
- "Táin Bó Cúailnge (Recension I), the Boyhood Deeds" and "the combat at the ford". Both episodes exist in
  Recension I; section naming varies. **Probably fine.**
- Malory book numbers XIII, XVII and XXI are correct for Caxton's divisions (verified from knowledge, not from a file
  in this session).

---

## Sources used
- Archive files named inline (Camelot Herald and Classes of Camelot captures, 2002–2005).
- World DB, read-only: tables Mob, NpcTemplate, DataQuest, ItemTemplate, Regions, Zones.
- Repo scripts: `MorganaBechardSilcharde.cs`, `Academy50.cs`, `GameRelic.cs`, `OFGreenKnight.cs`,
  `RealmRaidOpenWorld.cs`.
- Branch files: `docs/1.2.1/HEROES.md` and `L/history/{albion,midgard,hibernia}.json`, plus about 60 boss and
  location files.
- Web:
  - [Wikipedia: Dark Age of Camelot](https://en.wikipedia.org/wiki/Dark_Age_of_Camelot)
  - [GameSpot: Dark Age of Camelot Updated Preview](https://www.gamespot.com/articles/dark-age-of-camelot-updated-preview/1100-2799868/)
  - [FreddysHouse: Return to Prydwen or Excalibur?](https://forums.freddyshouse.com/threads/return-to-prydwen-or-excalibur.105679)
  - [GameBanshee: DAoC Servers in Europe](https://gamebanshee.com/36k4) (no server names given)
- Myth facts rely on my knowledge of the named public-domain texts, which I did not fetch in this session. Where I
  was unsure, the report says so.
