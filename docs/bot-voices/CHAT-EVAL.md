# How the bots' chat is evaluated, and the first blind result

Owner 2026-11-01: "more could be done to evaluate how the bots chat and respond and talk and interact." The checks
until then measured one thing: whether a bot reads what a player typed (the intent), on line sets the chat's own
authors wrote (`UT_ChatReplyAudit`, `chat_audit_player_lines.json` and the "holdout" `chat_audit_holdout_lines.json`):
95.6% and 95.9%. Nothing measured whether the reply is any good, and every test set was written by the people tuning
the chat. This page is the evaluation that does, and its first result. The files are in
`tools/bot-voices/eval/blind-2026-11-01/`.

## The method (repeat it for every chat change)

1. **A blind player corpus.** 400 lines of what 2002–2003 players typed (every channel, greetings to flames, roleplay,
   typos and leetspeak, 40 second turns such as "where?" and "lol no"), written by an agent that never saw the bot code
   or the corpus, each labelled with what a real player would reply (`blind_corpus.json`, `BLIND-CORPUS.md`). A new
   set is written for each round, so the chat can't be tuned to the test.
2. **Through the real reply path.** `UT_ChatReplyAudit` (`MeasureReadRate`, `RunAudit`) runs each line through the
   classifier, the question shapes, the fit filters and the corpus pick, as `AutonomousBotChatCoordinator.VoicedReply`
   does. World answers (knowledge lookups, live answers) are not run and show as markers such as `[from the world]`;
   graders skip those.
3. **A blind reply grader.** A judge with no project context scores each reply 0–2 on answers (did it respond to what
   was said), era (2003 DAoC) and human (would you suspect a bot), with a short flag (`grades.json`, `GRADES.md`).
4. **Spot the bot.** A second judge, also blind, labels 180 shuffled lines (90 bot lines from the simulator and the
   replies, 90 lines from the player corpus) as bot or player (`spot_packet.json`; the key is `spot_key.json`).
5. **The scorecard** below, and a ranked fix list. The next round repeats 1–4 with a new corpus and compares.

## Results, 2026-11-01 (release/1.2 at `50ecb30`)

| Measure | Result | Target for 1.2 |
|---|---|---|
| Read as expected, own sets | 95.6% / 95.9% | — |
| **Read as expected, blind set** | **53.0%** (212/400; part of the gap is labels that map loosely) | 80% |
| Reply quality, answers / era / human (0–2) | **1.02 / 1.27 / 1.15** (total 3.45 of 6) | 1.5 / 1.5 / 1.5 |
| Replies that ignore what was said (answers = 0) | **34%**; faction 46%, tell 44%, trade 57% | under 15% |
| Silence instead of a text reply | 84 of 400; silence scored **3.99**, text replies **3.30** | — |
| Spot the bot: bot lines caught | **72%** (65/90; 26 of 29 "sure" calls right); 11 of 90 players wrongly called bots | under 50% |

The best channel is /group (4.06 of 6); the worst are /trade (2.36) and /faction (2.76).

Saying nothing reads more human than what the bots say now, which is the clearest single finding: a wrong reply costs
more than a missing one.

## What the judges found, ranked

1. **Brush-offs to questions anyone could answer** (41): "where is the armor trainer?" → "ask around maybe"; "where is
   the entrance to DF" → "beats me". A place question that the bot's knowledge can't answer falls back to a deflection.
2. **Ignores or misreads the request** (34): "help! im stuck under this rock" → "Run to the guards if it goes bad."
3. **Canned quips and aphorisms** (27): "safe travels" to someone going to bed; "Careful." to "train coming out of the
   dungeon".
4. **Silence where a player would answer** (24): a tell "hi! long time no see"; a group "bed time for me, gn".
5. **Stock non sequiturs** (24): "which way to the camp" → "i think so?"; "hey u there?" → "someone said something about
   that earlier".
6. **A line from the wrong situation** (18): LFG, RvR or trade filler pasted into another conversation ("need a healer
   still?" to "inv pls").
7. **The bot doesn't know its own state** (14): its name, its hunt, where it is.
8. **Lore musings that talk about the game instead of playing it** (the spot-the-bot judge's top tell, 22 lines): "the
   bind stone and i are on first name terms", "a purple con walking past the group makes everyone go very quiet".
9. **Filler agreement that fits anything:** "good question", "happy to help", "sounds familiar", "good observation".
10. **Near-duplicates and template slips:** "Ha yes, this exactly." / "Heh yep, this exactly."; "a kobold with a grudge
    are worse".
11. **Too tidy for 2003 chat:** capitals and full stops ("Np, have fun.").
12. **"Check the exchange"** offered for trades and even for XP loss. The Realm Exchange is this server's own feature and
    stays; bots should mention it only when someone asks where to buy or sell.

Gaps in what the bots understand at all (blind read rate by label): roleplay (16 of 17 missed), banter (21 of 29),
offers of help (16 of 19), RvR calls (13 of 23), and simple period words ("lo all" gets nothing).

## The fix list, in order of what it buys

1. **Silence over a weak reply.** When the read is unknown or low-confidence, say nothing, except to a direct tell or a
   line naming the bot, which gets a short human acknowledgement ("?", "ya?", "sec"). This alone removes most of
   problems 2, 5 and 6.
2. **Answer the answerable.** Place, trainer, camp and "how do I" questions go to the knowledge lookup first, and a bot
   that doesn't know says so like a player ("no clue, try /faction" or nothing) instead of a stock brush-off.
   Measure with the world parts run (step 2 above can't), on the owner's PC or a test world.
3. **Context fit.** Goodnights get "gn", "stuck" gets "/stuck", "inc" gets a direction or an "omw", a trade gets a price
   or a no; the reply set is chosen by the player's situation, not only by intent.
4. **Cut the musings and the filler from replies.** Lore musings stay as rare /faction openers only; generic agreement
   lines are removed or limited to one per channel per hour.
5. **Read more.** Roleplay (an RP reply set in kind), banter, offers of help, RvR calls, and the period's short words
   ("lo", "re", "wb", "gz", "inc", "omw").
6. **Self-knowledge.** A bot answers questions about itself from its own state (name, class, level, zone, what it's
   doing).
7. **Polish.** Lowercase by default, near-duplicates collapsed, grammar agreement in templates, the Exchange mentioned
   only when asked where to buy or sell.
8. **Then the parts this round couldn't measure:** multi-turn conversations (5–10 turns, checking memory and
   contradictions), an hour at 900 bots a realm (lines per minute, repeats, spam bursts), and an in-game command for
   the owner and players to flag a bad reply with its context, which becomes the next round's corpus.

## Caveats

- Both judges and the corpus writer are models, not 2003 players; their calls are consistent and blind, not
  authoritative. A human panel (the owner, r/daoc volunteers) is the next step for the spot-the-bot part.
- The 90 "player" lines in spot-the-bot were also written by a model (the corpus), so the test asks whether bot lines
  stand out from period-style lines, not from real logs.
- Some blind misses are label mismatches between the corpus's labels and the audit's (join requests above all); the
  graders' reply scores don't depend on labels.
- World-dependent answers weren't run; in game some brush-offs may be answered by the knowledge lookup.
