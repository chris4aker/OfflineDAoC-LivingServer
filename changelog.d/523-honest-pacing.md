---
version: 1.2.3
section: RvR
---
- **Different warbands answer each keep fight** (#523 step 2). Each siege and defense still holds back about three in ten gamebot warbands to keep the war going elsewhere, but it holds back different ones every time. Before, the same warbands sat out every defense.
- **Each realm's siege balance follows its own keeps** (#523 step 2). How many defenders answer a siege, and how large an army may attack, now follow the defending realm's own last six sieges once it has fought three of its own; until then the realm uses the server-wide record (owner's answer to question 52, #523 issuecomment-7227740738). One realm losing keeps no longer brings out more defenders, or smaller attacking armies, against the other realms. Each realm's record, and when it switches to its own, carries over a restart: a finished siege now names its defending realm in the event ledger.
