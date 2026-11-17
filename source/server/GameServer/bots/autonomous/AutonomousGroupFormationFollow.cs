using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Autonomous party members keep their formation slot continuously, the way companions and pets follow
    /// (owner 2026-10-17: "they need to move as one unit like how companions follow me"). A follower's AI turn
    /// comes only every 2.6-4.2 s at a full population, and it used to walk to a slot 0.8 s ahead of the leader,
    /// reach it in about a second and stand until its next turn: a stop-go line. Now the AI turn only (re)starts
    /// a follow of the leader and the movement service steers to the slot around the leader every
    /// <see cref="FollowCheckMilliseconds"/> (NpcMovementComponent.FollowTick), at the follower's full speed
    /// while the leader walks (AutonomousGroupPace still adds the catch-up bonus while behind).
    /// </summary>
    public static class AutonomousGroupFormationFollow
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousGroupFormationFollow));

        public const int FollowCheckMilliseconds = 400;
        /// <summary>
        /// The slot is placed this far ahead of a walking leader, well past what a follower walks in one follow check: at
        /// 1.0 s followers often reached the point before the next check and stood there (run 24: 9,162 of the standing
        /// checks were followers standing at the end of their last walk, 60 at their current aim).
        /// </summary>
        public const double LookaheadSeconds = 1.5;
        /// <summary>Without a refresh from the AI turn for this long the follow is no longer a formation follow.</summary>
        public const long StateLifetimeMilliseconds = 15_000;
        public const int MaximumFollowDistance = 3_000;
        public const int ArrivedTolerance = 24;
        /// <summary>A follower whose slot cannot be reached walks this close behind the leader instead, for
        /// <see cref="TrailLeaderMilliseconds"/>.</summary>
        public const int TrailLeaderDistance = 64;
        public const long TrailLeaderMilliseconds = 3_000;
        /// <summary>A follower walks straight after a leader that just crossed a zone border up to this far.</summary>
        public const int BorderWalkDistance = 600;
        public const long ReportIntervalMilliseconds = 5 * 60_000;

        private sealed class State
        {
            public GameBot Leader;
            public Vector3 Offset;
            public bool Tight;
            public long RefreshedTick;
            public long TrailLeaderUntil;
            public bool StoodLastCheck;
            // Bots as people 1.2, work package 5: the /stick line (BotStickLine) and a wall spot to wait on.
            public bool Stick;
            public long StickCheckTick;
            public GameBot Ahead;
            public float Gap;
            public bool StickRunning;
            public long NextLegTick;
            public long LostUntil;
            public Vector3 LostAim;
            public float Behind;
            public Vector3? HabitSpot;
        }

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        // Measurement for the owner's long run: follow checks while the leader walks, how many found the follower
        // standing still, and how far followers trail their slot.
        private static long _walkingChecks, _standingChecks, _trailTotal, _trailOver300, _nextReportTick;
        // Why a follower stood while its leader walked (owner 2026-10-17: group travel must be smooth; 10-17% standing).
        private static long _standCasting, _standCombat, _standNoPath, _standAtSlot, _standOther;
        // Partial: the slot path stops short. Twice: standing at two follow checks in a row (not just the reaction to
        // a leader that started walking). Trail: checks spent walking behind the leader because the slot was unreachable.
        private static long _standPartial, _standTwice, _trailLeader;
        // Follow checks whose slot lay over a zone border (aimed at the leader instead), and those that walked over it.
        private static long _borderFollow, _borderWalk;
        // The rest ('other'), split: sitting, held (stunned, mezzed, rooted or turning disabled), no speed, slot in another
        // zone than the follower; what remains stood with a found path.
        private static long _otherSitting, _otherHeld, _otherNoSpeed, _otherZone, _atSlotSlowLeader;
        // Standing at the slot point it aims at now / at the end of its last walk (reached it before the next check).
        private static long _standAtAim, _standAtOldAim;
        public const short SlowLeaderSpeed = 100;

        public static void NoteBorderFollow(bool walkedOver)
        {
            Interlocked.Increment(ref _borderFollow);
            if (walkedOver) Interlocked.Increment(ref _borderWalk);
        }

        /// <summary>Called from the follower's AI turn: keep following <paramref name="leader"/> at
        /// <paramref name="slot"/> (computed around <paramref name="center"/>).</summary>
        public static void Ensure(GameBot follower, GameBot leader, Vector3 center, Vector3 slot, bool tight)
        {
            if (follower == null || leader == null || follower == leader) return;
            State state = States.GetOrCreateValue(follower);
            state.Leader = leader;
            state.Offset = slot - center;
            state.Tight = tight;
            state.RefreshedTick = GameLoop.GameLoopTime;
            RefreshHabits(follower, leader, state, tight);
            if (follower.movementComponent is NpcMovementComponent mover && mover.FollowTarget != leader)
                follower.Follow(leader, 0, MaximumFollowDistance);
        }

        /// <summary>
        /// Bots as people 1.2, work package 5, once an AI turn: whether this follower runs on the /stick line, who it
        /// follows and its gap (BotStickLine), the 1-in-50 lost stick each 10 s of travel, and, while the leader stands,
        /// sitting while it waits or a held keep's wall spot (BotMovementHabitsRuntime.WaitTurn).
        /// </summary>
        private static void RefreshHabits(GameBot follower, GameBot leader, State state, bool tight)
        {
            long now = GameLoop.GameLoopTime;
            if (now >= state.StickCheckTick)
            {
                state.StickCheckTick = now + 5_000;
                state.Stick = !tight && leader.IsAutonomousWorldBot && !leader.IsOnStableMasterRoute &&
                    BotPeopleScope.Check(follower, BotPeopleRule.Movement, BotPeopleSwitches.Current.MoveStickLine, out _) ==
                    BotPeopleRefusal.None;
            }
            bool leaderWalking = leader.IsMoving && leader.CurrentSpeed > 0;
            // The line is worked out only while the leader walks (a standing group keeps its formation).
            if (BotStickLine.Refreshes(state.Stick, leaderWalking))
            {
                using var aiProfile = AiProfile.Scope(AiSystem.MovementHabits);
                state.Ahead = StickAhead(follower, leader, out int followers);
                state.Gap = BotStickLine.Gap(follower.DatabaseID > 0 ? follower.DatabaseID : follower.ObjectID, followers);
                if (state.Ahead != null)
                {
                    BotMovementHabitsRuntime.Count(BotMovementHabitsRuntime.Counter.StickMembers);
                    if (now >= state.NextLegTick)
                    {
                        state.NextLegTick = now + BotStickLine.LegMilliseconds;
                        if (now >= state.LostUntil && BotStickLine.LoseStick(Random.Shared.NextDouble()))
                        {
                            long lost = BotStickLine.LostMilliseconds(Random.Shared.NextDouble());
                            state.LostUntil = now + lost;
                            state.LostAim = new(state.Ahead.X, state.Ahead.Y, state.Ahead.Z);
                            BotMovementHabitsRuntime.Count(BotMovementHabitsRuntime.Counter.StickLost);
                            if (BotMovementHabitsRuntime.ShouldLog(BotMovementHabitsRuntime.EventTag.StickLost))
                                BotMovementHabitsRuntime.Info($"BOT_MOVE_STICK_LOST bot=\"{follower.Name}\" id={follower.DatabaseID} " +
                                    $"leader=\"{leader.Name}\" ahead=\"{state.Ahead.Name}\" gap={(int)state.Gap} lost_ms={lost} " +
                                    $"region={follower.CurrentRegionID}");
                        }
                    }
                    // The pace rule's catch-up measured from the line spot, not the formation slot (re-set every check).
                    AutonomousGroupPace.CatchUp(follower, state.Behind, now);
                }
            }
            else if (!state.Stick)
                state.Ahead = null;
            if (!leaderWalking && !leader.InCombat && !follower.InCombat)
            {
                // Standing at its slot it waits; walking to its wall spot it keeps it.
                if (!follower.IsMoving || state.HabitSpot != null)
                    state.HabitSpot = BotMovementHabitsRuntime.WaitTurn(follower, BotMovementHabitsRuntime.WaitSpot.Formation, leader);
            }
            else if (state.HabitSpot != null)
            {
                state.HabitSpot = null;
                BotMovementHabitsRuntime.EndWall(follower);
            }
        }

        /// <summary>The one this follower sticks to: the nearest member above it in the group window that is on the line
        /// with the same leader, or the leader (BotStickLine.Order); null when it is not found. Also the line's followers.</summary>
        private static GameBot StickAhead(GameBot follower, GameBot leader, out int followers)
        {
            followers = 0;
            if (follower.Group == null)
                return null;
            var members = follower.Group.GetMembersInTheGroup();
            int count = Math.Min(members.Count, 16);
            int leaderIndex = -1, mine = -1, previous = -1;
            for (int i = 0; i < count; i++)
                if (members[i] == leader)
                    leaderIndex = i;
            // No allocation: the group window is at most 16 (8 in the game).
            Span<bool> inLine = stackalloc bool[16];
            Span<int> order = stackalloc int[16];
            inLine = inLine[..count];
            order = order[..count];
            for (int i = 0; i < count; i++)
                inLine[i] = members[i] is GameBot bot && bot != leader && bot.IsAlive && bot.CurrentRegion == follower.CurrentRegion &&
                    (bot == follower || States.TryGetValue(bot, out State other) && other.Stick && other.Leader == leader);
            BotStickLine.Order(inLine, leaderIndex, order);
            followers = BotStickLine.Followers((ReadOnlySpan<int>)order);
            for (int i = 0; i < count; i++)
                if (members[i] == follower)
                    mine = i;
            if (mine >= 0)
                previous = order[mine];
            return previous >= 0 ? members[previous] as GameBot : null;
        }

        /// <summary>The follower takes another order (a route, a fight): its formation follow ends.</summary>
        public static void Release(GameBot follower)
        {
            if (follower == null || !States.TryGetValue(follower, out State state)) return;
            if (follower.movementComponent is NpcMovementComponent mover && mover.FollowTarget == state.Leader)
                follower.StopFollowing();
            States.Remove(follower);
        }

        private static bool TryGetLive(GameNPC owner, GameLiving target, out GameBot bot, out State state)
        {
            bot = owner as GameBot;
            state = null;
            return bot != null && States.TryGetValue(bot, out state) && state.Leader == target &&
                GameLoop.GameLoopTime - state.RefreshedTick <= StateLifetimeMilliseconds &&
                bot.Group != null && bot.Group == state.Leader.Group;
        }

        /// <summary>This follow is a live formation follow (a cast pauses it instead of ending it).</summary>
        public static bool IsFormationFollow(GameNPC owner, GameLiving target) => TryGetLive(owner, target, out _, out _);

        /// <summary>
        /// While the party walks, its members hold ordinary buffs until it stops, as companions do while their player
        /// walks (CompanionFollowPolicy.DeferBuff). A cast stops the caster, and in the owner's first session on the
        /// formation follow (2026-10-17) followers stood in 12% of the checks while their leader walked.
        /// </summary>
        public static bool DeferBuff(GameBot bot, Spell spell)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || bot.Group == null || bot.InCombat || spell == null ||
                bot.Group.LivingLeader is not GameBot leader || !leader.IsAlive || !leader.IsMoving || leader.InCombat)
                return false;
            if (leader != bot && !TryGetLive(bot, leader, out _, out _))
                return false;
            return CompanionFollowPolicy.IsOrdinaryBuff(bot, spell);
        }

        /// <summary>For NpcMovementComponent.FollowTick: the follower's slot when this follow is a live formation follow.</summary>
        public static bool TryGetSlot(GameNPC owner, GameLiving target, out Vector3 slot, out bool leaderWalking)
        {
            slot = default;
            leaderWalking = false;
            if (!TryGetLive(owner, target, out GameBot bot, out State state))
                return false;
            GameBot leader = state.Leader;
            leaderWalking = leader.IsMoving && leader.CurrentSpeed > 0;
            Vector3 center = new(leader.X, leader.Y, leader.Z);
            if (leaderWalking && !state.Tight)
            {
                Point2D ahead = leader.GetPointFromHeading(leader.Heading, (int)(leader.CurrentSpeed * LookaheadSeconds));
                center = new Vector3(ahead.X, ahead.Y, leader.Z);
            }
            slot = center + state.Offset;
            // Bots as people 1.2 (WP5): on the /stick line the member follows the one above it instead of its slot.
            if (leaderWalking && state.Stick && StickSlot(bot, state, ref slot))
                return true;
            // A waiting member walks to its wall spot and sits there while the leader stands.
            if (!leaderWalking && state.HabitSpot is Vector3 wall)
                slot = wall;
            if (leaderWalking)
                Measure(bot, state, new Vector3(leader.X, leader.Y, leader.Z) + state.Offset, leader.CurrentSpeed, slot);
            else
                state.StoodLastCheck = false;
            return true;
        }

        /// <summary>The follow aim on the /stick line (BotStickLine.AimFor), or false when the one ahead is gone (the slot then).</summary>
        private static bool StickSlot(GameBot bot, State state, ref Vector3 slot)
        {
            GameBot ahead = state.Ahead;
            if (ahead == null || !ahead.IsAlive || ahead.CurrentRegion != bot.CurrentRegion || ahead.ObjectState != GameObject.eObjectState.Active)
                return false;
            long now = GameLoop.GameLoopTime;
            if (now < state.LostUntil)
            {
                // Lost stick: it runs on to where the one ahead was and stands there until it sticks again.
                slot = state.LostAim;
                return true;
            }
            Vector2 forward = Vector2.Zero;
            if (ahead.IsMoving && ahead.CurrentSpeed > 0)
                forward = BotCombatMovement.PointFromHeading(Vector2.Zero, ahead.Heading, 1f);
            BotStickLine.Aim aim = BotStickLine.AimFor(new(bot.X, bot.Y), new(ahead.X, ahead.Y), forward, ahead.CurrentSpeed,
                state.Gap, state.StickRunning);
            state.StickRunning = aim.Run;
            state.Behind = aim.Behind;
            slot = new Vector3(aim.Point, aim.Run ? ahead.Z : bot.Z);
            return true;
        }

        /// <summary>
        /// For NpcMovementComponent.FollowTick while the leader walks: true when the follower should walk behind the
        /// leader instead of to its slot. A slot on ground the follower cannot reach (behind a wall, over a ledge, the
        /// far side of a narrow bridge) left it standing with a partial path until the slot moved somewhere reachable;
        /// the leader's own track is walkable. <paramref name="slotUnreachable"/> starts a
        /// <see cref="TrailLeaderMilliseconds"/> spell of it.
        /// </summary>
        public static bool TrailLeader(GameNPC owner, bool slotUnreachable)
        {
            if (owner is not GameBot bot || !States.TryGetValue(bot, out State state)) return false;
            long now = GameLoop.GameLoopTime;
            if (slotUnreachable) state.TrailLeaderUntil = now + TrailLeaderMilliseconds;
            if (now >= state.TrailLeaderUntil) return false;
            Interlocked.Increment(ref _trailLeader);
            return true;
        }

        private static void Measure(GameBot bot, State state, Vector3 slot, short leaderSpeed, Vector3 aim)
        {
            Interlocked.Increment(ref _walkingChecks);
            // Trail is measured to the slot beside the leader (not the one it aims at, ahead of the leader).
            long trail = (long)Vector3.Distance(new(bot.X, bot.Y, bot.Z), slot);
            bool stood = !bot.IsMoving;
            if (stood)
            {
                Interlocked.Increment(ref _standingChecks);
                // Standing on the point it walked to (it got there before this check) or short of it (it never moved).
                if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(aim.X, aim.Y)) <= 48 * 48) Interlocked.Increment(ref _standAtAim);
                else if (bot.movementComponent is NpcMovementComponent mover &&
                         Vector2.DistanceSquared(new(bot.X, bot.Y), new(mover.Destination.X, mover.Destination.Y)) <= 48 * 48)
                    Interlocked.Increment(ref _standAtOldAim);
                if (state.StoodLastCheck) Interlocked.Increment(ref _standTwice);
                PathfindingStatus status = bot.movementComponent?.LastPathfindingStatus ?? PathfindingStatus.NotSet;
                if (bot.IsCasting) Interlocked.Increment(ref _standCasting);
                else if (bot.InCombat || bot.IsAttacking) Interlocked.Increment(ref _standCombat);
                else if (status == PathfindingStatus.NoPathFound) Interlocked.Increment(ref _standNoPath);
                else if (trail <= 120)
                {
                    Interlocked.Increment(ref _standAtSlot);
                    // At its place beside a leader that only creeps (pacing for stragglers, a slow walk): nothing to catch up.
                    if (leaderSpeed < SlowLeaderSpeed) Interlocked.Increment(ref _atSlotSlowLeader);
                }
                else if (status == PathfindingStatus.PartialPathFound) Interlocked.Increment(ref _standPartial);
                else
                {
                    Interlocked.Increment(ref _standOther);
                    if (bot.IsSitting) Interlocked.Increment(ref _otherSitting);
                    else if (bot.IsStunned || bot.IsMezzed || bot.IsTurningDisabled) Interlocked.Increment(ref _otherHeld);
                    else if (bot.MaxSpeed <= 0) Interlocked.Increment(ref _otherNoSpeed);
                    else if (bot.CurrentRegion?.GetZone((int)slot.X, (int)slot.Y) != bot.CurrentZone) Interlocked.Increment(ref _otherZone);
                }
            }
            state.StoodLastCheck = stood;
            Interlocked.Add(ref _trailTotal, trail);
            if (trail > 300) Interlocked.Increment(ref _trailOver300);
            long now = GameLoop.GameLoopTime;
            long next = Interlocked.Read(ref _nextReportTick);
            if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, next) != next)
                return;
            long checks = Interlocked.Exchange(ref _walkingChecks, 0);
            long standing = Interlocked.Exchange(ref _standingChecks, 0);
            long total = Interlocked.Exchange(ref _trailTotal, 0);
            long over = Interlocked.Exchange(ref _trailOver300, 0);
            long casting = Interlocked.Exchange(ref _standCasting, 0), combat = Interlocked.Exchange(ref _standCombat, 0),
                 noPath = Interlocked.Exchange(ref _standNoPath, 0), atSlot = Interlocked.Exchange(ref _standAtSlot, 0),
                 other = Interlocked.Exchange(ref _standOther, 0), partial = Interlocked.Exchange(ref _standPartial, 0),
                 twice = Interlocked.Exchange(ref _standTwice, 0), trailLeader = Interlocked.Exchange(ref _trailLeader, 0),
                 border = Interlocked.Exchange(ref _borderFollow, 0), borderWalk = Interlocked.Exchange(ref _borderWalk, 0),
                 sitting = Interlocked.Exchange(ref _otherSitting, 0), held = Interlocked.Exchange(ref _otherHeld, 0),
                 noSpeed = Interlocked.Exchange(ref _otherNoSpeed, 0), otherZone = Interlocked.Exchange(ref _otherZone, 0),
                 slowLeader = Interlocked.Exchange(ref _atSlotSlowLeader, 0),
                 atAim = Interlocked.Exchange(ref _standAtAim, 0), atOldAim = Interlocked.Exchange(ref _standAtOldAim, 0);
            if (next == 0 || checks == 0) return;
            Log.Info($"AUTONOMOUS_GROUP_FORMATION walkingChecks={checks} standingWhileLeaderWalks={standing} " +
                     $"standingPct={100.0 * standing / checks:0.0} averageTrail={total / checks} trailOver300Pct={100.0 * over / checks:0.0} " +
                     $"standing: casting={casting} combat={combat} noPath={noPath} atSlot={atSlot} partial={partial} other={other} " +
                     $"standingTwice={twice} trailLeaderChecks={trailLeader} borderChecks={border} borderWalks={borderWalk} " +
                     $"other: sitting={sitting} held={held} noSpeed={noSpeed} otherZone={otherZone} atSlotSlowLeader={slowLeader} " +
                     $"standingAtAim={atAim} standingAtLastWalkEnd={atOldAim}");
        }
    }
}
