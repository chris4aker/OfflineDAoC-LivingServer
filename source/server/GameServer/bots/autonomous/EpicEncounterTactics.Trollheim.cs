using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Trollheim's raid rule (Living Server 1.2, every big boss in): Nosdoden. His essence waves at 90%-10% are the catalog's
    /// required adds (the raid kills each wave, then goes back to him). His Call of Void is announced ("Nosdoden starts
    /// casting void magic at X") and lands 5 s later, 1,100 cold to everyone within 550 of X: the script raises it as a
    /// glare, so everyone within it but X and his tank steps out and the healers heal X first (Signals.cs). Here: the
    /// casters, archers and healers outside his target's party fight from beyond 950 of him (his melee stand within 350 of
    /// him, and a void on one of them reaches 550 further), as the period did: "keeping all casters out of range to avoid
    /// one-shots, and keeping a tank or two nearby" (Allakhazam, Nosdoden, 18 May 2003).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        public static readonly BossRing[] TrollheimRings =
        [
            new("Nosdoden", 950, "a Call of Void on his melee reaches 550 around them"),
        ];

        [DungeonRules]
        private static IEnumerable<DungeonRule> TrollheimRules() =>
        [
            new("Nosdoden", ["Nosdoden"], c => RingAdvice(c, TrollheimRings))
            {
                Summary = "Casters, archers and healers outside his target's party fight from beyond 950 of him, so one Call " +
                          "of Void (550 around a raider, announced 5 s ahead and stepped out of) never takes melee and casters at once.",
            },
        ];
    }
}
