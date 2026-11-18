#!/usr/bin/env python3
"""Era builds 1.2, WP0: the research draft -> the data file the server and the launcher read.

    python3 source/tools/make_era_builds.py            # writes the data file and prints every change for the review
    python3 source/tools/make_era_builds.py --check    # exit 1 if the data file is not what the draft + decisions give

Input: docs/research/bot-builds.draft.json (the record of what the 2002-2003 posts said, kept unchanged).
Output: source/server/GameServer/bots/autonomous/data/era-builds-165.json (embedded in GameServer.dll and the launcher;
read by OfflineDaoc.Shared.EraBuildCatalog).

The owner's review of 2026-11-17 09:48 (docs/ERA-BUILDS-1.2.md, "The owner's review"): "Yes to all except the custom
class, that should never be played by bots". Applied here:
  D3 A'  Midgard's shares moved toward the sourced Excalibur count of 2003-01-22, healers kept (MIDGARD_A_PRIME).
  D4     one class-share table per realm: the draft's RvR table (its levelling table stays in the research doc).
  D5     offMetaPercent 10, jitterPoints 2.
  D7     replaced by the owner on 2026-11-17 18:59 ("bots should be fully trained on what our 1.65 and Wayback build
         scraping showed") and 19:43 ("use more recent discussions and templates as a base and then tune back towards
         what you directionally know about 1.65"): a build whose class the research covers with high or medium
         confidence is the research's template (era_research.RESEARCH, Uthgard 1.65 and reposted period guides, cited
         per build); the others are the posted points, fully trained. Every autotrain line a build doesn't train is at
         its free 12. The text below says how the posted points are trained. Every build is the period build (the dated FreddysHouse posts of the draft, checked against
         the Wayback scrape of 2026-11-13: WAYBACK) and spends a level 50 player's whole budget (fully_trained): the
         posted points, then the rest in the build's remainder line - or, where the posted points cost more than a
         level 50 has, less in that line. What is left is always less than the next level of any of its lines.
         A player's autotrain lines are free up to level 12 at 50 (AUTOTRAIN; owner 2026-11-17 18:11: "companions and
         bots should be the same as a player").
  D8     Wizard Fire 60 / Earth 25 / Ice PBAE 15 (WEIGHT_CHANGES).
  Owner rule (replaces D6): bots never play the Sluaghbinder, in any edition. It has no share and no builds here, and
         the loader refuses it (excludedClasses).
Every share and every weight is an estimate (sourced: false); only the spec points come from dated posts, and a trim or
fill says so in "change" (the posted points stay in "posted"; "changeBasis" says what the change rests on).

    python3 source/tools/make_era_builds.py --table    # the per-build source check (docs/research/ERA-BUILDS-CHECK.md)
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from urllib.parse import urlparse

sys.path.insert(0, str(Path(__file__).resolve().parent))
from era_research import CONFIDENCE, NEW_BUILDS, RESEARCH  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
DRAFT = REPO / "docs/research/bot-builds.draft.json"
OUT = REPO / "source/server/GameServer/bots/autonomous/data/era-builds-165.json"
CHECK_DOC = REPO / "docs/research/ERA-BUILDS-CHECK.md"

APPROVED = "2026-11-17"
OFF_META_PERCENT = 10
JITTER_POINTS = 2

# Class id (eCharacterClass), realm, the spec multiplier (m_specializationMultiplier in playerclasses/) and the lines
# a 1.65 character of the class trains (Specs constants' values).
CLASSES = {
    "Paladin": (1, "Albion", 20, ["Slash", "Thrust", "Crush", "Two Handed", "Shields", "Parry", "Chants"]),
    "Armsman": (2, "Albion", 20, ["Slash", "Thrust", "Crush", "Polearm", "Two Handed", "Shields", "Parry", "Crossbows"]),
    "Scout": (3, "Albion", 20, ["Slash", "Thrust", "Shields", "Longbows", "Stealth"]),
    "Minstrel": (4, "Albion", 15, ["Slash", "Thrust", "Stealth", "Instruments"]),
    "Theurgist": (5, "Albion", 10, ["Earth Magic", "Cold Magic", "Wind Magic"]),
    "Cleric": (6, "Albion", 10, ["Rejuvenation", "Enhancement", "Smite"]),
    "Wizard": (7, "Albion", 10, ["Earth Magic", "Cold Magic", "Fire Magic"]),
    "Sorcerer": (8, "Albion", 10, ["Matter Magic", "Body Magic", "Mind Magic"]),
    "Infiltrator": (9, "Albion", 25, ["Slash", "Thrust", "Dual Wield", "Critical Strike", "Stealth", "Envenom"]),
    "Friar": (10, "Albion", 15, ["Rejuvenation", "Enhancement", "Staff", "Parry"]),
    "Mercenary": (11, "Albion", 20, ["Slash", "Thrust", "Crush", "Dual Wield", "Shields", "Parry"]),
    "Necromancer": (12, "Albion", 10, ["Deathsight", "Death Servant", "Painworking"]),
    "Cabalist": (13, "Albion", 10, ["Matter Magic", "Body Magic", "Spirit Magic"]),
    "Reaver": (19, "Albion", 20, ["Slash", "Thrust", "Crush", "Flexible", "Shields", "Parry", "Soulrending"]),
    "Thane": (21, "Midgard", 20, ["Sword", "Axe", "Hammer", "Shields", "Parry", "Stormcalling"]),
    "Warrior": (22, "Midgard", 20, ["Sword", "Axe", "Hammer", "Shields", "Parry", "Thrown Weapons"]),
    "Shadowblade": (23, "Midgard", 22, ["Sword", "Axe", "Left Axe", "Critical Strike", "Stealth", "Envenom"]),
    "Skald": (24, "Midgard", 15, ["Sword", "Axe", "Hammer", "Parry", "Battlesongs"]),
    "Hunter": (25, "Midgard", 20, ["Spear", "Sword", "Composite Bow", "Beastcraft", "Stealth"]),
    "Healer": (26, "Midgard", 10, ["Mending", "Augmentation", "Pacification"]),
    "Spiritmaster": (27, "Midgard", 10, ["Darkness", "Suppression", "Summoning"]),
    "Shaman": (28, "Midgard", 10, ["Mending", "Augmentation", "Subterranean"]),
    "Runemaster": (29, "Midgard", 10, ["Darkness", "Suppression", "Runecarving"]),
    "Bonedancer": (30, "Midgard", 10, ["Darkness", "Suppression", "Bone Army"]),
    "Berserker": (31, "Midgard", 20, ["Sword", "Axe", "Hammer", "Left Axe", "Parry"]),
    "Savage": (32, "Midgard", 15, ["Sword", "Axe", "Hammer", "Hand to Hand", "Savagery", "Parry"]),
    "Eldritch": (40, "Hibernia", 10, ["Light", "Mana", "Void"]),
    "Enchanter": (41, "Hibernia", 10, ["Light", "Mana", "Enchantments"]),
    "Mentalist": (42, "Hibernia", 10, ["Light", "Mana", "Mentalism"]),
    "Blademaster": (43, "Hibernia", 20, ["Blades", "Blunt", "Piercing", "Celtic Dual", "Shields", "Parry"]),
    "Hero": (44, "Hibernia", 20, ["Blades", "Blunt", "Piercing", "Large Weapons", "Celtic Spear", "Shields", "Parry"]),
    "Champion": (45, "Hibernia", 20, ["Blades", "Blunt", "Piercing", "Large Weapons", "Valor", "Shields", "Parry"]),
    "Warden": (46, "Hibernia", 15, ["Blades", "Blunt", "Regrowth", "Nurture", "Parry"]),
    "Druid": (47, "Hibernia", 10, ["Regrowth", "Nurture", "Nature"]),
    "Bard": (48, "Hibernia", 15, ["Blades", "Blunt", "Regrowth", "Nurture", "Music"]),
    "Nightshade": (49, "Hibernia", 22, ["Blades", "Piercing", "Celtic Dual", "Critical Strike", "Stealth", "Envenom"]),
    "Ranger": (50, "Hibernia", 20, ["Blades", "Piercing", "Celtic Dual", "Recurve Bow", "Pathfinding", "Stealth"]),
    "Animist": (55, "Hibernia", 10, ["Arboreal Path", "Creeping Path", "Verdant Path"]),
    "Valewalker": (56, "Hibernia", 15, ["Scythe", "Arboreal Path", "Parry"]),
}

# The class's autotrain lines among the lines above (GetAutotrainableSkills in playerclasses/, in its order; Archery is
# not a 1.65 line, and the Hunter's list is commented out there). A player pays nothing for them up to a quarter of the
# level, whenever they are trained (GamePlayer.GetAutoTrainPoints, VerifySpecPoints): 77 points a line at 50.
AUTOTRAIN = {
    "Paladin": ["Slash", "Chants"],
    "Armsman": ["Slash", "Thrust"],
    "Scout": ["Longbows"],
    "Minstrel": ["Instruments"],
    "Infiltrator": ["Stealth"],
    "Mercenary": ["Slash", "Thrust"],
    "Reaver": ["Slash", "Flexible"],
    "Warrior": ["Axe", "Hammer", "Sword"],
    "Shadowblade": ["Stealth"],
    "Nightshade": ["Stealth"],
    "Ranger": ["Recurve Bow"],
}
AUTOTRAIN_LEVEL_50 = 50 // 4

# The owner's rule of 2026-11-17 (replaces D6): no gamebot, companion or helper is ever made as the custom class.
EXCLUDED = {
    "Sluaghbinder": {
        "classId": 63,
        "realm": "Hibernia",
        "rule": "bots never play the custom class, in any edition (owner 2026-11-17): no share, no builds; "
                "players keep it in the 0.37b edition",
    }
}

# D3 A': Midgard moved toward the Excalibur level-50 count of 2003-01-22; Healer 15 and Shaman 9 kept.
MIDGARD_A_PRIME = {"Thane": 0.08, "Runemaster": 0.09, "Berserker": 0.08, "Shadowblade": 0.10, "Hunter": 0.05,
                   "Savage": 0.06}

# The Wayback scrape of 2026-11-13 (the owner's PC; packs 3-builds-specs, a3-build-threads, 6-mechanics, 2-patch-notes).
# Only citations are kept here: site, page, capture date, and the few words that carry the point. No scraped page is in
# the repository.
WAYBACK_SOURCES = {
    "shaman-guide": ("camelot.allakhazam.com", "Shaman Guide by Seraphym (Shaman_Guide.html)", "2002-11-12"),
    "warden-guide": ("camelot.allakhazam.com", "Warden Guide by Hoode Wynk, v1.1b of 29 May 2002 (Warden_Guide.html)",
                     "2003-01-05"),
    "blademaster-guide": ("camelot.allakhazam.com", "Blade Guide by Melkio (Blademaster_Guide.html)", "2003-01-05"),
    "hero-guide": ("camelot.allakhazam.com", "Hero Guide (Hero_Guide.html)", "2003-02-24"),
    "ranger-guide": ("camelot.allakhazam.com", "Ranger Guide (Ranger_Guide.html)", "2002-12-30"),
    "cleric-comments": ("camelot.allakhazam.com", "Cleric class page, reader comments of 2002-2003 (db/class.html?cclass=10)",
                        "2004-09-19"),
    "smite-comments": ("camelot.allakhazam.com", "skill page, reader comment of 2002-01-28 (db/skills.html?cskill=25)",
                       "2005-12-06"),
    "healer-comments": ("camelot.allakhazam.com", "skill page, reader comment of 2001-11-06 (db/skills.html?cskill=57)",
                        "2005-12-06"),
    "mind-comments": ("camelot.allakhazam.com", "skill pages, reader comments of 2001-11-07 and 2002-04-02 "
                      "(db/skills.html?cskill=20 and cskill=16)", "2005-12-08"),
    "good-specs": ("camelot.allakhazam.com", "DAoC General Discussion forum, thread \"Good Specs?\", posts of 2004-10-21 to "
                   "2005-01-26, after the era (forum.html?forum=13)", "2005-03-20"),
}

# (class, label) -> [(source, what it shows, verdict)]. Verdicts: "matches" (the top two lines within 4 points, the
# draft's own test), "same lines" (the lines of the build, another split), "remainder" (says where the build's spare
# points go) or "trim" (says which line is not worth its posted points). A build not listed here is not in the scrape.
WAYBACK = {
    ("Shaman", "Cave/Aug"): [("shaman-guide", "36 Cave/37 Aug/15 Mend and 46 Cave/27 Aug/8 Mend", "same lines")],
    ("Shaman", "Aug/Cave (resist buffs, end regen)"): [
        ("shaman-guide", "37 Aug/36 Cave/15 Mend, 'the lines can be maneuvered to raise Aug to 38'", "matches")],
    ("Shaman", "Aug/Mend"): [("shaman-guide", "38 Aug/34 Mend/19 Cave and 47 Aug/26 Mend/5 Cave", "matches")],
    ("Cleric", "Rejuvenation healer"): [("cleric-comments", "'45+12 rejuv, 28+7enh and 10+4 smite' (2003-12-16)", "matches")],
    ("Cleric", "Enhancement/Rejuv (spec buffs)"): [
        ("cleric-comments", "'40 rej, 34 ench, and 12 smite' (2003-10-15): the two lines the other way round", "same lines")],
    ("Cleric", "Smite"): [("smite-comments", "'a template of 48 smite, 23 rejuv, 7 enhance'", "matches"),
                          ("cleric-comments", "'38 Smite, 33 Rej, and 18 Enh' (2002-12-19)", "same lines")],
    ("Healer", "Mend/Aug"): [("healer-comments", "'At 50, 40 aug, 32 mend and 15 pac looks the best to me'", "same lines")],
    ("Infiltrator", "Thrust/CS assassin"): [
        ("good-specs", "'most rr5s are 35 stealth, 35 env, 50 thrust, 34 cs, rest dual wield' (2004-11-02)", "matches"),
        ("good-specs", "'rest dual wield'", "remainder")],
    ("Infiltrator", "Thrust/DW \"5-spec\""): [
        ("good-specs", "'most rr5s are 35 stealth, 35 env, 50 thrust, 34 cs, rest dual wield' (2004-11-02)", "matches")],
    ("Infiltrator", "Slash/CS assassin"): [
        ("good-specs", "'The new flavor for infs are briton slash infs' (2004-11-02), the same template with Slash",
         "same lines"),
        ("good-specs", "'rest dual wield'", "remainder")],
    ("Nightshade", "Piercing/CS"): [
        ("good-specs", "'most 50 NSs/SBs/Infs that i know only take CS to 36 and pierce or thrust to 50' (2005-01-11)",
         "matches")],
    ("Minstrel", "Instruments/Stealth/Slash"): [
        ("good-specs", "'No reason to take Stealth to 50, the highest would be around 35 or so' and '35 is as high as "
         "you need to bring stealth' (2004-10-21/22, said of an Infiltrator)", "trim")],
    ("Warden", "Nurture/Blades"): [
        ("warden-guide", "Nurture 45, 46, 47 or 49; 'a weapon skill around 40 (often stopping at new styles at 34, 39, or "
         "44), regrowth at one of the popular stopping points (2, 12, 16, or 20), and the remainder in parry'", "matches")],
    ("Warden", "Nurture/Regrowth"): [
        ("warden-guide", "Nurture 49 is a popular stop; the guide's Wardens take a weapon to 34 or more and stop Regrowth "
         "at 20, which this build does not", "same lines"),
        ("warden-guide", "'the Parry skill is what's left over once the other skills have been raised'", "remainder")],
    ("Blademaster", "Blades/Celtic Dual"): [
        ("blademaster-guide", "'most people say Celtic Dual and Blades then the last points go into parry'", "matches")],
    ("Hero", "Sword and board"): [
        ("hero-guide", "'A hero with 50 sword, 50 parry, and 50 shield is, obviously, going to be very powerful'",
         "same lines")],
    ("Hero", "Large Weapons/Shield"): [
        ("hero-guide", "'the lvl 42 shield style is considered quite uber'; LW heroes 'spending some significant points on "
         "their shield skills' is called an unusual hybrid (2002)", "same lines")],
    ("Hero", "Spear (Celtic Spear)/Shield"): [
        ("hero-guide", "'the lvl 42 shield style is considered quite uber'; CS heroes 'spending some significant points on "
         "their shield skills' is called an unusual hybrid (2002)", "same lines")],
    ("Ranger", "Archery/Pathfinding"): [
        ("ranger-guide", "templates of 2002: '43 Pathfinding, 25 Pierce, 45 Recurve Bows, 31 Stealth, 18 Celtic Dual' and "
         "'27 Pathfinding, 25 Pierce, 50 Recurve Bows, 40 Stealth, 18 Celtic Dual'", "same lines")],
    ("Ranger", "Melee ranger (Piercing/CD)"): [
        ("ranger-guide", "the same five lines in its 2002 templates, the bow higher and the melee lines lower", "same lines")],
    ("Sorcerer", "Body/Mind"): [
        ("mind-comments", "'only plan on having level 34 spec in mind twising so i get the 2nd best mez'; '35 in mind which "
         "gives u second best mez'", "same lines")],
    ("Sorcerer", "Matter/Mind"): [
        ("mind-comments", "'only plan on having level 34 spec in mind twising so i get the 2nd best mez'; '35 in mind which "
         "gives u second best mez'", "same lines")],
}

# Where a build's spare points go, and where they come from when the posted points cost more than a level 50 has: the
# build's remainder line, its lowest posted line. The period guides say so in words: the Shaman guide ("places the
# remaining points in Cave", "the remainder in Cave", "the rest in Aug"), the Warden guide ("the remainder in parry"),
# the Blademaster guide ("the last points go into parry") and the Infiltrator thread ("rest dual wield").
REMAINDER_BASIS = ("the build's remainder line (its lowest posted line), where the period guides put spare points "
                   "(Allakhazam's Shaman, Warden and Blademaster guides, captured 2002-11-12 and 2003-01-05)")

# The one build whose points come out of another line than its lowest: a source says which line is not worth them.
TRIM_LINE = {("Minstrel", "Instruments/Stealth/Slash"): "Stealth"}

# D8: the weight that contradicted its own source text.
WEIGHT_CHANGES = {
    ("Wizard", "Fire"): (0.60, "D8: 0.45 -> 0.60"),
    ("Wizard", "Ice PBAE"): (0.15, "D8: 0.35 -> 0.15 (one post; the threads say Ice wizards were rare)"),
    ("Wizard", "Earth"): (0.25, "D8: 0.20 -> 0.25"),
}

# The class file family (an eSpecType name in BotSpec.GetSpecializationChoices) whose weapon and gear rules a build uses.
FAMILIES = {
    ("Armsman", "Polearm/Shield hybrid"): "TwoHandHybrid",
    ("Armsman", "Two-handed/Shield hybrid"): "TwoHandHybrid",
    ("Armsman", "Sword and board"): "OneHandAndShield",
    ("Armsman", "Polearm/Shield hybrid (Thrust)"): "TwoHandHybrid",
    ("Paladin", "Chants/Shield/Parry"): "OneHandAndShield",
    ("Paladin", "Chants/Shield/Slash"): "OneHandAndShield",
    ("Paladin", "Full shield"): "OneHandAndShield",
    ("Paladin", "Two-handed chanter"): "TwoHandHybrid",
    ("Mercenary", "*"): "DualWield",
    ("Reaver", "Flex/Shield/Soulrending"): "None",
    ("Reaver", "Soulrending focus"): "SoulrendingReaver",
    ("Cleric", "Rejuvenation healer"): "RejuvCleric",
    ("Cleric", "Enhancement/Rejuv (spec buffs)"): "EnhanceCleric",
    ("Cleric", "Smite"): "SmiteCleric",
    ("Friar", "*"): "EnhanceFriar",
    ("Minstrel", "*"): "None",
    ("Infiltrator", "*"): "None",
    ("Scout", "*"): "None",
    ("Theurgist", "Air"): "AirTheur",
    ("Theurgist", "Earth (PBT)"): "EarthTheur",
    ("Theurgist", "Ice/Earth"): "IceTheur",
    ("Wizard", "Fire"): "FireWiz",
    ("Wizard", "Ice PBAE"): "IceWiz",
    ("Wizard", "Earth"): "EarthWiz",
    ("Sorcerer", "Mind/Body (AE mez)"): "MindSorc",
    ("Sorcerer", "Body/Mind"): "BodySorc",
    ("Sorcerer", "Matter/Mind"): "MatterSorc",
    ("Cabalist", "Matter/Body"): "MatterCab",
    ("Cabalist", "Matter/Spirit"): "MatterCab",
    ("Cabalist", "Body"): "BodyCab",
    ("Cabalist", "Spirit/Body"): "SpiritCab",
    ("Necromancer", "Deathsight"): "DeathsightNecro",
    ("Necromancer", "Death Servant"): "DeathServantNecro",
    ("Necromancer", "Deathsight/Painworking"): "DeathsightNecro",
    ("Healer", "Pacification (48 Pac)"): "PacHealer",
    ("Healer", "Pac/Mend"): "PacHealer",
    ("Healer", "Mend/Aug"): "MendHealer",
    ("Shaman", "Cave/Aug"): "SubtShaman",
    ("Shaman", "Aug/Cave (resist buffs, end regen)"): "AugShaman",
    ("Shaman", "Aug/Mend"): "AugShaman",
    ("Skald", "Battlesongs/Axe"): "OneHandAndShield",
    ("Skald", "Two-handed sword"): "TwoHanded",
    ("Skald", "Battlesongs/Hammer"): "OneHandAndShield",
    ("Warrior", "*"): "OneHandAndShield",
    ("Berserker", "*"): "None",
    ("Savage", "Hand to Hand"): "DualWield",
    ("Savage", "Two-handed (Sword/Hammer)"): "TwoHanded",
    ("Thane", "*"): "OneHandAndShield",
    ("Shadowblade", "Axe/Left Axe"): "LeftAxe",
    ("Shadowblade", "Sword/CS (2h or 1h)"): "TwoHanded",
    ("Shadowblade", "Axe/CS"): "LeftAxe",
    ("Hunter", "Bow/Spear"): "None",
    ("Hunter", "Spear/Bow (melee hunter)"): "SpearHunter",
    ("Runemaster", "Darkness"): "DarkRune",
    ("Runemaster", "Darkness 50"): "DarkRune",
    ("Runemaster", "Suppression (PBT)"): "SuppRune",
    ("Spiritmaster", "Suppression (PBAE)"): "SuppSpirit",
    ("Spiritmaster", "Darkness/Suppression"): "DarkSpirit",
    ("Spiritmaster", "Summoning"): "SummSpirit",
    ("Bonedancer", "Suppression"): "SuppBone",
    ("Bonedancer", "Bone Army"): "ArmyBone",
    ("Druid", "Regrowth/Nurture"): "RegrowthDruid",
    ("Druid", "Nurture/Regrowth (spec buffs)"): "NurtureDruid",
    ("Druid", "Nature"): "NatureDruid",
    ("Bard", "Music/Nurture"): "None",
    ("Bard", "Regrowth/Nurture"): "RegrowthBard",
    ("Warden", "Nurture/Regrowth"): "NurtureWarden",
    ("Warden", "Nurture/Blades"): "BattleWarden",
    ("Hero", "Large Weapons/Shield"): "TwoHandHybrid",
    ("Hero", "Spear (Celtic Spear)/Shield"): "TwoHandHybrid",
    ("Hero", "Sword and board"): "OneHandAndShield",
    ("Champion", "*"): "TwoHanded",
    ("Blademaster", "*"): "DualWield",
    ("Nightshade", "*"): "None",
    ("Ranger", "*"): "None",
    ("Eldritch", "Mana (PBAE \"manabomb\")"): "ManaEld",
    ("Eldritch", "Void"): "VoidEld",
    ("Eldritch", "Light"): "LightEld",
    ("Enchanter", "Mana (PBAE)"): "ManaEnchanter",
    ("Enchanter", "Light"): "LightEnchanter",
    ("Enchanter", "Enchantments (pet/buff)"): "EnchantmentEnchanter",
    ("Mentalist", "Light/Mana"): "LightMenta",
    ("Mentalist", "Mana/Mentalism"): "ManaMenta",
    ("Mentalist", "Mentalism (heals)"): "MentaMenta",
    ("Valewalker", "*"): "None",
    ("Animist", "*"): "ArborealAnimist",
}

# Short words for build ids (stable: made from the posted points, which never change).
ABBREV = {
    "Two Handed": "2h", "Polearm": "pole", "Shields": "shld", "Critical Strike": "cs", "Dual Wield": "dw",
    "Left Axe": "la", "Celtic Dual": "cd", "Large Weapons": "lw", "Celtic Spear": "spear", "Hand to Hand": "h2h",
    "Composite Bow": "bow", "Recurve Bow": "bow", "Longbows": "bow", "Stealth": "stl", "Envenom": "env",
    "Instruments": "inst", "Enhancement": "enh", "Rejuvenation": "rejuv", "Pacification": "pac", "Mending": "mend",
    "Augmentation": "aug", "Subterranean": "cave", "Battlesongs": "battle", "Stormcalling": "storm",
    "Suppression": "supp", "Darkness": "dark", "Summoning": "summ", "Bone Army": "army", "Deathsight": "dsight",
    "Death Servant": "dserv", "Painworking": "pain", "Soulrending": "soul", "Flexible": "flex", "Pathfinding": "path",
    "Beastcraft": "beast", "Arboreal Path": "arb", "Creeping Path": "creep", "Verdant Path": "verd",
    "Mentalism": "menta", "Enchantments": "ench", "Nurture": "nurt", "Regrowth": "regrow", "Earth Magic": "earth",
    "Cold Magic": "cold", "Wind Magic": "wind", "Fire Magic": "fire", "Matter Magic": "matter", "Body Magic": "body",
    "Mind Magic": "mind", "Spirit Magic": "spirit", "Savagery": "sav", "Thrown Weapons": "thrown",
}


def point_budget(multiplier: int, level: int = 50) -> int:
    """GameBot.GetSpecPointsForLevel summed over levels 2..level, with the half levels above 40 (arrival training)."""
    points = 0
    for lvl in range(2, level + 1):
        if lvl > 40:
            points += multiplier * (lvl - 1) // 20
        points += multiplier * lvl // 10 if lvl > 5 else lvl
    return points


def line_cost(level: int, autotrain: bool = False) -> int:
    """Points a level 50 pays to raise a line from 1 to level (2 + 3 + ... + level); an autotrain line's levels up to 12
    are free (GamePlayer.VerifySpecPoints, EraBuildCatalog.LineCost)."""
    cost = level * (level + 1) // 2 - 1 if level > 1 else 0
    return cost - line_cost(min(AUTOTRAIN_LEVEL_50, level)) if autotrain else cost


def build_cost(specs: dict, autotrain=()) -> int:
    """What the lines cost a level 50 whose class autotrains `autotrain` (none: every level is paid for)."""
    return sum(line_cost(v, k in autotrain) for k, v in specs.items())


def next_level_cost(level: int, autotrain: bool = False) -> int:
    """What a level 50 pays for the next level of a line (GameBot.NextLevelCost): nothing below 12 in an autotrain line."""
    return 0 if autotrain and level < AUTOTRAIN_LEVEL_50 else level + 1


def points_left(specs: dict, budget: int, autotrain=()) -> int:
    return budget - build_cost(specs, autotrain)


def cheapest_next_level(specs: dict, autotrain=()) -> int | None:
    """The cost of the cheapest next level in any of the build's lines (None when every line is at 50)."""
    costs = [next_level_cost(v, k in autotrain) for k, v in specs.items() if v < 50]
    return min(costs) if costs else None


