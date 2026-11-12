using System.Collections.Generic;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// A boss's linked pack (1.2, epic wave 3): the NPCs with its PackageID within a radius of it join its fight (each gets the
    /// boss's aggro list). Qan'ris Duros and Durval (Shrouded Isles) passed their radius, 3,500, to WorldMgr.GetNPCsFromRegion
    /// as if it were a region id (OpenDAoC-Core 3513bad has the same slip): region 3500 does not exist, so the loop saw no
    /// NPC and the pack link never fired (the guards joined only by standing close and sharing a faction). The link now
    /// looks 3,500 around the boss, which is what the call meant.
    /// </summary>
    public static class EncounterPacks
    {
        /// <summary>The live NPCs of <paramref name="packageId"/> among <paramref name="candidates"/> (not the boss itself)
        /// get <paramref name="brain"/>'s aggro list; how many were linked.</summary>
        public static int Link(StandardMobBrain brain, string packageId, IEnumerable<GameNPC> candidates)
        {
            if (brain?.Body == null || string.IsNullOrEmpty(packageId) || candidates == null) return 0;
            int linked = 0;
            foreach (GameNPC npc in candidates)
            {
                if (npc == null || npc == brain.Body || !npc.IsAlive || npc.PackageID != packageId) continue;
                if (npc.Brain is not StandardMobBrain other) continue;
                brain.AddAggroListTo(other);
                linked++;
            }
            return linked;
        }

        /// <summary>The boss's pack within <paramref name="radius"/> of it joins its fight; how many were linked.</summary>
        public static int LinkInRadius(StandardMobBrain brain, string packageId, ushort radius) =>
            brain?.Body == null ? 0 : Link(brain, packageId, brain.Body.GetNPCsInRadius(radius));
    }
}
