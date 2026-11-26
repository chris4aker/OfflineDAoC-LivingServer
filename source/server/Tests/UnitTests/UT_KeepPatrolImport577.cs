using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #577 (owner decision 2026-11-26, (a)): the New Frontiers keep patrols from db-public e6770459, added once to a world by
/// KeepPatrolImport, only where rows are missing, and never beside another set of patrol positions.
/// </summary>
[TestFixture]
public class UT_KeepPatrolImport577
{
    private static string Repo()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "keeps", "data", KeepPatrolImport.ResourceSuffix)))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        return dir;
    }

    private static KeepPatrolImport.Data FromRepo() =>
        KeepPatrolImport.Parse(File.ReadAllText(Path.Combine(Repo(), "source", "server", "GameServer", "keeps", "data", KeepPatrolImport.ResourceSuffix)));

    [Test]
    public void TheDataIsDbPublicsSixteenPatrols()
    {
        KeepPatrolImport.Data data = FromRepo();
        Assert.That(data.Positions, Has.Count.EqualTo(16));
        Assert.That(data.Paths, Has.Count.EqualTo(16));
        Assert.That(data.Points, Has.Count.EqualTo(140));
        // One patrol per keep layout (1-7) and per tower (0), for the old and the new keep models.
        Assert.That(data.Positions.Select(p => p.KeepType).Distinct().OrderBy(t => t), Is.EqualTo(Enumerable.Range(0, 8)));
        Assert.That(data.Positions.Where(p => p.KeepType > 0).Select(p => p.ComponentSkin).Distinct().OrderBy(s => s), Is.EqualTo(new[] { 0, 24 }));
        Assert.That(data.Positions.Where(p => p.KeepType == 0).Select(p => p.ComponentSkin).OrderBy(s => s), Is.EqualTo(new[] { 11, 31 }));
        Assert.That(data.Positions.Select(p => p.TemplateId), Is.Unique);
    }

    [Test]
    public void EveryPatrolHasAWholeGroundPath()
    {
        KeepPatrolImport.Data data = FromRepo();
        foreach (KeepPatrolImport.Position position in data.Positions)
        {
            Assert.That(data.Paths.Count(p => p.PathId == position.TemplateId), Is.EqualTo(1), position.TemplateId);
            int[] steps = data.Points.Where(p => p.PathId == position.TemplateId).Select(p => p.Step).OrderBy(s => s).ToArray();
            Assert.That(steps, Has.Length.GreaterThanOrEqualTo(8), position.TemplateId);
            Assert.That(steps, Is.EqualTo(Enumerable.Range(1, steps.Length)), $"{position.TemplateId}: steps 1..n");
        }
        Assert.That(data.Points.All(p => Math.Abs(p.Z) <= 200), Is.True, "ground loops: Z is a small offset from the keep's own");
        Assert.That(data.Paths.All(p => p.PathType == 3 && p.RegionId == 0), Is.True);
        Assert.That(data.Points.Select(p => p.PathId).Distinct().Count(), Is.EqualTo(16));
    }

    [Test]
    public void AnEmptyWorldGetsEverything()
    {
        KeepPatrolImport.Data data = FromRepo();
        KeepPatrolImport.Plan plan = KeepPatrolImport.For(data, [], [], _ => 0);
        Assert.That(plan.Skip, Is.Null);
        Assert.That((plan.Positions.Count, plan.Paths.Count, plan.Points.Count), Is.EqualTo((16, 16, 140)));
        Assert.That(KeepPatrolImport.ImportedLine(plan), Is.EqualTo(
            "KEEP_PATROL_IMPORTED positions=16 paths=16 points=140 source=db-public-e6770459 reason=owner-577a"));
    }

    [Test]
    public void OnlyMissingRowsAreAdded()
    {
        KeepPatrolImport.Data data = FromRepo();
        KeepPatrolImport.Position have = data.Positions.First(p => p.TemplateId == "Tower_Patrol_new");
        KeepPatrolImport.Plan plan = KeepPatrolImport.For(data, [(have.TemplateId, have.ComponentSkin)], ["Tower_Patrol_new"],
            id => id == "Tower_Patrol_new" ? 8 : 0);
        Assert.That(plan.Skip, Is.Null);
        Assert.That(plan.Positions.Select(p => p.TemplateId), Does.Not.Contain("Tower_Patrol_new"));
        Assert.That(plan.Positions, Has.Count.EqualTo(15));
        Assert.That(plan.Paths, Has.Count.EqualTo(15));
        Assert.That(plan.Points.Any(p => p.PathId == "Tower_Patrol_new"), Is.False);
        Assert.That(KeepPatrolImport.For(data, data.Positions.Select(p => (p.TemplateId, p.ComponentSkin)),
            data.Paths.Select(p => p.PathId), _ => 9).Empty, Is.True, "a world that has them all gets nothing");
    }

    [Test]
    public void AWorldWithOtherPatrolPositionsGetsNothing()
    {
        KeepPatrolImport.Plan plan = KeepPatrolImport.For(FromRepo(), [("Benowyc", 1), ("towerpatrol", 11)], [], _ => 0);
        Assert.That(plan.Empty, Is.True, "no keep gets two patrols");
        Assert.That(plan.Skip, Does.Contain("Benowyc").And.Contain("towerpatrol"));
    }

    [Test]
    public void TheImportRunsOncePerWorld()
    {
        Assert.That(KeepPatrolImport.AlreadyRan("hib-keep-levels-1.2.3"), Is.False);
        string both = KeepPatrolImport.WithRepair("hib-keep-levels-1.2.3");
        Assert.That(both, Is.EqualTo("hib-keep-levels-1.2.3,keep-patrols-1.2.3"));
        Assert.That(KeepPatrolImport.AlreadyRan(both), Is.True);
        Assert.That(KeepPatrolImport.WithRepair(both), Is.EqualTo(both));
    }

    [Test]
    public void TheDataIsEmbeddedAndTheImportRunsBeforeTheKeepsLoad()
    {
        string repo = Repo();
        string csproj = File.ReadAllText(Path.Combine(repo, "source", "server", "GameServer", "GameServer.csproj"));
        Assert.That(csproj, Does.Contain(@"<EmbeddedResource Include="".\keeps\data\keep_patrols_dbpublic.json"" />"));
        Assert.That(KeepPatrolImport.Load().Positions, Has.Count.EqualTo(16), "read from GameServer.dll");
        string server = File.ReadAllText(Path.Combine(repo, "source", "server", "GameServer", "GameServer.cs"));
        int import = server.IndexOf("KeepPatrolImport.Run()", StringComparison.Ordinal);
        int keeps = server.IndexOf("InitComponent(StartKeepManager(), \"Keep Manager\")", StringComparison.Ordinal);
        Assert.That(import, Is.GreaterThan(0));
        Assert.That(import, Is.LessThan(keeps));
    }
}
