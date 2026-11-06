using System;

namespace DOL.GS;

/// <summary>
/// Computes a gradual startup target for real bot actors.  This controls only
/// how many persisted characters are loaded; it never creates progress and it
/// never deletes or resets a character. The complete non-retired roster is the
/// target -- there is deliberately no machine-performance cap.
/// </summary>
public static class AutonomousPopulationRamp
{
    public const double EarlyPopulationFraction = 0.33d;
    public const int EarlyRampMinutes = 5;

    public static int DesiredActiveCount(bool enabled, int rosterCount, int rampMinutes, TimeSpan sinceStartup)
    {
        if (!enabled || rosterCount <= 0)
            return 0;

        int target = rosterCount;
        if (rampMinutes <= 0 || sinceStartup.TotalMinutes >= rampMinutes)
            return target;

        double elapsed = Math.Clamp(sinceStartup.TotalMinutes, 0, rampMinutes);
        double earlyMinutes = Math.Min(EarlyRampMinutes, rampMinutes);
        int earlyTarget = Math.Max(1, (int)Math.Ceiling(target * EarlyPopulationFraction));
        int desired;
        if (elapsed <= earlyMinutes)
        {
            // Stagger the first 33%, rather than scheduling a startup burst.
            desired = (int)Math.Floor(earlyTarget * elapsed / earlyMinutes + 1e-9);
        }
        else
        {
            double remainingMinutes = Math.Max(1d, rampMinutes - earlyMinutes);
            desired = earlyTarget + (int)Math.Floor((target - earlyTarget) * (elapsed - earlyMinutes) / remainingMinutes + 1e-9);
        }
        return Math.Min(target, desired);
    }

    /// <summary>Minutes after the ramp's end that it counts as finished even if some bots never logged in.</summary>
    public const int FinishGraceMinutes = 10;

    /// <summary>
    /// The startup ramp has finished: the population is off, or the ramp's minutes have passed and the whole roster is
    /// online (<paramref name="populationSatisfied"/>), or the ramp ended <see cref="FinishGraceMinutes"/> ago. Until
    /// then no automatic realm raid opens (2026-11-04/05 on the owner's test copy: Tuscaran Glacier opened two minutes
    /// after a restart and was sized from the few level 50s already online, 58 instead of 100).
    /// </summary>
    public static bool Finished(bool enabled, int rampMinutes, TimeSpan sinceStartup, bool populationSatisfied)
    {
        double ramp = Math.Max(0, rampMinutes), minutes = sinceStartup.TotalMinutes;
        return !enabled || minutes >= ramp && populationSatisfied || minutes >= ramp + FinishGraceMinutes;
    }

    /// <summary>Minutes until <see cref="Finished"/> at the latest.</summary>
    public static int MinutesLeft(int rampMinutes, TimeSpan sinceStartup) =>
        Math.Max(0, (int)Math.Ceiling(Math.Max(0, rampMinutes) + FinishGraceMinutes - sinceStartup.TotalMinutes));

    public static TimeSpan NominalLoginOffset(int ordinal, int rosterCount, int rampMinutes)
    {
        int target = Math.Max(0, rosterCount);
        if (target == 0 || ordinal < 0 || ordinal >= target)
            return TimeSpan.MaxValue;

        double earlyMinutes = Math.Min(EarlyRampMinutes, Math.Max(0, rampMinutes));
        int earlyTarget = Math.Max(1, (int)Math.Ceiling(target * EarlyPopulationFraction));
        double minutes = ordinal < earlyTarget
            ? earlyMinutes * (ordinal + 1d) / earlyTarget
            : earlyMinutes + Math.Max(0, rampMinutes - earlyMinutes) * (ordinal - earlyTarget + 1d) / Math.Max(1, target - earlyTarget);
        return TimeSpan.FromMinutes(minutes);
    }
}
