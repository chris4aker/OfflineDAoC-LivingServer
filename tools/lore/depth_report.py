"""A Python mirror of LoreBible.DepthReport() (source/server/GameServer/bots/autonomous/LoreBible.cs), so the depth floors of
LORE-1.2-DEPTH.md can be measured without dotnet, file by file.

    python3 tools/lore/depth_report.py                      the summary lines, as the C# prints them (LORE_DEPTH ...)
    python3 tools/lore/depth_report.py --files              and, per file, the floors it misses with the measured values
    python3 tools/lore/depth_report.py --files --kind major --realm Hibernia
    python3 tools/lore/depth_report.py --json               everything, for machines

Kinds (as DepthReport splits them): location (every location, epic and minor included, as the C# counts them; the story 500+ floor
skips epic locations, whose story is held to the epic 300-500 summary instead), major (a boss
neither minor nor epic: "order" files and an empty tier count here), minor, epic-location, epic-boss. --exclude-epic leaves
epic locations out of the location kind (the C# summary keeps them). --realm filters on the file's own "realm" field.
With a filter the summary lines count the filtered set only.

The reading follows LoreBible.Parse exactly: a field of the wrong JSON type reads as empty, Strs keeps only strings,
"bookPage" and "bookPages" each give one page (text) or a list of non-blank pages and are joined (BookPagesOf), loremaster
topics are counted once per case-insensitive keyword, and Words() splits on .NET whitespace. Keep it in step with
LoreBible.DepthReport when its floors change; the C# test (UT_LoreBible, LORE_DEPTH lines) stays the authority."""
import argparse, glob, json, os, sys, unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_LORE = os.path.join(HERE, "..", "..", "source/server/GameServer/bots/autonomous/data/lore")
REALMS = ["Albion", "Midgard", "Hibernia", "Neutral"]  # LoreBible.Realms
KINDS = ["location", "major", "minor", "epic-location", "epic-boss"]


# ---- .NET string helpers -------------------------------------------------------------------------------------------
def is_ws(c):
    """char.IsWhiteSpace: Zs, Zl, Zp, U+0009-U+000D, U+0085 (U+00A0 is Zs). Python's str.isspace also takes U+001C-U+001F."""
    return c in "\t\n\v\f\r\x85" or unicodedata.category(c) in ("Zs", "Zl", "Zp")


def blank(t):
    """string.IsNullOrWhiteSpace."""
    return t is None or all(is_ws(c) for c in t)


def words(t):
    """LoreBible.Words: text.Split(null, RemoveEmptyEntries).Length."""
    if blank(t):
        return 0
    n, inword = 0, False
    for c in t:
        if is_ws(c):
            inword = False
        elif not inword:
            n, inword = n + 1, True
    return n


def upper_key(k):
    """StringComparer.OrdinalIgnoreCase: char by char upper-invariant (simple case mapping)."""
    return "".join(u if len(u := c.upper()) == 1 else c for c in k)


# ---- LoreBible's JSON readers (Obj, Str, Strs, Objects, PagesIn) ---------------------------------------------------
class Pairs(list):
    """An object kept with its properties in order, duplicates included (JsonElement.EnumerateObject); lookups take the
    last duplicate, as JsonElement.TryGetProperty does."""
    def get(self, k, default=None):
        for name, v in reversed(self):
            if name == k:
                return v
        return default

    def __contains__(self, k):
        return any(name == k for name, _ in self)


def is_obj(v): return isinstance(v, Pairs)
def obj(e, k): return v if is_obj(e) and is_obj(v := e.get(k)) else None
def text(e, k): return v if is_obj(e) and isinstance(v := e.get(k), str) else ""
def strs(e, k): return [x for x in v if isinstance(x, str)] if is_obj(e) and isinstance(v := e.get(k), list) else []
def array(e, k): return v if is_obj(e) and isinstance(v := e.get(k), list) else []
def objects(e, k): return [x for x in array(e, k) if is_obj(x)]


def pages_in(root, k):
    v = root.get(k)
    if isinstance(v, list):
        return [p for p in strs(root, k) if not blank(p)]
    p = text(root, k)
    return [p] if len(p) > 0 else []


def book_pages_of(root):
    return pages_in(root, "bookPage") + pages_in(root, "bookPages")


def topic_count(master):
    """ParseLoremaster: a Dictionary(OrdinalIgnoreCase) filled with TryAdd from the string-valued topics."""
    if master is None:
        return None
    t = master.get("topics")
    if not is_obj(t):
        return 0
    return len({upper_key(name) for name, v in t if isinstance(v, str)})


