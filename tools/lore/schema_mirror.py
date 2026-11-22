"""A Python mirror of LoreBible.Validate's problem checks (not its warnings) and its parse checks, so the C# schema test
(UT_LoreBible.EveryLoreFilePassesTheSchema) can be run without dotnet. Run from the repo root: python3 tools/lore/schema_mirror.py .
Keep it in step with LoreBible.cs when its rules change; CI's C# test stays the authority."""
import json, os, re, sys, glob, itertools
from collections import Counter, defaultdict

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
LD = os.path.join(ROOT, "source/server/GameServer/bots/autonomous/data/lore")
P = []
def need(ok, msg):
    if not ok: P.append(msg)

REALMS = ["Albion", "Midgard", "Hibernia", "Neutral"]
PROV = ["mythic", "game", "legend", "myth", "original"]
SLOTS = ["realm", "raidLeader", "minutes"]
MECH = ["teleport", "throw", "jail", "pull", "barrier", "glare", "stare", "stun", "stomp", "breath", "messengers", "retreat",
        "safeSpots", "berserk", "scream", "healed", "twinHeal"]
ID_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
KW_RE = re.compile(r"\[([^\[\]|]+)\]")
SLOT_RE = re.compile(r"\{([A-Za-z]+)\}")
TITLE_RE = re.compile(r"\b[A-Z][a-z]")
CHOICE_RE = re.compile(r"\[([^\[\]]*)\]")
LOC_KEYS = ["id", "name", "realm", "regions", "zones", "aliases", "summary", "facts", "story", "legend", "atmosphere", "botLines",
            "loremaster", "book", "bosses", "realmTakes", "era", "factions", "related", "chapters", "tier"]
BOSS_KEYS = ["id", "name", "catalogKey", "locationId", "realm", "aliases", "facts", "story", "legend", "yells", "atmosphere", "botLines",
             "loremasterTopic", "bookPage", "bookPages", "chronicle", "realmTakes", "era", "factions", "related", "npcNames", "tier",
             "chapters", "members"]

def obj(e, k):
    v = e.get(k) if isinstance(e, dict) else None
    return v if isinstance(v, dict) else {}
def s(e, k):
    v = e.get(k) if isinstance(e, dict) else None
    return v if isinstance(v, str) else ""
def unknown(e, known, where):
    if isinstance(e, dict):
        for k in e:
            if k not in known: P.append(f"{where}: unknown field \"{k}\"")
def strs(e, k, where):
    if not isinstance(e, dict) or e.get(k) is None: return []
    v = e[k]
    if not isinstance(v, list): P.append(f"{where}: \"{k}\" must be a list of strings"); return []
    out = []
    for x in v:
        if isinstance(x, str): out.append(x)
        else: P.append(f"{where}: \"{k}\" holds a non-string")
    return out
def intv(e, k, where, fb=0):
    if not isinstance(e, dict) or k not in e: return fb
    v = e[k]
    if isinstance(v, int) and not isinstance(v, bool) and -2**31 <= v < 2**31: return v
    P.append(f"{where}: \"{k}\" must be a whole number"); return fb
def ushorts(e, k, where, mn=1):
    if not isinstance(e, dict) or k not in e: return []
    v = e[k]
    if not isinstance(v, list): P.append(f"{where}: \"{k}\" must be a list of numbers"); return []
    out = []
    for x in v:
        if isinstance(x, int) and mn <= x <= 65535: out.append(x)
        else: P.append(f"{where}: \"{k}\" holds {x}")
    return out
def objects(e, k, where):
    if not isinstance(e, dict) or e.get(k) is None: return []
    v = e[k]
    if not isinstance(v, list): P.append(f"{where}: \"{k}\" must be a list"); return []
    out = []
    for x in v:
        if isinstance(x, dict): out.append(x)
        else: P.append(f"{where}: \"{k}\" holds a non-object")
    return out
