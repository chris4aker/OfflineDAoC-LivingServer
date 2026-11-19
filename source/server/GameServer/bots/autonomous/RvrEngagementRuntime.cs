using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Work package 4 of docs/RVR-FIELD-1.2.md, the part that touches the game: it keeps a bot group's stance facts in its
/// <see cref="RvrGroupState"/> (leader traits, notices, contact, the last fight's spot), and acts on each new plan once per
/// board: it logs the stance changes (RVR_STANCE, RVR_SIZE_UP, RVR_WATCH, RVR_REGROUP), publishes and clears the
/// group's own retreat order (BotGroupRetreat, RVR_RETREAT), and passes a retreat or regroup on to the other groups of
/// its warband. The rules are in <see cref="RvrEngagement"/> (pure). Callers check the scope first (BotBrain.RvrTacticsActive).
/// </summary>
public static class RvrEngagementRuntime
{
    private static readonly Logger Log = LoggerManager.Create(typeof(RvrEngagementRuntime));

    /// <summary>RVR_STANCE at most this often per group (the rest are counted in the next line).</summary>
    public const long StanceLogGapMilliseconds = 2_000;
    /// <summary>RVR_SIZE_UP and RVR_WATCH at most this often per group.</summary>
    public const long SizeUpLogGapMilliseconds = 10_000;
    /// <summary>A group keeps its board fresh this long after its last notice, contact or stance change.</summary>
    public const long UpkeepMilliseconds = 30_000;
    /// <summary>The reason prefix of the retreat orders the RvR stance publishes.</summary>
    public const string OrderReasonPrefix = "rvr ";

    /// <summary>One group's bookkeeping between boards. Guarded by its own lock.</summary>
    private sealed class Runtime
    {
        public readonly object Lock = new();
        /// <summary>The group's own main record as last acted on (RvrEngagement.Track): boards of other regions never move it.</summary>
        public RvrStanceLedger Ledger = RvrStanceLedger.Empty;
        /// <summary>When the group last passed a stance on (RvrEngagement.MayRelay: once per relay window).</summary>
        public long LastRelayAt = long.MinValue / 2;
        public long UpkeepUntil = long.MinValue;
        public long LastStanceLogAt = long.MinValue / 2;
        public int SuppressedStanceLogs;
        public long LastSizeUpLogAt = long.MinValue / 2;
        public long LastWatchLogAt = long.MinValue / 2;
        public long OrderId;
        public long OrderPublishedAt;
        public long OrderUntil;
        public long RegroupStartedAt;
        public string RegroupReason = string.Empty;
    }

    private static readonly ConditionalWeakTable<Group, Runtime> Groups = new();

    private static Runtime For(Group group) => Groups.GetValue(group, static _ => new Runtime());

    // ---- facts -------------------------------------------------------------------------------------------------------

    /// <summary>The group's stance facts: who leads it and how many members it has. Written only when they change.</summary>
    public static RvrGroupState EnsureFacts(GameBot bot)
    {
        Group group = bot?.Group;
        if (group == null)
            return RvrGroupState.Empty;
        RvrGroupState state = RvrFightBoards.StateOf(group);
        GameBot leader = group.LivingLeader as GameBot ?? bot;
        long key = leader.DatabaseID > 0 ? leader.DatabaseID : leader.ObjectID;
        int size = group.MemberCount;
        RvrEngagementFacts facts = state.Engagement ?? RvrEngagementFacts.Empty;
        if (facts.GroupKey == key && facts.GroupSize == size)
            return state;
        BotPersonality? personality = AutonomousBotPersonality.Applies(leader) ? AutonomousBotPersonality.For(leader) : null;
        int patience = personality?.Traits.Patience ?? 5;
        double boldness = AutonomousRvrPerception.Boldness(personality);
        return RvrFightBoards.UpdateState(group, current => current.WithEngagement(f =>
            f with { GroupKey = key, GroupSize = size, Patience = patience, Boldness = boldness }));
    }

