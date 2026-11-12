using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Bot-led groups (docs/BOT-LED-GROUPS.md, B1, change 2): a coordinated PvE party that hosts the real player. The party
/// is still run by its gamebot leader exactly as a bot-only party; the player is counted where the design says (the
/// meetup, the pace, the pull gate, the roster, the camp level) through <see cref="BotLedGroups.FollowState"/> and the
/// lock-free <see cref="BotLedGroups.HostedView"/>. The player is never the leader, never leased and never serialized.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private static readonly HashSet<string> HostingPhases = new(StringComparer.Ordinal)
    {
        "Meeting up", "Choosing group target", "Traveling", "Grinding",
    };

    /// <summary>The only real players in the group are the one this session hosts (and there is one).</summary>
    private static bool OnlyHostedPlayer(Group group, Session session)
    {
        List<GamePlayer> players = group.GetPlayersInTheGroup();
        return players.Count == 1 && session?.HostedPlayer != null && ReferenceEquals(players[0], session.HostedPlayer);
    }

    /// <summary>A place a player gets to on foot or by horse: not a frontier, battleground or Darkness Falls.</summary>
    private static bool ReachableForPlayer(ushort regionId, bool frontier)
    {
        Region region = WorldMgr.GetRegion(regionId);
        return region != null && !frontier && !region.IsRvR && !BattlegroundBrackets.IsBattlegroundRegion(regionId) &&
               regionId != AutonomousDarknessFallsPolicy.RegionId;
    }

    private static BotLedGroups.Candidate CandidateOf(Session session, GameBot[] members, GamePlayer player)
    {
        GameBot leader = ChooseLeader(session, members);
        SharedCamp camp = session.Camp ?? session.PlannedCamp;
        bool reachable = leader != null && ReachableForPlayer(leader.CurrentRegionID, leader.CurrentZone?.IsRvR == true) &&
                         (camp == null || ReachableForPlayer(camp.RegionId, camp.IsFrontier));
        bool onDuty = AutonomousRealmRaid.GetView(session.Group) != null ||
                      members.Any(member => !BotDutyLeases.WouldGrant(member, eDutyLeaseKind.BotLedGroupWithPlayer, session.Id));
        bool hunting = session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve && !session.Ending &&
                       HostingPhases.Contains(session.Phase) && !session.Recovery.IsRegrouping && leader?.IsAlive == true &&
                       leader.IsAutonomousWorldBot && !leader.IsTemporaryGroupHelper;
        double distance = leader != null && leader.CurrentRegionID == player.CurrentRegionID ? leader.GetDistanceTo(player) : -1;
        return new BotLedGroups.Candidate(leader?.DatabaseID ?? 0, leader?.Name ?? string.Empty, leader?.Realm ?? eRealm.None,
            members.Length == 0 ? 0 : (int)Math.Round(members.Average(member => member.Level)),
            session.Group.MemberCount, session.Group.MaximumMemberCount,
            session.PveRoles.Values.Any(role => role == BotPveGroupRole.Healer),
            hunting, onDuty, reachable, session.Group.GetPlayersInTheGroup().Count > 0, distance);
    }

    /// <summary>
    /// The leader whose party invites the player now (BotLedGroups.PickInvitingLeader over every coordinated party of
    /// the player's realm). With <paramref name="askedBot"/> (the player asked that bot) only its own party is
    /// considered.
    /// </summary>
    internal static bool TryPickHostingLeader(GamePlayer player, BotLedGroups.InviteMemory memory, long now, GameBot askedBot,
        out GameBot leader, out BotLedGroups.Candidate party)
    {
        leader = null;
        party = default;
        if (player == null)
            return false;
        bool healer = player.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)player.CharacterClass.ID);
        var leaders = new Dictionary<long, GameBot>();
        var candidates = new List<BotLedGroups.Candidate>();
        lock (Sync)
        {
            foreach (Session session in Sessions.Values)
            {
                if (askedBot != null && session.Group != askedBot.Group)
                    continue;
                GameBot[] members = BotMembers(session.Group);
                if (members.Length == 0 || members[0].Realm != player.Realm)
                    continue;
                BotLedGroups.Candidate candidate = CandidateOf(session, members, player);
                GameBot chosen = ChooseLeader(session, members);
                if (chosen == null)
                    continue;
                leaders[candidate.LeaderId] = chosen;
                candidates.Add(candidate);
            }
        }
        if (BotLedGroups.PickInvitingLeader(candidates, player.Realm, player.Level, healer, memory, now) is not { } picked ||
            !leaders.TryGetValue(picked.LeaderId, out leader))
            return false;
        party = picked;
        return true;
    }

    /// <summary>The leader still leads a party that could take a player (just before the box is shown).</summary>
    internal static bool StillHosting(GameBot leader)
    {
        if (leader?.Group == null)
            return false;
        lock (Sync)
        {
            if (!TryGetSession(leader.Group, out Session session) || session.HostedPlayer != null)
                return false;
            GameBot[] members = BotMembers(session.Group);
            return ChooseLeader(session, members) == leader && session.Group.MemberCount < session.Group.MaximumMemberCount &&
                   session.Group.GetPlayersInTheGroup().Count == 0 && !session.Ending &&
                   HostingPhases.Contains(session.Phase);
        }
    }

    /// <summary>
    /// The player said yes: the player joins the leader's group as a member (never its leader), the session hosts them,
    /// and every gamebot of the party holds the "bot-led group with a player" lease, which nothing automatic takes.
    /// </summary>
    internal static bool TryHostPlayer(GameBot leader, GamePlayer player, out string why, bool welcome = true)
    {
        why = null;
        if (leader?.Group == null || player == null)
        {
            why = "party_gone";
            return false;
        }
        lock (Sync)
        {
            if (!TryGetSession(leader.Group, out Session session))
            {
                why = "party_gone";
                return false;
            }
            GameBot[] members = BotMembers(session.Group);
            BotLedGroups.Candidate candidate = CandidateOf(session, members, player);
            why = BotLedGroups.CandidateBlocks(candidate, player.Realm, player.Level);
            if (why == null && ChooseLeader(session, members) != leader)
                why = "leader_changed";
            if (why != null)
                return false;
            if (!session.Group.AddMember(player))
            {
                why = "full";
                return false;
            }
            long now = GameLoop.GameLoopTime;
            session.HostedPlayer = player;
            session.HadHostedPlayer = true;
            session.PlayerRole = player.CharacterClass == null ? BotPveGroupRole.Attacker
                : BotLedGroups.PlayerRole((eCharacterClass)player.CharacterClass.ID);
            session.Follow = new BotLedGroups.FollowState(now);
            session.PlayerLastPosition = new Vector3(player.X, player.Y, player.Z);
            session.PlayerLastRegion = player.CurrentRegionID;
            BotLedGroups.SetHosted(session.Group, new BotLedGroups.HostedView(player, session.Id) { HoldsPace = true, BlocksPull = true });
            foreach (GameBot member in members)
                BotDutyLeases.Claim(member, eDutyLeaseKind.BotLedGroupWithPlayer, session.Id, DutyLeaseReason.HostingPlayer, player.Name,
                    session.Group);
            bool assembling = IsAssemblyPhase(session.Phase);
            // With bot_led_group_join_mode = lead the player takes over at once; the handover says "all yours" instead.
            if (welcome)
                BotLedGroups.Announce(leader, assembling ? BotLedGroups.Lead.Welcome : BotLedGroups.Lead.WelcomeOnTheWay, player,
                    assembling ? session.RendezvousName : PlaceOf(leader), session.Camp?.MonsterName);
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_LED_GROUP_HOSTING group={session.Id} player=\"{player.Name}\" level={player.Level} role={session.PlayerRole} " +
                     $"leader=\"{leader.Name}\" phase=\"{session.Phase}\" bots={members.Length}");
            WriteSessionMetadata(session, members);
            return true;
        }
    }

    private static string PlaceOf(GameBot bot) => bot?.CurrentZone?.Description ?? bot?.CurrentRegion?.Description ?? "here";

    /// <summary>Where the leader's party hunts (its camp's zone, or where it meets up), for the invite tell.</summary>
    internal static string HuntingPlace(GameBot leader)
    {
        if (leader?.Group == null)
            return null;
        lock (Sync)
        {
            if (!TryGetSession(leader.Group, out Session session))
                return null;
            SharedCamp camp = session.Camp ?? session.PlannedCamp;
            return !string.IsNullOrWhiteSpace(camp?.ZoneName) ? camp.ZoneName
                : !string.IsNullOrWhiteSpace(session.RendezvousName) ? session.RendezvousName : null;
        }
    }

    /// <summary>What the leader's party hunts (its camp's monster), or null before it has a camp.</summary>
    internal static string HuntingTarget(GameBot leader)
    {
        if (leader?.Group == null)
            return null;
        lock (Sync)
            return TryGetSession(leader.Group, out Session session) ? (session.Camp ?? session.PlannedCamp)?.MonsterName : null;
    }

    /// <summary>
    /// One pass of the hosted player (from UpdateSession, under the lock): observe the player, step the follow rules,
    /// publish the hosted view, say what the leader says and let the player go when the rules do. False when the player
    /// is no longer hosted (the caller carries on as a bot-only party).
    /// </summary>
    private static bool StepHostedPlayer(Session session, GameBot[] members, GameBot leader)
    {
        GamePlayer player = session.HostedPlayer;
        if (player == null)
            return false;
        long now = GameLoop.GameLoopTime;
        // A zone or region change (or a GM /jump) takes the player out of the world for the seconds the client loads; the
        // player is still in the group and has not left it (PC test 2026-11-07: let go 80 s after accepting, with only a
        // /jump to the leader and a /follow in between).
        bool away = player.ObjectState != GameObject.eObjectState.Active && player.Client?.ClientState != GameClient.eClientState.Linkdead;
        if (!away) session.PlayerAwaySince = 0;
        else if (session.PlayerAwaySince == 0) session.PlayerAwaySince = now;
        if (BotLedGroups.HostedPlayerLeft(player.Group == session.Group, away, session.PlayerAwaySince, now))
        {
            EndHosting(session, player, "left");
            return false;
        }
        if (away)
            return true;
        var position = new Vector3(player.X, player.Y, player.Z);
        // Each member's pulse runs this, many times a second: "moved" is measured from the last spot the player was seen
        // to leave, not from the last pass.
        bool moved = Vector3.DistanceSquared(position, session.PlayerLastPosition) >= BotLedGroups.ActivityMoveUnits * BotLedGroups.ActivityMoveUnits ||
                     player.CurrentRegionID != session.PlayerLastRegion;
        if (moved)
        {
            session.PlayerLastPosition = position;
            session.PlayerLastRegion = player.CurrentRegionID;
        }
        bool sameRegion = leader != null && player.CurrentRegionID == leader.CurrentRegionID;
        var seen = new BotLedGroups.Observation(
            SameRegion: sameRegion,
            DistanceToLeader: sameRegion ? player.GetDistanceTo(leader) : float.MaxValue,
            Alive: player.IsAlive,
            Sitting: player.IsSitting,
            ResourcesReady: ReadyForGroupPull(player.HealthPercent, player.ManaPercent, player.EndurancePercent, player.MaxMana > 0),
            AfkFlag: player.TempProperties.GetProperty<string>(GamePlayer.AFK_MESSAGE) != null,
            Moved: moved || player.IsCasting,
            InCombat: player.InCombat,
            Linkdead: player.Client?.ClientState == GameClient.eClientState.Linkdead,
            MeetingUp: IsAssemblyPhase(session.Phase),
            GroupFighting: members.Any(member => member.InCombat));
        BotLedGroups.Say said = session.Follow.Step(seen, now);
        if (BotLedGroups.ViewOf(session.Group) is { } view)
        {
            view.HoldsPace = session.Follow.HoldsPace;
            view.BlocksPull = session.Follow.BlocksPull;
            view.BlocksNewTravel = session.Follow.BlocksNewTravel;
        }
        string camp = session.Camp?.MonsterName ?? session.PlannedCamp?.MonsterName;
        string place = IsAssemblyPhase(session.Phase) ? session.RendezvousName : PlaceOf(leader);
        switch (said)
        {
            case BotLedGroups.Say.MeetupReminder:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.MeetupReminder, player, place, camp);
                break;
            case BotLedGroups.Say.MeetupGoingOn:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.MeetupGoingOn, player, place, camp);
                break;
            case BotLedGroups.Say.WhereAreYou:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.WhereAreYou, player, place, camp);
                break;
            case BotLedGroups.Say.GoingOn:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.GoingOn, player, place, camp);
                if (Log.IsInfoEnabled)
                    Log.Info($"BOT_LED_GROUP_GOING_ON group={session.Id} player=\"{player.Name}\" reason=not_getting_closer");
                break;
            case BotLedGroups.Say.AfkHold:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.AfkHold, player, place, camp);
                break;
            case BotLedGroups.Say.AfkLetGo:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.AfkLetGo, player, place, camp);
                session.Group.RemoveMember(player);
                EndHosting(session, player, "afk_30_minutes");
                return false;
            case BotLedGroups.Say.LinkdeadLeft:
                BotLedGroups.Announce(leader, BotLedGroups.Lead.PlayerLeft, player, place, camp);
                session.Group.RemoveMember(player);
                EndHosting(session, player, "linkdead_5_minutes");
                return false;
        }
        return true;
    }

    /// <summary>The player is no longer hosted: the party carries on as a bot-only party and its members' leases go back.</summary>
    private static void EndHosting(Session session, GamePlayer player, string reason)
    {
        if (session?.HostedPlayer == null)
            return;
        // B3: a raid guest's seat goes back to the raid, which refills it with a gamebot.
        if (BotLedGroups.ViewOf(session.Group)?.RaidGuest == true || AutonomousRealmRaid.GetView(session.Group) != null)
            AutonomousRealmRaid.OnPlayerLeftParty(session.Group);
        session.HostedPlayer = null;
        session.Follow = null;
        BotLedGroups.ClearHosted(session.Group);
        int released = BotDutyLeases.ReleaseOwner(eDutyLeaseKind.BotLedGroupWithPlayer, session.Id, DutyLeaseReason.LeftGroup);
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_PLAYER_LEFT group={session.Id} player=\"{player?.Name}\" reason={reason} leasesReleased={released}");
    }

    /// <summary>The hosted player left the group (/disband, logout, a let-go): the party goes on without them.</summary>
    private static void OnHostedPlayerRemoved(Group group, GamePlayer player)
    {
        lock (Sync)
        {
            if (group != null && Sessions.TryGetValue(group, out Session session) && ReferenceEquals(session.HostedPlayer, player))
            {
                bool raidGuest = BotLedGroups.ViewOf(group)?.RaidGuest == true;
                BotPveGroupRole role = session.PlayerRole;
                EndHosting(session, player, "left_group");
                // B4: the party carries on without the player and looks for a replacement of the player's role (it waits
                // for one, no pulls, when the player was its only healer, tank or damage dealer).
                GameBot[] stayed = BotMembers(group);
                if (!raidGuest && stayed.Length >= 2 && session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve)
                    TryStartReplacement(session, role, stayed, HasViablePveComposition(session, stayed), player.Name);
            }
        }
    }

    /// <summary>The hosted player's role still counts for the roster's tank, healer and attacker core.</summary>
    private static bool HostedPlayerPresent(Session session) => session?.HostedPlayer is { } player && player.Group == session.Group;

    /// <summary>
    /// The directive's level for a hosted party: the group's level counts the player and no camp is more than six levels
    /// above the player (docs/BOT-LED-GROUPS.md, decision 3).
    /// </summary>
    private static (int AverageLevel, int Bonus) HostedLevels(Session session, GameBot[] members)
    {
        int botAverage = members.Length == 0 ? 0 : (int)Math.Round(members.Average(member => member.Level));
        if (!HostedPlayerPresent(session))
            return (botAverage, session.PreferredLevelBonus);
        int playerLevel = session.HostedPlayer.Level;
        int groupLevel = BotLedGroups.GroupLevelWithPlayer(members.Select(member => (int)member.Level).ToArray(), playerLevel);
        return BotLedGroups.HostedCampBand(groupLevel, session.PreferredLevelBonus, playerLevel);
    }

    /// <summary>The hosted player's party has not finished the meetup for the player's part.</summary>
    private static bool HostedPlayerHoldsMeetup(Session session) =>
        HostedPlayerPresent(session) && session.Follow != null && !session.Follow.ReadyToLeaveMeetup;

    /// <summary>The group that hosts this gamebot's player (lock-free, for movement and the watchdog).</summary>
    public static bool IsHostedParty(GameBot bot) => BotLedGroups.IsHostedGroup(bot?.Group);
}
