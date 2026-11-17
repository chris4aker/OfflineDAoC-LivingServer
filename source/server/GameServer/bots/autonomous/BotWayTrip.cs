using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// A bot (or the leader it tags along behind) as <see cref="BotWayTrip"/>'s walk sees it: plain reads and the few orders it
/// gives. <see cref="GameBotWayBody"/> is the live one; tests drive the walk with their own.
/// </summary>
public interface IBotWayBody
{
    string Name { get; }
    long Id { get; }
    eRealm Realm { get; }
    int Level { get; }
    Vector3 Position { get; }
    float Speed { get; }
    bool IsAlive { get; }
    bool IsActive { get; }
    bool IsMoving { get; }
    bool InGroup { get; }
    bool OnStableRide { get; }
    /// <summary>It needs (or is in) a rest by today's rules (BotRestRecovery): the walk hands the bot back to rest.</summary>
    bool NeedsRest { get; }
    string Assignment { get; }
    string TravelDestination { get; }
    object Region { get; }
    object Zone { get; }
    string ZoneDescription { get; }
    void PathTo(Vector3 target);
    void Stop();
    /// <summary>Takes (and clears) a failed path order, if any.</summary>
    bool TakePathFailure();
    void Turn(int headingOffset);
    /// <summary>
    /// Today's route-threat guard would act on a monster on the bot's way to <paramref name="target"/> (con yellow or worse
    /// for it, a pack it would go around, a boss): AutonomousRouteThreatPolicy.WayWalkMustYield.
    /// </summary>
    bool ThreatAhead(Vector3 target);
}

/// <summary>The live <see cref="IBotWayBody"/> of a gamebot.</summary>
public sealed class GameBotWayBody : IBotWayBody
{
    public GameBotWayBody(GameBot bot) => Bot = bot;

    public GameBot Bot { get; }

    public string Name => Bot.Name;
    public long Id => Bot.DatabaseID;
    public eRealm Realm => Bot.Realm;
    public int Level => Bot.Level;
    public Vector3 Position => new(Bot.X, Bot.Y, Bot.Z);
    public float Speed => Bot.MaxSpeed;
    public bool IsAlive => Bot.IsAlive;
    public bool IsActive => Bot.ObjectState == GameObject.eObjectState.Active;
    public bool IsMoving => Bot.IsMoving;
    public bool InGroup => Bot.Group != null;
    public bool OnStableRide => Bot.IsOnStableMasterRoute;
    public bool NeedsRest => BotRestRecovery.NeedsOrContinuesRest(Bot);
    public string Assignment => Bot.PersistentRecord?.ObjectiveAssignmentId;
    public string TravelDestination => Bot.PersistentRecord?.TravelDestination;
    public object Region => Bot.CurrentRegion;
    public object Zone => Bot.CurrentZone;
    public string ZoneDescription => Bot.CurrentZone?.Description;

    public void PathTo(Vector3 target)
    {
        Bot.ForcePathReplot();
        Bot.PathTo(target, Bot.MaxSpeed);
    }

    public void Stop()
    {
        if (Bot.IsOnStableMasterRoute)
            return;
        Bot.StopMovingOnPath();
        Bot.StopMoving();
    }

    public bool TakePathFailure() => Bot.TryConsumeAutonomousPathFailure(out _, out _);

    public void Turn(int headingOffset) => BotTurning.TurnGradually(Bot, AutonomousBotBodyLanguage.Turned(Bot.Heading, headingOffset));

    public bool ThreatAhead(Vector3 target) => BotWayFinding.ThreatAhead(Bot, target);
}

/// <summary>
/// One gamebot's way finding on its trips, owned by its AutonomousWorldBotController. <see cref="Observe"/> runs every turn
/// and costs two compares unless the bot's travel destination changed (a trip starts: the choice is made once, here) or it
/// died. <see cref="Walk"/> owns the bot's walk while it tags along or takes a wrong road, and returns false at once otherwise.
/// Every order is one PathTo per leg or per second at most; nothing is planned per tick.
///
/// Neither walk is less safe than today's travel: every 2.5 s it looks ahead as today's route-threat guard does, and when the
/// guard would act (a monster yellow or worse to the bot, a pack, a boss) a follow ends and a wrong road turns back, so the
/// controller and its guard take over; a death during either ends it and is counted. A follow also ends when the leader stops
/// for 40 s, and either walk ends when the bot needs a rest by today's rules.
/// </summary>
public sealed class BotWayTrip
{
    private enum Phase : byte { None, PendingWrongRoad, Out, Look, Back, PendingFollow, Following }

