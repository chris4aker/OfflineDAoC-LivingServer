---
version: 1.2.3
section: Chat
---
- **Your hired companion and your groupmates answer like the bots in /faction** (realism 2b, #479, step 1; tests only).
  Before, a hired companion in /say answered every line by its kind alone: "where is the cleric trainer?", "can someone rez
  me", "add!" and "back" all drew "sure Wynn", "ok" or "Point me at the next one", while the same companion knew the answer
  in /group. In /group, "im at the bridge", "omw", "be there in 5", "oom", "need to rest", "med break" and "rdy" drew
  nothing, a quick second question was dropped by a 5 s cooldown, and answers took a median 14.5 s at a flat 40 wpm while a
  companion typed any answer in 1-2 s. Now both take one party reply path (`AutonomousChatReplyPath.Party`, which the
  question bench runs too): where you say you are or that you are coming (a groupmate beside you says "k", one in your
  region says it is coming, one in another region says where it is and asks you to come), a pause or "rdy" (it meds too only
  when its own power is low, and says "sec" when it isn't ready), the knowledge lookup, a warning, "come here", then the
  reply corpus and the reply budget. A Formal companion keeps its own lines, and every companion keeps "be quiet", "you can
  talk" and "how are you"; its own lines stay only for a plain statement where they are true of a companion ("lead on, im
  behind u" to "lets go"). The screen now runs before a companion answers, and when your companion takes a /say line the
  gamebots nearby leave it to the companion. A question or a line naming a groupmate is never lost to the /group cooldown.
  The group's conversation now holds your /group line whatever answers it and every groupmate's line, so "how do i get
  there" after a groupmate names a place, or after an answered "where is spindelhalla?", has its place. One typing delay
  (`AutonomousChatArbiter.AnswerDelayMs`) serves /faction, /say, /group and companions: the bot's own typing speed, a short
  known fact faster, and at most 12 s for a line said to the bot, in its group or to a companion.
