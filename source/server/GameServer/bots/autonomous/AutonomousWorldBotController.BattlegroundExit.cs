using System.Numerics;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// #572 step 4: the battleground exit's clock (LeaveBattleground) and the border-keep spot the watchdog uses for a
    /// forced exit (AutonomousStuckWatchdog). Kept apart from the battleground tour code, which #530's work changes.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>GameLoop time this leave started, and of the last LeaveBattleground call (0 = not leaving).</summary>
        private long _battlegroundLeavingSince;
        private long _battlegroundLastLeaveCall;

        /// <summary>
        /// Where a gamebot taken out of a battleground lands: the arrival point of its realm's battleground exit (its
        /// border keep, the destination KeepManager.ExitBattleground gives players). False when the row is missing.
        /// </summary>
        public static bool TryBattlegroundExitSpot(eRealm realm, out string name, out ushort regionId, out int x, out int y,
            out int z, out ushort heading)
        {
            name = string.Empty;
            regionId = 0;
            x = y = z = 0;
            heading = 0;
            DbTeleport exit = BattlegroundExit(realm);
            if (exit == null || exit.RegionID <= 0) return false;
            Vector3 arrival = AutonomousTownTeleporters.Arrival(exit, 0);
            name = exit.TeleportID;
            regionId = (ushort)exit.RegionID;
            x = (int)arrival.X;
            y = (int)arrival.Y;
            z = (int)arrival.Z;
            heading = (ushort)exit.Heading;
            return true;
        }
    }
}
