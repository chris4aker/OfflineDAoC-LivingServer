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
}
