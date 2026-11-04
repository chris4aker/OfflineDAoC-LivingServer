# Questing complete, and the quest journal

Owner: Chris. Written 2026-10-30 from a file-by-file map of the quest code on `main` (`e57d2d8`), the base
changelog, and GitHub as of today. **Verified** means checked in the code or on GitHub today; **inference** is this
file's reasoning; **proposed** is a choice for the owner. Nothing here is built. The quest content itself (the
`DataQuest` rows, `classic-quests.json`, the guides, the reward items and shadowofze's generator and audit scripts)
is **not in the repository**; it ships only inside the game, so anything that needs those rows says **[needs the
world]**.

**Update 2026-11-04 (Living Server 1.2):** the quest harness exists (section 6), 1.1.2 and 1.2 fixed most of the
defects below, and section 7 has the status of each of the fourteen. Sections 1 to 5 are kept as written.

## 1. Why quests can feel broken on 1.1 (verified in the code, not yet seen in game)

The base game's quest work is large and recent: 0.35 added the 448 classic quests (1,302 class versions), red map
markers and the Quest Guide; 0.36 added the 940 reward items and declared "every classic quest can be finished";
0.37 made every quest startable and refreshed markers at level-up (all in `CHANGELOG.md`). On top of that, 1.1
ported HearthDAoC's quest changes **without playing them** (`CHANGELOG-LIVING-SERVER.md:391`). The map found these
defects in the code as it is on `main`. The first four fit "feels broken" for a solo player at high XP rates:

| # | Defect | Where | Why it bites you |
|---|---|---|---|
| 1 | **A kill by your bot group gives you no quest credit unless you damaged the monster yourself.** Kill credit comes from player attackers and from NPC attackers' player owners (`BotBrain.GetPlayerOwner` is `BotBody?.Owner`); an autonomous gamebot in your group has a group leader, not an owner, so a monster your bots killed before you touched it, or while you healed, credits nobody; the classic kill drops go only to the direct killer or a pet's owner | `BotBrain.cs:113-116`, `GameLiving.ProcessDeath:2033-2111`, `ClassicQuests.DropForSteps:366-389` | You play solo with bots; a healer or caster who lets the bots pull loses kill steps. Companion (`/spawn`) bots do credit you |
| 2 | **Hand-ins are refused once you are above the quest's maximum level**; kill, interact and whisper steps have no such check | `DataQuest.cs:2624-2625` | At 10× XP you outlevel a quest between taking it and finishing it |
| 3 | **Quest XP ignores XP-by-level-range** (data quests, bounties and the Atlas dailies pay through `ForceGainExperience`, which skips every rate) | `GamePlayer.cs:4278-4319`, `BountyRewardService.cs:57-58` | Quests feel worthless next to grinding at 10× |
| 4 | **The epic chains were never linked.** `epic_chains.py --apply` has not been run on a clean 0.37b world and Setup doesn't run it, so `/epic` says "no epic chain linked by quest IDs"; the level-50 scripted epics don't check the 1–48 chain (the check is commented out in all ten) | `tools/claude-version/epic_chains.py`, `install-server-build.ps1:224-275`, `Albion/epic/Shadows50.cs:1877` | Your story-rich 1–50 paths have no spine |
| 5 | Repeatable data quests are never offered again (`IsDoingQuest()` is true for a finished one) | `DataQuest.cs:1052-1058` | **[needs the world]** for how many rows repeat |
| 6 | Group kills can count more than once per kill (the killer's quests are notified again for each groupmate in range) | `DataQuest.cs:2011-2020, 2931-2962`, `GamePlayer.cs:6091-6109` | Steps skip |
| 7 | A delivery quest with no `CollectItemTemplate` can never be handed in | `DataQuest.cs:2621` | **[needs the world]** |
| 8 | Journal step text is cut at 255 characters; GMs see debug tails | `PacketLib1124.cs:572-579`, `DataQuest.cs:1255-1284` | Long steps read as broken; test as a normal account |
| 9 | A whisper within 1.5 s of the last is dropped, so quick [keyword] clicks do nothing | `GameLiving.cs:3119-3124` | Conversations feel stuck |
| 10 | The 25-quest limit isn't enforced at login; older saves hide the extras | `GamePlayer.HandleQuests:10542-10588` | Quests "vanish" |
| 11 | The new client draws marker value 16 as orange "in progress elsewhere" while the server sends 16 to mean "this NPC is your current step" | `quest_markers.gd:134`, `QuestIndicators.cs:130-136` | Wrong knots on the client you play |
| 12 | Bot parties hunt named dungeon bosses that are real quest kill targets (only bosses in `QuestMonsterIds` are spared); the Bounty pool doesn't filter quest monsters although a comment says it does | `AutonomousDungeonBossGoals.cs`, `BountyTargetCatalog.cs:254-276` | Your target is dead when you arrive (softened by the 300 s respawn on 162 targets) |
| 13 | `ClassicQuests` spawns, hands items and advances quests from a thread-pool timer, not the game loop | `ClassicQuests.cs:183, 704-856` | Race conditions that show up as "it just didn't count" |
| 14 | Removing Living Server with the chains applied breaks the Guild of Shadows (stock reads `#id` dependencies as names); `ROLLBACK.cmd` doesn't run `epic_chains.py rollback` | `QuestDependencies.cs`, `ROLLBACK.cmd` | A trap for anyone who rolls back |

Also verified (re-checked 2026-10-31 against his final `main`, `b0b855d`): upstream 0.38 touches no quest logic beyond the HearthDAoC name matching 1.1 already has, a tasks rework and a world script that gives 68 classic quest rewards their 1.65 stats; the 1.1 port's unit tests cover rule helpers only (nothing drives
`AdvanceQuestStep`, `FinishQuest`, hand-ins, kills or the classic custom steps); the only end-to-end quest harness
is the new client's `tests/quest_driver.gd`, whose `plan.json` and results aren't in the repo.

