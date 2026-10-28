using System;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The periodic part of the living-world features (AutonomousLivingServer.Pulse, once a second on the game loop): reputation
/// talk (AutonomousPlayerDeeds), gold spammers, roleplayers' in-character lines and true rumours. Each part has its own
/// switch and timing, and one failing part never stops the others.
/// </summary>
public static class AutonomousLivingWorld
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static void Pulse(long now)
    {
        Run("deeds", () => AutonomousPlayerDeeds.Pulse(now));
        Run("gold spammers", () => AutonomousGoldSpammers.Pulse(now));
        Run("roleplayers", () => AutonomousRoleplayers.Pulse(now));
        Run("rumours", () => AutonomousRumours.Pulse(now));
    }

    private static void Run(string part, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Warn($"Living world: {part} pulse failed", exception);
        }
    }
}
