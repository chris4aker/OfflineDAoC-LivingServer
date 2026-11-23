# Hibernia's present-day cast (NPC pass, phase 1)

The people a story of Hibernia can hand a line to. Owner decision 10 (2026-11-22): present-day characters belong in the
stories and recur across places and bosses; legends stay legends ([HEROES.md](HEROES.md)); nothing after 1.65. Hibernia
has no Church: its present-day priests are druids, its record-keepers are bards, brehons and keeners.

Counts, sources and the **over** mark are read as in [CAST-ALBION.md](CAST-ALBION.md): bare-name mentions in the narrative
strings (`build_npc_roster.py --counts`), cap 12, *real* from `tools/lore/npc_roster.json`, *ours* for Living Server
writing.

## The recurring cast already in the lore

Hibernia's lore is rich in tellers of Hy Brasil and the Shrouded Isles and thin in tellers of the old mainland. Its two
Domnann voices already work together, as Albion's Elowen and Sefin do.

**Aodhan of Tir na Nog, the bard who wintered in Domnann.** *Ours.* Mentions **65, over** (20 files).
- Knows: the Sylvan teachings and songs he put into the realm's tongue (`bosses/olcasgean`, `bosses/aroon-the-urlamhai`,
  `bosses/easmarach`, `bosses/corrupt-primal-of-water`, `bosses/xaga`, `locations/galladoria`, `locations/tir-na-nog`).
- Talks: asks why, and writes down that he was not answered ("Aodhan noted that she would not say why not").
- Ties: Lisín; Orlaith's hundred and forty; the druid Muirgen.
- Phase 2: none.

**Lisín the fire-keeper of Domnann.** *Ours* (a Sylvan). Mentions **66, over** (19 files).
- Knows: the Sylvan view of every power in Galladoria and the springs gone dry, among them her own family's Tobar Lisín
  (`locations/domnann`, `bosses/corrupt-primal-of-water`, `bosses/tine`, `bosses/beatha`, `bosses/xaga`).
- Talks: one sentence, final ("Cut a root, Lisín said, and the tree grows another trunk").
- Phase 2: none. Her house and her spring can stand in for her.

**Muirgen, the druid who went west with the first swords.** *Ours.* Mentions **44, over** (9 files): Tur Suil and the
brehons' judgements she copied (`locations/tur-suil`, `bosses/balor`, `bosses/abomos-the-soultrapper`,
`bosses/sargan-the-conqueror`: she "stopped counting the names at forty").

**Orlaith, who led the hundred and forty into the hollow.** *Ours.* Mentions **49, over** (16 files); a present-day
company leader of Galladoria, kin in function to Albion's third company.

**Derbáil of Ardee and her grandmother's list.** *Ours.* Mentions **30, over**: 27 book pages open "From Derbáil of
Ardee's list" (`bosses/badb`, `bosses/blight`, `bosses/lhia`, `bosses/saemus`...), and `history/hibernia` calls the list
the realm's best-known record of the wilds' superstitions.

**Nóra, a keener's daughter of the Breifine hills.** *Ours.* Mentions **52, over** (9 files): the keen of Caimheul,
Marfach and the three Ladies (`locations/marfach-caverns`, `bosses/chieftain-caimheul`, `bosses/black-lady`,
`bosses/red-lady`, `bosses/blue-lady`).

**Coll of the Domnann guides.** *Ours* (a Sylvan guide). Mentions **21, over**: 20 Hy Brasil pages open "From Coll of the
Domnann guides" (`bosses/zytka`, `bosses/razorbeak`, `bosses/goldenleaf`...).

**Flann of Howth.** *Ours.* Mentions **33, over** (8 files): went into Fomor as a young man and kept his promise not to
describe the room with the hooks, "mostly" (`locations/fomor`, `bosses/krackenschtein`, `bosses/nogoribando`).

**Hervelina the hermit of Treibh Caillte.** *Ours.* Mentions **36, over** (11 files).

**Noirin the enchanter.** *Ours.* Mentions 17, **over** (9 files).

### The real NPCs already used

| Name | Home | Role | Era | Mentions | Where |
|---|---|---|---|---|---|
| Lovernios the Druid Prince | Tir na Nog, the palace garden | wants Ysberin's staff and Saemus's crystal | probable (capture) | **24, over** | `locations/bog-of-cullen`, `locations/tir-na-nog`, `bosses/ide`, `bosses/madawg`, `bosses/saemus` |
| Druid Tobryn | Tir Urphost, the Cliffs of Moher | the Broken Seal druid; his theory of Coimirceoir | probable | **20, over** | `locations/cliffs-of-moher`, `bosses/coimirceoir`, `bosses/tawi-shon` |
| Lobais | Tir na Nog, the throne room | keeps the realm's account | probable | **19, over** | `locations/tir-na-nog`, `bosses/abbadona` |
| Chief Proinnsias | Howth | the chief's hut, Nuada's Silver | probable | **16, over** | `locations/silvermine-mountains`, `bosses/mikka` |
| Cathbad of Howth | Howth, the chief's hut | Morven's Return | probable | **14, over** | `locations/silvermine-mountains`, `bosses/morven`, `locations/cruachan-gorge` |
| Brigit of the palace | Tir na Nog | the moonstone-seekers (*The Moonstone Twin*) | probable (`Essence50.cs`) | 12, at the cap | `locations/tir-na-nog`, `bosses/rioldna`, `bosses/faerie-queen` |
| Ainrebh | Caille | enchanter; studies the crystal that ate a man | probable (`Focus50.cs`) | 6 | `locations/valley-of-bri-leith` |
| Brother Esras | his hermitage on the southern cliffs of Moher | hermit | probable (capture) | 4 | `locations/cliffs-of-moher`, `bosses/aoife` |

