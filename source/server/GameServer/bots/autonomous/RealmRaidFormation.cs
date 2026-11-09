using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>Party-sized posts; existing eight-person formations fill each post.</summary>
    public static class RealmRaidFormation
    {
        public const float SharedPostRadius = 600;

        public static bool TryResolve(IPathfindingMgr nav, Zone zone, Vector3 center, int slot,
            IReadOnlyCollection<Vector3> occupied, out Vector3 position)
        {
            position = default;
            if (slot < 0 || slot >= RealmRaidRecruitmentPolicy.FormationSlots || nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            bool epicInterior = zone.ID is 60 or 160 or 191 or 248 or 249 || AutonomousRealmRaid.IsRaidDungeon(zone.ID);
            // Never a post inside a ground hazard (Orshom's Fire: PC 2026-11-09, the front 200 from it put parties in its 300).
            ushort region = zone.ZoneRegion?.ID ?? 0;
            bool Valid(Vector3 floor) => EpicEncounterTactics.HazardAt(region, floor) == null &&
                nav.HasLineOfSight(zone, center, floor, nav.DefaultFilters) &&
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, center, floor) &&
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor, center);
            for (int attempt = 0; attempt < (epicInterior ? 22 : 6); attempt++)
            {
                bool compact = attempt >= 6;
                double angle = (slot * 137.5 + attempt * (compact ? 47.5 : 30)) * Math.PI / 180;
                float radius = compact ? 100 + (attempt - 6 + slot) % 5 * 80 :
                    slot == 0 && attempt == 0 ? 0 : 140 * MathF.Sqrt(slot + 1) + attempt % 2 * 70;
                Vector3 raw = center + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
                Vector3? floor = nav.GetClosestPoint(zone, raw, 48, 48, 128, nav.DefaultFilters);
                if (!floor.HasValue || Math.Abs(floor.Value.Z - center.Z) > 128) continue;
                bool overlaps = false;
                foreach (Vector3 other in occupied)
                    if (Vector3.DistanceSquared(other, floor.Value) < (compact ? 80 * 80 : 120 * 120)) { overlaps = true; break; }
                if (overlaps) continue;
                if (!Valid(floor.Value)) continue;
                position = floor.Value;
                return true;
            }
            // A cramped spot (Tuscaran Glacier's griffon ledge, 2026-10-12: one post for 38 parties) used
            // to leave every other party at its previous post, so nobody reached the target, it was
            // skipped at 100% and the raid then waited eight hours. Such a party shares an occupied
            // post that passes the same sight and corridor checks: crowded, but at the fight. Still no
            // raw offset or teleport through a wall.
            foreach (Vector3 other in occupied.Where(o => Vector3.DistanceSquared(o, center) <= SharedPostRadius * SharedPostRadius)
                         .OrderBy(o => Vector3.DistanceSquared(o, center)))
            {
                if (Math.Abs(other.Z - center.Z) > 128 || !Valid(other)) continue;
                position = other;
                return true;
            }
            return false;
        }
    }
}
