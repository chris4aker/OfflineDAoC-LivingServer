---
version: 1.2.3
section: The server
---
- **Living Server rows, set A** (#625; owner, 2026-11-25: "yes build all of A and B now before the candidate final release
  for testing"). New server-log rows, logging only, on by default, low-volume and without any player or account name:
  `BG_POPULATION` (every 5 minutes per battleground and realm: gamebots there on the battleground goal or passing
  through, travelling, the goal shares' expected headcount, companions and the player; checks #610), `CAST_INTERRUPT`
  (a sampled cast an attacker's interrupt timer touched, at #596's half-cast decision: caster kind, spell type, time
  left, kept or lost), `CC_BREAK` (each break of a mez or breakable root: breaker kind, ally or enemy of the caster,
  time since it landed; checks #621), `BG_TARGET_PICK` and `BG_RALLY_RESPONSE` (sampled battleground picks and the time
  from a rally contact to each rallied bot's engage; #621) and `PLAYER_LINE_OUTCOME` (each line the player types to
  bots: answered within 30 s, latency, reply kind, answering bots). Each row has a budget a minute (drops counted in
  `RVR_HOUR`); sampling rates are the server properties `living_rows.cast_interrupt_sample_every` and
  `living_rows.bg_target_pick_sample_every` (10). Report sections in `source/tools/living_report_sections/`. **Tests
  only.**
