using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Tactics = DOL.GS.EpicEncounterTactics;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Party posts (1.2, owner 2026-11-11: "Tactics only for now"): Spindler Broodmother's mesmerize lands 300 around one
    /// random raider, and the raid stood in one pile. Each party's back line now stands at its own post. Pure rules and the
    /// catalog row; play tests cover the walk, the floor and her fight.
    /// </summary>
    [TestFixture]
    public class UT_PartyPosts
    {
        private const int Mesmerize = 300, Spacing = 380, MeleeReach = 200;
        private static readonly Vector3 Her = new(21_283, 51_707, 10_876);

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        [Test]
        public void OnlyTheBroodmotherHasPartyPosts()
        {
            Assert.That(Tactics.For("SpindlerBroodmother").PartyPostSpacing, Is.EqualTo(Spacing), "her mesmerize's 300 and a margin");
            Assert.That(Tactics.All.Where(t => t.PartyPostSpacing > 0).Select(t => t.Boss), Is.EquivalentTo(new[] { "SpindlerBroodmother" }));
        }

        [Test]
        public void TheRingHasTwoCirclesOfPostsAllApart()
        {
            Vector3[] posts = Tactics.PostRing(Her, Spacing);
            Assert.That(posts.Count(p => Flat(p, Her) < 700), Is.EqualTo(10), "inner ring, 660 from her");
            Assert.That(posts.Count(p => Flat(p, Her) > 1_000), Is.EqualTo(17), "outer ring, 1,060 from her");
            Assert.That(posts, Has.Length.EqualTo(27), "more posts than a raid has parties");
            Assert.That(posts.Max(p => Flat(p, Her)), Is.LessThan(1_500), "inside her spell's reach and the raid's");
            for (int i = 0; i < posts.Length; i++)
                for (int j = i + 1; j < posts.Length; j++)
                    Assert.That(Flat(posts[i], posts[j]), Is.GreaterThanOrEqualTo(Spacing - 0.5f), $"posts {i} and {j}");
            Assert.That(posts.All(p => p.Z == Her.Z));
        }

        [Test]
        public void OneMesmerizeCatchesOnePostOrTheMeleeNeverBoth()
        {
            Vector3[] posts = Tactics.PostRing(Her, Spacing);
            // Around a raider at a post: no other post, and no melee raider (they stand within 200 of her).
            foreach (Vector3 hit in posts)
            {
                Assert.That(posts.Count(p => Flat(p, hit) <= Mesmerize), Is.EqualTo(1));
                Assert.That(Flat(hit, Her) - MeleeReach, Is.GreaterThan(Mesmerize));
            }
            // Around a melee raider anywhere within 200 of her: no post.
            foreach (Vector3 melee in new[] { Her, new(Her.X + MeleeReach, Her.Y, Her.Z), new(Her.X, Her.Y - MeleeReach, Her.Z) })
                Assert.That(posts.Count(p => Flat(p, melee) <= Mesmerize), Is.Zero);
        }

        [Test]
        public void PostsTheFloorMovedTogetherAreNotBothKept()
        {
            Vector3 a = new(0, 0, 0), b = new(379, 0, 0), c = new(380, 0, 0), d = new(380, 380, 0);
            Assert.That(Tactics.PickPosts(new[] { a, b, c, d }, Spacing), Is.EqualTo(new[] { a, c, d }), "in order, the first one wins");
            Assert.That(Tactics.PickPosts(null, Spacing), Is.Empty);
            Assert.That(Tactics.PostRing(Her, 0), Is.Empty, "no spacing, no posts");
        }

        [Test]
        public void APartyKeepsItsPostAndExtraPartiesShare()
        {
            Assert.That(Tactics.PostSlot(0, 12), Is.EqualTo(0));
            Assert.That(Tactics.PostSlot(11, 12), Is.EqualTo(11));
            Assert.That(Tactics.PostSlot(12, 12), Is.EqualTo(0), "a thirteenth party shares the first post");
            Assert.That(Tactics.PostSlot(5, 0), Is.EqualTo(-1), "no floor posts: nobody is sent anywhere");
        }

        [Test]
        public void TheRingMovesOnlyWhenSheHasMovedAway()
        {
            long old = Tactics.PartyPostReanchorMilliseconds;
            Vector3 moved = new(Her.X + Tactics.PartyPostReanchor + 1, Her.Y, Her.Z);
            Assert.That(Tactics.PostsNeedAnchor(null, Her, 0), Is.True, "first seen");
            Assert.That(Tactics.PostsNeedAnchor(Her, new(Her.X + Tactics.PartyPostReanchor, Her.Y, Her.Z), old), Is.False);
            Assert.That(Tactics.PostsNeedAnchor(Her, moved, old), Is.True);
            Assert.That(Tactics.PostsNeedAnchor(Her, moved, old - 1), Is.False, "she is still walking: the back line waits");
            // While the ring holds, its inner posts stay out of a mesmerize on her melee.
            Assert.That(Spacing + Tactics.PartyPostMeleeClear - Tactics.PartyPostReanchor - MeleeReach, Is.GreaterThan(Mesmerize));
        }

        [Test]
        public void ABackLineRaiderWalksToItsPostBetweenCasts()
        {
            Assert.That(Tactics.StepToPost(Tactics.PartyPostTolerance + 1, false, false), Is.EqualTo(Tactics.PostStep.Walk));
            Assert.That(Tactics.StepToPost(Tactics.PartyPostTolerance, false, false), Is.EqualTo(Tactics.PostStep.CarryOn), "at its post");
            Assert.That(Tactics.StepToPost(900, true, false), Is.EqualTo(Tactics.PostStep.CarryOn), "it finishes its cast first");
            Assert.That(Tactics.StepToPost(900, false, true), Is.EqualTo(Tactics.PostStep.CarryOn), "mesmerized or stunned");
        }
    }
}
