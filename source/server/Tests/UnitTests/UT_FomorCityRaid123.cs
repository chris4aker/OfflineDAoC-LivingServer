using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using DOL.GS;
using DOL.GS.LoreChannels;
using Tales = DOL.GS.AutonomousTales;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Fomor City's raid (1.2.3, #414; tests only, nothing played). The raid, its period order and the four walk links of the
    /// zone180.nav content patch. The patched mesh itself is probed in UT_RaidDungeonRoutesProbe (explicit: needs the mesh).
    /// </summary>
    [TestFixture]
    public class UT_FomorCityRaid123
    {
        private static AutonomousRealmRaid.Definition Fomor =>
            AutonomousRealmRaid.Definitions.Single(d => d.Id == "dungeon-hibernia-fomor");

        [Test]
        public void FomorCityIsAHiberniaRaidDungeonAtThePeriodsSevenGroups() => Assert.Multiple(() =>
        {
            Assert.That(Fomor.Realm, Is.EqualTo(eRealm.Hibernia));
            Assert.That(Fomor.Region, Is.EqualTo((ushort)180));
            Assert.That(Fomor.IsDungeon, Is.True);
            Assert.That(Fomor.IsNeutral, Is.False);
            // Thredh's Fomor guide (Valmerwolf, 2003/2005): "7 gruppi sarebbero l'ideale".
            Assert.That(Fomor.SuggestedBots, Is.EqualTo(56));
            Assert.That(Fomor.BossType, Is.EqualTo("Myrddraxis"));
            Assert.That(Fomor.FinalTypes, Is.EqualTo(new[] { "Myrddraxis" }));
            Assert.That(Fomor.Trigger, Is.EqualTo(new Vector3(32302, 32221, 15635)), "Myrddraxis's spawn (world database)");
            var hubs = RealmRaidMuster.Hubs.Where(h => h.Event == Fomor.Id).Select(h => h.Name).ToArray();
            Assert.That(hubs, Is.EqualTo(new[] { "Droighaid", "Aalid Feie" }), "the period's bind first");
            Assert.That(LoreChronicle.RaidFinals[Fomor.Id], Is.EqualTo(Fomor.FinalTypes));
            Assert.That(Tales.RaidIdFor("myrddraxis", eRealm.Hibernia), Is.EqualTo(Fomor.Id));
            Assert.That(Tales.RaidlessTales, Does.Not.Contain("myrddraxis"));
            Assert.That(Tales.RaidlessTales, Does.Contain("behemoth"));
        });

        [Test]
        public void FomorsNamedAreFoughtInThePeriodsOrder() => Assert.Multiple(() =>
        {
            string[] order = RaidNamedOrder.For(180);
            Assert.That(order, Is.EqualTo(new[] { "Krackenschtein", "Nogoribando", "Anurigunda", "Myrddraxis" }));
            string[] all = ["Krackenschtein", "Nogoribando", "Anurigunda", "Myrddraxis", "MyrddraxisSecondHead"];
            Assert.That(RaidNamedOrder.WaitsFor(order, "Krackenschtein", false, all), Is.Null, "the outer ring's named first");
            Assert.That(RaidNamedOrder.WaitsFor(order, "Nogoribando", false, all), Is.EqualTo("Krackenschtein"));
            Assert.That(RaidNamedOrder.WaitsFor(order, "Nogoribando", true, all), Is.Null, "a named that fights the raid is fought");
            Assert.That(RaidNamedOrder.WaitsFor(order, "MyrddraxisSecondHead", false, ["Anurigunda", "MyrddraxisSecondHead"]),
                Is.EqualTo("Anurigunda"), "his heads wait for the Hydra's door too");
            Assert.That(RaidNamedOrder.WaitsFor(order, "Myrddraxis", false, ["Myrddraxis", "MyrddraxisSecondHead"]), Is.Null);
            Assert.That(RaidNamedOrder.WaitsFor(order, "fomorian gentarn", false, all), Is.Null, "monsters outside the order are not held");
            Assert.That(RaidNamedOrder.Next(order, ["Anurigunda", "Myrddraxis"]), Is.EqualTo("Anurigunda"));
            Assert.That(RaidNamedOrder.Next(order, []), Is.Null);
            Assert.That(RaidNamedOrder.For(190), Is.Empty, "other dungeons keep the nearest-first choice");
            Assert.That(RaidNamedOrder.Line(180, "Nogoribando", 2), Does.StartWith("REALM_EXPEDITION_NAMED_ORDER region=180 next=\"Nogoribando\" heldBack=2"));
        });

        [Test]
        public void TheWalkLinksAreShortAndLevel() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmRaid.FomorWalkLinks, Has.Length.EqualTo(4));
            foreach (var (a, b) in AutonomousRealmRaid.FomorWalkLinks)
            {
                float run = Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
                Assert.That(run, Is.InRange(30f, 60f), $"{a} -> {b}: a crack of 21-33 units plus 10 inside each floor");
                Assert.That(MathF.Abs(a.Z - b.Z), Is.LessThan(28.8f), $"{a} -> {b}: within the builder's walkable climb");
                // Both ends in one tile (add_offmesh.py): zone180.nav's origin 25147.0, 21693.8 and tile width 307.2.
                Assert.That(Math.Floor((a.X - 25147.0) / 307.2), Is.EqualTo(Math.Floor((b.X - 25147.0) / 307.2)));
                Assert.That(Math.Floor((a.Y - 21693.8) / 307.2), Is.EqualTo(Math.Floor((b.Y - 21693.8) / 307.2)));
            }
        });

        [Test]
        public void TheNavmeshPatchFixesTheReleasedFomorMesh()
        {
            string root = RepoRoot();
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "fork-build", "content-patches.json")));
            JsonElement entry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("Path").GetString() == "runtime/server/navmesh/zone180.nav");
            // The 0.36b and 0.37b packages ship the same zone180.nav (release-baselines), the mesh navmesh-drops.json scanned.
            const string released = "5482cffb1bae66bf4dfab869795c948d6e132a7278d0a941889459961f730172";
            foreach (string baseline in new[] { "0.36/0.36b-package.sha256", "0.37/0.37b-package.sha256" })
                Assert.That(File.ReadLines(Path.Combine(root, "source", "tools", "release-baselines", baseline))
                    .Any(l => l.StartsWith(released) && l.EndsWith("runtime/server/navmesh/zone180.nav")), Is.True, baseline);
            Assert.Multiple(() =>
            {
                Assert.That(entry.GetProperty("Released").GetString(), Is.EqualTo(released));
                Assert.That(entry.GetProperty("Fixed").GetString(), Is.EqualTo("a1162c94b38aede0ae4f19aa7e181bce73e5c2640e660182f8e75ef037329945"));
                // Four off-mesh connections appended to four tiles (add_offmesh.py): 624 bytes more than the released mesh.
                Assert.That(entry.GetProperty("Size").GetInt64(), Is.EqualTo(3798880));
                long copied = entry.GetProperty("Ops").EnumerateArray().Where(o => o.TryGetProperty("copy", out _))
                    .Sum(o => o.GetProperty("copy")[1].GetInt64());
                Assert.That(copied, Is.GreaterThan(3_700_000), "a byte patch of the released mesh, not a new mesh");
            });
        }

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "tools", "fork-build", "content-patches.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }
    }
}
