using System.Reflection;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// Hired companions (HiredCompanions165) are autonomous bot rows that a player owns, so every rule that asks only
    /// "an autonomous gamebot, not a /spawn helper" would take them in. They stay outside the open-field RvR tactics
    /// (RvrTacticsScope, the calls and the perception kind: a player's bot, as a player-led group is) and outside the
    /// economy's bot-side trade and craft-order rules, whether they are in their player's group or waiting alone.
    /// The same world-less gamebots as the scope tests (DutyLeaseTestKit). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_HiredCompanionsKeptApart : DutyLeaseFixture
    {
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [SetUp]
        public void Fresh()
        {
            BotDutyLeases.Clear();
            Forget();
        }

        [TearDown]
        public void Clean()
        {
            BotDutyLeases.Clear();
            Forget();
        }

        private static void Forget() =>
            typeof(HiredCompanions165).GetMethod("ClearForTests", HiddenStatic)!.Invoke(null, null);

        private static Bot Hired(Bot bot)
        {
            typeof(HiredCompanions165).GetMethod("RegisterForTests", HiddenStatic)!.Invoke(null,
                [new OfflineHiredCompanionRecord { BotId = bot.DatabaseID, OwnerCharacterID = "owner-1", OwnerName = "Owner" }]);
            return bot;
        }

        [Test]
        public void A_hired_companion_waiting_alone_is_outside_the_rvr_tactics()
        {
            Bot gamebot = Gamebot(objective: "RvR");
            Bot companion = Hired(Gamebot(objective: "RvR"));
            Bot spotter = Gamebot(objective: "RvR");
            Assert.Multiple(() =>
            {
                Assert.That(gamebot.IsHiredCompanion, Is.False);
                Assert.That(companion.IsHiredCompanion, Is.True);
                Assert.That(RvrTacticsScope.Refusal(gamebot), Is.EqualTo(RvrScopeRefusal.None), "the same bot unhired is in");
                Assert.That(RvrTacticsScope.Refusal(companion), Is.EqualTo(RvrScopeRefusal.NotAGamebot));
                Assert.That(RvrTacticsScope.FactsOf(companion).Companion, Is.True);
                Assert.That(BotBrain.RvrTacticsApplyTo(companion), Is.False);
                Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(gamebot), Is.True);
                Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(companion), Is.False, "never leaves its player for a call");
                Assert.That(AutonomousWorldBotController.CanHearRvrCall(companion, spotter), Is.False);
                Assert.That(AutonomousWorldBotController.PerceivedKind(companion), Is.EqualTo(RvrPerceivedKind.PlayerCompanion),
                    "enemy bots notice it by the player rule, as a /spawn companion");
                Assert.That(AutonomousWorldBotController.PerceivedKind(gamebot), Is.EqualTo(RvrPerceivedKind.EnemyBot));
            });
        }

        [Test]
        public void A_hired_companion_is_no_trader_crafter_or_craft_order_buyer()
        {
            Bot gamebot = Gamebot();
            Bot companion = Hired(Gamebot());
            Player player = RealPlayer();
            Assert.Multiple(() =>
            {
                Assert.That(CrafterCommerce.IsCommerceBot(gamebot, player), Is.True);
                Assert.That(CrafterCommerce.IsCommerceBot(companion, player), Is.False, "no bot trade window, tells or buy list");
                Assert.That(AutonomousBotCraftOrders.IsAbleCrafter(gamebot), Is.True);
                Assert.That(AutonomousBotCraftOrders.IsAbleCrafter(companion), Is.False, "fills no craft orders");
                Assert.That(AutonomousBotCraftOrders.TryCommission(companion), Is.Null, "orders nothing for itself");
                Assert.That(BotGuildCommerce.FundMaterials(companion, 500), Is.Zero);
            });
        }
    }
}
