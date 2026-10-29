using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_DarknessFallsLoginEntrance
{
    private static AutonomousDarknessFallsNavigation.SpawnProof Spawn(params (eRealm Realm, float X, float Y, float Z)[] entrances) =>
        new()
        {
            Id = "mob",
            Routes = entrances.Select(entrance => new AutonomousDarknessFallsNavigation.RouteProof
            {
                Realm = entrance.Realm,
                InWaypoints = [[entrance.X, entrance.Y, entrance.Z], [entrance.X + 100, entrance.Y, entrance.Z]],
            }).ToArray(),
        };

    [Test]
    public void TheEntranceIsTheFirstWaypointOfTheRealmsRoute()
    {
        var spawns = new[]
        {
            Spawn((eRealm.Albion, 31211, 27924, 22893), (eRealm.Midgard, 18798, 18667, 22892), (eRealm.Hibernia, 46325, 40969, 21357)),
        };
        Assert.That(AutonomousDarknessFallsNavigation.TryFindEntrance(spawns, eRealm.Midgard, out Vector3 entrance), Is.True);
        Assert.That(entrance, Is.EqualTo(new Vector3(18798, 18667, 22892)));
        Assert.That(AutonomousDarknessFallsNavigation.TryFindEntrance(spawns, eRealm.Hibernia, out entrance), Is.True);
        Assert.That(entrance, Is.EqualTo(new Vector3(46325, 40969, 21357)));
    }

    [Test]
    public void ARealmWithoutARouteHasNoEntrance()
    {
        var spawns = new[] { Spawn((eRealm.Albion, 1, 2, 3)) };
        Assert.That(AutonomousDarknessFallsNavigation.TryFindEntrance(spawns, eRealm.Midgard, out _), Is.False);
        Assert.That(AutonomousDarknessFallsNavigation.TryFindEntrance([], eRealm.Albion, out _), Is.False);
        Assert.That(AutonomousDarknessFallsNavigation.TryFindEntrance(null, eRealm.Albion, out _), Is.False);
    }

    [Test]
    public void ABrokenRouteIsSkipped()
    {
        var broken = new AutonomousDarknessFallsNavigation.SpawnProof
        {
            Id = "bad",
            Routes = [new AutonomousDarknessFallsNavigation.RouteProof { Realm = eRealm.Albion, InWaypoints = [[float.NaN, 0, 0]] }],
        };
        var good = Spawn((eRealm.Albion, 31211, 27924, 22893));
        Assert.That(AutonomousDarknessFallsNavigation.TryFindEntrance([broken, good], eRealm.Albion, out Vector3 entrance), Is.True);
        Assert.That(entrance, Is.EqualTo(new Vector3(31211, 27924, 22893)));
    }
}
