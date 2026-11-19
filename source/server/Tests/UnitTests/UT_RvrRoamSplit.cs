using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 6: the population split (owner decision 2: 55% warbands, 30% eights, 15% soloers,
    /// duos and stealthers, of the RvR gamebots not in a siege) over many bots and many formation passes, the setting's
    /// text, the party sizes and the solo reserve. Pure.
    /// </summary>
    [TestFixture]
    public class UT_RvrRoamSplit
    {
        [Test]
        public void The_setting_reads_three_numbers_and_falls_back_to_55_30_15()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrRoamShares.Parse("55/30/15"), Is.EqualTo(new RvrRoamShares(55, 30, 15)));
                Assert.That(RvrRoamShares.Parse(" 60, 30 ,10 "), Is.EqualTo(new RvrRoamShares(60, 30, 10)));
                Assert.That(RvrRoamShares.Parse("1/1/1").Share(RvrPartyKind.Eight), Is.EqualTo(1 / 3.0).Within(1e-9));
                Assert.That(RvrRoamShares.Parse("0/100/0").Share(RvrPartyKind.Eight), Is.EqualTo(1.0));
                foreach (string bad in new[] { null, "", "55/30", "a/b/c", "-5/50/55", "0/0/0", "1/2/3/4" })
                    Assert.That(RvrRoamShares.Parse(bad), Is.EqualTo(RvrRoamShares.Default), bad ?? "null");
                Assert.That(RvrRoamShares.Default.ToString(), Is.EqualTo("55/30/15"));
                Assert.That(RvrRoamShares.Default.Share(RvrPartyKind.Small), Is.EqualTo(0.15).Within(1e-9));
            });
        }

        private sealed record Party(RvrPartyKind Kind, int Size);

        /// <summary>
        /// One realm: <paramref name="bots"/> RvR bots, all free at first. Each pass up to 15 free leaders try to form a
        /// party (as the coordinator's pass does), and some parties end (task expiry, wipes) and free their members.
        /// </summary>
        private static (RvrRoamCensus Census, int Formed) Simulate(int bots, RvrRoamShares shares, int passes, double endShare, int seed)
        {
            var random = new Random(seed);
            var parties = new List<Party>();
            int formed = 0;
            RvrRoamCensus census = new(0, 0, bots);
            for (int pass = 0; pass < passes; pass++)
            {
                foreach (Party ended in parties.Where(_ => random.NextDouble() < endShare).ToArray())
                {
                    parties.Remove(ended);
                    census = ended.Kind == RvrPartyKind.Warband
                        ? census with { Warband = census.Warband - ended.Size, Small = census.Small + ended.Size }
                        : census with { Eight = census.Eight - ended.Size, Small = census.Small + ended.Size };
                }
                for (int leader = 0; leader < 15 && census.Small >= 2; leader++)
                {
                    bool stealther = random.NextDouble() < 0.12;
                    RvrPartyKind kind = RvrRoamSplit.Choose(census, shares, stealther, random.NextDouble());
                    int rolled = AutonomousRvrStaging.RollWarbandSize(8, random.NextDouble(), 50);
                    int size = Math.Min(census.Small, RvrRoamSplit.Size(kind, rolled, 8, 50, random.NextDouble()));
                    if (size < 2 || kind == RvrPartyKind.Small)
                        continue; // alone, or a duo that stays in the small share
                    parties.Add(new Party(kind, size));
                    census = census.Formed(kind, size);
                    formed++;
                }
            }
            return (census, formed);
        }

        [TestCase(300, 1)]
        [TestCase(1_000, 2)]
        [TestCase(3_000, 3)]
        public void Over_many_bots_the_split_settles_at_55_30_15(int bots, int seed)
        {
            (RvrRoamCensus census, int formed) = Simulate(bots, RvrRoamShares.Default, 400, 0.02, seed);
            Assert.That(census.Total, Is.EqualTo(bots));
            Assert.That(formed, Is.GreaterThan(bots / 8));
            double Share(int count) => count * 100.0 / bots;
            Assert.Multiple(() =>
            {
                Assert.That(Share(census.Warband), Is.EqualTo(55).Within(4), "warbands");
                Assert.That(Share(census.Eight), Is.EqualTo(30).Within(4), "eights");
                Assert.That(Share(census.Small), Is.EqualTo(15).Within(4), "soloers, duos and stealthers");
            });
        }

        [Test]
        public void Another_split_is_followed_too()
        {
            (RvrRoamCensus census, _) = Simulate(2_000, RvrRoamShares.Parse("40/40/20"), 400, 0.02, 9);
            Assert.That(census.Warband * 100.0 / 2_000, Is.EqualTo(40).Within(4));
            Assert.That(census.Eight * 100.0 / 2_000, Is.EqualTo(40).Within(4));
            Assert.That(census.Small * 100.0 / 2_000, Is.EqualTo(20).Within(4));
        }

        [Test]
        public void The_kind_furthest_below_its_share_is_chosen()
        {
            RvrRoamShares shares = RvrRoamShares.Default;
            Assert.Multiple(() =>
            {
                Assert.That(RvrRoamSplit.Choose(new(0, 0, 100), shares, false, 0.5), Is.EqualTo(RvrPartyKind.Warband), "all free: warbands first");
                Assert.That(RvrRoamSplit.Choose(new(55, 0, 45), shares, false, 0.5), Is.EqualTo(RvrPartyKind.Eight));
                Assert.That(RvrRoamSplit.Choose(new(55, 30, 15), shares, false, 0.5), Is.EqualTo(RvrPartyKind.Small), "on target: no new party");
                Assert.That(RvrRoamSplit.Choose(new(60, 35, 5), shares, false, 0.5), Is.EqualTo(RvrPartyKind.Small), "too few alone: stay alone");
                Assert.That(RvrRoamSplit.Choose(new(40, 20, 40), shares, true, 0.5), Is.EqualTo(RvrPartyKind.Warband),
                    "a stealther leader stays alone only while the small share is not clearly over");
                Assert.That(RvrRoamSplit.Choose(new(52, 29, 19), shares, true, 0.5), Is.EqualTo(RvrPartyKind.Small), "a little over: the stealther stays alone");
                Assert.That(RvrRoamSplit.Choose(default, shares, false, 0.5), Is.EqualTo(RvrPartyKind.Small), "nobody counted");
            });
        }

        [Test]
        public void Party_sizes_per_kind()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Eight, 3, 8, 50, 0.5), Is.EqualTo(8));
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Eight, 3, 5, 50, 0.5), Is.EqualTo(5), "what the reserve allows");
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Small, 6, 8, 50, 0.1), Is.EqualTo(2), "a duo");
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Small, 6, 8, 50, 0.9), Is.EqualTo(1), "alone");
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Small, 6, 1, 50, 0.1), Is.EqualTo(1), "no room for a duo");
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Warband, 6, 8, 50, 0.5), Is.EqualTo(6), "today's roll kept");
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Warband, 6, 4, 50, 0.5), Is.EqualTo(4));
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Warband, 1, 1, 50, 0.5), Is.EqualTo(1));
            });
            for (double roll = 0; roll < 1; roll += 0.01)
            {
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Warband, 1, 8, 50, roll), Is.InRange(AutonomousRvrStaging.SiegeWarbandSize, 8),
                    "a level 50 warband is siege-sized (the split, not the roll, decides who is alone)");
                Assert.That(RvrRoamSplit.Size(RvrPartyKind.Warband, 1, 8, 30, roll), Is.InRange(2, 8));
            }
        }

        [Test]
        public void The_solo_reserve_follows_the_split_and_keeps_its_old_value_without_it()
        {
            Assert.That(AutonomousBotGroupCoordinator.MinimumSoloRvrReserve(100), Is.EqualTo(35), "the old 35%");
            Assert.That(AutonomousBotGroupCoordinator.MinimumSoloRvrReserve(10), Is.EqualTo(4));
            Assert.That(AutonomousBotGroupCoordinator.MinimumSoloRvrReserve(100, RvrRoamShares.Default.Share(RvrPartyKind.Small)), Is.EqualTo(15));
            Assert.That(AutonomousBotGroupCoordinator.MinimumSoloRvrReserve(10, 0.15), Is.EqualTo(2), "at least two");
            Assert.That(AutonomousBotGroupCoordinator.MinimumSoloRvrReserve(3, 0.15), Is.EqualTo(1));
        }
    }
}
