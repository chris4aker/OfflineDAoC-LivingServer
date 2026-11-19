using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Camps of the same monster close together share their bots and monsters (from Offline DAoC True 1.65,
    /// shadowofze).</summary>
    [TestFixture, NonParallelizable]
    public class UT_CampCompetitionShared
    {
        [Test]
        public void CampsOfTheSameSpawnsShareTheirBotsAndMonsters()
        {
            const string a = "t1106:202:69:152:haunted driftwood", b = "t1106:202:70:152:haunted driftwood",
                c = "t1106:202:71:153:haunted driftwood", far = "t1106:202:90:152:haunted driftwood",
                other = "t1106:202:70:152:river sprite";
            try
            {
                AutonomousCampCompetition.PublishCatalog([
                    (a, 202, 293_000, 640_000, "haunted driftwood", 2, false),
                    (b, 202, 295_668, 641_600, "Haunted Driftwood", 3, false),
                    (c, 202, 298_000, 643_000, "haunted driftwood", 2, false),
                    (far, 202, 380_000, 640_000, "haunted driftwood", 6, false),
                    (other, 202, 295_700, 641_600, "river sprite", 5, false)]);
                Assert.That(AutonomousCampCompetition.SameSpawns(a, b), Is.True);
                Assert.That(AutonomousCampCompetition.SameSpawns(a, far), Is.False);
                Assert.That(AutonomousCampCompetition.SameSpawns(b, other), Is.False, "another monster does not share");
                Assert.That(AutonomousCampCompetition.SharedLiveMobs(b, 3), Is.EqualTo(7));
                Assert.That(AutonomousCampCompetition.SharedLiveMobs(far, 6), Is.EqualTo(6));

                AutonomousCampCompetition.Occupancy(a);   // first count
                int before = AutonomousCampCompetition.Occupancy(b);
                AutonomousCampCompetition.NoteAssigned(a, 3);
                AutonomousCampCompetition.NoteAssigned(c, 2);
                Assert.That(AutonomousCampCompetition.Occupancy(b) - before, Is.EqualTo(5), "bots at the neighbouring camps count");
                Assert.That(AutonomousCampCompetition.Occupancy(far), Is.Zero);

                // b alone (3 monsters, 0 bots) looked quiet; with its neighbours' 5 bots on 7 monsters it is crowded
                string[] quiet = AutonomousCampCompetition.PreferQuiet([b, far], id => id, id => id == b ? 3 : 6);
                Assert.That(quiet, Is.EqualTo(new[] { far }));
            }
            finally
            {
                AutonomousCampCompetition.PublishCatalog([]);
            }
        }
    }
}
