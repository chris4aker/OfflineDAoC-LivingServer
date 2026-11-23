#!/usr/bin/env python3
"""The continuity ledger (1.2.1): facts the lore tells in more than one file, checked file by file.

tools/lore/continuity.json holds the ledger: counts of the dead and the returned, the numbers of companies and raiders,
dates, Rolls and winters, who saw or did what, and named people's fates. Each entry gives its scope and the forms the fact
may take (see the ledger's "about"). This script reads every visible string of the lore files in the entry's scope
(fourth_wall.visible) and the hidden facts' text too (marked "hidden fact": a writer's note can contradict as well), and
reports every statement that differs.

  python3 tools/lore/continuity.py               the ledger, the contradictions it finds
  python3 tools/lore/continuity.py --check       exit 1 on any contradiction (CI runs it report-only until it is clean)
  python3 tools/lore/continuity.py --suggest     candidate recurring facts not in the ledger: a number and its noun told
                                                 beside the same name in two or more files, those with different
                                                 numbers first
  python3 tools/lore/continuity.py --write       also write docs/1.2.1/CONTINUITY.md

Nothing here rewrites lore: contradictions go to docs/1.2.1/CONTINUITY.md for the writers and the owner
(HEROES.md decision 10: Mythic's canon first, else the rule of cool, adding words rather than cutting).
"""
import collections, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from lore_text import LORE_DIR, lore_files, sentences, all_facts
import fourth_wall

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
LEDGER = os.path.join(HERE, "continuity.json")
DOC = os.path.join(ROOT, "docs/1.2.1/CONTINUITY.md")
I = re.IGNORECASE

UNITS = ("zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen "
         "eighteen nineteen").split()
TENS = "_ _ twenty thirty forty fifty sixty seventy eighty ninety".split()
TWO_WORDS = re.compile(r"\b(twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety) (one|two|three|four|five|six|seven|eight|nine)\b", re.I)


def number(word):
    """A number word or digits as an int ("sixty-seven" 67, "80" 80, "Tenth" 10), else None."""
    w = (word or "").lower().replace(",", "").strip()
    if w.isdigit():
        return int(w)
    if w in UNITS:
        return UNITS.index(w)
    if w in TENS[2:]:
        return TENS.index(w) * 10
    if "-" in w:
        a, b = w.split("-", 1)
        if a in TENS[2:] and b in UNITS[1:10]:
            return TENS.index(a) * 10 + UNITS.index(b)
    return None


def read_json(path):
    with open(path, encoding="utf-8-sig") as fh:
        return json.load(fh)


def load_lore(root=LORE_DIR):
    """[(id, data)] with ids like "bosses/host"."""
    return [(os.path.relpath(f, root).replace(os.sep, "/")[:-5], read_json(f)) for f in lore_files(root)]


def fields(lid, d):
    """(field path, text) of every visible string, then the hidden facts' text."""
    folder = lid.split("/")[0]
    out = [(p.split(".", 1)[-1] if "." in p else p, t) for p, t, _ in fourth_wall.visible(d, folder)
           if not p.endswith("<keyword>") and isinstance(t, str)]
    if folder != "history":
        out += [("hidden fact", x["text"]) for x in all_facts(d) if isinstance(x.get("text"), str)]
    return out


def in_scope(lid, d, scope):
    files, locs = scope.get("files"), scope.get("locations")
    if not files and not locs:
        return True
    if files and lid in files:
        return True
    if locs:
        if lid.startswith("locations/") and lid.split("/", 1)[1] in locs:
            return True
        if lid.startswith("bosses/") and d.get("locationId") in locs:
            return True
    return False


def compile_entry(e):
    c = dict(e)
    s = e.get("scope") or {}
    c["_sentence"] = re.compile(s["sentence"], I) if s.get("sentence") else None
    c["_exclude"] = re.compile(s["exclude"], I) if s.get("exclude") else None
    c["_fields"] = re.compile(s["fields"]) if s.get("fields") else None
    if e["kind"] == "value":
        claims = e["claim"] if isinstance(e["claim"], list) else [e["claim"]]
        c["_claims"] = [re.compile(p, I) for p in claims]
        c["_claim"] = re.compile("|".join(f"(?:{re.sub(r'[(][?]P<v>', '(', p)})" for p in claims), I)   # any claim, for --suggest
        c["_allowed"] = {a if isinstance(a, int) else str(a).lower() for a in e["allowed"]}
    elif e["kind"] == "forbid":
        c["_forbid"] = [re.compile(p, I) for p in e["forbid"]]
        c["_unless"] = re.compile(e["unless"], I) if e.get("unless") else None
    elif e["kind"] == "requires":
        c["_all"] = [re.compile(p, I) for p in e["all"]]
        c["_require"] = re.compile(e["require"], I)
    else:
        raise ValueError(f"{e['id']}: unknown kind {e['kind']}")
    return c