def is_fully_trained(specs: dict, budget: int, autotrain=()) -> bool:
    """A level 50 can pay for the build, and what is left buys no further level in any of its lines."""
    left = points_left(specs, budget, autotrain)
    cheapest = cheapest_next_level(specs, autotrain)
    return left >= 0 and (cheapest is None or left < cheapest)


def spend_rest(specs: dict, order: list, budget: int, autotrain=(), exclude=()) -> dict:
    """The rest of a level 50's points go into the cheapest next level of the build's lines - its lowest line, the first
    of `order` on a tie - a level at a time, until what is left buys none (EraBuildCatalog.SpendRest does the same)."""
    specs = dict(specs)
    while True:
        open_lines = [k for k in order if specs[k] < 50 and k not in exclude]
        if not open_lines:
            break
        line = min(open_lines, key=lambda k: (next_level_cost(specs[k], k in autotrain), specs[k], order.index(k)))
        if next_level_cost(specs[line], line in autotrain) > points_left(specs, budget, autotrain):
            break
        specs[line] += 1
    return specs


def fully_trained(posted: dict, budget: int, autotrain=(), trim_line: str | None = None) -> dict:
    """The posted build as a level 50 player trains it with every point (owner 2026-11-17 18:59).

    Over the budget: points come out of the remainder line - the lowest posted line (lines posted at the same level come
    down together), or `trim_line` when a source names another - until a level 50 can pay for it. Then, and for a build
    under the budget, the rest goes into the lowest line a level at a time (the highest posted first on a tie) until it
    buys no further level in any line. No line is added and none goes below 1 or above 50."""
    specs = dict(posted)
    order = list(ordered(posted))
    trimmed = set()
    while points_left(specs, budget, autotrain) < 0:
        if trim_line and specs[trim_line] > 1:
            group = [trim_line]
        else:
            floor = min(posted[k] for k in order if specs[k] > 1)
            group = [k for k in order if posted[k] == floor and specs[k] > 1]
        line = max(group, key=lambda k: (specs[k], order.index(k)))   # the highest of the group, the last listed on a tie
        specs[line] -= 1
        trimmed.add(line)
    # a line just trimmed is never filled again (review of 1f3dcefe2: Wind 11 -> 1 -> 4)
    specs = spend_rest(specs, order, budget, autotrain, exclude=trimmed)
    return specs


