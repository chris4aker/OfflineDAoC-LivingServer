using System;
using NUnit.Framework;
using Place = DOL.GS.AutonomousBotGroupCoordinator.FormationPlace;

namespace DOL.GS.Tests;

/// <summary>Veteran review M11 (groups G12): tanks in front toward the camp, melee on the flanks, healers and casters behind.</summary>
[TestFixture]
public sealed class UT_RoleFormation
{
    private static double Off(double angle, double front)
    {
        double d = Math.IEEERemainder(angle - front, 2 * Math.PI);
        return Math.Abs(d);
    }

    [Test]
    public void RolesAndClassesPickTheirPlace()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.PlaceFor(BotPveGroupRole.Tank, eCharacterClass.Armsman), Is.EqualTo(Place.Front));
            Assert.That(AutonomousBotGroupCoordinator.PlaceFor(BotPveGroupRole.Healer, eCharacterClass.Cleric), Is.EqualTo(Place.Back));
            Assert.That(AutonomousBotGroupCoordinator.PlaceFor(BotPveGroupRole.Attacker, eCharacterClass.Wizard), Is.EqualTo(Place.Back));
            Assert.That(AutonomousBotGroupCoordinator.PlaceFor(BotPveGroupRole.Buffer, eCharacterClass.Shaman), Is.EqualTo(Place.Back));
            Assert.That(AutonomousBotGroupCoordinator.PlaceFor(BotPveGroupRole.Attacker, eCharacterClass.Mercenary), Is.EqualTo(Place.Side));
            Assert.That(AutonomousBotGroupCoordinator.PlaceFor(BotPveGroupRole.Buffer, eCharacterClass.Skald), Is.EqualTo(Place.Side));
        });
    }

    [Test]
    public void TanksLeadAndHealersTrail()
    {
        const double front = 0.7;
        var slots = AutonomousBotGroupCoordinator.RoleSlots(
            new[] { Place.Back, Place.Front, Place.Side, Place.Front, Place.Back, Place.Side, Place.Back }, front);
        Assert.Multiple(() =>
        {
            Assert.That(Off(slots[1].Angle, front), Is.LessThanOrEqualTo(Math.PI / 6 + 1e-9));
            Assert.That(Off(slots[3].Angle, front), Is.LessThanOrEqualTo(Math.PI / 6 + 1e-9));
            Assert.That(slots[1].Angle, Is.Not.EqualTo(slots[3].Angle), "two tanks do not stack");
            Assert.That(Off(slots[2].Angle, front), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(Off(slots[5].Angle, front), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(slots[2].Angle, Is.Not.EqualTo(slots[5].Angle), "flanks alternate sides");
            foreach (int back in new[] { 0, 4, 6 })
            {
                Assert.That(slots[back].Back, Is.True);
                Assert.That(Off(slots[back].Angle, front), Is.GreaterThanOrEqualTo(Math.PI - Math.PI * 5 / 18 - 1e-9));
            }
            Assert.That(slots[1].Back, Is.False);
        });
    }

    [Test]
    public void TheBackRowStandsFartherOutdoorsAndCloseInDungeons()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.SlotRadius(true, false, 8), Is.EqualTo(AutonomousBotGroupCoordinator.BackRowDistance));
            Assert.That(AutonomousBotGroupCoordinator.BackRowDistance, Is.InRange(150, 250));
            Assert.That(AutonomousBotGroupCoordinator.SlotRadius(false, false, 8), Is.EqualTo(64 + 8 * 3), "the old ring");
            Assert.That(AutonomousBotGroupCoordinator.SlotRadius(true, true, 8), Is.LessThan(150));
            Assert.That(AutonomousBotGroupCoordinator.ArcAngles(1, 2.0, 1.0), Is.EqualTo(new[] { 2.0 }));
            Assert.That(AutonomousBotGroupCoordinator.ArcAngles(0, 2.0, 1.0), Is.Empty);
        });
    }
}
