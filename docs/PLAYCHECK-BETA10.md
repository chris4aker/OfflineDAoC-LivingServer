# Play check: the Beta 1.0 features never seen

One session of about two hours that covers every row of [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), "Beta 1.0
features never seen", plus the "A tale that starts by itself" row. It is ordered so the settings are changed once,
before the server starts, and GM is switched on once, near the end.

Written 2026-11-15 from the code of `integration/1.2` (`c58ea66`) plus the two fixes in the same PR (below). Nothing
here has been played yet: every row stays "Not seen" until its line is in a PC log of a candidate build.

**Character:** Testrunner, the non-GM level 22 Albion Fighter, on the test copy
(`C:\Games\OfflineDAoC\playable-v0.37b-fresh`). **Log:** `runtime\server\logs\server.log`. **Helper:**
`tools\claude-version\playcheck_beta10.py` (copy it into the test copy's `tools\claude-version\`, or run it from the repo
with `--root C:\Games\OfflineDAoC\playable-v0.37b-fresh`). `report` lists every row's proof lines at the end.

## What needs care

- **No `/lstest` hook exists for any of these rows** (only for tales). Every step runs the feature's own code; only step
  13's `/keep realm` stands in for something (a lord kill), and only if no capture comes by itself.
- **The tale must come first.** A realm's first tale of a session comes 25-45 minutes after you log in, but never sooner
  than 3 hours after the realm's last tale (a `/lstest tale`, a tale you asked for and a raid's tale all count, and the
  time is saved across restarts). So: no `/lstest tale`, no "tell the one about..." and no raid before step 9. `prep`
  says whether the last tale is too recent.
- **The automatic tale needs one of Albion's two storytellers online** (level 35+, named in `TALE_TELLER` lines). It
  does not use a stand-in (a raid's tale does), and when it can't start it tries again 10 minutes later without a log
  line. With the clock schedule a storyteller may be offline; if no `TALE_START` comes by 55 minutes, `/who` the names
  from the last `TALE_TELLER` lines.
- **You can't rez as a Fighter.** "You rezzing a dead bot" needs a rez class with a resurrection spell (step 15, another
  character). The dead bot's `LIVING_SERVER_REZ_ASK` line says `has_rez_spell=` for the nearest rez-class player, so
  the log tells you whether that character can.
- **Linkdeads are luck.** Each group with you rolls 1 in 90 every 30 seconds while nobody in it fights, at most once in
  45 minutes. Idle in a group, that is about 45 minutes on average (a 2-in-3 chance in 50 minutes, 7 in 8 in 90).
  Steps 3-9 keep you grouped and idle for that reason.
- **Nemesis lines need Albion bots killing and dying in RvR**, and a bot speaks only after the same enemy killed it
  twice (then 60% of the time, once in 20 minutes per bot). Your own kills don't count for this row: the victim speaks
  in its own realm's /faction, which you can't hear. The battleground (step 10) is the fastest place.
- **Keep claims need a capture with at least 4 members of one Albion bot guild inside a tower (8 inside a keep)**, no
  Albion player inside, and a guild under `guilds_claim_limit` (1). Claim attempts run every 20 s for 160 s after the
  capture. A natural capture is best; step 13 speeds one up with GM.
- **The between-pulls wait row may be moot.** Since 2026-10-27 group formation takes bots mid-fight
  (`AutonomousGroupMatchmakingPause`), so the code itself calls the pause "mostly for show". The step counts groups per
  hour so the owner can compare with an older log and decide whether the row stays.

## Fixed for this check (tests only)

- **A passing bot rezzing you never happened in RvR.** A bot that has run an RvR goal once keeps its force id
  (`rvr-<id>`) for the rest of the session, and the rez rule read any force id as "busy". So no bot that had ever been to
  a battleground or the frontier would rez a stranger; the schedule had the same mistake, fixed in 1.1.3. Now only a
  force in a running realm event counts (`BotBrain.FreeToRezStrangers`, `UT_RezStrangersAfterRvr`).
- **A bot guild at its claim limit blocked every other guild.** The claimant was the guild with the most members inside,
  even one already holding its one keep; it then failed the limit and nobody claimed. Such a guild is now passed over
  (`AutonomousBotGuildKeeps.ChooseClaimant`, `ChooseClaimant_AGuildAtItsClaimLimitDoesNotBlockTheNext`).

Both rows need a build with these fixes to get a fair try.

