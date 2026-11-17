using System;
using System.Linq;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.GS.Tests
{
    /// <summary>
    /// fix/rvr-quiet-frontier (owner's PC log 2026-11-17, build 19e7037): roaming eights rarely hunted. 27 eights formed
    /// against 235 warbands (178 of them pairs: an eight that came up short formed as a warband at once); 18 of the 27 were
    /// drafted into another realm's keep event and only 2 ever walked a circuit; the spots had no enemy levellers overnight.
    /// The rules: a short eight waits for its pool, a roaming eight defends but is not drafted into an attack, and at its
    /// check spot it hunts enemy RvR bots its numbers would take on. Pure, plus the event layer's join.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrQuietFrontier
    {
        [SetUp]
        public void SetUp()
        {
            RvrEventTestState.Clear();
            AutonomousRvrEventLayer.StagedAssault = true;
            AutonomousRvrEventLayer.SiegeScheduling = true;
        }

        [TearDown]
        public void TearDown()
        {
            RvrEventTestState.Clear();
            AutonomousRvrEventLayer.StagedAssault = true;
            AutonomousRvrEventLayer.SiegeScheduling = true;
        }

        private static readonly RvrRoamShares Split = RvrRoamShares.Default;

        [Test]
        public void A_short_eight_waits_for_its_pool_then_forms_as_a_warband()
        {
            var census = new RvrRoamCensus(64, 4, 12); // Albion at 01:44 on the owner's PC
            Assert.Multiple(() =>
            {
                Assert.That(RvrRoamSplit.ShortEight(8, census, Split, 0), Is.EqualTo(RvrPartyKind.Eight));
                Assert.That(RvrRoamSplit.ShortEight(RvrRosterTemplates.MinimumEight, census, Split, 0), Is.EqualTo(RvrPartyKind.Eight));
                Assert.That(RvrRoamSplit.ShortEight(2, census, Split, 0), Is.Null, "two found: no pair-sized 'warband', wait");
                Assert.That(RvrRoamSplit.ShortEight(3, census, Split, RvrRoamSplit.ShortEightWaitMilliseconds - 1), Is.Null);
                Assert.That(RvrRoamSplit.ShortEight(3, census, Split, RvrRoamSplit.ShortEightWaitMilliseconds), Is.EqualTo(RvrPartyKind.Warband),
                    "after five minutes short the realm forms what it has, as before");
                // A realm so thin that its eight share cannot hold a minimum eight forms warbands at once, as before.
                Assert.That(RvrRoamSplit.ShortEight(2, new RvrRoamCensus(8, 0, 2), Split, 0), Is.EqualTo(RvrPartyKind.Warband));
                Assert.That(RvrRoamSplit.ShortEight(2, new RvrRoamCensus(10, 0, 4), Split, 0), Is.Null, "14 bots: 4.2 eight slots, wait");
                Assert.That(RvrRoamSplit.ShortEight(2, census, new RvrRoamShares(100, 0, 0), 0), Is.EqualTo(RvrPartyKind.Warband), "no eight share");
            });
        }

        [TestCase(false, false, ExpectedResult = true, TestName = "Any other force joins an attack as before")]
        [TestCase(false, true, ExpectedResult = true, TestName = "Any other force defends as before")]
        [TestCase(true, true, ExpectedResult = true, TestName = "A roaming eight answers its realm's defence")]
        [TestCase(true, false, ExpectedResult = false, TestName = "A roaming eight is not drafted into another realm's keep")]
        public bool Who_joins_a_keep_event(bool roamingEight, bool defender) => RvrRoamSplit.MayJoinKeepEvent(roamingEight, defender);

        [Test]
        public void A_roaming_eight_is_not_drafted_into_an_attack_but_defends()
        {
            string id = Guid.NewGuid().ToString();
            long opened = GameLoop.GameLoopTime;
            var keep = new AutonomousRvrEventLayer.LiveObjective(id, "Scouted keep",
                AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Hibernia, 200, 100, 100, 0, false, 0, 0, 5, 2);
            var roam = new AutonomousRvrEventLayer.LiveObjective(id + "-roam", "frontier patrol",
                AutonomousRvrEventLayer.Intent.Roam, eRealm.None, 200, 5000, 5000, 0, false, 0, 0, 0, 0);
            AutonomousRvrEventLayer.LiveObjective[] objectives = [keep, roam];
            var attacker = new AutonomousRvrEventLayer.Force(id + "-attack", eRealm.Albion, 8, 50, 1);
            var warband = new AutonomousRvrEventLayer.Force(id + "-warband", eRealm.Albion, 6, 50, 1);
            var eight = new AutonomousRvrEventLayer.Force(id + "-eight", eRealm.Albion, 8, 50, 2, CanSupplySiege: false, RoamingEight: true);
            var defendingEight = new AutonomousRvrEventLayer.Force(id + "-defend", eRealm.Hibernia, 8, 50, 2, CanSupplySiege: false,
                RoamingEight: true);

            Assert.That(AutonomousRvrEventLayer.ForceStart(keep, eRealm.Albion, opened, out _), Is.True);
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(attacker, objectives, opened, 0)?.TargetId, Is.EqualTo(id));
            var warbandPlan = AutonomousRvrEventLayer.ChooseOrJoin(warband, objectives, opened + 1, 0);
            var eightPlan = AutonomousRvrEventLayer.ChooseOrJoin(eight, objectives, opened + 2, 0);

            long firstDeadline = opened + AutonomousRvrEventLayer.HomeMusterMilliseconds + 1;
            AutonomousRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, firstDeadline);
            long marched = firstDeadline + AutonomousRvrEventLayer.MusterExtensionMilliseconds + 1;
            AutonomousRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, marched);
            var defencePlan = AutonomousRvrEventLayer.ChooseOrJoin(defendingEight, objectives, marched + 1, 0);
            Assert.Multiple(() =>
            {
                Assert.That(warbandPlan?.TargetId, Is.EqualTo(id), "an ordinary warband still rallies to its realm's attack");
                Assert.That(eightPlan?.TargetId, Is.Not.EqualTo(id), "the eight keeps roaming");
                Assert.That(eightPlan?.Intent, Is.Not.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
                Assert.That(defencePlan?.TargetId, Is.EqualTo(id), "the keep's own eight answers the defence");
                Assert.That(defencePlan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent));
            });
        }

        [Test]
        public void At_its_spot_an_eight_hunts_levellers_and_the_roamers_its_numbers_take_on()
        {
            Assert.Multiple(() =>
            {
                foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                {
                    Assert.That(RvrRoamCircuits.MayHuntAtSpot(true, 4, 12, tier), Is.True, $"{tier}: a leveller, whatever is near");
                    Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 8, 1, tier), Is.True, $"{tier}: a lone enemy roamer");
                    Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 8, 8, tier), Is.True, $"{tier}: even numbers");
                    Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 8, 11, tier), Is.False, $"{tier}: a bigger force is left alone");
                    Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 0, 1, tier), Is.False, $"{tier}: nobody to hunt with");
                }
                Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 6, 7, RvrSkillTier.Veteran), Is.False, "veterans want even odds");
                Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 6, 7, RvrSkillTier.Average), Is.True);
                Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 6, 8, RvrSkillTier.Average), Is.False);
                Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 6, 8, RvrSkillTier.Casual), Is.True, "casual groups bite off more");
                Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 6, 9, RvrSkillTier.Casual), Is.False);
                Assert.That(RvrRoamCircuits.MayHuntAtSpot(false, 4, 0, RvrSkillTier.Veteran), Is.True, "the target itself is one head");
            });
        }

        /// <summary>
        /// The formation loop the owner's PC showed, over one realm: leaders keep getting "eight" (furthest below its share),
        /// but the free pool in reach rarely holds four. Before the fix every short eight formed as a warband of 2-3; now it
        /// waits until a pool holds four, or five minutes. Pure model: each pass the free pool in reach is drawn from 1-6.
        /// </summary>
        [Test]
        public void Over_a_night_of_passes_short_pools_make_eights_not_pairs()
        {
            int Run(bool wait, out int pairs)
            {
                var random = new Random(1117);
                long shortSince = -1;
                int eights = 0;
                pairs = 0;
                for (long now = 0; now < 4 * 3_600_000L; now += 15_000)
                {
                    int found = 1 + random.Next(6);
                    var census = new RvrRoamCensus(40, 4, 10);
                    RvrPartyKind? kind = wait
                        ? RvrRoamSplit.ShortEight(found, census, Split, shortSince < 0 ? 0 : now - shortSince)
                        : found >= RvrRosterTemplates.MinimumEight ? RvrPartyKind.Eight : RvrPartyKind.Warband;
                    if (kind == null)
                    {
                        if (shortSince < 0) shortSince = now;
                        continue;
                    }
                    shortSince = -1;
                    if (kind == RvrPartyKind.Eight) eights++;
                    else if (found <= 3) pairs++;
                }
                return eights;
            }
            int before = Run(false, out int pairsBefore);
            int after = Run(true, out int pairsAfter);
            Assert.That(after, Is.EqualTo(before), "every pool of four still forms an eight");
            Assert.That(pairsAfter, Is.LessThan(pairsBefore / 10), $"short warbands {pairsBefore} -> {pairsAfter}");
        }
    }
}

