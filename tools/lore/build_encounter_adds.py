#!/usr/bin/env python3
"""Writes source/server/GameServer/bots/autonomous/data/encounter_adds.json from the epic encounter catalog.

The lore engine (LoreBossWatch) counts a boss's adds for the "addSpawn", "orbDeath" and "addsDead" lore triggers; the
server has no catalog at run time, so this small map (boss script class -> add script classes, lower case) is built into
GameServer.dll. UT_LoreBible fails when it is out of date with docs/encounters/epic-encounters.json.

  python3 tools/lore/build_encounter_adds.py          write it
  python3 tools/lore/build_encounter_adds.py --check  exit 1 if it is out of date
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'docs/encounters/epic-encounters.json'
OUT = ROOT / 'source/server/GameServer/bots/autonomous/data/encounter_adds.json'


def build():
    catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
    adds = {}
    for e in catalog['encounters']:
        names = sorted({a['class'].lower() for a in (e.get('adds') or []) if a.get('class')})
        if names:
            adds.setdefault(e['class'].lower(), set()).update(names)
    data = {'about': 'Boss script class -> add script classes (lower case), from docs/encounters/epic-encounters.json. '
                     'Written by tools/lore/build_encounter_adds.py; do not edit.',
            'adds': {k: sorted(v) for k, v in sorted(adds.items())}}
    return json.dumps(data, indent=1, ensure_ascii=False) + '\n'


def main():
    text = build()
    if '--check' in sys.argv:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT} is out of date: run python3 tools/lore/build_encounter_adds.py')
            return 1
        return 0
    OUT.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {OUT}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
