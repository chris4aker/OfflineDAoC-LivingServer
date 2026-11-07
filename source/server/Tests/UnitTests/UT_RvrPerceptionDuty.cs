using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// RvR perception and the duty lease (audit 2026-11-03, challenge 2, step 3): a gamebot alerted by a spotter's call
    /// leaves what it is doing only when its lease allows it; the player's own companions and group bots are never told
    /// and count as the player's for the perception rule. Objects without a world (DutyLeaseTestKit).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrPerceptionDuty : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        [Test]
        public void A_free_bot_an_rvr_marcher_and_a_bot_group_member_answer_a_call()
        {
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(Gamebot(objective: "RvR")), Is.True, "free");
            Bot marcher = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "force-1", DutyLeaseReason.RvrForce);
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(marcher), Is.True);
            Bot grouped = Gamebot(objective: "RvR");
            Group party = Token();
            grouped.Group = party;
            BotDutyLeases.Claim(grouped, Kind.BotGroupSession, "session-1", DutyLeaseReason.GroupSession, group: party);
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(grouped), Is.True);
        }

        [Test]
        public void A_raider_and_a_battleground_stormer_keep_their_duty()
        {
            Bot raider = Gamebot(objective: "RvR");
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(raider), Is.False);
            Bot stormer = Gamebot(level: 22, objective: "RvR");
            BotDutyLeases.Claim(stormer, Kind.BattlegroundWave, "bg-rally", DutyLeaseReason.BattlegroundRally);
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(stormer), Is.False);
        }

        [Test]
        public void The_players_own_bots_are_never_told_and_count_as_the_players()
        {
            Player player = RealPlayer();
            Bot inGroup = Gamebot(objective: "RvR");
            JoinPlayersGroup(inGroup, player);
            Bot companion = Companion();
            Bot spotter = Gamebot(objective: "RvR");
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(inGroup), Is.False);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(inGroup, spotter), Is.False);
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(companion), Is.False);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(companion, spotter), Is.False);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(Gamebot(objective: "RvR"), spotter), Is.True);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(Gamebot(realm: eRealm.Midgard, objective: "RvR"), spotter), Is.False);
            // As targets, the player's bots are perceived like the player (noticed by chance), not acquired at once.
            Assert.That(AutonomousWorldBotController.PerceivedKind(inGroup), Is.EqualTo(RvrPerceivedKind.PlayerCompanion));
            Assert.That(AutonomousWorldBotController.PerceivedKind(companion), Is.EqualTo(RvrPerceivedKind.PlayerCompanion));
            Assert.That(AutonomousWorldBotController.PerceivedKind(player), Is.EqualTo(RvrPerceivedKind.Player));
            Assert.That(AutonomousWorldBotController.PerceivedKind(spotter), Is.EqualTo(RvrPerceivedKind.Other), "an enemy gamebot: as before");
        }
    }
}
