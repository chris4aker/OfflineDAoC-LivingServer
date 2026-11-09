namespace DOL.GS
{
    /// <summary>
    /// How much navigation one camp target search may spend (AutonomousWorldBotController.FindCampTarget).
    ///
    /// Owner's PC, 1.1.3 RC nights (2026-11-08/09): three of the seventeen server stalls over a second were one bot's AI turn of
    /// 2.0-2.2 s, a Sluaghbinder "Searching the dampwood mite camp" (Long NpcService.Tick: PathStraight=1906-1970,
    /// ClosestPoint=2240-2327, Corridor=1013-1060 in a single turn). A camp whose targets need a route proof (dungeons, audited
    /// camps, source-empty camps, flyers) proved one candidate after another, nearest first, until one passed: with dozens of
    /// unreachable mites in 5,000 units, hundreds of proofs in one turn, and the same ones again a second later.
    ///
    /// Now one search proves at most <see cref="RouteProofsPerSearch"/> routes and casts at most <see cref="SightChecksPerSearch"/>
    /// dungeon sight lines; the next candidates wait for the next search (0.9-1.4 s later), and a target whose route failed is
    /// skipped for <see cref="RetryMilliseconds"/> (one out of sight for <see cref="SightRetryMilliseconds"/>). Solo Savages keep their own, longer failed-route memory.
    /// </summary>
    public static class AutonomousCampTargetProof
    {
        public const int RouteProofsPerSearch = 4;
        public const int SightChecksPerSearch = 24;
        public const long RetryMilliseconds = 60_000;
        /// <summary>A dungeon target out of sight is looked past for this long, so the next searches reach the ones behind it.</summary>
        public const long SightRetryMilliseconds = 15_000;

        /// <summary>Whether this search may prove another route (<paramref name="proofsSoFar"/> already made). Pure.</summary>
        public static bool MayProveRoute(int proofsSoFar) => proofsSoFar < RouteProofsPerSearch;

        /// <summary>Whether this search may cast another dungeon sight line. Pure.</summary>
        public static bool MayCheckSight(int checksSoFar) => checksSoFar < SightChecksPerSearch;
    }
}
