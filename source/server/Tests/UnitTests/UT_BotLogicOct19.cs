using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Bot logic fixes from the overnight log of 2026-10-19 (7 hours, 650 bots online).</summary>
    [TestFixture]
    public class UT_BotLogicOct19
    {
        // Levelling solo bots favour camps they can reach soon; far camps keep a floor so bots still spread out.
        [TestCase(0.0, 100)]
        [TestCase(2.0, 92)]
        [TestCase(7.0, 50)]
        [TestCase(14.0, 20)]
        [TestCase(30.0, 10)]
        [TestCase(9999.0, 10)]     // no known crossing
        [TestCase(-1.0, 100)]      // unknown travel time: no penalty
        public void NearCampsWeighMoreButFarOnesStayPossible(double minutes, int percent) =>
            Assert.That(AutonomousBotDecisionEngine.TravelWeightPercent(minutes), Is.EqualTo(percent));

        [Test]
        public void TravelWeightNeverRisesWithDistance()
        {
            int last = 101;
            for (double minutes = 0; minutes < 60; minutes += 0.5)
            {
                int percent = AutonomousBotDecisionEngine.TravelWeightPercent(minutes);
                Assert.That(percent, Is.LessThanOrEqualTo(last), $"{minutes} minutes");
                last = percent;
            }
        }

        [TestCase(GameNPC.eFlags.FLYING, true, 2933, true)]      // the Runemaster's glider
        [TestCase(GameNPC.eFlags.FLYING, true, 1200, false)]
        [TestCase(GameNPC.eFlags.FLYING, false, 2933, false)]    // melee: the approach proof handles it
        [TestCase((GameNPC.eFlags)0, true, 2933, false)]
        public void CastersOnlyPickFlyersTheyCanAlreadyShoot(GameNPC.eFlags flags, bool ranged, double distance, bool skipped) =>
            Assert.That(AutonomousPveTargetPolicy.FlyerOutOfRangedReach(flags, ranged, distance), Is.EqualTo(skipped));

        [Test]
        public void AnOffTankHoldsTheTankSlotOnlyWhenNoRealTankIsThere()
        {
            Assert.Multiple(() =>
            {
                // Midgard level 50s waiting with no Warrior or Thane in reach (overnight 2026-10-19).
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Berserker,
                    eCharacterClass.Healer, eCharacterClass.Runemaster, eCharacterClass.Hunter, eCharacterClass.Shadowblade), Is.EqualTo(5));
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Runemaster,
                    eCharacterClass.Healer, eCharacterClass.Savage, eCharacterClass.Hunter, eCharacterClass.Shadowblade), Is.EqualTo(5));
                // No tank and no off-tank: still no party.
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Runemaster,
                    eCharacterClass.Healer, eCharacterClass.Spiritmaster, eCharacterClass.Hunter, eCharacterClass.Shadowblade), Is.EqualTo(0));
                // A real tank in the pool: the off-tank rule stays off.
                Assert.That(AutonomousBotGroupCoordinator.UseOffTanks(eCharacterClass.Berserker,
                    [eCharacterClass.Warrior, eCharacterClass.Healer]), Is.False);
                Assert.That(AutonomousBotGroupCoordinator.UseOffTanks(eCharacterClass.Berserker,
                    [eCharacterClass.Healer, eCharacterClass.Hunter]), Is.True);
            });
        }

        // The Old Frontiers dungeons (246 Dodens Gruva, 248 Summoner's Hall, 276 Marfach, 277 Hall of the Corrupt)
        // are destinations, never hallways: Albion bots walked past Aidon (277) on the way to Midgard and Hibernia.
        [TestCase((ushort)277, (ushort)1, (ushort)100, false)]   // Albion to Midgard's frontier through Hall of the Corrupt
        [TestCase((ushort)277, (ushort)1, (ushort)246, false)]   // Albion to Dodens Gruva
        [TestCase((ushort)248, (ushort)1, (ushort)200, false)]   // on through Summoner's Hall
        [TestCase((ushort)246, (ushort)100, (ushort)1, false)]   // Midgard to Albion's frontier
        [TestCase((ushort)248, (ushort)1, (ushort)248, true)]    // the neutral raid's own approach
        [TestCase((ushort)277, (ushort)1, (ushort)248, true)]
        [TestCase((ushort)277, (ushort)277, (ushort)1, true)]    // already inside: the way out
        [TestCase((ushort)248, (ushort)246, (ushort)10, true)]   // logged in deep in the hub: still gets home
        [TestCase((ushort)1, (ushort)100, (ushort)10, true)]     // ordinary regions are unaffected
        [TestCase((ushort)249, (ushort)1, (ushort)200, true)]    // Darkness Falls is not part of this rule
        public void FrontierDungeonsAreDestinationsNotHallways(ushort region, ushort start, ushort target, bool allowed) =>
            Assert.That(AutonomousDungeonPolicy.MayRouteThrough(region, start, target), Is.EqualTo(allowed));

        [Test]
        public void WarbandsReachTheOtherFrontiersByPorter()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousFrontierTransport.PorterFrontiers(eRealm.Albion, 1), Is.EquivalentTo(new ushort[] { 100, 200 }));
                Assert.That(AutonomousFrontierTransport.PorterFrontiers(eRealm.Albion, 100), Is.EquivalentTo(new ushort[] { 1, 200 }),
                    "from an enemy frontier: home and the third frontier");
                Assert.That(AutonomousFrontierTransport.PorterFrontiers(eRealm.Hibernia, 200), Is.EquivalentTo(new ushort[] { 1, 100 }));
                Assert.That(AutonomousFrontierTransport.PorterFrontiers(eRealm.Midgard, 277), Is.Empty, "no porter inside a dungeon");
                Assert.That(AutonomousFrontierTransport.PorterFrontiers(eRealm.Albion, 10), Is.Empty, "or in a capital");
            });
        }

        [TestCase(8, 10 * 60_000L)]
        [TestCase(49, 10 * 60_000L)]
        [TestCase(50, 0L)]      // level 50 camps are sparse: keep searching wide
        public void OnlyLevellingBotsPauseTheWideSearchAfterAFailedPull(int level, long pause) =>
            Assert.That(AutonomousWorldBotController.WideTargetSearchPauseFor(level), Is.EqualTo(pause));

        [Test]
        public void LevelFiftyLeadersFormSiegeSizedWarbandsOrRoamAlone()
        {
            Assert.Multiple(() =>
            {
                for (double roll = 0; roll < 1; roll += 0.01)
                {
                    int size = AutonomousRvrStaging.RollWarbandSize(8, roll, 50);
                    Assert.That(size == 1 || size >= AutonomousRvrStaging.SiegeWarbandSize && size <= 8, $"roll {roll}: {size}");
                }
                Assert.That(AutonomousRvrStaging.RollWarbandSize(8, 0.1, 50), Is.EqualTo(1), "a quarter stay solo roamers");
                Assert.That(AutonomousRvrStaging.RollWarbandSize(8, 0.999, 50), Is.EqualTo(8));
                Assert.That(AutonomousRvrStaging.RollWarbandSize(8, 0.3, 49), Is.EqualTo(AutonomousRvrStaging.RollWarbandSize(8, 0.3)),
                    "levelling leaders keep the even roll");
                Assert.That(AutonomousRvrStaging.RollWarbandSize(3, 0.9, 50), Is.EqualTo(AutonomousRvrStaging.RollWarbandSize(3, 0.9)),
                    "no room for a siege warband: even roll");
            });
        }

        [TestCase(6, 5, true)]
        [TestCase(6, 2, true)]     // forms with the members it found
        [TestCase(6, 0, false)]
        [TestCase(1, 0, false)]
        public void AWarbandFormsWithTheMembersItFinds(int rolled, int found, bool forms) =>
            Assert.That(AutonomousRvrStaging.AcceptWarband(rolled, found), Is.EqualTo(forms));

        // Realm raids sized to the roster: 150 level-50 bots per realm, ~110 online, never reached the fixed 200.
        [TestCase(110, 200, 0, 55)]     // half the realm's online level 50s
        [TestCase(60, 200, 0, 40)]      // never below 40
        [TestCase(500, 200, 0, 200)]    // never above the encounter's suggested size
        [TestCase(110, 200, 64, 64)]    // the server setting wins
        public void AutomaticRaidsNeedHalfTheRealmsOnlineLevelFifties(int online, int suggested, int setting, int required) =>
            Assert.That(RealmRaidRecruitmentPolicy.RequiredBots(online, suggested, setting), Is.EqualTo(required));

        [Test]
        public void AScaledRaidStartsAndDepartsOnItsOwnRequirement()
        {
            const long staged = RealmRaidRecruitmentPolicy.AutonomousMinimumStagingMilliseconds;
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, staged, 55, true, 55), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, staged, 54, true, 55), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, RealmRaidRecruitmentPolicy.AutonomousLateStartMilliseconds, 39, true, 55),
                    Is.True, "a scaled raid starts late with 70%");
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, RealmRaidRecruitmentPolicy.AutonomousLateStartMilliseconds, 38, true, 55),
                    Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, RealmRaidRecruitmentPolicy.AutonomousLateStartMilliseconds, 50, true, 70),
                    Is.True, "the failed Caer Sidi rally (50 of 70) would have gone in");
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, staged, 199, true), Is.False, "the default stays 200");
                Assert.That(RealmRaidRecruitmentPolicy.LateStartMinimum(200), Is.EqualTo(180));
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 55, 0, 55), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 33, RealmRaidRecruitmentPolicy.HubPatienceMilliseconds, 55), Is.True,
                    "early departure at 60% after the hub's patience");
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 99, RealmRaidRecruitmentPolicy.HubPatienceMilliseconds), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 100, RealmRaidRecruitmentPolicy.HubPatienceMilliseconds), Is.True,
                    "half of a full raid leaves after the hub's patience (2026-10-21)");
                Assert.That(RealmRaidRecruitmentPolicy.RecruitCap(55), Is.EqualTo(72));
                Assert.That(RealmRaidRecruitmentPolicy.RecruitCap(200), Is.EqualTo(RealmRaidRecruitmentPolicy.MaximumBots));
            });
        }

        [Test]
        public void EachRealmOpensARaidOnASchedule()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.ScheduledRaidDue(1_000, 1_000, false, 110, 150), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.ScheduledRaidDue(999, 1_000, false, 110, 150), Is.False, "not yet due");
                Assert.That(RealmRaidRecruitmentPolicy.ScheduledRaidDue(1_000, 1_000, true, 110, 150), Is.False, "one raid per realm");
                Assert.That(RealmRaidRecruitmentPolicy.ScheduledRaidDue(1_000, 1_000, false, 79, 150), Is.False, "too few level 50s online");
                Assert.That(RealmRaidRecruitmentPolicy.ScheduledRaidDue(1_000, 1_000, false, 110, 0), Is.False, "schedule switched off");
                Assert.That(RealmRaidRecruitmentPolicy.FirstScheduledDelayMilliseconds(0), Is.EqualTo(20 * 60_000L));
                Assert.That(RealmRaidRecruitmentPolicy.FirstScheduledDelayMilliseconds(2), Is.EqualTo(40 * 60_000L));
                Assert.That(RealmRaidRecruitmentPolicy.ScheduleMinutes, Is.EqualTo(150));
                Assert.That(RealmRaidRecruitmentPolicy.MinimumBotsSetting, Is.Zero);
            });
        }

        [Test]
        public void BotsKeepAwayFromWhereAMonsterFarAboveThemKilledOne()
        {
            AutonomousPveBossDanger.Clear();
            var now = new System.DateTime(2026, 10, 19, 13, 0, 0, System.DateTimeKind.Utc);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 30, 12), Is.True, "merucha 30 on a level 12");
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 21, 12), Is.False, "nine levels above");
                    Assert.That(AutonomousPveBossDanger.Marks("bot", 50, 12), Is.False, "enemy bots are RvR, not a camp to avoid");
                    AutonomousPveBossDanger.Record(200, 332000, 431500, 30, now);
                    Assert.That(AutonomousPveBossDanger.IsNear(200, 333500, 432500, 12, now.AddHours(1)), Is.True);
                    Assert.That(AutonomousPveBossDanger.IsNear(200, 336000, 431500, 12, now), Is.False, "4,000 away");
                    Assert.That(AutonomousPveBossDanger.IsNear(100, 332000, 431500, 12, now), Is.False, "another region");
                    Assert.That(AutonomousPveBossDanger.IsNear(200, 332000, 431500, 28, now), Is.False,
                        "the same camp is ordinary for a level 28");
                    Assert.That(AutonomousPveBossDanger.IsNear(200, 332000, 431500, 12, now.AddHours(7)), Is.False, "forgotten after 6 hours");
                    Assert.That(AutonomousPveBossDanger.RouteCrosses(200, 320000, 431000, 345000, 432000, 12, now), Is.True,
                        "a straight walk past the camp");
                    Assert.That(AutonomousPveBossDanger.RouteCrosses(200, 320000, 440000, 345000, 440000, 12, now), Is.False,
                        "8,500 units to the side");
                });
            }
            finally { AutonomousPveBossDanger.Clear(); }
        }

        [Test]
        public void ALevellingBotBendsAroundAPackFarAboveIt()
        {
            // The Connacht merucha pack (levels 30-33) on a level 12's road north.
            var mobs = new[]
            {
                new AutonomousPveDangerBypass.MobView(new(332070, 431451), 30, "merucha", true),
                new AutonomousPveDangerBypass.MobView(new(332839, 430379), 30, "merucha", true),
                new AutonomousPveDangerBypass.MobView(new(333067, 431961), 33, "merucha", true),
                new AutonomousPveDangerBypass.MobView(new(331000, 429000), 11, "hill toad", true),   // blue: not a danger
            };
            var dangers = AutonomousPveDangerBypass.BuildDangers(12, mobs, null);
            System.Numerics.Vector2 start = new(332400, 428200), goal = new(332400, 434500);
            var bends = AutonomousPveDangerBypass.BypassCandidates(start, goal, dangers);
            Assert.Multiple(() =>
            {
                Assert.That(dangers, Has.Count.EqualTo(3));
                Assert.That(dangers[0].Radius, Is.EqualTo(1650), "18 levels above: the widest circle");
                Assert.That(bends, Is.Not.Empty, "a way around inside the 6,000-unit detour limit");
                Assert.That(AutonomousPveDangerBypass.LegClear(start, bends[0], dangers), Is.True);
                Assert.That(AutonomousPveDangerBypass.LegClear(bends[0], goal, dangers), Is.True);
                Assert.That(AutonomousPveDangerBypass.BuildDangers(31, mobs, null), Is.Empty, "the same pack is no danger for a level 31");
                Assert.That(AutonomousPveDangerBypass.BypassCandidates(start, new(340000, 428200), dangers), Is.Empty,
                    "a road that doesn't pass the pack is not bent");
                var remembered = AutonomousPveDangerBypass.BuildDangers(12, null, new[] { (332400, 431300, 30) });
                Assert.That(AutonomousPveDangerBypass.BypassCandidates(start, goal, remembered), Is.Not.Empty,
                    "a remembered death spot is bent around even when its killer isn't in sight");
            });
        }

        [Test]
        public void TheBendLooksAboutThreeThousandUnitsAhead()
        {
            System.Numerics.Vector3[] route = [new(0, 0, 0), new(2000, 0, 0), new(2000, 4000, 0)];
            Assert.That(AutonomousPveDangerBypass.PointAlong(route, 3000), Is.EqualTo(new System.Numerics.Vector3(2000, 1000, 0)));
            Assert.That(AutonomousPveDangerBypass.PointAlong(route, 99999), Is.EqualTo(route[^1]));
        }

        [Test]
        public void ARealmThatHasNotAttackedCatchesUp()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRvrEventLayer.RealmCatchUpChance(20), Is.Zero);
                Assert.That(AutonomousRvrEventLayer.RealmCatchUpChance(55), Is.EqualTo(0.5).Within(1e-9));
                Assert.That(AutonomousRvrEventLayer.RealmCatchUpChance(600), Is.EqualTo(0.5), "at most 50% a minute");
                Assert.That(AutonomousRvrEventLayer.RealmSiegeStartChancePerMinute(1, 0, 10), Is.EqualTo(0.04).Within(1e-9),
                    "a realm that just attacked keeps the old odds");
                Assert.That(AutonomousRvrEventLayer.RealmSiegeStartChancePerMinute(1, 0, 60), Is.EqualTo(0.54).Within(1e-9),
                    "an hour without attacking: Midgard and Hibernia get their turn while Albion's siege runs");
            });
        }

        [Test]
        public void SiegeDefendersFollowRecentResults()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.NextShift(0, [true, true]), Is.Zero, "too few results");
                Assert.That(SiegeDefenseBalance.NextShift(0, [true, true, true]), Is.EqualTo(SiegeDefenseBalance.Step),
                    "attackers took every keep: more defenders");
                Assert.That(SiegeDefenseBalance.NextShift(0, [true, false, false, false, false, false]), Is.EqualTo(-SiegeDefenseBalance.Step),
                    "attackers lost nearly every siege: fewer defenders");
                // Owner 2026-11-09: about half. Three captures in six hold; two take defenders away, four (or two in three) add them.
                Assert.That(SiegeDefenseBalance.NextShift(0, [true, false, true, false, false, false]), Is.EqualTo(-SiegeDefenseBalance.Step),
                    "two in six: fewer defenders");
                Assert.That(SiegeDefenseBalance.NextShift(0, [true, false, true, false, true, false]), Is.Zero, "half: unchanged");
                Assert.That(SiegeDefenseBalance.NextShift(0, [true, true, false]), Is.EqualTo(SiegeDefenseBalance.Step),
                    "two in three: more defenders");
                Assert.That(SiegeDefenseBalance.NextShift(0.25, [true, true, true]), Is.EqualTo(0.375),
                    "past the share's limit the attackers' cap is trimmed (2026-11-06)");
                Assert.That(SiegeDefenseBalance.RatioAt(0.75, 0.375), Is.EqualTo(1.0), "the share never more than 0.25 off the setting");
                Assert.That(SiegeDefenseBalance.NextShift(-0.25, [false, false, false]), Is.EqualTo(-0.25), "nor less");
                Assert.That(DOL.GS.RealmEventPolicy.DefenseRatio, Is.EqualTo(0.75));
            });
        }

        [TestCase(0.0, 130L)]
        [TestCase(0.5, 150L)]
        [TestCase(1.0, 170L)]
        public void RaidSlotsDriftSoTheRealmsTakeTurns(double roll, long minutes) =>
            Assert.That(RealmRaidRecruitmentPolicy.NextScheduleMilliseconds(150, roll), Is.EqualTo(minutes * 60_000L));

        [Test]
        public void ADeathNamesWhatKindOfThingKilledTheBot() =>
            Assert.That(AutonomousDeathDiagnostics.KillerKind(null), Is.EqualTo("none"));
    }
}
