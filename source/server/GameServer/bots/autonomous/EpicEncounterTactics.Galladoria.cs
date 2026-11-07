using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public static partial class EpicEncounterTactics
    {
        /// <summary>The Corrupt Primals of Galladoria's last room (the Fire primal's melee go to these while one lives).</summary>
        public static readonly string[] GalladoriaPrimals = ["WaterPrimal", "EarthPrimal", "AirPrimal", "FirePrimal"];

        /// <summary>
        /// The Fire primal walks its loop through the room without stopping and drops a trail of fire where it stands every
        /// 2-3 s; each patch burns everything within its radius every 2 s for 6 s (with AllowFullFirePatches, the original
        /// 220 in 450; else 120 in 300). Anyone in melee reach stands in the newest patch, so the hazard rule would send melee
        /// out and back for ever. Melee fight another living primal (one they can reach: the Air primal flies) and leave
        /// the Fire primal to casters and archers, who step out of the patches (its hazard rule). When it is the last one
        /// melee fight it and take the fire, and the healers keep them up.
        /// </summary>
        private static Advice? DecideGalladoria(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live, bool canShootFlyers, int classId)
        {
            if (target.GetType().Name != "FirePrimal" || AtRange(canShootFlyers, classId)) return null;
            GameNPC other = live.Where(n => Present(n) && n != target && n.CurrentRegionID == target.CurrentRegionID &&
                                            n.GetType().Name is "WaterPrimal" or "EarthPrimal" && target.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(n => raider.GetDistanceTo(n)).FirstOrDefault();
            if (other != null)
                return new(BotAction.Retarget, other, null, $"fire trail: melee take \"{other.Name}\", casters and archers the Fire primal");
            // The last primal: melee fight it in the fire (no step-out loop).
            return new(BotAction.None, target, null, "fire trail: the last primal, melee fight it in the fire");
        }
    }
}
