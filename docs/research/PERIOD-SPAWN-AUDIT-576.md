# Period spawn audit (#576): what can be checked now, and what DataLoggin still has to answer

Report only, written 2026-11-26 by the Account #2 session. **No spawn, world row or game file is changed.** No DataLoggin row,
and no row from any other source, is copied here: this file holds counts, zone names and the method.

## Summary

- **The DataLoggin comparison could not be run.** The issue asks for our world to be compared with DOL DataLoggin `DB1.2.zip`
  (live logs, 2003-11 to 2004-02). From this session sourceforge.net is refused by the network proxy, and so is web.archive.org.
  The zip, its SHA-256 and "logged zones.txt" were not read. Our released reference world is not in Git and not on this machine
  either. So none of the issue's acceptance items can be ticked yet: zone coverage, level, model and size differences, camp
  sizes, merchants, crafting stations, zone points, and which True 1.65 Prima corrections the logs confirm.
- **What this report does instead** is set up our side of that comparison from what Git holds:
  - the OpenDAoC world data our Setup imports (`opendaoc-db-core`, OpenDAoC-Database `7068aef`);
  - the Setup tool's own Classic 1.65 migrations, applied the same way (`source/tools/DAoCLivingServer.Setup/Program.cs`);
  - our two restore manifests (`bots/autonomous/data/classic165_*.txt`);
  - True 1.65's Prima corrections and Prima spawns (zomboden/OfflineDAoC-True165 `50c407d`, `runtime/true165/`).

  The result is a per-zone table: monsters, names, merchants, crafting stations, corrections. The DataLoggin script can then be
  checked against it.
- **Findings that stand without DataLoggin:**
  1. **The Old Frontier zones and the newer dungeons are thinned the most.** The Setup's "post-legacy" archive removes 83-99%
     of their monster rows in the model: Odin's Gate 97%, Breifine 96%, Yggdra 96%, Hadrian's Wall 95%, Uppland 95%,
     Pennine 93%, Snowdonia 89%, Cruachan 87%. The restore manifests bring back 2-14% of them. These are the first zones to
     compare camp counts with the logs.
  2. **The restores favour Albion.** Of the 2,273 rows the manifests bring back, 1,657 are in Albion, 450 in Midgard and 166 in
     Hibernia. Midgard's and Hibernia's home zones (each main region without its four Old Frontier zones) were thinned as much as
     Albion's or more: 8,967 and 7,044 rows archived, against Albion's 6,867. Albion's home zones got 795 back, Midgard's and
     Hibernia's none. Shannon Estuary lost 82% and Vanern Swamp 77%, with 0 restored.
  3. **The True 1.65 Prima corrections are 206 UPDATE statements touching 10,934 spawns, not "about 1,090".**
     `docs/TRUE165-CONTENT.md` and this issue say "about 1,090". 1,098 of the 10,934 IDs are not in the OpenDAoC data, so
     True 1.65's own rows may be where that number came from. Of the 9,836 found:
     - 5,841 get a lower level and 245 a higher one (1,417 by 10 levels or more);
     - 3,904 change aggression;
     - 813 change speed.
     Every corrected row is a row the Setup keeps. Whether these corrections are in the shipped world is not checked
     (TRUE165-CONTENT.md, "To ship it (not done)").
  4. **Latent Setup break.** `classic165_period_restored_spawn_ids.txt` grew past its 1,290 IDs (2026-09-15) in `dcdf5ef6`
     (2026-10-08), and has held 7,754 IDs plus two `#` comment lines since `7b127a90` (2026-10-16). `DAoCLivingServer.Setup` still expects 1,290
     (`ExpectedPeriodMapRestoredSpawnCount`), and its `LoadRestoredSpawnIds` throws on any line that is not an ID. So a fresh
     Setup world build (`--apply-classic165-spawns` or the default path) stops at the period restore. The game server's
     `AutonomousClassic165RestoredSpawnCatalog` skips those lines and is not affected. The release pipeline builds its world
     from a local install, not from Setup, so no shipped package is known to be affected. Not checked on a PC.
     - The extra 6,464 IDs are 5,836 name-based (v5) and 628 random (v4) UUIDs (the v4 ones all in the goal 9 / Howth-Connla
       section, lines 1292-7589; the 166 quest-camp IDs are all v5), from research scripts that are not in the repository
       (`research/period-data/bestiary/goal9_camp_ids.py`, `.../quests/camp_ids.py`). None of them is in the OpenDAoC data. 1,063 of the 1,098 Prima-corrected
       IDs that are not in the OpenDAoC data (finding 3) are in this extra section, so it is largely True 1.65 / Prima rows. Whether
       our shipped world has them could not be checked here.
  5. **DataLoggin may have a licence after all.** A web search shows the SourceForge project page (sourceforge.net/p/doldataloggin)
     listing "GNU General Public License version 2.0 (GPLv2)". The page itself could not be opened to confirm it, and a project
     licence label may not cover logged game data. Until the owner decides, the issue's rule stands: audit reference only, no
     rows copied.

