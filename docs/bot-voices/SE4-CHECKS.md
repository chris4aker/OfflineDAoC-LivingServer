# Bot chat 0.36-se.4: checks before release (2026-10-18)

What was checked for the se.4 chat rework, and how. **Nothing here was played in game.** The numbers come
from a Python simulation of the server's chat rules and from samples judged by hand, so treat them as
estimates of what a session will look like, not measurements of one.

## 1. Audit of 0.36-se.3 (before)

Code and corpus read in full; the real C# intent model run on 90 realistic player messages; a simulation
of 50 bots per realm over 3 hours with one player hearing all /faction and the /say of 4 nearby bots.

- 19 of 90 player messages ("lol", "ty", "gz", "inc", "brb"...) read as real-world talk, which /say ignored.
  41% of messages with a fitting intent were read right.
- 34 of 50 sampled bot-to-bot replies didn't fit the line they answered.
- 19% of what the player saw was fixed formal text ("Fair enough. Safe travels." about 10 times a session).
- 31% of a session's lines had been heard in the previous session.
- Group talk ("pull?", "rez pls") was 7% of /faction; the player's own group said nothing.
- A 12-member bot guild said about 34 idle lines in 3 hours, 11 of them distinct.

## 2. After (simulated on commit bb9fda1)

| Metric (50 bots, 3 h) | Before | After |
|---|---|---|
| Lines the player sees | 180 | 147 |
| Fixed formal text | 19% | 0% |
| Same exact text seen again | 9.1% | 0.07% |
| Bot replies that are old formal lines | 30% | 0% |
| Same first two words within 15 minutes | 6.5% | 1.1% |
| A 12-member guild's idle lines | 23-34, 9-11 distinct | 21-32, all distinct |
| Lines in the player's own /group | 0 | about 13 an hour |
| 50 bot-to-bot pairs: fit / passable / don't fit (hand-judged) | 8 / 8 / 34 | 20 / 18 / 12 |

On 260 labelled player messages, the new reading gets 232 right (89%).

Fewer lines are seen because a plain remark now often gets no reply, as in real chat. "/faction"
volume is otherwise set by the same per-realm gaps (now 2-4 minutes at random instead of a fixed 3).

## 3. Independent code review

A separate review of the change (not by the code's author) found no crashes or races. It found, and these
were fixed before release:
- some voices-only behavior also applied with bot voices off (reply counts, who answers "gz" or "ty", the
  bots' writing style on knowledge answers);
- intent misreads ("rez pls ty", "keep door down?", "heat res gear", "terrible lag" read as an insult);
- a bot that wouldn't come to rez could say it had no rez spell; a guildmate could promise "omw";
- item adverts that weren't about the item; the shorter repeat rest applied to bot chatter too;
- saves of the no-repeat window that could be lost after a failed write; a few wrong plurals; slow
  trainer lookups.

## 4. Fixed after the simulation

The simulation ran before the review fixes. It also found, and these were fixed:
- repetition across evenings (the six-hour window had expired by the next evening): the window is now
  three days, with a six-hour fallback when a kind of line runs out;
- 26% of bot questions unanswered because the chosen bot was fighting: another bot takes the reply;
- "anyone on?" answered only with refusals, "how is everyone?" with "no idea", "x or y?" with "i would":
  new answer types for these questions;
- class talk about the wrong class; battleground lines said in the frontier; a few lines that read badly
  ("lfg xp" at 50, "ask in faction" said in /faction, "i know one").

These later fixes are covered by unit tests but were not simulated again.

## 5. Still open

See [KNOWN-ISSUES.md](../KNOWN-ISSUES.md) (Bot chat). Mainly: replies still guess from words alone, a
frontier bot may say "omw" to "inc" without coming, and chat volume is untuned for 1,000+ bots a realm.
