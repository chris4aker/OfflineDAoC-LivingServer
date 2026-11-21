using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// RvR route and keep problems on the owner's PC, 2026-11-19, build 1.2-aa537ce (16:17-16:53), each case at its logged
/// position. Tests only: none of these fixes has been seen on the owner's PC yet.
/// </summary>
[TestFixture]
public class UT_RvrRoutesAa537
{
    // ---- GM /jump to a bot inside an enemy keep ---------------------------------------------------------------------

    [Test]
    public void UnsafeKeepMove_GmAndAdminStay_PlayersMoveAsBefore()
    {
        Assert.That(PlayerInitRequestHandler.MovesOffUnsafeKeep(ePrivLevel.GM, enemyKeep: true, keepInCombat: true), Is.False);
        Assert.That(PlayerInitRequestHandler.MovesOffUnsafeKeep(ePrivLevel.Admin, enemyKeep: true, keepInCombat: false), Is.False);
        Assert.That(PlayerInitRequestHandler.MovesOffUnsafeKeep(ePrivLevel.Player, enemyKeep: true, keepInCombat: false), Is.True);
        Assert.That(PlayerInitRequestHandler.MovesOffUnsafeKeep(ePrivLevel.Player, enemyKeep: false, keepInCombat: true), Is.True);
        Assert.That(PlayerInitRequestHandler.MovesOffUnsafeKeep(ePrivLevel.Player, enemyKeep: false, keepInCombat: false), Is.False);
    }

    // ---- Midgard's defence of Dun nGed marched with 0 bots (16:38:53) ------------------------------------------------

    [Test]
    public void DefenseMusterDeadline_NoForceAssigned_StandsDown()
    {
        Assert.That(AutonomousRvrEventLayer.DefenseMusterDeadline(0, 0, 64, playerDefense: false, extended: false),
            Is.EqualTo(AutonomousRvrEventLayer.DefenseMusterDeadlineOutcome.StandDown));
        Assert.That(AutonomousRvrEventLayer.DefenseMusterDeadline(0, 3, 64, playerDefense: true, extended: true),
            Is.EqualTo(AutonomousRvrEventLayer.DefenseMusterDeadlineOutcome.StandDown), "stale muster entries are no defenders");
    }

    [Test]
    public void DefenseMusterDeadline_WithDefenders_KeepsTheOldRule()
    {
        Assert.That(AutonomousRvrEventLayer.DefenseMusterDeadline(16, 10, 64, playerDefense: false, extended: false),
            Is.EqualTo(AutonomousRvrEventLayer.DefenseMusterDeadlineOutcome.Extend));
        Assert.That(AutonomousRvrEventLayer.DefenseMusterDeadline(16, 10, 64, playerDefense: false, extended: true),
            Is.EqualTo(AutonomousRvrEventLayer.DefenseMusterDeadlineOutcome.March));
        Assert.That(AutonomousRvrEventLayer.DefenseMusterDeadline(16, 10, 64, playerDefense: true, extended: false),
            Is.EqualTo(AutonomousRvrEventLayer.DefenseMusterDeadlineOutcome.March));
        Assert.That(AutonomousRvrEventLayer.DefenseMusterDeadline(40, 32, 64, playerDefense: false, extended: false),
            Is.EqualTo(AutonomousRvrEventLayer.DefenseMusterDeadlineOutcome.March));
    }
}
