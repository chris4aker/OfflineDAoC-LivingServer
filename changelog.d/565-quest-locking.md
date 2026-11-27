---
version: 1.2.3
section: The server
---
- **Quest journal: no more doubled or lost quests when two quest steps happen at once** (#565, OpenDAoC port X4 step 1;
  ports of OpenDAoC-Core `22197f1f6`, `cdb4384c0`, `63f46636d`; tests only). The active quest list is now one
  dictionary behind one lock, with lookups by data quest ID and scripted quest class: the 25-quest limit, the
  "already on it" check, the free journal slot and the add are one step, so two quest starts at once can't both get in
  or share a slot, and the journal update moves quests waiting past slot 25 under the same lock. Finishing or
  abandoning a quest that wasn't in the list no longer blanks journal slot 0. Joining a group on a mission shows the
  mission's text at once; leaving a group resends the journal only when it had a mission. Players only (gamebots and
  companions hold no quests). `UT_QuestListLocking`.