def with_autotrain(specs: dict, autotrain=()) -> dict:
    """Every autotrain line at its free level at least (12 at 50), as a player has it: listed or not (research finding 1,
    review of 1f3dcefe2 item 3)."""
    out = dict(specs)
    for line in autotrain:
        out[line] = max(out.get(line, 1), AUTOTRAIN_LEVEL_50)
    return out


def train_build(cls: str, label: str, posted: dict, budget: int, autotrain=()) -> tuple[dict, str]:
    """The trained specs and what they rest on ("research" or "posted")."""
    entry = RESEARCH.get((cls, label), {})
    if entry.get("specs"):
        specs = with_autotrain(entry["specs"], autotrain)
        assert points_left(specs, budget, autotrain) >= 0, (cls, label, "the research template is over budget")
        if entry.get("rest"):
            specs = spend_rest(specs, [entry["rest"]], budget, autotrain)
        base = "research"
    else:
        specs = fully_trained(posted, budget, autotrain, TRIM_LINE.get((cls, label)))
        if entry.get("rest") and entry["rest"] in specs:
            # a source names where the rest goes; until then the build's own lines take it
            specs = fully_trained(posted, budget, autotrain, TRIM_LINE.get((cls, label)))
        specs = with_autotrain(specs, autotrain)
        base = "posted"
    # points still left buy a level in the build's own lines (never one it trimmed, never an autotrain line it doesn't
    # train), the named rest line first
    own = [k for k in ordered(posted if base == "posted" else entry["specs"]) if k in specs]
    if entry.get("rest") in own:
        own.remove(entry["rest"])
        own.insert(0, entry["rest"])
    trimmed = {k for k in posted if base == "posted" and specs.get(k, 0) < posted[k]}
    specs = spend_rest(specs, own, budget, autotrain, exclude=trimmed)
    if not is_fully_trained(specs, budget, autotrain):
        specs = spend_rest(specs, list(ordered(specs)), budget, autotrain, exclude=trimmed)
    assert is_fully_trained(specs, budget, autotrain), (cls, label, specs, points_left(specs, budget, autotrain))
    return specs, base


