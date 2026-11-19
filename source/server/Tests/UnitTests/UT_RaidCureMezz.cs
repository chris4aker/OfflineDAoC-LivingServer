using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.Unit.Gameserver
{
    [TestFixture]
    public class UT_RaidCureMezz
    {
        private static RaidCureMezz.Mezzed M(long id, bool cure = false, bool healer = false, bool tank = false) => new(id, cure, healer, tank);

        [Test]
        public void NobodyMesmerizedNobodyCured() =>
            Assert.That(RaidCureMezz.Choose([], 7), Is.EqualTo(-1));

        [Test]
        public void ACurerIsCuredFirstThenAHealerThenATank()
        {
            var mezzed = new List<RaidCureMezz.Mezzed> { M(1), M(2, tank: true), M(3, healer: true), M(4, cure: true) };
            Assert.That(RaidCureMezz.Choose(mezzed, 100), Is.EqualTo(3), "the Bard who can cure in turn");
            mezzed.RemoveAt(3);
            Assert.That(RaidCureMezz.Choose(mezzed, 100), Is.EqualTo(2), "then the healer");
            mezzed.RemoveAt(2);
            Assert.That(RaidCureMezz.Choose(mezzed, 100), Is.EqualTo(1), "then the tank");
            mezzed.RemoveAt(1);
            Assert.That(RaidCureMezz.Choose(mezzed, 100), Is.EqualTo(0), "then anyone");
        }

        [Test]
        public void SixteenCurersTakeDifferentRaiders()
        {
            // Play test duo34: about 100 of 128 raiders mesmerized at once.
            List<RaidCureMezz.Mezzed> mezzed = Enumerable.Range(1, 100).Select(i => M(i)).ToList();
            int[] picks = Enumerable.Range(500, 16).Select(curer => RaidCureMezz.Choose(mezzed, curer)).ToArray();
            Assert.That(picks.Distinct().Count(), Is.EqualTo(16));
            // With fewer of the most urgent kind than curers, they share those.
            mezzed[10] = M(11, cure: true);
            mezzed[20] = M(21, cure: true);
            Assert.That(Enumerable.Range(500, 16).Select(curer => RaidCureMezz.Choose(mezzed, curer)).Distinct().OrderBy(i => i),
                Is.EqualTo(new[] { 10, 20 }));
        }

        [Test]
        public void TheRaidCureComesBeforeHealingUnlessAPartyMemberIsAboutToDie()
        {
            Assert.That(RaidCureMezz.ComesBeforeHealing(0), Is.True);
            Assert.That(RaidCureMezz.ComesBeforeHealing(1), Is.False);
        }
    }
}
