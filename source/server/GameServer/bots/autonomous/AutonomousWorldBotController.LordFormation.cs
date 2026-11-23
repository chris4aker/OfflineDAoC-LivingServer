using System;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The lord fight's formation (1.2.2, <see cref="RvrLordFormation"/>): inside a keep or tower lord's reach a caster, archer
/// or healer walks to its own spot behind the melee on the raid's side before it fights, frontier sieges and battleground
/// storms alike. Melee and tanks are not concerned. Autonomous gamebots only (companions in a player's group follow the
/// player and fight from their own paths).
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    // The lord the formation spot was picked for, where he stood then, and the spot (null: no floor spot, or its walk failed;
    // the bot then fights where it stands, as before).
    private GuardLord _lordFormationLord;
    private Vector2 _lordFormationAnchor;
    private Vector3? _lordFormationSpot;
    private float _lordFormationDistance;

    /// <summary>A floor spot lies at most this far (flat) from the formation's candidate.</summary>
    private const float LordFormationSnap = 48f;

    /// <summary>The bot's place in a lord's fight by its class (<see cref="RvrLordFormation.RoleOf"/>).</summary>
    internal static RvrLordFormation.Role LordFormationRole(GameBot bot) => bot?.CharacterClass == null
        ? RvrLordFormation.Role.Melee
        : RvrLordFormation.RoleOf((eCharacterClass)bot.CharacterClass.ID, bot.CharacterClass.ClassType == eClassType.ListCaster);

    /// <summary>
    /// A standing-back bot inside the lord's reach, on his floor and off its formation spot walks there (not mid-cast,
    /// stunned or mesmerized). True while it walks: the turn is the walk's. False: it fights from where it stands.
    /// </summary>
    private bool TakeLordFormation(GameBot bot, GuardLord lord)
    {
        if (lord == null || !lord.IsAlive || lord.CurrentRegionID != bot.CurrentRegionID) return false;
        RvrLordFormation.Role role = LordFormationRole(bot);
        if (!RvrLordFormation.StandsBack(role)) return false;
        bool keep = lord.Component?.Keep is GameKeep;
        if (!bot.IsWithinRadius(lord, (int)RvrLordReach.For(keep)) ||
            Math.Abs(bot.Z - lord.Z) > AutonomousBattlegroundKeepRally.StrikeFloorHeight)
            return false;
        Vector2 lordAt = new(lord.X, lord.Y);
        if (RvrLordFormation.NeedsSpot(_lordFormationLord != null, _lordFormationLord == lord,
                Vector2.Distance(_lordFormationAnchor, lordAt)))
        {
            _lordFormationLord = lord;
            _lordFormationAnchor = lordAt;
            _lordFormationSpot = PickLordFormationSpot(bot, lord, role, keep, out _lordFormationDistance, out string door);
            if (RvrLogBudget.Take("RVR_LORD_FORMATION", GameLoop.GameLoopTime, 30))
                Log.Info($"RVR_LORD_FORMATION bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                         $"class={bot.CharacterClass?.Name ?? "-"} role={role} dist={(_lordFormationSpot.HasValue ? (int)_lordFormationDistance : -1)} " +
                         $"lord=\"{lord.Name}\" keep=\"{lord.Component?.Keep?.Name ?? "-"}\" kind={(keep ? "keep" : "tower")} " +
                         $"approach={door} spot={(_lordFormationSpot is { } s ? $"{(int)s.X},{(int)s.Y},{(int)s.Z}" : "none")} " +
                         $"from={(int)bot.GetDistanceTo(lord)} region={bot.CurrentRegionID}");
        }
        if (_lordFormationSpot is not Vector3 spot) return false;
        float off = Vector2.Distance(new(bot.X, bot.Y), new(spot.X, spot.Y));
        if (!RvrLordFormation.WalksToSpot(off, bot.IsCasting, bot.IsStunned || bot.IsMezzed)) return false;
        if (bot.IsAttacking) bot.StopAttack();
        if (!IssuePath(bot, spot, preciseArrival: true, retargetWhileMoving: true))
        {
            _lordFormationSpot = null;
            return false;
        }
        SetRvrStatus(bot, "Taking formation on the lord", lord.Name,
            role == RvrLordFormation.Role.Healer ? "Healers stand behind the casters" : "Casters and archers stand behind the melee");
        return true;
    }

    /// <summary>
    /// The bot's floor spot: the first of <see cref="RvrLordFormation.Candidates"/> with navmesh floor on the lord's floor and
    /// a straight floor line to him (nothing between a caster there and him). Null without a navmesh or a spot.
    /// </summary>
    private static Vector3? PickLordFormationSpot(GameBot bot, GuardLord lord, RvrLordFormation.Role role, bool keep,
        out float distance, out string door)
    {
        distance = 0;
        Vector2 lordAt = new(lord.X, lord.Y);
        Vector2? doorAt = LordRoomDoor(lord, out door);
        Vector2 approach = RvrLordFormation.Approach(lordAt, doorAt, new(bot.X, bot.Y));
        var nav = PathfindingProvider.Instance;
        Zone zone = lord.CurrentZone;
        if (zone == null || nav is not { IsAvailable: true } || !nav.HasNavmesh(zone) ||
            nav.GetClosestPoint(zone, new(lord.X, lord.Y, lord.Z), 128, 128, 256, nav.DefaultFilters) is not Vector3 lordFloor)
            return null;
        foreach (var (candidate, d) in RvrLordFormation.Candidates(lordAt, approach, role, keep, bot.DatabaseID))
        {
            if (nav.GetClosestPoint(zone, new(candidate.X, candidate.Y, lord.Z), LordFormationSnap, LordFormationSnap,
                    AutonomousBattlegroundKeepRally.StrikeFloorHeight, nav.DefaultFilters) is not Vector3 at ||
                Math.Abs(at.Z - lord.Z) > AutonomousBattlegroundKeepRally.StrikeFloorHeight ||
                Vector2.Distance(new(at.X, at.Y), candidate) > LordFormationSnap ||
                Vector2.Distance(new(at.X, at.Y), lordAt) > RvrLordReach.For(keep) ||
                !nav.HasLineOfSight(zone, at, lordFloor, nav.DefaultFilters))
                continue;
            distance = d;
            return at;
        }
        return null;
    }

    /// <summary>The way into the lord's keep or tower: its door (not a postern) nearest him on the flat, or null.</summary>
    private static Vector2? LordRoomDoor(GuardLord lord, out string label)
    {
        label = "bot";
        AbstractGameKeep keep = lord.Component?.Keep;
        if (keep?.Doors == null) return null;
        GameKeepDoor door = keep.Doors.Values
            .Where(d => d != null && !PosternLike(keep, d))
            .OrderBy(d => Vector2.DistanceSquared(new(d.X, d.Y), new(lord.X, lord.Y)))
            .FirstOrDefault();
        if (door == null) return null;
        label = $"door{SiegeDoorNumber(door)}";
        return new Vector2(door.X, door.Y);
    }
}