    /// <summary>A member noticed an enemy (RvR perception let it through to the threat gate).</summary>
    public static RvrGroupState NoteNoticed(GameBot bot, long now)
    {
        EnsureFacts(bot);
        if (bot?.Group is not { } group)
            return RvrGroupState.Empty;
        lock (For(group).Lock)
            For(group).UpkeepUntil = Math.Max(For(group).UpkeepUntil, now + UpkeepMilliseconds);
        return RvrFightBoards.UpdateState(group, state => state.WithEngagement(f => Noticed(f, now)));
    }

    /// <summary>The facts after a notice at <paramref name="now"/>: a notice after a quiet spell opens a new encounter. Pure.</summary>
    public static RvrEngagementFacts Noticed(RvrEngagementFacts f, long now)
    {
        if (f.LastNoticedAt >= now)
            return f;
        return f with
        {
            LastNoticedAt = now,
            EncounterSince = now - f.LastNoticedAt > RvrEngagement.AwareMilliseconds ? now : f.EncounterSince,
        };
    }

    /// <summary>The facts after a board in contact: the fight's spot, the enemy's, and a notice. Pure.</summary>
    public static RvrEngagementFacts Contact(RvrEngagementFacts f, RvrFightBoard board) =>
        board == null || !board.InContact || f.LastContactAt >= board.BuiltAt ? f : Noticed(f, board.BuiltAt) with
        {
            LastContactAt = board.BuiltAt,
            LastFightPoint = board.FriendlyCentroid,
            LastEnemyPoint = board.EnemyCentroid ?? f.LastEnemyPoint,
        };

    /// <summary>Whether the group's board should be kept fresh even when the bot is not fighting.</summary>
    public static bool NeedsUpkeep(GameBot bot, long now)
    {
        if (bot?.Group is not { } group || !Groups.TryGetValue(group, out Runtime runtime))
            return RvrFightBoards.StateOf(bot?.Group).Engagement?.Relay != null;
        return runtime.Ledger.Stance != RvrStance.Engage || now < runtime.UpkeepUntil ||
               RvrFightBoards.StateOf(group).Engagement?.Relay != null;
    }

    /// <summary>
    /// The stance this bot acts on now: the board's, or, when facts changed after the board was built (a notice this
    /// very scan), the same rule run again on the current facts.
    /// </summary>
    public static RvrStance CurrentStance(RvrFightBoard board, RvrGroupState state, long now)
    {
        if (board?.Plan == null)
            return RvrStance.Engage;
        if (state == null || ReferenceEquals(state, board.State))
            return board.Plan.Stance;
        return RvrEngagement.PlanStance(board.Plan, board, state, RvrTacticsSwitches.Current, now).Stance;
    }

    /// <summary>The bot was hit by an enemy player or bot in the last 2.5 s.</summary>
    public static bool Hit(GameLiving living, long now) =>
        living != null && RvrEngagement.WasHit(now, living.LastAttackedByEnemyTickPvP);

    /// <summary>Hit lately with an attacker in melee: caught, so it fights back rather than run.</summary>
    public static bool HitInMelee(GameLiving living, long now) =>
        Hit(living, now) && (living.attackComponent?.AttackerTracker?.MeleeCount ?? 0) > 0;