def citations(cls: str, label: str) -> list[dict]:
    return [{"url": url, "site": urlparse(url).netloc.removeprefix("www."), "date": date, "says": says}
            for url, date, says in RESEARCH.get((cls, label), {}).get("cite", [])]


def describe(posted: dict, specs: dict, autotrain=(), base: str = "posted") -> str | None:
    """'Uthgard 1.65 template: Shields 25->42, ...; autotrain: Thrust 1->12; rest to Parry (6->13)'."""
    free = [f"{k} {posted.get(k, 1)}->{specs[k]}" for k in specs
            if k in autotrain and specs[k] >= AUTOTRAIN_LEVEL_50 and posted.get(k, 1) < AUTOTRAIN_LEVEL_50]
    if base == "research":
        moved = [f"{k} {posted.get(k, 1)}->{specs[k]}" for k in specs if specs[k] != posted.get(k, 1)
                 and f"{k} {posted.get(k, 1)}->{specs[k]}" not in free]
        moved += [f"{k} {v}->1" for k, v in posted.items() if k not in specs]
        parts = (["research template: " + ", ".join(moved)] if moved else []) + \
                (["autotrain at its free level: " + ", ".join(free)] if free else [])
        return "; ".join(parts) or None
    note = change_note(posted, {k: v for k, v in specs.items() if k in posted})
    parts = ([note] if note else []) + (["autotrain at its free level: " + ", ".join(free)] if free else [])
    return "; ".join(parts) or None


