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
    # One spelling per hero in the lore (Cú Chulainn, Oisín); the patterns also catch an unaccented slip so it still counts.
    # "Fionn Óg of Connla" (Marfoirwort, Hy Brasil) is a different man and is not counted as Fionn mac Cumhaill.
    "Cú Chulainn": r"C[uú] Chulainn", "Fionn mac Cumhaill": r"Fionn(?! Óg)", "Oisín": r"Ois[ií]n",
    # Their legend companions (owner decision 5), counted by bare name and Welsh form. Arthur is the realm's king, not a
    # recurring witness, and Oscar and Mordred are bosses: none of the three is capped here. Lucan, Hjalmar and Nuada are
    # left out because the same names belong to others in the game (Hibernia's Guardian Lucan, the Icelord Hjalmar of
    # Tuscaran, the Overseer of Nuada's Silver Hand).
    "Kay": r"(?:Kay|Cei)", "Gareth": r"Gareth", "Percival": r"(?:Percival|Peredur)",
    "Lancelot": r"Lancelot", "Bors": r"Bors", "Tristan": r"Tristan", "Gaheris": r"Gaheris", "Agravaine": r"Agravaine",
    "Sigmund": r"Sigmund", "Sinfjötli": r"Sinfj[öo]tli", "Brynhild": r"Brynhild", "Regin": r"Regin",
    "Angantyr": r"Angantyr", "Starkad": r"Starkad",
    "Caílte": r"Ca[ií]lte", "Diarmuid": r"Diarmuid", "Goll mac Morna": r"Goll", "Conán Maol": r"Con[aá]n",
    "Conall Cernach": r"Conall", "Fergus mac Róich": r"Fergus", "Láeg": r"L[aá]eg",
    "Deirdre": r"Deirdre",
}
# Heroes held over the cap at a known count. Empty since the canon pass of 2026-11-20 brought every hero to 12 or fewer.
HERO_OVER_CAP = {}

HEROES_MD = os.path.join(os.path.dirname(__file__), "..", "..", "docs", "1.2.1", "HEROES.md")
# Owner decision 7 (2026-11-21): a hired companion's suggested name may be a lesser legend's, never a legend the lore tells
# of. These ordinary given names are allowed although a legend of the lore bears them, because present-day characters of
# the lore share them (decision 7 names Edith, Gunnar and Aine): Edith of the wash-house (and the Edith of Camelot in
# Tuka's topic); Gunnar Alfevson, the shade in Muspelheim (the Volsung Gunnar shares it); Aine the fire-keeper in
# Abomos's story (the goddess of Lough Gur shares it); Eirik in Audliten, the willow-branch quest giver (Ragnar's son
# Eirik in history/midgard shares it).
SHARED_GIVEN_NAMES = {"Edith", "Gunnar", "Aine", "Eirik"}
# Independent review (2026-11-21): a legend told beside a god or a myth figure who is not a HEROES legend ("Fand left
# Manannan") was missed, so these also mark a sentence as legend-age. Folded spellings; checked against the lore and the
# companion list (only Eirik, a shared name, sits in such a sentence).
LEGEND_ANCHORS = ["Arthur", "Merlin", "Morgana", "Arawn", "Mordred", "Uther", "Guinevere", "Nimue", "Ector", "Lamorak",
                  "Pellinore", "Palomides", "Lucan", "Odin", "Thor", "Loki", "Freyja", "Frigg", "Baldur", "Tyr", "Ivar",
                  "Beowulf", "Manannan", "Lugh", "Dagda", "Morrigan", "Nuada", "Balor", "Aengus", "Oengus", "Boann", "Oscar",
                  "Medb", "Conchobar", "Emer", "Scathach", "Ferdiad"]
_CAPITALISED = re.compile(r"\b[A-Z][a-z]+\b")

def fold(text):
    """Accents off (Gráinne -> Grainne, Áine -> Aine), so a companion's plain spelling meets the lore's accented one."""
    import unicodedata
    return "".join(c for c in unicodedata.normalize("NFD", text) if unicodedata.category(c) != "Mn")

def legend_names(root=LORE_DIR, heroes_md=HEROES_MD):
    """Every name the lore tells of as a legend, folded: name -> where it was found.
      - the HEROES patterns above (the realms' leaders and legend companions);
      - the roster table in docs/1.2.1/HEROES.md (leaders and companions columns);
      - every capitalised word of a fact marked "myth" (the folklore texts the lore retells; "legend" and "game" facts
        cite game guides and the world, so they name present-day NPCs, not legends);
      - every capitalised word of a player-visible sentence that also names a HEROES legend or a LEGEND_ANCHORS figure
        (a legend-age sentence: "Diarmuid fled with Grainne", "Fand left Manannan").
    Over-inclusion is harmless: only the companion names are looked up in it."""
    import fourth_wall
    out = {}
    def add(word, where): out.setdefault(fold(word), where)
    for h in HEROES: add(h.split()[0], "name_counts.HEROES")
    if os.path.exists(heroes_md):
        rows = [l for l in open(heroes_md, encoding="utf-8") if l.startswith("| Albion") or l.startswith("| Midgard") or l.startswith("| Hibernia")]
        for row in rows:
            cells = row.split("|")
            for cell in cells[2:4]:
                for w in _CAPITALISED.findall(fold(cell)): add(w, "HEROES.md roster")
    hero = re.compile(r"\b(?:" + "|".join(list(HEROES.values()) + LEGEND_ANCHORS) + r")\b")
    for f in lore_files(root):
        d = json.load(open(f, encoding="utf-8"))
        folder = os.path.basename(os.path.dirname(f)); rel = f"{folder}/{os.path.basename(f)}"
        for x in all_facts(d):
            if x.get("provenance") == "myth":
                for w in _CAPITALISED.findall(fold(x.get("text", ""))): add(w, f"{rel} myth fact")
        for _, text, _ in fourth_wall.visible(d, folder):
            for s in sentences(text):
                if hero.search(s) or hero.search(fold(s)):
                    for w in _CAPITALISED.findall(fold(s)): add(w, f"{rel} legend-age sentence")
    return out

def companion_clashes(names, root=LORE_DIR, heroes_md=HEROES_MD):
    """The suggested companion names that are legends the lore tells of: [(name, where)]."""
    legends = legend_names(root, heroes_md)
    return [(n, legends[fold(n)]) for n in names if fold(n) in legends and fold(n) not in SHARED_GIVEN_NAMES]

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
