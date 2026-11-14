using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.Unit
{
    /// <summary>When an epic event is open to a raid: rules ported from Offline DAoC True 1.65 (shadowofze, 2026-11-12).</summary>
    [TestFixture]
    public class UT_RealmRaidAvailability
    {
        private const long Minute = 60_000;

        [Test]
        public void CaerSidiReopensAfterAFailedHorsemanFightAndStaysClosedOnlyForItsRespawn()
        {
            Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(false, 0, true), Is.True, "untouched");
            Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(true, 0, true), Is.True, "a raid failed with a horseman still up");
            Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(true, 30 * Minute, false), Is.False, "Apocalypse died: its respawn");
            Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(true, 0, false), Is.False, "Apocalypse just died, respawn not yet set");
        }

        [Test]
        public void TuscaranNeedsTheKingAndTheQueen()
        {
            var tuscaran = System.Array.Find(AutonomousRealmRaid.Definitions, d => d.Id == "epic-midgard");
            Assert.That(AutonomousRealmRaid.StandingFinals(tuscaran), Is.EquivalentTo(new[] { "KingTuscar", "QueenKula" }));
            var caerSidi = System.Array.Find(AutonomousRealmRaid.Definitions, d => d.Id == "epic-albion");
            Assert.That(AutonomousRealmRaid.StandingFinals(caerSidi), Is.EqualTo(new[] { "ApocInitializator" }), "Apocalypse appears in the fight");
        }
    }
}
