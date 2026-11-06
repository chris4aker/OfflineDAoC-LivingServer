"""Lets players create Necromancers again (owner 2026-10-30: reports that Britons couldn't make a Necromancer).

The disabled_classes server setting held 20, the Disciple: the class a new Necromancer starts as (levels 1-4, like
the Acolyte or Elementalist), and what the client sends when a Necromancer is created. So the server refused every new
Necromancer (Briton, Inconnu or Saracen) for normal accounts; GM accounts skip that check, so it could seem to work.
Necromancer is a Shrouded Isles class (1.65). This removes only the 20 from disabled_classes; the other entries stay.

    python enable_necromancer_creation.py          (server stopped; backs up the database first; safe to rerun)
    python enable_necromancer_creation.py --dry    (shows the change only)
"""
import datetime, os, sqlite3, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DB = os.path.join(HERE, "..", "..", "runtime", "data", "opendaoc.sqlite3.db")
DISCIPLE = "20"


def main():
    dry = "--dry" in sys.argv
    if not dry and "coreserver" in subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower():
        sys.exit("CoreServer is running; stop the server first.")
    con = sqlite3.connect(DB)
    row = con.execute("select Value from ServerProperty where `Key` = 'disabled_classes'").fetchone()
    if row is None:
        print("disabled_classes is not set: Necromancers can already be created")
        return
    entries = [entry for entry in row[0].split(";") if entry.strip()]
    kept = [entry for entry in entries if entry.strip() != DISCIPLE]
    if len(kept) == len(entries):
        print(f"disabled_classes = '{row[0]}': Necromancers can already be created")
        return
    value = ";".join(kept)
    print(f"disabled_classes: '{row[0]}' -> '{value}'")
    if dry:
        return
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = sqlite3.connect(os.path.join(os.path.dirname(DB), f"opendaoc.sqlite3.before-necromancer-creation-{stamp}.db"))
    con.backup(backup)
    backup.close()
    with con:
        con.execute("update ServerProperty set Value = ? where `Key` = 'disabled_classes'", (value,))
    print("Necromancers can be created again (takes effect at the next server start)")


if __name__ == "__main__":
    main()
