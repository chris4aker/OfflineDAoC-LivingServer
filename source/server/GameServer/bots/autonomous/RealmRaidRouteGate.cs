using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Living epic bosses on a realm raid's way in, and epic respawns while a raid is in its dungeon (Living Server 1.2, build 3).
/// <para>
/// Owner's PC, 2026-11-14 (build e3b8238): Albion's Summoner's Hall raid goes from Snowdonia Station through Hall of the Corrupt,
/// whose way into Summoner's Hall is past Aidon the Archwizard's room. Aidon (level 75, aggro 600) had come back. The parties'
/// walk logged AUTONOMOUS_DUNGEON_EPIC_AVOIDED action=pass at his spot (there is no way round), the 64 raiders walked into him
/// with no muster, and 182 died in 50 minutes to him and his illusions in a trickle (each released raider walked back alone and
/// died to him again), while the Summoner's Hall side waited "for formed parties inside" for 40 minutes. Nothing counted it as
/// a wipe or an attempt, because Aidon was not the raid's target.
/// </para>
/// <para>
/// Now, when the walk from the way into the dungeon on the route to the way out of it towards the raid's own dungeon passes
/// within a living boss's aggro range (<see cref="Margin"/> included), that boss is a route objective, a "gate": the raid
/// fights it there as it fights any named encounter (a muster outside its aggro, its encounter tactic from the catalog, its
/// attempts and wipes counted, a second wipe defeats the raid) and goes on once it is dead. This covers every raid that walks
/// through another dungeon: Summoner's Hall from Hall of the Corrupt, Dodens Gruva or Marfach Caverns, and Crystal Cave from
/// Avalon City. Which bosses stand on a walk is found on the navigation mesh when the raid runs.
/// </para>
/// <para>
/// Respawns: the epic encounters' 60-minute respawn (set_si_epic_encounter_respawninterval /
/// set_epic_game_encounter_respawninterval) is shorter than a raid (Galladoria, same night: Easmarach killed 00:56:58 was back
/// at 01:57:59 while the raid fought in the last room, and his current blocked it). While a realm raid is inside a dungeon (or
/// the dungeon its way in leads through), that dungeon's epic monsters with a long respawn do not come back; their timers
/// look again every minute and they respawn once the raid has ended. Chosen over "a respawned boss becomes a route objective":
/// a dungeon the raid has cleared stays cleared, a respawn can no longer cost the raid an attempt or cut it off from its
/// final, and the gate rule above still covers a boss that was alive before the raid came.
/// </para>
/// </summary>
public static class RealmRaidRouteGate
{
    [ServerProperty("autonomous", "realm_raid_route_gates",
        "Raids that walk through another dungeon on their way in (Summoner's Hall, Crystal Cave): a living boss whose aggro covers the walk is fought there as a route objective, with a muster, its tactic and counted attempts. False = the raid walks past it, as before 1.2 build 3.", true)]
    public static bool On = true;

    [ServerProperty("autonomous", "realm_raid_hold_epic_respawn",
        "While a realm raid is inside a dungeon (or the dungeon its way in leads through), that dungeon's epic monsters with a respawn of ten minutes or more do not respawn; they come back once the raid has ended. False = their own timers, as before 1.2 build 3.", true)]
    public static bool HoldEpicRespawn = true;

    /// <summary>Added to a boss's aggro range when its distance to the walk is judged.</summary>
    public const float Margin = 200f;
    /// <summary>A boss more than this above or below the walk is on another floor.</summary>
    public const float FloorBand = 500f;
    /// <summary>How often a raid looks for a gate on its way.</summary>
    public const long CheckMilliseconds = 60_000;
    /// <summary>A gate the route could not reach (blocked) is looked at again after this long.</summary>
    public const long SetAsideMilliseconds = 15 * 60_000;
    /// <summary>A held respawn looks again this often.</summary>
    public const int RespawnRecheckMilliseconds = 60_000;
    /// <summary>Only respawns at least this long are held (encounter mechanics that respawn adds quickly are left alone).</summary>
    public const int HeldRespawnMinimumMilliseconds = 10 * 60_000;

    /// <summary>Whether a boss at <paramref name="boss"/> with <paramref name="aggroRange"/> covers the walk (pure).</summary>
    public static bool Blocks(IReadOnlyList<Vector3> walk, Vector3 boss, int aggroRange)
    {
        if (walk == null || walk.Count == 0 || aggroRange <= 0) return false;
        float reach = aggroRange + Margin;
        for (int i = 0; i < walk.Count; i++)
        {
            Vector3 a = walk[i], b = i + 1 < walk.Count ? walk[i + 1] : walk[i];
            Vector2 a2 = new(a.X, a.Y), ab = new(b.X - a.X, b.Y - a.Y), p = new(boss.X, boss.Y);
            float length = ab.LengthSquared();
            float t = length < 0.01f ? 0 : Math.Clamp(Vector2.Dot(p - a2, ab) / length, 0, 1);
            Vector2 nearest = a2 + ab * t;
            float z = a.Z + (b.Z - a.Z) * t;
            if (Math.Abs(z - boss.Z) <= FloorBand && Vector2.DistanceSquared(nearest, p) <= reach * reach) return true;
        }
        return false;
    }

