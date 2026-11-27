using System;
using System.Collections.Generic;
using System.Threading;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Movement;
using DOL.GS.ServerProperties;
using DOL.Logging;
using static DOL.GS.GameObject;

namespace DOL.GS
{
    public class NpcMovementComponent : MovementComponent
    {
        public static readonly Logger log = LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const short DEFAULT_WALK_SPEED = 70;


        private MovementState _movementState;
        private Vector3 _velocity;
        private Vector3 _destination;
        private long _nextFollowTick;
        // Realism 4a (#487): an arrived follower of a standing leader faced it once; from then on it keeps its heading.
        private bool _facedStandingLeader;
        private int _followTickInterval;
        private short _moveOnPathSpeed;
        private long _stopAtPathPointUntil;
        private long _walkingToEstimatedArrivalTime;
        private readonly MovementRequest _movementRequest = new();
        private readonly Pathfinder _pathfinder;
        private ResetHeadingAction _resetHeadingAction;
        private Vector3 _destinationForClient;
        private long _positionForClientTick;
        private Vector3 _positionForClient;
        private bool _needsBroadcastUpdate = true;
        private short _currentMovementDesiredSpeed;
        private int? _groundTravelArrivalMilliseconds;
        private PathVisualization _pathVisualization;
        private long _lastPositionUpdateTick = -1;
        private long _nextAutonomousClientCorrectionTick;
        private bool _forceReliableBroadcast;
        private bool _autonomousPathFailurePending;
        private PathfindingStatus _autonomousPathFailureStatus;
        private Vector3 _autonomousPathFailureDestination;
        private bool _autonomousTravelArrival;
        private SeamContinuation _seamContinuation;
        private FallContinuation _fallContinuation;
        private static int _stairLandingLogs;
        // 1.2.2 (#438 generalized, docs/NAVMESH-DROPS.md): the last path leg a bot walked outside Darkness Falls. A leg
        // over an off-mesh link (ladder, climb, ledge drop, gap) has no polygon in its middle; Commit: the bot is walking
        // a steep one and does not replot before its far end. Kept after a stop (Commit cleared) for the landing rule.
        private PathLeg _pathLeg;
        private static int _linkLandingLogs;
        private sealed record PathLeg(Vector3 From, Vector3 To, ushort Region, bool Commit);
        private ClimbContinuation _climbContinuation;
        private static int _stairClimbLogs;
        private Vector3 _stairClimbCheckedFinal;
        private long _stairClimbCheckedUntil;
        private bool _stairClimbCheckedResult;
        private long _petStuckSince;
        private Vector3 _petStuckAnchor;
        private enum FallStage { WalkOffLip, Descend }
        private sealed record FallContinuation(Vector3 Air, Vector3 Landing, Vector3 Final,
            ushort Region, Group Group, string Assignment, short Speed, FallStage Stage);
        // #438 (1.2.2): a certified stair fall walked backwards, landing -> air (rise) -> lip (step on).
        private enum ClimbStage { Rise, StepOn }
        private sealed record ClimbContinuation(Vector3 Air, Vector3 Lip, Vector3 Final,
            ushort Region, Group Group, string Assignment, short Speed, ClimbStage Stage);
        private sealed record SeamContinuation(Vector3 At, Vector3 Next, Vector3? Final,
            ushort Region, Group Group, string Assignment, bool Walk);

        private bool TryContinueCertifiedFall()
        {
            FallContinuation fall = _fallContinuation;
            if (fall == null) return false;
            Vector3 expected = fall.Stage == FallStage.WalkOffLip ? fall.Air : fall.Landing;
            // #438: player-led companions take the stair falls too (UsesStairFalls).
            if (Owner is not GameBot bot ||
                bot.CurrentRegionID != fall.Region || bot.Group != fall.Group ||
                bot.PersistentRecord?.ObjectiveAssignmentId != fall.Assignment || !bot.IsAlive ||
                bot.IsOnStableMasterRoute || Vector3.DistanceSquared(_ownerPosition, expected) > 16 * 16)
            {
                _fallContinuation = null;
                return false;
            }

            if (fall.Stage == FallStage.WalkOffLip)
            {
                // Autonomous NPC movement has no gravity. Ordinary XYZ
                // interpolation from the lip straight to the lower floor
                // would cut through DF's vertical stone wall.
                _fallContinuation = fall with { Stage = FallStage.Descend };
                WalkToInternal(fall.Landing, fall.Speed);
            }
            else
            {
                _fallContinuation = null;
                PathToInternal(fall.Final, fall.Speed);
            }
            return IsFlagSet(MovementState.WalkTo);
        }

        /// <summary>A stair climb is being walked: the mover is on its way to the air point or the lip. New orders then
        /// only change where the bot goes once it stands on the lip (#438, 1.2.2).</summary>
        private bool StairClimbUnderWay() =>
            _climbContinuation is { } climb && IsMoving && IsFlagSet(MovementState.WalkTo) &&
            Owner.CurrentRegionID == climb.Region &&
            Vector3.DistanceSquared(_destination, climb.Stage == ClimbStage.Rise ? climb.Air : climb.Lip) <= 16 * 16;

        private bool TryContinueStairClimb()
        {
            ClimbContinuation climb = _climbContinuation;
            if (climb == null) return false;
            Vector3 expected = climb.Stage == ClimbStage.Rise ? climb.Air : climb.Lip;
            if (Owner is not GameBot bot ||
                bot.CurrentRegionID != climb.Region || bot.Group != climb.Group ||
                bot.PersistentRecord?.ObjectiveAssignmentId != climb.Assignment || !bot.IsAlive ||
                bot.IsOnStableMasterRoute || Vector3.DistanceSquared(_ownerPosition, expected) > 16 * 16)
            {
                _climbContinuation = null;
                return false;
            }

            if (climb.Stage == ClimbStage.Rise)
            {
                // Over the landing at lip height: step level onto the lip, the reverse of the walk off it.
                _climbContinuation = climb with { Stage = ClimbStage.StepOn };
                WalkToInternal(climb.Lip, climb.Speed);
            }
            else
            {
                _climbContinuation = null;
                PathToInternal(climb.Final, climb.Speed);
            }
            return IsFlagSet(MovementState.WalkTo);
        }

        /// <summary>
        /// #438 (1.2.2): Darkness Falls' entrance stairs are three one-way drops per realm in zone249.nav, so no path leads
        /// up them. A bot whose path toward a destination at or above a drop's lip is partial (a companion following its
        /// player up the stairs) walks to that drop's landing and climbs the certified fall backwards: it rises over the
        /// landing to the fall's air point and steps onto the lip, then paths on (and climbs the next drop the same way).
        /// Only where the navmesh agrees: the landing reachable from the bot and a complete path from the top of the
        /// stairs to the destination. The links are the hash-guarded ones the falls use (none on another mesh).
        /// </summary>
        private bool TryClimbDarknessFallsStairs(Zone zone, Vector3 destination, short speed)
        {
            if (speed <= 0 || Owner is not GameBot bot || zone?.ID != AutonomousDarknessFallsPolicy.RegionId ||
                !bot.IsAlive || bot.IsOnStableMasterRoute)
                return false;
            // #571 step 3: a player-led gamebot (hired, in a player's group) climbs on the installed links, as companions do.
            IReadOnlyList<AutonomousDarknessFallsNavigation.TraversalLink> links = CompanionDarknessFallsPaths.UsesGamebotRules(bot)
                ? AutonomousDarknessFallsNavigation.CertifiedFallLinks
                : AutonomousDarknessFallsNavigation.InstalledFallLinks;
            if (!AutonomousDarknessFallsNavigation.TryPlanStairClimb(_ownerPosition, destination, links,
                    out AutonomousDarknessFallsNavigation.StairClimb climb))
                return false;

            bool atFoot = AutonomousDarknessFallsNavigation.IsAtStairFoot(_ownerPosition, climb);
            long now = GameLoop.GameLoopTime;
            // A follower re-plans every few hundred ms; the two navmesh checks are kept for two seconds per destination.
            if (now >= _stairClimbCheckedUntil || Vector3.DistanceSquared(destination, _stairClimbCheckedFinal) > 64 * 64)
            {
                IPathfindingMgr nav = PathfindingProvider.Instance;
                _stairClimbCheckedResult = StairPathComplete(nav, zone, climb.Top, destination) &&
                    (atFoot || StairPathComplete(nav, zone, _ownerPosition, climb.Foot));
                _stairClimbCheckedFinal = destination;
                _stairClimbCheckedUntil = now + 2000;
            }
            if (!_stairClimbCheckedResult)
                return false;

            if (!atFoot)
            {
                // Walk to the landing first; every node reached re-plans toward the real destination (and climbs there).
                PathToInternal(climb.Foot, speed, false);
                _movementRequest.Set(MovementRequestType.Path, destination, speed);
                return true;
            }

            _climbContinuation = new(climb.Air, climb.Lip, destination, bot.CurrentRegionID, bot.Group,
                bot.PersistentRecord?.ObjectiveAssignmentId, speed, ClimbStage.Rise);
            UnsetFlag(MovementState.Pathfinding);
            WalkToInternal(climb.Air, speed);
            if (Interlocked.Increment(ref _stairClimbLogs) <= 200)
                BotPetStuckRecovery.Log.Info($"DF_STAIR_CLIMB bot=\"{bot.Name}\" " +
                    $"kind={CompanionDarknessFallsPaths.KindOf(bot)} region={Owner.CurrentRegionID} " +
                    $"from={(int) _ownerPosition.X},{(int) _ownerPosition.Y},{(int) _ownerPosition.Z} " +
                    $"lip={(int) climb.Lip.X},{(int) climb.Lip.Y},{(int) climb.Lip.Z} steps={climb.Steps} " +
                    $"to={(int) destination.X},{(int) destination.Y},{(int) destination.Z}");
            return true;
        }

