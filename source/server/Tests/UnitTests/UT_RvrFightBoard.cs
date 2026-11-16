using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md layer 2): the shared fight state. One holder per
    /// group and region, lazy (nothing is built until a consumer asks), rebuilt at most once a second by the first
    /// asker, published as an immutable snapshot. Pure tests: the holder is given its builder, and the board is built
    /// from plain records, so no game world is needed. The builder that reads the game (RvrFightBoards.Get) is not
    /// covered here: it needs a region.
    /// </summary>
    [TestFixture]
    public class UT_RvrFightBoard
    {
        private static RvrCombatant Head(eRealm realm, eCharacterClass characterClass, int level = 50, int health = 100,
            int? power = null, int x = 0, int y = 0) => new()
        {
            Realm = realm, Class = characterClass, Role = RvrRoles.For(characterClass), Level = level, HealthPercent = health,
            UsesPower = power.HasValue, PowerPercent = power ?? 0, X = x, Y = y,
        };

        private static RvrCombatant Pet(eRealm realm, int x = 0, int y = 0) =>
            new() { Realm = realm, Role = RvrRole.Pet, IsPet = true, Level = 44, HealthPercent = 100, X = x, Y = y };

        // ---- laziness and the once-a-second rebuild ---------------------------------------------------------------------

        [Test]
        public void Nothing_is_built_until_a_consumer_asks()
        {
            var holder = new RvrFightBoards.Holder();
            int builds = 0;
            Assert.Multiple(() =>
            {
                Assert.That(holder.Board, Is.Null);
                Assert.That(holder.Builds, Is.Zero);
                Assert.That(holder.Peek(1_000), Is.Null, "a peek never builds");
                Assert.That(builds, Is.Zero);
                Assert.That(RvrFightBoards.Get(null), Is.Null);
                Assert.That(RvrFightBoards.Peek(null), Is.Null);
                Assert.That(RvrFightBoards.PeekPlan(null), Is.Null);
            });
            RvrFightBoard board = holder.Get(1_000, 51, _ => { builds++; return new RvrFightBoard { BuiltAt = 1_000, RegionId = 51 }; });
            Assert.Multiple(() =>
            {
                Assert.That(builds, Is.EqualTo(1));
                Assert.That(holder.Builds, Is.EqualTo(1));
                Assert.That(holder.Board, Is.SameAs(board));
                Assert.That(holder.Peek(1_500), Is.SameAs(board));
            });
        }

        [Test]
        public void The_board_is_rebuilt_at_most_once_a_second_by_the_first_asker()
        {
            var holder = new RvrFightBoards.Holder();
            int builds = 0;
            var previous = new List<RvrFightBoard>();
            RvrFightBoard Ask(long now, ushort region = 51) => holder.Get(now, region, before =>
            {
                builds++;
                previous.Add(before);
                return new RvrFightBoard { BuiltAt = now, RegionId = region };
            });

            RvrFightBoard first = Ask(10_000);
            Assert.Multiple(() =>
            {
                Assert.That(Ask(10_000), Is.SameAs(first), "the same tick");
                Assert.That(Ask(10_400), Is.SameAs(first));
                Assert.That(Ask(10_999), Is.SameAs(first), "still under a second old");
                Assert.That(builds, Is.EqualTo(1));
            });
            RvrFightBoard second = Ask(11_000);
            Assert.Multiple(() =>
            {
                Assert.That(second, Is.Not.SameAs(first), "a second old: rebuilt");
                Assert.That(builds, Is.EqualTo(2));
                Assert.That(previous, Is.EqualTo(new[] { null, first }), "the builder gets the board before it, for the plan");
                Assert.That(Ask(11_100, 52), Is.Not.SameAs(second), "another region is never fresh");
                Assert.That(builds, Is.EqualTo(3));
                Assert.That(holder.Builds, Is.EqualTo(3));
                Assert.That(RvrFightBoard.RebuildMilliseconds, Is.EqualTo(1_000));
                Assert.That(RvrFightBoard.NeedsRebuild(1_999, 1_000, true), Is.False);
                Assert.That(RvrFightBoard.NeedsRebuild(2_000, 1_000, true), Is.True);
                Assert.That(RvrFightBoard.NeedsRebuild(1_100, 1_000, false), Is.True);
                Assert.That(new RvrFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(1_500, 51), Is.True);
                Assert.That(new RvrFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(1_500, 52), Is.False);
            });
        }

        [Test]
        public void Members_asking_at_once_build_one_board()
        {
            var holder = new RvrFightBoards.Holder();
            int builds = 0;
            var boards = new RvrFightBoard[64];
            Parallel.For(0, boards.Length, i => boards[i] = holder.Get(5_000, 51, _ =>
            {
                System.Threading.Interlocked.Increment(ref builds);
                System.Threading.Thread.Sleep(5);
                return new RvrFightBoard { BuiltAt = 5_000, RegionId = 51 };
            }));
            Assert.Multiple(() =>
            {
                Assert.That(builds, Is.EqualTo(1));
                Assert.That(boards.Distinct().Count(), Is.EqualTo(1), "everyone reads the one published snapshot");
            });
        }

        [Test]
        public void The_peek_never_builds_and_hands_out_boards_up_to_two_seconds_old()
        {
            var holder = new RvrFightBoards.Holder();
            RvrFightBoard board = holder.Get(1_000, 51, _ => new RvrFightBoard { BuiltAt = 1_000, RegionId = 51 });
            Assert.Multiple(() =>
            {
                Assert.That(holder.Peek(3_000), Is.SameAs(board));
                Assert.That(holder.Peek(3_001), Is.Null);
                Assert.That(holder.Builds, Is.EqualTo(1));
                Assert.That(RvrFightBoard.PeekFresh(3_000, 1_000), Is.True);
                Assert.That(RvrFightBoard.PeekFresh(3_001, 1_000), Is.False);
            });
        }

        [Test]
        public void A_builder_that_gives_nothing_publishes_nothing()
        {
            var holder = new RvrFightBoards.Holder();
            Assert.That(holder.Get(1_000, 51, _ => null, out bool built), Is.Null);
            Assert.That(built, Is.False);
            Assert.That(holder.Builds, Is.Zero);
        }

        // ---- immutability -------------------------------------------------------------------------------------------------

        private static readonly Type[] SnapshotTypes =
        [
            typeof(RvrFightBoard), typeof(RvrFightPlan), typeof(RvrGroupState), typeof(RvrStrength), typeof(RvrCombatant),
            typeof(RvrAssignment), typeof(RvrScopeFacts), typeof(RvrTacticsSwitches),
        ];

        [Test]
        public void The_snapshot_records_cannot_be_changed_after_they_are_made()
        {
            foreach (Type type in SnapshotTypes)
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    MethodInfo setter = property.SetMethod;
                    bool initOnly = setter == null ||
                        setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
                    Assert.That(initOnly, Is.True, $"{type.Name}.{property.Name} has a setter");
                    Type valueType = property.PropertyType;
                    Assert.That(valueType.IsArray || valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(List<>) ||
                                valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(HashSet<>),
                        Is.False, $"{type.Name}.{property.Name} is a collection that can be changed");
                }
                Assert.That(type.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty, $"{type.Name} has public fields");
            }
        }

        [Test]
        public void A_changed_copy_leaves_the_published_board_alone()
        {
            var members = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Cleric) };
            var enemies = new List<RvrCombatant> { Head(eRealm.Midgard, eCharacterClass.Healer) };
            RvrFightBoard board = RvrFightBoard.Build(1_000, 51, eRealm.Albion, members, enemies);
            // the lists the board was built from are copied, so later changes to them do not reach it
            members.Add(Head(eRealm.Albion, eCharacterClass.Armsman));
            enemies.Clear();
            RvrFightBoard changed = board with { Plan = board.Plan with { Stance = RvrStance.Retreat } };
            Assert.Multiple(() =>
            {
                Assert.That(board.Members, Has.Length.EqualTo(1));
                Assert.That(board.Enemies, Has.Length.EqualTo(1));
                Assert.That(board.Friendly.Count, Is.EqualTo(1));
                Assert.That(board.Plan.Stance, Is.EqualTo(RvrStance.Engage));
                Assert.That(changed.Plan.Stance, Is.EqualTo(RvrStance.Retreat));
                Assert.That(changed, Is.Not.SameAs(board));
            });
        }

        // ---- strength, centroids, the third-party flag ------------------------------------------------------------------

        [Test]
        public void Strength_counts_heads_healers_levels_health_and_power()
        {
            RvrStrength strength = RvrStrength.Of(
            [
                Head(eRealm.Albion, eCharacterClass.Cleric, level: 50, health: 100, power: 80),
                Head(eRealm.Albion, eCharacterClass.Cleric, level: 48, health: 60, power: 40),
                Head(eRealm.Albion, eCharacterClass.Sorcerer, level: 50, health: 90, power: 30),
                Head(eRealm.Albion, eCharacterClass.Armsman, level: 46, health: 50),
                Head(eRealm.Albion, eCharacterClass.Friar, level: 50, health: 100, power: 100),
                Pet(eRealm.Albion),
                null,
            ]);
            Assert.Multiple(() =>
            {
                Assert.That(strength.Count, Is.EqualTo(5), "pets are not heads");
                Assert.That(strength.Pets, Is.EqualTo(1));
                Assert.That(strength.Healers, Is.EqualTo(2), "main healers only: a Friar is support");
                Assert.That(strength.TotalLevels, Is.EqualTo(244));
                Assert.That(strength.AverageLevel, Is.EqualTo(49));
                Assert.That(strength.HighestLevel, Is.EqualTo(50));
                Assert.That(strength.HealthPercent, Is.EqualTo(80));
                Assert.That(strength.PowerUsers, Is.EqualTo(4), "the Armsman has no power");
                Assert.That(strength.PowerPercent, Is.EqualTo(63), "the average of those who use power: 62.5");
                Assert.That(RvrStrength.Of([]), Is.SameAs(RvrStrength.None));
                Assert.That(RvrStrength.Of(null), Is.SameAs(RvrStrength.None));
                Assert.That(RvrStrength.None.Count + RvrStrength.None.Healers + RvrStrength.None.AverageLevel, Is.Zero);
                Assert.That(RvrStrength.Of([Pet(eRealm.Midgard)]).Count, Is.Zero);
                Assert.That(RvrStrength.Of([Pet(eRealm.Midgard)]).Pets, Is.EqualTo(1));
            });
        }

        [Test]
        public void The_board_counts_both_sides_and_finds_their_centres()
        {
            RvrFightBoard board = RvrFightBoard.Build(7_000, 163, eRealm.Hibernia,
                [
                    Head(eRealm.Hibernia, eCharacterClass.Druid, x: 1_000, y: 1_000),
                    Head(eRealm.Hibernia, eCharacterClass.Hero, x: 1_200, y: 1_000),
                    Head(eRealm.Hibernia, eCharacterClass.Eldritch, x: 1_100, y: 1_300),
                    Pet(eRealm.Hibernia, x: 9_000, y: 9_000),
                ],
                [
                    Head(eRealm.Albion, eCharacterClass.Cleric, level: 50, x: 3_000, y: 1_000) with { TargetsGroup = true },
                    Head(eRealm.Albion, eCharacterClass.Mercenary, level: 50, x: 3_200, y: 1_200) with { Control = RvrControlState.Mezzed },
                    Pet(eRealm.Albion, x: 0, y: 0),
                ]);
            Assert.Multiple(() =>
            {
                Assert.That(board.BuiltAt, Is.EqualTo(7_000));
                Assert.That(board.RegionId, Is.EqualTo(163));
                Assert.That(board.Realm, Is.EqualTo(eRealm.Hibernia));
                Assert.That(board.Friendly.Count, Is.EqualTo(3));
                Assert.That(board.Friendly.Healers, Is.EqualTo(1));
                Assert.That(board.Friendly.Pets, Is.EqualTo(1));
                Assert.That(board.Enemy.Count, Is.EqualTo(2));
                Assert.That(board.Enemy.Healers, Is.EqualTo(1));
                Assert.That(board.Enemy.Pets, Is.EqualTo(1));
                Assert.That(board.FriendlyCentroid, Is.EqualTo(new Vector3(1_100, 1_100, 0)), "pets do not move the centre");
                Assert.That(board.EnemyCentroid, Is.EqualTo(new Vector3(3_100, 1_100, 0)));
                Assert.That(board.InContact, Is.True, "the Cleric targets a member");
                Assert.That(board.Enemies[1].IsControlled, Is.True);
                Assert.That(board.ThirdPartyFight, Is.False);
                Assert.That(board.Plan.IsNeutral, Is.True, "a board has the neutral plan until its builder sets one");
                Assert.That(board.State, Is.SameAs(RvrGroupState.Empty));
            });

            RvrFightBoard empty = RvrFightBoard.Build(7_000, 163, eRealm.Hibernia, [Head(eRealm.Hibernia, eCharacterClass.Druid)], null);
            Assert.Multiple(() =>
            {
                Assert.That(empty.Enemies, Is.Empty);
                Assert.That(empty.Enemy, Is.SameAs(RvrStrength.None));
                Assert.That(empty.EnemyCentroid, Is.Null);
                Assert.That(empty.InContact, Is.False);
                Assert.That(new RvrFightBoard().Members, Is.Empty, "never a default (unusable) array");
                Assert.That(new RvrFightBoard().Enemies, Is.Empty);
            });
        }

        [Test]
        public void A_fight_between_the_other_two_realms_is_flagged()
        {
            RvrCombatant albFightingMid = Head(eRealm.Albion, eCharacterClass.Mercenary) with { Fighting = true, TargetRealm = eRealm.Midgard };
            RvrCombatant albFightingUs = Head(eRealm.Albion, eCharacterClass.Mercenary) with { Fighting = true, TargetRealm = eRealm.Hibernia };
            RvrCombatant albHealingAlb = Head(eRealm.Albion, eCharacterClass.Cleric) with { Casting = true, TargetRealm = eRealm.Albion };
            RvrCombatant midCastingAtAlb = Head(eRealm.Midgard, eCharacterClass.Runemaster) with { Casting = true, TargetRealm = eRealm.Albion };
            RvrCombatant midLookingAtAlb = Head(eRealm.Midgard, eCharacterClass.Runemaster) with { TargetRealm = eRealm.Albion };
            RvrCombatant albHittingAMonster = Head(eRealm.Albion, eCharacterClass.Mercenary) with { Fighting = true, TargetRealm = eRealm.None };
            bool Sees(params RvrCombatant[] enemies) => RvrFightBoard.SeesThirdPartyFight(eRealm.Hibernia, enemies.ToImmutableArray());
            Assert.Multiple(() =>
            {
                Assert.That(Sees(albFightingMid), Is.True);
                Assert.That(Sees(midCastingAtAlb), Is.True);
                Assert.That(Sees(albFightingUs), Is.False, "that is our fight");
                Assert.That(Sees(albHealingAlb), Is.False, "a heal on a realm mate");
                Assert.That(Sees(midLookingAtAlb), Is.False, "a target alone is not a fight");
                Assert.That(Sees(albHittingAMonster), Is.False, "PvE");
                Assert.That(Sees(), Is.False);
                Assert.That(RvrFightBoard.Build(1, 1, eRealm.Hibernia, [Head(eRealm.Hibernia, eCharacterClass.Druid)],
                    [albFightingUs, albFightingMid]).ThirdPartyFight, Is.True);
            });
        }

        [Test]
        public void Every_class_has_a_role() => Assert.Multiple(() =>
        {
            Assert.That(RvrRoles.For(eCharacterClass.Cleric), Is.EqualTo(RvrRole.Healer));
            Assert.That(RvrRoles.For(eCharacterClass.Healer), Is.EqualTo(RvrRole.Healer));
            Assert.That(RvrRoles.For(eCharacterClass.Druid), Is.EqualTo(RvrRole.Healer));
            Assert.That(RvrRoles.For(eCharacterClass.Shaman), Is.EqualTo(RvrRole.Support));
            Assert.That(RvrRoles.For(eCharacterClass.Bard), Is.EqualTo(RvrRole.Support));
            Assert.That(RvrRoles.For(eCharacterClass.Minstrel), Is.EqualTo(RvrRole.Support));
            Assert.That(RvrRoles.For(eCharacterClass.Sorcerer), Is.EqualTo(RvrRole.Caster));
            Assert.That(RvrRoles.For(eCharacterClass.Runemaster), Is.EqualTo(RvrRole.Caster));
            Assert.That(RvrRoles.For(eCharacterClass.Eldritch), Is.EqualTo(RvrRole.Caster));
            Assert.That(RvrRoles.For(eCharacterClass.Armsman), Is.EqualTo(RvrRole.Tank));
            Assert.That(RvrRoles.For(eCharacterClass.Warrior), Is.EqualTo(RvrRole.Tank));
            Assert.That(RvrRoles.For(eCharacterClass.Hero), Is.EqualTo(RvrRole.Tank));
            Assert.That(RvrRoles.For(eCharacterClass.Mercenary), Is.EqualTo(RvrRole.Melee));
            Assert.That(RvrRoles.For(eCharacterClass.Berserker), Is.EqualTo(RvrRole.Melee));
            Assert.That(RvrRoles.For(eCharacterClass.Blademaster), Is.EqualTo(RvrRole.Melee));
            Assert.That(RvrRoles.For(eCharacterClass.Infiltrator), Is.EqualTo(RvrRole.Stealther));
            Assert.That(RvrRoles.For(eCharacterClass.Ranger), Is.EqualTo(RvrRole.Archer));
            Assert.That(RvrRoles.For(eCharacterClass.Unknown), Is.EqualTo(RvrRole.Unknown));
            foreach (eCharacterClass characterClass in Enum.GetValues<eCharacterClass>())
                if (characterClass != eCharacterClass.Unknown)
                    Assert.That(RvrRoles.For(characterClass), Is.Not.EqualTo(RvrRole.Unknown).And.Not.EqualTo(RvrRole.Pet), characterClass.ToString());
        });

        // ---- the plan and the group's stored facts ----------------------------------------------------------------------

        [Test]
        public void The_plan_builder_makes_the_neutral_plan()
        {
            RvrFightBoard board = RvrFightBoard.Build(9_000, 51, eRealm.Albion,
                [Head(eRealm.Albion, eCharacterClass.Cleric), Head(eRealm.Albion, eCharacterClass.Mercenary)],
                [Head(eRealm.Midgard, eCharacterClass.Healer) with { TargetsGroup = true }, Head(eRealm.Midgard, eCharacterClass.Runemaster)]);
            var state = new RvrGroupState { SkillTier = RvrSkillTier.Veteran, RallyPoint = new Vector3(10, 20, 30), RallyRegion = 51 };
            foreach (RvrTacticsSwitches switches in new[] { RvrTacticsSwitches.AllOn, RvrTacticsSwitches.AllOff })
            {
                RvrFightPlan plan = RvrFightPlanner.Next(null, board, state, switches, 9_000);
                Assert.Multiple(() =>
                {
                    Assert.That(plan.IsNeutral, Is.True);
                    Assert.That(plan.MainAssist, Is.Null);
                    Assert.That(plan.CalledTarget, Is.Null);
                    Assert.That(plan.CrowdControl, Is.Empty);
                    Assert.That(plan.Interrupts, Is.Empty);
                    Assert.That(plan.Protect, Is.Empty);
                    Assert.That(plan.Stance, Is.EqualTo(RvrStance.Engage));
                    Assert.That(plan.StanceSince, Is.EqualTo(9_000));
                    Assert.That(plan.BuiltAt, Is.EqualTo(9_000));
                    Assert.That(plan.SkillTier, Is.EqualTo(RvrSkillTier.Veteran));
                    Assert.That(plan.RallyPoint, Is.EqualTo(new Vector3(10, 20, 30)));
                    Assert.That(plan.RallyRegion, Is.EqualTo(51));
                });
            }
            // the next plan carries the one before it: the stance keeps its start time, the group's facts are refreshed
            RvrFightPlan first = RvrFightPlanner.Next(null, board, state, RvrTacticsSwitches.AllOn, 9_000);
            RvrFightPlan second = RvrFightPlanner.Next(first, board, state with { RallyPoint = null, RallyRegion = 0 },
                RvrTacticsSwitches.AllOn, 10_000);
            Assert.Multiple(() =>
            {
                Assert.That(second.BuiltAt, Is.EqualTo(10_000));
                Assert.That(second.StanceSince, Is.EqualTo(9_000));
                Assert.That(second.RallyPoint, Is.Null);
                Assert.That(second.IsNeutral, Is.True);
                Assert.That(RvrFightPlanner.Next(null, null, null, RvrTacticsSwitches.AllOn, 5).IsNeutral, Is.True);
                Assert.That(RvrFightPlan.None.IsNeutral, Is.True);
                Assert.That(RvrFightPlan.SubjectFor(first.Protect, null), Is.Null);
                Assert.That(Enum.GetNames<RvrStance>(), Is.EqualTo(new[] { "Observe", "Engage", "Hold", "Kite", "Retreat", "Regroup" }));
            });
        }

        [Test]
        public void A_groups_stored_facts_are_changed_by_making_a_new_record()
        {
            RvrGroupState empty = RvrGroupState.Empty;
            RvrGroupState wiped = empty.WithWipe(60_000).WithWipe(120_000);
            RvrGroupState rallied = wiped.WithRally(new Vector3(1, 2, 3), 163);
            Assert.Multiple(() =>
            {
                Assert.That(empty.SkillTier, Is.Null);
                Assert.That(empty.RecentWipes, Is.Empty);
                Assert.That(empty.RallyPoint, Is.Null);
                Assert.That(wiped.RecentWipes, Is.EqualTo(new long[] { 60_000, 120_000 }));
                Assert.That(wiped.WipesWithin(130_000), Is.EqualTo(2));
                Assert.That(wiped.WipesWithin(60_000 + RvrGroupState.WipeMemoryMilliseconds + 1), Is.EqualTo(1));
                Assert.That(wiped.WithWipe(60_000 + RvrGroupState.WipeMemoryMilliseconds + 1).RecentWipes, Has.Length.EqualTo(2),
                    "the stale wipe is dropped when a new one is added");
                Assert.That(rallied.RallyPoint, Is.EqualTo(new Vector3(1, 2, 3)));
                Assert.That(rallied.RallyRegion, Is.EqualTo(163));
                Assert.That(rallied.WithRally(null, 163).RallyRegion, Is.Zero);
                Assert.That(empty, Is.SameAs(RvrGroupState.Empty), "the shared empty record is untouched");
            });
            RvrGroupState many = empty;
            for (int i = 0; i < 20; i++)
                many = many.WithWipe(1_000 + i);
            Assert.That(many.RecentWipes, Has.Length.EqualTo(RvrGroupState.MaxWipes));
            Assert.That(many.RecentWipes[^1], Is.EqualTo(1_019));
        }

        [Test]
        public void Stored_facts_are_kept_per_group_and_updates_do_not_lose_each_other()
        {
            object group = new(), other = new();
            try
            {
                Assert.That(RvrFightBoards.StateOf(group), Is.SameAs(RvrGroupState.Empty));
                Assert.That(RvrFightBoards.StateOf(null), Is.SameAs(RvrGroupState.Empty));
                RvrFightBoards.UpdateState(group, state => state with { SkillTier = RvrSkillTier.Casual });
                Parallel.For(0, 200, i => RvrFightBoards.UpdateState(group, state => state.WithWipe(1_000 + i)));
                Assert.Multiple(() =>
                {
                    Assert.That(RvrFightBoards.StateOf(group).SkillTier, Is.EqualTo(RvrSkillTier.Casual));
                    Assert.That(RvrFightBoards.StateOf(group).RecentWipes, Has.Length.EqualTo(RvrGroupState.MaxWipes));
                    Assert.That(RvrFightBoards.StateOf(other), Is.SameAs(RvrGroupState.Empty), "another group");
                    Assert.That(RvrFightBoards.UpdateState(null, state => state), Is.SameAs(RvrGroupState.Empty));
                });
                RvrFightBoards.Forget(group);
                Assert.That(RvrFightBoards.StateOf(group), Is.SameAs(RvrGroupState.Empty));
            }
            finally
            {
                RvrFightBoards.Forget(group);
                RvrFightBoards.Forget(other);
            }
        }

        // ---- the log line's rate limit ------------------------------------------------------------------------------------

        [Test]
        public void The_board_log_line_is_rate_limited() => Assert.Multiple(() =>
        {
            long never = long.MinValue / 2;
            Assert.That(RvrFightBoards.ShouldLog(1_000, never, enemies: 0, stanceChanged: false), Is.False, "nothing in view: no line");
            Assert.That(RvrFightBoards.ShouldLog(1_000, never, enemies: 0, stanceChanged: true), Is.False);
            Assert.That(RvrFightBoards.ShouldLog(1_000, never, enemies: 3, stanceChanged: false), Is.True, "first contact");
            Assert.That(RvrFightBoards.ShouldLog(2_000, 1_000, enemies: 3, stanceChanged: false), Is.False);
            Assert.That(RvrFightBoards.ShouldLog(10_999, 1_000, enemies: 3, stanceChanged: false), Is.False);
            Assert.That(RvrFightBoards.ShouldLog(11_000, 1_000, enemies: 3, stanceChanged: false), Is.True, "every ten seconds");
            Assert.That(RvrFightBoards.ShouldLog(2_000, 1_000, enemies: 3, stanceChanged: true), Is.False);
            Assert.That(RvrFightBoards.ShouldLog(3_000, 1_000, enemies: 3, stanceChanged: true), Is.True, "a new stance sooner");

            var holder = new RvrFightBoards.Holder();
            RvrFightBoard Contact(long now) => RvrFightBoard.Build(now, 51, eRealm.Albion,
                [Head(eRealm.Albion, eCharacterClass.Cleric)], [Head(eRealm.Midgard, eCharacterClass.Healer)]);
            Assert.That(holder.TakeLogTurn(Contact(1_000)), Is.True);
            Assert.That(holder.TakeLogTurn(Contact(2_000)), Is.False);
            Assert.That(holder.TakeLogTurn(Contact(11_000)), Is.True);
            RvrFightBoard retreating = Contact(13_000);
            Assert.That(holder.TakeLogTurn(retreating with { Plan = retreating.Plan with { Stance = RvrStance.Retreat } }), Is.True);

            string line = RvrFightBoards.DescribeBoard(null, Contact(1_000), 4);
            Assert.That(line, Does.StartWith("RVR_FIGHT_BOARD "));
            foreach (string field in new[] { "group=", "region=51", "members=1", "enemies=1", "friendlyHealers=1", "enemyHealers=1",
                         "enemyLevel=50", "friendlyHealth=100", "friendlyPower=", "contact=False", "thirdParty=False",
                         "stance=Engage", "skill=Average", "wipes=0", "builds=4" })
                Assert.That(line, Does.Contain(field));
        });
    }
}
