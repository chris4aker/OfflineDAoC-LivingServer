---
version: 1.2.3
section: Bots and companions
---
- **Gamebots share the roads that are blocked, and stop standing on cliff edges above their path** (#570). When three
  solo gamebots give up a camp within 30 minutes because a monster blocks the road with no way around, the spot is
  remembered for half an hour: solo gamebots up to the highest of their levels pick another camp instead of walking
  there or straight past it (as long as another camp is left). A gamebot whose path stops on a cliff edge just
  above where it was heading now takes the zone's roads down at once, and every gamebot going there afterwards takes
  them too, instead of standing on the edge until it gives up.