    /// <summary>
    /// A living hostile epic monster (a boss, or epic trash such as Avalon City's xanxicarian sentries) whose aggro covers the
    /// walk (<see cref="Blocks"/>), at its spawn or where it stands. A boss gates the walk; epic trash gates it only when
    /// the mesh has no way round it (RealmRaidDungeonRoute.TryCreateGate).
    /// <para>
    /// Owner's PC, build b1b6846, 2026-11-18 (Crystal Cave, Albion; W-1): the raid's way in through Avalon City passes the
    /// xanxicarian sentries (levels 70-77, aggro 200). Only a GameEpicBoss was a gate, so the sentries were walked past
    /// (AUTONOMOUS_DUNGEON_EPIC_AVOIDED action=pass, 190 times): 37 raiders died to them during the raid and 57 after it. Now
    /// the raid clears the ones on its walk as a gate, and the epic respawn hold keeps them down for the walk out. Epic
    /// monsters off the walk are left alone; the sentries themselves are unchanged.
    /// </para>
    /// </summary>
    public static bool BlocksWalk(GameNPC npc, IReadOnlyList<Vector3> walk)
    {
        if (npc is not IGameEpicNpc || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active || npc.Realm != eRealm.None ||
            (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ||
            npc.Brain is not DOL.AI.Brain.StandardMobBrain { AggroRange: > 0 } brain)
            return false;
        return Blocks(walk, new(npc.SpawnPoint.X, npc.SpawnPoint.Y, npc.SpawnPoint.Z), brain.AggroRange) ||
               Blocks(walk, new(npc.X, npc.Y, npc.Z), brain.AggroRange);
    }

    /// <summary>
    /// The part of <paramref name="walk"/> from <paramref name="back"/> units before its point nearest <paramref name="at"/>
    /// to its end (pure): the corridor a party's detour search starts from, as when it meets the monster on its way.
    /// </summary>
    public static Vector3[] CorridorBefore(IReadOnlyList<Vector3> walk, Vector3 at, float back)
    {
        if (walk == null || walk.Count == 0) return [];
        // The nearest point on the walk (by segment) and the walked distance to it.
        int bestSegment = 0;
        float bestT = 0, best = float.MaxValue;
        for (int i = 0; i + 1 < walk.Count; i++)
        {
            Vector3 a = walk[i], b = walk[i + 1], ab = b - a;
            float length = ab.LengthSquared();
            float t = length < 0.01f ? 0 : Math.Clamp(Vector3.Dot(at - a, ab) / length, 0, 1);
            float d = Vector3.DistanceSquared(a + ab * t, at);
            if (d < best) { best = d; bestSegment = i; bestT = t; }
        }
        if (walk.Count == 1) return [walk[0]];
        // Walk back from the nearest point by <back>.
        Vector3 point = Vector3.Lerp(walk[bestSegment], walk[bestSegment + 1], bestT);
        float left = back;
        int segment = bestSegment;
        Vector3 start = point;
        while (true)
        {
            Vector3 a = walk[segment];
            float length = Vector3.Distance(a, start);
            if (length >= left)
            {
                start = length < 0.01f ? a : start + (a - start) / length * left;
                break;
            }
            left -= length;
            start = a;
            if (segment == 0) break;
            segment--;
        }
        var corridor = new List<Vector3> { start };
        for (int i = segment + 1; i < walk.Count; i++)
            if (Vector3.DistanceSquared(walk[i], corridor[^1]) > 0.01f) corridor.Add(walk[i]);
        return corridor.ToArray();
    }

    /// <summary>A gate is looked for only while the raid has not mostly gone past it into its own dungeon (pure): a boss
    /// found alive behind a raid that is already through is left to the raid's return waves.</summary>
    public static bool RaidStillBeforeIt(int livingInRaidDungeon, int livingRaid) => livingInRaidDungeon * 2 < livingRaid;

    /// <summary>Whether a dead monster's respawn is held now (pure).</summary>
    public static bool HoldsRespawn(bool on, bool epic, bool ownedByNoRealm, int respawnInterval, bool raidInItsDungeon) =>
        on && epic && ownedByNoRealm && respawnInterval >= HeldRespawnMinimumMilliseconds && raidInItsDungeon;
}
