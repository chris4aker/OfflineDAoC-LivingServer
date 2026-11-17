using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bots as people for 1.2, work package 5: the game side of the movement habits whose rules are
/// <see cref="BotMovementHabits"/>, <see cref="BotStickLine"/> and <see cref="BotKeepWallSpots"/>. It holds the habits'
/// counts and the BOT_MOVE_HABITS summary, the leg budget (40 mesh leg checks a second server-wide, none while the loop
/// is busy), sitting while waiting, the keep walls and the overshoot in town. Every entry asks
/// <see cref="BotPeopleScope.Check"/> first with its switch and does nothing when refused. Decisions are made at events
/// (a wait that lasted 30-50 s, an arrival); nothing here runs per tick or calls the pathfinder per tick. Tests only
/// until seen in the owner's PC log.
/// </summary>
public static class BotMovementHabitsRuntime
{
    private static readonly Logger Log = LoggerManager.Create(typeof(BotMovementHabitsRuntime));

    // ---- counts and the summary -------------------------------------------------------------------------------------

    public enum Counter
    {
        StrafeFights, Strafes, Faces, StickMembers, StickLost, Overshoots, StepBacks, Sits, WallWalks, WallSits, WallStands,
        WallGivenUp, BankLeans, BlockedLegs, BusyDrops, BudgetDrops,
    }

    private static readonly string[] CounterNames =
    [
        "strafeFights", "strafes", "faces", "stickTurns", "stickLost", "overshoots", "stepBacks", "sits", "wallWalks",
        "wallSits", "wallStands", "wallGivenUp", "bankLeans", "refusedLegs", "busyDrops", "budgetDrops",
    ];

    private static readonly long[] Counts = new long[CounterNames.Length];

    public static void Count(Counter counter, long by = 1) => Interlocked.Add(ref Counts[(int)counter], by);

    /// <summary>The window's counts (and resets them), named as in the log line.</summary>
    public static (string Name, long Count)[] TakeCounts()
    {
        var taken = new (string, long)[CounterNames.Length];
        for (int i = 0; i < CounterNames.Length; i++)
            taken[i] = (CounterNames[i], Interlocked.Exchange(ref Counts[i], 0));
        return taken;
    }

    public enum EventTag { Strafe, StickLost, Overshoot, Sit, Wall }

    private static readonly long[] LastEventAt = [long.MinValue / 2, long.MinValue / 2, long.MinValue / 2, long.MinValue / 2,
        long.MinValue / 2];

    /// <summary>One line per tag at most every <see cref="BotMovementHabits.EventLogGapMs"/> server-wide.</summary>
    public static bool ShouldLog(EventTag tag)
    {
        if (!Log.IsInfoEnabled)
            return false;
        long now = GameLoop.GameLoopTime;
        long last = Volatile.Read(ref LastEventAt[(int)tag]);
        return BotMovementHabits.ShouldLog(now, last) &&
               Interlocked.CompareExchange(ref LastEventAt[(int)tag], now, last) == last;
    }

