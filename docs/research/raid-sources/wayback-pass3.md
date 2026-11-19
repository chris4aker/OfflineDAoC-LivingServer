# Raid bosses, pass 3: the Wayback gaps, Krondon, helper packs, and checks against our scripts (2026-11-15)

Companion to `wayback-corpus.md` (pass 1), `wayback-pass2.md` (pass 2) and `wayback-gaps.md` (the gap list). Written for
Living Server 1.2's raid AI, priority on the raids that lost or stalled on the owner's PC. Everything is in our own
words; quotes are a few words at most. Confidence levels are the ones pass 2 uses:

- **H:** official Mythic text, or two independent first-hand period sources that agree.
- **M:** one first-hand period account, or a period map or database entry.
- **L:** rumour, second-hand, contradicted, or written after 2005 ("late").

## 0. The Wayback Machine was not reachable from this session either

Tried on 2026-11-15 with the WebFetch tool (not curl), once each, in the three forms the task named:

| Form | URL tried | Answer |
|---|---|---|
| Calendar capture | `https://web.archive.org/web/2003/http://www.vnboards.ign.com/` | refused: URL not on the session's allowlist |
| CDX API | `https://web.archive.org/cdx/search/cdx?url=vnboards.ign.com/message.asp?topic=46131774*&output=json&limit=20` | refused: same |
| Raw `id_` form | `https://web.archive.org/web/2003id_/http://vnboards.ign.com/message.asp?topic=46131774&replies=118` | refused: same |

The refusal says a URL can be opened only after it appears in a web-search result. Web search never returns
`web.archive.org` pages (a search limited to that domain was rejected by the search proxy, HTTP 400; open searches for
the gap pages return only live sites), so no capture could be opened. Nothing was routed around: no curl, mirror, cache
or third-party archive was used.

**Result: 0 of the gap URLs were fetched. All of them, about 150 (gap list section 2 priorities 1-7 and 9-12, plus
the pass 2 leads), are in a new job for the owner's PC tool:
[`tools/wayback-fetch/jobs/b-raid-gaps.json`](../../../tools/wayback-fetch/jobs/b-raid-gaps.json)** (156 targets: 136
exact URLs, 20 prefixes). Herald articles (priority 8: "The Death of a Dragon", `more/226`, and the 89 Grab Bags) are
not repeated there, because `9-herald-articles` already covers every `/more/` id. The 1.57-1.61 patch notes come from
`2-patch-notes`.

