"""Era builds 1.2: the modern base, tuned back to 1.65 (owner 2026-11-17 19:43: "use more recent discussions and templates
as a base and then tune back towards what you directionally know about 1.65").

The research is scratchpad PERIOD-BUILDS-RESEARCH.md of 2026-11-17. Its base is the Uthgard forum. Uthgard is a patch
1.65 freeshard (Classic + Shrouded Isles, no ToA, CL or ML) with this server's spec budget, and its best autotrain case
is this server's flat 77 points a line. Some period guides are reposted there (the ZAM Warden and Ranger guides, the June
2003 Mentalist Team Leader report, the Champion TL guide). Only links, dates and the few words that carry each point are
kept here.

RESEARCH[(class, draft label)] holds, for a build of the draft:
  specs   the template a level 50 trains (None: the posted points, trained by make_era_builds.fully_trained)
  cite    [(url, date, what it says)]: the sources for these points
  rest    the line that takes points the template leaves (its "rest in X")
  conf    the research's confidence for the class: high / medium / low
  note    why, where the build changed or where the research and the class review (REVIEW-ERA-PERIOD-1117) differ
  weight, use, label, family: changes the research gives a reason for
NEW_BUILDS[class] holds builds the research adds: a template no draft build has, with a source.
Every class's unlisted autotrain lines are trained to their free 12 by make_era_builds (research finding 1).
"""

U = "https://www.uthgard.net/forum/viewtopic.php?"

