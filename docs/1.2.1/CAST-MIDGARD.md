# Midgard's present-day cast (NPC pass, phase 1)

The people a story of Midgard can hand a line to. Owner decision 10 (2026-11-22): present-day characters belong in the
stories and recur across places and bosses; legends stay legends ([HEROES.md](HEROES.md)); nothing after 1.65.

Counts, sources and the **over** mark are read as in [CAST-ALBION.md](CAST-ALBION.md): bare-name mentions in the narrative
strings (`build_npc_roster.py --counts`), cap 12, *real* from `tools/lore/npc_roster.json`, *ours* for Living Server
writing. Midgard's tellers are mostly bare names, so CI's titled-name counter sees almost none of them.

## The recurring cast already in the lore

Midgard's lore spreads its telling across many local witnesses, each tied to one place. That is its strength. What it
lacks is anyone who crosses from one place to the next.

**Loremaster Sigrun.** *Ours* (the loremaster NPC). Mentions **35, over**, one in each of 33 location files.
- Every Midgard location's loremaster intro opens `Loremaster Sigrun says, "<place>.` (`locations/jordheim`,
  `locations/malmohus`, `locations/trollheim`...). It is the realm's one fixed formula.
- Phase 2: no new mentions. The intro is spoken by her NPC, so the name can drop from the text ("The loremaster says").
- Caution: Sigrun is also the first valkyrie's name in `locations/jordheim`, and a gamebot first name
  (`EraBotNames.cs`).

**Thorgrim of Faraheim.** *Ours.* Mentions **22, over** (13 files).
- Role: the Remnants' note-taker at Faraheim; ten book pages open "From Thorgrim of Faraheim's notes"
  (`bosses/ancient-syver`, `bosses/delegalt`, `bosses/hati`, `bosses/lydsyg`, `bosses/stanga`...).
- Knows: Nyr's cold shard on the slate-house shelf (`bosses/elder-icelord-suttung`); the giants' nine refusals
  (`bosses/icelord-hakr`); the narfi's one answer, "where she lies sick" (`locations/iarnvidiurs-lair`).
- Talks: records two accounts and lets them stand; admits his translations are bad.
- Caution: `QuestCaptains.cs` names Midgard's new Caledonia captain "Captain Thorgrim". Keep them apart.

**Orri of Hagall.** *Ours.* Mentions **30, over** (13 files).
- Role: the Remnants' slate-writer, who crossed to Modernagrav with the first explorers and paced the bones
  (`locations/modernagrav`).
- Knows: Basilius at the dig by night, Shiake's god, the mammoth's worn path (`bosses/basilius`, `bosses/shiake`,
  `bosses/mortufoghus`, `bosses/hoden`, `bosses/council-vagn`: the frost came down on him).
- Talks: first person, close observation ("I lay on the cliff above the dig a whole night once").

**Brisa the beam-cutter of Svarhamr.** *Ours.* Mentions **30, over** (18 files).
- Knows: the svartalfar's beam of Malmohus, every drakulv name and line on it (`bosses/gunnolf`, `bosses/geirolf`,
  `bosses/esulv`, `bosses/wyulv`...).
- Talks: reads marks as a craftsman, one clean claim per page ("Brisa the beam-cutter says it is pride").
- Ties: Ragnhild's song takes the beam's new marks every year (`locations/myrkwood-forest`).

**Ivrik the kobold lamp-carrier of Bjarken.** *Ours.* Mentions **14, over**, one in each of 14 Trollheim boss pages, all
opening "From what Ivrik the kobold lamp-carrier of Bjarken told the skalds".

**Thyri of Galplen, teller of the old tales.** *Ours.* Mentions 11 (10 files): *Fenrir's Folly* as Galplen tells it
(`locations/raumarik`, `bosses/loken`, `bosses/oona`, `bosses/kelic`, `bosses/wretch-of-winter`). Room: 1.

**Hrefna of Faraheim, the slate-keeper.** *Ours.* Mentions **30, over** (8 files). The child who walked back out of the
old one's hollow; counted Stanga's marks (`bosses/stanga`, `bosses/droom`, `locations/iarnwood`).

**Hallveig the Valkyn guide.** *Ours.* Mentions **25, over** (5 files): Trollheim's guide and Arnor of Jordheim's source
(`locations/trollheim`, `bosses/dagar`, `bosses/fostra-orm`). Also a gamebot first name.

**Ragnhild of Galplen, skald.** *Ours.* Mentions **14, over** (3 files). Sang Hallbjorn Ulfsson's eighty at Malmohus and
lived (`bosses/gjalpinulva`, `locations/malmohus`). Her song grows every year. Also a gamebot first name.

**Gudny of the Seithkona Circle.** *Ours.* Mentions 11 (7 files): *What the Circle Sees* (`bosses/storslange`).

### The real quest givers already used

