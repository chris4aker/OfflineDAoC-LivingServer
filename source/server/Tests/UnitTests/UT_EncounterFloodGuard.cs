using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The flood guards (1.2, epic wow): mechanics that count raiders are capped at the encounter's period raid size
    /// (docs/RAID-SIZES.md, AutonomousRealmRaid.Definitions), each behind its own setting.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EncounterFloodGuard
    {
        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            EncounterFloodGuard.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            EncounterFloodGuard.Reset();
            _server.Dispose();
        }

        [Test]
        public void EachGuardIsCappedAtItsRaidsPeriodSize()
        {
            Assert.That(EncounterFloodGuard.ShippedLimit("Legion.Adds"), Is.EqualTo(64));
            Assert.That(EncounterFloodGuard.ShippedLimit("Legion.KillHeal"), Is.EqualTo(64));
            Assert.That(EncounterFloodGuard.ShippedLimit("HighLordSaeor.KillHeal"), Is.EqualTo(64));
            Assert.That(EncounterFloodGuard.ShippedLimit("Beliathan.Minions"), Is.EqualTo(64));
            Assert.That(EncounterFloodGuard.ShippedLimit("Nosdoden.Spirits"), Is.EqualTo(64));
            Assert.That(EncounterFloodGuard.ShippedLimit("OFGreenKnight.Trees"), Is.EqualTo(70), "on the Albion dragon raid's way");
            Assert.That(EncounterFloodGuard.ShippedLimit("Oona.Undead"), Is.EqualTo(70), "on the Midgard dragon raid's way");
            Assert.That(EncounterFloodGuard.ShippedLimit("RisnirCruss.Apparitions"), Is.EqualTo(40), "no raid: the smallest period raid");
            Assert.That(EncounterFloodGuard.ShippedLimit("Unknown"), Is.Zero);
            foreach (EncounterFloodGuard.Guard g in EncounterFloodGuard.Shipped.Where(g => g.RaidId != null))
                Assert.That(AutonomousRealmRaid.Definitions.Any(d => d.Id == g.RaidId), Is.True, g.Key + " names a real raid");
        }

        [Test]
        public void ACountUpToThePeriodSizeIsTheOriginalAboveItThePeriodRaids()
        {
            Assert.That(EncounterFloodGuard.Cap("Legion.Adds", 1), Is.EqualTo(1), "you alone: as before");
            Assert.That(EncounterFloodGuard.Cap("Legion.Adds", 64), Is.EqualTo(64), "a full period raid: as for 64 players");
            Assert.That(EncounterFloodGuard.Cap("Legion.Adds", 152), Is.EqualTo(64), "a 200% bot raid and the Special Forces: 64");
            Assert.That(EncounterFloodGuard.Cap("Legion.Adds", 152) / 4, Is.EqualTo(16), "16 adds a step, not 38");
            Assert.That(EncounterFloodGuard.MayAdd("Beliathan.Minions", 63), Is.True);
            Assert.That(EncounterFloodGuard.MayAdd("Beliathan.Minions", 64), Is.False);
        }

        [Test]
        public void EachGuardIsItsOwnSetting()
        {
            EncounterFloodGuard.SetLimit("Legion.Adds", 0);
            Assert.That(EncounterFloodGuard.Cap("Legion.Adds", 152), Is.EqualTo(152), "0: no cap, the count grows with the raid");
            Assert.That(EncounterFloodGuard.Cap("Legion.KillHeal", 152), Is.EqualTo(64), "the other guards keep theirs");
            EncounterFloodGuard.SetLimit("Legion.Adds", 100);
            Assert.That(EncounterFloodGuard.Cap("Legion.Adds", 152), Is.EqualTo(100));
            Assert.That(EncounterFloodGuard.Values(), Does.Contain("Legion.Adds=100"));
            EncounterFloodGuard.SetLimit("Legion.Adds", null);
            Assert.That(EncounterFloodGuard.Limit("Legion.Adds"), Is.EqualTo(64), "null: back to the shipped cap");
        }

        [Test]
        public void HealsByDeathsAreCappedInARollingWindow()
        {
            var boss = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(boss, new PropertyCollection());
            EncounterFloodGuard.SetLimit("Legion.KillHeal", 3);
            Assert.That(Enumerable.Range(0, 5).Count(i => EncounterFloodGuard.TryRecent(boss, "Legion.KillHeal", 1_000 + i)), Is.EqualTo(3));
            Assert.That(EncounterFloodGuard.TryRecent(boss, "Legion.KillHeal", 1_000 + EncounterFloodGuard.RecentWindowMilliseconds - 1), Is.False,
                "still inside the window");
            Assert.That(EncounterFloodGuard.TryRecent(boss, "Legion.KillHeal", 1_000 + EncounterFloodGuard.RecentWindowMilliseconds), Is.True,
                "the first heal left the window: deaths later in a long fight heal again");
        }
    }
}
