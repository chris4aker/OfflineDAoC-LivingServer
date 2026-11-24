using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// The levels a gamebot party hunts (1.2.3, #536). A party used to take camps +3 to +bonus raw levels over its
    /// average (bonus +3..+10, +3..+6 for five or six), with the highest member only setting the grey floor. That is a
    /// level-50 rule: at level 10-15 con bands are one or two levels wide, so +3 is already red and +5 purple, and the
    /// lowest member of a spread party is further below. In the 1.2.2 log (11/24) 2,950 of 3,927 grouped deaths (75%)
    /// were to monsters purple to the victim.
    ///
    /// The band is now set in con steps against the party's members, as players picked group camps:
    /// <list type="bullet">
    /// <item>at most orange to the lowest member (red for a full eight), one step less after a wipe;</item>
    /// <item>at least yellow to the lowest member (blue after a wipe), so the band never sinks below the party's level;</item>
    /// <item>never grey to the highest member (no experience for it);</item>
    /// <item>never above the rolled raw bonus over the average (the old level-50 cap stays as an upper bound).</item>
    /// </list>
    /// At level 50 with every member 50 this is the old band's top (orange 51-55, red 56-60 for eight). Every member
    /// counts, a hosted real player included; the planner takes the highest level in the band.
    /// </summary>
    public static class AutonomousGroupCampBand
    {
        public const int FullParty = 8;

        public readonly record struct Band(int Minimum, int Maximum, int HighestMemberLevel)
        {
            public bool Allows(int level) => level >= Minimum && level <= Maximum &&
                                             !AutonomousSoloCampFloor.IsGrey(HighestMemberLevel, level);
        }

        public static ConColor MaximumConForLowest(int groupSize, int wipePenalty) =>
            (ConColor)Math.Max((int)ConColor.YELLOW,
                (int)(groupSize >= FullParty ? ConColor.RED : ConColor.ORANGE) - (wipePenalty > 0 ? 1 : 0));

        public static ConColor MinimumConForLowest(int wipePenalty) => wipePenalty > 0 ? ConColor.BLUE : ConColor.YELLOW;

        public static Band For(int lowestMemberLevel, int highestMemberLevel, int planningLevel, int preferredBonus,
            int groupSize, int wipePenalty)
        {
            int lowest = Math.Max(1, Math.Min(lowestMemberLevel, highestMemberLevel));
            int minimum = LowestLevelAtLeast(lowest, MinimumConForLowest(wipePenalty));
            int conCap = HighestLevelAtMost(lowest, MaximumConForLowest(groupSize, wipePenalty));
            int maximum = Math.Max(minimum, Math.Min(planningLevel + preferredBonus, conCap));
            return new Band(minimum, maximum, Math.Max(lowest, highestMemberLevel));
        }

        /// <summary>The level the party hunts among valid camp levels: the highest in the band, or 0.</summary>
        public static int SelectLevel(IEnumerable<int> levels, Band band)
        {
            int best = 0;
            foreach (int level in levels ?? Array.Empty<int>())
                if (band.Allows(level))
                    best = Math.Max(best, level);
            return best;
        }

        public static int LowestLevelAtLeast(int observerLevel, ConColor con)
        {
            for (int level = 0; level < 127; level++)
                if (AutonomousPveBossDanger.ConTo(observerLevel, level) >= con)
                    return level;
            return 127;
        }

        public static int HighestLevelAtMost(int observerLevel, ConColor con)
        {
            int best = 0;
            for (int level = 0; level < 127; level++)
            {
                if (AutonomousPveBossDanger.ConTo(observerLevel, level) > con)
                    break;
                best = level;
            }
            return best;
        }
    }
}