def change_note(posted: dict, specs: dict) -> str | None:
    """'trimmed to budget: Stealth 50->33; rest to Slash (29->30)' or 'filled: rest to Parry (6->13)'."""
    down = [f"{k} {posted[k]}->{specs[k]}" for k in posted if specs[k] < posted[k]]
    up = [f"{k} ({posted[k]}->{specs[k]})" for k in posted if specs[k] > posted[k]]
    if down:
        return "trimmed to budget: " + ", ".join(down) + (("; rest to " + ", ".join(up)) if up else "")
    return "filled: rest to " + ", ".join(up) if up else None


def wayback_entries(cls: str, label: str) -> list[dict]:
    out = []
    for source, shows, verdict in WAYBACK.get((cls, label), []):
        site, page, captured = WAYBACK_SOURCES[source]
        out.append({"site": site, "page": page, "captured": captured, "shows": shows, "verdict": verdict})
    return out


def wayback_check(entries: list[dict]) -> str:
    verdicts = {e["verdict"] for e in entries}
    if "matches" in verdicts:
        return "matches a Wayback source"
    if "same lines" in verdicts:
        return "same lines in a Wayback source, another split"
    if verdicts:
        return "a Wayback source speaks to its points only"
    return "not in the Wayback scrape (dated FreddysHouse posts only)"


