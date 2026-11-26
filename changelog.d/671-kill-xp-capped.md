---
version: 1.2.3
section: Tests
---
- **Kill XP report: level-50 kills leave the XP median** (#671; Python report only). The living report's group line
  read "median XP per member 0" in every realm because level-50 parties, paid nothing by design, were in the median.
  `kill_xp_report.py` now leaves capped kills (the party's lowest level at 50, or a solo bot at 50) out of the XP median
  and mean, counts them in a `capped` column (rows/member values), and splits a realm's group line into `group <50` and
  `group 50` when it has capped group kills. The footer says what `off` means: kills on the way that were not the camp's
  target, not XP lost. A log without level-50 kills prints as before.