## Sources and inputs

| Input | Version | What it gives | Reached? |
|---|---|---|---|
| DOL DataLoggin DB v1.2 (`DB1.2.zip`, SourceForge `doldataloggin`) | 2004-03-22 | live spawns, merchants, items, zone points, crafting stations, "logged zones.txt" | **No**: sourceforge.net refused by the proxy (curl and WebFetch) |
| Wayback (Camelot Herald, period guides) | | cross-checks | **No**: web.archive.org refused by the proxy |
| OpenDAoC-Database `opendaoc-db-core/` (`mob.sql`, `zones.sql`, `regions.sql`, `worldobject.sql`, `zonepoint.sql`) | `7068aef` (2026-11-01); `mob.sql` sha256 `403c7755096e3537...` | the world data `DAoCLivingServer.Setup` imports ("Importing the OpenDAoC 1.65 world data") | Yes (git clone) |
| `DAoCLivingServer.Setup/Program.cs` migrations | ours, `cf4c6028` | archive and restore rules | Yes |
| `classic165_restored_spawn_ids.txt` (983), `classic165_period_restored_spawn_ids.txt` (7,754) | ours, `cf4c6028` | restored rows | Yes |
| True 1.65 `runtime/true165/true165-rules.json` (Prima corrections), `add-primaspawns165.json` (978), `add-cornwallprima165.json` (168), `add-periodspawns165.json` (453) | `50c407d`; rules sha256 `bada95c3f081df75...` | corrections and additions, with their own period sources (Prima, CapnBry, Uthgard, Allakhazam) | Yes (git clone) |
| `source/tools/release-world-defaults.json` | ours | keep owners, levels, doors only | Yes; holds no spawns |

### How the model is built

1. Take every `mob.sql` row (97,737).
2. Archive what `ApplyClassic165SpawnProfile` archives. These are rows in a classic region (Expansion 0) with class
   `DOL.GS.GameNPC`, realm 0, region not 249, package not `offline_realm_exchange`, and `LastTimeRowUpdated` from 2021-01-01
   on: **39,795 rows**.
3. `RestoreClassic165ShroudedIslesAndDarknessFallsSpawns` restores nothing in this version, because the archive never takes
   Shrouded Isles or region 249 rows.
4. Restore the 983 IDs and the 1,290 period IDs found in the data: **2,273 rows**, all of them archived rows.
5. That leaves **60,215 rows** in the model world.
6. Zones are worked out from `zones.sql` offsets (×8192). A "monster" is a realm-0 row, a "merchant" a row whose class contains
   `Merchant`, and a "crafting station" a world object named forge, lathe, alchemy table, loom, spinning wheel or tanning tub.
7. Scope: every region with Expansion 0 or 1 that has spawns, without the three housing regions. That is 118 zones, battlegrounds,
   Darkness Falls and the tutorial included.

**Limits of the model:**
- It is not our shipped world. That world was built from an earlier OpenDAoC dump and has had world updates since (Prima content,
  Uthgard drops, realm exchange brokers). The counts are a close stand-in to check the script against. They are not the
  audit.
- Some regions with Expansion 0 may be later than patch 1.65 (Marfach Caverns, Hall of the Corrupt, Dodens Gruva, Caledon). Their
  era was not checked.
- DataLoggin's logs (2003-11 to 2004-02) are after our 1.65 cutoff (2003-10-08) and after Trials of Atlantis came out. A level or
  spawn that differs may be a later live change, not an error of ours.

