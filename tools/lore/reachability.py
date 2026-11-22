#!/usr/bin/env python3
"""Which visible lore fields the engine can show, and through which code path (1.2.1).

For every player-visible field of the lore files (source/server/GameServer/bots/autonomous/data/lore) it names the code
that reads it and checks the per-entry conditions the engine applies, mirrored here without dotnet:

  - location firstEntry: LoreAtmosphere.Poll shows it when LoreEntryRules.LocationAt returns the file for some zone
    (zones from source/server/GameServer/scripts/dbupdater/insert/Zones.xml; a region it does not list counts as one
    unknown zone), or, since 1.2.1, as an overview (LoreEntryRules.NeverHere/OverviewsAt) on walking into a region it
    covers;
  - location areas[].lines: LoreAtmosphere.Poll by the area's region (its own or the file's first; none: never);
  - location book: sold by the loremasters and read in the codex (LoreBooks.Pages) always; it also drops
    (LoreBookDrops.Near) only in a region the file lists, alone there or near one of its areas of that region;
  - boss yells: LoreBossWatch, when BossOf finds the boss (a script class that exists, or npcNames) and the script does not
    already speak on that trigger (LoreBossWatch.ScriptSpeaks);
  - boss phases (yells.phases, atmosphere.phases): health, death and playerDeath once the boss is found; adds and
    partner triggers only with an encounter_adds.json entry; wake only for the three dragons that call
    LoreAtmosphere.DragonWakes; mechanic triggers as mechanic_trigger_coverage.py finds them; formChange is counted
    as possible (a model change cannot be checked here);
  - boss wake: LoreAtmosphere.DragonWakes, the three dragon scripts only;
  - members of an order file: yells and lair lines since 1.2.1 (Boss.AggroFor and the rest); post, story and topic are
    read by nothing.
Every other visible field has one reader, given in FIELDS with an anchor (a file and a token) that must still be in the
code; a missing anchor makes the field "reader not found".

  python3 tools/lore/reachability.py             print the summary
  python3 tools/lore/reachability.py --write     also write docs/1.2.1/REACHABILITY.md
  python3 tools/lore/reachability.py --json      everything, for machines
  python3 tools/lore/reachability.py --check     exit 1 if an unreachable entry is not in KNOWN_UNREACHABLE's counts

Tests only: this reads the code as text; nothing here was seen in game.
"""
import collections, glob, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
LORE = os.path.join(ROOT, "source/server/GameServer/bots/autonomous/data/lore")
SERVER = os.path.join(ROOT, "source/server/GameServer")
ZONES_XML = os.path.join(SERVER, "scripts/dbupdater/insert/Zones.xml")
ADDS_JSON = os.path.join(SERVER, "bots/autonomous/data/encounter_adds.json")
DOC = os.path.join(ROOT, "docs/1.2.1/REACHABILITY.md")
sys.path.insert(0, HERE)

DRAGON_CLASSES = {"albgolestandt", "midgjalpinulva", "hibcuuldurach"}  # the scripts that call LoreAtmosphere.DragonWakes
REALMS = ["Albion", "Midgard", "Hibernia", "Neutral"]

