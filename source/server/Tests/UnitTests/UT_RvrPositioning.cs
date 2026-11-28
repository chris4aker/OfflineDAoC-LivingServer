using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using static DOL.UnitTests.UT_RvrPeelPlan;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 3 (docs/RVR-FIELD-1.2.md "Hands"): where a bot-only group stands (RvrPositioning):
    /// backline behind the tanks for any enemy bearing, healers more than 350 apart, scatter on incoming, kiting on melee
    /// contact, the tiers' errors, and the scope guard of the brain's hooks. Pure apart from the last tests, which ask the
    /// hooks with world-less bots.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrPositioning : DutyLeaseFixture
    {
        private static readonly double[] Bearings = Enumerable.Range(0, 8).Select(i => i * Math.PI / 4 + 0.1).ToArray();

        /// <summary>A group at the origin facing an enemy 1,800 units away along <paramref name="bearing"/>: two tanks and a
        /// mercenary 150 ahead, the backline classes given standing loosely around the origin.</summary>
        private static (List<RvrPositioning.Placed> Members, Vector2 Enemy) Group(double bearing, params eCharacterClass[] back)
        {
            Vector2 front = RvrPositioning.Direction(bearing), side = RvrPositioning.Direction(bearing + Math.PI / 2);
            var members = new List<RvrPositioning.Placed>();
            void Add(eCharacterClass characterClass, Vector2 at)
            {
                RvrCombatant member = C(Gamebot(), characterClass, (int)at.X, (int)at.Y);
                members.Add(new(member, RvrPositioning.StandFor(member.Role, member.Class)));
            }
            Add(eCharacterClass.Armsman, front * 150 + side * 60);
            Add(eCharacterClass.Paladin, front * 150 - side * 60);
            Add(eCharacterClass.Mercenary, front * 160);
            for (int i = 0; i < back.Length; i++)
                Add(back[i], side * (i * 40 - 60) + front * (i % 2 * 30));
            return (members, front * 1_800);
        }

        private static readonly eCharacterClass[][] Backlines =
        [
            [eCharacterClass.Cleric, eCharacterClass.Cleric, eCharacterClass.Sorcerer, eCharacterClass.Theurgist],
            [eCharacterClass.Cleric, eCharacterClass.Cleric, eCharacterClass.Wizard],
            [eCharacterClass.Healer, eCharacterClass.Healer, eCharacterClass.Shaman, eCharacterClass.Runemaster, eCharacterClass.Spiritmaster],
            [eCharacterClass.Druid, eCharacterClass.Druid, eCharacterClass.Bard, eCharacterClass.Eldritch],
            [eCharacterClass.Cleric],
        ];

        private static Vector2? LineOf(List<RvrPositioning.Placed> members) =>
            RvrPositioning.Line(members.Select(m => (RvrPeelPlan.At(m.Member), m.Stand)).ToList());

        [Test]
        public void Backline_slots_stand_behind_the_tanks_for_every_enemy_bearing()
        {
            foreach (double bearing in Bearings)
                foreach (eCharacterClass[] back in Backlines)
                {
                    (List<RvrPositioning.Placed> members, Vector2 enemy) = Group(bearing, back);
                    Vector2 line = LineOf(members)!.Value;
                    double front = RvrPositioning.Bearing(line, enemy);
                    float tanksAhead = members.Where(m => m.Stand == RvrStand.Front)
                        .Min(m => RvrPositioning.Ahead(RvrPeelPlan.At(m.Member), line, front));
                    foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                    {
                        ImmutableArray<RvrSlot> exact = RvrPositioning.Layout(members, enemy, tier, false, 50_000, errors: false);
                        ImmutableArray<RvrSlot> human = RvrPositioning.Layout(members, enemy, tier, false, 50_000);
                        Assert.That(exact.Count(s => s.Stand == RvrStand.Back), Is.EqualTo(back.Length));
                        foreach (RvrSlot slot in exact.Where(s => s.Stand == RvrStand.Back))
                            Assert.That(RvrPositioning.Ahead(slot.Spot, line, front),
                                Is.LessThan(-RvrPositioning.MinLineDepth).And.LessThan(tanksAhead),
                                $"bearing {bearing:F2}, {string.Join(",", back)}: behind the tanks' line");
                        foreach (RvrSlot slot in human.Where(s => s.Stand == RvrStand.Back && (s.Error & RvrSlotError.Charge) == 0))
                            Assert.That(RvrPositioning.Ahead(slot.Spot, line, front), Is.LessThan(0), "ragged, but still behind");
                        Assert.That(human.Where(s => s.Stand != RvrStand.Back).Select(s => s.Spot),
                            Is.EqualTo(members.Where(m => m.Stand != RvrStand.Back).Select(m => RvrPeelPlan.At(m.Member))),
                            "tanks and melee are not moved");
                    }
                }
        }

        // ---- #531 step 2e: caster depth from spell reach, and the healer's cover pick ----------------------------------

        [Test]
        public void Depth_follows_the_spell_reach() => Assert.Multiple(() =>
        {
            const float radius = 400;
            Assert.That(RvrPositioning.DepthFor(false, 0, 0, radius), Is.EqualTo(radius), "no reach: the layout's depth, as before");
            Assert.That(RvrPositioning.DepthFor(false, 1_500, 0, radius), Is.EqualTo(1_350), "a nuker in contact: 1,500 less the margin");
            Assert.That(RvrPositioning.DepthFor(false, 1_000, 0, radius), Is.EqualTo(850), "a shorter reach stands closer");
            Assert.That(RvrPositioning.DepthFor(false, 1_875, 0, radius), Is.EqualTo(RvrPositioning.MaxCasterDepth), "a bolt reach is capped");
            Assert.That(RvrPositioning.DepthFor(false, 1_500, 600, radius), Is.EqualTo(750), "the enemy 600 ahead of the line: 600 less deep");
            Assert.That(RvrPositioning.DepthFor(false, 1_500, 1_400, radius), Is.EqualTo(radius), "enemy far ahead: never less than the spread depth");
            Assert.That(RvrPositioning.DepthFor(false, 1_500, -300, radius), Is.EqualTo(1_350), "an enemy behind the line counts as in contact");
            Assert.That(RvrPositioning.DepthFor(true, 2_000, 0, radius), Is.EqualTo(RvrPositioning.HealerStandOff), "a healer 900 from the enemy");
            Assert.That(RvrPositioning.DepthFor(true, 700, 0, radius), Is.EqualTo(550), "a healer stays within its heal's reach of the line");
            Assert.That(RvrPositioning.DepthFor(true, 2_000, 700, radius), Is.EqualTo(radius), "a healer near a far line keeps the spread depth");
        });

        [Test]
        public void Layout_puts_a_caster_at_its_reach_behind_the_line_and_keeps_the_spread()
        {
            foreach (double bearing in Bearings)
            {
                (List<RvrPositioning.Placed> members, Vector2 enemy) =
                    Group(bearing, eCharacterClass.Cleric, eCharacterClass.Cleric, eCharacterClass.Sorcerer, eCharacterClass.Theurgist);
                Vector2 line = LineOf(members)!.Value;
                double front = RvrPositioning.Bearing(line, enemy);
                // the enemy in contact: just ahead of the tanks
                Vector2 contact = line + RvrPositioning.Direction(front) * 100;
                List<RvrPositioning.Placed> reached = members.Select(m => m.Stand != RvrStand.Back ? m
                    : m with { Reach = RvrPositioning.CountsAsHealer(m.Member.Role, m.Member.Class) ? 2_000 : 1_500 }).ToList();
                ImmutableArray<RvrSlot> before = RvrPositioning.Layout(members, contact, RvrSkillTier.Veteran, false, 50_000, errors: false);
                ImmutableArray<RvrSlot> after = RvrPositioning.Layout(reached, contact, RvrSkillTier.Veteran, false, 50_000, errors: false);
                float Depth(RvrSlot slot) => -RvrPositioning.Ahead(slot.Spot, line, front);
                Assert.That(before.Where(s => s.Stand == RvrStand.Back).Max(Depth), Is.LessThanOrEqualTo(RvrPositioning.MaxBackDepth + 1),
                    "without reach: 250-700, as before");
                foreach (RvrSlot slot in after.Where(s => s.Stand == RvrStand.Back))
                {
                    if (slot.Healer)
                        Assert.That(Vector2.Distance(slot.Spot, contact), Is.InRange(RvrPositioning.HealerStandOff - 200, RvrPositioning.HealerStandOff + 120),
                            $"bearing {bearing:F2}: a healer about 900 from the enemy");
                    else
                        Assert.That(Vector2.Distance(slot.Spot, contact), Is.InRange(1_100, 1_500),
                            $"bearing {bearing:F2}: a caster 1,200-1,500 from the enemy, within its reach");
                    Assert.That(RvrPositioning.Ahead(slot.Spot, line, front), Is.LessThan(-RvrPositioning.MinLineDepth), "still behind the line");
                }
                RvrSlot[] all = after.Where(s => s.Stand == RvrStand.Back).ToArray();
                for (int i = 0; i < all.Length; i++)
                    for (int j = i + 1; j < all.Length; j++)
                        Assert.That(Vector2.Distance(all[i].Spot, all[j].Spot), Is.GreaterThan(RvrPositioning.MezSpread), "still spread");
            }
        }

        [Test]
        public void Cover_tries_its_own_place_first_then_beside_it_at_the_same_depth()
        {
            Vector2 line = new(100, 100), spot = new(100, -800);
            Vector2[] candidates = RvrPositioning.CoverCandidates(spot, line);
            Assert.Multiple(() =>
            {
                Assert.That(candidates, Has.Length.EqualTo(5));
                Assert.That(candidates[0], Is.EqualTo(spot), "its own place first");
                foreach (Vector2 c in candidates)
                    Assert.That(Vector2.Distance(c, line), Is.EqualTo(900).Within(0.5), "the same depth");
                Assert.That(Vector2.Distance(candidates[1], spot), Is.LessThan(Vector2.Distance(candidates[3], spot)), "nearest first");
                Assert.That(RvrPositioning.PickCover([(true, true), (true, false), (true, false)]), Is.EqualTo(1), "the first hidden one");
                Assert.That(RvrPositioning.PickCover([(true, true), (false, false), (true, true)]), Is.EqualTo(0),
                    "a place that can't see the line is no cover; none hides: its own place");
                Assert.That(RvrPositioning.PickCover([(true, false)]), Is.EqualTo(0), "already hidden");
                Assert.That(RvrPositioning.PickCover(null), Is.EqualTo(0));
            });
        }

        [Test]
        public void Cover_skips_a_place_inside_another_back_liners_spread()
        {
            Vector2 at = new(0, 0);
            Assert.Multiple(() =>
            {
                Assert.That(RvrPositioning.CoverKeepsSpread(at, false, null), Is.True, "nobody else");
                Assert.That(RvrPositioning.CoverKeepsSpread(at, false, [(new Vector2(400, 0), false)]), Is.True, "a caster 400 off");
                Assert.That(RvrPositioning.CoverKeepsSpread(at, false, [(new Vector2(340, 0), false)]), Is.False, "a caster inside the mez spread");
                Assert.That(RvrPositioning.CoverKeepsSpread(at, false, [(new Vector2(355, 0), true)]), Is.False, "a healer inside mez spread + 10");
                Assert.That(RvrPositioning.CoverKeepsSpread(at, false, [(new Vector2(361, 0), true)]), Is.True, "a healer just outside");
                Assert.That(RvrPositioning.CoverKeepsSpread(at, true, [(new Vector2(400, 0), false)]), Is.False, "incoming: the scatter spread");
            });
            // Whatever CoverKeepsSpread allows, NeedsMove does not send the healer off again for the spread.
            var slot = new RvrSlot(null, RvrStand.Back, new Vector2(0, -200), true, RvrSlotError.None); // walking to it
            Assert.That(RvrPositioning.NeedsMove(at, slot, null, 0, false, [(new Vector2(340, 0), true)]), Is.EqualTo(RvrMoveReason.Spread),
                "the check below can fail");
            foreach (bool incoming in new[] { false, true })
                foreach (float d in new[] { 361f, 400f, 451f, 600f })
                {
                    (Vector2, bool)[] others = [(new Vector2(d, 0), true)];
                    if (RvrPositioning.CoverKeepsSpread(at, incoming, others))
                        Assert.That(RvrPositioning.NeedsMove(at, slot, null, 0, incoming, others), Is.EqualTo(RvrMoveReason.None),
                            $"kept the spread at {d} (incoming {incoming})");
                }
        }

        [Test]
        public void Cover_never_picks_a_place_that_NeedsMove_calls_Far()
        {
            // A contact healer at 900 plus Casual's depth jitter (up to +120): the 24-degree places are a chord of about
            // 0.416 x depth from its own, past FarFromSlot less SlotTolerance, so the pick must skip them.
            Vector2 line = new(0, 0);
            Vector3 caster = new(0, 2000, 0);
            double front = Math.PI / 2; // the enemy is toward +Y
            float hold = RvrPositioning.FarFromSlot - RvrPositioning.SlotTolerance;
            Vector2[] offsets = [Vector2.Zero, new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(0.7071f, 0.7071f), new(-0.7071f, 0.7071f)];
            foreach (float depth in new[] { 600f, 800f, 900f, 960f, 1020f })
            {
                Vector2 spot = new(0, -depth);
                Vector2[] candidates = RvrPositioning.CoverCandidates(spot, line);
                var slot = new RvrSlot(null, RvrStand.Back, spot, true, RvrSlotError.None);
                // Every place beside its own is hidden from the caster, so the pick returns whichever it may take first,
                // and with the earlier ones held by other healers, each later one in turn.
                for (int skip = 0; skip < candidates.Length; skip++)
                {
                    (Vector2, bool)[] others = candidates.Skip(1).Take(skip).Select(c => (c, true)).ToArray();
                    Vector2 picked = BotBrain.RvrCoverPick(
                        new DOL.GS.Tests.Rehearsal.FlatNavmesh(1)
                        {
                            SightBlocked = (from, to) => Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(caster.X, caster.Y)) < 1
                                && Vector2.Distance(new Vector2(to.X, to.Y), spot) >= 1
                        },
                        null, caster, 0, spot, line, false, others);
                    Assert.That(RvrPositioning.CoverNearSlot(picked, spot), Is.True, $"depth {depth}, skip {skip}: near its own place");
                    foreach (Vector2 off in offsets)
                    {
                        Vector2 standing = picked + off * RvrPositioning.SlotTolerance;
                        Assert.That(RvrPositioning.NeedsMove(standing, slot, line, front, false, null), Is.Not.EqualTo(RvrMoveReason.Far),
                            $"depth {depth}, skip {skip}: stopped {off} x SlotTolerance off its cover place");
                    }
                }
                Assert.That(Vector2.Distance(candidates[3], spot) > hold, Is.EqualTo(depth >= 900f),
                    $"depth {depth}: the 24-degree place is skipped exactly when it is too far");
            }
            Assert.That(RvrPositioning.CoverNearSlot(new Vector2(hold, 0), Vector2.Zero), Is.True, "the limit itself");
            Assert.That(RvrPositioning.CoverNearSlot(new Vector2(hold + 1, 0), Vector2.Zero), Is.False, "just past it");
            // The check can fail: a 24-degree place at 1,020 stood on is Far.
            Vector2 farSpot = new(0, -1020);
            Vector2 farCandidate = RvrPositioning.CoverCandidates(farSpot, line)[3];
            Vector2 overshoot = farCandidate + Vector2.Normalize(farCandidate - farSpot) * RvrPositioning.SlotTolerance;
            Assert.That(RvrPositioning.NeedsMove(overshoot, new RvrSlot(null, RvrStand.Back, farSpot, true, RvrSlotError.None), line, front,
                false, null), Is.EqualTo(RvrMoveReason.Far), "without the skip the healer would be sent back");
        }

        [Test]
        public void Cover_skips_a_lone_hidden_24_degree_place_past_the_hold_limit()
        {
            // The live case: no other healers near, the 12-degree places in the caster's sight, and only one 24-degree
            // place hidden behind a rock. Past 340 from its own place the pick must keep the healer's own spot; at 800 the
            // place is within the limit and is taken, so the skip turns on at the limit, not everywhere.
            Vector2 line = new(0, 0);
            Vector3 caster = new(0, 2000, 0);
            foreach (int hidden in new[] { 3, 4 })
                foreach (float depth in new[] { 800f, 900f, 960f, 1020f })
                {
                    Vector2 spot = new(0, -depth);
                    Vector2[] candidates = RvrPositioning.CoverCandidates(spot, line);
                    Vector2 rock = candidates[hidden];
                    Vector2 picked = BotBrain.RvrCoverPick(
                        new DOL.GS.Tests.Rehearsal.FlatNavmesh(1)
                        {
                            SightBlocked = (from, to) => Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(caster.X, caster.Y)) < 1
                                && Vector2.Distance(new Vector2(to.X, to.Y), rock) < 1
                        },
                        null, caster, 0, spot, line, false, []);
                    Assert.That(picked, Is.EqualTo(depth >= 900f ? spot : rock),
                        $"depth {depth}, only candidate {hidden} hidden: {(depth >= 900f ? "too far to hold, keeps its own spot" : "within the limit, takes the rock")}");
                }
        }

        [Test]
        public void Cover_pick_walks_to_the_first_hidden_place_beside_its_own()
        {
            Vector2 line = new(0, 0), spot = new(0, -900);
            Vector3 caster = new(0, 1800, 0);
            Vector2[] candidates = RvrPositioning.CoverCandidates(spot, line);
            static bool Near(Vector3 a, Vector2 b) => Vector2.Distance(new Vector2(a.X, a.Y), b) < 1;
            Vector2 Pick(Func<Vector3, Vector3, bool> blocked, IReadOnlyList<(Vector2, bool)> others) =>
                BotBrain.RvrCoverPick(new DOL.GS.Tests.Rehearsal.FlatNavmesh(1) { SightBlocked = blocked }, null, caster, 0, spot, line,
                    false, others);
            // A rock hides the healer's first place beside its own from the caster; its own place is in the open.
            bool HidesFirst(Vector3 from, Vector3 to) => Near(from, new Vector2(caster.X, caster.Y)) && Near(to, candidates[1]);
            Assert.Multiple(() =>
            {
                Assert.That(Pick(null, []), Is.EqualTo(spot), "open field: its own place");
                Assert.That(Pick(HidesFirst, []), Is.EqualTo(candidates[1]), "the first hidden place beside its own");
                Assert.That(Pick((from, to) => Near(from, new Vector2(caster.X, caster.Y)) && !Near(to, spot), []), Is.EqualTo(candidates[1]),
                    "everything beside it hidden: the nearest first");
                Assert.That(Pick((from, to) => Near(from, new Vector2(caster.X, caster.Y)) && !Near(to, spot), [(candidates[1], true)]),
                    Is.EqualTo(candidates[2]), "a healer stands on the first: the next one");
                Assert.That(Pick(HidesFirst, [(candidates[1], false)]), Is.EqualTo(spot), "the only hidden place breaks the spread: its own");
                Assert.That(Pick((from, to) => Near(from, candidates[1]) || Near(to, candidates[1]), []), Is.EqualTo(spot),
                    "a place that can't see the tanks' line is no cover");
            });
        }

        private static void AssertHealersApart(ImmutableArray<RvrSlot> slots, float spread, string why)
        {
            RvrSlot[] healers = slots.Where(s => s.Stand == RvrStand.Back && s.Healer && s.Error == RvrSlotError.None).ToArray();
            for (int i = 0; i < healers.Length; i++)
                for (int j = i + 1; j < healers.Length; j++)
                    Assert.That(Vector2.Distance(healers[i].Spot, healers[j].Spot), Is.GreaterThan(spread), why);
        }

        [Test]
        public void Spread_keeps_more_than_350_between_healers_and_450_on_incoming()
        {
            for (long now = 0; now < 20 * RvrPositioning.ErrorWindowMilliseconds; now += RvrPositioning.ErrorWindowMilliseconds)
                foreach (double bearing in Bearings)
                    foreach (eCharacterClass[] back in Backlines)
                    {
                        (List<RvrPositioning.Placed> members, Vector2 enemy) = Group(bearing, back);
                        foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                        {
                            AssertHealersApart(RvrPositioning.Layout(members, enemy, tier, false, now), RvrPositioning.MezSpread,
                                $"{tier}: healers that make no mistake");
                            AssertHealersApart(RvrPositioning.Layout(members, enemy, tier, true, now), RvrPositioning.ScatterSpread,
                                $"{tier}: incoming, they scatter");
                        }
                        ImmutableArray<RvrSlot> exact = RvrPositioning.Layout(members, enemy, RvrSkillTier.Veteran, false, now, errors: false);
                        RvrSlot[] all = exact.Where(s => s.Stand == RvrStand.Back).ToArray();
                        for (int i = 0; i < all.Length; i++)
                            for (int j = i + 1; j < all.Length; j++)
                                Assert.That(Vector2.Distance(all[i].Spot, all[j].Spot), Is.GreaterThan(RvrPositioning.MezSpread),
                                    "without errors every backline member is spread");
                    }
        }

        [Test]
        public void Incoming_is_enemies_near_and_nobody_in_contact()
        {
            Bot foe = Gamebot(realm: eRealm.Midgard);
            var near = ImmutableArray.Create(C(foe, eCharacterClass.Warrior, 2_000, realm: eRealm.Midgard));
            var far = ImmutableArray.Create(C(foe, eCharacterClass.Warrior, 3_000, realm: eRealm.Midgard));
            Assert.Multiple(() =>
            {
                Assert.That(RvrPositioning.Incoming(near, false, Vector2.Zero), Is.True);
                Assert.That(RvrPositioning.Incoming(near, true, Vector2.Zero), Is.False, "in contact: the fight is on");
                Assert.That(RvrPositioning.Incoming(far, false, Vector2.Zero), Is.False);
                Assert.That(RvrPositioning.Incoming(ImmutableArray<RvrCombatant>.Empty, false, Vector2.Zero), Is.False);
            });
        }

        [Test]
        public void The_arc_order_spreads_healers_over_the_whole_arc() => Assert.Multiple(() =>
        {
            Assert.That(RvrPositioning.ArcOrder([true, false, false, false, true]), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(RvrPositioning.ArcOrder([false, true, true, false]), Is.EqualTo(new[] { 1, 0, 3, 2 }), "healers at both ends");
            Assert.That(RvrPositioning.ArcOrder([false, false, true]), Is.EqualTo(new[] { 0, 2, 1 }), "one healer: straight behind");
            Assert.That(RvrPositioning.ArcOrder([]), Is.Empty);
            Assert.That(RvrPositioning.BackRadius(1, 0, 380), Is.EqualTo(RvrPositioning.MinBackDepth));
            Assert.That(RvrPositioning.BackRadius(8, 1, 380), Is.EqualTo(RvrPositioning.MaxBackDepth), "capped so casters stay in range");
        });

        // ---- the errors ---------------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.15, 0.30, 0.60)]
        [TestCase(RvrSkillTier.Average, 0.04, 0.10, 0.30)]
        [TestCase(RvrSkillTier.Veteran, 0.01, 0.03, 0.10)]
        public void Each_tier_makes_its_share_of_mistakes(RvrSkillTier tier, double charge, double tooClose, double noKite)
        {
            const int Healers = 20_000;
            int charging = 0, close = 0, skipped = 0;
            for (long id = 1; id <= Healers; id++)
            {
                RvrSlotError errors = RvrPositioning.Errors(tier, 4_000_000 + id, 77_777, healer: true);
                charging += (errors & RvrSlotError.Charge) != 0 ? 1 : 0;
                close += (errors & RvrSlotError.TooClose) != 0 ? 1 : 0;
                skipped += RvrPositioning.KiteSkipped(tier, 4_000_000 + id, 3) ? 1 : 0;
            }
            Assert.Multiple(() =>
            {
                Assert.That(charging / (double)Healers, Is.EqualTo(charge).Within(0.01), "healers charging with the tanks");
                // a healer that charges does not also stand too close
                Assert.That(close / (double)Healers, Is.EqualTo((1 - charge) * tooClose).Within(0.015), "healers too close together");
                Assert.That(skipped / (double)Healers, Is.EqualTo(noKite).Within(0.015), "healers that do not kite");
                Assert.That(RvrPositioning.Errors(tier, 4_000_001, 77_777, healer: false), Is.EqualTo(RvrSlotError.None),
                    "only healers make these mistakes");
            });
        }

        [Test]
        public void Ragged_spread_and_reaction_delays_grow_from_veteran_to_casual()
        {
            Assert.That(RvrPositioning.Ragged(RvrSkillTier.Veteran).Radial, Is.LessThan(RvrPositioning.Ragged(RvrSkillTier.Average).Radial));
            Assert.That(RvrPositioning.Ragged(RvrSkillTier.Average).Radial, Is.LessThan(RvrPositioning.Ragged(RvrSkillTier.Casual).Radial));
            var ranges = new Dictionary<RvrSkillTier, (long Min, long Max)>
            {
                [RvrSkillTier.Casual] = (1_000, 4_000), [RvrSkillTier.Average] = (600, 2_500), [RvrSkillTier.Veteran] = (300, 1_500),
            };
            foreach ((RvrSkillTier tier, (long min, long max)) in ranges)
            {
                long[] delays = Enumerable.Range(0, 2_000).Select(i => RvrPositioning.ReactionMilliseconds(tier, 5_000 + i, i * 1_000L)).ToArray();
                Assert.That(delays.Min(), Is.GreaterThanOrEqualTo(min), tier.ToString());
                Assert.That(delays.Max(), Is.LessThanOrEqualTo(max), tier.ToString());
                Assert.That(delays.Average(), Is.EqualTo((min + max) / 2.0).Within((max - min) * 0.1), tier.ToString());
            }
        }

        [Test]
        public void A_too_close_healer_really_stands_within_one_mez_of_another()
        {
            int found = 0;
            for (long now = 0; now < 400 * RvrPositioning.ErrorWindowMilliseconds && found < 20; now += RvrPositioning.ErrorWindowMilliseconds)
            {
                (List<RvrPositioning.Placed> members, Vector2 enemy) = Group(0.3, Backlines[0]);
                ImmutableArray<RvrSlot> slots = RvrPositioning.Layout(members, enemy, RvrSkillTier.Casual, false, now);
                // one healer too close, the other where it belongs
                if (slots.Count(s => s.Healer && s.Error == RvrSlotError.TooClose) != 1 ||
                    slots.Count(s => s.Healer && s.Error == RvrSlotError.None) != 1)
                    continue;
                foreach (RvrSlot slot in slots.Where(s => (s.Error & RvrSlotError.TooClose) != 0))
                {
                    found++;
                    float nearest = slots.Where(s => s.Healer && s.Stand == RvrStand.Back && !ReferenceEquals(s.Member, slot.Member))
                        .Min(s => Vector2.Distance(s.Spot, slot.Spot));
                    Assert.That(nearest, Is.InRange(RvrPositioning.TooCloseMin - 1, RvrPositioning.TooCloseMax + 1));
                }
            }
            Assert.That(found, Is.GreaterThan(0), "casual groups do make the mistake");
        }

        [Test]
        public void Who_walks_and_why()
        {
            var slot = new RvrSlot(null, RvrStand.Back, new Vector2(-500, 0), true, RvrSlotError.None);
            Vector2 line = Vector2.Zero;
            const double Front = 0; // the enemy is along +x
            Assert.Multiple(() =>
            {
                Assert.That(RvrPositioning.NeedsMove(new(50, 0), slot, line, Front, false, []), Is.EqualTo(RvrMoveReason.Backline),
                    "in front of the tanks' line");
                Assert.That(RvrPositioning.NeedsMove(new(-300, 0), slot, line, Front, false, [(new(-300, 200), true)]),
                    Is.EqualTo(RvrMoveReason.Spread), "two healers within one mez");
                Assert.That(RvrPositioning.NeedsMove(new(-300, 0), slot, line, Front, true, [(new(-300, 360), false)]),
                    Is.EqualTo(RvrMoveReason.Incoming), "incoming: wider");
                Assert.That(RvrPositioning.NeedsMove(new(-300, 0), slot, line, Front, false, [(new(-300, 420), false)]),
                    Is.EqualTo(RvrMoveReason.None), "spread and behind: stays, though not on its slot");
                Assert.That(RvrPositioning.NeedsMove(new(-1000, 0), slot, line, Front, false, []), Is.EqualTo(RvrMoveReason.Far));
                Assert.That(RvrPositioning.NeedsMove(new(-500, 30), slot, line, Front, false, []), Is.EqualTo(RvrMoveReason.None));
                Assert.That(RvrPositioning.NeedsMove(new(-300, 0), slot with { Error = RvrSlotError.TooClose }, line, Front, false,
                    [(new(-300, 200), true)]), Is.EqualTo(RvrMoveReason.None), "a too-close healer does not mind");
                Assert.That(RvrPositioning.NeedsMove(new(-500, 0), slot with { Spot = new(60, 0), Error = RvrSlotError.Charge }, line,
                    Front, false, []), Is.EqualTo(RvrMoveReason.Charge), "it charges with the tanks");
                Assert.That(RvrPositioning.NeedsMove(new(50, 0), slot with { Stand = RvrStand.Front }, line, Front, false, []),
                    Is.EqualTo(RvrMoveReason.None), "tanks are not walked");
                Assert.That(RvrPositioning.NeedsMove(new(50, 0), slot, null, Front, false, []), Is.EqualTo(RvrMoveReason.None),
                    "no line (no tank or melee): only the spread counts");
                Assert.That(RvrPositioning.WithinRange(new(-1500, 0), new(800, 0), 1500), Is.EqualTo(new Vector2(-700, 0)),
                    "a caster's place is pulled into range of its target");
                Assert.That(RvrPositioning.EscortSpot(new(0, 0), new(1000, 0)), Is.EqualTo(new Vector2(RvrPositioning.EscortOffset, 0)));
            });
        }

        [Test]
        public void A_walk_is_proven_in_short_pieces_that_end_at_the_spot()
        {
            Vector2[] pieces = RvrPositioning.Chunks(new(0, 0), new(500, 0));
            Assert.That(pieces[^1], Is.EqualTo(new Vector2(500, 0)));
            Vector2 previous = Vector2.Zero;
            foreach (Vector2 piece in pieces)
            {
                Assert.That(Vector2.Distance(previous, piece), Is.LessThanOrEqualTo(RvrPositioning.ChunkLength + 0.01f));
                previous = piece;
            }
            Assert.That(RvrPositioning.Chunks(Vector2.Zero, Vector2.Zero), Has.Length.EqualTo(1));
        }

        // ---- kiting -------------------------------------------------------------------------------------------------------

        [Test]
        public void A_healer_kites_on_melee_contact_only()
        {
            Bot healer = Gamebot();
            Bot berserker = Gamebot(realm: eRealm.Midgard), caster = Gamebot(realm: eRealm.Midgard);
            Player savage = RealPlayer(eRealm.Midgard);
            RvrCombatant onMe = C(berserker, eCharacterClass.Berserker, 120, realm: eRealm.Midgard, target: healer, fighting: true);
            RvrCombatant playerOnMe = C(savage, eCharacterClass.Savage, 0, -150, eRealm.Midgard, healer, true);
            RvrCombatant nuker = C(caster, eCharacterClass.Runemaster, 1_500, realm: eRealm.Midgard, target: healer, fighting: true);
            Assert.Multiple(() =>
            {
                Assert.That(RvrPositioning.KiteFrom(healer, 0, 0, ImmutableArray.Create(onMe, nuker))?.Living, Is.SameAs(berserker));
                Assert.That(RvrPositioning.KiteFrom(healer, 0, 0, ImmutableArray.Create(playerOnMe))?.Living, Is.SameAs(savage),
                    "a player in melee too");
                Assert.That(RvrPositioning.KiteFrom(healer, 0, 0, ImmutableArray.Create(nuker)), Is.Null, "a caster at range: no kite");
                Assert.That(RvrPositioning.KiteFrom(healer, 0, 0, ImmutableArray.Create(nuker with { X = 100 })), Is.Null,
                    "a caster standing close is not kited from");
                Assert.That(RvrPositioning.KiteFrom(healer, 0, 0, ImmutableArray.Create(onMe with { Control = RvrControlState.Stunned })),
                    Is.Null, "a stunned attacker");
                Assert.That(RvrPositioning.KiteFrom(healer, 0, 0, ImmutableArray.Create(onMe with { Target = Gamebot() })), Is.Null,
                    "a melee fighter on someone else");
            });

            Vector2 self = Vector2.Zero, attacker = new(100, 0), refuge = new(0, -600);
            Vector2 run = RvrPositioning.KiteSpot(self, attacker, refuge, 300);
            Vector2 straight = RvrPositioning.KiteSpot(self, attacker, null, 300);
            Assert.Multiple(() =>
            {
                Assert.That(Vector2.Distance(straight, attacker), Is.EqualTo(400).Within(0.5f), "straight away");
                Assert.That(Vector2.Distance(run, attacker), Is.GreaterThan(Vector2.Distance(self, attacker) + 150), "away from it");
                Assert.That(Vector2.Distance(run, refuge), Is.LessThan(Vector2.Distance(self, refuge)), "toward the guard");
                Assert.That(RvrPositioning.KiteSpot(self, attacker, new(300, 0), 300).X, Is.LessThan(0),
                    "never toward a refuge behind the attacker");
                Assert.That(RvrPositioning.KiteDistance(RvrSkillTier.Veteran, 1), Is.EqualTo(RvrPositioning.KiteMaxDistance));
                Assert.That(RvrPositioning.KiteHealWindow(0), Is.EqualTo(RvrPositioning.KiteHealWindowMinMilliseconds));
            });
        }

        // ---- the kiting group (stance Kite) -------------------------------------------------------------------------------

        private static RvrCombatant Foe(eCharacterClass characterClass, Vector2 at, RvrControlState control = RvrControlState.None,
            bool pet = false) => C(null, characterClass, (int)at.X, (int)at.Y, eRealm.Midgard, control: control, pet: pet);

        /// <summary>The first step number from <paramref name="step"/> on at which this member makes no kite error.</summary>
        private static long Clean(RvrSkillTier tier, long id, long step = 1)
        {
            while (RvrPositioning.KiteErrors(tier, id, step) != RvrKiteError.None)
                step++;
            return step;
        }

        [Test]
        public void A_kite_step_backs_away_from_the_enemy_centroid_for_every_bearing()
        {
            const long Id = 7_001;
            foreach (double bearing in Bearings)
            {
                Vector2 front = RvrPositioning.Direction(bearing);
                Vector2 self = new(500, -300);
                var enemies = ImmutableArray.Create(Foe(eCharacterClass.Berserker, self + front * 800),
                    Foe(eCharacterClass.Runemaster, self + front * 1_700));
                Vector2 centroid = self + front * 1_250;
                long step = Clean(RvrSkillTier.Veteran, Id);
                RvrKiteStep kite = RvrPositioning.KiteStep(self, null, bearing, enemies, centroid, null, RvrSkillTier.Veteran, Id, step,
                    (_, _) => true);
                Assert.That(kite.Spot, Is.Not.Null, $"bearing {bearing:F2}");
                Vector2 leg = kite.Spot!.Value - self;
                Assert.Multiple(() =>
                {
                    Assert.That(kite.Held, Is.EqualTo(RvrKiteHeld.None));
                    Assert.That(Vector2.Dot(Vector2.Normalize(leg), -front), Is.GreaterThan(0.999f), "straight back, away from the centroid");
                    Assert.That(leg.Length(), Is.EqualTo(RvrPositioning.KiteStepLength).Within(0.5f), "a short leg");
                    Assert.That(kite.MeleeDistance, Is.EqualTo(800).Within(2f), "positions are whole units");
                });

                // the back-arc slot bends the leg sideways, never forward
                Vector2 side = RvrPositioning.Direction(bearing + Math.PI / 2);
                RvrKiteStep bent = RvrPositioning.KiteStep(self, self - front * 300 + side * 400, bearing, enemies, centroid, null,
                    RvrSkillTier.Veteran, Id, step, (_, _) => true);
                Vector2 bentLeg = bent.Spot!.Value - self;
                Assert.That(Vector2.Dot(bentLeg, side), Is.GreaterThan(50), "toward its slot's side");
                Assert.That(Vector2.Dot(bentLeg, -front), Is.GreaterThan(200), "still backing away");
            }
        }

        [TestCase(RvrSkillTier.Casual)]
        [TestCase(RvrSkillTier.Average)]
        [TestCase(RvrSkillTier.Veteran)]
        public void Kite_steps_keep_the_stand_off_distance_then_the_member_stands_and_casts(RvrSkillTier tier)
        {
            const long Id = 7_102;
            Vector2 melee = Vector2.Zero, self = new(-600, 0);
            var enemies = ImmutableArray.Create(Foe(eCharacterClass.Armsman, melee));
            float standOff = RvrPositioning.KiteStandOffFor(tier, Id);
            long step = 1;
            int steps = 0;
            while (true)
            {
                step = Clean(tier, Id, step);
                RvrKiteStep kite = RvrPositioning.KiteStep(self, null, 0, enemies, melee, null, tier, Id, step++, (_, _) => true);
                if (kite.Spot == null)
                {
                    Assert.That(kite.Held, Is.EqualTo(RvrKiteHeld.Clear));
                    break;
                }
                Assert.That(Vector2.Distance(kite.Spot.Value, melee), Is.GreaterThan(Vector2.Distance(self, melee)), "every step gains distance");
                Assert.That(kite.Length, Is.InRange(RvrPositioning.KiteMinStep, RvrPositioning.KiteStepLength));
                self = kite.Spot.Value;
                Assert.That(++steps, Is.LessThan(10));
            }
            float kept = Vector2.Distance(self, melee);
            Assert.Multiple(() =>
            {
                Assert.That(kept, Is.GreaterThanOrEqualTo(standOff), "kept the stand-off distance");
                Assert.That(kept, Is.LessThan(standOff + RvrPositioning.KiteMinStep + 1), "and no farther (still in cast range)");
                Assert.That(standOff, Is.InRange(1_000f, 1_400f));
                Assert.That(RvrPositioning.KiteStep(new(-1_500, 0), null, 0, enemies, melee, null, tier, Id, 1, (_, _) => true).Held,
                    Is.EqualTo(RvrKiteHeld.Clear), "far enough: it does not step");
                Assert.That(RvrPositioning.KiteStep(self, null, 0, ImmutableArray.Create(Foe(eCharacterClass.Wizard, new(-200, 0)),
                    Foe(eCharacterClass.Berserker, new(-300, 0), RvrControlState.Mezzed), Foe(eCharacterClass.Unknown, new(-300, 0), pet: true)),
                    melee, null, tier, Id, 1, (_, _) => true).Held, Is.EqualTo(RvrKiteHeld.NoMelee),
                    "casters, mezzed fighters and pets do not chase: the layout applies");
            });
        }

        [Test]
        public void A_kite_step_never_takes_a_blocked_or_dangerous_leg_and_holds_instead()
        {
            const long Id = 7_204;
            long step = Clean(RvrSkillTier.Veteran, Id);
            Vector2 self = Vector2.Zero, melee = new(700, 0);
            var chasers = ImmutableArray.Create(Foe(eCharacterClass.Berserker, melee));
            RvrKiteStep Step(ImmutableArray<RvrCombatant> enemies, Func<Vector2, Vector2, bool> clear,
                IReadOnlyList<AutonomousArmyDetour.Hazard> keeps = null) =>
                RvrPositioning.KiteStep(self, null, 0, enemies, melee, keeps, RvrSkillTier.Veteran, Id, step, clear);

            var tried = new List<Vector2>();
            RvrKiteStep blocked = Step(chasers, (_, to) =>
            {
                tried.Add(to);
                return false;
            });
            Assert.Multiple(() =>
            {
                Assert.That(blocked.Spot, Is.Null);
                Assert.That(blocked.Held, Is.EqualTo(RvrKiteHeld.Blocked), "no leg clear on the mesh: it holds and fights");
                Assert.That(tried, Has.Count.EqualTo(1 + RvrPositioning.KiteDetourDegrees.Length * 2), "straight, then 35 and 70 degrees each side");
            });

            // only the straight leg is blocked (a ledge behind): it takes a detour leg that is clear
            Vector2 straight = new(-RvrPositioning.KiteStepLength, 0);
            RvrKiteStep detour = Step(chasers, (_, to) => Vector2.Distance(to, straight) > 1);
            Assert.That(detour.Spot, Is.Not.Null);
            Assert.That(Vector2.Distance(detour.Spot!.Value, straight), Is.GreaterThan(1));
            Assert.That(detour.Spot.Value.X, Is.LessThan(-100), "still backing away");

            // another enemy group behind: no leg toward it
            var second = chasers.Add(Foe(eCharacterClass.Warrior, new(-1_200, 0))).Add(Foe(eCharacterClass.Hero, new(-1_200, 400)))
                .Add(Foe(eCharacterClass.Champion, new(-1_200, -400)));
            RvrKiteStep group = RvrPositioning.KiteStep(self, null, 0, second, melee, null, RvrSkillTier.Veteran, Id, step, (_, _) => true);
            Assert.That(group.Spot, Is.Null);
            Assert.That(group.Held, Is.EqualTo(RvrKiteHeld.EnemyGroup));

            // an enemy keep behind: no leg into its guards' range
            var keep = new[] { new AutonomousArmyDetour.Hazard(new(-1_500, 0), 3_000) };
            RvrKiteStep guards = Step(chasers, (_, _) => true, keep);
            Assert.That(guards.Spot, Is.Null);
            Assert.That(guards.Held, Is.EqualTo(RvrKiteHeld.KeepGuards));
            Assert.That(Step(chasers, (_, _) => true, [new(new(-1_500, 0), 1_000)]).Spot, Is.Not.Null, "a keep out of range does not stop it");
            Assert.That(RvrPositioning.KiteHazard(self, new(300, 0), chasers, melee, keep), Is.EqualTo(RvrKiteHeld.None),
                "inside the range, a leg away from the keep is fine");
        }

        [TestCase(RvrSkillTier.Casual, 0.35, 0.20, 0.25, 0.25)]
        [TestCase(RvrSkillTier.Average, 0.15, 0.08, 0.10, 0.10)]
        [TestCase(RvrSkillTier.Veteran, 0.05, 0.02, 0.03, 0.03)]
        public void Each_tier_kites_with_its_share_of_mistakes(RvrSkillTier tier, double late, double wrongWay, double overrun, double chase)
        {
            const int Members = 20_000;
            int lates = 0, wrongs = 0, overruns = 0, chases = 0;
            float minStandOff = float.MaxValue, maxStandOff = 0;
            for (long id = 1; id <= Members; id++)
            {
                RvrKiteError errors = RvrPositioning.KiteErrors(tier, 9_000_000 + id, 5);
                lates += (errors & RvrKiteError.Late) != 0 ? 1 : 0;
                wrongs += (errors & RvrKiteError.WrongWay) != 0 ? 1 : 0;
                overruns += (errors & RvrKiteError.Overrun) != 0 ? 1 : 0;
                chases += RvrPositioning.KiteChases(tier, 9_000_000 + id, 90_000) ? 1 : 0;
                float standOff = RvrPositioning.KiteStandOffFor(tier, 9_000_000 + id);
                minStandOff = Math.Min(minStandOff, standOff);
                maxStandOff = Math.Max(maxStandOff, standOff);
            }
            float spread = RvrPositioning.KiteStandOffSpread(tier);
            Assert.Multiple(() =>
            {
                Assert.That(lates / (double)Members, Is.EqualTo(late).Within(0.015), "backs off late");
                Assert.That(wrongs / (double)Members, Is.EqualTo(wrongWay).Within(0.015), "runs the wrong way");
                Assert.That(overruns / (double)Members, Is.EqualTo(overrun).Within(0.015), "runs too far");
                Assert.That(chases / (double)Members, Is.EqualTo(chase).Within(0.015), "melee charges anyway");
                Assert.That(minStandOff, Is.GreaterThanOrEqualTo(RvrPositioning.KiteStandOff - spread));
                Assert.That(maxStandOff, Is.LessThanOrEqualTo(RvrPositioning.KiteStandOff + spread));
            });
            Assert.That(RvrPositioning.KiteStandOffSpread(RvrSkillTier.Veteran), Is.LessThan(RvrPositioning.KiteStandOffSpread(RvrSkillTier.Average)));
            Assert.That(RvrPositioning.KiteStandOffSpread(RvrSkillTier.Average), Is.LessThan(RvrPositioning.KiteStandOffSpread(RvrSkillTier.Casual)));
        }

        [Test]
        public void The_kite_errors_change_the_step()
        {
            Vector2 self = Vector2.Zero, melee = new(700, 0);
            var enemies = ImmutableArray.Create(Foe(eCharacterClass.Berserker, melee));
            RvrKiteStep With(RvrKiteError wanted, Vector2 at, out long id)
            {
                for (id = 1; ; id++)
                    if (RvrPositioning.KiteErrors(RvrSkillTier.Casual, id, 1) == wanted)
                        return RvrPositioning.KiteStep(at, null, 0, enemies, melee, null, RvrSkillTier.Casual, id, 1, (_, _) => true);
            }
            RvrKiteStep late = With(RvrKiteError.Late, self, out _);
            RvrKiteStep lateClose = With(RvrKiteError.Late, new(400, 0), out _);
            RvrKiteStep overrun = With(RvrKiteError.Overrun, self, out _);
            RvrKiteStep wrong = With(RvrKiteError.WrongWay, self, out _);
            Assert.Multiple(() =>
            {
                Assert.That(late.Held, Is.EqualTo(RvrKiteHeld.Clear), "late: 700 away it does not move yet");
                Assert.That(lateClose.Spot, Is.Not.Null, "late: 300 away it finally runs");
                Assert.That(overrun.Length, Is.EqualTo(RvrPositioning.KiteStepLength * RvrPositioning.KiteOverrunFactor).Within(0.5f), "too far");
                double off = Math.Abs(Math.Atan2(wrong.Spot!.Value.Y, -wrong.Spot.Value.X) * 180 / Math.PI);
                Assert.That(off, Is.InRange(RvrPositioning.KiteWrongWayMinDegrees - 0.5, RvrPositioning.KiteWrongWayMaxDegrees + 0.5),
                    "the wrong way: well off the back direction");
            });
        }

        [Test]
        public void Melee_screen_the_backline_and_fight_only_what_reaches_it()
        {
            Vector2 enemy = new(2_000, 0);
            var backline = new List<Vector2> { new(-100, 300), new(-100, -300) };
            Vector2 screen = new(-100 + RvrPositioning.KiteScreenDistance, 0);
            Assert.Multiple(() =>
            {
                (RvrKitePeel action, Vector2 spot) = RvrPositioning.KitePeel(new(900, 0), new(1_000, 0), backline, enemy, false, false);
                Assert.That(action, Is.EqualTo(RvrKitePeel.Screen), "a far target: back to the backline instead of charging");
                Assert.That(Vector2.Distance(spot, screen), Is.LessThan(1));
                Assert.That(RvrPositioning.KitePeel(screen, new(1_000, 0), backline, enemy, false, false).Action, Is.EqualTo(RvrKitePeel.Stand));
                Assert.That(RvrPositioning.KitePeel(new(900, 0), new(200, 250), backline, enemy, false, false).Action,
                    Is.EqualTo(RvrKitePeel.Fight), "an enemy that reached the backline");
                Assert.That(RvrPositioning.KitePeel(new(900, 0), new(1_000, 0), backline, enemy, true, false).Action,
                    Is.EqualTo(RvrKitePeel.Fight), "hit in melee: it fights back");
                Assert.That(RvrPositioning.KitePeel(new(900, 0), new(1_000, 0), backline, enemy, false, true).Action,
                    Is.EqualTo(RvrKitePeel.Fight), "the tier's charge anyway");
                Assert.That(RvrPositioning.KitePeel(new(900, 0), new(1_000, 0), [], enemy, false, false).Action,
                    Is.EqualTo(RvrKitePeel.Fight), "nobody to screen");
            });
        }

        [Test]
        public void Stances_other_than_kite_place_as_before() => Assert.Multiple(() =>
        {
            Assert.That(RvrPositioning.PlacesIn(RvrStance.Engage), Is.True);
            Assert.That(RvrPositioning.PlacesIn(RvrStance.Observe), Is.True);
            Assert.That(RvrPositioning.PlacesIn(RvrStance.Hold), Is.True);
            Assert.That(RvrPositioning.PlacesIn(RvrStance.Kite), Is.True, "kiting now moves the group");
            Assert.That(RvrPositioning.PlacesIn(RvrStance.Retreat), Is.False, "the stance's own retreat");
            Assert.That(RvrPositioning.PlacesIn(RvrStance.Regroup), Is.False, "the stance's own regroup");
            // the layout the other stances use is untouched by the kite rules
            (List<RvrPositioning.Placed> members, Vector2 enemy) = Group(0.3, eCharacterClass.Cleric, eCharacterClass.Wizard);
            Assert.That(RvrPositioning.Layout(members, enemy, RvrSkillTier.Veteran, false, 0, errors: false),
                Is.EqualTo(RvrPositioning.Layout(members, enemy, RvrSkillTier.Veteran, false, 0, errors: false)));
        });

        [Test]
        public void The_kite_turn_keeps_out_of_player_groups_companions_raids_and_keeps()
        {
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            enemy.Region = 250;
            long builds = RvrFightBoards.Builds;
            Bot raider = Gamebot(objective: "RvR"), raidMate = Gamebot(objective: "RvR");
            raider.Region = raidMate.Region = 250;
            GroupOf(raider, raidMate);
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            NoOpinion(new BotBrain { Body = raider }, enemy, "a raider");
            Bot companion = Companion();
            companion.Region = 250;
            NoOpinion(new BotBrain { Body = companion }, enemy, "a companion");
            Bot invited = Gamebot(objective: "RvR");
            invited.Region = 250;
            JoinPlayersGroup(invited, RealPlayer());
            NoOpinion(new BotBrain { Body = invited }, enemy, "a player's group");
            Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds), "no board, so no stance and no kite step");
        }

        // ---- scope guard --------------------------------------------------------------------------------------------------

        private readonly Dictionary<string, bool> _switches = new();
        private static readonly string[] Fields = ["RVR_BOT_FIELD_TACTICS", "RVR_BOT_PEEL", "RVR_BOT_POSITIONING", "RVR_BOT_STEALTH_OPENERS"];

        [SetUp]
        public void SwitchesOn()
        {
            BotDutyLeases.Clear();
            foreach (string field in Fields)
            {
                _switches[field] = (bool)typeof(Properties).GetField(field)!.GetValue(null)!;
                typeof(Properties).GetField(field)!.SetValue(null, true);
            }
        }

        [TearDown]
        public void SwitchesBack()
        {
            foreach ((string field, bool value) in _switches)
                typeof(Properties).GetField(field)!.SetValue(null, value);
            BotDutyLeases.Clear();
        }

        /// <summary>A group of real members (the fields Group's constructor sets) without a world.</summary>
        private static Group GroupOf(params GameLiving[] members)
        {
            var group = (Group)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Group));
            typeof(Group).GetField("_groupMembers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(group, new List<GameLiving>(members));
            typeof(Group).GetField("_groupMembersLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(group, new System.Threading.Lock());
            foreach (GameLiving member in members)
                member.Group = group;
            return group;
        }

        private static void NoOpinion(BotBrain brain, Bot enemy, string why) => Assert.Multiple(() =>
        {
            Assert.That(brain.RvrTacticsPeel(), Is.False, why);
            Assert.That(brain.RvrTacticsProtectionTarget(out GameLiving target), Is.False, why);
            Assert.That(target, Is.Null, why);
            Assert.That(brain.TryRvrTacticsMove(enemy), Is.False, why);
            Assert.That(brain.TryRvrTacticsSupport(), Is.False, why);
        });

        [Test]
        public void The_hooks_keep_out_of_player_groups_companions_raids_keeps_and_pve()
        {
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            enemy.Region = 250;
            Bot Field()
            {
                Bot bot = Gamebot(objective: "RvR");
                bot.Region = 250;
                return bot;
            }
            long builds = RvrFightBoards.Builds;

            Bot a = Field(), b = Field();
            GroupOf(a, b);
            var brain = new BotBrain { Body = a };
            Assert.That(brain.RvrTacticsActive(), Is.True, "a bot-only group in a battle region is in scope");
            NoOpinion(brain, enemy, "in scope, but no fight against players or bots on its board");

            Bot raider = Field(), raidMate = Field();
            GroupOf(raider, raidMate);
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            NoOpinion(new BotBrain { Body = raider }, enemy, "a raider");

            Bot defender = Field(), defenderMate = Field();
            GroupOf(defender, defenderMate);
            defender.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.DefendEvent);
            NoOpinion(new BotBrain { Body = defender }, enemy, "a keep defender (a fight at an objective)");

            Bot companion = Companion();
            companion.Region = 250;
            NoOpinion(new BotBrain { Body = companion }, enemy, "a companion");

            Player player = RealPlayer();
            Bot invited = Field();
            JoinPlayersGroup(invited, player);
            NoOpinion(new BotBrain { Body = invited }, enemy, "a player's group");

            Bot home = Gamebot(objective: "RvR"), homeMate = Gamebot(objective: "RvR");
            GroupOf(home, homeMate);
            NoOpinion(new BotBrain { Body = home }, enemy, "outside the frontier, battlegrounds and battle dungeons");

            Properties.RVR_BOT_PEEL = false;
            Properties.RVR_BOT_POSITIONING = false;
            Properties.RVR_BOT_STEALTH_OPENERS = false;
            NoOpinion(brain, enemy, "both switches off");
            Properties.RVR_BOT_FIELD_TACTICS = false;
            NoOpinion(brain, enemy, "the master switch off");
            Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds), "no board was built for any of them");
        }

        [Test]
        public void PvE_combat_movement_stays_off_against_players_and_bots() => Assert.Multiple(() =>
        {
            Assert.That(BotCombatMovement.Allowed(gamebot: true, playerLedGroup: false, raidOrEpic: false, monsterTarget: false), Is.False);
            Assert.That(BotCombatMovement.Allowed(gamebot: true, playerLedGroup: false, raidOrEpic: false, monsterTarget: true), Is.True);
        });
    }
}
