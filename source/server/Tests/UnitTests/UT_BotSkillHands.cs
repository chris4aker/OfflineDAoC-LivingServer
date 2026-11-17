using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people WP2 at the hook points (review 2026-11-17): what each live hook of BotSkillHands answers for real
    /// gamebot objects (DutyLeaseTestKit): a free gamebot is in; a raider, a stable rider, a companion and every bot with
    /// bot_skill_profiles off get today's rule from every hook; a running buff lapse is dropped once the bot is on a raid or
    /// a named is in play. Objects without a world: tests only.
    /// </summary>
    [TestFixture]
    public class UT_BotSkillHands : DutyLeaseFixture
    {
        private BotPeopleStore _previousStore;

        [SetUp]
        public void FreshState()
        {
            _previousStore = BotPeopleStore.UseForTests(new BotPeopleStore());
            BotSkill.ResetForTests();
            BotSkillHands.ResetForTests();
        }

        [TearDown]
        public void RestoreState()
        {
            BotPeopleStore.UseForTests(_previousStore);
            BotSkill.ResetForTests();
            BotSkillHands.ResetForTests();
            Properties.BOT_SKILL_PROFILES = true;
        }

        private static Bot Raider()
        {
            Bot raider = Gamebot();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            return raider;
        }

        private static Bot Rider()
        {
            Bot rider = Gamebot();
            typeof(GameBot).GetProperty(nameof(GameBot.IsOnStableMasterRoute))!.SetValue(rider, true);
            return rider;
        }

        private static GameNPC Monster(string name)
        {
            var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(npc, new ArrayList());
            npc.Name = name;
            npc.Realm = eRealm.None;
            return npc;
        }

        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group)!;
            foreach (GameLiving living in members)
            {
                list.Add(living);
                living.Group = group;
            }
            return group;
        }

        /// <summary>Every hook's answer is today's rule for this bot (loops: the hooks roll).</summary>
        private static void AssertTodaysRules(GameBot bot, string who)
        {
            for (int i = 0; i < 200; i++)
            {
                Assert.That(BotSkillHands.ReactionFactor(bot, out BotSkillProfile profile, out bool spared), Is.EqualTo(1), who);
                Assert.That(profile.IsNone, Is.True, who);
                Assert.That(spared, Is.False, who);
                Assert.That(BotSkillHands.BuffLapse(bot, bot, null, false), Is.EqualTo(0), who + ": no buff lapse");
                Assert.That(BotSkillHands.FireDelay(bot, RvrSkillTier.Casual), Is.EqualTo(-1), who + ": no step-out rule");
                Assert.That(BotSkillHands.RvrPlaceDelay(bot, RvrSkillTier.Casual, 1_234), Is.EqualTo(1_234), who);
                Assert.That(BotSkillHands.PowerRestFloor(bot), Is.EqualTo(AutonomousRestPolicy.PowerRestThreshold), who);
                Assert.That(BotSkillHands.PositionMistake(bot, Monster("a bog creeper"), false), Is.False, who);
                Assert.That(BotSkillHands.PullMistake(bot, [Monster("a bog creeper"), Monster("a bog creeper")]), Is.EqualTo(-1), who);
                Assert.That(BotSkillHands.LapseStillAllowed(bot, false), Is.False, who + ": a running lapse is dropped");
            }
        }

        [Test]
        public void A_free_gamebot_is_in_and_gets_its_profile()
        {
            Bot free = Gamebot();
            Assert.That(BotSkillHands.Allows(free, out BotSkillProfile profile, out bool spared), Is.True);
            Assert.That(profile.IsNone, Is.False);
            Assert.That(spared, Is.False);
            Assert.That(BotSkillHands.ReactionFactor(free, out _, out _), Is.EqualTo(BotSkillMistakes.ReactionFactor(profile)));
            Assert.That(BotSkillHands.FireDelay(free, RvrSkillTier.Average), Is.InRange(1_000L, 3_000L), "inside the group tier's band");
            Assert.That(BotSkillHands.RvrPlaceDelay(free, RvrSkillTier.Veteran, -1), Is.InRange(300L, 1_500L));
            Assert.That(BotSkillHands.LapseStillAllowed(free, false), Is.True);
            Assert.That(BotSkillHands.PullMistake(free, [Monster("a bog creeper")]), Is.EqualTo(-1), "a solo bot never takes an add");
        }

        [Test]
        public void A_raider_a_stable_rider_and_a_companion_keep_todays_rules_at_every_hook()
        {
            AssertTodaysRules(Raider(), "raider");
            AssertTodaysRules(Rider(), "stable rider");
            AssertTodaysRules(Companion(), "companion");
            AssertTodaysRules(SpecialForce(), "epic special force");
        }

        [Test]
        public void With_bot_skill_profiles_off_every_hook_keeps_todays_rule()
        {
            Properties.BOT_SKILL_PROFILES = false;
            AssertTodaysRules(Gamebot(), "switched off");
        }

        [Test]
        public void In_the_players_group_the_bot_is_spared()
        {
            Bot leader = Gamebot(), member = Gamebot();
            GroupOf(leader, member, RealPlayer());
            Assert.That(BotSkillHands.Allows(member, out BotSkillProfile profile, out bool spared), Is.True);
            Assert.That(spared, Is.True);
            Assert.That(BotSkillHands.ReactionFactor(member, out _, out _), Is.EqualTo(BotSkillMistakes.ReactionFactor(profile)),
                "its delays are not shortened");
        }

        [Test]
        public void A_running_lapse_is_dropped_when_the_bot_joins_a_raid_or_a_named_comes_into_play()
        {
            Bot bot = Gamebot();
            var lapses = new BotBuffLapses<GameLiving>();
            bool named = false;
            bool Still(GameBot b) => BotSkillHands.LapseStillAllowed(b, named);
            Assert.That(lapses.Waits(bot, 1, 1_000, (GameBot)bot, _ => 60_000, Still), Is.True, "a lapse starts");
            Assert.That(lapses.Waits(bot, 1, 5_000, (GameBot)bot, _ => 0, Still), Is.True, "and holds while nothing changed");
            named = true;
            Assert.That(lapses.Waits(bot, 1, 6_000, (GameBot)bot, _ => 60_000, Still), Is.False, "a named in play: renewed now");
            Assert.That(lapses.Waits(bot, 1, 7_000, (GameBot)bot, _ => 60_000, Still), Is.False, "and stays renewed (no new roll)");

            named = false;
            Assert.That(lapses.Waits(bot, 2, 1_000, (GameBot)bot, _ => 60_000, Still), Is.True);
            BotDutyLeases.Claim(bot, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            Assert.That(lapses.Waits(bot, 2, 9_000, (GameBot)bot, _ => 60_000, Still), Is.False, "a raid muster: renewed now");
        }

        [Test]
        public void A_groupmates_named_target_counts_as_a_named_in_play()
        {
            Bot bot = Gamebot(), mate = Gamebot(), third = Gamebot();
            GroupOf(bot, mate, third);
            Assert.That(BotSkillHands.GroupTargetsNamed(bot), Is.False);
            Assert.That(BotSkillHands.LapseStillAllowed(bot, false), Is.True);
            mate.TargetObject = Monster("a bog creeper");
            Assert.That(BotSkillHands.GroupTargetsNamed(bot), Is.False, "a plain monster is no boss");
            mate.TargetObject = Monster("Grimwood Keeper");
            Assert.That(BotSkillHands.GroupTargetsNamed(bot), Is.True);
            Assert.That(BotSkillHands.LapseStillAllowed(bot, false), Is.False);
            Assert.That(BotSkillHands.BuffLapse(bot, bot, null, false), Is.EqualTo(0), "no new lapse either");
        }
    }
}