    /// <summary>A follow ends when its leader has not moved for this long (resting, selling, waiting).</summary>
    public const long LeaderIdleMilliseconds = 40_000;

    /// <summary>The leader counts as moving when it went this far since the last look.</summary>
    public const float LeaderMovedDistance = 64f;

    /// <summary>A destination decided this recently is the same journey (no second choice after a stable ride's town).</summary>
    public const long JourneyMilliseconds = 30 * 60_000;

    private string _seenDestination;
    private string _trip;
    private string _travelKey;
    private long _tripStartedTick;
    private RvrSkillTier _tier;
    private bool _wrongRoadUsed;
    private int _attempts;
    private BotWrongRoadRefusal _lastRefusal;
    private Phase _phase;
    private long _nextCheckTick;
    private long _nextThreatTick;
    private long _lastWalkTick;
    private long _phaseStartedTick;
    private long _wrongStartedTick;
    private long _wrongEndedTick;
    private long _followEndedTick;
    private bool _lookedOtherWay;
    private string _assignment;
    private Vector3 _fork;
    private Vector3 _branch;
    private BotWrongLeg _leg;
    private IBotWayBody _leader;
    private BotPlaceKey[] _tripPlaces = [];
    private float _followDistance;
    private Vector3 _lastOrder;
    private long _lastOrderTick;
    private Vector3 _leaderSeenAt;
    private long _leaderMovedTick;
    private long _lastDeathTick;
    private bool _endedPending;
    private bool _tripUnknown;
    private bool _dead;
    private string _lastResult;
    private GameBotWayBody _body;
    private readonly List<Vector3> _anchors = new(16);
    private readonly (string Destination, long Tick)[] _recent = new (string, long)[4];

    /// <summary>Whether this bot is tagging along or on a wrong road now.</summary>
    public bool IsWalking => _phase is Phase.Out or Phase.Look or Phase.Back or Phase.Following;

    public bool IsFollowing => _phase == Phase.Following;

    public bool IsOnWrongRoad => _phase is Phase.Out or Phase.Look or Phase.Back;

    /// <summary>"out", "look", "back", "following", "pending_wrong_road", "pending_follow" or "none" (tests and /lswhy).</summary>
    public string PhaseName => _phase switch
    {
        Phase.Out => "out",
        Phase.Look => "look",
        Phase.Back => "back",
        Phase.Following => "following",
        Phase.PendingWrongRoad => "pending_wrong_road",
        Phase.PendingFollow => "pending_follow",
        _ => "none",
    };

    /// <summary>How the last follow or wrong road ended ("back", "threat", "cap", "leader_stopped", "died", ...), or null.</summary>
    public string LastResult => _lastResult;

    private GameBotWayBody BodyOf(GameBot bot) => _body != null && _body.Bot == bot ? _body : _body = new GameBotWayBody(bot);

    // ---- every turn ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every controller turn, alive or not: a death ends a follow or wrong road and is counted; a new trip is decided once.
    /// Two compares when nothing changed.
    /// </summary>
    public void Observe(GameBot bot, long now)
    {
        GameBotWayBody body = BodyOf(bot);
        if (!bot.IsAlive)
        {
            ObserveDeath(body, now);
            return;
        }
        _dead = false;
        // /lstest lost on a bot already walking to a place it does not know: this trip takes it.
        if (BotWayFinding.AnyForced && _tripUnknown && !_wrongRoadUsed && _phase == Phase.None && bot.Group == null &&
            BotWayFinding.TakeForcedWrongRoad(bot))
            BotWayFinding.NoteTrip(bot, $"{_trip}: {PendWrongRoad(now, true)}.");
        string destination = bot.PersistentRecord?.TravelDestination;
        if (ReferenceEquals(destination, _seenDestination))
            return;
        _seenDestination = destination;
        if (string.IsNullOrWhiteSpace(destination) || string.Equals(destination, _trip, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination, bot.CurrentZone?.Description, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination, bot.CurrentRegion?.Description, StringComparison.OrdinalIgnoreCase) ||
            !AutonomousBotVoices.IsPlaceName(destination))
            return;
        // A stable ride's town or a zone connection on the way names another place for a while; the journey is the same.
        if (SameJourney(destination, now))
        {
            _trip = destination;
            return;
        }
        try
        {
            using var aiProfile = AiProfile.Scope(AiSystem.WayFinding); // AI_PROFILE: measures only
            StartTrip(bot, destination, now);
        }
        catch (Exception exception)
        {
            _phase = Phase.None;
            LoggerManager.Create(typeof(BotWayTrip)).Warn($"Bots as people: way finding failed for {bot.Name}", exception);
        }
    }

