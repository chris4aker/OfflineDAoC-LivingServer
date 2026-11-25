using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>#525 siegecraft stand-ins: the player's ram crew, defending operators, the visible build of synthetic
    /// engines with a per-siege stock, the operator's status text, and the defenders' door repair with wood.</summary>
    [TestFixture, NonParallelizable]
    public class UT_Siegecraft525
    {
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;
        [SetUp] public void Setup()
        { _previous = GameServer.Instance; GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server))); }
        [TearDown] public void Cleanup() => GameServer.LoadTestDouble(_previous);

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool TestAlive = true;
            public override bool IsAlive => TestAlive;
            public override eRealm Realm { get; set; }
        }

        private static Bot MakeBot(eRealm realm = eRealm.Albion, bool autonomous = true, bool alive = true)
        {
            var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            bot.Realm = realm;
            bot.TestAlive = alive;
            typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, autonomous);
            return bot;
        }

        private static GamePlayer MakePlayer() => (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));

        // ---- the player's ram ----

        [Test]
        public void APlayersRamWithTwelveBotsBesideItHitsLikeABotRamWithTwelveCrew()
        {
            var crew = Enumerable.Range(0, 12).Select(_ => (GameBot)MakeBot()).ToArray();
            var botOperator = MakeBot();
            int playerCrew = GameSiegeRam.CountCrew(MakePlayer(), eRealm.Albion, 0, crew, 12);
            int botCrew = GameSiegeRam.CountCrew(botOperator, eRealm.Albion, 0, crew, 12);
            Assert.That(playerCrew, Is.EqualTo(12));
            Assert.That(botCrew, Is.EqualTo(12));
            Assert.That(GameSiegeRam.CrewDamage(200, playerCrew), Is.EqualTo(GameSiegeRam.CrewDamage(200, botCrew)).And.EqualTo(1400));
            Assert.That(GameSiegeRam.CrewReloadMilliseconds(3, playerCrew, 12), Is.EqualTo(GameSiegeRam.CrewReloadMilliseconds(3, botCrew, 12)).And.EqualTo(8000));
        }

        [Test]
        public void OnlyLivingFriendlyBotsOtherThanTheOperatorCrewARam()
        {
            var op = MakeBot();
            var nearby = new GameBot[] { op, MakeBot(eRealm.Midgard), MakeBot(alive: false), MakeBot(autonomous: false), MakeBot() };
            // The operator, the enemy, the dead bot and a companion of someone else's group do not count; one gamebot does.
            Assert.That(GameSiegeRam.CountCrew(op, eRealm.Albion, 0, nearby, 12), Is.EqualTo(1));
            Assert.That(GameSiegeRam.CountCrew(MakePlayer(), eRealm.Albion, 2, nearby, 12), Is.EqualTo(4), "riders plus bots, the player's ram included");
            Assert.That(GameSiegeRam.CountCrew(null, eRealm.Albion, 1, nearby, 12), Is.EqualTo(1), "nobody operating: riders only");
            Assert.That(GameSiegeRam.CountCrew(op, eRealm.Albion, 0, Enumerable.Range(0, 20).Select(_ => (GameBot)MakeBot()), 12), Is.EqualTo(12));
        }

        [Test]
        public void TheRamOutputLineNamesOwnerKindCrewDamageAndTarget()
        {
            Assert.That(GameSiegeRam.OperatorKind(MakePlayer()), Is.EqualTo("player"));
            Assert.That(GameSiegeRam.OperatorKind(MakeBot()), Is.EqualTo("gamebot"));
            Assert.That(GameSiegeRam.OperatorKind(MakeBot(autonomous: false)), Is.EqualTo("companion"));
            string line = GameSiegeRam.OutputLine(null, eRealm.Albion, 7, 12, 5, 4321, 60, null);
            foreach (string field in new[] { "RVR_SIEGE_OUTPUT", "kind=ram", "owner=", "operator=none", "crew=7/12", "hits=5", "damage=4321", "target=", "targetHp=" })
                Assert.That(line, Does.Contain(field));
        }

        [Test]
        public void AnEngineNobodyOperatesStillCountsAgainstTheBotsPlaces()
        {
            Assert.That(AutonomousWorldBotController.CountsAgainstBotEngineCap(new GameSiegeRam()), Is.True);
            Assert.That(GameSiegeRam.SharesRamLimit(new GameSiegeRam(), new GameSiegeRam()), Is.True);
        }

        // ---- defending operators ----

        [TestCase(false, true, false, false, false, ExpectedResult = true, TestName = "NoSiegeJob_TakesAnyEnemy")]
        [TestCase(true, true, false, false, false, ExpectedResult = false, TestName = "DefendingOperator_KeepsEngine_EnemyFarAway")]
        [TestCase(true, true, true, false, false, ExpectedResult = true, TestName = "DefendingOperator_EnemyWithin450")]
        [TestCase(true, true, true, true, false, ExpectedResult = false, TestName = "DefendingOperator_NeverMeleesAnEngine")]
        [TestCase(true, true, false, false, true, ExpectedResult = true, TestName = "DefendingOperator_AnswersAnEnemyOnItsEngine")]
        [TestCase(true, false, false, false, true, ExpectedResult = false, TestName = "AttackingOperator_Unchanged_FarEnemy")]
        [TestCase(true, false, true, false, false, ExpectedResult = true, TestName = "AttackingOperator_Unchanged_Within450")]
        public bool ASiegeOperatorLeavesItsEngineOnlyForACloseEnemy(bool assigned, bool defending, bool within, bool engine, bool onEngine) =>
            AutonomousWorldBotController.SiegeOperatorMayEngage(assigned, defending, within, engine, onEngine);

        [Test]
        public void TheOperatorRadiusIs450() => Assert.That(AutonomousWorldBotController.SiegeOperatorEngageRadius, Is.EqualTo(450));

        // ---- visible build ----

        [Test]
        public void BuildTimeIsHalfThePeriodTimePerEngine()
        {
            Assert.That(AutonomousSiegeBuild.BuildTimeMultiplier, Is.EqualTo(0.5));
            foreach (BotSiegeKind kind in Enum.GetValues<BotSiegeKind>())
                Assert.That(AutonomousSiegeBuild.BuildMilliseconds(kind), Is.EqualTo(AutonomousSiegeBuild.PeriodBuildSeconds(kind) * 500));
            Assert.That(AutonomousSiegeBuild.BuildMilliseconds(BotSiegeKind.Ram), Is.EqualTo(30_000));
            Assert.That(AutonomousSiegeBuild.BuildMilliseconds(BotSiegeKind.Ballista), Is.EqualTo(20_000));
            Assert.That(AutonomousSiegeBuild.BuildMilliseconds(BotSiegeKind.Catapult), Is.EqualTo(45_000));
            Assert.That(AutonomousSiegeBuild.BuildMilliseconds(BotSiegeKind.Trebuchet), Is.EqualTo(60_000));
        }

        [Test]
        public void ABuildCompletesOnlyAfterItsTime()
        {
            Assert.That(AutonomousSiegeBuild.Complete(BotSiegeKind.Ram, 0, 99_999), Is.False, "never started");
            Assert.That(AutonomousSiegeBuild.Complete(BotSiegeKind.Ram, 1_000, 30_999), Is.False);
            Assert.That(AutonomousSiegeBuild.Complete(BotSiegeKind.Ram, 1_000, 31_000), Is.True);
            Assert.That(AutonomousSiegeBuild.SecondsLeft(BotSiegeKind.Ram, 1_000, 11_000), Is.EqualTo(20));
        }

        [TestCase(true, 0L, 10_000L, true, true, null, TestName = "Build_GoesOn")]
        [TestCase(false, 0L, 10_000L, true, true, "killed", TestName = "Build_BuilderKilled")]
        [TestCase(true, 5_000L, 10_000L, true, true, "attacked", TestName = "Build_BuilderAttacked")]
        [TestCase(true, 0L, 40_000L, true, true, "left", TestName = "Build_BuilderWentAway")]
        [TestCase(true, 0L, 10_000L, false, true, "moved", TestName = "Build_SpotChanged")]
        [TestCase(true, 0L, 10_000L, true, false, "moved", TestName = "Build_KindChanged")]
        public void ABuildIsInterruptedByAnAttackDeathOrBreak(bool alive, long attackedAt, long now, bool sameSpot, bool sameKind, string expected)
        {
            // Started at 1,000; last worked at 9,000; an attack before the start does not count.
            long attacked = attackedAt == 0 ? 500 : attackedAt;
            Assert.That(AutonomousSiegeBuild.Interruption(1_000, 9_000, now, attacked, alive, sameSpot, sameKind), Is.EqualTo(expected));
            Assert.That(AutonomousSiegeBuild.Interruption(0, 0, now, attacked, alive, sameSpot, sameKind), Is.Null, "no build running");
        }

        [Test]
        public void EachSiegeHasAFiniteStockSizedToTheArmy()
        {
            AutonomousSiegeBuild.Reset();
            Assert.That(AutonomousSiegeBuild.Stock(3), Is.Zero);
            Assert.That(AutonomousSiegeBuild.Stock(4), Is.EqualTo(1 + 2));
            Assert.That(AutonomousSiegeBuild.Stock(100), Is.EqualTo(6 + 8));
            string keep = "rvr-keep-test-" + Guid.NewGuid();
            Assert.That(AutonomousSiegeBuild.Remaining(163, keep, eRealm.Albion, 100, 1_000, out int stock), Is.EqualTo(14));
            Assert.That(stock, Is.EqualTo(14));
            for (int i = 0; i < 14; i++) Assert.That(AutonomousSiegeBuild.TryTake(163, keep, eRealm.Albion, 40, 2_000 + i), Is.True, "the army that came sets the stock");
            Assert.That(AutonomousSiegeBuild.TryTake(163, keep, eRealm.Albion, 100, 3_000), Is.False, "stock spent: no 15th engine");
            Assert.That(AutonomousSiegeBuild.Remaining(163, keep, eRealm.Albion, 100, 4_000, out _), Is.Zero);
            Assert.That(AutonomousSiegeBuild.Remaining(163, keep, eRealm.Midgard, 100, 4_000, out _), Is.EqualTo(14), "per realm");
            // The next siege at the keep, after ten quiet minutes, gets a fresh stock.
            Assert.That(AutonomousSiegeBuild.Remaining(163, keep, eRealm.Albion, 100, 4_000 + AutonomousSiegeBuild.StockIdleResetMilliseconds + 1, out _), Is.EqualTo(14));
            AutonomousSiegeBuild.Reset();
        }

        [Test]
        public void TheStatusTextSaysWhereTheEngineCameFrom()
        {
            string built = AutonomousSiegeBuild.StatusDetail(true, 3, 14);
            Assert.That(built, Does.Contain("built on the spot").And.Contain("3 of 14 left"));
            Assert.That(built, Does.Not.Contain("Real siege equipment"));
            Assert.That(AutonomousSiegeBuild.StatusDetail(false, 0, 0), Does.Contain("Carried siege kit"));
        }

        // ---- door repair ----

        [Test]
        public void DoorRepairFollowsThePlayersWoodRule()
        {
            Assert.That(RepairCommandHandler.GetNeededWoodForOneTick(5), Is.EqualTo(3576));
            Assert.That(RepairCommandHandler.GetNeededWoodForOneTick(4), Is.EqualTo(840));
            Assert.That(RepairCommandHandler.RepairChance(300, 5), Is.EqualTo(1.0));
            Assert.That(RepairCommandHandler.DoorRepairShare, Is.EqualTo(0.05));
            Assert.That(AutonomousDoorRepair.RepairMilliseconds, Is.EqualTo(20_000));
            Assert.That(AutonomousDoorRepair.WoodUnits(new[] { ("duskwood wooden boards", 10), ("oak wooden boards", 3), ("iron bar", 50) }),
                Is.EqualTo(10 * 136 + 3 * 8));
            Assert.That(AutonomousDoorRepair.RepairSkill(0), Is.EqualTo(AutonomousDoorRepair.UntrainedRepairSkill));
            Assert.That(AutonomousDoorRepair.RepairSkill(450), Is.EqualTo(450));
        }

        [Test]
        public void NoRepairWhileHitOrWithEnemiesNear()
        {
            Assert.That(AutonomousDoorRepair.Blocked(true, false, false, false, false, 40, true, 4000, 3576), Is.Null);
            Assert.That(AutonomousDoorRepair.Blocked(true, true, false, false, false, 40, true, 4000, 3576), Is.EqualTo("in_combat"));
            Assert.That(AutonomousDoorRepair.Blocked(true, false, false, true, false, 40, true, 4000, 3576), Is.EqualTo("door_under_attack"));
            Assert.That(AutonomousDoorRepair.Blocked(true, false, false, false, true, 40, true, 4000, 3576), Is.EqualTo("enemies_near"));
            Assert.That(AutonomousDoorRepair.Blocked(true, false, false, false, false, 40, true, 100, 3576), Is.EqualTo("no_wood"));
            Assert.That(AutonomousDoorRepair.Blocked(true, false, false, false, false, 40, false, 4000, 3576), Is.EqualTo("too_far"));
            Assert.That(AutonomousDoorRepair.Blocked(true, false, false, false, false, 100, true, 4000, 3576), Is.EqualTo("full"));
            Assert.That(AutonomousDoorRepair.Blocked(false, false, false, false, false, 40, true, 4000, 3576), Is.EqualTo("dead"));
        }

        [Test]
        public void WoodIsBoughtInWholePacksWithRealCoinOnly()
        {
            // Six repairs of a level-5 door in duskwood (136 units a board), packs of 10 at 50 copper, plenty of coin.
            var plan = AutonomousDoorRepair.PlanPurchase(3576 * 6, 3576, 136, 10, 50, 1000, 1_000_000);
            Assert.That(plan.Boards, Is.EqualTo(160));
            Assert.That(plan.Packs, Is.EqualTo(16));
            Assert.That(plan.Cost, Is.EqualTo(800));
            // Coin for two packs: 20 boards, short of one repair (27 boards), so nothing is bought.
            Assert.That(AutonomousDoorRepair.PlanPurchase(3576 * 6, 3576, 136, 10, 50, 1000, 100).Packs, Is.Zero);
            // Coin for three packs: one repair's worth (30 boards) is bought.
            var one = AutonomousDoorRepair.PlanPurchase(3576 * 6, 3576, 136, 10, 50, 1000, 150);
            Assert.That((one.Packs, one.Boards, one.Cost), Is.EqualTo((3, 30, 150L)));
            // A stack limit caps one trip.
            Assert.That(AutonomousDoorRepair.PlanPurchase(3576 * 6, 3576, 136, 10, 50, 100, 1_000_000).Boards, Is.EqualTo(100));
            Assert.That(AutonomousDoorRepair.PlanPurchase(100, 100, 0, 1, 50, 100, 1_000_000).Packs, Is.Zero, "not wood");
        }

        [Test]
        public void AtMostTwoRepairersShareADoor()
        {
            AutonomousDoorRepair.ResetForTests();
            Bot a = MakeBot(), b = MakeBot(), c = MakeBot();
            Assert.That(AutonomousDoorRepair.TryClaim(163, 1, a, 1_000), Is.True);
            Assert.That(AutonomousDoorRepair.TryClaim(163, 1, b, 1_000), Is.True);
            Assert.That(AutonomousDoorRepair.TryClaim(163, 1, c, 1_000), Is.False);
            Assert.That(AutonomousDoorRepair.TryClaim(163, 1, a, 2_000), Is.True, "a holder keeps its place");
            Assert.That(AutonomousDoorRepair.TryClaim(163, 2, c, 1_000), Is.True, "another door");
            AutonomousDoorRepair.Release(a);
            Assert.That(AutonomousDoorRepair.TryClaim(163, 1, c, 3_000), Is.True);
            AutonomousDoorRepair.ResetForTests();
        }
    }
}
