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

    // ---- Castle Sauvage -> Caer Benowyc: "No connected exterior route" after RVR_KEEP_ROUTE_RESTART -------------------

    [Test]
    public void KeepRouteRestart_InTheHomeFrontier_StartsFromTheBorderKeepMuster()
    {
        var sauvage = new System.Numerics.Vector3(585085, 477504, 2600);
        Assert.Multiple(() =>
        {
            // The home porter's drop (584285,477200), where the 1.2 restart put Kanira3 and Krudrum.
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Albion, 1, new(584285, 477200, 2600), 2, out var fromDrop), Is.True);
            Assert.That(fromDrop, Is.EqualTo(sauvage), "the muster centre the army marched from, not the drop again");
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Albion, 1, new(587500, 479000, 2700), 2, out var near), Is.True);
            Assert.That(near, Is.EqualTo(sauvage));
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Albion, 1, sauvage + new System.Numerics.Vector3(80, 0, 0), 4, out _),
                Is.False, "a bot on the muster centre already starts from it");
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Midgard, 100, new(767000, 670500, 5736), 2, out var svasud), Is.True);
            Assert.That(svasud, Is.EqualTo(new System.Numerics.Vector3(766235, 669173, 5736)));
            // Abroad the frontier porter's drop is the muster centre, unchanged (UT_RvrRouteTraps43c).
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Albion, 200, new(476630, 343539, 4083), 2, out var emain), Is.True);
            Assert.That(emain, Is.EqualTo(new System.Numerics.Vector3(475835, 343661, 4080)));
        });
    }
}
