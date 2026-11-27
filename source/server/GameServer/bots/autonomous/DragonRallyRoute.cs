using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

// Expedition staging only. This is not a global navigation/aggro change.
public static class DragonRallyRoute
{
    public const int Clearance = 2200; // 800 native aggro + formation/pet/travel margin.
    public const int ApproachRadius = 14000;

    public static float SegmentDistanceSquared(Vector3 a, Vector3 b, Vector3 center)
    {
        Vector2 start = new(a.X,a.Y), end = new(b.X,b.Y), c = new(center.X,center.Y);
        Vector2 delta = end-start;
        float t = delta.LengthSquared() < 0.01f ? 0 : Math.Clamp(Vector2.Dot(c-start,delta)/delta.LengthSquared(),0,1);
        return Vector2.DistanceSquared(start+delta*t,c);
    }

    public static bool Outside(Vector3 point, Vector3 center) =>
        Vector2.DistanceSquared(new(point.X,point.Y),new(center.X,center.Y)) >= Clearance*Clearance;

    public static bool TryBuild(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 goal, Vector3 home, out Vector3[] route) =>
        TryBuild(nav, zone, start, goal, home, out route, out _);

    /// <summary>
    /// The same search; on failure <paramref name="stage"/> says how far it got (issue #767: 2,923 retries said only
    /// queries=133): unsafe-goal (the post is inside the margin), no-navmesh, ring (no ring point has floor outside the
    /// margin), first-leg (no ring point is reachable from the start) or goal-leg (ring points were walked, none
    /// reaches the goal). Empty on success.
    /// </summary>
    public static bool TryBuild(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 goal, Vector3 home, out Vector3[] route, out string stage)
    {
        route = [];
        stage = "";
        if (nav == null || zone == null || !nav.IsAvailable) { stage = "no-navmesh"; return false; }
        if (!Outside(goal,home)) { stage = "unsafe-goal"; return false; }
        bool Leg(Vector3 a, Vector3 b)
        {
            return nav is RvrPlanningNavigation work
                ? work.CompleteCorridor(zone,a,b,()=>CalculateLeg(a,b)) : CalculateLeg(a,b);
        }
        bool CalculateLeg(Vector3 a, Vector3 b)
        {
            if (!Outside(b,home)) return false;
            var nodes = new WrappedPathfindingNode[512];
            var result = nav.GetPathStraight(zone,a,b,nav.DefaultFilters,nodes);
            if (result.Status != PathfindingStatus.PathFound || result.NodeCount <= 0 ||
                Vector3.DistanceSquared(nodes[result.NodeCount-1].Position,b) > 80*80) return false;
            Vector3 previous = a;
            for (int i=0;i<result.NodeCount;i++)
            {
                if (!SafeSegment(previous,nodes[i].Position,home)) return false;
                previous=nodes[i].Position;
            }
            return SafeSegment(previous,b,home);
        }
        if (Leg(start,goal)) { route=[goal]; return true; }
        // Waypoints, not just endpoints, must go around the encounter. Reusing
        // a bare "walkable" path here used to cut through the sleeping dragon.
        const int count=24;
        double angle=Math.Atan2(start.Y-home.Y,start.X-home.X);
        int nearest=((int)Math.Round(angle/(2*Math.PI/count))+count)%count;
        bool anyRing=false, anyFirstLeg=false;
        foreach(int radius in new[]{3600,4800,6000})
        {
            var ring=new Vector3?[count];
            for(int i=0;i<count;i++)
            {
                double theta=i*2*Math.PI/count;
                Vector3 raw=home+new Vector3((float)Math.Cos(theta)*radius,(float)Math.Sin(theta)*radius,0);
                var floor=nav.GetClosestPoint(zone,raw,96,96,4096,nav.DefaultFilters);
                if(floor.HasValue && Outside(floor.Value,home)) { ring[i]=floor; anyRing=true; }
            }
            foreach(int offset in new[]{0,1,-1,2,-2,3,-3,4,-4,6,-6,12})
            foreach(int direction in new[]{1,-1})
            {
                var steps=new List<Vector3>();
                Vector3 previous=start;
                for(int step=0;step<count;step++)
                {
                    int index=(nearest+offset+direction*step+count*2)%count;
                    if(!ring[index].HasValue || !Leg(previous,ring[index].Value)) break;
                    if(step==0) anyFirstLeg=true;
                    previous=ring[index].Value; steps.Add(previous);
                    if(Leg(previous,goal)) { steps.Add(goal); route=steps.ToArray(); return true; }
                }
            }
        }
        stage = !anyRing ? "ring" : !anyFirstLeg ? "first-leg" : "goal-leg";
        return false;
    }

