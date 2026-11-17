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

        /// <summary>The raw post (before the floor snap) of a party's <paramref name="slot"/> around <paramref name="center"/> on
        /// its <paramref name="attempt"/>-th try: six spread tries (140 x the root of slot + 1 out, 70 more on odd tries), then
        /// compact ones (100-420 out).</summary>
        public static Vector3 RawPost(Vector3 center, int slot, int attempt)
        {
            bool compact = attempt >= 6;
            double angle = (slot * 137.5 + attempt * (compact ? 47.5 : 30)) * Math.PI / 180;
            float radius = compact ? 100 + (attempt - 6 + slot) % 5 * 80 :
                slot == 0 && attempt == 0 ? 0 : 140 * MathF.Sqrt(slot + 1) + attempt % 2 * 70;
            return center + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
        }

        /// <summary><paramref name="dangers"/>: spots no post may stand in (RealmRaidDungeonRoute.PostDangers while the raid
        /// holds: a resting boss's aggro + 300; owner's PC, build 453e01a, 2026-11-17, a post 546 from Icelord Agmundr's spawn
        /// around a muster 907 from it pulled him). Null or empty: none.</summary>
        public static bool TryResolve(IPathfindingMgr nav, Zone zone, Vector3 center, int slot,
            IReadOnlyCollection<Vector3> occupied, out Vector3 position,
            IReadOnlyList<RealmRaidDungeonRoute.BossDanger> dangers = null) =>
            TryResolve(nav, zone, center, slot, occupied, out position, out _, dangers);

        /// <summary>As above; <paramref name="dangerOnly"/>: no post was found, but one would have been without
        /// <paramref name="dangers"/> (found in the same search: no second one, re-review of 06b9cb2c0).</summary>
        public static bool TryResolve(IPathfindingMgr nav, Zone zone, Vector3 center, int slot,
            IReadOnlyCollection<Vector3> occupied, out Vector3 position, out bool dangerOnly,
            IReadOnlyList<RealmRaidDungeonRoute.BossDanger> dangers)
        {
            position = default;
            dangerOnly = false;
            if (slot < 0 || slot >= RealmRaidRecruitmentPolicy.FormationSlots || nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            bool epicInterior = zone.ID is 60 or 160 or 191 or 248 or 249 || AutonomousRealmRaid.IsRaidDungeon(zone.ID);
            // Never a post inside a ground hazard (Orshom's Fire: PC 2026-11-09, the front 200 from it put parties in its 300).
            ushort region = zone.ZoneRegion?.ID ?? 0;
            bool Valid(Vector3 floor) => EpicEncounterTactics.HazardAt(region, floor) == null &&
                nav.HasLineOfSight(zone, center, floor, nav.DefaultFilters) &&
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, center, floor) &&
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor, center);
            Vector3? Floor(Vector3 raw) => nav.GetClosestPoint(zone, raw, 48, 48, 128, nav.DefaultFilters);
            return Resolve(epicInterior ? 22 : 6, center, slot, occupied, Floor, Valid,
                dangers is { Count: > 0 } ? p => RealmRaidDungeonRoute.InBossDangerStrict(p, dangers) : null,
                out position, out dangerOnly);
        }

        /// <summary>
        /// The post search (pure but for <paramref name="floorOf"/> and <paramref name="valid"/>, the navmesh's floor snap and its
        /// sight and corridor checks): the first of <paramref name="attempts"/> tries (<see cref="RawPost"/>) that snaps to the
        /// floor of the centre, overlaps no occupied post and is <paramref name="valid"/>, else the nearest occupied post within
        /// <see cref="SharedPostRadius"/> that is valid. A candidate <paramref name="inDanger"/> is skipped; until one of those is
        /// valid, each is checked as before (so the search costs at most the one pass it cost before) to tell
        /// <paramref name="dangerOnly"/>: a post would have been found but for the dangers.
        /// </summary>
        public static bool Resolve(int attempts, Vector3 center, int slot, IReadOnlyCollection<Vector3> occupied,
            Func<Vector3, Vector3?> floorOf, Func<Vector3, bool> valid, Func<Vector3, bool> inDanger,
            out Vector3 position, out bool dangerOnly)
        {
            position = default;
            dangerOnly = false;
            bool blockedByDanger = false;
            bool Take(Vector3 p)
            {
                if (inDanger != null && inDanger(p))
                {
                    // Checked as integration/1.2's search checked it, until one would have done: at most one pass in all.
                    if (!blockedByDanger) blockedByDanger = valid(p);
                    return false;
                }
                return valid(p);
            }
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                bool compact = attempt >= 6;
                Vector3 raw = RawPost(center, slot, attempt);
                Vector3? floor = floorOf(raw);
                if (!floor.HasValue || Math.Abs(floor.Value.Z - center.Z) > 128) continue;
                bool overlaps = false;
                foreach (Vector3 other in occupied)
                    if (Vector3.DistanceSquared(other, floor.Value) < (compact ? 80 * 80 : 120 * 120)) { overlaps = true; break; }
                if (overlaps) continue;
                if (!Take(floor.Value)) continue;
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
                if (Math.Abs(other.Z - center.Z) > 128 || !Take(other)) continue;
                position = other;
                return true;
            }
            dangerOnly = blockedByDanger;
            return false;
        }
    }
}
