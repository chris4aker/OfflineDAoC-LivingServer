#!/usr/bin/env python3
"""Build the roster of real present-day NPCs (patch 1.65, 8 October 2003) for the lore's NPC pass, and the map of the
generic role stand-ins each lore file uses ("the druid", "the captain", ...) to a fitting real NPC.

    python3 tools/lore/build_npc_roster.py --opendaoc-db /path/to/OpenDAoC-Database/opendaoc-db-core \
        [--opendaoc-commit SHA] [--out tools/lore/npc_roster.json] [--role-map tools/lore/npc_role_map.json]
    python3 tools/lore/build_npc_roster.py --counts "Elowen" "Sefin"     bare-name mentions, as the cap counts them

Writes:
  npc_roster.json    one entry per NPC: name, role, realm, places [{region, zone, near, x, y}], sources [...], era
  npc_role_map.json  one entry per lore file: its stand-ins with counts and a suggested real NPC where one fits

Sources, in the order they are trusted (docs/1.2.1/CAST-*.md explain the choices):
  1. PERIOD: pages checked by hand with a dated period record (the Camelot Herald, or an Allakhazam page whose
     player comments date from before 8 October 2003). Listed in PERIOD_SOURCES below with their links.
  2. QUEST SCRIPTS: this repo's classic and Shrouded Isles quest scripts (source/server/GameServer/scripts/quests:
     the level-50 epics and the three Shrouded Isles quests) and QuestCaptains.cs (the Herald's battleground captains).
     The Atlas daily quests, bounties, reputation and Sluaghbinder scripts are later or ours and are not read.
  3. OPENDAOC DATABASE: the public OpenDAoC-Database dump (github.com/OpenDAoC/OpenDAoC-Database, mob.sql, keep.sql,
     area.sql, zones.sql, zonepoint.sql, regions.sql). Its NPCs are live captures, so an NPC there proves the NPC
     existed on live, not that it existed by 1.65: only the classic and Shrouded Isles regions are read, roles that came
     later (seals, bounty, market, account vaults, teleporters, the Catacombs classes) are dropped, and the era is
     "probable" unless a period source confirms it.
  4. LORE FACTS: a named NPC in a lore fact marked game, legend or mythic, with that fact's source (an Allakhazam,
     Prima or Herald page, or the world data). Living Server writing ("original") is never a source.
Keep lords are not stored in the database: the server names them from the keep (GuardLord SetName in
source/server/GameServer/keeps/Gameobjects/Guards/Lord.cs: Lord/Lady, Jarl, Chieftain/Chieftess + the keep's short name),
so they are derived from keep.sql and marked "probable" (the live 1.65 naming is not checked).

Era values: "confirmed" (a period source names the NPC), "probable" (live capture or quest script in a 1.65 region and
role, no dated source), never anything later: post-1.65 regions and roles are excluded, not listed."""
import argparse, collections, glob, json, math, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
from lore_text import lore_files, all_facts, LORE_DIR   # noqa: E402
import name_counts                                       # noqa: E402

QUEST_DIR = os.path.join(ROOT, "source", "server", "GameServer", "scripts", "quests")
QUEST_SCRIPTS = ["Albion/epic", "Midgard/epic", "Hibernia/epic",
                 "Albion/shrouded isles", "Midgard/shrouded isles", "Hibernia/shrouded isles"]
CAPTAINS_CS = os.path.join(ROOT, "source", "server", "GameServer", "quests", "QuestsMgr", "QuestCaptains.cs")
OPENDAOC_REPO = "https://github.com/OpenDAoC/OpenDAoC-Database"
REALMS = {1: "Albion", 2: "Midgard", 3: "Hibernia"}

# ---- the regions that existed by 1.65 (classic and Shrouded Isles), by the dump's region table -------------------------
# Housing (2, 102, 202) is in: Foundations went live in July 2003 (worthplaying.com, 2003-08-19: 20,000 houses sold "in
# less than six weeks"), so its regions, vault keepers and consignment merchants are 1.65. Left out: the tutorial (27), New Frontiers (163), Cathal Valley (165), the later battlegrounds and
# test regions (230-243), Trials of Atlantis (30-47, 70-91, 123-147 expansion 2) and Catacombs (expansion 3 and 4).
ERA_REGIONS = {
    # Albion: the realm, Camelot, the classic dungeons, Avalon and the Shrouded Isles dungeons
    1, 2, 10, 20, 21, 22, 23, 24, 50, 51, 60, 61, 62,
    # Midgard
    100, 101, 102, 125, 126, 127, 128, 129, 150, 151, 160, 161,
    # Hibernia
    200, 201, 202, 220, 221, 222, 223, 224, 180, 181, 190, 191,
    # shared: Darkness Falls and its halls, the classic battlegrounds
    244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 276, 277,
}
CAPITALS = {10: "Camelot", 101: "Jordheim", 201: "Tir na Nog"}
OUTDOOR = {1, 51, 100, 151, 200, 181}

