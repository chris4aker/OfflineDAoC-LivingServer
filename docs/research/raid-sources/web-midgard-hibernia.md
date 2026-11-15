# Midgard and Hibernia big bosses: period research notes

Compiled 2026-11-14 for the OfflineDAoC Living Server bot raids. Every boss section is my own summary.
Short phrases in quotation marks are the only verbatim text.

## How to read this file

- **[P]** marks a period source (2001-2006). **[L]** marks a later source (2007-2012 database comments or
  later Mythic patch notes). **[S]** marks a secondary source (freeshard bug trackers and wikis). [S]
  sources describe their own server's version of a fight, not proof of what live servers did.
- Confidence is given as high, med or low. High means two or more independent period sources agree, or
  one detailed period guide says so first-hand.
- "Corpus" means the local Wayback captures in `/home/claude/raid-research/wayback/`. The path and
  capture timestamp are given for each.
- Live web fetches were made on 2026-11-14. The tools refused daocpedia.com (robots), and I did not
  work around that. I could not reach Wayback directly from this session, so archive material comes
  only from the existing corpus.
- Levels are reported three ways: Allakhazam DB (manual entries), capnbry.net radar (2002-2003
  packet-radar captures, min/avg/max) and Carne's map PDF (2003). They sometimes disagree, and the
  disagreements are noted.

### Key sources (cited by short tag below)

| Tag | Source | Date | Type |
|---|---|---|---|
| CARNE-TG | Carne DelMuerto (Midgard/Gawaine), *Tuscaran Glacier map, hunting guide and loot list* (PDF). Tips are taken from Dabokel's VN thread `vnboards.ign.com/message.asp?topic=46131774`. Live: https://camelot.allakhazam.com/Maps/Tuscaran_Glacier_Carne.pdf. Corpus: `d2-raid-maps-20261113/camelot.allakhazam.com/875b31e65c8ccdf8_20030821163328.bin` | PDF created 2003-07-22; captured 2003-08-21 | [P] high |
| FH-TG | FreddysHouse, "TG raids again", thread 134596, pages 1-5. https://forums.freddyshouse.com/threads/tg-raids-again.134596/ | 2003-07-07 to 2003-08-24 | [P] |
| VN-GALLA | VN Boards (IGN Vault), "Galladoria Notes", topic 41447313, pages 1, 2, 3 and 5. Corpus: `d3-epic-threads-20261113/vnboards.ign.com/{127c8d89a36f217c,1b195021fd277eb0,9772f51289cc72b7,1f347df88a72c8c4}_200307121*.html` | posts 2002-12-08 to 2003-01-17; captured 2003-07-12 | [P] high |
| VN-OLC | VN Boards, "Olcasgean has been defeated!", topic 49803406 (Stonehenge EU, first live kill). Corpus: `d3-epic-threads-20261113/vnboards.ign.com/24c8407e07756744_20040405082239.html` | 2003-05-07; captured 2004-04-05 | [P] high |
| THREDH-GALLA | Thredh (Guinevere), Galladoria guide (Vault guide `camelotvault.ign.com/thegame/guides/may03/galladoria.shtml`). Italian translation by Valmerwolf/Tabris: http://www.valmerwolf.com/mappe/guide/galladoria.htm | guide dated 2003-05-24, plus a 1.62 note; translation 2005-10-14 | [P] high (translated) |
| THREDH-TS | Thredh, Tur Suil guide (Italian translation): http://www.valmerwolf.com/mappe/guide/tursuil.htm | 2003-05-27 | [P] high |
| THREDH-FOMOR | Thredh, "Hydra Guide" (2003-08-17), quoted in English in FH-MYRD. Italian translation with extra notes from Valmerwolf and Borealis: http://www.valmerwolf.com/mappe/guide/fomor.htm | 2003-08-17 and 2003-09-18 | [P] high |
| FH-GALLAFE | FreddysHouse, "Final encounter in Galladoria", thread 123118 | 2003-04-22 to 2003-04-25 | [P] |
| FH-MYRD | FreddysHouse, "Myrddraxis Fun Raid", thread 157841 | 2003-11-15 to 2003-11-18 | [P] |
| FH-FOMOR | FreddysHouse, "Fomor City & Bosses", thread 168758 | 2005-08 | [P] |
| FH-CUUL | FreddysHouse, "How to kill Cuuldurach", thread 124821 (Edlina, Prydwen) | 2003-05-03 to 2003-05-04 | [P] high |
| FH-DR2002 | FreddysHouse, "Ok, guys, dragon raid time!", thread 97548, pages 1-2 | 2002-09 | [P] |
| FH-GJALP | FreddysHouse, "GoV Dragon Raid Sunday Nov. 9th", thread 157076. It translates a guide by Sanys Sil (Fedaykin, Mid/Dartmoor) | 2003-11-07 | [P] high |
| FH-GJALP08 | FreddysHouse, "Dragon Raid - Friday", thread 226840 | 2008 | [L] |
| VN-DRAGQ | VN Boards, "Your dragon questions answered" (Drasticle). Corpus: `d3-epic-threads-20261113/vnboards.ign.com/1ef9d0c3c138d894_20050429010915.html` | captured 2005-04-29; thread c.2004 | [P] |
| SALARIS | VN Boards, "Salaris' Guide to Albion Epic Encounters". Corpus: `d1-df-legion-20261113/vnboards.ign.com/d93a49692f4553c9_20030407233031.html` | captured 2003-04-07 | [P] |
| ZAM-{id} | Allakhazam bestiary page and its comments: `https://camelot.allakhazam.com/db/search.html?cmob={id}`. Each comment's own date is given. | comments 2001-2012 | [P] or [L], by comment date |
| CAPNBRY | capnbry.net radar mob summaries, e.g. http://capnbry.net/daoc/mobs.php?z=160 (TG) | ©2002, data 2002-2003 | [P] med |
| HERALD | Camelot Herald patch and read-file notes in the corpus, `7b-herald-articles-20261113/www.camelotherald.com/*` (ids below) | 2002-2005 | [P] high |
| HERALD-DRAGON | Herald zone-release article ("Dragon Behavior" section). Corpus: `7b-herald-articles-20261113/www.camelotherald.com/b86ba8631b57beba_20051220095559.html` (`/more/193.php`) | c. 1.45-era, 2002; captured 2005 | [P] |
| ZAM-STORY762 | Allakhazam news, "Gjalpinulva Was Too Low": https://camelot.allakhazam.com/story.html?story=762 | 2002-02-27 | [P] |
| MAP-IARN / MAP-TROLL | Carne's Iarnvidiur's Lair map (updated Oct 2003) and Beeblebrox's Trollheim map, both on Allakhazam (`/Maps/Midgard/dungeon/iarnvidiurslair/Iarnvidiurs_Lair_Carne.jpg`, `/Maps/trollheimlair/trollheim_beeble.jpg`) | 2003 | [P] |
| GB-2007 | darkageofcamelot.com Grab Bag, 2007-08-24 | 2007 | [L] official |
| EDEN-* | eden-daoc.net bug threads (t=1173 Cuuldurach messengers, t=2486 Iarnvidiur) | 2022-2023 | [S] |
| UTH-GJALP | uthgard.net forum, Midgard dragon raid (t=19911) | 2010s | [S] |
| JOL-* | jeuxonline.info forum threads (French): Mortufoghus 288849, Gjalpinulva archive 86467 | 2002-2004 | [P] |

---

# MIDGARD

## Tuscaran Glacier (Tuscaren Glacier), Shrouded Isles epic dungeon, Delling Crater 55k/9k

### Zone-wide mechanics

- **Two respawn clocks.** One covers the griffon and undead area, the other the giant area. A clock
  starts when you pass a set point or kill a primary named boss. It then runs 8-10 hours, at least 8,
  and a broadcast warns shortly before the area repops. Mobs near the entrance use normal spawn timers.
  Everything past the first undead tunnels uses the special timers. [CARNE-TG, high]
- **Patches.** On 2004-04-15 a read-file let players zone into Tuscaren Glacier in Delling Crater
  again [HERALD /more/... b467a5056f826832, P]. In January 2003, non-boss mobs in Caer Sidi,
  "Tuscarian Caverns" and Galladoria got more coin and higher drop rates [VN-GALLA p5, quoting patch
  notes 2003-01-14]. Patch 1.62 (mid-2003) fixed the Skuf/Steinvor encounter (see below) [FH-TG p3].
