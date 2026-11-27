# DOL starter quests against our world and the 1.65 period (#578)

Research only, written 2026-11-26 by the Account #2 session for issue #578 (harvest item D-F3). **No quest row, script
or world file is changed.** No quest text is copied here: this file holds names, IDs, dates and the method.

## Summary

- **Nothing in DOL's quest scripts that our world lacks passes the 1.65 gate.** Of the 96 scripts the issue names, 23
  are quests we already have (11 as world rows, 12 as C# scripts), 72 are later than 1.65: the 1.69 quests
  of April 2004, the Catacombs new-user chains and mini quests (1.72-1.73, October-December 2004), 1.75 and 1.76 quests
  of 2005, and DOL's 2007 rewrites of the later live starter areas; and one (Arrows for Yetta Fletcher) has no period
  source at all.
- **Why our world has none of them:** the base game's 448 classic quests were built from Allakhazam pages archived
  August-December 2004 (387 of the 448 guides are from 2004-10 or earlier) and then vetted to the 1.65 set. The 1.69
  quests were on Allakhazam by then and are still not in the world, so the base project most likely left them out on
  purpose (an inference: its own vetting notes aren't in Git). Every
  1.65 quest DOL scripts is in that set already.
- **So the port the issue plans would add post-1.65 content.** The issue's own rule ("Exclude ... post-1.65 content ...
  Check each against a period source") and the owner's 2026-10-09 ruling on quests with no period source
  (`source/new-client/pipeline/build_quests.py`, `REMOVED_QUESTS`) both say to leave them out. Whether to take any of
  them anyway is an owner decision, asked on #578 (`needs-owner`); nothing is built until then.

## What was compared

- **DOL:** Dawn-of-Light/DOLSharp `master` at `154e150993e078408f87c3e681a62576737d2892`,
  `GameServerScripts/quests/{Albion,Midgard,Hibernia}/` (96 `.cs` files: 56 Albion, 13 Hibernia, 27 Midgard). Titles
  read from each script's `questTitle`, or from our `language/EN` files for the `LanguageMgr` ones; author and date
  from each file's header.
- **Our world:** the released 0.36b world (`runtime/data/opendaoc.sqlite3.db` of `OfflineDAoC-v0.36b.zip`, the base
  every install starts from; SHA-256 begins `d7013934b8dfc3f0`), read by HTTP range from Living Server's copy of the
  base files and opened read-only. Its `DataQuest` table has 1,540 rows, 676 distinct names: 1,302 class rows of the
  448 classic quests (`ClassicQuestStep`) and 238 others (kill tasks, XP items). `classic-quest-guides.json` beside it
  holds the 448 Allakhazam walkthroughs with their archive dates. The 0.37 world update was not checked.
- **Our server's C# quests:** `source/server/GameServer/scripts/quests/` (the eleven level 50 epics and Wolf Pelt Cloak
  are there; OpenDAoC-Core has none of the others).
- **Names** were matched without case, punctuation or the "(Mid)/(Hib)" suffixes, then by hand ("Amora's Aid for
  Mistyc" is our "Amora's Aid", "Rinda's Lost Key" our "Rinda's Lost Keys").

## Period sources

- **1.69 (2004-04-14):** Allakhazam's "1.69 Has Landed" lists as new quests Ydenia's Crush, Andrew's Skins, Argus's
  Arrows and Godeleva's Need (Albion), Humberton's Small Problem, Nyra's Package and Anra's Magic.
  <https://camelot.allakhazam.com/story.html?story=3355>
- **1.72c (2004-10) and 1.73 Catacombs (2004-12):** the new-user quest givers Master Frederick (Cotswold guard tower),
  Dalikor (Mularn) and Addrir (Mag Mell), and "over 400 new quests which are referred to as Mini Quests", coin and XP
  only. The 1.73 notes also fix Traitor in Cotswold, Traitor in Mularn and Traitor in Mag Mell.
  <https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73>; Allakhazam's 1.72C notes ("1.72C of Fallen
  Barriers", 2004-10-05: the new base class trainers send players to the new-user quest givers)
  <https://wow.allakhazam.com/news/tags/News/2292>; Allakhazam's "Servers Up, Here Are the Notes" (2005-04-06, the
  Dalikor chain revamped) <https://camelot.allakhazam.com/news/tags/DarkAgeOfCamelot/321>.
- **Catacombs world notes (2005-01-07):** Bones to Blades as a "level 2 to 5 mini quest". **Unlinked:** seen only in a
  web-search snippet of a patch archive (losojos PatchArchive, "Catacombs"); the page itself wasn't fetched. DOL's
  Bones to Blades header is dated 2010, so it doesn't date the quest.
- **1.75 (2005-04):** Mauler Invasion (level 1-4 mini quest) and The Birthday Gift. Allakhazam's "1.75 Begins"
  <https://camelot.allakhazam.com/story.html?story=5302>.
- **Trevian's Best Friend:** a 1.71c note says it "has been re-enabled" (Allakhazam's copy
  <https://everquest.allakhazam.com/news/tags/News/2329>); when it was first added wasn't found, so its row is marked
  "inference".
- **1.65 (2003-10-08):** no starter quest added. <https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65>
- **Allakhazam's quest list** gives each quest's type ("Mini Quest" or "Regular"); the mini quests are Catacombs content.
  <https://camelot.allakhazam.com/quests.html?realm=Albion>, `?realm=midgard`. Allakhazam's quest IDs rise with the date
  added (Wolf Pelt Cloak 3, Heart of Sephucoth 5, the 1.69 quests 2027-2051, Lawrence's Oil 2448 added 2005-05-18), which
  is the evidence for the three rows marked "inference".
- **Not reached from this session:** the Wayback Machine and sourceforge (refused by the network proxy), and
  Allakhazam's per-quest pages by constructed link. Nothing below depends on them.

## The table

"have" = in our world or server today. "post-1.65" = a period source dates it after 1.65. "inference" = dated from
Allakhazam's ID order and the base world's 1.65 set, not from a patch note.

| DOL script | Quest | Status | Evidence |
|---|---|---|---|
| `Albion/AFewRepairs.cs` | A Few Repairs | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/AMessageToTheManes.cs` | A Message to the Manes | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/ANewHeroesWelcome.cs` | A New Heroes Welcome | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/AfterTheAccident.cs` | After The Accident | post-1.65 (inference) | no author or date in the DOL header; same LanguageMgr style and starter chain as the 2007 k109 scripts; NPCs absent from our world |
| `Albion/AgainstTheGrain.cs` | Against the Grain | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/Aiding_Guard_Alakyrr.cs` | Aiding Guard Alakyrr | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/AndrewsSkins.cs` | Andrew's Skins | post-1.65: 1.69 | same 1.69 notes; Allakhazam type Mini Quest |
| `Albion/ArgussArrows.cs` | Argus's Arrows | post-1.65: 1.69 | same 1.69 notes |
| `Albion/ArleighsAssistant.cs` | Arleigh's Assistant | post-1.65: 1.76 | DOL header: "as per patch 1.76" |
| `Albion/ArrowsForYettaFletcher.cs` | Arrows for Yetta Fletcher | no period source found | no Allakhazam page; Yetta Fletcher gives only "Unused Arrow Heads XP Item" in our world |
| `Albion/BaseFrederickQuest.cs` | (base class) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/BeginningOfWar.cs` | Beginning of War | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/BoarStew.cs` | Boar Stew | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/BoulderlingBalm.cs` | Boulderling Balm | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/BreakingTheBandits.cs` | Breaking the Bandits | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/BuildingABetterBow.cs` | Building a Better Bow | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/Cellar_Infestation.cs` | Cellar Infestation | have | world row 20008 (Mistress Laws) |
| `Albion/CemmethsOrders.cs` | Cemmeth's Orders | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/ChildsPlay.cs` | Child's Play | post-1.65 | DOL 2007 ("work like live"); Demons' Breach |
| `Albion/CityOfCamelot.cs` | City of Camelot | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/ClericMulgrut.cs` | Cleric Mulgrut | have | world row 20207 (Hugh Gallen) |
| `Albion/Collection.cs` | Collection | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/Culmination.cs` | Culmination | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/Disenchanted.cs` | Disenchanted | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/DredgeUpAPledge.cs` | Dredge Up a Pledge | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/Frontiers.cs` | Frontiers | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/GodelevasNeed.cs` | Godeleva's Need | post-1.65: 1.69 | same 1.69 notes |
| `Albion/GreenerPastures.cs` | Greener Pastures | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/GreetingsArmsman.cs` | Alb.GreetingsArmsman.QuestTitle | post-1.65 (inference) | no author or date in the DOL header; same LanguageMgr style and starter chain as the 2007 k109 scripts; NPCs absent from our world |
| `Albion/GreetingsPaladin.cs` | Alb.GreetingsPaladin.QuestTitle | post-1.65 (inference) | no author or date in the DOL header; same LanguageMgr style and starter chain as the 2007 k109 scripts; NPCs absent from our world |
| `Albion/HalfOgreAllMan.cs` | Half Ogre, All Man | post-1.65 (inference) | Allakhazam quest 2184, between the 1.69 batch (2027-2051) and the 2005 mini quests; not in the 1.65 set |
| `Albion/HeartOfSephucoth.cs` | Heart of Sephucoth | have | world row 21330 (Elvar Ironhand); Allakhazam quest 5 |
| `Albion/HuntForArachneida.cs` | The Hunt for Arachneida | have | world rows 20607+ (Kealan) |
| `Albion/HuntForSlith.cs` | Hunt for Slith | have | world row 20021 (Commander Burcrif) |
| `Albion/ImportantDelivery.cs` | Important Delivery | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/IreFairyIre.cs` | Ire Fairy Ire | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/LawrencesOil.cs` | Lawrence's Oil | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/MarlinsSalesPitch.cs` | Marlin's Sales Pitch | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/MovementAndInteraction.cs` | Movement and Interaction | post-1.65 | tutorial chain; excluded by the issue |
| `Albion/NoHopeForTheHopeful.cs` | No Hope For The Hopeful | post-1.65 (inference) | no author or date in the DOL header; same LanguageMgr style and starter chain as the 2007 k109 scripts; NPCs absent from our world |
| `Albion/Nuisances.cs` | Nuisances | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/RecruitingNothingButTrouble.cs` | Recruiting Nothing But Trouble | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/RevengeTheOtherWhiteMeat.cs` | Revenge, the Other White Meat | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/ShakenSquire.cs` | Shaken Squire | post-1.65: 1.73 Catacombs mini quest | Allakhazam type "Mini Quest"; mini quests came with Catacombs (1.73 notes) |
| `Albion/ToReachTheBreach.cs` | To Reach the Breach | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/TombWithAView.cs` | Tomb With A View | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/TraitorInCotswold.cs` | Traitor in Cotswold | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Albion/TreviansBestFriend.cs` | Trevian's best Friend | post-1.65 (inference) | not in the base world's 1.65 set; 1.71c notes: "re-enabled" ([Allakhazam](https://everquest.allakhazam.com/news/tags/News/2329)); first date not found |
| `Albion/WhenBloodSpeaks.cs` | When Blood Speaks | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/WhenGoodBrowniesGoBad.cs` | When Good Brownies Go Bad | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Albion/WolfPeltCloak.cs` | Wolf Pelt Cloak | have | C# script in our server (`scripts/quests/Albion/WolfPeltCloak.cs`); Allakhazam quest 3 |
| `Albion/YdeniasCrush.cs` | Ydenia's Crush | post-1.65: 1.69 | Allakhazam "1.69 Has Landed" (2004-04-14): new quest |
| `Albion/epic/Academy50.cs` | Symbol of the Broken | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Albion/epic/Church50.cs` | Passage to Eternity | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Albion/epic/Defenders50.cs` | Feast of the Decadent | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Albion/epic/Shadows50.cs` | Feast of the Decadent | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Hibernia/BaseAddrirQuest.cs` | (base class) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Hibernia/BonesToBlades.cs` | Bones to Blades | post-1.65: 1.73 Catacombs mini quest | Catacombs world notes (2005-01-07), "level 2 to 5 mini quest", seen only in a search snippet of a patch archive (unlinked; page not fetched); DOL's own header is 2010 |
| `Hibernia/ChildsPlay.cs` | Child's Play | post-1.65 | DOL 2007 ("work like live"); Demons' Breach |
| `Hibernia/CityOfTirnaNog.cs` | City of Tir na Nog | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Hibernia/ImportantDelivery.cs` | Important Delivery (Hib) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Hibernia/MagicalBacklash.cs` | Magical Backlash | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Hibernia/Nuisances.cs` | Nuisances (Hib) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Hibernia/SearchForKnowledge.cs` | Search For Knowledge | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Hibernia/ToReachTheBreach.cs` | To Reach the Breach | post-1.65 | DOL 2007 "mimics live US servers" (k109); NPCs absent from our world |
| `Hibernia/TraitorInMagMell.cs` | Traitor in Mag Mell | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Hibernia/epic/Essence50.cs` | The Moonstone Twin | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Hibernia/epic/Focus50.cs` | Unnatural Powers | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Hibernia/epic/Harmony50.cs` | The Horn Twin | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Midgard/A_Beary_Bad_Problem.cs` | A Beary Bad Problem | post-1.65 (inference) | not in the 1.65 set; its Silver Ring of Health (Mid) pairs with the 1.69 Albion ring quest |
| `Midgard/Amoras_Aid.cs` | Amora's Aid for Mistyc | have | world rows "Amora's Aid" |
| `Midgard/Bad_Food.cs` | Bad Food | have | world row 20095 (Pedra) |
| `Midgard/BaseDalikorQuest.cs` | (base class) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Bear Skins.cs` | Bear Skins | post-1.65 (inference) | Allakhazam quest 2608 (after Lawrence's Oil, added 2005); not in the 1.65 set |
| `Midgard/BeginningOfWar.cs` | Beginning of War (Mid) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Childs_Play.cs` | Child's Play | post-1.65 | DOL 2007 ("work like live"); Demons' Breach |
| `Midgard/CityOfJordheim.cs` | City of Jordheim | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Collection.cs` | Collection (Mid) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Culmination.cs` | Culmination (Mid) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Essence_of_Life.cs` | Essence of Life | post-1.65: 1.73 Catacombs mini quest | Allakhazam marks it Catacombs, type Mini Quest |
| `Midgard/Frontiers.cs` | Frontiers (Mid) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/ImportantDelivery.cs` | Important Delivery (Mid) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Learn_The_Hunt.cs` | Learn the hunt | have | world row 20096 (Aegan) |
| `Midgard/Mauler_Invasion.cs` | Mauler Invasion | post-1.65: 1.75 | [Allakhazam "1.75 Begins"](https://camelot.allakhazam.com/story.html?story=5302) (level 1-4 mini quest); excluded by the issue |
| `Midgard/Mead_Run.cs` | Mead Run | post-1.65: 1.73 Catacombs mini quest | Allakhazam marks it Catacombs, type Mini Quest |
| `Midgard/Nuisances.cs` | Nuisances (Mid) | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Rindas_Lost_Key.cs` | Rinda's Lost Key | have | world rows "Rinda's Lost Keys" |
| `Midgard/StolenEggs.cs` | Stolen Eggs | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Sveabone_Hilt_Sword.cs` | Sveabone Hilt Sword | have | world row 20098 (Gridash) |
| `Midgard/The_Birthday_Gift.cs` | The Birthday Gift | post-1.65: 1.75 | [Allakhazam "1.75 Begins"](https://camelot.allakhazam.com/story.html?story=5302) |
| `Midgard/TraitorInMularn.cs` | Traitor in Mularn | post-1.65: 1.72-1.73 new-user chain | 1.72C notes ([Allakhazam](https://wow.allakhazam.com/news/tags/News/2292)) and [1.73 notes](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.73): Master Frederick, Dalikor, Addrir new-user quests |
| `Midgard/Trial_of_Strength.cs` | Trial of Strength | have | world row 20100 (Torold Sterkkriger) |
| `Midgard/epic/MidgardRogue50.cs` | War Concluded | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Midgard/epic/Mystic50.cs` | Saving the Clan | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Midgard/epic/Seer50.cs` | The Desire of a God | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |
| `Midgard/epic/Viking50.cs` | An End to the Daggers | have | epic: C# script in our server (`scripts/quests/*/epic/`); excluded by the issue |

## Bots

- Quests are run by **real players** only. Companion bots and autonomous gamebots don't take or run `DataQuest` rows.
- **But the rows steer autonomous gamebots.** `AutonomousDungeonBossGoals` loads every `DataQuest` row's kill targets
  (`TargetName`) and makes a named dungeon boss a party goal when it is the target of a real quest at the party's
  level: a gamebot party camping in that dungeon also goes for that boss. So **adding quest rows (option 2 or 3 on
  #578) would change gamebot boss goals**: any new kill target that is a dungeon named monster becomes a bot party goal
  at the quest's minimum level. Starter quests mostly target overland monsters, but each new row's targets would have
  to be checked, and the change tested (`UT_` coverage of `AutonomousDungeonBossGoals`) before it ships.
- The 1.2 quest harness credits a gamebot's kill to the players of its group, which is unchanged.
- This PR changes no row, so no bot behaviour changes here.

## If the owner wants some of them anyway

The cheapest set with the strongest dates is the four 1.69 Albion quests (Ydenia's Crush, Andrew's Skins, Argus's
Arrows, Godeleva's Need): their givers stand in our world except Godeleva Dowden. Each would be a `DataQuest` row set
(the format `QuestWalker` walks), shipped by the server at start with its own rows only and a rollback (the
`LoreBooks` pattern), walked start to finish by `UT_QuestHarness`. Their item rewards (Silver Ring of Health,
Dull Black Gem, Reed Bracer) would be new item templates, so real loot stays real. New rows' kill targets also feed the
gamebots' dungeon boss goals (see Bots), so each one is checked for that. The Catacombs mini quests pay coin and
XP only and the new-user chains hand out the Recruit's set; both are further from the era.
