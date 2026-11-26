---
version: 1.2.3
section: Tools
---
- **Raid mechanic, threat and role rows** (#644 parts 3-5; logging only, no boss, tactic or size change, no raider names).
  `RAID_MECHANIC_FIRE`, on by default: every boss mechanic during a raid attempt, with how many raiders were exposed,
  stepped out, were caught, were hit, died or avoided it, how much warning they had and the damage it did. Legion,
  Grand Summoner Govannon, the Green Knight, Spindler Broodmother, Queen Kula and King Tuscar now report their mechanics
  for these rows without changing their fights. Behind the off-by-default diagnostics setting `raid_diagnostic_rows`
  (for test runs): `RAID_THREAT` for each change of a raid boss's target and `RAID_ROLE_TOTALS` with damage, healing,
  overheal, crowd control, interrupts and power-outs by role. The morning report's **Raid attempts** section and
  `raid_postmortem.py` read all three.
