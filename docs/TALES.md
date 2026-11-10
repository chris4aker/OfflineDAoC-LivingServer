# Tales

Living Server 1.2. The owner's idea (2026-11-10): "a bot in faction asks another famous bot to tell the story about X. A
couple others chime in and tell everyone to shut up and listen, and beg the storyteller. Then the storyteller tells the
story with a question or two from others that help lead the story along. Then when it's over everyone is in awe, slightly
quiet in reflection, and then maybe a couple rumours that are related come next." He chose the fuller version, in 1.2.

Nothing here has been played yet. Everything below is built and covered by automated tests only.

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
you, at most twice a tale, then goes on.

**It can go wrong.** A storyteller pulled into a fight is waited for up to three minutes; if it stays busy or leaves, the
tale simply stops, a listener grumbles, and it is told another night. A cynic may heckle. /faction's ceiling still holds:
a tale waits for room like any scene.

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

GM checks: `/lstest tale [boss]` asks the realm's storyteller for a tale now (`/lstest tale legion`); `/lstest fireside`
forgets today's fireside tales so the next rest tells one.

## Where it lives

| Piece | File |
|---|---|
| The tales | `data/chat/tales.lines`: scenes like any debate. Topic `tale_<lore id>`; role B is the storyteller, `{name_b}` its name; A asks, C hushes, D asks the questions, E has heard it too often, F is new. Fireside scenes: topic `fireside_<lore id>`, A tells, B listens. |
| Scheduling, storytellers, requests, the rumour and raid, fireside | `AutonomousTales.cs` |
| Playing the scene | `AutonomousBotDebates.cs`: `StartTale` (the storyteller is cast as B and never recast; a busy one is waited for like a storyline's protagonist), `TellerAside` (its answer to a player). Ordinary debates never pick a `tale_` or `fireside_` scene. |
| The raid | `AutonomousRealmRaid.CouldStartForTale` and `StartForTale`: the scheduled raid's own checks and start. |
| Memory | `offline_lore_memory`, keys `tale:teller:<realm>:<slot>`, `tale:told:<realm>:<scene>`, `tale:last:<realm>`, `tale:fire:<character>:<scene>`. No new table. |

Log lines: `TALE_TELLER`, `TALE_START`, `TALE_NOT_STARTED`, `TALE_END` (finished, lines, reason), `TALE_ASIDE`,
`TALE_REQUEST`, `TALE_RUMOUR` (kind=raid or lore), `TALE_RAID` (opened, reason), `TALE_FIRESIDE`. The scene's lines are
`CHAT_LINE src=tale`.

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