    /// <summary>
    /// The bot is dead: a wrong road or follow it was on ends ("died") and counts in BOT_WAY_SUMMARY's deaths, as does a death
    /// within 60 s after one. Once per death (until <see cref="ObserveAlive"/>).
    /// </summary>
    public void ObserveDeath(IBotWayBody body, long now)
    {
        _lastDeathTick = now;
        if (_dead)
            return;
        _dead = true;
        if (IsOnWrongRoad)
        {
            EndWrongRoad(body, now, "died");
            BotWayFinding.CountWrongRoadDeath();
        }
        else if (_phase == Phase.Following)
        {
            EndFollow(body, now, "died");
            BotWayFinding.CountFollowDeath();
        }
        else
        {
            _phase = Phase.None;
            if (_wrongEndedTick != 0 && now - _wrongEndedTick <= BotWayFinding.DeathWindowMilliseconds)
                BotWayFinding.CountWrongRoadDeath();
            if (_followEndedTick != 0 && now - _followEndedTick <= BotWayFinding.DeathWindowMilliseconds)
                BotWayFinding.CountFollowDeath();
        }
        _wrongEndedTick = 0;
        _followEndedTick = 0;
    }

    /// <summary>The bot is alive again (Observe does this every live turn): its next death counts.</summary>
    public void ObserveAlive() => _dead = false;

    private bool SameJourney(string destination, long now)
    {
        int oldest = 0;
        for (int i = 0; i < _recent.Length; i++)
        {
            if (_recent[i].Destination != null && now - _recent[i].Tick < JourneyMilliseconds &&
                string.Equals(_recent[i].Destination, destination, StringComparison.OrdinalIgnoreCase))
                return true;
            if (_recent[i].Tick < _recent[oldest].Tick)
                oldest = i;
        }
        _recent[oldest] = (destination, now);
        return false;
    }

    // ---- the trip's choice (once) ---------------------------------------------------------------------------------------

    private void StartTrip(GameBot bot, string destination, long now)
    {
        GameBotWayBody body = BodyOf(bot);
        if (IsOnWrongRoad)
            EndWrongRoad(body, now, "new_trip");
        else if (_phase == Phase.Following)
            EndFollow(body, now, "new_trip");
        _phase = Phase.None;
        _trip = destination;
        _tripUnknown = false;
        _tripStartedTick = now;
        _wrongRoadUsed = false;
        _attempts = 0;
        _lastRefusal = BotWrongRoadRefusal.None;
        string key = BotWayFinding.TravelKeyFor(bot.Realm, destination);
        BotWayFinding.NoteTraveller(bot, _travelKey, key);
        _travelKey = key;
        BotPeopleSwitches switches = BotPeopleSwitches.Current;
        if (!switches.PlaceKnowledge)
            return;
        if (BotPeopleScope.Check(bot, BotPeopleRule.WayFinding, switches.PlaceKnowledge, out _) != BotPeopleRefusal.None)
            return;
        BotPlaceKnowing knowing = BotWayFinding.KnowingOf(bot, destination);
        BotWayFinding.CountTrip(knowing != BotPlaceKnowing.Known);
        if (knowing == BotPlaceKnowing.Known)
            return;
        _tripUnknown = true;
        _tier = BotWayFinding.TierOf(bot);
        BotWayGroup group = GroupOf(bot, destination);
        BotWayChoice choice = BotWayFinding.Choose(knowing, _tier, group, switches.AskTheWay, switches.WrongRoads, Random.Shared.NextDouble());
        bool forced = !group.InGroup && switches.WrongRoads && BotWayFinding.TakeForcedWrongRoad(bot);
        if (forced)
            choice = BotWayChoice.WrongRoad;
        BotWayFinding.CountChoice(_tier, choice);
        string what = choice switch
        {
            BotWayChoice.Ask => Asked(bot, destination, now),
            BotWayChoice.Follow => BeginFollow(bot, destination, now) ? $"tags along behind {_leader?.Name}" : NoFollow(),
            BotWayChoice.WrongRoad => PendWrongRoad(now, forced),
            _ => group.InGroup ? (group.IsLeader ? "a member knows the way" : "takes its leader's way") : "knows the way anyway",
        };
        BotWayFinding.NoteTrip(bot, $"{destination} ({BotWayFinding.KnowingName(knowing)}, {BotWayFinding.TierName(_tier)}): {what}.");
    }

