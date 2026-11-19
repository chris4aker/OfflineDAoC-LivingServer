# Bot-led groups: "You are wanted" (design for 1.2, phase B)

Owner: Chris. Asked for on r/daoc (2026-11-03): *"I like the idea of eventually having bot led groups so the player
can be more passive from a leading perspective."* Roadmap line ([RELEASE-TRAIN.md](RELEASE-TRAIN.md), phase B): bots
invite the player; bot-led groups the player can follow without leading, with a toggle back to leading; a raid leader
bot who invites the player. Approved by the owner on 2026-11-04. **B1 (invites and following) is built**, with
automated tests only; nothing has been played in game yet. **B2 (`/lead`, the handover) is built** the same way,
automated tests only, and **B3 (the raid leader) too**. See [B1 as built](#b1-as-built-2026-11-04),
[B2 as built](#b2-as-built-2026-11-05), [B3 as built](#b3-as-built-2026-11-05) and
[B4 as built](#b4-as-built-2026-11-05) (replacements, the natural break, the leader's pull call) at the end.

## The page for players

**Today** you lead every group you are in. You `/invite` gamebots, you pick the camp, you `/pull`. A bot that wants
to group with you says "invite me". In 1.2 a gamebot leading a group can invite **you**, and you can just follow.

**When a bot invites you**
- Only a group leader of your realm, whose group is within 3 levels of you on average and has room for your class
  (a group short of a healer asks a healer first). It is hunting somewhere you can reach on foot or by horse.
- First a tell, the way a 2003 leader did it: *"lf 1 more for skellies in Cursed Forest, want in?"* Then the usual
  group invite box: *"Brannoc has invited you to join his group. Do you wish to join?"* Yes or No.
- **Never** while you are fighting, dead, riding, on a boat, in another yes/no box, trading, in a group, with `/spawn`
  companions out, `/afk`, or in a frontier, battleground or raid. Never unasked in your first 10 minutes online.
- **How often:** when you ask (`/lfg`, or a tell like "lfg?" to a bot), the leader answers by tell and the invite
  box follows within 15 seconds (owner 2026-11-04). Otherwise
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
lead (owner 2026-11-04): an item your class can use that beats what you wear comes to you; otherwise an upgrade for a
gamebot nearby goes to the bot it improves most (they start without armour and earn it); everything else goes round
with you counting as two members, so with seven bots nearby you get 2 of every 9 of those items. Coin is split evenly
with you counting as two (2/9 with seven bots). The weight is a setting (default 2). A kill by
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
   goes to you when you lead); confirmed by the owner, so it ships with a changelog line and the setting. Refined after
   the B1 review: upgrades for a gamebot in range go to that bot before the rotation (see B1 as built).
3. **Camp difficulty:** capped at 6 levels above you while you are in the group. Accepted.
4. **AFK:** keep pulling in place up to 15 minutes, hold to 30, then let you go at the next break. Accepted.
5. **Order:** after the duty lease record; if the lease slips, B1 ships with the interim flag. Accepted.

## B1 as built (2026-11-04)

Branch `feature/bot-led-groups-b1`, stacked on the duty lease (`feature/duty-lease`). Changes 1-3, 5-7, 9 and 10.

| Change | Where | Notes |
|---|---|---|
| 1. Invites | `bots/autonomous/BotLedGroups.cs` (pure rules first, then the glue) | `StateBlocks`, `InLevelBand`, `CandidateBlocks`, `CadenceBlocks`, `InviteMemory`, `PickInvitingLeader`; the pulse (every 5 s, from `AutonomousLivingServer.Pulse`), `OnPlayerAsked` (`/lfg`, a tell asking a bot leader), the tell then the box on the client service, `OnInviteTellAnswered` ("no thanks"). The tell budget is shared through `AutonomousLivingServer.SinceLastBotTell` / `CountInviteAsTell`. |
| 2. The coordinator | `AutonomousBotGroupCoordinator.BotLed.cs` and small hooks in `AutonomousBotGroupCoordinator.cs` | `Session.HostedPlayer`, `PlayerRole`, `Follow` (a `BotLedGroups.FollowState`). The fake "Player-led" directive only when the group's player is not the hosted one; the broken-session sweep accepts the hosted player. The player counts in the meetup, `FarthestMemberDistance` / `IsCohesive` (through the lock-free `BotLedGroups.HostedView`), `CanInitiateNewPull`, `HasViablePveComposition`, the dungeon arrival hold and the directive's level (`HostedCampBand`). `PublishCamp` takes no new camp while the player is away. A hosted directive skips frontier, battleground and Darkness Falls camps. |
| 3. Group | `gameutils/Group.cs` | `RemoveMember` elects a gamebot (`BotLedGroups.ElectLeader`) in a hosted party. Loot: `BotLedGroups.TrySplitMoney` / `TrySplitItem` for every group with the player and an autonomous gamebot, whoever leads. |
| 5. Spoken for | the duty lease | No `IsSpokenForByPlayer` flag: the party's gamebots hold `BotLedGroupWithPlayer` (priority 90, reason `hosting_player`, bound to the group), which only a player's own group takes. The "don't take this bot" reads were moved to the lease in phase A (DUTY-LEASE.md); a hosted bot is not `IsPlayerLedGroup`, so the reads that mean "follows the player" stay false for it: the bot leads. |
| 6. Travel | `bots/TemporaryGroupStableTravel.cs` | `PlayerTheBotTravelsWith`: porters bring the hosted player's party members beside them, as for invited bots. |
| 7. Quest credit | `quests/QuestsMgr/QuestKillCredit.cs` | Only `GroupLeaderFor` changed (the hosted player, same rule). The file is also edited by `feature/quest-harness`. |
| 9. Chat | `data/chat/bot_led_groups.lines`, `group_talk.lines`, `AutonomousBotVoices` | Topics `lead_*` (flag `lead_call`, so only the leader's calls say them) with plain fallbacks; flag `bot_leads`, and `!bot_leads` on the lines that assume the player leads. |
| 10. Settings | `ServerProperties`, the launcher's `SocialExpansionSettings` and its tab, `ForkBuildInfo` | `bot_group_invite_player` (on), `bot_group_player_loot_weight` (2). |

**Decisions (owner 2026-11-04, after the B1 review):**
- **Asked invites come within 15 seconds.** When the player asks (`/lfg`, or "lfg?" or the like by tell), the leader
  answers first with a tell, then the invite box follows within 15 seconds of the ask (`AskedWindowMs`; the answer
  1-2.5 s after the decision, the box 1.5-3 s after the answer, and an ask the pulse picks up later is served only
  while that still fits). Asked invites skip the 10-minute start, the 45-minute gap, the 30-minute quiet after a no and
  the three-noes rule; they still respect every "never" state, a box already open and the 6 hours per leader. With
  invites off, asking still works. Unasked invites keep every rule.
- **Loot: the player gets the primary spoils, and gamebots still gear up and fund their improvements.** Each item in a
  group with the player and gamebots, whoever leads (`BotLedGroups.Route`): (a) an upgrade for the player goes to the
  player; (b) otherwise an upgrade for a gamebot in range goes to the bot it improves most, by the gamebots' own
  appraisal (`AutonomousBotEconomy.TryGetEquipmentUpgrade`, ranked by `EquipmentValue` gain); (c) everything else goes
  through the rotation with the player counting as `bot_group_player_loot_weight` members. Upgrades are given outside
  the rotation and not counted in it. Coin is split evenly among the members in range with the player counting as
  that weight. A manual pickup in such a group follows the same rules. `BOT_LED_LOOT` logs each item's reason
  (`upgrade-player`, `upgrade-bot`, `rotation`).
- **The leader's calls skip the chat budget's gaps.** Where to meet, still waiting, going on, where u at and med up
  carry what the player needs to follow, and there are only a few an hour by construction, so they are not held by
  the group channel's gaps. The group's flavour talk stays under the budget.
- `bot_led_group_join_mode` and `bot_raid_invite_player` come with B2 and B3.

**Decided while building:**
- A hosted party's camp is never in a frontier, battleground or Darkness Falls, so the frontier boarding queue is not
  needed in B1.

**Not in B1 (yet):** `/lead`, `/lead bot`, `/makeleader`, `/invites on|off`, `bot_led_group_join_mode` and the
"fewer than two bots" yes/no box (B2); the raid leader and `bot_raid_invite_player` (B3). The gamebots don't mirror the
player's horse or boat ride (the leader rides its own and waits at the far end); the region-crossing hold waits on the
near side of a zone line, not the far side; the "no rezzer: release, we'll wait" line, levels drifting at a break
("you outgrew us") and looking for a replacement after the player leaves are not built.

## B2 as built (2026-11-05)

Branch `feature/bot-led-groups-b2`, stacked on B1. Change 4 and the B2 items of "Not in B1". Automated tests only
(`UT_BotLedLeadership`, the launcher's `TheJoinModeIsSavedReadAndChecked` on Windows CI); **nothing has been played in
game**.

| Piece | Where | Notes |
|---|---|---|
| Pure rules | `bots/autonomous/BotLedGroups.Leadership.cs` | `ParseJoinMode`, `TakeLeadBlocks`, `HandBackBlocks` (and their messages), `PickNewLeader`, `OffersLeadOnEnd`, `NaturalRole`, `ParseToggle`, `BotLeadsMessage`; `InviteMemory.PlayerInvitesOff` read by `CadenceBlocks`. |
| `/lead` | `commands/playercommands/lead.cs` → `BotLedGroups.TakeLead` → `AutonomousBotGroupCoordinator.TryHandOverToPlayer` (`AutonomousBotGroupCoordinator.Handover.cs`) | The leader says `lead_handover` while the bots still lead (so its flags fit), each gamebot `EnterPlayerLedGroup` (the PlayerGroup lease takes the bot-led one in one step), the session ends without disbanding (`DetachSession`: a new ending mode, `disbanded=false`), `MakeLeader(player)`. |
| `/lead bot [name]`, `/makeleader <gamebot>` | `lead.cs`, `makeleader.cs` → `BotLedGroups.HandBack` → `TryHandBackToBot` | A session for the existing group, phase "Choosing group target", rendezvous where the leader stands (no meetup), the player hosted with the meetup done. Roles: the bots' own roster when it fits (`TryAssignExactPveRoles`), else each by class, the player counting. Leases move with `BotDutyLeases.HandOver` (PlayerGroup → BotLedGroupWithPlayer, never free in between). The new leader says `lead_take`. |
| The join mode | `ServerProperties.BOT_LED_GROUP_JOIN_MODE` (`follow`), the launcher's **When you join a bot's group**, `ForkBuildInfo` | With `lead`, accepting the invite hosts the player without the welcome line and hands over at once. |
| The last gamebot's box | `FinishGroupTask` → `TryOfferLeadToLastBot`; `BotLedGroups.AskToLeadLastBot` | Instead of disbanding a hosting party with one gamebot left (not a raid party): `lead_last_bot`, the session detaches, the box. Yes: `MakeLastBotFollow`. No or 60 s: farewell, the bot leaves, back to its own goals. Never over another box (retried every 5 s within the minute). |
| `/invites on|off` | `commands/playercommands/invites.cs`, `BotLedGroups.SetPlayerInvites` | Saved per character in `DOLCharactersXCustomParam` (`LivingServer.BotLedInvites`), a saved-progress table (emptied by the release builder, carried by the progress transfer). |
| Bot leads, so | `PlayerLedPullCoordinator`, `invite.cs`, `BotGroupInvite.TryInvite`, `makeleader.cs` | "Brannoc is leading this group. /lead to take over." |
| Fixes found on the way | `Group.MakeLeader`, `HasActivePveComposition` | `MakeLeader` named `Leader.Name` (null when a gamebot is made leader: a crash); a hosted player now counts in the pull gate's roster as in `HasViablePveComposition` (a party whose only healer was the player never pulled). |
| Chat | `data/chat/bot_led_groups.lines` | `lead_handover`, `lead_take`, `lead_last_bot` (spoken); `lead_pull`, `lead_sell`, `lead_lf_more` (written, not spoken: see below). Realm-neutral. |

What a handover keeps: the group object (the loot ledger is keyed by it, so the rotation carries on), its gamebots,
the player's hold on their time, and quest credit (after `/lead` a gamebot's kill credits its `PlayerGroupLeader`;
after `/lead bot` the hosted player, `QuestKillCredit.GroupLeaderFor`). The session's task clock, camp and wipe
history do not survive `/lead`; `/lead bot` starts a fresh task.

**Decided while building** (the owner confirmed three of them on 2026-11-05; the rest are still open):
- **Confirmed by the owner (2026-11-05):** `/lead` is refused in a realm raid's party: the raid leader runs a raid.
- **(confirm)** `/lead bot` needs at least two gamebots, a tank, a healer and a damage dealer (the player counting),
  no `/spawn` companions, no fight going on, and a place a player can walk or ride to; without a name or target the
  highest-level gamebot leads.
- **Confirmed by the owner (2026-11-05):** when the player who took the lead leaves (`/disband`), the gamebots leave
  too, as with gamebots the player invited (issue #33). They don't carry on as a party of their own.
- **(confirm)** The last gamebot's box waits one minute; no answer counts as no.
- **Confirmed by the owner (2026-11-05):** `/invites off` is per character and also stops the raid leader's invites (B3).
- **(confirm)** A party started by `/lead bot` keeps going after the player leaves even when its gamebots' own goals
  differ (a bot-only party with mixed goals is broken up today); its task clock still ends it.

**Not in B2:** the leader's "pulling", "last pull then town" and "lf 1 more" calls (lines written, not spoken; built in
B4, see [B4 as built](#b4-as-built-2026-11-05)). Wiring them was not
a small change: the pull is already called by the existing combat calls ("inc", `AutonomousBotCombatCalls`), so a
second pull line would double it; "last pull then town" needs a bags-full break for a party hosting the player, which
doesn't exist (a party only sells at its task's end); "lf 1 more" needs the replacement search after a member leaves
(B1's "not built"), or the line would promise a recruit that never comes;
the B1 list above (horse and boat rides, the far-side zone-line hold, "release, we'll wait", levels drifting, a
replacement after the player leaves) is unchanged. The handover's packets, chat timing and the box are not covered by
automated tests (they need a client); they need the owner's played check: one `/lead` and `/lead bot` per realm, and
one party left with one gamebot.

## B3 as built (2026-11-05)

Branch `feature/bot-led-groups-b3`, stacked on B2. Change 8. Automated tests only (`UT_RaidLeaderInvite`, the
launcher's `RaidLeadersInvitesAreSavedReadAndTurnedOffWithAllOff` on Windows CI); **nothing has been played in game**.

| Piece | Where | Notes |
|---|---|---|
| Pure rules | `bots/autonomous/RealmRaidPlayerInvite.cs` | `InviteBlocks`, `PickRaidLeader`, `PickParty`, `PickStepOut`, `TargetWithPlayer` / `TargetWithoutPlayer`, the tell and box texts. |
| `RaidLeader` | `AutonomousRealmRaid.Players.cs` (`AutonomousRealmRaid` is now `partial`) | Computed from the raid's parties: a level-50 rank 0-2 member of a `Raiding`-charter bot guild (highest rank, earliest party, lowest id), else the first party's leader. Not stored in `Raid`, so the raid file is untouched apart from `partial`. |
| The invite | `AutonomousRealmRaid.PulsePlayerInvites` (from `BotLedGroups.Pulse`, every 5 s, only while a raid runs), `TryInvitePlayer`, `SendRaidInvite`, `ShowRaidBox` | Once per raid per player (`InvitedPlayers`, keyed by raid id and start tick, forgotten when the raid ends); only when a seat exists (`HasSeatFor`); shares the group invite's "never" states, the 10-minute start, the box-waiting flag and `/invites off`; counts as that period's bot tell. Timed on the player, so a leader logging off can't leave the box flag set. |
| The seat | `AutonomousBotGroupCoordinator.RaidGuest.cs` (`TrySeatRaidGuest`) and `AutonomousRealmRaid.SeatPlayer` | `PickParty` over the raid's parties (roles from each party's session); a full party: `PickStepOut`, the bot leaves the party's roster and lease (not marked released, so refills may take it again), then the group (`ProcessingAttendanceRemovals`) and goes back to its own goals; the party's gamebot target drops by the player's seat. The player is hosted as a **raid guest** (`HostedView.RaidGuest`). |
| Raid guest | `BotLedGroups.HostedView.RaidGuest`, `TemporaryGroupStableTravel.PlayerTheBotTravelsWith`, `TakeLead`, `TryOfferLeadToLastBot`, `EndHosting` | No follow rules (B1's `StepHostedPlayer` already skips raid parties), no porter rides, no `/lead`, no last-bot box; quest credit, a gamebot leader when one leaves and the loot share apply. Leaving gives the seat back (`OnPlayerLeftParty`). |
| Lease | unchanged rules | The raid party's gamebots keep `RealmRaid` (`raid_party`), so the raid's own checks (`IsEligible`) keep them and nothing below 40 takes them; a gamebot in the player's group or a hosting party is never recruited (`IsSpokenForByPlayer`). |
| Settings, chat | `bot_raid_invite_player` (on) in `ServerProperties`, the launcher, `ForkBuildInfo`; `lead_raid_invite` (tell) and `lead_raid_welcome` (group) | Realm-neutral lines. Log: `RAID_PLAYER_INVITE`. |

**Decided while building** (the owner confirmed three of them on 2026-11-05; the rest are still open):
- **Confirmed by the owner (2026-11-05):** invites come until the raid begins its battle (also after the parties left
  the hub: the player then catches up on their own), for automatic and launcher-forced raids alike, never for neutral
  raids.
- **Confirmed by the owner (2026-11-05):** the gamebot that steps out goes back to its own play and may be recruited
  again by the raid's refills ("the raid's reserve" in the plan); it is not kept reserved.
- **(confirm)** A party with room takes the player without anyone stepping out, before any full party.
- **Confirmed by the owner (2026-11-05):** raid invites keep the 10-minute start, the box-waiting flag and
  `/invites off`, but not the 45-minute gap or the tell budget's wait (a raid forms rarely and only for a while); the invite
  still counts as a bot tell.
- **(confirm)** In a raid party the player's linkdead and AFK timers of B1 don't apply (the raid runs the party).

**Merge note:** `feature/raid-win-balance` (#75) changes `AutonomousRealmRaid.cs` in many places; B3 changes only its
class line (`partial`). The new partial reads `Raid.Parties`, `Party.Members`, `Party.Target`, `EndPartyLease` and
`Membership`, so a rename there would need a matching change here; #75's Special Forces slots are recognised the same
way (a party of Special Forces only).

**Not in B3:** the player in a raid party is not counted at the hub or the staging ring (the raid departs and starts
on its gamebots' numbers, as the design says); raid chat and battlegroup windows (not in 1.2). Darkness Falls raid AI,
Legion and the hardest level 70+ encounters are not implemented and nothing here changes that.

## B4 as built (2026-11-05)

Branch `feature/bot-led-groups-b4`, stacked on B3. Asked for by the owner after B2's review: the leader lines that were
written but not spoken, with the parts behind them. Automated tests only (`UT_BotLedReplacementAndBreaks`);
**nothing has been played in game**.

| Piece | Where | Notes |
|---|---|---|
| Replacement rules | `bots/autonomous/AutonomousPartyReplacement.cs` | `Wanted` (a party that hosts or hosted the player, PvE, not a raid party, choosing a camp / travelling / grinding, a seat free), `Search` and `Next` (a try every 15 s, give up after 5 minutes: carry on when still viable, end otherwise), `HoldsParty`, `Pick` (free, waiting for a group, the lease allows it, the role, within 5 levels or both 50, at most 8 minutes away; the nearest, then the closest level), `NeedWord`, `LfgFits`. |
| Replacement glue | `AutonomousBotGroupCoordinator.Replacement.cs`; hooks in `OnMemberRemoved`, `OnHostedPlayerRemoved`, `UpdateSession`, `RemoveBrokenSession` | A member leaving starts a search for its role (the player leaving: the player's role); a party that lost its core is held (no pulls: `HasActivePveComposition` stays false) instead of ending. Each try: the `/lfg` ask once (`lead_lf_more`, else the corpus's `lfm` lines, through `AutonomousBotChatCoordinator.SendLfgLine` and the channel meter), then up to 16 candidates get a travel estimate. The recruit joins the group, claims the party's lease (`BotLedGroupWithPlayer` while the player is hosted, else `BotGroupSession`; a refused claim lets it go), gets the role and `LockedSize`, and travels to the party through the normal directive. |
| Natural break | `BotLedGroups.Breaks.cs` (`BreakDue`, `BreakState.EndsNow`); `StepNaturalBreak` | Half or more of the gamebots' bags nearly full (`AutonomousBotEconomy.IsBackpackNearlyFull`, the between-task unload mark), or 5 minutes or less of the task left. `lead_sell` at the call, the last pull fought (or 3 minutes with none), then `FinishGroupTask` with `lead_town` as the goodbye; each gamebot then sells between tasks as at any task's end. Checked every 10 s, never while meeting up, never in a raid party. |
| Pull call | `AutonomousBotCombatCalls` (`PullCallChance`, `LeaderCallsPull`, `SayLeaderPull`) | In a party a gamebot leads (not a raid party) a pull is called 60% of the time (35% elsewhere, unchanged) by the leader with `lead_pull` lines, under the same group gap and meter; only when the leader has no line does a member's "inc" stand in. |
| Chat | `data/chat/bot_led_groups.lines` | `lead_pull` (11 forms), `lead_sell`, `lead_town`, `lead_recruited`, `lead_recruit_none`, `lead_recruit_end` (group), `lead_lf_more` (lfg, `{need}` and `{camp}`); realm-neutral. Fallbacks for each in `BotLedGroups.Fallback`. |

**Decided while building, for the owner to confirm:**
- **(confirm)** The replacement search is for parties that host the player (or did until the player left). Bot-only
  parties keep today's rules (carry on short or end), so the change can't alter the whole population's grouping.
- **(confirm)** Recruits come only from gamebots whose own goal is a group (as formation takes them), never from a
  solo task, a raid, the frontier or a battleground.
- **(confirm)** The search gives up after 5 minutes; the `/lfg` ask is said once per search.
- **(confirm)** When the player leaves a party they followed, it always looks for someone of the player's role (the
  design: "looks for a replacement"); it waits for one, without pulling, when the player was its only healer, tank or
  damage dealer.
- **(confirm)** The natural break ends the party (as every gamebot party's task end does) instead of walking it to town
  and back with the player: a gamebot sells only between tasks, out of a group, so a grouped trip to the merchant would
  need the selling itself rebuilt for groups. The design's wording "last pull then town to sell" is kept as the call.
- **(confirm)** The break comes when half the gamebots' bags are nearly full (6 free slots or fewer, the between-task
  mark), or with 5 minutes of the task left; no pull within 3 minutes of the call, and the party goes anyway.
- **(confirm)** A bot-led party calls 60% of its pulls (35% in a group the player leads).

**Not in B4:** a walk to town and back as a party; potion and repair breaks (gamebots carry no potions and their gear
doesn't wear); recruiting into bot-only parties; the "release, we'll wait" line and the far-side zone-line hold (B1's
list). The recruit's journey, the ask in `/lfg` and the break need the owner's played check: one party losing its
healer, one bag-full break, and listening for one pull call per pull.
