#!/usr/bin/env python3
"""Lint the lore files. Exit 1 on any problem.

Hard rules (always zero):
  - developer notes in player-facing text (DEV_NOTE in lore_text.py)
  - duplicate entries inside a yell or bot-line pool
Ratchets (may only go down; lower the number when you fix some):
  - template labels such as "Superstition:" and "Nicknames:" at the start of a paragraph
"""
import json, re, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from lore_text import *

TEMPLATE_LABEL = re.compile(r"(?:^|\n\n|(?<=[.!?]) )(Superstition|Nicknames?|Rumou?rs?|Trivia):")
TEMPLATE_LABEL_CAP = 69  # 39 Superstition + 28 Nicknames + 2 Nickname on 2026-11-20; step 1.2.1/lore-4 rewrites them

def texts(d):
    for k in ("legend", "bookPage", "story"):
        if isinstance(d.get(k), str): yield k, d[k]
    for i, c in enumerate(d.get("chapters") or []):
        if isinstance(c.get("text"), str): yield f"chapters[{i}]", c["text"]

def lint(root=LORE_DIR):
    problems, labels = [], 0
    for f in lore_files(root):
        rel = os.path.relpath(f, root).replace(os.sep, "/")
        d = json.load(open(f, encoding="utf-8"))
        for k, t in texts(d):
            for s in sentences(t):
                if DEV_NOTE.search(s): problems.append(f"{rel} {k}: developer note: {s[:100]}")
            labels += len(TEMPLATE_LABEL.findall(t))
        pools = [("botLines", d.get("botLines"))] + [("yells." + k, v) for k, v in (d.get("yells") or {}).items()]
        for name, pool in pools:
            t = [entry_text(e) for e in pool_entries(pool)]
            if len(t) != len(set(t)): problems.append(f"{rel} {name}: duplicate entry")
    if labels > TEMPLATE_LABEL_CAP: problems.append(f"template labels: {labels} found, cap is {TEMPLATE_LABEL_CAP}")
    return problems, labels

if __name__ == "__main__":
    problems, labels = lint()
    for p in problems: print("LORE LINT", p)
    print(f"lore lint: {len(problems)} problem(s), {labels} template label(s) (cap {TEMPLATE_LABEL_CAP})")
    sys.exit(1 if problems else 0)