        private static bool StairPathComplete(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to)
        {
            if (nav == null || zone == null) return false;
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
            PathfindingResult result = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
            return result.Status == PathfindingStatus.PathFound && result.NodeCount > 0 && result.NodeCount <= nodes.Length &&
                Vector3.Distance(nodes[result.NodeCount - 1].Position, to) <= 96;
        }

        public void PathAcrossValidatedSeam(Vector3 inside, Vector3 outside, Vector3? final, short speed)
        {
            PathTo(inside, speed);
            if (Owner is GameBot { IsAutonomousWorldBot: true } bot)
                _seamContinuation = new(inside, outside, final, bot.CurrentRegionID, bot.Group,
                    bot.PersistentRecord?.ObjectiveAssignmentId, true);
        }

        public void PathViaValidatedWaypoint(Vector3 waypoint, Vector3 final, short speed)
        {
            PathTo(waypoint, speed);
            if (Owner is GameBot { IsAutonomousWorldBot: true } bot)
                _seamContinuation = new(waypoint, final, null, bot.CurrentRegionID, bot.Group,
                    bot.PersistentRecord?.ObjectiveAssignmentId, false);
        }

        private bool TryContinueValidatedSeam()
        {
            SeamContinuation continuation = _seamContinuation;
            if (continuation == null) return false;
            _seamContinuation = null;
            if (Owner is not GameBot bot || bot.CurrentRegionID != continuation.Region ||
                bot.Group != continuation.Group || bot.PersistentRecord?.ObjectiveAssignmentId != continuation.Assignment ||
                !bot.IsAlive || bot.InCombat || bot.IsAttacking || bot.IsCasting || bot.IsCrowdControlled ||
                bot.castingComponent?.HasPendingSkillRequests == true ||
                bot.IsOnStableMasterRoute || bot.IsRecoveryResting || bot.Brain is not BotBrain brain || brain.HasAggro ||
                Vector3.DistanceSquared(_ownerPosition, continuation.At) > 16 * 16)
                return false;
            if (bot.Group != null)
                foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
                    if (member.InCombat || member.IsAttacking || member is GameBot { IsRecoveryResting: true }) return false;
            if (continuation.Walk)
            {
                UnsetFlag(MovementState.Pathfinding);
                _movementRequest.Set(MovementRequestType.Walk, continuation.Next, _currentMovementDesiredSpeed);
                WalkToInternal(continuation.Next, _currentMovementDesiredSpeed);
                if (continuation.Final.HasValue)
                    _seamContinuation = continuation with { At = continuation.Next, Next = continuation.Final.Value, Final = null, Walk = false };
            }
            else
                PathToInternal(continuation.Next, _currentMovementDesiredSpeed);
            return IsFlagSet(MovementState.WalkTo);
        }

        public bool ConsumeAutonomousTravelArrival()
        {
            bool arrived = _autonomousTravelArrival;
            _autonomousTravelArrival = false;
            return arrived;
        }

        public new GameNPC Owner { get; }
        public int CopyUpcomingPath(Vector3 target, Span<Vector3> destination) =>
            _pathfinder.CopyUpcomingPath(target, destination);
        public ref Vector3 Velocity => ref _velocity;
        public ref Vector3 Destination => ref _destination;
        public GameLiving FollowTarget { get; private set; }
        public int MinFollowDistance { get; private set; }
        public int MaxFollowDistance { get; private set; }
        public string PathID { get; set; }
        public PathPoint CurrentPathPoint { get; set; }
        /// <summary>
        /// Offline DAoC: the first point of a path built in code (MovementMgr.CreatePath) for a scripted boss's patrol, set by
        /// <see cref="SetScriptedPath"/>. Upstream OpenDAoC-Core lets such a CurrentPathPoint drive the patrol state on its own
        /// (its path rework); here a scripted path counts as a path the brain patrols, like a database PathID.
        /// </summary>
        public PathPoint ScriptedPath { get; private set; }
        public bool IsReturningToSpawnPoint { get; private set; }
        /// <summary>The NPC is crossing an off-mesh link (Pathfinder.IsJumping; monsters, guards and pets only).</summary>
        public bool IsCrossingOffMeshLink => _pathfinder?.IsJumping == true;
        public int RoamingRange { get; set; }
        public long MovementStartTick { get; set; }
        public long MovementElapsedTicks => IsMoving ? GameLoop.GameLoopTime - MovementStartTick : 0;
        public bool FixedSpeed { get; set; }
        public override short MaxSpeed => FixedSpeed ? MaxSpeedBase : Owner is GameBot bot
            ? AutonomousGroupPace.Apply(bot, CompanionFollowPolicy.SpeedLimit(bot, base.MaxSpeed)) : base.MaxSpeed;
        public bool IsMovingOnPath => IsFlagSet(MovementState.OnPath);
        public bool IsNearSpawn => Owner.IsWithinRadius(Owner.SpawnPoint, 25);
        public bool IsDestinationValid { get; private set; }
        public bool IsAtDestination => !IsDestinationValid || (_destination - _ownerPosition).LengthSquared() < 1.0f;
        public bool CanRoam => Properties.ALLOW_ROAM && RoamingRange > 0 && !CanMoveOnPath;
        public bool CanMoveOnPath => !string.IsNullOrEmpty(PathID) || ScriptedPath != null;

        /// <summary>Patrols <paramref name="first"/> (a chain from MovementMgr.CreatePath, not shared with another NPC) from its
        /// first point; null clears it.</summary>
        public void SetScriptedPath(PathPoint first)
        {
            ScriptedPath = first;
            CurrentPathPoint = first;
        }
        public double HorizontalVelocityForClient { get; private set; }
        public bool HasActiveResetHeadingAction => _resetHeadingAction != null && _resetHeadingAction.IsAlive;
        public ref Vector3 DestinationForClient => ref _destinationForClient;
        public ref Vector3 PositionForClient => ref _positionForClientTick == GameLoop.GameLoopTime ? ref _positionForClient : ref _ownerPosition;

        public int X => (int) Math.Round(_ownerPosition.X);
        public int Y => (int) Math.Round(_ownerPosition.Y);
        public int Z => (int) Math.Round(_ownerPosition.Z);

        public NpcMovementComponent(GameNPC owner) : base(owner)
        {
            Owner = owner;
            _pathfinder = new(owner);
        }

        protected override void TickInternal()
        {
            // Update the component's position first for correct calculations.
            UpdatePosition();

            if (IsFlagSet(MovementState.TurnTo))
            {
                if (!Owner.IsAttacking)
                {
                    FinalizeTick();
                    return;
                }

                UnsetFlag(MovementState.TurnTo);
                _resetHeadingAction.Stop();
                _resetHeadingAction = null;
            }

            if (IsFlagSet(MovementState.Request))
            {
                UnsetFlag(MovementState.Request);
                ProcessMovementRequest();
            }

            if (IsFlagSet(MovementState.Follow))
            {
                if (GameServiceUtils.ShouldTick(_nextFollowTick))
                {
                    _followTickInterval = FollowTick();

                    if (_followTickInterval != 0)
                        _nextFollowTick = GameLoop.GameLoopTime + _followTickInterval;
                    else
                        UnsetFlag(MovementState.WalkTo);
                }
            }

            if (IsFlagSet(MovementState.WalkTo))
            {
                if (GameServiceUtils.ShouldTick(_walkingToEstimatedArrivalTime))
                {
                    UnsetFlag(MovementState.WalkTo);
                    OnArrival();
                }
            }

            if (IsFlagSet(MovementState.AtPathPoint))
            {
                if (GameServiceUtils.ShouldTick(_stopAtPathPointUntil))
                {
                    UnsetFlag(MovementState.AtPathPoint);
                    MoveToNextPathPoint();
                }
            }

            FinalizeTick();
        }

