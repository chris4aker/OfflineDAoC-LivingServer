using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Commands;

namespace DOL.GS
{
    /// <summary>
    /// #525 (owner 2026-11-24): defender gamebots repair keep doors the way players did, with wood. The door's slow
    /// self-repair (5% per 30 minutes, GameKeepDoor) stays as a backstop.
    /// - Who: autonomous gamebots that were defending a keep when its defense ended with the keep still theirs, and who
    ///   are still near it (<see cref="WindowMilliseconds"/>). At most <see cref="RepairersPerDoor"/> on one door.
    /// - How: the player's /repair rules (RepairCommandHandler): a 20-second repair, 5% of the door's health on
    ///   success, the wood units the keep level needs taken from the bot's backpack on success only, the chance
    ///   woodworking skill against (level + 1) x 50, and no repair while the bot is in combat or the door is under attack
    ///   (nor, as for the self-repair, with an enemy within 3,000 of the door).
    /// - Wood: the bot's own wood first; otherwise it buys boards for coin (real money, BotSinks) from a merchant of its
    ///   realm in the same region that sells them, the cheapest per wood unit. No wood seller, no coin or no room: no
    ///   repair, and the self-repair goes on.
    /// Companions and players never come here.
    /// </summary>
    public static class AutonomousDoorRepair
    {
        /// <summary>How long after a defense ends its defenders still take the repair job.</summary>
        public const long WindowMilliseconds = 15 * 60_000;
        /// <summary>One bot's job ends after this long, done or not (bounded; never blocks a bot for good).</summary>
        public const long JobLimitMilliseconds = 30 * 60_000;
        /// <summary>Repairers on one door at a time.</summary>
        public const int RepairersPerDoor = 2;
        /// <summary>Repairs' worth of wood bought on one trip.</summary>
        public const int RepairsPerPurchase = 6;
        /// <summary>How close the repairer stands (the player's rule is the interact distance).</summary>
        public static int Reach => WorldMgr.INTERACT_DISTANCE;
        /// <summary>The 20-second repair of the player's command.</summary>
        public const long RepairMilliseconds = RepairCommandHandler.repairDuration * 1000L;

        /// <summary>
        /// Woodworking skill a bot repairs with when it never trained woodworking. Under the player's rule a player
        /// without woodworking cannot repair at all; few gamebots train it, so the owner's "bots repair the way players
        /// did" would otherwise rarely happen. 100 gives a third on a level-5 door. Owner check listed on #525.
        /// </summary>
        public const int UntrainedRepairSkill = 100;

        public static int RepairSkill(int trainedWoodworking) => Math.Max(trainedWoodworking, UntrainedRepairSkill);

        /// <summary>Wood units in a set of stacks, by the player's rule (name and wood value).</summary>
        public static int WoodUnits(IEnumerable<(string Name, int Count)> stacks) =>
            stacks.Where(s => RepairCommandHandler.IsWood(s.Name))
                .Sum(s => s.Count * RepairCommandHandler.GetWoodValue(s.Name.ToLowerInvariant()));

        /// <summary>Why a repair cannot go on now, or null. Player rules first, then the self-repair's enemy rule.</summary>
        public static string Blocked(bool alive, bool botInCombat, bool botMoving, bool doorInCombat, bool enemiesNear,
            int doorHealthPercent, bool withinReach, int woodUnits, int woodNeeded)
        {
            if (!alive) return "dead";
            if (doorHealthPercent >= 100) return "full";
            if (botInCombat) return "in_combat";
            if (doorInCombat) return "door_under_attack";
            if (enemiesNear) return "enemies_near";
            if (woodUnits < woodNeeded) return "no_wood";
            if (!withinReach) return "too_far";
            if (botMoving) return "moving";
            return null;
        }

        /// <summary>A purchase of wood: whole packs, enough for <paramref name="unitsWanted"/> where coin and one stack
        /// allow; none if not even one repair's worth (<paramref name="unitsNeeded"/>) is affordable.</summary>
        public static (int Packs, int Boards, long Cost) PlanPurchase(int unitsWanted, int unitsNeeded, int woodValue, int packSize,
            long packPrice, int maxStack, long money)
        {
            if (woodValue <= 0 || packPrice <= 0 || unitsNeeded <= 0) return (0, 0, 0);
            packSize = Math.Max(1, packSize);
            int boardsWanted = (int)Math.Ceiling(Math.Max(unitsWanted, unitsNeeded) / (double)woodValue);
            int boardsNeeded = (int)Math.Ceiling(unitsNeeded / (double)woodValue);
            int packs = (int)Math.Ceiling(boardsWanted / (double)packSize);
            if (maxStack > 0) packs = Math.Min(packs, Math.Max(1, maxStack / packSize));
            packs = (int)Math.Min(packs, money / packPrice);
            if (packs * packSize < boardsNeeded) return (0, 0, 0);
            return (packs, packs * packSize, packs * packPrice);
        }

        // --- door claims ---
        private sealed record Claim(WeakReference<GameBot> Bot, long Until);
        private static readonly object Gate = new();
        private static readonly Dictionary<(ushort Region, int Door), List<Claim>> Claims = new();

        /// <summary>Claims a place on a door (refreshes a place the bot already holds).</summary>
        public static bool TryClaim(ushort region, int doorId, GameBot bot, long now)
        {
            lock (Gate)
            {
                var key = (region, doorId);
                if (!Claims.TryGetValue(key, out var list)) Claims[key] = list = new List<Claim>();
                list.RemoveAll(c => c.Until < now || !c.Bot.TryGetTarget(out GameBot b) || b == null || !b.IsAlive && b != bot);
                int mine = list.FindIndex(c => c.Bot.TryGetTarget(out GameBot b) && b == bot);
                if (mine >= 0) { list[mine] = list[mine] with { Until = now + 60_000 }; return true; }
                if (list.Count >= RepairersPerDoor) return false;
                list.Add(new Claim(new WeakReference<GameBot>(bot), now + 60_000));
                return true;
            }
        }

        public static void Release(GameBot bot)
        {
            lock (Gate)
                foreach (var list in Claims.Values)
                    list.RemoveAll(c => !c.Bot.TryGetTarget(out GameBot b) || b == bot);
        }

        public static void ResetForTests() { lock (Gate) Claims.Clear(); }
    }
}
