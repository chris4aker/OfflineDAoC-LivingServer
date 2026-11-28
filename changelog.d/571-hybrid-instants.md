---
version: 1.2.3
section: Bots and companions
---
- **Melee hybrids keep swinging through their instant spells** (#571 step 5;
  tests only). After every spell, a bot's combat turn stopped its melee swing, so a Reaver casting its instant lifedrain
  every turn ran at its enemy and then stood beside it without landing a hit. The Savage and the autonomous Minstrel had
  their own narrower exceptions. Now any melee bot (Reaver, Paladin, Thane, Champion, Valewalker, Warden and the like)
  keeps its swing and chase after an instant spell, as the engine already allows (it stops melee only for a cast with a
  cast time). An instant priority taunt no longer costs a tank its swing either. Spells with a cast time, songs that
  need an instrument and ranged casters are unchanged. This is a shared combat fix: it applies to hired, `/spawn` and
  `/bot` companions and to gamebots, grouped or not. `UT_BotHybridMeleePolicy`.
