---
version: 1.2.3
section: Tools
---
- **The RvR hour report counts fights by size and shows the multi-group forces** (#599 step 5). `rvr_hour.py` now puts
  each fight into a bucket by its biggest side: 1-2, 3-8 (one group), 9-16 (two groups), 17-24 (three groups or a small
  zerg) and 25+. For each hour it also lists the top matchups (such as `16v8`) and the fights where a side had 2 or more
  groups, or 3 or more. Per realm, it reports the battlegroups, zergs and battleground battlegroups that formed and
  ended, with their joins, leave reasons, peak heads and groups, and lifetimes (from `RVR_FORCE_BATTLEGROUP`,
  `RVR_FORCE_ZERG` and `RVR_FORCE_BG_BATTLEGROUP`). It adds the 3,000-unit force clusters (`RVR_FORCE_CLUSTER`: the
  biggest, the samples at 9-15 and 16+, and those over 24) and the enemies the fight boards saw in contact. `--forces`
  prints each force's timeline. On an older log, each new figure names the row it needs. The total-size buckets are
  unchanged.
