# Bot-led groups: "You are wanted" (design for 1.2, phase B)

Owner: Chris. Asked for on r/daoc (2026-11-03): *"I like the idea of eventually having bot led groups so the player
can be more passive from a leading perspective."* Roadmap line ([RELEASE-TRAIN.md](RELEASE-TRAIN.md), phase B): bots
invite the player; bot-led groups the player can follow without leading, with a toggle back to leading; a raid leader
bot who invites the player. **Nothing here is built yet.** This page is for approval before any code.

## The page for players

**Today** you lead every group you are in. You `/invite` gamebots, you pick the camp, you `/pull`. A bot that wants
to group with you says "invite me". In 1.2 a gamebot leading a group can invite **you**, and you can just follow.

**When a bot invites you**
- Only a group leader of your realm, whose group is within 3 levels of you on average and has room for your class
  (a group short of a healer asks a healer first). It is hunting somewhere you can reach on foot or by horse.
- First a tell, the way a 2003 leader did it: *"lf 1 more for skellies in Cursed Forest, want in?"* Then the usual
  group invite box: *"Brannoc has invited you to join his group. Do you wish to join?"* Yes or No.
- **Never** while you are fighting, dead, riding, on a boat, in another yes/no box, trading, in a group, with `/spawn`
  companions out, `/afk`, in a frontier, battleground or raid, or in your first 10 minutes online.
- **How often:** when you ask (`/lfg`, or a tell like "lfg?" to a bot), an invite can come within a minute. Otherwise
  at most one unasked invite every 45 minutes, and it counts as that period's bot tell, so tells and invites never
  stack.
- **Saying no:** press No, or answer the tell "no thanks". No unasked invite for 30 minutes after a no, nothing from
  that leader for 6 hours, and none at all for the rest of the session after three. `/invites off` (or the launcher)
  turns invites off; `/lfg` still gets you one.
- **You can ask too:** a tell to a bot leader with room ("room for a cleric?") gets the box, not "invite me".

**Following.** The leader runs the group as a bot group does now: it picks a camp for the group's level (never more
than 6 levels above yours), a meeting spot near you, the pulls and the breaks.
- **Getting there:** the leader tells you where to meet ("meet at Adribard's, horse to Caer Ulfwych"). You travel
  yourself: run, `/follow` the leader, or buy your own stable ticket. You are **never teleported**, summoned or
  given money. The group waits at the meeting spot up to 20 minutes and calls out where it is at 5 and 10.
- **On the way:** the group moves as one, at the speed you can run. The leader stops when you fall 1,500 units behind
  and goes on once you are close. It waits for you on the far side of zone lines and dungeon entrances, and at docks
  and stablemasters: you board or ride first and the bots come with you, as your invited bots do now.
- **Lagging behind:** after 2 minutes stopped for you the leader asks "where u at?"; after 10 minutes with you not
  getting closer the group goes on to the camp, says so in `/group`, and keeps your spot for you there.
- **At camp:** the group pulls only when you are near the camp, alive, and up on health and power like the rest. If
  you sit to rest, they wait.
- **AFK:** short breaks are normal. Set `/afk`, or stop doing anything for 10 minutes, and the group keeps pulling
  where it stands but goes nowhere new. After 15 minutes it holds ("waiting on X, everyone med"). After 30 minutes it
  lets you go at the next break, with a tell to send it a tell when you are back.
- **Dying:** the fight finishes, a healer rezzes you and the group waits until you are back up. With no rezzer the
  leader says "release, we'll wait": the group holds the camp for 10 minutes while you run back from your bind.
- **Leaving:** `/disband` at any time. The group carries on without you and looks for a replacement. Logging out or
  going linkdead counts as leaving after 5 minutes.

**Taking the lead.** `/lead` takes over: the leader says "all yours", you become the group leader and the bots follow
you exactly as invited bots do now (`/pull`, your camps, your pace). `/lead bot` (or `/makeleader <bot>`) hands it
back; that bot picks a camp near where you stand. The launcher has **When you join a bot's group: follow / lead**.
With *lead*, saying yes makes you the leader at once.

**Loot, quests, XP.** Real loot, as everywhere, shared with you and weighted in your favour, whether you follow or
lead (owner 2026-11-04): you count as two members in the coin split and in the item rotation, so with seven bots
nearby you get 2 of every 9 items and 2/9 of the coin. Items your class can use and that beat what you wear come to
you first. The weight is a setting (default 2). A kill by
the bots counts for your kill quests when you are alive and in range, as it does now in a group you lead. XP is the
game's normal group XP. Selling happens only at natural breaks: the end of the session, after a wipe, or "last pull
then town to sell" when bags fill. The group never leaves a camp because one bot wants a merchant.