**First thing to do, before any plan:** play three quests on the test copy as a normal account (a kill quest in
a bot group, a delivery, and one of your class's epic steps), on 1.1 and then on stock 0.37b via `ROLLBACK.cmd`.
That tells us in an hour whether the 1.1 port regressed shadowofze's work or whether the defects above are the
whole story.

## 2. "Full questing complete and working": the program (proposed)

1. **Fix the fourteen** above in order of bite (1–4 first). All are code except 5 and 7, which need the rows.
2. **A server-side quest harness** in the chat-simulator pattern: a test that instantiates a `DataQuest` from a
   row for a fake player and drives every step type (interact, kill by the player, kill by a groupmate, kill by a
   gamebot in the group, deliver, collect, whisper, search, the classic custom steps) to the finish, asserting
   rewards and markers. Then run it over **all 448 rows** from a copy of the world (on your PC, or a world file
   given to CI as a secret-free artifact), so every quest is machine-walked on every build. This is the lever:
   it turns "feel broken" into a list.
3. **A quest audit script** (`tools/claude-version/quest_audit.py`, the recipe-check pattern): for every row,
   does the giver exist in its region, do the targets and collect items exist, is `CollectItemTemplate` set where
   a step delivers, do rewards resolve to items, do dependencies resolve, is the Quest Guide page present.
   Output: a broken list by realm and level, shared with shadowofze (he fixed the last list within a release).
4. **Link the chains:** Setup runs `epic_chains.py --apply` on install (and `rollback` on uninstall), the ten
   level-50 epics check their 1–48 chain, and dialogue for the eleven lines that only have pins (Albion's other
   three, all of Midgard's and Hibernia's), realm-equal.
5. **Quest XP follows XP-by-level** (decision below) so quests pay at your pace.
6. **Bots and quest targets:** bot parties leave real quest kill targets alone while a player is on that step, or
   the target spawns for the player as the 0.36 "28 targets" already do.
7. **Play coverage:** your nine characters' epic lines plus a sampled trainer and optional quest per level band
   per realm; community reports (they are arriving); the Quest Guide checked against each chain.
8. **Honesty gate:** "complete and working" is claimed only when the harness is green over all rows and the
   epic lines you play are finished end to end.

## 3. The journal: a WoW-style tracker and a book-page log

**On the new client (0.37c, Godot): yes, both, and it is the right place.** The client already receives each
journal line as `{name, text, goals}` and raises `PlayerData.quests_changed` (`net/server.gd:622-627`;
`journal_window.gd` is 169 lines). Bounties already carry structured goals with counts and waypoints. So:
- **An objective tracker** is a HUD panel that lists tracked quests with their current step text, updates on
  `quests_changed`, collapses per quest, and opens the journal on click. One Godot scene plus a "track" toggle
  in the journal.
- **A book-page journal** is a theme: parchment textures, serif font, two-page layout with the quest list on the
  left and the step and guide text on the right (the Quest Guide text can live on the right page instead of
  behind a button).
- **What the server should add for both clients:** progress counts in the step text ("Kill arawnite messengers
  (3/5)"), since the kill and collect counts exist server-side (`CharacterXDataQuest.Count`) but aren't sent; and
  a richer text path for the new client so the 255-character cut applies only to the old client.
- **Ownership:** the new client is shadowofze's; the project has already sent him client changes (the AgX tone
  mapping, the target buttons). Build it on a branch, send it upstream, carry it in the fork meanwhile.

**On the original client: the book page yes, the tracker no (as far as the code shows).** The classic client's
windows are XML plus TGA in `ui/<skin>/` (verified: the pipeline reads `ui/isles/realmwar_alb.xml`), and the
client lets a player choose a custom skin. A custom skin can restyle the quest journal as a parchment book
(textures, fonts, layout, a compact frameless variant you can leave open in a corner). It cannot add a new data
binding, so an auto-updating objective list needs a feature the 2003 client doesn't have; the closest is the
compact journal left open plus the server's step-update messages. **Not verified:** which skin folder this exact
client build reads and whether the journal window keeps its open state across zoning. One look in the game's
`ui\` folder settles it.

## 4. Where this sits in the train

The stops are in [RELEASE-TRAIN.md](RELEASE-TRAIN.md): the code-only defects and the three-quest A/B in 1.1.1,
the harness, the audit and the linked chains in 1.2, dialogue for the other lines and the new-client journal in 1.3,
"complete" certified in 2.0.

## 5. Decisions

1. Decided 2026-10-30: the train (see RELEASE-TRAIN.md).
2. Quest XP follows XP-by-level-range (proposed), or stays flat.
3. Hand-ins allowed above a quest's maximum level (proposed), or the classic refusal.
4. Gamebot kills credit the group's player for quest steps (proposed), as companion bots already do.
5. The tracker and book journal on the new client, sent upstream (proposed); a custom skin for the old client as
   a smaller follow-up.
6. Which three quests to A/B first, and your two or three examples of what felt broken.

## 6. The quest harness (Living Server 1.2)

Today's playtest couldn't test quests at all through remote control, so quests are tested by the harness from now on.
It plays a quest row from offer to finish **on the server's own quest code**: a real `GamePlayer` (its own XP, money,
inventory and quest lists) on a fake connection, real `GameNPC`s, and every action entering where the game sends it
(an interact through `GameNPC.Interact`, a whisper through `GamePlayer.Whisper`, a hand-in as the move-item packet
handler notifies it, a kill through the credit rule `GameLiving.ProcessDeath` uses, now `QuestKillCredit.PlayersCredited`,
then `GamePlayer.EnemyKilled`, a `/search` where its timer ends, the classic custom steps through `ClassicQuests`' own
event handlers and its refresh). Only the world around them is a stand-in: no regions, no network, and an in-memory
database holding the quest rows and item templates it is given, which never writes anything.

Code: `source/server/Tests/UnitTests/QuestHarness/` (`QuestHarness.cs` the session, `QuestWalker.cs` the walk,
`QuestWorldReport.cs` the world report).

**What it drives:** interact, kill by the player, kill by a groupmate (another real player), kill by an autonomous
gamebot invited into the group, kills with a drop chance, deliver (the item the quest hands), collect (`id;N` counts),
whisper, search, every classic custom step (`travel`, `use_item`, `has_item`, `interact`, `group`, `trade` with kill
drops, `choose`, `say`, `die_to`, `quest`), the start types (standard, auto-start, reward quest, search start,
collection, interact-complete, kill-complete), and two variations: kills made by a gamebot in the group, and the player
outlevelling the quest before the hand-ins (10x XP). **What it asserts:** the quest finishes, the XP paid (with the
XP-by-level rate and the one-level cap), money, items received and final rewards, the NPC quest markers (yellow before
the offer, white on the step's NPC, blue where it finishes), the classic red map markers, that nothing is logged as an
error, and that no world row is written. A quest that stops says where and why, with what the server logged.

**The unit tests** (`UT_QuestHarness`, synthetic rows, in every CI run):

```
dotnet test source/server/Tests/Tests.csproj -c Release --filter "FullyQualifiedName~UT_QuestHarness"
```

### The world report on your PC

The real rows ship inside the game, so the world report runs where a game is. It walks every quest row of a world
database (by the player alone; again with the kills made by a gamebot in the group when the quest has kill steps; again
outlevelled before the hand-ins when it has hand-in steps), checks each row against the world (its giver and step
targets stand in their region, its items are item templates, its dependencies name quests), and writes a Markdown report
of which quests can't be completed and why.

**It never writes to your world.** It copies the database (and its `-wal` file) into a folder of its own under
`%TEMP%`, opens the copy read-only, deletes it afterwards, and checks that the original's SHA-256 is unchanged. Close
the game first anyway, so the copy is a consistent one.

You need the .NET 10 SDK (see [DEVELOPMENT.md](DEVELOPMENT.md)) and a checkout of this repository. In PowerShell, from
the checkout:

```powershell
copy source\server\CoreServer\config\serverconfig.example.xml source\server\CoreServer\config\serverconfig.xml   # once
$env:QUEST_HARNESS_WORLD = "C:\Games\OfflineDAoC\playable-v0.37b\runtime\data\opendaoc.sqlite3.db"
$env:QUEST_HARNESS_REPORT = "$env:USERPROFILE\Desktop\quest-report.md"
dotnet test source\server\Tests\Tests.csproj -c Release --filter "FullyQualifiedName~UT_QuestHarnessWorld.TheWorldsQuestRows" --logger "console;verbosity=normal"
```

- `classic-quests.json` is read from beside the world's server (`runtime\server\classic-quests.json`); set
  `QUEST_HARNESS_CLASSIC` to use another one.
- `QUEST_HARNESS_ONLY=20188,20157` walks only those quest IDs.
- Without `QUEST_HARNESS_REPORT` the report goes to `%TEMP%\quest-harness-report.md`; the test output names it.
- Without `QUEST_HARNESS_WORLD` the test is skipped (that is what CI does).

The report lists: how many rows were walked and finished; the reasons quests can't be completed, grouped; how many rows
have each `MaxCount` (the open question of defect 5); a table of every quest that can't be completed (ID, name, start
type, levels, classes, the step it stopped at, why); the world problems per quest; and every walk step by step.

**Reading it honestly:** a walk is automated on the server's code with a fake player. It proves the rows and the code
fit together; it doesn't prove pathing to the NPC, the client's windows, or that a monster is reachable. A quest that
the report calls finished still needs to be played to be called working (section 2, item 8).

## 7. Status of the fourteen defects (2026-11-04)

"Harness case" names the test that walks it. **Automated tests only; none of this has been played in game.**

| # | Defect | Status | Where, and the harness case |
|---|---|---|---|
| 1 | A kill by your bot group gives no quest credit | **Fixed in 1.1.2** (`QuestKillCredit.GroupLeaderFor`). 1.2 moved `ProcessDeath`'s credit rule into `QuestKillCredit.PlayersCredited` unchanged, so the harness walks the same code | `UT_QuestHarness.AKillStepCountsWhoeverInThePlayersGroupMakesTheKill` (player, groupmate, gamebot) |
| 2 | Hand-ins refused above the quest's maximum level | **Fixed in 1.1.2** | `AQuestOutlevelledAfterTakingItCanStillBeHandedIn`; the world report walks every hand-in quest outlevelled |
| 3 | Quest XP ignores XP-by-level | **Fixed in 1.1.2** (`quest_xp_by_level`) | `QuestXpFollowsTheXpByLevelRate`, `AtTwelveTimesAQuestPaysAtMostOneLevel` |
| 4 | The epic chains were never linked | **Fixed in 1.2**: the server links them as it reads the quest rows (`EpicChainLinks`, epic_chains.py's plan ported line for line, same data file built into GameServer.dll), in memory; `/epic` shows the chain on a clean install. Setting `epic_chains_linked` (on). **Not done:** the level-50 scripted epics still don't check the 1-48 chain (owner's choice: turning it on locks out anyone who skipped the chain), and the dialogue for the eleven lines that only have pins | `UT_EpicChainLinks` (the Python tool's own test world: same counts and values, every Guild of Shadows class walks its chain with `QuestDependencies`' rule, `/epic`'s `ChainFor` finds it); checked here against epic_chains.py on that world and on one where all 35 dialogue rows apply: identical output |
| 5 | Repeatable data quests never offered again | **Fixed in 1.2 for MaxCount 2 and more** (`QuestRepeats`). MaxCount 0 ("no limit" in the row format) and 1 stay offered once, as every quest was: **[needs the world]** whether the classic rows use 0 for one-time quests; the world report counts rows per MaxCount | `ARepeatableQuestIsOfferedAgainAfterItIsFinished` (failed before the fix), `AQuestWithNoCountLimitIsStillOfferedOnce` |
| 6 | Group kills counted more than once | **Fixed in 1.1.2** (`QuestKillCredit.IsRepeat`) | `OneKillAdvancesOneKillStepWithIdleGroupmatesInRange` |
| 7 | A delivery quest with no CollectItemTemplate can't be handed in | **Fixed in 1.2** (`DataQuest.OnPlayerGiveItem` no longer returns early). **[needs the world]** for how many rows: the world report lists them | `ADeliveryRowWithNoCollectItemTemplateCanBeHandedIn` (failed before the fix) |
| 8 | Journal text cut at 255 characters; GM debug tails | **Open.** The cut is the old client's packet limit; the richer text path for the new client is 1.3 (section 3). Test quests as a normal account | — |
| 9 | Quick whispers dropped | **Fixed in 1.1.2** (`WhisperThrottle`) | `UT_WhisperThrottle`; the harness's whisper steps (`WhisperStepsAdvanceOnTheirKeywords`) go through the same `WhisperReceive` |
| 10 | 25-quest limit at login | **Fixed in 1.1.2** | `UT_QuestJournal` |
| 11 | New client draws marker 16 as orange | **Fixed in 1.1.1** (the installer patches `quest_markers.gd`) | the client side isn't in the harness; the synthetic walks assert the server's marker meanings (`AnInteractQuestIsFinishedWithItsRewardsAndMarkers`) |
| 12 | Bot parties hunt named quest bosses; the Bounty pool doesn't filter quest monsters | **Open.** Needs a rule for "a player is on that step" (section 2, item 6); the bounty filter is a one-line change but has no test here yet | — |
| 13 | `ClassicQuests` works from a thread-pool timer | **Fixed in 1.2**: the timer only reads the file and asks the game loop to run the refresh, once however many ticks pass first (`ClassicQuests.OnRefreshTimer`, `RefreshPlayer`) | `TheClassicRefreshTimerOnlyQueuesTheRefreshForTheGameLoop` (new seam: it can't fail on the old code, which had no such method) |
| 14 | Removing Living Server with the chains applied breaks the Guild of Shadows | **Fixed in 1.2**: nothing is written to the world for the chains any more, so ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd have nothing to undo. A world `epic_chains.py --apply` wrote them into (1.1) is noted beside the server (`runtime\server\epic-chains-in-world.txt`, by the tool and by the server at start), and both refuse on it until `epic_chains.py rollback --apply` put the rows back (`-Force` goes ahead anyway) | `test-install-scripts.ps1` "epic chains" checks (run here under PowerShell 7 against fake game folders: all installer checks pass); `UT_EpicChainLinks.AWorldTheLinksWereWrittenIntoIsNotedForTheRollback`; `test_epic_chains.py` |

**Why the server links the chains instead of the installer running epic_chains.py:** the game ships no Python (checked
against the 0.36b package's file list: `tools\dotnet` is there, no Python), and PowerShell can't write SQLite without
it. The server already reads every quest row at start, so it makes the same changes there, without writing them: no
backup or rollback record is needed because nothing changes on disk, an uninstall leaves the released rows, and an
Offline DAoC update (which changes a world row only while it holds its released value) finds them as it expects.

**Found by the harness, not yet fixed (data, [needs the world] to count):** a delivery item that isn't an item template
stops the step *before* the delivery (the item is handed when that step ends, and the missing template throws), so the
player talks to the NPC and nothing happens. The world report shows these as "interacting with ... did not advance" with
"StepItemTemplate ... not found" from the server's log.

**First world report (2026-11-04, a copy of the owner's test world, 1,540 quest rows):**
the first run said 1,253 finished. Almost all of the rest were the harness's own gaps, now closed: the player didn't walk
to the kill, so a gamebot's kill credited nobody (214); classic steps whose monsters drop what they collect weren't
farmed (44, "unknown classic step kind"); the NPC templates of classic event monsters weren't loaded, and the template
lookup's logger threw (12; a logger failure is now reported as a harness problem, apart from the quests); and drop
chances got too few tries (2 or 3 per run by luck). One was a real defect, fixed in 1.2: **quest 401 Human Leg XP Item**
collects `Human_Leg` while its item template is `Human_leg`; the server finds templates without case, so the leg drops,
but the collection compared ids with case and Dera never took it. Now all 1,540 finish every walk (the player alone,
kills by a gamebot in the group, outlevelled before the hand-ins), with no world problems found. That is the harness's
verdict on the rows and the code, not a play test (section 6, "Reading it honestly").

**Next:** play the three quests of section 1 (a kill quest in a bot group, a delivery, an epic step) as a normal account.
