# Tales

Living Server 1.2. The owner's idea (2026-11-10): "a bot in faction asks another famous bot to tell the story about X. A
couple others chime in and tell everyone to shut up and listen, and beg the storyteller. Then the storyteller tells the
story with a question or two from others that help lead the story along. Then when it's over everyone is in awe, slightly
quiet in reflection, and then maybe a couple rumours that are related come next." He chose the fuller version, in 1.2.

## What has been seen in game

Owner's PC, test copy, 2026-11-10, staged with `/lstest tale` unless marked. "Tests only" means unit tests and CI.

| What | Seen | Build |
|---|---|---|
| A tale starts, is heard line by line, ends with the hush and the closing lines | Yes: Legion, Golestandt and Apocalypse, both storytellers, five tales | 15fcc70, 5300a87. On cf2c873 it started and ended but no line was heard. |
| The storyteller answers a player's question mid-tale | Yes, twice | 15fcc70, 5300a87 |
| A player asks for a tale in plain words ("tell the one about golestandt") | Yes, started within a second | 15fcc70 |
| The rumour after a finished tale | Yes, four times; held and said later three times | 15fcc70, 5300a87 |
| The rumour is not a sentence of the tale | Yes on 5300a87 (twice). On 15fcc70 it was. | 5300a87 |
| A fireside tale in /group, naming the storyteller | Yes (Dartmoor, one gamebot in the group) | 15fcc70 |
| The rumour is a raid that then opens (`TALE_RAID`) | **No. Tests only.** All four rumours were lore; those builds did not log whether the roll or the raid slot decided it. | |
| A tale goes on when a listener leaves; a busy teller is waited for briefly | **Tests only** (both faults were seen on 15fcc70; on 5300a87 neither situation came up) | |
| A tale that starts by itself, without `/lstest` or a request | **Not seen yet** (the first one comes 25 minutes into a session) | |

Faults those checks found, each fixed in the build after: on cf2c873 no line of a tale was heard and the rumour was lost;
on 15fcc70 the storyteller's answer, a line of the tale and the rumour were the same sentence, a tale a player asked for
stopped after twelve lines because the bot that begged for it logged off, and a storyteller who got into a fight after the
opening lines kept /faction silent for three minutes; on 5300a87 the storyteller's answer said in other words what the
tale said two minutes later ("the whole fortress stops turning").

## What a player sees

**A tale in /faction.** Now and then a bot asks the realm's storyteller, by name, for the story of a dragon or a boss.
One or two others hush the channel and beg. The storyteller tells it in about a dozen short lines; someone asks a question
on the way and the next lines answer it. Then the channel is quiet for about half a minute, one or two listeners say
something small, and a little later a bot passes on a rumour about the same boss.

**Sometimes the rumour is true.** About half the time, when the realm's raid on that boss could form anyway, the rumour is
"heard there is a raid forming for him tonight", and six to twelve minutes later that raid really opens: the realm's own
automatic raid, with its usual size, muster and notices. Its leader invites the player like any other raid.

**The storytellers are people.** Each realm has up to two, chosen once and kept across restarts. Other bots name them, and
only they tell. If you ask for a tale while neither is online, a bot says so.

**You can ask.** "tell the one about legion", "any stories tonight?" or "Maeve, tell us a tale" in /faction starts one
within a few seconds (not within twenty minutes of the last). Ask a question while a tale runs and the storyteller answers
you, at most twice a tale, then goes on. The answer and the rumour afterwards are never a sentence the tale itself tells.

**It can go wrong.** A storyteller pulled into a fight is waited for a little (45 seconds at most at one line, a minute
and a half over the whole tale) and then types between pulls, as a player would. One who leaves the game ends the tale: a
listener grumbles, and it is told another night. A listener who leaves is no loss: another one asks the next question. A
cynic may heckle. /faction's ceiling still holds: a tale waits for room like any scene.

**Fireside tales.** Sit down out of combat in a boss's place (a dragon's zone, an epic dungeon, Darkness Falls, Summoner's
Hall) with a gamebot in your group, and a groupmate tells the short version in /group, five lines or so, and says who in
/faction tells it properly. Once a day per character and tale, at most one every half hour.