def load_ledger(path=LEDGER):
    return [compile_entry(e) for e in read_json(path)["facts"]]


def claimed(m):
    return m.groupdict().get("v")


def check(lore, ledger):
    """{entry id: {"told": set of files that tell it as the ledger has it, "bad": [(file, field, sentence, said)]}}."""
    out = {e["id"]: {"told": set(), "bad": []} for e in ledger}
    for lid, d in lore:
        texts = fields(lid, d)
        for e in ledger:
            if not in_scope(lid, d, e.get("scope") or {}):
                continue
            r = out[e["id"]]
            for field, text in texts:
                if e["_fields"] and not e["_fields"].search(field):
                    continue
                if e["kind"] == "requires" and e.get("within", "field") == "field":
                    if all(p.search(text) for p in e["_all"]):
                        if e["_require"].search(text):
                            r["told"].add(lid)
                        else:
                            r["bad"].append((lid, field, first_match(text, e["_all"][-1]), "does not count it"))
                    continue
                for s in sentences(text):
                    s = TWO_WORDS.sub(lambda m: m.group(1) + "-" + m.group(2), s)   # "twenty seven" in bot lines
                    if e["_sentence"] and not e["_sentence"].search(s) or e["_exclude"] and e["_exclude"].search(s):
                        continue
                    if e["kind"] == "value":
                        for m in (m for p in e["_claims"] for m in p.finditer(s)):
                            said = claimed(m)
                            if said is None:
                                continue
                            v = number(said) if e.get("numeric") else said.lower()
                            if v is None:
                                continue
                            if v in e["_allowed"]:
                                r["told"].add(lid)
                            else:
                                r["bad"].append((lid, field, s, said))
                    elif e["kind"] == "forbid":
                        hit = next((p for p in e["_forbid"] if p.search(s)), None)
                        if hit and not (e["_unless"] and e["_unless"].search(s)):
                            r["bad"].append((lid, field, s, hit.search(s).group(0)))
                        elif not hit:
                            r["told"].add(lid)
                    else:  # requires, within a sentence
                        if all(p.search(s) for p in e["_all"]):
                            if e["_require"].search(s):
                                r["told"].add(lid)
                            else:
                                r["bad"].append((lid, field, s, "does not count it"))
    return out


def first_match(text, pattern):
    for s in sentences(text):
        if pattern.search(s):
            return s
    return text[:200]


# ---- --suggest ---------------------------------------------------------------------------------------------------
NUM = re.compile(r"\b(\d[\d,]*|(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:-(?:one|two|three|four|five|six|seven|eight|nine))?"
                 r"|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|"
                 r"nineteen)\s+(?:of\s+(?:the\s+|its\s+|their\s+|his\s+|her\s+)?)?([a-z][a-z'-]+)", I)
NOT_NOUN = set(("of the a an and or to in on at by for with from times more less than who that which them they are were was have "
                "had has is be been it its his her their us we you me him she he our your more most other others others' each "
                "such so as at all both years days times hundred thousand words ways reasons great high because things parts kinds sorts "
                "lines").split())
NAME = re.compile(r"(?<=\s)([A-Z][\w'’]+(?:\s(?:of|the|na|an|se|den)\s[A-Z][\w'’]+)*)")
NOT_NAME = set("The A An He She It They His Her Its Their I We You Of And But In On At When Then There This That So Sir Dame Lord "
               "Lady Brother Father Master King Queen Prince Princess Duke Earl Marquis High Captain Albion Midgard Hibernia Camelot "
               "Jordheim Albion's Midgard's Hibernia's".split()) | {"Tir na Nog"}