**Raid leaders.** When a dragon or epic dungeon raid forms in your realm and you are level 50, its raid leader (an
officer of a raiding guild) may send you a tell and a box: *"Forming for Gjalpinulva at Svasud Faste. Want a spot?"*
Yes puts you in a party that needs your class; one bot of your role steps out to make room. You travel to the muster
town yourself; your party waits there until the raid leaves, and after that you catch up on your own. One invite per
raid. A no is final for that raid. The raid's loot rule for players is unchanged: a quarter of the drops.

**Group chat.** The leader says what comes next in `/group`, short and lowercase, the way 2003 leaders did:
"moving to the ruins", "pulling", "inc 2", "med up", "last pull then town", "lf 1 more, any healer?" in `/lfg` when a
spot opens. The rest of the group's calls (adds, oom, rez) work as they do now. Everything goes through the chat
budgets, so a group is never chattier than one you lead.

**Edge cases**
- **The leader dies:** it stays leader while the group rezzes it. If it releases and isn't back in 5 minutes, or it
  logs off or goes linkdead, the next bot leads ("Brannoc went ld, i'll lead"). You never become leader unless you
  type `/lead`; the only exception is when fewer than two bots remain, and then you get a yes/no box.
- **You are the only healer:** the group counts you as its healer. It won't pull while you are dead, out of power,
  AFK or away, and when you leave it asks for a healer in `/lfg` or ends at the next break.
- **Levels drift:** checked at each break. Bots that outlevel the group by more than 5 leave and are replaced. If
  *you* move out of the group's range, the leader picks a camp for the new spread, or ends the group at the next
  break ("you outgrew us, gl").

**Not in 1.2:** bot-led RvR groups and warbands inviting you (after RvR perception lands); battlegroup windows and
raid chat; more than one real player in a bot-led group; your `/spawn` companions in one; asking the leader for a
particular camp or quest (take `/lead` for that); stances, presets and a named main assist (the group framework's
later steps). Darkness Falls raid AI, Legion and the hardest level 70+ encounters are not implemented and nothing here
changes that.

---

## Technical plan

### What exists (read 2026-11-04 on `sync/1.2-with-1.1.2`)

| Piece | Where | What it does today |
|---|---|---|
| Bot group sessions | `AutonomousBotGroupCoordinator.cs` (`Session`, `TryFormGroups`, `UpdateSession`, `ChooseLeader`, `ChoosePuller`, `OnMemberRemoved`, `FinishGroupTask`) | Forms bot-only GroupPve/RvR parties from role templates (`PveRosterTemplates`, `LevelsCompatible` = within 5 or both 50), plans a camp and a meetup at the safe town nearest it (`PlanMeetupNearCamp`), runs attendance (`AutonomousRendezvousAttendance`, 15-minute no-show timeout), a 45-120 minute task clock, a locked puller, recovery after wipes. |
| A player in a bot group | `AutonomousBotGroupCoordinator.Pulse` | **Any** group with a `GamePlayer` gets a fake "Player-led" directive and no session. So there is no bot-led group with a player in it today. |
| Player-led groups | `GameBot.PlayerGroupLeader` / `IsPlayerLedGroup`, `EnterPlayerLedGroup`, `BotGroupInvite.TryInvite`, `Group.ReleaseGamebotsLedBy`, `PlayerLedPullCoordinator`, `CompanionFollowPolicy`, `BotGroupRelease` | `/invite` pulls a gamebot out of its autonomous group into the player's; the bot follows the player and assists. `IsPlayerLedGroup` is read at about 70 places in 29 files. |
| Companion bots | `/spawn`, `IsTemporaryGroupHelper`, `Owner` | Temporary, owned by the player, always player-led, no loot or progress (`SuppressLootAndProgress`). Untouched by this design. |
| Bots asking you | `AutonomousLivingServer.Tells.cs` | Duo and help offers by tell (one every 25-45 minutes at most, 6-hour pair cooldown, `/lfg` answers); "sure" gets "invite me": *a bot cannot invite a player*. |
| A bot's yes/no box | `AutonomousBotGuildSocial.SendInviteDialog` | `SendCustomDialog` posted on the client service, never over another open box, a 24-hour decline memory. Both clients show the Dialog packet. |
| Realm raids | `AutonomousRealmRaid.cs`, `RealmRaidMuster`, `RealmRaidLootOwner` | Bot-only eight-person parties reserved for a raid; a screen notice tells players where it musters; a player near the encounter gets a quarter of high-end drops. No raid leader bot, and no way to put a player in a raid party. |
| Quest credit (1.1.2) | `QuestKillCredit.GroupLeaderFor`, `GameLiving.ProcessDeath` | A gamebot's kill credits its `PlayerGroupLeader` when alive, in its group and within 6,000 units. Keyed on the leader, so a bot-led group would credit nobody. |
| Group loot | `Group.TryAutoPickUpMoney/Item` | `Group.Leader` is `LivingLeader as GamePlayer`. With a bot leading it is null, and coin and items go to the bots only: **a player following a bot would get nothing.** |
| Leader succession | `Group.RemoveMember` | When the leader leaves, the first `GamePlayer` is made leader: a bot-led group would silently become player-led. |
| Group chat | `AutonomousBotCombatCalls`, `AutonomousBotGroupChatter` | Keyed on `GroupHasPlayer`, so they already speak in a bot-led group. |
| Breaks and logouts | `AutonomousBotSchedule.CanLogOut` | Grouped bots never take a break or log out; only the linkdead roll applies to bots grouped with you. |

