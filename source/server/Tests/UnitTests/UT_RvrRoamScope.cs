using System.IO;
using System.Linq;
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
    /// Open-field RvR, work package 6: the scope guard. Siege parties still form before the split; a leader out of scope
    /// (a player's group, a raid) or the split switched off leaves today's warband roll and pick untouched; a bot in a
    /// player's group never gets a check spot; an in-scope leader gets the kind the split wants. Gamebots without a world.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrRoamScope : DutyLeaseFixture
    {
        private bool _tactics, _parties, _checks, _openers;
        private string _split;
        private int _petCap;

        [SetUp]
        public void SwitchesOn()
        {
            (_tactics, _parties, _checks, _openers, _split, _petCap) = (Properties.RVR_BOT_FIELD_TACTICS, Properties.RVR_ROAMING_PARTIES,
                Properties.RVR_ROAM_CHECKS, Properties.RVR_BOT_STEALTH_OPENERS, Properties.RVR_ROAM_POPULATION_SPLIT, Properties.RVR_GROUP_PET_CAP);
            Properties.RVR_BOT_FIELD_TACTICS = Properties.RVR_ROAMING_PARTIES = Properties.RVR_ROAM_CHECKS = Properties.RVR_BOT_STEALTH_OPENERS = true;
            Properties.RVR_ROAM_POPULATION_SPLIT = "55/30/15";
            Properties.RVR_GROUP_PET_CAP = 2;
            BotDutyLeases.Clear();
        }

        [TearDown]
        public void Restore()
        {
            (Properties.RVR_BOT_FIELD_TACTICS, Properties.RVR_ROAMING_PARTIES, Properties.RVR_ROAM_CHECKS, Properties.RVR_BOT_STEALTH_OPENERS,
                Properties.RVR_ROAM_POPULATION_SPLIT, Properties.RVR_GROUP_PET_CAP) = (_tactics, _parties, _checks, _openers, _split, _petCap);
            BotDutyLeases.Clear();
        }

        private static Bot[] FreeRvrBots(int count) => Enumerable.Range(0, count).Select(_ => Gamebot(objective: "RvR")).ToArray();

        [Test]
        public void Siege_parties_form_before_the_split_and_committed_recruits_are_not_drawn()
        {
            DirectoryInfo dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "source", "server", "GameServer")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            string source = File.ReadAllText(Path.Combine(dir!.FullName, "source", "server", "GameServer", "bots", "autonomous",
                "AutonomousBotGroupCoordinator.cs"));
            int siege = source.IndexOf("FormSiegeParties();", System.StringComparison.Ordinal);
            int groups = source.IndexOf("TryFormGroups();", System.StringComparison.Ordinal);
            Assert.That(siege, Is.GreaterThan(0));
            Assert.That(siege, Is.LessThan(groups), "the serial pulse forms siege parties first");
            Assert.That(source, Does.Contain("AutonomousRvrEventLayer.IsForceCommitted($\"rvr-{bot.DatabaseID}\", GameLoop.GameLoopTime)"),
                "a siege recruit is not in the pool the split draws from");
            Assert.That(source.IndexOf("RvrTacticsPartySize(", System.StringComparison.Ordinal), Is.GreaterThan(groups));
        }

        [Test]
        public void Out_of_scope_or_switched_off_the_warband_roll_and_pick_stand()
        {
            Bot[] roster = FreeRvrBots(40);
            Player player = RealPlayer();
            Bot invited = Gamebot(objective: "RvR");
            JoinPlayersGroup(invited, player);
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(invited, 3, 8, roster), Is.EqualTo(3), "a player's group");
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsRoster(invited, roster, roster.Take(2).ToArray(), 3), Is.Null);

            Bot raider = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(raider, 1, 8, roster), Is.EqualTo(1), "a raider");

            Properties.RVR_ROAMING_PARTIES = false;
            Bot free = roster[0];
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(free, 1, 8, roster), Is.EqualTo(1), "the split off: alone stays alone");
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(free, 5, 8, roster), Is.EqualTo(5));
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsRoster(free, roster, roster.Take(4).ToArray(), 5), Is.Null);
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsSoloReserveShare(), Is.EqualTo(0.35), "the old reserve");
            Properties.RVR_ROAMING_PARTIES = true;
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsSoloReserveShare(), Is.EqualTo(0.15).Within(1e-9));
        }

        [Test]
        public void In_scope_the_split_picks_the_kind_and_an_eight_is_built_from_the_pool()
        {
            Bot[] roster = FreeRvrBots(40);
            Bot leader = roster[0];
            // Everyone free: warbands are furthest below their share. Today's roll is kept.
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(leader, 3, 8, roster), Is.EqualTo(3));
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsRoster(leader, roster.Skip(1).ToArray(), roster.Skip(1).Take(2).ToArray(), 3),
                Is.Null, "a warband keeps today's pick");

            Properties.RVR_ROAM_POPULATION_SPLIT = "0/100/0";
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(leader, 3, 8, roster), Is.EqualTo(8), "an eight takes all it may");
            GameBot[] pool = roster.Skip(1).ToArray();
            GameBot[] eight = AutonomousBotGroupCoordinator.RvrTacticsRoster(leader, pool, pool.Take(7).ToArray(), 8);
            Assert.That(eight, Has.Length.EqualTo(7));
            Assert.That(eight.Distinct().Count(), Is.EqualTo(7));
            Assert.That(eight.All(pool.Contains), Is.True, "from the same pool");

            Properties.RVR_ROAM_POPULATION_SPLIT = "0/0/100";
            int size = AutonomousBotGroupCoordinator.RvrTacticsPartySize(leader, 6, 8, roster);
            Assert.That(size, Is.EqualTo(1).Or.EqualTo(2), "alone or a duo");
        }

        [Test]
        public void A_bot_in_a_players_group_never_gets_a_check_spot_and_is_never_a_roamer()
        {
            Player player = RealPlayer();
            Bot invited = Gamebot(objective: "RvR");
            JoinPlayersGroup(invited, player);
            Assert.That(AutonomousBotGroupCoordinator.RvrPartyKindOf(invited), Is.EqualTo(RvrPartyKind.Warband), "not small, not an eight");
            MethodInfo roam = typeof(AutonomousWorldBotController).GetMethod("RvrTacticsRoamDestination", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(roam.Invoke(new AutonomousWorldBotController(), [invited, null]), Is.Null);
            Assert.That(BotBrain.RvrTacticsApplyTo(invited), Is.False);
            Assert.That(new BotBrain { Body = invited }.TryRvrTacticsMove(Gamebot(realm: eRealm.Midgard, objective: "RvR")), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.RvrPartyKindOf(Gamebot(objective: "RvR")), Is.EqualTo(RvrPartyKind.Small), "a bot alone");
        }
    }
}
