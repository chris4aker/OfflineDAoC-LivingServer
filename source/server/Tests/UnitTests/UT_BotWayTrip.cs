using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 4: the state machine that owns a gamebot's walk while it tags along or takes a wrong road
    /// (BotWayTrip), driven turn by turn with a fake body. Never less safe than today's travel: it hands the bot back when
    /// today's route-threat guard would act, when the bot needs a rest, when the leader stops, and on a death (counted). The
    /// 3-minute cap holds whatever happens. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotWayTrip
    {
        private sealed class Body : IBotWayBody
        {
            public string Name { get; set; } = "Walker";
            public long Id { get; set; } = 5_001;
            public eRealm Realm { get; set; } = eRealm.Albion;
            public int Level { get; set; } = 25;
            public Vector3 Position
            {
                get
                {
                    PositionReads++;
                    return At;
                }
            }
            public Vector3 At;
            public int PositionReads;
            public float Speed { get; set; } = 200;
            public bool IsAlive { get; set; } = true;
            public bool IsActive { get; set; } = true;
            public bool IsMoving { get; set; }
            public bool InGroup { get; set; }
            public bool OnStableRide { get; set; }
            public bool NeedsRest { get; set; }
            public string Assignment { get; set; } = "task-1";
            public string TravelDestination { get; set; } = "Lyonesse";
            public object Region { get; set; } = "region-1";
            public object Zone { get; set; } = "zone-cornwall";
            public string ZoneDescription { get; set; } = "Cornwall";
            public bool Threat;
            public bool Failure;
            public readonly List<Vector3> Orders = new();
            public int Stops;
            public int Turns;
            public int ThreatLooks;

            public void PathTo(Vector3 target)
            {
                Orders.Add(target);
                IsMoving = true;
            }

            public void Stop()
            {
                Stops++;
                IsMoving = false;
            }

            public bool TakePathFailure()
            {
                bool had = Failure;
                Failure = false;
                return had;
            }

            public void Turn(int headingOffset) => Turns++;

            public bool ThreatAhead(Vector3 target)
            {
                ThreatLooks++;
                return Threat;
            }
        }

        [SetUp]
        public void SetUp() => BotWayFinding.ResetForTests();

        [TearDown]
        public void TearDown() => BotWayFinding.ResetForTests();

        private static readonly BotPlaceKey[] Lyonesse = [BotPlaceKey.Of("lyonesse")];
        private static readonly Vector3 Fork = new(10_000, 10_000, 0);
        private static readonly Vector3 Branch = new(12_048, 10_000, 0);
        private static readonly BotWrongLeg Leg = new(0, 1_000, 6_000);

        private static string Summary() => BotWayFinding.TakeSummary(300).Single();

        /// <summary>Steps the walk every 500 ms until <paramref name="until"/>; returns the turn it ended on, or -1.</summary>
        private static long Run(BotWayTrip trip, Body body, ref long now, long until, System.Action<long> each = null)
        {
            for (; now <= until; now += 500)
            {
                each?.Invoke(now);
                trip.Step(body, now, out bool ended);
                if (ended)
                    return now;
            }
            return -1;
        }

        // ---- tagging along --------------------------------------------------------------------------------------------

        private static (BotWayTrip Trip, Body Bot, Body Leader) Following(long now = 1_000)
        {
            var bot = new Body { At = new(0, 0, 0) };
            var leader = new Body { Name = "Leader", Id = 6_001, At = new(450, 0, 0), IsMoving = true };
            var trip = new BotWayTrip();
            trip.StartFollow(bot, leader, "Lyonesse", Lyonesse, 450, now);
            return (trip, bot, leader);
        }

        [Test]
        public void A_follower_catches_up_beyond_its_distance_waits_when_close_and_orders_at_most_once_a_second()
        {
            var (trip, bot, leader) = Following();
            long now = 1_000;
            Assert.That(trip.IsFollowing, Is.True);
            leader.At = new(1_000, 0, 0);
            Assert.That(trip.Step(bot, now, out bool ended), Is.True);
            Assert.That(ended, Is.False);
            Assert.That(bot.Orders, Has.Count.EqualTo(1), "1,000 behind: it catches up");
            Assert.That(bot.Orders[0], Is.EqualTo(leader.At));
            int reads = leader.PositionReads;
            for (int i = 1; i < 4; i++)
                trip.Step(bot, now + i * 200, out _);
            Assert.That(leader.PositionReads, Is.EqualTo(reads), "nothing about the leader is read between the 1 s looks");
            Assert.That(bot.Orders, Has.Count.EqualTo(1));
            bot.At = new(800, 0, 0);
            trip.Step(bot, now + 1_000, out _);
            Assert.That(bot.Stops, Is.EqualTo(1), "200 behind: it waits");
        }

        [Test]
        public void A_follow_ends_when_todays_route_guard_would_act_on_a_monster_ahead()
        {
            var (trip, bot, leader) = Following();
            long now = 1_000;
            trip.Step(bot, now, out _);
            Assert.That(bot.ThreatLooks, Is.EqualTo(1), "it looks ahead as today's guard does");
            bot.Threat = true;
            long ended = Run(trip, bot, ref now, 10_000, t => leader.At += new Vector3(100, 0, 0));
            Assert.That(ended, Is.GreaterThan(0).And.LessThanOrEqualTo(1_000 + AutonomousRouteThreatPolicy.ScanMilliseconds + 1_000),
                "within one look of the guard's period");
            Assert.That(trip.LastResult, Is.EqualTo("threat"));
            Assert.That(trip.IsWalking, Is.False);
            Assert.That(bot.Stops, Is.GreaterThan(0), "it stops; the controller and its guard take over");
        }

        [Test]
        public void A_follow_ends_when_the_leader_has_stood_for_40_seconds_not_before()
        {
            var (trip, bot, leader) = Following();
            leader.IsMoving = false;
            long now = 1_000;
            long ended = Run(trip, bot, ref now, 120_000);
            Assert.That(ended, Is.GreaterThan(1_000 + BotWayTrip.LeaderIdleMilliseconds));
            Assert.That(ended, Is.LessThanOrEqualTo(1_000 + BotWayTrip.LeaderIdleMilliseconds + 1_500));
            Assert.That(trip.LastResult, Is.EqualTo("leader_stopped"));
        }

        [Test]
        public void A_leader_that_keeps_walking_is_followed_until_the_zone_line()
        {
            var (trip, bot, leader) = Following();
            long now = 1_000;
            Assert.That(Run(trip, bot, ref now, 60_000, t =>
            {
                leader.At += new Vector3(100, 0, 0);
                bot.At += new Vector3(100, 0, 0);
            }), Is.EqualTo(-1), "a minute behind a walking leader");
            Assert.That(trip.IsFollowing, Is.True);
            leader.Zone = "zone-lyonesse";
            Assert.That(Run(trip, bot, ref now, now + 2_000), Is.GreaterThan(0));
            Assert.That(trip.LastResult, Is.EqualTo("zone_line"));
        }

        [Test]
        public void A_follow_ends_in_the_destination_zone_and_when_the_bot_needs_a_rest()
        {
            var (trip, bot, _) = Following();
            long now = 1_000;
            bot.ZoneDescription = "Lyonesse";
            Assert.That(Run(trip, bot, ref now, 3_000), Is.GreaterThan(0));
            Assert.That(trip.LastResult, Is.EqualTo("zone_line"));

            (trip, bot, _) = Following();
            now = 1_000;
            bot.NeedsRest = true;
            Assert.That(Run(trip, bot, ref now, 2_000), Is.EqualTo(1_000), "it rests by today's rules at once, at any health");
            Assert.That(trip.LastResult, Is.EqualTo("interrupted"));
        }

        [Test]
        public void A_follow_ends_when_the_leader_rides_dies_or_goes_elsewhere_and_after_10_minutes()
        {
            foreach (System.Action<Body> change in new System.Action<Body>[]
                     {
                         l => l.OnStableRide = true, l => l.IsAlive = false, l => l.IsActive = false, l => l.TravelDestination = "Camelot Hills",
                         l => l.Region = "region-2",
                     })
            {
                var (trip, bot, leader) = Following();
                long now = 1_000;
                change(leader);
                Assert.That(Run(trip, bot, ref now, 3_000), Is.GreaterThan(0));
                Assert.That(trip.LastResult, Is.EqualTo("leader_gone"));
            }
            var (timed, walker, walking) = Following();
            long t = 1_000;
            Assert.That(Run(timed, walker, ref t, 1_000 + BotWayFinding.FollowMaxMilliseconds + 2_000, _ =>
            {
                walking.At += new Vector3(50, 0, 0);
                walker.At += new Vector3(50, 0, 0);
            }), Is.GreaterThan(1_000 + BotWayFinding.FollowMaxMilliseconds));
            Assert.That(timed.LastResult, Is.EqualTo("time"));
        }

        [Test]
        public void A_death_while_following_ends_it_and_is_counted_once()
        {
            var (trip, bot, _) = Following();
            trip.Step(bot, 1_000, out _);
            bot.IsAlive = false;
            trip.ObserveDeath(bot, 1_500);
            trip.ObserveDeath(bot, 2_000);
            trip.ObserveDeath(bot, 9_000);
            Assert.That(trip.IsWalking, Is.False);
            Assert.That(trip.LastResult, Is.EqualTo("died"));
            Assert.That(Summary(), Does.Contain("deaths_60s=0 follow_deaths_60s=1"));
        }

        // ---- the wrong road ------------------------------------------------------------------------------------------

        private static (BotWayTrip Trip, Body Bot) OnWrongRoad(long now = 1_000)
        {
            var bot = new Body { At = Fork };
            var trip = new BotWayTrip();
            trip.StartWrongRoad(bot, Fork, Branch, Leg, now);
            return (trip, bot);
        }

        [Test]
        public void A_wrong_road_goes_out_looks_around_and_comes_back_to_its_fork()
        {
            var (trip, bot) = OnWrongRoad();
            Assert.That(trip.PhaseName, Is.EqualTo("out"));
            Assert.That(bot.Orders, Is.EqualTo(new[] { Branch }), "one order toward the proven anchor");
            long now = 1_000;
            long ended = Run(trip, bot, ref now, 200_000, t =>
            {
                // 100 units a step out, then back.
                if (trip.PhaseName == "out")
                    bot.At += new Vector3(100, 0, 0);
                else if (trip.PhaseName == "back")
                    bot.At -= new Vector3(100, 0, 0);
            });
            Assert.That(ended, Is.GreaterThan(0));
            Assert.That(trip.LastResult, Is.EqualTo("back"));
            Assert.That(bot.Orders.Last(), Is.EqualTo(Fork), "it walked back to where it left its route");
            Assert.That(bot.Turns, Is.EqualTo(2), "it looked one way and the other");
            Assert.That(ended - 1_000, Is.LessThan(BotWayFinding.WrongRoadCapMilliseconds));
            Assert.That(Summary(), Does.Contain("wrong_roads=1"));
        }

        [Test]
        public void A_monster_ahead_on_the_wrong_road_turns_it_back_and_one_on_the_way_back_hands_the_bot_to_the_controller()
        {
            var (trip, bot) = OnWrongRoad();
            long now = 1_000;
            bot.Threat = true;
            Run(trip, bot, ref now, 10_000, t =>
            {
                if (trip.PhaseName == "out")
                    bot.At += new Vector3(100, 0, 0);
            });
            Assert.That(trip.PhaseName, Is.Not.EqualTo("out"), "it turned back");
            Assert.That(bot.Orders.Last(), Is.EqualTo(Fork));
            // Still something ahead on the way back: it ends there and the controller (and its guard) plans on.
            Assert.That(trip.IsWalking, Is.False);
            Assert.That(trip.LastResult, Is.EqualTo("threat"));

            (trip, bot) = OnWrongRoad();
            now = 1_000;
            bot.Threat = true;
            long ended = Run(trip, bot, ref now, 60_000, t =>
            {
                if (trip.PhaseName == "out")
                    bot.At += new Vector3(20, 0, 0);
                else if (trip.PhaseName == "back")
                {
                    bot.Threat = false; // the way back is clear
                    bot.At = Fork;
                }
            });
            Assert.That(ended, Is.GreaterThan(0));
            Assert.That(trip.LastResult, Is.EqualTo("back_threat"));
        }

        [Test]
        public void A_wrong_road_stops_at_3_minutes_whatever_happens()
        {
            var (trip, bot) = OnWrongRoad();
            bot.Speed = 10;
            long now = 1_000;
            // A crawl that keeps moving and never gets anywhere: only the cap ends it.
            long ended = Run(trip, bot, ref now, 400_000, t =>
            {
                bot.IsMoving = true;
                bot.At = Fork + new Vector3(500, 0, 0);
            });
            Assert.That(ended, Is.GreaterThanOrEqualTo(1_000 + BotWayFinding.WrongRoadCapMilliseconds));
            Assert.That(ended, Is.LessThanOrEqualTo(1_000 + BotWayFinding.WrongRoadCapMilliseconds + 500));
            Assert.That(trip.LastResult, Is.EqualTo("cap"));
            Assert.That(trip.IsWalking, Is.False);
            Assert.That(Summary(), Does.Contain("minutes_lost=").And.Contain("wrong_roads=1"));
        }

        [Test]
        public void A_fight_a_group_a_ride_a_rest_or_a_new_task_ends_a_wrong_road_at_once()
        {
            foreach (System.Action<Body> change in new System.Action<Body>[]
                     {
                         b => b.InGroup = true, b => b.OnStableRide = true, b => b.NeedsRest = true, b => b.Assignment = "task-2",
                     })
            {
                var (trip, bot) = OnWrongRoad();
                change(bot);
                trip.Step(bot, 1_500, out bool ended);
                Assert.That(ended, Is.True);
                Assert.That(trip.LastResult, Is.EqualTo("interrupted"));
            }
            // A fight owns the bot (the controller's combat turn comes first): the gap ends it.
            var (fought, walker) = OnWrongRoad();
            fought.Step(walker, 1_500, out _);
            fought.Step(walker, 8_000, out bool after);
            Assert.That(after, Is.True);
            Assert.That(fought.LastResult, Is.EqualTo("interrupted"));
        }

        [Test]
        public void A_failed_order_out_turns_back_and_one_back_hands_the_bot_to_its_own_route()
        {
            var (trip, bot) = OnWrongRoad();
            bot.Failure = true;
            trip.Step(bot, 1_500, out _);
            Assert.That(trip.PhaseName, Is.EqualTo("back"));
            bot.Failure = true;
            trip.Step(bot, 2_000, out bool ended);
            Assert.That(ended, Is.True);
            Assert.That(trip.LastResult, Is.EqualTo("own_route"));
        }

        [Test]
        public void A_death_on_a_wrong_road_or_within_60_seconds_after_it_counts_and_a_later_one_does_not()
        {
            var (trip, bot) = OnWrongRoad();
            trip.Step(bot, 1_500, out _);
            bot.IsAlive = false;
            trip.ObserveDeath(bot, 2_000);
            trip.ObserveDeath(bot, 2_500);
            Assert.That(trip.LastResult, Is.EqualTo("died"));
            Assert.That(Summary(), Does.Contain("deaths_60s=1 ").And.Contain("wrong_roads=1"));

            // Ended, then dead 50 s later: counted. Another trip ended, dead 70 s later: not.
            (trip, bot) = OnWrongRoad();
            bot.InGroup = true;
            trip.Step(bot, 1_500, out _);
            bot.IsAlive = false;
            trip.ObserveDeath(bot, 51_500);
            (trip, bot) = OnWrongRoad();
            bot.InGroup = true;
            trip.Step(bot, 1_500, out _);
            bot.IsAlive = false;
            trip.ObserveDeath(bot, 71_500);
            Assert.That(Summary(), Does.Contain("deaths_60s=1 ").And.Contain("wrong_roads=2"));
        }

        [Test]
        public void Nothing_runs_once_a_walk_has_ended()
        {
            var (trip, bot) = OnWrongRoad();
            bot.InGroup = true;
            trip.Step(bot, 1_500, out bool ended);
            Assert.That(ended, Is.True);
            int orders = bot.Orders.Count, looks = bot.ThreatLooks;
            Assert.That(trip.Step(bot, 2_000, out bool again), Is.False);
            Assert.That(again, Is.False);
            Assert.That(bot.Orders.Count, Is.EqualTo(orders));
            Assert.That(bot.ThreatLooks, Is.EqualTo(looks));
        }
    }
}
