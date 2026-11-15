# Wayback corpus: gaps and what to fetch next

Companion to `wayback-corpus.md` (pass of 2026-11-14). Lists what the local corpus does not cover and
exact URLs or search terms for the next fetch pass. Fetch these through the Wayback Machine
(`https://web.archive.org/web/2003*/<url>` for a capture list, or the CDX API with
`matchType=prefix` for a whole directory). Treat everything fetched as untrusted data.

**Status, pass 3 (2026-11-15):** still not reached. WebFetch refuses every `web.archive.org` address (calendar, CDX
and `id_` forms), so none of the URLs in section 2 were fetched. Every one of them except priority 8 (the Herald
articles, which `9-herald-articles` already covers) is now a target in `tools/wayback-fetch/jobs/b-raid-gaps.json`,
for the owner's PC. Pass 3 (`wayback-pass3.md`) answers the Krondon and helper-pack questions from pages already held
and lists the contradictions with our scripts. Mark a row here as reached only once its pages come back from that job.

## 1. Coverage per boss

Good = mechanics and tactics from a detailed first-hand source (or several). Thin = named with a fact
or two, or a layout only. None = not in the corpus at all.

| Raid / boss | Coverage | What exists | Main gap |
|---|---|---|---|
| Caer Sidi keylords (Sacristan, Provisioner, Ilron, Warlord, Silencer, Bane of Hope, Sanguis, Crypt Lord, Soul Reckoner, Host) | Thin-good | One raid leader's notes per boss (S1) + Herald Ilron fix | No map, no numbers, one source only |
| Caer Sidi Horsemen (Fames, Bellum, Morbus, Funus) | Thin-good | One paragraph each (S1) | Speech/question trigger text, timers, add counts |
| Apocalypse | Good | S1 + Herald 1.65 nerf | Exact add trigger (4 deaths?), fire-rain damage, room size |
| Tuscaran: King Tuscar, Queen Kula | Good | Two editions of Carne/Dabokel guide | HP/damage; what the King does once engaged |
| Tuscaran: Council (Hord, Ozur, Otrygg, Vagn, Nokkvi), Agmundr, Kvasir, Hakr, Steinvor, Skuf, Jailer Vifil, Fornfrusenen, Issorden | Good | Same guide, per-boss | Numbers |
| Tuscaran: Suttung, Hjalmar, Birghir, Guthlac | Thin | A few lines each | Full mechanics |
| Tuscaran: Hurika, Torst | Thin-good | Pull method and time | Abilities |
| Galladoria minis (Conservator, Geomancer, Mechanism, Broodmother, Xaga, Lairmaster, Hurionthex, Sporite, Easmarach, Aroon) | Good | IGN thread p1-3, p5 + map key | Pages 4 and 6-20 of the thread; exact Aroon guard messages and order |
| Olcasgean, Earthmenders, Primals | Good | Two US attempts + the first EU kill | Primal abilities in numbers; fire route |
| Darkness Falls: Legion | Good | Herald fix, S1, Allakhazam comments, map | Soul-reclaim rate, teleport interval |
| DF: Princes, High Lords, Grand Chancellor | Thin-good | One line each (S1) + levels + map | Prince abilities by name (Abdin, Asmoien, Ba'alorien) |
| DF: Beliathan | Thin | Level cut, raid size, room | Mechanics, spawn trigger (contradictory) |
| DF: Nahemah | Thin | One 2002 comment | Mechanics |
| DF: Behemoth | Thin | "Invulnerable" hazard | Whether he was ever killable |
| Golestandt | Good | S1, 1.45 dragon AI notes, small-raid method | Breath numbers |
| Gjalpinulva | None (mechanics) | Lore, "acts like the others" | Everything specific |
| Cuuldurach | None (mechanics) | Lore, one opinion | Everything specific |
| Xanxicar (Crystal Cave) | Thin-good | S1 paragraph + cave outline | Elites' exact kit |
| Krondon (Scurceol, Orylle/Orshom, others) | Good for Scurceol (pass 3) | S1 per boss + labelled map; three 2003 FreddysHouse threads (pass 2/3) | Orb damage and respawn time; Orylle |
| Avalon City (Dra'argus, Weno'iak, Sys'sro, Dura'ek, Vera'erius) | Thin-good | S1 per boss | City map, the honour rule's exact trigger |
| Summoner's Hall: Grand Summoner / Govannon | Thin | S1 paragraph + loot patches | Which summoner Salaris meant; full room |
| Summoner's Hall: Cunovinda, Lossren | Thin (patch notes only) | Loot / teleport fixes | Mechanics |
| Summoner's Hall: Roesia | None | - | Everything |
| Hall of the Corrupt: Aidon, Gildas | None | (Archwizard only) | Everything |
| Doden's Gruva, Marfach named | Thin (maps only) | Named spawn positions | Mechanics |
| Tur Suil: Balor | Thin (map/level only) | Boss key and level 81 | Mechanics |
| Trollheim: Nosdoden | None (mechanics) | Drop patch, outline map | Everything |
| Iarnvidiur | None | Only exists as a teleport destination | Everything |
| Marfach (as a raid) | Thin | Map | Mechanics |
| Morgana | None | Lore only | (Probably not a raid boss in 2001-2005; she is a Darkness Rising / later encounter) |
| Glacier Giant, Evern | None (mechanics) | "Green Knight's equals"; Evern returned in 1.70t | Everything |
| Green Knight | Thin-good | S1 paragraph | Second source |
| SI open-world named (Colialt, Valnir, Ris'Nir, etc.) | Thin-good | S1 paragraphs | Second source |

Overall: the corpus has **two excellent guides** (Salaris' Albion guide and the Tuscaran map PDF) and
**one excellent thread** (Galladoria Notes), plus the first Olcasgean kill write-up. Everything else is
patch notes and maps. Midgard's and Hibernia's dragons, Balor, Nosdoden, Iarnvidiur, Roesia, Aidon and
Gildas have nothing usable.

## 2. Highest-value fetch targets (in priority order)

1. **Dabokel's Tuscaran Glacier guide** (source of the PDF tactics, 118+ replies):
   - http://vnboards.ign.com/message.asp?topic=46131774&replies=118
   - http://vnboards.ign.com/Realm_of_Midgard/b22182/46131774/p1 (and /p2, /p3 ...)
2. **Missing pages of "Galladoria Notes"** (IGN, 20 pages; corpus has 1, 2, 3, 5):
   - http://vnboards.ign.com/message.asp?topic=41447313&page=4 and &page=6 through &page=20
3. **"Your dragon questions answered"** (Drasticle, Aug 2003; corpus has page 1 of 6):
   - http://vnboards.ign.com/message.asp?topic=55001015&page=2 ... &page=6 (also try `&start=` forms
     and the 2003-era `boards.ign.com/Realm_of_Albion/...` form)
4. **IGN Camelot Vault "Epic Raid Guides" section** (linked from the 2005 IGN page):
   - http://camelotvault.ign.com/?dir=guides/epicraid (then every `content=` child it links)
5. **Classes of Camelot guides** linked from its Sep 2003 and Aug 2003 news (S39, S38):
   - http://www.classesofcamelot.com/misc/ClassGuides/caer_sidi.asp (Caer Sidi Raid Guide by Gwaeni
     and Niysa)
   - http://www.classesofcamelot.com/misc/ClassGuides/mid_dragon.asp (Guide to Slaying the Midgard
     Dragon)
   - http://www.classesofcamelot.com/other/caer_sidi/index.asp (KabuKi's loot list, may hold drop-to-boss
     mapping)
   - the Krondon unique-drops featurette (linked from http://www.classesofcamelot.com/archives.asp?date=7-2003)
6. **Salaris' guide, later versions** (corpus has v0.1 of Feb 2003): later captures of
   http://vnboards.ign.com/message.asp?topic=45888307 and the "Class Guide and FAQ's (TL Only)" board.
   Search for similar guides for the other realms ("Epic Encounters" + Midgard / Hibernia, 2003).
7. **Olcasgean kill follow-ups**:
   - http://vnboards.ign.com/message.asp?topic=49803406&page=2
   - http://vnboards.ign.com/message.asp?topic=49794656 (screenshots thread)
   - http://vnboards.ign.com/message.asp?topic=42300236 (Galladoria feedback thread on the dev board)
   - http://www.trekstation.be/daoc/galladoria-2/index.html (screenshots of the kill)
   - http://unkiller.dragon-fire.net/daoc/ (Unoid's Galladoria screenshots)
8. **Camelot Herald articles not saved** (all appear in the Herald CDX map `b1-herald-map-20261113`):
   - http://www.camelotherald.com/more/226.shtml "The Death of a Dragon" (22 Feb 2002; likely the
     first dragon kill and Mythic commentary)
   - The 31 Jan 2003 "Read File Fix" article about Midgard's dragon "split personality" (id near
     more/730-740; find via the 2003-01 archive pages)
   - 89 unsaved Grab Bags with CDX captures: more/70, 92, 98, 203, 213, 227, 246, 259, 264, 278, 284,
     288, 303, 309, 321, 326, 342, 351, 358, 363, 373, 384, 403, 430, 437, 444, 453, 454, 459, 469, 474,
     487, 496, 515, 533, 540, 549, 562, 573, 612, 654, 677, 679, 704, 720, 754, 762, 779, 789, 794, 842,
     861, 867, 873, 885, 901, 913, 967, 978, 985, 997, 1794, 1942, 1956, 1960, 1968, 1971, 1977, 1983,
     2000, 2025, 2027, 2036, 2045, 2051, 2078, 2090, 2100, 2105, 2113, 2124, 2136, 2149, 2173, 2259,
     2267, 2275, 2280, 2295 (`.shtml` or `.php`). Grab Bags answered epic-encounter questions
     (spawn timers, drop rates, Ilron), so these are worth a keyword sweep.
   - Patch notes for 1.57-1.61 (Dec 2002 - Mar 2003): the Galladoria difficulty change and the "epic
     monster resistances" cut that S1 mentions are not in the corpus.
