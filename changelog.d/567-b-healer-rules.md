---
version: 1.2.3
section: Bots and companions
---
- **Bot healers heal like 1.65 healers** (#567 B1-B5; owner decision 2026-11-26; port of OfflineDAoC-True165 BotHealTriage165,
  1add69478). A dedicated healer now starts healing at two-thirds health (65%, it was 80%), heals the tank first, uses its
  small heal before the big one outside an emergency, puts a heal over time on the tank as a fight wears it down, and
  casts a group heal only with three or more hurt and not on the pull, as the period healer guides advise. Raid healer
  posts keep their own rules. One server setting, `bot_healer_period_rules` (on), turns it off.
