using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// A raid's mesmerized are cured across its parties (Living Server 1.2). A bot cured only its own party's mesmerized,
    /// and only when nobody there needed a heal. Play test duo34 (2026-11-11, 128 Hibernians at Spindler Broodmother, who
    /// mesmerizes everyone within 300 of a random raider for 60 seconds about every half minute): 95 to 105 of the raid
    /// stood mesmerized on average through the second, third and fourth minute, her health moved 65% to 61% in those
    /// minutes, and the raid wiped at 57% and 52%. Bards and Mentalists (Hibernia), Minstrels and Sorcerers (Albion),
    /// Healers and Spiritmasters (Midgard) have the cure: 1,500 range, 3 seconds.
    /// </summary>
    public static class RaidCureMezz
    {
        /// <summary>A mesmerized raider a curer could reach.</summary>
        public readonly record struct Mezzed(long Id, bool CanCure, bool Healer, bool Tank);

        /// <summary>Whom to cure first: one who can cure in turn (each cured curer frees others), then a healer, then a
        /// tank, then anyone.</summary>
        public static int Rank(Mezzed m) => m.CanCure ? 0 : m.Healer ? 1 : m.Tank ? 2 : 3;

        /// <summary>
        /// The mesmerized raider this curer takes (pure), or -1 when there is none: from the most urgent kind present
        /// (<see cref="Rank"/>), a different one for each curer as far as there are enough (by the curer's id), so that
        /// sixteen curers do not all cast on the same raider.
        /// </summary>
        public static int Choose(IReadOnlyList<Mezzed> mezzed, long curerId)
        {
            if (mezzed == null || mezzed.Count == 0) return -1;
            int best = mezzed.Min(Rank);
            int[] urgent = Enumerable.Range(0, mezzed.Count).Where(i => Rank(mezzed[i]) == best).OrderBy(i => mezzed[i].Id).ToArray();
            return urgent[(int)(Math.Abs(curerId) % urgent.Length)];
        }

        /// <summary>Whether the raid cure comes before this turn's ordinary heal (pure): always, unless a member of the
        /// curer's own party is about to die.</summary>
        public static bool ComesBeforeHealing(int partyEmergencies) => partyEmergencies <= 0;
    }
}