    private string Asked(GameBot bot, string destination, long now)
    {
        BotWayAskOutcome outcome = BotWayFinding.Ask(bot, destination, _tier);
        if (outcome == BotWayAskOutcome.Said)
            return "asked the way";
        // Nobody to ask: a solo bot looks for someone to tag along behind, then goes by its own reckoning.
        if (bot.Group == null && BotPeopleSwitches.Current.AskTheWay && BeginFollow(bot, destination, now))
            return $"could not ask ({BotWayFinding.OutcomeName(outcome)}), tags along behind {_leader?.Name}";
        return $"could not ask ({BotWayFinding.OutcomeName(outcome)}), goes by its own reckoning";
    }

    private string NoFollow()
    {
        // The search budget was spent this second: look again for a few seconds before going alone.
        if (_phase == Phase.PendingFollow)
            return "looks for someone going there";
        BotWayFinding.CountFollow(false);
        return "nobody to tag along behind, goes by its own reckoning";
    }

    private string PendWrongRoad(long now, bool forced)
    {
        _phase = Phase.PendingWrongRoad;
        _nextCheckTick = now + BotWayFinding.WrongRoadEarliestMilliseconds;
        return forced ? "takes a wrong road (/lstest lost)" : "may take a wrong road";
    }

    private static BotWayGroup GroupOf(GameBot bot, string destination)
    {
        Group group = bot.Group;
        if (group == null || group.MemberCount <= 1)
            return BotWayGroup.Solo;
        bool leader = group.LivingLeader == bot;
        bool anyKnows = false;
        if (leader)
            foreach (GameLiving member in group.GetMembersInTheGroup())
                if (member is GameBot other && other != bot && BotWayFinding.KnowingOf(other, destination) == BotPlaceKnowing.Known)
                {
                    anyKnows = true;
                    break;
                }
        return new BotWayGroup(true, leader, anyKnows);
    }

    private bool BeginFollow(GameBot bot, string destination, long now)
    {
        if (!BotWayFinding.FollowSearchAllowed(now))
        {
            _phase = Phase.PendingFollow;
            _phaseStartedTick = now;
            _nextCheckTick = now + 250;
            return false;
        }
        GameBot leader = BotWayFinding.FindLeader(bot, destination, _travelKey);
        if (leader == null)
            return false;
        BotPlaceKey[] places = System.Linq.Enumerable.ToArray(BotPlaceCatalog.Current.For(destination));
        float distance = BotWayFinding.FollowNearest + (float)Random.Shared.NextDouble() * (BotWayFinding.FollowFarthest - BotWayFinding.FollowNearest);
        StartFollow(BodyOf(bot), new GameBotWayBody(leader), destination, places, distance, now);
        BotWayFinding.CountFollow(true);
        BotWayFinding.Line(() => $"BOT_WAY_FOLLOW bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} " +
                                 $"leader={leader.Name} leaderId={leader.DatabaseID} leaderLevel={leader.Level} dest=\"{destination}\" " +
                                 $"distance={(int)Vector2.Distance(new(bot.X, bot.Y), new(leader.X, leader.Y))} at={(int)distance}");
        return true;
    }

    /// <summary>
    /// Starts tagging along behind <paramref name="leader"/> toward <paramref name="destination"/> (whose places are
    /// <paramref name="destinationPlaces"/>: the follow ends in their zone), keeping <paramref name="distance"/> (300-600) behind.
    /// The live trip calls it once it found a leader; tests call it with their own bodies.
    /// </summary>
    public void StartFollow(IBotWayBody body, IBotWayBody leader, string destination, BotPlaceKey[] destinationPlaces, float distance, long now)
    {
        _trip = destination;
        _tripPlaces = destinationPlaces ?? [];
        _leader = leader;
        _followDistance = Math.Clamp(distance, BotWayFinding.FollowNearest, BotWayFinding.FollowFarthest);
        _phase = Phase.Following;
        _phaseStartedTick = now;
        _lastWalkTick = now;
        _nextCheckTick = now;
        _nextThreatTick = now;
        _assignment = body.Assignment;
        _lastOrderTick = 0;
        _leaderSeenAt = leader.Position;
        _leaderMovedTick = now;
    }