def ordered(specs: dict) -> dict:
    return dict(sorted(specs.items(), key=lambda kv: (-kv[1], kv[0])))


def family(cls: str, label: str) -> str:
    return FAMILIES.get((cls, label)) or FAMILIES[(cls, "*")]


def make_ids(cls: str, builds: list[dict]) -> list[str]:
    prefix = cls.lower()
    posted = [ordered(b["specs"]) for b in builds]
    keys = [[ABBREV.get(k, k.lower().replace(" ", "")) for k in specs] for specs in posted]
    ids = []
    for i, words in enumerate(keys):
        n = min(2, len(words))
        while True:
            candidate = "-".join([prefix] + words[:n])
            clash = [j for j, other in enumerate(keys) if j != i and "-".join([prefix] + other[:n]) == candidate]
            if not clash or n >= len(words):
                break
            n += 1
        if clash:
            # Same lines in the same order (Warrior Axe/Shield 42 and Axe/Shield 50): the second line's points tell them apart.
            second = list(posted[i].values())[1]
            candidate = "-".join([prefix, words[0], f"{words[1]}{second}"])
        ids.append(candidate)
    assert len(set(ids)) == len(ids), ids
    return ids


def build_data(draft: dict) -> tuple[dict, list[str]]:
    changes: list[str] = []
    realms = {}
    for realm, info in draft["realms"].items():
        shares = dict(info["classShareRvR"])
        if realm == "Midgard":
            for cls, new in MIDGARD_A_PRIME.items():
                changes.append(f"share Midgard {cls}: {shares[cls]:.2f} -> {new:.2f} (D3 A')")
                shares[cls] = new
        assert not any(c in EXCLUDED for c in shares), "the draft already leaves the custom class out"
        entries = {}
        for cls, share in sorted(shares.items(), key=lambda kv: (-kv[1], kv[0])):
            moved = realm == "Midgard" and cls in MIDGARD_A_PRIME
            entries[cls] = {
                "share": round(share, 4),
                "sourced": False,
                "basis": ("estimate, moved toward the Excalibur level-50 count of 2003-01-22 (D3 A')" if moved
                          else "estimate (2002-2003 forum post counts and period wording)"),
            }
        realm_out = {
            "classShare": entries,
            "sharesSourced": False,
            "shareBasis": info.get("shareBasis", "estimate"),
        }
        if "sourcedSnapshot" in info:
            realm_out["sourcedSnapshot"] = info["sourcedSnapshot"]
        realms[realm] = realm_out

    classes = {}
    for cls, builds in draft["classes"].items():
        class_id, realm, multiplier, lines = CLASSES[cls]
        budget = point_budget(multiplier)
        autotrain = AUTOTRAIN.get(cls, [])
        assert all(line in lines for line in autotrain), cls
        out_builds = []
        drafted = [dict(b, id=i) for b, i in zip(builds, make_ids(cls, builds))]
        added = [dict(b, sources=[c[0] for c in b["cite"]], sourceDates=[c[1] for c in b["cite"]], forumMentions=0,
                      sourced=True, new=True) for b in NEW_BUILDS.get(cls, [])]
        low = CONFIDENCE.get(cls) == "low"
        for build in drafted + added:
            label = build["label"]
            build_id = build["id"]
            entry = {} if build.get("new") else RESEARCH.get((cls, label), {})
            posted = ordered(build["specs"])
            if build.get("new"):
                entry = dict(specs=build["specs"], rest=build.get("rest"), cite=build["cite"])
                RESEARCH[(cls, label)] = entry
            trained, base = train_build(cls, label, posted, budget, autotrain)
            specs = ordered(trained)
            change = describe(posted, specs, autotrain, base)
            cites = citations(cls, label)
            wayback = wayback_entries(cls, label)
            change_basis, change_sourced = None, False
            if change is not None:
                only_free = all(k in autotrain and v == AUTOTRAIN_LEVEL_50 for k, v in specs.items() if v != posted.get(k, 1)) \
                    and set(posted) <= set(specs)
                named = [e for e in wayback if e["verdict"] in ("remainder", "trim")]
                if base == "research" and cites:
                    change_sourced = True
                    change_basis = "the cited template (" + "; ".join(f"{c['site']} {c['date']}" for c in cites) + ")"
                elif only_free:
                    change_sourced = True
                    change_basis = ("a player's free autotrain levels (this server: 12 at 50); Uthgard 1.65 writes them in "
                                    "the same way (www.uthgard.net forum t=46589 2018, t=19921 2010, t=36249 2016)")
                elif cites and entry.get("rest"):
                    change_sourced = True
                    change_basis = f"the rest in {entry['rest']}, as the cited source puts it (" + \
                        "; ".join(f"{c['site']} {c['date']}" for c in cites) + ")"
                elif named:
                    change_sourced = True
                    change_basis = "; ".join(f"{e['shows']} ({e['site']}, {e['page']}, captured {e['captured']})" for e in named)
                else:
                    change_basis = REMAINDER_BASIS
                changes.append(f"build {build_id}: {fmt(posted)} -> {fmt(specs)} ({change}; "
                               f"{build_cost(posted, autotrain)} -> {build_cost(specs, autotrain)} of {budget} points)")
            weight, weight_basis = build["weight"], "estimate from forumMentions and period wording"
            if (cls, label) in WEIGHT_CHANGES:
                weight, note = WEIGHT_CHANGES[(cls, label)]
                weight_basis = f"estimate, {note}"
                changes.append(f"weight {build_id}: {build['weight']:.2f} -> {weight:.2f} ({note})")
            if "weight" in entry and not build.get("new"):
                weight_basis = f"estimate, the research: {build['weight']:.2f} -> {entry['weight']:.2f}" + \
                    (f" ({entry['note']})" if entry.get("note", "").startswith("weight") else "")
                changes.append(f"weight {build_id}: {build['weight']:.2f} -> {entry['weight']:.2f}")
                weight = entry["weight"]
            if build.get("new"):
                weight_basis = "estimate, the research adds this template"
            sourced = not low and bool(cites or build.get("sourced"))
            out_builds.append({
                "id": build_id,
                "label": entry.get("label", label),
                "draftLabel": label,
                "role": build["role"],
                "use": entry.get("use", build["use"]),
                "family": entry.get("family") or build.get("family") or family(cls, label),
                "weight": round(weight, 4),
                "weightSourced": False,
                "weightBasis": weight_basis,
                "specs": specs,
                "posted": posted,
                "change": change,
                "changeBasis": change_basis,
                "changeSourced": change_sourced,
                "pointsLeft": points_left(specs, budget, autotrain),
                "sourced": sourced,
                "confidence": CONFIDENCE.get(cls) or entry.get("conf") or ("high" if build.get("new") else "medium"),
                "base": "Uthgard 1.65 / reposted period guide" if base == "research" else "dated 2002-2003 posts",
                "citations": cites,
                "note": entry.get("note"),
                "specsSourced": sourced and (change is None or change_sourced),
                "postedSourced": bool(build.get("sourced", False)),
                "new": bool(build.get("new")),
                "wayback": wayback,
                "check": wayback_check(wayback),
                "forumMentions": build["forumMentions"],
                "sources": build["sources"],
                "sourceDates": build["sourceDates"],
            })
        classes[cls] = {
            "classId": class_id,
            "realm": realm,
            "specMultiplier": multiplier,
            "pointBudget50": budget,
            "lines": lines,
            "autotrain": autotrain,
            "builds": out_builds,
        }

    data = {
        "schema": 1,
        "era": "DAoC patch 1.65 (to 2003-10-08), Classic + Shrouded Isles",
        "status": f"approved {APPROVED}",
        "approved": APPROVED,
        "decisions": "owner 2026-11-17 09:48: D1-D5, D7-D12 as recommended; D6 replaced: bots never play the "
                     "Sluaghbinder (docs/ERA-BUILDS-1.2.md)",
        "estimates": "every share and every weight is an estimate from 2002-2003 forum posts (European servers); "
                     "spec points come from dated posts unless 'change' says they were trimmed or filled "
                     "('posted' keeps the posts' points, 'changeBasis' says what the change rests on)",
        "points": "a build fits when a level 50 player of the class can pay for it: every level of every line, less a "
                  "class's 'autotrain' lines up to level 12 (free for a player, 77 points a line); every build is "
                  "fully trained: 'pointsLeft' buys no further level in any of its lines",
        "sources": "posted points: dated FreddysHouse posts of 2002-2003 ('sources', 'sourceDates'); 'wayback': what the "
                   "Wayback scrape of 2026-11-13 holds for the build (site, page, capture date), empty when nothing does",
        "generatedBy": "source/tools/make_era_builds.py from docs/research/bot-builds.draft.json",
        "offMetaPercent": OFF_META_PERCENT,
        "jitterPoints": JITTER_POINTS,
        "excludedClasses": EXCLUDED,
        "realms": realms,
        "classes": classes,
    }
    return data, changes