## Our side, per zone

Columns:
- **Monsters (model):** realm-0 rows left after the Setup migrations.
- **Names:** distinct monster names among them.
- **Archived** and **Restored:** rows the Setup archives, and rows its manifests bring back.
- **Prima corr.:** spawns that a True 1.65 Prima correction touches.
- **Level down / up**, **Aggro**, **Speed:** what those corrections change against the OpenDAoC values.
- **True 1.65 additions:** True 1.65 Prima, Cornwall and period spawns placed in that zone. They are rows True 1.65 adds, and
  whether our world has them is not checked.

| Region | Zone | Monsters (model) | Names | Archived | Restored | Merchants | Craft stations | Prima corr. | Level down / up | Aggro | Speed | True 1.65 additions |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| -1  | -1 (outside zone bounds) | 111 | 22 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 1 Albion | 0 Camelot Hills | 915 | 70 | 1382 | 350 | 43 | 0 | 65 | 4 / 0 | 59 | 2 | 7 |
| 1 Albion | 1 Salisbury Plains | 869 | 67 | 475 | 92 | 9 | 0 | 479 | 0 / 0 | 479 | 33 | 4 |
| 1 Albion | 2 Black Mtns. South | 912 | 80 | 328 | 19 | 42 | 0 | 55 | 25 / 0 | 47 | 0 | 4 |
| 1 Albion | 3 Black Mtns. North | 327 | 51 | 164 | 29 | 23 | 0 | 120 | 0 / 0 | 119 | 7 | 6 |
| 1 Albion | 4 Dartmoor | 249 | 26 | 401 | 12 | 2 | 3 | 21 | 0 / 0 | 21 | 0 | 5 |
| 1 Albion | 6 Cornwall | 476 | 33 | 792 | 61 | 7 | 3 | 63 | 4 / 0 | 60 | 0 | 171 |
| 1 Albion | 7 Llyn Barfog | 348 | 36 | 570 | 16 | 7 | 0 | 31 | 0 / 0 | 31 | 6 | 6 |
| 1 Albion | 8 Campacorentin Forest | 1814 | 98 | 506 | 16 | 34 | 0 | 632 | 0 / 16 | 601 | 15 | 56 |
| 1 Albion | 9 Avalon Marsh | 955 | 62 | 1106 | 115 | 32 | 2 | 334 | 0 / 0 | 307 | 65 | 14 |
| 1 Albion | 10 Lyonesse | 314 | 31 | 1143 | 85 | 0 | 0 | 117 | 3 / 1 | 100 | 61 | 11 |
| 1 Albion | 11 Forest Sauvage | 290 | 25 | 1187 | 167 | 8 | 2 | 5 | 0 / 0 | 5 | 0 | 8 |
| 1 Albion | 12 Snowdonia | 242 | 9 | 1361 | 71 | 1 | 0 | 25 | 0 / 0 | 25 | 0 | 52 |
| 1 Albion | 14 Pennine Mountains | 290 | 21 | 1362 | 188 | 0 | 0 | 11 | 0 / 0 | 9 | 2 | 43 |
| 1 Albion | 15 Hadrian's Wall | 55 | 6 | 519 | 29 | 3 | 2 | 5 | 2 / 2 | 0 | 0 | 26 |
| 10 Camelot City | 26 City of Camelot | 16 | 2 | 12 | 0 | 78 | 11 | 0 | 0 / 0 | 0 | 0 | 0 |
| 20 Stonehenge Barrows | 19 Stonehenge Barrows | 309 | 47 | 117 | 0 | 0 | 0 | 28 | 0 / 0 | 23 | 5 | 2 |
| 21 Tomb of Mithra | 21 Tomb of Mithra | 286 | 43 | 267 | 160 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 2 |
| 22 Keltoi Fogou | 22 Keltoi Fogou | 129 | 16 | 195 | 123 | 0 | 0 | 50 | 32 / 0 | 28 | 0 | 0 |
| 23 Catacombs of Cardova | 23 Catacombs of Cardova | 126 | 28 | 209 | 124 | 0 | 0 | 109 | 0 / 0 | 109 | 0 | 18 |
| 24 Tepok's Mine | 24 Tepok's Mine | 126 | 26 | 71 | 0 | 0 | 0 | 31 | 0 / 0 | 31 | 0 | 1 |
| 27 Tutorial | 27 Constantine's Sound | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 / 0 | 0 | 0 | 0 |
| 27 Tutorial | 28 Grenlock's Sound | 0 | 0 | 101 | 0 | 0 | 2 | 0 | 0 / 0 | 0 | 0 | 0 |
| 27 Tutorial | 29 Lamfhota's Sound | 22 | 22 | 22 | 0 | 16 | 2 | 0 | 0 / 0 | 0 | 0 | 0 |
| 50 Avalon City | 50 Avalon City | 1020 | 82 | 0 | 0 | 0 | 0 | 251 | 241 / 10 | 0 | 0 | 1 |
| 51 Avalon | 51 Isle of Glass | 1029 | 52 | 0 | 0 | 38 | 6 | 69 | 55 / 14 | 0 | 0 | 4 |
| 51 Avalon | 52 Avalon Isle | 952 | 39 | 0 | 0 | 15 | 3 | 134 | 134 / 0 | 0 | 0 | 4 |
| 51 Avalon | 53 Dales of Devwy | 911 | 111 | 0 | 0 | 12 | 3 | 71 | 71 / 0 | 0 | 0 | 34 |
| 51 Avalon | 54 Caldey | 773 | 43 | 0 | 0 | 6 | 0 | 162 | 162 / 0 | 0 | 0 | 2 |
| 51 Avalon | 55 Gwyddneau | 904 | 49 | 0 | 0 | 7 | 0 | 280 | 280 / 0 | 0 | 0 | 48 |
| 51 Avalon | 56 Aldland | 1261 | 46 | 0 | 0 | 13 | 3 | 264 | 264 / 0 | 0 | 0 | 5 |
| 51 Avalon | 57 Inishail Island | 462 | 23 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 60 Caer Sidi | 60 Caer Sidi | 370 | 40 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 61 Krondon | 61 Krondon | 562 | 42 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 62 Crystal Cave | 62 Crystal Cave | 14 | 2 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 100 Midgard | 100 Vale of Mularn | 646 | 52 | 1446 | 0 | 49 | 1 | 161 | 0 / 47 | 62 | 52 | 5 |
| 100 Midgard | 101 East Svealand | 702 | 57 | 599 | 0 | 55 | 0 | 446 | 421 / 0 | 64 | 34 | 3 |
| 100 Midgard | 102 West Svealand | 477 | 73 | 480 | 0 | 20 | 0 | 71 | 21 / 0 | 44 | 33 | 21 |
| 100 Midgard | 103 Gotar | 705 | 75 | 1488 | 0 | 35 | 0 | 547 | 547 / 0 | 63 | 11 | 9 |
| 100 Midgard | 104 Muspelheim | 454 | 50 | 373 | 0 | 0 | 0 | 99 | 39 / 0 | 57 | 12 | 6 |
| 100 Midgard | 105 Myrkwood Forest | 654 | 59 | 1701 | 0 | 21 | 2 | 401 | 401 / 0 | 171 | 1 | 37 |
| 100 Midgard | 106 Skona Ravine | 438 | 55 | 836 | 0 | 0 | 0 | 170 | 138 / 0 | 42 | 0 | 40 |
| 100 Midgard | 107 Vanern Swamp | 288 | 28 | 971 | 0 | 0 | 0 | 90 | 15 / 0 | 84 | 6 | 15 |
| 100 Midgard | 108 Raumarik | 344 | 34 | 681 | 0 | 0 | 0 | 106 | 20 / 0 | 52 | 84 | 2 |
| 100 Midgard | 111 Uppland | 236 | 31 | 1459 | 159 | 6 | 2 | 40 | 7 / 0 | 36 | 0 | 83 |
| 100 Midgard | 112 Yggdra Forest | 187 | 23 | 1432 | 130 | 0 | 0 | 17 | 0 / 0 | 17 | 0 | 63 |
| 100 Midgard | 113 Jamtland Mountains | 247 | 19 | 1164 | 110 | 0 | 0 | 31 | 1 / 0 | 30 | 0 | 125 |
| 100 Midgard | 115 Odin's Gate | 74 | 10 | 862 | 51 | 3 | 2 | 24 | 0 / 0 | 24 | 0 | 8 |
| 100 Midgard | 116 Malmohus | 496 | 40 | 392 | 0 | 0 | 3 | 47 | 6 / 7 | 7 | 26 | 10 |
| 101 Jordheim | 120 Jordheim | 19 | 2 | 9 | 0 | 63 | 7 | 0 | 0 / 0 | 0 | 0 | 0 |
| 125 Spindelhalla | 125 Spindelhalla | 410 | 49 | 23 | 0 | 1 | 0 | 181 | 108 / 0 | 80 | 0 | 0 |
| 126 Vendo Caverns | 126 Vendo Caverns | 94 | 11 | 76 | 0 | 0 | 0 | 2 | 2 / 0 | 0 | 0 | 0 |
| 127 Varulvhamn | 127 Varulvhamn | 482 | 36 | 5 | 0 | 0 | 0 | 9 | 2 / 0 | 9 | 0 | 1 |
| 128 Cursed Tomb | 128 Cursed Tomb | 215 | 27 | 6 | 0 | 0 | 0 | 49 | 10 / 1 | 44 | 0 | 0 |
| 129 Nisse's Lair | 129 Nisse's Lair | 362 | 35 | 6 | 0 | 0 | 0 | 63 | 25 / 12 | 24 | 2 | 0 |
| 150 Trollheim | 150 Trollheim | 1276 | 60 | 0 | 0 | 0 | 0 | 169 | 169 / 0 | 0 | 0 | 2 |
| 151 Aegir | 151 Aegir's Landing | 1010 | 48 | 0 | 0 | 33 | 6 | 0 | 0 / 0 | 0 | 0 | 12 |
| 151 Aegir | 152 Gripklosa Mountains | 1302 | 54 | 0 | 0 | 11 | 2 | 230 | 222 / 6 | 0 | 0 | 2 |
| 151 Aegir | 153 Delling Crater | 1637 | 66 | 0 | 0 | 2 | 0 | 257 | 243 / 12 | 0 | 0 | 6 |
| 151 Aegir | 154 Faraheim | 1513 | 52 | 0 | 0 | 14 | 3 | 246 | 246 / 0 | 0 | 0 | 6 |
| 151 Aegir | 155 Munin Sound | 1618 | 43 | 0 | 0 | 18 | 3 | 173 | 173 / 0 | 0 | 0 | 1 |
| 151 Aegir | 156 Iarnwood | 1040 | 30 | 0 | 0 | 1 | 0 | 0 | 0 / 0 | 0 | 0 | 5 |
| 151 Aegir | 158 Modernagrav | 1139 | 47 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 160 Tuscaran Glacier | 160 Tuscaran Glacier | 389 | 45 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 1 |
| 161 Iarnvidiur's Lair | 161 Iarnvidiur's Lair | 551 | 27 | 0 | 0 | 0 | 0 | 32 | 4 / 28 | 0 | 0 | 0 |
| 163 New Frontiers | 169 Yggdra Forest | 0 | 0 | 0 | 0 | 0 | 3 | 0 | 0 / 0 | 0 | 0 | 0 |
| 163 New Frontiers | 170 Uppland | 0 | 0 | 0 | 0 | 0 | 6 | 0 | 0 / 0 | 0 | 0 | 0 |
| 163 New Frontiers | 173 Cruachan Gorge | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 / 0 | 0 | 0 | 0 |
| 163 New Frontiers | 174 Mount Collory | 0 | 0 | 0 | 0 | 0 | 3 | 0 | 0 / 0 | 0 | 0 | 0 |
| 163 New Frontiers | 175 Snowdonia | 0 | 0 | 0 | 0 | 0 | 5 | 0 | 0 / 0 | 0 | 0 | 0 |
| 163 New Frontiers | 176 Forest Sauvage | 0 | 0 | 0 | 0 | 0 | 3 | 0 | 0 / 0 | 0 | 0 | 0 |
| 165 Cathal Valley | 165 Cathal Valley | 8 | 5 | 298 | 0 | 19 | 3 | 0 | 0 / 0 | 0 | 0 | 0 |
| 180 Fomor City | 180 Fomor | 346 | 71 | 0 | 0 | 0 | 0 | 50 | 50 / 0 | 0 | 0 | 0 |
| 181 HyBrasil | 181 Domnann | 1210 | 40 | 0 | 0 | 36 | 3 | 16 | 16 / 0 | 0 | 0 | 0 |
| 181 HyBrasil | 182 Caillte Garran | 651 | 26 | 0 | 0 | 16 | 3 | 40 | 40 / 0 | 0 | 0 | 44 |
| 181 HyBrasil | 183 Vale of Balor | 653 | 40 | 0 | 0 | 15 | 3 | 128 | 128 / 0 | 0 | 0 | 1 |
| 181 HyBrasil | 184 Cothrom Gorge | 647 | 62 | 0 | 0 | 5 | 0 | 127 | 127 / 0 | 0 | 0 | 1 |
| 181 HyBrasil | 185 World's End | 579 | 38 | 0 | 0 | 5 | 0 | 0 | 0 / 0 | 0 | 0 | 5 |
| 181 HyBrasil | 186 Vigilant Rock | 916 | 47 | 0 | 0 | 15 | 3 | 219 | 219 / 0 | 0 | 0 | 6 |
| 181 HyBrasil | 187 Allta Fearann | 919 | 37 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 190 Tur Suil | 190 Tur Suil | 269 | 19 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 191 Galladoria | 191 Galladoria | 197 | 39 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 200 Hibernia | 200 Lough Derg | 920 | 83 | 1478 | 0 | 37 | 0 | 235 | 119 / 0 | 132 | 3 | 23 |
| 200 Hibernia | 201 Silvermine Mts. | 445 | 37 | 1196 | 0 | 32 | 0 | 238 | 186 / 5 | 166 | 0 | 13 |
| 200 Hibernia | 202 Shannon Estuary | 276 | 29 | 1261 | 0 | 25 | 0 | 147 | 145 / 0 | 40 | 0 | 4 |
| 200 Hibernia | 203 Cliffs of Moher | 514 | 40 | 231 | 0 | 5 | 0 | 85 | 25 / 2 | 55 | 5 | 3 |
| 200 Hibernia | 204 Lough Gur | 696 | 52 | 436 | 0 | 22 | 2 | 386 | 358 / 0 | 126 | 0 | 78 |
| 200 Hibernia | 205 Bog of Cullen | 703 | 55 | 861 | 0 | 18 | 0 | 110 | 84 / 0 | 32 | 0 | 3 |
| 200 Hibernia | 206 Valley of Bri Leith | 359 | 42 | 126 | 0 | 7 | 0 | 28 | 11 / 4 | 4 | 14 | 10 |
| 200 Hibernia | 207 Connacht | 1052 | 75 | 858 | 0 | 30 | 0 | 107 | 36 / 8 | 60 | 8 | 6 |
| 200 Hibernia | 208 Cursed Forest | 327 | 33 | 306 | 0 | 0 | 0 | 9 | 9 / 0 | 0 | 0 | 6 |
| 200 Hibernia | 210 Mount Collory | 139 | 12 | 769 | 44 | 0 | 0 | 7 | 1 / 0 | 6 | 0 | 99 |
| 200 Hibernia | 211 Cruachan Gorge | 178 | 16 | 1061 | 26 | 1 | 2 | 48 | 0 / 0 | 48 | 0 | 201 |
| 200 Hibernia | 212 Breifine | 52 | 8 | 836 | 19 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 44 |
| 200 Hibernia | 214 Emain Macha | 112 | 14 | 673 | 77 | 3 | 2 | 0 | 0 / 0 | 0 | 0 | 8 |
| 200 Hibernia | 216 Sheeroe Hills | 565 | 32 | 291 | 0 | 0 | 3 | 325 | 8 / 0 | 0 | 325 | 10 |
| 201 Tir na Nog | 209 Tir na Nog | 2 | 1 | 30 | 0 | 73 | 8 | 0 | 0 / 0 | 0 | 0 | 0 |
| 220 Coruscating Mine | 220 Coruscating Mine | 384 | 45 | 8 | 0 | 0 | 0 | 129 | 84 / 17 | 68 | 0 | 0 |
| 221 Muire Tomb | 221 Muire Tomb | 85 | 12 | 233 | 0 | 0 | 0 | 1 | 1 / 0 | 0 | 0 | 0 |
| 222 Spraggon Den | 222 Spraggon Den | 298 | 17 | 50 | 0 | 0 | 0 | 32 | 31 / 1 | 0 | 0 | 0 |
| 223 Koalinth Tribal Caverns | 223 Koalinth Caverns | 321 | 21 | 8 | 0 | 0 | 0 | 91 | 39 / 52 | 0 | 0 | 0 |
| 224 Treibh Caillte | 224 Treibh Caillte | 327 | 26 | 9 | 0 | 0 | 0 | 183 | 25 / 0 | 181 | 0 | 0 |
| 234 The Proving Grounds | 234 The Proving Grounds | 94 | 12 | 34 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 235 The Lion's Den | 235 The Lions Den | 78 | 4 | 0 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 236 The Hills of Claret | 236 The Hills of Claret | 62 | 8 | 11 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 237 Killaloe | 237 Killaloe | 72 | 5 | 2 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 238 Thidranki | 238 Thidranki | 75 | 10 | 25 | 0 | 1 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 239 Braemar | 239 Braemar | 95 | 7 | 22 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 241 Molvik | 241 Molvik | 108 | 17 | 14 | 0 | 1 | 0 | 22 | 2 / 0 | 22 | 1 | 0 |
| 242 Leirvik | 254 Leirvik | 65 | 7 | 35 | 0 | 1 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 244 Passage of Conflict | 244 Passage of Conflict | 404 | 26 | 57 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 246 Dodens Gruva | 246 Dodens Gruva | 6 | 6 | 601 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 248 Summoner's Hall | 248 Summoner's Hall | 112 | 8 | 199 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 249 Darkness Falls | 249 Darkness Falls | 2460 | 176 | 0 | 0 | 27 | 0 | 0 | 0 / 0 | 0 | 0 | 7 |
| 250 Caledon | 250 Caledon | 50 | 11 | 326 | 0 | 1 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 252 Thidranki | 252 Thidranki | 63 | 3 | 313 | 0 | 2 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 276 Marfach Caverns | 276 Marfach Cavern | 4 | 4 | 440 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 0 |
| 277 Hall of the Corrupt | 277 Hall of the Corrupt | 10 | 10 | 398 | 0 | 0 | 0 | 0 | 0 / 0 | 0 | 0 | 30 |

