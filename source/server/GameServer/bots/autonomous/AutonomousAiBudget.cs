using System;

namespace DOL.GS;

public enum eAutonomousThinkMode
{
    Combat,
    PlayerLed,
    NearbyInteraction,
    Travel,
    Resting,
    Planning,
}

/// <summary>
/// Lightweight scheduling policy for live bot actors. Slower thinking never
/// grants progress or teleports work: movement, attacks and interactions still
/// occur through the normal world. Stable offsets prevent synchronized spikes.
/// </summary>
public static class AutonomousAiBudget
{
    public static int IntervalMilliseconds(eAutonomousThinkMode mode) => mode switch
    {
        eAutonomousThinkMode.Combat => 250,
        eAutonomousThinkMode.PlayerLed => 300,
        eAutonomousThinkMode.NearbyInteraction => 600,
        eAutonomousThinkMode.Travel => 900,
        eAutonomousThinkMode.Resting => 1800,
        _ => 2500,
    };

    public static int StableOffsetMilliseconds(long botId, eAutonomousThinkMode mode)
    {
        int interval = IntervalMilliseconds(mode);
        ulong mixed = unchecked((ulong)botId) * 11400714819323198485UL;
        return (int)(mixed % (uint)interval);
    }

    /// <summary>
    /// How many deferred planning decisions may run this tick. Not called by the server yet: the deferral queue that
    /// would call it (audit 2026-11-03 §8.4 step 2) is decided after the L300/L600 baselines. When it lands, it is
    /// called from the serial planning as
    /// <c>MaximumDecisionsThisTick(AiProfile.ElapsedThisTickMs(), AutonomousPopulationProperties.AI_BUDGET_MS, queue.Count)</c>;
    /// combat, movement and retaliation are never deferred.
    /// </summary>
    public static int MaximumDecisionsThisTick(double recentTickMilliseconds, double budgetMilliseconds, int queueCount)
    {
        if (queueCount <= 0 || budgetMilliseconds <= 0 || recentTickMilliseconds >= budgetMilliseconds)
            return 0;

        double headroom = budgetMilliseconds - recentTickMilliseconds;
        int allowance = Math.Max(1, (int)Math.Floor(headroom / 0.12));
        return Math.Min(queueCount, allowance);
    }
}