        private void FinalizeTick()
        {
            base.TickInternal();

            // A bot stable ride has one authoritative mover: its taxi. Keep
            // the server-side bot actor at the taxi position before any spatial
            // queries or client broadcasts observe this tick.
            if (Owner is GameBotTaxi botTaxi)
                botTaxi.SynchronizeRider();

            bool autonomousCorrection = Owner is GameBot { IsOnStableMasterRoute: false } && IsMoving &&
                                        GameServiceUtils.ShouldTick(_nextAutonomousClientCorrectionTick);
            if (_needsBroadcastUpdate || autonomousCorrection)
            {
                if (Owner is GameBot)
                    _nextAutonomousClientCorrectionTick = GameLoop.GameLoopTime + 1200 + Owner.ObjectID % 350;

                _needsBroadcastUpdate = false;
                OnPositionUpdate();
                // Movement starts/stops and combat-facing changes must not be
                // lossy for playerbots. Periodic in-motion corrections remain
                // UDP and cheaply re-anchor client extrapolation if one update
                // was missed.
                // A stable-route bot is visually attached to its GameTaxi.
                // Sending an independent NPC movement packet for the rider
                // detaches it client-side and produces an empty horse.
                if (Owner is not GameBot { IsOnStableMasterRoute: true })
                    ClientService.UpdateNpcForPlayers(Owner, !_forceReliableBroadcast);
                _forceReliableBroadcast = false;
            }

            if (_movementState is MovementState.None)
                RemoveFromServiceObjectStore();
        }

        public void WalkTo(Vector3 destination, short speed)
        {
            _seamContinuation = null;
            _fallContinuation = null;
            ReleasePathLegCommit();
            _climbContinuation = null;
            _movementRequest.Set(MovementRequestType.Walk, destination, speed);
            SetFlag(MovementState.Request);
            AddToServiceObjectStore();
        }

        public void PathTo(Vector3 destination, short speed)
        {
            // The controller may reissue the same travel order every tick.
            // Do not restart a certified fall between its lip and landing.
            if (_fallContinuation is { } fall &&
                Owner is GameBot fallingBot &&
                Owner.CurrentRegionID == fall.Region)
            {
                // #571 step 3: the gamebot rule is for gamebots no player leads; a player-led one keeps the companion rule.
                bool gamebotRules = CompanionDarknessFallsPaths.UsesGamebotRules(fallingBot);
                if (gamebotRules && Vector3.DistanceSquared(destination, fall.Final) <= 64 * 64)
                    return;
                // #438: a companion re-orders its formation slot every think while its player walks on. Restarting
                // the path between the lip and the landing replotted from inside the drop, where the navmesh has no
                // polygon in the pick box, and the companion stood there for good ("stuck on one of the steps").
                // It finishes the fall and then walks to the newest slot. Only while the fall is under way: a fall
                // whose walk was stopped is dropped, so it can never swallow a companion's orders.
                if (!gamebotRules && IsFlagSet(MovementState.WalkTo))
                {
                    _fallContinuation = fall with { Final = destination, Speed = speed > 0 ? speed : fall.Speed };
                    return;
                }
            }
            // #438 (1.2.2): the same for a stair climb under way, for every bot: it steps onto the lip, then goes on.
            if (StairClimbUnderWay())
            {
                _climbContinuation = _climbContinuation with { Final = destination, Speed = speed > 0 ? speed : _climbContinuation.Speed };
                return;
            }
            // 1.2.2 (#438 generalized): the same for any bot on a steep off-mesh link leg outside Darkness Falls (a
            // ladder, a wall or tower climb, a ledge drop). Its new order becomes the path it takes from the leg's far end;
            // replotting from inside the leg would start on no polygon and fail.
            if (IsCommittedToPathLeg())
            {
                _seamContinuation = null;
                _movementRequest.Set(MovementRequestType.Path, destination, speed);
                return;
            }
            _seamContinuation = null;
            _fallContinuation = null;
            _climbContinuation = null;
            _movementRequest.Set(MovementRequestType.Path, destination, speed);
            SetFlag(MovementState.Request);
            AddToServiceObjectStore();
        }

        private bool IsCommittedToPathLeg() =>
            _pathLeg is { Commit: true } leg && Owner is GameBot && Owner.CurrentRegionID == leg.Region &&
            IsFlagSet(MovementState.WalkTo) && IsFlagSet(MovementState.Pathfinding) &&
            Vector3.DistanceSquared(_destination, leg.To) <= 4 && NavLinkLeg.IsOnLeg(_ownerPosition, leg.From, leg.To);

        private void ReleasePathLegCommit()
        {
            if (_pathLeg is { Commit: true } leg)
                _pathLeg = leg with { Commit = false };
        }

        private void TrackPathLeg(Vector3 next)
        {
            ushort region = Owner.CurrentRegionID;
            if (_pathLeg is { } leg && leg.Region == region && Vector3.DistanceSquared(leg.To, next) <= 4)
            {
                // The same leg re-issued (a stop and a new order with the same next node): commit again from where it began.
                if (!leg.Commit && NavLinkLeg.IsCommitLeg(leg.From, leg.To))
                    _pathLeg = leg with { Commit = true };
                return;
            }
            _pathLeg = new(_ownerPosition, next, region, NavLinkLeg.IsCommitLeg(_ownerPosition, next));
        }

        /// <summary>The floor at the far end of the leg this bot was walking, when it is inside that leg and on no polygon
        /// within the pick box (the #438 stuck condition). Outside Darkness Falls only.</summary>
        private bool TryGetPathLegLanding(Zone zone, out Vector3 landing)
        {
            landing = default;
            if (_pathLeg is not { } leg || leg.Region != Owner.CurrentRegionID ||
                !NavLinkLeg.IsOnLeg(_ownerPosition, leg.From, leg.To))
                return false;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Vector3? here = nav.GetClosestPoint(zone, _ownerPosition, nav.DefaultFilters);
            if (here.HasValue) return false;
            return NavLinkLeg.TryResolveLanding(_ownerPosition, leg.From, leg.To, null,
                nav.GetClosestPoint(zone, leg.To, nav.DefaultFilters), out landing);
        }

        public void ForcePathReplot()
        {
            _pathfinder.ForceReplot = true;
        }

        /// <summary>Mover and path state for a route stall trace (diagnostics).</summary>
        public string DescribeRouteState() =>
            $"state={_movementState} speed={CurrentSpeed} dest={_destination.X:0},{_destination.Y:0},{_destination.Z:0} " +
            $"casting={Owner.IsCasting} combat={Owner.InCombat} {_pathfinder.DescribeRoute()}";

        /// <summary>A movement order was given and the mover has not taken it yet (diagnostics).</summary>
        public bool HasPendingMoveRequest => IsFlagSet(MovementState.Request);

        /// <summary>The pathfinder's last result (diagnostics).</summary>
        public PathfindingStatus LastPathfindingStatus => _pathfinder.PathfindingStatus;

        public bool TryConsumeAutonomousPathFailure(out PathfindingStatus status, out Vector3 destination)
        {
            status = _autonomousPathFailureStatus;
            destination = _autonomousPathFailureDestination;
            if (!_autonomousPathFailurePending)
                return false;
            _autonomousPathFailurePending = false;
            return true;
        }

        public void StopMoving()
        {
            _seamContinuation = null;
            _fallContinuation = null;
            ReleasePathLegCommit();
            _climbContinuation = null;
            _movementState = MovementState.None;
            StopFollowing();
            StopMovingOnPath();
            CancelReturnToSpawnPoint();

            if (IsMoving)
                UpdateMovement(0);
        }

        public void Follow(GameLiving target, int minDistance, int maxDistance)
        {
            if (target == null || target.ObjectState is not eObjectState.Active || !Owner.castingComponent.IsAllowedToFollow(target))
                return;

            _seamContinuation = null;
            _fallContinuation = null;
            if (!StairClimbUnderWay())
                _climbContinuation = null;

            if (target != FollowTarget)
            {
                _nextFollowTick = 0;
                _facedStandingLeader = false;
            }

            FollowTarget = target;
            MinFollowDistance = minDistance;
            MaxFollowDistance = maxDistance;
            SetFlag(MovementState.Follow);
            AddToServiceObjectStore();
        }

        public void StopFollowing()
        {
            UnsetFlag(MovementState.Follow);
            FollowTarget = null;
        }

        public void MoveOnPath(short speed)
        {
            StopMoving();
            _moveOnPathSpeed = speed;

            // Move to the first path point if we don't have any.
            // Otherwise and if we're not currently moving on path, move to the previous one (current path point if none).
            if (CurrentPathPoint == null && ScriptedPath != null)
                CurrentPathPoint = ScriptedPath; // a scripted patrol starts again from its first point (death clears the current one)

            if (CurrentPathPoint == null)
            {
                if (PathID == null)
                {
                    if (log.IsErrorEnabled)
                        log.Error($"Called {nameof(MoveOnPath)} but PathID is null (NPC: {Owner})");

                    return;
                }

                CurrentPathPoint = MovementMgr.LoadPath(PathID);

                if (CurrentPathPoint == null)
                {
                    if (log.IsErrorEnabled)
                        log.Error($"Called {nameof(MoveOnPath)} but LoadPath returned null (PathID: {PathID}) (NPC: {Owner})");

                    return;
                }

                SetFlag(MovementState.OnPath);
                PathTo(new(CurrentPathPoint.X, CurrentPathPoint.Y, CurrentPathPoint.Z), Math.Min(_moveOnPathSpeed, CurrentPathPoint.MaxSpeed));
                return;
            }
            else if (!IsFlagSet(MovementState.OnPath))
            {
                SetFlag(MovementState.OnPath);

                if (Owner.IsWithinRadius(CurrentPathPoint, 25))
                {
                    MoveToNextPathPoint();
                    return;
                }

                if (CurrentPathPoint.Type == EPathType.Path_Reverse && CurrentPathPoint.FiredFlag)
                {
                    if (CurrentPathPoint.Next != null)
                        CurrentPathPoint = CurrentPathPoint.Next;
                }
                else if (CurrentPathPoint.Prev != null)
                    CurrentPathPoint = CurrentPathPoint.Prev;

                PathTo(new(CurrentPathPoint.X, CurrentPathPoint.Y, CurrentPathPoint.Z), Owner.MaxSpeed);
            }
            else if (log.IsErrorEnabled)
                log.Error($"Called {nameof(MoveOnPath)} but both {nameof(CurrentPathPoint)} and {nameof(MovementState.OnPath)} are already set. (NPC: {Owner})");
        }

