# The duty lease (Living Server 1.2, phase A)

Roadmap line: *raids and RvR events claim a bot's time with a priority; the watchdog exempts leased bots; reason
codes; `/lswhy` for bot decisions.* It comes before bot-led groups ([BOT-LED-GROUPS.md](BOT-LED-GROUPS.md), decision
5), whose "a bot in your group can't be taken" is one lease kind, not a second flag.

Code: `bots/autonomous/BotDutyLease.cs` (kinds, reason codes, the pure rules), `BotDutyLeases.cs` (the record).
Tests: `UT_DutyLeaseCurrentRules` (the rules as they were, written first and unchanged since), `UT_BotDutyLeaseRules`,
`UT_DutyLease*` (one per migration). Automated only: nothing here has been played in game.

## The record

Each **autonomous gamebot** holds at most one lease: kind, owner (raid id, RvR force, rally, group session, player),
priority, since, reason code, optional expiry, optional group. Real players are never leased; `/spawn` companions are
not either (their owner rules stay as they are).

| Kind | Priority | Claimed by | Ends |
|---|---|---|---|
| `PlayerGroup` | 100 | `GameBot.EnterPlayerLedGroup` (invite, handover) | `LeavePlayerLedGroup`, logout |
| `BotLedGroupWithPlayer` | 90 | a gamebot party that took in the player (`hosting_player`, bound to the group; BOT-LED-GROUPS.md, B1) | the player leaves, the bot leaves the party, the party disbands, logout |
| `RealmRaid` | 40 | raid reservation (`raid_reserved`), formed/joined party or replacement (`raid_party`, bound to the group) | released member, party leaves the raid, raid ends, logout |
| `RvrEvent` | 30 | defense response (`rvr_force`), siege party (`siege_party`, bound to the group) | `RemoveForce`, the event or relic escort ends, a superseded siege's defenders |
| `BattlegroundWave` | 30 | a gamebot going up with its realm's keep rally (`bg_rally`) | regroup, new keep holder, lapsed rally, death, released |
| `BotGroupSession` | 10 | the coordinator publishing a bot-only party (`group_session`, bound to the group) | member leaves, party disbands |

**Rules** (`BotDutyLeaseRules.Decide`): a player's group always gets the bot (the invite checks the bot's duty first:
dead, siege crew, relic, RvR march, raid reservation, stable route) and is never taken by anything automatic; a
bot-led group hosting a player takes only from its own bot group; otherwise the higher priority takes the lower,
equals of the same kind move the bot to the newer claim, and equals of different kinds (an RvR event and a battleground
rally, equal by the owner's decision of 2026-11-04) leave the bot where it is; a lower claim is refused with the holder's reason (`in_players_group`,
`hosting_player`, `on_realm_raid`, `on_rvr_event`, `on_battleground`, `in_bot_group`).

**Endings:** a group lease ends when the bot leaves that group; only a rally lease ends on death (the others' activities
keep their dead: rezzes, raid rejoin, the player's group); **nothing persists**: every claimant (raids, RvR events,
rallies, groups) lives in memory and is rebuilt from nothing at start-up, so a saved lease would point at nothing.
Logout (`AutonomousBotRegistry.Unregister`) ends any lease.

**Visible:** `/lswhy duty [name]` (lease + last 6 decisions), plain `/lswhy` on a bot (lease + last 3 under its chat
decisions). `DUTY_LEASE` log lines: granted, taken, changed, refused (once per distinct refusal), ended.

## Where "busy / may be taken" was decided, and where it is now

| Decision | Before | Now |
|---|---|---|
| Raid eligibility (`AutonomousRealmRaid.IsEligible`, all raid planning) | `IsPlayerLedGroup` | lease: spoken for by a player |
| Raid reservations (`IsReserved`, `IsReservedFor`, `ForcedParties`, Special Forces) | id → raid map | `RealmRaid` lease, `raid_reserved` stage |
| Raid party membership (`Membership`, views, `Protects`) | group → raid map | unchanged; the party lease mirrors it |
| Siege crews (`AutonomousSiegeJobs.Eligible`) | `IsPlayerLedGroup` | lease |
| Siege parties (`FormSiegeParties`) | `IsPlayerLedGroup`, `IsReserved` | lease (both) |
| RvR defense responders and their parties | `IsPlayerLedGroup` | lease |
| Objective rebalancing roster | `IsPlayerLedGroup` | lease |
| Frontier transport (siege plan, boarding) | `IsPlayerLedGroup` | lease |
| Merchant fallback (`TryClearBlockedBackpackAtMerchant`) | `IsPlayerLedGroup` | lease |
| Group matchmaking (`TryFormGroups`) | `IsPlayerLedGroup`, `IsReserved` | lease (both) |
| Battleground rally members (`IsMember`) | `IsPlayerLedGroup` | lease; stormers hold `BattlegroundWave` |
| Invite duty (`BotGroupInvite.DutyOf`) | force tag, `IsReserved` | `RvrEvent` lease (tag still read for solo forces), raid lease |
| Watchdog: never relocate a bot a player has (`HasHumanCompany`) | `IsPlayerLedGroup` | lease kind exempts (`ExemptsFromWatchdog`) |
| Watchdog holds: raid staging/rally, RvR participation, group holds, town idle | activity checks | unchanged (see below) |

**Not changed on purpose.** `IsPlayerLedGroup` still means "follows the player" in its other 58 reads: `BotBrain` (29: follow,
assist, pulls, buffs, songs, rezzes, ambient wander), `CompanionFollowPolicy`, `BotRangedCombat`,
`BotIncomingFireReaction`, `BotAnimistPolicy`, `BotGroupRelease` (return to party, portal keep), `GameBot` (release
hand-over), `TemporaryGroupStableTravel`, the controller (`Tick`, release return, Savage pulls, frontier threats, route
threats), the group coordinator's player-led answers (`IsRecovering`, `IsInitialMeetup`,
`ProtectsFromIndividualWatchdog`, `EndPvePartyBeforeIndividualRecovery`, `CanInitiateNewPull`), group motion, ground
travel, formation follow, member stall, tether reset, goal diagnostics, voices and chat. In phase B a bot-led group
hosting a player is *not* player-led for these: the bot leads.

**Watchdog.** Only the player kinds exempt a bot by themselves. A raid, RvR or group lease does not: a raider stuck on
its way must still be rescued. Their holds stay with the activity, which knows when the bot is legitimately waiting
or fighting (`AutonomousRealmRaid.Protects` / `IsInRally`, `AutonomousRvrEventLayer.ProtectsParticipantFromInactivity`,
`AutonomousBotGroupCoordinator.ProtectsFromIndividualWatchdog`).

## Intended differences

- A raid recruit that logged out before its party formed stayed reserved by id and, back online, sat out every raid,
  siege party and bot group until the raid ended. A lease ends at logout.
- A bot holding a raid lease that is sent on an RvR march keeps the raid lease (a raid outranks the frontier): an
  invite is refused as "committed to a realm raid" instead of "marching with a realm force". Both refuse.

## Left for later

- Solo RvR forces (`rvr-<id>`) that join events through the planner (`ChooseOrJoin`, the controller's per-tick force
  tag) hold no lease; `IsForceCommitted` / `IsEventForce` on the tag still decide for them.
- Siege weapon jobs (`BotSiegeRuntime.Assigned`), relic carriers, stable routes and the Epic Special Forces' own
  lifetime keep their flags; the invite still reads them.
- The battleground wave (`AutonomousBattlegroundWave`) counts bots by objective, not by claim; only the keep rally
  leases.
