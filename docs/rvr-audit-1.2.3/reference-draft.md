# RvR audit — reference: what Old Frontiers RvR looked like, 1.50-1.65 (2002-2003)

Superseded by [reference.md](reference.md) (issue #493); kept for the record.

Agent: reference. Status: **DRAFT 1 — section 1 only, from memory plus the repo's own data. Web checks and sections 2-7,
the design judgement and the checklist follow in later saves.** Other agents: use this file as it stands; items are
tagged so you know how hard to lean on them.

Tags: **[S]** sourced this session (link given), **[R]** sourced in the repo's own research note
(docs/research/rvr-field-1.2/ERA-RESEARCH.md, era-dated or retrospective as it says), **[M]** from memory of the period
(not re-checked), **[D]** read from this repo's world data. A number without a tag is [M].

Units: DAoC world units. 1,500 = nuke range, 2,000-2,200 = longbow range, 350 = PBAoE/AE radius, ~500 = melee reach is 200.
Run speed about 191 units/s unhasted [M]; so 1,000 units is about 5 s on foot, 2.5 s at speed 6.

## 1. The map of the war

### 1.1 Three frontiers, twelve zones [M, D agrees]
- Each realm has a four-zone frontier, joined on foot only inside itself. There is **no land link between frontiers**.
  The only ways across are the portal ceremony and (rare) relic-gate walks.
  - **Albion** (region 1): Forest Sauvage (home door, Castle Sauvage), Snowdonia (Snowdonia Fortress, home door 2),
    Pennine Mountains (middle), **Hadrian's Wall** (the far end, where the enemies arrive).
  - **Midgard** (region 100): Uppland (Svasud Faste), Yggdra Forest (Vindsaul Faste), Jamtland Mountains (middle),
    **Odin's Gate** (far end).
  - **Hibernia** (region 200): Cruachan Gorge (Druim Ligen), Mount Collory (Druim Cain), Breifine (middle),
    **Emain Macha** (far end).
- The repo's check-spot catalog uses exactly these regions and zone IDs (11/12/14/15, 111/112/113/115, 210/211/212/214),
  and the release world lists the Old Frontiers keeps by name [D]. See 1.9.

### 1.2 Keeps [M; names D]
- Seven claimable keeps per realm, 21 in all, plus two relic keeps per realm (six).
  - Albion: Caer Benowyc (Hadrian's Wall, the front keep), Berkstead, Erasleigh, Boldiam, Sursbrooke (Pennine and
    around), Hurbury (Snowdonia), Renaris (Sauvage). Relics: Castle Excalibur (Strength), Castle Myrddin (Power).
  - Midgard: Bledmeer Faste (Odin's Gate, the front keep), Nottmoor, Hlidskialf, Blendrake, Glenlock (Jamtland),
    Fensalir (Uppland), Arvakr (Yggdra). Relics: Mjollner Faste (Strength), Grallarhorn Faste (Power).
  - Hibernia: Dun Crauchon (Emain, the front keep), Crimthain and Bolg (Breifine, the two "back" keeps every Emain run
    passed), nGed, Da Behnn, Scathaig (Collory), Ailinne (Cruachan). Relics: Dun Lamfhota (Strength), Dun Dagda (Power).
- An Old Frontiers keep is **one walled yard with an outer gate, an inner keep door, and a lord's room upstairs**.
  No destructible walls, no towers to capture, no climb points, no hookpoint-mounted defences. Those are New Frontiers
  (1.70, June 2004). The only way in is through the doors (or a postern used by the owners).
- The front keep of each realm (Benowyc, Bledmeer, Crauchon) changed hands several times a day. The deep keeps were
  taken mainly to open a relic or to flip Darkness Falls.

### 1.3 Portal keeps and the ceremony [M]
- Each realm has **two fixed outposts in each enemy frontier's far zone** ("portal keep", "teleport keep": APK/ATK,
  MPK/MTK, HPK/HTK). They cannot be taken. Their guards are high level and kill enemies who come close. They have a
  bindstone (from 1.52 [R]) and merchants.
  - In Emain Macha: Albion's and Midgard's. In Odin's Gate: Albion's and Hibernia's. In Hadrian's Wall: Midgard's and
    Hibernia's.
- **Getting there.** At your home border keep (Castle Sauvage / Snowdonia Fortress; Svasud / Vindsaul; Druim Ligen /
  Druim Cain) you buy a cheap **medallion necklace** for the frontier you want, wear it, and stand on the portal pad.
  Casters at the pad run a ceremony on a fixed cycle and everyone on the pad wearing a medallion is ported at once.
  The cycle was long at launch (about 15-20 minutes) and was cut during 2002; by 1.6x people spoke of a wait of
  a few minutes at most (exact cycle: check). Result: **arrivals come in waves**, a realm's force leaves its
  portal keep in clumps a few minutes apart, and a death costs a wave (5-10 minutes [R]).
- **The home realm walks or rides.** Hibs ran from Druim Ligen through Breifine (past Crim or Bolg) into Emain; Albs
  rode or ran Sauvage -> Pennine -> Hadrian's Wall; Mids Svasud -> Jamtland -> Odin's Gate. Horse routes existed inside
  the home frontier between border keep and some keeps (stable routes; exact list: check). **No boats** in the classic
  frontiers. Speed classes and keep/border hasteners (free speed buff, 1.52 [R]) did the rest.
- Release after death goes to your bind: usually the portal keep (away) or the border keep (home).

### 1.4 Milegates and the wall [M, R]
- **A milegate is a short gap in a wall or ridge that every arrival must pass** between an enemy portal keep and the
  open zone. It is the war's main chokepoint. Names are by the realm whose portal keep sits behind it: **AMG and MMG in
  Emain Macha** [R], **MMG and HMG in Hadrian's Wall**, **AMG (and a Hib-side gate) in Odin's Gate** [M, low confidence
  on the Odin's Hib gate; the repo catalog has five milegates, not six].
- Hadrian's Wall the structure and Odin's Gate the structure are long walls with gate openings; the fights there are
  at the openings and on the slope either side.
- What happened at a milegate:
  - Arrivals bunched on the inside ("our side"), waiting for numbers, afraid to walk through.
  - The home realm and the other enemy sat on the outside, casters at max range on the gate mouth, stealthers in
    the gap itself and on the walls.
  - "Milegate ping-pong": push through, AE mez, wipe or drive them back to their guards, repeat. Dozens of minutes.
  - Realm point farming at a gate was the norm, not a scandal ("AMG AMG AMG all the time" [R]).
  - People were told never to rest at a milegate [R].

### 1.5 The usual fight spots and routes
- **Emain Macha: the bulk of all RvR** ("80%" [R, retrospective]). Flat, few aggressive monsters, two realms port in.
  - Spots [R, era-dated list]: AMG, MMG, APK/ATK, MPK/MTK, Dun Crauchon, the Crauchon merchant/"cruach" area, Levi
    ridge, Dhak's Tower by AMG, the Grogans plain near Crauchon, "Grannies" right of AMG, Middle Hill, valley 1 (Crim
    side), valley 2 (Bolg side).
  - Routes: AMG <-> MMG along the wall and back "all night" [R]; AMG -> Crauchon; Hibs in from Breifine by the Crim
    road or the Bolg valley; chain Emain -> Breifine -> Gorge when Emain was too hot [R].
- **Odin's Gate**: second. AMG fights, the road to Bledmeer, the gate wall, Mids on foot to Bledmeer. Yggdra was the
  grey-ganking zone because people levelled there [R, era-dated].
- **Hadrian's Wall**: third and quietest for group fights; MMG/HMG, Benowyc, the wall road, the pass into Pennine.
  Pennine camps (fellwoods, cliff crawlers, angry bwca, Cyraeth-type camps) drew gankers of levellers [R].
- **Deep zones** (Sauvage, Snowdonia, Uppland, Yggdra, Collory, Gorge): soloers, stealthers, levellers, relic-raid
  columns. Hours can pass with no enemy.
- **Frontier dungeons**: one per realm, entered from the frontier and open to enemies — Hall of the Corrupt (Pennine?
  area [D catalog puts its door in Pennine]), Dodens Gruva (Jamtland/Uppland side), Marfach Caverns (Breifine).
  Levelling there with an XP bonus, occasional gank groups. [M; added in a 1.5x patch — check which.]

### 1.6 Border keeps and guards [M]
- The border keep (Sauvage, Svasud, Druim Ligen...) is the home door. It has many high-level guards with long aggro
  and a doorway zone line; an enemy who chases to it dies ("do not chase to the TK, you will die" [R] is the same
  rule for portal keeps).
- Realm guards on roads and at keeps call out and assist; they see stealthers at short range.
- Hasteners stand at border keeps, portal keeps and keeps [R].

### 1.7 Relic keeps and their rules [M]
- Six relics: one Strength (melee) and one Power (magic) per realm. Each held relic gives the realm a bonus
  (about +10% melee or magic damage per extra relic held; own relics count for the base).
- **A relic keep's gates open to attack only when the owning realm has lost keeps**; the guard strength of a relic
  keep scales with how many keeps the realm still owns (the more frontier keeps the defender holds, the more and
  the tougher the relic guards). The period rule moved between patches (check exact numbers).
- Taking one: break the gate(s), kill the guards and the keep lord/relic guards, one player picks the relic up,
  **cannot use speed/teleport normally, is visible on the map/as a carrier**, and must walk it home to a relic pad in
  their own relic keep (or a claimed frontier keep temporarily?). Dropped relics return after a timer
  (server here: 20 minutes [D: Relic_Return_Time]).
- **Relic gates** (the walls between a realm's frontier and its mainland relic-town path) and the long walk home
  were where the carrier was caught.

### 1.8 Darkness Falls [M, R]
- One shared dungeon, entrance in each realm's **home mainland** (not the frontier). **The realm that holds the most
  frontier keeps holds the entrance**; when ownership flips, the losers inside stay until they die or leave, and the
  new owners pour in and hunt them. Taking keeps "to get DF" was the commonest reason for a keep run on a quiet
  night. Mids retook a keep "within 30 minutes" to get it back [R].
- Inside: levellers of 20-50, seal farmers, and gank groups right after a flip.

### 1.9 Battlegrounds [M, D]
- Instanced-style small frontiers with one central keep, three portal keeps, level-capped and realm-rank-capped,
  **no realm points above a low cap (RR cap about 2L?) — check**, no relics, no DF effect.
  - **Thidranki** 20-24, **Murdaigean** 25-29, **Caledonia** 30-35 (1.49/1.50, early 2002).
  - **Abermenai** 15-19 was added later in the period (about 1.61-1.62, 2003 — check).
- The repo's world has exactly these four as live battleground keeps with brackets 15-19, 20-24, 25-29, 30-35 [D], plus
  rows for later battlegrounds (Molvik, Leirvik, Killaloe, Braemar, Orseo: New Frontiers-era, no bracket) [D].

### 1.10 Which frontier is this server's world? [D]
- `source/tools/release-world-defaults.json`: 81 keep rows. The claimable keeps are the 21 Old Frontiers keeps by
  their classic names, the six relic keeps (level 10), 40 portal-keep rows, and battleground keeps. **No tower rows.**
- `rvr-checkspots.json`: regions 1, 100, 200 with the classic zone IDs; milegates labelled AMG/MMG/HMG.
- `use_new_keeps` defaults to 0 (old keep appearance).
- But the keep **engine** is DOL/OpenDAoC's New Frontiers-capable component engine: `GameKeepTower`,
  `KeepHookPoint`, `HookPointInventory`, `allow_tower_climb`, keep "components" with health
  (`keep_components_base_health`), keep levels with `max_keep_level` = 5 and unclaimed keeps starting at level 4.
  So: **Old Frontiers map and keeps, on a New Frontiers-shaped rules engine**. Each NF-era mechanic the engine exposes
  (hookpoint siege and oil, component/wall damage, towers, climbing, keep-level-as-NF-upgrade) must be checked
  one by one against section 5 before it is called period behaviour.
