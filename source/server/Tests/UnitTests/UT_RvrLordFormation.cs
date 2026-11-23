using System;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Keeps;
using NUnit.Framework;
using Role = DOL.GS.RvrLordFormation.Role;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.2 (owner: "add the rule about standing behind melee"): the lord's fight in formation. Melee and tanks fight him at
    /// melee range in front; casters and archers stand behind them on the raid's side (toward the door) inside his reach;
    /// healers behind the casters; spread so one area spell cannot catch them all. Pure.
    /// </summary>
    [TestFixture]
    public class UT_RvrLordFormation
    {
        private static readonly Vector2 Lord = new(32000f, 38000f);
        private static readonly Vector2 Door = new(32000f, 36800f); // the door lies south of him: the raid comes up from there

        [TestCase(eCharacterClass.Armsman, false, Role.Tank)]
        [TestCase(eCharacterClass.Warrior, false, Role.Tank)]
        [TestCase(eCharacterClass.Hero, false, Role.Tank)]
        [TestCase(eCharacterClass.Berserker, false, Role.Melee)]
        [TestCase(eCharacterClass.Friar, false, Role.Melee)]
        [TestCase(eCharacterClass.Paladin, false, Role.Tank)]
        [TestCase(eCharacterClass.Wizard, true, Role.Ranged)]
        [TestCase(eCharacterClass.Eldritch, true, Role.Ranged)]
        [TestCase(eCharacterClass.Scout, false, Role.Ranged)]
        [TestCase(eCharacterClass.Hunter, false, Role.Ranged)]
        [TestCase(eCharacterClass.Cleric, false, Role.Healer)]
        [TestCase(eCharacterClass.Healer, false, Role.Healer)]
        [TestCase(eCharacterClass.Druid, false, Role.Healer)]
        [TestCase(eCharacterClass.Shaman, false, Role.Healer)]
        public void EveryClassHasItsPlaceInTheLordsFight(eCharacterClass cls, bool listCaster, Role role) =>
            Assert.That(RvrLordFormation.RoleOf(cls, listCaster), Is.EqualTo(role));

        [TestCase(true)]
        [TestCase(false)]
        public void EveryBandLiesInsideTheLordsReachAndHealersStandBehindTheCasters(bool keep)
        {
            var ranged = RvrLordFormation.Band(Role.Ranged, keep);
            var healer = RvrLordFormation.Band(Role.Healer, keep);
            float reach = RvrLordReach.For(keep);
            Assert.That(ranged.Max, Is.LessThan(reach));
            Assert.That(healer.Max, Is.LessThanOrEqualTo(reach), "a healer's own hits count too");
            Assert.That(healer.Min, Is.GreaterThan(ranged.Max), "healers at the back");
            Assert.That(ranged.Min, Is.GreaterThan(150f), "behind the melee, not in their pile");
            Assert.That(RvrLordFormation.Band(Role.Melee, keep), Is.EqualTo((0f, 0f)));
            Assert.That(RvrLordFormation.Spot(Lord, Vector2.UnitX, Role.Tank, keep, 7), Is.Null, "melee fight at melee range");
        }

        [Test]
        public void TheRadiiAreUpstreams()
        {
            Assert.That(GuardLord.KeepDamageRadius, Is.EqualTo(400));
            Assert.That(GuardLord.TowerDamageRadius, Is.EqualTo(300));
        }

        [TestCase(true, 230f, 345f)]
        [TestCase(false, 160f, 245f)]
        public void TheSpotsLieOnTheRaidsSideInsideTheReachAndSpread(bool keep, float min, float max)
        {
            Vector2 approach = RvrLordFormation.Approach(Lord, Door, new(0, 0));
            Assert.That(approach.Y, Is.EqualTo(-1f).Within(1e-4), "toward the door");
            float half = keep ? RvrLordFormation.KeepArcHalfDegrees : RvrLordFormation.TowerArcHalfDegrees;
            foreach (Role role in new[] { Role.Ranged, Role.Healer })
            {
                var spots = Enumerable.Range(0, RvrLordFormation.Slots)
                    .Select(id => RvrLordFormation.Spot(Lord, approach, role, keep, id)!.Value).ToArray();
                foreach (var (spot, distance) in spots)
                {
                    Assert.That(Vector2.Distance(spot, Lord), Is.EqualTo(distance).Within(0.5f));
                    Assert.That(distance, Is.InRange(min, max));
                    Assert.That(distance, Is.LessThanOrEqualTo(RvrLordReach.For(keep)));
                    Vector2 dir = Vector2.Normalize(spot - Lord);
                    double off = Math.Acos(Math.Clamp(Vector2.Dot(dir, approach), -1f, 1f)) * 180 / Math.PI;
                    Assert.That(off, Is.LessThanOrEqualTo(half + 0.5), "on the raid's side, within the arc");
                    Assert.That(spot.Y, Is.LessThan(Lord.Y), "between the lord and the door");
                }
                float closest = spots.SelectMany((a, i) => spots.Skip(i + 1).Select(b => Vector2.Distance(a.Spot, b.Spot))).Min();
                Assert.That(closest, Is.GreaterThan(keep ? 70f : 40f), "spread: no two bots of a role on one spot");
            }
        }

        [Test]
        public void AKeepCasterStandsAbout250FromTheLordAndATowerCasterAbout170()
        {
            Vector2 approach = Vector2.UnitX;
            Assert.That(RvrLordFormation.Spot(Lord, approach, Role.Ranged, true, 2)!.Value.Distance, Is.EqualTo(247.5f).Within(0.1f));
            Assert.That(RvrLordFormation.Spot(Lord, approach, Role.Ranged, false, 2)!.Value.Distance, Is.EqualTo(171.25f).Within(0.1f));
            // the middle column of the near row stands straight on the approach
            Vector2 spot = RvrLordFormation.Spot(Lord, approach, Role.Ranged, true, 2)!.Value.Spot;
            Assert.That(spot.Y, Is.EqualTo(Lord.Y).Within(0.01f));
            Assert.That(spot.X, Is.GreaterThan(Lord.X));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheCandidatesStartAtTheSpotAndStayInTheBand(bool keep)
        {
            Vector2 approach = RvrLordFormation.Approach(Lord, Door, Lord);
            for (long id = 0; id < RvrLordFormation.Slots; id++)
            {
                var candidates = RvrLordFormation.Candidates(Lord, approach, Role.Ranged, keep, id);
                Assert.That(candidates, Is.Not.Empty);
                Assert.That(candidates[0], Is.EqualTo(RvrLordFormation.Spot(Lord, approach, Role.Ranged, keep, id)!.Value));
                var band = RvrLordFormation.Band(Role.Ranged, keep);
                Assert.That(candidates.All(c => c.Distance >= band.Min - 0.01f && c.Distance <= band.Max + 0.01f), Is.True);
                Assert.That(candidates.Any(c => Vector2.Dot(Vector2.Normalize(c.Spot - Lord), approach) > 0.999f), Is.True,
                    "the centre line is tried for a narrow room");
            }
            Assert.That(RvrLordFormation.Candidates(Lord, approach, Role.Melee, keep, 1), Is.Empty);
        }

        [Test]
        public void TheApproachFallsBackToTheBotsSide()
        {
            Assert.That(RvrLordFormation.Approach(Lord, null, Lord + new Vector2(0, 500)), Is.EqualTo(Vector2.UnitY));
            Assert.That(RvrLordFormation.Approach(Lord, Lord, Lord), Is.EqualTo(Vector2.UnitX));
        }

        [TestCase(true, Role.Ranged, 350)]
        [TestCase(false, Role.Healer, 250)]
        [TestCase(true, Role.Melee, 280)]
        [TestCase(false, Role.Tank, 280)]
        public void CastersCloseOnlyIntoTheReachMeleeTo280(bool keep, Role role, int hold) =>
            Assert.That(RvrLordFormation.HoldRadius(role, keep), Is.EqualTo((float)hold));

        [TestCase(200f, false, false, true)]
        [TestCase(40f, false, false, false)]  // at its spot
        [TestCase(200f, true, false, false)]  // mid-cast: finish it
        [TestCase(200f, false, true, false)]  // stunned or mesmerized
        public void ABotWalksToItsSpotOnlyWhenOffItAndFree(float off, bool casting, bool cannotMove, bool walks) =>
            Assert.That(RvrLordFormation.WalksToSpot(off, casting, cannotMove), Is.EqualTo(walks));

        [TestCase(false, true, 0f, true)]
        [TestCase(true, false, 0f, true)]
        [TestCase(true, true, 50f, false)]
        [TestCase(true, true, 150f, true)]
        public void TheSpotIsPickedAnewForANewLordOrWhenHeMoves(bool has, bool same, float moved, bool needs) =>
            Assert.That(RvrLordFormation.NeedsSpot(has, same, moved), Is.EqualTo(needs));

        [TestCase(true, 1, true, true)]   // a lone tank on a lord taunts
        [TestCase(true, 1, false, false)] // ...not on a monster alone (as before)
        [TestCase(true, 4, false, true)]
        [TestCase(false, 1, true, false)]
        public void ATankHoldsTheLordsAttention(bool tank, int group, bool lord, bool taunts) =>
            Assert.That(RvrLordFormation.TankTaunts(tank, group, lord), Is.EqualTo(taunts));
    }
}
