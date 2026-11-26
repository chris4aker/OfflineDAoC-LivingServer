---
version: 1.2.3
section: RvR
---
- **Lone gamebots size up a fight before they start it** (#662). In the frontier and the battlegrounds, a gamebot on its
  own or in a pair looks at the enemies once it notices them, and either opens or backs off. Bolder characters take worse
  odds, and a careless one sometimes misjudges a close fight. No lone gamebot ever charges three or more enemies. A
  gamebot that backs off pulls out of range, waits nearby, calls its friends, then carries on with what it was doing.
  One server setting, `bot_rvr_sight_size_up` (on), turns it off.
