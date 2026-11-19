using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The Spirit of Lord Emthoro raid musters at Fort Gwyntell (owner PC, 2026-11-16, ccba364): from Caer Diogel it walked
    /// through Gwyddneau's marooned viking camp and Aldland's lammia camp (240 deaths, 8 of 40 arrived, a failed rally).
    /// A forced raid recruits round the first hub and an event takes the first hub with a complete route.
    /// </summary>
    [TestFixture]
    public class UT_EmthoroHub
    {
        [Test]
        public void EmthoroMustersAtFortGwyntellFirstWithCaerDiogelAsTheFallback()
        {
            string[] hubs = RealmRaidMuster.Hubs.Where(h => h.Event == "openworld-albion-emthoro").Select(h => h.Name).ToArray();
            Assert.That(hubs, Is.EqualTo(new[] { "Fort Gwyntell", "Caer Diogel" }));
        }

        [Test]
        public void ColiathKeepsCaerDiogelFirst()
        {
            // Coliath (in Aldland) was won from Caer Diogel on 1dc66b6; only Emthoro's order changes.
            Assert.That(RealmRaidMuster.Hubs.First(h => h.Event == "openworld-albion-coliath").Name, Is.EqualTo("Caer Diogel"));
        }
    }
}
