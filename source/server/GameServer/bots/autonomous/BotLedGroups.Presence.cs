namespace DOL.GS;

/// <summary>Bot-led groups: when the hosted player counts as gone from the party (pure).</summary>
public static partial class BotLedGroups
{
    /// <summary>A hosted player out of the world this long (not linkdead, still in the group) has left: a zone or region
    /// change, or a GM /jump, takes a few seconds of client loading; a crash without a linkdead state does not come back.</summary>
    public const long AwayGraceMs = 2 * 60_000L;

    /// <summary>
    /// The hosted player has left the party: out of the group, or out of the world (and not linkdead, which has its own
    /// five-minute rule) for longer than <see cref="AwayGraceMs"/>. PC test 2026-11-07: a player let go 80 s after
    /// accepting the invite; between, only a GM /jump to the leader and a /follow (the jump took them out of the world).
    /// </summary>
    public static bool HostedPlayerLeft(bool inGroup, bool outOfWorld, long awaySince, long now) =>
        !inGroup || outOfWorld && awaySince > 0 && now - awaySince > AwayGraceMs;
}