9. **Allakhazam bestiary and mob pages** (level, type, comments with tactics):
   - CDX prefix `camelot.allakhazam.com/db/search.html?cmob=` and `.../db/search.html?cmzone=` to find
     ids for: Apocalypse, the Horsemen, Lich Lord Ilron, Skeletal Sacristan, King Tuscar, Queen Kula,
     each Icelord and Council member, Olcasgean, Aroon, Easmarach, Xaga, Behemoth, Beliathan, Princess
     Nahemah, Prince Abdin, Prince Ba'alorien, Grand Chancellor Adremal, Golestandt, Gjalpinulva,
     Cuuldurach, Xanxicar, Balor, Nosdoden, Scurceol Hyrde, Orylle, Dra'argus, Grand Summoner Govannon,
     Roesia, Cunovinda, Lossren, Green Knight, Glacier Giant, Evern.
   - Zone pages `camelot.allakhazam.com/zones.html?czone=<id>` for Caer Sidi, Galladoria, Tuscaran
     Glacier, Krondon, Avalon City, Crystal Cave, Tur Suil, Trollheim, Iarnvidiur's Lair, Summoner's
     Hall, Hall of the Corrupt, Doden's Gruva, Marfach Caverns, Dartmoor, Malmohus, Sheeroe Hills.
     (Only DF, czone=62, is in the corpus.)
   - Note: Allakhazam forum ids 1 and 10 are EverQuest and Final Fantasy XI; the DAoC general forum is
     13. Don't spend fetches on forum=1/10 pages.