        public void StopMovingOnPath()
        {
            if (!IsFlagSet(MovementState.OnPath))
                return;

            UnsetFlag(MovementState.OnPath);

            if (Owner is (GameTaxi or GameTaxiBoat) and not GameBotTaxi)
                Owner.RemoveFromWorld();

            // We don't reset CurrentPathPoint here to allow the path to be resumed. This must be done manually if needed (on NPC death for example).
        }

        public void ReturnToSpawnPoint()
        {
            ReturnToSpawnPoint(DEFAULT_WALK_SPEED);
        }

        public void ReturnToSpawnPoint(short speed)
        {
            StopMoving();
            Owner.TargetObject = null;
            // Read safely: the return now starts as RETURN_TO_SPAWN is entered (d53eceb0c), which can be before an NPC's
            // components are complete (a test double, a body set up by a script).
            Owner.attackComponent?.StopAttack();
            (Owner.Brain as StandardMobBrain)?.ClearAggroList();
            IsReturningToSpawnPoint = true;
            PathTo(new(Owner.SpawnPoint.X, Owner.SpawnPoint.Y, Owner.SpawnPoint.Z), speed);
        }

        public void CancelReturnToSpawnPoint()
        {
            IsReturningToSpawnPoint = false;
        }

        public void Roam(short speed)
        {
            // `CanRoam` returns false if `RoamingRange` is <= 0.
            if (!CanRoam)
                return;

            int maxRoamingRadius = Owner.RoamingRange;

            if (Owner.CurrentZone.IsPathfindingEnabled)
            {
                EDtPolyFlags[] filters = PathfindingProvider.Instance.DefaultFilters;
                Vector3? target = PathfindingProvider.Instance.GetRandomPoint(Owner.CurrentZone, new(Owner.SpawnPoint.X, Owner.SpawnPoint.Y, Owner.SpawnPoint.Z), maxRoamingRadius, filters);

                if (target.HasValue)
                    PathTo(target.Value, speed);

                return;
            }

            maxRoamingRadius = Util.Random(maxRoamingRadius);
            double angle = Util.RandomDouble() * Math.PI * 2;
            double targetX = Owner.SpawnPoint.X + maxRoamingRadius * Math.Cos(angle);
            double targetY = Owner.SpawnPoint.Y + maxRoamingRadius * Math.Sin(angle);
            WalkTo(new((float) targetX, (float) targetY, Owner.SpawnPoint.Z), speed);
        }

        public void RestartCurrentMovement()
        {
            if (IsDestinationValid && !IsAtDestination)
                WalkToInternal(new(_destination.X, _destination.Y, _destination.Z), _currentMovementDesiredSpeed);
        }

        public void TurnTo(GameObject target, int duration = 0)
        {
            if (target == null || target.CurrentRegion != Owner.CurrentRegion)
                return;

            TurnTo(target.X, target.Y, duration);
        }

        public void TurnTo(int x, int y, int duration = 0)
        {
            TurnTo(Owner.GetHeading(x, y), duration);
        }

        public void TurnTo(ushort heading, int duration = 0)
        {
            if (Owner.Heading == heading || Owner.IsCrowdControlled || IsTurningDisabled)
                return;

            if (duration > 0)
            {
                SetFlag(MovementState.TurnTo);

                if (_resetHeadingAction == null)
                {
                    _resetHeadingAction = CreateResetHeadingAction();
                    _resetHeadingAction.Start(duration);
                }
                else
                {
                    // Attempt to extend the duration of our existing `ResetHeadingAction`.
                    _resetHeadingAction.Start(duration);

                    if (!_resetHeadingAction.IsAlive)
                    {
                        _resetHeadingAction = CreateResetHeadingAction();
                        _resetHeadingAction.Start(duration);
                    }
                }
            }

            _needsBroadcastUpdate = true;
            if (Owner is GameBot && Owner.IsAttacking)
                _forceReliableBroadcast = true;
            Owner.Heading = heading;
            AddToServiceObjectStore();
        }

        public override void DisableTurning(bool add)
        {
            // Trigger an update to make sure the NPC properly starts or stops auto facing client side.
            // May technically be only necessary if the count is going from 0 to 1 or 1 to 0, but we're skipping that because it would need to be thread safe.

            _needsBroadcastUpdate = true;
            base.DisableTurning(add);
        }

        public void TogglePathVisualization()
        {
            // Toggle visualization for both `Pathfinder` (pathfinding) and `PathPoint` (patrols, horse routes).

            _pathfinder.ToggleVisualization();

            if (_pathVisualization != null)
            {
                _pathVisualization.CleanUp();
                _pathVisualization = null;
                return;
            }

            _pathVisualization = new();

            if (CurrentPathPoint != null)
                _pathVisualization.Visualize(MovementMgr.FindFirstPathPoint(CurrentPathPoint), Owner.CurrentRegion);
        }

        public void ForceUpdatePosition()
        {
            _seamContinuation = null;
            _fallContinuation = null;
            _pathLeg = null;
            _climbContinuation = null;
            // Must be called every time the NPC is teleported or moved by other means than this component.
            _ownerPosition = new(Owner.RealX, Owner.RealY, Owner.RealZ);
            _positionForClient = _ownerPosition;
            _lastPositionUpdateTick = GameLoop.GameLoopTime;
        }

        protected override void UpdatePosition()
        {
            if (_lastPositionUpdateTick == GameLoop.GameLoopTime)
                return;

            if (!IsMoving)
            {
                _lastPositionUpdateTick = GameLoop.GameLoopTime;
                return;
            }

            long timeDelta = GameLoop.GameLoopTime - _lastPositionUpdateTick;
            Vector3 movementDelta = _velocity * (timeDelta * 0.001f);
            Vector3 potentialPosition = _ownerPosition + movementDelta;

            if (!IsDestinationValid)
            {
                if (!AllowPosition(potentialPosition)) return;
                _ownerPosition = potentialPosition;
                _lastPositionUpdateTick = GameLoop.GameLoopTime;
                return;
            }

            Vector3 absToDestination = Vector3.Abs(_destination - _ownerPosition);
            Vector3 absMovementDelta = Vector3.Abs(movementDelta);

            // Create a "mask" vector (1.0f or 0.0f) for each axis.
            // 1.0f means we use the potential position.
            // 0.0f means we have overshot and should clamp to destination.
            Vector3 usePotential = new(
                absToDestination.X >= absMovementDelta.X ? 1.0f : 0.0f,
                absToDestination.Y >= absMovementDelta.Y ? 1.0f : 0.0f,
                absToDestination.Z >= absMovementDelta.Z ? 1.0f : 0.0f
            );

            potentialPosition = potentialPosition * usePotential + _destination * (Vector3.One - usePotential);
            if (!AllowPosition(potentialPosition)) return;
            _ownerPosition = potentialPosition;
            _lastPositionUpdateTick = GameLoop.GameLoopTime;
        }

        private void ProcessMovementRequest()
        {
            if (_movementRequest.Type is MovementRequestType.Walk)
                WalkToInternal(_movementRequest.Destination, _movementRequest.Speed);
            else
                PathToInternal(_movementRequest.Destination, _movementRequest.Speed);
        }

        private void UpdateVelocity(float distanceToTarget)
        {
            MovementStartTick = GameLoop.GameLoopTime;
            _groundTravelArrivalMilliseconds = null;

            if (!IsMoving || distanceToTarget <= 0)
            {
                _velocity = Vector3.Zero;
                HorizontalVelocityForClient = 0.0;
                return;
            }

            if (!IsDestinationValid)
            {
                double heading = Owner.Heading * Point2D.HEADING_TO_RADIAN;
                _velocity = new((float) -Math.Sin(heading), (float) Math.Cos(heading), 0.0f);
            }
            else
            {
                Vector3 direction = _destination - _ownerPosition;
                if (AutonomousGroundTravelMotion.Applies(Owner, FixedSpeed, FollowTarget) &&
                    AutonomousGroundTravelMotion.TryCalculate(direction, CurrentSpeed, out var travel))
                {
                    _velocity = travel.Velocity;
                    HorizontalVelocityForClient = travel.HorizontalSpeed;
                    _groundTravelArrivalMilliseconds = travel.ArrivalMilliseconds;
                    return;
                }
                float scale = CurrentSpeed / distanceToTarget;
                _velocity = direction * scale;
            }

            HorizontalVelocityForClient =  new Vector2(_velocity.X, _velocity.Y).Length();
            return;
        }