    // ---- the walk -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The controller's turn after its combat and casting checks: true while this bot's tag-along or wrong road owns its walk
    /// (the controller then does nothing else this turn). <paramref name="ended"/> is true on the turn one ends: the controller
    /// then re-plans its route, with its route-threat guard, from where the bot stands. False at once when there is nothing to do.
    /// </summary>
    public bool Walk(GameBot bot, long now, Vector3? goal, out bool ended)
    {
        ended = _endedPending;
        _endedPending = false;
        if (_phase == Phase.None)
            return false;
        if (_phase is Phase.PendingWrongRoad or Phase.PendingFollow && now < _nextCheckTick)
            return false;
        using var aiProfile = AiProfile.Scope(AiSystem.WayFinding); // AI_PROFILE: measures only
        bool owns;
        try
        {
            owns = _phase switch
            {
                Phase.PendingWrongRoad => TryStartWrongRoad(bot, now, goal),
                Phase.PendingFollow => RetryFollow(bot, now),
                _ => Step(BodyOf(bot), now, out _),
            };
        }
        catch (Exception exception)
        {
            LoggerManager.Create(typeof(BotWayTrip)).Warn($"Bots as people: way finding walk failed for {bot.Name}", exception);
            Stop(BodyOf(bot), "error");
            owns = false;
        }
        ended |= _endedPending;
        _endedPending = false;
        return owns;
    }

    /// <summary>
    /// One turn of a started follow or wrong road (the live Walk calls it; tests call it with their own body). True while the
    /// walk still owns the bot; <paramref name="ended"/> is true on the turn it ends.
    /// </summary>
    public bool Step(IBotWayBody body, long now, out bool ended)
    {
        bool owns = _phase switch
        {
            Phase.Following => Follow(body, now),
            Phase.Out or Phase.Look or Phase.Back => WrongRoad(body, now),
            _ => false,
        };
        ended = _endedPending;
        _endedPending = false;
        return owns;
    }

    private bool RetryFollow(GameBot bot, long now)
    {
        long started = _phaseStartedTick;
        _phase = Phase.None;
        if (_trip == null || now - started > 10_000)
        {
            BotWayFinding.CountFollow(false);
            return false;
        }
        if (BeginFollow(bot, _trip, now))
            return true;
        if (_phase == Phase.PendingFollow)
            _phaseStartedTick = started;
        else
            BotWayFinding.CountFollow(false);
        return false;
    }

    // ---- the wrong road ---------------------------------------------------------------------------------------------