    // ---- once per board ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Acts on a group's board once (the first member to see it): stores contact, logs a stance change, publishes or
    /// clears the group's retreat order, relays a retreat or regroup, and lets go of a used relay and a finished rally.
    /// Only the group's main body's boards count (RvrEngagement.Track): a member in another region cannot start, end or
    /// restart the group's stance.
    /// </summary>
    public static void Observe(GameBot bot, RvrFightBoard board, long now)
    {
        Group group = bot?.Group;
        if (group == null || board?.Plan == null)
            return;
        Runtime runtime = For(group);
        RvrStance previous;
        long previousSince;
        RvrFightPlan plan = board.Plan;
        bool changed;
        lock (runtime.Lock)
        {
            RvrStanceStep step = RvrEngagement.Track(runtime.Ledger, board);
            if (!step.Acted)
                return;
            runtime.Ledger = step.Next;
            previous = step.Previous;
            previousSince = step.PreviousSince;
            changed = step.Changed;
            if (board.InContact || plan.Stance != RvrStance.Engage)
                runtime.UpkeepUntil = Math.Max(runtime.UpkeepUntil, now + UpkeepMilliseconds);
        }

        RvrGroupState state = RvrFightBoards.UpdateState(group, current => current.WithEngagement(f =>
        {
            f = Contact(f, board);
            if (f.Relay is { } relay && (now >= relay.ExpiresAt ||
                    plan.Stance is RvrStance.Retreat or RvrStance.Regroup && plan.StanceSince >= relay.AppliesAt))
                f = f with { Relay = null };
            return f;
        }));

        // A new stance, or a new retreat (one that began again between two boards seen here).
        if (!changed)
        {
            KeepOrder(group, runtime, plan, now);
            return;
        }

        RvrSizeUp sizeUp = RvrEngagement.SizeUp(board, board.State.Engagement, plan.SkillTier);
        RvrEngagementOptions options = RvrEngagementOptions.From(RvrTacticsSwitches.Current, BotGroupRetreat.Enabled);
        // The reason the planner gave; recomputed only for a plan without one.
        string reason = !string.IsNullOrEmpty(plan.StanceReason) ? plan.StanceReason : RvrEngagement.Decide(previous,
            previousSince == long.MinValue ? plan.StanceSince : previousSince,
            sizeUp, board.State.Engagement, board.State, options, plan.SkillTier, board.BuiltAt).Reason;
        string groupName = GroupName(bot);
        // The relay this stance acted on: the board's stored facts are the ones the planner decided from.
        string relayFrom = reason.StartsWith("relayed", StringComparison.Ordinal)
            ? board.State.Engagement?.Relay?.From ?? "?"
            : null;
        LogStance(runtime, groupName, board, previous, plan, reason, now);

        if (previous == RvrStance.Observe || plan.Stance is RvrStance.Kite or RvrStance.Retreat && previous != RvrStance.Retreat)
            LogSizeUp(runtime, groupName, board, sizeUp, plan, reason, now);
        if (plan.Stance == RvrStance.Observe)
            LogWatch(runtime, groupName, board, sizeUp, plan, now);

        if (previous == RvrStance.Retreat && plan.Stance != RvrStance.Retreat)
            ClearOrder(group, runtime);
        if (plan.Stance == RvrStance.Retreat)
            BeginRetreat(bot, group, runtime, board, sizeUp, plan, reason, relayFrom, now);

        if (previous == RvrStance.Regroup && plan.Stance != RvrStance.Regroup)
            EndRegroup(group, runtime, groupName, board, plan, reason, now);
        if (plan.Stance == RvrStance.Regroup && previous != RvrStance.Regroup)
            BeginRegroup(bot, runtime, groupName, board, plan, reason, relayFrom, now);
    }

    private static void KeepOrder(Group group, Runtime runtime, RvrFightPlan plan, long now)
    {
        // The stance left Retreat on a board nobody here saw change: still clear our order.
        if (plan.Stance != RvrStance.Retreat && runtime.OrderId != 0)
            ClearOrder(group, runtime);
    }

    /// <summary>Whether this order is the RvR stance's own, and when it was published.</summary>
    public static bool IsOwnOrder(Group group, BotGroupRetreat.Order order, out long publishedAt)
    {
        publishedAt = 0;
        if (group == null || order == null || !Groups.TryGetValue(group, out Runtime runtime))
            return false;
        lock (runtime.Lock)
        {
            if (runtime.OrderId != order.Id)
                return false;
            publishedAt = runtime.OrderPublishedAt;
            return true;
        }
    }