        private void WalkToInternal(Vector3 destination, short speed)
        {
            if (!AllowPosition(destination)) return;
            // A gamebot seated by a movement habit stands before it walks (Bots as people 1.2, WP5).
            if (speed > 0 && Owner is GameBot { IsSeatedByHabit: true } seated) seated.StandFromHabit();
            if (IsTurningDisabled)
                return;

            _currentMovementDesiredSpeed = speed;

            if (speed > MaxSpeed)
                speed = MaxSpeed;

            if (speed <= 0)
            {
                if (CurrentSpeed > 0)
                    UpdateMovement(0);

                return;
            }

            float distanceToTarget = (_ownerPosition - destination).Length();

            if (distanceToTarget > 25)
                TurnTo((int) destination.X, (int) destination.Y);
            else if (!IsFlagSet(MovementState.Pathfinding))
                TurnTo(FollowTarget);

            int ticksToArrive = (int) (distanceToTarget * 1000 / speed);

            if (ticksToArrive <= 0)
            {
                _ownerPosition = destination;

                if (CurrentSpeed > 0)
                    UpdateMovement(0);

                return;
            }

            // Assume either the destination or speed has changed.
            UpdateMovement(destination, distanceToTarget, speed);
            SetFlag(MovementState.WalkTo);
            // Without this, a slope-corrected bot reaches the next node early
            // then visibly waits for the old, longer 3D arrival timer.
            _walkingToEstimatedArrivalTime = GameLoop.GameLoopTime + (_groundTravelArrivalMilliseconds ?? ticksToArrive);
        }

        private void PathToInternal(Vector3 destination, short speed, bool allowFloorRepair = true)
        {
            // A follow check or a new order during a stair climb: finish the climb, then go there (#438, 1.2.2).
            if (StairClimbUnderWay())
            {
                _climbContinuation = _climbContinuation with { Final = destination, Speed = speed > 0 ? speed : _climbContinuation.Speed };
                return;
            }
            if (!AutonomousRealmBoundary.Allows(Owner, destination))
            {
                UpdateMovement(0);
                return;
            }
            Zone zone = Owner.CurrentZone;

            // Port of OpenDAoC-Core 799b4f507: the zone the NPC really is in (GameObject.X/Y lag a tick behind), for the NPCs
            // that bridge zone borders. Gamebots are unchanged.
            if (Pathfinder.UsesUpstreamLinkAndZoneRules(Owner) && Owner.CurrentRegion?.GetZone((int) _ownerPosition.X, (int) _ownerPosition.Y) is Zone actual)
                zone = actual;

            if (!_pathfinder.ShouldPath(zone, destination, _ownerPosition))
            {
                if (IsPersistentAutonomous(this))
                    PauseMovement(this, destination);
                else
                    FallbackToWalk(this, destination, speed);
                return;
            }

            bool hasNextNode = _pathfinder.TryGetNextNode(zone, _ownerPosition, destination, out Vector3? _nextNode);
            // #438 (1.2.2): no path leads up Darkness Falls' entrance stairs; a partial one toward a goal above them is
            // replaced by the certified climb, before the bot walks a partial path into another level.
            if (_pathfinder.PathfindingStatus is PathfindingStatus.PartialPathFound or PathfindingStatus.NoPathFound &&
                TryClimbDarknessFallsStairs(zone, destination, speed))
                return;
            if (hasNextNode)
            {
                ClearAutonomousPathFailure();
                // #438 (1.2.1): every bot walks Darkness Falls' entrance stair drops as certified falls (off the lip,
                // then down), not only autonomous gamebots. A companion used to slide diagonally from the lip into the
                // lower landing and could be left inside the drop (see the DF_STAIR_LANDING case below).
                Vector3 air = default, landing = default;
                // #571 step 3: a player-led gamebot (hired, in a player's group) takes the companions' stair falls.
                if (Owner is GameBot bot &&
                    zone.ID == AutonomousDarknessFallsPolicy.RegionId &&
                    (CompanionDarknessFallsPaths.UsesGamebotRules(bot)
                        ? _pathfinder.PathfindingStatus == PathfindingStatus.PathFound &&
                          AutonomousDarknessFallsNavigation.TryGetCertifiedFall(_ownerPosition, _nextNode.Value, out air, out landing)
                        : _pathfinder.PathfindingStatus is PathfindingStatus.PathFound or PathfindingStatus.PartialPathFound &&
                          AutonomousDarknessFallsNavigation.TryGetStairFall(_ownerPosition, _nextNode.Value, out air, out landing)))
                {
                    _fallContinuation = new(air, landing, destination, bot.CurrentRegionID,
                        bot.Group, bot.PersistentRecord?.ObjectiveAssignmentId, speed, FallStage.WalkOffLip);
                    UnsetFlag(MovementState.Pathfinding);
                    WalkToInternal(air, speed);
                    return;
                }
                if (Owner is GameBot && NavLinkLeg.AppliesIn(zone.ID))
                    TrackPathLeg(_nextNode.Value);
                // Continue to the next node, even on partial paths.
                _movementRequest.Set(MovementRequestType.Path, destination, speed);
                SetFlag(MovementState.Pathfinding);
                // The Detour node chain is authoritative. Injecting a second
                // steering destination here made otherwise valid routes cut
                // into walls and repeatedly restart. Threat avoidance belongs
                // to high-level route selection, not the continuous mover.
                WalkToInternal(_nextNode.Value, speed);
                return;
            }

            switch (_pathfinder.PathfindingStatus)
            {
                // #438: a bot left inside one of Darkness Falls' entrance stair drops (a stop or a new order between the
                // lip and the landing) is on no polygon: every path from there fails and it stood on the step for good.
                // It is set down on the drop's certified landing, the floor it was falling to, and paths on from there.
                case PathfindingStatus.NoPathFound when allowFloorRepair && Owner is GameBot stairBot &&
                    zone.ID == AutonomousDarknessFallsPolicy.RegionId &&
                    AutonomousDarknessFallsNavigation.TryGetStairFallLanding(_ownerPosition, out Vector3 stairLanding):
                {
                    Vector3 inDrop = _ownerPosition;
                    _ownerPosition = stairLanding;
                    UpdateMovement(0);
                    _pathfinder.ForceReplot = true;
                    if (Interlocked.Increment(ref _stairLandingLogs) <= 200)
                        BotPetStuckRecovery.Log.Warn($"DF_STAIR_LANDING bot=\"{stairBot.Name}\" " +
                            $"kind={(stairBot.IsAutonomousWorldBot ? "gamebot" : "companion")} region={Owner.CurrentRegionID} " +
                            $"from={(int) inDrop.X},{(int) inDrop.Y},{(int) inDrop.Z} " +
                            $"to={(int) stairLanding.X},{(int) stairLanding.Y},{(int) stairLanding.Z}");
                    PathToInternal(destination, speed, false);
                    break;
                }
                // 1.2.2 (#438 generalized, docs/NAVMESH-DROPS.md): the same anywhere else. A bot left inside an off-mesh link
                // leg (a ladder, a climb, a ledge drop or a gap; stopped, or given a new order mid-leg) is on no polygon, so
                // every path from there fails. It is set down on the floor at the leg's far end and paths on from there.
                case PathfindingStatus.NoPathFound when allowFloorRepair && Owner is GameBot linkBot &&
                    NavLinkLeg.AppliesIn(zone.ID) && TryGetPathLegLanding(zone, out Vector3 linkLanding):
                {
                    Vector3 inLeg = _ownerPosition;
                    _pathLeg = null;
                    _ownerPosition = linkLanding;
                    UpdateMovement(0);
                    _pathfinder.ForceReplot = true;
                    if (Interlocked.Increment(ref _linkLandingLogs) <= 200)
                        BotPetStuckRecovery.Log.Warn($"NAV_LINK_LANDING bot=\"{linkBot.Name}\" " +
                            $"kind={(linkBot.IsAutonomousWorldBot ? "gamebot" : "companion")} region={Owner.CurrentRegionID} " +
                            $"zone={zone.ID} from={(int) inLeg.X},{(int) inLeg.Y},{(int) inLeg.Z} " +
                            $"to={(int) linkLanding.X},{(int) linkLanding.Y},{(int) linkLanding.Z}");
                    PathToInternal(destination, speed, false);
                    break;
                }
                case PathfindingStatus.NoPathFound when allowFloorRepair &&
                    Owner is GameBot { IsAutonomousWorldBot: true, IsOnStableMasterRoute: false, InCombat: false }:
                {
                    // Only a failed path triggers these two bounded lookups.
                    // Old mob/bind Z and interrupted slope movement can leave a
                    // bot 80-90 units below the polygon, outside Detour's pick box.
                    // Correct altitude without MoveTo/remove/add packet flicker;
                    // never move laterally through an obstruction or bypass a path.
                    var nav = PathfindingProvider.Instance;
                    Vector3 start = _ownerPosition, end = destination;
                    AutonomousNavigationSurface.TryFloor(nav, zone, start, out start);
                    AutonomousNavigationSurface.TryFloor(nav, zone, end, out end);
                    if (Vector3.DistanceSquared(start, _ownerPosition) > 4 ||
                        Vector3.DistanceSquared(end, destination) > 4)
                    {
                        _ownerPosition = start;
                        UpdateMovement(0);
                        _pathfinder.ForceReplot = true;
                        PathToInternal(end, speed, false);
                    }
                    else PauseMovement(this, destination);
                    break;
                }
                case PathfindingStatus.PathFound:
                {
                    EDtPolyFlags[] filters = PathfindingProvider.Instance.BlockingDoorAvoidanceFilters;

                    // Finalize the path if we have direct LoS to the destination.
                    // This ensures that the NPC stays on the mesh, assuming it's on it to begin with.
                    // Use the most restrictive filters for now, since we don't know which ones were used.
                    if (PathfindingProvider.Instance.HasLineOfSight(zone, _ownerPosition, destination, filters))
                    {
                        ClearAutonomousPathFailure();
                        FallbackToWalk(this, destination, speed);
                    }
                    else
                        PauseMovement(this, destination);

                    break;
                }
                case PathfindingStatus.PartialPathFound:
                case PathfindingStatus.BufferTooSmall:
                case PathfindingStatus.NoPathFound: // Happens when either the current position or the destination isn't on a mesh.
                {
                    // Owner 2026-10-07: raid bots chasing a wall archer were jumped onto the keep wall (the node nearest
                    // the target), inside a keep whose gate still stood. No bot in the frontier takes that jump.
                    if (IsPersistentAutonomous(this) || CompanionFollowPolicy.HasFormationOrder(Owner as GameBot) ||
                        Owner is GameBot && AutonomousWorldBotController.IsBattleground(Owner))
                    {
                        PauseMovement(this, destination);
                        break;
                    }

                    // Non-pet NPCs are teleported to the closest reachable node from a reverse-path.
                    // The teleport can cover a large distance in some cases, for example when both the NPC and the player are on a mesh island.
                    // This helps against exploits and misplaced NPCs.

                    // Pets following their owner are teleported at their feet if both are out of combat.
                    // This allows them to keep up if they jump down a ledge or bridge.
                    // This can theoretically be exploited by players in combat, but it requires both the pet and the owner to leave combat.

                    if (Owner.Brain is not ControlledMobBrain petBrain)
                    {
                        if (JumpToClosestReachableNode(this, destination))
                            break;
                    }
                    else if (!Owner.InCombat && !petBrain.Owner.InCombat && FollowTarget != null && petBrain.Owner == FollowTarget)
                    {
                        if (TeleportPetToFloorBeneathOwner(this, petBrain))
                            break;
                    }

                    PauseMovement(this, destination);
                    break;
                }
                case PathfindingStatus.NotSet:
                case PathfindingStatus.NavmeshUnavailable:
                {
                    if (IsPersistentAutonomous(this))
                        PauseMovement(this, destination);
                    else
                        FallbackToWalk(this, destination, speed);
                    break;
                }
                default:
                {
                    PauseMovement(this, destination);
                    break;
                }
            }

            static void FallbackToWalk(NpcMovementComponent component, Vector3 destination, short speed)
            {
                component.UnsetFlag(MovementState.Pathfinding);
                component.WalkToInternal(destination, speed);
            }

            static bool IsPersistentAutonomous(NpcMovementComponent component) =>
                component.Owner is GameBot { IsAutonomousWorldBot: true };

            static bool JumpToClosestReachableNode(NpcMovementComponent component, Vector3 destination)
            {
                if (!component._pathfinder.TryGetClosestReachableNode(component.Owner.CurrentZone, destination, component._ownerPosition, out Vector3? node) || !node.HasValue)
                    return false;

                component._ownerPosition = node.Value;
                component.UpdateMovement(0);
                component._pathfinder.ForceReplot = true;
                return true;
            }

            static bool TeleportPetToFloorBeneathOwner(NpcMovementComponent component, ControlledMobBrain petBrain)
            {
                const int MAX_TELEPORT_TRIGGER_RANGE = 1024;
                const int MAX_FLOOR_SEARCH_DEPTH = 1024;
                const int MIN_TELEPORT_DISTANCE = 128;

                GamePlayer playerOwner = petBrain.GetPlayerOwner();

                if (DragonCombatGeometry.IsRecoveringFromThrow(playerOwner))
                    return false;

                if (!component.Owner.IsWithinRadius(playerOwner, MAX_TELEPORT_TRIGGER_RANGE))
                    return false;

                Vector3 playerOwnerPos = new(playerOwner.X, playerOwner.Y, playerOwner.Z);
                EDtPolyFlags[] filters = PathfindingProvider.Instance.DefaultFilters;
                Vector3? floor = PathfindingProvider.Instance.GetFloorBeneath(playerOwner.CurrentZone, playerOwnerPos, MAX_FLOOR_SEARCH_DEPTH, filters);

                if (!floor.HasValue || component.Owner.IsWithinRadius(floor.Value, MIN_TELEPORT_DISTANCE))
                    return false;

                component._ownerPosition = floor.Value;
                component.UpdateMovement(0);
                component._pathfinder.ForceReplot = true;
                return true;
            }

            static void PauseMovement(NpcMovementComponent component, Vector3 destination)
            {
                bool autonomous = IsPersistentAutonomous(component);
                if (autonomous)
                {
                    component._autonomousPathFailurePending = true;
                    component._autonomousPathFailureStatus = component._pathfinder.PathfindingStatus;
                    component._autonomousPathFailureDestination = destination;
                }
                else
                {
                    component.TurnTo((int) destination.X, (int) destination.Y);
                }
                component.UnsetFlag(MovementState.Pathfinding);

                if (component.IsMoving)
                    component.UpdateMovement(0);
            }
        }