    // A bot displaced inside the margin by genuine combat can still leave it.
    // Each segment must move outward, never closer to the encounter.
    public static bool SafeSegment(Vector3 a, Vector3 b, Vector3 home)
    {
        if (Outside(a,home)) return SegmentDistanceSquared(a,b,home) >= Clearance*Clearance;
        Vector2 radial=new(a.X-home.X,a.Y-home.Y), delta=new(b.X-a.X,b.Y-a.Y);
        return Vector2.Dot(radial,delta)>=0;
    }

    public static bool AtAssembly(Vector3 position, IEnumerable<Vector3> posts, Vector3? dragonHome = null) =>
        (!dragonHome.HasValue || Outside(position,dragonHome.Value)) &&
        posts.Any(post => Vector3.DistanceSquared(position,post) <= 600*600);
}

public enum DragonRallyFallbackAction
{
    /// <summary>Plan the safe route again in 30 seconds (towards <see cref="DragonRallyFallback.GoalForAttempt"/>).</summary>
    Retry,
    /// <summary>Walk to the post by ordinary expedition travel for a while, then plan again from wherever the bot is.</summary>
    OrdinaryTravel,
    /// <summary>Keep retrying the safe route: the boss would aggro on an ordinary path. The watchdog's rally bound catches
    /// a bot that never gets out (AutonomousStuckWatchdog.RallyStuckThreshold).</summary>
    KeepSafeRoute,
}

/// <summary>
/// What a raider does when its safe rally route around the boss fails (issue #767). Owner's PC, rc.2, 2026-11-27: 75
/// Glacier Giant raiders retried the same route from the same spots every 30 seconds for about 20 minutes (2,923
/// DRAGON_RALLY_ROUTE_RETRY rows) until the raid ended as a failed rally.
/// - The 2nd and 3rd tries aim at the raid's other staging posts, nearest first: a raider at any post counts as staged
///   (<see cref="DragonRallyRoute.AtAssembly"/>), so another post is as good a rally point as its own.
/// - After <see cref="RouteFailureLimit"/> failures from the same spot (about a minute) a raider walks by ordinary travel
///   when the boss is not aggressive. The Glacier Giant is "not aggressive by default" (Allakhazam 16 Oct 2002,
///   FreddysHouse Dec 2003; docs/research/raid-sources/wayback-pass2.md section 11), and its brain has AggroLevel 0, so
///   walking past it does not pull it; raiders never attack it before the order (IsPendingDragonTarget).
/// - An aggressive boss (the dragons) keeps the safe-route retry: an ordinary path could walk a lone raider into it.
/// </summary>
public static class DragonRallyFallback
{
    public const int RouteFailureLimit = 3;
    public const float SameSpotRadius = 400;
    public const int RetryMilliseconds = 30_000;
    public const int OrdinaryTravelMilliseconds = 120_000;
    public const float PostArrival = 175;

    /// <summary>The failure count after one more failed plan from <paramref name="here"/>: a new spot starts again at one.</summary>
    public static int CountFailure(int failures, Vector3 lastSpot, Vector3 here) =>
        failures > 0 && Vector2.DistanceSquared(new(here.X,here.Y),new(lastSpot.X,lastSpot.Y)) <= SameSpotRadius*SameSpotRadius
            ? failures + 1 : 1;

    /// <summary>A boss that never pulls a raider who only walks past it.</summary>
    public static bool IsNeutral(int aggroLevel, int aggroRange) => aggroLevel <= 0 || aggroRange <= 0;

    public static DragonRallyFallbackAction AfterFailure(int failures, bool neutralBoss) =>
        failures < RouteFailureLimit ? DragonRallyFallbackAction.Retry :
        neutralBoss ? DragonRallyFallbackAction.OrdinaryTravel : DragonRallyFallbackAction.KeepSafeRoute;

    /// <summary>
    /// Where the next plan goes after <paramref name="failures"/> failures from one spot: the bot's own post first, then
    /// the raid's other posts, nearest to the bot first, then round again. A post within 600 of its own is the same post.
    /// </summary>
    public static Vector3 GoalForAttempt(Vector3 own, IReadOnlyList<Vector3> posts, Vector3 from, int failures)
    {
        if (failures <= 0 || posts == null || posts.Count == 0) return own;
        var others = posts.Where(p => Vector3.DistanceSquared(p,own) > 600*600)
            .OrderBy(p => Vector2.DistanceSquared(new(p.X,p.Y),new(from.X,from.Y))).ThenBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (others.Length == 0) return own;
        int index = failures % (others.Length + 1);
        return index == 0 ? own : others[index - 1];
    }

    /// <summary>The post nearest to the bot (its own when there are no others): where ordinary travel goes.</summary>
    public static Vector3 NearestPost(Vector3 own, IReadOnlyList<Vector3> posts, Vector3 from) =>
        (posts ?? Array.Empty<Vector3>()).Append(own).OrderBy(p => Vector2.DistanceSquared(new(p.X,p.Y),new(from.X,from.Y))).First();
}