def facts(e, where):
    if not isinstance(e, dict) or "facts" not in e: return []
    v = e["facts"]
    if not isinstance(v, list): P.append(f"{where}: \"facts\" must be a list"); return []
    for f in v: unknown(f, ["text", "provenance", "source"], where + " facts")
    return [(s(f, "text"), s(f, "provenance"), s(f, "source")) for f in v]
def phases(e, where):
    if not isinstance(e, dict) or "phases" not in e: return []
    v = e["phases"]
    if not isinstance(v, list): P.append(f"{where}: \"phases\" must be a list"); return []
    for p in v: unknown(p, ["trigger", "text"], where + " phases")
    return [(s(p, "trigger"), s(p, "text")) for p in v]
def chapters(root, f):
    cs = objects(root, "chapters", f)
    for c in cs: unknown(c, ["title", "text"], f + " chapter")
    return [(s(c, "title"), s(c, "text")) for c in cs]
def pages(root, name, f):
    v = root.get(name)
    if isinstance(v, list): return [p for p in strs(root, name, f) if p.strip()]
    return [v] if isinstance(v, str) and v else []
def related(root, f):
    rs = objects(root, "related", f)
    for r in rs: unknown(r, ["id", "relation"], f + " related")
    return [(s(r, "id"), s(r, "relation")) for r in rs]

def dupcheck_pairs(pairs):
    return pairs
def loremaster(m, f, raw_topics_pairs):
    if not isinstance(m, dict): return None
    unknown(m, ["intro", "topics"], f + " loremaster")
    topics = {}
    t = m.get("topics")
    if t is not None:
        if not isinstance(t, dict): P.append(f"{f}: loremaster.topics must be an object")
        else:
            seen = set()
            for k, v in raw_topics_pairs.get(id(t), list(t.items())):
                if not isinstance(v, str): P.append(f"{f}: loremaster topic \"{k}\" must be text")
                elif k.lower() in seen: P.append(f"{f}: loremaster topic \"{k}\" is there twice")
                else: seen.add(k.lower()); topics[k] = v
    return (s(m, "intro"), topics)

TAKE_REALMS = ["albion", "midgard", "hibernia"]
def takes(root, f, botkeys, pairs):
    out = {}
    a = root.get("realmTakes")
    if a is None: return out
    if not isinstance(a, dict): P.append(f"{f}: realmTakes must be an object"); return out
    for name, e in a.items():
        key = name.strip().lower(); where = f"{f} realmTakes.{name}"
        if key not in TAKE_REALMS: P.append(f"{f}: realmTakes \"{name}\" is not albion, midgard or hibernia"); continue
        if key in out: P.append(f"{f}: realmTakes \"{name}\" is there twice"); continue
        if not isinstance(e, dict): P.append(f"{where}: must be an object"); continue
        unknown(e, ["story", "legend", "botLines", "loremaster", "loremasterTopic"], where)
        bot = obj(e, "botLines"); unknown(bot, botkeys, where + " botLines")
        top = obj(e, "loremasterTopic"); unknown(top, ["keyword", "text"], where + " loremasterTopic")
        out[key] = dict(story=s(e, "story"), legend=s(e, "legend"),
                        bot={k: strs(bot, k, where) for k in ["lore", "rumour", "tell", "briefing", "aftermathWin", "aftermathLoss"]},
                        lm=loremaster(obj(e, "loremaster") or None, where, pairs), kw=s(top, "keyword"), kt=s(top, "text"))
    return out

def tryparse(t):
    if not t or not t.strip(): return "empty line"
    d = 0
    for c in t:
        if c == "[":
            d += 1
            if d > 1: return "nested ["
        if c == "]":
            d -= 1
            if d < 0: return "] without ["
    if d: return "[ without ]"
    for m in CHOICE_RE.finditer(t):
        if "|" not in m.group(1): return f"[{m.group(1)}] has no |"
    return None
