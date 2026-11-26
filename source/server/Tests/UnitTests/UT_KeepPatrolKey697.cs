using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #697: FillPositions checked a key (TemplateID + component id) that a patrol is never kept under (its TemplateID), so the
/// second FillPositions of a keep level change made another Patrol, whose Patrols.Add threw and stopped the level change.
/// </summary>
[TestFixture]
public class UT_KeepPatrolKey697
{
    private static (GameKeep Keep, GameKeepComponent Component) KeepWithComponent()
    {
        var keep = (GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(GameKeep));
        keep.Patrols = new Dictionary<string, Patrol>();
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        return (keep, component);
    }

    [Test]
    public void ThePatrolKeyIsThePositionsTemplateId()
    {
        var position = new DbKeepPosition { TemplateID = "Crauchon_Bledmeer_Benowyc_Patrol_new", ClassType = "DOL.GS.Keeps.Patrol" };
        Assert.That(Patrol.KeyFor(position), Is.EqualTo("Crauchon_Bledmeer_Benowyc_Patrol_new"));
        Assert.That(Patrol.KeyFor(null), Is.Null);
    }

    [Test]
    public void ASecondPatrolOfTheSameKeyNeitherThrowsNorAddsGuards()
    {
        (GameKeep keep, GameKeepComponent component) = KeepWithComponent();
        var first = new Patrol(component) { PatrolID = "Tower_Patrol_new" };
        keep.Patrols.Add(first.PatrolID, first);   // the patrol the keep's first FillPositions made
        var second = new Patrol(component) { PatrolID = "Tower_Patrol_new" };

        Assert.DoesNotThrow(() => second.InitialiseGuards(), "a keep level change's FillPositions again");
        Assert.That(keep.Patrols["Tower_Patrol_new"], Is.SameAs(first), "the patrol already there stays");
        Assert.That(keep.Patrols, Has.Count.EqualTo(1));
        Assert.That(second.PatrolGuards, Is.Empty, "no second set of guards");
    }

    [Test]
    public void FillPositionsChecksTheKeyThePatrolIsKeptUnder()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "keeps", "GameKeepComponent.cs")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        string fill = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "keeps", "GameKeepComponent.cs"));
        int start = fill.IndexOf("case \"DOL.GS.Keeps.Patrol\":", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        string patrolCase = fill.Substring(start, fill.IndexOf("continue;", start, StringComparison.Ordinal) - start);
        Assert.That(patrolCase, Does.Contain("Keep.Patrols.ContainsKey(Patrol.KeyFor(position))"));
        Assert.That(patrolCase, Does.Contain("p.PatrolID = Patrol.KeyFor(position);"));
        Assert.That(patrolCase, Does.Not.Contain("ContainsKey(sKey)"));
    }
}
