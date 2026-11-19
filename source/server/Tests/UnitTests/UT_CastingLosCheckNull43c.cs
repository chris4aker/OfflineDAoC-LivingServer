using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Owner's PC, 2026-11-19 14:56:33 (build 1.2-43c43aa): "Critical error encountered in CastingService:
/// System.NullReferenceException at CastingComponent.StartDuringCastLosCheck ... line 201 &lt;- SpellHandler.CheckDuringCast";
/// the service then removed the gamebot Carik from the world. The component's SpellHandler had been cleared during the
/// tick. Tests only.
/// </summary>
[TestFixture]
public sealed class UT_CastingLosCheckNull43c
{
    private static CastingComponent Component() => (CastingComponent)RuntimeHelpers.GetUninitializedObject(typeof(CastingComponent));
    private static GameNPC Target() => (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));

    [Test]
    public void ADuringCastCheckWithItsHandlerClearedMidTickIsNoCheck()
    {
        CastingComponent component = Component();
        Assert.That(component.SpellHandler, Is.Null, "cleared by an interrupt or the cast ending during the tick");
        Assert.That(component.StartDuringCastLosCheck(Target()), Is.False, "43c43aa threw here");
    }

    [Test]
    public void TheAskingHandlersCheckerIsUsedAndAMissingOneIsNoCheck()
    {
        var handler = (SpellHandler)RuntimeHelpers.GetUninitializedObject(typeof(SpellHandler));
        handler.LosChecker = null;
        Assert.Multiple(() =>
        {
            Assert.That(Component().StartDuringCastLosCheck(Target(), handler), Is.False);
            Assert.That(Component().StartEndOfCastLosCheck(Target(), handler), Is.False);
            Assert.That(Component().StartEndOfCastLosCheck(Target(), null), Is.False, "no handler at all");
        });
    }
}
