using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Who a boss script may pick for a mechanic: the whole raid, real players AND bots (the /spawn and /raid companions
    /// and the autonomous gamebots). The scripts used <c>GetPlayersInRadius</c>, and a <see cref="GameBot"/> is a
    /// <see cref="GameNPC"/>, so every glare, stare, throw, debuff and "random target" landed on the human player alone
    /// (owner, 2026-11-06: "the throws and other unique triggers or scripts or anything target bots too").
    /// <para>
    /// The pool is every real player the script saw before (<c>GetPlayersInRadius</c>, nothing removed) plus the bots in
    /// the same radius. The script's own filters (alive, not a GM, not stealthed, its class rule ...) are applied to
    /// both by the caller's <c>filter</c>, so a real player who could be picked before can still be picked, and one who
    /// could not still cannot. Random picks are uniform over the pool: with bots in it the human is one of many, never
    /// picked more often than before. Messages stay real-player-only (<see cref="Tell"/>); a bot gets the mechanic, not a
    /// packet.
    /// </para>
    /// <para>
    /// A bot is left out of the pool while it rides a stablemaster route, while it is not in the world, and when it is of
    /// the picking NPC's own realm (bosses are realm none, so only realm-aligned scripts ever hit that).
    /// </para>
    /// </summary>
    public static class EncounterTargets
    {
        /// <summary>
        /// Picks that MOVE their victim (throws, teleports, jails, pulls) include bots only while this is on. A bot moved
        /// by a raw <c>MoveTo</c> has no way back to its raid yet; the displacement work (EpicEncounterDisplacement and the
        /// bot recovery, feature/epic-displacement) turns this on, or replaces it per encounter, once a displaced bot
        /// recovers. Off by default: today's throws pick real players exactly as before.
        /// </summary>
        public static bool BotsCanBeDisplaced { get; set; }

        /// <summary>The bots-in-pool value for a pick that moves its victim (see <see cref="BotsCanBeDisplaced"/>).</summary>
        public static bool ForDisplacement => BotsCanBeDisplaced;

        // ---- the pool ---------------------------------------------------------------------------------------------------

        /// <summary>Every raid member in <paramref name="radius"/> of <paramref name="origin"/> the filter accepts: real
        /// players first (in <c>GetPlayersInRadius</c> order), then bots.</summary>
        public static List<GameLiving> InRadius(GameObject origin, ushort radius, Func<GameLiving, bool> filter = null, bool bots = true)
        {
            if (origin == null || origin.CurrentRegion == null)
                return [];
            IEnumerable<GameNPC> npcs = bots ? origin.GetNPCsInRadius(radius) : [];
            return Pool(origin, origin.GetPlayersInRadius(radius), npcs, filter, bots);
        }

        /// <summary>The pool from given candidates (the world-free core of <see cref="InRadius"/>, used by the tests).</summary>
        public static List<GameLiving> Pool(GameObject origin, IEnumerable<GamePlayer> players, IEnumerable<GameNPC> npcs,
            Func<GameLiving, bool> filter = null, bool bots = true)
        {
            var pool = new List<GameLiving>();
            foreach (GamePlayer player in players ?? [])
                if (player != null && (filter == null || filter(player)) && !pool.Contains(player))
                    pool.Add(player);
            if (bots)
                foreach (GameNPC npc in npcs ?? [])
                    if (npc is GameBot bot && IsBotCandidate(origin, bot) && (filter == null || filter(bot)) && !pool.Contains(bot))
                        pool.Add(bot);
            return pool;
        }

        /// <summary>A bot that may join the pool: in the world, not on a stablemaster ride, and not of the picker's realm.
        /// (Like <c>GetPlayersInRadius</c> for players it does not check health: the script's filter does.)</summary>
        public static bool IsBotCandidate(GameObject origin, GameBot bot) =>
            bot != null && bot.ObjectState == GameObject.eObjectState.Active && !bot.IsOnStableMasterRoute &&
            !(origin is GameLiving picker && picker.Realm != eRealm.None && picker.Realm == bot.Realm);

        // ---- picks ------------------------------------------------------------------------------------------------------

        /// <summary>One member of the pool at random (uniform), or null when it is empty.</summary>
        public static GameLiving PickRandom(IReadOnlyList<GameLiving> pool) =>
            pool == null || pool.Count == 0 ? null : pool[Util.Random(0, pool.Count - 1)];

        /// <summary>A random enemy in radius.</summary>
        public static GameLiving Random(GameObject origin, ushort radius, Func<GameLiving, bool> filter = null, bool bots = true) =>
            PickRandom(InRadius(origin, radius, filter, bots));

        /// <summary>A random enemy in radius that isn't <paramref name="except"/> (usually the boss's main target).</summary>
        public static GameLiving RandomOther(GameObject origin, ushort radius, GameObject except, Func<GameLiving, bool> filter = null,
            bool bots = true) =>
            PickRandom(InRadius(origin, radius, l => l != except && (filter == null || filter(l)), bots));

        /// <summary>Up to <paramref name="count"/> different enemies in radius, chosen at random.</summary>
        public static List<GameLiving> RandomSome(GameObject origin, ushort radius, int count, Func<GameLiving, bool> filter = null,
            bool bots = true) =>
            TakeRandom(InRadius(origin, radius, filter, bots), count);

        /// <summary>Up to <paramref name="count"/> different members of a pool, chosen at random.</summary>
        public static List<GameLiving> TakeRandom(IEnumerable<GameLiving> pool, int count)
        {
            var left = pool?.ToList() ?? [];
            var picked = new List<GameLiving>();
            while (picked.Count < count && left.Count > 0)
            {
                int i = Util.Random(0, left.Count - 1);
                picked.Add(left[i]);
                left.RemoveAt(i);
            }
            return picked;
        }

        /// <summary>The nearest enemy in radius (ties: real players first, then the pool's order).</summary>
        public static GameLiving Nearest(GameObject origin, ushort radius, Func<GameLiving, bool> filter = null, bool bots = true)
        {
            GameLiving best = null;
            int bestDistance = int.MaxValue;
            foreach (GameLiving living in InRadius(origin, radius, filter, bots))
            {
                int d = origin.GetDistanceTo(living);
                if (d < bestDistance)
                {
                    best = living;
                    bestDistance = d;
                }
            }
            return best;
        }

        // ---- the filters the scripts used -------------------------------------------------------------------------------

        /// <summary>A real player (not a bot, not a pet).</summary>
        public static bool IsPlayer(GameObject o) => o is GamePlayer;

        /// <summary>A raid member: a real player or a bot (companion or autonomous gamebot).</summary>
        public static bool IsRaider(GameObject o) => o is GamePlayer or GameBot;

        /// <summary>The raid member behind an attacker or target (a real player or a bot), or null for anything else.
        /// Replaces <c>x is GamePlayer</c> where the script meant "someone of the raid".</summary>
        public static GameLiving AsRaider(GameObject o) => o is GamePlayer or GameBot ? (GameLiving)o : null;

        /// <summary><c>PrivLevel == 1</c>: true for a bot, and for a real player whose account is a player's (not a GM's).</summary>
        public static bool NotGm(GameLiving living) => living switch
        {
            GameBot => true,
            GamePlayer player => player.Client?.Account?.PrivLevel == (int)ePrivLevel.Player,
            _ => false,
        };

        /// <summary>The usual filter: alive and not a GM.</summary>
        public static bool AliveNotGm(GameLiving living) => living != null && living.IsAlive && NotGm(living);

        /// <summary>The usual filter with stealth: alive, not a GM, not stealthed.</summary>
        public static bool AliveVisibleNotGm(GameLiving living) => AliveNotGm(living) && !living.IsStealthed;

        /// <summary>A raid member's class (players and bots), or null.</summary>
        public static ICharacterClass ClassOf(GameObject o) => o switch
        {
            GamePlayer player => player.CharacterClass,
            GameBot bot => bot.CharacterClass,
            _ => null,
        };

        /// <summary>A raid member's class ID (<see cref="eCharacterClass"/>), or 0.</summary>
        public static int ClassId(GameObject o) => ClassOf(o)?.ID ?? 0;

        /// <summary>A message to a real player (a bot has no client: it gets nothing).</summary>
        public static void Tell(GameObject o, string message, eChatType type, eChatLoc loc)
        {
            if (o is GamePlayer player)
                player.Out.SendMessage(message, type, loc);
        }
    }
}