def suggestions(lore, ledger, min_files=2):
    """(differs, files, (name, noun), {number: [files]}) for numbers told beside the same name in min_files+ files, outside
    the sentences a ledger entry already reads."""
    keys = collections.defaultdict(lambda: collections.defaultdict(set))
    for lid, d in lore:
        for field, text in fields(lid, d):
            if field.startswith("legend") or field == "hidden fact" or "{" in text or field.startswith("botLines"):
                continue
            for s in sentences(text):
                if any(e.get("_claim") and e["_claim"].search(s) and (not e["_sentence"] or e["_sentence"].search(s)) for e in ledger):
                    continue
                names = {m.group(1) for m in NAME.finditer(s) if m.group(1) not in NOT_NAME and not m.group(1).isupper()}
                for m in NUM.finditer(s):
                    v = number(m.group(1))
                    noun = m.group(2).lower()
                    if v is None or v < 2 or noun in NOT_NOUN:
                        continue
                    for n in names:
                        if n.lower() != noun:
                            keys[(n, noun)][v].add(lid)
    rows = []
    for k, vals in keys.items():
        files = set().union(*vals.values())
        if len(files) >= min_files:
            rows.append((len(vals) > 1, len(files), k, {v: sorted(fs) for v, fs in sorted(vals.items())}))
    rows.sort(key=lambda r: (not r[0], -r[1], r[2]))
    return rows


# ---- the report --------------------------------------------------------------------------------------------------
def short(lid):
    return lid.split("/", 1)[-1]


def render(ledger, result, suggested, show=40):
    bad = sum(len(r["bad"]) for r in result.values())
    out = ["# Continuity (1.2.1)", "",
           "Written by `python3 tools/lore/continuity.py --write`. The ledger is `tools/lore/continuity.json`: facts the lore",
           "tells in more than one file and the forms each may take. `--check` reads every visible string in each fact's scope",
           "(and the hidden facts' text, marked) and lists the statements that differ. Nothing here rewrites lore; settling a",
           "contradiction follows HEROES.md decision 10 (Mythic's canon first, else the rule of cool, adding words, never",
           "cutting).", "",
           f"**{len(ledger)} ledger facts, {bad} contradiction(s).**", "",
           "## The ledger", "", "| Fact | told as the ledger has it in | contradictions |", "|---|---|---|"]
    for e in ledger:
        r = result[e["id"]]
        told = ", ".join(f"`{short(f)}`" for f in sorted(r["told"])[:6]) + (f" and {len(r['told']) - 6} more" if len(r["told"]) > 6 else "")
        out.append(f"| **{e['id']}**: {e['fact']} | {len(r['told'])}: {told or '-'} | {len(r['bad']) or '-'} |")
    out += ["", "## Contradictions found", ""]
    if not bad:
        out.append("None.")
    for e in ledger:
        r = result[e["id"]]
        if not r["bad"]:
            continue
        out += [f"### {e['id']}", "", f"Ledger: {e['fact']} ({e.get('source', '')})", ""]
        if e.get("finding"):
            out += [f"Finding: {e['finding']}", ""]
        for lid, field, s, said in r["bad"]:
            out.append(f"- `{lid}` {field}: \"{s.strip()[:300]}\" (says **{said}**)")
        out.append("")
    out += ["", "## Candidates not in the ledger (`--suggest`)", "",
            "A number and the noun after it, told beside the same name in two or more files (fight advice, bot lines and the",
            "Herald's templates left out). Those with different numbers come first: most are different things (two dukes'",
            "thirty and thirty-one fights), some are worth a ledger entry or a look.", "",
            "| name | noun | numbers (files) |", "|---|---|---|"]
    for differs, n, (name, noun), vals in suggested[:show]:
        cells = "; ".join(f"{'**' if differs else ''}{v}{'**' if differs else ''}: " + ", ".join(f"`{short(f)}`" for f in fs[:3]) +
                          (f" +{len(fs) - 3}" if len(fs) > 3 else "") for v, fs in vals.items())
        out.append(f"| {name} | {noun} | {cells} |")
    out.append("")
    return "\n".join(out)


def main(argv):
    lore = load_lore()
    ledger = load_ledger()
    result = check(lore, ledger)
    bad = 0
    for e in ledger:
        r = result[e["id"]]
        bad += len(r["bad"])
        print(f"CONTINUITY {e['id']}: told in {len(r['told'])} file(s), {len(r['bad'])} contradiction(s)")
        for lid, field, s, said in r["bad"]:
            print(f"  {lid} {field}: says {said!r}: {s.strip()[:160]}")
    suggested = suggestions(lore, ledger) if ("--suggest" in argv or "--write" in argv) else []
    if "--suggest" in argv:
        for differs, n, (name, noun), vals in suggested[:60]:
            print(f"SUGGEST {'DIFFERS ' if differs else ''}{name} / {noun}: " + "; ".join(f"{v} in {len(fs)}" for v, fs in vals.items()))
    if "--write" in argv:
        with open(DOC, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(render(ledger, result, suggested))
        print("wrote", os.path.relpath(DOC, ROOT))
    print(f"continuity: {len(ledger)} ledger facts, {bad} contradiction(s)")
    return 1 if "--check" in argv and bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
