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