namespace DOL.UnitTests
{
    /// <summary>fix/rvr-quiet-frontier: the coordinator's hook leaves a short eight's leader free instead of forming a pair.</summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrQuietFrontierRoster : DutyLeaseFixture
    {
        private bool _tactics, _parties, _checks;
        private string _split;

        [SetUp]
        public void SwitchesOn()
        {
            (_tactics, _parties, _checks, _split) = (Properties.RVR_BOT_FIELD_TACTICS, Properties.RVR_ROAMING_PARTIES,
                Properties.RVR_ROAM_CHECKS, Properties.RVR_ROAM_POPULATION_SPLIT);
            Properties.RVR_BOT_FIELD_TACTICS = Properties.RVR_ROAMING_PARTIES = Properties.RVR_ROAM_CHECKS = true;
            Properties.RVR_ROAM_POPULATION_SPLIT = "0/100/0";
            BotDutyLeases.Clear();
        }

        [TearDown]
        public void Restore()
        {
            (Properties.RVR_BOT_FIELD_TACTICS, Properties.RVR_ROAMING_PARTIES, Properties.RVR_ROAM_CHECKS, Properties.RVR_ROAM_POPULATION_SPLIT) =
                (_tactics, _parties, _checks, _split);
            BotDutyLeases.Clear();
        }

        [Test]
        public void A_short_eight_forms_nothing_this_pass_and_a_full_one_forms()
        {
            Bot[] roster = Enumerable.Range(0, 40).Select(_ => Gamebot(objective: "RvR")).ToArray();
            Bot leader = roster[0];
            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(leader, 3, 8, roster), Is.EqualTo(8));
            GameBot[] thin = roster.Skip(1).Take(2).ToArray();
            GameBot[] none = AutonomousBotGroupCoordinator.RvrTacticsRoster(leader, thin, thin, 8);
            Assert.That(none, Is.Not.Null.And.Empty, "two in reach: no pair, the leader stays free");
            Assert.That(AutonomousRvrStaging.AcceptWarband(8, none.Length), Is.False, "so the pass forms no group");

            Assert.That(AutonomousBotGroupCoordinator.RvrTacticsPartySize(leader, 3, 8, roster), Is.EqualTo(8));
            GameBot[] pool = roster.Skip(1).ToArray();
            GameBot[] eight = AutonomousBotGroupCoordinator.RvrTacticsRoster(leader, pool, pool.Take(7).ToArray(), 8);
            Assert.That(eight, Has.Length.EqualTo(7), "a pool of four or more forms the eight");
        }
    }
}
