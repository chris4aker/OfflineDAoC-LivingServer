#!/usr/bin/env python3
"""Count the named people (honorific + name) in the lore texts, possessives folded in.

  name_counts.py            top names with mentions and files
  name_counts.py --check    exit 1 if a name is over MAX_MENTIONS (or over its ratchet in OVER_CAP)
"""
import json, re, sys, os, collections
sys.path.insert(0, os.path.dirname(__file__))
from lore_text import *
from lint_lore import texts

HONORIFIC = r"(?:Dame|Sir|Thane|Jarl|Brother|Sister|Friar|Skald|Captain|Brehon|Warden|Lady|Lord|Master)"
NAME = re.compile(r"\b(" + HONORIFIC + r")\s+([A-Z][a-zé']+(?:\s+(?:the\s+)?[A-Z][a-z]+)?)")
NOT_A_NAME = {"Odran's Door"}   # places that carry a person's name
COURT = {"Lord Baln", "Lord Oro", "Lord Saeor", "Lord Baelerdoth", "Lady Lurlei"}   # the demon court of Darkness Falls, by design in many files
MAX_MENTIONS = 12               # one named person may be mentioned this often across all the lore
# The recurring witnesses that the 1.2.1 lore pass replaces with each realm's folklore heroes (docs/1.2.1/HEROES.md).
# name -> its mention count on 2026-11-20; may only go down, and each must end at MAX_MENTIONS or fewer.
OVER_CAP = {"Brother Cadoc": 60, "Dame Ysolde": 57, "Sir Cador": 27, "Brother Odran": 20, "Sir Geraint": 16, "Captain Rhodri": 15}

def person(m):
    name = re.sub(r"'s$", "", m.group(2).strip()).strip()
    return f"{m.group(1)} {name}"

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
        for _, t in texts(d):
            for m in NAME.finditer(t):
                if m.group(2).strip() in NOT_A_NAME: continue
                p = person(m)
                if p in COURT or p in own or m.group(0).strip() in own: continue
                c[p] += 1; files[p].add(os.path.basename(f))
    return c, files

def problems(c):
    out = []
    for name, n in c.items():
        cap = OVER_CAP.get(name, MAX_MENTIONS)
        if n > cap: out.append(f"{name}: {n} mentions, cap {cap}")
    return out

if __name__ == "__main__":
    c, files = count()
    if "--check" in sys.argv:
        p = problems(c)
        for x in p: print("LORE NAMES", x)
        print(f"lore names: {len(p)} over cap")
        sys.exit(1 if p else 0)
    for name, n in c.most_common(40): print(f"{n:4} {len(files[name]):3} files  {name}")