# ---- roles ------------------------------------------------------------------------------------------------------------
LATER_CLASSES = {"Mauler", "Heretic", "Warlock", "Vampiir", "Bainshee"}    # Catacombs (2004) and later
LATER_CLASSTYPES = re.compile(r"Scripts\.|MarketExplorer|SealsMerchant|BountyMerchant|AccountVaultKeeper|EffectNPC|"
                              r"ItemModel|Teleporter|GuildRegistrar|NameRegistrar|EmblemNPC|Recharger|GameGuard$|Keeps\.")
LATER_GUILDS = re.compile(r"Seals|Diamond|Emerald|Sapphire|Medallion|Necklace|Realm Point|Bindstone|Reskin|"
                          r"Effect|Emissary|^Master Trainer$|Market|Teleporter|Account|Bounty|Registrar|Emblem", re.I)
# Market Explorers (the housing market search) came well after 1.65; the consignment merchants came with Foundations.
PLAYER_GUILDS = {"PWNOGRAPHY", "French Corporation", "Malafinja", "Martine Fait Du RvR", "Die Raecher von Hel"}
GENERIC = set("""Master Dyemaster the of Fletcher Guard Gothi Mistress Captain Lieutenant Barkeep Lady Sister Magus Lord Stor
Filidh Albion Magess Enchanter Commander Minstrel Gythja crier Crier Chieftain Hibernia Harper Midgard Apprentice Viking Bard
horse Hunter Bowman Siegemaster Sentinel Smith Guardian Piper Merchant Weaver Scryer Chief Father Trainer Flayer Armor
Sergeant Odin Thor Ymir Hel Modi Tyr Aesir Juton World Fall Young Sentry Decorator Weapons Knight Veteran Wizard Elementalist
Runes Huscarl Dreng Archer Keep Taxidermist Explorer Nightshade Eldritch Champion Troll Dwarven Alchemist Celtic Valkyn
Valkyrie Jarl Chieftess Armsman Armswoman Guardsman Local Dream Sage Kobold Arboreal Noble Elven Royal Mystic Frontier Stable
Squire General Scribe Stonemason Druid Skald Brother Sir Elder Steward Healer Seer Shaman Warden Ranger Hero Blademaster
Animist Valewalker Mentalist Cleric Friar Paladin Mercenary Infiltrator Scout Theurgist Sorcerer Cabalist Necromancer Reaver
Berserker Warrior Thane Savage Runemaster Spiritmaster Bonedancer Shadowblade Vendor Banker Hastener Town Village""".split())

def role_of(cls, guild, name):
    cls = (cls or "").split(".")[-1]
    guild = guild or ""
    m = re.match(r"(\w+)Trainer$", cls)
    if m: return f"{m.group(1)} trainer", "trainer"
    if guild.endswith("Trainer"): return guild[:-len(" Trainer")] + " trainer", "trainer"
    if cls == "GameHealer" or guild == "Healer": return "healer", "healer"
    if cls == "GameStableMaster" or guild == "Stable Master": return "stablemaster", "stablemaster"
    if cls == "Blacksmith" or guild == "Smith": return "smith", "smith"
    if cls.endswith("CraftingMaster") or cls in ("FletchingMaster", "AlchemistsMaster") or guild.endswith("Master"):
        return (guild or cls).replace("Crafting", "crafting ").lower(), "craft master"
    if cls == "Enchanter" or guild == "Enchanter": return "enchanter", "enchanter"
    if cls == "GameVaultKeeper" or guild in ("Vault Keeper", "Vault Banker"): return "vault keeper", "vault keeper"
    if cls == "GameHastener" or guild == "Hastener": return "hastener", "hastener"
    if cls == "GameMerchant" or "Merchant" in guild: return (guild or "merchant").lower(), "merchant"
    if re.match(r"(Captain|Lieutenant|Sergeant|Sentry|Commander)\b", name): return "guard officer", "captain"
    if guild: return guild.lower(), "townsfolk"
    return "townsfolk (named)", "townsfolk"

