---
version: 1.2.3
section: RvR
---
- **Gamebots come and go from the battlegrounds cleanly** (#572 step 4). A gamebot leaving a
  battleground finishes its fight at the exit, and one whose walk out fails takes the exit after four minutes instead of
  walking forever. A battleground roamer in a quiet battleground is no longer sent to its capital after 45 minutes
  without a fight. A gamebot that makes no progress inside for 20 minutes, or dies three times without gaining anything,
  is taken out to its border keep (not the capital) and stays out of the battlegrounds for 30 minutes, so it no longer
  loops between the entry and the exit. Players and companions are unchanged.