def tidy(t):
    t = re.sub(r"\s+", " ", t).strip()
    return t.replace(" ,", ",").replace(" ?", "?").replace(" !", "!").replace(" .", ".")
def expand(t):
    ms = CHOICE_RE.findall(t)
    if not ms: return [tidy(t)]
    groups = [m.split("|") for m in ms]
    out = set()
    for combo in itertools.islice(itertools.product(*groups), 5000):   # all ways (a superset of the C# 24-sample)
        it = iter(combo)
        out.add(tidy(CHOICE_RE.sub(lambda _: next(it), t)))
    return [x for x in out if x]

def check_facts(fs, f):
    need(len(fs) > 0, f"{f}: at least one fact")
    for text, prov, src in fs:
        need(text.strip() != "", f"{f}: a fact without text")
        need(prov in PROV, f"{f}: provenance \"{prov}\"")
        need(prov == "original" or src.strip() != "", f"{f}: a {prov} fact needs its source: {text[:60]}")
def check_chapters(cs, f):
    for t, x in cs:
        need(t.strip() != "", f"{f}: a chapter without a title")
        need(x.strip() != "", f"{f}: chapter \"{t}\" has no text")
        need(len(t) <= 60 and "[" not in t, f"{f}: chapter title \"{t}\" over 60 or has brackets")
    for k, n in Counter(t.strip().lower() for t, _ in cs).items():
        if n > 1: P.append(f"{f}: chapter title \"{k}\" is there twice")
def check_atmos(lines, what, f):
    for l in lines:
        need(l.strip() != "", f"{f}: an empty {what} line")
        need(len(l) <= 60, f"{f}: {what} line is {len(l)} characters (max 60): {l}")
        need("[" not in l and "{" not in l, f"{f}: {what} lines are said as written: {l}")
def check_bot(lines, what, f):
    for l in lines:
        e = tryparse(l)
        if e: P.append(f"{f}: {what} line: {e}: {l}"); continue
        need(not TITLE_RE.search(l), f"{f}: {what} lines are lower case: {l}")
        need(not SLOT_RE.search(l), f"{f}: {what} lines take no slots: {l}")
        for said in expand(l):
            need(len(said) <= 140, f"{f}: {what} line is {len(said)} characters as said (max 140): {said}")
def check_yells(lines, what, f):
    for l in lines:
        need(l.strip() != "", f"{f}: an empty {what} yell")
        need(len(l) <= 200, f"{f}: {what} yell is {len(l)} characters (max 200)")
        e = tryparse(l); need(e is None, f"{f}: {what} yell: {e}")
def parse_trigger(t):
    t = (t or "").replace(" ", "")
    tl = t.lower()
    if tl.startswith("health<="):
        try: n = int(t[8:]); return ("health", n) if 1 <= n <= 99 else None
        except ValueError: return None
    for k in ["addSpawn", "addsSpawn"]:
        if tl == k.lower(): return ("adds", 0)
    for k in ["formChange", "death", "wake", "partnerDead", "playerDeath", "orbDeath", "addsDead"]:
        if tl == k.lower(): return (k, 0)
    if tl in [m.lower() for m in MECH]: return ("mechanic", 0)
    return None

# Catalog (UT_LoreBible.CatalogFrom)
cat = json.load(open(os.path.join(ROOT, "docs/encounters/epic-encounters.json"), encoding="utf-8"))
ids = set(); adds = defaultdict(set); partners = defaultdict(set)
for e in cat["encounters"]:
    ids.add(e["id"]); cls = e["class"].lower()
    names = [x["class"].lower() for x in (e.get("adds") or [])] + [x.lower() for x in (e.get("spawnsEncounters") or [])]
    adds[cls] |= set(names)
    if e.get("partners"): partners[cls] |= {x.lower() for x in e["partners"]}
for e in cat["encounters"]:
    cls = e["class"]; bs = []
    if cls.endswith("Add") and cls[:-3].lower() in adds: bs.append(cls[:-3].lower())
    if cls.endswith("SpawnedAdd") and isinstance(e.get("partners"), list): bs += [x.lower() for x in e["partners"]]
    for b in bs: adds[b].add(cls.lower())
