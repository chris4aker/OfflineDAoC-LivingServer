---
version: 1.2.3
section: Bots and companions
---
- **Gamebot parties stop going back to places they cannot win** (#569; port of OfflineDAoC-True165 fb7487a). When a
  party loses two members (or takes three deaths) within ten minutes near one spot, to monsters well beyond anything
  it plans to fight, it now ends its task there, or at once after a wipe to such a monster. Before, it regrouped beside
  the killers and lost the next leader too. For two hours, parties of that level band and below plan no camp at that
  camp or beside that spot; stronger parties are not affected. Solo bots already remembered such places, and parties
  hosting a player, parties formed from a player's group and realm expeditions are never ended by it.
