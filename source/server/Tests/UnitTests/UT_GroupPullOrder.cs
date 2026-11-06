using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Prima's pull order for a party's puller, from Offline DAoC True 1.65 (shadowofze).</summary>
    [TestFixture]
    public class UT_GroupPullOrder
    {
        [Test]
        public void PartiesPullTheMonsterStandingApartFirst() => Assert.Multiple(() =>
        {
            var monsters = new (System.Numerics.Vector3 Position, int AggroRange)[]
            {
                (new(0, 0, 0), 500), (new(300, 0, 0), 500), (new(0, 300, 0), 500), (new(3000, 0, 0), 500),
                (new(6000, 0, 0), 0),
            };
            int[] counts = AutonomousGroupPullOrder.NeighbourCounts(monsters);
            Assert.That(counts[0], Is.EqualTo(2), "two others within its aggro range");
            Assert.That(counts[1], Is.EqualTo(2), "(300,0)-(0,300) is 424 apart");
            Assert.That(counts[3], Is.Zero, "the lone one comes first");
            Assert.That(counts[4], Is.Zero, "no aggro range: nobody counted");
        });
    }
}
