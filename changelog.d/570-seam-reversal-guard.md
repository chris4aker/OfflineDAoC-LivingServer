---
version: 1.2.3
section: Bots and companions
---
- **Gamebots stop sooner when a zone border turns them back** (#570, step 1; port of True165 `d1320e6`, last `2f0f143`). A
  gamebot whose route steps over a zone border and straight back now gives that route up at the second turn instead of
  the third; a camp that sits right on the border is left alone. The crossing that turned back is forgotten for every
  bot, so the next one isn't sent the same way, and a group member who hits it waits a moment and follows its leader
  again instead of dropping the whole group's camp (twice at most; after that it gives up like a leader would). Checked against the Cruachan Gorge / Connacht loop of #555.
