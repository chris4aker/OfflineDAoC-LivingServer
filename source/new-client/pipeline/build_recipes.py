"""The crafting window's recipe lists (owner 2026-10-30: "work on ... the crafting UI for new client").

The server never sends a recipe list - the original client built its TradeSkillWindow from its own tdl data, and so
does the new client: the Claude version server's own recipe tables (CraftedItem + CraftedXItem + ItemTemplate) - its
ids are the ones its MakeProduct handler (0xED) looks up - grouped as the 1.64 client's recipe file (research/period-data/
client-1.64/ifd/tdl.irf) groups the same products.
Each recipe: id, product name, the base product it is a tier of (the window's tree: trade > product > material tier),
item level, recipe skill level, product model (its icon), ingredients [[name, count], ...]. Grouped per realm
(1 Albion, 2 Midgard, 3 Hibernia) and trade (eCraftingSkill id).
Writes godot/content/crafting/recipes.json. Reads the server database read-only.

    python pipeline/build_recipes.py
"""
import collections
import csv
import json
import sqlite3
from pathlib import Path

TOP = Path(__file__).resolve().parent.parent
SERVER = TOP.parent / 'new class test - CLAUDE VERSION'
DB = SERVER / 'runtime/data/opendaoc.sqlite3.db'
TDL = SERVER / 'development-source/research/period-data/client-1.64/ifd/tdl.irf'
OUT = TOP / 'godot/content/crafting/recipes.json'

# eCraftingSkill (GameServer/craft/CraftingMgr.cs) and the names the server gives them (languages/EN/Crafting.txt)
SKILLS = {1: 'Weaponcraft', 2: 'Armorcraft', 3: 'Siegecraft', 4: 'Alchemy', 6: 'Metalworking', 7: 'Leathercrafting',
          8: 'Clothworking', 9: 'Gemcutting', 10: 'Herbcraft', 11: 'Tailoring', 12: 'Fletching', 13: 'Spellcrafting',
          14: 'Woodworking', 15: 'Basic Crafting'}
# the 1.64 file's material codes -> the product name's prefix (a fallback: the prefixes are learned from the recipes
# whose ids name the same product in the server's tables, which covers the arrow / bolt codes too)
PREFIX = {}
for codes, names in [
        ('BRON IRON STEE ALLO FALL MITH ADAM ASTE MET9 ME10',
         'bronze|iron|steel|alloy|fine alloy|mithril|adamantium|asterite|netherium|arcanium'),
        ('ORE1 ORE2 ORE3 ORE4 ORE5 ORE6 ORE7 ORE8 ORE9 OR10',
         'copper|ferrite|quartz|dolomite|cobalt|carbide|sapphire|diamond|netherite|arcanite'),
        ('WOO1 WOO2 WOO3 WOO4 WOO5 WOO6 WOO7 WOO8 WOO9 WO10',
         'rowan|elm|oaken|ironwood|heartwood|runewood|stonewood|ebonwood|dyrwood|duskwood'),
        ('LEA1 LEA2 LEA3 LEA4 LEA5 LEA6 LEA7 LEA8 LEA9 LE10',
         'rawhide|tanned|cured|hard|rigid|embossed|imbued|runed|eldritch|tempered'),
        ('CLO1 CLO2 CLO3 CLO4 CLO5 CLO6 CLO7 CLO8 CLO9 CL10',
         'woolen|linen|brocade|silk|gossamer|sylvan|seamist|nightshade|wyvernskin|silksteel'),
        ('NAT1 NAT2 NAT3 NAT4 NAT5 NAT6 NAT7 NAT8 NAT9 NA10',
         'leaf|bone|vine|shell|fossil|amber|coral|chitin|petrified|crystalized'),
        ('GEM0 GEM1 GEM2 GEM3 GEM4 GEM5 GEM6 GEM7 GEM8 GEM9',
         'raw|uncut|rough|flawed|imperfect|polished|faceted|precious|flawless|perfect')]:
    for c, n in zip(codes.split(), names.split('|')):
        PREFIX[c] = n
ALL_PREFIXES = sorted(set(PREFIX.values()), key=len, reverse=True)


def tdl_rows():
    """[(row fields, realm, trade)] of the 1.64 file's recipe rows, and its alchemy category labels."""
    rows, labels = [], {}
    with open(TDL, encoding='latin1', newline='') as f:
        for r in csv.reader(f):
            if not r or r[0].startswith(';') or not r[0].strip().lstrip('-').isdigit():
                continue
            rid = int(r[0])
            if rid == 0:
                continue                                    # a trade's header row
            if len(r) > 6 and r[6].strip() == '99':
                labels[(int(r[3]), int(r[1]), int(r[2]))] = r[7].strip()   # an alchemy category label
                continue
            rows.append(r)
    return rows, labels


def base_of(name):
    """The product without its material prefix ("bronze dagger" -> "dagger")."""
    low = name.lower()
    for p in ALL_PREFIXES:
        if low.startswith(p + ' '):
            return name[len(p) + 1:]
    return name


