using System;

namespace DOL.GS
{
    /// <summary>
    /// Solo grind spots. A camp used to qualify when any one of its spawns conned green, so a camp of grey monsters
    /// with one rare green spawn was valid and the bot stood waiting for that spawn. Level 50 solo bots only count
    /// monsters of level 38 or higher (the top of green, blue and up: con at 50 is grey 0-35, green 36-40, blue
    /// 41-45), and never pick a lower monster at the camp (with none left, the normal empty-camp timeout moves them
    /// on). Group spots use <see cref="AutonomousGroupCampBand"/>.
    ///
    /// 1.2.3 (#535): the share rule holds at every level. Below 50 it was skipped, and at low levels con bands are a
    /// level or two wide, so a camp of mostly grey spawns qualified on one green: the 1.2.2 log (11/24) had 5,176
    /// AUTONOMOUS_CAMP_OUTLEVELED in five hours (on 11/23, 5,265 were level 4 bots at level 1 skeletal pawns), the bot
    /// walking from one grey camp to the next. A camp now needs at least half of its spawns, and at least 3 (or all of a
    /// smaller camp below 50), to con green or better and no harder than the bot's ceiling.
    /// </summary>
    public static class AutonomousSoloCampFloor
    {
        public const int Level50MinimumMonsterLevel = 38;
        public const int MinimumQualifyingSpawns = 3;

        public static int MinimumMonsterLevel(int botLevel) => botLevel >= 50 ? Level50MinimumMonsterLevel : 0;

        public static bool Allows(int botLevel, int monsterLevel) => monsterLevel >= MinimumMonsterLevel(botLevel);

        /// <summary>Whether a monster of this level is grey (no experience) to a bot of this level.</summary>
        public static bool IsGrey(int botLevel, int monsterLevel) =>
            ConLevels.GetConColor(ConLevels.GetConLevel(Math.Max(0, botLevel), Math.Max(0, monsterLevel))) <= ConColor.GREY;

        public static bool CampQualifies(int botLevel, int qualifyingSpawns, int totalSpawns)
        {
            int minimum = botLevel >= 50 ? MinimumQualifyingSpawns : Math.Min(MinimumQualifyingSpawns, Math.Max(1, totalSpawns));
            return qualifyingSpawns >= Math.Max(minimum, (totalSpawns + 1) / 2);
        }
    }
}
