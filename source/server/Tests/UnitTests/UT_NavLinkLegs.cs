using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.2 (#438 generalized): every off-mesh link leg in the shipped navmeshes whose middle is on no polygon within the
/// server's pick box (tools/navmesh/navmesh-drops.json, from tools/navmesh/navscan over all 103 navmeshes of the v0.36b
/// download) is covered by the mover's generic rules: a bot inside one is set down on the leg's far end, and a steep one
/// is walked to its end before a replot. Darkness Falls keeps its certified stair falls (UT_DarknessFallsStairFalls).
/// Tests only: the navmesh queries were run with tools/navmesh/navscan, not in this test.
/// </summary>
[TestFixture]
public class UT_NavLinkLegs
{
    private sealed record Link(Vector3 From, Vector3 To, float DeadT0, float DeadT1, bool Lift, bool Commit, bool Used);

    private static string FindUp(params string[] relative)
    {
        for (DirectoryInfo at = new(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
        {
            string candidate = Path.Combine(new[] { at.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static JsonDocument Table()
    {
        string path = FindUp("tools", "navmesh", "navmesh-drops.json");
        Assert.That(path, Is.Not.Null, "tools/navmesh/navmesh-drops.json not found");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static List<(int Zone, Link Link)> StuckProne()
    {
        using JsonDocument table = Table();
        var all = new List<(int, Link)>();
        foreach (JsonElement zone in table.RootElement.GetProperty("zones").EnumerateArray())
        foreach (JsonElement x in zone.GetProperty("stuckProne").EnumerateArray())
        {
            JsonElement t = x.GetProperty("deadT");
            all.Add((zone.GetProperty("zone").GetInt32(), new Link(V(x.GetProperty("from")), V(x.GetProperty("to")),
                t[0].GetSingle(), t[1].GetSingle(), x.GetProperty("lift").GetBoolean(), x.GetProperty("commit").GetBoolean(),
                x.GetProperty("used").GetBoolean())));
        }
        return all;
    }

    [Test]
    public void TheScanCoversEveryShippedNavmesh_AndItsDarknessFallsMeshIsTheCertifiedOne()
    {
        using JsonDocument table = Table();
        var zones = table.RootElement.GetProperty("zones").EnumerateArray().ToArray();
        string df = zones.Single(z => z.GetProperty("zone").GetInt32() == 249).GetProperty("sha256").GetString();
        string certificate = FindUp("GameServer", "bots", "autonomous", "data", "darkness_falls_navigation_proofs.json") ??
            FindUp("source", "server", "GameServer", "bots", "autonomous", "data", "darkness_falls_navigation_proofs.json");
        Assert.That(certificate, Is.Not.Null);
        using JsonDocument proofs = JsonDocument.Parse(File.ReadAllText(certificate));
        Assert.Multiple(() =>
        {
            Assert.That(zones, Has.Length.EqualTo(103));
            Assert.That(df, Is.EqualTo(proofs.RootElement.GetProperty("NavSha256").GetString()).IgnoreCase);
            Assert.That(StuckProne(), Has.Count.EqualTo(1262));
        });
    }

    [Test]
    public void DarknessFalls_KeepsItsOwnRules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NavLinkLeg.AppliesIn(249), Is.False);
            Assert.That(NavLinkLeg.AppliesIn(60), Is.True, "Caer Sidi");
            Assert.That(NavLinkLeg.AppliesIn(0), Is.True, "Camelot Hills");
        });
    }

    [Test]
    public void ABotInsideAnyStuckProneLink_IsSetDownOnItsFarEnd()
    {
        int checkedLinks = 0;
        foreach ((int zone, Link link) in StuckProne())
        {
            if (!NavLinkLeg.AppliesIn((ushort) zone)) continue;
            // The middle of the link's dead window: the point where the probe found no polygon.
            float t = (link.DeadT0 + link.DeadT1) * 0.5f;
            Vector3 inside = Vector3.Lerp(link.From, link.To, t);
            if (Vector3.Distance(inside, link.To) <= 8) continue; // the far end itself has no polygon: no path uses it
            Assert.That(NavLinkLeg.TryResolveLanding(inside, link.From, link.To, null, link.To, out Vector3 landing), Is.True,
                $"zone {zone} {link.From} -> {link.To} at t={t:0.00}");
            Assert.That(landing, Is.EqualTo(link.To));
            checkedLinks++;
        }
        Assert.That(checkedLinks, Is.GreaterThan(1150));
    }

    [Test]
    public void EverySteepLinkTheScanMarks_IsACommitLeg_AndEveryUsedOneIsSteepOrHasALandingRule()
    {
        var all = StuckProne();
        Assert.Multiple(() =>
        {
            foreach ((int zone, Link link) in all)
            {
                Assert.That(NavLinkLeg.IsCommitLeg(link.From, link.To), Is.EqualTo(link.Commit), $"zone {zone} {link.From} -> {link.To}");
                Assert.That(NavLinkLeg.IsLiftLeg(link.From, link.To), Is.EqualTo(link.Lift), $"lift: zone {zone} {link.From} -> {link.To}");
                if (link.Lift) Assert.That(link.Commit, Is.False, $"a lift is never a commit leg: zone {zone}");
            }
            // Of the non-lift directions a path really takes, all but the shallow gaps and ice steps (Tuscaran Glacier,
            // Fomor) are steep: these are walked to their end. The shallow ones are covered by the landing rule above,
            // and the lifts by 1.2's lift ride (Pathfinder.IsVerticalLink).
            int used = all.Count(x => x.Link.Used && !x.Link.Lift), steepUsed = all.Count(x => x.Link.Used && x.Link.Commit);
            Assert.That(steepUsed, Is.GreaterThanOrEqualTo(used - 30));
            Assert.That(all.Count(x => x.Link.Lift), Is.EqualTo(546));
        });
    }

    // 1.2.2 regression guard (audit item 6): the zones with a raid win or siege seen on the owner's PC on 1.2 builds and
    // stuck-prone links. Every one of their links is a lift, which 1.2 rode with Pathfinder.IsVerticalLink and no commit
    // rule. They must stay so: no leg there is a commit leg, every one is still ridden by the lift rule, and the only new
    // behaviour is the stranded set-down (a bot left inside one, off the mesh, is put on its far end).
    [TestCase(190, 4, TestName = "Tur Suil (zone 190): 4 lift directions")]
    [TestCase(161, 12, TestName = "Iarnvidiur's Lair (zone 161): 12 lift directions")]
    [TestCase(56, 2, TestName = "Aldland (zone 56, Coliath): 2 lift directions")]
    [TestCase(211, 16, TestName = "Cruachan Gorge (zone 211): 16 lift directions")]
    public void WonZones_LegsBehaveAsIn12_ExceptTheStrandedSetDown(int zoneId, int directions)
    {
        var links = StuckProne().Where(x => x.Zone == zoneId).Select(x => x.Link).ToList();
        Assert.That(links, Has.Count.EqualTo(directions), "the scan's table changed for a won zone");
        Assert.Multiple(() =>
        {
            Assert.That(NavLinkLeg.AppliesIn((ushort) zoneId), Is.True);
            foreach (Link link in links)
            {
                string at = $"zone {zoneId} {link.From} -> {link.To}";
                Assert.That(link.Lift, Is.True, at);
                Assert.That(Pathfinder.IsVerticalLink(EDtPolyFlags.Jump, link.From, link.To), Is.True, "1.2's lift ride: " + at);
                Assert.That(NavLinkLeg.IsLiftLeg(link.From, link.To), Is.True, at);
                Assert.That(NavLinkLeg.IsCommitLeg(link.From, link.To), Is.False, "no commit (1.2 had none): " + at);
                Vector3 inside = Vector3.Lerp(link.From, link.To, (link.DeadT0 + link.DeadT1) * 0.5f);
                Assert.That(NavLinkLeg.TryResolveLanding(inside, link.From, link.To, null, link.To, out Vector3 landing), Is.True,
                    "the stranded set-down: " + at);
                Assert.That(landing, Is.EqualTo(link.To));
                Assert.That(NavLinkLeg.TryResolveLanding(inside, link.From, link.To, inside, link.To, out _), Is.False,
                    "a bot on a polygon is left alone, as in 1.2: " + at);
            }
        });
    }

    [Test]
    public void TurSuilLiftLeg_IsRidden_NeverCommitted()
    {
        // tools/navmesh/navmesh-drops.json, zone 190: the lift 880 up over 64 (both directions).
        Vector3 foot = new(31065.6f, 15148.8f, 11040.0f), top = new(31065.6f, 15084.8f, 11920.0f);
        Assert.That(StuckProne().Any(x => x.Zone == 190 && x.Link.From == foot && x.Link.To == top), Is.True,
            "the Tur Suil lift is in the scan's table");
        Assert.Multiple(() =>
        {
            Assert.That(Pathfinder.IsVerticalLink(EDtPolyFlags.Jump, foot, top), Is.True, "ridden up");
            Assert.That(Pathfinder.IsVerticalLink(EDtPolyFlags.Jump, top, foot), Is.True, "ridden down");
            Assert.That(NavLinkLeg.IsCommitLeg(foot, top), Is.False);
            Assert.That(NavLinkLeg.IsCommitLeg(top, foot), Is.False);
        });
    }

    [TestCase(572828.8f, 504777.6f, 2198.4f, 572828.8f, 504803.2f, 2384f, TestName = "Camelot Hills climb, 186 up over 26")]
    [TestCase(33508.0f, 27951.7f, 22884.4f, 33541.0f, 27951.2f, 22709.2f, TestName = "A DF-shaped stair drop, 175 down over 33")]
    public void SteepLinks_AreCommitLegs(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        Assert.That(NavLinkLeg.IsCommitLeg(new(x0, y0, z0), new(x1, y1, z1)), Is.True);
    }

    [TestCase(0f, 0f, 0f, 200f, 0f, 40f, TestName = "A walkable slope")]
    [TestCase(0f, 0f, 0f, 50f, 0f, 50f, TestName = "45 degrees but under the pick box height")]
    [TestCase(0f, 0f, 0f, 400f, 0f, 400f, TestName = "Steep but longer than any link")]
    [TestCase(0f, 0f, 0f, 120f, 0f, 90f, TestName = "Rises 90 over 120")]
    [TestCase(583273.6f, 487273.6f, 3356.8f, 583273.6f, 487222.4f, 3670.4f, TestName = "A Camelot Hills lift, 314 up over 51: ridden, not committed")]
    [TestCase(31065.6f, 15148.8f, 11040.0f, 31065.6f, 15084.8f, 11920.0f, TestName = "The Tur Suil lift, 880 up over 64")]
    public void OrdinaryLegs_AreNotCommitLegs(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        Assert.That(NavLinkLeg.IsCommitLeg(new(x0, y0, z0), new(x1, y1, z1)), Is.False);
    }

    private static readonly Vector3 Lip = new(1000, 1000, 500), Foot = new(1030, 1000, 300);

    [Test]
    public void TheLandingRule_OnlyMovesABotThatIsInsideItsLegAndOffTheMesh()
    {
        Vector3 inside = Vector3.Lerp(Lip, Foot, 0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(NavLinkLeg.TryResolveLanding(inside, Lip, Foot, null, Foot, out Vector3 landing), Is.True);
            Assert.That(landing, Is.EqualTo(Foot));
            Assert.That(NavLinkLeg.TryResolveLanding(inside, Lip, Foot, inside, Foot, out _), Is.False, "on a polygon: left alone");
            Assert.That(NavLinkLeg.TryResolveLanding(inside, Lip, Foot, null, null, out _), Is.False, "no floor at the far end");
            Assert.That(NavLinkLeg.TryResolveLanding(inside, Lip, Foot, null, Foot + new Vector3(0, 0, 100), out _), Is.False,
                "the floor found is not the leg's end");
            Assert.That(NavLinkLeg.TryResolveLanding(Foot + new Vector3(2, 0, 0), Lip, Foot, null, Foot, out _), Is.False,
                "already at the far end");
            Assert.That(NavLinkLeg.TryResolveLanding(inside + new Vector3(0, 80, 0), Lip, Foot, null, Foot, out _), Is.False,
                "beside the leg, not in it");
            Assert.That(NavLinkLeg.TryResolveLanding(new(1015, 1000, 700), Lip, Foot, null, Foot, out _), Is.False,
                "far above both ends");
            Assert.That(NavLinkLeg.TryResolveLanding(new(1200, 1000, 400), new(1000, 1000, 400), new(1400, 1000, 420), null,
                new(1400, 1000, 420), out _), Is.False, "a long walking leg is never jumped along");
        });
    }

    [Test]
    public void TheLandingRule_IgnoresNonFinitePositions()
    {
        Vector3 nan = new(float.NaN, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(NavLinkLeg.IsOnLeg(nan, Lip, Foot), Is.False);
            Assert.That(NavLinkLeg.IsCommitLeg(nan, Foot), Is.False);
        });
    }
}
