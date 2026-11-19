using System;
using System.Collections.Generic;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md, principle 6): the one scope test. The tactics apply
    /// to an autonomous gamebot, solo or in a bot-only group, whose lease is none, a bot group session, or an RvR event
    /// while it is not at an objective, and to nobody else. The pure rule from plain values, then the same rule read
    /// from gamebots without a world (DutyLeaseTestKit).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrTacticsScope : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        // ---- the pure rule ---------------------------------------------------------------------------------------------

        [TestCase(Kind.None, true)]
        [TestCase(Kind.BotGroupSession, true)]
        [TestCase(Kind.RvrEvent, true)]
        [TestCase(Kind.RealmRaid, false)]
        [TestCase(Kind.BattlegroundWave, false)]
        [TestCase(Kind.BotLedGroupWithPlayer, false)]
        [TestCase(Kind.PlayerGroup, false)]
        public void Every_lease_kind_is_in_or_out(Kind duty, bool allowed)
        {
            Assert.That(RvrTacticsScope.Allows(duty), Is.EqualTo(allowed));
            Assert.That(RvrTacticsScope.Allows(RvrScopeFacts.FreeGamebot with { Duty = duty }), Is.EqualTo(allowed));
        }

        [Test]
        public void Every_lease_kind_there_is_has_a_case_above()
        {
            var covered = new HashSet<Kind>
            {
                Kind.None, Kind.BotGroupSession, Kind.RvrEvent, Kind.RealmRaid, Kind.BattlegroundWave,
                Kind.BotLedGroupWithPlayer, Kind.PlayerGroup,
            };
            Assert.That(Enum.GetValues<Kind>(), Is.SubsetOf(covered), "a new lease kind needs a decision in RvrTacticsScope");
        }

        [Test]
        public void The_lease_part_mirrors_who_may_answer_a_spotters_call()
        {
            foreach (Kind duty in Enum.GetValues<Kind>())
                Assert.That(RvrTacticsScope.Allows(duty), Is.EqualTo(AutonomousRvrPerception.MayAnswerCall(duty)), duty.ToString());
        }

        [Test]
        public void An_rvr_marcher_is_in_until_it_is_at_its_objective()
        {
            Assert.That(RvrTacticsScope.Allows(Kind.RvrEvent), Is.True, "marching");
            Assert.That(RvrTacticsScope.Allows(Kind.RvrEvent, atObjective: true), Is.False, "a committed defender or siege fighter");
            Assert.That(RvrTacticsScope.Refusal(RvrScopeFacts.FreeGamebot with { Duty = Kind.RvrEvent, AtObjective = true }),
                Is.EqualTo(RvrScopeRefusal.AtObjective));
            Assert.That(RvrTacticsScope.Allows(Kind.None, atObjective: true), Is.False, "at an objective without a lease too");
        }

        [Test]
        public void Each_fenced_off_duty_is_refused_with_its_reason() => Assert.Multiple(() =>
        {
            RvrScopeFacts free = RvrScopeFacts.FreeGamebot;
            Assert.That(RvrTacticsScope.Refusal(free), Is.EqualTo(RvrScopeRefusal.None));
            Assert.That(RvrTacticsScope.Refusal(default), Is.EqualTo(RvrScopeRefusal.NotAGamebot), "nothing known about it");
            Assert.That(RvrTacticsScope.Refusal(free with { AutonomousGamebot = false }), Is.EqualTo(RvrScopeRefusal.NotAGamebot));
            Assert.That(RvrTacticsScope.Refusal(free with { Companion = true }), Is.EqualTo(RvrScopeRefusal.NotAGamebot));
            Assert.That(RvrTacticsScope.Refusal(free with { PlayerLedGroup = true }), Is.EqualTo(RvrScopeRefusal.PlayerGroup));
            Assert.That(RvrTacticsScope.Refusal(free with { PlayerInGroup = true }), Is.EqualTo(RvrScopeRefusal.PlayerGroup));
            Assert.That(RvrTacticsScope.Refusal(free with { Duty = Kind.PlayerGroup }), Is.EqualTo(RvrScopeRefusal.PlayerGroup));
            Assert.That(RvrTacticsScope.Refusal(free with { Duty = Kind.BotLedGroupWithPlayer }), Is.EqualTo(RvrScopeRefusal.PlayerGroup));
            Assert.That(RvrTacticsScope.Refusal(free with { Duty = Kind.RealmRaid }), Is.EqualTo(RvrScopeRefusal.RealmRaid));
            Assert.That(RvrTacticsScope.Refusal(free with { OnRealmRaid = true }), Is.EqualTo(RvrScopeRefusal.RealmRaid),
                "the group is a raid party, whatever the lease says");
            Assert.That(RvrTacticsScope.Refusal(free with { Duty = Kind.BotGroupSession, OnRealmRaid = true }),
                Is.EqualTo(RvrScopeRefusal.RealmRaid));
            Assert.That(RvrTacticsScope.Refusal(free with { Duty = Kind.BattlegroundWave }), Is.EqualTo(RvrScopeRefusal.BattlegroundAssault));
            Assert.That(RvrTacticsScope.Refusal(free with { BattlegroundAssault = true }), Is.EqualTo(RvrScopeRefusal.BattlegroundAssault),
                "gathering for the wave, before any lease");
            Assert.That(RvrTacticsScope.Refusal(free with { AtObjective = true }), Is.EqualTo(RvrScopeRefusal.AtObjective));
            Assert.That(RvrTacticsScope.Refusal(free with { SiegeOperator = true }), Is.EqualTo(RvrScopeRefusal.SiegeOperator));
            Assert.That(RvrTacticsScope.Refusal(free with { RelicCarrier = true }), Is.EqualTo(RvrScopeRefusal.RelicCarrier));
            Assert.That(RvrTacticsScope.Refusal(free with { Duty = (Kind)200 }), Is.EqualTo(RvrScopeRefusal.OtherDuty),
                "a lease kind the rule does not know is out");
        });

        [Test]
        public void The_plain_overload_takes_every_fact() => Assert.Multiple(() =>
        {
            Assert.That(RvrTacticsScope.Allows(Kind.None), Is.True);
            Assert.That(RvrTacticsScope.Allows(Kind.None, autonomousGamebot: false), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.None, companion: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.None, playerLedGroup: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.BotGroupSession, playerInGroup: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.BotGroupSession, onRealmRaid: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.None, battlegroundAssault: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.RvrEvent, atObjective: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.RvrEvent, siegeOperator: true), Is.False);
            Assert.That(RvrTacticsScope.Allows(Kind.RvrEvent, relicCarrier: true), Is.False);
        });

        // ---- read from gamebots ----------------------------------------------------------------------------------------

        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden)!.GetValue(group)!;
            foreach (GameLiving member in members)
            {
                list.Add(member);
                member.Group = group;
            }
            return group;
        }

        [Test]
        public void A_free_gamebot_an_rvr_marcher_and_a_bot_group_member_are_in()
        {
            Bot free = Gamebot(objective: "RvR");
            Assert.That(RvrTacticsScope.Allows(free), Is.True, "solo, no lease");
            Assert.That(RvrTacticsScope.FactsOf(free), Is.EqualTo(RvrScopeFacts.FreeGamebot));

            Bot marcher = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "force-1", DutyLeaseReason.RvrForce);
            Assert.That(RvrTacticsScope.Allows(marcher), Is.True, "marching with an RvR force");

            Bot leader = Gamebot(objective: "RvR"), mate = Gamebot(objective: "RvR");
            Group party = GroupOf(leader, mate);
            BotDutyLeases.Claim(mate, Kind.BotGroupSession, "session-1", DutyLeaseReason.GroupSession, group: party);
            Assert.That(RvrTacticsScope.Allows(mate), Is.True, "a bot-only group session");
            Assert.That(RvrTacticsScope.FactsOf(mate).PlayerInGroup, Is.False);
        }

        [Test]
        public void A_raider_and_a_battleground_stormer_are_out()
        {
            Bot raider = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            Assert.That(RvrTacticsScope.Refusal(raider), Is.EqualTo(RvrScopeRefusal.RealmRaid));

            Bot stormer = Gamebot(level: 22, objective: "Battleground");
            stormer.Region = 252;
            BotDutyLeases.Claim(stormer, Kind.BattlegroundWave, "bg-rally", DutyLeaseReason.BattlegroundRally);
            Assert.That(RvrTacticsScope.Refusal(stormer), Is.EqualTo(RvrScopeRefusal.BattlegroundAssault));

            Bot roamer = Gamebot(level: 22, objective: "Battleground");
            roamer.Region = 252;
            Assert.That(RvrTacticsScope.Allows(roamer), Is.True, "a battleground bot with no wave, rally or storm is a field fighter");
        }

        [Test]
        public void A_companion_and_the_players_group_are_out()
        {
            Assert.That(RvrTacticsScope.Refusal(Companion()), Is.EqualTo(RvrScopeRefusal.NotAGamebot));
            Assert.That(RvrTacticsScope.Refusal((GameBot)null), Is.EqualTo(RvrScopeRefusal.NotAGamebot));

            Player player = RealPlayer();
            Bot invited = Gamebot(objective: "RvR");
            JoinPlayersGroup(invited, player);
            Assert.That(RvrTacticsScope.Refusal(invited), Is.EqualTo(RvrScopeRefusal.PlayerGroup), "the player leads");

            // a bot-led group the player joined: the lease says so ...
            Bot host = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(host, Kind.BotLedGroupWithPlayer, "host", DutyLeaseReason.HostingPlayer);
            Assert.That(RvrTacticsScope.Refusal(host), Is.EqualTo(RvrScopeRefusal.PlayerGroup));

            // ... and a player in the group is enough even when a member's lease is an RvR event's
            Bot marcher = Gamebot(objective: "RvR"), other = Gamebot(objective: "RvR");
            GroupOf(other, marcher, RealPlayer());
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "force-1", DutyLeaseReason.RvrForce);
            Assert.That(RvrTacticsScope.FactsOf(marcher).PlayerInGroup, Is.True);
            Assert.That(RvrTacticsScope.Refusal(marcher), Is.EqualTo(RvrScopeRefusal.PlayerGroup));
        }

        [Test]
        public void A_keep_defender_a_siege_fighter_a_siege_operator_and_a_relic_carrier_are_out()
        {
            Bot defender = Gamebot(objective: "RvR");
            defender.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.DefendEvent);
            Assert.That(RvrTacticsScope.Refusal(defender), Is.EqualTo(RvrScopeRefusal.AtObjective));

            Bot attacker = Gamebot(objective: "RvR");
            attacker.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.AssaultKeep);
            BotDutyLeases.Claim(attacker, Kind.RvrEvent, "force-2", DutyLeaseReason.SiegeParty);
            Assert.That(RvrTacticsScope.Refusal(attacker), Is.EqualTo(RvrScopeRefusal.AtObjective));

            Bot roamer = Gamebot(objective: "RvR");
            roamer.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.Roam);
            Assert.That(RvrTacticsScope.Allows(roamer), Is.True, "roaming and hunting are the open field");
            roamer.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.HuntEnemy);
            Assert.That(RvrTacticsScope.Allows(roamer), Is.True);

            Bot siegeOperator = Gamebot(objective: "RvR");
            siegeOperator.TempProperties.SetProperty("SiegeJobUntil", long.MaxValue / 2);
            Assert.That(RvrTacticsScope.Refusal(siegeOperator), Is.EqualTo(RvrScopeRefusal.SiegeOperator));

            Bot carrier = Gamebot(objective: "RvR");
            carrier.TempProperties.SetProperty(GameRelic.PLAYER_CARRY_RELIC_WEAK,
                (GameRelic)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameRelic)));
            Assert.That(RvrTacticsScope.Refusal(carrier), Is.EqualTo(RvrScopeRefusal.RelicCarrier));
        }
    }
}