    private bool TryStartWrongRoad(GameBot bot, long now, Vector3? goal)
    {
        _nextCheckTick = now + BotWayFinding.PendingCheckMilliseconds;
        if (now - _tripStartedTick > BotWayFinding.WrongRoadLatestMilliseconds || !BotPeopleSwitches.Current.WrongRoads)
        {
            _phase = Phase.None;
            BotWayFinding.CountRefusal(BotPeopleSwitches.Current.WrongRoads ? (_lastRefusal == BotWrongRoadRefusal.None
                ? BotWrongRoadRefusal.NotWalking : _lastRefusal) : BotWrongRoadRefusal.SwitchedOff);
            return false;
        }
        Zone zone = bot.CurrentZone;
        string activity = bot.PersistentRecord?.Activity;
        bool walking = goal.HasValue && bot.IsMoving && zone != null && activity != null &&
                       activity.StartsWith("Traveling to", StringComparison.Ordinal) &&
                       string.Equals(bot.PersistentRecord?.TravelDestination, _trip, StringComparison.OrdinalIgnoreCase) &&
                       AutonomousObjectiveAssignments.KindFor(bot) is eAutonomousObjectiveKind.SoloPve or eAutonomousObjectiveKind.GroupPve &&
                       !BotRestRecovery.NeedsOrContinuesRest(bot);
        var facts = new BotWrongRoadFacts(true, _wrongRoadUsed, bot.IsOnStableMasterRoute, bot.Group != null,
            zone?.IsRvR == true || bot.CurrentRegion?.IsRvR == true || AutonomousBotVoices.IsBattlegroundRegion(bot.CurrentRegionID),
            zone?.IsDungeon == true || AutonomousBotVoices.InDungeon(bot), false, bot.HealthPercent,
            _lastDeathTick != 0 && now - _lastDeathTick < BotWayFinding.DeathWindowMilliseconds, walking);
        BotWrongRoadRefusal refusal = BotWayFinding.WrongRoadRefusal(facts);
        if (refusal != BotWrongRoadRefusal.None)
        {
            _lastRefusal = refusal;
            if (refusal is BotWrongRoadRefusal.StableRide or BotWrongRoadRefusal.Grouped or BotWrongRoadRefusal.UsedThisTrip)
            {
                // A ride or a group lasts the trip: it goes on as today.
                _phase = Phase.None;
                BotWayFinding.CountRefusal(refusal);
            }
            return false;
        }
        if (_attempts >= BotWayFinding.WrongRoadAttemptsPerTrip)
        {
            _phase = Phase.None;
            BotWayFinding.CountRefusal(_lastRefusal == BotWrongRoadRefusal.None ? BotWrongRoadRefusal.NoBranch : _lastRefusal);
            return false;
        }
        Vector3 from = new(bot.X, bot.Y, bot.Z);
        AutonomousZoneRoads.AnchorsNear(zone, from, _anchors);
        int branch = BotWayFinding.WrongBranch(from, goal.Value, _anchors, Random.Shared.NextDouble());
        if (branch < 0)
        {
            _lastRefusal = BotWrongRoadRefusal.NoBranch;
            return false;
        }
        _attempts++;
        Vector3 anchor = _anchors[branch];
        float branchDistance = Vector2.Distance(new(from.X, from.Y), new(anchor.X, anchor.Y));
        BotWrongLeg leg = BotWayFinding.LegFor(branch, branchDistance, Random.Shared.NextDouble(), Random.Shared.NextDouble(), bot.MaxSpeed);
        if (leg.IsNone)
        {
            _lastRefusal = BotWrongRoadRefusal.NoBranch;
            return false;
        }
        Vector3 end = from + (anchor - from) * (leg.Length / Math.Max(1f, Vector3.Distance(from, anchor)));
        if (BotWayFinding.WrongLegThreatened(bot, from, end))
        {
            _lastRefusal = BotWrongRoadRefusal.Threat;
            return false;
        }
        // Both ways proven before it starts, so the way back exists (no navmesh change; two checks, cached by the itinerary).
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (nav == null || !AutonomousZoneItinerary.HasDirectCorridor(nav, zone, from, anchor) ||
            !AutonomousZoneItinerary.HasDirectCorridor(nav, zone, anchor, from))
        {
            _lastRefusal = BotWrongRoadRefusal.NoBranch;
            return false;
        }
        StartWrongRoad(BodyOf(bot), from, anchor, leg, now);
        return true;
    }

    /// <summary>
    /// Starts a wrong road from <paramref name="fork"/> toward the route anchor <paramref name="branch"/> (both ways proven by
    /// the caller): out for the leg's length, a look around, back to the fork. The live trip calls it once every check passed;
    /// tests call it with their own body.
    /// </summary>
    public void StartWrongRoad(IBotWayBody body, Vector3 fork, Vector3 branch, BotWrongLeg leg, long now)
    {
        _wrongRoadUsed = true;
        _fork = fork;
        _branch = branch;
        _leg = leg;
        _wrongStartedTick = now;
        _assignment = body.Assignment;
        _nextThreatTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds;
        Begin(Phase.Out, now);
        body.Stop();
        body.PathTo(branch);
        _lastWalkTick = now;
    }

    private void Begin(Phase phase, long now)
    {
        _phase = phase;
        _phaseStartedTick = now;
        _lookedOtherWay = false;
    }

    /// <summary>Something else owned the bot in between, or its state no longer allows the walk (both walks).</summary>
    private bool Interrupted(IBotWayBody body, long now) =>
        now - _lastWalkTick > 5_000 || body.InGroup || body.OnStableRide || !body.IsAlive || body.NeedsRest ||
        !string.Equals(body.Assignment, _assignment, StringComparison.Ordinal);