def classkey(k):
    if not k or not k.strip(): return ""
    return k[k.rfind("/") + 1:].strip().lower()
def knows(k): return bool(k and k.strip()) and (k.strip() in ids or classkey(k) in adds)

# Parse
locs, bosses, hists = [], [], []
for path in sorted(glob.glob(LD + "/**/*.json", recursive=True)):
    rel = os.path.relpath(path, LD).replace("\\", "/")
    raw = open(path, encoding="utf-8").read()
    pairs = {}
    def hook(pp):
        d = dict(pp); pairs[id(d)] = pp; return d
    try: root = json.loads(raw, object_pairs_hook=hook)
    except json.JSONDecodeError as ex: P.append(f"{rel}: not valid JSON ({ex})"); continue
    if not isinstance(root, dict): P.append(f"{rel}: not a JSON object"); continue
    f = rel
    if rel.startswith("locations/"):
        unknown(root, LOC_KEYS, f)
        atm = obj(root, "atmosphere"); unknown(atm, ["firstEntry", "areas", "raidForming"], f + " atmosphere")
        regions = ushorts(root, "regions", f)
        areas = []
        a = atm.get("areas")
        if a is not None:
            if not isinstance(a, list): P.append(f"{f}: atmosphere.areas must be a list")
            else:
                for ar in a:
                    w = f"{f} area \"{s(ar, 'name')}\""
                    unknown(ar, ["name", "region", "x", "y", "z", "radius", "lines"], w)
                    areas.append(dict(name=s(ar, "name"), region=intv(ar, "region", w, regions[0] if regions else 0),
                                      x=intv(ar, "x", w), y=intv(ar, "y", w), z=intv(ar, "z", w), radius=intv(ar, "radius", w), lines=strs(ar, "lines", w)))
        bot = obj(root, "botLines"); unknown(bot, ["lore", "rumour", "tell"], f + " botLines")
        lm = loremaster(root.get("loremaster") if isinstance(root.get("loremaster"), dict) else None, f, pairs)
        book = obj(root, "book"); unknown(book, ["title", "pages"], f + " book")
        locs.append(dict(f=f, id=s(root, "id"), name=s(root, "name"), realm=s(root, "realm"), regions=regions, summary=s(root, "summary"),
                         facts=facts(root, f), first=strs(atm, "firstEntry", f), areas=areas, lore=strs(bot, "lore", f), rumour=strs(bot, "rumour", f),
                         tell=strs(bot, "tell", f), lm=lm, book=(s(book, "title"), strs(book, "pages", f)) if isinstance(root.get("book"), dict) else None,
                         bosses=strs(root, "bosses", f), zones=ushorts(root, "zones", f, 0), raid=strs(atm, "raidForming", f), aliases=strs(root, "aliases", f),
                         takes=takes(root, f, ["lore", "rumour", "tell"], pairs), chapters=chapters(root, f), tier=s(root, "tier"), era=s(root, "era"),
                         factions=strs(root, "factions", f), related=related(root, f)))
    elif rel.startswith("bosses/"):
        unknown(root, BOSS_KEYS, f)
        y = obj(root, "yells"); unknown(y, ["aggro", "phases", "death", "slay"], f + " yells")
        atm = obj(root, "atmosphere"); unknown(atm, ["lairEntry", "phases", "realmFirstKill", "wake"], f + " atmosphere")
        bot = obj(root, "botLines"); unknown(bot, ["briefing", "aftermathWin", "aftermathLoss", "rumour"], f + " botLines")
        top = obj(root, "loremasterTopic"); unknown(top, ["keyword", "text"], f + " loremasterTopic")
        chron = obj(root, "chronicle"); unknown(chron, ["win", "loss"], f + " chronicle")
        members = []
        if "members" in root:
            if not isinstance(root["members"], list): P.append(f"{f}: members must be a list")
            else:
                for m in root["members"]:
                    if not isinstance(m, dict): P.append(f"{f}: each member must be an object"); continue
                    w = f"{f} member \"{s(m, 'name')}\""
                    unknown(m, ["name", "level", "post", "story", "yells", "atmosphere", "loremasterTopic"], w)
                    my = obj(m, "yells"); unknown(my, ["aggro", "death", "slay"], w + " yells")
                    ma = obj(m, "atmosphere"); unknown(ma, ["lairEntry"], w + " atmosphere")
                    mt = obj(m, "loremasterTopic"); unknown(mt, ["keyword", "text"], w + " loremasterTopic")
                    members.append(dict(name=s(m, "name"), aggro=strs(my, "aggro", w), death=strs(my, "death", w), slay=strs(my, "slay", w),
                                        lair=strs(ma, "lairEntry", w), kw=s(mt, "keyword"), kt=s(mt, "text")))
        bosses.append(dict(f=f, id=s(root, "id"), name=s(root, "name"), cat=s(root, "catalogKey"), loc=s(root, "locationId"), realm=s(root, "realm"),
                           facts=facts(root, f), aggro=strs(y, "aggro", f), yph=phases(y, f + " yells"), death=strs(y, "death", f), slay=strs(y, "slay", f),
                           lair=strs(atm, "lairEntry", f), aph=phases(atm, f + " atmosphere"), rfk=s(atm, "realmFirstKill"), wake=s(atm, "wake"),
                           briefing=strs(bot, "briefing", f), win=strs(bot, "aftermathWin", f), loss=strs(bot, "aftermathLoss", f), rumour=strs(bot, "rumour", f),
                           kw=s(top, "keyword"), kt=s(top, "text"), cwin=s(chron, "win"), closs=s(chron, "loss"), aliases=strs(root, "aliases", f),
                           takes=takes(root, f, ["briefing", "aftermathWin", "aftermathLoss", "rumour"], pairs), npc=strs(root, "npcNames", f),
                           chapters=chapters(root, f), pages=pages(root, "bookPage", f) + pages(root, "bookPages", f), tier=s(root, "tier"),
                           era=s(root, "era"), factions=strs(root, "factions", f), related=related(root, f), members=members))
    elif rel.startswith("history/"):
        unknown(root, ["id", "realm", "title", "intro", "summary", "eras", "factions"], f)
        eras = []
        for e in objects(root, "eras", f):
            w = f"{f} era \"{s(e, 'id')}\""
            unknown(e, ["id", "order", "name", "when", "summary", "text", "events", "facts", "places", "bosses"], w)
            evs, fs = [], []
            for v in objects(e, "events", w) + objects(e, "facts", w):
                if "provenance" in v:
                    unknown(v, ["text", "provenance", "source"], w + " fact"); fs.append((s(v, "text"), s(v, "provenance"), s(v, "source")))
                else:
                    unknown(v, ["name", "text", "places", "bosses"], w + " event"); evs.append((s(v, "name"), s(v, "text"), strs(v, "places", w), strs(v, "bosses", w)))
            intv(e, "order", w)
            eras.append(dict(id=s(e, "id"), name=s(e, "name"), summary=s(e, "summary"), text=s(e, "text"), events=evs, facts=fs,
                             places=strs(e, "places", w), bosses=strs(e, "bosses", w)))
        facs = []
        for e in objects(root, "factions", f):
            w = f"{f} faction \"{s(e, 'id')}\""
            unknown(e, ["id", "name", "realm", "summary", "text", "places", "bosses", "related"], w)
            facs.append(dict(id=s(e, "id"), name=s(e, "name"), summary=s(e, "summary"), text=s(e, "text"), related=related(e, w)))
        hid = s(root, "id") or s(root, "realm").lower()
        hists.append(dict(f=f, id=hid, realm=s(root, "realm"), eras=eras, factions=facs))
    else:
        P.append(f"{rel}: lore files go in locations/, bosses/ or history/")

