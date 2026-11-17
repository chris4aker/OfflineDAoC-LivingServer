using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 4 (docs/RVR-FIELD-1.2.md, "Mind"): the stance machine (Observe, Engage, Hold, Kite,
    /// Retreat, Regroup) with its hysteresis, the odds, ragged retreats, the warband relay, the rest after a fight, the
    /// chase leash and wipe memory. Pure tests on board snapshots, plus the scope guard on the live hooks. Tests only:
    /// nothing here is seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrEngagement : DutyLeaseFixture
    {
        private const long T0 = 1_000_000;
        private static readonly RvrEngagementOptions On = RvrEngagementOptions.AllOn;

        private bool _field, _sizeUp, _disengage, _regroup, _groupRetreat;

        [SetUp]
        public void SwitchesOn()
        {
            BotDutyLeases.Clear();
            (_field, _sizeUp, _disengage, _regroup, _groupRetreat) = (Properties.RVR_BOT_FIELD_TACTICS, Properties.RVR_BOT_SIZE_UP,
                Properties.RVR_BOT_DISENGAGE, Properties.RVR_BOT_REGROUP, BotGroupRetreat.Enabled);
            Properties.RVR_BOT_FIELD_TACTICS = Properties.RVR_BOT_SIZE_UP = Properties.RVR_BOT_DISENGAGE = Properties.RVR_BOT_REGROUP = true;
            BotGroupRetreat.Enabled = true;
        }

        [TearDown]
        public void RestoreSwitches()
        {
            (Properties.RVR_BOT_FIELD_TACTICS, Properties.RVR_BOT_SIZE_UP, Properties.RVR_BOT_DISENGAGE, Properties.RVR_BOT_REGROUP,
                BotGroupRetreat.Enabled) = (_field, _sizeUp, _disengage, _regroup, _groupRetreat);
            BotDutyLeases.Clear();
        }

        private static RvrCombatant Head(eRealm realm, eCharacterClass characterClass, int x = 0, int y = 0, int level = 50,
            int health = 100, int? power = null, bool targetsGroup = false, RvrControlState control = RvrControlState.None,
            eRealm targetRealm = eRealm.None, bool fighting = false) => new()
        {
            Realm = realm, Class = characterClass, Role = RvrRoles.For(characterClass), Level = level, HealthPercent = health,
            UsesPower = power.HasValue, PowerPercent = power ?? 0, X = x, Y = y, TargetsGroup = targetsGroup, Control = control,
            TargetRealm = targetRealm, Fighting = fighting,
        };

        /// <summary>An Albion eight: two clerics, a sorcerer, a theurgist, a paladin, a minstrel, two mercenaries.</summary>
        private static List<RvrCombatant> Eight(int x = 0, int health = 100, int power = 100, int spread = 100) =>
        [
            Head(eRealm.Albion, eCharacterClass.Cleric, x, 0, health: health, power: power),
            Head(eRealm.Albion, eCharacterClass.Cleric, x + spread, 0, health: health, power: power),
            Head(eRealm.Albion, eCharacterClass.Sorcerer, x, spread, health: health, power: power),
            Head(eRealm.Albion, eCharacterClass.Theurgist, x + spread, spread, health: health, power: power),
            Head(eRealm.Albion, eCharacterClass.Paladin, x - spread, 0, health: health, power: power),
            Head(eRealm.Albion, eCharacterClass.Minstrel, x, -spread, health: health, power: power),
            Head(eRealm.Albion, eCharacterClass.Mercenary, x - spread, spread, health: health),
            Head(eRealm.Albion, eCharacterClass.Mercenary, x + spread, -spread, health: health),
        ];

        private static List<RvrCombatant> Enemies(int count, int x = 3_000, bool contact = false, eCharacterClass c = eCharacterClass.Shaman,
            eRealm realm = eRealm.Midgard) =>
            Enumerable.Range(0, count).Select(i => Head(realm, c, x + i % 10 * 50, i / 10 * 50, targetsGroup: contact)).ToList();

        private static RvrFightBoard Board(IEnumerable<RvrCombatant> members, IEnumerable<RvrCombatant> enemies, long now = T0) =>
            RvrFightBoard.Build(now, 163, eRealm.Albion, members, enemies);

        private static RvrEngagementFacts Facts(long noticed = T0, long encounter = T0, int patience = 5, double boldness = 0.5) => new()
        {
            GroupKey = 4242, GroupSize = 8, Patience = patience, Boldness = boldness, LastNoticedAt = noticed, EncounterSince = encounter,
        };

        private static RvrGroupState State(RvrEngagementFacts facts) => RvrGroupState.Empty with { Engagement = facts, SkillTier = RvrSkillTier.Average };

        /// <summary>One decision step from a stance taken at <paramref name="since"/>.</summary>
        private static RvrStanceDecision Step(RvrStance current, long since, RvrFightBoard board, RvrGroupState state, long now,
            RvrSkillTier tier = RvrSkillTier.Average, RvrEngagementOptions? options = null) =>
            RvrEngagement.Decide(current, since, RvrEngagement.SizeUp(board, state.Engagement, tier), state.Engagement, state,
                options ?? On, tier, now);

        // ---- the odds ------------------------------------------------------------------------------------------------------

        private static RvrStrength Side(int count = 8, int level = 50, int health = 100, int power = 100, int powerUsers = 6,
            int healers = 2, int controlled = 0, int pets = 0) => new()
        {
            Count = count, TotalLevels = level * count, HighestLevel = level, HealthPercent = health, PowerPercent = power,
            PowerUsers = Math.Min(powerUsers, count), Healers = Math.Min(healers, count), Controlled = controlled, Pets = pets,
        };

        [Test]
        public void The_odds_rise_with_every_friendly_strength_and_fall_with_every_enemy_one()
        {
            RvrStrength enemy = Side();
            var friendly = new Func<int, RvrStrength>[]
            {
                v => Side(count: 2 + v), v => Side(level: 30 + v * 2), v => Side(health: v * 10), v => Side(power: v * 10),
                v => Side(healers: v), v => Side(controlled: 8 - v), v => Side(pets: v),
            };
            foreach (Func<int, RvrStrength> make in friendly)
            {
                double last = -1;
                for (int v = 0; v <= 8; v++)
                {
                    double odds = RvrEngagement.Odds(make(v), enemy);
                    Assert.That(odds, Is.GreaterThanOrEqualTo(last), "friendly side stronger: odds never fall");
                    Assert.That(RvrEngagement.Odds(enemy, make(v)), Is.LessThanOrEqualTo(1 - last + 1e-12), "enemy stronger: odds never rise");
                    last = odds;
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.Odds(Side(), Side()), Is.EqualTo(0.5).Within(1e-9), "even sides");
                Assert.That(RvrEngagement.Odds(Side(), RvrStrength.None), Is.EqualTo(1), "no enemy");
                Assert.That(RvrEngagement.Odds(RvrStrength.None, Side()), Is.Zero);
                Assert.That(RvrEngagement.Odds(Side(count: 8), Side(count: 19)), Is.LessThan(RvrEngagement.RetreatOdds), "8 against 19 is bad odds");
                Assert.That(RvrEngagement.Odds(Side(count: 8), Side(count: 9)), Is.GreaterThan(RvrEngagement.EngageOdds), "8 against 9 is fair");
                Assert.That(RvrEngagement.Power(Side(healers: 5)), Is.EqualTo(RvrEngagement.Power(Side(healers: 3))), "healers count up to three");
                Assert.That(RvrEngagement.Power(Side(level: 40)), Is.EqualTo(RvrEngagement.Power(Side()) * 0.64).Within(1e-9));
            });
        }

        [Test]
        public void The_strength_counts_make_up_control_and_one_realm_of_a_side()
        {
            var side = new List<RvrCombatant>
            {
                Head(eRealm.Midgard, eCharacterClass.Warrior), Head(eRealm.Midgard, eCharacterClass.Berserker, control: RvrControlState.Mezzed),
                Head(eRealm.Midgard, eCharacterClass.Healer), Head(eRealm.Hibernia, eCharacterClass.Druid, control: RvrControlState.Rooted),
            };
            RvrStrength all = RvrStrength.Of(side), midgard = RvrStrength.Of(side, eRealm.Midgard);
            Assert.Multiple(() =>
            {
                Assert.That(all.Count, Is.EqualTo(4));
                Assert.That(all.MeleeHeads, Is.EqualTo(2));
                Assert.That(all.RangedHeads, Is.EqualTo(2));
                Assert.That(all.Controlled, Is.EqualTo(1), "a root does not take a head out of the fight");
                Assert.That(midgard.Count, Is.EqualTo(3));
                Assert.That(midgard.Healers, Is.EqualTo(1));
                Assert.That(RvrStrength.Of(side, eRealm.Albion), Is.SameAs(RvrStrength.None));
            });
        }

        [Test]
        public void A_fight_between_the_other_two_realms_is_sized_up_against_the_stronger_of_them()
        {
            var enemies = Enemies(6, realm: eRealm.Midgard).Concat(Enemies(3, realm: eRealm.Hibernia, c: eCharacterClass.Hero)).ToList();
            enemies[0] = enemies[0] with { Fighting = true, TargetRealm = eRealm.Hibernia };
            RvrFightBoard board = Board(Eight(), enemies);
            RvrSizeUp s = RvrEngagement.SizeUp(board, Facts(), RvrSkillTier.Average);
            Assert.Multiple(() =>
            {
                Assert.That(s.ThirdParty, Is.True);
                Assert.That(s.Enemy.Count, Is.EqualTo(6), "the Midgard six, not all nine");
                Assert.That(s.Odds, Is.GreaterThan(RvrEngagement.Odds(board.Friendly, board.Enemy)));
            });
        }

        // ---- watching and the size-up decision -----------------------------------------------------------------------------

        [Test]
        public void A_noticed_enemy_group_is_watched_then_engaged_kited_or_fled_by_the_odds()
        {
            RvrGroupState state = State(Facts());
            RvrFightBoard fair = Board(Eight(), Enemies(6));
            RvrStanceDecision first = Step(RvrStance.Engage, T0 - 60_000, fair, state, T0);
            Assert.That(first.Stance, Is.EqualTo(RvrStance.Observe), "the neutral Engage is not locked: the group watches at once");
            long watch = RvrEngagement.WatchMilliseconds(state.Engagement, RvrSkillTier.Average, T0);
            Assert.Multiple(() =>
            {
                Assert.That(Step(RvrStance.Observe, T0, fair, state, T0 + watch - 1).Stance, Is.EqualTo(RvrStance.Observe), "still watching");
                Assert.That(Step(RvrStance.Observe, T0, fair, state, T0 + watch).Stance, Is.EqualTo(RvrStance.Engage), "fair odds: engage");
                Assert.That(Step(RvrStance.Observe, T0, Board(Eight(), Enemies(17)), state, T0 + watch).Stance, Is.EqualTo(RvrStance.Kite),
                    "below fair: fight while backing away");
                Assert.That(Step(RvrStance.Observe, T0, Board(Eight(), Enemies(40)), state, T0 + watch).Stance, Is.EqualTo(RvrStance.Retreat),
                    "bad odds: run");
            });
        }

        [Test]
        public void Only_a_noticed_enemy_counts_and_a_lone_enemy_is_not_watched()
        {
            RvrFightBoard board = Board(Eight(), Enemies(6));
            RvrGroupState unaware = State(Facts(noticed: T0 - 60_000, encounter: T0 - 60_000));
            Assert.Multiple(() =>
            {
                Assert.That(Step(RvrStance.Engage, T0 - 90_000, board, unaware, T0).Stance, Is.EqualTo(RvrStance.Engage),
                    "on the board but not noticed by anyone: no stance (perception rules)");
                Assert.That(Step(RvrStance.Engage, T0 - 90_000, Board(Eight(), Enemies(1)), State(Facts()), T0).Stance, Is.EqualTo(RvrStance.Engage),
                    "one enemy: no watch");
                var third = Enemies(1, realm: eRealm.Midgard).Concat(Enemies(1, realm: eRealm.Hibernia)).ToList();
                third[0] = third[0] with { Fighting = true, TargetRealm = eRealm.Hibernia };
                Assert.That(Step(RvrStance.Engage, T0 - 90_000, Board(Eight(), third), State(Facts()), T0).Reason,
                    Is.EqualTo("third-party fight in view"));
            });
        }

        [Test]
        public void Impatient_groups_stop_watching_early_and_veterans_watch_longest()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.WatchMilliseconds(RvrSkillTier.Average, 0, 0.5), Is.LessThan(RvrEngagement.WatchMilliseconds(RvrSkillTier.Average, 9, 0.5)));
                Assert.That(RvrEngagement.WatchMilliseconds(RvrSkillTier.Casual, 5, 0.5), Is.LessThan(RvrEngagement.WatchMilliseconds(RvrSkillTier.Average, 5, 0.5)));
                Assert.That(RvrEngagement.WatchMilliseconds(RvrSkillTier.Average, 5, 0.5), Is.LessThan(RvrEngagement.WatchMilliseconds(RvrSkillTier.Veteran, 5, 0.5)));
                Assert.That(RvrEngagement.WatchMilliseconds(RvrSkillTier.Veteran, 9, 1), Is.LessThanOrEqualTo(RvrEngagement.MaxWatchMilliseconds));
            });
            // An impatient group decides at the 5 s floor; a patient one is still watching then.
            RvrFightBoard board = Board(Eight(), Enemies(6));
            RvrGroupState impatient = State(Facts(patience: 0)), patient = State(Facts(patience: 9));
            long at = T0 + RvrEngagement.MinStanceMilliseconds;
            Assert.That(Step(RvrStance.Observe, T0, board, impatient, at, RvrSkillTier.Casual).Stance, Is.EqualTo(RvrStance.Engage));
            Assert.That(Step(RvrStance.Observe, T0, board, patient, at, RvrSkillTier.Veteran).Stance, Is.EqualTo(RvrStance.Observe));
        }

        // ---- hysteresis --------------------------------------------------------------------------------------------------

        [Test]
        public void A_chosen_stance_lasts_five_seconds_but_contact_ends_a_watch_hold_or_regroup_at_once()
        {
            RvrGroupState state = State(Facts());
            RvrFightBoard bad = Board(Eight(), Enemies(40, contact: true));
            // Engage chosen at T1, after the encounter began at T0: bad odds wait out the 5 s.
            const long T1 = T0 + 1_000;
            Assert.That(Step(RvrStance.Engage, T1, bad, state, T1 + 4_999).Stance, Is.EqualTo(RvrStance.Engage));
            Assert.That(Step(RvrStance.Engage, T1, bad, state, T1 + 4_999).Reason, Is.EqualTo("held"));
            Assert.That(Step(RvrStance.Engage, T1, bad, state, T1 + 5_000).Stance, Is.EqualTo(RvrStance.Retreat));
            // Kite and Retreat hold too.
            RvrFightBoard fair = Board(Eight(), Enemies(5, contact: true));
            Assert.That(Step(RvrStance.Kite, T0, fair, state, T0 + 1_000).Stance, Is.EqualTo(RvrStance.Kite));
            Assert.That(Step(RvrStance.Kite, T0, fair, state, T0 + 5_000).Stance, Is.EqualTo(RvrStance.Engage));
            RvrFightBoard empty = Board(Eight(), []);
            RvrGroupState gone = State(Facts(noticed: T0 - 60_000));
            Assert.That(Step(RvrStance.Retreat, T0, empty, gone, T0 + 2_000).Stance, Is.EqualTo(RvrStance.Retreat));
            Assert.That(Step(RvrStance.Retreat, T0, empty, gone, T0 + 5_000).Stance, Is.EqualTo(RvrStance.Regroup));
            // Contact is not held back.
            foreach (RvrStance waiting in new[] { RvrStance.Observe, RvrStance.Hold, RvrStance.Regroup })
                Assert.That(Step(waiting, T0, fair, state, T0 + 100).Stance, Is.EqualTo(RvrStance.Engage), waiting.ToString());
        }

        [Test]
        public void Every_transition_of_the_machine()
        {
            RvrGroupState state = State(Facts());
            RvrFightBoard spread = Board(Eight(spread: 900), Enemies(6));
            RvrFightBoard together = Board(Eight(), Enemies(6));
            long later = T0 + 6_000;
            Assert.Multiple(() =>
            {
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, spread, state, T0).Stance, Is.EqualTo(RvrStance.Hold), "Engage(undecided) -> Hold");
                Assert.That(Step(RvrStance.Hold, T0, spread, state, T0 + 6_000).Stance, Is.EqualTo(RvrStance.Hold), "still strung out");
                Assert.That(Step(RvrStance.Hold, T0, spread, state, T0 + RvrEngagement.MaxHoldMilliseconds).Stance, Is.EqualTo(RvrStance.Observe),
                    "Hold -> Observe when the hold runs out");
                Assert.That(Step(RvrStance.Hold, T0, together, state, later).Stance, Is.EqualTo(RvrStance.Observe), "Hold -> Observe");
                Assert.That(Step(RvrStance.Observe, T0, spread, state, later).Stance, Is.EqualTo(RvrStance.Observe), "a watch does not go back to Hold");
                Assert.That(Step(RvrStance.Engage, T0, Board(Eight(), Enemies(17, contact: true)), state, later).Stance, Is.EqualTo(RvrStance.Kite), "Engage -> Kite");
                Assert.That(Step(RvrStance.Kite, T0, Board(Eight(), Enemies(40, contact: true)), state, later).Stance, Is.EqualTo(RvrStance.Retreat), "Kite -> Retreat");
                Assert.That(Step(RvrStance.Retreat, T0, Board(Eight(), Enemies(40, x: 1_000, contact: true)), state, later).Stance, Is.EqualTo(RvrStance.Retreat), "still running");
                Assert.That(Step(RvrStance.Retreat, T0, Board(Eight(), Enemies(40, x: 1_000, contact: true)), state, T0 + RvrEngagement.MaxRetreatMilliseconds).Stance,
                    Is.EqualTo(RvrStance.Regroup), "Retreat -> Regroup after 20 s at most");
                Assert.That(Step(RvrStance.Retreat, T0, Board(Eight(), Enemies(20, x: 4_000)), state, later).Stance, Is.EqualTo(RvrStance.Regroup),
                    "Retreat -> Regroup once clear");
                RvrGroupState quiet = State(Facts(noticed: T0 - 60_000));
                Assert.That(Step(RvrStance.Regroup, T0, Board(Eight(health: 50), []), quiet, later).Stance, Is.EqualTo(RvrStance.Regroup), "resting");
                Assert.That(Step(RvrStance.Regroup, T0, Board(Eight(), []), quiet, T0 + RvrEngagement.AverageMinRegroupMilliseconds).Stance,
                    Is.EqualTo(RvrStance.Engage), "Regroup -> Engage, rested (after the shortest regroup)");
                Assert.That(Step(RvrStance.Regroup, T0, Board(Eight(health: 50, spread: 900), []), quiet, T0 + RvrEngagement.MaxRegroupMilliseconds).Stance,
                    Is.EqualTo(RvrStance.Engage), "Regroup -> Engage, timed out");
                Assert.That(Step(RvrStance.Regroup, T0, Board(Eight(), Enemies(40, contact: true)), state, T0 + 100).Stance, Is.EqualTo(RvrStance.Retreat),
                    "caught regrouping by a zerg: run");
                Assert.That(Step(RvrStance.Kite, T0, Board(Eight(), []), quiet, later).Stance, Is.EqualTo(RvrStance.Engage), "Kite -> Engage, nothing in view");
            });
        }

        [Test]
        public void A_melee_heavy_enemy_is_kited_by_a_group_that_fights_from_range()
        {
            RvrGroupState state = State(Facts());
            var casters = Enumerable.Range(0, 6).Select(i => Head(eRealm.Albion, i < 2 ? eCharacterClass.Cleric : eCharacterClass.Wizard, i * 50, 0, power: 100)).ToList();
            RvrFightBoard board = Board(casters, Enemies(5, contact: true, c: eCharacterClass.Berserker));
            RvrSizeUp s = RvrEngagement.SizeUp(board, state.Engagement, RvrSkillTier.Average);
            Assert.That(s.Odds, Is.GreaterThan(RvrEngagement.EngageOdds), "fair odds ...");
            Assert.That(s.KiteMelee, Is.True);
            Assert.That(Step(RvrStance.Engage, T0, board, state, T0 + 6_000).Stance, Is.EqualTo(RvrStance.Kite), "... but kite the melee train");
            RvrFightBoard few = Board(casters, Enemies(3, contact: true, c: eCharacterClass.Berserker).Select(e => e with { HealthPercent = 30 }));
            Assert.That(RvrEngagement.SizeUp(few, state.Engagement, RvrSkillTier.Average).Odds, Is.GreaterThan(RvrEngagement.KiteMeleeMaxOdds + 0.06));
            Assert.That(Step(RvrStance.Engage, T0, few, state, T0 + 6_000).Stance, Is.EqualTo(RvrStance.Engage), "a clearly weaker melee group is fought");
        }

        [Test]
        public void A_lost_fight_is_left_and_a_group_that_just_ran_fights_instead()
        {
            var members = Eight(health: 30);
            members.RemoveAll(m => m.Role == RvrRole.Healer);
            var enemies = Enemies(5, contact: true);
            enemies.Add(Head(eRealm.Midgard, eCharacterClass.Healer, 3_000, 0, targetsGroup: true));
            RvrFightBoard lost = Board(members, enemies);
            RvrGroupState state = State(Facts());
            Assert.That(RvrEngagement.SizeUp(lost, state.Engagement, RvrSkillTier.Average).Odds, Is.LessThan(RvrEngagement.LostOdds));
            Assert.That(Step(RvrStance.Engage, T0, lost, state, T0 + 6_000).Reason, Does.Contain("fight lost").Or.Contain("bad odds"));
            RvrGroupState ran = State(Facts() with { LastRetreatAt = T0 });
            Assert.That(Step(RvrStance.Engage, T0, Board(Eight(), Enemies(40, contact: true)), ran, T0 + 6_000).Stance, Is.EqualTo(RvrStance.Kite),
                "within a minute of its last run: kill and then run, not run again");
        }

        // ---- settings ----------------------------------------------------------------------------------------------------

        [Test]
        public void Bot_veteran_retreat_off_turns_the_retreat_off_and_kiting_stays()
        {
            RvrEngagementOptions noGroupRetreat = RvrEngagementOptions.From(RvrTacticsSwitches.AllOn, groupRetreatEnabled: false);
            Assert.That(noGroupRetreat.Retreat, Is.False);
            Assert.That(noGroupRetreat.Kite, Is.True);
            Assert.That(RvrEngagementOptions.From(RvrTacticsSwitches.AllOff, true).Any, Is.False);
            Assert.That(RvrEngagementOptions.From(RvrTacticsSwitches.AllOn with { Disengage = false }, true), Is.EqualTo(new RvrEngagementOptions(true, false, false, true)));
            RvrGroupState state = State(Facts());
            RvrFightBoard zerg = Board(Eight(), Enemies(40, contact: true));
            Assert.That(Step(RvrStance.Engage, T0, zerg, state, T0 + 6_000, options: noGroupRetreat).Stance, Is.EqualTo(RvrStance.Kite));
            Assert.That(Step(RvrStance.Engage, T0, zerg, state, T0 + 6_000, options: new(true, false, false, true)).Stance, Is.EqualTo(RvrStance.Engage),
                "rvr_bot_disengage off: fight on");
            Assert.That(Step(RvrStance.Engage, T0 - 60_000, Board(Eight(), Enemies(6)), state, T0, options: new(false, true, true, true)).Stance,
                Is.Not.EqualTo(RvrStance.Observe), "rvr_bot_size_up off: no watching");
        }

        [Test]
        public void The_plan_step_keeps_a_solo_bot_and_switched_off_settings_neutral_and_sets_a_regroups_rally()
        {
            RvrFightBoard board = Board(Eight(), Enemies(6));
            RvrFightPlan neutral = RvrFightPlan.Neutral(T0 - 60_000, State(Facts()));
            RvrGroupState solo = State(Facts() with { GroupSize = 1 });
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.PlanStance(neutral, board, solo, RvrTacticsSwitches.AllOn, T0, true).Stance, Is.EqualTo(RvrStance.Engage));
                Assert.That(RvrEngagement.PlanStance(neutral, board, RvrGroupState.Empty, RvrTacticsSwitches.AllOn, T0, true), Is.SameAs(neutral),
                    "no facts stored yet (no WP4 hook ran): the plan is untouched");
                Assert.That(RvrEngagement.PlanStance(neutral with { Stance = RvrStance.Observe }, board, State(Facts()), RvrTacticsSwitches.AllOff, T0, true).Stance,
                    Is.EqualTo(RvrStance.Engage), "switched off: back to Engage");
                RvrFightPlan watching = RvrEngagement.PlanStance(neutral, board, State(Facts()), RvrTacticsSwitches.AllOn, T0, true);
                Assert.That(watching.Stance, Is.EqualTo(RvrStance.Observe));
                Assert.That(watching.StanceSince, Is.EqualTo(T0));
                Assert.That(RvrEngagement.PlanStance(watching, board, State(Facts()), RvrTacticsSwitches.AllOn, T0 + 1_000, true).StanceSince, Is.EqualTo(T0),
                    "the start time stays while the stance does");
                Assert.That(RvrFightPlanner.Next(neutral, board, State(Facts()), RvrTacticsSwitches.AllOn, T0).Stance, Is.EqualTo(RvrStance.Observe),
                    "through the planner");
                RvrGroupState rally = State(Facts(noticed: T0 - 60_000)).WithRally(new Vector3(500, 600, 0), 163);
                RvrFightPlan regroup = RvrEngagement.PlanStance(neutral with { Stance = RvrStance.Retreat, StanceSince = T0 - 10_000 },
                    Board(Eight(health: 50), []), rally, RvrTacticsSwitches.AllOn, T0, true);
                Assert.That(regroup.Stance, Is.EqualTo(RvrStance.Regroup));
                Assert.That(regroup.RallyPoint, Is.EqualTo(new Vector3(500, 600, 0)), "the retreat's end is the rally point");
            });
        }

        // ---- a bot that is hit always fights back --------------------------------------------------------------------------

        [Test]
        public void A_bot_that_is_hit_always_fights_back_whatever_the_stance()
        {
            foreach (RvrStance stance in Enum.GetValues<RvrStance>())
                Assert.Multiple(() =>
                {
                    Assert.That(RvrEngagement.MayTakeTarget(stance, hit: true, enemyUponGroup: false), Is.True, stance.ToString());
                    Assert.That(RvrEngagement.FightsBack(stance, hit: true), Is.True, stance.ToString());
                });
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.MayTakeTarget(RvrStance.Observe, false, false), Is.False, "a watcher does not open");
                Assert.That(RvrEngagement.MayTakeTarget(RvrStance.Observe, false, true), Is.True, "but takes an enemy on the group");
                Assert.That(RvrEngagement.MayTakeTarget(RvrStance.Retreat, false, true), Is.False, "a runner runs");
                Assert.That(RvrEngagement.MayTakeTarget(RvrStance.Kite, false, false), Is.True);
                Assert.That(RvrEngagement.HeedsRetreat(T0 + 5_000, T0, (300, false), hit: true), Is.False, "caught in melee: fights back");
                Assert.That(RvrEngagement.HeedsRetreat(T0 + 5_000, T0, (300, false), hit: false), Is.True);
                Assert.That(RvrEngagement.StopsChasing(9_000, 9_500, 1_500, hit: true), Is.False, "a hit chaser keeps fighting");
                Assert.That(RvrEngagement.WasHit(T0, T0 - RvrEngagement.HitMemoryMilliseconds), Is.True);
                Assert.That(RvrEngagement.WasHit(T0, T0 - RvrEngagement.HitMemoryMilliseconds - 1), Is.False);
                Assert.That(RvrEngagement.WasHit(T0, 0), Is.False);
            });
        }

        // ---- ragged retreats ---------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.20, 800, 2_000)]
        [TestCase(RvrSkillTier.Average, 0.10, 500, 1_600)]
        [TestCase(RvrSkillTier.Veteran, 0.04, 300, 1_000)]
        public void Retreats_are_ragged_by_tier(RvrSkillTier tier, double holdouts, int min, int max)
        {
            int held = 0, low = int.MaxValue, high = 0;
            const int n = 20_000;
            for (int i = 0; i < n; i++)
            {
                (int delay, bool holdsOut) = RvrEngagement.RetreatReaction(tier, 7_000_000 + i, 31);
                if (holdsOut)
                    held++;
                low = Math.Min(low, delay);
                high = Math.Max(high, delay);
                Assert.That(delay, Is.InRange(min, max));
            }
            Assert.Multiple(() =>
            {
                Assert.That((double)held / n, Is.EqualTo(holdouts).Within(0.015), "the share that never runs");
                Assert.That(low, Is.LessThan(min + 50), "the delays spread over the range");
                Assert.That(high, Is.GreaterThan(max - 50));
                Assert.That(RvrEngagement.RetreatReaction(tier, 5, 9), Is.EqualTo(RvrEngagement.RetreatReaction(tier, 5, 9)), "fixed per member and call");
                Assert.That(RvrEngagement.HeedsRetreat(T0 + min - 1, T0, (min, false), false), Is.False, "not before its delay");
                Assert.That(RvrEngagement.HeedsRetreat(T0 + 60_000, T0, (min, true), false), Is.False, "a holdout never runs");
            });
            Assert.That(RvrEngagement.ReactionRange(RvrSkillTier.Veteran).Min, Is.EqualTo(300), "the design's 0.3 to 2 s");
            Assert.That(RvrEngagement.ReactionRange(RvrSkillTier.Casual).Max, Is.EqualTo(2_000));
        }

        // ---- the warband relay -------------------------------------------------------------------------------------------

        [Test]
        public void A_retreat_or_regroup_is_passed_on_half_the_time_four_seconds_late()
        {
            int passed = 0;
            var random = new Random(5);
            for (int i = 0; i < 20_000; i++)
                if (RvrEngagement.RelayTo(RvrStance.Retreat, T0, random.NextDouble()) is { } relay)
                {
                    passed++;
                    Assert.That(relay.AppliesAt, Is.EqualTo(T0 + 4_000));
                    Assert.That(relay.ExpiresAt, Is.EqualTo(T0 + 4_000 + RvrEngagement.RelayValidMilliseconds));
                }
            Assert.Multiple(() =>
            {
                Assert.That(passed / 20_000.0, Is.EqualTo(0.5).Within(0.015));
                Assert.That(RvrEngagement.RelayChance, Is.EqualTo(AutonomousRvrPerception.WarbandRelayChance), "the perception calls' odds");
                Assert.That(RvrEngagement.RelayDelayMilliseconds, Is.EqualTo(AutonomousRvrPerception.WarbandRelayDelayMilliseconds));
                Assert.That(RvrEngagement.RelayTo(RvrStance.Regroup, T0, 0.1), Is.Not.Null);
                Assert.That(RvrEngagement.RelayTo(RvrStance.Engage, T0, 0.0), Is.Null, "only a retreat or regroup is passed on");
                Assert.That(RvrEngagement.RelayTo(RvrStance.Observe, T0, 0.0), Is.Null);
            });
        }

        [Test]
        public void A_relayed_retreat_is_acted_on_after_its_delay_and_only_once()
        {
            RvrStanceRelay relay = RvrEngagement.RelayTo(RvrStance.Retreat, T0, 0)!.Value;
            RvrGroupState told = State(Facts() with { Relay = relay });
            RvrFightBoard enemies = Board(Eight(), Enemies(6));
            Assert.Multiple(() =>
            {
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, enemies, told, T0 + 3_999).Stance, Is.EqualTo(RvrStance.Observe), "not yet arrived");
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, enemies, told, T0 + 4_000).Stance, Is.EqualTo(RvrStance.Retreat), "arrived: run with them");
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, Board(Eight(), []), State(Facts(noticed: T0 - 60_000) with { Relay = relay }), T0 + 4_000).Stance,
                    Is.EqualTo(RvrStance.Regroup), "nothing in view here: regroup instead");
                Assert.That(Step(RvrStance.Engage, T0 + 5_000, enemies, told, T0 + 12_000).Stance, Is.Not.EqualTo(RvrStance.Retreat),
                    "a relay older than the current stance is spent");
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, enemies, told, relay.ExpiresAt).Stance, Is.Not.EqualTo(RvrStance.Retreat), "expired");
            });
        }

        // ---- after the fight ---------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.10)]
        [TestCase(RvrSkillTier.Average, 0.50)]
        [TestCase(RvrSkillTier.Veteran, 0.90)]
        public void After_a_fight_veterans_leave_the_spot_and_casual_groups_rest_where_they_fought(RvrSkillTier tier, double share)
        {
            int moved = 0;
            var random = new Random(11);
            for (int i = 0; i < 20_000; i++)
                if (RvrEngagement.MovesOnAfterFight(tier, random.NextDouble()))
                    moved++;
            Assert.That(moved / 20_000.0, Is.EqualTo(share).Within(0.015));

            Vector3 fight = new(10_000, 10_000, 50);
            Vector3 there = RvrEngagement.AfterFightRestPoint(fight, new Vector3(12_000, 10_000, 0), movesOn: true);
            Assert.That(there, Is.EqualTo(new Vector3(10_000 - RvrEngagement.AfterFightMoveDistance, 10_000, 50)), "away from where the enemy came from");
            Assert.That(RvrEngagement.AfterFightRestPoint(fight, null, movesOn: false), Is.EqualTo(fight));
        }

        [Test]
        public void A_hurt_group_rests_after_its_fight_and_a_healthy_one_goes_on()
        {
            var facts = Facts(noticed: T0 - 30_000) with { LastContactAt = T0 - 5_000, LastFightPoint = new Vector3(1_000, 1_000, 0), LastEnemyPoint = new Vector3(3_000, 1_000, 0) };
            RvrStanceDecision hurt = Step(RvrStance.Engage, T0 - 20_000, Board(Eight(health: 50), []), State(facts), T0);
            Assert.Multiple(() =>
            {
                Assert.That(hurt.Stance, Is.EqualTo(RvrStance.Regroup));
                Assert.That(hurt.Reason, Is.EqualTo("after the fight"));
                Assert.That(hurt.Rally, Is.EqualTo(RvrEngagement.AfterFightRally(facts, RvrSkillTier.Average)));
                Assert.That(Step(RvrStance.Engage, T0 - 20_000, Board(Eight(), []), State(facts), T0).Stance, Is.EqualTo(RvrStance.Engage), "healthy: on");
                Assert.That(Step(RvrStance.Engage, T0 - 20_000, Board(Eight(health: 50), []), State(facts), T0 + RvrEngagement.AfterFightMilliseconds).Stance,
                    Is.EqualTo(RvrStance.Engage), "long after the fight: the normal recovery");
                Assert.That(Step(RvrStance.Engage, T0 - 1_000, Board(Eight(health: 50), []), State(facts), T0).Stance, Is.EqualTo(RvrStance.Engage),
                    "an Engage taken after the contact (a finished regroup) does not rest again");
                // The rally of one tier is the spot or 1,800 away from the enemy, never anywhere else.
                foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                {
                    Vector3 rally = RvrEngagement.AfterFightRally(facts, tier);
                    Assert.That(rally == facts.LastFightPoint || rally == new Vector3(1_000 - RvrEngagement.AfterFightMoveDistance, 1_000, 0), Is.True, tier.ToString());
                }
            });
        }

        // ---- chasing -----------------------------------------------------------------------------------------------------

        [Test]
        public void Bold_bots_chase_too_far_and_the_rest_stop_at_the_leash()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.ChaseLeash(RvrSkillTier.Veteran, 0.5), Is.EqualTo(1_500));
                Assert.That(RvrEngagement.ChaseLeash(RvrSkillTier.Average, 0.5), Is.EqualTo(2_000));
                Assert.That(RvrEngagement.ChaseLeash(RvrSkillTier.Casual, 0.5), Is.EqualTo(2_500));
                Assert.That(RvrEngagement.ChaseLeash(RvrSkillTier.Average, 1.0), Is.EqualTo(5_000), "a Daredevil chases into the guards");
                Assert.That(RvrEngagement.ChaseLeash(RvrSkillTier.Average, AutonomousRvrPerception.Boldness(null)), Is.EqualTo(2_000),
                    "personalities off: not bold");
                Assert.That(RvrEngagement.StopsChasing(2_100, 2_400, 2_000, false), Is.True, "past the leash, enemy running further");
                Assert.That(RvrEngagement.StopsChasing(1_900, 2_400, 2_000, false), Is.False, "inside the leash");
                Assert.That(RvrEngagement.StopsChasing(2_100, 1_800, 2_000, false), Is.False, "the enemy is coming back: not a chase");
                Assert.That(RvrEngagement.StopsChasing(2_100, 2_400, RvrEngagement.ChaseLeash(RvrSkillTier.Average, 1.0), false), Is.False, "bold");
            });
        }

        // ---- wipes -------------------------------------------------------------------------------------------------------

        [Test]
        public void A_group_remembers_its_wipes_and_fights_more_carefully()
        {
            var group = new object();
            try
            {
                Properties.RVR_BOT_FIELD_TACTICS = true;
                RvrEngagement.RecordWipe(group, T0);
                RvrEngagement.RecordWipe(group, T0 + 60_000);
                Assert.That(RvrEngagement.RecentWipes(group, T0 + 120_000), Is.EqualTo(2));
                Assert.That(RvrEngagement.RecentWipes(group, T0 + RvrGroupState.WipeMemoryMilliseconds + 60_001), Is.Zero, "ten minutes on, forgotten");
                Assert.That(RvrEngagement.RecentWipes(null, T0), Is.Zero);
            }
            finally
            {
                RvrFightBoards.Forget(group);
            }
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.EngageThreshold(1, 0.5), Is.GreaterThan(RvrEngagement.EngageThreshold(0, 0.5)));
                Assert.That(RvrEngagement.EngageThreshold(9, 0.5), Is.EqualTo(RvrEngagement.EngageThreshold(3, 0.5)), "capped at three");
                Assert.That(RvrEngagement.RetreatThreshold(2, 0.5), Is.GreaterThan(RvrEngagement.RetreatThreshold(0, 0.5)));
                Assert.That(RvrEngagement.EngageThreshold(0, 1.0), Is.LessThan(RvrEngagement.EngageThreshold(0, 0.0)), "a bold leader engages sooner");
                Assert.That(RvrEngagement.WipedRepeatedly(2), Is.False);
                Assert.That(RvrEngagement.WipedRepeatedly(3), Is.True, "AutonomousRvrDirector's rule: three wipes");
            });
            // The same just-fair fight (odds 0.51, a veteran group misreads by 0.03 at most): a fresh group engages, one with
            // three wipes kites.
            RvrFightBoard board = Board(Eight(), Enemies(10, contact: true));
            RvrGroupState fresh = State(Facts());
            RvrGroupState wiped = fresh.WithWipe(T0 - 3_000).WithWipe(T0 - 2_000).WithWipe(T0 - 1_000);
            Assert.That(Step(RvrStance.Engage, T0 - 10_000, board, fresh, T0, RvrSkillTier.Veteran).Stance, Is.EqualTo(RvrStance.Engage));
            Assert.That(Step(RvrStance.Engage, T0 - 10_000, board, wiped, T0, RvrSkillTier.Veteran).Stance, Is.EqualTo(RvrStance.Kite));
        }

        [Test]
        public void A_force_that_wiped_repeatedly_roams_before_it_hunts_again()
        {
            var force = new AutonomousRvrEventLayer.Force("wp4-wipes", eRealm.Albion, 3, 50, 1);
            Assert.That(AutonomousRvrEventLayer.ChooseIntent(force, true, false, false, 0.5), Is.EqualTo(AutonomousRvrEventLayer.Intent.HuntEnemy));
            Assert.That(AutonomousRvrEventLayer.ChooseIntent(force with { RecentWipes = 2 }, true, false, false, 0.5),
                Is.EqualTo(AutonomousRvrEventLayer.Intent.HuntEnemy));
            Assert.That(AutonomousRvrEventLayer.ChooseIntent(force with { RecentWipes = 3 }, true, false, false, 0.5),
                Is.EqualTo(AutonomousRvrEventLayer.Intent.Roam));
        }

        // ---- the runtime's pure parts and the group's own retreat order ----------------------------------------------------

        [Test]
        public void A_notice_after_a_quiet_spell_opens_an_encounter_and_contact_stores_the_fights_spot()
        {
            RvrEngagementFacts f = RvrEngagementRuntime.Noticed(RvrEngagementFacts.Empty, T0);
            Assert.That(f.EncounterSince, Is.EqualTo(T0));
            Assert.That(RvrEngagementRuntime.Noticed(f, T0 + 5_000).EncounterSince, Is.EqualTo(T0), "the same encounter");
            Assert.That(RvrEngagementRuntime.Noticed(f, T0 + RvrEngagement.AwareMilliseconds + 1).EncounterSince, Is.EqualTo(T0 + RvrEngagement.AwareMilliseconds + 1));
            RvrFightBoard board = Board(Eight(x: 500), Enemies(3, contact: true), T0 + 1_000);
            RvrEngagementFacts touched = RvrEngagementRuntime.Contact(f, board);
            Assert.Multiple(() =>
            {
                Assert.That(touched.LastContactAt, Is.EqualTo(T0 + 1_000));
                Assert.That(touched.LastFightPoint, Is.EqualTo(board.FriendlyCentroid));
                Assert.That(touched.LastEnemyPoint, Is.EqualTo(board.EnemyCentroid));
                Assert.That(RvrEngagementRuntime.Contact(f, Board(Eight(), Enemies(3))), Is.SameAs(f), "no contact: unchanged");
            });
        }

        [Test]
        public void The_retreat_order_is_published_on_retreat_and_cleared_by_the_board_itself()
        {
            Bot leader = Gamebot(objective: "RvR");
            leader.Region = 163;
            var group = new Group(leader);
            leader.Group = group;
            bool enabled = BotGroupRetreat.Enabled;
            try
            {
                Properties.RVR_BOT_DISENGAGE = true;
                BotGroupRetreat.Enabled = true;
                long now = GameLoop.GameLoopTime + 1_000;
                RvrFightBoard Plan(RvrStance stance, long since, long built) =>
                    Board(Eight(), Enemies(20, contact: true), built) with
                    {
                        State = RvrFightBoards.StateOf(group),
                        Plan = RvrFightPlan.Neutral(built, RvrGroupState.Empty) with { Stance = stance, StanceSince = since },
                    };
                RvrEngagementRuntime.Observe(leader, Plan(RvrStance.Engage, now - 10_000, now - 1_000), now - 1_000);
                RvrEngagementRuntime.Observe(leader, Plan(RvrStance.Retreat, now, now), now);
                BotGroupRetreat.Order order = BotGroupRetreat.Active(group, now);
                Assert.That(order, Is.Not.Null, "published through the group retreat every member reads");
                Assert.That(order.Reason, Does.StartWith(RvrEngagementRuntime.OrderReasonPrefix));
                Assert.That(order.Until, Is.EqualTo(now + RvrEngagement.MaxRetreatMilliseconds));
                Assert.That(RvrEngagementRuntime.IsOwnOrder(group, order, out long publishedAt), Is.True);
                Assert.That(publishedAt, Is.EqualTo(now));
                Assert.That(RvrFightBoards.StateOf(group).RallyPoint, Is.EqualTo(order.Point), "the retreat's end is the rally point");
                Assert.That(RvrFightBoards.StateOf(group).Engagement.LastRetreatAt, Is.EqualTo(now));
                Assert.That(order.Point.X, Is.LessThan(0), "away from the enemy (east of the group)");

                var pve = new BotGroupRetreat.Order { Region = 163, Until = now + 20_000 };
                Assert.That(RvrEngagementRuntime.IsOwnOrder(group, pve, out _), Is.False, "a PvE RUN is not the stance's");

                RvrEngagementRuntime.Observe(leader, Plan(RvrStance.Regroup, now + 6_000, now + 6_000), now + 6_000);
                Assert.That(BotGroupRetreat.Active(group, now + 6_000), Is.Null, "the RvR board clears its own order (CheckGroupRun is PvE-only)");
                Assert.That(RvrEngagementRuntime.IsOwnOrder(group, order, out _), Is.False);
            }
            finally
            {
                BotGroupRetreat.Enabled = enabled;
                BotGroupRetreat.Clear(group);
                RvrFightBoards.Forget(group);
            }
        }

        // ---- the regroup flap (owner's PC log, 1.2 test build 19e7037) --------------------------------------------------

        private static RvrFightBoard RegionBoard(ushort region, bool main, IEnumerable<RvrCombatant> members, IEnumerable<RvrCombatant> enemies,
            long now, RvrGroupState state, RvrFightPlan previous)
        {
            RvrFightBoard board = RvrFightBoard.Build(now, region, eRealm.Albion, members, enemies) with { State = state, MainBody = main };
            return board with { Plan = RvrEngagement.PlanStance(RvrFightPlanner.Carry(previous, state, now), board, state, RvrTacticsSwitches.AllOn, now, true) };
        }

        [Test]
        public void A_group_split_over_two_regions_has_one_stance_from_its_main_body()
        {
            RvrMemberSpot[] split =
            [
                new(1, 100, 100, false), new(1, 200, 100, false), new(1, 300, 100, false), new(1, 400, 100, false), new(1, 500, 100, false),
                new(100, 9_000, 9_000, true),
            ];
            RvrGroupBody body = RvrEngagement.MainBody(split);
            RvrGroupState state = State(Facts(noticed: T0 - 60_000));
            RvrFightPlan regrouping = RvrFightPlan.Neutral(T0 - 2_000, state) with { Stance = RvrStance.Regroup, StanceSince = T0 - 2_000 };
            RvrFightBoard stray = RegionBoard(100, false, [Head(eRealm.Albion, eCharacterClass.Cleric, power: 0)], Enemies(40, contact: true), T0, state, regrouping);
            Assert.Multiple(() =>
            {
                Assert.That(body.Region, Is.EqualTo(1), "most living members, even with the leader away");
                Assert.That(body.Members, Is.EqualTo(5));
                Assert.That(body.Centre, Is.EqualTo(new Vector2(300, 100)));
                Assert.That(RvrEngagement.MainBody([new(1, 0, 0, false), new(1, 0, 0, false), new(100, 0, 0, true), new(100, 0, 0, false)]).Region,
                    Is.EqualTo(100), "a tie goes to the leader's region");
                Assert.That(RvrEngagement.MainBody([new(200, 0, 0, false), new(1, 0, 0, false)]).Region, Is.EqualTo(1),
                    "a tie without the leader: the lowest region id");
                Assert.That(RvrEngagement.MainBody([]).Members, Is.EqualTo(0));
                Assert.That(stray.Plan.Stance, Is.EqualTo(RvrStance.Engage), "the stray's board has no stance of its own");
                Assert.That(stray.Plan.StanceReason, Is.EqualTo("away from the main body"));
                Assert.That(RvrEngagement.Track(RvrStanceLedger.Empty, stray).Acted, Is.False, "and never moves the group's runtime");
                Assert.That(RegionBoard(1, true, Eight(), Enemies(40, contact: true), T0, state, RvrFightPlan.Neutral(T0 - 60_000, state)).Plan.Stance,
                    Is.EqualTo(RvrStance.Retreat), "the main body's board decides");
            });
        }

        [Test]
        public void Alternating_boards_of_a_split_group_do_not_flap_its_regroup()
        {
            // Group 4110 in the log: 5 members in region 1, 1 (at 0 power) in region 100, a relayed regroup arriving at T0.
            RvrStanceRelay relay = RvrEngagement.RelayTo(RvrStance.Regroup, T0 - RvrEngagement.RelayDelayMilliseconds, 0)!.Value;
            RvrGroupState state = State(Facts(noticed: T0 - 60_000) with { Relay = relay });
            RvrFightPlan main = RvrFightPlan.Neutral(T0 - 60_000, state), stray = main;
            RvrStanceLedger ledger = RvrStanceLedger.Empty;
            var changes = new List<(long At, RvrStance From, RvrStance To, string Why)>();
            for (long t = T0; t <= T0 + 40_000; t += 1_000)
            {
                RvrFightBoard one = RegionBoard(1, true, Eight(), [], t, state, main);
                RvrFightBoard hundred = RegionBoard(100, false, [Head(eRealm.Albion, eCharacterClass.Cleric, power: 0)], [], t + 300, state, stray);
                (main, stray) = (one.Plan, hundred.Plan);
                // The two records are acted on in either order, as members of both regions think.
                foreach (RvrFightBoard board in (t / 1_000) % 2 == 0 ? new[] { one, hundred } : new[] { hundred, one })
                {
                    RvrStanceStep step = RvrEngagement.Track(ledger, board);
                    if (step.Changed)
                        changes.Add((board.BuiltAt, step.Previous, board.Plan.Stance, board.Plan.StanceReason));
                    ledger = step.Next;
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(changes.Count, Is.EqualTo(2), string.Join("; ", changes));
                Assert.That(changes[0], Is.EqualTo((T0, RvrStance.Engage, RvrStance.Regroup, "relayed regroup")));
                Assert.That(changes[1].To, Is.EqualTo(RvrStance.Engage));
                Assert.That(changes[1].At - changes[0].At, Is.EqualTo(RvrEngagement.AverageMinRegroupMilliseconds), "it lasts the shortest regroup, no less");
                Assert.That(ledger.Region, Is.EqualTo(1));
                Assert.That(RvrEngagement.Track(ledger, RegionBoard(1, true, Eight(), [], ledger.LastBuild - 500, state, main)).Acted, Is.False,
                    "an older build is not acted on");
            });
        }

        [TestCase(RvrSkillTier.Casual, 10_000)]
        [TestCase(RvrSkillTier.Average, 15_000)]
        [TestCase(RvrSkillTier.Veteran, 20_000)]
        public void A_regroup_lasts_its_tiers_shortest_time_unless_the_group_is_attacked(RvrSkillTier tier, long shortest)
        {
            RvrGroupState quiet = State(Facts(noticed: T0 - 60_000));
            RvrFightBoard rested = Board(Eight(), []);
            Assert.Multiple(() =>
            {
                Assert.That(RvrEngagement.MinRegroupMilliseconds(tier), Is.EqualTo(shortest));
                Assert.That(Step(RvrStance.Regroup, T0, rested, quiet, T0 + shortest - 1, tier).Stance, Is.EqualTo(RvrStance.Regroup), "rested, but too soon");
                Assert.That(Step(RvrStance.Regroup, T0, rested, quiet, T0 + shortest - 1, tier).Reason, Is.EqualTo("regrouping"));
                Assert.That(Step(RvrStance.Regroup, T0, rested, quiet, T0 + shortest, tier).Stance, Is.EqualTo(RvrStance.Engage));
                Assert.That(Step(RvrStance.Regroup, T0, Board(Eight(), Enemies(3, contact: true)), State(Facts()), T0 + 100, tier).Stance,
                    Is.EqualTo(RvrStance.Engage), "attacked: it fights at once");
                Assert.That(Step(RvrStance.Regroup, T0, rested, quiet, T0 + RvrEngagement.MinStanceMilliseconds, tier,
                    RvrEngagementOptions.AllOn with { Regroup = false }).Stance, Is.EqualTo(RvrStance.Engage), "switched off: over after the usual 5 s");
            });
        }

        [Test]
        public void Only_a_real_retreat_or_hurting_fight_is_passed_on_once_a_window_to_near_groups_in_the_region()
        {
            RvrGroupState quiet = State(Facts(noticed: T0 - 60_000));
            var fought = Facts(noticed: T0 - 30_000) with { LastContactAt = T0 - 5_000, LastFightPoint = new Vector3(1_000, 1_000, 0) };
            RvrStanceRelay relay = RvrEngagement.RelayTo(RvrStance.Regroup, T0 - 4_000, 0)!.Value;
            RvrStanceRelay retreatRelay = RvrEngagement.RelayTo(RvrStance.Retreat, T0 - 4_000, 0)!.Value;
            var here = new Vector2(10_000, 10_000);
            Assert.Multiple(() =>
            {
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, Board(Eight(), Enemies(40, contact: true)), State(Facts()), T0).Relays, Is.True, "a retreat from bad odds");
                Assert.That(Step(RvrStance.Retreat, T0 - 20_000, Board(Eight(), []), quiet, T0).Relays, Is.True, "the regroup after a retreat");
                Assert.That(Step(RvrStance.Engage, T0 - 20_000, Board(Eight(health: 50), []), State(fought), T0).Relays, Is.True, "a fight that hurt");
                RvrStanceDecision relayed = Step(RvrStance.Engage, T0 - 60_000, Board(Eight(), []), State(Facts(noticed: T0 - 60_000) with { Relay = relay }), T0);
                Assert.That((relayed.Stance, relayed.Relays), Is.EqualTo((RvrStance.Regroup, false)), "a relayed regroup is not passed on: no chains");
                RvrStanceDecision relayedRetreat = Step(RvrStance.Engage, T0 - 60_000, Board(Eight(), Enemies(6)), State(Facts() with { Relay = retreatRelay }), T0);
                Assert.That((relayedRetreat.Stance, relayedRetreat.Relays), Is.EqualTo((RvrStance.Retreat, false)), "nor a relayed retreat");
                Assert.That(Step(RvrStance.Engage, T0 - 20_000, Board(Eight(), []), State(fought), T0).Relays, Is.False, "nothing in view");
                RvrFightPlan plan = RvrEngagement.PlanStance(RvrFightPlan.Neutral(T0 - 60_000, quiet) with { Stance = RvrStance.Retreat, StanceSince = T0 - 20_000 },
                    Board(Eight(), []), quiet, RvrTacticsSwitches.AllOn, T0, true);
                Assert.That((plan.Stance, plan.StanceReason, plan.StanceRelays), Is.EqualTo((RvrStance.Regroup, "retreat over", true)), "the plan carries it");

                Assert.That(RvrEngagement.MayRelay(false, long.MinValue / 2, T0), Is.False);
                Assert.That(RvrEngagement.MayRelay(true, long.MinValue / 2, T0), Is.True);
                Assert.That(RvrEngagement.MayRelay(true, T0, T0 + RvrEngagement.RelayWindowMilliseconds - 1), Is.False, "once per window");
                Assert.That(RvrEngagement.MayRelay(true, T0, T0 + RvrEngagement.RelayWindowMilliseconds), Is.True);
                Assert.That(RvrEngagement.RelayWindowMilliseconds, Is.EqualTo(60_000));

                Assert.That(RvrEngagement.RelayRadius, Is.EqualTo(2_500));
                Assert.That(RvrEngagement.RelayReaches(1, here, new RvrGroupBody(1, here + new Vector2(2_500, 0), 8)), Is.True);
                Assert.That(RvrEngagement.RelayReaches(1, here, new RvrGroupBody(1, here + new Vector2(2_501, 0), 8)), Is.False, "too far");
                Assert.That(RvrEngagement.RelayReaches(1, here, new RvrGroupBody(100, here, 8)), Is.False, "another region");
                Assert.That(RvrEngagement.RelayReaches(1, here, default), Is.False, "nobody alive");
            });
        }

        [Test]
        public void Nothing_in_view_after_a_fight_at_good_health_and_power_goes_back_to_roaming()
        {
            var facts = Facts(noticed: T0 - 30_000) with { LastContactAt = T0 - 5_000, LastFightPoint = new Vector3(1_000, 1_000, 0) };
            RvrGroupState state = State(facts);
            Assert.Multiple(() =>
            {
                foreach (RvrStance from in new[] { RvrStance.Engage, RvrStance.Kite })
                {
                    RvrStanceDecision full = Step(from, T0 - 20_000, Board(Eight(), []), state, T0);
                    Assert.That((full.Stance, full.Reason), Is.EqualTo((RvrStance.Engage, "nothing in view")), $"{from}: full health and power");
                    Assert.That(Step(from, T0 - 20_000, Board(Eight(health: RvrEngagement.AfterFightHealth, power: RvrEngagement.AfterFightPower), []), state, T0).Stance,
                        Is.EqualTo(RvrStance.Engage), $"{from}: at the thresholds");
                    Assert.That(Step(from, T0 - 20_000, Board(Eight(health: RvrEngagement.AfterFightHealth - 1), []), state, T0).Stance,
                        Is.EqualTo(RvrStance.Regroup), $"{from}: hurt");
                    Assert.That(Step(from, T0 - 20_000, Board(Eight(power: RvrEngagement.AfterFightPower - 1), []), state, T0).Stance,
                        Is.EqualTo(RvrStance.Regroup), $"{from}: out of power");
                }
                Assert.That(Step(RvrStance.Engage, T0 - 60_000, Board(Eight(), []), State(Facts(noticed: T0 - 60_000)), T0).Stance, Is.EqualTo(RvrStance.Engage),
                    "no fight at all: on");
            });
        }

        // ---- scope guard on the live hooks ---------------------------------------------------------------------------------

        private static (Bot Bot, BotBrain Brain) GroupedFieldBot()
        {
            Bot bot = Gamebot(objective: "RvR");
            bot.Region = 250;
            bot.Group = new Group(bot);
            return (bot, new BotBrain { Body = bot });
        }

        [Test]
        public void Raids_battleground_assaults_keep_fights_and_player_groups_get_no_stance()
        {
            Properties.RVR_BOT_FIELD_TACTICS = Properties.RVR_BOT_SIZE_UP = Properties.RVR_BOT_DISENGAGE = Properties.RVR_BOT_REGROUP = true;
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo gate = typeof(AutonomousWorldBotController).GetMethod("RvrStanceGateThreats", hidden)!;
            MethodInfo hold = typeof(AutonomousWorldBotController).GetMethod("RvrTacticsTravelHold", hidden)!;
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            var cases = new List<(string Why, Bot Bot, BotBrain Brain)>();

            (Bot raider, BotBrain raidBrain) = GroupedFieldBot();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            cases.Add(("a raider", raider, raidBrain));

            Bot stormer = Gamebot(level: 22, objective: "Battleground");
            stormer.Region = 250;
            stormer.Group = new Group(stormer);
            BotDutyLeases.Claim(stormer, Kind.BattlegroundWave, "bg-rally", DutyLeaseReason.BattlegroundRally);
            cases.Add(("a battleground assault", stormer, new BotBrain { Body = stormer }));

            (Bot defender, BotBrain defenderBrain) = GroupedFieldBot();
            defender.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.DefendEvent);
            cases.Add(("a keep fight", defender, defenderBrain));

            Bot invited = Gamebot(objective: "RvR");
            invited.Region = 250;
            JoinPlayersGroup(invited, RealPlayer());
            cases.Add(("a player's group", invited, new BotBrain { Body = invited }));

            long builds = RvrFightBoards.Builds;
            var order = new BotGroupRetreat.Order { Region = 250, Until = GameLoop.GameLoopTime + 20_000 };
            GameLiving[] visible = [enemy];
            foreach ((string why, Bot bot, BotBrain brain) in cases)
                Assert.Multiple(() =>
                {
                    Assert.That(brain.RvrTacticsActive(), Is.False, why);
                    Assert.That(brain.RvrStanceRetreatOrder(order, GameLoop.GameLoopTime), Is.SameAs(order), why + ": the order unchanged");
                    Assert.That(brain.RvrStanceRetreatOrder(null, GameLoop.GameLoopTime), Is.Null, why);
                    var controller = new AutonomousWorldBotController();
                    Assert.That(gate.Invoke(controller, [brain, bot, visible]), Is.SameAs(visible), why + ": the threat gate unchanged");
                    Assert.That(hold.Invoke(controller, [brain, bot]), Is.EqualTo(false), why + ": no travel hold");
                    Assert.That(RvrFightBoards.Peek(bot), Is.Null, why);
                });
            Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds), "no board was built for any of them");
        }

        [Test]
        public void A_solo_bot_or_switched_off_settings_get_no_stance_either()
        {
            Properties.RVR_BOT_FIELD_TACTICS = true;
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo gate = typeof(AutonomousWorldBotController).GetMethod("RvrStanceGateThreats", hidden)!;
            Bot solo = Gamebot(objective: "RvR");
            solo.Region = 250;
            var soloBrain = new BotBrain { Body = solo };
            Assert.That(soloBrain.RvrTacticsActive(), Is.True, "in scope ...");
            GameLiving[] visible = [Gamebot(realm: eRealm.Midgard, objective: "RvR")];
            long builds = RvrFightBoards.Builds;
            Assert.That(gate.Invoke(new AutonomousWorldBotController(), [soloBrain, solo, visible]), Is.SameAs(visible), "... but solo");
            Assert.That(soloBrain.RvrStanceRetreatOrder(null, GameLoop.GameLoopTime), Is.Null);

            bool size = Properties.RVR_BOT_SIZE_UP, dis = Properties.RVR_BOT_DISENGAGE, re = Properties.RVR_BOT_REGROUP;
            try
            {
                Properties.RVR_BOT_SIZE_UP = Properties.RVR_BOT_DISENGAGE = Properties.RVR_BOT_REGROUP = false;
                (Bot grouped, BotBrain brain) = GroupedFieldBot();
                Assert.That(brain.RvrTacticsActive(), Is.True);
                Assert.That(gate.Invoke(new AutonomousWorldBotController(), [brain, grouped, visible]), Is.SameAs(visible), "all three off");
                var order = new BotGroupRetreat.Order { Region = 250, Until = GameLoop.GameLoopTime + 20_000 };
                Assert.That(brain.RvrStanceRetreatOrder(order, GameLoop.GameLoopTime), Is.SameAs(order));
            }
            finally
            {
                Properties.RVR_BOT_SIZE_UP = size;
                Properties.RVR_BOT_DISENGAGE = dis;
                Properties.RVR_BOT_REGROUP = re;
            }
            Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds));
        }
    }
}
