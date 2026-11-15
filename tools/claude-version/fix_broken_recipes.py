"""The recipe data fixes found by the crafting M0 audit (docs/CRAFTING-M0-FINDINGS.md, 2026-11-15). PROPOSED: not applied
to any world yet. Run it on the developer's world with the server stopped; the next clean build carries it, and
make_patch.py turns it into the patch's world update (CraftedXItem and ItemTemplate rows).

1. stable_cold_fine_alloy_tincture3 (recipe 14388, Hibernian alchemy, level 659) has no ingredients: its three
   CraftedXItem rows were keyed "stable_cold_fine_alloy__incture3" (a typo). They are re-keyed.
2. footed_flight_broadhead_arrows3 (recipe 16117, Hibernian fletching, level 490) has no ingredients. It gets the three
   its Hibernian siblings at the same level use (keen/blunt/barbed_footed_flight_broadhead_arrows3): heartwood wooden
   boards, phoenix feathers, dolomite metal bars, one each.
3. Eight recipe products carry the wrong realm, so no one (realm 0, with allow_craft_norealm_items off) or only the
   wrong realm can craft them. Each gets the realm of its recipe's id block and name suffix (Albion 1-6999, Midgard
   7000-13999 "...2", Hibernia 14000-20999 "...3"). Hibernian bow and arrow merchants already sell runewood_recurve_bow3.

Not fixed here (see the findings): 38 recipes whose product is a statless generic object in an armor or weapon slot
(the server now hides them), and 189 recipes with an ingredient no merchant sells and no monster drops.

The old values go into fix_broken_recipes.ledger.json for rollback.

  python fix_broken_recipes.py --dry-run   # what would change
  python fix_broken_recipes.py             # server stopped; backs up the DB, applies, checks, writes the ledger
  python fix_broken_recipes.py rollback    # undoes every change from the ledger
"""
import datetime, json, os, shutil, sqlite3, subprocess, sys, uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DB = os.path.join(ROOT, "runtime", "data", "opendaoc.sqlite3.db")
LEDGER = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fix_broken_recipes.ledger.json")

TYPO_KEY = ("stable_cold_fine_alloy__incture3", "stable_cold_fine_alloy_tincture3")
NEW_INGREDIENTS = ("footed_flight_broadhead_arrows3",
                   [("heartwood_wooden_boards", 1), ("phoenix_feathers", 1), ("dolomite_metal_bars", 1)])
REALMS = {  # product Id_nb: (realm today, realm it gets)
    "runewood_recurve_bow3": (0, 3),
    "copper_two-hand_spiked_mace3": (0, 3),
    "sapphire_two-hand_spiked_mace3": (0, 3),
    "diamond_two-hand_spiked_mace3": (0, 3),
    "netherite_two-hand_spiked_mace3": (0, 3),
    "arcanite_two-hand_spiked_mace3": (0, 3),
    "diamond_mace3": (1, 3),
    "bronze_long_sword2": (1, 2),
}
# Fixed ids, so every world that gets this fix (and the patch built from it) has the same rows.
NAMESPACE = uuid.UUID("6f1e0c52-3b0e-4d55-9a8e-6c2c0a8f0a00")


def new_row_id(product, ingredient):
    return str(uuid.uuid5(NAMESPACE, f"{product}|{ingredient}"))


def plan(con):
    """What applying would change, checked against the world as it is; raises if the world is not the audited one."""
    typo = con.execute("select CraftedXItem_ID, IngredientId_nb, Count from CraftedXItem where CraftedItemId_nb=?",
                       (TYPO_KEY[0],)).fetchall()
    have = con.execute("select count(*) from CraftedXItem where CraftedItemId_nb=?", (NEW_INGREDIENTS[0],)).fetchone()[0]
    for ingredient, _ in NEW_INGREDIENTS[1]:
        assert con.execute("select 1 from ItemTemplate where Id_nb=?", (ingredient,)).fetchone(), ingredient
    realms = {}
    for idnb, (old, new) in REALMS.items():
        row = con.execute("select Realm from ItemTemplate where Id_nb=?", (idnb,)).fetchone()
        if row is not None and row[0] == old:
            realms[idnb] = (old, new)
    return {"rekey": [r[0] for r in typo],
            "insert": [] if have else [(new_row_id(NEW_INGREDIENTS[0], i), i, n) for i, n in NEW_INGREDIENTS[1]],
            "realms": realms}


