using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DOL.AI.Brain;

namespace DOL.GS
{
    // Optional task-boundary travel only. Existing movement/navmesh, stable and
    // region-transition executors are reused, not replaced with cosmetic paths.
    public sealed partial class AutonomousWorldBotController
    {
        public const string TownIdlePhase = "Town idling";
        private sealed record IdleTown(eRealm Realm, ushort RegionId, string Name,
            GameNPC Anchor, AbstractArea Area, bool Capital, int MinimumLevel, int MaximumLevel);
        private sealed record IdleDestination(IdleTown Town, Vector3 Point);
        private static readonly object TownCatalogSync = new();
        private static IdleTown[] _idleTowns = [];
        private static long _townCatalogExpires;
        private static long _nextTownRouteValidation;
        private IdleTown[] _townCandidates;
        private int _townCandidateIndex;
        private IdleDestination _townIdleDestination;
        private DateTime? _townIdleUntilUtc;
        private bool _townIdleFinished;
        private readonly record struct TownRouteValidation(bool Valid, IdleTown Town, Vector3 Point, double Distance);
        private Task<TownRouteValidation> _townRouteValidation;
        // Living Server 1.2: the bank and the usual crowd spots (AutonomousTownHangouts).
        private DateTime? _townHangoutAtUtc;
        private bool _townHangoutWalking;
        private AutonomousTownHangouts.Kind _townHangoutKind;
        private GameObject _townHangoutFocus;
        private Vector2? _townHangoutFace;
        private static long _nextTownHangoutSearch;

        private void ResetTownIdle()
        {
            _townCandidates = null;
            _townCandidateIndex = 0;
            _townRouteValidation = null;
            _townIdleDestination = null;
            _townIdleUntilUtc = null;
            _townIdleFinished = false;
            _townHangoutAtUtc = null;
            _townHangoutWalking = false;
            _townHangoutFocus = null;
            _townHangoutFace = null;
        }

        public static bool IsIdleTownLevelAppropriate(int level, bool capital, int minimum, int maximum)
        {
            return capital || minimum > 0 && maximum >= minimum && level >= Math.Max(1, minimum - 3) && level <= maximum + 5;
        }

        public static bool IsIdleTownArea(AbstractArea area)
        {
            // The installed classic/SI towns are named Circle records rather
            // than SafeArea records. Catalog admission ALSO requires a friendly
            // real merchant/trainer/stable and local leveling coverage.
            return area != null && (area.IsSafeArea || area is Area.BindArea ||
                area is Area.Circle circle && circle.Radius >= 500 && circle.Radius <= 6_000 &&
                !string.IsNullOrWhiteSpace(area.Description));
        }

        public static bool IsAtAssignedIdleTown(GameBot bot)
        {
            return bot?.CurrentRegion?.IsCapitalCity == true || bot?.CurrentAreas?.OfType<AbstractArea>()
                .Any(area => IsIdleTownArea(area) && area.Description == bot.PersistentRecord?.TravelDestination) == true;
        }

        private bool HandleTownIdle(BotBrain brain, GameBot bot)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.TownIdle); // AI_PROFILE: measures only
            if (_townIdleFinished) return false;
            DateTime now = DateTime.UtcNow;
            if (!DateTime.TryParse(bot.PersistentRecord.ObjectiveExpiresUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime deadline))
                return FinishTownIdle(bot, "Invalid maintenance deadline");
            deadline = deadline.ToUniversalTime();

