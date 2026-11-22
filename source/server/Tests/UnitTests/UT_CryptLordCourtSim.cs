using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// #407 (Caer Sidi: the bot raid loses at the Crypt Lord). A deterministic simulation of a court pull, built on his script
    /// (CryptLord.PatrolPoints, PATROL_SPEED 100; CryptLordBrain: he aggroes raiders within 400 while he walks, and a court
    /// monster fighting within 800 of him pulls him and his whole court) and on the tactic's own pure functions
    /// (PredictPatrol, CourtPullWait, CourtLapsedGuard). Owner's PC, build b1b6846, 2026-11-18: the re-clear held on one court
    /// monster for the tactic's 20 minutes, then the route pulled it with no timing at all (RealmRaidDungeonRoute.HeldByTactic
    /// returned null on the lapse), he came in and the raid ended. The 20-minute lapse stays (the owner's call); after it a
    /// looser guard of the same rule still waits for a moment he cannot join.
    /// <para>The model: the raid walks from its muster to the court monster at 150 (RaidWalkSpeed), its crowd 300 deep; he
    /// joins if he comes within 400 of the crowd while it walks, or within 800 of the court monster for 15 s after the walk
    /// (the pull, and the monster's run after its target to the muster, where the court leash holds the raid). Not modelled:
    /// the navmesh walk (straight legs here), a court monster that does not follow, fights longer than that, his walk back
    /// after a skirmish (MoveOnPath resumes to the previous point at his template speed). Tests only: nothing here is seen
    /// in a PC log.</para>
    /// </summary>
    [TestFixture]
    public sealed class UT_CryptLordCourtSim
    {
        private const float Z = 15373;
        private const float CrowdDepth = 300;

        /// <summary>The court monsters of the logs, each with the raid's walk to it.</summary>
        private static readonly (string Name, Vector3 Court, Vector3[] Walk)[] Cases =
        [
            // Owner's PC, build 5378dbe: the muster 29617,41288 and its walk by 28081,40298 to the western forerunner.
            ("western forerunner", new(26744, 39884, Z), [new(29617, 41288, Z), new(28081, 40298, Z), new(26744, 39884, Z)]),
            // Owner's PC, build 1dc66b6: a court fate's bearer 430 from his patrol, its muster east of it, off his loop.
            ("fate's bearer", new(28901, 41021, Z), [new(29701, 41021, Z), new(28901, 41021, Z)]),
            // Constructed: a reckoned soul on his western leg (b1b6846: "a reckoned soul on his patrol leg"), from the 5378dbe muster.
            ("reckoned soul on his west leg", new(26741, 41600, Z), [new(29617, 41288, Z), new(26741, 41600, Z)]),
        ];

        /// <summary>His spot and the point he walks to, each second of one loop (the predictor's own walk).</summary>
        private static (Vector3 At, int Next)[] LoopStates()
        {
            Vector3[] loop = T.CryptLordPatrol;
            int seconds = (int)T.CryptLordLoopSeconds();
            var states = new List<(Vector3, int)>();
            Vector3 pos = loop[^1];
            int i = 0;
            for (int s = 0; s < seconds; s++)
            {
                states.Add((pos, i));
                float remaining = T.CryptLordWalkSpeed;
                while (remaining > 0.01f)
                {
                    float d = Vector3.Distance(pos, loop[i]);
                    if (d <= remaining) { pos = loop[i]; remaining -= d; i = (i + 1) % loop.Length; }
                    else { pos += (loop[i] - pos) / d * remaining; remaining = 0; }
                }
            }
            return states.ToArray();
        }

        private static float Length(Vector3[] walk)
        {
            float length = 0;
            for (int k = 1; k < walk.Length; k++) length += Vector3.Distance(walk[k - 1], walk[k]);
            return length;
        }

        private static Vector3 Along(Vector3[] walk, float d)
        {
            for (int k = 1; k < walk.Length; k++)
            {
                float leg = Vector3.Distance(walk[k - 1], walk[k]);
                if (d <= leg) return Vector3.Lerp(walk[k - 1], walk[k], leg < 1e-3f ? 0 : d / leg);
                d -= leg;
            }
            return walk[^1];
        }

        /// <summary>He joins the pull started with him at <paramref name="state"/> (the model above).</summary>
        private static bool Joins((Vector3 At, int Next) state, Vector3 court, Vector3[] walk)
        {
            float walkSeconds = Length(walk) / T.RaidWalkSpeed;
            float end = walkSeconds + T.CourtLapsedRunSeconds;
            Vector3[] path = T.PredictPatrol(state.At, T.CryptLordPatrol, state.Next, T.CryptLordWalkSpeed, end + 1);
            for (int s = 0; s < path.Length && s <= end; s++)
            {
                if (s <= walkSeconds)
                {
                    Vector3 front = Along(walk, s * T.RaidWalkSpeed), back = Along(walk, Math.Max(0, s * T.RaidWalkSpeed - CrowdDepth));
                    if (T.DistanceToWalk(path[s], [back, front]) < T.CryptLordWalkAggro) return true;
                }
                else if (Vector3.Distance(path[s], court) < T.CryptLordCourtLink) return true;
            }
            return false;
        }

        [Test]
        public void ThePullAfterTheLapseBroughtHimInAndTheGuardDoesNot()
        {
            var states = LoopStates();
            Assert.That(states.Length, Is.InRange(130, 132), "his loop: about 131 s at 100");
            foreach (var (name, court, walk) in Cases)
            {
                bool[] joins = states.Select(s => Joins(s, court, walk)).ToArray();
                bool[] strict = states.Select(s => T.CourtPullWait(s.At, s.Next, court, walk) == null).ToArray();
                bool[] guard = states.Select(s => T.CourtLapsedGuard(s.At, s.Next, court, walk) == null).ToArray();
                // Before (the lapse): the pull goes in at whatever point of his loop he is; each second of it equally likely.
                double untimed = joins.Count(j => j) / (double)states.Length;
                // After: the pull waits for the guard; from each second of his loop, the first second it clears.
                int joined = 0;
                long waited = 0;
                for (int s0 = 0; s0 < states.Length; s0++)
                {
                    int s = s0, guardWait = 0;
                    while (!guard[s % states.Length] && guardWait < states.Length) { s++; guardWait++; }
                    Assert.That(guardWait, Is.LessThan(states.Length), $"{name}: the guard opens somewhere on his loop");
                    waited += guardWait;
                    if (joins[s % states.Length]) joined++;
                }
                int strictJoins = Enumerable.Range(0, states.Length).Count(s => strict[s] && joins[s]);
                TestContext.Out.WriteLine($"{name}: walk {(int)Length(walk)}; untimed pull after the lapse: he joins {untimed:P0}; " +
                                          $"guarded: {joined} of {states.Length} ({joined / (double)states.Length:P0}), mean wait " +
                                          $"{waited / (double)states.Length:0} s; guard open {guard.Count(g => g)} s of his loop, " +
                                          $"the strict window {strict.Count(g => g)} s ({strictJoins} joins)");
                Assert.That(untimed, Is.GreaterThan(0.15), $"{name}: the lapse's untimed pull brings him in (b1b6846)");
                Assert.That(joined, Is.EqualTo(0), $"{name}: a guarded pull never brings him in");
                Assert.That(guard.Count(g => g), Is.GreaterThanOrEqualTo(strict.Count(g => g)), $"{name}: the guard is looser than the window");
            }
        }

        [Test]
        public void TheWesternForerunnersUntimedPullIsTheWorst()
        {
            var states = LoopStates();
            var (_, court, walk) = Cases[0];
            double untimed = states.Count(s => Joins(s, court, walk)) / (double)states.Length;
            Assert.That(untimed, Is.GreaterThan(0.5), "its walk crosses his east leg: most untimed pulls bring him in");
            int guard = states.Count(s => T.CourtLapsedGuard(s.At, s.Next, court, walk) == null);
            int strict = states.Count(s => T.CourtPullWait(s.At, s.Next, court, walk) == null);
            Assert.That(guard, Is.GreaterThan(strict), $"the guard ({guard} s) opens more of his loop than the strict window ({strict} s)");
        }

        [Test]
        public void TheGuardNamesWhatHolds()
        {
            Vector3 corner = new(28461, 40166, Z);
            var (_, court, walk) = Cases[0];
            Assert.That(T.CourtLapsedGuard(corner, 1, court, walk), Does.StartWith("lapsed-guard:").And.Contain("walk"));
            Assert.That(T.CourtLapsedGuard(new(26741, 40500, Z), 3, court, null), Does.Contain("within"), "beside it, no walk");
            Assert.That(T.CourtLapsedClearance, Is.EqualTo(T.CryptLordCourtLink + T.CourtWalkMargin));
            Assert.That(T.CourtWalkSeconds(walk), Is.EqualTo(Length(walk) / T.RaidWalkSpeed).Within(0.01));
            Assert.That(T.CourtWalkSeconds(null), Is.EqualTo(0));
        }
    }
}
