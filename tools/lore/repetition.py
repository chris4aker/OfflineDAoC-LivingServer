#!/usr/bin/env python3
"""Repeated phrasing in the player-visible lore (1.2.1): phrases repeated across files, page openings, set formulas.

What it measures, over every visible string of the lore files (fourth_wall.visible: hidden fields and the locations' and
bosses' own facts are left out; "[a|b]" choices read as their first choice; Herald templates with "{...}" are left out):

  - phrases: every run of N words (N = 6) that recurs in MIN_FILES or more files, by kind of text: "narrative" (stories,
    chapters, books, loremasters, atmosphere lines, history) and "chat" (bot lines). Overlapping runs are merged into the
    longest phrase that recurs. The fight advice ("legend", kind "advice") uses the owner's set strength tiers on purpose
    ("a foe for a hardened band of veterans"), so its phrases are listed apart, for reading, and not capped;
  - openings: the first two words of every book page (a location's book.pages, a boss's bookPage and bookPages) and
    every chapter, per realm ("From the", "Of the");
  - formulas: the set phrases in FORMULAS (teller attributions, "the wider country it belongs to", "is the only one of",
    "for the rest of his life"...), per realm, per 10,000 words of narrative text.

  python3 tools/lore/repetition.py               print the summary
  python3 tools/lore/repetition.py --write       also write docs/1.2.1/REPETITION.md (the ranked report)
  python3 tools/lore/repetition.py --check       exit 1 if anything got worse than tools/lore/repetition_caps.json
  python3 tools/lore/repetition.py --write-caps  set the caps to the current counts (only when a rise is meant)

The ratchet (--check, run by tests/test_repetition.py in CI):
  - a phrase capped in the caps file may not recur in more files than its cap; a phrase not in it may not reach
    FREE_PHRASE_FILES files;
  - a two-word page opening may not be used more often in a realm than its cap; one not in the caps may not reach
    FREE_OPENING;
  - each formula's rate per 10,000 narrative words, and each realm's share of narrative words inside repeated phrases,
    may not rise above its cap (rates, so new writing that does not repeat itself always passes).
Lower a cap by hand, or with --write-caps, after a rewrite brings a count down.
"""
import collections, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from lore_text import LORE_DIR, lore_files
import fourth_wall

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
DOC = os.path.join(ROOT, "docs/1.2.1/REPETITION.md")
CAPS = os.path.join(HERE, "repetition_caps.json")
REALMS = ["Albion", "Midgard", "Hibernia", "Neutral"]

N = 6
MIN_FILES = 3
FREE_PHRASE_FILES = 10   # a phrase not in the caps file fails --check once it is in this many files
CAPPED_PHRASE_FILES = 6  # --write-caps caps every phrase in this many files or more
FREE_OPENING = 6         # a page opening not in the caps file fails --check once a realm uses it this often
CAPPED_OPENING = 4       # --write-caps caps every opening used this often or more in a realm
SHOW = 40                # phrases listed per kind in the report

STOP = set("the a an of and to in on at by for with from is was were be been he she it they his her its their them that "
           "this as but or not no so i you we my our your who what which".split())
CHOICE = re.compile(r"\[([^\[\]|]*)\|[^\[\]]*\]")
WORD = re.compile(r"[^\W_]+(?:['’][^\W_]+)?")

# Set formulas: (name, pattern). Case-insensitive, counted in narrative text.
FORMULAS = [
    ("teller's list (\"From X's list\")", r"\bfrom [\w'’ -]{2,40}?['’]s list\b"),
    ("\"From the readings / ledger / trap lines of\"", r"\bfrom the (?:readings|ledger|trap lines|tally|roll|notes|count) of\b"),
    ("\"From what X told\"", r"\bfrom what [\w'’ -]{2,60}? told\b"),
    ("\"as told in\"", r"\bas told (?:in|at|by)\b"),
    ("\"the druids / bards / skalds of ... say\"", r"\bthe (?:druids|bards|skalds|monks|friars|seers|elders) of [\w'’ ]{2,30}? say\b"),
    ("\"the wider country it belongs to\"", r"\bthe wider country it belongs to\b"),
    ("\"why it matters\" / \"why does it matter\"", r"\bwhy (?:it matters|does it matter)\b"),
    ("\"whence the name\"", r"\bwhence the name\b"),
    ("\"is the only one of\"", r"\bis the only one of\b"),
    ("\"for the rest of his/her life\"", r"\bfor the rest of (?:his|her|their) li(?:fe|ves)\b"),
    ("\"the way (s)he said everything\"", r"\bthe way (?:she|he) said everything\b"),
    ("\"they do not say how\"", r"\bthey do not say (?:how|why)\b"),
    ("\"to this day\"", r"\bto this day\b"),
    ("\"never once\"", r"\bnever once\b"),
    ("\"no one / nobody knows\"", r"\b(?:no one|nobody) knows\b"),
    ("\"it is said\"", r"\bit is said\b"),
    ("\"for a long time, and then\"", r"\bfor a long time,? and then\b"),
    ("\"at the far end of the\"", r"\bat the far end of the\b"),
]
FORMULA_RE = [(name, re.compile(p, re.I)) for name, p in FORMULAS]