    /// <summary>Today's route-threat look, every 2.5 s while a walk owns the bot.</summary>
    private bool ThreatDue(long now)
    {
        if (now < _nextThreatTick)
            return false;
        _nextThreatTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds;
        return true;
    }

    private bool WrongRoad(IBotWayBody body, long now)
    {
        // Anything else that owned the bot in between (a fight, a group, a new task, a ride, a rest) ends the wrong road.
        if (Interrupted(body, now))
            return EndWrongRoad(body, now, "interrupted");
        _lastWalkTick = now;
        if (BotWayFinding.MustEnd(now - _wrongStartedTick))
            return EndWrongRoad(body, now, "cap");
        Vector3 at = body.Position;
        long inPhase = now - _phaseStartedTick;
        bool failed = body.TakePathFailure();
        switch (_phase)
        {
            case Phase.Out:
                if (failed)
                {
                    Back(body, now);
                    return true;
                }
                // Today's guard would steer around something ahead: turn back the way it came (that way was clear).
                if (ThreatDue(now) && body.ThreatAhead(_branch))
                {
                    _lastResult = "threat";
                    Back(body, now);
                    return true;
                }
                float out2D = Vector2.Distance(new(at.X, at.Y), new(_fork.X, _fork.Y));
                long outLimit = (long)(2_000 * _leg.Length / Math.Max(1f, body.Speed)) + 10_000;
                if (out2D >= _leg.Length || Vector3.Distance(at, _branch) <= BotWayFinding.WrongLegAnchorMargin ||
                    inPhase > outLimit || !body.IsMoving && inPhase > 3_000)
                {
                    body.Stop();
                    Begin(Phase.Look, now);
                    body.Turn(900 + Random.Shared.Next(300));
                }
                return true;
            case Phase.Look:
                if (!_lookedOtherWay && inPhase >= _leg.LookMilliseconds / 2)
                {
                    _lookedOtherWay = true;
                    body.Turn(-(1_800 + Random.Shared.Next(300)));
                }
                if (inPhase >= _leg.LookMilliseconds)
                    Back(body, now);
                return true;
            default:
                if (failed)
                    return EndWrongRoad(body, now, "own_route");
                if (Vector3.Distance(at, _fork) <= 120)
                    return EndWrongRoad(body, now, _lastResult == "threat" ? "back_threat" : "back");
                // Something ahead on the way back too: the controller and its guard take over from here.
                if (ThreatDue(now) && body.ThreatAhead(_fork))
                    return EndWrongRoad(body, now, "threat");
                long backLimit = (long)(2_000 * _leg.Length / Math.Max(1f, body.Speed)) + 15_000;
                if (inPhase > backLimit)
                    return EndWrongRoad(body, now, "own_route");
                if (!body.IsMoving && inPhase > 3_000)
                {
                    // One more order back along the proven leg; the controller takes over if that stalls too.
                    body.PathTo(_fork);
                    _phaseStartedTick = now - backLimit / 2;
                }
                return true;
        }
    }

    private void Back(IBotWayBody body, long now)
    {
        Begin(Phase.Back, now);
        _nextThreatTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds;
        body.PathTo(_fork);
    }

    private bool EndWrongRoad(IBotWayBody body, long now, string result)
    {
        long cost = now - _wrongStartedTick;
        Stop(body, result);
        _wrongEndedTick = now;
        BotWayFinding.CountWrongRoad(cost);
        BotWrongLeg leg = _leg;
        string trip = _trip;
        RvrSkillTier tier = _tier;
        Vector3 fork = _fork;
        BotWayFinding.Line(() => $"BOT_WAY_WRONG_ROAD bot={body.Name} id={body.Id} realm={GlobalConstants.RealmToName(body.Realm)} " +
                                 $"level={body.Level} tier={BotWayFinding.TierName(tier)} dest=\"{trip}\" zone=\"{body.ZoneDescription}\" " +
                                 $"cost_s={cost / 1000.0:0.0} length={(int)leg.Length} look_s={leg.LookMilliseconds / 1000.0:0.0} result={result} " +
                                 $"at={(int)fork.X},{(int)fork.Y},{(int)fork.Z}");
        BotWayFinding.NoteTrip(body.Id, $"{trip} ({BotWayFinding.TierName(tier)}): took a wrong road of {(int)leg.Length} units, " +
                                        $"{cost / 1000.0:0} s, then {(result is "back" or "back_threat" ? "went back to its route" : "planned on from there")} ({result}).");
        return false;
    }

