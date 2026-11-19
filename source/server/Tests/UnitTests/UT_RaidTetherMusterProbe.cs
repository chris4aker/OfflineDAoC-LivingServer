using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidTetherReach;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool and proof: the musters and staging rings the tether rules pick (RealmRaidTetherReach), on the PC's real navmeshes.
    /// Each dungeon muster is placed by the game's own planner (RealmRaidDungeonRoute.PlanMuster) from the dungeon's way in,
    /// then checked: clear of every other boss's aggro and the rules' dangers, inside the reach of every boss of the fight, a
    /// complete walk from the way in, and a walk on to the encounter. Each moved open-world ring is placed by
    /// RealmRaidStaging.TryDragonPost with its out-of-reach inner radius and the monsters' clearance. Fails when one does not.
    /// Env: OFFLINE_DAOC_NAV_ROOT (folder with navmesh/ and lib/), OFFLINE_DAOC_MOBS (the world's spawns, one per line:
    /// region|class|name|x|y|z|aggroRange|aggroLevel|roamingRange|realm), OFFLINE_DAOC_PROOF_OUT (the table).
    /// Read only; nothing here is a game check (tests only).
    /// </summary>
    [TestFixture, Explicit("Navmesh probe: needs the PC's navmeshes"), NonParallelizable]
    public class UT_RaidTetherMusterProbe
    {
        private sealed record Mob(ushort Region, string Class, string Name, Vector3 At, int Aggro, int AggroLevel, int Roam, int Realm)
        {
            public string Type => Class.Contains('.') ? Class[(Class.LastIndexOf('.') + 1)..] : Class;
            public bool IsEpicBoss => typeof(GameEpicBoss).Assembly.GetType(Class) is { } t && typeof(GameEpicBoss).IsAssignableFrom(t);
        }

        /// <summary>A dungeon muster to place: the encounter (class), its region and the way in (a zone point's arrival).</summary>
        private sealed record DungeonCase(string Dungeon, ushort Region, string Boss, Vector3 WayIn);

        private static readonly Vector3 SummonersHib = new(41144, 44332, 16512), SummonersAlb = new(37690, 29013, 16512),
            SummonersMid = new(24912, 30905, 16511), CorruptAlb = new(32395, 31847, 16158), CorruptFromHall = new(26428, 37869, 13478),
            MarfachHib = new(32915, 29238, 16268), MarfachFromHall = new(32125, 43898, 14725);

        private static IEnumerable<DungeonCase> DungeonCases()
        {
            yield return new("Tuscaran Glacier", 160, "QueenKula", new(35612, 17899, 19050));
            foreach (var (way, at) in new[] { ("Hib", SummonersHib), ("Alb", SummonersAlb), ("Mid", SummonersMid) })
                foreach (string boss in new[] { "SummonerRoesia", "SummonerCunovinda", "SummonerLossren" })
                    yield return new($"Summoner's Hall ({way})", 248, boss, at);
            foreach (var (way, at) in new[] { ("Alb", CorruptAlb), ("from the Hall", CorruptFromHall) })
                foreach (string boss in new[] { "AidonTheArchwizard", "CaptainAtwell", "CaptainBardalph", "CaptainHeathyr", "LadyDarra", "LordGildas" })
                    yield return new($"Hall of the Corrupt ({way})", 277, boss, at);
            foreach (var (way, at) in new[] { ("Hib", MarfachHib), ("from the Hall", MarfachFromHall) })
                // The Red and Blue Lady have no spawn in the PC's world (no mob row, no script spawns them): nothing to muster for.
                foreach (string boss in new[] { "BlackLady", "Avartack", "ChieftainCaimheul" })
                    yield return new($"Marfach Caverns ({way})", 276, boss, at);
            yield return new("Galladoria", 191, "EarthPrimal", new(32072, 29524, 17051));
            yield return new("Caer Sidi", 60, "Silencer", new(31666, 35982, 18639));
        }

        private string _root, _prior;
        private List<Mob> _mobs;

        [SetUp]
        public void SetUp()
        {
            _root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string mobs = Environment.GetEnvironmentVariable("OFFLINE_DAOC_MOBS");
            Assume.That(_root != null && mobs != null && File.Exists(mobs));
            _mobs = File.ReadAllLines(mobs).Select(l => l.Split('|')).Where(v => v.Length >= 10).Select(v => new Mob(ushort.Parse(v[0]), v[1], v[2],
                new(float.Parse(v[3]), float.Parse(v[4]), float.Parse(v[5])), int.Parse(v[6]), int.Parse(v[7]), int.Parse(v[8]), int.Parse(v[9]))).ToList();
            // Spawned by a script, not a mob row: the Earth primal (Olcasgean's script, OlcasgeanAndPrimals.cs; its brain's aggro 1,500).
            _mobs.Add(new Mob(191, "DOL.GS.EarthPrimal", "Corrupt Primal of Earth", new(39727, 62620, 11684), 1500, 100, 0, 0));
            _prior = Environment.CurrentDirectory;
            Environment.CurrentDirectory = _root;
        }

        [TearDown]
        public void TearDown()
        {
            if (_prior != null) Environment.CurrentDirectory = _prior;
        }

        private static void Write(List<string> lines)
        {
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            if (output != null) File.AppendAllLines(output, lines);
            TestContext.Progress.WriteLine(string.Join(Environment.NewLine, lines));
        }

        private static int Tether(string type) => T.Leashes.FirstOrDefault(l => l.Boss == type)?.TetherRange ?? 0;

        [Test]
        public void EveryMusterTheTetherRulesPickIsWalkableAndInReach()
        {
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string> { "| Dungeon | Boss | stand-off | muster | how | reach (dist/reach) | clear | walk from way in | walk to boss |", "|---|---|---|---|---|---|---|---|---|" };
            var failures = new List<string>();
            foreach (var group in DungeonCases().GroupBy(c => c.Region))
            {
                // A dungeon is one zone of 65,536 at 8,192 (its Zones row).
                var zone = new Zone(null, group.Key, $"zone{group.Key}", 8192, 8192, 65536, 65536, group.Key, false, 0, false, 0, 0, 0, 0, 0);
                LocalPathfindingMgr.LoadNavMesh(zone);
                try
                {
                    Assert.That(nav.HasNavmesh(zone), Is.True, $"zone{group.Key:000}.nav");
                    // Olcasgean stands CANTTARGET and PEACE (Flags 156, his AddToWorld) until his primals are dead: no danger to a
                    // muster (UntargetedBossDangers skips such flags).
                    List<Mob> bosses = _mobs.Where(m => m.Region == group.Key && m.IsEpicBoss && m.Realm == 0 && m.Type != "Olcasgean").ToList();
                    foreach (DungeonCase c in group)
                    {
                        Mob target = bosses.FirstOrDefault(m => m.Type == c.Boss);
                        if (target == null) { failures.Add($"{c.Boss}: no spawn in region {c.Region}"); continue; }
                        // The fight's partners (the catalog's "after"), as RealmRaidDungeonRoute.FightPartners.
                        string first = T.For(c.Boss)?.After;
                        List<Mob> fight = bosses.Where(m => m == target || T.For(m.Type)?.After == c.Boss || first != null && m.Type == first).ToList();
                        R.BossReach[] reaches = fight.Where(m => R.DamageReach(Tether(m.Type)) > 0)
                            .Select(m => new R.BossReach(m.Name, m.At, R.DamageReach(Tether(m.Type)))).ToArray();
                        float standOff = R.MusterStandOff(RealmRaidDungeonRoute.FinalMusterStandOff, target.Aggro, R.DamageReach(Tether(c.Boss)));
                        // Every other boss's aggro (the partner's too), as UntargetedBossDangers, and the rules' dangers.
                        var dangers = bosses.Where(m => m != target && m.Aggro > 0)
                            .Select(m => new RealmRaidDungeonRoute.BossDanger(m.Name, m.At, m.Aggro + RealmRaidDungeonRoute.UntargetedBossMargin)).ToList();
                        if (c.Region == 160 && bosses.Any(m => m.Type == "QueenKula"))
                            dangers.Add(new("Queen Kula (frost circle)", T.KulaFrostPoint, T.KulaFrostMusterRadius));
                        if (c.Region == 60 && bosses.Any(m => m.Type == "CryptLord"))
                            dangers.AddRange(T.CryptLordPatrolSamples.Select(p => new RealmRaidDungeonRoute.BossDanger("Crypt Lord (patrol)", p, T.CryptLordMusterClearance)));
                        Vector3? front = nav.GetClosestPoint(zone, target.At, 256, 256, 512, nav.DefaultFilters);
                        Vector3? wayIn = nav.GetClosestPoint(zone, c.WayIn, 256, 256, 512, nav.DefaultFilters);
                        if (front == null || wayIn == null) { failures.Add($"{c.Dungeon} {c.Boss}: no floor at the boss or the way in"); continue; }
                        RealmRaidDungeonRoute.MusterPlan plan = RealmRaidDungeonRoute.PlanMuster(nav, zone, front.Value, wayIn.Value, target.At,
                            standOff, dangers, reaches, target.Aggro);
                        Vector3 m0 = plan.Point;
                        bool inReach = !R.OutOfReach(m0, reaches);
                        bool clear = !RealmRaidDungeonRoute.InBossDanger(m0, dangers);
                        bool fromWayIn = AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, wayIn.Value, m0);
                        bool toBoss = AutonomousZonePointApproach.TryResolve(nav, zone, m0, front.Value, 160, out _);
                        string reach = string.Join(", ", fight.Select(m => $"{m.Name} {Vector3.Distance(m0, m.At):0}/{R.DamageReach(Tether(m.Type))}"));
                        string how = plan.By == null ? "walk" : plan.Unsafe ? "UNSAFE" : plan.How.StartsWith("no point") ? "ring" : "walk back";
                        lines.Add($"| {c.Dungeon} | {target.Name} | {standOff:0} | {(int)m0.X},{(int)m0.Y},{(int)m0.Z} | {how} | {reach} | {clear} | {fromWayIn} | {toBoss} |");
                        if (!inReach || !clear || !fromWayIn || !toBoss || plan.Unsafe)
                            failures.Add($"{c.Dungeon} {target.Name}: muster {m0} inReach={inReach} clear={clear} fromWayIn={fromWayIn} toBoss={toBoss} " +
                                         $"unsafe={plan.Unsafe} by=\"{plan.By}\" chosen={plan.Chosen}");
                    }
                }
                finally { LocalPathfindingMgr.UnloadNavMesh(zone); }
            }
            Write(lines);
            Write(failures.Select(f => "FAIL " + f).ToList());
            Assert.That(failures, Is.Empty);
        }

        /// <summary>An open-world raid whose ring the rule moved: its zone (offsets in world units) and its boss class.</summary>
        private sealed record RingCase(string Raid, ushort Region, string Boss, ushort Zone, int OffsetX, int OffsetY, int Slots);

        private static IEnumerable<RingCase> RingCases() =>
        [
            new("Marfoirwort", 181, "Marfoirwort", 184, 34 * 8192, 32 * 8192, 10),
            new("Curengkur", 181, "Curengkur", 187, 42 * 8192, 20 * 8192, 10),
            new("Coliath", 51, "Colialt", 55, 36 * 8192, 52 * 8192, 16),
            new("Juggernaut", 200, "EpicJuggernaut", 208, 55 * 8192, 59 * 8192, 10),
            new("Glacier Giant", 100, "GlacierGiant", 115, 72 * 8192, 70 * 8192, 12),
            new("Mortufoghus", 151, "Mortufoghus", 158, 52 * 8192, 40 * 8192, 10),
            new("Raumarik Revenant", 100, "RaumarikRevenant", 108, 72 * 8192, 90 * 8192, 10),
        ];

        [Test]
        public void EveryMovedOpenWorldRingPlacesItsPosts()
        {
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string> { "| Raid | inner | posts out of reach | nearest post | fallback (3,600 ring) |", "|---|---|---|---|---|" };
            var failures = new List<string>();
            foreach (RingCase c in RingCases())
            {
                var zone = new Zone(null, c.Zone, $"zone{c.Zone}", c.OffsetX, c.OffsetY, 65536, 65536, c.Zone, false, 0, false, 0, 0, 0, 0, 0);
                LocalPathfindingMgr.LoadNavMesh(zone);
                try
                {
                    Mob boss = _mobs.First(m => m.Region == c.Region && m.Type == c.Boss);
                    float inner = R.OpenWorldStagingInner(Tether(c.Boss), boss.Aggro, Math.Max(0, boss.Roam));
                    // AutonomousRealmRaid.RingMonsters: the aggressive monsters (not a realm's) within the ring and 3,000 more.
                    R.RingMonster[] monsters = _mobs.Where(m => m.Region == c.Region && m != boss && m.Realm == 0 && m.AggroLevel > 0 && m.Aggro > 0 &&
                                                                Vector3.Distance(m.At, boss.At) < inner + 3000)
                        .Select(m => new R.RingMonster(m.At, m.Aggro, Math.Max(0, m.Roam))).ToArray();
                    var occupied = new List<Vector3>();
                    int placed = 0, fallback = 0;
                    for (int slot = 0; slot < c.Slots; slot++)
                    {
                        if (RealmRaidStaging.TryDragonPost(nav, zone, boss.At, slot, occupied, out Vector3 post, inner, p => R.ClearOfMonsters(p, monsters)))
                        {
                            occupied.Add(post);
                            placed++;
                            Assert.That(Vector3.Distance(post, boss.At), Is.GreaterThanOrEqualTo(inner - 200), $"{c.Raid} slot {slot}");
                        }
                        else if (RealmRaidStaging.TryDragonPost(nav, zone, boss.At, slot, occupied, out Vector3 old))
                        {
                            occupied.Add(old);
                            fallback++;
                        }
                    }
                    float nearest = occupied.Count == 0 ? -1 : occupied.Min(p => Vector3.Distance(p, boss.At));
                    lines.Add($"| {c.Raid} | {inner:0} | {placed}/{c.Slots} | {nearest:0} | {fallback} |");
                    if (placed < c.Slots) failures.Add($"{c.Raid}: {placed} of {c.Slots} posts out of reach ({fallback} on the 3,600 ring), monsters={monsters.Length}");
                }
                finally { LocalPathfindingMgr.UnloadNavMesh(zone); }
            }
            Write(lines);
            Write(failures.Select(f => "FAIL " + f).ToList());
            Assert.That(failures, Is.Empty);
        }
    }
}