        private void ClearAutonomousPathFailure()
        {
            _autonomousPathFailurePending = false;
            _autonomousPathFailureStatus = PathfindingStatus.NotSet;
            _autonomousPathFailureDestination = default;
        }

        private int FollowTick()
        {
            // A stair climb under way is finished first: stopping it would leave the bot over the landing (#438, 1.2.2).
            if (StairClimbUnderWay())
                return Properties.GAMENPC_FOLLOWCHECK_TIME;

            // A bot's mobile song is a cast, but must not freeze its follow
            // route. Ordinary NPC casts and ranged attacks keep their stop rule.
            if (Owner.IsCasting && !BotSongTwistPolicy.HasMobileSongCast(Owner) ||
                (Owner.IsAttacking && Owner.ActiveWeaponSlot is eActiveWeaponSlot.Distance))
            {
                // A party member's cast pauses its formation follow; it moves on the moment the cast ends instead of
                // standing until its next AI turn re-issues the follow.
                if (AutonomousGroupFormationFollow.IsFormationFollow(Owner, FollowTarget))
                {
                    if (IsMoving)
                        UpdateMovement(0);
                    return AutonomousGroupFormationFollow.FollowCheckMilliseconds;
                }
                StopMoving();
                return Properties.GAMENPC_FOLLOWCHECK_TIME;
            }

            if (!FollowTarget.IsAlive || FollowTarget.ObjectState is not eObjectState.Active || Owner.CurrentRegionID != FollowTarget.CurrentRegionID)
            {
                StopMoving();
                return 0;
            }

            Vector3 targetPos = new(FollowTarget.X, FollowTarget.Y, FollowTarget.Z);

            // Stop at the dragon's body, not its origin under the model. Ordinary
            // following and all other NPC targets retain their existing distances.
            int dragonReach = DragonCombatGeometry.TargetReach(FollowTarget);
            if (dragonReach > 0 && Owner.IsAttacking && Owner.TargetObject == FollowTarget)
                MinFollowDistance = Math.Max(MinFollowDistance, dragonReach);

            // An autonomous party member keeps its formation slot around the leader (AutonomousGroupFormationFollow).
            bool formation = AutonomousGroupFormationFollow.TryGetSlot(Owner, FollowTarget, out Vector3 formationSlot, out bool leaderWalking);
            bool crossBorder = false;
            if (formation)
            {
                targetPos = formationSlot;
                MinFollowDistance = 0;
                // Realism 4a (#487): a watched party member seated by habit (a wait, a wall, a kept rest seat) stands
                // 0.4-1.5 s after its leader sets off, per member, instead of the whole party on one tick. Visual only:
                // anything to fight stands it at once (GameBot.HoldSeatBeforeStanding).
                if (leaderWalking && Owner is GameBot { IsSeatedByHabit: true } seatedMember)
                {
                    int sitFor = seatedMember.HoldSeatBeforeStanding(GameLoop.GameLoopTime,
                        AutonomousBotBodyLanguage.IsWatched(seatedMember));
                    if (sitFor > 0)
                        return Math.Min(sitFor, AutonomousGroupFormationFollow.FollowCheckMilliseconds);
                }
                // A slot the follower cannot reach: walk behind the leader for a while instead of standing.
                if (leaderWalking)
                {
                    bool slotUnreachable = !IsMoving && _pathfinder.PathfindingStatus is PathfindingStatus.PartialPathFound or PathfindingStatus.NoPathFound &&
                        Vector3.DistanceSquared(_ownerPosition, formationSlot) > 120 * 120;
                    if (AutonomousGroupFormationFollow.TrailLeader(Owner, slotUnreachable))
                    {
                        targetPos = new(FollowTarget.X, FollowTarget.Y, FollowTarget.Z);
                        MinFollowDistance = AutonomousGroupFormationFollow.TrailLeaderDistance;
                    }
                }
                // A slot over a zone border: paths are planned inside one zone, and an autonomous bot pauses on a
                // destination in another zone, so followers stood at every border their party crossed until the slot
                // came back into their zone. They aim at the leader instead and step over the border behind it.
                Zone here = Owner.CurrentZone;
                if (here != null && Owner.CurrentRegion?.GetZone((int) targetPos.X, (int) targetPos.Y) != here)
                {
                    targetPos = new(FollowTarget.X, FollowTarget.Y, FollowTarget.Z);
                    MinFollowDistance = AutonomousGroupFormationFollow.TrailLeaderDistance;
                    crossBorder = FollowTarget.CurrentZone != here;
                    AutonomousGroupFormationFollow.NoteBorderFollow(crossBorder);
                }
            }
            else if (Owner.Brain is StandardMobBrain brain && FollowTarget.Realm == Owner.Realm)
            {
                int tx = (int) targetPos.X;
                int ty = (int) targetPos.Y;
                int tz = (int) targetPos.Z;

                // Update to formation-adjusted position.
                if (brain.CheckFormation(ref tx, ref ty, ref tz))
                {
                    targetPos = new(tx, ty, tz);
                    MinFollowDistance = 0;
                }
            }

            Vector3 relative = targetPos - _ownerPosition;
            float distanceSquared = relative.LengthSquared();

            // Port of OpenDAoC-Core c59f4ba94: squared as a long. A follow with no limit (int.MaxValue: scouts running to their
            // friends) overflowed to 1 and stopped at once.
            if (distanceSquared > (long) MaxFollowDistance * MaxFollowDistance)
            {
                StopFollowing();
                return 0;
            }

            // The way position is updated ensures that we never move past the destination, so we need to take potential small inaccuracies into account.
            // A formation follower of a standing leader settles within a few units of its slot instead of jittering onto it.
            int arrived = formation && !leaderWalking ? AutonomousGroupFormationFollow.ArrivedTolerance : MinFollowDistance + 1;
            if (distanceSquared <= arrived * arrived)
            {
                // An autonomous gamebot re-faces what it follows only when more than 30 degrees off, not on every follow
                // check (Living Server 1.2: a party standing around its leader no longer turns with its every step).
                // Realism 4a (#487, tell 2): an arrived formation follower of a standing leader faces it once, then keeps
                // whatever heading it has, so its looks around (body language) are not snapped back every 400 ms.
                bool standingFormation = formation && !leaderWalking;
                if (!BotTurning.HoldsFollowHeading(Owner, FollowTarget) &&
                    !BotTurning.KeepsArrivedFollowHeading(Owner, standingFormation, _facedStandingLeader))
                    TurnTo(FollowTarget);
                _facedStandingLeader = standingFormation;

                if (IsMoving)
                    UpdateMovement(0);

                UnsetFlag(MovementState.Pathfinding); // Ensures OnArrival doesn't try to reach remaining nodes.
                return formation ? AutonomousGroupFormationFollow.FollowCheckMilliseconds : Properties.GAMENPC_FOLLOWCHECK_TIME;
            }

            _facedStandingLeader = false; // walking to its spot again: it faces the leader once more on arrival
            float distance = MathF.Sqrt(distanceSquared);
            float scale = MinFollowDistance / distance;

            Vector3 destination = targetPos - relative * scale;
            destination.Z = targetPos.Z; // May move the NPC closer than intended, but improves movement overall.

            short speed;

            // No smoothing if the NPC is attacking and is out of melee range, nor for a formation follower while its
            // leader walks (it keeps its full speed; slowing on approach made it fall back and stop and go).
            if (Owner.IsAttacking && distance > Owner.MeleeAttackRange || formation && leaderWalking)
                speed = MaxSpeed;
            else
                speed = (short) Math.Min(MaxSpeed, (distance - MinFollowDistance) * 2.5);

            if (TryRecoverStuckBotPet(targetPos, distance))
                return Properties.GAMENPC_FOLLOWCHECK_TIME;

            // The leader just crossed the border: walk straight after it over the last few hundred units (the ground it
            // walked); farther than that the follower's AI turn plans the border crossing.
            if (crossBorder && distance <= AutonomousGroupFormationFollow.BorderWalkDistance)
            {
                UnsetFlag(MovementState.Pathfinding);
                WalkToInternal(destination, Math.Max((short) 20, speed));
                return AutonomousGroupFormationFollow.FollowCheckMilliseconds;
            }
            // 1.2.3 (#538): farther than that, a path to the leader's zone finds nothing here (paths stay inside one zone):
            // it paused the follower, and it stood 1,500-2,500 units back at the border. The follower keeps any walk under
            // way and its next AI turn routes it over the border (AutonomousGroupFormationFollow.NeedsSeamRoute).
            if (crossBorder)
            {
                AutonomousGroupFormationFollow.NoteBorderHandOff();
                return AutonomousGroupFormationFollow.FollowCheckMilliseconds;
            }

            // Snap the destination to the mesh with a generous search distance. Use the follow target's position as a fallback.
            if (!TrySnapToMesh(ref destination))
            {
                destination = targetPos;
                TrySnapToMesh(ref destination);
            }

            // The pathfinder re-plots only when the target moved 64 units, so a formation follower that reached the end of
            // its last path stood until its slot had moved that far (run 20: followers stood in ~10% of the checks while
            // their leader walked). Standing while the leader walks gets a fresh path, unless the last one just failed
            // (a slot behind a closed keep door replanned every 400 ms during a siege, 2026-10-17).
            if (formation && leaderWalking && !IsMoving && !_pathfinder.HasRecentFailure(destination))
                _pathfinder.ForceReplot = true;
            // 1.2.2 (#438 generalized): a follower on a steep off-mesh link leg finishes it before it replots (the next
            // check after its arrival paths on to where the leader is then).
            if (IsCommittedToPathLeg())
                return formation ? AutonomousGroupFormationFollow.FollowCheckMilliseconds : Properties.GAMENPC_FOLLOWCHECK_TIME;
            PathToInternal(destination, Math.Max((short) 20, speed));
            return formation ? AutonomousGroupFormationFollow.FollowCheckMilliseconds : Properties.GAMENPC_FOLLOWCHECK_TIME;
        }

