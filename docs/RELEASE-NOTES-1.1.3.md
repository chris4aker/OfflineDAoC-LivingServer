# Living Server Beta 1.1.3: release notes

The text below the line is the GitHub release notes for `v1.1.3-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing. Nothing here names the Easter eggs or the project's internal vocabulary.

---

### Bots that talk like players, raids that keep moving, keeps worth defending, and quiet nights

1.1.3 is an update on top of 1.1.2. It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client,
like 1.1.2. Install it over 1.1.2 (or earlier) and keep everything; the launcher offers it as an update.

It started as a small hotfix. Then the owner's overnight checks on a real install showed how much of the bots' chat still
didn't sound like a real server, and what still went wrong in a long raid, and those were fixed too.

### Chat that sounds like a real server

**Conversations that hold together.** An answer no longer comes before its question, nobody thanks an answer that never
came, and a bot answers a call for a tank, a healer or its own class only if that's what it plays, near the level asked
for. A bot's good or bad moment draws one or two replies, big realm news a few, and the same short "oof" or "rip"
isn't heard twice in ten minutes. A dunce's scene, a debate or a story keeps its speakers to the end.

**Questions get answers.** Most questions bots asked in /faction used to hang there unanswered. Now someone answers,
and the answer is true where the game knows it: the real trainer and where to find them, the usual spec, where bots of
that level really hunt, how realm points work, back to whoever gave you a quest, the way to a zone ("Cornwall is its own
zone, a stable master can get u close"), and "what class?" gets a class. Your own questions get the same. /lfg and
/trade get some replies too ("x", "pst price?", "still need a healer?"), and a poster says "nvm full" or "sold" only when
that really happened.

**Bots sound like players.** Realm news comes out of a bot's mouth the way a player would type it ("krondon raid forming
at diogel, need 50s"), not as a herald's announcement. Reactions to news vary over an evening instead of "ugh, that one
again" every time. Debaters type in short lines instead of writing like a book. A death is told by what really killed the bot: a
gank as a gank ("got ganked lol"), never as if an enemy player were a kind of monster, a monster by its name, the guards
as the guards, and never more attackers than there were. A story of your deeds names the real monster and where. Nobody
says "tonight" in the morning, debates don't announce attacks that aren't happening, and a newcomer's first trip to the
frontier is told only by a bot that's really out there.

**Bots keep one story.** A bot either looks for a group or forms one, not both, and posts for the dungeon it's really
going to. Only a bot that's really solo posts "lfg", and only a party that's still forming posts "lfm". An item is only
asked about for a class that can use it, a level 50 doesn't ask where its own trainer is, and quest, stable and camp
talk names only zones of the bot's own realm. A bot in town asks for a group to head out with rather than "grinding" the
town, and a bot that said it found a group isn't asked later whether it ever did.

**Bots notice you.** Friends say hi when you log in, a grats when you ding, "ouch, u ok?" when you die near them, and a
healer offers a rez. Now and then a solo bot near your level invites you, with a real invitation. A busy bot you invite
says why it can't ("sry, on the raid in krondon atm"), one too far away says where it is, and a friend answers your
small talk with what it's really doing. A bot answering your /lfg doesn't ask the level you just posted, and a friend
who'd like to group again asks you directly while you're on. Your own /trade posts get answers too. Group chat fits a duo and talks about the
hunt only when you hunted together, and guild chat sounds like people.

**An evening that remembers, and regulars you get to know.** A handful of talkative regulars carry about half of /faction
all evening, each with its own manner and running jokes, and they ask after earlier talk ("did you ever find that
camp?"), always about something that was really said.

### Raids

**Out of Xanxicar's stomp.** His first stomp used to kill most of the raid in one second. Now the raid pulls back in good
time to the nearest open floor the stomp can't reach, while the party fighting him still holds him. Xanxicar is unchanged.

**Scurceol Hyrde's orbs.** He can't be hurt while his four floating orbs live, and the raid never found a place to shoot
them from. Now each party's casters and archers shoot their own orb from a spot on his platform with a clear line to it,
melee wait on the floor instead of trying to walk into the air, and Scurceol falls once the last orb is down. He and his
orbs are unchanged.

**No raid waits forever.** A raid started from the launcher waited at its gathering town until every one of its 200 was
there, and one bot stuck in another dungeon held it for good. Now it waits 15 minutes, then goes with nine in ten, and
after 30 minutes with half; the rest follow it in. A bot left behind in a dungeon no longer waits for a party member who
is somewhere else, and heads out to its party.

**Raids keep moving.** Inside a dungeon the raid clears the floor it stands on before going down to the next, instead of
walking back and forth between floors, and parties clear the ordinary monsters on their way to a gathering instead of
standing still before each one.

**Orshom's Fire.** The burning pit lit during the Orylle and Orshom Brong fight killed a hundred raiders in one raid.
Raiders now stay out of it, and those with a cold spell put it out from outside its reach.

### Keeps, the frontier and the server

**Defenders come in time.** A keep's realm was told only when the battle had already begun, so many keeps fell before a
defender arrived. Now scouts report the enemy army as it marches, and the realm's free warbands man the walls. The
balance between attackers and defenders aims at the attackers winning about half of their sieges.

**No more waiting in the capital for a porter.** A gamebot heading for the frontier from its bind in a capital waited for
a porter that isn't there. It now goes to its realm's border keep first.

**Fewer stalls.** Two searches that could hold the server for a second or two on a busy night are capped.

**Quiet nights.** With the Bot schedule on "Real clock" (launcher, Living Server tab), the realm follows a real server's
day: the evening crowd from 7 to 11 pm, a few hundred online before dawn, busier weekends. Bots log off a few at a time,
and a bot in a group, raid or siege finishes it first. Setup switches Real clock on for a new install; on an existing
install, check the Living Server tab.

### Also in this update

- **Two Darkness Falls exits work.** The exit by Legion's hall and the one near the Midgard boss now take you to your
  realm's usual exit. Nothing is written to your world for this. Found and fixed by rambodolphin13.
- **Setup waits out a busy download server.** After a "503" error Setup waits and tries again, up to four times over
  about seven and a half minutes. If it still can't download, it says so and changes nothing.

### How it was tested

Every server, launcher and Setup test passes on GitHub, including a real download install. Beyond that, each result below
says which build it came from. Release candidates (RC) are the test builds; the build that ships is the last one with
one small fix, named below.

**On a real install (the owner's PC, a test copy of the game):**
- Chat, RC2 to RC5: a chat-checking tool read 68, 151, 43 and 30 minutes of evenings. No line out of order, no bot
  claiming a class, level or role it doesn't have, /faction under its ceiling. Reading the lines themselves showed what
  the tool couldn't: unanswered questions, herald-style news, contradicting bots, items for the wrong class.
- Chat, RC6 (the fixes for those): 74 minutes as an Albion 50 and a new Hibernian, with the Bot schedule on Real
  clock. Asked in /faction where to hunt at 35, where a trainer is and where a new character should level, the player
  got true answers within 16-34 seconds. Reading the log showed new problems: a friend's answer swapped for "not sure,
  try /advice", a grouped bot talking about pulls and loot when nobody had fought, deaths told against what killed the
  bot, "tonight" in the morning, debates announcing attacks that weren't happening. The checker now looks for each.
- Chat, RC7: 77 minutes. A friend's answer to "hey! what u up to?" was true of the bot ("rvr in Odin's Gate, its busy,
  wanna duo?"), a far bot said where it was, an idle duo heard one group line in three minutes, 93% of bot questions
  were answered within a minute. The checker found one death told wrong.
- Chat, RC8: 46 minutes, 88% of bot questions answered within a minute, nothing out of time or place. The checker found
  a wipe by enemy players told as a wipe by monsters and a "stick with the zerg" after a death to the guards.
- Chat, RC10 (the fixes for those): 49 minutes. A friend's answer was true ("in a grp in Pennine Mountains atm"), a solo
  bot in the player's zone answered /lfg within 14 seconds, and an invite from too far drew "im in snowdonia come find me
  and inv", true of where the bot was by then. 26 of 27 bot questions were answered within a minute. Reading the lines
  found nine more slips (a roleplayer's question losing its "?", a meeting point named as if it were a monster, "is Manawatch on
  today?" while the player was on, a scene's frontier newcomer who was grinding in Avalon, "which stable for master
  visur?", "grinding city of camelot", a bot asked after a group it had said it found, "pennine mountains? thats in
  albion"), all fixed.
- Chat, RC11: 41 minutes, the checker passed, 34 of 35 bot questions answered within a minute. "how do i get to cornwall
  from here?" drew "Cornwall is its own zone, a stable master can get u close", and a friend who'd grouped with the player
  asked "Manawatch, u around? want to group again". Two small slips (an /lfg offer asking the level the player had just
  posted, "city of camelot anyone") are fixed.
- Chat, RC12 (the build that ships but one small fix): 21 minutes, the checker passed, every bot question answered
  within a minute. The player's "50 lf xp grp" drew offers that no longer asked the level it gave, and nothing was said
  about hunting or grouping in a capital. One offer said "we keep going after the same mobs" from across the zone; that
  last fix is in the build that ships and is tests only.
- Not yet seen: a bot answering the player's own /trade post (no reply in three tries, RC7-RC11; bots answer a post a
  little under half the time).
- Seen without prompting on those builds: a guild invite and a recruiter in /say, a bot that grouped with the player
  remembering it and greeting them hours later ("look who it is, hey Manawatch"), a tell from a bot that really had
  grouped with the player, an answer in /faction to the player's question about a camp, and a solo bot answering the
  player's /lfg with an offer to group, then joining.
- Scurceol on RC3: one Albion raid of 200, started from the launcher, shot all four orbs from the floor in 10 minutes and
  killed him 50 seconds later; nobody died to him or his orbs. That raid also showed the long wait at the gathering town
  and Orshom's Fire, which are fixed since.
- Xanxicar on the 1.1.3 build before the chat fix: one raid of 280 killed him in 23 minutes with no wipe.
- The Darkness Falls exits, on RC2: both took an Albion character to Albion's usual exit.

**Tests only so far** (automated tests and simulated evenings, not yet seen on a real install): everything else above,
including the raid fixes after RC3, the keep defense, the porter and the stalls. Real clock ran during the RC6-RC12
checks, by day; a full quiet night hasn't been watched.
In a simulated three-hour evening, 94% of the bots' questions were answered within a minute, with no herald-style line,
no contradicting bot and /faction under its ceiling.

One raid or one evening is a good sign, not a promise. Please report what you see.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.3-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.3-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC) and
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). The Darkness Falls exits from rambodolphin13's fork. Reports
from the community on GitHub and r/daoc. Directed and play-tested by [chris4aker](https://github.com/chris4aker), coded
with Claude (Anthropic). GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
