---
version: 1.2.3
section: Bots and companions
---
- **Gamebots stop walking into the places that keep killing them on their way to town and to a horse** (#570). Gamebots already kept their camps away
  from spots where far stronger monsters had killed bots of their level; now their town trips do too. A gamebot picks
  the nearest merchant, seal merchant or class trainer whose way there passes no such spot, and goes to a dangerous one
  only when nothing else is left. A solo gamebot no longer rides a horse to a landing beside such a spot when it is only
  passing through, prefers a landing whose walk to its goal avoids one (unless that is more than ten minutes slower),
  and doesn't walk to a stable master straight through one. Landings where six different gamebots of one level range
  died within five minutes of getting off in the last hour are skipped by bots of that range for a while.
