# Bot chat 0.36-se.4: checks before release (2026-10-18)

What was checked for the se.4 chat rework, and how. **Nobody has played it in game yet.** Sections 1-5 come
from a Python simulation of the server's chat rules and from samples judged by hand. Section 5 is a live test:
the real se.4 server with a scripted player that logged in and typed, which is closer to play but still not a
person at the keyboard.

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

## 5. Live headless test (2026-10-18)

The se.4 server ran in a cloud workspace on a copy of a played database (150 bots, every Social Expansion
feature on). A scripted player (the 0.36c new client's network code without its window) logged in as a level
5 Albion Scout, typed in /faction, /say, /send, /gu and /group on a timetable, and recorded every line it was
sent. Four sessions: 40 minutes near Cotswold, then about 25, 20 and 10 minutes in Camelot, each later one on a
build with the fixes from the one before. The scripted player didn't move or fight, and the server ran on Linux
without its navigation library, so bots moved worse than they do on Windows; neither affects chat.

| | Session 1: se.4 as merged (Cotswold) | Session 3: most fixes (Camelot) | Session 4: all fixes (Camelot) |
|---|---|---|---|
| Player lines that got a fitting answer, or rightly none | 17 of 24 | 13 of 19, 1 passable | S4_FIT |
| Unprompted lines heard a minute | 0.9 | 2.1 | S4_RATE |
| Lines heard twice | none | none | S4_DUP |
| Server errors after start | none | none | S4_ERR |

Player lines that couldn't be tested (no bot in /say range, no group yet, or a test-client mistake) aren't
counted. Session 2 (Camelot, 25 minutes) ran on a build between sessions 1 and 3 and is folded into the list
below. The five answers still wrong in session 3 were fixed before session 4.

Found and fixed (with unit tests, except the /group answers and the /faction answerer rotation, which only
the live sessions checked):
- **Capitals counted as dungeons.** The server calls a one-zone region at the dungeon offset a dungeon, and
  the capitals are such regions, so bots in Camelot said "lfg City of Camelot" and "this place is a maze".
- **Replies to the wrong kind of question.** "hi all" drew "not bad, and you?"; "how is everyone?" plain
  hellos; "anyone on?" in /gu "good question, someone else might know"; "mind if i hunt here?" "what?";
  "lf guild" (already guilded) "i could use a group too"; "what does lfg mean?" "what level range?";
  "where to next?" directions. A player's question type is now read before its topic, with new answer sets
  (who is on, things someone may know, the next hunt, levels and xp, plans, stories) and a short list of chat
  words.
- **Bot lines labelled as the wrong kind.** "who is the best healer in this guild? be honest" drew "ha! too
  true"; "anyone know who camps x? want to take turns" counted as a remark. The builder now reads a question
  before a trailing clause, and about 30 lines got a better label.
- **Answers that ask what was said.** "paladin 6 here" drew "what lvl?", "lfg 50 reaver" "what class are u?".
- **Sympathy for good news.** "tell me about it" answered "picked up new spells, cant wait to try them".
- **Scene lines read as answers.** A running /faction scene and unprompted lines now wait 10-15 seconds after
  the player speaks; "lfg 5 scout" was followed by the scene's "me too". A level 50 no longer plays a scene
  role that says "i am not 50 yet".
- **Your group didn't answer you.** A player's /group line got nothing; now a groupmate answers in /group. A
  group standing in a capital no longer says "pull?" or "oom".
- **One bot answered everything.** The player's groupmate answered nearly every /faction line (20 lines in
  20 minutes); in /faction the realm now answers, and recent answerers step back.
- **Smaller ones.** "where is camelot?" found nothing; trainer answers read badly without commas; four
  replies sent the asker to /faction while in /faction; a remembered-player reply ("Wrenfield?") could
  replace an answer.

Seen working on the first try: "where is the scout trainer?" named the nearest trainer and its zone, "is
friar good?" got friar answers, "are you a bot?" by /send got "a bot would have better gear than this", "inc",
"rez pls", "wts", "ding!" and "good night" got fitting answers, an insult got "noted", guild hellos on login,
a bot greeting the player by name from real memory of earlier grouping, and the no-repeat record being saved.

## 6. Still open

See [KNOWN-ISSUES.md](../KNOWN-ISSUES.md) (Bot chat). Mainly: replies still guess from words alone, a
frontier bot may say "omw" to "inc" without coming, and chat volume is untuned for 1,000+ bots a realm.
