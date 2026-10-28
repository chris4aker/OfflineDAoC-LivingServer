"""Write each place's monster level range into data/chat/places.json, from a world database.

The talking update: bots answer "what lvl is vendo caverns?" from the levels of the monsters that are really there
(the 20th to 80th percentile of the realm-0 mobs in the place's zones), not from memory. Battlegrounds keep their
bracket (BattlegroundBrackets) and capitals get none.

Usage: python tools/bot-voices/place_levels.py <opendaoc.sqlite3.db> [--check]
The database is opened read-only. --check prints the ranges without writing.
"""
import json
import sqlite3
import sys
from pathlib import Path

PLACES = Path(__file__).resolve().parents[2] / 'source/server/GameServer/bots/autonomous/data/chat/places.json'
BRACKETS = {'abermenai': (15, 19), 'thidranki': (20, 24), 'murdaigean': (25, 29), 'caledonia': (30, 35)}


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
    zones = {}
    for _, region, name, ox, oy, w, h in db.execute('select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones'):
        zones.setdefault(region, []).append((name, ox, oy, w, h))
    levels = {}
    for x, y, region, level in db.execute('select X, Y, Region, Level from Mob where Realm = 0 and Level > 0'):
        for name, ox, oy, w, h in zones.get(region, []):
            if 0 <= x / 8192 - ox < w and 0 <= y / 8192 - oy < h:
                levels.setdefault(name, []).append(level)
                break
    raw = PLACES.read_bytes().decode('utf-8')
    data = json.loads(raw)
    for place in data['places']:
        place.pop('levels', None)
        if place['key'] in BRACKETS:
            place['levels'] = list(BRACKETS[place['key']])
            continue
        if place.get('type') == 'capital':
            continue
        found = sorted(level for zone in place.get('zones', []) for level in levels.get(zone, []))
        if len(found) >= 15:
            place['levels'] = [found[int(len(found) * 0.2)], found[int(len(found) * 0.8)]]
    for place in data['places']:
        if 'levels' in place:
            print(f"{place.get('type', ''):12} {place['key']:28} {place['levels'][0]}-{place['levels'][1]}")
    if '--check' in sys.argv:
        return
    newline = '\r\n' if '\r\n' in raw else '\n'
    # The file's layout: a note, then one place per line.
    rows = [json.dumps(place, ensure_ascii=False) for place in data['places']]
    text = newline.join(['{', ' "note": ' + json.dumps(data['note'], ensure_ascii=False) + ',', ' "places": [']
                        + ['  ' + row + (',' if i < len(rows) - 1 else '') for i, row in enumerate(rows)] + [' ]', '}']) + newline
    PLACES.write_bytes(text.encode('utf-8'))
    print(f'wrote {PLACES}')


if __name__ == '__main__':
    main()
