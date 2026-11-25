using System;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Server-side line of sight from the navmesh, for checks no game client answers (1.2.3, DOL sweep X1). The client checks
    /// a spell or arrow only when a real player is near enough to be asked; away from the player, monsters, guards, pets and
    /// gamebots cast and shot through dungeon walls, keep walls and hills unchecked. The navmesh raycast
    /// (<see cref="IPathfindingMgr.HasLineOfSight"/>) already keeps monsters from noticing gamebots through walls
    /// (StandardMobBrain.CheckNpcAggro) and #521's bot-to-bot casts (<see cref="BotCastSight"/>); this is the one place that
    /// asks it for casts and shots.
    /// <para>The raycast runs along the walkable surface, so it can't see across a drop: a guard on a wall, a caster on a
    /// ledge or a bridge has no walkable line to someone below it, though it can see them. Outside #521's bot-to-bot rule a
    /// check is made only when both stand on about the same floor (<see cref="SameFloorHeight"/>), and it fails open
    /// everywhere else: no navmesh, another zone or region, a bigger height difference. A wall archer and a lord keep their
    /// shots; nothing a monster could do before is taken away except shooting through a wall on its own floor.</para>
    /// </summary>
    public static class NavmeshSight
    {
        /// <summary>The largest height difference at which two livings count as on one floor (a little over a body height).</summary>
        public const int SameFloorHeight = 128;

        /// <summary>Whether the raycast may judge sight between these two heights (pure).</summary>
        public static bool OnOneFloor(float sourceZ, float targetZ) => Math.Abs(sourceZ - targetZ) <= SameFloorHeight;

        /// <summary>
        /// True only when the navmesh shows a wall between <paramref name="source"/> and <paramref name="target"/>. With
        /// <paramref name="anyHeight"/> the height gate is off (#521's rule for a gamebot's cast at another bot).
        /// </summary>
        public static bool Blocked(GameObject source, GameObject target, bool anyHeight = false)
        {
            if (source == null || target == null || source == target || target.CurrentRegionID != source.CurrentRegionID)
                return false;
            Zone zone = source.CurrentZone;
            if (zone == null || target.CurrentZone != zone)
                return false;
            if (!anyHeight && !OnOneFloor(source.Z, target.Z))
                return false;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return false;
            return !nav.HasLineOfSight(zone, new Vector3(source.X, source.Y, source.Z), new Vector3(target.X, target.Y, target.Z),
                nav.DefaultFilters);
        }
    }
}
