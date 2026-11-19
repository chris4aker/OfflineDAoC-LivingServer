using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review M11: camp traits worked out by the background builder, and how camp choices weigh them.</summary>
[TestFixture]
public sealed class UT_AutonomousCampKnowledge
{
    private static AutonomousCampKnowledge.Member M(float x, float y, int aggro = 400, bool caster = false, byte body = 1) =>
        new(new Vector3(x, y, 0), aggro, caster, body);

    [Test]
    public void PackFactorCountsNeighboursWithinReach()
    {
        // three monsters 200 apart (each sees the other two), one alone far away
        var traits = AutonomousCampKnowledge.Measure(new[] { M(0, 0), M(200, 0), M(0, 200), M(3000, 0) }, Vector3.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(traits.PackFactor, Is.EqualTo(6f / 4f).Within(1e-4));
            Assert.That(traits.Radius, Is.EqualTo(3000));
            Assert.That(traits.Known, Is.True);
            Assert.That(traits.BodyType, Is.EqualTo(1));
        });
    }

    [Test]
    public void CasterShareAndMixedBodies()
    {
        var traits = AutonomousCampKnowledge.Measure(new[] { M(0, 0, caster: true, body: 9), M(5000, 0, caster: true, body: 7),
            M(9000, 0, body: 1) }, Vector3.Zero);
        Assert.That(traits.CasterShare, Is.EqualTo(2f / 3f).Within(1e-4));
        Assert.That(traits.PackFactor, Is.Zero);
        Assert.That(traits.BodyType, Is.EqualTo(1).Or.EqualTo(7).Or.EqualTo(9), "no majority beyond a third each: one of them");
    }

    [Test]
    public void UnknownTraitsNeverChangeTheDraw()
    {
        Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Armsman, default), Is.EqualTo(1));
        Assert.That(AutonomousCampKnowledge.GroupFactor(false, default), Is.EqualTo(1));
        Assert.That(AutonomousCampKnowledge.Measure(new AutonomousCampKnowledge.Member[0], Vector3.Zero).Known, Is.False);
    }

    [Test]
    public void DenseCampsAreForPartiesWithCrowdControl()
    {
        var dense = new AutonomousCampKnowledge.Traits(2.5f, 0, 1, 1000, 6);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.GroupFactor(false, dense), Is.EqualTo(0.5));
            Assert.That(AutonomousCampKnowledge.GroupFactor(true, dense), Is.EqualTo(1));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Warrior, dense), Is.EqualTo(0.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Wizard, dense), Is.EqualTo(0.75));
        });
    }

    [Test]
    public void ClassesPreferTheirPreyAndMeleeAvoidCasters()
    {
        var animals = new AutonomousCampKnowledge.Traits(0.5f, 0, 1, 1000, 6);
        var reptiles = new AutonomousCampKnowledge.Traits(0.5f, 0, 9, 1000, 6);
        var casters = new AutonomousCampKnowledge.Traits(0.5f, 0.8f, 6, 1000, 6);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Hunter, animals), Is.EqualTo(1.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Spiritmaster, reptiles), Is.EqualTo(1.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Spiritmaster, animals), Is.EqualTo(1));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Hero, casters), Is.EqualTo(0.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Cleric, casters), Is.EqualTo(1));
        });
    }

    [Test]
    public void SoloDungeonChanceFollowsTheClass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Necromancer), Is.EqualTo(150));
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Shadowblade), Is.EqualTo(150));
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Healer), Is.EqualTo(30));
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Armsman),
                Is.EqualTo(AutonomousBotDecisionEngine.SoloDungeonPreferencePermille));
        });
    }
    [Test]
    public void TheMainRaceIsAMajorityOnly()
    {
        var humanoids = AutonomousCampKnowledge.Measure(new[]
        {
            new AutonomousCampKnowledge.Member(Vector3.Zero, 300, false, 1, 2005),
            new AutonomousCampKnowledge.Member(new Vector3(900, 0, 0), 300, false, 1, 2005),
            new AutonomousCampKnowledge.Member(new Vector3(0, 900, 0), 300, false, 1, 2000),
        }, Vector3.Zero);
        var mixed = AutonomousCampKnowledge.Measure(new[]
        {
            new AutonomousCampKnowledge.Member(Vector3.Zero, 300, false, 1, 2005),
            new AutonomousCampKnowledge.Member(new Vector3(900, 0, 0), 300, false, 1, 2000),
        }, Vector3.Zero);
        Assert.That(humanoids.Race, Is.EqualTo(2005));
        Assert.That(mixed.Race, Is.Zero, "one of each is no majority");
    }

    [Test]
    public void WeaknessesComeFromLowResists()
    {
        // the server's Animal row: cold -15, thrust -15, slash +5
        ushort mask = AutonomousCampKnowledge.WeaknessMask(type => type switch
        {
            eDamageType.Cold => -15, eDamageType.Thrust => -15, eDamageType.Slash => 5, eDamageType.Heat => -5, _ => 0,
        });
        var animals = new AutonomousCampKnowledge.Traits(0.5f, 0, 1, 1000, 6, 2000, mask);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.IsWeakTo(animals, eDamageType.Cold), Is.True);
            Assert.That(AutonomousCampKnowledge.IsWeakTo(animals, eDamageType.Thrust), Is.True);
            Assert.That(AutonomousCampKnowledge.IsWeakTo(animals, eDamageType.Heat), Is.False, "-5 is no weakness");
            Assert.That(AutonomousCampKnowledge.IsWeakTo(animals, eDamageType.Slash), Is.False);
            Assert.That(AutonomousCampKnowledge.IsWeakTo(animals, eDamageType.Natural), Is.False);
            // a solo bot whose damage the camp is weak to draws it 1.5x; others unchanged
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Infiltrator, animals, eDamageType.Thrust), Is.EqualTo(1.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Infiltrator, animals, eDamageType.Slash), Is.EqualTo(1));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Infiltrator, animals), Is.EqualTo(1));
            Assert.That(AutonomousCampKnowledge.WeaknessMask(null), Is.Zero);
        });
    }

    [Test]
    public void CastersAreTheSpellDamageClasses()
    {
        Assert.That(AutonomousCampKnowledge.DamagesWithSpells(eCharacterClass.Eldritch), Is.True);
        Assert.That(AutonomousCampKnowledge.DamagesWithSpells(eCharacterClass.Runemaster), Is.True);
        Assert.That(AutonomousCampKnowledge.DamagesWithSpells(eCharacterClass.Thane), Is.False);
        Assert.That(AutonomousCampKnowledge.DamagesWithSpells(eCharacterClass.Cleric), Is.False);
    }
}