## Setup (once, server stopped)

1. **Back up and prepare the test copy.**
   - `python tools\claude-version\playcheck_beta10.py prep` lists the settings below and the last tale's age. If it says
     TOO RECENT, play 3 hours later, or (test copy only) delete the rows `tale:last:1`, `tale:last:2`, `tale:last:3` from
     `offline_lore_memory`.
   - `python tools\claude-version\playcheck_beta10.py prep --apply` backs up the database to
     `runtime\server\deployment-backups\playcheck-beta10-<time>\` and sets `max_camp_bonus` and
     `max_dungeon_camp_bonus` to 0, for the XP measurement (row 7). It changes nothing else. `restore --apply` puts them
     back after the session.
2. **Launcher, Living Server tab** (then SAVE SETTINGS):
   - Bot guilds on, with Keep claims on.
   - Rezzes and corpse runs, Nemeses, Breaks and linkdeads, Body language: on. Bots remember you and grats are on by
     default.
   - Bot schedule: Real clock.
   - XP by level range, AUTONOMOUS BOTS: Suggested (1.5x for 1-19, 2.5x for 20-34, 6x for 35-50).
   - Bot voices and tales stay on (the defaults).
3. **Bots.** Press **+ Lv.1** for 30 new bots (their first kill is the exact XP check for 1-19), and **+ Lv.20** for 30
   or more per realm if the battleground has few (step 10). The world needs level 50s in the frontier for sieges
   (step 13) and at least 8 bots of level 5+ per realm for bot guilds.
4. **When to play.** The clock row shows best in a falling hour: start about 21:30 on a weekday (the curve drops from
   0.90 of the roster at 22:00 to 0.66 at 23:00), when bots log off on schedule. In a rising hour you see logins
   following the curve instead; both count, the falling hour is clearer.

Options tab: GM off (Testrunner is a normal player until step 12).

## The session

Time is minutes after you log in. The grep recipes are PowerShell in the game folder (`$log = "runtime\server\logs\server.log"`).

### 0-5: Start

1. **Start the server and log in Testrunner in Camelot.**
   - Look for: nothing special yet.
   - Log: `Select-String $log -Pattern 'FORK_BUILD'` must show `bot_guilds=on bot_guild_parts=...claims...`,
     `bot_xp_rate_by_level="1-19:1.5;20-34:2.5;35-50:6"`, `living=...death,...nemesis,breaks,body...` and
     `bot_schedule=clock`. Then `Select-String $log -Pattern 'LIVING_SESSION_START realm=Albion'`: note its time, the
     tale (step 9) is due 25-45 minutes after it. `Bot guilds: founded` lines come about 90 s after the start.

### 5-15: Your guild (row 2)

2. **`/gc invite` and `/gc remove` on a bot.**
   - Setup: `/gc quit` (you leave Clan Cotswold), walk to a Guild Registrar, `/gc form Playcheck` (costs 1 gold; alone,
     the offline server forms it at once). If `/gc form` says you must be near a registrar, you are not.
   - Actions: target a gamebot of Albion, level 12-32 (10 levels of yours), out of combat, that is not a leader or
     officer of a bot guild, and type `/gc invite` (or `/gc invite <Name>` from anywhere). The server's command is
     `/gc invite`; use `/ginvite` only if your client passes it on. Then `/gc remove <Name>`; it needs the name, a
     target alone shows the help.
   - In game: "<Name> has joined the guild!" and a hello from it in guild chat; a member of a bot guild first says "sure,
     im in. sorry <guild>, see you around". An officer, a fighter or a bot too far from your level refuses in /say (or a
     /send when far): pick another. On remove: "Testrunner removed <Name> from the guild."
   - Log: `Select-String $log -Pattern 'BOT_GUILD_JOIN','BOT_GUILD_REMOVE'`:
     `BOT_GUILD_JOIN bot=<Name> id=<id> guild="Playcheck" invitedBy=Testrunner` and
     `BOT_GUILD_REMOVE bot=<Name> id=<id> guild="Playcheck" by=Testrunner`.

### 15-55: Camelot, grouped and idle (rows 3, 7, 8, 10, and the tale)

3. **Group with two gamebots** of level 22 to 30 (`/invite <Name>`, or accept a bot leader's invite after `/lfg`). They
   must be gamebots, not `/spawn` companions. Stay out of fights from here to step 9: the linkdead roll needs it.
4. **Exact XP rate, 20-34 band (row 7).** Before idling, take the group out to one camp of level 22-26 monsters and kill
   five, one at a time, with only your group hitting them. Watch the highest-level member of the group (a lower-level
   member gets a share by another rule that the script doesn't model; all at 22 is simplest). In a second window:
   `python tools\claude-version\playcheck_beta10.py watch-xp --bot <that groupmate's name> --group 3`.
   - Each kill prints the bot's experience gained and `EXACT with the factor 2.5: a level [..] monster, N in the group`
     when it is exactly what the code pays at 2.5x (its share of the monster's value, capped at 125% of its level's,
     times 2.5, plus 12.5% per other member unscaled), or `factor NOT applied` when it matches 1x. "no single kill"
     means two kills landed in one save or something else added (see the script's text): take the next kill.
   - Pass: at least three kills EXACT and none at 1x. It reads the bot's saved experience every second
     (`offline_world_bots.Experience`, saved about a second after each kill), read-only. Then walk back to Camelot.
5. **Body language: steps in town (row 8).** Stand by the bank or the merchants in Camelot, where bots on a town break
   stand (`AUTONOMOUS_TOWN_IDLE_STARTED bot=... town=...` in the log names them). Stay within sight of them for 10
   minutes.
   - In game: a bot on a break takes a step or two (25-60 units, at a walk) now and then, besides looking around and
     emoting; one that sees you walk up turns and waves.
   - Log: `Select-String $log -Pattern 'BOT_BODY_LANGUAGE'`, one line every 5 minutes with something in it:
     `BOT_BODY_LANGUAGE minutes=5 looks=.. emotes=.. steps=N greets=.. cheers=..`. Pass: `steps` above 0 while you stood
     there (steps happen only in a town break and only where a player can see).
6. **The clock schedule (row 3).** While you wait, now and then compare the launcher's bots online with the curve:
   `report` prints "now: X of Y bots online; the clock wants about ..." from the database.
   - In game: in a falling hour, bots near you say a goodbye line in /say or guild and vanish; in a rising hour new names
     arrive and some greet in /say or guild ("back"/logon lines come after the first 10 minutes only).
   - Log: `Select-String $log -Pattern 'LIVING_SERVER_LOGOUT.*reason=logoff mode=Clock'` (falling hour) and
     `Select-String $log -Pattern 'entered the live world from the staggered queue'` (rising hour; most are the startup
     ramp, the ones after it follow the curve). `reason=brb mode=Clock` lines are breaks, not the schedule.
   - Pass: the online count is within about 10% above the curve's share of the roster after the ramp, and it follows
     the curve as the hour turns (logoffs in a falling hour). No-lifers play more, so it sits a little above.
7. **Linkdeads (row 10).** Nothing to do: keep the group out of fights.
   - In game: a groupmate stops and stands still for 30-90 s; another says in /group that it went linkdead; it comes
     back with a line of its own. Any fight in the group "reconnects" it at once.
   - Log: `Select-String $log -Pattern 'LIVING_SERVER_LINKDEAD'` (`LIVING_SERVER_LINKDEAD bot="<Name>" id=<id> seconds=<30-90>`)
     and the lines: `Select-String $log -Pattern 'CHAT_LINE.*ch=group.*via=AutonomousBotSchedule\.'`
     (`via=AutonomousBotSchedule.RollLinkdead~` for "gone", `via=AutonomousBotSchedule.Unfreeze~` for "back").
   - Not in 90 minutes grouped and idle: report it as not seen, not as broken (the odds are about 1 in 8).
8. **Grats on your ding (row 6), if you are close to 23.** A natural ding here is best: groupmates say grats in /group
   and bots standing by in /say (0-2 of them, so it may take two dings), and 1-3 of them turn to you and cheer. Otherwise
   step 12 does it.
9. **The tale that starts by itself (25-45 minutes after `LIVING_SESSION_START`).** Keep /faction open.
   - In game: a bot asks for a tale in /faction, others hush the channel, and the realm's storyteller tells it in about
     a dozen lines; 45-90 s after it ends a rumour follows (sometimes a raid really forms).
   - Log: `Select-String $log -Pattern 'TALE_START realm=Albion','TALE_END realm=Albion','TALE_RUMOUR realm=Albion'`:
     `TALE_START realm=Albion scene=tale_<boss> teller="<Name>" asked_by="a bot"` 25-45 min after the session start,
     `TALE_END ... finished=True lines=<n>`, then `TALE_RUMOUR realm=Albion about=<boss> kind=lore|raid|none`. A raid's
     own tale has no `TALE_RUMOUR` (its raid is called instead), so the rumour line is what shows it was away from a raid.
   - Pass: `asked_by="a bot"`, a `TALE_RUMOUR` after it, and no `/lstest` or request from you before it.

### 55-95: Thidranki (rows 4, 5)

10. **The battleground for your level.** Leave the group (`/disband`): bots in your group never rez you as strangers.
    Take the battleground portal for levels 20-24 (Thidranki) and fight with the Albion bots there.
    - **Nemesis lines (row 4).** Watch /faction.
      In game: an Albion bot calls out the enemy who killed it twice, by race and class ("that thane got me again at
      ..."), and one or two others commiserate; when it kills that enemy back it says so ("finally got that ...").
      Log: `Select-String $log -Pattern 'CHAT_LINE.*src=nemesis','CHAT_LINE.*src=revenge'`:
      `CHAT_LINE who=bot ch=faction realm=Albion from="<Name>" via=AutonomousLivingServer.RecordKill~ src=nemesis ...`
      and `via=AutonomousLivingServer.OnBotKilled~ src=revenge`. The rows behind them are in `offline_bot_nemesis`
      (`Kills` 2 or more).
11. **A passing bot rezzes you (row 5).** Die in the battleground near Albion healers (clerics, friars) who are not in a
    group with you, away from the guards and monsters. Don't release.
    - In game: a healer further than 250 units says it is coming, walks over, says a rez line in /say, and the
      resurrect box opens.
    - Log: `Select-String $log -Pattern 'LIVING_SERVER_REZ '` (with the space):
      `LIVING_SERVER_REZ bot="<Name>" corpse="Testrunner" distance=<d>`. When none comes,
      `Select-String $log -Pattern 'LIVING_SERVER_REZ_SKIP.*corpse="Testrunner"'` says why for each healer nearby
      (`reason=busy`, `in_combat`, `hostiles_near`, `no_rez_spell`, `in_player_group`, ...); `busy` from every healer
      means the build lacks the fix above.

### 95-120: GM (rows 6, 8, 1)

12. **GM on** (launcher Options tab; log in again if it asks). In Camelot, among idle bots, if step 8 did not happen:
    `/player levelup` (a free level), then again after half a minute, up to three times. On the test copy only: it keeps
    the levels (`/player level 22` puts Testrunner back, with a reset).
    - In game: grats in /say from 0-2 bots standing by (in /group from groupmates if grouped), and 1-3 bots turning to
      you with a cheer, clap or victory emote.
    - Log: `Select-String $log -Pattern 'CHAT_LINE.*via=AutonomousLivingServer\.Grats~'` (`ch=say` from bots nearby,
      `ch=group` from groupmates; the text names you), and the next `BOT_BODY_LANGUAGE` line with `cheers=` above 0.
      A GM's levels still get grats (only server firsts skip GMs).
13. **Bot guilds claiming keeps (row 1).** First check whether it already happened:
    `Select-String $log -Pattern 'BOT_GUILD_CLAIM'`
    (`BOT_GUILD_CLAIM keep="<name>" guild="<guild>" realm=Albion present=<n>`; on screen the claim message and the
    guild's emblem on the banners; the guild's chat says it when you are in that guild). If not:
    - Watch the launcher's Realm Events tab for an Albion siege of a tower. Go there (GM travel), and count the bots
      inside the tower's walls whose name tags show the same Albion bot guild: you need 4 of one guild (8 in a keep).
    - Stand outside the tower, more than 1,500 units from its centre (3,000 for a keep) and within 10,000, and type
      `/keep realm 1`. The tower becomes Albion's at once, through the same capture code a lord kill runs, and the claim is
      tried every 20 s for 160 s. You inside the area would block it (a real player of the realm gets the first chance).
    - Pass: a `BOT_GUILD_CLAIM` line. Say in the notes whether the capture was natural or `/keep realm`; the owner
      decides whether the GM capture closes the row.
    - No line although enough members stood inside: the claim also needs the keep's base level 50 (frontier keeps and
      towers; battleground keeps only with `allow_bg_claim`) and the guild under its limit; there is no log line for a
      refused claim.
14. **Groups per hour (row 9).** `report` prints `AUTONOMOUS_GROUP_FORMED` per hour by objective
    (`objective=GroupPve`). Compare GroupPve per hour, divided by the bots online, with an older PC log (a 1.1.x one,
    before the wait). The row asks for a rise; a flat count is "not seen", and see "What needs care" above.

### Optional: you rezzing a dead bot (row 5, second half)

15. **Another character with a rez spell** (a Cleric or Friar of Albion that has its resurrection, on the same account;
    log out to character select, no setting changes). Group with a gamebot or stand by bots fighting, and wait for one to
    die near you (2,500 units). Don't fight next to it.
    - In game: the dead bot asks you for a rez (/group, /say, or a /send when farther); rez it; it thanks you.
    - Log: `Select-String $log -Pattern 'LIVING_SERVER_REZ_ASK'`:
      `LIVING_SERVER_REZ_ASK bot="<Name>" rezzer="<you>" ...`. When it says `rezzer="none" near_rez_class="<you>" ...
      has_rez_spell=False`, that character can't rez yet. After the rez:
      `Select-String $log -Pattern 'CHAT_LINE.*via=AutonomousLivingServer\.OnBotResurrected~'`.

## After the session

16. Close the game and server. `python tools\claude-version\playcheck_beta10.py report` (add `--player <name>` for the
    rez character too) prints each row's lines and counts, and the XP check:
    - **Row 7, 1-19 band:** "new bots' first kills": a new bot's first experience is one kill, nearly always alone. With
      the camp bonus at 0 it can only be 7, 15 or 18 at 1.5x (5, 10 or 12 without the factor). Pass: most are "exactly a
      solo kill with the factor", none "without it".
    - Level-up kills (`AUTONOMOUS_XP_PROGRESS ... amount=.. level=L->L+1`) are mostly group kills or blues; the few that
      sit exactly at the cap back the factor up. The 35-50 band has no exact check in this session (Testrunner is 22):
      level-ups at 35-50 that are exact at 6x, if any, are the only evidence. The three bands are one lookup in the code
      (`LevelRangeXpRates.BotRate`).
    - `AUTONOMOUS_XP_PROGRESS bot="<Name>" id=<id> class="<class>" amount=<gained> xp=<before>-><after> level=<L>-><L'>`
      is logged only for a bot's first experience and for a kill that gives a level.
17. `python tools\claude-version\playcheck_beta10.py restore --apply` puts the camp bonus back. Record each row in
    PLAYED-CHECK-1.2.md with the build's commit (from `FORK_BUILD`) and the log time.

## Rows and their proof

| Row | Step | Proof in the log |
|---|---|---|
| Bot guilds claiming keeps | 13 | `BOT_GUILD_CLAIM keep="..." guild="..." realm=Albion present=n` |
| `/ginvite` and `/gc remove` on a bot | 2 | `BOT_GUILD_JOIN ... invitedBy=Testrunner`, `BOT_GUILD_REMOVE ... by=Testrunner` |
| The `clock` bot schedule | 1, 6 | `FORK_BUILD ... bot_schedule=clock`; `LIVING_SERVER_LOGOUT ... reason=logoff mode=Clock`; online count against the curve |
| Nemesis lines in chat | 10 | `CHAT_LINE ... ch=faction ... src=nemesis` (and `src=revenge`) |
| You rezzing a dead bot / a passing bot rezzing you | 15 / 11 | `LIVING_SERVER_REZ_ASK ... rezzer="<you>"` and `via=AutonomousLivingServer.OnBotResurrected~` / `LIVING_SERVER_REZ bot="..." corpse="Testrunner"` |
| Grats on your own ding | 8 or 12 | `CHAT_LINE ... ch=say ... via=AutonomousLivingServer.Grats~ ... Testrunner` |
| The bot XP-by-level factor at its exact rate | 4, 16 | `watch-xp` EXACT lines (20-34); `report` first kills (1-19) |
| Body language: steps and cheers | 5, 8 or 12 | `BOT_BODY_LANGUAGE ... steps=n` and `cheers=n` above 0 |
| The between-pulls wait raises bot groups | 14 | `AUTONOMOUS_GROUP_FORMED ... objective=GroupPve` per hour, against an older log |
| Linkdeads | 7 | `LIVING_SERVER_LINKDEAD bot="..." seconds=n` |
| A tale that starts by itself | 9 | `TALE_START realm=Albion ... asked_by="a bot"` 25-45 min after `LIVING_SESSION_START`, then `TALE_RUMOUR` |
