using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Grand Summoner Govannon's demon (owner 2026-11-07, "teach positioning"): his Shroud of Agony hits everyone within 300
    /// of his target; in trio1 it took 45 raiders in 90 s, nearly all melee, who stood in it. His target keeps 150 from him
    /// (he closes to 131 himself) and the other melee fight him from 185 behind him on the line from his target.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_GovannonMeleeBehind
    {
        [SetUp]
        public void Reset() => EncounterMeleeStations.ResetForTests();

        [TearDown]
        public void Clean() => EncounterMeleeStations.ResetForTests();

        [Test]
        public void TheSpotBehindHimIsInReachOfHimAndClearOfHisTarget()
        {
            Vector3 boss = new(32124, 40754, 15466), tank = new(32124 + 131, 40754, 15466);
            float behind = EpicEncounterTactics.BehindFor(131, 300);
            Assert.That(behind, Is.EqualTo(189).Within(0.5), "300 + 10 + 10 - 131");
            Vector3 spot = EncounterMeleeStations.BehindSpot(boss, tank, behind, 15466);
            Assert.That(Vector2.Distance(new(spot.X, spot.Y), new(boss.X, boss.Y)), Is.EqualTo(189).Within(0.5));
            Assert.That(Vector2.Distance(new(spot.X, spot.Y), new(tank.X, tank.Y)), Is.GreaterThan(300 + 10),
                "his own closing distance (131) plus 185 clears the 300 Shroud");
            Assert.That(EpicEncounterTactics.BehindDistance, Is.LessThan(200), "inside bot melee reach (200)");
            Assert.That(EncounterMeleeStations.InReach(spot, tank, 300, EpicEncounterTactics.BehindMargin), Is.False);
            Assert.That(EncounterMeleeStations.InReach(new(tank.X - 40, tank.Y, tank.Z), tank, 300, 20), Is.True);
            Assert.That(EpicEncounterTactics.BehindFor(60, 300), Is.EqualTo(EpicEncounterTactics.BehindReach), "a target close in: as far back as melee reach allows");
            Assert.That(EpicEncounterTactics.BehindFor(200, 300), Is.EqualTo(EpicEncounterTactics.BehindDistance));
        }

        [Test]
        public void ATargetSwapTurnsTheSpotAround()
        {
            Vector3 boss = new(0, 0, 0);
            Vector3 east = EncounterMeleeStations.BehindSpot(boss, new(150, 0, 0), 185, 0);
            Vector3 west = EncounterMeleeStations.BehindSpot(boss, new(-150, 0, 0), 185, 0);
            Assert.That(east.X, Is.EqualTo(-185).Within(0.5));
            Assert.That(west.X, Is.EqualTo(185).Within(0.5));
        }

        [Test]
        public void AnOrderSetsTheFollowDistanceForItsTargetOnlyAndRunsOut()
        {
            var bot = (GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            var gov = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            var other = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            Assert.That(EncounterMeleeStations.MinimumFollowFor(bot, gov, 0), Is.Null, "no order: the bot's own 75");
            EncounterMeleeStations.Hold(bot, gov, 185, 1_000);
            Assert.That(EncounterMeleeStations.MinimumFollowFor(bot, gov, 2_000), Is.EqualTo(185));
            Assert.That(EncounterMeleeStations.MinimumFollowFor(bot, other, 2_000), Is.Null, "another target: its own");
            Assert.That(EncounterMeleeStations.MinimumFollowFor(bot, gov, 1_000 + EncounterMeleeStations.OrderMilliseconds + 1),
                Is.Null, "not renewed: it ends with the phase");
        }

        [Test]
        public void OnlyGovannonHasTheOrder()
        {
            Assert.That(EpicEncounterTactics.Catalog, Has.Exactly(1).Matches<EpicEncounterTactics.Tactic>(t => t.MeleeBehind));
            // The healer hold was dropped after duo7 (15 healers died within 200 of him, kept there): no boss has it.
            Assert.That(EpicEncounterTactics.Catalog, Has.None.Matches<EpicEncounterTactics.Tactic>(t => t.HealersHoldInSpread));
            EpicEncounterTactics.Tactic gov = null;
            foreach (var t in EpicEncounterTactics.Catalog) if (t.Boss == "GrandSummonerGovannon") gov = t;
            Assert.That(gov.MeleeBehind, Is.True);
        }
    }
}
