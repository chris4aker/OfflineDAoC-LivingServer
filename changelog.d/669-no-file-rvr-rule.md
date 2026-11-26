---
version: 1.2.3
section: Bots and companions
---
- **A world without a saved Bot Goals file behaves like one with the defaults saved** (#669; owner, 2026-11-26: "remove
  the rule"). Gamebots coming off an RvR tour no longer have to finish one solo PvE task first: they are in the next
  RvR share-out straight away, and their next task-break goal follows the default Bot Goals shares. A world that ran an
  earlier build drops the old "PvE task first" mark from each gamebot as it loads; levels, items, coins and the current
  task are kept. Worlds with a saved `bot-goals.json` are unchanged. **Tests only.**