        /// <summary>
        /// A bot's pet that ends up inside rock (seen at Vigilant Rock, 2026-10-03) gets no
        /// path from its own off-mesh position and stood there forever. When it makes no
        /// progress toward its owner for a while, put it back on the mesh beside the owner.
        /// Player pets and every other NPC are unchanged.
        /// </summary>
        private bool TryRecoverStuckBotPet(Vector3 ownerTarget, float distance)
        {
            if (Owner.Brain is not IControlledBrain { Owner: GameBot bot } || FollowTarget != bot ||
                distance <= BotPetStuckRecovery.MinimumDistance)
            {
                _petStuckSince = 0;
                return false;
            }

            long now = GameLoop.GameLoopTime;
            if (_petStuckSince == 0 || Vector3.DistanceSquared(_ownerPosition, _petStuckAnchor) >
                BotPetStuckRecovery.MinimumProgress * BotPetStuckRecovery.MinimumProgress)
            {
                _petStuckSince = now;
                _petStuckAnchor = _ownerPosition;
                return false;
            }

            if (!BotPetStuckRecovery.IsStuck(_petStuckSince, now))
                return false;

            Vector3 landing = ownerTarget;
            TrySnapToMesh(ref landing);
            Vector3 from = _ownerPosition;
            _petStuckSince = 0;
            if (!Owner.MoveInRegion(Owner.CurrentRegionID, (int) landing.X, (int) landing.Y, (int) landing.Z, Owner.Heading, true))
                return false;
            BotPetStuckRecovery.Log.Warn($"BOT_PET_STUCK_RECOVERY pet=\"{Owner.Name}\" owner=\"{bot.Name}\" region={Owner.CurrentRegionID} " +
                $"from={(int) from.X},{(int) from.Y},{(int) from.Z} to={(int) landing.X},{(int) landing.Y},{(int) landing.Z}");
            return true;
        }

        public bool TrySnapToMesh(ref Vector3 destination)
        {
            const float MAX_SNAP_DISTANCE = 128f;
            Zone zone = Owner.CurrentRegion.GetZone((int) destination.X, (int) destination.Y);
            return PathfindingProvider.Instance.TrySnapToMesh(zone, ref destination, MAX_SNAP_DISTANCE);
        }

