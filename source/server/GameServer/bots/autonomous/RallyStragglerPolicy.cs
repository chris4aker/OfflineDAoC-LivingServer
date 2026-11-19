namespace DOL.GS;

/// <summary>
/// The RVR_RALLY_DELAY rule (AutonomousRvrEventLayer's straggler sweep): an assigned siege or defense member with no approach
/// progress for <see cref="AutonomousRvrEventLayer.StragglerTimeoutMilliseconds"/> is logged. A member that reported itself
/// at its muster post within <see cref="AutonomousRvrEventLayer.AttendanceFreshnessMilliseconds"/> holds its post and is not a
/// straggler: the muster path stops the bot there and never reports travel, so every party holding its post was flagged
/// 8 minutes after its last step (owner's PC, 2026-11-19, build 1.2-43c43aa: 39 of 50 lines).
/// </summary>
public static class RallyStragglerPolicy
{
    public static bool HoldsMusterPost(long nowTick, long? heldPostTick) =>
        heldPostTick is long tick && tick <= nowTick && nowTick - tick <= AutonomousRvrEventLayer.AttendanceFreshnessMilliseconds;

    public static bool IsStalled(long nowTick, long progressTick) =>
        nowTick - progressTick >= AutonomousRvrEventLayer.StragglerTimeoutMilliseconds;

    /// <summary>The sweep's decision for one member: stalled and not holding its post.</summary>
    public static bool Flags(long nowTick, long progressTick, long? heldPostTick) =>
        !HoldsMusterPost(nowTick, heldPostTick) && IsStalled(nowTick, progressTick);
}