# Validate
def check_id(i, f, folder):
    need(bool(ID_RE.match(i or "")), f"{f}: id \"{i}\" must be lower-case kebab")
    need(f == f"{folder}/{i}.json", f"{f}: the file must be named {folder}/{i}.json")
loc_by = {l["id"].lower(): l for l in locs}
boss_by = {b["id"].lower(): b for b in bosses}
for k, n in Counter(l["id"].lower() for l in locs).items():
    if n > 1: P.append(f"location id \"{k}\" used twice")
bosskw = defaultdict(list)
for b in bosses:
    for k in [b["kw"]] + [t["kw"] for t in b["takes"].values()]:
        if k.strip(): bosskw[b["loc"].lower()].append(k)
def check_takes(tk, f):
    for r, t in tk.items():
        for fld, lines in t["bot"].items(): check_bot(lines, f"realmTakes.{r}.{fld}", f)
        need((t["kw"].strip() == "") == (t["kt"].strip() == ""), f"{f}: realmTakes.{r}.loremasterTopic needs both keyword and text")
for l in locs:
    f = l["f"]; check_id(l["id"], f, "locations")
    need(l["name"].strip() != "", f"{f}: a name"); need(l["realm"] in REALMS, f"{f}: realm")
    need(not l["zones"] or l["regions"], f"{f}: zones need the region")
    need(l["summary"].strip() != "", f"{f}: a summary")
    check_facts(l["facts"], f)
    need(len(l["first"]) > 0, f"{f}: at least one firstEntry line")
    check_atmos(l["first"], "firstEntry", f); check_atmos(l["raid"], "raidForming", f)
    for a in l["areas"]:
        need(a["name"].strip() != "", f"{f}: an area without a name")
        need(100 <= a["radius"] <= 30000, f"{f}: area \"{a['name']}\" radius {a['radius']}")
        need(a["x"] != 0 or a["y"] != 0, f"{f}: area \"{a['name']}\" has no x/y")
        need(a["region"] > 0 if not l["regions"] else a["region"] in l["regions"], f"{f}: area \"{a['name']}\" region {a['region']}")
        need(len(a["lines"]) > 0, f"{f}: area \"{a['name']}\" has no lines")
        check_atmos(a["lines"], f"area \"{a['name']}\"", f)
    check_bot(l["lore"], "lore", f); check_bot(l["rumour"], "rumour", f); check_bot(l["tell"], "tell", f)
    need(len(l["tell"]) > 0, f"{f}: at least one tell line")
    need(len(l["lore"]) + len(l["rumour"]) + len(l["tell"]) >= 3, f"{f}: at least 3 bot lines")
    if l["lm"] is None: P.append(f"{f}: a loremaster block")
    else:
        need(l["lm"][0].strip() != "", f"{f}: loremaster.intro")
        known = {k.lower() for k in l["lm"][1]} | {k.lower() for k in bosskw.get(l["id"].lower(), [])}
        tks = [t["lm"] for t in l["takes"].values() if t["lm"]]
        for t in tks: known |= {k.lower() for k in t[1]}
        for m in [l["lm"]] + tks:
            for text in [m[0]] + list(m[1].values()):
                for kw in KW_RE.findall(text or ""):
                    need(kw.strip().lower() in known, f"{f}: loremaster keyword [{kw}] has no topic")
    check_takes(l["takes"], f)
    need(l["tier"] == "" or l["tier"].lower() in ("epic", "minor", "major"), f"{f}: tier \"{l['tier']}\"")
    check_chapters(l["chapters"], f)
    if l["book"] is None: P.append(f"{f}: a book")
    else:
        need(l["book"][0].strip() != "", f"{f}: book.title"); need(len(l["book"][1]) > 0, f"{f}: at least one book page")
    for i in l["bosses"]:
        b = boss_by.get(i.lower())
        if b: need(b["loc"].lower() == l["id"].lower(), f"{f}: boss \"{i}\" says its locationId is \"{b['loc']}\"")
    need(len({x.lower() for x in l["bosses"]}) == len(l["bosses"]), f"{f}: a boss is listed twice")