# (kind, field, reader, anchor file under source/server/GameServer, anchor token)
FIELDS = [
    ("location", "summary", "the /lore codex, the loremasters' place list, the book's delve", "lorechannels/LoreCodex.cs", "Summary"),
    ("location", "story", "the /lore codex, the loremasters", "lorechannels/LoreCodex.cs", "Story"),
    ("location", "legend", "the /lore codex (\"What is said\")", "lorechannels/LoreCodex.cs", "Legend"),
    ("location", "chapters", "the /lore codex and the loremasters' chapter keywords", "lorechannels/LoremasterDialogue.cs", "Chapters"),
    ("location", "loremaster", "the loremasters' intro and topics", "lorechannels/LoremasterDialogue.cs", "Topics"),
    ("location", "book", "lore books: the loremasters' sale and the codex (LoreBooks.Pages); drops near the place", "lorechannels/LoreBooks.cs", "Pages(ILoreSource"),
    ("location", "firstEntry", "LoreAtmosphere.Poll: LocationAt, or an overview's region entry", "bots/autonomous/LoreAtmosphere.cs", "OverviewsAt"),
    ("location", "areas.lines", "LoreAtmosphere.Poll: AreasInRegion and InArea", "bots/autonomous/LoreAtmosphere.cs", "AreasInRegion"),
    ("location", "raidForming", "LoreAtmosphere.RaidForming, from AutonomousRealmRaid when a realm raid forms", "bots/autonomous/AutonomousRealmRaid.cs", "LoreAtmosphere.RaidForming"),
    ("location", "botLines.lore", "LoreChat: the ambient lore corpus and placed lore", "bots/autonomous/LoreChat.cs", "LoreFor"),
    ("location", "botLines.rumour", "LoreChat: the ambient lore corpus and placed lore", "bots/autonomous/LoreChat.cs", "RumourFor"),
    ("location", "botLines.tell", "LoreChat.TryAnswer / LoreBible.TryAnswer (\"whats in ...?\")", "bots/autonomous/LoreChat.cs", "TellFor"),
    ("location", "realmTakes", "each realm's codex, loremasters and bots (ForRealm, TakeFor)", "lorechannels/LoreChannelData.cs", "ForRealm"),
    ("boss", "story", "the /lore codex, the Bestiary, the loremasters", "lorechannels/LoreCodex.cs", "Story"),
    ("boss", "legend", "the codex and the Bestiary (\"What the raid leaders say\")", "lorechannels/LoreHerald.cs", "Legend"),
    ("boss", "chapters", "the /lore codex and the loremasters' chapter keywords", "lorechannels/LoremasterDialogue.cs", "Chapters"),
    ("boss", "loremasterTopic", "the loremasters' boss topics", "lorechannels/LoremasterDialogue.cs", "Topic"),
    ("boss", "bookPage", "its place's book (LoreBooks.Pages: \"Of <boss>. ...\")", "lorechannels/LoreBooks.cs", "BookPages"),
    ("boss", "chronicle", "the Herald's chronicle (LoreHerald)", "bots/autonomous/LoreBibleSource.cs", "ChronicleWin"),
    ("boss", "yells", "LoreBossWatch: aggro on attack, death on dying, slay on a player's death", "bots/autonomous/LoreBossWatch.cs", "AggroFor"),
    ("boss", "phases", "LoreBossWatch: health, adds, partner, event and mechanic triggers", "bots/autonomous/LoreBossWatch.cs", "PhaseYells"),
    ("boss", "lairEntry", "LoreAtmosphere.Poll: a found boss within 1,500 units", "bots/autonomous/LoreAtmosphere.cs", "LairEntryFor"),
    ("boss", "realmFirstKill", "LoreAtmosphere.FirstKill, from LoreFirstKills", "bots/autonomous/LoreAtmosphere.cs", "RealmFirstKill"),
    ("boss", "wake", "LoreAtmosphere.DragonWakes (the three dragons)", "bots/autonomous/LoreAtmosphere.cs", "DragonWakes"),
    ("boss", "botLines.briefing", "LoreChat: the raid leader's briefing", "bots/autonomous/LoreChat.cs", "BriefingFor"),
    ("boss", "botLines.aftermath", "LoreChat: a veteran after the raid", "bots/autonomous/LoreChat.cs", "AftermathFor"),
    ("boss", "botLines.rumour", "LoreChat: the ambient lore corpus; AutonomousTales", "bots/autonomous/LoreChat.cs", "boss.Rumour"),
    ("boss", "realmTakes", "each realm's codex, loremasters and bots", "lorechannels/LoreChannelData.cs", "ForRealm"),
    ("boss", "members.yells", "LoreBossWatch by the member's spawn name (1.2.1)", "bots/autonomous/LoreBossWatch.cs", "AggroFor"),
    ("boss", "members.lairEntry", "LoreAtmosphere.Poll by the member's spawn name (1.2.1)", "bots/autonomous/LoreAtmosphere.cs", "LairEntryFor"),
    ("boss", "members.post", None, None, None),
    ("boss", "members.story", None, None, None),
    ("boss", "members.loremasterTopic", None, None, None),
    ("history", "intro", "the /lore codex history and the loremasters' era answers", "lorechannels/LoreCodex.cs", "Intro"),
    ("history", "eras", "the codex's eras and the loremasters (name, summary, text, events, facts' text)", "bots/autonomous/LoreBibleSource.cs", "LoreEraText"),
    ("history", "factions", "the codex's factions and the loremasters (name, summary, text)", "bots/autonomous/LoreBibleSource.cs", "LoreFactionText"),
]