            if (_townIdleUntilUtc.HasValue)
            {
                if (now >= _townIdleUntilUtc.Value || now >= deadline)
                    return FinishTownIdle(bot, "Town break completed");
                if (!IsInsideIdleTown(bot, _townIdleDestination))
                    return FinishTownIdle(bot, "Left the selected safe town; cancelling optional break");
                if (ContinueTownHangoutWalk(bot))
                    return true;
                if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), _townIdleDestination.Point) > 96 * 96)
                {
                    bot.WakeRecoveryRest();
                    if (!IssuePath(bot, _townIdleDestination.Point))
                        return FinishTownIdle(bot, "Could not return to the selected town spot after interruption");
                    SetStatus(bot, "Returning to town idle spot", $"Idle in {_townIdleDestination.Town.Name}",
                        "Returning after interruption without extending either deadline", string.Empty, _townIdleDestination.Town.Name);
                    return true;
                }
                if (TryStartTownHangout(bot, now, _townIdleUntilUtc.Value < deadline ? _townIdleUntilUtc.Value : deadline))
                    return true;
                // Body language (Living Server) may take a step or two around the spot; don't cancel it.
                if (!AutonomousBotBodyLanguage.Idle(bot, BodySpot.Town, _townIdleDestination.Point))
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                }
                if (brain.CheckHeals()) return true;
                if (AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0))
                    bot.WakeRecoveryRest();
                else if (AutonomousRestPolicy.NeedsRecovery(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0))
                {
                    if (!BotRestRecovery.BlocksRest(bot))
                        bot.BeginRecoveryRest();
                }
                PublishTownIdleStatus(bot, deadline);
                // Existing rare context-aware ambient chat is already called by
                // BotBrain. No additional chat timer, roll or crowd broadcast.
                return true;
            }

            TimeSpan remaining = deadline - now;
            if (remaining < AutonomousTownDowntime.MinimumDuration)
                return FinishTownIdle(bot, "Less than fifteen minutes remain after maintenance/travel");
            if (_townIdleDestination == null)
            {
                _townCandidates ??= IdleTownCatalog().Where(town => town.Realm == bot.Realm &&
                    IsIdleTownLevelAppropriate(bot.Level, town.Capital, town.MinimumLevel, town.MaximumLevel))
                    .OrderBy(_ => Random.Shared.Next()).ToArray();
                SetStatus(bot, "Choosing a reachable town", "Take an optional 15-30 minute town break",
                    "Validating a same-realm town route within the original maintenance deadline");

                // A validation walks up to 64 navmesh path queries. It runs on a
                // worker thread so one bot's route proof never stretches the
                // whole game-loop tick; the result is applied on a later turn.
                if (_townRouteValidation != null)
                {
                    if (!_townRouteValidation.IsCompleted)
                        return true;
                    TownRouteValidation result = _townRouteValidation.IsCompletedSuccessfully
                        ? _townRouteValidation.Result : default;
                    _townRouteValidation = null;
                    remaining = deadline - DateTime.UtcNow;
                    if (result.Valid &&
                        TimeSpan.FromSeconds(result.Distance / Math.Max(1, (int)bot.MaxSpeed) * 1.25 + 10) + AutonomousTownDowntime.MinimumDuration <= remaining)
                    {
                        _townIdleDestination = new(result.Town, result.Point);
                        Log.Info($"AUTONOMOUS_TOWN_IDLE_ROUTE bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                            $"town=\"{result.Town.Name}\" region={result.Town.RegionId} destination={result.Point.X},{result.Point.Y},{result.Point.Z} " +
                            $"validatedWalkUnits={(int)result.Distance}");
                    }
                    return true;
                }

                if (_townCandidateIndex >= Math.Min(12, _townCandidates.Length))
                    return FinishTownIdle(bot, "No fully validated town route fits the remaining budget");

                // At most two candidate validations a second server-wide, and
                // only for the 15% task-boundary roll. Never a per-tick world scan.
                long tick = GameLoop.GameLoopTime;
                long next = Volatile.Read(ref _nextTownRouteValidation);
                if (tick < next || Interlocked.CompareExchange(ref _nextTownRouteValidation, tick + 500, next) != next)
                    return true;
                IdleTown town = _townCandidates[_townCandidateIndex++];
                if (TryChooseTownPoint(bot, town, out Vector3 point))
                {
                    eRealm realm = bot.Realm;
                    ushort startRegion = bot.CurrentRegionID;
                    Vector3 start = new(bot.X, bot.Y, bot.Z);
                    _townRouteValidation = Task.Run(() =>
                        TryValidateTownRoute(realm, startRegion, start, town.RegionId, point, out double distance)
                            ? new TownRouteValidation(true, town, point, distance)
                            : default);
                }
                return true;
            }

            IdleDestination selected = _townIdleDestination;
            if (bot.CurrentRegionID != selected.Town.RegionId)
            {
                CampDestination previous = _camp;
                try
                {
                    _camp = new("town-idle", selected.Town.Name, selected.Town.Name, selected.Town.RegionId,
                        (int)selected.Point.X, (int)selected.Point.Y, (int)selected.Point.Z, 0, false, false);
                    return TravelAcrossRegions(bot);
                }
                finally { _camp = previous; }
            }
            if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), selected.Point) > 48 * 48)
            {
                bot.WakeRecoveryRest();
                if (TryBeginFasterStableRoute(bot, selected.Point, selected.Town.Name)) return true;
                if (!IssuePath(bot, selected.Point))
                    return FinishTownIdle(bot, "Town route failed during travel; leaving it for diagnostics");
                SetStatus(bot, $"Traveling to {selected.Town.Name}", $"Idle in {selected.Town.Name}",
                    "Following a validated route; the idle timer starts only on arrival", string.Empty, selected.Town.Name);
                return true;
            }

            if (!IsInsideIdleTown(bot, selected))
                return FinishTownIdle(bot, "Arrival is outside the safe area");
            DateTime arrived = DateTime.UtcNow;
            TimeSpan? duration = AutonomousBotPersonality.ScaleTownBreak(bot,
                AutonomousTownDowntime.RollWithinBudget(deadline - arrived), deadline - arrived);
            if (!duration.HasValue)
                return FinishTownIdle(bot, "Travel left insufficient time for the minimum break");
            _townIdleUntilUtc = arrived.Add(duration.Value);
            _townHangoutAtUtc = arrived.AddMilliseconds(AutonomousTownHangouts.FirstHopDelay(Random.Shared.NextDouble()));
            bot.StopMovingOnPath();
            bot.StopMoving();
            PublishTownIdleStatus(bot, deadline);
            Log.Info($"AUTONOMOUS_TOWN_IDLE_STARTED bot=\"{bot.Name}\" id={bot.DatabaseID} town=\"{selected.Town.Name}\" " +
                $"durationSeconds={(int)duration.Value.TotalSeconds} untilUtc={_townIdleUntilUtc:O}");
            return true;
        }

        private void PublishTownIdleStatus(GameBot bot, DateTime deadline)
        {
            bot.PersistentRecord.ObjectivePhase = TownIdlePhase;
            SetStatus(bot, TownIdlePhase, $"Idle in {_townIdleDestination.Town.Name}",
                $"Town break ends {_townIdleUntilUtc.Value:HH:mm:ss} UTC; original maintenance deadline {deadline:HH:mm:ss} UTC",
                string.Empty, _townIdleDestination.Town.Name);
        }

        private bool FinishTownIdle(GameBot bot, string reason)
        {
            _townIdleFinished = true;
            _townIdleUntilUtc = null;
            bot.WakeRecoveryRest();
            bot.StopMovingOnPath();
            bot.StopMoving();
            bot.PersistentRecord.ObjectivePhase = "Between tasks";
            Log.Info($"AUTONOMOUS_TOWN_IDLE_ENDED bot=\"{bot.Name}\" id={bot.DatabaseID} reason=\"{reason}\"");
            return false;
        }

        private static bool IsInsideIdleTown(GameBot bot, IdleDestination destination)
        {
            return destination != null && bot.CurrentRegionID == destination.Town.RegionId &&
                (destination.Town.Capital || destination.Town.Area?.IsContaining(new Point3D(bot.X, bot.Y, bot.Z), false) == true);
        }

        private static IdleTown[] IdleTownCatalog()
        {
            long now = GameLoop.GameLoopTime;
            if (now < Volatile.Read(ref _townCatalogExpires)) return _idleTowns;
            lock (TownCatalogSync)
            {
                if (now < _townCatalogExpires) return _idleTowns;
                var towns = new List<IdleTown>();
                var seen = new HashSet<(ushort, ushort)>();
                var camps = CampCatalogSnapshot().GroupBy(cell => cell.Zone.ID).ToDictionary(group => group.Key, group => group.ToArray());
                foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                    !region.IsDungeon && AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion)))
                {
                    foreach (GameNPC anchor in region.Objects.OfType<GameNPC>().Where(npc =>
                        npc.ObjectState == GameObject.eObjectState.Active && npc.CurrentZone != null &&
                        npc is GameMerchant or GameTrainer or GameStableMaster))
                    {
                        Zone zone = anchor.CurrentZone;
                        eRealm realm = ProtectedRealm(region.ID, zone.ID);
                        if (realm == eRealm.None || anchor.Realm != eRealm.None && anchor.Realm != realm ||
                            IsFrontierZone(region.ID, zone.ID)) continue;
                        AbstractArea area = anchor.CurrentAreas?.OfType<AbstractArea>().FirstOrDefault(IsIdleTownArea);
                        if (!region.IsCapitalCity && area == null) continue;
                        if (!seen.Add((region.ID, region.IsCapitalCity ? ushort.MaxValue : area.ID))) continue;
                        CampCatalogCell[] nearby = camps.GetValueOrDefault(zone.ID, [])
                            .Where(cell => DistanceSquared(anchor.X, anchor.Y, cell.X, cell.Y) <= 18_000L * 18_000L)
                            .OrderBy(cell => DistanceSquared(anchor.X, anchor.Y, cell.X, cell.Y)).Take(8).ToArray();
                        int[] levels = nearby.SelectMany(cell => cell.Levels).Where(level => level >= 1 && level <= 50).ToArray();
                        if (!region.IsCapitalCity && levels.Length == 0) continue;
                        towns.Add(new(realm, region.ID, region.IsCapitalCity ? region.Description : area.Description,
                            anchor, area, region.IsCapitalCity, levels.Length > 0 ? levels.Min() : 1,
                            levels.Length > 0 ? levels.Max() : 50));
                    }
                }
                _idleTowns = towns.ToArray();
                Volatile.Write(ref _townCatalogExpires, now + 10 * 60_000);
                return _idleTowns;
            }
        }

        private static bool TryChooseTownPoint(GameBot bot, IdleTown town, out Vector3 point)
        {
            point = default;
            GameNPC anchor = town.Anchor;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (anchor?.ObjectState != GameObject.eObjectState.Active || !nav.IsAvailable || !nav.HasNavmesh(anchor.CurrentZone)) return false;
            Vector3 center = new(anchor.X, anchor.Y, anchor.Z);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector3? candidate = nav.GetRandomPoint(anchor.CurrentZone, center, town.Capital ? 900 : 600, nav.DefaultFilters);
                if (!candidate.HasValue || anchor.CurrentRegion.GetZone((int)candidate.Value.X, (int)candidate.Value.Y) != anchor.CurrentZone ||
                    !town.Capital && town.Area?.IsContaining(new Point3D((int)candidate.Value.X, (int)candidate.Value.Y, (int)candidate.Value.Z), false) != true)
                    continue;
                // Safe PvP areas need not exclude aggressive monsters. Reject a
                // resting spot that lies in a currently live monster's aggro bubble.
                if (InLiveAggroRange(anchor, candidate.Value)) continue;
                point = candidate.Value;
                return true;
            }
            return false;
        }

        private static bool InLiveAggroRange(GameObject around, Vector3 point) =>
            around.GetNPCsInRadius(3_500).Any(npc => npc.IsAlive && IsExperienceMonster(npc) && npc.IsAggressive &&
                npc.Brain is StandardMobBrain mobBrain &&
                Vector3.DistanceSquared(point, new(npc.X, npc.Y, npc.Z)) <= (double)mobBrain.AggroRange * mobBrain.AggroRange);

        /// <summary>
        /// The walk over to a hangout: true while it walks (the town break's other work waits), false once there or when
        /// the route fails (then it simply stays where it is). On arrival it turns, in steps, to what it came to.
        /// </summary>
        private bool ContinueTownHangoutWalk(GameBot bot)
        {
            if (!_townHangoutWalking)
                return false;
            Vector3 here = new(bot.X, bot.Y, bot.Z);
            if (Vector2.DistanceSquared(new(here.X, here.Y), new(_townIdleDestination.Point.X, _townIdleDestination.Point.Y)) >
                AutonomousTownHangouts.ArriveTolerance * AutonomousTownHangouts.ArriveTolerance)
            {
                if (IssuePath(bot, _townIdleDestination.Point))
                {
                    SetStatus(bot, $"Wandering over to the {AutonomousTownHangouts.Word(_townHangoutKind)}",
                        $"Idle in {_townIdleDestination.Town.Name}", "Town break: the bank and the usual crowd spots",
                        _townHangoutFocus?.Name ?? string.Empty, _townIdleDestination.Town.Name);
                    return true;
                }
                // No route after all: stand where it got to.
                _townIdleDestination = _townIdleDestination with { Point = here };
            }
            _townHangoutWalking = false;
            bot.StopMovingOnPath();
            bot.StopMoving();
            if (_townHangoutFocus is { ObjectState: GameObject.eObjectState.Active } focus && focus.CurrentRegion == bot.CurrentRegion)
                BotTurning.TurnGradually(bot, focus);
            else if (_townHangoutFace is Vector2 face)
                BotTurning.TurnGradually(bot, bot.GetHeading((int)face.X, (int)face.Y));
            AutonomousTownHangouts.Count(_townHangoutKind, arrived: true);
            return false;
        }

        /// <summary>
        /// Now and then during the break the bot wanders over to the bank, a merchant, a trainer or a crowd already standing
        /// somewhere in town (AutonomousTownHangouts). Only fully recovered and out of a fight, at most four searches a second
        /// server-wide, and only to a spot with a complete short path on the bot's own zone mesh.
        /// </summary>
        private bool TryStartTownHangout(GameBot bot, DateTime now, DateTime breakEnds)
        {
            if (!AutonomousTownHangouts.HopDue(now, _townHangoutAtUtc, breakEnds) || bot.InCombat || bot.IsRecoveryResting ||
                bot.IsMoving || !AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0))
                return false;
            long tick = GameLoop.GameLoopTime;
            long next = Volatile.Read(ref _nextTownHangoutSearch);
            if (tick < next || Interlocked.CompareExchange(ref _nextTownHangoutSearch, tick + 250, next) != next)
                return false;
            using var aiProfile = AiProfile.Scope(AiSystem.TownHangout); // AI_PROFILE: measures only
            _townHangoutAtUtc = now.AddMilliseconds(AutonomousTownHangouts.RetryMs);
            IdleDestination current = _townIdleDestination;
            if (!TryChooseHangout(bot, current.Town, out AutonomousTownHangouts.Kind kind, out GameObject focus,
                    out Vector2? face, out Vector3 point))
            {
                AutonomousTownHangouts.Count(null);
                return false;
            }
            _townHangoutAtUtc = now.AddMilliseconds(AutonomousTownHangouts.NextDwell(Random.Shared.NextDouble()));
            _townIdleDestination = current with { Point = point };
            _townHangoutKind = kind;
            _townHangoutFocus = focus;
            _townHangoutFace = face;
            _townHangoutWalking = true;
            AutonomousTownHangouts.Count(kind);
            return ContinueTownHangoutWalk(bot);
        }

        private static bool TryChooseHangout(GameBot bot, IdleTown town, out AutonomousTownHangouts.Kind kind,
            out GameObject focus, out Vector2? face, out Vector3 point)
        {
            kind = default;
            focus = null;
            face = null;
            point = default;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone zone = bot.CurrentZone;
            if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return false;
            ushort radius = town.Capital ? AutonomousTownHangouts.CapitalSearchRadius : AutonomousTownHangouts.SearchRadius;
            var banks = new List<GameNPC>();
            var merchants = new List<GameNPC>();
            var trainers = new List<GameNPC>();
            var standers = new List<Vector2>();
            foreach (GameNPC npc in bot.GetNPCsInRadius(radius))
            {
                if (npc == bot || npc.ObjectState != GameObject.eObjectState.Active || !npc.IsAlive || npc.CurrentZone != zone)
                    continue;
                if (npc is GameBot other)
                {
                    if (other.Realm == bot.Realm && !other.IsMoving && !other.InCombat)
                        standers.Add(new(other.X, other.Y));
                    continue;
                }
                if (npc.Realm != bot.Realm && npc.Realm != eRealm.None || !InsideTown(town, new(npc.X, npc.Y, npc.Z)))
                    continue;
                if (npc is GameVaultKeeper) banks.Add(npc);
                else if (npc is GameMerchant) merchants.Add(npc);
                else if (npc is GameTrainer) trainers.Add(npc);
            }
            foreach (GamePlayer player in bot.GetPlayersInRadius(radius))
                if (player.IsAlive && player.Realm == bot.Realm && !player.IsMoving)
                    standers.Add(new(player.X, player.Y));
            Vector2 self = new(bot.X, bot.Y);
            bool crowd = AutonomousTownHangouts.TryFindCrowd(standers, self, out Vector2 crowdCenter, out _);
            AutonomousTownHangouts.Kind? chosen = AutonomousTownHangouts.Choose(banks.Count > 0, merchants.Count > 0,
                trainers.Count > 0, crowd, Random.Shared.NextDouble());
            if (chosen is not AutonomousTownHangouts.Kind picked)
                return false;
            kind = picked;
            GameNPC target = null;
            if (picked != AutonomousTownHangouts.Kind.Crowd)
            {
                List<GameNPC> list = picked switch
                {
                    AutonomousTownHangouts.Kind.Bank => banks,
                    AutonomousTownHangouts.Kind.Merchant => merchants,
                    _ => trainers,
                };
                target = list[Random.Shared.Next(list.Count)];
            }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Vector2 spot = target != null
                    ? AutonomousTownHangouts.SpotNear(new(target.X, target.Y), target.Heading, picked,
                        Random.Shared.NextDouble(), Random.Shared.NextDouble())
                    : AutonomousTownHangouts.SpotInCrowd(crowdCenter, self, Random.Shared.NextDouble(), Random.Shared.NextDouble());
                if (!AutonomousTownHangouts.HasRoom(spot, standers))
                    continue;
                float floorZ = target?.Z ?? bot.Z;
                if (!TryHangoutSpot(bot, town, nav, zone, spot, floorZ, out point))
                    continue;
                focus = target;
                face = target == null ? crowdCenter : null;
                return true;
            }
            return false;
        }

        private static bool InsideTown(IdleTown town, Vector3 point) =>
            town.Capital || town.Area?.IsContaining(new Point3D((int)point.X, (int)point.Y, (int)point.Z), false) == true;

        /// <summary>The spot on the mesh, dry, on the floor of what the bot came to, in town, out of every live monster's
        /// aggro range, and a short complete walk from the bot.</summary>
        private static bool TryHangoutSpot(GameBot bot, IdleTown town, IPathfindingMgr nav, Zone zone, Vector2 spot,
            float floorZ, out Vector3 point)
        {
            point = default;
            if (bot.CurrentRegion?.GetZone((int)spot.X, (int)spot.Y) != zone)
                return false;
            Vector3? floor = nav.GetClosestPoint(zone, new(spot.X, spot.Y, floorZ), 24, 24, AutonomousTownHangouts.MaxRise,
                nav.DefaultFilters);
            if (floor is not Vector3 f || MathF.Abs(f.Z - floorZ) > AutonomousTownHangouts.MaxRise ||
                zone.IsUnderwater((int)f.X, (int)f.Y, (int)f.Z) || !InsideTown(town, f) || InLiveAggroRange(bot, f))
                return false;
            Vector3 start = new(bot.X, bot.Y, bot.Z);
            int budget = 4;
            double length = 0;
            if (!ValidateTownCorridor(nav, zone, start, f, ref budget, ref length) ||
                !AutonomousTownHangouts.WalkAcceptable(length, Vector3.Distance(start, f)))
                return false;
            point = f;
            return true;
        }

        // Conservative proof of the exact canonical walking itinerary. Stable
        // travel can improve it, but is never needed to excuse a missing mesh.
        // Each zone corridor must finish; disconnected islands/partial dead ends
        // are rejected. This never changes the world's route/pathfinding policy.
        private static bool TryValidateTownRoute(GameBot bot, ushort targetRegion, Vector3 target, out double length) =>
            TryValidateTownRoute(bot.Realm, bot.CurrentRegionID, new(bot.X, bot.Y, bot.Z), targetRegion, target, out length);

        // Snapshot inputs only: this runs on a worker thread (navmesh queries
        // are thread-local), so it must not read the live bot.
        private static bool TryValidateTownRoute(eRealm realm, ushort startRegion, Vector3 start,
            ushort targetRegion, Vector3 target, out double length)
        {
            length = 0;
            Vector3 cursor = start;
            ushort regionId = startRegion;
            var visited = new HashSet<ushort>();
            int queryBudget = 64;
            for (int hop = 0; hop < 12; hop++)
            {
                if (!visited.Add(regionId)) return false;
                Region region = WorldMgr.GetRegion(regionId);
                if (regionId == targetRegion)
                    return ValidateTownRegionWalk(region, cursor, target, ref queryBudget, ref length);
                var crossing = FindNextCrossing(realm, regionId, targetRegion, (int)target.X, (int)target.Y);
                if (crossing == null || !ValidateTownRegionWalk(region, cursor,
                    new(crossing.SourceX, crossing.SourceY, crossing.SourceZ), ref queryBudget, ref length)) return false;
                regionId = crossing.TargetRegion;
                cursor = new(crossing.TargetX, crossing.TargetY, crossing.TargetZ);
            }
            return false;
        }

        private static bool ValidateTownRegionWalk(Region region, Vector3 cursor, Vector3 target, ref int budget, ref double length)
        {
            IPathfindingMgr nav = PathfindingProvider.Instance;
            var zones = new HashSet<ushort>();
            for (int seam = 0; region != null && seam < 16; seam++)
            {
                Zone zone = region.GetZone((int)cursor.X, (int)cursor.Y);
                Zone goalZone = region.GetZone((int)target.X, (int)target.Y);
                if (zone == null || goalZone == null || !nav.HasNavmesh(zone) || !zones.Add(zone.ID)) return false;
                if (zone == goalZone) return ValidateTownCorridor(nav, zone, cursor, target, ref budget, ref length);
                if (!AutonomousZoneItinerary.TryNextStep(region, zone, goalZone, cursor, target, nav, out var step) ||
                    !ValidateTownCorridor(nav, zone, cursor, step.Inside, ref budget, ref length)) return false;
                length += Vector3.Distance(step.Inside, step.Outside);
                cursor = step.Outside;
            }
            return false;
        }

        public static bool IsCompleteTownCorridor(PathfindingResult result, Vector3 last, Vector3 target)
        {
            return result.Status == PathfindingStatus.PathFound && result.NodeCount > 0 &&
                Vector3.DistanceSquared(last, target) <= 48 * 48;
        }

        private static bool ValidateTownCorridor(IPathfindingMgr nav, Zone zone, Vector3 cursor, Vector3 target,
            ref int budget, ref double length)
        {
            if (!nav.TrySnapToMesh(zone, ref cursor, 96) || !nav.TrySnapToMesh(zone, ref target, 96)) return false;
            WrappedPathfindingNode[] nodes = ArrayPool<WrappedPathfindingNode>.Shared.Rent(512);
            try
            {
                for (int segment = 0; segment < 12 && budget-- > 0; segment++)
                {
                    PathfindingResult result = nav.GetPathStraight(zone, cursor, target, nav.DefaultFilters, nodes);
                    if (result.NodeCount < 1 || result.NodeCount > nodes.Length) return false;
                    Vector3 end = nodes[result.NodeCount - 1].Position;
                    bool complete = IsCompleteTownCorridor(result, end, target);
                    if (!complete && (!AutonomousRouteRecoveryPolicy.CanContinuePartial(result.Status, cursor, end) ||
                        Vector3.DistanceSquared(end, target) >= Vector3.DistanceSquared(cursor, target))) return false;
                    Vector3 previous = cursor;
                    for (int index = 0; index < result.NodeCount; index++)
                    {
                        length += Vector3.Distance(previous, nodes[index].Position);
                        previous = nodes[index].Position;
                    }
                    if (complete) return true;
                    cursor = end;
                }
                return false;
            }
            finally { ArrayPool<WrappedPathfindingNode>.Shared.Return(nodes); }
        }
    }
}
