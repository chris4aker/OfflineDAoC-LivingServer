using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// M11 / M6 (completeness review 2026-11-11): each camp gets a pull spot and a safe spot worked out ahead by the camp
    /// builder, and a losing solo bot or party runs to its realm's bindstone close by, else that safe spot, else back
    /// along its approach.
    /// </summary>
    [TestFixture]
    public class UT_CampSafeSpots
    {
        [Test]
        public void TheSafeSpotIsOnTheSideWithoutMonsters()
        {
            var anchor = new Vector3(10_000, 10_000, 500);
            // a pack east of the anchor, an aggressive camp north-east: west (direction 4) is clear
            var monsters = new List<Vector3>();
            for (int i = 0; i < 6; i++)
                monsters.Add(new Vector3(11_300 + i * 50, 10_000 + i * 40, 500));
            monsters.Add(new Vector3(11_000, 11_000, 500));
            AutonomousCampKnowledge.Spots(anchor, 400, monsters, out Vector3 pull, out Vector3 safe);
            Assert.Multiple(() =>
            {
                Assert.That(safe.X, Is.LessThan(anchor.X + 1), "never towards the pack");
                Assert.That(Vector2.Distance(new(safe.X, safe.Y), new(anchor.X, anchor.Y)),
                    Is.EqualTo(400 + AutonomousCampKnowledge.SafeSpotMargin).Within(1), "past the camp's edge by the margin");
                Assert.That(Vector2.Distance(new(pull.X, pull.Y), new(anchor.X, anchor.Y)), Is.EqualTo(400).Within(1), "the camp's edge");
                Assert.That(safe.Z, Is.EqualTo(anchor.Z));
                foreach (Vector3 m in monsters)
                    Assert.That(Vector2.Distance(new(m.X, m.Y), new(safe.X, safe.Y)), Is.GreaterThan(AutonomousCampKnowledge.SafeSpotClearance));
            });
        }

        [Test]
        public void WithNoMonstersTheFirstSideIsTakenAndTheTraitsCarryIt()
        {
            var anchor = new Vector3(0, 0, 0);
            AutonomousCampKnowledge.Traits traits = AutonomousCampKnowledge.WithSpots(new AutonomousCampKnowledge.Traits(1, 0, 0, 0, 3),
                anchor, new List<Vector3>());
            Assert.Multiple(() =>
            {
                Assert.That(traits.HasSpots, Is.True);
                Assert.That(traits.Pull.X, Is.EqualTo(AutonomousCampKnowledge.MinimumSocialRange).Within(0.01), "a tiny camp's edge is the social range");
                Assert.That(traits.Safe.X, Is.EqualTo(AutonomousCampKnowledge.MinimumSocialRange + AutonomousCampKnowledge.SafeSpotMargin).Within(0.01));
                Assert.That(default(AutonomousCampKnowledge.Traits).HasSpots, Is.False, "unknown camps have none");
            });
        }

        [Test]
        public void ALosingBotRunsToTheBindstoneThenTheCampSafeSpotThenBack()
        {
            var here = new Vector3(0, 0, 0);
            var monster = new Vector3(200, 0, 0);
            var fallback = new Vector3(-2_500, 0, 0);
            var bindWest = new Vector3(-3_000, 500, 0);
            var safeSouth = new Vector3(0, -1_500, 0);
            Assert.Multiple(() =>
            {
                Assert.That(BotGroupRetreat.SafePoint(here, bindWest, safeSouth, monster, fallback), Is.EqualTo(bindWest), "guards close by");
                Assert.That(BotGroupRetreat.SafePoint(here, new Vector3(-5_000, 0, 0), safeSouth, monster, fallback), Is.EqualTo(safeSouth),
                    "the bindstone is too far: the camp's safe spot");
                Assert.That(BotGroupRetreat.SafePoint(here, new Vector3(1_000, 0, 0), null, monster, fallback), Is.EqualTo(fallback),
                    "never through the monster to a bindstone");
                Assert.That(BotGroupRetreat.SafePoint(here, null, new Vector3(400, 0, 0), monster, fallback), Is.EqualTo(fallback),
                    "a safe spot behind the monster is not taken");
                Assert.That(BotGroupRetreat.SafePoint(here, null, null, monster, fallback), Is.EqualTo(fallback), "no knowledge: as before");
            });
        }
    }
}
