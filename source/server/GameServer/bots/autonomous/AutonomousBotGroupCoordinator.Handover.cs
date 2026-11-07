using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B2 (docs/BOT-LED-GROUPS.md, "Taking the lead"): the coordinator's side of a handover. /lead ends
/// the hosting session without disbanding (the gamebots now follow the player, PlayerGroup lease); /lead bot starts a
/// session for the player's own group (the gamebots lead it, BotLedGroupWithPlayer lease); a hosting party down to one
/// gamebot ends its session without disbanding and the player is asked whether to lead (BotLedGroups.AskToLeadLastBot).
/// The group object is kept every time, so its loot ledger carries on.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>
    /// Ends a session without breaking the group up: its gamebots stay in it, their bot-group leases end, the hosted view
    /// goes, and the coordinator forgets the party (it is then a player-led group, or waiting for the last-bot box).
    /// Under the coordinator's lock.
    /// </summary>
    private static void DetachSession(Session session, string reason)
    {
        session.Ending = true;
        AutonomousDefensivePull.Cancel(session.Group);
        if (session.DungeonBoss is { } boss)
            LogDungeonBossResult(session, boss, "party handed over");
        GameBot[] members = AllBotMembers(session.Group);
        foreach (GameBot bot in members)
        {
            ClearMetadata(bot, true);
            BotDutyLeases.Release(bot, eDutyLeaseKind.BotGroupSession, session.Id, DutyLeaseReason.ActivityEnded);
        }
        session.HostedPlayer = null;
        session.Follow = null;
        BotLedGroups.ClearHosted(session.Group);
        Sessions.Remove(session.Group);
        if (Log.IsInfoEnabled)
            Log.Info($"AUTONOMOUS_GROUP_TASK_ENDED group={session.Id} objective={session.ObjectiveKind} size={members.Length} " +
                     $"members=\"{string.Join(",", members.Select(member => member.Name))}\" reason=\"{reason}\" disbanded=false");
    }

    /// <summary>
    /// /lead: the hosted player takes over. The leader says "all yours" (while the bots still lead, so the line fits), each
    /// gamebot moves to the player's group lease and follows the player, the session ends without disbanding and the
    /// player is made group leader. False with the reason when the player is not hosted here (or in a raid party).
    /// </summary>
    internal static bool TryHandOverToPlayer(GamePlayer player, string why, out string refusal)
    {
        refusal = null;
        Group group = player?.Group;
        lock (Sync)
        {
            if (group == null || !TryGetSession(group, out Session session) || !ReferenceEquals(session.HostedPlayer, player) || session.Ending)
            {
                refusal = "not_bot_led";
                return false;
            }
            if (AutonomousRealmRaid.GetView(group) != null || session.RaidMusterEvent != null || BotLedGroups.ViewOf(group)?.RaidGuest == true)
            {
                refusal = "raid_party";
                return false;
            }
            GameBot[] members = BotMembers(group);
            GameBot leader = ChooseLeader(session, members);
            if (leader != null)
                BotLedGroups.Announce(leader, BotLedGroups.Lead.Handover, player, PlaceOf(leader), session.Camp?.MonsterName);
            // The player's group lease takes the bot-led one in one step (BotDutyLeaseRules.Decide: a player's group always
            // gets the bot), so no raid or RvR event can take a bot in between.
            foreach (GameBot bot in members)
                bot.EnterPlayerLedGroup(player);
            string id = session.Id;
            DetachSession(session, $"handed over to {player.Name} ({why})");
            BotDutyLeases.ReleaseOwner(eDutyLeaseKind.BotLedGroupWithPlayer, id, DutyLeaseReason.LeftGroup);
            group.MakeLeader(player);
            BotLedGroups.LogHandover("to_player", player, leader, why, why, id);
            return true;
        }
    }

    /// <summary>
    /// The roles of a party built around the player's own group: the bots' own roster when they fit one alone
    /// (TryAssignExactPveRoles), else each by its class (healer, tank, hitter), the player counting for their own class.
    /// </summary>
    private static Dictionary<long, BotPveGroupRole> RolesForHandBack(GameBot[] bots, GameBot leader)
    {
        if (TryAssignExactPveRoles(bots, leader, out Dictionary<long, BotPveGroupRole> exact))
            return exact;
        return bots.Where(bot => bot.CharacterClass != null)
            .ToDictionary(MemberKey, bot => BotLedGroups.NaturalRole((eCharacterClass)bot.CharacterClass.ID));
    }

    /// <summary>
    /// /lead bot (or /makeleader on a gamebot): a session starts for the player's existing group, led by
    /// <paramref name="leader"/>. It starts where the group stands ("that bot picks a camp near where you stand"): no
    /// meetup, straight to choosing a camp. The gamebots move from the player's group lease to the bot-led one in one
    /// step and stop following the player; the player is hosted, with the meetup done. False with the reason when the
    /// group can't run as a bot-led party (no tank, healer and hitter, the player counting; nowhere to start from).
    /// </summary>
    internal static bool TryHandBackToBot(GamePlayer player, GameBot leader, out string refusal)
    {
        refusal = null;
        Group group = player?.Group;
        if (group == null || leader?.Group != group || leader.CurrentRegion == null)
        {
            refusal = "party_gone";
            return false;
        }
        lock (Sync)
        {
            if (TryGetSession(group, out _))
            {
                refusal = "not_leader";
                return false;
            }
            GameBot[] bots = BotMembers(group);
            if (bots.Length < BotLedGroups.MinimumBotsToLead || !bots.Contains(leader))
            {
                refusal = "too_few_bots";
                return false;
            }
            long now = GameLoop.GameLoopTime;
            var session = new Session
            {
                Group = group,
                Leader = leader,
                Id = SessionId(leader.Realm, ++_nextGroupNumber),
                Rendezvous = new Vector3(leader.X, leader.Y, leader.Z),
                RendezvousName = PlaceOf(leader),
                RendezvousRegion = leader.CurrentRegionID,
                TaskClock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.GroupPve),
                LockedSize = bots.Length,
                PreferredLevelBonus = RollPreferredLevelBonus(bots.Length),
                ObjectiveKind = eAutonomousObjectiveKind.GroupPve,
                LeaderStagingDeadlineTick = now + LeaderStagingTimeoutMilliseconds,
                LeaderStagingDeadlineUtc = DateTime.UtcNow.AddMilliseconds(LeaderStagingTimeoutMilliseconds),
                Phase = "Choosing group target",
                LeaderReadyForAssembly = true,
                MeetupPlanned = true,
                FromPlayersGroup = true,
                HadHostedPlayer = true,
                HostedPlayer = player,
                PlayerRole = player.CharacterClass == null ? BotPveGroupRole.Attacker
                    : BotLedGroups.PlayerRole((eCharacterClass)player.CharacterClass.ID),
            };
            foreach ((long key, BotPveGroupRole role) in RolesForHandBack(bots, leader))
                session.PveRoles[key] = role;
            if (!HasViablePveComposition(session, bots))
            {
                refusal = "no_core_roster";
                return false;
            }
            if (!TryBuildRendezvousSlots(session, bots))
            {
                refusal = "no_place";
                return false;
            }
            session.Puller = ChooseLockedPvePuller(session, bots);
            session.Recovery.Observe(RecoveryMembers(session, bots, false), false);
            session.Follow = new BotLedGroups.FollowState(now, meetupDone: true);
            session.PlayerLastPosition = new Vector3(player.X, player.Y, player.Z);
            session.PlayerLastRegion = player.CurrentRegionID;
            foreach (GameBot bot in bots)
            {
                // From "in the player's group" to "a bot-led group with the player" in one step: never free on the way.
                if (!BotDutyLeases.HandOver(bot, eDutyLeaseKind.PlayerGroup, null, eDutyLeaseKind.BotLedGroupWithPlayer, session.Id,
                        DutyLeaseReason.HostingPlayer, player.Name, group))
                    BotDutyLeases.Claim(bot, eDutyLeaseKind.BotLedGroupWithPlayer, session.Id, DutyLeaseReason.HostingPlayer, player.Name, group);
                bot.LeavePlayerLedGroup();
                if (bot.Brain is DOL.AI.Brain.BotBrain brain)
                    brain.FSM.SetCurrentState(eFSMStateType.IDLE);
            }
            Sessions[group] = session;
            BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, session.Id));
            group.MakeLeader(leader);
            BotLedGroups.Announce(leader, BotLedGroups.Lead.TakeBack, player, session.RendezvousName, null);
            WriteSessionMetadata(session, bots);
            BotLedGroups.LogHandover("to_bot", player, leader, "lead_bot", "lead_bot", session.Id);
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_LED_GROUP_HOSTING group={session.Id} player=\"{player.Name}\" level={player.Level} role={session.PlayerRole} " +
                         $"leader=\"{leader.Name}\" phase=\"{session.Phase}\" bots={bots.Length} from=players_group");
            return true;
        }
    }

    /// <summary>
    /// A party that hosts the player is ending with one gamebot left (the design's "fewer than two bots remain"): instead
    /// of breaking up, the session ends without disbanding and the player gets a yes/no box. The gamebot keeps its hold
    /// (bound to the group) until the player answers. True when the box is offered (the caller's ending is done).
    /// </summary>
    private static bool TryOfferLeadToLastBot(Session session, string reason)
    {
        GameBot[] bots = BotMembers(session.Group);
        bool raidParty = AutonomousRealmRaid.GetView(session.Group) != null || session.RaidMusterEvent != null;
        raidParty |= BotLedGroups.ViewOf(session.Group)?.RaidGuest == true;
        if (!BotLedGroups.OffersLeadOnEnd(HostedPlayerPresent(session), raidParty, bots.Length))
            return false;
        GamePlayer player = session.HostedPlayer;
        GameBot bot = bots[0];
        BotLedGroups.Announce(bot, BotLedGroups.Lead.LastBot, player, PlaceOf(bot), null);
        DetachSession(session, $"{reason}; asking {player.Name} to lead");
        BotLedGroups.AskToLeadLastBot(session.Group, bot, player);
        return true;
    }

    /// <summary>The player said yes to leading the last gamebot: it follows the player, who leads the group.</summary>
    internal static bool MakeLastBotFollow(Group group, GameBot bot, GamePlayer player)
    {
        lock (Sync)
        {
            if (group == null || bot?.Group != group || player?.Group != group || TryGetSession(group, out _))
                return false;
            if (!bot.EnterPlayerLedGroup(player))
                return false;
            group.MakeLeader(player);
            return true;
        }
    }
}