    // ---- tagging along ----------------------------------------------------------------------------------------------

    private bool Follow(IBotWayBody body, long now)
    {
        // Cheap checks every turn; the leader, the zone line and the threat look once a second (no allocation).
        if (Interrupted(body, now) ||
            !string.IsNullOrEmpty(body.TravelDestination) && !string.Equals(body.TravelDestination, _trip, StringComparison.OrdinalIgnoreCase))
            return EndFollow(body, now, "interrupted");
        _lastWalkTick = now;
        if (now < _nextCheckTick)
            return true;
        _nextCheckTick = now + 1_000;
        IBotWayBody leader = _leader;
        string reason = null;
        if (now - _phaseStartedTick > BotWayFinding.FollowMaxMilliseconds)
            reason = "time";
        else if (leader == null || !leader.IsActive || !leader.IsAlive || leader.Region != body.Region || leader.OnStableRide ||
                 !string.Equals(leader.TravelDestination, _trip, StringComparison.OrdinalIgnoreCase))
            reason = "leader_gone";
        else if (leader.Zone != body.Zone || InDestinationZone(body.ZoneDescription))
            reason = "zone_line";
        else if (body.TakePathFailure())
            reason = "no_path";
        Vector3 at = body.Position;
        Vector3 leaderAt = leader?.Position ?? at;
        float distance = Vector2.Distance(new(at.X, at.Y), new(leaderAt.X, leaderAt.Y));
        if (reason == null && distance > BotWayFinding.FollowLostDistance)
            reason = "lost";
        if (reason == null)
        {
            if (leader.IsMoving || Vector3.DistanceSquared(leaderAt, _leaderSeenAt) > BotWayTrip.LeaderMovedDistance * BotWayTrip.LeaderMovedDistance)
            {
                _leaderSeenAt = leaderAt;
                _leaderMovedTick = now;
            }
            else if (now - _leaderMovedTick > LeaderIdleMilliseconds)
                reason = "leader_stopped";
        }
        // Today's guard would act on a monster on the way: the follow ends and the controller's own travel (and guard) goes on.
        if (reason == null && ThreatDue(now) && body.ThreatAhead(leaderAt))
            reason = "threat";
        if (reason != null)
            return EndFollow(body, now, reason);
        switch (BotWayFinding.FollowStep(distance, _followDistance))
        {
            case -1:
                if (body.IsMoving)
                    body.Stop();
                break;
            case 1:
                if (!body.IsMoving || now - _lastOrderTick >= 1_500 && Vector3.DistanceSquared(leaderAt, _lastOrder) > 250 * 250)
                {
                    body.PathTo(leaderAt);
                    _lastOrder = leaderAt;
                    _lastOrderTick = now;
                }
                break;
        }
        return true;
    }

    private bool InDestinationZone(string zone)
    {
        if (_tripPlaces.Length == 0 || string.IsNullOrEmpty(zone))
            return false;
        BotPlaceKey[] here = BotPlaceCatalog.Current.At(zone);
        for (int i = 0; i < here.Length; i++)
            for (int j = 0; j < _tripPlaces.Length; j++)
                if (here[i] == _tripPlaces[j])
                    return true;
        return false;
    }

    private bool EndFollow(IBotWayBody body, long now, string reason)
    {
        IBotWayBody leader = _leader;
        string trip = _trip;
        Stop(body, reason);
        _followEndedTick = now;
        BotWayFinding.Line(() => $"BOT_WAY_FOLLOW bot={body.Name} id={body.Id} realm={GlobalConstants.RealmToName(body.Realm)} " +
                                 $"leader={leader?.Name} dest=\"{trip}\" ended={reason}");
        return false;
    }

    /// <summary>Ends whatever this trip's way finding is doing; the bot stands where it is and the controller plans on.</summary>
    private void Stop(IBotWayBody body, string reason)
    {
        Phase was = _phase;
        _phase = Phase.None;
        _leader = null;
        _lastResult = reason;
        if (was is Phase.Out or Phase.Look or Phase.Back or Phase.Following)
        {
            _endedPending = true;
            body.TakePathFailure();
            body.Stop();
        }
    }
}
