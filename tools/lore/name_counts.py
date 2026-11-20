#!/usr/bin/env python3
"""Count the named people (honorific + name) in the lore texts, possessives folded in.

  name_counts.py            top names with mentions and files
  name_counts.py --check    exit 1 if a name is over MAX_MENTIONS (or over its ratchet in OVER_CAP)
"""
import json, re, sys, os, collections
sys.path.insert(0, os.path.dirname(__file__))
from lore_text import *

HONORIFIC = r"(?:Dame|Sir|Thane|Jarl|Brother|Sister|Friar|Skald|Captain|Brehon|Warden|Lady|Lord|Master)"
NAME = re.compile(r"\b(" + HONORIFIC + r")\s+([A-Z][a-zé']+(?:\s+(?:the\s+)?[A-Z][a-z]+)?)")
NOT_A_NAME = {"Odran's Door"}   # places that carry a person's name
COURT = {"Lord Baln", "Lord Oro", "Lord Saeor", "Lord Baelerdoth", "Lady Lurlei"}   # the demon court of Darkness Falls, by design in many files
MAX_MENTIONS = 12               # one named person may be mentioned this often across all the lore
# Dame Ysolde, Brother Cadoc, Sir Cador, Brother Odran and Sir Geraint were replaced by the realms'
# folklore heroes on 2026-11-20 (docs/1.2.1/HEROES.md) and now sit under the general cap.
# Regional through-line characters and quest NPCs whose stories recur by design (Sir Arward across Dartmoor, Lord Aryon's
# box, Lady Nimue's Sequence). Ratchet at their count on 2026-11-20; a later pass may vary them like the retired witnesses.
OVER_CAP = {"Sir Arward": 25, "Lord Aryon": 17, "Lady Nimue": 15, "Lord Emthoro": 13, "Sister Elaydith": 13}

# The folklore heroes (docs/1.2.1/HEROES.md) are counted by bare name as well, history files included, so the cap of
# MAX_MENTIONS holds for "Gawain" as much as for "Sir Gawain".
HEROES = {
    "Galahad": r"Galahad", "Gawain": r"Gawain", "Bedivere": r"Bedivere",
    "Sigurd": r"Sigurd", "Ragnar Lodbrok": r"Ragnar", "Hervor": r"Hervor",
    "Cu Chulainn": r"C[uú] Chulainn", "Fionn mac Cumhaill": r"Fionn", "Oisin": r"Ois[ií]n",
}
# Over the cap on 2026-11-20 and held at that count until the canon review (CANON-REVIEW-HEROES) decides their passages:
# the Caer Sidi Bedivere and Gawain inserts, and Fionn, whom older Hibernian lore already names.
HERO_OVER_CAP = {"Fionn mac Cumhaill": 17, "Gawain": 14, "Bedivere": 13}

def hero_count(root=LORE_DIR):
    c = collections.Counter()
    pats = {h: re.compile(r"\b" + rx + r"\b") for h, rx in HEROES.items()}
    for f in lore_files(root):
        d = json.load(open(f, encoding="utf-8"))
        for t in strings(d):
            for h, rx in pats.items():
                c[h] += len(rx.findall(t))
    return c

def person(m):
    name = re.sub(r"'s$", "", m.group(2).strip()).strip()
    return f"{m.group(1)} {name}"

def strings(v):
    """Every narrative string in a lore file: story, legend, chapters, book pages, loremaster topics, yells, bot lines.
    The `facts` arrays are left out: they are game-sourced (quest NPCs such as Sir Arward) and repeat names truthfully."""
    if isinstance(v, str): yield v
    elif isinstance(v, dict):
        for k, x in v.items():
            if k != "facts": yield from strings(x)
    elif isinstance(v, list):
        for x in v: yield from strings(x)

def boss_names(root=LORE_DIR):
    """Names that belong to bosses and NPCs themselves (Lord Gildas, Lady Darra): never counted as heroes."""
    out = set()
    for f in lore_files(root):
        d = json.load(open(f, encoding="utf-8"))
        out.update([d.get("name") or ""] + list(d.get("npcNames") or []) + list(d.get("aliases") or []))
    return out

def count(root=LORE_DIR):
    own = boss_names(root)
    c, files = collections.Counter(), collections.defaultdict(set)
    for f in lore_files(root):
        d = json.load(open(f, encoding="utf-8"))
        for t in strings(d):
            for m in NAME.finditer(t):
                if m.group(2).strip() in NOT_A_NAME: continue
                p = person(m)
                if p in COURT or p in own or m.group(0).strip() in own: continue
                c[p] += 1; files[p].add(os.path.basename(f))
    return c, files

def problems(c, heroes=None):
    out = []
    for hero, n in (heroes or {}).items():
        cap = HERO_OVER_CAP.get(hero, MAX_MENTIONS)
        if n > cap: out.append(f"hero {hero}: {n} mentions, cap {cap}")
    for name, n in c.items():
        cap = OVER_CAP.get(name, MAX_MENTIONS)
        if n > cap: out.append(f"{name}: {n} mentions, cap {cap}")
    return out

if __name__ == "__main__":
    c, files = count()
    if "--check" in sys.argv:
        p = problems(c, hero_count())
        for x in p: print("LORE NAMES", x)
        print(f"lore names: {len(p)} over cap")
        sys.exit(1 if p else 0)
    for name, n in c.most_common(40): print(f"{n:4} {len(files[name]):3} files  {name}")
    print("heroes (bare name, narrative text, history included):")
    for hero, n in hero_count().most_common(): print(f"{n:4}  {hero}")