# ---- period sources checked by hand (2026-11-22) ------------------------------------------------------------------------
# Allakhazam quest pages whose player comments date from before 8 October 2003 show the quest, and its named NPCs, at 1.65.
AKZ_WAR_OF_OLD = ("https://camelot.allakhazam.com/quests.html?cquest=131",
                  "Allakhazam, 'A War of Old' (Midgard rogue epic, level 15): comments from Nov 2001 on")
AKZ_DESIRE = ("https://camelot.allakhazam.com/quests.html?cquest=709",
              "Allakhazam, 'The Desire of a God' (Midgard Seer epic, level 43): comments from Mar 2003 on")
AKZ_MOONSTONE = ("https://camelot.allakhazam.com/quests.html?cquest=299",
                 "Allakhazam, 'The Moonstone Twin' (Hibernian Path of Essence epic, level 50): comments from Aug 2002 on")
AKZ_BROKEN = ("https://camelot.allakhazam.com/db/quests.html?cquest=459",
              "Allakhazam, 'Symbol of the Broken' (Albion, level 45): comments from May 2003 on")
AKZ_AMREN = ("https://camelot.allakhazam.com/db/search.html?cmob=11736",
             "Allakhazam NPC page: upstairs in the Defenders of Albion building, Camelot; gives the Shrouded Isles "
             "Defenders epic steps (Island Holiday to General Vaul's Last Stand), so in the world from December 2002")
AKZ_SKALD = ("https://camelot.allakhazam.com/db/class.html?cclass=25",
             "Allakhazam, Skald class page: trainers by town (page updated 2011; with the live capture, probable)")
HERALD_BG = ("https://camelotherald.com/content/battleground-quest-guide",
             "Camelot Herald, Battleground Quest Guide (Thidranki: 'Speak with Captain Circa/Captain Cody/Captain Astridr')")
PERIOD_SOURCES = {
    ("Morlin Caan", "Midgard"): [AKZ_WAR_OF_OLD], ("Masrim", "Midgard"): [AKZ_WAR_OF_OLD],
    ("Scout Argyle", "Midgard"): [AKZ_WAR_OF_OLD],
    ("Inaksha", "Midgard"): [AKZ_DESIRE], ("Miri", "Midgard"): [AKZ_DESIRE],
    ("Ilisa", "Hibernia"): [AKZ_MOONSTONE], ("Lasairiona", "Hibernia"): [AKZ_MOONSTONE],
    ("Eli", "Hibernia"): [AKZ_MOONSTONE], ("Kern", "Hibernia"): [AKZ_MOONSTONE],
    ("Caolan", "Hibernia"): [AKZ_MOONSTONE], ("Sadhbh", "Hibernia"): [AKZ_MOONSTONE],
    ("Loremaster Alain", "Albion"): [AKZ_BROKEN],
    ("Sir Amren", "Albion"): [AKZ_AMREN],
    ("Captain Circa", "Albion"): [HERALD_BG], ("Captain Cody", "Hibernia"): [HERALD_BG],
    ("Captain Astridr", "Midgard"): [HERALD_BG],
}
PROBABLE_SOURCES = {(n, "Midgard"): [AKZ_SKALD] for n in ("Otik", "Regin", "Lalida", "Vanah", "Astra", "Leif", "Sven", "Eryk")}
PERIOD_PLACES = {   # NPCs a period page places but the dump does not hold
    ("Loremaster Alain", "Albion"): {"region": 10, "zone": "Camelot", "near": "Camelot"},
    ("Scout Argyle", "Midgard"): {"region": 100, "zone": None, "near": "outside Fort Atla"},
    ("Eli", "Hibernia"): {"region": 200, "zone": None, "near": "Connla"},
    ("Kern", "Hibernia"): {"region": 200, "zone": None, "near": "Innis Carthaig"},
    ("Caolan", "Hibernia"): {"region": 201, "zone": "Tir na Nog", "near": "Tir na Nog, by the vault"},
    ("Sadhbh", "Hibernia"): {"region": 201, "zone": "Tir na Nog", "near": "Tir na Nog"},
}
# Names a roster NPC shares with a legend the lore tells of (docs/1.2.1/HEROES.md): use with care, never as the legend.
LEGEND_CLASH_NOTE = "shares a name with a legend the lore tells of; never let the text make one the other"

# ---- SQL dump reader ----------------------------------------------------------------------------------------------------
def sql_rows(path):
    cols = None
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if line.startswith(("REPLACE INTO", "INSERT INTO")):
                cols = re.findall(r"`(\w+)`", line.split("VALUES")[0])[1:]
                continue
            s = line.strip()
            if cols and s.startswith("("):
                yield dict(zip(cols, _tuple(s)))

