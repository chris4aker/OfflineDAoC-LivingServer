using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

public static class AutonomousRvrDefense
{
    public static bool IsCommittedDefender(GameBot bot) => bot?.IsAutonomousWorldBot == true &&
        AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) &&
        bot.TempProperties.GetProperty<int>("RvrWarbandIntent", -1) == (int)AutonomousRvrEventLayer.Intent.DefendEvent;

    public static bool IsCombatant(GameLiving target) => target is GamePlayer or GameBot ||
        target is GameNPC && AutonomousBotRealmPointRewards.ResolveRootRewardOwner(target) is GamePlayer or GameBot;

    public static bool IsCommittedSiegeFighter(GameBot bot) => bot?.IsAutonomousWorldBot == true &&
        AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) &&
        (AutonomousRvrEventLayer.Intent)bot.TempProperties.GetProperty<int>("RvrWarbandIntent", -1) is
            AutonomousRvrEventLayer.Intent.DefendEvent or AutonomousRvrEventLayer.Intent.AssaultKeep or
            AutonomousRvrEventLayer.Intent.AssaultRelicKeep;

    public static bool ObjectiveExposed(AbstractGameKeep keep) => keep.Doors.Values.Any(d => d.IsAttackableDoor) &&
        !keep.Doors.Values.Any(d => d.IsAttackableDoor && d.IsAlive && d.State == eDoorState.Closed);

    public static bool IsRangedDefender(GameBot bot) => bot?.CharacterClass != null &&
        (bot.CharacterClass.ClassType == eClassType.ListCaster ||
         (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Hunter or eCharacterClass.Ranger or eCharacterClass.Scout or eCharacterClass.Animist);

    public static bool HoldWall(GameBot bot, GameLiving target)
    {
        if (!bot.IsAutonomousWorldBot || !IsRangedDefender(bot) || target == null ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) ||
            bot.IsWithinRadius(target, bot.MeleeAttackRange + 100)) return false;
        return GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID).Any(keep =>
            keep.Realm == bot.Realm && bot.GetDistanceTo(new Point3D(keep.X, keep.Y, keep.Z)) < 1800 &&
            keep.Doors.Values.Any(door => door.IsAttackableDoor) &&
            keep.Doors.Values.Where(door => door.IsAttackableDoor).All(door => door.IsAlive && door.State == eDoorState.Closed) &&
            bot.Z > keep.Doors.Values.Min(door => door.Z) + 100);
    }

    public static Vector3 Position(GameBot bot, AbstractGameKeep keep)
    {
        var nav = AutonomousKeepApproachNavigation.ForRealm(PathfindingProvider.Instance,bot.CurrentRegion,bot.Realm);
        var guards = keep.Guards.Values.Where(guard => guard.IsAlive && guard.Realm == bot.Realm)
            .OrderBy(guard => IsRangedDefender(bot) ? guard is GuardArcher ? 0 : 1 : guard is GuardLord ? 0 : 1)
            .ThenBy(guard => (unchecked((ulong)(guard.ObjectID + bot.DatabaseID)) * 2654435761UL) % 100);
        foreach (var guard in guards.Take(12))
        {
            Vector3 raw = new(guard.X, guard.Y, guard.Z);
            if (AutonomousNavigationSurface.TryFloor(nav, guard.CurrentZone, raw, out Vector3 floor) &&
                Math.Abs(floor.Z - raw.Z) < 96 &&
                AutonomousRvrRally.HasRoute(bot.CurrentRegion,nav,bot.Realm,new(bot.X,bot.Y,bot.Z),floor)) return floor;
        }
        // Remain at a real friendly doorway if no validated interior position
        // exists. Never invent a wall-top point or teleport onto a rampart.
        var door = keep.Doors.Values.OrderBy(bot.GetDistanceTo).FirstOrDefault(d=>
            AutonomousRvrRally.HasRoute(bot.CurrentRegion,nav,bot.Realm,new(bot.X,bot.Y,bot.Z),new(d.X,d.Y,d.Z)));
        return door == null ? new(bot.X, bot.Y, bot.Z) : new(door.X, door.Y, door.Z);
    }

    /// <summary>
    /// Bots as people 1.2, work package 5 (keep walls, owner decision 11): the floors a waiting bot may sit on at this keep.
    /// The positions <see cref="Position"/> proves, without its route check (the caller proves the one it picks): each
    /// live guard of the keep's realm floored on the mesh (TryFloor, within 96 units of its height), marked archer or not,
    /// with its rise above the keep's lowest gate. Never an invented wall-top point.
    /// </summary>
    public static List<BotKeepWallSpots.Candidate> WallCandidates(GameBot bot, AbstractGameKeep keep)
    {
        var candidates = new List<BotKeepWallSpots.Candidate>();
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (bot == null || keep == null || nav == null || !nav.IsAvailable)
            return candidates;
        float? gate = null;
        foreach (var door in keep.Doors.Values)
            if (door.IsAttackableDoor)
                gate = gate is float z ? MathF.Min(z, door.Z) : door.Z;
        foreach (var guard in keep.Guards.Values)
        {
            if (guard == null || !guard.IsAlive || guard.Realm != keep.Realm || guard.CurrentZone == null)
                continue;
            Vector3 raw = new(guard.X, guard.Y, guard.Z);
            if (!AutonomousNavigationSurface.TryFloor(nav, guard.CurrentZone, raw, out Vector3 floor) || Math.Abs(floor.Z - raw.Z) >= 96)
                continue;
            candidates.Add(new(floor, guard is GuardArcher, BotKeepWallSpots.Rise(floor.Z, gate, keep.Z)));
        }
        return candidates;
    }
}
