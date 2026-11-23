#!/usr/bin/env python3
"""Strip developer notes from lore `legend` texts and drop duplicate entries from yell and bot-line pools.

  clean_lore.py          dry run: lists what would change
  clean_lore.py --apply  rewrites the files (same JSON layout: indent 2, UTF-8, no ASCII escaping)
"""
import json, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from lore_text import *

def clean(d):
    changes = []
    lg = d.get("legend")
    if isinstance(lg, str):
        keep = [s for s in sentences(lg) if not DEV_NOTE.search(s)]
        if len(keep) != len(sentences(lg)):
            changes.append(("legend", [s for s in sentences(lg) if DEV_NOTE.search(s)]))
            d["legend"] = " ".join(keep)
    pools = [("botLines", d.get("botLines"))] + [("yells." + k, v) for k, v in (d.get("yells") or {}).items()]
    for name, pool in pools:
        if not isinstance(pool, list): continue
        seen, out = set(), []
        for e in pool:
            t = entry_text(e)
            if t in seen: continue
            seen.add(t); out.append(e)
        if len(out) != len(pool):
            changes.append((name, [f"{len(pool) - len(out)} duplicate(s)"]))
            pool[:] = out
    return changes

def main():
    apply = "--apply" in sys.argv
    total = 0
    for f in lore_files():
        raw = open(f, encoding="utf-8").read()
        d = json.loads(raw)
        ch = clean(d)
        if not ch: continue
        total += 1
        print(os.path.relpath(f, LORE_DIR))
        for k, v in ch:
            for x in v: print("   ", k, "|", x[:150])
        if apply:
            indent = 2 if raw.startswith('{\n  "') else 4 if raw.startswith('{\n    "') else 2
            open(f, "w", encoding="utf-8", newline="\n").write(json.dumps(d, indent=indent, ensure_ascii=False) + ("\n" if raw.endswith("\n") else ""))
    print(f"{total} file(s) {'changed' if apply else 'would change'}")

if __name__ == "__main__":
    main()