def _tuple(s):
    s = s.rstrip(",;")[1:-1]
    out, i, n = [], 0, len(s)
    while i < n:
        c = s[i]
        if c in " ,\t": i += 1; continue
        if c == "'":
            j, buf = i + 1, []
            while True:
                ch = s[j]
                if ch == "\\": buf.append({"n": "\n", "r": "\r", "t": "\t", "0": "\0"}.get(s[j + 1], s[j + 1])); j += 2; continue
                if ch == "'":
                    if j + 1 < n and s[j + 1] == "'": buf.append("'"); j += 2; continue
                    break
                buf.append(ch); j += 1
            out.append("".join(buf)); i = j + 1
        else:
            j = s.find(",", i); j = n if j < 0 else j
            v = s[i:j].strip()
            out.append(None if v == "NULL" else int(v) if re.fullmatch(r"-?\d+", v) else v); i = j
    return out

# ---- the world: regions, zones, towns, keeps, dungeon entrances ------------------------------------------------------------
class World:
    def __init__(self, db):
        self.region_name = {r["RegionID"]: r["Description"] for r in sql_rows(os.path.join(db, "regions.sql"))}
        self.zones = [z for z in sql_rows(os.path.join(db, "zones.sql"))]
        self.places = collections.defaultdict(list)       # region -> [(name, x, y)]
        for a in sql_rows(os.path.join(db, "area.sql")):
            if a["ClassType"] == "DOL.GS.Area+Circle" and a["Region"] in ERA_REGIONS:
                self.places[a["Region"]].append((a["Description"], a["X"], a["Y"]))
        self.keeps = []
        for k in sql_rows(os.path.join(db, "keep.sql")):
            if k["Region"] in (1, 100, 200) and k["OriginalRealm"] in REALMS and "Portal" not in k["Name"]:
                self.keeps.append(k)
                self.places[k["Region"]].append((k["Name"], k["X"], k["Y"]))
        self.entrance = {}                                # dungeon region -> (outdoor region, x, y)
        for zp in sql_rows(os.path.join(db, "zonepoint.sql")):
            if zp["SourceRegion"] in ERA_REGIONS and zp["TargetRegion"] in OUTDOOR | set(CAPITALS) \
                    and zp["SourceRegion"] not in OUTDOOR | set(CAPITALS):
                self.entrance.setdefault(zp["SourceRegion"], (zp["TargetRegion"], zp["TargetX"], zp["TargetY"]))

    def zone(self, region, x, y):
        for z in self.zones:
            if z["RegionID"] != region: continue
            x0, y0 = z["OffsetX"] * 8192, z["OffsetY"] * 8192
            if x0 <= x < x0 + z["Width"] * 8192 and y0 <= y < y0 + z["Height"] * 8192:
                return z["Name"]
        return None

    def zone_ids(self, region):
        return {z["ZoneID"]: z for z in self.zones if z["RegionID"] == region}

    def near(self, region, x, y):
        if region in CAPITALS: return CAPITALS[region]
        best = min(self.places.get(region, []), key=lambda p: (p[1] - x) ** 2 + (p[2] - y) ** 2, default=None)
        if best is None: return self.region_name.get(region)
        d = math.hypot(best[1] - x, best[2] - y)
        return best[0] if d < 12000 else f"between {best[0]} and the wilds ({int(d / 1000)}k from it)" if d < 40000 \
            else f"{self.zone(region, x, y) or self.region_name.get(region)} (no town near)"

    def place(self, region, x, y):
        return {"region": region, "zone": self.zone(region, x, y) or self.region_name.get(region),
                "near": self.near(region, x, y), "x": x, "y": y}

# ---- source 2: quest scripts ----------------------------------------------------------------------------------------------
def quest_script_npcs():
    out = []
    for sub in QUEST_SCRIPTS:
        for f in sorted(glob.glob(os.path.join(QUEST_DIR, sub, "*.cs"))):
            text = open(f, encoding="utf-8").read()
            rel = os.path.relpath(f, ROOT).replace(os.sep, "/")
            quest = re.search(r'questTitle\s*=\s*"([^"]+)"', text)
            for var, name in re.findall(r'\b(\w+)\.Name\s*=\s*"((?:[^"\\]|\\.)+)"', text):
                block = text[text.find(f'{var}.Name'):][:1500]
                realm = re.search(rf'{var}\.Realm\s*=\s*eRealm\.(\w+)', block)
                region = re.search(rf'{var}\.CurrentRegionID\s*=\s*(\d+)', block)
                if not realm or realm.group(1) not in REALMS.values() or not region: continue
                xy = [re.search(rf'{var}\.{a}\s*=\s*(\d+)', block) for a in ("X", "Y")]
                guild = re.search(rf'{var}\.GuildName\s*=\s*"([^"]*)"', block)
                out.append({"name": name.replace("\\'", "'"), "realm": realm.group(1), "region": int(region.group(1)),
                            "x": int(xy[0].group(1)) if xy[0] else None, "y": int(xy[1].group(1)) if xy[1] else None,
                            "guild": guild.group(1) if guild else "",
                            "source": {"kind": "quest script", "path": rel,
                                       "note": f"quest NPC{' of ' + repr(quest.group(1)) if quest else ''}"
                                               f" ({os.path.basename(os.path.dirname(f))})"}})
    return out

