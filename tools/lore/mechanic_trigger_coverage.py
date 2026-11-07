#!/usr/bin/env python3
"""Which lore mechanic triggers can fire: every boss file's yells.phases / atmosphere.phases line whose trigger is a
mechanic (throw, teleport, jail, glare, breath, berserk, ...), and whether the boss's script announces it.

The lore engine (LoreBossWatch, Living Server 1.2) fires a mechanic trigger only when the boss's script says it happened:
  - EncounterDisplacement.Move / MoveInRegion with DisplacementKind.Throw|Teleport|Jail|Pull|Barrier (or the dragons'
    DragonEncounterThrow): the kind's own name, and "teleport" for any move;
  - EncounterSignals.Raise(..., EncounterSignals.Glare|Stun|Breath|Stomp|Messengers|Retreat|SafeSpots): its name
    (a glare is also a "stare");
  - EncounterSignals.Note(..., EncounterSignals.Berserk|Scream|Healed|TwinHeal): its name.
A mechanic from an add or an object counts for the nearest watched boss, so an encounter's own adds (the catalog's
spawnsEncounters and "<Boss>..." classes in the same file) count for it too.

The script is found by the boss file's catalogKey (a catalog id or a class name) in docs/encounters/epic-encounters.json;
the class's own part of its file (the class and its brain) is read.

  python3 tools/lore/mechanic_trigger_coverage.py            print the list (markdown)
  python3 tools/lore/mechanic_trigger_coverage.py --check    exit 1 if a line uses a trigger its boss can't fire
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LORE = ROOT / 'source/server/GameServer/bots/autonomous/data/lore/bosses'
CATALOG = ROOT / 'docs/encounters/epic-encounters.json'
RULES = ROOT / 'source/server/GameServer/bots/autonomous/LoreRules.cs'

KIND_TRIGGERS = {'Throw': 'throw', 'Teleport': 'teleport', 'Jail': 'jail', 'Pull': 'pull', 'Barrier': 'barrier'}
SIGNAL_TRIGGERS = {'Glare': ['glare', 'stare'], 'Stun': ['stun'], 'Breath': ['breath'], 'Stomp': ['stomp'],
                   'Messengers': ['messengers'], 'Retreat': ['retreat'], 'SafeSpots': ['safeSpots'],
                   'Berserk': ['berserk'], 'Scream': ['scream'], 'Healed': ['healed'], 'TwinHeal': ['twinHeal']}

# Lines that use a trigger their boss can't fire, kept on purpose (file id -> {trigger: reason}). Empty: every one fires.
KNOWN_GAPS = {}


def mechanic_triggers():
    """LoreFightRules.MechanicTriggers, read from LoreRules.cs (the engine is the source of truth)."""
    text = RULES.read_text(encoding='utf-8')
    block = re.search(r'MechanicTriggers\s*=\s*\[(.*?)\];', text, re.S).group(1)
    return re.findall(r'"([A-Za-z]+)"', block)


def class_parts(source):
    """{class name: its text}, cut at each class declaration (brains included)."""
    parts = {}
    marks = [(m.start(), m.group(1)) for m in re.finditer(r'^\s*(?:public|internal)\s+(?:sealed\s+|partial\s+|abstract\s+)*class\s+(\w+)', source, re.M)]
    for i, (start, name) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else len(source)
        parts[name] = parts.get(name, '') + source[start:end]
    return parts


def fired_by(text):
    """The mechanic triggers a piece of script announces."""
    fired = set()
    moves = re.findall(r'DisplacementKind\.(Throw|Teleport|Jail|Pull|Barrier)', text)
    if 'DragonEncounterThrow.Throw' in text:
        moves.append('Throw')
    for kind in moves:
        fired.update({KIND_TRIGGERS[kind], 'teleport'})
    for name in re.findall(r'EncounterSignals\.(?:Raise|Note)\([^;]*?EncounterSignals\.(\w+)', text, re.S):
        fired.update(SIGNAL_TRIGGERS.get(name, []))
    return fired


def build():
    known = mechanic_triggers()
    known_lower = {t.lower(): t for t in known}
    catalog = json.loads(CATALOG.read_text(encoding='utf-8'))['encounters']
    by_id = {e['id'].lower(): e for e in catalog}
    by_class = {e['class'].lower(): e for e in catalog}
    sources = {}
    rows = []
    for path in sorted(LORE.glob('*.json')):
        lore = json.loads(path.read_text(encoding='utf-8-sig'))
        uses = {}
        for section in ('yells', 'atmosphere'):
            for phase in (lore.get(section) or {}).get('phases') or []:
                trigger = (phase.get('trigger') or '').replace(' ', '')
                if trigger.lower() in known_lower:
                    uses.setdefault(known_lower[trigger.lower()], set()).add(section)
        if not uses:
            continue
        key = (lore.get('catalogKey') or '').strip()
        encounter = by_id.get(key.lower()) or by_class.get(key.split('/')[-1].lower())
        fired, via = set(), None
        if encounter:
            file = ROOT / encounter['file']
            if file not in sources:
                sources[file] = class_parts(file.read_text(encoding='utf-8-sig', errors='replace'))
            parts = sources[file]
            names = {encounter['class'], encounter.get('brain') or encounter['class'] + 'Brain'}
            names |= set(encounter.get('spawnsEncounters') or [])
            names |= {a['class'] for a in encounter.get('adds') or [] if a.get('class')}
            for name in names:
                fired |= fired_by(parts.get(name, ''))
            via = encounter['file'].replace('source/server/GameServer/scripts/namedmobs/', '')
        for trigger, sections in sorted(uses.items()):
            ok = trigger in fired
            gap = KNOWN_GAPS.get(lore['id'], {}).get(trigger)
            rows.append({'file': lore['id'], 'class': encounter['class'] if encounter else None, 'trigger': trigger,
                         'sections': sorted(sections), 'fires': ok, 'script': via, 'kept': gap})
    return rows


def main():
    rows = build()
    gaps = [r for r in rows if not r['fires'] and not r['kept']]
    print('| lore file | script class | trigger | in | fires |')
    print('|---|---|---|---|---|')
    for r in rows:
        fires = 'yes' if r['fires'] else ('no (kept: ' + r['kept'] + ')' if r['kept'] else 'NO')
        print(f"| {r['file']} | {r['class'] or '(no script)'} | {r['trigger']} | {', '.join(r['sections'])} | {fires} |")
    print(f"\n{len(rows)} mechanic trigger uses in {len({r['file'] for r in rows})} boss files; "
          f"{sum(r['fires'] for r in rows)} fire, {len(gaps)} can't.")
    if '--check' in sys.argv and gaps:
        for r in gaps:
            print(f"{r['file']}: \"{r['trigger']}\" is never announced by {r['class'] or 'any script'} ({r['script'] or 'not in the catalog'})")
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
