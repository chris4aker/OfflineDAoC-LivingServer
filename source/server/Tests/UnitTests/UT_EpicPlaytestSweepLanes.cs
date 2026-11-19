using System.Linq;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The sweep's start words for the 1.2 overnight run (docs/OVERNIGHT-SWEEP-1.2.md, 2026-11-15): the raid list keeps its
    /// order, <c>parallel</c> runs one lane per realm side by side, and <c>cap=</c> sets the per-raid cap. Tests only: the
    /// parallel sweep has not run on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_EpicPlaytestSweepLanes
    {
        // The night's commands exactly as docs/OVERNIGHT-SWEEP-1.2.md gives them: a start and five adds, each line short
        // enough for the game client's chat line (the whole list is over 800 characters).
        private static readonly string[] NightCommands =
        [
            "/epictest sweep start dragon-midgard,dungeon-hibernia-tursuil,epic-albion parallel cap=360",
            "/epictest sweep add dungeon-midgard-iarnvidiur,summoners-midgard,epic-midgard,summoners-hibernia,epic-hibernia,summoners-albion",
            "/epictest sweep add dungeon-midgard-trollheim,openworld-albion-greenknight,openworld-albion-emthoro,dungeon-albion-avalon,dungeon-albion-crystal",
            "/epictest sweep add openworld-hibernia-juggernaut,openworld-hibernia-curengkur,openworld-hibernia-marfoirwort,openworld-hibernia-mollunero",
            "/epictest sweep add openworld-hibernia-evern,openworld-midgard-glaciergiant,openworld-midgard-wreth-jotun,openworld-midgard-wreth-arachite",
            "/epictest sweep add openworld-midgard-wreth-spider,openworld-midgard-wreth-scorpion,openworld-midgard-wreth-crab,openworld-midgard-revenant,openworld-midgard-mortufoghus",
        ];

        private static readonly string[] Known = AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray();

        private static eRealm RealmOf(string id) => AutonomousRealmRaid.Definitions.First(d => d.Id == id).Realm;

        /// <summary>The lanes the night's commands build, as the sweep's start and adds would (pure parts only).</summary>
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> NightLanes()
        {
            string[] start = NightCommands[0].Split(' ').Skip(3).ToArray();
            Assert.That(EpicPlaytestCommandHandler.TryParseSweepStart(start, out string raids, out _, out bool parallel, out _, out string error), Is.True, error);
            string[] first = EpicPlaytestSweep.Parse(raids, Known, out string[] unknown);
            Assert.That(unknown, Is.Empty, NightCommands[0]);
            var lanes = EpicPlaytestSweep.SplitLanes(first, RealmOf, parallel)
                .ToDictionary(l => l.Name, l => l.Ids.ToList());
            foreach (string line in NightCommands.Skip(1))
            {
                string[] words = line.Split(' ');
                Assert.That(words.Length, Is.EqualTo(4), $"one list, no spaces: {line}");
                string[] ids = EpicPlaytestSweep.Parse(words[3], Known, out unknown);
                Assert.That(unknown, Is.Empty, line);
                foreach ((string lane, string id) in EpicPlaytestSweep.PlanAdd(ids, lanes.Values.SelectMany(v => v).ToArray(), "sweep", RealmOf,
                             parallel, out _))
                {
                    if (!lanes.TryGetValue(lane, out var list)) lanes[lane] = list = [];
                    list.Add(id);
                }
            }
            return lanes;
        }

        [Test]
        public void TheNightsCommands_FitAChatLine() =>
            Assert.That(NightCommands.Select(c => c.Length), Is.All.LessThanOrEqualTo(170));

        [Test]
        public void TheNightsCommands_BuildOneLanePerRealm_BarFirst_ThenTheBigThree_ThenTheOpenWorld()
        {
            var lanes = NightLanes();
            Assert.That(lanes.Keys, Is.EqualTo(new[] { "Midgard", "Hibernia", "Albion" }), "in the order the realms first appear");
            Assert.Multiple(() =>
            {
                Assert.That(lanes["Midgard"], Is.EqualTo(new[]
                {
                    "dragon-midgard", "dungeon-midgard-iarnvidiur", "summoners-midgard", "epic-midgard", "dungeon-midgard-trollheim",
                    "openworld-midgard-glaciergiant", "openworld-midgard-wreth-jotun", "openworld-midgard-wreth-arachite",
                    "openworld-midgard-wreth-spider", "openworld-midgard-wreth-scorpion", "openworld-midgard-wreth-crab",
                    "openworld-midgard-revenant", "openworld-midgard-mortufoghus",
                }));
                Assert.That(lanes["Hibernia"], Is.EqualTo(new[]
                {
                    "dungeon-hibernia-tursuil", "summoners-hibernia", "epic-hibernia", "openworld-hibernia-juggernaut",
                    "openworld-hibernia-curengkur", "openworld-hibernia-marfoirwort", "openworld-hibernia-mollunero", "openworld-hibernia-evern",
                }));
                Assert.That(lanes["Albion"], Is.EqualTo(new[]
                {
                    "epic-albion", "summoners-albion", "openworld-albion-greenknight", "openworld-albion-emthoro",
                    "dungeon-albion-avalon", "dungeon-albion-crystal",
                }));
                foreach (var (name, ids) in lanes)
                    Assert.That(ids.Select(RealmOf).Select(r => GlobalConstants.RealmToName(r)), Is.All.EqualTo(name), $"{name}: one realm's bots only");
                Assert.That(lanes.Values.Sum(v => v.Count), Is.EqualTo(27));
                Assert.That(lanes.Values.SelectMany(v => v), Is.Unique);
                Assert.That(lanes.Values.SelectMany(v => v).Where(RealmRaidOpenWorld.Is).Count(), Is.EqualTo(15),
                    "every open-world boss but Coliath (won on 1dc66b6)");
            });
        }

        [Test]
        public void Add_LeavesOutWhatTheSweepAlreadyHolds()
        {
            var plan = EpicPlaytestSweep.PlanAdd(["epic-albion", "dragon-midgard", "summoners-albion"], ["epic-albion"], "sweep", RealmOf,
                parallel: true, out string[] skipped);
            Assert.That(skipped, Is.EqualTo(new[] { "epic-albion" }));
            Assert.That(plan, Is.EqualTo(new[] { ("Midgard", "dragon-midgard"), ("Albion", "summoners-albion") }));
            plan = EpicPlaytestSweep.PlanAdd(["dragon-midgard", "summoners-albion"], [], "sweep", RealmOf, parallel: false, out _);
            Assert.That(plan.Select(p => p.Lane), Is.All.EqualTo("sweep"), "one lane: every raid in it");
        }

        [Test]
        public void TheNightsListParsesAsOneComma_ListToo()
        {
            string all = string.Join(",", NightCommands.Select(c => c.Split(' ')[3]));
            string[] order = EpicPlaytestSweep.Parse(all, Known, out string[] unknown);
            Assert.That(unknown, Is.Empty);
            Assert.That(order, Is.EqualTo(all.Split(',')), "a comma list runs in the order it is given");
        }

        [Test]
        public void NotParallel_IsOneLane_TheOldSweep()
        {
            string[] order = EpicPlaytestSweep.Parse("all", Known, out _);
            var lanes = EpicPlaytestSweep.SplitLanes(order, RealmOf, parallel: false);
            Assert.That(lanes.Length, Is.EqualTo(1));
            Assert.That(lanes[0].Ids, Is.EqualTo(order));
        }

        [Test]
        public void Parallel_DarknessFallsStaysLastInItsLane()
        {
            string[] order = EpicPlaytestSweep.Parse("darkness-albion,epic-albion,dragon-midgard,summoners-albion", Known, out _);
            var lanes = EpicPlaytestSweep.SplitLanes(order, RealmOf, parallel: true);
            Assert.That(lanes.Single(l => l.Name == "Albion").Ids, Is.EqualTo(new[] { "epic-albion", "summoners-albion", "darkness-albion" }));
        }

        [TestCase(null, EpicPlaytestSweep.RaidCapMs, TestName = "no cap= : the four hours as before")]
        [TestCase("off", 0L, TestName = "off: no cap, the raid's own clocks")]
        [TestCase("OFF", 0L, TestName = "off in capitals")]
        [TestCase("360", 360 * 60_000L, TestName = "minutes")]
        [TestCase("30", 30 * 60_000L, TestName = "the smallest cap")]
        public void Cap_Parses(string word, long expected)
        {
            Assert.That(EpicPlaytestSweep.TryParseCap(word, out long capMs), Is.True);
            Assert.That(capMs, Is.EqualTo(expected));
        }

        [TestCase("29")]
        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("6h")]
        [TestCase("")]
        public void Cap_RefusesWhatIsNotMinutesOrOff(string word) =>
            Assert.That(EpicPlaytestSweep.TryParseCap(word, out _), Is.False);

        [Test]
        public void CapReached_ZeroNeverStops() =>
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestSweep.CapReached(0, 48 * 60 * 60_000L), Is.False);
                Assert.That(EpicPlaytestSweep.CapReached(EpicPlaytestSweep.RaidCapMs, EpicPlaytestSweep.RaidCapMs - 1), Is.False);
                Assert.That(EpicPlaytestSweep.CapReached(EpicPlaytestSweep.RaidCapMs, EpicPlaytestSweep.RaidCapMs), Is.True);
            });

        [Test]
        public void StartWords_FlagsInAnyOrder()
        {
            Assert.That(EpicPlaytestCommandHandler.TryParseSweepStart(["cap=360", "dragon-midgard,epic-albion", "parallel"],
                out string raids, out bool guest, out bool parallel, out long capMs, out string error), Is.True, error);
            Assert.Multiple(() =>
            {
                Assert.That(raids, Is.EqualTo("dragon-midgard,epic-albion"));
                Assert.That(guest, Is.False);
                Assert.That(parallel, Is.True);
                Assert.That(capMs, Is.EqualTo(360 * 60_000L));
            });
        }

        [Test]
        public void StartWords_TheOldFormsStillWork()
        {
            Assert.That(EpicPlaytestCommandHandler.TryParseSweepStart([], out string raids, out bool guest, out bool parallel, out long capMs, out _), Is.True);
            Assert.That((raids, guest, parallel, capMs), Is.EqualTo(("all", false, false, EpicPlaytestSweep.RaidCapMs)));
            Assert.That(EpicPlaytestCommandHandler.TryParseSweepStart(["guest", "openworld"], out raids, out guest, out parallel, out _, out _), Is.True);
            Assert.That((raids, guest, parallel), Is.EqualTo(("openworld", true, false)));
        }

        [Test]
        public void StartWords_RefuseASecondListAndABadCap() =>
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestCommandHandler.TryParseSweepStart(["dragon-midgard,", "epic-albion"], out _, out _, out _, out _, out string error), Is.False,
                    "a space in the list is a second list");
                Assert.That(error, Does.Contain("without spaces"));
                Assert.That(EpicPlaytestCommandHandler.TryParseSweepStart(["all", "cap=5"], out _, out _, out _, out _, out error), Is.False);
                Assert.That(error, Does.Contain("cap=5"));
            });

        [Test]
        public void AParallelSweepRefusesAGuest() =>
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestSweep.GuestRefusal(parallel: true, guest: true), Does.StartWith("A parallel sweep runs bots only"));
                Assert.That(EpicPlaytestSweep.GuestRefusal(parallel: true, guest: false), Is.Null);
                Assert.That(EpicPlaytestSweep.GuestRefusal(parallel: false, guest: true), Is.Null);
            });

        private static (eRealm, ushort) Of(string id)
        {
            var d = AutonomousRealmRaid.Definitions.First(x => x.Id == id);
            return (d.Realm, d.Region);
        }

        [Test]
        public void ALaneNeverSendsItsRaidIntoADungeonAnotherRealmsRaidHolds() =>
            Assert.Multiple(() =>
            {
                string[] midgard = ["summoners-midgard", "epic-midgard", "dungeon-midgard-trollheim"];
                (string, eRealm, ushort)[] hibInSummoners = [("summoners-hibernia", eRealm.Hibernia, 248)];
                Assert.That(EpicPlaytestSweep.NextStartable(midgard, Of, []), Is.EqualTo(0), "nothing running: the lane's order");
                Assert.That(EpicPlaytestSweep.NextStartable(midgard, Of, hibInSummoners), Is.EqualTo(1),
                    "Hibernia in Summoner's Hall: Tuscaran Glacier goes first, Summoner's Hall waits its turn");
                Assert.That(EpicPlaytestSweep.NextStartable(["summoners-midgard"], Of, hibInSummoners), Is.EqualTo(-1), "nothing else: the lane waits");
                Assert.That(EpicPlaytestSweep.NextStartable(midgard, Of, [("dragon-midgard", eRealm.Midgard, 100)]), Is.EqualTo(0),
                    "its own realm elsewhere: no hold");
                Assert.That(EpicPlaytestSweep.NextStartable(["darkness-albion"], Of, [("darkness-hibernia", eRealm.Hibernia, 249)]), Is.EqualTo(-1),
                    "Darkness Falls too");
                Assert.That(EpicPlaytestSweep.HeldLine(midgard, 1, hibInSummoners),
                    Is.EqualTo("event=summoners-midgard region=248 by=summoners-hibernia next=epic-midgard"));
            });

        [Test]
        public void OnlySummonersHallAndDarknessFallsAreSharedByTheNightsLanes()
        {
            var lanes = NightLanes();
            var shared = lanes.Values.SelectMany(v => v).GroupBy(id => Of(id).Item2)
                .Where(g => g.Select(id => Of(id).Item1).Distinct().Count() > 1).Select(g => (int)g.Key).ToArray();
            Assert.That(shared, Is.EqualTo(new[] { 248 }), "the night's only cross-realm dungeon is Summoner's Hall (region 248)");
        }

        [Test]
        public void Skip_NamesItsRaidWhenSeveralRun() =>
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestSweep.SkipTarget([], null, out string why), Is.Null);
                Assert.That(why, Is.EqualTo("No sweep raid is running."));
                Assert.That(EpicPlaytestSweep.SkipTarget(["epic-albion"], null, out _), Is.EqualTo("epic-albion"));
                Assert.That(EpicPlaytestSweep.SkipTarget(["epic-albion", "epic-midgard"], null, out why), Is.Null);
                Assert.That(why, Does.Contain("/epictest sweep skip <event>"));
                Assert.That(EpicPlaytestSweep.SkipTarget(["epic-albion", "epic-midgard"], "epic-midgard", out _), Is.EqualTo("epic-midgard"));
                Assert.That(EpicPlaytestSweep.SkipTarget(["epic-albion"], "epic-hibernia", out why), Is.Null);
                Assert.That(why, Does.Contain("not a sweep raid running now"));
            });
    }
}
