---
version: 1.2.3
section: Bots and companions
---
- **Gamebot parties meet where everyone can get to, and pick camps they can reach in time** (#569; port of
  OfflineDAoC-True165 14dcaa5).
  - A forming party moves its meetup next to its planned camp only when every member can get to that town in time: the
    farthest one within 3 minutes, or no more than 2 minutes worse than the current town. Otherwise it keeps the town it
    has.
  - The meetup wait is sized to the farthest member, from 15 up to 20 minutes, instead of a fixed 15, so a member
    coming from far away is no longer dropped as a no-show on the way.
  - A formed party only picks camps it can reach within about a third of the task time it has left. When none fits, it
    takes the nearest few.
  - Switches: `bot_group_meetup_reachable_town`, `bot_group_meetup_window_by_travel` and `bot_group_camp_travel_budget`
    (all on).
