---
version: 1.2.3
section: Tests
---
- **Evening chat simulation: one random stream per subsystem** (#622). The player's evenings
  (`UT_ChatBudgetSimulation.PlayerEvenings`) drew every subsystem from one random stream, so a change to one reply
  reshuffled every later trade, fight-call and /say line, and the evening ratchet read unrelated metrics as worse (#614
  had to loosen three for this alone). Each subsystem (the ambient scheduler, /faction, /say, /lfg, /trade, /guild, the
  debate scenes, stories, the group's banter, its fights, solo events, tells and the player's lines) now has its own
  seeded stream, derived from the evening's seed and the stream's name. The roster keeps the evening's own seed. New
  tests pin the seeds, and show that a /group change leaves /trade and the fight calls' moments alone, and that a /trade
  change leaves the fight calls alone. The ratchet baseline is re-derived once for this change, with the reason recorded.