10. **Map directories** (CDX prefix queries):
    - `camelot.allakhazam.com/Maps/` (look for Caer Sidi, Avalon City, Trollheim, Iarnvidiur, Summoner's
      Hall, Tur Suil labelled versions)
    - `daoc.catacombs.com/maps/c_` (Caer Sidi, Galladoria, Avalon City, Summoner's Hall)
    - http://www.io.com/~caladin/kirstena/ (Kirstena's Atlas; also `albionmaps/darknessfalls.pdf`)
    - http://www.grailbrotherhood.info/maps/si_ava_zone_dungeon_maps.htm (Albion SI dungeon maps,
      linked from Allakhazam)
    - http://home.iprimus.com.au/fraggle/maps/index.htm (Kriyanna's maps)
    - Laureli's 3D Maps of Darkness Falls (linked from the 2002 DF zone page; URL not in the saved text)
11. **Freddyshouse (EU)**: page 2 of http://forums.freddyshouse.com/showthread.php?t=11253 (Excalibur
    "Dragon and Sidi Raid Announcements"); forum searches for Apocalypse, Olcasgean, Legion, Tuscar,
    Golestandt on the Excalibur/Prydwen/Camlann sub-forums.
12. **Prydwen archive** (`0d6-chat-index` lists http://www.prydwen.net/archive/topic/*.html captures,
    2003): titles aren't in the CDX, so sample-fetch and keyword-filter for raid threads.