- **Dominator triplets.** Every Hrimthursa Dominator comes with 2 adds however it is pulled. Hit only
  the Dominator and have the shaman kite the 2 adds. [CARNE-TG]
- **Puller.** Use a solo puller, ideally a Shaman with Endurance 5. [CARNE-TG]
- **Radar extras.** The radar shows environmental objects: "falling ice" (lvl 99-108), "freezer"
  (99-102), "Morkimma" (50-55) and four DUNGEON SPAWNER entries (giants and griffons). These are
  probably the zone hazard and respawn controllers. [CAPNBRY z=160, med]
- **Raid size and length.** FFA modifier raids ran with about 60 people and took 5-6 hours. Sunday
  raids of about 80 were "too large to run well", and smaller Friday raids went better. The King was
  attempted only when more than 9 fighter groups were present. [FH-TG p3-p4]
- **Route.** One period raid ran: Issorden → Fornfrusenen → Spine room → Councils → Icelords → Elders
  → Queen Kula and King Tuscar. They usually skipped Hurika and Torst ("45+ min with a 60 zerg, poor
  drops"), Jailer Vifil (an hour's detour) and "the Jailor" (about 1.5 h). [FH-TG p3, 2003-07/08]
  Note: FH-TG lists "Vilfil" and "Jailor" as separate skips, but they are the same mob, Jailor Vifil.
  CARNE-TG's order differs: Ozur → Hord → Otrygg → Vagn/Nokkvi → Agmundr → Kvasir → Jailer → Hakr →
  Steinvor/Skuf → Suttung/Hjalmar → Birghir/Guthlac → King and Queen.
- **Room layout** (CARNE-TG map):
  - Entrance → Shard Room (Fornfrusenen and 2 shards) → Griffon Aviary (Hurika, Torst, Issorden).
  - Dragonspine Room (Vagn and Nokkvi).
  - Council Chamber (Hord, Ozur, Otrygg, Agmundr).
  - Hrimthursa Chamber, then Kvasir and Hakr toward the Jail Room (Jailer Vifil, with a teleport
    point "T").
  - Lord's Room (Steinvor, Skuf, Hjalmar(?), Suttong).
  - Royal Chamber (King Tuscar, Queen Kula, Birghir, Guthlac, Throne Guards).

### Levels (DB / Carne / radar)

| Boss | Level |
|---|---|
| King Tuscar | 85 (giant, slash) |
| Queen Kula | 85 (giant, crush) |
| Hurika | 85 (crush) |
| Torst | 85 (thrust) |
| Jailor Vifil | 82 (crush) |
| Elder Icelords Hjalmar, Suttung | 81 |
| Elder Council Birghir, Guthlac | 80 |
| Icelords Agmundr, Hakr, Kvasir, Skuf, Steinvor | 80 (crush) |
| Council Vagn, Nokkvi | 78 |
| Council Hord, Ozur, Otrygg | 77 |
| Issorden | 78 (undead) |
| Fornfrusenen | 75 in DB, **82** in Carne, **88** on radar (conflict; the radar is likely right for live 2003) |
| Fornfrusenen Shard | 75-81 |
| Griffon Icewing | 75-79 |
| Royal Iceweaver | 78 |
| Throne Guard | 77-79 |
| Hakr's Barrier | 50 (a "magical" object mob) |

[ZAM-6029..6076, CAPNBRY, CARNE-TG]

### Issorden (78, undead)

- The puller runs ahead down the slide and aggroes Issorden plus about 10 Icebound Skeletons, then
  dies at the bottom. Nobody heals or fights. Rez him afterwards.
- **Skeleton "wall" leash.** Halfway toward Issorden every skeleton in the room aggroes, and the
  puller runs back. There is a boundary where the skeletons freeze: step toward Issorden and they
  come, step back and they stop. Use it to pull a few at a time. You must stand between Issorden and
  the mob to hit it; attacks from the puller's side of the wall get "dodged".
- Clear all the adds, rest 60 s, then kill Issorden. He is not much harder than the skeletons but
  casts an **AoE root**, so everyone should close to melee at once. [CARNE-TG, high]

### Fornfrusenen (75/82/88) and Fornfrusenen Shards

- Clear the 10-15 Icebound skeletons on the way. The puller runs in until the Shards aggro, then sits
  at the back wall. The raid hugs the right wall coming in.
- The Shards then stay frozen unless someone attacks them, so **no AoE**. Everyone targets Fornfrusenen
  at the back and charges.
- He "hits very very hard": 2-shots most buffed players and 3-shots most warriors. Whoever gets aggro
  must yell, and all healers keep that person up.
- Leave the shards alone; they drop nothing. [CARNE-TG, high] (One 2005 comment says a shard dropped
  a jewel [ZAM-11275], which contradicts "no loot".)

### Hurika and Torst (85, the named griffons above Issorden's room)

- Advice: skip them. With 60 people expect at least 45 minutes. [CARNE-TG; FH-TG agrees]
- **Pull.** The puller groups up, climbs the wall left of Fornfrusenen's entrance to the highest perch,
  bolts the griffon, then jumps. If he lands at full health he is left on 1 HP, so a healer heals him.
  The griffon then kills the puller's group, and the raid engages. [CARNE-TG]
- Killed by about 60 people on Excalibur EU on 2003-06-06 by "bolt-pull and jump". It was "fairly
  easy" at that size, with poor drops. [ZAM-6059 comment, P]

### Council Ozur (77, crush)

- Straight melee, nothing special. Pull him first, after clearing the non-named near Hord and Ozur or
  you get adds. [CARNE-TG]

### Council Hord (77)

- Heals to full at **random**, sometimes repeatedly ("wasn't your night"). Throw everyone at him,
  RMs and SMs included with staves; healers stop meleeing only to heal. [CARNE-TG, med]
- For bots: a random full self-heal with no visible trigger.

### Council Otrygg (77, crush)

- Pull every mob to the T junction. Disease-pull the Rooks: they don't bring a friend (BAF) on a single
  disease cast but can on a bolt pull. Then pull Otrygg to the T and don't fight him before he gets
  there.
- He "summons a ton of purple pets", but they **stay at his spawn unless you get close**. Fought away
  from his spawn he is as easy as Ozur. [CARNE-TG, high]

### Council Vagn and Council Nokkvi (78, duo)

- Clear the room's adds. Everyone targets Vagn. The shaman recasts End 5, bolts Nokkvi and runs for the
  spine with Nokkvi chasing.
- By the time the shaman reaches the spine Nokkvi gives up and Vagn should be dead. Nokkvi then walks
  back "through the wall", which takes about 5-6 minutes.
- Once back, Nokkvi is no tougher than Vagn. [CARNE-TG, high]
- Contrary report from 2005: Nokkvi has "like a million hit points" and resisted every spell. Tanks hit
  for "1 (-44)". Advice was fast 1-handers and many tanks, with someone kiting Vagn. [ZAM-4690,
  2005-08-06, P]

### Icelord Agmundr (80, crush)

- He seems to block the way into the Hrimthursa Chamber but doesn't. Take the ramp on the right to the
  floor and clear it; any Seer left on the floor joins when Agmundr is pulled.
- There are 4 Seers, 2 on each upper platform. Pull as many as possible down.
- Come up the far ramp behind him and pull. He **calls any Seer you missed**. Kill the Seers first,
  then him. [CARNE-TG]
- FH-TG reports a 24-person raid killing the Council and Agmundr on 2003-07-20 before wiping.

### Icelord Kvasir (80, crush)

- He appears to sit with 3 adds but has 2; the rightmost comes separately. The shaman kites Kvasir
  while the raid kills the 2 adds, then brings him back.
- **Long PBAoE mez.** He is also a broadcast "spammer" (turn off /b).
- **Heals from cold**: no cold spells or cold procs. He thanks whoever healed him by name. [CARNE-TG, high]

### Jailor Vifil (82, crush)

- He sits south of the big room. Go all out on damage. It is a long fight, and he **teleports people
  unless kept backed into a corner of the room**. [CARNE-TG]
- The map marks a "Jailer teleport point" T near the Hrimthursa Chamber. [CARNE-TG map]
- FH-TG skipped him: about 1-1.5 h for poor loot.

### Icelord Hakr (80, humanoid, crush), with "Hakr's Barrier" (lvl 50 magical mob)

- Pull from the hallway up to Hakr. First pull his 2 **Royal Iceweavers**, which also brings 2 more
  from near the throne room; the shaman kites those 2 to the big room.
- Then "/stick, attack, sprint and go afk 10 minutes". He **keeps teleporting you backwards**, but close
  enough to re-stick. [CARNE-TG]
- FH-TG says "Hakr (if not bugged again)". The encounter was buggy in mid-2003.
- Hakr's Barrier is listed as a separate magical mob [ZAM-6040]. Its role isn't documented; it is
  probably a shield object.

### Icelords Steinvor and Skuf (80, duo)

- **Steinvor summons Seer adds to help Skuf.** The Seers come from the tunnel you entered by and depop
  when they reach the intersection north of the little alcove.
- Clear the Throne Guards in the room nearest the King and Queen first, because of the bosses'
  "mad warping around". Put the whole raid in the far back cave east of their room; from there you
  can single-pull each named. Standing in the 4-way gets you 4 mobs at once. [CARNE-TG]
- **Patch 1.62** (2003-07/08) fixed bugs in the Skuf/Steinvor encounter. Steinvor's Seers and both
  bosses' magic were improved, and Skuf's Seers now spawn properly, so the fight got harder.
  [FH-TG p3, med]

### Elder Icelord Suttung ("Suttong", 81, crush) and Elder Icelord Hjalmar (81)

- Suttung has only a low PBAoE (200-300). He semi-berserks: 300-400 a hit, then 600-700, then back.
  3-5 groups should do it. [CARNE-TG, marked "Incomplete info"]
- Hjalmar: no mechanics found. He drops bows and quivers. [ZAM-6034]

### Elder Council Birghir and Guthlac (80, duo)

- They **cannot be single-pulled**, so expect heavy casualties.
- Straight melee plus minor DDs or bolts on seemingly random people. [CARNE-TG]

### King Tuscar and Queen Kula (85 each)

Rated "BVIF" (very important fight) in a 2003-05 comment. The King looks like a normal Hrimthursa giant.
[ZAM-6072]

**Original tactic** [CARNE-TG, high, the most detailed period account]:

1. The puller goes to the room edge and bolts or DoTs the King. **When he steps outside the room both
   King and Queen freeze**; they cannot leave the room.
2. The puller backs up to the wall across from the room. The raid enters along the **right wall** to
   the **NW corner**, using the raised path border.
3. **Healers stay outside the room. Kill all pets**, because pets can aggro the King and Queen.
4. Pull the **Queen to the NW corner** and melee her there. The King should not move. **No AoE.**
5. **Healers cure mez on people "transported to the middle of the room"** (a teleport plus mez
   mechanic).
6. Keep the Queen in the corner until the King aggroes. That leaves enough lead to finish her, back
   out, regroup and finish the King.
7. **PBT works on the Queen, not on the King.**

**Outcomes:**
- About 60 people killed the Queen "fairly easily". The King was harder. [FH-TG p3, 2003-07-27]
- The pair died again on 2003-08-02 "with a lot more effort". [FH-TG p3]

**Gaps:** no hit numbers or HP for the King or Queen were found.

### Griffon Icewing (75-79, animal)

Normal aviary mobs. No boss mechanics found. Killing them lowers Klippa Vaktaren and Vind Ryttaren
faction and raises Hrimthursa. [ZAM-6039]

---

## Trollheim (Gripklosa 24k/32k): Nosdoden, the Rift, Adimedus

### Nosdoden (70 in DB and radar; mutated dragon / "worm")

**Mechanics** (ZAM-4687 description, written by players, c. 2003):
- Cons purple to level 50.
- Melee about **900 per hit**, about **200 per spell cast**.
- **Spit** for about 800, sometimes a **double spit for 1600**.
- **When an attacker dies, Nosdoden creates a ghost of that attacker** that fights the raid. The ghost
  cons purple to 50 and **disappears if the dead attacker is rezzed**.
- A 2003-05-13 comment says the shades also expire after a set time ("the shade of me killed me after
  I'd just been rezzed").

**Tactics:**
- 2003-05-18 [P]: they did under 10% before "our dead outnumbered the living". Advice: put as many as
  possible on rez and heal duty to keep ghost numbers down, and keep **all casters out of the way** to
  avoid being one-shot. Rezzing or healing pulls immediate aggro, so keep tanks near the rezzers.
- 2003-10-27, Mid/Bors [P]: 42 people (10 at level 50, the rest 40-48). They first killed the
  "nedfalls" (small skrat-looking adds). A main aggro group ran in first and the healers focused on
  it; everyone else hit from behind. Three deaths in about 30 minutes.
- 2005-07-17 [P]: Warrior, ML9 Spiritmaster, Skald, Healer and 2 bots killed him in about 30 min. The
  warrior tanked with the SM damage shield on him.

**Patches:**
- 1.65 (Sep/Oct 2003): Nosdoden drops more items [HERALD 8eed317c39b2b831].
- c. 2004: Nosdoden's Remains trophy set to power level 35, and the behemoth-potion trophy bug fixed
  [HERALD 9394fb91730693fc, ef8318acd87b5694].
- 1.100 (2009-12-15): new abilities; his monsters raised in level [L].
- 1.101 (2010-02-24): greatly reduced in level, more HP and defences, new tricks, and "new mechanics
  to target inattentive players" [L].

Confidence: med.

### The Rift / "Rift" (Rift 99, The Rift 77) and Trollkarl Bonchar's Void

- **The Rift**, first reported by Midgard players (quoted on VN-GALLA p1, 2002-12-08) [P]:
  - Hitting the Rift spawns about 10 "mjorvaen" that con red or purple to 50. They have huge HP and a
    reverse proc that **PBAoEs everything for 300+**. They respawn, so you can't fight them at range.
  - **Kiters** take their aggro and run them around the room's edge; the energy ripples hit you if
    you're too close to the rift.
  - A good kiter can drag the adds onto the room's other mobs, which then fight them.
  - Everyone else zergs the Rift; damage add is essential. Don't fight the adds: they are slow and
    don't hit very hard.
- 2003-05-18, Kay [P]: two groups handled adds (Suppression SMs stack well on them). Everyone else hit
  the Rift, targeted with the nearest-monster hotkey. Healers stood at the base of the stone with a
  couple of aggro magnets. The first known kill on Kay gave **no loot, no death message and no XP**.
  [ZAM-6008]
- 2004 [P]: done with one full group, using PAC healer AoE stuns and PBAoE on the adds. [ZAM-6008]
- Trollheim map [MAP-TROLL]: "Trollkarl Bonchar spawns Void (level 99 mob) on death. Void spawns
  variable number of level 60 mobs." This is probably the same Rift/Void encounter.
- Shade of Adimendus (76) and Adimedus (70) have no data.

Confidence: med.

---

## Iarnvidiur's Lair (Iarnwood 28k/39k)

**Layout** [MAP-IARN, Oct 2003]: a winding cave. The final boss room is a **circle cut in half by a
water channel, crossed by a bridge**. **Hati** (left) and **Skoll** (right) are wolves at the bridge, and
**Iarnvidiur** waits beyond it. On the way: Delegalt (cross-shaped room), Droom, Mahattava (lake room
with waterfall), Dyranapur (water nodes), Stanga, Ancient Syver.

**Bosses:**

| Boss | Level | Notes | Source |
|---|---|---|---|
| Iarnvidiur | 65 (giant) | No period mechanics found | ZAM-4673 |
| Hati, Skoll | 65 / map "1-50" | | ZAM-4672, MAP-IARN |
| Delegalt | 70 | In a room full of Animus and dogs. **Clear the room first or the whole room adds.** Two groups were wiped by hallway adds. | ZAM-4670, 2003-05 and 2003-10 [P] |
| Droom | 69 (plant, "named mushroom") | Melee hits of 590 plus a 90 poison. Poison DoT plus disease reduces heals. A group of 14 with 4 healers and 2 shamans struggled until the shamans chain-cast cure disease and cure poison. Also killed by 6 people. | ZAM-4669, 2003-05/06 [P] |
| Mahattava | 70 (fjarskafinn, lake room) | Hits 300-400, high HP | ZAM-4666, 2003 [P] |

- [S] Eden 2023 bug report: Iarnvidiur **glares**, and her **teleport moves random players into the
  water**, where the dead can't be rezzed ("if enough healers are ported into the water the boss is
  unbeatable"). On Eden, if she followed a glared main tank into the water she reset to full; their fix
  was to make her drop her target after a glare. This shows a glare plus teleport-to-water design, but
  the details are Eden's. [EDEN t=2486, low-med]

Coverage: thin on the final boss.

---

## Dodens Gruva (Jamtland 62k/48k; removed from US servers with New Frontiers, 2004)

- **Aros the Spiritmaster**: 72 [ZAM-7330, CAPNBRY]. A 2019 recollection [L, low] calls him "insta
  death", the kind of boss you "sprint through the room" past. No mechanics found.
- **Jarl Ormarr**: 55-64 in DB, 65-72 on radar (conflict). He has a voice line ("You call that a hit?
  I'll show you a hit"), which hints at a scripted taunt or strike. [ZAM-6440]
- Patches: 1.74 (2005-03) made common mobs drop class-specific unique objects. Snow vendos no longer
  drop low-level weapons for Albion players (2005). [HERALD]

Coverage: thin.

---

## Gjalpinulva (Malmohus; Midgard realm dragon; DB 80, radar 80; 2002 lore and French sources say 75)

### Mechanics

**FH-GJALP** (2003-11-07, translated from Sanys Sil, Fedaykin, Mid/Dartmoor) [P, high]:
- **Glare (targeted AoE DD).** She warns "glares at" one player, then hits that player and everyone
  near them. The glared player must sprint away from others. If stunned, Purge, then sprint.
- **Health-triggered PBAoE breath** at about 95-90%, 75-70%, 50%, 30% and 10-5%, each preceded by a
  chat warning. Players get about **3-8 s to run** as far from her as possible. "These PBAoE attacks
  wipe most raids."
  - VN-DRAGQ gives 90/75/40/20% for the dragons generally. That is a different list.
- **Retrievers.** At about **40%** she releases "retrievers", dog-like mobs that run at about **speed 6**
  toward the lair exits. Any that escape come back in about **2 min with 12-20 very purple mobs**.
  So from about 45%, 2 players per group guard each exit, and insta-CC healers mez or stun the dogs
  for the group to kill. Skalds are good for this because of their speed.
  - A follow-up post in the same thread says the dogs appear **at random**, as early as 90%, and
    sometimes clip through walls, so the best guard spot is slightly outside each exit.
  - 2006 [ZAM-3223]: one escaped dog returns with about 6 red or purple drakulvs.
  - [S] Uthgard calls them "scaled messengers" that return as about level-60 mobs.
- **AoE stun** from about 20%, up to 30 s unless resisted. The main tank must have Purge or
  Determination; healers stay out of PBAoE range.
- **Melee is slash only.** Breath is cold; required resists were 26%+ slash and cold.
- **Leaving the lair.** "Once the raid has entered the lair no one can leave the lair."
  - 2002-12-20 [ZAM-3223]: if anyone outside the lair (the "hort") attacks her, she AoEs.
  - [S] Uthgard: "do not go AFK within LoS of the dragon".
- **Patrol.** About once a day she patrols the zone after a zone-wide broadcast, dive-bombing
  clearings, camps and paths, then returns. She can be damaged but not fought fully while patrolling.
  [HERALD-DRAGON, P]
- **Respawn** (dragons generally): a 30-minute check with a small chance each time. One Merlin raid
  saw three pops in one evening. [VN-DRAGQ, P]
- A myth was debunked in 2003: "don't use procs or spells, ungroup everyone" is false. [FH-GJALP]
  - VN-DRAGQ adds that spells don't trigger the AoE unless cast away from the person protecting the
    caster.

### Tactic (FH-GJALP, 24-25 people, 3 groups)

- **Main Aggro Holder is a Savage.** He constantly taunts (taunt shout every 30 s), with attack-speed
  and melee-resist buffs and evade up. A shield tank guards him, runemasters bladeturn him, and the
  Bonedancer gives everyone absorb buffs every 10 minutes.
- **On arrival only the MAH engages.** He must **not be healed** at first and uses Ignore Pain if low.
  The other 2 groups clear the lair adds.
- After the adds are down, their tanks join using **detaunt styles only**:
  - Axe: Hoarfrost
  - Hammer: Placate
  - Sword: Temper
  - Hand to Hand: Taunting Call
- Healers then use **only the smallest baseline single heal, only within their own group**.
- Shamans rez tanks and buff end regen first; healers rez casters and buff PoM first.
- Every tank keeps a macro to call "DRAGON NUKE INCOMING".
- This was "apparently pulled off with 17 people too".

### Other period numbers

- First reported US kill: Gawaine Mids killed her **in 30 s in Feb 2002** because she was **mistakenly
  using a lower monster's resist table**. Mythic fixed this on 2002-02-27. [ZAM-STORY762, P, high]
- About 200 attackers were eaten in 3 AoE blasts, after which dogs killed the healers outside the
  circle (2002-06) [ZAM zone 60 comments]. 74 people lasted about 3 minutes (2002-10) [ZAM-3223].
- Kay, 2003-04, about 100 people [ZAM-3223]: two kills in one raid. Stay **inside the ruins**, kill the
  small adds, **don't nuke her**, and keep a warrior constantly taunting.
- Tristan, 2003-10 [ZAM-3223]: killed twice a day for a week with 27 people. "If you bring over a
  hundred people the dragon goes berzerk".
- 2006 [ZAM-3223]: 21-25 people, set up as:
  - two tank groups (aug healer, mend healer, aug shaman, 2 thane/valkyrie, 2 warriors);
  - a third group of PAC healer, aug shaman, shield tank and 5 casters to **guard the exits**;
  - one solo rezzer.
  At about 30% the group dropped determination fields because "she will stun for 30 secs".
- 2008 [FH-GJALP08, L]: typical kills took 3-4 per 3-4 h raid. Base comp was 3 healers, 2 shamans,
  1 warrior, 1 RM and 3 hunters.

### Unverified 2002 rumours

Two 2002 allakhazam comments claim that **killing the Glacier Giant (or "Yar") first removes or reduces
her fire AoE**. Yar is a lvl 73 mob in Malmohus on radar. [ZAM-872 2002-10-08; ZAM zone 60
2002-07-21; ZAM-3223 2002-12-20] No official confirmation was found. **Low confidence**; it may be
folklore.

### Later patches [L]

- 1.100 (2009-12-15): harder, spawns more often, and her corpse decays in 30 min so Gerrik's Sword can
  force a spawn.
- 1.101 (2010-02-24): damage reduced. The main breath gives immunity to further resist debuffs, the
  primary specials' reuse went from 2 to 5 min, and "inattentive player" mechanics were added.
  [ZAM-3223]

Coverage: good.

---

## Open-world Midgard

### Glacier Giant (Jamtland / Odin's Gate, loc 33322, 49113; 73-76; crush; body type EI/Cl; attack speed 5.0)

- **Not aggressive by default** ("only Hostile").
- Hits 850-1300+ through chain.
- Was "throwing" random aggro players (not the main tank) to random places in Jamtland or Odin's Gate
  by 2002-10.
- **Kills:**
  - First kills, Dec 2001: about 250 people from all three realms took an hour; only level 44+
    characters could hit it.
  - Shining Force (Lancelot Alb) killed it on 2002-02-10 with about 100.
  - 11 people in 2002-08 ("key is good tanking and BT").
  - 16 people by 2002-09.
  - 1-2 full groups of level 50 by 2002-10.

[ZAM-872, 2001-12 to 2002-10, P, high] SALARIS rates the Green Knight "the difficulty equivalent of the
Glacier Giant and Evern" [P].

### Wretch of Winter ("Wreths of Winter"; Raumarik; 70-75 DB, 70-80 radar)

- Several models: giant spider (easiest), trow, scav/djupt, scorpion/arachite, jotun (hardest), ghost
  hag, crab, lich.
- **No adds.** Pulsing bladeturn is "NECESSARY".
- Raumarik epics start **neutral**; faction drops with each kill, and after about 6-8 kills they turn
  aggressive.
- Arachite wiped a 10-person group twice and was killed on the third try with PBT, Enhanced Celerity
  and GTAoE. A Jotun hit a warrior for about 800.

[ZAM-2812, 2002-04 to 2003-07, P]

### Raumarik Revenant (70; DB lists both "Revenant" and a radar misspelling "Revenent" 71)

Linked to the quest "The Legend of Fenrir's Folly". **No mechanics found.** [ZAM-3804, CAPNBRY]

### Mortufoghus (Modernagrav; named mammoth; DB 70-75, radar 80 as "Mortalufagus")

- Roams.
- **Throws** people far, **stuns** whoever has aggro, small AoE nuke.
- Estimated about 12,000 HP in 2003. Three full groups of mostly-50s couldn't get it under 50%
  (2003-10).
- Later kills: 11 people in about 45 min (2006); 1.5-3 groups per JOL (2003-04).
- French rumour, unverified: level varies with location and time of day.
- Patch 1.66H (late 2003): more magical drops.

[ZAM-6681; JOL 288849; HERALD 95be89db/e05897386, P, med]

---

# HIBERNIA

## Galladoria (Shrouded Isles epic dungeon, World's End 50k/14k; bind at Necht)

### Zone-wide

- **Nameds call every mob in the surrounding rooms** ("up to 30 at once"), so every room near a named
  must be cleared first. This applies to the Geomancer, Mechanism, Broodmother, Lairmaster, Sporite
  Cluster and Aroon. [THREDH-GALLA, high; VN-GALLA agrees]
- **Respawn.**
  - Dec 2002-Jan 2003: the dungeon **did not respawn** until patch or reboot. "The only way… is to
    clear it entirely (including Olcasgean)". A "Galladoria Respawn Mob" is listed in Prima.
    [VN-GALLA p2-p5]
  - May 2003: Thredh gives an 8-12 h respawn. [THREDH-GALLA]
  - The DB lists "Galladoria Dungeon Spawner" and "Primal Encounter" controller mobs. [ZAM zone 75]
  - These accounts conflict, probably because it changed in 1.59-1.60.
- **Water.** You **can't rez or loot in water**; anything that dies in the pool loses its loot. This
  matters for the Conservator, Easmarach and the final room. [VN-GALLA, many posts]
- **Difficulty patches.**
  - About 2002-12-12 a patch made mobs more likely to BAF, made trash come in threes, and raised the
    Geomancer and Mechanism's difficulty. One raid of 55 couldn't get the Geomancer past half where
    45 had "stomped him" before; the Geomancer's DD and DoT and the Mechanism's GTAoE (300+) were
    raised. [VN-GALLA p2/p3]
  - 2003-01-14: more coin and drops from trash. [VN-GALLA p5]
- **Raid size and length.** 60 people take 3-5 h. Kill order: Geomancer → Mechanism → Conservator →
  Broodmother → Xaga → Lairmaster → Hurionthex → Sporite Cluster → Easmarach, then Aroon last or never.
  [VN-GALLA p5, Vogar 2003-01-17]
- **"Every mob has a giant version as boss":** Geomancer (aliens), Conservator (jellyfish/shrouder),
  Xaga (monkey-men/bugbear), Aroon (evil trees), Broodmother (spindlers), Sporite Cluster (spores),
  Hurionthex (stone people, shapeshifter). [VN-GALLA p1]

### Levels (DB)

| Boss | Level |
|---|---|
| Olcasgean | 87 |
| Watery Vortex | 87 |
| Corrupt Primals (Air, Earth, Fire, Water) | 83 |
| Earthmenders | 73 (FH-GALLAFE / THREDH) |
| Aroon, Easmarach, Hurionthex, Broodmother, Lairmaster | 81 |
| Xaga | 67-81 |
| Giant Sporite Cluster, Organic-Energy Mechanism | 79 |
| Conservator, Olcasar Geomancer | 77 |
| Tine | 67-70 |
| Beatha | 65 |
| Aroon's Scaith Drudges | 75 |
| Fire Trail | 83-99 |

### Olcasar Geomancer (77)

- "Killer DoT". Send in high-HP tanks and Purge first; tanks take melee aggro after the DoT, then the
  rest charge. "Very weak to GTAoE". [VN-GALLA p1, 2002-12-11]
- **He spawns minion groups** ("Geomancer Minion", 65). Split tanks half on him, half on the adds.
  - With surrounding mobs cleared you get only one pet; keep a guarded tank on it.
  - Melee stand **behind him**: he parries a lot.
  - Druids put **heat resist** up first.
  - 4+ groups.
  [THREDH-GALLA; VN-GALLA p3]
- One Guinevere raid had him "at 0%" for a long time while minions kept coming. Kill the pets with one
  group and the rest on him. [VN-GALLA p3]
- Clear the mobs under the Geomancer or they aggro through the floor. [THREDH-GALLA]

### Organic-Energy Mechanism (79, "magical"; killing it opens the wooden barrier)

- **Original version (Dec 2002):**
  - Melee on it sets off a "nasty DD". **Ranged and GTAoE only; never target it while clearing.**
  - About 15 orange "bottom feeder" adds spawn and walk back into the centre and vanish after a while.
  - One working set-up: 4 rangers and 3 mages shooting it, 4 PBAoE enchanters bubbled in a tight circle
    killing anything that came close.
  - Pets on defensive provoke heavier nukes; put pets on passive.
  - Clear the "square-pathed hallways" first, or coming straight from the Geomancer it **summons every
    mob in those hallways**.
  [VN-GALLA p1-p3]
- Thredh records **two contradictory versions** [THREDH-GALLA]:
  1. surround it with melee on all sides, mages at the door; it can't move, so dying melee run out;
  2. range only, because entering the central area is instant death.
- Later [L, 2012]: randomly spawns orange spindlers with a big STR/CON debuff, and must have visual
  effects on to be seen at all. [ZAM-4676]

### Conservator (77, named shrouder in the waterfall pool; social "Call")

- Easy, 2 groups. **Stay on land** and bow-pull; don't /stick; don't melee in the water. His lightning
  becomes a GTAoE if meleed in water. Wardens put **energy resist** up first. Don't kill him mid-pool or
  the loot sinks. [VN-GALLA p1; THREDH-GALLA]
- Later: tethered to the pond but casts on targets outside it; **calls nearby Cave Drifters** and heals
  to full if you disengage. [ZAM-4674, 2005-2012, L]

### Spindler Broodmother (81, octonid)

- Throws people for 90% fall damage, especially **anyone who turns their back on her**, onto far walls.
- Fire AE. Drops red-con babies throughout: melee switch to the babies instantly, then back.
- On death about 20 yellow "underdeveloped spindlers" swarm out; AoE them.
- Some 4-group kills were "long". A 39-person raid lost to the babies while she regenerated to full.
- Newborns cast a 1-min mez [2012, L].

[VN-GALLA p1/p3; THREDH-GALLA; ZAM-4677]

### Xaga (67-81, bugbear) with Tine and Beatha (wisps)

- **Bow-pull Beatha (the blue wisp) and kill her, then Tine, then Xaga.** Kill Tine before Xaga or
  Tine despawns with her sword drop.
- Ranged attacks on the faeries are the way. About 40 people finished in under 90 s; 30 managed it too.
- "Runs halfway through the fight to bring a friend".

[VN-GALLA p1/p3/p5; THREDH-GALLA]

### Uaimh Lairmaster (81)

- **Speed**: kill within about 30 s. At about **5% he flees and comes back at about 75%**, sometimes
  hiding in the east room where the Conservator spawns. Clear the whole area first so he can't gather
  help as he runs.
- A bug sometimes stacked about 20 trash mobs on him. A raid of 24 lost to "all the friends he
  brought"; 70-80 "devoured him".

[THREDH-GALLA; VN-GALLA p1/p5]

### Hurionthex (81, shapeshifting Sylvanshade)

Easy. Pull all mobs first (warriors first), then everyone on him. He morphs into other dungeon models,
apparently cosmetically. [VN-GALLA p2; THREDH-GALLA]

### Giant Sporite Cluster (79) and Virulent/Blue Cattail Sporites

- About 7-9 mobs **share one HP pool**; any hit hurts the whole cluster.
- It flies around hitting up to 7 people at once and picks off fleeing casters.
- It roams up and down the ramp at waterfall 2. It is safer to come from the lower tunnel and wait for
  it to settle on the beach.
- **Tactic:** the whole raid stands in a tight circle (about 300-600 radius, "smaller than the DL
  pad"), casters and healers inside, tanks outside, no /stick. Everyone assists the puller on one
  sporite while mages PBAoE; each PBAoE does "8-10x" damage through the shared pool.

[VN-GALLA p2/p5; THREDH-GALLA; ZAM-4684, high]

### Easmarach (81, named manta in the lake under waterfall 2)

- Kill the Lurkers and the Sporite Cluster first. Line tanks up on the beach or "sliver" of land; a
  swimmer bow-pulls.
- **From 50% (VN) or about 10% (Thredh) it "panics" and flies circles round the pool**: 15 s flying,
  15 s settled, repeating.
- Stand at the water's edge to hit it as it passes, and stop ranged attacks at the end so it dies on
  shore, not mid-lake where the loot is lost.
- Resists nukes heavily. Swimming past it to the final room is blocked: "The strong current of the
  waterfall pulls you back in" teleports you under it.

[VN-GALLA p2/p5; THREDH-GALLA, high; conflict on the % threshold]

### Aroon the Uriamhai (81, giant tree)

- **Mechanic:** on pull he summons **9 Scaith Drudge guards** (purple, 75). They are "virtually
  untouchable" ("quickly evades") except the one whose turn it is, and **they go for healers first**.
- Aroon is **immune to everything at first and loses one immunity per guard killed**. A chat message
  names the next guard, which "begins to glow"; after the kill you get a "susceptible to…" message.
- **Guard order** [THREDH-GALLA, high]:
  1. Corp
  2. Spiorad
  3. Ropadh
  4. Damhna
  5. Fuinneamg
  6. Bru
  7. Fuar
  8. Teas
  9. Scor (all "Scaith Drudge")
- **Pull tactic:**
  - A Warden relogs ungrouped (Long Wind I, end regen, sprint) and arrow-pulls Aroon, then runs **west
    into the ring corridor** while camera-targeting the next guard.
  - A second puller (a caster) /assists him and stuns that guard back to the raid.
  - Aroon and the rest reset to spawn. Repeat until 2-3 guards remain, then charge.
  - **Don't pull all guards without Aroon**: if he is later aggroed alone he re-summons them.
- 2002 notes:
  - Guards keep aggro and **don't despawn after a wipe**; relogging rezzers get killed.
  - Once guards return to position and despawn, re-pulling Aroon sometimes **didn't re-summon them**;
    5 groups then killed him in about 25 min with damage add and guard/intercept on one tank.
  - After 1.58-1.60 Mythic "adjusted the encounter to 'work as intended'". [FH-GALLAFE, 2003-04]
  [VN-GALLA p1/p2/p3]
- Some raids skipped him or left him last. [VN-GALLA p5]

### Final encounter: Corrupt Primals (83), Earthmenders (73), Watery Vortex (87), Fire Trail, Olcasgean (87)

**Room layout**
- Swim through the tunnel left of waterfall 2. A hallway of aggro mobs leads to a second waterfall,
  then a **narrow bridge or ledge you can't see until through the waterfall**, onto a central island
  or plateau.
- Two giant "trees" (Olcasgean's roots, waist-up in mist) stand there. Olcasgean "is" the writhing
  roots.
- **Entry rules:**
  - Once one person crosses, nearby raiders are pulled in (reported, disputed).
  - Look straight at the waterfall when entering or you are teleported into the water.
  - "Buffeting winds" at the landing knock idle people into the water; enter with speed and autorun.
  - Once inside you **can't go back up the ramp** until it's over.
  [VN-GALLA p2-p5; VN-OLC]

**Mobs**
- **Earthmenders** (Magical, Guardian, Shadowy, Natural; 73):
  - Don't attack.
  - **Heal to full instantly if hit by the wrong class**.
  - **Heal each other and themselves** even if no one errs. The heal is a cast that can be interrupted,
    not instant.
  - Reported heal chain: Magician → Guardian → Naturalist → Stalker → Magician. [FH-GALLAFE dieien]
  - Who may hit which mender (Hibernia classes):

    | Mender | Classes that may hit it |
    |---|---|
    | Guardian | Hero, Champion, Blademaster; Valewalkers count as Guardians |
    | Natural | Druid, Bard, Warden |
    | Magical | Eldritch, Enchanter, Mentalist; Animists count as Magicians |
    | Shadowy | Nightshade, Ranger |

  - Exceptions [THREDH-GALLA]: **anyone can hit any mender with a bow**; Animists and Valewalkers can
    lifetap any mender; mages can stun and debuff any mender.
  - Testing showed styles, damage add, procs and healing are all fine if they come from the right
    class. [VN-GALLA p5]
- **Corrupt Primal of Water.** Easy to zerg. It may hide under the bridge (rotate the camera and click
  it). One EU tactic: a sacrifice aggroes it and it rushes into the tunnels where it dies.
  A 2003-06 comment calls it "very immune to magic". [VN-GALLA p5; THREDH-GALLA; FH-GALLAFE; ZAM-5104]
- **Corrupt Primal of Earth.** **Can't be killed until all Earthmenders are dead.** It defends the
  menders. If someone with its aggro goes up the ramp near the waterfall (or stays on the bridge,
  ungrouped), it stops defending them. Gets easier to damage the more it is damaged. [VN-GALLA p5;
  FH-GALLAFE; THREDH-GALLA]
- **Corrupt Primal of Fire.**
  - Circles the plateau and sometimes cuts straight across, with a **constant PBAoE**.
  - Leaves **"fire trail"** pets (83-99) that explode when stepped on.
  - Early US notes say it "cycles every 9 min" for about a minute. The EU (1.60) killers report it
    **roaming non-stop**. [VN-GALLA p5; VN-OLC]
  - Two **safe spots** sit left and right of the entrance, a little behind two healers. [VN-OLC]
  - **Only GTAoE (void Eldritches) and LW "Annihilation" damage it** (still true after 1.62).
    [THREDH-GALLA]
  - It heals itself at about 20% and 10%. [VN-OLC]
  - Pets left at the GTAoE spot keep it in combat so it can't heal; bard lullaby range helps.
    **Nightshade "Distraction" stops it briefly** on each pass. [THREDH-GALLA]
- **Corrupt Primal of Air.**
  - Hard to see. **Chain AE mez** ("Tranquilize Area", body), **stun** ("Suspend Charge", body),
    **attack-speed debuff** ("Major Friction"), single-target nuke of about 300.
  - Nukes didn't land in early tries.
  - Weak to void GTAoE: Eldritches put their ground targets on the island end opposite the bridge,
    animist pets keep it in combat, Nightshade Distraction stalls it.
  [VN-GALLA p5 (.phenix., 2003-01-16); THREDH-GALLA]
- **Watery Vortex** (87): knocks people off the platform into the water, where they can't be rezzed.
  [VN-GALLA p2]
- **Some mob chain-AE-mezzes the raid** (Air) and AoE-nukes "like the dragon". [VN-GALLA p3]

**Proven kill orders** (they differ)

1. **Stonehenge EU, 2003-05-07, first live kill** [VN-OLC, 1.60, high]:
   1. About 150-158 people, 15-20 void Eldritches.
   2. A Nightshade spawns the primals and watches Fire.
   3. When Fire passes the entrance, one group enters and drags Water into the right safe spot (about
      30 s window).
   4. At the next pass, 6 groups enter the right safe spot and hold Earth.
   5. The rest go into the left safe spot and kill **Fire** with GTAoE on a shared ground target
      (about 15-60 min).
   6. Then the Earthmenders with the right classes, then Earth and Water, then Air (about 15 min of
      shooting).
   7. **Olcasgean then goes aggressive and is easy to zerg.**

   It took 1.5 h in the room and 2-3 h counting setup. One earlier try with 350-400 people failed to
   kill even one mender.
2. **Thredh (Guinevere), 2003-05** [THREDH-GALLA]:
   Water → Fire (Annihilation stuns plus timed GTAoE from the bridge, pets parked at the ground target)
   → Air (while others do the menders) → Earthmenders: **Magical first**, using energy debuffs, PBAoE,
   animist and valewalker lifetaps and bows, the others "tanking" their own mender to keep it in
   combat; then Guardian → Shadowy → Natural → Earth → then about **30 s until Olcasgean's roots
   attack**. Everyone sticks to and /assists the raid leader on one root.
3. **Earlier US plan, 2003-01** [VN-GALLA p5]: Water first, then one mender at a time (Magical,
   Guardian, Shadowy, Natural), then Earth, Water, Fire, Air, then Olcasgean, who is "melee only…
   not too much trouble". Proposed sizes were 100-200 people.

**Patches**
- About 1.58: US raids killed Water, Earth and the menders. Fire and Air were undamageable until
  Pendragon devs "dropped the levels on fire and air". [FH-GALLAFE 2003-04-23]
- 1.61: chance of respec stones. [VN-OLC]
- **1.62: Fire primal damage greatly lowered; Fire and Air easier to hit, but still only by GTAoE or
  Annihilation.** [THREDH-GALLA]
- 1.65D (2003-09): Fire primal drop count fixed. [HERALD 7317b4deca9bda87]
- 1.98 (2009-06-05): "Olcasgean will no longer call the rest of his dungeon to his aid". [ZAM-5136, L]

Coverage: good.

---

## Tur Suil: Balor (81) and Queen Qunilaria (75)

**Route and keys** [THREDH-TS, 2003-05-27, high]:
- Bind at Aalid Feie. 24-48 people, 2-4 h.
- Tur Suil stairs are death traps: stop 10 steps from each landing, since mobs aggro through walls.
- Kierac the Destroyer (69): bubble and high parry, fight from behind. Drops an **outer-door key to
  every member of the killing group**, and **killing him respawns the Master of Pain**.
- Sargan the Conqueror (71) and Norsob the Annihilator (72): same pattern; clear adds and fight from
  behind.
- **Master of Pain (74):** melee only, high HP and damage. Drops the **inner-door key**, and **killing
  him spawns Balor** in his room.
- Keys are permanent; you open each door by using the key on it.
- **Don't log out inside Balor's room without keys**: after Balor dies the room empties and you can
  only leave via /appeal.

**Balor**
- Sits on his throne. **Two purple companions in the south of the room can aggro through the door**;
  kill them first.
- 2003-08 [ZAM-5070, P]: an **eye beam ("laser") that goes through walls, can't be interrupted, and
  kills anyone in ≤2 hits**. "You can't focus him… just rez when people die." 50+ people needed: if
  he isn't visibly dying within about 10 min "you're probably gonna lose".
- Thredh's tactics [THREDH-TS]:
  1. straight zerg in which **healers who get aggro run circles** until he switches target;
  2. masses of Animist turrets (more attackers lower his AF and to-hit).
- Later [L, 2007-2012]: 7 people in 10-15 min. The laser "does not much dmg, low area". He **throws
  the tank onto the walls or toward the door guards** and switches aggro. Hits 450-1k. This
  **contradicts the 2003 description**: either the beam was nerfed, or players outlevelled it via
  ToA/ML.
- 2003-05: "you need a Legion-sized group".

**Queen Qunilaria** (75, burrowing horror past Abomos; keep right at every branch) [THREDH-TS;
ZAM-5068, 2003-03 to 06]:
- Static. Summons tiny horrors (orange to purple, link) every 20-40 s. On death about **20 yellow
  copies** scatter: a Warden's Thornweed Field holds aggro while mages AoE.
- 2-3 groups, or 10 with AE stuns and a PBAoE enchanter.

**Patches:** Balor's Remains trophy set to power level 35 (c. 2004) [HERALD 9394fb91]. Balorian Maccus
and Ewyn no longer use invisible weapons (2005) [HERALD 582800b8].

Coverage: good.

---

## Marfach Caverns (Breifine 60k/40k; removed from US in New Frontiers)

- **Black Lady** (65): **no mechanics found** [ZAM-3969].
- **Blue Lady** (65):
  - **Summons swords and axes "in ridiculous quantity"** (about 5, purple to 48). Mez or root the
    summoned weapons and kill her. Killing the weapons first gets you killed because she keeps
    resummoning.
  - Pull her guards first, but **the guard nearest her always brings her**.
  - If the pulling group dies, the weapons **roam the whole dungeon** killing anyone.
  - **1.69 (2004-04-14): summoned weapons no longer leave her room.**
  [ZAM-3750 and zone 70 comments, 2002-09 to 2003-09; HERALD 6dd606e3e51674cd, high]
- **Red Lady** (65): a green or grey add; "nearly resistant against any magic dmg". Killed by 3-4
  people. [ZAM-3956, 2003]
- Patches: withered oaks typed as plants for artifacts (2004). Common mobs drop class unique objects
  (2005). [HERALD]

Coverage: thin for the Black Lady.

---

## Cuuldurach the Glimmer King (Sheeroe Hills; Hibernia realm dragon; 80 in DB, "around 90" on the fandom wiki; slash and thrust)

### Mechanics and period tactic

**FH-CUUL** (Edlina, Prydwen EU, 2003-05-03, 120 people) [P, high]:
- **No healing allowed**; rezzers only rez, rest, rez. Rezzing draws aggro and often an AoE.
- **Never send more than half the army at him.** Only about 30 attackers at a time: about 60% of the
  tanks plus some mages. "Not meant to be zerged but taken down slowly."
- **All four entrances** are held by **2-3 full groups each**, mostly rezzers and mages for add control
  and rezzing; 2 of them on the two most exposed entrances (NW and NE of the mound).
- He **moves from entrance to entrance**, AoEing now and then and killing **15-25 per AoE**. A wiped
  entrance rests while he moves on.
- **"Couriers move at something like mach 3-4 and must be stopped."** If they escape, adds come; about
  15 came at 40%. Kill adds first, as on Legion.
- **Stay off his mound**: tanks who followed him onto it were all AoEd, and it is a hard place to rez.
- Attackers stand behind him; anyone not tanking stays far away.
- **If you draw aggro outside the lair he will always AoE you**, so spread out before rezzing from
  outside.
- At 15%, tank-group archers stepped off to avoid the **AoE stun**; only Champions and Nightshades
  meleed.
- Set-up: 6 add-control groups (2 tanks, warden, bard, mages, maybe a druid), 5 tank groups on the
  dragon, druids ungrouped as roaming rezzers. Groups are rebuilt by class at the lair.
- Pull with a ranger, send 1 tank group to engage, the rest charge **from all four sides**.

**Other**
- FH-DR2002 (2002-09): attacking from range triggers AE nukes; "he will stomp you flat in 18 seconds,
  the time for 3 AoE blasts". The first US successes came in v1.52, "the patch in which they nerfed the
  dragon". One spot can be scouted by 3-4 Glimmer Knight spawns at once and pulls every glimmer in the
  zone. [P]
- [S] EDEN t=1173: messengers spawn **at 75%**, run away to call adds, and must be CC'd and killed.
  This is Eden's implementation.
- Fandom wiki [S/low]: his gas turns players into Glimmer Knights; he makes stragglers into
  "Dragonsworn" minions.

### Patches

- 1.61b: "more vulnerable to heat" [ZAM-3606].
- Remains trophy power level 35 (c. 2004).
- 1.100 and 1.101 as for Gjalpinulva [L].

Other kills: Bedevere 2004-03, "10 min with massive shrooms". Logres EU 2003-05, dropped nothing.
[ZAM-3606]

Coverage: good.

---

## Fomor: Myrddraxis (80, five-headed hydra) and Anurigunda (75)

### Layout [THREDH-FOMOR / Valmerwolf]

- Two concentric rings plus a broken inner ring that ends at the hydra. Each ring transition is a single
  choke guarded by a named; some transitions are climbs.
- Instant-repop points sit in or near the houses. **Traps (DoT, nukes) fire as you pass** before
  Anurigunda and around the hydra.
- The zone was crash-prone: several EU raids report the zone crashing on the kill and losing the loot.
  [FH-FOMOR 2005]

### Nameds on the way

- **Krackenschtein** (70): 6 purple guards. He **"ports people to him"** while you fight the guards, so
  zerg him first. Sometimes absent.
- **Noguribando** (70): straight zerg.
- **Anurigunda** (75):
  - **You must kill him or the hydra entrance teleports you back.**
  - He can only be attacked at his spot (about 700 range).
  - Fire GTAoE (about 250 radius) ticks every 2 s for about 60 s and **interrupts casting**.
  - At about 40% and 20-30% he yells for help (3, then 1 purple adds). A nearby guard respawns about
    every 5 min. [ZAM-6092, 2012, L]

### Myrddraxis

- **Lair stations** [THREDH-FOMOR, 2003-08-17, high]:
  1. the entry door, for mages;
  2. an alcove left of the entry, for Animists;
  3. the left bridge, for melee on heads 2 and 3;
  4. the right bridge, for melee on head 4 and head 1 (the body).
- **Order:**
  1. Everyone on **head 2** (the **lifetap head**) from station 3, melee at the bottom of the bridge,
     as far from the hydra as possible while still in range. Animist pets at the top of the bridge.
  2. Heads lifetap or heal each other and the body. **Head 5 usually dies by itself.**
  3. Then **head 4** from station 4 (Animists at station 1).
  4. Then **head 3** from station 3 (Animists at station 2).
  5. Then the body.
- **Other reports:**
  - **Constant mez, stun and DoT**; DoTs out-damage heals. [Valmerwolf, ZAM-4686 2012]
  - A group on each ramp attacking all heads at once **failed**.
  - **Casters can't land spells except PBAoE**, so they melee with staves.
  - Total HP is about "6 level 80s"; at least 50 people and about an hour.
  - 2003-03: 6 groups gave up because the head being fought kept healing itself.
  [ZAM-4686, 2003-03 to 08, P]
- **FH-MYRD** (2003-11) alternatives:
  - 5 Mana enchanters each pet-pull one head, kept alive by 12-18 healers; **siege weapons**
    (catapult, palintone) kill the heads one by one.
  - Or 6-10 tank groups with 2 shield heroes holding each head.
  - **Siege must stop at 10% on the last head**: "if the siege weapon makes the last hit, Myrddraxis
    will not drop anything".
- **No remains**: official, Grab Bag 2007-08-24 [GB-2007, L].

### Patches

- 1.65 (2003): drops more items [HERALD 8eed317c].
- 1.100: Lesser Heads reduced in level.
- 1.101: greatly reduced in level, more HP and defences, "inattentive player" mechanics [L].

Coverage: good.

---

## Open-world Hibernia

### Evern (Breifine 25k/63k; 70-75, "magical"; with wraith drakes 65 and wraith fairies)

- **Evern summons fairies that fly to one of the 23 stones around him. A fairy that lands heals
  Evern**, so kill every fairy before it lands.
- An 8-person kill on 2003-07-25: 3 rangers, Mana mentalist and Mana enchanter killing fairies, the
  enchanter's pet tanking under focus shield with power potions; 20 min.
- An earlier full group was "owned" (2003-05).
- **Wraith drakes:** **immune to arrows**, DD about 130, attack speed 3.8, speed 188. [ZAM-747]
- SALARIS puts the Green Knight at "the difficulty equivalent of the Glacier Giant and Evern".
- 1.70T (2004-05-26): "Evern, his wraith drakes, and his wraith fairies have returned to Breifine",
  so the encounter was missing for a while. [HERALD 7deddae33971110f]

[ZAM-3755, P, med-high]

### Juggernaut (Cursed Forest 45351/36343; 75 in DB, est. 68+; giant)

- Roams around "Flame" (64-79).
- Starts **neutral**; faction drops with kills (like Raumarik).
- A 2002 full group of 50s with bladeturn got it to about 25% in 10 min; reported as 3 people in 2003
  (disputed).
- Later [L, 2012]: "hits hard and has **instant mezz for 60 secs**".

[ZAM-3157, med-low]

### Curengkur (Allta Fearann; 70-72; animal)

- DoTs and DDs; takes little damage from magic; strong double hit.
- **Its nest/meteor pulses a PBAoE DD about every 2 s, about 700 range, plus a "radiation" DoT near
  the nest.** At about 15-20% the **nest turns red and it regenerates**.
- Pulling it **out of the nest stops the regen**. At 40% or less it yells for help (red add).
  [ZAM-6629, 2005-2012]
- Patches:
  - 2003-10-16: no longer drops loot where it can't be picked up.
  - 1.65B: returns to the right spot after combat.
  - 1.66 (2003-10-28): "now correctly call[s] for help".
  [HERALD 5cc5c544, 44b8e18c, 057bbfcf]

Confidence: med.

### Mollunero (World's End; 65-70)

Fights with **7-8 yellow-to-orange baby octonids**, on the river shore near the Krrzck bug town, shared
with Zytka. [ZAM-8143] Thin.

### Marfoirwort (Cothrom Gorge, behind the waterfall mountain, 25000/12500; plant; DB 65-68, or 75-80 per a 2012 comment)

- Part of the **Writhing Death** quest, with Cronwort, Fuilwort and Tachtwort; Cronwort has 2-6 purple
  Breanworts.
- Hits 400-950 and uses **lifeleech** often ("impossible with tanks because of the lifeleech", 2012, L).
- Patches:
  - 2003-09-24: "Marfoirwort can no longer be dragged away from his area" (leash).
  - 1.66H (2004-12): quest credit goes to the whole battlegroup.
  - Marfoirwort's Rapier fixed (2003).
  [HERALD fc67afb4, e0589738, 7317b4de]

Confidence: low-med.

---

# Bosses where I found nothing or almost nothing

**Nothing**
- Raumarik Revenant: level and quest only.
- Elder Icelord Hjalmar: drops only.
- Black Lady (Marfach): level only.
- Shade of Adimendus and Adimedus (Trollheim): level only.
- Hati and Skoll (Iarnvidiur): position only.

**Thin**
- Aros the Spiritmaster: level plus one 2019 "insta death" recollection.
- Jarl Ormarr: level only.
- Iarnvidiur herself: layout plus an [S] glare and teleport-to-water note.
- Mollunero: add count only.
- Raumarik "Wreths" by individual model: one 2003 group report.
- Griffon Icewing: no boss mechanics.
- Hakr's Barrier: role unknown.

**Not found or not attempted**
- No Prima strategy guide text was found online.
- I could not reach daocpedia (robots refusal) or live Wayback captures of camelotvault guides, Dabokel's
  VN TG thread (topic 46131774) or javamansworld.com (domain dead).
- These are the next sources to pull if the corpus builder can fetch Wayback.

# Contradictions to resolve before implementing

1. **Fornfrusenen level:** 75 in DB, 82 in Carne, 88 on radar.
2. **Easmarach panic threshold:** 50% (VN, Dec 2002) vs about 10% (Thredh, May 2003). It may have been
   tuned between them.
3. **Fire Primal patrol:** every 9 min (US, Jan 2003) vs non-stop (EU 1.60, May 2003).
4. **Galladoria respawn:** none until a full clear or patch (Dec 2002-Jan 2003) vs 8-12 h (May 2003).
5. **Gjalpinulva breath thresholds:** 95/75/50/30/10% (FH-GJALP 2003) vs 90/75/40/20% (VN-DRAGQ, all
   dragons). Retriever timing is "about 40%" vs "random, as early as 90%".
6. **Balor's eye beam:** lethal through walls (2003) vs minor (2007+).
7. **Nokkvi:** "no tougher than Vagn" (Carne 2003) vs "a million HP, resists all magic" (2005).
8. **Organic-Energy Mechanism:** melee from all sides vs ranged only (Thredh lists both).
9. **Killing the Glacier Giant or Yar to weaken Gjalpinulva's AoE:** 2002 rumour, unconfirmed.