# Unreachable entries accepted for now, by (kind, field): the count may only go down (--check). Lower it when fixed.
KNOWN_UNREACHABLE = {
    ("boss", "yells"): 176,                    # the script speaks on that trigger itself (ScriptSpeaks), on purpose
    ("boss", "wake"): 5,                       # non-dragons: beliathan, mokerkialfi, nosdoden, olcasgean, xanxicar
    ("boss", "phases"): 2,                     # olcasgean's two "wake" phase lines
    ("boss", "members.post"): 76,              # the Darkness Falls orders' members: read by no code yet
    ("boss", "members.story"): 76,
    ("boss", "members.loremasterTopic"): 76,
}

# What is left, and why it was not fixed here (written into the report).
NOTES = [
    "**Boss yells the script already speaks** (176 lines in 38 files): `LoreBossWatch` stays quiet on a trigger the boss's "
    "own script speaks on (`ScriptSpeaks`), so the two never talk over each other. These lines are codex-only. Wiring them "
    "means taking the script's own lines out, boss by boss: not small.",
    "**`wake` on five bosses that are not dragons** (beliathan, mokerkialfi, nosdoden, olcasgean, xanxicar; and olcasgean's "
    "two `wake` phase lines): only the three dragon scripts call `LoreAtmosphere.DragonWakes`. Each needs its own script "
    "hook (Beliathan's spawn when the princes die, Olcasgean's waking): not small.",
    "**Order members' post, story and loremaster topic** (76 members of chthonic-knights, inquisitors, lieutenants and "
    "mutilators): `LoreBible.Member` is parsed and checked but no channel reads it. Their yells and lair lines are spoken "
    "since 1.2.1. Showing the rest needs the lore channels' boss record to carry members (codex and loremaster topics).",
    "**Two books that never drop** (`proving-fields`, `shrouded-isles`): they list no regions, and the regions their areas "
    "name belong to the field and isle files, whose books drop there. Sold by the loremasters, so still readable.",
    "**`hibernia-wilds` and `hy-brasil`**: LORE.md said their first entry shows on coming into the open world, but zone "
    "files claim every zone of regions 200 and 181 in `Zones.xml`, so `LocationAt` never returned them. Since 1.2.1 they "
    "are overviews.",
]


def read_text(path):
    with open(path, encoding="utf-8-sig", errors="replace") as fh:
        return fh.read()


def read_json(path):
    with open(path, encoding="utf-8-sig") as fh:
        return json.load(fh)


def load(kind):
    out = []
    for f in sorted(glob.glob(os.path.join(LORE, kind, "*.json"))):
        d = read_json(f)
        d["_file"] = os.path.relpath(f, LORE).replace(os.sep, "/")
        out.append(d)
    return out


def zones_by_region():
    text = read_text(ZONES_XML)
    out = collections.defaultdict(list)
    for z, r in re.findall(r"<ZoneID>(\d+)</ZoneID>\s*<RegionID>(\d+)</RegionID>", text):
        out[int(r)].append(int(z))
    return out


def script_classes():
    names = set()
    for f in glob.glob(os.path.join(SERVER, "**", "*.cs"), recursive=True):
        names.update(n.lower() for n in re.findall(r"\bclass\s+(\w+)", read_text(f)))
    return names


def script_speaks():
    text = read_text(os.path.join(SERVER, "bots/autonomous/LoreBossWatch.cs"))
    block = text[text.index("ScriptSpeaks = new"):]
    block = block[:block.index("};")]
    return {k: set(re.findall(r'"(\w+)"', v)) for k, v in re.findall(r'\["(\w+)"\]\s*=\s*\[([^\]]*)\]', block)}


def anchor_ok(path, token):
    p = os.path.join(SERVER, path)
    return os.path.exists(p) and token in read_text(p)


def strs(v):
    return [x for x in v if isinstance(x, str) and x.strip()] if isinstance(v, list) else []


def words(t):
    return len(t.split())


# ---- the engine's rules, mirrored ------------------------------------------------------------------------------
def areas_of(loc):
    regions = loc.get("regions") or []
    out = []
    for a in ((loc.get("atmosphere") or {}).get("areas") or []):
        r = a.get("region", regions[0] if regions else 0)
        out.append(dict(a, region=r or 0))
    return out