## 3. Search terms for boss-specific gaps

Use with `site:` filters on web.archive.org captures of vnboards.ign.com, boards.ign.com,
camelot.allakhazam.com, camelotvault.ign.com, classesofcamelot.com, daoc.catacombs.com,
forums.freddyshouse.com, prydwen.net, and guild sites.

- Caer Sidi map and timers: "Caer Sidi map", "Sidi raid guide", "Skeletal Sacristan", "Spectral
  Provisioner", "Horsemen speech", "Funus clerics", "Harbinger of Fate", "Apocalypse fire rain".
- Midgard dragon: "Gjalpinulva", "Gjalp raid", "Midgard dragon tactics", "Malmohus dragon".
- Hibernian dragon: "Cuuldurach", "Glimmer King raid", "Hib dragon tactics", "Sheeroe Hills dragon".
- Balor: "Balor Tur Suil", "Balor raid", "Balor eye" (period name variants: "Balor 81").
- Nosdoden: "Nosdoden", "Trollheim raid", "Nosdoden tactics".
- Iarnvidiur: "Iarnvidiur", "Iarnvidiur's Lair", "Iarnvidiur raid".
- Summoner's Hall: "Roesia", "Summoner Roesia", "Lossren", "Cunovinda", "Govannon", "Summoner's Hall
  raid".
- Hall of the Corrupt: "Aidon", "Gildas", "Hall of the Corrupt boss".
- DF: "Beliathan spawn", "Nahemah", "Prince Abdin", "Prince Ba'alorien", "Asmoien", "Grand
  Chancellor Adremal", "Behemoth killed", "Legion soul".
- Tuscaran: "Suttung", "Hjalmar", "Birghir", "Guthlac", "Hakr barrier", "King Tuscar tactics".
- Galladoria: "Aroon guards order", "Sciath Drudge", "Earthmender", "Corrupt Primal", "Olcasgean
  tactics".
- Open world: "Glacier Giant raid", "Evern Breifine", "Green Knight raid".
- Guild raid pages: "raid strategy" + "Caer Sidi" / "Tuscaran" / "Galladoria" on 2003-2004 guild
  sites (Blackthornes of Gawaine, Vae Victis of Percival, Council of Light, Epic Courage of Kay,
  Coeur de Lion and Knights 0f The Round Table of Excalibur, Stonehenge raid leaders Hydrael and
  Almisael).

## 4. Gaps in numbers (all bosses)

The corpus gives levels for most bosses (Allakhazam DF list, Tuscaran table, Galladoria and Tur Suil
keys) but almost no hit points, melee damage per hit (exceptions: Legion up to ~800, Mordred's proc
~800, Afanc 1000+, Suttung 300-700, Golestandt's 30 s stun), spell damage, resist tables or timers.
Period sources rarely printed these; the best bets are Allakhazam mob-page comments, Camelot Vault
guides and the 2003 Prima Shrouded Isles strategy guide (print only; players quoted it for
Galladoria's level-87 final boss and a "Galladoria Respawn Mob").