RESEARCH = {
    # ---- Albion
    ("Armsman", "Polearm/Shield hybrid"): dict(conf="high", rest="Parry", cite=[
        (U + "f=99&t=46589", "2018-05", '"standard spec": 50 Polearm, 42 Shields, 39 Slash, 12 Thrust, 13 Parry'),
        (U + "f=99&t=39654", "2017", '"42 shield, 50 polearm, 39 slash and the rest into parry"')]),
    ("Armsman", "Polearm/Shield hybrid (Thrust)"): dict(conf="high", rest="Parry", cite=[
        (U + "f=99&t=46589", "2018-05", "the standard spec with Thrust as the weapon, Slash on autotrain")]),
    ("Armsman", "Sword and board"): dict(conf="high", rest="Parry", cite=[
        (U + "t=27062", "2012", '"50 Shield, 50 Pole, 39 ... rest Parry", the 50/50 shield tank')]),
    ("Armsman", "Two-handed/Shield hybrid"): dict(conf="high", rest="Slash", cite=[]),
    ("Paladin", "Chants/Shield/Slash"): dict(conf="high", rest="Parry", cite=[
        (U + "f=107&t=40117", "2017", '"48 chants 39 slash 42 shield ... 15 parry also without AT" (23 with it)')]),
    ("Paladin", "Chants/Shield/Parry"): dict(conf="high", rest="Slash", weight=0.30, cite=[
        (U + "f=60&t=30610", "2013", '"46 Chant 42 Shield 30 Parry 34 slash"')]),
    ("Paladin", "Full shield"): dict(conf="high", specs={"Shields": 50, "Chants": 48, "Slash": 29, "Parry": 21},
        rest="Parry", cite=[(U + "f=60&t=30610", "2013", '"50 shield 48 chants 29 slash", posted twice')]),
    ("Paladin", "Two-handed chanter"): dict(conf="medium",
        specs={"Two Handed": 50, "Chants": 46, "Parry": 36, "Shields": 7, "Slash": 12}, rest="Parry",
        label="Two-handed chanter (full 2H)", cite=[
            (U + "f=107&t=46177", "2018", '"50 2h and just enough points in shield to get ... Guard and Engage"')],
        note="the fill had put 23 points into Parry (43); no source has a 43-Parry two-hander"),
    ("Mercenary", "Thrust dual wield"): dict(conf="medium", rest="Parry", weight=0.15, cite=[
        (U + "f=60&t=45364", "2017", '"50 thrust 50 dw rest parry"')],
        note="weight 0.45 -> 0.15: 0.30 to the new DW/Shield build"),
    ("Mercenary", "Slash dual wield"): dict(conf="medium", rest="Parry", cite=[
        (U + "f=60&t=45364", "2017", '"50 thrust 50 dw rest parry", the same with Slash')]),
    ("Mercenary", "Defensive parry"): dict(conf="medium", rest="Slash", cite=[],
        note="no modern source; one period post (kept at its weight)"),
    ("Reaver", "Flex/Shield/Soulrending"): dict(conf="high", rest="Parry", cite=[
        (U + "f=60&t=20071", "2010", '"The one and only reaver spec is 50 flex / 42 shield / 36 soulrending / rest parry"'),
        (U + "f=60&t=23586", "2011", "50/42/36/15 and 50/42/41/6")]),
    ("Reaver", "Soulrending focus"): dict(conf="high", rest="Parry", cite=[
        (U + "f=60&t=20071", "2010", '"44 flex ... 45 soul ... 42 shield"')]),
    ("Cleric", "Enhancement/Rejuv (spec buffs)"): dict(conf="high", cite=[
        (U + "f=60&t=29332", "2013", '"42 enhance ... 33 rejuv ... 7 smite"; "40enhance 36rejuv"')]),
    ("Cleric", "Rejuvenation healer"): dict(conf="high", cite=[
        (U + "f=60&t=29332", "2013", '"41-40rej/35-36enh"; the period comment "45+12 rejuv, 28+7enh" backs ours')]),
    ("Cleric", "Smite"): dict(conf="high", cite=[
        (U + "f=60&t=42177", "2017", '"46 smite / 27 enhance / 8 rejuv"; the period comment says 48/23/7')]),
    ("Friar", "Enhance/Staff"): dict(conf="high", cite=[
        (U + "t=46728", "2018", '"44 staff, 46 enh, 15 rejuv, 8 parry"; "drop enh to 45 and boost parry to 12"')]),
    ("Friar", "Enhance/Staff/Rejuv"): dict(conf="high", cite=[
        (U + "f=60&t=19734", "2010", '"39 Staff 47 Enhancement 18 Rejuvenation 16 Parry"')]),
    ("Minstrel", "Instruments/Stealth/Slash"): dict(conf="high", specs={"Instruments": 50, "Stealth": 33, "Slash": 30},
        cite=[(U + "f=105&t=38323", "2016", '"50 Inst / 29 Slash / 33 Stealth"'),
              (U + "t=19081", "2010", '"29 Slash (30 with AT) 31 Stealth (33 with AT)"')],
        note="the trim to Stealth 33 now rests on Uthgard, not on a 2004 Infiltrator quote"),
    ("Minstrel", "Instruments/Slash (no stealth)"): dict(conf="high",
        specs={"Instruments": 50, "Slash": 37, "Stealth": 25}, label="Instruments/Slash (solo)", cite=[
            (U + "f=105&t=38323", "2016", '"50 Inst / 37 Slash / 25 Stealth (minimum for wall climb)"; '
                                          '"50 Instruments is a must"')],
        note='the draft label said "no stealth" with Stealth 24-28; the solo template keeps 25 for climbing'),
    ("Minstrel", "Instruments/Thrust"): dict(conf="high", specs={"Instruments": 50, "Thrust": 44, "Stealth": 8},
        label="Instruments/Thrust (8v8)", cite=[
            (U + "f=105&t=38323", "2016", '"8v8 spec = 50 Instruments / 44 Thrust / 8 Stealth"')]),
    ("Infiltrator", "Thrust/CS assassin"): dict(conf="high",
        specs={"Thrust": 50, "Critical Strike": 39, "Stealth": 36, "Envenom": 36, "Dual Wield": 27}, rest="Dual Wield",
        cite=[(U + "f=103&t=41180", "2017", '"50 thrust 39 CS 36 stealth 36 envenom 27 DW"'),
              (U + "f=60&t=25840", "2012", "50/39/35/35/27; \"35 + 11 (items) + 4 (from rr5) = 50\" stealth")],
        note="the class review proposed Stealth 48 to tell it from the 5-spec; every Uthgard 1.65 spec stops Stealth "
             "at 33-39, so the research's CS 39 / DW 27 template is used: it differs from the 5-spec in CS and DW"),
    ("Infiltrator", "Thrust/DW \"5-spec\""): dict(conf="high", rest="Dual Wield", cite=[
        (U + "f=103&t=37321", "2016", '"50 thrust / 44 cs" with 33 stealth; "Higher RR drop Env/Stealth for DW"')]),
    ("Infiltrator", "Slash/CS assassin"): dict(conf="high",
        specs={"Slash": 50, "Critical Strike": 39, "Stealth": 36, "Envenom": 36, "Dual Wield": 27}, rest="Dual Wield",
        cite=[(U + "f=103&t=41180", "2017", "the thrust assassin template with Slash (Allakhazam 2004: \"briton slash "
                                            "infs\")")],
        note="as Thrust/CS assassin"),
    ("Scout", "Longbow archer"): dict(conf="high", rest="Thrust", cite=[
        (U + "f=60&t=16466", "2010", '"42 shield 50 bow 35 stealth"'),
        (U + "t=19921", "2010", '"50 bow, 42 shield, 35 stealth, rest weapon"')]),
    ("Scout", "Longbow/Slash"): dict(conf="high", rest="Slash", cite=[
        (U + "f=60&t=16466", "2010", '"45 Bow 42 Shield 36 Stealth"')]),
    ("Scout", "Melee scout"): dict(conf="medium",
        specs={"Thrust": 50, "Shields": 42, "Stealth": 35, "Longbows": 21}, rest="Longbows", cite=[
            (U + "t=19921", "2010", '"50 weapon, 42 shield, 35 stealth, rest bow"; "leave bow on autotrain"')],
        note="the fill had left Longbows at 1 (a Scout that can't shoot); the class review proposed Longbows 36 "
             "Stealth 20, the research's source has Stealth 35 and the rest in the bow"),
    ("Theurgist", "Earth (PBT)"): dict(conf="medium", specs={"Earth Magic": 48, "Cold Magic": 24, "Wind Magic": 6},
        weight=0.4, cite=[
            (U + "f=111&t=36641", "2016", '"48earth rest ice or wind that\'s the most common 8v8 spec"'),
            (U + "f=60&t=30972", "2013", '"go 48 Earth rest whatever for speed6 earthlords and red pbt"')],
        note="the trim had cut Wind to 4 (no stop); the class review proposed the base Earth 50 Cold 17 Wind 11, "
             "the research's 1.65 group spec is Earth 48 with the rest in Ice"),
    ("Theurgist", "Ice/Earth"): dict(conf="medium", weight=0.3, use="both", cite=[
        (U + "f=111&t=36641", "2016", '"41 Ice / 35 Earth is probably the most common for 8-man RVR"')]),
    ("Theurgist", "Air"): dict(conf="medium", weight=0.3, cite=[
        (U + "f=111&t=36641", "2016", '"45 Air / 29 Earth"'),
        (U + "f=60&t=30972", "2013", '"45 air is a solo spec. It doesnt work in grps"')],
        note="weight 0.5 -> 0.3: Air is the solo spec on a 1.65 server"),
    ("Wizard", "Fire"): dict(conf="high", cite=[
        (U + "f=112&t=41544", "2017", '"Wizard Specs in Uthgard 1.65": "50 Fire / 19 Earth / 7 Ice"')]),
    ("Wizard", "Ice PBAE"): dict(conf="high", specs={"Cold Magic": 50, "Earth Magic": 20, "Fire Magic": 4}, cite=[
        (U + "f=112&t=41544", "2017", '"50 Ice / 20 Earth"')]),
    ("Wizard", "Earth"): dict(conf="high", specs={"Earth Magic": 48, "Fire Magic": 23, "Cold Magic": 9}, cite=[
        (U + "f=112&t=41544", "2017", '"48 Earth / 23 Fire / 7 Ice"; "Earth is not good at this patch level"')]),
    ("Sorcerer", "Mind/Body (AE mez)"): dict(conf="high", cite=[
        (U + "t=37922", "2016", '"If you want to rvr (8-man / zergs), I\'d suggest 44 mind / 31 body"'),
        (U + "f=60&t=24637", "2011", '"44mind/31 body seems to be the standard at higher rr"')]),
    ("Sorcerer", "Body/Mind"): dict(conf="high", cite=[
        (U + "f=60&t=24637", "2011", '"split spec (39 body/37 mind) ... is the standard sorc spec"')]),
    ("Sorcerer", "Matter/Mind"): dict(conf="high", cite=[], note="no modern source; kept as the levelling build"),
    ("Cabalist", "Matter/Body"): dict(conf="high", cite=[
        (U + "f=100&t=40382", "2017", '"The classic matter cab is 46 matter 28 body and 4 spirit for pet recycle"')]),
    ("Cabalist", "Matter/Spirit"): dict(conf="high", weight=0.15, cite=[],
        note="no modern source names it; weight 0.30 -> 0.15, the rest to the RvR tri-spec"),
    ("Cabalist", "Body"): dict(conf="high", cite=[(U + "f=100&t=36855", "2016", '"46 body 25 matter"')]),
    ("Cabalist", "Spirit/Body"): dict(conf="high", specs={"Spirit Magic": 46, "Body Magic": 28, "Matter Magic": 4}, cite=[
        (U + "f=100&t=40382", "2017", '"The classic spirit cab is 46 spirit 28 body"')]),
    # Necromancer: low confidence, no entry (posted points, sourced false)
    # ---- Midgard
    ("Healer", "Pac/Mend"): dict(conf="high", weight=0.30, cite=[
        (U + "t=24011", "2011", 'Healer Guide: "44 pac 31 mend rest aug"')]),
    ("Healer", "Mend/Aug"): dict(conf="high", weight=0.30, cite=[
        (U + "t=24011", "2011", 'Healer Guide: "39 mend, 37 aug"')]),
    ("Healer", "Pacification (48 Pac)"): dict(conf="high", weight=0.25, cite=[
        (U + "f=61&t=22160", "2011", '"48 in pac for 10 sec stun instead 9 sec is waste of points"')],
        note="weight 0.45 -> 0.25: the period posts favour it, the 1.65 server calls Pac 48 a waste"),
    ("Shaman", "Cave/Aug"): dict(conf="high", cite=[(U + "f=61&t=25947", "2012", '"41 sub / 32 aug, 14 mend"')]),
    ("Shaman", "Aug/Cave (resist buffs, end regen)"): dict(conf="high", cite=[
        (U + "f=121&t=37579", "2016", '"38 aug 37 cave 9 mend is just about perfect"')]),
    ("Shaman", "Aug/Mend"): dict(conf="high", weight=0.07, cite=[],
        note="the period Shaman guide's 47/26/5 Aug/Mend backs it; weight split with the new Aug group build"),
    ("Skald", "Battlesongs/Hammer"): dict(conf="high", weight=0.40, cite=[
        (U + "t=11362", "2009", '"46 BS, 44 hammer, 17 parry"'), (U + "f=122&t=39941", "2017", "the same")],
        note="weight 0.30 -> 0.40: Uthgard Skalds almost all go Hammer"),
    ("Skald", "Battlesongs/Axe"): dict(conf="high", weight=0.25, cite=[]),
    ("Skald", "Two-handed sword"): dict(conf="high", cite=[
        (U + "f=122&t=39941", "2017", '"50 Sword spec due to the high growth rate of Ragnarok"')]),
    ("Warrior", "Axe/Shield/Parry"): dict(conf="high", rest="Parry", cite=[
        (U + "t=36249", "2016", '"50 weapon 42 shield 41 parry, always"')]),
    ("Warrior", "Hammer/Shield"): dict(conf="high", rest="Parry", cite=[
        (U + "t=36249", "2016", '"50 Hammer 50 Shield 31 Parry"; "50 hammer / 50 shield / 12 sword / 12 axe"')]),
    ("Warrior", "Axe/Shield 50"): dict(conf="high", rest="Parry", cite=[
        (U + "t=36249", "2016", '"50 sword 50 shield and 31 parry" with full autotrain')]),
    ("Berserker", "Axe/Left Axe"): dict(conf="high", cite=[
        (U + "f=61&t=25876", "2012", '"weapon 50 la 50 parry 28 this one is my favorite"')]),
    ("Berserker", "Hammer/Left Axe"): dict(conf="high", cite=[(U + "f=61&t=25876", "2012", "the same with Hammer")]),
    ("Berserker", "Sword/Left Axe"): dict(conf="high", weight=0.07, cite=[(U + "f=61&t=25876", "2012", "the same with Sword")],
        note="weight 0.15 -> 0.07, the rest to the new bi-spec"),
    ("Savage", "Hand to Hand"): dict(conf="medium", cite=[
        (U + "f=119&t=39894", "2017", '"50H2H ... 39-42 Savagery ... rest parry"')]),
    ("Savage", "Two-handed (Sword/Hammer)"): dict(conf="medium", cite=[
        (U + "f=119&t=39894", "2017", '"44H2H or Weapon(2-hander) 49Savagery 4Parry"')]),
    ("Thane", "Storm/Shield/Parry"): dict(conf="high",
        specs={"Stormcalling": 50, "Shields": 42, "Hammer": 39, "Parry": 6}, weight=0.55, cite=[
            (U + "f=124&t=38066", "2016-17", '"50 SC / 42 shield / 39 hammer / 6 parry"; "if you stop before 50 SC '
                                            'your lose the 75 str/con buff"')]),
    ("Thane", "Storm/Hammer/Shield"): dict(conf="high", weight=0.45, cite=[
        (U + "f=124&t=38066", "2016-17", '"Unless you plan to not get Slam, you can\'t go 48 SC / 44 Hammer"')],
        note="the period posts back it; weight 0.7 -> 0.45"),
    ("Shadowblade", "Sword/CS (2h or 1h)"): dict(conf="medium",
        specs={"Critical Strike": 50, "Sword": 39, "Stealth": 34, "Envenom": 34, "Left Axe": 12}, rest="Left Axe",
        weight=0.45, label="Critblade (Sword/CS)", cite=[
            ("https://uthgard.net/forum/viewtopic.php?t=33907", "2014-17", '"sword39 / 50cs / 34stealth / 34venom / rest '
             'LA"; "SB Critblades were the dominating stealther in 1.65"')]),
    ("Shadowblade", "Axe/Left Axe"): dict(conf="medium", weight=0.30, cite=[
        ("https://uthgard.net/forum/viewtopic.php?t=33907", "2014-17", '"weap39 / 10cs / 34stealth / 34venom / 50LA"; '
         "the period posts are 44/44")]),
    ("Shadowblade", "Axe/CS"): dict(conf="medium", cite=[
        ("https://uthgard.net/forum/viewtopic.php?t=33907", "2014-17", '"the all spec, all is 34 but 39LA"')]),
    ("Hunter", "Spear/Bow (melee hunter)"): dict(conf="high", cite=[
        (U + "t=39501&start=15", "2017", '"BC 32, ST 35, SP 44, Bow 40"')]),
    ("Hunter", "Bow/Spear"): dict(conf="high", cite=[(U + "t=39501", "2017", '"35 BC 39 Weapon 32 Stealth 45 Bow"')]),
    ("Runemaster", "Darkness"): dict(conf="high", specs={"Darkness": 47, "Suppression": 26, "Runecarving": 5}, cite=[
        (U + "f=118&t=39893", "2017", '"47 dark / 26 supp"'),
        (U + "f=61&t=32297", "", '"standard dark specs is 47 in dark"')],
        note="the 17 spare points go into the third line, as Uthgard casters do (research finding 3)"),
    ("Runemaster", "Darkness 50"): dict(conf="high", specs={"Darkness": 50, "Suppression": 20, "Runecarving": 4}, cite=[
        (U + "f=61&t=20924", "2011", '"50 Darkness 20 Suppression"')]),
    ("Runemaster", "Suppression (PBT)"): dict(conf="high", cite=[(U + "f=61&t=32297", "", '"Supp runemaster are awesome"')]),
    ("Spiritmaster", "Suppression (PBAE)"): dict(conf="high", specs={"Suppression": 49, "Darkness": 22, "Summoning": 5},
        cite=[(U + "f=61&t=40747", "2017", '"49 Supp for highest bomb and rest dark"')]),
    ("Spiritmaster", "Darkness/Suppression"): dict(conf="high", specs={"Darkness": 47, "Suppression": 26, "Summoning": 5},
        cite=[(U + "f=61&t=24879", "2011", '"47 Darkness 26 Suppression is the only way to go as dark spec"')]),
    ("Spiritmaster", "Summoning"): dict(conf="high", cite=[], note="no modern source; kept for levelling"),
    ("Bonedancer", "Suppression"): dict(conf="high", specs={"Suppression": 47, "Darkness": 26, "Bone Army": 5}, cite=[
        (U + "f=115&t=38851", "2017", 'BD Guide by Sheena (BD Teamlead): "47 Suppression / 26 Darkness / 5 Bone Army"')]),
    ("Bonedancer", "Bone Army"): dict(conf="high", cite=[
        (U + "f=115&t=38851", "2017", '"44 Bone Army / 31 Suppression / 4 Darkness", "48 Bone Army / 24 Suppression / '
                                      '6 Darkness"')]),
    # ---- Hibernia
    ("Druid", "Nurture/Regrowth (spec buffs)"): dict(conf="high", specs={"Nurture": 44, "Regrowth": 31, "Nature": 4},
        cite=[(U + "f=62&t=17324", "2010", '"44 nurture/31 regrowth/4 nature"')]),
    ("Druid", "Regrowth/Nurture"): dict(conf="high", cite=[(U + "f=131&t=40770", "2017", '"35r+40n or 33r+42n"')]),
    ("Druid", "Nature"): dict(conf="high", cite=[(U + "f=131&t=40770", "2017", '"36 nature, 26 nurture, 30 regrowth"')]),
    ("Bard", "Music/Nurture"): dict(conf="medium", specs={"Music": 44, "Nurture": 43, "Regrowth": 23}, cite=[
        (U + "f=62&t=29049", "2013", '"43nurture 44music 20regrowth ... The last aoe instamezz is trash, that\'s why not '
                                     '47 music"')]),
    ("Bard", "Regrowth/Nurture"): dict(conf="medium", specs={"Nurture": 43, "Music": 37, "Regrowth": 33},
        label="Group bard (Nurture/Music/Regrowth)", cite=[
            (U + "t=22367", "2011", '"43 nurture 37 music 33 regrowth"'),
            (U + "f=128&t=48068", "2020", '"43 nurture ... 37 music ... 31 regrowth"')],
        note="no source has a Music-16 bard: it loses the instant mezz (id kept for saved plans)"),
    ("Warden", "Nurture/Regrowth"): dict(conf="high", specs={"Nurture": 49, "Regrowth": 33, "Blades": 29, "Parry": 3},
        cite=[(U + "f=62&t=24521", "2011", '"You can go 49 nurt 33 reg 29 weapon 3 parry as well"'),
              (U + "p=418263", "2017", '"49 nurture 33 heals and rest blade/parry"')],
        note="the fill had dropped the weapon (Parry 29); the class review proposed the base Blades 25 Parry 14, the "
             "research's Uthgard template has the weapon at 29"),
    ("Warden", "Nurture/Blades"): dict(conf="high", cite=[]),
    ("Hero", "Large Weapons/Shield"): dict(conf="medium", cite=[
        (U + "f=134&t=43002", "2017", '"50 LW 42 shield 39 spear", the same lines with Spear for Parry; the period posts '
                                      "back ours")]),
    ("Hero", "Sword and board"): dict(conf="medium", cite=[(U + "f=134&t=43002", "2017", '"50 Shield, 50 LW ... rest parry"')]),
    ("Hero", "Spear (Celtic Spear)/Shield"): dict(conf="medium", cite=[]),
    ("Champion", "Large Weapons/Valor"): dict(conf="high", cite=[
        (U + "f=130&t=38881", "", 'Champion Guide by Jubal (Champ TeamLead): "Valor 50, Large Weapons 50, Parry 27"'),
        (U + "t=36032", "2016", '"most popular are 50valor, 50lw, rest in parry"')]),
    ("Champion", "Large Weapons/Valor/Blades"): dict(conf="high",
        specs={"Valor": 50, "Shields": 42, "Blades": 39, "Parry": 6}, label="Valor/Shield/Blades",
        family="OneHandAndShield", cite=[
            (U + "f=130&t=38881", "", 'TL guide: "Valor 50, Shields 42, Blades 39, Parry 6"; "the Champion does not have '
                                      'enough points to diversify"'),
            (U + "t=36032", "2016", '"50 valore, 42shield, 34 weapon, 20 parry"')],
        note="the posted LW+Blades build cost 298 more than a level 50 has; the class review proposed the base LW 50 "
             "Blades 36 Valor 35 Parry 28, the research's TL guide has one weapon and a shield (id kept for saved plans)"),
    ("Blademaster", "Blades/Celtic Dual"): dict(conf="medium", cite=[]),
    ("Blademaster", "Piercing/Celtic Dual"): dict(conf="medium",
        specs={"Celtic Dual": 50, "Shields": 42, "Piercing": 39, "Parry": 6}, label="Celtic Dual/Shield/Piercing",
        family="DualWieldAndShield", cite=[(U + "t=23852", "2011", '"50CD 42shield 39pierce"')],
        note="every source has CD 50; the period posts' Piercing 50 / CD 42 has 3 posts (id kept)"),
    ("Blademaster", "Blunt/Celtic Dual"): dict(conf="medium",
        specs={"Celtic Dual": 50, "Blunt": 39, "Blades": 39, "Parry": 16}, label="Celtic Dual/split weapon", cite=[
            (U + "t=23852", "2011", '"50CD 39pierce 39blade"')], note="as Piercing/Celtic Dual (id kept)"),
    ("Nightshade", "Critical Strike"): dict(conf="medium",
        specs={"Critical Strike": 44, "Piercing": 38, "Stealth": 37, "Envenom": 37, "Celtic Dual": 19},
        rest="Celtic Dual", cite=[(U + "t=45724", "2017", '"44 CS, 37 stealth/env, 38 Pierce, rest CD"')]),
    ("Nightshade", "Piercing/CS"): dict(conf="medium", cite=[]),
    ("Nightshade", "Celtic Dual"): dict(conf="medium", cite=[], note="weak support; kept at its weight"),
    ("Ranger", "Archery/Pathfinding"): dict(conf="medium",
        specs={"Recurve Bow": 42, "Pathfinding": 40, "Stealth": 35, "Piercing": 30, "Celtic Dual": 21},
        rest="Celtic Dual", cite=[
            (U + "t=27267", "2012", 'Ranger Guide: "Stealth 35, Pierce 32, CD 29, Path 40, Bow 35"')],
        note="every Ranger template has some CD; the period ZAM guide too"),
    ("Ranger", "Melee ranger (Piercing/CD)"): dict(conf="medium", cite=[
        (U + "t=27267", "2012", 'Ranger Guide: "Stealth 35, Path 42, Pierce 35, CD 40, Bow Auto (12)"')]),
    ("Eldritch", "Mana (PBAE \"manabomb\")"): dict(conf="medium", specs={"Mana": 50, "Light": 20, "Void": 4}, cite=[
        ("https://uthgard.net/forum/viewtopic.php?t=23989", "2011", '"Mana spec (50 mana / 20 light)"')]),
    ("Eldritch", "Void"): dict(conf="medium", specs={"Void": 49, "Mana": 20, "Light": 10}, weight=0.25, cite=[
        ("https://uthgard.net/forum/viewtopic.php?t=23989", "2011", '"Void spec (49 void, 10 light, 20 mana)"')]),
    ("Eldritch", "Light"): dict(conf="medium", weight=0.35, use="both", cite=[
        ("https://uthgard.net/forum/viewtopic.php?t=23989", "2011", '"Light spec (46 light / 28 mana)"')]),
    ("Enchanter", "Mana (PBAE)"): dict(conf="high", specs={"Mana": 50, "Light": 20, "Enchantments": 4}, cite=[
        (U + "f=62&t=8963", "2009", '"49 Mana, 22 Light is the best RvR spec"; "50mana/20light would be better"')]),
    ("Enchanter", "Light"): dict(conf="high", cite=[]),
    ("Enchanter", "Enchantments (pet/buff)"): dict(conf="high", cite=[]),
    ("Mentalist", "Mana/Mentalism"): dict(conf="medium", weight=0.45, cite=[
        (U + "t=38871", "2003-06", 'Mentalist Team Leader Report: "the Mana specialization is the most popular"'),
        (U + "f=62&t=27551", "", '"46 mana 28 menta"')]),
    ("Mentalist", "Light/Mana"): dict(conf="medium", weight=0.40, cite=[
        (U + "t=38871", "2003-06", '"The Light specialization is not too far behind"')]),
    ("Mentalist", "Mentalism (heals)"): dict(conf="medium", cite=[]),
    ("Valewalker", "Scythe/Arboreal"): dict(conf="high", weight=0.7, cite=[
        (U + "p=398755", "2017", 'The complete VW guide: "38/50/20 ... possibly one of the most common melee spec\'s"')]),
    ("Animist", "Arboreal (bomber/pet)"): dict(conf="high", cite=[
        (U + "f=62&t=31993", "2013", '"50 Arb 19 Creeping 7 Verd ... 7 Verdant is for the first AoE bubble"')]),
}

