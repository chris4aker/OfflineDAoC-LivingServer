using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Movement;
using DOL.GS.PacketHandler;

namespace DOL.GS;

/// <summary>
/// Keeps ephemeral /spawn companions with their real-player leader over
/// authoritative travel, and brings an autonomous gamebot in the player's group
/// along when it stood beside the player at departure (TryBringGroupmateAlong).
/// It never considers money, inventory, or ticket ownership.
/// </summary>
public static class TemporaryGroupStableTravel
{
    private const int CompanionBoardingRadius = 650;
    private const int BoardAfterPlayerMountMilliseconds = 550;
    private const int PlayerRouteStartMilliseconds = 4000;
    private const int CompanionStaggerMilliseconds = 125;
    private const int FormationRadius = 180;
    private const int RouteAbortGraceMilliseconds = 5000;
    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(TemporaryGroupStableTravel));
    private static readonly TimeSpan TransferMarkerLifetime = TimeSpan.FromSeconds(30);

    private sealed class RouteMonitor
    {
        public required GamePlayer Player;
        public required GameBot[] Companions;
        public required DateTime ExpectedArrivalUtc;
        public required DateTime StartedUtc;
        public int Queued;
    }

    private static readonly ConcurrentDictionary<GamePlayer, RouteMonitor> ActiveRoutes = new();
    // Zone/region differences occur during ordinary follower movement.  A marker
    // is created only by a successful GamePlayer.MoveTo, then consumed once the
    // player's temporary helpers have joined that completed transfer.
    private static readonly ConcurrentDictionary<GamePlayer, DateTime> PendingPlayerTransfers = new();
    private static Timer _monitorTimer;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        _monitorTimer?.Dispose();
        _monitorTimer = new Timer(PollRoutes, null, 250, 250);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _monitorTimer?.Dispose();
        _monitorTimer = null;
        ActiveRoutes.Clear();
        PendingPlayerTransfers.Clear();
    }

    /// <summary>
    /// Called only after GameStableMaster accepted and consumed the real
    /// player's ticket.  Companion rides cost nothing because helpers have no
    /// persistent purse or inventory; the player's normal transaction remains
    /// the sole authority for the route.
    /// </summary>
    public static void QueueMirroredStableRoute(GamePlayer player, PathPoint route, DbItemTemplate ticket)
    {
        if (player == null || route == null || ticket == null)
            return;

        ECSGameTimer boardAfterPlayer = new(player, _ =>
        {
            StartMirroredStableRoute(player, route, ticket);
            return 0;
        });
        boardAfterPlayer.Start(BoardAfterPlayerMountMilliseconds);
    }

    public static bool IsEligibleForMirroredRide(
        bool isTemporaryHelper,
        bool isAlive,
        bool isActive,
        bool sharesGroup,
        bool followsPlayer,
        int distanceToPlayer) =>
        isTemporaryHelper && isAlive && isActive && sharesGroup && followsPlayer &&
        distanceToPlayer <= CompanionBoardingRadius;

    public static bool ShouldRelocateForOwnerTransfer(
        bool hasConfirmedPlayerTransfer,
        bool isTemporaryHelper,
        bool sharesGroup,
        bool followsPlayer,
        ushort companionRegion,
        ushort playerRegion,
        ushort companionZone,
        ushort playerZone) =>
        hasConfirmedPlayerTransfer && isTemporaryHelper && sharesGroup && followsPlayer;

    /// <summary>Gamebots in the player's group within this distance of where the player left go along, as through the same portal.</summary>
    public const int GroupmateTravelRadius = 1_500;

    private static readonly ConcurrentDictionary<GamePlayer, (ushort Region, int X, int Y, DateTime Utc)> TransferOrigins = new();

    /// <summary>Called as a player's MoveTo starts. Only a living player's trip counts (a release is not one).</summary>
    public static void MarkTransferOrigin(GamePlayer player)
    {
        if (player?.IsAlive == true && player.Group != null && player.ObjectState == GameObject.eObjectState.Active)
            TransferOrigins[player] = (player.CurrentRegionID, player.X, player.Y, DateTime.UtcNow);
    }

    /// <summary>Whether a gamebot was beside the player when the player left. Pure.</summary>
    public static bool WasBesideDeparture(ushort botRegion, int botX, int botY, ushort originRegion, int originX, int originY, bool freeToGo) =>
        freeToGo && botRegion == originRegion &&
        (long)(botX - originX) * (botX - originX) + (long)(botY - originY) * (botY - originY) <= (long)GroupmateTravelRadius * GroupmateTravelRadius;

    /// <summary>
    /// Play test 2026-10-27: an autonomous gamebot invited into the player's group stayed behind whenever the player took a
    /// porter, so it ended up in another region with no way to follow. A gamebot that was beside the player when the player
    /// left (and free: alive, not fighting, not on a duty) arrives with the player, as a real groupmate taking the same
    /// portal would. A gamebot elsewhere stays where it is.
    /// </summary>
    public static bool TryBringGroupmateAlong(GameBot bot)
    {
        if (bot is not { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsOnStableMasterRoute: false, IsAlive: true } ||
            bot.PlayerGroupLeader is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active } player ||
            player.Group == null || bot.Group != player.Group || player.CurrentZone == null ||
            !TransferOrigins.TryGetValue(player, out var origin) || DateTime.UtcNow - origin.Utc > TransferMarkerLifetime)
            return false;
        // Already there (or the player moved only a little).
        if (bot.CurrentRegionID == player.CurrentRegionID && bot.IsWithinRadius(player, GroupmateTravelRadius))
            return false;
        bool free = !bot.InCombat && BotGroupInvite.DutyOf(bot) == BotGroupInvite.Duty.None;
        if (!WasBesideDeparture(bot.CurrentRegionID, bot.X, bot.Y, origin.Region, origin.X, origin.Y, free))
            return false;
        bot.StopCurrentSpellcast();
        bot.StopMoving();
        Vector3 destination = FormationPoint(player, bot.ObjectID % 6);
        if (!bot.MoveTo(player.CurrentRegionID, (int)Math.Round(destination.X), (int)Math.Round(destination.Y),
                (int)Math.Round(destination.Z), player.Heading))
            return false;
        bot.EndReturnJourney();
        if (bot.Brain is BotBrain brain)
            brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
        return true;
    }

    public static void MarkAcceptedPlayerTransfer(GamePlayer player)
    {
        if (player != null)
            PendingPlayerTransfers[player] = DateTime.UtcNow;
    }

    private static void StartMirroredStableRoute(GamePlayer player, PathPoint route, DbItemTemplate ticket)
    {
        // The delayed callback ensures the native stable master mounted the
        // player first.  Cancel cleanly if that boarding was interrupted.
        if (player?.ObjectState is not GameObject.eObjectState.Active || !player.IsAlive ||
            player.Steed == null || !player.IsRiding || player.Group == null ||
            !AutonomousStableRoutePlanner.TryMeasureRide(route, out _, out double rideSeconds))
            return;

        // Owner 2026-10-31: companions that followed the horse on foot (too far to board, or a ride that failed) died on
        // the way. Every companion of the player's group (/spawn helpers, at any
        // distance) is now put away while the player rides: gone from view, untouchable, idle. They come back beside the
        // player when the player gets off, at the end of the route or by jumping off early.
        DateTime started = DateTime.UtcNow;
        GameBot[] companions = player.Group.GetMembersInTheGroup()
            .OfType<GameBot>()
            .Where(bot => bot.IsTemporaryGroupHelper &&
                          bot.IsAlive && bot.ObjectState is GameObject.eObjectState.Active &&
                          bot.Group == player.Group && bot.PlayerGroupLeader == player)
            .ToArray();
        if (companions.Length == 0)
            return;

        foreach (GameBot bot in companions)
            Stow(bot);

        ActiveRoutes[player] = new RouteMonitor
        {
            Player = player,
            Companions = companions,
            StartedUtc = started,
            ExpectedArrivalUtc = started + TimeSpan.FromMilliseconds(PlayerRouteStartMilliseconds - BoardAfterPlayerMountMilliseconds) +
                                 TimeSpan.FromSeconds(rideSeconds) + TimeSpan.FromSeconds(2),
        };
    }

    /// <summary>
    /// Every think of a player's companion: its player on a stable horse puts it away; off the horse (at the end or by
    /// jumping off) brings it back beside the player. This does not depend on the stable master's hand-off, which did
    /// not catch every ride (owner 2026-10-31: hired companions still ran behind the horse). True while it is put away.
    /// </summary>
    // A stable route seats the player on the route horse as its steed (IsRiding); IsOnHorse is only a personal horse,
    // never set on a stable route, so checking it caught no ride at all (owner 2026-10-31: a companion still died on foot).
    public static bool SyncStowWithLeaderRide(GameBot bot)
    {
        if (bot is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active, IsPlayerCompanion: true })
            return false;
        GamePlayer leader = bot.PlayerGroupLeader;
        bool riding = leader is { IsAlive: true, ObjectState: GameObject.eObjectState.Active, IsRiding: true } &&
                      leader.Steed != null && bot.Group != null && bot.Group == leader.Group;
        if (riding)
        {
            if (!bot.StowedForRide)
            {
                Stow(bot);
                Log.Info($"COMPANION_RIDE_STOWED bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{leader.Name}\"");
            }
            return true;
        }
        // the ride's own monitor brings them back after its grace period; without one (or no leader) do it here
        if (bot.StowedForRide && (leader == null || !ActiveRoutes.ContainsKey(leader)))
        {
            Unstow(bot, leader, Math.Max(0, bot.GroupIndex - 1));
            Log.Info($"COMPANION_RIDE_BACK bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{leader?.Name}\"");
        }
        return false;
    }

    /// <summary>Puts a companion (and its pet) away for the ride: out of every fight, out of sight, untouchable.</summary>
    private static void Stow(GameBot bot)
    {
        bot.StopCurrentSpellcast();
        bot.StopAttack();
        bot.StopFollowing();
        bot.StopMovingOnPath();
        bot.StopMoving();
        (bot.Brain as BotBrain)?.ClearAggroList();
        GameNPC pet = bot.ControlledBrain?.Body is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } body ? body : null;
        foreach (GameNPC npc in bot.GetNPCsInRadius(WorldMgr.VISIBILITY_DISTANCE))
            if (npc.Brain is StandardMobBrain enemy)
            {
                enemy.RemoveFromAggroList(bot);
                if (pet != null)
                    enemy.RemoveFromAggroList(pet);
            }
        foreach (GameNPC hidden in new[] { bot, pet })
        {
            if (hidden == null)
                continue;
            hidden.StopAttack();
            hidden.StowedForRide = true;
            foreach (GamePlayer viewer in hidden.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                viewer.Out.SendObjectRemove(hidden);
        }
        if (pet?.Brain is ControlledMobBrain petBrain)
            petBrain.Disengage();
    }

    /// <summary>Brings a stowed companion (and its pet) back beside the player.</summary>
    private static void Unstow(GameBot bot, GamePlayer player, int ordinal)
    {
        GameNPC pet = bot.ControlledBrain?.Body;
        bool petStowed = pet?.StowedForRide == true;
        if (petStowed)
            pet.StowedForRide = false;
        if (!bot.StowedForRide)
            return;
        bot.StowedForRide = false;
        if (bot.ObjectState is not GameObject.eObjectState.Active || player?.ObjectState is not GameObject.eObjectState.Active)
            return;
        Vector3 beside = FormationPoint(player, ordinal);
        bot.MoveTo(player.CurrentRegionID, (int)Math.Round(beside.X), (int)Math.Round(beside.Y), (int)Math.Round(beside.Z), player.Heading);
        if (petStowed && pet.IsAlive && pet.ObjectState is GameObject.eObjectState.Active && bot.ObjectState is GameObject.eObjectState.Active &&
            (pet.CurrentRegionID != bot.CurrentRegionID || !pet.IsWithinRadius(bot, 300)))
        {
            Point2D behind = bot.GetPointFromHeading(bot.Heading, 64);
            if (pet.CurrentRegionID == bot.CurrentRegionID)
                pet.MoveInRegion(bot.CurrentRegionID, behind.X, behind.Y, bot.Z + 10, bot.Heading, true);
            else
                pet.MoveTo(bot.CurrentRegionID, behind.X, behind.Y, bot.Z + 10, bot.Heading);
        }
        if (bot.Brain is BotBrain brain)
        {
            bot.Follow(player, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            brain.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
        }
    }

    /// <summary>
    /// Event and failsafe entry point after a real player has completed an
    /// authoritative MoveTo/portal/region transfer.  Existing helper objects
    /// move; none are created, saved, disbanded, or applied to other players.
    /// </summary>
    public static int RelocateHelpersAfterPlayerTransfer(GamePlayer player, bool explicitMoveTo = false)
    {
        if (DragonCombatGeometry.IsDisplacing(player))
            return 0;
        bool hasConfirmedTransfer = explicitMoveTo || HasPendingPlayerTransfer(player);
        if (player?.ObjectState is not GameObject.eObjectState.Active || !player.IsAlive || player.Group == null || player.CurrentZone == null ||
            !hasConfirmedTransfer)
            return 0;

        int relocated = 0;
        int ordinal = 0;
        foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
        {
            if (!bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active)
                continue; // Dead helpers retain their corpse/recovery timer.
            // A helper already fighting near where the player landed stays in that fight (Legion's purgatory: the
            // zone point back down lands about 3000 from him, where the raid is still fighting).
            if (bot.CurrentRegionID == player.CurrentRegionID && bot.InCombat && bot.IsWithinRadius(player, 4000))
                continue;
            if (bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper)
            {
                if (TryBringGroupmateAlong(bot))
                    relocated++;
                continue;
            }
            bool needsRelocation = ShouldRelocateForOwnerTransfer(
                    hasConfirmedTransfer,
                    bot.IsTemporaryGroupHelper,
                    bot.Group == player.Group,
                    bot.PlayerGroupLeader == player,
                    bot.CurrentRegionID,
                    player.CurrentRegionID,
                    bot.CurrentZone?.ID ?? 0,
                    player.CurrentZone.ID);
            if (!needsRelocation || !bot.IsTemporaryGroupHelper || bot.Group != player.Group || bot.PlayerGroupLeader != player)
                continue;

            bot.CompleteStableMasterRoute();
            // A portal or /release ends the old encounter for helpers. Do not
            // carry a queued pull, cast or walking request to the new location.
            bot.StopCurrentSpellcast();
            (bot.Brain as BotBrain)?.PrepareForTankPull();
            bot.StopMoving();
            Vector3 destination = FormationPoint(player, ordinal++);
            if (!bot.MoveTo(player.CurrentRegionID, (int)Math.Round(destination.X), (int)Math.Round(destination.Y),
                    (int)Math.Round(destination.Z), player.Heading))
                continue;

            bot.EnterPlayerLedGroup(player);
            bot.Follow(player, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            if (bot.Brain is BotBrain brain)
                brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
            relocated++;
        }
        PendingPlayerTransfers.TryRemove(player, out _);
        return relocated;
    }

    /// <summary>
    /// The bot-brain fallback covers an unusual missed transfer event without
    /// changing ordinary same-zone following.
    /// </summary>
    public static bool EnsureOwnerTransferCohesion(GameBot bot)
    {
        // A gamebot in the player's group: the player's region change has no event for it, so its think checks.
        if (bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsPlayerLedGroup: true } &&
            bot.PlayerGroupLeader?.CurrentRegionID != bot.CurrentRegionID)
            return TryBringGroupmateAlong(bot);
        if (bot is not { IsTemporaryGroupHelper: true, IsOnStableMasterRoute: false } || bot.Owner == null ||
            !HasPendingPlayerTransfer(bot.Owner))
            return false;

        GamePlayer player = bot.Owner;
        if (!ShouldRelocateForOwnerTransfer(
                true,
                true,
                bot.Group == player.Group,
                bot.PlayerGroupLeader == player,
                bot.CurrentRegionID,
                player.CurrentRegionID,
                bot.CurrentZone?.ID ?? 0,
                player.CurrentZone?.ID ?? 0))
            return false;

        return RelocateHelpersAfterPlayerTransfer(player) > 0;
    }

    private static bool HasPendingPlayerTransfer(GamePlayer player)
    {
        if (player == null || !PendingPlayerTransfers.TryGetValue(player, out DateTime markedUtc))
            return false;
        if (DateTime.UtcNow - markedUtc <= TransferMarkerLifetime)
            return true;
        PendingPlayerTransfers.TryRemove(player, out _);
        return false;
    }

    // NpcMovementComponent toggles PathPoint.FiredFlag as it progresses.  Give
    // every companion an independent Once-path chain so staggered riders cannot
    // mutate the native rider's route state or each other's route state.
    private static PathPoint CloneRoute(PathPoint route)
    {
        PathPoint first = null;
        PathPoint previous = null;
        for (PathPoint current = route; current != null; current = current.Next)
        {
            var copy = new PathPoint(current.X, current.Y, current.Z, current.MaxSpeed, current.Type)
            {
                WaitTime = current.WaitTime,
            };
            first ??= copy;
            copy.Prev = previous;
            if (previous != null)
                previous.Next = copy;
            previous = copy;
        }
        return first;
    }

    private static void PollRoutes(object state)
    {
        foreach (RouteMonitor monitor in ActiveRoutes.Values)
        {
            if (Interlocked.Exchange(ref monitor.Queued, 1) != 0)
                continue;
            NpcService.Instance.Post(MonitorRouteOnGameLoop, monitor);
        }
    }

    private static void MonitorRouteOnGameLoop(RouteMonitor monitor)
    {
        try
        {
            if (!ActiveRoutes.TryGetValue(monitor.Player, out RouteMonitor current) || current != monitor)
                return;

            DateTime now = DateTime.UtcNow;
            bool playerStillRiding = monitor.Player.ObjectState is GameObject.eObjectState.Active &&
                                     monitor.Player.IsAlive && monitor.Player.Steed != null && monitor.Player.IsRiding;
            if (monitor.Companions.Any(bot => bot.StowedForRide))
            {
                // stowed companions wait out the ride, then come back beside the player wherever the player got off
                if (playerStillRiding || now - monitor.StartedUtc <= TimeSpan.FromMilliseconds(RouteAbortGraceMilliseconds))
                    return;
                int slot = 0;
                foreach (GameBot bot in monitor.Companions)
                    Unstow(bot, monitor.Player, slot++);
                ActiveRoutes.TryRemove(monitor.Player, out _);
                return;
            }
            bool routeMayHaveFinished = now >= monitor.ExpectedArrivalUtc;
            bool interrupted = !playerStillRiding && now - monitor.StartedUtc > TimeSpan.FromMilliseconds(RouteAbortGraceMilliseconds) &&
                               !routeMayHaveFinished;
            if (interrupted)
            {
                foreach (GameBot bot in monitor.Companions)
                    bot.CompleteStableMasterRoute();
                ActiveRoutes.TryRemove(monitor.Player, out _);
                return;
            }

            if (routeMayHaveFinished || monitor.Companions.All(bot => !bot.IsOnStableMasterRoute))
                ActiveRoutes.TryRemove(monitor.Player, out _);
        }
        finally
        {
            Volatile.Write(ref monitor.Queued, 0);
        }
    }

    internal static Vector3 FormationPoint(GamePlayer player, int ordinal)
    {
        double angle = player.Heading * Math.PI * 2d / 4096d + ordinal * Math.PI * 2d / 6d;
        double radius = FormationRadius;
        if (player.Group?.IsCompanionRaid == true)
        {
            (angle, radius) = CompanionRaidFormation.Slot(ordinal, player.CurrentRegion?.IsDungeon == true);
        }
        Vector3 current = new(player.X, player.Y, player.Z);
        Vector3 desired = new(
            player.X + (float)(Math.Cos(angle) * radius),
            player.Y + (float)(Math.Sin(angle) * radius),
            player.Z);
        return PathfindingProvider.Instance.GetMoveAlongSurface(
                   player.CurrentZone,
                   current,
                   desired,
                   PathfindingProvider.Instance.DefaultFilters) ?? current;
    }

    private static int Distance(GameObject first, GameObject second)
    {
        long dx = (long)first.X - second.X;
        long dy = (long)first.Y - second.Y;
        long dz = (long)first.Z - second.Z;
        return (int)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
