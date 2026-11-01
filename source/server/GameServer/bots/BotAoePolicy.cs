using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// When a bot's area spell is worth casting (owner 2026-10-28, GitHub issue #17: bots never used their area spells
    /// on two or more monsters). A blast counts the monsters already fighting the bot's side that stand well inside its
    /// radius, and is refused outright when it would also reach a mesmerized monster (the hit wakes it) or one that is
    /// not in the fight yet (the hit pulls it). A point blank spell (centred on the caster) needs two or more.
    /// </summary>
    public static class BotAoePolicy
    {
        /// <summary>Only monsters this far inside the edge count: they move while the spell is cast.</summary>
        public const int EdgeMargin = 40;
        /// <summary>Anything that must not be hit is looked for this far past the edge.</summary>
        public const int SafetyMargin = 60;
        public const int MinimumTargets = 2;
        /// <summary>A caster stops this far short of the spot it walks to for a point blank spell.</summary>
        public const int ApproachSlack = 80;
        public const int PointBlankWalkLimit = 700;
        public const long PointBlankWalkMilliseconds = 4_000;
        /// <summary>No point blank walk or cast for this long after one was interrupted or the caster was hit.</summary>
        public const long PointBlankBackoffMilliseconds = 15_000;
        public const int PointBlankFailuresPerFight = 2;

        public readonly record struct Candidate(float Distance, bool Engaged, bool Mezzed);

        public readonly record struct Blast(int Hits, bool Vetoed)
        {
            public bool Worthwhile => !Vetoed && Hits >= MinimumTargets;
        }

        /// <param name="countRadius">The radius to count within (the spell's radius less the margins in use).</param>
        /// <param name="spellRadius">The spell's real radius: the safety check looks past it.</param>
        /// <param name="spellIsMez">A mez neither wakes nor counts a monster that is already mesmerized.</param>
        public static Blast Evaluate(IEnumerable<Candidate> candidates, int countRadius, int spellRadius, bool spellIsMez)
        {
            int hits = 0;
            bool vetoed = false;
            foreach (Candidate candidate in candidates)
            {
                if (candidate.Distance > spellRadius + SafetyMargin)
                    continue;
                if (candidate.Mezzed && spellIsMez)
                    continue;
                if (!candidate.Engaged || candidate.Mezzed)
                {
                    vetoed = true;
                    continue;
                }
                if (candidate.Distance <= countRadius)
                    hits++;
            }
            return new(hits, vetoed);
        }

        /// <summary>An area damage spell beats the best single-target damage spell when its total over the hits is larger.</summary>
        public static bool BeatsSingle(double areaDamage, int hits, double singleDamage) => areaDamage * hits > singleDamage;

        /// <summary>The spot to walk to for a point blank spell: the most hits within the walk limit, then the shortest walk; -1 for none.</summary>
        public static int ChooseSpot(IReadOnlyList<(Blast Blast, float Walk)> spots)
        {
            int best = -1;
            for (int i = 0; i < spots.Count; i++)
            {
                (Blast blast, float walk) = spots[i];
                if (!blast.Worthwhile || walk > PointBlankWalkLimit)
                    continue;
                if (best < 0 || blast.Hits > spots[best].Blast.Hits ||
                    blast.Hits == spots[best].Blast.Hits && walk < spots[best].Walk)
                    best = i;
            }
            return best;
        }

        /// <summary>A walk-in is allowed while no monster is on the caster, it has a group to hold them, and it has not failed too often.</summary>
        public static bool MayWalkIn(bool grouped, bool enemyOnCaster, bool beingInterrupted, long now, long backoffUntil, int failures) =>
            grouped && !enemyOnCaster && !beingInterrupted && now >= backoffUntil && failures < PointBlankFailuresPerFight;
    }
}