def fmt(specs: dict) -> str:
    return " ".join(f"{k} {v}" for k, v in specs.items())


def validate(data: dict) -> list[str]:
    """The data rules (the C# loader checks the same ones; UT_EraBuildData ties them to the game)."""
    problems = []
    for realm, info in data["realms"].items():
        total = sum(entry["share"] for entry in info["classShare"].values())
        if abs(total - 1) > 0.005:
            problems.append(f"{realm}: shares sum to {total:.4f}")
        for cls in info["classShare"]:
            if cls in data["excludedClasses"]:
                problems.append(f"{realm}: {cls} has a share")
            elif data["classes"].get(cls, {}).get("realm") != realm:
                problems.append(f"{realm}: {cls} is not a {realm} class")
    for cls, info in data["classes"].items():
        if cls in data["excludedClasses"]:
            problems.append(f"{cls}: excluded class has builds")
        total = sum(b["weight"] for b in info["builds"])
        if abs(total - 1) > 0.005:
            problems.append(f"{cls}: weights sum to {total:.4f}")
        if cls not in data["realms"][info["realm"]]["classShare"]:
            problems.append(f"{cls}: no share in {info['realm']}")
        for line in info.get("autotrain", []):
            if line not in info["lines"]:
                problems.append(f"{cls}: autotrain line {line} is not a {cls} line")
        for b in info["builds"]:
            cost = build_cost(b["specs"], info.get("autotrain", []))
            if cost > info["pointBudget50"]:
                problems.append(f"{b['id']}: {cost} points, budget {info['pointBudget50']}")
            for line, value in b["specs"].items():
                if line not in info["lines"]:
                    problems.append(f"{b['id']}: {line} is not a {cls} line")
                if not 1 <= value <= 50:
                    problems.append(f"{b['id']}: {line} {value}")
            if (b["specs"] != b["posted"]) != (b["change"] is not None):
                problems.append(f"{b['id']}: specs differ from posted without a change note (or the reverse)")
            if (b["change"] or "").startswith("trimmed") and \
                    build_cost(b["posted"], info.get("autotrain", [])) <= info["pointBudget50"]:
                problems.append(f"{b['id']}: trimmed, but the posted points fit the budget")
            # fully trained (owner 2026-11-17 18:59): what is left buys no further level in any of the build's lines
            autotrain = info.get("autotrain", [])
            left = points_left(b["specs"], info["pointBudget50"], autotrain)
            cheapest = cheapest_next_level(b["specs"], autotrain)
            if left >= 0 and cheapest is not None and left >= cheapest:
                problems.append(f"{b['id']}: {left} points left, the next level of a line costs {cheapest}")
            if b.get("pointsLeft") != left:
                problems.append(f"{b['id']}: pointsLeft {b.get('pointsLeft')} is not {left}")
            # a line comes in or goes out only with a cited template, or as an autotrain line at its free level
            for line in set(b["specs"]) ^ set(b["posted"]):
                free = line in autotrain and b["specs"].get(line, 0) >= AUTOTRAIN_LEVEL_50
                if not free and not b.get("citations"):
                    problems.append(f"{b['id']}: {line} added or dropped without a cited template")
            for line in autotrain:
                if b["specs"].get(line, 1) < AUTOTRAIN_LEVEL_50:
                    problems.append(f"{b['id']}: autotrain line {line} below its free {AUTOTRAIN_LEVEL_50}")
            # a fill of more than 10 levels needs a source (review of 1f3dcefe2)
            for line, level in b["specs"].items():
                start = b["posted"].get(line, 1)
                if line in autotrain:
                    start = max(start, AUTOTRAIN_LEVEL_50)   # the free levels are no fill
                if level - start > 10 and not b.get("citations"):
                    problems.append(f"{b['id']}: {line} +{level - start} levels without a source")
            if not b.get("sources"):
                problems.append(f"{b['id']}: no dated source for its posted points")
            if b.get("sourced") and not (b.get("citations") or b.get("postedSourced")):
                problems.append(f"{b['id']}: sourced without a source")
            if b["change"] is not None and not b.get("changeBasis"):
                problems.append(f"{b['id']}: changed without a basis")
    return problems


