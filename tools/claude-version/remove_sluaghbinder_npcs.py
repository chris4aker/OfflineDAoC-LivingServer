"""0.38: the Sluaghbinder is discontinued (owner 2026-10-30). Removes its trainer Muirenn and her bound sluagh wisp
from the normal world (Tir na Nog). The quest monsters were spawned by code and no longer start
(SluaghbinderEpicQuests.Discontinued). Spells, items and specs stay so an old character's things still resolve.

    python -I remove_sluaghbinder_npcs.py            (server must be stopped)
    python -I remove_sluaghbinder_npcs.py restore    (puts the two rows back from the backup)
"""
import json, os, sqlite3, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DB = os.path.normpath(os.path.join(HERE, '..', '..', 'runtime', 'data', 'opendaoc.sqlite3.db'))
BACKUP = os.path.join(HERE, 'remove_sluaghbinder_npcs.backup.json')
IDS = ('sluaghbinder_trainer_tir_na_nog', 'sluaghbinder_bound_wisp_tir_na_nog')


def main():
    if 'coreserver' in subprocess.run(['tasklist'], capture_output=True, text=True).stdout.lower():
        sys.exit('Stop the server first.')
    con = sqlite3.connect(DB)
    cols = [r[1] for r in con.execute('PRAGMA table_info(Mob)')]
    if len(sys.argv) > 1 and sys.argv[1] == 'restore':
        rows = json.load(open(BACKUP, encoding='utf-8'))
        with con:
            for row in rows:
                use = [c for c in cols if c in row]
                con.execute(f"INSERT OR REPLACE INTO Mob ({','.join(use)}) VALUES ({','.join('?' * len(use))})", [row[c] for c in use])
        print(f'restored {len(rows)}')
        return
    rows = [dict(zip(cols, r)) for r in con.execute(f"SELECT * FROM Mob WHERE Mob_ID IN ({','.join('?' * len(IDS))})", IDS)]
    if rows:
        json.dump(rows, open(BACKUP, 'w', encoding='utf-8'), indent=1)
    with con:
        n = con.execute(f"DELETE FROM Mob WHERE Mob_ID IN ({','.join('?' * len(IDS))})", IDS).rowcount
    print(f'removed {n} (backup {os.path.basename(BACKUP)})')


if __name__ == '__main__':
    main()
