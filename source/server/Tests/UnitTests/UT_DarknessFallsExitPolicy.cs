using DOL.Database;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_DarknessFallsExitPolicy
{
    [Test]
    public void EachExitId_UsesCharactersOwnRealmRow_EvenWhenAlbionIsFirst()
    {
        foreach (ushort id in new ushort[] { 70, 71, 72, 73, 74, 75, 76, 77 })
        {
            DbZonePoint[] rows =
            [
                new() { Id = id, Realm = 1, SourceRegion = 249, TargetRegion = 1 },
                new() { Id = id, Realm = 2, SourceRegion = 249, TargetRegion = 100 },
                new() { Id = id, Realm = 3, SourceRegion = 249, TargetRegion = 200 },
            ];

            Assert.Multiple(() =>
            {
                Assert.That(DarknessFallsExitPolicy.SelectRealmExit(rows, eRealm.Albion, id), Is.SameAs(rows[0]));
                Assert.That(DarknessFallsExitPolicy.SelectRealmExit(rows, eRealm.Midgard, id), Is.SameAs(rows[1]));
                Assert.That(DarknessFallsExitPolicy.SelectRealmExit(rows, eRealm.Hibernia, id), Is.SameAs(rows[2]));
                Assert.That(DarknessFallsExitPolicy.SelectRealmExit(rows, eRealm.None, id), Is.Null);
            });
        }
    }

    [Test]
    public void WrongRealmOrSource_CannotBeSelected_AndOtherPortalsAreUnaffected()
    {
        DbZonePoint[] badRows =
        [
            new() { Id = 72, Realm = 1, SourceRegion = 249, TargetRegion = 1 },
            new() { Id = 72, Realm = 3, SourceRegion = 249, TargetRegion = 1 },
            new() { Id = 72, Realm = 3, SourceRegion = 100, TargetRegion = 200 },
        ];

        Assert.Multiple(() =>
        {
            Assert.That(DarknessFallsExitPolicy.SelectRealmExit(badRows, eRealm.Hibernia, 72), Is.Null);
            Assert.That(DarknessFallsExitPolicy.SelectRealmExit(badRows, eRealm.Albion, 74), Is.Null,
                "A different portal ID cannot be selected from an unexpected DB result.");
            Assert.That(DarknessFallsExitPolicy.IsExitRequest(249, 78), Is.False,
                "The internal Purgatory portal is not a realm exit.");
            Assert.That(DarknessFallsExitPolicy.IsExitRequest(200, 72), Is.False,
                "Other regions keep their existing portal rules.");
        });
    }

    [TestCase((ushort)71)]
    [TestCase((ushort)77)]
    public void TheMidBossAndLegionExits_LeadEachRealmHome_WhenTheWorldHasNoRow(ushort id)
    {
        Assert.That(DarknessFallsExitPolicy.IsExitRequest(249, id), Is.True);
        foreach (var (realm, home) in new[] { (eRealm.Albion, (ushort)1), (eRealm.Midgard, (ushort)100), (eRealm.Hibernia, (ushort)200) })
        {
            DbZonePoint exit = DarknessFallsExitPolicy.ResolveRealmExit([], realm, id);
            Assert.That(exit, Is.Not.Null, $"{realm} {id}");
            Assert.That(exit.TargetRegion, Is.EqualTo(home));
            Assert.That(exit.Id, Is.EqualTo(id));
        }
        Assert.That(DarknessFallsExitPolicy.ResolveRealmExit([], eRealm.None, id), Is.Null);
    }

    [Test]
    public void AWorldRow_WinsOverTheBuiltInOne()
    {
        DbZonePoint own = new() { Id = 77, Realm = 1, SourceRegion = 249, TargetRegion = 1, TargetX = 1 };
        Assert.That(DarknessFallsExitPolicy.ResolveRealmExit([own], eRealm.Albion, 77), Is.SameAs(own));
        Assert.That(DarknessFallsExitPolicy.ResolveRealmExit([], eRealm.Albion, 72), Is.Null,
            "only 71 and 77 have built-in rows; the others come from the world");
    }

    [Test]
    public void TheBuiltInExits_GoWhereTheWorldsOtherExitsGo()
    {
        // The released world's DF exits 70 and 72-76 all lead to these three spots (checked on a 0.37b world, 2026-11-08).
        foreach (DbZonePoint exit in DarknessFallsExitPolicy.BuiltInExits)
        {
            (int x, int y, int z) want = exit.Realm switch
            {
                1 => (599195, 536170, 2960),
                2 => (760782, 700723, 6292),
                _ => (324957, 433427, 6317),
            };
            Assert.That((exit.TargetX, exit.TargetY, exit.TargetZ), Is.EqualTo(want), exit.ObjectId);
        }
    }
}
