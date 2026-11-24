# Realism audit 1.2.3 — interactions (player ↔ bot, besides free chat)

Auditor: interactions. Read at commit `4a00ab75` (`fix/1.2.3-faction-polish`, PR #469 head; main + #469).
Worktree: `/home/claude/a2-interactions` (detached; `origin/fix/1.2.3-faction-polish` was not a local remote ref, so the
PR head commit was checked out directly; the branch is not merged).

**Everything here is tests only, and less than that: it is code reading.** No C# was run, no CI branch was pushed, no
server was started. Counts are static (grep over code and data) or arithmetic on the timers in code. Nothing below is
in the owner's nine screenshots; items that touch them say so.

Paths are relative to `source/server/GameServer/`. "GB" = autonomous gamebot. "Companion" = `/spawn` helper or hired
companion.

---

## Top 10, worst first (ranked by how often a player meets it in a normal evening)

### 1. `/invite` is a switch, not a person
- **What the player sees.** Target any GB, press Invite. Same instant, two system lines: "Brannoc leaves their group
  to answer your invitation." "Brannoc joins your group and starts moving to you." No pause, no "sure", no "sec, in a
  fight". It works on a level 48 for a level 6, on a bot mid-pull, on the healer of a five-bot party that is fighting.
  2–6 s later (75%) the bot says "hi all, ty for the inv".
- **Where.** `bots/BotGroupInvite.cs:71-151` (`TryInvite`): no delay, no level check, no combat check; line 127 pulls
  the bot out of its own party; lines 128 and 148 are the narration. Callers: `commands/playercommands/invite.cs:28,53,68`,
  `packets/Client/168/InviteToGroupHandler.cs:18`. Hello line: `AutonomousBotGroupChatter.cs:170-183`.
- **The words disagree with the act.** By tell the same bot refuses a gap over 8 levels ("im 48 lol, way too high for
  you", `AutonomousBotChatCoordinator.cs:2300-2315`) and offers a duo only within 2–3 levels
  (`AutonomousLivingServer.Tells.cs:77`, `Engage.cs:271`). The invite ignores all of it.
- **What 2002 did.** A grouped player could not be invited at all ("still in a group"). A solo one got a box and took
  3–20 s; mid-fight, after the fight; a 48 said no to a 6, or said "i'll PL you for a bit".
- **How often.** Every invite. Several an evening.
- **Class.** Instant state change with a narrator; rules in chat not shared with the act.
- **Fix (M).** One `InviteDecision` used by tell replies and by `TryInvite`: level gap (same 8), in combat → accept after
  the fight (cap 60 s, with "sec, fighting" by tell), in a bot party → decline in words and offer the party instead
  ("in a grp, we have room tho" → the bot-led invite that already exists). Accept delay 2–8 s solo and idle. Replace
  lines 128/148 with the game's own join message only.
- **Risk.** Medium. Owner chose "invite takes the bot from its party" (2026-10-27 note at
  `AutonomousBotGroupCoordinator.cs:1604`). Hired companions and `/spawn` helpers use the same function and must stay
  instant (`mode165/HiredCompanions165.cs:384,480,670,776`). Needs an owner decision on grouped bots.

### 2. Bots take your mob and your camp, and notice nothing when you take theirs
- **What the player sees.** Solo at a camp, pulling. A GB on the same camp walks up and hits the mob you are fighting.
  No "sry", no "oops". Other bots keep arriving at the camp you are on. The other way: you hit a bot's mob, or pull its
  whole camp from under it, or drag a train over it. It says nothing and does nothing different. Yet in /faction the
  bots talk about exactly this ("someone just walked into our camp and started pulling", `data/chat/debates.lines:156`;
  "if you were there first its your camp", `gen_social_threads.lines:16`).
- **Where.** `bots/autonomous/AutonomousWorldBotController.cs:3396-3460` (`FindCampTarget`): the filter has no "already
  fighting someone outside my group" test (the route-threat scan at `…RouteThreats.cs:98` does have `!npc.InCombat`).
  Camp crowding counts bots only: `AutonomousCampCompetition.cs` (no `GamePlayer` anywhere in the file). No hook exists
  for a player damaging a bot's target (the Living Server hooks are: died, killed, released, resurrected, level, invite,
  lfg, rez; list from `AutonomousLivingServer.*.cs`).
- **How often.** Not measured. Likely many times an evening for a solo leveller, since bots and players are sent to the
  same camps by level.
- **Class.** Bots talk about a social rule they cannot perceive.
- **Fix.** (a) S: skip a mob whose attackers are all outside the bot's group, unless that attacker is in trouble
  (health under ~35%: then help, and say "got it" / take the "ty"). (b) M: count a real player as two bots in
  `AutonomousCampCompetition`, and a bot arriving at a camp with a player on it asks once in /say ("camp taken?" /
  "mind if i pull the other side?") and moves on at "yes". (c) M: a bot whose target dies to a stranger says one of a
  small pool ("uh, that was mine", "np", silence 50%), remembered as mood in the existing memory rows.
- **Risk.** (a) low; a bot at a one-mob camp may idle longer (the empty-camp timeout already moves it on). (b)(c) low.

### 3. In a group, the fight is deaf and orders are answered but not obeyed
- **What the player sees.** Mid-fight: "heal pls", "add on me", "wait", "oom". Nothing. The line is not queued; after
  the fight nobody picks it up. Out of a fight: "pull the next one" → "roger". "wait here" → "k np". "stop" → "ok ok".
  Nothing changes; the bots act only on `/pull`, `/defensive`, `/aggressive`.
- **Where.** `AutonomousBotGroupChatter.cs:226-229`: only groupmates that are not fighting and not casting may answer,
  and with none the method returns. Orders: `AutonomousChatShortTalk.cs:474-475,501-512` (22 verbs) →
  `data/chat/replies_player.lines:121-127` ("k np", "kk will try", "alright alright", "k, will try", "ok ok",
  "roger"). The only honesty check is "cant heal, im a thane" (`AutonomousChatQuestions.cs:632-646`). The promise
  system (`ChatPromises.cs`) covers "coming" words only.
- **How often.** Every grouped session; the first is hit on most hard pulls.
- **Class.** Promise without behaviour; channel gated by a state the request is about.
- **Fix (M).** Map the leader's plain orders to what exists: "pull"/"inc" → `/pull` on the player's target; "wait",
  "hold", "stay", "stop" → the defensive hold; "go", "ok pull" → release. Answer only when the order was applied. For
  the rest keep an honest "cant do that one". In a fight: one bot (the healer first) answers alarms and requests in 2–4 s
  with a two-word line ("on it", "oom", "cant, mezzed"), and a question is queued to the end of the fight (the tell path
  already does this: `AutonomousBotChatCoordinator.cs:2057-2071`).
- **Risk.** Medium for the order bridge (a sentence that starts with "wait" is not always an order; require the player
  to be leader and the line to be ≤4 words). Low for the fight replies.

### 4. A bot-led group ends without a word (looks like a bug)
- **What the player sees.** "last pull then town" … then "Your group has been disbanded." No "good group all", no "ty".
  Same when the last bot goes its own way. And when the player leaves with `/disband`, or kicks a bot, or disbands a
  group they lead: no "cya", no "ty for group".
- **Where.** `BotLedGroups.Announce` always sends through a timer 0.8–2.3 s later (`BotLedGroups.cs:1041`). Its callers
  break the group in the same call: `AutonomousBotGroupCoordinator.cs:2794-2797` (`Announce(...)` then
  `session.Group.DisbandGroup()`), and `BotLedGroups.Leadership.cs:341-342` (`Announce(bot, Lead.Farewell…)` then
  `RemoveMember(bot)`). When the timer fires, `Speak(Channel.Group)` needs a group with a player in it
  (`AutonomousLivingServer.cs:286-288`) and drops the line. So `lead_break`, `lead_town` and `lead_recruit_end` are
  never heard. Player leaving: `…BotLed.cs:306-323` starts a replacement search and says nothing. Bot removed from a
  player's group: `gameutils/Group.cs:222-229`, `421-444`: no line.
- **How often.** Once per bot-led group, and at every group end.
- **Class.** A delayed line whose channel is gone by the time it is said.
- **Fix (S).** Say the farewell synchronously (or delay the disband 3–5 s). Add one "cya"/"ty for the grp" in /say or by
  tell from one bot when the player leaves or a bot is released (60%, 2–5 s).
- **Risk.** Low. Not confirmed by a run: I could not find a test that asserts the farewell is heard.

### 5. Every stranger waves at you; nobody reacts to you
- **What the player sees.** Walk up to a merchant. Each idle GB within 650 units turns to you and waves, bows or
  salutes, one after another, within about a minute. Then: `/wave`, `/bow`, `/hug`, `/laugh`, `/point`, `/rude` at any
  of them: nothing. Stand beside one for five minutes, or keep it targeted: nothing; when it "looks around" it turns
  30–150° at random, never to you.
- **Where.** `bots/autonomous/AutonomousBotBodyLanguage.cs`: greet radius 650 (l.35), once per bot and player per 20 min
  (l.37), 60% of each idle turn while due (l.115), idle turn every 8–25 s in town, 10–30 s elsewhere (l.93-101). Mean
  time to a wave ≈ 27 s per bot; within 60 s about 9 in 10 bots in range have waved. Any player of the realm, no
  memory or level test (l.226-228). Random look: l.134-139. Player emotes: `commands/playercommands/emotes.cs:364`
  sends the animation; the only bot hook is `/beg` to a beggar (l.367). Clicks on a bot are only logged
  (`packets/Client/168/PlayerTargetHandler.cs:83-99`).
- **How often.** Every town visit; every time a player tries an emote on a bot (a first-hour thing to try).
- **Class.** Unprompted where a person would not; silent where a person would.
- **Fix (M).** Greet only players the bot remembers (friend, guildmate, grouped before), plus ~10% of strangers.
  New hook at `emotes.cs:364` for a targeted emote within 512: answer in kind after 1.5–4 s (wave→wave/bow, bow→bow,
  hug→hug or "lol", laugh→shrug, rude→rude or "?" and a mood note, point→look where pointed), 70% of the time, once
  per 30 s per bot. A player standing within 200 units and facing or targeting a bot for 20–40 s: the bot turns to face
  them; after 60–90 s, once, "?", "hi?", "need something?" in /say.
- **Risk.** Low. Body language is already cosmetic and switchable (`bot_living_body_language`).

### 6. "brb", "afk 5", "gn all", "one more pull then i need a break" — and then nothing
- **What the player sees.** A groupmate says "brb, door" and pulls again four seconds later. A bot says "afk 5" in /say
  and walks off to its camp. "ok im off to bed. night midgard" in /faction; the bot is in `/who` all night and answers
  a tell in three seconds.
- **Where.** Away words are tracked only so that a later "back" is allowed
  (`AutonomousChatEngagement.cs:271-327`; `AutonomousBotChatCoordinator.cs:492-493`). No movement, pull or reply code
  reads it. Lines: `data/chat/group_chatter.lines:17,21`, `group_talk.lines:53-55,78`,
  `AutonomousChatFieldTalk.cs:1190,1195,1204,1788,1792,1886-1887` ("brb", "afk 5", "brb food", "brb selling", "one more
  lvl then bed", "bed, night"), `gen_alb_threads.lines:800,802`, `gen_mid_threads.lines:511`,
  `guild_chat_more.lines:68`. A real leave exists only for the schedule and breaks
  (`AutonomousBotSchedule.cs:345,358-388`), and the schedule is off by default (owner, 2026-11-21).
- **How often.** Not measured live. 6 away lines in the two group files; "brb"/"afk" are in three field-talk pools.
- **Class.** Stated state without the state (the same family as the fixed "oom at full power", RC6).
- **Fix (M).** One `Away` state per bot, set when it says an away line: stand or sit, no pull start, no chat or tell
  replies (tells queue), 40–240 s by line ("door" short, "food" long), then "back". In a group the leader holds the
  pull as it does for a sitting player. A goodnight line is allowed only for a bot the break system is about to log
  out; otherwise filter it (same as `FitsGroup` does for "back").
- **Risk.** Medium in groups (a healer going away mid-camp must not start during a fight; gate on out-of-combat and
  cap at one away bot per group).

### 7. `/who` and system lines call them bots
- **What the player sees.** `/who`: "Brannoc the Level 30 Thane in Vasudheim [Playerbot]" on every row. System lines:
  "Only a gamebot of your realm will accept the invitation.", "X is a buffbot and never joins a group.", "X is
  another player's hired companion…", "[X sells] 3 pieces: say "buy <number>" to X, or /tradewindow buy <number>.",
  "X has not accepted: no use for it" / "it is level 20, too low for it".
- **Where.** `commands/playercommands/who.cs:318-332` (tag at l.330; also no guild, no `<AFK>`).
  `bots/BotGroupInvite.cs:33-44,75,82,89`. `craft/economy/commerce/BotTrades.cs:410,514-526`. 84 player-facing
  messages in bots/commands/craft mention bot, gamebot, companion or helper (grep count, includes the helper commands
  where that is right).
- **How often.** Every `/who`; every refused invite or trade.
- **Class.** The interface names the trick.
- **Fix (S).** A setting for the `/who` tag (default: no tag; add guild name and a few `<AFK>` for bots in the Away
  state of item 6). Reword GB-facing refusals as the game words them for players, and let the bot's own tell carry the
  reason (it already does for duties: `Engage.cs:211-238`).
- **Risk.** Low, but the tag may be deliberate: owner decision. Launcher or tools that parse `/who` output must be checked.

### 8. Tells: the dead never answer, the living always do, in three seconds
- **What the player sees.** A tell to a bot that just died: silence (the one moment a real player has free hands). A
  tell to any other bot: an answer every time; "hi" → "hey" in about 2–4 s. In a fight: always "sec, fighting" 4–9 s in,
  then the answer when the fight ends, or silence after 90 s. `/yell` for help beside five bots: nothing. `/whisper`
  to a targeted bot: nothing.
- **Where.** `AutonomousBotChatCoordinator.cs:2016-2096`. Dead → dropped at l.2051. First wait `1.0 s + 20 ms per
  character (max 3 s) + 0–1.5 s` (l.2096), then typing at the bot's speed, cap 12 s (l.1980, 2079). Busy: retry 5 s,
  ack at 4 s, give up at 90 s (l.1973-1978). The invite-decline path does let a dead bot type (`Engage.cs:232`):
  inconsistent. `/yell`: no bot hook (`commands/playercommands/yell.cs`). `/whisper`: `GameBot.WhisperReceive`
  (`bots/GameBot.cs:3235`) handles hired-companion dialog links only.
- **Humanlike?** The typing model is fine. The spread is not: no slow answers, no missed tells, no "sry was afk".
- **How often.** Every tell.
- **Fix (S).** Dead bots answer (and sooner). Add a tail: 15% of first tells from a stranger wait 10–40 s; a bot in the
  Away state answers on return with "sry, was afk". After the 90 s give-up, answer when free with "sry, was fighting"
  instead of dropping. Route `/whisper` to the tell path as /say. `/yell` with no text ("yells for help") → the
  existing help-call path (`ChatPromises`, reason Help) for bots within yell range.
- **Risk.** Low.

### 9. You cannot give a bot anything, and kindness gets no thanks
- **What the player sees.** Drop 5 gold on a lowbie bot as a tip for a rez: a trade window opens and says "Nothing to
  trade yet"; it cannot be accepted. Drop a spare sword on a bot that has no use for it: "X has not accepted: no use
  for it". If it wants it, it pays you; a gift is not possible. Buff or heal a passing bot: no "ty". Ask an ungrouped
  healer bot "buff pls" or "heal?": a line, no cast (bots rez strangers, nothing else: `bots/BotBrain.cs:5264-5345`).
  In /trade, a bot answers your WTS with "how much?" (40% of posts) or your WTB with "how much u paying?" (35%); you
  cannot answer for 60 s (slow mode) and nothing reads the answer.
- **Where.** `bots/GameBot.cs:305-317` → `craft/economy/commerce/BotTrades.cs` (blockers:
  `BotTradeTerms.cs:170-173`). No buff/heal hook (hook list in item 2). /trade:
  `AutonomousBotChatCoordinator.cs:1369-1399`, `AutonomousChatMarket.cs:49-56,224-237`,
  `commands/playercommands/trade.cs:31-39`.
- **How often.** Gifts and tips: a few times a week for a friendly player; the /trade question on roughly 4 in 10 posts.
- **Class.** Shown interest with no follow-through; a missing social verb.
- **Fix (M).** Gift path: coin, or an item with no coin asked, is accepted when nothing is owed; the bot says "ty!!" /
  "wow thanks" (the beggars' thank lines show the pattern, `AutonomousBeggars.cs:687-749`), wears an upgrade, and the
  memory row gets a "gift". Thanks for a buff or heal from a stranger: 50%, 2–5 s, /say "ty" (+ a bow). An idle healer
  or buffer asked in /say within 512 casts one buff or heal, then "np". The /trade "how much?" should come only from a
  bot that would buy (the appraisal exists: `BotTrades.cs:508`), by tell, and a price answered by tell opens the trade
  or gets "too rich for me".
- **Risk.** Medium: coin into bots touches the economy flows (name the flow kind as the beggars do). Owner rule "real
  loot, inventories, coins" is kept, since the player's own coin moves.

### 10. Bots' own invites and exits are machine-timed
- **What the player sees and the numbers.**
  - `/lfg` → a leader's tell after **1.0–2.5 s**, the invite box **1.5–3.0 s** after that (`BotLedGroups.cs:39-45,
    842-843`). Nobody reads /lfg and types that fast. Humanlike: 6–25 s.
  - Unasked: "lf 1 more for skellies in Cursed Forest, want in?" and the box **2.5–4.5 s** later, before any answer
    (`BotLedGroups.cs:842-843`). Either ask and wait for "sure" (the tell-answer path exists, l.2115-2124 of the
    coordinator), or blind-invite without the question.
  - Two wordings for one thing: "X has invited you to join his group. Do you wish to join?" (`BotLedGroups.cs:878`)
    and "X (30 Thane) invites you to group up. Join?" (`AutonomousChatEngagement.cs:173-174`).
  - Guild: "lf guild" → officer's line and box together after 2–4 s (`AutonomousBotGuildSocial.cs:755-769`); fine.
    The other way: `/gc invite` of a GB by name joins "at once", from any distance, mid-fight, silently unless it left a
    bot guild (`commands/playercommands/guild.cs:373-398`, `AutonomousBotGuilds.cs:166-196`).
  - Logout: a line, then the body is deleted **2.5–4.5 s** later where it stands (`AutonomousBotSchedule.cs:358-388`).
    A 2002 quit was 20 s seated.
  - A GB in your group never leaves: grouped bots take no break and never log (`AutonomousBotSchedule.cs:275-287`).
- **How often.** Each /lfg; each unasked invite (≤1 per 45 min); logouts only with breaks or the schedule on.
- **Fix (S each).** Asked window 15 s → 30 s with tell at 5–15 s; unasked box only after "sure", or drop the question;
  one dialog text; guild join after 3–10 s with "sure, ty" in /gu and a refusal in combat; logout = sit 15–20 s then
  delete. The never-leaving groupmate is a convenience: leave as is, or an optional "gotta go in 10" after 2–3 h.
- **Risk.** Low. `AskedWindowMs` is pinned by `UT_BotLedInvitePolicy`; tests need the new numbers.

---

## Smaller findings

- **Companions speak like butlers (companions, second priority).** `/spawn` helpers: "My thanks, Dumbledork. I am
  ready to continue.", "I have caught up. Returning to formation.", "I woke at the bindstone. Hold the road; I will
  return on foot." (`bots/GameBot.cs:1400-1405,1672-1690`). Hired companions got the gamebot voice on 2026-11-18;
  `/spawn` helpers did not (`IsSpeaker` excludes them, `AutonomousLivingServer.cs:241`), and they answer no tells
  (`commands/playercommands/send.cs:37,58`). Fix S: reuse `GamebotReturnLine` and `rez_thanks`. Risk: the Formal option
  for hired companions must keep its lines.
- **No rez sickness.** A rezzed bot fights at once; 35% grumble about sickness it does not have
  (`AutonomousLivingServer.Events.cs:609-615`). Fix S: sit 20–40 s after a rez in a group ("sec, rez sick").
- **"X is dead and can't join you right now."** (`BotGroupInvite.cs:35`). A dead player can accept an invite.
- **/duel on a bot:** "you need a target" (`commands/playercommands/duel.cs:124-129`). A person would say "no thx" or
  accept. Fix S: a tell ("lol no", "after this pull?"); no duel.
- **/assist** works on a bot target (`assist.cs:153-200`). Fine.
- **The duo offer after `/lfg`** comes after 9–33 s for the first, later ones stacked (`Tells.cs:314-341`). Humanlike.
- **A real invite from a bot makes the player the leader** (`Engage.cs:201`). Odd but harmless.

## Timers, in one table

| What | Number | Where | Humanlike? |
|---|---|---|---|
| Bot accepts your `/invite` | 0 ms | `BotGroupInvite.cs:71-151` | No |
| "hi all, ty for the inv" | 2.5–6 s, 75% | `AutonomousBotGroupChatter.cs:172-175` | Yes |
| Decline tell ("sry, on the raid") | 2–5 s, once/min | `Engage.cs:225,233` | Yes |
| Tell reply, first wait | 1.0–5.5 s + typing ≤12 s | `…ChatCoordinator.cs:2096,2079` | Median yes, no tail |
| Tell while fighting | ack at ≥4 s, retry 5 s, drop at 90 s | `…:1973-1978` | Too regular; drop is wrong |
| Tell to a dead bot | never | `…:2051` | No |
| /group reply | read+think+type, ≥2 s + 0.3 s/word; none in a fight | `AutonomousChatArbiter.cs:94-105`, `…GroupChatter.cs:227` | Yes / No |
| /lfg → leader's tell → box | 1.0–2.5 s → +1.5–3.0 s | `BotLedGroups.cs:41-43` | No |
| Unasked invite: tell → box | 1.5–4 s → +2.5–4.5 s | `BotLedGroups.cs:842-843` | No |
| Unasked invite cadence | ≥10 min after login, 45 min apart, 30 min after a no | `BotLedGroups.cs:32-56` | Yes |
| Bot-started tells | first at 4–10 min, then 25–45 min, 6 h per pair | `Tells.cs:19-23` | Yes |
| Meet-up wait | 20 min, reminders at 5 and 10 | `BotLedGroups.cs:69-71` | Yes |
| "where u at?" / go on | 2 min / 10 min | `BotLedGroups.cs:65-67` | Yes |
| AFK | idle 10 → hold 15 → let go 30 min; linkdead 5 min | `BotLedGroups.cs:73-79` | Yes |
| Death sympathy in /say | 2.5–5.5 s, 60% | `Engage.cs:131-134` | Yes |
| Rez thanks | 1.5–3.5 s | `Events.cs:635` | Yes |
| Guild invite after "lf guild" | 2–4 s, box with the line | `…GuildSocial.cs:769` | Borderline |
| Wave at a passer-by | mean ≈27 s, every bot, 20 min repeat | `…BodyLanguage.cs:35-37,93-115` | No (everyone) |
| Trade hold | 120 s, ends on a fight | `BotTrades.cs:58` | Yes |
| Logout after goodbye | 2.5–4.5 s, standing | `…Schedule.cs:388` | No |

## Checked and found fine (on paper)

- **Refusals by duty** (raid, siege, relic, realm force, horse): system line plus the bot's own tell, once a minute
  (`BotGroupInvite.cs`, `Engage.cs:211-238`).
- **Too-far invite:** tell with where the bot is (`Tells.cs:232-271`). Screenshot 1's "lol" reply is being fixed in
  #465–#469 (`AutonomousChatMeetup`).
- **Bot-led group design:** invite rules, level band 3, pace, the pull gate on the player's health and sitting, AFK
  steps, leader succession, loot weight (`BotLedGroups.cs`, `…GroupCoordinator.BotLed.cs`, docs/BOT-LED-GROUPS.md).
  The lines exist for every step; only the ending is lost (item 4).
- **"omw" promises:** `ChatPromises.cs` makes them real or withholds them. I found no "coming" word outside it.
- **Rez:** a free healer bot rezzes a dead stranger and says so (`BotBrain.cs:5264-5345`, `Engage.cs:147-169`); a dead
  bot asks a nearby player rezzer and waits 90 s (`Events.cs:556-590`).
- **Trade window:** busy rules, hold, timeout line, both directions, repair and spellcraft jobs (`BotTrades.cs`).
- **Friends list** resolves bots online and offline (`bots/BotFriends.cs`). **/send to an offline bot** gives the
  game's own "not in the game".
- **Guild:** officers and leaders of bot guilds refuse a player's guild in words (`AutonomousBotRealmGuilds.cs:604-626`).
- **Beggars** take coin and items and thank (`AutonomousBeggars.cs:687-749`).

## Could not check, and why

- **Anything at run time.** No dotnet here and I pushed no CI branch. Items 2 and 4 most need a run: 2 to count how
  often a bot picks an engaged mob, 4 to confirm the farewell is dropped.
- **Whether a bot invited mid-fight finishes its mob.** `EnterPlayerLedGroup` sets the FOLLOW state
  (`bots/GameBot.cs:3131-3149`); the brain may re-enter combat. Not traced.
- **/stick, /follow, /face on a bot.** Client-side on the original client; no server command file exists. The docs
  mention a PC `/follow` of a leader on 2026-11-07.
- **The Realm Exchange with a player beside a bot, haggling by tell, crafter commissions.** Read only at the entry
  points; `CrafterCommerce`/`CrafterHaggle` not traced.
- **Whether a price by tell after a bot's /trade "how much?" reaches any buying code.** The commerce tell handler runs
  first (`AutonomousLivingServer.Tells.cs:69`); I did not trace `BotTrades` l.1139-1210 for a bare price.
- **Raid-leader invites (B3), battlegrounds, RvR groups.** Out of time.
- **Live frequencies** of away lines, waves, and camp overlaps.

## Measuring tools worth building (none built)

1. **`UT_InteractionMatrix` (C#, report-only, could ratchet).** One row per (action × bot state): invite × {idle,
   fighting, in a bot party, +20 levels, dead, riding}, tell × {idle, fighting, dead}, emote, gift, group order,
   group line in a fight, group end. Columns: delay ms, what the bot said, what changed. Printed as
   `INTERACTION_ROW` like `CHAT_BENCH_ROW`. Ratchet: no row with delay 0 and a state change; no row with a reply and
   no change for an order; no silent row where a reply is due.
2. **`tools/bot-voices/eval/claims_lint.py` (Python, CI-ready).** Flags corpus and code lines that claim a state
   ("brb", "afk", "gn", "bed", "one more pull", "gotta go", "roger", "on it", "will do") unless tagged with the state
   that makes them true, as `// promise: true` does for "omw" today. Start count from this audit: 6 lines in the group
   files, 11 `V("brb…"/"afk…")` entries in `AutonomousChatFieldTalk.cs`, 6 order acks. Ratchet: the count may
   only go down.
3. **A `BOT_KS` log line** when a bot's first hit lands on a mob whose attackers are outside its group. Gives the
   live rate for item 2 from one evening's PC log.

---

## Hand-back summary

1. `/invite` is instant, any level, any state, and narrated by system lines; the bot's own chat rules (8-level gap,
   "in a fight") are not applied to the act. `bots/BotGroupInvite.cs:71-151`. M, owner decision on grouped bots.
2. Bots attack mobs the player is fighting and crowd the player's camp; no reaction when the player does it to them.
   `AutonomousWorldBotController.cs:3396-3460`, `AutonomousCampCompetition.cs`. S + M.
3. /group lines during a fight get no answer and are not queued; orders get "roger" and no action.
   `AutonomousBotGroupChatter.cs:226-229`, `replies_player.lines:121-127`. M.
4. Likely bug: the bot leader's goodbye is scheduled after the group is already disbanded and is dropped.
   `AutonomousBotGroupCoordinator.cs:2794-2797`, `BotLedGroups.cs:1041`, `Leadership.cs:341-342`. S. Needs a run.
5. Every idle bot within 650 waves at any player (≈27 s mean); emotes at bots, staring and standing by get nothing.
   `AutonomousBotBodyLanguage.cs`, `emotes.cs:364`. M.
6. "brb", "afk 5", "gn all", "one more pull" change nothing the bot does. `AutonomousChatEngagement.cs:271-327`. M.
7. `/who` tags every bot "[Playerbot]"; refusals say "gamebot", "buffbot". `who.cs:330`. S, owner decision.
8. Tells: dead bots never answer; all others always answer in 2–4 s; `/yell` and `/whisper` are ignored.
   `AutonomousBotChatCoordinator.cs:2051,2096`. S.
9. No gifts or tips; no thanks for a buff or heal; "buff pls" gets words only; /trade "how much?" leads nowhere. M.
10. Bot invites and exits are machine-timed: /lfg → tell in 1–2.5 s → box in 1.5–3 s; unasked box before any answer;
    logout 2.5–4.5 s standing; guild join instant from anywhere. S each.
- Companions (`/spawn`) still speak in the formal butler voice and answer no tells. S.
- Fine on paper: duty refusals, too-far tells, bot-led pacing and AFK rules, "omw" promises, rez both ways, the trade
  window, friends list.
- Not run: nothing was executed. All of it is code reading at `4a00ab75`. Items 2 and 4 need a CI run or a PC log.
- Tools proposed, not built: an interaction matrix test, a "claims without state" lint (both could ratchet), a
  `BOT_KS` log line.
- Note for the lead: `origin/fix/1.2.3-faction-polish` did not resolve as a remote ref in the clone; I used the PR
  head commit `4a00ab75` directly.
