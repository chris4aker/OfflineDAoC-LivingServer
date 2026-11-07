using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The dragons' wing-gust throw, restored from OpenDAoC-Core 3513bad (1.2, behind
    /// <see cref="EpicEncounterMechanics.AllowDragonThrow"/>, on per dragon). Every 60-80 s of a fight on the ground the dragon
    /// picks two to five raiders within 2,500 that are not its target, and each still within 2,000 is hurled to one of five
    /// spots of its lair (high above the floor: the fall hurts). The original picked real players only; now the pool is
    /// real players and the bots fighting the dragon, so a player is picked no more often than before and less with bots
    /// around. Bots are moved through <see cref="EncounterDisplacement"/> (their dragon's AllowBotDisplacement) and walk back
    /// (<see cref="EncounterDisplacementRecovery"/>).
    /// </summary>
    public static class DragonEncounterThrow
    {
        public const int PickRadius = 2_500;
        public const int MoveRadius = 2_000;
        public const int MinimumPicks = 2;
        public const int MaximumPicks = 5;
        public const int MinimumIntervalMilliseconds = 60_000;
        public const int MaximumIntervalMilliseconds = 80_000;

        public readonly record struct Spot(int X, int Y, int Z, ushort Heading);

        /// <summary>The five spots of each lair (3513bad; the first is above the lair's spawn point).</summary>
        public static readonly IReadOnlyDictionary<string, Spot[]> Spots = new Dictionary<string, Spot[]>(StringComparer.Ordinal)
        {
            ["AlbGolestandt"] =
            [
                new(391348, 755751, 1815, 2069), new(398605, 754458, 1404, 1042), new(392450, 743176, 1404, 4063),
                new(383669, 758112, 1847, 3003), new(401432, 755310, 1728, 1065),
            ],
            ["MidGjalpinulva"] =
            [
                new(708632, 1021688, 3721, 2499), new(713073, 1015679, 3833, 441), new(713388, 1024499, 3833, 1372),
                new(705812, 1024952, 3833, 2573), new(706019, 1018867, 3833, 3521),
            ],
            ["HibCuuldurach"] =
            [
                new(408807, 706640, 4315, 1588), new(404579, 699656, 4683, 1840), new(410650, 698271, 4758, 2890),
                new(402790, 707787, 4083, 2628), new(407532, 695634, 4533, 281),
            ],
        };

        public static Spot[] SpotsFor(GameNPC dragon) =>
            dragon != null && Spots.TryGetValue(dragon.GetType().Name, out Spot[] spots) ? spots : [];

        /// <summary>The next throw's delay.</summary>
        public static int NextDelay() => Util.Random(MinimumIntervalMilliseconds, MaximumIntervalMilliseconds);

        /// <summary>
        /// Who the gust can pick: real players (alive, plain accounts) within <see cref="PickRadius"/> and, where the dragon may
        /// move bots (<see cref="EncounterTargets.DisplacesBots"/>, the same switch <see cref="EncounterDisplacement.Move"/> asks),
        /// the bots on the dragon's aggro list within it; never its current target. With the dragon's bot displacement off the
        /// gust picks real players only, as the original did. (The aggro list, not every bot in reach: bots fighting it.)
        /// </summary>
        public static List<GameLiving> Candidates(GameNPC dragon, IEnumerable<GameLiving> aggro)
        {
            var pool = new List<GameLiving>();
            if (dragon == null) return pool;
            foreach (GamePlayer player in dragon.GetPlayersInRadius(PickRadius))
                if (player != null && player.IsAlive && player.Client?.Account?.PrivLevel == 1 && player != dragon.TargetObject && !pool.Contains(player))
                    pool.Add(player);
            if (aggro != null && EncounterTargets.DisplacesBots(dragon))
                foreach (GameLiving living in aggro)
                    if (living is GameBot bot && bot.IsAlive && bot != dragon.TargetObject && bot.CurrentRegionID == dragon.CurrentRegionID &&
                        dragon.IsWithinRadius(bot, PickRadius) && !pool.Contains(bot))
                        pool.Add(bot);
            return pool;
        }

        /// <summary>
        /// How high above the thrown raider this dragon may hurl, in units; 0 = the original spots (up to about 1,400 above
        /// the lair floor: about 60% of a raider's health). Server properties epic_dragon_throw_height_golestandt /
        /// _gjalpinulva / _cuuldurach (owner default 0, the original).
        /// </summary>
        public static int MaxThrowHeightFor(GameNPC dragon) => dragon?.GetType().Name switch
        {
            "AlbGolestandt" => ServerProperties.Properties.EPIC_DRAGON_THROW_HEIGHT_GOLESTANDT,
            "MidGjalpinulva" => ServerProperties.Properties.EPIC_DRAGON_THROW_HEIGHT_GJALPINULVA,
            "HibCuuldurach" => ServerProperties.Properties.EPIC_DRAGON_THROW_HEIGHT_CUULDURACH,
            _ => 0,
        };

        /// <summary>The height a spot is thrown to: the spot's own, or at most <paramref name="maxHeight"/> above
        /// <paramref name="standingZ"/> when the setting softens the throw (never higher than the original spot).</summary>
        public static int ThrowZ(Spot spot, int standingZ, int maxHeight) =>
            maxHeight > 0 ? Math.Min(spot.Z, standingZ + maxHeight) : spot.Z;

        /// <summary>Two to five of the pool, at random (pure but for <paramref name="random"/>).</summary>
        public static List<GameLiving> Pick(IReadOnlyList<GameLiving> pool, Random random)
        {
            int count = random.Next(MinimumPicks, MaximumPicks + 1);
            return pool.OrderBy(_ => random.Next()).Take(count).ToList();
        }

        /// <summary>The gust: picks and hurls. Returns how many raiders were moved.</summary>
        public static int Throw(GameNPC dragon, IEnumerable<GameLiving> aggro, Random random = null)
        {
            if (dragon == null || !dragon.IsAlive || !EpicEncounterMechanics.AllowDragonThrowFor(dragon)) return 0;
            Spot[] spots = SpotsFor(dragon);
            if (spots.Length == 0) return 0;
            random ??= Random.Shared;
            int maxHeight = MaxThrowHeightFor(dragon);
            int moved = 0;
            foreach (GameLiving living in Pick(Candidates(dragon, aggro), random))
            {
                if (living == null || !living.IsAlive || !living.IsWithinRadius(dragon, MoveRadius)) continue;
                EncounterTargets.Tell(living, dragon.Name + " begins flapping his wings violently. You struggle to hold your footing on the ground!",
                    eChatType.CT_Broadcast, eChatLoc.CL_ChatWindow);
                Spot s = spots[random.Next(spots.Length)];
                if (EncounterDisplacement.Move(living, dragon.CurrentRegionID, s.X, s.Y, ThrowZ(s, living.Z, maxHeight), s.Heading, DisplacementKind.Throw, dragon))
                    moved++;
            }
            return moved;
        }
    }
}