def herald_captains():
    text = open(CAPTAINS_CS, encoding="utf-8").read()
    out = []
    for realm, region, place, x, y, name in re.findall(
            r'new Post\(eRealm\.(\w+), (\w+), "([^"]+)", (\d+), (\d+), \d+, \d+,\s*"([^"]+)"', text):
        if region != "Thidranki": continue          # the Caledonia captains are new names, not period
        out.append({"name": name, "realm": realm, "region": 252, "x": int(x), "y": int(y), "guild": "Thidranki Garrison",
                    "source": {"kind": "quest script", "path": os.path.relpath(CAPTAINS_CS, ROOT).replace(os.sep, "/"),
                               "note": f"battleground quest captain at {place} (named from the Camelot Herald)"}})
    return out

# ---- source 3: the OpenDAoC dump ------------------------------------------------------------------------------------------
def proper(name):
    if not name or not name[0].isupper() or "'s" in name or name.endswith("'"): return False
    return any(w not in GENERIC and w.rstrip("s") not in GENERIC for w in re.findall(r"[A-Za-z']+", name))

def dump_npcs(db, commit):
    out = []
    for r in sql_rows(os.path.join(db, "mob.sql")):
        if r["Realm"] not in REALMS or r["Region"] not in ERA_REGIONS: continue
        cls, guild, name = r["ClassType"] or "", r["Guild"] or "", r["Name"]
        if LATER_CLASSTYPES.search(cls) or LATER_GUILDS.search(guild) or guild in PLAYER_GUILDS: continue
        if any(c in cls or c in guild for c in LATER_CLASSES): continue
        if not proper(name): continue
        out.append({"name": name, "realm": REALMS[r["Realm"]], "region": r["Region"], "x": r["X"], "y": r["Y"],
                    "cls": cls, "guild": guild,
                    "source": {"kind": "OpenDAoC database", "path": f"{OPENDAOC_REPO}/blob/{commit}/opendaoc-db-core/mob.sql",
                               "note": f"mob {r['Mob_ID']} ({cls.split('.')[-1]}{', ' + guild if guild else ''})"}})
    return out

def keep_lords(world, commit):
    out = []
    for k in world.keeps:
        name = k["Name"]
        if name.startswith(("Caer ", "Fort ")): short = name[5:]
        elif name.startswith("Castle "): short = name[7:]
        elif name.startswith("Dun "): short = {"Dun nGed": "Ged", "Dun da Behn": "Behn"}.get(name, name[4:])
        else: short = name.split(" ")[0]
        realm = REALMS[k["OriginalRealm"]]
        title = {"Albion": "Lord", "Midgard": "Jarl", "Hibernia": "Chieftain"}[realm]
        out.append({"name": f"{title} {short}", "realm": realm, "region": k["Region"], "x": k["X"], "y": k["Y"],
                    "role": f"keep lord of {name}" + (" (or Lady, by the lord's sex)" if realm == "Albion" else
                                                      " (or Chieftess)" if realm == "Hibernia" else ""),
                    "group": "keep lord",
                    "source": {"kind": "OpenDAoC database", "path": f"{OPENDAOC_REPO}/blob/{commit}/opendaoc-db-core/keep.sql",
                               "note": f"keep {k['KeepID']} {name}; the lord's name follows the server's rule "
                                       "(source/server/GameServer/keeps/Gameobjects/Guards/Lord.cs, SetName)"}})
    return out

# ---- source 4: lore facts --------------------------------------------------------------------------------------------------
def lore_facts():
    """[(file rel, realm, fact)] for every fact marked game, legend or mythic."""
    out = []
    for f in lore_files():
        d = json.load(open(f, encoding="utf-8"))
        rel = os.path.relpath(f, ROOT).replace(os.sep, "/")
        for x in all_facts(d):
            if x.get("provenance") in ("game", "legend", "mythic"):
                out.append((rel, d.get("realm"), x))
    return out

