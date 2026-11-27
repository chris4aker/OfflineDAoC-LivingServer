---
version: 1.2.3
section: Fixes
---
- **Client display fixes for the original 1.127 client** (#564; ports of OpenDAoC-Core, tests only):
  - Touching a frontier portal stone opens your realm's war map again, instead of an empty Accept/Decline box
    (d74d4941f).
  - The keep hookpoint store (guards, siege engines, oil) uses the 1.127 layout, so its items, prices and icons show
    correctly (476059682, d15534653).
  - Spell and song tooltips show your own spell's level and values instead of level 1 (42bf098af, f5f3e4e43, 95e394d35).
  - Spell delves show the real cast time for casts under 2 seconds instead of 2.0 s (ef017586c).
  - Quest reward items can be delved from the quest offer and reward windows (ec097f507).
  - Item tooltips show charges whose spell is in any spell line, not only Item Effects (a376d6182).
