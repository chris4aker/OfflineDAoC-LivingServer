using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2 (planned as 1.2.1), movement that doesn't look scripted: when a melee gamebot circles, where it
    /// steps, the throttles, who is left out, the caster's step, the stand-off and the turns that don't snap.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotCombatMovement
    {
        [Test]
        public void OnlyAnAutonomousGamebotOutsideRaidsAndPlayerGroupsMovesAgainstAMonster() => Assert.Multiple(() =>
        {
            Assert.That(BotCombatMovement.Allowed(true, false, false, true), Is.True, "a gamebot fighting a monster");
            Assert.That(BotCombatMovement.Allowed(false, false, false, true), Is.False, "a companion keeps its behaviour");
            Assert.That(BotCombatMovement.Allowed(true, true, false, true), Is.False, "a player-led group keeps its behaviour");
            Assert.That(BotCombatMovement.Allowed(true, false, true, true), Is.False, "raid tactics and stations stay in charge");
            Assert.That(BotCombatMovement.Allowed(true, false, false, false), Is.False, "not against a player or a bot");
        });

        [Test]
        public void ATankHoldingAggroStaysPutASoloBotStepsRarelyTheRestCircle() => Assert.Multiple(() =>
        {
            Assert.That(BotCombatMovement.Kind(true, true, false, false, holdsAggro: true, tank: true),
                Is.EqualTo(BotCombatMovement.CircleKind.None), "a tank holding aggro stays put and faces the monster");
            Assert.That(BotCombatMovement.Kind(true, true, false, false, holdsAggro: true, tank: false),
                Is.EqualTo(BotCombatMovement.CircleKind.Small), "a solo bot the monster hits takes a small step now and then");
            Assert.That(BotCombatMovement.Kind(true, true, false, false, holdsAggro: false, tank: true),
                Is.EqualTo(BotCombatMovement.CircleKind.Normal), "a tank that lost aggro circles like anyone");
            Assert.That(BotCombatMovement.Kind(true, true, false, false, false, false), Is.EqualTo(BotCombatMovement.CircleKind.Normal));
            Assert.That(BotCombatMovement.Kind(false, true, false, false, false, false), Is.EqualTo(BotCombatMovement.CircleKind.None));
            Assert.That(BotCombatMovement.Kind(true, false, false, false, false, false), Is.EqualTo(BotCombatMovement.CircleKind.None),
                "a bow in hand");
            Assert.That(BotCombatMovement.Kind(true, true, true, false, false, false), Is.EqualTo(BotCombatMovement.CircleKind.None),
                "a caster steps between casts instead");
            Assert.That(BotCombatMovement.Kind(true, true, false, true, false, false), Is.EqualTo(BotCombatMovement.CircleKind.None),
                "a stealthed bot");
        });

        [Test]
        public void CirclingIsThrottledInSecondsPerBot() => Assert.Multiple(() =>
        {
            Assert.That(BotCombatMovement.NextCircleDelay(BotCombatMovement.CircleKind.Normal, 0), Is.EqualTo(7_000));
            Assert.That(BotCombatMovement.NextCircleDelay(BotCombatMovement.CircleKind.Normal, 1), Is.LessThan(15_000));
            Assert.That(BotCombatMovement.NextCircleDelay(BotCombatMovement.CircleKind.Small, 0), Is.EqualTo(21_000));
            Assert.That(BotCombatMovement.NextCircleDelay(BotCombatMovement.CircleKind.Small, 1), Is.LessThan(45_000));

            Assert.That(BotCombatMovement.CircleDue(10_000, 0, 0, false, 0), Is.False, "not settled in melee");
            Assert.That(BotCombatMovement.CircleDue(10_000, 9_000, 0, true, 0), Is.False, "it swings first (2.5 s)");
            Assert.That(BotCombatMovement.CircleDue(12_000, 9_000, 11_000, false, 0), Is.True, "its timer is up");
            Assert.That(BotCombatMovement.CircleDue(12_000, 9_000, 20_000, false, 0), Is.False, "its timer is not up");
            Assert.That(BotCombatMovement.CircleDue(12_000, 9_000, 20_000, true, 9_000), Is.True,
                "standing in someone: sooner");
            Assert.That(BotCombatMovement.CircleDue(12_000, 9_000, 20_000, true, 11_000), Is.False,
                "but at most every 2.5 s");
        });

        [Test]
        public void AnArcIsShortAndNeverMoreThan45Degrees() => Assert.Multiple(() =>
        {
            float normal = BotCombatMovement.SweepDegrees(BotCombatMovement.CircleKind.Normal, 75, 0);
            Assert.That(normal * MathF.PI / 180 * 75, Is.EqualTo(BotCombatMovement.ArcMinimum).Within(0.01));
            Assert.That(BotCombatMovement.SweepDegrees(BotCombatMovement.CircleKind.Normal, 40, 0.99), Is.EqualTo(45),
                "close in, the arc is cut at 45 degrees");
            float held = BotCombatMovement.SweepDegrees(BotCombatMovement.CircleKind.Small, 75, 0.99);
            Assert.That(held * MathF.PI / 180 * 75, Is.LessThanOrEqualTo(BotCombatMovement.HeldArcMaximum + 0.01));
            Assert.That(BotCombatMovement.SweepDegrees(BotCombatMovement.CircleKind.None, 75, 0.5), Is.Zero);
        });

        [Test]
        public void ItStepsAwayFromSomeoneItStandsInAndOutOfTheMonstersFace() => Assert.Multiple(() =>
        {
            Assert.That(BotCombatMovement.ChooseDirection(120, 30, crowdSide: 1, false, null, 0.1), Is.EqualTo(-1));
            Assert.That(BotCombatMovement.ChooseDirection(120, 30, crowdSide: -1, false, null, 0.9), Is.EqualTo(1));
            Assert.That(BotCombatMovement.ChooseDirection(10, 30, 0, false, null, 0.9), Is.EqualTo(1),
                "in front, to the nearer side");
            Assert.That(BotCombatMovement.ChooseDirection(350, 30, 0, false, null, 0.1), Is.EqualTo(-1));
            Assert.That(BotCombatMovement.ChooseDirection(60, 30, 0, false, null, 0.9), Is.EqualTo(1),
                "never into the monster's face: the other way");
            Assert.That(BotCombatMovement.ChooseDirection(60, 30, 0, true, null, 0.9), Is.EqualTo(-1),
                "the monster's target is in its face anyway");
            Assert.That(BotCombatMovement.ChooseDirection(120, 0, 0, false, null, 0.5), Is.Zero, "no arc, no step");
        });

        [Test]
        public void CirclingKeepsABotInTheWindowOfItsBackOrSideStyle() => Assert.Multiple(() =>
        {
            // The flank step put it straight behind (180): it stays within 150-210 for its back style.
            Assert.That(BotCombatMovement.ChooseDirection(180, 25, 0, false, Style.eOpeningPosition.Back, 0.1), Is.Not.Zero);
            Assert.That(BotCombatMovement.ChooseDirection(195, 25, 0, false, Style.eOpeningPosition.Back, 0.1), Is.EqualTo(-1),
                "toward the middle of the window");
            Assert.That(BotCombatMovement.ChooseDirection(180, 40, 0, false, Style.eOpeningPosition.Back, 0.1), Is.Zero,
                "a step out of the window either way is not taken");
            // Not in its window yet (the flank step is still to come): no window to keep.
            Assert.That(BotCombatMovement.ChooseDirection(100, 25, 0, false, Style.eOpeningPosition.Back, 0.1), Is.EqualTo(1));
            // On a side: it stays on that side.
            Assert.That(BotCombatMovement.ChooseDirection(140, 25, 0, false, Style.eOpeningPosition.Side, 0.9), Is.EqualTo(-1));
        });

        [Test]
        public void TheCrowdSideIsWhereTheNearestFighterStandsSeenFromTheMonster()
        {
            var monster = new Vector2(0, 0);
            var self = new Vector2(0, 75);  // heading 0 from the monster
            Assert.Multiple(() =>
            {
                Assert.That(BotCombatMovement.CrowdSide(monster, self, []), Is.Zero);
                Assert.That(BotCombatMovement.CrowdSide(monster, self, [new(0, 160)]), Is.Zero, "too far to stand in");
                // Heading grows clockwise from +Y toward -X (Point2D: x - sin, y + cos).
                Assert.That(BotCombatMovement.CrowdSide(monster, self, [new(-15, 72)]), Is.EqualTo(1));
                Assert.That(BotCombatMovement.CrowdSide(monster, self, [new(15, 72)]), Is.EqualTo(-1));
                Assert.That(BotCombatMovement.CrowdSide(monster, self, [new(15, 72), new(-5, 75)]), Is.EqualTo(1),
                    "the nearest one counts");
            });
        }

        private static GameNPC Monster(int x, int y, ushort heading)
        {
            var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            ((GameLiving)npc).movementComponent = npc.movementComponent = new NpcMovementComponent(npc);
            npc.X = x; npc.Y = y;
            npc.movementComponent.ForceUpdatePosition();
            npc.Heading = heading;
            return npc;
        }

        [TestCase((ushort)0, 1)] [TestCase((ushort)1000, -1)] [TestCase((ushort)4000, 1)] [TestCase((ushort)2500, -1)]
        public void TheArcGoesAroundTheMonsterInShortLegsAtTheSameDistance(ushort bearing, int direction)
        {
            GameNPC monster = Monster(10_000, 10_000, 300);
            var center = new Vector2(10_000, 10_000);
            Vector2 start = BotCombatMovement.PointFromHeading(center, bearing, 75);
            Point2D engine = monster.GetPointFromHeading(bearing, 75);
            Assert.That(Vector2.Distance(start, new(engine.X, engine.Y)), Is.LessThan(1.5f), "the engine's geometry");
            var rounded = new Point2D((int)start.X, (int)start.Y);
            Assert.That((int)BotCombatMovement.Heading(center, new Vector2(rounded.X, rounded.Y)),
                Is.EqualTo((int)monster.GetHeading(rounded)).Within(1), "the engine's headings");

            Vector2[] legs = BotCombatMovement.ArcLegs(center, 75, bearing, direction, 40);
            Assert.That(legs.Length, Is.InRange(1, BotCombatMovement.MaxLegs));
            Vector2 from = start;
            float startAngle = monster.GetAngle(new Point2D((int)start.X, (int)start.Y));
            foreach (Vector2 leg in legs)
            {
                Assert.That(Vector2.Distance(from, leg), Is.LessThanOrEqualTo(BotCombatMovement.LegLength + 0.01f),
                    "each leg is short enough that the mover doesn't turn the bot sideways");
                Assert.That(Vector2.Distance(center, leg), Is.EqualTo(75).Within(0.01), "at the distance it stood");
                from = leg;
            }
            float endAngle = monster.GetAngle(new Point2D((int)from.X, (int)from.Y));
            float turned = BotCombatMovement.Normalize(endAngle - startAngle);
            float signed = turned > 180 ? turned - 360 : turned;
            Assert.That(Math.Sign(signed), Is.EqualTo(direction), "the direction GetAngle measures");
            Assert.That(Math.Abs(signed), Is.InRange(25f, 41f));
        }

        [Test]
        public void NoArcWithoutADirection() =>
            Assert.That(BotCombatMovement.ArcLegs(Vector2.Zero, 75, 0, 0, 30), Is.Empty);

        [Test]
        public void ALegIsTakenOnlyOnDryGroundOnTheBotsOwnFloor() => Assert.Multiple(() =>
        {
            var spot = new Vector3(1000, 1000, 500);
            Assert.That(BotCombatMovement.LegReached(spot, null, 500), Is.False, "no mesh (or water, a wall): don't move");
            Assert.That(BotCombatMovement.LegReached(spot, new Vector3(1003, 1002, 510), 500), Is.True);
            Assert.That(BotCombatMovement.LegReached(spot, new Vector3(1010, 1000, 500), 500), Is.False, "a wall or ledge stopped it");
            Assert.That(BotCombatMovement.LegReached(spot, new Vector3(1000, 1000, 540), 500), Is.False, "a step up or another floor");
            Assert.That(BotCombatMovement.LegReached(spot, new Vector3(1000, 1000, 460), 500), Is.False, "a drop");
        });

        [Test]
        public void ACasterStepsRightAfterACastNeverDuringOne() => Assert.Multiple(() =>
        {
            Assert.That(BotCombatMovement.CasterStepAllowed(true, true, false, false, false, false), Is.True);
            Assert.That(BotCombatMovement.CasterStepAllowed(true, true, casting: true, false, false, false), Is.False);
            Assert.That(BotCombatMovement.CasterStepAllowed(true, true, false, castQueued: true, false, false), Is.False);
            Assert.That(BotCombatMovement.CasterStepAllowed(true, true, false, false, holdsAggro: true, false), Is.False);
            Assert.That(BotCombatMovement.CasterStepAllowed(true, true, false, false, false, meleePressure: true), Is.False);
            Assert.That(BotCombatMovement.CasterStepAllowed(true, rangedCaster: false, false, false, false, false), Is.False);
            Assert.That(BotCombatMovement.CasterStepAllowed(allowed: false, true, false, false, false, false), Is.False);

            Assert.That(BotCombatMovement.CasterStepDue(10_000, 0, 0), Is.False, "no cast yet");
            Assert.That(BotCombatMovement.CasterStepDue(10_000, 9_000, 0), Is.True, "just after a cast");
            Assert.That(BotCombatMovement.CasterStepDue(10_000, 8_000, 0), Is.False, "too long after it: the next cast comes");
            Assert.That(BotCombatMovement.CasterStepDue(10_000, 9_500, 10_001), Is.False, "its timer is not up");
            Assert.That(BotCombatMovement.NextCasterStepDelay(0), Is.EqualTo(12_000));
            Assert.That(BotCombatMovement.NextCasterStepDelay(1), Is.LessThan(24_000));
        });

        [Test]
        public void ACastersStepGoesToTheSideAndALittleBack()
        {
            var self = new Vector2(0, 0);
            var target = new Vector2(0, 1000);
            Vector2 left = BotCombatMovement.CasterStepSpot(self, target, 0.1);
            Vector2 right = BotCombatMovement.CasterStepSpot(self, target, 0.9);
            Assert.Multiple(() =>
            {
                Assert.That(Vector2.Distance(self, left), Is.LessThanOrEqualTo(BotCombatMovement.LegLength));
                Assert.That(Math.Sign(left.X), Is.Not.EqualTo(Math.Sign(right.X)), "either side");
                Assert.That(left.Y, Is.LessThan(0), "a little back from the target");
                Assert.That(Vector2.Distance(left, target), Is.GreaterThan(Vector2.Distance(self, target)));
            });
        }

        [Test]
        public void EachBotStopsItsApproachAFewUnitsShortTheSameEveryFight()
        {
            int[] standOffs = Enumerable.Range(1, 200).Select(id => BotCombatMovement.StandOff(75, id)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(standOffs.All(s => s is >= 55 and <= 75), Is.True, "never past the stick distance");
                Assert.That(standOffs.Distinct().Count(), Is.GreaterThan(10), "not one ring");
                Assert.That(BotCombatMovement.StandOff(75, 12345), Is.EqualTo(BotCombatMovement.StandOff(75, 12345)));
                Assert.That(BotCombatMovement.StandOff(30, 7), Is.EqualTo(30), "a short stick distance is kept");
                Assert.That(BotCombatMovement.StandOff(50, 99), Is.GreaterThanOrEqualTo(40));
            });
        }

        [Test]
        public void AFollowerReFacesOnlyWhenMoreThan30DegreesOff() => Assert.Multiple(() =>
        {
            Assert.That(BotTurning.ShouldFace(0, 0), Is.False);
            Assert.That(BotTurning.ShouldFace(0, 341), Is.False, "30 degrees");
            Assert.That(BotTurning.ShouldFace(0, 342), Is.True);
            Assert.That(BotTurning.ShouldFace(4000, 200), Is.False, "across north");
            Assert.That(BotTurning.ShouldFace(4000, 300), Is.True);
            Assert.That(BotTurning.Difference(4000, 100), Is.EqualTo(196));
            Assert.That(BotTurning.Difference(100, 4000), Is.EqualTo(-196));
            Assert.That(BotTurning.Difference(0, 2048), Is.EqualTo(2048));
        });

        [TestCase((ushort)0, (ushort)600)] [TestCase((ushort)0, (ushort)1700)] [TestCase((ushort)3900, (ushort)1200)]
        [TestCase((ushort)1200, (ushort)3900)] [TestCase((ushort)100, (ushort)2148)]
        public void AWideStandingTurnIsSentInStepsOfAtMost45Degrees(ushort from, ushort to)
        {
            ushort[] steps = BotTurning.Steps(from, to);
            Assert.That(steps[^1], Is.EqualTo(to), "it ends where it was turning to");
            int difference = Math.Abs(BotTurning.Difference(from, to));
            if (difference <= BotTurning.GradualAbove)
            {
                Assert.That(steps, Has.Length.EqualTo(1), "a small turn is one update");
                return;
            }
            ushort previous = from;
            int direction = Math.Sign(BotTurning.Difference(from, to));
            foreach (ushort step in steps)
            {
                int turn = BotTurning.Difference(previous, step);
                Assert.That(Math.Abs(turn), Is.LessThanOrEqualTo(BotTurning.MaxStep));
                Assert.That(Math.Sign(turn), Is.EqualTo(direction), "always the same way round, the short way");
                previous = step;
            }
            Assert.That(steps.Length * BotTurning.StepMilliseconds, Is.LessThanOrEqualTo(600), "a few hundred ms");
        }
    }
}
