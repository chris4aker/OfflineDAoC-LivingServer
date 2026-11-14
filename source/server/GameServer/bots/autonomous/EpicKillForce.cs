using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// "Slain by a force of N warriors": the boss scripts counted real players only, so a raid of bots read "a force of 0
    /// warriors" (Golestandt, 2026-11-13 sweep). The force is the players they counted plus the gamebots and companions
    /// standing around the boss. Kill points and other awards still go by the players' own count.
    /// </summary>
    public static class EpicKillForce
    {
        public static int Count(GameObject boss, int players) =>
            boss == null ? players : players + boss.GetNPCsInRadius(WorldMgr.VISIBILITY_DISTANCE).OfType<GameBot>().Count();
    }
}