# class -> [build]: templates the research adds. Their weights come out of the builds whose weight changed above.
NEW_BUILDS = {
    "Paladin": [dict(id="paladin-chants-shld-2h", label="Two-handed hybrid (Chants/Shield/2H)", role="tank-support",
        use="rvr", family="TwoHandHybrid", weight=0.10,
        specs={"Chants": 43, "Shields": 42, "Two Handed": 39, "Slash": 31, "Parry": 4}, rest="Slash", cite=[
            (U + "f=107&t=46177", "2018", '"the most common spec for group RvR Paladins ... 39 2h, 42 Shield, 21+ Slash, '
                                          '42+ Chants"; one player posts 43/42/39/31/4')])],
    "Mercenary": [dict(id="mercenary-dw-shld", label="Dual wield/Shield (slam)", role="melee-dps", use="rvr",
        family="DualWieldAndShield", weight=0.30,
        specs={"Dual Wield": 50, "Shields": 42, "Slash": 39, "Parry": 13, "Thrust": 12}, rest="Parry", cite=[
            (U + "f=104&t=41099", "2017", '"Mercenary should aim to have 42 Shield for RvR"'),
            (U + "f=60&t=45364", "2017", '"50DW/42shield/3x weaponskill/rest parry"')])],
    "Cabalist": [dict(id="cabalist-body-spirit-matter", label="RvR tri-spec", role="assist-dd", use="rvr",
        family="BodyCab", weight=0.15, specs={"Body Magic": 34, "Spirit Magic": 33, "Matter Magic": 25}, cite=[
            (U + "f=100&t=41486", "2017", '"many RvR Cabalists choose Tri-Specs like 25 Matter, 34 Body, 33 Spirit"')])],
    "Healer": [dict(id="healer-pac-mend-aug19", label="Tri-spec", role="healer-cc", use="rvr", family="PacHealer",
        weight=0.15, specs={"Pacification": 38, "Mending": 33, "Augmentation": 19}, cite=[
            (U + "t=24011", "2011", 'Healer Guide: "Tri Spec (38 Pac / 33 Mend / 19 Aug)"')])],
    "Shaman": [dict(id="shaman-aug-cave46", label="Aug group RvR", role="healer-buffer", use="rvr", family="AugShaman",
        weight=0.08, specs={"Augmentation": 46, "Subterranean": 27, "Mending": 8}, cite=[
            (U + "f=121&t=37579", "2016", '"The best shaman rvr spec is 46aug, 27cave, 8mend"')])],
    "Berserker": [dict(id="berserker-la-axe-hammer", label="Bi-spec (group)", role="melee-dps", use="rvr", family="None",
        weight=0.08, specs={"Left Axe": 50, "Axe": 39, "Hammer": 39, "Parry": 16}, rest="Parry", cite=[
            (U + "f=61&t=25876", "2012", '"bi spec la 50 weapons slash/hammer 39 rest in parry, this one is pure grp spec"'),
            (U + "f=114&t=37162", "2016", '"39 Axe 39 Hammer 50 Left Axe with the leftovers into Parry"')])],
    "Valewalker": [dict(id="valewalker-scythe-arb34", label="Scythe/Arboreal 34", role="melee-dps", use="both",
        family="None", weight=0.3, specs={"Scythe": 50, "Arboreal Path": 34, "Parry": 26}, rest="Parry", cite=[
            (U + "p=398755", "2017", 'The complete VW guide: "34/50/26 Another very popular melee spec"')])],
}

# The research's confidence per class (section 3). Necromancer is low: its builds stay as posted, sourced false.
CONFIDENCE = {"Necromancer": "low"}
