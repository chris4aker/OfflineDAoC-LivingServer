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
    /// suggested raider and period size"). docs/RAID-SIZES.md, "Raising a period size". The three 1.2.2 raises (Summoner's
    /// Hall 80, Galladoria 112, Tuscaran Glacier 120) were withdrawn on audit (owner, 2026-11-23, "Revert all three"): no
    /// encounter is raised, the rule stays, tightened, and a world that ran the raised sizes carries on in bots.
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
                    // The rule's (e), 2026-11-23: the evidence is pinned in a C# test, not only in Python outside the repo.
                    Assert.That(raise.EvidenceTest, Is.Not.Empty, raise.RaidId + ": the C# test that pins its evidence");
                    Assert.That(typeof(UT_RaidPeriodSizes).Assembly.GetType("DOL.UnitTests." + raise.EvidenceTest), Is.Not.Null,
                        raise.RaidId + ": " + raise.EvidenceTest + " is a test in this assembly");
                    // The rule's (d): a raid row shared by several encounters is split before one boss's evidence raises it.
                    Assert.That(AutonomousRealmRaid.Definitions.Count(d => d.Id == raise.RaidId), Is.EqualTo(1), raise.RaidId);
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
            // The rule's (c), 2026-11-23: the control is checked at the size the game really starts a raid at, not at 100%.
            Assert.That(RaidPeriodSizes.ControlStartPercent, Is.EqualTo(RealmRaidRecruitmentPolicy.StartSizePercentSetting),
                "the no-zerg control is checked at the real start size");
            // The rule's (a): a sim's loss is the game's: half the engaged fallen, or a reset from 70% or lower.
            Assert.That(RealmRaidDungeonRoute.ResetWipeLowestPercent, Is.EqualTo(70));
            Assert.That(RealmRaidDungeonRoute.IsWipe(100, 50, 100, 100), Is.True, "half the engaged fallen");
            Assert.That(RealmRaidDungeonRoute.IsWipe(100, 0, 95, 70), Is.True, "a reset from 70%");
            Assert.That(RealmRaidDungeonRoute.IsWipe(100, 49, 60, 60), Is.False);
        }

        [Test]
        public void NoEncounterIsRaised()
        {
            // Owner, 2026-11-23, after an independent audit of the sims: "Revert all three". docs/RAID-SIZES.md lists each
            // withdrawn raise and why.
            Assert.That(RaidPeriodSizes.Raises, Is.Empty);
        }

        // Every encounter at its 2026-11-04 period size. The three withdrawn 1.2.2 raises first (were 80, 80, 80, 112, 120).
        [TestCase("summoners-albion", 64)]
        [TestCase("summoners-midgard", 64)]
        [TestCase("summoners-hibernia", 64)]
        [TestCase("epic-hibernia", 100)]
        [TestCase("epic-midgard", 100)]
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
        // The Green Knight's raise to 80 was withdrawn on the real stats (owner, 2026-11-22, "option 1").
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
        public void ARecordFromAWithdrawnRaiseCarriesOverInBotsNotPercent()
        {
            Assert.Multiple(() =>
            {
                // A 1.2.2 test build wrote "125% of 80" at Summoner's Hall (100 bots): now 156.25% of 64, the same 100.
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", 125, 80), Is.EqualTo(125.0 * 80 / 64).Within(1e-9));
                // 1.2.1 rows (no "of") were of 64 all along: unchanged.
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", 166, null), Is.EqualTo(166));
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", 166, 64), Is.EqualTo(166), "written at 64: as it is");
                Assert.That(RaidPeriodSizes.Rebase("epic-midgard", 125, 120), Is.EqualTo(150.0).Within(1e-9), "150 bots: 150% of 100");
                Assert.That(RaidPeriodSizes.Rebase("epic-hibernia", 100, 112), Is.EqualTo(112.0).Within(1e-9));
                Assert.That(RaidPeriodSizes.Rebase("openworld-albion-greenknight", 125, 80), Is.EqualTo(250.0).Within(1e-9),
                    "a row from his withdrawn 80: the same 100 bots");
                Assert.That(RaidPeriodSizes.Rebase("dragon-albion", 145.3, null), Is.EqualTo(145.3), "not raised: unchanged");
                Assert.That(RaidPeriodSizes.Rebase("summoners-midgard", null, 80), Is.Null);
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
        public void TheLedgerLoadsAWithdrawnRaisesRowsInBots()
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
                    Assert.That(results[0].SizePercent, Is.EqualTo(145.3).Within(1e-9), "1.2.1's row is a percentage of 64, the size today");
                    Assert.That(results[1].SizePercent, Is.EqualTo(125.0 * 80 / 64).Within(1e-9), "a 1.2.2 test build's row of 80: the same 100 bots");
                });
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