**To run it on the PC:** copy `tools/wayback-fetch/` to `C:\Games\OfflineDAoC\wayback-fetch\`, drag
`jobs\b-raid-gaps.json` onto `RUN-WAYBACK-FETCH.cmd` (or run all jobs), then double-click `PACK-RESULTS.cmd` and bring
back `out\_packs\b-raid-gaps-<date>.zip`. It needs about 160 index requests and at most 700 downloads, well under one
day's cap (1,500).

What the job holds:

| Gap | Targets |
|---|---|
| 1. Dabokel's Tuscaran guide (IGN 46131774) | the `replies=118` link, the thread under any later parameter, pages 1-6, and the `/Realm_of_Midgard/b22182/46131774/` path on both IGN hosts |
| 2. Galladoria Notes (IGN 41447313) | pages 4 and 6-20 (the earlier run saved only pages 1, 2, 3 and 5, all captured 12 Jul 2003), plus the later path form |
| 3. Dragon Q&A (IGN 55001015) and IGN 43419655 | pages 2-6, the later path form, the Hibernian dragon thread |
| 4. Camelot Vault epic raid guides | the section page and a keyword lookup for its children |
| 5. Classes of Camelot | the Caer Sidi raid guide, the Midgard dragon guide, the `ClassGuides/` and `other/caer_sidi/` folders, the July 2003 news archive and the Krondon featurette |
| 6. Salaris' guide, later versions | IGN 45888307, one capture per month |
| 7. Olcasgean follow-ups | IGN 49803406 page 2, 49794656, 42300236, the trekstation and unkiller screenshot sites |
| 9. Allakhazam bestiary | 63 boss pages in the old `db/search.html?cmob=<id>` form (ids taken from pass 2; the old and new ids match for Legion 2861, Saeor 3073 and Asmoien 3123), the Raumarik Revenant (id unchecked), and the mob list and zone page of 16 raid zones |
| 10. Map folders | Allakhazam `Maps/`, Catacombs `maps/c_`, Kirstena's Atlas, Grail Brotherhood, Kriyanna's maps (images and PDFs) |
| 11-12. FreddysHouse, Prydwen | page 2 of FH thread 11253; a 40-page sample of the Prydwen 2003 archive |
| Pass 2 leads | the 2003 Caer Sidi guides at `oc.edu/.../sidi/` and `silverdaggers.net/daoc/`, Bedroc's Hibernian dragon guide |

Not in the job: the guild-site searches of gap list section 3 (the index cannot be searched by words, only by
address), and Allakhazam forum threads (they need `MAKE-THREAD-JOBS.cmd` after a listing run).

## 1. What this pass could still do

The period pages already held were re-read for the task's priority questions: the owner's earlier Wayback captures
(the packs of 2026-11-13, raw copies outside the repository, mined in pass 1) and pass 2's live FreddysHouse,
Allakhazam and GameBanshee text. Then they were checked against our boss scripts and the hand notes
(`docs/encounters/hand-notes.json`, read only; this pass changes no code and no hand notes). Section 4 lists every
contradiction found.

Source keys used below:

| Key | Where | Date |
|---|---|---|
| SAL | Salaris' Guide to Albion Epic Encounters, `http://vnboards.ign.com/message.asp?topic=45888307&replies=0` | Wayback capture 2003-04-07 (guide v0.1, Feb 2003) |
| HER-1197 | Camelot Herald 1.66h notes, `http://www.camelotherald.com/more/1197.shtml` | Wayback capture 2004-12-08 |
| AZQ | Allakhazam Albion quest list, `http://camelot.allakhazam.com/quests.html?realm=Albion&` | Wayback capture 2004-09-08 |
| FH-KR1 | FreddysHouse "Krondon, a nice place to visit....", `https://forums.freddyshouse.com/threads/krondon-a-nice-place-to-visit.114706/` | posts 23-25 Feb 2003 (live) |
| FH-KR2 | FreddysHouse "SS Alliance Krondon raid" (Pin) | posts 27 Mar 2003 (live) |
| FH-KR3 | FreddysHouse "Krondon raid sum up" | posts 8-11 May 2003 (live) |
| FH-GG | FreddysHouse "Glacier Giant 0 - Midgard 1", `https://forums.freddyshouse.com/threads/glacier-giant-0-midgard-1.86858/` | post 1 May 2002 (live) |
| AZ-<id> | Allakhazam bestiary, `https://camelot.allakhazam.com/db/mob/<id>` | comment dates as given (live) |
| GB-69943 | GameBanshee mirror of the 1.63 patch notes, `https://www.gamebanshee.com/news/69943-camelot-version-163-patch-notes.html` | 24 Jun 2003 (live) |
| VW | Valmerwolf labelled dungeon maps (pass 2) | undated, labels match 2003-2004 |

"Live" pages are today's copies of period posts; their post dates are given instead of a capture date.

## 2. Krondon: could a level-50 raid kill Scurceol Hyrde and his Lyft Miht orbs?

**Yes, and players did it often in 2003.**

- **The quest he ends is a level-49 quest.** "Ogre's Might" is listed at level 49 with Scurceol's Bracer as the reward
  (AZQ, capture 2004-09-08; M). Salaris names him as the quest's boss (SAL, 2003-04-07; M).
- **Kills by level-50 raids.** Salaris had killed him on live by early 2003 (SAL; M). An EU leader's raid killed him
  again on 8 May 2003 ("once again"), with level 47-50 players posting afterwards, one on their third Scurceol belt
  (FH-KR3; M). Pin's Mar 2003 note splits the dungeon into an easy half ending at Scurceol and a hard half ending at
  Orylle (FH-KR2; M). Together: **H** that a level-50 raid could kill him.
- **Raid size.** About 40 players (FH-KR1, 23 Feb 2003; M); Salaris suggests about 50, mainly to hit the high-level
  nodes reliably (SAL; M). A 3-group attempt gave up (FH-KR1; M). So: **40-50 level-50 players**.
- **How the orbs worked then.**
  - Four of them, stationary (SAL; M). Allakhazam lists "Lyft Miht" as a **level-81 elemental** (AZ-4734, entry dated
    2006, one 2006 comment; M), and the Valmerwolf map shows it four times (VW; M).
  - While they live he cannot be harmed (SAL; M). A Feb 2003 poster put it another way: each orb adds 10 levels to
    him, and with all four dead he is level 59 (FH-KR1; M). Allakhazam gives his levels as 59, 69, 79, 81, 89 and 99
    (AZ-4735; M). Both readings say the same in practice: kill the orbs first.
  - The orbs **fight by throwing players into the air** (fall damage; /stick lessens it) (SAL; M).
  - **They respawned.** A raid of about 40 took roughly an hour for the four, had him at about 50% when one came back,
    and he could not be touched again; another raid saw him heal back to full after the orbs returned (FH-KR1,
    24-25 Feb 2003; M). The advice was more groups, to finish him before the next orb (M).
  - When the first orb was hit, many raiders died within seconds (FH-KR1; M). Pet spam worked well on the orbs (SAL; M).
