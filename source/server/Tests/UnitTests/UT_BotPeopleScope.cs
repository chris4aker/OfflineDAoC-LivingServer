using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;
using Refusal = DOL.GS.BotPeopleRefusal;
using Rule = DOL.GS.BotPeopleRule;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 0 (docs/BOTS-AS-PEOPLE-1.2.md, principle 4): the one scope test. Autonomous gamebots
    /// only; behaviour rules keep today's rules wherever the open-field RvR tactics do, plus stable rides and dungeon
    /// raids, and (but for mistakes, which are spared there) in a group with a real player. Records only need a person.
    /// The pure rule from plain values, then the same rule read from gamebots without a world. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotPeopleScope : DutyLeaseFixture
    {
        private static readonly Rule[] Behaviour = [Rule.Mistakes, Rule.WayFinding, Rule.Movement];

        [SetUp]
        public void Fresh()
        {
            BotDutyLeases.Clear();
            BotPeopleScope.TakeCounts();
        }

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        private static BotPeopleFacts Free => BotPeopleFacts.FreeGamebot;

        private static BotPeopleFacts With(Func<RvrScopeFacts, RvrScopeFacts> change) => Free with { Rvr = change(Free.Rvr) };

        // ---- the pure rule ---------------------------------------------------------------------------------------------

        [Test]
        public void A_free_gamebot_is_in_for_every_rule()
        {
            foreach (Rule rule in Enum.GetValues<Rule>())
                Assert.That(BotPeopleScope.Refusal(Free, rule), Is.EqualTo(Refusal.None), rule.ToString());
        }

        [Test]
        public void Nobody_but_a_saved_gamebot_is_ever_in() => Assert.Multiple(() =>
        {
            foreach (Rule rule in Enum.GetValues<Rule>())
            {
                Assert.That(BotPeopleScope.Refusal(default, rule), Is.EqualTo(Refusal.NotAGamebot), "nothing known: " + rule);
                Assert.That(BotPeopleScope.Refusal(With(f => f with { AutonomousGamebot = false }), rule), Is.EqualTo(Refusal.NotAGamebot), "a real player or NPC: " + rule);
                Assert.That(BotPeopleScope.Refusal(With(f => f with { Companion = true }), rule), Is.EqualTo(Refusal.NotAGamebot), "a companion: " + rule);
                Assert.That(BotPeopleScope.Refusal(Free with { InMemoryOnly = true }, rule), Is.EqualTo(Refusal.InMemoryOnly), "special forces, buffbots: " + rule);
            }
        });

        [Test]
        public void Each_fenced_off_duty_keeps_todays_rules_for_behaviour_with_its_reason() => Assert.Multiple(() =>
        {
            foreach (Rule rule in Behaviour)
            {
                Assert.That(BotPeopleScope.Refusal(With(f => f with { Duty = Kind.RealmRaid }), rule), Is.EqualTo(Refusal.RealmRaid), rule.ToString());
                Assert.That(BotPeopleScope.Refusal(With(f => f with { OnRealmRaid = true }), rule), Is.EqualTo(Refusal.RealmRaid));
                Assert.That(BotPeopleScope.Refusal(Free with { InRaidDungeon = true }, rule), Is.EqualTo(Refusal.DungeonRaid));
                Assert.That(BotPeopleScope.Refusal(With(f => f with { Duty = Kind.BattlegroundWave }), rule), Is.EqualTo(Refusal.BattlegroundAssault));
                Assert.That(BotPeopleScope.Refusal(With(f => f with { BattlegroundAssault = true }), rule), Is.EqualTo(Refusal.BattlegroundAssault));
                Assert.That(BotPeopleScope.Refusal(With(f => f with { RelicCarrier = true }), rule), Is.EqualTo(Refusal.RelicCarrier));
                Assert.That(BotPeopleScope.Refusal(With(f => f with { SiegeOperator = true }), rule), Is.EqualTo(Refusal.SiegeOperator));
                Assert.That(BotPeopleScope.Refusal(With(f => f with { AtObjective = true }), rule), Is.EqualTo(Refusal.AtObjective));
                Assert.That(BotPeopleScope.Refusal(Free with { StableRide = true }, rule), Is.EqualTo(Refusal.StableRide));
                Assert.That(BotPeopleScope.Refusal(With(f => f with { Duty = (Kind)200 }), rule), Is.EqualTo(Refusal.OtherDuty), "a lease kind the rule does not know");
                Assert.That(BotPeopleScope.Refusal(With(f => f with { Duty = Kind.RvrEvent }), rule), Is.EqualTo(Refusal.None), "an RvR marcher not at its keep");
                Assert.That(BotPeopleScope.Refusal(With(f => f with { Duty = Kind.BotGroupSession }), rule), Is.EqualTo(Refusal.None), "a bot-only group");
            }
        });

        [Test]
        public void Records_need_only_a_person_so_a_raider_still_earns_its_trophy()
        {
            BotPeopleFacts busy = With(f => f with
            {
                Duty = Kind.RealmRaid, OnRealmRaid = true, BattlegroundAssault = true, AtObjective = true, SiegeOperator = true,
                RelicCarrier = true, PlayerInGroup = true,
            }) with { InRaidDungeon = true, StableRide = true };
            Assert.That(BotPeopleScope.Refusal(busy, Rule.Record), Is.EqualTo(Refusal.None));
            Assert.That(Behaviour.Select(rule => BotPeopleScope.Refusal(busy, rule)), Has.None.EqualTo(Refusal.None));
        }

        [Test]
        public void Mistakes_apply_in_the_players_group_spared_but_movement_and_way_finding_do_not()
        {
            foreach (BotPeopleFacts withPlayer in new[]
                     {
                         With(f => f with { PlayerLedGroup = true }), With(f => f with { PlayerInGroup = true }),
                         With(f => f with { Duty = Kind.PlayerGroup }), With(f => f with { Duty = Kind.BotLedGroupWithPlayer }),
                     })
            {
                Assert.That(withPlayer.InPlayerGroup, Is.True);
                Assert.That(BotPeopleScope.Refusal(withPlayer, Rule.Mistakes), Is.EqualTo(Refusal.None), "spared, not refused (owner decision 4)");
                Assert.That(BotPeopleScope.Refusal(withPlayer, Rule.WayFinding), Is.EqualTo(Refusal.PlayerGroup));
                Assert.That(BotPeopleScope.Refusal(withPlayer, Rule.Movement), Is.EqualTo(Refusal.PlayerGroup));
                Assert.That(BotPeopleScope.Refusal(withPlayer, Rule.Record), Is.EqualTo(Refusal.None));
            }
            Assert.That(Free.InPlayerGroup, Is.False);
            Assert.That(BotPeopleScope.Refusal(With(f => f with { PlayerInGroup = true, Duty = Kind.RealmRaid }), Rule.Mistakes),
                Is.EqualTo(Refusal.RealmRaid), "a player on a raid does not open the raid");
            Assert.That(Enum.GetValues<Rule>().Where(BotPeopleScope.AllowsPlayerGroup), Is.EquivalentTo(new[] { Rule.Record, Rule.Mistakes }));
        }

        [Test]
        public void Behaviour_mirrors_the_rvr_tactics_scope_for_every_fact_it_shares()
        {
            // Every combination of the shared facts (no player: the player group is where the two scopes differ on purpose).
            int checkedCases = 0;
            foreach (Kind duty in Enum.GetValues<Kind>().Where(kind => !BotDutyLeaseRules.IsPlayerKind(kind)).Append((Kind)200))
                for (int bits = 0; bits < 1 << 7; bits++)
                {
                    var rvr = new RvrScopeFacts
                    {
                        AutonomousGamebot = (bits & 1) != 0, Companion = (bits & 2) != 0, OnRealmRaid = (bits & 4) != 0,
                        BattlegroundAssault = (bits & 8) != 0, AtObjective = (bits & 16) != 0, SiegeOperator = (bits & 32) != 0,
                        RelicCarrier = (bits & 64) != 0, Duty = duty,
                    };
                    bool rvrIn = RvrTacticsScope.Allows(rvr);
                    foreach (Rule rule in Behaviour)
                        Assert.That(BotPeopleScope.Allows(new BotPeopleFacts { Rvr = rvr }, rule), Is.EqualTo(rvrIn), $"{rule} {rvr}");
                    checkedCases++;
                }
            Assert.That(checkedCases, Is.GreaterThan(500));
        }

        [Test]
        public void Every_refusal_and_rule_has_a_log_name()
        {
            var names = Enum.GetValues<Refusal>().Select(BotPeopleScope.RefusalName).ToArray();
            Assert.That(names, Is.Unique);
            Assert.That(names, Has.All.Match("^[a-z_]+$"));
            Assert.That(Enum.GetValues<Rule>().Select(BotPeopleScope.RuleName), Is.Unique);
            Assert.That(Enum.GetValues<Rule>().Select(BotPeopleScope.RuleName), Has.All.Match("^[a-z]+$"));
        }

        // ---- the switches ----------------------------------------------------------------------------------------------

        [Test]
        public void A_master_switch_turns_its_items_parts_off()
        {
            Assert.That(BotPeopleSwitches.AllOn, Is.EqualTo(BotPeopleSwitches.From(true, true, true, true, true, true, true, true, true, true, true, true)));
            var noSkill = BotPeopleSwitches.From(false, true, true, true, true, true, true, true, true, true, true, true);
            Assert.That((noSkill.SkillProfiles, noSkill.SkillGrowth, noSkill.SparePlayerGroup, noSkill.RaidLearnsFromRaiders), Is.EqualTo((false, false, false, true)),
                "raid learning is not under the skill switch");
            var noPlaces = BotPeopleSwitches.From(true, true, true, true, false, true, true, true, true, true, true, true);
            Assert.That((noPlaces.AskTheWay, noPlaces.WrongRoads), Is.EqualTo((false, false)));
            var noMovement = BotPeopleSwitches.From(true, true, true, true, true, true, true, false, true, true, true, true);
            Assert.That((noMovement.MoveStrafe, noMovement.MoveStickLine, noMovement.MoveSitAndWalls), Is.EqualTo((false, false, false)));
            Assert.That(BotPeopleSwitches.AllOn.Describe(), Does.Not.Contain("off"));
            Assert.That(BotPeopleSwitches.AllOff.Describe(), Does.Not.Contain("=on"));
        }

        [Test]
        public void The_switches_start_on_before_the_database_is_read()
        {
            Assert.That(BotPeopleSwitches.Current, Is.EqualTo(BotPeopleSwitches.AllOn), "every field starts at its default (on)");
        }

        // ---- read from gamebots ----------------------------------------------------------------------------------------

        [Test]
        public void A_free_gamebot_is_in_and_the_check_counts_it()
        {
            Bot free = Gamebot();
            Assert.That(BotPersonKey.Of(free), Is.EqualTo(BotPersonKey.Character(free.DatabaseID)));
            foreach (Rule rule in Enum.GetValues<Rule>())
                Assert.That(BotPeopleScope.Refusal(free, rule), Is.EqualTo(Refusal.None), rule.ToString());
            Assert.That(BotPeopleScope.Check(free, Rule.Movement, true, out BotPeopleFacts facts), Is.EqualTo(Refusal.None));
            Assert.That(facts.Rvr.AutonomousGamebot, Is.True);
            Assert.That(BotPeopleScope.Check(free, Rule.Movement, false, out _), Is.EqualTo(Refusal.SwitchedOff));
            long[,] counts = BotPeopleScope.TakeCounts();
            Assert.That((counts[(int)Rule.Movement, (int)Refusal.None], counts[(int)Rule.Movement, (int)Refusal.SwitchedOff]), Is.EqualTo((1L, 1L)));
            Assert.That(BotPeopleScope.TakeCounts()[(int)Rule.Movement, (int)Refusal.None], Is.Zero, "taking resets");
        }

        [Test]
        public void Companions_special_forces_and_buffbots_have_no_person()
        {
            Assert.That(BotPersonKey.Of(Companion()).IsNone, Is.True);
            Assert.That(BotPeopleScope.Refusal(Companion(), Rule.Record), Is.EqualTo(Refusal.NotAGamebot));
            Assert.That(BotPeopleScope.Refusal((GameBot)null, Rule.Record), Is.EqualTo(Refusal.NotAGamebot));
            Bot force = SpecialForce();
            Assert.That(BotPersonKey.Of(force).IsNone, Is.True);
            Assert.That(BotPeopleScope.Refusal(force, Rule.Record), Is.EqualTo(Refusal.InMemoryOnly));
            Bot buffBot = Gamebot();
            typeof(GameBot).GetProperty(nameof(GameBot.BuffBotMainId))!.SetValue(buffBot, 77L);
            Assert.That(BotPeopleScope.Refusal(buffBot, Rule.Movement), Is.EqualTo(Refusal.InMemoryOnly));
            Assert.That(BotPeopleScope.Check(buffBot, Rule.Record, true, out _), Is.EqualTo(Refusal.InMemoryOnly));
        }

        [Test]
        public void A_bot_in_the_players_group_is_spared_not_moved()
        {
            // A player in a bot-led group (the group's own member list, as RvrTacticsScope reads it) ...
            Bot leader = Gamebot(), member = Gamebot();
            GroupOf(leader, member, RealPlayer());
            // ... and a bot whose lease says the player leads (/invite).
            Bot invited = Gamebot();
            BotDutyLeases.Claim(invited, Kind.PlayerGroup, "player", DutyLeaseReason.PlayerGroup);
            foreach (Bot bot in new[] { member, invited })
            {
                Assert.That(BotPeopleScope.FactsOf(bot).InPlayerGroup, Is.True);
                Assert.That(BotPeopleScope.Refusal(bot, Rule.Mistakes), Is.EqualTo(Refusal.None), "spared, not refused");
                Assert.That(BotPeopleScope.Refusal(bot, Rule.Movement), Is.EqualTo(Refusal.PlayerGroup));
                Assert.That(BotPeopleScope.Refusal(bot, Rule.WayFinding), Is.EqualTo(Refusal.PlayerGroup));
                Assert.That(BotPeopleScope.Refusal(bot, Rule.Record), Is.EqualTo(Refusal.None));
            }
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

        [Test]
        public void A_raider_a_stable_rider_and_a_bot_in_a_raid_dungeon_keep_todays_rules()
        {
            Bot raider = Gamebot();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            Assert.That(BotPeopleScope.Refusal(raider, Rule.Mistakes), Is.EqualTo(Refusal.RealmRaid));
            Assert.That(BotPeopleScope.Refusal(raider, Rule.Record), Is.EqualTo(Refusal.None));

            Bot rider = Gamebot();
            typeof(GameBot).GetProperty(nameof(GameBot.IsOnStableMasterRoute))!.SetValue(rider, true);
            Assert.That(BotPeopleScope.Refusal(rider, Rule.Movement), Is.EqualTo(Refusal.StableRide));
            Assert.That(BotPeopleScope.Refusal(rider, Rule.WayFinding), Is.EqualTo(Refusal.StableRide));

            Bot inDungeon = Gamebot();
            inDungeon.Region = AutonomousRealmRaid.RaidDungeons[0].Region;
            Assert.That(BotPeopleScope.FactsOf(inDungeon).InRaidDungeon, Is.True);
            Assert.That(BotPeopleScope.Refusal(inDungeon, Rule.Movement), Is.EqualTo(Refusal.DungeonRaid));

            Bot defender = Gamebot(objective: "RvR");
            defender.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.DefendEvent);
            Assert.That(BotPeopleScope.Refusal(defender, Rule.Movement), Is.EqualTo(Refusal.AtObjective));
        }
    }
}