for k, n in Counter(b["id"].lower() for b in bosses).items():
    if n > 1: P.append(f"boss id \"{k}\" used twice")
for k, n in Counter(classkey(b["cat"]) for b in bosses if classkey(b["cat"])).items():
    if n > 1: P.append(f"catalogKey class \"{k}\" used twice")
npcown = defaultdict(set)
for b in bosses:
    for n in b["npc"]:
        if n.strip(): npcown[n.strip()].add(b["id"])
for n, o in npcown.items():
    if len(o) > 1: P.append(f"npcNames \"{n}\" claimed by {sorted(o)}")
for b in bosses:
    f = b["f"]; check_id(b["id"], f, "bosses")
    need(b["name"].strip() != "", f"{f}: a name"); need(b["realm"] in REALMS, f"{f}: realm")
    need(b["cat"].strip() != "" or any(n.strip() for n in b["npc"]), f"{f}: a catalogKey or npcNames")
    for n in b["npc"]: need(n.strip() != "" and n == n.strip(), f"{f}: npcNames \"{n}\" must be exact")
    if b["cat"].strip(): need(knows(b["cat"]), f"{f}: catalogKey \"{b['cat']}\" not in the catalog")
    home = loc_by.get(b["loc"].lower())
    if home is None: P.append(f"{f}: locationId \"{b['loc']}\" has no file")
    else: need(home["realm"] == "Neutral" or home["realm"] == b["realm"], f"{f}: realm {b['realm']}, but its location is {home['realm']}")
    check_facts(b["facts"], f)
    check_yells(b["aggro"], "aggro", f); check_yells(b["death"], "death", f); check_yells(b["slay"], "slay", f)
    check_yells([t for _, t in b["yph"]], "phase", f)
    for what, ph in (("yells.phases", b["yph"]), ("atmosphere.phases", b["aph"])):
        for trig, text in ph:
            t = parse_trigger(trig)
            if t is None: P.append(f"{f}: {what} trigger \"{trig}\" is not valid"); continue
            need(text.strip() != "", f"{f}: {what} \"{trig}\" has no text")
            if t[0] == "adds": need(len(adds.get(classkey(b["cat"]), ())) > 0, f"{f}: {what} trigger \"{trig}\" needs adds; catalog lists none")
            if t[0] == "partnerDead": need(len(partners.get(classkey(b["cat"]), ())) > 0, f"{f}: {what} trigger \"{trig}\" needs partners")
    check_atmos(b["lair"], "lairEntry", f); check_atmos([t for _, t in b["aph"]], "atmosphere phase", f)
    check_atmos([b["rfk"]] if b["rfk"] else [], "realmFirstKill", f); check_atmos([b["wake"]] if b["wake"] else [], "wake", f)
    need(len(b["lair"]) + len(b["aph"]) + (1 if b["rfk"] else 0) + (1 if b["wake"] else 0) > 0, f"{f}: at least one atmosphere line")
    for k in ("briefing", "win", "loss", "rumour"): check_bot(b[k], k, f)
    check_takes(b["takes"], f)
    need(len(b["briefing"]) + len(b["win"]) + len(b["loss"]) + len(b["rumour"]) >= 3, f"{f}: at least 3 bot lines")
    need((b["kw"].strip() == "") == (b["kt"].strip() == ""), f"{f}: loremasterTopic needs both")
    need(b["tier"] == "" or b["tier"].lower() in ("minor", "epic", "order", "major"), f"{f}: tier \"{b['tier']}\"")
    need(not b["members"] or b["tier"].lower() == "order", f"{f}: members belong to an order file")
    for m in b["members"]:
        mf = f"{f} member \"{m['name']}\""
        need(m["name"].strip() != "" and m["name"] in b["npc"], f"{mf}: name must be one of npcNames")
        check_yells(m["aggro"], "aggro", mf); check_yells(m["death"], "death", mf); check_yells(m["slay"], "slay", mf)
        check_atmos(m["lair"], "lairEntry", mf)
        need((m["kw"].strip() == "") == (m["kt"].strip() == ""), f"{mf}: loremasterTopic needs both")
    for k, n in Counter(m["name"] for m in b["members"]).items():
        if n > 1: P.append(f"{f}: member \"{k}\" listed {n} times")
    check_chapters(b["chapters"], f)
    for text in (b["cwin"], b["closs"]):
        for sl in SLOT_RE.findall(text or ""): need(sl in SLOTS, f"{f}: chronicle slot {{{sl}}}")
