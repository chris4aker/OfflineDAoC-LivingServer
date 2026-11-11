using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Tactics = DOL.GS.EpicEncounterTactics;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Govannon's demon (1.2, after play test duo28's damage ledger): the raid steps out of his Shroud of Agony while he
    /// casts it (owner 2026-11-11: the tactic before any change to his heal). Pure rules and the catalog row; play tests cover the rest.
    /// </summary>
    [TestFixture]
    public class UT_GovannonShroud
    {
        [Test]
        public void TheCatalogNamesTheShroud()
        {
            Tactics.Tactic tactic = Tactics.For("GrandSummonerGovannon");
            Assert.That(tactic.StepOutOfCast, Is.EqualTo("Govannon's Shroud of Agony"));
            Assert.That(tactic.SpreadRadius, Is.EqualTo(300), "the Shroud's radius around his target");
        }

        [Test]
        public void ARaiderInsideTheAreaStepsJustPastIt()
        {
            Vector3 target = new(1_000, 1_000, 50);
            // 100 east of his target: out along the same line, to 420 from the target.
            Vector3? step = Tactics.StepOutOfArea(new(1_100, 1_000, 60), target, 300);
            Assert.That(step, Is.Not.Null);
            Assert.That(step.Value.X, Is.EqualTo(1_000 + 300 + Tactics.CastStepOut).Within(0.5));
            Assert.That(step.Value.Y, Is.EqualTo(1_000).Within(0.5));
            Assert.That(step.Value.Z, Is.EqualTo(60), "the raider's own height");
            // Just outside the radius still counts (the spell lands where his target stands then).
            Assert.That(Tactics.StepOutOfArea(new(1_000 + 300 + Tactics.CastMargin, 1_000, 50), target, 300), Is.Not.Null);
            // Clear of it: stays.
            Assert.That(Tactics.StepOutOfArea(new(1_000 + 300 + Tactics.CastMargin + 1, 1_000, 50), target, 300), Is.Null);
            // On his target's spot: some way out, not nowhere.
            Assert.That(Tactics.StepOutOfArea(target, target, 300), Is.Not.Null);
        }

        [Test]
        public void NobodyStepsOutWhenNothingIsCast()
        {
            Assert.That(Tactics.IsCasting(null, "Govannon's Shroud of Agony"), Is.False);
            Assert.That(Tactics.IsCasting(null, null), Is.False);
        }

        [Test]
        public void ARaiderSentOutHoldsOutUntilTheSpellHasLanded()
        {
            // Play test duo33: past the radius a raider was told nothing, so the melee went back to their station 294 from
            // his target while he was still casting; three Shrouds caught 25, 31 and 36.
            Assert.Multiple(() =>
            {
                Assert.That(Tactics.StepDuringCast(insideArea: true, sentOutThisCast: false), Is.EqualTo(Tactics.CastStep.MoveOut));
                Assert.That(Tactics.StepDuringCast(true, true), Is.EqualTo(Tactics.CastStep.MoveOut));
                Assert.That(Tactics.StepDuringCast(false, true), Is.EqualTo(Tactics.CastStep.HoldOut));
                Assert.That(Tactics.StepDuringCast(false, false), Is.EqualTo(Tactics.CastStep.CarryOn),
                    "a caster who always stood clear carries on");
            });
        }

        [Test]
        public void RaidersOnOneSpotStepOutToDifferentSpots()
        {
            Vector3 target = new(1_000, 1_000, 50);
            // A party on its melee station, 294 from his target, and a party standing on him.
            foreach (Vector3 from in new[] { new Vector3(1_294, 1_000, 50), target })
            {
                var spots = new List<Vector3>();
                for (long id = 1; id <= 8; id++)
                {
                    Vector3 spot = Tactics.StepOutOfArea(from, target, 300, id).Value;
                    Assert.That(Vector2.Distance(new(spot.X, spot.Y), new(target.X, target.Y)),
                        Is.EqualTo(300 + Tactics.CastStepOut).Within(1), "every way out ends past the radius");
                    spots.Add(spot);
                }
                Assert.That(spots.Distinct().Count(), Is.EqualTo(8), "eight raiders, eight spots");
                float widest = spots.SelectMany(a => spots.Select(b => Vector3.Distance(a, b))).Max();
                Assert.That(widest, Is.GreaterThan(300), "one Shroud on any of them no longer reaches all of them");
            }
            // Without an id the way out is straight, as before.
            Assert.That(Tactics.StepOutOfArea(new(1_100, 1_000, 60), target, 300).Value.Y, Is.EqualTo(1_000).Within(0.01));
        }
    }
}
