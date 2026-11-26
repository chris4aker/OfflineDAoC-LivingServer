---
version: 1.2.3
section: Tools
---
- **Raid fight samples and the raid funnel** (#644 parts 2-3; logging only, no boss, tactic or size change, on by
  default, no raider names). `RAID_FIGHT_SAMPLE` every 15 s of a live raid attempt (dungeons, dragons and open-world
  bosses): the boss's health, raiders alive and engaged by role, the healers' power, whether the boss is on a tank and
  whom it fights, the median distance to it, and the raiders mezzed and stunned (at most 4 a minute per attempt and 30 a
  minute overall). `RAID_FUNNEL` at each raid's start and end: asked, recruited, parties, at the hub, staged and at the
  boss, the drop-outs by reason, and the time each leg took. The morning report's **Raid attempts** section adds a fight
  curve per attempt and a funnel table per raid, and `raid_postmortem.py` interleaves the samples with the deaths in a
  timeline.
