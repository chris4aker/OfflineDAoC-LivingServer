---
version: 1.2.3
section: RvR
---
- **Gamebots that out-level a battleground stay and fight until they die** (#572). A gamebot that gains a level
  past the battleground's cap while inside keeps fighting there, as players could. Once it dies it leaves, and the level
  cap keeps it out. A gamebot that logs in out-levelled inside one still leaves. Gamebots in a player's group and hired
  companions are unchanged.
