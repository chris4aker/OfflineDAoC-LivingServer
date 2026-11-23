using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #438 (1.2.2): the way up Darkness Falls' entrance stairs. zone249.nav has each realm's stairs as three one-way drops,
/// so a path up them is partial: tools/navmesh/dfprobe found it ends at the foot of the stairs, or, for a goal near the
/// arrival point, 100-250 polygons into another level (Albion: 103 nodes to 30395,27411,20180; Midgard: 235 nodes to
/// 18315,21055,21543). A bot heading up climbs the certified falls backwards, one drop at a time (DF_STAIR_CLIMB).
/// These use the certified links as shipped. Tests only: the navmesh queries were run with dfprobe, not in this test.
/// </summary>
[TestFixture]
public class UT_DarknessFallsStairClimb
{
    private static AutonomousDarknessFallsNavigation.TraversalLink[] ShippedLinks()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        string path = null;
        for (DirectoryInfo at = new(dir); at != null && path == null; at = at.Parent)
        {
            string candidate = Path.Combine(at.FullName, "GameServer", "bots", "autonomous", "data", "darkness_falls_navigation_proofs.json");
            if (File.Exists(candidate)) path = candidate;
            candidate = Path.Combine(at.FullName, "source", "server", "GameServer", "bots", "autonomous", "data", "darkness_falls_navigation_proofs.json");
            if (path == null && File.Exists(candidate)) path = candidate;
        }
        Assert.That(path, Is.Not.Null, "darkness_falls_navigation_proofs.json not found above " + dir);
        var certificate = JsonSerializer.Deserialize<AutonomousDarknessFallsNavigation.Certificate>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        // The climbs and ladders together, as the server holds them: the planner must pick the falls only.
        return certificate.TraversalLinks;
    }

    // Hall floor beside the foot of each realm's stairs, toward that realm's arrival point at the top.
    [TestCase(34400f, 27990f, 22386f, 31211f, 27924f, 22893f, 34301.0f, 34269.0f, 33508.0f, TestName = "Albion hall floor to the arrival")]
    [TestCase(15300f, 18660f, 22381f, 18798f, 18667f, 22892f, 15373.1f, 15407.7f, 16196.2f, TestName = "Midgard hall floor to the arrival")]
    [TestCase(43450f, 38060f, 20336f, 46325f, 40969f, 21357f, 43528.8f, 43560.6f, 44310.0f, TestName = "Hibernia hall floor to the arrival")]
    public void FromTheHallFloor_TheClimbStartsAtTheLowestDrop_AndTheChainReachesTheTop(float x, float y, float z,
        float gx, float gy, float gz, float footX, float lipX, float topX)
    {
        Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(new(x, y, z), new(gx, gy, gz), ShippedLinks(),
            out var climb), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(climb.Foot.X, Is.EqualTo(footX).Within(0.2f), "the lowest drop's landing");
            Assert.That(climb.Lip.X, Is.EqualTo(lipX).Within(0.2f), "the lowest drop's lip");
            Assert.That(climb.Top.X, Is.EqualTo(topX).Within(0.2f), "the top drop's lip, on the arrival floor");
            Assert.That(climb.Steps, Is.EqualTo(3));
            Assert.That(climb.Air.Z, Is.EqualTo(climb.Lip.Z).Within(16f), "the rise ends at lip height");
            Assert.That(new Vector2(climb.Air.X - climb.Foot.X, climb.Air.Y - climb.Foot.Y).Length(), Is.LessThanOrEqualTo(8f),
                "the rise is straight up over the landing, the column the fall comes down");
        });
    }

    [TestCase(34269.0f, 27960.2f, 22512.6f, 33839.9f, 2, TestName = "Albion second landing: the middle drop next")]
    [TestCase(33805.8f, 27957.4f, 22704.6f, 33541.0f, 1, TestName = "Albion first landing: the top drop next")]
    [TestCase(15873.2f, 18671.4f, 22704.6f, 16164.0f, 1, TestName = "Midgard first landing")]
    [TestCase(43560.6f, 38054.6f, 20464.6f, 43997.0f, 2, TestName = "Hibernia second landing")]
    public void FromALanding_TheNextDropUpIsClimbed(float x, float y, float z, float footX, int steps)
    {
        Vector3 goal = x > 40000 ? new(46325, 40969, 21357) : x > 30000 ? new(31211, 27924, 22893) : new(18798, 18667, 22892);
        Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(new(x, y, z), goal, ShippedLinks(), out var climb), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(climb.Foot.X, Is.EqualTo(footX).Within(0.2f));
            Assert.That(climb.Steps, Is.EqualTo(steps));
        });
    }

    [Test]
    public void AGoalOnTheFirstLanding_IsClimbedToFromTheHall()
    {
        // A player standing halfway up: the climb starts, and the top of the chain still reaches the goal (falls down).
        Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(new(34400, 27990, 22386), new(33700, 27955, 22706),
            ShippedLinks(), out var climb), Is.True);
        Assert.That(climb.Foot.X, Is.EqualTo(34301.0f).Within(0.2f));
    }

    [TestCase(34400f, 27990f, 22386f, 36118f, 30504f, 22381f, TestName = "Hall floor to the Albion exit: no climb")]
    [TestCase(31211f, 27924f, 22893f, 31600f, 27940f, 22893f, TestName = "On the arrival floor: nothing above")]
    [TestCase(31211f, 27924f, 22893f, 36118f, 30504f, 22381f, TestName = "Arrival to the exit: the falls, not a climb")]
    [TestCase(36118f, 30504f, 22381f, 31211f, 27924f, 22893f, TestName = "At the exit, 3,100 from the foot: out of reach")]
    [TestCase(34400f, 27990f, 20179f, 31211f, 27924f, 22893f, TestName = "A lower level under the stairs")]
    public void NoClimb_WhereTheStairsAreNotTheWayUp(float x, float y, float z, float gx, float gy, float gz)
    {
        Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(new(x, y, z), new(gx, gy, gz), ShippedLinks(), out _),
            Is.False);
    }

    [Test]
    public void TheExitOfEachRealm_IsNotUpTheStairs()
    {
        // Leaving DF never needs a climb: every realm's exit is on its hall floor, below the stairs (dfprobe: complete paths
        // from the hall floor and from the arrival point to zone points 74, 70 and 72).
        Vector3[] exits = [new(36118, 30504, 22381), new(11920, 18671, 22380), new(41722, 36569, 20333)];
        Vector3[] halls = [new(34400, 27990, 22386), new(15300, 18660, 22381), new(43450, 38060, 20336)];
        for (int i = 0; i < 3; i++)
            Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(halls[i], exits[i], ShippedLinks(), out _), Is.False);
    }

    [Test]
    public void AtTheFoot_OnlyOnTheLanding()
    {
        Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(new(34400, 27990, 22386), new(31211, 27924, 22893),
            ShippedLinks(), out var climb), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.IsAtStairFoot(climb.Foot, climb), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.IsAtStairFoot(climb.Foot + new Vector3(20, 0, 5), climb), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.IsAtStairFoot(new(34400, 27990, 22386), climb), Is.False);
            Assert.That(AutonomousDarknessFallsNavigation.IsAtStairFoot(climb.Air, climb), Is.False, "over the landing is not on it");
        });
    }

    [Test]
    public void NoLinks_NoClimb()
    {
        // The links come from the hash-guarded set the falls use: none when zone249.nav is not the certified mesh.
        Assert.That(AutonomousDarknessFallsNavigation.TryPlanStairClimb(new(34400, 27990, 22386), new(31211, 27924, 22893),
            null, out _), Is.False);
    }
}