### Realm totals (model)

| Realm | Monsters | Archived | Restored | Merchants | Craft stations | Prima corr. | Level down / up | Aggro | Speed | True 1.65 additions |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| Albion (incl. SI and dungeons) | 17,328 | 12,290 | 1,657 | 396 | 44 | 3,412 | 1,277 / 43 | 2,054 | 196 | 534 |
| Midgard | 19,005 | 14,009 | 450 | 332 | 31 | 3,661 | 2,820 / 113 | 910 | 261 | 463 |
| Hibernia | 14,142 | 10,721 | 166 | 345 | 29 | 2,741 | 1,742 / 89 | 918 | 355 | 565 |
| Shared (battlegrounds, DF, other) | 3,877 | 2,775 | 0 | 52 | 27 | 22 | 2 / 0 | 22 | 1 | 37 |

The zone points in scope number 166 rows, counted by source region. A zone point can only be compared with the logs.

### The zones thinned most (model; 100 or more monster rows before the archive)

| Zone | Region | Monster rows before | Archived | Restored | Left | Archived share |
|---|---|--:|--:|--:|--:|--:|
| 28 Grenlock's Sound | 27 | 101 | 101 | 0 | 0 | 100% |
| 276 Marfach Cavern | 276 | 444 | 440 | 0 | 4 | 99% |
| 23 Catacombs of Cardova | 23 | 211 | 209 | 124 | 126 | 99% |
| 246 Dodens Gruva | 246 | 607 | 601 | 0 | 6 | 99% |
| 277 Hall of the Corrupt | 277 | 408 | 398 | 0 | 10 | 98% |
| 115 Odin's Gate | 100 | 885 | 862 | 51 | 74 | 97% |
| 165 Cathal Valley | 165 | 306 | 298 | 0 | 8 | 97% |
| 22 Keltoi Fogou | 22 | 201 | 195 | 123 | 129 | 97% |
| 212 Breifine | 200 | 869 | 836 | 19 | 52 | 96% |
| 112 Yggdra Forest | 100 | 1489 | 1432 | 130 | 187 | 96% |
| 15 Hadrian's Wall | 1 | 545 | 519 | 29 | 55 | 95% |
| 214 Emain Macha | 200 | 708 | 673 | 77 | 112 | 95% |
| 111 Uppland | 100 | 1536 | 1459 | 159 | 236 | 95% |
| 14 Pennine Mountains | 1 | 1464 | 1362 | 188 | 290 | 93% |
| 11 Forest Sauvage | 1 | 1310 | 1187 | 167 | 290 | 91% |
| 113 Jamtland Mountains | 100 | 1301 | 1164 | 110 | 247 | 89% |
| 210 Mount Collory | 200 | 864 | 769 | 44 | 139 | 89% |
| 12 Snowdonia | 1 | 1532 | 1361 | 71 | 242 | 89% |
| 211 Cruachan Gorge | 200 | 1213 | 1061 | 26 | 178 | 87% |
| 250 Caledon | 250 | 376 | 326 | 0 | 50 | 87% |
| 10 Lyonesse | 1 | 1372 | 1143 | 85 | 314 | 83% |
| 252 Thidranki | 252 | 376 | 313 | 0 | 63 | 83% |
| 202 Shannon Estuary | 200 | 1537 | 1261 | 0 | 276 | 82% |
| 107 Vanern Swamp | 100 | 1259 | 971 | 0 | 288 | 77% |
| 221 Muire Tomb | 221 | 318 | 233 | 0 | 85 | 73% |

