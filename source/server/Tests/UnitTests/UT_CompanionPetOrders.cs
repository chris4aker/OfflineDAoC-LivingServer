using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Owner 2026-11-12: a healing Druid companion's pet ran at a target and came back to her, over and over.</summary>
[TestFixture]
public class UT_CompanionPetOrders
{
    [Test]
    public void AHealingDruidsPetIsNotCalledOffFromTheTargetSheSentItAt() => Assert.Multiple(() =>
    {
        // Attack my target, or a Nature Druid on Automatic: the brain does not hold the pet, and the Druid sends it
        Assert.That(CompanionControls165.SupportCallsPetOff(false, true), Is.False);
        // any other support class that sits a fight out still calls its pet off
        Assert.That(CompanionControls165.SupportCallsPetOff(false, false), Is.True);
        // a held pet (Guard me, Take adds, a defence-only class) was never called off here
        Assert.That(CompanionControls165.SupportCallsPetOff(true, true), Is.False);
        Assert.That(CompanionControls165.SupportCallsPetOff(true, false), Is.False);
    });

    [Test]
    public void TakeAddsKeepsTheAddThePetTook() => Assert.Multiple(() =>
    {
        // the add turned on the pet: it is no longer "on a groupmate", and it stays the pet's job
        Assert.That(CompanionControls165.TakeAddsChoice("held", true, null, null), Is.EqualTo("held"));
        Assert.That(CompanionControls165.TakeAddsChoice("held", true, "new add", "on pet"), Is.EqualTo("held"));
        // dead or out of reach: the next add on the group, else whatever is hitting the pet, else nothing
        Assert.That(CompanionControls165.TakeAddsChoice("held", false, "new add", "on pet"), Is.EqualTo("new add"));
        Assert.That(CompanionControls165.TakeAddsChoice("held", false, null, "on pet"), Is.EqualTo("on pet"));
        Assert.That(CompanionControls165.TakeAddsChoice<string>(null, true, null, null), Is.Null);
        Assert.That(CompanionControls165.TakeAddsChoice<string>(null, false, null, null), Is.Null);
    });
}