    public static void Info(string line) => Log.Info(line);

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) => RegisterSeams();

    /// <summary>The BOT_MOVE_HABITS summary and /lstest wall (idempotent; tests call it too).</summary>
    public static void RegisterSeams()
    {
        BotPeopleSummary.Register("BOT_MOVE_HABITS", seconds => [BotMovementHabits.SummaryLine(seconds, TakeCounts())]);
        BotPeopleTest.Register("wall", WallTest);
    }

    // ---- the leg budget ---------------------------------------------------------------------------------------------

    private static readonly object BudgetLock = new();
    private static long _budgetSecond = -1;
    private static int _budgetUsed;
    private static long _routeSecond = -1;
    private static int _routeUsed;
    /// <summary>Wall route proofs (a corridor query each) at most this many a second server-wide.</summary>
    public const int WallRoutesPerSecond = 2;

    /// <summary>Takes <paramref name="checks"/> mesh leg checks from this second's budget; none while the loop is busy.</summary>
    public static bool TakeLegChecks(int checks)
    {
        if (BotMovementHabits.LoopBusy(AiProfile.LastTickMs))
        {
            Count(Counter.BusyDrops);
            return false;
        }
        lock (BudgetLock)
            if (BotMovementHabits.TakeBudget(ref _budgetSecond, ref _budgetUsed, GameLoop.GameLoopTime, checks))
                return true;
        Count(Counter.BudgetDrops);
        return false;
    }

    private static bool TakeWallRoute()
    {
        lock (BudgetLock)
            return BotMovementHabits.TakeBudget(ref _routeSecond, ref _routeUsed, GameLoop.GameLoopTime, 1, WallRoutesPerSecond);
    }

    public static RvrBotAwareness Awareness => AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS);

    /// <summary>
    /// The one place the movement habits read a bot's own tier (the overshoot; in an RvR fight the group's tier counts,
    /// owner decision 5): the tier of its skill profile when <see cref="ProfileTier"/> gives one, else this package's own
    /// roll (<see cref="BotMovementHabits.TierOf"/>, salt "MOVE"), so a bot never has two tiers once profiles exist.
    /// </summary>
    public static RvrSkillTier TierOf(GameBot bot) =>
        BotMovementHabits.TierFor(ProfileTier?.Invoke(bot), bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID, Awareness);

    /// <summary>
    /// The skill profile's tier of a bot, or null when it has none. Set where the skill profiles (work package 1) are
    /// on the branch: <c>ProfileTier = bot =&gt; BotSkill.TryProfile(bot, out var profile) ? profile.Tier : null;</c>.
    /// Unset, every bot takes the fallback roll.
    /// </summary>
    public static Func<GameBot, RvrSkillTier?> ProfileTier { get; set; }

    /// <summary>The leg's end as the mesh walks it: same zone, dry ground, the bot's own floor; null when not (as
    /// BotBrain.ReachableLeg).</summary>
    public static Vector3? ProveLeg(GameBot bot, Vector3 from, Vector3 to)
    {
        Zone zone = bot.CurrentZone;
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone) ||
            bot.CurrentRegion?.GetZone((int)to.X, (int)to.Y) != zone || zone.IsUnderwater((int)to.X, (int)to.Y, (int)to.Z))
            return null;
        Vector3? walked = AutonomousNavigationSurface.MoveAlongDryGround(nav, zone, from, to);
        if (!BotCombatMovement.LegReached(to, walked, from.Z) ||
            zone.IsUnderwater((int)walked.Value.X, (int)walked.Value.Y, (int)walked.Value.Z))
            return null;
        return walked;
    }

    /// <summary>Proves legs to <paramref name="ends"/> one after another from <paramref name="from"/>; the proven ones
    /// (stops at the first the mesh refuses). Takes the checks from the budget first: empty when it has none.</summary>
    public static Vector3[] ProveLegs(GameBot bot, Vector3 from, IReadOnlyList<Vector2> ends)
    {
        if (ends.Count == 0 || !TakeLegChecks(ends.Count))
            return [];
        var legs = new List<Vector3>(ends.Count);
        foreach (Vector2 end in ends)
        {
            if (ProveLeg(bot, from, new(end.X, end.Y, from.Z)) is not Vector3 leg)
            {
                Count(Counter.BlockedLegs);
                break;
            }
            legs.Add(leg);
            from = leg;
        }
        return legs.ToArray();
    }

    /// <summary>
    /// Walks proven legs one after another, each at <paramref name="speed"/>, keeping the bot's heading (a leg under 25
    /// units does not turn an NPC); <paramref name="face"/> (if any) is faced before each leg. A walk the bot was given
    /// meanwhile (its destination moved off the leg) ends the chain.
    /// </summary>
    public static void WalkLegs(GameBot bot, Vector3[] legs, short speed, GameObject face, Action done = null)
    {
        if (legs.Length == 0)
            return;
        int index = 0;
        void Next()
        {
            Vector3 leg = legs[index];
            if (face is { ObjectState: GameObject.eObjectState.Active } && face.CurrentRegion == bot.CurrentRegion)
                bot.TurnTo(face);
            bot.PathTo(leg, speed);
            float length = Vector2.Distance(new(bot.X, bot.Y), new(leg.X, leg.Y));
            int arrival = (int)(length * 1000 / Math.Max((short)40, speed)) + 60;
            AutonomousLivingServer.Later(bot, arrival, () => bot.IsAlive && !bot.InCombat, () =>
            {
                // Someone else moved the bot meanwhile: the rest of the walk is not from here.
                if (Vector2.Distance(new(bot.X, bot.Y), new(leg.X, leg.Y)) > BotMovementHabits.LegLength)
                    return;
                if (++index < legs.Length)
                    Next();
                else
                    done?.Invoke();
            });
        }
        Next();
    }

    // ---- overshoot (town arrivals) ----------------------------------------------------------------------------------


    private sealed class OvershootState
    {
        public Vector3 Past;
        public Vector3 Back;
        public long Until;
        /// <summary>0 pending, 1 stepped back or cancelled: set once, by whichever of the watcher and the routing comes first.</summary>
        public int Done;
        public long SteppingUntil;
    }

    private static readonly ConditionalWeakTable<GameBot, OvershootState> Overshoots = new();
    private static long _arrivals;

    /// <summary>
    /// At the start of a walk on foot to <paramref name="spot"/> (the bank, a merchant, a trainer, a crowd): whether it
    /// overshoots (by the bot's tier), and the spot 30-60 units past it on the line it comes along, proven on the mesh
    /// from <paramref name="spot"/> and accepted by <paramref name="accept"/> (inside the town). It steps back on its own
    /// (a watcher, 150-600 ms after it stops there) or when the routing sees it there (BotMovementHabits.Hangout), once.
    /// Null: no overshoot.
    /// </summary>
    public static Vector3? Overshoot(GameBot bot, Vector3 spot, Func<Vector3, bool> accept)
    {
        if (bot == null || BotPeopleScope.Check(bot, BotPeopleRule.Movement, BotPeopleSwitches.Current.MovementHabits, out _) !=
            BotPeopleRefusal.None)
            return null;
        using var aiProfile = AiProfile.Scope(AiSystem.MovementHabits);
        Vector2 from = new(bot.X, bot.Y), to = new(spot.X, spot.Y);
        long id = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        if (!BotMovementHabits.Overshoots(TierOf(bot), id, Interlocked.Increment(ref _arrivals), Vector2.Distance(from, to)))
            return null;
        Vector2 past = BotMovementHabits.OvershootSpot(from, to, BotMovementHabits.OvershootDistance(Random.Shared.NextDouble()));
        Vector3[] legs = ProveLegs(bot, spot, BotMovementHabits.Legs(to, past));
        if (legs.Length == 0 || Vector2.Distance(new(legs[^1].X, legs[^1].Y), to) < BotMovementHabits.OvershootMinimum ||
            accept != null && !accept(legs[^1]))
            return null;
        var state = new OvershootState { Past = legs[^1], Back = spot, Until = GameLoop.GameLoopTime + 60_000 };
        Overshoots.AddOrUpdate(bot, state);
        WatchOvershoot(bot, state);
        Count(Counter.Overshoots);
        return state.Past;
    }

    private static void WatchOvershoot(GameBot bot, OvershootState state)
    {
        _ = new ECSGameTimer(bot, _ =>
        {
            if (Volatile.Read(ref state.Done) != 0 || GameLoop.GameLoopTime > state.Until || !bot.IsAlive ||
                bot.ObjectState != GameObject.eObjectState.Active)
                return 0;
            if (bot.IsMoving || Vector2.Distance(new(bot.X, bot.Y), new(state.Past.X, state.Past.Y)) > 12)
                return 250;
            AutonomousLivingServer.Later(bot, (int)BotMovementHabits.StepBackDelay(Random.Shared.NextDouble()),
                () => Volatile.Read(ref state.Done) == 0 && !bot.IsMoving, () => StepBack(bot));
            return 0;
        }, 250);
    }

    /// <summary>An overshoot was started and is not yet stepped back or cancelled.</summary>
    public static bool OvershootPending(GameBot bot) =>
        bot != null && Overshoots.TryGetValue(bot, out OvershootState state) && Volatile.Read(ref state.Done) == 0 &&
        GameLoop.GameLoopTime <= state.Until;

    /// <summary>The step-back's legs are walking: the routing gives it no new route meanwhile.</summary>
    public static bool SteppingBack(GameBot bot) =>
        bot != null && Overshoots.TryGetValue(bot, out OvershootState state) && GameLoop.GameLoopTime < state.SteppingUntil;

    /// <summary>The overshoot ended without a step back (the bot was stopped at the spot itself, or the walk failed).</summary>
    public static void CancelOvershoot(GameBot bot)
    {
        if (bot != null && Overshoots.TryGetValue(bot, out OvershootState state))
            Interlocked.Exchange(ref state.Done, 1);
    }

    /// <summary>Steps back from the overshoot to the spot in short legs, facing the way it came: once, whichever of the
    /// watcher and the routing asks first.</summary>
    public static void StepBack(GameBot bot)
    {
        if (bot == null || !Overshoots.TryGetValue(bot, out OvershootState state) ||
            Interlocked.CompareExchange(ref state.Done, 1, 0) != 0)
            return;
        Vector3 back = state.Back;
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Vector3[] legs = ProveLegs(bot, here, BotMovementHabits.Legs(new(here.X, here.Y), new(back.X, back.Y)));
        if (legs.Length == 0)
            return;
        short speed = (short)Math.Clamp(bot.MaxSpeed * 45 / 100, 60, 110);
        float length = Vector2.Distance(new(here.X, here.Y), new(back.X, back.Y));
        state.SteppingUntil = GameLoop.GameLoopTime + (long)(length * 1000 / speed) + 60L * legs.Length + 800;
        WalkLegs(bot, legs, speed, null);
        Count(Counter.StepBacks);
        if (ShouldLog(EventTag.Overshoot))
            Info($"BOT_MOVE_OVERSHOOT bot=\"{bot.Name}\" id={bot.DatabaseID} region={bot.CurrentRegionID} " +
                 $"past={(int)length} tier={TierOf(bot)}");
    }

    // ---- sitting while waiting, and the keep walls ------------------------------------------------------------------

    public enum WaitSpot
    {
        /// <summary>A wait site of the controller (waiting for group members, formed up at a rendezvous, holding formation).</summary>
        Group,
        /// <summary>A party member standing in formation around its standing leader.</summary>
        Formation,
        /// <summary>At the vault keeper on a town break.</summary>
        Bank,
    }

    /// <summary>Only a formation member takes the walls (BotKeepWallSpots.Offered): its follow slot walks it there; every
    /// other wait sits where it is, so no wait site's stop order fights a wall walk.</summary>
    public static bool OffersWall(WaitSpot spot) => BotKeepWallSpots.Offered(spot == WaitSpot.Formation);

    private sealed class WaitState
    {
        public long Since;
        public long LastSeen;
        public float X, Y;
        public long Number;
        public long Delay;
        public bool Sits;
        public bool Decided;
        public bool ForceWall;
        public Vector3? WallSpot;
        public long WallStarted;
        public bool OnWall;
        public bool Standing;
        public long NextCheck;
        public AbstractGameKeep Keep;
    }

    private static readonly ConditionalWeakTable<GameBot, WaitState> Waits = new();

    /// <summary>
    /// One turn of a waiting bot (an AI turn, not a tick): after 30-50 s of waiting, 60% of waits sit. A formation member
    /// near a keep its realm holds (no siege, no enemy noticed, not a committed defender or siege fighter) first takes a
    /// guard archer's floor on the wall: the returned spot is its follow slot until it sits there or gives it up (null).
    /// </summary>
    public static Vector3? WaitTurn(GameBot bot, WaitSpot spot, GameBot leader = null)
    {
        if (bot == null || !bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAutonomousWorldBot ||
            bot.IsTemporaryGroupHelper)
            return null;
        try
        {
            long now = GameLoop.GameLoopTime;
            WaitState state = Waits.GetOrCreateValue(bot);
            if (state.WallSpot is Vector3 wall && !state.OnWall)
                return ContinueWall(bot, state, wall, spot, now);
            if (bot.IsSeatedByHabit)
            {
                state.LastSeen = now;
                if (state.OnWall)
                    WatchWall(bot, state, now);
                return state.OnWall ? state.WallSpot : null;
            }
            if (state.OnWall)
            {
                // Stood up on the wall (an enemy, a cast): the wait there is over; back to its formation slot.
                state.OnWall = false;
                state.WallSpot = null;
                state.Decided = true;
            }
            float dx = bot.X - state.X, dy = bot.Y - state.Y;
            if (!state.ForceWall && (state.Since == 0 || now - state.LastSeen > BotMovementHabits.WaitGapMs ||
                dx * dx + dy * dy > BotMovementHabits.WaitDrift * BotMovementHabits.WaitDrift))
            {
                long id = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
                state.Number++;
                state.Since = now;
                (state.X, state.Y) = (bot.X, bot.Y);
                state.Delay = BotMovementHabits.SitDelay(id, state.Number);
                state.Sits = BotMovementHabits.SitsThisWait(id, state.Number);
                state.Decided = false;
                state.WallSpot = null;
                state.OnWall = false;
            }
            state.LastSeen = now;
            bool forced = state.ForceWall && OffersWall(spot);
            if (!forced && (state.Decided || !BotMovementHabits.SitDue(now - state.Since, state.Delay)))
                return null;
            state.Decided = true;
            state.ForceWall = false;
            if (!forced && !state.Sits ||
                BotPeopleScope.Check(bot, BotPeopleRule.Movement, BotPeopleSwitches.Current.MoveSitAndWalls, out _) != BotPeopleRefusal.None)
                return null;
            if (!BotMovementHabits.MaySit(bot.IsAlive, bot.IsMoving, bot.IsCasting, bot.IsStealthed, bot.InCombat, bot.IsAttacking,
                    (bot.Brain as BotBrain)?.HasAggro == true, bot.IsCrowdControlled, bot.IsRecoveryResting, bot.IsOnStableMasterRoute))
            {
                // Busy at the moment it would have sat: it tries again a little later in the same wait.
                state.Decided = false;
                state.Delay += 5_000;
                return null;
            }
            using var aiProfile = AiProfile.Scope(AiSystem.MovementHabits);
            if (OffersWall(spot) && TryStartWall(bot, state, leader ?? bot, now, forced) is Vector3 started)
                return started;
            Sit(bot, spot, state, wall: false);
            return null;
        }
        catch (Exception exception)
        {
            // A habit is cosmetic: never let it break a bot's turn.
            Log.Debug($"BOT_MOVE_HABITS wait turn failed for {bot.Name}", exception);
            return null;
        }
    }

    private static void Sit(GameBot bot, WaitSpot spot, WaitState state, bool wall)
    {
        if (!bot.SitByHabit())
            return;
        Count(wall ? Counter.WallSits : Counter.Sits);
        if (wall ? ShouldLog(EventTag.Wall) : ShouldLog(EventTag.Sit))
            Info(wall
                ? $"BOT_MOVE_WALL bot=\"{bot.Name}\" id={bot.DatabaseID} region={bot.CurrentRegionID} keep=\"{state.Keep?.Name}\" " +
                  $"x={bot.X} y={bot.Y} z={bot.Z} waited_s={(GameLoop.GameLoopTime - state.Since) / 1000}"
                : $"BOT_MOVE_SIT bot=\"{bot.Name}\" id={bot.DatabaseID} region={bot.CurrentRegionID} where={spot.ToString().ToLowerInvariant()} " +
                  $"waited_s={(GameLoop.GameLoopTime - state.Since) / 1000}");
    }

    /// <summary>The keep this bot waits at, if any: the nearest within <see cref="BotKeepWallSpots.KeepRadius"/>.</summary>
    private static AbstractGameKeep KeepNear(GameBot bot)
    {
        if (GameServer.KeepManager == null || bot.CurrentRegion == null)
            return null;
        AbstractGameKeep best = null;
        float bestDistance = BotKeepWallSpots.KeepRadius * BotKeepWallSpots.KeepRadius;
        foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID))
        {
            if (keep == null)
                continue;
            float d = Vector2.DistanceSquared(new(keep.X, keep.Y), new(bot.X, bot.Y));
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = keep;
            }
        }
        return best;
    }

    /// <summary>Whether a live enemy player or gamebot (not hidden) stands within <paramref name="radius"/> of the bot.</summary>
    public static bool EnemyNear(GameBot bot, float radius)
    {
        ushort r = (ushort)Math.Min(ushort.MaxValue, radius);
        foreach (GamePlayer player in bot.GetPlayersInRadius(r))
            if (player.IsAlive && player.Realm != bot.Realm && player.Realm != eRealm.None && !player.IsStealthed &&
                player.Client?.Account?.PrivLevel <= 1)
                return true;
        foreach (GameNPC npc in bot.GetNPCsInRadius(r))
            if (npc is GameBot other && other.IsAlive && other.Realm != bot.Realm && other.Realm != eRealm.None && !other.IsStealthed)
                return true;
        return false;
    }

    /// <summary>Whether a siege is on at this keep: it was hit by an enemy lately, or an RvR event has it as its target.</summary>
    public static bool SiegeAt(AbstractGameKeep keep) =>
        keep.InCombat || AutonomousRvrEventLayer.IsTargetActive($"rvr-keep-{keep.KeepID}", GameLoop.GameLoopTime);

    /// <summary>Decision 11 and the scope, read afresh (no count: the scope's summary counts only the decisions).</summary>
    private static bool WallStillAllowed(GameBot bot, AbstractGameKeep keep) =>
        keep != null && BotKeepWallSpots.StillAllowed(
            BotPeopleSwitches.Current.MoveSitAndWalls && BotPeopleScope.Refusal(bot, BotPeopleRule.Movement) == BotPeopleRefusal.None,
            keep.Realm == bot.Realm, SiegeAt(keep), EnemyNear(bot, BotKeepWallSpots.WatchRadius),
            AutonomousRvrDefense.IsCommittedDefender(bot), AutonomousRvrDefense.IsCommittedSiegeFighter(bot));

    private static Vector3? TryStartWall(GameBot bot, WaitState state, GameBot anchorBot, long now, bool forced)
    {
        AbstractGameKeep keep = KeepNear(bot);
        if (keep == null)
            return null;
        if (!BotKeepWallSpots.Allowed(keep.Realm == bot.Realm, SiegeAt(keep), EnemyNear(bot, BotKeepWallSpots.WatchRadius),
                AutonomousRvrDefense.IsCommittedDefender(bot), AutonomousRvrDefense.IsCommittedSiegeFighter(bot)))
            return null;
        if (!forced && !TakeWallRoute())
            return null;
        IReadOnlyList<BotKeepWallSpots.Candidate> candidates = AutonomousRvrDefense.WallCandidates(bot, keep);
        if (candidates.Count == 0)
            return null;
        GameBot anchor = anchorBot?.CurrentRegion == bot.CurrentRegion ? anchorBot : bot;
        var taken = new List<Vector2>();
        foreach (GameNPC npc in bot.GetNPCsInRadius((ushort)(BotKeepWallSpots.MaxFromAnchor * 2)))
            if (npc is GameBot other && other != bot && Waits.TryGetValue(other, out WaitState theirs) && theirs.WallSpot is Vector3 at)
                taken.Add(new(at.X, at.Y));
        int index = BotKeepWallSpots.Choose(candidates, new(anchor.X, anchor.Y), taken, Random.Shared.NextDouble());
        if (index < 0)
            return null;
        Vector3 floor = candidates[index].Floor;
        Zone zone = bot.CurrentZone;
        IPathfindingMgr nav = PathfindingProvider.Instance;
        // The route its follow slot will walk (the ordinary mesh), proven whole once: a wall reached only through a closed
        // gate is left alone.
        if (zone == null || nav == null || !nav.IsAvailable || bot.CurrentRegion.GetZone((int)floor.X, (int)floor.Y) != zone ||
            !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, new(bot.X, bot.Y, bot.Z), floor))
        {
            Count(Counter.BlockedLegs);
            return null;
        }
        state.WallSpot = floor;
        state.WallStarted = now;
        state.NextCheck = now + BotKeepWallSpots.RecheckMs;
        state.OnWall = false;
        state.Standing = false;
        state.Keep = keep;
        Count(Counter.WallWalks);
        return floor;
    }

    /// <summary>The walk to the wall (BotKeepWallSpots.Step): the follow slot walks it, so nothing is ordered here; a
    /// re-check each second abandons it when decision 11 or the scope no longer holds.</summary>
    private static Vector3? ContinueWall(GameBot bot, WaitState state, Vector3 wall, WaitSpot spot, long now)
    {
        state.LastSeen = now;
        bool recheck = now >= state.NextCheck;
        if (recheck)
            state.NextCheck = now + BotKeepWallSpots.RecheckMs;
        float distance = Vector2.Distance(new(bot.X, bot.Y), new(wall.X, wall.Y));
        switch (BotKeepWallSpots.Step(distance, bot.IsMoving, now - state.WallStarted, recheck,
                    !recheck || WallStillAllowed(bot, state.Keep)))
        {
            case BotKeepWallSpots.WalkStep.Arrive:
                if (!WallStillAllowed(bot, state.Keep))
                    goto case BotKeepWallSpots.WalkStep.Abandon;
                state.OnWall = true;
                state.NextCheck = now + BotKeepWallSpots.RecheckMs;
                if (state.Keep != null)
                    BotTurning.TurnGradually(bot, BotCombatMovement.Heading(new(state.Keep.X, state.Keep.Y), new(bot.X, bot.Y)));
                Sit(bot, spot, state, wall: true);
                return wall;
            case BotKeepWallSpots.WalkStep.Abandon:
            case BotKeepWallSpots.WalkStep.GiveUp:
                state.WallSpot = null;
                state.OnWall = false;
                state.Decided = true;
                Count(Counter.WallGivenUp);
                return null;
            default:
                return wall;
        }
    }

    /// <summary>A bot sitting on a wall, at most once a second: an enemy noticed, a siege begun, a duty or a refusal makes
    /// it stand within 0.3-1 s (decision 11); then it rejoins its formation slot.</summary>
    private static void WatchWall(GameBot bot, WaitState state, long now)
    {
        if (state.Standing || now < state.NextCheck)
            return;
        state.NextCheck = now + BotKeepWallSpots.RecheckMs;
        if (WallStillAllowed(bot, state.Keep))
            return;
        state.Standing = true;
        AutonomousLivingServer.Later(bot, (int)BotKeepWallSpots.StandDelay(Random.Shared.NextDouble()), () => bot.IsSeatedByHabit,
            () =>
            {
                bot.StandFromHabit();
                Count(Counter.WallStands);
            });
    }

    /// <summary>Forgets a bot's wall spot once it leaves the wait (its slot is the formation's again).</summary>
    public static void EndWall(GameBot bot)
    {
        if (bot != null && Waits.TryGetValue(bot, out WaitState state))
        {
            state.WallSpot = null;
            state.OnWall = false;
        }
    }

    // ---- /lstest wall -----------------------------------------------------------------------------------------------

    /// <summary>
    /// /lstest wall: the gamebots of the GM's realm standing in formation within 1,200 units of the nearest keep it holds
    /// (no siege there) take its walls on their next formation turn: no 30-50 s wait and no roll, the same spots, proofs
    /// and decision 11 checks as the habit. Leaders, soloers and bots at a rendezvous sit where they wait.
    /// </summary>
    private static string WallTest(GamePlayer gm, GameBot target, string[] words)
    {
        GameObject from = (GameObject)target ?? gm;
        if (from?.CurrentRegion == null || GameServer.KeepManager == null)
            return "No region.";
        eRealm realm = target?.Realm ?? gm.Realm;
        AbstractGameKeep keep = GameServer.KeepManager.GetKeepsOfRegion(from.CurrentRegionID)
            .Where(k => k != null && k.Realm == realm)
            .OrderBy(k => Vector2.DistanceSquared(new(k.X, k.Y), new(from.X, from.Y))).FirstOrDefault();
        if (keep == null)
            return "No keep of that realm in this region.";
        if (SiegeAt(keep))
            return $"{keep.Name} is under siege: walls are left to its defenders (owner decision 11).";
        GameBot[] near = from.GetNPCsInRadius(2_500).OfType<GameBot>()
            .Where(b => b.Realm == realm && b.IsAlive && b.IsAutonomousWorldBot &&
                        Vector2.DistanceSquared(new(b.X, b.Y), new(keep.X, keep.Y)) <= BotKeepWallSpots.KeepRadius * BotKeepWallSpots.KeepRadius)
            .ToArray();
        if (near.Length == 0)
            return $"No gamebot of that realm stands within {(int)BotKeepWallSpots.KeepRadius} units of {keep.Name}.";
        int candidates = AutonomousRvrDefense.WallCandidates(near[0], keep).Count(c => c.Archer && c.Rise >= BotKeepWallSpots.MinimumRise);
        int marked = 0, refused = 0;
        foreach (GameBot bot in near)
        {
            if (bot.Group == null || bot.Group.LivingLeader == bot ||
                BotPeopleScope.Refusal(bot, BotPeopleRule.Movement) != BotPeopleRefusal.None || !BotPeopleSwitches.Current.MoveSitAndWalls)
            {
                refused++;
                continue;
            }
            WaitState state = Waits.GetOrCreateValue(bot);
            state.ForceWall = true;
            marked++;
        }
        return $"{keep.Name}: {candidates} guard archer floors on the wall. {marked} formation members take the walls on their " +
               $"next turn while their leader stands (decision 11 checked then); {refused} stay (a leader, a soloer, or out of scope).";
    }
}