    private static void ClearOrder(Group group, Runtime runtime)
    {
        long id;
        lock (runtime.Lock)
        {
            id = runtime.OrderId;
            runtime.OrderId = 0;
        }
        if (id != 0 && BotGroupRetreat.Active(group, GameLoop.GameLoopTime) is { } active && active.Id == id)
            BotGroupRetreat.Clear(group);
    }

    private static void BeginRetreat(GameBot bot, Group group, Runtime runtime, RvrFightBoard board, in RvrSizeUp sizeUp,
        RvrFightPlan plan, string reason, string relayFrom, long now)
    {
        if (!BotGroupRetreat.Enabled || !Properties.RVR_BOT_DISENGAGE)
            return;
        Vector3 fight = board.FriendlyCentroid;
        Vector3 enemies = board.EnemyCentroid ?? fight;
        Vector3 point = BotGroupRetreat.SafePoint(fight,
            BotGroupRetreat.NearestBind(board.RegionId, (int)fight.X, (int)fight.Y, board.Realm), null, enemies,
            BotGroupRetreat.RetreatPoint(fight, null, enemies));
        var order = new BotGroupRetreat.Order
        {
            Region = board.RegionId,
            Point = point,
            Until = now + RvrEngagement.MaxRetreatMilliseconds,
            Reason = $"{OrderReasonPrefix}{reason} odds={sizeUp.Odds:0.00}",
        };
        lock (runtime.Lock)
        {
            runtime.OrderId = order.Id;
            runtime.OrderPublishedAt = now;
            runtime.OrderUntil = order.Until;
        }
        BotGroupRetreat.Publish(group, order);
        RvrFightBoards.UpdateState(group, state => state.WithRally(point, board.RegionId)
            .WithEngagement(f => f with { LastRetreatAt = now }));

        int holdouts = 0, runners = 0, slowest = 0;
        foreach (RvrCombatant member in board.Members)
        {
            if (member == null || member.IsPet || member.Living == null)
                continue;
            (int delay, bool holdsOut) = RvrEngagement.RetreatReaction(plan.SkillTier, MemberKey(member.Living), order.Id);
            if (holdsOut)
                holdouts++;
            else
            {
                runners++;
                slowest = Math.Max(slowest, delay);
            }
        }
        bool relayedHere = reason.StartsWith("relayed", StringComparison.Ordinal);
        int relayed = Relay(bot, group, runtime, board, plan, RvrStance.Retreat, now, out string relay);
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_RETREAT group={GroupName(bot)} realm={GlobalConstants.RealmToName(board.Realm)} region={board.RegionId} " +
                     $"reason=\"{reason}\" odds={sizeUp.Odds:0.00} read={sizeUp.PerceivedOdds:0.00} friendly=\"{sizeUp.Friendly}\" " +
                     $"enemy=\"{sizeUp.Enemy}\" skill={plan.SkillTier} runners={runners} holdouts={holdouts} slowestMs={slowest} " +
                     $"to={board.RegionId}:{(int)point.X},{(int)point.Y} relayedTo={relayed} relay={relay} relayed={relayedHere}" +
                     (relayFrom != null ? $" from={relayFrom}" : string.Empty));
    }

    private static void BeginRegroup(GameBot bot, Runtime runtime, string groupName, RvrFightBoard board, RvrFightPlan plan,
        string reason, string relayFrom, long now)
    {
        lock (runtime.Lock)
        {
            runtime.RegroupStartedAt = now;
            runtime.RegroupReason = reason;
        }
        int relayed = Relay(bot, bot.Group, runtime, board, plan, RvrStance.Regroup, now, out string relay);
        Vector3 rally = plan.RallyPoint ?? board.FriendlyCentroid;
        RvrEngagementFacts facts = board.State.Engagement ?? RvrEngagementFacts.Empty;
        double fromFight = Vector2.Distance(new Vector2(rally.X, rally.Y), new Vector2(facts.LastFightPoint.X, facts.LastFightPoint.Y));
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_REGROUP phase=start group={groupName} realm={GlobalConstants.RealmToName(board.Realm)} region={board.RegionId} " +
                     $"reason=\"{reason}\" skill={plan.SkillTier} members={board.Friendly.Count} health={board.Friendly.HealthPercent} " +
                     $"power={board.Friendly.PowerPercent} rally={board.RegionId}:{(int)rally.X},{(int)rally.Y} " +
                     $"fromFight={(facts.LastContactAt > long.MinValue / 2 ? ((int)fromFight).ToString() : "-")} relayedTo={relayed} relay={relay} " +
                     $"minSeconds={RvrEngagement.MinRegroupMilliseconds(plan.SkillTier) / 1000}" +
                     (relayFrom != null ? $" from={relayFrom}" : string.Empty));
    }

    private static void EndRegroup(Group group, Runtime runtime, string groupName, RvrFightBoard board, RvrFightPlan plan,
        string reason, long now)
    {
        long started;
        string why;
        lock (runtime.Lock)
        {
            started = runtime.RegroupStartedAt;
            why = runtime.RegroupReason;
            runtime.RegroupStartedAt = 0;
        }
        if (plan.Stance != RvrStance.Retreat)   // a new retreat sets its own
            RvrFightBoards.UpdateState(group, state => state.RallyPoint == null ? state : state.WithRally(null, 0));
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_REGROUP phase=end group={groupName} region={board.RegionId} next={plan.Stance} reason=\"{reason}\" " +
                     $"started=\"{why}\" seconds={(started > 0 ? (now - started) / 1000 : -1)} members={board.Friendly.Count} " +
                     $"health={board.Friendly.HealthPercent} power={board.Friendly.PowerPercent}");
    }

    /// <summary>
    /// Passes a retreat or regroup to the other groups of the bot's warband: only when the plan started it for a real
    /// reason, at most once per relay window, and only to groups whose main body is in this board's region within
    /// <see cref="RvrEngagement.RelayRadius"/> (half of them, 4 s later). Returns how many; <paramref name="outcome"/> is
    /// the log word: "none" (no real reason), "window" (relayed lately), "sent".
    /// </summary>
    private static int Relay(GameBot bot, Group own, Runtime runtime, RvrFightBoard board, RvrFightPlan plan, RvrStance stance,
        long now, out string outcome)
    {
        lock (runtime.Lock)
        {
            if (!plan.StanceRelays)
            {
                outcome = "none";
                return 0;
            }
            if (!RvrEngagement.MayRelay(true, runtime.LastRelayAt, now))
            {
                outcome = "window";
                return 0;
            }
            runtime.LastRelayAt = now;
        }
        outcome = "sent";
        GameLiving[] members = RealmWarbandSupport.Get(bot)?.Members;
        if (members == null || members.Length == 0)
            return 0;
        var groups = new HashSet<Group>();
        foreach (GameLiving member in members)
            if (member?.Group is { } other && other != own)
                groups.Add(other);
        var from = new Vector2(board.FriendlyCentroid.X, board.FriendlyCentroid.Y);
        string sender = GroupName(bot);
        int told = 0;
        foreach (Group other in groups)
        {
            if (!RvrEngagement.RelayReaches(board.RegionId, from, BodyOf(other)))
                continue;
            if (RvrEngagement.RelayTo(stance, now, Random.Shared.NextDouble(), sender) is not { } relay)
                continue;
            RvrFightBoards.UpdateState(other, state => state.WithEngagement(f => f with { Relay = relay }));
            told++;
        }
        return told;
    }

    /// <summary>A group's main body (RvrEngagement.MainBody) from its living members.</summary>
    private static RvrGroupBody BodyOf(Group group)
    {
        GameLiving leader = group.LivingLeader;
        var spots = new List<RvrMemberSpot>(8);
        foreach (GameLiving member in group.GetMembersInTheGroup())
            if (member?.IsAlive == true)
                spots.Add(new RvrMemberSpot(member.CurrentRegionID, member.X, member.Y, member == leader));
        return RvrEngagement.MainBody(spots);
    }

    // ---- logs ----------------------------------------------------------------------------------------------------------

    private static void LogStance(Runtime runtime, string groupName, RvrFightBoard board, RvrStance previous, RvrFightPlan plan,
        string reason, long now)
    {
        int suppressed;
        lock (runtime.Lock)
        {
            if (now - runtime.LastStanceLogAt < StanceLogGapMilliseconds)
            {
                runtime.SuppressedStanceLogs++;
                return;
            }
            runtime.LastStanceLogAt = now;
            suppressed = runtime.SuppressedStanceLogs;
            runtime.SuppressedStanceLogs = 0;
        }
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_STANCE group={groupName} realm={GlobalConstants.RealmToName(board.Realm)} region={board.RegionId} " +
                     $"from={previous} to={plan.Stance} reason=\"{reason}\" skill={plan.SkillTier} members={board.Friendly.Count} " +
                     $"enemies={board.Enemy.Count} contact={board.InContact} wipes={board.State.WipesWithin(now)} unlogged={suppressed}");
    }

    private static void LogSizeUp(Runtime runtime, string groupName, RvrFightBoard board, in RvrSizeUp s, RvrFightPlan plan,
        string reason, long now)
    {
        lock (runtime.Lock)
        {
            if (now - runtime.LastSizeUpLogAt < SizeUpLogGapMilliseconds)
                return;
            runtime.LastSizeUpLogAt = now;
        }
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_SIZE_UP group={groupName} realm={GlobalConstants.RealmToName(board.Realm)} region={board.RegionId} " +
                     $"friendly=\"{s.Friendly}\" enemy=\"{s.Enemy}\" friendlyPower={s.FriendlyPower:0.00} enemyPower={s.EnemyPower:0.00} " +
                     $"odds={s.Odds:0.00} read={s.PerceivedOdds:0.00} thirdParty={s.ThirdParty} bigger={s.EnemyBigger} " +
                     $"kiteMelee={s.KiteMelee} contact={s.InContact} decision={plan.Stance} reason=\"{reason}\" skill={plan.SkillTier} " +
                     $"wipes={board.State.WipesWithin(now)}");
    }

    private static void LogWatch(Runtime runtime, string groupName, RvrFightBoard board, in RvrSizeUp s, RvrFightPlan plan, long now)
    {
        lock (runtime.Lock)
        {
            if (now - runtime.LastWatchLogAt < SizeUpLogGapMilliseconds)
                return;
            runtime.LastWatchLogAt = now;
        }
        RvrEngagementFacts f = board.State.Engagement ?? RvrEngagementFacts.Empty;
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_WATCH group={groupName} realm={GlobalConstants.RealmToName(board.Realm)} region={board.RegionId} " +
                     $"enemies={s.Enemy.Count} thirdParty={s.ThirdParty} distance={(double.IsInfinity(s.EnemyDistance) ? -1 : (int)s.EnemyDistance)} " +
                     $"watchMs={RvrEngagement.WatchMilliseconds(f, plan.SkillTier, plan.StanceSince)} " +
                     $"patience={f.Patience} skill={plan.SkillTier} odds={s.Odds:0.00}");
    }

    /// <summary>The group's name in the logs: its leader's database id (as RVR_FIGHT_BOARD).</summary>
    public static string GroupName(GameBot bot)
    {
        GameLiving leader = bot?.Group?.LivingLeader ?? bot;
        return (leader is GameBot leaderBot ? leaderBot.DatabaseID : leader?.ObjectID ?? 0).ToString();
    }

    /// <summary>A member's key for its rolls.</summary>
    public static long MemberKey(GameLiving living) => living is GameBot bot && bot.DatabaseID > 0 ? bot.DatabaseID : living?.ObjectID ?? 0;
}
