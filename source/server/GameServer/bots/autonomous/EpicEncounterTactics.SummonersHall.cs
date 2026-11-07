using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Summoner's Hall's raid rules (1.2, epic wave 2). Grand Summoner Govannon's sacrifice and demon, his demon-form spread
    /// and the Shade of Aelfgar are catalog tactics (hand-notes "aiTactics"): casters and archers kill the walkers before
    /// they reach his altar, and if both get there the whole raid kills the Shade (it sends raiders up to the gallery).
    /// Code rule here: Summoner Lossren's infected ghoul crawls toward one raider and bursts (Plague, 1,000 in 500), so the
    /// raider it crawls toward walks away from it while the casters kill it (a catalog ranged add).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The ghoul's target walks away once it is this close (it moves at 110 and bursts at 200).</summary>
        public const int GhoulKiteRadius = 650;

        [DungeonRules]
        private static IEnumerable<DungeonRule> SummonersHallRules() =>
        [
            new("SummonerLossren", ["SummonerLossren", "TorturedSouls", "ExplodeUndead"], LossrenGhoulKite)
            {
                Summary = "The raider the infected ghoul crawls toward walks away from it; casters kill it (ranged add).",
            },
        ];

        private static Advice LossrenGhoulKite(RuleContext c)
        {
            foreach (GameNPC ghoul in c.All("ExplodeUndead"))
                if (ghoul.TargetObject == c.Raider && AwayFrom(c.At, PosOf(ghoul), GhoulKiteRadius) is Vector3 away)
                    return new(BotAction.Reposition, c.Target, away, $"the ghoul crawls at me: away from it ({GhoulKiteRadius})", "epic_ghoul_kite");
            return default;
        }
    }
}
