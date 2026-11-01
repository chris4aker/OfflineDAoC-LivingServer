"""Quest rewards with real 1.65 stats (owner 2026-10-30: fix the invented and weakened quest rewards, normal mode too).

The classic quest rewards were installed by install_classic_reward_items.py from the MMOEngine pipeline's rows. A 1.65
audit (research/1.65-mode/rewards, steps s13-s17) found:
  - 20 rewards whose stats were made up have a real Allakhazam page under another spelling: they now use that page
    (level, AF or DPS/speed, bonuses);
  - 23 more made-up rewards have a real sibling page (another piece of the same set, or the same reward made for
    another class): they take its bonuses, with the item's own weapon skill, and its AF/DPS when within 2 levels;
  - 2 made-up rewards stronger than any real reward of their slot and level are scaled to the period median;
  - 23 rewards lost real 1.65 bonuses the pipeline had no mapping for (Smiting, Soulrending, Arboreal Path, Light and
    Mana skills, the elemental / Body / Mind / Spirit staff focus levels): put back.
Later forms are turned back into the 1.65 ones: Power Pool % -> Power, All Archery -> the realm's bow skill. Bonuses of
Catacombs classes (Vampiir, Warlock, Bainshee, Mauler lines) are not added. Hunter's Mane is left as it is.

The exact changes are in quest_reward_fixes.json next to this file (Id_nb, before, after). A row is changed only when
each of its columns still holds the "before" or the "after" value, so the tool is safe to rerun. The same changes go into
the research copies (research/period-data/quests/reward_item_rows.json and reward_item_rows_mmoengine.json), so a
reinstall keeps them.

    python fix_quest_reward_stats.py          (server stopped; backs up the database first)
    python fix_quest_reward_stats.py --dry    (shows what would change)
"""
import datetime, json, os, sqlite3, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
DB = os.path.join(ROOT, "runtime", "data", "opendaoc.sqlite3.db")
QUESTS = os.path.join(ROOT, "development-source", "research", "period-data", "quests")
FIXES = json.load(open(os.path.join(HERE, "quest_reward_fixes.json"), encoding="utf-8"))


def main():
    dry = "--dry" in sys.argv
    if not dry and "coreserver" in subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower():
        sys.exit("CoreServer is running; stop the server first.")
    con = sqlite3.connect(DB)
    todo, done, skipped = [], 0, []
    for fix in FIXES:
        cols = list(fix["after"])
        cur = con.execute(f"select {', '.join(cols)} from ItemTemplate where Id_nb = ?", (fix["Id_nb"],)).fetchone()
        if cur is None:
            skipped.append((fix["Name"], "not in the database"))
            continue
        now = dict(zip(cols, cur))
        if now == fix["after"]:
            done += 1
        elif all(now[k] in (fix["before"][k], fix["after"][k]) for k in cols):
            todo.append(fix)          # untouched, or fixed by an earlier version of this list
        else:
            skipped.append((fix["Name"], "changed since the audit"))
    print(f"{len(todo)} rewards to fix, {done} already fixed, {len(skipped)} skipped")
    for name, why in skipped:
        print(f"  skipped {name}: {why}")
    if dry or not todo:
        return
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = sqlite3.connect(os.path.join(os.path.dirname(DB), f"opendaoc.sqlite3.before-quest-reward-fixes-{stamp}.db"))
    con.backup(backup)
    backup.close()
    with con:
        for fix in todo:
            sets = ", ".join(f"{k} = ?" for k in fix["after"])
            con.execute(f"update ItemTemplate set {sets}, LastTimeRowUpdated = ? where Id_nb = ?",
                        (*fix["after"].values(), datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), fix["Id_nb"]))
    bad = [f["Name"] for f in todo if dict(zip(f["after"], con.execute(
        f"select {', '.join(f['after'])} from ItemTemplate where Id_nb = ?", (f["Id_nb"],)).fetchone())) != f["after"]]
    print(f"{len(todo) - len(bad)} rewards fixed" + (f"; NOT applied: {bad}" if bad else ""))
    by_id = {f["Id_nb"]: f["after"] for f in FIXES}
    for name in ("reward_item_rows.json", "reward_item_rows_mmoengine.json"):
        path = os.path.join(QUESTS, name)
        if not os.path.exists(path):
            continue
        rows = json.load(open(path, encoding="utf-8"))
        n = 0
        for r in rows:
            if r.get("Id_nb") in by_id:
                r.update(by_id[r["Id_nb"]])
                n += 1
        json.dump(rows, open(path, "w", encoding="utf-8"), indent=1)
        print(f"{name}: {n} rows updated")


if __name__ == "__main__":
    main()