def render(data: dict) -> str:
    return json.dumps(data, indent=2, ensure_ascii=False) + "\n"


def render_table(data: dict) -> str:
    """docs/research/ERA-BUILDS-CHECK.md: every build against its sources (the WP0 check of docs/ERA-BUILDS-1.2.md)."""
    every = [(cls, b) for cls, c in data["classes"].items() for b in c["builds"]]
    wayback = [(cls, b) for cls, b in every if b["wayback"]]
    out = [
        "# Era builds: every build against its sources",
        "",
        "Generated by `source/tools/make_era_builds.py --table` from `bot-builds.draft.json` and the tool's Wayback table;",
        "`--check` fails when this file is out of date. Do not edit by hand.",
        "",
        f"- **{len(every)} builds, {len(data['classes'])} classes** ({sum(1 for _, b in every if b['new'])} added by the research). "
        "The draft's have dated FreddysHouse posts of 2002-2003 for",
        "  their posted points (not re-read: the forum is not reachable from the build environment).",
        f"- **Owner 2026-11-17 19:43:** the modern base (Uthgard, a 1.65 freeshard with this server's budget, and period guides",
        "  reposted there) tuned back to 1.65. "
        f"{sum(1 for _, b in every if b['base'].startswith('Uthgard'))} builds are the research's template; "
        f"{sum(1 for _, b in every if b['citations'])} cite a source (\"Cited\" below); "
        f"{sum(1 for _, b in every if not b['sourced'])} are not sourced (the Necromancer, low confidence).",
        "- Every autotrain line is at its free 12 at least, listed in the build or not.",
        f"- **{len(wayback)} builds are also in the Wayback scrape of 2026-11-13** "
        f"({sum(1 for _, b in wayback if b['check'].startswith('matches'))} match within 4 points on their top two lines); "
        f"**{len(every) - len(wayback)} are not** (\"-\" below).",
        f"- **{sum(1 for _, b in every if b['change'])} builds were changed to be fully trained** (owner, 2026-11-17 18:59): "
        "the posted points, and the rest of a level 50 player's points in the build's remainder line (or fewer points in",
        "  it, where the posts ask for more than a level 50 has). \"Left\" is what remains; it buys no further level in",
        "  any of the build's lines.",
        "- Budget and costs are a player's on this server: an autotrain line is free up to level 12 at 50.",
        "",
        "## The Wayback sources",
        "",
        "| Site | Page | Captured |",
        "|---|---|---|",
    ]
    for site, page, captured in sorted(set(WAYBACK_SOURCES.values()), key=lambda entry: entry[1]):
        out.append(f"| {site} | {page} | {captured} |")
    out += ["", "## The builds", "",
            "| Class | Build | Posted (dated posts, newest) | Trained | Left | Change | Cited | Wayback |",
            "|---|---|---|---|---|---|---|---|"]
    for cls, b in every:
        shows = "<br>".join(f"**{e['verdict']}**: {e['shows']} ({e['page'].split(' (')[0]}, captured {e['captured']})"
                            for e in b["wayback"]) or "-"
        newest = max(b["sourceDates"]) if b["sourceDates"] else "-"
        cited = "<br>".join(f"[{c['site']} {c['date']}]({c['url']}): {c['says']}" for c in b["citations"]) or "-"
        out.append(f"| {cls} | `{b['id']}` {b['label']} | {fmt(b['posted'])} ({b['forumMentions']}, {newest}) | "
                   f"{fmt(b['specs']) if b['change'] else 'as posted'} | {b['pointsLeft']} | {b['change'] or '-'} | "
                   f"{cited.replace('|', '/')} | {shows.replace('|', '/')} |")
    out += ["", "## Not in the Wayback scrape", "",
            "Builds with dated FreddysHouse posts only, by class: " +
            "; ".join(f"{cls} ({', '.join(b['label'] for c2, b in every if c2 == cls and not b['wayback'])})"
                      for cls in data["classes"] if any(c2 == cls and not b["wayback"] for c2, b in every)) + ".",
            ""]
    return "\n".join(out)


def summary(data: dict) -> list[str]:
    lines = []
    for realm, info in data["realms"].items():
        lines.append(f"{realm}: " + ", ".join(f"{c} {e['share'] * 100:g}" for c, e in info["classShare"].items()))
    builds = sum(len(c["builds"]) for c in data["classes"].values())
    every = [b for c in data["classes"].values() for b in c["builds"]]
    research = sum(1 for b in every if b["base"].startswith("Uthgard") and not b["new"])
    new = sum(1 for b in every if b["new"])
    changed = sum(1 for b in every if b["change"])
    sourced = sum(1 for b in every if b["sourced"])
    cited = sum(1 for b in every if b["citations"])
    left = [b["pointsLeft"] for b in every]
    lines.append(f"{len(data['classes'])} classes, {builds} builds ({new} new): {sourced} sourced ({cited} with a modern or "
                 f"period citation), {builds - sourced} not; {research} are the research's template, {changed} differ "
                 f"from their posted points ({sum(1 for b in every if b['changeSourced'])} of them with a source), "
                 f"{builds - changed} as posted; points left {min(left)}-{max(left)}; sharesSourced 0/3, "
                 f"weightsSourced 0/{builds}; excluded: {', '.join(data['excludedClasses'])}")
    return lines


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--check", action="store_true", help="fail if the data file is out of date or invalid")
    parser.add_argument("--table", action="store_true", help="write the per-build source check (ERA-BUILDS-CHECK.md)")
    args = parser.parse_args(argv)
    data, changes = build_data(json.loads(DRAFT.read_text(encoding="utf-8")))
    problems = validate(data)
    if problems:
        print("\n".join(f"INVALID {p}" for p in problems))
        return 1
    text = render(data)
    if args.check:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current != text:
            print(f"{OUT.relative_to(REPO)} is not what the draft and the decisions give: run make_era_builds.py")
            return 1
        if not CHECK_DOC.exists() or CHECK_DOC.read_text(encoding="utf-8") != render_table(data):
            print(f"{CHECK_DOC.relative_to(REPO)} is out of date: run make_era_builds.py --table")
            return 1
        print("\n".join(summary(data)))
        return 0
    if args.table:
        CHECK_DOC.write_text(render_table(data), encoding="utf-8", newline="\n")
        print(f"wrote {CHECK_DOC.relative_to(REPO)}")
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print("\n".join(changes))
    print("\n".join(summary(data)))
    print(f"wrote {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
