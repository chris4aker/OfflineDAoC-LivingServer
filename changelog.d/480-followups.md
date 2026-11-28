---
version: 1.2.3
section: Chat
---
- **Follow-ups keep their subject** (realism 2c, #480, step 1; tests only). Bots now finish a short follow-up from what the
  conversation is about before they read it, in /faction, /say, tells, /group and with your hired companion:
  - After you talk about a place, "what lvl is it?", "where?", "how far is that?", "what zone is that in?" and "ok how do
    i find it" ask about that place. "is it good xp?", "is it hard?" and "can i solo it?" ask whether it suits your level.
    Before, these got silence, the bot's own level ("32" by tell) or a lore line about the barrows.
  - After you name a class, "who trains them?", "what spec for one?", "what armor do they wear?" and "are they good
    solo?" ask about that class. Before, the answer was for your own class ("your armsman
    trainer").
  - After you name an item, "who sells them?", "where do i get one?" and "who makes those?" ask about that item, and "who
    makes ..." gets the same crafter-or-exchange answer as "who sells ...".
  - "anyone?", "hello??", "nobody knows?", "so no one can help", "bump" and a bare "?" ask your last question again if
    nobody answered it (from 15 seconds to 3 minutes after you asked).
  - "sorry i meant spindelhalla not varulvhamn. what lvl" asks about Spindelhalla. "i meant X" and "i said 12 not 21" ask
    your last question again with the correction in it. The conversation now remembers what you meant, not the word you
    took back.
