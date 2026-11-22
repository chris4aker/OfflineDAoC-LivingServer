using System.Numerics;

namespace DOL.GS;

public static class AutonomousRouteRecoveryPolicy
{
    // A movement callback may describe the route that was just replaced.
    // Retain intermediate/seam failures while the intent is unchanged, but
    // never charge an obsolete destination against a freshly selected route.
    public static bool AppliesToCurrentDestination(bool destinationChanged, Vector3 failed, Vector3 current) =>
        !destinationChanged || Vector3.DistanceSquared(failed, current) <= 96 * 96;

    public const int MaximumLocalAttempts = 3;
    public const float ForwardProgressRequired = 240f;
    public const long RepeatedFailureWindowMilliseconds = 10 * 60_000L;
    // A keep courtyard is up to ~1,000 units across: bots trapped inside Nottmoor
    // Faste bounced between two spots ~460 apart and never counted as repeating.
    public const float RepeatedFailureRadius = 1024f;
    public const int FailuresBeforeSafeRelocation = 3;
    public const long ImmediateRouteFailureCooldownMilliseconds = 30_000;

    public static bool HasMeaningfulForwardProgress(float baselineDistance, float currentDistance) =>
        baselineDistance >= 0 && baselineDistance - currentDistance >= ForwardProgressRequired;

    public static bool ShouldAbandon(int attemptedRecoveries) =>
        attemptedRecoveries > MaximumLocalAttempts;

    public static bool ShouldRetainMovementOrder(bool isMoving, long nowTick, long retryAfterTick) =>
        isMoving || nowTick < retryAfterTick;

    // A collision-stopped actor reports IsMoving=false. Route ownership is
    // established by the controller before this policy is called, so elapsed
    // position time—not the movement flag—is the reliable stall signal.
    public static bool ShouldRecoverActiveRouteStall(long lastProgressTick, long nowTick, long threshold) =>
        lastProgressTick > 0 && nowTick - lastProgressTick >= threshold;

    public static long ReplanDelayMilliseconds(bool emptyLiveCamp, long actorKey) =>
        (emptyLiveCamp ? 2_500 : ImmediateRouteFailureCooldownMilliseconds) +
        System.Math.Abs(actorKey % 1_500);

    /// <summary>Forward progress only clears the pocket count once the bot is out of the pocket.</summary>
    public static bool HasLeftFailurePocket(ushort pocketRegion, ushort currentRegion, Vector3 pocket, Vector3 current) =>
        pocketRegion == 0 || pocketRegion != currentRegion ||
        Vector2.DistanceSquared(new(pocket.X, pocket.Y), new(current.X, current.Y)) >
        RepeatedFailureRadius * RepeatedFailureRadius;

    public static bool IsSameRepeatedFailurePocket(ushort previousRegion, ushort currentRegion,
        Vector3 previous, Vector3 current, long elapsedMilliseconds) =>
        previousRegion == currentRegion && elapsedMilliseconds >= 0 &&
        elapsedMilliseconds <= RepeatedFailureWindowMilliseconds &&
        Vector2.DistanceSquared(new(previous.X, previous.Y), new(current.X, current.Y)) <=
        RepeatedFailureRadius * RepeatedFailureRadius;

    // Detour has a bounded search budget. A useful partial corridor is not a
    // collision: continue once from its reached end, never retry a zero-progress island.
    public static bool CanContinuePartial(PathfindingStatus status, Vector3 origin, Vector3 current) =>
        status == PathfindingStatus.PartialPathFound &&
        Vector3.DistanceSquared(origin, current) >= ForwardProgressRequired * ForwardProgressRequired;

    // 1.2.1 (#420, #421): a terminal-pocket escape that puts a bot back on ground from which the same goal fails again
    // (the Connacht hill below 200:314846,471535 lies inside the 4,000-unit pocket whose escape is the road 3,300 units
    // off; the Lough Derg / Bri Leith seam cycle escapes and returns) was repeated with no end: the failure count was
    // anchored where each failure happened and reset by the escape. The rescue itself is counted here, by goal and
    // escape point: after two identical rescues within half an hour the third is not made, and the goal is given up instead.
    public const int PocketEscapesBeforeLoop = 3;
    public const long PocketEscapeLoopWindowMilliseconds = 30 * 60_000L;
    public const float PocketEscapeSameSpotRadius = 256f;

    /// <summary>The goal a pocket escape serves, as the loop detector compares it: the camp, else the goal point on a
    /// 512-unit grid, else "none". Pure.</summary>
    public static string PocketEscapeGoal(string campId, Vector3? goal) =>
        !string.IsNullOrEmpty(campId) ? "camp:" + campId
        : goal is { } g ? $"goal:{(int)System.MathF.Floor(g.X / 512f)},{(int)System.MathF.Floor(g.Y / 512f)}"
        : "none";

    /// <summary>One bot's run of identical pocket escapes (same goal, same escape point, within the window).</summary>
    public sealed class PocketEscapeRun
    {
        public string Goal { get; private set; } = string.Empty;
        public Vector3 Escape { get; private set; }
        public long FirstTick { get; private set; }
        public int Count { get; private set; }

        /// <summary>Counts this escape: the run goes on when the goal and the escape point are the ones before and the
        /// first of the run is within the window, else a new run starts. Returns the run's length with this one.</summary>
        public int Note(string goal, Vector3 escape, long now)
        {
            bool same = Count > 0 && string.Equals(goal, Goal, System.StringComparison.Ordinal) &&
                        now >= FirstTick && now - FirstTick <= PocketEscapeLoopWindowMilliseconds &&
                        Vector2.DistanceSquared(new(escape.X, escape.Y), new(Escape.X, Escape.Y)) <=
                        PocketEscapeSameSpotRadius * PocketEscapeSameSpotRadius;
            if (!same)
            {
                Goal = goal ?? string.Empty;
                Escape = escape;
                FirstTick = now;
                Count = 0;
            }
            return ++Count;
        }

        /// <summary>Whether this escape would be the loop's (the run with it reaches PocketEscapesBeforeLoop), without
        /// counting it. Pure.</summary>
        public bool WouldLoop(string goal, Vector3 escape, long now) =>
            Count + 1 >= PocketEscapesBeforeLoop && Count > 0 && string.Equals(goal, Goal, System.StringComparison.Ordinal) &&
            now >= FirstTick && now - FirstTick <= PocketEscapeLoopWindowMilliseconds &&
            Vector2.DistanceSquared(new(escape.X, escape.Y), new(Escape.X, Escape.Y)) <=
            PocketEscapeSameSpotRadius * PocketEscapeSameSpotRadius;

        public void Clear()
        {
            Goal = string.Empty;
            Escape = default;
            FirstTick = 0;
            Count = 0;
        }
    }
}