def has_coords(loc):
    return any(a.get("x") or a.get("y") for a in areas_of(loc))


def location_at(locs, region, zone):
    """LoreEntryRules.LocationAt."""
    here = [l for l in locs if region in (l.get("regions") or [])]
    by_zone = sorted((l for l in here if zone in (l.get("zones") or [])), key=lambda l: (len(l["zones"]), l["id"]))
    if by_zone:
        return by_zone[0]["id"]
    rest = sorted((l for l in here if not l.get("zones")),
                  key=lambda l: (len(l.get("regions") or []), not has_coords(l), l["id"]))
    return rest[0]["id"] if rest else None


def regions_covered(loc):
    return sorted({r for r in (loc.get("regions") or [])} | {a["region"] for a in areas_of(loc) if a["region"]})


def shown_ids(locs, zones):
    """The ids LocationAt returns for some zone of some region a file lists (a region with no zones known: one unknown zone)."""
    shown = set()
    for r in {r for l in locs for r in (l.get("regions") or [])}:
        for z in zones.get(r) or [-1]:
            i = location_at(locs, r, z)
            if i:
                shown.add(i)
    return shown


def never_here(locs, loc, zones):
    """LoreEntryRules.NeverHere."""
    if loc.get("zones") or not strs((loc.get("atmosphere") or {}).get("firstEntry")):
        return False
    for r in set(loc.get("regions") or []):
        zs = zones.get(r) or []
        if not zs or any(location_at(locs, r, z) == loc["id"] for z in zs):
            return False
    return True


def book_drops(locs, loc):
    """Whether LoreBookDrops.Near can ever return this file: some region it lists where it is the only owner, or where it
    has an area of that region (z 0 is any height since 1.2.1)."""
    book = loc.get("book") or {}
    if not (book.get("title") and strs(book.get("pages"))):
        return False
    for r in loc.get("regions") or []:
        owners = [l for l in locs if r in (l.get("regions") or [])]
        if len(owners) == 1 or any(a["region"] in (0, r) and a.get("radius") for a in areas_of(loc)):
            return True
    return False


def trigger_kind(t):
    t = (t or "").replace(" ", "")
    m = re.fullmatch(r"health<=(\d+)", t, re.I)
    if m:
        return "health" if 0 < int(m.group(1)) < 100 else "bad"
    low = t.lower()
    table = {"addsspawn": "adds", "addspawn": "adds", "orbdeath": "adds", "addsdead": "adds", "partnerdead": "partner",
             "formchange": "form", "death": "event", "playerdeath": "event", "wake": "wake"}
    return table.get(low, "mechanic")