### The changes

1. **One new file, `bots/autonomous/BotLedGroups.cs`** (pure rules first, then the game glue):
   `MayInvite(...)` (states, band, place, cadence), `NextUnaskedInvite`, decline memory, `PickInvitingLeader`,
   `SendGroupInvite` (tell, then the box via the guild invite's pattern, text as the native group invite), accept →
   `Group.AddMember(player)` and `session.HostedPlayer = player`. Hooks from `AutonomousLivingServer.TryStartTell`
   (shares the tell cadence), `OnPlayerLfg` and the join-request branch of `AutonomousBotChatCoordinator`.
2. **`AutonomousBotGroupCoordinator`:** `Session` gains `HostedPlayer`, `PlayerRole`, `PlayerJoinedTick`,
   `PlayerAwaySince`, `PlayerAfkSince`. `Pulse` keeps the fake "Player-led" directive only when the session has no
   `HostedPlayer`. Attendance, pace (`AutonomousGroupPace`), cohesion (`IsCohesive`, `FarthestMemberDistance`),
   `MembersReadyForPull` and `HasViablePveComposition` count the player. Camp level is capped at the player's level
   + 6. `OnMemberRemoved` handles a `GamePlayer` leaving (the party goes on). `ChooseLeader` never returns the player.
3. **`Group.cs`:** in a hosted session, `RemoveMember` elects a bot leader (through the coordinator), not the player.
   Coin and items in a group with a bot leader and a player use a mixed split: coin evenly by member in range; items
   through a per-group ledger like `RealmRaidLootOwner.Ledger` (the player one in N, the rest at random to bots).
4. **`/lead`** (new `commands/playercommands/lead.cs`): take over (`MakeLeader(player)`, every autonomous bot
   `EnterPlayerLedGroup`, the session ends **without** disbanding: a new `FinishGroupTask` mode); `/lead bot` and
   `/makeleader <bot>` start a session for the existing group. `/invites on|off`. `/pull` and `/invite` in a hosted
   group answer "Brannoc is leading; /lead to take over" (today `/pull` would quietly make every bot player-led).
5. **Spoken for by a player:** `GameBot.IsHostingPlayer`, and `IsSpokenForByPlayer => IsPlayerLedGroup ||
   IsHostingPlayer` used where `IsPlayerLedGroup` means "don't take this bot" rather than "follows a player": raid
   eligibility (`AutonomousRealmRaid.IsEligible`), siege jobs and siege parties, RvR defense, objective rebalancing,
   frontier transport, the economy's merchant fallback. The ~45 reads that mean "follows the player" stay as they are.
   Each of the 70 reads gets classified in the PR.
6. **Travel:** `TemporaryGroupStableTravel.TryBringGroupmateAlong` and the frontier boarding queue accept a hosted
   player as the one the bots ride with; zone-line holds use the existing dungeon-arrival hold with the player counted.
7. **Quest credit:** `QuestKillCredit.GroupLeaderFor` becomes `CreditedPlayerFor`: the `PlayerGroupLeader`, or else
   the hosted player of the bot's session, with the same alive / same group / 6,000-unit rule.
8. **Raid leader:** `AutonomousRealmRaid` gains `RaidLeader` (a level-50 officer of a raiding-charter guild in the
   muster, else the first party's leader), `TryInvitePlayer` once per raid per player, and `AddPlayerToParty`
   (picks the party by role, releases one same-role bot to the raid's reserve with `ReleasePartyMember`, keeps the
   party at its hub post until departure). The raid's existing loot share applies.
9. **Chat:** a `lead` topic in the voice corpus (move, pull, wait, where-u-at, break, sell, lf-more, handover) with
   a plain fallback line each; spoken through `AutonomousLivingServer.Speak(Channel.Group)` under the group budget.
10. **Settings:** `bot_group_invite_player` (on), `bot_raid_invite_player` (on), `bot_led_group_join_mode`
    (`follow`/`lead`) in `ServerProperties`, the launcher's `SocialExpansionSettings` and its control, and
    `ForkBuildInfo`. Logs: `BOT_LED_INVITE` (sent/declined/accepted, with reason), `BOT_LED_HANDOVER`,
    `BOT_LED_PLAYER_LEFT`, `RAID_PLAYER_INVITE`.

### The duty lease

The roadmap's lease record (phase A) is not built yet. A bot-led group hosting you is exactly a lease: kind
`player_group`, the highest automatic priority (no automatic or forced raid, siege or RvR event takes its members),
exempt from the solo watchdog, reason code `hosting_player`, and `/lswhy` answers "in X's group with you". **If the
lease lands first**, step 5 is one lease kind instead of a flag. If not, step 5 ships as the interim flag and becomes
a lease kind when the record exists.

### Tests (automated; none of this replaces a played check)

- `UT_BotLedInvitePolicy`: every "never" state, the level band and role need, cadence (10-minute start, 45-minute
  gap, the shared tell budget, the `/lfg` fast path), decline memory (30 minutes, 6 hours per leader, three in a
  session), no box over an open box.
- `UT_BotLedGroupSession`: the player counts for pace, cohesion, attendance (20 minutes, reminders at 5 and 10) and
  pull readiness; trailing, AFK (10/15/30 minutes) and death holds; the camp cap; a player leaving keeps the party.
- `UT_BotLedLeadership`: succession elects a bot; `/lead` hands over without disbanding; `/lead bot` starts a session;
  refused with `/spawn` companions; `/pull` and `/invite` refused while a bot leads.
- `UT_BotLedLoot`: coin includes the player; the item ledger gives one in N; bot-only groups unchanged.
- `UT_QuestKillCredit` (extended): a hosted player is credited in range, not when dead, far or in another region.
- `UT_RaidLeaderInvite`: one invite per raid, the role pick, the released bot goes to the reserve, level 50 only.
- `UT_SpokenForByPlayer`: raids, sieges, RvR events and rebalancing skip hosted bots.
- Launcher settings round trip (Windows CI). With the scripted player (phase A harness): accept an invite, reach the
  camp, finish a kill quest. The owner's played check: one session per realm following a bot, one `/lead` handover
  and back, one raid invite.

### Size

About 2,000-2,500 lines of server code and 1,000 of tests, in three pull requests: **B1** invites and following
(steps 1-3, 5-7, 9-10; the largest), **B2** the `/lead` toggle and handover (step 4), **B3** the raid leader
(step 8). Roughly three build sessions plus a soak, after the lease record and before RvR perception.

## Decisions (owner 2026-11-04)

1. **Defaults:** invites on, follow as the join mode, unasked invites at most every 45 minutes (asked ones within a
   minute). Accepted as recommended.
2. **Loot:** in every group with you and gamebots, following *or* leading, loot is shared with you and skewed in your
   favour: you count as two members in the coin split and the item rotation (a setting, default 2), and an item your
   class can use that beats what you wear comes to you first. This changes the player-led rule too (today everything
   goes to you when you lead), so it ships with a changelog line and the setting.
3. **Camp difficulty:** capped at 6 levels above you while you are in the group. Accepted.
4. **AFK:** keep pulling in place up to 15 minutes, hold to 30, then let you go at the next break. Recommended; awaiting the
   owner's answer after a plain-words explanation (2026-11-04); confirm or change before B1.
5. **Order:** after the duty lease record; if the lease slips, B1 ships with the interim flag. Accepted.
