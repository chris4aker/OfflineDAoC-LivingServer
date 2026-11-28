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
        /// <summary>51B: the battleground this bot was last logged as staying in out-levelled (0 = not staying).</summary>
        private ushort _battlegroundStayLogged;

        /// <summary>
        /// #572, owner's answer 51B: an autonomous gamebot that out-levels the battleground it is in stays there until it
        /// dies, as players did; it then leaves and the level cap keeps it out. One look per call (it also records the
        /// in-bracket sighting the stay needs). A bot in a player's company keeps today's rules.
        /// </summary>
        internal static bool StaysOutlevelled(GameBot bot)
        {
            if (bot == null) return false;
            bool inBattleground = BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID);
            return AutonomousBattlegroundExit.ObserveStay(bot.DatabaseID, bot.CurrentRegionID, bot.Level, bot.IsAlive,
                inBattleground && InPlayerCompany(bot), AutonomousBattlegroundExit.NowMilliseconds());
        }

        /// <summary>A hired companion, a bot in a player's group, or one on a duty lease the watchdog already leaves alone.</summary>
        internal static bool InPlayerCompany(GameBot bot) =>
            bot.IsHiredCompanion || bot.IsPlayerLedGroup || AutonomousStuckWatchdog.HasHumanCompany(bot);

        /// <summary>The battleground this bot works: the one it stays in out-levelled, otherwise its level's (null above all).</summary>
        private static BattlegroundBrackets.Bracket WorkingBattleground(GameBot bot) =>
            AutonomousBattlegroundExit.WorkingBracket(StaysOutlevelled(bot), bot.CurrentRegionID, bot.Level);

        /// <summary>The controller's turn: records the sighting, and logs once when a stay begins.</summary>
        private void ObserveBattlegroundStay(GameBot bot)
        {
            bool stays = StaysOutlevelled(bot);
            if (!stays)
            {
                _battlegroundStayLogged = 0;
                return;
            }
            if (_battlegroundStayLogged == bot.CurrentRegionID) return;
            _battlegroundStayLogged = bot.CurrentRegionID;
            BattlegroundBrackets.Bracket here = BattlegroundBrackets.ForRegion(bot.CurrentRegionID);
            Log.Info($"AUTONOMOUS_BATTLEGROUND_OUTLEVEL_STAY bot={bot.Name} id={bot.DatabaseID} " +
                     $"realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} battleground={here?.Name} " +
                     $"maxLevel={here?.MaxLevel ?? 0}");
        }

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