- **Credit.** In Feb 2003 only the main-aggro group got the quest step (FH-KR1; M). 1.66h gave his quest credit to
  the whole battlegroup (HER-1197, capture 2004-12-08; H).

**Against our script:** see contradictions C1-C4. The short version: our orbs are level 90 with high resists and AF,
our Scurceol is a fixed level 81, and on the owner's PC (build e90bf21, 2026-11-11) the orbs took no damage in 75
minutes. The period orb was level 81, and the boss was meant for a 40-50 player level-50 raid.

## 3. Helper ("BAF") packs: did period players report them?

The question: our world lacks upstream's helper spawns for five bosses (hand notes, `upstream.helperPacks`). Did
2001-2005 players see helpers come?

| Boss and upstream pack | What the period sources say | Conf. |
|---|---|---|
| Black Lady and the Ancient Black Oak (`AncientOakBaf`, 14 "Ancient Oak") | The Oak is described as about level 65 and **not aggressive** (AZ-3968 description). One player had killed close to 100 withered oaks in the area with no faction change, and the Ancient Black Oak stayed neutral to him; he guessed that attacking the Black Lady might anger it (AZ-3968, 10 Dec 2002). Another killed the Black Lady and saw the tree only **"wiggles"** and not aggro, while **a group of ogres came** when she was attacked (AZ-3968, 28 Jun 2003). **No period report of oaks coming to help.** The ogres match our script's Ogresses. | M (two comments that agree) |
| Chieftain Caimheul (`CaimheulBaf`, reanimated guardians) | Only "a tough named guard" of about level 65 near the Summoner's Hall entrance (AZ-3970 description, undated). No comments; no helpers mentioned. | L (absence) |
| Cailleach Uragaig (`CailleachUragaigBaf`, 3 guards) | Nothing found in any held source. | none |
| Raumarik Revenant (`RevenantBaf`, 11 wraiths and wights) | Killed on Excalibur by spring 2002, no details (FH-GG, 1 May 2002; L). 1.63 fixed his damage table and stopped him healing himself when he hit someone in a Legion breastplate (GB-69943, 24 Jun 2003; H). Raumarik's epics start neutral and turn aggressive after several kills (pass 2's Midgard notes, Allakhazam 2002-2003; M). No helpers mentioned. | L (absence) |
| Runil (`RunilBaf`, 4 clan deserters) | Nothing found. | none |

