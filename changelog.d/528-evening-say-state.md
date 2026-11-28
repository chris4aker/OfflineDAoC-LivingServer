---
version: 1.2.3
section: Tests
---
- **Evening chat simulation: /say keeps its own state** (#740, finishing #622). #622 gave each subsystem its own random
  stream, but the evening's shared state (the recent-line memory, the realm's field memory, the stance, the exchange
  numbers and the one channel pick a near bot's check made) still carried any /faction change into /say: #528's truer inc
  calls moved the ratchet's `say_stock_share`. In the simulation only, /say now keeps its own recent-line memory, field
  memory, stance and exchange numbers, and a bot near the player checks /say on its own clock against /say's meter alone.
  New tests show that a /faction, channel-pick or scene change leaves every /say line as it was, and that a /say change
  still moves /say. The live server's chat is unchanged. The ratchet baseline is re-derived once from this run, with the
  owner's approval (2026-11-27, "24A").
