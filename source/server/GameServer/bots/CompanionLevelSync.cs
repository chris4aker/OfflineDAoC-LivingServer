using System;

namespace DOL.GS
{
    /// <summary>
    /// /spawn helpers keep up with their owner's level while grouped with him (issue #571 step 6; ported from DetShaft
    /// OfflineDAoC-LivingServer 56972bb08 / 6ce49e33b, GPL-3.0). Helpers earn no experience (SuppressLootAndProgress),
    /// so before this a helper spawned at 20 stayed 20 until it was respawned, and the only catch-up was the one-time
    /// endgame upgrade when the owner reached 50. GameBot.TryMatchOwnerLevel applies these rules on the helper's idle
    /// turn. Hired companions, /bot companions, autonomous gamebots and the Epic Special Forces never use them.
    /// </summary>
    public static class CompanionLevelSync
    {
        /// <summary>The level a helper should be at: its owner's, never lower than it already is, within 1..max.</summary>
        public static byte TargetLevel(int ownerLevel, int botLevel, int maxLevel)
        {
            int max = Math.Max(1, maxLevel);
            int target = Math.Clamp(ownerLevel, 1, max);
            return (byte)Math.Clamp(Math.Max(botLevel, target), 1, max);
        }

        /// <summary>Only a helper in its owner's current group follows the owner's level.</summary>
        public static bool InOwnersGroup(object botGroup, object ownerGroup) =>
            botGroup != null && ReferenceEquals(botGroup, ownerGroup);

        /// <summary>
        /// The kit level after a level sync. A helper rolls in the same level-10..level band it spawned with
        /// (TemporaryGroupCommands; the full endgame kit at 50) but never below the kit it already wore.
        /// </summary>
        public static int GearLevel(int newLevel, int previousGearLevel, double roll)
        {
            newLevel = Math.Max(1, newLevel);
            if (TemporaryCompanionBalance.IsEndgame(true, newLevel))
                return newLevel;
            int floor = Math.Max(1, newLevel - 10);
            int span = newLevel - floor + 1;
            int rolled = floor + Math.Clamp((int)(Math.Clamp(roll, 0, 0.999999) * span), 0, span - 1);
            return Math.Clamp(Math.Max(rolled, previousGearLevel), floor, newLevel);
        }

        /// <summary>
        /// Which bots follow the owner's level: /spawn (and /raid) helpers only. Autonomous gamebots, including one in
        /// a player's group, and the Epic Special Forces keep their own progression; hired and /bot companions are not
        /// temporary helpers.
        /// </summary>
        public static bool Applies(bool temporaryHelper, bool autonomous, bool specialForce) =>
            temporaryHelper && !autonomous && !specialForce;
    }
}