# ---- the report -------------------------------------------------------------------------------------------------
def build():
    locs, bosses, hist = load("locations"), load("bosses"), load("history")
    zones = zones_by_region()
    classes = script_classes()
    speaks = script_speaks()
    adds = read_json(ADDS_JSON)
    adds_map = {k.lower() for k in adds.get("adds", {})}
    partners_map = {k.lower() for k in adds.get("partners", {})}
    import mechanic_trigger_coverage as mtc
    fires = {(r["file"], r["trigger"].lower()): r["fires"] for r in mtc.build()}
    loc_by_id = {l["id"]: l for l in locs}
    shown = shown_ids(locs, zones)

    rows = []  # {realm, kind, field, file, n, words, ok, why}

    def add(realm, kind, field, file, texts, ok, why=""):
        texts = [t for t in texts if isinstance(t, str) and t.strip()]
        if texts:
            rows.append({"realm": realm if realm in REALMS else "Neutral", "kind": kind, "field": field, "file": file,
                         "n": len(texts), "words": sum(map(words, texts)), "ok": ok, "why": why})

    for l in locs:
        realm, f = l.get("realm"), l["_file"]
        add(realm, "location", "summary", f, [l.get("summary")], True)
        add(realm, "location", "story", f, [l.get("story")], True)
        add(realm, "location", "legend", f, [l.get("legend")], True)
        add(realm, "location", "chapters", f, [c.get("text") for c in l.get("chapters") or []] + [c.get("title") for c in l.get("chapters") or []], True)
        lm = l.get("loremaster") or {}
        add(realm, "location", "loremaster", f, [lm.get("intro")] + list((lm.get("topics") or {}).values()), True)
        book = l.get("book") or {}
        add(realm, "location", "book", f, [book.get("title")] + strs(book.get("pages")), True)
        atm = l.get("atmosphere") or {}
        fe = strs(atm.get("firstEntry"))
        if l["id"] in shown:
            add(realm, "location", "firstEntry", f, fe, True, "LocationAt")
        elif never_here(locs, l, zones) and regions_covered(l):
            add(realm, "location", "firstEntry", f, fe, True, "overview: regions " + ", ".join(map(str, regions_covered(l))))
        else:
            add(realm, "location", "firstEntry", f, fe, False, "LocationAt never returns it and it covers no region")
        for a in areas_of(l):
            add(realm, "location", "areas.lines", f, strs(a.get("lines")), bool(a["region"]), "" if a["region"] else f"area \"{a.get('name')}\" has no region")
        add(realm, "location", "raidForming", f, strs(atm.get("raidForming")), True, "a realm raid on its region or name")
        bl = l.get("botLines") or {}
        for k in ("lore", "rumour", "tell"):
            add(realm, "location", "botLines." + k, f, strs(bl.get(k)), True)
        for key, take in (l.get("realmTakes") or {}).items():
            tb = take.get("botLines") or {}
            tl = take.get("loremaster") or {}
            add(realm, "location", "realmTakes", f, [take.get("story"), take.get("legend"), tl.get("intro")] +
                list((tl.get("topics") or {}).values()) + sum((strs(tb.get(k)) for k in ("lore", "rumour", "tell")), []), True)

    for b in bosses:
        realm, f = b.get("realm"), b["_file"]
        key = (b.get("catalogKey") or "").strip()
        cls = key.split("/")[-1].lower() if key else ""
        by_class = bool(cls) and cls in classes
        found = by_class or bool(strs(b.get("npcNames")))
        find_why = "" if found else ("no script class " + cls if cls else "no catalogKey or npcNames")
        add(realm, "boss", "story", f, [b.get("story")], True)
        add(realm, "boss", "legend", f, [b.get("legend")], True)
        add(realm, "boss", "chapters", f, [c.get("text") for c in b.get("chapters") or []] + [c.get("title") for c in b.get("chapters") or []], True)
        t = b.get("loremasterTopic") or {}
        add(realm, "boss", "loremasterTopic", f, [t.get("text")], True)
        pages = [b["bookPage"]] if isinstance(b.get("bookPage"), str) else strs(b.get("bookPage"))
        pages += [b["bookPages"]] if isinstance(b.get("bookPages"), str) else strs(b.get("bookPages"))
        home = loc_by_id.get(b.get("locationId")) or {}
        has_book = bool((home.get("book") or {}).get("title") and strs((home.get("book") or {}).get("pages")))
        add(realm, "boss", "bookPage", f, pages, has_book, "" if has_book else f"its place {b.get('locationId')} has no book")
        ch = b.get("chronicle") or {}
        add(realm, "boss", "chronicle", f, [ch.get("win"), ch.get("loss")], True)
        yells = b.get("yells") or {}
        for trig in ("aggro", "death", "slay"):
            spoken = cls in speaks and trig in speaks[cls]
            ok = found and not spoken
            add(realm, "boss", "yells", f, strs(yells.get(trig)), ok,
                find_why if not found else (f"the script speaks on {trig} itself (ScriptSpeaks)" if spoken else ""))
        atm = b.get("atmosphere") or {}
        for section, src in (("yells", yells.get("phases")), ("atmosphere", atm.get("phases"))):
            for p in src or []:
                if not isinstance(p, dict):
                    continue
                kind = trigger_kind(p.get("trigger"))
                why = ""
                if not found:
                    ok, why = False, find_why
                elif kind in ("health", "event", "form"):
                    ok = True
                elif kind == "adds":
                    ok = cls in adds_map
                    why = "" if ok else "no encounter_adds.json \"adds\" entry for " + (cls or "(no class)")
                elif kind == "partner":
                    ok = cls in partners_map
                    why = "" if ok else "no encounter_adds.json \"partners\" entry for " + (cls or "(no class)")
                elif kind == "wake":
                    ok = cls in DRAGON_CLASSES
                    why = "" if ok else "wake: only the three dragons call DragonWakes"
                elif kind == "mechanic":
                    ok = fires.get((b["id"], (p.get("trigger") or "").replace(" ", "").lower()), False)
                    why = "" if ok else f"mechanic \"{p.get('trigger')}\" not announced by its script"
                else:
                    ok, why = False, f"trigger \"{p.get('trigger')}\" does not parse"
                add(realm, "boss", "phases", f, [p.get("text")], ok, why)
        add(realm, "boss", "lairEntry", f, strs(atm.get("lairEntry")), found, find_why)
        add(realm, "boss", "realmFirstKill", f, [atm.get("realmFirstKill")], found, find_why)
        add(realm, "boss", "wake", f, [atm.get("wake")], cls in DRAGON_CLASSES, "" if cls in DRAGON_CLASSES else "only the three dragons call DragonWakes")
        bl = b.get("botLines") or {}
        add(realm, "boss", "botLines.briefing", f, strs(bl.get("briefing")), True)
        add(realm, "boss", "botLines.aftermath", f, strs(bl.get("aftermathWin")) + strs(bl.get("aftermathLoss")), True)
        add(realm, "boss", "botLines.rumour", f, strs(bl.get("rumour")), True)
        for key2, take in (b.get("realmTakes") or {}).items():
            tb = take.get("botLines") or {}
            add(realm, "boss", "realmTakes", f, [take.get("story"), take.get("legend")] +
                sum((strs(tb.get(k)) for k in ("briefing", "aftermathWin", "aftermathLoss", "rumour")), []), True)
        for m in b.get("members") or []:
            my = m.get("yells") or {}
            add(realm, "boss", "members.yells", f, sum((strs(my.get(k)) for k in ("aggro", "death", "slay")), []), True)
            add(realm, "boss", "members.lairEntry", f, strs((m.get("atmosphere") or {}).get("lairEntry")), True)
            add(realm, "boss", "members.post", f, [m.get("post")], False, "read by no code (LoreBible.Member is only checked)")
            add(realm, "boss", "members.story", f, [m.get("story")], False, "read by no code (LoreBible.Member is only checked)")
            add(realm, "boss", "members.loremasterTopic", f, [(m.get("loremasterTopic") or {}).get("text")], False,
                "read by no code (LoreBible.Member is only checked)")

    for h in hist:
        realm, f = h.get("realm"), h["_file"]
        add(realm, "history", "intro", f, [h.get("summary"), h.get("intro"), h.get("title")], True)
        for e in h.get("eras") or []:
            add(realm, "history", "eras", f, [e.get("name"), e.get("summary"), e.get("text")] +
                [v.get("text") for v in e.get("events") or [] if isinstance(v, dict)], True)
        for x in h.get("factions") or []:
            add(realm, "history", "factions", f, [x.get("name"), x.get("summary"), x.get("text")], True)

    readers = {}
    for kind, field, reader, path, token in FIELDS:
        readers[(kind, field)] = {"reader": reader or "nothing", "found": bool(path) and anchor_ok(path, token),
                                  "anchor": f"{path}: {token}" if path else ""}
    for r in rows:
        rd = readers.get((r["kind"], r["field"]))
        if rd is None:
            r["ok"], r["why"] = False, "field not in FIELDS"
        elif rd["reader"] != "nothing" and not rd["found"]:
            r["ok"], r["why"] = False, "reader not found: " + rd["anchor"]
    drops = {l["id"]: book_drops(locs, l) for l in locs if (l.get("book") or {}).get("title")}
    return rows, readers, drops, locs