### True 1.65 Prima corrections against the OpenDAoC values

| What | Count |
|---|--:|
| UPDATE statements (`reason` starting "True 1.65 Prima monster:") | 206 |
| Spawn IDs they name | 10,934 |
| ... not in the OpenDAoC data (True 1.65's own rows, or rows since removed) | 1,098 |
| ... in it; all in scope and all kept by the Setup | 9,836 |
| Level lowered: by 1 / by 2 to 4 / by 5 to 9 / by 10 or more | 3 / 3,289 / 1,132 / 1,417 |
| Level raised | 245 |
| Aggression changed (most often 200 to 5, 0 to 90, 0 to 50, 0 to 99) | 3,904 |
| Speed changed (median +70) | 813 |
| IDs that are also in our restore manifests | 1,482 (145 in the 983 list, 1,337 in the period list) |

Which of these the logs **confirm or contradict** is the issue's cross-check, and it waits for DataLoggin.

## What could not be checked, and how to finish it

1. **Download DataLoggin and record its SHA-256.** This needs sourceforge.net reachable, for example on the owner's PC, or the
   domain allowed for a cloud session. Keep it outside the repository.
2. **Write `tools/audit/dataloggin_audit.py` as the issue specifies.** It should:
   - read `Mob.xml`, `Merchant.xml`, `WorldObject.xml`, the zone points and "logged zones.txt" from a path outside the
     repository;
   - read a copy of the release's clean reference world, or the Setup model above;
   - match spawns by name and region, and camps by name within a zone cell.
   For the tests, test the matcher on made-up rows. The model above is the first real input to check it against: its per-zone
   counts should come out the same.
3. **Report shape:** counts and our Mob_IDs only, one table per logged zone, with these columns:
   - level, model and size differences;
   - camps with at least twice, or at most half, the logged count;
   - missing merchants and crafting stations;
   - zone points that differ;
   - the Prima corrections the logs confirm or contradict.
4. **CI guard:** a check that no DataLoggin-shaped XML or row is tracked (for example, a grep for `<Mob ` or the DataLoggin header in
   `tools/check.py`).
5. **Follow-up issues to open,** each with its own period source:
   - the Setup period-manifest break (finding 4);
   - the Midgard and Hibernia restores, if the logs show thinner camps than live (finding 2);
   - the "about 1,090" figure in `docs/TRUE165-CONTENT.md` (finding 3);
   - the owner's view on the DataLoggin licence (finding 5).

   This report opens none of them.

## Method notes

- Every figure here comes from automated reads of the files above. Nothing was seen in game, and nothing came from one of the owner's
  PC logs.
- The scripts that made the numbers were scratch work for this report and are not committed. Their method is above, step by step,
  and can be redone in an afternoon.