        private void OnArrival()
        {
            if (!AllowPosition(_destination)) return;
            if (TryContinueCertifiedFall()) return;
            if (TryContinueStairClimb()) return;
            if (_seamContinuation != null && Vector3.DistanceSquared(_ownerPosition, _seamContinuation.At) <= 16 * 16 &&
                TryContinueValidatedSeam()) return;
            if (IsFlagSet(MovementState.Pathfinding))
            {
                ProcessMovementRequest();

                if (IsFlagSet(MovementState.WalkTo))
                    return;
            }

            if (IsFlagSet(MovementState.Follow))
                return;

            if (IsReturningToSpawnPoint)
            {
                _ownerPosition = _destination;
                UpdateMovement(0);
                CancelReturnToSpawnPoint();
                TurnTo(Owner.SpawnHeading);
                return;
            }

            if (IsFlagSet(MovementState.OnPath))
            {
                if (CurrentPathPoint != null)
                {
                    if (CurrentPathPoint.WaitTime == 0)
                    {
                        MoveToNextPathPoint();
                        return;
                    }

                    SetFlag(MovementState.AtPathPoint);
                    _stopAtPathPointUntil = GameLoop.GameLoopTime + CurrentPathPoint.WaitTime * 100;
                }
                else
                    StopMovingOnPath();
            }

            if (TryContinueValidatedSeam()) return;

            if (IsMoving)
            {
                _ownerPosition = _destination;
                UpdateMovement(0);
            }
            if (!_autonomousPathFailurePending || _autonomousPathFailureStatus == PathfindingStatus.PartialPathFound)
            {
                _autonomousTravelArrival = Owner is GameBot { IsAutonomousWorldBot: true };
            }
        }

        private void MoveToNextPathPoint()
        {
            PathPoint oldPathPoint = CurrentPathPoint;
            PathPoint nextPathPoint = CurrentPathPoint.Next;

            if ((CurrentPathPoint.Type is EPathType.Path_Reverse) && CurrentPathPoint.FiredFlag)
                nextPathPoint = CurrentPathPoint.Prev;

            if (nextPathPoint == null)
            {
                switch (CurrentPathPoint.Type)
                {
                    case EPathType.Loop:
                    {
                        CurrentPathPoint = MovementMgr.FindFirstPathPoint(CurrentPathPoint);
                        break;
                    }
                    case EPathType.Once:
                    {
                        CurrentPathPoint = null;
                        PathID = null; // Unset the path ID, otherwise the brain will re-enter patrolling state and restart it.
                        break;
                    }
                    case EPathType.Path_Reverse:
                    {
                        CurrentPathPoint = oldPathPoint.FiredFlag ? CurrentPathPoint.Next : CurrentPathPoint.Prev;
                        break;
                    }
                }
            }
            else
                CurrentPathPoint = CurrentPathPoint.Type is EPathType.Path_Reverse && CurrentPathPoint.FiredFlag ? CurrentPathPoint.Prev : CurrentPathPoint.Next;

            oldPathPoint.FiredFlag = !oldPathPoint.FiredFlag;

            if (CurrentPathPoint != null)
                PathTo(new(CurrentPathPoint.X, CurrentPathPoint.Y, CurrentPathPoint.Z), Math.Min(_moveOnPathSpeed, CurrentPathPoint.MaxSpeed));
            else
                StopMovingOnPath();
        }

        private void PrepareValuesForClient(bool wasMoving, double distanceToTarget)
        {
            // Use slightly modified object position and target position to smooth movement out client-side.
            // The real target position makes NPCs stop before it. The real object position makes NPCs teleport a bit ahead when initiating movement.
            // The reasons why it happens and the expected values by the client are unknown.
            _positionForClientTick = GameLoop.GameLoopTime;

            if (!IsDestinationValid)
            {
                _positionForClient = _ownerPosition;
                _destinationForClient = Destination;
                return;
            }

            float magic;
            float ratio;

            if (wasMoving)
                _positionForClient = _ownerPosition;
            else
            {
                magic = (float) (CurrentSpeed * 0.15);
                ratio = (float) ((distanceToTarget + magic) / distanceToTarget);
                _positionForClient = Vector3.Lerp(_destination, _ownerPosition, ratio);
            }

            magic = (float) Math.Max(15, CurrentSpeed * 0.1);
            ratio = (float) ((distanceToTarget + magic) / distanceToTarget);
            _destinationForClient = Vector3.Lerp(_ownerPosition, _destination, ratio);
        }

        private void UpdateMovement(short speed)
        {
            if (speed == 0)
                SnapAutonomousBotToGround();

            // Save current position.
            Owner.X = (int) Math.Round(_ownerPosition.X);
            Owner.Y = (int) Math.Round(_ownerPosition.Y);
            Owner.Z = (int) Math.Round(_ownerPosition.Z);

            _needsBroadcastUpdate = true;
            IsDestinationValid = false;
            bool wasMoving = IsMoving;
            CurrentSpeed = speed;
            UpdateVelocity(0);
            PrepareValuesForClient(wasMoving, 0);
        }

        private void UpdateMovement(Vector3 destination, float distanceToTarget, short speed)
        {
            if (!AllowPosition(destination)) return;
            // Save current position.
            Owner.X = (int) Math.Round(_ownerPosition.X);
            Owner.Y = (int) Math.Round(_ownerPosition.Y);
            Owner.Z = (int) Math.Round(_ownerPosition.Z);

            IsDestinationValid = distanceToTarget >= 0;
            _destination = destination;
            _needsBroadcastUpdate = true;

            bool wasMoving = IsMoving;
            CurrentSpeed = speed;
            UpdateVelocity(distanceToTarget);
            PrepareValuesForClient(wasMoving, distanceToTarget);

        }

        private bool AllowPosition(Vector3 position)
        {
            if (AutonomousRealmBoundary.Allows(Owner, position)) return true;
            _lastPositionUpdateTick = GameLoop.GameLoopTime;
            _velocity = Vector3.Zero;
            CurrentSpeed = 0;
            _pathfinder.ForceReplot = true;
            _needsBroadcastUpdate = true;
            return false;
        }

        private void SnapAutonomousBotToGround()
        {
            if (Owner is not GameBot || Owner is GameBot { IsOnStableMasterRoute: true } || Owner.CurrentZone == null)
                return;

            // A broad nearest-poly snap at every stop could land on an isolated
            // prop or another side of a wall. Only correct slight underground Z
            // with effectively unchanged XY. Never pull an actor down a floor.
            if (!AutonomousNavigationSurface.TryFloor(PathfindingProvider.Instance,
                    Owner.CurrentZone, _ownerPosition, out Vector3 snapped))
                return;

            // Preserve legitimate fine movement while correcting visible feet-
            // through-ground offsets at authoritative stop/combat positions.
            if (Vector3.DistanceSquared(_ownerPosition, snapped) <= 1f)
                return;

            _ownerPosition = snapped;
            _pathfinder.ForceReplot = true;
        }

        private ResetHeadingAction CreateResetHeadingAction()
        {
            return new(Owner, this, () =>
            {
                UnsetFlag(MovementState.TurnTo);
                _resetHeadingAction = null;
            });
        }

        private bool IsFlagSet(MovementState flag)
        {
            return (_movementState & flag) == flag;
        }

        private void SetFlag(MovementState flag)
        {
            _movementState |= flag;
        }

        private void UnsetFlag(MovementState flag)
        {
            _movementState &= ~flag;
        }

        private delegate void MovementRequestAction(Vector3 destination, short speed);

        private enum MovementRequestType
        {
            Walk,
            Path
        }

        private class MovementRequest
        {
            public MovementRequestType Type { get; private set; }
            public Vector3 Destination { get; private set; }
            public short Speed { get; private set; }

            public void Set(MovementRequestType type, Vector3 destination, short speed)
            {
                Type = type;
                Destination = destination;
                Speed = speed;
            }
        }

        private class ResetHeadingAction : ECSGameTimerWrapperBase
        {
            private NpcMovementComponent _movementComponent;
            private ushort _oldHeading;
            private long _oldMovementStartTick;
            private Action _onCompletion;

            public ResetHeadingAction(GameObject actionSource, NpcMovementComponent movementComponent, Action onCompletion) : base(actionSource)
            {
                _movementComponent = movementComponent;
                _oldHeading = actionSource.Heading;
                _oldMovementStartTick = movementComponent.MovementStartTick;
                _onCompletion = onCompletion;
            }

            protected override int OnTick(ECSGameTimer timer)
            {
                GameNPC owner = _movementComponent.Owner;

                if (_oldMovementStartTick == _movementComponent.MovementStartTick &&
                    !_movementComponent.IsMoving &&
                    owner.IsAlive &&
                    owner.ObjectState is eObjectState.Active &&
                    !owner.attackComponent.AttackState)
                {
                    _movementComponent.TurnTo(_oldHeading);
                }

                _onCompletion();
                return 0;
            }
        }

        [Flags]
        private enum MovementState
        {
            None = 0,
            Request = 1 << 1,     // Was requested to move.
            WalkTo = 1 << 2,      // Is moving and has a destination.
            Follow = 1 << 3,      // Is following an object.
            OnPath = 1 << 4,      // Is following a path / is patrolling.
            AtPathPoint = 1 << 5, // Is waiting at a path point.
            Pathfinding = 1 << 6, // Is moving using Pathfinder.
            TurnTo = 1 << 7       // Is facing a direction for a certain duration.
        }
    }
}