# ---- build ---------------------------------------------------------------------------------------------------------------
def build(db, commit):
    world = World(db)
    raw = quest_script_npcs() + herald_captains() + dump_npcs(db, commit) + keep_lords(world, commit)
    roster = {}
    for r in raw:
        key = (r["name"], r["realm"])
        e = roster.setdefault(key, {"name": r["name"], "realm": r["realm"], "role": None, "group": None,
                                    "places": [], "sources": [], "era": "probable", "notes": []})
        if r.get("role"): e["role"], e["group"] = r["role"], r["group"]
        elif e["role"] is None or e["group"] == "townsfolk":
            role, group = role_of(r.get("cls"), r.get("guild"), r["name"])
            if r["source"]["kind"] == "quest script" and group == "townsfolk":
                role, group = (r["guild"].lower() + ", " if r["guild"] else "") + "quest giver", "quest giver"
            if e["group"] in (None, "townsfolk") or group != "townsfolk": e["role"], e["group"] = role, group
        if r.get("x") is not None:
            p = world.place(r["region"], r["x"], r["y"])
            if not any(q["region"] == p["region"] and abs(q["x"] - p["x"]) < 2000 and abs(q["y"] - p["y"]) < 2000
                       for q in e["places"]):
                e["places"].append(p)
        if r["source"] not in e["sources"] and len(e["sources"]) < 6: e["sources"].append(r["source"])
    for key, links in list(PERIOD_SOURCES.items()) + list(PROBABLE_SOURCES.items()):
        e = roster.setdefault(key, {"name": key[0], "realm": key[1], "role": "quest NPC", "group": "quest giver",
                                    "places": [PERIOD_PLACES[key]] if key in PERIOD_PLACES else [], "sources": [],
                                    "era": "probable", "notes": []})
        for url, note in links:
            e["sources"].insert(0, {"kind": "period page" if key in PERIOD_SOURCES else "web page", "path": url, "note": note})
        if key in PERIOD_SOURCES: e["era"] = "confirmed"
    # lore facts: an NPC already listed gains the fact as a source; an honorific name only the facts know joins the roster
    facts = lore_facts()
    bosses = name_counts.boss_names()
    for rel, realm, x in facts:
        text = x.get("text", "")
        for (name, rrealm), e in roster.items():
            if rrealm == realm and len(name) > 4 and re.search(r"\b" + re.escape(name) + r"\b", text):
                s = {"kind": "lore fact", "path": rel,
                     "note": f"name match; {x['provenance']}: {x.get('source', '')[:160]}"}
                if s not in e["sources"] and sum(1 for q in e["sources"] if q["kind"] == "lore fact") < 3:
                    e["sources"].append(s)
                if x["provenance"] == "mythic": e["era"] = "confirmed"
        if realm not in REALMS.values(): continue
        for m in name_counts.NAME.finditer(text):
            name = re.sub(r"\s+the\s+\w+$", "", name_counts.person(m))
            if name in name_counts.COURT or (name, realm) in roster or not proper(name): continue
            if any(b == name or b.endswith(" " + name) for b in bosses): continue
            src = x.get("source", "")
            if not re.search(r"allakhazam|camelotherald|darkageofcamelot|prima|gamebanshee|world data|quest", src, re.I):
                continue
            roster[(name, realm)] = {"name": name, "realm": realm, "role": "named in a sourced lore fact",
                                     "group": "quest giver" if "quest" in (src + text).lower() else "townsfolk",
                                     "places": [{"region": None, "zone": None, "near": os.path.basename(rel)[:-5]}],
                                     "sources": [{"kind": "lore fact", "path": rel, "note": f"{x['provenance']}: {src[:160]}"}],
                                     "era": "confirmed" if x["provenance"] == "mythic" else "probable", "notes": []}
    legends = {w: where for w, where in name_counts.legend_names().items()
               if where in ("name_counts.HEROES", "HEROES.md roster")}
    legends.update({name_counts.fold(w): "name_counts.LEGEND_ANCHORS" for w in name_counts.LEGEND_ANCHORS})
    for (name, realm), e in roster.items():
        first = name_counts.fold(name.split()[-1] if len(name.split()) > 1 and name.split()[0] in GENERIC else name.split()[0])
        if first in legends and first not in name_counts.SHARED_GIVEN_NAMES:
            e["notes"].append(f"'{first}' {LEGEND_CLASH_NOTE} ({legends[first]})")
    out = sorted(roster.values(), key=lambda e: (e["realm"], e["group"] or "", e["name"]))
    return world, out

