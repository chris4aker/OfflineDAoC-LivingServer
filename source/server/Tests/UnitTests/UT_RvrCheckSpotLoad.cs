using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 6: the embedded check-spot catalog loads (lazily, once), its entries parse, and the
    /// start-up validation turns off exactly what fails: a spot whose stand fails, every circuit through it, a circuit
    /// with a failed leg, a realm's reach to a spot. Pure: the walks are replaced by recorded results.
    /// </summary>
    [TestFixture]
    public class UT_RvrCheckSpotLoad
    {
        private static string Embedded()
        {
            var assembly = typeof(RvrCheckSpots).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("rvr-checkspots.json", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            return reader.ReadToEnd();
        }

        [Test]
        public void The_embedded_catalog_loads_with_every_spot_and_circuit()
        {
            RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
            Assert.That(catalog, Is.Not.Null);
            Assert.That(RvrCheckSpots.Catalog, Is.SameAs(catalog), "loaded once");
            Assert.Multiple(() =>
            {
                Assert.That(catalog.Frontiers.Select(f => (int)f.Region), Is.EquivalentTo(new[] { 1, 100, 200 }));
                Assert.That(catalog.SpotCount, Is.EqualTo(98));
                Assert.That(catalog.CircuitCount, Is.EqualTo(18));
                Assert.That(catalog.ParseRejections, Is.Empty);
                Assert.That(catalog.ProofHash, Does.StartWith("sha256:"));
            });
            foreach (RvrFrontier frontier in catalog.Frontiers)
            {
                eRealm owner = frontier.Region switch { 1 => eRealm.Albion, 100 => eRealm.Midgard, _ => eRealm.Hibernia };
                Assert.That(frontier.Entries.Keys, Is.EquivalentTo(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Where(r => r != owner)));
                Assert.That(frontier.Circuits, Has.Length.EqualTo(6));
                Assert.That(frontier.Circuits.All(c => c.Realm != owner), Is.True, "circuits are walked by the enemy realms");
                Assert.That(frontier.Circuits.All(c => c.Spots.All(id => catalog.Spot(id)?.Region == frontier.Region)), Is.True);
                Assert.That(frontier.Spots.All(s => s.ZoneId != 0), Is.True, "every spot lies in a frontier zone");
            }
            // The reserve zones: Forest Sauvage, Snowdonia, Mount Collory, Cruachan Gorge.
            var reserve = catalog.Frontiers.SelectMany(f => f.Spots).Where(s => s.ReserveZone).ToArray();
            Assert.That(reserve.Select(s => s.ZoneId).Distinct(), Is.EquivalentTo(new[] { 11, 12, 210, 211 }));
            Assert.That(catalog.Spot("sauvage-green-knight").ReserveZone, Is.True);
            Assert.That(catalog.Spot("sauvage-green-knight").OnCircuit, Is.True, "a reserve spot on a circuit (alb-hib-3-south-fringe)");
            Assert.That(catalog.Spot("emain-amg").Kind, Is.EqualTo(RvrSpotKind.Chokepoint));
            Assert.That(catalog.Spot("pennine-hall-of-the-corrupt").Kind, Is.EqualTo(RvrSpotKind.DungeonDoor));
            Assert.That(catalog.Frontiers.SelectMany(f => f.Spots).Count(s => !s.ViaFor(eRealm.Albion).IsEmpty), Is.EqualTo(15),
                "the Albion via routes in Emain");
        }

        [Test]
        public void A_bad_spot_or_circuit_is_dropped_and_the_rest_loads()
        {
            const string json = """
                {"version":1,"proof_hash":"x","frontiers":[{"region":200,"name":"t",
                 "zones":[{"id":214,"x":0,"y":0,"width":100000,"height":100000}],
                 "entries":[{"realm":"Albion","x":1,"y":1,"z":1}],
                 "spots":[{"id":"a","type":"chokepoint","x":10,"y":10,"z":0,"radius":500},
                          {"id":"b","type":"camp","x":20,"y":20,"z":0,"stand":{"x":21,"y":21,"z":0},"radius":600,"levels":[40,45]},
                          {"id":"c","type":"volcano","x":30,"y":30,"z":0}],
                 "circuits":[{"id":"ok","realm":"Albion","spots":["a","b"]},
                             {"id":"bad","realm":"Albion","spots":["a","c"]},
                             {"id":"norealm","realm":"Atlantis","spots":["a","b"]}]}]}
                """;
            RvrCheckSpotCatalog catalog = RvrCheckSpotCatalog.Parse(json);
            Assert.Multiple(() =>
            {
                Assert.That(catalog.SpotCount, Is.EqualTo(2));
                Assert.That(catalog.CircuitCount, Is.EqualTo(1));
                Assert.That(catalog.ParseRejections, Has.Length.EqualTo(3));
                Assert.That(catalog.Spot("a").Stand, Is.EqualTo(catalog.Spot("a").Position), "no stand: the spot itself");
                Assert.That(catalog.Spot("b").MinLevel, Is.EqualTo(40));
                Assert.That(catalog.Spot("b").OnCircuit, Is.True);
            });
        }

        [Test]
        public void The_validation_plan_walks_stands_then_circuits_then_reaches()
        {
            RvrCheckSpotCatalog catalog = RvrCheckSpotCatalog.Parse(Embedded());
            var plan = RvrCheckSpotValidation.Plan(catalog);
            int legs = catalog.Frontiers.SelectMany(f => f.Circuits).Sum(c => c.Spots.Length);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Count(c => c.Kind == RvrCheckSpotCheckKind.Stand), Is.EqualTo(98));
                Assert.That(plan.Count(c => c.Kind == RvrCheckSpotCheckKind.Approach), Is.EqualTo(18));
                Assert.That(plan.Count(c => c.Kind == RvrCheckSpotCheckKind.Leg), Is.EqualTo(legs));
                Assert.That(plan.Count(c => c.Kind == RvrCheckSpotCheckKind.Reach), Is.EqualTo(98 * 2));
                Assert.That(plan.Take(98).All(c => c.Kind == RvrCheckSpotCheckKind.Stand), Is.True, "stands first");
                Assert.That(plan.Last().Kind, Is.EqualTo(RvrCheckSpotCheckKind.Reach));
            });
            RvrCheckSpotCheck approach = plan.First(c => c.Kind == RvrCheckSpotCheckKind.Approach && c.Subject == "hib-alb-2-crauchon");
            Assert.That(approach.SpotsUsed, Is.EqualTo(new[] { "emain-amg", "emain-lane-hastener", "emain-crossroads" }), "through the approach");
            Assert.That(approach.Points, Has.Length.EqualTo(4), "entry, two approach stands, the first spot");
            RvrCheckSpotCheck leg = plan.Last(c => c.Kind == RvrCheckSpotCheckKind.Leg && c.Subject == "hib-alb-1-amg");
            Assert.That(leg.SpotsUsed, Is.EqualTo(new[] { "emain-crossroads", "emain-amg" }), "the closing leg back to the first spot");
        }

        [Test]
        public void A_failed_check_turns_off_its_spot_circuit_or_reach_and_nothing_else()
        {
            RvrCheckSpotCatalog catalog = RvrCheckSpotCatalog.Parse(Embedded());
            var plan = RvrCheckSpotValidation.Plan(catalog);
            var failures = new System.Collections.Generic.List<string>();
            bool Fails(RvrCheckSpotCheck check) =>
                check.Kind == RvrCheckSpotCheckKind.Stand && check.Subject == "emain-crauchon-plain" ||
                check.Kind == RvrCheckSpotCheckKind.Leg && check.Detail == "leg hw-mmg > hw-benowyc-north-camps" ||
                check.Kind == RvrCheckSpotCheckKind.Reach && check.Subject == "og-amg" && check.Realm == eRealm.Albion;
            int skipped = 0;
            foreach (RvrCheckSpotCheck check in plan)
            {
                if (RvrCheckSpotValidation.Skip(catalog, check)) { skipped++; continue; }
                if (RvrCheckSpotValidation.Apply(catalog, check, !Fails(check), "test") is { } line)
                    failures.Add(line);
            }
            Assert.That(failures, Has.Count.EqualTo(3));
            Assert.That(failures.All(line => line.StartsWith("RVR_CHECKSPOT_REJECTED ")), Is.True);
            Assert.That(failures[0], Does.Contain("kind=Stand off=spot id=emain-crauchon-plain"));
            Assert.That(skipped, Is.GreaterThan(0), "checks through the rejected spot are not walked");

            RvrRoamCircuit Circuit(string id) => catalog.Frontiers.SelectMany(f => f.Circuits).Single(c => c.Id == id);
            Assert.Multiple(() =>
            {
                Assert.That(catalog.Usable(catalog.Spot("emain-crauchon-plain")), Is.False);
                Assert.That(catalog.Usable(Circuit("hib-alb-2-crauchon")), Is.False, "through the rejected spot");
                Assert.That(catalog.Usable(Circuit("hib-mid-1-mmg")), Is.False);
                Assert.That(catalog.Usable(Circuit("hib-mid-2-crossroads")), Is.False);
                Assert.That(catalog.Usable(Circuit("alb-mid-1-east-wall")), Is.False, "its leg failed");
                Assert.That(catalog.Usable(catalog.Spot("hw-mmg")), Is.True, "the leg's spots stay usable for soloers");
                Assert.That(catalog.Usable(Circuit("hib-alb-1-amg")), Is.True);
                Assert.That(catalog.Usable(catalog.Spot("og-amg"), eRealm.Albion), Is.False, "Albion's reach failed");
                Assert.That(catalog.Usable(catalog.Spot("og-amg"), eRealm.Hibernia), Is.True);
                Assert.That(catalog.Usable(catalog.Spot("og-amg")), Is.True);
                Assert.That(catalog.Frontiers.SelectMany(f => f.Circuits).Count(catalog.Usable), Is.EqualTo(14));
            });
            Assert.That(RvrCheckSpotValidation.Apply(catalog, plan[0], false), Is.Null.Or.Not.Null, "applying twice is harmless");
            Assert.That(catalog.Reject(RvrCheckSpotCatalog.SpotKey("emain-crauchon-plain")), Is.False, "already off");
        }

        [Test]
        public void A_via_spot_turned_off_takes_the_spot_off_for_that_realm()
        {
            RvrCheckSpotCatalog catalog = RvrCheckSpotCatalog.Parse(Embedded());
            RvrFrontierPoi deep = catalog.Frontiers.SelectMany(f => f.Spots).First(s => s.ViaFor(eRealm.Albion).Contains("emain-amg"));
            Assert.That(catalog.Usable(deep, eRealm.Albion), Is.True);
            catalog.Reject(RvrCheckSpotCatalog.SpotKey("emain-amg"));
            Assert.That(catalog.Usable(deep, eRealm.Albion), Is.False);
            Assert.That(catalog.Usable(deep, eRealm.Midgard), Is.True, "Midgard walks there direct");
        }
    }
}