def main():
    db = sqlite3.connect(DB.as_uri() + '?mode=ro', uri=True)
    items = {r[0].lower(): {'name': r[1], 'model': r[2] or 0, 'level': r[3] or 0, 'realm': r[4] or 0}
             for r in db.execute('SELECT Id_nb, Name, Model, Level, Realm FROM ItemTemplate')}
    by_name = collections.defaultdict(list)
    for v in items.values():
        by_name[v['name'].lower()].append(v)
    ingredients = collections.defaultdict(list)
    for prod, ing, count in db.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
        it = items.get((ing or '').lower())
        ingredients[(prod or '').lower()].append([it['name'] if it else ing, int(count or 0)])
    crafted = {}
    for cid, prod, level, skill in db.execute('SELECT CraftedItemID, Id_nb, CraftingLevel, CraftingSkillType FROM CraftedItem'):
        try:
            crafted[int(cid)] = ((prod or '').lower(), int(level or 0), int(skill or 0))
        except ValueError:
            continue

    rows, labels = tdl_rows()
    tdl_by_id = {int(r[0]): r for r in rows}

    # material prefixes learned from the recipes both lists agree on (same id, the product ends with the 1.64 base)
    votes = collections.defaultdict(collections.Counter)
    for rid, r in tdl_by_id.items():
        c = crafted.get(rid)
        it = items.get(c[0]) if c else None
        base, code = r[7].strip(), r[8].strip()
        if it and code and code != 'GENE' and it['name'].lower().endswith(' ' + base.lower()):
            votes[code][it['name'][:-len(base) - 1].strip().lower()] += 1
    prefix = dict(PREFIX)
    for code, counter in votes.items():
        prefix[code] = counter.most_common(1)[0][0]

    def named(base, code):
        p = prefix.get(code, '') if code and code not in ('0', 'GENE') else ''
        return (p + ' ' + base).strip() if p else base

    def tdl_ingredients(r):
        out = []
        for k in range(11, min(len(r), 61), 5):
            iname = r[k].strip()
            if iname not in ('', '0'):
                out.append([named(iname, r[k + 1].strip()), int(r[k + 2] or 0)])
        return out

    # the 1.64 file's ingredient names -> the items' own names ("rowan wood boards" is the item "rowan wooden boards"):
    # learned from the recipes both lists share, ingredient by ingredient where the counts line up
    ing_votes = collections.defaultdict(collections.Counter)
    for rid, r in tdl_by_id.items():
        c = crafted.get(rid)
        if not c:
            continue
        mine, theirs = tdl_ingredients(r), ingredients.get(c[0], [])
        if len(mine) == len(theirs):
            for (a_name, a_n), (b_name, b_n) in zip(mine, theirs):
                if a_n == b_n:
                    ing_votes[a_name.lower()][b_name] += 1
    ing_name = {k: v.most_common(1)[0][0] for k, v in ing_votes.items()}

    # --- normal: the server's own tables, grouped like the 1.64 file where an id names the same product
    outn = collections.defaultdict(lambda: collections.defaultdict(list))
    for cid, (prod, level, skill) in crafted.items():
        it = items.get(prod)
        if it is None or skill not in SKILLS:
            continue
        r = tdl_by_id.get(cid)
        same = r is not None and int(r[1]) == skill and named(r[7].strip(), r[8].strip()).lower() == it['name'].lower()
        base = r[7].strip() if same else base_of(it['name'])
        realms = [it['realm']] if it['realm'] in (1, 2, 3) else [1, 2, 3]
        for realm in realms:
            outn[str(realm)][str(skill)].append({
                'id': cid, 'name': it['name'], 'base': base,
                'group': labels.get((realm, skill, int(r[2])), '') if same else '',
                'category': int(r[2]) if same else 999, 'level': it['level'], 'skill': level, 'model': it['model'],
                'ingredients': ingredients.get(prod, [])})

    def tidy(tree):
        return {realm: {trade: sorted(lst, key=lambda e: (e['category'], e['base'].lower(), e['skill'], e['id']))
                        for trade, lst in sorted(trades.items(), key=lambda kv: int(kv[0]))}
                for realm, trades in sorted(tree.items())}

    out = {'skills': {str(k): v for k, v in SKILLS.items()}, 'normal': tidy(outn),
           'source': 'pipeline/build_recipes.py: the server database'}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, separators=(',', ':')), encoding='utf-8')
    for mode in ('normal',):
        counts = {realm: sum(len(v) for v in trades.values()) for realm, trades in out[mode].items()}
        print(mode, counts, 'recipes')
    unknown = sorted({r[8].strip() for r in rows if r[8].strip() not in prefix and r[8].strip() not in ('', '0', 'GENE')})
    print('material codes without a prefix:', unknown)
    print('learned prefixes:', {k: v for k, v in sorted(prefix.items()) if k not in PREFIX})


if __name__ == '__main__':
    main()
