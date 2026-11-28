---
version: 1.2.3
section: Bots and companions
---
- **Bot and companion healers heal like 1.65 healers** (#567 B1-B5; owner decision 2026-11-26).
  A dedicated healer now starts healing at two-thirds health (65%, it was 80%), heals the tank first, uses its
  small heal before the big one outside an emergency, puts a heal over time on the tank as a fight wears it down, and
  casts a group heal only with three or more hurt and not on the pull, as the period healer guides advise. **This covers
  your own companion healers and `/spawn` helpers too**, so you'll notice it first in your own group. Realm raids,
  warbands and boss fights keep their own healer rules. One server setting, `bot_healer_period_rules` (on), turns it off.
