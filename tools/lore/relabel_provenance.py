#!/usr/bin/env python3
"""Relabel facts marked "mythic" whose source is not Mythic's own words as "legend".
  relabel_provenance.py          dry run
  relabel_provenance.py --apply  rewrite the files
"""
import json, re, sys, os, collections
sys.path.insert(0, os.path.dirname(__file__))
from lore_text import *

FACT = re.compile(r'("provenance"\s*:\s*")mythic("\s*,\s*"source"\s*:\s*")((?:[^"\\]|\\.)*)(")')

def relabel(raw):
    """Change the label in the text itself, so the file's layout is untouched. Returns (new text, count)."""
    count = 0
    def sub(m):
        nonlocal count
        if MYTHIC_SOURCE.search(json.loads('"' + m.group(3) + '"')): return m.group(0)
        count += 1
        return m.group(1) + "legend" + m.group(2) + m.group(3) + m.group(4)
    return FACT.sub(sub, raw), count

def main():
    apply = "--apply" in sys.argv
    n, files = 0, collections.Counter()
    for f in lore_files():
        raw = open(f, encoding="utf-8").read()
        new, count = relabel(raw)
        if not count: continue
        expect = sum(1 for x in all_facts(json.loads(raw)) if x.get("provenance") == "mythic" and not MYTHIC_SOURCE.search(x.get("source", "")))
        assert count == expect, f"{f}: text edit found {count}, the JSON has {expect}"
        n += count; files[os.path.dirname(os.path.relpath(f, LORE_DIR))] += 1
        if apply: open(f, "w", encoding="utf-8", newline="\n").write(new)
    print(f"{n} fact(s) {'relabelled' if apply else 'would be relabelled'} in {sum(files.values())} file(s) {dict(files)}")

if __name__ == "__main__":
    main()
