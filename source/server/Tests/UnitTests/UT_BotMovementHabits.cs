using System;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people for 1.2, work package 5 (docs/BOTS-AS-PEOPLE-1.2.md, item 5): the pure rules of circle-strafing, facing
    /// the enemy, overshooting, sitting while waiting, crowding the bank, the leg budget and the summary line, with the
    /// shares by skill tier.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotMovementHabits
    {
        private const int Bots = 10_000;

        private static double ShareOf(Func<long, bool> rule) => Enumerable.Range(1, Bots).Count(id => rule(id)) / (double)Bots;

        // ---- the bot's own tier -----------------------------------------------------------------------------------------

        [Test]
        public void A_bots_own_movement_tier_has_the_rvr_shares_and_moves_with_awareness() => Assert.Multiple(() =>
        {
            foreach (var (awareness, shares) in new[]
                     {
                         (RvrBotAwareness.Normal, (30, 50, 20)), (RvrBotAwareness.Easy, (45, 45, 10)), (RvrBotAwareness.Veteran, (15, 45, 40)),
                     })
            {
                var tiers = Enumerable.Range(1, Bots).Select(id => BotMovementHabits.TierOf(id, awareness)).ToArray();
                Assert.That(tiers.Count(t => t == RvrSkillTier.Casual) * 100.0 / Bots, Is.EqualTo(shares.Item1).Within(2), $"{awareness} casual");
                Assert.That(tiers.Count(t => t == RvrSkillTier.Average) * 100.0 / Bots, Is.EqualTo(shares.Item2).Within(2), $"{awareness} average");
                Assert.That(tiers.Count(t => t == RvrSkillTier.Veteran) * 100.0 / Bots, Is.EqualTo(shares.Item3).Within(2), $"{awareness} veteran");
            }
            Assert.That(BotMovementHabits.TierOf(1234, RvrBotAwareness.Normal), Is.EqualTo(BotMovementHabits.TierOf(1234, RvrBotAwareness.Normal)),
                "the same bot, the same tier");
        });

        [Test]
        public void A_bots_movement_tier_does_not_follow_its_rvr_group_tier()
        {
            int same = Enumerable.Range(1, Bots).Count(id =>
                BotMovementHabits.TierOf(id, RvrBotAwareness.Normal) == RvrGroupSkill.For(id, null, RvrBotAwareness.Normal));
            // Independent rolls agree about 0.3^2 + 0.5^2 + 0.2^2 = 38% of the time; the same salt would agree always.
            Assert.That(same / (double)Bots, Is.EqualTo(0.38).Within(0.03));
        }

        [Test]
        public void Rolls_are_fixed_and_spread() => Assert.Multiple(() =>
        {
            Assert.That(BotMovementHabits.Roll(7, 3, 99), Is.EqualTo(BotMovementHabits.Roll(7, 3, 99)));
            var rolls = Enumerable.Range(0, Bots).Select(i => BotMovementHabits.Roll(42, i, 99)).ToArray();
            Assert.That(rolls.All(r => r >= 0 && r < 1));
            Assert.That(rolls.Average(), Is.EqualTo(0.5).Within(0.02));
        });

        // ---- circle-strafe ----------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.20)]
        [TestCase(RvrSkillTier.Average, 0.50)]
        [TestCase(RvrSkillTier.Veteran, 0.85)]
        public void Melee_bots_strafe_their_tiers_share_of_fights(RvrSkillTier tier, double share)
        {
            Assert.That(BotMovementHabits.StrafeShare(tier), Is.EqualTo(share));
            Assert.That(ShareOf(id => BotMovementHabits.StrafesThisFight(tier, id, 5_000 + id % 37)), Is.EqualTo(share).Within(0.02));
            Assert.That(BotMovementHabits.StrafesThisFight(tier, 11, 22), Is.EqualTo(BotMovementHabits.StrafesThisFight(tier, 11, 22)),
                "one roll a fight");
        }

        [Test]
        public void Strafes_come_every_three_to_eight_seconds() => Assert.Multiple(() =>
        {
            Assert.That(BotMovementHabits.NextStrafeDelay(0), Is.EqualTo(3_000));
            Assert.That(BotMovementHabits.NextStrafeDelay(0.999999), Is.EqualTo(8_000).Within(1));
            Assert.That(BotMovementHabits.NextStrafeDelay(-1), Is.EqualTo(3_000));
            Assert.That(BotMovementHabits.NextStrafeDelay(2), Is.LessThanOrEqualTo(8_000));
        });

        [Test]
        public void Only_a_settled_melee_fighter_hitting_an_enemy_strafes() => Assert.Multiple(() =>
        {
            bool Ok(bool melee = true, bool ranged = false, bool attacking = true, bool enemy = true, bool range = true,
                bool moving = false, bool targetMoving = false, bool casting = false, bool stealthed = false, bool held = false) =>
                BotMovementHabits.StrafeAllowed(melee, ranged, attacking, enemy, range, moving, targetMoving, casting, stealthed, held);
            Assert.That(Ok(), Is.True);
            Assert.That(Ok(melee: false), Is.False, "a bow in hand");
            Assert.That(Ok(ranged: true), Is.False, "a caster");
            Assert.That(Ok(attacking: false), Is.False);
            Assert.That(Ok(enemy: false), Is.False, "a monster: the merged circling's job");
            Assert.That(Ok(range: false), Is.False, "not in melee");
            Assert.That(Ok(moving: true), Is.False);
            Assert.That(Ok(targetMoving: true), Is.False, "the target runs: chase, not strafe");
            Assert.That(Ok(casting: true), Is.False, "never while casting");
            Assert.That(Ok(stealthed: true), Is.False, "never stealthed");
            Assert.That(Ok(held: true), Is.False, "stunned, mezzed or rooted");
        });

        [Test]
        public void A_strafe_is_one_to_three_short_legs_around_the_target() => Assert.Multiple(() =>
        {
            Vector2 target = new(1000, 1000);
            foreach (float radius in new[] { 30f, 60f, 120f })
                foreach (double roll in new[] { 0.0, 0.5, 0.999 })
                {
                    float sweep = BotMovementHabits.StrafeSweep(radius, roll);
                    Assert.That(sweep, Is.GreaterThan(0).And.LessThanOrEqualTo(BotMovementHabits.StrafeMaxSweepDegrees));
                    Vector2 start = target + new Vector2(0, radius);
                    Vector2[] legs = BotCombatMovement.ArcLegs(target, radius, BotCombatMovement.Heading(target, start), 1, sweep);
                    Assert.That(legs.Length, Is.InRange(1, 3));
                    Vector2 at = start;
                    foreach (Vector2 leg in legs)
                    {
                        Assert.That(Vector2.Distance(at, leg), Is.LessThanOrEqualTo(BotMovementHabits.LegLength + 0.01f),
                            "a leg under 25 units keeps the bot facing its target");
                        Assert.That(Vector2.Distance(target, leg), Is.EqualTo(radius).Within(0.5f), "around, not away");
                        at = leg;
                    }
                }
            Assert.That(BotMovementHabits.StrafeSweep(0, 0.5), Is.Zero);
        });

        // ---- face the enemy ---------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.30)]
        [TestCase(RvrSkillTier.Average, 0.60)]
        [TestCase(RvrSkillTier.Veteran, 0.90)]
        public void Casters_and_healers_face_their_tiers_share_of_melee_contacts(RvrSkillTier tier, double share)
        {
            Assert.That(BotMovementHabits.FaceShare(tier), Is.EqualTo(share));
            Assert.That(ShareOf(id => BotMovementHabits.FacesThisContact(tier, id, 1 + id % 5)), Is.EqualTo(share).Within(0.02));
        }

        [Test]
        public void A_back_off_is_an_arc_of_short_legs_away_from_the_attacker() => Assert.Multiple(() =>
        {
            Vector2 self = new(0, 0), attacker = new(0, -40);
            foreach (Vector2? refuge in new Vector2?[] { null, new Vector2(300, 100), new Vector2(-300, 100) })
            {
                Vector2[] legs = BotMovementHabits.BackOffLegs(self, attacker, refuge, 0.3);
                Assert.That(legs.Length, Is.EqualTo(BotMovementHabits.FaceLegs));
                Vector2 at = self;
                float lastFrom = Vector2.Distance(self, attacker);
                foreach (Vector2 leg in legs)
                {
                    Assert.That(Vector2.Distance(at, leg), Is.EqualTo(BotMovementHabits.FaceLegLength).Within(0.01f));
                    Assert.That(Vector2.Distance(at, leg), Is.LessThan(25), "never a leg the server turns the bot for");
                    float from = Vector2.Distance(leg, attacker);
                    Assert.That(from, Is.GreaterThan(lastFrom), "every leg takes it farther from the attacker");
                    lastFrom = from;
                    at = leg;
                }
                // An arc: the end is off the straight line away from the attacker.
                Assert.That(MathF.Abs(legs[^1].X), Is.GreaterThan(5), "bent, not a straight run");
                if (refuge is Vector2 r)
                    Assert.That(MathF.Sign(legs[^1].X), Is.EqualTo(MathF.Sign(r.X)), "bent toward the guard or the tanks' line");
            }
            Assert.That(BotMovementHabits.BackOffLegs(self, self, null, 0.5), Is.Empty, "no direction, no back-off");
        });

        [Test]
        public void A_caster_faces_again_after_three_to_five_seconds()
        {
            Assert.That(BotMovementHabits.NextFaceDelay(0), Is.EqualTo(3_000));
            Assert.That(BotMovementHabits.NextFaceDelay(0.999999), Is.EqualTo(5_000).Within(1));
        }

        // ---- overshoot --------------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.25)]
        [TestCase(RvrSkillTier.Average, 0.15)]
        [TestCase(RvrSkillTier.Veteran, 0.08)]
        public void Arrivals_overshoot_at_the_tiers_share(RvrSkillTier tier, double share)
        {
            Assert.That(BotMovementHabits.OvershootShare(tier), Is.EqualTo(share));
            Assert.That(ShareOf(id => BotMovementHabits.Overshoots(tier, id, id * 3, 400)), Is.EqualTo(share).Within(0.02));
            Assert.That(ShareOf(id => BotMovementHabits.Overshoots(tier, id, id * 3, 100)), Is.Zero, "a short shuffle never overshoots");
        }

        [Test]
        public void An_overshoot_runs_thirty_to_sixty_past_on_the_line_it_came_along() => Assert.Multiple(() =>
        {
            Assert.That(BotMovementHabits.OvershootDistance(0), Is.EqualTo(30));
            Assert.That(BotMovementHabits.OvershootDistance(0.999999), Is.EqualTo(60).Within(0.01));
            Vector2 from = new(0, 0), spot = new(300, 400);
            Vector2 past = BotMovementHabits.OvershootSpot(from, spot, 50);
            Assert.That(Vector2.Distance(spot, past), Is.EqualTo(50).Within(0.01));
            Assert.That(Vector2.Distance(from, past), Is.EqualTo(550).Within(0.01), "past the spot, not beside it");
            Assert.That(BotMovementHabits.OvershootSpot(spot, spot, 50), Is.EqualTo(spot));
            Assert.That(BotMovementHabits.StepBackDelay(0), Is.EqualTo(150));
            Assert.That(BotMovementHabits.StepBackDelay(0.999999), Is.EqualTo(600).Within(1));
        });

        [Test]
        public void A_walk_is_cut_into_legs_of_at_most_22() => Assert.Multiple(() =>
        {
            Vector2[] legs = BotMovementHabits.Legs(new(0, 0), new(60, 0));
            Assert.That(legs.Length, Is.EqualTo(3));
            Assert.That(legs[^1].X, Is.EqualTo(60).Within(0.01));
            Assert.That(legs.Zip(legs.Skip(1)).All(pair => Vector2.Distance(pair.First, pair.Second) <= 22.01f));
            Assert.That(BotMovementHabits.Legs(new(0, 0), new(10, 0)).Length, Is.EqualTo(1));
            Assert.That(BotMovementHabits.Legs(new(0, 0), new(500, 0)).Length, Is.EqualTo(3), "a longer way is cut short");
            Assert.That(BotMovementHabits.Legs(new(0, 0), new(500, 0))[^1].X, Is.EqualTo(66).Within(0.01));
            Assert.That(BotMovementHabits.Legs(new(5, 5), new(5, 5)), Is.Empty);
        });

        // ---- sitting while waiting --------------------------------------------------------------------------------------

        [Test]
        public void Sixty_percent_of_waits_sit_after_thirty_to_fifty_seconds() => Assert.Multiple(() =>
        {
            Assert.That(ShareOf(id => BotMovementHabits.SitsThisWait(id, 1)), Is.EqualTo(0.60).Within(0.02));
            Assert.That(ShareOf(id => BotMovementHabits.SitsThisWait(77, id)), Is.EqualTo(0.60).Within(0.02), "a roll each wait");
            var delays = Enumerable.Range(1, Bots).Select(id => BotMovementHabits.SitDelay(id, 2)).ToArray();
            Assert.That(delays.Min(), Is.GreaterThanOrEqualTo(30_000));
            Assert.That(delays.Max(), Is.LessThan(50_000));
            Assert.That(delays.Average(), Is.EqualTo(40_000).Within(600), "spread over the twenty seconds");
            Assert.That(BotMovementHabits.SitDue(29_999, 30_000), Is.False);
            Assert.That(BotMovementHabits.SitDue(30_000, 30_000), Is.True);
        });

        [Test]
        public void A_bot_never_sits_while_it_moves_casts_fights_hides_or_rides() => Assert.Multiple(() =>
        {
            bool Ok(bool alive = true, bool moving = false, bool casting = false, bool stealthed = false, bool combat = false,
                bool attacking = false, bool aggro = false, bool held = false, bool resting = false, bool ride = false) =>
                BotMovementHabits.MaySit(alive, moving, casting, stealthed, combat, attacking, aggro, held, resting, ride);
            Assert.That(Ok(), Is.True);
            Assert.That(Ok(alive: false), Is.False);
            Assert.That(Ok(moving: true), Is.False);
            Assert.That(Ok(casting: true), Is.False, "never while casting");
            Assert.That(Ok(stealthed: true), Is.False, "never stealthed");
            Assert.That(Ok(combat: true), Is.False);
            Assert.That(Ok(attacking: true), Is.False);
            Assert.That(Ok(aggro: true), Is.False, "never holding aggro");
            Assert.That(Ok(held: true), Is.False);
            Assert.That(Ok(resting: true), Is.False, "resting already sits, and its timing is not changed");
            Assert.That(Ok(ride: true), Is.False, "never on a stable ride");
        });

        // ---- the bank ---------------------------------------------------------------------------------------------------

        [Test]
        public void A_crowd_choice_leans_to_the_bank_once_four_stand_there() => Assert.Multiple(() =>
        {
            var crowd = AutonomousTownHangouts.Kind.Crowd;
            Assert.That(BotMovementHabits.LeanToBank(crowd, 3, true), Is.EqualTo(crowd));
            Assert.That(BotMovementHabits.LeanToBank(crowd, 4, true), Is.EqualTo(AutonomousTownHangouts.Kind.Bank));
            Assert.That(BotMovementHabits.LeanToBank(crowd, 9, false), Is.EqualTo(crowd), "no vault keeper in this town");
            foreach (var other in new[] { AutonomousTownHangouts.Kind.Merchant, AutonomousTownHangouts.Kind.Trainer,
                         AutonomousTownHangouts.Kind.Station, AutonomousTownHangouts.Kind.Bank })
                Assert.That(BotMovementHabits.LeanToBank(other, 9, true), Is.EqualTo(other), "other choices are kept: the mix stays");
        });

        // ---- the budget and the log -------------------------------------------------------------------------------------

        [Test]
        public void At_most_forty_leg_checks_a_second_and_none_while_the_loop_is_busy() => Assert.Multiple(() =>
        {
            long second = -1;
            int used = 0;
            int taken = 0;
            for (int i = 0; i < 50; i++)
                if (BotMovementHabits.TakeBudget(ref second, ref used, 10_100, 1))
                    taken++;
            Assert.That(taken, Is.EqualTo(BotMovementHabits.LegChecksPerSecond));
            Assert.That(BotMovementHabits.TakeBudget(ref second, ref used, 10_999, 1), Is.False, "the same second");
            Assert.That(BotMovementHabits.TakeBudget(ref second, ref used, 11_000, 3), Is.True, "a new second");
            Assert.That(used, Is.EqualTo(3));
            Assert.That(BotMovementHabits.TakeBudget(ref second, ref used, 11_000, 38), Is.False, "a walk takes all its legs or none");
            Assert.That(BotMovementHabits.TakeBudget(ref second, ref used, 11_000, 0), Is.False);
            Assert.That(BotMovementHabits.LoopBusy(49.9), Is.False);
            Assert.That(BotMovementHabits.LoopBusy(50), Is.True);
        });

        [Test]
        public void The_summary_line_names_every_count_and_events_are_rate_limited() => Assert.Multiple(() =>
        {
            string line = BotMovementHabits.SummaryLine(300, [("strafes", 4), ("sits", 0)]);
            Assert.That(line, Is.EqualTo("BOT_MOVE_HABITS seconds=300 strafes=4 sits=0"));
            Assert.That(BotMovementHabits.ShouldLog(20_000, 10_001), Is.False);
            Assert.That(BotMovementHabits.ShouldLog(20_000, 10_000), Is.True);
            var counts = BotMovementHabitsRuntime.TakeCounts();
            Assert.That(counts.Select(c => c.Name), Is.Unique);
            Assert.That(counts.Select(c => c.Name), Does.Contain("refusedLegs").And.Contain("wallSits").And.Contain("stickLost")
                .And.Contain("overshoots").And.Contain("strafes").And.Contain("bankLeans"));
            Assert.That(counts.Length, Is.EqualTo(Enum.GetValues<BotMovementHabitsRuntime.Counter>().Length));
        });

        [Test]
        public void The_package_registers_its_summary_and_lstest_wall()
        {
            try
            {
                BotMovementHabitsRuntime.RegisterSeams();
                Assert.That(BotPeopleSummary.Tags, Does.Contain("BOT_MOVE_HABITS"));
                Assert.That(BotPeopleTest.IsBuilt("wall"), Is.True);
                Assert.That(BotPeopleSummary.TakeLines(300).Single(l => l.StartsWith("BOT_MOVE_HABITS")),
                    Does.StartWith("BOT_MOVE_HABITS seconds=300 strafeFights="));
            }
            finally
            {
                BotPeopleSummary.Unregister("BOT_MOVE_HABITS");
                BotPeopleTest.Unregister("wall");
            }
        }

        // ---- the switches and the scope ---------------------------------------------------------------------------------

        [Test]
        public void The_master_switch_turns_every_habit_off_and_the_scope_refuses_the_players_group() => Assert.Multiple(() =>
        {
            var off = BotPeopleSwitches.From(true, true, true, true, true, true, true, false, true, true, true, true);
            Assert.That(off.MoveStrafe || off.MoveStickLine || off.MoveSitAndWalls, Is.False);
            var on = BotPeopleSwitches.AllOn;
            Assert.That(on.MovementHabits && on.MoveStrafe && on.MoveStickLine && on.MoveSitAndWalls, Is.True);
            var free = BotPeopleFacts.FreeGamebot;
            Assert.That(BotPeopleScope.Refusal(free, BotPeopleRule.Movement), Is.EqualTo(BotPeopleRefusal.None));
            Assert.That(BotPeopleScope.Refusal(free with { Rvr = free.Rvr with { PlayerInGroup = true } }, BotPeopleRule.Movement),
                Is.EqualTo(BotPeopleRefusal.PlayerGroup), "never in a group with a player");
            Assert.That(BotPeopleScope.Refusal(free with { StableRide = true }, BotPeopleRule.Movement), Is.EqualTo(BotPeopleRefusal.StableRide));
            Assert.That(BotPeopleScope.Refusal(free with { Rvr = free.Rvr with { AtObjective = true } }, BotPeopleRule.Movement),
                Is.EqualTo(BotPeopleRefusal.AtObjective), "never in a fight at a keep");
            Assert.That(BotPeopleScope.Refusal(free with { InRaidDungeon = true }, BotPeopleRule.Movement), Is.EqualTo(BotPeopleRefusal.DungeonRaid));
        });

        // ---- hook points (the fixes of the WP5 review) --------------------------------------------------------------

        [Test]
        public void An_overshoot_always_clears_the_hangout_arrival_tolerance() => Assert.Multiple(() =>
        {
            Assert.That(BotMovementHabits.OvershootMinimum, Is.GreaterThan(AutonomousTownHangouts.ArriveTolerance),
                "the end of an overshoot never counts as arrived at the spot");
            for (int i = 0; i < 100; i++)
                Assert.That(BotMovementHabits.OvershootDistance(i / 100.0), Is.GreaterThan(AutonomousTownHangouts.ArriveTolerance));
        });

        [Test]
        public void The_town_routing_walks_on_past_steps_back_once_and_never_routes_over_the_step_back() => Assert.Multiple(() =>
        {
            var step = BotMovementHabits.HangoutStep.WalkToSpot;
            // No overshoot: today's walk.
            Assert.That(BotMovementHabits.Hangout(false, false, false, true, false), Is.EqualTo(step));
            Assert.That(BotMovementHabits.Hangout(true, false, false, false, false), Is.EqualTo(BotMovementHabits.HangoutStep.Arrived));
            // Pending: passing through the spot does not count, and a stop short of the spot past it walks on.
            Assert.That(BotMovementHabits.Hangout(true, true, false, true, false), Is.EqualTo(BotMovementHabits.HangoutStep.WalkPast));
            Assert.That(BotMovementHabits.Hangout(false, true, false, false, false), Is.EqualTo(BotMovementHabits.HangoutStep.WalkPast));
            Assert.That(BotMovementHabits.Hangout(false, true, false, true, false), Is.EqualTo(BotMovementHabits.HangoutStep.WalkPast));
            // At the spot past it: step back.
            Assert.That(BotMovementHabits.Hangout(false, true, true, false, false), Is.EqualTo(BotMovementHabits.HangoutStep.StepBack));
            // The step-back's legs own the walk (no IssuePath over them) until it stands at the spot.
            Assert.That(BotMovementHabits.Hangout(false, false, false, true, true), Is.EqualTo(BotMovementHabits.HangoutStep.SteppingBack));
            Assert.That(BotMovementHabits.Hangout(false, true, false, true, true), Is.EqualTo(BotMovementHabits.HangoutStep.SteppingBack),
                "a step-back the watcher started is not routed back past");
            Assert.That(BotMovementHabits.Hangout(true, false, false, true, true), Is.EqualTo(BotMovementHabits.HangoutStep.SteppingBack));
            Assert.That(BotMovementHabits.Hangout(true, false, false, false, true), Is.EqualTo(BotMovementHabits.HangoutStep.Arrived));
            // A stopped step-back short of the spot: walk to the spot as before.
            Assert.That(BotMovementHabits.Hangout(false, false, false, false, false), Is.EqualTo(step));
        });

        [Test]
        public void A_healers_face_back_keeps_its_kites_run() => Assert.Multiple(() =>
        {
            Vector2 self = new(0, 0);
            foreach (float run in new[] { 220f, 275f, 330f })
                foreach (double roll in new[] { 0.2, 0.8 })
                {
                    Vector2 spot = new(run * 0.6f, run * 0.8f);
                    Vector2[] legs = BotMovementHabits.ArcLegsTo(self, spot, roll);
                    Assert.That(legs.Length, Is.LessThanOrEqualTo(BotMovementHabits.FaceMaxLegs));
                    Assert.That(Vector2.Distance(legs[^1], spot), Is.LessThan(0.01f), "it ends where its kite would have ended");
                    Vector2 at = self;
                    foreach (Vector2 leg in legs)
                    {
                        Assert.That(Vector2.Distance(at, leg), Is.LessThanOrEqualTo(BotMovementHabits.LegLength),
                            "short legs: it keeps facing the attacker");
                        at = leg;
                    }
                    Vector2 middle = legs[legs.Length / 2];
                    Vector2 straight = spot * ((legs.Length / 2 + 1) / (float)legs.Length);
                    Assert.That(Vector2.Distance(middle, straight), Is.GreaterThan(5), "an arc, not a straight run");
                }
            Assert.That(BotMovementHabits.FaceWalkTaken(16, 18, 270, 300, kite: true), Is.True);
            Assert.That(BotMovementHabits.FaceWalkTaken(10, 18, 200, 300, kite: true), Is.False,
                "blocked short of 80% of the run: it kites as today instead");
            Assert.That(BotMovementHabits.FaceWalkTaken(1, 4, 20, 80, kite: false), Is.False, "a caster backed into a wall");
            Assert.That(BotMovementHabits.FaceWalkTaken(2, 4, 40, 80, kite: false), Is.True);
        });

        [Test]
        public void A_bot_has_one_tier_its_profile_once_profiles_exist() => Assert.Multiple(() =>
        {
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                Assert.That(BotMovementHabits.TierFor(tier, 123, RvrBotAwareness.Normal), Is.EqualTo(tier));
            Assert.That(BotMovementHabits.TierFor(null, 123, RvrBotAwareness.Normal),
                Is.EqualTo(BotMovementHabits.TierOf(123, RvrBotAwareness.Normal)), "no profile: the skill profile's start roll (WP7)");
            Assert.That(BotMovementHabitsRuntime.ProfileTier, Is.Not.Null, "wired to the skill profiles (WP1): one tier per bot");
        });

        [Test]
        public void Only_formation_members_take_the_walls_the_wait_sites_only_sit() => Assert.Multiple(() =>
        {
            Assert.That(BotMovementHabitsRuntime.OffersWall(BotMovementHabitsRuntime.WaitSpot.Formation), Is.True);
            Assert.That(BotMovementHabitsRuntime.OffersWall(BotMovementHabitsRuntime.WaitSpot.Group), Is.False,
                "a wait site stops its bot every turn and a rendezvous pulls it back to its slot");
            Assert.That(BotMovementHabitsRuntime.OffersWall(BotMovementHabitsRuntime.WaitSpot.Bank), Is.False);
        });
    }
}