for k, n in Counter(h["id"].lower() for h in hists).items():
    if n > 1: P.append(f"history id \"{k}\" used twice")
hid = {}
for h in hists:
    f = h["f"]; check_id(h["id"], f, "history")
    need(h["realm"] in REALMS, f"{f}: realm"); need(len(h["eras"]) > 0, f"{f}: at least one era")
    for i, name, what in [(e["id"], e["name"], "era") for e in h["eras"]] + [(x["id"], x["name"], "faction") for x in h["factions"]]:
        need(bool(ID_RE.match(i or "")), f"{f}: {what} id \"{i}\" must be kebab"); need(name.strip() != "", f"{f}: {what} \"{i}\" needs a name")
        key = "era:" + i if what == "era" else f"faction:{i}@{f}"
        if i:
            if key in hid: P.append(f"{f}: {what} id \"{i}\" also in {hid[key]}")
            else: hid[key] = f
        if what == "era" and i and ("faction-any:" + i) in hid: P.append(f"{f}: era id \"{i}\" is also a faction")
        if what == "faction": hid.setdefault("faction-any:" + i, f)
    for e in h["eras"]:
        for x in h["factions"]: need(e["id"].lower() != x["id"].lower(), f"{f}: \"{x['id']}\" both era and faction")
        need(e["text"].strip() != "" or e["summary"].strip() != "", f"{f}: era \"{e['id']}\" has no text")
        for text, prov, src in e["facts"]:
            need(prov in PROV, f"{f}: provenance \"{prov}\""); need(prov == "original" or src.strip(), f"{f}: a {prov} fact needs its source")
        for name, text, _, _ in e["events"]: need(name.strip() != "" and text.strip() != "", f"{f}: an event needs a name and text")
    for x in h["factions"]: need(x["text"].strip() != "" or x["summary"].strip() != "", f"{f}: faction \"{x['id']}\" has no text")
eras = {e["id"].lower() for h in hists for e in h["eras"]}
facs = {x["id"].lower() for h in hists for x in h["factions"]}
hrealms = {h["realm"].lower() for h in hists}
def links(f, realm, era, factions, rel, self):
    if era.strip() and era.lower() not in eras and realm.lower() in hrealms: P.append(f"{f}: era \"{era}\" is in no history file")
    for x in factions:
        if x.lower() not in facs and realm.lower() in hrealms: P.append(f"{f}: faction \"{x}\" is in no history file")
    for i, r in rel:
        need(r.strip() != "", f"{f}: related \"{i}\" needs a relation"); need(i.lower() != self.lower(), f"{f}: related to itself")
for l in locs: links(l["f"], l["realm"], l["era"], l["factions"], l["related"], l["id"])
for b in bosses:
    home = loc_by.get(b["loc"].lower())
    links(b["f"], home["realm"] if home else b["realm"], b["era"], b["factions"], b["related"], b["id"])

print(f"schema mirror: {len(locs)} locations, {len(bosses)} bosses, {len(hists)} history files, {len(P)} problem(s)")
for p in P: print("PROBLEM", p)
sys.exit(1 if P else 0)
