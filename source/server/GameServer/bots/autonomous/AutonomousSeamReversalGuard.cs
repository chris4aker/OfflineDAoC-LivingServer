using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// #570 step 1. Port of True165 d1320e6 (last 2f0f143) AutonomousSeamReversalGuard, on our own reversal test
    /// (<see cref="AutonomousZoneBoundaryRouting.IsReversal"/>, the broader one). A bot whose zone steps take the same
    /// crossing back and forth for one goal (A>B, B>A, A>B) stands on ground that leads on in neither zone; each short hop
    /// counted as route progress. Our controller caught it at the third reversal; this stops it at the second, leaves a goal
    /// on the border alone, and lets a group member give up only its own step. Pure rules; the controller applies them.
    /// </summary>
    public static class AutonomousSeamReversalGuard
    {
        /// <summary>A>B, B>A, A>B: two reversals in a row (the issue's "stops after the second turn").</summary>
        public const int TriggerReversals = 2;
        /// <summary>A step older than this starts a new count (ours since 2026-10-28; True165 uses one minute).</summary>
        public const long WindowMilliseconds = 5 * 60_000;
        /// <summary>A goal moved more than this is a new trip (ours since 2026-10-28).</summary>
        public const float GoalTolerance = 500f;
        /// <summary>A goal this close to either side of the crossing may be crossed back and forth (it sits on the border).</summary>
        public const float ExemptRadius = 512f;
        /// <summary>
        /// How long a group member waits after its step failed before it plans again (a moved goal plans at once). Under
        /// the controller's route-stall time, so a waiting member never turns into stall recovery and its AbandonCamp.
        /// </summary>
        public const long MemberHoldMilliseconds = 10_000;
        /// <summary>
        /// #570 review: how many holds a member gets inside <see cref="WindowMilliseconds"/> before it gives up like a solo
        /// bot. Each hold ends with the member following its leader again; a crossing that still turns it back after that
        /// is a route that doesn't lead on, and a bounded give-up beats pacing at the border.
        /// </summary>
        public const int MaxMemberHolds = 2;

        public struct State
        {
            public AutonomousZoneBoundaryRouting.Step? Last;
            public Vector3 Goal;
            public long Tick;
            public int Reversals;
            /// <summary>Member holds taken since <see cref="FirstHoldTick"/> (kept by <see cref="ClearReversals"/>).</summary>
            public int MemberHolds;
            public long FirstHoldTick;
        }

        /// <summary>After a caught reversal: the count starts again from the next step; the member holds are kept.</summary>
        public static void ClearReversals(ref State state)
        {
            state.Last = null;
            state.Reversals = 0;
        }

        /// <summary>
        /// Takes one member hold: true while fewer than <see cref="MaxMemberHolds"/> were taken inside the window (an older
        /// first hold starts a fresh window), false when the member should give up instead.
        /// </summary>
        public static bool TakeMemberHold(ref State state, long now)
        {
            if (state.MemberHolds == 0 || now - state.FirstHoldTick >= WindowMilliseconds)
            {
                state.MemberHolds = 0;
                state.FirstHoldTick = now;
            }
            if (state.MemberHolds >= MaxMemberHolds) return false;
            state.MemberHolds++;
            return true;
        }

        /// <summary>
        /// Notes a newly planned step and returns the reversals in a row: back to 0 when the goal moved, the last step is
        /// older than the window, or this step doesn't take the last one back.
        /// </summary>
        public static int Note(ref State state, AutonomousZoneBoundaryRouting.Step step, Vector3 goal, long now)
        {
            bool reversed = state.Last is { } last &&
                            now - state.Tick < WindowMilliseconds &&
                            Vector3.DistanceSquared(state.Goal, goal) < GoalTolerance * GoalTolerance &&
                            AutonomousZoneBoundaryRouting.IsReversal(last, step);
            state.Reversals = reversed ? state.Reversals + 1 : 0;
            state.Last = step;
            state.Goal = goal;
            state.Tick = now;
            return state.Reversals;
        }

        /// <summary>True when the goal lies within <see cref="ExemptRadius"/> (flat) of either side of the step.</summary>
        public static bool Exempt(AutonomousZoneBoundaryRouting.Step step, Vector3 goal)
        {
            const float limit = ExemptRadius * ExemptRadius;
            Vector2 flat = new(goal.X, goal.Y);
            return Vector2.DistanceSquared(new(step.Inside.X, step.Inside.Y), flat) <= limit ||
                   Vector2.DistanceSquared(new(step.Outside.X, step.Outside.Y), flat) <= limit;
        }

        /// <summary>Whether this newly planned step ends the trip (the count is kept either way).</summary>
        public static bool ShouldFail(ref State state, AutonomousZoneBoundaryRouting.Step step, Vector3 goal, long now) =>
            Note(ref state, step, goal, now) >= TriggerReversals && !Exempt(step, goal);

        /// <summary>
        /// A member of a dynamic group who isn't its leader fails only its own step and waits (the group keeps its camp: a
        /// member's AbandonCamp rejects the group's shared camp). Leaders, solo bots and realm-raid members (whose own
        /// AbandonCamp already retries) take the full no-seam way.
        /// </summary>
        public static bool FailsMemberOnly(bool dynamicGroup, bool isLeader, bool inRealmRaid) =>
            dynamicGroup && !isLeader && !inRealmRaid;
    }
}