def apply(con):
    p = plan(con)
    n = 0
    for row_id in p["rekey"]:
        n += con.execute("update CraftedXItem set CraftedItemId_nb=? where CraftedXItem_ID=? and CraftedItemId_nb=?",
                         (TYPO_KEY[1], row_id, TYPO_KEY[0])).rowcount
    for row_id, ingredient, count in p["insert"]:
        n += con.execute("insert into CraftedXItem (CraftedItemId_nb, IngredientId_nb, Count, LastTimeRowUpdated, CraftedXItem_ID) "
                         "values (?, ?, ?, '2000-01-01 00:00:00', ?)", (NEW_INGREDIENTS[0], ingredient, count, row_id)).rowcount
    for idnb, (old, new) in p["realms"].items():
        n += con.execute("update ItemTemplate set Realm=? where Id_nb=? and Realm=?", (new, idnb, old)).rowcount
    expected = len(p["rekey"]) + len(p["insert"]) + len(p["realms"])
    assert n == expected, (n, expected)
    return p


def rollback(con, ledger):
    n = 0
    for row_id in ledger["rekey"]:
        n += con.execute("update CraftedXItem set CraftedItemId_nb=? where CraftedXItem_ID=? and CraftedItemId_nb=?",
                         (TYPO_KEY[0], row_id, TYPO_KEY[1])).rowcount
    for row_id, _, _ in ledger["insert"]:
        n += con.execute("delete from CraftedXItem where CraftedXItem_ID=?", (row_id,)).rowcount
    for idnb, (old, new) in ledger["realms"].items():
        n += con.execute("update ItemTemplate set Realm=? where Id_nb=? and Realm=?", (old, idnb, new)).rowcount
    expected = len(ledger["rekey"]) + len(ledger["insert"]) + len(ledger["realms"])
    assert n == expected, (n, expected)
    return n


def describe(p):
    print(f"re-key {len(p['rekey'])} CraftedXItem rows {TYPO_KEY[0]} -> {TYPO_KEY[1]}")
    print(f"add {len(p['insert'])} CraftedXItem rows to {NEW_INGREDIENTS[0]}: {[i for _, i, _ in p['insert']]}")
    for idnb, (old, new) in sorted(p["realms"].items()):
        print(f"  ItemTemplate {idnb}: Realm {old} -> {new}")


if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else "apply"
    if action == "--dry-run":
        with sqlite3.connect(f"file:{DB}?mode=ro", uri=True) as ro:
            describe(plan(ro))
        sys.exit()
    if os.name == "nt" and "coreserver" in subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower():
        sys.exit("CoreServer is running; stop the server first.")
    backup = DB.replace(".db", f".before-recipe-fixes-{action}-{datetime.datetime.now():%Y%m%d-%H%M%S}.db")
    shutil.copy2(DB, backup)
    print("backup", backup)
    con = sqlite3.connect(DB)
    try:
        if action == "rollback":
            print("undid", rollback(con, json.load(open(LEDGER))), "changes")
            con.commit()
            os.remove(LEDGER)
            sys.exit()
        assert not os.path.exists(LEDGER), "already applied (see the ledger); roll back first"
        p = apply(con)
        con.commit()
        describe(p)
        json.dump({"applied": datetime.datetime.now().isoformat(), **p}, open(LEDGER, "w"), indent=1)
        print("ledger", LEDGER)
    finally:
        con.close()
