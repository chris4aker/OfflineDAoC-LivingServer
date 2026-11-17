using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidDungeonRoute;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// The review of 27abe6e78 (2026-11-17): the raid's own resting target was a danger, so a stranded raider of a clear-first
    /// target without a muster (Krondon's Drihten students) was always refused, and a refusal sent it to its capital; a muster
    /// at exactly aggro + 300 (Summoner Roesia's 900) was a coin flip; a rebase counted re-walked steps as headway. Real
    /// coordinates from the PC's world database (playable 0.37b) and the owner's PC logs. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_RaidRegroupReview1117
    {
        private static R.DangerCandidate Npc(string name, Vector3 at, int aggro, bool boss = true, bool pack = false,
            bool ofTarget = false, bool inCombat = false, bool hazard = false, Vector3? standing = null) =>
            new(name, at, standing ?? at, aggro, inCombat, hazard, boss, pack, ofTarget);

        // ---- what goes into the stranded-regroup dangers ---------------------------------------------------------------------

        [Test]
        public void RestingDangersHoldRestingBossesAndPacksButNotTheTargetOrAFightOn()
        {
            var agmundr = new Vector3(15037, 50114, 12408);
            List<R.BossDanger> dangers = R.RestingDangersOf(
            [
                Npc("Icelord Agmundr", agmundr, 600),
                Npc("hrimthursa depredator", new(16418, 49908, 12059), 500, boss: false, pack: true),
                Npc("hrimthursa icespine", new(16899, 49466, 12089), 200, ofTarget: true),
                Npc("Queen Kula", new(34000, 56000, 12000), 600, inCombat: true),
                Npc("frost hazard", new(20000, 40000, 12000), 300, hazard: true),
                Npc("ordinary trash", new(15500, 49000, 12400), 400, boss: false),
                Npc("Hord", new(40000, 40000, 11000), 1_500, standing: new Vector3(40800, 40000, 11000)),
            ]);
            Assert.That(dangers.Where(d => d.Name == "Icelord Agmundr").Select(d => d.Radius), Is.EqualTo(new[] { 900f }), "aggro + 300");
            Assert.That(dangers.Single(d => d.Name == "hrimthursa depredator").Radius, Is.EqualTo(500f), "a linked monster: its own aggro");
            Assert.That(dangers.Any(d => d.Name == "hrimthursa icespine"), Is.False, "the raid's own target");
            Assert.That(dangers.Any(d => d.Name == "Queen Kula"), Is.False, "in combat: a fight already on");
            Assert.That(dangers.Any(d => d.Name == "frost hazard"), Is.False, "a hazard");
            Assert.That(dangers.Any(d => d.Name == "ordinary trash"), Is.False, "neither boss nor linked");
            Assert.That(dangers.Count(d => d.Name == "Hord"), Is.EqualTo(2), "away from his spawn: there and at his spawn");
        }

        // ---- the wiring: clear-first target without a muster, wipe 2 again -------------------------------------------------

        [Test]
        public void KrondonsDrihtenStudentsGoingInStillGetTheFront()
        {
            // The raid goes in at a student (thrawn ogre bocere, DrihtenBaf, aggro 150) 1,173 from Drihten Elreden's spawn; the
            // front is 150 from the student. Drihten (600) and a neighbouring student of his are in the world; no muster.
            var student = new Vector3(38016, 67270, 15117);
            var drihten = new Vector3(37323, 68217, 15102);
            var front = student + new Vector3(150, 0, 0);
            // Who goes with the target is worked out as RestingDangers does: Drihten owns both students (ClearFirstOwner).
            object drihtenNpc = new(), otherBoss = new();
            Assert.That(R.GoesWithTarget(false, false, false, drihtenNpc, drihtenNpc), Is.True, "a fellow student of the target");
            Assert.That(R.GoesWithTarget(false, false, false, otherBoss, drihtenNpc), Is.False, "another boss's student");
            Assert.That(R.GoesWithTarget(false, false, false, null, drihtenNpc), Is.False, "Drihten himself: no owner, a danger");
            Assert.That(R.GoesWithTarget(false, false, false, null, null), Is.False);
            Assert.That(R.GoesWithTarget(true, false, false, null, null) && R.GoesWithTarget(false, true, false, null, null) &&
                        R.GoesWithTarget(false, false, true, null, null), Is.True, "the target, what it covers, its required adds");
            R.DangerCandidate[] world =
            [
                Npc("Drihten Elreden", drihten, 600, ofTarget: R.GoesWithTarget(false, false, false, null, drihtenNpc)),
                Npc("thrawn ogre bocere", student, 150, boss: false, pack: true, ofTarget: R.GoesWithTarget(true, false, false, drihtenNpc, drihtenNpc)),
                Npc("thrawn ogre bocere", new(38023, 67467, 15117), 150, boss: false, pack: true,
                    ofTarget: R.GoesWithTarget(false, false, false, drihtenNpc, drihtenNpc)),
            ];
            Assert.That(R.StrandedRegroupPoint(false, front, front, null, R.RestingDangersOf(world)), Is.EqualTo(front));
            // The first cut counted the target (and its fellow students) as dangers: refused.
            R.DangerCandidate[] firstCut = world.Select(c => c with { OfTheTarget = false }).ToArray();
            List<R.BossDanger> old = firstCut.Select(c => new R.BossDanger(c.Name, c.Spawn, c.AggroRange + R.UntargetedBossMargin)).ToList();
            Assert.That(R.StrandedRegroupPoint(false, front, front, null, old), Is.Null, "the review's M2");
        }

        [Test]
        public void WipeTwoStaysFixedWithTheTargetLeftOut()
        {
            // Target Agmundr (left out): a holding raid still sends them to its muster, never to his front.
            var agmundr = new Vector3(15037, 50114, 12408);
            var front = new Vector3(15037, 49914, 12424);
            var muster = new Vector3(15184, 48849, 12516);
            List<R.BossDanger> dangers = R.RestingDangersOf(
            [
                Npc("Icelord Agmundr", agmundr, 600, ofTarget: true),
                Npc("hrimthursa icespine", new(16899, 49466, 12089), 200, boss: false, pack: true),
                Npc("hrimthursa depredator", new(16418, 49908, 12059), 500, boss: false, pack: true),
                Npc("hrimthursa seer", new(12969, 49582, 12071), 450, boss: false, pack: true),
            ]);
            Assert.That(R.StrandedRegroupPoint(true, front, muster, muster, dangers), Is.EqualTo(muster));
        }

        [Test]
        public void AMusterAtExactlyAggroPlusThreeHundredIsClear()
        {
            // Summoner Roesia: aggro 600, muster stand-off 900 = aggro + 300; the floor snap moves it a little.
            var roesia = new Vector3(30000, 30000, 16000);
            R.BossDanger[] d = [new("Summoner Roesia", roesia, 600 + R.UntargetedBossMargin)];
            foreach (float off in new[] { 900f, 880f, 850f })
            {
                var muster = roesia + new Vector3(off, 0, 0);
                Assert.That(R.InBossDangerStrict(muster, d), Is.False, $"{off} out");
            }
            Assert.That(R.InBossDanger(roesia + new Vector3(900, 0, 0), d), Is.True, "the plain check: a coin flip at the edge");
            Assert.That(R.InBossDangerStrict(roesia + new Vector3(830, 0, 0), d), Is.True, "well inside");
        }

        // ---- the return walk's pieces -----------------------------------------------------------------------------------------

        [Test]
        public void ARebaseKeepsTheBestStepSoReWalkedStepsAreNoHeadway()
        {
            var catching = new Dictionary<long, int> { [1] = 3, [2] = 5, [3] = 5 };
            Assert.That(W.Rebase(catching), Is.EqualTo(5), "the furthest catching up");
            Assert.That(catching, Is.EqualTo(new Dictionary<long, int> { [1] = 3 }), "those at it are the wave now");
            const int bestStep = 34;
            Assert.That(W.Headway(6, bestStep), Is.False, "walking steps 6..34 again holds off no stall verdict");
            Assert.That(W.Headway(35, bestStep), Is.True);
            Assert.That(W.Rebase(new Dictionary<long, int>()), Is.EqualTo(-1), "nobody catching up: the walk ends");
        }

        [Test]
        public void StepsWorkedOutAgainKeepEachRaiderCatchingUpBehindTheWave()
        {
            Vector3[] old = Enumerable.Range(0, 10).Select(i => new Vector3(i * 1_200, 0, 0)).ToArray();
            Vector3[] fresh = Enumerable.Range(0, 12).Select(i => new Vector3(i * 1_000, 0, 0)).ToArray();
            var catching = new Dictionary<long, int> { [1] = 1, [2] = 4, [3] = 8 };
            W.RemapCatching(old, fresh, catching, 7);
            Assert.That(catching[1], Is.EqualTo(1), "1,200 is nearest 1,000");
            Assert.That(catching[2], Is.EqualTo(5), "4,800 is nearest 5,000");
            Assert.That(catching.ContainsKey(3), Is.False, "9,600 is past the wave's new step 7: with the wave");
        }

        [Test]
        public void ARaiderAlreadyWithTheRaidLeavesTheWalk()
        {
            Vector3[] steps = Enumerable.Range(0, 10).Select(i => new Vector3(i * 1_200, 0, 0)).ToArray();
            Assert.That(W.WithTheRaid(steps, 3, steps[^1] + new Vector3(200, 0, 0)), Is.True, "moved to the muster (the last step)");
            Assert.That(W.WithTheRaid(steps, 3, steps[5]), Is.False);
            Assert.That(W.WithTheRaid(steps, 9, steps[^1]), Is.False, "the wave is there itself: it arrives as a wave");
            Assert.That(W.LatestStepAt(steps, steps[6] + new Vector3(300, 0, 0)), Is.EqualTo(6));
            Assert.That(W.LatestStepAt(steps, new(600, 900, 0)), Is.EqualTo(-1), "between steps, off the walk");
        }

        // ---- raids already won: their posts are unchanged ----------------------------------------------------------------------

        private sealed record Boss(string Name, Vector3 At, int Aggro);

        /// <summary>The GameEpicBoss spawns of the won raids' dungeons (world database, playable 0.37b; their brains' aggro).</summary>
        private static readonly Dictionary<ushort, Boss[]> Bosses = new()
        {
            [61] =
            [
                new("Orshom Brong", new(31697, 70458, 15610), 600), new("Scurceol Hyrde", new(51941, 20068, 17174), 600),
                new("Ulor se Bysen", new(28173, 53842, 15098), 600), new("Deben se Gecynde", new(21615, 54280, 14076), 600),
                new("Beran the Supply Master", new(48907, 26990, 17163), 600), new("Drihten Elreden", new(37323, 68217, 15102), 600),
                new("Lurfos the Herald", new(31414, 65541, 15610), 600), new("Fuladl", new(38701, 28678, 12017), 600),
                new("Fulafeallan", new(42353, 45665, 14586), 600), new("Krevo Ricik", new(27189, 64319, 15614), 600),
                new("Jehrog se Bysen", new(28056, 54828, 15104), 600), new("Orylle", new(31413, 70773, 15765), 600),
                new("Morgus Urgalorg", new(44808, 18929, 17164), 600),
            ],
            [62] = [new("Xanxicar", new(31577, 35305, 14551), 600)],
            [50] =
            [
                new("Dra'argus the Mighty", new(26899, 37223, 8644), 300), new("Sys'sro the Ruthless", new(41505, 39496, 8040), 400),
                new("Weno'iak the Enlightened", new(35817, 33952, 8625), 600), new("Dura'ek the Empowered", new(32425, 28962, 8756), 600),
                new("Vera'erius the Brave", new(23752, 28660, 8791), 600),
            ],
            [161] =
            [
                new("Hati", new(13349, 14481, 18114), 600), new("Droom", new(22055, 21795, 14903), 600),
                new("Iarnvidiur", new(13848, 11523, 18263), 600), new("Stanga", new(21537, 32290, 16513), 600),
                new("Delegalt", new(17956, 17650, 17976), 600), new("Ancient Syver", new(25281, 33542, 16513), 600),
                new("Mahattava", new(17361, 29203, 12962), 600), new("Hurjavelen", new(21487, 34669, 16513), 600),
                new("Skoll", new(14985, 14528, 18125), 600),
            ],
            [246] =
            [
                // Thane Dyggve and Gudlaugr have no aggro (0): never a danger.
                new("Aros the Spiritmaster", new(39061, 36970, 15495), 600), new("Jarl Ormarr", new(45432, 37768, 13966), 400),
                new("Hamar", new(32017, 41854, 15484), 550), new("Mistress of Runes", new(29728, 42388, 12912), 500),
            ],
            [276] =
            [
                new("Avartack the Champion", new(28896, 35752, 15065), 400), new("Chieftain Caimheul", new(32597, 38903, 15061), 500),
                new("Black Lady", new(30086, 37378, 15001), 500),
            ],
            [277] =
            [
                new("Lady Darra", new(29551, 31554, 13941), 500), new("Aidon the Archwizard", new(31353, 37634, 14873), 600),
                new("Lord Gildas", new(29015, 41910, 12933), 500), new("Captain Heathyr", new(33731, 35840, 14646), 500),
                new("Captain Atwell", new(33999, 38790, 14614), 500), new("Captain Bardalph", new(34604, 35842, 14134), 500),
            ],
            [190] =
            [
                new("Sargan the Conqueror", new(25249, 38173, 14109), 600), new("Balor", new(32032, 31048, 15984), 600),
                new("Norsob the Annihilator", new(25033, 31235, 14109), 600), new("Abomos the Soultrapper", new(31471, 21474, 11612), 600),
                new("Kierac the Destroyer", new(28439, 28150, 13246), 600), new("Queen Qunilaria", new(31066, 16596, 11951), 600),
            ],
        };

        /// <summary>The musters these raids held at in the owner's PC logs (REALM_EXPEDITION_FINAL_MUSTER at=), with their target.</summary>
        private static IEnumerable<TestCaseData> WonMusters()
        {
            (ushort, string, Vector3)[] musters =
            [
                (61, "Ulor se Bysen", new(29536, 54158, 15099)), (61, "Krevo Ricik", new(27245, 62920, 15626)),
                (61, "Deben se Gecynde", new(22148, 52985, 14076)), (61, "Beran the Supply Master", new(48792, 25594, 17159)),
                (61, "Scurceol Hyrde", new(50541, 20089, 17164)), (61, "Morgus Urgalorg", new(45014, 20313, 17157)),
                (61, "Lurfos the Herald", new(31574, 64150, 15604)), (61, "Fulafeallan", new(42039, 47024, 14711)),
                (61, "Fuladl", new(40094, 28814, 12037)), (61, "Orshom Brong", new(31626, 69059, 15621)),
                (62, "Xanxicar", new(30274, 35520, 15002)),
                (50, "Dura'ek the Empowered", new(33486, 29874, 8724)), (50, "Sys'sro the Ruthless", new(40542, 40512, 8021)),
                (50, "Weno'iak the Enlightened", new(34454, 34275, 8613)),
                (161, "Iarnvidiur", new(14199, 12878, 18262)), (161, "Delegalt", new(19308, 17959, 18158)),
                (161, "Droom", new(22467, 23107, 15156)), (161, "Ancient Syver", new(24518, 34696, 16724)),
                (161, "Skoll", new(14652, 15887, 18086)), (161, "Mahattava", new(17747, 27857, 12984)),
                (161, "Stanga", new(22587, 33210, 16597)),
                (246, "Aros the Spiritmaster", new(37903, 37757, 15493)), (246, "Thane Dyggve", new(36419, 34187, 14064)),
                (246, "Mistress of Runes", new(30382, 41156, 13047)), (246, "Gudlaugr", new(43212, 33301, 13440)),
                (246, "Jarl Ormarr", new(45199, 36387, 13947)),
                (276, "Black Lady", new(30280, 35991, 14980)),
                (277, "Aidon the Archwizard", new(31384, 36234, 14885)), (277, "Captain Atwell", new(32831, 38054, 14376)),
                (277, "Lord Gildas", new(28915, 40513, 12978)),
                (190, "Norsob the Annihilator", new(26431, 31289, 14110)), (190, "Queen Qunilaria", new(30938, 15203, 11889)),
                (190, "Kierac the Destroyer", new(28444, 29548, 13184)), (190, "Balor", new(31986, 32447, 15988)),
                (190, "Sargan the Conqueror", new(26644, 38059, 14110)),
            ];
            foreach (var (region, target, at) in musters)
                yield return new TestCaseData(region, target, at).SetName($"WonRaidPostsUnchanged({region}, {target})");
        }

        /// <summary>
        /// The dungeon raids' musters from the owner's PC logs: Krondon, Crystal Cave and Avalon City (won), Iarnvidiur's Lair,
        /// Dodens Gruva, Marfach Caverns (the Black Lady), Hall of the Corrupt and Tur Suil. The dangers only remove tries of a
        /// party's post, so where no try lies in another boss's danger the post search returns what integration/1.2 returned (the
        /// muster itself is placed by unchanged code). Every try of slots 0-28 (232 raiders) is clear; the only tries removed are
        /// of slots 29-40 (Special Forces' 38-40 included): six at Ulor se Bysen's muster (by Jehrog se Bysen), seven at the
        /// Black Lady's (by Avartack), two at Captain Atwell's (by Aidon). Bosses at their spawn; no rule muster danger lies in
        /// these dungeons (only Tuscaran's frost circle and Caer Sidi's Crypt Lord have one).
        /// </summary>
        [TestCaseSource(nameof(WonMusters))]
        public void WonRaidPostsUnchanged(ushort region, string target, Vector3 muster)
        {
            R.BossDanger[] dangers = Bosses[region].Where(b => b.Name != target)
                .Select(b => new R.BossDanger(b.Name, b.At, b.Aggro + R.UntargetedBossMargin)).ToArray();
            var removed = new List<(int Slot, int Attempt)>();
            for (int slot = 0; slot < RealmRaidRecruitmentPolicy.FormationSlots; slot++)
                for (int attempt = 0; attempt < 22; attempt++)
                    if (R.InBossDangerStrict(RealmRaidFormation.RawPost(muster, slot, attempt), dangers))
                        removed.Add((slot, attempt));
            Assert.That(removed.Where(r => r.Slot < 29), Is.Empty, "no try of the first 29 slots (232 raiders) changes");
            int expected = target switch
            {
                "Ulor se Bysen" => 6,  // slots 32-40, 729-807 from Jehrog se Bysen
                "Black Lady" => 7,     // slots 29-40, by Avartack the Champion
                "Captain Atwell" => 2, // slots 37-40, by Aidon the Archwizard
                _ => 0,
            };
            Assert.That(removed, Has.Count.EqualTo(expected), string.Join(" ", removed));
            Assert.That(R.InBossDangerStrict(muster, dangers), Is.False, "the muster itself");
        }

        // ---- one post search; the refusal stays inside; the watchdog's spot ----------------------------------------------------

        [Test]
        public void APostSearchTellsADangerOnlyFailureInTheSamePass()
        {
            var center = new Vector3(15333, 49257, 12484);
            var agmundr = new Vector3(15037, 50114, 12408);
            int validCalls = 0;
            bool Valid(Vector3 _) { validCalls++; return true; }
            // Every try lies in a danger (a radius that covers them all): no post, but one would have been there.
            bool found = RealmRaidFormation.Resolve(22, center, 6, [], p => p, Valid, _ => true, out _, out bool dangerOnly);
            Assert.That(found, Is.False);
            Assert.That(dangerOnly, Is.True);
            Assert.That(validCalls, Is.EqualTo(1), "one check told it: no second search");
            // The real danger: slot 6 skips its tries by Agmundr and takes a clear one, as one pass.
            validCalls = 0;
            R.BossDanger[] d = [new("Icelord Agmundr", agmundr, 900)];
            found = RealmRaidFormation.Resolve(22, center, 6, [], p => p, Valid, p => R.InBossDangerStrict(p, d), out Vector3 post, out dangerOnly);
            Assert.That(found && !dangerOnly && !R.InBossDangerStrict(post, d), Is.True);
            Assert.That(validCalls, Is.LessThanOrEqualTo(22));
            // Nothing valid anywhere: not a danger-only failure (the party waits as before).
            found = RealmRaidFormation.Resolve(22, center, 6, [], p => p, _ => false, _ => true, out _, out dangerOnly);
            Assert.That(found || dangerOnly, Is.False);
            // No dangers: the first valid try, exactly as integration/1.2 chose it.
            found = RealmRaidFormation.Resolve(22, center, 6, [], p => p, _ => true, null, out post, out _);
            Assert.That(post, Is.EqualTo(RealmRaidFormation.RawPost(center, 6, 0)));
        }

        [Test]
        public void ARefusedRegroupInsideTheDungeonStaysNeverTheCapital()
        {
            Assert.That(RealmRaidStuckRecovery.StaysInside(true, false), Is.True, "inside, refused: stays");
            Assert.That(RealmRaidStuckRecovery.StaysInside(true, true), Is.False, "inside, clear: moved to the regroup point");
            Assert.That(RealmRaidStuckRecovery.StaysInside(false, false), Is.False, "outside its raid's dungeon: as before");
        }

        [Test]
        public void TheWatchdogNeverPutsAStuckRaiderBesideARestingBoss()
        {
            // Holding at Agmundr's muster; one raider stands by the muster, one at his front, 200 from his spawn.
            var muster = new Vector3(15184, 48849, 12516);
            R.BossDanger[] d = [new("Icelord Agmundr", new(15037, 50114, 12408), 900)];
            Vector3[] raiders = [new(15037, 49914, 12424), new(15184, 47400, 12516)];
            Assert.That(RealmRaidStuckRecovery.Choose(new(29505, 27351, 17087), muster, raiders, out _, out int before) && before == 0, Is.True,
                "unguarded: the raider nearest the point, by him (1,090 off against 1,449)");
            Assert.That(RealmRaidStuckRecovery.Choose(new(29505, 27351, 17087), muster, raiders, p => R.InBossDangerStrict(p, d),
                out Vector3 spot, out int index), Is.True);
            Assert.That(index, Is.EqualTo(1), "the one clear of him");
            Assert.That(spot, Is.EqualTo(raiders[1]));
            Assert.That(RealmRaidStuckRecovery.Choose(new(29505, 27351, 17087), muster, [raiders[0]], p => R.InBossDangerStrict(p, d),
                out spot, out index) && index == -1 && spot == muster, Is.True, "nobody clear: the regroup point itself");
        }
    }
}
