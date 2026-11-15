# Raid bosses, pass 2: fetched sources (2026-11-14)

Companion to `wayback-corpus.md` (pass 1, local Wayback corpus) and `wayback-gaps.md` (the gap list this pass worked
from). Everything below is paraphrased in my own words; quotes are short phrases only. Each fact carries a source key
(table below), the date the text was written, and a confidence:

- **H:** official Mythic text (patch notes, Grab Bag, a Mythic staff quote), or two independent first-hand sources
  that agree.
- **M:** one first-hand player account from the period, or a period map.
- **L:** rumour, second-hand, contradicted, or written after 2005 (late comments are flagged "late").

## 0. What could and could not be fetched

**The Wayback Machine was not reachable from this session.** `web.archive.org` connections were reset at the session's
egress proxy (every attempt, including the CDX API), and WebFetch refuses any URL that did not come from a web-search
result, so no `web.archive.org/web/...` or CDX URL could be opened. Per the task rules I noted this and did not try to
route around it. The live sites `vnboards.ign.com` and `camelotvault.ign.com` answer 403, `classesofcamelot.com`,
`io.com/~caladin`, `grailbrotherhood.info` do not answer, `daoc.catacombs.com` and `cginc.net` answer 503, and
`camelotherald.com` (old `more/*.shtml` paths) is not reachable directly.

Consequence: priorities 1-6 (Dabokel's IGN guide, Galladoria Notes p4/6-20, Dragon Q&A p2-6, Camelot Vault epic raid
guides, Classes of Camelot guides, Herald "Death of a Dragon" and the Grab Bag sweep) were **not fetched**. I used
substitutes that are still live and hold period text:

| Key | Source | What it is | Fetched |
|---|---|---|---|
| FH-* | `forums.freddyshouse.com/threads/<slug>/` | EU community forum; its 2002-2004 threads survive on the live site with original post dates. 78 raid threads saved | live, 2026-11-14 |
| AZ-<zone> | `camelot.allakhazam.com/db/mob/<id>` (+ `/db/mobs/zone/<id>`) | Allakhazam bestiary: level, type, patch-note descriptions, player comments dated 2002-2014. 388 boss/elite pages over 23 zones saved | live, 2026-11-14 |
| VW-<map> | `www.valmerwolf.com/mappe/...` | Labelled dungeon maps linked from Allakhazam zone pages (Caer Sidi, Tuscaran, Galladoria, Tur Suil, Trollheim, Iarnvidiur, Krondon, Avalon City, Crystal Cave, NF Summoner's Hall). Undated; labels match 2003-2004 | live |
| AZMAP-* | `camelot.allakhazam.com/Maps/...` | Beeblebrox Trollheim map; Carne DelMuerto's Iarnvidiur map ("Updated October 2003") | live |
| GB-<id> | `www.gamebanshee.com/news/<id>-...` | GameBanshee news mirror of Herald patch notes and Grab Bag teasers, Jul 2002 - 2004 (sparse; gap Nov 2002 - Mar 2003). 78 relevant articles saved | live |
| PR | Internet Archive item "Dark Age of Camelot Revised and Expanded Prima Official eGuide" (PDF, made 17 Sep 2004) | Classic zones + DF only; no SI dungeons, no boss tactics | archive.org item (not Wayback) |

Raw text is under `/home/claude/raid-research/wayback-pass2/` (`freddyshouse/`, `allakhazam/`, `gamebanshee/`,
`other/`, `maps/map-labels.txt`); each file starts with its URL and the note that it is a live fetch, not a Wayback
capture. E-mail addresses were stripped. These are for internal research only.

Because the substitutes are live pages, "capture date" below means the **post date** of the text quoted (shown per
fact). Comments on Allakhazam pages can be edited later; Freddyshouse posts show their own date.

---

## 1. Cross-cutting mechanics (new in this pass)

- **What triggers dragon breath.** A Herald Grab Bag answer (quoted on FH by Pin, 8 Aug 2003) says casting, debuffing,
  healing, chants, proc weapons and buffing do **not** make a dragon breathe; breath and porting are basically random,
  with frequency driven mainly by how many people are there. FH-how-many-ppl-to-kill-the-dragon (H: Mythic text,
  second-hand copy). Pin adds that the AoE lands on whoever tops the aggro list, wherever they stand (M). This
  contradicts the 2002 player belief (FH-dragon-raid-strategy, FH-ok-guys-dragon-raid-time, AZ-malmohus Dec 2002) that
  any attack from outside melee range triggers the AoE; the 2003-2004 raid leaders kept "no ranged" rules anyway
  because it worked for them (L-M). **For bots:** model breath as a timer/random roll scaled by attacker count, aimed at
  the top of the hate list; keep "spread out, nobody clustered with the top-aggro target".
- **Summons drop no loot.** Sanya Thomas (Mythic), quoted in FH-farm-beliathan (17 Mar 2003): anything a boss can
  summon repeatedly is an extension of the boss and drops nothing; a one-time summon may drop loot (H).
- **Final bosses called their whole dungeon.** Allakhazam's patch-note descriptions: until patch 1.98 (May 2009)
  Apocalypse and Olcasgean "called the rest of the dungeon to his aid" when attacked (AZ-caer-sidi, AZ-galladoria; H).
  FH-working-on-the-final-encounter (7 Jun 2003, EU) shows it in practice: the whole Galladoria dungeon runs on one
  respawn timer; when it ran out mid-final-encounter, Olcasgean respawned and every mob still in the dungeon came to
  him (M). **For bots:** a final-boss pull should be preceded by a full clear and done inside the respawn window.
- **1.62 epic retune (live April 2003).** GB-69829 (H): "re-tuned several high-level encounters"; dragons in every
  realm easier; Galladoria final encounter easier; each original dragon has **two breath spells** and one had its
  damage "greatly lowered"; dragon zones got new AI for all named, smaller scout (BAF) ranges and slower spawn
  generators. Specific encounter fixes are listed under each boss below.
- **1.67a test (3 Dec 2003).** GB-70934 (H): dragons in each realm and the Galladoria final encounter made easier again.
- **Engine to-hit timer.** FH players (Aug 2003) believed the attacker-count to-hit bonus stays active for a few
  minutes after a character's last action on the mob, so one debuff every few minutes per necro/cabalist kept the bonus
  up (L, theory). Pass 1 has the attacker-count rule itself (H).
- **Quest credit goes to one group.** For Abomos (Tur Suil) and Scurceol Hyrde (Krondon), 2003 players saw only one
  group (the killing-blow or main-aggro group) get quest credit and had to kill the boss again per group
  (FH-tur-suil-quest-abomos Jul 2003, FH-krondon-a-nice-place-to-visit Feb 2003; M). One player says credit follows
  damage/aggro, not the last blow (L). Battlegroup credit came in 1.66h (pass 1, H).
- **Big-raid organisation that worked (EU 2002-2003).** Add-control groups (PBAE) posted at every entrance; resser
  groups standing apart and only running in to rez; fight groups ungrouped or paired to avoid chain aggro; a
  disbanded puller; chat-group per role (FH-dragon-raid-strategy, FH-how-to-kill-cuuldurach, FH-dragon-400-hibernia-0;
  M, consistent across three raid leaders = H).

---

## 2. Caer Sidi

**Map with kill order** (VW-caer-sidi; M). Entrance via a long corridor from the bottom right. Each keylord opens the
next door; the map labels (level / door it opens): Skeletal Sacristan 77/1, Spectral Provisioner 77/2, Lich Lord
Ilron 79/3, Warlord Dorinakka 81/4, Silencer 81/5, Bane of Hope 81/6, Lord Sanguis 81/7, Crypt Lord 77/8, Soul
Reckoner 81/9, The Host 79/10; door 10 leads to the horsemen and Apocalypse (87). Full transcription in
`wayback-pass2/maps/map-labels.txt`. Allakhazam disagrees on Crypt Lord (81) and lists Ilron as 70-79 (AZ-caer-sidi).
The map labels the fourth horseman "Furius"; every 2003 source says Funus (trust Funus).

**Levels** (AZ-caer-sidi; M, database): horsemen Fames, Bellum, Morbus, Funus 83; Archlich 83; Fate's Chosen 81;
Fate's Bearer 77; Harbringer [sic] of Fate 75; War Incarnate 75; Swarm of Morbus 75; trash 75-79.

| Boss | New facts | Source, date, conf. |
|---|---|---|
| Skeletal Sacristan | Never attacks; runs the corridors at very high speed and paths into walls. Line casters along his route, tanks stick to him. | AZ 13 Apr 2003 + 1 Dec 2004; H (agrees with S1) |
| Spectral Provisioner | Sprints and drops ~500 lb sacks into players' bags so they cannot move; clear his level first and corner him. | AZ 31 Jan 2006 (late); M (agrees with S1) |
| Lich Lord Ilron | On pull splits into about five; attack the solid one; the copies cast nukes, AE mezz and bolts. 1.62: no longer resets his encounter when he should not. | AZ 29 Jun 2003 (M); GB-69829 Apr 2003 (H) |
| Warlord Dorinakka | Hits a level-46 plate wearer for 800-1000, uses styles, parries heavily from the front, sticks in walls (do not /stick, just /face). Two paladins guarding each other with heal chant can hold him; 2 good groups can kill him with fast rezzes and guard. Wiped 54 players once. | AZ 29 Jun, 8 Nov, 23 Nov, 29 Dec 2003; M |
| Silencer | Ports a large part of the raid into the pit (with ~40 people this is the slow fight). | FH-caer-sidi-raid-sunday-20th-july, Pin 17 Jul 2003; H with S1 |
| Lich Lord Sanguis | Spawns twice; the second (skeletal) form is fully immune to slash and dies fast to crush; a Sanguineous Mage pops with it. Blood mages roam and add. | AZ 29 Dec 2003 (M); 2012 (late, L) |
| Bane of Hope | Teleports around the tunnels if not killed fast. | AZ 2012 (late, L); agrees with S1 |
| Soul Reckoner | Reckoned Souls all attack at once and must die first. | AZ 2012 (late, L); agrees with S1 |
| Morbus | Swarm of Morbus casts an instant AE disease; ~80% resists did not stop it; cure disease before healing. | AZ 29 Dec 2003; M |
| Funus | Only clerics can damage him (Allakhazam's own description; FH raid shouts "only clerics attack the last horseman", 31 Mar 2003). A Dec 2003 player says archery classes (scout, armsman, mercenary, infiltrator with bow/crossbow) can also damage him. Debuffs can be landed without healing him. | AZ desc + FH-caer-sidi-advice; H. Archery: M. Debuff: L |
| Horsemen encounter | EU, 15 Mar 2003: the encounter started, the first horse reached 80%, then everything turned invulnerable again, the horse healed and all horsemen walked back to their spots, after half the raid wandered into the room against orders. | FH-caer-sidi-take-4; M (bug or reset rule) |
| Apocalypse | Patch 1.59 made him "significantly harder": ~120 people needed versus ~70 before (EU, 14 Apr 2003). 1.62: the encounter no longer stalls and refuses to restart after a failed attempt. Until 1.98 (2009) he pulled the whole dungeon when attacked. | FH-caer-sidi-raid-take-something-or-other (M); GB-69829 (H); AZ desc (H) |
| Archlich (83) | Weak melee, rotating resist-type DDs (fire, cold, matter, spirit), root and mezz. | AZ 14 Feb 2014 (late, L) |
| Crusader's Antithesis (79 trash) | Turns into a sword and keeps hitting while untargetable; keep damage on it. | AZ 2012 (late, L) |

**Raid sizing** (Pin, FH-caer-sidi-raid-sunday-20th-july, 17 Jul 2003; M): ~40 people to move at a good pace;
40-60 is the reward sweet spot and reaches the last stages but can wipe at the Host or Bellum; 80-90 can do
Apocalypse with good groups; 90-100 makes him straightforward. Ilron was killed by 23 people (same thread).

**Unfetched leads named in FH posts:** Sidi guides at `oc.edu/pages/nick.stageberg/sidi/guide/` and
`silverdaggers.net/daoc/csg_index.html` (2003), both only on Wayback.

---

## 3. Tuscaran Glacier

**Levels** (AZ-tuscaren-glacier; M): King Tuscar, Queen Kula, Hurika, Torst 85; Jailor Vifil 82; Elder Icelords
Suttung, Hjalmar 81; Elder Councils Birghir, Guthlac 80; Icelords Agmundr, Hakr, Kvasir, Skuf, Steinvor 80; Councils
Nokkvi, Vagn 78, Hord, Ozur, Otrygg 77; Issorden 78; Fornfrusenen 75 (+ Fornfrusenen Shard 75); Hrimthursa Throne
Guard and Tormentor 77-79; Dominator 72-80; Hakr's Barrier and Dark Bubble are level-50 objects. The Valmerwolf map
gives Fornfrusenen 82, Issorden 75-80, Kvasir 75-85, Hurika 75-85 (VW-tuscaran; M).

**Raid route** used by Prydwen (EU) raids, shanks, 31 Jul 2003 (FH-tg-raids-again; M): Issorden, Fornfrusenen, clear
the spine room, Councils Hord, Ozur, Otrygg, then Nokkvi + Vagn together, Icelords Agmundr, Kvasir, Hakr ("if he's not
bugged again"), Steinvor + Skuf together, Elders Guthlac + Birghir together, Suttung, Hjalmar, Queen Kula, King Tuscar.
Griffons (Hurika, Torst) and Jailor Vifil skipped as long detours for poor loot.

**Numbers** (same thread; M): King attempted only with 9+ full groups; the Queen fell "rather easily" to ~60;
Hurika takes 2 groups pet-tanking for an hour or two, or a 60-person zerg 45+ minutes. Hurika was killed on Excalibur
(6 Jun 2003) by ~60 people after a runemaster bolt-pulled her and jumped down to fight her below (AZ; M).

**Respawn** (FH-tuscaren-glacier-raid-tuesday-15-07-03, 16-17 Jul 2003; FH-about-the-tuscaran-glacier, 2006): about 8
hours (some saw up to 12). Two independent clocks: skeletons/griffons (started in the tunnel toward Issorden) and
giants (started by killing the first giants past the skeleton corridor). Killing a council mob starts the giant clock;
raids died when a full giant repop landed on them ~10 minutes after killing two councils because someone had farmed
Issorden earlier. If a raid starts mid-dungeon without touching the first giants, the giant side does not respawn at
all (2006). M each; H combined with pass 1's S9.

**Per boss, new:**
- Council Nokkvi: very high hit points, resisted every spell, styles hit for ~1; kite Council Vagn, who comes with him
  (AZ 6 Aug 2005; M).
- Council Ozur: attacks four times per round ("quads") (FH 2006; L).
- Throne guards in the queen's antechamber hit ~950 on a chain wearer; Guthlac, Birghir and Suttung can be pulled
  with them (FH 2006; L).
- Icelords Skuf and Steinvor: 1.62 fixed several bugs in their joint encounter; "Steinvor's seers should now work
  better" and both icelords' magic now works (GB-69829, Apr 2003; H). So Steinvor fights with seer adds and both cast.
- Damage types: a 2004 raid leader asked for spirit or matter damage because the dungeon's mobs are neutral to it,
  and heat only on the King and Queen (FH-tuscaran-glacier-raid-november-13th, 6 Nov 2004; L-M).

**Not found:** Dabokel's guide itself (IGN topic 46131774) and what the King does once engaged.

---

## 4. Galladoria

**Map** (VW-galladoria; M): entrance top left; Lairmaster 81, Olcasar Geomancer 77, Organic-Energy Mechanism 79 (far
NE), Conservator 77 (pond, W), Aroon 81 and Hurionthex 81 (E cave), Easmarach 81 in the central lake with Giant Sporite
Cluster 79 on its shore, Broodmother 81 (far W), Xaga 81 / Tine 70 / Beatha 65 (SW), final round room (S): Olcasgean 87
and the four Corrupt Primals 83.

**Final encounter mechanics** (FH-final-encounter-in-galladoria, 22-24 Apr 2003; FH-working-on-the-final-encounter,
Jun 2003):
- Four Earthmenders (level 73), each damageable only by one class family: Guardian (tanks), Magical (casters), Natural
  (naturalists), Shadowy (stalkers) (censi; M, matches pass 1).
- The menders heal each other in a ring: magician mender heals the guardian mender, guardian heals naturalist,
  naturalist heals stalker, stalker heals magician. Kill one and the next in the ring loses its healer (dieien; M).
  The IGN-quoted US advice (copied into the same thread) is to have casters kill their mender first, keeping PBAE off
  the others because splash makes them heal (M; same as pass 1 S5).
- A mender heals itself back ("IP") when hit to ~95% even if only the right class attacks; damage must outpace heals
  (censi, solo test; M).
- The Earth Primal cannot be killed until all menders are dead; when all four primals are dead Olcasgean spawns, and
  "he" is the writhing roots (IGN quote; M). A June 2003 EU raid of 54 killed all menders and got the Earth Primal to
  50% before the dungeon reset (FH; M).
- The Fire Trail (Allakhazam: level 83-99) patrols the plateau about every 9 minutes and takes about a minute; do not
  fight on the plateau during its pass (dieien; M).
- Water Primal: a lone volunteer aggroes it at the waterfall and dies; it then rushes into the tunnels, where it is easy
  (giolla; M). Very resistant to magic: a mana enchanter did nothing to it (AZ 2 Jun 2003; M).
- Fire and Air primals: US players reported nobody could damage them until developers lowered their levels on a
  Pendragon test raid (Brennik, Apr 2003; L, second-hand). Primal of Air "Void GTAOE" (censi; L).
- Mythic adjusted Aroon to "work as intended", which then wiped 70-person raids (Brennik; L). 1.62: Aroon no longer
  aggroes through walls (GB-69829; H). 1.62 and 1.67a: final encounter made easier (H).
- An Oct 2003 EU raid asked each guild group for a void eldritch (GTAoE) and an animist (turrets), plus a nightshade
  who knows Distract, and planned ~4 hours for full clear + final encounter (FH-4th-final-encounter; M).

**Other bosses** (AZ-galladoria unless noted):
- Conservator: tethered to its pond; mostly melee, a light DoT that breaks focus shields, occasional lightning, and an
  AE DD with a broadcast warning; calls the local Cave Drifters (75) and heals to full while you deal with them; can
  cast on targets outside the pond (AZ 2005-2012; M-L).
- Organic-Energy Mechanism: killing it opens a wooden barrier deeper into the dungeon; only visible with spell effects
  set to All; randomly spawns spindler adds with a large str/con debuff (AZ 2009-2012; L).
- Spindler Broodmother: tethered; hatchlings spawn during combat and cast a 1-minute mezz; on death ~20 weak
  "underdeveloped" spindlers pour out (AZ 2012, late; L; 2004 note "takes a couple of full groups").
- Giant Sporite Cluster: killing one piece damages the whole cluster (AZ desc; M).
- Hurionthex: a shapeshifting sylvanshade (AZ desc; M). Xaga 67-81 with Tine and Beatha nearby (AZ; M).
- After 1.62, focus-pull farmers soloed most of it: Conservator 2 players, Lairmaster 3, Broodmother 4, Xaga 3,
  Hurionthex 2 (FH-galladoria, 17 Sep 2003; M).

**Not found:** IGN Galladoria Notes pages 4 and 6-20; exact Aroon guard messages and order.

---

## 5. Darkness Falls

**Room key** (PR p.359, Sep 2004; H for layout): Prince Abdin room 20, Princess Nahemah 28, Prince Asmoien 35, Prince
Ba'alorien 71; High Lords Oro 89, Baelerdoth 91, Saeor 93, Bain [Baln] 98; Dukes and Marquises 86-101; Grand
Chancellor Adremal 103; room 104 = Behemoth with the Chamberlain, Pale Guardian, Chaosian, Marquis Haurian,
inquisitors, mutilators, chthonic knights, umbrood warrior; Legion 105. Full key in
`wayback-pass2/other/prima-revised-expanded-2004-df-and-dragon-keys.txt`.

- **Legion:** heals by consuming players who die to him; ports a group into Behemoth's room, where the dead crawl up
  from the floor; ported players must run west to the teleporter back (FH-time-to-kill-legion, 6 Feb 2004; M, agrees
  with pass 1 = H). 4-5 full groups killed him in early 2004 (M). A Chamberlain respawn broke a small attempt (AZ Jul
  2004; L). One player thinks disease lowers his life-tap (L).
- **Behemoth:** a Grab Bag of 11 Jan 2008 says he is not killable, has no loot, and you only reach him by being sent
  (AZ desc; H, though late). Hits 2000-3000 (AZ 2003; M). A 2009 attempt saw him heal from ~35% to 60-70% (L).
- **Beliathan:** spawns rotting maggots without limit (they drop nothing; Sanya, Mar 2003; H). A blue zone message
  that the halls of Darkness Falls rumble marks his spawn; spawn seems independent of killing the princes (FH 2003 and
  2004, AZ Apr 2003; H). He then moves through the tunnels (Jan 2003; M). Hits ~900 on chain and fast; 1.5-2 groups
  kill him in 2004; a focus-shield pet could hold him for hours in Mar 2003 (M/L).
- **Princes** (AZ 2002, M): Albion prince is a plain tank; Midgard prince Abdin has two pets and hit a warrior for
  ~700; Hibernian prince Ba'alorien resists spells more, has one pet and heals each time he kills something (so pets
  feed him) (AZ 2006; L). Throne areas hold 3-4 inquisitor types and an earl; logout-pull the boss alone. Abdin's Rage
  (65) is Abdin's pet.
- **Princess Nahemah:** summons skeletal minions that cast a fire AoE; random spawn, roughly every 90 minutes or less
  (AZ Jun 2003; M). Later: a spell that turns pets on their owner, bainshee bolts (AZ 2006; L).
- **High Lord Oro:** three Oros come at once (AZ 2006; M, agrees with pass 1's "two duplicates"); PBAE power drain
  (L). **High Lord Baelerdoth:** pulsing PBAE absorb debuff and environment AEs (AZ 2006; L).
- **Grand Chancellor Adremal:** brings his whole room (BAF) if pulled wrongly (AZ Oct 2002; M).
- **Dukes/Marquises** (AZ 2004-2006; L-M): Marquis Focalleste summons 10-15 floating weapons below 50%; Marquis
  Scottiax's cloud pulses an 80-120 AE DoT; Duke Harboris opens with AE mezz and summons air pets; Marquis Sabonach
  shrinks; Duke Alloc resists all magic.

---

## 6. Dragons

### Common rules
- Two breath spells each; one greatly lowered in 1.62 (Apr 2003) (GB-69829; H).
- About 80-90% of dragon damage is magic; melee is rare and light (Gjalpinulva, FH-gov-dragon-raid, Nov 2003; M).
- Each dragon warns its breath target: "<dragon> glares at <name>"; that player runs away from the raid
  (FH Aug 2003 Gjalp; AZ 2006 Golestandt and Gjalp; H).
- Random AE stun on the melee, repeating (every ~30 s late in a Golestandt fight, FH-gratz-ss-golie-dead, 15 Oct 2003;
  "few times, completely random" on Gjalp, FH Nov 2003; M). Save purge for it.
- 24 is the ideal small-raid size with at least 9 rezzers; all three behave almost identically (Tiarta, FH Aug 2003, who links IGN topic 55001015 and
  claims 18 kills that week; probably the same player as pass 1's S7 Drasticle; M).
- "Pets landing the killing blow mean no loot" (AZ 2006; L).

### Golestandt (Dartmoor, level 80)
- 2002 accounts (M): the release-era breath hit for 3000-6000 (one rogue took a 6000 nuke, FH-up-close-and-personal,
  Jul 2002). The 2006 Allakhazam method: two cross-guarded paladins hold aggro, everyone (healers too) stands inside melee range, a solo rezzer waits
  outside the lair; giants respawn mid-fight; tossed players should strafe in the air to avoid landing on the mound;
  3-4 adds at 50% go for the healers (AZ Mar 2006, Dec 2008; L, late).
- Prima key: "Golestandt & Castle Grounds" (PR; H).

### Gjalpinulva (Malmohus, level 80) — first mechanics for the "None" entry
- Fight inside the "hort" (her ruined lair); anyone attacking from outside it gets the AoE (AZ 20 Dec 2002; M).
- Calls "retrievers"/dogs at random times (seen as early as 90% health); a dog that escapes the lair returns with about 6 (AZ 2006) to 10-20 (FH 2003)
  high-level drakulvs that are immune to crowd control. Cover every exit with ranged players (healers who can insta-mezz
  them, then tanks kill); dogs sometimes run through the lair walls, so stand a little outside each exit (FH-gov-dragon
  -raid-sunday-nov-9th, 7-10 Nov 2003; AZ 29 Dec 2006; H).
- Breath is cold: raids required capped cold resist plus cold buffs; one player took 1800+ at 63% cold resist
  (FH Nov 2003, FH-wake-up-gjalpinulva Jul 2004; H).
- Stuns a few times, at random; at ~30% drop det fields and save purge (FH Nov 2003; AZ Feb 2006; M).
- Killing "Yar" lowers her chance to exhale (AZ Dec 2002; L). Killing the Glacier Giant takes away her fire AE (AZ Oct
  2002; L, rumour). With over 100 people she "goes berserk" (AZ Oct 2003; L).
- Sizes: ~100 on Kay (Apr 2003), 27 on Tristan twice a day (Oct 2003), 23-person EU raids (Jul 2004), 21-25 people
  (2006). Prima key: "Gjalpinulva's Fortress (Dragon)" beside Drakulv settlements (PR; H).

### Cuuldurach the Glimmer King (Sheeroe Hills, level 80) — first mechanics for the "None" entry
- **1.61b (Mar-Apr 2003): made more vulnerable to heat** (AZ patch quote; H). Resists slash; a siege ram barely hurt
  him (crush) (FH Dec 2002; L-M, contradictory).
- **Edlina's write-up after a 120-player Prydwen kill, 3 May 2003** (FH-how-to-kill-cuuldurach; M, the best single
  source found):
  - Never send more than half the army at him; ~30 attacking at once, mostly tanks (~60% of tanks) plus a few mages.
  - Man all four lair entrances with 2-3 groups each (resurrectors and mages) at all times; these groups handle adds
    and rez, and never stay inside.
  - Resurrecting draws heavy aggro and often an AoE; rezzers run in, rez one or two, run out.
  - About 15 adds came at 40%; kill adds first. "Couriers" run off at very high speed and must be caught (implied: they
    fetch help).
  - Each AoE killed 15-25 people. He moves from entrance to entrance fighting whoever is there; the entrance that was
    hit rests while he moves on.
  - Stay off his mound; once the tanks followed him onto it and a large part of them died there.
  - Attack from behind; pull with a ranger, then one tank group engages, then the rest charge from all sides evenly.
  - At 15% all tanks with bows were ordered off, leaving only champions and nightshades in melee, "to avoid AoE stun".
- Earlier attempts (FH-dragon-raid-strategy, FH-the-dragon-ate-us-all, Dec 2002; M): below ~10% he stuns and turns
  invulnerable briefly, so damage must be held in reserve for the end; a final swarm of adds came as he neared death and
  wiped a raid at 5%; the first player into the ring is always blasted, then there is about a minute before the next
  AoE (L); rangers hit more often and harder from behind; he always faces whoever has the most aggro; nukes were
  resisted ~49 of 50 casts but ground-target AoE always landed.
- **Bug:** in Mar 2003 (EU) he hovered above his stone and nuked anyone within ~2300 range; the same was reported on a
  US server (FH-dragon-400-hibernia-0; M). 160 Hibernians failed twice at 95-98%.
- Players believed the Hibernian dragon needed far more tactics than the other two (FH Mar 2003; L).

### Xanxicar (Crystal Cave, level 80)
- 1.61: stomping message spam removed; defence lowered slightly (AZ patch quote; H). 1.62: the time before the Crystal
  Cave fills with lightning was increased (GB-69829; H) — so the lair has an **enrage timer that fills the cave with
  lightning**.
- Stomp ~1585 cold-looking damage; it is a frontal cone, safer behind him; throws players across the room for 99%; hits
  700+ on a plate tank (AZ Jan 2004, Feb 2006; M).
- Summons champions between 50% and 45%; bellows at 10%, AoE at 4%; an energy shield kills pet spam (AZ Feb 2006; L).
- Getting there is the hard part: an archer patroller, level-60+ guards, then 12-14 level-75 Xanxicarian Elites; 2005-6
  players dragged the elites aside with a passive necro pet (FH-avalon-city-dragon-raid-crystal-caves; M).

---

## 7. Midgard SI: Trollheim (Nosdoden), Iarnvidiur

- **Nosdoden** (Allakhazam level 70; Valmerwolf map 80; "Mutated Dragon"): ~900 per melee hit, ~200 per cast, a spit
  for ~800 and sometimes a double spit for ~1600. **Anyone who dies fighting him becomes a purple-con ghost that fights
  the raid**; the ghost vanishes when the player is rezzed (AZ description, ~2003; M) or after a duration (AZ 13 May
  2003; M). Keep many rezzers, casters out of the way; rezzing draws aggro (AZ 18 May 2003; M). A 42-person Bors raid
  (Oct 2003) killed the nedfalls first, let an aggro group take the front and hit from behind; ~30 minutes (AZ; M).
- **Trollheim chain** (VW-trollheim; AZMAP-beeblebrox; M): three round rooms in a row: Trollkarl Bonchar (62), The
  Rift (75), Nosdoden. Bonchar spawns "Void" (level 99) on death, which spawns a variable number of level-60 adds
  (Beeblebrox map). The Rift (listed 77/99): the Trollkarl at the table turns into a storm creature that keeps spawning
  red/purple morkenhet adds; 1 group on the Rift and 3 groups (with spiritmasters) on adds; the adds resist most nukes;
  target it with the nearest-enemy key (FH-killing-the-rift Nov 2004; AZ May 2003; M). First known Kay kill (18 May 2003)
  gave no loot or XP.
- Rotoddjur (70): a big mushroom that spawns 4-5 purple mushrooms on aggro (AZ Jun 2003; M).
- **Iarnvidiur** is only level 65 (AZ) with Hati (65) and Skoll (60) beside her; the hard parts are on the way:
  Delegalt (70) in a room of animus and dogs that all add (AZ 2003; M), Droom (69) with poison and disease DoTs (cure
  spam) (AZ May 2003, FH May 2003; M), Mahattava (70) hitting 300-400 (AZ 2003; M). Map and mob list in
  `maps/map-labels.txt` (Carne, Oct 2003). **Conclusion:** Iarnvidiur is a 1-2 group named, not an epic raid boss.

---

## 8. Tur Suil and Balor

- Spawn chain (VW-tursuil caption; AZ 2003; H): kill Kierac to respawn the Master of Pain; kill the Master of Pain to
  respawn Balor; the Master of Pain drops the key to Balor's room ("inner sanctum key"). Teleport pads 1-8 link the areas.
- **Balor (81):** an eye laser that goes through walls, cannot be interrupted and kills in two hits (AZ 24 Aug 2003; M);
  throws his target onto the Wall Spikes (level 90, untargetable, indestructible), which keep damaging until the player
  backs off them (AZ 3 Mar 2004; M); "a Legion-sized group" (AZ May 2003; L). Two guards at the entrance do not join
  (AZ 2012; L). EU players called it an easy 2-hour raid by Dec 2003 (FH; M).
- Queen Qunilaria (75): static; summons a tiny horror every 20-40 seconds; on death ~20 small copies swarm out
  (AZ Mar 2003; M). Abomos (68): sylvanshade adds; pull him out of his room (FH Jul 2003; M). Sargan double-hits;
  Kierac casts bladeturn (AZ; M).

---

## 9. Summoner's Hall, Hall of the Corrupt, Marfach, Doden's Gruva

- **Lesser Summoners (75)** (AZ Oct 2002; M): summon purple-con "bows" (animated weapons); they stop at about six
  unless those die; mezz the adds or kill them; parry a lot; one item each. Sidhe guards may be aggro by faction.
- **Grand Summoner Govannon (80)** (AZ desc, ~2002; M): three forms: elf caster, taller form, and true demon form at
  60%; 43 people took ~30 minutes. After New Frontiers (mid-2004): three nodes (Healing, Fire, Shield) plus two
  summoning orbs (one makes sacrifices, the other turns them into demons) and add portals; killing the Shield Node
  bugged him to invulnerable (AZ Jul 2004 - Sep 2005; M, post-period).
- **Summoner Cunovinda (60):** teleports, throws players into the air for ~75% fall damage, evades; the hardest of the
  summoners (FH-summoner-cunovinda Dec 2003, AZ Jun 2003 and 2006; H).
- **Summoner Roesia (55):** a vicious energy AE DoT (AZ 28 Dec 2003; M). Summoner Lossren (75): summoner adds (AZ; L).
- **Aidon the Archwizard (65-70)** (AZ desc, ~2002-2003; M): makes four clones when aggroed, each with his hit points
  and damage; they cast bladeturn and a damage shield and nuke hard; mezz the clones and stay on the original (killing
  clones is wasted, he recasts). He guards the way to the lower section; hugging a wall can sneak past.
- **Lord Gildas (55-60)** (AZ desc; M): blocks and parries constantly; sits in a pit like the Barrows deep-goblin pit.
- Hall of the Corrupt trash: Innocents proc a 40-minute disease (no regen, half healing), later shortened (AZ 2002-3;
  M). Lady Darra has a spectral paladin that heals her (AZ 2003; M).
- Marfach: the Blue Lady summons swords and axes without end; mezz them and focus her (AZ Sep 2002; M); 1.69: her
  weapons no longer leave the room (AZ patch quote; H). Red Lady resists nearly all magic (AZ 2003; M).
- Doden's Gruva: Thane Dyggve's AE hammers (AZ 2003; L); Aros the Spiritmaster (72).

---

## 10. Krondon and Avalon City

- **Scurceol Hyrde** (levels 59/69/79/89/99 by Allakhazam): four "rings"/orbs (Lyft Miht, level 81, map shows 4x) each
  add 10 levels to him; kill the orbs and he drops to 59; orbs respawn and push him back up, making him untouchable
  again (FH-krondon-a-nice-place-to-visit, 23-25 Feb 2003; M). ~40 people; four rings took about an hour.
- Krondon layout: easy half up to Scurceol Hyrde, hard half down to Orylle (Pin, FH-krondon-raid-sum-up, 27 Mar 2003;
  M). Hygegeomor Modesfa appears anywhere and vanishes after ~30 s (AZ; M); Deben se Gecynde fights broke when Modesfa
  wandered in with respawns (AZ Jul 2003; M); Morgus Urgalorg brings 8-10 red/purple adds and hits 650+ (AZ 2004; M);
  Orylle fights with her servant Orshom Bronde, who teleports players into his fire (AZ 2017; L); Fulafeallan splits
  into forms (AZ 2017; L). 1.68 (Feb 2004): hall guards and patrols spawn much more slowly (GB-71704; H).
- **Avalon City honour rule** (AZ Vera'erius, 2 Jun 2003; M): any proc, DoT, spell or pet makes Vera'erius shout about
  dishonourable curs and bring in his ten honoured guards; a group using only melee, heals and buffs, no procs, fights
  him alone. He calls his swords down on a few players three times and heals himself often below a third (AZ 29 Jun
  2003; M). The 10 guards can also be pulled away first (AZ Jan 2003; M).
- Dra'argus: shielded by a sphere in the room above his throne; the sphere is neutral, summons fleeing attackers back,
  and must die first (AZ 7 Jan 2003; M, guess by the poster). Sys'sro: in the sewers over a pit of guargans; casters
  must stand in the pool to land spells on the pit mobs; he shoots arrows until he wades in (AZ 23 May 2003; M).

---

## 11. Open-world: Glacier Giant, Evern, Green Knight, Morgana

- **Glacier Giant** (73-76, Jamtland/Odin's Gate border, loc 33322, 49113): not aggressive by default; throws random
  players who are not the main tank to random spots across Jamtland and Odin's Gate (fall damage, often death)
  (AZ 16 Oct 2002, FH-glacier-giant Dec 2003; H); hits 800+; 11-16 players killed him in 2002 (AZ; M).
- **Evern** (70-75, Breifine 25k, 63k): summons fairies that fly to 23 stones around him and heal him if they land;
  kill each fairy before it lands; an 8-player pet/focus-shield group killed him in ~20 minutes, Jul 2003
  (AZ; M).
- **Green Knight:** nothing new beyond pass 1 (Salaris guide; that guide is also mirrored in FH thread 115688).
- **Morgana:** no raid encounter in 2001-2005 sources; she appears only as lore (Avalon City, Undead Servants of Morgana
  faction). Treat as not a period raid boss (L, absence).

---

## 12. Contradictions and cautions

- Caer Sidi 4th horseman: "Furius" (VW map) vs Funus (all 2003 text). Crypt Lord 77 (map) vs 81 (AZ).
- Nosdoden 70 (AZ) vs 80 (VW). Fornfrusenen 75 (AZ) vs 82 (VW).
- Dragon breath triggers: Mythic says random and size-driven (2003); 2002 players say ranged aggro triggers it.
- Cuuldurach weak/strong damage types: heat vulnerability added 1.61b (H); slash-resistant (L); crush poor (L).
- Late comments (2006+) describe post-ToA/NF versions (Govannon's nodes, 1.100-1.101 dragon rework). They are marked
  late and should not drive 2003-era bot logic.

---

## Still missing

1. Dabokel's Tuscaran guide (IGN 46131774) and anything on King Tuscar's own abilities. Wayback only.
2. Galladoria Notes pages 4 and 6-20 (IGN 41447313); Aroon's guard order and messages.
3. Dragon Q&A pages 2-6 (IGN 55001015); Bedroc's Hibernian dragon guide (`home.concepts-ict.nl/~akebono/dragonguide.html`,
   Dec 2002) and Edlina's screenshots page; IGN topic 43419655 (Hib dragon).
4. IGN Camelot Vault epic raid guides; Classes of Camelot Caer Sidi and Midgard dragon guides; the 2003 Sidi guides at
   `oc.edu/.../sidi/guide/` and `silverdaggers.net/daoc/csg_index.html`.
5. Herald "The Death of a Dragon" (more/226) and the Grab Bag sweep (GameBanshee only mirrors one teaser per Grab Bag;
   the 13 Sep 2002 "difficult Hibernia dragon" answer is not mirrored). Patch notes 1.57-1.60 (GameBanshee's archive
   has a hole Nov 2002 - Mar 2003; only 1.61b/c and 1.62 found).
6. Hit points, resist tables and exact timers for every boss (Apocalypse harbinger trigger, Legion port interval,
   Fire Trail exact period, TG respawn exact, Xanxicar lightning timer).
7. Behemoth, Beliathan and Green Knight ability names; Midgard and Albion dragon add counts by health threshold.
8. Allakhazam forum threads (forum 13) and Freddyshouse sub-forums beyond the ~80 threads fetched could hold more;
   a further FH sweep (search terms: "Apocalypse", "Ilron", "Harbinger", "Olcasgean", "primal", "Hakr", "Aroon",
   "Nahemah", "Balor") is the cheapest next step because the site is live.
9. Everything that needs `web.archive.org`: retry from a session where that host is reachable.