# ---- the role map ----------------------------------------------------------------------------------------------------------
STAND_INS = ["the druid", "the skald", "the priest", "the warden", "the healer", "the captain", "the marshal",
             "the elder", "the chieftain", "the steward", "the smith", "the third company"]
# Which roster groups and roles may stand in for each generic role, per realm (first match wins, nearest first).
FITS = {
    "the druid":    {"Hibernia": [("trainer", "Druid")], "*": []},
    "the skald":    {"Midgard": [("trainer", "Skald")], "*": []},
    "the priest":   {"Albion": [("trainer", "Cleric"), ("trainer", "Friar"), ("healer", "")],
                     "Midgard": [("trainer", "Healer"), ("trainer", "Shaman"), ("healer", "")],
                     "Hibernia": [("trainer", "Druid"), ("healer", "")]},
    "the warden":   {"Hibernia": [("trainer", "Warden"), ("keep lord", "")], "*": [("keep lord", ""), ("captain", "")]},
    "the healer":   {"Midgard": [("healer", ""), ("trainer", "Healer")], "*": [("healer", "")]},
    "the captain":  {"*": [("captain", ""), ("trainer", "Armsman"), ("keep lord", "")]},
    "the marshal":  {"*": [("keep lord", ""), ("captain", "")]},
    "the elder":    {"Midgard": [("trainer", "Seer"), ("trainer", "Runemaster")], "Hibernia": [("trainer", "Druid")],
                     "Albion": [("trainer", "Cleric")]},
    "the chieftain": {"Hibernia": [("keep lord", "")], "Midgard": [("keep lord", "")]},
    "the steward":  {"*": [("vault keeper", "")]},
    "the smith":    {"*": [("smith", ""), ("craft master", "weaponsmith"), ("craft master", "armorsmith")]},
    "the third company": {},
}
CAPITAL_OF = {"Albion": 10, "Midgard": 101, "Hibernia": 201}
LOCAL = 40000            # within this distance (same region) a suggestion is "local", beyond it "realm"

def file_anchor(world, d, locations):
    """(region, x, y) where a lore file's story stands: an area of the file (or of its location), a dungeon's entrance."""
    loc = d if "regions" in d else locations.get(d.get("locationId"), {})
    regions = loc.get("regions") or []
    areas = [a for a in (loc.get("atmosphere") or {}).get("areas") or [] if a.get("x")]
    if d is not loc:
        own = [a for a in areas if d.get("name", "").split()[0].lower() in (a.get("name") or "").lower()]
        areas = own or areas
    region = (areas[0].get("region") if areas else None) or (regions[0] if regions else None)
    if region is None: return None
    if areas:
        x, y = areas[0]["x"], areas[0]["y"]
    else:
        zs = world.zone_ids(region)
        z = zs.get((loc.get("zones") or [None])[0]) or (next(iter(zs.values())) if zs else None)
        if z is None: return (region, None, None)
        x, y = (z["OffsetX"] + z["Width"] / 2) * 8192, (z["OffsetY"] + z["Height"] / 2) * 8192
    if region in world.entrance:
        return world.entrance[region]
    return (region, x, y)

def suggest(world, roster, realm, stand_in, anchor):
    rules = FITS.get(stand_in, {})
    rules = rules.get(realm, rules.get("*", []))
    best = None
    for group, word in rules:
        cands = [e for e in roster if e["realm"] == realm and e["group"] == group and word.lower() in (e["role"] or "").lower()
                 and not e["notes"]]
        scored = []
        for e in cands:
            for p in e["places"]:
                if p.get("region") is None or p.get("x") is None: continue
                if anchor and anchor[0] == p["region"] and anchor[1] is not None:
                    scored.append((math.hypot(p["x"] - anchor[1], p["y"] - anchor[2]), e, p))
                elif p["region"] == CAPITAL_OF.get(realm):
                    scored.append((10 ** 7, e, p))     # the capital's trainer serves the whole realm
        if scored:
            dist, e, p = min(scored, key=lambda s: (s[0], s[1]["era"] != "confirmed", s[1]["name"]))
            fit = "local" if dist < LOCAL else "realm"
            best = {"name": e["name"], "role": e["role"], "near": p["near"], "fit": fit, "era": e["era"]}
            break
    return best

