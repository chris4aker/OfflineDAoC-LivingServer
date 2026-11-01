# Blind chat corpus (DAoC 2002–2003)

`blind_corpus.json` holds 400 lines that a player might have typed on a Classic + Shrouded Isles server
(patches up to 1.65, before Trials of Atlantis). It is for testing chat bots that should sound and reply like other players.

## How it was written

- **Written blind.** No bot code, bot chat lines, repo files or `/home/claude/wt-*` folders were opened. (`sim_transcript.txt`
  was already in this folder and was not read.)
- **Written from memory of the era.** One web search was used, only to check when patches came out. No real post was copied, and
  no real player, guild or developer is named. The names that appear (Thoriel, Grimbold, Kaelynn, Fenwick, Eiliss, Mordath,
  Brannoc, Sigrunn, Oswin) are made up.
- **Drafted long, then cut down.** About 550 lines were drafted by category and cut to 400 by even sampling within each label.
  These blocks were kept whole: roleplay, questions to a named player, multi-part lines, typos/leetspeak and the second turns.
- **Order.** Items are grouped roughly by theme. The 40 second-turn items are **b361–b400**. In those, `context` is the bot's
  previous line and `text` is the player's follow-up.
- **Meaning of `expect`.** It names the kind of reply a real player would give, not the kind of line the player typed:
  - `thanks` covers both sides of a thank-you. Either the bot answers someone's thanks with "np/yw", or the bot thanks someone for
    a grats, a rez or a gift.
  - Urgent group alerts ("ADD!", "add on healer") are labelled `rvr_call`, because they are combat alarms.
  - `none` means silence, or at most an emote, is the natural reply.

## Counts

**By expect label**

| Label | Count |
|---|---|
| answer_info | 37 |
| answer_where | 34 |
| none | 31 |
| banter | 29 |
| answer_yesno | 28 |
| join_group | 28 |
| greeting | 24 |
| trade_reply | 23 |
| rvr_call | 23 |
| thanks | 20 |
| offer_help | 19 |
| roleplay_reply | 17 |
| congrats | 16 |
| farewell | 14 |
| sympathy | 13 |
| insult_reply | 13 |
| answer_who | 12 |
| welcome_back | 10 |
| followup | 9 |

**By channel:** guild 115, group 89, faction 79, say 53, tell 36, lfg 14, trade 14.

**Themes covered:**
- greetings, logins and returns ("re", "back", "linkdead")
- farewells
- places, trainers, camps, quests and how-tos
- LFG ads and invites, including "x" and "inv pls"
- WTS/WTB lines and haggling
- RvR calls: inc, keeps under siege, relics, DF ownership, stealthers
- pulls, adds, oom, medding, wipes and rez requests
- dings, grats and crafting milestones
- complaints, lag and death penalty
- jokes and sarcasm
- PG-13 flames and KS accusations
- RP speech, plus one OOC aside
- questions to a named player
- multi-part lines
- lowercase, typos and leetspeak ("pl0x", "teh", "ne1", "pwned")
- lines that need no reply

## Things I'm unsure are period-correct

- **Channels.** The `lfg`, `trade` and `faction` channels are taken as given by the task. Before the chat-channel changes of the
  1.6x era, much of this traffic went to `/send`, `/region` or the `/lfg` and `/advice` channels. "faction" is used here as
  realm-wide chat.
- **Spellcrafting and alchemy** (1.5x) and **8-man "bomb groups"** fit 2003. Housing (around 1.62 to 1.64) and consignment were
  left out on purpose.
- **Mounts.** There are no personal mounts. Horses are stable-master routes only.
- **Battleground levels.** "Thid" as a battleground is right, but exact level brackets are not given.
- **Crafting skill numbers** ("700 in armorcraft") are plausible but not checked.
- **"o/" and "^^"** may be more common after 2003, but both were in use on IRC by then.
- **Single-tick replies** such as "k", "rdy" and "x" depend on context. Some testers may want to label "x" or "rdy" as `none`.
- **Toned down.** Flames are softer than real 2003 chat, and spelling is somewhat cleaner than it really was.
