# RvR audit: the period reference rubric (Old Frontiers, 1.50-1.65, 2002-2003)

Agent: reference (issue #493). Report only; no game behaviour changes. This replaces
[reference-draft.md](reference-draft.md) (section 1 draft), which stays for the record. The other RvR audits (#494-#502)
judge the code against this file.

**How to use it.** Each section lists what one human in the frontier would *see*, with rough numbers, then a short
**Passes for real** / **Fails** pair. Judge the code against the observable line, not against the mechanism. Where the
period varied by server, by patch or by hour, the range is given; anything inside the range passes.

**Tags** (how hard to lean on an item):
- **[S]** sourced this session; the link is in "Sources" at the end. Era-dated (2001 to January 2004) unless marked
  *retro* (written later, mostly by players of Uthgard, a 1.65 freeshard) or *wiki* (a wiki copy of patch notes, read
  through a page summariser, not word for word).
- **[R]** from the repo's own era research, [../research/rvr-field-1.2/ERA-RESEARCH.md](../research/rvr-field-1.2/ERA-RESEARCH.md),
  which names its sources.
- **[M]** from memory of the period, not re-checked. Use as a guide, not as a number to hard-code.
- **[D]** read from this repo's world data or code.
- **[E]** this rubric's estimate from the above (a stated guess).
A number with no tag is [M].

**Units.** DAoC world units. Melee reach about 200; PBAoE radius about 350; most nukes and mez 1,500; longbow
2,000-2,300; a player on flat ground is plainly visible at 3,000-4,000 and a group's nameplates further on a good PC
[M]. Unhasted run is about 190 units/s [M], so 1,000 units is about 5 s on foot and about 3 s at a speed song.

**The period, in dates** [S unless marked]: launch Oct 2001; first relic taken (Midgard from Hibernia, Lancelot) about
20 Dec 2001; mez/stun immunity rule for RvR in the January 2002 test patch; keep claiming 1.47 (5 Mar 2002); Darkness
Falls 1.49 (9 Apr 2002); realm abilities 1.50 (spring 2002); bindstones, hasteners and AE mez fall-off 1.52 (mid
2002); Shrouded Isles 12 Nov 2002; See Hidden cut 1.62 (June 2003); Foundations June 2003; **1.65 live 8 Oct 2003**;
Trials of Atlantis 28 Oct 2003 (out of scope); New Frontiers 1.70, mid 2004 (out of scope).

---

## 1. The map of the war

### 1.1 Three frontiers, twelve zones [M; D agrees]
- Each realm has a four-zone frontier, walkable only inside itself. **No land link between frontiers.** You cross by
  the porting ceremony (1.3).
  - **Albion** (region 1): Forest Sauvage, Snowdonia, Pennine Mountains, **Hadrian's Wall** (where enemies arrive).
  - **Midgard** (region 100): Uppland, Yggdra Forest, Jamtland Mountains, **Odin's Gate**.
  - **Hibernia** (region 200): Cruachan Gorge, Mount Collory, Breifine, **Emain Macha**.
- Border keeps, the home doors [S, wiki *Frontiers*]: Castle Sauvage and Snowdonia Fortress; Svasud Faste and Vindsaul
  Faste; Druim Ligen and Druim Cain.
- The repo's check-spot catalog uses these regions and zone IDs, and the release world lists the Old Frontiers keeps by
  name [D].

### 1.2 Keeps [M; names D]
- **Seven claimable keeps per realm (21)** plus **two relic keeps per realm (six)**.
  - Albion: Caer Benowyc (front, Hadrian's Wall), Berkstead, Erasleigh, Boldiam, Sursbrooke, Hurbury, Renaris. Relics:
    Castle Excalibur (Strength), Castle Myrddin (Power).
  - Midgard: Bledmeer Faste (front, Odin's Gate), Nottmoor, Hlidskialf, Blendrake, Glenlock, Fensalir, Arvakr. Relics:
    Mjollner Faste (Strength), Grallarhorn Faste (Power).
  - Hibernia: Dun Crauchon (front, Emain), Crimthain and Bolg (Breifine, passed on every Hib run to Emain), nGed,
    Da Behnn, Scathaig, Ailinne. Relics: Dun Lamfhota (Strength), Dun Dagda (Power).
- **What an Old Frontiers keep is.** A walled yard with an outer gate, an inner keep, and the lord upstairs. Walls cannot
  be broken. **No towers** to capture and no hookpoint-mounted defences: those are New Frontiers (1.70) [M, high
  confidence]. You get in through the doors.
- **Correction to the draft: the 1.65 keep layout and climb points.** The 1.65 notes [S, wiki] say a **new frontier
  keep layout** was introduced at every frontier keep except Dun Crimthainn, Caer Erasleigh and Nottmoor Faste: an
  interior door on the lord's tower, one gatehouse door fewer, and rampart doors into the lord's tower that only the
  owners can use. The same notes cut Crimthainn's **climb points** from six to four, "matching the other frontier keeps",
  so by 1.65 a frontier keep had **about four climb points**. Battleground keeps got the new layout too. When climbing
  arrived (before 1.65) was not found. So at 1.65: climbing at a few fixed points is period; broken walls, towers and
  hookpoints are not.
- **Difficulty by depth** [S, Mythic plan of Jan 2002]: keeps near the home side are difficulty 1, keeps deep in enemy
  territory difficulty 5, and the deep ones paid more bonus. The front keeps (Benowyc, Bledmeer, Crauchon) changed hands
  several times on a busy day [M]. The deep keeps were taken mostly to open the way to a relic or to flip Darkness Falls
  [M].

### 1.3 Portal keeps and the porting ceremony [S/M]
- Each realm has **two fixed portal keeps in each enemy frontier's far zone** (APK/MPK in Emain, APK/HPK in Odin's Gate,
  MPK/HPK in Hadrian's Wall) [M; names R]. They cannot be taken; their guards kill enemies who come close; "do not chase
  enemy to Alb TK... you WILL die" [R]. Bindstone from 1.52 [S, wiki].
- **Getting there.** At the home border keep you buy a cheap medallion for the frontier you want, wear it, stand on the
  pad, and wait for **the next porting ceremony**; everyone on the pad goes at once [S for the ceremony, 1.52; M for the
  medallion]. The wait was several minutes; the exact cycle was not found (check). 1.62 removed the *return* wait from
  the Shrouded Isles areas to the portal keeps [S].
- **So arrivals come in waves.** A realm's force leaves its portal keep in clumps a few minutes apart. A death costs a
  wave and a run: "5-10 minutes" [R].
- **The home realm walks.** Hibs buffed at Druim Ligen, raced to Crim or Bolg, met a second group, rebuffed, got a
  report from Emain, then went in [R]. Albs and Mids at home rode or ran from their border keeps. Hasteners at every
  frontier keep and border keep give a free speed buff from 1.52 [S, wiki]. **No boats in Old Frontiers** [M].
- **1.65 login rule** [S, wiki]: a player who logs in near a frontier or relic keep that is under attack or enemy-held is
  moved: attackers to the enemy's portal keep, defenders to their border keep's portal pad; a 10-minute grace after a
  link-death.

### 1.4 Milegates and the wall [R/M]
- **A milegate is the one gap every arrival must pass** between an enemy portal keep and the open zone; the war's main
  chokepoint. The repo's lane survey found each enemy portal keep sits in a closed valley whose only exit is one gate
  about 200 units wide [D].
- Names: **AMG and MMG in Emain** [R]; **MMG and HMG in Hadrian's Wall** [M]; Odin's Gate has the AMG [M] and, by the
  same logic, a Hib-side gate [M, not found; the repo has five milegates, no Hib gate in Odin's Gate: see 9.2].
- What a milegate looked like on a busy night:
  - Arrivals bunched on their side, waiting for numbers [M].
  - The home realm and the third realm outside: casters at max range on the gate mouth, stealthers in the gap and on the
    walls [M]. "3 fg Mids, and the required 1 fg hunters and SB were camping amg" [R].
  - Push through, AE mez, wipe or be driven back to the guards, repeat, for tens of minutes [M]. "AMG AMG AMG all the
    time" [R].
  - Never rest at a milegate [R].

### 1.5 Where the fighting was
- **Emain Macha: most of it.** "80%" [S retro, Qt3; R]. Flat, few aggressive monsters, two realms port in, the Hib route
  in is mostly aggro-free [S retro, Uthgard 2018].
  - Named spots [R, era-dated]: AMG, MMG, APK/ATK, MPK/MTK, Dun Crauchon and its merchant area, Levi ridge, Dhak's Tower
    by AMG, the Grogans plain near Crauchon, "Grannies" right of AMG, Middle Hill, valley 1 (Crim side), valley 2 (Bolg
    side).
  - Routes: AMG <-> MMG "and back all night"; AMG -> Crauchon; Hibs in by the Crim road or the Bolg valley; when Emain
    was too hot, chain to Breifine then the Gorge [R].
- **Odin's Gate**: second. The AMG, the road to Bledmeer, Mids on foot to their relic keep and Dodens Gruva [R]. Yggdra
  was "THE greyganking area" because people levelled there [R].
- **Hadrian's Wall**: third and quietest for group fights; MMG/HMG, Benowyc, the road into Pennine; Pennine camps drew
  gankers of levellers [R]. 1.52 made Hadrian's and Odin's monsters less aggressive and thinned Odin's trees for sight
  lines [S, wiki].
- **Deep zones** (Sauvage, Snowdonia, Uppland, Yggdra, Collory, Gorge): soloers, stealthers, levellers, relic-raid
  columns. An hour could pass with no enemy [M].
- **Frontier dungeons**, open to enemies: Hall of the Corrupt (Albion), Dodens Gruva (Midgard), Marfach Caverns (Hibernia,
  entrance in Breifine, 40-50) [R; M]. 1.65 matched frontier keep XP bonuses to dungeons and gave the biggest XP bonus near
  a keep your guild claimed [S, wiki], so levelling groups sat near keeps and were ganked there.
- Frontier levelling camps the gankers checked [R]: Midgard crab plateau behind Svasud, Blendrake wraiths, Nottmoor
  liches, Glenlock giants; Albion hill giants NW of Sauvage, Pennine fellwoods, cliff crawlers, angry bwca; Hibernia sett
  dwellers in the Gorge and Collory.

### 1.6 Border keeps and guards [M]
- The border keep is the home door: many high guards with long aggro; an enemy who chases there dies.
- Realm guards on keeps shout and assist; from 1.50 they shout less as they attack [S, wiki 1.50]. NPC guards see
  stealthers only at short range (4.1).

### 1.7 Relics [S/M; corrects the draft]
- Six relics, one Strength and one Power per realm. Holding your own gives the base; **each enemy relic adds about 10%
  melee or magic damage** to the whole realm [S retro, Qt3; M].
- **No relic walls or relic gates in Old Frontiers** [M, high confidence]. The walls with gates opened by taking
  "associated keeps", relic towns and the realm war map's red lines are the New Frontiers system [S, wiki *Frontiers*
  describes them as current, NF]. The draft's "relic gates" item is NF; do not hold the code to it.
- **What tied relics to keeps at 1.65** [S, wiki]: part of each relic keep's courtyard guards is tied to frontier keeps;
  when such a keep is enemy-held, its guard no longer defends the relic. So the more keeps a realm has lost, the weaker its
  relic keeps. Taking keeps first, then the relic, was the plan (raiders "often had to take a sizable number of keeps
  first" [S retro, Qt3]).
- **Carrying.** One player lifts it and walks it home to a pad in their own relic keep. No stealth with a relic from early
  2002 [S, Mythic Jan 2002]; no porting with it [M]. Holding someone's relic for more than about a week was rare on a
  busy server [S retro, Qt3]. Relic keeps "require active player involvement" because guards alone cannot hold them
  [S, Mythic Jan 2002].

### 1.8 Darkness Falls [S/M]
- Added 1.49 (9 Apr 2002) [S]. One dungeon; the entrance is in each realm's home lands. "Whoever has the most keeps finds
  their portal open" [R]. A 15-minute grace for the losers is a 1.81 change [S, DOL task list], so at 1.65 the flip was
  immediate for new entries [E]; those inside stayed until they died or left [M].
- Taking a keep "to get DF" was the commonest reason for a keep take on a quiet night [M]; Mids retook a keep "within 30
  mins" to get it back [R]. Inside: levellers 20-50, seal farmers, and enemy gank groups right after a flip [R; M].

### 1.9 Battlegrounds [M; D]
- Small frontiers with **one central keep** and a portal keep per realm, level-capped, and **realm-rank-capped**: you are
  ported out when you level past the bracket or earn past the cap [M]. No relics, no DF effect. 1.52 cut battleground keep
  door strength again [S, wiki], so BG doors were softer than frontier doors.
  - **Thidranki 20-24, Murdaigean 25-29, Caledonia 30-35** [M]; **Abermenai 15-19**, an SI-era battleground (it already
    existed at 1.62, which added its splash screen) [S].
  - The realm-rank caps of 2002-2003 were not found. A later wiki table (after the battleground revamp) caps Thidranki at
    RR1L4 [S, later, *not period*]; at 1.65 the caps were low, around RR1-RR2 [M, check].
- The repo has exactly these four as live battleground keeps (15-19, 20-24, 25-29, 30-35) [D], plus rows for later ones
  (Molvik, Leirvik, Killaloe, Braemar, Orseo: post-period, no bracket) [D].

**Passes for real (map):** arrivals at a portal keep come in waves minutes apart; the milegate mouth is the single
busiest spot in each frontier; Emain carries most of the fights; deep zones are quiet; the front keeps flip often, the deep
ones rarely; a relic falls only after keeps have fallen.
**Fails:** enemies walking in between frontiers; a steady trickle out of a portal keep; deep keeps flipping as often as
the front ones; fights spread evenly over all twelve zones; relic keeps hit with every frontier keep still held.

---

## 2. Who was out there

### 2.1 The kinds of player
- **Solo stealthers** (Infiltrator, Nightshade, Shadowblade; archers: Scout, Ranger, Hunter; Minstrels stealth too).
  Common at milegates and on the roads, often in packs (2.4) [R]. Visible soloing was close to impossible: "invisible or
  join a zerg" [R].
- **Duos and trios**: stealth pairs (an assassin and an archer, two assassins) and small "stealth groups" of 3-8, partly to
  beat See Hidden [R]. Visible duos (a tank and a healer) were rare in Emain and normal in the deep zones [M].
- **The 8-man (FG, "full group")**, 2.2. The unit everyone measured themselves by.
- **Guild groups**: one or two FGs of one guild or alliance on voice or in group chat; alliances of up to 10 guilds used
  alerts to rally the realm [S retro, Qt3].
- **The zerg**: everyone else in an area following a loose lead in realm chat or /say. "Zergs move slow and rarely
  venture too far from their spawn point; AMG for albs" [R]; AE fire everywhere; little speed [S retro, Uthgard 2018].
  Zergs fed the 8-mans, and average groups beat good ones when "3fg+ add onto the fight" [R].

### 2.2 The 8-man and its make-up per realm [R]
- Skeleton for every realm: **two main healers, one main crowd controller, one shield (guard/slam) tank, a speed class,
  two who can cure mez**, plus a Shaman (Mid) or Theurgist (Alb) [R, retro]. A period player's short form: "a speed class,
  two healers, a crowd-control class, and four burst damage classes" [S retro, Qt3].
- **Albion**: 2 Clerics, Sorcerer, Theurgist, Paladin, Minstrel, 2 Mercenaries [R retro]; era advice: two Clerics and a
  Sorc, tanks as killers, open with Speed of Sound, live on two Bunker of Faith [R].
- **Midgard**: 2 Healers, Shaman, Runemaster, Skald or Savage, Savage, Berserker, Warrior [R retro]. "2-3 healers, a
  shammy and for the rest tanks" was the group others found hardest [R].
- **Hibernia**: 2 Druids, Bard, Warden, 2 PBAoE casters (Eldritch/Enchanter/Mentalist), 2 tanks [R]; the tank FOTM: 2
  Bards, 2 Druids, 2 Blademasters, 2 Heroes [R]; caster group: 3-4 Enchanters behind a slam tank [R].
- Stacked formations named at the time: "3 Healers or 3 Savages in Mid, 3 Chanters in Hib, 3 Mercs in Alb" [R].
- Pugs (pick-up groups) were common and did not fit the skeleton: three tanks, one healer, no speed [M].

### 2.3 How many, and when [E from R/S]
- **Prime time** (evening, server time, about 19:00-01:00) on a busy server, in Emain: 2-4 FGs per realm in play,
  plus a zerg of 20-50 at a milegate or keep; "4fg" in Emain was normal; Hibs hit Benowyc "with 50" and Crauchon with 3 FGs
  at once; "groups of upto 20 albs" [R]. Early peaks: three-way AMG fights "with like 100 mids/alb 40 hibs" [R retro].
- **Quiet hours** (about 04:00-12:00 server time): a handful of stealthers and soloers per frontier, one or two FGs per
  realm at most, and night keep takes by small forces against empty defence [M]. Population swung roughly 3-5 times from
  trough to peak [E].
- **Day/night shape matters.** Night raids on undermanned realms were real and won because defenders were offline [M;
  the repo's own night-siege doc says the same].
- No source gives shares of zerg, 8-man and solo play [R]. A defensible picture for prime time in Emain [E]: by head
  count, half or more in zergs and warbands, a quarter to a third in organised 8-mans, the rest stealthers and soloers; by
  fights started, the 8-mans and stealthers start most of them.

### 2.4 RP farming at milegates [R]
- Stealthers "camped amg" in packs and "almost everyone" did it [R]. Groups farmed arrivals at a gate for hours. It was
  the norm, not a scandal. Deaths cost a wave; nobody corpse-camped one body for long because there were always fresh
  arrivals [E].

### 2.5 Realm ranks seen [M; S for structure]
- Realm ranks run 1-10, 100 realm levels in all, one realm skill point per level (1.50) [S, wiki]. In practice by late
  2003 nobody was near RR10 [M].
- What a player saw at 1.65 [M]: most RvR regulars RR2-RR4; RR5 the mark of a serious RvR player; RR6-7 a handful of
  famous names per realm per server; RR1 everywhere among new 50s and levellers. RR5 realm abilities did not exist yet
  (post-1.65) [R].
- A level 50 kill of a level 50 was worth roughly 900 RP plus a little per the victim's rank [S retro, DOL 2004]; small
  fights paid more per head than zergs [S retro].

**Passes for real (who):** at a milegate in prime time you see stealthers, at least one organised FG per realm and a
zerg; in a deep zone you meet a stealther or a leveller, rarely a group; FGs look like the skeleton; ranks cluster 2-4.
**Fails:** every group a perfect skeleton; no pugs; no stealthers at gates; the same population at 04:00 as at 21:00;
lots of RR7+; zergs roaming deep zones at speed.

---

## 3. How an 8-man fought

### 3.1 Speed, scouting and the inc call
- **Speed** decided everything: "practically required" [R]; speed songs ("speed 5", "speed6") from Bard, Minstrel or
  Skald [R]. Speed broke on combat for most songs [M]; Speed of Sound (Alb RA) gave the group twice normal speed for 30 s,
  not broken by combat, ignoring mez/root/snare for movement [R].
- **Scouting**: a stealther ahead or a member on the ridge; guild alerts on claimed keeps [S retro, Qt3; M].
- **The inc call** [M]: "INC", count and direction, in group chat or realm chat: "inc 2fg amg", "8 hibs left of mmg",
  "zerg at crauchon". Arrives 1-5 s after the enemy is seen, often a little wrong in the count.
- **Scatter on sight**: "Learn to scatter as soon as you see any incomming forces", run at the target "in an arc" [R].
  Keep more than 350 apart so one AE mez cannot catch two healers [R retro].

### 3.2 The engage
- The opener is **AE mez on the charge** (or Speed of Sound in for Albs) [R]. AE mez lasts full time at the centre, half
  at the edge, RvR only, from 1.52 [S, wiki]. Resists shorten mez/root/snare/stun, never lengthen them (1.50) [S, wiki].
- **Ranges** [M]: mez 1,500; AE mez radius about 350; nuke 1,500; bolt 1,875; instant stun/mez of Bard/Healer close range
  and needs facing ("can't insta mez you if you're hitting them from behind") [R].
- Who starts: the side that sees first and mezzes first usually wins [M]. A good group holds its damage on the mezzed
  ones and demezzes its own ("DEMEZZ!!!!") [R].

### 3.3 Crowd control and immunity
- **Immunity**: a player (or realm guard) cannot be mezzed, stunned or rooted again until about **60 s after the last one
  wore off**, in RvR only [R; S that the rule is RvR- and guard-only, Jan 2002]. Monster mez also starts the timer from
  1.52 [S, wiki].
- Break a mez only with a stun (slam, caster stun) or a cure; any damage breaks it, so AE damage on a mezzed group is the
  classic mistake [R].
- **Determination** (RA) cuts duration about 15% per level, so tanks talk of "the 5 sec mezz" [R]. Purge (RA) clears
  everything, 30-minute reuse; "do not waste Purge on a mez unless you are a healer, mezzer or stunner" [R].
- Stuns at 1.65: Sun and Moon became a 7 s stun; Stunning Flash range raised to 1,500 [S, wiki].

### 3.4 The assist train
- "Nominate a tank... as main assist. Everyone then makes an /assist macro... when he has a target hit your assist macro"
  [R]. Melee spam the assist macro until a target appears, then stick and swing [R].
- **Kill order** (Jan 2003): casters, healers, bladeturn casters, CC, tanks last: "anything that wears a dress and moves
  their arms about" [R]. Vs Mids: mez the healers, kill the Shaman, then the tanks [R]. Vs Hibs: Bard, then Eldritch, then
  Druids [R retro].
- **Switch triggers**: a caster seen using Mastery of Concentration; whoever was just instantly rezzed [R].
- Speed: a Bard describes being dropped by an assist train "in 2 sec" [R]. Not universal: against three-healer Hib
  groups, "leave the MA and go hit em" [R].
- **Observable**: the first enemy to die is a caster or healer most of the time [E: well over half in a good group, near
  half in a pug]; damage lands on one target in a burst of 1-3 s, then moves.

### 3.5 Interrupts
- A hit, a miss, or a hit absorbed by bladeturn interrupts a caster; a hit on the caster's pet does not [R retro, test
  on a freeshard]. From 1.55 a Guard or Intercept that succeeds does not interrupt the guarded caster [R].
- Interrupt duration at 1.65 is **not sourced**; the 2 s fixed interrupt is a 1.68 change, do not use it [R].
- Who interrupts: Minstrel ("front line rupt"), Theurgist pets, archers on casters, Champion and Warden in Hib tank groups
  [R]. "If a healer purges then keep it interrupted somehow" [R]. MoC lets a caster cast through it for about 15 s [R].
- No era source describes a formal interrupt rotation [R]. Observable: casters at range are hit by anything quick
  (archers, pets, instant DDs), and a caster with a melee on him stops casting.

### 3.6 Peeling and healer position
- "Shield tanks - Guard + intercept + /stick on your casters, slam meleers that get too close" [R]; "if a savage ever
  attacks u stop whatever it is ur doing and stun it" [R].
- **Healers** stand behind the tanks and kite: "run a bit, throw heal" [R]; stun an enemy healer when it is low, not at
  the start [R]. Casters cast after /face while moving; never turn your back on a Berserker or Savage [R].

### 3.7 Extending, kiting, running and wiping
- **Push** casters and Albs; **kite** zergs and tank groups; break off when a member cannot follow [R retro].
- **Overextending**: tanks "dont pan camera and overextend easily" [R]; chasing into enemy guards or a PBAoE group [R].
- **Outnumbered**: "kill and then run like hell to somewhere else" [R].
- **When a group ran** [M]: two healers dead, or a second enemy FG arriving, or a zerg in view. **When it wiped** [M]:
  mezzed on the charge with no demez, or caught by 2+ FGs at once.
- **After**: stick, leave, rest elsewhere. "The place you just fought will be the first place the enemy come looking"
  [R]. Average groups rest where they fought and half go afk after a win [R].

### 3.8 First-generation realm abilities (1.50-1.65) [R; S for 1.50 frame]
- Actives on 5-30 minute reuse, 30-60 s or instant [S, wiki 1.50]. In use: Purge, Group Purge (30 min), Ignore Pain,
  MoC, Speed of Sound, Bunker of Faith, Perfect Recovery, Determination, Raging Power, See Hidden, True Sight, Vanish [R].
- Removed at 1.65: Brilliant Aura of Deflection, Avoidance of Magic, Volcanic Pillar [S, wiki]. **Not period**: RR5
  abilities and anything from Trials of Atlantis or New Frontiers [R].
- Observable: each long timer is used once a fight and then is gone for the night's next 15-30 minutes; good groups save
  them, bad ones "dump or hoard" [R retro].

### 3.9 Resurrection and death
- "Don't leave dead realm mates behind, rez and then move on" [R]. A low-health rez needs a follow-up heal; three Healers
  with Perfect Recovery made "11 players rather than just 8" [R].
- **Rez sickness** applied after a rez [M]; RvR deaths cost no experience or constitution at 1.65 [M]. Release goes to
  your bind (portal keep or border keep) [M]; rezzing in a camped spot is "a vicious circle" [R].

### 3.10 How long fights lasted [E from M]
- FG vs FG: 30 s to 2 minutes from first mez to the last kill; a stalemate with good healers 3-5 minutes.
- FG vs zerg: one pass of 20-60 s, then disengage.
- Milegate ping-pong: tens of minutes with lulls of 1-3 minutes between pushes.
- A gank on a solo: 3-10 s.

**Passes for real (8-man):** speed in, scatter, AE mez first, one target at a time from the back line, a demez within
seconds, a rez after, then leave; FGs run when a second FG appears; fights last about a minute.
**Fails:** every bot on a different target; no opener; mez broken at once by its own AE; no immunity window (re-mezzed
inside 60 s); healers in front; a group that never runs; ten-minute FG fights with no one dying; groups that rest where
they fought every time; ToA or NF abilities.

---

## 4. Stealth and detection

### 4.1 Ranges [S]
- **Normal detection**: about **125 units base, plus 20-25 per level** the looker is above the hider's stealth spec
  (formulas differ by source: 20 in the critshot formula the server uses, 25 on a 2014 compilation of patch notes) [S
  retro; D for the server's own].
- **Detect Hidden** (assassins, from stealth 16): **250 + 50 per level** of difference against non-assassins; against
  another assassin, base only [S retro]. So a level 50 assassin sees a 50-stealth scout at 250, a 40-stealth one at 750.
- **See Hidden** (assassin RA, 1.50-1.65): about **2,700 - 36 x the archer's stealth spec** against an archer or
  Minstrel *without Camouflage*; at spec 50 that is 900, at 40, 1,260 [S, FH thread of about Aug 2003]. **1.62 (June
  2003)** stripped and refunded See Hidden and cut its range on non-camouflaged archers to a quarter to three quarters of
  before, by archer spec and assassin level [S]. Camouflage (archers, level 30, 1.52) cancels it [S, wiki].
- **True Sight** (archer RA): sees all stealthers for 60 s, 30-minute reuse [R].
- **A detection cap** of 1,900 is mentioned on a period-compilation page [S retro]; detection checks pulse every couple of
  seconds, so a fast mover can slip through a range between checks [S retro].
- **Monsters and guards** see stealthers only at a short range set by their level against your stealth [M; D agrees].

### 4.2 What a human saw
- An assassin appears out of nothing at melee range; an archer's first shot comes from 1,500-2,300 with no warning [M].
- A stealther is found by another stealther, by an archer on True Sight, or by walking into a guard [M]. A visible player
  never sees a stealther who stands still at range [M].
- Stealth groups of 3-8 roamed the milegates and deep roads [R].

### 4.3 Openers [M; R]
- **Assassins** (Infiltrator, Nightshade, Shadowblade): a stealth opener from behind or the side (Backstab, then
  Perforate Artery from the side or front), then the chain; poisons on both weapons (damage over time, snare/disease,
  weapon or stat debuffs). A level 50 assassin kills an unaware light-armour caster in about 5-10 s [E].
- 1.62 moved side positionals 15 degrees further back and cut rear styles to a 60-degree arc [S]: by 1.65 "behind" is a
  narrow cone.
- **Archers**: Critical Shot from stealth (a big first hit, a few seconds' draw), then Rapid, Volley, Longshot; archers
  expected to take "out the casters" [R]. No re-stealth for 10 s after a shot [R retro].
- **Assassins against archers**: assassins hunted archers with See Hidden until 1.62; after it, camouflaged archers were
  hard to find and archers on True Sight could find assassins [S; R].

### 4.4 Escape and manners
- Escape [M]: run back into stealth range of friends, Vanish (assassin RA, out of combat into stealth), sprint, purge a
  snare, the zone line, the portal keep's guards.
- Manners [M]: a stealther who loses the opener usually runs; stealthers often /bow or /wave before or after a fair solo
  fight; many left a solo duel between two others alone; many did not (an etiquette, not a rule).

**Passes for real (stealth):** you see stealthers only when they hit you or when you have the tools; openers come from
behind; an archer's first hit comes from far; stealthers cluster at gates and roads; they run when an opener fails.
**Fails:** stealthers seen at 1,000+ by a visible player with no tools; every stealther opening from the front; archers
shooting from 3,000; stealth hunters with See Hidden ranges above 2,700; no stealthers at all.

---

## 5. Sieges

### 5.1 How a take began and was called [M; S retro]
- A raid leader or guild called it in realm or alliance chat: "Benowyc, bring rams". Keep-under-attack alerts went to the
  owning guild with an enemy count; guilds used "Call to Arms" to rally [S retro, Qt3]. Realm-wide broadcast messages
  worked at frontier keeps, relic keeps and the three battleground keeps from 1.52 [S, wiki].
- Reasons, in rough order [E]: Darkness Falls; a relic plan; RP from the defence it drew; a guild wanting a claim.

### 5.2 Rams and siege engines [M; S]
- **Siegecraft** made rams, catapults, trebuchets, ballistae and boiling oil from wood and metal [M]. Players operated them
  in person; dead players could not (1.52) [S, wiki]; enemies could not take control through a door (1.50) [S, wiki].
- **Rams on the doors** were how keeps fell. A small keep take was 1-3 FGs with one or two rams [E].
- **Boiling oil** from the gatehouse onto the ram crew, and defenders' **trebuchets/catapults** against the rams and
  crowd, were the defence [M].

### 5.3 Doors, repair and upgrades [S/M]
- Doors are upgraded and repaired by players with woodworking: /upgrade raises maximum hit points, /repair fills them;
  from Jan 2002 an upgrade mid-fight no longer heals the door [S, Mythic Jan 2002]. Players hauled the wood [S retro,
  Qt3]. Exact door HP were not found.
- Upgraded keeps fall back to the base level when released, guards included [S, 1.64d test notes].

### 5.4 Guards and lord by keep level [S/M]
- Keep **upgrade level 1-10** sets the gear tables of guards, archers, patrollers and lord [S, Mythic plan Jan 2002];
  claiming from 1.47 [S]; from 1.65 a group leader claims with any full group of 8 [S, wiki].
- In the yard: melee guards, archers on the walls, casters, and the **lord** upstairs with a few guards [M]. Guards were
  tough for one FG but the main danger was defenders [M].

### 5.5 Defence [M]
- Defenders on the walls with bows and spells; oil on the ram; casters at the lord room door; a defence group outside to
  hit the siege from behind. Most defences were won by a defending zerg arriving, not by the guards [E].

### 5.6 Duration and numbers [E from R/M]
- An undefended front keep: 10-20 minutes with 1-3 FGs [E]; Hibs took Benowyc "with 50" [R].
- A defended keep: 30-90 minutes, 50-150 people on the field across three realms, often ended by the third realm
  arriving [E].
- **Relic raids**: 100-200 people [E; a 2004 account names about 100 [S]], an evening or a night; deep relic keeps were
  hit late at night or early morning when the victim realm was asleep [M]. Servers crashed on early raids [S retro, Qt3].

### 5.7 What the Herald showed [M]
- The Camelot Herald's realm war page per server: who held each keep, which relics, who held Darkness Falls, updated a few
  times an hour. Players checked it before logging on and argued over it on forums.

**Passes for real (sieges):** a siege has a reason and a call; rams on the gate; defenders on the walls and oil; the take
of an empty keep is quick, a defended one long and decided by who arrives; relic raids are big, rare and late; the
Herald-style news says who holds what.
**Fails:** walls broken; towers taken; NF hookpoint engines; every siege the same size; relic raids every hour; no
defenders on the walls; sieges with no reason; a muster that waits for a fixed head count regardless of the night.

---

## 6. Movement

- **Sprint**: endurance-based, a burst of extra speed [M; exact percentage not sourced]. Used to close or escape.
- **Stick and follow**: melee /stick to the main assist's target; "if he says stick, STICK" [R]. Some stuck "the person
  above you in the mini group window" so the group ran strung out [R]. A group runs in a loose line, not a block [M].
- **Strafing and /face**: circle-strafe so positional styles fail; casters cast after /face while moving [R].
- **Casters at range and line of sight**: casters stand at 1,200-1,500 behind the tanks and use rocks, walls and the
  milegate's edges to break line of sight [M].
- **Melee circling**: melee chase the target round and round; rear and side positionals need it [R].
- **Running through enemies**: a speed group ran through a zerg to reach its casters; a group being chased ran past enemy
  stealthers and mobs [M].
- **A zerg on the road**: a slow, strung-out column, half on horseback where routes allowed, clumping at gates [R; M].
  Zergs had "little speed" [S retro].
- **Horse routes** inside the home lands (some changed at 1.52) [S, wiki]; no horses into the enemy frontiers [M].

**Passes for real (movement):** groups move strung out at a speed song; melee stick and circle; casters back off and use
cover; a zerg is a slow column that bunches at gates.
**Fails:** a perfect block of eight; casters in melee; nobody using cover; zergs at speed; bots moving in straight lines
that ignore terrain.

---

## 7. Etiquette and texture

- **Adds**: a third party adding into a fight was normal and fought over in forums [M; R "3fg+ add"].
- **Solo fights**: some left a solo duel alone; many did not; zergs never did [M].
- **After a kill**: "/laugh at the corpse" [R retro]; /spit, /rude, /cheer, /bow [M]. A bow after a fair 1v1 was
  respected [M].
- **Taunting**: across realms you could not read each other's language; it was all emotes [M].
- **Milegate camping**: normal [R]; complained of but done by "almost everyone".
- **Realm chat** [M]: incs ("inc amg 2fg"), keep calls ("crauchon under attack"), relic news, LFG; plenty of argument,
  some trolling; many guilds talked in alliance chat instead.
- **Mistakes people mocked** [R]: afk on stick, losing stick, healers charging, AE breaking mez, never turning the
  camera, half the group afk after a win, two groups splitting and dying one at a time, chasing into guards.
- **Tells of a human group** [R]: hesitation ("If you see a fg approaching lead by a kobold don't jump the gun"), Bards
  drawing a weapon to look like tanks, tanks fixating on their back attack.

**Passes for real:** emotes after kills, real argument and incs in realm chat, adds into fights, groups that make the
mocked mistakes sometimes.
**Fails:** silence; perfect play; every kill followed by the same emote; nobody ever adding into a fight.

---

## 8. Numbers at a glance [tags as above]

| Item | Period value | Tag |
|---|---|---|
| Claimable keeps / relic keeps | 21 / 6 | M, D |
| Climb points per frontier keep at 1.65 | about 4 | S wiki |
| Porting ceremony wait | a few minutes (exact: not found) | S/M |
| Death cost to get back | 5-10 minutes | R |
| Share of fights in Emain | about 80% | S retro, R |
| Prime-time Emain load | 2-4 FGs a realm plus a zerg of 20-50 | R, E |
| Quiet-hour load | a few stealthers, 0-2 FGs a realm | M |
| CC immunity | about 60 s after it wears off, RvR and guards only | R, S |
| AE mez at edge | 50% duration | S wiki |
| Determination | about -15% CC duration per level | R |
| Long RA reuse | 5-30 minutes, mostly 15-30 | S wiki, R |
| Normal stealth detection | 125 + 20-25 per level difference | S retro, D |
| Detect Hidden | 250 + 50 per level difference | S retro |
| See Hidden (before 1.62) | 2,700 - 36 x stealth spec, cut to 1/4-3/4 at 1.62 | S |
| True Sight | 60 s, 30-minute reuse | R |
| Spread to beat AE mez | > 350 units | R retro |
| FG vs FG fight | 30 s-2 min | E |
| Train swap | a target down in "2 sec" when it works | R |
| Undefended keep take | 10-20 min, 1-3 FGs | E |
| Defended keep | 30-90 min, 50-150 on the field | E |
| Relic raid | 100-200 people, late, rare | E, S |
| Relic bonus | about +10% melee or magic each | S retro |
| L50 on L50 kill | about 900 RP + a little per rank | S retro |

---

## 9. The repo's stated design against the rubric

Read only. These are judgements of the design docs and changelog entries, not of the code; the other audits check the
code.

### 9.1 docs/RVR-FIELD-1.2.md (open-field tactics)
- **Sound against the rubric**: chance-based noticing with a delay and a spotter's call (3.1); an assist train with a
  called target and the 2003 kill order (3.4); AE mez on the charge, never mez the kill target, recast on a resist (3.2);
  guard and peel for healers (3.6); spread > 350 and scatter on "incoming" (3.1); leave and rest elsewhere (3.7); ragged
  retreats, tunnel vision and the mocked mistakes (7); realm playbooks match 2.2; MoC and fresh rez as switch triggers.
- **Ranges**: always noticed within 1,000, never beyond 3,500 (normal). For visible enemies on Emain's flat ground the
  period saw groups at 3,000-4,000+; 3,500 as a ceiling is on the low side but passes. Check that hills and walls block it.
- **Interrupt duty** is the design's own (no era source) and is labelled so. Passes if it looks like "a quick hit on the
  caster", fails if every enemy caster has a shadow from the first second.
- **Population split 55/30/15** is an assumption; 2.3 gives a compatible picture for prime time, but the split must vary
  by zone (deep zones mostly solo/stealth, Emain mostly warbands) and by hour, which the design does not say.
- **The 3-minute move-on rule** is a fairness rule, not period. Period gank groups camped milegates for hours (2.4). The
  rule passes if it means "do not camp one corpse"; it fails the period if it stops groups holding a gate.
- **Skill tiers 30/50/20** and the follow delays fit 3.4 and the reaction-time note; not period-sourced.
- **Not covered by the design** and needed by the rubric: the 60 s CC immunity as a tactic (wait for the window), long
  realm-ability timers used once a fight, demez discipline, healer kiting, and rez-then-leave.

### 9.2 Old Frontiers world data [D]
- **Map: truly Old Frontiers.** The release world has the 21 classic keeps by name, six relic keeps (level 10), 40 portal
  keep rows, the four period battleground keeps with the right brackets, **no tower rows**; check spots use regions 1,
  100, 200 and the classic zones (`source/tools/release-world-defaults.json`,
  `source/server/GameServer/bots/autonomous/data/rvr-checkspots.json`).
- **Milegates: five, not six.** The catalog has HMG and MMG in Hadrian's Wall, AMG in Odin's Gate, AMG and MMG in Emain.
  If each enemy portal keep has its own gate, Odin's Gate should have a Hib gate too; check the Odin's Gate HPK's exit in
  the navmesh (pathing audit, #500).
- **Keep levels.** Albion and Midgard keeps start at level 5; Hibernia's Crauchon at 4, Crimthain, Bolg and Scathaig at 5,
  but **nGed, Da Behnn and Ailinne at 1** [D]. That looks like a data accident, not a period rule; for the siege audit.
- **Engine: New Frontiers-shaped.** The keep code is OpenDAoC's NF-capable component engine: towers, hookpoints and
  hookpoint inventories, tower climbing switch, keep components with health, keep levels with a max of 5. Changelog lines
  speak of "tower lords" and "frontier towers". Each must be checked: **towers, hookpoints and wall damage are not
  period**; **climbing at about four points per keep is period at 1.65** (1.2); a keep level as an NF upgrade is not the
  1.47-1.65 upgrade-by-gear-table (5.4).
- **The 1.65 keep layout** (lord tower door, one fewer gatehouse door, owners-only rampart doors) applies to all frontier
  keeps but Crimthainn, Erasleigh and Nottmoor. `use_new_keeps` = 0 gives the old appearance; whether the door set matches
  1.65 is for the siege and pathing audits (421 door rows, health 2,545 to 180,000).

### 9.3 docs/NIGHT-SIEGES-AND-MARFACH.md and the siege changelog
- **The era-realism note there is right**: night raids won because defenders were asleep (2.3); with flat bot numbers
  all night, a thin night muster is just a weak army.
- **Fixed musters (64 for a keep, 120 for a relic keep) and caps of 128/240** do not match the period's spread (5.6): empty
  keeps fell to 1-3 FGs; defended ones drew 50-150. The owner's direction (size to the realm's RvR force; deliberate small
  strikes) moves toward the rubric. Small strikes should target *keeps* (there are no towers in OF).
- **Door self-repair** (5% per 30 minutes out of combat, none with enemies within 3,000) is not period: doors were repaired
  by players with wood (5.3). Harmless as a stand-in, but defenders repairing should be the visible behaviour.
- **Relic sentinels per keep held, take keeps first** match the 1.65 rule (1.7). The relic pad needing 8 present was not
  found in the period sources; check before treating it as period.
- **Healers raising the fallen and the army staying together** match 3.9 and 6.
- **Battleground siege engines and storms** match the period in kind; BG doors were weaker than frontier doors (1.9).

### 9.4 docs/BOT-BUILDS-AND-RVR.md
- The 2003 eight by role, era-weighted builds and a pet cap match 2.2 (the community complaint of ten Cabalist pets was a
  real tell of a non-period roster). The `/lfg pbae` style asks are a convenience, not period.

---

## 10. Gaps left open
- The porting ceremony's exact cycle in 2002-2003; the medallion details.
- The Hib milegate in Odin's Gate; the portal keeps' proper names.
- Battleground realm-rank caps at 1.65.
- When climb points arrived (before 1.65) and whether all keeps had them.
- Door hit points, ram damage, guard counts and lord level by keep level in Old Frontiers.
- Rez sickness rules and the 120 s frontier immunity that 1.70 cut to 5 s (what started it before 1.70).
- Interrupt duration, sprint and speed-song percentages at 1.65.
- Shares of zerg, 8-man and solo; a US server's day/night curve.
- FreddysHouse and the Wayback Machine could not be reached this session; the FH items are as the repo's research note
  read them on 2026-11-16. The DOL forum link the repo note gives for the 60 s immunity (p=133788) opened on a different
  archived Mythic post this session; the immunity's RvR-only scope is confirmed by a Jan 2002 news item, the 60 s by the
  repo note.

---

## 11. Tester's checklist: one hour of frontier play (one page)

Stand where it says, watch, tick what you see. A tick in **Fail** is a finding; cite the place, the time and a log tag.

**Arriving (10 min, at your realm's portal keep in Emain, Odin's Gate or Hadrian's Wall)**
- [ ] Enemy and friendly arrivals come in **waves minutes apart**, not a trickle. Fail: a steady stream.
- [ ] Your realm bunches inside the milegate before going through. Fail: everyone walks out alone.
- [ ] At prime time there is fighting at the milegate mouth within 10 minutes. Fail: an empty gate all hour.

**At the milegate (15 min, watch from your side, out of range)**
- [ ] Stealthers in the gap or on the wall (you only see them when they strike). Fail: none in an hour, or seen at range.
- [ ] Enemy casters sit at about 1,500 on the gate mouth. Fail: casters walking into melee.
- [ ] Pushes and lulls ("ping-pong"), with 1-3 minute gaps. Fail: one endless brawl or nothing.

**A field fight (watch at least three)**
- [ ] The attacking group opens with AE mez or a speed charge. Fail: no opener, just a run-in.
- [ ] They scatter (> 350 apart). Fail: a tight block of eight.
- [ ] Damage lands on one target at a time; the first death is a healer or caster in most fights. Fail: damage spread
      over everyone.
- [ ] A mezzed member is cured or stunned out within seconds. Fail: nobody demezzes.
- [ ] No one is mezzed or stunned again within about a minute of the last one ending. Fail: chain CC.
- [ ] Healers stay behind and back off when hit. Fail: healers in front.
- [ ] The fight ends in 30 s-2 min. Fail: five-minute FG fights with no deaths.
- [ ] The loser runs when a second enemy group or a zerg appears. Fail: they fight to the last every time.
- [ ] The winner rezzes and leaves the spot. Fail: they always rest where they fought (some resting is fine).

**Solo and stealth (15 min alone in a deep zone, e.g. Pennine, Yggdra, Collory)**
- [ ] Mostly quiet; maybe one stealther or leveller. Fail: zergs or FGs every few minutes.
- [ ] An assassin opens from behind or the side, at melee range. Fail: from the front, or visible from far.
- [ ] An archer's first hit comes from 1,500-2,300. Fail: from beyond 2,300 or with no draw.
- [ ] A stealther who fails the opener runs. Fail: always fights to the death.

**A keep (if one is attacked this hour)**
- [ ] Someone calls it in realm chat; there is a reason (DF, a relic, the front keep). Fail: silent sieges.
- [ ] Rams at the gate; defenders on the walls; oil or engines against the ram. Fail: walls broken, towers taken.
- [ ] An empty keep falls in 10-20 minutes to a small force; a defended one lasts much longer. Fail: every siege the same
      size and length.

**Texture (all hour)**
- [ ] Realm chat has incs with a count and place ("inc 2fg amg"), a little wrong sometimes. Fail: silence, or perfect
      counts.
- [ ] Emotes after kills, varied. Fail: none, or the same one every time.
- [ ] Some groups make the mocked mistakes (AE breaks a mez, someone chases into guards). Fail: flawless play.
- [ ] Group make-ups look like the realm's 2003 eight, with some pugs. Fail: all identical, or ten pets.

**Write down**: start and end time, zone, how many enemy groups seen, fights watched and who died first, any Fail with
its place and time.

---

## Sources

Read this session (paraphrased; summariser tool, not word for word):
- Patch notes 1.50, 1.52, 1.65 (Camelot Herald wiki copies):
  [1.50](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.50),
  [1.52](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.52),
  [1.65](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65) (wiki).
- [1.62 goes live, GameBanshee, June 2003](https://www.gamebanshee.com/3r6y): See Hidden, positionals, Abermenai, portal
  wait.
- [1.64d test notes, GameBanshee, Aug 2003](https://gamebanshee.com/3f6q): keeps revert on release.
- [Old Frontier keep claim, Mythic, Jan 2002 (DOL archive)](https://dolserver.sourceforge.net/viewtopic.php?p=133791):
  difficulty 1-5, upgrade 1-10, claiming.
- [Door upgrades and relic stealth, Mythic, Jan 2002 (DOL archive)](https://dolserver.sourceforge.net/viewtopic.php?p=133790).
- [Mez/stun code altered, GameBanshee, Jan 2002](https://www.gamebanshee.com/news/67967-camelots-mezstun-code-altered.html):
  RvR and guards only.
- [First relic captured, GameBanshee, Dec 2001](https://www.gamebanshee.com/38ah).
- [Scouts and stealth, FreddysHouse, about Aug 2003](https://forums.freddyshouse.com/threads/scouts-and-stealth.139740/):
  See Hidden and Detect Hidden tables.
- [Stealth compilation, disorder.dk, 2014](https://disorder.dk/daoc/stealth/) (retro, cites 2001-2002 notes).
- [Uthgard: how See Hidden works, 2010](https://www.uthgard.net/forum/viewtopic.php?p=146838) and
  [Uthgard stealth thread](https://www.uthgard.net/forum/viewtopic.php?f=32&t=12717) (retro).
- [Uthgard: what made each realm's zerg work, 2018](https://uthgard.net/forum/viewtopic.php?p=471498) (retro).
- [Qt3: RvR in GW2 and DAoC, 2013](https://forum.quartertothree.com/t/realm-vs-realm-in-guild-wars-2-and-dark-age-of-camelot/71405)
  (retro).
- [DOL formulas thread](https://dolserver.sourceforge.net/viewtopic.php?p=14715) (realm points, 2004, retro) and
  [DOL code task list](https://dolserver.sourceforge.net/viewtopic.php?p=133792) (post-1.65 patch items, used only to date
  rules as later).
- [New Outriders guild history](https://newoutriders.org/?p=2151): patch dates 1.45, 1.47, 1.49, SI, Foundations, ToA.
- [Battleground limits, wiki](https://camelotherald.fandom.com/wiki/Battleground_Limits?oldid=8531) (later, not period)
  and [Frontiers, wiki](https://camelotherald.fandom.com/wiki/Frontiers) (New Frontiers, used to mark NF items).
- [Nick Yee, Daedalus player story, 2004](https://nickyee.com/daedalus/archives/000858.php?page=4) (a relic raid of
  about 100; 2004, edge of period).

Via the repo's research note (read there; FreddysHouse could not be reached this session): FH threads of Jan, Jul, Aug,
Nov 2003 and Jan 2004; Uthgard guides 2009-2018; Allakhazam Ranger guide May 2002; GameBanshee DF news Apr 2002.
