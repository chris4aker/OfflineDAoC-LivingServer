using System;

namespace DOL.GS
{
    /// <summary>
    /// Who a solo gamebot's death is blamed on (1.2.3, #537). The 1.2.2 log (11/24) had 14,166 deaths in about five
    /// hours, roughly one per bot-hour; 5,777 were to purple monsters and 5,598 of those came before the bot reached its
    /// camp. The death handler blamed the camp the bot was heading to: it benched that camp's monster across the region,
    /// remembered it as the failed target and lowered the bot's camp con, while the killer (a level 24-26 howling maiden
    /// that killed 120 different bots at one spot) was never remembered. Now:
    /// <list type="bullet">
    /// <item>a death to the camp's own monster (or to an unknown killer) blames the camp's monster, as before;</item>
    /// <item>a death to another monster that conned red or purple to the bot is a danger on the way: the killer and its
    /// spot are remembered (<see cref="AutonomousPveBossDanger"/>), and the bot keeps its camp con, as a player who met
    /// a wandering purple avoids that place rather than hunting greens;</item>
    /// <item>any other death (an add, a player, an enemy bot) benches no species.</item>
    /// </list>
    /// The camp cell itself still counts a death on its route (<see cref="AutonomousCampDangerBench"/>).
    /// </summary>
    public static class AutonomousDeathBlame
    {
        /// <summary>The killer of an autonomous gamebot, kept at death because the bot releases elsewhere before replanning.</summary>
        public readonly record struct Killer(string Name, int Level, string Kind, ushort RegionId, int X, int Y)
        {
            public bool IsMonster => string.Equals(Kind, "monster", StringComparison.Ordinal);
        }

        public static bool IsCampTarget(Killer? killer, string campMonster) =>
            killer is { IsMonster: true } known && !string.IsNullOrWhiteSpace(campMonster) &&
            string.Equals(known.Name?.Trim(), campMonster.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>A monster that was not the camp's and conned red or purple to the victim: a danger on the way.</summary>
        public static bool IsDangerOnTheWay(Killer? killer, string campMonster, int victimLevel) =>
            killer is { IsMonster: true } known && !IsCampTarget(killer, campMonster) &&
            AutonomousPveBossDanger.ConTo(victimLevel, known.Level) >= ConColor.RED;

        /// <summary>Whether the camp's monster is blamed: it was the killer, or the killer is not known.</summary>
        public static bool BlamesCampMonster(Killer? killer, string campMonster) =>
            !string.IsNullOrWhiteSpace(campMonster) && (killer == null || IsCampTarget(killer, campMonster));

        /// <summary>The species bench key a solo death counts against, or null for none.</summary>
        public static string SpeciesToBench(Killer? killer, ushort campRegionId, string campMonster) =>
            BlamesCampMonster(killer, campMonster) ? AutonomousCampDangerBench.SpeciesKey(campRegionId, campMonster) : null;
    }
}
