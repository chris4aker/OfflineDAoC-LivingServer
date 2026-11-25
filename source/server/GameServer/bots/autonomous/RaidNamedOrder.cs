using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// A raid dungeon whose named are fought in a fixed order (1.2.3, #414). A raid's route takes the nearest epic encounter
    /// first (RealmRaidDungeonRoute.WalkEstimate, a straight line), which in Fomor City would send the raid from the entrance to
    /// Nogoribando, past Krackenschtein's climb. Period (Thredh's Fomor guide, Clan of the Dragonfly, Guinevere; Valmerwolf's
    /// translation 2003, revised 2005): "moving from one ring to the next is always through a single forced passage per ring,
    /// guarded by a named": Krackenschtein on the outer ring by the climb, Nogoribando at the stairs at the far end of the
    /// second ring, Anurigunda at the Hydra's door, then Myrddraxis.
    ///
    /// The rule: a named of the order is no target while an earlier one of the order still lives, unless it is fighting
    /// (a named that fights the raid is fought). Types match by prefix, so "Myrddraxis" holds his heads back too. Bosses are
    /// not changed; only the raid's choice of its next encounter is.
    /// </summary>
    public static class RaidNamedOrder
    {
        private static readonly Dictionary<ushort, string[]> Orders = new()
        {
            [AutonomousRealmRaid.FomorRegion] = ["Krackenschtein", "Nogoribando", "Anurigunda", "Myrddraxis"],
        };

        /// <summary>The order of a region's named (empty: none).</summary>
        public static string[] For(ushort region) => Orders.TryGetValue(region, out string[] order) ? order : [];

        /// <summary>The place of a script type in an order (pure): the first entry it equals or starts with, or -1.</summary>
        public static int Rank(string[] order, string type)
        {
            if (order == null || string.IsNullOrEmpty(type)) return -1;
            for (int i = 0; i < order.Length; i++)
                if (type.StartsWith(order[i], StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// The earlier named a named of type <paramref name="type"/> waits for (pure), or null when it may be a target: it is
        /// in no order, it is fighting, or no earlier one of the order is among <paramref name="aliveTypes"/>.
        /// </summary>
        public static string WaitsFor(string[] order, string type, bool inCombat, IEnumerable<string> aliveTypes)
        {
            int rank = Rank(order, type);
            if (rank <= 0 || inCombat || aliveTypes == null) return null;
            return aliveTypes.Select(t => (Type: t, Rank: Rank(order, t)))
                .Where(t => t.Rank >= 0 && t.Rank < rank)
                .OrderBy(t => t.Rank).Select(t => t.Type).FirstOrDefault();
        }

        /// <summary>The named of the order the raid goes for next (pure): the earliest alive, or null.</summary>
        public static string Next(string[] order, IEnumerable<string> aliveTypes) =>
            aliveTypes?.Select(t => (Type: t, Rank: Rank(order, t))).Where(t => t.Rank >= 0)
                .OrderBy(t => t.Rank).Select(t => t.Type).FirstOrDefault();

        /// <summary>The REALM_EXPEDITION_NAMED_ORDER line.</summary>
        public static string Line(ushort region, string next, int heldBack) =>
            $"REALM_EXPEDITION_NAMED_ORDER region={region} next=\"{next ?? "none"}\" heldBack={heldBack} " +
            "reason=\"the period's order: each ring's passage is guarded by a named, fought before the next\"";
    }
}
