using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Bots as people for 1.2, item 5 (work package 5): a bot-only group travelling in the open world runs on /stick. "If he
/// says stick, STICK"; some stuck "the person above you in the mini group window", so the group ran strung out
/// (ERA-RESEARCH section 6, FH Jan and Aug 2003); "/Follow is usually a good way to stay with your buddies during regular
/// travel" (Prima). Each member follows the one above it in the group window at its own gap of 80-200 units (the whole
/// line at most <see cref="LineCap"/>, inside the pace rule's 1,500), so the party runs as a line instead of a block.
/// A /stick is a distance: inside it the member stands, past it the member runs after the one ahead. Every
/// <see cref="LegMilliseconds"/> of travel a member loses stick 1 time in 50 for 3-10 s (it runs on to where the one
/// ahead was, stops, then catches up). The same for all tiers. When the leader stops the group gathers in its formation
/// as before. The line replaces the formation slot only: it adds no path query. Pure.
/// </summary>
public static class BotStickLine
{
    public const float MinimumGap = 80f;
    public const float MaximumGap = 200f;
    /// <summary>The whole line from the leader to the last member, at most (AutonomousGroupPace stops the leader at 1,500).</summary>
    public const float LineCap = 900f;
    /// <summary>A running member stops only once closer than this share of its gap (no stop-go at the gap itself).</summary>
    public const float StopFraction = 0.6f;
    /// <summary>A running member aims this far ahead of the one it follows, so it never arrives and stands between checks.</summary>
    public const double LookaheadSeconds = 1.5;
    /// <summary>One "leg" of travel: each member rolls for a lost stick once per this much walking.</summary>
    public const long LegMilliseconds = 10_000;
    public const int LoseOneIn = 50;
    public const long LostMinimumMs = 3_000;
    public const long LostMaximumMs = 10_000;
    private const ulong GapSalt = 0x53544B47; // "STKG"

    /// <summary>
    /// Who each member follows: <paramref name="inLine"/> is the group window in order (true for a member on the line);
    /// a member follows the nearest one above it that is on the line, or the leader. The leader heads the line wherever
    /// it stands in the window. -1 for the leader and for members off the line. Pure.
    /// </summary>
    public static int[] Order(IReadOnlyList<bool> inLine, int leaderIndex)
    {
        Span<bool> line = inLine.Count <= 64 ? stackalloc bool[inLine.Count] : new bool[inLine.Count];
        for (int i = 0; i < inLine.Count; i++)
            line[i] = inLine[i];
        var order = new int[inLine.Count];
        Order(line, leaderIndex, order);
        return order;
    }

    /// <summary><see cref="Order(IReadOnlyList{bool}, int)"/> into <paramref name="order"/> (as long as
    /// <paramref name="inLine"/>), with no allocation: the follow refresh's form. Pure.</summary>
    public static void Order(ReadOnlySpan<bool> inLine, int leaderIndex, Span<int> order)
    {
        int previous = leaderIndex >= 0 && leaderIndex < inLine.Length ? leaderIndex : -1;
        for (int i = 0; i < inLine.Length; i++)
        {
            order[i] = -1;
            if (i == leaderIndex || !inLine[i] || previous < 0)
                continue;
            order[i] = previous;
            previous = i;
        }
    }

    /// <summary>The line is worked out (who is ahead, the gaps, the lost stick) only while the leader walks: a standing
    /// group is in its formation, as before. Pure.</summary>
    public static bool Refreshes(bool onLine, bool leaderWalking) => onLine && leaderWalking;

    /// <summary>The followers on the line (members with a predecessor) in an <see cref="Order"/> result.</summary>
    public static int Followers(IReadOnlyList<int> order)
    {
        int count = 0;
        foreach (int predecessor in order)
            if (predecessor >= 0)
                count++;
        return count;
    }

    /// <summary><see cref="Followers(IReadOnlyList{int})"/> of a span. Pure.</summary>
    public static int Followers(ReadOnlySpan<int> order)
    {
        int count = 0;
        foreach (int predecessor in order)
            if (predecessor >= 0)
                count++;
        return count;
    }

    /// <summary>
    /// A member's own stick distance: 80-200 by its id, cut so <paramref name="followers"/> gaps fit in
    /// <see cref="LineCap"/> (never under <see cref="MinimumGap"/>). Pure.
    /// </summary>
    public static float Gap(long memberId, int followers)
    {
        double roll = AutonomousBotPersonality.Mix(memberId, GapSalt) % 10_000 / 10_000.0;
        float gap = MinimumGap + (float)roll * (MaximumGap - MinimumGap);
        float cap = followers > 0 ? MathF.Max(MinimumGap, LineCap / followers) : MaximumGap;
        return MathF.Min(gap, cap);
    }

    /// <summary>A lost stick this leg; <paramref name="roll"/> in [0, 1). Pure.</summary>
    public static bool LoseStick(double roll) => roll < 1.0 / LoseOneIn;

    public static long LostMilliseconds(double roll) =>
        LostMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (LostMaximumMs - LostMinimumMs));

    /// <summary>Where a member on the line aims this follow check, and whether it runs.</summary>
    public readonly record struct Aim(Vector2 Point, bool Run, float Behind);

    /// <summary>
    /// The aim of a member at <paramref name="self"/> following one at <paramref name="ahead"/> that walks along
    /// <paramref name="forward"/> (a unit vector, or zero) at <paramref name="speed"/>. Standing (not
    /// <paramref name="running"/>), it starts once the one ahead is past its <paramref name="gap"/>; running, it stops only
    /// inside <see cref="StopFraction"/> of it. Running, it aims <see cref="LookaheadSeconds"/> ahead of the one it
    /// follows; standing, at itself. <see cref="Aim.Behind"/> is how far past its gap it trails (for the catch-up). Pure.
    /// </summary>
    public static Aim AimFor(Vector2 self, Vector2 ahead, Vector2 forward, float speed, float gap, bool running)
    {
        float distance = Vector2.Distance(self, ahead);
        bool run = running ? distance >= gap * StopFraction : distance > gap;
        float behind = MathF.Max(0, distance - gap);
        if (!run)
            return new(self, false, behind);
        Vector2 lead = forward.LengthSquared() > 0.25f ? forward * (float)(speed * LookaheadSeconds) : Vector2.Zero;
        return new(ahead + lead, true, behind);
    }
}