def read_json(path):
    with open(path, encoding="utf-8-sig") as fh:
        return json.load(fh)


def tokens(text):
    return [w.lower().replace("’", "'") for w in WORD.findall(CHOICE.sub(lambda m: m.group(1), text))]


def texts(root=LORE_DIR):
    """(file, realm, kind, path, text) for every visible string worth reading."""
    for f in lore_files(root):
        rel = os.path.relpath(f, root).replace(os.sep, "/")
        d = read_json(f)
        realm = d.get("realm") if d.get("realm") in REALMS else "Neutral"
        for path, t, kind in fourth_wall.visible(d, rel.split("/")[0]):
            if path.endswith("<keyword>") or "{" in t or not t.strip():
                continue
            yield rel, realm, kind, path, t


def pages(root=LORE_DIR):
    """(file, realm, text) of every book page and chapter."""
    for f in lore_files(root):
        rel = os.path.relpath(f, root).replace(os.sep, "/")
        d = read_json(f)
        realm = d.get("realm") if d.get("realm") in REALMS else "Neutral"
        ps = list((d.get("book") or {}).get("pages") or [])
        for k in ("bookPage", "bookPages"):
            v = d.get(k)
            ps += [v] if isinstance(v, str) else [p for p in (v or []) if isinstance(p, str)]
        ps += [c.get("text") for c in d.get("chapters") or [] if isinstance(c, dict)]
        for p in ps:
            if isinstance(p, str) and p.strip():
                yield rel, realm, p


def opening(text):
    return " ".join(text.split()[:2])


def phrases(items):
    """Phrases recurring in MIN_FILES+ files: {phrase: {"files": set, "realms": Counter, "count": n}}, the 6-word counts
    {gram: files} for the ratchet, and the words per realm inside a recurring run.

    Each recurring 6-word run, most widespread first, is grown a word at a time to the left and right while the next word
    is the same in at least four-fifths of its files, so "the druids of tir na nog" is one phrase and keeps its count;
    the runs inside a phrase already listed are not listed again."""
    items = [(f, r, tokens(t)) for f, r, t in items]
    grams = collections.defaultdict(set)
    for f, _, w in items:
        for i in range(len(w) - N + 1):
            g = tuple(w[i:i + N])
            if sum(x not in STOP for x in g) >= 2:
                grams[g].add(f)
    hot = {g for g, fs in grams.items() if len(fs) >= MIN_FILES}
    where = collections.defaultdict(list)  # gram -> [(item, position)]
    covered = collections.Counter()
    for k, (f, r, w) in enumerate(items):
        inside = set()
        for i in range(len(w) - N + 1):
            g = tuple(w[i:i + N])
            if g in hot:
                where[g].append((k, i))
                inside.update(range(i, i + N))
        covered[r] += len(inside)
    out, used = {}, set()
    for g in sorted(hot, key=lambda g: (-len(grams[g]), g)):
        if g in used:
            continue
        occ = [(k, i, i + N) for k, i in where[g]]
        files = {items[k][0] for k, _, _ in occ}

        def grow(occ, side):
            while True:
                nxt = collections.defaultdict(set)
                for k, a, b in occ:
                    w = items[k][2]
                    pos = a - 1 if side < 0 else b
                    if 0 <= pos < len(w):
                        nxt[w[pos]].add(items[k][0])
                if not nxt:
                    return occ
                word, fs = max(nxt.items(), key=lambda kv: (len(kv[1]), kv[0]))
                if len(fs) < max(MIN_FILES, 0.8 * len(files)) or b_len(occ) >= 30:
                    return occ
                occ = [(k, a - 1, b) if side < 0 else (k, a, b + 1) for k, a, b in occ
                       if 0 <= (a - 1 if side < 0 else b) < len(items[k][2]) and items[k][2][a - 1 if side < 0 else b] == word]

        def b_len(occ):
            k, a, b = occ[0]
            return b - a

        occ = grow(grow(occ, 1), -1)
        k0, a0, b0 = occ[0]
        words = items[k0][2][a0:b0]
        phrase = " ".join(words)
        for x in range(len(words) - N + 1):
            used.add(tuple(words[x:x + N]))
        e = {"files": {items[k][0] for k, _, _ in occ}, "realms": collections.Counter(items[k][1] for k, _, _ in occ), "count": len(occ)}
        out[phrase] = e
    return out, {" ".join(g): len(grams[g]) for g in hot}, covered


