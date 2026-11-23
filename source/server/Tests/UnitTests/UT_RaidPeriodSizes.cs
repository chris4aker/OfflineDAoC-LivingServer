using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2.2: raised period sizes (owner, 2026-11-22: "If the mechanics and tactics are working but the period
    /// raider size from logs isn't cutting it, and adding more bots without it becoming a Zerg wins, we should bump the
    /// suggested raider and period size"). docs/RAID-SIZES.md, "Raising a period size". Tests only: the raises rest on
    /// simulations, none on a PC log.
    /// </summary>
    [TestFixture]
    public class UT_RaidPeriodSizes
    {
        [Test]
        public void EachRaiseIsTheRaidsPeriodSizeAndWithinTheNoZergLimit()
        {
            Assert.That(RaidPeriodSizes.Raises.Select(r => r.RaidId), Is.Unique);
            Assert.Multiple(() =>
            {
                foreach (RaidPeriodSizes.Raise raise in RaidPeriodSizes.Raises)
                {
                    var definition = AutonomousRealmRaid.Definitions.SingleOrDefault(d => d.Id == raise.RaidId);
                    Assert.That(definition, Is.Not.Null, raise.RaidId + " is a raid");
                    if (definition == null) continue;
                    Assert.That(definition.SuggestedBots, Is.EqualTo(raise.RaisedBots), raise.RaidId + ": its SuggestedBots is the raise");
                    Assert.That(raise.RaisedBots, Is.GreaterThan(raise.EraBots), raise.RaidId + ": a raise, not a cut");
                    Assert.That(raise.RaisedBots % 8, Is.Zero, raise.RaidId + ": whole parties");
                    Assert.That(raise.RaisedBots, Is.LessThanOrEqualTo(RaidPeriodSizes.NoZergLimit(raise.SourceBots)),
                        raise.RaidId + ": at most twice the period source's own figure");
                    Assert.That(raise.RaisedBots, Is.LessThanOrEqualTo(RealmRaidRecruitmentPolicy.MaximumBots), raise.RaidId + ": never past 300");
                    Assert.That(raise.SourceBots, Is.InRange(8, raise.EraBots), raise.RaidId + ": the source's figure, at most the old period size");
                    Assert.That(raise.Evidence, Is.Not.Empty, raise.RaidId);
                }
            });
        }

        [Test]
        public void TheNoZergLimitIsTwiceTheSourcesFigureNeverPastThreeHundred()
        {
            Assert.That(RaidPeriodSizes.NoZergFactor, Is.EqualTo(2));
            Assert.That(RaidPeriodSizes.NoZergLimit(40), Is.EqualTo(80), "four to five groups");
            Assert.That(RaidPeriodSizes.NoZergLimit(100), Is.EqualTo(200));
            Assert.That(RaidPeriodSizes.NoZergLimit(200), Is.EqualTo(300), "never past the realm's 300");
            Assert.That(RaidPeriodSizes.NoZergLimit(-5), Is.Zero);
            Assert.That(RaidPeriodSizes.ControlMaxWinShare, Is.LessThan(RaidPeriodSizes.TargetWinShare / 2));
            Assert.That(RaidPeriodSizes.TargetWinShare * 100, Is.EqualTo(RealmRaidRecruitmentPolicy.TargetWinPercentSetting).Within(0.5),
                "the win balance's own target");
        }

        // 1.2.2's decisions, from the 1.2.1 sims' size sweeps (docs/RAID-SIZES.md).
        [TestCase("summoners-albion", 64, 80)]
        [TestCase("summoners-midgard", 64, 80)]
        [TestCase("summoners-hibernia", 64, 80)]
        [TestCase("epic-hibernia", 100, 112)]
        [TestCase("epic-midgard", 100, 120)]
        public void Raised(string id, int before, int now)
        {
            RaidPeriodSizes.Raise raise = RaidPeriodSizes.Of(id);
            Assert.That(raise, Is.Not.Null);
            Assert.That((raise.EraBots, raise.RaisedBots, RaidPeriodSizes.PeriodSize(id)), Is.EqualTo((before, now, now)));
        }

        // Kept: the sims win at the period size with the 1.2.1 tactics, or a larger size wins with the old rules too.
        [TestCase("epic-albion", 100)]
        [TestCase("dungeon-midgard-trollheim", 64)]
        [TestCase("dragon-albion", 70)]
        [TestCase("dragon-midgard", 70)]
        [TestCase("dragon-hibernia", 70)]
        [TestCase("darkness-albion", 64)]
        [TestCase("darkness-midgard", 64)]
        [TestCase("darkness-hibernia", 64)]
        [TestCase("openworld-hibernia-marfoirwort", 40)]
        [TestCase("dungeon-albion-krondon", 64)]
        // The Green Knight's raise to 80 was withdrawn on the real stats (owner, 2026-11-22, "option 1"): his period 40, and
        // his tactic fixed instead (tag and burn, UT_RaidSim122GreenKnight).
        [TestCase("openworld-albion-greenknight", 40)]
        public void Kept(string id, int size)
        {
            Assert.That(RaidPeriodSizes.Of(id), Is.Null);
            Assert.That(RaidPeriodSizes.PeriodSize(id), Is.EqualTo(size));
            Assert.That(RaidPeriodSizes.EraBots(id), Is.EqualTo(size));
        }

        [Test]
        public void NoBossMechanicMovesWithARaise()
        {
            EncounterFloodGuard.Reset();
            Assert.Multiple(() =>
            {
                // The Green Knight's trees stay capped at the Albion dragon raid's 70.
                Assert.That(EncounterFloodGuard.ShippedLimit("OFGreenKnight.Trees"), Is.EqualTo(70));
                Assert.That(EncounterFloodGuard.Shipped.Single(g => g.Key == "OFGreenKnight.Trees").RaidId, Is.EqualTo("dragon-albion"));
                Assert.That(EncounterFloodGuard.ShippedLimit("Nosdoden.Spirits"), Is.EqualTo(64));
                Assert.That(EncounterFloodGuard.ShippedLimit("Legion.Adds"), Is.EqualTo(64));
                foreach (EncounterFloodGuard.Guard g in EncounterFloodGuard.Shipped)
                    Assert.That(RaidPeriodSizes.Of(g.RaidId), Is.Null, g.Key + ": no guard reads a raised raid's size");
            });
        }

        [Test]
        public void AnOldRecordCarriesOverInBotsNotPercent()
        {
            Assert.Multiple(() =>
            {
                // 166% of 64 (106 bots) at Summoner's Hall, written by 1.2.1: 132.8% of 80, the same 106.
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", 166, null), Is.EqualTo(166.0 * 64 / 80).Within(1e-9));
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", 166, 80), Is.EqualTo(166), "written at 80: as it is");
                Assert.That(RaidPeriodSizes.Rebase("openworld-albion-greenknight", 200, null), Is.EqualTo(200), "his raise withdrawn: unchanged");
                Assert.That(RaidPeriodSizes.Rebase("epic-midgard", 125, 100), Is.EqualTo(125.0 * 100 / 120).Within(1e-9));
                Assert.That(RaidPeriodSizes.Rebase("dragon-albion", 145.3, null), Is.EqualTo(145.3), "not raised: unchanged");
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", null, 64), Is.Null);
            });
        }

        [Test]
        public void TheRecordSaysWhatItsSizeIsOf()
        {
            string details = RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithWipes("Defeated at Summoner Roesia.", 2), 125, 80);
            string withLeader = DOL.GS.LoreChannels.LoreChronicle.WithRaidLeader(details, "Sir Ector");
            Assert.Multiple(() =>
            {
                Assert.That(details, Does.EndWith(" Raid size: 125% of 80."));
                Assert.That(RealmEventRecordStore.RaidSizeIn(details), Is.EqualTo(125));
                Assert.That(RealmEventRecordStore.RaidSizeOfIn(details), Is.EqualTo(80));
                Assert.That(RealmEventRecordStore.RaidSizeOfIn(withLeader), Is.EqualTo(80), "the leader follows it");
                Assert.That(RealmEventRecordStore.WipesIn(details), Is.EqualTo(2), "the other markers still read");
                string old = RealmEventRecordStore.WithRaidSize("x", 145.3);
                Assert.That(old, Is.EqualTo("x Raid size: 145.3%."), "the old form, unchanged");
                Assert.That(RealmEventRecordStore.RaidSizeOfIn(old), Is.Null);
                Assert.That(RealmEventRecordStore.RaidSizeOfIn("x Raid size: 12% of ."), Is.Null);
                Assert.That(RealmEventRecordStore.RaidSizeOfIn(null), Is.Null);
                Assert.That(RealmEventRecordStore.WithRaidSize("x", null, 80), Is.EqualTo("x"), "an unsized raid writes nothing");
            });
        }

        [Test]
        public void TheLedgerLoadsARaisedRaidsOldRowsInBots()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ut-raid-sizes-" + Guid.NewGuid().ToString("N"));
            string path = RealmEventRecordStore.PathIn(folder);
            try
            {
                int n = 0;
                RealmEventRecord Row(string outcome, string details) =>
                    new(Guid.NewGuid().ToString("N"), "summoners-midgard", "Summoner's Hall", "Raid", "Midgard",
                        $"2026-11-20T10:{n:00}:00.0000000Z", $"2026-11-20T11:{n++:00}:00.0000000Z", "Ended", outcome, details, 80, 70);
                RealmEventRecordStore.Save(path, [
                    Row("Defeated", RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithWipes("Defeated at Summoner Roesia.", 2), 145.3)),
                    Row("Boss defeated", RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithWipes("Final defeated.", 0), 125, 80)),
                ]);
                var rows = RealmEventRecordStore.SizedResults(path, "summoners-midgard", 10, automaticOnly: true);
                Assert.Multiple(() =>
                {
                    Assert.That(rows.Select(r => r.SizeOf), Is.EqualTo(new int?[] { null, 80 }));
                    Assert.That(RealmEventRecordStore.Results(path, "summoners-midgard", 10).Select(r => r.SizePercent),
                        Is.EqualTo(new double?[] { 145.3, 125 }), "the old reader is unchanged");
                    var results = RealmRaidRecord.FromLedger("summoners-midgard", rows);
                    Assert.That(results, Has.Count.EqualTo(2));
                    Assert.That(results[0].SizePercent, Is.EqualTo(145.3 * 64 / 80).Within(1e-9), "1.2.1's row is a percentage of 64");
                    Assert.That(results[1].SizePercent, Is.EqualTo(125.0).Within(1e-9), "1.2.2's row says it is of 80");
                });
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
