---
version: 1.2.3
section: RvR
---
- **RvR healers find the keep guard or relic carrier they should heal without stalling the server** (#572).
  This covers autonomous gamebot healers on an RvR objective. Before, every
  heal check searched 2,000 units around the healer, cast a line-of-sight ray at every keep guard in it and sorted
  them, many times a second at a keep fight. Now a healer keeps its support target, or "nobody needs it", for 2
  seconds. When it looks again it ranks the candidates first (relic carrier, keep lord, other guards, then the lowest
  health) and casts a ray only at the best one, then at the next if that one is out of sight. The relic carriers are
  read once every 2 seconds for the whole server. The healer picks the same target as before. `UT_RvrHealerSupportCache`.
  **Tests only.**