def measure(root=LORE_DIR):
    allt = list(texts(root))
    words = collections.Counter()
    for _, r, kind, _, t in allt:
        if kind == "narrative":
            words[r] += len(tokens(t))
    result = {"words": dict(words)}
    for kind in ("narrative", "chat", "advice"):
        merged, grams, covered = phrases([(f, r, t) for f, r, k, _, t in allt if k == kind])
        result[kind] = {"phrases": merged, "grams": grams, "covered": dict(covered)}
    op = collections.defaultdict(collections.Counter)
    first = collections.defaultdict(collections.Counter)
    for _, r, p in pages(root):
        op[r][opening(p)] += 1
        first[r][p.split()[0]] += 1
    result["openings"], result["first_words"] = op, first
    fm = collections.defaultdict(collections.Counter)
    examples = collections.defaultdict(list)
    for f, r, kind, _, t in allt:
        if kind != "narrative":
            continue
        for name, rx in FORMULA_RE:
            for m in rx.finditer(t):
                fm[name][r] += 1
                if len(examples[name]) < 3 and f not in [e[0] for e in examples[name]]:
                    examples[name].append((f, m.group(0)))
    result["formulas"], result["formula_examples"] = fm, examples
    return result


def rate(n, words):
    return round(10_000 * n / words, 2) if words else 0.0


def current_caps(m):
    caps = {"phrases": {}, "openings": {}, "formula_rates": {}, "covered_share": {}}
    for kind in ("narrative", "chat"):
        caps["phrases"][kind] = {g: n for g, n in sorted(m[kind]["grams"].items()) if n >= CAPPED_PHRASE_FILES}
    for r in REALMS:
        caps["openings"][r] = {o: n for o, n in sorted(m["openings"][r].items()) if n >= CAPPED_OPENING}
        caps["formula_rates"][r] = {name: rate(m["formulas"][name][r], m["words"].get(r, 0)) for name, _ in FORMULAS
                                    if m["formulas"][name][r]}
        caps["covered_share"][r] = round(100 * m["narrative"]["covered"].get(r, 0) / max(1, m["words"].get(r, 0)), 2)
    return caps


def problems(m, caps):
    out = []
    for kind in ("narrative", "chat"):
        capped = caps["phrases"].get(kind, {})
        for g, n in m[kind]["grams"].items():
            cap = capped.get(g)
            if cap is not None and n > cap:
                out.append(f"{kind} phrase \"{g}\": in {n} files, cap {cap}")
            elif cap is None and n >= FREE_PHRASE_FILES:
                out.append(f"{kind} phrase \"{g}\": in {n} files (new; a phrase not in the caps may be in at most {FREE_PHRASE_FILES - 1})")
    for r in REALMS:
        capped = caps["openings"].get(r, {})
        for o, n in m["openings"][r].items():
            cap = capped.get(o)
            if cap is not None and n > cap:
                out.append(f"{r} page opening \"{o}\": {n} pages, cap {cap}")
            elif cap is None and n >= FREE_OPENING:
                out.append(f"{r} page opening \"{o}\": {n} pages (new; at most {FREE_OPENING - 1} without a cap)")
        for name, _ in FORMULAS:
            got = rate(m["formulas"][name][r], m["words"].get(r, 0))
            cap = caps["formula_rates"].get(r, {}).get(name, 0.0)
            if got > cap + 0.005:
                out.append(f"{r} formula {name}: {got} per 10,000 words, cap {cap}")
        share = round(100 * m["narrative"]["covered"].get(r, 0) / max(1, m["words"].get(r, 0)), 2)
        if share > caps["covered_share"].get(r, 0.0) + 0.005:
            out.append(f"{r}: {share}% of narrative words are in repeated phrases, cap {caps['covered_share'].get(r)}%")
    return out


def load_caps():
    with open(CAPS, encoding="utf-8") as fh:
        return json.load(fh)


def ranked(merged):
    """Longest-recurring first: by files, then count; a phrase inside a listed one with no more files is left out."""
    rows = sorted(merged.items(), key=lambda kv: (-len(kv[1]["files"]), -kv[1]["count"], kv[0]))
    shown = []
    for p, e in rows:
        if any(p in q and len(e["files"]) <= len(f["files"]) for q, f in shown):
            continue
        shown.append((p, e))
    return shown