def summarise(rows):
    by = collections.defaultdict(lambda: [0, 0, 0, 0])  # n ok, n not, words ok, words not
    for r in rows:
        for key in ((r["realm"], r["kind"], r["field"]), (r["realm"], "*", "*"), ("*", r["kind"], r["field"]), ("*", "*", "*")):
            s = by[key]
            s[0 if r["ok"] else 1] += r["n"]
            s[2 if r["ok"] else 3] += r["words"]
    return by


def unreachable(rows):
    out = collections.Counter()
    for r in rows:
        if not r["ok"]:
            out[(r["kind"], r["field"])] += r["n"]
    return out


def render(rows, readers, drops):
    by = summarise(rows)
    out = ["# Lore reachability (1.2.1)", "",
           "Written by `python3 tools/lore/reachability.py --write`. For each visible lore field: the code that shows it, and",
           "how many entries the engine can show. The rules are mirrored from the code (see the script's docstring);",
           "**tests only**, nothing here was seen in game.", "",
           "## Totals per realm", "", "| Realm | entries shown | entries never shown | words shown | words never shown |", "|---|---|---|---|---|"]
    for realm in REALMS + ["*"]:
        s = by[(realm, "*", "*")]
        out.append(f"| {'All' if realm == '*' else realm} | {s[0]:,} | {s[1]:,} | {s[2]:,} | {s[3]:,} |")
    out += ["", "## Fields", "", "| Kind | Field | Read by | entries shown | never shown |", "|---|---|---|---|---|"]
    for kind, field, *_ in FIELDS:
        s = by[("*", kind, field)]
        rd = readers[(kind, field)]
        reader = rd["reader"] if rd["reader"] == "nothing" or rd["found"] else rd["reader"] + " (**reader not found**)"
        out.append(f"| {kind} | `{field}` | {reader} | {s[0]:,} | {s[1]:,} |")
    out += ["", "## Per realm and field (entries shown / never shown)", "",
            "| Kind | Field | " + " | ".join(REALMS) + " |", "|---|---|" + "---|" * len(REALMS)]
    for kind, field, *_ in FIELDS:
        cells = []
        for realm in REALMS:
            s = by[(realm, kind, field)]
            cells.append(f"{s[0]} / {s[1]}" if s[0] or s[1] else "-")
        out.append(f"| {kind} | `{field}` | " + " | ".join(cells) + " |")
    out += ["", "## What is left", ""] + [f"- {n}" for n in NOTES]
    out += ["", "## Never shown, file by file", ""]
    groups = collections.defaultdict(lambda: collections.defaultdict(int))
    for r in rows:
        if not r["ok"]:
            groups[(r["kind"], r["field"], r["why"])][r["file"]] += r["n"]
    if not groups:
        out.append("None.")
    for (kind, field, why), files in sorted(groups.items()):
        listed = ", ".join(f"`{f.rsplit('/', 1)[-1][:-5]}` ({n})" for f, n in sorted(files.items()))
        out.append(f"- **{kind} `{field}`**, {why}: {sum(files.values())} in {len(files)} file(s): {listed}")
    overviews = sorted((r["file"].rsplit("/", 1)[-1][:-5], r["why"], r["n"]) for r in rows if r["why"].startswith("overview"))
    out += ["", "## First entries shown only as overviews (1.2.1)", "",
            "`LoreEntryRules.LocationAt` returns these files for no zone, so before 1.2.1 their first-entry lines never showed.",
            "They now show once a day on walking into a region they cover (`LoreEntryRules.OverviewsAt`):", ""]
    out += [f"- `{i}` ({n} lines), {why}" for i, why, n in overviews] or ["None."]
    never = sorted(k for k, v in drops.items() if not v)
    out += ["", "## Lore books that never drop", "",
            "Every book is sold by the loremasters and read in the codex. `LoreBookDrops.Near` drops a place's book only in a",
            "region the file lists, where it is the only file or near one of its areas (twice the radius; `z: 0` any height since",
            f"1.2.1). {len(drops) - len(never)} of {len(drops)} books can drop; these {len(never)} cannot:", ""]
    out.append(", ".join(f"`{i}`" for i in never) if never else "None.")
    out.append("")
    return "\n".join(out)


def main(argv):
    rows, readers, drops, _ = build()
    if "--json" in argv:
        print(json.dumps({"rows": rows, "drops": drops}, indent=1))
        return 0
    by = summarise(rows)
    for realm in REALMS + ["*"]:
        s = by[(realm, "*", "*")]
        print(f"REACH {'All' if realm == '*' else realm}: {s[0]} entries shown, {s[1]} never shown ({s[3]} words)")
    bad = unreachable(rows)
    for (kind, field), n in sorted(bad.items()):
        print(f"REACH never shown: {kind} {field}: {n}")
    print(f"REACH books that never drop: {sum(1 for v in drops.values() if not v)} of {len(drops)}")
    if "--write" in argv:
        with open(DOC, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(render(rows, readers, drops))
        print("wrote", os.path.relpath(DOC, ROOT))
    if "--check" in argv:
        over = [(k, n) for k, n in bad.items() if n > KNOWN_UNREACHABLE.get(k, 0)]
        for (kind, field), n in over:
            print(f"REACH over cap: {kind} {field}: {n} never shown, cap {KNOWN_UNREACHABLE.get((kind, field), 0)}")
        return 1 if over else 0
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