## How often

- A realm starts a tale on its own at most once in about three to four and a half hours, and not in the first 25 minutes
  of a play session. It starts only while a player of that realm is online.
- The tale the realm heard longest ago comes first; one about the place a player is standing in is preferred six times in
  ten.
- 1.2 ships fifteen tales: Golestandt, Apocalypse and Xanxicar (Albion); Gjalpinulva, King Tuscar and Nosdoden
  (Midgard); Cuuldurach, Olcasgean and the Black Lady (Hibernia); and Legion and Govannon as each realm tells them. Each
  has its fireside version.

## Settings

Server properties, category `bots`, read live:

| Setting | Default | What it does |
|---|---|---|
| `bot_living_tales` | True | Tales in /faction and fireside tales. Needs bot voices and debates (`bot_voices_debates`). |
| `bot_living_tale_raids` | True | The rumour after a tale may be a raid that really forms. False: always a lore rumour. |

GM checks: `/lstest tale [boss]` asks the realm's storyteller for a tale now (`/lstest tale legion`). With `raid` at the
end (`/lstest tale golestandt raid`) the rumour after it is the raid, without the roll, when that raid can form; the reply
says why it could not form right now. `/lstest fireside` forgets today's fireside tales so the next rest tells one.

## Where it lives

| Piece | File |
|---|---|
| The tales | `data/chat/tales.lines`: scenes like any debate. Topic `tale_<lore id>`; role B is the storyteller, `{name_b}` its name; A asks, C hushes, D asks the questions, E has heard it too often, F is new. Fireside scenes: topic `fireside_<lore id>`, A tells, B listens. |
| Scheduling, storytellers, requests, the rumour and raid, fireside | `AutonomousTales.cs` |
| Playing the scene | `AutonomousBotDebates.cs`: `StartTale` (the storyteller is cast as B and never recast; a busy one is waited for like a storyline's protagonist), `TellerAside` (its answer to a player). Ordinary debates never pick a `tale_` or `fireside_` scene. |
| The raid | `AutonomousRealmRaid.CouldStartForTale` and `StartForTale`: the scheduled raid's own checks and start. |
| Memory | `offline_lore_memory`, keys `tale:teller:<realm>:<slot>`, `tale:told:<realm>:<scene>`, `tale:last:<realm>`, `tale:fire:<character>:<scene>`. No new table. |

Log lines: `TALE_TELLER`, `TALE_START`, `TALE_NOT_STARTED`, `TALE_END` (finished, lines, reason), `TALE_ASIDE`,
`TALE_REQUEST`, `TALE_RUMOUR` (kind=raid or lore; `no_raid=` says why it was not a raid: the roll, or the raid's own
reason), `TALE_RAID` (opened, reason), `TALE_FIRESIDE`. The scene's lines are
`CHAT_LINE src=tale`; the lines after it (the rumour, a grumble when a tale is cut short) are `src=tale_after`: they are not
the scene's, so they fit under /faction's ceiling, and they keep their turn for three minutes (`LIVING_FEATURE_DUE` /
`LIVING_FEATURE_DROPPED feature=tale_after`).

## Writing a tale

- Keep to the boss's lore file (`data/lore/bosses/<id>.json`, the `story`). A tale is that story told aloud, not new lore.
- Chat voice: a teller types. Short lines, no narration, one idea a line, at most 140 characters.
- The ask (first line, role A) names `{name_b}`. Two or more lines pass before the teller begins.
- At least eight lines from the teller, at least one question from a listener, one `~ 8` breath in the middle.
- End with a pause of 20 seconds or more, then one to three lines from listeners. The teller does not speak after it.
- Every sentence of more than twelve characters must be unique across all scenes (`build_corpus.py` checks).
- Each tale needs a fireside scene of the same lore id for the same realms, and a raid in `AutonomousTales.RaidStems`.
- Run `python3 tools/bot-voices/build_corpus.py`, then the `UT_Tales` tests.

## Not built

- Stories about this server's own history ("the night Golestandt wiped 64 of us"). That needs the world-history record
  planned for 2.0.
- Two storytellers disputing a version, and a player's own deeds told as a tale.
- A launcher switch (the two server properties are the only settings).