def render(m):
    out = ["# Repetition in the lore (1.2.1)", "",
           "Written by `python3 tools/lore/repetition.py --write`; the ratchet is `--check` against",
           "`tools/lore/repetition_caps.json` (see the script). Words are counted over the visible text only. A phrase is a run of",
           f"{N} or more words in {MIN_FILES} or more files; its count is how often it appears. Nothing here is rewritten: this is a",
           "list for the writers (owner: never trim; reword by adding).", "",
           "## Summary", "", "| Realm | narrative words | in repeated phrases | top page opening |", "|---|---|---|---|"]
    for r in REALMS:
        w = m["words"].get(r, 0)
        cov = m["narrative"]["covered"].get(r, 0)
        top = m["openings"][r].most_common(1)
        total = sum(m["openings"][r].values())
        tops = f"\"{top[0][0]}\" {top[0][1]} of {total} pages" if top else "-"
        out.append(f"| {r} | {w:,} | {cov:,} ({100 * cov / max(1, w):.1f}%) | {tops} |")
    for kind, title in (("narrative", "Narrative phrases (stories, books, chapters, loremasters, atmosphere, history)"),
                        ("chat", "Bot-line phrases"),
                        ("advice", "Fight-advice phrases (the set strength tiers are meant; listed for reading, not capped)")):
        rows = ranked(m[kind]["phrases"])
        out += ["", f"## {title}", "", f"{len(m[kind]['phrases'])} phrases recur in {MIN_FILES}+ files; the top {min(SHOW, len(rows))}:", "",
                "| # | Phrase | files | times | by realm | e.g. |", "|---|---|---|---|---|---|"]
        for i, (p, e) in enumerate(rows[:SHOW], 1):
            realms = ", ".join(f"{r[:3]} {n}" for r, n in e["realms"].most_common())
            eg = ", ".join(f"`{f.rsplit('/', 1)[-1][:-5]}`" for f in sorted(e["files"])[:3])
            out.append(f"| {i} | {p} | {len(e['files'])} | {e['count']} | {realms} | {eg} |")
    out += ["", "## Page openings", "", "The first two words of every book page and chapter, per realm (the six most used):", "",
            "| Realm | pages | first word \"From\" | openings |", "|---|---|---|---|"]
    for r in REALMS:
        total = sum(m["openings"][r].values())
        frm = m["first_words"][r].get("From", 0)
        ops = "; ".join(f"\"{o}\" {n}" for o, n in m["openings"][r].most_common(6))
        out.append(f"| {r} | {total} | {frm} ({100 * frm / max(1, total):.0f}%) | {ops} |")
    out += ["", "## Set formulas", "", "Count per realm in narrative text (per 10,000 words in brackets):", "",
            "| Formula | " + " | ".join(REALMS) + " | e.g. |", "|---|" + "---|" * len(REALMS) + "---|"]
    for name, _ in FORMULAS:
        cells = [f"{m['formulas'][name][r]} ({rate(m['formulas'][name][r], m['words'].get(r, 0))})" if m["formulas"][name][r] else "-" for r in REALMS]
        eg = "; ".join(f"`{f.rsplit('/', 1)[-1][:-5]}`: \"{t}\"" for f, t in m["formula_examples"][name][:2])
        out.append(f"| {name} | " + " | ".join(cells) + f" | {eg} |")
    out.append("")
    return "\n".join(out)


def main(argv):
    m = measure()
    for r in REALMS:
        w = m["words"].get(r, 0)
        print(f"REPEAT {r}: {w} narrative words, {m['narrative']['covered'].get(r, 0)} in repeated phrases; "
              f"top opening {m['openings'][r].most_common(1)}")
    for kind in ("narrative", "chat", "advice"):
        print(f"REPEAT {kind}: {len(m[kind]['phrases'])} phrases in {MIN_FILES}+ files")
    if "--write" in argv:
        with open(DOC, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(render(m))
        print("wrote", os.path.relpath(DOC, ROOT))
    if "--write-caps" in argv:
        with open(CAPS, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(current_caps(m), fh, indent=1, ensure_ascii=False, sort_keys=True)
            fh.write("\n")
        print("wrote", os.path.relpath(CAPS, ROOT))
    if "--check" in argv:
        bad = problems(m, load_caps())
        for p in bad:
            print("REPEAT", p)
        print(f"repetition check: {len(bad)} problem(s)")
        return 1 if bad else 0
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
