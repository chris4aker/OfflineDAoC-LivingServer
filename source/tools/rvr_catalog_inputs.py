#!/usr/bin/env python3
"""World inputs of the two RvR data catalogs (bots/autonomous/data/rvr-lanes.json and rvr-checkspots.json).

  release-world <world.db> <out.db>   Copies a world and applies source/tools/release-world-defaults.json as
                                      build_release_037.py does (Keep Realm, Level and claim; Door Health and State).
                                      Use the copy as --db for build_rvr_lanes.py and rvr_checkspots.py request.
  digest <world.db> [...]             Prints a SHA-256 of exactly the rows and columns each generator reads, and of the
                                      columns a release world resets, so two worlds can be compared.

build_rvr_lanes.py reads Keep (KeepID, Name, X, Y, Z, BaseLevel), Zones and the X, Y of level 50+ aggressive realm-0
Mob rows. rvr_checkspots.py request reads Zones and realm-0 Mob rows (X, Y, Z, Level, AggroLevel, AggroRange,
RoamingRange, ClassType). Neither reads keep owners, keep levels, claims, door state or any saved-progress table.
"""
import hashlib
import json
import os
import shutil
import sqlite3
import sys

FRONTIERS = (1, 100, 200)
QUERIES = {
    'lanes: Keep': "select KeepID,Name,X,Y,Z,BaseLevel from Keep where Region in (1,100,200) order by KeepID",
    'lanes: Mob camps': "select Region,X,Y from Mob where Region in (1,100,200) and Level>=50 and Realm=0 and AggroLevel>0 "
                        "order by 1,2,3",
    'both: Zones': "select ZoneID,RegionID,OffsetX,OffsetY,Width,Height from Zones where RegionID in (1,100,200) order by 1",
    'checkspots: Mob': "select Region,X,Y,Z,Level,AggroLevel,AggroRange,RoamingRange,ClassType from Mob "
                       "where Region in (1,100,200) and Realm=0 and Level>0 order by 1,2,3,4,5,6,7,8,9",
    'not read: Keep owner/level/claim': "select KeepID,Realm,Level,coalesce(ClaimedGuildName,'') from Keep order by 1",
    'not read: Door health/state': "select Door_ID,Health,State from Door order by 1",
}
DEFAULTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'release-world-defaults.json')


def release_world(src, dst):
    with open(DEFAULTS, encoding='utf-8') as f:
        defaults = json.load(f)
    shutil.copyfile(src, dst)
    con = sqlite3.connect(dst)
    for keep_id, keep in defaults['Keep'].items():
        n = con.execute("UPDATE Keep SET Realm=?,Level=?,ClaimedGuildName='' WHERE KeepID=?",
                        (keep['Realm'], keep['Level'], int(keep_id))).rowcount
        assert n == 1, 'keep %s %s' % (keep_id, keep['Name'])
    for door_id, door in defaults['Door'].items():
        n = con.execute("UPDATE Door SET Health=?,State=? WHERE Door_ID=?", (door['Health'], door['State'], door_id)).rowcount
        assert n == 1, 'door %s' % door_id
    assert {r[0] for r in con.execute('SELECT KeepID FROM Keep')} == {int(k) for k in defaults['Keep']}, 'keep list differs'
    assert {r[0] for r in con.execute('SELECT Door_ID FROM Door')} == set(defaults['Door']), 'door list differs'
    con.commit()
    con.close()
    print('%s: %d keeps and %d doors set to the release defaults' % (dst, len(defaults['Keep']), len(defaults['Door'])))


def digest(paths):
    for path in paths:
        con = sqlite3.connect('file:%s?mode=ro' % path, uri=True)
        print(path)
        for name, query in QUERIES.items():
            rows = con.execute(query).fetchall()
            print('  %-34s %6d rows  %s' % (name, len(rows), hashlib.sha256(repr(rows).encode()).hexdigest()[:16]))
        con.close()


def main():
    if len(sys.argv) == 4 and sys.argv[1] == 'release-world':
        release_world(sys.argv[2], sys.argv[3])
    elif len(sys.argv) >= 3 and sys.argv[1] == 'digest':
        digest(sys.argv[2:])
    else:
        raise SystemExit(__doc__)


if __name__ == '__main__':
    main()