# ---- Parse (LoreBible.Parse, ParseLocation, ParseBoss, ParseHistory: only what DepthReport reads) -------------------
def parse_location(root, f):
    atm = obj(root, "atmosphere")
    bot = obj(root, "botLines")
    book = obj(root, "book")
    areas = array(atm, "areas")  # every entry, object or not, as the C# loop over atmosphere.areas
    return dict(file=f, kind_of="location", id=text(root, "id"), name=text(root, "name"), realm=text(root, "realm"), tier=text(root, "tier"),
                story=text(root, "story"), legend=text(root, "legend"), facts=len(array(root, "facts")),
                firstEntry=len(strs(atm, "firstEntry")), areas=len(areas), lore=len(strs(bot, "lore")), rumour=len(strs(bot, "rumour")),
                tell=len(strs(bot, "tell")), topics=topic_count(obj(root, "loremaster")),
                book=None if book is None else strs(book, "pages"), era=text(root, "era"), factions=len(strs(root, "factions")),
                chapters=[text(c, "text") for c in objects(root, "chapters")])


def parse_boss(root, f):
    y, atm, bot = obj(root, "yells"), obj(root, "atmosphere"), obj(root, "botLines")
    top, chron = obj(root, "loremasterTopic"), obj(root, "chronicle")
    return dict(file=f, kind_of="boss", id=text(root, "id"), name=text(root, "name"), realm=text(root, "realm"), tier=text(root, "tier"),
                locationId=text(root, "locationId"), story=text(root, "story"), legend=text(root, "legend"), facts=len(array(root, "facts")),
                aggro=len(strs(y, "aggro")), death=len(strs(y, "death")), slay=len(strs(y, "slay")), lairEntry=len(strs(atm, "lairEntry")),
                realmFirstKill=text(atm, "realmFirstKill"), briefing=len(strs(bot, "briefing")), aftermathWin=len(strs(bot, "aftermathWin")),
                aftermathLoss=len(strs(bot, "aftermathLoss")), rumour=len(strs(bot, "rumour")), keyword=text(top, "keyword"),
                chronicleWin=text(chron, "win"), chronicleLoss=text(chron, "loss"), bookPages=book_pages_of(root), era=text(root, "era"),
                factions=len(strs(root, "factions")), related=len(objects(root, "related")),
                chapters=[text(c, "text") for c in objects(root, "chapters")])


def parse_history(root, f):
    realm = text(root, "realm")
    return dict(file=f, realm=realm, eras=len(objects(root, "eras")), factions=len(objects(root, "factions")))


def load(lore_dir):
    """Directory.GetFiles(dir, "*.json", AllDirectories) in ordinal order, then LoreBible.Parse. Returns (locations, bosses,
    histories, problems); a file that is not a JSON object is a problem and is skipped, as in the C#."""
    locs, bosses, hists, problems = [], [], [], []
    paths = sorted(glob.glob(os.path.join(lore_dir, "**", "*.json"), recursive=True))
    files = sorted(((os.path.relpath(p, lore_dir).replace("\\", "/"), p) for p in paths), key=lambda x: x[1])
    for rel, path in files:
        try:
            with open(path, encoding="utf-8-sig") as fh:  # File.ReadAllText drops a BOM
                root = json.loads(fh.read(), object_pairs_hook=Pairs)
        except (ValueError, UnicodeDecodeError) as ex:
            problems.append(f"{rel}: not valid JSON ({ex})")
            continue
        if not is_obj(root):
            problems.append(f"{rel}: not a JSON object")
        elif rel.startswith("locations/"):
            locs.append(parse_location(root, rel))
        elif rel.startswith("bosses/"):
            bosses.append(parse_boss(root, rel))
        elif rel.startswith("history/"):
            hists.append(parse_history(root, rel))
        else:
            problems.append(f"{rel}: lore files go in locations/, bosses/ or history/")
    return locs, bosses, hists, problems


# ---- The floors (LoreBible.DepthReport), each returning what is short ("story 212<500"), empty when the floor holds -----
def lt(what, value, floor): return [f"{what} {value}<{floor}"] if value < floor else []


def pages_short(pages, floor):
    short = [f"page {i + 1} {w}<{floor}" for i, p in enumerate(pages) if (w := words(p)) < floor]
    return short


