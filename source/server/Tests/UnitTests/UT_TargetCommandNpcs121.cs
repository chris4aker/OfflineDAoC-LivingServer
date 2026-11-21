using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.1: "Caer Caledon's guards can't be picked with /target by players (checked as a GM only)". /target looked for an
/// NPC only for a GM, by one exact word. A player may now pick a nearby NPC it could not attack (the command's rule for
/// players), by its whole name. Tests only.
/// </summary>
[TestFixture]
public sealed class UT_TargetCommandNpcs121
{
    [Test]
    public void TheAskedNameIsEveryWordAfterTheCommand()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TargetCommandHandler.AskedName(new[] { "&target", "Guardian", "Sergeant" }), Is.EqualTo("Guardian Sergeant"));
            Assert.That(TargetCommandHandler.AskedName(new[] { "&target", "Lord" }), Is.EqualTo("Lord"));
            Assert.That(TargetCommandHandler.AskedName(new[] { "&target" }), Is.Empty);
            Assert.That(TargetCommandHandler.AskedName(null), Is.Empty);
        });
    }

    [Test]
    public void AnNpcAnswersToItsWholeNameInAnyCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TargetCommandHandler.NpcNameMatches("Guardian Sergeant", "guardian sergeant"), Is.True);
            Assert.That(TargetCommandHandler.NpcNameMatches("Guardian Sergeant", "Guardian"), Is.False, "not by a part of it");
            Assert.That(TargetCommandHandler.NpcNameMatches("Guardian", " "), Is.False);
            Assert.That(TargetCommandHandler.NpcNameMatches(null, "Guardian"), Is.False);
        });
    }

    [Test]
    public void APlayerPicksAGuardItCannotAttackAndAGmPicksAny()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TargetCommandHandler.MayTargetNpc(gm: false, alive: true, stealthed: false, cantTarget: false, attackable: false), Is.True,
                "a guard of the keep's own realm: before 1.2.1 only a GM could");
            Assert.That(TargetCommandHandler.MayTargetNpc(gm: false, alive: true, stealthed: false, cantTarget: false, attackable: true), Is.False,
                "an enemy's guard: as with players, /target does not find what you could attack");
            Assert.That(TargetCommandHandler.MayTargetNpc(gm: false, alive: true, stealthed: true, cantTarget: false, attackable: false), Is.False);
            Assert.That(TargetCommandHandler.MayTargetNpc(gm: false, alive: true, stealthed: false, cantTarget: true, attackable: false), Is.False);
            Assert.That(TargetCommandHandler.MayTargetNpc(gm: false, alive: false, stealthed: false, cantTarget: false, attackable: false), Is.False);
            Assert.That(TargetCommandHandler.MayTargetNpc(gm: true, alive: false, stealthed: true, cantTarget: true, attackable: true), Is.True, "as before");
        });
    }
}
