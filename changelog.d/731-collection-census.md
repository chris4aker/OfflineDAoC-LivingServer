---
version: 1.2.3
section: Tools
---
- **Collection census row** (#731; owner answer A, 2026-11-28; logging only, no gameplay change, counts only).
  `SERVER_COLLECTION_CENSUS`, on in every session: every 30 minutes, from the `SERVER_MEMORY` report, the size of each
  long-lived static collection and cache the server keeps (367 stores: the RvR fight boards and their holders, the RvR
  contact table, force intents, battlegroup records, chat conversations, claim and hold tables, the group coordinator's
  sessions and formations, the log throttles, duty leases and every other bot store that grows with bots, groups,
  monsters or players), with the process and pool threads, queued pool work, timers, the GC generation sizes (gen0,
  gen1, gen2, LOH, POH) and full collections. Counted off the game loop. The row comes in numbered parts
  (`part=1/N`, each line at most 4,000 characters); part 1 carries the thread, timer and GC figures. The store list is
  `ServerCollectionCensus.Registry`, one line per store.
