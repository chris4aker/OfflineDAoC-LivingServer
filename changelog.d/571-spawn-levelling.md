---
version: 1.2.3
section: Companion helpers
---
- **`/spawn` helpers level up with you** (#571 step 6; DetShaft 56972bb08 / 6ce49e33b, GPL-3.0; tests only). Helpers earn
  no experience, so before this a helper spawned at 20 stayed 20 until you respawned it. The only catch-up was the one-time
  endgame kit when you reached 50. Now, after you ding, each helper in your group catches up to your level once it is out
  of combat. Its stats, specializations, spells, styles, equipment (rolled in the same band as at spawn, never below
  what it wore) and pet are brought up to date, and you see "<name> is now level N, matching you." Helpers never level
  down. The endgame kit at 50 is unchanged. Hired and `/bot` companions, gamebots and the Epic Special Forces keep their
  own levels. Log: `COMPANION_LEVEL_SYNC`. `UT_CompanionLevelSync`.