LOCATION_FLOORS = [
    # An epic place's story is its short telling, held to the epic floor's 300-500 summary (owner 2026-11-22).
    ("story 500+ words", lambda l: [] if tier_is(l, "epic") else lt("story", words(l["story"]), 500)),
    ("legend 120+ words", lambda l: lt("legend", words(l["legend"]), 120)),
    ("6+ facts", lambda l: lt("facts", l["facts"], 6)),
    ("4-8 firstEntry lines", lambda l: lt("firstEntry", l["firstEntry"], 4)),
    ("an area per room (2+ areas)", lambda l: lt("areas", l["areas"], 2)),
    ("lore 12+, rumour 8+, tell 6+", lambda l: lt("lore", l["lore"], 12) + lt("rumour", l["rumour"], 8) + lt("tell", l["tell"], 6)),
    ("loremaster 8+ topics", lambda l: lt("loremaster topics", l["topics"] or 0, 8) if l["topics"] is not None else ["no loremaster"]),
    ("book 4-8 pages of 100+ words", lambda l: ["no book"] if l["book"] is None else
        lt("book pages", len(l["book"]), 4) + pages_short(l["book"], 100)),
    ("an era and a faction", lambda l: (["no era"] if blank(l["era"]) else []) + (["no faction"] if l["factions"] == 0 else [])),
]
MINOR_FLOORS = [
    ("story 150+ words", lambda b: lt("story", words(b["story"]), 150)),
    ("legend 60+ words", lambda b: lt("legend", words(b["legend"]), 60)),
    ("2+ facts", lambda b: lt("facts", b["facts"], 2)),
    ("aggro 2+, death 1+, slay 1+ yells", lambda b: lt("aggro yells", b["aggro"], 2) + lt("death yells", b["death"], 1) + lt("slay yells", b["slay"], 1)),
    ("lairEntry 1+", lambda b: lt("lairEntry", b["lairEntry"], 1)),
    ("briefing, aftermathWin, rumour 2+ each", lambda b: lt("briefing", b["briefing"], 2) + lt("aftermathWin", b["aftermathWin"], 2) + lt("rumour", b["rumour"], 2)),
    ("loremasterTopic and chronicle win", lambda b: (["no loremasterTopic"] if blank(b["keyword"]) else []) + (["no chronicle win"] if blank(b["chronicleWin"]) else [])),
]
BOSS_FLOORS = [
    ("story 250+ words", lambda b: lt("story", words(b["story"]), 250)),
    ("legend 100+ words", lambda b: lt("legend", words(b["legend"]), 100)),
    ("aggro 3+, death 2+, slay 3+ yells", lambda b: lt("aggro yells", b["aggro"], 3) + lt("death yells", b["death"], 2) + lt("slay yells", b["slay"], 3)),
    ("lairEntry 2+ and realmFirstKill", lambda b: lt("lairEntry", b["lairEntry"], 2) + (["no realmFirstKill"] if blank(b["realmFirstKill"]) else [])),
    ("briefing, aftermathWin, aftermathLoss, rumour 4+ each", lambda b: lt("briefing", b["briefing"], 4) + lt("aftermathWin", b["aftermathWin"], 4)
        + lt("aftermathLoss", b["aftermathLoss"], 4) + lt("rumour", b["rumour"], 4)),
    ("loremasterTopic and bookPage 100+ words", lambda b: (["no loremasterTopic"] if blank(b["keyword"]) else [])
        + (["no bookPage"] if not b["bookPages"] else pages_short(b["bookPages"], 100))),
    ("chronicle win and loss", lambda b: (["no chronicle win"] if blank(b["chronicleWin"]) else []) + (["no chronicle loss"] if blank(b["chronicleLoss"]) else [])),
    ("an era, a faction or related", lambda b: ["no era, faction or related"] if blank(b["era"]) and b["factions"] == 0 and b["related"] == 0 else []),
]


def chapter_words(e): return sum(words(c) for c in e["chapters"])


def range_short(what, value, low, high):
    return [f"{what} {value}<{low}"] if value < low else [f"{what} {value}>{high}"] if value > high else []


EPIC_LOCATION_FLOORS = [
    ("4-10 chapters", lambda l: range_short("chapters", len(l["chapters"]), 4, 10)),
    ("chapters 3,000+ words", lambda l: lt("chapter words", chapter_words(l), 3000)),
    ("story summary 300-500 words", lambda l: range_short("story", words(l["story"]), 300, 500)),
    ("book 8-16 pages", lambda l: ["no book"] if l["book"] is None else lt("book pages", len(l["book"]), 8)),
]
EPIC_BOSS_FLOORS = [
    ("4+ chapters", lambda b: lt("chapters", len(b["chapters"]), 4)),
    ("chapters 1,000+ words (1,800+ for raid finals and dragons)", lambda b: lt("chapter words", chapter_words(b), 1000)),
    ("story summary 300-500 words", lambda b: range_short("story", words(b["story"]), 300, 500)),
    ("bookPage 2-4 pages", lambda b: lt("bookPage pages", len(b["bookPages"]), 2)),
]


def tier_is(e, t): return e["tier"].upper() == t.upper()  # string.Equals(..., OrdinalIgnoreCase) on ASCII tiers


def groups(locs, bosses):
    """(kind, summary label, entries, floors) in DepthReport's order."""
    major = [b for b in bosses if not tier_is(b, "minor") and not tier_is(b, "epic")]
    return [
        ("location", "locations", locs, LOCATION_FLOORS),
        ("major", "bosses", major, BOSS_FLOORS),
        ("minor", "minor bosses", [b for b in bosses if tier_is(b, "minor")], MINOR_FLOORS),
        ("epic-location", "epic locations", [l for l in locs if tier_is(l, "epic")], EPIC_LOCATION_FLOORS),
        ("epic-boss", "epic bosses", [b for b in bosses if tier_is(b, "epic")], EPIC_BOSS_FLOORS),
    ]