| Name | Home | Role | Era | Mentions | Where |
|---|---|---|---|---|---|
| Inaksha | Haggerfel | the seers' great quest | **confirmed** (Allakhazam, *The Desire of a God*, comments from Mar 2003) | **22, over** | `locations/vale-of-mularn`, `locations/uppland`, `bosses/fjall`, `bosses/loken`, `bosses/thorkatla`, `bosses/storslange` |
| Miri | Jordheim | the seers' second step; visions of Loki | **confirmed** (same page) | 10 | `locations/jordheim`, `bosses/thorkatla`, `bosses/leildolf` |
| Masrim | Fort Atla | the rogues' great quest; blind | **confirmed** (Allakhazam, *A War of Old*, comments from 2001) | **21, over** | `locations/gotar`, `locations/skona-ravine`, `bosses/oona` |
| Morlin Caan | Jordheim, the forge by the temple | smith; receives Oona's head | **confirmed** (same page) | 3 | `locations/jordheim`, `bosses/oona` |
| Lynnleigh | Strider Island, East Svealand | the vikings' great quest | probable (`Viking50.cs`) | **18, over** | `locations/east-svealand`, `bosses/red-hagen`, `bosses/albion-leader` |
| Elizabeth | Mularn | smith and enchanter, the vikings' first step | probable | 8 | `locations/vale-of-mularn` |
| Danica | Mularn | the mystics' great quest | probable (`Mystic50.cs`) | **20, over** | `bosses/kelic`, `bosses/jango`, `bosses/serk` |
| Audun | Jordheim, the tavern | the vikings' chain | probable | 12, at the cap | `bosses/albion-leader`, `locations/jordheim` |
| Lyngheid | Fort Atla | a seeress of the seers' chain | probable | 7 | `locations/gotar` |

## Real 1.65 NPCs to bring in

| Name | Where | Role | Era | Mentions | Fits |
|---|---|---|---|---|---|
| **Sven** | Jordheim, the rows by the southern gates | skald trainer | probable (capture; Allakhazam's skald trainer list) | 2 | "the skald" (14 uses in 11 files); the realm's keeper of songs |
| Leif | Jordheim | skald trainer | probable | 5 | second skald; but a Remnants spy "Leif Asgautsson" (*ours*) shares the name |
| **Grimma** | Jordheim | shaman trainer | probable | 2 | "the priest" and "the elder" of Midgard's gods |
| Per | Jordheim | healer trainer | probable | 0 | "the healer" of the capital |
| Kalbin | Mularn | healer | probable | 0 | the healer the young meet first |
| Vahn | Mularn | smith | probable | 0 | "the smith" of the Vale |
| Jarl Bledmeer, Jarl Hlidskialf, Jarl Glenlock and the other keep lords | the frontier fastes | keep lords | probable (the server's naming rule) | 0-3 | "the chieftain" or "the warden" of a frontier scene |
| Captain Astridr | the Thidranki portal keep | battleground captain | **confirmed** (Herald) | 0 | Midgard's young at Thidranki |
| Arnfinn | Vasudheim | smith, the mystics' chain | probable | 0 | Danica's errand-man when Danica is capped |

Rana (healer trainer, Jordheim) is real but shares her name with the drakulv Rana of Malmohus (`bosses/rana`): do not
use her. Stor Gothi Annark (7) is the Shrouded Isles stone-sender of Aegirhamn; the builder leaves the porters out as
uncertain for 1.65, so he is not in the roster.

## Proposed new recurring figures (Midgard's Elowen)

Elowen works because she keeps one thing (the colleges' list) that every Avalon page can point to, and because she
has stood at the places she speaks of. Midgard has many keepers of one place each and no one who crosses. Three real
NPCs can, from Jordheim, where every warband starts and comes home:

1. **Sven, skald trainer of Jordheim.** Real, probable; 2 mentions. He takes each warband's account at the southern
   gates and sets the true parts into song, so a Malmohus page, a Tuscaran page and a Trollheim page can each end with
   what Sven kept and what he left out. He sings Ragnhild's song in Jordheim and knows it is too long to finish
   (`locations/malmohus`). Voice: curt in speech, exact in verse; he names the dead and never the living.
2. **Morlin Caan, the smith by the temple.** Real, **confirmed**; 3 mentions. Everything a company brings home that
   is not gold passes over his anvil: Oona's head (`bosses/oona`), the drakulv blades, the giants' spikes. He reads a
   fight in the nicks on a blade. Voice: few words, all of them about metal; talks to the shadows of Loki's house when
   he thinks nobody is listening (`locations/jordheim`).
3. **Grimma, shaman trainer of Jordheim.** Real, probable; 2 mentions. The realm's priest of the gods who are still
   alive at 1.65: she reads the omens before a warband leaves and the dead's tally after. She gives Midgard a
   present-day voice for its gods and its dead, where the lore now says "the priest" or "the gothi".

Keep each under 12 mentions; spread them so that no Midgard region has more than two of the three.

## Cautions

- **Gamebot names:** Sigrun, Ragnhild and Hallveig are in the gamebot name list (`EraBotNames.cs`, Midgard women). A
  player may meet a bot of the same name. Decision 7 covers only hired companions; ask the owner whether lore tellers
  should avoid gamebot names too.
- **Housing is in era, the Market Explorer is not:** the housing villages and their consignment merchants came with
  Foundations in July 2003 (worthplaying.com, 19 August 2003: 20,000 houses sold "in less than six weeks"), before 1.65.
  The Market Explorers that `locations/midgard-housing` mentions came later; that line needs an era fix.
