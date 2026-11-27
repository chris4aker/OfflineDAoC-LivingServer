using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    public class UT_DragonRallyRoute
    {
        private sealed class Attendee : GameBot
        {
            private Attendee():base((OfflineWorldBotRecord)null){}
            public Vector3 Position;
            public bool Alive=true;
            public ushort RegionId=100;
            public override byte Level {get=>50;set{}}
            public override bool IsAlive=>Alive;
            public override int X=>(int)Position.X;
            public override int Y=>(int)Position.Y;
            public override int Z=>(int)Position.Z;
            public override ushort CurrentRegionID {get=>RegionId;set=>RegionId=value;}
        }

        [Test,NonParallelizable]
        public void ProductionAttendanceCountsOnlyPresentIndividualsAndProtectsTheirWait()
        {
            using var server=new EpicTestServerScope();
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            Type manager=typeof(AutonomousRealmRaid);
            object raid=Activator.CreateInstance(manager.GetNestedType("Raid",BindingFlags.NonPublic),true);
            object party=Activator.CreateInstance(manager.GetNestedType("Party",BindingFlags.NonPublic),true);
            var home=DragonLairPlacement.Home(eRealm.Midgard);
            Vector3 own=new(home.X+4000,home.Y,home.Z),other=new(home.X-4000,home.Y,home.Z);
            var members=Enumerable.Range(0,8).Select(_=>
            {
                var bot=(Attendee)RuntimeHelpers.GetUninitializedObject(typeof(Attendee));
                bot.Alive=true;bot.RegionId=100;bot.Position=other;
                typeof(GameNPC).GetField("m_brains",hidden).SetValue(bot,new ArrayList());
                typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot,true);
                return bot;
            }).ToArray();
            var group=new Group(members[0]);
            typeof(Group).GetField("_groupMembers",hidden).SetValue(group,members.Cast<GameLiving>().ToList());
            foreach(var member in members)member.Group=group;
            raid.GetType().GetField("Definition").SetValue(raid,AutonomousRealmRaid.Definitions.Single(d=>d.Id=="dragon-midgard"));
            raid.GetType().GetField("HubDeparted").SetValue(raid,true);
            var boss=(GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            raid.GetType().GetField("Boss").SetValue(raid,boss);
            var posts=(Dictionary<int,Vector3>)raid.GetType().GetField("StagingPosts").GetValue(raid);
            posts.Add(0,own);posts.Add(1,other);
            party.GetType().GetField("Members").SetValue(party,members.Cast<GameBot>().ToArray());
            party.GetType().GetField("Staging").SetValue(party,own);
            ((IDictionary)raid.GetType().GetField("Parties").GetValue(raid)).Add(group,party);
            var membership=(IDictionary)manager.GetField("Membership",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            membership.Add(group,raid);
            try
            {
                members[0].Position=new(home.X,home.Y,home.Z);
                members[1].Alive=false;
                members[2].RegionId=1;
                members[3].Position=other+new Vector3(0,1000,0);
                typeof(GameBot).GetProperty(nameof(GameBot.IsOnStableMasterRoute)).SetValue(members[4],true);
                int present=(int)manager.GetMethod("PresentAtStaging",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{raid});
                Assert.That(present,Is.EqualTo(3),"Three arrivals count without the other five; even at another party's post.");
                Assert.That(AutonomousRealmRaid.Protects(members[5]),Is.True);
                Assert.That(AutonomousRealmRaid.IsPendingDragonTarget(members[5],boss),Is.True);
                Assert.That(AutonomousRealmRaid.IsPendingDragonTarget(members[5],members[0]),Is.False,"Other targets must not be filtered.");
                raid.GetType().GetField("Started").SetValue(raid,true);
                Assert.That(AutonomousRealmRaid.IsPendingDragonTarget(members[5],boss),Is.False,"Battle order releases the dragon pull.");
            }
            finally{membership.Remove(group);}
            Assert.That(AutonomousRealmRaid.IsPendingDragonTarget(members[5],boss),Is.False,"Normal PvE has no expedition gate.");
        }

        [Test]
        public void RouteGoesAroundDragonInsteadOfCrossingIt()
        {
            Vector3 home=new(20000,20000,1000), start=home+new Vector3(-6000,0,0), goal=home+new Vector3(6000,0,0);
            Assert.That(DragonRallyRoute.TryBuild(new UT_RealmRaidFormation.Mesh(),UT_RealmRaidFormation.Zone(),start,goal,home,out var route),Is.True);
            Assert.That(route.Length,Is.GreaterThan(1));
            foreach(var step in route)
            {
                Assert.That(DragonRallyRoute.SegmentDistanceSquared(start,step,home),Is.GreaterThanOrEqualTo(DragonRallyRoute.Clearance*DragonRallyRoute.Clearance));
                start=step;
            }
            Assert.That(start,Is.EqualTo(goal));
        }

        [Test]
        public void PartialPartyMemberAtAnotherExpeditionPostCountsButBossPileDoesNot()
        {
            Vector3 home=new(20000,20000,1000);
            Vector3[] posts=[home+new Vector3(4000,0,0),home+new Vector3(-4000,0,0)];
            Assert.That(DragonRallyRoute.AtAssembly(posts[1]+new Vector3(500,0,0),posts,home),Is.True);
            Assert.That(DragonRallyRoute.AtAssembly(home,posts,home),Is.False);
            Assert.That(DragonRallyRoute.AtAssembly(posts[1]+new Vector3(0,700,0),posts,home),Is.False);
        }

        [Test]
        public void ADisplacedBotCanLeaveTheMarginButCannotCutDeeperIntoIt()
        {
            Assert.That(DragonRallyRoute.SafeSegment(new(1000,0,0),new(4000,0,0),Vector3.Zero),Is.True);
            Assert.That(DragonRallyRoute.SafeSegment(new(1000,0,0),new(-4000,0,0),Vector3.Zero),Is.False);
            Assert.That(DragonRallyRoute.TryBuild(new UT_RealmRaidFormation.Mesh(),UT_RealmRaidFormation.Zone(),new(1000,0,0),new(4000,0,0),Vector3.Zero,out _),Is.True);
        }

        [Test]
        public void DisconnectedRoutesAndUnsafeDestinationsAreNotAccepted()
        {
            Assert.That(DragonRallyRoute.TryBuild(new UT_RealmRaidFormation.Mesh{Connected=false},UT_RealmRaidFormation.Zone(),new(6000,0,0),new(-6000,0,0),Vector3.Zero,out _),Is.False);
            Assert.That(DragonRallyRoute.TryBuild(new UT_RealmRaidFormation.Mesh(),UT_RealmRaidFormation.Zone(),new(6000,0,0),Vector3.Zero,Vector3.Zero,out _),Is.False);
        }

        // Issue #767: a mesh where some goals cannot be reached, and optionally no ring floor at all.
        private sealed class BlockedMesh : PathfindingMgrBase
        {
            public Vector3[] Unreachable = [];
            public bool NoFloor;
            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override Vector3? GetClosestPoint(Zone zone, Vector3 point, float x, float y, float z, EDtPolyFlags[] filters) =>
                NoFloor ? null : point;
            public override bool HasLineOfSight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters) => true;
            public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> result)
            {
                bool blocked = Unreachable.Any(u => Vector3.DistanceSquared(u,to) < 1);
                result[0] = new(blocked ? from : to, EDtPolyFlags.Walk);
                return new(blocked ? PathfindingStatus.PartialPathFound : PathfindingStatus.PathFound, 1);
            }
        }

        [Test]
        public void AFailedRouteSaysWhichStageFailed()
        {
            Vector3 home=Vector3.Zero, start=new(8000,0,0), goal=new(-6000,0,0);
            Assert.That(DragonRallyRoute.TryBuild(new BlockedMesh{Unreachable=[goal]},UT_RealmRaidFormation.Zone(),start,goal,home,out _,out var stage),Is.False);
            Assert.That(stage,Is.EqualTo("goal-leg"),"Ring points were walked; none reaches the post.");
            Assert.That(DragonRallyRoute.TryBuild(new UT_RealmRaidFormation.Mesh{Connected=false},UT_RealmRaidFormation.Zone(),start,goal,home,out _,out stage),Is.False);
            Assert.That(stage,Is.EqualTo("first-leg"));
            Assert.That(DragonRallyRoute.TryBuild(new BlockedMesh{NoFloor=true,Unreachable=[goal]},UT_RealmRaidFormation.Zone(),start,goal,home,out _,out stage),Is.False);
            Assert.That(stage,Is.EqualTo("ring"));
            Assert.That(DragonRallyRoute.TryBuild(new BlockedMesh(),UT_RealmRaidFormation.Zone(),start,new(1000,0,0),home,out _,out stage),Is.False);
            Assert.That(stage,Is.EqualTo("unsafe-goal"));
            Assert.That(DragonRallyRoute.TryBuild(new BlockedMesh(),UT_RealmRaidFormation.Zone(),start,goal,home,out _,out stage),Is.True);
            Assert.That(stage,Is.Empty);
        }

        [Test]
        public void AFailedRallyRouteTriesTheOtherPostsThenFallsBackWithinAMinute()
        {
            Vector3 home=Vector3.Zero, own=new(-6000,0,0), near=new(0,7000,0), far=new(0,-9000,0), at=new(9000,6000,0);
            Vector3[] posts=[own,near,far];
            var mesh=new BlockedMesh{Unreachable=[own]};
            var zone=UT_RealmRaidFormation.Zone();
            int failures=0; Vector3 spot=default;
            // 1st plan: its own post, which fails.
            Vector3 target=DragonRallyFallback.GoalForAttempt(own,posts,at,DragonRallyFallback.CountFailure(failures,spot,at)-1);
            Assert.That(target,Is.EqualTo(own));
            Assert.That(DragonRallyRoute.TryBuild(mesh,zone,at,target,home,out _),Is.False);
            failures=DragonRallyFallback.CountFailure(failures,spot,at); spot=at;
            Assert.That(DragonRallyFallback.AfterFailure(failures,neutralBoss:true),Is.EqualTo(DragonRallyFallbackAction.Retry));
            // 2nd plan from the same spot: the nearest other post, which has a safe route.
            target=DragonRallyFallback.GoalForAttempt(own,posts,at+new Vector3(50,0,0),DragonRallyFallback.CountFailure(failures,spot,at+new Vector3(50,0,0))-1);
            Assert.That(target,Is.EqualTo(near));
            Assert.That(DragonRallyRoute.TryBuild(mesh,zone,at,target,home,out var route),Is.True);
            Assert.That(route[^1],Is.EqualTo(near));
            Assert.That(DragonRallyRoute.AtAssembly(route[^1],posts,home),Is.True,"Another party's post counts as staged.");
            // 3rd: the far post; then the fallback.
            failures=DragonRallyFallback.CountFailure(failures,spot,at);
            Assert.That(DragonRallyFallback.GoalForAttempt(own,posts,at,failures),Is.EqualTo(far));
            failures=DragonRallyFallback.CountFailure(failures,spot,at);
            Assert.That(failures,Is.EqualTo(DragonRallyFallback.RouteFailureLimit));
            Assert.That(DragonRallyFallback.AfterFailure(failures,neutralBoss:true),Is.EqualTo(DragonRallyFallbackAction.OrdinaryTravel));
            Assert.That(DragonRallyFallback.AfterFailure(failures,neutralBoss:false),Is.EqualTo(DragonRallyFallbackAction.KeepSafeRoute),
                "An aggressive boss is never walked past on an ordinary path.");
            Assert.That((DragonRallyFallback.RouteFailureLimit-1)*DragonRallyFallback.RetryMilliseconds,Is.LessThanOrEqualTo(60_000),
                "Three failed plans take about a minute, not twenty.");
            Assert.That(DragonRallyFallback.NearestPost(own,posts,at),Is.EqualTo(near));
            // A bot that walked away starts counting again; a raid with one post keeps its own.
            Assert.That(DragonRallyFallback.CountFailure(failures,spot,at+new Vector3(500,0,0)),Is.EqualTo(1));
            Assert.That(DragonRallyFallback.GoalForAttempt(own,[own],at,2),Is.EqualTo(own));
        }

        [Test]
        public void TheGlacierGiantIsNeutralAndAnAggressiveBossIsNot()
        {
            var giant=new DOL.AI.Brain.GlacierGiantBrain();
            Assert.That(DragonRallyFallback.IsNeutral(giant.AggroLevel,giant.AggroRange),Is.True,"Not aggressive by default (Allakhazam 2002).");
            var dragon=new DOL.AI.Brain.StandardMobBrain{AggroLevel=100,AggroRange=1500};
            Assert.That(DragonRallyFallback.IsNeutral(dragon.AggroLevel,dragon.AggroRange),Is.False);
        }

        [Test]
        public void ARallyThatPlansARouteFirstTimeIsUnchanged()
        {
            Vector3 home=new(20000,20000,1000), start=home+new Vector3(-6000,0,0), goal=home+new Vector3(6000,0,0);
            Assert.That(DragonRallyFallback.GoalForAttempt(goal,[goal,home+new Vector3(0,6000,0)],start,0),Is.EqualTo(goal),"The first plan goes to its own post.");
            Assert.That(DragonRallyRoute.TryBuild(new UT_RealmRaidFormation.Mesh(),UT_RealmRaidFormation.Zone(),start,goal,home,out var route,out var stage),Is.True);
            Assert.That(route[^1],Is.EqualTo(goal));
            Assert.That(stage,Is.Empty);
        }

        [Test]
        public void AStuckRallyingBotIsCaughtAfterTheUsualBoundAndAMovingOneIsNot()
        {
            DateTime now=new(2026,11,27,1,0,0,DateTimeKind.Utc);
            Assert.That(AutonomousStuckWatchdog.ShouldCatchStuckRally(now-AutonomousStuckWatchdog.StuckThreshold,now,true,false),Is.True);
            Assert.That(AutonomousStuckWatchdog.ShouldCatchStuckRally(now-TimeSpan.FromMinutes(14),now,true,false),Is.False);
            Assert.That(AutonomousStuckWatchdog.ShouldCatchStuckRally(now-TimeSpan.FromMinutes(30),now,false,false),Is.False,"Dead: the release owns it.");
            Assert.That(AutonomousStuckWatchdog.ShouldCatchStuckRally(now-TimeSpan.FromMinutes(30),now,true,true),Is.False,"A player decides.");
        }

        [Test,NonParallelizable]
        public void ATravellingRallyMemberIsNoLongerHeldButHubAndStagedMembersAre()
        {
            using var server=new EpicTestServerScope();
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            Type manager=typeof(AutonomousRealmRaid);
            object raid=Activator.CreateInstance(manager.GetNestedType("Raid",BindingFlags.NonPublic),true);
            object party=Activator.CreateInstance(manager.GetNestedType("Party",BindingFlags.NonPublic),true);
            Vector3 home=new(651517,625897,5320), own=new(655296,635040,7751), other=new(651457,632837,5350);
            var members=Enumerable.Range(0,2).Select(_=>
            {
                var bot=(Attendee)RuntimeHelpers.GetUninitializedObject(typeof(Attendee));
                bot.Alive=true;bot.RegionId=100;bot.Position=home+new Vector3(12000,0,0);
                typeof(GameNPC).GetField("m_brains",hidden).SetValue(bot,new ArrayList());
                typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot,true);
                return bot;
            }).ToArray();
            var group=new Group(members[0]);
            typeof(Group).GetField("_groupMembers",hidden).SetValue(group,members.Cast<GameLiving>().ToList());
            foreach(var member in members)member.Group=group;
            raid.GetType().GetField("Definition").SetValue(raid,AutonomousRealmRaid.Definitions.Single(d=>d.Id=="openworld-midgard-glaciergiant"));
            var boss=(GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            typeof(GameNPC).GetField("m_brains",hidden).SetValue(boss,new ArrayList{new DOL.AI.Brain.GlacierGiantBrain()});
            boss.SpawnPoint=new Point3D((int)home.X,(int)home.Y,(int)home.Z);
            raid.GetType().GetField("Boss").SetValue(raid,boss);
            var posts=(Dictionary<int,Vector3>)raid.GetType().GetField("StagingPosts").GetValue(raid);
            posts.Add(0,own);posts.Add(1,other);
            party.GetType().GetField("Members").SetValue(party,members.Cast<GameBot>().ToArray());
            party.GetType().GetField("Staging").SetValue(party,own);
            ((IDictionary)raid.GetType().GetField("Parties").GetValue(raid)).Add(group,party);
            var membership=(IDictionary)manager.GetField("Membership",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            membership.Add(group,raid);
            try
            {
                Assert.That(AutonomousRealmRaid.IsInRally(members[0]),Is.True);
                Assert.That(AutonomousRealmRaid.IsTravellingInRally(members[0],out _),Is.False,"At or on the way to the hub: the hub's own rules hold it.");
                raid.GetType().GetField("HubDeparted").SetValue(raid,true);
                Assert.That(AutonomousRealmRaid.IsTravellingInRally(members[0],out string id),Is.True);
                Assert.That(id,Is.EqualTo("openworld-midgard-glaciergiant"));
                Assert.That(AutonomousRealmRaid.Protects(members[0]),Is.False,"Off the ring: the movement clock runs.");
                members[1].Position=other+new Vector3(100,0,0);
                Assert.That(AutonomousRealmRaid.Protects(members[1]),Is.True,"Standing at another party's post is a normal wait.");
                Assert.That(AutonomousRealmRaid.TryDragonRallyFallback(members[0],out var known,out bool neutral),Is.True);
                Assert.That(known,Is.EquivalentTo(new[]{own,other}));
                Assert.That(neutral,Is.True,"The Glacier Giant leaves passers-by alone.");
                raid.GetType().GetField("Started").SetValue(raid,true);
                Assert.That(AutonomousRealmRaid.IsTravellingInRally(members[0],out _),Is.False,"The battle has its own clocks.");
            }
            finally{membership.Remove(group);}
        }

        // --- Review of PR #771: the real wiring -------------------------------------------------------------------

        private static readonly Vector3 GgHome=new(651517,625897,5320), GgOwn=new(655296,635040,7751), GgNear=new(651457,632837,5350),
            GgFar=new(649709,622613,5461), GgFrom=new(663517,628897,5320);
        private static DragonRallyOrder GgOrder(bool neutral, string id="openworld-midgard-glaciergiant") =>
            new(id,GgOwn,GgHome,[GgOwn,GgNear,GgFar],neutral);

        /// <summary>Every plan fails (the #767 log: queries=133, a complete search with no answer).</summary>
        private static Func<Vector3,Vector3,bool,DragonRallySlice> Failing(List<Vector3> asked) =>
            (origin,target,fresh)=>{ asked.Add(target); return new(false,false,null,"goal-leg",133); };

        [Test]
        public void ThreeFailedPlansFromOneSpotWithANeutralBossWalkToTheNearestPostAndHoldThere()
        {
            var rally=new DragonRallyPlanner(); var asked=new List<Vector3>();
            var order=GgOrder(neutral:true);
            var first=rally.Step(0,GgFrom,order,Failing(asked));
            Assert.That((first.Move,first.RetryRow,first.FallbackRow,first.Attempt),Is.EqualTo((DragonRallyMove.Wait,true,false,1)));
            Assert.That(rally.Step(10_000,GgFrom,order,Failing(asked)).Move,Is.EqualTo(DragonRallyMove.Wait),"no plan before the retry is due");
            var second=rally.Step(30_000,GgFrom+new Vector3(40,0,0),order,Failing(asked));
            Assert.That((second.Move,second.Attempt),Is.EqualTo((DragonRallyMove.Wait,2)));
            var third=rally.Step(60_000,GgFrom,order,Failing(asked));
            Assert.That(asked,Is.EqualTo(new[]{GgOwn,GgNear,GgFar}),"own post, then the others nearest first");
            Assert.That((third.Move,third.Destination,third.RetryRow,third.FallbackRow,third.FallbackAction),
                Is.EqualTo((DragonRallyMove.Walk,GgOwn,true,true,"ordinary-travel")),"a minute, not twenty; the nearest post is its own");
            Assert.That(rally.Step(90_000,GgFrom+new Vector3(-3000,1500,0),order,Failing(asked)).Move,Is.EqualTo(DragonRallyMove.Walk));
            // Arrived: it holds, and never plans again for this rally (no churn after the two minutes).
            var arrived=rally.Step(150_000,GgOwn+new Vector3(150,0,0),order,Failing(asked));
            Assert.That((arrived.Move,arrived.StopFirst),Is.EqualTo((DragonRallyMove.Hold,true)));
            int plans=asked.Count;
            for(long t=200_000;t<=1_200_000;t+=30_000)
                Assert.That(rally.Step(t,GgOwn+new Vector3(t%60_000==0?150:-300,0,0),order,Failing(asked)).Move,Is.EqualTo(DragonRallyMove.Hold));
            Assert.That(asked.Count,Is.EqualTo(plans),"a raider at a post never plans the safe route again");
        }

        [Test]
        public void AnAggressiveBossKeepsTheSafeRouteRetryAndNeverWalksPastIt()
        {
            var rally=new DragonRallyPlanner(); var asked=new List<Vector3>();
            var order=GgOrder(neutral:false);
            var rows=new List<DragonRallyDecision>();
            for(long t=0;t<=300_000;t+=30_000) rows.Add(rally.Step(t,GgFrom,order,Failing(asked)));
            Assert.That(rows.Select(r=>r.Move),Is.All.EqualTo(DragonRallyMove.Wait));
            Assert.That(rows.Count(r=>r.RetryRow),Is.EqualTo(rows.Count));
            Assert.That(rows.Where(r=>r.FallbackRow).Select(r=>(r.Attempt,r.FallbackAction)),Is.EqualTo(new[]{(3,"keep-safe-route")}),"logged once");
        }

        [Test]
        public void AFirstTimeRouteIsFollowedToItsPostAndHeldAsBefore()
        {
            var rally=new DragonRallyPlanner();
            Vector3 corner=GgFrom+new Vector3(0,6000,0);
            int plans=0;
            Func<Vector3,Vector3,bool,DragonRallySlice> ok=(o,t,f)=>{ plans++; return new(false,true,[corner,t],"",40); };
            var d=rally.Step(0,GgFrom,GgOrder(true),ok);
            Assert.That((d.Move,d.Destination,d.RetryRow,d.FallbackRow),Is.EqualTo((DragonRallyMove.Follow,corner,false,false)));
            Assert.That(rally.Step(5_000,corner,GgOrder(true),ok).Destination,Is.EqualTo(GgOwn));
            Assert.That(rally.Step(9_000,GgOwn,GgOrder(true),ok).Move,Is.EqualTo(DragonRallyMove.Hold));
            Assert.That(plans,Is.EqualTo(1));
            // A yielded slice keeps planning from the same origin.
            var slow=new DragonRallyPlanner(); int slices=0;
            Func<Vector3,Vector3,bool,DragonRallySlice> sliced=(o,t,f)=>{ Assert.That(f,Is.EqualTo(slices==0)); return ++slices<3 ? DragonRallySlice.Yield : new(false,true,[t],"",9); };
            Assert.That(slow.Step(0,GgFrom,GgOrder(true),sliced).Move,Is.EqualTo(DragonRallyMove.Wait));
            Assert.That(slow.Step(250,GgFrom,GgOrder(true),sliced).Move,Is.EqualTo(DragonRallyMove.Wait));
            Assert.That(slow.Step(500,GgFrom,GgOrder(true),sliced).Move,Is.EqualTo(DragonRallyMove.Follow));
        }

        [Test]
        public void AHeldRaiderWalksAgainOnlyWhenMovedFarOrWhenTheEventChanges()
        {
            var rally=new DragonRallyPlanner(); var asked=new List<Vector3>();
            Assert.That(rally.Step(0,GgNear,GgOrder(true),Failing(asked)).Move,Is.EqualTo(DragonRallyMove.Hold),"standing at another party's post");
            Assert.That(rally.Step(1_000,GgNear+new Vector3(1200,0,0),GgOrder(true),Failing(asked)).Move,Is.EqualTo(DragonRallyMove.Hold),"a scuffle");
            Assert.That(rally.Step(2_000,GgFrom,GgOrder(true),Failing(asked)).Move,Is.EqualTo(DragonRallyMove.Wait),"released and back far away: it plans");
            Assert.That(asked,Has.Count.EqualTo(1));
            var other=new DragonRallyPlanner();
            other.Step(0,GgNear,GgOrder(true),Failing(asked));
            Assert.That(other.Held,Is.True);
            other.Step(1_000,GgNear,GgOrder(true,"openworld-midgard-revenant"),Failing(asked));
            Assert.That(other.EventId,Is.EqualTo("openworld-midgard-revenant"));
            other.Clear();
            Assert.That((other.Held,other.Failures,other.Route),Is.EqualTo((false,0,(Vector3[])null)));
        }

        private static string Source(params string[] parts)
        {
            string dir=TestContext.CurrentContext.TestDirectory;
            string[] path=new[]{"GameServer","bots","autonomous"}.Concat(parts).ToArray();
            while(dir!=null && !File.Exists(Path.Combine(new[]{dir}.Concat(path).ToArray()))) dir=Path.GetDirectoryName(dir);
            Assert.That(dir,Is.Not.Null,"the server sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[]{dir}.Concat(path).ToArray())).Replace("\r\n","\n");
        }

        [Test]
        public void TheControllerExecutesThePlannersDecisions()
        {
            string code=Source("AutonomousWorldBotController.Expedition.cs");
            int start=code.IndexOf("private bool TravelDragonRally(");
            Assert.That(start,Is.GreaterThan(0));
            string body=code.Substring(start,code.IndexOf("\n    }\n",start)-start);
            Assert.That(body,Does.Contain("_dragonRally.Step(now,current,new(order.EventId,goal,home,posts,neutralBoss)"));
            Assert.That(body,Does.Contain("AutonomousRealmRaid.TryDragonRallyFallback(bot,out var posts,out bool neutralBoss)"));
            Assert.That(body,Does.Contain("case DragonRallyMove.Walk:\n                GuardExpeditionDanger(bot,d.Destination);\n                IssuePath(bot,d.Destination,preciseArrival:true);"));
            Assert.That(body,Does.Contain("_dragonRally.PathFailed(now)"));
            Assert.That(body,Does.Contain("DRAGON_RALLY_FALLBACK"));
            Assert.That(Source("AutonomousWorldBotController.cs"),Does.Contain("_dragonRally.Interrupt();"),"a fight keeps the hold");
        }

        private sealed class RallyWorld : IDisposable
        {
            const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            public readonly object Raid;
            public readonly Attendee[] Members;
            private readonly IDictionary _membership;
            private readonly Group _group;
            public RallyWorld(string id,params Vector3[] at)
            {
                Type manager=typeof(AutonomousRealmRaid);
                Raid=Activator.CreateInstance(manager.GetNestedType("Raid",BindingFlags.NonPublic),true);
                object party=Activator.CreateInstance(manager.GetNestedType("Party",BindingFlags.NonPublic),true);
                Members=at.Select((p,i)=>
                {
                    var bot=(Attendee)RuntimeHelpers.GetUninitializedObject(typeof(Attendee));
                    bot.Alive=true;bot.RegionId=100;bot.Position=p;
                    typeof(GameNPC).GetField("m_brains",Hidden).SetValue(bot,new ArrayList());
                    typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot,true);
                    bot.DatabaseID=976_700+i;
                    typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,new OfflineWorldBotRecord{BotId=976_700+i});
                    return bot;
                }).ToArray();
                _group=new Group(Members[0]);
                typeof(Group).GetField("_groupMembers",Hidden).SetValue(_group,Members.Cast<GameLiving>().ToList());
                foreach(var member in Members)member.Group=_group;
                Raid.GetType().GetField("Definition").SetValue(Raid,AutonomousRealmRaid.Definitions.Single(d=>d.Id==id));
                var boss=(GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
                typeof(GameNPC).GetField("m_brains",Hidden).SetValue(boss,new ArrayList{new DOL.AI.Brain.GlacierGiantBrain()});
                boss.SpawnPoint=new Point3D((int)GgHome.X,(int)GgHome.Y,(int)GgHome.Z);
                Raid.GetType().GetField("Boss").SetValue(Raid,boss);
                var posts=(Dictionary<int,Vector3>)Raid.GetType().GetField("StagingPosts").GetValue(Raid);
                posts.Add(0,GgOwn);posts.Add(1,GgNear);
                party.GetType().GetField("Members").SetValue(party,Members.Cast<GameBot>().ToArray());
                party.GetType().GetField("Staging").SetValue(party,GgOwn);
                ((IDictionary)Raid.GetType().GetField("Parties").GetValue(Raid)).Add(_group,party);
                _membership=(IDictionary)manager.GetField("Membership",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                _membership.Add(_group,Raid);
                // The position check needs a live world; the clocks are what this test is about.
                var validated=(ConcurrentDictionary<long,DateTime>)typeof(AutonomousStuckWatchdog)
                    .GetField("LastPositionValidation",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                foreach(var member in Members) validated[member.DatabaseID]=DateTime.MaxValue;
            }
            public void Set(string field,bool value)=>Raid.GetType().GetField(field).SetValue(Raid,value);
            public void Dispose()
            {
                _membership.Remove(_group);
                AutonomousStuckWatchdog.StuckRallyRecoveryForTests=null;
                foreach(var member in Members) AutonomousStuckWatchdog.Unregister(member);
            }
        }

        [Test,NonParallelizable]
        public void TheWatchdogRecoversARallyMemberStuckOffTheRingButNotOneAtTheHubOrAPost()
        {
            DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null,DOL.Logging.LogLibrary.None); // the AUTONOMOUS_RALLY_STUCK row
            using var server=new EpicTestServerScope();
            using var world=new RallyWorld("openworld-midgard-glaciergiant",GgFrom,GgNear+new Vector3(100,0,0));
            GameBot traveller=world.Members[0], staged=world.Members[1];
            var recovered=new List<long>();
            AutonomousStuckWatchdog.StuckRallyRecoveryForTests=(bot,id)=>{ recovered.Add(bot.DatabaseID); Assert.That(id,Is.EqualTo("openworld-midgard-glaciergiant")); return true; };
            DateTime t0=new(2026,11,27,0,0,0,DateTimeKind.Utc);
            // At the hub (not departed): held however long it stands, as before.
            foreach(var bot in world.Members) Assert.That(AutonomousStuckWatchdog.Observe(bot,t0),Is.False);
            foreach(var bot in world.Members) Assert.That(AutonomousStuckWatchdog.Observe(bot,t0.AddMinutes(40)),Is.False);
            Assert.That(recovered,Is.Empty,"the hub's own rules hold it");
            world.Set("HubDeparted",true);
            DateTime t1=t0.AddMinutes(40);
            Assert.That(AutonomousStuckWatchdog.Observe(traveller,t1.AddMinutes(14)),Is.False);
            Assert.That(AutonomousStuckWatchdog.Observe(staged,t1.AddMinutes(14)),Is.False);
            Assert.That(AutonomousStuckWatchdog.Observe(staged,t1.AddMinutes(16)),Is.False,"waiting at a post is a normal wait");
            Assert.That(AutonomousStuckWatchdog.Observe(traveller,t1.AddMinutes(16)),Is.True,"a stuck rally is still stuck");
            Assert.That(recovered,Is.EqualTo(new[]{traveller.DatabaseID}));
        }

        [Test,NonParallelizable]
        public void TheRallyBoundIsForOpenWorldRalliesOnlyNotDungeonExpeditions()
        {
            using var server=new EpicTestServerScope();
            string dungeon=AutonomousRealmRaid.Definitions.First(d=>d.IsDungeon).Id;
            using var world=new RallyWorld(dungeon,GgFrom);
            world.Set("HubDeparted",true);
            Assert.That(AutonomousRealmRaid.IsInRally(world.Members[0]),Is.True);
            Assert.That(AutonomousRealmRaid.IsTravellingInRally(world.Members[0],out _),Is.False,"boats, portals and door waits keep the old hold");
        }

        [Test,Explicit("Read-only installed dragon meshes"),NonParallelizable]
        public void AllDragonStagingPostsHaveBossAvoidingApproaches()
        {
            string prior=Environment.CurrentDirectory;
            try
            {
                string native=Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
                System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name,assembly,search)=>name=="lib/Detour"?System.Runtime.InteropServices.NativeLibrary.Load(native):IntPtr.Zero);
                Environment.CurrentDirectory=Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
                foreach(var (realm,id,x,y) in new[]{(eRealm.Albion,4,43*8192,85*8192),(eRealm.Midgard,116,84*8192,118*8192),(eRealm.Hibernia,216,43*8192,79*8192)})
                {
                    var zone=new Zone(null,(ushort)id,"Dragon rally check",x,y,65536,65536,(ushort)id,false,0,false,0,0,0,0,0);
                    LocalPathfindingMgr.LoadNavMesh(zone);
                    try
                    {
                        var h=DragonLairPlacement.Home(realm);Vector3 home=new(h.X,h.Y,h.Z);
                        var posts=new List<Vector3>();var nav=PathfindingProvider.LocalPathfindingMgr;
                        for(int slot=0;slot<RealmRaidRecruitmentPolicy.MaximumParties;slot++)
                        {
                            Assert.That(RealmRaidStaging.TryDragonPost(nav,zone,home,slot,posts,out var p),Is.True,$"{realm} staging {slot}");
                            posts.Add(p);
                        }
                        for(int slot=0;slot<posts.Count;slot++)
                        {
                            // Cross-lair starts reproduce the unsafe shortcuts.
                            Vector3 start=posts[(slot+19)%posts.Count];
                            var work=new RvrPlanningNavigation(nav);Vector3[] route=null;bool found=false;
                            for(int slice=0;slice<1000 && route==null;slice++)
                            {
                                work.BeginSlice();
                                try{found=DragonRallyRoute.TryBuild(work,zone,start,posts[slot],home,out var answer);route=answer;}
                                catch(RvrPlanningNavigation.Yield){}
                                finally{work.EndSlice();}
                            }
                            Assert.That(found,Is.True,$"{realm} safe approach {slot} from={start} to={posts[slot]} queries={work.Queries}");
                            TestContext.Progress.WriteLine($"{realm} post={slot} safeWaypoints={route.Length} queries={work.Queries}");
                        }
                        int entrances=0;
                        for(int direction=0;direction<12;direction++)
                        {
                            double angle=direction*Math.PI/6;
                            Vector3 raw=home+new Vector3((float)Math.Cos(angle)*12500,(float)Math.Sin(angle)*12500,0);
                            var entry=nav.GetClosestPoint(zone,raw,256,256,4096,nav.DefaultFilters);
                            if(!entry.HasValue || !AutonomousZoneItinerary.HasCompleteCorridor(nav,zone,entry.Value,posts[0])) continue;
                            entrances++;
                            for(int slot=0;slot<posts.Count;slot++)
                            {
                                var work=new RvrPlanningNavigation(nav);bool done=false,found=false;
                                for(int slice=0;slice<1000 && !done;slice++)
                                {
                                    work.BeginSlice();
                                    try{found=DragonRallyRoute.TryBuild(work,zone,entry.Value,posts[slot],home,out _);done=true;}
                                    catch(RvrPlanningNavigation.Yield){}
                                    finally{work.EndSlice();}
                                }
                                Assert.That(found,Is.True,$"{realm} incoming direction={direction} slot={slot} start={entry} queries={work.Queries}");
                            }
                        }
                        Assert.That(entrances,Is.GreaterThan(0),$"{realm} needs at least one checked incoming approach");
                        TestContext.Progress.WriteLine($"{realm} incomingApproaches={entrances} toAllPosts={entrances*posts.Count}");
                    }
                    finally{LocalPathfindingMgr.UnloadNavMesh(zone);}
                }
            }
            finally{Environment.CurrentDirectory=prior;}
        }
    }
}
