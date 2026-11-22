# The shared places' present-day cast (NPC pass, phase 1)

Darkness Falls, the battlegrounds (Thidranki, Abermenai, Murdaigean, Caledon), the Proving Fields and the Shrouded Isles
umbrella belong to no realm. Their stories are where the three realms' casts meet, so decision 10's "the realms feel
connected" is done here more than anywhere. Counts, sources and the **over** mark are read as in
[CAST-ALBION.md](CAST-ALBION.md).

## The demon court is not present-day cast

The court below Darkness Falls tells its own history through its officers, who are bosses: Grand Chancellor Adremal
(98 bare mentions), Field Marshal Nebir (74), Gatekeeper Dommel (64), Director Kobil (47), the Archivist Borath, Mystemas
the court's go-between (56; also a real NPC row in the capture's Darkness Falls). `name_counts.py` leaves the court out
by design (`COURT`, boss names). Phase 2 does not add realm NPCs to the court's chapters; it adds them to the realms'
side of the doors.

## The realms' side: recurring figures already in the lore

**The warden of the castle ruins and the Door-Book.** *Ours.* The Church's record of every opening of the Albion door
(`locations/camelot-hills`, `locations/darkness-falls`, `bosses/high-lord-baln`, `bosses/prince-abdin`, 57 mentions of
"Door-Book" in 13 files). Brother Odran kept it until decision 1 retired him; the book now speaks through each company's
account (Dame Tegan Fras, Captain Gruffudd Fras, Sir Tomos Llwyd, Dame Elen of Caerwent, Dame Mabel of Sauvage, Sir
Cynwrig Goch: 1-5 mentions each). This is the model for the other two realms.

**Sister Elaydith's runner.** The Albion runner who went down the stair to fetch a ladle and lived
(`bosses/gatekeeper-dommel`, `bosses/malroch-the-cook`). Sister Elaydith is at her cap (CAST-ALBION.md); the runner is
not named and can be.

**Brother Aldric**, lost at the crossing; the warden's map has a cross for him (`locations/darkness-falls`,
`bosses/gatekeeper-dommel`). *Ours.* 5.

**The first breakers of Legion:** Sir Aldous Fenn of Camelot Hills (5), Thane Hrolf the Tall of Midgard (4) and
Ealadhach the bard of Hibernia (4), named on the three capitals' rolls (`bosses/legion`, `bosses/behemoth`). *Ours.* The
only place where all three realms' present-day heroes stand in one sentence.

**Sir Edric of Caer Ulfwych** and his squire Iorwen of Cotswold: the Grand Summoner's first death and *A Squire's First
Descent* (`locations/summoners-hall`, `locations/tomb-of-mithra`, `bosses/summoner-cunovinda`). *Ours.* Sir Edric 9
(titled count), bare "Edric" 22 (Camelot's infiltrator trainer, Master Edric, is a real NPC of the same name).

**Pico the imp**, who keeps a stall in the Albion hall and sold the court's order of precedence for a candle-end
(`locations/darkness-falls` book). *Ours.* 4.

**Brother Deiniol**, chaplain at Albion's gate of Abermenai in the first summer, who wrote down every waking
(`locations/abermenai`, `locations/proving-fields`). *Ours.* 4.

## Real 1.65 NPCs to bring in

| Name | Where | Role | Era | Mentions | Fits |
|---|---|---|---|---|---|
| Captain Circa (Albion), Captain Cody (Hibernia), Captain Astridr (Midgard) | the Thidranki portal keeps | battleground quest captains | **confirmed** (Camelot Herald, *Battleground Quest Guide*) | 0 each | `locations/thidranki`: the three captains who send the young out; a page each, in three voices |
| Bolo (Albion), Etho (Hibernia) | Darkness Falls | named realm NPCs in the halls | probable (capture) | 5 / 1 | the realms' own people below; who keeps the hall when the companies are down |
| Mystemas | Darkness Falls | the demon go-between of the plague and *Strange Bedfellows* quests | probable (capture; a realm-flagged row) | 56 | already well used; no more |
| The keep lords of each realm | the old frontiers | keep lords | probable | 0-8 | the realm that holds the keeps holds the door (`locations/camelot-hills`: "it opens only in the seasons when Albion holds the frontier") |

The capture has no named realm NPCs at Abermenai, Murdaigean or Caledon (region 250's captains are new names in
`QuestCaptains.cs`, not period), so those battlegrounds keep their roles.

## Proposed recurring figures

Midgard and Hibernia have no Door-Book. Give each realm's side of Darkness Falls one keeper, from their own new cast:

- Midgard: **Sven of Jordheim** (CAST-MIDGARD.md) sings each warband's descent into the realm's roll at Jordheim, the
  roll that names Thane Hrolf the Tall.
- Hibernia: **Daray of Tir na Nog** (CAST-HIBERNIA.md) keeps the druids' account of the door below, beside the roll
  that names Ealadhach.

Then the three capitals' rolls, already named in `bosses/legion`, each have a keeper, and a Darkness Falls page can end
with what Camelot, Jordheim and Tir na Nog each wrote down.