Conclusion for the open question in the hand notes: the period evidence gives **no reason to add** the 14 Ancient Oaks
(the only first-hand reports say the Oak itself stays out of the fight and the Black Lady's help is ogres). For the other
four packs the sources are silent; absence is weak evidence, so the job above fetches their Allakhazam pages again in
their 2002-2005 form.

## 4. Contradictions with our boss scripts and hand notes

Not fixed here (no code or hand-note edits in this pass). Each line: what we have, what the period says, the source.

| # | Boss | Ours (script / hand notes) | Period | Source, date, conf. |
|---|---|---|---|---|
| C1 | Lyft Miht orbs (Krondon) | Level 90, 15,000 health, 40% melee / 50% magic resists, AF 250 | Level 81 | AZ-4734 (2006 entry), VW map; M |
| C2 | Scurceol Hyrde | Fixed level 81, immune while an orb lives | 59 with no orbs, +10 levels per live orb (59-99); "invulnerable" while nodes live | FH-KR1 (Feb 2003), AZ-4735, SAL (2003-04-07); M |
| C3 | Lyft Miht orbs | Never respawn (only his own respawn brings them back) | Respawned during the fight, and he went back to full health | FH-KR1, 24-25 Feb 2003; M (already noted as an era note in the hand notes) |
| C4 | Lyft Miht orbs | Never attack; their throw picks only real players, so it is off for bots | They fought by throwing raiders into the air | SAL, 2003-04-07; M |
| C5 | Ancient Black Oak | Dies when the Black Lady dies; level 68 | Stayed alive and neutral after she died ("wiggles"); level about 65 | AZ-3968, 28 Jun 2003 and description; M |
| C6 | Summoner Roesia | Level 75 (her script's fallback spawn; the world row may differ), 100,000 health | Level 55; three level-50 players killed her in about 45 minutes in 2005 | AZ-3958 (level M; the 3-player kill is late, Aug 2005, L) |
| C7 | Grand Summoner Govannon | At 80% gains strength and summons a sacrifice and a demon that walk to his altar (a Shade if both arrive); at 50% turns demon, heals to 75%, strength 800, bleed, Shroud of Agony | 2003: changes form after losing about 35% health, mostly cosmetic; parries; heals himself; 4-5 groups. About 2002 database text: true demon form at 60%. The sacrifice-and-demon orbs are reported only after New Frontiers (mid-2004 to 2005) | SAL 2003-04-07 (M); AZ-3789 description (M); pass 2 AZ Jul 2004 - Sep 2005 (post-period) |
| C8 | Nosdoden (Trollheim) | Nine waves of 12-18 "essence of fallen" at every 10% (about 110-160 adds a fight); level 80 | No period report of add waves; the reported mechanic is ghosts of dead raiders (which we also have); a 42-person raid killed him in about 30 minutes; Allakhazam level 70 (map: 80) | pass 2: AZ-4687 May and Oct 2003; M (absence for the waves: L) |
| C9 | Dragons (all three) | At most six adds a fight, at 85/70/55/40/25/10% | Cuuldurach: about 15 adds at 40% and a last swarm near death (EU, Dec 2002 and May 2003); Gjalpinulva's runners return with about 6-20 drakulvs | pass 2: FH-how-to-kill-cuuldurach (3 May 2003), FH-the-dragon-ate-us-all (Dec 2002), FH-gov-dragon-raid (Nov 2003); M |
| C10 | Funus (Caer Sidi) | Clerics, and archer classes with their bow or crossbow, can hurt him | Only clerics (Allakhazam's description; an EU raid call, 31 Mar 2003); one Dec 2003 player adds the archery classes | pass 2: AZ-4236, FH-caer-sidi-advice; clerics only H, archers M |
| C11 | Apocalypse | Flies at 99% and 50%, 30 s each, raining fire under him | On the pull he flies up and fire rains from the ceiling for the rest of the fight | SAL, 2003-04-07; M |
| C12 | Icelord Hakr (Tuscaran) | No immunity; his toss comes only while an iceweaver lives | He sits behind a barrier (a level-50 "Hakr's Barrier" object) that drops for a normal fight when all four Royal Iceweavers are dead | pass 1: Tuscaran guide S9 (Jul 2003) and S10 (Jan 2004), Allakhazam level table; H |
| C13 | Icelord Agmundr | Six hrimthursa, cleared first; pulling one does not bring him | He calls every live hrimthursa seer when fought: four, two on each platform | pass 1: S9/S10; M-H |
| C14 | Soul Reckoner | Hand notes disagree with each other: "a quarter of the damage" while a soul lives (owner change, 2026-10-14/15) versus "his original full shield on" in the AI summary | Period: the souls must die before he can be hurt | SAL (M); AZ 2012 (late, L). The period matches the full shield; the quarter-damage rule is the owner's own change |

Checked and consistent with the period (no contradiction): Skeletal Sacristan never attacks and runs a loop;
Spectral Provisioner runs and must be ambushed; Crypt Lord's court comes with him; Morbus's swarm must die first;
Kvasir heals from cold and mesmerizes; Fornfrusenen's shards first; King Tuscar and Queen Kula, queen first, with
mesmerize and root in the room's middle; Issorden's skeletons first; Olcasgean asleep until all four primals die,
Earthmenders one class each; Spindler Broodmother's spindler burst at death; Legion's kill heals and his throw into
Behemoth's hall; High Lord Oro's copies; the Black Lady's ogre adds.

## 5. Per priority boss: what is still missing

Only the job above can close these (all on IGN, Camelot Vault and Classes of Camelot):

- **Caer Sidi:** the 2003 raid guides (Classes of Camelot, oc.edu, silverdaggers) for the horsemen's speech, the
  minute between horsemen, Harbinger timing, room map. KabuKi's loot list for drop-to-boss mapping.
- **Tuscaran Glacier:** Dabokel's thread (the source of the guide), for King Tuscar's own abilities and numbers.
- **Galladoria:** Galladoria Notes pages 4 and 6-20, for Aroon's guard order and messages, Easmarach and the primals.
- **Summoner's Hall:** Roesia, Lossren and Govannon before New Frontiers (Allakhazam's 2002-2004 page versions).
- **Krondon:** the Classes of Camelot unique-drops featurette; a second source on the orbs' damage and respawn time.
- **Darkness Falls:** Legion's port interval and soul-heal rate; the princes' abilities (Allakhazam's period pages).
- **Trollheim and the dragons:** the Midgard dragon guide, the dragon Q&A pages 2-6, Bedroc's Hibernian dragon guide;
  whether Nosdoden ever had add waves.
- **Helper packs:** the period Allakhazam pages of the five bosses above.