def report(lore_dir, realm=None, kinds=None, exclude_epic=False):
    locs, bosses, hists, problems = load(lore_dir)
    out = dict(lore_dir=os.path.abspath(lore_dir), locations=len(locs), bosses=len(bosses), histories=len(hists), problems=problems,
               summary=[], groups=[])
    for kind, label, entries, floors in groups(locs, bosses):
        if kinds and kind not in kinds:
            continue
        if kind == "location" and exclude_epic:
            entries = [l for l in entries if not tier_is(l, "epic")]
        if realm:
            entries = [e for e in entries if e["realm"].upper() == realm.upper()]
        g = dict(kind=kind, label=label, total=len(entries), floors=[], files=[], realms={})
        for what, check in floors:
            n = sum(1 for e in entries if check(e))
            g["floors"].append(dict(floor=what, below=n, of=len(entries)))
            out["summary"].append(f"{label}: {what}: {n} of {len(entries)} below")
        for r in REALMS + [""]:
            mine = [e for e in entries if (e["realm"] if e["realm"] in REALMS else "") == r]
            if mine:
                g["realms"][r or "other"] = dict(total=len(mine), below_any=sum(1 for e in mine if any(c(e) for _, c in floors)),
                                                 floors={what: sum(1 for e in mine if c(e)) for what, c in floors})
        for e in entries:
            missed = [dict(floor=what, short=s) for what, check in floors if (s := check(e))]
            if missed:
                g["files"].append(dict(file=e["file"], realm=e["realm"], tier=e["tier"], missed=missed))
        out["groups"].append(g)
    if not kinds and not realm and not exclude_epic:
        for r in REALMS[:3]:
            h = next((h for h in hists if h["realm"].upper() == r.upper()), None)
            counts = f"{h['eras']} eras, {h['factions']} factions" if h else "no file yet"
            out["summary"].append(f"history: {r}: {counts}")
    return out


def file_line(kind, f):
    tag = kind if kind not in ("location",) or not f["tier"] else f"{kind} {f['tier'].lower()}"
    return f"{f['file']} [{tag}, {f['realm'] or '?'}]: " + "; ".join(s for m in f["missed"] for s in m["short"])


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lore", default=DEFAULT_LORE, help="the lore folder (default: the server's data/lore)")
    ap.add_argument("--files", action="store_true", help="per file, the floors it misses with the measured values")
    ap.add_argument("--realm", choices=REALMS, type=lambda s: next((r for r in REALMS if r.lower() == s.lower()), s))
    ap.add_argument("--kind", action="append", choices=KINDS, help="only this kind (repeatable)")
    ap.add_argument("--exclude-epic", action="store_true", help="leave epic locations out of the location kind")
    ap.add_argument("--by-realm", action="store_true", help="with --files: group the files by realm, with counts")
    ap.add_argument("--json", action="store_true", help="the whole report as JSON")
    a = ap.parse_args(argv)
    r = report(a.lore, a.realm, a.kind, a.exclude_epic)
    if a.json:
        print(json.dumps(r, indent=1, ensure_ascii=False))
        return 0
    print(f"LORE_DEPTH {r['locations']} locations, {r['bosses']} bosses, {r['histories']} history files"
          + (f" (filtered: realm {a.realm or 'any'}, kind {','.join(a.kind or ['all'])}{', no epic locations' if a.exclude_epic else ''})"
             if a.realm or a.kind or a.exclude_epic else ""))
    for line in r["summary"]:
        print("LORE_DEPTH " + line)
    for p in r["problems"]:
        print("PARSE " + p)
    if a.files:
        for g in r["groups"]:
            print(f"\n== {g['label']} ({g['kind']}): {len(g['files'])} of {g['total']} below a floor")
            if a.by_realm:
                for realm, c in g["realms"].items():
                    per = "; ".join(f"{what}: {n}" for what, n in c["floors"].items() if n)
                    print(f"{realm}: {c['below_any']} of {c['total']} below a floor" + (f"; {per}" if per else ""))
                for realm in REALMS + [""]:
                    fs = [f for f in g["files"] if (f["realm"] if f["realm"] in REALMS else "") == realm]
                    if not fs:
                        continue
                    print(f"\n-- {realm or 'other realm'}: {len(fs)} below a floor")
                    for f in fs:
                        print(file_line(g["kind"], f))
            else:
                for f in g["files"]:
                    print(file_line(g["kind"], f))
    return 0


if __name__ == "__main__":
    sys.exit(main())
