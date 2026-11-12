using System;
using DOL.GS;
using DOL.GS.ServerProperties;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Who comes when a monster brings a friend (owner 2026-10-28). OpenDAoC counts only monsters of the same or an
    /// allied faction, so a monster with no faction never brought anyone: two thirds of the aggressive monsters in the
    /// database have none (Midgard's frore liches, windswept wraiths and chillsome wights among them). A monster with no
    /// faction now brings others of its own kind, as monsters did on live (wraiths bring wraiths): faction-less monsters
    /// of the same name. Monsters with a faction keep the faction rule.
    /// </summary>
    public static class BafFriends
    {
        [ServerProperty("pve", "baf_same_name_without_faction",
            "Does a monster with no faction bring others of its own kind (faction-less monsters with the same name) when it brings a friend? Monsters with a faction always use their faction.", true)]
        public static bool SameNameWithoutFaction = true;

        /// <summary>Pure rule: the faction when the pulled monster has one, otherwise the same name among faction-less monsters.</summary>
        public static bool IsFriend(bool pulledHasFaction, bool factionFriends, bool otherHasFaction, bool sameName, bool sameNameFallback) =>
            pulledHasFaction ? factionFriends : sameNameFallback && !otherHasFaction && sameName;

        public static bool CanBringFriends(GameNPC pulled) => pulled.Faction != null || SameNameWithoutFaction;

        /// <summary>
        /// Dawn of Light's Bring A Friend ranges (owner 2026-10-29: OpenDAoC's 450 units plus 150 per extra group member,
        /// up to 1,500, pulled whole rooms; in Cardova's first room adds came from other rooms and hallways). Close friends
        /// are within 2/5 of the pulled monster's aggro range (200 units for the usual 500); in a dungeon more come from up
        /// to 1,000 units, nearest first. How many come is unchanged (by group size). A friend must be in sight of the
        /// pulled monster: no adds through walls or from the next room.
        /// </summary>
        public const int DungeonReinforcementRadius = 1_000;

        public static int CloseRadius(int aggroRange) => Math.Max(0, aggroRange) * 2 / 5;

        /// <summary>
        /// Outdoors: the close range. In a dungeon the reach grows with the group like Dawn of Light's reinforcement
        /// search (from 250 units, 150 more per other member, at most 1,000): a small group no longer reaches 1,000.
        /// </summary>
        public static int Radius(int aggroRange, bool dungeon, int attackers = 1) =>
            dungeon ? Math.Max(CloseRadius(aggroRange), DungeonRadius(attackers)) : CloseRadius(aggroRange);

        public static int DungeonRadius(int attackers) =>
            Math.Min(DungeonReinforcementRadius, 250 + 150 * (Math.Max(1, attackers) - 1));

        private const string BafFightProperty = "OfflineDAoC.BafFightTick";
        public const long BafFightMilliseconds = 120_000;

        /// <summary>Pure rule: a puller still in combat within two minutes of the last adds is in the same fight.</summary>
        public static bool SameFight(bool inCombat, long now, long lastBaf) =>
            inCombat && lastBaf > 0 && now - lastBaf < BafFightMilliseconds;

        public static bool InBafFight(GamePlayer puller)
        {
            long now = GameLoop.GameLoopTime;
            long last = puller.TempProperties.GetProperty<long>(BafFightProperty);
            if (!SameFight(puller.InCombat, now, last))
                return false;
            puller.TempProperties.SetProperty(BafFightProperty, now); // the fight goes on
            return true;
        }

        public static void MarkBafFight(GamePlayer puller) =>
            puller.TempProperties.SetProperty(BafFightProperty, GameLoop.GameLoopTime);

        /// <summary>In sight along the walkable ground (the navmesh); where there is no navmesh, assumed so.</summary>
        public static bool InSight(GameNPC pulled, GameNPC other)
        {
            var nav = PathfindingProvider.Instance;
            Zone zone = pulled?.CurrentZone;
            if (nav == null || !nav.IsAvailable || zone == null || !nav.HasNavmesh(zone))
                return true;
            return nav.HasLineOfSight(zone, new System.Numerics.Vector3(pulled.X, pulled.Y, pulled.Z),
                new System.Numerics.Vector3(other.X, other.Y, other.Z), nav.DefaultFilters);
        }

        public static bool IsFriend(GameNPC pulled, GameNPC other) =>
            other != null && pulled != null && other is not GameBot &&
            IsFriend(pulled.Faction != null, pulled.Faction != null && other.IsFriend(pulled), other.Faction != null,
                string.Equals(pulled.Name, other.Name, StringComparison.OrdinalIgnoreCase), SameNameWithoutFaction);
    }
}
