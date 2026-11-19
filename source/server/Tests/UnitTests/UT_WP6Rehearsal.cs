using System;
using System.Linq;
using DOL.GS.Tests.Rehearsal;
using DOL.GS.Tests.Rehearsal.Adapters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Crafting 2.0 WP6 in the economy rehearsal (WP10): the set score as the upgrade rule, the gear check and the gap
    /// purchases, with every gate still passing and no set score falling. Rehearsal results are tests, not game results.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_WP6Rehearsal
    {
        [Test]
        public void TheModuleRunsOnlyWithBotCrafters()
        {
            Assert.That(RehearsalModules.For(RehearsalScenario.Today()).Select(m => m.Id), Does.Not.Contain(WP6Demand.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.FreshStart()).Select(m => m.Id), Does.Contain(WP6Demand.ModuleId));
        }

        [Test]
        public void ADayWithWP6_KeepsEveryGateAndNoSetScoreFalls()
        {
            RehearsalReport report = EconomyRehearsal.Run(
                RehearsalScenario.FreshStart(realms: 1, bots: 200, minutes: RehearsalScenario.Day), build: "wp6");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine,
                report.Metrics.Where(m => m.Key.StartsWith("wp6.")).Select(m => $"{m.Key}={m.Value}")));
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Gates.Select(g => $"{g.Name}: {g.Status} {g.Detail}")));
            Assert.That(report.Modules.Select(m => m.Id), Does.Contain(WP6Demand.ModuleId));
            Assert.That(report.GatesPass, string.Join("; ", report.Gates.Select(g => $"{g.Name}: {g.Status} {g.Detail}")));
            Assert.That(report.Metrics["wp6.setScoreDrops"], Is.EqualTo("0"));
            Assert.That(long.Parse(report.Metrics["wp6.checks"]), Is.GreaterThan(0));
            Assert.That(long.Parse(report.Metrics["wp6.withGaps"]), Is.GreaterThan(0), "fresh bots start without armour");
        }

        private static (RehearsalWorld World, RehearsalAgent Agent) LevelFifty(bool wp6)
        {
            var world = new RehearsalWorld(RehearsalScenario.AgedLegacy(realms: 1, bots: 1, minutes: 60));
            if (wp6)
                new WP6Demand().Attach(world);
            var agent = new RehearsalAgent { Id = 1, Name = "Alb0001", Realm = eRealm.Albion, ArmorObjectType = 34, WeaponObjectType = 3, Level = 50 };
            for (int slot = 0; slot < RehearsalAgent.Slots.Length; slot++)
                agent.Worn[slot] = Piece(slot, quality: 94, utility: 20);
            world.Agents.Add(agent);
            return (world, agent);
        }

        private static DOL.Database.DbInventoryItem Piece(int slot, int quality, int utility) =>
            RehearsalItems.Make("test", slot, 50, quality, utility, 100, 34, 3, rog: true);

        /// <summary>
        /// The week's 41 "undress events" (aged-legacy-12pct, 3 x 1,000, seed 1) were all of this kind: at 50, an armour
        /// piece of higher quality whose EquipmentValue is a little lower. The server's rule with bot_crafters on (the set
        /// score, 6 a quality point above 94) takes it and its guard allows it, so the gate now checks the server's guard;
        /// the trade is still counted. A move that lowers the set score or empties a slot is still an undress.
        /// </summary>
        [Test]
        public void TheUndressGateChecksTheServersGuard_AndCountsSlotValueTrades()
        {
            int torso = Array.IndexOf(RehearsalAgent.Slots, eInventorySlot.TorsoArmor);
            var better = Piece(torso, quality: 100, utility: 17); // EquipmentValue 851 against 854; set score +33
            Assert.That(DOL.GS.AutonomousBotEconomy.EquipmentValue(better), Is.LessThan(DOL.GS.AutonomousBotEconomy.EquipmentValue(Piece(torso, 94, 20))));

            (RehearsalWorld world, RehearsalAgent agent) = LevelFifty(wp6: true);
            Assert.That(world.IsUpgrade(agent, agent.Worn[torso], better), "the server's set-score rule takes it");
            world.Equip(agent, torso, better, "loot");
            Assert.That(world.UndressEvents, Is.Zero);
            Assert.That(world.SlotValueTradeoffs, Is.EqualTo(1));
            Assert.That(world.Lines.Single(), Does.Contain("event=\"tradeoff\"").And.Contain("rule=\"set score\"").And.Contain("source=\"loot\""));

            world.Equip(agent, torso, Piece(torso, quality: 94, utility: 10), "loot");
            Assert.That(world.UndressEvents, Is.EqualTo(1), "a lower set score is an undress");
            world.Equip(agent, torso, null, "test");
            Assert.That(world.UndressEvents, Is.EqualTo(2), "an emptied slot is an undress");

            // With bot_crafters off the server's rule is the slot's EquipmentValue, and the same piece is an undress.
            (RehearsalWorld today, RehearsalAgent bot) = LevelFifty(wp6: false);
            Assert.That(today.IsUpgrade(bot, bot.Worn[torso], better), Is.False);
            today.Equip(bot, torso, better, "test");
            Assert.That(today.UndressEvents, Is.EqualTo(1));
            Assert.That(today.SlotValueTradeoffs, Is.Zero);
        }
    }
}
