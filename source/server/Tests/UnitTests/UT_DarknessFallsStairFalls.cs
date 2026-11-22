using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #438 (1.2.1): Darkness Falls' entrance stairs are three one-way ledges per realm in zone249.nav (each upper landing
/// ends in a sheer edge, an off-mesh drop of 128-191 units leads to the next). The probe in tools/navmesh found that a
/// bot inside a drop is on no polygon within the server's pick box (game 32, 32, 64), so every path from there failed
/// and a companion left there by a new order stood on the step for good. These use the certified links as shipped.
/// Tests only: the navmesh queries were run with tools/navmesh/dfprobe, not in this test.
/// </summary>
[TestFixture]
public class UT_DarknessFallsStairFalls
{
    private static AutonomousDarknessFallsNavigation.TraversalLink[] ShippedFalls()
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
        return certificate.TraversalLinks.Where(link => link.Kind == "Fall").ToArray();
    }

    [Test]
    public void ShippedCertificate_HasThreeStairDropsPerRealm()
    {
        var falls = ShippedFalls();
        Assert.Multiple(() =>
        {
            Assert.That(falls, Has.Length.EqualTo(9));
            Assert.That(falls.Count(f => f.Start[0] is > 33000 and < 35000), Is.EqualTo(3), "Albion, x 33508-34269");
            Assert.That(falls.Count(f => f.Start[0] is > 15000 and < 17000), Is.EqualTo(3), "Midgard");
            Assert.That(falls.Count(f => f.Start[0] is > 43000 and < 45000), Is.EqualTo(3), "Hibernia");
        });
    }

    [TestCase(33524.5f, 27951.5f, 22797f, 33541.0f, 22709.2f, TestName = "Albion drop 1, dead window t=0.5")]
    [TestCase(33822.8f, 27957.1f, 22609f, 33839.9f, 22513.8f, TestName = "Albion drop 2, dead window t=0.5")]
    [TestCase(33839.9f, 27956.9f, 22600f, 33839.9f, 22513.8f, TestName = "Albion drop 2, under the air point")]
    [TestCase(16180.1f, 18680.5f, 22800f, 16164.0f, 22706.7f, TestName = "Midgard drop 1")]
    [TestCase(44014.0f, 38021.4f, 20561f, 43997.0f, 20465.8f, TestName = "Hibernia drop 2")]
    public void BotInsideADrop_IsSetDownOnThatDropsLanding(float x, float y, float z, float landingX, float landingZ)
    {
        Assert.That(AutonomousDarknessFallsNavigation.TryResolveFallLanding(new(x, y, z), ShippedFalls(), out Vector3 landing), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(landing.X, Is.EqualTo(landingX).Within(0.2f));
            Assert.That(landing.Z, Is.EqualTo(landingZ).Within(0.2f));
        });
    }

    [TestCase(33500f, 27951f, 22885f, TestName = "On the Albion entry landing")]
    [TestCase(33541f, 27951f, 22709f, TestName = "On the first landing below")]
    [TestCase(33700f, 27954f, 22800f, TestName = "In the air but away from any drop")]
    [TestCase(31211f, 27924f, 22893f, TestName = "The Albion arrival point")]
    [TestCase(36118f, 30504f, 22381f, TestName = "The Albion exit")]
    public void BotOnAFloorOrAwayFromTheDrops_IsLeftAlone(float x, float y, float z)
    {
        Assert.That(AutonomousDarknessFallsNavigation.TryResolveFallLanding(new(x, y, z), ShippedFalls(), out _), Is.False);
    }

    [Test]
    public void TheLipOfEveryShippedDrop_ResolvesItsFall_ForAnyBot()
    {
        foreach (var fall in ShippedFalls())
        {
            Vector3 lip = new(fall.Start[0], fall.Start[1], fall.Start[2]);
            Vector3 end = new(fall.End[0], fall.End[1], fall.End[2]);
            Assert.That(AutonomousDarknessFallsNavigation.TryResolveFall(lip, end, ShippedFalls(), out Vector3 air, out Vector3 landing), Is.True);
            Assert.That(air.Z, Is.EqualTo(lip.Z).Within(16f), "the walk off the lip stays level");
            Assert.That(landing, Is.EqualTo(end));
        }
    }

    [Test]
    public void NoLinks_NoLanding()
    {
        Assert.That(AutonomousDarknessFallsNavigation.TryResolveFallLanding(new(33524.5f, 27951.5f, 22797f), null, out _), Is.False);
    }
}