**Cathbad is two men.** Cathbad of Howth is a present-day NPC; the druid Cathbad who named Cú Chulainn the Hound of
Culann is a legend (`history/hibernia`). The name is told in both senses; decision 10 allows it only if no text makes
one the other. Phase 2 should call the present-day one "Cathbad of Howth" every time.

**Brother Esras is a churchman in Hibernia.** He is in the game data (a real NPC near the Cliffs of Moher), so he may
stay, but HEROES.md says Hibernia has no Church: phase 2 should keep him an outsider (a hermit from over the water), never
part of Hibernian faith. Owner's call.

## Real 1.65 NPCs to bring in

The 15 trainers of Tir na Nog are already listed in `locations/tir-na-nog` and nowhere else.

| Name | Where | Role | Era | Mentions | Fits |
|---|---|---|---|---|---|
| **Daray** | Tir na Nog, the druid grove | druid trainer | probable | 3 | "the druid" (67 uses in 25 files) |
| **Lasairiona** | Tir na Nog, the fighting paths under the north wall | champion trainer; sends her champions to Ilisa for *The Moonstone Twin* | **confirmed** (Allakhazam, comments from Aug 2002) | 2 | "the captain" of the realm's young |
| **Ilisa** | Mag Mell | weaponsmith; the Moonstone Twin's first step | **confirmed** (same page) | 0 | "the smith" (49 uses in 30 files) |
| Labhras | Tir na Nog | warden trainer | probable | 3 | a warden of the realm, kept apart from the Sylvan "wardens of Domnann" |
| Selia | Tir na Nog | bard trainer | probable | 3 | the realm's bard when Aodhan is capped |
| Caolan, Sadhbh | Tir na Nog | townsfolk of the Moonstone Twin | **confirmed** (period page only, not in the capture) | 0 | the city's errand-folk |
| Eli of Connla, Kern of Innis Carthaig | Connla, Innis Carthaig | townsfolk of the Moonstone Twin | **confirmed** (period page only) | 0 | the realm's coast and lakes |
| Eersie | Necht, Hy Brasil | healer | probable | 0 | "the healer" of Hy Brasil |
| Chieftain Crauchon, Chieftain Bolg, Chieftain Ailinne and the other keep lords | the frontier duns | keep lords (Chieftain or Chieftess) | probable (the server's naming rule) | 0-6 | "the chieftain" (72 uses in 18 files) |
| Captain Cody | the Thidranki portal keep | battleground captain | **confirmed** (Herald) | 0 | Hibernia's young at Thidranki |

## Proposed new recurring figures (Hibernia's Elowen)

Hibernia has strong keepers of Domnann and Breifine. It lacks a voice from the capital who crosses the old mainland:
Connacht, Lough Derg, the Cursed Forest, Cruachan Gorge.

1. **Daray, the druid trainer of Tir na Nog.** Real, probable; 3 mentions. The druid every young druid answers to, and
   the present-day druid that "the druid" now stands in for in 25 files. Daray can read what the Druid Court will not say
   aloud, and receives what the realm brings home from the Cursed Forest and the bog for Lovernios. Voice: a teacher's
   questions; answers a question with the older question under it. (The capture leaves Daray's sex unset: check the
   model in game before a pronoun goes in.)
2. **Lasairiona, champion trainer of Tir na Nog.** Real, **confirmed**; 2 mentions. She sends her champions to the
   Cursed Forest for the moonstone and counts who comes back; a fighter's plain account of places where the druids give
   only theories. Voice: short, practical, names every one of her champions.
3. **Ilisa, the weaponsmith of Mag Mell.** Real, **confirmed**; 0 mentions. Every Moonstone champion's road starts at
   Ilisa's anvil, and what comes back from Lough Derg and the Cursed Forest is mended there and read by its notches.
   Ilisa is Hibernia's counterpart to Morlin Caan in Jordheim (CAST-MIDGARD.md), so the two realms rhyme.

Keep each under 12 mentions, and give Daray the druid lines before adding any to Lovernios or Tobryn.

## Cautions

- **"The wardens of Domnann"** are Sylvan guards of Hy Brasil, not the warden class. Do not map them to Labhras.
- **Hibernia's housing** (`locations/hibernia-housing`) is in era (Foundations, July 2003), but has Lovernios grant the
  lots: check that against the game before adding to it.
