---
version: 1.2.3
section: Bots and companions
---
- **Gamebot parties pull from where they stand** (#569; port of OfflineDAoC-True165 0e0a397).
  - A monster too far from any spot the puller could shoot it from is passed over for a nearer one, instead of the tank
    walking into its pack. When only such monsters are left for about 45 seconds, the party walks in together toward
    one (at most twice per camp in 10 minutes).
  - A puller standing beside its leader at the camp starts pulling, even when its place in the formation is at the
    camp's edge.
  - While the camp is empty, the puller waits with the party instead of wandering off on its own.
  - Outdoor gamebot parties only; your own group is unchanged. Switch: `bot_party_puller_camp_turn` (on).
