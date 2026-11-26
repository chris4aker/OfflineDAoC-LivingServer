using System;
using System.IO;
using System.Linq;
using DOL.GS.Spells;
using NUnit.Framework;
using Charm = DOL.GS.Spells.CharmSpellHandler.eCharmType;

namespace DOL.GS.Tests;

/// <summary>
/// #579: the charm spells' body types from db-public 764bda56, checked against the period sources (CharmBodyTypeRepair's
/// summary), and the one-time repair that changes a world's row only while it holds db-public's old value.
/// </summary>
[TestFixture]
public class UT_CharmBodyTypeRepair579
{
    [Test]
    public void TheTableIsDbPublicsChange()
    {
        // (name, before 764bda56, after) for the 14 rows it changed (1156 and 11561 share a name).
        var expected = new (string Name, int Old, int New)[]
        {
            ("Captivating Melodies", 0, 1), ("Enchanting Melodies", 0, 4), ("Attracting Melodies", 0, 5),
            ("Pleasurable Melodies", 0, 6), ("Enticing Melodies", 0, 6), ("Alluring Melodies", 0, 6),
            ("Fabricated Enemy", 1, 4), ("Imaginary Enemy", 1, 5), ("Phantom Enemy", 1, 6), ("Ghostly Enemy", 1, 7),
            ("Dream Enemy", 1, 0), ("Control Reptile", 9, 8), ("Superior Call(Insect)", 7, 3),
        };
        Assert.That(CharmBodyTypeRepair.Fixes.Select(f => (f.Name, f.OldValue, (int)f.NewValue)), Is.EquivalentTo(expected));
    }

    [Test]
    public void TheMinstrelAndMentalistChargesFollowThePeriodSources()
    {
        // Minstrel (camelotvault 2003): humanoids; + animals; + insects; then + magical for the last three songs.
        Assert.That(new[] { "Captivating", "Enchanting", "Attracting", "Pleasurable", "Enticing", "Alluring" }
                .Select(n => CharmBodyTypeRepair.For(n + " Melodies", "Charm").NewValue),
            Is.EqualTo(new[] { Charm.Humanoid, Charm.HumanoidAnimal, Charm.HumanoidAnimalInsect, Charm.HumanoidAnimalInsectMagical,
                Charm.HumanoidAnimalInsectMagical, Charm.HumanoidAnimalInsectMagical }));
        // Mentalist (camelotvault 2004, the Herald): spec 10 + animals, 17 + insects, 25 + magical, 33 + undead, 42 any.
        Assert.That(new[] { "Fabricated", "Imaginary", "Phantom", "Ghostly", "Dream" }
                .Select(n => CharmBodyTypeRepair.For(n + " Enemy", "Charm").NewValue),
            Is.EqualTo(new[] { Charm.HumanoidAnimal, Charm.HumanoidAnimalInsect, Charm.HumanoidAnimalInsectMagical,
                Charm.HumanoidAnimalInsectMagicalUndead, Charm.All }));
        Assert.That(CharmBodyTypeRepair.For("Illusory Enemy", "Charm"), Is.Null, "the spec 4 charm (humanoids) is not changed");
    }

    [Test]
    public void TheOldReptileValueSkippedTheBodyTypeCheck()
    {
        // CharmSpellHandler checks body types only for AmnesiaChance in (All, Reptile]: 9 charmed anything; 8 is reptiles.
        Assert.That((ushort)9, Is.GreaterThan((ushort)Charm.Reptile));
        Assert.That((int)CharmBodyTypeRepair.For("Control Reptile", "Charm").NewValue, Is.EqualTo((int)Charm.Reptile));
        foreach (CharmBodyTypeRepair.Fix fix in CharmBodyTypeRepair.Fixes)
            Assert.That(Enum.IsDefined(fix.NewValue), Is.True, fix.Name);
    }

    [Test]
    public void OnlyDbPublicsOldValueIsChanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CharmBodyTypeRepair.Target("Phantom Enemy", "Charm", 1), Is.EqualTo(6));
            Assert.That(CharmBodyTypeRepair.Target("phantom enemy ", "charm", 1), Is.EqualTo(6), "name and type in any case");
            Assert.That(CharmBodyTypeRepair.Target("Phantom Enemy", "Charm", 6), Is.Null, "already right");
            Assert.That(CharmBodyTypeRepair.Target("Phantom Enemy", "Charm", 5), Is.Null, "someone else's value: left alone");
            Assert.That(CharmBodyTypeRepair.Target("Phantom Enemy", "DirectDamage", 1), Is.Null, "not a charm");
            Assert.That(CharmBodyTypeRepair.Target("Coerce Will", "Charm", 0), Is.Null, "not in the table");
            Assert.That(CharmBodyTypeRepair.Target(null, "Charm", 0), Is.Null);
            Assert.That(CharmBodyTypeRepair.Target("Dream Enemy", "Charm", 1), Is.EqualTo(0), "Dream Enemy becomes any type");
            Assert.That(CharmBodyTypeRepair.Target("Alluring Melodies", "Charm", 0), Is.EqualTo(6));
        });
    }

    [Test]
    public void TheRepairRunsOncePerWorld()
    {
        Assert.That(CharmBodyTypeRepair.AlreadyRan(""), Is.False);
        Assert.That(CharmBodyTypeRepair.WithRepair(""), Is.EqualTo(CharmBodyTypeRepair.RepairName));
        string both = CharmBodyTypeRepair.WithRepair("hib-keep-levels-1.2.3");
        Assert.That(both, Is.EqualTo("hib-keep-levels-1.2.3," + CharmBodyTypeRepair.RepairName));
        Assert.That(CharmBodyTypeRepair.AlreadyRan(both), Is.True);
        Assert.That(CharmBodyTypeRepair.WithRepair(both), Is.EqualTo(both));
    }

    [Test]
    public void TheRepairRowNamesTheOldValue()
    {
        Assert.That(CharmBodyTypeRepair.RepairedLine(4214, "Phantom Enemy", 1, 6), Is.EqualTo(
            "CHARM_BODY_TYPE_REPAIRED spellId=4214 spell=\"Phantom Enemy\" amnesia=1->6 bodyTypes=HumanoidAnimalInsectMagical " +
            "reason=old-db-public-value"));
    }

    [Test]
    public void TheServerRunsTheRepairBeforeTheSkillsLoad()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "GameServer.cs")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        string text = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "GameServer.cs"));
        int repair = text.IndexOf("CharmBodyTypeRepair.Run()", StringComparison.Ordinal);
        int skills = text.IndexOf("SkillBase.LoadSkills();", StringComparison.Ordinal);
        Assert.That(repair, Is.GreaterThan(0));
        Assert.That(repair, Is.LessThan(skills), "the spells are cached by LoadSkills: the repair comes first");
    }
}
