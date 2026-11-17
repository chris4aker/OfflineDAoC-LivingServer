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
  D7     the builds over the level-50 point budget trimmed, the 7 under it filled (SPEC_CHANGES). The review sheet
         counted every level of every line and trimmed 12. A player's autotrain lines are free up to level 12 at 50
         (AUTOTRAIN; owner 2026-11-17 18:11: "companions and bots should be the same as a player"), so 3 of the 12 fit
         as posted and are no longer trimmed, 2 are trimmed less, and 9 trims remain.
  D8     Wizard Fire 60 / Earth 25 / Ice PBAE 15 (WEIGHT_CHANGES).
  Owner rule (replaces D6): bots never play the Sluaghbinder, in any edition. It has no share and no builds here, and
         the loader refuses it (excludedClasses).
Every share and every weight is an estimate (sourced: false); only the spec points come from dated posts, and a trim or
fill says so in "change" (the posted points stay in "posted").
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DRAFT = REPO / "docs/research/bot-builds.draft.json"
OUT = REPO / "source/server/GameServer/bots/autonomous/data/era-builds-165.json"

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

# D7: (class, posted specs as a dict) -> (specs trained, why). The builds over the level-50 budget lose points from
# their lower lines; the 7 under it give the rest to a named line. Every one is the review sheet's line (marked *),
# except where a player's autotrain changes it: the two Minstrel builds keep the sheet's line and lose fewer points from it
# (the sheet had Stealth 50->31 and Slash 44->40), and Shadowblade Axe/Left Axe, Shadowblade Axe/CS and Ranger
# Archery/Pathfinding (the sheet's Left Axe 44->43, Critical Strike 39->38, Pathfinding 40->39) fit as posted.
SPEC_CHANGES = {
    ("Minstrel", "Instruments/Stealth/Slash"): ({"Instruments": 50, "Stealth": 33, "Slash": 29},
                                                 "trimmed to budget: Stealth 50->33"),
    ("Minstrel", "Instruments/Slash (no stealth)"): ({"Instruments": 44, "Slash": 42, "Stealth": 28},
                                                      "trimmed to budget: Slash 44->42"),
    ("Theurgist", "Earth (PBT)"): ({"Earth Magic": 50, "Cold Magic": 17, "Wind Magic": 11},
                                   "trimmed to budget: Cold Magic 26->17"),
    ("Wizard", "Ice PBAE"): ({"Cold Magic": 50, "Earth Magic": 14, "Fire Magic": 14},
                             "trimmed to budget: Earth Magic 20->14, Fire Magic 20->14"),
    ("Runemaster", "Suppression (PBT)"): ({"Suppression": 50, "Darkness": 20}, "trimmed to budget: Darkness 24->20"),
    ("Spiritmaster", "Suppression (PBAE)"): ({"Suppression": 49, "Darkness": 22}, "trimmed to budget: Darkness 25->22"),
    ("Champion", "Large Weapons/Valor/Blades"): ({"Large Weapons": 50, "Blades": 36, "Valor": 35, "Parry": 28},
                                                 "trimmed to budget: Valor 40->35, Blades 39->36"),
    ("Eldritch", "Mana (PBAE \"manabomb\")"): ({"Mana": 50, "Light": 19, "Void": 6}, "trimmed to budget: Light 20->19"),
    ("Mentalist", "Mentalism (heals)"): ({"Mentalism": 45, "Light": 21, "Mana": 21},
                                         "trimmed to budget: Light 25->21, Mana 25->21"),
    ("Paladin", "Two-handed chanter"): ({"Chants": 46, "Two Handed": 44, "Parry": 42}, "filled: rest to Parry (20->42)"),
    ("Mercenary", "Thrust dual wield"): ({"Thrust": 50, "Dual Wield": 50, "Parry": 28}, "filled: rest to Parry (10->28)"),
    ("Infiltrator", "Thrust/CS assassin"): ({"Thrust": 50, "Stealth": 46, "Envenom": 36, "Critical Strike": 34,
                                             "Dual Wield": 10}, "filled: rest to Stealth (35->46)"),
    ("Infiltrator", "Slash/CS assassin"): ({"Slash": 50, "Stealth": 46, "Envenom": 36, "Critical Strike": 34,
                                            "Dual Wield": 10}, "filled: rest to Stealth (35->46)"),
    ("Scout", "Melee scout"): ({"Thrust": 50, "Shields": 42, "Longbows": 34, "Stealth": 20},
                               "filled: rest to Longbows (new line, 34)"),
    ("Warrior", "Axe/Shield 50"): ({"Axe": 50, "Shields": 50, "Parry": 28}, "filled: rest to Parry (18->28)"),
    ("Warden", "Nurture/Regrowth"): ({"Nurture": 49, "Regrowth": 33, "Blades": 25, "Parry": 14},
                                     "filled: rest to Blades (new line, 25)"),
}

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
        for build, build_id in zip(builds, make_ids(cls, builds)):
            label = build["label"]
            posted = ordered(build["specs"])
            specs, change = posted, None
            if (cls, label) in SPEC_CHANGES:
                specs, change = SPEC_CHANGES[(cls, label)]
                specs = ordered(specs)
                changes.append(f"build {build_id}: {fmt(posted)} -> {fmt(specs)} ({change}; "
                               f"{build_cost(posted, autotrain)} -> {build_cost(specs, autotrain)} of {budget} points)")
            weight, weight_basis = build["weight"], "estimate from forumMentions and period wording"
            if (cls, label) in WEIGHT_CHANGES:
                weight, note = WEIGHT_CHANGES[(cls, label)]
                weight_basis = f"estimate, {note}"
                changes.append(f"weight {build_id}: {build['weight']:.2f} -> {weight:.2f} ({note})")
            out_builds.append({
                "id": build_id,
                "label": label,
                "role": build["role"],
                "use": build["use"],
                "family": family(cls, label),
                "weight": round(weight, 4),
                "weightSourced": False,
                "weightBasis": weight_basis,
                "specs": specs,
                "posted": posted,
                "change": change,
                "specsSourced": bool(build.get("sourced", False)) and change is None,
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
                     "spec points come from dated posts unless 'change' says they were trimmed or filled",
        "points": "a build fits when a level 50 player of the class can pay for it: every level of every line, less a "
                  "class's 'autotrain' lines up to level 12 (free for a player, 77 points a line)",
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
    return problems


def render(data: dict) -> str:
    return json.dumps(data, indent=2, ensure_ascii=False) + "\n"


def summary(data: dict) -> list[str]:
    lines = []
    for realm, info in data["realms"].items():
        lines.append(f"{realm}: " + ", ".join(f"{c} {e['share'] * 100:g}" for c, e in info["classShare"].items()))
    builds = sum(len(c["builds"]) for c in data["classes"].values())
    changed = sum(1 for c in data["classes"].values() for b in c["builds"] if b["change"])
    lines.append(f"{len(data['classes'])} classes, {builds} builds, {changed} changed by D7; sharesSourced 0/3, "
                 f"weightsSourced 0/{builds}; excluded: {', '.join(data['excludedClasses'])}")
    return lines


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--check", action="store_true", help="fail if the data file is out of date or invalid")
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
        print("\n".join(summary(data)))
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print("\n".join(changes))
    print("\n".join(summary(data)))
    print(f"wrote {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
