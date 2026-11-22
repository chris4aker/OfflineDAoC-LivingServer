#!/usr/bin/env python3
"""Lint the lore files. Exit 1 on any problem.

Hard rules (always zero):
  - developer notes in player-facing text (DEV_NOTE in lore_text.py)
  - duplicate entries inside a yell or bot-line pool
  - a fact labelled "mythic" whose source is not a Herald page, darkageofcamelot.com or a Mythic note
  - a real company, site, product, patch, developer or game-software word in a player-visible field, or a game term
    in a narrative field (owner decision 8, 2026-11-21; the rules and the field map are in fourth_wall.py)
Failures while fourth_wall.GAME_TERMS_FAIL is True (on since 2026-11-22; with it False they print as warnings, exit 0):
  - a game term in ANY player-visible field, legends and bot lines included (owner 2026-11-21, "Rewrite that";
    fourth_wall.GAME_TERMS). The open list is docs/1.2.1/LINT-LEGENDS-HITS.md.
Ratchets (may only go down; lower the number when you fix some):
  - template labels such as "Superstition:" and "Nicknames:" at the start of a paragraph
"""
import json, re, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from lore_text import *
import fourth_wall

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
        for x in all_facts(d):
            if x.get("provenance") == "mythic" and not MYTHIC_SOURCE.search(x.get("source", "")):
                problems.append(f"{rel}: \"mythic\" fact from a non-Mythic source ({x.get('source','')[:60]}): {x.get('text','')[:70]}")
        pools = [("botLines", d.get("botLines"))] + [("yells." + k, v) for k, v in (d.get("yells") or {}).items()]
        for name, pool in pools:
            t = [entry_text(e) for e in pool_entries(pool)]
            if len(t) != len(set(t)): problems.append(f"{rel} {name}: duplicate entry")
    problems += fourth_wall.problems(root)
    if labels > TEMPLATE_LABEL_CAP: problems.append(f"template labels: {labels} found, cap is {TEMPLATE_LABEL_CAP}")
    return problems, labels

def warnings(root=LORE_DIR):
    """Game terms in legends, bot lines and told text while fourth_wall.GAME_TERMS_FAIL is False (else lint() fails them)."""
    return fourth_wall.warnings(root)

if __name__ == "__main__":
    # --game-terms-fail: fail on the game-term warnings too, as CI will once fourth_wall.GAME_TERMS_FAIL is True.
    if "--game-terms-fail" in sys.argv: fourth_wall.GAME_TERMS_FAIL = True
    problems, labels = lint()
    warns = warnings()
    for w in warns: print("LORE LINT WARNING", w)
    for p in problems: print("LORE LINT", p)
    print(f"lore lint: {len(problems)} problem(s), {len(warns)} game-term warning(s), {labels} template label(s) (cap {TEMPLATE_LABEL_CAP})")
    sys.exit(1 if problems else 0)