def role_map(world, roster):
    locations = {}
    for f in lore_files():
        d = json.load(open(f, encoding="utf-8"))
        if "regions" in d: locations[d["id"]] = d
    out = []
    pats = {s: re.compile(r"\b" + s + r"\b", re.I) for s in STAND_INS}
    for f in lore_files():
        d = json.load(open(f, encoding="utf-8"))
        rel = os.path.relpath(f, LORE_DIR).replace(os.sep, "/")
        own = " ".join([d.get("name") or ""] + list(d.get("aliases") or [])).lower()
        text = "\n".join(name_counts.strings(d))
        uses = {}
        for s, rx in pats.items():
            n = len(rx.findall(text))
            if not n or s[4:] in own: continue      # the boss itself ("the Warden of ...") is not a stand-in
            uses[s] = n
        if not uses: continue
        anchor = file_anchor(world, d, locations)
        realm = d.get("realm")
        entry = {"file": rel, "realm": realm, "anchor": None if not anchor else
                 {"region": anchor[0], "near": world.near(anchor[0], anchor[1], anchor[2]) if anchor[1] is not None else
                  world.region_name.get(anchor[0])}, "stand_ins": []}
        for s, n in sorted(uses.items(), key=lambda kv: -kv[1]):
            sug = suggest(world, roster, realm, s, anchor) if realm in REALMS.values() else None
            item = {"stand_in": s, "count": n, "suggested": sug}
            if s == "the third company": item["note"] = "a present-day company (Caer Sidi), not one person; keep the label"
            elif sug is None: item["note"] = "no real NPC fits this place and role; keep the role or a Living Server name"
            entry["stand_ins"].append(item)
        out.append(entry)
    return out

def bare_counts(names):
    """Mentions of each bare name in the narrative strings, the way name_counts counts the heroes (facts and titles out)."""
    c = collections.Counter(); files = collections.defaultdict(set)
    pats = {n: re.compile(r"\b" + re.escape(n) + r"\b") for n in names}
    for f in lore_files():
        d = json.load(open(f, encoding="utf-8"))
        for t in name_counts.strings(d):
            for n, rx in pats.items():
                k = len(rx.findall(t))
                if k: c[n] += k; files[n].add(os.path.basename(f)[:-5])
    return c, files

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opendaoc-db", help="the opendaoc-db-core folder of a clone of OpenDAoC-Database")
    ap.add_argument("--opendaoc-commit", default="master", help="the clone's commit, for the links")
    ap.add_argument("--out", default=os.path.join(HERE, "npc_roster.json"))
    ap.add_argument("--role-map", default=os.path.join(HERE, "npc_role_map.json"))
    ap.add_argument("--counts", nargs="+", help="print bare-name mention counts and exit")
    a = ap.parse_args()
    if a.counts:
        c, files = bare_counts(a.counts)
        for n in a.counts: print(f"{c[n]:4} {len(files[n]):3} files  {n}  ({', '.join(sorted(files[n]))})")
        return
    if not a.opendaoc_db: ap.error("--opendaoc-db is required")
    world, roster = build(a.opendaoc_db, a.opendaoc_commit)
    rmap = role_map(world, roster)
    by = collections.Counter(e["realm"] for e in roster)
    src = collections.Counter(s["kind"] for e in roster for s in {q["kind"]: q for q in e["sources"]}.values())
    meta = {"about": "Real present-day NPCs that existed by patch 1.65 (8 October 2003), for the lore's NPC pass. "
                     "Built by tools/lore/build_npc_roster.py; see its docstring for the sources and the era rules.",
            "opendaoc_database": f"{OPENDAOC_REPO} at {a.opendaoc_commit}",
            "counts": {"by_realm": dict(sorted(by.items())), "by_source_kind": dict(sorted(src.items())),
                       "by_era": dict(collections.Counter(e["era"] for e in roster))}}
    with open(a.out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump({**meta, "npcs": roster}, fh, indent=1, ensure_ascii=False); fh.write("\n")
    fits = collections.Counter((s["suggested"] or {}).get("fit", "none") for e in rmap for s in e["stand_ins"])
    with open(a.role_map, "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"about": "The generic role stand-ins of each lore file (narrative strings, facts and titles out, the boss "
                            "itself excluded) with a suggested real NPC from npc_roster.json. fit: local (same region, "
                            f"within {LOCAL} units of the place or its entrance), realm (the capital's trainer or the "
                            "nearest in the region). A suggestion is a starting point for the writer, not a ruling.",
                   "counts": {"files": len(rmap), "stand_in_uses": sum(len(e["stand_ins"]) for e in rmap),
                              "by_fit": dict(fits)}, "files": rmap}, fh, indent=1, ensure_ascii=False); fh.write("\n")
    print(json.dumps(meta["counts"]), json.dumps(dict(fits)))

if __name__ == "__main__":
    main()
